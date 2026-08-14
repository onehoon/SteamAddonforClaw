using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.Steam;

internal sealed record SteamBigPictureProbeResult(bool IsActive, bool IsReliable, string Reason);
internal sealed record WindowTextReadResult(bool Succeeded, string Value);
internal sealed record WindowCandidateResult(bool IsCandidate, bool IsReliable);

internal interface ISteamBigPictureWindowProbe
{
    SteamBigPictureProbeResult Capture();
}

internal sealed class SteamBigPictureWindowProbe : ISteamBigPictureWindowProbe
{
    private const string ProcessName = "steamwebhelper";
    private const string WindowClass = "SDL_app";
    private const string TitlePrefix = "Steam Big Picture";
    private readonly Func<Func<IntPtr, bool>, bool> _enumerateWindows;
    private readonly Func<IntPtr, WindowCandidateResult> _candidateReader;
    private readonly Func<IntPtr, WindowTextReadResult> _classNameReader;
    private readonly Func<IntPtr, WindowTextReadResult> _titleReader;

    internal SteamBigPictureWindowProbe(
        Func<Func<IntPtr, bool>, bool>? enumerateWindows = null,
        Func<IntPtr, WindowCandidateResult>? candidateReader = null,
        Func<IntPtr, WindowTextReadResult>? classNameReader = null,
        Func<IntPtr, WindowTextReadResult>? titleReader = null)
    {
        _enumerateWindows = enumerateWindows ?? (callback => EnumWindows((window, _) => callback(window), IntPtr.Zero));
        _candidateReader = candidateReader ?? IsSteamWebHelperWindow;
        _classNameReader = classNameReader ?? ReadClassName;
        _titleReader = titleReader ?? ReadWindowTitle;
    }

    public SteamBigPictureProbeResult Capture()
    {
        try
        {
            var found = false;
            var reliable = true;
            var enumerationSucceeded = _enumerateWindows(window =>
            {
                if (!IsBigPictureWindow(window, out var windowReliable))
                {
                    reliable &= windowReliable;
                    return true;
                }

                found = true;
                return true;
            });
            reliable &= enumerationSucceeded;
            if (!enumerationSucceeded) return new(false, false, "WindowEnumerationFailed");
            return new(found, reliable, found ? "Active" : "Inactive");
        }
        catch (Exception exception)
        {
            return new(false, false, exception.GetType().Name);
        }
    }

    private bool IsBigPictureWindow(IntPtr window, out bool reliable)
    {
        reliable = true;
        var candidate = _candidateReader(window);
        if (!candidate.IsReliable) { reliable = false; return false; }
        if (!candidate.IsCandidate) return false;

        var classNameResult = _classNameReader(window);
        if (!classNameResult.Succeeded) { reliable = false; return false; }
        var titleResult = _titleReader(window);
        if (!titleResult.Succeeded) { reliable = false; return false; }
        var className = classNameResult.Value;
        var title = titleResult.Value;
        if (!string.Equals(className, WindowClass, StringComparison.Ordinal)) return false;
        if (!title.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static WindowCandidateResult IsSteamWebHelperWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return new(false, false);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new(string.Equals(process.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase), true);
        }
        catch
        {
            return new(false, false);
        }
    }

    private static WindowTextReadResult ReadClassName(IntPtr window)
    {
        var buffer = new StringBuilder(512);
        var length = GetClassName(window, buffer, buffer.Capacity);
        return new(length != 0, buffer.ToString());
    }

    private static WindowTextReadResult ReadWindowTitle(IntPtr window)
    {
        var buffer = new StringBuilder(512);
        SetLastError(0);
        var length = GetWindowText(window, buffer, buffer.Capacity);
        var error = Marshal.GetLastWin32Error();
        return new(length != 0 || error == 0, buffer.ToString());
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern void SetLastError(uint errorCode);
}

internal interface ISteamBigPictureEventHook : IDisposable
{
    bool Start(Action callback);
}

internal sealed class WindowsSteamBigPictureEventHook : ISteamBigPictureEventHook
{
    private static readonly uint[] Events = [0x8000, 0x8001, 0x8002, 0x8003, 0x800C];
    private readonly List<IntPtr> _hooks = [];
    private WinEventDelegate? _callback;
    private Action? _refresh;

    public bool Start(Action callback)
    {
        _refresh = callback;
        _callback = OnWinEvent;
        foreach (var eventId in Events)
        {
            var hook = SetWinEventHook(eventId, eventId, IntPtr.Zero, _callback, 0, 0, 0);
            if (hook == IntPtr.Zero)
            {
                Dispose();
                return false;
            }
            _hooks.Add(hook);
        }
        return true;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time)
    {
        const int objectIdWindow = 0;
        const int childIdSelf = 0;
        if (objectId == objectIdWindow && childId == childIdSelf && window != IntPtr.Zero) _refresh?.Invoke();
    }

    public void Dispose()
    {
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
        _callback = null;
        _refresh = null;
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minEvent, uint maxEvent, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}

internal sealed class SteamBigPictureWatcher : IDisposable
{
    private readonly ISteamBigPictureWindowProbe _probe;
    private readonly ISteamBigPictureEventHook _eventHook;
    private readonly object _sync = new();
    private bool _started;
    private bool _disposed;
    private bool _isActive;

    internal SteamBigPictureWatcher(ISteamBigPictureWindowProbe? probe = null, ISteamBigPictureEventHook? eventHook = null)
    {
        _probe = probe ?? new SteamBigPictureWindowProbe();
        _eventHook = eventHook ?? new WindowsSteamBigPictureEventHook();
    }
    public event EventHandler? StateChanged;
    public bool IsActive { get { lock (_sync) return _isActive; } }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            if (!_eventHook.Start(RefreshCore)) return;
            _started = true;
        }
        RefreshCore();
    }

    public void Refresh() => RefreshCore();

    private void RefreshCore()
    {
        var result = _probe.Capture();
        bool changed;
        lock (_sync)
        {
            if (_disposed || !_started) return;
            var nextActive = result.IsReliable && result.IsActive;
            if (_isActive == nextActive) return;
            _isActive = nextActive;
            changed = true;
        }
        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            _eventHook.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
