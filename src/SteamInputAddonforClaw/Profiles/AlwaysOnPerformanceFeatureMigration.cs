using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Profiles;

internal enum AlwaysOnPerformanceFeatureMigrationStatus
{
    Changed,
    Unchanged,
    UnsafeProfile
}

internal readonly record struct AlwaysOnPerformanceFeatureMigrationResult(
    AlwaysOnPerformanceFeatureMigrationStatus Status,
    ProfileLoadStatus ProfileStatus,
    int ActivatedProfileCount = 0);

/// <summary>Idempotently turns on already-configured TDP, CPU Boost, and Windows Power Mode
/// records. This utility owns no lifecycle or cached state; the caller schedules it after startup.</summary>
internal static class AlwaysOnPerformanceFeatureMigration
{
    internal static AlwaysOnPerformanceFeatureMigrationResult Run(
        ProfileStore store,
        ProfileMutationGate mutationGate,
        CancellationToken cancellationToken = default,
        TdpPowerLimits? tdpLimits = null,
        IReadOnlyDictionary<uint, string?>? steamGames = null,
        IReadOnlyDictionary<string, string?>? xboxGames = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mutationGate);
        cancellationToken.ThrowIfCancellationRequested();

        lock (mutationGate.Sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = store.Load();
            if (!loaded.CanSafelyReplace)
                return new(AlwaysOnPerformanceFeatureMigrationStatus.UnsafeProfile, loaded.Status);

            var updated = GameProfileEnrollment.Ensure(
                loaded.Document,
                steamGames ?? EmptySteamGames,
                xboxGames ?? EmptyXboxGames,
                includeExistingProfiles: true,
                tdpLimits,
                out var changed,
                out _,
                out var activatedCount);
            updated = EnableExistingFeatureRecords(updated, out var deviceFeaturesChanged);
            changed |= deviceFeaturesChanged;
            if (!changed)
                return new(AlwaysOnPerformanceFeatureMigrationStatus.Unchanged, loaded.Status);

            cancellationToken.ThrowIfCancellationRequested();
            store.Save(updated);
            return new(AlwaysOnPerformanceFeatureMigrationStatus.Changed, loaded.Status, activatedCount);
        }
    }

    private static readonly IReadOnlyDictionary<uint, string?> EmptySteamGames = new Dictionary<uint, string?>();
    private static readonly IReadOnlyDictionary<string, string?> EmptyXboxGames = new Dictionary<string, string?>();

    private static ProfileDocument EnableExistingFeatureRecords(ProfileDocument document, out bool changed)
    {
        changed = false;
        var performance = document.Device.Performance;
        var deviceTdp = Enable(performance.Tdp, ref changed);
        var deviceCpuBoost = Enable(performance.CpuBoost, ref changed);
        var devicePowerMode = Enable(performance.PowerMode, ref changed);

        if (!changed)
            return document;

        return document with
        {
            Device = document.Device with
            {
                Performance = performance with
                {
                    Tdp = deviceTdp,
                    CpuBoost = deviceCpuBoost,
                    PowerMode = devicePowerMode
                }
            }
        };
    }

    private static DeviceTdpSettings? Enable(DeviceTdpSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }

    private static DeviceCpuBoostSettings? Enable(DeviceCpuBoostSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }

    private static DevicePowerModeSettings? Enable(DevicePowerModeSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }

}
