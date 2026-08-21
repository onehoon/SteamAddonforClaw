using SteamInputAddonforClaw.Feedback;
using System.Runtime.InteropServices;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RumbleV1Tests
{
    [Fact]
    public void SteamDeckFeedbackBridge_captures_immutable_token_and_rejects_late_callback()
    {
        var authority = new FeedbackAuthority();
        var first = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var oldBridge = new SteamDeckRumbleFeedbackBridge(authority, first, sink);
        authority.RevokeAndDrain();
        var second = authority.Acquire("SteamDeck");
        var newBridge = new SteamDeckRumbleFeedbackBridge(authority, second, sink);
        Invoke(oldBridge.Callback, Packet(0x1234, 0x5678));
        Invoke(newBridge.Callback, Packet(0x1234, 0x5678));
        Assert.True(sink.WaitForValue(new TwoMotorRumble(0x1234, 0x5678), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void SteamDeckFeedbackBridge_contains_invalid_native_callback_inputs_and_sink_exceptions()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink { ThrowOnWrite = true };
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        bridge.Callback(0, 0, 4);
        var oversized = Marshal.AllocHGlobal(65);
        try { bridge.Callback(0, oversized, 65); }
        finally { Marshal.FreeHGlobal(oversized); }
        Invoke(bridge.Callback, Packet(1, 2));
        Assert.Empty(sink.Values);
    }

    [Fact]
    public async Task SteamDeckFeedbackBridge_paused_before_lease_is_rejected_after_revoke()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        var beforeLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim(false);
        bridge.BeforeLease = () => { beforeLease.TrySetResult(); release.Wait(); };
        var callback = Task.Run(() => Invoke(bridge.Callback, Packet(1, 2)));
        await beforeLease.Task;
        authority.RevokeAndDrain();
        release.Set();
        await callback;
        Assert.Empty(sink.Values);
    }

    [Fact]
    public async Task SteamDeckFeedbackBridge_admitted_write_drains_before_revoke_returns()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new BlockingRecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        var callback = Task.Run(() => Invoke(bridge.Callback, Packet(1, 2)));
        await sink.Entered.Task;
        var revoke = Task.Run(authority.RevokeAndDrain);
        Assert.False(revoke.IsCompleted);
        sink.Release.Set();
        await callback;
        await revoke;
        Assert.Equal([new TwoMotorRumble(1, 2)], sink.Values);
    }

    private static void Invoke(SteamInputAddonforClaw.VirtualOutput.Viiper.SteamDeckOutputCallback callback, byte[] report)
    {
        var pointer = Marshal.AllocHGlobal(report.Length);
        try { Marshal.Copy(report, 0, pointer, report.Length); callback(0, pointer, (uint)report.Length); }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static byte[] Packet(ushort left, ushort right) =>
        [0xEB, 9, 0, 0, 0, (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 2, 0];

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        private readonly object _gate = new();
        public List<TwoMotorRumble> Values { get; } = [];
        public bool ThrowOnWrite { get; set; }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        { lock (_gate) { if (ThrowOnWrite) throw new InvalidOperationException(); Values.Add(rumble); Monitor.PulseAll(_gate); } return new(PhysicalRumbleWriteStatus.Succeeded, "OK"); }
        public bool WaitForValue(TwoMotorRumble expected, TimeSpan timeout)
        {
            lock (_gate)
            {
                var deadline = DateTime.UtcNow + timeout;
                while (!Values.Contains(expected) && DateTime.UtcNow < deadline)
                    Monitor.Wait(_gate, TimeSpan.FromMilliseconds(10));
                return Values.Contains(expected);
            }
        }
    }

    private sealed class BlockingRecordingSink : IPhysicalRumbleSink
    {
        public List<TwoMotorRumble> Values { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        { Entered.TrySetResult(); Release.Wait(); Values.Add(rumble); return new(PhysicalRumbleWriteStatus.Succeeded, "OK"); }
    }
    [Fact]
    public void TwoMotorRumble_PreservesIndependentFullPrecisionChannels()
    {
        Assert.Equal(TwoMotorRumble.Stopped, new TwoMotorRumble(0, 0));
        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, 1), new TwoMotorRumble(ushort.MaxValue, 1));
    }

    [Fact]
    public void Decoder_MapsValidatedDeckRumbleFieldsWithoutReduction()
    {
        var report = new byte[] { 0xEB, 9, 0x04, 0x78, 0x56, 0x34, 0x12, 0xCD, 0xAB, 0xFE, 0x7F };
        var result = SteamDeckRumbleDecoder.Decode(report);
        Assert.True(result.HasPhysicalTranslation);
        Assert.Equal((byte)0x04, result.RumbleType);
        Assert.Equal((ushort)0x5678, result.RumbleIntensity);
        Assert.Equal(new TwoMotorRumble(0x1234, 0xABCD), result.Rumble);
        Assert.Equal((sbyte)-2, result.RumbleLeftGain);
        Assert.Equal((sbyte)127, result.RumbleRightGain);
    }

    [Fact]
    public void Bridge_IgnoresRumbleMetadataForPhysicalMapping()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);

        Assert.True(bridge.ProcessNormalizedReport([0xEB, 9, 0x04, 0x78, 0x56, 0x34, 0x12, 0xCD, 0xAB, 0x80, 0x7F]));

        Assert.True(sink.WaitForValue(new TwoMotorRumble(0x1234, 0xABCD), TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(0x80, -128)]
    [InlineData(0x7F, 127)]
    public void Decoder_PreservesSignedRumbleGainBoundaries(byte encodedGain, sbyte expectedGain)
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEB, 9, 0, 0, 0, 0, 0, 0, 0, encodedGain, encodedGain]);

        Assert.Equal(expectedGain, result.RumbleLeftGain);
        Assert.Equal(expectedGain, result.RumbleRightGain);
    }

    [Fact]
    public void Decoder_RejectsMalformedAndClassifiesUnsupportedCommands()
    {
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEB, 9]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEA]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0x8F]).Command);
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
    public void Decoder_PreservesModernSdlHapticMetadataAndExistingClawFallback()
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEA, 0x13, 0x03, 0x07, 0x04, 0xFE, 0x34, 0x12, 0xFC, 0xFF, 0x78, 0x56, 0xBC, 0x9A, 0x64, 0x7F, 0x06, 0x22, 0x11, 0x44, 0x33]);

        Assert.Equal(SteamDeckFeedbackCommand.Haptic, result.Command);
        Assert.Equal(new SteamDeckHapticMetadata(19, 3, 7, 4, -2, true, 0x1234, -4, 0x5678, unchecked((ushort)0x9ABC), 100, 127, 6, 0x1122, 0x3344), result.Haptic);
        Assert.Equal((byte)4, result.Intensity);
        Assert.Equal(-2, result.Gain);
        Assert.Equal((byte)0, result.Strength8);
        Assert.Equal(TwoMotorRumble.Stopped, result.Rumble);
    }

    [Fact]
    public void Decoder_RejectsTruncatedModernSdlHaptic()
    {
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEA, 19, 1, 2, 3, 4]).Command);
    }

    [Fact]
    public void Decoder_PreservesHistoricalHapticPrefixWithoutPretendingModernLayout()
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEA, 0x0D, 2, 4, 100, 0]);

        Assert.Equal(SteamDeckFeedbackCommand.Haptic, result.Command);
        Assert.Equal((byte)0x0D, result.Haptic!.Value.DeclaredPayloadLength);
        Assert.Equal((byte)2, result.Haptic.Value.Side);
        Assert.Equal((byte)4, result.Haptic.Value.CommandType);
        Assert.False(result.Haptic.Value.IsModernSdlLayout);
        Assert.Null(result.Haptic.Value.Frequency);
        Assert.Equal(new TwoMotorRumble(100 * 257, 100 * 257), result.Rumble);
    }

    [Theory]
    [InlineData(0x80, -128)]
    [InlineData(0x7F, 127)]
    public void Decoder_PreservesModernHapticSignedGainBoundaries(byte encodedGain, sbyte expectedGain)
    {
        var report = new byte[21];
        report[0] = 0xEA;
        report[1] = 19;
        report[5] = encodedGain;
        var result = SteamDeckRumbleDecoder.Decode(report);

        Assert.Equal(expectedGain, result.Haptic!.Value.DbGain);
    }

    [Fact]
    public void Decoder_HapticMetadataDoesNotChangeExistingClawFallback()
    {
        var first = SteamDeckRumbleDecoder.Decode([0xEA, 19, 1, 1, 100, 0, 0x34, 0x12, 0x02, 0, 0x78, 0x56, 0xBC, 0x9A, 100, 1, 2, 0x22, 0x11, 0x44, 0x33]);
        var second = SteamDeckRumbleDecoder.Decode([0xEA, 19, 3, 5, 100, 0, 0xFF, 0xFF, 0xFC, 0xFF, 0xAA, 0xBB, 0xCC, 0xDD, 1, 127, 6, 0x01, 0, 0x02, 0]);

        Assert.Equal(new TwoMotorRumble(100 * 257, 100 * 257), first.Rumble);
        Assert.Equal(first.Rumble, second.Rumble);
    }

    [Fact]
    public void Decoder_PreservesLinuxHapticPulseMetadataWithoutPhysicalTranslation()
    {
        var result = SteamDeckRumbleDecoder.Decode([0x8F, 8, 2, 0x34, 0x12, 0x78, 0x56, 3, 0, 0xA0]);

        Assert.Equal(SteamDeckFeedbackCommand.HapticPulse, result.Command);
        Assert.Equal(new SteamDeckHapticPulseMetadata(8, 2, 0x1234, 0x5678, 3, 0xA0, true), result.HapticPulse);
        Assert.Equal(TwoMotorRumble.Stopped, result.Rumble);
        Assert.False(result.HasPhysicalTranslation);
    }

    [Fact]
    public void Bridge_IgnoresHapticPulseWithoutSupersedingSupportedFeedback()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);

        Assert.True(bridge.ProcessNormalizedReport(Packet(9, 10), "Steam"));
        Assert.False(bridge.ProcessNormalizedReport([0x8F, 8, 2, 0x34, 0x12, 0x78, 0x56, 3, 0, 0xA0], "Steam", out var sequence));
        Assert.Equal(0, sequence);
        Assert.True(sink.WaitForValue(new TwoMotorRumble(9, 10), TimeSpan.FromSeconds(2)));
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
