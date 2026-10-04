using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationProfilePolicyTests
{
    [Fact]
    public void Production_pair_write_is_verified_only_for_CG3EM()
    {
        Assert.True(MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(new HandheldDeviceModelId("msi.claw.cg3em")));
        Assert.False(MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(new HandheldDeviceModelId("msi.claw.a2vm.7")));
        Assert.False(MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(new HandheldDeviceModelId("msi.claw.a2vm.8")));
        Assert.False(MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(new HandheldDeviceModelId("unknown")));
    }
}
