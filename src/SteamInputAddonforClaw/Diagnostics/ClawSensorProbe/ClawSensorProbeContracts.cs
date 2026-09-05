using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal enum ClawSensorProbePhase { REST, ROLL_LEFT, ROLL_RIGHT, PITCH_UP, PITCH_DOWN, YAW_LEFT, YAW_RIGHT }
internal enum ClawSensorProbeState { Idle, Discovering, Ready, Starting, Countdown, RecordingPhase, Stopping, Completed, Failed }
internal enum ClawSensorCaptureMode { Inactive, Transition, Recording }
// The diagnostic session's PURPOSE (chosen once at Start), distinct from ClawSensorCaptureMode above,
// which is the per-sample recording STATE written into CSV (docs/gyro/SD6A_CLAW_SENSOR_PROBE_PR_B_
// CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md section 4.2). Do not conflate the two.
internal enum ClawSensorProbeMode { LiveSanity, AxisCharacterization, StationaryBias }
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
    string? SelectionReason = null,
    bool? SupportsX = null,
    bool? SupportsY = null,
    bool? SupportsZ = null);

// One WinRT source's discovery evidence: whether GetDefault()+a live reading succeeded, the exact
// failure/HRESULT when it did not, and the resulting candidate (null when unavailable/unreadable).
internal sealed record ClawSensorProbeWinRtEvidence(bool Available, int? HResult, string? Failure, ClawSensorProbeCandidate? Candidate)
{
    internal static readonly ClawSensorProbeWinRtEvidence Unavailable = new(false, null, "Unavailable", null);
}

