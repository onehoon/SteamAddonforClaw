using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallDependencyOwnershipPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void HidHide_requires_valid_receipt_and_exact_package(bool receipt, bool exactPackage, bool expected)
    {
        var package = new HidHidePackageState(exactPackage, exactPackage ? "1.5.230.0" : null, true);
        Assert.Equal(expected, UninstallDependencyOwnershipPolicy.CanRemoveHidHide(receipt ? HidReceipt() : null, package));
    }

    [Fact]
    public void HidHide_upgraded_package_is_preserved()
    {
        Assert.False(UninstallDependencyOwnershipPolicy.CanRemoveHidHide(HidReceipt(), new(true, "1.5.231.0", true)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void UsbIp_requires_valid_receipt_and_exact_package(bool exactPackage, bool expected)
    {
        var package = new UsbIpWin2PackageState(exactPackage, exactPackage ? "0.9.7.7" : "0.9.7.8", true, true);
        Assert.Equal(expected, UninstallDependencyOwnershipPolicy.CanRemoveUsbIp(UsbReceipt(), package));
    }

    [Fact]
    public void Corrupt_or_missing_usb_receipt_is_preserved()
    {
        Assert.False(UninstallDependencyOwnershipPolicy.CanRemoveUsbIp(null, new(true, "0.9.7.7", true, true)));
    }

    private static HidHideProvisioningReceipt HidReceipt() => new(1, HidHideProvisioningReceiptState.Provisioned, Guid.NewGuid(), "1.5.230.0", HidHidePackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "1.5.230.0");
    private static UsbIpWin2ProvisioningReceipt UsbReceipt() => new(1, UsbIpWin2ProvisioningReceiptState.Provisioned, Guid.NewGuid(), "0.9.7.7", UsbIpWin2PackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "0.9.7.7");
}
