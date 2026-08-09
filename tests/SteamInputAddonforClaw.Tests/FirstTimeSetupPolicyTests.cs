using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.HidHide;
using System.Security.Cryptography;
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
    [InlineData((int)ElevatedProcessResultKind.CancelledBeforeStart, 0, (int)ElevatedPrerequisiteSetup.ResultKind.Cancelled)]
    public void ElevatedSetupExitCodes_AreTranslatedByTheSetupContract(int processKind, int exitCode, int expected)
        => Assert.Equal((ElevatedPrerequisiteSetup.ResultKind)expected, ElevatedPrerequisiteSetup.TranslateExitCode(new((ElevatedProcessResultKind)processKind, exitCode)));

    private static FirstTimeSetupInput Input(PrerequisiteStatus hidHide, PrerequisiteStatus usbIp) => new(
        new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported), true,
        new(ExternalControllerAssessmentStatus.Clear, 0, []), SteamSessionState.FromRunningAppId(0),
        new(PrerequisiteKind.HidHide, hidHide, "test"), new(PrerequisiteKind.UsbIpWin2, usbIp, "test"), new(ComponentProvisioningState.None, ComponentProvisioningState.None));
}
