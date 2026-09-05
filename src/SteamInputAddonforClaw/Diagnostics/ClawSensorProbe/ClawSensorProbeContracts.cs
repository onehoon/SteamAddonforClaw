using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal enum ClawSensorProbePhase { REST, ROLL_LEFT, ROLL_RIGHT, PITCH_UP, PITCH_DOWN, YAW_LEFT, YAW_RIGHT }
internal enum ClawSensorProbeState { Idle, Discovering, Ready, Starting, Countdown, RecordingPhase, Stopping, Completed, Failed }
internal enum ClawSensorCaptureMode { Inactive, Transition, Recording }
internal enum ClawSensorProbeBackend { LegacySensorApi, WinRtGyrometer, WinRtAccelerometer }
internal enum ClawSensorProbeUnitBasis { Unknown, DegreesPerSecond, G }
internal enum ClawSensorReadOutcome { Fresh, Duplicate, NoData, Failure }

internal sealed class ClawSensorProbeSessionClock
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public long ElapsedTicks => _clock.ElapsedTicks;
    public double ElapsedMs => TicksToMilliseconds(ElapsedTicks);
    public static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}

internal sealed record ClawSensorProbeCandidate(
    string FriendlyName,
    string SensorId,
    string TypeGuid,
    string CategoryGuid,
    string Manufacturer = "Unavailable",
    string Model = "Unavailable",
    string PersistentUniqueId = "Unavailable",
    string MinimumReportInterval = "Unavailable",
    string CustomUsage = "Unavailable",
    ClawSensorProbeBackend Backend = ClawSensorProbeBackend.LegacySensorApi,
    string State = "Unavailable",
    string DevicePath = "Unavailable",
    ClawSensorProbeUnitBasis UnitBasis = ClawSensorProbeUnitBasis.Unknown,
    bool IsDirectTypeMatch = false,
    string? SelectionReason = null);

internal sealed record ClawSensorDiscovery(IReadOnlyList<ClawSensorProbeCandidate> Sensors, ClawSensorProbeCandidate? Gyroscope, ClawSensorProbeCandidate? Accelerometer, IReadOnlyList<string> Errors)
{
    public bool IsValid => Gyroscope is not null && Accelerometer is not null && Errors.Count == 0;

    // Selection is diagnostic, model-bounded, and conservative (see docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 5.4):
    // gyro prefers a unique WinRT Gyrometer candidate before falling back to the unique legacy "Physical Gyrometer" match;
    // accel prefers a unique WinRT Accelerometer candidate, then a unique legacy candidate validated through a direct
    // GetSensorsByType lookup, then finally the unique broad-enumeration "Physical Accelerometer" match. Ambiguous
    // candidates at any preferred tier fail closed rather than falling through to a weaker tier.
    public static ClawSensorDiscovery Select(IReadOnlyList<ClawSensorProbeCandidate> sensors)
    {
        var errors = new List<string>();
        var gyroscope = SelectGyroscope(sensors, errors);
        var accelerometer = SelectAccelerometer(sensors, errors);
        return new(sensors, gyroscope, accelerometer, errors);
    }

