using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR #488 review finding 1: the SteamDeck feedback adapter must preserve the physical-output
/// safety semantics that were local to the deleted routing-era bridge -- a haptic <c>CommandType == 0</c>
/// STOP and a bounded dead-man stop for non-zero output -- without any authority machinery.</summary>
public sealed class SteamDeckRumbleFeedbackAdapterTests
{
    private static byte[] NonModernHaptic(byte commandType, byte intensity, byte gain)
        => [0xEA, 6, 1, commandType, intensity, gain];

    private static byte[] ModernTimedHaptic(short durationMs, byte intensity = 120)
    {
        var report = new byte[21];
        report[0] = 0xEA;
        report[1] = 19;             // modern SDL layout
        report[2] = 1;              // side
        report[3] = 1;              // commandType != 0 (not a stop)
        report[4] = intensity;
        report[5] = 0;              // gain
        BitConverter.GetBytes(durationMs).CopyTo(report, 8);
        return report;
    }

    private static byte[] EbRumble(ushort left, ushort right)
        => [0xEB, 9, 1, 0, 0, (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 0, 0];

    [Fact]
    public void Haptic_command_zero_is_translated_to_a_physical_stop()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;

        drive(NonModernHaptic(commandType: 0, intensity: 200, gain: 0));

        Assert.Equal([TwoMotorRumble.Stopped], sink.Writes);
    }

    [Fact]
    public async Task An_untimed_haptic_is_stopped_by_the_fallback_dead_man_window()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink, untimedHapticSafetyStop: TimeSpan.FromMilliseconds(40));
        using var _ = adapter;

        drive(NonModernHaptic(commandType: 1, intensity: 200, gain: 0));
        Assert.NotEqual(TwoMotorRumble.Stopped, sink.Writes[0]);

        await sink.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(2));
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task A_timed_haptic_is_stopped_after_its_declared_duration()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;

        drive(ModernTimedHaptic(durationMs: 40));
        Assert.NotEqual(TwoMotorRumble.Stopped, sink.Writes[0]);

        await sink.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(2));
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task Newer_feedback_supersedes_the_older_delayed_stop()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink,
            untimedHapticSafetyStop: TimeSpan.FromMilliseconds(60),
            rumbleSafetyStop: TimeSpan.FromSeconds(30));
        using var _ = adapter;

        drive(NonModernHaptic(commandType: 1, intensity: 200, gain: 0)); // would stop in 60 ms
        drive(EbRumble(0x4000, 0x2000));                                 // supersedes; its own stop is 30 s away

        await Task.Delay(250);

        Assert.Equal(2, sink.Writes.Count); // the superseded 60 ms stop never fired
        Assert.DoesNotContain(TwoMotorRumble.Stopped, sink.Writes);
    }

    [Fact]
    public async Task Dispose_cancels_a_pending_safety_stop()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink, untimedHapticSafetyStop: TimeSpan.FromMilliseconds(50));

        drive(NonModernHaptic(commandType: 1, intensity: 200, gain: 0));
        adapter.Dispose();

        await Task.Delay(200);
        Assert.Single(sink.Writes); // only the original non-zero write; no late stop
    }

    private static (SteamDeckRumbleFeedbackAdapter Adapter, Action<byte[]> Drive) Arm(
        RecordingSink sink, TimeSpan? untimedHapticSafetyStop = null, TimeSpan? rumbleSafetyStop = null)
    {
        SteamDeckOutputCallback? captured = null;
        var adapter = SteamDeckRumbleFeedbackAdapter.TryArm(
            sink,
            cb => { captured = cb; return true; },
            () => true,
            untimedHapticSafetyStop,
            rumbleSafetyStop);
        Assert.NotNull(adapter);
        Assert.NotNull(captured);

        void Drive(byte[] report)
        {
            var handle = GCHandle.Alloc(report, GCHandleType.Pinned);
            try { captured!(0, handle.AddrOfPinnedObject(), (uint)report.Length); }
            finally { handle.Free(); }
        }

        return (adapter!, Drive);
    }

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        private readonly object _sync = new();
        internal List<TwoMotorRumble> Writes { get; } = [];

        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            lock (_sync) Writes.Add(rumble);
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }

        internal async Task WaitForWriteCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_sync) { if (Writes.Count >= count) return; }
                await Task.Delay(10);
            }
            lock (_sync) Assert.True(Writes.Count >= count, $"expected {count} writes, saw {Writes.Count}");
        }
    }
}
