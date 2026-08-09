using Velopack.Locators;

namespace SteamInputAddonforClaw.Install;

internal static class VelopackAppPaths
{
    internal static string RecoveryJournalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamInputAddonforClaw", "recovery.json");
    internal static string HidHideProvisioningReceiptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamInputAddonforClaw", "provisioning", "hidhide.json");
    private const string ExecutableName = "SteamInputAddonforClaw.exe";

    public static string RootAppDirectory => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw")
        : VelopackLocator.Current.RootAppDir;

    public static string SettingsPath => Path.Combine(RootAppDirectory, "settings.json");

    public static string StableExecutablePath => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? string.Empty
        : Path.Combine(RootAppDirectory, ExecutableName);
}
