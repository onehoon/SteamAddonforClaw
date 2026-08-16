using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Frontend;

internal sealed class InProcessAddonFrontendControl : IAddonFrontendControl
{
    private readonly StartupSettingsCoordinator _settings;
    private readonly ISystemStatusProvider _status;
    private readonly AddonRuntimeHost _runtime;
    private readonly DeveloperTestModeState _developer;
    private readonly string _registrationMessage;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
    private readonly IElevatedProcessRunner _setupRunner = new ElevatedProcessRunner();

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
        var setup = EvaluateFirstTimeSetup(snapshot);
        return FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(snapshot, _runtime.CaptureRoutingStatus()), setup);
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

    public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken cancellationToken = default)
    {
        var current = await _status.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var setup = EvaluateFirstTimeSetup(current);
        var mapped = FrontendSnapshotMapper.ApplySetup(FrontendSnapshotMapper.Map(current, _runtime.CaptureRoutingStatus()), setup);
        if (!PrerequisiteSetupPromptPolicy.IsInstallable(setup))
            return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
        var result = await PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(setup, _setupRunner, executable, ElevatedPrerequisiteSetup.Argument, cancellationToken).ConfigureAwait(false);
        // RunIfInstallableAsync returns null only when its safety policy declines to launch.
        // Preserve that distinction from an elevated helper that actually returns Blocked.
        if (result is null) return new(FrontendPrerequisiteSetupResultKind.NotInstallable, mapped);
        var resultKind = MapResultKind(ElevatedPrerequisiteSetup.TranslateExitCode(result));
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return new(resultKind, await CaptureStatusAsync(cancellationToken).ConfigureAwait(false));
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

    private FirstTimeSetupAssessment EvaluateFirstTimeSetup(SystemStatusSnapshot snapshot)
    {
        var receipt = _hidHideReceiptStore.Load();
        var usbReceipt = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load();
        var hidPackage = new WindowsHidHidePackageProbe().Inspect();
        var usbPackage = new WindowsUsbIpWin2PackageProbe().Inspect();
        var storage = ProvisioningStorageSecurity.Inspect(VelopackAppPaths.ProvisioningStateDirectory);
        var hidState = receipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate ? ComponentProvisioningState.Corrupt : receipt.Receipt is not null ? ToComponentProvisioningState(receipt.Receipt.State) : File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath) ? ComponentProvisioningState.Legacy : ComponentProvisioningState.None;
        var usbState = usbReceipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate ? ComponentProvisioningState.Corrupt : usbReceipt.Receipt is not null ? ToComponentProvisioningState(usbReceipt.Receipt.State) : ComponentProvisioningState.None;
        var hidBootChanged = receipt.Receipt is { State: HidHideProvisioningReceiptState.InstalledPendingReboot } hp && BootSession.HasChangedSince(hp.StartedAtUtc);
        var usbBootChanged = usbReceipt.Receipt is { State: UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot } up && BootSession.HasChangedSince(up.StartedAtUtc);
        var hidInstall = ComponentInstallationAssessmentPolicy.AssessHidHide(hidPackage, snapshot.Prerequisites.HidHide, HidHidePackageMetadata.BundledVersion.ToString());
        var usbInstall = ComponentInstallationAssessmentPolicy.AssessUsbIp(usbPackage, snapshot.Prerequisites.UsbIpWin2, UsbIpWin2PackageMetadata.BundledVersion.ToString());
        return FirstTimeSetupPolicy.Evaluate(new FirstTimeSetupInput(snapshot.HardwareCompatibility, snapshot.Compatibility, snapshot.RecoverySafe, snapshot.AddonOwnedOutputIdentityUncertain, new SteamSessionState(snapshot.Steam.IsActive, snapshot.Steam.RunningAppId, snapshot.Steam.Source), snapshot.Prerequisites.HidHide, snapshot.Prerequisites.UsbIpWin2, hidInstall, usbInstall, new(hidState, usbState, hidBootChanged, usbBootChanged)));
    }

    private static ComponentProvisioningState ToComponentProvisioningState(HidHideProvisioningReceiptState state) => state switch { HidHideProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, HidHideProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, HidHideProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, HidHideProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, HidHideProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
    private static ComponentProvisioningState ToComponentProvisioningState(UsbIpWin2ProvisioningReceiptState state) => state switch { UsbIpWin2ProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned, UsbIpWin2ProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted, UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot, UsbIpWin2ProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed, UsbIpWin2ProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled, _ => ComponentProvisioningState.Indeterminate };
}
