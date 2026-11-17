using System.Globalization;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Profiles;

internal static class GameProfileEnrollment
{
    private const int FallbackPl1Watts = 20;
    private const int FallbackPl2Watts = 22;

    internal static ProfileDocument Ensure(
        ProfileDocument document,
        IEnumerable<KeyValuePair<uint, string?>> steamGames,
        IEnumerable<KeyValuePair<string, string?>> xboxGames,
        bool includeExistingProfiles,
        TdpPowerLimits? limits,
        out bool changed,
        out int skippedCount,
        out int activatedCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(steamGames);
        ArgumentNullException.ThrowIfNull(xboxGames);
        changed = false;
        skippedCount = 0;
        activatedCount = 0;

        var steamTargets = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (includeExistingProfiles)
        {
            foreach (var key in document.Games.Keys)
                if (TryParseCanonicalAppId(key, out _))
                    steamTargets.TryAdd(key, null);
        }

        foreach (var (appId, displayName) in steamGames)
        {
            if (appId == 0)
            {
                skippedCount++;
                continue;
            }

            steamTargets[appId.ToString(CultureInfo.InvariantCulture)] = displayName;
        }

        var xboxTargets = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (includeExistingProfiles)
        {
            foreach (var key in document.XboxGames.Keys)
                if (XboxGameProfileMutations.IsCanonicalKey(key))
                    xboxTargets.TryAdd(key, null);
        }

        foreach (var (key, displayName) in xboxGames)
        {
            if (!XboxGameProfileMutations.IsCanonicalKey(key))
            {
                skippedCount++;
                continue;
            }

            xboxTargets[key] = displayName;
        }

        var games = document.Games;
        foreach (var (key, displayName) in steamTargets)
        {
            document.Games.TryGetValue(key, out var current);
            if (!TryComplete(current ?? new GameProfile(), document.Device, limits, out var completed))
            {
                skippedCount++;
                continue;
            }

            var updated = completed with
            {
                Enabled = true,
                DisplayName = SelectDisplayName(current?.DisplayName, displayName)
            };
            if (current?.Enabled != true)
                activatedCount++;
            if (current == updated)
                continue;

            if (ReferenceEquals(games, document.Games))
                games = new Dictionary<string, GameProfile>(document.Games, document.Games.Comparer);
            games[key] = updated;
            changed = true;
        }

        var xboxProfiles = document.XboxGames;
        foreach (var (key, displayName) in xboxTargets)
        {
            document.XboxGames.TryGetValue(key, out var current);
            if (!TryComplete(current ?? new XboxGameProfile(), document.Device, limits, out var completed))
            {
                skippedCount++;
                continue;
            }

            var updated = completed with
            {
                Enabled = true,
                DisplayName = SelectDisplayName(current?.DisplayName, displayName)
            };
            if (current?.Enabled != true)
                activatedCount++;
            if (current == updated)
                continue;

            if (ReferenceEquals(xboxProfiles, document.XboxGames))
                xboxProfiles = new Dictionary<string, XboxGameProfile>(document.XboxGames, document.XboxGames.Comparer);
            xboxProfiles[key] = updated;
            changed = true;
        }

        if (!changed)
            return document;

        return document with { Games = games, XboxGames = xboxProfiles };
    }

    internal static bool TryComplete(
        GameProfile profile,
        DeviceSettings device,
        TdpPowerLimits? limits,
        out GameProfile completed)
    {
        if (!TryCompletePerformance(profile.Performance, device, limits, out var performance))
        {
            completed = profile;
            return false;
        }

        completed = profile with { Enabled = true, Performance = performance };
        return true;
    }

    internal static bool TryComplete(
        XboxGameProfile profile,
        DeviceSettings device,
        TdpPowerLimits? limits,
        out XboxGameProfile completed)
    {
        if (!TryCompletePerformance(profile.Performance, device, limits, out var performance))
        {
            completed = profile;
            return false;
        }

        completed = profile with { Enabled = true, Performance = performance };
        return true;
    }

    internal static GamePerformanceOverrides CompleteForCapture(GamePerformanceOverrides performance, DeviceSettings device)
    {
        var cpu = BuildCpuBoostDefaults(device);
        var tdp = BuildTdpDefaults(device, limits: null)!;
        var powerMode = BuildPowerModeDefaults(device);
        return CompletePerformance(performance, cpu, tdp, powerMode);
    }

