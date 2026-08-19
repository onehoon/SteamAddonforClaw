using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public class QamHostGamepadUiTargetSelectorTests
{
    private static CdpTarget Page(string title, string url, string? ws = "ws://127.0.0.1:8080/devtools/page/1") =>
        new("id-" + title, "page", title, url, ws);

    [Fact]
    public void SelectsTheGamepadUiTargetAmongOrdinaryTargets()
    {
        var targets = new[]
        {
            Page("Steam", "https://store.steampowered.com/"),
            Page("SP Overlay: GamepadUI", "https://steamloopback.host/routes/gamepadui/index.html"),
            Page("Friends", "https://steamloopback.host/friends"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.NotNull(selected);
        Assert.Contains("gamepadui", selected!.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturnsNullWhenNoGamepadUiTargetIsPresent()
    {
        var targets = new[]
        {
            Page("Steam", "https://store.steampowered.com/"),
            Page("Friends", "https://steamloopback.host/friends"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void DoesNotSelectAnArbitraryUnrelatedLoopbackPage()
    {
        var targets = new[]
        {
            Page("Steam Big Picture", "https://steamloopback.host/index.html"),
            Page("Notifications", "https://steamloopback.host/notifications"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void IgnoresTargetsWithoutAWebSocketDebuggerUrl()
    {
        var targets = new[]
        {
            Page("SP Overlay: GamepadUI", "https://steamloopback.host/routes/gamepadui/index.html", ws: null),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void ReturnsNullWhenMultipleAmbiguousGamepadUiCandidatesExist()
    {
        var targets = new[]
        {
            Page("SP Overlay: GamepadUI", "https://steamloopback.host/routes/gamepadui/index.html"),
            Page("SP Overlay: GamepadUI (2)", "https://steamloopback.host/routes/gamepadui/index.html?x=2"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }
}
