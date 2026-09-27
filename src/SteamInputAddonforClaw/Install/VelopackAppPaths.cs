using Velopack.Locators;

namespace SteamInputAddonforClaw.Install;

internal static class VelopackAppPaths
{
    internal static string ProvisioningStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SteamInputAddonforClaw", "provisioning");
    internal static string HidHideProvisioningReceiptPath => Path.Combine(ProvisioningStateDirectory, "hidhide.json");
    internal static string UsbIpWin2ProvisioningReceiptPath => Path.Combine(ProvisioningStateDirectory, "usbip-win2.json");
    internal static string LegacyHidHideProvisioningReceiptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamInputAddonforClaw-State", "provisioning", "hidhide.json");
    internal const string StableLauncherName = "Steam Addon for Claw.exe";
    internal const string MainExecutableName = "SteamInputAddonforClaw.exe";
    internal const string UpdaterExecutableName = "Update.exe";

    public static string RootAppDirectory => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw")
        : VelopackLocator.Current.RootAppDir;

    public static string StableExecutablePath => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? string.Empty
        : ResolveStableExecutablePath(RootAppDirectory);

    internal static string CurrentExecutablePath => ResolveCurrentExecutablePath(VelopackLocator.Current.AppContentDir);

    internal static string ResolveStableExecutablePath(string? rootAppDirectory)
        => string.IsNullOrWhiteSpace(rootAppDirectory)
            ? string.Empty
            : Path.Combine(Path.GetFullPath(rootAppDirectory), StableLauncherName);

    internal static string ResolveCurrentExecutablePath(string? currentBinaryDirectory)
        => string.IsNullOrWhiteSpace(currentBinaryDirectory)
            ? string.Empty
            : Path.Combine(Path.GetFullPath(currentBinaryDirectory), MainExecutableName);

    internal static bool TryResolveCurrentExecutablePath(
        string? currentProcessPath,
        string? expectedCurrentExecutablePath,
        out string currentExecutablePath)
    {
        currentExecutablePath = expectedCurrentExecutablePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentExecutablePath) || string.IsNullOrWhiteSpace(currentProcessPath))
            return false;

        try
        {
            currentExecutablePath = Path.GetFullPath(currentExecutablePath);
            return string.Equals(
                Path.GetFullPath(currentProcessPath),
                currentExecutablePath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            currentExecutablePath = string.Empty;
            return false;
        }
    }
}
