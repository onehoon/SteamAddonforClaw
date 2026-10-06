using System.Threading.Channels;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Xbox.Session;

internal sealed class XboxGameSessionRuntime : IAsyncDisposable
{
    private const int MaximumReconcileWindows = 512;

    private readonly IXboxGameWindowEventSource _windowSource;
    private readonly IXboxGameProcessIdentityProbe _processProbe;
    private readonly Channel<SessionMessage> _messages = Channel.CreateUnbounded<SessionMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<uint, ProcessRecord> _processes = [];
    private readonly Task _worker;
    private CancellationTokenSource? _sessionCancellation;
    private ActiveXboxGame? _activeGame;
    private XboxGameProcessGenerationKey? _activeGeneration;
    private int _acceptWindowEvents;
    private int _startAttempted;
    private int _disposed;

    internal XboxGameSessionRuntime(
        IXboxGameWindowEventSource? windowSource = null,
        IXboxGameProcessIdentityProbe? processProbe = null)
    {
        _windowSource = windowSource ?? new WindowsXboxGameWindowEventSource();
        _processProbe = processProbe ?? new WindowsXboxGameProcessIdentityProbe();
        _worker = Task.Run(ProcessMessagesAsync);
    }

    internal ActiveXboxGame? ActiveGame => Volatile.Read(ref _activeGame);

