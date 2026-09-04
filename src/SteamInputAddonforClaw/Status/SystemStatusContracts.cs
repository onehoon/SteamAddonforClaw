using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Status;

internal sealed record DeviceStatusSnapshot(string Manufacturer, string Model, string BaseBoardProduct, IReadOnlyList<string> GpuModels);

internal enum ControllerSoftwareKind { MsiCenterM, ClawTweaks, HandheldCompanion, Winhanced }
internal enum SoftwareInstallationStatus { Installed, NotInstalled, Indeterminate }
internal enum SoftwareRuntimeStatus { Running, NotRunning, Starting, Indeterminate }
internal sealed record ControllerSoftwareStatus(ControllerSoftwareKind Kind, string DisplayName, SoftwareInstallationStatus Installation, SoftwareRuntimeStatus Runtime, string Reason);
internal sealed record SteamStatusSnapshot(bool IsActive, uint RunningAppId, SteamSessionSource Source = SteamSessionSource.Actual);
internal enum AddonOperationalStatus { Ready, WaitingForSteam, Passive, Unsupported, SetupRequired, Indeterminate, RecoveryRequired }
internal sealed record AddonStatusSnapshot(AddonOperationalStatus Status, string Reason);
internal sealed record SystemStatusSnapshot(
    DeviceStatusSnapshot Device,
    HardwareCompatibilityAssessment HardwareCompatibility,
    IReadOnlyList<ControllerSoftwareStatus> ControllerSoftware,
    ControllerEnvironmentCompatibilityAssessment Compatibility,
    RuntimePrerequisiteAssessment Prerequisites,
    SteamStatusSnapshot Steam,
    AddonStatusSnapshot Addon,
    // This is a safety-boundary field with no safe default: a snapshot-construction
    // path that forgets to pass one must fail to compile rather than silently resolve to "safe".
    bool RecoverySafe);

internal interface IDeviceInformationProvider { DeviceStatusSnapshot Capture(DeviceProbeContext context); }
internal interface ISystemStatusProvider { Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default); }
internal interface IControllerSoftwareStatusProvider { ControllerSoftwareStatus Capture(); }
