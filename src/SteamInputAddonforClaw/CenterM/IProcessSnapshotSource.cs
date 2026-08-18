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
    IReadOnlyList<ProcessSnapshotEntry> GetProcessesByName(string processName);
}

internal sealed class Win32ProcessSnapshotSource : IProcessSnapshotSource
{
    public IReadOnlyList<ProcessSnapshotEntry> GetProcessesByName(string processName)
    {
        var results = new List<ProcessSnapshotEntry>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
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
