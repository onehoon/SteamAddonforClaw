using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HandheldDeviceRegistryTests
{
    [Fact]
    public void Resolve_WhenNoAdaptersMatch_ReturnsUnsupported() =>
        Assert.Equal(HandheldDeviceResolutionStatus.Unsupported, Resolve(NoMatch()).Status);

    [Fact]
    public void Resolve_WhenExactlyOneAdapterMatches_ReturnsMatchedAdapter()
    {
        var matching = Match();
        var result = Resolve(NoMatch(), matching);

        Assert.Equal(HandheldDeviceResolutionStatus.Matched, result.Status);
        Assert.Same(matching, result.Adapter);
    }

    [Fact]
    public void Resolve_WhenTwoAdaptersMatch_ReturnsAmbiguous() =>
        Assert.Equal(HandheldDeviceResolutionStatus.Ambiguous, Resolve(Match(), Match()).Status);

    [Fact]
    public void Resolve_WhenAnyAdapterIsIndeterminate_ReturnsIndeterminate() =>
        Assert.Equal(HandheldDeviceResolutionStatus.Indeterminate, Resolve(NoMatch(), Indeterminate()).Status);

    [Fact]
    public void Resolve_WhenMatchAndIndeterminateExist_ReturnsIndeterminate() =>
        Assert.Equal(HandheldDeviceResolutionStatus.Indeterminate, Resolve(Match(), Indeterminate()).Status);

    [Fact]
    public void Resolve_WhenProbeThrows_ReturnsIndeterminate() =>
        Assert.Equal(HandheldDeviceResolutionStatus.Indeterminate, Resolve(new FakeAdapter(_ => throw new InvalidOperationException())).Status);

    [Fact]
    public void TryGetById_ReturnsExactAdapter_AndDuplicateIdsAreRejected()
    {
        var adapter = Match();
        var registry = new HandheldDeviceRegistry([adapter]);
        Assert.True(registry.TryGetById(adapter.Descriptor.Id, out var found));
        Assert.Same(adapter, found);
        Assert.False(registry.TryGetById(new HandheldDeviceId("unknown.device"), out _));
        Assert.Throws<ArgumentException>(() => new HandheldDeviceRegistry([adapter, new FakeAdapter(_ => new(DeviceProbeStatus.NoMatch, "x"), adapter.Descriptor.Id)]));
    }

    private static HandheldDeviceResolution Resolve(params IHandheldDeviceAdapter[] adapters) =>
        new HandheldDeviceRegistry(adapters).Resolve(new DeviceProbeContext());

    private static FakeAdapter Match() => new(_ => new DeviceProbeResult(DeviceProbeStatus.Match, "Matched."));
    private static FakeAdapter NoMatch() => new(_ => new DeviceProbeResult(DeviceProbeStatus.NoMatch, "No match."));
    private static FakeAdapter Indeterminate() => new(_ => new DeviceProbeResult(DeviceProbeStatus.Indeterminate, "Indeterminate."));

    private sealed class FakeAdapter(Func<DeviceProbeContext, DeviceProbeResult> probe, HandheldDeviceId? id = null) : IHandheldDeviceAdapter
    {
        public HandheldDeviceDescriptor Descriptor { get; } = new(id ?? new HandheldDeviceId($"test.{Guid.NewGuid():N}"), "Test", "Device", "Test Device");
        public AuxiliaryControlCatalog AuxiliaryControls { get; } = new([]);
        public IInternalControllerMatcher InternalControllerMatcher { get; } = new NoMatchInternalControllerMatcher();
        public INativeControllerStateManager? NativeState => null;
        public DeviceProbeResult Probe(DeviceProbeContext context) => probe(context);
    }

    private sealed class NoMatchInternalControllerMatcher : IInternalControllerMatcher
    {
        public InternalControllerMatchResult Match(InternalControllerMatchContext context) =>
            new(InternalControllerMatchStatus.NoMatch, "Not applicable.");
    }
}
