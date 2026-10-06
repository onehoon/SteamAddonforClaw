using System.Text;
using System.Threading.Channels;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Xbox;

namespace SteamInputAddonforClaw.Diagnostics.XboxSession;

internal sealed class XboxGameSessionDiagnostic : IAsyncDisposable
{
    private const int MaximumReconcileWindows = 512;
    private const int MaximumLifecycleEvents = 256;
    private const int MaximumReportNameAttempts = 100;
    private const int MaximumFailureTextLength = 512;

    private readonly IXboxGameWindowEventSource _windowSource;
    private readonly IXboxGameProcessIdentityProbe _processProbe;
    private readonly string? _logDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action? _stateChanged;
    private readonly Channel<DiagnosticMessage> _messages = Channel.CreateUnbounded<DiagnosticMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<uint, ProcessRecord> _processes = [];
    private readonly List<FrontendXboxSessionDiagnosticLifecycleEvent> _lifecycleEvents = [];
    private readonly Task _worker;
    private CancellationTokenSource? _sessionCancellation;
    private FrontendXboxSessionDiagnosticState _state = FrontendXboxSessionDiagnosticState.Stopped;
    private string _status = "Stopped.";
    private string? _failureMessage;
    private FrontendXboxSessionDiagnosticCounters _counters = FrontendXboxSessionDiagnosticCounters.Empty;
    private FrontendXboxSessionDiagnosticGame? _activeGame;
    private FrontendXboxSessionDiagnosticGame? _lastPositiveGame;
    private XboxGameProcessGenerationKey? _activeGeneration;
    private bool _activeWasForeground;
    private bool _foregroundWasLost;
    private bool _foregroundHookInstalled;
    private bool _createHookInstalled;
    private bool _showHookInstalled;
    private int _omittedLifecycleEventCount;
    private int _acceptWindowEvents;
    private int _disposed;

    internal XboxGameSessionDiagnostic(
        IXboxGameWindowEventSource? windowSource = null,
        IXboxGameProcessIdentityProbe? processProbe = null,
        string? logDirectory = null,
        Func<DateTimeOffset>? clock = null,
        Action? stateChanged = null)
    {
        _windowSource = windowSource ?? new WindowsXboxGameWindowEventSource();
        _processProbe = processProbe ?? new WindowsXboxGameProcessIdentityProbe();
        _logDirectory = logDirectory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _stateChanged = stateChanged;
        _worker = Task.Run(ProcessMessagesAsync);
    }