    private static ClawSensorProbeCandidate? SelectGyroscope(IReadOnlyList<ClawSensorProbeCandidate> sensors, List<string> errors)
    {
        var winRt = sensors.Where(x => x.Backend == ClawSensorProbeBackend.WinRtGyrometer).ToArray();
        if (winRt.Length == 1) return winRt[0];
        if (winRt.Length > 1) { errors.Add("Multiple WinRT Gyrometer candidates were found."); return null; }

        var legacy = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && string.Equals(x.FriendlyName, "Physical Gyrometer", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (legacy.Length == 1) return legacy[0];
        errors.Add(legacy.Length == 0 ? "No Physical Gyrometer was found." : "Multiple Physical Gyrometer candidates were found.");
        return null;
    }

    private static ClawSensorProbeCandidate? SelectAccelerometer(IReadOnlyList<ClawSensorProbeCandidate> sensors, List<string> errors)
    {
        var winRt = sensors.Where(x => x.Backend == ClawSensorProbeBackend.WinRtAccelerometer).ToArray();
        if (winRt.Length == 1) return winRt[0];
        if (winRt.Length > 1) { errors.Add("Multiple WinRT Accelerometer candidates were found."); return null; }

        var direct = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && x.IsDirectTypeMatch).ToArray();
        if (direct.Length == 1) return direct[0];
        if (direct.Length > 1) { errors.Add("Multiple direct-type accelerometer candidates were found."); return null; }

        var legacy = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && string.Equals(x.FriendlyName, "Physical Accelerometer", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (legacy.Length == 1) return legacy[0];
        errors.Add(legacy.Length == 0 ? "No Physical Accelerometer was found." : "Multiple Physical Accelerometer candidates were found.");
        return null;
    }
}

internal sealed record ClawSensorCaptureContext(ClawSensorCaptureMode Mode, ClawSensorProbePhase Phase, int Pass);
internal readonly record struct ClawSensorReportReadResult(bool HasData, double X, double Y, double Z, DateTimeOffset SensorTimestamp)
{
    public static ClawSensorReportReadResult NoData() => new(false, 0, 0, 0, default);
    public static ClawSensorReportReadResult Data(double x, double y, double z, DateTimeOffset sensorTimestamp) => new(true, x, y, z, sensorTimestamp);
}
internal sealed class ClawSensorReportDeduplicator
{
    private DateTimeOffset? _previousSensorTimestamp;
    public bool ShouldAccept(ClawSensorReportReadResult result)
    {
        if (!result.HasData) return false;
        if (_previousSensorTimestamp == result.SensorTimestamp) return false;
        _previousSensorTimestamp = result.SensorTimestamp;
        return true;
    }
}
internal sealed record ClawSensorProbeSample(long Sequence, DateTimeOffset UtcTimestamp, double ElapsedMs, ClawSensorCaptureMode CaptureMode, ClawSensorProbePhase Phase, int PhasePass, string Sensor, double X, double Y, double Z, double SampleIntervalMs, DateTimeOffset? SensorTimestamp = null, string Backend = "Unavailable", double ReadDurationMs = 0, double? SensorAgeMs = null)
{
    public ClawSensorProbeSample(long sequence, DateTimeOffset utcTimestamp, double elapsedMs, ClawSensorProbePhase phase, int phasePass, string sensor, double x, double y, double z, double sampleIntervalMs)
        : this(sequence, utcTimestamp, elapsedMs, ClawSensorCaptureMode.Recording, phase, phasePass, sensor, x, y, z, sampleIntervalMs, null)
    {
    }
}
internal sealed record ClawSensorProbePhaseLog(string name, int pass, double transition_start_elapsed_ms, double recording_start_elapsed_ms, double end_elapsed_ms, long sample_count, string capture_status);
internal sealed record ClawSensorProbeError(string Code, string Message);

// Diagnostic-only timing/freshness evidence per docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 8.
// The 100ms long-read threshold and 5s stale-age threshold are labels for this developer capture, not a production
// freshness contract: a quiet/duplicate/no-data source keeps accumulating age here rather than terminating the session.
internal sealed class ClawSensorProbeTimingStatistics
{
    internal const double DefaultLongReadThresholdMs = 100;
    private readonly List<double> _freshIntervalsMs = [];
    public long FreshCount { get; private set; }
    public long DuplicateCount { get; private set; }
    public long NoDataCount { get; private set; }
    public long ReadFailureCount { get; private set; }
    public long LongReadCount { get; private set; }
    public double LastReadDurationMs { get; private set; }
    public double MaxReadDurationMs { get; private set; }
    public double FreshAgeMs { get; private set; }
    public double MaxFreshAgeMs { get; private set; }
    public double AverageFreshIntervalMs => _freshIntervalsMs.Count == 0 ? 0 : _freshIntervalsMs.Average();
    public double EffectiveFreshHz => AverageFreshIntervalMs <= 0 ? 0 : 1000d / AverageFreshIntervalMs;

