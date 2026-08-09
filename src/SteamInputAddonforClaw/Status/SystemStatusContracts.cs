using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Status;

internal sealed record DeviceStatusSnapshot(string Manufacturer, string Model, IReadOnlyList<string> GpuModels);

internal enum ControllerSoftwareKind { MsiCenterM, ClawTweaks, HandheldCompanion }
internal enum SoftwareInstallationStatus { Installed, NotInstalled, Indeterminate }
internal enum SoftwareRuntimeStatus { Running, NotRunning, Starting, Indeterminate }
internal sealed record ControllerSoftwareStatus(ControllerSoftwareKind Kind, string DisplayName, SoftwareInstallationStatus Installation, SoftwareRuntimeStatus Runtime, string Reason);
internal sealed record SteamStatusSnapshot(bool IsActive, uint RunningAppId);
internal enum AddonOperationalStatus { Ready, WaitingForSteam, Passive, Unsupported, SetupRequired, Indeterminate, RecoveryRequired }
internal sealed record AddonStatusSnapshot(AddonOperationalStatus Status, string Reason);
internal sealed record SystemStatusSnapshot(
    DeviceStatusSnapshot Device,
    IReadOnlyList<ControllerSoftwareStatus> ControllerSoftware,
    ControllerEnvironmentCompatibilityAssessment Compatibility,
    RuntimePrerequisiteAssessment Prerequisites,
    SteamStatusSnapshot Steam,
    ExternalControllerAssessment ExternalController,
    RoutingDecision RoutingDecision,
    AddonStatusSnapshot Addon);

internal interface IDeviceInformationProvider { DeviceStatusSnapshot Capture(); }
internal interface ISystemStatusProvider { Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default); }
internal interface IControllerSoftwareStatusProvider { ControllerSoftwareStatus Capture(); }