    internal async Task<FrontendXboxSessionDiagnosticSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<FrontendXboxSessionDiagnosticSnapshot>();
        await _messages.Writer.WriteAsync(new CaptureMessage(completion), cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<FrontendXboxSessionDiagnosticSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var beforeStart = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (beforeStart.State == FrontendXboxSessionDiagnosticState.Running)
                return beforeStart;

            _sessionCancellation?.Dispose();
            _sessionCancellation = new CancellationTokenSource();
            try
            {
                await _windowSource.StartAsync(
                    observation => TryQueue(new WindowObservationMessage(observation)),
                    exception => TryQueue(new EventSourceFailureMessage(exception)),
                    cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _acceptWindowEvents, 1);
                _ = await ConfigureAsync(
                    FrontendXboxSessionDiagnosticState.Running,
                    "Running. Waiting for XBOX game window events.",
                    null,
                    hooksInstalled: true,
                    resetSession: true,
                    preserveLastPositive: false,
                    lifecycleEvent: CreateLifecycleEvent("ObserverStarted", null, "Installed FOREGROUND, CREATE, and SHOW hooks.")).ConfigureAwait(false);
                AppLog.Info("XboxSessionDiagnostic", "XBOX active game diagnostic started.",
                    ("Hooks", "EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW"));
                NotifyStateChanged();
                return await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _acceptWindowEvents, 0);
                _sessionCancellation.Cancel();
                try { await _windowSource.StopAsync().ConfigureAwait(false); } catch { }
                var reason = DescribeFailure(exception);
                AppLog.Warn("XboxSessionDiagnostic", "XBOX active game diagnostic could not start.", exception);
                return await ConfigureAsync(
                    FrontendXboxSessionDiagnosticState.Failed,
                    "Failed to start the XBOX active game diagnostic.",
                    reason,
                    hooksInstalled: false,
                    resetSession: true,
                    preserveLastPositive: false,
                    lifecycleEvent: CreateLifecycleEvent("ObserverFailed", null, reason)).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task<FrontendXboxSessionDiagnosticSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task<FrontendXboxSessionDiagnosticReportResult> GenerateReportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<FrontendXboxSessionDiagnosticReportResult>();
        await _messages.Writer.WriteAsync(new ReportMessage(completion, cancellationToken), cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task ReconcileAfterResumeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            var snapshot = await CaptureAsync().ConfigureAwait(false);
            if (snapshot.State != FrontendXboxSessionDiagnosticState.Running)
                return;

            Volatile.Write(ref _acceptWindowEvents, 0);
            _sessionCancellation?.Cancel();
            await _windowSource.StopAsync().ConfigureAwait(false);
            await ConfigureAsync(
                FrontendXboxSessionDiagnosticState.Stopped,
                "Re-arming after resume.",
                null,
                hooksInstalled: false,
                resetSession: false,
                preserveLastPositive: true,
                lifecycleEvent: CreateLifecycleEvent("ResumeObserved", _activeGame?.CandidateKey, "Re-armed hooks while retaining process-generation exit ownership."),
                preserveActive: true).ConfigureAwait(false);
            _sessionCancellation?.Dispose();
            _sessionCancellation = new CancellationTokenSource();

            await _windowSource.StartAsync(
                observation => TryQueue(new WindowObservationMessage(observation)),
                exception => TryQueue(new EventSourceFailureMessage(exception)),
                CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _acceptWindowEvents, 1);
            await ConfigureAsync(
                FrontendXboxSessionDiagnosticState.Running,
                "Running after resume reconciliation.",
                null,
                hooksInstalled: true,
                resetSession: false,
                preserveLastPositive: true,
                lifecycleEvent: null,
                preserveActive: true).ConfigureAwait(false);
            AppLog.Info("XboxSessionDiagnostic", "Resume reconciliation started.");
            NotifyStateChanged();
            _ = await ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _acceptWindowEvents, 0);
            _sessionCancellation?.Cancel();
            try { await _windowSource.StopAsync().ConfigureAwait(false); } catch { }
            var reason = DescribeFailure(exception);
            AppLog.Warn("XboxSessionDiagnostic", "Resume reconciliation could not re-arm the diagnostic.", exception);
            await ConfigureAsync(
                FrontendXboxSessionDiagnosticState.Failed,
                "Failed to re-arm the XBOX active game diagnostic after resume.",
                reason,
                hooksInstalled: false,
                resetSession: false,
                preserveLastPositive: true,
                lifecycleEvent: CreateLifecycleEvent("ResumeReconcileFailed", null, reason),
                preserveActive: true).ConfigureAwait(false);
            NotifyStateChanged();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await _windowSource.DisposeAsync().ConfigureAwait(false);
            _messages.Writer.TryComplete();
            await _worker.ConfigureAwait(false);
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async Task<FrontendXboxSessionDiagnosticSnapshot> StopCoreAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _acceptWindowEvents, 0);
        _sessionCancellation?.Cancel();
        await _windowSource.StopAsync().ConfigureAwait(false);
        var snapshot = await ConfigureAsync(
            FrontendXboxSessionDiagnosticState.Stopped,
            "Stopped.",
            null,
            hooksInstalled: false,
            resetSession: false,
            preserveLastPositive: true,
            lifecycleEvent: CreateLifecycleEvent("ObserverStopped", _activeGame?.CandidateKey, "Unhooked events and cleared active diagnostic state.")).ConfigureAwait(false);
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        AppLog.Info("XboxSessionDiagnostic", "XBOX active game diagnostic stopped.");
        LogCountersDebug(snapshot.Counters);
        NotifyStateChanged();
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    private async Task<FrontendXboxSessionDiagnosticSnapshot> ReconcileAsync(CancellationToken cancellationToken)
    {
        var completion = NewCompletion<FrontendXboxSessionDiagnosticSnapshot>();
        await _messages.Writer.WriteAsync(new ReconcileMessage(completion, cancellationToken), cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<FrontendXboxSessionDiagnosticSnapshot> ConfigureAsync(
        FrontendXboxSessionDiagnosticState state,
        string status,
        string? failureMessage,
        bool hooksInstalled,
        bool resetSession,
        bool preserveLastPositive,
        FrontendXboxSessionDiagnosticLifecycleEvent? lifecycleEvent,
        bool preserveActive = false)
    {
        var completion = NewCompletion<FrontendXboxSessionDiagnosticSnapshot>();
        await _messages.Writer.WriteAsync(
            new ConfigureMessage(state, status, failureMessage, hooksInstalled, resetSession, preserveLastPositive, lifecycleEvent, preserveActive, completion),
            CancellationToken.None).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    private void TryQueue(DiagnosticMessage message) => _messages.Writer.TryWrite(message);

    private async Task ProcessMessagesAsync()
    {
        await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (message)
                {
                    case WindowObservationMessage observation:
                        if (Volatile.Read(ref _acceptWindowEvents) != 0)
                            await ProcessWindowObservationAsync(observation.Observation).ConfigureAwait(false);
                        break;
                    case ProcessExitedMessage exited:
                        ProcessExited(exited.Key);
                        break;
                    case ConfigureMessage configure:
                        await ProcessConfigureAsync(configure).ConfigureAwait(false);
                        break;
                    case CaptureMessage capture:
                        capture.Completion.TrySetResult(CreateSnapshot());
                        break;
                    case ReconcileMessage reconcile:
                        try
                        {
                            await ReconcileWindowsAsync(reconcile.CancellationToken).ConfigureAwait(false);
                            reconcile.Completion.TrySetResult(CreateSnapshot());
                        }
                        catch (Exception exception)
                        {
                            reconcile.Completion.TrySetException(exception);
                        }
                        break;
                    case ReportMessage report:
                        await ProcessReportAsync(report).ConfigureAwait(false);
                        break;
                    case EventSourceFailureMessage failure:
                        await ProcessEventSourceFailureAsync(failure.Exception).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception exception)
            {
                AppLog.Warn("XboxSessionDiagnostic", "A diagnostic worker message failed.", exception,
                    ("Message", message.GetType().Name));
                CompleteWithFailure(message, exception);
            }
        }
    }

    private async Task ProcessConfigureAsync(ConfigureMessage message)
    {
        if (message.ResetSession)
        {
            RetireAllProcesses();
            _counters = FrontendXboxSessionDiagnosticCounters.Empty;
            _lifecycleEvents.Clear();
            _omittedLifecycleEventCount = 0;
            _activeGame = null;
            _lastPositiveGame = null;
        }
        else if (!message.PreserveActive)
        {
            RetireAllProcesses();
            _activeGame = null;
            _activeGeneration = null;
        }

        if (!message.PreserveLastPositive)
            _lastPositiveGame = null;
        if (!message.PreserveActive)
        {
            _activeGeneration = null;
            _activeWasForeground = false;
            _foregroundWasLost = false;
        }
        _state = message.State;
        _status = message.Status;
        _failureMessage = message.FailureMessage;
        _foregroundHookInstalled = message.HooksInstalled;
        _createHookInstalled = message.HooksInstalled;
        _showHookInstalled = message.HooksInstalled;
        if (message.LifecycleEvent is { } lifecycleEvent)
            AddLifecycleEvent(lifecycleEvent);
        message.Completion.TrySetResult(CreateSnapshot());
        await Task.CompletedTask;
    }

    private async Task ReconcileWindowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var foregroundProcessId = _windowSource.GetForegroundProcessId();
        IReadOnlyList<uint> candidates = [];
        try
        {
            if (foregroundProcessId != 0)
            {
                UpdateForegroundEvidence(foregroundProcessId);
                await ProcessCandidateAsync(foregroundProcessId, XboxGameWindowEventKind.Foreground, cancellationToken).ConfigureAwait(false);
            }

            if (_activeGame is not null)
                return;

            candidates = _windowSource.EnumerateTopLevelProcessIds(MaximumReconcileWindows);
            foreach (var processId in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processId == 0 || processId == foregroundProcessId)
                    continue;
                await ProcessCandidateAsync(processId, XboxGameWindowEventKind.Create, cancellationToken).ConfigureAwait(false);
                if (_activeGame is not null)
                    return;
            }
        }
        finally
        {
            AppLog.Info("XboxSessionDiagnostic", "Bounded XBOX session reconcile completed.",
                ("ForegroundProcessId", foregroundProcessId),
                ("EnumeratedProcessCount", candidates.Count),
                ("ActiveCandidateKey", _activeGame?.CandidateKey ?? "<none>"));
        }
    }

