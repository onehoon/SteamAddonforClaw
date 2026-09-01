using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.GameBar;

namespace SteamInputAddonforClaw.Runtime;

internal sealed record AddonRuntimeComposition(
    AddonRuntimeHost RuntimeHost,
    StartupSettingsCoordinator StartupSettings,
    string StartupRegistrationMessage,
    ISystemStatusProvider StatusProvider,
    /// <summary>The owned initial OEM1 action-path activation task. Frontend/tray startup does not
    /// await it; Routing awaits the same task before entering its pipeline/helper-acquisition
    /// boundary. <see cref="Task.CompletedTask"/> when there is no routing runtime.</summary>
    Task Oem1ActivationTask);

internal static class AddonRuntimeCompositionFactory
{
    internal static AddonRuntimeComposition Create(
        IHandheldDeviceAdapter handheldDeviceAdapter,
        HandheldDeviceRegistry deviceRegistry,
        IControllerEnvironmentAssessmentProvider controllerEnvironmentAssessmentProvider,
        RecoveryManager recoveryManager,
        IStockCenterMStartupBaseline? stockCenterMBaseline,
        bool recoverySafe,
        bool hardwareSupported,
        WinGSuppressionGuard winGSuppressionGuard,
        Action<bool>? bigPictureStateChanged = null,
        Action? routingReconcileCompleted = null,
        Func<bool>? isLaunchAtWindowsStartupRequired = null)
    {
        ArgumentNullException.ThrowIfNull(winGSuppressionGuard);
        var settingsStore = new SettingsStore(AddonDataPaths.SettingsPath);
        var settings = settingsStore.Load();
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration, isLaunchAtWindowsStartupRequired);
        var steamRuntime = new SteamSessionRuntime(startupSettings);
        if (bigPictureStateChanged is not null) steamRuntime.BigPictureStateChanged += bigPictureStateChanged;
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            steamRuntime.StartRoutingObservation();
        }
        else
        {
            steamRuntime.StartActualObservation();
            AppLog.Warn("Recovery", "Steam/controller routing remains stopped because recovery is unsafe.", null, ("Action", "Passive"));
        }

        var recoverySafetyState = new RecoverySafetyState(recoverySafe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
        var powerGate = new PowerMutationGate();
        ISystemStatusProvider statusProvider = new SystemStatusProvider(
            new WindowsDeviceInformationProvider(),
            new WindowsDeviceProbeContextFactory(),
            new HardwareCompatibilityEvaluator(deviceRegistry),
            controllerEnvironmentAssessmentProvider,
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()),
                new ViiperRuntimeInspector()),
            () => steamRuntime.State,
            () => recoverySafetyState.Current == RecoverySafety.Safe);
        var routingRuntime = AddonRoutingRuntime.Create(
            handheldDeviceAdapter,
            statusProvider,
            recoveryManager,
            powerGate,
            recoverySafetyState,
            startupSettings,
            hardwareSupported,
            winGSuppressionGuard,
            wingMappingPreference: startupSettings);

        Func<CancellationToken, Task<bool>> establishBaseline = stockCenterMBaseline is null
            ? _ => Task.FromResult(false)
            : async token => (await stockCenterMBaseline.EstablishAsync(token).ConfigureAwait(false)).Succeeded;

        var runtimeHost = new AddonRuntimeHost(
            steamRuntime,
            routingRuntime,
            powerGate,
            recoverySafetyState,
            recoverySafe,
            () => recoveryManager.HasIncompleteRecovery,
            establishBaseline,
            routingReconcileCompleted: routingReconcileCompleted);

        if (bigPictureStateChanged is not null && steamRuntime.IsBigPictureActive)
            bigPictureStateChanged(true);

        return new AddonRuntimeComposition(
            runtimeHost, startupSettings, startupRegistrationResult.Message, statusProvider,
            routingRuntime?.Oem1ActivationTask ?? Task.CompletedTask);
    }
}
