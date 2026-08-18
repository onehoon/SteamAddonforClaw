using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Center M's own automatic-startup preference. PR1 only reads this value -- no write
/// path exists yet; AutoRun mutation/ownership is out of scope until a later Settings/UI PR
/// (research handoff section 21).</summary>
internal enum CenterMAutoRunState { Disabled, Enabled, Unknown }

internal static class CenterMAutoRunReader
{
    private const string KeyPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Settings";
    private const string ValueName = "AutoRun";

    internal static CenterMAutoRunState Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);
            return Classify(key?.GetValue(ValueName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
        {
            AppLog.Warn("CenterM.AutoRun", "AutoRun registry read failed.", ex);
            return CenterMAutoRunState.Unknown;
        }
    }

    /// <summary>Pure classification, tested directly: only an exact int 0 or 1 is a confident
    /// result -- any other type, value, or absence is Unknown, never guessed as Disabled.</summary>
    internal static CenterMAutoRunState Classify(object? rawValue) => rawValue switch
    {
        int i when i == 0 => CenterMAutoRunState.Disabled,
        int i when i == 1 => CenterMAutoRunState.Enabled,
        _ => CenterMAutoRunState.Unknown
    };
}
