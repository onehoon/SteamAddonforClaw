using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Wing;

internal sealed class WingGestureRecognizer : IDisposable
{
    private static readonly TimeSpan DoubleWindow = TimeSpan.FromMilliseconds(200);
    private readonly Func<bool> _doubleEnabled;
    private readonly IOem1GestureDelay _delay;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _first, _disposed;
    private long _firstTimestamp, _firstEpoch, _generation;
    internal WingGestureRecognizer(Func<bool> doubleEnabled, IOem1GestureDelay? delay = null, TimeProvider? timeProvider = null)
    { _doubleEnabled = doubleEnabled ?? throw new ArgumentNullException(nameof(doubleEnabled)); _delay = delay ?? new Oem1TaskDelay(); _time = timeProvider ?? TimeProvider.System; }
    internal event Action<WingGestureDelivery>? GestureRecognized;
    internal bool HasPending { get { lock (_gate) return _first; } }
    internal void OnPress(long authorityEpoch)
    {
        WingGestureDelivery? completed = null; var beginNew = false; var now = _time.GetTimestamp();
        lock (_gate)
        {
            if (_disposed) return;
            if (_first)
            {
                var inWindow = _firstEpoch == authorityEpoch && _time.GetElapsedTime(_firstTimestamp, now) < DoubleWindow;
                var epoch = _firstEpoch; _first = false; _generation++; CancelPendingCore();
                completed = new(inWindow ? WingGesture.Double : WingGesture.Single, epoch); beginNew = !inWindow;
            }
            else beginNew = true;
        }
        if (completed is { } delivery) Deliver(delivery);
        if (beginNew) BeginCurrentPress(now, authorityEpoch);
    }
    private void BeginCurrentPress(long timestamp, long epoch)
    {
        if (!_doubleEnabled()) { Deliver(new(WingGesture.Single, epoch)); return; }
        lock (_gate)
        {
            if (_disposed) return; _first = true; _firstTimestamp = timestamp; _firstEpoch = epoch; _generation++;
            var generation = _generation; _pending = new CancellationTokenSource(); _ = CompleteAsync(generation, epoch, _pending.Token);
        }
    }
    internal void InvalidatePending() { lock (_gate) { _first = false; _generation++; CancelPendingCore(); } }
    private async Task CompleteAsync(long generation, long epoch, CancellationToken token)
    {
        try { await _delay.DelayAsync(DoubleWindow, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        lock (_gate) { if (_disposed || !_first || generation != _generation) return; _first = false; _pending = null; }
        Deliver(new(WingGesture.Single, epoch));
    }
    private void Deliver(WingGestureDelivery delivery) { AppLog.Debug("Wing.Event", delivery.Gesture == WingGesture.Single ? "GestureSingle" : "GestureDouble"); GestureRecognized?.Invoke(delivery); }
    private void CancelPendingCore() { _pending?.Cancel(); _pending?.Dispose(); _pending = null; }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _first = false; CancelPendingCore(); } }
}
