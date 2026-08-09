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

    private static HandheldDeviceResolution Resolve(params IHandheldDeviceAdapter[] adapters) =>
        new HandheldDeviceRegistry(adapters).Resolve(new DeviceProbeContext());

    private static FakeAdapter Match() => new(_ => new DeviceProbeResult(DeviceProbeStatus.Match, "Matched."));
    private static FakeAdapter NoMatch() => new(_ => new DeviceProbeResult(DeviceProbeStatus.NoMatch, "No match."));
    private static FakeAdapter Indeterminate() => new(_ => new DeviceProbeResult(DeviceProbeStatus.Indeterminate, "Indeterminate."));

    private sealed class FakeAdapter(Func<DeviceProbeContext, DeviceProbeResult> probe) : IHandheldDeviceAdapter
    {
        public HandheldDeviceDescriptor Descriptor { get; } = new(new HandheldDeviceId("test.device"), "Test", "Device", "Test Device");
        public AuxiliaryControlCatalog AuxiliaryControls { get; } = new([]);
        public DeviceProbeResult Probe(DeviceProbeContext context) => probe(context);
    }
}
