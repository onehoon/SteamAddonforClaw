using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalSteamDeckOutputStageTests
{
    [Fact]
    public async Task Existing_exact_deck_target_blocks_attach_before_viiper()
    {
        var session = new FakeSession();
        var stage = Create(session, new FakeEnumerator([Deck("present")]));

        var result = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckOutputConflict", result.Reason);
        Assert.Empty(session.Trace);
    }

    [Fact]
    public async Task Conflict_inspection_failure_blocks_attach()
    {
        var session = new FakeSession();
        var stage = Create(session, new FakeEnumerator(throwOnEnumerate: true));

        var result = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckOutputConflictInspectionUnavailable", result.Reason);
        Assert.Empty(session.Trace);
    }

    [Fact]
    public async Task Successful_attach_does_not_wait_for_post_attach_pnp()
    {
        var session = new FakeSession();
        var enumerator = new FakeEnumerator([]);
        var stage = Create(session, enumerator);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, enumerator.EnumerateCalls);
        Assert.Equal(["Start", "Neutral"], session.Trace);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["Start", "Neutral", "Detach", "Dispose"], session.Trace);
    }

    private static CanonicalSteamDeckOutputStage Create(FakeSession session, FakeEnumerator enumerator) =>
        new(() => session, enumerator, new FakeSnapshot(), new BlockingTickSource());

    private static ControllerDeviceInfo Deck(string id) => new($"USB\\VID_28DE&PID_1205\\{id}", null, null, [], "USB", [], [], "HID", null, null, 0x28DE, 0x1205, true);

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo>? devices = null, bool throwOnEnumerate = false) : IControllerDeviceEnumerator
    {
        public int EnumerateCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices ?? [];
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId)
        {
            EnumerateCalls++;
            if (throwOnEnumerate) throw new InvalidOperationException("enumeration failed");
            return devices ?? [];
        }
    }

    private sealed class BlockingTickSource : IInputReportTickSource
    {
        public async ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class FakeSnapshot : IControllerStateSnapshotSource { public ControllerState LatestState => default; }

    private sealed class FakeSession : ICanonicalSteamDeckSession
    {
        public List<string> Trace { get; } = [];
        public CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
        public CanonicalPendingCleanupPhase PendingCleanupPhase => CanonicalPendingCleanupPhase.None;
        public uint? BusId => State == CanonicalSteamDeckSessionState.Clean ? null : 1;
        public uint? LogicalDeviceId => State == CanonicalSteamDeckSessionState.Clean ? null : 2;
        public bool Start() { Trace.Add("Start"); State = CanonicalSteamDeckSessionState.Active; return true; }
        public bool SetState(SteamDeckDeviceState state) { Trace.Add(state.Equals(default(SteamDeckDeviceState)) ? "Neutral" : "State"); return State == CanonicalSteamDeckSessionState.Active; }
        public bool SetNeutral() { Trace.Add("Neutral"); return State == CanonicalSteamDeckSessionState.Active; }
        public bool SetOutputCallback(SteamDeckOutputCallback callback) { Trace.Add("Callback"); return true; }
        public bool ClearOutputCallback() { Trace.Add("ClearCallback"); return true; }
        public bool DetachDevice() { Trace.Add("Detach"); State = CanonicalSteamDeckSessionState.Clean; return true; }
        public bool RetryPendingCleanup() => DetachDevice();
        public bool TryGetTrackedAttachmentState(out USBDeviceAttachmentState state) { state = USBDeviceAttachmentState.Attached; return State == CanonicalSteamDeckSessionState.Active; }
        public void Dispose() => Trace.Add("Dispose");
    }
}
