using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RumbleV1Tests
{
    [Fact]
    public void TwoMotorRumble_PreservesIndependentFullPrecisionChannels()
    {
        Assert.Equal(TwoMotorRumble.Stopped, new TwoMotorRumble(0, 0));
        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, 1), new TwoMotorRumble(ushort.MaxValue, 1));
    }

    [Fact]
    public void Decoder_MapsValidatedDeckRumbleFieldsWithoutReduction()
    {
        var report = new byte[] { 0xEB, 9, 0, 0, 0, 0x34, 0x12, 0xCD, 0xAB, 2, 0 };
        var result = SteamDeckRumbleDecoder.Decode(report);
        Assert.True(result.IsSupported);
        Assert.Equal(new TwoMotorRumble(0x1234, 0xABCD), result.Rumble);
    }

    [Fact]
    public void Decoder_RejectsMalformedAndClassifiesUnsupportedCommands()
    {
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEB, 9]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xEA]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0x8F]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB6]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB7]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB8]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB9]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unknown, SteamDeckRumbleDecoder.Decode([0xCA]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unknown, SteamDeckRumbleDecoder.Decode([0x99]).Command);
    }

    [Fact]
    public void Authority_RejectsStaleAndWrongSourceTokensAcrossRevokeAndReacquire()
    {
        var authority = new FeedbackAuthority();
        var first = authority.Acquire("SteamDeck");
        Assert.True(authority.IsCurrent(first));
        Assert.False(authority.IsCurrent(new FeedbackAuthorityToken(first.Generation, "Xbox360")));
        authority.Revoke();
        Assert.False(authority.IsCurrent(first));
        var second = authority.Acquire("SteamDeck");
        Assert.True(second.Generation > first.Generation);
        Assert.False(authority.IsCurrent(first));
        Assert.True(authority.IsCurrent(second));
    }

    [Fact]
    public async Task Authority_ConcurrentTransitionsNeverAcceptAnOldToken()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            authority.Revoke();
            authority.Acquire("SteamDeck");
            return authority.IsCurrent(token);
        }));
        Assert.DoesNotContain(true, await Task.WhenAll(tasks));
    }
}
