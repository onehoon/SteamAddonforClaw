using Velopack;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Lifecycle;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Program is the sole owner of final log shutdown on every exit path.
        try
        {
            var restartRequested = args.Contains("--restart", StringComparer.OrdinalIgnoreCase);
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => UninstallBootstrap.RunFastCallbackOnly())
                .Run();
            AddonLogRetention.PruneDirectory(AppLog.DirectoryPath);
            var persistedLogLevel = LogLevelBootstrap.Read(AddonDataPaths.SettingsPath);
            AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(persistedLogLevel);
            AppLog.Info("App", "Application startup entered.", ("PID", Environment.ProcessId), ("RestartRequested", restartRequested), ("BackgroundRequested", args.Contains("--background", StringComparer.OrdinalIgnoreCase)));
            AppLog.Debug("Velopack", "Velopack bootstrap completed.");
            if (args.Contains(ElevatedPrerequisiteSetup.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedPrerequisiteSetup.Run();
                return;
            }

            var restartDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
            var restartAttempt = 0;
            SingleInstanceGate singleInstanceGate;
            while (true)
            {
                AppLog.Info("SingleInstance", "Single-instance check started.", ("RestartRequested", restartRequested), ("Attempt", restartAttempt + 1));
                singleInstanceGate = SingleInstanceGate.CreateForCurrentUser();
                if (singleInstanceGate.IsPrimaryInstance)
                {
                    if (restartRequested)
                    {
                        AppLog.Info("SingleInstance", "Previous instance lock released.", ("Attempt", restartAttempt + 1));
                    }
                    break;
                }

                if (!restartRequested)
                {
                    AppLog.Info("SingleInstance", "Secondary launch detected; activating the existing instance.", ("PID", Environment.ProcessId));
                    singleInstanceGate.ActivatePrimaryInstance();
                    return;
                }

                singleInstanceGate.Dispose();
                restartAttempt++;
                if (DateTimeOffset.UtcNow >= restartDeadline)
                {
                    AppLog.Error("SingleInstance", "Restart timeout while waiting for the previous instance to exit.", new TimeoutException("The previous instance did not release its single-instance lock."), ("Attempts", restartAttempt));
                    return;
                }

                AppLog.Debug("SingleInstance", "Restart waiting for previous instance.", ("Attempt", restartAttempt), ("RemainingMs", (restartDeadline - DateTimeOffset.UtcNow).TotalMilliseconds));
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            using (singleInstanceGate)
            {
                try
                {
                    var launchMode = args.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "Background" : "Manual";
                    AppLog.Info("App", "Application launch header.", ("Version", typeof(Program).Assembly.GetName().Version), ("LaunchMode", launchMode), ("PID", Environment.ProcessId), ("ProcessArchitecture", RuntimeInformation.ProcessArchitecture), ("OSArchitecture", RuntimeInformation.OSArchitecture), ("OS", Environment.OSVersion), ("Runtime", Environment.Version), ("ProcessPath", Environment.ProcessPath), ("BaseDirectory", AppContext.BaseDirectory));
                    new RuntimeProcessApplication(args, singleInstanceGate).Run();
                }
                finally
                {
                    AppLog.Shutdown();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Startup", "Fatal startup exception.", exception);
            throw;
        }
        finally
        {
            AppLog.Shutdown();
        }
    }
}
