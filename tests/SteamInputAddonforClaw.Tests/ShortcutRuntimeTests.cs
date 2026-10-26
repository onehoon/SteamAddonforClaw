using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Shortcuts;
using SteamInputAddonforClaw.WindowsGaming;
using SteamInputAddonforClaw.Processes;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutRuntimeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SteamInputAddonforClaw.ShortcutRuntime.Tests.{Guid.NewGuid():N}");

    private string ShortcutsPath => Path.Combine(_testDirectory, "shortcuts.json");

    [Fact]
    public void Missing_document_persists_the_exact_first_install_shortcut_defaults()
    {
        var store = new ShortcutStore(ShortcutsPath);
        var runtime = new ShortcutRuntime(store);
        var loaded = store.Load();

        Assert.True(File.Exists(ShortcutsPath));
        Assert.Equal(ShortcutLoadStatus.Loaded, loaded.Status);
        Assert.Equal(ShortcutDocument.CurrentSchemaVersion, loaded.Document.SchemaVersion);
        using var seedJson = JsonDocument.Parse(File.ReadAllText(ShortcutsPath));
        Assert.All(seedJson.RootElement.GetProperty("dashboard").GetProperty("tiles").EnumerateArray(), tile =>
            Assert.False(tile.TryGetProperty("iconGlyphOverride", out _)));

        var tiles = loaded.Document.Dashboard.Tiles;
        Assert.Equal(
            new[]
            {
                ("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, false),
                ("Steam", ShortcutActionTypeIds.SteamClient, true),
                ("Steam Big Picture", ShortcutActionTypeIds.SteamBigPicture, true),
                ("XBOX", ShortcutActionTypeIds.XboxApp, true),
                ("Keyboard", ShortcutActionTypeIds.TouchKeyboard, true),
                ("Show Desktop", ShortcutActionTypeIds.ShowDesktop, true),
                ("Steam Input", ShortcutActionTypeIds.SteamInput, true),
                ("ClawHUD", ShortcutActionTypeIds.ClawHudToggle, false),
            },
            tiles.Select(tile => (tile.Title, tile.Action.TypeId, tile.CloseOverlayAfterLaunch)));
        Assert.All(tiles, tile =>
        {
            Assert.NotEqual(Guid.Empty, tile.TileId);
            Assert.Equal(1, tile.Action.SchemaVersion);
            Assert.Equal(JsonValueKind.Object, tile.Action.Parameters.ValueKind);
            Assert.Empty(tile.Action.Parameters.EnumerateObject());
            Assert.Null(tile.IconGlyphOverride);
        });
        Assert.Equal(tiles.Count, tiles.Select(tile => tile.TileId).Distinct().Count());

        var dashboard = runtime.Capture();
        var editor = runtime.CaptureEditor();
        Assert.True(dashboard.Available);
        Assert.True(editor.Available);
        Assert.Equal(tiles.Select(tile => (tile.TileId, tile.Title)), dashboard.Tiles.Select(tile => (tile.TileId, tile.Title)));
        Assert.Equal(tiles.Select(tile => (tile.TileId, tile.Title)), editor.Tiles.Select(tile => (tile.TileId, tile.Title)));
        Assert.Equal(["\uE722", "\uE7FC", "\uE7F4", "\uE990", "\uE765", "\uE7C4", "\uE713", "\uE770"],
            dashboard.Tiles.Select(tile => tile.IconGlyph));
        Assert.Equal(dashboard.Tiles.Select(tile => tile.IconGlyph), editor.Tiles.Select(tile => tile.IconGlyph));
        Assert.Null(editor.Tiles[0].Action.ScreenshotFolder);
        Assert.False(editor.Tiles[0].CloseOverlayAfterLaunch);
        Assert.All(editor.Tiles, tile => Assert.False(tile.Action.TypeEditable));
        Assert.All(editor.Tiles.Skip(1).Take(6), tile => Assert.True(tile.CloseOverlayAfterLaunch));
        Assert.False(editor.Tiles[7].CloseOverlayAfterLaunch);

        var persistedIds = tiles.Select(tile => tile.TileId).ToArray();
        var restartedRuntime = new ShortcutRuntime(store);
        Assert.Equal(persistedIds, restartedRuntime.Capture().Tiles.Select(tile => tile.TileId));
        Assert.Equal(persistedIds, restartedRuntime.CaptureEditor().Tiles.Select(tile => tile.TileId));
    }

    [Fact]
    public void Existing_empty_document_is_not_seeded_or_rewritten()
    {
        var store = new ShortcutStore(ShortcutsPath);
        store.Save(new ShortcutDocument());
        var existingBytes = File.ReadAllBytes(ShortcutsPath);

        var runtime = new ShortcutRuntime(store);

        Assert.True(runtime.Capture().Available);
        Assert.Empty(runtime.Capture().Tiles);
        Assert.True(runtime.CaptureEditor().Available);
        Assert.Empty(runtime.CaptureEditor().Tiles);
        Assert.Equal(existingBytes, File.ReadAllBytes(ShortcutsPath));
    }

    [Fact]
    public void Existing_nonempty_document_is_not_seeded_or_rewritten()
    {
        var existing = Tile("My Shortcut", "future.vendor.action", new { value = 1 });
        Save([existing]);
        var existingBytes = File.ReadAllBytes(ShortcutsPath);

        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));

        Assert.Equal(existing.TileId, Assert.Single(runtime.Capture().Tiles).TileId);
        Assert.Equal(existing.TileId, Assert.Single(runtime.CaptureEditor().Tiles).TileId);
        Assert.Equal(existingBytes, File.ReadAllBytes(ShortcutsPath));
    }

    [Fact]
    public void Existing_Xbox_shortcut_titles_are_preserved_verbatim_without_rewriting_the_document()
    {
        var existingXbox = Tile("Xbox", ShortcutActionTypeIds.XboxApp, Parameters("{}"), closeOverlayAfterLaunch: true);
        var customXbox = Tile("My XbOx", ShortcutActionTypeIds.XboxApp, Parameters("{}"), closeOverlayAfterLaunch: true);
        Save([existingXbox, customXbox]);
        var existingBytes = File.ReadAllBytes(ShortcutsPath);

        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));
        Assert.Equal(
            new[] { (existingXbox.TileId, "Xbox"), (customXbox.TileId, "My XbOx") },
            runtime.Capture().Tiles.Select(tile => (tile.TileId, tile.Title)));
        Assert.Equal(
            new[] { (existingXbox.TileId, "Xbox"), (customXbox.TileId, "My XbOx") },
            runtime.CaptureEditor().Tiles.Select(tile => (tile.TileId, tile.Title)));

        var restartedRuntime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));
        Assert.Equal(
            new[] { (existingXbox.TileId, "Xbox"), (customXbox.TileId, "My XbOx") },
            restartedRuntime.Capture().Tiles.Select(tile => (tile.TileId, tile.Title)));
        Assert.Equal(
            new[] { (existingXbox.TileId, "Xbox"), (customXbox.TileId, "My XbOx") },
            restartedRuntime.CaptureEditor().Tiles.Select(tile => (tile.TileId, tile.Title)));
        Assert.Equal(existingBytes, File.ReadAllBytes(ShortcutsPath));
    }

    [Fact]
    public void Initial_seed_write_failure_makes_only_shortcuts_unavailable()
    {
        Directory.CreateDirectory(_testDirectory);
        var blockedDirectory = Path.Combine(_testDirectory, "not-a-directory");
        File.WriteAllText(blockedDirectory, "block");
        var shortcutsPath = Path.Combine(blockedDirectory, "shortcuts.json");

        var runtime = new ShortcutRuntime(new ShortcutStore(shortcutsPath));

        Assert.False(runtime.Capture().Available);
        Assert.False(runtime.CaptureEditor().Available);
        Assert.False(File.Exists(shortcutsPath));
    }

    [Fact]
    public void Unreadable_document_is_unavailable_and_preserved_without_seeding()
    {
        Directory.CreateDirectory(_testDirectory);
        const string original = "{\"schemaVersion\":2,\"dashboard\":{\"tiles\":[]}}";
        File.WriteAllText(ShortcutsPath, original);

        ShortcutRuntime runtime;
        using (new FileStream(ShortcutsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));
            Assert.False(runtime.Capture().Available);
            Assert.False(runtime.CaptureEditor().Available);
        }

        Assert.Equal(original, File.ReadAllText(ShortcutsPath));
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":{\"tiles\":[]}}")]
    public async Task Unsafe_persistence_disables_only_the_shortcut_runtime(string json)
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ShortcutsPath, json);
        var tileId = Guid.NewGuid();

        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));

        Assert.False(runtime.Capture().Available);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, (await runtime.ExecuteAsync(tileId)).Outcome);
        Assert.Equal("Shortcut storage is unavailable.", (await runtime.ExecuteAsync(tileId)).FailureMessage);
        var edit = runtime.MutateEditor(
            new FrontendShortcutMutationIntent(FrontendShortcutMutationKind.Create, Title: "New",
                Action: new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Url, Url: "https://example.com")));
        Assert.False(edit.Succeeded);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Runtime_loads_the_authoritative_document_once_and_preserves_order()
    {
        var firstTiles = Enumerable.Range(0, 6)
            .Select(index => Tile($"First {index}", "future.unknown", new { index }))
            .ToArray();
        Save(firstTiles);

        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));
        Save([Tile("Replacement", "future.replacement", new { value = 2 })]);

        var snapshot = runtime.Capture();

        Assert.Equal(firstTiles.Select(tile => tile.TileId), snapshot.Tiles.Select(tile => tile.TileId));
        Assert.Equal(firstTiles.Select(tile => tile.Title), snapshot.Tiles.Select(tile => tile.Title));
    }

    [Fact]
    public async Task Unknown_action_is_preserved_but_unavailable_and_not_executable()
    {
        var tile = Tile("Future", "future.vendor.action", new { value = 30 });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutIconCatalog.FallbackGlyph, projected.IconGlyph);
        Assert.False(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Unavailable, projected.State);
        Assert.Equal("Unsupported", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Parameterless_builtins_project_enabled_and_execute_via_the_existing_process_seam()
    {
        var tiles = new[]
        {
            Tile("Big Picture", ShortcutActionTypeIds.SteamBigPicture, Parameters("{}"), closeOverlayAfterLaunch: true),
            Tile("Steam", ShortcutActionTypeIds.SteamClient, Parameters("{}"), closeOverlayAfterLaunch: true),
            Tile("Xbox", ShortcutActionTypeIds.XboxApp, Parameters("{}"), closeOverlayAfterLaunch: true)
        };
        Save(tiles);
        var started = new List<ProcessStartInfo>();
        var runtime = CreateRuntime(info =>
        {
            started.Add(info);
            return new Process();
        });

        var projected = runtime.Capture().Tiles;
        var results = new List<ShortcutExecutionResult>();
        foreach (var tile in tiles)
            results.Add(await runtime.ExecuteAsync(tile.TileId));

        Assert.All(projected, tile =>
        {
            Assert.True(tile.Enabled);
            Assert.Equal(FrontendShortcutTileState.Neutral, tile.State);
            Assert.Null(tile.StatusText);
        });
        Assert.All(results, result => Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome));
        Assert.All(results, result => Assert.True(result.RetireOverlayAfterExecution));
        Assert.All(started, info =>
        {
            Assert.EndsWith("explorer.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.False(info.UseShellExecute);
        });
        Assert.Equal("steam://open/bigpicture", started[0].ArgumentList.Single());
        Assert.Equal("steam://open/main", started[1].ArgumentList.Single());
        Assert.Equal($"shell:AppsFolder\\{XboxGamingHomeAppIdentity.Aumid}", started[2].ArgumentList.Single());
    }

    [Fact]
    public async Task Keyboard_and_show_desktop_use_their_specific_command_seams()
    {
        var keyboard = Tile("Keyboard", ShortcutActionTypeIds.TouchKeyboard, Parameters("{}"));
        var desktop = Tile("Desktop", ShortcutActionTypeIds.ShowDesktop, Parameters("{}"));
        Save([keyboard, desktop]);
        var invoked = new List<string>();
        var runtime = CreateRuntime(
            toggleTouchKeyboard: () => { invoked.Add("keyboard"); return true; },
            showDesktop: () => { invoked.Add("desktop"); return true; });

        var snapshot = runtime.Capture();
        var keyboardResult = await runtime.ExecuteAsync(keyboard.TileId);
        var desktopResult = await runtime.ExecuteAsync(desktop.TileId);

        Assert.All(snapshot.Tiles, tile => Assert.True(tile.Enabled));
        Assert.Equal(["keyboard", "desktop"], invoked);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, keyboardResult.Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, desktopResult.Outcome);
        Assert.False(keyboardResult.RetireOverlayAfterExecution);
        Assert.False(desktopResult.RetireOverlayAfterExecution);

        var failedRuntime = CreateRuntime(toggleTouchKeyboard: () => false, showDesktop: () => false);
        Assert.Equal(ShortcutExecutionOutcome.Failed, (await failedRuntime.ExecuteAsync(keyboard.TileId)).Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Failed, (await failedRuntime.ExecuteAsync(desktop.TileId)).Outcome);
    }

    [Fact]
    public async Task Focus_sensitive_actions_retire_overlay_before_dispatch_when_close_is_enabled()
    {
        var keyboard = Tile("Keyboard", ShortcutActionTypeIds.TouchKeyboard, Parameters("{}"), closeOverlayAfterLaunch: true);
        var desktop = Tile("Desktop", ShortcutActionTypeIds.ShowDesktop, Parameters("{}"), closeOverlayAfterLaunch: true);
        var steamInput = Tile("Steam Input", ShortcutActionTypeIds.SteamInput, Parameters("{}"), closeOverlayAfterLaunch: true);
        Save([keyboard, desktop, steamInput]);
        var order = new List<string>();
        var launcher = new UserProcessLauncher((info, _) =>
        {
            order.Add(info.ArgumentList.Single());
            return true;
        });
        var runtime = CreateRuntime(
            userProcessLauncher: launcher,
            actualRunningAppIdSource: () => 730,
            toggleTouchKeyboard: () => { order.Add("keyboard"); return true; },
            showDesktop: () => { order.Add("desktop"); return true; },
            retireOverlayBeforeFocusSensitiveAction: _ =>
            {
                order.Add("retire");
                return Task.FromResult(true);
            });

        var results = new[]
        {
            await runtime.ExecuteAsync(keyboard.TileId),
            await runtime.ExecuteAsync(desktop.TileId),
            await runtime.ExecuteAsync(steamInput.TileId)
        };

        Assert.All(results, result => Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome));
        Assert.All(results, result => Assert.False(result.RetireOverlayAfterExecution));
        Assert.Equal(
            ["retire", "keyboard", "retire", "desktop", "retire", "steam://controllerconfig/730"],
            order);
    }

    [Fact]
    public async Task Steam_input_is_disabled_without_an_actual_running_app_and_uses_a_fresh_app_id()
    {
        var tile = Tile("Steam Input", ShortcutActionTypeIds.SteamInput, Parameters("{}"), closeOverlayAfterLaunch: true);
        Save([tile]);
        uint appId = 0;
        var launched = new List<string>();
        var retireCount = 0;
        var launcher = new UserProcessLauncher((info, _) =>
        {
            launched.Add(info.ArgumentList.Single());
            return true;
        });
        var runtime = CreateRuntime(
            userProcessLauncher: launcher,
            actualRunningAppIdSource: () => appId,
            retireOverlayBeforeFocusSensitiveAction: _ =>
            {
                retireCount++;
                return Task.FromResult(true);
            });

        var noGameTile = Assert.Single(runtime.Capture().Tiles);
        Assert.False(noGameTile.Enabled);
        Assert.Equal("No game running", noGameTile.StatusText);
        var noGameResult = await runtime.ExecuteAsync(tile.TileId);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, noGameResult.Outcome);
        Assert.Empty(launched);
        Assert.Equal(0, retireCount);

        appId = 1245620;
        Assert.True(Assert.Single(runtime.Capture().Tiles).Enabled);
        var runningGameResult = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, runningGameResult.Outcome);
        Assert.Equal(["steam://controllerconfig/1245620"], launched);
        Assert.Equal(1, retireCount);

        var failedLauncher = new UserProcessLauncher((_, _) => false);
        var failedLaunchRuntime = CreateRuntime(
            userProcessLauncher: failedLauncher,
            actualRunningAppIdSource: () => 730);
        var failedLaunch = await failedLaunchRuntime.ExecuteAsync(tile.TileId);
        Assert.Equal(ShortcutExecutionOutcome.Failed, failedLaunch.Outcome);
        Assert.False(failedLaunch.RetireOverlayAfterExecution);
    }

    [Fact]
    public async Task Clawhud_shortcut_toggles_the_canonical_desired_state_and_reports_runtime_state()
    {
        var tile = Tile("HUD", ShortcutActionTypeIds.ClawHudToggle, Parameters("{}"));
        Save([tile]);
        var current = new FrontendClawHudSnapshot(false, FrontendClawHudRuntimeState.Disabled, "Off", null, null, null);
        var requestedStates = new List<bool>();
        var runtime = CreateRuntime(
            clawHudSnapshotSource: () => current,
            setClawHudEnabled: (enabled, _) =>
            {
                requestedStates.Add(enabled);
                current = new FrontendClawHudSnapshot(enabled,
                    enabled ? FrontendClawHudRuntimeState.Ready : FrontendClawHudRuntimeState.Disabled,
                    enabled ? "Ready" : "Off", null, null, null);
                return Task.FromResult(current);
            });

        var initially = Assert.Single(runtime.Capture().Tiles);
        Assert.Equal(FrontendShortcutTileState.Inactive, initially.State);
        Assert.Equal("Off", initially.StatusText);
        Assert.True(initially.Enabled);

        var enabled = await runtime.ExecuteAsync(tile.TileId);
        var onTile = Assert.Single(runtime.Capture().Tiles);
        var disabled = await runtime.ExecuteAsync(tile.TileId);
        var offTile = Assert.Single(runtime.Capture().Tiles);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, enabled.Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, disabled.Outcome);
        Assert.Equal([true, false], requestedStates);
        Assert.Equal(FrontendShortcutTileState.Active, onTile.State);
        Assert.Equal("On", onTile.StatusText);
        Assert.Equal(FrontendShortcutTileState.Inactive, offTile.State);
        Assert.Equal("Off", offTile.StatusText);
        Assert.False(enabled.RetireOverlayAfterExecution);
        Assert.False(disabled.RetireOverlayAfterExecution);
    }

    [Fact]
    public async Task Failed_clawhud_enable_does_not_report_a_ready_success()
    {
        var tile = Tile("HUD", ShortcutActionTypeIds.ClawHudToggle, Parameters("{}"));
        Save([tile]);
        var requested = 0;
        var runtime = CreateRuntime(
            clawHudSnapshotSource: () => new(false, FrontendClawHudRuntimeState.Disabled, "Off", null, null, null),
            setClawHudEnabled: (enabled, _) =>
            {
                requested++;
                return Task.FromResult(new FrontendClawHudSnapshot(enabled,
                    FrontendClawHudRuntimeState.Unavailable, "Unavailable", null, null, null));
            });

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(1, requested);
        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.Contains("could not be changed", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, false, "")]
    [InlineData(1, false, "0x5B:up")]
    [InlineData(2, false, "0x44:up,0x5B:up")]
    [InlineData(3, false, "0x5B:up")]
    [InlineData(4, true, "")]
    public void Show_desktop_sequence_is_symmetric_and_compensates_partial_send(
        uint acceptedEvents,
        bool expectedSuccess,
        string expectedReleaseEvents)
    {
        var calls = new List<ShortcutKeyboardInput[]>();
        var result = WindowsShortcutActionCommands.SendWinD(events =>
        {
            calls.Add(events);
            return calls.Count == 1 ? acceptedEvents : (uint)events.Length;
        });

        Assert.Equal(expectedSuccess, result);
        Assert.Equal(new ushort[] { 0x5B, 0x44, 0x44, 0x5B }, calls[0].Select(input => input.VirtualKey));
        Assert.Equal(new[] { false, false, true, true }, calls[0].Select(input => input.KeyUp));
        var releases = calls.Count > 1
            ? string.Join(',', calls[1].Select(input => $"0x{input.VirtualKey:X2}:{(input.KeyUp ? "up" : "down")}"))
            : string.Empty;
        Assert.Equal(expectedReleaseEvents, releases);
        Assert.Equal(acceptedEvents is >= 1 and <= 3 ? 2 : 1, calls.Count);
    }

    [Fact]
    public void Show_desktop_native_input_matches_the_windows_input_abi_size()
    {
        Assert.True(WindowsShortcutActionCommands.NativeInputLayoutIsExpectedForTests());
    }

    [Fact]
    public void Show_desktop_retries_only_the_unaccepted_compensating_key_up_events()
    {
        var calls = new List<ShortcutKeyboardInput[]>();
        var result = WindowsShortcutActionCommands.SendWinD(events =>
        {
            calls.Add(events);
            return calls.Count switch
            {
                1 => 2,
                2 => 0,
                _ => (uint)events.Length,
            };
        });

        Assert.False(result);
        Assert.Equal(3, calls.Count);
        Assert.Equal(new ushort[] { 0x44, 0x5B }, calls[1].Select(input => input.VirtualKey));
        Assert.All(calls[1], input => Assert.True(input.KeyUp));
        Assert.Equal(new ushort[] { 0x44, 0x5B }, calls[2].Select(input => input.VirtualKey));
        Assert.All(calls[2], input => Assert.True(input.KeyUp));
    }

    [Fact]
    public async Task Successful_supported_launch_actions_honor_the_persisted_close_preference()
    {
        var actionCases = new (string TypeId, string Parameters)[]
        {
            (ShortcutActionTypeIds.Executable, "{\"path\":\"C:\\\\Tools\\\\Tool.exe\",\"arguments\":\"\"}"),
            (ShortcutActionTypeIds.PowerShell, "{\"script\":\"Write-Output 1\"}"),
            (ShortcutActionTypeIds.Url, "{\"url\":\"https://example.com\"}"),
            (ShortcutActionTypeIds.SteamBigPicture, "{}"),
            (ShortcutActionTypeIds.SteamClient, "{}"),
            (ShortcutActionTypeIds.XboxApp, "{}")
        };
        var tiles = actionCases.SelectMany(action => new[]
        {
            Tile("Close", action.TypeId, Parameters(action.Parameters), closeOverlayAfterLaunch: true),
            Tile("Keep open", action.TypeId, Parameters(action.Parameters), closeOverlayAfterLaunch: false)
        }).ToArray();
        Save(tiles);
        var runtime = CreateRuntime(_ => new Process(), _ => true);

        foreach (var (tile, index) in tiles.Select((tile, index) => (tile, index)))
        {
            var result = await runtime.ExecuteAsync(tile.TileId);
            Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
            Assert.Equal(index % 2 == 0, result.RetireOverlayAfterExecution);
        }
    }

    [Fact]
    public async Task Failed_launch_never_requests_overlay_retirement_even_when_enabled()
    {
        var tile = Tile("Missing", ShortcutActionTypeIds.Executable,
            Parameters("{\"path\":\"C:\\\\Missing\\\\Tool.exe\",\"arguments\":\"\"}"),
            closeOverlayAfterLaunch: true);
        Save([tile]);
        var runtime = CreateRuntime(_ => new Process(), _ => false);

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.False(result.RetireOverlayAfterExecution);
    }

    [Theory]
    [InlineData("system.steam-big-picture", "{\"unexpected\":true}")]
    [InlineData("system.steam-big-picture", "{\"unexpected\":[]}")]
    [InlineData("system.steam-client", "{\"unexpected\":true}")]
    [InlineData("system.steam-client", "{\"unexpected\":[]}")]
    [InlineData("system.xbox-app", "{\"unexpected\":true}")]
    [InlineData("system.xbox-app", "{\"unexpected\":[]}")]
    public async Task Parameterless_builtins_with_parameters_are_disabled_and_not_executed(string typeId, string parametersJson)
    {
        var tile = Tile("Invalid built-in", typeId, Parameters(parametersJson));
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ =>
        {
            launchCount++;
            return new Process();
        });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Unavailable, projected.State);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Unknown_tile_id_returns_not_found_without_launch()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var result = await runtime.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(ShortcutExecutionOutcome.NotFound, result.Outcome);
        Assert.Equal("Shortcut tile was not found.", result.FailureMessage);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Known_action_with_unsupported_schema_is_preserved_but_not_executable()
    {
        var tile = Tile("Future schema", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" }, schemaVersion: 2);
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal("Unsupported", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Theory]
    [InlineData("system.executable", "{\"path\":\"\"}")]
    [InlineData("system.executable", "{\"path\":\"relative.exe\"}")]
    [InlineData("system.executable", "{\"path\":\"C:\\\\Tools\\\\Tool.txt\"}")]
    [InlineData("system.executable", "{\"path\":42}")]
    [InlineData("system.executable", "{\"path\":\"C:\\\\Tools\\\\Tool.exe\",\"arguments\":42}")]
    [InlineData("system.powershell", "{\"script\":\" \"}")]
    [InlineData("system.powershell", "{\"script\":42}")]
    [InlineData("system.url", "{\"url\":\"relative/path\"}")]
    [InlineData("system.url", "{\"url\":\"file://example.com/a\"}")]
    [InlineData("system.url", "{\"url\":\"https:///missing-host\"}")]
    [InlineData("system.url", "{\"url\":42}")]
    public async Task Invalid_known_parameters_remain_visible_but_disabled(string typeId, string parametersJson)
    {
        var tile = Tile("Invalid", typeId, Parameters(parametersJson));
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(runtime.Capture().Available);
        Assert.False(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Unavailable, projected.State);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Executable_launch_preserves_arguments_and_uses_executable_directory()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable, new
        {
            path = @"C:\Tools\Tool.exe",
            arguments = "--foo \"bar baz\""
        });
        Save([tile]);
        ProcessStartInfo? captured = null;
        var launchCount = 0;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            launchCount++;
            return new Process();
        }, _ => true);

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, launchCount);
        Assert.NotNull(captured);
        Assert.Equal(@"C:\Tools\Tool.exe", captured!.FileName);
        Assert.Equal("--foo \"bar baz\"", captured.Arguments);
        Assert.False(captured.UseShellExecute);
        Assert.Equal(@"C:\Tools", captured.WorkingDirectory);
    }

    [Fact]
    public async Task Executable_null_arguments_are_treated_as_empty_and_launch_successfully()
    {
        var tile = Tile(
            "Tool without arguments",
            ShortcutActionTypeIds.Executable,
            Parameters("{\"path\":\"C:\\\\Tools\\\\Tool.exe\",\"arguments\":null}"));
        Save([tile]);
        ProcessStartInfo? captured = null;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            return new Process();
        }, _ => true);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Neutral, projected.State);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(string.Empty, captured!.Arguments);
    }

    [Fact]
    public async Task Persisted_oversized_executable_is_preserved_repairable_and_never_launched()
    {
        var tile = Tile("Large executable", ShortcutActionTypeIds.Executable, new
        {
            path = @"C:\Tools\Tool.exe",
            arguments = new string('x', 30_100)
        });
        Save([tile]);
        var before = File.ReadAllText(ShortcutsPath);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var dashboardTile = Assert.Single(runtime.Capture().Tiles);
        var editorAction = Assert.Single(runtime.CaptureEditor().Tiles).Action;
        var execution = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(runtime.Capture().Available);
        Assert.False(dashboardTile.Enabled);
        Assert.Equal("Invalid configuration", dashboardTile.StatusText);
        Assert.True(editorAction.Editable);
        Assert.False(editorAction.ConfigurationValid);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, execution.Outcome);
        Assert.Equal(0, launchCount);
        Assert.Equal(before, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public async Task Persisted_oversized_powershell_is_preserved_repairable_and_never_launched()
    {
        var tile = Tile("Large PowerShell", ShortcutActionTypeIds.PowerShell,
            new { script = new string('x', 20_000) });
        Save([tile]);
        var before = File.ReadAllText(ShortcutsPath);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); });

        var dashboardTile = Assert.Single(runtime.Capture().Tiles);
        var editorAction = Assert.Single(runtime.CaptureEditor().Tiles).Action;
        var execution = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(runtime.Capture().Available);
        Assert.False(dashboardTile.Enabled);
        Assert.Equal("Invalid configuration", dashboardTile.StatusText);
        Assert.True(editorAction.Editable);
        Assert.False(editorAction.ConfigurationValid);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, execution.Outcome);
        Assert.Equal(0, launchCount);
        Assert.Equal(before, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public async Task Executable_missing_at_projection_is_unavailable_without_launch()
    {
        var tile = Tile("Missing", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => false);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal("Not found", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Executable_is_rechecked_when_it_disappears_before_launch()
    {
        var tile = Tile("Drifting tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var fileExistsCalls = 0;
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => ++fileExistsCalls == 1);

        Assert.True(Assert.Single(runtime.Capture().Tiles).Enabled);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.Equal(2, fileExistsCalls);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task PowerShell_uses_deterministic_windows_path_and_utf16le_encoded_script()
    {
        const string script = "Write-Output \"한글 \\\"quoted\\\"\"";
        var tile = Tile("PowerShell", ShortcutActionTypeIds.PowerShell, new { script });
        Save([tile]);
        ProcessStartInfo? captured = null;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            return new Process();
        });

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            captured!.FileName);
        Assert.False(captured.UseShellExecute);
        Assert.True(captured.CreateNoWindow);
        Assert.Equal(
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand"],
            captured.ArgumentList.Take(6));
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(captured.ArgumentList[6])));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("HTTPS://example.com")]
    public async Task Http_and_https_urls_use_shell_resolution(string url)
    {
        var tile = Tile("Web", ShortcutActionTypeIds.Url, new { url });
        Save([tile]);
        ProcessStartInfo? captured = null;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            return new Process();
        });

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        Assert.EndsWith("rundll32.exe", captured!.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"url.dll,FileProtocolHandler \"{new Uri(url).AbsoluteUri}\"", captured.Arguments);
        Assert.Empty(captured.ArgumentList);
        Assert.True(captured.CreateNoWindow);
        Assert.False(captured.UseShellExecute);
    }

    [Theory]
    [InlineData("file://example.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("steam://open/mainmenu")]
    [InlineData("relative/path")]
    [InlineData("https:///missing-host")]
    public async Task Non_http_urls_are_rejected_without_launch(string url)
    {
        var tile = Tile("Web", ShortcutActionTypeIds.Url, new { url });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Launch_failures_use_fixed_outcomes_and_safe_messages()
    {
        var exceptions = new (Exception Exception, ShortcutExecutionOutcome Outcome)[]
        {
            (new FileNotFoundException("secret path C:\\private\\Tool.exe"), ShortcutExecutionOutcome.Unavailable),
            (new DirectoryNotFoundException("secret directory C:\\private"), ShortcutExecutionOutcome.Unavailable),
            (new UnauthorizedAccessException("secret arguments --private"), ShortcutExecutionOutcome.Failed),
            (new Win32Exception(5, "secret process details"), ShortcutExecutionOutcome.Failed),
            (new InvalidOperationException("secret URL https://private.example"), ShortcutExecutionOutcome.Failed),
            (new Exception("secret script Write-Output"), ShortcutExecutionOutcome.Failed),
        };

        foreach (var (exception, expectedOutcome) in exceptions)
        {
            var tile = Tile("Launch", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
            Save([tile]);
            var runtime = CreateRuntime(_ => throw exception, _ => true);

            var result = await runtime.ExecuteAsync(tile.TileId);

            Assert.Equal(expectedOutcome, result.Outcome);
            Assert.DoesNotContain("secret", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(runtime.Capture().Available);
        }

        var nullTile = Tile("Null", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([nullTile]);
        var nullRuntime = CreateRuntime(_ => null, _ => true);
        var nullResult = await nullRuntime.ExecuteAsync(nullTile.TileId);
        Assert.Equal(ShortcutExecutionOutcome.Failed, nullResult.Outcome);
        Assert.Equal("Shortcut could not be launched.", nullResult.FailureMessage);
    }

    [Fact]
    public async Task Already_cancelled_execution_does_not_invoke_launch_delegate()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ExecuteAsync(tile.TileId, cancellation.Token));
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Host_composes_shortcut_runtime_as_an_independent_sibling_capability()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));

        Assert.Contains("private readonly ShortcutStore _shortcutStore", source, StringComparison.Ordinal);
        Assert.Contains("private readonly ShortcutRuntime _shortcutRuntime", source, StringComparison.Ordinal);
        Assert.Contains("AddonDataPaths.ShortcutsPath", source, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(testOnlyDataRoot, \"shortcuts.json\")", source, StringComparison.Ordinal);
        Assert.Contains("userProcessLauncher: _userProcessLauncher", source, StringComparison.Ordinal);
        Assert.Contains("launchBigPictureOverride: () => CenterM.Oem1BigPictureLauncher.Launch(_userProcessLauncher)", source, StringComparison.Ordinal);
        Assert.Contains("launchXboxAppOverride: () => CenterM.FrontButtonXboxAppLauncher.Launch(_userProcessLauncher)", source, StringComparison.Ordinal);
        Assert.Contains("launchApplicationOverride: application => CenterM.Oem1ApplicationLauncher.Launch(application, _userProcessLauncher)", source, StringComparison.Ordinal);
        Assert.Contains("_shortcutRuntime = new(_shortcutStore, screenshotAction: ExecuteFullscreenScreenshotShortcutAsync,", source, StringComparison.Ordinal);
        Assert.Contains("retireOverlayBeforeFocusSensitiveAction: RetireOverlayBeforeFocusSensitiveShortcutAsync", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudSnapshotSource", source, StringComparison.OrdinalIgnoreCase);
        var focusRetirementStart = source.IndexOf("private async Task<bool> RetireOverlayBeforeFocusSensitiveShortcutAsync", StringComparison.Ordinal);
        var focusRetirementEnd = source.IndexOf("private async Task<QuickSettingsMutationResult> HandleOverlayQuickSettingsMutationAsync", focusRetirementStart, StringComparison.Ordinal);
        Assert.True(focusRetirementStart >= 0 && focusRetirementEnd > focusRetirementStart);
        var focusRetirement = source[focusRetirementStart..focusRetirementEnd];
        Assert.Contains("_visibleSurfaceTransition.WaitAsync(token)", focusRetirement, StringComparison.Ordinal);
        Assert.Contains("RetireOverlayCaptureUnderTransitionAsync", focusRetirement, StringComparison.Ordinal);
        Assert.Contains("\"ShortcutFocusHandoff\"", focusRetirement, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_shortcutRuntime = new(_shortcutStore, screenshotAction: ExecuteFullscreenScreenshotShortcutAsync,", StringComparison.Ordinal)
            < source.IndexOf("AddonRuntimeCompositionFactory.Create(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Screenshot_projection_and_execution_use_the_async_callback_once()
    {
        var tile = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters("{}"));
        Save([tile]);
        var callbackCount = 0;
        string? callbackFolder = "not-called";
        var callbackResult = new ShortcutExecutionResult(ShortcutExecutionOutcome.Succeeded,
            RetireOverlayAfterExecution: true);
        var runtime = CreateRuntime(screenshotAction: (folder, _) =>
        {
            callbackCount++;
            callbackFolder = folder;
            return Task.FromResult(callbackResult);
        });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Neutral, projected.State);
        Assert.Null(projected.StatusText);
        Assert.Equal(1, callbackCount);
        Assert.Null(callbackFolder);
        Assert.NotSame(callbackResult, result);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.False(result.RetireOverlayAfterExecution);
    }

    [Fact]
    public async Task Screenshot_custom_folder_is_projected_and_passed_to_the_capture_callback()
    {
        const string folder = @"C:\Captures";
        var tile = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen,
            Parameters("{\"folder\":\"C:\\\\Captures\"}"));
        Save([tile]);
        string? callbackFolder = null;
        var runtime = CreateRuntime(screenshotAction: (configuredFolder, _) =>
        {
            callbackFolder = configuredFolder;
            return Task.FromResult(new ShortcutExecutionResult(ShortcutExecutionOutcome.Succeeded));
        });

        var editorAction = Assert.Single(runtime.CaptureEditor().Tiles).Action;
        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.True(editorAction.ConfigurationValid);
        Assert.Equal(folder, editorAction.ScreenshotFolder);
        Assert.True(projected.Enabled);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(folder, callbackFolder);
    }

    [Theory]
    [InlineData("{\"folder\":\"relative\"}")]
    [InlineData("{\"folder\":\"   \"}")]
    [InlineData("{\"folder\":\"C:\\\\Temp\",\"extra\":true}")]
    public async Task Screenshot_schema_one_rejects_invalid_custom_parameters_without_callback(string json)
    {
        var tile = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters(json));
        Save([tile]);
        var callbackCount = 0;
        var runtime = CreateRuntime(screenshotAction: (_, _) =>
        {
            callbackCount++;
            return Task.FromResult(new ShortcutExecutionResult(ShortcutExecutionOutcome.Succeeded));
        });

        var editorAction = Assert.Single(runtime.CaptureEditor().Tiles).Action;
        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.False(editorAction.ConfigurationValid);
        Assert.False(projected.Enabled);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task Screenshot_unsupported_schema_and_missing_callback_are_unavailable()
    {
        var unsupported = Tile("Future screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters("{}"), schemaVersion: 2);
        var withoutCallback = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters("{}"));
        Save([unsupported, withoutCallback]);
        var runtime = CreateRuntime();

        var projected = runtime.Capture().Tiles;
        var unsupportedResult = await runtime.ExecuteAsync(unsupported.TileId);
        var unavailableResult = await runtime.ExecuteAsync(withoutCallback.TileId);

        Assert.Equal("Unsupported", projected[0].StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unsupported, unsupportedResult.Outcome);
        Assert.False(projected[1].Enabled);
        Assert.Equal("Unavailable", projected[1].StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, unavailableResult.Outcome);
    }

    [Fact]
    public async Task Already_cancelled_screenshot_does_not_invoke_callback()
    {
        var tile = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters("{}"));
        Save([tile]);
        var callbackCount = 0;
        var runtime = CreateRuntime(screenshotAction: (_, _) =>
        {
            callbackCount++;
            return Task.FromResult(new ShortcutExecutionResult(ShortcutExecutionOutcome.Succeeded));
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ExecuteAsync(tile.TileId, cancellation.Token));
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task Screenshot_failure_does_not_poison_later_external_actions()
    {
        var screenshot = Tile("Screenshot", ShortcutActionTypeIds.ScreenshotFullscreen, Parameters("{}"));
        var executable = Tile("Tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        var url = Tile("Web", ShortcutActionTypeIds.Url, new { url = "https://example.com" });
        Save([screenshot, executable, url]);
        var launchCount = 0;
        var runtime = CreateRuntime(
            _ => { launchCount++; return new Process(); },
            _ => true,
            (_, _) => Task.FromResult(new ShortcutExecutionResult(ShortcutExecutionOutcome.Failed, "Screenshot could not be saved.")));

        Assert.Equal(ShortcutExecutionOutcome.Failed, (await runtime.ExecuteAsync(screenshot.TileId)).Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(executable.TileId)).Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(url.TileId)).Outcome);
        Assert.Equal(2, launchCount);
    }

    private ShortcutRuntime CreateRuntime(
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<string, bool>? fileExists = null,
        Func<string?, CancellationToken, Task<ShortcutExecutionResult>>? screenshotAction = null,
        UserProcessLauncher? userProcessLauncher = null,
        Func<uint>? actualRunningAppIdSource = null,
        Func<FrontendClawHudSnapshot>? clawHudSnapshotSource = null,
        Func<bool, CancellationToken, Task<FrontendClawHudSnapshot>>? setClawHudEnabled = null,
        Func<bool>? toggleTouchKeyboard = null,
        Func<bool>? showDesktop = null,
        Func<CancellationToken, Task<bool>>? retireOverlayBeforeFocusSensitiveAction = null) =>
        new(new ShortcutStore(ShortcutsPath), startProcess, fileExists, screenshotAction,
            userProcessLauncher: userProcessLauncher,
            actualRunningAppIdSource: actualRunningAppIdSource,
            clawHudSnapshotSource: clawHudSnapshotSource,
            setClawHudEnabled: setClawHudEnabled,
            toggleTouchKeyboard: toggleTouchKeyboard,
            showDesktop: showDesktop,
            retireOverlayBeforeFocusSensitiveAction: retireOverlayBeforeFocusSensitiveAction);

    private void Save(IReadOnlyList<ShortcutTileDefinition> tiles) =>
        new ShortcutStore(ShortcutsPath).Save(new ShortcutDocument
        {
            Dashboard = new ShortcutDashboardDefinition(tiles)
        });

    private static ShortcutTileDefinition Tile(
        string title,
        string typeId,
        object parameters,
        int schemaVersion = 1,
        bool closeOverlayAfterLaunch = false) =>
        Tile(title, typeId, JsonSerializer.SerializeToElement(parameters), schemaVersion, closeOverlayAfterLaunch);

    private static ShortcutTileDefinition Tile(
        string title,
        string typeId,
        JsonElement parameters,
        int schemaVersion = 1,
        bool closeOverlayAfterLaunch = false) =>
        new(Guid.NewGuid(), title, closeOverlayAfterLaunch, new ShortcutActionSpec(typeId, schemaVersion, parameters));

    private static JsonElement Parameters(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; the isolated GUID directory is safe to leave behind if a test
            // process still has a diagnostic handle open.
        }
    }
}
