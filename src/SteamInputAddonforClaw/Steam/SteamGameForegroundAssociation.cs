using System.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Steam;

/// <summary>Fails closed unless the current foreground process runs from the active Steam app's install directory.</summary>
internal sealed class SteamGameForegroundAssociation(
    Func<uint, string?>? installDirectoryProvider = null,
    Func<uint, string?>? processPathProvider = null)
{
    private readonly Func<uint, string?> _installDirectoryProvider =
        installDirectoryProvider ?? ProfileGameCatalogScanner.ResolveInstalledSteamGameDirectory;
    private readonly Func<uint, string?> _processPathProvider = processPathProvider ?? GetProcessPath;
    private readonly object _sync = new();
    private uint _cachedAppId;
    private string? _cachedInstallDirectory;

    internal bool IsForegroundSteamGameplay(uint runningAppId, uint foregroundProcessId)
    {
        if (runningAppId == 0 || foregroundProcessId == 0)
            return false;

        var installDirectory = ResolveInstallDirectory(runningAppId);
        var processPath = _processPathProvider(foregroundProcessId);
        return IsExecutableWithinInstallDirectory(processPath, installDirectory);
    }

    internal static bool IsExecutableWithinInstallDirectory(string? executablePath, string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(installDirectory))
            return false;

        try
        {
            var normalizedExecutable = Path.GetFullPath(executablePath);
            var normalizedDirectory = Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return normalizedExecutable.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private string? ResolveInstallDirectory(uint appId)
    {
        lock (_sync)
        {
            if (_cachedAppId == appId && _cachedInstallDirectory is not null)
                return _cachedInstallDirectory;

            var candidate = _installDirectoryProvider(appId);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                _cachedAppId = appId;
                _cachedInstallDirectory = candidate;
            }
            return candidate;
        }
    }

    private static string? GetProcessPath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
