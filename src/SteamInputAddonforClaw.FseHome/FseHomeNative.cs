using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.FseHome;

internal sealed class HandoffWindow : IDisposable
{
    private const uint ClassStyleHorizontalRedraw = 0x0002;
    private const uint ClassStyleVerticalRedraw = 0x0001;
    private const uint PopupStyle = 0x80000000;
    private const uint ExtendedTopmostStyle = 0x00000008;
    private const int BlackBrush = 4;
    private const int ScreenWidthMetric = 0;
    private const int ScreenHeightMetric = 1;
    private static readonly NativeMethods.WindowProcedure s_windowProcedure = WindowProcedure;

    private readonly string _className;
    private readonly IntPtr _moduleHandle;
    private IntPtr _handle;

    private HandoffWindow(string className, IntPtr moduleHandle, IntPtr handle, int width, int height)
    {
        _className = className;
        _moduleHandle = moduleHandle;
        _handle = handle;
        Bounds = $"x=0,y=0,width={width},height={height}";
    }

    internal IntPtr Handle => _handle;
    internal string Bounds { get; }

    internal static HandoffWindow Create()
    {
        NativeMethods.SetPerMonitorV2DpiAwareness();

        var width = NativeMethods.GetSystemMetrics(ScreenWidthMetric);
        var height = NativeMethods.GetSystemMetrics(ScreenHeightMetric);
        if (width <= 0 || height <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The primary display bounds could not be read.");

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        if (moduleHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The FseHome module handle could not be read.");

        var className = $"SteamInputAddonforClaw.FseHome.Handoff.{Environment.ProcessId}";
        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            Style = ClassStyleHorizontalRedraw | ClassStyleVerticalRedraw,
            WindowProcedure = s_windowProcedure,
            ModuleHandle = moduleHandle,
            BackgroundBrush = NativeMethods.GetStockObject(BlackBrush),
            ClassName = className,
        };
        if (windowClass.BackgroundBrush == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The handoff background brush could not be created.");
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The handoff window class could not be registered.");

        try
        {
            var handle = NativeMethods.CreateWindowEx(
                ExtendedTopmostStyle,
                className,
                "",
                PopupStyle,
                0,
                0,
                width,
                height,
                IntPtr.Zero,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The fullscreen handoff window could not be created.");

            var window = new HandoffWindow(className, moduleHandle, handle, width, height);
            NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowNormal);
            NativeMethods.UpdateWindow(handle);
            if (!NativeMethods.IsWindowVisible(handle))
            {
                window.Dispose();
                throw new InvalidOperationException("The fullscreen handoff window did not become visible.");
            }

            return window;
        }
        catch
        {
            NativeMethods.UnregisterClass(className, moduleHandle);
            throw;
        }
    }

    internal void Hide()
    {
        if (_handle != IntPtr.Zero && NativeMethods.IsWindow(_handle))
            NativeMethods.ShowWindow(_handle, NativeMethods.ShowWindowHide);
    }

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle != IntPtr.Zero && NativeMethods.IsWindow(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowHide);
            NativeMethods.DestroyWindow(handle);
        }

        NativeMethods.UnregisterClass(_className, _moduleHandle);
    }

    private static IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.MessageDestroy)
        {
            NativeMethods.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
    }
}

internal readonly record struct SteamBigPictureWindow(IntPtr Handle, uint ProcessId);

internal static class SteamBigPictureWindowProbe
{
    private const string ExpectedProcessName = "steamwebhelper";
    private const string ExpectedWindowClass = "SDL_app";
    private const string ExpectedTitlePrefix = "Steam Big Picture";
    private const int WindowTextCapacity = 512;

