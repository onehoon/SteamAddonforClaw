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

internal interface IMsiClawInputDiagnostic : IAsyncDisposable
{
    event EventHandler<MsiClawInputTestSummary>? TestCompleted;
    bool IsRunning { get; }
    MsiClawInputStartResult Start();
    Task StopAsync();
}
