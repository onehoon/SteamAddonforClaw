namespace SteamInputAddonforClaw.Recovery;

// Full1902 Cleanup G: read/delete only. Current production has no API that can create or update
// recovery.json -- the writer surface (WriteNew / ReplaceExisting) is gone. Delete() remains because
// the existing startup path still retires a validated stale journal after retained cleanup succeeds.
internal interface IRecoveryJournalStore
{
    string JournalPath { get; }
    bool Exists();
    string ReadText();
    void Delete();
}

internal sealed class RecoveryJournalStore(string journalPath) : IRecoveryJournalStore
{
    public string JournalPath { get; } = journalPath;
    public bool Exists() => File.Exists(JournalPath);
    public string ReadText() => File.ReadAllText(JournalPath);
    public void Delete() => File.Delete(JournalPath);
}
