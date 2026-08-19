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

        Assert.True(await bridge.ProcessDeveloperTestAsync(Report(command), addDeveloperStop: command is FrontendVibrationTestCommand.Rumble or FrontendVibrationTestCommand.Haptic, CancellationToken.None));
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

        Assert.True(await bridge.ProcessDeveloperTestAsync(Report(FrontendVibrationTestCommand.Rumble), true, CancellationToken.None));

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
        Assert.False(await test);
        Assert.Single(sink.Values);
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
}
