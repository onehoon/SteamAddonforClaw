using Vortice;
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
        return new DirectInputDeviceDescriptor(device.InstanceGuid, device.ProductGuid, device.ProductName, GetVendorId(device.ProductGuid), GetProductId(device.ProductGuid),
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
        public void Acquire()
        {
            var result = device.Acquire();
            if (result == ResultCode.NotAcquired)
                throw new DirectInputOperationException(DirectInputFailureKind.NotAcquired, new InvalidOperationException(ResultCode.NotAcquired.ToString()));
            if (result == ResultCode.InputLost)
                throw new DirectInputOperationException(DirectInputFailureKind.InputLost, new InvalidOperationException(ResultCode.InputLost.ToString()));
            result.CheckError();
        }
        public void Unacquire() => device.Unacquire();

        public DirectInputState ReadState()
        {
            var result = device.Poll();
            if (result == ResultCode.NotAcquired)
                throw new DirectInputOperationException(DirectInputFailureKind.NotAcquired, new InvalidOperationException(ResultCode.NotAcquired.ToString()));
            if (result == ResultCode.InputLost)
                throw new DirectInputOperationException(DirectInputFailureKind.InputLost, new InvalidOperationException(ResultCode.InputLost.ToString()));
            result.CheckError();
            var state = device.GetCurrentJoystickState();
            return new DirectInputState(state.Buttons, state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ, state.PointOfViewControllers);
        }

        public void Dispose() => device.Dispose();
    }

    private static ushort GetVendorId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 0);

    private static ushort GetProductId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 2);
}
