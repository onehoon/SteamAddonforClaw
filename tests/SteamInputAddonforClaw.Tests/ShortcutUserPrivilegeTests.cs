using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutUserPrivilegeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ShortcutUserPrivilegeTests.{Guid.NewGuid():N}");
    private string StorePath => Path.Combine(_directory, "shortcuts.json");

    [Fact]
    public async Task Legacy_exe_and_powershell_actions_default_to_medium_and_remain_editable()
    {
        var executable = Tile("Legacy EXE", ShortcutActionTypeIds.Executable,
            """{"path":"C:\\Tools\\Tool.exe","arguments":""}""");
        var powerShell = Tile("Legacy PowerShell", ShortcutActionTypeIds.PowerShell,
            """{"script":"Write-Output 1"}""");
        Save(executable, powerShell);
        var modes = new List<bool>();
        var runtime = CreateRuntime(new UserProcessLauncher((_, runAsAdministrator) =>
        {
            modes.Add(runAsAdministrator);
            return true;
        }), fileExists: _ => true);

        var actions = runtime.CaptureEditor().Tiles.Select(tile => tile.Action).ToArray();
        Assert.All(actions, action =>
        {
            Assert.True(action.ConfigurationValid);
            Assert.False(action.RunAsAdministrator);
        });
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(executable.TileId)).Outcome);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(powerShell.TileId)).Outcome);
        Assert.Equal([false, false], modes);
    }

    [Fact]
    public async Task Exe_and_powershell_privilege_choices_round_trip_and_control_launch_mode()
    {
        var modes = new List<bool>();
        var runtime = CreateRuntime(new UserProcessLauncher((_, runAsAdministrator) =>
        {
            modes.Add(runAsAdministrator);
            return true;
        }), fileExists: _ => true);

        var created = runtime.MutateEditor(new(FrontendShortcutMutationKind.Create,
            Title: "Admin EXE",
            Action: new(FrontendShortcutEditorActionKind.Executable, ExecutablePath: @"C:\Tools\Tool.exe",
                ExecutableArguments: "--example", RunAsAdministrator: true),
            CloseOverlayAfterLaunch: true));
        Assert.True(created.Succeeded);
        var executable = Assert.Single(new ShortcutStore(StorePath).Load().Document.Dashboard.Tiles);
        Assert.True(executable.Action.Parameters.GetProperty("runAsAdministrator").GetBoolean());
        Assert.True(Assert.Single(created.Snapshot.Tiles).Action.RunAsAdministrator);
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(executable.TileId)).Outcome);
        Assert.True(modes[^1]);

        var updated = runtime.MutateEditor(new(FrontendShortcutMutationKind.Update, executable.TileId,
            "Admin EXE",
            new(FrontendShortcutEditorActionKind.Executable, ExecutablePath: @"C:\Tools\Tool.exe",
                ExecutableArguments: "--example", RunAsAdministrator: false),
            CloseOverlayAfterLaunch: true));
        Assert.True(updated.Succeeded);
        Assert.False(Assert.Single(updated.Snapshot.Tiles).Action.RunAsAdministrator);
        Assert.False(new ShortcutStore(StorePath).Load().Document.Dashboard.Tiles[0]
            .Action.Parameters.GetProperty("runAsAdministrator").GetBoolean());
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(executable.TileId)).Outcome);
        Assert.False(modes[^1]);

        var powerShellCreated = runtime.MutateEditor(new(FrontendShortcutMutationKind.Create,
            Title: "Admin PowerShell",
            Action: new(FrontendShortcutEditorActionKind.PowerShell, PowerShellScript: "Write-Output 1",
                RunAsAdministrator: true),
            CloseOverlayAfterLaunch: false));
        Assert.True(powerShellCreated.Succeeded);
        var powerShell = new ShortcutStore(StorePath).Load().Document.Dashboard.Tiles[1];
        Assert.True(powerShell.Action.Parameters.GetProperty("runAsAdministrator").GetBoolean());
        Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(powerShell.TileId)).Outcome);
        Assert.True(modes[^1]);
        Assert.Equal([true, false, true], modes);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Wrong_typed_privilege_values_are_invalid_and_never_launched(string value)
    {
        var executable = Tile("Invalid EXE", ShortcutActionTypeIds.Executable,
            $"{{\"path\":\"C:\\\\Tools\\\\Tool.exe\",\"runAsAdministrator\":{value}}}");
        var powerShell = Tile("Invalid PowerShell", ShortcutActionTypeIds.PowerShell,
            $"{{\"script\":\"Write-Output 1\",\"runAsAdministrator\":{value}}}");
        Save(executable, powerShell);
        var launchCount = 0;
        var runtime = CreateRuntime(new UserProcessLauncher((_, _) => { launchCount++; return true; }));

        Assert.All(runtime.Capture().Tiles, tile => Assert.False(tile.Enabled));
        Assert.All(runtime.CaptureEditor().Tiles, tile => Assert.False(tile.Action.ConfigurationValid));
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, (await runtime.ExecuteAsync(executable.TileId)).Outcome);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, (await runtime.ExecuteAsync(powerShell.TileId)).Outcome);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Url_does_not_accept_privilege_flag_and_rejects_admin_input()
    {
        var tile = Tile("Invalid URL", ShortcutActionTypeIds.Url,
            """{"url":"https://example.com","runAsAdministrator":false}""");
        Save(tile);
        var launchCount = 0;
        var runtime = CreateRuntime(new UserProcessLauncher((_, _) => { launchCount++; return true; }));

        Assert.False(Assert.Single(runtime.Capture().Tiles).Enabled);
        Assert.Equal(ShortcutExecutionOutcome.InvalidConfiguration, (await runtime.ExecuteAsync(tile.TileId)).Outcome);
        var mutation = runtime.MutateEditor(new(FrontendShortcutMutationKind.Create,
            Title: "Admin URL",
            Action: new(FrontendShortcutEditorActionKind.Url, Url: "https://example.com", RunAsAdministrator: true),
            CloseOverlayAfterLaunch: false));
        Assert.False(mutation.Succeeded);
        Assert.Equal(0, launchCount);
    }

    [Fact]
    public async Task Medium_launch_failure_never_requests_overlay_retirement_or_high_fallback()
    {
        var tile = Tile("Tool", ShortcutActionTypeIds.Executable,
            """{"path":"C:\\Tools\\Tool.exe","runAsAdministrator":false}""", closeOverlayAfterLaunch: true);
        Save(tile);
        var requestedModes = new List<bool>();
        var runtime = CreateRuntime(new UserProcessLauncher((_, runAsAdministrator) =>
        {
            requestedModes.Add(runAsAdministrator);
            throw new InvalidOperationException("No Medium launch channel.");
        }), fileExists: _ => true);

        var result = await runtime.ExecuteAsync(tile.TileId);

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.False(result.RetireOverlayAfterExecution);
        Assert.Equal([false], requestedModes);
    }

    [Fact]
    public async Task Built_in_and_url_shell_activations_are_never_administrator_launches()
    {
        var tiles = new[]
        {
            Tile("Steam", ShortcutActionTypeIds.SteamClient, "{}"),
            Tile("Big Picture", ShortcutActionTypeIds.SteamBigPicture, "{}"),
            Tile("Xbox", ShortcutActionTypeIds.XboxApp, "{}"),
            Tile("Web", ShortcutActionTypeIds.Url, """{"url":"https://example.com"}""")
        };
        Save(tiles);
        var requests = new List<(ProcessStartInfo StartInfo, bool RunAsAdministrator)>();
        var runtime = CreateRuntime(new UserProcessLauncher((startInfo, runAsAdministrator) =>
        {
            requests.Add((startInfo, runAsAdministrator));
            return true;
        }));

        foreach (var tile in tiles)
            Assert.Equal(ShortcutExecutionOutcome.Succeeded, (await runtime.ExecuteAsync(tile.TileId)).Outcome);

        Assert.Equal(4, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.False(request.RunAsAdministrator);
            Assert.False(request.StartInfo.UseShellExecute);
            Assert.EndsWith("explorer.exe", request.StartInfo.FileName, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("steam://open/main", requests[0].StartInfo.ArgumentList.Single());
        Assert.Equal("steam://open/bigpicture", requests[1].StartInfo.ArgumentList.Single());
        Assert.Equal("https://example.com", requests[3].StartInfo.ArgumentList.Single());
    }

    private ShortcutRuntime CreateRuntime(UserProcessLauncher launcher, Func<string, bool>? fileExists = null) =>
        new(new ShortcutStore(StorePath), fileExists: fileExists, userProcessLauncher: launcher);

    private void Save(params ShortcutTileDefinition[] tiles) =>
        new ShortcutStore(StorePath).Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition(tiles) });

    private static ShortcutTileDefinition Tile(string title, string typeId, string parameters, bool closeOverlayAfterLaunch = false)
    {
        using var document = JsonDocument.Parse(parameters);
        return new(Guid.NewGuid(), title, closeOverlayAfterLaunch,
            new ShortcutActionSpec(typeId, 1, document.RootElement.Clone()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
