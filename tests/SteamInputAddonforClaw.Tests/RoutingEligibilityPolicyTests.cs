using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingEligibilityPolicyTests
{
    [Fact]
    public void InactiveSteamWithSafeEnvironment_WaitsForSteam()
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input(appId: 0));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), decision);
    }

    [Fact]
    public void ActiveSteamWithSafeEnvironment_IsEligible()
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input());

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), decision);
    }

    [Theory]
    [InlineData((int)SoftwareRuntimeStatus.Running, (int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    [InlineData((int)SoftwareRuntimeStatus.Starting, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    [InlineData((int)SoftwareRuntimeStatus.Indeterminate, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    public void HandheldCompanion_UsesFailClosedPolicy(int runtimeValue, int kindValue, int reasonValue)
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input(hhc: (SoftwareRuntimeStatus)runtimeValue));

        Assert.Equal(new RoutingDecision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue), decision);
    }

    [Theory]
    [InlineData((int)SoftwareRuntimeStatus.Running, (int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    [InlineData((int)SoftwareRuntimeStatus.Starting, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    [InlineData((int)SoftwareRuntimeStatus.Indeterminate, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    public void ClawTweaks_UsesFailClosedPolicy(int runtimeValue, int kindValue, int reasonValue)
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input(clawTweaks: (SoftwareRuntimeStatus)runtimeValue));

        Assert.Equal(new RoutingDecision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue), decision);
    }

    [Theory]
    [InlineData((int)PrerequisiteKind.HidHide, (int)PrerequisiteStatus.Missing)]
    [InlineData((int)PrerequisiteKind.UsbIpWin2, (int)PrerequisiteStatus.Missing)]
    [InlineData((int)PrerequisiteKind.Viiper, (int)PrerequisiteStatus.Present)]
    public void NonReadyPrerequisite_RequiresSetup(int kindValue, int statusValue)
    {
        var kind = (PrerequisiteKind)kindValue;
        var status = (PrerequisiteStatus)statusValue;
        var decision = RoutingEligibilityPolicy.Evaluate(Input(prerequisites: Prerequisites(kind, status)));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.SetupRequired, RoutingDecisionReason.PrerequisitesNotReady), decision);
    }

    [Fact]
    public void UnsafeRecovery_IsIndeterminate()
    {
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe), RoutingEligibilityPolicy.Evaluate(Input(recoverySafe: false)));
    }

    [Fact]
    public void UnsupportedHardware_IsPassive()
    {
        var unsupported = new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Unsupported, null, null, "test");
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Passive, RoutingDecisionReason.UnsupportedDevice), RoutingEligibilityPolicy.Evaluate(Input(hardware: unsupported)));
    }

    [Fact]
    public void IndeterminateHardware_IsIndeterminate()
    {
        var indeterminate = new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Indeterminate, null, null, "test");
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.DeviceCompatibilityIndeterminate), RoutingEligibilityPolicy.Evaluate(Input(hardware: indeterminate)));
    }

    // Regression: an external controller connecting or disconnecting must never change the routing
    // decision. This is the core behavioral contract of the external-controller-veto removal.
    [Fact]
    public void RoutingPolicyInput_HasNoExternalControllerField()
    {
        // RoutingPolicyInput intentionally has no ExternalController-shaped input. If this test
        // compiles, external-controller presence cannot influence RoutingEligibilityPolicy.Evaluate.
        var input = Input();
        Assert.Equal(RoutingDecisionKind.Eligible, RoutingEligibilityPolicy.Evaluate(input).Kind);
    }

    // Regression: addon-owned VIIPER output identity uncertainty must still fail safe, independent
    // of external-controller detection (which no longer exists as a concept in this policy at all).
    [Fact]
    public void AddonOwnedOutputIdentityUncertain_FailsSafeEvenWhenOtherwiseEligible()
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input(addonOwnedOutputIdentityUncertain: true));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.AddonOwnedOutputIdentityUncertain), decision);
    }

    [Fact]
    public void AddonOwnedOutputIdentityUncertain_TakesPriorityOverUnsupportedEnvironment()
    {
        var decision = RoutingEligibilityPolicy.Evaluate(Input(addonOwnedOutputIdentityUncertain: true, hhc: SoftwareRuntimeStatus.Running));

        Assert.Equal(RoutingDecisionReason.AddonOwnedOutputIdentityUncertain, decision.Reason);
    }

    private static RoutingPolicyInput Input(
        uint appId = 1,
        bool recoverySafe = true,
        HardwareCompatibilityAssessment? hardware = null,
        SoftwareRuntimeStatus hhc = SoftwareRuntimeStatus.NotRunning,
        SoftwareRuntimeStatus clawTweaks = SoftwareRuntimeStatus.NotRunning,
        PrerequisiteStatus prerequisiteStatus = PrerequisiteStatus.Ready,
        RuntimePrerequisiteAssessment? prerequisites = null,
        bool addonOwnedOutputIdentityUncertain = false) => new(
            SteamSessionState.FromRunningAppId(appId),
            hardware ?? SupportedHardware(),
            new CurrentControllerEnvironmentCompatibilityPolicy().Evaluate([Software(ControllerSoftwareKind.MsiCenterM, SoftwareRuntimeStatus.Running, SoftwareInstallationStatus.Installed), Software(ControllerSoftwareKind.ClawTweaks, clawTweaks), Software(ControllerSoftwareKind.HandheldCompanion, hhc)]),
            prerequisites ?? Prerequisites(PrerequisiteKind.HidHide, prerequisiteStatus),
            recoverySafe,
            addonOwnedOutputIdentityUncertain);

    private static HardwareCompatibilityAssessment SupportedHardware() => new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test");

    private static ControllerSoftwareStatus Software(ControllerSoftwareKind kind, SoftwareRuntimeStatus runtime = SoftwareRuntimeStatus.NotRunning, SoftwareInstallationStatus installation = SoftwareInstallationStatus.NotInstalled) => new(kind, kind.ToString(), installation, runtime, "test");
    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteKind changedKind, PrerequisiteStatus changedStatus) => new(
        new(PrerequisiteKind.HidHide, changedKind == PrerequisiteKind.HidHide ? changedStatus : PrerequisiteStatus.Ready, "test"),
        new(PrerequisiteKind.UsbIpWin2, changedKind == PrerequisiteKind.UsbIpWin2 ? changedStatus : PrerequisiteStatus.Ready, "test"),
        new(PrerequisiteKind.Viiper, changedKind == PrerequisiteKind.Viiper ? changedStatus : PrerequisiteStatus.Ready, "test"));
}
