using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Status;

internal sealed class SystemStatusProvider(
    IDeviceInformationProvider deviceInformationProvider,
    IWindowsDeviceProbeContextFactory deviceProbeContextFactory,
    IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
    IControllerEnvironmentAssessmentProvider environmentAssessmentProvider,
    IRuntimePrerequisiteInspector prerequisiteInspector,
    Func<SteamSessionState> steamStateProvider,
    Func<ExternalControllerAssessment> externalControllerProvider,
    Func<bool> recoverySafeProvider,
    IRoutingSessionStateMachine? routingSessionStateMachine = null) : ISystemStatusProvider
{
    private readonly IRoutingSessionStateMachine _routingSessionStateMachine = routingSessionStateMachine ?? new RoutingSessionStateMachine();

    internal SystemStatusProvider(
        IDeviceInformationProvider deviceInformationProvider,
        IWindowsDeviceProbeContextFactory deviceProbeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        IReadOnlyList<IControllerSoftwareStatusProvider> softwareProviders,
        IRuntimePrerequisiteInspector prerequisiteInspector,
        Func<SteamSessionState> steamStateProvider,
        Func<ExternalControllerAssessment> externalControllerProvider,
        Func<bool> recoverySafeProvider,
        IRoutingSessionStateMachine? routingSessionStateMachine = null)
        : this(deviceInformationProvider, deviceProbeContextFactory, hardwareCompatibilityEvaluator,
            new ControllerEnvironmentAssessmentProvider(softwareProviders), prerequisiteInspector, steamStateProvider,
            externalControllerProvider, recoverySafeProvider, routingSessionStateMachine)
    {
    }
    public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CaptureCore(cancellationToken), cancellationToken);

    private SystemStatusSnapshot CaptureCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deviceProbe = deviceProbeContextFactory.Capture();
        var device = deviceInformationProvider.Capture(deviceProbe.Context);
        var hardwareCompatibility = hardwareCompatibilityEvaluator.Evaluate(deviceProbe);
        var environment = environmentAssessmentProvider.Capture();
        var software = environment.Software;
        var compatibility = environment.Compatibility;
        var prerequisites = prerequisiteInspector.Inspect();
        var steam = TrySteamState();
        var external = TryExternalControllerAssessment();
        var recoverySafe = TryRecoverySafety();
        var decision = _routingSessionStateMachine.Evaluate(new RoutingPolicyInput(steam, external, hardwareCompatibility, compatibility, prerequisites, recoverySafe));
        var addon = AddonStatusEvaluator.Map(decision, external, compatibility);
        AppLog.Debug("Status", "System status snapshot refreshed.", ("HidHide", prerequisites.HidHide.Status), ("UsbIpWin2", prerequisites.UsbIpWin2.Status), ("Viiper", prerequisites.Viiper.Status), ("AddonStatus", addon.Status));
        return new SystemStatusSnapshot(device, hardwareCompatibility, software, compatibility, prerequisites, new SteamStatusSnapshot(steam.IsActive, steam.RunningAppId, steam.Source), external, decision, addon, recoverySafe);
    }

    private SteamSessionState TrySteamState() { try { return steamStateProvider(); } catch { return SteamSessionState.FromRunningAppId(0); } }
    private ExternalControllerAssessment TryExternalControllerAssessment() { try { return externalControllerProvider(); } catch { return new(ExternalControllerAssessmentStatus.Indeterminate, 0, []); } }
    private bool TryRecoverySafety() { try { return recoverySafeProvider(); } catch { return false; } }
}
