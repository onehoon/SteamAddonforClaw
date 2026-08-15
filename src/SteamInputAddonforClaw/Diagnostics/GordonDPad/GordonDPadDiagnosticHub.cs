namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>
/// A single, process-wide fan-out point for every stage of the Gordon D-pad diagnostic chain: physical
/// (DirectInput), canonical/mapped, native VIIPER (ABI-decoded, Gordon report, and any future stage the
/// VIIPER side adds), and Windows HID. Existing emission points (<see cref="ControllerStateDiagnostics"/>,
/// <see cref="VirtualOutput.Viiper.CanonicalSteamControllerInputPublisher"/>,
/// <see cref="VirtualOutput.Viiper.CanonicalViiperDiagnosticLog"/>) publish here unconditionally and
/// cheaply (a null-check and a delegate invoke) alongside their existing <see cref="AppLog"/> calls; a
/// <see cref="GordonDPadDiagnosticSession"/> subscribes only while an active capture needs the data, so
/// there is no cost at all when no diagnostic session is running.
/// </summary>
/// <remarks>
/// Deliberately generic on message *content*: publishers pass an already-formatted, stage-tagged line
/// (e.g. <c>"Stage=Physical Up=0 Right=0 Left=0 Down=1 Mask=0x08"</c>) rather than a stage-specific typed
/// event, so a new VIIPER-side diagnostic stage (e.g. <c>Stage=USBIPResponse</c>) is captured automatically
/// the moment the native side starts emitting it, without any change here or in
/// <see cref="GordonDPadDiagnosticSession"/>.
/// </remarks>
internal static class GordonDPadDiagnosticHub
{
    /// <summary>
    /// Fires for every published stage line, regardless of subscriber count. A subscriber that throws is
    /// caught and ignored here so one broken sink can never affect another subscriber, nor the publisher
    /// (which may be on a realtime input or native-callback thread).
    /// </summary>
    internal static event Action<string>? LineObserved;

    /// <summary>True while at least one diagnostic session is subscribed -- used by the "Native Trace"
    /// status indicator, since the hub itself (not the VIIPER side) is what the Addon can observe.</summary>
    internal static bool HasSubscribers => LineObserved is not null;

    internal static void Publish(string line)
    {
        var handlers = LineObserved;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action<string>)handler)(line); }
            catch { /* A misbehaving subscriber must never affect the publisher or other subscribers. */ }
        }
    }

    /// <summary>Test-only: clears all subscribers so tests don't leak subscriptions into each other via
    /// this process-wide static event.</summary>
    internal static void ResetForTests() => LineObserved = null;
}
