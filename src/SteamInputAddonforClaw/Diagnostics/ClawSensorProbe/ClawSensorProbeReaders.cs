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
        _workers = [RunAsync(discovery.Gyroscope!, "GYRO", writer, GyroscopeTiming), RunAsync(discovery.Accelerometer!, "ACCEL", writer, AccelerometerTiming)];
    }

    private IClawSensorProbeSourceHandle OpenSource(ClawSensorProbeCandidate candidate) => candidate.Backend switch
    {
        ClawSensorProbeBackend.WinRtGyrometer => ClawSensorProbeWinRtSourceHandle.OpenGyrometer(),
        ClawSensorProbeBackend.WinRtAccelerometer => ClawSensorProbeWinRtSourceHandle.OpenAccelerometer(),
        _ => new ClawSensorProbeLegacySourceHandle(_api.GetSensorById(Guid.Parse(candidate.SensorId)))
    };

    private Task RunAsync(ClawSensorProbeCandidate candidate, string sensorName, ClawSensorProbeSessionWriter writer, ClawSensorProbeTimingStatistics timing)
    {
        return Task.Run(() =>
        {
            IClawSensorProbeSourceHandle? handle = null;
            try
            {
                handle = OpenSource(candidate);
                var previous = 0L;
                var deduplicator = new ClawSensorReportDeduplicator();
                var lastFreshReport = _clock.ElapsedTicks;
                while (!_stop.IsCancellationRequested)
                {
                    var readStart = Stopwatch.GetTimestamp();
                    var values = handle.Read();
                    var readDurationMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
                    var now = _clock.ElapsedTicks;
                    var ageMs = ClawSensorProbeSessionClock.TicksToMilliseconds(now - lastFreshReport);
                    if (!values.HasData)
                    {
                        timing.Observe(ClawSensorReadOutcome.NoData, readDurationMs, ageMs);
                        Thread.Sleep(1);
                        continue;
                    }
                    if (!deduplicator.ShouldAccept(values))
                    {
                        timing.Observe(ClawSensorReadOutcome.Duplicate, readDurationMs, ageMs);
                        Thread.Sleep(1);
                        continue;
                    }
                    lastFreshReport = now;
                    var context = _contextProvider();
                    var interval = previous == 0 ? 0 : ClawSensorProbeSessionClock.TicksToMilliseconds(now - previous);
                    previous = now;
                    var elapsed = ClawSensorProbeSessionClock.TicksToMilliseconds(now);
                    timing.Observe(ClawSensorReadOutcome.Fresh, readDurationMs, ageMs, interval);
                    writer.Write(new(Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, elapsed, context.Mode, context.Phase, context.Pass, sensorName, values.X, values.Y, values.Z, interval, values.SensorTimestamp, candidate.Backend.ToString(), readDurationMs, ageMs));
                    Snapshot.Observe(sensorName, values.X, values.Y, values.Z, interval);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception)
            {
                timing.Observe(ClawSensorReadOutcome.Failure, 0);
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
