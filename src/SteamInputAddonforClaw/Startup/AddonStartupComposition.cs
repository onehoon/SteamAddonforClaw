using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Startup;

internal sealed record AddonStartupComposition(
    StartupCoordinator Coordinator,
    HandheldDeviceRegistry DeviceRegistry,
    IHandheldDeviceAdapter HandheldDeviceAdapter,
    IControllerEnvironmentAssessmentProvider ControllerEnvironmentAssessmentProvider,
    RecoveryManager RuntimeRecoveryManager,
    IStockCenterMStartupBaseline? StockCenterMBaseline,
    // PR4: the ONE shared Center M startup control -- startup reads it for the authority branch, and
    // AddonProcessHost reuses this same instance for the PR2.5 mandatory policy, the PR3 transition,
    // and Device-page capture.
    CenterMStartupControl CenterMStartupControl);

internal static class AddonStartupCompositionFactory
{
    internal static AddonStartupComposition Create(string[]? updateRestartArguments)
    {
        var deviceEnumerator = new WindowsControllerDeviceEnumerator();
        var msiClawAdapter = new MsiClawDeviceAdapter(deviceEnumerator);
        var classifier = new ControllerDeviceClassifier(msiClawAdapter.InternalControllerMatcher);
        var deviceRegistry = new HandheldDeviceRegistry([msiClawAdapter]);
        var controllerSoftwareProviders = new IControllerSoftwareStatusProvider[]
        {
            new MsiCenterMSoftwareStatusProvider(),
            // PR4: the Disabled-boot admission and the PR3 conflict gate both treat
            // Manager.Kind == None as "positively no known controller manager". Wire the real conflict
            // detectors so that holds -- the absence of a provider must never read as the absence of
            // the manager. (Winhanced has no production detector yet and is deliberately not faked.)
            new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
            new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector()),
        };
        var controllerEnvironmentAssessmentProvider = new ControllerEnvironmentAssessmentProvider(controllerSoftwareProviders);
        var recoveryJournalStore = new RecoveryJournalStore(AddonDataPaths.RecoveryJournalPath);
        var runtimeRecoveryManager = new RecoveryManager(recoveryJournalStore);
        var nativeState = msiClawAdapter.NativeState as MsiClawNativeStateManager;
        var stockCenterMBaseline = nativeState is null ? null : new StockCenterMStartupBaseline(nativeState);

        // PR4: the single shared Center M startup control (reader/writer). Always constructed
        // available -- startup only reads it after supported hardware is proven, and the process
        // only reaches Runtime initialization on supported hardware.
        var centerMStartupControl = new CenterMStartupControl(available: true);

        // PR4: read-only Disabled-boot admission. Reuses existing facts only -- no new scanner.
        var prerequisiteInspector = new RuntimePrerequisiteInspector(
            new HidHidePrerequisiteInspector(new HidHideDriverClient()),
            new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()),
            new ViiperRuntimeInspector());
        var addonExecutablePath = Environment.ProcessPath;
        var addonHidHideBaseline = addonExecutablePath is null
            ? null
            : new AddonControllerHidHideBaseline(new HidHideDriverClient(), addonExecutablePath);
        IDisabledBootControllerAdmission? disabledBootAdmission = addonHidHideBaseline is null
            ? null
            : new DisabledBootControllerAdmission(
                controllerEnvironmentAssessmentProvider,
                prerequisiteInspector,
                runtimeRecoveryManager.LoadJournal,
                () => addonHidHideBaseline.InspectDisabledModeBaseline([]));

        var coordinator = new StartupCoordinator(
            new SilentUpdateGate(updateRestartArguments),
            controllerEnvironmentAssessmentProvider,
            new ControllerEnvironmentWaiter(deviceEnumerator, classifier),
            recoveryJournalStore: recoveryJournalStore,
            stockCenterMBaseline: stockCenterMBaseline,
            hidHideRecoveryCleaner: new StartupHidHideRecoveryCleaner(new HidHideDriverClient()),
            disabledBootAdmission: disabledBootAdmission,
            captureCenterMStartup: centerMStartupControl.Capture,
            probeContextFactory: new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), deviceEnumerator),
            hardwareCompatibilityEvaluator: new HardwareCompatibilityEvaluator(deviceRegistry));

        return new AddonStartupComposition(
            coordinator,
            deviceRegistry,
            msiClawAdapter,
            controllerEnvironmentAssessmentProvider,
            runtimeRecoveryManager,
            stockCenterMBaseline,
            centerMStartupControl);
    }
}
