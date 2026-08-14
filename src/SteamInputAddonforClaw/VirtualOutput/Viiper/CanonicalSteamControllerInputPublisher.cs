using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class CanonicalSteamControllerInputPublisher
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly ICanonicalSteamControllerStateSink _sink;
    private readonly IInputReportTickSource _ticks;
    private readonly Action<Exception>? _fault;
    private readonly Func<long> _timestampProvider;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private int _publishedStateCount;
    private int _faultReported;

    // M5 diagnostics: mapped (post-SteamControllerDeviceStateMapper) D-pad transition tracking. Instance
    // state (not static like ControllerStateDiagnostics) because multiple publishers can run in the same
    // process, e.g. in tests.
    private bool _hasLoggedDPadState;
    private (byte Up, byte Right, byte Down, byte Left) _lastLoggedDPad;

    // M5 diagnostics: ~1 Hz heartbeat counters, reset every HeartbeatInterval.
    private long _lastHeartbeatTimestamp;
    private int _setStateCallsSinceHeartbeat;
    private long _maxSetStateTicksSinceHeartbeat;
    private long _totalSetStateFailures;

    internal CanonicalSteamControllerInputPublisher(
        IControllerStateSnapshotSource snapshot,
        ICanonicalSteamControllerStateSink sink,
        IInputReportTickSource? ticks = null,
        Action<Exception>? fault = null,
        Func<long>? timestampProvider = null)
    {
        _snapshot = snapshot;
        _sink = sink;
        _ticks = ticks ?? new PeriodicInputReportTickSource(TimeSpan.FromMilliseconds(4));
        _fault = fault;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    internal bool IsRunning => _task is { IsCompleted: false };
    internal int PublishedStateCount => _publishedStateCount;

    internal void Start()
    {
        if (IsRunning) throw new InvalidOperationException("The canonical Steam Controller publisher is already running.");
        _stop = new CancellationTokenSource();
        _lastHeartbeatTimestamp = _timestampProvider();
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

                // M5 diagnostics only run when Info (or Debug) is actually enabled: on the 4 ms hot
                // path, avoid the timestamp sampling / comparisons / heartbeat bookkeeping entirely
                // when logging is Off, rather than relying only on AppLog's own internal level check.
                var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Info);
                if (diagnosticsEnabled) LogMappedDPadTransitionIfChanged(state);

                var callStart = diagnosticsEnabled ? _timestampProvider() : 0;
                var accepted = _sink.SetState(state);

                if (diagnosticsEnabled)
                {
                    var callDuration = _timestampProvider() - callStart;
                    _setStateCallsSinceHeartbeat++;
                    if (callDuration > _maxSetStateTicksSinceHeartbeat) _maxSetStateTicksSinceHeartbeat = callDuration;
                }

                if (!accepted)
                {
                    if (diagnosticsEnabled) _totalSetStateFailures++;
                    ReportFault(new InvalidOperationException("Canonical VIIPER rejected a typed Gordon state."));
                    return;
                }
                _publishedStateCount++;

                if (diagnosticsEnabled) EmitHeartbeatIfDue();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ReportFault(exception); }
    }

    private void LogMappedDPadTransitionIfChanged(SteamControllerDeviceState state)
    {
        var current = (state.DPadUp, state.DPadRight, state.DPadDown, state.DPadLeft);
        if (_hasLoggedDPadState && _lastLoggedDPad == current) return;
        _hasLoggedDPadState = true;
        _lastLoggedDPad = current;
        AppLog.Info("SteamOutput", "Canonical mapped D-pad state changed",
            ("Up", state.DPadUp), ("Right", state.DPadRight), ("Down", state.DPadDown), ("Left", state.DPadLeft));
    }

    private void EmitHeartbeatIfDue()
    {
        var now = _timestampProvider();
        if (Stopwatch.GetElapsedTime(_lastHeartbeatTimestamp, now) < HeartbeatInterval) return;

        AppLog.Info("SteamOutput", "Canonical Steam Controller publisher heartbeat",
            ("SetStateCallsLastSecond", _setStateCallsSinceHeartbeat),
            ("TotalPublishedStateCount", _publishedStateCount),
            ("SetStateFailures", _totalSetStateFailures),
            ("MaxSetStateDurationMs", Stopwatch.GetElapsedTime(0, _maxSetStateTicksSinceHeartbeat).TotalMilliseconds));

        _lastHeartbeatTimestamp = now;
        _setStateCallsSinceHeartbeat = 0;
        _maxSetStateTicksSinceHeartbeat = 0;
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Canonical Steam Controller publisher fault.", exception,
            ("PublishedStateCount", _publishedStateCount));
        _fault?.Invoke(exception);
    }
}
