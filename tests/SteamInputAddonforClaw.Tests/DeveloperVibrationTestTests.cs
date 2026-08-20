using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DeveloperVibrationTestTests
{
    [Theory]
    [InlineData(FrontendVibrationTestCommand.Rumble, 32768, 32768)]
    [InlineData(FrontendVibrationTestCommand.Haptic, 32896, 32896)]
    [InlineData(FrontendVibrationTestCommand.HapticPulse, 41120, 41120)]
    public async Task Developer_command_uses_the_production_decoder_and_sink(FrontendVibrationTestCommand command, ushort large, ushort small)
    {
        var authority = new FeedbackAuthority();
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        Assert.True((await bridge.ProcessDeveloperTestAsync(Report(command), addDeveloperStop: command is FrontendVibrationTestCommand.Rumble or FrontendVibrationTestCommand.Haptic, CancellationToken.None)).Succeeded);
        Assert.Equal(new TwoMotorRumble(large, small), sink.Values[0]);
        if (command == FrontendVibrationTestCommand.HapticPulse)
        {
            await Task.Delay(300);
            Assert.Equal(2, sink.Values.Count);
        }
    }

    [Fact]
    public async Task EB_test_sends_a_production_path_zero_after_250ms()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        Assert.True((await bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None)).Succeeded);

        Assert.Equal([new TwoMotorRumble(32768, 32768), TwoMotorRumble.Stopped], sink.Values);
    }

    [Fact]
    public async Task New_developer_test_cancels_the_previous_delayed_stop()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        var oldTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None);
        await Task.Delay(20);
        var newTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Haptic), true, CancellationToken.None);
        await Task.WhenAll(oldTest, newTest);

        Assert.Equal([new TwoMotorRumble(32768, 32768), new TwoMotorRumble(32896, 32896), TwoMotorRumble.Stopped], sink.Values);
    }

    [Fact]
    public async Task Bridge_teardown_cancels_developer_delay_and_stale_authority_rejects_writes()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);
        var test = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None);

        await Task.Delay(20);
        bridge.Dispose();
        Assert.False((await test).Succeeded);
        Assert.Single(sink.Values);
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

        var developerTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), addDeveloperStop: true, CancellationToken.None);
        await Task.Delay(20);
        Assert.True(bridge.ProcessNormalizedReport(Report(FrontendVibrationTestCommand.Haptic), "Steam"));

        // The developer test's own return value reports its delayed STOP as a no-op (stale sequence),
        // not a failure of the original command -- exactly the intended behavior being verified here.
        Assert.False((await developerTest).Succeeded);
        await Task.Delay(300);

        // Two writes only: the developer Rumble command, then the newer real Steam Haptic command.
        // The developer test's delayed STOP must NOT appear as a third write.
        Assert.Equal([new TwoMotorRumble(32768, 32768), new TwoMotorRumble(32896, 32896)], sink.Values);
    }

    [Fact]
    public async Task Cancel_developer_test_and_stop_cancels_the_pending_delay_and_writes_a_fresh_stop()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);
        var developerTest = bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), addDeveloperStop: true, CancellationToken.None);
        await Task.Delay(20);

        bridge.CancelDeveloperTestAndStop();

        Assert.False((await developerTest).Succeeded);
        await Task.Delay(300);
        // Only the developer Rumble command and the CancelDeveloperTestAndStop() zero write --
        // the cancelled pending 250ms delayed STOP must never also fire.
        Assert.Equal([new TwoMotorRumble(32768, 32768), TwoMotorRumble.Stopped], sink.Values);
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
    public async Task Haptic_pulse_waits_for_the_production_stop_and_reports_its_physical_result()
    {
        var sink = new FailingSink();
        var authority = new FeedbackAuthority();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, authority.Acquire("SteamDeck"), sink);

        var outcome = await bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.HapticPulse), addDeveloperStop: false, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, outcome.CommandResult!.Value.Status);
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, outcome.StopResult!.Value.Status);
        Assert.Equal("WriteFailed", outcome.StopResult!.Value.Reason);
    }

    [Fact]
    public void Revoked_feedback_authority_rejects_developer_injection_without_a_write()
    {
        var sink = new RecordingSink();
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        authority.RevokeAndDrain();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);

        Assert.False(bridge.ProcessNormalizedReport(Report(FrontendVibrationTestCommand.Rumble), "DeveloperVibrationTest"));
        Assert.Empty(sink.Values);
    }

    private static byte[] Report(FrontendVibrationTestCommand command) => command switch
    {
        FrontendVibrationTestCommand.Rumble => [0xEB, 9, 0, 0, 0, 0, 0x80, 0, 0x80, 0, 0],
        FrontendVibrationTestCommand.Haptic => [0xEA, 0, 0, 0, 128, 0],
        FrontendVibrationTestCommand.HapticPulse => [0x8F, 0, 0, 0, 0, 0xA8, 0x61, 10, 0, 0],
        _ => [0xEB]
    };

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        public List<TwoMotorRumble> Values { get; } = [];
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        { Values.Add(rumble); return new(PhysicalRumbleWriteStatus.Succeeded, "OK"); }
    }

    private sealed class FailingSink : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble) => new(PhysicalRumbleWriteStatus.Failed, "WriteFailed");
    }
}
