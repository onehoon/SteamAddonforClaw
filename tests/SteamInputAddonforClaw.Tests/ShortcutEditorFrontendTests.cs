using SteamInputAddonforClaw.Contracts.Frontend;
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
        Assert.True(captured.ScreenshotFolder.UsingDefault);
        Assert.Equal(NirCmdScreenshotCapture.ResolveFolder(null), captured.ScreenshotFolder.EffectiveFolder);

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
    public async Task Screenshot_folder_mutation_persists_and_invalid_path_is_a_typed_failure()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var control = CreateControl(new StartupSettingsCoordinator(new AppSettings { DeveloperMenuEnabled = true }, store, new NoOpStartupManager()));
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var changed = await control.SetScreenshotSaveFolderAsync(Path.Combine(_directory, "captures"));
        Assert.True(changed.Succeeded);
        Assert.False(changed.Snapshot.UsingDefault);
        Assert.Equal(Path.Combine(_directory, "captures"), changed.Snapshot.EffectiveFolder);
        Assert.Equal(Path.Combine(_directory, "captures"), store.Load().ScreenshotSaveFolder);
        Assert.True(store.Load().DeveloperMenuEnabled);
        Assert.Equal(1, invalidations);

        var unchanged = await control.SetScreenshotSaveFolderAsync(Path.Combine(_directory, "captures"));
        Assert.True(unchanged.Succeeded);
        Assert.Equal(changed.Snapshot, unchanged.Snapshot);
        Assert.Equal(1, invalidations);

        var invalid = await control.SetScreenshotSaveFolderAsync("relative\\folder");
        Assert.False(invalid.Succeeded);
        Assert.Equal(changed.Snapshot, invalid.Snapshot);
        Assert.Equal(1, invalidations);

        var reset = await control.SetScreenshotSaveFolderAsync("   ");
        Assert.True(reset.Succeeded);
        Assert.True(reset.Snapshot.UsingDefault);
        Assert.Null(reset.Snapshot.ConfiguredFolder);
        Assert.Equal(2, invalidations);
    }

    [Fact]
    public async Task Screenshot_folder_save_failure_returns_previous_snapshot_without_invalidation()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var parentFile = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(parentFile, "file");
        var previous = Path.Combine(_directory, "prior-captures");
        var settings = new StartupSettingsCoordinator(
            new AppSettings { ScreenshotSaveFolder = previous, DeveloperMenuEnabled = true },
            new SettingsStore(Path.Combine(parentFile, "settings.json")),
            new NoOpStartupManager());
        var control = CreateControl(settings);
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var result = await control.SetScreenshotSaveFolderAsync(Path.Combine(_directory, "new-captures"));

        Assert.False(result.Succeeded);
        Assert.Equal(previous, result.Snapshot.ConfiguredFolder);
        Assert.Equal(previous, result.Snapshot.EffectiveFolder);
        Assert.Equal(previous, settings.ScreenshotSaveFolder);
        Assert.True(settings.Settings.DeveloperMenuEnabled);
        Assert.Equal(0, invalidations);
    }

    private InProcessAddonFrontendControl CreateControl(StartupSettingsCoordinator settings) =>
        new(settings, new ThrowingStatusProvider(), null);

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
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
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
