namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeCoordinator : IAsyncDisposable
{
    private readonly ClawSensorProbeWorkflow _workflow = new();
    private ClawSensorProbeSessionWriter? _writer;
    private ClawSensorProbeSensorApi? _api;
    private ClawSensorProbeReaders? _readers;
    private int _disposed;
    public ClawSensorProbeState State => _workflow.State;
    public ClawSensorProbeWorkflow Workflow => _workflow;
    public string? OutputDirectory => _writer?.DirectoryPath;
    public ClawSensorProbeLiveSnapshot? LiveSnapshot => _readers?.Snapshot;
    public ClawSensorProbeStatistics? GyroscopeSummary => _writer?.GyroscopeSummary;
    public ClawSensorProbeStatistics? AccelerometerSummary => _writer?.AccelerometerSummary;
    public long DroppedSampleCount => _writer?.DroppedSampleCount ?? 0;

    public void Prepare() => _workflow.Ready();
    public void Start(string? root = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_writer is not null) throw new InvalidOperationException("The probe session has already started.");
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs", "ClawSensorProbe");
        _writer = new ClawSensorProbeSessionWriter(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        _workflow.Start();
    }
    public async Task StartCaptureAsync()
    {
        if (_writer is null) throw new InvalidOperationException("The probe session has not started.");
        await Task.Run(() => _api = new ClawSensorProbeSensorApi());
        var api = _api ?? throw new InvalidOperationException("The sensor API is unavailable.");
        _readers = await Task.Run(() => new ClawSensorProbeReaders(api, _writer, (ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.Last().Pass));
        _writer.SetDiscovery(_readers.Discovery);
        _writer.SetDiscovery(_readers.Discovery);
    }
    public async Task RestartPhaseCaptureAsync()
    {
        if (_readers is not null) { await _readers.DisposeAsync(); _readers = null; }
        if (_api is null || _writer is null) throw new InvalidOperationException("The probe session is not active.");
        var api = _api ?? throw new InvalidOperationException("The sensor API is unavailable.");
        _readers = await Task.Run(() => new ClawSensorProbeReaders(api, _writer, (ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.Last().Pass));
    }
    public void BeginRecording() => _workflow.BeginRecording();
    public void Next() => _workflow.Next();
    public void Back() => _workflow.Back();
    public void Write(ClawSensorProbeSample sample) => _writer?.Write(sample);
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _workflow.Stop();
        if (_readers is not null) { await _readers.DisposeAsync(); _readers = null; }
        _api?.Dispose(); _api = null;
        if (_writer is not null) await _writer.FinalizeAsync(cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_readers is not null) await _readers.DisposeAsync();
        _api?.Dispose();
        if (_writer is not null) await _writer.FinalizeAsync();
    }
}
