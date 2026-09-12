using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayDelayedSliderCommitTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(2000);

    private static QuickSettingsMutationIntent Intent(int value) => new(
        QuickSettingsPageId.Device, AppId: null, QuickSettingsRowId.DeviceTdpAcPl1,
        [new QuickSettingsRowValue(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(value))]);

    private static QuickSettingsMutationResult SuccessResult(int _) => new(
        true, null, new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, null, true, null, [], []));

    private static QuickSettingsMutationResult FailureResult(string message) => new(
        false, message, new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, null, true, null, [], []));

    private static int ValueOf(QuickSettingsMutationIntent intent) => intent.Values[0].Value.IntegerValue!.Value;

    // Deterministic stand-in for the trailing wait: each call parks until Elapse() (or cancellation).
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

    // Records every submitted intent and lets the test settle each commit when it chooses.
    private sealed class GatedCommit
    {
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource<QuickSettingsMutationResult>> _pending = new();
        private readonly List<int> _submitted = new();

        public IReadOnlyList<int> Submitted { get { lock (_lock) return _submitted.ToArray(); } }

        public Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> Func => intent =>
        {
            var tcs = new TaskCompletionSource<QuickSettingsMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) { _submitted.Add(ValueOf(intent)); _pending.Enqueue(tcs); }
            return tcs.Task;
        };

        public void CompleteNext(QuickSettingsMutationResult result)
        {
            TaskCompletionSource<QuickSettingsMutationResult> tcs;
            lock (_lock) tcs = _pending.Dequeue();
            tcs.SetResult(result);
        }
    }

    private sealed class SettlementSink
    {
        private readonly object _lock = new();
        private readonly List<(int Generation, QuickSettingsCommitSettlement Settlement)> _items = new();
        public Action<int, QuickSettingsCommitSettlement> Callback => (g, s) => { lock (_lock) _items.Add((g, s)); };
        public IReadOnlyList<(int Generation, QuickSettingsCommitSettlement Settlement)> Items { get { lock (_lock) return _items.ToArray(); } }
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
        using var helper = new OverlayDelayedSliderCommit(commit.Func, (_, _) => { }, delay.Func);

        helper.Schedule(Intent(55), Delay);

        Assert.True(helper.HasPendingDraft);
        Assert.True(helper.TryGetPendingIntent(out var intent));
        Assert.Equal(55, ValueOf(intent));
        Assert.Empty(commit.Submitted);
    }

    [Fact]
    public async Task Rapid_edits_collapse_to_the_latest_value()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, (_, _) => { }, delay.Func);

        helper.Schedule(Intent(55), Delay);
        helper.Schedule(Intent(60), Delay);
        helper.Schedule(Intent(65), Delay);
        delay.Elapse();

        await SpinUntilAsync(() => commit.Submitted.Count >= 1, "commit submitted");
        Assert.Equal(new[] { 65 }, commit.Submitted);
    }

    [Fact]
    public async Task Trailing_window_restarts_on_each_new_edit()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, (_, _) => { }, delay.Func);

        helper.Schedule(Intent(55), Delay);   // window started
        helper.Schedule(Intent(60), Delay);   // previous window cancelled, restarted from here
        delay.Elapse();                        // completes only the window from the last schedule

        await SpinUntilAsync(() => commit.Submitted.Count >= 1, "commit submitted");
        Assert.Equal(new[] { 60 }, commit.Submitted);
        Assert.DoesNotContain(55, commit.Submitted);
    }

    [Fact]
    public void Delay_comes_from_the_caller_not_a_production_constant()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, (_, _) => { }, delay.Func);

        // A 750ms shared policy is honored with no Overlay production 2000ms constant involved.
        helper.Schedule(Intent(55), TimeSpan.FromMilliseconds(750));

        Assert.True(helper.HasPendingDraft);
    }

    [Fact]
    public async Task Independent_helper_instances_do_not_cancel_each_other()
    {
        var delayA = new ManualDelay();
        var delayB = new ManualDelay();
        var commitA = new GatedCommit();
        var commitB = new GatedCommit();
        using var a = new OverlayDelayedSliderCommit(commitA.Func, (_, _) => { }, delayA.Func);
        using var b = new OverlayDelayedSliderCommit(commitB.Func, (_, _) => { }, delayB.Func);

        a.Schedule(Intent(55), Delay);
        b.Schedule(Intent(30), Delay);
        a.Schedule(Intent(60), Delay);

        Assert.True(b.TryGetPendingIntent(out var bIntent));
        Assert.Equal(30, ValueOf(bIntent));
        Assert.True(a.TryGetPendingIntent(out var aIntent));
        Assert.Equal(60, ValueOf(aIntent));

        delayB.Elapse();
        await SpinUntilAsync(() => commitB.Submitted.Count >= 1, "B commit submitted");
        Assert.Equal(new[] { 30 }, commitB.Submitted);
        Assert.Empty(commitA.Submitted);
    }

    [Fact]
    public async Task Pending_value_stays_queryable_while_a_commit_is_in_flight()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, (_, _) => { }, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");

        Assert.True(helper.TryGetPendingIntent(out var intent));
        Assert.Equal(65, ValueOf(intent));

        commit.CompleteNext(SuccessResult(65));
        await SpinUntilAsync(() => !helper.HasPendingDraft, "draft cleared after settle");
    }

    [Fact]
    public async Task Current_success_settlement_clears_the_draft_and_fires_once()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");
        commit.CompleteNext(SuccessResult(65));

        await SpinUntilAsync(() => sink.Items.Count == 1, "settlement delivered");
        Assert.False(helper.HasPendingDraft);
        Assert.NotNull(sink.Items[0].Settlement.Result);
        Assert.True(sink.Items[0].Settlement.Result!.Succeeded);
        Assert.Null(sink.Items[0].Settlement.OperationFailureMessage);

        await Task.Delay(40);
        Assert.Single(sink.Items);
    }

    [Fact]
    public async Task Current_typed_failure_settlement_clears_the_draft_and_carries_the_result()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");
        commit.CompleteNext(FailureResult("boom"));

        await SpinUntilAsync(() => sink.Items.Count == 1, "failure settlement delivered");
        Assert.False(helper.HasPendingDraft);
        Assert.NotNull(sink.Items[0].Settlement.Result);
        Assert.False(sink.Items[0].Settlement.Result!.Succeeded);
        Assert.Equal("boom", sink.Items[0].Settlement.Result!.FailureMessage);
        Assert.Null(sink.Items[0].Settlement.OperationFailureMessage);
    }

    [Fact]
    public async Task Commit_that_throws_becomes_an_operation_failure_with_no_result()
    {
        var delay = new ManualDelay();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(
            _ => throw new InvalidOperationException("kaboom"), sink.Callback, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();

        await SpinUntilAsync(() => sink.Items.Count == 1, "failure settlement delivered");
        Assert.Null(sink.Items[0].Settlement.Result);
        Assert.Equal("kaboom", sink.Items[0].Settlement.OperationFailureMessage);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task Stale_in_flight_completion_cannot_overwrite_a_newer_draft()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(55), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "A in flight");

        helper.Schedule(Intent(60), Delay); // B becomes the current draft while A is still in flight
        Assert.True(helper.TryGetPendingIntent(out var draft));
        Assert.Equal(60, ValueOf(draft));

        commit.CompleteNext(SuccessResult(55)); // A settles late
        await Task.Delay(40);
        Assert.Empty(sink.Items);                       // A ignored as stale
        Assert.True(helper.TryGetPendingIntent(out var stillB));
        Assert.Equal(60, ValueOf(stillB));               // B still current

        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 2, "B in flight");
        commit.CompleteNext(SuccessResult(60));

        await SpinUntilAsync(() => sink.Items.Count == 1, "B settlement delivered");
        Assert.True(sink.Items[0].Settlement.Result!.Succeeded);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task CancelUnsubmitted_drops_the_draft_and_never_commits()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(55), Delay);
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
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit already submitted");

        helper.CancelUnsubmitted(); // Overlay close after the commit already started -- must be a no-op here

        commit.CompleteNext(SuccessResult(65));
        await SpinUntilAsync(() => sink.Items.Count == 1, "in-flight settlement still delivered");
        Assert.True(sink.Items[0].Settlement.Result!.Succeeded);
        Assert.False(helper.HasPendingDraft);
    }

    [Fact]
    public async Task A_newer_schedule_still_makes_an_older_in_flight_completion_stale_after_cancel()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(55), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "A submitted");

        helper.CancelUnsubmitted(); // no-op: A is in flight
        helper.Schedule(Intent(60), Delay); // B supersedes A

        commit.CompleteNext(SuccessResult(55)); // A settles late
        await Task.Delay(40);
        Assert.Empty(sink.Items); // A ignored as stale

        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 2, "B submitted");
        commit.CompleteNext(SuccessResult(60));
        await SpinUntilAsync(() => sink.Items.Count == 1, "B settlement delivered");
        Assert.True(sink.Items[0].Settlement.Result!.Succeeded);
    }

    [Fact]
    public async Task Disposal_rejects_new_scheduling_and_suppresses_stale_settlement()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(55), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");

        helper.Dispose();
        helper.Schedule(Intent(60), Delay); // rejected after disposal
        commit.CompleteNext(SuccessResult(55)); // stale after disposal

        await Task.Delay(40);
        Assert.Equal(new[] { 55 }, commit.Submitted);
        Assert.Empty(sink.Items);
        Assert.False(helper.TryGetPendingIntent(out _));
    }

    [Fact]
    public async Task Settlement_raises_its_producing_generation_for_the_ui_apply_boundary()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(65), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "commit in flight");
        commit.CompleteNext(SuccessResult(65));
        await SpinUntilAsync(() => sink.Items.Count == 1, "settlement delivered");

        // No newer edit: the raised generation is still current, so a guarded UI apply proceeds.
        Assert.True(helper.IsCurrentGeneration(sink.Items[0].Generation));
    }

    [Fact]
    public async Task Settlement_is_dropped_at_the_ui_apply_boundary_when_a_newer_draft_became_current()
    {
        var delay = new ManualDelay();
        var commit = new GatedCommit();
        var sink = new SettlementSink();
        using var helper = new OverlayDelayedSliderCommit(commit.Func, sink.Callback, delay.Func);

        helper.Schedule(Intent(55), Delay);
        delay.Elapse();
        await SpinUntilAsync(() => commit.Submitted.Count == 1, "A submitted");
        commit.CompleteNext(SuccessResult(55));
        await SpinUntilAsync(() => sink.Items.Count == 1, "A settlement raised");

        var settledGeneration = sink.Items[0].Generation;
        Assert.True(helper.IsCurrentGeneration(settledGeneration));

        // A newer adjustment becomes current before A's marshalled UI apply would run (e.g. a
        // controller step queued on the DispatcherQueue ahead of the settlement callback).
        helper.Schedule(Intent(60), Delay);

        Assert.False(helper.IsCurrentGeneration(settledGeneration)); // A's UI apply must skip
        Assert.True(helper.TryGetPendingIntent(out var pending));
        Assert.Equal(60, ValueOf(pending));                          // B's preview stays current
    }
}
