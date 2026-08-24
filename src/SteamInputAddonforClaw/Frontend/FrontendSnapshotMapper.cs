using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Frontend;

internal static class FrontendSnapshotMapper
{
    internal static FrontendStatusSnapshot Map(SystemStatusSnapshot snapshot, RoutingRuntimeStatusSnapshot routing) => new(
        new(snapshot.Device.Manufacturer, snapshot.Device.Model, snapshot.Device.BaseBoardProduct, snapshot.Device.GpuModels),
        new(MapHardwareStatus(snapshot.HardwareCompatibility.Status), snapshot.HardwareCompatibility.DeviceFamily?.Value, snapshot.HardwareCompatibility.DeviceModel?.Value, snapshot.HardwareCompatibility.Reason),
        snapshot.ControllerSoftware.Select(x => new FrontendSoftwareSnapshot(x.Kind.ToString(), x.DisplayName, MapInstallation(x.Installation), MapRuntime(x.Runtime), x.Reason)).ToArray(),
        MapControllerEnvironmentStatus(snapshot.Compatibility.Status), snapshot.Compatibility.Reason.ToString(),
        new(MapPrerequisite(snapshot.Prerequisites.HidHide.Status), snapshot.Prerequisites.HidHide.Reason, MapPrerequisite(snapshot.Prerequisites.UsbIpWin2.Status), snapshot.Prerequisites.UsbIpWin2.Reason, MapPrerequisite(snapshot.Prerequisites.Viiper.Status), snapshot.Prerequisites.Viiper.Reason),
        new(snapshot.Steam.IsActive, snapshot.Steam.RunningAppId, MapSteamSource(snapshot.Steam.Source)),
        new(MapRoutingReason(snapshot.RoutingDecision.Reason), MapOperationalState(routing.OperationalState), routing.Available, routing.SteamOutputActive, routing.NativeDirectInputActive),
        MapAddonStatus(snapshot.Addon.Status), snapshot.Addon.Reason, snapshot.RecoverySafe,
        FrontendSetupStatus.Indeterminate, "Status must be refreshed before setup evaluation.", false);

    internal static FrontendStatusSnapshot ApplySetup(FrontendStatusSnapshot snapshot, FirstTimeSetupAssessment setup) => snapshot with
    {
        SetupStatus = MapSetupStatus(setup.Status),
        SetupReason = setup.Reason.ToString(),
        CanInstallRequiredComponents = PrerequisiteSetupPromptPolicy.IsInstallable(setup)
    };

    private static FrontendHardwareStatus MapHardwareStatus(HardwareCompatibilityStatus status) => status switch
    {
        HardwareCompatibilityStatus.Supported => FrontendHardwareStatus.Supported,
        HardwareCompatibilityStatus.Unsupported => FrontendHardwareStatus.Unsupported,
        _ => FrontendHardwareStatus.Indeterminate
    };

    private static FrontendControllerEnvironmentStatus MapControllerEnvironmentStatus(ControllerEnvironmentCompatibilityStatus status) => status switch
    {
        ControllerEnvironmentCompatibilityStatus.Supported => FrontendControllerEnvironmentStatus.Supported,
        ControllerEnvironmentCompatibilityStatus.Unsupported => FrontendControllerEnvironmentStatus.Unsupported,
        _ => FrontendControllerEnvironmentStatus.Indeterminate
    };

    private static FrontendSteamSource MapSteamSource(SteamSessionSource source) => source switch
    {
        SteamSessionSource.Actual => FrontendSteamSource.Actual,
        SteamSessionSource.BigPicture => FrontendSteamSource.BigPicture,
        SteamSessionSource.DeveloperTest => FrontendSteamSource.DeveloperTest,
        _ => FrontendSteamSource.Indeterminate
    };

