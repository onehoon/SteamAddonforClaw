using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

// SF-V2-07 section 32: a valid Runtime settlement (including a normal typed feature failure) is
// Result != null; a thrown operation/transport-side failure is Result == null with a narrow
// message. Never a synthesized QuickSettingsMutationResult standing in for the latter.
internal sealed record QuickSettingsCommitSettlement(QuickSettingsMutationResult? Result, string? OperationFailureMessage);

// OQ5-UI-07 mechanics, refined for the real shared Quick Settings payload (SF-V2-07 section 21):
// QAM-equivalent trailing debounce for one logical slider/group setting. One instance owns at most
// one current draft and is NOT a global scheduler, a mutation-key dictionary, or a feature
// authority -- OverlayQuickSettingsPageBinding creates one instance per pending row/group key. The
// OQ5-UI-06 preview stays immediate; this only paces the request that follows the preview. The
// delay is supplied by the caller per Schedule() call (from the row's shared CommitPolicy) rather
// than being an Overlay-owned production constant.
internal sealed class OverlayDelayedSliderCommit : IDisposable
{
    private readonly Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> _commitAsync;
    // Raised with the generation that produced this settlement, after the background stale check
    // passes. The consumer marshals to its UI thread and MUST re-check IsCurrentGeneration there
    // before applying, because a newer Schedule() (or a newer edit already queued ahead of the
    // marshalled callback) can make the settlement stale between here and the actual apply.
    private readonly Action<int, QuickSettingsCommitSettlement> _onCurrentSettlement;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _sync = new();

    private int _generation;
    private QuickSettingsMutationIntent? _pendingIntent;
    private bool _hasPendingDraft;
    private bool _commitInFlight;
    private bool _disposed;
    private CancellationTokenSource? _scheduleCts;

    internal OverlayDelayedSliderCommit(
        Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> commitAsync,
        Action<int, QuickSettingsCommitSettlement> onCurrentSettlement,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _commitAsync = commitAsync;
        _onCurrentSettlement = onCurrentSettlement;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    internal bool HasPendingDraft { get { lock (_sync) return _hasPendingDraft; } }

    // Re-check at the UI apply boundary: true only while `generation` is still the current one and
    // the helper is alive. A settlement whose generation is no longer current must not be applied
    // over the newer pending preview.
    internal bool IsCurrentGeneration(int generation)
    {
        lock (_sync)
            return !_disposed && generation == _generation;
    }

    // The latest desired intent while a timer or current commit is still pending. This is the seam
    // an invalidation handler uses to keep the pending draft visible instead of snapping back.
    internal bool TryGetPendingIntent(out QuickSettingsMutationIntent intent)
    {
        lock (_sync)
        {
            intent = _hasPendingDraft ? _pendingIntent! : null!;
            return _hasPendingDraft;
        }
    }

    // A new emitted desired intent: replace any unsubmitted intent, restart the trailing window
    // using the caller-supplied delay (the row/group's current shared CommitPolicy).
    internal void Schedule(QuickSettingsMutationIntent intent, TimeSpan delay)
    {
        CancellationToken token;
        int generation;
        lock (_sync)
        {
            if (_disposed) return;
            _pendingIntent = intent;
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

        _ = RunAsync(intent, generation, delay, token);
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
            _pendingIntent = null;
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
            _pendingIntent = null;
            _scheduleCts?.Cancel();
            _scheduleCts?.Dispose();
            _scheduleCts = null;
        }
    }

    private async Task RunAsync(QuickSettingsMutationIntent intent, int generation, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await _delayAsync(delay, token).ConfigureAwait(false);
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

        QuickSettingsCommitSettlement settlement;
        try
        {
            var result = await _commitAsync(intent).ConfigureAwait(false);
            settlement = new QuickSettingsCommitSettlement(result, null);
        }
        catch (Exception exception)
        {
            // Operation/transport failure (section 32): never a synthesized product Result.
            settlement = new QuickSettingsCommitSettlement(null, exception.Message);
        }

        lock (_sync)
        {
            // A newer Schedule replaced this draft while the commit was in flight, or the helper
            // was disposed on teardown: this completion is stale and must not clear the newer
            // pending fact or raise its settlement.
            if (_disposed || generation != _generation) return;
            _commitInFlight = false;
            _hasPendingDraft = false;
            _pendingIntent = null;
        }

        // Not under _sync: the consumer marshals to its UI thread and re-checks IsCurrentGeneration
        // there, so a Schedule() (or an edit queued ahead of the marshalled callback) that lands
        // after this point is still caught at the actual apply boundary.
        _onCurrentSettlement(generation, settlement);
    }
}