internal sealed record ClawSensorDiscovery(
    IReadOnlyList<ClawSensorProbeCandidate> Sensors,
    ClawSensorProbeCandidate? Gyroscope,
    ClawSensorProbeCandidate? Accelerometer,
    IReadOnlyList<string> Errors,
    LegacySensorQueryInfo? LegacyCategoryAll = null,
    IReadOnlyList<LegacySensorQueryInfo>? LegacyDirectTypeQueries = null,
    ClawSensorProbeWinRtEvidence? WinRtGyrometer = null,
    ClawSensorProbeWinRtEvidence? WinRtAccelerometer = null)
{
    public bool IsValid => Gyroscope is not null && Accelerometer is not null && Errors.Count == 0;

    // Selection is diagnostic, model-bounded, and conservative (see docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 5.4):
    // gyro prefers a unique WinRT Gyrometer candidate before falling back to the unique legacy "Physical Gyrometer" match;
    // accel prefers a unique WinRT Accelerometer candidate, then a unique legacy candidate validated through a direct
    // GetSensorsByType lookup, then finally the unique broad-enumeration "Physical Accelerometer" match. Ambiguous
    // candidates at any preferred tier fail closed rather than falling through to a weaker tier.
    //
    // The optional query/projection evidence (legacyCategoryAll, legacyDirectTypeQueries, winRtGyrometer,
    // winRtAccelerometer) is preserved on the result even when selection fails or picks a different backend,
    // so a real case such as CategoryAll failing with 0x80070490 while a direct-type lookup still succeeds
    // remains visible in the finalized report instead of being projected away to just the merged candidates.
    public static ClawSensorDiscovery Select(IReadOnlyList<ClawSensorProbeCandidate> sensors) =>
        Select(sensors, null, null, null, null);

    public static ClawSensorDiscovery Select(
        IReadOnlyList<ClawSensorProbeCandidate> sensors,
        LegacySensorQueryInfo? legacyCategoryAll,
        IReadOnlyList<LegacySensorQueryInfo>? legacyDirectTypeQueries,
        ClawSensorProbeWinRtEvidence? winRtGyrometer,
        ClawSensorProbeWinRtEvidence? winRtAccelerometer)
    {
        var errors = new List<string>();
        var gyroscope = SelectGyroscope(sensors, errors);
        var accelerometer = SelectAccelerometer(sensors, errors);
        return new(sensors, gyroscope, accelerometer, errors, legacyCategoryAll, legacyDirectTypeQueries, winRtGyrometer, winRtAccelerometer);
    }

    // A present sensor projection can still be unusable during normal driver/device lifecycle (no X/Y/Z
    // support reported, or an explicit not-available/access-denied/error state); such a candidate must not
    // win selection over a genuinely usable fallback merely because its friendly name matched.
    private static bool HasRequiredLegacyXyz(ClawSensorProbeCandidate candidate) =>
        candidate.SupportsX == true && candidate.SupportsY == true && candidate.SupportsZ == true;

    private static bool HasExplicitUnusableState(ClawSensorProbeCandidate candidate) =>
        candidate.State is "NotAvailable" or "AccessDenied" or "Error";

    private static bool IsUsableLegacyCandidate(ClawSensorProbeCandidate candidate) =>
        HasRequiredLegacyXyz(candidate) && !HasExplicitUnusableState(candidate);

    private static ClawSensorProbeCandidate? SelectGyroscope(IReadOnlyList<ClawSensorProbeCandidate> sensors, List<string> errors)
    {
        var winRt = sensors.Where(x => x.Backend == ClawSensorProbeBackend.WinRtGyrometer).ToArray();
        if (winRt.Length == 1) return winRt[0];
        if (winRt.Length > 1) { errors.Add("Multiple WinRT Gyrometer candidates were found."); return null; }

        var legacy = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && string.Equals(x.FriendlyName, "Physical Gyrometer", StringComparison.OrdinalIgnoreCase) && IsUsableLegacyCandidate(x)).ToArray();
        if (legacy.Length == 1) return legacy[0] with { SelectionReason = "Selected as the unique usable legacy Physical Gyrometer fallback." };
        errors.Add(legacy.Length == 0 ? "No usable Physical Gyrometer was found." : "Multiple Physical Gyrometer candidates were found.");
        return null;
    }

    private static ClawSensorProbeCandidate? SelectAccelerometer(IReadOnlyList<ClawSensorProbeCandidate> sensors, List<string> errors)
    {
        var winRt = sensors.Where(x => x.Backend == ClawSensorProbeBackend.WinRtAccelerometer).ToArray();
        if (winRt.Length == 1) return winRt[0];
        if (winRt.Length > 1) { errors.Add("Multiple WinRT Accelerometer candidates were found."); return null; }

        var direct = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && x.IsDirectTypeMatch && IsUsableLegacyCandidate(x)).ToArray();
        if (direct.Length == 1) return direct[0];
        if (direct.Length > 1) { errors.Add("Multiple direct-type accelerometer candidates were found."); return null; }

        var legacy = sensors.Where(x => x.Backend == ClawSensorProbeBackend.LegacySensorApi && string.Equals(x.FriendlyName, "Physical Accelerometer", StringComparison.OrdinalIgnoreCase) && IsUsableLegacyCandidate(x)).ToArray();
        if (legacy.Length == 1) return legacy[0] with { SelectionReason = "Selected as the unique usable broad-enumeration Physical Accelerometer fallback." };
        errors.Add(legacy.Length == 0 ? "No usable Physical Accelerometer was found." : "Multiple Physical Accelerometer candidates were found.");
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
// Report-interval evidence for the source actually opened at capture time (docs/gyro/SD6A_CLAW_SENSOR_PROBE_
// CHARACTERIZATION_WORK_ORDER.md section 6.1): distinguishes the sensor's own minimum from what the probe
// requested and what Windows actually granted, so later CG3EM analysis can tell configuration from cadence.
// Legacy sources report null requested/effective values -- that request/grant negotiation is a WinRT concept.
internal sealed record ClawSensorProbeSourceConfiguration(ClawSensorProbeBackend Backend, uint? MinimumReportIntervalMs, uint? RequestedReportIntervalMs, uint? EffectiveReportIntervalMs);

internal sealed record ClawSensorProbePhaseLog(string name, int pass, double transition_start_elapsed_ms, double recording_start_elapsed_ms, double end_elapsed_ms, long sample_count, string capture_status);
internal sealed record ClawSensorProbeError(string Code, string Message);

// Immutable point-in-time copy of ClawSensorProbeTimingStatistics. The writer/report must only ever consume
// this, never the live mutable object: the bounded reader-teardown wait can return while a worker is still
// blocked in a backend read, so a live object handed to the writer could still be mutated concurrently with
// report serialization (docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 17).
internal sealed record ClawSensorProbeTimingSnapshot(
    long FreshCount,
    long DuplicateCount,
    long NoDataCount,
    long ReadFailureCount,
    double AverageFreshIntervalMs,
    double EffectiveFreshHz,
    double LastReadDurationMs,
    double MaxReadDurationMs,
    double FreshAgeMs,
    double MaxFreshAgeMs,
    long LongReadCount);

