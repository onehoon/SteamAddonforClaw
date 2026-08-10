using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public sealed class MsiClawDeviceAdapter : IHandheldDeviceAdapter
{
    public MsiClawDeviceAdapter(IControllerDeviceEnumerator? deviceEnumerator = null)
    {
        var devices = deviceEnumerator ?? new WindowsControllerDeviceEnumerator();
        var modeController = new MsiClawModeController(devices, new MsiClawControlHidResolver(), new WindowsMsiClawModeWriter());
        NativeState = new MsiClawNativeStateManager(devices, modeController);
    }
    public HandheldDeviceDescriptor Descriptor { get; } = new(new HandheldDeviceId("msi.claw"), "MSI", "Claw", "MSI Claw");
    public AuxiliaryControlCatalog AuxiliaryControls => MsiClawControls.Catalog;
    public IInternalControllerMatcher InternalControllerMatcher { get; } = new MsiClawInternalControllerMatcher();
    public INativeControllerStateManager? NativeState { get; }
    public IHandheldDeviceModelResolver? ModelResolver { get; } = new MsiClawDeviceModelResolver();

    public DeviceProbeResult Probe(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.PresentPnpDevices.Any(device => MsiClawHardware.IsKnownController(device.VendorId, device.ProductId))
            ? new(DeviceProbeStatus.Match, "KnownMsiClawVidPidPresent")
            : new(DeviceProbeStatus.NoMatch, "KnownMsiClawVidPidAbsent");
    }
}
