using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal static class SteamOutputComposition
{
    internal const string Target = "SteamDeck";
    internal const string VendorId = "0x28DE";
    internal const string ProductId = "0x1205";

    internal static Type ActiveStageType => typeof(CanonicalSteamDeckOutputStage);

    internal static void LogTargetSelected() => AppLog.Info(
        "SteamOutput",
        "Virtual output target selected.",
        ("Target", Target),
        ("VID", VendorId),
        ("PID", ProductId));
}
