namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Merges finite Steam/Quick Access pulses into the existing Edge publication path.</summary>
internal sealed class DualSenseEdgeSystemButtonOverlay
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(100);
    private const uint Ps = 0x00010000;
    private const uint Cross = 0x00000020;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private DateTimeOffset? _steamPulseExpiresAt;
    private DateTimeOffset? _quickAccessPulseExpiresAt;

    internal DualSenseEdgeSystemButtonOverlay(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    internal void RequestSteamPulse()
    {
        lock (_gate) _steamPulseExpiresAt = _time.GetUtcNow() + PulseDuration;
    }

    internal void RequestQuickAccessPulse()
    {
        lock (_gate) _quickAccessPulseExpiresAt = _time.GetUtcNow() + PulseDuration;
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _steamPulseExpiresAt = null;
            _quickAccessPulseExpiresAt = null;
        }
    }

    internal DSDeviceState Apply(DSDeviceState state)
    {
        bool steamActive;
        bool quickAccessActive;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            steamActive = _steamPulseExpiresAt is { } steam && now < steam;
            quickAccessActive = _quickAccessPulseExpiresAt is { } quickAccess && now < quickAccess;
            if (!steamActive) _steamPulseExpiresAt = null;
            if (!quickAccessActive) _quickAccessPulseExpiresAt = null;
        }

        if (steamActive || quickAccessActive) state.Buttons |= Ps;
        if (quickAccessActive) state.Buttons |= Cross;
        return state;
    }
}
