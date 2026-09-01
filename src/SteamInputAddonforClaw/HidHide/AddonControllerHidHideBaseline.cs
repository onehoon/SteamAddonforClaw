using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.HidHide;

internal enum AddonHidHideBaselineOutcome
{
    /// <summary>The requested baseline was applied/cleared and confirmed by read-back.</summary>
    Success,
    /// <summary>The requested baseline was already present; no mutation was performed.</summary>
    AlreadyCompliant,
    /// <summary>Read-only inspection only: the current configuration is readable and has no blocking
    /// conflict, so <c>Apply</c> could deterministically reach the baseline -- but it is NOT proven
    /// compliant yet. Never sets <see cref="AddonHidHideBaselineResult.IsCompliant"/>: a caller must
    /// not treat "can be applied" as "physical isolation is already safe".</summary>
    Applicable,
    /// <summary>Current HidHide configuration contains unsupported foreign ownership/state. The
    /// caller must NOT enter (or must leave) Addon Controller Mode -- this is an admission failure,
    /// never something to reconcile by silently destroying the foreign state.</summary>
    Conflict,
    /// <summary>HidHide is not installed, not readable, or access was denied.</summary>
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
    /// result must never be read by a future caller as permission to attach a virtual controller
    /// (work order section 26).</summary>
    public bool IsCompliant => Outcome is AddonHidHideBaselineOutcome.Success or AddonHidHideBaselineOutcome.AlreadyCompliant;
}

/// <summary>
/// The persistent, deterministic Addon-owned HidHide baseline primitive (Full PID1902 track, PR2).
///
/// This is NOT a Steam-route lease: applying the baseline writes persistent HidHide configuration
/// that deliberately survives Addon exit / restart / Windows reboot. It records nothing in the
/// routing recovery journal (no <c>RecoveryManager</c> dependency), so startup recovery cleanup can
/// never mistake it for a stale transient route mutation.
///
/// It owns only two states:
/// <list type="bullet">
/// <item>Disabled-mode baseline: <c>Inverse=false</c>, <c>Active=true</c>, exactly the Addon
/// executable whitelisted, exactly the caller-supplied exact PID1902 hidden target(s) blocked
/// (zero or more -- a broad VID/PID wildcard is never invented).</item>
/// <item>Enabled-mode (stock) baseline: the Addon executable and the Addon-owned hidden target(s)
/// removed, <c>Inverse=false</c>, <c>Active=false</c>.</item>
/// </list>
///
/// Any foreign whitelist/hidden entry, unresolved raw whitelist entry, or inverse mode that cannot
/// be normalized through the verified HidHide control path is a hard <see cref="AddonHidHideBaselineOutcome.Conflict"/>/
/// <see cref="AddonHidHideBaselineOutcome.Unavailable"/> -- it is never reconciled by destroying
/// unknown foreign state.
/// </summary>
internal sealed class AddonControllerHidHideBaseline
{
    private readonly IHidHideClient _client;
    private readonly string _addonExecutablePath;

