namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendWindowsTweakId
{
    MemoryIntegrity = 0,
    VirtualMachinePlatform = 1,
    InstantContextMenus = 2,
    ClassicContextMenu = 3,
    DisablePasswordOnWake = 4,
    // Value 5 is retired and must not be assigned to another tweak.
    DisableEdgeBackground = 6,
    DisableStartupAppDelay = 7,
}

public enum FrontendWindowsTweakState
{
    Enabled,
    Disabled,
    EnablePendingRestart,
    DisablePendingRestart,
    ExplorerRestartPending,
    Unsupported,
    Managed,
    Unknown,
    Mixed,
}

public enum FrontendWindowsTweakApplicability
{
    AllSupportedWindows,
    A2vmOnly,
}

public sealed record FrontendWindowsTweakDefinition(
    FrontendWindowsTweakId Id,
    string Title,
    string Description,
    FrontendWindowsTweakApplicability Applicability);

public static class FrontendWindowsTweakCatalog
{
    public static IReadOnlyList<FrontendWindowsTweakDefinition> All { get; } =
    [
        new(FrontendWindowsTweakId.MemoryIntegrity, "Memory Integrity", "Configure Windows kernel memory protection.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.VirtualMachinePlatform, "Virtual Machine Platform", "Enable or disable the Windows virtualization platform feature.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.InstantContextMenus, "Faster Menus", "Disable menu animation and reduce submenu delay to 10 ms.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.ClassicContextMenu, "Classic Context Menu", "Use the classic Windows 11 context menu for this user.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.DisablePasswordOnWake, "Disable Password on Wake", "Return from sleep without requiring sign-in.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.DisableEdgeBackground, "Disable Edge Background", "Disable Edge Startup Boost and background mode; existing processes may continue until closed.", FrontendWindowsTweakApplicability.AllSupportedWindows),
        new(FrontendWindowsTweakId.DisableStartupAppDelay, "Disable Startup App Delay", "Remove configured Explorer startup-app launch delays; actual startup gains may vary.", FrontendWindowsTweakApplicability.AllSupportedWindows),
    ];
}

public sealed record FrontendWindowsTweakSnapshot(
    FrontendWindowsTweakId Id,
    FrontendWindowsTweakState State,
    bool? ConfiguredEnabled,
    bool? EffectiveEnabled,
    bool CanChange,
    string? Detail);

public sealed record FrontendWindowsTweaksSnapshot(
    IReadOnlyList<FrontendWindowsTweakSnapshot> Tweaks,
    DateOnly? WarningAcknowledgedLocalDate = null)
{
    public static readonly FrontendWindowsTweaksSnapshot Unavailable = new(
    [
        UnavailableTweak(FrontendWindowsTweakId.MemoryIntegrity),
        UnavailableTweak(FrontendWindowsTweakId.VirtualMachinePlatform),
        UnavailableTweak(FrontendWindowsTweakId.InstantContextMenus),
        UnavailableTweak(FrontendWindowsTweakId.ClassicContextMenu),
        UnavailableTweak(FrontendWindowsTweakId.DisablePasswordOnWake),
        UnavailableTweak(FrontendWindowsTweakId.DisableEdgeBackground),
        UnavailableTweak(FrontendWindowsTweakId.DisableStartupAppDelay),
    ]);

    private static FrontendWindowsTweakSnapshot UnavailableTweak(FrontendWindowsTweakId id) =>
        new(id, FrontendWindowsTweakState.Unknown, null, null, false, "Windows state could not be verified.");
}

public sealed record FrontendWindowsTweakMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendWindowsTweakSnapshot UpdatedTweak);

public sealed record SetWindowsTweakRequest(FrontendWindowsTweakId Id, bool Enabled);
public sealed record AcknowledgeWindowsTweaksWarningRequest(DateOnly LocalDate);
