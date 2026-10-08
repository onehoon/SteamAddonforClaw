using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw.CenterM;

internal static class FrontButtonXboxAppLauncher
{
    internal const string XboxAppAumid = XboxGamingHomeAppIdentity.Aumid;

    internal static void Launch(UserProcessLauncher? userProcessLauncher = null)
    {
        if (!(userProcessLauncher ?? UserProcessLauncher.Shared).LaunchXboxApp())
            throw new InvalidOperationException("Xbox app activation could not be delegated to the interactive shell.");

        AppLog.Info(
            "FrontButtons.XboxApp",
            "Xbox app activation delegated to the interactive shell.",
            ("Aumid", XboxAppAumid));
    }
}
