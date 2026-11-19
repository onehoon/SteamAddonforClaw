using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutRendererTests
{
    [Fact]
    public void Shortcut_page_reconciles_authoritative_runtime_tiles_without_clearing_the_grid()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        Assert.DoesNotContain("TemporaryShortcutTiles", shell + renderer, StringComparison.Ordinal);
        foreach (var legacySlot in new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4" })
            Assert.DoesNotContain(legacySlot, shell + renderer, StringComparison.Ordinal);
        Assert.Contains("FrontendShortcutDashboardSnapshot", renderer, StringComparison.Ordinal);
        Assert.Contains("_shortcutSnapshot.Tiles", renderer, StringComparison.Ordinal);
        Assert.Contains("OverlayShortcutTileReconciliation.Between(_renderedShortcutTiles, tiles)", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.Children.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutTiles.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.RowDefinitions.Clear()", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.ColumnDefinitions.Clear()", renderer, StringComparison.Ordinal);
        Assert.Contains("if (updated.Contains(tile.TileId))", renderer, StringComparison.Ordinal);
        Assert.Contains("if (repositioned.Contains(tile.TileId))", renderer, StringComparison.Ordinal);
        Assert.Contains("for (var index = 0; index < tiles.Count; index++)", renderer, StringComparison.Ordinal);
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
    public void Warm_show_retains_last_known_tiles_and_resets_shortcut_selection_before_reveal()
    {
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var resetStart = shell.IndexOf("private void ResetUiForShow()", StringComparison.Ordinal);
        var resetEnd = shell.IndexOf("// OQ5-UI-02:", resetStart, StringComparison.Ordinal);
        var reset = shell[resetStart..resetEnd];
        var shortcutTabStart = shell.IndexOf("if (selected == AddonQuickSettingsTabId.Shortcut)", StringComparison.Ordinal);
        var shortcutTabEnd = shell.IndexOf("OnProfileTabSelectionChanged", shortcutTabStart, StringComparison.Ordinal);
        var shortcutTabSelection = shell[shortcutTabStart..shortcutTabEnd];
        var showStart = presentation.IndexOf("internal async Task ShowForPocAsync()", StringComparison.Ordinal);
        var hideStart = presentation.IndexOf("internal Task HideForPocAsync()", showStart, StringComparison.Ordinal);
        var show = presentation[showStart..hideStart];

        Assert.Contains("_tabState.ResetForShow();", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetShortcutForShow", reset + renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshShortcutSelection(FrontendShortcutDashboardSnapshot.Unavailable())", renderer, StringComparison.Ordinal);
        Assert.True(reset.IndexOf("_tabState.ResetForShow();", StringComparison.Ordinal)
            < reset.IndexOf("ApplySelectedTabVisualState();", StringComparison.Ordinal));
        Assert.True(show.IndexOf("ResetUiForShow();", StringComparison.Ordinal)
            < show.IndexOf("WindowInterop.ShowWithoutActivation(this);", StringComparison.Ordinal));
        Assert.Contains("ResetShortcutSelection();", shortcutTabSelection, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutSelectionVisual();", shortcutTabSelection, StringComparison.Ordinal);
        Assert.Contains("FindInitialShortcutTileIndex(_shortcutSnapshot.Tiles)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"), StringComparison.Ordinal);
        Assert.Contains("if (tiles[index].Enabled)", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"), StringComparison.Ordinal);
        Assert.Contains("_shortcutTiles.TryGetValue(tile.TileId, out var existing)", renderer, StringComparison.Ordinal);
        Assert.Contains("selectedTileId", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"), StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void First_show_without_a_runtime_snapshot_starts_empty_until_authoritative_state_arrives()
    {
        var renderer = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var buildStart = renderer.IndexOf("private FrameworkElement BuildShortcutPage()", StringComparison.Ordinal);
        var buildEnd = renderer.IndexOf("internal bool ApplyShortcutState(", buildStart, StringComparison.Ordinal);
        var build = renderer[buildStart..buildEnd];
        var renderStart = renderer.IndexOf("private void RenderShortcutSnapshot()", StringComparison.Ordinal);
        var renderEnd = renderer.IndexOf("private ShortcutTileControls CreateShortcutTile(", renderStart, StringComparison.Ordinal);
        var render = renderer[renderStart..renderEnd];

        Assert.Contains("FrontendShortcutDashboardSnapshot.Unavailable();", renderer, StringComparison.Ordinal);
        Assert.Contains("RenderShortcutSnapshot();", build, StringComparison.Ordinal);
        Assert.Contains("_shortcutSnapshot.Available", render, StringComparison.Ordinal);
        Assert.Contains("_shortcutSnapshot.Tiles", render, StringComparison.Ordinal);
        Assert.Contains(": []", render, StringComparison.Ordinal);
        Assert.Contains("OverlayShortcutTileReconciliation.Between(_renderedShortcutTiles, tiles)", render, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.Children.Clear()", render, StringComparison.Ordinal);
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
    public void Hide_completes_synchronously_after_cancelling_unsubmitted_work_and_hiding_the_window()
    {
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var start = presentation.IndexOf("internal Task HideForPocAsync()", StringComparison.Ordinal);
        var end = presentation.IndexOf("private void ConfigureWindow(bool alreadyVisible = false)", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var hide = presentation[start..end];
        Assert.Contains("CancelUnsubmittedDrafts()", hide, StringComparison.Ordinal);
        Assert.Contains("CancelPendingControllerVibrationDraft()", hide, StringComparison.Ordinal);
        Assert.Contains("WindowInterop.Hide(this)", hide, StringComparison.Ordinal);
        Assert.Contains("return Task.CompletedTask", hide, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", hide, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateAsync", hide, StringComparison.Ordinal);
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
