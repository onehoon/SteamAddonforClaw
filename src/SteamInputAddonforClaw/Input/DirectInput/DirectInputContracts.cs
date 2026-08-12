namespace SteamInputAddonforClaw.Input.DirectInput;

public enum DirectInputFailureKind { NotAcquired, InputLost, Unrecoverable }

public sealed class DirectInputOperationException(DirectInputFailureKind kind, Exception innerException) : Exception("DirectInput operation failed.", innerException)
{
    public DirectInputFailureKind Kind { get; } = kind;
}

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

public sealed record DirectInputState(
    IReadOnlyList<bool> Buttons,
    int? X = null,
    int? Y = null,
    int? Z = null,
    int? RotationX = null,
    int? RotationY = null,
    int? RotationZ = null,
    IReadOnlyList<int>? pointOfViewControllers = null)
{
    public IReadOnlyList<int> PointOfViewControllers { get; init; } = pointOfViewControllers ?? [-1];
}

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
