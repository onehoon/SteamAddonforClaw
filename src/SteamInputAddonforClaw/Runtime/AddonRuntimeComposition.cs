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
    ISystemStatusProvider StatusProvider);

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
        // Full1902 A2 section 11: true only when Center M startup roots are exactly Enabled/Automatic
        // (MSI / stock controller authority). It gates ONLY the stock PID1901 resume baseline. It is
        // NOT permission to run the legacy Steam-session physical routing owner -- that owner is never
        // production-composed (section 10).
        bool stockCenterMAuthority,
        WinGSuppressionGuard winGSuppressionGuard,
        Action<bool>? bigPictureStateChanged = null,
        Action? routingReconcileCompleted = null,
        Func<bool>? isLaunchAtWindowsStartupRequired = null)
    {
        ArgumentNullException.ThrowIfNull(winGSuppressionGuard);
        var settingsStore = new SettingsStore(AddonDataPaths.SettingsPath);
        var settings = settingsStore.Load();
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        // PR10 addendum section 16: a first, access-denied task creation from the normal Runtime
        // process falls back to one bounded elevated child that creates exactly this one task.
        var startupRegistration = WindowsTaskSchedulerStartupManager.WithElevatedRepair();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration, isLaunchAtWindowsStartupRequired);
        var steamRuntime = new SteamSessionRuntime();
        if (bigPictureStateChanged is not null) steamRuntime.BigPictureStateChanged += bigPictureStateChanged;
        var startupRegistrationResult = startupSettings.Repair();

        // Full1902 A2 section 10/12: the legacy Steam-session physical routing owner is never composed,
        // so the routing session watcher is never started. Only the actual-AppID fact used by
        // Device/Profile is observed; raw Steam/BPM facts for the Full1902 X360<->SteamDeck
        // presentation come from SteamSessionRuntime's own always-on BPM watcher + CapturePresentationSnapshot.
        steamRuntime.StartActualObservation();

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
        // Full1902 A2 section 10: no production branch creates AddonRoutingRuntime. Center M Enabled
        // has no Addon controller ownership at all; Center M Disabled uses the Full1902 VIIPER
        // presentation owner (PR6/PR7) + the feature-local front-button runtime, both composed in
        // AddonProcessHost -- not this legacy path.
        AddonRoutingRuntime? routingRuntime = null;
        AppLog.Info("Routing", "Legacy Steam-session routing runtime is not composed (Full1902 controller authority).", ("Action", "Passive"));

        // Full1902 A2 section 11: sleep/resume while Center M is Disabled must not call the legacy
        // stock XInput baseline; the Enabled (stock authority) state still needs stock PID1901
        // verification on resume. Gated independently of the (now removed) legacy routing selection.
        Func<CancellationToken, Task<bool>> establishBaseline = (!stockCenterMAuthority || stockCenterMBaseline is null)
            ? _ => Task.FromResult(!stockCenterMAuthority)
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
            runtimeHost, startupSettings, startupRegistrationResult.Message, statusProvider);
    }
}
