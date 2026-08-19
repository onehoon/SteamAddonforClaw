using Microsoft.Win32;
using System.Security;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Center M's own automatic-startup preference. Normal startup only reads this value;
/// mutation is available exclusively to the explicit elevated prerequisite setup path.</summary>
internal enum CenterMAutoRunState { Disabled, Enabled, Unknown }

internal static class CenterMAutoRunReader
{
    internal const string KeyPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Settings";
    internal const string ValueName = "AutoRun";

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

    internal static bool TryDisableExplicitly(out CenterMAutoRunState confirmedState, out int? originalValue)
    {
        confirmedState = CenterMAutoRunState.Unknown;
        originalValue = null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(KeyPath, writable: true);
            var raw = key?.GetValue(ValueName);
            if (raw is not int value || value is not (0 or 1)) return false;
            originalValue = value;
            if (value == 1) key!.SetValue(ValueName, 0, RegistryValueKind.DWord);
            confirmedState = Classify(key?.GetValue(ValueName));
            return confirmedState == CenterMAutoRunState.Disabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ObjectDisposedException)
        {
            AppLog.Warn("CenterM.AutoRun", "Explicit AutoRun setup failed.", ex);
            return false;
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
