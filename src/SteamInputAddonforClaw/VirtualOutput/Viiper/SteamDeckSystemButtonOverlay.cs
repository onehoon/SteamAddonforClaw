using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Output-only synthetic Steam Deck <c>Steam</c>/<c>QuickAccess</c> system-button primitive. Holds short pulses
/// intent that <see cref="Apply"/> merges into an otherwise mapper-produced <see cref="SteamDeckDeviceState"/>
/// on the existing continuous Steam Deck publish path -- this is not a second state-publication path
/// and never calls VIIPER directly. Steam pulse timing is software-only and remains unvalidated on
/// physical hardware.
/// </summary>
internal sealed class SteamDeckSystemButtonOverlay
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(50);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private DateTimeOffset? _steamPulseExpiresAt;
    private DateTimeOffset? _quickAccessPulseExpiresAt;
    private bool _publishedSteamActive;
    private bool _publishedQuickAccessActive;

    internal SteamDeckSystemButtonOverlay(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Records that Quick Access should be asserted for the next <see cref="PulseDuration"/>. Does
    /// not call VIIPER; the merge happens inside <see cref="Apply"/> on the existing publish path. A
    /// repeated call while a pulse is already active restarts/extends it from now -- no queue.
    /// </summary>
    internal void RequestQuickAccessPulse()
    {
        lock (_gate) _quickAccessPulseExpiresAt = _time.GetUtcNow() + PulseDuration;
        AppLog.Debug("SteamDeck.QuickAccess", "QuickAccess pulse requested");
    }

    internal void RequestSteamPulse()
    {
        lock (_gate) _steamPulseExpiresAt = _time.GetUtcNow() + PulseDuration;
        AppLog.Debug("SteamDeck.SystemButton", "Steam pulse requested");
    }

    /// <summary>Immediately clears any pending/active synthetic Quick Access pulse.</summary>
    internal void Clear()
    {
        lock (_gate) { _steamPulseExpiresAt = null; _quickAccessPulseExpiresAt = null; }
    }

    /// <summary>
    /// Called from the existing regular Steam Deck state publication path, after
    /// <see cref="SteamDeckDeviceStateMapper.Map"/> and before the VIIPER <c>SetState</c> call. Sets
    /// only <see cref="SteamDeckDeviceState.QuickAccess"/> while a pulse is active (check-and-clear if
    /// it has expired) and leaves every other field of <paramref name="state"/> untouched.
    /// </summary>
    internal SteamDeckDeviceState Apply(SteamDeckDeviceState state)
    {
        bool steamActive, quickAccessActive, steamEdge, quickAccessEdge;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            steamActive = _steamPulseExpiresAt is { } steam && now < steam;
            quickAccessActive = _quickAccessPulseExpiresAt is { } quick && now < quick;
            if (!steamActive) _steamPulseExpiresAt = null;
            if (!quickAccessActive) _quickAccessPulseExpiresAt = null;
            steamEdge = steamActive != _publishedSteamActive;
            quickAccessEdge = quickAccessActive != _publishedQuickAccessActive;
            _publishedSteamActive = steamActive;
            _publishedQuickAccessActive = quickAccessActive;
        }

        if (steamEdge) AppLog.Debug("SteamDeck.SystemButton", steamActive ? "Steam asserted" : "Steam cleared");
        if (quickAccessEdge) AppLog.Debug("SteamDeck.SystemButton", quickAccessActive ? "QuickAccess asserted" : "QuickAccess cleared");

        state.Steam = steamActive ? (byte)1 : (byte)0;
        state.QuickAccess = quickAccessActive ? (byte)1 : (byte)0;
        return state;
    }
}
