using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutRendererTests
{
    [Fact]
    public void Shortcut_page_uses_the_runtime_snapshot_and_has_no_fixed_slot_poc()
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
        Assert.Contains("Math.Min(2, _shortcutSnapshot.Tiles.Count)", renderer, StringComparison.Ordinal);
        Assert.Contains("for (var index = 0; index < _shortcutSnapshot.Tiles.Count; index++)", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_renderer_covers_loading_empty_unavailable_and_disabled_tiles()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");

        Assert.Contains("Loading shortcuts", renderer, StringComparison.Ordinal);
        Assert.Contains("No shortcuts configured", renderer, StringComparison.Ordinal);
        Assert.Contains("if (!_shortcutSnapshot.Available", renderer, StringComparison.Ordinal);
        Assert.Contains("tile.Enabled", renderer, StringComparison.Ordinal);
        Assert.Contains("StatusText", renderer, StringComparison.Ordinal);
        Assert.Contains("ResetShortcutForShow", renderer, StringComparison.Ordinal);
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
    public void Live_refresh_preserves_tile_identity_and_new_show_clears_stale_state()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("ResetShortcutForShow();", shell, StringComparison.Ordinal);
        Assert.Contains("selectedTileId", navigation, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault", renderer, StringComparison.Ordinal);
        Assert.Contains("Loading shortcuts", renderer, StringComparison.Ordinal);
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
