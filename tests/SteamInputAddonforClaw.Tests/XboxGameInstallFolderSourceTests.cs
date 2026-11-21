using System.Text;
using SteamInputAddonforClaw.Xbox;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameInstallFolderSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamAddon.XboxGameFolders.{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Gaming_root_marker_discovers_a_custom_game_library()
    {
        var drive = CreateDirectory("drive-a");
        var game = CreateGame(drive, "CustomLibrary", "Sample Game");
        WriteGamingRoot(drive, "CustomLibrary");

        var result = CreateSource([drive]).Enumerate(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(game), Assert.Single(result.Folders).GameDirectory);
    }

    [Fact]
    public void Multiple_fixed_drive_roots_and_narrow_fallback_are_combined()
    {
        var firstDrive = CreateDirectory("drive-a");
        var secondDrive = CreateDirectory("drive-b");
        var firstGame = CreateGame(firstDrive, "LibraryA", "First");
        var secondGame = CreateGame(secondDrive, "XboxGames", "Second");
        WriteGamingRoot(firstDrive, "LibraryA");

        var result = CreateSource([firstDrive, secondDrive]).Enumerate(CancellationToken.None);

        Assert.Equal(2, result.Folders.Count);
        Assert.Contains(result.Folders, folder => folder.GameDirectory == Path.GetFullPath(firstGame));
        Assert.Contains(result.Folders, folder => folder.GameDirectory == Path.GetFullPath(secondGame));
    }

    [Fact]
    public void Missing_or_invalid_marker_uses_only_the_direct_XboxGames_fallback()
    {
        var drive = CreateDirectory("drive");
        var game = CreateGame(drive, "XboxGames", "Fallback");
        File.WriteAllBytes(Path.Combine(drive, ".GamingRoot"), [1, 2, 3, 4]);

        var result = CreateSource([drive]).Enumerate(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(game), Assert.Single(result.Folders).GameDirectory);
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData(".. \\outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share")]
    public void Unsafe_marker_paths_cannot_escape_the_drive(string relativePath)
    {
        var drive = CreateDirectory("drive");
        CreateGame(_directory, "outside", "Outside");
        WriteGamingRoot(drive, relativePath);

        Assert.False(XboxGamingRootParser.TryResolveLibraryPath(drive, relativePath, out _));
        var result = CreateSource([drive]).Enumerate(CancellationToken.None);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public void Inaccessible_library_does_not_block_another_discovered_library()
    {
        var drive = CreateDirectory("drive");
        var accessibleGame = CreateGame(drive, "GoodLibrary", "Good");
        Directory.CreateDirectory(Path.Combine(drive, "LockedLibrary"));
        WriteGamingRoot(drive, "LockedLibrary", "GoodLibrary");
        var lockedPath = Path.GetFullPath(Path.Combine(drive, "LockedLibrary"));
        IEnumerable<string> Enumerate(string path)
        {
            if (string.Equals(Path.GetFullPath(path), lockedPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("synthetic inaccessible library");
            return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
        }

        var result = CreateSource([drive], enumerateDirectories: Enumerate).Enumerate(CancellationToken.None);

        Assert.Contains(result.Folders, folder => folder.GameDirectory == Path.GetFullPath(accessibleGame));
        Assert.Equal(1, result.SkippedFolderCount);
    }

    [Fact]
    public void Inaccessible_drive_does_not_block_a_later_drive()
    {
        var inaccessibleDrive = CreateDirectory("drive-locked");
        var accessibleDrive = CreateDirectory("drive-ready");
        var game = CreateGame(accessibleDrive, "XboxGames", "Available");
        var inaccessibleRoot = Path.GetFullPath(inaccessibleDrive);
        var source = new WindowsXboxInstalledGameFolderSource(
            () => [inaccessibleDrive, accessibleDrive],
            path =>
            {
                if (Path.GetFullPath(Path.GetDirectoryName(path)!) == inaccessibleRoot)
                    throw new UnauthorizedAccessException("synthetic inaccessible drive");
                return ReadGamingRoot(path);
            },
            Directory.Exists,
            Directory.EnumerateDirectories,
            File.Exists);

        var result = source.Enumerate(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(game), Assert.Single(result.Folders).GameDirectory);
        Assert.True(result.SkippedFolderCount > 0);
    }

    [Fact]
    public void Duplicate_library_entries_and_game_directories_are_deduplicated()
    {
        var drive = CreateDirectory("drive");
        var game = CreateGame(drive, "XboxGames", "One");
        WriteGamingRoot(drive, "XboxGames", "xboxgames");

        var result = CreateSource([drive]).Enumerate(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(game), Assert.Single(result.Folders).GameDirectory);
    }

    [Fact]
    public void Discovery_only_checks_immediate_game_folders_and_content_config()
    {
        var drive = CreateDirectory("drive");
        CreateGame(drive, Path.Combine("XboxGames", "Nested", "Game"), "Nested");
        WriteGamingRoot(drive, "XboxGames");

        var result = CreateSource([drive]).Enumerate(CancellationToken.None);

        Assert.Empty(result.Folders);
    }

    [Fact]
    public void WindowsApps_only_legacy_fixture_is_outside_the_supported_fast_path()
    {
        var drive = CreateDirectory("drive");
        CreateGame(drive, Path.Combine("WindowsApps", "LegacyGame"), "Legacy");

        var result = CreateSource([drive]).Enumerate(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public void Duplicate_drive_roots_do_not_scan_the_same_game_twice()
    {
        var drive = CreateDirectory("drive");
        var game = CreateGame(drive, "XboxGames", "One");
        WriteGamingRoot(drive, "XboxGames");

        var result = CreateSource([drive, drive.ToUpperInvariant()]).Enumerate(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(game), Assert.Single(result.Folders).GameDirectory);
    }

    [Fact]
    public void Parser_rejects_invalid_header_odd_size_and_over_limit_marker()
    {
        Assert.Throws<InvalidDataException>(() => XboxGamingRootParser.Parse([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Throws<InvalidDataException>(() => XboxGamingRootParser.Parse(BuildMarker("XboxGames")[..^1]));
        Assert.Throws<InvalidDataException>(() => XboxGamingRootParser.Parse(new byte[XboxGamingRootParser.MaximumMarkerBytes + 2]));
    }

    [Fact]
    public void Cancellation_during_one_drive_does_not_continue_to_library_enumeration()
    {
        var drive = CreateDirectory("drive");
        CreateGame(drive, "XboxGames", "One");
        WriteGamingRoot(drive, "XboxGames");
        using var cancellation = new CancellationTokenSource();
        var source = new WindowsXboxInstalledGameFolderSource(
            () => [drive],
            _ => { cancellation.Cancel(); return BuildMarker("XboxGames"); },
            Directory.Exists,
            Directory.EnumerateDirectories,
            File.Exists);

        Assert.ThrowsAny<OperationCanceledException>(() => source.Enumerate(cancellation.Token));
    }

    private WindowsXboxInstalledGameFolderSource CreateSource(
        IReadOnlyList<string> driveRoots,
        Func<string, IEnumerable<string>>? enumerateDirectories = null) =>
        new(() => driveRoots, ReadGamingRoot, Directory.Exists,
            enumerateDirectories ?? (path => Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)),
            File.Exists);

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateGame(string driveRoot, string libraryRelativePath, string gameName)
    {
        var gameDirectory = Path.Combine(driveRoot, libraryRelativePath, gameName);
        var contentDirectory = Path.Combine(gameDirectory, "Content");
        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(Path.Combine(contentDirectory, "MicrosoftGame.config"), "<Game />");
        return gameDirectory;
    }

    private static void WriteGamingRoot(string driveRoot, params string[] paths) =>
        File.WriteAllBytes(Path.Combine(driveRoot, ".GamingRoot"), BuildMarker(paths));

    private static byte[]? ReadGamingRoot(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static byte[] BuildMarker(params string[] paths)
    {
        var payload = Encoding.Unicode.GetBytes(string.Join('\0', paths) + '\0');
        var marker = new byte[8 + payload.Length];
        Encoding.ASCII.GetBytes("RGBX").CopyTo(marker, 0);
        payload.CopyTo(marker, 8);
        return marker;
    }
}
