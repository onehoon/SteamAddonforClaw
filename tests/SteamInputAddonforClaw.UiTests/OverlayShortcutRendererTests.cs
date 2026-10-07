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
        Assert.Contains("private const int ShortcutVisualColumnCount = 3;", renderer, StringComparison.Ordinal);
        Assert.Contains("column < ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.Contains("index / ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.Contains("index % ShortcutVisualColumnCount", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min(2", renderer, StringComparison.Ordinal);
        Assert.Contains("for (var index = 0; index < _shortcutSnapshot.Tiles.Count; index++)", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_renderer_centers_title_and_status_and_keeps_empty_unavailable_and_disabled_states()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var resources = ReadSource("src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml");

        Assert.Contains("FrontendShortcutDashboardSnapshot.Unavailable()", renderer, StringComparison.Ordinal);
        Assert.Contains("if (!_shortcutSnapshot.Available", renderer, StringComparison.Ordinal);
        Assert.Contains("tile.Enabled", renderer, StringComparison.Ordinal);
        Assert.Contains("Text = tile.Title", renderer, StringComparison.Ordinal);
        Assert.Contains("tile.StatusText", renderer, StringComparison.Ordinal);
        Assert.Contains("Text = tile.StatusText", renderer, StringComparison.Ordinal);
        Assert.Contains("ApplyTextStyle(status, \"QamCaptionTextStyle\")", renderer, StringComparison.Ordinal);
        Assert.Contains("Spacing = string.IsNullOrWhiteSpace(tile.StatusText) ? 0 : 4", renderer, StringComparison.Ordinal);
        Assert.Contains("TextAlignment = TextAlignment.Center", renderer, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center", renderer, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"QamShortcutTileSize\">117.33333333333333</x:Double>", resources, StringComparison.Ordinal);
        Assert.Contains("Width = OverlayQamResources.Get(\"QamShortcutTileSize\", 117.33333333333333)", renderer, StringComparison.Ordinal);
        Assert.Contains("Height = OverlayQamResources.Get(\"QamShortcutTileSize\", 117.33333333333333)", renderer, StringComparison.Ordinal);
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
