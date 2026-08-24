using System.Buffers.Binary;
using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Owns the production Steam Deck output lifecycle. VIIPER attachment is the virtual
/// output authority; Windows PnP is used only for the single pre-attach exact-target conflict check.</summary>
internal sealed class CanonicalSteamDeckOutputStage : IRoutingPipelineStage
{
    private enum LifecycleState { Inactive, Prepared, Creating, Active, RollingBack }
    private readonly Func<ICanonicalSteamDeckSession> _sessionFactory;
    private readonly IControllerDeviceEnumerator _enumerator;
    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly IInputReportTickSource? _reportTicks;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private uint _busId;
    private uint _deviceId;
    private LifecycleState _state;
    private bool _prepared;
    private CanonicalSteamDeckInputPublisher? _publisher;
    private ICanonicalSteamDeckSession? _canonicalSession;
    private readonly FeedbackAuthority? _feedbackAuthority;
    private readonly IPhysicalRumbleSink? _physicalRumbleSink;
    private SteamDeckRumbleFeedbackBridge? _feedbackBridge;
    private FeedbackAuthorityToken? _feedbackToken;
    private bool _feedbackCallbackRegistered;
    private bool _feedbackRevoked;
    private bool _feedbackArmed;
    private bool _presentationPaused;
    private Func<ValueTask>? _outputFaultHandler;
    private int _outputFaultReported;
    private readonly SteamDeckSystemButtonOverlay _systemButtonOverlay = new();

    private sealed class CreationTiming
    {
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal long CanonicalSessionStartMs { get; set; }
        internal long NeutralReportMs { get; set; }
        internal long PublisherStartMs { get; set; }
    }

    internal CanonicalSteamDeckOutputStage(Func<ICanonicalSteamDeckSession> sessionFactory, IControllerDeviceEnumerator enumerator, IControllerStateSnapshotSource snapshot, IInputReportTickSource? reportTicks = null, FeedbackAuthority? feedbackAuthority = null, IPhysicalRumbleSink? physicalRumbleSink = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _reportTicks = reportTicks;
        if (physicalRumbleSink is not null && feedbackAuthority is null) throw new ArgumentException("A physical rumble sink requires a feedback authority.", nameof(feedbackAuthority));
        _feedbackAuthority = feedbackAuthority;
        _physicalRumbleSink = physicalRumbleSink;
    }

    public RoutingStageKind Kind => RoutingStageKind.SteamOutput;
    public string Name => "SteamDeckOutput";
    internal void SetOutputFaultHandler(Func<ValueTask> handler) => _outputFaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    internal void RequestQuickAccessPulse() => _systemButtonOverlay.RequestQuickAccessPulse();
    internal void RequestSteamPulse() => _systemButtonOverlay.RequestSteamPulse();
    internal Action? FeedbackBeforeLease { set { if (_feedbackBridge is not null) _feedbackBridge.BeforeLease = value; } }

    internal bool TryRequestSteamPulse()
    {
        if (!_serial.Wait(0)) return false;
        try
        {
            if (_state != LifecycleState.Active || _presentationPaused || _publisher is null || _canonicalSession?.State != CanonicalSteamDeckSessionState.Active) return false;
            _systemButtonOverlay.RequestSteamPulse();
            return true;
        }
        finally { _serial.Release(); }
    }

