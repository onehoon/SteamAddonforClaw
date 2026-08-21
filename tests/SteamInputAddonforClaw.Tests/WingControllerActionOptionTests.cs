using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingControllerActionOptionTests
{
    [Theory]
    [InlineData(WingAction.KeyboardHotkey, true, false)]
    [InlineData(WingAction.LaunchApplication, false, true)]
    [InlineData(WingAction.None, false, false)]
    [InlineData(WingAction.SteamButton, false, false)]
    public void Action_option_projection_selects_the_expected_editor(WingAction action, bool hotkeyVisible, bool launchVisible)
    {
        var selected = new ControllerPage.WingActionOption(action, action.ToString());

        var projected = ControllerPage.SelectedWingAction(selected);

        Assert.Equal(action, projected);
        Assert.Equal(hotkeyVisible, projected == WingAction.KeyboardHotkey);
        Assert.Equal(launchVisible, projected == WingAction.LaunchApplication);
    }
}
