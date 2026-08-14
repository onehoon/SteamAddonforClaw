namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Reading a log file immediately after AppLog.DrainForTests() can transiently race a Windows file-handle
/// release even though the background writer has already synchronously closed its own handle by the time
/// DrainForTests() returns -- observed intermittently in CI (e.g. antivirus/indexer briefly opening a
/// freshly closed file). Retry briefly instead of failing an assertion on that unrelated timing hiccup.
/// </summary>
internal static class LogFileTestHelper
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

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
