namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Devices;

internal sealed class ClawSensorProbeCoordinator : IAsyncDisposable
{
    internal static bool AllowsReadOnlyDiagnostic(HardwareCompatibilityAssessment? hardware) =>
        string.Equals(hardware?.DeviceFamily?.Value, "msi.claw", StringComparison.OrdinalIgnoreCase);
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
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private int _disposed;
    private ClawSensorProbeSessionClock? _clock;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private ClawSensorCaptureContext _captureContext = new(ClawSensorCaptureMode.Transition, ClawSensorProbePhase.REST, 1);
    private ClawSensorProbeMode _mode = ClawSensorProbeMode.AxisCharacterization;
    private double? _recordingStartedElapsedMs;
    private double? _recordingEndedElapsedMs;
    public CancellationToken LifecycleCancellation => _lifecycleCancellation.Token;
    public ClawSensorProbeState State => _workflow.State;
    public ClawSensorProbeWorkflow Workflow => _workflow;
    public ClawSensorProbeMode Mode => _mode;
    public string? OutputDirectory => _writer?.DirectoryPath;
    public bool HasReport => _writer is not null && File.Exists(Path.Combine(_writer.DirectoryPath, "claw-sensor-report.json"));
    public ClawSensorProbeLiveSnapshot? LiveSnapshot => _readers?.Snapshot;
    public ClawSensorProbeStatistics? GyroscopeSummary => _writer?.GyroscopeSummary;
    public ClawSensorProbeStatistics? AccelerometerSummary => _writer?.AccelerometerSummary;
    // Live while readers are active; falls back to the writer's frozen teardown-boundary snapshot once
    // readers have gone away (Stop/Fail/Completed) -- see work order section 18. Narrow read-only
    // accessors only: timing ownership stays with ClawSensorProbeReaders/the writer.
    public ClawSensorProbeTimingSnapshot? GyroscopeTiming => _readers is { } r ? r.GyroscopeTiming.Snapshot() : _writer?.GyroscopeTimingSnapshot;
    public ClawSensorProbeTimingSnapshot? AccelerometerTiming => _readers is { } r ? r.AccelerometerTiming.Snapshot() : _writer?.AccelerometerTimingSnapshot;
    public ClawSensorProbeBiasSummarySnapshot? BiasSummary => _writer?.BiasSummary;
    public long DroppedSampleCount => _writer?.DroppedSampleCount ?? 0;
    public long DroppedGyroscopeCount => _writer?.DroppedGyroscopeCount ?? 0;
    public long DroppedAccelerometerCount => _writer?.DroppedAccelerometerCount ?? 0;
    public IReadOnlyList<string> ReaderErrors { get { if (_readers is not null) return _readers.Errors; lock (_readerErrors) return _readerErrors.ToArray(); } }
    public ClawSensorDiscovery? Discovery => _readers?.Discovery;
    public ClawSensorCaptureContext CaptureContext => Volatile.Read(ref _captureContext);

