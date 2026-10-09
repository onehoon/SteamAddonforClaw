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

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public void A2VM_status_explains_unverified_mapping_and_confirms_no_profile_write(string model)
    {
        var reason = MsiClawVibrationProfilePolicy.GetProductionUnavailableReason(new HandheldDeviceModelId(model));

        Assert.Contains("profile-write mapping has not been verified", reason, StringComparison.Ordinal);
        Assert.Contains("no profile write was issued", reason, StringComparison.Ordinal);
        Assert.False(MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified(new HandheldDeviceModelId(model)));
    }
}
