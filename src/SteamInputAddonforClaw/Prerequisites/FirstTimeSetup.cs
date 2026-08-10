using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum ComponentProvisioningState { None, Provisioned, InstallStarted, PendingReboot, AttemptFailed, AttemptCancelled, Corrupt, Indeterminate, Legacy }
internal sealed record ProvisioningStateAssessment(ComponentProvisioningState HidHide, ComponentProvisioningState UsbIpWin2);
internal sealed record FirstTimeSetupInput(HardwareCompatibilityAssessment HardwareCompatibility, ControllerEnvironmentCompatibilityAssessment Compatibility, bool RecoverySafe, ExternalControllerAssessment ExternalController, SteamSessionState Steam, PrerequisiteAssessment HidHide, PrerequisiteAssessment UsbIpWin2, ProvisioningStateAssessment Provisioning);
internal enum FirstTimeSetupStatus { Complete, Required, RestartRequired, Blocked, NotApplicable, Indeterminate }
internal enum FirstTimeSetupReason { Complete, MissingComponents, PendingReboot, RecoveryUnsafe, ExternalController, ExternalControllerIndeterminate, HardwareUnsupported, HardwareIndeterminate, CompatibilityUnsupported, CompatibilityIndeterminate, SteamActive, ProvisioningUncertain, LegacyHidHideMissing }
internal sealed record FirstTimeSetupAssessment(FirstTimeSetupStatus Status, FirstTimeSetupReason Reason, bool CanInstallRequiredComponents);

internal static class FirstTimeSetupPolicy
{
    public static FirstTimeSetupAssessment Evaluate(FirstTimeSetupInput input)
    {
        var componentsReady = input.HidHide.Status == PrerequisiteStatus.Ready && input.UsbIpWin2.Status == PrerequisiteStatus.Ready;
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Unsupported)
            return new(FirstTimeSetupStatus.NotApplicable, FirstTimeSetupReason.HardwareUnsupported, false);
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Indeterminate)
            return new(FirstTimeSetupStatus.Indeterminate, FirstTimeSetupReason.HardwareIndeterminate, false);
        if (input.Provisioning.HidHide is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate || input.Provisioning.UsbIpWin2 is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.InstallStarted || input.Provisioning.UsbIpWin2 == ComponentProvisioningState.InstallStarted)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.AttemptFailed && input.HidHide.Status == PrerequisiteStatus.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.UsbIpWin2 == ComponentProvisioningState.AttemptFailed && input.UsbIpWin2.Status == PrerequisiteStatus.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (!input.RecoverySafe) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.RecoveryUnsafe, false);
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ExternalController, false);
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.Indeterminate) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ExternalControllerIndeterminate, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Unsupported) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.CompatibilityUnsupported, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Indeterminate) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.CompatibilityIndeterminate, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.Legacy && input.HidHide.Status == PrerequisiteStatus.Missing)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.LegacyHidHideMissing, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.PendingReboot || input.Provisioning.UsbIpWin2 == ComponentProvisioningState.PendingReboot)
            return new(FirstTimeSetupStatus.RestartRequired, FirstTimeSetupReason.PendingReboot, false);
        if (componentsReady) return new(FirstTimeSetupStatus.Complete, FirstTimeSetupReason.Complete, false);
        if (input.Steam.IsActive) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.SteamActive, false);
        return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);
    }
}
