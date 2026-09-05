using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.FrontButtons;

namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendLogLevel { Off, Info, Debug }
public enum FrontendProfileGameSource { Steam, NonSteam }
public sealed record FrontendProfileGameCatalogEntry(uint AppId, string Name, FrontendProfileGameSource Source, bool Favorite = false);
public sealed record FrontendGameCpuBoostConfiguration(bool Enabled, CpuBoostMode Ac, CpuBoostMode Dc);
public sealed record FrontendGameTdpConfiguration(bool Enabled, FrontendTdpPowerPair Ac, FrontendTdpPowerPair Dc);
public sealed record FrontendGamePowerModeConfiguration(bool Enabled, WindowsPowerMode Ac, WindowsPowerMode Dc);
public sealed record FrontendGameResolution(int Width, int Height);
public sealed record FrontendGameFpsLimitConfiguration(bool Enabled, int AcFps, int DcFps, bool Available, string? UnavailableReason = null);
public sealed record FrontendGameProfileSnapshot(uint AppId, string? DisplayName, bool Exists, bool Enabled,
    FrontendGameCpuBoostConfiguration CpuBoost, FrontendGameTdpConfiguration Tdp, bool PersistenceWritable, FrontendTdpLimits? Limits, FrontendGameResolution? Resolution = null, FrontendGamePowerModeConfiguration? PowerMode = null, FrontendGameFpsLimitConfiguration? FpsLimit = null);
public enum FrontendPowerModeReadStatus { Known, Unknown, Unavailable }
public enum FrontendPowerModeMutationOutcome { Succeeded, PersistenceFailed, ApplyFailed }
public sealed record FrontendPowerModeSideSnapshot(FrontendPowerModeReadStatus CurrentStatus, WindowsPowerMode? Current, WindowsPowerMode? Desired);
public sealed record FrontendPowerModeSnapshot(FrontendPowerModeSideSnapshot Ac, FrontendPowerModeSideSnapshot Dc, bool Enabled, bool PersistenceWritable, string? LastFailure)
{ public static readonly FrontendPowerModeSnapshot Unavailable = new(new(FrontendPowerModeReadStatus.Unavailable, null, null), new(FrontendPowerModeReadStatus.Unavailable, null, null), false, false, null); }
public sealed record FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome Outcome, string? FailureMessage, FrontendPowerModeSnapshot Snapshot)
{ public bool Succeeded => Outcome == FrontendPowerModeMutationOutcome.Succeeded; }
public enum FrontendGameProfileMutationOutcome { Succeeded, InvalidTarget, PersistenceFailed, ApplyFailed, Unavailable }
public sealed record FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome Outcome, string? FailureMessage, FrontendGameProfileSnapshot Snapshot)
{
    public bool Succeeded => Outcome == FrontendGameProfileMutationOutcome.Succeeded;
}
public enum FrontendSetupStatus { Complete, Required, Blocked, RestartRequired, NotApplicable, Indeterminate }
public enum FrontendHardwareStatus { Supported, Unsupported, Indeterminate }
public enum FrontendSteamSource { Actual, BigPicture, DeveloperTest, Indeterminate }
public enum FrontendPrerequisiteStatus { Ready, Missing, Present, Unusable, Incompatible, Indeterminate }
public enum FrontendPrerequisiteSetupResultKind { Ready, Installed, RebootRequired, Cancelled, NotInstallable, Blocked, Failed, AlreadyInProgress }
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

