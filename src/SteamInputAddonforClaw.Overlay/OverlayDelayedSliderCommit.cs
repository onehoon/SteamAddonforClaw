namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-07: the authoritative settlement a delayed slider commit produces. The delayed helper
// never becomes feature authority -- a later feature binding turns a Runtime result/readback into
// this and, on success, renders AuthoritativeValue back through OverlaySliderRow.ApplyState(...).
internal sealed record OverlaySliderCommitSettlement(
    bool Succeeded,
    double? AuthoritativeValue,
    string? FailureMessage);

// OQ5-UI-07: QAM-equivalent trailing debounce for one logical slider setting. One instance owns at
// most one current draft. It is NOT a global scheduler, a mutation-key dictionary, or a feature
// authority -- a future feature binding creates one instance per slider it binds. The OQ5-UI-06
// preview stays immediate; this only paces the request that follows the preview.
internal sealed class OverlayDelayedSliderCommit : IDisposable
{
    // Matches QAM_SLIDER_COMMIT_DELAY_MS in src/SteamInputAddonforClaw.QamHost/Frontend/qam.js.
    internal static readonly TimeSpan ProductionDelay = TimeSpan.FromMilliseconds(2000);

    private readonly Func<double, Task<OverlaySliderCommitSettlement>> _commitAsync;
    private readonly Action<OverlaySliderCommitSettlement> _onCurrentSettlement;
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _sync = new();

    private int _generation;
    private double _pendingValue;
    private bool _hasPendingDraft;
    private bool _disposed;
    private CancellationTokenSource? _scheduleCts;

    internal OverlayDelayedSliderCommit(
        Func<double, Task<OverlaySliderCommitSettlement>> commitAsync,
        Action<OverlaySliderCommitSettlement> onCurrentSettlement,
        TimeSpan delay,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _commitAsync = commitAsync;
        _onCurrentSettlement = onCurrentSettlement;
        _delay = delay;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    internal bool HasPendingDraft { get { lock (_sync) return _hasPendingDraft; } }

    // The latest desired value while a timer or current commit is still pending. This is the seam a
    // future invalidation handler uses to keep the pending draft visible instead of snapping back.
    internal bool TryGetPendingValue(out double value)
    {
        lock (_sync)
        {
            value = _hasPendingDraft ? _pendingValue : default;
            return _hasPendingDraft;
        }
    }

    // A new emitted desired value: replace any unsubmitted value, restart the trailing window.
    internal void Schedule(double desiredValue)
    {
        CancellationToken token;
        int generation;
        lock (_sync)
        {
            if (_disposed) return;
            _pendingValue = desiredValue;
            _hasPendingDraft = true;
            generation = ++_generation;
            _scheduleCts?.Cancel();
            _scheduleCts?.Dispose();
            _scheduleCts = new CancellationTokenSource();
            token = _scheduleCts.Token;
        }

        _ = RunAsync(desiredValue, generation, token);
    }

    // Cancel an unsubmitted scheduled draft (e.g. Overlay begins hiding). An already in-flight
    // commit is left to settle, but its completion is stale by generation and cannot become
    // current UI authority.
    internal void CancelUnsubmitted()
    {
        lock (_sync)
        {
            _generation++;
            _hasPendingDraft = false;
            _scheduleCts?.Cancel();
            _scheduleCts?.Dispose();
            _scheduleCts = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _hasPendingDraft = false;
            _scheduleCts?.Cancel();
            _scheduleCts?.Dispose();
            _scheduleCts = null;
        }
    }

    private async Task RunAsync(double value, int generation, CancellationToken token)
    {
        try
        {
            await _delayAsync(_delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed || generation != _generation) return;
        }

        OverlaySliderCommitSettlement settlement;
        try
        {
            settlement = await _commitAsync(value).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            settlement = new OverlaySliderCommitSettlement(false, null, exception.Message);
        }

        lock (_sync)
        {
            // A newer Schedule replaced this draft while the commit was in flight: its completion
            // is stale and must not clear the newer pending fact or apply its settlement.
            if (_disposed || generation != _generation) return;
            _hasPendingDraft = false;
        }

        _onCurrentSettlement(settlement);
    }
}
