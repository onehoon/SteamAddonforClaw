using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.GordonDPad;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Routes the canonical VIIPER native log callback into the Addon's own log, narrowly scoped
/// to the D-pad runtime boundary diagnostics (Debug/Warn messages prefixed "VIIPER.DPad").
/// VIIPER's own routine Info-level logging is intentionally not forwarded here, to avoid
/// duplicating/flooding the Addon's product log with a second logging source.
/// </summary>
/// <remarks>
/// Every forwarded "VIIPER.DPad" message is also published to <see cref="GordonDPadDiagnosticHub"/>
/// (stripped of the "VIIPER.DPad " prefix, leaving the "Stage=..." line intact) so an active
/// <see cref="GordonDPadDiagnosticSession"/> capture receives it too. Deliberately not limited to
/// today's two known stages ("ABIDecoded"/"GordonReport") -- any future VIIPER-side stage using the
/// same prefix (e.g. a USB/IP or feature-command stage) is forwarded automatically without an Addon
/// code change, as long as it is logged at Debug or Warn on the native side.
/// </remarks>
internal static class CanonicalViiperDiagnosticLog
{
    private const string DiagnosticPrefix = "VIIPER.DPad";
    private const string Category = "VIIPER";

    /// <summary>
    /// A single static delegate instance: trivially rooted for the process lifetime, so it
    /// needs no separate keep-alive bookkeeping beyond what <see cref="CanonicalViiperNativeApi"/>
    /// already does for any log callback it is handed.
    /// </summary>
    internal static readonly ViiperLogCallback Callback = HandleNativeLogMessage;

    private static void HandleNativeLogMessage(ViiperLogLevel level, nint message)
    {
        // Never let a managed exception cross back into the native caller: a bad message
        // pointer, unexpected encoding, or a logging-path bug must not crash or corrupt the
        // Go runtime's C caller.
        try
        {
            var text = Marshal.PtrToStringUTF8(message);
            if (string.IsNullOrEmpty(text) || !text.StartsWith(DiagnosticPrefix, StringComparison.Ordinal))
            {
                return;
            }

            switch (level)
            {
                case ViiperLogLevel.Debug:
                    AppLog.Debug(Category, text);
                    PublishToHub(text);
                    break;
                case ViiperLogLevel.Warn:
                    AppLog.Warn(Category, text);
                    PublishToHub(text);
                    break;
                // Info/Error are not expected from this diagnostic prefix today; ignored rather
                // than promoted, per the task's "never promote to Info" requirement.
            }
        }
        catch
        {
            // Swallow: see remarks above.
        }
    }

    private static void PublishToHub(string text)
    {
        // Strip the "VIIPER.DPad " prefix so hub lines from every stage (physical, canonical, native)
        // share the same "Stage=..." shape. GordonDPadDiagnosticHub.Publish already isolates a
        // misbehaving subscriber, but this call itself must never throw back into the native-callback
        // path above, so it stays wrapped in the caller's try/catch rather than adding a second one here.
        var stripped = text.Length > DiagnosticPrefix.Length ? text[(DiagnosticPrefix.Length + 1)..] : text;
        GordonDPadDiagnosticHub.Publish(stripped);
    }
}
