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
internal sealed record ClawSensorProbePhaseLog(string name, int pass, double start_elapsed_ms, double end_elapsed_ms);
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
    public (double X, double Y, double Z, double Hz, long Count) Gyro { get { lock (_gate) return (_gyroX, _gyroY, _gyroZ, _gyroHz, _gyroCount); } }
    public (double X, double Y, double Z, double Hz, long Count) Accel { get { lock (_gate) return (_accelX, _accelY, _accelZ, _accelHz, _accelCount); } }
    public void Observe(string sensor, double x, double y, double z, double intervalMs)
    {
        lock (_gate)
        {
            var hz = intervalMs > 0 ? 1000d / intervalMs : 0;
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
    private readonly List<ClawSensorProbePhaseLog> _phases = [];
    private readonly Dictionary<string, int> _phaseRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _pendingPhaseEnds = new(StringComparer.Ordinal);
    private readonly object _phaseGate = new();
    private ClawSensorDiscovery? _discovery;
    private object _device = new { Manufacturer = "Unavailable", ProductName = "Unavailable", BaseBoardProduct = "Unavailable", ResolvedAddonDevice = "MSI Claw", ResolvedAddonModel = "Unknown / unresolved" };
    private readonly List<string> _errors = [];
    private bool _finalized;
    private long _dropped;
    private long _droppedGyro;
    private long _droppedAccel;
    public long DroppedSampleCount => Interlocked.Read(ref _dropped);
    public long DroppedGyroscopeCount => Interlocked.Read(ref _droppedGyro);
    public long DroppedAccelerometerCount => Interlocked.Read(ref _droppedAccel);
    public string DirectoryPath { get; }
    public ClawSensorProbeStatistics GyroscopeSummary => _gyro;
    public ClawSensorProbeStatistics AccelerometerSummary => _accel;
    public void SetDiscovery(ClawSensorDiscovery discovery) => _discovery = discovery;
    public void SetDevice(object device) => _device = device;
    public void AddError(string error) { lock (_errors) _errors.Add(error); }
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
        if (_finalized || !_channel.Writer.TryWrite(sample)) { Interlocked.Increment(ref _dropped); if (sample.Sensor == "GYRO") Interlocked.Increment(ref _droppedGyro); if (sample.Sensor == "ACCEL") Interlocked.Increment(ref _droppedAccel); }
    }
    public void WriteTransition(ClawSensorProbePhase phase, int pass, double elapsedMs) => Write(new(0, DateTimeOffset.UtcNow, elapsedMs, phase, pass, "TRANSITION", 0, 0, 0, 0));
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
            var key = $"{sample.Phase}:{sample.PhasePass}";
            lock (_phaseGate)
            {
                if (!_phaseRows.ContainsKey(key))
                {
                    _phaseRows[key] = _phases.Count;
                    var end = _pendingPhaseEnds.TryGetValue(key, out var pendingEnd) ? pendingEnd : sample.ElapsedMs;
                    _phases.Add(new(sample.Phase.ToString(), sample.PhasePass, sample.ElapsedMs, end));
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
        var report = new { SchemaVersion = 1, SessionId = Path.GetFileName(DirectoryPath), AppVersion = typeof(ClawSensorProbeSessionWriter).Assembly.GetName().Version?.ToString() ?? "Unknown", StartUtc = Directory.GetCreationTimeUtc(DirectoryPath), EndUtc = DateTime.UtcNow, Device = _device, Backend = "Windows Sensor API / ISensorManager", SensorDiscovery = _discovery?.Sensors, SelectedGyroscope = _discovery?.Gyroscope, SelectedAccelerometer = _discovery?.Accelerometer, DataKeys = new { Guid = "B14C764F-07CF-41E8-9D82-EBE3D0776A6F", X = 7, Y = 8, Z = 9 }, Phases = _phases, RestSummary = new { Gyroscope = "Not computed", Accelerometer = "Not computed" }, GyroscopeSummary = _gyro, AccelerometerSummary = _accel, DroppedSampleCount = DroppedSampleCount, DroppedGyroscopeCount = DroppedGyroscopeCount, DroppedAccelerometerCount = DroppedAccelerometerCount, Errors = errors, Warnings = DroppedSampleCount == 0 ? Array.Empty<string>() : new[] { "The diagnostic writer queue was full and samples were dropped." } };
        await File.WriteAllTextAsync(_reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
    }
    public ValueTask DisposeAsync() => new(FinalizeAsync().AsTask());
}
