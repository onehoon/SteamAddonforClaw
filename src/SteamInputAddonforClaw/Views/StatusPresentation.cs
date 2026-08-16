using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// Pure, UI-framework-free mapping from runtime state to Status page display strings. Kept
/// separate from StatusPage code-behind so the mapping rules can be unit tested without a
/// UI thread.
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

    internal static string FormatDeviceCompatibility(HardwareCompatibilityStatus status) => status switch
    {
        HardwareCompatibilityStatus.Supported => "Supported",
        HardwareCompatibilityStatus.Unsupported => "Unsupported",
        _ => "Compatibility unknown"
    };

    internal static string FormatSteamGame(SteamStatusSnapshot steam) => steam.Source switch
    {
        SteamSessionSource.BigPicture => "Big Picture Mode",
        SteamSessionSource.Actual when steam.RunningAppId != 0 => "Running",
        _ => "Not Running"
    };

    /// <summary>
    /// True only when recovery/ownership/compatibility state is trustworthy enough to report an
    /// actual controller status. RoutingEligibilityPolicy checks these same conditions first and
    /// fails routing safe on them, so Status must not guess a controller state past this point.
    /// </summary>
    internal static bool IsControllerStateTrusted(FrontendStatusSnapshot snapshot) =>
        snapshot.RecoverySafe
        && !snapshot.AddonOwnedOutputIdentityUncertain
        && snapshot.Hardware.Status == nameof(HardwareCompatibilityStatus.Supported)
        && snapshot.ControllerEnvironmentStatus == nameof(ControllerEnvironmentCompatibilityStatus.Supported)
        && snapshot.Routing.EligibilityReason is not (nameof(RoutingDecisionReason.DeviceCompatibilityIndeterminate) or nameof(RoutingDecisionReason.ControllerEnvironmentIndeterminate));

    /// <summary>
    /// Reports what controller path is actually active, derived from RoutingRuntimeStatusSnapshot
    /// (actual pipeline session/plan state), never from RoutingDecisionKind.Eligible or
    /// AddonOperationalStatus.Ready alone -- those only mean routing is *eligible* to start, not
    /// that it has actually entered and the Steam output stage is live.
    /// </summary>
    internal static string FormatControllerStatus(
        bool stateTrusted,
        RoutingRuntimeStatusSnapshot routingStatus,
        bool nativeXInputVerified)
    {
        if (!stateTrusted || !routingStatus.Available) return "Unavailable";

        if (routingStatus.OperationalState == RoutingOperationalState.OverrideActive
            && routingStatus.SteamOutputActive
            && routingStatus.NativeDirectInputActive)
            return "Steam Controller (DInput)";

        if (routingStatus.OperationalState == RoutingOperationalState.OverrideActive)
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
        if (snapshot.AddonOwnedOutputIdentityUncertain)
            return true;

        if (snapshot.Hardware.Status == nameof(HardwareCompatibilityStatus.Unsupported))
            return false;

        return !snapshot.RecoverySafe
        || snapshot.Routing.EligibilityReason is nameof(RoutingDecisionReason.DeviceCompatibilityIndeterminate) or nameof(RoutingDecisionReason.ControllerEnvironmentIndeterminate)
        || snapshot.AddonStatus is nameof(AddonOperationalStatus.SetupRequired) or nameof(AddonOperationalStatus.RecoveryRequired) or nameof(AddonOperationalStatus.Unsupported);
    }
}
