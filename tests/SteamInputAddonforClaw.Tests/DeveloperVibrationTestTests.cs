using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DeveloperVibrationTestTests
{
    [Theory]
    [InlineData(FrontendVibrationTestCommand.Rumble, 32768, 32768)]
    [InlineData(FrontendVibrationTestCommand.Haptic, 32896, 32896)]
    public async Task Developer_command_uses_the_production_decoder_and_sink(FrontendVibrationTestCommand command, ushort large, ushort small)
    {
        var authority = new FeedbackAuthority();
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        Assert.True((await bridge.ProcessDeveloperTestAsync(Report(command), addDeveloperStop: true, CancellationToken.None)).Succeeded);
        await sink.WaitForValueAsync(new TwoMotorRumble(large, small), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task EB_test_sends_a_production_path_zero_after_250ms()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        Assert.True((await bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None)).Succeeded);

        await sink.WaitForValueAsync(TwoMotorRumble.Stopped, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task New_developer_test_cancels_the_previous_delayed_stop()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCall = 0;
        bridge.DeveloperDelayOverride = (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref delayCall) == 1)
            {
                firstEntered.TrySetResult();
                return firstRelease.Task.WaitAsync(cancellationToken);
            }
            secondEntered.TrySetResult();
            return secondRelease.Task.WaitAsync(cancellationToken);
        };
        var oldTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None);
        await firstEntered.Task;
        var newTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Haptic), true, CancellationToken.None);
        await secondEntered.Task;
        firstRelease.TrySetResult();
        secondRelease.TrySetResult();
        await Task.WhenAll(oldTest, newTest);

        await sink.WaitForValueAsync(new TwoMotorRumble(32896, 32896), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Bridge_teardown_cancels_developer_delay_and_stale_authority_rejects_writes()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);
        var (delayEntered, releaseDelay) = HoldDeveloperDelay(bridge);
        var test = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None);
        await delayEntered;
        bridge.Dispose();
        releaseDelay();
        Assert.False((await test).Succeeded);
    }

    [Fact]
    public async Task Newer_real_Steam_feedback_during_the_developer_delay_is_not_stopped_by_the_stale_developer_STOP()
    {
        // Regression for PR #269 review: a developer EB/EA test's delayed zero write used to route
        // back through ProcessNormalizedReport (BeginFeedback() again), so if real Steam feedback
        // arrived during the 250ms window it became a NEWER developer STOP write and clobbered the
        // real feedback. The fix writes against the ORIGINAL developer sequence, so a newer arrival
        // makes the delayed STOP a silent no-op.
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        var (delayEntered, releaseDelay) = HoldDeveloperDelay(bridge);
        var developerTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), addDeveloperStop: true, CancellationToken.None);
        await delayEntered;
        Assert.True(bridge.ProcessNormalizedReport(Report(FrontendVibrationTestCommand.Haptic), "Steam"));
        releaseDelay();

        // The developer test's own return value reports its delayed STOP as a no-op (stale sequence),
        // not a failure of the original command -- exactly the intended behavior being verified here.
        Assert.False((await developerTest).Succeeded);
        await sink.WaitForValueAsync(new TwoMotorRumble(32896, 32896), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Cancel_developer_test_and_stop_cancels_the_pending_delay_and_writes_a_fresh_stop()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.DeveloperDelayOverride = (_, cancellationToken) =>
        {
            delayEntered.TrySetResult();
            return releaseDelay.Task.WaitAsync(cancellationToken);
        };
        var developerTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), addDeveloperStop: true, CancellationToken.None);
        await delayEntered.Task;

        bridge.CancelDeveloperTestAndStop();
        releaseDelay.TrySetResult();

        Assert.False((await developerTest).Succeeded);
        // Only the developer Rumble command and the CancelDeveloperTestAndStop() zero write --
        // the cancelled pending 250ms delayed STOP must never also fire.
        await sink.StopEntered.Task;
        Assert.True(sink.Contains(TwoMotorRumble.Stopped));
    }

    [Fact]
    public async Task Closing_developer_session_does_not_stop_newer_real_Steam_feedback()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);
        var (delayEntered, releaseDelay) = HoldDeveloperDelay(bridge);
        var developerTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None);
        await delayEntered;
        Assert.True(bridge.ProcessNormalizedReport(Report(FrontendVibrationTestCommand.Haptic), "Steam"));
        bridge.CancelDeveloperTestAndStop();
        releaseDelay();
        await developerTest;

        await sink.WaitForValueAsync(new TwoMotorRumble(32896, 32896), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_physical_write_failure_is_visible_in_the_outcome_even_though_authority_accepted_it()
    {
        // Regression for PR #269 review: TryWrite() used to discard the sink's PhysicalRumbleWriteResult
        // entirely, so a real MSI HID write failure was indistinguishable from success anywhere a
        // caller only looked at the accepted/rejected boolean. CommandResult must carry the real
        // physical status separately from Succeeded (which continues to mean authority/sequence
        // acceptance, unchanged).
        var sink = new FailingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        var outcome = await bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Haptic), addDeveloperStop: false, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.CommandResult);
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, outcome.CommandResult!.Value.Status);
        Assert.Equal("WriteFailed", outcome.CommandResult!.Value.Reason);
    }

    [Fact]
    public void Revoked_feedback_authority_rejects_developer_injection_without_a_write()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        authority.Revoke();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);

        Assert.False(bridge.ProcessNormalizedReport(Report(FrontendVibrationTestCommand.Rumble), "DeveloperVibrationTest"));
        Assert.Empty(sink.Values);
    }

    private static byte[] Report(FrontendVibrationTestCommand command) => command switch
    {
        FrontendVibrationTestCommand.Rumble => [0xEB, 9, 0, 0, 0, 0, 0x80, 0, 0x80, 0, 0],
        // Type 4 is the SDL Rumble generator command. Type 0 is the protocol Off command.
        FrontendVibrationTestCommand.Haptic => [0xEA, 0, 0, 4, 128, 0],
        _ => [0xEB]
    };

    private static (Task Entered, Action Release) HoldDeveloperDelay(SteamDeckRumbleFeedbackBridge bridge)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.DeveloperDelayOverride = (_, cancellationToken) =>
        {
            entered.TrySetResult();
            return release.Task.WaitAsync(cancellationToken);
        };
        return (entered.Task, () => release.TrySetResult());
    }

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        private readonly object _gate = new();
        public List<TwoMotorRumble> Values { get; } = [];
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            TaskCompletionSource changed;
            lock (_gate) { Values.Add(rumble); changed = _changed; _changed = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            changed.TrySetResult();
            if (rumble == TwoMotorRumble.Stopped) StopEntered.TrySetResult();
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }
        public async Task WaitForValueAsync(TwoMotorRumble expected, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (true)
            {
                TaskCompletionSource changed;
                lock (_gate)
                {
                    if (Values.Contains(expected)) return;
                    changed = _changed;
                }
                await changed.Task.WaitAsync(cancellation.Token);
            }
        }
        public bool Contains(TwoMotorRumble expected) { lock (_gate) return Values.Contains(expected); }
    }

    private sealed class FailingSink : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble) => new(PhysicalRumbleWriteStatus.Failed, "WriteFailed");
    }
}
