using System.Text.Json;

namespace SteamInputAddonforClaw.Recovery;

internal interface IRecoveryJournalStore
{
    string JournalPath { get; }
    bool Exists();
    string ReadText();
    void WriteNew(RecoveryJournal journal);
    void Delete();
}

internal sealed class RecoveryJournalStore(string journalPath) : IRecoveryJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string JournalPath { get; } = journalPath;
    public bool Exists() => File.Exists(JournalPath);
    public string ReadText() => File.ReadAllText(JournalPath);

    public void WriteNew(RecoveryJournal journal)
    {
        if (Exists()) throw new IOException("An incomplete recovery journal already exists.");
        var directory = Path.GetDirectoryName(JournalPath) ?? throw new InvalidOperationException("Recovery journal directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = JournalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, JournalPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Delete() => File.Delete(JournalPath);
}
