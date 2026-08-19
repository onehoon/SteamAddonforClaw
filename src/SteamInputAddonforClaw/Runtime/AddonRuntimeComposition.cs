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

namespace SteamInputAddonforClaw.Runtime;

internal sealed record AddonRuntimeComposition(
    AddonRuntimeHost RuntimeHost,
    StartupSettingsCoordinator StartupSettings,
    string StartupRegistrationMessage,
    ISystemStatusProvider StatusProvider,
    /// <summary>Review fix (BLOCKER): the routing composition's OEM1 action-path startup activation
    /// (see <see cref="AddonRoutingRuntime.Oem1ActivationTask"/>), forwarded so
    /// <see cref="Hosting.AddonProcessHost.InitializeRuntimeAsync"/> can await it before routing/power
    /// observation begins. <see cref="Task.CompletedTask"/> when there is no routing runtime.</summary>
    Task Oem1ActivationTask);

internal static class AddonRuntimeCompositionFactory
{
    internal static AddonRuntimeComposition Create(
        IHandheldDeviceAdapter handheldDeviceAdapter,
        HandheldDeviceRegistry deviceRegistry,
        IControllerEnvironmentAssessmentProvider controllerEnvironmentAssessmentProvider,
        AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker,
        RecoveryManager recoveryManager,
        IStockCenterMStartupBaseline? stockCenterMBaseline,
        bool recoverySafe)
    {
        var settingsStore = new SettingsStore(AddonDataPaths.SettingsPath);
        var settings = settingsStore.Load();
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var steamRuntime = new SteamSessionRuntime(startupSettings);
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            steamRuntime.StartRoutingObservation();
        }
        else
        {
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
            () => recoverySafetyState.Current == RecoverySafety.Safe,
            () => addonOwnedVirtualDeviceTracker.HasUncertainOwnership);
        var routingRuntime = AddonRoutingRuntime.Create(
            handheldDeviceAdapter,
            statusProvider,
            addonOwnedVirtualDeviceTracker,
            recoveryManager,
            powerGate,
            recoverySafetyState);

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
            establishBaseline);

        return new AddonRuntimeComposition(
            runtimeHost, startupSettings, startupRegistrationResult.Message, statusProvider,
            routingRuntime?.Oem1ActivationTask ?? Task.CompletedTask);
    }
}
