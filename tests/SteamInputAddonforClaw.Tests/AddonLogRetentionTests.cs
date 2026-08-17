using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonLogRetentionTests
{
    [Fact]
    public void PruneDirectory_KeepsTodayAndYesterdayAndDeletesOlderAddonLogs()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var today = new DateTime(2026, 8, 18);
        Directory.CreateDirectory(directory);

        try
        {
            var oldRuntime = CreateFile(directory, "SteamInputAddonforClaw-2026-08-16-120000.000-P100-Labc.log", today.AddDays(-2));
            var oldUi = CreateFile(directory, "ui-1111.log", today.AddDays(-2));
            var yesterdayRuntime = CreateFile(directory, "SteamInputAddonforClaw-2026-08-17-120000.000-P200-Ldef.log", today.AddDays(-1));
            var yesterdayUi = CreateFile(directory, "ui-2222.log", today.AddDays(-1));
            var todayUi = CreateFile(directory, "ui-3333.log", today);
            var unrelated = CreateFile(directory, "notes.txt", today.AddDays(-30));

            AddonLogRetention.PruneDirectory(directory, today);

            Assert.False(File.Exists(oldRuntime));
            Assert.False(File.Exists(oldUi));
            Assert.True(File.Exists(yesterdayRuntime));
            Assert.True(File.Exists(yesterdayUi));
            Assert.True(File.Exists(todayUi));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PruneDirectory_MissingDirectoryIsNoOp()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exception = Record.Exception(() => AddonLogRetention.PruneDirectory(directory, new DateTime(2026, 8, 18)));
        Assert.Null(exception);
    }

    private static string CreateFile(string directory, string name, DateTime lastWriteDate)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, lastWriteDate.AddHours(12));
        return path;
    }
}
