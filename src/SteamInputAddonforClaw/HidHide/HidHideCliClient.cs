using System.Diagnostics;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.HidHide;

internal sealed class HidHideCliClient(IHidHideCommandRunner? commandRunner = null) : IHidHideClient
{
    private const string InstallPathRegistryKey = @"SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide";
    private readonly IHidHideCommandRunner _commandRunner = commandRunner ?? new HidHideCommandRunner();

    public HidHideInspection Inspect()
    {
        var cliPath = FindCliPath();
        if (cliPath is null)
            return new(HidHideInspectionStatus.NotInstalled, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: "HidHideCLI.exe was not found.");

        try
        {
            var cloak = _commandRunner.Run(cliPath, "--cloak-state");
            var inverse = _commandRunner.Run(cliPath, "--inv-state");
            var applications = _commandRunner.Run(cliPath, "--app-list");
            var hiddenDevices = _commandRunner.Run(cliPath, "--dev-list");
            if (cloak.ExitCode != 0 || inverse.ExitCode != 0 || applications.ExitCode != 0 || hiddenDevices.ExitCode != 0)
                return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: "HidHide configuration could not be read.");
            if (!string.Equals(cloak.StandardOutput.Trim(), "--cloak-on", StringComparison.OrdinalIgnoreCase))
                return new(HidHideInspectionStatus.Disabled, ParseApplications(applications.StandardOutput), ParseHiddenDevices(hiddenDevices.StandardOutput), "HidHide device hiding is disabled.");
            if (!string.Equals(inverse.StandardOutput.Trim(), "--inv-off", StringComparison.OrdinalIgnoreCase))
                return new(HidHideInspectionStatus.InverseWhitelist, ParseApplications(applications.StandardOutput), ParseHiddenDevices(hiddenDevices.StandardOutput), "HidHide inverse whitelist mode is enabled.");
            return new(HidHideInspectionStatus.Available, ParseApplications(applications.StandardOutput), ParseHiddenDevices(hiddenDevices.StandardOutput));
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide inspection failed.", exception, ("Action", "DoNotMutate"));
            return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
    }

    public bool AddApplication(string executablePath) => RunMutation("--app-reg", executablePath);
    public bool RemoveApplication(string executablePath) => RunMutation("--app-unreg", executablePath);

    private bool RunMutation(string command, string executablePath)
    {
        var cliPath = FindCliPath();
        if (cliPath is null) return false;
        try
        {
            var result = _commandRunner.Run(cliPath, command, executablePath);
            return result.ExitCode == 0;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide whitelist mutation failed.", exception, ("Command", command), ("ExecutablePath", executablePath), ("Action", "PreserveJournal"));
            return false;
        }
    }

    private static HashSet<string> ParseApplications(string output)
    {
        var applications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string prefix = "--app-reg \"";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && line.EndsWith('"'))
                applications.Add(Path.GetFullPath(line[prefix.Length..^1]));
        }
        return applications;
    }

    private static string[] ParseHiddenDevices(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("--dev-hide ", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static string? FindCliPath()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(InstallPathRegistryKey);
        var installPath = key?.GetValue("Path") as string;
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        var cliPath = Path.Combine(installPath, "HidHideCLI.exe");
        return File.Exists(cliPath) ? cliPath : null;
    }
}

internal sealed class HidHideCommandRunner : IHidHideCommandRunner
{
    public HidHideCommandResult Run(string executablePath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("HidHideCLI could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, output, error);
    }
}
