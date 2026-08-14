using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;

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

    internal static string FormatSteamGame(bool steamIsActive) => steamIsActive ? "Running" : "Not Running";

    /// <summary>
    /// True only when recovery/ownership/compatibility state is trustworthy enough to report an
    /// actual controller status. RoutingEligibilityPolicy checks these same conditions first and
    /// fails routing safe on them, so Status must not guess a controller state past this point.
    /// </summary>
    internal static bool IsControllerStateTrusted(SystemStatusSnapshot snapshot) =>
        snapshot.RecoverySafe
        && !snapshot.AddonOwnedOutputIdentityUncertain
        && snapshot.HardwareCompatibility.Status == HardwareCompatibilityStatus.Supported
        && snapshot.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Supported
        && snapshot.RoutingDecision.Reason is not (RoutingDecisionReason.DeviceCompatibilityIndeterminate or RoutingDecisionReason.ControllerEnvironmentIndeterminate);

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
    internal static bool IsWarning(SystemStatusSnapshot snapshot)
    {
        if (snapshot.HardwareCompatibility.Status == HardwareCompatibilityStatus.Unsupported)
            return false;

        return !snapshot.RecoverySafe
        || snapshot.AddonOwnedOutputIdentityUncertain
        || snapshot.RoutingDecision.Reason is RoutingDecisionReason.DeviceCompatibilityIndeterminate or RoutingDecisionReason.ControllerEnvironmentIndeterminate
        || snapshot.Addon.Status is AddonOperationalStatus.SetupRequired or AddonOperationalStatus.RecoveryRequired or AddonOperationalStatus.Unsupported;
    }
}
