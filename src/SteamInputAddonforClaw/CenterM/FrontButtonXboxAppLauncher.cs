using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw.CenterM;

internal static class FrontButtonXboxAppLauncher
{
    internal const string XboxAppAumid = XboxGamingHomeAppIdentity.Aumid;

    internal static void Launch()
    {
        // The Runtime is elevated. Delegate packaged-app activation to the interactive shell so the
        // Windows app is started across the High-to-Medium integrity boundary.
        using var shellProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{XboxAppAumid}",
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Xbox app activation could not be delegated to the interactive shell.");

        AppLog.Info(
            "FrontButtons.XboxApp",
            "Xbox app activation delegated to the interactive shell.",
            ("Aumid", XboxAppAumid));
    }
}
