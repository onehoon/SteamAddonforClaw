namespace SteamInputAddonforClaw.Lifecycle;

internal static class ApplicationLifecyclePolicy
{
    public static bool ShouldLaunchFrontend(IEnumerable<string> arguments) =>
        !arguments.Contains("--background", StringComparer.OrdinalIgnoreCase);
}
