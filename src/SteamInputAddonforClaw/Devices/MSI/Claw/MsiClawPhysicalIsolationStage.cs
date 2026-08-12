using System.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Diagnostics;

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
    private string? _executablePath;
    private EntryState[] _entries = [];
    private bool _ownedWhitelist;
    private bool _ambiguousWhitelist;
    private bool _journaledWhitelist;
    private bool _activeMutationJournaled;
    private bool _activeMutationOwned;

    private sealed record Prepared(MsiClawPhysicalInputIdentity Identity, Guid SessionId, string ExecutablePath, bool WhitelistPreExisting, EntryState[] Entries, bool OriginalActive, bool OriginalInverse);
    private sealed class EntryState(string value, bool preExisting)
    {
        internal string Value { get; } = value;
        internal bool PreExisting { get; } = preExisting;
        internal bool Journaled { get; set; }
        internal bool Owned { get; set; }
        internal bool Ambiguous { get; set; }
    }

    internal MsiClawPhysicalIsolationStage(IMsiClawPhysicalInputIdentityProvider input, IRoutingRecoverySessionProvider session, RecoveryManager recovery, IHidHideClient hidHide, Func<string?>? executablePathProvider = null)
    { _input = input ?? throw new ArgumentNullException(nameof(input)); _session = session ?? throw new ArgumentNullException(nameof(session)); _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery)); _hidHide = hidHide ?? throw new ArgumentNullException(nameof(hidHide)); _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath); }

    public RoutingStageKind Kind => RoutingStageKind.PhysicalIsolation;

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Inspect(requireAvailable: false)); }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = _input.CurrentIdentity;
        if (identity is null) return ValueTask.FromResult(Failure("PhysicalInputIdentityMissing"));
        if (string.IsNullOrWhiteSpace(identity.PnpInstanceId) || string.IsNullOrWhiteSpace(identity.PhysicalIdentity)) return ValueTask.FromResult(Failure("PhysicalInputIdentityInvalid"));
        var sessionId = _session.CurrentRecoverySessionId;
        if (sessionId is not { } id) return ValueTask.FromResult(Failure("RecoverySessionMissing"));
        var path = _executablePathProvider();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return ValueTask.FromResult(Failure("ExecutablePathInvalid"));
        var inspection = _hidHide.Inspect();
        if (!inspection.CanPrepareRouting) return ValueTask.FromResult(Failure(inspection.Status.ToString()));

        var executablePath = Path.GetFullPath(path);
        var values = new[] { identity.PhysicalIdentity.Trim(), identity.PnpInstanceId.Trim() }
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new EntryState(value, (inspection.HiddenDeviceEntries ?? []).Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        lock (_sync) _prepared = new(identity, id, executablePath, inspection.ApplicationWhitelist.Contains(executablePath), values, inspection.IsActive, inspection.IsInverseWhitelist);
        return ValueTask.FromResult(Success("Ready"));
    }

    public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested(); Prepared? prepared; lock (_sync) prepared = _prepared;
        if (prepared is null) return ValueTask.FromResult(Failure("PhysicalIsolationNotPrepared"));
        if (!Matches(prepared)) return ValueTask.FromResult(Failure("PhysicalIsolationDrift"));
        var currentInspection = _hidHide.Inspect();
        if (!currentInspection.IsConfigurationReadable || currentInspection.IsActive != prepared.OriginalActive || currentInspection.IsInverseWhitelist != prepared.OriginalInverse)
            return ValueTask.FromResult(Failure("HidHideStateDrift"));
        _sessionId = prepared.SessionId; _executablePath = prepared.ExecutablePath; _entries = prepared.Entries;

        if (!prepared.WhitelistPreExisting)
        {
            if (_recovery.RecordHidHideWhitelistAddition(_sessionId, _executablePath).Status != RecoveryStatus.Success) return ValueTask.FromResult(Failure("WhitelistJournalFailed"));
            _journaledWhitelist = true;
            var addSucceeded = Try(() => _hidHide.AddApplication(_executablePath));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable) { _ambiguousWhitelist = true; return ValueTask.FromResult(Failure("WhitelistMutationAmbiguous")); }
            var present = verification.ApplicationWhitelist.Contains(_executablePath);
            if (!present)
            {
                var completed = _recovery.CompleteHidHideWhitelistAddition(_sessionId, _executablePath).Status == RecoveryStatus.Success;
                if (completed) _journaledWhitelist = false;
                return ValueTask.FromResult(Failure(completed ? (addSucceeded ? "WhitelistVerificationFailed" : "WhitelistAddFailed") : "WhitelistJournalCompletionFailed"));
            }
            _ownedWhitelist = true;
            if (!addSucceeded) return ValueTask.FromResult(Failure("WhitelistAddReportedFailure"));
        }

        foreach (var entry in _entries)
        {
            if (entry.PreExisting) continue;
            if (_recovery.RecordHidHideDeviceAddition(_sessionId, entry.Value).Status != RecoveryStatus.Success) return ValueTask.FromResult(Failure("DeviceJournalFailed"));
            entry.Journaled = true;
            var addSucceeded = Try(() => _hidHide.AddHiddenDevice(entry.Value));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable) { entry.Ambiguous = true; return ValueTask.FromResult(Failure("DeviceMutationAmbiguous")); }
            var present = (verification.HiddenDeviceEntries ?? []).Any(existing => string.Equals(existing, entry.Value, StringComparison.OrdinalIgnoreCase));
            if (!present)
            {
                var completed = _recovery.CompleteHidHideDeviceAddition(_sessionId, entry.Value).Status == RecoveryStatus.Success;
                if (completed) entry.Journaled = false;
                return ValueTask.FromResult(Failure(completed ? (addSucceeded ? "DeviceVerificationFailed" : "DeviceAddFailed") : "DeviceJournalCompletionFailed"));
            }
            entry.Owned = true;
            if (!addSucceeded) return ValueTask.FromResult(Failure("DeviceAddReportedFailure"));
        }
        if (!prepared.OriginalActive)
        {
            var activationInspection = _hidHide.Inspect();
            if (!activationInspection.IsConfigurationReadable || activationInspection.IsActive != prepared.OriginalActive || activationInspection.IsInverseWhitelist != prepared.OriginalInverse)
                return ValueTask.FromResult(Failure("HidHideStateDriftBeforeActivation"));
            if (_recovery.RecordHidHideActiveStateMutation(_sessionId, false).Status != RecoveryStatus.Success)
                return ValueTask.FromResult(Failure("ActiveStateJournalFailed"));
            _activeMutationJournaled = true;
            if (!_hidHide.SetActive(true)) return ValueTask.FromResult(Failure("ActiveStateEnableFailed"));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable || verification.IsInverseWhitelist || !verification.IsActive)
                return ValueTask.FromResult(Failure("ActiveStateEnableUnverified"));
            _activeMutationOwned = true;
        }
        lock (_sync) _prepared = null;
        AppLog.Debug("PhysicalIsolation", "PhysicalIsolation active", ("WhitelistPreExisting", prepared.WhitelistPreExisting), ("WhitelistAddonOwned", _ownedWhitelist), ("Entries", string.Join("|", _entries.Select(entry => $"{entry.Value};PreExisting={entry.PreExisting};AddonOwned={entry.Owned}"))));
        AppLog.Debug("RoutingTrace", "Physical isolation completed.", ("Event", "PhysicalIsolationCompleted"), ("TotalMs", Elapsed(totalStarted)), ("EntryCount", _entries.Length), ("WhitelistAdded", _ownedWhitelist), ("HiddenEntriesAdded", _entries.Count(entry => entry.Owned)), ("ActiveStateChanged", _activeMutationOwned), ("Result", "Success"), ("Reason", "PhysicalIsolationActive"));
        return ValueTask.FromResult(Success("PhysicalIsolationActive"));
    }

    public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        if (_activeMutationOwned || _activeMutationJournaled)
        {
            var inspection = _hidHide.Inspect();
            if (!inspection.IsConfigurationReadable) return ValueTask.FromResult(Failure("ActiveStateRestoreUnverified"));
            if (inspection.IsActive && !_hidHide.SetActive(false)) return ValueTask.FromResult(Failure("ActiveStateRestoreFailed"));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable || verification.IsActive) return ValueTask.FromResult(Failure("ActiveStateRestoreUnverified"));
            if (_recovery.CompleteHidHideActiveStateMutation(_sessionId).Status != RecoveryStatus.Success) return ValueTask.FromResult(Failure("ActiveStateJournalCompletionFailed"));
            _activeMutationOwned = false;
            _activeMutationJournaled = false;
        }
        if (_ambiguousWhitelist || _entries.Any(entry => entry.Ambiguous)) return ValueTask.FromResult(Failure("AmbiguousMutationPending"));
        foreach (var entry in _entries.Reverse().Where(entry => entry.Owned))
        {
            if (!Try(() => _hidHide.RemoveHiddenDevice(entry.Value))) return ValueTask.FromResult(Failure("DeviceRemoveFailed"));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable || (verification.HiddenDeviceEntries ?? []).Any(existing => string.Equals(existing, entry.Value, StringComparison.OrdinalIgnoreCase))) return ValueTask.FromResult(Failure("DeviceRemovalUnverified"));
            if (_recovery.CompleteHidHideDeviceAddition(_sessionId, entry.Value).Status != RecoveryStatus.Success) return ValueTask.FromResult(Failure("DeviceJournalCompletionFailed"));
            entry.Owned = false; entry.Journaled = false;
        }
        foreach (var entry in _entries.Reverse().Where(entry => entry.Journaled && !entry.Owned))
        {
            if (_recovery.CompleteHidHideDeviceAddition(_sessionId, entry.Value).Status != RecoveryStatus.Success)
                return ValueTask.FromResult(Failure("DeviceJournalCompletionFailed"));
            entry.Journaled = false;
        }
        if (_ownedWhitelist)
        {
            if (!Try(() => _hidHide.RemoveApplication(_executablePath!))) return ValueTask.FromResult(Failure("WhitelistRemoveFailed"));
            var verification = _hidHide.Inspect();
            if (!verification.IsConfigurationReadable || verification.ApplicationWhitelist.Contains(_executablePath!)) return ValueTask.FromResult(Failure("WhitelistRemovalUnverified"));
            if (_recovery.CompleteHidHideWhitelistAddition(_sessionId, _executablePath!).Status != RecoveryStatus.Success) return ValueTask.FromResult(Failure("WhitelistJournalCompletionFailed"));
            _ownedWhitelist = false; _journaledWhitelist = false;
        }
        if (_journaledWhitelist && !_ownedWhitelist)
        {
            if (_recovery.CompleteHidHideWhitelistAddition(_sessionId, _executablePath!).Status != RecoveryStatus.Success)
                return ValueTask.FromResult(Failure("WhitelistJournalCompletionFailed"));
            _journaledWhitelist = false;
        }
        lock (_sync) _prepared = null;
        AppLog.Debug("PhysicalIsolation", "PhysicalIsolation restored", ("WhitelistAddonOwned", _ownedWhitelist), ("Entries", string.Join("|", _entries.Select(entry => $"{entry.Value};PreExisting={entry.PreExisting};AddonOwned={entry.Owned}"))));
        AppLog.Debug("RoutingTrace", "Physical isolation rollback completed.", ("Event", "PhysicalIsolationRollbackCompleted"), ("TotalMs", Elapsed(totalStarted)), ("Result", "Success"), ("Reason", "PhysicalIsolationRestored"));
        return ValueTask.FromResult(Success("PhysicalIsolationRestored"));
    }

    private bool Matches(Prepared prepared) => _session.CurrentRecoverySessionId == prepared.SessionId && _input.CurrentIdentity is { } current && current == prepared.Identity;
    private RoutingStageOperationResult Inspect(bool requireAvailable)
    { if (_input.CurrentIdentity is null) return Failure("PhysicalInputIdentityMissing"); var inspection = _hidHide.Inspect(); return requireAvailable && inspection.Status != HidHideInspectionStatus.Available ? Failure(inspection.Status.ToString()) : Success(inspection.Status.ToString()); }
    private static bool Try(Func<bool> mutation) { try { return mutation(); } catch { return false; } }
    private static RoutingStageOperationResult Success(string reason) => RoutingStageOperationResult.Success(reason);
    private static RoutingStageOperationResult Failure(string reason) => RoutingStageOperationResult.Failure(reason);
    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