public enum FrontendTdpMutationOutcome { Succeeded, InvalidTarget, PersistenceFailed, Unavailable }
public enum FrontendTdpPowerSource { AC, DC }
public sealed record FrontendTdpPowerPair(int Pl1Watts, int Pl2Watts);
public sealed record FrontendTdpHardwareApplyResult(FrontendTdpPowerSource Source, int Pl1Watts, int Pl2Watts, bool Attempted, bool Succeeded);
public sealed record FrontendTdpLimits(int Pl1MinimumWatts, int Pl1MaximumWatts, int Pl2MinimumWatts, int Pl2MaximumWatts);
public sealed record FrontendTdpConfiguration(bool Enabled, FrontendTdpPowerPair Ac, FrontendTdpPowerPair Dc);
public sealed record FrontendTdpSnapshot(bool Available, bool PersistenceWritable, FrontendTdpConfiguration? Configuration, FrontendTdpLimits? Limits)
{
    public bool Initialized => Configuration is not null;
    public static readonly FrontendTdpSnapshot Unavailable = new(false, false, null, null);
}
public sealed record FrontendTdpMutationResult(FrontendTdpMutationOutcome Outcome, string? FailureMessage, FrontendTdpSnapshot Snapshot, FrontendTdpHardwareApplyResult? HardwareApply = null)
{
    public bool Succeeded => Outcome == FrontendTdpMutationOutcome.Succeeded;
}

/// <summary>Shared Device Quick Settings read projection (Shared Frontend V2, SF-V2-01) used by
/// Main UI and Steam QAM to read CPU Boost/TDP/Power Mode in one round trip. Each child is captured
/// independently, so one child being <see cref="FrontendCpuBoostSnapshot.Unavailable"/>/<see
/// cref="FrontendTdpSnapshot.Unavailable"/>/<see cref="FrontendPowerModeSnapshot.Unavailable"/> must
/// not imply the others are unavailable. This is a UI projection convenience only -- it is not a new
/// Device/Quick Settings authority and must not gain Center M, Status, active Profile, or other
/// feature members.</summary>
public sealed record FrontendDeviceQuickSettingsSnapshot(
    FrontendCpuBoostSnapshot CpuBoost,
    FrontendTdpSnapshot Tdp,
    FrontendPowerModeSnapshot PowerMode)
{
    public static readonly FrontendDeviceQuickSettingsSnapshot Unavailable = new(
        FrontendCpuBoostSnapshot.Unavailable,
        FrontendTdpSnapshot.Unavailable,
        FrontendPowerModeSnapshot.Unavailable);
}

public enum FrontendFanProbeState { Unavailable, Ready, Running, Completed, Failed }
public enum FrontendFanProbeOperation { Capture, AutomaticTest, RestoreAuto, PhysicalResponse, ArmSuspendResume }
public sealed record FrontendFanProbeSnapshot(bool Available, FrontendFanProbeState State, string Status, string Manufacturer, string Model, string BaseBoard, string ProbeModel, string? ReportPath, bool HasReport, string? ErrorMessage)
{
    public static readonly FrontendFanProbeSnapshot Unavailable = new(false, FrontendFanProbeState.Unavailable, "Unavailable", "", "", "", "Unsupported", null, false, "MSI fan probe is unavailable.");
}

/// <remarks><see cref="FrontButtonMapping"/> is the settings-layer projection of the one persisted
/// front-button mapping. The frontend deliberately carries the SAME
/// <see cref="FrontButtonMappingSettings"/> the runtime persists and the dispatcher validates
/// against, rather than a parallel frontend-shaped copy -- the settings UI and runtime capability
/// validation must never be able to disagree.</remarks>
public sealed record FrontendSettingsSnapshot(FrontendLogLevel LogLevel, bool SuppressDeveloperMenuWarning, FrontButtonMappingSettings FrontButtonMapping)
{
    public bool DeveloperMenuEnabled { get; init; }
}
public sealed record FrontendDeveloperSnapshot(bool TestModeEnabled);

