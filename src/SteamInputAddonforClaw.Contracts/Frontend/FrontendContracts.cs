namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendLogLevel { Off, Info, Debug }
public enum FrontendSetupStatus { Complete, Required, Blocked, RestartRequired, NotApplicable, Indeterminate }
public enum FrontendHardwareStatus { Supported, Unsupported, Indeterminate }
public enum FrontendControllerEnvironmentStatus { Supported, Unsupported, Indeterminate }
public enum FrontendSteamSource { Actual, BigPicture, DeveloperTest, Indeterminate }
public enum FrontendSoftwareInstallationStatus { Installed, NotInstalled, Indeterminate }
public enum FrontendSoftwareRuntimeStatus { Running, NotRunning, Starting, Indeterminate }
public enum FrontendPrerequisiteStatus { Ready, Missing, Present, Unusable, Incompatible, Indeterminate }
public enum FrontendRoutingOperationalState { Passive, OverrideActive, Indeterminate }
public enum FrontendPrerequisiteSetupResultKind { Ready, Installed, RebootRequired, Cancelled, Blocked, Failed, AlreadyInProgress }
public enum FrontendRoutingEligibilityReason
{
    SteamInactive,
    AddonOwnedOutputIdentityUncertain,
    RecoveryUnsafe,
    UnsupportedDevice,
    DeviceCompatibilityIndeterminate,
    ControllerEnvironmentUnsupported,
    ControllerEnvironmentIndeterminate,
    PrerequisitesNotReady,
    Eligible,
    Indeterminate
}
public enum FrontendAddonOperationalStatus
{
    Ready,
    WaitingForSteam,
    Passive,
    Unsupported,
    SetupRequired,
    Indeterminate,
    RecoveryRequired
}

public sealed record FrontendSettingsSnapshot(bool LaunchAtWindowsStartup, FrontendLogLevel LogLevel, bool RouteInSteamBigPicture, bool SuppressDeveloperMenuWarning);
public sealed record FrontendDeveloperSnapshot(bool TestModeEnabled);
public sealed record FrontendBootstrapSnapshot(FrontendSettingsSnapshot Settings, string StartupRegistrationMessage, FrontendDeveloperSnapshot Developer, string LogDirectoryPath);
public sealed record FrontendLaunchAtStartupResult(FrontendSettingsSnapshot Settings, string RegistrationMessage);
public sealed record FrontendPrerequisiteSetupResult(FrontendPrerequisiteSetupResultKind Result, FrontendStatusSnapshot Status);
public sealed record FrontendEnvironmentReportResult(bool Succeeded, string? Error);
public sealed record FrontendDeviceSnapshot(string Manufacturer, string Model, string BaseBoard, IReadOnlyList<string> GpuModels);
public sealed record FrontendHardwareSnapshot(FrontendHardwareStatus Status, string? Family, string? Model, string Reason);
public sealed record FrontendSteamSnapshot(bool Active, uint AppId, FrontendSteamSource Source);
public sealed record FrontendSoftwareSnapshot(string Kind, string DisplayName, FrontendSoftwareInstallationStatus Installation, FrontendSoftwareRuntimeStatus Runtime, string Reason);
public sealed record FrontendPrerequisiteSnapshot(FrontendPrerequisiteStatus HidHideStatus, string HidHideReason, FrontendPrerequisiteStatus UsbIpStatus, string UsbIpReason, FrontendPrerequisiteStatus ViiperStatus, string ViiperReason);
public sealed record FrontendRoutingSnapshot(FrontendRoutingEligibilityReason EligibilityReason, FrontendRoutingOperationalState OperationalState, bool Available, bool SteamOutputActive, bool NativeDirectInputActive);
public sealed record FrontendStatusSnapshot(
    FrontendDeviceSnapshot Device,
    FrontendHardwareSnapshot Hardware,
    IReadOnlyList<FrontendSoftwareSnapshot> ControllerSoftware,
    FrontendControllerEnvironmentStatus ControllerEnvironmentStatus,
    string ControllerEnvironmentReason,
    FrontendPrerequisiteSnapshot Prerequisites,
    FrontendSteamSnapshot Steam,
    FrontendRoutingSnapshot Routing,
    FrontendAddonOperationalStatus AddonStatus,
    string AddonReason,
    bool RecoverySafe,
    bool AddonOwnedOutputIdentityUncertain,
    FrontendSetupStatus SetupStatus,
    string SetupReason,
    bool CanInstallRequiredComponents);

public interface IAddonFrontendControl
{
    event EventHandler? StateInvalidated;
    Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default);
    Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default);
    Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SetRouteInSteamBigPictureAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default);
    Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default);
    Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken cancellationToken = default);
}
