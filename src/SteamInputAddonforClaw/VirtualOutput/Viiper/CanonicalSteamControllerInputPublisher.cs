using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class CanonicalSteamControllerInputPublisher
{
    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalSteamControllerStateSink _sink;
    private readonly IInputReportTickSource _ticks;
    private readonly Action<Exception>? _fault;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;

    internal CanonicalSteamControllerInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalSteamControllerStateSink sink,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null)
    {
        _snapshot = snapshot;
        _sink = sink;
        _ticks = ticks ?? new PeriodicInputReportTickSource(TimeSpan.FromMilliseconds(4));
        _fault = fault;
    }

    internal bool IsRunning => _task is { IsCompleted: false };
    internal int PublishedStateCount => _publishedStateCount;

    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Controller publisher is already running.");
        _stop = new CancellationTokenSource();
        _task = PublishAsync(_stop.Token);
    }

    internal async Task StopAsync()
    {
        if (_stop is null) return;
        _stop.Cancel();
        if (_task is not null) await _task.ConfigureAwait(false);
        _stop.Dispose();
        _stop = null;
        _task = null;
    }

    private async Task PublishAsync(CancellationToken token)
    {
        try
        {
            while (await _ticks.WaitForTickAsync(token).ConfigureAwait(false))
            {
                var state = SteamControllerDeviceStateMapper.Map(_snapshot.LatestState);
                if (!_sink.SetState(state))
                {
                    ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Gordon state."));
                    return;
                }
                _publishedStateCount++;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportFault(exception); }
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Steam Controller publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
