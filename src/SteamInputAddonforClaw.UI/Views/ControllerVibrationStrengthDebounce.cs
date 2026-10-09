namespace SteamInputAddonforClaw.Views;

internal sealed class ControllerVibrationStrengthDebounce : IDisposable
{
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<Action, bool> _dispatch;
    private readonly Action<int, int> _settled;
    private CancellationTokenSource? _pending;
    private (int Left, int Right)? _latestPair;
    private bool _disposed;

    internal ControllerVibrationStrengthDebounce(
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        Func<Action, bool> dispatch,
        Action<int, int> settled)
    {
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _settled = settled ?? throw new ArgumentNullException(nameof(settled));
    }

    internal bool HasPendingDraft => _pending is not null;

    internal void Schedule(int leftPercent, int rightPercent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPending();
        _latestPair = (leftPercent, rightPercent);
        var pending = _pending = new CancellationTokenSource();
        _ = WaitForSettleAsync(pending);
    }

    internal bool FlushPending()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pending = _pending;
        if (pending is null || _latestPair is not { } pair)
            return false;

        _pending = null;
        _latestPair = null;
        Cancel(pending);
        _settled(pair.Left, pair.Right);
        return true;
    }

    internal void CancelPending()
    {
        var pending = _pending;
        _pending = null;
        _latestPair = null;
        Cancel(pending);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPending();
    }

    private async Task WaitForSettleAsync(CancellationTokenSource pending)
    {
        try
        {
            await _delayAsync(SettleDelay, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            return;
        }

        _dispatch(() => Complete(pending));
    }

    private void Complete(CancellationTokenSource pending)
    {
        if (_disposed || !ReferenceEquals(_pending, pending)) return;
        _pending = null;
        var pair = _latestPair;
        _latestPair = null;
        pending.Dispose();
        if (pair is { } latest)
            _settled(latest.Left, latest.Right);
    }

    private static void Cancel(CancellationTokenSource? pending)
    {
        if (pending is null) return;
        pending.Cancel();
        pending.Dispose();
    }
}
