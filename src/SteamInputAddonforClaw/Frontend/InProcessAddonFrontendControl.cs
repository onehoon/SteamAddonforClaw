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
    private readonly Func<CancellationToken, Task>? _reconcileOem1Prerequisites;
    private int _shutdownStarted;

    /// <param name="oem1MappingAvailable">The startup hardware-support result
    /// (<see cref="Startup.StartupResult.HardwareSupported"/>), reported verbatim on bootstrap so the
    /// UI gates the Center M Button feature on the SAME fact the routing composition's OEM1 action
    /// path gates on. Defaults to false so any construction path that never established hardware
    /// support reports the feature unavailable rather than offering it.</param>
    internal InProcessAddonFrontendControl(StartupSettingsCoordinator settings, ISystemStatusProvider status, AddonRuntimeHost? runtime, DeveloperTestModeState developer, string registrationMessage, IFrontendPrerequisiteSetupExecutor? setupExecutor = null, Func<string?>? processPath = null, Func<RoutingRuntimeStatusSnapshot>? captureRoutingStatus = null, bool oem1MappingAvailable = false, Func<CancellationToken, Task>? reconcileOem1Prerequisites = null)
    {
        _oem1MappingAvailable = oem1MappingAvailable;
        _reconcileOem1Prerequisites = reconcileOem1Prerequisites ?? (runtime is null ? null : runtime.ReconcileOem1PrerequisitesAsync);
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
        var setup = _setupExecutor.Evaluate(snapshot, _settings?.Oem1Mapping?.RemappingEnabled ?? false);
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

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        var current = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = _setupExecutor.Evaluate(current, _settings?.Oem1Mapping?.RemappingEnabled ?? false);
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
        _settings?.RefreshCenterMAutoRunOwnershipFromDisk();
        var resultKind = MapResultKind(ElevatedPrerequisiteSetup.TranslateExitCode(result));
        if (_reconcileOem1Prerequisites is not null)
        {
            // OEM1 AutoRun is independent of HidHide/usbip. The elevated helper may have
            // confirmed AutoRun before a later unrelated prerequisite fails, so always let the
            // coordinator perform its own fresh fail-closed prerequisite evaluation.
            await _reconcileOem1Prerequisites(cancellationToken).ConfigureAwait(false);
        }
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

    internal void BeginProcessShutdown() => Interlocked.Exchange(ref _shutdownStarted, 1);

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
