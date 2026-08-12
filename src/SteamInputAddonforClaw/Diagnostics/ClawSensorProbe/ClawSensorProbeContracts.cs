using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal enum ClawSensorProbePhase { REST, ROLL_LEFT, ROLL_RIGHT, PITCH_UP, PITCH_DOWN, YAW_LEFT, YAW_RIGHT }
internal enum ClawSensorProbeState { Idle, Discovering, Ready, Starting, Countdown, RecordingPhase, Stopping, Completed, Failed }

internal sealed record ClawSensorProbeCandidate(string FriendlyName, string SensorId, string TypeGuid, string CategoryGuid, string Manufacturer = "Unavailable", string Model = "Unavailable", string PersistentUniqueId = "Unavailable", string MinimumReportInterval = "Unavailable", string CustomUsage = "Unavailable");
internal sealed record ClawSensorDiscovery(IReadOnlyList<ClawSensorProbeCandidate> Sensors, ClawSensorProbeCandidate? Gyroscope, ClawSensorProbeCandidate? Accelerometer, IReadOnlyList<string> Errors)
{
    public bool IsValid => Gyroscope is not null && Accelerometer is not null && Errors.Count == 0;
    public static ClawSensorDiscovery Select(IReadOnlyList<ClawSensorProbeCandidate> sensors)
    {
        var gyros = sensors.Where(x => string.Equals(x.FriendlyName, "Physical Gyrometer", StringComparison.OrdinalIgnoreCase)).ToArray();
        var accels = sensors.Where(x => string.Equals(x.FriendlyName, "Physical Accelerometer", StringComparison.OrdinalIgnoreCase)).ToArray();
        var errors = new List<string>();
        if (gyros.Length != 1) errors.Add(gyros.Length == 0 ? "No Physical Gyrometer was found." : "Multiple Physical Gyrometer candidates were found.");
        if (accels.Length != 1) errors.Add(accels.Length == 0 ? "No Physical Accelerometer was found." : "Multiple Physical Accelerometer candidates were found.");
        return new(sensors, gyros.Length == 1 ? gyros[0] : null, accels.Length == 1 ? accels[0] : null, errors);
    }
}

internal sealed record ClawSensorProbeSample(long Sequence, DateTimeOffset UtcTimestamp, double ElapsedMs, ClawSensorProbePhase Phase, int PhasePass, string Sensor, double X, double Y, double Z, double SampleIntervalMs, long? SensorTimestamp = null);
internal sealed record ClawSensorProbePhaseLog(string name, int pass, double start_elapsed_ms, double end_elapsed_ms, long sample_count, string capture_status);
internal sealed record ClawSensorProbeError(string Code, string Message);

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
        _csv.WriteLine("sequence,utc_timestamp,elapsed_ms,phase,phase_pass,sensor,x,y,z,sample_interval_ms,sensor_timestamp");
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
    public void WriteTransition(ClawSensorProbePhase phase, int pass, double elapsedMs) { EnsurePhaseStarted(phase, pass, elapsedMs); Write(new(0, DateTimeOffset.UtcNow, elapsedMs, phase, pass, "TRANSITION", 0, 0, 0, 0)); }
    private void EnsurePhaseStarted(ClawSensorProbePhase phase, int pass, double elapsedMs)
    {
        var key = $"{phase}:{pass}";
        lock (_phaseGate)
        {
            if (_phaseRows.ContainsKey(key)) return;
            _phaseRows[key] = _phases.Count;
            var end = _pendingPhaseEnds.TryGetValue(key, out var pendingEnd) ? pendingEnd : elapsedMs;
            _phases.Add(new(phase.ToString(), pass, elapsedMs, end, 0, "TransitionOnly"));
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
            _csv.WriteLine(string.Join(',', sample.Sequence.ToString(CultureInfo.InvariantCulture), sample.UtcTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), sample.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture), sample.Phase, sample.PhasePass, sample.Sensor, sample.X.ToString("R", CultureInfo.InvariantCulture), sample.Y.ToString("R", CultureInfo.InvariantCulture), sample.Z.ToString("R", CultureInfo.InvariantCulture), sample.SampleIntervalMs.ToString("0.###", CultureInfo.InvariantCulture), sample.SensorTimestamp?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            if (sample.Sensor is "GYRO" or "ACCEL") (sample.Sensor == "GYRO" ? _gyro : _accel).Add(sample.SampleIntervalMs);
            if (sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "GYRO") _restGyro.Add((sample.X, sample.Y, sample.Z));
            if (sample.Phase == ClawSensorProbePhase.REST && sample.Sensor == "ACCEL") _restAccel.Add((sample.X, sample.Y, sample.Z));
            var key = $"{sample.Phase}:{sample.PhasePass}";
            lock (_phaseGate)
            {
                if (!_phaseRows.ContainsKey(key))
                {
                    _phaseRows[key] = _phases.Count;
                    var end = _pendingPhaseEnds.TryGetValue(key, out var pendingEnd) ? pendingEnd : sample.ElapsedMs;
                    _phases.Add(new(sample.Phase.ToString(), sample.PhasePass, sample.ElapsedMs, end, 0, "TransitionOnly"));
                }
                if (sample.Sensor is "GYRO" or "ACCEL")
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
        var report = new { SchemaVersion = 1, SessionId = Path.GetFileName(DirectoryPath), AppVersion = typeof(ClawSensorProbeSessionWriter).Assembly.GetName().Version?.ToString() ?? "Unknown", StartUtc = Directory.GetCreationTimeUtc(DirectoryPath), EndUtc = DateTime.UtcNow, Device = _device, ResolvedHardware = _compatibility, Backend = "Windows Sensor API / ISensorManager", SensorDiscovery = _discovery?.Sensors, SelectedGyroscope = _discovery?.Gyroscope, SelectedAccelerometer = _discovery?.Accelerometer, DataKeys = new { Guid = "B14C764F-07CF-41E8-9D82-EBE3D0776A6F", X = 7, Y = 8, Z = 9 }, Phases = _phases, RestSummary = new { Gyroscope = AxisSummary(_restGyro, true), Accelerometer = AxisSummary(_restAccel, false) }, GyroscopeSummary = _gyro, AccelerometerSummary = _accel, DroppedSampleCount = DroppedSampleCount, DroppedGyroscopeCount = DroppedGyroscopeCount, DroppedAccelerometerCount = DroppedAccelerometerCount, ShutdownTimedOut = _shutdownTimedOut, Errors = errors, Warnings = warnings };
        await File.WriteAllTextAsync(_reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
    }
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
