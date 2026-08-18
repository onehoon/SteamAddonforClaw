namespace SteamInputAddonforClaw.CenterM;

/// <summary>One observed process instance for a given image name. ExecutablePath is null when the
/// query fails (e.g. access denied) -- callers must treat that as "path unknown", never as a
/// negative match.</summary>
internal readonly record struct ProcessSnapshotEntry(int ProcessId, string ProcessName, string? ExecutablePath);

/// <summary>Read-only process-by-name enumeration seam, shared by the backend probe, the helper
/// same-name invariant validator, and MainUI candidate detection. Isolated behind an interface so
/// all of that logic can be unit tested without touching real OS process state.</summary>
internal interface IProcessSnapshotSource
{
    /// <summary>Returns null when enumeration itself failed (uncertain), which callers must treat
    /// as a distinct safety fact from "enumerated successfully and found zero matches" -- an empty
    /// list is a confident negative; null is not.</summary>
    IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName);
}

internal sealed class Win32ProcessSnapshotSource : IProcessSnapshotSource
{
    public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
    {
        System.Diagnostics.Process[] processes;
        try { processes = System.Diagnostics.Process.GetProcessesByName(processName); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        var results = new List<ProcessSnapshotEntry>();
        foreach (var process in processes)
        {
            using (process)
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Access denied / process exited between enumeration and query -- path stays
                    // unknown, never treated as a mismatch.
                }
                results.Add(new ProcessSnapshotEntry(process.Id, processName, path));
            }
        }
        return results;
    }
}
