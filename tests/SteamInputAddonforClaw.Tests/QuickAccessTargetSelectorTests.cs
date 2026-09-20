using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QuickAccessTargetSelectorTests
{
    private static CdpTarget Page(string title, string url, string? ws = "ws://127.0.0.1:8080/devtools/page/1") =>
        new("id-" + title, "page", title, url, ws);

    [Fact]
    public void Selects_both_bpm_and_game_quick_access_targets()
    {
        var targets = new[]
        {
            Page("QuickAccess_uid165", "about:blank?browserviewpopup=1&parentpopup=165"),
            Page("MainMenu_uid165", "about:blank?browserviewpopup=1&parentpopup=165"),
            Page("QuickAccess_uid159", "about:blank?browserviewpopup=1&parentpopup=159"),
        };

        var selected = QuickAccessTargetSelector.SelectQuickAccessTargets(targets);

        Assert.Equal(new[] { "QuickAccess_uid159", "QuickAccess_uid165" }, selected.Select(target => target.Title));
    }

    [Fact]
    public void Requires_page_target_numeric_uid_parent_popup_and_websocket_url()
    {
        var targets = new[]
        {
            Page("QuickAccess_uid", "about:blank?browserviewpopup=1&parentpopup=159"),
            Page("QuickAccess_uidabc", "about:blank?browserviewpopup=1&parentpopup=159"),
            Page("QuickAccess_uid159", "about:blank?browserviewpopup=1", ws: null),
            Page("QuickAccess_uid165", "about:blank?browserviewpopup=1"),
            new CdpTarget("worker", "worker", "QuickAccess_uid166", "about:blank?parentpopup=166", "ws://worker"),
        };

        Assert.Empty(QuickAccessTargetSelector.SelectQuickAccessTargets(targets));
    }

    [Fact]
    public void Geometry_expression_only_reads_the_target_dom()
    {
        var expression = QuickAccessGeometryDiagnostic.CreateExpression(
            new QamGeometryClassNames("panel-class", "tab-group-class"));

        Assert.Contains("getBoundingClientRect", expression);
        Assert.Contains("getComputedStyle", expression);
        Assert.Contains("getElementsByClassName", expression);
        Assert.Contains("parentElement", expression);
        Assert.Contains("depth < 6", expression);
        Assert.DoesNotContain("MutationObserver", expression);
        Assert.DoesNotContain("setInterval", expression);
        Assert.DoesNotContain("setTimeout", expression);
        Assert.DoesNotContain("classList", expression);
        Assert.DoesNotContain("appendChild", expression);
        Assert.DoesNotContain("removeChild", expression);
        Assert.DoesNotContain("setProperty", expression);
    }

    [Fact]
    public void Selects_only_bounded_qam_host_targets_for_outer_geometry_diagnostics()
    {
        var targets = new[]
        {
            Page("QuickAccess_uid165", "about:blank?browserviewpopup=1&parentpopup=165"),
            Page("notificationtoasts_uid165", "about:blank?browserviewpopup=1&parentpopup=165"),
            Page("MainMenu_uid165", "about:blank?browserviewpopup=1&parentpopup=165"),
            Page("Menu", "about:blank?openerid=165"),
            Page("Steam Big Picture Mode", "about:blank?pid=0"),
            Page("SharedJSContext", "https://steamloopback.host/routes/library/home"),
            Page("Menu", "about:blank?openerid=165", ws: null),
        };

        var selected = QamHostTargetSelector.SelectQamHostTargets(targets);

        Assert.Equal(new[] { "Menu", "Steam Big Picture Mode", "SharedJSContext", "MainMenu_uid165" }, selected.Select(target => target.Title));
    }

    [Fact]
    public void Qam_host_geometry_expression_is_read_only_and_captures_outer_layout_metrics()
    {
        var expression = QamHostGeometryDiagnostic.CreateExpression(
            new QamGeometryClassNames("panel-class", "tab-group-class", "view-placeholder-class"));

        Assert.Contains("view-placeholder-class", expression);
        Assert.Contains("getBoundingClientRect", expression);
        Assert.Contains("getComputedStyle", expression);
        Assert.Contains("parentElement", expression);
        Assert.Contains("scrollWidth", expression);
        Assert.Contains("clientWidth", expression);
        Assert.Contains("transformOrigin", expression);
        Assert.Contains("floatingSidePanelWidth", expression);
        Assert.Contains("--vrgamepadui-floating-side-panel-width", expression);
        Assert.DoesNotContain("MutationObserver", expression);
        Assert.DoesNotContain("setInterval", expression);
        Assert.DoesNotContain("setTimeout", expression);
        Assert.DoesNotContain("classList", expression);
        Assert.DoesNotContain("appendChild", expression);
        Assert.DoesNotContain("removeChild", expression);
        Assert.DoesNotContain("setProperty", expression);
        Assert.DoesNotContain(".style", expression);
    }
}
