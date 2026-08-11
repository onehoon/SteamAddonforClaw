using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Recovery;

namespace SteamInputAddonforClaw.Diagnostics;

internal sealed class M1M2DiagnosticCoordinator : IAsyncDisposable
{
    private readonly IMsiClawInputDiagnostic _inputSource;
    private readonly IHidHideClient _hidHideClient;
    private readonly RecoveryManager _recoveryManager;
    private readonly string _executablePath;
    private readonly Func<IReadOnlyList<string>> _msiDirectInputHidInstanceIds;
    private readonly Lock _leaseSync = new();
    private bool _ownsWhitelistLease;
    private Guid? _recoverySessionId;

    public M1M2DiagnosticCoordinator(
        IMsiClawInputDiagnostic inputSource,
        IHidHideClient hidHideClient,
        RecoveryManager recoveryManager,
        string executablePath,
        Func<IReadOnlyList<string>> msiDirectInputHidInstanceIds)
    {
        _inputSource = inputSource;
        _hidHideClient = hidHideClient;
        _recoveryManager = recoveryManager;
        _executablePath = Path.GetFullPath(executablePath);
        _msiDirectInputHidInstanceIds = msiDirectInputHidInstanceIds;
        _inputSource.TestCompleted += OnTestCompleted;
    }

    public MsiClawInputStartResult Start()
    {
        if (_inputSource.IsRunning) return _inputSource.Start();

        var inspection = _hidHideClient.Inspect();
        var msiInstanceIds = ResolveMsiDirectInputHidInstanceIds();
        var msiIsHidden = msiInstanceIds.Any(instanceId => inspection.HiddenDeviceEntries?.Contains(instanceId, StringComparer.OrdinalIgnoreCase) == true);
        AppLog.Info("HidHide", "HidHide diagnostic inspection completed.",
            ("Status", inspection.Status), ("WhitelistCount", inspection.ApplicationWhitelist.Count),
            ("HiddenDeviceCount", inspection.HiddenDeviceEntries?.Count ?? 0),
            ("MsiDirectInputTargetCount", msiInstanceIds.Count), ("MsiPid1902Hidden", msiIsHidden),
            ("ExecutablePath", _executablePath), ("AlreadyAllowed", inspection.ApplicationWhitelist.Contains(_executablePath)));

        if (inspection.Status == HidHideInspectionStatus.Disabled)
            return _inputSource.Start();
        if (inspection.Status is HidHideInspectionStatus.NotInstalled or HidHideInspectionStatus.ConfigurationUnavailable or HidHideInspectionStatus.InverseWhitelist)
            return new(MsiClawInputStartStatus.InitializationFailed, $"HidHide access is unavailable ({inspection.Status}). No controller settings were changed.");
        if (!msiIsHidden)
            return _inputSource.Start();

        if (!inspection.ApplicationWhitelist.Contains(_executablePath))
        {
            HidHideInspection? leaseVerification = null;
            var journal = _recoveryManager.BeginHidHideWhitelistLease(_executablePath);
            if (journal.Status != RecoveryStatus.Success)
                return new(MsiClawInputStartStatus.InitializationFailed, "HidHide recovery journal could not be persisted. No controller settings were changed.");

            AppLog.Info("HidHide", "HidHide whitelist lease journal persisted.", ("ExecutablePath", _executablePath), ("SessionId", journal.Journal!.RecoverySessionId));
            _recoverySessionId = journal.Journal.RecoverySessionId;
            if (!_hidHideClient.AddApplication(_executablePath))
            {
                var postFailureInspection = _hidHideClient.Inspect();
                if (postFailureInspection.IsConfigurationReadable && !postFailureInspection.ApplicationWhitelist.Contains(_executablePath))
                {
                    var cleanup = _recoveryManager.CompleteHidHideWhitelistAddition(_recoverySessionId!.Value, _executablePath);
                    AppLog.Warn("HidHide", "HidHide whitelist addition failed without a persisted entry.", null, ("ExecutablePath", _executablePath), ("JournalCleared", cleanup.Status == RecoveryStatus.Success), ("Action", "DoNotAcquire"));
                    return new(MsiClawInputStartStatus.InitializationFailed, "HidHide whitelist access could not be granted. No controller settings were changed.");
                }
                if (postFailureInspection.IsConfigurationReadable && postFailureInspection.ApplicationWhitelist.Contains(_executablePath))
                {
                    _ownsWhitelistLease = true;
                    leaseVerification = postFailureInspection;
                    AppLog.Warn("HidHide", "HidHide whitelist command reported failure after the lease was persisted.", null, ("ExecutablePath", _executablePath), ("Action", "ContinueWithVerifiedLease"));
                }
                else
                {
                    AppLog.Warn("HidHide", "HidHide whitelist addition outcome is unknown.", null, ("ExecutablePath", _executablePath), ("Action", "PreserveJournal"));
                    return new(MsiClawInputStartStatus.InitializationFailed, "HidHide whitelist access could not be verified. Recovery remains pending.");
                }
            }
            if (!_ownsWhitelistLease)
            {
                var verification = _hidHideClient.Inspect();
                if (!verification.IsConfigurationReadable || !verification.ApplicationWhitelist.Contains(_executablePath))
                {
                    AppLog.Warn("HidHide", "HidHide whitelist addition could not be verified.", null, ("ExecutablePath", _executablePath), ("Action", "PreserveJournal"));
                    return new(MsiClawInputStartStatus.InitializationFailed, "HidHide whitelist access could not be verified. Recovery remains pending.");
                }
                _ownsWhitelistLease = true;
                leaseVerification = verification;
                AppLog.Info("HidHide", "HidHide whitelist lease acquired.", ("ExecutablePath", _executablePath));
            }
            if (_ownsWhitelistLease && leaseVerification is not null && !leaseVerification.CanAcquireWhitelistLease)
            {
                AppLog.Warn("HidHide", "HidHide whitelist lease is no longer eligible for input access.", null,
                    ("Status", leaseVerification.Status), ("ExecutablePath", _executablePath), ("Action", "ReleaseLease"));
                if (!ReleaseLease())
                    return new(MsiClawInputStartStatus.InitializationFailed, "HidHide whitelist lease could not be released. Recovery remains pending.");
                return leaseVerification.Status == HidHideInspectionStatus.Disabled
                    ? _inputSource.Start()
                    : new(MsiClawInputStartStatus.InitializationFailed, $"HidHide access lease is unavailable ({leaseVerification.Status}). No controller settings were changed.");
            }
        }
        else
        {
            _ownsWhitelistLease = _recoveryManager.OwnsHidHideWhitelistLease(_executablePath);
            if (_ownsWhitelistLease) _recoverySessionId = _recoveryManager.GetHidHideWhitelistLeaseSessionId(_executablePath);
        }

        var result = _inputSource.Start();
        if (!result.Started)
        {
            if (result.Status == MsiClawInputStartStatus.Pid1902NotFound && _ownsWhitelistLease)
                AppLog.Warn("Diagnostics", "MSI DirectInput device remains unavailable after HidHide whitelist access.", null, ("Reason", "Pid1902NotFoundAfterHidHideWhitelist"), ("Action", "DoNotAcquire"));
            ReleaseLease();
        }
        return result;
    }

