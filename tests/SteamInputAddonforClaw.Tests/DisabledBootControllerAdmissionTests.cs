using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR4 sections 9-14/25: the read-only Disabled-boot controller admission gate.
/// It classifies Ready/Blocked from existing facts only and mutates nothing.</summary>
public sealed class DisabledBootControllerAdmissionTests
{
    private static DisabledBootControllerAdmission Build(
        ControllerManagerKind manager = ControllerManagerKind.None,
        Func<ControllerEnvironmentAssessmentSnapshot>? capture = null,
        RuntimePrerequisiteAssessment? prerequisites = null,
        Func<RuntimePrerequisiteAssessment>? inspect = null,
        RecoveryResult? recovery = null,
        AddonHidHideBaselineOutcome hidHide = AddonHidHideBaselineOutcome.AlreadyCompliant,
        Func<AddonHidHideBaselineResult>? inspectHidHide = null)
        => new(
            new StubEnvironment(capture ?? (() => Snapshot(manager))),
            new StubInspector(inspect ?? (() => prerequisites ?? Ready())),
            () => recovery ?? new RecoveryResult(RecoveryStatus.NoRecoveryNeeded, "none"),
            inspectHidHide ?? (() => new AddonHidHideBaselineResult(hidHide, "r", AddonHidHideBaselineSnapshot.Unknown)));

    private static ControllerEnvironmentAssessmentSnapshot Snapshot(ControllerManagerKind kind) => new(
        Array.Empty<ControllerSoftwareStatus>(),
        new ControllerManagerClassification(kind, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate),
        new ControllerEnvironmentCompatibilityAssessment(
            ControllerEnvironmentCompatibilityStatus.Indeterminate,
            ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate));

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

    // ---- 25.4 / 25.5 HidHide ----

    [Theory]
    [InlineData("Applicable")]
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    public void HidHide_not_already_compliant_is_blocked(string outcome)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(hidHide: Enum.Parse<AddonHidHideBaselineOutcome>(outcome)).Evaluate().Outcome);

    [Fact]
    public void HidHide_inspection_throwing_is_blocked_not_a_crash()
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

    // ---- 25.7 controller manager ----

    [Theory]
    [InlineData("ClawTweaks")]
    [InlineData("HandheldCompanion")]
    [InlineData("Winhanced")]
    [InlineData("Multiple")]
    [InlineData("Indeterminate")]
    public void Any_non_none_controller_manager_is_blocked(string kind)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(manager: Enum.Parse<ControllerManagerKind>(kind)).Evaluate().Outcome);

    [Fact]
    public void Controller_manager_assessment_throwing_is_blocked()
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(capture: () => throw new InvalidOperationException("boom")).Evaluate().Outcome);

    // ---- 25.8 recovery journal ----

    [Theory]
    [InlineData("Success")]   // a valid old route-scoped journal still exists
    [InlineData("Failure")]   // malformed / unverifiable
    public void Any_present_or_unverifiable_recovery_journal_is_blocked(string status)
        => Assert.Equal(
            DisabledBootAdmissionOutcome.Blocked,
            Build(recovery: new RecoveryResult(Enum.Parse<RecoveryStatus>(status), "x")).Evaluate().Outcome);

    // ---- production wiring: a real assessment provider must actually observe the managers ----

    [Theory]
    [InlineData("ClawTweaks")]
    [InlineData("HandheldCompanion")]
    public void A_real_assessment_that_detects_a_manager_cannot_yield_ready(string kindName)
    {
        var kind = Enum.Parse<ControllerSoftwareKind>(kindName);
        // Feed the actual ControllerEnvironmentAssessmentProvider + ControllerManagerClassifier, not a
        // hand-set Manager.Kind, so this also guards the classifier path.
        var assessment = new ControllerEnvironmentAssessmentProvider(
            [new FakeSoftwareProvider(new ControllerSoftwareStatus(kind, kindName,
                SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running, "detected"))]);
        var admission = new DisabledBootControllerAdmission(
            assessment, new StubInspector(Ready), () => new RecoveryResult(RecoveryStatus.NoRecoveryNeeded, "none"),
            () => new AddonHidHideBaselineResult(AddonHidHideBaselineOutcome.AlreadyCompliant, "r", AddonHidHideBaselineSnapshot.Unknown));

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, admission.Evaluate().Outcome);
    }

    [Fact]
    public void Production_startup_composition_wires_the_real_conflict_detectors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs"));
        Assert.Contains("new ClawTweaksSoftwareStatusProvider(", source, StringComparison.Ordinal);
        Assert.Contains("new HandheldCompanionSoftwareStatusProvider(", source, StringComparison.Ordinal);
    }

    private sealed class FakeSoftwareProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider
    {
        public ControllerSoftwareStatus Capture() => status;
    }

    private sealed class StubEnvironment(Func<ControllerEnvironmentAssessmentSnapshot> capture) : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture() => capture();
    }

    private sealed class StubInspector(Func<RuntimePrerequisiteAssessment> inspect) : IRuntimePrerequisiteInspector
    {
        public RuntimePrerequisiteAssessment Inspect() => inspect();
    }
}
