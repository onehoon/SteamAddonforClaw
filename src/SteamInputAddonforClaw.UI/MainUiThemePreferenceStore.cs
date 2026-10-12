using Microsoft.UI.Xaml;
using System.Security;
using System.Text;

namespace SteamInputAddonforClaw.UI;

internal enum MainUiTheme
{
    System,
    Light,
    Dark,
}

internal sealed class MainUiThemePreferenceStore
{
    private const string PreferenceFileName = "main-ui-theme.txt";

    internal MainUiThemePreferenceStore(string logDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectoryPath);

        var fullLogDirectoryPath = Path.GetFullPath(logDirectoryPath);
        if (!string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(fullLogDirectoryPath)),
                "logs",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The log directory must be the canonical data-root logs folder.", nameof(logDirectoryPath));
        }

        var dataRoot = Directory.GetParent(fullLogDirectoryPath)
            ?? throw new ArgumentException("The log directory must have a parent data root.", nameof(logDirectoryPath));
        PreferenceFilePath = Path.Combine(dataRoot.FullName, PreferenceFileName);
    }

    internal string PreferenceFilePath { get; }

    internal MainUiTheme Load(out Exception? failure)
    {
        failure = null;
        try
        {
            if (!File.Exists(PreferenceFilePath))
                return MainUiTheme.System;

            var value = File.ReadAllText(PreferenceFilePath, Encoding.UTF8).Trim();
            return Enum.TryParse<MainUiTheme>(value, ignoreCase: true, out var theme)
                && Enum.IsDefined(theme)
                    ? theme
                    : MainUiTheme.System;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            failure = exception;
            return MainUiTheme.System;
        }
    }

    internal bool TrySave(MainUiTheme theme, out Exception? failure)
    {
        failure = null;
        if (!Enum.IsDefined(theme))
        {
            failure = new ArgumentOutOfRangeException(nameof(theme));
            return false;
        }

        var temporaryPath = PreferenceFilePath + ".tmp";
        try
        {
            var dataRoot = Path.GetDirectoryName(PreferenceFilePath)
                ?? throw new InvalidOperationException("The theme preference path has no parent directory.");
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(temporaryPath, theme.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, PreferenceFilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            failure = exception;
            try { File.Delete(temporaryPath); }
            catch (Exception cleanupException) when (IsStorageFailure(cleanupException)) { }
            return false;
        }
    }

    internal static ElementTheme ResolveSystemTheme(int? appsUseLightTheme, ElementTheme fallback) => appsUseLightTheme switch
    {
        1 => ElementTheme.Light,
        0 => ElementTheme.Dark,
        _ => fallback is ElementTheme.Light or ElementTheme.Dark ? fallback : ElementTheme.Dark,
    };

    private static bool IsStorageFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;
}