    private async Task ProcessWindowObservationAsync(XboxGameWindowObservation observation)
    {
        _counters = _counters with { AcceptedWinEvents = _counters.AcceptedWinEvents + 1 };
        if (observation.Kind == XboxGameWindowEventKind.Foreground)
            UpdateForegroundEvidence(observation.ProcessId);

        await ProcessCandidateAsync(observation.ProcessId, observation.Kind, _sessionCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ProcessCandidateAsync(uint processId, XboxGameWindowEventKind eventKind, CancellationToken cancellationToken)
    {
        if (processId == 0)
            return;
        cancellationToken.ThrowIfCancellationRequested();

        var opened = _processProbe.Open(processId);
        if (opened.Generation is not { } generation)
        {
            _counters = _counters with { ProcessOpenFailures = _counters.ProcessOpenFailures + 1 };
            AppLog.Debug("XboxSessionDiagnostic", "Candidate process could not be opened for limited information query.",
                ("PID", processId), ("Error", opened.ErrorCode));
            return;
        }

        if (_processes.TryGetValue(processId, out var existing))
        {
            if (existing.Generation.Key == generation.Key)
            {
                generation.Dispose();
                AppLog.Debug("XboxSessionDiagnostic", "Duplicate WinEvent for an already classified process generation was suppressed.",
                    ("PID", processId),
                    ("CreationTime", generation.Key.CreationTime),
                    ("Disposition", existing.Inspection?.Disposition.ToString() ?? "InspectionPending"));
                if (existing.Inspection?.Disposition == XboxGameProcessInspectionDisposition.Matched
                    && existing.Inspection.Game is { } cachedGame
                    && _activeGame is null)
                    Activate(existing, cachedGame, eventKind);
                return;
            }

            RetireProcess(existing, processExited: true, "A different process creation time reused this PID.");
        }

        var record = new ProcessRecord(generation);
        generation.Exited += OnProcessExited;
        _processes[processId] = record;
        _counters = _counters with { UniqueProcessGenerationsInspected = _counters.UniqueProcessGenerationsInspected + 1 };

        if (generation.IsSignaled)
        {
            TryQueue(new ProcessExitedMessage(generation.Key));
            return;
        }

        XboxGameProcessInspection inspection;
        try
        {
            inspection = await _processProbe.InspectAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            inspection = new(XboxGameProcessInspectionDisposition.PackageIdentityFailure, DescribeFailure(exception), null);
        }

        if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _acceptWindowEvents) == 0)
            return;
        record.Inspection = inspection;

        switch (inspection.Disposition)
        {
            case XboxGameProcessInspectionDisposition.ProcessImageFailure:
                _counters = _counters with { ProcessImageFailures = _counters.ProcessImageFailures + 1 };
                AppLog.Debug("XboxSessionDiagnostic", "Candidate executable path could not be resolved.",
                    ("PID", processId), ("Reason", inspection.FailureReason));
                break;
            case XboxGameProcessInspectionDisposition.NoPackage:
                _counters = _counters with { NoPackageCandidates = _counters.NoPackageCandidates + 1 };
                AppLog.Debug("XboxSessionDiagnostic", "Candidate process has no package identity.",
                    ("PID", processId));
                break;
            case XboxGameProcessInspectionDisposition.PackageIdentityFailure:
                _counters = _counters with { PackageIdentityFailures = _counters.PackageIdentityFailures + 1 };
                AppLog.Debug("XboxSessionDiagnostic", "Candidate package identity could not be resolved.",
                    ("PID", processId), ("Reason", inspection.FailureReason));
                break;
            case XboxGameProcessInspectionDisposition.ConfigNegative:
                _counters = _counters with { ConfigNegativeCandidates = _counters.ConfigNegativeCandidates + 1 };
                AppLog.Debug("XboxSessionDiagnostic", "Candidate had no usable MicrosoftGame.config evidence.",
                    ("PID", processId), ("Reason", inspection.FailureReason));
                break;
            case XboxGameProcessInspectionDisposition.ExecutableMismatch:
                _counters = _counters with { ExecutableMismatches = _counters.ExecutableMismatches + 1 };
                AppLog.Debug("XboxSessionDiagnostic", "Candidate executable did not match MicrosoftGame.config.",
                    ("PID", processId), ("Reason", inspection.FailureReason));
                break;
            case XboxGameProcessInspectionDisposition.Matched when inspection.Game is { } game:
                if (generation.IsSignaled)
                {
                    TryQueue(new ProcessExitedMessage(generation.Key));
                    return;
                }
                record.Inspection = inspection;
                _counters = _counters with { PositiveMatches = _counters.PositiveMatches + 1 };
                _lastPositiveGame = game;
                if (_activeGame is null)
                {
                    Activate(record, game, eventKind);
                }
                else if (_activeGeneration != generation.Key)
                {
                    var current = _activeGeneration is { } activeKey
                        && _processes.TryGetValue(activeKey.ProcessId, out var activeRecord)
                        ? activeRecord
                        : null;
                    if (current is not null && current.Generation.IsSignaled)
                    {
                        ProcessExited(current.Generation.Key);
                        if (_activeGame is null)
                            Activate(record, game, eventKind);
                    }
                    else
                    {
                        AddLifecycleEvent(CreateLifecycleEvent("SecondPositiveLiveGameConflict", game.CandidateKey,
                            $"Kept active PID {_activeGame.ProcessId}; did not arbitrate between live games."));
                        AppLog.Info("XboxSessionDiagnostic", "A second positive XBOX game was observed while the current matched process remained live.",
                            ("ActivePID", _activeGame.ProcessId),
                            ("CandidatePID", game.ProcessId),
                            ("ActiveCandidateKey", _activeGame.CandidateKey),
                            ("CandidateKey", game.CandidateKey));
                    }
                }
                break;
        }
    }

