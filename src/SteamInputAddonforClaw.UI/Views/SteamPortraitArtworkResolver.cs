using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.Views;

internal sealed class SteamPortraitArtworkContext(string steamRoot, uint? accountId)
{
    private readonly ConcurrentDictionary<(FrontendProfileGameSource Source, uint AppId), IReadOnlyList<string>> _candidatesByGame = new();

    internal string SteamRoot { get; } = steamRoot;
    internal uint? AccountId { get; } = accountId;

    internal IReadOnlyList<string> GetCandidates(FrontendProfileGameCatalogEntry game) =>
        game.AppId == 0
            ? []
            : _candidatesByGame.GetOrAdd((game.Source, game.AppId), key =>
                SteamPortraitArtworkResolver.ResolveCandidates(SteamRoot, key.AppId, key.Source, AccountId));
}

internal static class SteamPortraitArtworkResolver
{
    private const ulong SteamId64Base = 76561197960265728UL;
    private static readonly string[] CustomExtensions = [".png", ".jpg", ".jpeg"];

    internal static SteamPortraitArtworkContext? CreateContext()
    {
        var steamRoot = LocateSteamRoot();
        if (steamRoot is null)
            return null;

        var userdataRoot = Path.Combine(steamRoot, "userdata");
        var accountId = ResolveCurrentAccountId(
            userdataRoot,
            Path.Combine(steamRoot, "config", "loginusers.vdf"));
        return new SteamPortraitArtworkContext(steamRoot, accountId);
    }

    internal static IReadOnlyList<string> ResolveCandidates(
        SteamPortraitArtworkContext? context,
        FrontendProfileGameCatalogEntry game) =>
        context is null
            ? []
            : context.GetCandidates(game);

    internal static IReadOnlyList<string> ResolveCandidates(
        string? steamRoot,
        uint appId,
        FrontendProfileGameSource source,
        uint? accountId)
    {
        if (appId == 0 || string.IsNullOrWhiteSpace(steamRoot))
            return [];

        var appIdText = appId.ToString(CultureInfo.InvariantCulture);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (source == FrontendProfileGameSource.NonSteam)
        {
            if (accountId is not > 0)
                return [];

            var customDirectory = Path.Combine(
                steamRoot,
                "userdata",
                accountId.Value.ToString(CultureInfo.InvariantCulture),
                "config",
                "grid");
            AddExistingCandidates(candidates, seen, CustomExtensions.Select(extension =>
                Path.Combine(customDirectory, $"{appIdText}p{extension}")));

            if (candidates.Count == 0)
            {
                var launchId = ((ulong)appId << 32) | 0x02000000UL;
                var launchIdText = launchId.ToString(CultureInfo.InvariantCulture);
                AddExistingCandidates(candidates, seen, CustomExtensions.Select(extension =>
                    Path.Combine(customDirectory, $"{launchIdText}p{extension}")));
            }

            return candidates;
        }

        if (source != FrontendProfileGameSource.Steam)
            return [];

        if (accountId is > 0)
        {
            var customDirectory = Path.Combine(
                steamRoot,
                "userdata",
                accountId.Value.ToString(CultureInfo.InvariantCulture),
                "config",
                "grid");
            AddExistingCandidates(candidates, seen, CustomExtensions.Select(extension =>
                Path.Combine(customDirectory, $"{appIdText}p{extension}")));
        }

        var cacheRoot = Path.Combine(steamRoot, "appcache", "librarycache");
        var appDirectory = Path.Combine(cacheRoot, appIdText);
        var appDirectories = new List<string>();
        if (Directory.Exists(appDirectory))
        {
            appDirectories.Add(appDirectory);
            try
            {
                appDirectories.AddRange(Directory.EnumerateDirectories(appDirectory)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        foreach (var directory in appDirectories)
        {
            AddExistingCandidates(candidates, seen, [Path.Combine(directory, "library_600x900_2x.jpg")]);
        }

        AddExistingCandidates(candidates, seen, [Path.Combine(cacheRoot, $"{appIdText}_library_600x900_2x.jpg")]);

        foreach (var directory in appDirectories)
            AddExistingCandidates(candidates, seen, [Path.Combine(directory, "library_600x900.jpg")]);

        AddExistingCandidates(candidates, seen, [Path.Combine(cacheRoot, $"{appIdText}_library_600x900.jpg")]);

        return candidates;
    }

    internal static uint? ResolveUserdataAccountId(
        string? userdataRoot,
        string? loginUsersPath,
        uint? activeProcessAccountId)
    {
        if (string.IsNullOrWhiteSpace(userdataRoot) || !Directory.Exists(userdataRoot))
            return null;

        if (activeProcessAccountId is > 0 && HasAccountDirectory(userdataRoot, activeProcessAccountId.Value))
            return activeProcessAccountId;

        var recentAccounts = ReadMostRecentAccountIds(loginUsersPath)
            .Where(accountId => HasAccountDirectory(userdataRoot, accountId))
            .Distinct()
            .Take(2)
            .ToArray();
        if (recentAccounts.Length == 1)
            return recentAccounts[0];

        try
        {
            var accountIds = Directory.EnumerateDirectories(userdataRoot)
                .Select(Path.GetFileName)
                .Where(name => uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                .Select(name => uint.Parse(name!, NumberStyles.None, CultureInfo.InvariantCulture))
                .Distinct()
                .Take(2)
                .ToArray();
            return accountIds.Length == 1 ? accountIds[0] : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (SecurityException) { return null; }
    }

    internal static (double Width, double Height) GetCoverSizeDip(double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale) && rasterizationScale > 0
            ? rasterizationScale
            : 1.0;
        return (176.0 / scale, 264.0 / scale);
    }

    private static string? LocateSteamRoot()
    {
        string? currentUserSteamPath = null;
        string? currentUserInstallPath = null;
        string? machineInstallPath = null;
        try
        {
            using var currentUserSteam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            using var localMachineSteam = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam");
            currentUserSteamPath = currentUserSteam?.GetValue("SteamPath") as string;
            currentUserInstallPath = currentUserSteam?.GetValue("InstallPath") as string;
            machineInstallPath = localMachineSteam?.GetValue("InstallPath") as string;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Local artwork is optional; continue with known installation locations.
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return SelectValidSteamRoot(
        [
            currentUserSteamPath,
            currentUserInstallPath,
            machineInstallPath,
            string.IsNullOrWhiteSpace(programFilesX86) ? null : Path.Combine(programFilesX86, "Steam")
        ]);
    }

    private static string? SelectValidSteamRoot(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
                if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "steam.exe")))
                    return fullPath;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException)
            {
                // Continue to the next known Steam installation location.
            }
        }

        return null;
    }

