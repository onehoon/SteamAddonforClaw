using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// Pure, UI-framework-free formatting for the Device page identity/support summary. Kept separate
/// from <c>DevicePage</c> code-behind so the mapping rules can be unit tested without a UI thread,
/// and so <c>DevicePage.RenderDeviceSummary</c> has no reason to reimplement this policy inline.
/// </summary>
internal static class DeviceSummaryPresentation
{
    internal static string FormatManufacturerForDisplay(string? rawManufacturer)
    {
        var manufacturer = rawManufacturer?.Trim() ?? string.Empty;
        return manufacturer switch
        {
            _ when manufacturer.Equals("MICRO-STAR INTERNATIONAL", StringComparison.OrdinalIgnoreCase)
                || manufacturer.Equals("MICRO-STAR INTERNATIONAL CO., LTD", StringComparison.OrdinalIgnoreCase)
                || manufacturer.Equals("MICRO-STAR INTERNATIONAL CO., LTD.", StringComparison.OrdinalIgnoreCase)
                || manufacturer.Equals("MICRO-STAR INTERNATIONAL CO.,LTD", StringComparison.OrdinalIgnoreCase) => "MSI",
            _ => manufacturer
        };
    }

    internal static string FormatControllerBadge(FrontendHardwareStatus hardwareStatus, FrontendControllerBadgeState badge)
    {
        if (hardwareStatus == FrontendHardwareStatus.Unsupported)
            return "Unsupported";
        if (hardwareStatus == FrontendHardwareStatus.Indeterminate)
            return "Compatibility unknown";

        return badge switch
        {
            FrontendControllerBadgeState.MsiNative => "MSI Native",
            FrontendControllerBadgeState.Xbox360Active => "Xbox 360 · Active",
            FrontendControllerBadgeState.SteamDeckActive => "Steam Deck · Active",
            FrontendControllerBadgeState.Initializing => "Initializing…",
            FrontendControllerBadgeState.Reconnecting => "Reconnecting…",
            FrontendControllerBadgeState.NeedsAttention => "Needs attention",
            _ => "Unavailable"
        };
    }

    internal static FrontendStatusSnapshot WithUnavailableControllerBadge(FrontendStatusSnapshot snapshot) =>
        snapshot with { ControllerBadge = FrontendControllerBadgeState.Unavailable };
}
