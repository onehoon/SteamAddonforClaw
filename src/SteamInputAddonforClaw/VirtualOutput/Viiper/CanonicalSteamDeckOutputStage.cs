using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Steam Deck counterpart to <see cref="ClassicSteamControllerOutputStage"/>: the same Addon safety
/// shell (prepare/before-PnP snapshot -> recovery intent -> uncertain ownership -> create/attach ->
/// resolve PnP identity -> recovery checkpoint -> HidHide inspection -> neutral -> live publisher,
/// and the mirrored teardown ordering), driving the typed Steam Deck ABI
/// (<see cref="CanonicalSteamDeckSession"/> / <see cref="CanonicalSteamDeckInputPublisher"/>)
/// against exact identity <c>28DE:1205</c> instead of Gordon's <c>28DE:1102</c>.
/// </summary>
/// <remarks>
/// This is a side-by-side SD2 implementation, not a production cutover: see
/// docs/VIIPER_MIGRATION_TODO.md SD2/SD3/SD4. It is intentionally not wired into the normal
/// production routing pipeline; see the Developer-Test-only composition seam in App.xaml.cs.
/// </remarks>
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
    private bool _pnpAbsenceVerified;
    private bool _ownershipUncertaintyCleared;
    private bool _recoveryMutationCompleted;
    private CreationTiming? _creationTiming;

    private sealed class CreationTiming
    {
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal long RuntimeStartMs { get; set; }
        internal long CreateDeviceMs { get; set; }
        internal long PnpResolveMs { get; set; }
        internal long RecoveryCheckpointMs { get; set; }
        internal long HidHideInspectionMs { get; set; }
        internal long NeutralReportMs { get; set; }
        internal long PublisherStartMs { get; set; }
    }

    internal CanonicalSteamDeckOutputStage(Func<ICanonicalSteamDeckSession> sessionFactory, IControllerDeviceEnumerator enumerator,
        SteamDeckVirtualDeviceIdentityResolver resolver, AddonOwnedVirtualDeviceTracker tracker, RecoveryManager recovery,
        Func<Guid?> sessionId, IHidHideClient hidHide, IControllerStateSnapshotSource snapshot,
        TimeSpan? pnPTimeout = null, TimeSpan? pollInterval = null, IInputReportTickSource? reportTicks = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory)); _enumerator = enumerator; _resolver = resolver; _tracker = tracker; _recovery = recovery; _sessionId = sessionId; _hidHide = hidHide;
        _pnPTimeout = pnPTimeout ?? TimeSpan.FromSeconds(5); _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); _reportTicks = reportTicks;
    }

    public RoutingStageKind Kind => RoutingStageKind.SteamOutput;
    public string Name => "SteamDeckOutput";

    internal void SetOutputFaultHandler(Func<ValueTask> handler) => _outputFaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));

    private void ReportOutputFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _outputFaultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Live Steam Deck publishing failed.", exception);
        if (_outputFaultHandler is { } handler)
            _ = Task.Run(async () => await handler().ConfigureAwait(false));
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
        _before = _enumerator.EnumeratePresentDevices();
        _mutationId = Guid.NewGuid();
        _state = LifecycleState.Prepared;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamOutputPreflightComplete"));
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var timing = new CreationTiming();
        _creationTiming = timing;
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
            try
            {
                if (!_canonicalSession.Start()) return await FailAndRollbackCoreAsync("CanonicalSessionStartFailed").ConfigureAwait(false);
            }
            finally { timing.RuntimeStartMs = Elapsed(started); }
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
                return await FailAndRollbackCoreAsync(resolved.Reason).ConfigureAwait(false);
            }
            _owned = resolved.Devices;
            started = Stopwatch.GetTimestamp();
            RecoveryResult checkpoint;
            try { checkpoint = _recovery.ResolveAddonOwnedVirtualDeviceIdentity(session, _mutationId, _owned.Select(device => device.InstanceId)); }
            finally { timing.RecoveryCheckpointMs = Elapsed(started); }
            if (!checkpoint.IsSafeToContinue) return await FailAndRollbackCoreAsync("VirtualDeviceRecoveryCheckpointFailed").ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
            started = Stopwatch.GetTimestamp();
            HidHideInspection hidHideInspection;
            try { hidHideInspection = _hidHide.Inspect(); }
            finally { timing.HidHideInspectionMs = Elapsed(started); }
            if (!hidHideInspection.IsConfigurationReadable) return await FailAndRollbackCoreAsync("HidHideOutputInspectionUnavailable").ConfigureAwait(false);
            var ownedEntries = _owned.SelectMany(device => device.AncestorInstanceIds.Append(device.InstanceId).Append(device.ParentInstanceId ?? string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if ((hidHideInspection.HiddenDeviceEntries ?? []).Any(ownedEntries.Contains))
                return await FailAndRollbackCoreAsync("HidHideOutputAlreadyBlocked").ConfigureAwait(false);
            _tracker.ResolveOwnership(_owned);
            operationToken.ThrowIfCancellationRequested();
            started = Stopwatch.GetTimestamp();
            bool neutralAccepted;
            try { neutralAccepted = _canonicalSession.SetNeutral(); }
            finally { timing.NeutralReportMs = Elapsed(started); }
            if (!neutralAccepted) return await FailAndRollbackCoreAsync("NeutralReportRejected").ConfigureAwait(false);
            Interlocked.Exchange(ref _outputFaultReported, 0);
            _publisher = new CanonicalSteamDeckInputPublisher(_snapshot, _canonicalSession, _reportTicks,
                fault: ReportOutputFault);
            started = Stopwatch.GetTimestamp();
            try { _publisher.Start(); }
            finally { timing.PublisherStartMs = Elapsed(started); }
            _state = LifecycleState.Active;
            AppLog.Debug("SteamOutput", "SteamDeckOutput active", ("BusId", _busId), ("DeviceId", _deviceId), ("VID", $"{SteamDeckVirtualDeviceIdentityPolicy.VendorId:X4}"), ("PID", $"{SteamDeckVirtualDeviceIdentityPolicy.ProductId:X4}"), ("NeutralAccepted", true));
            AppLog.Debug("RoutingTrace", "Steam Deck output creation completed.", ("Event", "SteamDeckOutputCreated"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(timing.Started)), ("RuntimeStartMs", timing.RuntimeStartMs), ("CreateDeviceMs", timing.CreateDeviceMs), ("PnPResolveMs", timing.PnpResolveMs), ("RecoveryCheckpointMs", timing.RecoveryCheckpointMs), ("HidHideInspectionMs", timing.HidHideInspectionMs), ("NeutralReportMs", timing.NeutralReportMs), ("PublisherStartMs", timing.PublisherStartMs), ("OwnedPnpCount", _owned.Count), ("BusId", _busId), ("DeviceId", _deviceId), ("Result", "Success"));
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
            return await FailAndRollbackCoreAsync(exception.GetType().Name).ConfigureAwait(false);
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

    private async ValueTask<RoutingStageOperationResult> RollbackCoreAsync(CancellationToken cancellationToken)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        long removeMs = 0, pnpAbsenceMs = 0;
        RoutingStageOperationResult RollbackFailure(string reason)
        {
            AppLog.Debug("RoutingTrace", "Steam Deck output rollback failed.", ("Event", "SteamDeckOutputRollbackFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("RemoveDeviceMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Failure"), ("Reason", reason));
            return RoutingStageOperationResult.Failure(reason);
        }
        if (_state == LifecycleState.Inactive) return RoutingStageOperationResult.Success("SteamOutputAlreadyInactive");
        if (_state == LifecycleState.Prepared)
        {
            _canonicalSession?.Dispose();
            _canonicalSession = null;
            _before = null;
            _state = LifecycleState.Inactive;
            return RoutingStageOperationResult.Success("SteamOutputPreparationCancelled");
        }
        _state = LifecycleState.RollingBack;
        if (_publisher is not null)
        {
            await _publisher.StopAsync().ConfigureAwait(false);
            _publisher = null;
        }
        if (_sessionId() is not { } session) return RollbackFailure("RecoverySessionUnavailable");
        var hadResolvedIdentity = _owned is { Count: > 0 };
        var absent = _pnpAbsenceVerified;
        if (_canonicalSession is null) return RollbackFailure("CanonicalSessionUnavailable");
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.Unsafe)
            return RollbackFailure("CanonicalSessionUnsafe");
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.Active ||
            (_canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending &&
             _canonicalSession.PendingCleanupPhase == CanonicalPendingCleanupPhase.DeviceRemoval))
        {
            var removeStarted = Stopwatch.GetTimestamp();
            try
            {
                var removed = _canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending &&
                    _canonicalSession.PendingCleanupPhase == CanonicalPendingCleanupPhase.DeviceRemoval
                    ? _canonicalSession.RetryPendingCleanup()
                    : _canonicalSession.RemoveDevice();
                if (!removed) return RollbackFailure(_canonicalSession.State == CanonicalSteamDeckSessionState.Unsafe ? "CanonicalSessionUnsafe" : "VirtualDeviceRemoveFailed");
            }
            finally { removeMs = Elapsed(removeStarted); }
        }
        if (!_pnpAbsenceVerified)
        {
            var absenceStarted = Stopwatch.GetTimestamp();
            try
            {
                absent = hadResolvedIdentity
                    ? await WaitForAbsenceAsync(_owned!.Select(device => device.InstanceId), cancellationToken).ConfigureAwait(false)
                    : _potentialDeckInstanceIdsAtIdentityFailure.Count > 0
                        ? await WaitForAbsenceAsync(_potentialDeckInstanceIdsAtIdentityFailure, cancellationToken).ConfigureAwait(false)
                        : await WaitForNoNewMatchingCandidatesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { pnpAbsenceMs = Elapsed(absenceStarted); }
            if (!absent) return RollbackFailure("VirtualDevicePnPStillPresent");
            _pnpAbsenceVerified = true;
        }
        if (!_ownershipUncertaintyCleared)
        {
            if (!_tracker.ClearUncertaintyAfterVerifiedAbsence(_enumerator.EnumeratePresentDevices(), new SteamDeckVirtualDeviceIdentityPolicy(), _before, _owned))
                return RollbackFailure("UnrelatedMatchingVirtualDeviceStillPresent");
            _ownershipUncertaintyCleared = true;
        }
        if (!_recoveryMutationCompleted)
        {
            var complete = _recovery.CompleteAddonOwnedVirtualDeviceMutation(session, _mutationId);
            if (!complete.IsSafeToContinue) return RollbackFailure("VirtualDeviceRecoveryCompletionFailed");
            _recoveryMutationCompleted = true;
        }
        if (_canonicalSession.State is CanonicalSteamDeckSessionState.DeviceRemoved or CanonicalSteamDeckSessionState.CleanupPending)
        {
            var cleaned = _canonicalSession.State == CanonicalSteamDeckSessionState.CleanupPending
                ? _canonicalSession.RetryPendingCleanup()
                : _canonicalSession.CompleteRuntimeCleanup();
            if (!cleaned) return RollbackFailure("CanonicalSessionCleanupPending");
        }
        AppLog.Debug("SteamOutput", "SteamDeckOutput inactive", ("BusId", _busId), ("DeviceId", _deviceId), ("PnPAbsent", absent), ("RecoveryMutationCompleted", _recoveryMutationCompleted));
        AppLog.Debug("RoutingTrace", "Steam Deck output rollback completed.", ("Event", "SteamDeckOutputRollbackCompleted"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("RemoveDeviceMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Success"), ("Reason", "SteamDeckRemoved"));
        _canonicalSession.Dispose();
        _canonicalSession = null;
        _deviceId = 0; _busId = 0; _owned = null; _before = null; _potentialDeckInstanceIdsAtIdentityFailure = [];
        _pnpAbsenceVerified = false; _ownershipUncertaintyCleared = false; _recoveryMutationCompleted = false;
        _state = LifecycleState.Inactive;
        return RoutingStageOperationResult.Success("SteamDeckRemoved");
    }

    private static IReadOnlyList<string> FindPotentialDeckInstanceIds(IReadOnlyList<ControllerDeviceInfo> before, IReadOnlyList<ControllerDeviceInfo> snapshot)
    {
        var beforeIds = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot
            .Where(device => device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId && !beforeIds.Contains(device.InstanceId))
            .Select(device => device.InstanceId)
            .ToArray();
    }

    private async ValueTask<(ViiperVirtualDeviceResolution Result, IReadOnlyList<ControllerDeviceInfo> Snapshot)> WaitForIdentityAsync(IReadOnlyList<ControllerDeviceInfo> before, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _pnPTimeout;
        ViiperVirtualDeviceResolution result;
        IReadOnlyList<ControllerDeviceInfo> snapshot;
        do
        {
            snapshot = _enumerator.EnumeratePresentDevices();
            result = _resolver.Resolve(before, snapshot);
            if (result.Status != ViiperVirtualDeviceResolutionStatus.NoNewCandidate) return (result, snapshot);
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);
        return (result, snapshot);
    }

    private async ValueTask<bool> WaitForAbsenceAsync(IEnumerable<string> ids, CancellationToken token)
    {
        var wanted = ids.ToHashSet(StringComparer.OrdinalIgnoreCase); var deadline = DateTime.UtcNow + _pnPTimeout;
        while (DateTime.UtcNow < deadline)
        { if (!_enumerator.EnumeratePresentDevices().Any(device => wanted.Contains(device.InstanceId))) return true; await Task.Delay(_pollInterval, token).ConfigureAwait(false); }
        return !_enumerator.EnumeratePresentDevices().Any(device => wanted.Contains(device.InstanceId));
    }

    private async ValueTask<bool> WaitForNoNewMatchingCandidatesAsync(CancellationToken token)
    {
        if (_before is null) return false;
        var beforeIds = _before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + _pnPTimeout;
        var policy = new SteamDeckVirtualDeviceIdentityPolicy();
        while (DateTime.UtcNow < deadline)
        {
            var current = _enumerator.EnumeratePresentDevices();
            var currentByInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(current);
            if (!current.Any(device => policy.IsMatchingCandidate(device, currentByInstanceId) && !beforeIds.Contains(device.InstanceId))) return true;
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
        var final = _enumerator.EnumeratePresentDevices();
        var finalByInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(final);
        return !final.Any(device => policy.IsMatchingCandidate(device, finalByInstanceId) && !beforeIds.Contains(device.InstanceId));
    }

    private async ValueTask<RoutingStageOperationResult> FailAndRollbackCoreAsync(string reason)
    {
        var timing = _creationTiming;
        AppLog.Debug("RoutingTrace", "Steam Deck output creation failed.", ("Event", "SteamDeckOutputCreationFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("FailedOperation", FailureOperation(reason)), ("TotalMs", timing is null ? 0 : Elapsed(timing.Started)), ("RuntimeStartMs", timing?.RuntimeStartMs ?? 0), ("CreateDeviceMs", timing?.CreateDeviceMs ?? 0), ("PnPResolveMs", timing?.PnpResolveMs ?? 0), ("RecoveryCheckpointMs", timing?.RecoveryCheckpointMs ?? 0), ("HidHideInspectionMs", timing?.HidHideInspectionMs ?? 0), ("NeutralReportMs", timing?.NeutralReportMs ?? 0), ("PublisherStartMs", timing?.PublisherStartMs ?? 0), ("Reason", reason));
        var rollback = await RollbackCoreAsync(CancellationToken.None).ConfigureAwait(false);
        return RoutingStageOperationResult.Failure($"{reason};Rollback={rollback.Reason}");
    }

    private static string FailureOperation(string reason) => reason switch
    {
        "VirtualDeviceDidNotAppear" or "AmbiguousVirtualDeviceIdentity" => "PnPResolve",
        "NeutralReportRejected" => "NeutralReport",
        "VirtualDeviceRecoveryCheckpointFailed" => "RecoveryCheckpoint",
        "HidHideOutputInspectionUnavailable" or "HidHideOutputAlreadyBlocked" => "HidHideInspection",
        "VirtualDeviceRecoveryIntentFailed" => "RecoveryIntent",
        _ => reason.Contains("Rollback", StringComparison.OrdinalIgnoreCase) ? "Rollback" : reason
    };

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
