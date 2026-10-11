using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Full1902ControllerBadgeEvaluatorTests
{
    [Fact]
    public void Disabled_authority_shows_live_xbox360_presentation() =>
        Assert.Equal(FrontendControllerBadgeState.Xbox360Active, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: true,
            presentation: AddonPresentationKind.Xbox360,
            presentationLive: true));

    [Fact]
    public void Disabled_authority_shows_live_steamdeck_presentation() =>
        Assert.Equal(FrontendControllerBadgeState.SteamDeckActive, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: true,
            presentation: AddonPresentationKind.SteamDeck,
            presentationLive: true));

    [Fact]
    public void Disabled_startup_pending_is_initializing_not_native_or_active() =>
        Assert.Equal(FrontendControllerBadgeState.Initializing, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            startupPending: true));

    [Fact]
    public void Pending_initial_acquisition_is_initializing() =>
        Assert.Equal(FrontendControllerBadgeState.Initializing, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            initialAcquisitionPending: true));

    [Fact]
    public void Physical_loss_with_owned_recovery_is_reconnecting() =>
        Assert.Equal(FrontendControllerBadgeState.Reconnecting, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: false,
            recoveryPending: true,
            presentation: AddonPresentationKind.Xbox360,
            presentationLive: false));

    [Fact]
    public void Physical_loss_without_recovery_is_needs_attention() =>
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: false,
            presentation: AddonPresentationKind.Xbox360,
            presentationLive: true));

    [Fact]
    public void Missing_or_non_live_presentation_never_shows_active() =>
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: true,
            presentation: AddonPresentationKind.SteamDeck,
            presentationLive: false));

    [Fact]
    public void Missing_active_presentation_needs_attention() =>
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: true,
            presentation: null,
            presentationLive: false));

    [Fact]
    public void Enabled_center_m_is_native_authority() =>
        Assert.Equal(FrontendControllerBadgeState.MsiNative, Evaluate(
            centerM: FrontendCenterMStartupState.Enabled));

    [Fact]
    public void Partial_center_m_authority_needs_attention() =>
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: FrontendCenterMStartupState.Partial));

    [Fact]
    public void Unknown_center_m_authority_is_unavailable() =>
        Assert.Equal(FrontendControllerBadgeState.Unavailable, Evaluate(centerM: null));

    [Fact]
    public void Unsafe_recovery_overrides_initializing_and_active_states()
    {
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            recoverySafe: false,
            centerM: FrontendCenterMStartupState.Disabled,
            startupPending: true));
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            recoverySafe: false,
            centerM: FrontendCenterMStartupState.Disabled,
            physicalInputRunning: true,
            presentation: AddonPresentationKind.Xbox360,
            presentationLive: true));
    }

    [Fact]
    public void Blocked_recovery_release_and_suspend_never_show_active()
    {
        var disabled = FrontendCenterMStartupState.Disabled;
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: disabled, recoveryBlocked: true));
        Assert.Equal(FrontendControllerBadgeState.NeedsAttention, Evaluate(
            centerM: disabled, releaseStarted: true));
        Assert.Equal(FrontendControllerBadgeState.Unavailable, Evaluate(
            centerM: disabled, suspendPaused: true));
        Assert.Equal(FrontendControllerBadgeState.Unavailable, Evaluate(
            centerM: disabled, reconcilePending: true));
    }

    private static FrontendControllerBadgeState Evaluate(
        bool recoverySafe = true,
        FrontendCenterMStartupState? centerM = FrontendCenterMStartupState.Disabled,
        bool startupPending = false,
        bool initialAcquisitionPending = false,
        bool physicalInputRunning = true,
        AddonPresentationKind? presentation = AddonPresentationKind.Xbox360,
        bool presentationLive = true,
        bool suspendPaused = false,
        bool reconcilePending = false,
        bool recoveryPending = false,
        bool recoveryBlocked = false,
        bool releaseStarted = false) =>
        Full1902ControllerBadgeEvaluator.Evaluate(
            centerM,
            recoverySafe,
            startupPending,
            initialAcquisitionPending,
            physicalInputRunning,
            presentation,
            presentationLive,
            suspendPaused,
            reconcilePending,
            recoveryPending,
            recoveryBlocked,
            releaseStarted);
}