// Diagnostic-only timing/freshness evidence per docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 8.
// The 100ms long-read threshold and 5s stale-age threshold are labels for this developer capture, not a production
// freshness contract: a quiet/duplicate/no-data source keeps accumulating age here rather than terminating the session.
// All state is guarded by one lock: a running sum/count (not a List<double>.Average() on every read) backs
// AverageFreshIntervalMs so no shared collection is ever enumerated concurrently with a mutation.
internal sealed class ClawSensorProbeTimingStatistics
{
    internal const double DefaultLongReadThresholdMs = 100;
    private readonly object _gate = new();
    private double _freshIntervalTotalMs;
    private long _freshIntervalCount;
    private long _freshCount, _duplicateCount, _noDataCount, _readFailureCount, _longReadCount;
    private double _lastReadDurationMs, _maxReadDurationMs, _freshAgeMs, _maxFreshAgeMs;

    public long FreshCount { get { lock (_gate) return _freshCount; } }
    public long DuplicateCount { get { lock (_gate) return _duplicateCount; } }
    public long NoDataCount { get { lock (_gate) return _noDataCount; } }
    public long ReadFailureCount { get { lock (_gate) return _readFailureCount; } }
    public long LongReadCount { get { lock (_gate) return _longReadCount; } }
    public double LastReadDurationMs { get { lock (_gate) return _lastReadDurationMs; } }
    public double MaxReadDurationMs { get { lock (_gate) return _maxReadDurationMs; } }
    public double FreshAgeMs { get { lock (_gate) return _freshAgeMs; } }
    public double MaxFreshAgeMs { get { lock (_gate) return _maxFreshAgeMs; } }
    public double AverageFreshIntervalMs { get { lock (_gate) return ComputeAverageFreshIntervalMs(); } }
    public double EffectiveFreshHz { get { lock (_gate) { var average = ComputeAverageFreshIntervalMs(); return average <= 0 ? 0 : 1000d / average; } } }

    private double ComputeAverageFreshIntervalMs() => _freshIntervalCount == 0 ? 0 : _freshIntervalTotalMs / _freshIntervalCount;

    public void Observe(ClawSensorReadOutcome outcome, double readDurationMs, double? freshAgeMs = null, double? freshIntervalMs = null, double longReadThresholdMs = DefaultLongReadThresholdMs)
    {
        lock (_gate)
        {
            _lastReadDurationMs = readDurationMs;
            if (readDurationMs > _maxReadDurationMs) _maxReadDurationMs = readDurationMs;
            if (readDurationMs >= longReadThresholdMs) _longReadCount++;
            if (freshAgeMs is { } age)
            {
                // FreshAgeMs reflects the CURRENT freshness gap: a Fresh outcome means the source just
                // reported, so the gap resets to zero even though the age passed in here is the pre-reset
                // value measured just before RunAsync() updates lastFreshReport. MaxFreshAgeMs still keeps
                // the peak, so a prior stale/quiet period remains visible in the report.
                if (age > _maxFreshAgeMs) _maxFreshAgeMs = age;
                _freshAgeMs = outcome == ClawSensorReadOutcome.Fresh ? 0 : age;
            }
            switch (outcome)
            {
                case ClawSensorReadOutcome.Fresh:
                    _freshCount++;
                    if (freshIntervalMs is > 0) { _freshIntervalTotalMs += freshIntervalMs.Value; _freshIntervalCount++; }
                    break;
                case ClawSensorReadOutcome.Duplicate: _duplicateCount++; break;
                case ClawSensorReadOutcome.NoData: _noDataCount++; break;
                case ClawSensorReadOutcome.Failure: _readFailureCount++; break;
            }
        }
    }

