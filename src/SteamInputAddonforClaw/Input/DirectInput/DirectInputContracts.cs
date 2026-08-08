namespace SteamInputAddonforClaw.Input.DirectInput;

public sealed record DirectInputDeviceDescriptor(
    Guid InstanceGuid,
    Guid ProductGuid,
    string ProductName,
    ushort VendorId,
    ushort ProductId);

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

public enum MsiClawInputStartStatus
{
    Started,
    AlreadyRunning,
    InitializationFailed,
    EnumerationFailed,
    Pid1902NotFound,
    Indeterminate,
    CreateDeviceFailed,
    AcquireFailed
}

public sealed record MsiClawInputStartResult(MsiClawInputStartStatus Status, string Message)
{
    public bool Started => Status == MsiClawInputStartStatus.Started;
}

public sealed record MsiClawInputTestSummary(
    int TestSession,
    long DurationMs,
    bool M1Observed,
    bool M2Observed,
    bool Independent,
    int ReadFailures,
    bool CleanupSucceeded,
    MsiClawInputStopReason StopReason);

public enum MsiClawInputStopReason
{
    Stopped,
    ReadStateFailed,
    InvalidButtonLayout
}