    public void Prepare() { _workflow.Discovering(); _workflow.Ready(); }
    // Mode-less overload preserved for existing PR-A call sites/tests -- defaults to
    // AxisCharacterization, exactly the only mode that existed before PR B.
    public void Start(string? root = null) => Start(ClawSensorProbeMode.AxisCharacterization, root);
    public void Start(ClawSensorProbeMode mode, string? root = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_writer is not null) throw new InvalidOperationException("The probe session has already started.");
        _mode = mode;
        root ??= Path.Combine(AppLog.DirectoryPath, "ClawSensorProbe");
        var sessionId = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        _writer = new ClawSensorProbeSessionWriter(root, sessionId, mode);
        _clock = new ClawSensorProbeSessionClock();
        _workflow.Start(mode);
    }
    public void SetDeviceIdentity(string manufacturer, string productName, string baseBoardProduct, string resolvedModel)
    {
        _writer?.SetDevice(new { Manufacturer = manufacturer, ProductName = productName, SystemProductName = productName, BaseBoardProduct = baseBoardProduct, ResolvedAddonDevice = "MSI Claw", ResolvedAddonModel = resolvedModel });
    }
    public void SetHardwareCompatibility(string status, string family, string model, string reason) => _writer?.SetCompatibility(new { Status = status, DeviceFamily = family, DeviceModel = model, Reason = reason });
    public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_writer is null) throw new InvalidOperationException("The probe session has not started.");
            await Task.Run(() => _api = new ClawSensorProbeSensorApi());
            var api = _api ?? throw new InvalidOperationException("The sensor API is unavailable.");
            var discovery = await Task.Run(api.Discover);
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(discovery);
            if (!discovery.IsValid) throw new InvalidOperationException(string.Join(" ", discovery.Errors));
            if (_mode == ClawSensorProbeMode.AxisCharacterization)
            {
                var visit = Workflow.Visits.Last();
                SetCaptureContext(ClawSensorCaptureMode.Transition, visit.Phase, visit.Pass);
            }
            else
            {
                // Live Sanity / Stationary Bias have no real axis phase -- REST/1 here is only an
                // internal placeholder for the sample record's required Phase/Pass fields; the writer's
                // CSV/report projection is mode-aware and never surfaces it as real phase evidence.
                SetCaptureContext(ClawSensorCaptureMode.Transition, ClawSensorProbePhase.REST, 1);
            }
            var clock = _clock ?? throw new InvalidOperationException("The probe session clock has not started.");
            _readers = await Task.Run(() => new ClawSensorProbeReaders(api, _writer, discovery, () => CaptureContext, clock));
            cancellationToken.ThrowIfCancellationRequested();
            _writer.SetDiscovery(_readers.Discovery);
        }
        catch (Exception exception)
        {
            _writer?.AddError(exception.Message);
            await ShutdownReadersAndApiAsync(CancellationToken.None);
            if (_writer is not null) await _writer.FinalizeAsync(CancellationToken.None);
            throw;
        }
        finally { _lifecycleGate.Release(); }
    }
    public void BeginRecording()
    {
        ThrowIfReaderFaulted();
        _workflow.BeginRecording();
        _recordingStartedElapsedMs ??= ElapsedMs;
        if (_mode == ClawSensorProbeMode.AxisCharacterization)
        {
            var visit = Workflow.Visits.Last();
            _writer?.BeginRecordingPhase(visit.Phase, visit.Pass, ElapsedMs);
            SetCaptureContext(ClawSensorCaptureMode.Recording, visit.Phase, visit.Pass);
        }
        else
        {
            // Live/Bias: RecordingPhase begins immediately after discovery, with no
            // BeginRecordingPhase() phase-log entry -- there is no real phase to log (section 8).
            SetCaptureContext(ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1);
        }
    }
    public void Next() => _workflow.Next();
    public void Back() => _workflow.Back();
    public void Write(ClawSensorProbeSample sample) => _writer?.Write(sample);
    public double ElapsedMs => _clock?.ElapsedMs ?? 0;
    // Recording-relative capture duration for the frontend's "elapsed capture time" evidence -- unlike
    // ElapsedMs (the absolute session clock, used for CSV/report phase timestamps and still running
    // pre-discovery and after Stop/Fail), this excludes pre-recording discovery/countdown time and
    // freezes at teardown instead of continuing to grow while the completed-session UI keeps polling
    // (PR B review follow-up finding #2).
    public double RecordingElapsedMs
    {
        get
        {
            if (_recordingStartedElapsedMs is not { } start) return 0;
            var end = _recordingEndedElapsedMs ?? ElapsedMs;
            return Math.Max(0, end - start);
        }
    }
    private void FreezeRecordingElapsed()
    {
        if (_recordingStartedElapsedMs is not null && _recordingEndedElapsedMs is null)
            _recordingEndedElapsedMs = ElapsedMs;
    }
    public void WriteTransition() => _writer?.WriteTransition(Workflow.Visits.LastOrDefault().Phase, Workflow.Visits.LastOrDefault().Pass, ElapsedMs);
    public void EndCurrentPhase() => _writer?.EndPhase((ClawSensorProbePhase)Workflow.CurrentIndex, Workflow.Visits.LastOrDefault().Pass, ElapsedMs);
    public void BeginPhaseTransition() => WriteTransition();
    public void AdvancePhase()
    {
        ThrowIfReaderFaulted();
        var current = Workflow.Visits.Last();
        SetCaptureContext(ClawSensorCaptureMode.Transition, current.Phase, current.Pass);
        EndCurrentPhase();
        Next();
        if (State == ClawSensorProbeState.Countdown)
        {
            var visit = Workflow.Visits.Last();
            SetCaptureContext(ClawSensorCaptureMode.Transition, visit.Phase, visit.Pass);
            WriteTransition();
        }
    }
    public void RevisitPreviousPhase()
    {
        ThrowIfReaderFaulted();
        if (State == ClawSensorProbeState.RecordingPhase)
        {
            var current = Workflow.Visits.Last();
            SetCaptureContext(ClawSensorCaptureMode.Transition, current.Phase, current.Pass);
            EndCurrentPhase();
        }
        Back();
        if (State == ClawSensorProbeState.Countdown)
        {
            var visit = Workflow.Visits.Last();
            SetCaptureContext(ClawSensorCaptureMode.Transition, visit.Phase, visit.Pass);
            WriteTransition();
        }
    }
    public async Task CountdownAsync(Func<string, Task> updateStatus, Func<ClawSensorProbePhase, string> phaseLabel, CancellationToken cancellationToken)
    {
        ThrowIfReaderFaulted();
        var label = phaseLabel(Workflow.Visits.Last().Phase);
        var visit = Workflow.Visits.Last();
        SetCaptureContext(ClawSensorCaptureMode.Transition, visit.Phase, visit.Pass);
        for (var count = 3; count >= 1; count--)
        {
            BeginPhaseTransition();
            await updateStatus($"Get ready for {label}. {count}");
            await Task.Delay(1000, cancellationToken);
        }
    }
    private void SetCaptureContext(ClawSensorCaptureMode mode, ClawSensorProbePhase phase, int pass) => Volatile.Write(ref _captureContext, new(mode, phase, pass));
    public void ThrowIfReaderFaulted()
    {
        if (_readers is null) return;
        var errors = _readers.Errors;
        if (errors.Count == 0) return;
        foreach (var error in errors) { lock (_readerErrors) if (!_readerErrors.Contains(error, StringComparer.Ordinal)) _readerErrors.Add(error); _writer?.AddError(error); }
        throw new InvalidOperationException(string.Join(" ", errors));
    }
    public async Task FailOnReaderFaultAsync(CancellationToken cancellationToken = default)
    {
        var errors = _readers?.Errors;
        if (errors is null || errors.Count == 0 || State == ClawSensorProbeState.Failed) return;
        await FailAsync(string.Join(" ", errors), cancellationToken);
    }
    public async Task AdvancePhaseAsync(Func<string, Task> updateStatus, Func<ClawSensorProbePhase, string> phaseLabel, Action updatePhaseUi, CancellationToken cancellationToken)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            // Next()/Back() remain Axis-only operations (work order section 7) -- a Live/Bias session
            // must not be mutated by phase navigation, and AdvancePhase()/RevisitPreviousPhase() below
            // assume Workflow.Visits.Last() exists, which is never true outside Axis mode.
            if (_mode != ClawSensorProbeMode.AxisCharacterization) return;
            if (State != ClawSensorProbeState.RecordingPhase) return;
            AdvancePhase();
            updatePhaseUi();
            if (State == ClawSensorProbeState.Countdown)
            {
                await CountdownAsync(updateStatus, phaseLabel, cancellationToken);
                BeginRecording();
            }
        }
        finally { _navigationGate.Release(); }
    }
    public async Task RevisitPreviousPhaseAsync(Func<string, Task> updateStatus, Func<ClawSensorProbePhase, string> phaseLabel, Action updatePhaseUi, CancellationToken cancellationToken)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            if (_mode != ClawSensorProbeMode.AxisCharacterization) return;
            RevisitPreviousPhase();
            updatePhaseUi();
            if (State == ClawSensorProbeState.Countdown)
            {
                await CountdownAsync(updateStatus, phaseLabel, cancellationToken);
                BeginRecording();
            }
        }
        finally { _navigationGate.Release(); }
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
        FreezeRecordingElapsed();
        if (_workflow.State == ClawSensorProbeState.RecordingPhase)
        {
            if (_mode == ClawSensorProbeMode.AxisCharacterization)
            {
                var current = Workflow.Visits.Last();
                SetCaptureContext(ClawSensorCaptureMode.Inactive, current.Phase, current.Pass);
                EndCurrentPhase();
            }
            else
            {
                SetCaptureContext(ClawSensorCaptureMode.Inactive, ClawSensorProbePhase.REST, 1);
            }
        }
        _workflow.Stop();
        await ShutdownReadersAndApiAsync(cancellationToken);
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
        _lifecycleCancellation.Cancel();
        FreezeRecordingElapsed();
        if (_workflow.State == ClawSensorProbeState.RecordingPhase)
        {
            if (_mode == ClawSensorProbeMode.AxisCharacterization)
            {
                var current = Workflow.Visits.Last();
                SetCaptureContext(ClawSensorCaptureMode.Inactive, current.Phase, current.Pass);
                EndCurrentPhase();
            }
            else
            {
                SetCaptureContext(ClawSensorCaptureMode.Inactive, ClawSensorProbePhase.REST, 1);
            }
        }
        _workflow.Fail();
        _writer?.AddError(error);
        // Terminal-failure teardown/finalization is intentionally non-cancellable, not the caller's
        // token: the Cancel() above fires _lifecycleCancellation as part of entering Failed, so a
        // caller-supplied token linked to (or equal to) that same source would self-cancel this
        // cleanup and skip FinalizeAsync() -- leaving the workflow Failed with no report ever written
        // (PR #290 re-review). Once failure has begun, it must run to completion regardless of
        // cancellation; the caller's token already gated *entering* FailAsync via _lifecycleGate.WaitAsync.
        await ShutdownReadersAndApiAsync(CancellationToken.None);
        if (_writer is not null) await _writer.FinalizeAsync(CancellationToken.None);
    }
    private async Task ShutdownReadersAndApiAsync(CancellationToken cancellationToken)
    {
        var readers = _readers;
        _readers = null;
        if (readers is not null)
        {
            await readers.DisposeAsync();
            // Snapshot immutably after the bounded dispose attempt returns rather than handing the writer a
            // live, still-mutable statistics object: on the timeout path a reader worker can still be running
            // (see ClawSensorProbeReaders.DisposeAsync) and would otherwise race report serialization.
            _writer?.SetTiming(readers.GyroscopeTiming.Snapshot(), readers.AccelerometerTiming.Snapshot());
            _writer?.SetSourceConfiguration(readers.GyroscopeConfiguration, readers.AccelerometerConfiguration);
            foreach (var error in readers.Errors) { lock (_readerErrors) _readerErrors.Add(error); _writer?.AddError(error); }
            if (readers.ShutdownTimedOut)
            {
                _writer?.MarkShutdownTimedOut();
                _apiReleaseDeferred = true;
                if (_api is not null) DeferApiRelease(readers, _api);
                return;
            }
        }
        if (!_apiReleaseDeferred) { _api?.Dispose(); _api = null; }
        cancellationToken.ThrowIfCancellationRequested();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifecycleGate.WaitAsync();
        try
        {
            _lifecycleCancellation.Cancel();
            FreezeRecordingElapsed();
            await ShutdownReadersAndApiAsync(CancellationToken.None);
            if (_writer is not null) await _writer.FinalizeAsync();
        }
        finally
        {
            _lifecycleCancellation.Dispose();
            _lifecycleGate.Release();
        }
    }
}
