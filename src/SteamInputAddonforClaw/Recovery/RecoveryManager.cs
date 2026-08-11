using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Recovery;

internal interface IRecoveryManager
{
    bool HasIncompleteRecovery { get; }
    Task<RecoveryResult> RecoverIncompleteSessionAsync(CancellationToken cancellationToken);
}

internal sealed class RecoveryManager(IRecoveryJournalStore store, HandheldDeviceRegistry? deviceRegistry = null, IHidHideClient? hidHideClient = null, IControllerDeviceEnumerator? deviceEnumerator = null) : IRecoveryManager
{
    internal const int CurrentSchemaVersion = 3;
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
        if (journal.Mutations.DeviceNativeStateChanged || journal.Mutations.TemporaryXbox360OutputCreated ||
            journal.Mutations.HidHideDeviceAdditions is { Count: > 0 } || journal.Mutations.AddonOwnedVirtualDevices is { Count: > 0 } ||
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

    public RecoveryResult RecordAddonOwnedVirtualDeviceIntent(Guid sessionId, Guid mutationId, string deviceType, ushort vendorId, ushort productId, IEnumerable<string> preExistingIds)
    {
        if (mutationId == Guid.Empty || string.IsNullOrWhiteSpace(deviceType)) return new(RecoveryStatus.Failure, "The virtual-device mutation identity is invalid.");
        var existing = NormalizeIds(preExistingIds);
        return UpdateRecoverySession(sessionId, journal =>
        {
            var entries = (journal.Mutations.AddonOwnedVirtualDeviceEntries ?? []).ToList();
            if (entries.Any(entry => entry.MutationId == mutationId)) throw new InvalidOperationException("The virtual-device mutation ID is already recorded.");
            entries.Add(new(mutationId, deviceType, vendorId, productId, existing, []));
            return journal with { Mutations = journal.Mutations with { AddonOwnedVirtualDeviceEntries = entries } };
        });
    }

    public RecoveryResult ResolveAddonOwnedVirtualDeviceIdentity(Guid sessionId, Guid mutationId, IEnumerable<string> resolvedIds)
    {
        var resolved = NormalizeIds(resolvedIds);
        if (resolved.Count == 0) return new(RecoveryStatus.Failure, "At least one resolved virtual-device identity is required.");
        return UpdateRecoverySession(sessionId, journal =>
        {
            var entries = (journal.Mutations.AddonOwnedVirtualDeviceEntries ?? []).ToList();
            var index = entries.FindIndex(entry => entry.MutationId == mutationId);
            if (index < 0) throw new InvalidOperationException("The virtual-device mutation ID is unknown.");
            entries[index] = entries[index] with { ResolvedInstanceIds = resolved };
            return journal with { Mutations = journal.Mutations with { AddonOwnedVirtualDeviceEntries = entries } };
        });
    }

    public RecoveryResult CompleteAddonOwnedVirtualDeviceMutation(Guid sessionId, Guid mutationId) =>
        UpdateRecoverySession(sessionId, journal =>
        {
            var entries = (journal.Mutations.AddonOwnedVirtualDeviceEntries ?? []).Where(entry => entry.MutationId != mutationId).ToArray();
            if (entries.Length == (journal.Mutations.AddonOwnedVirtualDeviceEntries?.Count ?? 0)) throw new InvalidOperationException("The virtual-device mutation ID is unknown.");
            return journal with { Mutations = journal.Mutations with { AddonOwnedVirtualDeviceEntries = entries } };
        });

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string> values) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

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
            if (schema == CurrentSchemaVersion)
            {
                var journal = JsonSerializer.Deserialize<RecoveryJournal>(json) ?? throw new InvalidDataException("The recovery journal contains no recovery state.");
                if (!IsValidJournal(journal))
                    return new(RecoveryStatus.Failure, "Recovery journal is missing required state.", journal);
                return new(RecoveryStatus.Success, "Recovery journal loaded.", journal);
            }
            if (schema == 1) return TranslateLegacyV1(json);
            if (schema == 2)
            {
                var legacy = JsonSerializer.Deserialize<RecoveryJournal>(json) ?? throw new InvalidDataException("The legacy recovery journal contains no state.");
                using var legacyDocument = JsonDocument.Parse(json);
                var virtualProperty = legacyDocument.RootElement.GetProperty("Mutations").GetProperty("AddonOwnedVirtualDevices");
                if (virtualProperty.ValueKind is not JsonValueKind.Null and not JsonValueKind.Array)
                    return new(RecoveryStatus.Failure, "Legacy virtual-device recovery evidence cannot be translated safely.", legacy);
                if (virtualProperty.ValueKind == JsonValueKind.Array && virtualProperty.GetArrayLength() > 0)
                    return new(RecoveryStatus.Failure, "Legacy virtual-device recovery evidence cannot be translated safely.", legacy);
                return new(RecoveryStatus.Success, "Legacy schema v2 recovery journal loaded.", legacy with { SchemaVersion = CurrentSchemaVersion });
            }
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
        if (!CanRecoverRecordedMutations(journal, out var capabilityReason))
            return LogFailure(new(RecoveryStatus.Failure, capabilityReason, journal), stopwatch);

