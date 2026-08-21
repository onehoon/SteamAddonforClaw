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
        Assert.Single(sink.Values);
        Assert.Equal(new TwoMotorRumble(0x1234, 0x5678), sink.Values[0]);
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
        public List<TwoMotorRumble> Values { get; } = [];
        public bool ThrowOnWrite { get; set; }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        { if (ThrowOnWrite) throw new InvalidOperationException(); Values.Add(rumble); return new(PhysicalRumbleWriteStatus.Succeeded, "OK"); }
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
        Assert.True(result.IsSupported);
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

        Assert.Equal([new TwoMotorRumble(0x1234, 0xABCD)], sink.Values);
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

    [Theory]
    [InlineData(100, 2, 116)]
    [InlineData(100, -20, 0)]
    [InlineData(250, 20, 255)]
    public void Decoder_MapsHapticCommandWithHhcStrength(byte intensity, sbyte gain, byte strength)
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEA, 0, 0, 0, intensity, unchecked((byte)gain)]);
        Assert.Equal(SteamDeckFeedbackCommand.Haptic, result.Command);
        Assert.Equal(new TwoMotorRumble((ushort)(strength * 257), (ushort)(strength * 257)), result.Rumble);
        Assert.Equal(strength, result.Strength8);
    }

    [Fact]
    public void Decoder_MapsHapticPulseFieldsStrengthAndDuration()
    {
        var result = SteamDeckRumbleDecoder.Decode([0x8F, 0, 0, 0, 0, 250, 0, 3, 0, 4]);
        Assert.Equal(SteamDeckFeedbackCommand.HapticPulse, result.Command);
        Assert.Equal((ushort)250, result.PulsePeriod!.Value);
        Assert.Equal((ushort)3, result.PulseCount!.Value);
        Assert.Equal((byte)52, result.Strength8!.Value);
        Assert.Equal(1, result.PulseDurationMilliseconds!.Value);
        Assert.Equal(new TwoMotorRumble((ushort)(52 * 257), (ushort)(52 * 257)), result.Rumble);
    }

    [Fact]
    public void Decoder_HapticPulseDurationUsesWidenedMultiplication()
    {
        var result = SteamDeckRumbleDecoder.Decode([0x8F, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0]);
        Assert.Equal((int)Math.Ceiling(65535L * 65535 / 1000.0), result.PulseDurationMilliseconds!.Value);
    }

    [Fact]
    public async Task Bridge_PulseStopIsStaleSafeAndTeardownCancelsIt()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        Invoke(bridge.Callback, [0x8F, 0, 0, 0, 0, 10, 0, 10, 0, 0]);
        Invoke(bridge.Callback, Packet(7, 8));
        await Task.Delay(30);
        Assert.Equal([new TwoMotorRumble(41120, 41120), new TwoMotorRumble(7, 8)], sink.Values);
        bridge.Dispose();
        await Task.Delay(30);
        Assert.Equal(2, sink.Values.Count);
    }

    [Theory]
    [InlineData(0xEA)]
    [InlineData(0xEB)]
    public async Task Bridge_PulseStopCannotCancelNewerSupportedFeedback(byte newerOpcode)
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        Invoke(bridge.Callback, [0x8F, 0, 0, 0, 0, 10, 0, 10, 0, 0]);
        Invoke(bridge.Callback, newerOpcode == 0xEA ? [0xEA, 0, 0, 0, 1, 0] : Packet(9, 10));
        await Task.Delay(30);
        Assert.Equal(2, sink.Values.Count);
        Assert.Equal(newerOpcode == 0xEA ? new TwoMotorRumble(257, 257) : new TwoMotorRumble(9, 10), sink.Values[1]);
    }

    [Fact]
    public async Task Bridge_DelayedStopHoldsBridgeGateAcrossPhysicalWrite()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new BlockingStopSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        Invoke(bridge.Callback, [0x8F, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
        await sink.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newer = Task.Run(() => Invoke(bridge.Callback, Packet(9, 10)));
        await Task.Delay(20);
        Assert.False(newer.IsCompleted);
        sink.ReleaseStop.Set();
        await newer;
        Assert.Equal(TwoMotorRumble.Stopped, sink.Values[1]);
        Assert.Equal(new TwoMotorRumble(9, 10), sink.Values[^1]);
        Assert.DoesNotContain(TwoMotorRumble.Stopped, sink.Values.Skip(2));
    }

    [Fact]
    public async Task Bridge_ArmsOneMillisecondStopOnlyAfterImmediateWrite()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim(false);
        bridge.BeforeLease = () => { entered.TrySetResult(); release.Wait(); };

        var callback = Task.Run(() => Invoke(bridge.Callback, [0x8F, 0, 0, 0, 0, 0, 0, 1, 0, 0]));
        await entered.Task;
        await Task.Delay(20);
        Assert.Empty(sink.Values);
        release.Set();
        await callback;
        await Task.Delay(20);
        Assert.Equal([new TwoMotorRumble(4112, 4112), TwoMotorRumble.Stopped], sink.Values);
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

    private sealed class BlockingStopSink : IPhysicalRumbleSink
    {
        public List<TwoMotorRumble> Values { get; } = [];
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseStop { get; } = new(false);

        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            if (rumble == TwoMotorRumble.Stopped)
            {
                StopEntered.TrySetResult();
                ReleaseStop.Wait();
            }
            Values.Add(rumble);
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }
    }
}
