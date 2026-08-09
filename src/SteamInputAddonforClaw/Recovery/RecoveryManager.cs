using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;

namespace SteamInputAddonforClaw.Recovery;

internal interface IRecoveryManager
{
    bool HasIncompleteRecovery { get; }
    Task<RecoveryResult> RecoverIncompleteSessionAsync(CancellationToken cancellationToken);
}

internal sealed class RecoveryManager(IRecoveryJournalStore store, HandheldDeviceRegistry? deviceRegistry = null, IHidHideClient? hidHideClient = null) : IRecoveryManager
{
    internal const int CurrentSchemaVersion = 2;
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

    public RecoveryResult BeginDeviceNativeStateMutation(NativeStateCaptureResult captureResult)
    {
        if (!captureResult.AllowsMutation || captureResult.Snapshot is null)
            return new(RecoveryStatus.Failure, "Only a successful, mutation-authorizing native-state snapshot can start recovery.");
        return BeginRecoverySession(captureResult.Snapshot, new RecoveryMutationState(DeviceNativeStateChanged: true));
    }

    public RecoveryResult BeginHidHideWhitelistLease(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new(RecoveryStatus.Failure, "The HidHide executable path is unavailable.");
        var journal = new RecoveryJournal(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
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

    private RecoveryResult BeginRecoverySession(DeviceNativeStateSnapshot snapshot, RecoveryMutationState mutations)
    {
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
            var json = store.ReadText();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("SchemaVersion", out var schemaElement) || !schemaElement.TryGetInt32(out var schema))
                return new(RecoveryStatus.Failure, "Recovery journal schema is missing.");
            if (schema == CurrentSchemaVersion)
            {
                var journal = JsonSerializer.Deserialize<RecoveryJournal>(json) ?? throw new InvalidDataException("The recovery journal contains no recovery state.");
                if (journal.RecoverySessionId == Guid.Empty || journal.Mutations is null ||
                    (journal.Mutations.DeviceNativeStateChanged && journal.OriginalDeviceState is null))
                    return new(RecoveryStatus.Failure, "Recovery journal is missing required state.", journal);
                return new(RecoveryStatus.Success, "Recovery journal loaded.", journal);
            }
            if (schema == 1) return TranslateLegacyV1(json);
            return new(RecoveryStatus.Failure, $"Unsupported recovery schema {schema}.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery journal could not be loaded.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

    public async Task<RecoveryResult> RecoverIncompleteSessionAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("Recovery", "Recovery check started.", ("JournalPath", store.JournalPath));
        var loaded = LoadJournal();
        if (loaded.Status == RecoveryStatus.NoRecoveryNeeded) return loaded;
        if (loaded.Status == RecoveryStatus.Failure) return LogFailure(loaded, stopwatch);
        var journal = loaded.Journal!;
        AppLog.Info("Recovery", "Incomplete recovery journal detected.", ("SessionId", journal.RecoverySessionId), ("SchemaVersion", journal.SchemaVersion),
            ("DeviceId", journal.OriginalDeviceState?.DeviceId), ("RecordedMutations", journal.Mutations.HasRecordedMutations));
        if (journal.Mutations.DeviceNativeStateChanged)
        {
            if (!CanRecoverOnlyNativeState(journal))
                return LogFailure(new(RecoveryStatus.Failure, "This version cannot restore the recorded mutation combination.", journal), stopwatch);
            var recovered = await RecoverNativeStateAsync(journal, cancellationToken).ConfigureAwait(false);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
        }
        else if (journal.Mutations.HasRecordedMutations)
        {
            if (!CanRecoverHidHideWhitelistLease(journal)) return LogFailure(new(RecoveryStatus.Failure, "This version cannot restore the recorded mutation state.", journal), stopwatch);
            var recovered = RecoverHidHideWhitelistLease(journal);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
        }
        var completed = CompleteRecoverySession();
        if (completed.Status == RecoveryStatus.Success)
            AppLog.Info("Recovery", "Recovery completed.", ("SessionId", journal.RecoverySessionId), ("JournalDeleted", true), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return completed with { Journal = journal };
    }

    private static bool CanRecoverHidHideWhitelistLease(RecoveryJournal journal) =>
        !journal.Mutations.DeviceNativeStateChanged && !journal.Mutations.TemporaryXbox360OutputCreated &&
        journal.Mutations.HidHideDeviceAdditions is not { Count: > 0 } && journal.Mutations.AddonOwnedVirtualDevices is not { Count: > 0 } &&
        journal.Mutations.ExecutableWhitelistAdditions is { Count: 1 };

    private static bool CanRecoverOnlyNativeState(RecoveryJournal journal) =>
        journal.OriginalDeviceState is not null && !journal.Mutations.TemporaryXbox360OutputCreated &&
        journal.Mutations.HidHideDeviceAdditions is not { Count: > 0 } && journal.Mutations.ExecutableWhitelistAdditions is not { Count: > 0 } &&
        journal.Mutations.AddonOwnedVirtualDevices is not { Count: > 0 };

    private async Task<RecoveryResult> RecoverNativeStateAsync(RecoveryJournal journal, CancellationToken cancellationToken)
    {
        var snapshot = journal.OriginalDeviceState!;
        if (deviceRegistry is null || !deviceRegistry.TryGetById(snapshot.DeviceId, out var adapter))
            return new(RecoveryStatus.Failure, "The journaled handheld device adapter is unavailable.", journal);
        if (adapter.NativeState is null || adapter.NativeState.DeviceId != snapshot.DeviceId)
            return new(RecoveryStatus.Failure, "The journaled handheld device native-state manager is unavailable.", journal);
        var restored = await adapter.NativeState.RestoreSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return restored.Restored
            ? new(RecoveryStatus.Success, restored.Reason, journal)
            : new(RecoveryStatus.Failure, $"Native-state recovery failed: {restored.Reason}", journal);
    }

    private static RecoveryResult TranslateLegacyV1(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyRecoveryJournalV1>(json) ?? throw new InvalidDataException("The legacy recovery journal contains no state.");
        var mutations = legacy.Mutations ?? throw new InvalidDataException("The legacy recovery journal mutations are missing.");
        var unsupported = mutations.ControllerModeChanged || mutations.TemporaryXbox360OutputCreated ||
            mutations.HidHideDeviceAdditions is { Count: > 0 } || mutations.AddonOwnedVirtualDevices is { Count: > 0 };
        if (legacy.RecoverySessionId == Guid.Empty || unsupported || mutations.ExecutableWhitelistAdditions is { Count: > 1 })
            return new(RecoveryStatus.Failure, "Legacy recovery journal cannot be restored safely.");
        var journal = new RecoveryJournal(CurrentSchemaVersion, legacy.RecoverySessionId, legacy.CreatedAt, null,
            new(ExecutableWhitelistAdditions: mutations.ExecutableWhitelistAdditions));
        return new(RecoveryStatus.Success, "Recoverable legacy recovery journal loaded.", journal);
    }

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
