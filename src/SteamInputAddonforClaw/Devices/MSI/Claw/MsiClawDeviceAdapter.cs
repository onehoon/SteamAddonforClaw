using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public sealed class MsiClawDeviceAdapter : IHandheldDeviceAdapter
{
    public MsiClawDeviceAdapter(IControllerDeviceEnumerator? deviceEnumerator = null)
    {
        NativeState = new MsiClawNativeStateManager(deviceEnumerator ?? new WindowsControllerDeviceEnumerator());
    }
    public HandheldDeviceDescriptor Descriptor { get; } = new(new HandheldDeviceId("msi.claw"), "MSI", "Claw", "MSI Claw");
    public AuxiliaryControlCatalog AuxiliaryControls => MsiClawControls.Catalog;
    public IInternalControllerMatcher InternalControllerMatcher { get; } = new MsiClawInternalControllerMatcher();
    public INativeControllerStateManager? NativeState { get; }

    public DeviceProbeResult Probe(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.PresentPnpDevices.Any(device => MsiClawHardware.IsKnownController(device.VendorId, device.ProductId))
            ? new(DeviceProbeStatus.Match, "KnownMsiClawVidPidPresent")
            : new(DeviceProbeStatus.NoMatch, "KnownMsiClawVidPidAbsent");
    }
}
