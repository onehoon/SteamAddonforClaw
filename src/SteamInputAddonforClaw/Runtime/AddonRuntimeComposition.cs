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
        // True only when Center M startup roots are exactly Enabled/Automatic (MSI/stock authority).
        // It gates the stock PID1901 resume baseline. It does NOT gate any Addon controller
        // ownership: per REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md 3.1, Center M Enabled
        // means "Addon DirectInput / HidHide / VIIPER ownership = none" and "Steam/BPM must not
        // override this authority decision", so the legacy Steam-session physical routing owner is
        // never created here.
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

        // Full1902: the legacy Steam-session physical routing owner is never composed. Center M
        // Enabled has no Addon controller ownership at all; Center M Disabled uses the VIIPER
        // presentation owner (PR6/PR7), not this path. Only the actual-AppID fact used by
        // Device/Profile is observed.
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
        // Full1902 review [BLOCKER]: removing the Steam Input Routing preference must not let Steam/BPM
        // drive the legacy physical routing owner. That owner was only ever composed in the Center M
        // Enabled (stock authority) state, where the design forbids any Addon controller ownership, so
        // it is no longer created. The stock PID1901 resume baseline below is kept independently.
        AddonRoutingRuntime? routingRuntime = null;
        AppLog.Info("Routing", "Legacy Steam-session routing runtime is not composed (Full1902 controller authority).", ("Action", "Passive"));

        // Sleep/resume while Center M is Disabled must not call the legacy stock XInput baseline; the
        // Enabled (stock authority) state still needs stock PID1901 verification on resume.
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
            runtimeHost, startupSettings, startupRegistrationResult.Message, statusProvider,
            routingRuntime?.Oem1ActivationTask ?? Task.CompletedTask);
    }
}
