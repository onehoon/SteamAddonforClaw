using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Full1902 Cleanup G: RecoveryJournalStore is read/delete only -- current production has no API
// that can create or update recovery.json.
public sealed class RecoveryJournalStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawRecoveryStoreTests", Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "recovery.json");

    [Fact]
    public void Exists_ReflectsFilePresence_AndReadTextReturnsTheContent()
    {
        var store = new RecoveryJournalStore(JournalPath);
        Assert.False(store.Exists());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "{\"SchemaVersion\":5}");

        Assert.True(store.Exists());
        Assert.Equal("{\"SchemaVersion\":5}", store.ReadText());
        Assert.Equal(JournalPath, store.JournalPath);
    }

    [Fact]
    public void Delete_RemovesTheJournalFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "{}");
        var store = new RecoveryJournalStore(JournalPath);

        store.Delete();

        Assert.False(store.Exists());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
