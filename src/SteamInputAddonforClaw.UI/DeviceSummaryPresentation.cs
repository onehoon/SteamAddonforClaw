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
            FrontendControllerBadgeState.DualSenseEdgeActive => "DualSense Edge · Active",
            FrontendControllerBadgeState.Initializing => "Initializing…",
            FrontendControllerBadgeState.Reconnecting => "Reconnecting…",
            FrontendControllerBadgeState.NeedsAttention => "Needs attention",
            _ => "Unavailable"
        };
    }

    internal static FrontendStatusSnapshot WithUnavailableControllerBadge(FrontendStatusSnapshot snapshot) =>
        snapshot with
        {
            ControllerBadge = FrontendControllerBadgeState.Unavailable,
            StartupTask = FrontendStartupTaskSnapshot.Unknown
        };

    internal static string StartupTaskStatusResourceKey(FrontendStartupTaskSnapshot snapshot, bool repairing) =>
        repairing
            ? "Device_AutoStart_Repairing"
            : snapshot.State switch
            {
                FrontendStartupTaskState.Verified => "Device_AutoStart_Verified",
                FrontendStartupTaskState.NeedsAttention => "Device_AutoStart_NeedsAttention",
                _ => "Device_AutoStart_Unknown"
            };

    internal static string? StartupTaskIssueResourceKey(FrontendStartupTaskIssue issue) => issue switch
    {
        FrontendStartupTaskIssue.ReadFailed => "Device_AutoStart_Issue_ReadFailed",
        FrontendStartupTaskIssue.StableExecutableMissing => "Device_AutoStart_Issue_StableExecutableMissing",
        FrontendStartupTaskIssue.RegistrationFailed => "Device_AutoStart_Issue_RegistrationFailed",
        FrontendStartupTaskIssue.ReadbackUnverified => "Device_AutoStart_Issue_ReadbackUnverified",
        _ => null
    };

    internal static bool ShouldShowStartupTaskRepair(
        FrontendStartupTaskSnapshot snapshot,
        bool runtimeResponsive) =>
        snapshot.State == FrontendStartupTaskState.NeedsAttention
        || snapshot.State == FrontendStartupTaskState.Unknown && runtimeResponsive;

    internal static bool CanRepairStartupTask(
        FrontendStartupTaskSnapshot snapshot,
        bool runtimeResponsive,
        bool repairing) =>
        snapshot.State != FrontendStartupTaskState.Verified && runtimeResponsive && !repairing;
}