    internal event Action<ActiveXboxGame?>? ActiveGameChanged;

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Exchange(ref _startAttempted, 1) != 0)
                return;

            _sessionCancellation = new CancellationTokenSource();
            try
            {
                await StartWindowSourceAsync(_sessionCancellation.Token, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _acceptWindowEvents, 1);
                AppLog.Info("XboxSession", "Production XBOX game-session runtime started.",
                    ("Hooks", "EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW"));
                await ReconcileSafelyAsync(cancellationToken, "startup").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopWindowSourceSafelyAsync().ConfigureAwait(false);
                Volatile.Write(ref _acceptWindowEvents, 0);
                _sessionCancellation.Cancel();
                _sessionCancellation.Dispose();
                _sessionCancellation = null;
                Interlocked.Exchange(ref _startAttempted, 0);
                throw;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _acceptWindowEvents, 0);
                _sessionCancellation.Cancel();
                await StopWindowSourceSafelyAsync().ConfigureAwait(false);
                AppLog.Warn("XboxSession", "Production XBOX game-session observer could not start; Runtime will continue without active XBOX detection.", exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _startAttempted) == 0)
                return;

            Volatile.Write(ref _acceptWindowEvents, 0);
            _sessionCancellation?.Cancel();
            await StopWindowSourceSafelyAsync().ConfigureAwait(false);
            await SendAndWaitAsync(new PrepareForResumeMessage(NewCompletion()), cancellationToken).ConfigureAwait(false);

            _sessionCancellation?.Dispose();
            _sessionCancellation = new CancellationTokenSource();
            try
            {
                await StartWindowSourceAsync(_sessionCancellation.Token, CancellationToken.None).ConfigureAwait(false);
                Volatile.Write(ref _acceptWindowEvents, 1);
                await ReconcileSafelyAsync(CancellationToken.None, "resume").ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _acceptWindowEvents, 0);
                _sessionCancellation.Cancel();
                await StopWindowSourceSafelyAsync().ConfigureAwait(false);
                await SendAndWaitAsync(new RetireNonActiveMessage(NewCompletion()), CancellationToken.None).ConfigureAwait(false);
                AppLog.Warn("XboxSession", "Resume reconciliation could not re-arm the production XBOX game-session observer; Runtime will continue.", exception);
            }
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
            Volatile.Write(ref _acceptWindowEvents, 0);
            _sessionCancellation?.Cancel();
            await StopWindowSourceSafelyAsync().ConfigureAwait(false);
            await SendAndWaitAsync(new ShutdownMessage(NewCompletion()), CancellationToken.None).ConfigureAwait(false);
            _messages.Writer.TryComplete();
            await _worker.ConfigureAwait(false);
            await _windowSource.DisposeAsync().ConfigureAwait(false);
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            AppLog.Info("XboxSession", "Production XBOX game-session runtime stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async Task StartWindowSourceAsync(CancellationToken sessionToken, CancellationToken startToken)
    {
        await _windowSource.StartAsync(
            observation => TryQueue(new WindowObservationMessage(observation, sessionToken)),
            exception => TryQueue(new EventSourceFailureMessage(exception)),
            startToken).ConfigureAwait(false);
    }

    private async Task ReconcileSafelyAsync(CancellationToken cancellationToken, string reason)
    {
        try
        {
            var completion = NewCompletion<int>();
            await _messages.Writer.WriteAsync(new ReconcileMessage(completion, cancellationToken), cancellationToken).ConfigureAwait(false);
            var inspectedWindowCount = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            AppLog.Info("XboxSession", "Bounded production XBOX session reconcile completed.",
                ("Reason", reason), ("EnumeratedProcessCount", inspectedWindowCount), ("ActiveKey", ActiveGame?.Key ?? "<none>"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Warn("XboxSession", "Bounded production XBOX session reconcile failed; Runtime will continue.", exception,
                ("Reason", reason));
        }
    }

    private async Task ProcessMessagesAsync()
    {
        await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (message)
                {
                    case WindowObservationMessage observation:
                        if (Volatile.Read(ref _acceptWindowEvents) != 0 && !observation.SessionToken.IsCancellationRequested)
                            await ProcessWindowObservationAsync(observation.Observation, observation.SessionToken).ConfigureAwait(false);
                        break;
                    case ProcessExitedMessage exited:
                        ProcessExited(exited.Key);
                        break;
                    case ReconcileMessage reconcile:
                        try
                        {
                            var inspectedWindowCount = await ReconcileWindowsAsync(reconcile.CancellationToken).ConfigureAwait(false);
                            reconcile.Completion.TrySetResult(inspectedWindowCount);
                        }
                        catch (Exception exception)
                        {
                            reconcile.Completion.TrySetException(exception);
                        }
                        break;
                    case PrepareForResumeMessage prepare:
                        PrepareForResume();
                        prepare.Completion.TrySetResult();
                        break;
                    case RetireNonActiveMessage retire:
                        RetireProcessesExceptActive();
                        RetireSignaledActiveGeneration();
                        retire.Completion.TrySetResult();
                        break;
                    case ShutdownMessage shutdown:
                        RetireAllProcesses();
                        shutdown.Completion.TrySetResult();
                        break;
                    case EventSourceFailureMessage failure:
                        await ProcessEventSourceFailureAsync(failure.Exception).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception exception)
            {
                AppLog.Warn("XboxSession", "A production XBOX game-session worker message failed; Runtime will continue.", exception,
                    ("Message", message.GetType().Name));
                CompleteWithFailure(message, exception);
            }
        }
    }

    private async Task<int> ReconcileWindowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var foregroundProcessId = _windowSource.GetForegroundProcessId();
        var candidates = 0;
        if (foregroundProcessId != 0)
        {
            await ProcessCandidateAsync(foregroundProcessId, cancellationToken).ConfigureAwait(false);
            if (_activeGame is not null)
                return candidates;
        }

        var processIds = _windowSource.EnumerateTopLevelProcessIds(MaximumReconcileWindows);
        candidates = processIds.Count;
        foreach (var processId in processIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processId == 0 || processId == foregroundProcessId)
                continue;
            await ProcessCandidateAsync(processId, cancellationToken).ConfigureAwait(false);
            if (_activeGame is not null)
                break;
        }
        return candidates;
    }

    private async Task ProcessWindowObservationAsync(XboxGameWindowObservation observation, CancellationToken sessionToken)
    {
        if (sessionToken.IsCancellationRequested || Volatile.Read(ref _acceptWindowEvents) == 0)
            return;
        await ProcessCandidateAsync(observation.ProcessId, sessionToken).ConfigureAwait(false);
    }

    private async Task ProcessCandidateAsync(uint processId, CancellationToken cancellationToken)
    {
        if (processId == 0 || cancellationToken.IsCancellationRequested)
            return;

        var opened = _processProbe.Open(processId);
        if (opened.Generation is not { } generation)
        {
            AppLog.Debug("XboxSession", "Candidate process could not be opened for limited-information query.",
                ("PID", processId), ("Error", opened.ErrorCode));
            return;
        }

        if (_processes.TryGetValue(processId, out var existing))
        {
            if (existing.Generation.Key == generation.Key)
            {
                generation.Dispose();
                AppLog.Debug("XboxSession", "Duplicate event for an already classified process generation was suppressed.",
                    ("PID", processId), ("CreationTime", existing.Generation.Key.CreationTime),
                    ("Disposition", existing.Inspection?.Disposition.ToString() ?? "InspectionPending"));
                if (existing.Inspection?.Match is { } cachedMatch && _activeGame is null)
                    Activate(existing, cachedMatch);
                return;
            }

            RetireProcess(existing, processExited: true, "A different process creation time reused this PID.");
        }

        var record = new ProcessRecord(generation);
        generation.Exited += OnProcessExited;
        _processes[processId] = record;
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
            inspection = new(XboxGameProcessInspectionDisposition.PackageIdentityFailure,
                $"{exception.GetType().Name}: {exception.Message}", null);
        }

        if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _acceptWindowEvents) == 0)
            return;
        record.Inspection = inspection;

        if (inspection.Match is not { } match)
        {
            AppLog.Debug("XboxSession", "Candidate did not prove an exact XBOX game identity.",
                ("PID", processId), ("Disposition", inspection.Disposition), ("Reason", inspection.FailureReason));
            return;
        }

        if (generation.IsSignaled)
        {
            TryQueue(new ProcessExitedMessage(generation.Key));
            return;
        }

        if (_activeGame is null)
        {
            Activate(record, match);
            return;
        }

        if (_activeGeneration != generation.Key)
        {
            RetireSignaledActiveGeneration();
            if (_activeGame is null)
                Activate(record, match);
            else
                AppLog.Debug("XboxSession", "A second positive XBOX process was observed while the current matched process remained live; no arbitration was performed.",
                    ("ActivePID", _activeGeneration?.ProcessId), ("CandidatePID", match.ProcessId),
                    ("ActiveKey", _activeGame.Key), ("CandidateKey", match.Identity.Key));
        }
    }

    private void Activate(ProcessRecord record, XboxGameProcessMatch match)
    {
        if (record.Generation.IsSignaled)
        {
            TryQueue(new ProcessExitedMessage(record.Generation.Key));
            return;
        }

        _activeGeneration = record.Generation.Key;
        SetActiveGame(new(match.Identity.Key, match.Identity.DisplayName));
        AppLog.Info("XboxSession", "Positive XBOX active-game identity accepted.",
            ("Key", match.Identity.Key), ("DisplayName", match.Identity.DisplayName),
            ("PID", match.ProcessId), ("RunningExecutableName", match.RunningExecutableName));
    }

    private void SetActiveGame(ActiveXboxGame? game)
    {
        var previous = _activeGame;
        if (string.Equals(previous?.Key, game?.Key, StringComparison.OrdinalIgnoreCase))
            return;

        Volatile.Write(ref _activeGame, game);
        var handlers = ActiveGameChanged;
        if (handlers is null)
            return;
        foreach (Action<ActiveXboxGame?> handler in handlers.GetInvocationList())
        {
            try { handler(game); }
            catch (Exception exception)
            {
                AppLog.Debug("XboxSession", "An active-game notification handler failed.",
                    ("Reason", exception.GetType().Name));
            }
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

        if (_activeGeneration == key)
        {
            _activeGeneration = null;
            SetActiveGame(null);
            AppLog.Info("XboxSession", "Matched XBOX game process exited; active identity cleared.",
                ("PID", key.ProcessId), ("CreationTime", key.CreationTime));
        }
        else
        {
            AppLog.Debug("XboxSession", "A classified XBOX candidate process exited.", ("PID", key.ProcessId), ("Reason", reason));
        }
    }

    private void PrepareForResume()
    {
        RetireProcessesExceptActive();
        RetireSignaledActiveGeneration();
    }

    private void RetireSignaledActiveGeneration()
    {
        if (_activeGeneration is not { } key
            || !_processes.TryGetValue(key.ProcessId, out var active)
            || active.Generation.Key != key
            || !active.Generation.IsSignaled)
            return;
        ProcessExited(key);
    }

    private void RetireAllProcesses()
    {
        foreach (var record in _processes.Values.ToArray())
        {
            record.Generation.Exited -= OnProcessExited;
            record.Generation.Dispose();
        }
        _processes.Clear();
        _activeGeneration = null;
        SetActiveGame(null);
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
        await StopWindowSourceSafelyAsync().ConfigureAwait(false);
        RetireProcessesExceptActive();
        RetireSignaledActiveGeneration();
        AppLog.Warn("XboxSession", "The WinEvent source failed; a proven live active game is retained until process exit.", exception);
    }

    private async Task StopWindowSourceSafelyAsync()
    {
        try { await _windowSource.StopAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppLog.Warn("XboxSession", "WinEvent source cleanup failed; Runtime will continue.", exception);
        }
    }

    private void TryQueue(SessionMessage message) => _messages.Writer.TryWrite(message);

    private async Task SendAndWaitAsync(SessionMessage message, CancellationToken cancellationToken)
    {
        await _messages.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        var completion = message switch
        {
            PrepareForResumeMessage prepare => prepare.Completion.Task,
            RetireNonActiveMessage retire => retire.Completion.Task,
            ShutdownMessage shutdown => shutdown.Completion.Task,
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void CompleteWithFailure(SessionMessage message, Exception exception)
    {
        switch (message)
        {
            case ReconcileMessage reconcile:
                reconcile.Completion.TrySetException(exception);
                break;
            case PrepareForResumeMessage prepare:
                prepare.Completion.TrySetException(exception);
                break;
            case RetireNonActiveMessage retire:
                retire.Completion.TrySetException(exception);
                break;
            case ShutdownMessage shutdown:
                shutdown.Completion.TrySetException(exception);
                break;
        }
    }

    private abstract record SessionMessage;
    private sealed record WindowObservationMessage(XboxGameWindowObservation Observation, CancellationToken SessionToken) : SessionMessage;
    private sealed record ProcessExitedMessage(XboxGameProcessGenerationKey Key) : SessionMessage;
    private sealed record EventSourceFailureMessage(Exception Exception) : SessionMessage;
    private sealed record ReconcileMessage(TaskCompletionSource<int> Completion, CancellationToken CancellationToken) : SessionMessage;
    private sealed record PrepareForResumeMessage(TaskCompletionSource Completion) : SessionMessage;
    private sealed record RetireNonActiveMessage(TaskCompletionSource Completion) : SessionMessage;
    private sealed record ShutdownMessage(TaskCompletionSource Completion) : SessionMessage;

    private sealed class ProcessRecord(IXboxGameProcessGeneration generation)
    {
        internal IXboxGameProcessGeneration Generation { get; } = generation;
        internal XboxGameProcessInspection? Inspection { get; set; }
    }
}
