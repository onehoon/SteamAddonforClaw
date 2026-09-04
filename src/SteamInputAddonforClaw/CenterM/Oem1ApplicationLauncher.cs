using System.Diagnostics;
using System.IO;
using SteamInputAddonforClaw.Contracts.FrontButtons;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Launches the configured executable. Mirrors <see cref="Oem1BigPictureLauncher"/> exactly: start
/// it and forget it.
/// </summary>
/// <remarks>
/// The Addon never becomes an owner of what it starts here -- no process tracking, no toggle/kill,
/// no singleton detection.
///
/// Review fix (MAJOR): this action is scoped to "launch an executable", never a general shell-open.
/// <c>UseShellExecute = true</c> plus a <c>.lnk</c>/wildcard file picker would have let a saved
/// binding resolve through the shell's own file-type handler -- a script, a document, anything
/// registered to "open" -- which is exactly the scripting/arbitrary-shell-action surface this
/// feature is explicitly scoped to exclude. <c>UseShellExecute = false</c> starts the named file as
/// a process directly with no shell resolution, and the extension check below refuses anything that
/// is not literally an <c>.exe</c> before that ever happens.
/// </remarks>
internal static class Oem1ApplicationLauncher
{
    internal static void Launch(FrontButtonLaunchApplicationBinding application)
    {
        ArgumentNullException.ThrowIfNull(application);
        // An unfinished configuration is not a failure -- there is simply nothing to launch.
        if (!application.IsConfigured) return;

        if (!string.Equals(Path.GetExtension(application.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The OEM1 application action requires an .exe target.");

        var startInfo = new ProcessStartInfo
        {
            FileName = application.ExecutablePath,
            Arguments = application.Arguments ?? string.Empty,
            UseShellExecute = false
        };

        Process.Start(startInfo);
    }
}
