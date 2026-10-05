using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Processes;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FirstTimeSetupPolicyTests
{
    [Fact]
    public void UsbIpMetadata_UsesPinnedOfficialReleaseAsset()
    {
        Assert.Equal(new Version(0, 9, 8, 1), UsbIpWin2PackageMetadata.BundledVersion);
        Assert.Equal("USBip-0.9.8.1-x64.exe", UsbIpWin2PackageMetadata.InstallerFileName);
        Assert.Equal("38CAD6D4432B52D5BB9409D9AD03B72FDFFC4ADA4CD3A48FBECA1A2752A8518A", UsbIpWin2PackageMetadata.InstallerSha256);
        Assert.Equal(new Uri("https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe"), UsbIpWin2PackageMetadata.InstallerDownloadUri);
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
            HardwareCompatibility = new((HardwareCompatibilityStatus)hardwareStatus, null, null, "test"),
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.8.0")
        });

        Assert.Equal((FirstTimeSetupStatus)expectedSetupStatus, result.Status);
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
    [InlineData((int)ChildProcessResultKind.Completed, 0, (int)PrerequisiteSetupWorker.ResultKind.Installed)]
    [InlineData((int)ChildProcessResultKind.Completed, 3010, (int)PrerequisiteSetupWorker.ResultKind.RebootRequired)]
    [InlineData((int)ChildProcessResultKind.Completed, 2, (int)PrerequisiteSetupWorker.ResultKind.AlreadyInProgress)]
    [InlineData((int)ChildProcessResultKind.Completed, 3, (int)PrerequisiteSetupWorker.ResultKind.Blocked)]
    [InlineData((int)ChildProcessResultKind.FailedToStart, 0, (int)PrerequisiteSetupWorker.ResultKind.Failed)]
    [InlineData((int)ChildProcessResultKind.TimedOut, 0, (int)PrerequisiteSetupWorker.ResultKind.Failed)]
    public void Setup_worker_exit_codes_are_translated_by_the_setup_contract(int processKind, int exitCode, int expected)
        => Assert.Equal((PrerequisiteSetupWorker.ResultKind)expected, PrerequisiteSetupWorker.TranslateExitCode(new((ChildProcessResultKind)processKind, exitCode)));

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
        var result = PrerequisiteSetupWorker.WaitForHidHidePostInstallEvidence(
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
        var result = PrerequisiteSetupWorker.WaitForHidHidePostInstallEvidence(
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

    [Theory]
    [InlineData("0.9.8.1", "Installed", "ExpectedPackagePresent")]
    [InlineData("0.9.8.0", "UpdateRequired", "OlderPackageVersion")]
    [InlineData("0.9.7.7", "UpdateRequired", "OlderPackageVersion")]
    [InlineData("0.9.8.2", "Incompatible", "UnexpectedPackageVersion")]
    [InlineData("unknown", "Incompatible", "UnexpectedPackageVersion")]
    public void UsbIpInstallationAssessment_OrdersVersionsWithoutChangingBundledMetadata(string installedVersion, string expectedStatus, string expectedReason)
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessUsbIp(
            new UsbIpWin2PackageState(true, installedVersion, true, true),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Incompatible, "UsbIpWin2VersionUnsupported", installedVersion),
            "0.9.8.1");

        Assert.Equal(Enum.Parse<ComponentInstallationStatus>(expectedStatus), assessment.Status);
        Assert.Equal(expectedReason, assessment.Reason);
        Assert.Equal("0.9.8.1", UsbIpWin2PackageMetadata.BundledVersion.ToString());
        Assert.Equal("USBip-0.9.8.1-x64.exe", UsbIpWin2PackageMetadata.InstallerFileName);
    }

    [Theory]
    [InlineData(true, "Missing", "PackageAndRuntimeMissing")]
    [InlineData(false, "ExistingUnverified", "RuntimeEvidenceWithoutPackage")]
    public void UsbIpInstallationAssessment_HandlesMissingPackageAndRuntimeEvidence(bool runtimeMissing, string expectedStatus, string expectedReason)
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessUsbIp(
            new UsbIpWin2PackageState(false, null, true, false),
            new(PrerequisiteKind.UsbIpWin2, runtimeMissing ? PrerequisiteStatus.Missing : PrerequisiteStatus.Unusable, "test"),
            "0.9.8.1");

        Assert.Equal(Enum.Parse<ComponentInstallationStatus>(expectedStatus), assessment.Status);
        Assert.Equal(expectedReason, assessment.Reason);
    }

    [Fact]
    public void UsbIpInstallationAssessment_InspectionFailureIsIndeterminate()
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessUsbIp(
            new UsbIpWin2PackageState(false, null, false, false),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2PackageInspectionFailed"),
            "0.9.8.1");

        Assert.Equal(ComponentInstallationStatus.Indeterminate, assessment.Status);
        Assert.Equal("PackageInspectionFailed", assessment.Reason);
    }

    [Fact]
    public void UsbIpInstallationAssessment_MalformedBundledVersionFailsClosed()
    {
        var assessment = ComponentInstallationAssessmentPolicy.AssessUsbIp(
            new UsbIpWin2PackageState(true, "0.9.7.6", true, true),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Incompatible, "UsbIpWin2VersionUnsupported", "0.9.7.6"),
            "not-a-version");

        Assert.Equal(ComponentInstallationStatus.Incompatible, assessment.Status);
        Assert.Equal("UnexpectedPackageVersion", assessment.Reason);
    }

    [Fact]
    public void UsbIpPostInstallEvidence_WaitsForExpectedPackage()
    {
        var elapsed = 100000L;
        var polls = 0;
        var result = PrerequisiteSetupWorker.WaitForUsbIpPostInstallEvidence(
            () => polls++ == 0 ? new UsbIpWin2PackageState(false, null, true, false) : new UsbIpWin2PackageState(true, "0.9.8.1", true, true),
            () => new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Unusable, "UsbIpWin2DeviceUnavailable"),
            () => elapsed,
            milliseconds => elapsed += milliseconds,
            "0.9.8.1",
            0);

        Assert.Equal(ComponentInstallationStatus.Installed, ComponentInstallationAssessmentPolicy.AssessUsbIp(result.Package, result.Prerequisite, "0.9.8.1").Status);
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
    public void FailedUsbIpUpgradeWithOlderPackage_IsRetryable()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.7.6"),
            Provisioning = new(ComponentProvisioningState.None, ComponentProvisioningState.AttemptFailed)
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void UnresolvedUsbIpUpgradeInstallStarted_RemainsBlocked()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.7.6"),
            Provisioning = new(ComponentProvisioningState.None, ComponentProvisioningState.InstallStarted)
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void SafeUsbIpUpgrade_IsOfferedThroughExistingSetupPath()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.7.6")
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void SteamActiveUsbIpUpgrade_IsNotInstallable()
    {
        var input = Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            Steam = SteamSessionState.FromRunningAppId(1234),
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.7.6")
        };

        var setup = FirstTimeSetupPolicy.Evaluate(input);

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.Equal(FirstTimeSetupReason.SteamActive, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void RecoveryUnsafeUsbIpUpgrade_IsBlocked()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.7.6")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void DisabledBootPrerequisiteStop_AllowsARealPrerequisiteRepairWhenViiperIsReady()
    {
        var prerequisites = RuntimePrerequisites(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready);
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = FrontendPrerequisiteSetupExecutor.AllowsPrerequisiteRepairWhileRecoveryUnsafe(
                startupRepairWindow: true,
                prerequisites),
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.8.0")
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void DisabledBootFirstInstall_AllowsMissingHidHideAndUsbIpWhenViiperIsReady()
    {
        var prerequisites = new RuntimePrerequisiteAssessment(
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "Missing"),
            new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "Ready"));
        var input = Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = FrontendPrerequisiteSetupExecutor.AllowsPrerequisiteRepairWhileRecoveryUnsafe(
                startupRepairWindow: true,
                prerequisites)
        };

        var setup = FirstTimeSetupPolicy.Evaluate(input);

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.Equal(FirstTimeSetupReason.MissingComponents, setup.Reason);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void DisabledBootHidHideOnlyRepair_IsAllowedWhenViiperIsReady()
    {
        var prerequisites = new RuntimePrerequisiteAssessment(
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "Ready"),
            new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "Ready"));
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Ready) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = FrontendPrerequisiteSetupExecutor.AllowsPrerequisiteRepairWhileRecoveryUnsafe(
                startupRepairWindow: true,
                prerequisites)
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.Equal(FirstTimeSetupReason.MissingComponents, setup.Reason);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void DisabledBootRepairWindow_DoesNotBypassRecoveryUnsafeWhenNoPackageRepairIsRequired()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal(FirstTimeSetupReason.RecoveryUnsafe, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData(false, (int)PrerequisiteStatus.Ready, false)]
    [InlineData(true, (int)PrerequisiteStatus.Missing, false)]
    [InlineData(true, (int)PrerequisiteStatus.Unusable, false)]
    [InlineData(true, (int)PrerequisiteStatus.Indeterminate, false)]
    [InlineData(true, (int)PrerequisiteStatus.Ready, true)]
    public void PrerequisiteRepairPermission_RequiresStartupWindowAndReadyViiper(
        bool startupRepairWindow,
        int viiperStatus,
        bool expected)
    {
        var prerequisites = new RuntimePrerequisiteAssessment(
            new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "Missing"),
            new(PrerequisiteKind.Viiper, (PrerequisiteStatus)viiperStatus, "Test"));

        Assert.Equal(expected, FrontendPrerequisiteSetupExecutor.AllowsPrerequisiteRepairWhileRecoveryUnsafe(
            startupRepairWindow,
            prerequisites));
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)ComponentInstallationStatus.Incompatible)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate)]
    public void MissingHidHide_DoesNotMakeUnsafeUsbIpInstallationsRepairable(int usbIpInstallationStatus)
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2,
                (ComponentInstallationStatus)usbIpInstallationStatus, "UnsafePackageState")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal(FirstTimeSetupReason.ProvisioningUncertain, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)ComponentInstallationStatus.Incompatible)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate)]
    public void MissingUsbIp_DoesNotMakeUnsafeHidHideInstallationsRepairable(int hidHideInstallationStatus)
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Unusable, PrerequisiteStatus.Missing) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            HidHideInstallation = new(PrerequisiteKind.HidHide,
                (ComponentInstallationStatus)hidHideInstallationStatus, "UnsafePackageState")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal(FirstTimeSetupReason.ProvisioningUncertain, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData((int)PrerequisiteStatus.Indeterminate, (int)PrerequisiteStatus.Ready, (int)FirstTimeSetupReason.ProvisioningUncertain)]
    [InlineData((int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Incompatible, (int)FirstTimeSetupReason.RecoveryUnsafe)]
    public void DisabledBootPrerequisiteRepair_RemainsClosedWhenInstallationOrViiperIsUnsafe(
        int hidHideStatus,
        int viiperStatus,
        int expectedReason)
    {
        var prerequisites = RuntimePrerequisites((PrerequisiteStatus)hidHideStatus, (PrerequisiteStatus)viiperStatus);
        var setup = FirstTimeSetupPolicy.Evaluate(Input((PrerequisiteStatus)hidHideStatus, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = FrontendPrerequisiteSetupExecutor.AllowsPrerequisiteRepairWhileRecoveryUnsafe(
                startupRepairWindow: true,
                prerequisites),
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.8.0")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal((FirstTimeSetupReason)expectedReason, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }
    [Fact]
    public void DisabledBootPrerequisiteStop_AllowsMissingUsbIpThroughTheExistingSetupPath()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Missing) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.Incompatible)]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate)]
    [InlineData((int)ComponentInstallationStatus.Installed)]
    public void DisabledBootRepairWindow_DoesNotBypassRecoveryUnsafeForOtherUsbIpStates(int installationStatus)
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, (ComponentInstallationStatus)installationStatus, "test", "0.9.8.1")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal(FirstTimeSetupReason.RecoveryUnsafe, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Fact]
    public void NewerUsbIpPackage_RemainsBlockedAndCannotBeInstalled()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.Incompatible, "UnexpectedPackageVersion", "0.9.7.8")
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.False(setup.CanInstallRequiredComponents);
        Assert.False(PrerequisiteSetupWorker.ShouldInstallUsbIp(ComponentInstallationStatus.Incompatible));
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.Missing, true)]
    [InlineData((int)ComponentInstallationStatus.UpdateRequired, true)]
    [InlineData((int)ComponentInstallationStatus.Installed, false)]
    [InlineData((int)ComponentInstallationStatus.Incompatible, false)]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified, false)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate, false)]
    public void ExistingUsbIpInstallerPath_IsSelectedOnlyForMissingOrUpdateRequired(int statusValue, bool expected)
        => Assert.Equal(expected, PrerequisiteSetupWorker.ShouldInstallUsbIp((ComponentInstallationStatus)statusValue));

    [Fact]
    public void TrueFirstInstall_SelectsBothExistingInstallerSteps()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Missing, PrerequisiteStatus.Missing) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true
        });

        Assert.Equal(FirstTimeSetupStatus.Required, setup.Status);
        Assert.True(setup.CanInstallRequiredComponents);
        Assert.True(PrerequisiteSetupWorker.ShouldAcquireHidHide(ComponentInstallationStatus.Missing));
        Assert.True(PrerequisiteSetupWorker.ShouldInstallUsbIp(ComponentInstallationStatus.Missing));
    }

    [Fact]
    public void ElevatedFirstInstall_ProcessesHidHideBeforeUsbIp()
    {
        var source = ReadElevatedSetupSource();
        var hidHideStep = source.IndexOf("if (ShouldAcquireHidHide(hidInstallation.Status))", StringComparison.Ordinal);
        var usbIpProbe = source.IndexOf("var usbPackageProbe = new WindowsUsbIpWin2PackageProbe();", StringComparison.Ordinal);
        var usbIpStep = source.IndexOf("if (ShouldInstallUsbIp(usbInstallation.Status))", StringComparison.Ordinal);

        Assert.True(hidHideStep >= 0 && hidHideStep < usbIpProbe);
        Assert.True(usbIpProbe < usbIpStep);
    }

    [Theory]
    [InlineData((int)ComponentInstallationStatus.Missing, true)]
    [InlineData((int)ComponentInstallationStatus.Installed, false)]
    [InlineData((int)ComponentInstallationStatus.ExistingUnverified, false)]
    [InlineData((int)ComponentInstallationStatus.Incompatible, false)]
    [InlineData((int)ComponentInstallationStatus.Indeterminate, false)]
    public void HidHideAcquisition_IsSelectedOnlyForMissingPackage(int statusValue, bool expected)
        => Assert.Equal(expected, PrerequisiteSetupWorker.ShouldAcquireHidHide((ComponentInstallationStatus)statusValue));

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
    public void DisabledBootRepairWindow_DoesNotBypassPendingReboot()
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.8.0"),
            Provisioning = new(ComponentProvisioningState.None, ComponentProvisioningState.PendingReboot)
        });

        Assert.Equal(FirstTimeSetupStatus.RestartRequired, setup.Status);
        Assert.False(setup.CanInstallRequiredComponents);
    }

    [Theory]
    [InlineData((int)ComponentProvisioningState.Corrupt)]
    [InlineData((int)ComponentProvisioningState.Indeterminate)]
    [InlineData((int)ComponentProvisioningState.InstallStarted)]
    public void DisabledBootRepairWindow_DoesNotBypassUnresolvedUsbIpReceipt(int provisioningState)
    {
        var setup = FirstTimeSetupPolicy.Evaluate(Input(PrerequisiteStatus.Ready, PrerequisiteStatus.Incompatible) with
        {
            RecoverySafe = false,
            AllowPrerequisiteRepairWhileRecoveryUnsafe = true,
            UsbIpWin2Installation = new(PrerequisiteKind.UsbIpWin2, ComponentInstallationStatus.UpdateRequired, "OlderPackageVersion", "0.9.8.0"),
            Provisioning = new(ComponentProvisioningState.None, (ComponentProvisioningState)provisioningState)
        });

        Assert.Equal(FirstTimeSetupStatus.Blocked, setup.Status);
        Assert.Equal(FirstTimeSetupReason.ProvisioningUncertain, setup.Reason);
        Assert.False(setup.CanInstallRequiredComponents);
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

    private static FirstTimeSetupInput Input(PrerequisiteStatus hidHide, PrerequisiteStatus usbIp) => new(
        new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test"),
        true,
        SteamSessionState.FromRunningAppId(0),
        new(PrerequisiteKind.HidHide, hidHide, "test"), new(PrerequisiteKind.UsbIpWin2, usbIp, "test"),
        Installation(hidHide, PrerequisiteKind.HidHide), Installation(usbIp, PrerequisiteKind.UsbIpWin2),
        new(ComponentProvisioningState.None, ComponentProvisioningState.None));

    private static ComponentInstallationAssessment Installation(PrerequisiteStatus status, PrerequisiteKind kind) =>
        new(kind, status is PrerequisiteStatus.Ready or PrerequisiteStatus.Unusable ? ComponentInstallationStatus.Installed : status == PrerequisiteStatus.Missing ? ComponentInstallationStatus.Missing : ComponentInstallationStatus.ExistingUnverified, "test");

    private static RuntimePrerequisiteAssessment RuntimePrerequisites(
        PrerequisiteStatus hidHide,
        PrerequisiteStatus viiper) => new(
            new(PrerequisiteKind.HidHide, hidHide, "test"),
            new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Incompatible, "test", "0.9.8.0"),
            new(PrerequisiteKind.Viiper, viiper, "test"));

    private static string ReadElevatedSetupSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "Prerequisites", "PrerequisiteSetupWorker.cs"));
    }
}
