using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal static class SteamOutputComposition
{
    internal const string Target = "SteamDeck";
    internal static string VendorId => $"0x{SteamDeckVirtualDeviceIdentityPolicy.VendorId:X4}";
    internal static string ProductId => $"0x{SteamDeckVirtualDeviceIdentityPolicy.ProductId:X4}";

    internal static void LogTargetSelected() => AppLog.Info(
        "SteamOutput",
        "Virtual output target selected.",
        ("Target", Target),
        ("VID", VendorId),
        ("PID", ProductId));
}
