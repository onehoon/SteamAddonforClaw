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
    internal static string CefMarkerOwnershipDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteamInputAddonforClaw", "ownership");
    internal static string CefMarkerOwnershipPath => Path.Combine(CefMarkerOwnershipDirectory, "steam-cef-marker.json");
    private const string ExecutableName = "SteamInputAddonforClaw.exe";

    public static string RootAppDirectory => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw")
        : VelopackLocator.Current.RootAppDir;

    public static string StableExecutablePath => string.IsNullOrWhiteSpace(VelopackLocator.Current.RootAppDir)
        ? string.Empty
        : Path.Combine(RootAppDirectory, ExecutableName);
}
