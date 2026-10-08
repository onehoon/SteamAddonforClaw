using SteamInputAddonforClaw.Processes;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// POC normal-mapping replacement action: opens Steam Big Picture via the steam:// URI (which also
/// starts Steam if it is not already running). Deliberately minimal per the work order -- no Steam
/// install-path discovery, no process lookup/polling, no BPM/HWND polling, no retries.
/// </summary>
internal static class Oem1BigPictureLauncher
{
    internal static void Launch(UserProcessLauncher? userProcessLauncher = null)
    {
        if (!(userProcessLauncher ?? UserProcessLauncher.Shared).LaunchUri("steam://open/bigpicture"))
            throw new InvalidOperationException("Steam Big Picture could not be activated.");
    }
}
