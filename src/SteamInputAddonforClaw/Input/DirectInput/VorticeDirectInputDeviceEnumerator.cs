using Vortice.DirectInput;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Input.DirectInput;

public sealed class VorticeDirectInputDeviceEnumerator : IDirectInputDeviceEnumerator
{
    internal static DeviceType EnumeratedDeviceType => DeviceType.Gamepad;
    internal static DeviceEnumerationFlags EnumeratedDeviceFlags => DeviceEnumerationFlags.AllDevices;

    private readonly IntPtr _windowHandle;
    private readonly IDirectInput8 _directInput;
    private readonly DirectInputDeviceTopologyResolver _topologyResolver;

    public VorticeDirectInputDeviceEnumerator(IntPtr windowHandle)
        : this(windowHandle, new WindowsControllerDeviceEnumerator())
    {
    }

    internal VorticeDirectInputDeviceEnumerator(IntPtr windowHandle, IControllerDeviceEnumerator controllerDevices)
    {
        _windowHandle = windowHandle;
        _directInput = DInput.DirectInput8Create();
        _topologyResolver = new DirectInputDeviceTopologyResolver(controllerDevices);
    }

    public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers()
    {
        return _directInput.GetDevices(EnumeratedDeviceType, EnumeratedDeviceFlags)
            .Select(CreateDescriptor)
            .ToArray();
    }

    private DirectInputDeviceDescriptor CreateDescriptor(DeviceInstance device)
    {
        var vendorId = GetVendorId(device.ProductGuid);
        var productId = GetProductId(device.ProductGuid);
        // Product GUID is available from GetDevices without opening the device. Avoid interface,
        // capability, and PnP topology work for unrelated gamepads; only the exact MSI target is
        // eligible for the additional identity checks.
        if (vendorId != Devices.MSI.Claw.MsiClawHardware.VendorId || productId != Devices.MSI.Claw.MsiClawHardware.DirectInputProductId)
            return new DirectInputDeviceDescriptor(device.InstanceGuid, device.ProductGuid, device.ProductName,
                vendorId, productId, null, null, null, null, null, null, null, "SkippedNonMsiPid1902");

        string? devicePath = null;
        int? buttonCount = null;
        int? axisCount = null;
        try
        {
            using var inspectionDevice = _directInput.CreateDevice(device.InstanceGuid);
            devicePath = inspectionDevice.Properties.InterfacePath;
            var capabilities = inspectionDevice.Capabilities;
            buttonCount = capabilities.ButtonCount;
            axisCount = capabilities.AxeCount;
        }
        catch (Exception exception)
        {
            SteamInputAddonforClaw.Diagnostics.AppLog.Warn("DirectInput", "DirectInput device inspection failed.", exception, ("InstanceGuid", device.InstanceGuid), ("Action", "DoNotAcquire"));
        }

        var topology = _topologyResolver.Resolve(devicePath);
        return new DirectInputDeviceDescriptor(device.InstanceGuid, device.ProductGuid, device.ProductName, vendorId, productId,
            devicePath, topology.PnpInstanceId, topology.PhysicalIdentity, topology.UsagePage, topology.Usage, buttonCount, axisCount, topology.SelectionReason);
    }

    public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor)
    {
        var device = _directInput.CreateDevice(descriptor.InstanceGuid);
        device.SetDataFormat<RawJoystickState>();
        device.SetCooperativeLevel(_windowHandle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
        return new VorticeDirectInputDevice(device);
    }

    public void Dispose() => _directInput.Dispose();

    private sealed class VorticeDirectInputDevice(IDirectInputDevice8 device) : IDirectInputDevice
    {
        public void Acquire() => device.Acquire();
        public void Unacquire() => device.Unacquire();

        public DirectInputState ReadState()
        {
            device.Poll();
            var state = device.GetCurrentJoystickState();
            return new DirectInputState(state.Buttons, state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ, state.PointOfViewControllers);
        }

        public void Dispose() => device.Dispose();
    }

    private static ushort GetVendorId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 0);

    private static ushort GetProductId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 2);
}
