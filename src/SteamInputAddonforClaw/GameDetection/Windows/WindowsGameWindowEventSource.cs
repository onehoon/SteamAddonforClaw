using System.ComponentModel;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameDetection.Windows;

internal enum GameWindowEventKind
{
    Foreground,
    Create,
    Show,
}

internal sealed record GameWindowObservation(GameWindowEventKind Kind, nint WindowHandle, uint ProcessId);

internal interface IGameWindowEventSource : IAsyncDisposable
{
    Task StartAsync(Action<GameWindowObservation> observation, Action<Exception> failure, CancellationToken cancellationToken);
    Task StopAsync();
    uint GetForegroundProcessId();
    IReadOnlyList<uint> EnumerateTopLevelProcessIds(int maximumProcessCount);
}

internal sealed class WindowsGameWindowEventSource : IGameWindowEventSource
{
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectCreate = 0x8000;
    internal const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint GaRoot = 2;
    private const uint PmNoRemove = 0;
    private const uint WmQuit = 0x0012;

    private readonly WinEventProc _callback;
    private readonly object _sync = new();
    private Thread? _thread;
    private TaskCompletionSource? _started;
    private TaskCompletionSource? _stopped;
    private Action<GameWindowObservation>? _observation;
    private Action<Exception>? _failure;
    private uint _threadId;

    internal WindowsGameWindowEventSource() => _callback = OnWinEvent;

    public async Task StartAsync(
        Action<GameWindowObservation> observation,
        Action<Exception> failure,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WinEvent hooks are available only on Windows.");

        Task started;
        lock (_sync)
        {
            if (_thread is not null)
                throw new InvalidOperationException("Game window hooks are already running.");

            _observation = observation;
            _failure = failure;
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            started = _started.Task;
            _thread = new Thread(PumpMessages)
            {
                IsBackground = true,
                Name = "Windows game-window events",
            };
            _thread.Start();
        }

        try
        {
            await started.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        Task? stopped;
        uint threadId;
        lock (_sync)
        {
            if (_thread is null)
                return;
            stopped = _stopped?.Task;
            threadId = _threadId;
        }

        if (threadId != 0 && !PostThreadMessage(threadId, WmQuit, 0, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1444) // ERROR_INVALID_THREAD_ID: the pump already exited.
                AppLog.Warn("GameDetection.Windows", "Could not post the WinEvent pump quit message.", new Win32Exception(error));
        }

        if (stopped is not null)
            await stopped.ConfigureAwait(false);

        lock (_sync)
        {
            _thread = null;
            _started = null;
            _stopped = null;
            _observation = null;
            _failure = null;
            _threadId = 0;
        }
    }

    public uint GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetAncestor(window, GaRoot) != window)
            return 0;
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    public IReadOnlyList<uint> EnumerateTopLevelProcessIds(int maximumProcessCount)
    {
        if (maximumProcessCount <= 0)
            return [];

        var processIds = new HashSet<uint>();
        EnumWindowsProc callback = (window, parameter) =>
        {
            if (window != 0 && GetAncestor(window, GaRoot) == window)
            {
                GetWindowThreadProcessId(window, out var processId);
                if (processId != 0)
                    processIds.Add(processId);
            }

            return processIds.Count < maximumProcessCount;
        };

        _ = EnumWindows(callback, 0);
        return processIds.Take(maximumProcessCount).ToArray();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal static bool IsRelevantObservation(uint eventType, int objectId, int childId, nint window, bool isTopLevelWindow)
    {
        if (window == 0 || !isTopLevelWindow)
            return false;
        if (eventType == EventSystemForeground)
            return true;
        return eventType is EventObjectCreate or EventObjectShow && objectId == ObjectIdWindow && childId == ChildIdSelf;
    }

    private void PumpMessages()
    {
        var hooks = new List<nint>(3);
        try
        {
            _threadId = GetCurrentThreadId();
            _ = PeekMessage(out _, 0, 0, 0, PmNoRemove); // Create this thread's message queue before publishing readiness.

            var foreground = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, WineventOutOfContext);
            if (foreground == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EVENT_SYSTEM_FOREGROUND hook could not be installed.");
            hooks.Add(foreground);

            var create = SetWinEventHook(EventObjectCreate, EventObjectCreate, 0, _callback, 0, 0, WineventOutOfContext);
            if (create == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EVENT_OBJECT_CREATE hook could not be installed.");
            hooks.Add(create);

            var show = SetWinEventHook(EventObjectShow, EventObjectShow, 0, _callback, 0, 0, WineventOutOfContext);
            if (show == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EVENT_OBJECT_SHOW hook could not be installed.");
            hooks.Add(show);
            _started?.TrySetResult();

            while (true)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0)
                    break;
                if (result == -1)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The WinEvent message loop failed.");
                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            _started?.TrySetException(exception);
            if (_started?.Task.IsCompletedSuccessfully == true)
            {
                try { _failure?.Invoke(exception); }
                catch { }
            }
        }
        finally
        {
            foreach (var hook in hooks)
                _ = UnhookWinEvent(hook);
            _stopped?.TrySetResult();
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            var kind = eventType switch
            {
                EventSystemForeground => GameWindowEventKind.Foreground,
                EventObjectCreate => GameWindowEventKind.Create,
                EventObjectShow => GameWindowEventKind.Show,
                _ => (GameWindowEventKind?)null,
            };
            if (kind is null || window == 0)
                return;
            if (!IsRelevantObservation(eventType, objectId, childId, window, GetAncestor(window, GaRoot) == window))
                return;
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
                return;

            _observation?.Invoke(new GameWindowObservation(kind.Value, window, processId));
        }
        catch
        {
            // No managed exception may cross the unmanaged WinEvent callback boundary.
        }
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime);
    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
