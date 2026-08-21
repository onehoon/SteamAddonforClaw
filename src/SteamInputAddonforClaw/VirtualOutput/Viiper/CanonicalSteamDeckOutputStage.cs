using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Buffers.Binary;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// The production Steam output routing stage: the Addon safety shell around the canonical typed
/// Steam Deck path -- prepare/before-PnP snapshot, recovery intent, ownership uncertainty,
/// create/attach the typed Deck device, resolve the exact <c>28DE:1205</c> PnP identity, recovery
/// checkpoint, HidHide inspection, neutral, live publisher -- and the mirrored ordered teardown,
/// driving <see cref="CanonicalSteamDeckSession"/> / <see cref="CanonicalSteamDeckInputPublisher"/>.
/// Failures at any stage (native, publisher, HidHide, or recovery) fail closed rather than continuing
/// through an alternate output.
/// </summary>
internal sealed class CanonicalSteamDeckOutputStage : IRoutingPipelineStage
{
    private enum LifecycleState { Inactive, Prepared, IntentRecorded, Creating, Active, RollingBack }
    private readonly Func<ICanonicalSteamDeckSession> _sessionFactory;
    private readonly IControllerDeviceEnumerator _enumerator;
    private readonly SteamDeckVirtualDeviceIdentityResolver _resolver;
    private readonly AddonOwnedVirtualDeviceTracker _tracker;
    private readonly RecoveryManager _recovery;
    private readonly Func<Guid?> _sessionId;
    private readonly IHidHideClient _hidHide;
    private readonly TimeSpan _pnPTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private IReadOnlyList<ControllerDeviceInfo>? _before;
    private Guid _mutationId;
    private uint _busId;
    private uint _deviceId;
    private IReadOnlyList<ControllerDeviceInfo>? _owned;
    private IReadOnlyList<string> _potentialDeckInstanceIdsAtIdentityFailure = [];
    private LifecycleState _state;
    private CancellationTokenSource? _creationCancellation;
    private CanonicalSteamDeckInputPublisher? _publisher;
    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly IInputReportTickSource? _reportTicks;
    private Func<ValueTask>? _outputFaultHandler;
    private int _outputFaultReported;
    private ICanonicalSteamDeckSession? _canonicalSession;
    private bool _recoveryMutationCompleted;
    private bool _presentationPaused;
    private readonly FeedbackAuthority? _feedbackAuthority;
    private readonly IPhysicalRumbleSink? _physicalRumbleSink;
    private SteamDeckRumbleFeedbackBridge? _feedbackBridge;
    private FeedbackAuthorityToken? _feedbackToken;
    private bool _feedbackCallbackRegistered;
    private bool _feedbackRevoked;
    private bool _feedbackArmed;
    private readonly SteamDeckSystemButtonOverlay _systemButtonOverlay = new();
    private sealed class CreationTiming
    {
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal long CanonicalSessionStartMs { get; set; }
        internal long PnpResolveMs { get; set; }
        internal long RecoveryCheckpointMs { get; set; }
        internal long HidHideInspectionMs { get; set; }
        internal long NeutralReportMs { get; set; }
        internal long PublisherStartMs { get; set; }
    }

