using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record PrerequisiteNextBootCommitEvidence(
    RuntimePrerequisiteAssessment CurrentPrerequisites,
    ProvisioningStorageAssessment Storage,
    HidHideReceiptLoadResult HidHideReceipt,
    UsbIpWin2ReceiptLoadResult UsbIpWin2Receipt,
    HidHidePackageState HidHidePackage,
    UsbIpWin2PackageState UsbIpWin2Package,
    bool HidHideReceiptFromCurrentBoot,
    bool UsbIpWin2ReceiptFromCurrentBoot);

internal static class PrerequisiteNextBootCommitPolicy
{
    internal static bool IsAllowed(PrerequisiteNextBootCommitEvidence evidence)
    {
        if (evidence.Storage.Status != ProvisioningStorageStatus.Trusted
            || evidence.CurrentPrerequisites.Viiper.Status != PrerequisiteStatus.Ready)
            return false;

        var hasPendingPrerequisite = false;
        if (evidence.CurrentPrerequisites.HidHide.Status != PrerequisiteStatus.Ready)
        {
            if (evidence.CurrentPrerequisites.HidHide.Status is not (PrerequisiteStatus.Missing or PrerequisiteStatus.Present)
                || !IsExactCurrentBootHidHidePending(evidence))
                return false;
            hasPendingPrerequisite = true;
        }

        if (evidence.CurrentPrerequisites.UsbIpWin2.Status != PrerequisiteStatus.Ready)
        {
            if (evidence.CurrentPrerequisites.UsbIpWin2.Status != PrerequisiteStatus.Unusable
                || !IsExactCurrentBootUsbIpPending(evidence))
                return false;
            hasPendingPrerequisite = true;
        }

        return hasPendingPrerequisite;
    }

    internal static bool InspectCurrentBoot(RuntimePrerequisiteAssessment prerequisites)
    {
        try
        {
            var hidReceipt = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath).Load();
            var usbReceipt = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load();
            return IsAllowed(new(
                prerequisites,
                ProvisioningStorageSecurity.Inspect(VelopackAppPaths.ProvisioningStateDirectory),
                hidReceipt,
                usbReceipt,
                new WindowsHidHidePackageProbe().Inspect(),
                new WindowsUsbIpWin2PackageProbe().Inspect(),
                hidReceipt.Receipt is { } hid && !BootSession.HasChangedSince(hid.StartedAtUtc),
                usbReceipt.Receipt is { } usb && !BootSession.HasChangedSince(usb.StartedAtUtc)));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExactCurrentBootHidHidePending(PrerequisiteNextBootCommitEvidence evidence)
    {
        var receipt = evidence.HidHideReceipt.Receipt;
        var targetVersion = HidHidePackageMetadata.BundledVersion.ToString();
        return !evidence.HidHideReceipt.IsCorrupt
            && evidence.HidHideReceiptFromCurrentBoot
            && receipt is
            {
                IsValid: true,
                State: HidHideProvisioningReceiptState.InstalledPendingReboot,
                PreProvisioningStatus: PrerequisiteStatus.Missing,
            }
            && HidHidePackageVersionPolicy.AreEquivalent(receipt.InstallerVersion, targetVersion)
            && string.Equals(receipt.InstallerSha256, HidHidePackageMetadata.InstallerSha256, StringComparison.OrdinalIgnoreCase)
            && HidHidePackageVersionPolicy.AreEquivalent(receipt.ObservedInstalledVersion, targetVersion)
            && evidence.HidHidePackage.InspectionSucceeded
            && evidence.HidHidePackage.Installed
            && HidHidePackageVersionPolicy.AreEquivalent(evidence.HidHidePackage.Version, targetVersion);
    }

    private static bool IsExactCurrentBootUsbIpPending(PrerequisiteNextBootCommitEvidence evidence)
    {
        var receipt = evidence.UsbIpWin2Receipt.Receipt;
        var targetVersion = UsbIpWin2PackageMetadata.BundledVersion;
        return !evidence.UsbIpWin2Receipt.IsCorrupt
            && evidence.UsbIpWin2ReceiptFromCurrentBoot
            && receipt is
            {
                IsValid: true,
                State: UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot,
            }
            && Version.TryParse(receipt.InstallerVersion, out var installerVersion)
            && installerVersion == targetVersion
            && string.Equals(receipt.InstallerSha256, UsbIpWin2PackageMetadata.InstallerSha256, StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(receipt.ObservedInstalledVersion, out var observedVersion)
            && observedVersion == targetVersion
            && evidence.UsbIpWin2Package is
            {
                Installed: true,
                InspectionSucceeded: true,
                PackageEntryPresent: true,
            }
            && Version.TryParse(evidence.UsbIpWin2Package.Version, out var packageVersion)
            && packageVersion == targetVersion;
    }
}
