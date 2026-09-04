using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Recovery;

internal sealed class RecoveryManager(IRecoveryJournalStore store)
{
    internal const int CurrentSchemaVersion = 5;
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
        executablePath = Path.GetFullPath(executablePath);
        var journal = new RecoveryJournal(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            new(ExecutableWhitelistAdditions: [executablePath]));
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
            journal.Mutations.ExecutableWhitelistAdditions is { Count: > 0 } additions &&
            additions.Any(path => string.Equals(path, Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase));
    }

    public Guid? GetHidHideWhitelistLeaseSessionId(string executablePath)
    {
        var loaded = LoadJournal();
        if (loaded.Status != RecoveryStatus.Success || loaded.Journal is not { } journal)
            return null;
        var normalized = Path.GetFullPath(executablePath);
        return journal.Mutations.ExecutableWhitelistAdditions is { Count: > 0 } additions &&
            additions.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))
            ? journal.RecoverySessionId
            : null;
    }

    public Guid? TryGetStandaloneHidHideWhitelistLeaseSessionId(string executablePath)
    {
        var loaded = LoadJournal();
        if (loaded.Status != RecoveryStatus.Success || loaded.Journal is not { } journal)
            return null;
        if (journal.Mutations.DeviceNativeStateChanged ||
            journal.Mutations.HidHideDeviceAdditions is { Count: > 0 } ||
            journal.Mutations.AddonOwnedVirtualDeviceEntries is { Count: > 0 } ||
            journal.Mutations.ExecutableWhitelistAdditions is not { Count: 1 })
            return null;
        var normalized = Path.GetFullPath(executablePath);
        return string.Equals(journal.Mutations.ExecutableWhitelistAdditions[0], normalized, StringComparison.OrdinalIgnoreCase)
            ? journal.RecoverySessionId
            : null;
    }

    public RecoveryResult RecordHidHideWhitelistAddition(Guid recoverySessionId, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new(RecoveryStatus.Failure, "The HidHide executable path is unavailable.");
        executablePath = Path.GetFullPath(executablePath);
        return UpdateRecoverySession(recoverySessionId, journal =>
        {
            var additions = (journal.Mutations.ExecutableWhitelistAdditions ?? []).ToList();
            if (!additions.Contains(executablePath, StringComparer.OrdinalIgnoreCase)) additions.Add(executablePath);
            return journal with { Mutations = journal.Mutations with { ExecutableWhitelistAdditions = additions } };
        });
    }

    public RecoveryResult RecordHidHideDeviceAddition(Guid recoverySessionId, string deviceEntry)
    {
        if (!TryNormalizeDeviceEntry(deviceEntry, out var normalized))
            return new(RecoveryStatus.Failure, "The HidHide device entry is unavailable.");
        return UpdateRecoverySession(recoverySessionId, journal =>
        {
            var additions = (journal.Mutations.HidHideDeviceAdditions ?? []).ToList();
            if (!additions.Contains(normalized, StringComparer.OrdinalIgnoreCase)) additions.Add(normalized);
            return journal with { Mutations = journal.Mutations with { HidHideDeviceAdditions = additions } };
        });
    }

    public RecoveryResult RecordHidHideActiveStateMutation(Guid recoverySessionId, bool originalActiveState) =>
        UpdateRecoverySession(recoverySessionId, journal => journal with
        {
            Mutations = journal.Mutations with { OriginalHidHideActiveState = originalActiveState }
        });

    public RecoveryResult CompleteHidHideActiveStateMutation(Guid recoverySessionId) =>
        UpdateRecoverySession(recoverySessionId, journal => journal with
        {
            Mutations = journal.Mutations with { OriginalHidHideActiveState = null }
        });

    // Full1902 Cleanup F: the Addon-owned virtual-device mutation writers are gone -- current
    // Full1902 VIIPER presentation is not a RecoveryJournal mutation session. The schema-v5
    // AddonOwnedVirtualDeviceEntries collection is still read/validated below so an old
    // development-build recovery.json remains loadable until the dedicated RecoveryJournal cleanup.

    public RecoveryResult CompleteDeviceNativeStateMutation(Guid recoverySessionId) =>
        UpdateRecoverySession(recoverySessionId, journal => journal with
        {
            OriginalDeviceState = null,
            Mutations = journal.Mutations with { DeviceNativeStateChanged = false }
        });

    public RecoveryResult CompleteHidHideWhitelistAddition(Guid recoverySessionId, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new(RecoveryStatus.Failure, "The HidHide executable path is unavailable.");
        executablePath = Path.GetFullPath(executablePath);
        return UpdateRecoverySession(recoverySessionId, journal =>
        {
            var additions = (journal.Mutations.ExecutableWhitelistAdditions ?? [])
                .Where(path => !string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return journal with { Mutations = journal.Mutations with { ExecutableWhitelistAdditions = additions } };
        });
    }

    public RecoveryResult CompleteHidHideDeviceAddition(Guid recoverySessionId, string deviceEntry)
    {
        if (!TryNormalizeDeviceEntry(deviceEntry, out var normalized))
            return new(RecoveryStatus.Failure, "The HidHide device entry is unavailable.");
        return UpdateRecoverySession(recoverySessionId, journal =>
        {
            var additions = (journal.Mutations.HidHideDeviceAdditions ?? [])
                .Where(entry => !string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return journal with { Mutations = journal.Mutations with { HidHideDeviceAdditions = additions } };
        });
    }

    private static bool TryNormalizeDeviceEntry(string? deviceEntry, out string normalized)
    {
        normalized = deviceEntry?.Trim() ?? string.Empty;
        return normalized.Length > 0;
    }

    private RecoveryResult UpdateRecoverySession(Guid expectedRecoverySessionId, Func<RecoveryJournal, RecoveryJournal> update)
    {
        var loaded = LoadJournal();
        if (loaded.Status != RecoveryStatus.Success || loaded.Journal is not { } journal)
            return loaded.Status == RecoveryStatus.NoRecoveryNeeded
                ? new(RecoveryStatus.Failure, "The recovery journal does not exist.")
                : loaded;
        if (journal.RecoverySessionId != expectedRecoverySessionId)
            return new(RecoveryStatus.Failure, "The recovery session ID does not match.", journal);
        try
        {
            var updated = update(journal);
            if (updated.RecoverySessionId != expectedRecoverySessionId || !IsValidJournal(updated))
                return new(RecoveryStatus.Failure, "The updated recovery journal is invalid.", journal);
            if (updated.Mutations.HasRecordedMutations)
                store.ReplaceExisting(updated);
            else
                store.Delete();
            return new(RecoveryStatus.Success, "Recovery mutation updated.", updated);
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery mutation update failed; evidence was preserved where possible.", exception,
                ("SessionId", expectedRecoverySessionId), ("JournalPath", store.JournalPath), ("Action", "PreserveJournal"));
            return new(RecoveryStatus.Failure, exception.Message, journal);
        }
    }

    private static bool IsValidJournal(RecoveryJournal journal) =>
        journal.SchemaVersion == CurrentSchemaVersion &&
        journal.RecoverySessionId != Guid.Empty && journal.Mutations is not null &&
        (!journal.Mutations.DeviceNativeStateChanged || journal.OriginalDeviceState is not null) &&
        (journal.Mutations.AddonOwnedVirtualDeviceEntries ?? []).All(entry =>
            entry.MutationId != Guid.Empty && !string.IsNullOrWhiteSpace(entry.DeviceType) &&
            entry.PreExistingMatchingInstanceIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
            entry.ResolvedInstanceIds.All(id => !string.IsNullOrWhiteSpace(id)));

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
            if (schema != CurrentSchemaVersion)
                return new(RecoveryStatus.Failure, $"Unsupported recovery schema {schema}.");
            var journal = JsonSerializer.Deserialize<RecoveryJournal>(json) ?? throw new InvalidDataException("The recovery journal contains no recovery state.");
            if (!IsValidJournal(journal))
                return new(RecoveryStatus.Failure, "Recovery journal is missing required state.", journal);
            return new(RecoveryStatus.Success, "Recovery journal loaded.", journal);
        }
        catch (Exception exception)
        {
            AppLog.Error("Recovery", "Recovery journal could not be loaded.", exception, ("JournalPath", store.JournalPath), ("Action", "Passive"));
            return new(RecoveryStatus.Failure, exception.Message);
        }
    }

}
