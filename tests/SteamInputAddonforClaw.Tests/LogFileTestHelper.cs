using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// AppLog is one process-wide singleton with a single background writer and a single shared queue.
/// Reading its log file right after AppLog.DrainForTests() can still transiently race under the full test
/// suite (not just this test's own serialized collection): other concurrently-running collections'
/// production-code paths enqueue through the same writer and can re-target its one open file handle
/// between this test's own drain and its file read. Re-draining before every retry (not just once up
/// front) closes that window on each attempt instead of only checking a stale close from before the race.
/// </summary>
internal static class LogFileTestHelper
{
    private const int MaxAttempts = 40;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    internal static string ReadAllText(string path) => Retry(() => File.ReadAllText(path));

    internal static string[] ReadAllLines(string path) => Retry(() => File.ReadAllLines(path));

    private static T Retry<T>(Func<T> read)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return read(); }
            catch (IOException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelay);
                AppLog.DrainForTests();
            }
        }
    }
}
