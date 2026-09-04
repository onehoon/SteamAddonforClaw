using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
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
    ISystemStatusProvider StatusProvider);

internal static class AddonRuntimeCompositionFactory
{
    internal static AddonRuntimeComposition Create(
        HandheldDeviceRegistry deviceRegistry,
        RecoveryManager recoveryManager,
        IStockCenterMStartupBaseline? stockCenterMBaseline,
        bool recoverySafe,
        // Full1902 A2 section 11: true only when Center M startup roots are exactly Enabled/Automatic
        // (MSI / stock controller authority). It gates ONLY the stock PID1901 resume baseline.
        bool stockCenterMAuthority,
        Action<bool>? bigPictureStateChanged = null,
        Func<bool>? isLaunchAtWindowsStartupRequired = null,
        // Full1902 0903 cleanup (section 4.6): a read-only override for the final Addon operational
        // status, closing over AddonProcessHost's existing physical/presentation ownership facts.
        Func<AddonStatusSnapshot?>? captureFull1902AddonStatus = null)
    {
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
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()),
                new ViiperRuntimeInspector()),
            // Full1902 Cleanup A: raw Steam/BPM presentation facts for the Steam status card --
            // the legacy effective-routing-session state is no longer consulted.
            () => steamRuntime.CapturePresentationSnapshot(),
            () => recoverySafetyState.Current == RecoverySafety.Safe,
            captureFull1902AddonStatus);
        // Full1902 A2 section 11: sleep/resume while Center M is Disabled must not call the legacy
        // stock XInput baseline; the Enabled (stock authority) state still needs stock PID1901
        // verification on resume. Gated independently of the (now removed) legacy routing selection.
        Func<CancellationToken, Task<bool>> establishBaseline = (!stockCenterMAuthority || stockCenterMBaseline is null)
            ? _ => Task.FromResult(!stockCenterMAuthority)
            : async token => (await stockCenterMBaseline.EstablishAsync(token).ConfigureAwait(false)).Succeeded;

        var runtimeHost = new AddonRuntimeHost(
            steamRuntime,
            powerGate,
            recoverySafetyState,
            recoverySafe,
            () => recoveryManager.HasIncompleteRecovery,
            establishBaseline);

        if (bigPictureStateChanged is not null && steamRuntime.IsBigPictureActive)
            bigPictureStateChanged(true);

        return new AddonRuntimeComposition(
            runtimeHost, startupSettings, startupRegistrationResult.Message, statusProvider);
    }
}
