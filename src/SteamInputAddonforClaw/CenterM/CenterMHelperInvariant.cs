namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Armed-state authoritative invariant (research handoff section 17/INVARIANT A): while armed, the
/// only process named "MSI Center M" must be exactly the owned helper. Pure and testable
/// independent of how the process snapshot was captured.
/// </summary>
internal enum CenterMHelperInvariantState
{
    Valid,
    HelperMissing,
    ForeignProcessDetected,
    MultipleSameNameProcesses,
    Uncertain
}

internal static class CenterMHelperInvariant
{
    /// <param name="sameNameProcesses">Fresh snapshot of all processes named "MSI Center M", or
    /// null if enumeration itself failed.</param>
    internal static CenterMHelperInvariantState Evaluate(IReadOnlyList<ProcessSnapshotEntry>? sameNameProcesses, int ownedHelperProcessId)
    {
        if (sameNameProcesses is null) return CenterMHelperInvariantState.Uncertain;
        if (sameNameProcesses.Count == 0) return CenterMHelperInvariantState.HelperMissing;
        if (sameNameProcesses.Count > 1) return CenterMHelperInvariantState.MultipleSameNameProcesses;

        return sameNameProcesses[0].ProcessId == ownedHelperProcessId
            ? CenterMHelperInvariantState.Valid
            : CenterMHelperInvariantState.ForeignProcessDetected;
    }
}
