using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SteamInputAddonforClaw.Overlay.Diagnostics;
using WinRT.Interop;

namespace SteamInputAddonforClaw.Overlay;

internal static class WindowInterop
{
    private const int GwlExStyle = -20;
    private const nint HwndTopmost = -1;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSendChanging = 0x0400;
    private const uint SwpNoZOrder = 0x0004;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmActivate = 0x0006;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmNcDestroy = 0x0082;
    private const nint MaNoActivate = 3;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;
    private static readonly SubclassProc OverlayWindowProc = HandleOverlayWindowMessage;
    private static nint _subclassedHwnd;

    internal static nint GetWindowHandle(OverlayWindow window) => WindowNative.GetWindowHandle(window);

    internal static void Configure(OverlayWindow window, out OverlayRect rect, out uint dpi, out string monitorText)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        EnsureWindowSubclass(hwnd);
        var foreground = GetForegroundWindow();
        var monitor = foreground == IntPtr.Zero
            ? MonitorFromPoint(new POINT(), MonitorDefaultToPrimary)
            : MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not select a target monitor.");
            OverlayLog.Error("Geometry", "Target monitor selection failed.", exception, ("Operation", "MonitorFromWindow"));
            throw exception;
        }

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the target monitor work area.");
            OverlayLog.Error("Geometry", "Target monitor information read failed.", exception, ("Operation", "GetMonitorInfo"));
            throw exception;
        }

        var workWidth = Math.Max(0, info.rcWork.Right - info.rcWork.Left);
        var workHeight = Math.Max(0, info.rcWork.Bottom - info.rcWork.Top);
        var provisionalWidth = Math.Min((int)OverlayWindowGeometry.PocPanelWidthDip, workWidth);
        if (!SetWindowPos(
                hwnd,
                IntPtr.Zero,
                info.rcWork.Left,
                info.rcWork.Top,
                provisionalWidth,
                workHeight,
                SwpNoActivate | SwpNoSendChanging | SwpNoZOrder))
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the Overlay window on the target monitor.");
            OverlayLog.Error("Geometry", "Provisional Overlay placement failed.", exception, ("Operation", "SetWindowPos.Provisional"));
            throw exception;
        }

        dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the target-monitor DPI.");
            OverlayLog.Error("Geometry", "Target-monitor DPI read failed.", exception, ("Operation", "GetDpiForWindow"));
            throw exception;
        }

        rect = OverlayWindowGeometry.Calculate(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right,
            info.rcWork.Bottom,
            dpi);
        monitorText = $"Monitor: {info.rcMonitor.Left},{info.rcMonitor.Top} - {info.rcMonitor.Right},{info.rcMonitor.Bottom}";

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));

        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        if (AppWindow.GetFromWindowId(windowId).Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        if (!SetWindowPos(hwnd, HwndTopmost, rect.X, rect.Y, rect.Width, rect.Height, SwpNoActivate | SwpNoSendChanging))
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the Overlay window.");
            OverlayLog.Error("Geometry", "Final Overlay placement failed.", exception, ("Operation", "SetWindowPos.Final"));
            throw exception;
        }

        OverlayLog.Info("Geometry", "Overlay geometry applied",
            ("OverlayHwnd", hwnd), ("ForegroundHwnd", foreground),
            ("MonitorLeft", info.rcMonitor.Left), ("MonitorTop", info.rcMonitor.Top),
            ("MonitorRight", info.rcMonitor.Right), ("MonitorBottom", info.rcMonitor.Bottom),
            ("WorkLeft", info.rcWork.Left), ("WorkTop", info.rcWork.Top),
            ("WorkRight", info.rcWork.Right), ("WorkBottom", info.rcWork.Bottom),
            ("Dpi", dpi), ("Scale", dpi / 96.0),
            ("PanelWidthDip", OverlayWindowGeometry.PocPanelWidthDip),
            ("PanelWidthPx", rect.Width), ("PanelHeightPx", rect.Height));
    }

    internal static void ShowWithoutActivation(OverlayWindow window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (!SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoSendChanging | 0x0001 | 0x0002 | 0x0040))
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not show the Overlay window.");
            OverlayLog.Error("Window", "Overlay show operation failed.", exception, ("Operation", "SetWindowPos.Show"), ("OverlayHwnd", hwnd));
            throw exception;
        }
    }

    internal static void Hide(OverlayWindow window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoActivate | SwpNoSendChanging | 0x0001 | 0x0002 | 0x0080))
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not hide the Overlay window.");
            OverlayLog.Error("Window", "Overlay hide operation failed.", exception, ("Operation", "SetWindowPos.Hide"), ("OverlayHwnd", hwnd));
            throw exception;
        }
    }

    private static void EnsureWindowSubclass(nint hwnd)
    {
        if (_subclassedHwnd == hwnd) return;
        if (SetWindowSubclass(hwnd, OverlayWindowProc, (UIntPtr)1, UIntPtr.Zero))
        {
            _subclassedHwnd = hwnd;
            OverlayLog.Info("Window", "Overlay message subclass installed.", ("OverlayHwnd", hwnd));
            return;
        }

        var exception = new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the Overlay window message subclass.");
        OverlayLog.Error("Window", "Overlay message subclass installation failed.", exception, ("Operation", "SetWindowSubclass"), ("OverlayHwnd", hwnd));
    }

    private static nint HandleOverlayWindowMessage(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint idSubclass,
        nuint referenceData)
    {
        switch (message)
        {
            case WmMouseActivate:
                OverlayLog.Info("Input", "WM_MOUSEACTIVATE received.", ("OverlayHwnd", hwnd), ("Result", MaNoActivate));
                return MaNoActivate;
            case WmActivate:
                OverlayLog.Info("Window", "WM_ACTIVATE received.", ("OverlayHwnd", hwnd), ("State", (long)wParam & 0xffff));
                break;
            case WmClose:
                OverlayLog.Info("Window", "WM_CLOSE received.", ("OverlayHwnd", hwnd));
                break;
            case WmDestroy:
                OverlayLog.Info("Window", "WM_DESTROY received.", ("OverlayHwnd", hwnd));
                break;
            case WmNcDestroy:
                OverlayLog.Info("Window", "WM_NCDESTROY received.", ("OverlayHwnd", hwnd));
                RemoveWindowSubclass(hwnd, OverlayWindowProc, (UIntPtr)idSubclass);
                if (_subclassedHwnd == hwnd) _subclassedHwnd = IntPtr.Zero;
                break;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint idSubclass, nuint referenceData);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc procedure, UIntPtr idSubclass, UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc procedure, UIntPtr idSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        internal uint cbSize;
        internal RECT rcMonitor;
        internal RECT rcWork;
        internal uint dwFlags;
    }
}
