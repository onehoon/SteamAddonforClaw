namespace SteamInputAddonforClaw.CenterM;

internal enum Oem1Gesture
{
    Single,
    Double
}

internal interface IOem1GestureDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal interface IOem1GestureClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);
}

internal sealed class Oem1StopwatchClock : IOem1GestureClock
{
    public long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);
}

internal sealed class Oem1TaskDelay : IOem1GestureDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

/// <summary>Recognizes semantic OEM1 presses without owning event acquisition or actions.</summary>
internal sealed class Oem1GestureRecognizer : IDisposable
{
    private readonly object _gate = new();
    private readonly bool _doubleClickEnabled;
    private readonly TimeSpan _doubleClickWindow;
    private readonly IOem1GestureDelay _delay;
    private readonly IOem1GestureClock _clock;
    private CancellationTokenSource? _pendingCancellation;
    private long _generation;
    private bool _firstPressPending;
    private long _firstPressTimestamp;
    private bool _disposed;

    internal Oem1GestureRecognizer(
        bool doubleClickEnabled,
        TimeSpan doubleClickWindow,
        IOem1GestureDelay? delay = null,
        IOem1GestureClock? clock = null)
    {
        if (doubleClickEnabled && doubleClickWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleClickWindow), "The double-click window must be positive.");

        _doubleClickEnabled = doubleClickEnabled;
        _doubleClickWindow = doubleClickWindow;
        _delay = delay ?? new Oem1TaskDelay();
        _clock = clock ?? new Oem1StopwatchClock();
    }

    internal event Action<Oem1Gesture>? GestureRecognized;

    internal void OnPress()
    {
        Oem1Gesture? immediate = null;
        long generation = 0;
        CancellationToken token = default;
        var now = _clock.GetTimestamp();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_doubleClickEnabled)
            {
                immediate = Oem1Gesture.Single;
            }
            else if (_firstPressPending)
            {
                if (_clock.GetElapsedTime(_firstPressTimestamp, now) < _doubleClickWindow)
                {
                    _firstPressPending = false;
                    _generation++;
                    CancelPendingCore();
                    immediate = Oem1Gesture.Double;
                }
                else
                {
                    _firstPressPending = false;
                    _generation++;
                    CancelPendingCore();
                    immediate = Oem1Gesture.Single;
                    BeginPendingPressCore(now, out generation, out token);
                }
            }
            else
            {
                BeginPendingPressCore(now, out generation, out token);
            }

            if (immediate.HasValue)
                GestureRecognized?.Invoke(immediate.Value);
        }

        if (token != default)
            _ = CompleteSingleAfterTimeoutAsync(generation, token);
    }

    internal void Reset()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _generation++;
            _firstPressPending = false;
            CancelPendingCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _generation++;
            _firstPressPending = false;
            CancelPendingCore();
        }
    }

    private async Task CompleteSingleAfterTimeoutAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await _delay.DelayAsync(_doubleClickWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || generation != _generation || !_firstPressPending)
                return;

            _firstPressPending = false;
            _pendingCancellation = null;
            GestureRecognized?.Invoke(Oem1Gesture.Single);
        }
    }

    private void BeginPendingPressCore(long timestamp, out long generation, out CancellationToken token)
    {
        _firstPressPending = true;
        _firstPressTimestamp = timestamp;
        generation = ++_generation;
        _pendingCancellation = new CancellationTokenSource();
        token = _pendingCancellation.Token;
    }

    private void CancelPendingCore()
    {
        _pendingCancellation?.Cancel();
        _pendingCancellation?.Dispose();
        _pendingCancellation = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Oem1GestureRecognizer));
    }
}
