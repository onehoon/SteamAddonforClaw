using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TouchpadPoc;

internal sealed class TouchCaptureWindow
{
    internal const int DefaultDurationSeconds = 90;
    private const uint SessionDurationMilliseconds = DefaultDurationSeconds * 1_000;
    private const uint SessionTimerId = 1;

    private static readonly NativeMethods.WindowProcedure WindowProcedureThunk = WindowProcedure;
    private static TouchCaptureWindow? s_current;

    private readonly SessionLog _log;
    private readonly PointerContactTracker _contacts = new();
    private readonly PointerEventCounts _counts = new();
    private readonly string _className = $"TouchpadPocWindow-{Environment.ProcessId}";
    private nint _instance;
    private nint _hwnd;
    private nuint _timerId;
    private bool _classRegistered;
    private int _exitCode;
    private string? _closeReason;
    private PixelRect _primaryBounds;
    private PixelRect _region;
    private PixelRect _actualWindowBounds;
    private uint _actualWindowStyle;

    public TouchCaptureWindow(SessionLog log)
    {
        _log = log;
    }

    public int Run()
    {
        try
        {
            EnablePerMonitorV2DpiAwareness();
            _primaryBounds = GetPrimaryMonitorBounds();
            _region = PixelRect.BottomRightQuarter(_primaryBounds);
            RegisterWindowClass();
            CreateAndShowWindow();
            StartSessionTimer();
            WriteStartupEvent();
            WriteConsoleSummary();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                RequestClose("Ctrl+C");
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                RunMessageLoop();
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (Exception exception)
        {
            _exitCode = 1;
            var win32Error = exception is Win32Exception win32Exception
                ? win32Exception.NativeErrorCode
                : exception.InnerException is Win32Exception innerWin32Exception
                    ? innerWin32Exception.NativeErrorCode
                    : (int?)null;
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "Fatal",
                Message = exception.ToString(),
                Win32Error = win32Error
            }, flush: true);
            Console.Error.WriteLine($"TouchpadPoc failed: {exception}");
        }
        finally
        {
            Cleanup();
        }

