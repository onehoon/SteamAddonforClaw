using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutEditorRuntimeTests : IDisposable
{
    private const string ScreenshotFolder = @"C:\Users\Test\Pictures\Screenshots";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ShortcutEditorRuntimeTests.{Guid.NewGuid():N}");
    private string DocumentPath => Path.Combine(_directory, "shortcuts.json");
    private FrontendScreenshotFolderSnapshot Folder => new(true, ScreenshotFolder, null);

    [Fact]
    public void Capture_projects_known_actions_for_repair_and_keeps_unknown_actions_read_only()
    {
        var executable = Tile("Broken EXE", ShortcutActionTypeIds.Executable, "{\"path\":17,\"arguments\":false,\"secret\":\"do-not-project\"}");
        var script = Tile("Broken Script", ShortcutActionTypeIds.PowerShell, "{\"script\":false}");
        var url = Tile("Broken URL", ShortcutActionTypeIds.Url, "{\"url\":[]}");
        var screenshot = Tile("Legacy Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, "{\"legacy\":true}");
        var unknown = Tile("Future", "future.vendor.action", "{\"value\":1}");
        var oldSchema = Tile("Future schema", ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}", schemaVersion: 2);
        Save(executable, script, url, screenshot, unknown, oldSchema);
        var runtime = CreateRuntime();

        var snapshot = runtime.CaptureEditor(Folder);

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

    [Fact]
    public void Create_update_move_delete_round_trip_through_the_runtime_store()
    {
        var runtime = CreateRuntime();
        var first = runtime.MutateEditor(CreateIntent("One", FrontendShortcutEditorActionKind.Executable, executablePath: @"C:\Tools\One.exe"), Folder);
        var second = runtime.MutateEditor(CreateIntent("Two", FrontendShortcutEditorActionKind.PowerShell, script: "Write-Output 'two'"), Folder);
        var third = runtime.MutateEditor(CreateIntent("Three", FrontendShortcutEditorActionKind.Url, url: "https://example.com/path"), Folder);
        var screenshot = runtime.MutateEditor(CreateIntent("Capture", FrontendShortcutEditorActionKind.ScreenshotFullscreen), Folder);

        Assert.True(first.Succeeded && first.Changed);
        Assert.True(second.Succeeded && second.Changed);
        Assert.True(third.Succeeded && third.Changed);
        Assert.True(screenshot.Succeeded && screenshot.Changed);
        var ids = screenshot.Snapshot.Tiles.Select(tile => tile.TileId).ToArray();
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(new[] { "One", "Two", "Three", "Capture" }, screenshot.Snapshot.Tiles.Select(tile => tile.Title));
        Assert.Equal("{}", ReadActionParameters(Load().Dashboard.Tiles[3]));

        var stableId = ids[1];
        var update = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, stableId, "Renamed",
            new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Url, Url: "http://example.net")), Folder);
        Assert.True(update.Succeeded);
        Assert.Equal(stableId, update.Snapshot.Tiles[1].TileId);
        Assert.Equal("Renamed", update.Snapshot.Tiles[1].Title);
        Assert.Equal(FrontendShortcutEditorActionKind.Url, update.Snapshot.Tiles[1].Action.Kind);

        var moved = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, ids[0], TargetIndex: 3), Folder);
        Assert.True(moved.Succeeded && moved.Changed);
        Assert.Equal(new[] { ids[1], ids[2], ids[3], ids[0] }, moved.Snapshot.Tiles.Select(tile => tile.TileId));
        var noOp = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, ids[0], TargetIndex: 3), Folder);
        Assert.True(noOp.Succeeded);
        Assert.False(noOp.Changed);

        var deleted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[0]), Folder);
        Assert.True(deleted.Succeeded);
        Assert.Equal(new[] { ids[1], ids[2], ids[3] }, deleted.Snapshot.Tiles.Select(tile => tile.TileId));
        var deleteRemaining = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[1]), Folder);
        Assert.True(deleteRemaining.Succeeded);
        runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[2]), Folder);
        var empty = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, ids[3]), Folder);
        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Snapshot.Tiles);
        var reloaded = CreateRuntime().CaptureEditor(Folder);
        Assert.Empty(reloaded.Tiles);
    }

    [Fact]
    public void Screenshot_repair_canonicalizes_parameters_and_unsupported_actions_can_be_deleted()
    {
        var invalidScreenshot = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, "{\"oldOption\":true}");
        var unsupported = Tile("Future", "future.action", "{\"secret\":\"preserved\"}");
        Save(invalidScreenshot, unsupported);
        var runtime = CreateRuntime();

        var repaired = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, invalidScreenshot.TileId,
            "Screenshot", new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.ScreenshotFullscreen)), Folder);

        Assert.True(repaired.Succeeded);
        Assert.True(repaired.Snapshot.Tiles[0].Action.ConfigurationValid);
        Assert.Equal("{}", ReadActionParameters(Load().Dashboard.Tiles[0]));
        var deleted = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, unsupported.TileId), Folder);
        Assert.True(deleted.Succeeded);
        Assert.Single(deleted.Snapshot.Tiles);
    }

    [Fact]
    public void Failed_save_and_oversized_edits_leave_the_document_unchanged()
    {
        var existing = Tile("Existing", ShortcutActionTypeIds.PowerShell, "{\"script\":\"Write-Output 1\"}");
        Save(existing);
        var before = File.ReadAllText(DocumentPath);
        var runtime = new ShortcutRuntime(new ShortcutStore(DocumentPath), saveDocument: _ => throw new IOException("private path"));

        var failedSave = runtime.MutateEditor(CreateIntent("New", FrontendShortcutEditorActionKind.Url, url: "https://example.com"), Folder);
        var oversized = runtime.MutateEditor(CreateIntent("Large", FrontendShortcutEditorActionKind.PowerShell,
            script: new string('x', FrontendShortcutEditorPayloadPolicy.MaxFieldUtf8Bytes + 1)), Folder);

        Assert.False(failedSave.Succeeded);
        Assert.False(oversized.Succeeded);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal(existing.TileId, Assert.Single(runtime.CaptureEditor(Folder).Tiles).TileId);
    }

    [Fact]
    public void Unknown_tiles_invalid_actions_and_bad_move_indexes_fail_without_mutating()
    {
        var tile = Tile("Future", "future.action", "{\"value\":1}");
        Save(tile);
        var before = File.ReadAllText(DocumentPath);
        var runtime = CreateRuntime();

        var updateUnsupported = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, tile.TileId, "Changed",
            new(FrontendShortcutEditorActionKind.Url, Url: "https://example.com")), Folder);
        var deleteMissing = runtime.MutateEditor(new(FrontendShortcutMutationKind.Delete, Guid.NewGuid()), Folder);
        var moveOutOfRange = runtime.MutateEditor(new(FrontendShortcutMutationKind.Move, tile.TileId, TargetIndex: 1), Folder);
        var invalidAction = runtime.MutateEditor(CreateIntent("Bad URL", FrontendShortcutEditorActionKind.Url, url: "file:///C:/secret"), Folder);

        Assert.False(updateUnsupported.Succeeded);
        Assert.False(deleteMissing.Succeeded);
        Assert.False(moveOutOfRange.Succeeded);
        Assert.False(invalidAction.Succeeded);
        Assert.Equal(before, File.ReadAllText(DocumentPath));
        Assert.Equal("Future", Assert.Single(runtime.CaptureEditor(Folder).Tiles).Title);
    }

    [Fact]
    public void Read_failure_document_is_unavailable_for_editor_mutations()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(DocumentPath);
        var runtime = CreateRuntime();

        var snapshot = runtime.CaptureEditor(Folder);
        var result = runtime.MutateEditor(CreateIntent("Web", FrontendShortcutEditorActionKind.Url, url: "https://example.com"), Folder);

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
        Assert.True(runtime.CaptureEditor(Folder).Available);

        var result = runtime.MutateEditor(CreateIntent(new string('T', 130 * 1024),
            FrontendShortcutEditorActionKind.ScreenshotFullscreen), Folder);

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

        var snapshot = runtime.CaptureEditor(Folder);

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
            FrontendShortcutMutationKind.Move, first.TileId, TargetIndex: 0), Folder);

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

    private ShortcutRuntime CreateRuntime() => new(new ShortcutStore(DocumentPath));

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
        string? script = null,
        string? url = null) =>
        new(FrontendShortcutMutationKind.Create, Title: title,
            Action: new FrontendShortcutActionInput(kind,
                ExecutablePath: executablePath,
                PowerShellScript: script,
                Url: url));

    private static ShortcutTileDefinition Tile(string title, string typeId, string json, int schemaVersion = 1)
    {
        using var document = JsonDocument.Parse(json);
        return new(Guid.NewGuid(), title, new ShortcutActionSpec(typeId, schemaVersion, document.RootElement.Clone()));
    }

    private static string ReadActionParameters(ShortcutTileDefinition tile) => tile.Action.Parameters.GetRawText();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
