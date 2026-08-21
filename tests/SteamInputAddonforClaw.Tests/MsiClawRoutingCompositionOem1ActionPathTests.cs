using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
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

    private static (MsiClawRoutingComposition Composition, FakeMsiEventSource EventSource, FakeOem1MappingPreference Mapping) BuildArmable(
        bool wmiStartSucceeds = true,
        Action? launchBigPicture = null,
        Oem1MappingSettings? initialMapping = null,
        bool hardwareSupported = true,
        IOem1GestureDelay? gestureDelay = null)
    {
        var mapping = new FakeOem1MappingPreference(initialMapping ?? Oem1MappingSettings.Default);
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
            stager: _ => @"C:\fake\MSI Center M.exe",
            environmentEligibility: () => true,
            delay: (_, _) => Task.CompletedTask);

        var eventSource = new FakeMsiEventSource(wmiStartSucceeds);
        var composition = new MsiClawRoutingComposition(
            native,
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe),
            hardwareSupported: hardwareSupported,
            centerMOem1Coordinator: coordinator,
            testOnlyOem1EventSource: eventSource,
            // Review fix (MAJOR): deterministic gesture recognition (no real-time sleeps -- the
            // single-click debounce resolves as soon as it's awaited) and an observable replacement
            // action, so a test can actually prove Event41 reaches the normal mapping end-to-end
            // instead of merely asserting nothing threw.
            testOnlyOem1GestureDelay: gestureDelay ?? new ImmediateGestureDelay(),
            testOnlyOem1GestureClock: new ZeroGestureClock(),
            testOnlyOem1LaunchBigPicture: launchBigPicture);

        return (composition, eventSource, mapping);
    }

    [Fact]
    public async Task Default_normal_single_dispatches_without_scheduling_a_delay()
    {
        var delay = new TrackingGestureDelay();
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (composition, eventSource, mapping) = BuildArmable(launchBigPicture: () => launched.TrySetResult(), gestureDelay: delay);
        await ((IHandheldRoutingComposition)composition).ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, delay.ScheduleCount);
        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Default_routing_quick_access_dispatches_without_scheduling_a_delay()
    {
        var delay = new TrackingGestureDelay();
        var requests = 0;
        var (composition, eventSource, mapping) = BuildArmable(gestureDelay: delay);
        await ((IHandheldRoutingComposition)composition).ConfigureOem1ActionPath(() => Status(true), () => requests++, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await Task.Delay(50);

        Assert.Equal(1, requests);
        Assert.Equal(0, delay.ScheduleCount);
        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    // ---- Production suppression activation (Scope 7/8) ----

    // ---- Supported-MSI-Claw hardware availability gate (PR #256 follow-up) ----

    [Fact]
    public async Task Supported_hardware_allows_the_OEM1_action_path_to_activate()
    {
        var (composition, eventSource, mapping) = BuildArmable(hardwareSupported: true);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        Assert.True(eventSource.StartCalled);
        Assert.Equal(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Unsupported_hardware_never_starts_WMI_observation_or_enables_the_lifecycle()
    {
        // RemappingEnabled is deliberately true and persisted: the hardware gate must suppress the
        // FEATURE, never rewrite the user's saved settings.
        var enabled = Oem1MappingSettings.Default with { RemappingEnabled = true };
        var (composition, eventSource, mapping) = BuildArmable(hardwareSupported: false, initialMapping: enabled);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        Assert.False(eventSource.StartCalled);
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        Assert.False(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);
        // Nothing was wired, so no gesture bridge exists to admit a Center M press either.
        Assert.Null(composition.TestOnly_Oem1Bridge);
        // Persistence guarantee: the saved mapping is exactly what it was, so the same settings used
        // later on real Claw hardware still carry the user's bindings and switch.
        Assert.Equal(enabled, mapping.Oem1Mapping);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Unsupported_hardware_ignores_a_later_remapping_switch_change_without_rewriting_it()
    {
        var (composition, eventSource, mapping) = BuildArmable(
            hardwareSupported: false,
            initialMapping: Oem1MappingSettings.Default with { RemappingEnabled = false });
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        var turnedOn = Oem1MappingSettings.Default with { RemappingEnabled = true };
        mapping.Set(turnedOn);
        await composition.TestOnly_Oem1ActivationTask;

        Assert.False(eventSource.StartCalled);
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        // The switch the user set stays set; only the runtime effect is withheld.
        Assert.Equal(turnedOn, mapping.Oem1Mapping);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }


    [Fact]
    public async Task Wmi_start_success_arms_suppression_and_bridge_authority_turns_on()
    {
        var (composition, _, mapping) = BuildArmable(wmiStartSucceeds: true);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        Assert.Equal(CenterMOem1LifecycleState.Armed, composition.CenterMOem1Coordinator.GetSnapshot().State);
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Wmi_start_failure_never_arms_suppression()
    {
        var (composition, _, mapping) = BuildArmable(wmiStartSucceeds: false);
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
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
        var (composition, _, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
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
        var (composition, eventSource, mapping) = BuildArmable(launchBigPicture: () => launched.TrySetResult());
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
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
        var (composition, eventSource, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        var dispatchedGestures = new List<Oem1GesturePolicyRequest>();
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;
        composition.TestOnly_Oem1Bridge!.PolicyRequested += dispatchedGestures.Add;

        eventSource.Emit(new MsiOemEvent(88, CenterMOemCode.Oem2));
        await Task.Delay(20);

        Assert.Empty(dispatchedGestures);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    // ---- Global remapping switch ----

    [Fact]
    public async Task Remapping_off_at_startup_never_arms_suppression_and_leaves_native_center_m()
    {
        var (composition, _, mapping) = BuildArmable(initialMapping: Oem1MappingSettings.Default with { RemappingEnabled = false });
        IHandheldRoutingComposition handheld = composition;

        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        var snapshot = composition.CenterMOem1Coordinator.GetSnapshot();
        Assert.NotEqual(CenterMOem1LifecycleState.Armed, snapshot.State);
        Assert.False(snapshot.SuppressionReady);
        Assert.True(snapshot.NativeBehaviorGuaranteed);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Initial_OEM1_activation_is_deferred_until_after_composition_returns()
    {
        var (composition, eventSource, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        eventSource.BeforeStart = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };

        var activation = handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(activation.IsCompleted);

        release.Set();
        await activation;
        Assert.True(eventSource.StartCalled);
        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Turning_remapping_off_disables_suppression_and_turning_it_on_again_re_arms()
    {
        var (composition, _, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        mapping.Set(mapping.Oem1Mapping with { RemappingEnabled = false });
        await composition.TestOnly_Oem1ActivationTask;

        var disabled = composition.CenterMOem1Coordinator.GetSnapshot();
        Assert.False(disabled.SuppressionReady);
        Assert.True(disabled.NativeBehaviorGuaranteed);
        // The mappings themselves are untouched by the lifecycle transition.
        Assert.Equal(Oem1Action.SteamBigPicture, mapping.Oem1Mapping.NormalSingle.Action);

        mapping.Set(mapping.Oem1Mapping with { RemappingEnabled = true });
        await composition.TestOnly_Oem1ActivationTask;

        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Editing_a_slot_binding_never_disturbs_the_suppression_lifecycle()
    {
        var (composition, _, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;
        var armedPid = composition.CenterMOem1Coordinator.GetSnapshot().HelperProcessId;
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        mapping.Set(mapping.Oem1Mapping with { NormalDouble = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) });
        await composition.TestOnly_Oem1ActivationTask;

        var snapshot = composition.CenterMOem1Coordinator.GetSnapshot();
        Assert.True(snapshot.SuppressionReady);
        // Same exact owned helper: no disarm/re-arm cycle was triggered by a pure mapping edit.
        Assert.Equal(armedPid, snapshot.HelperProcessId);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task A_routing_transition_alone_never_touches_the_suppression_lifecycle()
    {
        // Work order requirement: routing start/stop only changes which mapping DOMAIN the next
        // gesture resolves in -- it must never arm, disarm, or reinitialize OEM1 suppression.
        var steamOutputActive = false;
        var (composition, _, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(steamOutputActive), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;
        var before = composition.CenterMOem1Coordinator.GetSnapshot();

        steamOutputActive = true;
        await Task.Delay(20);

        var after = composition.CenterMOem1Coordinator.GetSnapshot();
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.HelperProcessId, after.HelperProcessId);
        Assert.True(after.SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    // ---- Shutdown (Scope 15) ----

    [Fact]
    public async Task Shutdown_revokes_custom_authority_and_disposes_the_action_path()
    {
        var (composition, _, mapping) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
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
        var (composition, eventSource, mapping) = BuildArmable(launchBigPicture: () =>
        {
            launched.TrySetResult();
            throw new InvalidOperationException("simulated Big Picture launch failure");
        });
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;

        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = await Record.ExceptionAsync(async () => await ((IAsyncDisposable)composition).DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Real_action_failure_never_deadlocks_against_a_concurrent_stale_authority_refresh()
    {
        // Review fix (BLOCKER): lock-order inversion. Oem1EventGestureBridge.OnGestureRecognized
        // deliberately holds _recognizerOperationGate while invoking PolicyRequested, so a REAL
        // replacement-action failure reaches OnOem1ActionFailed while that bridge lock is already
        // held (re-entrant, same thread). A prior fix round made RefreshOem1BridgeAuthority take
        // _oem1TaskSync BEFORE calling into the bridge -- the exact reverse order -- which could
        // deadlock a real action failure against a concurrent lifecycle refresh. The fix makes the
        // bridge itself evaluate the "may activate" guard from inside its own lock
        // (SetCustomAuthority's allowActivation parameter), so the lock order is always
        // bridge -> _oem1TaskSync, never the reverse.
        //
        // This drives the REAL Event41 -> recognizer -> bridge -> dispatcher -> OnOem1ActionFailed
        // path (not a direct test-only call) concurrently with a refresh paused just before it enters
        // the bridge, and asserts both complete within a bounded timeout -- a hang here would mean
        // the inversion regressed.
        var launchCount = 0;
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (composition, eventSource, mapping) = BuildArmable(launchBigPicture: () =>
        {
            Interlocked.Increment(ref launchCount);
            launched.TrySetResult();
            throw new InvalidOperationException("simulated Big Picture launch failure");
        });
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
        await composition.TestOnly_Oem1ActivationTask;
        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        var refreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composition.TestOnly_BeforeOem1BridgeAuthorityPublish = () =>
        {
            refreshEntered.TrySetResult();
            releaseRefresh.Task.GetAwaiter().GetResult();
        };

        // Simulate a lifecycle tick's refresh that already read ready == true and is paused right
        // before it enters the bridge to publish it.
        var refreshTask = Task.Run(composition.TestOnly_RefreshOem1BridgeAuthority);
        await refreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Drive the REAL failure path while the refresh is paused. A bounded wait here (rather than
        // an unbounded one) is itself the deadlock check: a regressed lock-order inversion would hang.
        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseRefresh.TrySetResult();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100); // let OnOem1ActionFailed's synchronous tail settle

        // The one-way fail-open latch means custom OEM1 admission never re-activates for the rest of
        // this composition's lifetime, regardless of how the refresh/failure ordering landed.
        Assert.Equal(1, launchCount);
        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));
        await Task.Delay(100);
        Assert.Equal(1, launchCount);

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
        var (composition, eventSource, mapping) = BuildArmable(launchBigPicture: () => launched.TrySetResult());
        IHandheldRoutingComposition handheld = composition;
        await handheld.ConfigureOem1ActionPath(() => Status(false), () => { }, mapping);
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
        /// <summary>Proves whether Event41 WMI observation was ever actually started -- the exact
        /// thing unsupported hardware must never reach.</summary>
        internal bool StartCalled { get; private set; }
        internal Action? BeforeStart { get; set; }
        public bool Start()
        {
            BeforeStart?.Invoke();
            StartCalled = true;
            return startSucceeds;
        }
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
    }

    /// <summary>Resolves the gesture recognizer's single/double-click debounce delay immediately --
    /// lets a test drive real gesture recognition without any real-time wait.</summary>
    private sealed class ImmediateGestureDelay : IOem1GestureDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TrackingGestureDelay : IOem1GestureDelay
    {
        internal int ScheduleCount { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            ScheduleCount++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
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

    /// <summary>Stands in for the settings coordinator: the composition only ever sees the narrow
    /// mapping-preference seam, so a test can drive the global remapping switch without any file
    /// I/O.</summary>
    internal sealed class FakeOem1MappingPreference(Oem1MappingSettings initial) : SteamInputAddonforClaw.Settings.IOem1MappingPreference
    {
        public Oem1MappingSettings Oem1Mapping { get; private set; } = initial;
        public event EventHandler? Oem1MappingChanged;

        internal void Set(Oem1MappingSettings next)
        {
            Oem1Mapping = next;
            Oem1MappingChanged?.Invoke(this, EventArgs.Empty);
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
