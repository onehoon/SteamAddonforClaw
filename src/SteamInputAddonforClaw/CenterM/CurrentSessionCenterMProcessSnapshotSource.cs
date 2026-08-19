namespace SteamInputAddonforClaw.CenterM;

/// <summary>Determines whether a given process ID belongs to this Addon's own Windows session --
/// the scope the session-local <c>Local\MSI Center M.exe</c> suppression primitive actually
/// applies to. Isolated behind an interface purely for deterministic unit testing.</summary>
internal interface ICenterMSessionScopeInspector
{
    /// <summary>True if confidently in-scope, false if confidently a foreign session, null if the
    /// scope itself could not be determined (e.g. the process already exited, or access was
    /// denied) -- callers must treat null the same as any other process-observation uncertainty,
    /// never as a confident negative.</summary>
    bool? IsInCurrentSession(int processId);
}

internal sealed class Win32CurrentSessionScopeInspector : ICenterMSessionScopeInspector
{
    private static readonly int CurrentSessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;

    public bool? IsInCurrentSession(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.SessionId == CurrentSessionId;
        }
        catch (ArgumentException)
        {
            // No process with this ID exists anymore -- it is not a candidate either way, but this
            // is a confident fact (not uncertainty), so callers should simply drop the entry.
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>Review fix (MAJOR): scopes an inner <see cref="IProcessSnapshotSource"/> to this
/// Addon's own Windows session. <c>Local\MSI Center M.exe</c> -- the mutex the routing guard and
/// the OEM1 lifecycle coordinator both use as their suppression/launch-protection primitive -- is a
/// session-local kernel object, so a same-name <c>MSI Center M.exe</c> running in a different
/// session (e.g. another logged-in user, a different RDP/console session) can never be this
/// lifecycle's authority and must never inflate its same-name process topology. Fails open to
/// uncertain (returns null) if ANY observed process's session scope itself cannot be determined,
/// matching the existing "null enumeration = uncertain" contract every <see cref="IProcessSnapshotSource"/>
/// consumer in this codebase already relies on.</summary>
internal sealed class CurrentSessionCenterMProcessSnapshotSource(
    IProcessSnapshotSource inner,
    ICenterMSessionScopeInspector scope) : IProcessSnapshotSource
{
    public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
    {
        var raw = inner.GetProcessesByName(processName);
        if (raw is null) return null;

        var filtered = new List<ProcessSnapshotEntry>(raw.Count);
        foreach (var entry in raw)
        {
            var inCurrentSession = scope.IsInCurrentSession(entry.ProcessId);
            if (inCurrentSession is null) return null;
            if (inCurrentSession.Value) filtered.Add(entry);
        }
        return filtered;
    }
}