    internal CanonicalSteamDeckOutputStage(Func<ICanonicalSteamDeckSession> sessionFactory, IControllerDeviceEnumerator enumerator,
        SteamDeckVirtualDeviceIdentityResolver resolver, AddonOwnedVirtualDeviceTracker tracker, RecoveryManager recovery,
        Func<Guid?> sessionId, IHidHideClient hidHide, IControllerStateSnapshotSource snapshot,
        TimeSpan? pnPTimeout = null, TimeSpan? pollInterval = null, IInputReportTickSource? reportTicks = null,
        FeedbackAuthority? feedbackAuthority = null, IPhysicalRumbleSink? physicalRumbleSink = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory)); _enumerator = enumerator; _resolver = resolver; _tracker = tracker; _recovery = recovery; _sessionId = sessionId; _hidHide = hidHide;
        _pnPTimeout = pnPTimeout ?? TimeSpan.FromSeconds(5); _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); _reportTicks = reportTicks;
        if (physicalRumbleSink is not null && feedbackAuthority is null)
            throw new ArgumentException("A physical rumble sink requires a feedback authority.", nameof(feedbackAuthority));
        _feedbackAuthority = feedbackAuthority; _physicalRumbleSink = physicalRumbleSink;
    }

    public RoutingStageKind Kind => RoutingStageKind.SteamOutput;
    public string Name => "SteamDeckOutput";

    internal void SetOutputFaultHandler(Func<ValueTask> handler) => _outputFaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>
    /// Forwarding seam for requesting a synthetic Steam Deck Quick Access pulse. Called by
    /// <see cref="CenterM.Oem1ActionDispatcher"/> when OEM1 resolves to
    /// <see cref="CenterM.Oem1Action.SteamQuickAccess"/> while canonical Steam Deck routing is
    /// actually active; see docs/VIIPER_MIGRATION_TODO.md SD5.
    /// </summary>
    internal void RequestQuickAccessPulse() => _systemButtonOverlay.RequestQuickAccessPulse();
    internal Task<DeveloperVibrationTestOutcome> RunDeveloperVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken cancellationToken)
    {
        if (!_feedbackArmed || _feedbackBridge is null) return Task.FromResult(new DeveloperVibrationTestOutcome(false, null, null));
        var report = new byte[64];
        switch (command)
        {
            case FrontendVibrationTestCommand.Rumble: report[0] = 0xEB; report[1] = 9; BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(5, 2), 0x8000); BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(7, 2), 0x8000); break;
            case FrontendVibrationTestCommand.Haptic: report[0] = 0xEA; report[3] = 4; report[4] = 128; report[5] = 0; break;
            case FrontendVibrationTestCommand.Stop: report[0] = 0xEB; report[1] = 9; break;
        }
        return _feedbackBridge.ProcessDeveloperTestAsync(report, command is FrontendVibrationTestCommand.Rumble or FrontendVibrationTestCommand.Haptic, cancellationToken);
    }
    /// <summary>Cancels any pending developer-owned delayed STOP and issues a best-effort physical
    /// STOP. No-op if no developer test has ever run (feedback bridge unarmed).</summary>
    internal PhysicalRumbleWriteResult? CancelDeveloperVibrationTest() => _feedbackBridge?.CancelDeveloperTestAndStop();
    internal Action? FeedbackBeforeLease
    {
        set { if (_feedbackBridge is not null) _feedbackBridge.BeforeLease = value; }
    }

    private void ReportOutputFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _outputFaultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Live Steam Deck publishing failed.", exception);
        if (_outputFaultHandler is { } handler)
            _ = Task.Run(() => RunOutputFaultHandlerAsync(handler));
    }

    private static async Task RunOutputFaultHandlerAsync(Func<ValueTask> handler)
    {
        try { await handler().ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppLog.Error("SteamOutput", "Steam Deck output fail-close reconciliation failed.", exception,
                ("Component", "SteamOutput"), ("Action", "RemainFailClosed"), ("Reason", "OutputFaultHandlerFailed"));
        }
    }

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamDeckOutputAvailableForExplicitExperiment"));
    }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessionId() is null) return ValueTask.FromResult(RoutingStageOperationResult.Failure("RecoverySessionUnavailable"));
        if (_state != LifecycleState.Inactive) return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamOutputAlreadyActive"));
        if (_before is not null) return ValueTask.FromResult(RoutingStageOperationResult.Failure("SteamOutputAlreadyPrepared"));
        // Recovery and exact before/after delta semantics require the complete pre-mutation view.
        // Only the repeated post-create stabilization snapshots are target-scoped.
        _before = _enumerator.EnumeratePresentDevices();
        _mutationId = Guid.NewGuid();
        _state = LifecycleState.Prepared;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamOutputPreflightComplete"));
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var timing = new CreationTiming();
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_before is null || _sessionId() is not { } session) return RoutingStageOperationResult.Failure("SteamOutputNotPrepared");
            if (_state != LifecycleState.Prepared) return RoutingStageOperationResult.Failure("SteamOutputAlreadyActive");
            cancellationToken.ThrowIfCancellationRequested();
            _canonicalSession ??= _sessionFactory();
            var intent = _recovery.RecordAddonOwnedVirtualDeviceIntent(session, _mutationId, "steamdeck",
                SteamDeckVirtualDeviceIdentityPolicy.VendorId, SteamDeckVirtualDeviceIdentityPolicy.ProductId,
                _before.Where(device => device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId).Select(device => device.InstanceId));
            if (!intent.IsSafeToContinue) return RoutingStageOperationResult.Failure("VirtualDeviceRecoveryIntentFailed");

            _state = LifecycleState.IntentRecorded;
            _tracker.MarkOwnershipUncertain();
            using var creationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _creationCancellation = creationCancellation;
            var operationToken = creationCancellation.Token;

            operationToken.ThrowIfCancellationRequested();
            _state = LifecycleState.Creating;
            var started = Stopwatch.GetTimestamp();
            bool sessionStarted;
            try
            {
                sessionStarted = _canonicalSession.Start();
            }
            finally { timing.CanonicalSessionStartMs = Elapsed(started); }
            if (!sessionStarted) return await FailAndRollbackCoreAsync("CanonicalSessionStartFailed", timing).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
            _busId = _canonicalSession.BusId ?? 0;
            _deviceId = _canonicalSession.LogicalDeviceId ?? 0;
            operationToken.ThrowIfCancellationRequested();
            started = Stopwatch.GetTimestamp();
            ViiperVirtualDeviceResolution resolved;
            IReadOnlyList<ControllerDeviceInfo> identitySnapshot;
            try { (resolved, identitySnapshot) = await WaitForIdentityAsync(_before, operationToken).ConfigureAwait(false); }
            finally { timing.PnpResolveMs = Elapsed(started); }
            if (!resolved.Succeeded)
            {
                SteamDeckVirtualDeviceIdentityDiagnostics.LogOnFailure(_before, identitySnapshot, resolved, _busId, _deviceId);
                _potentialDeckInstanceIdsAtIdentityFailure = FindPotentialDeckInstanceIds(_before!, identitySnapshot);
                return await FailAndRollbackCoreAsync(resolved.Reason, timing).ConfigureAwait(false);
            }
            _owned = resolved.Devices;
            started = Stopwatch.GetTimestamp();
            RecoveryResult checkpoint;
            try { checkpoint = _recovery.ResolveAddonOwnedVirtualDeviceIdentity(session, _mutationId, _owned.Select(device => device.InstanceId)); }
            finally { timing.RecoveryCheckpointMs = Elapsed(started); }
            if (!checkpoint.IsSafeToContinue) return await FailAndRollbackCoreAsync("VirtualDeviceRecoveryCheckpointFailed", timing).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
            started = Stopwatch.GetTimestamp();
            HidHideInspection hidHideInspection;
            try { hidHideInspection = _hidHide.Inspect(); }
            finally { timing.HidHideInspectionMs = Elapsed(started); }
            if (!hidHideInspection.IsConfigurationReadable) return await FailAndRollbackCoreAsync("HidHideOutputInspectionUnavailable", timing).ConfigureAwait(false);
            var ownedEntries = _owned.SelectMany(device => device.AncestorInstanceIds.Append(device.InstanceId).Append(device.ParentInstanceId ?? string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if ((hidHideInspection.HiddenDeviceEntries ?? []).Any(ownedEntries.Contains))
                return await FailAndRollbackCoreAsync("HidHideOutputAlreadyBlocked", timing).ConfigureAwait(false);
            _tracker.ResolveOwnership(_owned);
            operationToken.ThrowIfCancellationRequested();
            started = Stopwatch.GetTimestamp();
            bool neutralAccepted;
            try { neutralAccepted = _canonicalSession.SetNeutral(); }
            finally { timing.NeutralReportMs = Elapsed(started); }
            if (!neutralAccepted) return await FailAndRollbackCoreAsync("NeutralReportRejected", timing).ConfigureAwait(false);
            if (_physicalRumbleSink is not null && _feedbackAuthority is not null)
            {
                _feedbackArmed = true;
                var token = _feedbackAuthority.Acquire("SteamDeck");
                _feedbackToken = token;
                _feedbackBridge = new SteamDeckRumbleFeedbackBridge(_feedbackAuthority, token, _physicalRumbleSink);
                if (!_canonicalSession.SetOutputCallback(_feedbackBridge.Callback))
                {
                    AppLog.Warn("Rumble", "Steam Deck feedback callback registration unavailable; routing continues without feedback.", null, ("Source", "SteamDeck"));
                    _feedbackBridge.Dispose();
                    _feedbackCallbackRegistered = false;
                    _feedbackArmed = false;
                }
                else
                {
                    _feedbackCallbackRegistered = true;
                    AppLog.Debug("Rumble", "Steam Deck feedback callback registered.", ("Source", "SteamDeck"));
                }
                _feedbackRevoked = false;
            }
            Interlocked.Exchange(ref _outputFaultReported, 0);
            _publisher = new CanonicalSteamDeckInputPublisher(_snapshot, _canonicalSession, _reportTicks,
                fault: ReportOutputFault, systemButtonOverlay: _systemButtonOverlay);
            started = Stopwatch.GetTimestamp();
            try { _publisher.Start(); }
            finally { timing.PublisherStartMs = Elapsed(started); }
            _state = LifecycleState.Active;
            _presentationPaused = false;
            AppLog.Debug("SteamOutput", "SteamDeckOutput active", ("BusId", _busId), ("DeviceId", _deviceId), ("VID", $"{SteamDeckVirtualDeviceIdentityPolicy.VendorId:X4}"), ("PID", $"{SteamDeckVirtualDeviceIdentityPolicy.ProductId:X4}"), ("NeutralAccepted", true));
            AppLog.Debug("RoutingTrace", "Steam Deck output creation completed.", ("Event", "SteamDeckOutputCreated"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(timing.Started)), ("CanonicalSessionStartMs", timing.CanonicalSessionStartMs), ("PnPResolveMs", timing.PnpResolveMs), ("RecoveryCheckpointMs", timing.RecoveryCheckpointMs), ("HidHideInspectionMs", timing.HidHideInspectionMs), ("NeutralReportMs", timing.NeutralReportMs), ("PublisherStartMs", timing.PublisherStartMs), ("OwnedPnpCount", _owned.Count), ("BusId", _busId), ("DeviceId", _deviceId), ("Result", "Success"));
            return RoutingStageOperationResult.Success("SteamDeckCreated");
        }
        catch (OperationCanceledException)
        {
            if (_state is not LifecycleState.Inactive and not LifecycleState.Prepared)
                await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw;
            return RoutingStageOperationResult.Failure("SteamOutputCreationCancelled");
        }
        catch (Exception exception)
        {
            return await FailAndRollbackCoreAsync(exception.GetType().Name, timing).ConfigureAwait(false);
        }
        finally
        {
            _creationCancellation?.Dispose();
            _creationCancellation = null;
            _serial.Release();
        }
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RollbackCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _serial.Release(); }
    }

    /// <summary>
    /// Deck presentation pause: stop the existing live Deck publisher, then -- only once the publisher
    /// is proven fully stopped -- write one neutral report while the Deck stays attached and the
    /// canonical session/route stay active. The production Game Bar presentation path uses this
    /// pause primitive; it owns Deck presentation state only and does not attach or publish Xbox360.
    /// It is serialized against the stage's existing rollback/mutation boundary via
    /// <see cref="_serial"/>. Ordering is safety-critical: neutral must never be written before the
    /// publisher is proven stopped, since a still-running publisher's next tick would immediately
    /// overwrite neutral with live state.
    /// </summary>
    internal async Task<bool> PausePresentationAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != LifecycleState.Active || _canonicalSession is null ||
                _canonicalSession.State != CanonicalSteamDeckSessionState.Active || _publisher is null)
                return false;
            if (_presentationPaused) return false;

            try
            {
                await _publisher.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Publisher shutdown could not be proven -- there may still be an in-flight SetState.
                // Do not write neutral, do not mark paused: fail closed through the existing output
                // fault path rather than attempting recovery/retry heuristics here.
                ReportOutputFault(exception);
                return false;
            }

            bool neutralAccepted;
            try { neutralAccepted = _canonicalSession.SetNeutral(); }
            catch (Exception exception)
            {
                ReportOutputFault(exception);
                return false;
            }
            if (!neutralAccepted)
            {
                // Publisher is proven stopped but the Deck rejected neutral: a real failure boundary.
                // Do not restart the publisher, do not detach here, do not pretend pause succeeded --
                // report through the existing output-fault/fail-close path; outer routing rollback
                // remains responsible for structural teardown.
                ReportOutputFault(new InvalidOperationException("Canonical VIIPER rejected the Steam Deck neutral report during presentation pause."));
                return false;
            }

            _presentationPaused = true;
            return true;
        }
        finally { _serial.Release(); }
    }

    /// <summary>
    /// Deck presentation resume: restart the same already-created Deck publisher against the same
    /// active canonical session/route -- no reattachment, no recreation, no PnP/HidHide/recovery
    /// re-run. The production Game Bar presentation path uses this primitive only after a
    /// successful X360 retirement.
    /// </summary>
    internal async Task<bool> ResumePresentationAsync(CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != LifecycleState.Active || !_presentationPaused || _canonicalSession is null ||
                _canonicalSession.State != CanonicalSteamDeckSessionState.Active || _publisher is null)
                return false;

            try
            {
                _publisher.Start();
            }
            catch (Exception exception)
            {
                // Restart failed -- do not claim live presentation, do not change attachment
                // ownership, do not recreate publisher/session/device. Fail closed.
                ReportOutputFault(exception);
                return false;
            }

            _presentationPaused = false;
            return true;
        }
        finally { _serial.Release(); }
    }

    private async ValueTask<RoutingStageOperationResult> RollbackCoreAsync(CancellationToken cancellationToken)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        long removeMs = 0, pnpAbsenceMs = 0;
        RoutingStageOperationResult RollbackFailure(string reason)
        {
            AppLog.Debug("RoutingTrace", "Steam Deck output rollback failed.", ("Event", "SteamDeckOutputRollbackFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("DetachMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Failure"), ("Reason", reason));
            return RoutingStageOperationResult.Failure(reason);
        }
        if (_state == LifecycleState.Inactive) return RoutingStageOperationResult.Success("SteamOutputAlreadyInactive");
        if (_state == LifecycleState.Prepared)
        {
            _canonicalSession?.Dispose();
            _canonicalSession = null;
            _before = null;
            _presentationPaused = false;
            _state = LifecycleState.Inactive;
            return RoutingStageOperationResult.Success("SteamOutputPreparationCancelled");
        }
        _state = LifecycleState.RollingBack;
        if (_publisher is not null)
        {
            await _publisher.StopAsync().ConfigureAwait(false);
            _publisher = null;
        }
        _systemButtonOverlay.Clear();
        if (_feedbackAuthority is not null && _feedbackToken is not null && !_feedbackRevoked)
        {
            _feedbackAuthority.Revoke();
            _feedbackRevoked = true;
            AppLog.Debug("Rumble", "Steam Deck feedback authority revoked.", ("Source", "SteamDeck"));
        }
        _feedbackBridge?.Dispose();
        if (_feedbackCallbackRegistered)
        {
            var cleared = _canonicalSession?.ClearOutputCallback() ?? false;
            if (!cleared)
            {
                AppLog.Warn("Rumble", "Steam Deck feedback callback clear failed; callback remains rooted and inert while structural teardown continues.", null, ("Operation", "ClearOutputCallback"));
            }
            else
            {
                AppLog.Debug("Rumble", "Steam Deck feedback callback cleared.", ("Source", "SteamDeck"));
            }
            _feedbackCallbackRegistered = false;
            _feedbackArmed = false;
        }
        if (_sessionId() is not { } session) return RollbackFailure("RecoverySessionUnavailable");
        var hadResolvedIdentity = _owned is { Count: > 0 };
        if (_canonicalSession is null) return RollbackFailure("CanonicalSessionUnavailable");
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.Unsafe)
            return RollbackFailure("CanonicalSessionUnsafe");
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.Active ||
            (_canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending &&
             _canonicalSession.PendingCleanupPhase == CanonicalPendingCleanupPhase.AttachmentDetach))
        {
            var removeStarted = Stopwatch.GetTimestamp();
            try
            {
                var removed = _canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending &&
                    _canonicalSession.PendingCleanupPhase == CanonicalPendingCleanupPhase.AttachmentDetach
                    ? _canonicalSession.RetryPendingCleanup()
                    : _canonicalSession.DetachDevice();
                if (!removed) return RollbackFailure(_canonicalSession.State == CanonicalSteamDeckSessionState.Unsafe ? "CanonicalSessionUnsafe" : "VirtualDeviceDetachFailed");
            }
            finally { removeMs = Elapsed(removeStarted); }
        }
        if (!_recoveryMutationCompleted)
        {
            var absenceStarted = Stopwatch.GetTimestamp();
            var exactIdsThatMustBeAbsent = hadResolvedIdentity
                ? _owned!.Select(device => device.InstanceId).ToArray()
                : _potentialDeckInstanceIdsAtIdentityFailure;
            bool verified;
            IReadOnlyList<ControllerDeviceInfo> evidence;
            try { (verified, evidence) = await WaitForSafeTeardownEvidenceAsync(exactIdsThatMustBeAbsent, cancellationToken).ConfigureAwait(false); }
            finally { pnpAbsenceMs = Elapsed(absenceStarted); }
            if (!verified) return RollbackFailure("VirtualDevicePnPStillPresent");
            // `evidence` is the SAME fresh snapshot that just proved both conditions above -- reused
            // here, not re-fetched, so this clear cannot race against a device-state change that
            // happened after evidence was captured.
            if (!_tracker.ClearUncertaintyAfterVerifiedAbsence(evidence, new SteamDeckVirtualDeviceIdentityPolicy(), _before, _owned))
                return RollbackFailure("UnrelatedMatchingVirtualDeviceStillPresent");
            var complete = _recovery.CompleteAddonOwnedVirtualDeviceMutation(session, _mutationId);
            if (!complete.IsSafeToContinue) return RollbackFailure("VirtualDeviceRecoveryCompletionFailed");
            _recoveryMutationCompleted = true;
        }
        AppLog.Debug("SteamOutput", "SteamDeckOutput inactive", ("BusId", _busId), ("DeviceId", _deviceId), ("PnPAbsent", true), ("RecoveryMutationCompleted", _recoveryMutationCompleted));
        AppLog.Debug("RoutingTrace", "Steam Deck output rollback completed.", ("Event", "SteamDeckOutputRollbackCompleted"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("DetachMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Success"), ("Reason", "SteamDeckDetached"));
        _canonicalSession.Dispose();
        _canonicalSession = null;
        _deviceId = 0; _busId = 0; _owned = null; _before = null; _potentialDeckInstanceIdsAtIdentityFailure = [];
        _recoveryMutationCompleted = false;
        _feedbackBridge = null; _feedbackToken = null; _feedbackRevoked = false;
        _presentationPaused = false;
        _state = LifecycleState.Inactive;
        return RoutingStageOperationResult.Success("SteamDeckDetached");
    }

    private static IReadOnlyList<string> FindPotentialDeckInstanceIds(IReadOnlyList<ControllerDeviceInfo> before, IReadOnlyList<ControllerDeviceInfo> snapshot)
    {
        var beforeIds = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot
            .Where(device => device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId && !beforeIds.Contains(device.InstanceId))
            .Select(device => device.InstanceId)
            .ToArray();
    }

    // Steam Deck (28DE:1205) is a composite device exposing separate Keyboard/Mouse/Controller HID
    // interfaces that can enumerate as sibling PnP nodes with real-world timing skew between them.
    // Resolving as soon as the FIRST sibling appears would treat normal composite enumeration as
    // already-complete and strand the later siblings as unowned. Instead, once a candidate logical
    // group is observed we keep polling (reusing the same before/after delta resolver, which always
    // re-evaluates against the original "before" snapshot, so the candidate set naturally grows as
    // more siblings appear) until the logical key and complete InstanceId set remain identical across
    // N consecutive snapshots, and
    // only then treat it as converged. A second, genuinely different logical group appearing at any
    // point during stabilization still surfaces as Ambiguous immediately (fail closed, unchanged).
    private const int IdentityStabilizationConsecutiveSnapshots = 3;

    private async ValueTask<(ViiperVirtualDeviceResolution Result, IReadOnlyList<ControllerDeviceInfo> Snapshot)> WaitForIdentityAsync(IReadOnlyList<ControllerDeviceInfo> before, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _pnPTimeout;
        ViiperVirtualDeviceResolution result = new(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, [], "VirtualDeviceDidNotAppear");
        IReadOnlyList<ControllerDeviceInfo> snapshot;
        var stableConsecutiveSnapshots = 0;
        string? lastCandidateSignature = null;
        while (true)
        {
            snapshot = _enumerator.EnumeratePresentDevices(SteamDeckVirtualDeviceIdentityPolicy.VendorId, SteamDeckVirtualDeviceIdentityPolicy.ProductId);
            result = _resolver.Resolve(before, snapshot);
            if (result.Status == ViiperVirtualDeviceResolutionStatus.Ambiguous)
                return (result, snapshot);
            if (result.Status == ViiperVirtualDeviceResolutionStatus.Resolved)
            {
                var signature = BuildIdentitySignature(result.Devices);
                stableConsecutiveSnapshots = StringComparer.OrdinalIgnoreCase.Equals(signature, lastCandidateSignature)
                    ? stableConsecutiveSnapshots + 1
                    : 1;
                lastCandidateSignature = signature;
                if (stableConsecutiveSnapshots >= IdentityStabilizationConsecutiveSnapshots) return (result, snapshot);
            }
            else
            {
                stableConsecutiveSnapshots = 0;
                lastCandidateSignature = null;
            }
            if (DateTime.UtcNow >= deadline) break;
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
        // A resolved candidate that did not reach the required consecutive identical signatures is
        // not safe evidence of ownership. In particular, do not promote a candidate merely because
        // it was resolved once before disappearing or changing identity near the timeout.
        return (new(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, [], "VirtualDeviceIdentityDidNotStabilize"), snapshot);
    }

    private static string BuildIdentitySignature(IReadOnlyList<ControllerDeviceInfo> devices)
    {
        var logicalKey = ControllerLogicalIdentity.GetLogicalKey(devices[0]);
        var instanceIds = devices.Select(device => device.InstanceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(instanceId => instanceId, StringComparer.OrdinalIgnoreCase);
        return $"{logicalKey}\u001f{string.Join("\u001f", instanceIds)}";
    }

    private async ValueTask<(bool Verified, IReadOnlyList<ControllerDeviceInfo> Snapshot)> WaitForSafeTeardownEvidenceAsync(IEnumerable<string> exactIds, CancellationToken token)
    {
        var wanted = exactIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var beforeIds = (_before ?? []).Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + _pnPTimeout;
        var policy = new SteamDeckVirtualDeviceIdentityPolicy();
        IReadOnlyList<ControllerDeviceInfo> snapshot = [];
        while (true)
        {
            snapshot = _enumerator.EnumeratePresentDevices();
            var currentByInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(snapshot);
            var exactIdsAbsent = !snapshot.Any(device => wanted.Contains(device.InstanceId));
            var noNewMatchingCandidate = !snapshot.Any(device => policy.IsMatchingCandidate(device, currentByInstanceId) && !beforeIds.Contains(device.InstanceId));
            if (exactIdsAbsent && noNewMatchingCandidate) return (true, snapshot);
            if (DateTime.UtcNow >= deadline) return (false, snapshot);
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
    }

    private async ValueTask<RoutingStageOperationResult> FailAndRollbackCoreAsync(string reason, CreationTiming timing)
    {
        AppLog.Debug("RoutingTrace", "Steam Deck output creation failed.", ("Event", "SteamDeckOutputCreationFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("FailedOperation", FailureOperation(reason)), ("TotalMs", Elapsed(timing.Started)), ("CanonicalSessionStartMs", timing.CanonicalSessionStartMs), ("PnPResolveMs", timing.PnpResolveMs), ("RecoveryCheckpointMs", timing.RecoveryCheckpointMs), ("HidHideInspectionMs", timing.HidHideInspectionMs), ("NeutralReportMs", timing.NeutralReportMs), ("PublisherStartMs", timing.PublisherStartMs), ("Reason", reason));
        var rollback = await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
        return RoutingStageOperationResult.Failure($"{reason};Rollback={rollback.Reason}");
    }

    private static string FailureOperation(string reason) => reason switch
    {
        "VirtualDeviceDidNotAppear" or "AmbiguousVirtualDeviceIdentity" or "VirtualDeviceIdentityDidNotStabilize" => "PnPResolve",
        "NeutralReportRejected" => "NeutralReport",
        "CanonicalSessionStartFailed" => "CanonicalSessionStart",
        "VirtualDeviceRecoveryCheckpointFailed" => "RecoveryCheckpoint",
        "HidHideOutputInspectionUnavailable" or "HidHideOutputAlreadyBlocked" => "HidHideInspection",
        "VirtualDeviceRecoveryIntentFailed" => "RecoveryIntent",
        _ => reason.Contains("Rollback", StringComparison.OrdinalIgnoreCase) ? "Rollback" : reason
    };

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
