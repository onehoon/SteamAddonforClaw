namespace SteamInputAddonforClaw.Install;

internal static class AddonDataPaths
{
    private const string DataDirectoryName = "SteamInputAddonforClaw-Data";

    internal static string RootDirectory => ResolveDataRoot(VelopackAppPaths.RootAppDirectory);

    internal static string SettingsPath => ResolveSettingsPath(VelopackAppPaths.RootAppDirectory);

    internal static string RecoveryJournalPath => ResolveRecoveryJournalPath(VelopackAppPaths.RootAppDirectory);

    internal static string LogDirectory => ResolveLogDirectory(VelopackAppPaths.RootAppDirectory);

    internal static string CefMarkerOwnershipPath => Path.Combine(RootDirectory, "steam-cef-marker.json");

    /// <summary>Path to the Device/Profile document (see SteamInputAddonforClaw.Profiles.ProfileStore) --
    /// a separate storage domain from <see cref="SettingsPath"/>, but the same canonical
    /// persistent <c>-Data</c> root.</summary>
    internal static string ProfilesPath => ResolveProfilesPath(VelopackAppPaths.RootAppDirectory);
    internal static string DisplayResolutionRecoveryPath => Path.Combine(RootDirectory, "display-resolution-recovery.json");
    internal static string IntelFpsLimitOwnershipPath => Path.Combine(RootDirectory, "intel-fps-limit-ownership.json");

    internal static string ResolveSettingsPath(string rootAppDirectory) =>
        Path.Combine(ResolveDataRoot(rootAppDirectory), "settings.json");

    internal static string ResolveProfilesPath(string rootAppDirectory) =>
        Path.Combine(ResolveDataRoot(rootAppDirectory), "profiles.json");

    internal static string ResolveRecoveryJournalPath(string rootAppDirectory) =>
        Path.Combine(ResolveDataRoot(rootAppDirectory), "recovery.json");

    internal static string ResolveLogDirectory(string rootAppDirectory) =>
        Path.Combine(ResolveDataRoot(rootAppDirectory), "logs");

    internal static void DeleteFullResetRoot(string rootAppDirectory)
    {
        var root = ResolveDataRoot(rootAppDirectory);
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (Exception exception) { Diagnostics.AppLog.Warn("Uninstall", "Full reset data removal failed.", exception, ("Path", root)); }
    }

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
