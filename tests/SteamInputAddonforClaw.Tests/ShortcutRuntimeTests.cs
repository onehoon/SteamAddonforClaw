using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutRuntimeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SteamInputAddonforClaw.ShortcutRuntime.Tests.{Guid.NewGuid():N}");

    private string ShortcutsPath => Path.Combine(_testDirectory, "shortcuts.json");

    [Fact]
    public void Missing_document_is_an_available_empty_dashboard()
    {
        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));

        var snapshot = runtime.Capture();

        Assert.True(snapshot.Available);
        Assert.Empty(snapshot.Tiles);
        Assert.False(File.Exists(ShortcutsPath));
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("{\"schemaVersion\":2,\"dashboard\":{\"tiles\":[]}}")]
    public void Unsafe_persistence_disables_only_the_shortcut_runtime(string json)
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ShortcutsPath, json);
        var tileId = Guid.NewGuid();

        var runtime = new ShortcutRuntime(new ShortcutStore(ShortcutsPath));

        Assert.False(runtime.Capture().Available);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, runtime.Execute(tileId).Outcome);
        Assert.Equal("Shortcut storage is unavailable.", runtime.Execute(tileId).FailureMessage);
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
    public void Unknown_action_is_preserved_but_unavailable_and_not_executable()
    {
        var tile = Tile("Future", "future.vendor.action", new { value = 30 });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = runtime.Execute(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Unavailable, projected.State);
        Assert.Equal("Unsupported", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Unknown_tile_id_returns_not_found_without_launch()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var result = runtime.Execute(Guid.NewGuid());

        Assert.Equal(ShortcutExecutionOutcome.NotFound, result.Outcome);
        Assert.Equal("Shortcut tile was not found.", result.FailureMessage);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Known_action_with_unsupported_schema_is_preserved_but_not_executable()
    {
        var tile = Tile("Future schema", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" }, schemaVersion: 2);
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = runtime.Execute(tile.TileId);

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
    public void Invalid_known_parameters_remain_visible_but_disabled(string typeId, string parametersJson)
    {
        var tile = Tile("Invalid", typeId, Parameters(parametersJson));
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = runtime.Execute(tile.TileId);

        Assert.True(runtime.Capture().Available);
        Assert.False(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Unavailable, projected.State);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Executable_launch_preserves_arguments_and_uses_executable_directory()
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

        var result = runtime.Execute(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, launchCount);
        Assert.NotNull(captured);
        Assert.Equal(@"C:\Tools\Tool.exe", captured!.FileName);
        Assert.Equal("--foo \"bar baz\"", captured.Arguments);
        Assert.False(captured.UseShellExecute);
        Assert.Equal(@"C:\Tools", captured.WorkingDirectory);
    }

    [Fact]
    public void Executable_null_arguments_are_treated_as_empty_and_launch_successfully()
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
        var result = runtime.Execute(tile.TileId);

        Assert.True(projected.Enabled);
        Assert.Equal(FrontendShortcutTileState.Neutral, projected.State);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(string.Empty, captured!.Arguments);
    }

    [Fact]
    public void Executable_missing_at_projection_is_unavailable_without_launch()
    {
        var tile = Tile("Missing", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => false);

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = runtime.Execute(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal("Not found", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Executable_is_rechecked_when_it_disappears_before_launch()
    {
        var tile = Tile("Drifting tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var fileExistsCalls = 0;
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => ++fileExistsCalls == 1);

        Assert.True(Assert.Single(runtime.Capture().Tiles).Enabled);
        var result = runtime.Execute(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Unavailable, result.Outcome);
        Assert.Equal(2, fileExistsCalls);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void PowerShell_uses_deterministic_windows_path_and_utf16le_encoded_script()
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

        var result = runtime.Execute(tile.TileId);

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
    public void Http_and_https_urls_use_shell_resolution(string url)
    {
        var tile = Tile("Web", ShortcutActionTypeIds.Url, new { url });
        Save([tile]);
        ProcessStartInfo? captured = null;
        var runtime = CreateRuntime(info =>
        {
            captured = info;
            return new Process();
        });

        var result = runtime.Execute(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(url, captured!.FileName);
        Assert.True(captured.UseShellExecute);
    }

    [Theory]
    [InlineData("file://example.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("steam://open/mainmenu")]
    [InlineData("relative/path")]
    [InlineData("https:///missing-host")]
    public void Non_http_urls_are_rejected_without_launch(string url)
    {
        var tile = Tile("Web", ShortcutActionTypeIds.Url, new { url });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); });

        var projected = Assert.Single(runtime.Capture().Tiles);
        var result = runtime.Execute(tile.TileId);

        Assert.False(projected.Enabled);
        Assert.Equal("Invalid configuration", projected.StatusText);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, result.Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public void Launch_failures_use_fixed_outcomes_and_safe_messages()
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

            var result = runtime.Execute(tile.TileId);

            Assert.Equal(expectedOutcome, result.Outcome);
            Assert.DoesNotContain("secret", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(runtime.Capture().Available);
        }

        var nullTile = Tile("Null", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([nullTile]);
        var nullRuntime = CreateRuntime(_ => null, _ => true);
        var nullResult = nullRuntime.Execute(nullTile.TileId);
        Assert.Equal(ShortcutExecutionOutcome.Failed, nullResult.Outcome);
        Assert.Equal("Shortcut could not be launched.", nullResult.FailureMessage);
    }

    [Fact]
    public void Already_cancelled_execution_does_not_invoke_launch_delegate()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable, new { path = @"C:\Tools\Tool.exe" });
        Save([tile]);
        var launchCount = 0;
        var runtime = CreateRuntime(_ => { launchCount++; return new Process(); }, _ => true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => runtime.Execute(tile.TileId, cancellation.Token));
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
        Assert.Contains("_shortcutRuntime = new(_shortcutStore);", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_shortcutRuntime = new(_shortcutStore);", StringComparison.Ordinal)
            < source.IndexOf("AddonRuntimeCompositionFactory.Create(", StringComparison.Ordinal));
    }

    private ShortcutRuntime CreateRuntime(
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<string, bool>? fileExists = null) =>
        new(new ShortcutStore(ShortcutsPath), startProcess, fileExists);

    private void Save(IReadOnlyList<ShortcutTileDefinition> tiles) =>
        new ShortcutStore(ShortcutsPath).Save(new ShortcutDocument
        {
            Dashboard = new ShortcutDashboardDefinition(tiles)
        });

    private static ShortcutTileDefinition Tile(
        string title,
        string typeId,
        object parameters,
        int schemaVersion = 1) =>
        Tile(title, typeId, JsonSerializer.SerializeToElement(parameters), schemaVersion);

    private static ShortcutTileDefinition Tile(
        string title,
        string typeId,
        JsonElement parameters,
        int schemaVersion = 1) =>
        new(Guid.NewGuid(), title, new ShortcutActionSpec(typeId, schemaVersion, parameters));

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
