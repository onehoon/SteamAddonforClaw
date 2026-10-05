using Velopack;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Lifecycle;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Updates;
using SteamInputAddonforClaw.WindowsGaming;

namespace SteamInputAddonforClaw;

public static class Program
{
    internal const string OriginatingUserSidArgument = "--elevated-runtime-origin-sid";
    private const int ElevationCancelledErrorCode = 1223;

    [STAThread]
    public static void Main(string[] args)
    {
        // Program is the sole owner of final log shutdown on every exit path.
        var runtimeLifetimeEntered = false;
        try
        {
            var restartRequested = args.Contains("--restart", StringComparer.OrdinalIgnoreCase);
            VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .OnAfterInstallFastCallback(hook => { SafeUninstallRegistration.EnsureCurrentInstallation(VelopackAppPaths.RootAppDirectory); })
                .OnAfterUpdateFastCallback(hook => { SafeUninstallRegistration.EnsureCurrentInstallation(VelopackAppPaths.RootAppDirectory); })
                .OnBeforeUninstallFastCallback(_ => UninstallBootstrap.RunFastCallbackOnly())
                .Run();
            if (UninstallBootstrap.IsSafeUninstallApproved)
                return;

            if (!args.Contains(SafeUninstall.Argument, StringComparer.OrdinalIgnoreCase))
                AddonLogRetention.PruneDirectory(AppLog.DirectoryPath);
            var persistedLogLevel = LogLevelBootstrap.Read(AddonDataPaths.SettingsPath);
            AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(persistedLogLevel);
            AppLog.Info("App", "Application startup entered.", ("PID", Environment.ProcessId), ("RestartRequested", restartRequested), ("BackgroundRequested", args.Contains("--background", StringComparer.OrdinalIgnoreCase)));
            AppLog.Debug("Velopack", "Velopack bootstrap completed.");
            var uninstallRegistration = SafeUninstallRegistration.EnsureCurrentInstallation(VelopackAppPaths.RootAppDirectory);
            if (uninstallRegistration.Success)
                AppLog.Info("Uninstall", "Safe Windows uninstall entry verified.", ("Result", uninstallRegistration.Reason));
            else
                AppLog.Warn("Uninstall", "Safe Windows uninstall entry could not be repaired.", null, ("Reason", uninstallRegistration.Reason));
            if (args.Contains(SafeUninstall.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SafeUninstall.Run(args.Contains(SafeUninstallRegistration.SilentArgument, StringComparer.OrdinalIgnoreCase));
                return;
            }
            if (args.Contains(SteamFseElevatedRegistration.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SteamFseElevatedRegistration.Run();
                return;
            }
            if (args.Contains(ElevatedPrerequisiteSetup.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedPrerequisiteSetup.Run();
                return;
            }
            if (args.Contains(ElevatedOwnedPrerequisiteUninstallEntry.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedOwnedPrerequisiteUninstallEntry.Run();
                return;
            }
            if (args.Contains(ElevatedWindowsAppRuntimeSetup.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedWindowsAppRuntimeSetup.Run();
                return;
            }
            if (args.Contains(ElevatedStartupTaskSetup.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedStartupTaskSetup.Run(args);
                return;
            }
            if (args.Contains(ElevatedStartupTaskSetup.RemoveArgument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedStartupTaskSetup.RunRemove(args);
                return;
            }

            if (!TryExtractOriginatingUserSid(args, out var originatingUserSid, out var runtimeArgs))
            {
                AppLog.Error("Elevation", "Invalid elevated-runtime user handoff; normal Runtime startup is blocked.", null);
                return;
            }

            string? currentUserSid;
            using (var currentIdentity = WindowsIdentity.GetCurrent())
                currentUserSid = currentIdentity.User?.Value;
            if (string.IsNullOrWhiteSpace(currentUserSid))
            {
                AppLog.Error("Elevation", "The current Windows user SID is unavailable; normal Runtime startup is blocked.", null);
                return;
            }

            if (originatingUserSid is not null && !OriginatingUserSidMatches(originatingUserSid, currentUserSid))
            {
                AppLog.Error("Elevation", "Elevated Runtime user does not match the originating interactive user; normal Runtime startup is blocked.",
                    null, ("OriginatingUserSid", originatingUserSid), ("CurrentUserSid", currentUserSid));
                return;
            }

            if (!IsCurrentProcessElevated())
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    throw new InvalidOperationException("The current executable path is unavailable for Runtime elevation.");

                try
                {
                    using var elevatedProcess = Process.Start(CreateElevationStartInfo(executablePath, args, currentUserSid));
                    if (elevatedProcess is null)
                    {
                        AppLog.Error("Elevation", "Windows did not start the elevated Runtime; normal Runtime startup is blocked.",
                            null, ("OriginatingUserSid", currentUserSid));
                        return;
                    }

                    AppLog.Info("Elevation", "Elevated Runtime replacement started; medium bootstrap is exiting.",
                        ("OriginatingUserSid", currentUserSid), ("ElevatedProcessId", elevatedProcess.Id));
                    return;
                }
                catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelledErrorCode)
                {
                    AppLog.Warn("Elevation", "Runtime elevation was cancelled; normal Runtime startup is blocked.", exception,
                        ("OriginatingUserSid", currentUserSid), ("ErrorCode", exception.NativeErrorCode));
                    return;
                }
                catch (Exception exception)
                {
                    AppLog.Error("Elevation", "Runtime elevation failed; normal Runtime startup is blocked.", exception,
                        ("OriginatingUserSid", currentUserSid));
                    return;
                }
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
                if (new VelopackUpdateClient().TrySchedulePendingUpdateApply(runtimeArgs))
                    return;

                runtimeLifetimeEntered = true;
                try
                {
                    var launchMode = runtimeArgs.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "Background" : "Manual";
                    AppLog.Info("App", "Application launch header.", ("Version", typeof(Program).Assembly.GetName().Version), ("LaunchMode", launchMode), ("PID", Environment.ProcessId), ("ProcessArchitecture", RuntimeInformation.ProcessArchitecture), ("OSArchitecture", RuntimeInformation.OSArchitecture), ("OS", Environment.OSVersion), ("Runtime", Environment.Version), ("ProcessPath", Environment.ProcessPath), ("BaseDirectory", AppContext.BaseDirectory));
                    new RuntimeProcessApplication(runtimeArgs, singleInstanceGate).Run();
                }
                catch (Exception exception)
                {
                    AppLog.Fatal("Startup", "Fatal runtime exception.", exception);
                    throw;
                }
                finally
                {
                    AppLog.Shutdown();
                }
            }
        }
        catch (Exception exception)
        {
            if (!runtimeLifetimeEntered)
                AppLog.Fatal("Startup", "Fatal startup exception.", exception);
            throw;
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    internal static ProcessStartInfo CreateElevationStartInfo(string executablePath, string[] args, string originatingUserSid)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (var argument in args)
            startInfo.ArgumentList.Add(argument);

        startInfo.ArgumentList.Add(OriginatingUserSidArgument);
        startInfo.ArgumentList.Add(originatingUserSid);
        return startInfo;
    }

    internal static bool TryExtractOriginatingUserSid(string[] args, out string? originatingUserSid, out string[] runtimeArgs)
    {
        var handoffIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, OriginatingUserSidArgument, StringComparison.OrdinalIgnoreCase));
        if (handoffIndex < 0)
        {
            originatingUserSid = null;
            runtimeArgs = args;
            return true;
        }

        if (handoffIndex + 1 >= args.Length
            || string.IsNullOrWhiteSpace(args[handoffIndex + 1])
            || Array.FindIndex(args, handoffIndex + 1, argument =>
                string.Equals(argument, OriginatingUserSidArgument, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            originatingUserSid = null;
            runtimeArgs = [];
            return false;
        }

        originatingUserSid = args[handoffIndex + 1];
        runtimeArgs = args.Where((_, index) => index != handoffIndex && index != handoffIndex + 1).ToArray();
        return true;
    }

    internal static bool OriginatingUserSidMatches(string originatingUserSid, string currentUserSid) =>
        string.Equals(originatingUserSid, currentUserSid, StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