    private static FrontendRoutingEligibilityReason MapRoutingReason(RoutingDecisionReason reason) => reason switch
    {
        RoutingDecisionReason.SteamInactive => FrontendRoutingEligibilityReason.SteamInactive,
        RoutingDecisionReason.RecoveryUnsafe => FrontendRoutingEligibilityReason.RecoveryUnsafe,
        RoutingDecisionReason.UnsupportedDevice => FrontendRoutingEligibilityReason.UnsupportedDevice,
        RoutingDecisionReason.DeviceCompatibilityIndeterminate => FrontendRoutingEligibilityReason.DeviceCompatibilityIndeterminate,
        RoutingDecisionReason.ControllerEnvironmentUnsupported => FrontendRoutingEligibilityReason.ControllerEnvironmentUnsupported,
        RoutingDecisionReason.ControllerEnvironmentIndeterminate => FrontendRoutingEligibilityReason.ControllerEnvironmentIndeterminate,
        RoutingDecisionReason.PrerequisitesNotReady => FrontendRoutingEligibilityReason.PrerequisitesNotReady,
        RoutingDecisionReason.Eligible => FrontendRoutingEligibilityReason.Eligible,
        _ => FrontendRoutingEligibilityReason.Indeterminate
    };

    private static FrontendAddonOperationalStatus MapAddonStatus(AddonOperationalStatus status) => status switch
    {
        AddonOperationalStatus.Ready => FrontendAddonOperationalStatus.Ready,
        AddonOperationalStatus.WaitingForSteam => FrontendAddonOperationalStatus.WaitingForSteam,
        AddonOperationalStatus.Passive => FrontendAddonOperationalStatus.Passive,
        AddonOperationalStatus.Unsupported => FrontendAddonOperationalStatus.Unsupported,
        AddonOperationalStatus.SetupRequired => FrontendAddonOperationalStatus.SetupRequired,
        AddonOperationalStatus.RecoveryRequired => FrontendAddonOperationalStatus.RecoveryRequired,
        AddonOperationalStatus.Indeterminate => FrontendAddonOperationalStatus.Indeterminate,
        _ => FrontendAddonOperationalStatus.Indeterminate
    };

    private static FrontendSoftwareInstallationStatus MapInstallation(SoftwareInstallationStatus status) => status switch
    {
        SoftwareInstallationStatus.Installed => FrontendSoftwareInstallationStatus.Installed,
        SoftwareInstallationStatus.NotInstalled => FrontendSoftwareInstallationStatus.NotInstalled,
        _ => FrontendSoftwareInstallationStatus.Indeterminate
    };

    private static FrontendSoftwareRuntimeStatus MapRuntime(SoftwareRuntimeStatus status) => status switch
    {
        SoftwareRuntimeStatus.Running => FrontendSoftwareRuntimeStatus.Running,
        SoftwareRuntimeStatus.NotRunning => FrontendSoftwareRuntimeStatus.NotRunning,
        SoftwareRuntimeStatus.Starting => FrontendSoftwareRuntimeStatus.Starting,
        _ => FrontendSoftwareRuntimeStatus.Indeterminate
    };

    private static FrontendPrerequisiteStatus MapPrerequisite(PrerequisiteStatus status) => status switch
    {
        PrerequisiteStatus.Ready => FrontendPrerequisiteStatus.Ready,
        PrerequisiteStatus.Missing => FrontendPrerequisiteStatus.Missing,
        PrerequisiteStatus.Present => FrontendPrerequisiteStatus.Present,
        PrerequisiteStatus.Unusable => FrontendPrerequisiteStatus.Unusable,
        PrerequisiteStatus.Incompatible => FrontendPrerequisiteStatus.Incompatible,
        _ => FrontendPrerequisiteStatus.Indeterminate
    };

    private static FrontendRoutingOperationalState MapOperationalState(RoutingOperationalState state) => state switch
    {
        RoutingOperationalState.Passive => FrontendRoutingOperationalState.Passive,
        RoutingOperationalState.OverrideActive => FrontendRoutingOperationalState.OverrideActive,
        _ => FrontendRoutingOperationalState.Indeterminate
    };

    private static FrontendSetupStatus MapSetupStatus(FirstTimeSetupStatus status) => status switch
    {
        FirstTimeSetupStatus.Complete => FrontendSetupStatus.Complete,
        FirstTimeSetupStatus.Required => FrontendSetupStatus.Required,
        FirstTimeSetupStatus.RestartRequired => FrontendSetupStatus.RestartRequired,
        FirstTimeSetupStatus.Blocked => FrontendSetupStatus.Blocked,
        FirstTimeSetupStatus.NotApplicable => FrontendSetupStatus.NotApplicable,
        _ => FrontendSetupStatus.Indeterminate
    };
}
