using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Shortcuts;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ShortcutEditorFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ShortcutEditorFrontendTests.{Guid.NewGuid():N}");
    private readonly AppLogLevel _previousMinimumLevel = AppLog.MinimumLevelOverride;

    [Fact]
    public async Task Capture_and_mutation_use_the_existing_runtime_and_publish_only_after_commit()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var settings = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        var shortcutRuntime = new ShortcutRuntime(new ShortcutStore(Path.Combine(_directory, "shortcuts.json")));
        var control = new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null, shortcutRuntime: shortcutRuntime);
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var captured = await control.CaptureShortcutEditorAsync();
        Assert.True(captured.Available);
        Assert.Empty(captured.Tiles);

        var changed = await control.MutateShortcutAsync(new(FrontendShortcutMutationKind.Create, Title: "Web",
            Action: new(FrontendShortcutEditorActionKind.Url, Url: "https://example.com"),
            CloseOverlayAfterLaunch: true));
        Assert.True(changed.Succeeded);
        Assert.True(changed.Changed);
        Assert.True(Assert.Single(changed.Snapshot.Tiles).CloseOverlayAfterLaunch);
        Assert.Equal(1, invalidations);

        var noOp = await control.MutateShortcutAsync(new(FrontendShortcutMutationKind.Move,
            TileId: changed.Snapshot.Tiles[0].TileId, TargetIndex: 0));
        Assert.True(noOp.Succeeded);
        Assert.False(noOp.Changed);
        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void Shortcut_editor_uses_action_defaults_only_for_creation_and_forces_screenshot_option_off()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "ShortcutPage.xaml.cs"));
        var defaultHelper = source[source.IndexOf("internal static bool DefaultCloseOverlayAfterLaunch", StringComparison.Ordinal)..];
        var selectionHandler = source[source.IndexOf("actionPicker.SelectionChanged +=", StringComparison.Ordinal)..];
        var saveSection = source[source.IndexOf("var intent = new FrontendShortcutMutationIntent(", StringComparison.Ordinal)..];

        Assert.Contains("SteamBigPicture or", defaultHelper, StringComparison.Ordinal);
        Assert.Contains("SteamClient or", defaultHelper, StringComparison.Ordinal);
        Assert.Contains("XboxApp;", defaultHelper, StringComparison.Ordinal);
        Assert.Contains("if (existing is null)", selectionHandler, StringComparison.Ordinal);
        Assert.Contains("DefaultCloseOverlayAfterLaunch(selectedKind)", selectionHandler, StringComparison.Ordinal);
        Assert.Contains("selectedKind != FrontendShortcutEditorActionKind.ScreenshotFullscreen && closeOverlayToggle.IsOn", saveSection, StringComparison.Ordinal);
        Assert.Contains("existing?.CloseOverlayAfterLaunch ?? false", source, StringComparison.Ordinal);
        Assert.Contains("closeOverlayToggle.Visibility = selectedKind == FrontendShortcutEditorActionKind.ScreenshotFullscreen", selectionHandler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshot_folder_is_persisted_by_shortcut_mutation_not_settings()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var settingsPath = Path.Combine(_directory, "settings.json");
        var settings = new StartupSettingsCoordinator(new AppSettings { DeveloperMenuEnabled = true },
            new SettingsStore(settingsPath), new NoOpStartupManager());
        var shortcutsPath = Path.Combine(_directory, "shortcuts.json");
        var shortcutStore = new ShortcutStore(shortcutsPath);
        var control = new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null,
            shortcutRuntime: new ShortcutRuntime(shortcutStore));
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;
        var destination = Path.Combine(_directory, "captures");

        var changed = await control.MutateShortcutAsync(new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Create,
            Title: "Screenshot",
            Action: new(FrontendShortcutEditorActionKind.ScreenshotFullscreen, ScreenshotFolder: destination),
            CloseOverlayAfterLaunch: true));
        Assert.True(changed.Succeeded);
        var editorAction = Assert.Single(changed.Snapshot.Tiles).Action;
        Assert.Equal(destination, editorAction.ScreenshotFolder);
        var stored = Assert.Single(shortcutStore.Load().Document.Dashboard.Tiles);
        Assert.Equal(destination, stored.Action.Parameters.GetProperty("folder").GetString());
        Assert.False(stored.CloseOverlayAfterLaunch);
        Assert.Equal(1, invalidations);
        Assert.True(settings.Settings.DeveloperMenuEnabled);
        settings.ChangeLogLevel(AppLogPreference.Debug);
        using var settingsDocument = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.False(settingsDocument.RootElement.TryGetProperty("ScreenshotSaveFolder", out _));
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
        AppLog.MinimumLevelOverride = _previousMinimumLevel;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Status capture is not part of these tests.");
    }
}
