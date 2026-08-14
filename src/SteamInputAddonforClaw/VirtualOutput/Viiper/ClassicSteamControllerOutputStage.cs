using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class ClassicSteamControllerOutputStage : IRoutingPipelineStage
{
    private enum LifecycleState { Inactive, Prepared, IntentRecorded, Creating, Active, RollingBack }
    private readonly Func<ICanonicalSteamControllerSession> _sessionFactory;
    private readonly IControllerDeviceEnumerator _enumerator;
    private readonly ViiperVirtualDeviceIdentityResolver _resolver;
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
    private IReadOnlyList<string> _potentialGordonInstanceIdsAtIdentityFailure = [];
    private LifecycleState _state;
    private CancellationTokenSource? _creationCancellation;
    private CanonicalSteamControllerInputPublisher? _publisher;
    private readonly IControllerStateSnapshotSource _snapshot;
    private readonly IInputReportTickSource? _reportTicks;
    private Func<ValueTask>? _outputFaultHandler;
    private int _outputFaultReported;
    private ICanonicalSteamControllerSession? _canonicalSession;
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

    internal ClassicSteamControllerOutputStage(Func<ICanonicalSteamControllerSession> sessionFactory, IControllerDeviceEnumerator enumerator,
        ViiperVirtualDeviceIdentityResolver resolver, AddonOwnedVirtualDeviceTracker tracker, RecoveryManager recovery,
        Func<Guid?> sessionId, IHidHideClient hidHide, IControllerStateSnapshotSource snapshot,
        TimeSpan? pnPTimeout = null, TimeSpan? pollInterval = null, IInputReportTickSource? reportTicks = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory)); _enumerator = enumerator; _resolver = resolver; _tracker = tracker; _recovery = recovery; _sessionId = sessionId; _hidHide = hidHide;
        _pnPTimeout = pnPTimeout ?? TimeSpan.FromSeconds(5); _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); _reportTicks = reportTicks;
    }

    // Compatibility seam for the pre-M4B test fixtures only. Production composition uses the
    // canonical session factory overload above and never constructs this adapter.
    internal ClassicSteamControllerOutputStage(IViiperRuntime runtime, IControllerDeviceEnumerator enumerator,
        ViiperVirtualDeviceIdentityResolver resolver, AddonOwnedVirtualDeviceTracker tracker, RecoveryManager recovery,
        Func<Guid?> sessionId, IHidHideClient hidHide, IControllerStateSnapshotSource snapshot,
        TimeSpan? pnPTimeout = null, TimeSpan? pollInterval = null, IInputReportTickSource? reportTicks = null)
        : this(() => new LegacyRuntimeSessionAdapter(runtime), enumerator, resolver, tracker, recovery, sessionId, hidHide, snapshot, pnPTimeout, pollInterval, reportTicks) { }

    public RoutingStageKind Kind => RoutingStageKind.SteamOutput;
    public string Name => "SteamOutput";

    internal void SetOutputFaultHandler(Func<ValueTask> handler) => _outputFaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));

    private void ReportOutputFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _outputFaultReported, 1) != 0) return;
        AppLog.Error("SteamOutput", "Live Classic Steam Controller publishing failed.", exception);
        if (_outputFaultHandler is { } handler)
            _ = Task.Run(async () => await handler().ConfigureAwait(false));
    }

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("SteamOutputAvailableForExplicitExperiment"));
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
            var intent = _recovery.RecordAddonOwnedVirtualDeviceIntent(session, _mutationId, "steamcontroller",
                ViiperVirtualDeviceIdentityPolicy.VendorId, ViiperVirtualDeviceIdentityPolicy.ProductId,
                _before.Where(device => device.VendorId == ViiperVirtualDeviceIdentityPolicy.VendorId && device.ProductId == ViiperVirtualDeviceIdentityPolicy.ProductId).Select(device => device.InstanceId));
            if (!intent.IsSafeToContinue) return RoutingStageOperationResult.Failure("VirtualDeviceRecoveryIntentFailed");

            _state = LifecycleState.IntentRecorded;
            _tracker.MarkOwnershipUncertain();
            using var creationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _creationCancellation = creationCancellation;
            var operationToken = creationCancellation.Token;

            operationToken.ThrowIfCancellationRequested();
            _state = LifecycleState.Creating;
            _canonicalSession ??= _sessionFactory();
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
                ViiperVirtualDeviceIdentityDiagnostics.LogOnFailure(_before, identitySnapshot, _resolver.Policy, resolved, _busId, _deviceId);
                _potentialGordonInstanceIdsAtIdentityFailure = FindPotentialGordonInstanceIds(_before!, identitySnapshot);
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
            _publisher = new CanonicalSteamControllerInputPublisher(_snapshot, _canonicalSession, _reportTicks,
                fault: ReportOutputFault);
            started = Stopwatch.GetTimestamp();
            try { _publisher.Start(); }
            finally { timing.PublisherStartMs = Elapsed(started); }
            _state = LifecycleState.Active;
            AppLog.Debug("SteamOutput", "SteamOutput active", ("BusId", _busId), ("DeviceId", _deviceId), ("VID", $"{ViiperVirtualDeviceIdentityPolicy.VendorId:X4}"), ("PID", $"{ViiperVirtualDeviceIdentityPolicy.ProductId:X4}"), ("NeutralAccepted", true));
            AppLog.Debug("RoutingTrace", "Steam output creation completed.", ("Event", "SteamOutputCreated"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(timing.Started)), ("RuntimeStartMs", timing.RuntimeStartMs), ("CreateDeviceMs", timing.CreateDeviceMs), ("PnPResolveMs", timing.PnpResolveMs), ("RecoveryCheckpointMs", timing.RecoveryCheckpointMs), ("HidHideInspectionMs", timing.HidHideInspectionMs), ("NeutralReportMs", timing.NeutralReportMs), ("PublisherStartMs", timing.PublisherStartMs), ("OwnedPnpCount", _owned.Count), ("BusId", _busId), ("DeviceId", _deviceId), ("Result", "Success"));
            return RoutingStageOperationResult.Success("ClassicSteamControllerCreated");
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
            AppLog.Debug("RoutingTrace", "Steam output rollback failed.", ("Event", "SteamOutputRollbackFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("RemoveDeviceMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Failure"), ("Reason", reason));
            return RoutingStageOperationResult.Failure(reason);
        }
        if (_state == LifecycleState.Inactive) return RoutingStageOperationResult.Success("SteamOutputAlreadyInactive");
        if (_state == LifecycleState.Prepared)
        {
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
        if (_canonicalSession.State is CanonicalSteamControllerSessionState.Unsafe)
            return RollbackFailure("CanonicalSessionUnsafe");
        if (_canonicalSession.State is CanonicalSteamControllerSessionState.Active or CanonicalSteamControllerSessionState.CleanupPending)
        {
            var removeStarted = Stopwatch.GetTimestamp();
            try
            {
                var removed = _canonicalSession.State == CanonicalSteamControllerSessionState.CleanupPending &&
                    _canonicalSession.PendingCleanupPhase == CanonicalPendingCleanupPhase.DeviceRemoval
                    ? _canonicalSession.RetryPendingCleanup()
                    : _canonicalSession.RemoveDevice();
                if (!removed) return RollbackFailure(_canonicalSession.State == CanonicalSteamControllerSessionState.Unsafe ? "CanonicalSessionUnsafe" : "VirtualDeviceRemoveFailed");
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
                    : _potentialGordonInstanceIdsAtIdentityFailure.Count > 0
                        ? await WaitForAbsenceAsync(_potentialGordonInstanceIdsAtIdentityFailure, cancellationToken).ConfigureAwait(false)
                        : await WaitForNoNewMatchingCandidatesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { pnpAbsenceMs = Elapsed(absenceStarted); }
            if (!absent) return RollbackFailure("VirtualDevicePnPStillPresent");
            _pnpAbsenceVerified = true;
        }
        if (!_ownershipUncertaintyCleared)
        {
            if (!_tracker.ClearUncertaintyAfterVerifiedAbsence(_enumerator.EnumeratePresentDevices(), new ViiperVirtualDeviceIdentityPolicy(), _before, _owned))
                return RollbackFailure("UnrelatedMatchingVirtualDeviceStillPresent");
            _ownershipUncertaintyCleared = true;
        }
        if (!_recoveryMutationCompleted)
        {
            var complete = _recovery.CompleteAddonOwnedVirtualDeviceMutation(session, _mutationId);
            if (!complete.IsSafeToContinue) return RollbackFailure("VirtualDeviceRecoveryCompletionFailed");
            _recoveryMutationCompleted = true;
        }
        if (_canonicalSession.State is CanonicalSteamControllerSessionState.DeviceRemoved or CanonicalSteamControllerSessionState.CleanupPending)
        {
            var cleaned = _canonicalSession.State == CanonicalSteamControllerSessionState.CleanupPending
                ? _canonicalSession.RetryPendingCleanup()
                : _canonicalSession.CompleteRuntimeCleanup();
            if (!cleaned) return RollbackFailure("CanonicalSessionCleanupPending");
        }
        AppLog.Debug("SteamOutput", "SteamOutput inactive", ("BusId", _busId), ("DeviceId", _deviceId), ("PnPAbsent", absent), ("RecoveryMutationCompleted", _recoveryMutationCompleted));
        AppLog.Debug("RoutingTrace", "Steam output rollback completed.", ("Event", "SteamOutputRollbackCompleted"), ("RoutingExecution", RoutingTraceContext.Current), ("TotalMs", Elapsed(totalStarted)), ("RemoveDeviceMs", removeMs), ("PnPAbsenceMs", pnpAbsenceMs), ("Result", "Success"), ("Reason", "ClassicSteamControllerRemoved"));
        _canonicalSession.Dispose();
        _canonicalSession = null;
        _deviceId = 0; _busId = 0; _owned = null; _before = null; _potentialGordonInstanceIdsAtIdentityFailure = [];
        _pnpAbsenceVerified = false; _ownershipUncertaintyCleared = false; _recoveryMutationCompleted = false;
        _state = LifecycleState.Inactive;
        return RoutingStageOperationResult.Success("ClassicSteamControllerRemoved");
    }

    private static IReadOnlyList<string> FindPotentialGordonInstanceIds(IReadOnlyList<ControllerDeviceInfo> before, IReadOnlyList<ControllerDeviceInfo> snapshot)
    {
        var beforeIds = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot
            .Where(device => device.VendorId == ViiperVirtualDeviceIdentityPolicy.VendorId && device.ProductId == ViiperVirtualDeviceIdentityPolicy.ProductId && !beforeIds.Contains(device.InstanceId))
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
        var policy = new ViiperVirtualDeviceIdentityPolicy();
        while (DateTime.UtcNow < deadline)
        {
            var current = _enumerator.EnumeratePresentDevices();
            var currentByInstanceId = ViiperVirtualDeviceIdentityPolicy.BuildInstanceIndex(current);
            if (!current.Any(device => policy.IsMatchingCandidate(device, currentByInstanceId) && !beforeIds.Contains(device.InstanceId))) return true;
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
        var final = _enumerator.EnumeratePresentDevices();
        var finalByInstanceId = ViiperVirtualDeviceIdentityPolicy.BuildInstanceIndex(final);
        return !final.Any(device => policy.IsMatchingCandidate(device, finalByInstanceId) && !beforeIds.Contains(device.InstanceId));
    }

    private async ValueTask<RoutingStageOperationResult> FailAndRollbackCoreAsync(string reason)
    {
        var timing = _creationTiming;
        AppLog.Debug("RoutingTrace", "Steam output creation failed.", ("Event", "SteamOutputCreationFailed"), ("RoutingExecution", RoutingTraceContext.Current), ("FailedOperation", FailureOperation(reason)), ("TotalMs", timing is null ? 0 : Elapsed(timing.Started)), ("RuntimeStartMs", timing?.RuntimeStartMs ?? 0), ("CreateDeviceMs", timing?.CreateDeviceMs ?? 0), ("PnPResolveMs", timing?.PnpResolveMs ?? 0), ("RecoveryCheckpointMs", timing?.RecoveryCheckpointMs ?? 0), ("HidHideInspectionMs", timing?.HidHideInspectionMs ?? 0), ("NeutralReportMs", timing?.NeutralReportMs ?? 0), ("PublisherStartMs", timing?.PublisherStartMs ?? 0), ("Reason", reason));
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

    private sealed class LegacyRuntimeSessionAdapter(IViiperRuntime runtime) : ICanonicalSteamControllerSession
    {
        private uint _deviceId;
        private bool _started;
        public CanonicalSteamControllerSessionState State { get; private set; } = CanonicalSteamControllerSessionState.Clean;
        public CanonicalPendingCleanupPhase PendingCleanupPhase => CanonicalPendingCleanupPhase.None;
        public uint? BusId => _started ? runtime.BusId : null;
        public uint? LogicalDeviceId => _deviceId == 0 ? null : _deviceId;
        public bool Start()
        {
            runtime.Start();
            _deviceId = runtime.CreateDevice();
            _started = true;
            State = CanonicalSteamControllerSessionState.Active;
            return true;
        }
        public bool SetState(SteamControllerDeviceState state) => runtime.SetInput(_deviceId, new byte[64]);
        public bool SetNeutral() => runtime.SetNeutral(_deviceId);
        public bool RemoveDevice()
        {
            var result = runtime.RemoveDevice(runtime.BusId, _deviceId);
            if (!result.DeviceRemoved) return false;
            State = CanonicalSteamControllerSessionState.DeviceRemoved;
            return true;
        }
        public bool RetryPendingCleanup() => false;
        public bool CompleteRuntimeCleanup()
        {
            if (State != CanonicalSteamControllerSessionState.DeviceRemoved) return false;
            runtime.StopIfUnused();
            _started = false;
            State = CanonicalSteamControllerSessionState.Clean;
            return true;
        }
        public void Dispose() => runtime.Dispose();
    }
}
