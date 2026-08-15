using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.Steam;

// Kept for ElevatedPrerequisiteSetup's safety-gate probe (SteamBigPictureWindowProbe.Capture), which is
// unrelated to BPM session detection and must keep its existing fail-closed-on-any-unreliable-window
// behavior unchanged.
internal sealed record SteamBigPictureProbeResult(bool IsActive, bool IsReliable, string Reason);
internal sealed record WindowTextReadResult(bool Succeeded, string Value);
internal sealed record WindowCandidateResult(bool IsCandidate, bool IsReliable, uint ProcessId);

/// <summary>Result of inspecting a single HWND against the BPM candidate identity (process/class/title).</summary>
internal readonly record struct BigPictureCandidateInspection(bool IsCandidate, bool IsReliable, uint ProcessId);

/// <summary>
/// Result of re-validating an already-authorized tracked HWND's lifetime/ownership. Deliberately lighter
/// than <see cref="BigPictureCandidateInspection"/>: title/class are discovery-time evidence only, so an
/// already-tracked window must not be required to still match them (a NAMECHANGE during Steam's startup
/// churn must not un-authorize it).
/// </summary>
internal readonly record struct BigPictureTrackedWindowInspection(bool StillValid, bool IsReliable);

/// <summary>
/// Result of a one-shot EnumWindows scan for a BPM candidate. Used only for startup snapshot, end-of-protection
/// reconciliation, and tracked-window replacement search -- never on a recurring/periodic basis.
/// </summary>
internal readonly record struct BigPictureScanResult(bool Found, IntPtr Hwnd, uint ProcessId, bool EnumerationSucceeded);

internal readonly record struct BigPictureWinEvent(uint EventType, IntPtr Hwnd, int ObjectId, int ChildId);

internal static class BigPictureWinEventType
{
    internal const uint Create = 0x8000;
    internal const uint Destroy = 0x8001;
    internal const uint Show = 0x8002;
    internal const uint Hide = 0x8003;
    internal const uint NameChange = 0x800C;
}

internal interface ISteamBigPictureWindowProbe
{
    /// <summary>Inspects exactly one HWND. Used for event-driven discovery -- no EnumWindows call.</summary>
    BigPictureCandidateInspection InspectCandidate(IntPtr window);

    /// <summary>
    /// Performs a single EnumWindows pass. If <paramref name="preferredHwnd"/> is itself a valid candidate it
    /// is returned; otherwise the first valid candidate found is returned.
    /// </summary>
    BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd);

    /// <summary>
    /// Re-validates an already-tracked HWND's lifetime and ownership only -- no EnumWindows call, and no
    /// title/class re-check (see <see cref="BigPictureTrackedWindowInspection"/>).
    /// </summary>
    BigPictureTrackedWindowInspection InspectTrackedWindow(IntPtr window, uint expectedProcessId);
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

    /// <summary>
    /// Legacy full-scan probe used only by the elevated Steam safety gate (ElevatedPrerequisiteSetup). Not
    /// part of the BPM watcher's detection path and intentionally left as fail-closed on any unreliable window.
    /// </summary>
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

    public BigPictureCandidateInspection InspectCandidate(IntPtr window)
    {
        var candidate = _candidateReader(window);
        if (!candidate.IsReliable) return new(false, false, 0);
        if (!candidate.IsCandidate) return new(false, true, 0);

        var classNameResult = _classNameReader(window);
        if (!classNameResult.Succeeded) return new(false, false, 0);
        var titleResult = _titleReader(window);
        if (!titleResult.Succeeded) return new(false, false, 0);

        if (!string.Equals(classNameResult.Value, WindowClass, StringComparison.Ordinal)) return new(false, true, 0);
        if (!titleResult.Value.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase)) return new(false, true, 0);

        return new(true, true, candidate.ProcessId);
    }

    public BigPictureTrackedWindowInspection InspectTrackedWindow(IntPtr window, uint expectedProcessId)
    {
        // Deliberately skips the class/title re-check: those are discovery-time evidence only, and Steam
        // is free to rename the already-authorized BPM window during its startup churn.
        var candidate = _candidateReader(window);
        if (!candidate.IsReliable) return new(false, false);
        if (!candidate.IsCandidate) return new(false, true); // window gone, or no longer owned by steamwebhelper
        if (expectedProcessId != 0 && candidate.ProcessId != expectedProcessId) return new(false, true); // HWND reused by a different process
        return new(true, true);
    }

    public BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd)
    {
        try
        {
            var found = IntPtr.Zero;
            uint foundPid = 0;
            var stoppedOnPreferredMatch = false;
            var enumerationSucceeded = _enumerateWindows(window =>
            {
                var inspection = InspectCandidate(window);
                // A single window's read failing (process-lookup race, etc.) must not poison the whole
                // scan -- just skip it and keep looking.
                if (!inspection.IsReliable || !inspection.IsCandidate) return true;

                if (window == preferredHwnd)
                {
                    found = window;
                    foundPid = inspection.ProcessId;
                    stoppedOnPreferredMatch = true;
                    // Returning false stops EnumWindows early, but per Win32 semantics that also makes the
                    // wrapped EnumWindows call itself return FALSE -- indistinguishable from a real
                    // enumeration failure. stoppedOnPreferredMatch lets us tell the two apart below.
                    return false;
                }

                if (found == IntPtr.Zero)
                {
                    found = window;
                    foundPid = inspection.ProcessId;
                }
                return true;
            });
            if (!enumerationSucceeded && !stoppedOnPreferredMatch) return new(false, IntPtr.Zero, 0, false);
            return new(found != IntPtr.Zero, found, foundPid, true);
        }
        catch
        {
            return new(false, IntPtr.Zero, 0, false);
        }
    }

    private bool IsBigPictureWindow(IntPtr window, out bool reliable)
    {
        var inspection = InspectCandidate(window);
        reliable = inspection.IsReliable;
        return inspection.IsCandidate;
    }

    private static WindowCandidateResult IsSteamWebHelperWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return new(false, false, 0);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new(string.Equals(process.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase), true, processId);
        }
        catch
        {
            return new(false, false, 0);
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
    bool Start(Action<BigPictureWinEvent> callback);
}

