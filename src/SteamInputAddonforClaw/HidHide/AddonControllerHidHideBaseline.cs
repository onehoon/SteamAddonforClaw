using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.HidHide;

internal enum AddonHidHideBaselineOutcome
{
    /// <summary>The requested baseline was applied/cleared and confirmed by read-back.</summary>
    Success,
    /// <summary>The requested baseline was already present; no mutation was performed.</summary>
    AlreadyCompliant,
    /// <summary>Read-only inspection only: the current configuration is readable and can deterministically
    /// be normalized into the Addon baseline -- but it is NOT proven compliant yet. Never sets
    /// <see cref="AddonHidHideBaselineResult.IsCompliant"/>: a caller must not treat "can be normalized"
    /// as "physical isolation is already safe".</summary>
    Applicable,
    /// <summary>Current HidHide configuration cannot be normalized into the Addon baseline through the
    /// verified control path (an unresolved raw whitelist entry, or an inverse-whitelist machine with
    /// no supported mutation path). Foreign whitelist/hidden entries the Addon CAN normalize are NOT a
    /// conflict -- Addon Controller Mode owns the effective controller-isolation configuration
    /// (PR10 addendum sections 2-6).</summary>
    Conflict,
    /// <summary>HidHide is not installed/readable, access was denied, or the official HidHide
    /// application paths could not be resolved to add a missing required entry.</summary>
    Unavailable,
    /// <summary>A required HidHide mutation reported failure.</summary>
    MutationFailed,
    /// <summary>A mutation was issued but the post-mutation read-back did not match the desired
    /// baseline.</summary>
    VerificationFailed,
}

/// <summary>Observed HidHide facts relevant to the Addon controller baseline, for logs/tests/UI.</summary>
internal sealed record AddonHidHideBaselineSnapshot(
    bool Active,
    bool Inverse,
    int WhitelistCount,
    int HiddenTargetCount,
    int RequestedTargetCount)
{
    public static readonly AddonHidHideBaselineSnapshot Unknown = new(false, false, 0, 0, 0);
}

internal sealed record AddonHidHideBaselineResult(
    AddonHidHideBaselineOutcome Outcome,
    string Reason,
    AddonHidHideBaselineSnapshot Snapshot)
{
    /// <summary>True only when the deterministic Addon baseline is proven present. A failed/conflicting
    /// result must never be read by a future caller as permission to attach a virtual controller.</summary>
    public bool IsCompliant => Outcome is AddonHidHideBaselineOutcome.Success or AddonHidHideBaselineOutcome.AlreadyCompliant;
}

/// <summary>
/// The persistent, deterministic Addon-owned HidHide baseline primitive (Full PID1902 track).
///
/// Applying the baseline writes persistent HidHide configuration that deliberately survives Addon
/// exit / restart / Windows reboot. It records nothing in the routing recovery journal.
///
/// PR10 addendum: while Addon Controller Mode owns the controller (Center M startup roots exactly
/// Disabled), the Addon also owns the effective HidHide controller-isolation configuration. It
/// normalizes HidHide into ONE deterministic baseline rather than requiring the user to manually
/// clean configuration another controller application left behind. It does NOT track provenance,
/// snapshot, or restore third-party configuration.
///
/// <list type="bullet">
/// <item>Disabled-mode baseline: <c>Inverse=false</c>, <c>Active=true</c>, Applications are exactly
/// the two official HidHide applications (<c>HidHideCLI.exe</c>, <c>HidHideClient.exe</c>) plus the
/// Addon executable, and the hidden devices are exactly the caller-supplied exact PID1902 target(s)
/// (zero or more -- a broad VID/PID wildcard is never invented).</item>
/// <item>Enabled-mode (release) baseline: the Addon executable and the Addon-owned hidden target(s)
/// removed, the official HidHide applications preserved, <c>Inverse=false</c>, <c>Active=false</c>.
/// Other applications' entries are left untouched -- Enable is not a third-party cleanup operation.</item>
/// </list>
///
/// Only an unresolved raw whitelist entry, or an inverse-whitelist machine with no supported
/// mutation path, is a hard <see cref="AddonHidHideBaselineOutcome.Conflict"/>. A required official
/// application entry that is missing and whose canonical path cannot be resolved is
/// <see cref="AddonHidHideBaselineOutcome.Unavailable"/> (a prerequisite gap, not a config repair).
/// </summary>
internal sealed class AddonControllerHidHideBaseline
{
    private static readonly string[] OfficialApplicationFileNames = ["HidHideCLI.exe", "HidHideClient.exe"];

