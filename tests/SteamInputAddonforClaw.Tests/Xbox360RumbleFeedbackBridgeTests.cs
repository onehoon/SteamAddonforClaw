using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Xbox360RumbleFeedbackBridgeTests
{
    [Fact]
    public async Task Non_zero_feedback_is_stopped_after_inactivity_window()
    {
        var sink = new RecordingSink();
        var (bridge, drive) = Arm(sink, TimeSpan.FromMilliseconds(120));
        using var _ = bridge;

        drive(255, 0);

        await sink.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(2));

        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, 0), sink.Writes[0]);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task Newer_non_zero_feedback_refreshes_the_inactivity_deadline()
    {
        var sink = new RecordingSink();
        var (bridge, drive) = Arm(sink, TimeSpan.FromMilliseconds(500));
        using var _ = bridge;

        drive(200, 0);
        await Task.Delay(100);
        drive(50, 0);

        await Task.Delay(350);

        Assert.Equal(2, sink.Writes.Count);
        Assert.DoesNotContain(TwoMotorRumble.Stopped, sink.Writes);

        await sink.WaitForWriteCountAsync(3, TimeSpan.FromSeconds(2));
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task Explicit_zero_feedback_stops_immediately_and_cancels_delayed_stop()
    {
        var sink = new RecordingSink();
        var (bridge, drive) = Arm(sink, TimeSpan.FromMilliseconds(250));
        using var _ = bridge;

        drive(255, 128);
        drive(0, 0);

        Assert.Equal(
            [new TwoMotorRumble(ushort.MaxValue, 32896), TwoMotorRumble.Stopped],
            sink.Writes);

        await Task.Delay(350);

        Assert.Equal(2, sink.Writes.Count);
    }

    [Fact]
    public async Task Dispose_cancels_a_pending_safety_stop()
    {
        var sink = new RecordingSink();
        var (bridge, drive) = Arm(sink, TimeSpan.FromMilliseconds(100));

        drive(255, 0);
        bridge.Dispose();

        await Task.Delay(250);

        Assert.Single(sink.Writes);
    }

    [Fact]
    public void Debug_correlation_logging_preserves_callback_translation_and_explicit_stop()
    {
        var previousDirectory = AppLog.DirectoryOverride;
        var previousLevel = AppLog.MinimumLevelOverride;
        var logDirectory = Path.Combine(Path.GetTempPath(), $"RumbleBridge-{Guid.NewGuid():N}");
        AppLog.DrainForTests();
        try
        {
            AppLog.DirectoryOverride = logDirectory;
            AppLog.MinimumLevelOverride = AppLogLevel.Debug;
            var sink = new RecordingSink();
            var (bridge, drive) = Arm(sink, TimeSpan.FromMinutes(1));
            using (bridge)
            {
                drive(3, 6);
                drive(0, 0);
            }

            var logPath = AppLog.CurrentLogFilePath;
            var content = AppLog.ReadAllTextForTests(logPath);

            Assert.Equal([new TwoMotorRumble(771, 1542), TwoMotorRumble.Stopped], sink.Writes);
            Assert.Contains("Event=ProductionXbox360RumbleRx", content);
            Assert.Contains("RumbleSeq=1 Left8=3 Right8=6 IsStop=False", content);
            Assert.Contains("RumbleSeq=2 Left8=0 Right8=0 IsStop=True", content);
            Assert.Contains("Event=ProductionXbox360RumblePhysicalWrite", content);
            Assert.Contains("Large8=3 Small8=6 Status=Succeeded Reason=OK", content);
        }
        finally
        {
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = previousDirectory;
            AppLog.MinimumLevelOverride = previousLevel;
            if (Directory.Exists(logDirectory))
                Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Debug_correlation_logging_records_the_source_of_the_safety_stop()
    {
        var previousDirectory = AppLog.DirectoryOverride;
        var previousLevel = AppLog.MinimumLevelOverride;
        var logDirectory = Path.Combine(Path.GetTempPath(), $"RumbleSafety-{Guid.NewGuid():N}");
        AppLog.DrainForTests();
        try
        {
            AppLog.DirectoryOverride = logDirectory;
            AppLog.MinimumLevelOverride = AppLogLevel.Debug;
            var sink = new RecordingSink();
            var (bridge, drive) = Arm(sink, TimeSpan.FromMilliseconds(40));
            using (bridge)
            {
                drive(3, 6);
                await sink.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(2));
            }

            var content = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);

            Assert.Equal([new TwoMotorRumble(771, 1542), TwoMotorRumble.Stopped], sink.Writes);
            Assert.Contains("Event=ProductionXbox360RumbleSafetyStop", content);
            Assert.Contains("SourceRumbleSeq=1", content);
            Assert.Contains("RumbleSeq=1 WriteKind=SafetyStop Large8=0 Small8=0 Status=Succeeded Reason=OK", content);
        }
        finally
        {
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = previousDirectory;
            AppLog.MinimumLevelOverride = previousLevel;
            if (Directory.Exists(logDirectory))
                Directory.Delete(logDirectory, recursive: true);
        }
    }

    private static (Xbox360RumbleFeedbackBridge Bridge, Action<byte, byte> Drive) Arm(
        RecordingSink sink, TimeSpan safetyStop)
    {
        Xbox360RumbleCallback? captured = null;
        var bridge = Xbox360RumbleFeedbackBridge.TryArm(
            sink,
            callback => { captured = callback; return true; },
            safetyStop);
        Assert.NotNull(bridge);
        Assert.NotNull(captured);

        void Drive(byte leftMotor, byte rightMotor) => captured!(0, leftMotor, rightMotor);

        return (bridge!, Drive);
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
                lock (_sync)
                {
                    if (Writes.Count >= count) return;
                }
                await Task.Delay(10);
            }

            lock (_sync) Assert.True(Writes.Count >= count, $"expected {count} writes, saw {Writes.Count}");
        }
    }
}
