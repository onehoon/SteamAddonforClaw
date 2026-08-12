namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using System.Diagnostics;

internal sealed class ClawSensorProbeCoordinator : IAsyncDisposable
{
    private readonly ClawSensorProbeWorkflow _workflow = new();
    private ClawSensorProbeSessionWriter? _writer;
    private ClawSensorProbeSensorApi? _api;
    private ClawSensorProbeReaders? _readers;
    private readonly List<string> _readerErrors = [];
    private bool _apiReleaseDeferred;
    private void DeferApiRelease(ClawSensorProbeReaders readers, ClawSensorProbeSensorApi api)
    {
        _api = null;
        _ = readers.Completion.ContinueWith(_ => api.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private int _disposed;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    public CancellationToken LifecycleCancellation => _lifecycleCancellation.Token;
    public ClawSensorProbeState State => _workflow.State;
    public ClawSensorProbeWorkflow Workflow => _workflow;
    public string? OutputDirectory => _writer?.DirectoryPath;
    public bool HasReport => _writer is not null && File.Exists(Path.Combine(_writer.DirectoryPath, "claw-sensor-report.json"));
    public ClawSensorProbeLiveSnapshot? LiveSnapshot => _readers?.Snapshot;
    public ClawSensorProbeStatistics? GyroscopeSummary => _writer?.GyroscopeSummary;
    public ClawSensorProbeStatistics? AccelerometerSummary => _writer?.AccelerometerSummary;
    public long DroppedSampleCount => _writer?.DroppedSampleCount ?? 0;
    public long DroppedGyroscopeCount => _writer?.DroppedGyroscopeCount ?? 0;
    public long DroppedAccelerometerCount => _writer?.DroppedAccelerometerCount ?? 0;
    public IReadOnlyList<string> ReaderErrors { get { if (_readers is not null) return _readers.Errors; lock (_readerErrors) return _readerErrors.ToArray(); } }
    public ClawSensorDiscovery? Discovery => _readers?.Discovery;

    public void Prepare() { _workflow.Discovering(); _workflow.Ready(); }
    public void Start(string? root = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_writer is not null) throw new InvalidOperationException("The probe session has already started.");
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs", "ClawSensorProbe");
        var sessionId = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        _writer = new ClawSensorProbeSessionWriter(root, sessionId);
        _workflow.Start();
    }
    public void SetDeviceIdentity(string manufacturer, string productName, string baseBoardProduct, string resolvedModel)
    {
        _writer?.SetDevice(new { Manufacturer = manufacturer, ProductName = productName, SystemProductName = productName, BaseBoardProduct = baseBoardProduct, ResolvedAddonDevice = "MSI Claw", ResolvedAddonModel = resolvedModel });
    }
    public void SetHardwareCompatibility(string status, string family, string model, string reason) => _writer?.SetCompatibility(new { Status = status, DeviceFamily = family, DeviceModel = model, Reason = reason });
    public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("The probe session has not started.");
        try
        {
            await Task.Run(() => _api = new ClawSensorProbeSensorApi());
            var api = _api ?? throw new InvalidOperationException("The sensor API is unavailable.");
            var discovery = await Task.Run(api.Discover);
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(discovery);
            if (!discovery.IsValid) throw new InvalidOperationException(string.Join(" ", discovery.Errors));
            _readers = await Task.Run(() => new ClawSensorProbeReaders(api, _writer, discovery, (ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.Last().Pass));
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(_readers.Discovery);
        }
        catch (Exception exception)
        {
            _writer.AddError(exception.Message);
            if (_readers is not null)
            {
                var readers = _readers;
                await readers.DisposeAsync();
                foreach (var readerError in readers.Errors) { lock (_readerErrors) _readerErrors.Add(readerError); _writer.AddError(readerError); }
                if (readers.ShutdownTimedOut) { _writer.MarkShutdownTimedOut(); _apiReleaseDeferred = true; }
                _readers = null;
            }
            if (_apiReleaseDeferred && _readers is not null && _api is not null) DeferApiRelease(_readers, _api);
            else if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
            await _writer.FinalizeAsync(CancellationToken.None);
            throw;
        }
    }
    public async Task RestartPhaseCaptureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_readers is not null) { var readers = _readers; await readers.DisposeAsync(); foreach (var error in readers.Errors) { lock (_readerErrors) _readerErrors.Add(error); _writer?.AddError(error); } if (readers.ShutdownTimedOut) { _writer?.MarkShutdownTimedOut(); _apiReleaseDeferred = true; } _readers = null; }
            if (_api is null || _writer is null) throw new InvalidOperationException("The probe session is not active.");
            var api = _api;
            var discovery = await Task.Run(api.Discover);
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(discovery);
            if (!discovery.IsValid) throw new InvalidOperationException(string.Join(" ", discovery.Errors));
            _readers = await Task.Run(() => new ClawSensorProbeReaders(api, _writer, discovery, (ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.Last().Pass));
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(_readers.Discovery);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException) _workflow.Fail();
            if (_writer is not null)
            {
                _writer.AddError(exception.Message);
                if (_readers is not null)
                {
                    var readers = _readers;
                    await readers.DisposeAsync();
                    foreach (var error in readers.Errors) { lock (_readerErrors) _readerErrors.Add(error); _writer.AddError(error); }
                    if (readers.ShutdownTimedOut) { _writer.MarkShutdownTimedOut(); _apiReleaseDeferred = true; }
                    _readers = null;
                }
                if (_apiReleaseDeferred && _readers is not null && _api is not null) DeferApiRelease(_readers, _api);
                else if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
                await _writer.FinalizeAsync(CancellationToken.None);
            }
            throw;
        }
    }
    public void BeginRecording() => _workflow.BeginRecording();
    public void Next() => _workflow.Next();
    public void Back() => _workflow.Back();
    public void Write(ClawSensorProbeSample sample) => _writer?.Write(sample);
    public double ElapsedMs => _clock.Elapsed.TotalMilliseconds;
    public void WriteTransition() => _writer?.WriteTransition((ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.LastOrDefault().Pass, ElapsedMs);
    public void EndCurrentPhase() => _writer?.EndPhase((ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.LastOrDefault().Pass, ElapsedMs);
    public void BeginPhaseTransition() => WriteTransition();
    public void AdvancePhase() { EndCurrentPhase(); WriteTransition(); Next(); WriteTransition(); }
    public void RevisitPreviousPhase() { if (State == ClawSensorProbeState.RecordingPhase) { EndCurrentPhase(); WriteTransition(); } Back(); if (State == ClawSensorProbeState.Countdown) WriteTransition(); }
    public async Task CountdownAsync(Func<string, Task> updateStatus, Func<ClawSensorProbePhase, string> phaseLabel, CancellationToken cancellationToken)
    {
        var label = phaseLabel(Workflow.Visits.Last().Phase);
        for (var count = 3; count >= 1; count--)
        {
            BeginPhaseTransition();
            await updateStatus($"Get ready for {label}. {count}");
            await Task.Delay(1000, cancellationToken);
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await StopCoreAsync(cancellationToken); }
        finally { _lifecycleGate.Release(); }
    }
    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        _lifecycleCancellation.Cancel();
        if (_workflow.State == ClawSensorProbeState.RecordingPhase) EndCurrentPhase();
        _workflow.Stop();
        if (_readers is not null)
        {
            var readers = _readers;
            await readers.DisposeAsync();
            foreach (var error in readers.Errors) { lock (_readerErrors) _readerErrors.Add(error); _writer?.AddError(error); }
            if (readers.ShutdownTimedOut) { _writer?.MarkShutdownTimedOut(); _apiReleaseDeferred = true; if (_api is not null) DeferApiRelease(readers, _api); }
            _readers = null;
        }
        if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
        if (_writer is not null) await _writer.FinalizeAsync(CancellationToken.None);
    }
    public async Task FailAsync(string error, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await FailCoreAsync(error, cancellationToken); }
        finally { _lifecycleGate.Release(); }
    }
    private async Task FailCoreAsync(string error, CancellationToken cancellationToken)
    {
        if (_workflow.State == ClawSensorProbeState.RecordingPhase) EndCurrentPhase();
        _workflow.Fail();
        _writer?.AddError(error);
        if (_readers is not null) { var readers = _readers; await readers.DisposeAsync(); foreach (var readerError in readers.Errors) { lock (_readerErrors) _readerErrors.Add(readerError); _writer?.AddError(readerError); } if (readers.ShutdownTimedOut) { _writer?.MarkShutdownTimedOut(); _apiReleaseDeferred = true; if (_api is not null) DeferApiRelease(readers, _api); } _readers = null; }
        if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
        if (_writer is not null) await _writer.FinalizeAsync(CancellationToken.None);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifecycleGate.WaitAsync();
        try
        {
            _lifecycleCancellation.Cancel();
            if (_readers is not null) { var readers = _readers; await readers.DisposeAsync(); foreach (var error in readers.Errors) { lock (_readerErrors) _readerErrors.Add(error); _writer?.AddError(error); } if (readers.ShutdownTimedOut) { _writer?.MarkShutdownTimedOut(); _apiReleaseDeferred = true; if (_api is not null) DeferApiRelease(readers, _api); } _readers = null; }
            if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
            if (_writer is not null) await _writer.FinalizeAsync();
        }
        finally
        {
            _lifecycleCancellation.Dispose();
            _lifecycleGate.Release();
        }
    }
}
