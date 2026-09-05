using System.Diagnostics;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeReaders : IAsyncDisposable
{
    // Diagnostic-only staleness label (docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md section 7):
    // a quiet/duplicate/no-data source no longer terminates the session on its own. Only an actual backend
    // read failure (handle loss, COM/WinRT exception) is terminal. This threshold exists purely so timing
    // evidence can flag a suspiciously old reading; it is not a production freshness contract.
    internal static readonly TimeSpan StaleWarningThreshold = TimeSpan.FromSeconds(5);
    private readonly ClawSensorProbeSensorApi _api;
    private readonly Func<ClawSensorCaptureContext> _contextProvider;
    private readonly ClawSensorProbeSessionClock _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;
    private int _stopDisposed;
    private long _sequence;
    internal ClawSensorProbeLiveSnapshot Snapshot { get; } = new();
    internal ClawSensorDiscovery Discovery { get; }
    internal ClawSensorProbeTimingStatistics GyroscopeTiming { get; } = new();
    internal ClawSensorProbeTimingStatistics AccelerometerTiming { get; } = new();
    internal ClawSensorProbeSourceConfiguration? GyroscopeConfiguration { get; private set; }
    internal ClawSensorProbeSourceConfiguration? AccelerometerConfiguration { get; private set; }
    internal IReadOnlyList<string> Errors { get { lock (_errors) return _errors.ToArray(); } }
    internal bool ShutdownTimedOut { get; private set; }
    internal bool HasCompleted => _workers.All(x => x.IsCompleted);
    internal Task Completion => Task.WhenAll(_workers);
    private readonly List<string> _errors = [];
    public ClawSensorProbeReaders(ClawSensorProbeSensorApi api, ClawSensorProbeSessionWriter writer, ClawSensorDiscovery discovery, Func<ClawSensorCaptureContext> contextProvider, ClawSensorProbeSessionClock clock)
    {
        _api = api;
        _contextProvider = contextProvider;
        _clock = clock;
        Discovery = discovery;
        if (!discovery.IsValid) throw new InvalidOperationException(string.Join(" ", discovery.Errors));
        _workers = [
            RunAsync(discovery.Gyroscope!, "GYRO", writer, GyroscopeTiming, config => GyroscopeConfiguration = config),
            RunAsync(discovery.Accelerometer!, "ACCEL", writer, AccelerometerTiming, config => AccelerometerConfiguration = config)];
    }

    private IClawSensorProbeSourceHandle OpenSource(ClawSensorProbeCandidate candidate) => candidate.Backend switch
    {
        ClawSensorProbeBackend.WinRtGyrometer => ClawSensorProbeWinRtSourceHandle.OpenGyrometer(),
        ClawSensorProbeBackend.WinRtAccelerometer => ClawSensorProbeWinRtSourceHandle.OpenAccelerometer(),
        _ => new ClawSensorProbeLegacySourceHandle(_api.GetSensorById(Guid.Parse(candidate.SensorId)))
    };

    // The sample's own SensorTimestamp reflects when the backend produced the reading, which can lag well
    // behind "now" under a blocking/stalled read (docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md
    // section 4/8: the WSGM ~200ms accelerometer stall this diagnostic exists to catch). This is deliberately
    // distinct from FreshAgeMs (time since the last accepted report, used for stale/quiet-source tracking) --
    // a fast, freshly-accepted sample can still carry an old sensor timestamp, and that is exactly the signal
    // sensor_age_ms must preserve.
    internal static double? ComputeSensorAgeMs(DateTimeOffset receiveUtc, DateTimeOffset sensorTimestamp) =>
        sensorTimestamp == default ? null : Math.Max(0, (receiveUtc - sensorTimestamp).TotalMilliseconds);

    private Task RunAsync(ClawSensorProbeCandidate candidate, string sensorName, ClawSensorProbeSessionWriter writer, ClawSensorProbeTimingStatistics timing, Action<ClawSensorProbeSourceConfiguration> onConfigured)
    {
        return Task.Run(() =>
        {
            IClawSensorProbeSourceHandle? handle = null;
            try
            {
                handle = OpenSource(candidate);
                onConfigured(handle.Configuration);
                var previous = 0L;
                var deduplicator = new ClawSensorReportDeduplicator();
                var lastFreshReport = _clock.ElapsedTicks;
                while (!_stop.IsCancellationRequested)
                {
                    var readStart = Stopwatch.GetTimestamp();
                    ClawSensorReportReadResult values;
                    try
                    {
                        values = handle.Read();
                    }
                    catch
                    {
                        // A blocking/stalled backend call that then fails is exactly the stall evidence this
                        // diagnostic exists to capture; record the attempt's duration before the outer catch
                        // turns this into a terminal reader error, instead of losing it as readDurationMs=0.
                        var failedReadDurationMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
                        var failedNow = _clock.ElapsedTicks;
                        var failedFreshAgeMs = ClawSensorProbeSessionClock.TicksToMilliseconds(failedNow - lastFreshReport);
                        timing.Observe(ClawSensorReadOutcome.Failure, failedReadDurationMs, failedFreshAgeMs);
                        throw;
                    }
                    var readDurationMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
                    var now = _clock.ElapsedTicks;
                    var freshAgeMs = ClawSensorProbeSessionClock.TicksToMilliseconds(now - lastFreshReport);
                    if (!values.HasData)
                    {
                        timing.Observe(ClawSensorReadOutcome.NoData, readDurationMs, freshAgeMs);
                        Thread.Sleep(1);
                        continue;
                    }
                    if (!deduplicator.ShouldAccept(values))
                    {
                        timing.Observe(ClawSensorReadOutcome.Duplicate, readDurationMs, freshAgeMs);
                        Thread.Sleep(1);
                        continue;
                    }
                    lastFreshReport = now;
                    var context = _contextProvider();
                    var interval = previous == 0 ? 0 : ClawSensorProbeSessionClock.TicksToMilliseconds(now - previous);
                    previous = now;
                    var elapsed = ClawSensorProbeSessionClock.TicksToMilliseconds(now);
                    var receiveUtc = DateTimeOffset.UtcNow;
                    var sensorAgeMs = ComputeSensorAgeMs(receiveUtc, values.SensorTimestamp);
                    timing.Observe(ClawSensorReadOutcome.Fresh, readDurationMs, freshAgeMs, interval);
                    writer.Write(new(Interlocked.Increment(ref _sequence), receiveUtc, elapsed, context.Mode, context.Phase, context.Pass, sensorName, values.X, values.Y, values.Z, interval, values.SensorTimestamp, candidate.Backend.ToString(), readDurationMs, sensorAgeMs));
                    Snapshot.Observe(sensorName, values.X, values.Y, values.Z, interval);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception)
            {
                // The failing read's duration/age is already recorded above, at the point of failure;
                // recording it again here (as readDurationMs=0) would both lose the real value and
                // double-count ReadFailureCount.
                lock (_errors) _errors.Add($"{sensorName} reader failed: {exception.Message}");
            }
            finally { handle?.Dispose(); }
        }, _stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await Completion.WaitAsync(TimeSpan.FromSeconds(3));
            DisposeStopSource();
        }
        catch (TimeoutException)
        {
            ShutdownTimedOut = true;
            lock (_errors) _errors.Add("Sensor reader shutdown exceeded the bounded wait.");
            _ = Completion.ContinueWith(_ => DisposeStopSource(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch
        {
            DisposeStopSource();
        }
    }

    private void DisposeStopSource()
    {
        if (Interlocked.Exchange(ref _stopDisposed, 1) == 0) _stop.Dispose();
    }
}
