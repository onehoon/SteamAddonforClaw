using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal static class FrontButtonXboxAppLauncher
{
    internal const string XboxAppAumid = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

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
