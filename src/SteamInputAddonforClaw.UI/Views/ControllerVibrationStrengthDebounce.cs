namespace SteamInputAddonforClaw.Views;

internal sealed class ControllerVibrationStrengthDebounce : IDisposable
{
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<Action, bool> _dispatch;
    private readonly Action<int, int> _settled;
    private CancellationTokenSource? _pending;
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
        var pending = _pending = new CancellationTokenSource();
        _ = WaitForSettleAsync(pending, leftPercent, rightPercent);
    }

    internal void CancelPending()
    {
        var pending = _pending;
        _pending = null;
        Cancel(pending);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPending();
    }

    private async Task WaitForSettleAsync(CancellationTokenSource pending, int leftPercent, int rightPercent)
    {
        try
        {
            await _delayAsync(SettleDelay, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            return;
        }

        _dispatch(() => Complete(pending, leftPercent, rightPercent));
    }

    private void Complete(CancellationTokenSource pending, int leftPercent, int rightPercent)
    {
        if (_disposed || !ReferenceEquals(_pending, pending)) return;
        _pending = null;
        pending.Dispose();
        _settled(leftPercent, rightPercent);
    }

    private static void Cancel(CancellationTokenSource? pending)
    {
        if (pending is null) return;
        pending.Cancel();
        pending.Dispose();
    }
}
