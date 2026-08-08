using System.Diagnostics;
using Microsoft.Win32;

namespace SteamInputAddonforClaw.Install;

public interface IWindowsStartupManager
{
    StartupRegistrationResult Synchronize(bool enabled);
}

public sealed class WindowsTaskSchedulerStartupManager : IWindowsStartupManager
{
    private const string TaskName = "Steam Input Addon for Claw";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteamInputAddonforClaw";
    private readonly Func<string> _stableExecutablePathProvider;

    public WindowsTaskSchedulerStartupManager(Func<string>? stableExecutablePathProvider = null)
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
            RemoveLegacyRunValue();
            if (enabled)
            {
                var result = RunSchtasks($"/Create /TN \"{TaskName}\" /TR {BuildRunValue(stableExecutablePath)} /SC ONLOGON /DELAY 0003:00 /RL LIMITED /F");
                if (result.ExitCode != 0)
                {
                    return StartupRegistrationResult.Failed(result.Error);
                }

                return StartupRegistrationResult.Enabled();
            }

            var deleteResult = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            if (deleteResult.ExitCode != 0 && !deleteResult.Error.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            {
                return StartupRegistrationResult.Failed(deleteResult.Error);
            }

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

    internal static string BuildCreateArguments(string stableExecutablePath) =>
        $"/Create /TN \"{TaskName}\" /TR {BuildRunValue(stableExecutablePath)} /SC ONLOGON /DELAY 0003:00 /RL LIMITED /F";

    private static void RemoveLegacyRunValue()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static (int ExitCode, string Error) RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Unable to start schtasks.exe.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }
}

public sealed record StartupRegistrationResult(bool Success, string Message)
{
    public static StartupRegistrationResult Enabled() => new(true, "Launch at Windows startup is enabled.");

    public static StartupRegistrationResult Disabled() => new(true, "Launch at Windows startup is disabled.");

    public static StartupRegistrationResult NotInstalled() => new(false, "Windows startup is available after Velopack installation.");

    public static StartupRegistrationResult Failed(string message) => new(false, $"Windows startup setting could not be applied: {message}");
}
