using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Small persistence owner for enabling and disabling complete game profiles.</summary>
internal sealed class GameProfileMutations
{
    private const int FallbackPl1Watts = 20;
    private const int FallbackPl2Watts = 22;
    private readonly ProfileStore _store;
    private readonly ProfileMutationGate _gate;

    private HandheldDeviceModelId? _modelId;
    internal GameProfileMutations(ProfileStore store, ProfileMutationGate? gate = null, HandheldDeviceModelId? modelId = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? new ProfileMutationGate();
        _modelId = modelId;
    }

    internal void SetModelId(HandheldDeviceModelId modelId) => _modelId = modelId;

    internal enum MutationOutcome { Succeeded, InvalidTarget, PersistenceFailed, Unavailable }
    internal sealed record Capture(uint AppId, GameProfile Profile, bool Exists, bool PersistenceWritable);

    internal Capture? CaptureProfile(uint appId)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return new(appId, Complete(new GameProfile(), loaded.Document.Device), false, false);
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return loaded.Document.Games.TryGetValue(key, out var profile)
                ? new(appId, Complete(profile, loaded.Document.Device), true, true)
                : new(appId, Complete(new GameProfile(), loaded.Document.Device), false, true);
        }
    }

    internal MutationOutcome SetEnabled(uint appId, bool enabled, string? displayName)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GameProfile profile;
            if (enabled)
            {
                var existing = loaded.Document.Games.TryGetValue(key, out var current) ? current : new GameProfile();
                var completed = Complete(existing, loaded.Document.Device);
                profile = completed with { Enabled = true, DisplayName = displayName ?? completed.DisplayName };
            }
            else
            {
                if (!loaded.Document.Games.TryGetValue(key, out var disabledProfile)) return MutationOutcome.Succeeded;
                profile = disabledProfile with { Enabled = false };
            }
            loaded.Document.Games[key] = profile;
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetCpuBoostAc(uint appId, CpuBoostMode mode)
        => SetCpuBoost(appId, cpu => cpu with { Ac = mode });

    internal MutationOutcome SetCpuBoostDc(uint appId, CpuBoostMode mode)
        => SetCpuBoost(appId, cpu => cpu with { Dc = mode });

    internal IReadOnlySet<uint> CaptureFavoriteAppIds()
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            return loaded.Document.Games.Where(x => x.Value.Favorite && uint.TryParse(x.Key, out _)).Select(x => uint.Parse(x.Key, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        }
    }

    internal MutationOutcome SetFavorite(uint appId, bool favorite, string? displayName)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var current))
            {
                if (!favorite) return MutationOutcome.Succeeded;
                loaded.Document.Games[key] = new GameProfile { Favorite = true, DisplayName = displayName };
            }
            else loaded.Document.Games[key] = current with { Favorite = favorite, DisplayName = displayName ?? current.DisplayName };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    private MutationOutcome SetCpuBoost(uint appId, Func<GameCpuBoostSettings, GameCpuBoostSettings> update)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)) return MutationOutcome.Unavailable;
            var currentCpu = profile.Performance.CpuBoost;
            if (currentCpu is null) return MutationOutcome.Unavailable;
            loaded.Document.Games[key] = profile with { Performance = profile.Performance with { CpuBoost = update(currentCpu) } };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetTdp(uint appId, TdpPowerPair ac, TdpPowerPair dc)
    {
        if (_modelId is not { } model || !MsiClawTdpPolicy.TryResolve(model, out var policy) || !policy.IsValid(ac) || !policy.IsValid(dc)) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)) return MutationOutcome.Unavailable;
            loaded.Document.Games[key] = profile with { Performance = profile.Performance with { Tdp = new GameTdpSettings { Ac = ac, Dc = dc } } };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal bool Enable(uint appId, string? displayName)
        => SetEnabled(appId, true, displayName) == MutationOutcome.Succeeded;

    internal bool Disable(uint appId)
        => SetEnabled(appId, false, null) == MutationOutcome.Succeeded;

    private static GameProfile Complete(GameProfile profile, DeviceSettings device)
    {
        var cpu = device.Performance.CpuBoost is { Ac: { } ac, Dc: { } dc }
            ? new GameCpuBoostSettings { Ac = ac, Dc = dc }
            : new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled };

        var tdp = device.Performance.Tdp is { Ac: { } tdpAc, Dc: { } tdpDc }
            ? new GameTdpSettings { Ac = tdpAc, Dc = tdpDc }
            : new GameTdpSettings
            {
                Ac = new TdpPowerPair { Pl1Watts = FallbackPl1Watts, Pl2Watts = FallbackPl2Watts },
                Dc = new TdpPowerPair { Pl1Watts = FallbackPl1Watts, Pl2Watts = FallbackPl2Watts }
            };

        var existingTdp = profile.Performance.Tdp is { Ac: { }, Dc: { } } existing
            ? existing
            : tdp;
        return profile with { Performance = profile.Performance with { CpuBoost = profile.Performance.CpuBoost ?? cpu, Tdp = existingTdp } };
    }
}
