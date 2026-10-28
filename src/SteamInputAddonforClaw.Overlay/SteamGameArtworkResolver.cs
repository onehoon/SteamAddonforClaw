using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.Overlay;

internal static class SteamGameArtworkResolver
{
    private const ulong SteamId64Base = 76561197960265728UL;
    private static readonly string[] CustomExtensions = [".png", ".jpg", ".jpeg"];

    internal static uint? EligibleSteamAppId(QuickSettingsPageSnapshot? page)
    {
        if (page is not { PageId: QuickSettingsPageId.Profile, Available: true })
            return null;

        var target = page.ProfileTarget;
        return target is { Kind: QuickSettingsProfileTargetKind.Steam, IsStructurallyValid: true, SteamAppId: > 0 }
            ? target.SteamAppId.Value
            : null;
    }

    internal static IReadOnlyList<string> ResolveArtworkCandidates(QuickSettingsPageSnapshot? page)
    {
        var appId = EligibleSteamAppId(page);
        if (appId is null)
            return [];

        var steamRoot = LocateSteamRoot();
        if (steamRoot is null)
            return [];

        var accountId = ResolveCurrentSteamAccountId(steamRoot);
        return ResolveArtworkCandidates(steamRoot, appId.Value, accountId);
    }

    internal static IReadOnlyList<string> ResolveArtworkCandidates(
        string? steamRoot,
        QuickSettingsPageSnapshot? page,
        uint? userdataAccountId)
    {
        var appId = EligibleSteamAppId(page);
        return appId is > 0
            ? ResolveArtworkCandidates(steamRoot, appId.Value, userdataAccountId)
            : [];
    }

    internal static IReadOnlyList<string> ResolveArtworkCandidates(
        string? steamRoot,
        uint appId,
        uint? userdataAccountId)
    {
        if (appId == 0 || string.IsNullOrWhiteSpace(steamRoot))
            return [];

        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appIdText = appId.ToString(CultureInfo.InvariantCulture);
        var isNativeSteamApp = userdataAccountId is > 0 && HasNativeSteamManifest(steamRoot, appId);

        if (userdataAccountId is > 0)
        {
            var customDirectory = Path.Combine(
                steamRoot, "userdata", userdataAccountId.Value.ToString(CultureInfo.InvariantCulture), "config", "grid");
            AddExistingCandidates(candidates, seen,
                CustomExtensions.Select(extension => Path.Combine(customDirectory, $"{appIdText}{extension}")));

            if (!isNativeSteamApp)
            {
                var launchId = ((ulong)appId << 32) | 0x02000000UL;
                var launchIdText = launchId.ToString(CultureInfo.InvariantCulture);
                AddExistingCandidates(candidates, seen,
                    CustomExtensions.Select(extension => Path.Combine(customDirectory, $"{launchIdText}{extension}")));
            }
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

        foreach (var name in new[] { "library_header_2x.jpg", "library_header.jpg", "header_2x.jpg", "header.jpg" })
            AddExistingCandidates(candidates, seen,
                appDirectories.Select(directory => Path.Combine(directory, name)));

        AddExistingCandidates(candidates, seen,
            [Path.Combine(cacheRoot, $"{appIdText}_library_header.jpg")]);

        return candidates;
    }

    internal static bool HasNativeSteamManifest(string? steamRoot, uint appId)
    {
        if (appId == 0 || string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
            return false;

        var expectedAppId = appId.ToString(CultureInfo.InvariantCulture);
        foreach (var library in EnumerateSteamLibraries(steamRoot))
        {
            try
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{expectedAppId}.acf");
                if (!File.Exists(manifest))
                    continue;

                var manifestAppId = ReadVdfValues(File.ReadAllText(manifest), "appid").FirstOrDefault();
                if (string.Equals(manifestAppId, expectedAppId, StringComparison.Ordinal))
                    return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (SecurityException) { }
        }

        return false;
    }

    internal static string? LocateSteamRoot()
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
            // Optional artwork must not affect Profile availability when registry access fails.
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return SelectValidSteamRoot([
            currentUserSteamPath,
            currentUserInstallPath,
            machineInstallPath,
            string.IsNullOrWhiteSpace(programFilesX86) ? null : Path.Combine(programFilesX86, "Steam")]);
    }

    internal static string? SelectValidSteamRoot(IEnumerable<string?> candidates)
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

    private static uint? ResolveCurrentSteamAccountId(string steamRoot)
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
            // loginusers.vdf and the single-account fallback remain available.
        }

        return ResolveUserdataAccountId(
            Path.Combine(steamRoot, "userdata"),
            Path.Combine(steamRoot, "config", "loginusers.vdf"),
            activeAccountId);
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

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        try
        {
            foreach (var rawPath in ReadVdfValues(File.ReadAllText(libraryFolders), "path"))
            {
                if (!Path.IsPathRooted(rawPath))
                    continue;
                try
                {
                    var path = Path.GetFullPath(rawPath);
                    if (seen.Add(path))
                        result.Add(path);
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (SecurityException) { }

        try
        {
            var normalizedRoot = Path.GetFullPath(steamRoot);
            if (seen.Add(normalizedRoot))
                result.Add(normalizedRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException) { }

        return result;
    }

    private static IEnumerable<string> ReadVdfValues(string text, string key)
    {
        var tokens = Regex.Matches(text, "\\\"((?:\\\\.|[^\\\"])*)\\\"")
            .Select(match => match.Groups[1].Value.Replace("\\\\\"", "\\\"").Replace("\\\\", "\\"))
            .ToArray();
        for (var index = 0; index + 1 < tokens.Length; index++)
            if (string.Equals(tokens[index], key, StringComparison.OrdinalIgnoreCase))
                yield return tokens[index + 1];
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
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException) { }
        }
    }
}