    // This one-shot identity intentionally matches SteamBigPictureWindowProbe in the Runtime.
    internal static bool MatchesIdentity(string processName, string windowClass, string title) =>
        string.Equals(processName, ExpectedProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(windowClass, ExpectedWindowClass, StringComparison.Ordinal) &&
        title.StartsWith(ExpectedTitlePrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesVisibleIdentity(string processName, string windowClass, string title, bool isVisible) =>
        isVisible && MatchesIdentity(processName, windowClass, title);

    internal static bool TryFind(out SteamBigPictureWindow candidate)
    {
        candidate = default;
        var found = default(SteamBigPictureWindow);
        var stoppedAfterMatch = false;
        NativeMethods.EnumWindowsProcedure callback = (handle, _) =>
        {
            if (!TryInspectWindow(handle, out var inspected))
                return true;

            found = inspected;
            stoppedAfterMatch = true;
            return false;
        };

        var enumerationCompleted = NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        if (found.Handle == IntPtr.Zero)
        {
            if (!enumerationCompleted && !stoppedAfterMatch)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Top-level window enumeration failed.");
            return false;
        }

        if (!TryInspectWindow(found.Handle, out var selected) || selected.ProcessId != found.ProcessId)
            return false;

        candidate = selected;
        return true;
    }

    private static bool TryInspectWindow(IntPtr handle, out SteamBigPictureWindow candidate)
    {
        candidate = default;
        if (!NativeMethods.IsWindow(handle))
            return false;

        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (threadId == 0 || processId == 0)
            return false;

        var className = new StringBuilder(WindowTextCapacity);
        if (NativeMethods.GetClassName(handle, className, className.Capacity) == 0)
            return false;
        var title = new StringBuilder(WindowTextCapacity);
        NativeMethods.GetWindowText(handle, title, title.Capacity);

        string processName;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            processName = process.ProcessName;
        }
        catch (Exception)
        {
            return false;
        }

        if (!MatchesVisibleIdentity(
                processName,
                className.ToString(),
                title.ToString(),
                NativeMethods.IsWindowVisible(handle)) ||
            !NativeMethods.IsWindow(handle))
            return false;

        candidate = new(handle, processId);
        return true;
    }
}

internal static class NativeMethods
{
    internal const int ShowWindowNormal = 5;
    internal const int ShowWindowMaximized = 3;
    internal const int ShowWindowHide = 0;
    internal const uint MessageDestroy = 0x0002;

    private const uint DpiAwarenessPerMonitorV2 = unchecked((uint)-4);
    private const uint PeekRemove = 0x0001;
    private const uint QueueAllInput = 0x04FF;
    private const uint WaitInputAvailable = 0x0004;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint MessageQuit = 0x0012;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool EnumWindowsProcedure(IntPtr handle, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal WindowProcedure? WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal IntPtr ModuleHandle;
        internal IntPtr IconHandle;
        internal IntPtr CursorHandle;
        internal IntPtr BackgroundBrush;
        internal string? MenuName;
        internal string? ClassName;
        internal IntPtr SmallIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal IntPtr WindowHandle;
        internal uint Id;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int PointX;
        internal int PointY;
        internal uint Private;
    }

    internal static void SetPerMonitorV2DpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Per-monitor-v2 DPI awareness could not be enabled.");
    }

    internal static bool WaitForMessagesOrTimeout(uint milliseconds)
    {
        var result = MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, milliseconds, QueueAllInput, WaitInputAvailable);
        if (result == WaitFailed)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The handoff message wait failed.");
        if (result != 0 && result != WaitTimeout)
            throw new Win32Exception($"The handoff message wait returned unexpected status 0x{result:X8}.");

        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PeekRemove))
        {
            if (message.Id == MessageQuit)
                return false;

            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics", SetLastError = true)]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("gdi32.dll", EntryPoint = "GetStockObject", SetLastError = true)]
    internal static extern IntPtr GetStockObject(int objectIndex);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, IntPtr moduleHandle);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr moduleHandle,
        IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    internal static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "IsIconic", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProcedure callback, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true)]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, IntPtr handle, uint filterMinimum, uint filterMaximum, uint removeFlags);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjectsEx", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint count,
        IntPtr handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);
}
