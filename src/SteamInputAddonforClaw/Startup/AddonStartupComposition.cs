using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Startup;

internal sealed record AddonStartupComposition(
    StartupCoordinator Coordinator,
    AddonOwnedVirtualDeviceTracker AddonOwnedVirtualDeviceTracker,
    HandheldDeviceRegistry DeviceRegistry,
    IHandheldDeviceAdapter HandheldDeviceAdapter,
    IControllerEnvironmentAssessmentProvider ControllerEnvironmentAssessmentProvider,
    RecoveryManager RuntimeRecoveryManager,
    IStockCenterMStartupBaseline? StockCenterMBaseline);

internal static class AddonStartupCompositionFactory
{
    internal static AddonStartupComposition Create(string[]? updateRestartArguments)
    {
        var deviceEnumerator = new WindowsControllerDeviceEnumerator();
        var msiClawAdapter = new MsiClawDeviceAdapter(deviceEnumerator);
        var addonOwnedVirtualDeviceTracker = new AddonOwnedVirtualDeviceTracker();
        var classifier = new ControllerDeviceClassifier(msiClawAdapter.InternalControllerMatcher, addonOwnedVirtualDeviceTracker);
        var deviceRegistry = new HandheldDeviceRegistry([msiClawAdapter]);
        var controllerSoftwareProviders = new IControllerSoftwareStatusProvider[]
        {
            // HHC/ClawTweaks coexistence detection is intentionally not part of the supported production controller environment.
            new MsiCenterMSoftwareStatusProvider()
        };
        var controllerEnvironmentAssessmentProvider = new ControllerEnvironmentAssessmentProvider(controllerSoftwareProviders);
        var recoveryJournalStore = new RecoveryJournalStore(AddonDataPaths.RecoveryJournalPath);
        var runtimeRecoveryManager = new RecoveryManager(recoveryJournalStore);
        var nativeState = msiClawAdapter.NativeState as MsiClawNativeStateManager;
        var stockCenterMBaseline = nativeState is null ? null : new StockCenterMStartupBaseline(nativeState);
        var coordinator = new StartupCoordinator(
            new SilentUpdateGate(updateRestartArguments),
            controllerEnvironmentAssessmentProvider,
            new ControllerEnvironmentWaiter(deviceEnumerator, classifier),
            recoveryJournalStore: recoveryJournalStore,
            stockCenterMBaseline: stockCenterMBaseline,
            hidHideRecoveryCleaner: new StartupHidHideRecoveryCleaner(new HidHideDriverClient()),
            virtualOutputRecoveryInspector: new StartupVirtualOutputRecoveryInspector(deviceEnumerator),
            probeContextFactory: new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), deviceEnumerator),
            hardwareCompatibilityEvaluator: new HardwareCompatibilityEvaluator(deviceRegistry));

        return new AddonStartupComposition(
            coordinator,
            addonOwnedVirtualDeviceTracker,
            deviceRegistry,
            msiClawAdapter,
            controllerEnvironmentAssessmentProvider,
            runtimeRecoveryManager,
            stockCenterMBaseline);
    }
}
