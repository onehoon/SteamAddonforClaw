using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupVirtualOutputRecoveryInspectorTests
{
    [Fact]
    public async Task NoEvidenceDoesNotEnumerateOrDelay()
    {
        var enumerator = new SequenceEnumerator([]);
        var inspector = new StartupVirtualOutputRecoveryInspector(enumerator, delay: (_, _) => throw new Xunit.Sdk.XunitException("unexpected delay"));

        var result = await inspector.AssessAsync([], CancellationToken.None);

        Assert.True(result.SafeToRetire);
        Assert.Equal("NoVirtualOutputEvidence", result.Reason);
        Assert.Equal(0, enumerator.CallCount);
    }

    [Fact]
    public async Task ResolvedOwnedIdStillPresentIsUnsafe()
    {
        var owned = Deck("owned").InstanceId;
        var entry = Entry([], [owned]);
        var result = await Inspect(entry, [Deck("owned")]);

        Assert.False(result.SafeToRetire);
        Assert.Equal("RecordedOwnedVirtualDeviceStillPresent", result.Reason);
    }

    [Fact]
    public async Task EmptyResolvedIdsWithNewExactCandidateIsUnsafe()
    {
        var result = await Inspect(Entry([], []), [Deck("new")]);

        Assert.False(result.SafeToRetire);
        Assert.Equal("ResidualOrAmbiguousVirtualDevicePresent", result.Reason);
    }

    [Fact]
    public async Task PreExistingExactIdentityIsPreservedAndSafe()
    {
        var result = await Inspect(Entry([Deck("external").InstanceId], [Deck("owned").InstanceId]), [Deck("external")]);

        Assert.True(result.SafeToRetire);
        Assert.Equal("StableSafeAbsence", result.Reason);
    }

    [Fact]
    public async Task CandidateAppearingAfterInitialAbsenceBlocksRetirement()
    {
        var enumerator = new SequenceEnumerator([[], [], [Deck("late")]]);
        var inspector = new StartupVirtualOutputRecoveryInspector(enumerator, requiredStableSnapshots: 2, maximumSnapshots: 3, delay: (_, _) => Task.CompletedTask);

        var result = await inspector.AssessAsync([Entry([], [])], CancellationToken.None);

        Assert.False(result.SafeToRetire);
        Assert.Equal("ResidualOrAmbiguousVirtualDevicePresent", result.Reason);
        Assert.Equal(3, enumerator.CallCount);
    }

    [Fact]
    public async Task StableSafeAbsenceRetiresEvidence()
    {
        var result = await Inspect(Entry([], ["owned"]), []);

        Assert.True(result.SafeToRetire);
        Assert.Equal("StableSafeAbsence", result.Reason);
    }

    [Fact]
    public async Task EnumerationFailureFailsClosed()
    {
        var inspector = new StartupVirtualOutputRecoveryInspector(new ThrowingEnumerator());

        var result = await inspector.AssessAsync([Entry([], [])], CancellationToken.None);

        Assert.False(result.SafeToRetire);
        Assert.Equal("EnumerationFailed", result.Reason);
    }

    [Fact]
    public async Task InvalidEvidenceFailsClosed()
    {
        var invalid = Entry([], []) with { DeviceType = "xbox360" };

        var result = await Inspect(invalid, []);

        Assert.False(result.SafeToRetire);
        Assert.Equal("EvidenceInvalid", result.Reason);
    }

    [Fact]
    public async Task CancellationDuringStabilizationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var inspector = new StartupVirtualOutputRecoveryInspector(
            new SequenceEnumerator([[], []]),
            delay: (_, token) =>
            {
                cancellation.Cancel();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.AssessAsync([Entry([], [])], cancellation.Token));
    }

    private static Task<StartupVirtualOutputRecoveryAssessment> Inspect(AddonOwnedVirtualDeviceRecoveryEntry entry, IReadOnlyList<ControllerDeviceInfo> devices) =>
        new StartupVirtualOutputRecoveryInspector(new SequenceEnumerator([devices]), delay: (_, _) => Task.CompletedTask)
            .AssessAsync([entry], CancellationToken.None);

    private static AddonOwnedVirtualDeviceRecoveryEntry Entry(IReadOnlyList<string> preExisting, IReadOnlyList<string> resolved) =>
        new(Guid.NewGuid(), "steamdeck", SteamDeckVirtualDeviceIdentityPolicy.VendorId, SteamDeckVirtualDeviceIdentityPolicy.ProductId, preExisting, resolved);

    private static ControllerDeviceInfo Deck(string instanceId) =>
        new($"USB\\VID_28DE&PID_1205\\{instanceId}", null, null, [], null, [], [], null, null, null,
            SteamDeckVirtualDeviceIdentityPolicy.VendorId, SteamDeckVirtualDeviceIdentityPolicy.ProductId, true);

    private sealed class SequenceEnumerator(IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> snapshots) : IControllerDeviceEnumerator
    {
        private int _index;
        public int CallCount { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            CallCount++;
            return snapshots[Math.Min(_index++, snapshots.Count - 1)];
        }
    }

    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException("PnP unavailable");
    }
}
