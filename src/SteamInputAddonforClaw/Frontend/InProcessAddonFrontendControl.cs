using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Frontend;

internal sealed class InProcessAddonFrontendControl : IAddonFrontendControl
{
    private readonly StartupSettingsCoordinator _settings;
    private readonly ISystemStatusProvider _status;
    private readonly AddonRuntimeHost? _runtime;
    private readonly Func<RoutingRuntimeStatusSnapshot> _captureRoutingStatus;
    private readonly DeveloperTestModeState _developer;
    private string _registrationMessage;
    private readonly IFrontendPrerequisiteSetupExecutor _setupExecutor;
    private readonly Func<string?> _processPath;
    private readonly bool _oem1MappingAvailable;
    private int _shutdownStarted;
    private readonly object _vibrationSessionGate = new();
    private Feedback.VibrationTestSessionWriter? _vibrationSession;

    /// <param name="oem1MappingAvailable">The startup hardware-support result
    /// (<see cref="Startup.StartupResult.HardwareSupported"/>), reported verbatim on bootstrap so the
    /// UI gates the Center M Button feature on the SAME fact the routing composition's OEM1 action
    /// path gates on. Defaults to false so any construction path that never established hardware
    /// support reports the feature unavailable rather than offering it.</param>
    internal InProcessAddonFrontendControl(StartupSettingsCoordinator settings, ISystemStatusProvider status, AddonRuntimeHost? runtime, DeveloperTestModeState developer, string registrationMessage, IFrontendPrerequisiteSetupExecutor? setupExecutor = null, Func<string?>? processPath = null, Func<RoutingRuntimeStatusSnapshot>? captureRoutingStatus = null, bool oem1MappingAvailable = false)
    {
        _oem1MappingAvailable = oem1MappingAvailable;
        _settings = settings;
        _status = status;
        _runtime = runtime;
        _captureRoutingStatus = captureRoutingStatus ?? (() => _runtime?.CaptureRoutingStatus() ?? throw new InvalidOperationException("Routing status is unavailable."));
        _developer = developer;
        _registrationMessage = registrationMessage;
        _setupExecutor = setupExecutor ?? new FrontendPrerequisiteSetupExecutor();
        _processPath = processPath ?? (() => Environment.ProcessPath);
        if (_runtime is not null)
        {
            _runtime.SteamSessionStateChanged += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
            _runtime.StatusRefreshRequested += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StateInvalidated;

    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FrontendBootstrapSnapshot(MapSettings(), _registrationMessage, new(_developer.IsEnabled), AppLog.DirectoryPath, _oem1MappingAvailable));

    public async Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(snapshot);
        return FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(snapshot, _captureRoutingStatus()), setup);
    }

    public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var result = _settings.ChangeLaunchAtWindowsStartup(enabled);
        _registrationMessage = result.Message;
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendLaunchAtStartupResult(MapSettings(), _registrationMessage));
    }

    public Task<FrontendSettingsSnapshot> SetSteamInputRoutingEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.ChangeSteamInputRoutingEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.ChangeLogLevel(level switch { FrontendLogLevel.Debug => AppLogPreference.Debug, FrontendLogLevel.Info => AppLogPreference.Info, _ => AppLogPreference.Off });
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetOem1MappingAsync(Contracts.Oem1.Oem1MappingSettings mapping, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ThrowIfShuttingDown();
        _settings.ChangeOem1Mapping(mapping);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _settings.SuppressDeveloperMenuWarningPermanently();
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        _developer.SetEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendDeveloperSnapshot(_developer.IsEnabled));
    }

    /// <summary>Called when the Vibration Test detail page is entered: creates the dedicated session
    /// log immediately (even if no command is ever run) so the file always has a header recording
    /// current Test Mode/routing state at page entry. Idempotent -- a call while a session is
    /// already open returns that same session's file rather than starting a second one.</summary>
    public Task<FrontendVibrationTestResult> OpenVibrationTestSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var session = GetOrOpenVibrationSession();
        return Task.FromResult(new FrontendVibrationTestResult(true, "SessionOpened", session.FilePath));
    }

    public async Task<FrontendVibrationTestResult> RunVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var testModeEnabled = _developer.IsEnabled;
        var steamOutputActive = _captureRoutingStatus().SteamOutputActive;
        if (!testModeEnabled) return new FrontendVibrationTestResult(false, "Enable Test Mode from Developer Menu.", null);
        if (!steamOutputActive) return new FrontendVibrationTestResult(false, "Steam Deck output is not active.", null);

        var session = GetOrOpenVibrationSession();
        WriteVibrationSessionIfCurrent(session, $"Command={command} Opcode={VibrationTestOpcode(command)} TestModeEnabled={testModeEnabled} SteamOutputActive={steamOutputActive}");
        var outcome = await (_runtime?.RunDeveloperVibrationTestAsync(command, cancellationToken) ?? Task.FromResult(new Feedback.DeveloperVibrationTestOutcome(false, null, null))).ConfigureAwait(false);
        // Accepted (authority/sequence) and physically-successful are different questions: a real MSI
        // HID write failure must be visible here even when the write was accepted, so PhysicalStatus/
        // PhysicalReason are logged separately from Succeeded rather than folded into one boolean.
        WriteVibrationSessionIfCurrent(session, $"Result Command={command} Succeeded={outcome.Succeeded} {DecodeFields(outcome.Decode)} PhysicalStatus={outcome.CommandResult?.Status} PhysicalReason={outcome.CommandResult?.Reason} StopPhysicalStatus={outcome.StopResult?.Status} StopPhysicalReason={outcome.StopResult?.Reason}");

        var (succeeded, reason) = MapVibrationTestOutcome(outcome);
        return new FrontendVibrationTestResult(succeeded, reason, session.FilePath);
    }

    /// <summary>Pure mapping, factored out for direct unit testing: <see cref="Feedback.DeveloperVibrationTestOutcome.Succeeded"/>
    /// only means the write was ACCEPTED (authority/sequence), not that the physical MSI HID write
    /// succeeded -- a truthful diagnostic result must require the actual write(s) to have succeeded
    /// too, or a real hardware failure would be reported to the page as "Succeeded".</summary>
    internal static (bool Succeeded, string Reason) MapVibrationTestOutcome(Feedback.DeveloperVibrationTestOutcome outcome)
    {
        var commandPhysicalOk = outcome.CommandResult is { } commandResult && commandResult.Succeeded;
        var stopPhysicalOk = outcome.StopResult is null || outcome.StopResult.Value.Succeeded;
        var succeeded = outcome.Succeeded && commandPhysicalOk && stopPhysicalOk;
        var reason = !outcome.Succeeded
            ? "Feedback bridge is unavailable, superseded, or the test was cancelled."
            : !commandPhysicalOk
                ? $"Physical write failed: {outcome.CommandResult?.Reason ?? "Unknown"}"
                : !stopPhysicalOk
                    ? $"Physical STOP failed: {outcome.StopResult?.Reason ?? "Unknown"}"
                    : "Succeeded";
        return (succeeded, reason);
    }

    /// <summary>Test-only seam so the disposed-writer race fix can be exercised directly without
    /// reproducing the exact 250ms async interleaving through a real runtime.</summary>
    internal Feedback.VibrationTestSessionWriter? TestOnly_CurrentVibrationSession { get { lock (_vibrationSessionGate) return _vibrationSession; } }

    /// <summary>Writes to the session only if it is still the currently open one: a page exit can
    /// close (detach + dispose) the session while an in-flight EB/EA developer command is still
    /// awaiting its 250ms delayed STOP, and writing to an already-disposed <c>StreamWriter</c> would
    /// throw from an <c>async void</c> UI click handler. Serializes against the same
    /// <see cref="_vibrationSessionGate"/> <see cref="CloseVibrationTestSessionAsync"/> detaches
    /// under, so a write either completes before detach or observes the session is stale and no-ops.</summary>
    internal void WriteVibrationSessionIfCurrent(Feedback.VibrationTestSessionWriter session, string message)
    {
        lock (_vibrationSessionGate)
        {
            if (ReferenceEquals(_vibrationSession, session)) session.Write(message);
        }
    }

    /// <summary>Called when the Vibration Test detail page is left, regardless of how: cancels any
    /// pending developer-owned delayed STOP so it cannot later stop newer real Steam feedback, issues
    /// a best-effort production-path STOP, and flushes/closes the dedicated session log.</summary>
    public async Task<FrontendVibrationTestResult> CloseVibrationTestSessionAsync(CancellationToken cancellationToken = default)
    {
        Feedback.VibrationTestSessionWriter? session;
        lock (_vibrationSessionGate) { session = _vibrationSession; _vibrationSession = null; }
        if (session is null) return new FrontendVibrationTestResult(true, "NoSessionActive", null);

        var stop = _runtime?.CancelDeveloperVibrationTest();
        session.Write($"SessionClosed CancelledPendingDeveloperStop=True BestEffortStopRequested=True PhysicalStatus={stop?.Status} PhysicalReason={stop?.Reason}");
        var path = session.FilePath;
        await session.DisposeAsync().ConfigureAwait(false);
        return new FrontendVibrationTestResult(true, "SessionClosed", path);
    }

    private Feedback.VibrationTestSessionWriter GetOrOpenVibrationSession()
    {
        lock (_vibrationSessionGate)
        {
            if (_vibrationSession is { } existing) return existing;
            var session = new Feedback.VibrationTestSessionWriter(AppLog.DirectoryPath);
            var routing = _captureRoutingStatus();
            session.Write($"SessionStarted TestModeEnabled={_developer.IsEnabled} SteamOutputActive={routing.SteamOutputActive} NativeDirectInputActive={routing.NativeDirectInputActive}");
            _vibrationSession = session;
            return session;
        }
    }

    private static string VibrationTestOpcode(FrontendVibrationTestCommand command) => command switch
    {
        FrontendVibrationTestCommand.Rumble => "0xEB",
        FrontendVibrationTestCommand.Haptic => "0xEA",
        FrontendVibrationTestCommand.HapticPulse => "0x8F",
        FrontendVibrationTestCommand.Stop => "0xEB(zero)",
        _ => "Unknown"
    };

    private static string DecodeFields(Feedback.SteamDeckFeedbackDecodeResult? decoded) => decoded switch
    {
        { Command: Feedback.SteamDeckFeedbackCommand.Rumble, Rumble: var rumble } => $"Decode=Rumble Large16={rumble.LargeMotor} Small16={rumble.SmallMotor} Large8={rumble.LargeMotor / 257} Small8={rumble.SmallMotor / 257}",
        { Command: Feedback.SteamDeckFeedbackCommand.Haptic, Intensity: var intensity, Gain: var gain, Strength8: var strength } => $"Decode=Haptic Intensity={intensity} Gain={gain} Strength8={strength}",
        { Command: Feedback.SteamDeckFeedbackCommand.HapticPulse, PulsePeriod: var period, PulseCount: var count, Gain: var gain, Strength8: var strength, PulseDurationMilliseconds: var duration } => $"Decode=HapticPulse Period={period} Count={count} Gain={gain} Strength8={strength} PulseDurationMs={duration}",
        _ => "Decode=Unavailable"
    };

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var current = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(current);
        AppLog.Info("PrerequisiteSetup", "Prerequisite setup requested.",
            ("HidHideStatus", current.Prerequisites.HidHide.Status),
            ("UsbIpWin2Status", current.Prerequisites.UsbIpWin2.Status),
            ("CompatibilityStatus", current.Compatibility.Status),
            ("CompatibilityReason", current.Compatibility.Reason),
            ("SteamActive", current.Steam.IsActive),
            ("RecoverySafe", current.RecoverySafe),
            ("AddonOwnedOutputIdentityUncertain", current.AddonOwnedOutputIdentityUncertain),
            ("SetupStatus", setup.Status));
        var mapped = FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(current, _captureRoutingStatus()), setup);
        if (!PrerequisiteSetupPromptPolicy.IsInstallable(setup))
            return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        ThrowIfShuttingDown();
        var executable = _processPath() ?? throw new InvalidOperationException("The executable path is unavailable.");
        var result = await _setupExecutor.RunAsync(setup, executable, cancellationToken).ConfigureAwait(false);
        // RunIfInstallableAsync returns null only when its safety policy declines to launch.
        // Preserve that distinction from an elevated helper that actually returns Blocked.
        if (result is null) return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        var resultKind = MapResultKind(ElevatedPrerequisiteSetup.TranslateExitCode(result));
        // No OEM1 reconcile here: HidHide/usbip setup no longer mutates any OEM1 prerequisite. OEM1
        // arming is owned entirely by the mapping-change/startup lifecycle plus the coordinator's own
        // environment/Launcher/Server/process/helper reconciliation.
        FrontendStatusSnapshot? postStatus = null;
        try
        {
            postStatus = await CaptureStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Post-setup status refresh failed.", exception, ("Result", resultKind));
        }
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new(resultKind, postStatus);
    }

    private static FrontendPrerequisiteSetupResultKind MapResultKind(ElevatedPrerequisiteSetup.ResultKind kind) => kind switch
    {
        ElevatedPrerequisiteSetup.ResultKind.Ready => FrontendPrerequisiteSetupResultKind.Ready,
        ElevatedPrerequisiteSetup.ResultKind.Installed => FrontendPrerequisiteSetupResultKind.Installed,
        ElevatedPrerequisiteSetup.ResultKind.RebootRequired => FrontendPrerequisiteSetupResultKind.RebootRequired,
        ElevatedPrerequisiteSetup.ResultKind.Cancelled => FrontendPrerequisiteSetupResultKind.Cancelled,
        ElevatedPrerequisiteSetup.ResultKind.Blocked => FrontendPrerequisiteSetupResultKind.Blocked,
        ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress => FrontendPrerequisiteSetupResultKind.AlreadyInProgress,
        _ => FrontendPrerequisiteSetupResultKind.Failed
    };

    internal void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        Feedback.VibrationTestSessionWriter? session;
        lock (_vibrationSessionGate) { session = _vibrationSession; _vibrationSession = null; }
        if (session is null) return;
        var stop = _runtime?.CancelDeveloperVibrationTest();
        session.Write($"SessionClosed Reason=RuntimeShutdown CancelledPendingDeveloperStop=True BestEffortStopRequested=True PhysicalStatus={stop?.Status} PhysicalReason={stop?.Reason}");
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ThrowIfShuttingDown()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
            throw new FrontendProtocolException("Runtime is shutting down.");
    }

    public async Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new EnvironmentDiscoveryReportGenerator(new WindowsEnvironmentDiscoverySnapshotSource(), new EnvironmentDiscoveryReportStore(AppLog.DirectoryPath), new EnvironmentDiscoveryReportWriter()).GenerateAsync().ConfigureAwait(false);
            return new(true, null);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            return new(false, exception.Message);
        }
    }

    private FrontendSettingsSnapshot MapSettings() => new(_settings.Settings.LaunchAtWindowsStartup, _settings.Settings.LogLevel switch { AppLogPreference.Debug => FrontendLogLevel.Debug, AppLogPreference.Info => FrontendLogLevel.Info, _ => FrontendLogLevel.Off }, _settings.SteamInputRoutingEnabled, _settings.SuppressDeveloperMenuWarning, _settings.Oem1Mapping);

}