    private void Activate(ProcessRecord record, FrontendXboxSessionDiagnosticGame game, XboxGameWindowEventKind eventKind)
    {
        if (record.Generation.IsSignaled)
        {
            TryQueue(new ProcessExitedMessage(record.Generation.Key));
            return;
        }

        _activeGame = game;
        _activeGeneration = record.Generation.Key;
        _activeWasForeground = eventKind == XboxGameWindowEventKind.Foreground;
        _foregroundWasLost = false;
        AddLifecycleEvent(CreateLifecycleEvent("ActiveGameDetected", game.CandidateKey,
            $"PID {game.ProcessId}; executable {game.RunningExecutableName}."));
        AppLog.Info("XboxSessionDiagnostic", "Positive XBOX active-game identity match.",
            ("PID", game.ProcessId),
            ("RunningProcessPath", game.RunningProcessPath),
            ("PackageFullName", game.PackageFullName),
            ("PackageFamilyName", game.PackageFamilyName),
            ("CandidateKey", game.CandidateKey),
            ("StoreId", game.StoreId ?? "<absent>"),
            ("TitleId", game.TitleId ?? "<absent>"),
            ("MatchedExecutable", game.MatchedExecutableName),
            ("ConfigPath", game.ConfigPath));
        NotifyStateChanged();
    }

