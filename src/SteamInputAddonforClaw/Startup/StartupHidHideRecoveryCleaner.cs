namespace SteamInputAddonforClaw.Startup;

using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;

/// <summary>
/// Removes persistent HidHide mutations proven addon-owned by a validated previous-process
/// recovery journal. This never replays previous routing/native/VIIPER state; it only depends
/// on the journal's HidHide evidence and <see cref="IHidHideClient"/>.
/// </summary>
internal interface IStartupHidHideRecoveryCleaner
{
    bool TryClean(RecoveryJournal journal, out string reason);
}

internal sealed class StartupHidHideRecoveryCleaner(IHidHideClient hidHideClient) : IStartupHidHideRecoveryCleaner
{
    internal static bool RequiresCleanup(RecoveryJournal journal) =>
        journal.Mutations.HidHideDeviceAdditions is { Count: > 0 } ||
        journal.Mutations.ExecutableWhitelistAdditions is { Count: > 0 } ||
        journal.Mutations.OriginalHidHideActiveState is not null;

    public bool TryClean(RecoveryJournal journal, out string reason)
    {
        var hiddenEntries = journal.Mutations.HidHideDeviceAdditions ?? [];
        var whitelistEntries = journal.Mutations.ExecutableWhitelistAdditions ?? [];
        var originalActive = journal.Mutations.OriginalHidHideActiveState;

        if (hiddenEntries.Any(string.IsNullOrWhiteSpace) || whitelistEntries.Any(entry => !IsValidExecutablePath(entry)))
        {
            reason = "Recovery journal contains invalid HidHide evidence.";
            LogFailure(journal, reason);
            return false;
        }

        AppLog.Info("Recovery", "Startup stale HidHide recovery started.",
            ("SessionId", journal.RecoverySessionId), ("RecordedHiddenCount", hiddenEntries.Count),
            ("RecordedWhitelistCount", whitelistEntries.Count), ("OriginalActiveState", originalActive));

        var inspection = hidHideClient.Inspect();
        if (!IsSafeToMutate(inspection))
        {
            reason = $"HidHide is not in a safe, configuration-readable state for startup recovery ({inspection.Status}).";
            LogFailure(journal, reason);
            return false;
        }

        // Current production routing only ever records this field false->true, so a value of
        // true is evidence the journal does not match the recovery contract this cleaner
        // implements. Fail closed rather than guess at intent.
        if (originalActive == true)
        {
            reason = "Unexpected recovery evidence: OriginalHidHideActiveState was true.";
            LogFailure(journal, reason);
            return false;
        }

        if (originalActive == false && inspection.IsActive)
        {
            if (!IsHiddenSubsetOfOwned(inspection.HiddenDeviceEntries, hiddenEntries))
            {
                reason = "Currently hidden devices are not fully owned by the recovery journal; global HidHide state cannot be safely restored.";
                LogFailure(journal, reason);
                return false;
            }

            // Re-inspect immediately before the global mutation to guard against another
            // process changing HidHide between the admission check above and this call.
            var reinspection = hidHideClient.Inspect();
            if (!IsSafeToMutate(reinspection) || !reinspection.IsActive || !IsHiddenSubsetOfOwned(reinspection.HiddenDeviceEntries, hiddenEntries))
            {
                reason = "HidHide configuration changed before the global active-state restore could proceed safely.";
                LogFailure(journal, reason);
                return false;
            }

            if (!hidHideClient.SetActive(false))
            {
                reason = "HidHide global active-state restore failed.";
                LogFailure(journal, reason);
                return false;
            }

            var verifyActive = hidHideClient.Inspect();
            if (!verifyActive.IsConfigurationReadable || verifyActive.IsActive)
            {
                reason = "HidHide global active-state restore could not be verified.";
                LogFailure(journal, reason);
                return false;
            }

            AppLog.Info("Recovery", "Startup stale HidHide global state restored.", ("SessionId", journal.RecoverySessionId), ("Active", false));
        }

        foreach (var entry in hiddenEntries)
        {
            if (!hidHideClient.RemoveHiddenDevice(entry))
            {
                reason = $"Failed to remove addon-owned hidden device entry '{entry}'.";
                LogFailure(journal, reason);
                return false;
            }
        }
        if (hiddenEntries.Count > 0)
        {
            var verify = hidHideClient.Inspect();
            if (!verify.IsConfigurationReadable || StillPresent(verify.HiddenDeviceEntries, hiddenEntries))
            {
                reason = "Addon-owned hidden device entries remain after removal.";
                LogFailure(journal, reason);
                return false;
            }
            AppLog.Info("Recovery", "Startup stale HidHide hidden entries cleaned.", ("SessionId", journal.RecoverySessionId), ("Count", hiddenEntries.Count));
        }

        foreach (var entry in whitelistEntries)
        {
            if (!hidHideClient.RemoveApplication(entry))
            {
                reason = $"Failed to remove addon-owned whitelist entry '{entry}'.";
                LogFailure(journal, reason);
                return false;
            }
        }
        if (whitelistEntries.Count > 0)
        {
            var verify = hidHideClient.Inspect();
            if (!verify.IsConfigurationReadable || whitelistEntries.Any(entry => verify.ApplicationWhitelist.Contains(entry)))
            {
                reason = "Addon-owned whitelist entries remain after removal.";
                LogFailure(journal, reason);
                return false;
            }
            AppLog.Info("Recovery", "Startup stale HidHide whitelist entries cleaned.", ("SessionId", journal.RecoverySessionId), ("Count", whitelistEntries.Count));
        }

        reason = "Startup stale HidHide recovery completed.";
        AppLog.Info("Recovery", reason, ("SessionId", journal.RecoverySessionId));
        return true;
    }

    private static void LogFailure(RecoveryJournal journal, string reason) =>
        AppLog.Warn("Recovery", "Startup stale HidHide recovery failed.", null,
            ("SessionId", journal.RecoverySessionId), ("Reason", reason), ("Action", "PreserveJournal"));

    private static bool IsSafeToMutate(HidHideInspection inspection) =>
        inspection.IsConfigurationReadable && inspection.Status != HidHideInspectionStatus.InverseWhitelist;

    private static bool IsHiddenSubsetOfOwned(IReadOnlyList<string>? current, IReadOnlyList<string> owned) =>
        (current ?? []).All(entry => owned.Contains(entry, StringComparer.OrdinalIgnoreCase));

    private static bool StillPresent(IReadOnlyList<string>? current, IReadOnlyList<string> owned) =>
        owned.Any(entry => (current ?? []).Contains(entry, StringComparer.OrdinalIgnoreCase));

    private static bool IsValidExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { _ = Path.GetFullPath(path); return true; }
        catch { return false; }
    }
}
