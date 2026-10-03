using SteamInputAddonforClaw.FseHome;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FseHomeHandoffTests
{
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fse-home-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