    public async Task StopAsync()
    {
        await _inputSource.StopAsync().ConfigureAwait(false);
        ReleaseLease();
    }

    public async ValueTask DisposeAsync()
    {
        _inputSource.TestCompleted -= OnTestCompleted;
        await _inputSource.DisposeAsync().ConfigureAwait(false);
        ReleaseLease();
    }

    private void OnTestCompleted(object? sender, MsiClawInputTestSummary summary) => ReleaseLease();

    private IReadOnlyList<string> ResolveMsiDirectInputHidInstanceIds()
    {
        try { return _msiDirectInputHidInstanceIds(); }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "MSI DirectInput HidHide target resolution failed.", exception, ("Action", "DoNotMutate"));
            return [];
        }
    }

    private bool ReleaseLease()
    {
        lock (_leaseSync)
        {
            if (!_ownsWhitelistLease) return true;
            if (!_hidHideClient.RemoveApplication(_executablePath))
            {
                AppLog.Error("HidHide", "HidHide whitelist lease cleanup failed.", new InvalidOperationException("HidHide whitelist removal failed."), ("ExecutablePath", _executablePath), ("Action", "PreserveJournal"));
                return false;
            }
            var verification = _hidHideClient.Inspect();
            if (!verification.IsConfigurationReadable || verification.ApplicationWhitelist.Contains(_executablePath))
            {
                AppLog.Error("HidHide", "HidHide whitelist lease cleanup could not be verified.", new InvalidOperationException("HidHide whitelist removal verification failed."), ("ExecutablePath", _executablePath), ("Action", "PreserveJournal"));
                return false;
            }
            if (_recoverySessionId is not { } recoverySessionId)
            {
                AppLog.Error("HidHide", "HidHide whitelist recovery session ID is missing.", new InvalidOperationException("RecoverySessionIdMissing"), ("Action", "PreserveJournal"));
                return false;
            }
            var completed = _recoveryManager.CompleteHidHideWhitelistAddition(recoverySessionId, _executablePath);
            if (completed.Status != RecoveryStatus.Success)
            {
                AppLog.Error("HidHide", "HidHide whitelist lease journal cleanup failed.", new InvalidOperationException(completed.Reason), ("ExecutablePath", _executablePath), ("Action", "PreserveJournal"));
                return false;
            }
            _ownsWhitelistLease = false;
            _recoverySessionId = null;
            AppLog.Info("HidHide", "HidHide whitelist lease released.", ("ExecutablePath", _executablePath), ("JournalDeleted", true));
            return true;
        }
    }
}