    private readonly IHidHideClient _client;
    private readonly string _addonExecutablePath;
    private readonly Func<IReadOnlyList<string>> _officialApplicationPathsResolver;

    internal AddonControllerHidHideBaseline(
        IHidHideClient client,
        string addonExecutablePath,
        Func<IReadOnlyList<string>>? officialApplicationPathsResolver = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(addonExecutablePath) || !Path.IsPathFullyQualified(addonExecutablePath))
            throw new ArgumentException("The Addon executable path must be fully qualified.", nameof(addonExecutablePath));
        _addonExecutablePath = Path.GetFullPath(addonExecutablePath);
        _officialApplicationPathsResolver = officialApplicationPathsResolver
            ?? (() => new HidHideTrustedApplicationPathResolver().Resolve());
    }

    /// <summary>Read-only classification of whether the current HidHide state already is, or can
    /// deterministically be normalized into, the Disabled-mode Addon baseline for the given exact
    /// targets. Never mutates anything.</summary>
    internal AddonHidHideBaselineResult InspectDisabledModeBaseline(IReadOnlyCollection<string> requestedHiddenTargets)
    {
        var targets = Normalize(requestedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindNormalizationConflict(inspection);
        if (conflict is not null)
            return Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);

        var outcome = IsDisabledCompliant(inspection, targets) ? AddonHidHideBaselineOutcome.AlreadyCompliant : AddonHidHideBaselineOutcome.Applicable;
        var result = Result(outcome, outcome == AddonHidHideBaselineOutcome.AlreadyCompliant ? "DisabledModeBaselineCompliant" : "DisabledModeBaselineApplicable", inspection, targets.Count);
        Log("inspection completed", result);
        return result;
    }

    /// <summary>Read-only startup admission classification. Accepts the two persistent shapes a
    /// Disabled boot may legitimately already be compliant in: the zero-target foundation, or a later
    /// boot where exactly one hidden target the caller's validator confirms is an Addon-owned primary
    /// PID1902 gamepad collection is persisted. A configuration that only needs normalization is
    /// <see cref="AddonHidHideBaselineOutcome.Applicable"/>, exactly as
    /// <see cref="InspectDisabledModeBaseline"/> reports it.</summary>
    internal AddonHidHideBaselineResult InspectDisabledModeBaselineAllowingExistingOwnedTarget(Func<string, bool> ownedTargetValidator)
    {
        ArgumentNullException.ThrowIfNull(ownedTargetValidator);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var hidden = Normalize(inspection.HiddenDeviceEntries?.ToArray() ?? []);
        IReadOnlyCollection<string> owned = hidden.Count == 1 && ownedTargetValidator(hidden[0])
            ? [hidden[0]]
            : [];
        return InspectDisabledModeBaseline(owned);
    }

    /// <summary>Persistently normalizes HidHide into the Disabled-mode Addon baseline on a Disabled
    /// boot, keeping an existing single hidden target the caller's validator confirms is an Addon-owned
    /// primary PID1902 gamepad collection (so a normal boot does not churn the owned target) and
    /// wiping every other hidden entry. Verified by read-back (PR10 addendum sections 7-8).</summary>
    internal AddonHidHideBaselineResult ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(Func<string, bool> ownedTargetValidator)
    {
        ArgumentNullException.ThrowIfNull(ownedTargetValidator);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var hidden = Normalize(inspection.HiddenDeviceEntries?.ToArray() ?? []);
        IReadOnlyCollection<string> keep = hidden.Count == 1 && ownedTargetValidator(hidden[0])
            ? [hidden[0]]
            : [];
        return ApplyDisabledModeBaseline(keep);
    }

    /// <summary>Read-only. Recovers the one exact Addon-owned primary PID1902 hidden target from the
    /// persistent HidHide configuration when it is present and the whole baseline is proven compliant
    /// for that single target. Returns <see langword="null"/> for zero, more than one, a
    /// validator-rejected, or a non-compliant configuration.</summary>
    internal string? TryGetSingleExistingOwnedTarget(Func<string, bool> ownedTargetValidator)
    {
        ArgumentNullException.ThrowIfNull(ownedTargetValidator);
        if (!TryInspect(out var inspection, out _))
            return null;

        var hidden = Normalize(inspection.HiddenDeviceEntries?.ToArray() ?? []);
        if (hidden.Count != 1 || !ownedTargetValidator(hidden[0]))
            return null;

        return InspectDisabledModeBaseline([hidden[0]]).Outcome == AddonHidHideBaselineOutcome.AlreadyCompliant
            ? hidden[0]
            : null;
    }

    /// <summary>Persistently normalizes HidHide into the Disabled-mode Addon baseline and verifies it
    /// by read-back. Idempotent: a call when already compliant performs no mutation. Foreign whitelist
    /// and hidden entries are removed; the two official HidHide application entries are added back if
    /// missing.</summary>
    internal AddonHidHideBaselineResult ApplyDisabledModeBaseline(IReadOnlyCollection<string> requestedHiddenTargets)
    {
        var targets = Normalize(requestedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindNormalizationConflict(inspection);
        if (conflict is not null)
        {
            var conflictResult = Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);
            Log("conflict detected", conflictResult);
            return conflictResult;
        }

        if (IsDisabledCompliant(inspection, targets))
        {
            var compliant = Result(AddonHidHideBaselineOutcome.AlreadyCompliant, "DisabledModeBaselineCompliant", inspection, targets.Count);
            Log("already compliant", compliant);
            return compliant;
        }

        Log("apply started", Result(AddonHidHideBaselineOutcome.Applicable, "DisabledModeBaselineApplyStarted", inspection, targets.Count));

        // 1. Normalize inverse -> false through the verified control path, or fail closed.
        if (inspection.IsInverseWhitelist && !TryMutate(() => _client.SetInverseWhitelist(false), out var inverseFailure))
            return Fail(inverseFailure, "DisabledModeInverseNormalizeFailed", targets.Count);

        // 2. Remove every whitelist entry that is neither an official HidHide application nor the Addon.
        var initialWhitelistCount = inspection.ApplicationWhitelist.Count;
        var removedWhitelist = 0;
        foreach (var entry in inspection.ApplicationWhitelist.Where(entry => !IsOfficialApplicationEntry(entry) && !PathEquals(entry, _addonExecutablePath)).ToArray())
        {
            if (!TryMutate(() => _client.RemoveApplication(entry), out var removeFailure))
                return Fail(removeFailure, "DisabledModeForeignWhitelistRemoveFailed", targets.Count);
            removedWhitelist++;
        }

        // 3. Ensure both official HidHide application entries are present; resolve canonical paths for
        //    any that are missing. An unresolvable required path is a prerequisite gap (Unavailable),
        //    NOT a config repair -- this PR does not reinstall/repair the HidHide package itself.
        var (cliPath, clientPath) = ResolveOfficialApplicationPaths();
        if (!inspection.ApplicationWhitelist.Any(entry => IsOfficialApplicationEntry(entry, "HidHideCLI.exe")))
        {
            if (cliPath is null)
                return Unavailable("OfficialHidHideCliPathUnresolved", targets.Count);
            if (!TryMutate(() => _client.AddApplication(cliPath), out var cliFailure))
                return Fail(cliFailure, "DisabledModeOfficialCliAddFailed", targets.Count);
        }
        if (!inspection.ApplicationWhitelist.Any(entry => IsOfficialApplicationEntry(entry, "HidHideClient.exe")))
        {
            if (clientPath is null)
                return Unavailable("OfficialHidHideClientPathUnresolved", targets.Count);
            if (!TryMutate(() => _client.AddApplication(clientPath), out var clientFailure))
                return Fail(clientFailure, "DisabledModeOfficialClientAddFailed", targets.Count);
        }

        // 4. Exact Addon whitelist entry.
        if (!inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, _addonExecutablePath))
            && !TryMutate(() => _client.AddApplication(_addonExecutablePath), out var addonFailure))
            return Fail(addonFailure, "DisabledModeAddonWhitelistAddFailed", targets.Count);

        // 5. Remove every hidden device entry that is not a requested exact target.
        var initialHiddenCount = (inspection.HiddenDeviceEntries ?? []).Count;
        var removedHidden = 0;
        foreach (var entry in (inspection.HiddenDeviceEntries ?? []).Where(entry => !targets.Any(target => string.Equals(target, entry, StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            if (!TryMutate(() => _client.RemoveHiddenDevice(entry), out var removeHiddenFailure))
                return Fail(removeHiddenFailure, "DisabledModeForeignHiddenRemoveFailed", targets.Count);
            removedHidden++;
        }

        // 6. Exact supplied hidden target(s) -- add only the ones not already present.
        foreach (var target in targets)
        {
            if ((inspection.HiddenDeviceEntries ?? []).Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!TryMutate(() => _client.AddHiddenDevice(target), out var hiddenFailure))
                return Fail(hiddenFailure, "DisabledModeHiddenTargetAddFailed", targets.Count);
        }

        // 7. Active = true.
        if (!inspection.IsActive && !TryMutate(() => _client.SetActive(true), out var activeFailure))
            return Fail(activeFailure, "DisabledModeActivateFailed", targets.Count);

        // 8-9. Re-inspect and verify the complete desired baseline by exact read-back.
        if (!TryInspect(out var verification, out var verifyUnavailable))
            return verifyUnavailable;
        if (FindNormalizationConflict(verification) is { } postConflict)
            return Fail(Result(AddonHidHideBaselineOutcome.Conflict, postConflict, verification, targets.Count), postConflict, targets.Count);
        if (!IsDisabledCompliant(verification, targets))
            return Fail(Result(AddonHidHideBaselineOutcome.VerificationFailed, "DisabledModeBaselineVerificationFailed", verification, targets.Count), "DisabledModeBaselineVerificationFailed", targets.Count);

        var applied = Result(AddonHidHideBaselineOutcome.Success, "DisabledModeBaselineAppliedAndVerified", verification, targets.Count);
        AppLog.Info("HidHideBaseline", "Addon HidHide Disabled baseline normalized and verified.",
            ("Result", applied.Outcome), ("Reason", applied.Reason),
            ("InitialWhitelistCount", initialWhitelistCount), ("RemovedWhitelistCount", removedWhitelist),
            ("InitialHiddenCount", initialHiddenCount), ("RemovedHiddenCount", removedHidden),
            ("RequestedHiddenCount", targets.Count),
            ("Active", applied.Snapshot.Active), ("Inverse", applied.Snapshot.Inverse), ("WhitelistCount", applied.Snapshot.WhitelistCount));
        return applied;
    }

    /// <summary>Removes the Addon-owned controller isolation and returns HidHide to the release
    /// (Enabled-mode) baseline: the Addon executable and the given Addon-owned hidden target(s)
    /// removed, the two official HidHide applications preserved, <c>Inverse=false</c>,
    /// <c>Active=false</c>. It never reconstructs or removes other applications' configuration -- the
    /// Addon releases its own entries and authority; other applications reconcile their own state
    /// (PR10 addendum sections 10, 12).</summary>
    internal AddonHidHideBaselineResult ApplyEnabledModeBaseline(IReadOnlyCollection<string> addonOwnedHiddenTargets)
    {
        var targets = Normalize(addonOwnedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindNormalizationConflict(inspection);
        if (conflict is not null)
        {
            var conflictResult = Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);
            Log("conflict detected", conflictResult);
            return conflictResult;
        }

        if (IsEnabledCompliant(inspection, targets))
        {
            var clean = Result(AddonHidHideBaselineOutcome.AlreadyCompliant, "EnabledModeBaselineCompliant", inspection, targets.Count);
            Log("already compliant", clean);
            return clean;
        }

        if (inspection.IsInverseWhitelist && !TryMutate(() => _client.SetInverseWhitelist(false), out var inverseFailure))
            return Fail(inverseFailure, "EnabledModeInverseNormalizeFailed", targets.Count);

        foreach (var target in targets)
        {
            if (!(inspection.HiddenDeviceEntries ?? []).Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!TryMutate(() => _client.RemoveHiddenDevice(target), out var removeHiddenFailure))
                return Fail(removeHiddenFailure, "EnabledModeHiddenTargetRemoveFailed", targets.Count);
        }

        if (inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, _addonExecutablePath))
            && !TryMutate(() => _client.RemoveApplication(_addonExecutablePath), out var removeWhitelistFailure))
            return Fail(removeWhitelistFailure, "EnabledModeWhitelistRemoveFailed", targets.Count);

        if (inspection.IsActive && !TryMutate(() => _client.SetActive(false), out var deactivateFailure))
            return Fail(deactivateFailure, "EnabledModeDeactivateFailed", targets.Count);

        if (!TryInspect(out var verification, out var verifyUnavailable))
            return verifyUnavailable;
        if (FindNormalizationConflict(verification) is { } postConflict)
            return Fail(Result(AddonHidHideBaselineOutcome.Conflict, postConflict, verification, targets.Count), postConflict, targets.Count);
        if (!IsEnabledCompliant(verification, targets))
            return Fail(Result(AddonHidHideBaselineOutcome.VerificationFailed, "EnabledModeBaselineVerificationFailed", verification, targets.Count), "EnabledModeBaselineVerificationFailed", targets.Count);

        var cleared = Result(AddonHidHideBaselineOutcome.Success, "EnabledModeBaselineClearedAndVerified", verification, targets.Count);
        Log("cleared and verified", cleared);
        return cleared;
    }

    // ---- classification helpers ----

    private string? FindNormalizationConflict(HidHideInspection inspection)
    {
        if (inspection.HasUnresolvedApplicationWhitelistEntries) return "UnresolvedWhitelistEntry";
        // An inverse-whitelist machine is a conflict only when this client has no verified path to
        // normalize it. When the path exists, Apply attempts it and fails closed if it cannot confirm.
        if (inspection.IsInverseWhitelist && !_client.SupportsInverseWhitelistMutation) return "InverseWhitelistUnsupported";
        return null;
    }

    private bool IsDisabledCompliant(HidHideInspection inspection, IReadOnlyList<string> targets) =>
        !inspection.IsInverseWhitelist
        && inspection.IsActive
        && WhitelistIsExactly(inspection, includeAddon: true)
        && HiddenIsExactly(inspection, targets);

    private bool IsEnabledCompliant(HidHideInspection inspection, IReadOnlyList<string> targets) =>
        // The release baseline only proves the Addon's OWN state is gone -- it deliberately does not
        // require other applications' whitelist/hidden entries to be absent, nor does it re-add the
        // official HidHide applications; it only preserves them (PR10 addendum sections 10, 12).
        !inspection.IsInverseWhitelist
        && !inspection.IsActive
        && !inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, _addonExecutablePath))
        && !(inspection.HiddenDeviceEntries ?? []).Any(entry => targets.Any(target => string.Equals(target, entry, StringComparison.OrdinalIgnoreCase)));

    private bool WhitelistIsExactly(HidHideInspection inspection, bool includeAddon)
    {
        var entries = inspection.ApplicationWhitelist;
        var hasAddon = entries.Any(entry => PathEquals(entry, _addonExecutablePath));
        var onlyBaseline = entries.All(entry => IsOfficialApplicationEntry(entry) || PathEquals(entry, _addonExecutablePath));
        return OfficialApplicationsPresent(inspection) && onlyBaseline && (includeAddon ? hasAddon : !hasAddon);
    }

    private static bool OfficialApplicationsPresent(HidHideInspection inspection) =>
        inspection.ApplicationWhitelist.Any(entry => IsOfficialApplicationEntry(entry, "HidHideCLI.exe"))
        && inspection.ApplicationWhitelist.Any(entry => IsOfficialApplicationEntry(entry, "HidHideClient.exe"));

    private static bool HiddenIsExactly(HidHideInspection inspection, IReadOnlyList<string> targets)
    {
        var hidden = inspection.HiddenDeviceEntries ?? [];
        return hidden.Count == targets.Count
            && targets.All(target => hidden.Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsOfficialApplicationEntry(string entry)
    {
        try { return OfficialApplicationFileNames.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool IsOfficialApplicationEntry(string entry, string fileName)
    {
        try { return string.Equals(Path.GetFileName(entry), fileName, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private (string? Cli, string? Client) ResolveOfficialApplicationPaths()
    {
        IReadOnlyList<string> resolved;
        try { resolved = _officialApplicationPathsResolver() ?? []; }
        catch (Exception exception)
        {
            AppLog.Warn("HidHideBaseline", "Official HidHide application path resolution threw.", exception);
            resolved = [];
        }
        string? Pick(string fileName) => resolved.FirstOrDefault(path => IsOfficialApplicationEntry(path, fileName));
        return (Pick("HidHideCLI.exe"), Pick("HidHideClient.exe"));
    }

    private bool TryInspect(out HidHideInspection inspection, out AddonHidHideBaselineResult unavailable)
    {
        try { inspection = _client.Inspect(); }
        catch (Exception exception)
        {
            AppLog.Warn("HidHideBaseline", "Addon HidHide baseline inspection threw.", exception, ("Reason", "HidHideInspectionThrew"));
            inspection = null!;
            unavailable = new AddonHidHideBaselineResult(AddonHidHideBaselineOutcome.Unavailable, "HidHideInspectionThrew", AddonHidHideBaselineSnapshot.Unknown);
            return false;
        }

        if (inspection.Status == HidHideInspectionStatus.NotInstalled)
        { unavailable = Result(AddonHidHideBaselineOutcome.Unavailable, "HidHideNotInstalled", inspection, 0); return false; }
        if (!inspection.IsConfigurationReadable
            || inspection.Status is HidHideInspectionStatus.AccessDenied or HidHideInspectionStatus.ConfigurationUnavailable)
        { unavailable = Result(AddonHidHideBaselineOutcome.Unavailable, inspection.Status.ToString(), inspection, 0); return false; }

        unavailable = null!;
        return true;
    }

    private static bool TryMutate(Func<bool> mutation, out AddonHidHideBaselineResult failure)
    {
        bool ok;
        try { ok = mutation(); }
        catch { ok = false; }
        failure = ok ? null! : new AddonHidHideBaselineResult(AddonHidHideBaselineOutcome.MutationFailed, "HidHideMutationReportedFailure", AddonHidHideBaselineSnapshot.Unknown);
        return ok;
    }

    private AddonHidHideBaselineResult Fail(AddonHidHideBaselineResult template, string reason, int requestedTargetCount)
    {
        var result = template.Outcome is AddonHidHideBaselineOutcome.Conflict or AddonHidHideBaselineOutcome.VerificationFailed
            ? template
            : new AddonHidHideBaselineResult(template.Outcome, reason, template.Snapshot);
        AppLog.Warn("HidHideBaseline", "Addon HidHide baseline operation failed.", null,
            ("Result", result.Outcome), ("Reason", result.Reason),
            ("Active", result.Snapshot.Active), ("Inverse", result.Snapshot.Inverse),
            ("WhitelistCount", result.Snapshot.WhitelistCount), ("HiddenTargetCount", result.Snapshot.HiddenTargetCount),
            ("RequestedTargetCount", requestedTargetCount));
        return result;
    }

    private static AddonHidHideBaselineResult Unavailable(string reason, int requestedTargetCount)
    {
        AppLog.Warn("HidHideBaseline", "Addon HidHide baseline operation cannot proceed.", null,
            ("Result", AddonHidHideBaselineOutcome.Unavailable), ("Reason", reason), ("RequestedTargetCount", requestedTargetCount));
        return new AddonHidHideBaselineResult(AddonHidHideBaselineOutcome.Unavailable, reason, AddonHidHideBaselineSnapshot.Unknown);
    }

    private static AddonHidHideBaselineResult Result(AddonHidHideBaselineOutcome outcome, string reason, HidHideInspection inspection, int requestedTargetCount)
        => new(outcome, reason, ToSnapshot(inspection, requestedTargetCount));

    private static AddonHidHideBaselineSnapshot ToSnapshot(HidHideInspection inspection, int requestedTargetCount) => new(
        inspection.IsActive, inspection.IsInverseWhitelist,
        inspection.ApplicationWhitelist.Count, (inspection.HiddenDeviceEntries ?? []).Count, requestedTargetCount);

    private static void Log(string what, AddonHidHideBaselineResult result) =>
        AppLog.Info("HidHideBaseline", $"Addon HidHide baseline {what}.",
            ("Result", result.Outcome), ("Reason", result.Reason),
            ("Active", result.Snapshot.Active), ("Inverse", result.Snapshot.Inverse),
            ("WhitelistCount", result.Snapshot.WhitelistCount),
            ("HiddenTargetCount", result.Snapshot.HiddenTargetCount),
            ("RequestedTargetCount", result.Snapshot.RequestedTargetCount));

    private static IReadOnlyList<string> Normalize(IReadOnlyCollection<string>? requested) => (requested ?? [])
        .Where(entry => !string.IsNullOrWhiteSpace(entry))
        .Select(entry => entry.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