/// <summary>Whether MSI Center M is configured to start with Windows, judged ONLY from the three
/// startup roots this feature owns (work order PR1): the <c>MSI_Center_M_Server</c> and
/// <c>MSI_Center_M_Updater</c> Scheduled Tasks' enabled state and the <c>MSI Foundation Service</c>
/// configured startup type. <see cref="Partial"/> is any mixed state -- it is surfaced, never
/// auto-repaired. <see cref="Unavailable"/> means a meaningful startup-configuration snapshot could
/// not be produced (feature not applicable to the detected hardware, the startup components could not
/// be identified, or Task Scheduler / SCM state could not be read) -- it is NOT used merely because a
/// privileged mutation helper failed to start or its UAC prompt was cancelled (PR1 Addendum E).
/// A <see cref="Disabled"/> configuration does NOT imply Center M is absent from the current Windows
/// session; the clean baseline only begins after a reboot (work order PR1 section 12).</summary>
public enum FrontendCenterMStartupState { Enabled, Disabled, Partial, Unavailable }

/// <summary>Narrowly Center-M-startup-specific snapshot (work order PR1 section 5/6) -- not a
/// generalized Windows service/task administration contract. The three booleans are the actual read
/// Windows state of each root; <see cref="State"/> is their classification. For the Foundation
/// Service, <see cref="FoundationServiceEnabled"/> reflects the configured startup type
/// (<c>Automatic</c>/<c>Manual</c> =&gt; enabled, <c>Disabled</c> =&gt; disabled) -- never whether the
/// service is currently Running (work order PR1 section 7 / Addendum F).</summary>
public sealed record FrontendCenterMStartupSnapshot(
    FrontendCenterMStartupState State,
    bool ServerTaskEnabled,
    bool UpdaterTaskEnabled,
    bool FoundationServiceEnabled,
    string? FailureMessage)
{
    public static readonly FrontendCenterMStartupSnapshot Unavailable =
        new(FrontendCenterMStartupState.Unavailable, false, false, false, null);
}

/// <summary>Outcome of one Enable/Disable action over the three startup roots (work order PR1
/// section 8/9, PR1 Addendum E). Never collapsed to a bool -- <see cref="Cancelled"/> (the user
/// dismissed the elevation prompt before the mutation completed) must be distinguishable from
/// <see cref="Failed"/> (the mutation was attempted but read-back did not verify the requested
/// configuration) and from <see cref="Unavailable"/> (the feature itself cannot be operated).</summary>
public enum FrontendCenterMStartupMutationOutcome { Succeeded, Cancelled, Failed, Unavailable }

/// <summary>Result of a Center M startup Enable/Disable. <see cref="Snapshot"/> is always the latest
/// actual three-root Windows state -- after <see cref="FrontendCenterMStartupMutationOutcome.Cancelled"/>
/// or <see cref="FrontendCenterMStartupMutationOutcome.Failed"/> it carries the real resulting state
/// (often <see cref="FrontendCenterMStartupState.Partial"/>), never a fabricated "requested"
/// state.</summary>
public sealed record FrontendCenterMStartupMutationResult(
    FrontendCenterMStartupMutationOutcome Outcome,
    FrontendCenterMStartupSnapshot Snapshot,
    string? FailureMessage)
{
    public bool Succeeded => Outcome == FrontendCenterMStartupMutationOutcome.Succeeded;
}
/// <param name="FrontButtonMappingAvailable">Whether the front-button mapping feature (Gamebar
/// Button + Center M Button) exists at all on this machine. It is the runtime's single startup
/// hardware-support result (a supported MSI Claw), NOT a Steam/BPM/presentation/Overlay/Win+G runtime
/// condition -- a machine that is not a recognized Claw reports false while its saved mapping stays
/// untouched. A startup fact, so it lives on bootstrap rather than on the settings snapshot every
/// setter returns.</param>
public sealed record FrontendBootstrapSnapshot(FrontendSettingsSnapshot Settings, FrontendDeveloperSnapshot Developer, string LogDirectoryPath, bool FrontButtonMappingAvailable);
public sealed record FrontendPrerequisiteSetupResult(FrontendPrerequisiteSetupResultKind Result, FrontendStatusSnapshot? Status);
public sealed record FrontendEnvironmentReportResult(bool Succeeded, string? Error);
public sealed record FrontendDeviceSnapshot(string Manufacturer, string Model, string BaseBoard, IReadOnlyList<string> GpuModels);
public sealed record FrontendHardwareSnapshot(FrontendHardwareStatus Status, string? Family, string? Model, string Reason);
public sealed record FrontendSteamSnapshot(bool Active, uint AppId, FrontendSteamSource Source);
public sealed record FrontendPrerequisiteSnapshot(FrontendPrerequisiteStatus HidHideStatus, string HidHideReason, FrontendPrerequisiteStatus UsbIpStatus, string UsbIpReason, FrontendPrerequisiteStatus ViiperStatus, string ViiperReason);
public enum FrontendClawSensorProbeState { Idle, Discovering, Ready, Starting, Countdown, RecordingPhase, Stopping, Completed, Failed }
public enum FrontendClawSensorProbePhase { Rest, RollLeft, RollRight, PitchUp, PitchDown, YawLeft, YawRight }
/// <summary>The diagnostic session's purpose, chosen once at Start and immutable for the life of the
/// session (docs/gyro/SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md).</summary>
public enum FrontendClawSensorProbeMode { LiveSanity, AxisCharacterization, StationaryBias }
public enum FrontendClawSensorProbeBackend { LegacySensorApi, WinRtGyrometer, WinRtAccelerometer }
public enum FrontendClawSensorProbeUnitBasis { Unknown, DegreesPerSecond, G }

