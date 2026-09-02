using System.Diagnostics;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayDelayedSliderCommitTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(2000);

    // Deterministic stand-in for the 2s wait: each call parks until Elapse() (or cancellation).
    private sealed class ManualDelay
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource> _live = new();

        public Func<TimeSpan, CancellationToken, Task> Func => (_, ct) =>
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) _live.Add(tcs);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        };

        public void Elapse()
        {
            TaskCompletionSource[] snapshot;
            lock (_lock) { snapshot = _live.ToArray(); _live.Clear(); }
            foreach (var tcs in snapshot) tcs.TrySetResult();
        }
    }

    // Records every submitted value and lets the test settle each commit when it chooses.
    private sealed class GatedCommit
    {
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource<OverlaySliderCommitSettlement>> _pending = new();
        private readonly List<double> _submitted = new();

        public IReadOnlyList<double> Submitted { get { lock (_lock) return _submitted.ToArray(); } }

        public Func<double, Task<OverlaySliderCommitSettlement>> Func => value =>
        {
            var tcs = new TaskCompletionSource<OverlaySliderCommitSettlement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) { _submitted.Add(value); _pending.Enqueue(tcs); }
            return tcs.Task;
        };

        public void CompleteNext(OverlaySliderCommitSettlement settlement)
        {
            TaskCompletionSource<OverlaySliderCommitSettlement> tcs;
            lock (_lock) tcs = _pending.Dequeue();
            tcs.SetResult(settlement);
        }
    }

    private sealed class SettlementSink
    {
        private readonly object _lock = new();
        private readonly List<OverlaySliderCommitSettlement> _items = new();
        public Action<OverlaySliderCommitSettlement> Callback => s => { lock (_lock) _items.Add(s); };
        public IReadOnlyList<OverlaySliderCommitSettlement> Items { get { lock (_lock) return _items.ToArray(); } }
    }

    private static async Task SpinUntilAsync(Func<bool> condition, string because)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > 5000) Assert.Fail($"Timed out waiting: {because}");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Schedule_takes_immediate_draft_ownership_without_committing()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, _ => { }, Delay, delay.Func);

        helper.Schedule(55);

        Assert.True(helper.HasPendingDraft);
        Assert.True(helper.TryGetPendingValue(out var value));
        Assert.Equal(55, value);
        Assert.Empty(commit.Submitted);
    }

    [Fact]
    public async Task Rapid_edits_collapse_to_the_latest_value()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, _ => { }, Delay, delay.Func);

        helper.Schedule(55);
        helper.Schedule(60);
        helper.Schedule(65);
        delay.Elapse();

        await SpinUntilAsync(() => commit.Submitted.Count >= 1, "commit submitted");
        Assert.Equal(new[] { 65.0 }, commit.Submitted);
    }

    [Fact]
    public async Task Trailing_window_restarts_on_each_new_edit()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, _ => { }, Delay, delay.Func);

        helper.Schedule(55);   // window started
        helper.Schedule(60);   // previous window cancelled, restarted from here
        delay.Elapse();        // completes only the window from the last schedule

        await SpinUntilAsync(() => commit.Submitted.Count >= 1, "commit submitted");
        Assert.Equal(new[] { 60.0 }, commit.Submitted);
        Assert.DoesNotContain(55.0, commit.Submitted);
    }

    [Fact]
    public async Task Independent_helper_instances_do_not_cancel_each_other()
    {
        var delayA = new ManualDelay();
        var delayB = new ManualDelay();
        var commitA = new GatedCommit();
        var commitB = new GatedCommit();
        using var a = new OverlayDelayedSliderCommit(commitA.Func, _ => { }, Delay, delayA.Func);
        using var b = new OverlayDelayedSliderCommit(commitB.Func, _ => { }, Delay, delayB.Func);

        a.Schedule(55);
        b.Schedule(30);
        a.Schedule(60);

        Assert.True(b.TryGetPendingValue(out var bv));
        Assert.Equal(30, bv);
        Assert.True(a.TryGetPendingValue(out var av));
        Assert.Equal(60, av);

        delayB.Elapse();
        await SpinUntilAsync(() => commitB.Submitted.Count >= 1, "B commit submitted");
        Assert.Equal(new[] { 30.0 }, commitB.Submitted);
        Assert.Empty(commitA.Submitted);
    }

    [Fact]
    public async Task Pending_value_stays_queryable_while_a_commit_is_in_flight()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, _ => { }, Delay, delay.Func);

        helper.Schedule(65);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");

        Assert.True(helper.TryGetPendingValue(out var value));
        Assert.Equal(65, value);

        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 65, null));
        await SpinUntilAsync(() => !helper.HasPendingDraft, "draft cleared after settle");
    }

    [Fact]
    public async Task Current_success_settlement_clears_the_draft_and_fires_once()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(65);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");
        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 65, null));

        await SpinUntilAsync(() => sink.Items.Count == 1, "settlement delivered");
        Assert.False(helper.HasPendingDraft);
        Assert.True(sink.Items[0].Succeeded);
        Assert.Equal(65, sink.Items[0].AuthoritativeValue);

        await Task.Delay(40);
        Assert.Single(sink.Items);
    }

    [Fact]
    public async Task Current_failure_settlement_clears_the_draft_and_exposes_the_fallback()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(65);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");
        commit.CompleteNext(new OverlaySliderCommitSettlement(false, 50, "boom"));

        await SpinUntilAsync(() => sink.Items.Count == 1, "failure settlement delivered");
        Assert.False(helper.HasPendingDraft);
        Assert.False(sink.Items[0].Succeeded);
        Assert.Equal(50, sink.Items[0].AuthoritativeValue);
        Assert.Equal("boom", sink.Items[0].FailureMessage);
    }

    [Fact]
    public async Task Commit_that_throws_becomes_a_failure_settlement()
    {
        var delay = new ManualDelay();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(
            _ => throw new InvalidOperationException("kaboom"), sink.Callback, Delay, delay.Func);

        helper.Schedule(65);
        delay.Elapse();

        await SpinUntilAsync(() => sink.Items.Count == 1, "failure settlement delivered");
        Assert.False(sink.Items[0].Succeeded);
        Assert.Equal("kaboom", sink.Items[0].FailureMessage);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task Stale_in_flight_completion_cannot_overwrite_a_newer_draft()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(55);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "A in flight");

        helper.Schedule(60); // B becomes the current draft while A is still in flight
        Assert.True(helper.TryGetPendingValue(out var draft));
        Assert.Equal(60, draft);

        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 55, null)); // A settles late
        await Task.Delay(40);
        Assert.Empty(sink.Items);                       // A ignored as stale
        Assert.True(helper.TryGetPendingValue(out var stillB));
        Assert.Equal(60, stillB);                        // B still current

        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 2, "B in flight");
        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 60, null));

        await SpinUntilAsync(() => sink.Items.Count == 1, "B settlement delivered");
        Assert.Equal(60, sink.Items[0].AuthoritativeValue);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task CancelUnsubmitted_drops_the_draft_and_never_commits()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(55);
        helper.CancelUnsubmitted();
        delay.Elapse();

        await Task.Delay(40);
        Assert.Empty(commit.Submitted);
        Assert.Empty(sink.Items);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task CancelUnsubmitted_leaves_an_already_submitted_commit_to_settle_normally()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(65);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit already submitted");

        helper.CancelUnsubmitted(); // Overlay close after the commit already started -- must be a no-op here

        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 65, null));
        await SpinUntilAsync(() => sink.Items.Count == 1, "in-flight settlement still delivered");
        Assert.True(sink.Items[0].Succeeded);
        Assert.Equal(65, sink.Items[0].AuthoritativeValue);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task A_newer_schedule_still_makes_an_older_in_flight_completion_stale_after_cancel()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(55);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "A submitted");

        helper.CancelUnsubmitted(); // no-op: A is in flight
        helper.Schedule(60);        // B supersedes A

        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 55, null)); // A settles late
        await Task.Delay(40);
        Assert.Empty(sink.Items); // A ignored as stale

        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 2, "B submitted");
        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 60, null));
        await SpinUntilAsync(() => sink.Items.Count == 1, "B settlement delivered");
        Assert.Equal(60, sink.Items[0].AuthoritativeValue);
    }

    [Fact]
    public async Task Disposal_rejects_new_scheduling_and_suppresses_stale_settlement()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, Delay, delay.Func);

        helper.Schedule(55);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");

        helper.Dispose();
        helper.Schedule(60); // rejected after disposal
        commit.CompleteNext(new OverlaySliderCommitSettlement(true, 55, null)); // stale after disposal

        await Task.Delay(40);
        Assert.Equal(new[] { 55.0 }, commit.Submitted);
        Assert.Empty(sink.Items);
        Assert.False(helper.TryGetPendingValue(out _));
    }

    [Fact]
    public void Production_delay_matches_the_QAM_policy()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2000), OverlayDelayedSliderCommit.ProductionDelay);
    }
}
