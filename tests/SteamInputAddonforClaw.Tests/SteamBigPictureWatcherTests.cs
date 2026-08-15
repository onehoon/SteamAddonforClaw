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
    public void ProtectionBoundaryScanFailure_DoesNotGetStuckInEntryProtected_ReacquiresOnNextValidEvent()
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

        // A transient full-enumeration failure at the protection boundary must not revoke the session, but
        // must also not leave the watcher stuck in EntryProtected forever.
        probe.EnumerationReliable = false;
        scheduler.FireLast();
        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // No timer was rescheduled (no polling); recovery is purely event-driven.
        var scheduleCountAfterFailure = scheduler.ScheduleCount;

        // Confirm the watcher lost exact-HWND authority: destroying the old HWND now (if the watcher were
        // still tracking it) would be a no-op either way, but a *new* valid candidate must re-anchor it.
        probe.EnumerationReliable = true;
        probe.RemoveCandidate(Hwnd1); // simulate Hwnd1 genuinely gone by the time recovery happens
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions); // still no externally-visible transition -- session never left "active"
        Assert.Equal(scheduleCountAfterFailure, scheduler.ScheduleCount); // reacquire schedules no new timer

        // Prove Hwnd2 is now genuinely tracked.
        probe.RemoveCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd2);
        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void ActiveTrackedHwndDestroyed_ReplacementScanFails_DoesNotStayActiveTrackingDeadHwnd_ReacquiresLater()
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
        probe.EnumerationReliable = false;
        probe.RemoveCandidate(Hwnd1);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd1);

        Assert.True(watcher.IsActive); // must not revoke on a transient failure
        Assert.Equal(1, transitions);

        // Because ACTIVE ignores all non-destroy discovery events, if the watcher were still ACTIVE while
        // tracking the (now-dead) Hwnd1, a new BPM window appearing would never be picked up. Prove it is.
        probe.EnumerationReliable = true;
        probe.AddCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Create, Hwnd2);

        Assert.True(watcher.IsActive);
        Assert.Equal(1, transitions);

        // Prove Hwnd2 is now genuinely tracked (not the dead Hwnd1).
        probe.RemoveCandidate(Hwnd2);
        hook.Raise(BigPictureWinEventType.Destroy, Hwnd2);
        Assert.False(watcher.IsActive);
        Assert.Equal(2, transitions);
    }

    private sealed class FakeProbe : ISteamBigPictureWindowProbe
    {
        private readonly Dictionary<IntPtr, (bool Reliable, uint Pid)> _candidates = new();
        private readonly List<IntPtr> _order = [];

        public int ScanForCandidateCallCount { get; private set; }

        /// <summary>When false, ScanForCandidate simulates a full EnumWindows enumeration failure.</summary>
        public bool EnumerationReliable { get; set; } = true;

        public void AddCandidate(IntPtr hwnd, uint pid = 100, bool reliable = true)
        {
            _candidates[hwnd] = (reliable, pid);
            if (!_order.Contains(hwnd)) _order.Add(hwnd);
        }

        public void RemoveCandidate(IntPtr hwnd)
        {
            _candidates.Remove(hwnd);
            _order.Remove(hwnd);
        }

        public BigPictureCandidateInspection InspectCandidate(IntPtr window)
        {
            if (!_candidates.TryGetValue(window, out var info)) return new(false, true, 0);
            if (!info.Reliable) return new(false, false, 0);
            return new(true, true, info.Pid);
        }

        public BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd)
        {
            ScanForCandidateCallCount++;
            if (!EnumerationReliable) return new(false, IntPtr.Zero, 0, false);

            if (preferredHwnd != IntPtr.Zero && _candidates.TryGetValue(preferredHwnd, out var preferred) && preferred.Reliable)
                return new(true, preferredHwnd, preferred.Pid, true);

            foreach (var hwnd in _order)
            {
                if (_candidates.TryGetValue(hwnd, out var info) && info.Reliable) return new(true, hwnd, info.Pid, true);
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
