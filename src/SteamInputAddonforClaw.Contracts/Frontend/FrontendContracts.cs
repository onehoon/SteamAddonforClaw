using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendLogLevel { Off, Info, Debug }
public enum FrontendVibrationTestCommand { Rumble, Haptic, HapticPulse, Stop }
public enum FrontendSetupStatus { Complete, Required, Blocked, RestartRequired, NotApplicable, Indeterminate }
public enum FrontendHardwareStatus { Supported, Unsupported, Indeterminate }
public enum FrontendControllerEnvironmentStatus { Supported, Unsupported, Indeterminate }
public enum FrontendSteamSource { Actual, BigPicture, DeveloperTest, Indeterminate }
public enum FrontendSoftwareInstallationStatus { Installed, NotInstalled, Indeterminate }
public enum FrontendSoftwareRuntimeStatus { Running, NotRunning, Starting, Indeterminate }
public enum FrontendPrerequisiteStatus { Ready, Missing, Present, Unusable, Incompatible, Indeterminate }
public enum FrontendRoutingOperationalState { Passive, OverrideActive, Indeterminate }
public enum FrontendPrerequisiteSetupResultKind { Ready, Installed, RebootRequired, Cancelled, NotInstallable, Blocked, Failed, AlreadyInProgress }
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

/// <remarks><see cref="Oem1Mapping"/> is the settings-layer projection of the persisted OEM1 mapping.
/// The frontend deliberately carries the SAME <see cref="Oem1MappingSettings"/> the runtime persists
/// and the dispatcher validates against, rather than a parallel frontend-shaped copy -- the settings
/// UI and runtime capability validation must never be able to disagree.</remarks>
public sealed record FrontendSettingsSnapshot(bool LaunchAtWindowsStartup, FrontendLogLevel LogLevel, bool SteamInputRoutingEnabled, bool SuppressDeveloperMenuWarning, Oem1MappingSettings Oem1Mapping);
public sealed record FrontendDeveloperSnapshot(bool TestModeEnabled);
public sealed record FrontendVibrationTestResult(bool Succeeded, string Reason, string? LogFilePath);
/// <param name="Oem1MappingAvailable">Whether the Center M (OEM1) mapping feature exists at all on
/// this machine. It is the runtime's single startup hardware-support result (a supported MSI Claw),
/// NOT a routing/Steam/BPM/runtime condition, and NOT the persisted remapping switch -- a machine
/// that is not a recognized Claw reports false while its saved mapping stays untouched. A startup
/// fact, so it lives on bootstrap rather than on the settings snapshot every setter returns.</param>
public sealed record FrontendBootstrapSnapshot(FrontendSettingsSnapshot Settings, string StartupRegistrationMessage, FrontendDeveloperSnapshot Developer, string LogDirectoryPath, bool Oem1MappingAvailable);
public sealed record FrontendLaunchAtStartupResult(FrontendSettingsSnapshot Settings, string RegistrationMessage);
public sealed record FrontendPrerequisiteSetupResult(FrontendPrerequisiteSetupResultKind Result, FrontendStatusSnapshot? Status);
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
    Task<FrontendSettingsSnapshot> SetSteamInputRoutingEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default);
    /// <summary>Persists a COMPLETE new OEM1 mapping (remapping switch + all four slot bindings).
    /// Whole-record, not per-slot: it is what makes "turning remapping off never erases the mappings"
    /// structural rather than a rule each caller has to remember.</summary>
    Task<FrontendSettingsSnapshot> SetOem1MappingAsync(Oem1MappingSettings mapping, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default);
    Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<FrontendVibrationTestResult> RunVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendVibrationTestResult(false, "Vibration test is unavailable.", null));
    /// <summary>Opens the dedicated Vibration Test diagnostic session: creates the session log file
    /// (even if no command is ever run) and records a header with current Test Mode/routing state.
    /// Call when the Vibration Test detail page is entered, before/alongside the status refresh.
    /// Idempotent: a call while a session is already open returns that same session's file.</summary>
    Task<FrontendVibrationTestResult> OpenVibrationTestSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendVibrationTestResult(true, "SessionUnavailable", null));
    /// <summary>Closes the dedicated Vibration Test diagnostic session, if one is open: cancels any
    /// pending developer-owned delayed STOP, issues a best-effort production-path STOP, and flushes/
    /// closes the session log. Call when the Vibration Test detail page is left, regardless of how.</summary>
    Task<FrontendVibrationTestResult> CloseVibrationTestSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendVibrationTestResult(true, "NoSessionActive", null));
    Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default);
    Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken cancellationToken = default);
}