        if (!journal.Mutations.HasRecordedMutations)
        {
            var emptyCompletion = CompleteRecoverySession();
            if (emptyCompletion.Status == RecoveryStatus.Success)
                AppLog.Info("Recovery", "Empty recovery journal completed.", ("SessionId", journal.RecoverySessionId), ("JournalDeleted", true));
            return emptyCompletion with { Journal = journal };
        }

        if (journal.Mutations.AddonOwnedVirtualDeviceEntries is { Count: > 0 } virtualEntries)
        {
            for (var index = virtualEntries.Count - 1; index >= 0; index--)
            {
                var completion = CompleteAddonOwnedVirtualDeviceMutation(journal.RecoverySessionId, virtualEntries[index].MutationId);
                if (completion.Status != RecoveryStatus.Success) return LogFailure(completion with { Journal = journal }, stopwatch);
                journal = completion.Journal ?? journal with
                {
                    Mutations = journal.Mutations with
                    {
                        AddonOwnedVirtualDeviceEntries = virtualEntries.Take(index).ToArray()
                    }
                };
            }
        }

        var deviceAdditions = journal.Mutations.HidHideDeviceAdditions ?? [];
        for (var index = deviceAdditions.Count - 1; index >= 0; index--)
        {
            var recovered = RecoverHidHideDeviceAddition(journal, deviceAdditions[index]);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
            var deviceCompletion = CompleteHidHideDeviceAddition(journal.RecoverySessionId, deviceAdditions[index]);
            if (deviceCompletion.Status != RecoveryStatus.Success) return LogFailure(deviceCompletion with { Journal = journal }, stopwatch);
            journal = deviceCompletion.Journal ?? journal with
            {
                Mutations = journal.Mutations with
                {
                    HidHideDeviceAdditions = deviceAdditions.Take(index).ToArray()
                }
            };
        }

        var additions = journal.Mutations.ExecutableWhitelistAdditions ?? [];
        for (var index = additions.Count - 1; index >= 0; index--)
        {
            var recovered = RecoverHidHideWhitelistAddition(journal, additions[index]);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
            var whitelistCompletion = CompleteHidHideWhitelistAddition(journal.RecoverySessionId, additions[index]);
            if (whitelistCompletion.Status != RecoveryStatus.Success) return LogFailure(whitelistCompletion with { Journal = journal }, stopwatch);
            journal = whitelistCompletion.Journal ?? journal with
            {
                Mutations = journal.Mutations with
                {
                    ExecutableWhitelistAdditions = additions.Take(index).ToArray()
                }
            };
        }
        if (journal.Mutations.DeviceNativeStateChanged)
        {
            var recovered = await RecoverNativeStateAsync(journal, cancellationToken).ConfigureAwait(false);
            if (recovered.Status != RecoveryStatus.Success) return LogFailure(recovered, stopwatch);
            var nativeCompletion = CompleteDeviceNativeStateMutation(journal.RecoverySessionId);
            if (nativeCompletion.Status != RecoveryStatus.Success) return LogFailure(nativeCompletion with { Journal = journal }, stopwatch);
            journal = nativeCompletion.Journal ?? journal with
            {
                OriginalDeviceState = null,
                Mutations = journal.Mutations with { DeviceNativeStateChanged = false }
            };
        }
        var completed = LoadJournal().Status == RecoveryStatus.NoRecoveryNeeded
            ? new RecoveryResult(RecoveryStatus.Success, "Recovery journal deleted.", journal)
            : new RecoveryResult(RecoveryStatus.Failure, "Recovery journal remains after recovery.", journal);
        if (completed.Status == RecoveryStatus.Success)
            AppLog.Info("Recovery", "Recovery completed.", ("SessionId", journal.RecoverySessionId), ("JournalDeleted", true), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return completed with { Journal = journal };
    }

