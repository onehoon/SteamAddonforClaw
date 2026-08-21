using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Install;

public interface IWindowsStartupManager
{
    StartupRegistrationResult Synchronize(bool enabled);
}

public sealed class WindowsTaskSchedulerStartupManager : IWindowsStartupManager
{
    private const string TaskName = "Steam Input Addon for Claw";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValueName = "SteamInputAddonforClaw";
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private readonly Func<string> _stableExecutablePathProvider;
    private readonly Func<string> _currentUserIdentityProvider;

    public WindowsTaskSchedulerStartupManager(
        Func<string>? stableExecutablePathProvider = null,
        Func<string>? currentUserIdentityProvider = null)
    {
        _stableExecutablePathProvider = stableExecutablePathProvider ?? (() => VelopackAppPaths.StableExecutablePath);
        _currentUserIdentityProvider = currentUserIdentityProvider ?? (() => WindowsIdentity.GetCurrent().Name);
    }

    public StartupRegistrationResult Synchronize(bool enabled)
    {
        var stableExecutablePath = _stableExecutablePathProvider();
        AppLog.Info("TaskScheduler", "Startup task synchronization started.", ("Enabled", enabled), ("TaskName", TaskName), ("ExecutablePath", stableExecutablePath), ("Arguments", "--background"));
        if (!File.Exists(stableExecutablePath))
        {
            AppLog.Warn("TaskScheduler", "Startup task synchronization skipped.", null, ("Reason", "StableExecutableMissing"));
            return StartupRegistrationResult.NotInstalled();
        }

        try
        {
            RemoveLegacyRunValue();
            dynamic service = ConnectToTaskService();
            dynamic rootFolder = service.GetFolder("\\");
            if (!enabled)
            {
                DeleteOwnedTaskIfPresent(rootFolder);
                AppLog.Info("TaskScheduler", "Startup task deleted.");
                return StartupRegistrationResult.Disabled();
            }

            var configuration = CreateTaskConfiguration(stableExecutablePath, _currentUserIdentityProvider());
            dynamic taskDefinition = service.NewTask(0);
            taskDefinition.RegistrationInfo.Description = "Starts Steam Input Addon for Claw after Windows logon.";
            taskDefinition.Principal.UserId = configuration.UserId;
            taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;
            taskDefinition.Principal.RunLevel = 1;

            dynamic logonTrigger = taskDefinition.Triggers.Create(TaskTriggerLogon);
            logonTrigger.UserId = configuration.UserId;

            dynamic action = taskDefinition.Actions.Create(TaskActionExec);
            action.Path = configuration.ExecutablePath;
            action.Arguments = "--background";

            rootFolder.RegisterTaskDefinition(
                configuration.TaskName,
                taskDefinition,
                TaskCreateOrUpdate,
                Type.Missing,
                Type.Missing,
                TaskLogonInteractiveToken,
                Type.Missing);
            AppLog.Info("TaskScheduler", "Startup task registered.");
            return StartupRegistrationResult.Enabled();
        }
        catch (COMException exception)
        {
            Debug.WriteLine($"Task Scheduler startup registration failed. HRESULT=0x{exception.HResult:X8}");
            AppLog.Error("TaskScheduler", "Task Scheduler operation failed.", exception, ("HRESULT", $"0x{exception.HResult:X8}"));
            return StartupRegistrationResult.Failed();
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Task Scheduler startup registration was denied. {exception.Message}");
            AppLog.Error("TaskScheduler", "Task Scheduler operation was denied.", exception);
            return StartupRegistrationResult.Failed();
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Task Scheduler startup registration failed. {exception.Message}");
            AppLog.Error("TaskScheduler", "Task Scheduler IO operation failed.", exception);
            return StartupRegistrationResult.Failed();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Task Scheduler startup registration failed. {exception.Message}");
            AppLog.Error("TaskScheduler", "Task Scheduler operation failed.", exception);
            return StartupRegistrationResult.Failed();
        }
    }

    internal static ScheduledTaskConfiguration CreateTaskConfiguration(string stableExecutablePath, string currentUserId) =>
        new(TaskName, stableExecutablePath, currentUserId);

    private static dynamic ConnectToTaskService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Task Scheduler could not be created.");
        service.Connect();
        return service;
    }

    private static void DeleteOwnedTaskIfPresent(dynamic rootFolder)
    {
        try
        {
            rootFolder.DeleteTask(TaskName, 0);
        }
        catch (COMException exception) when (exception.HResult == FileNotFoundHResult)
        {
        }
    }

    private static void RemoveLegacyRunValue()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
    }
}

internal sealed record ScheduledTaskConfiguration(string TaskName, string ExecutablePath, string UserId);

public sealed record StartupRegistrationResult(bool Success, string Message)
{
    public static StartupRegistrationResult Enabled() => new(true, "Launch at Windows startup is enabled.");

    public static StartupRegistrationResult Disabled() => new(true, "Launch at Windows startup is disabled.");

    public static StartupRegistrationResult NotInstalled() => new(false, "Windows startup is available after Velopack installation.");

    public static StartupRegistrationResult Failed() => new(false, "Windows startup setting could not be applied.");
}
