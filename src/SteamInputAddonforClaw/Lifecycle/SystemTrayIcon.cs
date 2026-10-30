using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Lifecycle;

internal enum TrayNotificationAction
{
    None,
    Activate,
    ContextMenu
}

internal sealed class SystemTrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint WM_APP = 0x8000;
    private const uint WM_USER = 0x0400;
    private const uint NIN_SELECT = WM_USER + 0;
    private const uint NIN_KEYSELECT = WM_USER + 1;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_NULL = 0;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_STRING = 0;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint OpenCommand = 1;
    private const uint RestartCommand = 2;
    private const uint RegistrationRetryIntervalMs = 2000;
    private const int RegistrationRetryMaxAttempts = 15;
    private static readonly UIntPtr RegistrationRetryTimerId = (UIntPtr)0x51A7;
    private readonly IntPtr _windowHandle;
    private readonly Action _open;
    private readonly Action _restart;
    private readonly Func<UserTerminationDecision> _restartDecision;
    private readonly uint _taskbarCreatedMessage;
    private readonly SubclassProc _subclassProc;
    private readonly IntPtr _icon;
    private readonly Func<uint, bool> _notifyIcon;
    private readonly Func<bool> _startRetryTimer;
    private readonly Action _stopRetryTimer;
    private readonly Func<bool> _installSubclass;
    private readonly Action _removeSubclass;
    private readonly Action<IntPtr> _destroyIcon;
    private bool _subclassInstalled;
    private bool _iconRegistered;
    private bool _retryTimerActive;
    private int _retryAttempts;
    private int _disposed;

    public bool IsAvailable { get; private set; }

    public SystemTrayIcon(IntPtr windowHandle, Action open, Action restart, Func<UserTerminationDecision> restartDecision)
        : this(windowHandle, open, restart, restartDecision, null)
    {
    }

    internal SystemTrayIcon(
        IntPtr windowHandle,
        Action open,
        Action restart,
        Func<UserTerminationDecision> restartDecision,
        SystemTrayIconTestHooks? testHooks)
    {
        AppLog.Info("Tray", "Tray initialization started.", ("HWND", $"0x{windowHandle:X}"));
        _windowHandle = windowHandle;
        _open = open;
        _restart = restart;
        _restartDecision = restartDecision ?? throw new ArgumentNullException(nameof(restartDecision));
        _icon = testHooks?.Icon ?? ExtractIconW(IntPtr.Zero, Environment.ProcessPath!, 0);
        _subclassProc = WindowProcedure;
        _notifyIcon = testHooks?.NotifyIcon ?? NativeNotifyIcon;
        _startRetryTimer = testHooks?.StartRetryTimer ?? StartRegistrationRetryTimer;
        _stopRetryTimer = testHooks?.StopRetryTimer ?? StopRegistrationRetryTimer;
        _installSubclass = testHooks?.InstallSubclass ?? (() => SetWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)1, UIntPtr.Zero));
        _removeSubclass = testHooks?.RemoveSubclass ?? (() => RemoveWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)1));
        _destroyIcon = testHooks?.DestroyIcon ?? (icon => DestroyIcon(icon));
        try
        {
            AppLog.Debug("Tray", "ExtractIconW completed.", ("Success", _icon != IntPtr.Zero));
            _taskbarCreatedMessage = testHooks?.TaskbarCreatedMessage ?? RegisterWindowMessageW("TaskbarCreated");
            if (_taskbarCreatedMessage == 0)
                throw new InvalidOperationException("Could not register the TaskbarCreated message.");
            if (!_installSubclass())
            {
                AppLog.Error("Tray", "SetWindowSubclass failed.", new InvalidOperationException("SetWindowSubclass returned false."), ("Operation", "SetWindowSubclass"));
                throw new InvalidOperationException("Could not subclass the application window.");
            }
            _subclassInstalled = true;
            AppLog.Debug("Tray", "SetWindowSubclass completed.", ("Success", true));

            IsAvailable = TryRegisterIcon();
            if (!IsAvailable)
                StartRetryTimer();
        }
        catch
        {
            if (_subclassInstalled)
            {
                _removeSubclass();
                _subclassInstalled = false;
            }
            if (_icon != IntPtr.Zero)
                _destroyIcon(_icon);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        AppLog.Info("Tray", "Tray disposal started.");
        StopRetryTimer();
        if (_iconRegistered)
        {
            _ = _notifyIcon(NIM_DELETE);
            _iconRegistered = false;
        }
        if (_subclassInstalled)
        {
            _removeSubclass();
            _subclassInstalled = false;
        }
        if (_icon != IntPtr.Zero)
        {
            _destroyIcon(_icon);
        }
        AppLog.Info("Tray", "Tray disposal completed.");
    }

    private bool TryRegisterIcon()
    {
        if (!_iconRegistered)
        {
            if (!_notifyIcon(NIM_ADD))
            {
                AppLog.Debug("Tray", "Tray icon registration is not available yet.",
                    ("Operation", "NIM_ADD"), ("Success", false));
                return false;
            }
            _iconRegistered = true;
            AppLog.Debug("Tray", "Shell_NotifyIcon NIM_ADD completed.", ("Success", true));
        }

        if (_notifyIcon(NIM_SETVERSION))
        {
            AppLog.Debug("Tray", "Shell_NotifyIcon NIM_SETVERSION completed.", ("Version", NOTIFYICON_VERSION_4), ("Success", true));
            return true;
        }

        AppLog.Debug("Tray", "Tray icon version registration is not available yet.",
            ("Operation", "NIM_SETVERSION"), ("Version", NOTIFYICON_VERSION_4), ("Success", false));
        if (_notifyIcon(NIM_DELETE))
            _iconRegistered = false;
        return false;
    }

    private void StartRetryTimer()
    {
        if (_retryTimerActive || Volatile.Read(ref _disposed) != 0) return;
        try
        {
            _retryTimerActive = _startRetryTimer();
            if (!_retryTimerActive)
                AppLog.Warn("Tray", "Tray registration retry timer could not be started.", null,
                    ("Action", "KeepRuntimeAvailable"));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Tray", "Tray registration retry timer failed to start; Runtime remains available.", exception);
        }
    }

    private void StopRetryTimer()
    {
        if (!_retryTimerActive) return;
        _stopRetryTimer();
        _retryTimerActive = false;
    }

    private bool StartRegistrationRetryTimer() =>
        SetTimer(_windowHandle, RegistrationRetryTimerId, RegistrationRetryIntervalMs, IntPtr.Zero) != UIntPtr.Zero;

    private void StopRegistrationRetryTimer() => _ = KillTimer(_windowHandle, RegistrationRetryTimerId);

    private bool NativeNotifyIcon(uint command)
    {
        var data = CreateNotifyIconData();
        if (command == NIM_SETVERSION)
            data.uVersion = NOTIFYICON_VERSION_4;
        return Shell_NotifyIconW(command, ref data);
    }

    private void OnTaskbarCreated()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        AppLog.Warn("Tray", "TaskbarCreated received. Re-registering the tray icon.");
        _iconRegistered = false;
        _retryAttempts = 0;
        IsAvailable = TryRegisterIcon();
        if (IsAvailable)
            StopRetryTimer();
        else
            StartRetryTimer();
    }

    private void OnRegistrationRetryTimer()
    {
        if (Volatile.Read(ref _disposed) != 0 || !_retryTimerActive) return;
        _retryAttempts++;
        var attempt = _retryAttempts;
        IsAvailable = TryRegisterIcon();
        if (IsAvailable)
        {
            _retryAttempts = 0;
            StopRetryTimer();
            AppLog.Info("Tray", "Tray registration recovered.", ("Attempts", attempt));
            return;
        }

        if (_retryAttempts >= RegistrationRetryMaxAttempts)
        {
            StopRetryTimer();
            AppLog.Warn("Tray", "Tray registration retry window ended; Runtime remains available.", null,
                ("Attempts", _retryAttempts), ("Action", "WaitForTaskbarCreated"));
        }
        else
        {
            AppLog.Debug("Tray", "Tray registration retry did not succeed yet.", ("Attempt", _retryAttempts));
        }
    }

    internal void ProcessWindowMessage(uint message, IntPtr wParam = default, IntPtr lParam = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (message == _taskbarCreatedMessage)
            OnTaskbarCreated();
        else if (message == WM_TIMER && wParam == (IntPtr)RegistrationRetryTimerId)
            OnRegistrationRetryTimer();
        else if (message == WM_APP + 1)
        {
            var notification = GetNotificationCode(lParam);
            switch (ClassifyNotification(notification))
            {
                case TrayNotificationAction.Activate:
                    AppLog.Info("Tray", "Tray activation requested.");
                    _open();
                    break;
                case TrayNotificationAction.ContextMenu:
                    AppLog.Info("Tray", "Tray context menu requested.");
                    ShowMenu(DecodeCallbackPoint(wParam));
                    break;
            }
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr referenceData)
    {
        ProcessWindowMessage(message, wParam, lParam);
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>NOTIFYICON_VERSION_4 packs the notification code into the low word of lParam.</summary>
    internal static uint GetNotificationCode(IntPtr lParam) => (uint)((ulong)lParam.ToInt64() & 0xffff);

    /// <summary>
    /// Classifies a v4 callback notification into a single logical action so one physical gesture
    /// (mouse or keyboard) maps to exactly one action, instead of also matching the legacy raw
    /// mouse-message notifications a v4 client no longer needs to handle.
    /// </summary>
    internal static TrayNotificationAction ClassifyNotification(uint notification) => notification switch
    {
        NIN_SELECT or NIN_KEYSELECT => TrayNotificationAction.Activate,
        WM_CONTEXTMENU => TrayNotificationAction.ContextMenu,
        _ => TrayNotificationAction.None
    };

    /// <summary>
    /// NOTIFYICON_VERSION_4 packs the WM_CONTEXTMENU anchor as signed screen coordinates in wParam
    /// (LOWORD = X, HIWORD = Y). Signed decoding matters for monitors positioned left of/above the
    /// primary monitor, where these coordinates are negative.
    /// </summary>
    internal static POINT DecodeCallbackPoint(IntPtr wParam)
    {
        var packed = (ulong)wParam.ToInt64();
        var x = unchecked((short)(packed & 0xffff));
        var y = unchecked((short)((packed >> 16) & 0xffff));
        return new POINT { X = x, Y = y };
    }

    private void ShowMenu(POINT point)
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, OpenCommand, "Open");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            var restart = _restartDecision();
            var restartFlags = TerminationMenuFlags(restart.CanTerminate);
            AppendMenuW(menu, restartFlags, RestartCommand, "Restart Addon");
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD, point.X, point.Y, _windowHandle, IntPtr.Zero);
            if (command == OpenCommand)
            {
                AppLog.Info("Tray", "Open command selected.");
                _open();
            }
            if (command == RestartCommand)
            {
                AppLog.Info("Tray", "Restart command selected.");
                _restart();
            }
            PostMessageW(_windowHandle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
    }

    internal static uint TerminationMenuFlags(bool canTerminate) => canTerminate ? MF_STRING : MF_STRING | MF_GRAYED;

    private NOTIFYICONDATA CreateNotifyIconData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _windowHandle, uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_APP + 1,
        hIcon = _icon, szTip = "Steam Addon for Claw"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA { public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; public uint dwState; public uint dwStateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo; public uint uTimeoutOrVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle; public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon; public uint uVersion { set => uTimeoutOrVersion = value; } }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);
    [DllImport("user32.dll", SetLastError = true)] private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint intervalMilliseconds, IntPtr timerProcedure);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool KillTimer(IntPtr window, UIntPtr id);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIconW(IntPtr instance, string fileName, uint index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string value);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

internal sealed record SystemTrayIconTestHooks(
    Func<uint, bool> NotifyIcon,
    Func<bool> StartRetryTimer,
    Action StopRetryTimer,
    Func<bool> InstallSubclass,
    Action RemoveSubclass,
    IntPtr Icon,
    Action<IntPtr> DestroyIcon,
    uint TaskbarCreatedMessage);
