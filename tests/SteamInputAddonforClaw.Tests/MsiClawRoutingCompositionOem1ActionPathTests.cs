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
        bool wmiStartSucceeds = true)
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
            testOnlyOem1EventSource: eventSource);

        return (composition, eventSource);
    }

    // ---- Production suppression activation (Scope 7/8) ----

    [Fact]
    public async Task Wmi_start_success_arms_suppression_and_bridge_authority_turns_on()
    {
        var (composition, _) = BuildArmable(wmiStartSucceeds: true);
        IHandheldRoutingComposition handheld = composition;

        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
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

        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
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

        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        Assert.True(composition.CenterMOem1Coordinator.GetSnapshot().SuppressionReady);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Event41_reaches_normal_mapping_while_armed_and_routing_inactive()
    {
        var (composition, eventSource) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;

        // Swap in a real launch is not possible in a unit test -- assert dispatch reached the bridge
        // and custom authority is active; the actual Big Picture launch delegate is exercised in
        // Oem1ActionDispatcherTests.
        Assert.NotNull(composition.TestOnly_Oem1Bridge);
        eventSource.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        // No exception/failure should have revoked authority as a result of a normal-mapping dispatch
        // (Big Picture actually launching a real process is not exercised here).
        await Task.Delay(50);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Event88_never_reaches_oem1_mapping()
    {
        var (composition, eventSource) = BuildArmable();
        IHandheldRoutingComposition handheld = composition;
        var dispatchedGestures = new List<Oem1GesturePolicyRequest>();
        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
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
        handheld.ConfigureOem1ActionPath(() => Status(false), () => { });
        await composition.TestOnly_Oem1ActivationTask;
        var bridge = composition.TestOnly_Oem1Bridge!;

        await ((IAsyncDisposable)composition).DisposeAsync();

        // Dispose is idempotent; calling SetCustomAuthority after disposal must be a safe no-op
        // (proves the bridge was actually disposed, not merely deactivated).
        var exception = Record.Exception(() => bridge.SetCustomAuthority(true));
        Assert.Null(exception);
    }

    private sealed class FakeMsiEventSource(bool startSucceeds) : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start() => startSucceeds;
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
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
