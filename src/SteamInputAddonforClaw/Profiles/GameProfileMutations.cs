using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>Small persistence owner for ensuring complete enabled game profiles.</summary>
internal sealed class GameProfileMutations
{
    private readonly ProfileStore _store;
    private readonly ProfileMutationGate _gate;

    private TdpPowerLimits? _tdpLimits;
    internal GameProfileMutations(ProfileStore store, ProfileMutationGate? gate = null, TdpPowerLimits? tdpLimits = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? new ProfileMutationGate();
        _tdpLimits = tdpLimits;
    }

    internal void SetTdpLimits(TdpPowerLimits tdpLimits) => _tdpLimits = tdpLimits ?? throw new ArgumentNullException(nameof(tdpLimits));

    internal enum MutationOutcome { Succeeded, InvalidTarget, PersistenceFailed, Unavailable }
    internal sealed record Capture(uint AppId, GameProfile Profile, bool Exists, bool PersistenceWritable);
    internal sealed record EnrollmentResult(MutationOutcome Outcome, bool Changed, int SkippedCount, int ActivatedCount);

    internal TdpPowerLimits? TdpLimits => _tdpLimits;

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

    internal (bool Enabled, SteamVirtualControllerPreference? Preference) CaptureSteamVirtualController(uint appId)
    {
        var captured = CaptureProfile(appId);
        return captured is null || !captured.PersistenceWritable
            ? (false, null)
            : (captured.Exists && captured.Profile.Enabled, captured.Profile.SteamVirtualController);
    }

