using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingEligibilityPolicyTests
{
    [Fact]
    public void InactiveSteamWithSafeEnvironment_WaitsForSteam()
    {
        var decision = new RoutingSessionStateMachine().Evaluate(Input(appId: 0));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), decision);
    }

    [Fact]
    public void ActiveSteamWithSafeEnvironment_IsEligible()
    {
        var decision = new RoutingSessionStateMachine().Evaluate(Input());

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), decision);
    }

    [Fact]
    public void ExternalController_HasHighestPriorityAndLatchesForSession()
    {
        var machine = new RoutingSessionStateMachine();

        var decision = machine.Evaluate(Input(external: External(ExternalControllerAssessmentStatus.ExternalPresent), recoverySafe: false, hhc: SoftwareRuntimeStatus.Running, prerequisiteStatus: PrerequisiteStatus.Missing));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerPresent), decision);
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerSessionLatched), machine.Evaluate(Input()));
    }

    [Fact]
    public void ExternalControllerLatch_ResetsOnlyAfterSteamBecomesInactive()
    {
        var machine = new RoutingSessionStateMachine();
        machine.Evaluate(Input(appId: 100, external: External(ExternalControllerAssessmentStatus.ExternalPresent)));

        Assert.Equal(RoutingDecisionKind.VetoedForSession, machine.Evaluate(Input(appId: 200)).Kind);
        Assert.Equal(RoutingDecisionKind.WaitingForSteam, machine.Evaluate(Input(appId: 0)).Kind);
        Assert.Equal(RoutingDecisionKind.Eligible, machine.Evaluate(Input(appId: 300)).Kind);
    }

    [Fact]
    public void ObservedSteamSessionEnd_ResetsLatchEvenWhenNoInactiveSnapshotIsEvaluated()
    {
        var machine = new RoutingSessionStateMachine();
        machine.Evaluate(Input(appId: 100, external: External(ExternalControllerAssessmentStatus.ExternalPresent)));

        machine.ObserveSteamSessionState(SteamSessionState.FromRunningAppId(0));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), machine.Evaluate(Input(appId: 200)));
    }

    [Fact]
    public void StaleSnapshotAfterObservedSteamSessionEnd_DoesNotRelatchExternalControllerVeto()
    {
        var machine = new RoutingSessionStateMachine();
        machine.ObserveSteamSessionState(SteamSessionState.FromRunningAppId(100));
        machine.Evaluate(Input(appId: 100, external: External(ExternalControllerAssessmentStatus.ExternalPresent)));
        var staleSnapshot = Input(appId: 100, external: External(ExternalControllerAssessmentStatus.ExternalPresent));

        machine.ObserveSteamSessionState(SteamSessionState.FromRunningAppId(0));
        machine.Evaluate(staleSnapshot);
        machine.ObserveSteamSessionState(SteamSessionState.FromRunningAppId(200));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), machine.Evaluate(Input(appId: 200)));
    }

    [Fact]
    public void ExternalControllerBeforeSteam_IsPassiveButDoesNotLatch()
    {
        var machine = new RoutingSessionStateMachine();

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Passive, RoutingDecisionReason.ExternalControllerPresent), machine.Evaluate(Input(appId: 0, external: External(ExternalControllerAssessmentStatus.ExternalPresent))));
        Assert.Equal(RoutingDecisionKind.Eligible, machine.Evaluate(Input(appId: 1)).Kind);
    }

    [Fact]
    public void IndeterminateExternalController_FailsClosed()
    {
        var decision = new RoutingSessionStateMachine().Evaluate(Input(external: External(ExternalControllerAssessmentStatus.Indeterminate)));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.ExternalControllerIndeterminate), decision);
    }

    [Theory]
    [InlineData((int)SoftwareRuntimeStatus.Running, (int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    [InlineData((int)SoftwareRuntimeStatus.Starting, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    [InlineData((int)SoftwareRuntimeStatus.Indeterminate, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    public void HandheldCompanion_UsesFailClosedPolicy(int runtimeValue, int kindValue, int reasonValue)
    {
        var decision = new RoutingSessionStateMachine().Evaluate(Input(hhc: (SoftwareRuntimeStatus)runtimeValue));

        Assert.Equal(new RoutingDecision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue), decision);
    }

    [Theory]
    [InlineData((int)SoftwareRuntimeStatus.Running, (int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    [InlineData((int)SoftwareRuntimeStatus.Starting, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    [InlineData((int)SoftwareRuntimeStatus.Indeterminate, (int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ControllerEnvironmentIndeterminate)]
    public void ClawTweaks_UsesFailClosedPolicy(int runtimeValue, int kindValue, int reasonValue)
    {
        var decision = new RoutingSessionStateMachine().Evaluate(Input(clawTweaks: (SoftwareRuntimeStatus)runtimeValue));

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
        var decision = new RoutingSessionStateMachine().Evaluate(Input(prerequisites: Prerequisites(kind, status)));

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.SetupRequired, RoutingDecisionReason.PrerequisitesNotReady), decision);
    }

    [Fact]
    public void UnsafeRecovery_IsIndeterminateUnlessExternalControllerIsPresent()
    {
        var machine = new RoutingSessionStateMachine();

        Assert.Equal(new RoutingDecision(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe), machine.Evaluate(Input(recoverySafe: false)));
        Assert.Equal(new RoutingDecision(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerPresent), machine.Evaluate(Input(recoverySafe: false, external: External(ExternalControllerAssessmentStatus.ExternalPresent))));
    }

    private static RoutingPolicyInput Input(
        uint appId = 1,
        ExternalControllerAssessment? external = null,
        bool recoverySafe = true,
        SoftwareRuntimeStatus hhc = SoftwareRuntimeStatus.NotRunning,
        SoftwareRuntimeStatus clawTweaks = SoftwareRuntimeStatus.NotRunning,
        PrerequisiteStatus prerequisiteStatus = PrerequisiteStatus.Ready,
        RuntimePrerequisiteAssessment? prerequisites = null) => new(
            SteamSessionState.FromRunningAppId(appId),
            external ?? External(ExternalControllerAssessmentStatus.Clear),
            new CurrentControllerEnvironmentCompatibilityPolicy().Evaluate([Software(ControllerSoftwareKind.MsiCenterM, SoftwareRuntimeStatus.Running, SoftwareInstallationStatus.Installed), Software(ControllerSoftwareKind.ClawTweaks, clawTweaks), Software(ControllerSoftwareKind.HandheldCompanion, hhc)]),
            prerequisites ?? Prerequisites(PrerequisiteKind.HidHide, prerequisiteStatus),
            recoverySafe);

    private static ControllerSoftwareStatus Software(ControllerSoftwareKind kind, SoftwareRuntimeStatus runtime = SoftwareRuntimeStatus.NotRunning, SoftwareInstallationStatus installation = SoftwareInstallationStatus.NotInstalled) => new(kind, kind.ToString(), installation, runtime, "test");
    private static ExternalControllerAssessment External(ExternalControllerAssessmentStatus status) => new(status, status == ExternalControllerAssessmentStatus.ExternalPresent ? 1 : 0, []);
    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteKind changedKind, PrerequisiteStatus changedStatus) => new(
        new(PrerequisiteKind.HidHide, changedKind == PrerequisiteKind.HidHide ? changedStatus : PrerequisiteStatus.Ready, "test"),
        new(PrerequisiteKind.UsbIpWin2, changedKind == PrerequisiteKind.UsbIpWin2 ? changedStatus : PrerequisiteStatus.Ready, "test"),
        new(PrerequisiteKind.Viiper, changedKind == PrerequisiteKind.Viiper ? changedStatus : PrerequisiteStatus.Ready, "test"));
}
