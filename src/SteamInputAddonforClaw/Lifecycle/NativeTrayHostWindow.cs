using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class NativeTrayHostWindow : IDisposable
{
    private const int CS_HREDRAW = 0x0002;
    private const int CS_VREDRAW = 0x0001;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private readonly WndProc _windowProc;
    private readonly string _className;
    private readonly IntPtr _instance;
    private readonly ushort _classAtom;
    private IntPtr _handle;
    private int _disposed;

    internal IntPtr Handle => _handle;

    internal NativeTrayHostWindow()
    {
        _windowProc = WindowProcedure;
        _className = $"SteamInputAddonforClaw.TrayHost.{Guid.NewGuid():N}";
        _instance = GetModuleHandleW(null);
        if (_instance == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not get the current module handle for the tray host window.");
        }

        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc = _windowProc,
            hInstance = _instance,
            lpszClassName = _className
        };
        _classAtom = RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the tray host window class.");
        }

        _handle = CreateWindowExW(
            WS_EX_NOACTIVATE,
            _className,
            null,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);
        if (_handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            UnregisterClassW(_className, _instance);
            throw new Win32Exception(error, "Could not create the tray host window.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            UnregisterClassW(_className, _instance);
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        DefWindowProcW(window, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        internal uint cbSize;
        internal uint style;
        internal WndProc lpfnWndProc;
        internal int cbClsExtra;
        internal int cbWndExtra;
        internal IntPtr hInstance;
        internal IntPtr hIcon;
        internal IntPtr hCursor;
        internal IntPtr hbrBackground;
        internal string? lpszMenuName;
        internal string lpszClassName;
        internal IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
