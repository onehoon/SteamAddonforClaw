using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// Pure, UI-framework-free mapping from the frontend contract snapshot to Status page display
/// strings. Kept separate from StatusPage code-behind so the mapping rules can be unit tested
/// without a UI thread, and so StatusPage.Render has no reason to reimplement any of this policy
/// inline.
/// </summary>
internal static class StatusPresentation
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

    internal static string FormatDeviceCompatibility(FrontendHardwareStatus status) => status switch
    {
        FrontendHardwareStatus.Supported => "Supported",
        FrontendHardwareStatus.Unsupported => "Unsupported",
        _ => "Compatibility unknown"
    };

    internal static string FormatSteamGame(FrontendSteamSnapshot steam) => steam.Source switch
    {
        FrontendSteamSource.BigPicture => "Big Picture Mode",
        FrontendSteamSource.Actual when steam.AppId != 0 => "Running",
        _ => "Not Running"
    };

    /// <summary>
    /// True when the addon's derived operational status is a safety-boundary condition that must
    /// stay visible as a warning InfoBar on supported hardware.
    /// </summary>
    internal static bool IsWarning(FrontendStatusSnapshot snapshot)
    {
        if (snapshot.Hardware.Status == FrontendHardwareStatus.Unsupported)
            return false;

        return !snapshot.RecoverySafe
        || snapshot.Hardware.Status == FrontendHardwareStatus.Indeterminate
        || snapshot.AddonStatus is FrontendAddonOperationalStatus.SetupRequired
            or FrontendAddonOperationalStatus.RecoveryRequired
            or FrontendAddonOperationalStatus.Unsupported
            or FrontendAddonOperationalStatus.Indeterminate;
    }
}
