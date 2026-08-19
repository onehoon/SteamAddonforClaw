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
        if (!settings.CenterMAutoRunMutationPending)
            return settings;

        var current = Read();
        var reconciled = ReconcilePendingState(settings, current);

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

    internal static AppSettings ReconcilePendingState(AppSettings settings, CenterMAutoRunState current)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.CenterMAutoRunMutationPending)
            return settings;

        if (current == CenterMAutoRunState.Enabled)
        {
            return settings with
            {
                CenterMAutoRunMutationPending = false,
                CenterMAutoRunOwnedByAddon = false,
                OriginalAutoRun = null,
                AppliedAutoRun = null
            };
        }

        if (current != CenterMAutoRunState.Disabled || settings.OriginalAutoRun != 1 || settings.AppliedAutoRun != 0)
        {
            // The registry isn't confidently Disabled, or the durable intent record itself is
            // incomplete/corrupt (missing/unexpected OriginalAutoRun or AppliedAutoRun). A pending
            // marker alone proves only that a mutation was attempted, never what the original value
            // was -- fabricating OriginalAutoRun=1 here would let a later restore/uninstall path
            // write a value that was never actually recorded as the user's original setting.
            // Preserve the pending marker and stay fail-open until it can be resolved.
            return settings;
        }

        return settings with
        {
            CenterMAutoRunMutationPending = false,
            CenterMAutoRunOwnedByAddon = true,
            OriginalAutoRun = 1,
            AppliedAutoRun = 0
        };
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