    internal MutationOutcome SetSteamVirtualController(uint appId, SteamVirtualControllerPreference? preference)
    {
        if (preference is { } value && !Enum.IsDefined(value)) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile) || !profile.Enabled)
                return MutationOutcome.Unavailable;
            if (profile.SteamVirtualController == preference) return MutationOutcome.Succeeded;
            loaded.Document.Games[key] = profile with { SteamVirtualController = preference };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetEnabled(uint appId, bool enabled, string? displayName)
    {
        if (appId == 0) return MutationOutcome.InvalidTarget;
        if (!enabled) return MutationOutcome.Succeeded;
        return EnsureProfiles(
            new Dictionary<uint, string?> { [appId] = displayName },
            EmptyXboxGames,
            includeExistingProfiles: false).Outcome;
    }

    internal EnrollmentResult EnsureProfiles(
        IReadOnlyDictionary<uint, string?> steamGames,
        IReadOnlyDictionary<string, string?> xboxGames,
        bool includeExistingProfiles)
    {
        ArgumentNullException.ThrowIfNull(steamGames);
        ArgumentNullException.ThrowIfNull(xboxGames);
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace)
                return new(MutationOutcome.PersistenceFailed, false, 0, 0);

            var document = GameProfileEnrollment.Ensure(
                loaded.Document,
                steamGames,
                xboxGames,
                includeExistingProfiles,
                _tdpLimits,
                out var changed,
                out var skippedCount,
                out var activatedCount);
            if (!changed)
                return new(MutationOutcome.Succeeded, false, skippedCount, activatedCount);

            try
            {
                _store.Save(document);
                return new(MutationOutcome.Succeeded, true, skippedCount, activatedCount);
            }
            catch
            {
                return new(MutationOutcome.PersistenceFailed, false, skippedCount, activatedCount);
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyXboxGames =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    internal MutationOutcome SetCpuBoostAc(uint appId, CpuBoostMode mode)
        => SetCpuBoost(appId, cpu => cpu with { Ac = mode });

    internal MutationOutcome SetCpuBoostEnabled(uint appId, bool enabled)
        => SetCpuBoost(appId, cpu => cpu with { Enabled = enabled });

    internal MutationOutcome SetCpuBoostDc(uint appId, CpuBoostMode mode)
        => SetCpuBoost(appId, cpu => cpu with { Dc = mode });
    internal MutationOutcome SetPowerModeAc(uint appId, WindowsPowerMode mode) => SetPowerMode(appId, p => p with { Ac = mode });
    internal MutationOutcome SetPowerModeDc(uint appId, WindowsPowerMode mode) => SetPowerMode(appId, p => p with { Dc = mode });
    internal MutationOutcome SetPowerModeEnabled(uint appId, bool enabled) => SetPowerMode(appId, p => p with { Enabled = enabled });
    internal MutationOutcome SetFpsLimitEnabled(uint appId, bool enabled)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)) return MutationOutcome.Unavailable;
            var fps = profile.Performance.FpsLimit ?? new GameFpsLimitSettings { Enabled = false, AcFps = 60, DcFps = 60 };
            try { loaded.Document.Games[key] = profile with { Performance = profile.Performance with { FpsLimit = fps with { Enabled = enabled } } }; _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }
    internal MutationOutcome SetFpsLimitAc(uint appId, int fps) => SetFpsLimitValue(appId, fps, true);
    internal MutationOutcome SetFpsLimitDc(uint appId, int fps) => SetFpsLimitValue(appId, fps, false);

    internal MutationOutcome SetGpuMinimumClockEnabled(uint appId, bool enabled, GameGpuMinimumClockSettings? enabledSettings)
    {
        if (enabled && (enabledSettings is not { Enabled: true }
            || !double.IsFinite(enabledSettings.AcMhz) || enabledSettings.AcMhz <= 0
            || !double.IsFinite(enabledSettings.DcMhz) || enabledSettings.DcMhz <= 0))
            return MutationOutcome.InvalidTarget;

        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile) || !profile.Enabled)
                return MutationOutcome.Unavailable;

            var current = profile.Performance.GpuMinimumClock;
            var updated = enabled
                ? enabledSettings!
                : current is null ? null : current with { Enabled = false };
            if (current == updated) return MutationOutcome.Succeeded;
            loaded.Document.Games[key] = profile with
            {
                Performance = profile.Performance with { GpuMinimumClock = updated }
            };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetGpuMinimumClockAc(uint appId, double mhz) => SetGpuMinimumClockRail(appId, mhz, ac: true);
    internal MutationOutcome SetGpuMinimumClockDc(uint appId, double mhz) => SetGpuMinimumClockRail(appId, mhz, ac: false);

    private MutationOutcome SetGpuMinimumClockRail(uint appId, double mhz, bool ac)
    {
        if (!double.IsFinite(mhz) || mhz <= 0) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)
                || !profile.Enabled
                || profile.Performance.GpuMinimumClock is not { Enabled: true } current)
                return MutationOutcome.Unavailable;

            var updated = ac ? current with { AcMhz = mhz } : current with { DcMhz = mhz };
            loaded.Document.Games[key] = profile with
            {
                Performance = profile.Performance with { GpuMinimumClock = updated }
            };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }
    private MutationOutcome SetFpsLimitValue(uint appId, int fps, bool ac)
    {
        if (fps is < 40 or > 120) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)) return MutationOutcome.Unavailable;
            var current = profile.Performance.FpsLimit ?? new GameFpsLimitSettings { Enabled = false, AcFps = 60, DcFps = 60 };
            var updated = ac ? current with { AcFps = fps } : current with { DcFps = fps };
            try { loaded.Document.Games[key] = profile with { Performance = profile.Performance with { FpsLimit = updated } }; _store.Save(loaded.Document); return MutationOutcome.Succeeded; }
            catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal IReadOnlySet<uint>? CaptureFavoriteAppIds()
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return null;
            return loaded.Document.Games.Where(x => x.Value.Favorite && uint.TryParse(x.Key, out _)).Select(x => uint.Parse(x.Key, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        }
    }

    internal MutationOutcome SetFavorite(uint appId, bool favorite, string? displayName)
    {
        if (appId == 0) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var current))
            {
                if (!favorite) return MutationOutcome.Succeeded;
                current = new GameProfile();
            }

            if (!current.Enabled)
            {
                if (!GameProfileEnrollment.TryComplete(current, loaded.Document.Device, _tdpLimits, out var completed))
                    return MutationOutcome.InvalidTarget;
                current = completed;
            }

            var updated = current with { Favorite = favorite, DisplayName = SelectDisplayName(current.DisplayName, displayName) };
            if (loaded.Document.Games.TryGetValue(key, out var existing) && existing == updated)
                return MutationOutcome.Succeeded;

            loaded.Document.Games[key] = updated;
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetResolution(uint appId, GameDisplayResolution? resolution, string? displayName)
    {
        if (appId == 0) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var current))
            {
                if (resolution is null) return MutationOutcome.Succeeded;
                current = new GameProfile();
            }
            if (!current.Enabled)
            {
                if (!GameProfileEnrollment.TryComplete(current, loaded.Document.Device, _tdpLimits, out var completed))
                    return MutationOutcome.InvalidTarget;
                current = completed;
            }
            if (current.Display.Resolution is null && resolution is null) return MutationOutcome.Succeeded;
            var display = current.Display with { Resolution = resolution };
            var updated = current with { Display = display, DisplayName = SelectDisplayName(current.DisplayName, displayName) };
            if (loaded.Document.Games.TryGetValue(key, out var existing) && existing == updated)
                return MutationOutcome.Succeeded;
            loaded.Document.Games[key] = updated;
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetScalingTo100Percent(uint appId, bool enabled, string? displayName)
    {
        if (appId == 0) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var current))
            {
                if (!enabled) return MutationOutcome.Succeeded;
                current = new GameProfile();
            }
            if (!current.Enabled)
            {
                if (!GameProfileEnrollment.TryComplete(current, loaded.Document.Device, _tdpLimits, out var completed))
                    return MutationOutcome.InvalidTarget;
                current = completed;
            }
            if (current.Display.ScalingTo100Percent == enabled) return MutationOutcome.Succeeded;
            loaded.Document.Games[key] = current with
            {
                Display = current.Display with { ScalingTo100Percent = enabled },
                DisplayName = SelectDisplayName(current.DisplayName, displayName)
            };
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

    private MutationOutcome SetPowerMode(uint appId, Func<GamePowerModeSettings, GamePowerModeSettings> update)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)) return MutationOutcome.Unavailable;
            var completed = Complete(profile, loaded.Document.Device);
            if (completed.Performance.PowerMode is not { } current) return MutationOutcome.Unavailable;
            loaded.Document.Games[key] = completed with { Performance = completed.Performance with { PowerMode = update(current) } };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetTdp(uint appId, TdpPowerPair ac, TdpPowerPair dc)
    {
        if (_tdpLimits is not { } limits || !limits.IsValid(ac) || !limits.IsValid(dc)) return MutationOutcome.InvalidTarget;
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile)
                || profile.Performance.Tdp is not { } current)
                return MutationOutcome.Unavailable;
            loaded.Document.Games[key] = profile with { Performance = profile.Performance with { Tdp = current with { Ac = ac, Dc = dc } } };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal MutationOutcome SetTdpEnabled(uint appId, bool enabled)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) return MutationOutcome.PersistenceFailed;
            var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!loaded.Document.Games.TryGetValue(key, out var profile) || profile.Performance.Tdp is not { } current) return MutationOutcome.Unavailable;
            loaded.Document.Games[key] = profile with { Performance = profile.Performance with { Tdp = current with { Enabled = enabled } } };
            try { _store.Save(loaded.Document); return MutationOutcome.Succeeded; } catch { return MutationOutcome.PersistenceFailed; }
        }
    }

    internal bool Enable(uint appId, string? displayName)
        => SetEnabled(appId, true, displayName) == MutationOutcome.Succeeded;

    // Keep the legacy call contract safe for stale callers; game profiles are always enabled.
    internal bool Disable(uint appId)
        => SetEnabled(appId, false, null) == MutationOutcome.Succeeded;

    private static GameProfile Complete(GameProfile profile, DeviceSettings device)
    {
        return profile with { Performance = GameProfileEnrollment.CompleteForCapture(profile.Performance, device) };
    }

    private static string? SelectDisplayName(string? existing, string? incoming) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
}
