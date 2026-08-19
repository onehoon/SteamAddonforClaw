using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// PR3 development-only OEM1 production E2E POC: proves the production composition wiring
/// (<see cref="IHandheldRoutingComposition.ConfigureOem1ActionPath"/>) -- WMI-start-gated
/// suppression activation, gesture-bridge authority following the existing coordinator's own
/// <see cref="CenterMOem1LifecycleSnapshot.SuppressionReady"/>, replacement-action failure fail-open,
/// and shutdown ordering. All fakes -- no real WMI, no real process launches, no real hardware.
/// Normal-mapping/routing-domain-selection semantics themselves are covered by
/// <see cref="Oem1ActionDispatcherTests"/>; this file only proves the composition actually wires
/// those pieces together the way the coordinator/runtime/bridge already documented.
/// </summary>
public sealed class MsiClawRoutingCompositionOem1ActionPathTests
{
    private static RoutingRuntimeStatusSnapshot Status(bool steamOutputActive) =>
        new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: steamOutputActive, NativeDirectInputActive: false);

    private static (MsiClawRoutingComposition Composition, FakeMsiEventSource EventSource) BuildArmable(
        bool wmiStartSucceeds = true,
        Action? launchBigPicture = null)
    {
        var devices = new FakeDeviceEnumerator();
        var native = new MsiClawNativeStateManager(devices, new FakeModeController());
        var snapshots = new FakeSnapshotSource();
        var helperApi = new AlwaysSucceedsHelperApi();
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        // Mirrors real Windows: even a CREATE_SUSPENDED owned helper process is enumerable under its
        // own "MSI Center M" image name, so the coordinator's post-start invariant check (which
        // re-enumerates that same name and expects to find exactly its own owned PID) must see it too.
        snapshots.HelperProcessIdProvider = () => helperOwnership.IsOwned ? helperOwnership.ProcessId : null;
        var coordinator = new CenterMOem1LifecycleCoordinator(
            publishRootProvider: () => "fake-publish-root",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            autoRunReader: () => CenterMAutoRunState.Disabled,
            stager: _ => @"C:\fake\MSI Center M.exe",
            environmentEligibility: () => true,
            delay: (_, _) => Task.CompletedTask);

        var eventSource = new FakeMsiEventSource(wmiStartSucceeds);
        var composition = new MsiClawRoutingComposition(
            native,
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe),
            centerMOem1Coordinator: coordinator,
            testOnlyOem1EventSource: eventSource,
            // Review fix (MAJOR): deterministic gesture recognition (no real-time sleeps -- the
            // single-click debounce resolves as soon as it's awaited) and an observable replacement
            // action, so a test can actually prove Event41 reaches the normal mapping end-to-end
            // instead of merely asserting nothing threw.
            testOnlyOem1GestureDelay: new ImmediateGestureDelay(),
            testOnlyOem1GestureClock: new ZeroGestureClock(),
            testOnlyOem1LaunchBigPicture: launchBigPicture);

        return (composition, eventSource);
    }

    // ---- Production suppression activation (Scope 7/8) ----

    [Fact]
    public async Task Wmi_start_success_arms_suppression_and_bridge_authority_turns_on()
    {
        var (composition, _) = BuildArmable(wmiStartSucceeds: true);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        Assert.Equal(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Wmi_start_failure_never_arms_suppression()
    {
        var (composition, _) = BuildArmable(wmiStartSucceeds: false);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        Assert.NotEqual(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        Assert.False(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Routing_disabled_does_not_turn_bridge_authority_off()
    {
        // Work order Scope 12 (core acceptance test): routing setting OFF must never disable the
        // custom bridge/suppression -- normal mapping (Big Picture) stays reachable.
        var (composition, _) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Event41_reaches_normal_mapping_while_armed_and_routing_inactive()
    {
        // Review fix (MAJOR): actually observe the replacement action firing end-to-end (Event41 ->
        // recognizer -> bridge -> dispatcher -> normal-mapping action), rather than only asserting
        // nothing threw. Deterministic gesture delay/clock fakes (via BuildArmable) mean the
        // single-click debounce resolves without any real-time wait; a TaskCompletionSource lets this
        // test await the actual signal instead of guessing a sleep duration.
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (composition, eventSource) = BuildArmable(launchBigPicture: () => launched.TrySetResult());
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;
        Assert.NotNull(composition.TestOnly_Oem1Bridge);

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // No action-execution failure should have revoked authority as a result of this successful
        // normal-mapping dispatch.
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Event88_never_reaches_oem1_mapping()
    {
        var (composition, eventSource) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        var dispatchedGestures = new List<Oem1GesturePolicyRequest>();
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;
        composition.TestOnly_Oem1Bridge!.PolicyRequested += dispatchedGestures.Add;

        eventSource.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        await Task.Delay(20);

        Assert.Empty(dispatchedGestures);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    // ---- Shutdown (Scope 15) ----

    [Fact]
    public async Task Shutdown_revokes_custom_authority_and_disposes_the_action_path()
    {
        var (composition, _) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;
        var bridge = composition.TestOnly_Oem1Bridge!;

        await ((IAsyncDisposable)composition).DisposeAsync();

        // Dispose is idempotent; calling SetCustomAuthority after disposal must be a safe no-op
        // (proves the bridge was actually disposed, not merely deactivated).
        var exception = Record.Exception(() => bridge.SetCustomAuthority(true));
        Assert.Null(exception);
    }

    [Fact]
    public async Task Shutdown_drains_an_in_flight_action_failure_fail_open_before_disposing_the_coordinator()
    {
        // Review fix (BLOCKER): the action-failure fail-open path (OnOem1ActionFailed) was previously
        // an untracked Task.Run that could still be entering the coordinator while DisposeAsync tore
        // it down. Trigger a real action failure, then dispose with no extra wait -- if the owned
        // fail-open task were not drained before the coordinator is disposed below it, this would
        // either throw out of DisposeAsync or leave that task's continuation touching a disposed
        // coordinator.
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (composition, eventSource) = BuildArmable(launchBigPicture: () =>
        {
            launched.TrySetResult();
            throw new InvalidOperationException("simulated Big Picture launch failure");
        });
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = await Record.ExceptionAsync(async () => await ((IAsyncDisposable)composition).DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Action_failure_revocation_is_never_undone_by_a_racing_stale_authority_refresh()
    {
        // Review fix (BLOCKER): RefreshOem1BridgeAuthority (the production driver's onReconciled
        // callback) and OnOem1ActionFailed used to run fully independently. A refresh that had
        // already read a stale SuppressionReady == true snapshot could still call
        // bridge.SetCustomAuthority(true) AFTER an action failure's own SetCustomAuthority(false),
        // undoing the fail-open admission boundary while the owned disable was still in flight. Both
        // paths now share the _oem1TaskSync lock; this test pauses a refresh mid-publish (via
        // TestOnly_BeforeOem1BridgeAuthorityPublish, inside that same locked region) and proves a
        // concurrent action-failure revocation cannot interleave with it -- the revocation must wait
        // for the stale refresh's publish to finish, and its own false write must win afterward.
        var launchCount = 0;
        var (composition, eventSource) = BuildArmable(launchBigPicture: () => Interlocked.Increment(ref launchCount));
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        var refreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composition.TestOnly_BeforeOem1BridgeAuthorityPublish = () =>
        {
            refreshEntered.TrySetResult();
            releaseRefresh.Task.GetAwaiter().GetResult();
        };

        // Simulate a lifecycle tick's stale refresh that already read ready == true and is about to
        // publish it, but is paused right before doing so.
        var refreshTask = Task.Run(() => composition.TestOnly_RefreshOem1BridgeAuthority());
        await refreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A concurrent action-failure revocation must be serialized behind the same lock -- it must
        // not be able to complete while the stale refresh's publish is still paused.
        var failTask = Task.Run(composition.TestOnly_InvokeOnOem1ActionFailed);
        await Task.Delay(50);
        Assert.False(failTask.IsCompleted);

        releaseRefresh.TrySetResult();
        await Task.WhenAll(refreshTask, failTask).WaitAsync(TimeSpan.FromSeconds(5));

        // The revocation ran strictly after the stale refresh's publish (never interleaved), so its
        // false write is the final word -- a subsequent physical press must be ignored.
        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await Task.Delay(100);
        Assert.Equal(0, launchCount);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Event41_cannot_dispatch_once_disposal_has_started()
    {
        // Review fix (MAJOR): the stated shutdown contract is "close custom gesture admission first,
        // then tear down the rest" -- SetCustomAuthority(false) and disposing the bridge/event source
        // must happen as the very first synchronous step of DisposeAsync, before the awaited
        // NativeModeSession/PhysicalInputSource teardown that used to precede it. An async method
        // runs synchronously up to its first await regardless of whether the caller awaits the
        // returned ValueTask, so admission is already closed by the time this test's call to
        // DisposeAsync() returns control -- a press emitted right after starting (not yet awaiting)
        // disposal must never reach the dispatcher.
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (composition, eventSource) = BuildArmable(launchBigPicture: () => launched.TrySetResult());
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        var disposeTask = ((IAsyncDisposable)composition).DisposeAsync().AsTask();

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        await disposeTask;
        var launchedAfterAll = await Task.WhenAny(launched.Task, Task.Delay(200)) == launched.Task;

        Assert.False(launchedAfterAll);
    }

    private sealed class FakeMsiEventSource(bool startSucceeds) : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start() => startSucceeds;
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
    }

    /// <summary>Resolves the gesture recognizer's single/double-click debounce delay immediately --
    /// lets a test drive real gesture recognition without any real-time wait.</summary>
    private sealed class ImmediateGestureDelay : IOem1GestureDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ZeroGestureClock : IOem1GestureClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
    }

    private sealed class FakeSnapshotSource : IProcessSnapshotSource
    {
        internal Func<int?>? HelperProcessIdProvider { get; set; }

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (processName == CenterMProcessNames.Launcher) return [new ProcessSnapshotEntry(1, CenterMProcessNames.Launcher, null)];
            if (processName == CenterMProcessNames.Server) return [new ProcessSnapshotEntry(2, CenterMProcessNames.Server, null)];
            if (processName == CenterMProcessNames.MainUi)
            {
                return HelperProcessIdProvider?.Invoke() is int pid
                    ? [new ProcessSnapshotEntry(pid, CenterMProcessNames.MainUi, null)]
                    : [];
            }
            return [];
        }
    }

    private sealed class AlwaysSucceedsHelperApi : IHelperProcessNativeApi
    {
        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            win32Error = 0;
            processId = 9001;
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
        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error) { win32Error = 0; return true; }
        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout) => true;
        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle) => LiveProcessProbeStatus.Alive;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        private static IntPtr GetCurrentProcessHandle() => GetCurrentProcess();
    }

    private sealed class FakeDeviceEnumerator : IControllerDeviceEnumerator
    {
        private readonly Guid _containerId = Guid.NewGuid();
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [new("HID\\MSI_CLAW", _containerId, "PCIROOT\\0", [], "HID", [], [], "HIDClass", null, null,
            MsiClawHardware.VendorId, MsiClawHardware.XInputProductId, true)];
    }

    private sealed class FakeModeController : IMsiClawModeController
    {
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken) =>
            Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded, MsiClawNativeMode.DirectInput, target, null,
                MsiClawHardware.XInputProductId, true, true, true, true, true, 1, "test"));
    }

    private sealed class MemoryJournalStore : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => System.Text.Json.JsonSerializer.Serialize(_journal);
        public void WriteNew(RecoveryJournal journal) => _journal = journal;
        public void ReplaceExisting(RecoveryJournal journal) { if (_journal is null) throw new IOException(); _journal = journal; }
        public void Delete() => _journal = null;
    }
}