public sealed record FrontendClawSensorProbeCandidate(
    string FriendlyName, string SensorId, string TypeGuid, string CategoryGuid, string Manufacturer, string Model, string PersistentUniqueId, string MinimumReportInterval, string CustomUsage,
    FrontendClawSensorProbeBackend Backend = FrontendClawSensorProbeBackend.LegacySensorApi,
    string State = "Unavailable",
    string DevicePath = "Unavailable",
    FrontendClawSensorProbeUnitBasis UnitBasis = FrontendClawSensorProbeUnitBasis.Unknown,
    string? SelectionReason = null);
public sealed record FrontendClawSensorProbeDiscovery(IReadOnlyList<FrontendClawSensorProbeCandidate> Sensors, FrontendClawSensorProbeCandidate? Gyroscope, FrontendClawSensorProbeCandidate? Accelerometer, IReadOnlyList<string> Errors, bool IsValid);
public sealed record FrontendClawSensorProbeAxisSnapshot(
    double X, double Y, double Z, double Hz, long Count,
    double FreshAgeMs = 0,
    double LastReadDurationMs = 0,
    bool IsFresh = false,
    double? MagnitudeG = null)
{
    public static readonly FrontendClawSensorProbeAxisSnapshot Empty = new(0, 0, 0, 0, 0);
}
public sealed record FrontendClawSensorProbeStatistics(long SampleCount, long DroppedSampleCount, double DurationMs, double AverageIntervalMs, double MinimumIntervalMs, double MaximumIntervalMs, double EffectiveHz)
{
    public static readonly FrontendClawSensorProbeStatistics Empty = new(0, 0, 0, 0, 0, 0, 0);
}
/// <summary>Compact per-source timing/freshness evidence for UI display -- a narrower projection of
/// the Runtime's <c>ClawSensorProbeTimingSnapshot</c>. Raw per-attempt history never crosses the pipe.</summary>
public sealed record FrontendClawSensorProbeTiming(
    long FreshCount, long DuplicateCount, long NoDataCount, long ReadFailureCount,
    double EffectiveFreshHz, double LastReadDurationMs, double MaxReadDurationMs,
    double FreshAgeMs, double MaxFreshAgeMs, long LongReadCount)
{
    public static readonly FrontendClawSensorProbeTiming Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
/// <summary>Compact Stationary Bias completion summary sufficient for the UI (detailed per-phase
/// summaries stay JSON-report-only). Null until a StationaryBias session has recorded data.</summary>
public sealed record FrontendClawSensorProbeBiasSummary(
    long GyroSampleCount, double GyroEffectiveHz,
    double GyroMeanX, double GyroMeanY, double GyroMeanZ,
    double GyroStandardDeviationX, double GyroStandardDeviationY, double GyroStandardDeviationZ,
    double GyroSpanX, double GyroSpanY, double GyroSpanZ,
    long AccelSampleCount, double AccelEffectiveHz,
    double AccelSpanX, double AccelSpanY, double AccelSpanZ,
    double? AccelMagnitudeGMean, double? AccelMagnitudeGSpan);

/// <remarks>A read-only diagnostic session snapshot for the developer-only Claw Sensor Probe
/// (gyro/accelerometer discovery and phase-by-phase motion capture). <see cref="Available"/> is
/// gated purely on the MSI Claw device family (see <c>ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic</c>
/// on the Runtime side) -- NOT on production hardware-compatibility status, Developer Test Mode, or
/// any Steam/routing state, so an MSI Claw with Indeterminate/Unsupported model compatibility can
/// still run this diagnostic.</remarks>
public sealed record FrontendClawSensorProbeSnapshot(
    bool Available,
    FrontendClawSensorProbeState State,
    FrontendClawSensorProbePhase Phase,
    int PhaseIndex,
    int PhaseCount,
    FrontendClawSensorProbeDiscovery? Discovery,
    FrontendClawSensorProbeAxisSnapshot Gyro,
    FrontendClawSensorProbeAxisSnapshot Accel,
    FrontendClawSensorProbeStatistics? GyroscopeSummary,
    FrontendClawSensorProbeStatistics? AccelerometerSummary,
    long DroppedSampleCount,
    long DroppedGyroscopeCount,
    long DroppedAccelerometerCount,
    IReadOnlyList<string> ReaderErrors,
    string? OutputDirectory,
    bool HasReport,
    string? ErrorMessage,
    string Manufacturer,
    string Model,
    string BaseBoard,
    string ResolvedModel,
    FrontendClawSensorProbeMode? Mode = null,
    double ElapsedMs = 0,
    FrontendClawSensorProbeTiming? GyroTiming = null,
    FrontendClawSensorProbeTiming? AccelTiming = null,
    FrontendClawSensorProbeBiasSummary? BiasSummary = null)
{
    public static readonly FrontendClawSensorProbeSnapshot Unavailable = new(
        false, FrontendClawSensorProbeState.Idle, FrontendClawSensorProbePhase.Rest, -1, 0,
        null, FrontendClawSensorProbeAxisSnapshot.Empty, FrontendClawSensorProbeAxisSnapshot.Empty,
        null, null, 0, 0, 0, [], null, false, "The Claw Sensor Probe diagnostic is unavailable.",
        "Unavailable", "Unavailable", "Unavailable", "Unknown / unresolved");
}

public sealed record FrontendStatusSnapshot(
    FrontendDeviceSnapshot Device,
    FrontendHardwareSnapshot Hardware,
    FrontendPrerequisiteSnapshot Prerequisites,
    FrontendSteamSnapshot Steam,
    FrontendAddonOperationalStatus AddonStatus,
    string AddonReason,
    bool RecoverySafe,
    FrontendSetupStatus SetupStatus,
    string SetupReason,
    bool CanInstallRequiredComponents);

public interface IAddonFrontendControl
{
    event EventHandler? StateInvalidated;
    Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default);
    Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default);
    /// <summary>Persists the COMPLETE new front-button mapping (both buttons, both domains --
    /// four bindings). Whole-record, not per-binding: the cross-button same-domain uniqueness rule
    /// belongs to one whole mapping. An invalid candidate is rejected by the settings layer and the
    /// returned snapshot reflects the unchanged persisted state.</summary>
    Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(FrontButtonMappingSettings mapping, CancellationToken cancellationToken = default);
    Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default);
    Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default);
    /// <summary>Captures the current MSI Center M startup configuration (work order PR1). Read-only:
    /// opening the Device page and capturing this must not mutate any Windows state.</summary>
    Task<FrontendCenterMStartupSnapshot> CaptureCenterMStartupAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendCenterMStartupSnapshot.Unavailable);
    /// <summary>Requests a reboot-bound MSI Center M controller-authority transition (work order PR3):
    /// the Runtime verifies mandatory startup, applies/clears the persistent Addon HidHide baseline,
    /// mutates and read-back-verifies the three Center M startup roots, and then immediately requests
    /// a Windows restart. It never performs a live same-session physical-controller takeover.
    /// <see cref="FrontendCenterMStartupMutationOutcome.Succeeded"/> means the persistent target was
    /// verified AND the restart request was issued; if only the restart request failed the result is
    /// <see cref="FrontendCenterMStartupMutationOutcome.Failed"/> with the real snapshot and a
    /// manual-restart message.</summary>
    /// <param name="centerMEnabled"><see langword="true"/> = Enable and Restart (restore MSI/stock
    /// authority); <see langword="false"/> = Disable and Restart (switch authority to the Addon).</param>
    Task<FrontendCenterMStartupMutationResult> RequestCenterMAuthorityTransitionAsync(bool centerMEnabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendCenterMStartupMutationResult(FrontendCenterMStartupMutationOutcome.Unavailable, FrontendCenterMStartupSnapshot.Unavailable, "MSI Center M controller authority control is unavailable."));
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
    Task<FrontendPowerModeSnapshot> CapturePowerModeAsync(CancellationToken cancellationToken = default) => Task.FromResult(FrontendPowerModeSnapshot.Unavailable);
    Task<FrontendPowerModeMutationResult> SetDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable.", FrontendPowerModeSnapshot.Unavailable));
    Task<FrontendPowerModeMutationResult> SetDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable.", FrontendPowerModeSnapshot.Unavailable));
    Task<FrontendPowerModeMutationResult> SetDevicePowerModeEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable.", FrontendPowerModeSnapshot.Unavailable));
    Task<FrontendTdpSnapshot> CaptureTdpAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendTdpSnapshot.Unavailable);
    Task<FrontendTdpMutationResult> SetDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Unavailable, "TDP is unavailable.", FrontendTdpSnapshot.Unavailable));
    Task<FrontendTdpMutationResult> SetDeviceTdpEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Unavailable, "TDP is unavailable.", FrontendTdpSnapshot.Unavailable));
    /// <summary>Shared Device Quick Settings aggregate read (Shared Frontend V2, SF-V2-01): captures
    /// CPU Boost/TDP/Power Mode in one round trip for Main UI/QAM Device refresh. A UI projection
    /// convenience over <see cref="CaptureCpuBoostAsync"/>/<see cref="CaptureTdpAsync"/>/<see
    /// cref="CapturePowerModeAsync"/> -- it must not replace those focused methods, must not persist
    /// or mutate anything, and one child capture failing must not discard healthy siblings.</summary>
    Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendDeviceQuickSettingsSnapshot.Unavailable);
    /// <summary>Shared Quick Settings product projection (Shared Frontend V2, SF-V2-03): captures one
    /// page's rows in the closed shared product shape. Read-only, like <see
    /// cref="CaptureDeviceQuickSettingsAsync"/> -- must not persist/mutate/reconcile. The default
    /// fails closed for any implementation (test double or otherwise) that does not opt in.</summary>
    Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(QuickSettingsPageId pageId, uint? appId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(QuickSettingsPageSnapshot.Unavailable(pageId, appId));
    /// <summary>Validates and dispatches a shared Quick Settings mutation intent onto the existing
    /// typed Device mutation methods, then returns a freshly re-projected page (work order section
    /// 21/28). The default fails closed without fabricating a successful mutation.</summary>
    Task<QuickSettingsMutationResult> MutateQuickSettingAsync(QuickSettingsMutationIntent intent, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuickSettingsMutationResult(false, "Quick Settings are unavailable.", QuickSettingsPageSnapshot.Unavailable(intent.PageId, intent.AppId)));
    Task<FrontendFanProbeSnapshot> OpenFanProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(FrontendFanProbeSnapshot.Unavailable);
    Task<FrontendFanProbeSnapshot> RunFanProbeAsync(FrontendFanProbeOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(FrontendFanProbeSnapshot.Unavailable);
    Task<IReadOnlyList<FrontendProfileGameCatalogEntry>> ScanProfileGamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FrontendProfileGameCatalogEntry>>([]);
    Task<FrontendGameProfileSnapshot> CaptureGameProfileAsync(uint appId, CancellationToken cancellationToken = default) => Task.FromResult(FrontendGameProfileSnapshotUnavailable(appId));
    Task<FrontendGameProfileSnapshot> CaptureActiveGameProfileAsync(CancellationToken cancellationToken = default) => Task.FromResult(FrontendGameProfileSnapshotUnavailable(0));
    Task<FrontendGameProfileMutationResult> SetGameProfileEnabledAsync(uint appId, bool enabled, string? displayName, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileTdpEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostAcAsync(uint appId, CpuBoostMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostDcAsync(uint appId, CpuBoostMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeAcAsync(uint appId, WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeDcAsync(uint appId, WindowsPowerMode mode, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitEnabledAsync(uint appId, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Intel FPS Limit is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitAcAsync(uint appId, int fps, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Intel FPS Limit is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitDcAsync(uint appId, int fps, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Intel FPS Limit is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileTdpAsync(uint appId, FrontendGameTdpConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Game Profile is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileFavoriteAsync(uint appId, bool favorite, string? displayName, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Favorites are unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));
    Task<FrontendGameProfileMutationResult> SetGameProfileResolutionAsync(uint appId, FrontendGameResolution? resolution, string? displayName, CancellationToken cancellationToken = default) => Task.FromResult(new FrontendGameProfileMutationResult(FrontendGameProfileMutationOutcome.Unavailable, "Display resolution is unavailable.", FrontendGameProfileSnapshotUnavailable(appId)));

    // ---- Claw Sensor Probe (developer-only gyro/accelerometer diagnostic) ----
    /// <summary>Opens (or re-opens, if the previous session Completed/Failed) the diagnostic session:
    /// evaluates eligibility, and if eligible prepares the Runtime-owned coordinator and records
    /// device identity/hardware compatibility. Idempotent while a session is already open/running.</summary>
    Task<FrontendClawSensorProbeSnapshot> OpenClawSensorProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Starts sensor discovery and capture for the currently open session using the
    /// requested diagnostic mode (Live Sanity / Axis Characterization / Stationary Bias). The mode is
    /// immutable for the life of the session once accepted.</summary>
    Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(FrontendClawSensorProbeMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Returns the current snapshot without mutating anything -- used for UI polling.</summary>
    Task<FrontendClawSensorProbeSnapshot> CaptureClawSensorProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Axis Characterization only -- a no-op (unchanged snapshot) outside that mode.</summary>
    Task<FrontendClawSensorProbeSnapshot> NextClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Axis Characterization only -- a no-op (unchanged snapshot) outside that mode.</summary>
    Task<FrontendClawSensorProbeSnapshot> PreviousClawSensorProbePhaseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Stops capture, keeping the session open so the final Completed/Failed report stays
    /// visible until the page is closed.</summary>
    Task<FrontendClawSensorProbeSnapshot> StopClawSensorProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);
    /// <summary>Closes the diagnostic session, if one is open: stops any in-progress capture and
    /// disposes the Runtime-owned coordinator. Call when the page is left, regardless of how.</summary>
    Task<FrontendClawSensorProbeSnapshot> CloseClawSensorProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FrontendClawSensorProbeSnapshot.Unavailable);

    private static FrontendGameProfileSnapshot FrontendGameProfileSnapshotUnavailable(uint appId) => new(appId, null, false, false,
        new(false, CpuBoostMode.Enabled, CpuBoostMode.Enabled), new(false, new(20, 22), new(20, 22)), false, null, FpsLimit: new(false, 60, 60, false, "Intel FPS Limit is unavailable."));
}
