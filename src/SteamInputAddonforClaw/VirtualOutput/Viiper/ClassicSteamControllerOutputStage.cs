using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class ClassicSteamControllerOutputStage : IRoutingPipelineStage, IPowerTransitionParticipant
{
    private enum LifecycleState { Inactive, Prepared, Creating, Active, RollingBack }
    private readonly IViiperRuntime _runtime;
    private readonly IControllerDeviceEnumerator _enumerator;
    private readonly ViiperVirtualDeviceIdentityResolver _resolver;
    private readonly AddonOwnedVirtualDeviceTracker _tracker;
    private readonly RecoveryManager _recovery;
    private readonly Func<Guid?> _sessionId;
    private readonly IHidHideClient _hidHide;
    private readonly TimeSpan _pnPTimeout;
    private readonly TimeSpan _pollInterval;
    private IReadOnlyList<ControllerDeviceInfo>? _before;
    private Guid _mutationId;
    private uint _deviceId;
    private uint _busId;
    private IReadOnlyList<ControllerDeviceInfo>? _owned;
    private LifecycleState _state;

    internal ClassicSteamControllerOutputStage(IViiperRuntime runtime, IControllerDeviceEnumerator enumerator,
        ViiperVirtualDeviceIdentityResolver resolver, AddonOwnedVirtualDeviceTracker tracker, RecoveryManager recovery,
        Func<Guid?> sessionId, IHidHideClient hidHide, TimeSpan? pnPTimeout = null, TimeSpan? pollInterval = null)
    {
        _runtime = runtime; _enumerator = enumerator; _resolver = resolver; _tracker = tracker; _recovery = recovery; _sessionId = sessionId; _hidHide = hidHide;
        _pnPTimeout = pnPTimeout ?? TimeSpan.FromSeconds(5); _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
    }

    public RoutingStageKind Kind => RoutingStageKind.SteamOutput;
    public string Name => "SteamOutput";

    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        if (_state == LifecycleState.Inactive) return true;
        var result = await RollbackMutationAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_deviceId == 0 && _owned is null);
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
        if (_before is null || _sessionId() is not { } session) return RoutingStageOperationResult.Failure("SteamOutputNotPrepared");
        _state = LifecycleState.Creating;
        cancellationToken.ThrowIfCancellationRequested();
        var intent = _recovery.RecordAddonOwnedVirtualDeviceIntent(session, _mutationId, ViiperRuntimeManager.DeviceType,
            ViiperRuntimeManager.VendorId, ViiperRuntimeManager.ProductId,
            _before.Where(device => device.VendorId == ViiperRuntimeManager.VendorId && device.ProductId == ViiperRuntimeManager.ProductId).Select(device => device.InstanceId));
        if (!intent.IsSafeToContinue) return RoutingStageOperationResult.Failure("VirtualDeviceRecoveryIntentFailed");

            _tracker.MarkOwnershipUncertain();
        try
        {
            _runtime.Start();
            _deviceId = _runtime.CreateDevice();
            _busId = _runtime.BusId;
            var resolved = await WaitForIdentityAsync(_before, cancellationToken).ConfigureAwait(false);
            if (!resolved.Succeeded) return await FailAndRollbackAsync(resolved.Reason).ConfigureAwait(false);
            _owned = resolved.Devices;
            var checkpoint = _recovery.ResolveAddonOwnedVirtualDeviceIdentity(session, _mutationId, _owned.Select(device => device.InstanceId));
            if (!checkpoint.IsSafeToContinue) return await FailAndRollbackAsync("VirtualDeviceRecoveryCheckpointFailed").ConfigureAwait(false);
            var hidHideInspection = _hidHide.Inspect();
            if (!hidHideInspection.IsConfigurationReadable) return await FailAndRollbackAsync("HidHideOutputInspectionUnavailable").ConfigureAwait(false);
            var ownedEntries = _owned.SelectMany(device => device.AncestorInstanceIds.Append(device.InstanceId).Append(device.ParentInstanceId ?? string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if ((hidHideInspection.HiddenDeviceEntries ?? []).Any(ownedEntries.Contains))
                return await FailAndRollbackAsync("HidHideOutputAlreadyBlocked").ConfigureAwait(false);
            _tracker.ResolveOwnership(_owned);
            if (!_runtime.SetNeutral(_deviceId)) return await FailAndRollbackAsync("NeutralReportRejected").ConfigureAwait(false);
            _state = LifecycleState.Active;
            return RoutingStageOperationResult.Success("ClassicSteamControllerCreated");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return await FailAndRollbackAsync(exception.GetType().Name).ConfigureAwait(false); }
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        if (_state == LifecycleState.Inactive) return RoutingStageOperationResult.Success("SteamOutputAlreadyInactive");
        if (_state == LifecycleState.Prepared)
        {
            _before = null;
            _state = LifecycleState.Inactive;
            return RoutingStageOperationResult.Success("SteamOutputPreparationCancelled");
        }
        _state = LifecycleState.RollingBack;
        if (_sessionId() is not { } session) return RoutingStageOperationResult.Failure("RecoverySessionUnavailable");
        var hadResolvedIdentity = _owned is { Count: > 0 };
        if (_deviceId != 0)
        {
            if (!_runtime.RemoveDevice(_busId, _deviceId)) return RoutingStageOperationResult.Failure("VirtualDeviceRemoveFailed");
            var absent = hadResolvedIdentity
                ? await WaitForAbsenceAsync(_owned!.Select(device => device.InstanceId), cancellationToken).ConfigureAwait(false)
                : await WaitForNoNewMatchingCandidatesAsync(cancellationToken).ConfigureAwait(false);
            if (!absent) return RoutingStageOperationResult.Failure("VirtualDevicePnPStillPresent");
        }
        if (!_tracker.ClearUncertaintyAfterVerifiedAbsence(_enumerator.EnumeratePresentDevices(), new ViiperVirtualDeviceIdentityPolicy(), _before, _owned))
            return RoutingStageOperationResult.Failure("UnrelatedMatchingVirtualDeviceStillPresent");
        var complete = _recovery.CompleteAddonOwnedVirtualDeviceMutation(session, _mutationId);
        if (!complete.IsSafeToContinue) return RoutingStageOperationResult.Failure("VirtualDeviceRecoveryCompletionFailed");
        _deviceId = 0; _busId = 0; _owned = null; _before = null; _state = LifecycleState.Inactive; _runtime.StopIfUnused();
        return RoutingStageOperationResult.Success("ClassicSteamControllerRemoved");
    }

    private async ValueTask<ViiperVirtualDeviceResolution> WaitForIdentityAsync(IReadOnlyList<ControllerDeviceInfo> before, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _pnPTimeout;
        ViiperVirtualDeviceResolution result;
        do { result = _resolver.Resolve(before, _enumerator.EnumeratePresentDevices()); if (result.Status != ViiperVirtualDeviceResolutionStatus.NoNewCandidate) return result; await Task.Delay(_pollInterval, token).ConfigureAwait(false); }
        while (DateTime.UtcNow < deadline);
        return result;
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
        while (DateTime.UtcNow < deadline)
        {
            var current = _enumerator.EnumeratePresentDevices();
            if (!current.Any(device => new ViiperVirtualDeviceIdentityPolicy().IsMatchingCandidate(device) && !beforeIds.Contains(device.InstanceId))) return true;
            await Task.Delay(_pollInterval, token).ConfigureAwait(false);
        }
        var final = _enumerator.EnumeratePresentDevices();
        return !final.Any(device => new ViiperVirtualDeviceIdentityPolicy().IsMatchingCandidate(device) && !beforeIds.Contains(device.InstanceId));
    }

    private async ValueTask<RoutingStageOperationResult> FailAndRollbackAsync(string reason)
    { var rollback = await RollbackMutationAsync(CancellationToken.None).ConfigureAwait(false); return RoutingStageOperationResult.Failure($"{reason};Rollback={rollback.Reason}"); }
}
