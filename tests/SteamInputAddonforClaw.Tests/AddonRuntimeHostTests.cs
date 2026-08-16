using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonRuntimeHostTests
{
    [Fact]
    public async Task Host_with_unavailable_routing_remains_valid_and_passive()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null);

        Assert.False(host.IsRoutingAvailable);
        Assert.Equal(RoutingRuntimeStatusSnapshot.Unavailable, host.CaptureRoutingStatus());
        Assert.False(host.IsSafetySessionActive);
        Assert.False(host.HasOwnedRecoveryBoundary);
        Assert.False(host.HasResidualSessionState);

        // No fallback routing backend must appear, and neither operation must throw.
        await host.ReconcileAsync();
        Assert.True(await host.ReconcileFreshAfterResumeAsync(CancellationToken.None));

        await host.ShutdownRoutingAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Host_republishes_Steam_state_transitions_to_subscribers()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null);
        SteamSessionStateChangedEventArgs? observed = null;
        host.SteamSessionStateChanged += (_, args) => observed = args;

        steamRuntime.DeveloperTestModeState.SetEnabled(true);

        Assert.NotNull(observed);
        Assert.Equal(SteamSessionSource.DeveloperTest, observed.Current.Source);

        await host.ShutdownRoutingAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Steam_state_transition_drives_a_normal_reconcile_and_a_single_status_refresh()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var routingRuntime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            new FakeStatusProvider(Snapshot(WaitingForSteam())),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime);
        var refreshRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StatusRefreshRequested += (_, _) => refreshRequested.TrySetResult();

        try
        {
            // The state-change handler triggers the normal reconcile fire-and-forget; wait for
            // ReconcileSafelyAsync's finally-guaranteed status refresh rather than a fixed delay.
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await host.ShutdownRoutingAsync();
            await host.DisposeAsync();
        }
    }

    private sealed class FakeBigPicturePreference : ISteamBigPictureRoutingPreference
    {
        public bool RouteInSteamBigPicture => false;
        public event EventHandler? RouteInSteamBigPictureChanged { add { } remove { } }
    }

    private sealed class EmptyDeviceEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [];
    }

    private sealed class FakeStatusProvider(SystemStatusSnapshot? snapshot = null) : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot ?? throw new InvalidOperationException("Not exercised."));
    }

    private static SystemStatusSnapshot Snapshot(RoutingDecision decision) =>
        new(new("Test", "Test", "Test", []), null!, [], null!, null!, null!, decision, null!, true, false);

    private static RoutingDecision WaitingForSteam() => new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);

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
