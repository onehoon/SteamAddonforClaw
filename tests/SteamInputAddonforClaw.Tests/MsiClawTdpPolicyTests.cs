using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawTdpPolicyTests
{
    [Theory]
    [InlineData("msi.claw.a2vm.7", 30, 37, 0)]
    [InlineData("msi.claw.a2vm.8", 30, 37, 0)]
    [InlineData("msi.claw.cg3em", 35, 45, 6)]
    public void TryResolve_ReturnsExpectedModelPolicy(string modelId, int pl1Max, int pl2Max, int shift)
    {
        Assert.True(MsiClawTdpPolicy.TryResolve(new HandheldDeviceModelId(modelId), out var policy));
        Assert.Equal(8, policy.Pl1MinimumWatts);
        Assert.Equal(pl1Max, policy.Pl1MaximumWatts);
        Assert.Equal(8, policy.Pl2MinimumWatts);
        Assert.Equal(pl2Max, policy.Pl2MaximumWatts);
        Assert.Equal(shift, policy.ManualCompatibleShiftSelector);
    }

    [Fact]
    public void TryResolve_UnknownModelFailsClosed()
        => Assert.False(MsiClawTdpPolicy.TryResolve(new HandheldDeviceModelId("unknown"), out _));

    [Theory]
    [InlineData("msi.claw.a2vm.7", 30, 8)]
    [InlineData("msi.claw.cg3em", 35, 8)]
    public void IsValid_ValidatesPl1AndPl2Independently(string modelId, int pl1, int pl2)
    {
        Assert.True(MsiClawTdpPolicy.TryResolve(new HandheldDeviceModelId(modelId), out var policy));
        Assert.True(policy.IsValid(new TdpPowerPair { Pl1Watts = pl1, Pl2Watts = pl2 }));
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7", 7, 20)]
    [InlineData("msi.claw.a2vm.7", 31, 20)]
    [InlineData("msi.claw.a2vm.7", 20, 7)]
    [InlineData("msi.claw.a2vm.7", 20, 38)]
    [InlineData("msi.claw.cg3em", 7, 20)]
    [InlineData("msi.claw.cg3em", 36, 20)]
    [InlineData("msi.claw.cg3em", 20, 7)]
    [InlineData("msi.claw.cg3em", 20, 46)]
    public void IsValid_RejectsValuesOutsideModelRanges(string modelId, int pl1, int pl2)
    {
        Assert.True(MsiClawTdpPolicy.TryResolve(new HandheldDeviceModelId(modelId), out var policy));
        Assert.False(policy.IsValid(new TdpPowerPair { Pl1Watts = pl1, Pl2Watts = pl2 }));
    }
}
