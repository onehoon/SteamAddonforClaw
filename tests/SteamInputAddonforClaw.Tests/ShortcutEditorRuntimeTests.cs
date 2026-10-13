using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutEditorRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ShortcutEditorRuntimeTests.{Guid.NewGuid():N}");
    private string DocumentPath => Path.Combine(_directory, "shortcuts.json");

    [Fact]
    public void Capture_projects_known_actions_for_repair_and_keeps_unknown_actions_read_only()
    {
        var executable = Tile("Broken EXE", ShortcutActionTypeIds.Executable, "{\"path\":17,\"arguments\":false,\"secret\":\"do-not-project\"}", closeOverlayAfterLaunch: true);
        var script = Tile("Broken Script", ShortcutActionTypeIds.PowerShell, "{\"script\":false}");
        var url = Tile("Broken URL", ShortcutActionTypeIds.Url, "{\"url\":[]}");
        var screenshot = Tile("Legacy Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, "{\"legacy\":true}");
        var unknown = Tile("Future", "future.vendor.action", "{\"value\":1}");
        var oldSchema = Tile("Future schema", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}", schemaVersion: 2);
        Save(executable, script, url, screenshot, unknown, oldSchema);
        var runtime = CreateRuntime();

        var snapshot = runtime.CaptureEditor();

        Assert.True(snapshot.Available);
        Assert.Equal(6, snapshot.Tiles.Count);
        Assert.All(snapshot.Tiles.Take(4), tile =>
        {
            Assert.True(tile.Action.Editable);
            Assert.False(tile.Action.ConfigurationValid);
            Assert.Contains("Needs attention", tile.Action.ValidationMessage);
        });
        Assert.Equal(FrontendShortcutEditorActionKind.Executable, snapshot.Tiles[0].Action.Kind);
        Assert.Equal(string.Empty, snapshot.Tiles[0].Action.ExecutablePath);
        Assert.Equal(string.Empty, snapshot.Tiles[0].Action.ExecutableArguments);
        Assert.True(snapshot.Tiles[0].CloseOverlayAfterLaunch);
        Assert.Equal(FrontendShortcutEditorActionKind.PowerShell, snapshot.Tiles[1].Action.Kind);
        Assert.Equal(string.Empty, snapshot.Tiles[1].Action.PowerShellScript);
        Assert.Equal(FrontendShortcutEditorActionKind.Url, snapshot.Tiles[2].Action.Kind);
        Assert.Equal(string.Empty, snapshot.Tiles[2].Action.Url);
        Assert.Equal(FrontendShortcutEditorActionKind.ScreenshotFullscreen, snapshot.Tiles[3].Action.Kind);
        Assert.False(snapshot.Tiles[4].Action.Editable);
        Assert.Equal("future.vendor.action", snapshot.Tiles[4].Action.TypeId);
        Assert.False(snapshot.Tiles[5].Action.Editable);
        Assert.DoesNotContain("do-not-project", JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FrontendShortcutEditorActionKind.ScreenshotFullscreen, "system.screenshot-fullscreen", "\uE722")]
    [InlineData(FrontendShortcutEditorActionKind.SteamClient, "system.steam-client", "\uE7FC")]
    [InlineData(FrontendShortcutEditorActionKind.SteamBigPicture, "system.steam-big-picture", "\uE7F4")]
    [InlineData(FrontendShortcutEditorActionKind.XboxApp, "system.xbox-app", "\uE990")]
    [InlineData(FrontendShortcutEditorActionKind.Executable, "system.executable", "\uE71D")]
    [InlineData(FrontendShortcutEditorActionKind.PowerShell, "system.powershell", "\uE756")]
    [InlineData(FrontendShortcutEditorActionKind.Url, "system.url", "\uE774")]
    public void Runtime_projects_action_default_icon_from_type_id_not_title_or_tile_id(
        FrontendShortcutEditorActionKind kind,
        string typeId,
        string expectedGlyph)
    {
        Save(Tile("Misleading custom title", typeId, "{}"));
        var projected = Assert.Single(CreateRuntime().Capture().Tiles);
        var editor = Assert.Single(CreateRuntime().CaptureEditor().Tiles);

        Assert.Equal(expectedGlyph, projected.IconGlyph);
        Assert.Equal(kind, editor.Action.Kind);
        Assert.Equal(expectedGlyph, editor.IconGlyph);
        Assert.Null(editor.IconGlyphOverride);
    }

    [Theory]
    [InlineData(FrontendShortcutEditorActionKind.ScreenshotFullscreen, "system.screenshot-fullscreen", "\uE722")]
    [InlineData(FrontendShortcutEditorActionKind.SteamClient, "system.steam-client", "\uE7FC")]
    [InlineData(FrontendShortcutEditorActionKind.SteamBigPicture, "system.steam-big-picture", "\uE7F4")]
    [InlineData(FrontendShortcutEditorActionKind.XboxApp, "system.xbox-app", "\uE990")]
    public void Deleted_and_recreated_builtin_uses_default_icon_with_new_identity_and_title(
        FrontendShortcutEditorActionKind kind,
        string typeId,
        string expectedGlyph)
    {
        var store = new ShortcutStore(DocumentPath);
        var runtime = new ShortcutRuntime(store);
        var original = Assert.Single(runtime.CaptureEditor().Tiles, tile => tile.Action.TypeId == typeId);

        var deleted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, original.TileId));
        var recreated = runtime.MutateEditor(CreateIntent("User-chosen title", kind,
            closeOverlayAfterLaunch: kind != FrontendShortcutEditorActionKind.ScreenshotFullscreen));

        Assert.True(deleted.Succeeded);
        Assert.True(recreated.Succeeded);
        var replacement = Assert.Single(recreated.Snapshot.Tiles, tile => tile.Action.TypeId == typeId);
        Assert.NotEqual(original.TileId, replacement.TileId);
        Assert.Equal("User-chosen title", replacement.Title);
        Assert.Equal(expectedGlyph, replacement.IconGlyph);
        Assert.Null(replacement.IconGlyphOverride);
        Assert.Null(Assert.Single(store.Load().Document.Dashboard.Tiles, tile => tile.Action.TypeId == typeId).IconGlyphOverride);
    }

    [Fact]
    public void Custom_icon_survives_create_save_reload_rename_and_reorder()
    {
        var runtime = CreateRuntime();
        var created = runtime.MutateEditor(CreateIntent("Web", FrontendShortcutEditorActionKind.Url,
            url: "https://example.com") with { IconGlyphOverride = "\uE722" });
        var second = runtime.MutateEditor(CreateIntent("Steam", FrontendShortcutEditorActionKind.SteamClient));
        var firstTile = created.Snapshot.Tiles[0];
        var secondTileId = second.Snapshot.Tiles.Single(tile => tile.Title == "Steam").TileId;

        var renamed = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, firstTile.TileId,
            "Renamed Web", new(FrontendShortcutEditorActionKind.Url, Url: "https://example.com"),
            CloseOverlayAfterLaunch: false, IconGlyphOverride: "\uE722"));
        var moved = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, firstTile.TileId, TargetIndex: 1));
        var restarted = CreateRuntime();

        Assert.True(created.Succeeded && second.Succeeded && renamed.Succeeded && moved.Succeeded);
        Assert.Equal(new[] { secondTileId, firstTile.TileId }, moved.Snapshot.Tiles.Select(tile => tile.TileId));
        var stored = Assert.Single(restarted.CaptureEditor().Tiles, tile => tile.TileId == firstTile.TileId);
        Assert.Equal("Renamed Web", stored.Title);
        Assert.Equal("\uE722", stored.IconGlyphOverride);
        Assert.Equal("\uE722", stored.IconGlyph);
        Assert.Equal("\uE722", Assert.Single(restarted.Capture().Tiles, tile => tile.TileId == firstTile.TileId).IconGlyph);
        using var json = JsonDocument.Parse(File.ReadAllText(DocumentPath));
        Assert.Equal("\uE722", json.RootElement.GetProperty("dashboard").GetProperty("tiles")[1]
            .GetProperty("iconGlyphOverride").GetString());
    }

    [Fact]
    public void Use_default_clears_override_and_action_change_updates_default_glyph()
    {
        var runtime = CreateRuntime();
        var created = runtime.MutateEditor(CreateIntent("Web", FrontendShortcutEditorActionKind.Url,
            url: "https://example.com") with { IconGlyphOverride = "\uE722" });
        var tile = Assert.Single(created.Snapshot.Tiles);

        var changedActionKeepingOverride = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update,
            tile.TileId, "Web", new(FrontendShortcutEditorActionKind.SteamClient),
            CloseOverlayAfterLaunch: true, IconGlyphOverride: "\uE722"));
        var reset = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tile.TileId,
            "Web", new(FrontendShortcutEditorActionKind.SteamClient),
            CloseOverlayAfterLaunch: true, IconGlyphOverride: null));
        var changedAgain = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tile.TileId,
            "Web", new(FrontendShortcutEditorActionKind.PowerShell, PowerShellScript: "Write-Output 1"),
            CloseOverlayAfterLaunch: true, IconGlyphOverride: null));

        Assert.True(changedActionKeepingOverride.Succeeded && reset.Succeeded && changedAgain.Succeeded);
        Assert.Equal("\uE722", Assert.Single(changedActionKeepingOverride.Snapshot.Tiles).IconGlyph);
        var resetTile = Assert.Single(reset.Snapshot.Tiles);
        Assert.Null(resetTile.IconGlyphOverride);
        Assert.Equal("\uE7FC", resetTile.IconGlyph);
        var changedTile = Assert.Single(changedAgain.Snapshot.Tiles);
        Assert.Null(changedTile.IconGlyphOverride);
        Assert.Equal("\uE756", changedTile.IconGlyph);
        Assert.Null(Assert.Single(Load().Dashboard.Tiles).IconGlyphOverride);
    }

    [Fact]
    public void Invalid_icon_mutations_are_rejected_without_saving_and_delete_move_cannot_change_icons()
    {
        var existing = Tile("Existing", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}");
        Save(existing);
        var runtime = CreateRuntime();
        var before = File.ReadAllBytes(DocumentPath);

        var invalidCreate = runtime.MutateEditor(CreateIntent("Invalid", FrontendShortcutEditorActionKind.Url,
            url: "https://example.org") with { IconGlyphOverride = "\uEFFF" });
        var invalidUpdate = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, existing.TileId,
            "Updated", new(FrontendShortcutEditorActionKind.Url, Url: "https://example.org"),
            CloseOverlayAfterLaunch: false, IconGlyphOverride: "not-a-glyph"));
        var deleteWithIcon = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, existing.TileId,
            IconGlyphOverride: "\uE722"));
        var moveWithIcon = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, existing.TileId,
            TargetIndex: 0, IconGlyphOverride: "\uE722"));

        Assert.False(invalidCreate.Succeeded);
        Assert.False(invalidUpdate.Succeeded);
        Assert.False(deleteWithIcon.Succeeded);
        Assert.False(moveWithIcon.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(DocumentPath));
    }

    [Fact]
    public async Task Unknown_persisted_icon_falls_back_preserves_value_and_does_not_disable_execution()
    {
        var unknownGlyph = "future-icon-codepoint";
        var tile = Tile("Steam", ShortcutActionTypeIds.SteamClient, "{}") with { IconGlyphOverride = unknownGlyph };
        Save(tile);
        var original = File.ReadAllBytes(DocumentPath);
        var runtime = CreateRuntime(_ => new Process());

        var dashboardTile = Assert.Single(runtime.Capture().Tiles);
        var editorTile = Assert.Single(runtime.CaptureEditor().Tiles);
        var execution = await runtime.ExecuteAsync(tile.TileId);
        var renamed = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tile.TileId,
            "Renamed Steam", new(FrontendShortcutEditorActionKind.SteamClient),
            CloseOverlayAfterLaunch: false, IconGlyphOverride: unknownGlyph));

        Assert.Equal("\uE7FC", dashboardTile.IconGlyph);
        Assert.Equal(unknownGlyph, editorTile.IconGlyphOverride);
        Assert.Equal("\uE7FC", editorTile.IconGlyph);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, execution.Outcome);
        Assert.True(renamed.Succeeded);
        Assert.Equal(unknownGlyph, Assert.Single(Load().Dashboard.Tiles).IconGlyphOverride);
        Assert.NotEqual(original, File.ReadAllBytes(DocumentPath));
    }

    [Fact]
    public void Schema_two_document_without_icon_property_loads_and_is_not_rewritten()
    {
        Directory.CreateDirectory(_directory);
        const string original = "{\"schemaVersion\":2,\"dashboard\":{\"tiles\":[{\"tileId\":\"22222222-2222-2222-2222-222222222222\",\"title\":\"Steam\",\"closeOverlayAfterLaunch\":false,\"action\":{\"typeId\":\"system.steam-client\",\"schemaVersion\":1,\"parameters\":{}}}]}}";
        File.WriteAllText(DocumentPath, original);

        var runtime = new ShortcutRuntime(new ShortcutStore(DocumentPath));

        Assert.True(runtime.Capture().Available);
        Assert.Equal("\uE7FC", Assert.Single(runtime.Capture().Tiles).IconGlyph);
        Assert.Null(Assert.Single(runtime.CaptureEditor().Tiles).IconGlyphOverride);
        Assert.Equal(original, File.ReadAllText(DocumentPath));
    }

    [Fact]
    public void Create_update_move_delete_round_trip_through_the_runtime_store()
    {
        var runtime = CreateRuntime();
        var first = runtime.MutateEditor(CreateIntent("One", FrontendShortcutEditorActionKind.Executable, executablePath: @"C:\Tools\One.exe"));
        var second = runtime.MutateEditor(CreateIntent("Two", FrontendShortcutEditorActionKind.PowerShell, script: "Write-Output 'two'"));
        var third = runtime.MutateEditor(CreateIntent("Three", FrontendShortcutEditorActionKind.Url, url: "https://example.com/path"));
        var screenshot = runtime.MutateEditor(CreateIntent("Capture", FrontendShortcutEditorActionKind.ScreenshotFullscreen));

        Assert.True(first.Succeeded && first.Changed);
        Assert.True(second.Succeeded && second.Changed);
        Assert.True(third.Succeeded && third.Changed);
        Assert.True(screenshot.Succeeded && screenshot.Changed);
        var ids = screenshot.Snapshot.Tiles.Select(tile => tile.TileId).ToArray();
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(new[] { "One", "Two", "Three", "Capture" }, screenshot.Snapshot.Tiles.Select(tile => tile.Title));
        Assert.Equal("{}", ReadActionParameters(Load().Dashboard.Tiles[3]));
        Assert.False(screenshot.Snapshot.Tiles[3].CloseOverlayAfterLaunch);

        var stableId = ids[1];
        var update = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, stableId, "Renamed",
            new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Url, Url: "http://example.net"),
            CloseOverlayAfterLaunch: true));
        Assert.True(update.Succeeded);
        Assert.Equal(stableId, update.Snapshot.Tiles[1].TileId);
        Assert.Equal("Renamed", update.Snapshot.Tiles[1].Title);
        Assert.Equal(FrontendShortcutEditorActionKind.Url, update.Snapshot.Tiles[1].Action.Kind);
        Assert.True(update.Snapshot.Tiles[1].CloseOverlayAfterLaunch);
        Assert.True(Load().Dashboard.Tiles[1].CloseOverlayAfterLaunch);

        var moved = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, ids[0], TargetIndex: 3));
        Assert.True(moved.Succeeded && moved.Changed);
        Assert.Equal(new[] { ids[1], ids[2], ids[3], ids[0] }, moved.Snapshot.Tiles.Select(tile => tile.TileId));
        var noOp = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, ids[0], TargetIndex: 3));
        Assert.True(noOp.Succeeded);
        Assert.False(noOp.Changed);

        var deleted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[0]));
        Assert.True(deleted.Succeeded);
        Assert.Equal(new[] { ids[1], ids[2], ids[3] }, deleted.Snapshot.Tiles.Select(tile => tile.TileId));
        var deleteRemaining = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[1]));
        Assert.True(deleteRemaining.Succeeded);
        runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[2]));
        var empty = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[3]));
        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Snapshot.Tiles);
        var reloaded = CreateRuntime().CaptureEditor();
        Assert.Empty(reloaded.Tiles);
    }

    [Fact]
    public void Close_overlay_preference_is_required_for_create_and_update_and_rejected_for_delete_and_move()
    {
        var tile = Tile("Existing", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}");
        Save(tile);
        var before = File.ReadAllText(DocumentPath);
        var runtime = CreateRuntime();
        var action = new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Url, Url: "https://example.org");

        var createWithoutPreference = runtime.MutateEditor(new(FrontendShortcutMutationKind.Create,
            Title: "New", Action: action));
        var updateWithoutPreference = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update,
            tile.TileId, "Changed", action));
        var deleteWithPreference = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete,
            tile.TileId, CloseOverlayAfterLaunch: false));
        var moveWithPreference = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move,
            tile.TileId, CloseOverlayAfterLaunch: false, TargetIndex: 0));

        Assert.False(createWithoutPreference.Succeeded);
        Assert.False(updateWithoutPreference.Succeeded);
        Assert.False(deleteWithPreference.Succeeded);
        Assert.False(moveWithPreference.Succeeded);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
    }

    [Theory]
    [InlineData(FrontendShortcutEditorActionKind.SteamBigPicture, "system.steam-big-picture")]
    [InlineData(FrontendShortcutEditorActionKind.SteamClient, "system.steam-client")]
    [InlineData(FrontendShortcutEditorActionKind.XboxApp, "system.xbox-app")]
    [InlineData(FrontendShortcutEditorActionKind.ScreenshotFullscreen, "system.screenshot-fullscreen")]
    public void Parameterless_builtin_mutations_canonicalize_and_preserve_explicit_titles(
        FrontendShortcutEditorActionKind kind,
        string typeId)
    {
        var runtime = CreateRuntime();
        var created = runtime.MutateEditor(CreateIntent("Custom title", kind));

        Assert.True(created.Succeeded);
        var createdTile = Assert.Single(created.Snapshot.Tiles);
        Assert.Equal("Custom title", createdTile.Title);
        Assert.Equal(kind, createdTile.Action.Kind);
        Assert.Equal(typeId, createdTile.Action.TypeId);
        var stored = Assert.Single(Load().Dashboard.Tiles);
        Assert.Equal("Custom title", stored.Title);
        Assert.Equal(typeId, stored.Action.TypeId);
        Assert.False(stored.CloseOverlayAfterLaunch);
        Assert.Equal(1, stored.Action.SchemaVersion);
        Assert.Equal("{}", ReadActionParameters(stored));

        var updated = runtime.MutateEditor(new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Update,
            createdTile.TileId,
            "Renamed title",
            new FrontendShortcutActionInput(kind),
            CloseOverlayAfterLaunch: true));

        Assert.True(updated.Succeeded);
        Assert.Equal("Renamed title", Assert.Single(updated.Snapshot.Tiles).Title);
        var updatedStored = Assert.Single(Load().Dashboard.Tiles);
        Assert.Equal("Renamed title", updatedStored.Title);
        Assert.Equal(kind != FrontendShortcutEditorActionKind.ScreenshotFullscreen, updatedStored.CloseOverlayAfterLaunch);
        Assert.Equal(typeId, updatedStored.Action.TypeId);
        Assert.Equal(1, updatedStored.Action.SchemaVersion);
        Assert.Equal("{}", ReadActionParameters(updatedStored));
    }

    [Theory]
    [InlineData(FrontendShortcutEditorActionKind.SteamBigPicture, "system.steam-big-picture", "Steam Big Picture")]
    [InlineData(FrontendShortcutEditorActionKind.SteamClient, "system.steam-client", "Steam client")]
    [InlineData(FrontendShortcutEditorActionKind.XboxApp, "system.xbox-app", "XBOX app")]
    public void Invalid_parameterless_builtin_stays_editable_for_canonical_repair(
        FrontendShortcutEditorActionKind kind,
        string typeId,
        string summary)
    {
        var tile = Tile("Persisted custom title", typeId, "{\"unexpected\":true}");
        Save(tile);
        var runtime = CreateRuntime();

        var projected = Assert.Single(runtime.CaptureEditor().Tiles);

        Assert.Equal("Persisted custom title", projected.Title);
        Assert.Equal(summary, projected.TargetSummary);
        Assert.Equal(kind, projected.Action.Kind);
        Assert.True(projected.Action.Editable);
        Assert.False(projected.Action.ConfigurationValid);

        var repaired = runtime.MutateEditor(new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Update,
            tile.TileId,
            "Persisted custom title",
            new FrontendShortcutActionInput(kind),
            CloseOverlayAfterLaunch: tile.CloseOverlayAfterLaunch));

        Assert.True(repaired.Succeeded);
        Assert.Equal("Persisted custom title", Assert.Single(repaired.Snapshot.Tiles).Title);
        Assert.True(Assert.Single(repaired.Snapshot.Tiles).Action.ConfigurationValid);
        var stored = Assert.Single(Load().Dashboard.Tiles);
        Assert.Equal("{}", ReadActionParameters(stored));
    }

    [Fact]
    public void Screenshot_repair_canonicalizes_parameters_and_unsupported_actions_can_be_deleted()
    {
        var invalidScreenshot = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, "{\"oldOption\":true}");
        var unsupported = Tile("Future", "future.action", "{\"secret\":\"preserved\"}");
        Save(invalidScreenshot, unsupported);
        var runtime = CreateRuntime();

        var repaired = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, invalidScreenshot.TileId,
            "Screenshot", new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.ScreenshotFullscreen),
            CloseOverlayAfterLaunch: false));

        Assert.True(repaired.Succeeded);
        Assert.True(repaired.Snapshot.Tiles[0].Action.ConfigurationValid);
        Assert.False(repaired.Snapshot.Tiles[0].CloseOverlayAfterLaunch);
        Assert.Equal("{}", ReadActionParameters(Load().Dashboard.Tiles[0]));
        var deleted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, unsupported.TileId));
        Assert.True(deleted.Succeeded);
        Assert.Single(deleted.Snapshot.Tiles);
    }

    [Fact]
    public void Screenshot_folder_create_update_and_reset_are_persisted_in_action_parameters()
    {
        var runtime = CreateRuntime();
        var defaultAction = runtime.MutateEditor(CreateIntent("Screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen,
            screenshotFolder: "  "));

        Assert.True(defaultAction.Succeeded);
        var tileId = Assert.Single(defaultAction.Snapshot.Tiles).TileId;
        Assert.Equal("{}", ReadActionParameters(Assert.Single(Load().Dashboard.Tiles)));
        Assert.Null(Assert.Single(defaultAction.Snapshot.Tiles).Action.ScreenshotFolder);

        var customFolder = @"C:\Users\Test\Captures";
        var customAction = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tileId, "Screenshot",
            new(FrontendShortcutEditorActionKind.ScreenshotFullscreen, ScreenshotFolder: customFolder),
            CloseOverlayAfterLaunch: true));

        Assert.True(customAction.Succeeded);
        Assert.Equal(customFolder, Assert.Single(customAction.Snapshot.Tiles).Action.ScreenshotFolder);
        Assert.Equal(customFolder, Assert.Single(Load().Dashboard.Tiles).Action.Parameters.GetProperty("folder").GetString());
        Assert.False(Assert.Single(Load().Dashboard.Tiles).CloseOverlayAfterLaunch);

        var reset = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tileId, "Screenshot",
            new(FrontendShortcutEditorActionKind.ScreenshotFullscreen), CloseOverlayAfterLaunch: false));

        Assert.True(reset.Succeeded);
        Assert.Null(Assert.Single(reset.Snapshot.Tiles).Action.ScreenshotFolder);
        Assert.Equal("{}", ReadActionParameters(Assert.Single(Load().Dashboard.Tiles)));

        Assert.True(runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, tileId)).Succeeded);
        var createdCustom = runtime.MutateEditor(CreateIntent("Custom screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen,
            screenshotFolder: customFolder));
        Assert.True(createdCustom.Succeeded);
        Assert.Equal(customFolder, Assert.Single(createdCustom.Snapshot.Tiles).Action.ScreenshotFolder);
        Assert.Equal(customFolder, Assert.Single(Load().Dashboard.Tiles).Action.Parameters.GetProperty("folder").GetString());
    }

    [Fact]
    public void Invalid_screenshot_folder_is_rejected_without_changing_shortcut_document()
    {
        var runtime = CreateRuntime();
        var before = File.Exists(DocumentPath) ? File.ReadAllText(DocumentPath) : null;

        var result = runtime.MutateEditor(CreateIntent("Screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen,
            screenshotFolder: "relative\\captures"));

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(before, File.Exists(DocumentPath) ? File.ReadAllText(DocumentPath) : null);
        Assert.Empty(runtime.CaptureEditor().Tiles);
    }

    [Fact]
    public void Screenshot_mutations_reject_duplicates_but_keep_the_existing_screenshot_editable()
    {
        var runtime = CreateRuntime();
        var first = runtime.MutateEditor(CreateIntent("Screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen));
        Assert.True(first.Succeeded);
        var screenshot = Assert.Single(first.Snapshot.Tiles);
        var beforeDuplicate = File.ReadAllText(DocumentPath);

        var duplicate = runtime.MutateEditor(CreateIntent("Second screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen));
        Assert.False(duplicate.Succeeded);
        Assert.False(duplicate.Changed);
        Assert.Equal("Only one Screenshot Shortcut can be added.", duplicate.FailureMessage);
        Assert.Equal(beforeDuplicate, File.ReadAllText(DocumentPath));

        var other = runtime.MutateEditor(CreateIntent("Other", FrontendShortcutEditorActionKind.Url, url: "https://example.com"));
        Assert.True(other.Succeeded);
        var otherTile = other.Snapshot.Tiles.Single(tile => tile.TileId != screenshot.TileId);
        var beforeUpdate = File.ReadAllText(DocumentPath);
        var converted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, otherTile.TileId, "Other",
            new(FrontendShortcutEditorActionKind.ScreenshotFullscreen), CloseOverlayAfterLaunch: false));
        Assert.False(converted.Succeeded);
        Assert.Equal(beforeUpdate, File.ReadAllText(DocumentPath));

        var edited = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, screenshot.TileId, "Renamed screenshot",
            new(FrontendShortcutEditorActionKind.ScreenshotFullscreen, ScreenshotFolder: @"C:\Captures"),
            CloseOverlayAfterLaunch: true));
        Assert.True(edited.Succeeded);
        Assert.Equal(screenshot.TileId, edited.Snapshot.Tiles[0].TileId);
        Assert.Equal("Renamed screenshot", edited.Snapshot.Tiles[0].Title);
        Assert.Equal(@"C:\Captures", edited.Snapshot.Tiles[0].Action.ScreenshotFolder);
        Assert.False(edited.Snapshot.Tiles[0].CloseOverlayAfterLaunch);
    }

    [Fact]
    public void Failed_save_and_oversized_edits_leave_the_document_unchanged()
    {
        var existing = Tile("Existing", ShortcutActionTypeIds.PowerShell, "{\"script\":\"Write-Output 1\"}");
        Save(existing);
        var before = File.ReadAllText(DocumentPath);
        var runtime = new ShortcutRuntime(new ShortcutStore(DocumentPath), saveDocument: _ => throw new IOException("private path"));

        var failedSave = runtime.MutateEditor(CreateIntent("New", FrontendShortcutEditorActionKind.Url, url: "https://example.com"));
        var oversized = runtime.MutateEditor(CreateIntent("Large", FrontendShortcutEditorActionKind.PowerShell,
            script: new string('x', FrontendShortcutEditorPayloadPolicy.MaxFieldUtf8Bytes + 1)));

        Assert.False(failedSave.Succeeded);
        Assert.False(oversized.Succeeded);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal(existing.TileId, Assert.Single(runtime.CaptureEditor().Tiles).TileId);
    }

    [Fact]
    public void Oversized_executable_mutation_is_rejected_before_save()
    {
        var existing = Tile("Existing", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}");
        Save(existing);
        var before = File.ReadAllText(DocumentPath);
        var saveCount = 0;
        var runtime = new ShortcutRuntime(new ShortcutStore(DocumentPath),
            saveDocument: _ => saveCount++);

        var result = runtime.MutateEditor(CreateIntent("Large executable",
            FrontendShortcutEditorActionKind.Executable,
            executablePath: @"C:\Tools\Tool.exe",
            executableArguments: new string('x', 30_100)));

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("Executable path or arguments are too long to run.", result.FailureMessage);
        Assert.Equal(0, saveCount);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal(existing.TileId, Assert.Single(runtime.CaptureEditor().Tiles).TileId);
    }

    [Fact]
    public void Oversized_powershell_mutation_is_rejected_before_save()
    {
        var existing = Tile("Existing", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}");
        Save(existing);
        var before = File.ReadAllText(DocumentPath);
        var saveCount = 0;
        var runtime = new ShortcutRuntime(new ShortcutStore(DocumentPath),
            saveDocument: _ => saveCount++);

        var result = runtime.MutateEditor(CreateIntent("Large PowerShell",
            FrontendShortcutEditorActionKind.PowerShell,
            script: new string('x', FrontendShortcutEditorPayloadPolicy.MaxFieldUtf8Bytes / 4)));

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("PowerShell script is too long to run.", result.FailureMessage);
        Assert.Equal(0, saveCount);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal(existing.TileId, Assert.Single(runtime.CaptureEditor().Tiles).TileId);
    }

    [Fact]
    public async Task Near_safe_powershell_command_is_editable_projected_and_started()
    {
        var script = new string('x', 11_000);
        ProcessStartInfo? captured = null;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            return new Process();
        });

        var mutation = runtime.MutateEditor(CreateIntent("Near-safe PowerShell",
            FrontendShortcutEditorActionKind.PowerShell, script: script));
        var tile = Assert.Single(mutation.Snapshot.Tiles);
        var editorAction = Assert.Single(runtime.CaptureEditor().Tiles).Action;
        var projected = Assert.Single(runtime.Capture().Tiles);
        var execution = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(mutation.Succeeded);
        Assert.True(tile.Action.ConfigurationValid);
        Assert.True(editorAction.ConfigurationValid);
        Assert.True(projected.Enabled);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, execution.Outcome);
        Assert.NotNull(captured);
        var encodedScript = captured!.ArgumentList[6];
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encodedScript)));
        var fixedArgumentChars = new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand"
        }.Sum(argument => argument.Length);
        Assert.True(encodedScript.Length + captured.FileName.Length + fixedArgumentChars + 32 < 30_000);
    }

    [Fact]
    public void Unknown_tiles_invalid_actions_and_bad_move_indexes_fail_without_mutating()
    {
        var tile = Tile("Future", "future.action", "{\"value\":1}");
        Save(tile);
        var before = File.ReadAllText(DocumentPath);
        var runtime = CreateRuntime();

        var updateUnsupported = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tile.TileId, "Changed",
            new(FrontendShortcutEditorActionKind.Url, Url: "https://example.com"), CloseOverlayAfterLaunch: false));
        var deleteMissing = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, Guid.NewGuid()));
        var moveOutOfRange = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, tile.TileId, TargetIndex: 1));
        var invalidAction = runtime.MutateEditor(CreateIntent("Bad URL", FrontendShortcutEditorActionKind.Url, url: "file:///C:/secret"));
        var invalidSteamProtocol = runtime.MutateEditor(CreateIntent("Steam URI", FrontendShortcutEditorActionKind.Url, url: "steam://open/main"));

        Assert.False(updateUnsupported.Succeeded);
        Assert.False(deleteMissing.Succeeded);
        Assert.False(moveOutOfRange.Succeeded);
        Assert.False(invalidAction.Succeeded);
        Assert.False(invalidSteamProtocol.Succeeded);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal("Future", Assert.Single(runtime.CaptureEditor().Tiles).Title);
    }

    [Fact]
    public void Read_failure_document_is_unavailable_for_editor_mutations()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(DocumentPath);
        var runtime = CreateRuntime();

        var snapshot = runtime.CaptureEditor();
        var result = runtime.MutateEditor(CreateIntent("Web", FrontendShortcutEditorActionKind.Url, url: "https://example.com"));

        Assert.False(snapshot.Available);
        Assert.False(result.Succeeded);
        Assert.True(Directory.Exists(DocumentPath));
    }

    [Fact]
    public void Oversized_result_is_rejected_before_saving_the_candidate()
    {
        Save(Tile("Large existing", ShortcutActionTypeIds.PowerShell,
            JsonSerializer.Serialize(new { script = new string('x', 400 * 1024) })));
        var before = File.ReadAllText(DocumentPath);
        var runtime = CreateRuntime();
        Assert.True(runtime.CaptureEditor().Available);

        var result = runtime.MutateEditor(CreateIntent(new string('T', 130 * 1024),
            FrontendShortcutEditorActionKind.ScreenshotFullscreen));

        Assert.False(result.Succeeded);
        Assert.Equal("Shortcut configuration is too large to edit in this version.", result.FailureMessage);
        Assert.Single(result.Snapshot.Tiles);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
    }

    [Fact]
    public void Existing_oversized_document_is_preserved_and_reported_unavailable_for_editing()
    {
        var tile = Tile("Large", ShortcutActionTypeIds.PowerShell,
            JsonSerializer.Serialize(new { script = new string('x', FrontendShortcutEditorPayloadPolicy.MaxSnapshotBytes) }));
        Save(tile);
        var before = File.ReadAllText(DocumentPath);
        var runtime = CreateRuntime();

        var snapshot = runtime.CaptureEditor();

        Assert.False(snapshot.Available);
        Assert.Equal("Shortcut configuration is too large to edit in this version.", snapshot.FailureMessage);
        Assert.Empty(snapshot.Tiles);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
    }

    [Fact]
    public void Near_limit_mutation_request_fits_under_the_actual_frontend_frame_limit()
    {
        const int fieldSize = 250 * 1024;
        var title = new string('T', fieldSize);
        var script = new string('x', fieldSize);
        var intent = CreateIntent(title, FrontendShortcutEditorActionKind.PowerShell, script: script);
        Assert.True(FrontendShortcutEditorPayloadPolicy.IsMutationWithinLimit(intent));
        var mutationBytes = JsonSerializer.SerializeToUtf8Bytes(intent,
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }).Length;
        Assert.InRange(mutationBytes, FrontendShortcutEditorPayloadPolicy.MaxMutationBytes - 16 * 1024,
            FrontendShortcutEditorPayloadPolicy.MaxMutationBytes);

        var request = new FrontendWireEnvelope(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Request,
            1, FrontendRpcMethod.MutateShortcut, Payload: FrontendWireCodec.Payload(intent));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(request, FrontendWireCodec.Json).Length < FrontendWireCodec.MaxFrameBytes);
    }

    [Fact]
    public void Near_limit_editor_result_fits_under_the_actual_frontend_frame_limit()
    {
        const int scriptSize = 250 * 1024;
        var script = new string('x', scriptSize);
        var first = Tile("First", ShortcutActionTypeIds.PowerShell, JsonSerializer.Serialize(new { script }));
        var second = Tile("Second", ShortcutActionTypeIds.PowerShell, JsonSerializer.Serialize(new { script }));
        Save(first, second);
        var runtime = CreateRuntime();
        var result = runtime.MutateEditor(new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Move, first.TileId, TargetIndex: 0));

        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        Assert.True(FrontendShortcutEditorPayloadPolicy.IsMutationResultWithinLimit(result));
        var resultBytes = JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }).Length;
        Assert.InRange(resultBytes, FrontendShortcutEditorPayloadPolicy.MaxSnapshotBytes - 16 * 1024,
            FrontendShortcutEditorPayloadPolicy.MaxSnapshotBytes);
        var response = new FrontendWireEnvelope(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response,
            1, FrontendRpcMethod.MutateShortcut, Payload: FrontendWireCodec.Payload(result));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(response, FrontendWireCodec.Json).Length < FrontendWireCodec.MaxFrameBytes);
    }

    private ShortcutRuntime CreateRuntime(Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        if (!File.Exists(DocumentPath) && !Directory.Exists(DocumentPath))
            new ShortcutStore(DocumentPath).Save(new ShortcutDocument());
        return new ShortcutRuntime(new ShortcutStore(DocumentPath), startProcess);
    }

    private void Save(params ShortcutTileDefinition[] tiles)
    {
        Directory.CreateDirectory(_directory);
        new ShortcutStore(DocumentPath).Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition(tiles) });
    }

    private ShortcutDocument Load() => new ShortcutStore(DocumentPath).Load().Document;

    private static FrontendShortcutMutationIntent CreateIntent(
        string title,
        FrontendShortcutEditorActionKind kind,
        string? executablePath = null,
        string? executableArguments = null,
        string? script = null,
        string? url = null,
        string? screenshotFolder = null,
        bool closeOverlayAfterLaunch = false) =>
        new(FrontendShortcutMutationKind.Create, Title: title,
            Action: new FrontendShortcutActionInput(kind,
                ExecutablePath: executablePath,
                ExecutableArguments: executableArguments,
                PowerShellScript: script,
                Url: url,
                ScreenshotFolder: screenshotFolder),
            CloseOverlayAfterLaunch: closeOverlayAfterLaunch);

    private static ShortcutTileDefinition Tile(string title, string typeId, string json, int schemaVersion = 1,
        bool closeOverlayAfterLaunch = false)
    {
        using var document = JsonDocument.Parse(json);
        return new(Guid.NewGuid(), title, closeOverlayAfterLaunch,
            new ShortcutActionSpec(typeId, schemaVersion, document.RootElement.Clone()));
    }

    private static string ReadActionParameters(ShortcutTileDefinition tile) => tile.Action.Parameters.GetRawText();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
