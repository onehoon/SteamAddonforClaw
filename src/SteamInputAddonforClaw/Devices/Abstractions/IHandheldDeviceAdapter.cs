namespace SteamInputAddonforClaw.Devices.Abstractions;

public interface IHandheldDeviceAdapter
{
    HandheldDeviceDescriptor Descriptor { get; }
    AuxiliaryControlCatalog AuxiliaryControls { get; }
    DeviceProbeResult Probe(DeviceProbeContext context);
}
