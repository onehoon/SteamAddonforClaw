using Microsoft.Win32;
using System.Text;

namespace SteamInputAddonforClaw.Profiles;

public enum ProfileGameSource { Steam, NonSteam }

public sealed record ProfileGameCatalogEntry(uint AppId, string Name, ProfileGameSource Source);

public sealed class ProfileGameCatalogScanner
{
    private readonly Func<string?> _steamRootProvider;

    public ProfileGameCatalogScanner(Func<string?>? steamRootProvider = null) =>
        _steamRootProvider = steamRootProvider ?? LocateSteamRoot;

    public Task<IReadOnlyList<ProfileGameCatalogEntry>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<uint, ProfileGameCatalogEntry>();
        var root = _steamRootProvider();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<ProfileGameCatalogEntry>>(Array.Empty<ProfileGameCatalogEntry>());

        var libraries = ReadLibraries(root);
        libraries.Add(root);
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apps = Path.Combine(library, "steamapps");
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var values = TextVdf.Read(File.ReadAllText(manifest));
                    if (uint.TryParse(values.GetValueOrDefault("appid"), out var appId) &&
                        !string.IsNullOrWhiteSpace(values.GetValueOrDefault("name")))
                        result[appId] = new(appId, values["name"], ProfileGameSource.Steam);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (FormatException) { }
            }
        }

        var userdata = Path.Combine(root, "userdata");
        if (Directory.Exists(userdata))
            foreach (var account in Directory.EnumerateDirectories(userdata))
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

        return Task.FromResult<IReadOnlyList<ProfileGameCatalogEntry>>(result.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AppId).ToArray());
    }

    private static List<string> ReadLibraries(string root)
    {
        var result = new List<string>();
        try
        {
            var values = TextVdf.Read(File.ReadAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf")));
            foreach (var value in values.Values)
                if (value.StartsWith("\\") || Path.IsPathRooted(value)) result.Add(value);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { } catch (FormatException) { }
        return result;
    }

    private static string? LocateSteamRoot()
    {
        var candidates = new[] {
            (string?)Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath"),
            (string?)Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam")?.GetValue("InstallPath"),
            (string?)Registry.LocalMachine.OpenSubKey(@"Software\Valve\Steam")?.GetValue("InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
            File.Exists(Path.Combine(path, "steam.exe")));
    }

    private static class TextVdf
    {
        public static Dictionary<string, string> Read(string text)
        {
            var tokens = System.Text.RegularExpressions.Regex.Matches(text, "\\\"((?:\\\\.|[^\\\"])*)\\\"")
                .Select(m => m.Groups[1].Value.Replace("\\\\\"", "\\\"").Replace("\\\\", "\\"))
                .ToArray();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < tokens.Length; i++)
                if (tokens[i] is "appid" or "name" or "path") values[tokens[i]] = tokens[i + 1];
            return values;
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
                var type = stream.ReadByte(); if (type < 0 || type == 8) { if (stack.Count == 1) return root; stack.Pop(); continue; }
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
