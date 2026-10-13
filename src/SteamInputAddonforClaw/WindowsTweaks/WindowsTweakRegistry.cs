using Microsoft.Win32;

namespace SteamInputAddonforClaw.WindowsTweaks;

internal readonly record struct WindowsTweakRegistryValueRead(
    bool KeyExists,
    bool ValueExists,
    object? Value,
    RegistryValueKind? Kind);

internal static class WindowsTweakRegistry
{
    internal static WindowsTweakRegistryValueRead Read(RegistryKey hive, string path, string valueName)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(valueName);

        using var key = hive.OpenSubKey(path, writable: false);
        if (key is null)
            return new(false, false, null, null);

        if (!key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
            return new(true, false, null, null);

        return new(
            true,
            true,
            key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
            key.GetValueKind(valueName));
    }

    internal static void Write(
        RegistryKey hive,
        string path,
        string valueName,
        object value,
        RegistryValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(valueName);
        ArgumentNullException.ThrowIfNull(value);

        using var key = hive.CreateSubKey(path, writable: true)
            ?? throw new UnauthorizedAccessException("The Windows tweak registry key could not be opened.");
        key.SetValue(valueName, value, kind);
    }

    internal static void DeleteValue(RegistryKey hive, string path, string valueName)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(valueName);

        using var key = hive.OpenSubKey(path, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
