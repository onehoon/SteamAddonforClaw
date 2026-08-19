using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Launches the configured application. Mirrors <see cref="Oem1BigPictureLauncher"/> exactly: start
/// it and forget it.
/// </summary>
/// <remarks>
/// The Addon never becomes an owner of what it starts here -- no process tracking, no toggle/kill,
/// no singleton detection, no shell scripting. <c>UseShellExecute</c> is on so the target resolves
/// the same way it would from Explorer (including shortcuts and registered handlers) and inherits
/// its own working directory rather than the Addon's.
/// </remarks>
internal static class Oem1ApplicationLauncher
{
    internal static void Launch(Oem1LaunchApplicationBinding application)
    {
        ArgumentNullException.ThrowIfNull(application);
        // An unfinished configuration is not a failure -- there is simply nothing to launch.
        if (!application.IsConfigured) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = application.ExecutablePath,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(application.Arguments))
            startInfo.Arguments = application.Arguments;

        Process.Start(startInfo);
    }
}