    private bool CanRecoverRecordedMutations(RecoveryJournal journal, out string reason)
    {
        if (!IsValidJournal(journal)) { reason = "Recovery journal is missing required state."; return false; }
        if (journal.Mutations.AddonOwnedVirtualDevices is { Count: > 0 }) { reason = "Recorded virtual-device mutations are unsupported."; return false; }
        if (journal.Mutations.AddonOwnedVirtualDeviceEntries is { Count: > 0 } entries)
        {
            if (deviceEnumerator is null) { reason = "Virtual-device recovery enumeration is unavailable."; return false; }
            var present = deviceEnumerator.EnumeratePresentDevices();
            foreach (var entry in entries)
            {
                var ids = entry.ResolvedInstanceIds.Count > 0 ? entry.ResolvedInstanceIds :
                    present.Where(device => device.VendorId == entry.VendorId && device.ProductId == entry.ProductId &&
                        !entry.PreExistingMatchingInstanceIds.Contains(device.InstanceId, StringComparer.OrdinalIgnoreCase))
                        .Select(device => device.InstanceId).ToArray();
                if (ids.Any(id => present.Any(device => string.Equals(device.InstanceId, id, StringComparison.OrdinalIgnoreCase))))
                { reason = "A journaled virtual device is still present."; return false; }
            }
        }
        if (journal.Mutations.TemporaryXbox360OutputCreated) { reason = "Recorded Xbox output mutations are unsupported."; return false; }
        if (journal.Mutations.ExecutableWhitelistAdditions is { Count: > 0 } || journal.Mutations.HidHideDeviceAdditions is { Count: > 0 })
        {
            if (hidHideClient is null) { reason = "HidHide recovery support is unavailable."; return false; }
            var inspection = hidHideClient.Inspect();
            if (!inspection.IsConfigurationReadable)
            { reason = $"HidHide recovery inspection is unsafe: {inspection.Status}."; return false; }
        }
        if (journal.Mutations.HidHideDeviceAdditions is { Count: > 0 } &&
            journal.Mutations.HidHideDeviceAdditions.Any(string.IsNullOrWhiteSpace))
        { reason = "The journaled HidHide device entry is invalid."; return false; }
        if (journal.Mutations.DeviceNativeStateChanged && (deviceRegistry is null || journal.OriginalDeviceState is null))
        { reason = "Native-state recovery support is unavailable."; return false; }
        if (journal.Mutations.DeviceNativeStateChanged)
        {
            if (!deviceRegistry!.TryGetById(journal.OriginalDeviceState!.DeviceId, out var adapter))
            { reason = "The journaled handheld device adapter is unavailable."; return false; }
            if (adapter.NativeState is null || adapter.NativeState.DeviceId != journal.OriginalDeviceState.DeviceId)
            { reason = "The journaled handheld device native-state manager is unavailable."; return false; }
        }
        reason = "Recoverable recorded mutations.";
        return true;
    }

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

    private RecoveryResult RecoverHidHideWhitelistAddition(RecoveryJournal journal, string executablePath)
    {
        if (hidHideClient is null)
            return new(RecoveryStatus.Failure, "HidHide recovery support is unavailable.", journal);

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

    private RecoveryResult RecoverHidHideDeviceAddition(RecoveryJournal journal, string deviceEntry)
    {
        if (hidHideClient is null)
            return new(RecoveryStatus.Failure, "HidHide recovery support is unavailable.", journal);

        var inspection = hidHideClient.Inspect();
        AppLog.Info("HidHide", "HidHide device recovery inspection completed.", ("Status", inspection.Status), ("DeviceEntry", deviceEntry));
        if (!inspection.IsConfigurationReadable)
            return new(RecoveryStatus.Failure, $"HidHide recovery inspection is unsafe: {inspection.Status}.", journal);

        var present = (inspection.HiddenDeviceEntries ?? [])
            .Any(entry => string.Equals(entry, deviceEntry, StringComparison.OrdinalIgnoreCase));
        if (!present)
            return new(RecoveryStatus.Success, "Recorded HidHide device entry was already restored.", journal);

        if (!hidHideClient.RemoveHiddenDevice(deviceEntry))
            return new(RecoveryStatus.Failure, "Recorded HidHide device entry could not be removed.", journal);

        var verification = hidHideClient.Inspect();
        if (!verification.IsConfigurationReadable || (verification.HiddenDeviceEntries ?? [])
            .Any(entry => string.Equals(entry, deviceEntry, StringComparison.OrdinalIgnoreCase)))
            return new(RecoveryStatus.Failure, "Recorded HidHide device entry removal could not be verified.", journal);

        return new(RecoveryStatus.Success, "Recorded HidHide device entry restored.", journal);
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