internal sealed class WindowsSteamBigPictureEventHook : ISteamBigPictureEventHook
{
    private static readonly uint[] Events =
    [
        BigPictureWinEventType.Create,
        BigPictureWinEventType.Destroy,
        BigPictureWinEventType.Show,
        BigPictureWinEventType.Hide,
        BigPictureWinEventType.NameChange
    ];
    private readonly List<IntPtr> _hooks = [];
    private WinEventDelegate? _callback;
    private Action<BigPictureWinEvent>? _dispatch;

    public bool Start(Action<BigPictureWinEvent> callback)
    {
        _dispatch = callback;
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
        => _dispatch?.Invoke(new BigPictureWinEvent(eventType, window, objectId, childId));

    public void Dispose()
    {
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
        _callback = null;
        _dispatch = null;
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minEvent, uint maxEvent, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}

internal enum BigPictureSessionState { Inactive, EntryProtected, Active, Reconciling }

/// <summary>
/// Event-driven BPM (Steam Big Picture) session tracker.
///
/// State machine: INACTIVE -> ENTRY_PROTECTED -> ACTIVE -> INACTIVE, with a bounded RECONCILING detour out
/// of ACTIVE. Externally only the boolean IsActive is exposed (true for ENTRY_PROTECTED, ACTIVE, and
/// RECONCILING alike). A newly discovered candidate HWND authorizes a session and activates it
/// immediately; the following 3-second ENTRY_PROTECTED window absorbs Steam's known startup window churn
/// (the tracked HWND can be replaced any number of times without publishing an inactive transition). At
/// the end of that window the tracked HWND's lifetime/ownership is re-validated directly -- title/class
/// are discovery-time evidence only, so a NAMECHANGE during startup does not un-authorize the already-
/// tracked window; a full EnumWindows scan is only used as a fallback when the tracked window itself can't
/// be confirmed.
///
/// Once ACTIVE, the session tracks the exact HWND's lifetime -- HIDE/SHOW/NAMECHANGE/foreground changes
/// are irrelevant. Destruction of the tracked HWND immediately relinquishes exact-HWND authority
/// (RECONCILING, still externally active) *before* the one-shot EnumWindows replacement search runs, so a
/// replacement CREATE WinEvent racing that search is still consumed rather than dropped (ACTIVE ignores
/// discovery events; RECONCILING does not). RECONCILING is bounded to that single search: if it doesn't
/// establish a replacement (found none, or the scan itself failed) the session ends -- an indefinite
/// "waiting for some future WinEvent that may never come" state is deliberately avoided, since the tracked
/// HWND's destruction is already strong termination evidence. A genuine reopen immediately afterward starts
/// a brand-new session at no extra cost.
///
/// Both resolution paths (protection-boundary and post-destroy replacement search) revalidate their
/// resolved candidate's lifetime/ownership one more time immediately before adopting it, inside the same
/// lock that performs the commit (see IsStillValid_NoLock) -- closing the window where the candidate could
/// have been destroyed between producing that scan/inspection result and committing it as tracked.
///
/// No polling: all work is triggered by WinEvents, plus a single one-shot delayed callback per session for
/// the startup protection window.
/// </summary>
internal sealed class SteamBigPictureWatcher : IDisposable
{
    private static readonly TimeSpan ProtectionWindow = TimeSpan.FromSeconds(3);

    private readonly ISteamBigPictureWindowProbe _probe;
    private readonly ISteamBigPictureEventHook _eventHook;
    private readonly Func<TimeSpan, Action, IDisposable> _scheduler;
    private readonly object _sync = new();

    private bool _started;
    private bool _disposed;
    private BigPictureSessionState _state = BigPictureSessionState.Inactive;
    private IntPtr _trackedHwnd;
    private uint _trackedPid;
    private IntPtr _initialHwnd;
    private int _generation;
    private int _rebindCount;
    private DateTimeOffset _sessionStartedAt;
    private IDisposable? _protectionTimer;

    internal SteamBigPictureWatcher(
        ISteamBigPictureWindowProbe? probe = null,
        ISteamBigPictureEventHook? eventHook = null,
        Func<TimeSpan, Action, IDisposable>? scheduler = null)
    {
        _probe = probe ?? new SteamBigPictureWindowProbe();
        _eventHook = eventHook ?? new WindowsSteamBigPictureEventHook();
        _scheduler = scheduler ?? DefaultScheduler;
    }

    public event EventHandler? StateChanged;
    public bool IsActive { get { lock (_sync) return _state != BigPictureSessionState.Inactive; } }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            if (!_eventHook.Start(OnWinEvent)) return;
            _started = true;
        }

        // One-time startup snapshot: the addon may start while BPM is already running.
        TryBeginFromScan(_probe.ScanForCandidate(IntPtr.Zero));
    }