    internal static bool TryParseCanonicalAppId(string key, out uint appId) =>
        uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out appId)
        && appId > 0
        && string.Equals(key, appId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static bool TryCompletePerformance(
        GamePerformanceOverrides performance,
        DeviceSettings device,
        TdpPowerLimits? limits,
        out GamePerformanceOverrides completed)
    {
        if (limits is null)
        {
            completed = performance;
            return false;
        }

        var cpu = BuildCpuBoostDefaults(device);
        var tdp = performance.Tdp is { } existingTdp
            ? existingTdp with { Enabled = true }
            : BuildTdpDefaults(device, limits);
        if (tdp is null)
        {
            completed = performance;
            return false;
        }

        var powerMode = BuildPowerModeDefaults(device);
        completed = CompletePerformance(performance, cpu, tdp, powerMode);
        if (completed.Tdp is not { Ac: not null, Dc: not null } savedTdp
            || !limits.IsValid(savedTdp.Ac)
            || !limits.IsValid(savedTdp.Dc))
        {
            completed = performance;
            return false;
        }

        return true;
    }

    private static GamePerformanceOverrides CompletePerformance(
        GamePerformanceOverrides performance,
        GameCpuBoostSettings cpuDefaults,
        GameTdpSettings tdpDefaults,
        GamePowerModeSettings powerModeDefaults)
    {
        var currentTdp = performance.Tdp;
        var tdp = currentTdp is null
            ? tdpDefaults
            : currentTdp with
            {
                Enabled = true,
                Ac = currentTdp.Ac ?? tdpDefaults.Ac,
                Dc = currentTdp.Dc ?? tdpDefaults.Dc
            };

        return performance with
        {
            CpuBoost = performance.CpuBoost is { } currentCpu
                ? currentCpu with { Enabled = true }
                : cpuDefaults,
            Tdp = tdp,
            PowerMode = performance.PowerMode is { } currentPowerMode
                ? currentPowerMode with { Enabled = true }
                : powerModeDefaults
        };
    }

    private static GameCpuBoostSettings BuildCpuBoostDefaults(DeviceSettings device)
    {
        var configured = device.Performance.CpuBoost;
        var ac = configured?.Ac is { } acMode && Enum.IsDefined(acMode) ? acMode : CpuBoostMode.EfficientEnabled;
        var dc = configured?.Dc is { } dcMode && Enum.IsDefined(dcMode) ? dcMode : CpuBoostMode.EfficientEnabled;
        return new GameCpuBoostSettings { Enabled = true, Ac = ac, Dc = dc };
    }

    private static GameTdpSettings? BuildTdpDefaults(DeviceSettings device, TdpPowerLimits? limits)
    {
        var configured = device.Performance.Tdp;
        var ac = BuildTdpPair(configured?.Ac, limits);
        var dc = BuildTdpPair(configured?.Dc, limits);
        if (ac is null || dc is null)
            return null;

        return new GameTdpSettings { Enabled = true, Ac = ac, Dc = dc };
    }

    private static TdpPowerPair? BuildTdpPair(TdpPowerPair? configured, TdpPowerLimits? limits)
    {
        var pl1 = configured?.Pl1Watts ?? FallbackPl1Watts;
        var pl2 = configured?.Pl2Watts ?? FallbackPl2Watts;

        if (limits is not null && (!limits.IsValidPl1(pl1) || !limits.IsValidPl2(pl2)))
            return null;

        return new TdpPowerPair { Pl1Watts = pl1, Pl2Watts = pl2 };
    }

    private static GamePowerModeSettings BuildPowerModeDefaults(DeviceSettings device)
    {
        var configured = device.Performance.PowerMode;
        var ac = configured is { } settings && Enum.IsDefined(settings.Ac) ? settings.Ac : WindowsPowerMode.BestPerformance;
        var dc = configured is { } settingsDc && Enum.IsDefined(settingsDc.Dc) ? settingsDc.Dc : WindowsPowerMode.BestPerformance;
        return new GamePowerModeSettings { Enabled = true, Ac = ac, Dc = dc };
    }

    private static string? SelectDisplayName(string? existing, string? incoming) =>
        string.IsNullOrWhiteSpace(existing) ? incoming : existing;
}
