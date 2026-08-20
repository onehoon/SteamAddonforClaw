using SteamInputAddonforClaw.Contracts.DeviceProfiles;
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

/// <summary>Whether the current Windows CPU Boost value for one side (AC or DC) is a known/mapped
/// <see cref="CpuBoostMode"/>, was read but did not map to any supported mode
/// (<see cref="Unknown"/>), or could not be read at all (<see cref="Unavailable"/>). Mirrors the
/// Runtime's own <c>CpuBoostReadStatus</c> so the frontend never has to guess/normalize a value the
/// Runtime itself does not consider known (work order section 18).</summary>
public enum FrontendCpuBoostReadStatus { Known, Unknown, Unavailable }

/// <summary>Distinguishes persistence failing before any Windows write was attempted (the previous
/// desired value stays authoritative -- refresh/restore to it) from the Windows apply itself failing
/// after the new desired value was already durably persisted (that new value IS now authoritative
/// and will be retried later -- never roll the UI back to the old value for this outcome). See work
/// order section 7.</summary>
public enum FrontendCpuBoostMutationOutcome { Succeeded, PersistenceFailed, ApplyFailed }

/// <summary>One side (AC or DC) of the CPU Boost frontend snapshot: the actual current Windows value
/// (<see cref="CurrentStatus"/>/<see cref="Current"/>) versus the Addon's persisted desired value
/// (<see cref="Desired"/>, <see langword="null"/> only while Device CPU Boost is uninitialized). The
/// two are deliberately kept separate -- displaying the current Windows value must never be confused
/// with the Addon owning/persisting that value ("showing a value does NOT mean the Addon owns that
/// value").</summary>
public sealed record FrontendCpuBoostSideSnapshot(FrontendCpuBoostReadStatus CurrentStatus, CpuBoostMode? Current, CpuBoostMode? Desired);

/// <summary>Narrowly CPU-Boost-specific frontend snapshot (work order section 5) -- not a
/// generalized device-setting/profile framework. <see cref="PersistenceWritable"/> false means the
/// last profile load was unsafe to replace (malformed/unsupported schema/read failure): the frontend
/// must disable CPU Boost mutation rather than risk overwriting that file (work order section 19).
///
/// <see cref="Enabled"/> (Device CPU Boost Toggle addendum) controls only whether the Addon applies
/// the Device/global <see cref="FrontendCpuBoostSideSnapshot.Desired"/> AC/DC values -- it is not an
/// application-wide CPU Boost master switch, and a future Game Profile CPU Boost path is not gated
/// by it. <see cref="FrontendCpuBoostSideSnapshot.Desired"/> remains populated while
/// <see cref="Enabled"/> is <see langword="false"/> so the UI can keep showing (and re-enable) the
/// saved selections.</summary>
public sealed record FrontendCpuBoostSnapshot(FrontendCpuBoostSideSnapshot Ac, FrontendCpuBoostSideSnapshot Dc, bool Enabled, bool PersistenceWritable, string? LastFailure)
{
    public static readonly FrontendCpuBoostSnapshot Unavailable = new(
        new(FrontendCpuBoostReadStatus.Unavailable, null, null),
        new(FrontendCpuBoostReadStatus.Unavailable, null, null),
        Enabled: false, PersistenceWritable: false, LastFailure: null);
}

/// <summary>Result of a single-side CPU Boost mutation. Never discards the Runtime's
/// <see cref="FrontendCpuBoostMutationOutcome"/> distinction into a plain bool (work order section
/// 6/7) -- callers must be able to tell <see cref="FrontendCpuBoostMutationOutcome.PersistenceFailed"/>
/// apart from <see cref="FrontendCpuBoostMutationOutcome.ApplyFailed"/> to react correctly.</summary>
public sealed record FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome Outcome, string? FailureMessage, FrontendCpuBoostSnapshot Snapshot)
{
    public bool Succeeded => Outcome == FrontendCpuBoostMutationOutcome.Succeeded;
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
    /// <summary>Captures the current CPU Boost frontend snapshot. Never mutates anything -- opening
    /// the Device page and capturing this snapshot must cause zero ProfileStore/Windows writes
    /// (work order section 8/21).</summary>
    Task<FrontendCpuBoostSnapshot> CaptureCpuBoostAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendCpuBoostSnapshot.Unavailable);
    /// <summary>Sets the persisted/desired AC CPU Boost mode and applies it to Windows. DC is left
    /// completely untouched (work order section 10).</summary>
    Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable));
    /// <summary>Sets the persisted/desired DC CPU Boost mode and applies it to Windows. AC is left
    /// completely untouched (work order section 10).</summary>
    Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable));
    /// <summary>Turns the Device/global CPU Boost apply path on or off (Device CPU Boost Toggle
    /// addendum). Not an application-wide CPU Boost switch, and never gates a future Game Profile
    /// CPU Boost path. Turning it off performs no restoration -- Windows is left exactly as it is,
    /// and the saved AC/DC selections are preserved so turning it back on immediately re-applies
    /// them.</summary>
    Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.PersistenceFailed, "CPU Boost is unavailable.", FrontendCpuBoostSnapshot.Unavailable));
}
