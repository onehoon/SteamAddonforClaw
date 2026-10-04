using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationFirmwarePolicyTests
{
    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    [InlineData("msi.claw.cg3em")]
    [InlineData("unknown")]
    public void Direct_motor_profile_addresses_are_unverified_without_model_specific_evidence(string modelId)
        => Assert.False(MsiClawVibrationFirmwarePolicy.IsDirectMotorProfileAddressVerified(new HandheldDeviceModelId(modelId)));
}
