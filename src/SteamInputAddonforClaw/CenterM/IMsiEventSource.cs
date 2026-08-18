namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Observes MSI_Event WMI notifications (\\.\root\WMI, SELECT * FROM MSI_Event, property MSIEvt).
/// This source is purely observational: it never suppresses, consumes, or otherwise interferes
/// with Event41/Event88 -- MSI's own listeners (Launcher, Server, MainUI) receive the same
/// multicast WMI event regardless of whether this source is running.
/// </summary>
internal interface IMsiEventSource : IDisposable
{
    event Action<MsiOemEvent>? EventReceived;

    /// <summary>Starts watching. Returns false (fail-safe, does not throw) if registration fails.</summary>
    bool Start();
}
