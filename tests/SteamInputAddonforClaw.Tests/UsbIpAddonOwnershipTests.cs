using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UsbIpAddonOwnershipTests
{
    [Fact]
    public void MissingPackage_IsOwnedWhenAddonStartsInstallation()
    {
        Assert.True(PrerequisiteSetupWorker.ShouldMarkUsbIpInstalledByAddon(
            null, ComponentInstallationStatus.Missing, null));
    }

    [Theory]
    [InlineData(nameof(UsbIpWin2ProvisioningReceiptState.Provisioned))]
    [InlineData(nameof(UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot))]
    public void LaterAddonUpgrade_CarriesProvenOwnershipWhenInstalledVersionMatchesReceipt(string receiptState)
    {
        var receipt = Receipt() with
        {
            State = Enum.Parse<UsbIpWin2ProvisioningReceiptState>(receiptState),
            InstalledByAddon = true,
            ObservedInstalledVersion = "0.9.8.1"
        };

        Assert.True(PrerequisiteSetupWorker.ShouldMarkUsbIpInstalledByAddon(
            receipt, ComponentInstallationStatus.UpdateRequired, "0.9.8.1"));
    }

    [Theory]
    [InlineData(nameof(UsbIpWin2ProvisioningReceiptState.InstallStarted))]
    [InlineData(nameof(UsbIpWin2ProvisioningReceiptState.AttemptFailed))]
    [InlineData(nameof(UsbIpWin2ProvisioningReceiptState.AttemptCancelled))]
    public void IncompleteReceiptState_DoesNotCarryUsbIpOwnership(string receiptState)
    {
        var receipt = Receipt() with
        {
            State = Enum.Parse<UsbIpWin2ProvisioningReceiptState>(receiptState),
            InstalledByAddon = true,
            ObservedInstalledVersion = "0.9.8.1"
        };

        Assert.False(PrerequisiteSetupWorker.ShouldMarkUsbIpInstalledByAddon(
            receipt, ComponentInstallationStatus.UpdateRequired, "0.9.8.1"));
    }

    [Theory]
    [InlineData(false, "0.9.8.1")]
    [InlineData(true, "0.9.7.9")]
    public void LaterUpgrade_DoesNotInventOwnershipForPreExistingOrChangedPackage(bool owned, string currentVersion)
    {
        var receipt = Receipt() with { InstalledByAddon = owned, ObservedInstalledVersion = "0.9.8.1" };

        Assert.False(PrerequisiteSetupWorker.ShouldMarkUsbIpInstalledByAddon(
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
