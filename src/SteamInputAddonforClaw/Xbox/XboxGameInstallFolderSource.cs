using System.Text;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Xbox;

internal sealed record XboxInstalledGameFolder(string GameDirectory, string ConfigPath);

internal sealed record XboxGameDriveRoot(string RootPath, DriveType DriveType, bool IsReady);

internal sealed record XboxInstalledGameFolderEnumeration(
    IReadOnlyList<XboxInstalledGameFolder> Folders,
    bool Succeeded,
    bool Unavailable,
    string? FailureReason,
    int SkippedFolderCount = 0);

internal interface IXboxInstalledGameFolderSource
{
    XboxInstalledGameFolderEnumeration Enumerate(CancellationToken cancellationToken);
}

internal sealed class WindowsXboxInstalledGameFolderSource : IXboxInstalledGameFolderSource
{
    private readonly Func<IReadOnlyList<XboxGameDriveRoot>> _gameDrives;
    private readonly Func<string, byte[]?> _readGamingRoot;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, IEnumerable<string>> _enumerateDirectories;
    private readonly Func<string, bool> _fileExists;

    internal WindowsXboxInstalledGameFolderSource()
        : this(GetGameDriveRoots, ReadGamingRoot, Directory.Exists, Directory.EnumerateDirectories, File.Exists)
    {
    }

    internal WindowsXboxInstalledGameFolderSource(
        Func<IReadOnlyList<XboxGameDriveRoot>> gameDrives,
        Func<string, byte[]?> readGamingRoot,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectories,
        Func<string, bool> fileExists)
    {
        _gameDrives = gameDrives;
        _readGamingRoot = readGamingRoot;
        _directoryExists = directoryExists;
        _enumerateDirectories = enumerateDirectories;
        _fileExists = fileExists;
    }

