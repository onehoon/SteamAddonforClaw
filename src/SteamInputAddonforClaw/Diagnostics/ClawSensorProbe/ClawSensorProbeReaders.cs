using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeReaders : IAsyncDisposable
{
    private readonly ClawSensorProbeSensorApi _api;
    private readonly Func<ClawSensorCaptureContext> _contextProvider;
    private readonly ClawSensorProbeSessionClock _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;
    private int _stopDisposed;
    private long _sequence;
    internal ClawSensorProbeLiveSnapshot Snapshot { get; } = new();
    internal ClawSensorDiscovery Discovery { get; }
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
        _workers = [RunAsync(discovery.Gyroscope!, "GYRO", writer), RunAsync(discovery.Accelerometer!, "ACCEL", writer)];
    }
    private Task RunAsync(ClawSensorProbeCandidate candidate, string sensorName, ClawSensorProbeSessionWriter writer)
    {
        return Task.Run(() =>
        {
            IntPtr sensor = IntPtr.Zero;
            try
            {
                sensor = _api.GetSensorById(Guid.Parse(candidate.SensorId));
                if (sensor == IntPtr.Zero) throw new InvalidOperationException($"Selected {sensorName} sensor was not available.");
                var previous = 0L;
                var deduplicator = new ClawSensorReportDeduplicator();
                while (!_stop.IsCancellationRequested)
                {
                    var values = ClawSensorProbeSensorApi.ReadXYZ(sensor);
                    if (!deduplicator.ShouldAccept(values))
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    var context = _contextProvider();
                    var now = _clock.ElapsedTicks;
                    var interval = previous == 0 ? 0 : ClawSensorProbeSessionClock.TicksToMilliseconds(now - previous);
                    previous = now;
                    var elapsed = ClawSensorProbeSessionClock.TicksToMilliseconds(now);
                    writer.Write(new(Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, elapsed, context.Mode, context.Phase, context.Pass, sensorName, values.X, values.Y, values.Z, interval, values.SensorTimestamp));
                    Snapshot.Observe(sensorName, values.X, values.Y, values.Z, interval);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception) { lock (_errors) _errors.Add($"{sensorName} reader failed: {exception.Message}"); }
            finally { if (sensor != IntPtr.Zero) Marshal.Release(sensor); }
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
