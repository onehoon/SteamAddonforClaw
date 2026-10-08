using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Processes;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ShortcutLaunchDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ShortcutLaunchDiagnosticsTests.{Guid.NewGuid():N}");
    private readonly string? _previousLogDirectory = AppLog.DirectoryOverride;
    private readonly AppLogLevel _previousMinimumLevel = AppLog.MinimumLevelOverride;

    [Fact]
    public async Task Accepted_builtin_dispatch_logs_medium_mode_without_claiming_the_target_is_visible()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        Directory.CreateDirectory(_directory);

        var tileId = Guid.NewGuid();
        using var parameters = JsonDocument.Parse("{}");
        var tile = new ShortcutTileDefinition(tileId, "Steam", false,
            new ShortcutActionSpec("system.steam-client", 1, parameters.RootElement.Clone()));
        var store = new ShortcutStore(Path.Combine(_directory, "shortcuts.json"));
        store.Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition([tile]) });
        var requestedAdministratorModes = new List<bool>();
        var launcher = new UserProcessLauncher((_, runAsAdministrator) =>
        {
            requestedAdministratorModes.Add(runAsAdministrator);
            return true;
        });
        var runtime = new ShortcutRuntime(store, userProcessLauncher: launcher);

        var result = await runtime.ExecuteAsync(tileId);
        AppLog.DrainForTests();

        Assert.Equal(ShortcutExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal([false], requestedAdministratorModes);
        var log = File.ReadAllText(Directory.EnumerateFiles(_directory, "SteamInputAddonforClaw-*.log").Single());
        Assert.Contains("Shortcut action dispatch accepted.", log, StringComparison.Ordinal);
        Assert.Contains("RequestedPrivilegeMode=Medium", log, StringComparison.Ordinal);
        Assert.Contains("Outcome=DispatchAccepted", log, StringComparison.Ordinal);
        Assert.Contains("TargetVisibility=NotObserved", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Builtin_native_failure_logs_api_stage_and_code_without_high_fallback()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        Directory.CreateDirectory(_directory);

        var tileId = Guid.NewGuid();
        using var parameters = JsonDocument.Parse("{}");
        var tile = new ShortcutTileDefinition(tileId, "Steam", true,
            new ShortcutActionSpec("system.steam-client", 1, parameters.RootElement.Clone()));
        var store = new ShortcutStore(Path.Combine(_directory, "shortcuts.json"));
        store.Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition([tile]) });

        var requestedAdministratorModes = new List<bool>();
        var launcher = new UserProcessLauncher((_, runAsAdministrator) =>
        {
            requestedAdministratorModes.Add(runAsAdministrator);
            throw UserProcessLauncher.CaptureNativeFailure("CreateProcessWithTokenW", () => 1314);
        });
        var runtime = new ShortcutRuntime(store, userProcessLauncher: launcher);

        var result = await runtime.ExecuteAsync(tileId);
        AppLog.DrainForTests();

        Assert.Equal(ShortcutExecutionOutcome.Failed, result.Outcome);
        Assert.False(result.RetireOverlayAfterExecution);
        Assert.Equal([false], requestedAdministratorModes);
        var log = File.ReadAllText(Directory.EnumerateFiles(_directory, "SteamInputAddonforClaw-*.log").Single());
        Assert.Contains("Stage=CreateProcessWithTokenW", log, StringComparison.Ordinal);
        Assert.Contains("NativeErrorCode=1314", log, StringComparison.Ordinal);
        Assert.Contains("NativeErrorCodeHex=0x00000522", log, StringComparison.Ordinal);
        Assert.Contains("NativeErrorMessage=", log, StringComparison.Ordinal);
        Assert.Contains("RequestedPrivilegeMode=Medium", log, StringComparison.Ordinal);
        Assert.Contains("TypeId=system.steam-client", log, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionMessage", log, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = _previousLogDirectory;
        AppLog.MinimumLevelOverride = _previousMinimumLevel;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