    private void UpdateForegroundEvidence(uint processId)
    {
        if (_activeGame is not { } active)
            return;
        if (processId == active.ProcessId)
        {
            if (_foregroundWasLost)
            {
                AddLifecycleEvent(CreateLifecycleEvent("ForegroundReturned", active.CandidateKey, "The matched process returned to the foreground."));
                _foregroundWasLost = false;
                NotifyStateChanged();
            }
            _activeWasForeground = true;
            return;
        }

        if (_activeWasForeground)
        {
            AddLifecycleEvent(CreateLifecycleEvent("ForegroundLeft", active.CandidateKey, $"Foreground moved to PID {processId}; the active process remains authoritative."));
            _foregroundWasLost = true;
            _activeWasForeground = false;
            NotifyStateChanged();
        }
    }

    private void OnProcessExited(IXboxGameProcessGeneration generation) =>
        TryQueue(new ProcessExitedMessage(generation.Key));

    private void ProcessExited(XboxGameProcessGenerationKey key)
    {
        if (!_processes.TryGetValue(key.ProcessId, out var record) || record.Generation.Key != key)
            return;

        RetireProcess(record, processExited: true, "Matched process handle signaled exit.");
    }

    private void RetireProcess(ProcessRecord record, bool processExited, string reason)
    {
        var key = record.Generation.Key;
        if (_processes.TryGetValue(key.ProcessId, out var current) && ReferenceEquals(current, record))
            _processes.Remove(key.ProcessId);
        record.Generation.Exited -= OnProcessExited;
        record.Generation.Dispose();

        if (!processExited)
            return;

        _counters = _counters with { ProcessExits = _counters.ProcessExits + 1 };
        var wasActive = _activeGeneration == key;
        AddLifecycleEvent(CreateLifecycleEvent("ProcessExited", record.Inspection?.Game?.CandidateKey ?? _activeGame?.CandidateKey, reason));
        if (wasActive)
        {
            _activeGame = null;
            _activeGeneration = null;
            _activeWasForeground = false;
            _foregroundWasLost = false;
            _status = _state == FrontendXboxSessionDiagnosticState.Failed
                ? "Previously matched process exited; the observer remains failed."
                : "Matched process exited. Waiting for another XBOX game.";
            AppLog.Info("XboxSessionDiagnostic", "Matched XBOX game process exited; active diagnostic state cleared.",
                ("PID", key.ProcessId), ("CreationTime", key.CreationTime));
            NotifyStateChanged();
        }
    }

