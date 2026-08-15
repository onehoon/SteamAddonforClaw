using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamBigPictureWatcherTests
{
    private static readonly IntPtr Hwnd1 = new(1);
    private static readonly IntPtr Hwnd2 = new(2);
    private static readonly IntPtr Hwnd3 = new(3);
    private static readonly IntPtr UnrelatedHwnd = new(999);

    [Fact]
    public void FirstValidCandidate_PublishesExactlyOneInactiveToActiveTransition()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;

        watcher.Start();
        Assert.False(watcher.IsActive);
        Assert.Equal(0, transitions);

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void CandidateDestroyedShortlyAfterDiscovery_RemainsActive()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        Assert.Equal(1, transitions);

        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void NewCandidateDuringStartupProtection_TransfersTrackedHwnd_RemainsActive()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        Assert.Equal(1, transitions);

        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // End the protection window: reconciliation should confirm Hwnd2 (the transferred candidate) as
        // the stable tracked window, without publishing another transition (still active externally).
        scheduler.FireLast();
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // Prove Hwnd2 (not Hwnd1) is now tracked: destroying it with no replacement ends the session.
        probe.RemoveCandidate(Hwnd1);
        probe.RemoveCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd2);

        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void MultipleReplacementsDuringProtectionWindow_NoActiveInactiveOscillation()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);
        probe.AddCandidate(Hwnd3);
        hook.Raise(BigPictureWinEventType.Create, Hwnd3);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);
        hook.Raise(BigPictureWinEventType.NameChange, Hwnd3);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void StartupProtectionCompletion_FinalCandidateBecomesTrackedActiveHwnd()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        probe.RemoveCandidate(Hwnd1);
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        scheduler.FireLast();

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        probe.RemoveCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd2);

        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void TrackedHwndHide_RemainsActive()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();

        hook.Raise(BigPictureWinEventType.Hide, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void TrackedHwndNameChange_RemainsActive()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();

        hook.Raise(BigPictureWinEventType.NameChange, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void UnrelatedSteamWebHelperHwndDestroyed_RemainsActive_NoReplacementScan()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();
        var scanCountBefore = probe.ScanForCandidateCallCount;

        hook.Raise(BigPictureWinEventType.Destroy, UnrelatedHwnd);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
        Assert.Equal(scanCountBefore, probe.ScanForCandidateCallCount);
    }

    [Fact]
    public void TrackedActiveHwndDestroyed_ReplacementExists_RebindsAndRemainsActive()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();

        probe.RemoveCandidate(Hwnd1);
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void TrackedActiveHwndDestroyed_NoReplacement_ExactlyOneActiveToInactiveTransition()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();

        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void RapidGenuineReopenAfterInactive_ActivatesImmediately_WithFreshProtectionWindow()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();
        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);
        Assert.False(watcher.IsActive);
        var scheduleCountAfterFirstSession = scheduler.ScheduleCount;

        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        Assert.True(watcher.IsActive);
        Assert.Equal(3, transitions);
        Assert.Equal(scheduleCountAfterFirstSession + 1, scheduler.ScheduleCount);
    }

    [Fact]
    public void HookRegistrationFailure_NeverAuthorizesBigPicture()
    {
        var probe = new FakeProbe();
        probe.AddCandidate(Hwnd1);
        var hook = new FakeHook { StartResult = false };
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);

        watcher.Start();

        Assert.False(watcher.IsActive);
        Assert.Equal(0, probe.ScanForCandidateCallCount);
    }

    [Fact]
    public void UnrelatedLookupRaceWhileActive_DoesNotClearExistingSession()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();

        // Simulate an unrelated HWND whose inspection is unreliable (process lookup race). ACTIVE state
        // ignores non-destroy events entirely, so this must not affect the session either way.
        probe.AddCandidate(UnrelatedHwnd, reliable: false);
        hook.Raise(BigPictureWinEventType.Show, UnrelatedHwnd);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void NewUnreadableCandidate_FailsClosed()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1, reliable: false);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);

        Assert.False(watcher.IsActive);
        Assert.Equal(0, transitions);
    }

    [Fact]
    public void AddonStartsWhileBigPictureAlreadyExists_InitialSnapshotDetectsIt()
    {
        var probe = new FakeProbe();
        probe.AddCandidate(Hwnd1);
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;

        watcher.Start();

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
    }

    [Fact]
    public void Idle_NoWinEventNoTransition_NoRecurringEnumWindowsOrTimerCallbacks()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);

        watcher.Start();

        Assert.Equal(1, probe.ScanForCandidateCallCount); // startup snapshot only
        Assert.Equal(0, scheduler.ScheduleCount); // nothing found -> no protection timer scheduled

        // No events raised, no time-based re-check triggered by the watcher itself.
        Assert.Equal(1, probe.ScanForCandidateCallCount);
        Assert.Equal(0, scheduler.ScheduleCount);
        Assert.False(watcher.IsActive);
    }

    [Fact]
    public void ProtectionBoundaryResolutionFails_EndsSessionInsteadOfWaitingIndefinitely_FreshCandidateStartsNewSession()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        Assert.Equal(1, transitions);

        // Both the tracked-window fast-path check and the EnumWindows fallback fail transiently at the
        // 3s boundary. The protection window only gets this one resolution attempt: bounded, not an
        // indefinite "wait for some future WinEvent that may never come" state.
        probe.AddCandidate(Hwnd1, reliable: false);
        probe.EnumerationReliable = false;
        scheduler.FireLast();

        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);

        // A genuine reopen right afterward is unaffected -- it simply starts a brand-new session.
        probe.EnumerationReliable = true;
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        Assert.True(watcher.IsActive);
        Assert.Equal(3, transitions);
    }

    [Fact]
    public void TitleChangeBeforeProtectionBoundary_TrackedHwndStillConfirmed_RemainsActiveWithoutFullScan()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        Assert.Equal(1, transitions);

        // Steam renames the already-authorized window during startup (NAMECHANGE). Title is discovery-time
        // evidence only, so this alone must not un-authorize the exact HWND that was already tracked.
        probe.SetTitleMatches(Hwnd1, titleMatches: false);
        hook.Raise(BigPictureWinEventType.NameChange, Hwnd1);
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        var scanCountBeforeBoundary = probe.ScanForCandidateCallCount;
        scheduler.FireLast();

        // Still active, still the same tracked HWND (proven by destroying it with no replacement below),
        // and reconciliation used the lightweight tracked-window check -- no EnumWindows call needed.
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);
        Assert.Equal(scanCountBeforeBoundary, probe.ScanForCandidateCallCount);

        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);
        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void ActiveTrackedHwndDestroyed_ReplacementScanFails_EndsSessionInsteadOfStayingActiveForever()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // Tracked HWND is destroyed, and the replacement-search EnumWindows call itself fails (transient).
        // The destruction is already strong termination evidence, and the search is bounded to this one
        // attempt, so the session ends immediately rather than staying ambiguously "active" forever.
        probe.EnumerationReliable = false;
        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);

        // A genuine reopen right afterward is unaffected.
        probe.EnumerationReliable = true;
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        Assert.True(watcher.IsActive);
        Assert.Equal(3, transitions);
    }

    [Fact]
    public void ReplacementCreateEventDuringReplacementScan_IsNotLostToTheRace()
    {
        var probe = new FakeProbe();
        var hook = new FakeHook();
        var scheduler = new ManualScheduler();
        using var watcher = new SteamBigPictureWatcher(probe, hook, scheduler.Schedule);
        var transitions = 0;
        watcher.StateChanged += (_, _) => transitions++;
        watcher.Start();

        probe.AddCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Create, Hwnd1);
        scheduler.FireLast();
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // A different candidate is visible to the scan's own fallback search, but a CREATE WinEvent for yet
        // another HWND races the in-flight EnumWindows call and must win -- the tracked HWND was already
        // relinquished (ACTIVE -> RECONCILING) before the scan started, so RECONCILING can still consume it.
        probe.AddCandidate(Hwnd3);
        probe.RemoveCandidate(Hwnd1);
        probe.DuringScan = () =>
        {
            probe.AddCandidate(Hwnd2);
            hook.Raise(BigPictureWinEventType.Create, Hwnd2);
        };
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // Hwnd3 was visible to the scan but must not be what ended up tracked -- the racing event won.
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd3);
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // Prove Hwnd2 (the racing event's candidate) is genuinely tracked.
        probe.RemoveCandidate(Hwnd2);
        probe.RemoveCandidate(Hwnd3);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd2);
        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    private sealed class FakeProbe : ISteamBigPictureWindowProbe
    {
        private sealed record CandidateInfo(bool Reliable, uint Pid, bool TitleMatches);

        private readonly Dictionary<IntPtr, CandidateInfo> _candidates = new();
        private readonly List<IntPtr> _order = [];

        public int ScanForCandidateCallCount { get; private set; }

        /// <summary>When false, ScanForCandidate simulates a full EnumWindows enumeration failure.</summary>
        public bool EnumerationReliable { get; set; } = true;

        /// <summary>
        /// If set, invoked (once) synchronously in the middle of the next ScanForCandidate call, before it
        /// computes its own result -- simulates a WinEvent racing an in-flight EnumWindows scan.
        /// </summary>
        public Action? DuringScan { get; set; }

        public void AddCandidate(IntPtr hwnd, uint pid = 100, bool reliable = true, bool titleMatches = true)
        {
            _candidates[hwnd] = new CandidateInfo(reliable, pid, titleMatches);
            if (!_order.Contains(hwnd)) _order.Add(hwnd);
        }

        public void RemoveCandidate(IntPtr hwnd)
        {
            _candidates.Remove(hwnd);
            _order.Remove(hwnd);
        }

        /// <summary>Simulates a NAMECHANGE that moves an existing, still-alive candidate off the BPM title prefix.</summary>
        public void SetTitleMatches(IntPtr hwnd, bool titleMatches)
        {
            if (_candidates.TryGetValue(hwnd, out var info)) _candidates[hwnd] = info with { TitleMatches = titleMatches };
        }

        public BigPictureCandidateInspection InspectCandidate(IntPtr window)
        {
            if (!_candidates.TryGetValue(window, out var info)) return new(false, true, 0);
            if (!info.Reliable) return new(false, false, 0);
            if (!info.TitleMatches) return new(false, true, 0);
            return new(true, true, info.Pid);
        }

        public BigPictureTrackedWindowInspection InspectTrackedWindow(IntPtr window, uint expectedProcessId)
        {
            if (!_candidates.TryGetValue(window, out var info)) return new(false, true); // gone
            if (!info.Reliable) return new(false, false);
            if (expectedProcessId != 0 && info.Pid != expectedProcessId) return new(false, true);
            return new(true, true); // alive, same owner -- title irrelevant
        }

        public BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd)
        {
            ScanForCandidateCallCount++;
            var duringScan = DuringScan;
            DuringScan = null;
            duringScan?.Invoke();

            if (!EnumerationReliable) return new(false, IntPtr.Zero, 0, false);

            if (preferredHwnd != IntPtr.Zero && _candidates.TryGetValue(preferredHwnd, out var preferred) && preferred.Reliable && preferred.TitleMatches)
                return new(true, preferredHwnd, preferred.Pid, true);

            foreach (var hwnd in _order)
            {
                if (_candidates.TryGetValue(hwnd, out var info) && info.Reliable && info.TitleMatches) return new(true, hwnd, info.Pid, true);
            }
            return new(false, IntPtr.Zero, 0, true);
        }
    }

    private sealed class FakeHook : ISteamBigPictureEventHook
    {
        private Action<BigPictureWinEvent>? _callback;
        public bool StartResult { get; init; } = true;
        public bool Start(Action<BigPictureWinEvent> callback) { _callback = callback; return StartResult; }
        public void Raise(uint eventType, IntPtr hwnd, int objectId = 0, int childId = 0)
            => _callback?.Invoke(new BigPictureWinEvent(eventType, hwnd, objectId, childId));
        public void Dispose() => _callback = null;
    }

    private sealed class ManualScheduler
    {
        private sealed class Handle : IDisposable
        {
            public bool Cancelled;
            public void Dispose() => Cancelled = true;
        }

        private (TimeSpan Delay, Action Callback, Handle Handle)? _last;
        public int ScheduleCount { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            ScheduleCount++;
            var handle = new Handle();
            _last = (delay, callback, handle);
            return handle;
        }

        public void FireLast()
        {
            if (_last is { } scheduled && !scheduled.Handle.Cancelled) scheduled.Callback();
        }
    }
}
