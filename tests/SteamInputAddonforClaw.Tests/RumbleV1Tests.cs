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
    public void Decoder_CoversNormalizedMinimumAndIndependentMotorBoundaries()
    {
        static byte[] Packet(ushort left, ushort right, byte size = 9)
            => [0xEB, size, 0, 0, 0, (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 2, 0];

        Assert.Equal(TwoMotorRumble.Stopped, SteamDeckRumbleDecoder.Decode(Packet(0, 0)).Rumble);
        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, ushort.MaxValue), SteamDeckRumbleDecoder.Decode(Packet(ushort.MaxValue, ushort.MaxValue)).Rumble);
        Assert.Equal(new TwoMotorRumble(0x1234, 0), SteamDeckRumbleDecoder.Decode(Packet(0x1234, 0)).Rumble);
        Assert.Equal(new TwoMotorRumble(0, 0x5678), SteamDeckRumbleDecoder.Decode(Packet(0, 0x5678)).Rumble);
        Assert.Equal(SteamDeckFeedbackCommand.Rumble, SteamDeckRumbleDecoder.Decode(Packet(1, 2)).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode(Packet(1, 2)[..10]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode(Packet(1, 2, 8)).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([]).Command);
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
    public void Authority_RevokeIsImmediateAndNonDraining()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        Assert.True(authority.TryAcquireLease(token, out var lease));
        using (lease!)
        {
            authority.Revoke();
            Assert.False(authority.IsCurrent(token));
            Assert.False(authority.TryAcquireLease(token, out _));
            var reacquired = authority.Acquire("SteamDeck");
            Assert.True(reacquired.Generation > token.Generation);
            Assert.False(authority.IsCurrent(token));
        }
    }

    [Fact]
    public async Task Authority_ConcurrentTransitionsNeverAcceptAnOldToken()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            authority.RevokeAndDrain();
            authority.Acquire("SteamDeck");
            return authority.IsCurrent(token);
        }));
        Assert.DoesNotContain(true, await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task Authority_RevokeAndDrainWaitsForAdmittedLeaseBeforeReturning()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        Assert.True(authority.TryAcquireLease(token, out var lease));
        using var admitted = lease!;
        var revoked = Task.Run(() => authority.RevokeAndDrain());

        Assert.False(revoked.IsCompleted);
        admitted.Dispose();
        await revoked;
        Assert.False(authority.TryAcquireLease(token, out _));
    }

    [Fact]
    public async Task Authority_DoesNotAllowAcquireToRepopulateAnActiveRevokeBoundary()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        Assert.True(authority.TryAcquireLease(token, out var lease));
        using var admitted = lease!;

        var revoke = Task.Run(() => authority.RevokeAndDrain());
        while (!authority.IsRevocationInProgress) await Task.Yield();

        var reacquire = Task.Run(() => authority.Acquire("SteamDeck"));
        await authority.AcquireBlocked;
        Assert.False(reacquire.IsCompleted);
        admitted.Dispose();
        await revoke;
        var newToken = await reacquire;
        Assert.True(newToken.Generation > token.Generation);
        Assert.True(authority.TryAcquireLease(newToken, out var newLease));
        newLease!.Dispose();
    }
}
