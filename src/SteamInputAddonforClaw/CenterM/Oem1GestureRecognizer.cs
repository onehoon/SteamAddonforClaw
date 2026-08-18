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

internal interface IOem1GestureCancellationSource : IDisposable
{
    CancellationToken Token { get; }
    void Cancel();
}

internal interface IOem1GestureCancellationSourceFactory
{
    IOem1GestureCancellationSource Create();
}

internal sealed class Oem1GestureCancellationSource : IOem1GestureCancellationSource
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken Token => _source.Token;
    public void Cancel() => _source.Cancel();
    public void Dispose() => _source.Dispose();
}

internal sealed class Oem1GestureCancellationSourceFactory : IOem1GestureCancellationSourceFactory
{
    public IOem1GestureCancellationSource Create() => new Oem1GestureCancellationSource();
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
    [ThreadStatic]
    private static HashSet<long>? _currentDeliveries;

    private readonly object _gate = new();
    private readonly object _deliveryGate = new();
    private readonly bool _doubleClickEnabled;
    private readonly TimeSpan _doubleClickWindow;
    private readonly IOem1GestureDelay _delay;
    private readonly IOem1GestureClock _clock;
    private readonly Action<long>? _deliveryBeforeNotification;
    private readonly IOem1GestureCancellationSourceFactory _cancellationSourceFactory;
    private IOem1GestureCancellationSource? _pendingCancellation;
    private long _generation;
    private long _deliveryEpoch;
    private long _nextDeliveryId;
    private readonly HashSet<long> _activeDeliveries = [];
    private bool _firstPressPending;
    private long _firstPressTimestamp;
    private bool _disposed;

    internal Oem1GestureRecognizer(
        bool doubleClickEnabled,
        TimeSpan doubleClickWindow,
        IOem1GestureDelay? delay = null,
        IOem1GestureClock? clock = null,
        IOem1GestureCancellationSourceFactory? cancellationSourceFactory = null,
        Action<long>? deliveryBeforeNotification = null)
    {
        if (doubleClickEnabled && doubleClickWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleClickWindow), "The double-click window must be positive.");

        _doubleClickEnabled = doubleClickEnabled;
        _doubleClickWindow = doubleClickWindow;
        _delay = delay ?? new Oem1TaskDelay();
        _clock = clock ?? new Oem1StopwatchClock();
        _cancellationSourceFactory = cancellationSourceFactory ?? new Oem1GestureCancellationSourceFactory();
        _deliveryBeforeNotification = deliveryBeforeNotification;
    }

    internal event Action<Oem1Gesture>? GestureRecognized;
    internal event Action<Oem1Gesture, long>? GestureRecognizedWithEpoch;

    internal long CurrentDeliveryEpoch
    {
        get
        {
            lock (_gate)
                return _deliveryEpoch;
        }
    }

    internal void OnPress()
    {
        Oem1Gesture? immediate = null;
        long immediateDeliveryEpoch = 0;
        long generation = 0;
        CancellationToken token = default;
        var startTimeout = false;
        var now = _clock.GetTimestamp();

        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_doubleClickEnabled)
                {
                    immediate = Oem1Gesture.Single;
                    immediateDeliveryEpoch = _deliveryEpoch;
                }
                else if (_firstPressPending)
                {
                    if (_clock.GetElapsedTime(_firstPressTimestamp, now) < _doubleClickWindow)
                    {
                        _firstPressPending = false;
                        _generation++;
                        CancelPendingCore();
                        immediate = Oem1Gesture.Double;
                        immediateDeliveryEpoch = _deliveryEpoch;
                    }
                    else
                    {
                        _firstPressPending = false;
                        _generation++;
                        CancelPendingCore();
                        immediate = Oem1Gesture.Single;
                        immediateDeliveryEpoch = _deliveryEpoch;
                        BeginPendingPressCore(now, out generation, out token);
                        startTimeout = true;
                    }
                }
                else
                {
                    BeginPendingPressCore(now, out generation, out token);
                    startTimeout = true;
                }

            }

            if (immediate.HasValue)
                DeliverGesture(immediate.Value, immediateDeliveryEpoch);
        }
        finally
        {
            if (startTimeout)
                _ = CompleteSingleAfterTimeoutAsync(generation, token);
        }
    }

    internal void Reset()
    {
        InvalidatePending();
        WaitForDeliveriesToDrain();
    }

    internal void InvalidatePending()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _generation++;
            _deliveryEpoch++;
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
            _deliveryEpoch++;
            _firstPressPending = false;
            CancelPendingCore();
        }

        WaitForDeliveriesToDrain();
    }

    private async Task CompleteSingleAfterTimeoutAsync(long generation, CancellationToken cancellationToken)
    {
        long deliveryEpoch;
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
            var completedCancellation = _pendingCancellation;
            _pendingCancellation = null;
            completedCancellation?.Dispose();
            deliveryEpoch = _deliveryEpoch;
        }

        DeliverGesture(Oem1Gesture.Single, deliveryEpoch);
    }

    private void DeliverGesture(Oem1Gesture gesture, long deliveryEpoch)
    {
        long deliveryId;
        lock (_deliveryGate)
        {
            lock (_gate)
            {
                if (_disposed || deliveryEpoch != _deliveryEpoch)
                    return;

                deliveryId = ++_nextDeliveryId;
                _activeDeliveries.Add(deliveryId);
            }
        }

        (_currentDeliveries ??= []).Add(deliveryId);
        try
        {
            _deliveryBeforeNotification?.Invoke(deliveryEpoch);
            GestureRecognizedWithEpoch?.Invoke(gesture, deliveryEpoch);
            GestureRecognized?.Invoke(gesture);
        }
        finally
        {
            lock (_deliveryGate)
            {
                var current = _currentDeliveries!;
                var completedDeliveryId = current.Last();
                current.Remove(completedDeliveryId);
                _activeDeliveries.Remove(completedDeliveryId);
                if (current.Count == 0)
                    _currentDeliveries = null;
                Monitor.PulseAll(_deliveryGate);
            }
        }
    }

    private void WaitForDeliveriesToDrain()
    {
        lock (_deliveryGate)
        {
            while (_activeDeliveries.Any(id => _currentDeliveries is null || !_currentDeliveries.Contains(id)))
                Monitor.Wait(_deliveryGate);
        }
    }

    private void BeginPendingPressCore(long timestamp, out long generation, out CancellationToken token)
    {
        _firstPressPending = true;
        _firstPressTimestamp = timestamp;
        generation = ++_generation;
        _pendingCancellation = _cancellationSourceFactory.Create();
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
