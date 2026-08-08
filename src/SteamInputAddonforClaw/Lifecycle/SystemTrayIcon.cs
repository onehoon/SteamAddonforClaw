using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Lifecycle;

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
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_STRING = 0;
    private const uint MF_SEPARATOR = 0x0800;
    private readonly IntPtr _windowHandle;
    private readonly Action _open;
    private readonly Action _exit;
    private readonly uint _taskbarCreatedMessage;
    private readonly SubclassProc _subclassProc;
    private readonly IntPtr _icon;

    public bool IsAvailable { get; private set; }

    public SystemTrayIcon(IntPtr windowHandle, Action open, Action exit)
    {
        AppLog.Info("Tray", "Tray initialization started.", ("HWND", $"0x{windowHandle:X}"));
        _windowHandle = windowHandle;
        _open = open;
        _exit = exit;
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
            var notification = (uint)((ulong)lParam.ToInt64() & 0xffff);
            if (notification == WM_LBUTTONDBLCLK)
            {
                AppLog.Info("Tray", "Tray left double click.");
                _open();
            }
            else if (notification == WM_RBUTTONUP || notification == WM_CONTEXTMENU)
            {
                AppLog.Info("Tray", "Tray context menu requested.");
                ShowMenu();
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, 1, "Open");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, 2, "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD, point.X, point.Y, _windowHandle, IntPtr.Zero);
            if (command == 1) _open();
            if (command == 2) _exit();
            PostMessageW(_windowHandle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
    }

    private NOTIFYICONDATA CreateNotifyIconData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _windowHandle, uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_APP + 1,
        hIcon = _icon, szTip = "Steam Input Addon for Claw"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA { public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; public uint dwState; public uint dwStateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo; public uint uTimeoutOrVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle; public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon; public uint uVersion { set => uTimeoutOrVersion = value; } }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
