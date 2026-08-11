using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawPhysicalIsolationStage : IRoutingPipelineStage
{
    private readonly IMsiClawPhysicalInputIdentityProvider _input;
    private readonly IRoutingRecoverySessionProvider _session;
    private readonly RecoveryManager _recovery;
    private readonly IHidHideClient _hidHide;
    private readonly Func<string?> _executablePathProvider;
    private readonly Lock _sync = new();
    private Prepared? _prepared;
    private Guid _sessionId;
    private string? _exe;
    private string? _device;
    private bool _ownedWhitelist;
    private bool _ownedDevice;

    private sealed record Prepared(MsiClawPhysicalInputIdentity Identity, Guid SessionId, string ExecutablePath, bool HasWhitelist, bool HasDevice);

    internal MsiClawPhysicalIsolationStage(IMsiClawPhysicalInputIdentityProvider input, IRoutingRecoverySessionProvider session, RecoveryManager recovery, IHidHideClient hidHide, Func<string?>? executablePathProvider = null)
    { _input = input; _session = session; _recovery = recovery; _hidHide = hidHide; _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath); }

    public RoutingStageKind Kind => RoutingStageKind.PhysicalIsolation;

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Inspect(requireAvailable: false)); }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = _input.CurrentIdentity;
        if (identity is null) return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalInputIdentityMissing"));
        if (string.IsNullOrWhiteSpace(identity.PnpInstanceId) || string.IsNullOrWhiteSpace(identity.PhysicalIdentity)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalInputIdentityInvalid"));
        var sessionId = _session.CurrentRecoverySessionId;
        if (sessionId is not { } id) return ValueTask.FromResult(RoutingStageOperationResult.Failure("RecoverySessionMissing"));
        var path = _executablePathProvider();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("ExecutablePathInvalid"));
        var inspection = _hidHide.Inspect();
        if (inspection.Status != HidHideInspectionStatus.Available) return ValueTask.FromResult(RoutingStageOperationResult.Failure(inspection.Status.ToString()));
        var normalized = Path.GetFullPath(path);
        var device = identity.PnpInstanceId.Trim();
        lock (_sync) _prepared = new(identity, id, normalized, inspection.ApplicationWhitelist.Contains(normalized), (inspection.HiddenDeviceEntries ?? []).Any(x => string.Equals(x, device, StringComparison.OrdinalIgnoreCase)));
        return ValueTask.FromResult(RoutingStageOperationResult.Success("Ready"));
    }

    public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Prepared? p; lock (_sync) p = _prepared;
        if (p is null) return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalIsolationNotPrepared"));
        if (!Matches(p)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalIsolationDrift"));
        _sessionId = p.SessionId; _exe = p.ExecutablePath; _device = p.Identity.PnpInstanceId.Trim();
        if (!p.HasWhitelist)
        {
            if (_recovery.RecordHidHideWhitelistAddition(_sessionId, _exe).Status != RecoveryStatus.Success) return ValueTask.FromResult(RoutingStageOperationResult.Failure("WhitelistJournalFailed"));
            var added = false; try { added = _hidHide.AddApplication(_exe); } catch { }
            var verify = _hidHide.Inspect();
            if (verify.IsConfigurationReadable && verify.ApplicationWhitelist.Contains(_exe)) _ownedWhitelist = true;
            else if (!verify.IsConfigurationReadable) return ValueTask.FromResult(RoutingStageOperationResult.Failure("WhitelistMutationAmbiguous"));
            else { _recovery.CompleteHidHideWhitelistAddition(_sessionId, _exe); return ValueTask.FromResult(RoutingStageOperationResult.Failure(added ? "WhitelistVerificationFailed" : "WhitelistAddFailed")); }
        }
        if (!p.HasDevice)
        {
            if (_recovery.RecordHidHideDeviceAddition(_sessionId, _device).Status != RecoveryStatus.Success) return ValueTask.FromResult(RoutingStageOperationResult.Failure("DeviceJournalFailed"));
            var added = false; try { added = _hidHide.AddHiddenDevice(_device); } catch { }
            var verify = _hidHide.Inspect();
            if (verify.IsConfigurationReadable && (verify.HiddenDeviceEntries ?? []).Any(x => string.Equals(x, _device, StringComparison.OrdinalIgnoreCase))) _ownedDevice = true;
            else if (!verify.IsConfigurationReadable) return ValueTask.FromResult(RoutingStageOperationResult.Failure("DeviceMutationAmbiguous"));
            else { _recovery.CompleteHidHideDeviceAddition(_sessionId, _device); return ValueTask.FromResult(RoutingStageOperationResult.Failure(added ? "DeviceVerificationFailed" : "DeviceAddFailed")); }
        }
        lock (_sync) _prepared = null;
        return ValueTask.FromResult(RoutingStageOperationResult.Success("PhysicalIsolationActive"));
    }

    public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_ownedDevice)
        {
            if (!_hidHide.RemoveHiddenDevice(_device!)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("DeviceRemoveFailed"));
            var check = _hidHide.Inspect(); if (!check.IsConfigurationReadable || (check.HiddenDeviceEntries ?? []).Any(x => string.Equals(x, _device, StringComparison.OrdinalIgnoreCase))) return ValueTask.FromResult(RoutingStageOperationResult.Failure("DeviceRemovalUnverified"));
            if (_recovery.CompleteHidHideDeviceAddition(_sessionId, _device!).Status != RecoveryStatus.Success) return ValueTask.FromResult(RoutingStageOperationResult.Failure("DeviceJournalCompletionFailed"));
            _ownedDevice = false;
        }
        if (_ownedWhitelist)
        {
            if (!_hidHide.RemoveApplication(_exe!)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("WhitelistRemoveFailed"));
            var check = _hidHide.Inspect(); if (!check.IsConfigurationReadable || check.ApplicationWhitelist.Contains(_exe!)) return ValueTask.FromResult(RoutingStageOperationResult.Failure("WhitelistRemovalUnverified"));
            if (_recovery.CompleteHidHideWhitelistAddition(_sessionId, _exe!).Status != RecoveryStatus.Success) return ValueTask.FromResult(RoutingStageOperationResult.Failure("WhitelistJournalCompletionFailed"));
            _ownedWhitelist = false;
        }
        _prepared = null; return ValueTask.FromResult(RoutingStageOperationResult.Success("PhysicalIsolationRestored"));
    }

    private bool Matches(Prepared p) => _session.CurrentRecoverySessionId == p.SessionId && _input.CurrentIdentity is { } now && now == p.Identity;
    private RoutingStageOperationResult Inspect(bool requireAvailable)
    { var i = _input.CurrentIdentity; if (i is null) return RoutingStageOperationResult.Failure("PhysicalInputIdentityMissing"); var s = _hidHide.Inspect(); if (requireAvailable && s.Status != HidHideInspectionStatus.Available) return RoutingStageOperationResult.Failure(s.Status.ToString()); return RoutingStageOperationResult.Success(s.Status.ToString()); }
}
