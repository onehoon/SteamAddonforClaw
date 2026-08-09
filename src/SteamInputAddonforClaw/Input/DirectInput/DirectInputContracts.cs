namespace SteamInputAddonforClaw.Input.DirectInput;

public sealed record DirectInputDeviceDescriptor(
    Guid InstanceGuid,
    Guid ProductGuid,
    string ProductName,
    ushort VendorId,
    ushort ProductId,
    string? DevicePath = null,
    string? PnpInstanceId = null,
    string? PhysicalIdentity = null,
    ushort? UsagePage = null,
    ushort? Usage = null,
    int? ButtonCount = null,
    int? AxisCount = null,
    string? TopologyReason = null);

public sealed record DirectInputState(IReadOnlyList<bool> Buttons);

public interface IDirectInputDeviceEnumerator : IDisposable
{
    IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers();
    IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor);
}

public interface IDirectInputDevice : IDisposable
{
    void Acquire();
    void Unacquire();
    DirectInputState ReadState();
}
