using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("SteamCefDebug")]
public sealed class SteamCefDebugBootstrapTests
{
    [Fact]
    public void Missing_marker_is_created_as_empty_file()
    {
        using var scope = new SteamDirectoryScope();

        Assert.True(SteamCefDebugBootstrap.EnsureForSteamDirectory(scope.Path));

        var marker = System.IO.Path.Combine(scope.Path, SteamCefDebugBootstrap.MarkerFileName);
        Assert.True(File.Exists(marker));
        Assert.Equal(0, new FileInfo(marker).Length);
        SteamCefDebugBootstrap.RemoveOwnedMarker();
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Existing_marker_is_preserved()
    {
        using var scope = new SteamDirectoryScope();
        var marker = System.IO.Path.Combine(scope.Path, SteamCefDebugBootstrap.MarkerFileName);
        File.WriteAllText(marker, "owned-by-another-tool");

        Assert.True(SteamCefDebugBootstrap.EnsureForSteamDirectory(scope.Path));

        Assert.Equal("owned-by-another-tool", File.ReadAllText(marker));
        SteamCefDebugBootstrap.RemoveOwnedMarker();
        Assert.Equal("owned-by-another-tool", File.ReadAllText(marker));
    }

    [Fact]
    public void Directory_without_steam_executable_is_rejected_without_creating_marker()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "steam-cef-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(SteamCefDebugBootstrap.EnsureForSteamDirectory(directory));
            Assert.False(File.Exists(System.IO.Path.Combine(directory, SteamCefDebugBootstrap.MarkerFileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Empty_directory_value_fails_open()
    {
        Assert.False(SteamCefDebugBootstrap.EnsureForSteamDirectory("   "));
    }

    [Fact]
    public void Ownership_persistence_failure_rolls_back_new_marker()
    {
        using var scope = new SteamDirectoryScope();
        Directory.CreateDirectory(scope.OwnershipPath);

        Assert.False(SteamCefDebugBootstrap.EnsureForSteamDirectory(scope.Path));
        Assert.False(File.Exists(System.IO.Path.Combine(scope.Path, SteamCefDebugBootstrap.MarkerFileName)));
    }

    [Fact]
    public void Ownership_io_failure_rolls_back_new_marker()
    {
        using var scope = new SteamDirectoryScope();
        using var ownershipLock = new FileStream(scope.OwnershipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.False(SteamCefDebugBootstrap.EnsureForSteamDirectory(scope.Path));
        Assert.False(File.Exists(System.IO.Path.Combine(scope.Path, SteamCefDebugBootstrap.MarkerFileName)));
    }

    private sealed class SteamDirectoryScope : IDisposable
    {
        internal SteamDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "steam-cef-bootstrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "steam.exe"), string.Empty);
            OwnershipPath = System.IO.Path.Combine(Path, "addon-ownership.json");
            SteamCefDebugBootstrap.OwnershipPathProvider = () => OwnershipPath;
        }

        internal string Path { get; }
        internal string OwnershipPath { get; }

        public void Dispose()
        {
            SteamCefDebugBootstrap.OwnershipPathProvider = static () => SteamInputAddonforClaw.Install.AddonDataPaths.CefMarkerOwnershipPath;
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

[CollectionDefinition("SteamCefDebug", DisableParallelization = true)]
public sealed class SteamCefDebugCollection
{
}
