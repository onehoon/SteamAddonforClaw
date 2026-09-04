using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Recovery;

/// <summary>
/// Full1902 Cleanup G: read-only. Current Full1902 runtime never creates or updates recovery.json --
/// controller recovery is current-world reconciliation, not historical mutation replay. This type
/// only reports journal presence (<see cref="HasIncompleteRecovery"/>) and loads/validates a
/// pre-existing schema-v5 development-build file (<see cref="LoadJournal"/>) so the existing bounded
/// startup HidHide cleanup / fail-close / retirement policy can act on it. The final decision on
/// whether an old recovery.json remains supported belongs to the dedicated RecoveryJournal cleanup.
/// </summary>
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

    private static bool IsValidJournal(RecoveryJournal journal) =>
        journal.SchemaVersion == CurrentSchemaVersion &&
        journal.RecoverySessionId != Guid.Empty && journal.Mutations is not null &&
        (!journal.Mutations.DeviceNativeStateChanged || journal.OriginalDeviceState is not null) &&
        (journal.Mutations.AddonOwnedVirtualDeviceEntries ?? []).All(entry =>
            entry.MutationId != Guid.Empty && !string.IsNullOrWhiteSpace(entry.DeviceType) &&
            entry.PreExistingMatchingInstanceIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
            entry.ResolvedInstanceIds.All(id => !string.IsNullOrWhiteSpace(id)));
}
