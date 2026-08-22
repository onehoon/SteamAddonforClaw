using Microsoft.Win32;
using System.Text;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles;

public enum ProfileGameSource { Steam, NonSteam }

public sealed record ProfileGameCatalogEntry(uint AppId, string Name, ProfileGameSource Source);

public sealed class ProfileGameCatalogScanner
{
    private readonly Func<string?> _steamRootProvider;

    public ProfileGameCatalogScanner(Func<string?>? steamRootProvider = null) =>
        _steamRootProvider = steamRootProvider ?? LocateSteamRoot;

    public Task<IReadOnlyList<ProfileGameCatalogEntry>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<ProfileGameCatalogEntry> Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<uint, ProfileGameCatalogEntry>();
        var root = _steamRootProvider();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            AppLog.Warn("Profiles.Catalog", "Steam installation could not be located.");
            return Array.Empty<ProfileGameCatalogEntry>();
        }

        var libraries = ReadLibraries(root);
        libraries.Add(root);
        var libraryCount = libraries.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apps = Path.Combine(library, "steamapps");
            string[] manifests;
            try { manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var text = File.ReadAllText(manifest);
                    var appIdText = TextVdf.ReadValues(text, "appid").FirstOrDefault();
                    var name = TextVdf.ReadValues(text, "name").FirstOrDefault();
                    if (uint.TryParse(appIdText, out var appId) && !string.IsNullOrWhiteSpace(name))
                        result[appId] = new(appId, name, ProfileGameSource.Steam);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (FormatException) { }
            }
        }

        var userdata = Path.Combine(root, "userdata");
        string[] accounts = [];
        if (Directory.Exists(userdata))
        {
            try { accounts = Directory.EnumerateDirectories(userdata).ToArray(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shortcuts = Path.Combine(account, "config", "shortcuts.vdf");
                try
                {
                    foreach (var shortcut in BinaryShortcuts.Read(shortcuts))
                        if (!result.ContainsKey(shortcut.AppId) && shortcut.AppId != 0 && !string.IsNullOrWhiteSpace(shortcut.Name))
                            result[shortcut.AppId] = new(shortcut.AppId, shortcut.Name, ProfileGameSource.NonSteam);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (FormatException) { }
            }

        var steamGameCount = result.Values.Count(x => x.Source == ProfileGameSource.Steam);
        var nonSteamGameCount = result.Values.Count(x => x.Source == ProfileGameSource.NonSteam);
        AppLog.Info("Profiles.Catalog", "Profile game catalog scan completed.",
            ("SteamRoot", root), ("LibraryCount", libraryCount),
            ("SteamGameCount", steamGameCount), ("NonSteamGameCount", nonSteamGameCount),
            ("TotalCount", result.Count));

        return result.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AppId).ToArray();
    }

    private static List<string> ReadLibraries(string root)
    {
        try
        {
            return TextVdf.ReadValues(File.ReadAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf")), "path")
                .Where(Path.IsPathRooted).ToList();
        }
        catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; } catch (FormatException) { return []; }
    }

    private static string? LocateSteamRoot()
    {
        using var currentUserSteam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var localMachineSteam = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam");
        return SteamInstallPathSelector.SelectValidSteamRoot([
            currentUserSteam?.GetValue("SteamPath") as string,
            currentUserSteam?.GetValue("InstallPath") as string,
            localMachineSteam?.GetValue("InstallPath") as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")]);
    }

    internal static class SteamInstallPathSelector
    {
        internal static string? SelectValidSteamRoot(IEnumerable<string?> candidates) =>
            candidates.Select(Normalize).FirstOrDefault(IsValid);

        private static string? Normalize(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            try { return Path.GetFullPath(candidate.Trim().Trim('"')); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static bool IsValid(string? candidate) => candidate is not null &&
            Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "steam.exe"));
    }

    private static class TextVdf
    {
        public static IEnumerable<string> ReadValues(string text, string wantedKey)
        {
            var tokens = System.Text.RegularExpressions.Regex.Matches(text, "\\\"((?:\\\\.|[^\\\"])*)\\\"")
                .Select(m => m.Groups[1].Value.Replace("\\\\\"", "\\\"").Replace("\\\\", "\\"))
                .ToArray();
            for (var i = 0; i + 1 < tokens.Length; i++)
                if (string.Equals(tokens[i], wantedKey, StringComparison.OrdinalIgnoreCase)) yield return tokens[i + 1];
        }
    }

    private static class BinaryShortcuts
    {
        public static IEnumerable<(uint AppId, string Name)> Read(string path)
        {
            if (!File.Exists(path)) yield break;
            using var stream = File.OpenRead(path);
            var root = ReadTree(stream);
            if (!root.TryGetValue("shortcuts", out var value) || value is not Dictionary<string, object> shortcuts) yield break;
            foreach (var entry in shortcuts.Values.OfType<Dictionary<string, object>>())
                if (entry.TryGetValue("appid", out var id) && id is int appId && entry.TryGetValue("AppName", out var name) && name is string appName)
                    yield return (unchecked((uint)appId), appName);
        }

        private static Dictionary<string, object> ReadTree(Stream stream)
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal);
            var stack = new Stack<Dictionary<string, object>>(); stack.Push(root);
            while (true)
            {
                var type = stream.ReadByte();
                if (type < 0)
                {
                    if (stack.Count != 1) throw new FormatException("Unexpected end of binary VDF.");
                    return root;
                }
                if (type == 8)
                {
                    if (stack.Count == 1) return root;
                    stack.Pop();
                    continue;
                }
                var key = ReadCString(stream); var current = stack.Peek();
                switch (type)
                {
                    case 0: var child = new Dictionary<string, object>(StringComparer.Ordinal); current[key] = child; stack.Push(child); break;
                    case 1: current[key] = ReadCString(stream); break;
                    case 2: current[key] = ReadInt32(stream); break;
                    case 3: _ = ReadInt32(stream); break;
                    case 7: stream.Seek(8, SeekOrigin.Current); break;
                    default: throw new FormatException($"Unsupported binary VDF type 0x{type:X2}.");
                }
            }
        }
        private static string ReadCString(Stream stream) { var bytes = new List<byte>(); int b; while ((b = stream.ReadByte()) >= 0 && b != 0) bytes.Add((byte)b); if (b < 0) throw new FormatException(); return Encoding.UTF8.GetString(bytes.ToArray()); }
        private static int ReadInt32(Stream stream) { Span<byte> bytes = stackalloc byte[4]; stream.ReadExactly(bytes); return BitConverter.ToInt32(bytes); }
    }
}
