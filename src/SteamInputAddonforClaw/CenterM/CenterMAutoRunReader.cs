using Microsoft.Win32;
using System.Security;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Settings;

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

    internal static AppSettings ReconcilePendingStartup(AppSettings settings, SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        if (!settings.CenterMAutoRunMutationPending || !settings.Oem1Mapping.RemappingEnabled)
            return settings;

        var current = Read();
        var reconciled = current switch
        {
            CenterMAutoRunState.Disabled => settings with
            {
                CenterMAutoRunMutationPending = false,
                CenterMAutoRunOwnedByAddon = true,
                OriginalAutoRun = settings.OriginalAutoRun ?? 1,
                AppliedAutoRun = 0
            },
            CenterMAutoRunState.Enabled => settings with
            {
                CenterMAutoRunMutationPending = false,
                CenterMAutoRunOwnedByAddon = false,
                OriginalAutoRun = null,
                AppliedAutoRun = null
            },
            _ => settings
        };

        if (!ReferenceEquals(reconciled, settings))
        {
            store.Save(reconciled);
            AppLog.Info("CenterM.AutoRun", "Pending AutoRun mutation reconciled during normal startup.", ("ObservedState", current), ("OwnedByAddon", reconciled.CenterMAutoRunOwnedByAddon));
        }
        else
        {
            AppLog.Warn("CenterM.AutoRun", "Pending AutoRun mutation remains unresolved during normal startup; OEM1 must remain fail-open.", null, ("ObservedState", current));
        }

        return reconciled;
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
