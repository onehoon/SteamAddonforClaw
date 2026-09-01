using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR3: the Device-page MSI Center M info bar is a pure function of the latest snapshot
/// state -- there is no sticky "restart required" flag. The reboot-bound transition either starts a
/// Windows restart (so the running session ends) or fails loudly; nothing outlives it in the UI.</summary>
public sealed class CenterMStartupPresentationTests
{
    [Theory]
    [InlineData(FrontendCenterMStartupState.Enabled)]
    [InlineData(FrontendCenterMStartupState.Disabled)]
    public void A_settled_state_shows_no_infobar(FrontendCenterMStartupState state)
        => Assert.Equal(
            ControllerPage.CenterMStartupInfoBarKind.None,
            ControllerPage.CenterMStartupPresentation.ResolveInfoBar(state));

    [Fact]
    public void Mixed_and_unreadable_states_still_surface()
    {
        Assert.Equal(ControllerPage.CenterMStartupInfoBarKind.Partial,
            ControllerPage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Partial));
        Assert.Equal(ControllerPage.CenterMStartupInfoBarKind.Unavailable,
            ControllerPage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Unavailable));
    }

    [Fact]
    public void No_restart_required_kind_exists_anymore()
        => Assert.DoesNotContain("RestartRequired", Enum.GetNames<ControllerPage.CenterMStartupInfoBarKind>());
}
