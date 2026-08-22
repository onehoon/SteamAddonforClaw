using SteamInputAddonforClaw.Contracts.DeviceProfiles;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Small persistence owner for enabling and disabling complete game profiles.</summary>
internal sealed class GameProfileMutations
{
    private const int FallbackPl1Watts = 20;
    private const int FallbackPl2Watts = 22;
    private readonly ProfileStore _store;
    private readonly ProfileMutationGate _gate;

    internal GameProfileMutations(ProfileStore store, ProfileMutationGate? gate = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? new ProfileMutationGate();
    }

    internal bool Enable(uint appId, string? displayName)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace)
                return false;

            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var existing = loaded.Document.Games.TryGetValue(key, out var profile) ? profile : new GameProfile();
            var completed = Complete(existing, loaded.Document.Device);
            loaded.Document.Games[key] = completed with { Enabled = true, DisplayName = displayName ?? completed.DisplayName };
            _store.Save(loaded.Document);
            return true;
        }
    }

    internal bool Disable(uint appId)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace)
                return false;

            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile))
                return true;

            loaded.Document.Games[key] = profile with { Enabled = false };
            _store.Save(loaded.Document);
            return true;
        }
    }

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
