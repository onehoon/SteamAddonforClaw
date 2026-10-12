using System.ComponentModel;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Touchpad;

internal sealed class TouchpadRightCaptureWindow : ITouchpadCaptureWindow
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExNoRedirectionBitmap = 0x00200000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint RequiredExtendedStyle = WsExNoActivate | WsExToolWindow | WsExNoRedirectionBitmap | WsExTopmost;
    private const int GwlExStyle = -20;
    private const int HtClient = 1;
    private const int MaNoActivate = 3;
    private const int PaNoActivate = 3;
    private const uint MonitorInfoPrimary = 1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const int SwHide = 0;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmPointerUpdate = 0x0245;
    private const uint WmPointerDown = 0x0246;
    private const uint WmPointerUp = 0x0247;
    private const uint WmPointerEnter = 0x0249;
    private const uint WmPointerLeave = 0x024A;
    private const uint WmPointerActivate = 0x024B;
    private const uint WmPointerCaptureChanged = 0x024C;
    private const uint PointerFlagCanceled = 0x00008000;
    private const int PtTouch = 2;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly WindowProcedure WindowProcedureThunk = WindowProcedureImpl;

    [ThreadStatic]
    private static TouchpadRightCaptureWindow? _threadWindow;

    private readonly TouchpadPointerContactTracker _contacts;
    private readonly object _regionSync = new();
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private nint _hwnd;
    private TouchpadPixelRect _region;
    private int _visible;
    private int _closeRequested;

    internal TouchpadRightCaptureWindow(TouchpadContactHandoff handoff)
    {
        _contacts = new TouchpadPointerContactTracker(handoff);
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Runtime touchpad capture window",
        };
    }

    public bool IsVisible => Volatile.Read(ref _visible) != 0;
    public TouchpadPixelRect Region { get { lock (_regionSync) return _region; } }
    public uint Dpi => _hwnd == 0 ? 0 : GetDpiForWindow(_hwnd);

    public void RequestClose()
    {
        Volatile.Write(ref _visible, 0);
        Interlocked.Exchange(ref _closeRequested, 1);
        var hwnd = Volatile.Read(ref _hwnd);
        if (hwnd != 0)
        {
            _ = ShowWindowAsync(hwnd, SwHide);
            if (!PostMessage(hwnd, WmClose, 0, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                if (IsWindow(hwnd))
                    AppLog.Warn("Touchpad", "Could not post the capture-window close request.", new Win32Exception(error),
                        ("Event", "TouchpadCaptureUnavailable"), ("Win32Error", error));
            }
        }
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _started.TrySetResult(false);
            _stopped.TrySetResult();
            return false;
        }

        try
        {
            _thread.Start();
            return await _started.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_thread.IsAlive)
                await StopAsync().ConfigureAwait(false);
            else
                _stopped.TrySetResult();
            throw;
        }
    }

    public async Task<bool> StopAsync()
    {
        // Also mark a close before CreateWindowEx publishes _hwnd. The owner thread checks this
        // flag around creation/show so cancellation cannot leave a late, visible HWND behind.
        RequestClose();

        try
        {
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException exception)
        {
            AppLog.Error("Touchpad", "Capture-window owner thread did not stop within the bounded shutdown interval.", exception,
                ("Event", "TouchpadCaptureUnavailable"));
            return false;
        }
    }

    private void Pump()
    {
        _threadWindow = this;
        var className = $"SteamAddonTouchpad_{Environment.ProcessId}_{Environment.CurrentManagedThreadId}_{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);
        ushort classAtom = 0;
        try
        {
            if (Volatile.Read(ref _closeRequested) != 0)
                return;

            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureThunk),
                Instance = instance,
                ClassName = Marshal.StringToHGlobalUni(className),
            };
            try
            {
                classAtom = RegisterClassEx(ref windowClass);
            }
            finally
            {
                Marshal.FreeHGlobal(windowClass.ClassName);
            }

            if (classAtom == 0)
                throw LastWin32Exception("RegisterClassExW failed.");

            if (!TryGetPrimaryBounds(out var primaryBounds, out var geometryError))
                throw new InvalidOperationException(geometryError);
            SetRegion(TouchpadPixelRect.BottomRightQuarter(primaryBounds));
            _hwnd = CreateWindowEx(
                RequiredExtendedStyle,
                className,
                string.Empty,
                WsPopup,
                _region.X,
                _region.Y,
                _region.Width,
                _region.Height,
                0,
                0,
                instance,
                0);
            if (_hwnd == 0)
                throw LastWin32Exception("CreateWindowExW failed.");

            if (Volatile.Read(ref _closeRequested) != 0)
            {
                _ = DestroyWindow(_hwnd);
                _hwnd = 0;
                return;
            }

            if (!SetWindowPos(_hwnd, HwndTopmost, _region.X, _region.Y, _region.Width, _region.Height,
                    SwpNoActivate | SwpShowWindow))
                throw LastWin32Exception("SetWindowPos(show) failed.");

            VerifyWindowGeometryAndStyle(_hwnd, _region);
            if (Volatile.Read(ref _closeRequested) != 0)
            {
                _ = ShowWindowAsync(_hwnd, SwHide);
                _ = DestroyWindow(_hwnd);
                _hwnd = 0;
                return;
            }
            Volatile.Write(ref _visible, 1);
            if (Volatile.Read(ref _closeRequested) != 0)
            {
                Volatile.Write(ref _visible, 0);
                _ = ShowWindowAsync(_hwnd, SwHide);
                _ = DestroyWindow(_hwnd);
                _hwnd = 0;
                return;
            }
            _started.TrySetResult(true);

            while (true)
            {
                var messageResult = GetMessage(out var message, 0, 0, 0);
                if (messageResult == 0)
                    break;
                if (messageResult < 0)
                    throw LastWin32Exception("GetMessageW failed.");
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _visible, 0);
            _contacts.Clear();
            var error = exception is Win32Exception win32 ? win32.NativeErrorCode : Marshal.GetLastPInvokeError();
            AppLog.Warn("Touchpad", "Runtime capture window could not be armed; controller presentation remains unchanged.", exception,
                ("Event", "TouchpadCaptureUnavailable"), ("Win32Error", error));
            _started.TrySetResult(false);
        }
        finally
        {
            Volatile.Write(ref _visible, 0);
            _contacts.Clear();
            if (_hwnd != 0)
            {
                _ = DestroyWindow(_hwnd);
                _hwnd = 0;
            }
            if (classAtom != 0)
                _ = UnregisterClass(className, instance);
            _threadWindow = null;
            _started.TrySetResult(false);
            _stopped.TrySetResult();
        }
    }

    private nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WmNcHitTest:
                return HtClient;
            case WmMouseActivate:
                return MaNoActivate;
            case WmPointerActivate:
                return PaNoActivate;
            case WmEraseBkgnd:
                return 1;
            case WmPointerEnter:
            case WmPointerLeave:
            case WmPointerDown:
            case WmPointerUpdate:
            case WmPointerUp:
            case WmPointerCaptureChanged:
                return HandlePointerMessage(hwnd, message, wParam, lParam);
            case WmDisplayChange:
            case WmDpiChanged:
                RepositionForDisplayChange(hwnd);
                return 0;
            case WmClose:
                _contacts.Clear();
                Volatile.Write(ref _visible, 0);
                _ = DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private nint HandlePointerMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (!IsVisible)
            return DefWindowProc(hwnd, message, wParam, lParam);

        var pointerId = (uint)(wParam & 0xFFFF);
        var tracked = _contacts.Owns(pointerId);
        var pointerType = PtTouch;
        if (!tracked && !GetPointerType(pointerId, out pointerType))
            return 0; // Fail closed; never promote an unclassified pointer to ordinary input.
        else if (!tracked && pointerType != PtTouch)
            return DefWindowProc(hwnd, message, wParam, lParam);

        if (message is WmPointerEnter or WmPointerLeave)
            return 0;

        if (!GetPointerInfo(pointerId, out var info))
        {
            if (tracked)
                ClearTrackedPointer(pointerId, canceled: true);
            return 0;
        }
        if (info.PointerType != PtTouch)
        {
            if (tracked)
                ClearTrackedPointer(pointerId, canceled: true);
            return tracked ? 0 : DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == WmPointerCaptureChanged)
        {
            if (tracked)
                ClearTrackedPointer(pointerId, canceled: true);
            return 0;
        }

        if (message == WmPointerDown)
        {
            var x = info.PixelLocationRaw.X;
            var y = info.PixelLocationRaw.Y;
            _contacts.Down(pointerId, _region, x, y);
            return 0;
        }

        if (!tracked)
            return 0;

        if (message == WmPointerUpdate)
        {
            if ((info.PointerFlags & PointerFlagCanceled) != 0)
            {
                ClearTrackedPointer(pointerId, canceled: true);
                return 0;
            }

            _contacts.Move(pointerId, _region, info.PixelLocationRaw.X, info.PixelLocationRaw.Y);
            return 0;
        }

        if (message == WmPointerUp)
            _contacts.End(pointerId, canceled: (info.PointerFlags & PointerFlagCanceled) != 0);
        return 0;
    }

    private void ClearTrackedPointer(uint pointerId, bool canceled = false) => _contacts.End(pointerId, canceled);

    private void RepositionForDisplayChange(nint hwnd)
    {
        Volatile.Write(ref _visible, 0);
        _contacts.Clear();
        if (Volatile.Read(ref _closeRequested) != 0)
        {
            _ = SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpHideWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
            _ = DestroyWindow(hwnd);
            return;
        }

        if (!SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpHideWindow | SwpNoActivate | SwpNoMove | SwpNoSize))
        {
            CloseAfterGeometryFailure(hwnd, "Could not hide the capture window during a display geometry change.");
            return;
        }

        if (!TryGetPrimaryBounds(out var bounds, out var error))
        {
            CloseAfterGeometryFailure(hwnd, error);
            return;
        }

        var next = TouchpadPixelRect.BottomRightQuarter(bounds);
        try
        {
            VerifyWindowGeometryAndStyle(hwnd, _region, checkRect: false);
            if (!SetWindowPos(hwnd, HwndTopmost, next.X, next.Y, next.Width, next.Height, SwpNoActivate | SwpShowWindow | SwpFrameChanged))
                throw LastWin32Exception("SetWindowPos after display geometry change failed.");
            VerifyWindowGeometryAndStyle(hwnd, next);
            SetRegion(next);
            Volatile.Write(ref _visible, 1);
            AppLog.Info("Touchpad", "Capture geometry updated after a display change; contact was reset.",
                ("Event", "TouchpadArmed"), ("Presentation", "SteamDeck"), ("Region", FormatRect(next)),
                ("Dpi", Dpi), ("WindowFlags", "WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_NOREDIRECTIONBITMAP|WS_EX_TOPMOST"));
        }
        catch (Exception exception)
        {
            CloseAfterGeometryFailure(hwnd, exception.Message);
        }
    }

    private void CloseAfterGeometryFailure(nint hwnd, string reason)
    {
        _contacts.Clear();
        Volatile.Write(ref _visible, 0);
        AppLog.Warn("Touchpad", "Capture window hidden because display geometry could not be verified.", null,
            ("Event", "TouchpadCaptureUnavailable"), ("Reason", reason));
        _ = DestroyWindow(hwnd);
    }

    private void SetRegion(TouchpadPixelRect region)
    {
        lock (_regionSync)
            _region = region;
    }

    private static bool TryGetPrimaryBounds(out TouchpadPixelRect bounds, out string error)
    {
        NativeRect? primary = null;
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info) && (info.Flags & MonitorInfoPrimary) != 0)
                primary = info.Monitor;
            return true;
        };
        _ = EnumDisplayMonitors(0, 0, callback, 0);
        if (primary is { } rect && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            bounds = new TouchpadPixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            error = string.Empty;
            return true;
        }

        bounds = default;
        error = "The primary display bounds could not be verified.";
        return false;
    }

    private static void VerifyWindowGeometryAndStyle(nint hwnd, TouchpadPixelRect expected, bool checkRect = true)
    {
        var style = unchecked((uint)GetWindowLongPtr(hwnd, GwlExStyle).ToInt64());
        if ((style & RequiredExtendedStyle) != RequiredExtendedStyle)
            throw new InvalidOperationException($"Capture window styles mismatch: 0x{style:X8}.");
        if (!checkRect)
            return;
        if (!GetWindowRect(hwnd, out var actual))
            throw LastWin32Exception("GetWindowRect failed.");
        if (actual.Left != expected.X || actual.Top != expected.Y
            || actual.Right - actual.Left != expected.Width || actual.Bottom - actual.Top != expected.Height)
            throw new InvalidOperationException($"Capture window bounds mismatch: actual={FormatRect(actual)}, expected={FormatRect(expected)}.");
    }

    private static string FormatRect(TouchpadPixelRect rect) => $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";
    private static string FormatRect(NativeRect rect) => $"{rect.Left},{rect.Top},{rect.Right - rect.Left},{rect.Bottom - rect.Top}";

    private static Win32Exception LastWin32Exception(string message) => new(Marshal.GetLastPInvokeError(), message);

    private static nint WindowProcedureImpl(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        var owner = _threadWindow;
        return owner is null ? DefWindowProc(hwnd, message, wParam, lParam) : owner.HandleMessage(hwnd, message, wParam, lParam);
    }

    public async ValueTask DisposeAsync() => _ = await StopAsync().ConfigureAwait(false);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public int PointerType; public uint PointerId; public uint FrameId; public uint PointerFlags;
        public nint SourceDevice; public nint HwndTarget; public NativePoint PixelLocation;
        public NativePoint HimetricLocation; public NativePoint PixelLocationRaw; public NativePoint HimetricLocationRaw;
        public uint Time; public uint HistoryCount; public int InputData; public uint KeyStates;
        public ulong PerformanceCount; public uint ButtonChangeType;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size; public uint Style; public nint WindowProcedure; public int ClassExtra; public int WindowExtra;
        public nint Instance; public nint Icon; public nint Cursor; public nint BackgroundBrush; public nint MenuName;
        public nint ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time;
        public NativePoint Point; public uint Private;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, nint instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetPointerType", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerType(uint pointerId, out int pointerType);
    [DllImport("user32.dll", EntryPoint = "GetPointerInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerInfo(uint pointerId, out PointerInfo pointerInfo);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint hwnd, uint minimum, uint maximum);
    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "ShowWindowAsync", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static extern void PostQuitMessage(int exitCode);
}
