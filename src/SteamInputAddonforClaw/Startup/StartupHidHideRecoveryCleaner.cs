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

        if (hiddenEntries.Any(entry => !IsValidHiddenEntry(entry)) || whitelistEntries.Any(entry => !IsValidExecutablePath(entry)))
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
            // Re-inspect immediately before the global mutation to guard against another
            // process changing HidHide between the admission check above and this call.
            var reinspection = hidHideClient.Inspect();
            if (!IsSafeToMutate(reinspection))
            {
                reason = "HidHide configuration changed before the global active-state restore could proceed safely.";
                LogFailure(journal, reason);
                return false;
            }

            if (reinspection.IsActive && !hidHideClient.SetActive(false))
            {
                reason = "HidHide global active-state restore failed.";
                LogFailure(journal, reason);
                return false;
            }

            var verifyActive = hidHideClient.Inspect();
            if (!IsSafeToMutate(verifyActive) || verifyActive.IsActive)
            {
                reason = "HidHide global active-state restore could not be verified.";
                LogFailure(journal, reason);
                return false;
            }

            AppLog.Info("Recovery", "Startup stale HidHide global state restored.", ("SessionId", journal.RecoverySessionId), ("Active", false));
        }

        if (hiddenEntries.Count > 0)
        {
            var preRemoval = hidHideClient.Inspect();
            if (!IsSafeToMutate(preRemoval))
            {
                reason = "HidHide configuration became unsafe to mutate before hidden-device cleanup.";
                LogFailure(journal, reason);
                return false;
            }
            var currentHidden = new HashSet<string>(preRemoval.HiddenDeviceEntries ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var entry in hiddenEntries)
            {
                if (!currentHidden.Contains(entry)) continue; // Already absent: idempotent success, no call needed.
                if (!hidHideClient.RemoveHiddenDevice(entry))
                {
                    reason = $"Failed to remove addon-owned hidden device entry '{entry}'.";
                    LogFailure(journal, reason);
                    return false;
                }
            }
            var verify = hidHideClient.Inspect();
            if (!IsSafeToMutate(verify) || StillPresent(verify.HiddenDeviceEntries, hiddenEntries))
            {
                reason = "Addon-owned hidden device entries remain after removal.";
                LogFailure(journal, reason);
                return false;
            }
            AppLog.Info("Recovery", "Startup stale HidHide hidden entries cleaned.", ("SessionId", journal.RecoverySessionId), ("Count", hiddenEntries.Count));
        }

        if (whitelistEntries.Count > 0)
        {
            var preRemoval = hidHideClient.Inspect();
            if (!IsSafeToMutate(preRemoval) || !HasFullyNormalizedWhitelist(preRemoval))
            {
                // A raw whitelist entry that HidHideDriverClient could not normalize to a DOS
                // path is silently dropped from ApplicationWhitelist (kept only in
                // RawApplicationWhitelist), which would make an addon-owned journal entry look
                // "already absent" from the normalized view alone. Ambiguous ownership must not
                // be treated as already-clean.
                reason = "HidHide whitelist state is unsafe to mutate, or contains entries that could not be normalized; ownership cannot be safely verified.";
                LogFailure(journal, reason);
                return false;
            }
            foreach (var entry in whitelistEntries)
            {
                if (!preRemoval.ApplicationWhitelist.Contains(entry)) continue; // Already absent: idempotent success, no call needed.
                if (!hidHideClient.RemoveApplication(entry))
                {
                    reason = $"Failed to remove addon-owned whitelist entry '{entry}'.";
                    LogFailure(journal, reason);
                    return false;
                }
            }
            var verify = hidHideClient.Inspect();
            if (!IsSafeToMutate(verify) || !HasFullyNormalizedWhitelist(verify) || whitelistEntries.Any(entry => verify.ApplicationWhitelist.Contains(entry)))
            {
                reason = "Addon-owned whitelist entries remain after removal, or whitelist ownership could not be safely re-verified.";
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

    private static bool StillPresent(IReadOnlyList<string>? current, IReadOnlyList<string> owned) =>
        owned.Any(entry => (current ?? []).Contains(entry, StringComparer.OrdinalIgnoreCase));

    // HidHideDriverClient.Inspect() silently drops any raw whitelist entry it cannot normalize
    // to a DOS path from ApplicationWhitelist (it survives only in RawApplicationWhitelist).
    // If raw and normalized counts differ, the normalized view cannot be trusted as complete
    // ownership evidence -- conservatively treat that as unsafe to mutate.
    private static bool HasFullyNormalizedWhitelist(HidHideInspection inspection) =>
        inspection.RawApplicationWhitelist is not null &&
        inspection.RawApplicationWhitelist.Count == inspection.ApplicationWhitelist.Count;

    // The recovery writer only ever records Trim()med hidden-device entries, so anything else
    // is not evidence this addon produced and must be rejected rather than normalized/guessed.
    private static bool IsValidHiddenEntry(string entry) =>
        !string.IsNullOrWhiteSpace(entry) && entry == entry.Trim();

    // The recovery writer only ever records Path.GetFullPath()-canonicalized whitelist entries
    // (see RecoveryManager.BeginHidHideWhitelistLease/RecordHidHideWhitelistAddition), so a
    // relative, drive-relative, root-relative, UNC, or non-canonical (e.g. containing "..")
    // path is not evidence this addon produced. HidHidePathConverter.ToFullImageName() would
    // re-resolve such a path against the *current* process, which could target a different file
    // than the journal actually recorded -- fail closed instead of guessing.
    private static bool IsValidExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim()) return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\")) return false;
        if (!Path.IsPathFullyQualified(path)) return false;
        try
        {
            var canonical = Path.GetFullPath(path);
            return string.Equals(canonical, path, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
