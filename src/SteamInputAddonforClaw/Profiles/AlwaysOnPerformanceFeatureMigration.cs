namespace SteamInputAddonforClaw.Profiles;

internal enum AlwaysOnPerformanceFeatureMigrationStatus
{
    Changed,
    Unchanged,
    UnsafeProfile
}

internal readonly record struct AlwaysOnPerformanceFeatureMigrationResult(
    AlwaysOnPerformanceFeatureMigrationStatus Status,
    ProfileLoadStatus ProfileStatus);

/// <summary>Idempotently turns on already-configured TDP, CPU Boost, and Windows Power Mode
/// records. This utility owns no lifecycle or cached state; the caller schedules it after startup.</summary>
internal static class AlwaysOnPerformanceFeatureMigration
{
    internal static AlwaysOnPerformanceFeatureMigrationResult Run(
        ProfileStore store,
        ProfileMutationGate mutationGate,
        CancellationToken cancellationToken = default)
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

            var updated = EnableExistingFeatureRecords(loaded.Document, out var changed);
            if (!changed)
                return new(AlwaysOnPerformanceFeatureMigrationStatus.Unchanged, loaded.Status);

            cancellationToken.ThrowIfCancellationRequested();
            store.Save(updated);
            return new(AlwaysOnPerformanceFeatureMigrationStatus.Changed, loaded.Status);
        }
    }

    private static ProfileDocument EnableExistingFeatureRecords(ProfileDocument document, out bool changed)
    {
        changed = false;
        var performance = document.Device.Performance;
        var deviceTdp = Enable(performance.Tdp, ref changed);
        var deviceCpuBoost = Enable(performance.CpuBoost, ref changed);
        var devicePowerMode = Enable(performance.PowerMode, ref changed);

        Dictionary<string, GameProfile>? games = null;
        foreach (var (key, profile) in document.Games)
        {
            var gamePerformance = profile.Performance;
            var tdp = Enable(gamePerformance.Tdp, ref changed);
            var cpuBoost = Enable(gamePerformance.CpuBoost, ref changed);
            var powerMode = Enable(gamePerformance.PowerMode, ref changed);
            if (ReferenceEquals(tdp, gamePerformance.Tdp)
                && ReferenceEquals(cpuBoost, gamePerformance.CpuBoost)
                && ReferenceEquals(powerMode, gamePerformance.PowerMode))
                continue;

            games ??= new Dictionary<string, GameProfile>(document.Games, document.Games.Comparer);
            games[key] = profile with
            {
                Performance = gamePerformance with { Tdp = tdp, CpuBoost = cpuBoost, PowerMode = powerMode }
            };
        }

        Dictionary<string, XboxGameProfile>? xboxGames = null;
        foreach (var (key, profile) in document.XboxGames)
        {
            var gamePerformance = profile.Performance;
            var tdp = Enable(gamePerformance.Tdp, ref changed);
            var cpuBoost = Enable(gamePerformance.CpuBoost, ref changed);
            var powerMode = Enable(gamePerformance.PowerMode, ref changed);
            if (ReferenceEquals(tdp, gamePerformance.Tdp)
                && ReferenceEquals(cpuBoost, gamePerformance.CpuBoost)
                && ReferenceEquals(powerMode, gamePerformance.PowerMode))
                continue;

            xboxGames ??= new Dictionary<string, XboxGameProfile>(document.XboxGames, document.XboxGames.Comparer);
            xboxGames[key] = profile with
            {
                Performance = gamePerformance with { Tdp = tdp, CpuBoost = cpuBoost, PowerMode = powerMode }
            };
        }

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
            },
            Games = games ?? document.Games,
            XboxGames = xboxGames ?? document.XboxGames
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

    private static GameTdpSettings? Enable(GameTdpSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }

    private static GameCpuBoostSettings? Enable(GameCpuBoostSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }

    private static GamePowerModeSettings? Enable(GamePowerModeSettings? settings, ref bool changed)
    {
        if (settings is not { Enabled: false }) return settings;
        changed = true;
        return settings with { Enabled = true };
    }
}