    public XboxInstalledGameFolderEnumeration Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<XboxGameDriveRoot> gameDrives;
        try
        {
            gameDrives = _gameDrives();
        }
        catch (Exception exception)
        {
            var unavailable = exception is NotSupportedException or TypeLoadException or EntryPointNotFoundException or DllNotFoundException;
            return new([], false, unavailable, MicrosoftGameConfigReader.Describe(exception));
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedFolderCount = 0;
        foreach (var drive in gameDrives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;

            var driveLibraries = ResolveLibraries(drive.RootPath, cancellationToken, ref skippedFolderCount);
            foreach (var library in driveLibraries)
                libraries.Add(library);
        }

        var folders = new List<XboxInstalledGameFolder>();
        var seenGameDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var gameDirectory in _enumerateDirectories(library))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string normalizedGameDirectory;
                    try
                    {
                        normalizedGameDirectory = Path.GetFullPath(gameDirectory);
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
                    {
                        skippedFolderCount++;
                        continue;
                    }
                    if (!seenGameDirectories.Add(normalizedGameDirectory)) continue;

                    try
                    {
                        if ((File.GetAttributes(normalizedGameDirectory) & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedFolderCount++;
                            continue;
                        }
                    }
                    catch (Exception exception) when (IsFileSystemFailure(exception))
                    {
                        skippedFolderCount++;
                        AppLog.Debug("XboxCatalog", "Game folder metadata could not be read; folder skipped.",
                            ("GameDirectory", normalizedGameDirectory), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
                        continue;
                    }

                    try
                    {
                        var configPath = Path.Combine(normalizedGameDirectory, "Content", "MicrosoftGame.config");
                        if (_fileExists(configPath)) folders.Add(new(normalizedGameDirectory, configPath));
                    }
                    catch (Exception exception) when (IsFileSystemFailure(exception) || exception is ArgumentException)
                    {
                        skippedFolderCount++;
                        AppLog.Debug("XboxCatalog", "Game folder config could not be checked; folder skipped.",
                            ("GameDirectory", normalizedGameDirectory), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                skippedFolderCount++;
                AppLog.Debug("XboxCatalog", "Xbox game library could not be enumerated; other libraries remain eligible.",
                    ("Library", library), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
            }
        }

        return new(folders, true, false, null, skippedFolderCount);
    }

    private List<string> ResolveLibraries(string driveRoot, CancellationToken cancellationToken, ref int skippedFolderCount)
    {
        var libraries = new List<string>();
        try
        {
            var markerPath = Path.Combine(driveRoot, ".GamingRoot");
            var marker = _readGamingRoot(markerPath);
            if (marker is not null)
            {
                foreach (var relativePath in XboxGamingRootParser.Parse(marker))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!XboxGamingRootParser.TryResolveLibraryPath(driveRoot, relativePath, out var library))
                    {
                        skippedFolderCount++;
                        AppLog.Debug("XboxCatalog", "Unsafe Xbox game-library path in .GamingRoot was ignored.",
                            ("DriveRoot", driveRoot));
                        continue;
                    }
                    if (_directoryExists(library)) libraries.Add(library);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception) || exception is InvalidDataException or ArgumentException)
        {
            skippedFolderCount++;
            AppLog.Debug("XboxCatalog", "Xbox .GamingRoot marker could not be used; checking the narrow XboxGames fallback.",
                ("DriveRoot", driveRoot), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
        }

        if (libraries.Count == 0)
        {
            try
            {
                var fallback = Path.GetFullPath(Path.Combine(driveRoot, "XboxGames"));
                if (_directoryExists(fallback)) libraries.Add(fallback);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception) || exception is ArgumentException)
            {
                skippedFolderCount++;
                AppLog.Debug("XboxCatalog", "XboxGames fallback could not be checked.",
                    ("DriveRoot", driveRoot), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
            }
        }

        return libraries;
    }

    private static IReadOnlyList<XboxGameDriveRoot> GetGameDriveRoots()
    {
        var drives = new List<XboxGameDriveRoot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                drives.Add(new(drive.Name, drive.DriveType, drive.IsReady));
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                AppLog.Debug("XboxCatalog", "A game drive could not be inspected for an Xbox game library.",
                    ("Drive", drive.Name), ("Reason", MicrosoftGameConfigReader.Describe(exception)));
            }
        }
        return drives;
    }

    private static byte[]? ReadGamingRoot(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1024, FileOptions.SequentialScan);
        using var content = new MemoryStream(XboxGamingRootParser.MaximumMarkerBytes);
        var buffer = new byte[1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (content.Length + read > XboxGamingRootParser.MaximumMarkerBytes)
                throw new InvalidDataException("The .GamingRoot marker exceeds the size limit.");
            content.Write(buffer, 0, read);
        }
        return content.ToArray();
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException;
}

internal static class XboxGamingRootParser
{
    internal const int MaximumMarkerBytes = 4096;
    private const int HeaderSize = 8;
    private const string Magic = "RGBX";

    internal static IReadOnlyList<string> Parse(ReadOnlySpan<byte> marker)
    {
        if (marker.Length is < HeaderSize or > MaximumMarkerBytes || marker.Length % 2 != 0)
            throw new InvalidDataException("The .GamingRoot marker has an invalid size.");
        if (!marker[..Magic.Length].SequenceEqual(Encoding.ASCII.GetBytes(Magic)))
            throw new InvalidDataException("The .GamingRoot marker has an unrecognized header.");

        var payload = Encoding.Unicode.GetString(marker[HeaderSize..]);
        return payload.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool TryResolveLibraryPath(string driveRoot, string relativePath, out string libraryPath)
    {
        libraryPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':')
            || relativePath.Contains('\0'))
            return false;

        var normalizedRelative = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalizedRelative.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment.Length == 0 || segment.TrimEnd(' ', '.') is "" or "." or ".."))
            return false;

        try
        {
            var fullDriveRoot = Path.GetFullPath(driveRoot);
            var fullLibraryPath = Path.GetFullPath(Path.Combine(fullDriveRoot, normalizedRelative));
            var rootPrefix = Path.EndsInDirectorySeparator(fullDriveRoot)
                ? fullDriveRoot
                : fullDriveRoot + Path.DirectorySeparatorChar;
            if (string.Equals(fullLibraryPath, fullDriveRoot, StringComparison.OrdinalIgnoreCase)
                || !fullLibraryPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            libraryPath = fullLibraryPath;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