    private static uint? ResolveCurrentAccountId(string userdataRoot, string loginUsersPath)
    {
        uint? activeAccountId = null;
        try
        {
            using var activeProcess = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            var value = activeProcess?.GetValue("ActiveUser");
            if (value is not null)
            {
                var parsed = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                if (parsed > 0)
                    activeAccountId = parsed;
            }
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException
            or IOException or UnauthorizedAccessException or SecurityException)
        {
            // loginusers.vdf and the sole-account fallback remain available.
        }

        return ResolveUserdataAccountId(userdataRoot, loginUsersPath, activeAccountId);
    }

    private static bool HasAccountDirectory(string userdataRoot, uint accountId) =>
        accountId > 0 && Directory.Exists(Path.Combine(userdataRoot, accountId.ToString(CultureInfo.InvariantCulture)));

    private static IReadOnlyList<uint> ReadMostRecentAccountIds(string? loginUsersPath)
    {
        if (string.IsNullOrWhiteSpace(loginUsersPath))
            return [];

        string content;
        try { content = File.ReadAllText(loginUsersPath); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (SecurityException) { return []; }

        var result = new List<uint>();
        foreach (Match account in Regex.Matches(content,
            "\\\"(?<steamId>\\d{17})\\\"\\s*\\{(?<body>[^{}]*)\\}", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(account.Groups["body"].Value,
                    "\\\"MostRecent\\\"\\s*\\\"1\\\"", RegexOptions.IgnoreCase)
                || !ulong.TryParse(account.Groups["steamId"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId64)
                || steamId64 <= SteamId64Base)
                continue;

            var accountId = steamId64 - SteamId64Base;
            if (accountId is > 0 and <= uint.MaxValue)
                result.Add((uint)accountId);
        }

        return result;
    }

    private static void AddExistingCandidates(List<string> candidates, HashSet<string> seen, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) && seen.Add(path))
                    candidates.Add(path);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException)
            {
                // An unreadable optional artwork candidate is simply unavailable.
            }
        }
    }
}
