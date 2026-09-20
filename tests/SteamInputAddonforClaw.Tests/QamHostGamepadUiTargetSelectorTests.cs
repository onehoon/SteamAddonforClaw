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
            Page("SharedJSContext", "https://steamloopback.host/routes/library/home"),
            Page("Friends", "https://steamloopback.host/friends"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.NotNull(selected);
        Assert.Equal("SharedJSContext", selected!.Title);
    }

    [Fact]
    public void SelectsTheSharedContextIndexPage()
    {
        var targets = new[]
        {
            Page("SP", "https://steamloopback.host/index.html"),
            Page("Notifications", "https://steamloopback.host/notifications"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.NotNull(selected);
        Assert.Equal("https://steamloopback.host/index.html", selected!.Url);
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
    public void DoesNotSelectALoopbackPageWithoutASharedContextTitle()
    {
        var targets = new[]
        {
            Page("Steam Big Picture Mode", "https://steamloopback.host/index.html"),
            Page("Notifications", "https://steamloopback.host/routes/notifications"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void IgnoresTargetsWithoutAWebSocketDebuggerUrl()
    {
        var targets = new[]
        {
            Page("SharedJSContext", "https://steamloopback.host/routes/library/home", ws: null),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void ReturnsNullWhenMultipleAmbiguousGamepadUiCandidatesExist()
    {
        var targets = new[]
        {
            Page("SharedJSContext", "https://steamloopback.host/routes/library/home"),
            Page("Steam", "https://steamloopback.host/routes/library/app/440"),
        };

        var selected = GamepadUiTargetSelector.SelectGamepadUiTarget(targets);

        Assert.Null(selected);
    }

    [Fact]
    public void FormatsReadOnlyTargetInventoryWithoutLoggingWebSocketUrls()
    {
        var targets = new[]
        {
            Page("SharedJSContext", "https://steamloopback.host/routes/library/home"),
            Page("NoSocket", "https://steamloopback.host/routes/notifications", ws: null),
        };

        var snapshot = CdpTargetSnapshotFormatter.Format("test", targets);

        Assert.Contains("QAM CDP target snapshot.", snapshot);
        Assert.Contains("\"Reason\":\"test\"", snapshot);
        Assert.Contains("\"Id\":\"id-SharedJSContext\"", snapshot);
        Assert.Contains("\"Type\":\"page\"", snapshot);
        Assert.Contains("\"Title\":\"NoSocket\"", snapshot);
        Assert.Contains("\"WebSocketDebuggerUrlPresent\":true", snapshot);
        Assert.Contains("\"WebSocketDebuggerUrlPresent\":false", snapshot);
        Assert.DoesNotContain("ws://127.0.0.1:8080/devtools/page/1", snapshot);
    }
}
