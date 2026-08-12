using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeReaders : IAsyncDisposable
{
    private readonly ClawSensorProbeSensorApi _api;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _sequence;
    internal ClawSensorProbeLiveSnapshot Snapshot { get; } = new();
    internal ClawSensorDiscovery Discovery { get; }
    public ClawSensorProbeReaders(ClawSensorProbeSensorApi api, ClawSensorProbeSessionWriter writer, ClawSensorDiscovery discovery, ClawSensorProbePhase phase, int phasePass)
    {
        _api = api;
        Discovery = discovery;
        if (!discovery.IsValid) throw new InvalidOperationException(string.Join(" ", discovery.Errors));
        _workers = [RunAsync(discovery.Gyroscope!, "GYRO", writer, phase, phasePass), RunAsync(discovery.Accelerometer!, "ACCEL", writer, phase, phasePass)];
    }
    private Task RunAsync(ClawSensorProbeCandidate candidate, string sensorName, ClawSensorProbeSessionWriter writer, ClawSensorProbePhase phase, int phasePass)
    {
        return Task.Run(() =>
        {
            IntPtr sensor = IntPtr.Zero;
            try
            {
                var collection = _api.GetAllSensors();
                try
                {
                    for (var i = 0; i < ClawSensorProbeSensorApi.GetCollectionCount(collection); i++)
                    {
                        var item = ClawSensorProbeSensorApi.GetCollectionItem(collection, i);
                        var metadata = ClawSensorProbeSensorApi.ReadMetadata(item);
                        if (string.Equals(metadata.SensorId, candidate.SensorId, StringComparison.OrdinalIgnoreCase)) { sensor = item; break; }
                        Marshal.Release(item);
                    }
                }
                finally { Marshal.Release(collection); }
                if (sensor == IntPtr.Zero) throw new InvalidOperationException($"Selected {sensorName} sensor was not available.");
                var previous = 0L;
                while (!_stop.IsCancellationRequested)
                {
                    var values = ClawSensorProbeSensorApi.ReadXYZ(sensor);
                    var now = _clock.ElapsedTicks;
                    var interval = previous == 0 ? 0 : (now - previous) * 1000d / Stopwatch.Frequency;
                    previous = now;
                    var elapsed = now * 1000d / Stopwatch.Frequency;
                    writer.Write(new(Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, elapsed, phase, phasePass, sensorName, values.X, values.Y, values.Z, interval));
                    Snapshot.Observe(sensorName, values.X, values.Y, values.Z, interval);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { if (sensor != IntPtr.Zero) Marshal.Release(sensor); }
        }, _stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        _stop.Dispose();
    }
}
