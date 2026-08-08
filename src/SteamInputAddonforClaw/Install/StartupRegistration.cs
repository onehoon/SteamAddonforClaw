using Microsoft.Win32;

namespace SteamInputAddonforClaw.Install;

public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteamInputAddonforClaw";
    private readonly Func<string> _stableExecutablePathProvider;

    public StartupRegistration(Func<string>? stableExecutablePathProvider = null)
    {
        _stableExecutablePathProvider = stableExecutablePathProvider ?? (() => VelopackAppPaths.StableExecutablePath);
    }

    public StartupRegistrationResult Synchronize(bool enabled)
    {
        var stableExecutablePath = _stableExecutablePathProvider();
        if (!File.Exists(stableExecutablePath))
        {
            return StartupRegistrationResult.NotInstalled();
        }

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                runKey.SetValue(ValueName, BuildRunValue(stableExecutablePath), RegistryValueKind.String);
                return StartupRegistrationResult.Enabled();
            }

            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return StartupRegistrationResult.Disabled();
        }
        catch (UnauthorizedAccessException exception)
        {
            return StartupRegistrationResult.Failed(exception.Message);
        }
        catch (IOException exception)
        {
            return StartupRegistrationResult.Failed(exception.Message);
        }
    }

    internal static string BuildRunValue(string stableExecutablePath) => $"\"{stableExecutablePath}\"";
}

public sealed record StartupRegistrationResult(bool Success, string Message)
{
    public static StartupRegistrationResult Enabled() => new(true, "Launch at Windows startup is enabled.");

    public static StartupRegistrationResult Disabled() => new(true, "Launch at Windows startup is disabled.");

    public static StartupRegistrationResult NotInstalled() => new(false, "Windows startup is available after Velopack installation.");

    public static StartupRegistrationResult Failed(string message) => new(false, $"Windows startup setting could not be applied: {message}");
}
