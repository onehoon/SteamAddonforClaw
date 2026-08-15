using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Routes the canonical VIIPER native log callback into the Addon's own log, narrowly scoped
/// to the D-pad runtime boundary diagnostics (Debug/Warn messages prefixed "VIIPER.DPad").
/// VIIPER's own routine Info-level logging is intentionally not forwarded here, to avoid
/// duplicating/flooding the Addon's product log with a second logging source.
/// </summary>
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
                    break;
                case ViiperLogLevel.Warn:
                    AppLog.Warn(Category, text);
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
}
