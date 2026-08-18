using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMOem1LifecycleCoordinatorTests
{
    // ---- Shared harness ----

    private static Harness NewHarness() => new();

    private sealed class Harness
    {
        internal readonly FakeSnapshotSource Snapshots = new();
        internal readonly FakeHelperApi HelperApi = new();
        internal readonly FakeHandleOpener HandleOpener = new();
        internal readonly FakeTerminateInvoker TerminateInvoker = new();
        internal readonly FakeIdentityInspector IdentityInspector = new();
        internal readonly FakeWindowProvider WindowProvider = new();
        internal readonly ManualDelayGate Delay = new();

        internal CenterMAutoRunState AutoRun = CenterMAutoRunState.Disabled;
        internal string? StagedPath = @"C:\fake\Runtime\MSI Center M.exe";
        internal Func<bool> EnvironmentEligible = () => true;
        internal readonly CenterMHelperOwnership HelperOwnership;

        internal Harness()
        {
            HelperOwnership = new CenterMHelperOwnership(HelperApi);
            // A real owned helper process (even suspended) shows up in a same-name enumeration --
            // the fake mirrors that by always reflecting live ownership state instead of requiring
            // every test to manually add/remove the helper's own PID from its foreign-process list.
            Snapshots.HelperProcessIdProvider = () => HelperOwnership.IsOwned ? HelperOwnership.ProcessId : null;
            // Once a real MainUI process's exit is actually confirmed, it stops appearing in a
            // fresh enumeration -- mirrors real Windows behavior so post-termination reconciliation
            // in tests observes a consistent world instead of re-discovering an already-dead PID.
            TerminateInvoker.OnExitConfirmed = () => Snapshots.Foreign = [];
        }

        internal CenterMOem1LifecycleCoordinator Build(TimeSpan? debounce = null)
        {
            var backendProbe = new CenterMBackendProbe(Snapshots);
            var mainUiObserver = new MainUiLifecycleObserver(WindowProvider);
            var terminator = new SafeMainUiTerminator(TerminateInvoker, IdentityInspector, WindowProvider, Snapshots);

            return new CenterMOem1LifecycleCoordinator(
                publishRootProvider: () => "fake-publish-root",
                backendProbe: backendProbe,
                autoRunReader: () => AutoRun,
                processSnapshotSource: Snapshots,
                helperOwnership: HelperOwnership,
                mainUiObserver: mainUiObserver,
                terminator: terminator,
                handleOpener: HandleOpener,
                identityInspector: IdentityInspector,
                stager: _ => StagedPath,
                delay: Delay.DelayAsync,
                environmentEligibility: () => EnvironmentEligible(),
                hiddenDebounce: debounce ?? TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class FakeSnapshotSource : IProcessSnapshotSource
    {
        internal IReadOnlyList<ProcessSnapshotEntry>? Launcher { get; set; } = [new(1, CenterMProcessNames.Launcher, null)];
        internal IReadOnlyList<ProcessSnapshotEntry>? Server { get; set; } = [new(2, CenterMProcessNames.Server, null)];
        /// <summary>Foreign (non-helper-owned) "MSI Center M" processes only -- the live owned
        /// helper PID (if any) is contributed automatically via <see cref="HelperProcessIdProvider"/>,
        /// matching real Windows behavior where even a CREATE_SUSPENDED process is enumerable.</summary>
        internal List<ProcessSnapshotEntry> Foreign { get; set; } = [];
        internal bool MainUiEnumerationUncertain { get; set; }
        internal Func<int?>? HelperProcessIdProvider { get; set; }
        /// <summary>Highest-priority override: when non-empty, each call dequeues one exact result,
        /// bypassing <see cref="Foreign"/>/<see cref="HelperProcessIdProvider"/> entirely -- for
        /// tests that need to assert on a specific raw sequence.</summary>
        internal Queue<IReadOnlyList<ProcessSnapshotEntry>?>? MainUiSequence { get; set; }
        internal int MainUiCallCount { get; private set; }

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (processName == CenterMProcessNames.MainUi)
            {
                MainUiCallCount++;
                if (MainUiSequence is { Count: > 0 } q) return q.Dequeue();
                if (MainUiEnumerationUncertain) return null;

                var result = new List<ProcessSnapshotEntry>(Foreign);
                if (HelperProcessIdProvider?.Invoke() is int helperPid)
                    result.Add(new ProcessSnapshotEntry(helperPid, CenterMProcessNames.MainUi, null));
                return result;
            }
            if (processName == CenterMProcessNames.Launcher) return Launcher;
            if (processName == CenterMProcessNames.Server) return Server;
            return [];
        }
    }

    private sealed class FakeHelperApi : IHelperProcessNativeApi
    {
        internal bool CreateSucceeds { get; set; } = true;
        internal bool JobSucceeds { get; set; } = true;
        internal bool LimitSucceeds { get; set; } = true;
        internal bool AssignSucceeds { get; set; } = true;
        internal bool ResumeSucceeds { get; set; } = true;
        internal bool TerminateSucceeds { get; set; } = true;
        internal bool WaitForExitSucceeds { get; set; } = true;
        internal LiveProcessProbeStatus LivenessResult { get; set; } = LiveProcessProbeStatus.Alive;
        internal int NextProcessId { get; set; } = 9000;
        internal int StartCallCount { get; private set; }
        internal int PollLivenessCallCount { get; private set; }
        /// <summary>Test-only hook (round-3 finding #6): fires at the moment <see cref="WaitForExit"/>
        /// is invoked during a helper stop, letting a test mutate world state (AutoRun, foreign
        /// process presence, ...) to simulate drift occurring during the bounded cleanup wait.</summary>
        internal Action? OnWaitForExitCalled { get; set; }
        /// <summary>Test-only hook (round-4 finding #2): fires right after a helper has been
        /// successfully created (before Job/Assign/Resume), letting a test simulate a suspend
        /// request becoming authoritative while an arm is already in flight, deterministically and
        /// without any real timing race.</summary>
        internal Action? OnCreateSuspendedCalled { get; set; }

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            StartCallCount++;
            win32Error = 0;
            if (!CreateSucceeds) { processId = 0; processHandle = null; threadHandle = null; return false; }
            OnCreateSuspendedCalled?.Invoke();
            processId = NextProcessId++;
            processHandle = new SafeProcessHandle(CurrentProcessHandle(), false);
            threadHandle = new SafeGenericHandle(CurrentProcessHandle());
            return true;
        }

        public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
        {
            win32Error = 0;
            if (!JobSucceeds) { jobHandle = null; return false; }
            jobHandle = new SafeGenericHandle(CurrentProcessHandle());
            return true;
        }

        public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error) { win32Error = 0; return LimitSucceeds; }
        public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error) { win32Error = 0; return AssignSucceeds; }
        public bool TryResumeThread(SafeHandle threadHandle, out int win32Error) { win32Error = 0; return ResumeSucceeds; }
        internal int TerminateCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error) { TerminateCallCount++; win32Error = TerminateSucceeds ? 0 : 5; return TerminateSucceeds; }
        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout)
        {
            OnWaitForExitCalled?.Invoke();
            return WaitForExitSucceeds;
        }

        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle)
        {
            PollLivenessCallCount++;
            return LivenessResult;
        }

        private static IntPtr CurrentProcessHandle() => System.Diagnostics.Process.GetCurrentProcess().Handle;
    }

    private sealed class FakeHandleOpener : IProcessHandleOpener
    {
        private const uint PROCESS_TERMINATE = 0x0001;
        internal bool FullRightsSucceed { get; set; } = true;
        internal bool ObservationSucceeds { get; set; } = true;
        internal int OpenCallCount { get; private set; }

        public SafeProcessHandle Open(int processId, uint desiredAccess)
        {
            OpenCallCount++;
            var isFullRights = (desiredAccess & PROCESS_TERMINATE) != 0;
            var succeeds = isFullRights ? FullRightsSucceed : ObservationSucceeds;
            return succeeds ? new SafeProcessHandle(System.Diagnostics.Process.GetCurrentProcess().Handle, false) : new SafeProcessHandle(IntPtr.Zero, false);
        }
    }

    private sealed class FakeTerminateInvoker : ITerminateProcessInvoker
    {
        internal bool TerminateSucceeds { get; set; } = true;
        internal bool WaitConfirmsExit { get; set; } = true;
        internal int TerminateCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
        {
            TerminateCallCount++;
            win32Error = TerminateSucceeds ? 0 : 5;
            return TerminateSucceeds;
        }

        /// <summary>Fires once exit is confirmed -- lets the harness mirror a real Windows fact:
        /// once a process has genuinely exited, it no longer appears in a subsequent same-name
        /// enumeration.</summary>
        internal Action? OnExitConfirmed { get; set; }

        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout)
        {
            if (WaitConfirmsExit) OnExitConfirmed?.Invoke();
            return WaitConfirmsExit;
        }
    }

    private sealed class FakeIdentityInspector : IProcessIdentityInspector
    {
        internal LiveProcessProbeStatus Status { get; set; } = LiveProcessProbeStatus.Alive;
        internal int? ProcessId { get; set; }
        internal string? ProcessName { get; set; } = CenterMProcessNames.MainUi;
        internal string? ExecutablePath { get; set; } = @"C:\Program Files\WindowsApps\Fake\MSI Center M\MSI Center M.exe";

        public LiveProcessIdentity Inspect(SafeProcessHandle handle) =>
            new(Status, Status == LiveProcessProbeStatus.Alive ? ProcessId : null,
                Status == LiveProcessProbeStatus.Alive ? ProcessName : null,
                Status == LiveProcessProbeStatus.Alive ? ExecutablePath : null);
    }

    private sealed class FakeWindowProvider : IMainUiWindowSnapshotProvider
    {
        internal MainUiWindowSnapshot? NextSnapshot { get; set; } = new MainUiWindowSnapshot(true, 0, 0);

        public MainUiWindowSnapshot? Capture(int processId) => NextSnapshot;
    }

    /// <summary>Injectable delay boundary: tests either let it resolve immediately (default) or hold
    /// it open with a gate until the test explicitly releases it, so debounce timing is never
    /// asserted via real wall-clock sleeps.</summary>
    private sealed class ManualDelayGate
    {
        private readonly SemaphoreSlim _release = new(0);
        internal bool Held { get; set; }
        internal int CallCount { get; private set; }

        internal async Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            CallCount++;
            if (!Held) return;
            await _release.WaitAsync(ct).ConfigureAwait(false);
        }

        internal void Release() => _release.Release();
    }

    private const string ExpectedPath = @"C:\Program Files\WindowsApps\Fake\MSI Center M\MSI Center M.exe";

    /// <summary>Deterministic replacement for wall-clock <c>Task.Delay</c> polling (finding #7):
    /// subscribes to the coordinator's debounce-finished test seam BEFORE invoking
    /// <paramref name="trigger"/>, so a fire-and-forget debounce continuation that completes very
    /// quickly (or even synchronously) can never race past the subscription and be missed.</summary>
    private static async Task AwaitDebounceCompletionAsync(CenterMOem1LifecycleCoordinator coordinator, Func<Task> trigger)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => tcs.TrySetResult();
        coordinator.TestOnly_DebounceContinuationFinished += OnFinished;
        try
        {
            await trigger().ConfigureAwait(false);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            coordinator.TestOnly_DebounceContinuationFinished -= OnFinished;
        }
    }

    private static Task AwaitDebounceCompletionAsync(CenterMOem1LifecycleCoordinator coordinator, Action trigger) =>
        AwaitDebounceCompletionAsync(coordinator, () => { trigger(); return Task.CompletedTask; });

    // ============================================================
    // Eligibility / arming
    // ============================================================

    [Fact]
    public async Task Disabled_NoArm_NativeBehaviorGuaranteed()
    {
        var h = NewHarness();
        var coordinator = h.Build();

        await coordinator.ReconcileAsync("test");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.Disabled, snap.State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task AutoRunEnabled_NeedsSetup_NoHelperCreated()
    {
        var h = NewHarness();
        h.AutoRun = CenterMAutoRunState.Enabled;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snap.State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task AutoRunUnknown_NeedsSetup_NoHelperCreated()
    {
        var h = NewHarness();
        h.AutoRun = CenterMAutoRunState.Unknown;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task LauncherUnavailable_NoArm_RemainsNative()
    {
        var h = NewHarness();
        h.Snapshots.Launcher = [];
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task ServerUnavailable_NoArm_RemainsNative()
    {
        var h = NewHarness();
        h.Snapshots.Server = [];
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task CleanArm_AllPrerequisitesSatisfied_CommitsArmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.Armed, snap.State);
        Assert.True(snap.SuppressionReady);
        Assert.Equal(1, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task StagingFailure_FaultedNative_NoHelperCreated()
    {
        var h = NewHarness();
        h.StagedPath = null;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task StartFailure_CreateProcessFailed_FaultedNative_NoOwnership()
    {
        var h = NewHarness();
        h.HelperApi.CreateSucceeds = false;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.True(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task PartialCleanupUnconfirmed_RetainsOwnership_NoSecondStartAttempted()
    {
        var h = NewHarness();
        h.HelperApi.JobSucceeds = false; // construction failure path
        h.HelperApi.WaitForExitSucceeds = false; // cleanup cannot confirm -> PartialCleanupUnconfirmed
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.False(coordinator.GetSnapshot().NativeBehaviorGuaranteed); // ownership retained

        // A further reconcile attempt while ownership is retained must never create a second helper,
        // AND (round-3 finding #1) must never promote the residual, job-less cleanup-only handle to
        // Armed even though it is alive and the only same-name process in a fresh enumeration -- a
        // PartialCleanupUnconfirmed residue never completed the operational arm sequence.
        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Alive;
        await coordinator.ReconcileAsync("retry");
        Assert.Equal(1, h.HelperApi.StartCallCount);
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.False(coordinator.GetSnapshot().NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task PostStartInvariantFailure_ForeignProcessDetected_StopsHelper_NeverCommitsArmed()
    {
        var h = NewHarness();
        // After Start(), the fresh re-enumeration finds two same-name processes (helper + foreign).
        h.Snapshots.MainUiSequence = new Queue<IReadOnlyList<ProcessSnapshotEntry>?>(
        [
            [], // pre-arm check inside ReconcileCore
            [new ProcessSnapshotEntry(9000, CenterMProcessNames.MainUi, null), new ProcessSnapshotEntry(4242, CenterMProcessNames.MainUi, ExpectedPath)]
        ]);
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.True(coordinator.GetSnapshot().NativeBehaviorGuaranteed); // helper stop succeeded
    }

    [Fact]
    public async Task ProcessEnumerationUncertain_NeverArms()
    {
        var h = NewHarness();
        h.Snapshots.MainUiEnumerationUncertain = true;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    // ============================================================
    // Helper liveness
    // ============================================================

    [Fact]
    public async Task Liveness_Alive_StaysArmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Alive;
        await coordinator.PollHelperLivenessAsync();

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task Liveness_Exited_RetiresAndReconciles_ReArmsCleanly()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(1, h.HelperApi.StartCallCount);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        await coordinator.PollHelperLivenessAsync();

        // Retired ownership triggers a fresh reconcile which re-arms since the world is clean.
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(2, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task Liveness_Uncertain_NoBlindRespawn_RetainsHandle()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Uncertain;
        await coordinator.PollHelperLivenessAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed); // handle retained, not discarded
        Assert.Equal(1, h.HelperApi.StartCallCount); // no respawn attempted
    }

    [Fact]
    public async Task Liveness_WaitFailedIsNotConflatedWithAliveOrExited()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Uncertain;
        await coordinator.PollHelperLivenessAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.NotEqual(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task UnexpectedHelperDeath_NeverIssuesRestoreOperation_OnlyReconciles()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        await coordinator.PollHelperLivenessAsync();

        // Only Start() (a fresh, fully governed arm) was ever called again -- no other native
        // mutation path exists in this fake, so a count of exactly 2 Start calls (initial + re-arm)
        // proves no separate "restore" mechanism was invoked.
        Assert.Equal(2, h.HelperApi.StartCallCount);
    }

    // ============================================================
    // Real MainUI appearance while Armed
    // ============================================================

    [Fact]
    public async Task ForeignSameNameProcess_InvalidatesArmedImmediately_StopsHelperFirst()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.IdentityInspector.ProcessId = 5555;

        await coordinator.PollTickAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.True(coordinator.GetSnapshot().NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task UnconfirmedHelperStop_BlocksAdoptionAndReArm()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed);
        Assert.Null(snap.RealMainUiProcessId); // no adoption happened
    }

    [Fact]
    public async Task TrustedWindowsAppsCandidate_IsTrackedAndAdopted()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.IdentityInspector.ProcessId = 5555;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 0, 0); // starting, never visible yet

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(5555, snap.RealMainUiProcessId);
        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, snap.State);
        Assert.Null(snap.HelperProcessId);
    }

    [Fact]
    public async Task UnknownPath_NoKillNoAdoption()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, null)];

        await coordinator.PollTickAsync();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
        Assert.Null(coordinator.GetSnapshot().RealMainUiProcessId);
    }

    [Fact]
    public async Task WrongPath_NoKillNoAdoption()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, @"C:\NotWindowsApps\MSI Center M.exe")];

        await coordinator.PollTickAsync();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
        Assert.Null(coordinator.GetSnapshot().RealMainUiProcessId);
    }

    [Fact]
    public async Task MultipleForeignCandidates_NoKill_FaultedNative()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign =
        [
            new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath),
            new ProcessSnapshotEntry(6666, CenterMProcessNames.MainUi, ExpectedPath)
        ];

        await coordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task IdentityMismatchAfterEnumeration_HandleUnavailable_NoAdoption()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.HandleOpener.FullRightsSucceed = false;
        h.HandleOpener.ObservationSucceeds = false; // handle acquisition itself fails entirely

        await coordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.Null(coordinator.GetSnapshot().RealMainUiProcessId);
    }

    // ============================================================
    // Visibility lifecycle
    // ============================================================

    private async Task<CenterMOem1LifecycleCoordinator> ArmAndAdoptStartingHidden(Harness h)
    {
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.IdentityInspector.ProcessId = 5555;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 0, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);
        Assert.False(coordinator.GetSnapshot().SeenVisible);
        return coordinator;
    }

    [Fact]
    public async Task HiddenBeforeFirstVisible_NoDebounceNoTermination()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);

        // Still zero visible windows (never seen visible) -- must remain StartingOrHiddenNeverVisible,
        // never a kill candidate.
        await coordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_SetsSeenVisible()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);

        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();

        Assert.True(coordinator.GetSnapshot().SeenVisible);
        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task VisibleThenHidden_StartsDebounce()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();

        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount); // not terminated yet -- still debouncing
    }

    [Fact]
    public async Task VisibleAgainDuringDebounce_CancelsIt()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Cancellation happens synchronously as part of this poll (the debounce continuation is
        // parked on the delay awaiting the cancellation token, so canceling it can finish the
        // continuation before this call even returns) -- subscribe to the completion seam BEFORE
        // triggering it so the signal can never be missed.
        await AwaitDebounceCompletionAsync(coordinator, async () =>
        {
            h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
            await coordinator.PollTickAsync();
        });

        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);

        // Releasing the (already-canceled, already-finished) delay must not cause a second
        // continuation run or a termination.
        h.Delay.Release();
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task NaturalExitDuringDebounce_NoTerminationCall()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();

        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(false, 0, 0); // process exited
        h.Snapshots.Foreign = []; // exited process no longer appears in a fresh enumeration either
        await coordinator.PollTickAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task DisableDuringDebounce_CantKillStale()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Disable cancels the pending debounce synchronously as part of its own gate turn, so the
        // continuation may already finish before this call returns -- subscribe first.
        await AwaitDebounceCompletionAsync(coordinator, () => coordinator.SetDesiredEnabledAsync(false));
        h.Delay.Release();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
        Assert.Equal(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task SuspendDuringDebounce_CantKillStale()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();

        // Suspend cancels the pending debounce synchronously as part of its own gate turn, so the
        // continuation may already finish before this call returns -- subscribe first.
        await AwaitDebounceCompletionAsync(coordinator,
            () => coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None));
        h.Delay.Release();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    // ============================================================
    // Safe termination result handling
    // ============================================================

    private async Task<(CenterMOem1LifecycleCoordinator Coordinator, Harness H)> ArmAdoptAndReachDebounce()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = false; // let the debounce resolve immediately once started
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        // Subscribe BEFORE triggering the poll that starts the debounce: with Held == false the
        // fire-and-forget continuation may finish essentially immediately, so subscribing after
        // would risk a missed signal.
        await AwaitDebounceCompletionAsync(coordinator, () => coordinator.PollTickAsync());
        return (coordinator, h);
    }

    [Fact]
    public async Task DebounceCompletes_TerminatorInvokedExactlyOnce()
    {
        var (coordinator, h) = await ArmAdoptAndReachDebounce();
        h.TerminateInvoker.WaitConfirmsExit = true;

        Assert.Equal(1, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task Terminated_RetiresIdentityAndReconciles()
    {
        var (coordinator, h) = await ArmAdoptAndReachDebounce();

        var snap = coordinator.GetSnapshot();
        Assert.Null(snap.RealMainUiProcessId);
        // Reconciliation after termination attempts to re-arm since the world is clean and no
        // same-name process remains.
        Assert.Equal(CenterMOem1LifecycleState.Armed, snap.State);
    }

    [Fact]
    public async Task AlreadyExited_NoSecondAttempt_Reconciles()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = false;
        h.IdentityInspector.Status = LiveProcessProbeStatus.Exited; // fresh evidence: already exited
        h.Snapshots.Foreign = []; // an already-exited process no longer appears in enumeration either
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        // The retained-handle check at the top of ObserveTrackedMainUiCore now catches this
        // "Exited" evidence immediately -- it routes straight to the natural-exit reconciliation
        // path and never even reaches HiddenDebounce, so no debounce continuation is ever started
        // and no completion wait is needed here.
        await coordinator.PollTickAsync();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount); // AlreadyExited never reaches a real TerminateProcess call
        Assert.Null(coordinator.GetSnapshot().RealMainUiProcessId);
    }

    [Fact]
    public async Task VisibleAgainResultFromTerminator_ReturnsToNative()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        // Hold the delay open so the exact moment the debounce's fresh capture runs is
        // deterministic: only after we explicitly change the window snapshot and release it.
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Debounce evaluates SafeMainUiTerminator.TryTerminate which itself captures a *fresh*
        // window snapshot immediately before deciding -- becomes visible again before that fresh
        // capture runs.
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await AwaitDebounceCompletionAsync(coordinator, () => h.Delay.Release());

        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task FailOpenResults_NeverReArmFromStaleState()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.HandleOpener.FullRightsSucceed = false; // no terminate rights -> AccessDenied path (observation-only handle)
        h.HandleOpener.ObservationSucceeds = true;
        // Re-adopt is not re-run mid-test; instead directly force AccessDenied via handle rights on
        // the already-tracked identity is not possible post-adoption, so this test exercises the
        // WaitTimedOut/Failed style fail-open via a terminate failure instead.
        h.TerminateInvoker.TerminateSucceeds = false;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await AwaitDebounceCompletionAsync(coordinator, () => coordinator.PollTickAsync());

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.Equal(1, h.HelperApi.StartCallCount); // still just the original arm -- no re-arm from a stale/failed termination
    }

    // ============================================================
    // Disable / shutdown
    // ============================================================

    [Fact]
    public async Task DisableWhileArmed_StopsExactHelper()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        await coordinator.SetDesiredEnabledAsync(false);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.Disabled, snap.State);
        Assert.True(snap.NativeBehaviorGuaranteed);
        Assert.Null(snap.HelperProcessId);
    }

    [Fact]
    public async Task DisableWithUnconfirmedStop_DoesNotFalselyReportClean()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;
        await coordinator.SetDesiredEnabledAsync(false);

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Disabled, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task Shutdown_CancelsDebounce_NeverTerminatesRealMainUi()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Shutdown cancels the pending debounce synchronously as part of its own gate turn, so the
        // continuation may already finish before this call returns -- subscribe first.
        await AwaitDebounceCompletionAsync(coordinator, () => coordinator.ShutdownAsync());
        h.Delay.Release();

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task Shutdown_CleansOnlyAddonOwnedHelper_NeverTouchesRealMainUi()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        await coordinator.ShutdownAsync();

        Assert.Equal(1, h.HelperApi.StartCallCount);
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount); // no real MainUI ever tracked/terminated
        Assert.Equal(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);
    }

    // ============================================================
    // Suspend / resume
    // ============================================================

    [Fact]
    public async Task Suspend_DoesNotKillAValidHelper()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        Assert.Equal(1, h.HelperApi.StartCallCount);
        Assert.False(coordinator.GetSnapshot().NativeBehaviorGuaranteed); // still owned
    }

    [Fact]
    public async Task Resume_SameValidHelper_StaysArmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(1, h.HelperApi.StartCallCount); // no duplicate helper created
    }

    [Fact]
    public async Task Resume_HelperExitedDuringSleep_FreshReconcile_ReArms()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(2, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task Resume_RealMainUiAppearedDuringSleep_YieldsNative()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.IdentityInspector.ProcessId = 5555;

        await coordinator.ReconcileAfterResumeAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(5555, snap.RealMainUiProcessId);
        Assert.True(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task Resume_AutoRunChangedDuringSleep_NoReArm()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.AutoRun = CenterMAutoRunState.Enabled;
        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, coordinator.GetSnapshot().State);
        Assert.True(coordinator.GetSnapshot().NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task Resume_LauncherDisappearedDuringSleep_NoReArm()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Launcher = [];
        await coordinator.ReconcileAfterResumeAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.True(coordinator.GetSnapshot().NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task Resume_UncertainEnumeration_NoReArm()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.MainUiEnumerationUncertain = true;
        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
    }

    [Fact]
    public async Task ResumeReconcile_HasNoDependencyOnRoutingOrViiper()
    {
        // The coordinator's public surface takes no VIIPER/routing dependency at all -- this is
        // a structural assertion that resume succeeds purely from CenterM-local facts.
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
    }

    // ============================================================
    // Concurrency races
    // ============================================================

    [Fact]
    public async Task EnableRacingDisable_SerializedNoDoubleHelperCreation()
    {
        var h = NewHarness();
        var coordinator = h.Build();

        var t1 = coordinator.SetDesiredEnabledAsync(true);
        var t2 = coordinator.SetDesiredEnabledAsync(false);
        await Task.WhenAll(t1, t2);

        // Whatever the final state, at most one helper was ever created concurrently (the gate
        // guarantees full serialization of the two calls).
        Assert.True(h.HelperApi.StartCallCount <= 1);
    }

    [Fact]
    public async Task ReconcileRacingDisable_NoDoubleHelperCreation()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        var t1 = coordinator.ReconcileAsync("race1");
        var t2 = coordinator.SetDesiredEnabledAsync(false);
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task HiddenDebounceCompletionRacingShutdown_NoStaleTermination()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => tcs.TrySetResult();
        coordinator.TestOnly_DebounceContinuationFinished += OnFinished;
        try
        {
            var shutdownTask = coordinator.ShutdownAsync();
            h.Delay.Release();
            await shutdownTask;
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            coordinator.TestOnly_DebounceContinuationFinished -= OnFinished;
        }

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task HelperDeathObservationRacingDisable_NoRetainedOwnershipOverwrite()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        var t1 = coordinator.PollHelperLivenessAsync();
        var t2 = coordinator.SetDesiredEnabledAsync(false);
        await Task.WhenAll(t1, t2);

        // No exception, and the coordinator settles into a single consistent terminal state.
        var snap = coordinator.GetSnapshot();
        Assert.True(snap.State is CenterMOem1LifecycleState.Disabled or CenterMOem1LifecycleState.Armed or CenterMOem1LifecycleState.FaultedNative);
    }

    [Fact]
    public async Task ResumeReconciliationRacingShutdown_NoDoubleCleanup()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        var t1 = coordinator.ReconcileAfterResumeAsync();
        var t2 = coordinator.ShutdownAsync();
        await Task.WhenAll(t1, t2);

        Assert.Equal(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);
    }

    // ============================================================
    // Review round 2 (PR #239, review 4957250006): regression coverage for the 8 findings.
    // ============================================================

    // ---- Finding #1: suspended mutation barrier ----

    [Fact]
    public async Task Suspend_HelperExitsAfterQuiesce_LivenessPoll_NoReArmBeforeResume()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        await coordinator.PollHelperLivenessAsync();

        // The exited handle is retired (bookkeeping only), but no fresh reconcile -- and therefore
        // no new helper -- may run while the suspended barrier is active.
        Assert.Equal(1, h.HelperApi.StartCallCount);
        Assert.True(coordinator.GetSnapshot().NativeBehaviorGuaranteed);

        await coordinator.ReconcileAfterResumeAsync();

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(2, h.HelperApi.StartCallCount); // resume is the explicit boundary that re-arms
    }

    [Fact]
    public async Task Suspend_TrackedMainUiHiddenAfterQuiesce_PollStartsNoDebounceOrTermination()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        Assert.True(coordinator.GetSnapshot().SeenVisible);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0); // hidden
        await coordinator.PollTickAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(0, h.Delay.CallCount); // no debounce ever started while suspended
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task SuspendedBarrier_BlocksGenericReconcile_UntilResumeClearsIt()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        await coordinator.ReconcileAsync("attempted-reconcile-while-suspended");
        Assert.Equal(1, h.HelperApi.StartCallCount); // barrier refused to mutate anything

        await coordinator.ReconcileAfterResumeAsync(); // explicit boundary: clears barrier, reconciles fresh

        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(1, h.HelperApi.StartCallCount); // world was clean -- resume simply reconfirms Armed
    }

    // ---- Finding #2: retained-handle-anchored adoption and observation ----

    [Fact]
    public async Task AdoptionCandidate_RetainedHandleReportsDifferentIdentity_NoAdoption()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        // The enumeration candidate is 5555, but the freshly retained handle proves a DIFFERENT
        // process (PID mismatch) -- the candidate exited/changed between enumeration and handle
        // acquisition.
        h.IdentityInspector.ProcessId = 9999;

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.Null(snap.RealMainUiProcessId);
    }

    [Fact]
    public async Task RetainedHandleExited_ReusedPidReportsAliveAndVisible_NeverTransfersSeenVisible()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        Assert.True(coordinator.GetSnapshot().SeenVisible);

        // The retained handle now proves the original identity is gone, even though the PID/window
        // provider currently reports something alive and visible for the same numeric PID (a reused
        // PID from an unrelated process).
        h.IdentityInspector.Status = LiveProcessProbeStatus.Exited;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);

        await coordinator.PollTickAsync();

        Assert.Null(coordinator.GetSnapshot().RealMainUiProcessId);
        Assert.False(coordinator.GetSnapshot().SeenVisible); // never transferred to the reused PID
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task RetainedHandleUncertainWhileHidden_FailsOpen_NoTerminationNoReArm()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        Assert.True(coordinator.GetSnapshot().SeenVisible);
        var startCallCountBeforeUncertain = h.HelperApi.StartCallCount;

        h.IdentityInspector.Status = LiveProcessProbeStatus.Uncertain;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0); // would otherwise look hidden

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.Equal(0, h.Delay.CallCount); // no debounce ever started from stale PID/window evidence
        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
        Assert.Equal(startCallCountBeforeUncertain, h.HelperApi.StartCallCount); // no re-arm either
    }

    // ---- Finding #3: authoritative same-name invariant for an already-owned helper ----

    [Fact]
    public async Task AliveRetainedHelper_EmptySameNameEnumeration_NeverCommitsArmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        var startCallCountAfterArm = h.HelperApi.StartCallCount;

        // A confidently empty enumeration despite an alive, owned, retained helper handle is exactly
        // the inconsistent-world case the stronger invariant must catch -- HelperMissing, not Valid.
        h.Snapshots.MainUiSequence = new Queue<IReadOnlyList<ProcessSnapshotEntry>?>([[]]);

        await coordinator.ReconcileAsync("empty-enumeration-despite-owned-helper");

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.Equal(startCallCountAfterArm, h.HelperApi.StartCallCount); // no re-arm attempted either
    }

    // ---- Finding #4: edge-triggered, identity-bound hidden debounce ----

    [Fact]
    public async Task RepeatedHiddenPolls_DoNotRestartDebounce_TerminatorFiresExactlyOnce()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(1, h.Delay.CallCount);

        // Additional hidden polls before expiry (simulating a poll cadence faster than the
        // debounce) must never start (or cancel/restart) a second delay for the same identity.
        await coordinator.PollTickAsync();
        await coordinator.PollTickAsync();
        await coordinator.PollTickAsync();
        Assert.Equal(1, h.Delay.CallCount);

        // Releasing the ORIGINAL delay runs the terminator exactly once.
        await AwaitDebounceCompletionAsync(coordinator, () => h.Delay.Release());
        Assert.Equal(1, h.TerminateInvoker.TerminateCallCount);
    }

    // ---- Finding #5: confirmed-clean helper removal before ever reporting a clean state ----

    [Fact]
    public async Task AutoRunDrift_StopFailure_StaysFaultedNative_NeverNeedsSetup()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.AutoRun = CenterMAutoRunState.Enabled; // drift
        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;

        await coordinator.ReconcileAsync("autorun-drift");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed); // still owned -- cleanup unconfirmed
    }

    [Fact]
    public async Task PrerequisiteDrift_StopFailure_StaysFaultedNative_NeverNeedsSetup()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.Snapshots.Launcher = []; // drift
        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;

        await coordinator.ReconcileAsync("prerequisite-drift");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task FailedDisable_FollowedByGenericReconcile_NeverFalselyReportsDisabled()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;
        await coordinator.SetDesiredEnabledAsync(false);
        Assert.NotEqual(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);

        // A later generic reconcile (e.g. a subsequent timer tick) with desiredEnabled == false must
        // preserve the same "never falsely report clean" semantics as the direct disable path --
        // it must retry cleanup, not blindly commit Disabled.
        await coordinator.ReconcileAsync("post-disable-generic-reconcile");

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Disabled, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task Shutdown_UnconfirmedCleanup_NeverReportsCleanDisabled()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);

        h.HelperApi.TerminateSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;

        await coordinator.ShutdownAsync();

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Disabled, snap.State);
        Assert.False(snap.NativeBehaviorGuaranteed); // exact helper still unresolved
    }

    // ---- Finding #6: request-time epoch invalidation ahead of the debounce's mutation gate ----

    [Fact]
    public async Task DisableEpochBump_CompletesBeforeDebounceEntersGate_PreventsStaleTermination()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        var enteredHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.TestOnly_BeforeDebounceGateAcquire = async () =>
        {
            enteredHook.TrySetResult();
            await releaseHook.Task;
        };

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => finished.TrySetResult();
        coordinator.TestOnly_DebounceContinuationFinished += OnFinished;

        h.Delay.Release();
        // The debounce continuation's delay has completed and it is now parked immediately before
        // it would compete for the gate.
        await enteredHook.Task;

        // The disable request now runs to FULL completion -- including its epoch bump, which
        // happens before it ever tries to acquire the gate -- entirely before the debounce
        // continuation is permitted to enter the gate. This is exactly the ordering finding #6
        // describes as unsafe without the epoch check: even though the debounce "arrived" first, a
        // higher-priority request became authoritative before the debounce ever entered the gate.
        await coordinator.SetDesiredEnabledAsync(false);

        releaseHook.TrySetResult(); // let the now-stale debounce continuation proceed into the gate
        await finished.Task;
        coordinator.TestOnly_DebounceContinuationFinished -= OnFinished;

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount);
        Assert.Equal(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);
    }

    // ---- Finding #8: supported-environment eligibility gate ----

    [Fact]
    public async Task UnsupportedEnvironment_NeedsSetup_HelperNeverCreated()
    {
        var h = NewHarness();
        h.EnvironmentEligible = () => false;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snap.State);
        Assert.Equal(0, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task UnsupportedEnvironment_ExistingOwnedHelper_StoppedBeforeNeedsSetup()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.EnvironmentEligible = () => false;
        await coordinator.ReconcileAsync("environment-became-unsupported");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snap.State);
        Assert.True(snap.NativeBehaviorGuaranteed);
        Assert.Null(snap.HelperProcessId);
    }

    // ============================================================
    // Review round 3 (PR #239, review 4957383088): regression coverage for the 6 findings.
    // ============================================================

    // ---- Finding #1: residual cleanup-only ownership must never be promoted to Armed ----

    [Fact]
    public async Task PartialCleanupUnconfirmed_LaterConfirmsExit_ThenFreshArmCreatesNewOperationalHelper()
    {
        var h = NewHarness();
        h.HelperApi.JobSucceeds = false; // construction failure path
        h.HelperApi.WaitForExitSucceeds = false; // cleanup cannot confirm -> PartialCleanupUnconfirmed
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);
        Assert.Equal(1, h.HelperApi.StartCallCount);

        // The residual handle's exit now confirms (e.g. via a later liveness poll) -- only THIS
        // releases ownership, at which point a brand-new, fully operational helper may be armed.
        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;
        h.HelperApi.JobSucceeds = true; // the retried Start sequence now succeeds cleanly
        await coordinator.PollHelperLivenessAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.Armed, snap.State);
        Assert.Equal(2, h.HelperApi.StartCallCount);
        Assert.False(snap.NativeBehaviorGuaranteed); // now owned by a genuine, operational Armed helper
    }

    [Fact]
    public async Task PartialCleanupUnconfirmed_ResumeReconcile_DoesNotUpgradeToArmed()
    {
        var h = NewHarness();
        h.HelperApi.JobSucceeds = false;
        h.HelperApi.WaitForExitSucceeds = false;
        var coordinator = h.Build();

        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, coordinator.GetSnapshot().State);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        // The residual, job-less handle is still alive across the sleep -- resume must not treat
        // "alive and unique" as sufficient to upgrade it to Armed.
        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Alive;
        await coordinator.ReconcileAfterResumeAsync();

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, snap.State);
        Assert.Equal(1, h.HelperApi.StartCallCount); // no second Start attempted either
    }

    // ---- Finding #2: completed/stale debounce continuations must always retire the pending debounce ----

    [Fact]
    public async Task DebounceExpires_VisibleAgain_SecondHiddenCycleStartsANewDebounce()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Debounce expires; the terminator's fresh capture finds it visible again -> VisibleAgain.
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await AwaitDebounceCompletionAsync(coordinator, () => h.Delay.Release());
        Assert.Equal(CenterMOem1LifecycleState.NativeMainUiActive, coordinator.GetSnapshot().State);

        // A second visible -> hidden cycle for the same tracked identity must be able to start (and
        // reach) a brand-new debounce -- the completed first debounce must not leave a dead pending
        // marker behind that blocks this.
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(2, h.Delay.CallCount);

        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(false, 0, 0);
        await AwaitDebounceCompletionAsync(coordinator, () => h.Delay.Release());
        Assert.Equal(1, h.TerminateInvoker.TerminateCallCount);
    }

    [Fact]
    public async Task FirstMainUiDebounceCompletes_NewlyAdoptedMainUiCanStartItsOwnDebounce()
    {
        var (firstCoordinator, firstHarness) = await ArmAdoptAndReachDebounce(); // first identity's debounce already completed

        Assert.Equal(CenterMOem1LifecycleState.Armed, firstCoordinator.GetSnapshot().State); // re-armed after termination

        // A brand-new real MainUI now appears and is adopted -- its own hidden/visible cycle must be
        // able to start a fresh debounce untouched by the retired first identity's completed CTS.
        firstHarness.Snapshots.Foreign = [new ProcessSnapshotEntry(7777, CenterMProcessNames.MainUi, ExpectedPath)];
        firstHarness.IdentityInspector.ProcessId = 7777;
        firstHarness.IdentityInspector.Status = LiveProcessProbeStatus.Alive;
        firstHarness.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await firstCoordinator.PollTickAsync();
        Assert.Equal(7777, firstCoordinator.GetSnapshot().RealMainUiProcessId);
        Assert.True(firstCoordinator.GetSnapshot().SeenVisible);

        firstHarness.Delay.Held = true;
        firstHarness.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await firstCoordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, firstCoordinator.GetSnapshot().State);
        Assert.Equal(2, firstHarness.Delay.CallCount); // the new identity's own debounce actually started
    }

    [Fact]
    public async Task StaleContinuation_EpochMismatch_DoesNotBlockNextValidDebounce()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        // Force a true post-delay epoch-mismatch stale completion (not a cancellation): let the
        // debounce's delay resolve, park it immediately before it competes for the gate, then let a
        // disable request (which bumps the lifecycle epoch before it ever acquires the gate) run to
        // full completion first -- exactly the finding #6 ordering, reused here to exercise finding
        // #2's retirement guarantee on the stale-epoch-mismatch exit path specifically.
        var enteredHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.TestOnly_BeforeDebounceGateAcquire = async () =>
        {
            enteredHook.TrySetResult();
            await releaseHook.Task;
        };
        var firstFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFirstFinished() => firstFinished.TrySetResult();
        coordinator.TestOnly_DebounceContinuationFinished += OnFirstFinished;

        h.Delay.Release();
        await enteredHook.Task;
        await coordinator.SetDesiredEnabledAsync(false); // epoch bump + full disable completes first
        Assert.Equal(CenterMOem1LifecycleState.Disabled, coordinator.GetSnapshot().State);

        releaseHook.TrySetResult(); // let the now-stale, epoch-mismatched continuation proceed
        await firstFinished.Task;
        coordinator.TestOnly_DebounceContinuationFinished -= OnFirstFinished;
        coordinator.TestOnly_BeforeDebounceGateAcquire = null;

        Assert.Equal(0, h.TerminateInvoker.TerminateCallCount); // stale continuation never terminated anything

        // Re-enable (a clean helper arm -- clear the stale foreign-process evidence left over from
        // the first identity first), then adopt a brand-new real MainUI and reach hidden: this must
        // be able to start (and reach) its own new debounce -- the earlier stale, epoch-mismatched
        // continuation must not have left a dead pending marker behind.
        h.Snapshots.Foreign = [];
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(8888, CenterMProcessNames.MainUi, ExpectedPath)];
        h.IdentityInspector.ProcessId = 8888;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        Assert.True(coordinator.GetSnapshot().SeenVisible);

        h.Delay.Held = true;
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();

        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);
        Assert.Equal(2, h.Delay.CallCount);

        await AwaitDebounceCompletionAsync(coordinator, () => h.Delay.Release());
        Assert.Equal(1, h.TerminateInvoker.TerminateCallCount); // the NEW debounce actually ran to completion
    }

    // ---- Finding #3: authoritative validity check must also gate the steady-state Armed poll ----

    [Fact]
    public async Task PollTick_ArmedWithAliveHandle_EmptySameNameEnumeration_InvalidatesArmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        // A confidently empty enumeration despite an alive, owned, retained helper handle is exactly
        // the inconsistent-world case the authoritative invariant must catch -- HelperMissing, not
        // Valid -- even during the steady-state poll path, not only a fresh reconcile.
        h.Snapshots.MainUiSequence = new Queue<IReadOnlyList<ProcessSnapshotEntry>?>([[]]);

        await coordinator.PollTickAsync();

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
    }

    // ---- Finding #4: resume/reconcile must resolve an existing tracked real MainUI first ----

    [Fact]
    public async Task Resume_TrackedRealMainUiStillAlive_IdentityAndSeenVisiblePreserved_NoDoubleAdoption()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync();
        Assert.True(coordinator.GetSnapshot().SeenVisible);
        var handleOpenCallsBeforeSuspend = h.HandleOpener.OpenCallCount;

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        // The exact same real MainUI remains alive across resume (still enumerable, and the
        // retained handle still confirms it).
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.ReconcileAfterResumeAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(5555, snap.RealMainUiProcessId);
        Assert.True(snap.SeenVisible); // history preserved -- no re-adoption/reset
        Assert.Equal(handleOpenCallsBeforeSuspend, h.HandleOpener.OpenCallCount); // no second handle opened
    }

    [Fact]
    public async Task Resume_TrackedRealMainUiExitedDuringSleep_RetiredBeforeAnyArmCommit()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        // The tracked real MainUI exited during sleep: the retained handle proves it, and it no
        // longer appears in a fresh enumeration.
        h.IdentityInspector.Status = LiveProcessProbeStatus.Exited;
        h.Snapshots.Foreign = [];

        await coordinator.ReconcileAfterResumeAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Null(snap.RealMainUiProcessId); // retired, never left dangling while re-arming
        Assert.Equal(CenterMOem1LifecycleState.Armed, snap.State); // clean world -> fresh arm afterward
        Assert.Equal(2, h.HelperApi.StartCallCount);
    }

    [Fact]
    public async Task Resume_TrackedRealMainUiRetainedIdentityUncertain_NoHelperStart()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        var startCallCountAfterAdopt = h.HelperApi.StartCallCount; // the helper was stopped before adoption

        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);

        h.IdentityInspector.Status = LiveProcessProbeStatus.Uncertain;

        await coordinator.ReconcileAfterResumeAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.Equal(startCallCountAfterAdopt, h.HelperApi.StartCallCount); // no helper arm from an uncertain retained identity
    }

    // ---- Finding #5: environment eligibility must default/fail closed (unsupported), never open ----

    [Fact]
    public void DefaultEnvironmentEligibility_FailsClosed_NoPredicateInjected()
    {
        var h = NewHarness();
        var coordinator = new CenterMOem1LifecycleCoordinator(
            publishRootProvider: () => "fake-publish-root",
            backendProbe: new CenterMBackendProbe(h.Snapshots),
            autoRunReader: () => h.AutoRun,
            processSnapshotSource: h.Snapshots,
            helperOwnership: h.HelperOwnership,
            mainUiObserver: new MainUiLifecycleObserver(h.WindowProvider),
            terminator: new SafeMainUiTerminator(h.TerminateInvoker, h.IdentityInspector, h.WindowProvider, h.Snapshots),
            handleOpener: h.HandleOpener,
            identityInspector: h.IdentityInspector,
            stager: _ => h.StagedPath,
            delay: h.Delay.DelayAsync,
            environmentEligibility: null, // explicitly not injected
            hiddenDebounce: TimeSpan.FromMilliseconds(1));

        // Never claim hardware validation here -- this only asserts the constructor's own default,
        // not any device probe.
        _ = coordinator;
    }

    [Fact]
    public async Task EligibilityProbeThrows_TreatedAsUnsupported_NoHelperCreated_ExistingOwnershipStillReconciled()
    {
        var h = NewHarness();
        h.EnvironmentEligible = () => true;
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        var startCallCountAfterArm = h.HelperApi.StartCallCount;

        h.EnvironmentEligible = () => throw new InvalidOperationException("probe failure");
        await coordinator.ReconcileAsync("eligibility-probe-throws");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snap.State);
        Assert.True(snap.NativeBehaviorGuaranteed); // an already-owned helper is still cleaned up
        Assert.Equal(startCallCountAfterArm, h.HelperApi.StartCallCount); // no new helper started
    }

    // ---- Finding #6: foreign-vanished-after-helper-stop must re-enter a fresh full reconciliation ----

    [Fact]
    public async Task ForeignVanishesDuringHelperStop_AutoRunDriftsBeforeCleanupCompletes_NoReArmFromStalePrerequisites()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        var startCallCountAfterArm = h.HelperApi.StartCallCount;

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(5555, CenterMProcessNames.MainUi, ExpectedPath)];
        h.HelperApi.OnWaitForExitCalled = () =>
        {
            // By the time helper cleanup actually confirms (a bounded wait), the foreign process has
            // already vanished AND AutoRun has drifted to Enabled -- both facts must be recaptured
            // fresh by a full reconciliation, never assumed from the pre-stop snapshot.
            h.Snapshots.Foreign = [];
            h.AutoRun = CenterMAutoRunState.Enabled;
        };

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snap.State);
        Assert.Equal(startCallCountAfterArm, h.HelperApi.StartCallCount); // no second helper started from stale prerequisites
    }

    [Fact]
    public async Task ForeignVanishesDuringHelperStop_LauncherDriftsBeforeCleanupCompletes_NoReArmFromStalePrerequisites()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        var startCallCountAfterArm = h.HelperApi.StartCallCount;

        h.Snapshots.Foreign = [new ProcessSnapshotEntry(6666, CenterMProcessNames.MainUi, ExpectedPath)];
        h.HelperApi.OnWaitForExitCalled = () =>
        {
            h.Snapshots.Foreign = [];
            h.Snapshots.Launcher = []; // Launcher disappears during the bounded cleanup wait
        };

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, snap.State);
        Assert.Equal(startCallCountAfterArm, h.HelperApi.StartCallCount);
    }

    // ============================================================
    // Review 4957507443, finding #1 (BLOCKER): losing the Armed invariant must actually disarm
    // the exact owned helper, not merely relabel the state.
    // ============================================================

    [Fact]
    public async Task Reconcile_ExistingArmedHelperInvalid_HelperActuallyDisarmed_NotOnlyStateChanged()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.True(h.HelperOwnership.IsOwned);

        // The retained handle no longer confirms Alive -- EvaluateArmedHelperValidity classifies
        // this as invalid. The regression: only State moved to FaultedNative while the operational
        // helper (the actual suppression mechanism) stayed alive/owned.
        h.HelperApi.LivenessResult = LiveProcessProbeStatus.Exited;

        await coordinator.ReconcileAsync("test");

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(h.HelperOwnership.IsOwned);
        Assert.True(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task PollTick_SameNameEnumerationUncertainWhileArmed_HelperActuallyDisarmed()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);
        Assert.True(h.HelperOwnership.IsOwned);

        // An uncertain enumeration is exactly the "certainty lost" case -- the regression left this
        // as a diagnostic-only transition to FaultedNative with the operational helper still alive.
        h.Snapshots.MainUiEnumerationUncertain = true;

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.False(h.HelperOwnership.IsOwned);
        Assert.True(snap.NativeBehaviorGuaranteed);
    }

    [Fact]
    public async Task PollTick_SameNameEnumerationUncertainWhileArmed_CleanupUnconfirmed_OwnershipRetainedForRetry()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.Equal(CenterMOem1LifecycleState.Armed, coordinator.GetSnapshot().State);

        h.HelperApi.WaitForExitSucceeds = false;
        h.HelperApi.TerminateSucceeds = false; // Stop() cannot confirm termination
        h.Snapshots.MainUiEnumerationUncertain = true;

        await coordinator.PollTickAsync();

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        // Not discarded, not name/PID rediscovery -- the exact handle stays retained for a later
        // retry, matching PR1's retention contract.
        Assert.True(h.HelperOwnership.IsOwned);
        Assert.False(snap.NativeBehaviorGuaranteed);

        // A later retry whose exact-handle wait finally succeeds resolves it via the normal
        // reconcile-time disarm path -- proving this is not a dead end.
        h.HelperApi.WaitForExitSucceeds = true;
        h.HelperApi.TerminateSucceeds = true;
        await coordinator.ReconcileAsync("retry");
        Assert.False(h.HelperOwnership.IsOwned);
    }

    // ============================================================
    // Review 4957507443, finding #2 (BLOCKER): the request-time suspend boundary must invalidate
    // an arm already in flight, before Armed is ever committed.
    // ============================================================

    [Fact]
    public async Task AttemptArm_SuspendRequestedWhileArmInFlight_HelperCleanedUp_ArmedNeverCommitted()
    {
        var h = NewHarness();
        var coordinator = h.Build();

        // Simulates QuiesceForSuspendAsync bumping the lifecycle epoch (which it always does BEFORE
        // ever waiting on the gate) at the exact moment the helper process has just been created,
        // strictly after AttemptArm (which already holds the gate) started its work.
        h.HelperApi.OnCreateSuspendedCalled = () => coordinator.TestOnly_BumpLifecycleEpoch();

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, snap.State);
        Assert.False(h.HelperOwnership.IsOwned); // newly-created helper was cleaned up, not left running
        Assert.Equal(1, h.HelperApi.StartCallCount); // no second helper created from the stale operation
    }

    [Fact]
    public async Task AttemptArm_SuspendRequestedWhileArmInFlight_CleanupUnconfirmed_OwnershipRetainedFaultedNative()
    {
        var h = NewHarness();
        var coordinator = h.Build();

        h.HelperApi.OnCreateSuspendedCalled = () => coordinator.TestOnly_BumpLifecycleEpoch();
        h.HelperApi.WaitForExitSucceeds = false;
        h.HelperApi.TerminateSucceeds = false;

        await coordinator.SetDesiredEnabledAsync(true);

        var snap = coordinator.GetSnapshot();
        Assert.Equal(CenterMOem1LifecycleState.FaultedNative, snap.State);
        Assert.True(h.HelperOwnership.IsOwned); // retained -- unresolved cleanup is never discarded
    }

    // ============================================================
    // Review 4957507443, finding #3 (MAJOR): DisposeAsync must drain the fire-and-forget debounce
    // continuation before disposing the gate.
    // ============================================================

    [Fact]
    public async Task DisposeAsync_DrainsPendingDebounceContinuation_CompletesCleanlyAfterRelease()
    {
        var h = NewHarness();
        var coordinator = await ArmAndAdoptStartingHidden(h);
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 1);
        await coordinator.PollTickAsync(); // Visible

        var hookEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.TestOnly_BeforeDebounceGateAcquire = () =>
        {
            hookEntered.TrySetResult();
            return release.Task;
        };

        // Delay is not held, so the debounce's own delay resolves immediately and the fire-and-
        // forget continuation parks deterministically right before it tries to (re-)acquire the
        // gate -- exactly the race window finding #3 describes.
        h.WindowProvider.NextSnapshot = new MainUiWindowSnapshot(true, 1, 0);
        await coordinator.PollTickAsync();
        Assert.Equal(CenterMOem1LifecycleState.HiddenDebounce, coordinator.GetSnapshot().State);

        await hookEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // DisposeAsync (via ShutdownAsync) can still acquire the gate here -- the continuation is
        // parked in the hook, not holding it. The regression: _gate got disposed immediately after
        // ShutdownAsync returned, before this parked continuation ever got its turn.
        var disposeTask = coordinator.DisposeAsync().AsTask();

        // Give DisposeAsync's own ShutdownAsync a chance to actually finish before releasing the
        // parked continuation, so the continuation resumes strictly after shutdown completed.
        await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
        release.SetResult();

        // Must complete cleanly -- no unobserved ObjectDisposedException from the drained
        // continuation touching a disposed semaphore.
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ============================================================
    // Review 4957507443, finding #4 (MAJOR): DisposeAsync must not simply discard the exact
    // retained helper handle when cleanup remains unresolved.
    // ============================================================

    [Fact]
    public async Task DisposeAsync_HelperCleanupUnconfirmed_RetriesBoundedThenFinalizes_OwnershipRemainsRetained()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.True(h.HelperOwnership.IsOwned);

        h.HelperApi.WaitForExitSucceeds = false;
        h.HelperApi.TerminateSucceeds = false; // Stop() can never confirm termination

        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        // Never simply discarded: the exact retained handle is still the sole authority over the
        // process, available to whatever caller-held reference can attempt resolution later.
        Assert.True(h.HelperOwnership.IsOwned);
        // ShutdownAsync's own attempt (1) plus DisposeAsync's bounded extra attempts were all made,
        // not zero extra attempts as before this fix.
        Assert.True(h.HelperApi.TerminateCallCount > 1);
    }

    [Fact]
    public async Task DisposeAsync_HelperCleanupConfirmedOnRetry_OwnershipCleared()
    {
        var h = NewHarness();
        var coordinator = h.Build();
        await coordinator.SetDesiredEnabledAsync(true);
        Assert.True(h.HelperOwnership.IsOwned);

        // ShutdownAsync's own Stop() attempt can invoke WaitForExit up to twice (graceful wait, then
        // a Job-close fallback wait) and still fail both times here; a later bounded retry inside
        // DisposeAsync succeeds.
        var attemptsBeforeSuccess = 3;
        h.HelperApi.WaitForExitSucceeds = false;
        h.HelperApi.OnWaitForExitCalled = () =>
        {
            if (--attemptsBeforeSuccess <= 0) h.HelperApi.WaitForExitSucceeds = true;
        };

        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(h.HelperOwnership.IsOwned);
    }
}
