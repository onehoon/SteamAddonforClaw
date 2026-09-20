using System.Diagnostics;

namespace SteamInputAddonforClaw.FseHome;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "steam://open/bigpicture",
                UseShellExecute = true,
            });
            return process is null ? 1 : 0;
        }
        catch
        {
            // Gaming Home must exit after the one bounded launch attempt. Runtime/BPM recovery
            // remains owned by the main Addon process and is intentionally not duplicated here.
            return 1;
        }
    }
}