    /// <summary>Manual nudge: only meaningful while INACTIVE (no periodic re-check while a session is live).</summary>
    public void Refresh()
    {
        lock (_sync)
        {
            if (_disposed || !_started || _state != BigPictureSessionState.Inactive) return;
        }
        TryBeginFromScan(_probe.ScanForCandidate(IntPtr.Zero));
    }

    private void OnWinEvent(BigPictureWinEvent evt)
    {
        const int objectIdWindow = 0;
        const int childIdSelf = 0;
        if (evt.ObjectId != objectIdWindow || evt.ChildId != childIdSelf || evt.Hwnd == IntPtr.Zero) return;

        if (evt.EventType == BigPictureWinEventType.Destroy) { HandleDestroy(evt.Hwnd); return; }
        HandleDiscoveryEvent(evt.Hwnd);
    }

    private void HandleDiscoveryEvent(IntPtr hwnd)
    {
        lock (_sync)
        {
            if (_disposed || !_started || _state == BigPictureSessionState.Active) return;
        }

        // Inspect the delivered HWND directly -- no EnumWindows for normal runtime discovery.
        var inspection = _probe.InspectCandidate(hwnd);
        if (!inspection.IsReliable || !inspection.IsCandidate) return; // fail closed

        lock (_sync)
        {
            if (_disposed || !_started || _state == BigPictureSessionState.Active) return;

            if (_state == BigPictureSessionState.Inactive)
            {
                BeginSession_NoLock(hwnd, inspection.ProcessId, out var generation);
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture session detected.",
                    ("InitialHwnd", hwnd), ("Pid", inspection.ProcessId), ("State", "EntryProtected"));
                ScheduleProtectionTimer_NoLock(generation);
            }
            else if (_state == BigPictureSessionState.Reconciling)
            {
                // The tracked HWND was just destroyed and a bounded replacement search is in flight (or
                // about to run); this WinEvent races it and wins -- re-anchor immediately so the in-flight
                // scan's result (checked against _state below) gets discarded rather than clobbering this.
                _trackedHwnd = hwnd;
                _trackedPid = inspection.ProcessId;
                _state = BigPictureSessionState.Active;
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture tracked window replaced.",
                    ("CurrentHwnd", hwnd), ("Pid", inspection.ProcessId), ("Reason", "ReplacementObservedDuringReconciliation"));
            }
            else if (_trackedHwnd != hwnd)
            {
                var previous = _trackedHwnd;
                _trackedHwnd = hwnd;
                _trackedPid = inspection.ProcessId;
                _rebindCount++;
                Diagnostics.AppLog.Debug("Steam.BigPicture", "Steam Big Picture tracked window replaced.",
                    ("PreviousHwnd", previous), ("CurrentHwnd", hwnd), ("Reason", "CandidateReplacedDuringStartup"));
            }
            else
            {
                return;
            }
        }

        RaiseStateChangedIfJustActivated();
    }

    private bool _justActivatedPendingPublish;

    private void RaiseStateChangedIfJustActivated()
    {
        bool publish;
        lock (_sync)
        {
            publish = _justActivatedPendingPublish;
            _justActivatedPendingPublish = false;
        }
        if (publish) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginSession_NoLock(IntPtr hwnd, uint pid, out int generation)
    {
        _trackedHwnd = hwnd;
        _trackedPid = pid;
        _initialHwnd = hwnd;
        _state = BigPictureSessionState.EntryProtected;
        _generation++;
        generation = _generation;
        _sessionStartedAt = DateTimeOffset.UtcNow;
        _rebindCount = 0;
        _justActivatedPendingPublish = true;
    }

    private void TryBeginFromScan(BigPictureScanResult scan)
    {
        // Authorizing a brand-new session fails closed: both a failed enumeration and "nothing found"
        // leave the watcher inactive.
        if (!scan.EnumerationSucceeded || !scan.Found) return;

        lock (_sync)
        {
            if (_disposed || !_started || _state != BigPictureSessionState.Inactive) return;
            BeginSession_NoLock(scan.Hwnd, scan.ProcessId, out var generation);
            Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture session detected.",
                ("InitialHwnd", scan.Hwnd), ("Pid", scan.ProcessId), ("State", "EntryProtected"));
            ScheduleProtectionTimer_NoLock(generation);
        }
        RaiseStateChangedIfJustActivated();
    }

    private void ScheduleProtectionTimer_NoLock(int generation)
    {
        _protectionTimer?.Dispose();
        _protectionTimer = _scheduler(ProtectionWindow, () => ProtectionElapsed(generation));
    }

    private void ProtectionElapsed(int generation)
    {
        IntPtr trackedHwndSnapshot;
        uint trackedPidSnapshot;
        IDisposable? firedTimer;
        lock (_sync)
        {
            if (_disposed || !_started || _generation != generation || _state != BigPictureSessionState.EntryProtected) return;
            trackedHwndSnapshot = _trackedHwnd;
            trackedPidSnapshot = _trackedPid;
            // This one-shot timer has now fired and served its purpose; release the reference immediately
            // rather than holding it until the next session starts (or Dispose()).
            firedTimer = _protectionTimer;
            _protectionTimer = null;
        }
        firedTimer?.Dispose();

        // Fast path: the tracked HWND (however many times it was transferred during startup churn) is
        // re-validated directly by lifetime/ownership only -- no title/class re-check, and no EnumWindows
        // call at all in the common case. A full scan is only a fallback for discovering a *different*
        // candidate when the tracked one can no longer be confirmed.
        var resolved = trackedHwndSnapshot != IntPtr.Zero
            ? ResolveTrackedOrFallback(trackedHwndSnapshot, trackedPidSnapshot)
            : _probe.ScanForCandidate(IntPtr.Zero);
        var raiseInactive = false;

        lock (_sync)
        {
            if (_disposed || !_started || _generation != generation || _state != BigPictureSessionState.EntryProtected) return;

            // Final revalidation immediately before commit, inside this same critical section: the
            // resolved candidate may have been destroyed in the window between producing `resolved` and
            // taking this lock. Lightweight lifetime-only check (no EnumWindows), so it's safe to run here.
            var committed = resolved.Found && IsStillValid_NoLock(resolved.Hwnd, resolved.ProcessId);

            if (committed)
            {
                var rebindCount = _rebindCount;
                var elapsedMs = (long)(DateTimeOffset.UtcNow - _sessionStartedAt).TotalMilliseconds;
                _trackedHwnd = resolved.Hwnd;
                _trackedPid = resolved.ProcessId;
                _state = BigPictureSessionState.Active;
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture startup stabilized.",
                    ("InitialHwnd", _initialHwnd), ("FinalHwnd", resolved.Hwnd), ("RebindCount", rebindCount), ("ElapsedMs", elapsedMs));
            }
            else
            {
                // Bounded: whether nothing was found, the fallback scan itself failed, or the resolved
                // candidate died before adoption, the protection window only gets this one resolution
                // attempt -- no indefinite ambiguous-active wait for a WinEvent that may never come. A
                // genuine reopen right afterward starts a fresh session.
                var elapsedMs = (long)(DateTimeOffset.UtcNow - _sessionStartedAt).TotalMilliseconds;
                _state = BigPictureSessionState.Inactive;
                _trackedHwnd = IntPtr.Zero;
                _generation++;
                raiseInactive = true;
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture session ended.",
                    ("Hwnd", trackedHwndSnapshot), ("Reason", "NoStableCandidateAtProtectionBoundary"), ("DurationMs", elapsedMs));
            }
        }

        if (raiseInactive) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Lightweight lifetime/ownership-only re-check (no EnumWindows) for a candidate about to be adopted.
    /// Must be called from inside <see cref="_sync"/>, immediately before committing the candidate, to
    /// close the window between producing a scan/inspection result and adopting it.
    /// </summary>
    private bool IsStillValid_NoLock(IntPtr hwnd, uint pid)
    {
        var revalidated = _probe.InspectTrackedWindow(hwnd, pid);
        return revalidated.StillValid && revalidated.IsReliable;
    }

    private BigPictureScanResult ResolveTrackedOrFallback(IntPtr trackedHwnd, uint trackedPid)
    {
        var tracked = _probe.InspectTrackedWindow(trackedHwnd, trackedPid);
        if (tracked.StillValid && tracked.IsReliable) return new(true, trackedHwnd, trackedPid, true);
        return _probe.ScanForCandidate(IntPtr.Zero);
    }

    private void HandleDestroy(IntPtr hwnd)
    {
        int generation;
        uint previousPid;
        lock (_sync)
        {
            if (_disposed || !_started) return;
            if (_state == BigPictureSessionState.Inactive) return;
            if (hwnd != _trackedHwnd) return; // unrelated HWND destroyed (or nothing currently tracked) -- ignore
            generation = _generation;

            if (_state == BigPictureSessionState.EntryProtected)
            {
                // Sticky session, transferable HWND: destruction during startup churn is not termination.
                // Clear the tracked HWND; either a later discovery event re-anchors it, or the end-of-
                // protection reconciliation resolves it (see ProtectionElapsed).
                _trackedHwnd = IntPtr.Zero;
                return;
            }

            // state == Active: tracked HWND destroyed. Strong termination evidence unless a replacement
            // exists. Relinquish exact-HWND authority *before* the replacement scan runs: ACTIVE ignores
            // discovery WinEvents, so a replacement CREATE racing the scan would otherwise be dropped.
            // RECONCILING does not ignore them (see HandleDiscoveryEvent), so the race is resolved by
            // whichever completes first, and the generation/state re-check below discards a stale scan
            // result if a WinEvent already won.
            previousPid = _trackedPid;
            _trackedHwnd = IntPtr.Zero;
            _state = BigPictureSessionState.Reconciling;
        }

        var scan = _probe.ScanForCandidate(IntPtr.Zero);
        var raiseInactive = false;

        lock (_sync)
        {
            if (_disposed || !_started || _generation != generation || _state != BigPictureSessionState.Reconciling) return;

            // Final revalidation immediately before commit, inside this same critical section: the
            // replacement candidate found by the scan may itself have been destroyed in the window between
            // producing that result and taking this lock.
            var committed = scan.Found && IsStillValid_NoLock(scan.Hwnd, scan.ProcessId);

            if (committed)
            {
                _trackedHwnd = scan.Hwnd;
                _trackedPid = scan.ProcessId;
                _state = BigPictureSessionState.Active;
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture tracked window replaced.",
                    ("PreviousHwnd", hwnd), ("PreviousPid", previousPid), ("CurrentHwnd", scan.Hwnd), ("CurrentPid", scan.ProcessId), ("Reason", "TrackedWindowDestroyed"));
            }
            else
            {
                // Bounded: whether nothing was found, the scan itself failed, or the replacement candidate
                // died before adoption, this is the one and only replacement attempt for this destruction --
                // no indefinite ambiguous-active wait for a WinEvent that may never come. The tracked HWND's
                // destruction is already strong termination evidence; a genuine reopen right afterward
                // starts a brand-new session.
                var durationMs = (long)(DateTimeOffset.UtcNow - _sessionStartedAt).TotalMilliseconds;
                _state = BigPictureSessionState.Inactive;
                _generation++;
                raiseInactive = true;
                Diagnostics.AppLog.Info("Steam.BigPicture", "Steam Big Picture session ended.",
                    ("Hwnd", hwnd), ("Pid", previousPid), ("Reason", "TrackedWindowDestroyed"), ("DurationMs", durationMs));
            }
        }

        if (raiseInactive) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IDisposable DefaultScheduler(TimeSpan delay, Action callback)
        => new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        IDisposable? timer;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            timer = _protectionTimer;
            _protectionTimer = null;
        }
        timer?.Dispose();
        _eventHook.Dispose();
        GC.SuppressFinalize(this);
    }
}
