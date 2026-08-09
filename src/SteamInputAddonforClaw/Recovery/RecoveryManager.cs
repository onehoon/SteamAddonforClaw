using System.Diagnostics;
using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Recovery;

internal interface IRecoveryManager
{
    bool HasIncompleteRecovery { get; }
    RecoveryResult RecoverIncompleteSession();
}

internal sealed class RecoveryManager(IRecoveryJournalStore store, IHidHideClient? hidHideClient = null) : IRecoveryManager
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

    public RecoveryResult BeginRecoverySession(MsiControllerSnapshotResult snapshotResult)
        => BeginRecoverySession(snapshotResult, new RecoveryMutationState());

    public RecoveryResult BeginHidHideWhitelistLease(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new(RecoveryStatus.Failure, "The HidHide executable path is unavailable.");
        var journal = new RecoveryJournal(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new MsiControllerSnapshot(MsiControllerNativeMode.Indeterminate, null, null, null, null, DateTimeOffset.UtcNow),
            new(ExecutableWhitelistAdditions: [Path.GetFullPath(executablePath)]));
        try
        {
            store.WriteNew(journal);
            AppLog.Info("Recovery", "HidHide whitelist lease journal persisted.", ("SessionId", journal.RecoverySessionId), ("JournalPath", store.JournalPath));
            return new(RecoveryStatus.Success, "HidHide whitelist lease journal persisted.", journal);
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "HidHide whitelist lease journal could not be persisted.", exception, ("JournalPath", store.JournalPath), ("Action", "DoNotMutate"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

    public bool OwnsHidHideWhitelistLease(string executablePath)
    {
        var loaded = LoadJournal();
        return loaded.Status == RecoveryStatus.Success && loaded.Journal is { } journal &&
            CanRecoverHidHideWhitelistLease(journal) &&
            string.Equals(journal.Mutations.ExecutableWhitelistAdditions!.Single(), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    private RecoveryResult BeginRecoverySession(MsiControllerSnapshotResult snapshotResult, RecoveryMutationState mutations)
    {
        if (!snapshotResult.AllowsMutation || snapshotResult.Snapshot is not { } snapshot || snapshot.Mode == MsiControllerNativeMode.Indeterminate)
        {
            AppLog.Warn("Recovery", "Recovery session authorization denied.", null,
                ("SnapshotStatus", snapshotResult.Status),
                ("AllowsMutation", snapshotResult.AllowsMutation),
                ("Mode", snapshotResult.Snapshot?.Mode),
                ("Reason", snapshotResult.Reason),
                ("Action", "Passive"));
            return new(RecoveryStatus.Failure, "Only a successful, mutation-authorizing snapshot result can start recovery.");
        }
        var journal = new RecoveryJournal(CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, snapshot, mutations);
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
        {
            if (!CanRecoverHidHideWhitelistLease(journal))
                return LogFailure(new(RecoveryStatus.Failure, "This version cannot restore the recorded mutation state.", journal), stopwatch);

            var recovered = RecoverHidHideWhitelistLease(journal);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
        }
        var completed = CompleteRecoverySession();
        if (completed.Status == RecoveryStatus.Success)
            AppLog.Info("Recovery", "Recovery completed.", ("SessionId", journal.RecoverySessionId), ("JournalDeleted", true), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return completed with { Journal = journal };
    }

    private static bool CanRecoverHidHideWhitelistLease(RecoveryJournal journal) =>
        !journal.Mutations.ControllerModeChanged && !journal.Mutations.TemporaryXbox360OutputCreated &&
        journal.Mutations.HidHideDeviceAdditions is not { Count: > 0 } && journal.Mutations.AddonOwnedVirtualDevices is not { Count: > 0 } &&
        journal.Mutations.ExecutableWhitelistAdditions is { Count: 1 };

    private RecoveryResult RecoverHidHideWhitelistLease(RecoveryJournal journal)
    {
        if (hidHideClient is null)
            return new(RecoveryStatus.Failure, "HidHide recovery support is unavailable.", journal);

        var executablePath = journal.Mutations.ExecutableWhitelistAdditions!.Single();
        var inspection = hidHideClient.Inspect();
        AppLog.Info("HidHide", "HidHide recovery inspection completed.", ("Status", inspection.Status), ("ExecutablePath", executablePath));
        if (!inspection.IsConfigurationReadable)
            return new(RecoveryStatus.Failure, $"HidHide recovery inspection is unsafe: {inspection.Status}.", journal);

        if (!inspection.ApplicationWhitelist.Contains(executablePath))
        {
            AppLog.Info("HidHide", "Recorded HidHide whitelist lease was already absent.", ("ExecutablePath", executablePath), ("Action", "ClearJournal"));
            return new(RecoveryStatus.Success, "Recorded HidHide whitelist lease was already restored.", journal);
        }

        if (!hidHideClient.RemoveApplication(executablePath))
            return new(RecoveryStatus.Failure, "Recorded HidHide whitelist entry could not be removed.", journal);

        var verification = hidHideClient.Inspect();
        if (!verification.IsConfigurationReadable || verification.ApplicationWhitelist.Contains(executablePath))
            return new(RecoveryStatus.Failure, "Recorded HidHide whitelist entry removal could not be verified.", journal);

        AppLog.Info("HidHide", "Recorded HidHide whitelist lease removed.", ("ExecutablePath", executablePath));
        return new(RecoveryStatus.Success, "Recorded HidHide whitelist lease restored.", journal);
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
