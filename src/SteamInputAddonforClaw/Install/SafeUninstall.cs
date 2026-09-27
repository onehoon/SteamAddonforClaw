using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.Install;

internal enum DirectoryDeletionStatus { Deleted, AlreadyAbsent, Failed }
internal sealed record DirectoryDeletionResult(DirectoryDeletionStatus Status, string Reason)
{
    internal bool Succeeded => Status is DirectoryDeletionStatus.Deleted or DirectoryDeletionStatus.AlreadyAbsent;
}
internal enum FinalUninstallHandoffResult { Launched, DataRootRemovalFailed, UpdaterLaunchFailed }

internal static class BoundedDirectoryDeletion
{
    internal const int MaximumAttempts = 5;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    internal static DirectoryDeletionResult Delete(
        string path,
        Func<string, bool>? directoryExists = null,
        Action<string>? deleteDirectory = null,
        Action<TimeSpan>? delay = null)
    {
        directoryExists ??= Directory.Exists;
        deleteDirectory ??= static directory => Directory.Delete(directory, recursive: true);
        delay ??= static duration => Thread.Sleep(duration);

        if (string.IsNullOrWhiteSpace(path)) return new(DirectoryDeletionStatus.Failed, "DirectoryPathMissing");
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) { return new(DirectoryDeletionStatus.Failed, "InvalidPath:" + exception.GetType().Name); }

        try
        {
            if (!directoryExists(fullPath)) return new(DirectoryDeletionStatus.AlreadyAbsent, "AlreadyAbsent");
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return new(DirectoryDeletionStatus.Failed, "DirectoryIsReparsePoint");
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try { deleteDirectory(fullPath); }
                catch (IOException) when (attempt < MaximumAttempts) { }
                catch (UnauthorizedAccessException) when (attempt < MaximumAttempts) { }
                catch (IOException exception) { return new(DirectoryDeletionStatus.Failed, "DeleteFailed:" + exception.GetType().Name); }
                catch (UnauthorizedAccessException exception) { return new(DirectoryDeletionStatus.Failed, "DeleteFailed:" + exception.GetType().Name); }

                if (!directoryExists(fullPath)) return new(DirectoryDeletionStatus.Deleted, "Deleted");
                if (attempt < MaximumAttempts) delay(RetryDelay);
            }
            return new(DirectoryDeletionStatus.Failed, "DirectoryStillExistsAfterBoundedAttempts");
        }
        catch (Exception exception)
        {
            return new(DirectoryDeletionStatus.Failed, "VerificationFailed:" + exception.GetType().Name);
        }
    }
}

internal static class SafeUninstall
{
    internal const string Argument = SafeUninstallRegistration.SafeUninstallArgument;
    private const string AttemptMutexName = @"Local\SteamInputAddonforClaw.SafeUninstall";
    private static readonly TimeSpan RuntimeReleaseBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RuntimeProbeInterval = TimeSpan.FromMilliseconds(100);
    private const int ElevatedHelperWaitBudgetMilliseconds = 6 * 60 * 1000;

