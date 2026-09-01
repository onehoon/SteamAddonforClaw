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
    private const uint WM_NULL = 0;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_STRING = 0;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_SEPARATOR = 0x0800;
    private readonly IntPtr _windowHandle;
    private readonly Action _open;
    private readonly Action _restart;
    private readonly Action _exit;
    private readonly Action _overlayToggle;
    private readonly Func<UserTerminationDecision> _terminationDecision;
    private readonly uint _taskbarCreatedMessage;
    private readonly SubclassProc _subclassProc;
    private readonly IntPtr _icon;

    public bool IsAvailable { get; private set; }

    public SystemTrayIcon(IntPtr windowHandle, Action open, Action restart, Action exit, Func<UserTerminationDecision> terminationDecision, Action? overlayToggle = null)
    {
        AppLog.Info("Tray", "Tray initialization started.", ("HWND", $"0x{windowHandle:X}"));
        _windowHandle = windowHandle;
        _open = open;
        _restart = restart;
        _exit = exit;
        _overlayToggle = overlayToggle ?? (() => { });
        _terminationDecision = terminationDecision ?? throw new ArgumentNullException(nameof(terminationDecision));
        _icon = ExtractIconW(IntPtr.Zero, Environment.ProcessPath!, 0);
        AppLog.Debug("Tray", "ExtractIconW completed.", ("Success", _icon != IntPtr.Zero));
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        _subclassProc = WindowProcedure;
        if (!SetWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)1, UIntPtr.Zero))
        {
            AppLog.Error("Tray", "SetWindowSubclass failed.", new InvalidOperationException("SetWindowSubclass returned false."), ("Operation", "SetWindowSubclass"));
            throw new InvalidOperationException("Could not subclass the application window.");
        }
        AppLog.Debug("Tray", "SetWindowSubclass completed.", ("Success", true));

        IsAvailable = AddIcon();
        if (!IsAvailable)
        {
            RemoveWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)1);
            throw new InvalidOperationException("Could not add the notification area icon.");
        }
    }

    public void Dispose()
    {
        AppLog.Info("Tray", "Tray disposal started.");
        var data = CreateNotifyIconData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        RemoveWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)1);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }
        AppLog.Info("Tray", "Tray disposal completed.");
    }

    private bool AddIcon()
    {
        var data = CreateNotifyIconData();
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            AppLog.Error("Tray", "Tray icon registration failed.", new InvalidOperationException("Shell_NotifyIcon NIM_ADD returned false."), ("Operation", "NIM_ADD"), ("Success", false));
            return false;
        }
        AppLog.Debug("Tray", "Shell_NotifyIcon NIM_ADD completed.", ("Success", true));
        data.uVersion = NOTIFYICON_VERSION_4;
        if (Shell_NotifyIconW(NIM_SETVERSION, ref data)) { AppLog.Debug("Tray", "Shell_NotifyIcon NIM_SETVERSION completed.", ("Version", NOTIFYICON_VERSION_4), ("Success", true)); return true; }
        AppLog.Error("Tray", "Tray icon version registration failed.", new InvalidOperationException("Shell_NotifyIcon NIM_SETVERSION returned false."), ("Operation", "NIM_SETVERSION"), ("Version", NOTIFYICON_VERSION_4), ("Fallback", "NIM_DELETE"));
        Shell_NotifyIconW(NIM_DELETE, ref data);
        return false;
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr referenceData)
    {
        if (message == _taskbarCreatedMessage)
        {
            AppLog.Warn("Tray", "TaskbarCreated received. Attempting tray re-registration.");
            IsAvailable = AddIcon();
            if (!IsAvailable)
            {
                AppLog.Error("Tray", "Tray re-registration failed.", new InvalidOperationException("Shell_NotifyIcon failed."), ("Action", "ShowMainWindow"));
                _open();
            }
        }
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
            AppendMenuW(menu, MF_STRING, 1, "Open");
            AppendMenuW(menu, MF_STRING, 4, "Overlay POC: Toggle");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            var termination = _terminationDecision();
            var terminationFlags = TerminationMenuFlags(termination.CanTerminate);
            AppendMenuW(menu, terminationFlags, 2, "Restart");
            AppendMenuW(menu, terminationFlags, 3, "Exit");
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD, point.X, point.Y, _windowHandle, IntPtr.Zero);
            if (command == 1)
            {
                AppLog.Info("Tray", "Open command selected.");
                _open();
            }
            if (command == 2)
            {
                AppLog.Info("Tray", "Restart command selected.");
                _restart();
            }
            if (command == 4)
            {
                AppLog.Info("Tray", "Overlay POC toggle selected.");
                _overlayToggle();
            }
            if (command == 3)
            {
                AppLog.Info("Tray", "Exit command selected.");
                _exit();
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