    internal Task<DeveloperVibrationTestOutcome> RunDeveloperVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken cancellationToken)
    {
        if (!_feedbackArmed || _feedbackBridge is null) return Task.FromResult(new DeveloperVibrationTestOutcome(false, null, null));
        var report = new byte[64];
        switch (command)
        {
            case FrontendVibrationTestCommand.Rumble: report[0] = 0xEB; report[1] = 9; BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(5, 2), 0x8000); BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(7, 2), 0x8000); break;
            case FrontendVibrationTestCommand.Haptic: report[0] = 0xEA; report[3] = 4; report[4] = 128; break;
            case FrontendVibrationTestCommand.Stop: report[0] = 0xEB; report[1] = 9; break;
        }
        return _feedbackBridge.ProcessDeveloperTestAsync(report, command is FrontendVibrationTestCommand.Rumble or FrontendVibrationTestCommand.Haptic, cancellationToken);
    }

    internal PhysicalRumbleWriteResult? CancelDeveloperVibrationTest() => _feedbackBridge?.CancelDeveloperTestAndStop();

    private void ReportOutputFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _outputFaultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Live Steam Deck publishing failed.", exception);
        if (_outputFaultHandler is { } handler) _ = Task.Run(async () => { try { await handler().ConfigureAwait(false); } catch (Exception error) { AppLog.Error("SteamOutput", "Steam Deck output fail-close reconciliation failed.", error); } });
    }

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamDeckOutputAvailableForExplicitExperiment"));
    }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state != LifecycleState.Inactive) return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamOutputAlreadyActive"));
        if (_prepared) return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamOutputAlreadyPrepared"));
        try
        {
            var present = _enumerator.EnumeratePresentDevices(SteamDeckVirtualDeviceIdentityPolicy.VendorId, SteamDeckVirtualDeviceIdentityPolicy.ProductId);
            if (present.Any(device => device.Present && device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamDeckOutputConflict"));
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamOutput", "Steam Deck pre-attach conflict inspection failed; attach is blocked.", exception, ("Reason", "SteamDeckOutputConflictInspectionUnavailable"));
            return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamDeckOutputConflictInspectionUnavailable"));
        }
        _prepared = true;
        _state = LifecycleState.Prepared;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamOutputPreflightComplete"));
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var timing = new CreationTiming();
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_prepared || _state != LifecycleState.Prepared) return RoutingStageOperationResult.Failure("SteamOutputNotPrepared");
            _canonicalSession ??= _sessionFactory();
            _state = LifecycleState.Creating;
            var started = Stopwatch.GetTimestamp();
            bool sessionStarted;
            try { sessionStarted = _canonicalSession.Start(); }
            finally { timing.CanonicalSessionStartMs = Elapsed(started); }
            if (!sessionStarted) return await FailAndRollbackCoreAsync(_canonicalSession.State == CanonicalSteamDeckSessionState.Unsafe ? "CanonicalSessionUnsafe" : "CanonicalSessionStartFailed", timing).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _busId = _canonicalSession.BusId ?? 0;
            _deviceId = _canonicalSession.LogicalDeviceId ?? 0;
            started = Stopwatch.GetTimestamp();
            bool neutralAccepted;
            try { neutralAccepted = _canonicalSession.SetNeutral(); }
            finally { timing.NeutralReportMs = Elapsed(started); }
            if (!neutralAccepted) return await FailAndRollbackCoreAsync("NeutralReportRejected", timing).ConfigureAwait(false);
            if (_physicalRumbleSink is not null && _feedbackAuthority is not null)
            {
                var feedbackAuthority = _feedbackAuthority;
                var physicalRumbleSink = _physicalRumbleSink;
                _feedbackArmed = true;
                var feedbackToken = feedbackAuthority.Acquire("SteamDeck");
                _feedbackToken = feedbackToken;
                _feedbackBridge = new SteamDeckRumbleFeedbackBridge(feedbackAuthority, feedbackToken, physicalRumbleSink);
                _feedbackCallbackRegistered = _canonicalSession.SetOutputCallback(_feedbackBridge.Callback);
                if (!_feedbackCallbackRegistered) { _feedbackBridge.Dispose(); _feedbackBridge = null; _feedbackArmed = false; }
                _feedbackRevoked = false;
            }
            Interlocked.Exchange(ref _outputFaultReported, 0);
            _publisher = new CanonicalSteamDeckInputPublisher(_snapshot, _canonicalSession, _reportTicks, fault: ReportOutputFault, systemButtonOverlay: _systemButtonOverlay);
            started = Stopwatch.GetTimestamp();
            try { _publisher.Start(); }
            finally { timing.PublisherStartMs = Elapsed(started); }
            _state = LifecycleState.Active;
            _presentationPaused = false;
            AppLog.Debug("RoutingTrace", "Steam Deck output creation completed.", ("Event", "SteamDeckOutputCreated"), ("TotalMs", Elapsed(timing.Started)), ("CanonicalSessionStartMs", timing.CanonicalSessionStartMs), ("NeutralReportMs", timing.NeutralReportMs), ("PublisherStartMs", timing.PublisherStartMs), ("BusId", _busId), ("DeviceId", _deviceId), ("Result", "Success"));
            return RoutingStageOperationResult.Success("SteamDeckCreated");
        }
        catch (OperationCanceledException)
        {
            if (_state is not LifecycleState.Inactive and not LifecycleState.Prepared) await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw;
            return RoutingStageOperationResult.Failure("SteamOutputCreationCancelled");
        }
        catch (Exception exception) { return await FailAndRollbackCoreAsync(exception.GetType().Name, timing).ConfigureAwait(false); }
        finally { _serial.Release(); }
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RollbackCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _serial.Release(); }
    }

    internal async Task<bool> PausePresentationAsync(CancellationToken cancellationToken = default, bool reportOutputFaultOnFailure = true)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != LifecycleState.Active || _canonicalSession is null || _canonicalSession.State != CanonicalSteamDeckSessionState.Active || _publisher is null) return false;
            if (_presentationPaused) return true;
            try { await _publisher.StopAsync().ConfigureAwait(false); } catch (Exception exception) { if (reportOutputFaultOnFailure) ReportOutputFault(exception); return false; }
            if (!_canonicalSession.SetNeutral()) { if (reportOutputFaultOnFailure) ReportOutputFault(new InvalidOperationException("Canonical VIIPER rejected neutral during presentation pause.")); return false; }
            _presentationPaused = true;
            return true;
        }
        finally { _serial.Release(); }
    }

    internal async Task<bool> ResumePresentationAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ResumePresentationCoreAsync().ConfigureAwait(false); }
        finally { _serial.Release(); }
    }

    private async Task<bool> ResumePresentationCoreAsync()
    {
        if (_state != LifecycleState.Active || !_presentationPaused || _canonicalSession is null || _canonicalSession.State != CanonicalSteamDeckSessionState.Active || _publisher is null) return false;
        try { _publisher.Start(); } catch (Exception exception) { ReportOutputFault(exception); return false; }
        _presentationPaused = false;
        return true;
    }

    internal async ValueTask<RoutingStageOperationResult> ReconcileOwnedStateAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != LifecycleState.Active || _canonicalSession is null || _canonicalSession.State != CanonicalSteamDeckSessionState.Active || _canonicalSession.PendingCleanupPhase != CanonicalPendingCleanupPhase.None || _busId == 0 || _deviceId == 0) return RoutingStageOperationResult.Failure("SteamDeckStructuralStateUnsafe");
            if (!_canonicalSession.TryGetTrackedAttachmentState(out _) || _publisher is null) return RoutingStageOperationResult.Failure(_publisher is null ? "SteamDeckPublisherMissing" : "SteamDeckAttachmentNotAttached");
            if (!_presentationPaused && !_publisher.IsRunning) return RoutingStageOperationResult.Failure("SteamDeckPublisherNotRunning");
            if (_presentationPaused) return await ResumePresentationCoreAsync() ? RoutingStageOperationResult.Success("Repaired") : RoutingStageOperationResult.Failure("SteamDeckPresentationResumeFailed");
            return RoutingStageOperationResult.Success("Healthy");
        }
        finally { _serial.Release(); }
    }

    private async ValueTask<RoutingStageOperationResult> RollbackCoreAsync(CancellationToken cancellationToken)
    {
        if (_state == LifecycleState.Inactive) return RoutingStageOperationResult.Success("SteamOutputAlreadyInactive");
        if (_state == LifecycleState.Prepared)
        {
            _canonicalSession?.Dispose(); _canonicalSession = null; _prepared = false; _state = LifecycleState.Inactive; return RoutingStageOperationResult.Success("SteamOutputPreparationCancelled");
        }
        _state = LifecycleState.RollingBack;
        if (_publisher is not null) { await _publisher.StopAsync().ConfigureAwait(false); _publisher = null; }
        _systemButtonOverlay.Clear();
        if (_feedbackAuthority is not null && _feedbackToken is not null && !_feedbackRevoked) { _feedbackAuthority.Revoke(); _feedbackRevoked = true; }
        _feedbackBridge?.Dispose();
        if (_feedbackCallbackRegistered) { _canonicalSession?.ClearOutputCallback(); _feedbackCallbackRegistered = false; _feedbackArmed = false; }
        if (_canonicalSession is null) return RoutingStageOperationResult.Failure("CanonicalSessionUnavailable");
        if (_canonicalSession.State == CanonicalSteamDeckSessionState.Unsafe) return RoutingStageOperationResult.Failure("CanonicalSessionUnsafe");
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.Active or CanonicalSteamDeckSessionState.CleanupPending)
        {
            var detached = _canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending ? _canonicalSession.RetryPendingCleanup() : _canonicalSession.DetachDevice();
            if (!detached) return RoutingStageOperationResult.Failure(_canonicalSession.State == CanonicalSteamDeckSessionState.Unsafe ? "CanonicalSessionUnsafe" : "VirtualDeviceDetachFailed");
        }
        _canonicalSession.Dispose(); _canonicalSession = null; _deviceId = 0; _busId = 0; _prepared = false; _feedbackBridge = null; _feedbackToken = null; _feedbackRevoked = false; _presentationPaused = false; _state = LifecycleState.Inactive;
        return RoutingStageOperationResult.Success("SteamDeckDetached");
    }

    private async ValueTask<RoutingStageOperationResult> FailAndRollbackCoreAsync(string reason, CreationTiming timing)
    {
        var rollback = await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
        return RoutingStageOperationResult.Failure($"{reason};Rollback={rollback.Reason}");
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
