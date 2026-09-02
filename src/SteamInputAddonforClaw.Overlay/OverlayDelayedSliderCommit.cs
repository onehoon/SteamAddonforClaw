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
    // Invoked only while this settlement is still the current generation. Delivered while holding
    // _sync so a concurrent Schedule() cannot slip in between the check and the delivery, so it
    // MUST NOT re-enter this helper synchronously (the fixture marshals via DispatcherQueue).
    private readonly Action<OverlaySliderCommitSettlement> _onCurrentSettlement;
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _sync = new();

    private int _generation;
    private double _pendingValue;
    private bool _hasPendingDraft;
    private bool _commitInFlight;
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
            // A fresh draft: any commit still running belongs to an older generation and is now
            // stale, so this new draft is once again "not submitted".
            _commitInFlight = false;
            generation = ++_generation;
            _scheduleCts?.Cancel();
            _scheduleCts?.Dispose();
            _scheduleCts = new CancellationTokenSource();
            token = _scheduleCts.Token;
        }

        _ = RunAsync(desiredValue, generation, token);
    }

    // Cancel a draft that is still waiting out the trailing window (e.g. Overlay begins hiding).
    // A commit that has already passed the delay and entered the commit call is left alone: it may
    // finish and deliver its current settlement without holding OQ4 capture open. Disposal, not
    // this method, is what suppresses an already-submitted operation's settlement.
    internal void CancelUnsubmitted()
    {
        lock (_sync)
        {
            if (_disposed || !_hasPendingDraft || _commitInFlight)
                return;

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
            _commitInFlight = false;
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
            // Past the delay: this draft is now submitted, so CancelUnsubmitted() must leave it be.
            _commitInFlight = true;
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
            // A newer Schedule replaced this draft while the commit was in flight, or the helper
            // was disposed on teardown: this completion is stale and must not clear the newer
            // pending fact or apply its settlement.
            if (_disposed || generation != _generation) return;
            _commitInFlight = false;
            _hasPendingDraft = false;

            // Deliver under _sync: Schedule() cannot make a newer generation current between the
            // final current-generation check and this settlement being applied.
            _onCurrentSettlement(settlement);
        }
    }
}
