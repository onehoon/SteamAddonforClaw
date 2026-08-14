namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Reading a log file immediately after AppLog.DrainForTests() can, in principle, still race a transient
/// OS-level file lock (e.g. antivirus/indexer briefly opening a freshly closed file) even though the
/// writer's own handle is guaranteed closed by then. The main cross-test race that used to make this
/// common -- other, untagged test collections concurrently writing through the same AppLog singleton --
/// is closed at the assembly level instead (see AssemblyInfo.cs, DisableTestParallelization = true).
/// A short bounded retry remains as defense-in-depth: DisableTestParallelization guarantees no two test
/// *methods* run concurrently, but does not join/cancel background work a test left running past its own
/// return (a separate test-hygiene concern), so a rare residual race is still possible.
/// </summary>
internal static class LogFileTestHelper
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);

    internal static string ReadAllText(string path) => Retry(() => File.ReadAllText(path));

    internal static string[] ReadAllLines(string path) => Retry(() => File.ReadAllLines(path));

    private static T Retry<T>(Func<T> read)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return read(); }
            catch (IOException) when (attempt < MaxAttempts) { Thread.Sleep(RetryDelay); }
        }
    }
}
