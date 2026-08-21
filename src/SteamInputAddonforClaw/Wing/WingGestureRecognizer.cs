using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Wing;

internal sealed class WingGestureRecognizer : IDisposable
{
    private readonly Func<bool> _doubleEnabled;
    private readonly IOem1GestureDelay _delay;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _first;
    private bool _disposed;
    private long _generation;
    private long _firstPressTimestamp;

    internal WingGestureRecognizer(Func<bool> doubleEnabled, IOem1GestureDelay? delay = null, TimeProvider? timeProvider = null)
    { _doubleEnabled = doubleEnabled ?? throw new ArgumentNullException(nameof(doubleEnabled)); _delay = delay ?? new Oem1TaskDelay(); _time = timeProvider ?? TimeProvider.System; }

    internal event Action<WingGesture>? GestureRecognized;
    internal bool HasPending { get { lock (_gate) return _first; } }

    internal void OnPress()
    {
        WingGesture? gesture = null;
        lock (_gate)
        {
            if (_disposed) return;
            if (_first)
            {
                var inWindow = _time.GetElapsedTime(_firstPressTimestamp, _time.GetTimestamp()) < TimeSpan.FromMilliseconds(200);
                _first = false; _generation++; _pending?.Cancel(); _pending = null;
                gesture = inWindow ? WingGesture.Double : WingGesture.Single;
            }
            else if (_doubleEnabled())
            {
                _first = true; _firstPressTimestamp = _time.GetTimestamp(); _generation++;
                _pending = new CancellationTokenSource(); var g = _generation; _ = CompleteAsync(g, _pending.Token); return;
            }
            else gesture = WingGesture.Single;
        }
        Deliver(gesture!.Value);
    }

    internal void InvalidatePending()
    { lock (_gate) { _first = false; _generation++; _pending?.Cancel(); _pending = null; } }

    private async Task CompleteAsync(long generation, CancellationToken token)
    {
        try { await _delay.DelayAsync(TimeSpan.FromMilliseconds(200), token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        lock (_gate)
        { if (_disposed || !_first || generation != _generation) return; _first = false; _pending = null; }
        Deliver(WingGesture.Single);
    }

    private void Deliver(WingGesture gesture)
    { AppLog.Debug("Wing.Event", gesture == WingGesture.Single ? "GestureSingle" : "GestureDouble"); GestureRecognized?.Invoke(gesture); }

    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _first = false; _pending?.Cancel(); _pending = null; } }
}
