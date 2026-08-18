using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

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

public enum MsiClawInputStopReason { Stopped, ReadStateFailed, InvalidButtonLayout, InitialStateNotReady }

internal interface IMsiClawPreparedInputSource : IAsyncDisposable
{
    event EventHandler<ControllerState>? StateChanged;
    bool IsRunning { get; }
    MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor);
    Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken);
    Task StopAsync();
}

internal sealed record MsiClawPhysicalInputIdentity(Guid InstanceGuid, string DevicePath, string PnpInstanceId, string PhysicalIdentity);

internal interface IMsiClawPhysicalInputIdentityProvider
{
    MsiClawPhysicalInputIdentity? CurrentIdentity { get; }
    long CurrentSessionGeneration { get; }
}

internal interface IMsiClawInputDiagnostic : IMsiClawPreparedInputSource
{
    event EventHandler<MsiClawInputTestSummary>? TestCompleted;
    MsiClawInputStartResult Start();
}
