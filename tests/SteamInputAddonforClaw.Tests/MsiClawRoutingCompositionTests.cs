using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawRoutingCompositionTests
{
    [Fact]
    public async Task SafetySession_ControllerStateSource_and_SessionBoundaryParticipant_are_the_same_owned_instances()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        IHandheldRoutingComposition handheld = composition;

        Assert.Same(composition.NativeModeSession, handheld.SafetySession);
        Assert.Same(composition.PhysicalInputSource, handheld.ControllerStateSource);
        Assert.Same(composition.NativeModeSession, Assert.Single(handheld.SessionBoundaryParticipants));
        Assert.Same(composition.PhysicalRumbleSink, handheld.PhysicalRumbleSink);
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_owned_native_mode_session()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        Assert.True(await composition.NativeModeSession.ObserveRoutingDecisionAsync(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), 1));
        Assert.True(composition.NativeModeSession.IsActive);

        await ((IAsyncDisposable)composition).DisposeAsync();

        // DisposeAsync must reach the owned NativeModeSession -- observable via IsActive flipping
        // false, the same state MsiClawNativeModeSessionCoordinator.DisposeAsync produces when
        // called directly (it stops native mode before releasing its gate).
        Assert.False(composition.NativeModeSession.IsActive);
    }

    [Fact]
    public async Task DisposeAsync_completes_without_throwing_when_the_physical_input_source_was_never_started()
    {
        // MsiClawRoutingComposition constructs its own MsiClawInputSource against the real
        // Vortice DirectInput enumerator factory (not fakeable without an invasive production
        // seam), so its disposal here is exercised only in the default never-started state --
        // the sequential two-line DisposeAsync implementation is otherwise reviewed by inspection.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        await ((IAsyncDisposable)composition).DisposeAsync();

        Assert.False(composition.PhysicalInputSource.IsRunning);
    }

    [Fact]
    public async Task CenterMGuardStage_is_included_in_the_generic_stage_list()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        IHandheldRoutingComposition handheld = composition;

        Assert.Contains(composition.CenterMGuardStage, handheld.Stages);
        Assert.Equal(RoutingStageKind.CenterMGuard, composition.CenterMGuardStage.Kind);
    }

    [Fact]
    public async Task Composition_uses_the_injected_shared_helper_ownership_as_the_default_guards_authority()
    {
        // PR1 ownership convergence: the composition must be the single authority that constructs
        // CenterMHelperOwnership and hand THAT SAME instance to its default CenterMMainUiRoutingGuard
        // via constructor injection -- never a second, unrelated production instance. The guard-level
        // arm/borrow behavior itself is covered by CenterMMainUiRoutingGuardTests.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var helperOwnership = new CenterMHelperOwnership(new BlockingTerminateHelperApi(new ManualResetEventSlim(true)));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), centerMHelperOwnership: helperOwnership);

        Assert.Same(helperOwnership, composition.CenterMHelperOwnership);

        await ((IAsyncDisposable)composition).DisposeAsync();

        // Sole final disposer: composition disposal reaches the shared instance it created (a no-op
        // here since nothing was ever started -- this only proves DisposeAsync flows through to it
        // without throwing).
        Assert.False(helperOwnership.IsOwned);
    }

    [Fact]
    public async Task DisposeAsync_does_not_dispose_a_caller_injected_shared_helper_ownership()
    {
        // Review fix: a caller-injected CenterMHelperOwnership (the future OEM1 sharing seam) must
        // remain the injecting caller's terminal responsibility. CenterMHelperOwnership.Dispose()
        // actively terminates an owned helper, so if composition disposal called it unconditionally,
        // it could terminate a helper a second terminal owner (e.g. a future
        // CenterMOem1LifecycleCoordinator sharing this same instance) still needs operational.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var helperApi = new BlockingTerminateHelperApi(new ManualResetEventSlim(true));
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.Started, helperOwnership.Start(@"C:\fake\MSI Center M.exe"));
        // PR2: the composition's own default OEM1 coordinator would otherwise share this SAME
        // instance and (correctly, as part of its own pre-existing/unmodified terminal cleanup)
        // attempt to Stop it during composition disposal -- unrelated to this test's actual point
        // (that composition itself never calls CenterMHelperOwnership.Dispose() on a caller-injected
        // instance). Inject an OEM1 coordinator with its own unshared ownership so this test keeps
        // isolating the one behavior it targets.
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), centerMHelperOwnership: helperOwnership,
            centerMOem1Coordinator: new CenterMOem1LifecycleCoordinator(() => AppContext.BaseDirectory), startCenterMOem1LifecycleRuntime: false);

        await ((IAsyncDisposable)composition).DisposeAsync();

        // The composition's own default guard never armed, so it never touched the shared instance
        // either -- this proves composition disposal itself skips CenterMHelperOwnership.Dispose()
        // for an instance it did not create.
        Assert.True(helperOwnership.IsOwned);
    }

    [Fact]
    public async Task DisposeAsync_disarms_the_guard_even_when_arm_never_ran()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var guard = new CenterMMainUiRoutingGuard(
            processSnapshotSource: new AlwaysEmptySnapshotSource(),
            stager: _ => null);
        // Staging is forced to fail so this never touches a real DirectInput/Win32 helper -- the
        // point of this test is only that the unarmed/never-attempted Dispose path is reachable
        // and idempotent-safe, not the arm sequence itself (covered by CenterMMainUiRoutingGuardTests).
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), centerMGuard: guard);

        var exception = await Record.ExceptionAsync(async () => await ((IAsyncDisposable)composition).DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_disposes_native_mode_before_disarming_the_guard()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var terminateBlocker = new ManualResetEventSlim(false);
        var helperApi = new BlockingTerminateHelperApi(terminateBlocker);
        var guard = new CenterMMainUiRoutingGuard(
            // First call (before helper start) sees no same-name process; the second (post-mutex
            // invariant re-check) sees exactly the owned helper's fixed PID.
            processSnapshotSource: new SequencedSnapshotSource([[], [new ProcessSnapshotEntry(7777, "MSI Center M", null)]]),
            helperOwnership: new CenterMHelperOwnership(helperApi),
            mutexOwnership: new CenterMMainUiMutexOwnership(new NoOpMutexFactory()),
            stager: _ => @"C:\fake\MSI Center M.exe");
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), centerMGuard: guard);

        Assert.True(await composition.NativeModeSession.ObserveRoutingDecisionAsync(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), 1));
        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

        var disposing = ((IAsyncDisposable)composition).DisposeAsync().AsTask();

        Assert.True(helperApi.TerminateEntered.Wait(TimeSpan.FromSeconds(5)));
        // The guard's helper-stop (inside DisarmAsync) is still in flight here -- if DisposeAsync's
        // ordering is correct, NativeModeSession.DisposeAsync must have already run to completion
        // before DisarmAsync was ever reached.
        Assert.False(composition.NativeModeSession.IsActive);

        terminateBlocker.Set();
        await disposing;
    }

    /// <summary>Always succeeds except <see cref="TryTerminate"/>, which blocks on the supplied
    /// signal so a test can observe composition-disposal state while the guard's helper-stop is
    /// still in flight.</summary>
    private sealed class BlockingTerminateHelperApi(ManualResetEventSlim terminateBlocker) : IHelperProcessNativeApi
    {
        internal ManualResetEventSlim TerminateEntered { get; } = new(false);

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            win32Error = 0;
            processId = 7777;
            processHandle = new SafeProcessHandle(GetCurrentProcessHandle(), false);
            threadHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
        {
            win32Error = 0;
            jobHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error) { win32Error = 0; return true; }
        public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error) { win32Error = 0; return true; }
        public bool TryResumeThread(SafeHandle threadHandle, out int win32Error) { win32Error = 0; return true; }

        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error)
        {
            TerminateEntered.Set();
            terminateBlocker.Wait(TimeSpan.FromSeconds(10));
            win32Error = 0;
            return true;
        }

        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout) => true;
        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle) => LiveProcessProbeStatus.Alive;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        private static IntPtr GetCurrentProcessHandle() => GetCurrentProcess();
    }

    private sealed class NoOpMutexFactory : ICenterMMainUiMutexFactory
    {
        public (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name) => (new Handle(), true);
        private sealed class Handle : ICenterMMainUiMutexHandle { public void Dispose() { } }
    }

    private sealed class AlwaysEmptySnapshotSource : IProcessSnapshotSource
    {
        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName) => [];
    }

    private sealed class SequencedSnapshotSource(IEnumerable<IReadOnlyList<ProcessSnapshotEntry>> snapshots) : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshotEntry>> _snapshots = new(snapshots);
        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName) =>
            _snapshots.Count > 0 ? _snapshots.Dequeue() : [];
    }

    [Fact]
    public async Task Normal_stop_reason_does_not_report_a_runtime_fault()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        var reasons = new List<string>();
        ((IHandheldRoutingComposition)composition).SetRuntimeFaultHandler((reason, _, _) => { reasons.Add(reason); return ValueTask.CompletedTask; });

        composition.OnPhysicalInputCompleted(composition, StoppedSummary(MsiClawInputStopReason.Stopped));
        await Task.Delay(50);

        Assert.Empty(reasons);
    }

    [Fact]
    public async Task Fatal_stop_reason_before_ownership_was_ever_committed_does_not_report_a_runtime_fault()
    {
        // PhysicalInputStage.CurrentIdentity is null here because ownership was never committed
        // (no ExecuteMutationAsync ran) -- this is the natural state without real DirectInput
        // hardware, and exercises the "failure before routing entered" path from the work order.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        Assert.Null(composition.PhysicalInputStage.CurrentIdentity);
        var reasons = new List<string>();
        ((IHandheldRoutingComposition)composition).SetRuntimeFaultHandler((reason, _, _) => { reasons.Add(reason); return ValueTask.CompletedTask; });

        composition.OnPhysicalInputCompleted(composition, StoppedSummary(MsiClawInputStopReason.ReadStateFailed));
        await Task.Delay(50);

        Assert.Empty(reasons);
    }

    [Fact]
    public async Task A_throwing_runtime_fault_handler_does_not_escape_a_dispatched_fault()
    {
        // ReportRuntimeFault is the actual dispatch point OnPhysicalInputCompleted's fatal branch
        // calls -- exercised directly here (internal seam) because driving a real fatal+owned
        // dispatch requires committed PhysicalInputStage ownership, which needs real DirectInput
        // hardware (see the pre-ownership tests above). The exception-containment logic itself does
        // not depend on which fatal condition triggered the dispatch.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        var handlerInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IHandheldRoutingComposition)composition).SetRuntimeFaultHandler((_, _, _) =>
        {
            handlerInvoked.TrySetResult();
            throw new InvalidOperationException("handler bug");
        });

        var exception = Record.Exception(() => composition.ReportRuntimeFault(MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason));
        await handlerInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Runtime_fault_handler_forwards_explicit_safe_cleanup_reentry_policy()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        var calls = new List<(string Reason, bool Yield, bool Retry)>();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IHandheldRoutingComposition)composition).SetRuntimeFaultHandler((reason, yieldCurrentSteamSession, retry) =>
        {
            calls.Add((reason, yieldCurrentSteamSession, retry));
            dispatched.TrySetResult();
            return ValueTask.CompletedTask;
        });

        composition.ReportRuntimeFault(
            MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason,
            retryCurrentSessionAfterSafeCleanup: true);
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        composition.ReportRuntimeFault(
            MsiClawPhysicalInputFaultPolicy.ExternalNativeTakeoverReason,
            yieldCurrentSteamSession: true);
        composition.ReportRuntimeFault("SharedHelperSafetyFault");
        await Task.Delay(50);

        Assert.Equal([
            (MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason, false, true),
            (MsiClawPhysicalInputFaultPolicy.ExternalNativeTakeoverReason, true, false),
            ("SharedHelperSafetyFault", false, false)], calls);
    }

    private static MsiClawInputTestSummary StoppedSummary(MsiClawInputStopReason stopReason) =>
        new(TestSession: 1, DurationMs: 1, M1Observed: false, M2Observed: false, Independent: false, ReadFailures: 0, CleanupSucceeded: true, stopReason);

    private sealed class FakeDeviceEnumerator(MsiClawNativeMode initialMode) : IControllerDeviceEnumerator
    {
        private readonly Guid _containerId = Guid.NewGuid();
        private readonly string _parent = "PCIROOT\\0";
        public MsiClawNativeMode Mode { get; set; } = initialMode;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [new("HID\\MSI_CLAW", _containerId, _parent, [], "HID", [], [], "HIDClass", null, null,
            MsiClawHardware.VendorId, Mode == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId, true)];
    }

    private sealed class FakeModeController(FakeDeviceEnumerator devices) : IMsiClawModeController
    {
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            devices.Mode = target;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, true, 1, "test"));
        }
    }

    private sealed class MemoryJournalStore : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => JsonSerializer.Serialize(_journal);
        public void WriteNew(RecoveryJournal journal) => _journal = journal;
        public void ReplaceExisting(RecoveryJournal journal) { if (_journal is null) throw new IOException(); _journal = journal; }
        public void Delete() => _journal = null;
    }
}