    private void RetireAllProcesses()
    {
        foreach (var record in _processes.Values.ToArray())
        {
            record.Generation.Exited -= OnProcessExited;
            record.Generation.Dispose();
        }
        _processes.Clear();
    }

    private void RetireProcessesExceptActive()
    {
        foreach (var record in _processes.Values.ToArray())
        {
            if (_activeGeneration == record.Generation.Key)
                continue;
            _processes.Remove(record.Generation.ProcessId);
            record.Generation.Exited -= OnProcessExited;
            record.Generation.Dispose();
        }
    }

    private async Task ProcessEventSourceFailureAsync(Exception exception)
    {
        Volatile.Write(ref _acceptWindowEvents, 0);
        _sessionCancellation?.Cancel();
        RetireProcessesExceptActive();
        _state = FrontendXboxSessionDiagnosticState.Failed;
        _status = "The XBOX active game event source failed.";
        _failureMessage = DescribeFailure(exception);
        _foregroundHookInstalled = false;
        _createHookInstalled = false;
        _showHookInstalled = false;
        AddLifecycleEvent(CreateLifecycleEvent("ObserverFailed", null, _failureMessage));
        AppLog.Warn("XboxSessionDiagnostic", _status, exception);
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    private async Task ProcessReportAsync(ReportMessage message)
    {
        if (message.CancellationToken.IsCancellationRequested)
        {
            message.Completion.TrySetCanceled(message.CancellationToken);
            return;
        }

        var snapshot = CreateSnapshot();
        LogCountersDebug(snapshot.Counters);
        try
        {
            var reportPath = await WriteReportAsync(snapshot, message.CancellationToken).ConfigureAwait(false);
            message.Completion.TrySetResult(new(
                FrontendXboxSessionDiagnosticReportOutcome.Created,
                "Diagnostic report created.",
                reportPath,
                snapshot,
                null));
        }
        catch (OperationCanceledException) when (message.CancellationToken.IsCancellationRequested)
        {
            message.Completion.TrySetCanceled(message.CancellationToken);
        }
        catch (Exception exception)
        {
            var reason = DescribeFailure(exception);
            AppLog.Warn("XboxSessionDiagnostic", "Diagnostic report could not be written.", exception);
            message.Completion.TrySetResult(new(
                FrontendXboxSessionDiagnosticReportOutcome.Failed,
                "Diagnostic report could not be written.",
                null,
                snapshot,
                reason));
        }
    }

    private async Task<string> WriteReportAsync(FrontendXboxSessionDiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_logDirectory ?? AppLog.DirectoryPath, "Discovery");
        Directory.CreateDirectory(directory);
        var stem = $"xbox-session-diagnostic-{_clock():yyyyMMdd-HHmmss}";
        var report = BuildReport(snapshot);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(report);
        for (var suffix = 1; suffix <= MaximumReportNameAttempts; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = suffix == 1 ? $"{stem}.txt" : $"{stem}-{suffix}.txt";
            var path = Path.Combine(directory, name);
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        throw new IOException("A unique diagnostic report file name could not be created.");
    }

    private string BuildReport(FrontendXboxSessionDiagnosticSnapshot snapshot)
    {
        var report = new StringBuilder();
        report.AppendLine("XBOX Active Game Session Diagnostic");
        report.AppendLine($"Timestamp UTC: {_clock():O}");
        report.AppendLine($"OS: {Environment.OSVersion.VersionString} (build {Environment.OSVersion.Version.Build})");
        report.AppendLine($"Addon version: {typeof(XboxGameSessionDiagnostic).Assembly.GetName().Version?.ToString() ?? "<Unavailable>"}");
        report.AppendLine();
        report.AppendLine($"Observer: {snapshot.State} — {snapshot.Status}");
        report.AppendLine($"Hooks: Foreground={snapshot.ForegroundHookInstalled}; Create={snapshot.CreateHookInstalled}; Show={snapshot.ShowHookInstalled}");
        if (snapshot.FailureMessage is { } failure)
            report.AppendLine($"Failure: {failure}");
        report.AppendLine();
        report.AppendLine("Counters");
        report.AppendLine($"Accepted WinEvents: {snapshot.Counters.AcceptedWinEvents}");
        report.AppendLine($"Unique process generations inspected: {snapshot.Counters.UniqueProcessGenerationsInspected}");
        report.AppendLine($"Process open failures: {snapshot.Counters.ProcessOpenFailures}");
        report.AppendLine($"Process image failures: {snapshot.Counters.ProcessImageFailures}");
        report.AppendLine($"No-package candidates: {snapshot.Counters.NoPackageCandidates}");
        report.AppendLine($"Package identity failures: {snapshot.Counters.PackageIdentityFailures}");
        report.AppendLine($"Config-negative candidates: {snapshot.Counters.ConfigNegativeCandidates}");
        report.AppendLine($"Executable mismatches: {snapshot.Counters.ExecutableMismatches}");
        report.AppendLine($"Positive matches: {snapshot.Counters.PositiveMatches}");
        report.AppendLine($"Process exits: {snapshot.Counters.ProcessExits}");
        AppendGame(report, "Current active game", snapshot.ActiveGame);
        AppendGame(report, "Last positive game", snapshot.LastPositiveGame);
        report.AppendLine();
        report.AppendLine("Lifecycle evidence");
        foreach (var entry in snapshot.LifecycleEvents)
            report.AppendLine($"{entry.TimestampUtc:O} | {entry.Event} | {entry.CandidateKey ?? "<none>"} | {entry.Detail ?? "<none>"}");
        if (snapshot.OmittedLifecycleEventCount > 0)
            report.AppendLine($"Omitted older lifecycle events: {snapshot.OmittedLifecycleEventCount}");
        return report.ToString();
    }

    private static void AppendGame(StringBuilder report, string heading, FrontendXboxSessionDiagnosticGame? game)
    {
        report.AppendLine();
        report.AppendLine(heading);
        if (game is null)
        {
            report.AppendLine("<none>");
            return;
        }

        report.AppendLine($"  Candidate key: {game.CandidateKey}");
        report.AppendLine($"  PID: {game.ProcessId}");
        report.AppendLine($"  Running process path: {game.RunningProcessPath}");
        report.AppendLine($"  Running executable: {game.RunningExecutableName}");
        report.AppendLine($"  Package full name: {game.PackageFullName}");
        report.AppendLine($"  Package family name: {game.PackageFamilyName}");
        report.AppendLine($"  AUMID: {game.ApplicationUserModelId ?? "<absent>"} (result {game.ApplicationUserModelIdResult})");
        report.AppendLine($"  Package identity: {game.PackageIdentityName ?? "<absent>"}; publisher={game.PackageIdentityPublisher ?? "<absent>"}; publisherId={game.PackageIdentityPublisherId ?? "<absent>"}; resourceId={game.PackageIdentityResourceId ?? "<absent>"}; architecture={game.PackageIdentityArchitecture ?? "<absent>"}; version={game.PackageIdentityVersion ?? "<absent>"}");
        report.AppendLine($"  MicrosoftGame.config identity: {game.IdentityName}; publisher={game.IdentityPublisher}; resourceId={game.IdentityResourceId ?? "<absent>"}");
        report.AppendLine($"  StoreId: {game.StoreId ?? "<absent>"}");
        report.AppendLine($"  TitleId: {game.TitleId ?? "<absent>"}");
        report.AppendLine($"  Matched ExecutableList entry: {game.MatchedExecutableName}");
        report.AppendLine($"  Config path: {game.ConfigPath}");
        report.AppendLine("  PackagePathType evidence:");
        foreach (var path in game.PackagePaths)
            report.AppendLine($"    {path.PathType}: result={path.ResultCode}; path={path.Path ?? "<unavailable>"}");
    }

    private FrontendXboxSessionDiagnosticSnapshot CreateSnapshot() => new(
        true,
        _state,
        _status,
        _foregroundHookInstalled,
        _createHookInstalled,
        _showHookInstalled,
        _counters,
        _activeGame,
        _lastPositiveGame,
        _lifecycleEvents.ToArray(),
        _omittedLifecycleEventCount,
        _failureMessage);

    private void AddLifecycleEvent(FrontendXboxSessionDiagnosticLifecycleEvent item)
    {
        _lifecycleEvents.Add(item with { TimestampUtc = _clock().ToUniversalTime() });
        if (_lifecycleEvents.Count <= MaximumLifecycleEvents)
            return;
        _lifecycleEvents.RemoveAt(0);
        _omittedLifecycleEventCount++;
    }

    private FrontendXboxSessionDiagnosticLifecycleEvent CreateLifecycleEvent(
        string eventName,
        string? candidateKey,
        string? detail) => new(_clock().ToUniversalTime(), eventName, candidateKey, detail);

    private void NotifyStateChanged()
    {
        try { _stateChanged?.Invoke(); }
        catch (Exception exception)
        {
            AppLog.Debug("XboxSessionDiagnostic", "Frontend state notification callback failed.",
                ("Reason", exception.GetType().Name));
        }
    }

    private static void LogCountersDebug(FrontendXboxSessionDiagnosticCounters counters) =>
        AppLog.Debug("XboxSessionDiagnostic", "XBOX active game diagnostic counters.",
            ("AcceptedWinEvents", counters.AcceptedWinEvents),
            ("UniqueProcessGenerationsInspected", counters.UniqueProcessGenerationsInspected),
            ("ProcessOpenFailures", counters.ProcessOpenFailures),
            ("ProcessImageFailures", counters.ProcessImageFailures),
            ("NoPackageCandidates", counters.NoPackageCandidates),
            ("PackageIdentityFailures", counters.PackageIdentityFailures),
            ("ConfigNegativeCandidates", counters.ConfigNegativeCandidates),
            ("ExecutableMismatches", counters.ExecutableMismatches),
            ("PositiveMatches", counters.PositiveMatches),
            ("ProcessExits", counters.ProcessExits));

    private static string DescribeFailure(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}";
        return message.Length <= MaximumFailureTextLength ? message : message[..MaximumFailureTextLength];
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void CompleteWithFailure(DiagnosticMessage message, Exception exception)
    {
        switch (message)
        {
            case CaptureMessage capture:
                capture.Completion.TrySetException(exception);
                break;
            case ConfigureMessage configure:
                configure.Completion.TrySetException(exception);
                break;
            case ReconcileMessage reconcile:
                reconcile.Completion.TrySetException(exception);
                break;
            case ReportMessage report:
                report.Completion.TrySetException(exception);
                break;
        }
    }

    private abstract record DiagnosticMessage;
    private sealed record WindowObservationMessage(XboxGameWindowObservation Observation) : DiagnosticMessage;
    private sealed record ProcessExitedMessage(XboxGameProcessGenerationKey Key) : DiagnosticMessage;
    private sealed record EventSourceFailureMessage(Exception Exception) : DiagnosticMessage;
    private sealed record CaptureMessage(TaskCompletionSource<FrontendXboxSessionDiagnosticSnapshot> Completion) : DiagnosticMessage;
    private sealed record ReconcileMessage(TaskCompletionSource<FrontendXboxSessionDiagnosticSnapshot> Completion, CancellationToken CancellationToken) : DiagnosticMessage;
    private sealed record ConfigureMessage(
        FrontendXboxSessionDiagnosticState State,
        string Status,
        string? FailureMessage,
        bool HooksInstalled,
        bool ResetSession,
        bool PreserveLastPositive,
        FrontendXboxSessionDiagnosticLifecycleEvent? LifecycleEvent,
        bool PreserveActive,
        TaskCompletionSource<FrontendXboxSessionDiagnosticSnapshot> Completion) : DiagnosticMessage;
    private sealed record ReportMessage(
        TaskCompletionSource<FrontendXboxSessionDiagnosticReportResult> Completion,
        CancellationToken CancellationToken) : DiagnosticMessage;

    private sealed class ProcessRecord(IXboxGameProcessGeneration generation)
    {
        internal IXboxGameProcessGeneration Generation { get; } = generation;
        internal XboxGameProcessInspection? Inspection { get; set; }
    }
}
