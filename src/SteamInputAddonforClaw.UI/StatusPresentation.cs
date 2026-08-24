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
    /// Mirrors the pre-frontend-contract ControllerSoftwareStatusFormatter exactly: Runtime state
    /// takes priority, falling back to Installation only when Runtime carries no positive signal.
    /// </summary>
    internal static string FormatControllerSoftwareStatus(FrontendSoftwareSnapshot item) => item.Runtime switch
    {
        FrontendSoftwareRuntimeStatus.Running => "Running",
        FrontendSoftwareRuntimeStatus.Starting => "Starting",
        FrontendSoftwareRuntimeStatus.Indeterminate => "Indeterminate",
        _ when item.Installation == FrontendSoftwareInstallationStatus.Installed => "Installed / Not running",
        _ when item.Installation == FrontendSoftwareInstallationStatus.NotInstalled => "Not installed",
        _ => "Indeterminate"
    };

    /// <summary>
    /// True only when recovery/compatibility state is trustworthy enough to report an
    /// actual controller status. RoutingEligibilityPolicy checks these same conditions first and
    /// fails routing safe on them, so Status must not guess a controller state past this point.
    /// </summary>
    internal static bool IsControllerStateTrusted(FrontendStatusSnapshot snapshot) =>
        snapshot.RecoverySafe
        && snapshot.Hardware.Status == FrontendHardwareStatus.Supported
        && snapshot.ControllerEnvironmentStatus == FrontendControllerEnvironmentStatus.Supported
        && snapshot.Routing.EligibilityReason is not (FrontendRoutingEligibilityReason.DeviceCompatibilityIndeterminate
            or FrontendRoutingEligibilityReason.ControllerEnvironmentIndeterminate
            or FrontendRoutingEligibilityReason.Indeterminate);

    /// <summary>
    /// Reports what controller path is actually active, derived from the frontend routing
    /// snapshot (actual pipeline session/plan state), never from eligibility alone -- eligibility
    /// only means routing is *eligible* to start, not that it has actually entered and the Steam
    /// output stage is live.
    /// </summary>
    internal static string FormatControllerStatus(
        bool stateTrusted,
        FrontendRoutingSnapshot routingStatus,
        bool nativeXInputVerified)
    {
        if (!stateTrusted || !routingStatus.Available) return "Unavailable";

        if (routingStatus.OperationalState == FrontendRoutingOperationalState.Indeterminate)
            return "Unavailable";

        if (routingStatus.OperationalState == FrontendRoutingOperationalState.OverrideActive
            && routingStatus.SteamOutputActive
            && routingStatus.NativeDirectInputActive)
            return "Steam Controller (DInput)";

        if (routingStatus.OperationalState == FrontendRoutingOperationalState.OverrideActive)
            // Override is engaged but the active plan doesn't prove the stock Steam output path
            // (e.g. a non-stock controller-manager plan) -- fail conservative rather than guess.
            return "Unavailable";

        return nativeXInputVerified ? "MSI Center M Native (XInput)" : "MSI Center M Native";
    }

    /// <summary>
    /// True when the addon's derived operational status is a safety-boundary condition that must
    /// stay visible as a warning InfoBar on supported hardware.
    /// </summary>
    internal static bool IsWarning(FrontendStatusSnapshot snapshot)
    {
        if (snapshot.Hardware.Status == FrontendHardwareStatus.Unsupported)
            return false;

        return !snapshot.RecoverySafe
        || snapshot.Routing.EligibilityReason is FrontendRoutingEligibilityReason.DeviceCompatibilityIndeterminate
            or FrontendRoutingEligibilityReason.ControllerEnvironmentIndeterminate
            or FrontendRoutingEligibilityReason.Indeterminate
        || snapshot.AddonStatus is FrontendAddonOperationalStatus.SetupRequired
            or FrontendAddonOperationalStatus.RecoveryRequired
            or FrontendAddonOperationalStatus.Unsupported
            or FrontendAddonOperationalStatus.Indeterminate;
    }
}
