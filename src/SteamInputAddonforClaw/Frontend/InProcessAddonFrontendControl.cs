using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Frontend;

internal sealed class InProcessAddonFrontendControl : IAddonFrontendControl
{
    private readonly StartupSettingsCoordinator _settings;
    private readonly ISystemStatusProvider _status;
    private readonly AddonRuntimeHost _runtime;
    private readonly DeveloperTestModeState _developer;
    private readonly string _registrationMessage;

    internal InProcessAddonFrontendControl(StartupSettingsCoordinator settings, ISystemStatusProvider status, AddonRuntimeHost runtime, DeveloperTestModeState developer, string registrationMessage)
    {
        _settings = settings;
        _status = status;
        _runtime = runtime;
        _developer = developer;
        _registrationMessage = registrationMessage;
        _runtime.SteamSessionStateChanged += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
        _runtime.StatusRefreshRequested += (_, _) => StateInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StateInvalidated;

    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FrontendBootstrapSnapshot(MapSettings(), _registrationMessage, new(_developer.IsEnabled), AppLog.DirectoryPath));

    public async Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return FrontendSnapshotMapper.Map(snapshot, _runtime.CaptureRoutingStatus());
    }

    public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var result = _settings.ChangeLaunchAtWindowsStartup(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendLaunchAtStartupResult(MapSettings(), result.Message));
    }

    public Task<FrontendSettingsSnapshot> SetRouteInSteamBigPictureAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _settings.ChangeRouteInSteamBigPicture(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken cancellationToken = default)
    {
        _settings.ChangeLogLevel(level switch { FrontendLogLevel.Debug => AppLogPreference.Debug, FrontendLogLevel.Info => AppLogPreference.Info, _ => AppLogPreference.Off });
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken cancellationToken = default)
    {
        _settings.SuppressDeveloperMenuWarningPermanently();
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(MapSettings());
    }

    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _developer.SetEnabled(enabled);
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendDeveloperSnapshot(_developer.IsEnabled));
    }

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default) =>
        new("NotImplementedInBoundary", await CaptureStatusAsync(cancellationToken).ConfigureAwait(false));

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

    private FrontendSettingsSnapshot MapSettings() => new(_settings.Settings.LaunchAtWindowsStartup, _settings.Settings.LogLevel switch { AppLogPreference.Debug => FrontendLogLevel.Debug, AppLogPreference.Info => FrontendLogLevel.Info, _ => FrontendLogLevel.Off }, _settings.RouteInSteamBigPicture, _settings.SuppressDeveloperMenuWarning);
}