    public void Observe(ClawSensorReadOutcome outcome, double readDurationMs, double? freshAgeMs = null, double? freshIntervalMs = null, double longReadThresholdMs = DefaultLongReadThresholdMs)
    {
        LastReadDurationMs = readDurationMs;
        if (readDurationMs > MaxReadDurationMs) MaxReadDurationMs = readDurationMs;
        if (readDurationMs >= longReadThresholdMs) LongReadCount++;
        if (freshAgeMs is { } age)
        {
            FreshAgeMs = age;
            if (age > MaxFreshAgeMs) MaxFreshAgeMs = age;
        }
        switch (outcome)
        {
            case ClawSensorReadOutcome.Fresh:
                FreshCount++;
                if (freshIntervalMs is > 0) _freshIntervalsMs.Add(freshIntervalMs.Value);
                break;
            case ClawSensorReadOutcome.Duplicate: DuplicateCount++; break;
            case ClawSensorReadOutcome.NoData: NoDataCount++; break;
            case ClawSensorReadOutcome.Failure: ReadFailureCount++; break;
        }
    }
}

internal sealed class ClawSensorProbeStatistics
{
    private readonly List<double> _intervals = [];
    public long SampleCount { get; private set; }
    public long DroppedSampleCount { get; private set; }
    public double DurationMs { get; private set; }
    public double AverageIntervalMs => _intervals.Count == 0 ? 0 : _intervals.Average();
    public double MinimumIntervalMs => _intervals.Count == 0 ? 0 : _intervals.Min();
    public double MaximumIntervalMs => _intervals.Count == 0 ? 0 : _intervals.Max();
    public double EffectiveHz => AverageIntervalMs <= 0 ? 0 : 1000 / AverageIntervalMs;
    public void Add(double intervalMs) { SampleCount++; DurationMs += intervalMs; if (intervalMs > 0) _intervals.Add(intervalMs); }
    public void AddDropped() => DroppedSampleCount++;
}

internal sealed class ClawSensorProbeLiveSnapshot
{
    private readonly object _gate = new();
    private double _gyroX, _gyroY, _gyroZ, _accelX, _accelY, _accelZ, _gyroHz, _accelHz;
    private long _gyroCount, _accelCount;
    private readonly Queue<double> _gyroIntervals = new();
    private readonly Queue<double> _accelIntervals = new();
    public (double X, double Y, double Z, double Hz, long Count) Gyro { get { lock (_gate) return (_gyroX, _gyroY, _gyroZ, _gyroHz, _gyroCount); } }
    public (double X, double Y, double Z, double Hz, long Count) Accel { get { lock (_gate) return (_accelX, _accelY, _accelZ, _accelHz, _accelCount); } }
    public void Observe(string sensor, double x, double y, double z, double intervalMs)
    {
        lock (_gate)
        {
            var intervals = sensor == "GYRO" ? _gyroIntervals : _accelIntervals;
            if (intervalMs > 0) { intervals.Enqueue(intervalMs); if (intervals.Count > 20) intervals.Dequeue(); }
            var hz = intervals.Count > 0 ? 1000d / intervals.Average() : 0;
            if (sensor == "GYRO") (_gyroX, _gyroY, _gyroZ, _gyroHz, _gyroCount) = (x, y, z, hz, _gyroCount + 1);
            else (_accelX, _accelY, _accelZ, _accelHz, _accelCount) = (x, y, z, hz, _accelCount + 1);
        }
    }
}

internal sealed class ClawSensorProbeSessionWriter : IAsyncDisposable
{
    private readonly StreamWriter _csv;
    private readonly Channel<ClawSensorProbeSample> _channel = Channel.CreateBounded<ClawSensorProbeSample>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly Task _writerTask;
    private readonly string _reportPath;
    private readonly ClawSensorProbeStatistics _gyro = new();
    private readonly ClawSensorProbeStatistics _accel = new();
    private readonly List<(double X, double Y, double Z)> _restGyro = [];
    private readonly List<(double X, double Y, double Z)> _restAccel = [];
    private readonly List<ClawSensorProbePhaseLog> _phases = [];
    private readonly Dictionary<string, int> _phaseRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _pendingPhaseEnds = new(StringComparer.Ordinal);
    private readonly object _phaseGate = new();
    private ClawSensorDiscovery? _discovery;
    private ClawSensorProbeTimingStatistics? _gyroTiming;
    private ClawSensorProbeTimingStatistics? _accelTiming;
    private object _device = new { Manufacturer = "Unavailable", ProductName = "Unavailable", BaseBoardProduct = "Unavailable", ResolvedAddonDevice = "MSI Claw", ResolvedAddonModel = "Unknown / unresolved" };
    private object _compatibility = new { Status = "Indeterminate", DeviceFamily = "Unavailable", DeviceModel = "Unavailable", Reason = "Not captured" };
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private bool _finalized;
    private bool _shutdownTimedOut;
    private long _dropped;
    private long _droppedGyro;
    private long _droppedAccel;
    private long _diagnosticSequence;
    public long DroppedSampleCount => Interlocked.Read(ref _dropped);
    public long DroppedGyroscopeCount => Interlocked.Read(ref _droppedGyro);
    public long DroppedAccelerometerCount => Interlocked.Read(ref _droppedAccel);
    public string DirectoryPath { get; }
    public ClawSensorProbeStatistics GyroscopeSummary => _gyro;
    public ClawSensorProbeStatistics AccelerometerSummary => _accel;
    public void SetDiscovery(ClawSensorDiscovery discovery) => _discovery = discovery;
    public void SetTiming(ClawSensorProbeTimingStatistics gyroscope, ClawSensorProbeTimingStatistics accelerometer) { _gyroTiming = gyroscope; _accelTiming = accelerometer; }
    public void SetDevice(object device) => _device = device;
    public void SetCompatibility(object compatibility) => _compatibility = compatibility;
    public void AddError(string error) { lock (_errors) _errors.Add(error); }
    public void AddWarning(string warning) { lock (_warnings) _warnings.Add(warning); }
    public void MarkShutdownTimedOut() => _shutdownTimedOut = true;
    public ClawSensorProbeSessionWriter(string root, string sessionId)
    {
        DirectoryPath = Path.Combine(root, sessionId);
        Directory.CreateDirectory(DirectoryPath);
        _reportPath = Path.Combine(DirectoryPath, "claw-sensor-report.json");
        _csv = new StreamWriter(Path.Combine(DirectoryPath, "claw-sensor-live.csv"), false, new UTF8Encoding(false)) { AutoFlush = false };
        _csv.WriteLine("sequence,utc_timestamp,elapsed_ms,capture_mode,phase,phase_pass,sensor,x,y,z,sample_interval_ms,sensor_timestamp,backend,read_duration_ms,sensor_age_ms");
        _writerTask = WriteLoopAsync();
    }
    public void Write(ClawSensorProbeSample sample)
    {
        sample = sample with { Sequence = Interlocked.Increment(ref _diagnosticSequence) };
        if (_finalized || !_channel.Writer.TryWrite(sample))
        {
            Interlocked.Increment(ref _dropped);
            if (sample.Sensor == "GYRO") { Interlocked.Increment(ref _droppedGyro); _gyro.AddDropped(); }
            if (sample.Sensor == "ACCEL") { Interlocked.Increment(ref _droppedAccel); _accel.AddDropped(); }
        }
    }
    public void WriteTransition(ClawSensorProbePhase phase, int pass, double elapsedMs) { EnsurePhaseStarted(phase, pass, elapsedMs); Write(new(0, DateTimeOffset.UtcNow, elapsedMs, ClawSensorCaptureMode.Transition, phase, pass, "TRANSITION", 0, 0, 0, 0)); }
    private void EnsurePhaseStarted(ClawSensorProbePhase phase, int pass, double elapsedMs)
    {
        var key = $"{phase}:{pass}";
        lock (_phaseGate)
        {
            if (_phaseRows.ContainsKey(key)) return;
            _phaseRows[key] = _phases.Count;
            var end = _pendingPhaseEnds.TryGetValue(key, out var pendingEnd) ? pendingEnd : elapsedMs;
            _phases.Add(new(phase.ToString(), pass, elapsedMs, 0, end, 0, "TransitionOnly"));
        }
    }
    public void BeginRecordingPhase(ClawSensorProbePhase phase, int pass, double elapsedMs)
    {
        EnsurePhaseStarted(phase, pass, elapsedMs);
        var key = $"{phase}:{pass}";
        lock (_phaseGate)
        {
            var index = _phaseRows[key];
            var phaseLog = _phases[index];
            _phases[index] = phaseLog with { recording_start_elapsed_ms = elapsedMs };
        }
    }
    public void EndPhase(ClawSensorProbePhase phase, int pass, double elapsedMs)
    {
        var key = $"{phase}:{pass}";
        lock (_phaseGate)
        {
            if (_phaseRows.TryGetValue(key, out var index)) _phases[index] = _phases[index] with { end_elapsed_ms = elapsedMs };
            else _pendingPhaseEnds[key] = elapsedMs;
        }
    }
    private async Task WriteLoopAsync()
    {
        await foreach (var sample in _channel.Reader.ReadAllAsync())
        {
            _csv.WriteLine(string.Join(',', sample.Sequence.ToString(CultureInfo.InvariantCulture), sample.UtcTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), sample.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture), sample.CaptureMode.ToString().ToUpperInvariant(), sample.Phase, sample.PhasePass, sample.Sensor, sample.X.ToString("R", CultureInfo.InvariantCulture), sample.Y.ToString("R", CultureInfo.InvariantCulture), sample.Z.ToString("R", CultureInfo.InvariantCulture), sample.SampleIntervalMs.ToString("0.###", CultureInfo.InvariantCulture), sample.SensorTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty, sample.Backend, sample.ReadDurationMs.ToString("0.###", CultureInfo.InvariantCulture), sample.SensorAgeMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty));
            var isRecordingSensorSample = sample.CaptureMode == ClawSensorCaptureMode.Recording && sample.Sensor is "GYRO" or "ACCEL";
            if (isRecordingSensorSample) (sample.Sensor == "GYRO" ? _gyro : _accel).Add(sample.SampleIntervalMs);
            if (isRecordingSensorSample && sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "GYRO") _restGyro.Add((sample.X, sample.Y, sample.Z));
            if (isRecordingSensorSample && sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "ACCEL") _restAccel.Add((sample.X, sample.Y, sample.Z));
            var key = $"{sample.Phase}:{sample.PhasePass}";
            lock (_phaseGate)
            {
                if (!_phaseRows.ContainsKey(key))
                {
                    _phaseRows[key] = _phases.Count;
                    var end = _pendingPhaseEnds.TryGetValue(key, out var pendingEnd) ? pendingEnd : sample.ElapsedMs;
                    _phases.Add(new(sample.Phase.ToString(), sample.PhasePass, sample.ElapsedMs, sample.CaptureMode == ClawSensorCaptureMode.Recording ? sample.ElapsedMs : 0, end, 0, "TransitionOnly"));
                }
                if (isRecordingSensorSample)
                {
                    var phaseIndex = _phaseRows[key];
                    var phaseLog = _phases[phaseIndex];
                    _phases[phaseIndex] = phaseLog with { sample_count = phaseLog.sample_count + 1, capture_status = "Captured" };
                }
            }
        }
    }
    public async ValueTask FinalizeAsync(CancellationToken cancellationToken = default)
    {
        if (_finalized) return;
        _finalized = true;
        _channel.Writer.TryComplete();
        await _writerTask;
        await _csv.FlushAsync(cancellationToken);
        await _csv.DisposeAsync();
        string[] errors; lock (_errors) errors = (_discovery?.Errors ?? []).Concat(_errors).Distinct(StringComparer.Ordinal).ToArray();
        string[] explicitWarnings; lock (_warnings) explicitWarnings = _warnings.ToArray();
        var warnings = explicitWarnings
            .Concat(DroppedSampleCount > 0 ? new[] { "The diagnostic writer queue was full and samples were dropped." } : Array.Empty<string>())
            .Concat(_shutdownTimedOut ? new[] { "Sensor reader shutdown exceeded the bounded wait." } : Array.Empty<string>())
            .ToArray();
        var report = new { SchemaVersion = 2, SessionId = Path.GetFileName(DirectoryPath), AppVersion = typeof(ClawSensorProbeSessionWriter).Assembly.GetName().Version?.ToString() ?? "Unknown", StartUtc = Directory.GetCreationTimeUtc(DirectoryPath), EndUtc = DateTime.UtcNow, Device = _device, ResolvedHardware = _compatibility, SensorDiscovery = _discovery?.Sensors, SelectedGyroscope = _discovery?.Gyroscope, SelectedAccelerometer = _discovery?.Accelerometer, DataKeys = new { Guid = "B14C764F-07CF-41E8-9D82-EBE3D0776A6F", X = 7, Y = 8, Z = 9 }, Phases = _phases, RestSummary = new { Gyroscope = AxisSummary(_restGyro, true), Accelerometer = AxisSummary(_restAccel, false) }, GyroscopeSummary = _gyro, AccelerometerSummary = _accel, TimingSummary = new { Gyroscope = TimingSummaryOf(_gyroTiming), Accelerometer = TimingSummaryOf(_accelTiming) }, DroppedSampleCount = DroppedSampleCount, DroppedGyroscopeCount = DroppedGyroscopeCount, DroppedAccelerometerCount = DroppedAccelerometerCount, ShutdownTimedOut = _shutdownTimedOut, Errors = errors, Warnings = warnings };
        await File.WriteAllTextAsync(_reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
    }
    private static object? TimingSummaryOf(ClawSensorProbeTimingStatistics? timing) => timing is null ? null : new
    {
        timing.FreshCount,
        timing.DuplicateCount,
        timing.NoDataCount,
        timing.ReadFailureCount,
        timing.AverageFreshIntervalMs,
        timing.EffectiveFreshHz,
        timing.LastReadDurationMs,
        timing.MaxReadDurationMs,
        timing.FreshAgeMs,
        timing.MaxFreshAgeMs,
        timing.LongReadCount
    };
    private static object AxisSummary(IReadOnlyList<(double X, double Y, double Z)> values, bool includeStandardDeviation)
    {
        if (values.Count == 0) return new { SampleCount = 0 };
        var mean = new { X = values.Average(x => x.X), Y = values.Average(x => x.Y), Z = values.Average(x => x.Z) };
        var result = new Dictionary<string, object?> { ["SampleCount"] = values.Count, ["Mean"] = mean, ["MagnitudeMean"] = values.Average(x => Math.Sqrt(x.X * x.X + x.Y * x.Y + x.Z * x.Z)) };
        if (includeStandardDeviation) result["StandardDeviation"] = new { X = Std(values.Select(x => x.X)), Y = Std(values.Select(x => x.Y)), Z = Std(values.Select(x => x.Z)) };
        return result;
    }
    private static double Std(IEnumerable<double> source) { var values = source.ToArray(); var mean = values.Average(); return Math.Sqrt(values.Select(x => (x - mean) * (x - mean)).Average()); }
    public ValueTask DisposeAsync() => new(FinalizeAsync().AsTask());
}
