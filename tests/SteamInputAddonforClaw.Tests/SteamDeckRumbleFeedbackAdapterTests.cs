using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR #488 review finding 1: the SteamDeck feedback adapter must preserve the physical-output
/// safety semantics that were local to the deleted routing-era bridge -- a haptic <c>CommandType == 0</c>
/// STOP and a bounded dead-man stop for non-zero output -- without any authority machinery.</summary>
[Collection("AppLog")]
public sealed class SteamDeckRumbleFeedbackAdapterTests : IDisposable
{
    private readonly AppLogLevel _previousMinimumLevel;
    private readonly string? _previousDirectory;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), nameof(SteamDeckRumbleFeedbackAdapterTests), Guid.NewGuid().ToString("N"));

    public SteamDeckRumbleFeedbackAdapterTests()
    {
        _previousMinimumLevel = AppLog.MinimumLevelOverride;
        _previousDirectory = AppLog.DirectoryOverride;
        AppLog.DrainForTests();
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.MinimumLevelOverride = _previousMinimumLevel;
        AppLog.DirectoryOverride = _previousDirectory;
        AppLog.DrainForTests();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

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

    [Fact]
    public void Haptic_pulse_is_logged_before_the_translation_filter_without_a_physical_write()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;
        byte[] report = [0x8F, 0x08, 0x01, 0x34, 0x12, 0x78, 0x56, 0x02, 0x03, 0xA5];

        drive(report);

        Assert.Empty(sink.Writes);
        var line = Assert.Single(ReceiveLines());
        Assert.Contains("Opcode=8F", line);
        Assert.Contains("RawHex=8F0801341278560203A5", line);
        Assert.Contains("DecodeResult=HapticPulse", line);
        Assert.Contains("HasPhysicalTranslation=False", line);
        Assert.Contains("Disposition=Ignored", line);
        Assert.Contains("Side=1", line);
        Assert.Contains("OnDurationMicroseconds=4660", line);
        Assert.Contains("OffIntervalMicroseconds=22136", line);
        Assert.Contains("Count=770", line);
        Assert.Contains("GainRaw=165", line);
        Assert.Contains("IsLinuxLayout=True", line);
    }

    [Fact]
    public void Receive_log_caps_raw_hex_to_the_first_64_copied_bytes()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;
        var report = Enumerable.Range(0, 65).Select(value => (byte)value).ToArray();

        drive(report);

        var line = Assert.Single(ReceiveLines());
        Assert.Contains("OriginalLength=65", line);
        Assert.Contains("CapturedLength=65", line);
        var rawHex = line.Split("RawHex=", StringSplitOptions.None)[1].Split(' ', StringSplitOptions.None)[0];
        Assert.Equal(Convert.ToHexString(report.AsSpan(0, 64)), rawHex);
        Assert.Contains("RawTruncated=True", line);
        Assert.Empty(sink.Writes);
    }

    [Fact]
    public void Unknown_0xDC_is_logged_as_raw_unknown_and_never_written()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;
        byte[] report = [0xDC, 0x09, 0x10, 0x20];

        drive(report);

        Assert.Empty(sink.Writes);
        var line = Assert.Single(ReceiveLines());
        Assert.Contains("Opcode=DC", line);
        Assert.Contains("RawHex=DC091020", line);
        Assert.Contains("DecodeResult=Unknown", line);
        Assert.Contains("Disposition=Ignored", line);
    }

    [Fact]
    public void Haptic_receive_and_sink_records_are_correlated_without_changing_stop_or_motor_output()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;

        drive(NonModernHaptic(commandType: 1, intensity: 50, gain: 0));
        drive(NonModernHaptic(commandType: 0, intensity: 200, gain: 0));

        Assert.Equal([new TwoMotorRumble(50 * 257, 50 * 257), TwoMotorRumble.Stopped], sink.Writes);
        var receive = ReceiveLines();
        var results = SinkResultLines();
        Assert.Equal(2, receive.Length);
        Assert.Equal(2, results.Length);
        Assert.Contains("RxSeq=1", receive[0]);
        Assert.Contains("Strength8=50", receive[0]);
        Assert.Contains("CommandType=1", receive[0]);
        Assert.Contains("Side=1", receive[0]);
        Assert.Contains("UiIntensity=50", receive[0]);
        Assert.Contains("DbGain=0", receive[0]);
        Assert.Contains("IsModernSdlLayout=False", receive[0]);
        Assert.Contains("RxSeq=1", results[0]);
        Assert.Contains("Large16=12850", results[0]);
        Assert.Contains("Small16=12850", results[0]);
        Assert.Contains("Status=Succeeded", results[0]);
        Assert.Contains("RxSeq=2", receive[1]);
        Assert.Contains("Disposition=Stop", receive[1]);
        Assert.Contains("RxSeq=2", results[1]);
        Assert.Contains("Stopped=True", results[1]);
    }

    [Fact]
    public async Task Selected_receive_is_logged_before_a_blocking_physical_sink_completes()
    {
        using var sinkEntered = new ManualResetEventSlim();
        using var releaseSink = new ManualResetEventSlim();
        var sink = new BlockingSink(sinkEntered, releaseSink);
        var (adapter, drive) = Arm(sink, rumbleSafetyStop: TimeSpan.FromSeconds(30));
        using var _ = adapter;
        var callback = Task.Run(() => drive(EbRumble(0x1234, 0x5678)));

        try
        {
            Assert.True(sinkEntered.Wait(TimeSpan.FromSeconds(2)), "The physical sink was not entered.");

            var inFlightLog = ReadLog();
            Assert.Single(inFlightLog.Split('\n'), line => line.Contains("Event=SteamDeckFeedbackRx", StringComparison.Ordinal));
            Assert.DoesNotContain("Event=SteamDeckFeedbackSinkResult", inFlightLog);

            releaseSink.Set();
            await callback.WaitAsync(TimeSpan.FromSeconds(2));

            var completedLog = ReadLog();
            Assert.Single(completedLog.Split('\n'), line => line.Contains("Event=SteamDeckFeedbackRx", StringComparison.Ordinal));
            Assert.Single(completedLog.Split('\n'), line => line.Contains("Event=SteamDeckFeedbackSinkResult", StringComparison.Ordinal));
        }
        finally
        {
            releaseSink.Set();
            await callback.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void Sink_failures_are_logged_even_when_the_same_eb_frame_is_rate_limited()
    {
        var sink = new RecordingSink { Result = new(PhysicalRumbleWriteStatus.Failed, "EndpointUnavailable") };
        var (adapter, drive) = Arm(sink, rumbleSafetyStop: TimeSpan.FromSeconds(30));
        using var _ = adapter;
        var report = EbRumble(0x1234, 0);

        drive(report);
        drive(report);

        Assert.Equal(2, sink.Writes.Count);
        var receive = ReceiveLines();
        var results = SinkResultLines();
        Assert.Equal(2, receive.Length);
        Assert.Equal(2, results.Length);
        Assert.Contains("RxSeq=1", receive[0]);
        Assert.Contains("RxSeq=1", results[0]);
        Assert.Contains("Status=Failed", results[0]);
        Assert.Contains("Reason=EndpointUnavailable", results[0]);
        Assert.Contains("RxSeq=2", receive[1]);
        Assert.Contains("RxSeq=2", results[1]);
        Assert.Contains("Status=Failed", results[1]);
    }

    [Fact]
    public async Task Rumble_diagnostics_are_bounded_but_zero_changes_and_sink_writes_are_preserved()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink, rumbleSafetyStop: TimeSpan.FromSeconds(30));
        using var _ = adapter;
        var report = EbRumble(0x1234, 0);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < 100; index++) drive(report);
        await Task.Delay(120);
        drive(report);
        drive(EbRumble(0, 0));
        elapsed.Stop();

        Assert.Equal(102, sink.Writes.Count);
        Assert.Equal(new TwoMotorRumble(0x1234, 0), sink.Writes[0]);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);

        var receive = ReceiveLines();
        var maximumExpectedSamples = 3 + (int)Math.Ceiling(elapsed.Elapsed.TotalMilliseconds / 100);
        Assert.InRange(receive.Length, 3, maximumExpectedSamples);
        Assert.Contains("Large16=4660", receive[0]);
        Assert.Contains("Small16=0", receive[0]);
        Assert.Contains("Opcode=EB", receive[^1]);
        Assert.Contains("Disposition=Stop", receive[^1]);
        Assert.Equal(receive.Length, SinkResultLines().Length);
        Assert.Contains($"RxSeq=102", SinkResultLines()[^1]);
    }

    [Fact]
    public void Other_opcodes_are_limited_independently_and_keep_sequence_gaps()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;

        for (var index = 0; index < 6; index++) drive([0x44, (byte)index]);
        drive([0x45, 0x01]);
        drive([0xEA, 0x13, 0x01, 0x01, 0x02, 0x03]); // malformed modern frame
        drive([0xB6, 0x01]);

        Assert.Empty(sink.Writes);
        var lines = ReceiveLines();
        Assert.Equal(7, lines.Length); // four 0x44 examples, then independent opcodes
        Assert.Equal(["1", "2", "3", "4", "7", "8", "9"], lines.Select(ReadRxSequence));
        Assert.Equal(4, lines.Count(line => line.Contains("Opcode=44", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.Contains("Opcode=45", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Opcode=EA", StringComparison.Ordinal) && line.Contains("DecodeResult=Malformed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Opcode=B6", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_callback_is_logged_with_no_opcode_and_without_dereferencing_data()
    {
        var sink = new RecordingSink();
        SteamDeckOutputCallback? captured = null;
        var adapter = SteamDeckRumbleFeedbackAdapter.TryArm(sink, callback => { captured = callback; return true; }, () => true);
        using var _ = Assert.IsType<SteamDeckRumbleFeedbackAdapter>(adapter);

        captured!(0, 0, 0);

        var line = Assert.Single(ReceiveLines());
        Assert.Contains("Opcode=None", line);
        Assert.Contains("OriginalLength=0", line);
        Assert.Contains("CapturedLength=0", line);
        Assert.Contains("DecodeResult=Malformed", line);
        Assert.Contains("Disposition=Ignored", line);
    }

    [Theory]
    [InlineData("Info")]
    [InlineData("Off")]
    public void Info_and_off_do_not_emit_debug_feedback_records_or_change_sink_behavior(string level)
    {
        AppLog.MinimumLevelOverride = Enum.Parse<AppLogLevel>(level);
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink);
        using var _ = adapter;

        drive([0x8F, 0x08, 0x01, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0xFF]);
        drive(EbRumble(0x1000, 0));

        Assert.Single(sink.Writes);
        Assert.DoesNotContain("SteamDeckFeedbackRx", ReadLog());
        Assert.DoesNotContain("SteamDeckFeedbackSinkResult", ReadLog());
    }

    [Fact]
    public async Task Safety_stop_log_uses_originating_receive_sequence_after_the_existing_stop_write()
    {
        var sink = new RecordingSink();
        var (adapter, drive) = Arm(sink, untimedHapticSafetyStop: TimeSpan.FromMilliseconds(40));
        using var _ = adapter;

        drive(NonModernHaptic(commandType: 1, intensity: 40, gain: 0));
        await sink.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(2));

        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
        var line = Assert.Single(ReadLog().Split('\n'), line => line.Contains("Event=SteamDeckFeedbackSafetyStop", StringComparison.Ordinal));
        Assert.Contains("RxSeq=1", line);
        Assert.Contains("Status=Succeeded", line);
        Assert.Contains("Reason=OK", line);
        Assert.Contains("Stopped=True", line);
    }

    private static (SteamDeckRumbleFeedbackAdapter Adapter, Action<byte[]> Drive) Arm(
        IPhysicalRumbleSink sink, TimeSpan? untimedHapticSafetyStop = null, TimeSpan? rumbleSafetyStop = null)
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

    private string ReadLog()
    {
        AppLog.DrainForTests();
        return File.Exists(AppLog.CurrentLogFilePath) ? File.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty;
    }

    private string[] ReceiveLines() => ReadLog().Split('\n').Where(line => line.Contains("Event=SteamDeckFeedbackRx", StringComparison.Ordinal)).ToArray();

    private string[] SinkResultLines() => ReadLog().Split('\n').Where(line => line.Contains("Event=SteamDeckFeedbackSinkResult", StringComparison.Ordinal)).ToArray();

    private static string ReadRxSequence(string line)
        => line.Split("RxSeq=", StringSplitOptions.None)[1].Split(' ', StringSplitOptions.None)[0];

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        private readonly object _sync = new();
        internal List<TwoMotorRumble> Writes { get; } = [];
        internal PhysicalRumbleWriteResult Result { get; init; } = new(PhysicalRumbleWriteStatus.Succeeded, "OK");

        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            lock (_sync) Writes.Add(rumble);
            return Result;
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

    private sealed class BlockingSink(ManualResetEventSlim entered, ManualResetEventSlim release) : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not release the blocked physical sink.");
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }
    }
}
