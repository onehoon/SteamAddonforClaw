using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Persistence-only mutations for the canonical string-keyed XBOX profile collection.</summary>
internal sealed class XboxGameProfileMutations
{
    private const int FallbackPl1Watts = 20;
    private const int FallbackPl2Watts = 22;
    private readonly ProfileStore _store;
    private readonly ProfileMutationGate _gate;
    private HandheldDeviceModelId? _modelId;

    internal XboxGameProfileMutations(ProfileStore store, ProfileMutationGate gate, HandheldDeviceModelId? modelId = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _modelId = modelId;
    }

    internal enum MutationOutcome { Succeeded, InvalidTarget, PersistenceFailed, Unavailable }

    internal sealed record Capture(string Key, XboxGameProfile Profile, bool Exists, bool PersistenceWritable);

    private readonly record struct MutationChange(MutationOutcome Outcome, bool Changed)
    {
        internal static MutationChange ChangedSuccessfully => new(MutationOutcome.Succeeded, true);
        internal static MutationChange NoChange => new(MutationOutcome.Succeeded, false);
        internal static MutationChange Unavailable => new(MutationOutcome.Unavailable, false);
    }

    internal void SetModelId(HandheldDeviceModelId modelId) => _modelId = modelId;

    internal Capture CaptureProfile(string key)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            var writable = IsCanonicalKey(key) && loaded.CanSafelyReplace;
            if (!writable)
                return new(key, Complete(new XboxGameProfile(), loaded.Document.Device), false, false);

