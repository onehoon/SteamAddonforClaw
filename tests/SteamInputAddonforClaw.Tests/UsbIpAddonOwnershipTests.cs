using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UsbIpAddonOwnershipTests
{
    [Fact]
    public void MissingPackage_IsOwnedWhenAddonStartsInstallation()
    {
        Assert.True(ElevatedPrerequisiteSetup.ShouldMarkUsbIpInstalledByAddon(
            null, ComponentInstallationStatus.Missing, null));
    }

    [Fact]
    public void LaterAddonUpgrade_CarriesProvenOwnershipWhenInstalledVersionMatchesReceipt()
    {
        var receipt = Receipt() with { InstalledByAddon = true, ObservedInstalledVersion = "0.9.8.1" };

        Assert.True(ElevatedPrerequisiteSetup.ShouldMarkUsbIpInstalledByAddon(
            receipt, ComponentInstallationStatus.UpdateRequired, "0.9.8.1"));
    }

    [Theory]
    [InlineData(false, "0.9.8.1")]
    [InlineData(true, "0.9.7.9")]
    public void LaterUpgrade_DoesNotInventOwnershipForPreExistingOrChangedPackage(bool owned, string currentVersion)
    {
        var receipt = Receipt() with { InstalledByAddon = owned, ObservedInstalledVersion = "0.9.8.1" };

        Assert.False(ElevatedPrerequisiteSetup.ShouldMarkUsbIpInstalledByAddon(
            receipt, ComponentInstallationStatus.UpdateRequired, currentVersion));
    }

    private static UsbIpWin2ProvisioningReceipt Receipt() => new(
        UsbIpWin2ProvisioningReceipt.CurrentSchemaVersion,
        UsbIpWin2ProvisioningReceiptState.Provisioned,
        Guid.NewGuid(),
        "0.9.8.1",
        UsbIpWin2PackageMetadata.InstallerSha256,
        PrerequisiteStatus.Missing,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "0.9.8.1",
        PreInstallationStatus: ComponentInstallationStatus.Missing,
        InstalledByAddon: true);
}
