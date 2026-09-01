using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Install;

internal enum ElevatedStartupTaskOutcome { Created, Cancelled, Failed }

/// <summary>Runs one bounded, privileged "create the Addon-owned startup task" operation. It reuses
/// the same <c>Verb="runas"</c> self-elevation pattern as the prerequisite setup helper: a short
/// child <c>SteamInputAddonforClaw.exe --ensure-startup-task &lt;user&gt;</c> that creates exactly
/// this one task and exits. There is no long-lived elevated process, service, or generic task API
/// (PR10 addendum sections 16/17/19).</summary>
internal interface IElevatedStartupTaskInvoker
{
    ElevatedStartupTaskOutcome EnsureOwnedTask();
}

internal sealed class SelfElevatedStartupTaskInvoker : IElevatedStartupTaskInvoker
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED -- the UAC consent prompt was dismissed.
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<string> _executablePathProvider;
    private readonly Func<string> _currentUserIdentityProvider;

    internal SelfElevatedStartupTaskInvoker(
        Func<string>? executablePathProvider = null,
        Func<string>? currentUserIdentityProvider = null)
    {
        _executablePathProvider = executablePathProvider ?? (() => VelopackAppPaths.StableExecutablePath);
        _currentUserIdentityProvider = currentUserIdentityProvider
            ?? (() => System.Security.Principal.WindowsIdentity.GetCurrent().Name);
    }

    public ElevatedStartupTaskOutcome EnsureOwnedTask()
    {
        var executablePath = _executablePathProvider();
        if (!File.Exists(executablePath))
        {
            AppLog.Warn("TaskScheduler", "Elevated startup-task helper is unavailable.", null, ("Reason", "StableExecutableMissing"));
            return ElevatedStartupTaskOutcome.Failed;
        }

        // The child runs elevated (possibly under a different admin token), so the logged-in user is
        // passed explicitly -- the task's logon trigger must target the CURRENT interactive user.
        var userArgument = Convert.ToBase64String(Encoding.UTF8.GetBytes(_currentUserIdentityProvider()));

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(executablePath, $"{ElevatedStartupTaskSetup.Argument} {userArgument}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            AppLog.Info("TaskScheduler", "Elevated startup-task creation was cancelled at the UAC prompt.");
            return ElevatedStartupTaskOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            AppLog.Warn("TaskScheduler", "Elevated startup-task helper could not be started.", exception);
            return ElevatedStartupTaskOutcome.Failed;
        }

        if (process is null) return ElevatedStartupTaskOutcome.Failed;
        using (process)
        {
            if (!process.WaitForExit((int)ChildTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* best effort */ }
                AppLog.Warn("TaskScheduler", "Elevated startup-task helper did not exit in time.");
                return ElevatedStartupTaskOutcome.Failed;
            }
            return process.ExitCode == 0 ? ElevatedStartupTaskOutcome.Created : ElevatedStartupTaskOutcome.Failed;
        }
    }
}

/// <summary>The <c>--ensure-startup-task</c> elevated entrypoint (invoked from <see cref="Program"/>).
/// It creates ONLY the fixed Addon-owned task and exits: 0 = created/compliant, 1 = failed.</summary>
internal static class ElevatedStartupTaskSetup
{
    internal const string Argument = "--ensure-startup-task";

    public static int Run(string[] args)
    {
        try
        {
            string? currentUser = null;
            var index = Array.FindIndex(args, a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < args.Length)
            {
                try { currentUser = Encoding.UTF8.GetString(Convert.FromBase64String(args[index + 1])); }
                catch (FormatException) { currentUser = null; }
            }

            // No elevated fallback here: this process IS elevated, so RegisterTaskDefinition succeeds
            // directly. A null currentUser falls back to this (elevated) identity.
            var manager = string.IsNullOrWhiteSpace(currentUser)
                ? new WindowsTaskSchedulerStartupManager()
                : new WindowsTaskSchedulerStartupManager(currentUserIdentityProvider: () => currentUser);
            var result = manager.Synchronize(true);
            AppLog.Info("TaskScheduler", "Elevated startup-task ensure completed.", ("Success", result.Success), ("Message", result.Message));
            return result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            AppLog.Error("TaskScheduler", "Elevated startup-task ensure threw.", exception);
            return 1;
        }
    }
}