            return loaded.Document.XboxGames.TryGetValue(key, out var profile)
                ? new(key, Complete(profile, loaded.Document.Device), true, true)
                : new(key, Complete(new XboxGameProfile(), loaded.Document.Device), false, true);
        }
    }

    internal IReadOnlySet<string> CaptureFavoriteKeys()
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            return loaded.Document.XboxGames
                .Where(entry => entry.Value.Favorite && IsCanonicalKey(entry.Key))
                .Select(entry => entry.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    internal MutationOutcome SetEnabled(string key, bool enabled, string? displayName) => Mutate(key, (document, canonicalKey) =>
    {
        if (!enabled)
        {
            if (!document.XboxGames.TryGetValue(canonicalKey, out var existing)) return MutationChange.NoChange;
            document.XboxGames[canonicalKey] = existing with { Enabled = false };
            return MutationChange.ChangedSuccessfully;
        }

        var profile = document.XboxGames.TryGetValue(canonicalKey, out var current)
            ? Complete(current, document.Device)
            : Complete(new XboxGameProfile(), document.Device);
        document.XboxGames[canonicalKey] = profile with
        {
            Enabled = true,
            DisplayName = displayName ?? profile.DisplayName
        };
        return MutationChange.ChangedSuccessfully;
    });

    internal MutationOutcome SetFavorite(string key, bool favorite, string? displayName) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var current))
        {
            if (!favorite) return MutationChange.NoChange;
            document.XboxGames[canonicalKey] = new XboxGameProfile { Favorite = true, DisplayName = displayName };
        }
        else
        {
            document.XboxGames[canonicalKey] = current with
            {
                Favorite = favorite,
                DisplayName = displayName ?? current.DisplayName
            };
        }
        return MutationChange.ChangedSuccessfully;
    });

    internal MutationOutcome SetCpuBoostEnabled(string key, bool enabled) => UpdateCpuBoost(key, current => current with { Enabled = enabled });
    internal MutationOutcome SetCpuBoostAc(string key, CpuBoostMode mode) => UpdateCpuBoost(key, current => current with { Ac = mode });
    internal MutationOutcome SetCpuBoostDc(string key, CpuBoostMode mode) => UpdateCpuBoost(key, current => current with { Dc = mode });

    internal MutationOutcome SetTdpEnabled(string key, bool enabled) => UpdateTdp(key, current => current with { Enabled = enabled });

    internal MutationOutcome SetTdp(string key, TdpPowerPair ac, TdpPowerPair dc)
    {
        if (ac is null || dc is null
            || _modelId is not { } model
            || !MsiClawTdpPolicy.TryResolve(model, out var policy)
            || !policy.IsValid(ac)
            || !policy.IsValid(dc))
            return MutationOutcome.InvalidTarget;

        return UpdateTdp(key, current => current with { Ac = ac, Dc = dc });
    }

    internal MutationOutcome SetPowerModeEnabled(string key, bool enabled) => UpdatePowerMode(key, current => current with { Enabled = enabled });
    internal MutationOutcome SetPowerModeAc(string key, WindowsPowerMode mode) => UpdatePowerMode(key, current => current with { Ac = mode });
    internal MutationOutcome SetPowerModeDc(string key, WindowsPowerMode mode) => UpdatePowerMode(key, current => current with { Dc = mode });

    internal MutationOutcome SetFpsLimitEnabled(string key, bool enabled) => MutateFpsLimit(key, current => current with { Enabled = enabled });
    internal MutationOutcome SetFpsLimitAc(string key, int fps) => SetFpsLimitValue(key, fps, ac: true);
    internal MutationOutcome SetFpsLimitDc(string key, int fps) => SetFpsLimitValue(key, fps, ac: false);

    internal MutationOutcome SetResolution(string key, GameDisplayResolution? resolution, string? displayName) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var current))
        {
            if (resolution is null) return MutationChange.NoChange;
            current = new XboxGameProfile { Enabled = false, DisplayName = displayName };
        }

        if (current.Display.Resolution is null && resolution is null) return MutationChange.NoChange;
        document.XboxGames[canonicalKey] = current with
        {
            Display = current.Display with { Resolution = resolution },
            DisplayName = displayName ?? current.DisplayName
        };
        return MutationChange.ChangedSuccessfully;
    });

    internal static bool IsCanonicalKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return (key.StartsWith("store:", StringComparison.Ordinal) && key.Length > "store:".Length)
            || (key.StartsWith("pfn:", StringComparison.Ordinal) && key.Length > "pfn:".Length)
            || (key.StartsWith("identity:", StringComparison.Ordinal) && key.Length > "identity:".Length);
    }

    private MutationOutcome UpdateCpuBoost(string key, Func<GameCpuBoostSettings, GameCpuBoostSettings> update) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var profile) || profile.Performance.CpuBoost is not { } current)
            return MutationChange.Unavailable;
        document.XboxGames[canonicalKey] = profile with { Performance = profile.Performance with { CpuBoost = update(current) } };
        return MutationChange.ChangedSuccessfully;
    });

    private MutationOutcome UpdateTdp(string key, Func<GameTdpSettings, GameTdpSettings> update) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var profile) || profile.Performance.Tdp is not { } current)
            return MutationChange.Unavailable;
        document.XboxGames[canonicalKey] = profile with { Performance = profile.Performance with { Tdp = update(current) } };
        return MutationChange.ChangedSuccessfully;
    });

    private MutationOutcome UpdatePowerMode(string key, Func<GamePowerModeSettings, GamePowerModeSettings> update) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var profile)) return MutationChange.Unavailable;
        var completed = Complete(profile, document.Device);
        if (completed.Performance.PowerMode is not { } current) return MutationChange.Unavailable;
        document.XboxGames[canonicalKey] = completed with { Performance = completed.Performance with { PowerMode = update(current) } };
        return MutationChange.ChangedSuccessfully;
    });

    private MutationOutcome MutateFpsLimit(string key, Func<GameFpsLimitSettings, GameFpsLimitSettings> update) => Mutate(key, (document, canonicalKey) =>
    {
        if (!document.XboxGames.TryGetValue(canonicalKey, out var profile)) return MutationChange.Unavailable;
        var current = profile.Performance.FpsLimit ?? new GameFpsLimitSettings { Enabled = false, AcFps = 60, DcFps = 60 };
        document.XboxGames[canonicalKey] = profile with { Performance = profile.Performance with { FpsLimit = update(current) } };
        return MutationChange.ChangedSuccessfully;
    });

    private MutationOutcome SetFpsLimitValue(string key, int fps, bool ac)
    {
        if (fps is < 40 or > 120) return MutationOutcome.InvalidTarget;
        return MutateFpsLimit(key, current => ac ? current with { AcFps = fps } : current with { DcFps = fps });
    }

    private MutationOutcome Mutate(string? key, Func<ProfileDocument, string, MutationChange> update)
    {
        if (!IsCanonicalKey(key)) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;

            try
            {
                var change = update(loaded.Document, key!);
                if (change.Outcome != MutationOutcome.Succeeded || !change.Changed) return change.Outcome;
                _store.Save(loaded.Document);
                return MutationOutcome.Succeeded;
            }
            catch
            {
                return MutationOutcome.PersistenceFailed;
            }
        }
    }

    private static XboxGameProfile Complete(XboxGameProfile profile, DeviceSettings device)
    {
        var cpu = device.Performance.CpuBoost is { Ac: { } ac, Dc: { } dc }
            ? new GameCpuBoostSettings { Enabled = true, Ac = ac, Dc = dc }
            : new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled };
        var tdp = device.Performance.Tdp is { Ac: { } tdpAc, Dc: { } tdpDc }
            ? new GameTdpSettings { Enabled = true, Ac = tdpAc, Dc = tdpDc }
            : new GameTdpSettings
            {
                Enabled = true,
                Ac = new TdpPowerPair { Pl1Watts = FallbackPl1Watts, Pl2Watts = FallbackPl2Watts },
                Dc = new TdpPowerPair { Pl1Watts = FallbackPl1Watts, Pl2Watts = FallbackPl2Watts }
            };
        var power = device.Performance.PowerMode is { Ac: { } powerAc, Dc: { } powerDc }
            ? new GamePowerModeSettings { Enabled = true, Ac = powerAc, Dc = powerDc }
            : null;

        var completeTdp = profile.Performance.Tdp is { Ac: not null, Dc: not null } existingTdp ? existingTdp : tdp;
        return profile with
        {
            Performance = profile.Performance with
            {
                CpuBoost = profile.Performance.CpuBoost ?? cpu,
                Tdp = completeTdp,
                PowerMode = profile.Performance.PowerMode ?? power
            }
        };
    }
}
