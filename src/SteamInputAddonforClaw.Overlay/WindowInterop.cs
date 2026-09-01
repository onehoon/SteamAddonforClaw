using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
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
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;

    internal static void Configure(OverlayWindow window, out OverlayRect rect, out uint dpi, out string monitorText)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var foreground = GetForegroundWindow();
        var monitor = foreground == IntPtr.Zero
            ? MonitorFromPoint(new POINT(), MonitorDefaultToPrimary)
            : MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not select a target monitor.");

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the target monitor work area.");

        dpi = GetDpiForWindow(hwnd);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the Overlay window.");
    }

    internal static void ShowWithoutActivation(OverlayWindow window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (!SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoSendChanging | 0x0001 | 0x0002 | 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not show the Overlay window.");
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
