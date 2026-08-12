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

public enum MsiClawInputStopReason { Stopped, ReadStateFailed, InvalidButtonLayout }

internal interface IMsiClawPreparedInputSource : IAsyncDisposable
{
    event EventHandler<ControllerState>? StateChanged;
    bool IsRunning { get; }
    MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor);
    Task StopAsync();
}

internal interface IMsiClawRuntimeRecoverableInputSource
{
    void ConfigureRoutingRecovery(
        Func<DirectInputDeviceDescriptor, CancellationToken, ValueTask<bool>> prepareIsolation,
        Action<DirectInputDeviceDescriptor> publishIdentity,
        Func<bool> recoveryAllowed,
        Func<ValueTask> terminalFaultHandler);
}

internal interface IControllerStateSnapshotSource
{
    ControllerState LatestState { get; }
}

internal sealed record MsiClawPhysicalInputIdentity(Guid InstanceGuid, string DevicePath, string PnpInstanceId, string PhysicalIdentity);

internal interface IMsiClawPhysicalInputIdentityProvider
{
    MsiClawPhysicalInputIdentity? CurrentIdentity { get; }
}

internal interface IMsiClawInputDiagnostic : IMsiClawPreparedInputSource
{
    event EventHandler<MsiClawInputTestSummary>? TestCompleted;
    MsiClawInputStartResult Start();
}
