using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Status;

/// <summary>Projects existing Full1902 authority and owner facts into a read-only Device-page badge.</summary>
internal static class Full1902ControllerBadgeEvaluator
{
    internal static FrontendControllerBadgeState Evaluate(
        FrontendCenterMStartupState? centerMStartupState,
        bool recoverySafe,
        bool disabledControllerStartupPending,
        bool initialControllerAcquisitionPending,
        bool physicalInputSourceRunning,
        AddonPresentationKind? activePresentation,
        bool activePresentationLive,
        bool presentationSuspendPaused,
        bool presentationReconcilePending,
        bool ownedControllerRecoveryPending,
        bool ownedControllerRecoveryBlocked,
        bool controllerOwnershipReleaseStarted)
    {
        if (ownedControllerRecoveryBlocked || controllerOwnershipReleaseStarted
            || centerMStartupState == FrontendCenterMStartupState.Partial)
            return FrontendControllerBadgeState.NeedsAttention;

        if (centerMStartupState == FrontendCenterMStartupState.Enabled)
            return recoverySafe
                ? FrontendControllerBadgeState.MsiNative
                : FrontendControllerBadgeState.NeedsAttention;

        if (centerMStartupState != FrontendCenterMStartupState.Disabled)
            return FrontendControllerBadgeState.Unavailable;

        if (disabledControllerStartupPending || initialControllerAcquisitionPending)
            return FrontendControllerBadgeState.Initializing;

        if (presentationSuspendPaused)
            return FrontendControllerBadgeState.Unavailable;

        if (ownedControllerRecoveryPending)
            return physicalInputSourceRunning
                ? FrontendControllerBadgeState.Unavailable
                : FrontendControllerBadgeState.Reconnecting;

        if (presentationReconcilePending)
            return FrontendControllerBadgeState.Unavailable;

        if (!physicalInputSourceRunning)
            return FrontendControllerBadgeState.NeedsAttention;

        if (!activePresentationLive)
            return FrontendControllerBadgeState.NeedsAttention;

        return activePresentation switch
        {
            AddonPresentationKind.Xbox360 => FrontendControllerBadgeState.Xbox360Active,
            AddonPresentationKind.SteamDeck => FrontendControllerBadgeState.SteamDeckActive,
            _ => FrontendControllerBadgeState.NeedsAttention
        };
    }
}
