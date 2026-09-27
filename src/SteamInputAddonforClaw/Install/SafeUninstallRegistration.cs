using Microsoft.Win32;

namespace SteamInputAddonforClaw.Install;

internal sealed record SafeUninstallRegistryEntry(string? InstallLocation, string? UninstallString, string? QuietUninstallString);

internal interface ISafeUninstallRegistry
{
    SafeUninstallRegistryEntry? ReadAddonEntry();
    bool WriteAddonUninstallStrings(string uninstallString, string quietUninstallString);
}

internal sealed class WindowsSafeUninstallRegistry : ISafeUninstallRegistry
{
    private const string AddonUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SteamInputAddonforClaw";

    public SafeUninstallRegistryEntry? ReadAddonEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AddonUninstallKey, writable: false);
        if (key is null) return null;
        return new(
            key.GetValue("InstallLocation") as string,
            key.GetValue("UninstallString") as string,
            key.GetValue("QuietUninstallString") as string);
    }

    public bool WriteAddonUninstallStrings(string uninstallString, string quietUninstallString)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AddonUninstallKey, writable: true);
        if (key is null) return false;
        key.SetValue("UninstallString", uninstallString, RegistryValueKind.String);
        key.SetValue("QuietUninstallString", quietUninstallString, RegistryValueKind.String);
        return true;
    }
}

internal sealed record SafeUninstallRegistrationResult(bool Success, string Reason);

internal static class SafeUninstallRegistration
{
    internal const string SafeUninstallArgument = "--safe-uninstall";
    internal const string SilentArgument = "--silent";

    internal static SafeUninstallRegistrationResult EnsureCurrentInstallation(
        string rootAppDirectory,
        ISafeUninstallRegistry? registry = null,
        Func<string, bool>? fileExists = null)
    {
        try
        {
            registry ??= new WindowsSafeUninstallRegistry();
            fileExists ??= File.Exists;
            if (!TryResolveInstallation(rootAppDirectory, registry, fileExists, out var entry, out var stubPath, out _, out var reason))
                return new(false, reason);

            var uninstall = $"{Quote(stubPath)} {SafeUninstallArgument}";
            var quietUninstall = $"{Quote(stubPath)} {SafeUninstallArgument} {SilentArgument}";
            if (string.Equals(entry!.UninstallString, uninstall, StringComparison.Ordinal)
                && string.Equals(entry.QuietUninstallString, quietUninstall, StringComparison.Ordinal))
                return new(true, "AlreadyRegistered");

            return registry.WriteAddonUninstallStrings(uninstall, quietUninstall)
                ? new(true, "Registered")
                : new(false, "AddonUninstallEntryCouldNotBeOpenedForWrite");
        }
        catch (Exception exception)
        {
            return new(false, "RegistrationFailed:" + exception.GetType().Name);
        }
    }

    internal static bool TryValidateCurrentInstallation(
        string rootAppDirectory,
        string? currentProcessPath,
        string? expectedCurrentExecutablePath,
        out string updaterPath,
        ISafeUninstallRegistry? registry = null,
        Func<string, bool>? fileExists = null)
    {
        updaterPath = string.Empty;
        try
        {
            registry ??= new WindowsSafeUninstallRegistry();
            fileExists ??= File.Exists;
            if (!VelopackAppPaths.TryResolveCurrentExecutablePath(currentProcessPath, expectedCurrentExecutablePath, out var currentExecutablePath)
                || !fileExists(currentExecutablePath)
                || !TryResolveInstallation(rootAppDirectory, registry, fileExists, out _, out _, out var resolvedUpdater, out _))
                return false;
            updaterPath = resolvedUpdater;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveInstallation(
        string rootAppDirectory,
        ISafeUninstallRegistry registry,
        Func<string, bool> fileExists,
        out SafeUninstallRegistryEntry? entry,
        out string stubPath,
        out string updaterPath,
        out string reason)
    {
        entry = null;
        stubPath = string.Empty;
        updaterPath = string.Empty;
        reason = "InstallationNotVerified";
        if (string.IsNullOrWhiteSpace(rootAppDirectory)) return false;

        var root = Path.GetFullPath(rootAppDirectory);
        stubPath = VelopackAppPaths.ResolveStableExecutablePath(root);
        updaterPath = Path.Combine(root, VelopackAppPaths.UpdaterExecutableName);
        entry = registry.ReadAddonEntry();
        if (entry is null)
        {
            reason = "AddonUninstallEntryMissing";
            return false;
        }
        if (!PathEquals(entry.InstallLocation, root))
        {
            reason = "InstallLocationDoesNotMatchVelopackRoot";
            return false;
        }
        if (!fileExists(stubPath) || !fileExists(updaterPath))
        {
            reason = "StableStubOrUpdaterMissing";
            return false;
        }
        if (!TryReadExecutablePath(entry.UninstallString, out var registeredExecutable)
            || (!PathEquals(registeredExecutable, updaterPath) && !PathEquals(registeredExecutable, stubPath)))
        {
            reason = "ExistingUninstallCommandDoesNotBelongToVelopackRoot";
            return false;
        }

        reason = "Verified";
        return true;
    }

    private static bool TryReadExecutablePath(string? command, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(command)) return false;
        var value = command.Trim();
        if (value[0] == '"')
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote <= 1) return false;
            executablePath = value[1..closingQuote];
            return Path.IsPathFullyQualified(executablePath);
        }

        var extensionEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (extensionEnd < 0) return false;
        executablePath = value[..(extensionEnd + 4)].Trim();
        return Path.IsPathFullyQualified(executablePath);
    }

    private static string Quote(string path)
    {
        if (path.Contains('"', StringComparison.Ordinal))
            throw new InvalidDataException("The installation path contains an invalid quote.");
        return $"\"{path}\"";
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
