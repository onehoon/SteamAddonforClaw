using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.HidHide;
using System.Security.Cryptography;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FirstTimeSetupPolicyTests
{
    [Fact]
    public void BundledUsbIpInstaller_HashMatchesRuntimeMetadata()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "Dependencies", "UsbIpWin2", UsbIpWin2PackageMetadata.InstallerFileName);
        Assert.True(File.Exists(installer));
        Assert.Equal(UsbIpWin2PackageMetadata.InstallerSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))));
    }

    [Fact]
    public void ReadyInstallableComponents_AreCompleteWhenViiperIsUnavailable() => Assert.Equal(FirstTimeSetupStatus.Complete, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready)).Status);

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported, (int)FirstTimeSetupStatus.NotApplicable)]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate, (int)FirstTimeSetupStatus.Indeterminate)]
    public void UnsupportedOrIndeterminateHardware_NeverOffersSetupMutation(int hardwareStatus, int expectedSetupStatus)
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with
        {
            HardwareCompatibility = new((HardwareCompatibilityStatus)hardwareStatus, null, null, "test")
        });

        Assert.Equal((FirstTimeSetupStatus)expectedSetupStatus, result.Status);
        Assert.False(result.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData((int)ExternalControllerAssessmentStatus.ExternalPresent)]
    [InlineData((int)ExternalControllerAssessmentStatus.Indeterminate)]
    public void UnsafeInstallOpportunity_DisablesMutationWithoutInvalidatingComponentRequirement(int externalStatus)
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with { ExternalController = new((ExternalControllerAssessmentStatus)externalStatus, 0, []) });
        Assert.Equal(FirstTimeSetupStatus.Blocked, result.Status);
        Assert.False(result.CanInstallRequiredComponents);
    }

    [Fact]
    public void LegacyReadyHidHide_AllowsUsbIpProvisioning() => Assert.True(FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Missing) with { Provisioning = new(ComponentProvisioningState.Legacy, ComponentProvisioningState.None) }).CanInstallRequiredComponents);

    [Fact]
    public void LegacyMissingHidHide_BlocksFailClosed() => Assert.Equal(FirstTimeSetupStatus.Blocked, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Ready) with { Provisioning = new(ComponentProvisioningState.Legacy, ComponentProvisioningState.None) }).Status);

    [Theory]
    [InlineData((int)ComponentProvisioningState.InstallStarted)]
    [InlineData((int)ComponentProvisioningState.AttemptFailed)]
    [InlineData((int)ComponentProvisioningState.Corrupt)]
    public void UnsafeReceiptState_BlocksAnotherInstallAttempt(int state)
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with { Provisioning = new((ComponentProvisioningState)state, ComponentProvisioningState.None) });
        Assert.Equal(FirstTimeSetupStatus.Blocked, result.Status);
        Assert.False(result.CanInstallRequiredComponents);
    }

    [Fact]
    public void PendingReboot_RequiresRestart() => Assert.Equal(FirstTimeSetupStatus.RestartRequired, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with { Provisioning = new(ComponentProvisioningState.None, ComponentProvisioningState.PendingReboot) }).Status);

    [Fact]
    public void PendingReboot_RemainsRestartRequiredUntilElevatedReconciliation() => Assert.Equal(FirstTimeSetupStatus.RestartRequired, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with { Provisioning = new(ComponentProvisioningState.PendingReboot, ComponentProvisioningState.PendingReboot) }).Status);

    [Fact]
    public void RecoveryUnsafe_BlocksEvenWhenComponentsAreReady() => Assert.Equal(FirstTimeSetupStatus.Blocked, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with { RecoverySafe = false }).Status);

    [Theory]
    [InlineData((int)ElevatedProcessResultKind.Completed, 0, (int)ElevatedPrerequisiteSetup.ResultKind.Installed)]
    [InlineData((int)ElevatedProcessResultKind.Completed, 3010, (int)ElevatedPrerequisiteSetup.ResultKind.RebootRequired)]
    [InlineData((int)ElevatedProcessResultKind.Completed, 2, (int)ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress)]
    [InlineData((int)ElevatedProcessResultKind.Completed, 3, (int)ElevatedPrerequisiteSetup.ResultKind.Blocked)]
    [InlineData((int)ElevatedProcessResultKind.CancelledBeforeStart, 0, (int)ElevatedPrerequisiteSetup.ResultKind.Cancelled)]
    public void ElevatedSetupExitCodes_AreTranslatedByTheSetupContract(int processKind, int exitCode, int expected)
        => Assert.Equal((ElevatedPrerequisiteSetup.ResultKind)expected, ElevatedPrerequisiteSetup.TranslateExitCode(new((ElevatedProcessResultKind)processKind, exitCode)));

    [Theory]
    [InlineData(false, (int)PrerequisiteStatus.Missing, false, (int)PrerequisiteComponentAction.Install)]
    [InlineData(true, (int)PrerequisiteStatus.Ready, false, (int)PrerequisiteComponentAction.AlreadyReady)]
    [InlineData(true, (int)PrerequisiteStatus.Unusable, false, (int)PrerequisiteComponentAction.Blocked)]
    [InlineData(true, (int)PrerequisiteStatus.Unusable, true, (int)PrerequisiteComponentAction.RestartRequired)]
    public void ExistingPackageNeverSelectsReinstallation(bool packageInstalled, int prerequisiteStatus, bool pendingReboot, int expected)
        => Assert.Equal((PrerequisiteComponentAction)expected, PrerequisiteSetupExecutionPolicy.SelectAction(packageInstalled, (PrerequisiteStatus)prerequisiteStatus, pendingReboot, unresolvedInstallStarted: false));

    [Fact]
    public void UnresolvedInstallStarted_BlocksAnotherInstaller() => Assert.Equal(PrerequisiteComponentAction.Blocked, PrerequisiteSetupExecutionPolicy.SelectAction(false, PrerequisiteStatus.Missing, false, unresolvedInstallStarted: true));

    [Fact]
    public void HidHideInstallStartedAndMissing_RemainsBlockedUntilResolved() => Assert.Equal(PrerequisiteComponentAction.Blocked, PrerequisiteSetupExecutionPolicy.SelectAction(false, PrerequisiteStatus.Missing, false, unresolvedInstallStarted: true));

    [Fact]
    public void UsbIpInstallStartedAndMissing_RemainsBlockedUntilResolved() => Assert.Equal(PrerequisiteComponentAction.Blocked, PrerequisiteSetupExecutionPolicy.SelectAction(false, PrerequisiteStatus.Missing, false, unresolvedInstallStarted: true));

    [Fact]
    public void PendingRebootReceiptAndMissingPackage_BlocksReinstallUntilReconciled() => Assert.Equal(PrerequisiteComponentAction.Blocked, PrerequisiteSetupExecutionPolicy.SelectAction(false, PrerequisiteStatus.Missing, addonReceiptPendingReboot: true, unresolvedInstallStarted: false));

    [Fact]
    public void ExitZeroWithoutInstalledPackage_IsFailedInsteadOfPendingReboot()
    {
        var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(0, true, false, null, "1.5.230.0", PrerequisiteStatus.Missing);

        Assert.False(outcome.IsProvisioned);
        Assert.False(outcome.RequiresRestart);
    }

    [Fact]
    public void ExitZeroWithInstalledPackageAndReadyPrerequisite_IsProvisioned()
    {
        var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(0, true, true, "1.5.230.0", "1.5.230.0", PrerequisiteStatus.Ready);

        Assert.True(outcome.IsProvisioned);
        Assert.False(outcome.RequiresRestart);
    }

    [Fact]
    public void SetupCompleteWithViiperUnavailable_DoesNotPresentSetupRequired()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready));
        var prerequisites = new RuntimePrerequisiteAssessment(
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "Ready"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "Ready"),
            new(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing, "Missing"));

        var presentation = FirstTimeSetupPresentation.GetAddonPresentation(
            setup,
            prerequisites,
            new(AddonOperationalStatus.SetupRequired, "VIIPER is required for controller routing."));

        Assert.Equal(FirstTimeSetupStatus.Complete, setup.Status);
        Assert.False(prerequisites.IsRoutingReady);
        Assert.Equal("Setup complete", presentation.Status);
        Assert.Equal("Routing runtime is not available in this build.", presentation.Reason);
    }

    [Theory]
    [InlineData(true, true, "1.5.230.0", "1.5.230.0", (int)PrerequisiteStatus.Ready, true, (int)ProvisioningReconciliationAction.Provisioned)]
    [InlineData(true, true, "0.9.7.7", "0.9.7.7", (int)PrerequisiteStatus.Ready, false, (int)ProvisioningReconciliationAction.Provisioned)]
    [InlineData(true, true, "0.9.7.5", "0.9.7.7", (int)PrerequisiteStatus.Ready, true, (int)ProvisioningReconciliationAction.Preserve)]
    [InlineData(true, true, "0.9.7.9", "0.9.7.7", (int)PrerequisiteStatus.Ready, false, (int)ProvisioningReconciliationAction.Preserve)]
    [InlineData(false, true, "1.5.230.0", "1.5.230.0", (int)PrerequisiteStatus.Ready, true, (int)ProvisioningReconciliationAction.Preserve)]
    [InlineData(true, true, "1.5.230.0", "1.5.230.0", (int)PrerequisiteStatus.Unusable, true, (int)ProvisioningReconciliationAction.PendingReboot)]
    [InlineData(true, true, "0.9.7.7", "0.9.7.7", (int)PrerequisiteStatus.Unusable, false, (int)ProvisioningReconciliationAction.Preserve)]
    public void Reconciliation_RequiresExactReceiptVersionAndSuccessfulInspection(bool inspectionSucceeded, bool installed, string observedVersion, string expectedVersion, int prerequisiteStatus, bool installStarted, int expectedAction)
    {
        var result = ProvisioningReconciliationPolicy.Evaluate(inspectionSucceeded, installed, observedVersion, expectedVersion, (PrerequisiteStatus)prerequisiteStatus, installStarted);

        Assert.Equal((ProvisioningReconciliationAction)expectedAction, result.Action);
    }

    private static FirstTimeSetupInput Input(PrerequisiteStatus hidHide, PrerequisiteStatus usbIp) => new(
        new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test"),
        new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported), true,
        new(ExternalControllerAssessmentStatus.Clear, 0, []), SteamSessionState.FromRunningAppId(0),
        new(PrerequisiteKind.HidHide, hidHide, "test"), new(PrerequisiteKind.UsbIpWin2, usbIp, "test"), new(ComponentProvisioningState.None, ComponentProvisioningState.None));
}