    public ClawSensorProbeTimingSnapshot Snapshot()
    {
        lock (_gate)
        {
            var average = ComputeAverageFreshIntervalMs();
            return new(_freshCount, _duplicateCount, _noDataCount, _readFailureCount, average, average <= 0 ? 0 : 1000d / average, _lastReadDurationMs, _maxReadDurationMs, _freshAgeMs, _maxFreshAgeMs, _longReadCount);
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

// Pass-aware per-(Phase, Pass, Sensor) / stationary-bias vector aggregator (docs/gyro/SD6A_CLAW_
// SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md section 9): mean/min/max/span plus an
// optional known-g magnitude accumulation. All mutation happens from ClawSensorProbeSessionWriter's
// single-consumer WriteLoopAsync, so -- like _restGyro/_restAccel before it -- no locking is needed.
internal sealed class ClawSensorVectorAccumulator
{
    private double _sumX, _sumY, _sumZ, _sumSqX, _sumSqY, _sumSqZ;
    private double _minX = double.MaxValue, _minY = double.MaxValue, _minZ = double.MaxValue;
    private double _maxX = double.MinValue, _maxY = double.MinValue, _maxZ = double.MinValue;
    private double _firstElapsedMs, _lastElapsedMs;
    private double _sumMagnitudeG, _minMagnitudeG = double.MaxValue, _maxMagnitudeG = double.MinValue;
    public long Count { get; private set; }
    public bool HasMagnitude { get; private set; }

    public void Add(double x, double y, double z, double elapsedMs)
    {
        if (Count == 0) _firstElapsedMs = elapsedMs;
        _lastElapsedMs = elapsedMs;
        Count++;
        _sumX += x; _sumY += y; _sumZ += z;
        _sumSqX += x * x; _sumSqY += y * y; _sumSqZ += z * z;
        if (x < _minX) _minX = x; if (x > _maxX) _maxX = x;
        if (y < _minY) _minY = y; if (y > _maxY) _maxY = y;
        if (z < _minZ) _minZ = z; if (z > _maxZ) _maxZ = z;
        // Magnitude is always accumulated from the raw triple -- it's just sqrt(x^2+y^2+z^2) and
        // carries no unit assumption -- but the caller only ever SURFACES it when the selected
        // source's UnitBasis is proven G (see AxisSummary's existing includeMagnitudeG pattern).
        var magnitude = Math.Sqrt(x * x + y * y + z * z);
        HasMagnitude = true;
        _sumMagnitudeG += magnitude;
        if (magnitude < _minMagnitudeG) _minMagnitudeG = magnitude;
        if (magnitude > _maxMagnitudeG) _maxMagnitudeG = magnitude;
    }

    public double MeanX => Count == 0 ? 0 : _sumX / Count;
    public double MeanY => Count == 0 ? 0 : _sumY / Count;
    public double MeanZ => Count == 0 ? 0 : _sumZ / Count;
    public double MinX => Count == 0 ? 0 : _minX;
    public double MinY => Count == 0 ? 0 : _minY;
    public double MinZ => Count == 0 ? 0 : _minZ;
    public double MaxX => Count == 0 ? 0 : _maxX;
    public double MaxY => Count == 0 ? 0 : _maxY;
    public double MaxZ => Count == 0 ? 0 : _maxZ;
    public double SpanX => Count == 0 ? 0 : _maxX - _minX;
    public double SpanY => Count == 0 ? 0 : _maxY - _minY;
    public double SpanZ => Count == 0 ? 0 : _maxZ - _minZ;
    public double StandardDeviationX => Count == 0 ? 0 : Math.Sqrt(Math.Max(0, _sumSqX / Count - MeanX * MeanX));
    public double StandardDeviationY => Count == 0 ? 0 : Math.Sqrt(Math.Max(0, _sumSqY / Count - MeanY * MeanY));
    public double StandardDeviationZ => Count == 0 ? 0 : Math.Sqrt(Math.Max(0, _sumSqZ / Count - MeanZ * MeanZ));
    public double StartElapsedMs => Count == 0 ? 0 : _firstElapsedMs;
    public double EndElapsedMs => Count == 0 ? 0 : _lastElapsedMs;
    public double DurationMs => Count <= 1 ? 0 : _lastElapsedMs - _firstElapsedMs;
    public double EffectiveHz => Count <= 1 || DurationMs <= 0 ? 0 : (Count - 1) * 1000d / DurationMs;
    public double MagnitudeGMean => Count == 0 ? 0 : _sumMagnitudeG / Count;
    public double MagnitudeGMin => Count == 0 ? 0 : _minMagnitudeG;
    public double MagnitudeGMax => Count == 0 ? 0 : _maxMagnitudeG;
    public double MagnitudeGSpan => Count == 0 ? 0 : _maxMagnitudeG - _minMagnitudeG;
}

// Immutable Stationary Bias completion evidence, computed once in FinalizeAsync() after the writer
// task has drained (no further accumulator mutation is possible past that point) so it is safe to
// read concurrently from a polling RPC without any additional lock. Kept as a plain internal record
// here (rather than a Contracts/Frontend type) to keep this file free of a Frontend-layer dependency;
// InProcessAddonFrontendControl projects this into FrontendClawSensorProbeBiasSummary.
internal sealed record ClawSensorProbeBiasSummarySnapshot(
    long GyroSampleCount, double GyroEffectiveHz,
    double GyroMeanX, double GyroMeanY, double GyroMeanZ,
    double GyroStandardDeviationX, double GyroStandardDeviationY, double GyroStandardDeviationZ,
    double GyroSpanX, double GyroSpanY, double GyroSpanZ,
    long AccelSampleCount, double AccelEffectiveHz,
    double AccelSpanX, double AccelSpanY, double AccelSpanZ,
    double? AccelMagnitudeGMean, double? AccelMagnitudeGSpan);

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
    private readonly ClawSensorProbeMode _mode;
    // Axis per-(Phase, Pass, Sensor) accumulators, in first-seen order so PerPhaseSummaries reads in
    // visit order; Bias's two accumulators are simpler since a bias session has exactly one implicit
    // "visit". Both are mutated only from WriteLoopAsync (the single channel consumer), same as the
    // pre-existing _restGyro/_restAccel/_phases fields above.
    private readonly Dictionary<string, ClawSensorVectorAccumulator> _phaseAccumulators = new(StringComparer.Ordinal);
    private readonly List<(ClawSensorProbePhase Phase, int Pass, string Sensor)> _phaseAccumulatorOrder = [];
    private readonly ClawSensorVectorAccumulator _biasGyro = new();
    private readonly ClawSensorVectorAccumulator _biasAccel = new();
    private ClawSensorDiscovery? _discovery;
    private ClawSensorProbeTimingSnapshot? _gyroTiming;
    private ClawSensorProbeTimingSnapshot? _accelTiming;
    private ClawSensorProbeSourceConfiguration? _gyroConfiguration;
    private ClawSensorProbeSourceConfiguration? _accelConfiguration;
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
    // Frozen teardown-boundary timing evidence (set via SetTiming, same as PR A), exposed read-only so
    // the frontend's compact UI projection can read final values after readers have gone away (work
    // order section 18) without keeping the disposed reader object alive.
    public ClawSensorProbeTimingSnapshot? GyroscopeTimingSnapshot => _gyroTiming;
    public ClawSensorProbeTimingSnapshot? AccelerometerTimingSnapshot => _accelTiming;
    // Only non-null after FinalizeAsync() has run for a StationaryBias session -- computed once past
    // the point where the single-consumer writer task can still mutate the underlying accumulators,
    // so it is safe to read concurrently from a polling RPC with no additional lock.
    public ClawSensorProbeBiasSummarySnapshot? BiasSummary { get; private set; }
    public void SetDiscovery(ClawSensorDiscovery discovery) => _discovery = discovery;
    public void SetTiming(ClawSensorProbeTimingSnapshot? gyroscope, ClawSensorProbeTimingSnapshot? accelerometer) { _gyroTiming = gyroscope; _accelTiming = accelerometer; }
    public void SetSourceConfiguration(ClawSensorProbeSourceConfiguration? gyroscope, ClawSensorProbeSourceConfiguration? accelerometer) { _gyroConfiguration = gyroscope; _accelConfiguration = accelerometer; }
    public void SetDevice(object device) => _device = device;
    public void SetCompatibility(object compatibility) => _compatibility = compatibility;
    public void AddError(string error) { lock (_errors) _errors.Add(error); }
    public void AddWarning(string warning) { lock (_warnings) _warnings.Add(warning); }
    public void MarkShutdownTimedOut() => _shutdownTimedOut = true;
    // Mode defaults to AxisCharacterization so the many existing PR-A tests that construct a writer
    // without a mode keep exercising exactly the original seven-phase behavior.
    public ClawSensorProbeSessionWriter(string root, string sessionId) : this(root, sessionId, ClawSensorProbeMode.AxisCharacterization) { }
    public ClawSensorProbeSessionWriter(string root, string sessionId, ClawSensorProbeMode mode)
    {
        _mode = mode;
        DirectoryPath = Path.Combine(root, sessionId);
        Directory.CreateDirectory(DirectoryPath);
        _reportPath = Path.Combine(DirectoryPath, "claw-sensor-report.json");
        _csv = new StreamWriter(Path.Combine(DirectoryPath, "claw-sensor-live.csv"), false, new UTF8Encoding(false)) { AutoFlush = false };
        _csv.WriteLine("sequence,utc_timestamp,elapsed_ms,probe_mode,capture_mode,phase,phase_pass,sensor,x,y,z,sample_interval_ms,sensor_timestamp,backend,read_duration_ms,sensor_age_ms");
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
    private bool IsAxis => _mode == ClawSensorProbeMode.AxisCharacterization;
    private async Task WriteLoopAsync()
    {
        await foreach (var sample in _channel.Reader.ReadAllAsync())
        {
            // Live Sanity / Stationary Bias have no real axis phase -- the sample still carries the
            // reader context's REST/1 placeholder (see ClawSensorProbeCoordinator), but the CSV/report
            // projection must not claim that as real phase evidence for those modes (work order
            // section 14): blank phase/phase_pass in the CSV row and skip all phase-log bookkeeping.
            _csv.WriteLine(string.Join(',', sample.Sequence.ToString(CultureInfo.InvariantCulture), sample.UtcTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), sample.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture), _mode.ToString(), sample.CaptureMode.ToString().ToUpperInvariant(), IsAxis ? sample.Phase.ToString() : string.Empty, IsAxis ? sample.PhasePass.ToString(CultureInfo.InvariantCulture) : string.Empty, sample.Sensor, sample.X.ToString("R", CultureInfo.InvariantCulture), sample.Y.ToString("R", CultureInfo.InvariantCulture), sample.Z.ToString("R", CultureInfo.InvariantCulture), sample.SampleIntervalMs.ToString("0.###", CultureInfo.InvariantCulture), sample.SensorTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty, sample.Backend, sample.ReadDurationMs.ToString("0.###", CultureInfo.InvariantCulture), sample.SensorAgeMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty));
            var isRecordingSensorSample = sample.CaptureMode == ClawSensorCaptureMode.Recording && sample.Sensor is "GYRO" or "ACCEL";
            if (isRecordingSensorSample) (sample.Sensor == "GYRO" ? _gyro : _accel).Add(sample.SampleIntervalMs);
            if (isRecordingSensorSample && IsAxis && sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "GYRO") _restGyro.Add((sample.X, sample.Y, sample.Z));
            if (isRecordingSensorSample && IsAxis && sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "ACCEL") _restAccel.Add((sample.X, sample.Y, sample.Z));
            if (isRecordingSensorSample && _mode == ClawSensorProbeMode.StationaryBias)
                (sample.Sensor == "GYRO" ? _biasGyro : _biasAccel).Add(sample.X, sample.Y, sample.Z, sample.ElapsedMs);
            if (isRecordingSensorSample && IsAxis)
            {
                var accumulatorKey = $"{sample.Phase}:{sample.PhasePass}:{sample.Sensor}";
                if (!_phaseAccumulators.TryGetValue(accumulatorKey, out var accumulator))
                {
                    accumulator = new ClawSensorVectorAccumulator();
                    _phaseAccumulators[accumulatorKey] = accumulator;
                    _phaseAccumulatorOrder.Add((sample.Phase, sample.PhasePass, sample.Sensor));
                }
                accumulator.Add(sample.X, sample.Y, sample.Z, sample.ElapsedMs);
            }
            if (IsAxis)
            {
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
        var isAccelG = _discovery?.Accelerometer?.UnitBasis == ClawSensorProbeUnitBasis.G;
        if (_mode == ClawSensorProbeMode.StationaryBias)
            BiasSummary = new ClawSensorProbeBiasSummarySnapshot(
                _biasGyro.Count, _biasGyro.EffectiveHz,
                _biasGyro.MeanX, _biasGyro.MeanY, _biasGyro.MeanZ,
                _biasGyro.StandardDeviationX, _biasGyro.StandardDeviationY, _biasGyro.StandardDeviationZ,
                _biasGyro.SpanX, _biasGyro.SpanY, _biasGyro.SpanZ,
                _biasAccel.Count, _biasAccel.EffectiveHz,
                _biasAccel.SpanX, _biasAccel.SpanY, _biasAccel.SpanZ,
                isAccelG ? _biasAccel.MagnitudeGMean : null, isAccelG ? _biasAccel.MagnitudeGSpan : null);
        var report = new { SchemaVersion = 2, SessionId = Path.GetFileName(DirectoryPath), AppVersion = typeof(ClawSensorProbeSessionWriter).Assembly.GetName().Version?.ToString() ?? "Unknown", StartUtc = Directory.GetCreationTimeUtc(DirectoryPath), EndUtc = DateTime.UtcNow, Device = _device, ResolvedHardware = _compatibility, CaptureMode = _mode, Discovery = new { LegacyCategoryAll = _discovery?.LegacyCategoryAll, LegacyDirectTypeQueries = _discovery?.LegacyDirectTypeQueries, WinRtGyrometer = _discovery?.WinRtGyrometer, WinRtAccelerometer = _discovery?.WinRtAccelerometer }, SensorDiscovery = _discovery?.Sensors, SelectedGyroscope = _discovery?.Gyroscope, SelectedAccelerometer = _discovery?.Accelerometer, SourceConfiguration = new { Gyroscope = _gyroConfiguration, Accelerometer = _accelConfiguration }, LegacyCustomDataKeys = new { Guid = "B14C764F-07CF-41E8-9D82-EBE3D0776A6F", X = 7, Y = 8, Z = 9 }, Phases = _phases, PerPhaseSummaries = BuildPerPhaseSummaries(isAccelG), RestSummary = new { Gyroscope = AxisSummary(_restGyro, true), Accelerometer = AxisSummary(_restAccel, false, isAccelG) }, StationaryBiasSummary = BuildStationaryBiasSummary(isAccelG), GyroscopeSummary = _gyro, AccelerometerSummary = _accel, TimingSummary = new { Gyroscope = TimingSummaryOf(_gyroTiming), Accelerometer = TimingSummaryOf(_accelTiming) }, DroppedSampleCount = DroppedSampleCount, DroppedGyroscopeCount = DroppedGyroscopeCount, DroppedAccelerometerCount = DroppedAccelerometerCount, ShutdownTimedOut = _shutdownTimedOut, Errors = errors, Warnings = warnings };
        await File.WriteAllTextAsync(_reportPath, JsonSerializer.Serialize(report, ReportSerializerOptions), Encoding.UTF8, cancellationToken);
    }

    // Axis-only: one entry per (Phase, Pass, Sensor) actually recorded, in first-seen (visit) order
    // (docs/gyro/SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md section 10).
    // Live Sanity / Stationary Bias never populate _phaseAccumulators (see WriteLoopAsync), so this is
    // an empty array for those modes, matching the required JSON shape.
    private object[] BuildPerPhaseSummaries(bool includeAccelMagnitudeG) => [.. _phaseAccumulatorOrder.Select(entry =>
    {
        var accumulator = _phaseAccumulators[$"{entry.Phase}:{entry.Pass}:{entry.Sensor}"];
        var includeMagnitude = includeAccelMagnitudeG && entry.Sensor == "ACCEL";
        var result = new Dictionary<string, object?>
        {
            ["Phase"] = entry.Phase,
            ["Pass"] = entry.Pass,
            ["Sensor"] = entry.Sensor,
            ["Backend"] = entry.Sensor == "GYRO" ? _discovery?.Gyroscope?.Backend : _discovery?.Accelerometer?.Backend,
            ["SampleCount"] = accumulator.Count,
            ["MeanX"] = accumulator.MeanX, ["MeanY"] = accumulator.MeanY, ["MeanZ"] = accumulator.MeanZ,
            ["MinX"] = accumulator.MinX, ["MinY"] = accumulator.MinY, ["MinZ"] = accumulator.MinZ,
            ["MaxX"] = accumulator.MaxX, ["MaxY"] = accumulator.MaxY, ["MaxZ"] = accumulator.MaxZ,
            ["SpanX"] = accumulator.SpanX, ["SpanY"] = accumulator.SpanY, ["SpanZ"] = accumulator.SpanZ,
            ["DurationMs"] = accumulator.DurationMs,
            ["EffectiveHz"] = accumulator.EffectiveHz,
            ["StartElapsedMs"] = accumulator.StartElapsedMs,
            ["EndElapsedMs"] = accumulator.EndElapsedMs,
        };
        // MagnitudeG* keys are omitted entirely (not merely null) when the source's unit basis is not
        // proven G, matching the existing AxisSummary/RestSummary convention (docs section 10).
        if (includeMagnitude)
        {
            result["MagnitudeGMean"] = accumulator.MagnitudeGMean;
            result["MagnitudeGMin"] = accumulator.MagnitudeGMin;
            result["MagnitudeGMax"] = accumulator.MagnitudeGMax;
            result["MagnitudeGSpan"] = accumulator.MagnitudeGSpan;
        }
        return result;
    })];

    // StationaryBias-only (docs section 11): a gyro zero-rate bias CANDIDATE and accelerometer
    // stability check -- never applied anywhere, purely reported for later human/offline analysis.
    private object? BuildStationaryBiasSummary(bool includeAccelMagnitudeG)
    {
        if (_mode != ClawSensorProbeMode.StationaryBias) return null;
        return new
        {
            Gyroscope = new
            {
                SampleCount = _biasGyro.Count,
                DurationMs = _biasGyro.DurationMs,
                EffectiveHz = _biasGyro.EffectiveHz,
                MeanX = _biasGyro.MeanX, MeanY = _biasGyro.MeanY, MeanZ = _biasGyro.MeanZ,
                StandardDeviationX = _biasGyro.StandardDeviationX, StandardDeviationY = _biasGyro.StandardDeviationY, StandardDeviationZ = _biasGyro.StandardDeviationZ,
                MinX = _biasGyro.MinX, MinY = _biasGyro.MinY, MinZ = _biasGyro.MinZ,
                MaxX = _biasGyro.MaxX, MaxY = _biasGyro.MaxY, MaxZ = _biasGyro.MaxZ,
                SpanX = _biasGyro.SpanX, SpanY = _biasGyro.SpanY, SpanZ = _biasGyro.SpanZ,
            },
            Accelerometer = BuildBiasAccelerometerSummary(includeAccelMagnitudeG)
        };
    }

    private object BuildBiasAccelerometerSummary(bool includeAccelMagnitudeG)
    {
        var result = new Dictionary<string, object?>
        {
            ["SampleCount"] = _biasAccel.Count,
            ["DurationMs"] = _biasAccel.DurationMs,
            ["EffectiveHz"] = _biasAccel.EffectiveHz,
            ["MeanX"] = _biasAccel.MeanX, ["MeanY"] = _biasAccel.MeanY, ["MeanZ"] = _biasAccel.MeanZ,
            ["MinX"] = _biasAccel.MinX, ["MinY"] = _biasAccel.MinY, ["MinZ"] = _biasAccel.MinZ,
            ["MaxX"] = _biasAccel.MaxX, ["MaxY"] = _biasAccel.MaxY, ["MaxZ"] = _biasAccel.MaxZ,
            ["SpanX"] = _biasAccel.SpanX, ["SpanY"] = _biasAccel.SpanY, ["SpanZ"] = _biasAccel.SpanZ,
        };
        // MagnitudeG* keys are omitted entirely (not merely null) unless the accelerometer's unit
        // basis is proven G, matching the existing AxisSummary/RestSummary/PerPhaseSummaries convention.
        if (includeAccelMagnitudeG)
        {
            result["MagnitudeGMean"] = _biasAccel.MagnitudeGMean;
            result["MagnitudeGMin"] = _biasAccel.MagnitudeGMin;
            result["MagnitudeGMax"] = _biasAccel.MagnitudeGMax;
            result["MagnitudeGSpan"] = _biasAccel.MagnitudeGSpan;
        }
        return result;
    }

    // Enums (Backend, UnitBasis, ...) must serialize as their named diagnostic values -- e.g. "WinRtGyrometer",
    // "DegreesPerSecond" -- rather than System.Text.Json's default numeric ordinals, so the standalone report
    // stays self-describing without the source enum declarations.
    private static readonly JsonSerializerOptions ReportSerializerOptions = CreateReportSerializerOptions();
    private static JsonSerializerOptions CreateReportSerializerOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
    private static object? TimingSummaryOf(ClawSensorProbeTimingSnapshot? timing) => timing is null ? null : new
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
    // MagnitudeGMean is only meaningful -- and only computed -- when the selected source's unit basis is
    // proven to be g (docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 9): the normal
    // legacy fallback path leaves UnitBasis Unknown, and a magnitude computed from an unproven unit would be
    // presented as if it meant something before CG3EM characterization confirms it.
    private static object AxisSummary(IReadOnlyList<(double X, double Y, double Z)> values, bool includeStandardDeviation, bool includeMagnitudeG = false)
    {
        if (values.Count == 0) return new { SampleCount = 0 };
        var mean = new { X = values.Average(x => x.X), Y = values.Average(x => x.Y), Z = values.Average(x => x.Z) };
        var result = new Dictionary<string, object?> { ["SampleCount"] = values.Count, ["Mean"] = mean };
        if (includeMagnitudeG) result["MagnitudeGMean"] = values.Average(x => Math.Sqrt(x.X * x.X + x.Y * x.Y + x.Z * x.Z));
        if (includeStandardDeviation) result["StandardDeviation"] = new { X = Std(values.Select(x => x.X)), Y = Std(values.Select(x => x.Y)), Z = Std(values.Select(x => x.Z)) };
        return result;
    }
    private static double Std(IEnumerable<double> source) { var values = source.ToArray(); var mean = values.Average(); return Math.Sqrt(values.Select(x => (x - mean) * (x - mean)).Average()); }
    public ValueTask DisposeAsync() => new(FinalizeAsync().AsTask());
}
