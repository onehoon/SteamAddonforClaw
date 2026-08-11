using System.Text.Json;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RecoveryJournalStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawRecoveryStoreTests", Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "recovery.json");

    [Fact]
    public void ReplaceExisting_ReplacesJournalWithoutTemporaryResidue()
    {
        var store = new RecoveryJournalStore(JournalPath);
        var first = Journal(Guid.NewGuid());
        var second = Journal(Guid.NewGuid());
        store.WriteNew(first);

        store.ReplaceExisting(second);

        var loaded = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(JournalPath));
        Assert.Equal(second.RecoverySessionId, loaded!.RecoverySessionId);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp-*"));
    }

    [Fact]
    public void ReplaceExisting_RequiresExistingJournal()
    {
        var store = new RecoveryJournalStore(JournalPath);

        Assert.Throws<IOException>(() => store.ReplaceExisting(Journal(Guid.NewGuid())));
    }

    private static RecoveryJournal Journal(Guid sessionId) =>
        new(RecoveryManager.CurrentSchemaVersion, sessionId, DateTimeOffset.UtcNow, null, new(ExecutableWhitelistAdditions: ["C:\\addon.exe"]));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
