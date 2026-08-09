using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum ComponentProvisioningState { None, Provisioned, InstallStarted, PendingReboot, AttemptFailed, AttemptCancelled, Corrupt, Indeterminate, Legacy }
internal sealed record ProvisioningStateAssessment(ComponentProvisioningState HidHide, ComponentProvisioningState UsbIpWin2);
internal sealed record FirstTimeSetupInput(ControllerEnvironmentCompatibilityAssessment Compatibility, bool RecoverySafe, ExternalControllerAssessment ExternalController, SteamSessionState Steam, PrerequisiteAssessment HidHide, PrerequisiteAssessment UsbIpWin2, ProvisioningStateAssessment Provisioning);
internal enum FirstTimeSetupStatus { Complete, Required, RestartRequired, Blocked, Indeterminate }
internal enum FirstTimeSetupReason { Complete, MissingComponents, PendingReboot, RecoveryUnsafe, ExternalController, ExternalControllerIndeterminate, CompatibilityUnsupported, CompatibilityIndeterminate, SteamActive, ProvisioningUncertain, LegacyHidHideMissing }
internal sealed record FirstTimeSetupAssessment(FirstTimeSetupStatus Status, FirstTimeSetupReason Reason, bool CanInstallRequiredComponents);

internal static class FirstTimeSetupPolicy
{
    public static FirstTimeSetupAssessment Evaluate(FirstTimeSetupInput input)
    {
        var componentsReady = input.HidHide.Status == PrerequisiteStatus.Ready && input.UsbIpWin2.Status == PrerequisiteStatus.Ready;
        if (input.Provisioning.HidHide == ComponentProvisioningState.Legacy && input.HidHide.Status == PrerequisiteStatus.Missing)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.LegacyHidHideMissing, false);
        if (input.Provisioning.HidHide is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate || input.Provisioning.UsbIpWin2 is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.HidHide is ComponentProvisioningState.InstallStarted or ComponentProvisioningState.AttemptFailed || input.Provisioning.UsbIpWin2 is ComponentProvisioningState.InstallStarted or ComponentProvisioningState.AttemptFailed)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (componentsReady) return new(FirstTimeSetupStatus.Complete, FirstTimeSetupReason.Complete, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.PendingReboot || input.Provisioning.UsbIpWin2 == ComponentProvisioningState.PendingReboot)
            return new(FirstTimeSetupStatus.RestartRequired, FirstTimeSetupReason.PendingReboot, false);
        if (!input.RecoverySafe) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.RecoveryUnsafe, false);
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.ExternalController, false);
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.Indeterminate) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.ExternalControllerIndeterminate, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Unsupported) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.CompatibilityUnsupported, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Indeterminate) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.CompatibilityIndeterminate, false);
        if (input.Steam.IsActive) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.SteamActive, false);
        return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);
    }
}