    internal AddonControllerHidHideBaseline(IHidHideClient client, string addonExecutablePath)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(addonExecutablePath) || !Path.IsPathFullyQualified(addonExecutablePath))
            throw new ArgumentException("The Addon executable path must be fully qualified.", nameof(addonExecutablePath));
        _addonExecutablePath = Path.GetFullPath(addonExecutablePath);
    }

    /// <summary>Read-only classification of whether the current HidHide state already is, or can
    /// deterministically become, the Disabled-mode Addon baseline for the given exact targets.
    /// Never mutates anything.</summary>
    internal AddonHidHideBaselineResult InspectDisabledModeBaseline(IReadOnlyCollection<string> requestedHiddenTargets)
    {
        var targets = Normalize(requestedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindDisabledModeConflict(inspection, targets);
        if (conflict is not null)
            return Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);

        var compliant = !inspection.IsInverseWhitelist
            && inspection.IsActive
            && WhitelistIsExactly(inspection, includeAddon: true)
            && HiddenIsExactly(inspection, targets);
        var outcome = compliant ? AddonHidHideBaselineOutcome.AlreadyCompliant : AddonHidHideBaselineOutcome.Applicable;
        var result = Result(outcome, compliant ? "DisabledModeBaselineCompliant" : "DisabledModeBaselineApplicable", inspection, targets.Count);
        Log("inspection completed", result);
        return result;
    }

    /// <summary>Read-only startup admission classification (work order PR5 section 14). Accepts the
    /// two persistent shapes a Disabled boot may legitimately be in: the PR3 zero-target foundation,
    /// or a later boot where PR5 has persisted exactly one hidden target that the caller's validator
    /// confirms is an Addon-owned primary PID1902 gamepad collection. Everything else -- a foreign or
    /// unresolved whitelist entry, more than one hidden target, or a hidden target the validator
    /// rejects -- still fails closed exactly as <see cref="InspectDisabledModeBaseline"/> would.</summary>
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

    /// <summary>Read-only. Recovers the one exact Addon-owned primary PID1902 hidden target from the
    /// persistent HidHide configuration when it is present and the whole baseline is proven compliant
    /// for that single target (work order PR5 review). Returns <see langword="null"/> for zero, more
    /// than one, a validator-rejected, or a non-compliant configuration -- the caller must not treat
    /// an arbitrary hidden entry as owned.</summary>
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

    /// <summary>Persistently applies the Disabled-mode Addon baseline and verifies it by read-back.
    /// Idempotent: a call when already compliant performs no mutation.</summary>
    internal AddonHidHideBaselineResult ApplyDisabledModeBaseline(IReadOnlyCollection<string> requestedHiddenTargets)
    {
        var targets = Normalize(requestedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindDisabledModeConflict(inspection, targets);
        if (conflict is not null)
        {
            var conflictResult = Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);
            Log("conflict detected", conflictResult);
            return conflictResult;
        }

        if (!inspection.IsInverseWhitelist && inspection.IsActive
            && WhitelistIsExactly(inspection, includeAddon: true) && HiddenIsExactly(inspection, targets))
        {
            var compliant = Result(AddonHidHideBaselineOutcome.AlreadyCompliant, "DisabledModeBaselineCompliant", inspection, targets.Count);
            Log("already compliant", compliant);
            return compliant;
        }

        Log("apply started", Result(AddonHidHideBaselineOutcome.Applicable, "DisabledModeBaselineApplyStarted", inspection, targets.Count));

        // 1. Normalize inverse -> false through the verified control path, or fail closed.
        if (inspection.IsInverseWhitelist && !TryMutate(() => _client.SetInverseWhitelist(false), out var inverseFailure))
            return Fail(inverseFailure, "DisabledModeInverseNormalizeFailed", targets.Count);

        // 2. Exact Addon whitelist entry.
        if (!inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, _addonExecutablePath))
            && !TryMutate(() => _client.AddApplication(_addonExecutablePath), out var whitelistFailure))
            return Fail(whitelistFailure, "DisabledModeWhitelistAddFailed", targets.Count);

        // 3. Exact supplied hidden target(s) -- add only the ones not already present.
        foreach (var target in targets)
        {
            if ((inspection.HiddenDeviceEntries ?? []).Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!TryMutate(() => _client.AddHiddenDevice(target), out var hiddenFailure))
                return Fail(hiddenFailure, "DisabledModeHiddenTargetAddFailed", targets.Count);
        }

        // 4. Active = true.
        if (!inspection.IsActive && !TryMutate(() => _client.SetActive(true), out var activeFailure))
            return Fail(activeFailure, "DisabledModeActivateFailed", targets.Count);

        // 5-6. Re-inspect and verify the complete desired baseline.
        if (!TryInspect(out var verification, out var verifyUnavailable))
            return verifyUnavailable;
        if (FindDisabledModeConflict(verification, targets) is { } postConflict)
            return Fail(Result(AddonHidHideBaselineOutcome.Conflict, postConflict, verification, targets.Count), postConflict, targets.Count);
        if (verification.IsInverseWhitelist || !verification.IsActive
            || !WhitelistIsExactly(verification, includeAddon: true)
            || !HiddenIsExactly(verification, targets))
            return Fail(Result(AddonHidHideBaselineOutcome.VerificationFailed, "DisabledModeBaselineVerificationFailed", verification, targets.Count), "DisabledModeBaselineVerificationFailed", targets.Count);

        var applied = Result(AddonHidHideBaselineOutcome.Success, "DisabledModeBaselineAppliedAndVerified", verification, targets.Count);
        Log("applied and verified", applied);
        return applied;
    }

    /// <summary>Removes the Addon-owned controller isolation and returns HidHide to the deterministic
    /// clean stock (Enabled-mode) baseline: the Addon executable and the given Addon-owned hidden
    /// target(s) removed, <c>Inverse=false</c>, <c>Active=false</c>. Fails closed on any foreign
    /// state -- it never tries to restore arbitrary pre-existing configuration. This is the primitive
    /// a future Center M Enable transition will call; PR2 does not wire it to shutdown/Enable/uninstall.</summary>
    internal AddonHidHideBaselineResult ApplyEnabledModeBaseline(IReadOnlyCollection<string> addonOwnedHiddenTargets)
    {
        var targets = Normalize(addonOwnedHiddenTargets);
        if (!TryInspect(out var inspection, out var unavailable))
            return unavailable;

        var conflict = FindEnabledModeConflict(inspection, targets);
        if (conflict is not null)
        {
            var conflictResult = Result(AddonHidHideBaselineOutcome.Conflict, conflict, inspection, targets.Count);
            Log("conflict detected", conflictResult);
            return conflictResult;
        }

        var alreadyClear = !inspection.IsInverseWhitelist && !inspection.IsActive
            && WhitelistIsExactly(inspection, includeAddon: false)
            && (inspection.HiddenDeviceEntries ?? []).Count == 0;
        if (alreadyClear)
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
        if (FindEnabledModeConflict(verification, targets) is { } postConflict)
            return Fail(Result(AddonHidHideBaselineOutcome.Conflict, postConflict, verification, targets.Count), postConflict, targets.Count);
        if (verification.IsInverseWhitelist || verification.IsActive
            || !WhitelistIsExactly(verification, includeAddon: false)
            || (verification.HiddenDeviceEntries ?? []).Count != 0)
            return Fail(Result(AddonHidHideBaselineOutcome.VerificationFailed, "EnabledModeBaselineVerificationFailed", verification, targets.Count), "EnabledModeBaselineVerificationFailed", targets.Count);

        var cleared = Result(AddonHidHideBaselineOutcome.Success, "EnabledModeBaselineClearedAndVerified", verification, targets.Count);
        Log("cleared and verified", cleared);
        return cleared;
    }

    // ---- classification helpers ----

    private string? FindDisabledModeConflict(HidHideInspection inspection, IReadOnlyList<string> targets) =>
        FindCommonConflict(inspection, targets);

    private string? FindEnabledModeConflict(HidHideInspection inspection, IReadOnlyList<string> targets) =>
        FindCommonConflict(inspection, targets);

    private string? FindCommonConflict(HidHideInspection inspection, IReadOnlyList<string> targets)
    {
        if (inspection.HasUnresolvedApplicationWhitelistEntries) return "UnresolvedWhitelistEntry";
        // An inverse-whitelist machine is a conflict only when this client has no verified path to
        // normalize it (work order section 6/14). When the path exists, Apply attempts it and fails
        // closed if the mutation/read-back cannot be confirmed.
        if (inspection.IsInverseWhitelist && !_client.SupportsInverseWhitelistMutation) return "InverseWhitelistUnsupported";
        if (inspection.ApplicationWhitelist.Any(entry => !PathEquals(entry, _addonExecutablePath)))
            return "ForeignWhitelistEntry";
        if ((inspection.HiddenDeviceEntries ?? []).Any(entry => !targets.Any(target => string.Equals(target, entry, StringComparison.OrdinalIgnoreCase))))
            return "ForeignHiddenDeviceEntry";
        return null;
    }

    private bool WhitelistIsExactly(HidHideInspection inspection, bool includeAddon)
    {
        var hasAddon = inspection.ApplicationWhitelist.Any(entry => PathEquals(entry, _addonExecutablePath));
        var onlyAddon = inspection.ApplicationWhitelist.All(entry => PathEquals(entry, _addonExecutablePath));
        return includeAddon ? hasAddon && onlyAddon : !hasAddon && inspection.ApplicationWhitelist.Count == 0;
    }

    private static bool HiddenIsExactly(HidHideInspection inspection, IReadOnlyList<string> targets)
    {
        var hidden = inspection.HiddenDeviceEntries ?? [];
        return hidden.Count == targets.Count
            && targets.All(target => hidden.Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase)));
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
