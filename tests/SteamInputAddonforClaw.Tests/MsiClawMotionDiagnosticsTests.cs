using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class MsiClawMotionDiagnosticsTests : IDisposable
{
    private const string MotionType = "E83AF229-8640-4D18-A213-E22675EBB2C3";
    private const string A2VmGyroDeviceId = @"\\?\ACPI#INTC0AC2#VID_8087&PID_0AC2#0";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MsiClawMotionDiagnosticsTests", Guid.NewGuid().ToString("N"));

    public MsiClawMotionDiagnosticsTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void Ten_second_summary_uses_monotonic_receive_gaps_without_an_initial_gap()
    {
        var diagnostics = new MsiClawMotionReaderDiagnostics();
        var timestamp = DateTimeOffset.UnixEpoch;
        diagnostics.StartWindow(1_000);
        diagnostics.ObserveRead(2, 1_000);
        diagnostics.ObserveRead(4, 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 1_000, timestamp, timestamp.AddMilliseconds(10), 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 1_030, timestamp.AddMilliseconds(25), timestamp.AddMilliseconds(35), 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 1_090, timestamp.AddMilliseconds(20), timestamp.AddMilliseconds(45), 1_000);

        Assert.True(diagnostics.IsSummaryDue(11_000, 1_000));
        var summary = diagnostics.CreateSummary(11_000, 1_000);

        Assert.Equal(10_000, summary.WindowMs);
        Assert.Equal(2, summary.ReadAttempts);
        Assert.Equal(3, summary.FreshAcceptedCount);
        Assert.Equal(0.3, summary.ActualFreshHz, 6);
        Assert.Equal(3, summary.ReadDurationMeanMs);
        Assert.Equal(4, summary.ReadDurationMaxMs);
        Assert.Equal(45, summary.FreshReceiveIntervalMeanMs);
        Assert.Equal(60, summary.FreshReceiveIntervalMaxMs);
        Assert.Equal(2, summary.GapOver20MsCount);
        Assert.Equal(1, summary.GapOver50MsCount);
        Assert.Equal(0, summary.GapOver100MsCount);
        Assert.Equal(25, summary.SensorTimestampIntervalMeanMs);
        Assert.Equal(25, summary.SensorTimestampIntervalMaxMs);
        Assert.Equal(1, summary.SensorTimestampNonMonotonicCount);
        Assert.Equal(15, summary.SensorAgeMeanMs);
        Assert.Equal(25, summary.SensorAgeMaxMs);
        Assert.Equal(3, summary.AxisSampleCount);
        Assert.Equal(0, summary.StdDevX);
    }

    [Fact]
    public void No_data_duplicates_missing_and_backwards_timestamps_are_classified_separately()
    {
        var diagnostics = new MsiClawMotionReaderDiagnostics();
        var timestamp = DateTimeOffset.UnixEpoch;
        diagnostics.StartWindow(0);
        diagnostics.ObserveRead(1, 1_000);
        diagnostics.ObserveNoData();
        diagnostics.ObserveRead(1, 1_000);
        diagnostics.ObserveDuplicateTimestamp();
        diagnostics.ObserveRead(1, 1_000);
        diagnostics.ObserveMissingSensorTimestamp();
        diagnostics.ObserveFresh(1, 2, 3, 100, null, null, 1_000);
        diagnostics.ObserveRead(1, 1_000);
        diagnostics.ObserveFresh(2, 4, 6, 110, timestamp.AddMilliseconds(10), timestamp, 1_000);
        diagnostics.ObserveRead(1, 1_000);
        diagnostics.ObserveFresh(3, 6, 9, 120, timestamp.AddMilliseconds(5), timestamp.AddMilliseconds(10), 1_000);

        var summary = diagnostics.CreateSummary(10_000, 1_000);

        Assert.Equal(3, summary.FreshAcceptedCount);
        Assert.Equal(1, summary.NoDataCount);
        Assert.Equal(1, summary.DuplicateTimestampCount);
        Assert.Equal(1, summary.MissingSensorTimestampCount);
        Assert.Equal(1, summary.SensorTimestampNonMonotonicCount);
        Assert.Null(summary.SensorTimestampIntervalMeanMs);
        Assert.Equal(1, summary.SensorAgeNegativeCount);
        Assert.Equal(1, summary.SensorAgeSampleCount);
        Assert.Equal(5, summary.SensorAgeMeanMs);
    }

    [Fact]
    public void Negative_sensor_age_does_not_change_normalization_or_monotonic_freshness()
    {
        var state = new MsiClawMotionState(
            0, 0, 0, 0.1, 0.9, -0.2,
            1_000, 1_000, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
            true, true, "gyro", "accel");

        var fresh = state.WithFreshness(1_000, 1_000);

        Assert.True(fresh.HasGyro);
        Assert.True(fresh.HasAccelerometer);
        Assert.True(fresh.IsUsableForSteamDeckImu);
        Assert.Equal((0.1, 0.9, -0.2), (fresh.AccelXG, fresh.AccelYG, fresh.AccelZG));
    }

    [Fact]
    public void Axis_statistics_use_population_standard_deviation_and_handle_stationary_vectors()
    {
        var gyro = new MsiClawMotionReaderDiagnostics();
        gyro.StartWindow(0);
        gyro.ObserveFresh(0.41, -0.29, -0.18, 10, null, null, 1_000);
        gyro.ObserveFresh(0.41, -0.29, -0.18, 20, null, null, 1_000);
        var stationary = gyro.CreateSummary(10_000, 1_000);

        Assert.Equal(0.41, stationary.MeanX);
        Assert.Equal(-0.29, stationary.MeanY);
        Assert.Equal(-0.18, stationary.MeanZ);
        Assert.Equal(0, stationary.StdDevX);
        Assert.Equal(0, stationary.StdDevY);
        Assert.Equal(0, stationary.StdDevZ);

        var accel = new MsiClawMotionReaderDiagnostics();
        accel.StartWindow(0);
        accel.ObserveFresh(0, 0, 1, 10, null, null, 1_000);
        accel.ObserveFresh(2, 0, 3, 20, null, null, 1_000);
        var varying = accel.CreateSummary(10_000, 1_000);

        Assert.Equal(1, varying.MeanX);
        Assert.Equal(1, varying.StdDevX);
        Assert.Equal(2, varying.MeanZ);
        Assert.Equal(1, varying.StdDevZ);
        Assert.Equal(2, varying.AxisSampleCount);
    }

    [Fact]
    public void A_blocked_read_duration_and_the_following_receive_gap_are_both_visible()
    {
        var diagnostics = new MsiClawMotionReaderDiagnostics();
        diagnostics.StartWindow(0);
        diagnostics.ObserveRead(125, 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 0, null, null, 1_000);
        diagnostics.ObserveRead(2, 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 125, null, null, 1_000);

        var summary = diagnostics.CreateSummary(10_000, 1_000);

        Assert.Equal(125, summary.ReadDurationMaxMs);
        Assert.Equal(125, summary.FreshReceiveIntervalMaxMs);
        Assert.Equal(1, summary.GapOver100MsCount);
    }

    [Fact]
    public void Reset_discards_partial_window_and_prior_timestamps_for_a_fresh_debug_epoch()
    {
        var diagnostics = new MsiClawMotionReaderDiagnostics();
        diagnostics.StartWindow(0);
        diagnostics.ObserveRead(2, 1_000);
        diagnostics.ObserveFresh(1, 2, 3, 100, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1_000);
        diagnostics.Reset();
        diagnostics.StartWindow(20_000);
        diagnostics.ObserveRead(4, 1_000);
        diagnostics.ObserveFresh(4, 5, 6, 20_100, DateTimeOffset.UnixEpoch.AddSeconds(2), DateTimeOffset.UnixEpoch.AddSeconds(2), 1_000);

        var summary = diagnostics.CreateSummary(30_000, 1_000);

        Assert.Equal(1, summary.ReadAttempts);
        Assert.Equal(1, summary.FreshAcceptedCount);
        Assert.Equal(4, summary.ReadDurationMeanMs);
        Assert.Null(summary.FreshReceiveIntervalMeanMs);
        Assert.Null(summary.SensorTimestampIntervalMeanMs);
        Assert.Equal(0, summary.SensorTimestampNonMonotonicCount);
        Assert.False(diagnostics.IsSummaryDue(29_999, 1_000));
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    [InlineData("msi.claw.cg3em")]
    public async Task Debug_source_configuration_logs_the_opened_backend_for_both_reader_roles(string modelId)
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        var (gyro, accel) = SensorsFor(modelId);
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, sensorTimestamp: DateTimeOffset.MaxValue),
            [accel.SensorId] = new(accel, sensorTimestamp: DateTimeOffset.MaxValue)
        };
        var clock = new FakeClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId(modelId),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("DiagnosticsTest"));
        await Task.WhenAll(handles.Values.Select(handle => handle.FirstReadObserved)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(await source.StopAsync("DiagnosticsTest"));
        var log = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);

        Assert.Equal(2, CountOccurrences(log, "Event=MotionReaderDiagnosticsStarted"));
        Assert.Contains($"Model={modelId}", log);
        Assert.Contains("Role=Gyroscope", log);
        Assert.Contains("Role=Accelerometer", log);
        Assert.Contains("NonFreshBackoffMs=1", log);
        Assert.Contains("FreshAcceptedExtraWaitMs=0", log);
        Assert.DoesNotContain("PollDelayMeaning=ExtraApplicationWaitAfterEachRead", log);
        if (modelId.StartsWith("msi.claw.a2vm.", StringComparison.Ordinal))
        {
            Assert.Contains("Role=Gyroscope Backend=WinRtGyrometer", log);
            Assert.Contains("WinRtMinimumReportIntervalMs=10", log);
            Assert.Contains("WinRtRequestedReportIntervalMs=10", log);
            Assert.Contains("WinRtEffectiveReportIntervalMs=10", log);
            Assert.Contains("Role=Accelerometer Backend=LegacySensorApi", log);
        }
        else
        {
            Assert.Contains("Role=Gyroscope Backend=LegacySensorApi", log);
            Assert.Contains("Role=Accelerometer Backend=LegacySensorApi", log);
            Assert.Contains("WinRtMinimumReportIntervalMs=null", log);
            Assert.Contains("WinRtRequestedReportIntervalMs=null", log);
            Assert.Contains("WinRtEffectiveReportIntervalMs=null", log);
        }
    }

    [Fact]
    public async Task Debug_summary_uses_the_same_outcome_dependent_wait_contract_as_start_record()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        using var releaseSecondRead = new ManualResetEventSlim();
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, releaseSecondRead: releaseSecondRead),
            [accel.SensorId] = new(accel, releaseSecondRead: releaseSecondRead)
        };
        var clock = new FakeClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("SummaryPolicyTest"));
        await Task.WhenAll(handles.Values.Select(handle => handle.SecondReadObserved)).WaitAsync(TimeSpan.FromSeconds(3));
        clock.Set(10_000);
        releaseSecondRead.Set();
        await Task.WhenAll(handles.Values.Select(handle => handle.ThirdReadObserved)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(await source.StopAsync("SummaryPolicyTest"));
        AppLog.DrainForTests();

        var log = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);
        var summaries = log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("Event=MotionReaderDiagnosticsSummary", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, summaries.Length);
        Assert.All(summaries, line =>
        {
            Assert.Contains("NonFreshBackoffMs=1", line);
            Assert.Contains("FreshAcceptedExtraWaitMs=0", line);
        });
    }

    [Fact]
    public async Task Info_level_keeps_existing_source_selection_and_failure_logs_without_debug_telemetry()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, failRead: true),
            [accel.SensorId] = new(accel, sensorTimestamp: DateTimeOffset.UnixEpoch)
        };
        var clock = new FakeClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("InfoDiagnosticsTest"));
        await Task.WhenAll(handles.Values.Select(handle => handle.FirstReadObserved)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(await source.StopAsync("InfoDiagnosticsTest"));
        var log = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);

        Assert.Contains("Event=MotionSourcesSelected", log);
        Assert.Contains("Event=MotionReaderFailed", log);
        Assert.DoesNotContain("MotionReaderDiagnosticsStarted", log);
        Assert.DoesNotContain("MotionReaderDiagnosticsSummary", log);
    }

    private static (ClawSensorProbeCandidate Gyro, ClawSensorProbeCandidate Accel) SensorsFor(string modelId)
    {
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}", "Min", "2");
        if (modelId.StartsWith("msi.claw.a2vm.", StringComparison.Ordinal))
        {
            var gyro = new ClawSensorProbeCandidate(
                "WinRT Gyrometer", A2VmGyroDeviceId, "Unavailable", "Unavailable",
                MinimumReportInterval: "10",
                Backend: ClawSensorProbeBackend.WinRtGyrometer,
                State: "Ready",
                DevicePath: A2VmGyroDeviceId,
                UnitBasis: ClawSensorProbeUnitBasis.DegreesPerSecond);
            return (gyro, accel);
        }

        return (Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}"), accel);
    }

    private static ClawSensorProbeCandidate Legacy(string role, string sensorId, string state = "Ready", string minimumInterval = "10") => new(
        role, sensorId, MotionType, "C317C286-C468-4288-9975-D4C4587C442C",
        Manufacturer: "ST_MICRO",
        Model: "LSM6DSO",
        MinimumReportInterval: minimumInterval,
        Backend: ClawSensorProbeBackend.LegacySensorApi,
        State: state,
        DevicePath: @"\\?\USB#VID_0483&PID_XXXX",
        IsDirectTypeMatch: true,
        SupportsX: true,
        SupportsY: true,
        SupportsZ: true);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private sealed class FakeClock
    {
        private long _timestamp;
        internal long Frequency => 1_000;
        internal long Read() => Interlocked.Read(ref _timestamp);
        internal void Set(long timestamp) => Interlocked.Exchange(ref _timestamp, timestamp);
    }

    private sealed class FakeSourceHandle(
        ClawSensorProbeCandidate candidate,
        bool failRead = false,
        DateTimeOffset? sensorTimestamp = null,
        ManualResetEventSlim? releaseSecondRead = null) : IClawSensorProbeSourceHandle
    {
        private int _readCount;
        private readonly TaskCompletionSource _firstReadObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondReadObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _thirdReadObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task FirstReadObserved => _firstReadObserved.Task;
        internal Task SecondReadObserved => _secondReadObserved.Task;
        internal Task ThirdReadObserved => _thirdReadObserved.Task;
        public ClawSensorProbeSourceConfiguration Configuration { get; } = candidate.Backend == ClawSensorProbeBackend.WinRtGyrometer
            ? new(ClawSensorProbeBackend.WinRtGyrometer, 10, 10, 10)
            : new(ClawSensorProbeBackend.LegacySensorApi, null, null, null);

        public ClawSensorReportReadResult Read()
        {
            var readCount = Interlocked.Increment(ref _readCount);
            _firstReadObserved.TrySetResult();
            if (failRead)
                throw new InvalidOperationException("Synthetic reader failure.");

            if (releaseSecondRead is not null && readCount >= 2)
            {
                if (readCount == 2)
                {
                    _secondReadObserved.TrySetResult();
                    releaseSecondRead.Wait();
                }
                else if (readCount == 3)
                {
                    _thirdReadObserved.TrySetResult();
                }

                return ClawSensorReportReadResult.NoData();
            }

            return ClawSensorReportReadResult.Data(0.25, -0.5, 0.75, sensorTimestamp ?? DateTimeOffset.UnixEpoch.AddMilliseconds(readCount));
        }

        public void Dispose() { }
    }
}
