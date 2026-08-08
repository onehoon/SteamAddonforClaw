using Vortice.DirectInput;

namespace SteamInputAddonforClaw.Input.DirectInput;

public sealed class VorticeDirectInputDeviceEnumerator : IDirectInputDeviceEnumerator
{
    private readonly IntPtr _windowHandle;
    private readonly IDirectInput8 _directInput;

    public VorticeDirectInputDeviceEnumerator(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _directInput = DInput.DirectInput8Create();
    }

    public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers()
    {
        return _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .Select(device => new DirectInputDeviceDescriptor(
                device.InstanceGuid,
                device.ProductGuid,
                device.ProductName,
                GetVendorId(device.ProductGuid),
                GetProductId(device.ProductGuid)))
            .ToArray();
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
            return new DirectInputState(state.Buttons);
        }

        public void Dispose() => device.Dispose();
    }

    private static ushort GetVendorId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 0);

    private static ushort GetProductId(Guid productGuid) => BitConverter.ToUInt16(productGuid.ToByteArray(), 2);
}
