using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Wing;

internal sealed class WingGestureRecognizer : IDisposable
{
    private static readonly TimeSpan DoubleWindow = TimeSpan.FromMilliseconds(200);
    private readonly Func<bool> _doubleEnabled;
    private readonly IOem1GestureDelay _delay;
    private readonly TimeProvider _time;
    private readonly object _operationGate = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _first;
    private bool _disposed;
    private long _generation;
    private long _firstPressTimestamp;
    private long _firstAuthorityEpoch;

    internal WingGestureRecognizer(Func<bool> doubleEnabled, IOem1GestureDelay? delay = null, TimeProvider? timeProvider = null)
    {
        _doubleEnabled = doubleEnabled ?? throw new ArgumentNullException(nameof(doubleEnabled));
        _delay = delay ?? new Oem1TaskDelay();
        _time = timeProvider ?? TimeProvider.System;
    }

    internal event Action<WingGestureDelivery>? GestureRecognized;
    internal bool HasPending { get { lock (_gate) return _first; } }

    internal void OnPress(long authorityEpoch)
    {
        WingGestureDelivery? completed = null;
        bool beginCurrent = false;
        long now = _time.GetTimestamp();
        lock (_operationGate)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_first)
                {
                    var inWindow = _time.GetElapsedTime(_firstPressTimestamp, now) < DoubleWindow;
                    var previousEpoch = _firstAuthorityEpoch;
                    _first = false;
                    _generation++;
                    CancelPendingCore();
                    if (inWindow && previousEpoch == authorityEpoch)
                        completed = new(WingGesture.Double, previousEpoch);
                    else
                    {
                        completed = new(WingGesture.Single, previousEpoch);
                        beginCurrent = true;
                    }
                }
                else beginCurrent = true;
            }
            if (completed is { } delivery) Deliver(delivery);
            if (beginCurrent) BeginCurrentPressCore(now, authorityEpoch);
        }
    }

    internal void InvalidatePending()
    { lock (_operationGate) lock (_gate) { _first = false; _generation++; CancelPendingCore(); } }

    private void BeginCurrentPressCore(long timestamp, long authorityEpoch)
    {
        if (_disposed) return;
        if (!_doubleEnabled())
        {
            Deliver(new(WingGesture.Single, authorityEpoch));
            return;
        }
        _first = true;
        _firstPressTimestamp = timestamp;
        _firstAuthorityEpoch = authorityEpoch;
        var generation = ++_generation;
        _pending = new CancellationTokenSource();
        _ = CompleteAsync(generation, authorityEpoch, _pending.Token);
    }

    private async Task CompleteAsync(long generation, long authorityEpoch, CancellationToken token)
    {
        try { await _delay.DelayAsync(DoubleWindow, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        lock (_operationGate)
        {
            lock (_gate)
            {
                if (_disposed || !_first || generation != _generation) return;
                _first = false;
                CancelPendingCore();
            }
            Deliver(new(WingGesture.Single, authorityEpoch));
        }
    }

    private void Deliver(WingGestureDelivery delivery)
    {
        AppLog.Debug("Wing.Event", delivery.Gesture == WingGesture.Single ? "GestureSingle" : "GestureDouble", ("AuthorityEpoch", delivery.AuthorityEpoch));
        GestureRecognized?.Invoke(delivery);
    }

    private void CancelPendingCore() { _pending?.Cancel(); _pending?.Dispose(); _pending = null; }

    public void Dispose()
    {
        lock (_operationGate)
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _first = false;
            _generation++;
            CancelPendingCore();
        }
    }
}
