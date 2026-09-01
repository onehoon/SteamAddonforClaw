using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR #430 review: a successful MSI Center M startup mutation followed by a normal
/// authoritative refresh (StateInvalidated event, or Activate() on tab re-entry) must keep showing
/// "Restart Windows to apply this change." -- the old Center M session is deliberately still running,
/// so the reboot instruction is the only cue that the new state is not active yet.</summary>
public sealed class CenterMStartupPresentationTests
{
    [Theory]
    [InlineData(FrontendCenterMStartupState.Enabled)]
    [InlineData(FrontendCenterMStartupState.Disabled)]
    [InlineData(FrontendCenterMStartupState.Partial)]
    [InlineData(FrontendCenterMStartupState.Unavailable)]
    public void Restart_required_flag_wins_over_every_snapshot_state(FrontendCenterMStartupState state)
        => Assert.Equal(
            DevicePage.CenterMStartupInfoBarKind.RestartRequired,
            DevicePage.CenterMStartupPresentation.ResolveInfoBar(state, restartRequired: true));

    [Fact]
    public void Without_the_flag_a_settled_state_shows_no_infobar()
    {
        Assert.Equal(DevicePage.CenterMStartupInfoBarKind.None,
            DevicePage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Enabled, restartRequired: false));
        Assert.Equal(DevicePage.CenterMStartupInfoBarKind.None,
            DevicePage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Disabled, restartRequired: false));
    }

    [Fact]
    public void Without_the_flag_mixed_and_unreadable_states_still_surface()
    {
        Assert.Equal(DevicePage.CenterMStartupInfoBarKind.Partial,
            DevicePage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Partial, restartRequired: false));
        Assert.Equal(DevicePage.CenterMStartupInfoBarKind.Unavailable,
            DevicePage.CenterMStartupPresentation.ResolveInfoBar(FrontendCenterMStartupState.Unavailable, restartRequired: false));
    }
}
