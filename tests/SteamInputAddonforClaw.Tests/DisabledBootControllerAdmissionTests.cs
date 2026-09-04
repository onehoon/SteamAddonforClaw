using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR4 sections 9-14/25 (Cleanup D: the third-party controller-manager gate is
/// gone): the read-only Disabled-boot controller admission gate. It classifies Ready/Blocked from
/// prerequisite / recovery-journal / HidHide-baseline facts only and mutates nothing beyond the
/// deterministic HidHide baseline normalization.</summary>
public sealed class DisabledBootControllerAdmissionTests
{
    private static DisabledBootControllerAdmission Build(
        RuntimePrerequisiteAssessment? prerequisites = null,
        Func<RuntimePrerequisiteAssessment>? inspect = null,
        RecoveryResult? recovery = null,
        AddonHidHideBaselineOutcome hidHide = AddonHidHideBaselineOutcome.AlreadyCompliant,
        Func<AddonHidHideBaselineResult>? inspectHidHide = null)
        => new(
            new StubInspector(inspect ?? (() => prerequisites ?? Ready())),
            () => recovery ?? new RecoveryResult(RecoveryStatus.NoRecoveryNeeded, "none"),
            inspectHidHide ?? (() => new AddonHidHideBaselineResult(hidHide, "r", AddonHidHideBaselineSnapshot.Unknown)));

    private static RuntimePrerequisiteAssessment Ready() => new(
        new PrerequisiteAssessment(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "ok"),
        new PrerequisiteAssessment(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "ok"),
        new PrerequisiteAssessment(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "ok"));

    private static RuntimePrerequisiteAssessment With(PrerequisiteKind kind, PrerequisiteStatus status)
    {
        PrerequisiteAssessment P(PrerequisiteKind k) => new(k, k == kind ? status : PrerequisiteStatus.Ready, "x");
        return new(P(PrerequisiteKind.HidHide), P(PrerequisiteKind.UsbIpWin2), P(PrerequisiteKind.Viiper));
    }

    // ---- 25.1 Ready ----

    [Fact]
    public void All_facts_verified_is_ready()
        => Assert.Equal(DisabledBootAdmissionOutcome.Ready, Build().Evaluate().Outcome);

    // ---- HidHide: normalize + verify on every Disabled boot (PR10 addendum section 7) ----

    [Theory]
    [InlineData("Applicable")]  // normalization did not reach a proven baseline
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public void HidHide_baseline_not_proven_compliant_is_blocked(string outcome)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(hidHide: Enum.Parse<AddonHidHideBaselineOutcome>(outcome)).Evaluate().Outcome);

    [Theory]
    [InlineData("Success")]          // foreign state normalized this boot
    [InlineData("AlreadyCompliant")] // nothing to normalize
    public void HidHide_baseline_proven_compliant_is_ready(string outcome)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Ready,
            Build(hidHide: Enum.Parse<AddonHidHideBaselineOutcome>(outcome)).Evaluate().Outcome);

    [Fact]
    public void HidHide_normalization_throwing_is_blocked_not_a_crash()
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(inspectHidHide: () => throw new InvalidOperationException("boom")).Evaluate().Outcome);

    // ---- 25.6 prerequisites ----

    [Theory]
    [InlineData("UsbIpWin2", "Missing")]
    [InlineData("Viiper", "Unusable")]
    [InlineData("HidHide", "Indeterminate")]
    [InlineData("Viiper", "Incompatible")]
    public void Prerequisite_not_ready_is_blocked(string kind, string status)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(prerequisites: With(Enum.Parse<PrerequisiteKind>(kind), Enum.Parse<PrerequisiteStatus>(status))).Evaluate().Outcome);

    [Fact]
    public void Prerequisite_inspection_throwing_is_blocked()
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(inspect: () => throw new InvalidOperationException("boom")).Evaluate().Outcome);

    // ---- 25.8 recovery journal ----

    [Theory]
    [InlineData("Success")]   // a valid old route-scoped journal still exists
    [InlineData("Failure")]   // malformed / unverifiable
    public void Any_present_or_unverifiable_recovery_journal_is_blocked(string status)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(recovery: new RecoveryResult(Enum.Parse<RecoveryStatus>(status), "x")).Evaluate().Outcome);

    // ---- PR10 addendum section 22: end-to-end Disabled-boot HidHide normalization gate ----

    [Fact]
    public void Disabled_boot_normalizes_foreign_hidhide_state_then_admits_full1902()
    {
        const string addonExe = @"C:\Program Files\SteamInputAddonForClaw\SteamInputAddonforClaw.exe";
        const string foreignExe = @"C:\Program Files\ClawTweaks\ClawTweaks.exe";
        const string officialCli = @"C:\Program Files\Nefarius\HidHide\x64\HidHideCLI.exe";
        const string officialClient = @"C:\Program Files\Nefarius\HidHide\x64\HidHideClient.exe";
        var client = new NormalizingHidHideClient
        {
            Whitelist = { foreignExe, "HidHideClient.exe" }, // foreign present, CLI missing, Addon missing
            Hidden = { @"HID\VID_0DB0&PID_1902&MI_03\7&ABCDEF&0&0003" }, // foreign hidden entry
            Active = false,
        };
        var baseline = new SteamInputAddonforClaw.HidHide.AddonControllerHidHideBaseline(
            client, addonExe, () => [officialCli, officialClient]);
        var admission = new DisabledBootControllerAdmission(
            new StubInspector(Ready),
            () => new RecoveryResult(RecoveryStatus.NoRecoveryNeeded, "none"),
            () => baseline.ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(
                SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId));

        Assert.Equal(DisabledBootAdmissionOutcome.Ready, admission.Evaluate().Outcome);
        Assert.DoesNotContain(client.Whitelist, e => e.Contains("ClawTweaks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFileName(e), "HidHideCLI.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), addonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(client.Hidden);
        Assert.True(client.Active);
    }

    [Fact]
    public void Disabled_boot_that_cannot_prove_the_hidhide_baseline_never_admits_full1902()
    {
        const string addonExe = @"C:\Program Files\SteamInputAddonForClaw\SteamInputAddonforClaw.exe";
        var client = new NormalizingHidHideClient { Whitelist = { "HidHideClient.exe" }, Active = false };
        var baseline = new SteamInputAddonforClaw.HidHide.AddonControllerHidHideBaseline(
            client, addonExe, () => []); // official CLI path cannot be resolved -> Unavailable
        var admission = new DisabledBootControllerAdmission(
            new StubInspector(Ready),
            () => new RecoveryResult(RecoveryStatus.NoRecoveryNeeded, "none"),
            () => baseline.ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(_ => false));

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, admission.Evaluate().Outcome);
    }

    private sealed class NormalizingHidHideClient : SteamInputAddonforClaw.HidHide.IHidHideClient
    {
        public List<string> Whitelist { get; } = [];
        public List<string> Hidden { get; } = [];
        public bool Active { get; set; }

        public SteamInputAddonforClaw.HidHide.HidHideInspection Inspect() => new(
            Active ? SteamInputAddonforClaw.HidHide.HidHideInspectionStatus.Available
                : SteamInputAddonforClaw.HidHide.HidHideInspectionStatus.Disabled,
            new HashSet<string>(Whitelist, StringComparer.OrdinalIgnoreCase),
            Hidden.ToList(), Whitelist.ToList(), Active, false);

        public bool AddApplication(string executablePath)
        {
            if (!Whitelist.Any(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)))
                Whitelist.Add(executablePath);
            return true;
        }

        public bool RemoveApplication(string executablePath)
        {
            Whitelist.RemoveAll(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool AddHiddenDevice(string deviceEntry) { if (!Hidden.Contains(deviceEntry, StringComparer.OrdinalIgnoreCase)) Hidden.Add(deviceEntry); return true; }
        public bool RemoveHiddenDevice(string deviceEntry) { Hidden.RemoveAll(e => string.Equals(e, deviceEntry, StringComparison.OrdinalIgnoreCase)); return true; }
        public bool SetActive(bool active) { Active = active; return true; }
    }

    private sealed class StubInspector(Func<RuntimePrerequisiteAssessment> inspect) : IRuntimePrerequisiteInspector
    {
        public RuntimePrerequisiteAssessment Inspect() => inspect();
    }
}
