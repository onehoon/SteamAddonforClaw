using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Status;

internal sealed class SystemStatusProvider(
    IDeviceInformationProvider deviceInformationProvider,
    IReadOnlyList<IControllerSoftwareStatusProvider> softwareProviders,
    IRuntimePrerequisiteInspector prerequisiteInspector,
    Func<SteamSessionState> steamStateProvider,
    Func<ExternalControllerAssessment> externalControllerProvider,
    Func<bool> recoverySafeProvider,
    IControllerEnvironmentCompatibilityPolicy? compatibilityPolicy = null,
    IRoutingSessionStateMachine? routingSessionStateMachine = null) : ISystemStatusProvider
{
    private readonly IRoutingSessionStateMachine _routingSessionStateMachine = routingSessionStateMachine ?? new RoutingSessionStateMachine();
    private readonly IControllerEnvironmentCompatibilityPolicy _compatibilityPolicy = compatibilityPolicy ?? new CurrentControllerEnvironmentCompatibilityPolicy();
    public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CaptureCore(cancellationToken), cancellationToken);

    private SystemStatusSnapshot CaptureCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = deviceInformationProvider.Capture();
        var software = ControllerSoftwareStatusSorter.Sort(softwareProviders.Select(provider => provider.Capture()));
        var compatibility = _compatibilityPolicy.Evaluate(software);
        var prerequisites = prerequisiteInspector.Inspect();
        var steam = TrySteamState();
        var external = TryExternalControllerAssessment();
        var recoverySafe = TryRecoverySafety();
        var decision = _routingSessionStateMachine.Evaluate(new RoutingPolicyInput(steam, external, compatibility, prerequisites, recoverySafe));
        var addon = AddonStatusEvaluator.Map(decision, external, compatibility);
        AppLog.Info("Status", "System status snapshot refreshed.", ("HidHide", prerequisites.HidHide.Status), ("UsbIpWin2", prerequisites.UsbIpWin2.Status), ("Viiper", prerequisites.Viiper.Status), ("AddonStatus", addon.Status));
        return new SystemStatusSnapshot(device, software, compatibility, prerequisites, new SteamStatusSnapshot(steam.IsActive, steam.RunningAppId), external, decision, addon);
    }

    private SteamSessionState TrySteamState() { try { return steamStateProvider(); } catch { return SteamSessionState.FromRunningAppId(0); } }
    private ExternalControllerAssessment TryExternalControllerAssessment() { try { return externalControllerProvider(); } catch { return new(ExternalControllerAssessmentStatus.Indeterminate, 0, []); } }
    private bool TryRecoverySafety() { try { return recoverySafeProvider(); } catch { return false; } }
}
