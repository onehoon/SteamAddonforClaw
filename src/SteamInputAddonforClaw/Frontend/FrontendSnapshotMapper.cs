using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Frontend;

internal static class FrontendSnapshotMapper
{
    internal static FrontendStatusSnapshot Map(SystemStatusSnapshot snapshot, RoutingRuntimeStatusSnapshot routing) => new(
        new(snapshot.Device.Manufacturer, snapshot.Device.Model, snapshot.Device.BaseBoardProduct, snapshot.Device.GpuModels),
        new(snapshot.HardwareCompatibility.Status.ToString(), snapshot.HardwareCompatibility.DeviceFamily?.Value ?? "", snapshot.HardwareCompatibility.DeviceModel?.Value ?? "", snapshot.HardwareCompatibility.Reason),
        snapshot.ControllerSoftware.Select(x => new FrontendSoftwareSnapshot(x.Kind.ToString(), x.DisplayName, x.Installation.ToString(), x.Runtime.ToString(), x.Reason)).ToArray(),
        snapshot.Compatibility.Status.ToString(), snapshot.Compatibility.Reason.ToString(),
        new(snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason, snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason, snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason),
        new(snapshot.Steam.IsActive, snapshot.Steam.RunningAppId, snapshot.Steam.Source.ToString()),
        new(snapshot.RoutingDecision.Reason.ToString(), routing.OperationalState.ToString(), routing.SteamOutputActive, routing.NativeDirectInputActive),
        snapshot.Addon.Status.ToString(), snapshot.Addon.Reason, snapshot.RecoverySafe, snapshot.AddonOwnedOutputIdentityUncertain,
        FrontendSetupStatus.Indeterminate, "Status must be refreshed before setup evaluation.", false);
}