        return _exitCode;
    }

    private static void EnablePerMonitorV2DpiAwareness()
    {
        if (!NativeMethods.SetProcessDpiAwarenessContext(
                new nint(NativeMethods.DpiAwarenessContextPerMonitorAwareV2)))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, "Could not enable per-monitor-v2 DPI awareness.");
        }
    }

    private static PixelRect GetPrimaryMonitorBounds()
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.NativePoint { X = 0, Y = 0 },
            NativeMethods.MonitorDefaultToPrimary);
        if (monitor == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not find the primary monitor.");

        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not query primary monitor bounds.");

        var bounds = new PixelRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException($"Primary monitor returned empty physical bounds: {bounds}.");

        return bounds;
    }

    private void RegisterWindowClass()
    {
        if (s_current is not null)
            throw new InvalidOperationException("Only one TouchpadPoc window may exist in a process.");

        s_current = this;
        _instance = NativeMethods.GetModuleHandle(null);
        if (_instance == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not get the current module handle.");

        var className = Marshal.StringToHGlobalUni(_className);
        try
        {
            var windowClass = new NativeMethods.WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureThunk),
                Instance = _instance,
                ClassName = className
            };

            if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not register the touch target window class.");

            _classRegistered = true;
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }
    }

    private void CreateAndShowWindow()
    {
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.RequestedExtendedStyle,
            _className,
            string.Empty,
            NativeMethods.WsPopup,
            _region.X,
            _region.Y,
            _region.Width,
            _region.Height,
            0,
            0,
            _instance,
            0);
        if (_hwnd == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the touch target window.");

        if (!NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HwndTopmost,
                _region.X,
                _region.Y,
                _region.Width,
                _region.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not show the topmost touch target window.");
        }

        if (!NativeMethods.GetWindowRect(_hwnd, out var actualRect))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not verify the touch target window rectangle.");

        var actualBounds = new PixelRect(
            actualRect.Left,
            actualRect.Top,
            actualRect.Right - actualRect.Left,
            actualRect.Bottom - actualRect.Top);
        if (actualBounds != _region)
            throw new InvalidOperationException($"Window rectangle mismatch. Requested {_region}, actual {actualBounds}.");
        _actualWindowBounds = actualBounds;

        var actualStyle = GetWindowStyleValue(_hwnd, NativeMethods.GwlexStyle);
        if ((actualStyle & NativeMethods.RequestedExtendedStyle) != NativeMethods.RequestedExtendedStyle)
            throw new InvalidOperationException(
                $"Window extended styles mismatch. Requested 0x{NativeMethods.RequestedExtendedStyle:X8}, actual 0x{actualStyle:X8}.");

        _actualWindowStyle = GetWindowStyleValue(_hwnd, NativeMethods.GwlStyle);
        if ((_actualWindowStyle & NativeMethods.WsPopup) != NativeMethods.WsPopup)
            throw new InvalidOperationException(
                $"Window style mismatch. Requested WS_POPUP (0x{NativeMethods.WsPopup:X8}), actual 0x{_actualWindowStyle:X8}.");
    }

    private void StartSessionTimer()
    {
        _timerId = NativeMethods.SetTimer(_hwnd, SessionTimerId, SessionDurationMilliseconds, 0);
        if (_timerId == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not start the 90-second session timer.");
    }

    private void WriteStartupEvent()
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        var actualExtendedStyle = GetWindowStyleValue(_hwnd, NativeMethods.GwlexStyle);
        _log.Write(new PocLogEvent
        {
            Event = "Startup",
            Phase = "WindowReady",
            ProcessId = Environment.ProcessId,
            ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            OsVersion = Environment.OSVersion.VersionString,
            DpiAwareness = "PER_MONITOR_AWARE_V2",
            DpiAwarenessSet = true,
            WindowDpi = dpi == 0 ? null : dpi,
            WindowDpiScale = dpi == 0 ? null : $"{dpi / 96.0:0.###}x",
            SessionDurationSeconds = DefaultDurationSeconds,
            PrimaryBounds = _primaryBounds,
            RegionRect = _region,
            ActualWindowRect = _actualWindowBounds,
            Hwnd = FormatHandle(_hwnd),
            WindowStyle = $"0x{NativeMethods.WsPopup:X8}",
            ActualWindowStyle = $"0x{_actualWindowStyle:X8}",
            RequestedExtendedStyle = $"0x{NativeMethods.RequestedExtendedStyle:X8}",
            ActualExtendedStyle = $"0x{actualExtendedStyle:X8}",
            Message = dpi == 0 ? "GetDpiForWindow returned 0." : null
        }, flush: true);
    }

    private void WriteConsoleSummary()
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        Console.WriteLine("TouchpadPoc — single bottom-right zone");
        Console.WriteLine(
            $"Primary monitor: X={_primaryBounds.X} Y={_primaryBounds.Y} " +
            $"W={_primaryBounds.Width} H={_primaryBounds.Height} physical pixels; " +
            $"DPI awareness=Per-Monitor-V2; window DPI={(dpi == 0 ? "unavailable" : dpi.ToString())}");
        Console.WriteLine(
            $"Zone: X={_region.X} Y={_region.Y} W={_region.Width} H={_region.Height} " +
            $"({_region.Width * (double)_region.Height / (_primaryBounds.Width * (double)_primaryBounds.Height):P0} of primary screen)");
        Console.WriteLine($"Touch window: created; HWND={FormatHandle(_hwnd)}");
        Console.WriteLine($"Log: {_log.Path}");
        Console.WriteLine($"Exit: Ctrl+C or automatic timeout after {DefaultDurationSeconds} seconds.");
        Console.WriteLine("WARNING: touch and pointer input in this region may be intercepted, including the taskbar/system tray.");
    }

    private void RunMessageLoop()
    {
        while (true)
        {
            var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
            if (result == -1)
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "MessageLoop",
                    Message = "GetMessageW failed.",
                    Win32Error = error
                }, flush: true);
                _exitCode = 1;
                break;
            }

            if (result == 0)
                break;

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmNcHitTest:
                return NativeMethods.HtClient;

            case NativeMethods.WmMouseActivate:
                _log.Write(new PocLogEvent
                {
                    Event = "MouseActivate",
                    Phase = "Input",
                    Hwnd = FormatHandle(hwnd),
                    Reason = $"hitTest={(short)((long)lParam & 0xFFFF)}, mouseMessage=0x{((ulong)lParam >> 16) & 0xFFFF:X4}"
                }, flush: true);
                return NativeMethods.MaNoActivate;

            case NativeMethods.WmPointerActivate:
                _log.Write(new PocLogEvent
                {
                    Event = "PointerActivate",
                    Phase = "Input",
                    PointerId = GetPointerId(wParam),
                    Hwnd = FormatHandle(hwnd),
                    Reason = $"hitTest={(short)(((ulong)wParam >> 16) & 0xFFFF)}"
                }, flush: true);
                return NativeMethods.PaNoActivate;

            case NativeMethods.WmActivate:
                var activation = (ushort)((ulong)wParam & 0xFFFF);
                _log.Write(new PocLogEvent
                {
                    Event = "WindowActivation",
                    Phase = "Window",
                    Hwnd = FormatHandle(hwnd),
                    Reason = $"state={activation}, minimized={(((ulong)wParam >> 16) & 0xFFFF) != 0}, otherWindow={FormatHandle(lParam)}"
                }, flush: true);
                return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);

            case NativeMethods.WmPointerEnter:
            case NativeMethods.WmPointerLeave:
            case NativeMethods.WmPointerDown:
            case NativeMethods.WmPointerUpdate:
            case NativeMethods.WmPointerUp:
            case NativeMethods.WmPointerCaptureChanged:
                return HandlePointerMessage(hwnd, message, wParam, lParam);

            case NativeMethods.WmTimer when wParam == _timerId:
                RequestClose("AutomaticTimeout90Seconds");
                return 0;

            case NativeMethods.WmClose:
                Interlocked.CompareExchange(ref _closeReason, "WM_CLOSE", null);
                if (!NativeMethods.DestroyWindow(hwnd))
                {
                    var error = Marshal.GetLastPInvokeError();
                    _log.Write(new PocLogEvent
                    {
                        Event = "Error",
                        Phase = "WindowClose",
                        Message = "DestroyWindow failed.",
                        Win32Error = error
                    }, flush: true);
                    _exitCode = 1;
                    NativeMethods.PostQuitMessage(1);
                }
                return 0;

            case NativeMethods.WmEraseBkgnd:
                return 1;

            case NativeMethods.WmDestroy:
                OnWindowDestroyed(hwnd);
                return 0;

            default:
                return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private nint HandlePointerMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        var pointerId = GetPointerId(wParam);
        var knownTouchPointer = _contacts.IsKnown(pointerId);
        var pointerType = NativeMethods.PtTouch;
        if (!knownTouchPointer && !NativeMethods.GetPointerType(pointerId, out pointerType))
        {
            var error = Marshal.GetLastPInvokeError();
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "PointerType",
                PointerId = pointerId,
                Message = "GetPointerType failed; pointer message was consumed without default processing.",
                Win32Error = error
            }, flush: true);
            return 0;
        }

        if (!knownTouchPointer)
        {
            if (pointerType != NativeMethods.PtTouch)
                return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message is NativeMethods.WmPointerEnter or NativeMethods.WmPointerLeave)
            return 0;

        var hasInfo = NativeMethods.GetPointerInfo(pointerId, out var pointerInfo);
        if (!hasInfo)
        {
            var error = Marshal.GetLastPInvokeError();
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "PointerInfo",
                PointerId = pointerId,
                PointerType = "PT_TOUCH",
                Message = "GetPointerInfo failed; this touch message was consumed and will not be passed to DefWindowProc.",
                Win32Error = error
            }, flush: true);
        }
        else if (pointerInfo.PointerType != NativeMethods.PtTouch && !knownTouchPointer)
        {
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "PointerInfo",
                PointerId = pointerId,
                PointerType = "PT_TOUCH",
                Message = $"GetPointerInfo returned unexpected pointer type {pointerInfo.PointerType}."
            }, flush: true);
            return 0;
        }
        else if (pointerInfo.PointerType != NativeMethods.PtTouch)
        {
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "PointerInfo",
                PointerId = pointerId,
                PointerType = "PT_TOUCH",
                Message = $"Tracked touch pointer returned unexpected pointer type {pointerInfo.PointerType}; its lifecycle will still be cleared."
            }, flush: true);
        }

        if (message == NativeMethods.WmPointerCaptureChanged)
        {
            _contacts.End(pointerId, out var acquired);
            _counts.AddCaptureLost();
            WritePointerEvent("CaptureLost", pointerId, pointerInfo, hasInfo, acquired, flush: true);
            return 0;
        }

        if (message == NativeMethods.WmPointerDown)
        {
            var inside = hasInfo && _region.Contains(
                pointerInfo.PixelLocationRaw.X,
                pointerInfo.PixelLocationRaw.Y);
            if (!_contacts.Begin(pointerId, inside, out var acquired))
            {
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "PointerTracking",
                    PointerId = pointerId,
                    Message = "A touch DOWN arrived for an already tracked pointer ID."
                }, flush: true);
            }
            _counts.AddDown();
            WritePointerEvent("Down", pointerId, pointerInfo, hasInfo, acquired, flush: true);
            return 0;
        }

        if (message == NativeMethods.WmPointerUpdate)
        {
            if (hasInfo && (pointerInfo.PointerFlags & NativeMethods.PointerFlagCanceled) != 0)
            {
                _contacts.End(pointerId, out var acquired);
                _counts.AddCancel();
                WritePointerEvent("Cancel", pointerId, pointerInfo, hasInfo, acquired, flush: true);
                return 0;
            }

            var tracked = _contacts.IsAcquired(pointerId);
            _counts.AddMove();
            WritePointerEvent("Move", pointerId, pointerInfo, hasInfo, tracked, flush: false);
            return 0;
        }

        if (message == NativeMethods.WmPointerUp)
        {
            _contacts.End(pointerId, out var acquired);
            var canceled = hasInfo && (pointerInfo.PointerFlags & NativeMethods.PointerFlagCanceled) != 0;
            if (canceled)
                _counts.AddCancel();
            else
                _counts.AddUp();
            WritePointerEvent(canceled ? "Cancel" : "Up", pointerId, pointerInfo, hasInfo, acquired, flush: true);
            return 0;
        }

        return 0;
    }

    private void WritePointerEvent(
        string eventName,
        uint pointerId,
        NativeMethods.PointerInfo pointerInfo,
        bool hasInfo,
        bool tracked,
        bool flush)
    {
        int? localX = null;
        int? localY = null;
        int? rawLocalX = null;
        int? rawLocalY = null;
        bool? targetWindowIsThisWindow = null;
        int? screenX = null;
        int? screenY = null;
        int? rawScreenX = null;
        int? rawScreenY = null;
        string? flags = null;
        bool? primary = null;

        if (hasInfo)
        {
            screenX = pointerInfo.PixelLocation.X;
            screenY = pointerInfo.PixelLocation.Y;
            rawScreenX = pointerInfo.PixelLocationRaw.X;
            rawScreenY = pointerInfo.PixelLocationRaw.Y;
            flags = $"0x{pointerInfo.PointerFlags:X8}";
            primary = (pointerInfo.PointerFlags & NativeMethods.PointerFlagPrimary) != 0;
            targetWindowIsThisWindow = pointerInfo.HwndTarget == _hwnd;

            var localPoint = pointerInfo.PixelLocation;
            if (NativeMethods.ScreenToClient(_hwnd, ref localPoint))
            {
                localX = localPoint.X;
                localY = localPoint.Y;
            }
            else
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "ScreenToClient",
                    PointerId = pointerId,
                    PointerType = "PT_TOUCH",
                    Message = "Could not convert the physical screen coordinates to window-client coordinates.",
                    Win32Error = error
                }, flush: true);
            }

            var rawLocalPoint = pointerInfo.PixelLocationRaw;
            if (NativeMethods.ScreenToClient(_hwnd, ref rawLocalPoint))
            {
                rawLocalX = rawLocalPoint.X;
                rawLocalY = rawLocalPoint.Y;
            }
            else
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "ScreenToClientRaw",
                    PointerId = pointerId,
                    PointerType = "PT_TOUCH",
                    Message = "Could not convert raw physical screen coordinates to window-client coordinates.",
                    Win32Error = error
                }, flush: true);
            }
        }

        _log.Write(new PocLogEvent
        {
            Event = eventName,
            Phase = "Touch",
            PointerId = pointerId,
            PointerType = "PT_TOUCH",
            Flags = flags,
            ScreenX = screenX,
            ScreenY = screenY,
            RawScreenX = rawScreenX,
            RawScreenY = rawScreenY,
            LocalX = localX,
            LocalY = localY,
            RawLocalX = rawLocalX,
            RawLocalY = rawLocalY,
            PrimaryPointer = primary,
            TargetWindowIsThisWindow = targetWindowIsThisWindow,
            Tracked = tracked,
            ActivePointers = _contacts.AcquiredCount
        }, flush);
    }

    private void OnWindowDestroyed(nint hwnd)
    {
        if (_timerId != 0)
        {
            if (!NativeMethods.KillTimer(hwnd, _timerId))
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "WindowCleanup",
                    Message = "KillTimer failed while destroying the window.",
                    Win32Error = error
                }, flush: true);
                _exitCode = 1;
            }
            _timerId = 0;
        }

        foreach (var contact in _contacts.Clear())
        {
            _counts.AddCancel();
            _log.Write(new PocLogEvent
            {
                Event = "Cancel",
                Phase = "Shutdown",
                PointerId = contact.PointerId,
                PointerType = "PT_TOUCH",
                Tracked = contact.Acquired,
                ActivePointers = _contacts.AcquiredCount,
                Reason = "WindowDestroyed"
            }, flush: true);
        }

        WriteShutdown(hwnd, _closeReason ?? "WindowDestroyed");
        _hwnd = 0;
        NativeMethods.PostQuitMessage(_exitCode);
    }

    private void RequestClose(string reason)
    {
        Interlocked.CompareExchange(ref _closeReason, reason, null);
        if (_hwnd != 0 && !NativeMethods.PostMessage(_hwnd, NativeMethods.WmClose, 0, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            _log.Write(new PocLogEvent
            {
                Event = "Error",
                Phase = "CloseRequest",
                Message = $"PostMessageW(WM_CLOSE) failed while handling {reason}.",
                Win32Error = error
            }, flush: true);
            Console.Error.WriteLine($"Could not request a clean window close (Win32 error {error}).");
            Interlocked.Exchange(ref _exitCode, 1);
        }
    }

    private void Cleanup()
    {
        if (_hwnd != 0)
        {
            var hwnd = _hwnd;
            _hwnd = 0;
            if (!NativeMethods.DestroyWindow(hwnd))
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "Cleanup",
                    Message = "DestroyWindow failed during cleanup.",
                    Win32Error = error
                }, flush: true);
                _exitCode = 1;
            }
        }

        if (_classRegistered)
        {
            if (!NativeMethods.UnregisterClass(_className, _instance))
            {
                var error = Marshal.GetLastPInvokeError();
                _log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "Cleanup",
                    Message = "UnregisterClassW failed.",
                    Win32Error = error
                }, flush: true);
                _exitCode = 1;
            }
            _classRegistered = false;
        }

        WriteShutdown(_hwnd, _closeReason ?? "ProcessExit");
        s_current = null;
    }

    private void WriteShutdown(nint hwnd, string reason)
    {
        if (_shutdownLogged)
            return;

        _log.Write(new PocLogEvent
        {
            Event = "Shutdown",
            Phase = "ProcessExit",
            Hwnd = hwnd == 0 ? null : FormatHandle(hwnd),
            Reason = reason,
            ActivePointers = 0,
            EventCounts = _counts.Summary
        }, flush: true);
        _shutdownLogged = true;
    }

    private bool _shutdownLogged;

    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        var current = s_current;
        if (current is null)
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);

        try
        {
            return current.HandleMessage(hwnd, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            current._exitCode = 1;
            Console.Error.WriteLine($"TouchpadPoc window procedure failed: {exception}");
            try
            {
                var win32Error = exception is Win32Exception win32Exception
                    ? win32Exception.NativeErrorCode
                    : (int?)null;
                current._log.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "WindowProcedure",
                    Message = exception.ToString(),
                    Win32Error = win32Error
                }, flush: true);
            }
            catch (Exception logException)
            {
                Console.Error.WriteLine($"TouchpadPoc could not write the failure to its log: {logException.Message}");
            }

            if (message == NativeMethods.WmDestroy)
            {
                NativeMethods.PostQuitMessage(1);
            }
            else
            {
                NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, 0, 0);
            }
            return 0;
        }
    }

    private static uint GetPointerId(nuint wParam) =>
        unchecked((uint)((ulong)wParam & 0xFFFF));

    private static uint GetWindowStyleValue(nint hwnd, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var style = NativeMethods.GetWindowLongPtr(hwnd, index);
        var error = Marshal.GetLastPInvokeError();
        if (style == 0 && error != 0)
            throw new Win32Exception(error, $"GetWindowLongPtrW({index}) failed.");
        return unchecked((uint)style);
    }

    private static string FormatHandle(nint value) =>
        $"0x{unchecked((ulong)value):X}";
}
