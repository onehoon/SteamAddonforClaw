using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.HidHide;
using System.Security.Cryptography;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.CenterM;
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

    [Fact]
    public void Oem1AutoRunEnabled_OffersExplicitSetupWhenComponentsAreAlreadyReady()
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Oem1RemappingEnabled = true,
            CenterMAutoRun = CenterMAutoRunState.Enabled
        });

        Assert.Equal(FirstTimeSetupStatus.Required, result.Status);
        Assert.Equal(FirstTimeSetupReason.CenterMAutoRunEnabled, result.Reason);
        Assert.True(result.CanInstallRequiredComponents);
    }

    [Fact]
    public void Oem1Disabled_DoesNotOfferAutoRunMutation()
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Oem1RemappingEnabled = false,
            CenterMAutoRun = CenterMAutoRunState.Enabled
        });

        Assert.Equal(FirstTimeSetupStatus.Complete, result.Status);
    }

    [Fact]
    public void Oem1AutoRunUnknown_WithComponentsReady_IsNotReportedComplete()
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Oem1RemappingEnabled = true,
            CenterMAutoRun = CenterMAutoRunState.Unknown
        });

        Assert.NotEqual(FirstTimeSetupStatus.Complete, result.Status);
        Assert.Equal(FirstTimeSetupStatus.Indeterminate, result.Status);
        Assert.Equal(FirstTimeSetupReason.CenterMAutoRunUnknown, result.Reason);
        Assert.False(result.CanInstallRequiredComponents);
    }

    [Fact]
    public void Oem1AutoRunEnabled_WhileSteamActive_IsBlockedFromMutation()
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Oem1RemappingEnabled = true,
            CenterMAutoRun = CenterMAutoRunState.Enabled,
            Steam = new(true, 123, SteamSessionSource.BigPicture)
        });

        Assert.Equal(FirstTimeSetupStatus.Required, result.Status);
        Assert.Equal(FirstTimeSetupReason.SteamActive, result.Reason);
        Assert.False(result.CanInstallRequiredComponents);
    }

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

    // Regression: addon-owned VIIPER output identity uncertainty must block (re)installation even when
    // prerequisites are Missing and would otherwise be installable. This mirrors RoutingEligibilityPolicy's
    // AddonOwnedOutputIdentityUncertain gate and must not depend on external-controller detection, which
    // does not exist anywhere in this policy.
    [Fact]
    public void AddonOwnedOutputIdentityUncertain_BlocksInstallEvenWhenComponentsAreMissing()
    {
        var result = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing, addonOwnedOutputIdentityUncertain: true));

        Assert.Equal(FirstTimeSetupStatus.Blocked, result.Status);
        Assert.Equal(FirstTimeSetupReason.AddonOwnedOutputIdentityUncertain, result.Reason);
        Assert.False(result.CanInstallRequiredComponents);
    }

    [Fact]
    public void LegacyReadyHidHide_AllowsUsbIpProvisioning() => Assert.True(FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Missing) with { Provisioning = new(ComponentProvisioningState.Legacy, ComponentProvisioningState.None) }).CanInstallRequiredComponents);

    [Fact]
    public void LegacyMissingHidHide_BlocksFailClosed() => Assert.Equal(FirstTimeSetupStatus.Blocked, FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Ready) with { Provisioning = new(ComponentProvisioningState.Legacy, ComponentProvisioningState.None) }).Status);

    [Theory]
    [InlineData((int)ComponentProvisioningState.InstallStarted)]
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
    public void ExitZeroWithInstalledPackageAndDisabledConfiguration_IsInstallationSuccess()
    {
        var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(0, true, true, "1.5.230.0", "1.5.230.0", PrerequisiteStatus.Unusable);

        Assert.True(outcome.IsProvisioned);
        Assert.False(outcome.RequiresRestart);
        Assert.Equal("Provisioned", outcome.Reason);
    }

    [Fact]
    public void UsbIpWithoutInstalledPackageAndUnusablePrerequisite_IsNotProvisioned()
    {
        var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(0, true, false, null, "1.5.230.0", PrerequisiteStatus.Unusable);

        Assert.False(outcome.IsProvisioned);
        Assert.Equal("PostInstallPackageMissing", outcome.Reason);
    }

    [Fact]
    public void InstalledButRuntimeUnreadyHidHideAndReadyUsbIp_CompletesSetup()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Unusable, PrerequisiteStatus.Ready));

        Assert.Equal(FirstTimeSetupStatus.Complete, setup.Status);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void HidHidePostInstallEvidence_StopsWhenControlBecomesDisabled()
    {
        var elapsed = 100000L;
        var packagePoll = 0;
        var result = ElevatedPrerequisiteSetup.WaitForHidHidePostInstallEvidence(
            () => packagePoll++ == 0 ? new HidHidePackageState(false, null, true) : new HidHidePackageState(true, "1.5.230.0", true),
            () => new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideAvailableInactive"),
            () => elapsed,
            milliseconds => elapsed += milliseconds,
            "1.5.230.0",
            0);

        Assert.Equal(PrerequisiteStatus.Ready, result.Prerequisite.Status);
        Assert.Equal("HidHideAvailableInactive", result.Prerequisite.Reason);
        Assert.Equal(2, packagePoll);
    }

    [Fact]
    public void HidHidePostInstallEvidence_IsBoundedWhenNoEvidenceAppears()
    {
        var elapsed = 100000L;
        var polls = 0;
        var result = ElevatedPrerequisiteSetup.WaitForHidHidePostInstallEvidence(
            () => new HidHidePackageState(false, null, true),
            () =>
            {
                polls++;
                return new PrerequisiteAssessment(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing");
            },
            () => elapsed,
            milliseconds => elapsed += milliseconds,
            "1.5.230.0",
            0);

        Assert.Equal(115000, elapsed);
        Assert.Equal(PrerequisiteStatus.Missing, result.Prerequisite.Status);
        Assert.Equal(31, polls);
    }

    [Theory]
    [InlineData((int)PrerequisiteStatus.Missing, (int)ComponentInstallationStatus.Missing)]
    [InlineData((int)PrerequisiteStatus.Unusable, (int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)PrerequisiteStatus.Ready, (int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)PrerequisiteStatus.Indeterminate, (int)ComponentInstallationStatus.ExistingUnverified)]
    public void MissingPackage_IsNotAutomaticallyReinstallableWhenRuntimeEvidenceExists(int runtimeStatus, int expected)
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessHidHide(
            new HidHidePackageState(false, null, true),
            new(PrerequisiteKind.HidHide, (PrerequisiteStatus)runtimeStatus, "test"),
            "1.5.230.0");

        Assert.Equal((ComponentInstallationStatus)expected, assessment.Status);
    }

    [Fact]
    public void ExactPackageWithDisabledRuntime_IsInstalledButNotRoutingReady()
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessHidHide(
            new HidHidePackageState(true, "1.5.230.0", true),
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, "HidHideAvailableInactive"),
            "1.5.230.0");

        Assert.Equal(ComponentInstallationStatus.Installed, assessment.Status);
    }

    [Fact]
    public void UsbIpPostInstallEvidence_WaitsForExpectedPackage()
    {
        var elapsed = 100000L;
        var polls = 0;
        var result = ElevatedPrerequisiteSetup.WaitForUsbIpPostInstallEvidence(
            () => polls++ == 0 ? new UsbIpWin2PackageState(false, null, true, false) : new UsbIpWin2PackageState(true, "0.9.7.7", true, true),
            () => new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Unusable, "UsbIpWin2DeviceUnavailable"),
            () => elapsed,
            milliseconds => elapsed += milliseconds,
            "0.9.7.7",
            0);

        Assert.Equal(ComponentInstallationStatus.Installed, ComponentInstallationAssessmentPolicy.AssessUsbIp(result.Package, result.Prerequisite, "0.9.7.7").Status);
        Assert.Equal(2, polls);
    }

    [Fact]
    public void FailedReceiptWithMissingComponent_IsRetryable()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with
        {
            Provisioning = new(ComponentProvisioningState.AttemptFailed, ComponentProvisioningState.None)
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void InstallStartedWithExactPackage_DoesNotBlockMissingComponentSetup()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Missing) with
        {
            Provisioning = new(ComponentProvisioningState.InstallStarted, ComponentProvisioningState.None)
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void PendingRebootInSameBootSession_RemainsRestartRequired()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Provisioning = new(ComponentProvisioningState.PendingReboot, ComponentProvisioningState.None)
        });

        Assert.Equal(FirstTimeSetupStatus.RestartRequired, setup.Status);
    }

    [Fact]
    public void PendingRebootAfterBootChangeIsCompleteWhenPackagesAreInstalled()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            Provisioning = new(ComponentProvisioningState.PendingReboot, ComponentProvisioningState.None, HidHideBootSessionChanged: true)
        });

        Assert.Equal(FirstTimeSetupStatus.Complete, setup.Status);
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
        Assert.Equal("SetupRequired", presentation.Status);
        Assert.Equal("VIIPER is required for controller routing.", presentation.Reason);
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

    private static FirstTimeSetupInput Input(PrerequisiteStatus hidHide, PrerequisiteStatus usbIp, bool addonOwnedOutputIdentityUncertain = false) => new(
        new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test"),
        new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported), true, addonOwnedOutputIdentityUncertain,
        SteamSessionState.FromRunningAppId(0),
        new(PrerequisiteKind.HidHide, hidHide, "test"), new(PrerequisiteKind.UsbIpWin2, usbIp, "test"),
        Installation(hidHide, PrerequisiteKind.HidHide), Installation(usbIp, PrerequisiteKind.UsbIpWin2),
        new(ComponentProvisioningState.None, ComponentProvisioningState.None));

    private static ComponentInstallationAssessment Installation(PrerequisiteStatus status, PrerequisiteKind kind) =>
        new(kind, status is PrerequisiteStatus.Ready or PrerequisiteStatus.Unusable ? ComponentInstallationStatus.Installed : status == PrerequisiteStatus.Missing ? ComponentInstallationStatus.Missing : ComponentInstallationStatus.ExistingUnverified, "test");
}
