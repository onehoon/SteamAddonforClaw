using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Runtime;

internal sealed record AddonRuntimeComposition(
    AddonRuntimeHost RuntimeHost,
    StartupSettingsCoordinator StartupSettings,
    ISystemStatusProvider StatusProvider);

internal static class AddonRuntimeCompositionFactory
{
    internal static AddonRuntimeComposition Create(
        HandheldDeviceRegistry deviceRegistry,
        IStockCenterMStartupBaseline? stockCenterMBaseline,
        bool recoverySafe,
        // Full1902 A2 section 11: true only when Center M startup roots are exactly Enabled/Automatic
        // (MSI / stock controller authority). It gates ONLY the stock PID1901 resume baseline.
        bool stockCenterMAuthority,
        Action<bool>? bigPictureStateChanged = null,
        // Full1902 0903 cleanup (section 4.6): a read-only override for the final Addon operational
        // status, closing over AddonProcessHost's existing physical/presentation ownership facts.
        Func<AddonStatusSnapshot?>? captureFull1902AddonStatus = null,
        // Full1902 Suspend/Resume section 5.2 / addendum A.3: the one host-local Full1902 suspend
        // participant, created by AddonProcessHost and passed through unchanged. Its quiesce callback
        // reads AddonProcessHost's current presentation ownership with a null guard at execution time.
        IPowerSuspendParticipant? full1902SuspendParticipant = null,
        bool uninstallPreparationOnly = false,
        HandheldDeviceModelId? hardwareDeviceModel = null)
    {
        var settingsStore = new SettingsStore(AddonDataPaths.SettingsPath);
        var settings = settingsStore.Load(SelectFrontButtonMappingDefault(hardwareDeviceModel));
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        // The Runtime is already High before composition; startup-task repair writes directly and
        // verifies the exact owned-task contract.
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var steamRuntime = new SteamSessionRuntime();
        if (bigPictureStateChanged is not null) steamRuntime.BigPictureStateChanged += bigPictureStateChanged;
        // Installed-app lifecycle infrastructure: prove the owned startup task exists at Runtime
        // startup. A failed repair is logged but never exits an already-running Runtime.
        if (!uninstallPreparationOnly)
        {
            var startupRegistrationResult = startupSettings.EnsureStartupRegistration();
            AppLog.Info("Startup", "Startup registration ensured.",
                ("Success", startupRegistrationResult.Success), ("Message", startupRegistrationResult.Message));
        }

        // Full1902 A2 section 10/12: the legacy Steam-session physical routing owner is never composed,
        // so the routing session watcher is never started. Only the actual-AppID fact used by
        // Device/Profile is observed; raw Steam/BPM facts for the Full1902 X360<->SteamDeck
        // presentation come from SteamSessionRuntime's own always-on BPM watcher + CapturePresentationSnapshot.
        if (!uninstallPreparationOnly)
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
            establishBaseline,
            suspendParticipant: full1902SuspendParticipant);

        if (bigPictureStateChanged is not null && steamRuntime.IsBigPictureActive)
            bigPictureStateChanged(true);

        return new AddonRuntimeComposition(
            runtimeHost, startupSettings, statusProvider);
    }

    internal static FrontButtonMappingSettings SelectFrontButtonMappingDefault(HandheldDeviceModelId? hardwareDeviceModel)
        => hardwareDeviceModel is { } model
            && (model == MsiClawDeviceModels.Claw7AiPlusA2vm.Id
                || model == MsiClawDeviceModels.Claw8AiPlusA2vm.Id)
                ? FrontButtonMappingSettings.A2vmDefault
                : FrontButtonMappingSettings.Default;
}
