namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Output-only synthetic Steam Deck <c>QuickAccess</c> system-button primitive. Holds a short pulse
/// intent that <see cref="Apply"/> merges into an otherwise mapper-produced <see cref="SteamDeckDeviceState"/>
/// on the existing continuous Steam Deck publish path -- this is not a second state-publication path
/// and never calls VIIPER directly. See docs/VIIPER_MIGRATION_TODO.md SD5 for the (not yet wired)
/// OEM1/Quick Access routing this primitive will eventually serve.
/// </summary>
internal sealed class SteamDeckSystemButtonOverlay
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(50);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private DateTimeOffset? _pulseExpiresAt;

    internal SteamDeckSystemButtonOverlay(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Records that Quick Access should be asserted for the next <see cref="PulseDuration"/>. Does
    /// not call VIIPER; the merge happens inside <see cref="Apply"/> on the existing publish path. A
    /// repeated call while a pulse is already active restarts/extends it from now -- no queue.
    /// </summary>
    internal void RequestQuickAccessPulse()
    {
        lock (_gate) _pulseExpiresAt = _time.GetUtcNow() + PulseDuration;
    }

    /// <summary>Immediately clears any pending/active synthetic Quick Access pulse.</summary>
    internal void Clear()
    {
        lock (_gate) _pulseExpiresAt = null;
    }

    /// <summary>
    /// Called from the existing regular Steam Deck state publication path, after
    /// <see cref="SteamDeckDeviceStateMapper.Map"/> and before the VIIPER <c>SetState</c> call. Sets
    /// only <see cref="SteamDeckDeviceState.QuickAccess"/> while a pulse is active (check-and-clear if
    /// it has expired) and leaves every other field of <paramref name="state"/> untouched.
    /// </summary>
    internal SteamDeckDeviceState Apply(SteamDeckDeviceState state)
    {
        bool active;
        lock (_gate)
        {
            active = _pulseExpiresAt is { } expiresAt && _time.GetUtcNow() < expiresAt;
            if (!active) _pulseExpiresAt = null;
        }

        state.QuickAccess = active ? (byte)1 : (byte)0;
        return state;
    }
}
