using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full1902 0903 cleanup section 9.1: the conservative positive-Ready boundary for the
/// Center M Disabled / Addon-authority controller path.</summary>
public sealed class Full1902AddonStatusEvaluatorTests
{
    [Theory]
    [InlineData(false)] // Xbox360
    [InlineData(true)]  // SteamDeck
    public void Healthy_disabled_authority_with_running_source_and_active_presentation_is_ready(bool steamDeck)
    {
        var presentation = steamDeck ? AddonPresentationKind.SteamDeck : AddonPresentationKind.Xbox360;
        var result = Full1902AddonStatusEvaluator.Evaluate(
            FrontendCenterMStartupState.Disabled, disabledControllerStartupPending: false,
            physicalInputSourceRunning: true, activePresentation: presentation);

        Assert.NotNull(result);
        Assert.Equal(AddonOperationalStatus.Ready, result.Status);
        Assert.Contains(presentation.ToString(), result.Reason);
    }

    [Fact]
    public void Disabled_startup_still_pending_is_not_ready()
        => Assert.Null(Full1902AddonStatusEvaluator.Evaluate(
            FrontendCenterMStartupState.Disabled, disabledControllerStartupPending: true,
            physicalInputSourceRunning: true, activePresentation: AddonPresentationKind.Xbox360));

    [Fact]
    public void Physical_input_source_stopped_is_not_ready()
        => Assert.Null(Full1902AddonStatusEvaluator.Evaluate(
            FrontendCenterMStartupState.Disabled, disabledControllerStartupPending: false,
            physicalInputSourceRunning: false, activePresentation: AddonPresentationKind.SteamDeck));

    [Fact]
    public void No_active_presentation_is_not_ready()
        => Assert.Null(Full1902AddonStatusEvaluator.Evaluate(
            FrontendCenterMStartupState.Disabled, disabledControllerStartupPending: false,
            physicalInputSourceRunning: true, activePresentation: null));

    [Theory]
    [InlineData(FrontendCenterMStartupState.Enabled)]
    [InlineData(FrontendCenterMStartupState.Partial)]
    [InlineData(FrontendCenterMStartupState.Unavailable)]
    [InlineData(null)]
    public void Any_non_disabled_authority_state_returns_null_so_the_legacy_status_stands(FrontendCenterMStartupState? state)
        => Assert.Null(Full1902AddonStatusEvaluator.Evaluate(
            state, disabledControllerStartupPending: false,
            physicalInputSourceRunning: true, activePresentation: AddonPresentationKind.Xbox360));
}
