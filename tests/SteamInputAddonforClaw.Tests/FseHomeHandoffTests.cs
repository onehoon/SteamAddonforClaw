using SteamInputAddonforClaw.FseHome;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FseHomeHandoffTests
{
    [Fact]
    public void Fse_home_diagnostics_use_the_persistent_addon_log_directory()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "local-app-data");

        var path = FseHomeDiagnostics.ResolveLogPath(localAppData);

        Assert.Equal(
            Path.Combine(localAppData, "SteamInputAddonforClaw-Data", "logs", "fse-home-last.log"),
            path);
        Assert.Equal(
            Path.Combine(localAppData, "SteamInputAddonforClaw-Data", "settings.json"),
            FseHomeDiagnostics.ResolveSettingsPath(localAppData));
    }

    [Fact]
    public void Missing_settings_defaults_fse_home_logging_to_info()
    {
        var localAppData = CreateTemporaryDirectory();
        try
        {
            var diagnostics = new FseHomeDiagnostics(localAppData);
            diagnostics.Start();
            diagnostics.Info("started");
            diagnostics.Debug("intermediate detail");

            var text = File.ReadAllText(FseHomeDiagnostics.ResolveLogPath(localAppData));
            Assert.Contains("[INFO] started", text);
            Assert.DoesNotContain("[DEBUG]", text);
        }
        finally { Directory.Delete(localAppData, recursive: true); }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"LogLevel\":\"Verbose\"}")]
    public void Malformed_or_unknown_setting_fails_closed_without_creating_a_log(string settings)
    {
        var localAppData = CreateTemporaryDirectory();
        try
        {
            WriteSettings(localAppData, settings);
            var diagnostics = new FseHomeDiagnostics(localAppData);
            diagnostics.Start();
            diagnostics.Info("must not be written");

            Assert.False(File.Exists(FseHomeDiagnostics.ResolveLogPath(localAppData)));
        }
        finally { Directory.Delete(localAppData, recursive: true); }
    }

    [Fact]
    public void Off_does_not_create_or_truncate_the_fse_home_log()
    {
        var localAppData = CreateTemporaryDirectory();
        try
        {
            WriteSettings(localAppData, "{\"LogLevel\":\"Off\"}");
            var logPath = FseHomeDiagnostics.ResolveLogPath(localAppData);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, "previous record");

            var diagnostics = new FseHomeDiagnostics(localAppData);
            diagnostics.Start();
            diagnostics.Info("must not be appended");

            Assert.Equal("previous record", File.ReadAllText(logPath));
        }
        finally { Directory.Delete(localAppData, recursive: true); }
    }

    [Fact]
    public void Debug_setting_keeps_intermediate_fse_home_diagnostics()
    {
        var localAppData = CreateTemporaryDirectory();
        try
        {
            WriteSettings(localAppData, "{\"LogLevel\":\"Debug\"}");
            var diagnostics = new FseHomeDiagnostics(localAppData);
            diagnostics.Start();
            diagnostics.Info("handoff started");
            diagnostics.Debug("foreground probe");
            diagnostics.Warn("handoff fallback");

            var text = File.ReadAllText(FseHomeDiagnostics.ResolveLogPath(localAppData));
            Assert.Contains("[INFO] handoff started", text);
            Assert.Contains("[DEBUG] foreground probe", text);
            Assert.Contains("[WARN] handoff fallback", text);
        }
        finally { Directory.Delete(localAppData, recursive: true); }
    }

    [Fact]
    public void Steam_executable_resolution_prefers_a_valid_steam_exe_registry_value()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(root, "custom", "steam.exe");
            var fallbackExecutable = Path.Combine(root, "default", "steam.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackExecutable)!);
            File.WriteAllText(executable, "test");
            File.WriteAllText(fallbackExecutable, "test");

            var resolved = SteamLauncher.ResolveFromRegistryValues(executable, Path.GetDirectoryName(fallbackExecutable));

            Assert.Equal(Path.GetFullPath(executable), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Steam_executable_resolution_uses_steam_path_when_steam_exe_is_stale()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var fallbackExecutable = Path.Combine(root, "steam.exe");
            File.WriteAllText(fallbackExecutable, "test");

            var resolved = SteamLauncher.ResolveFromRegistryValues(
                Path.Combine(root, "missing", "steam.exe"), root);

            Assert.Equal(Path.GetFullPath(fallbackExecutable), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("steamwebhelper", "SDL_app", "Steam Big Picture")]
    [InlineData("STEAMWEBHELPER", "SDL_app", "steam big picture - Library")]
    public void Big_picture_identity_accepts_only_the_expected_process_class_and_title_prefix(
        string processName,
        string windowClass,
        string title)
    {
        Assert.True(SteamBigPictureWindowProbe.MatchesIdentity(processName, windowClass, title));
    }

    [Theory]
    [InlineData("steam", "SDL_app", "Steam Big Picture")]
    [InlineData("steamwebhelper", "Chrome_WidgetWin_1", "Steam Big Picture")]
    [InlineData("steamwebhelper", "SDL_app", "Steam")]
    public void Big_picture_identity_rejects_a_candidate_when_any_required_identity_part_differs(
        string processName,
        string windowClass,
        string title)
    {
        Assert.False(SteamBigPictureWindowProbe.MatchesIdentity(processName, windowClass, title));
    }

    [Fact]
    public void Big_picture_readiness_requires_a_visible_matching_window()
    {
        Assert.False(SteamBigPictureWindowProbe.MatchesVisibleIdentity(
            "steamwebhelper",
            "SDL_app",
            "Steam Big Picture",
            isVisible: false));
        Assert.True(SteamBigPictureWindowProbe.MatchesVisibleIdentity(
            "steamwebhelper",
            "SDL_app",
            "Steam Big Picture",
            isVisible: true));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fse-home-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteSettings(string localAppData, string content)
    {
        var path = FseHomeDiagnostics.ResolveSettingsPath(localAppData);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
