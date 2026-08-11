using System.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal interface IInputReportTickSource
{
    ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken);
}

internal sealed class PeriodicInputReportTickSource(TimeSpan interval) : IInputReportTickSource
{
    private readonly long _intervalTicks = Stopwatch.Frequency * interval.Ticks / TimeSpan.TicksPerSecond;
    private long _nextDeadline;

    public async ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken)
    {
        var now = Stopwatch.GetTimestamp();
        if (_nextDeadline == 0) _nextDeadline = now + _intervalTicks;
        var remaining = _nextDeadline - now;
        if (remaining > 0)
        {
            var delay = TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        now = Stopwatch.GetTimestamp();
        do { _nextDeadline += _intervalTicks; } while (_nextDeadline <= now);
        return true;
    }
}

internal sealed class ClassicSteamControllerInputPublisher
{
    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly IViiperRuntime _runtime;
    private readonly uint _deviceId;
    private readonly IInputReportTickSource _ticks;
    private readonly Action<Exception>? _fault;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private uint _frame;
    private int _reportCount;
    private int _faultReported;

    internal ClassicSteamControllerInputPublisher(IControllerStateSnapshotSource snapshot, IViiperRuntime runtime, uint deviceId,
        IInputReportTickSource? ticks = null, Action<Exception>? fault = null)
    {
        _snapshot = snapshot; _runtime = runtime; _deviceId = deviceId;
        _ticks = ticks ?? new PeriodicInputReportTickSource(TimeSpan.FromMilliseconds(4)); _fault = fault;
    }

    internal bool IsRunning => _task is { IsCompleted: false };
    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The Classic Steam Controller publisher is already running.");
        _stop = new CancellationTokenSource();
        AppLog.Info("SteamOutput", "Classic Steam Controller publisher started.", ("DeviceId", _deviceId), ("NominalHz", 250));
        _task = PublishAsync(_stop.Token);
    }
    internal async Task StopAsync()
    {
        if (_stop is null) return;
        _stop.Cancel();
        if (_task is not null) await _task.ConfigureAwait(false);
        _stop.Dispose(); _stop = null; _task = null;
        AppLog.Info("SteamOutput", "Classic Steam Controller publisher stopped.", ("DeviceId", _deviceId), ("ReportCount", _reportCount));
    }
    private async Task PublishAsync(CancellationToken token)
    {
        try
        {
            while (await _ticks.WaitForTickAsync(token).ConfigureAwait(false))
            {
                var report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
                ClassicSteamControllerReportBuilder.Write(report, unchecked(++_frame), ClassicSteamControllerInputMapper.Map(_snapshot.LatestState));
                if (!_runtime.SetInput(_deviceId, report))
                {
                    ReportFault(new InvalidOperationException("VIIPER rejected a live Classic Steam Controller report."));
                    return;
                }
                _reportCount++;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportFault(exception); }
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) == 0)
        {
            AppLog.Error("SteamOutput", "Classic Steam Controller live output fault.", exception,
                ("DeviceId", _deviceId), ("ReportCount", _reportCount));
            _fault?.Invoke(exception);
        }
    }
}
