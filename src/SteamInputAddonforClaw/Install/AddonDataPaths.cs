namespace SteamInputAddonforClaw.Install;

internal static class AddonDataPaths
{
    private const string DataDirectoryName = "SteamInputAddonforClaw-Data";

    internal static string RootDirectory => ResolveDataRoot(VelopackAppPaths.RootAppDirectory);

    internal static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    internal static string RecoveryJournalPath => Path.Combine(RootDirectory, "recovery.json");

    internal static string ResolveDataRoot(string rootAppDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAppDirectory);

        var normalizedRoot = Path.GetFullPath(rootAppDirectory);
        var filesystemRoot = Path.GetPathRoot(normalizedRoot);
        if (!string.Equals(normalizedRoot, filesystemRoot, StringComparison.OrdinalIgnoreCase))
        {
            normalizedRoot = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        var parent = Directory.GetParent(normalizedRoot)
            ?? throw new ArgumentException("The application root must have a valid parent directory.", nameof(rootAppDirectory));

        return Path.Combine(parent.FullName, DataDirectoryName);
    }
}
