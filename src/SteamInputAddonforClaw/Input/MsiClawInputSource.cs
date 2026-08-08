using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input.DirectInput;

namespace SteamInputAddonforClaw.Input;

public sealed class MsiClawInputSource : IAsyncDisposable
{
    private const ushort MsiVendorId = 0x0DB0;
    private const ushort DirectInputProductId = 0x1902;
    private const int M1ButtonIndex = 15;
    private const int M2ButtonIndex = 16;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
    private readonly IDirectInputDeviceEnumerator _enumerator;
    private readonly Lock _sync = new();
    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;
    private int _testSession;

    public MsiClawInputSource(IDirectInputDeviceEnumerator enumerator)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
    }

    public event EventHandler<ControllerState>? StateChanged;
    public event EventHandler<MsiClawInputTestSummary>? TestCompleted;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _pollingTask is not null;
            }
        }
    }

    public MsiClawInputStartResult Start()
    {
        lock (_sync)
        {
            if (_pollingTask is not null)
            {
                return new MsiClawInputStartResult(MsiClawInputStartStatus.AlreadyRunning, "M1/M2 DirectInput test is already running.");
            }

            var selection = SelectDevice();
            if (selection.Status != MsiClawInputStartStatus.Started)
            {
                return new MsiClawInputStartResult(selection.Status, selection.Message);
            }

            IDirectInputDevice? device = null;
            try
            {
                device = _enumerator.CreateDevice(selection.Descriptor!);
                AppLog.Info("DirectInput", "Device acquire started.", ("InstanceGuid", selection.Descriptor!.InstanceGuid));
                var stopwatch = Stopwatch.StartNew();
                device.Acquire();
                AppLog.Info("DirectInput", "Device acquire succeeded.", ("ElapsedMs", stopwatch.ElapsedMilliseconds));
                var cancellation = new CancellationTokenSource();
                var session = ++_testSession;
                _pollingCancellation = cancellation;
                _pollingTask = PollAsync(device, cancellation, session);
                return new MsiClawInputStartResult(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.");
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "Device acquire failed.", exception, ("Action", "AbortInputTest"), ("Reason", "AcquireException"));
                device?.Dispose();
                return new MsiClawInputStartResult(MsiClawInputStartStatus.AcquireFailed, "DirectInput device acquisition failed. No controller settings were changed.");
            }
        }
    }

    public async Task StopAsync()
    {
        Task? pollingTask;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            pollingTask = _pollingTask;
            cancellation = _pollingCancellation;
        }

        if (pollingTask is null)
        {
            return;
        }

        cancellation!.Cancel();
        await pollingTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _enumerator.Dispose();
    }

    private SelectionResult SelectDevice()
    {
        AppLog.Info("MsiInput", "DirectInput enumeration started.");
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<DirectInputDeviceDescriptor> candidates;
        try
        {
            candidates = _enumerator.EnumerateGameControllers();
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiInput", "DirectInput enumeration failed.", exception, ("Action", "DoNotAcquire"));
            return new SelectionResult(MsiClawInputStartStatus.Pid1902NotFound, "DirectInput PID_1902 device not found. No changes were made.", null);
        }

        AppLog.Debug("MsiInput", "DirectInput enumeration completed.", ("DeviceCount", candidates.Count), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        foreach (var candidate in candidates)
        {
            var matches = candidate.VendorId == MsiVendorId && candidate.ProductId == DirectInputProductId;
            AppLog.Trace("MsiInput", matches ? "DirectInput device candidate." : "DirectInput device ignored.", ("InstanceGuid", candidate.InstanceGuid), ("ProductGuid", candidate.ProductGuid), ("ProductName", candidate.ProductName), ("VID", $"0x{candidate.VendorId:X4}"), ("PID", $"0x{candidate.ProductId:X4}"), ("Reason", matches ? "KnownMsiClawDirectInput" : "NotMsiClawPid1902"));
        }

        var selectedCandidates = candidates.Where(candidate => candidate.VendorId == MsiVendorId && candidate.ProductId == DirectInputProductId).ToArray();
        return selectedCandidates.Length switch
        {
            0 => new SelectionResult(MsiClawInputStartStatus.Pid1902NotFound, "DirectInput PID_1902 device not found. No changes were made.", null),
            1 => Select(selectedCandidates[0]),
            _ => new SelectionResult(MsiClawInputStartStatus.Indeterminate, "Multiple MSI Claw PID_1902 DirectInput devices were found. No changes were made.", null)
        };
    }

    private static SelectionResult Select(DirectInputDeviceDescriptor descriptor)
    {
        AppLog.Info("MsiInput", "MSI Claw DirectInput device selected.", ("VID", "0x0DB0"), ("PID", "0x1902"), ("InstanceGuid", descriptor.InstanceGuid), ("Reason", "KnownMsiClawDirectInput"));
        return new SelectionResult(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.", descriptor);
    }

    private async Task PollAsync(IDirectInputDevice device, CancellationTokenSource cancellation, int session)
    {
        var stopwatch = Stopwatch.StartNew();
        var previous = new ControllerState(false, false);
        var hasPrevious = false;
        var m1Observed = false;
        var m2Observed = false;
        var independent = false;
        var readFailures = 0;
        var cleanupSucceeded = true;

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                ControllerState current;
                try
                {
                    current = MapState(device.ReadState());
                }
                catch (Exception exception)
                {
                    readFailures++;
                    AppLog.Warn("DirectInput", "Controller state read failed.", exception, ("Attempt", readFailures), ("Action", "StopDiagnostic"));
                    break;
                }

                if (!hasPrevious || current != previous)
                {
                    LogStateChange(previous, current, hasPrevious);
                    StateChanged?.Invoke(this, current);
                    previous = current;
                    hasPrevious = true;
                }

                if (current.M1 && !m1Observed)
                {
                    m1Observed = true;
                    AppLog.Info("Diagnostics", "M1 input verified.", ("ButtonIndex", M1ButtonIndex));
                }

                if (current.M2 && !m2Observed)
                {
                    m2Observed = true;
                    AppLog.Info("Diagnostics", "M2 input verified.", ("ButtonIndex", M2ButtonIndex));
                }

                if (!independent && ((current.M1 && !current.M2) || (!current.M1 && current.M2)))
                {
                    independent = true;
                    AppLog.Info("Diagnostics", "Independent M1/M2 input verified.", ("M1ButtonIndex", M1ButtonIndex), ("M2ButtonIndex", M2ButtonIndex));
                }

                await Task.Delay(PollInterval, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                AppLog.Info("DirectInput", "Device unacquire started.");
                device.Unacquire();
                AppLog.Info("DirectInput", "Device unacquire completed.", ("Success", true));
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("Operation", "Unacquire"));
            }

            try
            {
                device.Dispose();
                AppLog.Info("DirectInput", "Device disposed.");
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("Operation", "Dispose"));
            }

            var summary = new MsiClawInputTestSummary(session, stopwatch.ElapsedMilliseconds, m1Observed, m2Observed, independent, readFailures, cleanupSucceeded);
            lock (_sync)
            {
                _pollingTask = null;
                _pollingCancellation?.Dispose();
                _pollingCancellation = null;
            }
            AppLog.Info("Diagnostics", "M1/M2 input diagnostic completed.", ("TestSession", summary.TestSession), ("DurationMs", summary.DurationMs), ("M1Observed", summary.M1Observed), ("M2Observed", summary.M2Observed), ("Independent", summary.Independent), ("ReadFailures", summary.ReadFailures), ("CleanupSucceeded", summary.CleanupSucceeded));
            TestCompleted?.Invoke(this, summary);
        }
    }

    private static ControllerState MapState(DirectInputState state)
    {
        if (state.Buttons.Count <= M2ButtonIndex)
        {
            AppLog.Warn("MsiInput", "DirectInput state has too few buttons.", null, ("ButtonCount", state.Buttons.Count), ("RequiredIndex", M2ButtonIndex), ("Action", "FailSafeNeutralState"));
            return new ControllerState(false, false);
        }

        return new ControllerState(state.Buttons[M1ButtonIndex], state.Buttons[M2ButtonIndex]);
    }

    private static void LogStateChange(ControllerState previous, ControllerState current, bool hasPrevious)
    {
        if (hasPrevious && previous.M1 != current.M1)
        {
            AppLog.Trace("MsiInput", "M1 state changed.", ("ButtonIndex", M1ButtonIndex), ("Previous", previous.M1), ("Current", current.M1));
        }
        if (hasPrevious && previous.M2 != current.M2)
        {
            AppLog.Trace("MsiInput", "M2 state changed.", ("ButtonIndex", M2ButtonIndex), ("Previous", previous.M2), ("Current", current.M2));
        }
        AppLog.Trace("MsiInput", "ControllerState changed.", ("M1", $"{previous.M1}->{current.M1}"), ("M2", $"{previous.M2}->{current.M2}"));
    }

    private sealed record SelectionResult(MsiClawInputStartStatus Status, string Message, DirectInputDeviceDescriptor? Descriptor);
}