    internal static int Run(bool silent)
    {
        var root = VelopackAppPaths.RootAppDirectory;
        if (!SafeUninstallRegistration.TryValidateCurrentInstallation(
                root, Environment.ProcessPath, out var updaterPath))
            return Abort(silent, "The Addon installation could not be verified. No files were removed.");

        using var attemptMutex = new Mutex(initiallyOwned: true, AttemptMutexName, out var ownsAttempt);
        if (!ownsAttempt)
            return Abort(silent, "Another Addon uninstall attempt is already running.");

        var runtimeGate = UninstallBootstrap.AcquireRuntimeGateForSafeUninstall(RuntimeReleaseBudget, RuntimeProbeInterval);
        if (runtimeGate is null)
            return Abort(silent, "The running Addon Runtime did not complete safe shutdown. No uninstall was started.");

        var logShutdown = false;
        try
        {
            var host = new AddonProcessHost(headlessUninstallPreparation: true);
            try
            {
                var startup = host.RunStartupAsync().GetAwaiter().GetResult();
                if (startup != AddonProcessStartupOutcome.RuntimeReady)
                    return Abort(silent, "Current controller safety could not be established. No uninstall was started.");

                host.InitializeRuntimeAsync().GetAwaiter().GetResult();
                var stock = host.PrepareForUninstallAsync().GetAwaiter().GetResult();
                if (stock is not { Succeeded: true })
                {
                    AppLog.Warn("Uninstall", "Independent PR12 stock-safety proof failed; final uninstall was blocked.", null,
                        ("Reason", stock?.Reason ?? "StockPreparationUnavailable"));
                    return Abort(silent, "The controller could not be proven stock-safe. No uninstall was started.");
                }

                AppLog.Info("Uninstall", "Independent PR12 stock-safety proof succeeded.", ("Reason", stock.Reason));
                var clawHud = host.StopManagedClawHudForUninstallAsync().GetAwaiter().GetResult();
                if (!clawHud.Succeeded)
                {
                    AppLog.Warn("Uninstall.ClawHUD", "Managed ClawHUD shutdown could not be confirmed; final uninstall was blocked.", null,
                        ("Reason", clawHud.Reason));
                    return Abort(silent, "Managed ClawHUD could not be safely stopped. No uninstall was started.");
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Uninstall", "Headless stock-safety preparation failed; final uninstall was blocked.", exception);
                return Abort(silent, "The Addon could not verify a safe uninstall state. No uninstall was started.");
            }
            finally
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            var dependencyResult = RunElevatedDependencyCleanup(root);
            if (dependencyResult is null || !dependencyResult.Succeeded)
                return Abort(silent, "Owned prerequisite cleanup did not complete. No uninstall was started.");
            if (dependencyResult.RestartRequired && !silent)
                NativeStartupWarning.Show("A Windows restart is required after uninstall to finish removing the Addon-owned controller drivers.");

            if (!UninstallBootstrap.RunBoundedLocalCleanup(runtimeReleased: true, deleteDataRoot: false))
                return Abort(silent, "Addon-owned local cleanup could not be completed. No uninstall was started.");

            var clawHudCache = BoundedDirectoryDeletion.Delete(AddonDataPaths.ClawHudRuntimeRoot);
            AppLog.Info("Uninstall", "Managed ClawHUD cache removal completed.",
                ("Status", clawHudCache.Status), ("Reason", clawHudCache.Reason));
            if (!clawHudCache.Succeeded)
                return Abort(silent, "Managed ClawHUD files could not be removed. No uninstall was started.");

            AppLog.Info("Uninstall", "Safe uninstall preparation completed; final Addon data-root removal is starting.",
                ("RestartRequired", dependencyResult.RestartRequired));
            var handoff = DeleteDataRootAndLaunchVeloPack(
                AddonDataPaths.RootDirectory, updaterPath, root, silent,
                () => { AppLog.Shutdown(); logShutdown = true; }, path => BoundedDirectoryDeletion.Delete(path), LaunchProcess);
            return handoff switch
            {
                FinalUninstallHandoffResult.Launched => 0,
                FinalUninstallHandoffResult.DataRootRemovalFailed => Abort(silent, "Addon data files could not be removed. No uninstall was started."),
                _ => Abort(silent, "The final VeloPack uninstall could not be started. Run uninstall again to retry.")
            };
        }
        finally
        {
            if (!logShutdown)
                AppLog.Info("Uninstall", "Safe uninstall attempt ended before final data cleanup.");
            if (logShutdown)
                runtimeGate.DisposeWithoutLogging();
            else
                runtimeGate.Dispose();
        }
    }

    private static OwnedPrerequisiteUninstallResult? RunElevatedDependencyCleanup(string root)
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath)
                || !string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(Path.Combine(root, "SteamInputAddonforClaw.exe")), StringComparison.OrdinalIgnoreCase))
                return new(false, false, "StableAddonExecutablePathChanged");

            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = root,
            };
            startInfo.ArgumentList.Add(ElevatedOwnedPrerequisiteUninstallEntry.Argument);
            using var process = Process.Start(startInfo);
            if (process is null) return new(false, false, "ElevatedHelperDidNotStart");
            if (!process.WaitForExit(ElevatedHelperWaitBudgetMilliseconds))
                return new(false, false, "ElevatedHelperWaitTimedOut");
            return process.ExitCode switch
            {
                0 => new(true, false, "OwnedPrerequisiteCleanupCompleted"),
                3010 => new(true, true, "OwnedPrerequisiteCleanupCompletedRestartRequired"),
                _ => new(false, false, "ElevatedHelperExitCode:" + process.ExitCode)
            };
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            AppLog.Info("Uninstall.Dependency", "User cancelled elevated prerequisite cleanup.", ("Result", "UacCancelled"));
            return new(false, false, "UacCancelled");
        }
        catch (Exception exception)
        {
            AppLog.Error("Uninstall.Dependency", "Elevated prerequisite cleanup could not be completed.", exception);
            return new(false, false, "ElevatedHelperFailed:" + exception.GetType().Name);
        }
    }

    internal static FinalUninstallHandoffResult DeleteDataRootAndLaunchVeloPack(
        string dataRoot,
        string updaterPath,
        string root,
        bool silent,
        Action shutdownLogs,
        Func<string, DirectoryDeletionResult> deleteDirectory,
        Func<ProcessStartInfo, bool> launch)
    {
        DirectoryDeletionResult deletion;
        try
        {
            shutdownLogs();
            deletion = deleteDirectory(dataRoot);
        }
        catch { return FinalUninstallHandoffResult.DataRootRemovalFailed; }
        if (!deletion.Succeeded) return FinalUninstallHandoffResult.DataRootRemovalFailed;

        try
        {
            var startInfo = CreateVeloPackUninstallStartInfo(updaterPath, root, silent);
            return startInfo is not null && launch(startInfo)
                ? FinalUninstallHandoffResult.Launched
                : FinalUninstallHandoffResult.UpdaterLaunchFailed;
        }
        catch { return FinalUninstallHandoffResult.UpdaterLaunchFailed; }
    }

    internal static ProcessStartInfo? CreateVeloPackUninstallStartInfo(string updaterPath, string root, bool silent)
    {
        try
        {
            if (!string.Equals(Path.GetFullPath(updaterPath), Path.GetFullPath(Path.Combine(root, "Update.exe")), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(updaterPath))
                return null;

            var startInfo = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                WorkingDirectory = root,
            };
            startInfo.ArgumentList.Add("uninstall");
            if (silent) startInfo.ArgumentList.Add("--silent");
            startInfo.Environment[UninstallBootstrap.SafeUninstallApprovedEnvironmentVariable] = "1";
            return startInfo;
        }
        catch
        {
            return null;
        }
    }

    private static bool LaunchProcess(ProcessStartInfo startInfo) => Process.Start(startInfo) is not null;

    private static int Abort(bool silent, string message)
    {
        if (!silent)
            NativeStartupWarning.Show(message);
        return 1;
    }
}

internal static class ElevatedOwnedPrerequisiteUninstallEntry
{
    internal const string Argument = "--uninstall-owned-prerequisites";

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            AppLog.Warn("Uninstall.Dependency", "Owned prerequisite helper refused to run without elevation.");
            return 1;
        }

        var result = new ElevatedOwnedPrerequisiteUninstall().Execute();
        AppLog.Info("Uninstall.Dependency", "Elevated owned-prerequisite uninstall helper completed.",
            ("Succeeded", result.Succeeded), ("RestartRequired", result.RestartRequired), ("Reason", result.Reason));
        return result.Succeeded ? result.RestartRequired ? 3010 : 0 : 1;
    }
}
