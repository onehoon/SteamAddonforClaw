namespace SteamInputAddonforClaw.Devices.Abstractions;

public interface IHandheldDeviceAdapter
{
    HandheldDeviceDescriptor Descriptor { get; }
    AuxiliaryControlCatalog AuxiliaryControls { get; }
    IInternalControllerMatcher InternalControllerMatcher { get; }
    INativeControllerStateManager? NativeState { get; }
    IHandheldDeviceModelResolver? ModelResolver { get; }
    DeviceProbeResult Probe(DeviceProbeContext context);
}
