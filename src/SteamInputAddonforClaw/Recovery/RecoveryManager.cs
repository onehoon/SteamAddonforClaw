using System.Diagnostics;
using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Recovery;

internal interface IRecoveryManager
{
    bool HasIncompleteRecovery { get; }
    RecoveryResult RecoverIncompleteSession();
}

internal sealed class RecoveryManager(IRecoveryJournalStore store) : IRecoveryManager
{
    internal const int CurrentSchemaVersion = 1;
    public bool HasIncompleteRecovery
    {
        get
        {
            try { return store.Exists(); }
            catch (Exception exception)
            {
                AppLog.Error("Recovery", "Recovery journal existence check failed.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
                return true;
            }
        }
    }

    public RecoveryResult BeginRecoverySession(MsiControllerSnapshot snapshot)
    {
        if (snapshot.Mode == MsiControllerNativeMode.Indeterminate)
            return new(RecoveryStatus.Failure, "An indeterminate snapshot cannot authorize mutation.");
        var journal = new RecoveryJournal(CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, snapshot, new());
        try
        {
            store.WriteNew(journal);
            AppLog.Info("Recovery", "Recovery session journal persisted.", ("SessionId", journal.RecoverySessionId), ("SchemaVersion", journal.SchemaVersion), ("JournalPath", store.JournalPath));
            return new(RecoveryStatus.Success, "Recovery journal persisted; future mutation may proceed.", journal);
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery session could not be started.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

    public RecoveryResult LoadJournal()
    {
        try
        {
            if (!store.Exists()) return new(RecoveryStatus.NoRecoveryNeeded, "Recovery journal does not exist.");
            var journal = store.Read();
            if (journal.SchemaVersion != CurrentSchemaVersion)
                return new(RecoveryStatus.Failure, $"Unsupported recovery schema {journal.SchemaVersion}.", journal);
            if (journal.RecoverySessionId == Guid.Empty || journal.OriginalControllerState is null || journal.Mutations is null)
                return new(RecoveryStatus.Failure, "Recovery journal is missing required state.", journal);
            return new(RecoveryStatus.Success, "Recovery journal loaded.", journal);
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery journal could not be loaded.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

    public RecoveryResult RecoverIncompleteSession()
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("Recovery", "Recovery check started.", ("JournalPath", store.JournalPath));
        var loaded = LoadJournal();
        if (loaded.Status == RecoveryStatus.NoRecoveryNeeded) return loaded;
        if (loaded.Status == RecoveryStatus.Failure) return LogFailure(loaded, stopwatch);
        var journal = loaded.Journal!;
        AppLog.Info("Recovery", "Incomplete recovery journal detected.", ("SessionId", journal.RecoverySessionId), ("SchemaVersion", journal.SchemaVersion),
            ("OriginalMode", journal.OriginalControllerState.Mode), ("RecordedMutations", journal.Mutations.HasRecordedMutations));
        if (journal.Mutations.HasRecordedMutations)
            return LogFailure(new(RecoveryStatus.Failure, "This version cannot restore the recorded mutation state.", journal), stopwatch);
        var completed = CompleteRecoverySession();
        if (completed.Status == RecoveryStatus.Success)
            AppLog.Info("Recovery", "Recovery completed.", ("SessionId", journal.RecoverySessionId), ("JournalDeleted", true), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return completed with { Journal = journal };
    }

    public RecoveryResult CompleteRecoverySession()
    {
        try
        {
            if (!store.Exists()) return new(RecoveryStatus.NoRecoveryNeeded, "Recovery journal does not exist.");
            store.Delete();
            if (store.Exists()) return new(RecoveryStatus.Failure, "Recovery journal still exists after deletion.");
            return new(RecoveryStatus.Success, "Recovery journal deleted.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery journal cleanup failed.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

    private static RecoveryResult LogFailure(RecoveryResult result, Stopwatch stopwatch)
    {
        AppLog.Error("Recovery", "Recovery failed.", new InvalidOperationException(result.Reason),
            ("SessionId", result.Journal?.RecoverySessionId), ("Action", "Passive"), ("Reason", result.Reason), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return result;
    }
}
