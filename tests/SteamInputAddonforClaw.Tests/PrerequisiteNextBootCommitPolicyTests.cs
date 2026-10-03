using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PrerequisiteNextBootCommitPolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Exact_current_boot_pending_receipts_allow_only_the_next_boot_commit(bool hidHidePending, bool usbIpPending)
    {
        var evidence = ValidEvidence(hidHidePending, usbIpPending);

        Assert.True(PrerequisiteNextBootCommitPolicy.IsAllowed(evidence));
    }

    [Fact]
    public void Current_ready_prerequisites_do_not_use_the_pending_exception()
    {
        var evidence = ValidEvidence(hidHidePending: false, usbIpPending: false) with
        {
            CurrentPrerequisites = ReadyPrerequisites(),
        };

        Assert.False(PrerequisiteNextBootCommitPolicy.IsAllowed(evidence));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("wrong-installer-version")]
    [InlineData("wrong-installer-hash")]
    [InlineData("wrong-observed-version")]
    [InlineData("prior-boot")]
    [InlineData("package-probe-failed")]
    [InlineData("package-version-mismatch")]
    [InlineData("storage-untrusted")]
    [InlineData("viiper-not-ready")]
    public void Invalid_hidhide_pending_evidence_fails_closed(string failure)
    {
        var evidence = ValidEvidence(hidHidePending: true, usbIpPending: false);
        evidence = failure switch
        {
            "missing" => evidence with { HidHideReceipt = new(null, false) },
            "corrupt" => evidence with { HidHideReceipt = new(null, true) },
            "wrong-installer-version" => evidence with { HidHideReceipt = new(HidReceipt() with { InstallerVersion = "1.5.229.0" }, false) },
            "wrong-installer-hash" => evidence with { HidHideReceipt = new(HidReceipt() with { InstallerSha256 = new string('0', 64) }, false) },
            "wrong-observed-version" => evidence with { HidHideReceipt = new(HidReceipt() with { ObservedInstalledVersion = "1.5.229.0" }, false) },
            "prior-boot" => evidence with { HidHideReceiptFromCurrentBoot = false },
            "package-probe-failed" => evidence with { HidHidePackage = new(true, "1.5.230.0", false) },
            "package-version-mismatch" => evidence with { HidHidePackage = new(true, "1.5.229.0", true) },
            "storage-untrusted" => evidence with { Storage = new(ProvisioningStorageStatus.Unsafe, "unsafe") },
            "viiper-not-ready" => evidence with { CurrentPrerequisites = evidence.CurrentPrerequisites with { Viiper = Assessment(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing) } },
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        Assert.False(PrerequisiteNextBootCommitPolicy.IsAllowed(evidence));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("wrong-installer-version")]
    [InlineData("wrong-installer-hash")]
    [InlineData("wrong-observed-version")]
    [InlineData("prior-boot")]
    [InlineData("package-probe-failed")]
    [InlineData("package-entry-missing")]
    [InlineData("package-version-mismatch")]
    public void Invalid_usbip_pending_evidence_fails_closed(string failure)
    {
        var evidence = ValidEvidence(hidHidePending: false, usbIpPending: true);
        evidence = failure switch
        {
            "missing" => evidence with { UsbIpWin2Receipt = new(null, false) },
            "corrupt" => evidence with { UsbIpWin2Receipt = new(null, true) },
            "wrong-installer-version" => evidence with { UsbIpWin2Receipt = new(UsbReceipt() with { InstallerVersion = "0.9.8.0" }, false) },
            "wrong-installer-hash" => evidence with { UsbIpWin2Receipt = new(UsbReceipt() with { InstallerSha256 = new string('0', 64) }, false) },
            "wrong-observed-version" => evidence with { UsbIpWin2Receipt = new(UsbReceipt() with { ObservedInstalledVersion = "0.9.8.0" }, false) },
            "prior-boot" => evidence with { UsbIpWin2ReceiptFromCurrentBoot = false },
            "package-probe-failed" => evidence with { UsbIpWin2Package = new(true, "0.9.8.1", false, true) },
            "package-entry-missing" => evidence with { UsbIpWin2Package = new(true, "0.9.8.1", true, false) },
            "package-version-mismatch" => evidence with { UsbIpWin2Package = new(true, "0.9.8.0", true, true) },
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        Assert.False(PrerequisiteNextBootCommitPolicy.IsAllowed(evidence));
    }

    private static PrerequisiteNextBootCommitEvidence ValidEvidence(bool hidHidePending, bool usbIpPending) => new(
        new RuntimePrerequisiteAssessment(
            hidHidePending ? Assessment(PrerequisiteKind.HidHide, PrerequisiteStatus.Present) : Assessment(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready),
            usbIpPending ? Assessment(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Unusable) : Assessment(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready),
            Assessment(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready)),
        new(ProvisioningStorageStatus.Trusted, "trusted"),
        new(hidHidePending ? HidReceipt() : null, false),
        new(usbIpPending ? UsbReceipt() : null, false),
        new(true, "1.5.230.0", true),
        new(true, "0.9.8.1", true, true),
        hidHidePending,
        usbIpPending);

    private static RuntimePrerequisiteAssessment ReadyPrerequisites() => new(
        Assessment(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready),
        Assessment(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready),
        Assessment(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready));

    private static PrerequisiteAssessment Assessment(PrerequisiteKind kind, PrerequisiteStatus status) =>
        new(kind, status, "test", status == PrerequisiteStatus.Ready ? "1.0.0.0" : null);

    private static HidHideProvisioningReceipt HidReceipt() => new(
        HidHideProvisioningReceipt.CurrentSchemaVersion,
        HidHideProvisioningReceiptState.InstalledPendingReboot,
        Guid.NewGuid(),
        "1.5.230.0",
        HidHidePackageMetadata.InstallerSha256,
        PrerequisiteStatus.Missing,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "1.5.230.0");

    private static UsbIpWin2ProvisioningReceipt UsbReceipt() => new(
        UsbIpWin2ProvisioningReceipt.CurrentSchemaVersion,
        UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot,
        Guid.NewGuid(),
        "0.9.8.1",
        UsbIpWin2PackageMetadata.InstallerSha256,
        PrerequisiteStatus.Missing,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "0.9.8.1");
}
