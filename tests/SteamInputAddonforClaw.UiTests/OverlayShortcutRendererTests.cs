using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutRendererTests
{
    [Fact]
    public void Shortcut_page_uses_a_fixed_three_column_runtime_snapshot_and_has_no_fixed_slot_poc()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");

        Assert.DoesNotContain("TemporaryShortcutTiles", shell + renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot 1", shell + renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot 2", shell + renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot 3", shell + renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Slot 4", shell + renderer, StringComparison.Ordinal);
        Assert.Contains("FrontendShortcutDashboardSnapshot", renderer, StringComparison.Ordinal);
        Assert.Contains("_shortcutSnapshot.Tiles", renderer, StringComparison.Ordinal);
        Assert.Contains("OverlayShortcutTileReconciliation.Between(_renderedShortcutTiles, tiles)", renderer, StringComparison.Ordinal);
        Assert.Contains("Reconciliation", renderer, StringComparison.Ordinal);
        Assert.Contains("private const int ShortcutVisualColumnCount = 3;", renderer, StringComparison.Ordinal);
        Assert.Contains("column < ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.Contains("index / ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.Contains("index % ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.Children.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutTiles.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.RowDefinitions.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.ColumnDefinitions.Clear()", renderer, StringComparison.Ordinal);
        Assert.Contains("if (updated.Contains(tile.TileId))", renderer, StringComparison.Ordinal);
        Assert.Contains("if (repositioned.Contains(tile.TileId))", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min(2", renderer, StringComparison.Ordinal);
        Assert.Contains("for (var index = 0; index < tiles.Count; index++)", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_renderer_centers_title_and_status_and_keeps_empty_unavailable_and_disabled_states()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var resources = ReadSource("src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml");

        Assert.Contains("FrontendShortcutDashboardSnapshot.Unavailable()", renderer, StringComparison.Ordinal);
        Assert.Contains("_shortcutSnapshot.Available", renderer, StringComparison.Ordinal);
        Assert.Contains("tile.Enabled", renderer, StringComparison.Ordinal);
        Assert.Contains("Text = tile.Title", renderer, StringComparison.Ordinal);
        Assert.Contains("new FontIcon", renderer, StringComparison.Ordinal);
        Assert.Contains("Glyph = tile.IconGlyph", renderer, StringComparison.Ordinal);
        Assert.Contains("FontSize = 24", renderer, StringComparison.Ordinal);
        Assert.Contains("FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(\"Segoe Fluent Icons\")", renderer, StringComparison.Ordinal);
        Assert.Contains("Orientation = Orientation.Vertical", renderer, StringComparison.Ordinal);
        Assert.Contains("header.Children.Add(icon)", renderer, StringComparison.Ordinal);
        Assert.Contains("header.Children.Add(title)", renderer, StringComparison.Ordinal);
        Assert.True(
            renderer.IndexOf("header.Children.Add(icon)", StringComparison.Ordinal)
            < renderer.IndexOf("header.Children.Add(title)", StringComparison.Ordinal));
        Assert.Contains("MaxWidth = contentWidth", renderer, StringComparison.Ordinal);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", renderer, StringComparison.Ordinal);
        Assert.Contains("var hasStatus = !string.IsNullOrWhiteSpace(tile.StatusText);", renderer, StringComparison.Ordinal);
        Assert.Contains("MaxLines = hasStatus ? 1 : 2", renderer, StringComparison.Ordinal);
        Assert.Contains("Spacing = hasStatus ? 4 : 6", renderer, StringComparison.Ordinal);
        Assert.Contains("Spacing = hasStatus ? 4 : 0", renderer, StringComparison.Ordinal);
        Assert.Contains("Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed", renderer, StringComparison.Ordinal);
        Assert.Contains("tile.StatusText ?? string.Empty", renderer, StringComparison.Ordinal);
        Assert.Contains("controls.Status.Text != statusText", renderer, StringComparison.Ordinal);
        Assert.Contains("controls.Icon.Glyph != tile.IconGlyph", renderer, StringComparison.Ordinal);
        Assert.Contains("controls.Border.Opacity != opacity", renderer, StringComparison.Ordinal);
        var statusStart = renderer.IndexOf("var status = new TextBlock", StringComparison.Ordinal);
        var statusEnd = renderer.IndexOf("OverlayQamResources.ApplyTextStyle(status", statusStart, StringComparison.Ordinal);
        var statusBuilder = renderer[statusStart..statusEnd];
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", statusBuilder, StringComparison.Ordinal);
        Assert.Contains("MaxLines = 2", statusBuilder, StringComparison.Ordinal);
        Assert.Contains("ApplyTextStyle(status, \"QamCaptionTextStyle\")", renderer, StringComparison.Ordinal);
        Assert.Contains("TextAlignment = TextAlignment.Center", renderer, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Center", renderer, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("contentWidth - 22", renderer, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"QamTileTitleTextStyle\" TargetType=\"TextBlock\" BasedOn=\"{StaticResource QamBodyStrongTextStyle}\" />", resources, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"16\" />", resources, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"QamShortcutTileSize\">117.33333333333333</x:Double>", resources, StringComparison.Ordinal);
        Assert.Contains("Width = tileSize", renderer, StringComparison.Ordinal);
        Assert.Contains("Height = tileSize", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutStatus", renderer, StringComparison.Ordinal);
        Assert.Contains("ResetShortcutForShow", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void One_through_six_tiles_keep_the_same_three_column_square_slot_geometry()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var resources = ReadSource("src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml");

        Assert.Contains("ColumnSpacing = OverlayQamResources.Get(\"QamTileSpacing\", 8.0)", renderer, StringComparison.Ordinal);
        Assert.Contains("RowSpacing = OverlayQamResources.Get(\"QamTileSpacing\", 8.0)", renderer, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"QamTileSpacing\">8</x:Double>", resources, StringComparison.Ordinal);

        for (var tileCount = 1; tileCount <= 6; tileCount++)
        {
            var positions = Enumerable.Range(0, tileCount)
                .Select(index => (Row: index / 3, Column: index % 3))
                .ToArray();
            Assert.Equal(Enumerable.Range(0, tileCount),
                positions.Select(position => position.Row * 3 + position.Column));
            Assert.All(positions, position => Assert.InRange(position.Column, 0, 2));
        }
    }

    [Fact]
    public void Accept_and_pointer_use_the_same_tile_id_execution_intent()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("ShortcutExecutionRequested", renderer, StringComparison.Ordinal);
        Assert.Contains("Func<Guid, Task>? ShortcutExecutionRequested", renderer, StringComparison.Ordinal);
        Assert.Contains("await request(tileId)", renderer, StringComparison.Ordinal);
        Assert.Contains("finally", renderer, StringComparison.Ordinal);
        Assert.Contains("RequestSelectedShortcutExecution", renderer + navigation, StringComparison.Ordinal);
        Assert.Contains("tile.Enabled", renderer + navigation, StringComparison.Ordinal);
        Assert.Contains("RequestShortcutExecution(tile.TileId)", renderer + navigation, StringComparison.Ordinal);
        Assert.Contains("StartBringIntoView", renderer + navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_execution_guard_is_kept_without_showing_an_in_flight_message()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var request = renderer[renderer.IndexOf("private void RequestShortcutExecution(Guid tileId)", StringComparison.Ordinal)..
            renderer.IndexOf("private async Task ExecuteShortcutIntentAsync", StringComparison.Ordinal)];
        Assert.Contains("_shortcutExecutionInFlight", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Running shortcut...", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateShortcutMessage", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutFeedbackMessage", renderer, StringComparison.Ordinal);
        Assert.Contains("finally", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_refresh_preserves_tile_identity_and_new_show_clears_stale_state()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("ResetShortcutForShow();", shell, StringComparison.Ordinal);
        Assert.Contains("selectedTileId", navigation, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault", renderer, StringComparison.Ordinal);
        Assert.Contains("FrontendShortcutDashboardSnapshot.Unavailable()", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_refresh_reuses_tile_controls_by_identity_and_only_navigation_requests_scroll()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("_shortcutTiles.TryGetValue(tile.TileId, out var existing)", renderer, StringComparison.Ordinal);
        Assert.Contains("CreateShortcutTile(tile, index", renderer, StringComparison.Ordinal);
        Assert.Contains("_shortcutGrid.Children.Remove(controls.Border)", renderer, StringComparison.Ordinal);
        Assert.Contains("if (bringIntoView && OnShortcutPage()", navigation, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutSelectionVisual(bringIntoView: true)", navigation, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutSelectionVisual();", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("StartBringIntoView", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_shortcut_snapshot_refresh_does_not_request_scroll()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        var refreshStart = navigation.IndexOf("private void RefreshShortcutSelection(", StringComparison.Ordinal);
        var refreshEnd = navigation.IndexOf("private IReadOnlyList<OverlayRowCapabilities> CapabilitiesFor", refreshStart, StringComparison.Ordinal);
        var refresh = navigation[refreshStart..refreshEnd];

        Assert.Contains("RenderShortcutSnapshot();", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("bringIntoView", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("StartBringIntoView", refresh + renderer, StringComparison.Ordinal);
        Assert.Contains("if (bringIntoView && OnShortcutPage()", navigation, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutSelectionVisual(bringIntoView: true)", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_shortcut_renderer_never_consumes_editor_action_payload()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");

        Assert.DoesNotContain("FrontendShortcutEditor", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutablePath", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerShellScript", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenshotSaveFolder", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionSpec", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeId", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("tile.Action", renderer, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
