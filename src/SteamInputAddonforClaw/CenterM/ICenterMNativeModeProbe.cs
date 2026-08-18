namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMNativeModeProbeResult
{
    /// <summary>Physical controller is authoritatively, stably in XInput mode.</summary>
    XInput,
    /// <summary>Physical controller is authoritatively in some other mode (DirectInput, Other,
    /// etc.) -- a definite negative, not an unknown.</summary>
    NotXInput,
    /// <summary>Current native state could not be authoritatively determined (device absent,
    /// indeterminate topology, capture failure, ...). Must never be treated as either
    /// <see cref="XInput"/> or <see cref="NotXInput"/>.</summary>
    Uncertain
}

/// <summary>
/// Read-only capability the Center M routing-retirement logic needs from native controller-mode
/// state -- deliberately narrow so that code stays decoupled from the concrete MSI Claw
/// implementation. Never sends any controller-mode command; observation only. The concrete
/// implementation wraps the SAME already-owned native-state manager instance the routing
/// composition uses for the real NativeMode stage -- it must never construct a second one.
/// </summary>
internal interface ICenterMNativeModeProbe
{
    /// <summary>Captures the current stable native-mode state once. Callers that need to wait for a
    /// transition (e.g. after requesting a minimize) are responsible for their own bounded polling
    /// loop -- this method never blocks beyond whatever settling the concrete implementation's own
    /// stable-capture logic already performs for a single read.</summary>
    Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken);
}
