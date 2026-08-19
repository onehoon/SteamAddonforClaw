using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.CenterM;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum ComponentProvisioningState { None, Provisioned, InstallStarted, PendingReboot, AttemptFailed, AttemptCancelled, Corrupt, Indeterminate, Legacy }
internal sealed record ProvisioningStateAssessment(ComponentProvisioningState HidHide, ComponentProvisioningState UsbIpWin2, bool HidHideBootSessionChanged = false, bool UsbIpWin2BootSessionChanged = false);
internal sealed record FirstTimeSetupInput(HardwareCompatibilityAssessment HardwareCompatibility, ControllerEnvironmentCompatibilityAssessment Compatibility, bool RecoverySafe, bool AddonOwnedOutputIdentityUncertain, SteamSessionState Steam, PrerequisiteAssessment HidHide, PrerequisiteAssessment UsbIpWin2, ComponentInstallationAssessment HidHideInstallation, ComponentInstallationAssessment UsbIpWin2Installation, ProvisioningStateAssessment Provisioning, bool Oem1RemappingEnabled = false, CenterMAutoRunState CenterMAutoRun = CenterMAutoRunState.Unknown);
internal enum FirstTimeSetupStatus { Complete, Required, RestartRequired, Blocked, NotApplicable, Indeterminate }
internal enum FirstTimeSetupReason { Complete, MissingComponents, CenterMAutoRunEnabled, PendingReboot, RecoveryUnsafe, AddonOwnedOutputIdentityUncertain, HardwareUnsupported, HardwareIndeterminate, CompatibilityUnsupported, CompatibilityIndeterminate, SteamActive, ProvisioningUncertain, LegacyHidHideMissing }
internal sealed record FirstTimeSetupAssessment(FirstTimeSetupStatus Status, FirstTimeSetupReason Reason, bool CanInstallRequiredComponents);

internal static class FirstTimeSetupPolicy
{
    public static FirstTimeSetupAssessment Evaluate(FirstTimeSetupInput input)
    {
        var hidHideInstalled = input.HidHideInstallation.Status == ComponentInstallationStatus.Installed
            && (input.Provisioning.HidHide != ComponentProvisioningState.PendingReboot || input.Provisioning.HidHideBootSessionChanged);
        var usbIpInstalled = input.UsbIpWin2Installation.Status == ComponentInstallationStatus.Installed
            && (input.Provisioning.UsbIpWin2 != ComponentProvisioningState.PendingReboot || input.Provisioning.UsbIpWin2BootSessionChanged);
        var componentsReady = hidHideInstalled && usbIpInstalled;
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Unsupported)
            return new(FirstTimeSetupStatus.NotApplicable, FirstTimeSetupReason.HardwareUnsupported, false);
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Indeterminate)
            return new(FirstTimeSetupStatus.Indeterminate, FirstTimeSetupReason.HardwareIndeterminate, false);
        if (input.Provisioning.HidHide is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate || input.Provisioning.UsbIpWin2 is ComponentProvisioningState.Corrupt or ComponentProvisioningState.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if ((input.Provisioning.HidHide == ComponentProvisioningState.InstallStarted && input.HidHideInstallation.Status != ComponentInstallationStatus.Installed)
            || (input.Provisioning.UsbIpWin2 == ComponentProvisioningState.InstallStarted && input.UsbIpWin2Installation.Status != ComponentInstallationStatus.Installed))
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.AttemptFailed && input.HidHideInstallation.Status is not (ComponentInstallationStatus.Missing or ComponentInstallationStatus.Installed))
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Provisioning.UsbIpWin2 == ComponentProvisioningState.AttemptFailed && input.UsbIpWin2Installation.Status is not (ComponentInstallationStatus.Missing or ComponentInstallationStatus.Installed))
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (!input.RecoverySafe) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.RecoveryUnsafe, false);
        // Independent of external-controller detection (which does not exist in this policy): if the addon
        // cannot verify which VIIPER device it owns, (re)installing prerequisites must stay blocked too.
        if (input.AddonOwnedOutputIdentityUncertain) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.AddonOwnedOutputIdentityUncertain, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Unsupported) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.CompatibilityUnsupported, false);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Indeterminate) return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.CompatibilityIndeterminate, false);
        if (input.Provisioning.HidHide == ComponentProvisioningState.Legacy && input.HidHide.Status == PrerequisiteStatus.Missing)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.LegacyHidHideMissing, false);
        if ((input.Provisioning.HidHide == ComponentProvisioningState.PendingReboot && !input.Provisioning.HidHideBootSessionChanged)
            || (input.Provisioning.UsbIpWin2 == ComponentProvisioningState.PendingReboot && !input.Provisioning.UsbIpWin2BootSessionChanged))
            return new(FirstTimeSetupStatus.RestartRequired, FirstTimeSetupReason.PendingReboot, false);
        if (input.Oem1RemappingEnabled && input.CenterMAutoRun == CenterMAutoRunState.Enabled)
        {
            if (input.Steam.IsActive)
                return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.SteamActive, false);
            return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.CenterMAutoRunEnabled, true);
        }
        if (componentsReady) return new(FirstTimeSetupStatus.Complete, FirstTimeSetupReason.Complete, false);
        if (input.HidHideInstallation.Status is ComponentInstallationStatus.ExistingUnverified or ComponentInstallationStatus.Incompatible or ComponentInstallationStatus.Indeterminate
            || input.UsbIpWin2Installation.Status is ComponentInstallationStatus.ExistingUnverified or ComponentInstallationStatus.Incompatible or ComponentInstallationStatus.Indeterminate)
            return new(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.ProvisioningUncertain, false);
        if (input.Steam.IsActive) return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.SteamActive, false);
        return new(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);
    }
}
