using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

internal static class MainUiPowerSourceVisibility
{
    internal static bool IsVisible(
        bool currentPowerSourceOnly,
        FrontendTdpPowerSource? currentPowerSource,
        FrontendTdpPowerSource rail) =>
        !currentPowerSourceOnly || currentPowerSource is null || currentPowerSource == rail;
}
