namespace SteamInputAddonforClaw.Lifecycle;

internal enum ApplicationCloseAction
{
    HideWindow,
    ExitApplication
}

internal static class ApplicationLifecyclePolicy
{
    public static bool ShouldShowMainWindow(IEnumerable<string> arguments) =>
        !arguments.Contains("--background", StringComparer.OrdinalIgnoreCase);

    public static ApplicationCloseAction OnWindowClose(bool explicitExit) =>
        explicitExit ? ApplicationCloseAction.ExitApplication : ApplicationCloseAction.HideWindow;
}
