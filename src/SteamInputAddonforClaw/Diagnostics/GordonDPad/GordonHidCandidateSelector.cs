namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

internal enum GordonHidSelectionStatus
{
    /// <summary>No VID/PID/usage-matching HID collection is present at all.</summary>
    NoneFound,
    /// <summary>Exactly one candidate was chosen -- either because its own instance ID or an ancestor's
    /// matched the Addon-owned Gordon (<see cref="GordonHidSelectionResult.OwnershipConfirmed"/> true),
    /// or because it was the only VID/PID/usage-matching candidate present
    /// (<see cref="GordonHidSelectionResult.OwnershipConfirmed"/> false -- best-effort, not verified).</summary>
    Selected,
    /// <summary>More than one candidate is present and none could be correlated to the Addon-owned
    /// Gordon; refusing to guess rather than risk attaching to a real Steam Controller or a stale node.</summary>
    Ambiguous,
}

internal readonly record struct GordonHidSelectionResult(GordonHidSelectionStatus Status, GordonHidCandidate? Selected, bool OwnershipConfirmed, IReadOnlyList<GordonHidCandidate> AllCandidates);

/// <summary>
/// Pure selection logic deciding which (if any) VID/PID/usage-matching HID collection is the Addon-owned
/// Gordon. Never itself does PnP I/O -- <paramref name="ancestorsOf"/> is injected so this can be tested
/// without live device state, and so the ancestor walk (only needed when there is more than one
/// candidate, or to confirm a single one) is not performed unnecessarily.
/// </summary>
internal static class GordonHidCandidateSelector
{
    internal static GordonHidSelectionResult Select(
        IReadOnlyList<GordonHidCandidate> candidates,
        IReadOnlySet<string> ownedInstanceIds,
        Func<GordonHidCandidate, IReadOnlyList<string>> ancestorsOf)
    {
        if (candidates.Count == 0) return new GordonHidSelectionResult(GordonHidSelectionStatus.NoneFound, null, false, candidates);

        foreach (var candidate in candidates)
        {
            if (candidate.InstanceId is not null && ownedInstanceIds.Contains(candidate.InstanceId))
                return new GordonHidSelectionResult(GordonHidSelectionStatus.Selected, candidate, true, candidates);

            if (ancestorsOf(candidate).Any(ownedInstanceIds.Contains))
                return new GordonHidSelectionResult(GordonHidSelectionStatus.Selected, candidate, true, candidates);
        }

        return candidates.Count == 1
            ? new GordonHidSelectionResult(GordonHidSelectionStatus.Selected, candidates[0], false, candidates)
            : new GordonHidSelectionResult(GordonHidSelectionStatus.Ambiguous, null, false, candidates);
    }
}
