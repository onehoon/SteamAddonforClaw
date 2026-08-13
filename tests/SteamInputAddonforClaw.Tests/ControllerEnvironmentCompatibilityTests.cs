using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentCompatibilityTests
{
    private readonly CurrentControllerEnvironmentCompatibilityPolicy _policy = new();

    [Fact]
    public void StockCenterMOnly_IsSupported() => Assert.Equal(
        new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported),
        _policy.Evaluate(Software()));

    [Theory]
    [InlineData((int)ControllerSoftwareKind.ClawTweaks, (int)ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion)]
    [InlineData((int)ControllerSoftwareKind.HandheldCompanion, (int)ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion)]
    public void InstalledThirdPartyManager_IsUnsupportedRegardlessOfRuntime(int kindValue, int reasonValue)
    {
        var kind = (ControllerSoftwareKind)kindValue;
        var reason = (ControllerEnvironmentCompatibilityReason)reasonValue;
        var statuses = Software();
        statuses[(int)kind] = Status(kind, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning);
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Unsupported, assessment.Status);
        Assert.Equal(reason, assessment.Reason);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void MultipleThirdPartyManagers_AreUnsupported() => Assert.Equal(
        ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion,
        _policy.Evaluate(Software(clawTweaks: Status(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.Installed), handheldCompanion: Status(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.Installed))).Reason);

    [Fact]
    public void Winhanced_IsAnOptionalSnapshotInputAndIsUnsupportedWhenPresent()
    {
        var statuses = Software();
        statuses.Add(Status(ControllerSoftwareKind.Winhanced, SoftwareInstallationStatus.Installed));
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Unsupported, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.WinhancedNotSupportedByCurrentVersion, assessment.Reason);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void Winhanced_Running_IsUnsupported()
    {
        var statuses = Software();
        statuses.Add(Status(ControllerSoftwareKind.Winhanced, runtime: SoftwareRuntimeStatus.Running));
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Unsupported, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.WinhancedNotSupportedByCurrentVersion, assessment.Reason);
    }

    [Fact]
    public void Winhanced_Unresolved_IsIndeterminate()
    {
        var statuses = Software();
        statuses.Add(Status(ControllerSoftwareKind.Winhanced, runtime: SoftwareRuntimeStatus.Indeterminate));
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate, assessment.Reason);
    }

    [Theory]
    [InlineData((int)ControllerSoftwareKind.ClawTweaks, (int)ControllerSoftwareKind.Winhanced)]
    [InlineData((int)ControllerSoftwareKind.HandheldCompanion, (int)ControllerSoftwareKind.Winhanced)]
    public void ThirdPartyManagerWithWinhanced_IsMultiple(int firstKindValue, int secondKindValue)
    {
        var statuses = Software();
        var firstKind = (ControllerSoftwareKind)firstKindValue;
        var secondKind = (ControllerSoftwareKind)secondKindValue;
        statuses[(int)firstKind] = Status(firstKind, SoftwareInstallationStatus.Installed);
        statuses.Add(Status(secondKind, SoftwareInstallationStatus.Installed));
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Unsupported, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion, assessment.Reason);
    }

    [Fact]
    public void AllThirdPartyManagersPresent_IsMultiple()
    {
        var statuses = Software();
        statuses[(int)ControllerSoftwareKind.ClawTweaks] = Status(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.Installed);
        statuses[(int)ControllerSoftwareKind.HandheldCompanion] = Status(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.Installed);
        statuses.Add(Status(ControllerSoftwareKind.Winhanced, SoftwareInstallationStatus.Installed));
        Assert.Equal(ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion, _policy.Evaluate(statuses).Reason);
    }

    [Fact]
    public void UnknownManagerClassification_IsFailClosed()
    {
        var classification = new ControllerManagerClassification((ControllerManagerKind)999, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);
        var assessment = CurrentControllerEnvironmentCompatibilityPolicy.MapClassification(classification, Status(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running));
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void WinhancedAbsentOrNotInstalled_PreservesStockBehavior()
    {
        var absent = _policy.Evaluate(Software());
        var notInstalled = Software();
        notInstalled.Add(Status(ControllerSoftwareKind.Winhanced));
        Assert.Equal(absent, _policy.Evaluate(notInstalled));
    }

    [Fact]
    public void PresentManagerTakesPrecedenceOverUnresolvedManager()
    {
        var statuses = Software(clawTweaks: Status(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.Installed), handheldCompanion: Status(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate));
        Assert.Equal(ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion, _policy.Evaluate(statuses).Reason);
    }

    [Fact]
    public void DuplicateOptionalWinhanced_IsIndeterminate()
    {
        var statuses = Software();
        statuses.Add(Status(ControllerSoftwareKind.Winhanced));
        statuses.Add(Status(ControllerSoftwareKind.Winhanced));
        var assessment = _policy.Evaluate(statuses);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.False(assessment.AllowsMutation);
    }

    [Theory]
    [InlineData((int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMRequired)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Starting, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMStarting)]
    [InlineData((int)SoftwareInstallationStatus.Indeterminate, (int)SoftwareRuntimeStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate)]
    public void CenterMStates_AreFailClosed(int installationValue, int runtimeValue, int expectedStatusValue, int expectedReasonValue)
    {
        var installation = (SoftwareInstallationStatus)installationValue;
        var runtime = (SoftwareRuntimeStatus)runtimeValue;
        var expectedStatus = (ControllerEnvironmentCompatibilityStatus)expectedStatusValue;
        var expectedReason = (ControllerEnvironmentCompatibilityReason)expectedReasonValue;
        var assessment = _policy.Evaluate(Software(centerM: Status(ControllerSoftwareKind.MsiCenterM, installation, runtime)));
        Assert.Equal(expectedStatus, assessment.Status);
        Assert.Equal(expectedReason, assessment.Reason);
    }

    [Fact]
    public void MissingOrDuplicateSoftwareEntry_IsIndeterminate()
    {
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, _policy.Evaluate(Software()[..2]).Status);
        var duplicate = Software(); duplicate.Add(Status(ControllerSoftwareKind.ClawTweaks));
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, _policy.Evaluate(duplicate).Status);
    }

    [Fact]
    public void UnexpectedSoftwareEntry_IsIndeterminate()
    {
        var statuses = Software();
        statuses.Add(Status((ControllerSoftwareKind)999, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running));

        var assessment = _policy.Evaluate(statuses);

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate, assessment.Reason);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void UnsupportedCompatibility_MakesRoutingPassive()
    {
        var compatibility = new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion);
        var input = new RoutingPolicyInput(SteamSessionState.FromRunningAppId(1), SupportedHardware(), compatibility, ReadyPrerequisites(), true, false);
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Passive, RoutingDecisionReason.ControllerEnvironmentUnsupported), RoutingEligibilityPolicy.Evaluate(input));
        var supported = input with { Compatibility = new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported) };
        Assert.Equal(RoutingDecisionKind.Eligible, RoutingEligibilityPolicy.Evaluate(supported).Kind);
    }

    [Fact]
    public void CompatibilityUnsupported_MapsToSpecificAddonMessage()
    {
        var compatibility = new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion);
        var addon = AddonStatusEvaluator.Map(new(RoutingDecisionKind.Passive, RoutingDecisionReason.ControllerEnvironmentUnsupported), compatibility);
        Assert.Equal(AddonOperationalStatus.Unsupported, addon.Status);
        Assert.Contains("ClawTweaks is installed", addon.Reason);
    }

    private static HardwareCompatibilityAssessment SupportedHardware() => new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test");

    private static List<ControllerSoftwareStatus> Software(ControllerSoftwareStatus? centerM = null, ControllerSoftwareStatus? clawTweaks = null, ControllerSoftwareStatus? handheldCompanion = null) =>
        [centerM ?? Status(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running), clawTweaks ?? Status(ControllerSoftwareKind.ClawTweaks), handheldCompanion ?? Status(ControllerSoftwareKind.HandheldCompanion)];
    private static ControllerSoftwareStatus Status(ControllerSoftwareKind kind, SoftwareInstallationStatus installation = SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus runtime = SoftwareRuntimeStatus.NotRunning) => new(kind, kind.ToString(), installation, runtime, "test");
    private static RuntimePrerequisiteAssessment ReadyPrerequisites() => new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "test"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "test"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "test"));
}
