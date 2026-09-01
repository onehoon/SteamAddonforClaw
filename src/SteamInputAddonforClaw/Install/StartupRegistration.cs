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

internal enum StartupTaskWriteOutcome { Registered, AccessDenied, Failed }

/// <summary>The observable contract of the one Addon-owned Task Scheduler task (PR10 addendum
/// sections 15/17). All values are read straight from the registered task so
/// <see cref="WindowsTaskSchedulerStartupManager"/> can verify it WITHOUT rewriting it.</summary>
internal sealed record OwnedStartupTaskState(
    bool Enabled,
    string ActionPath,
    string ActionArguments,
    string LogonTriggerUserId,
    int LogonType,
    int RunLevel);

/// <summary>Narrow seam over the Task Scheduler COM API for the one Addon-owned task, so the
/// register/verify policy can be unit tested without a real scheduler.</summary>
internal interface IOwnedStartupTaskStore
{
    /// <summary>The current owned task, or <see langword="null"/> when it is missing/unreadable.</summary>
    OwnedStartupTaskState? Read();
    StartupTaskWriteOutcome Register(ScheduledTaskConfiguration configuration);
    void Delete();
}

public sealed class WindowsTaskSchedulerStartupManager : IWindowsStartupManager
{
    internal const string TaskName = "Steam Input Addon for Claw";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValueName = "SteamInputAddonforClaw";
    internal const int TaskLogonInteractiveToken = 3;

    private readonly Func<string> _stableExecutablePathProvider;
    private readonly Func<string> _currentUserIdentityProvider;
    private readonly IOwnedStartupTaskStore _taskStore;
    // Null == no elevated fallback (unit tests, and the elevated child process itself). Production
    // wires SelfElevatedStartupTaskInvoker so a first, access-denied registration can still succeed.
    private readonly IElevatedStartupTaskInvoker? _elevatedInvoker;

    public WindowsTaskSchedulerStartupManager(
        Func<string>? stableExecutablePathProvider = null,
        Func<string>? currentUserIdentityProvider = null)
        : this(stableExecutablePathProvider, currentUserIdentityProvider, null, null) { }

    /// <summary>The production instance: an access-denied first registration falls back to a bounded
    /// elevated child (PR10 addendum section 16).</summary>
    internal static WindowsTaskSchedulerStartupManager WithElevatedRepair() =>
        new(null, null, null, new SelfElevatedStartupTaskInvoker());

    internal WindowsTaskSchedulerStartupManager(
        Func<string>? stableExecutablePathProvider,
        Func<string>? currentUserIdentityProvider,
        IOwnedStartupTaskStore? taskStore,
        IElevatedStartupTaskInvoker? elevatedInvoker)
    {
        _stableExecutablePathProvider = stableExecutablePathProvider ?? (() => VelopackAppPaths.StableExecutablePath);
        _currentUserIdentityProvider = currentUserIdentityProvider ?? (() => WindowsIdentity.GetCurrent().Name);
        _taskStore = taskStore ?? new WindowsOwnedStartupTaskStore();
        _elevatedInvoker = elevatedInvoker;
    }

    public StartupRegistrationResult Synchronize(bool enabled)
    {
        var stableExecutablePath = _stableExecutablePathProvider();
        AppLog.Info("TaskScheduler", "Startup task synchronization started.", ("Enabled", enabled), ("TaskName", TaskName), ("ExecutablePath", stableExecutablePath), ("Arguments", "--background"));
        RemoveLegacyRunValue();

        if (!enabled)
        {
            try { _taskStore.Delete(); AppLog.Info("TaskScheduler", "Startup task deleted."); return StartupRegistrationResult.Disabled(); }
            catch (Exception exception) { AppLog.Error("TaskScheduler", "Startup task deletion failed.", exception); return StartupRegistrationResult.Failed(); }
        }

        if (!File.Exists(stableExecutablePath))
        {
            AppLog.Warn("TaskScheduler", "Startup task synchronization skipped.", null, ("Reason", "StableExecutableMissing"));
            return StartupRegistrationResult.NotInstalled();
        }

        var configuration = CreateTaskConfiguration(stableExecutablePath, _currentUserIdentityProvider());

        // 1. Read-only verification first: an already-compliant task returns Success with no
        //    RegisterTaskDefinition call and no UAC (PR10 addendum section 15).
        var current = SafeRead();
        if (current is not null && IsCompliant(current, configuration))
        {
            AppLog.Info("TaskScheduler", "Startup task already compliant.", ("TaskFound", true), ("TaskCompliant", true));
            return StartupRegistrationResult.Enabled();
        }

        AppLog.Info("TaskScheduler", "Startup task repair required.", ("TaskFound", current is not null), ("RepairRequired", true));

        // 2. Ordinary (non-elevated) registration -- proven only by an exact non-elevated readback,
        //    the same contract the elevated path uses (PR10 addendum section 15, review [P1]). This
        //    task is the mandatory next-logon Runtime guarantee before Center M can be disabled.
        var write = _taskStore.Register(configuration);
        if (write == StartupTaskWriteOutcome.Registered)
        {
            var rereadAfterWrite = SafeRead();
            var writeVerified = rereadAfterWrite is not null && IsCompliant(rereadAfterWrite, configuration);
            AppLog.Info("TaskScheduler", "Startup task registered.", ("ReadbackVerified", writeVerified));
            return writeVerified ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Failed();
        }
        if (write == StartupTaskWriteOutcome.Failed)
            return StartupRegistrationResult.Failed();

        // 3. write == AccessDenied. The first creation on a clean machine can be denied from the
        //    normal Runtime process; a bounded elevated child creates exactly this one task and exits.
        if (_elevatedInvoker is null)
        {
            AppLog.Warn("TaskScheduler", "Startup task registration was denied and no elevated repair path is available.", null);
            return StartupRegistrationResult.Failed();
        }

        AppLog.Info("TaskScheduler", "Startup task elevated repair requested.", ("ElevatedRepairRequested", true));
        var outcome = _elevatedInvoker.EnsureOwnedTask();
        AppLog.Info("TaskScheduler", "Startup task elevated repair completed.", ("ElevatedRepairResult", outcome));
        if (outcome != ElevatedStartupTaskOutcome.Created)
            return StartupRegistrationResult.Failed();

        // 4. Never trust the elevated child's exit code alone -- prove the task by a non-elevated readback.
        var reread = SafeRead();
        var verified = reread is not null && IsCompliant(reread, configuration);
        AppLog.Info("TaskScheduler", "Startup task readback verification completed.", ("ReadbackVerified", verified));
        return verified ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Failed();
    }

    private OwnedStartupTaskState? SafeRead()
    {
        try { return _taskStore.Read(); }
        catch (Exception exception) { AppLog.Warn("TaskScheduler", "Startup task read failed.", exception); return null; }
    }

    internal static bool IsCompliant(OwnedStartupTaskState state, ScheduledTaskConfiguration configuration) =>
        state.Enabled
        && PathEquals(state.ActionPath, configuration.ExecutablePath)
        && string.Equals(state.ActionArguments.Trim(), "--background", StringComparison.Ordinal)
        && state.LogonType == TaskLogonInteractiveToken
        && state.RunLevel == 0
        && string.Equals(state.LogonTriggerUserId, configuration.UserId, StringComparison.OrdinalIgnoreCase);

    internal static ScheduledTaskConfiguration CreateTaskConfiguration(string stableExecutablePath, string currentUserId) =>
        new(TaskName, stableExecutablePath, currentUserId);

    private static void RemoveLegacyRunValue()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) { AppLog.Warn("TaskScheduler", "Legacy Run value cleanup failed.", exception); }
    }

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

/// <summary>Production <see cref="IOwnedStartupTaskStore"/> over the Task Scheduler COM API. Reads
/// the single Addon-owned task's contract for verification and registers/deletes only that one task
/// -- there is no generic "create any task" surface (PR10 addendum section 16).</summary>
internal sealed class WindowsOwnedStartupTaskStore : IOwnedStartupTaskStore
{
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;

    public OwnedStartupTaskState? Read()
    {
        try
        {
            dynamic service = ConnectToTaskService();
            dynamic rootFolder = service.GetFolder("\\");
            dynamic task;
            try { task = rootFolder.GetTask(WindowsTaskSchedulerStartupManager.TaskName); }
            catch (COMException exception) when (exception.HResult == FileNotFoundHResult) { return null; }

            bool enabled = task.Enabled;
            dynamic definition = task.Definition;
            dynamic actions = definition.Actions;
            string actionPath = string.Empty;
            string actionArguments = string.Empty;
            if (actions.Count >= 1)
            {
                dynamic action = actions[1];
                if ((int)action.Type == TaskActionExec)
                {
                    actionPath = (string)(action.Path ?? string.Empty);
                    actionArguments = (string)(action.Arguments ?? string.Empty);
                }
            }

            dynamic principal = definition.Principal;
            int logonType = (int)principal.LogonType;
            int runLevel = (int)principal.RunLevel;

            string logonTriggerUserId = string.Empty;
            dynamic triggers = definition.Triggers;
            for (var i = 1; i <= (int)triggers.Count; i++)
            {
                dynamic trigger = triggers[i];
                if ((int)trigger.Type == TaskTriggerLogon)
                {
                    logonTriggerUserId = (string)(trigger.UserId ?? string.Empty);
                    break;
                }
            }

            return new OwnedStartupTaskState(enabled, actionPath, actionArguments, logonTriggerUserId, logonType, runLevel);
        }
        catch (Exception exception)
        {
            AppLog.Warn("TaskScheduler", "Owned startup task could not be read.", exception);
            return null;
        }
    }

    public StartupTaskWriteOutcome Register(ScheduledTaskConfiguration configuration)
    {
        try
        {
            dynamic service = ConnectToTaskService();
            dynamic rootFolder = service.GetFolder("\\");
            dynamic taskDefinition = service.NewTask(0);
            taskDefinition.RegistrationInfo.Description = "Starts Steam Input Addon for Claw after Windows logon.";
            taskDefinition.Principal.UserId = configuration.UserId;
            taskDefinition.Principal.LogonType = WindowsTaskSchedulerStartupManager.TaskLogonInteractiveToken;
            taskDefinition.Principal.RunLevel = 0;

            dynamic logonTrigger = taskDefinition.Triggers.Create(TaskTriggerLogon);
            logonTrigger.UserId = configuration.UserId;

            dynamic action = taskDefinition.Actions.Create(TaskActionExec);
            action.Path = configuration.ExecutablePath;
            action.Arguments = "--background";

            rootFolder.RegisterTaskDefinition(
                configuration.TaskName, taskDefinition, TaskCreateOrUpdate,
                Type.Missing, Type.Missing, WindowsTaskSchedulerStartupManager.TaskLogonInteractiveToken, Type.Missing);
            return StartupTaskWriteOutcome.Registered;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Info("TaskScheduler", "Startup task registration was denied.", ("HRESULT", $"0x{exception.HResult:X8}"));
            return StartupTaskWriteOutcome.AccessDenied;
        }
        catch (COMException exception) when (exception.HResult == AccessDeniedHResult)
        {
            AppLog.Info("TaskScheduler", "Startup task registration was denied.", ("HRESULT", $"0x{exception.HResult:X8}"));
            return StartupTaskWriteOutcome.AccessDenied;
        }
        catch (COMException exception)
        {
            AppLog.Error("TaskScheduler", "Task Scheduler operation failed.", exception, ("HRESULT", $"0x{exception.HResult:X8}"));
            return StartupTaskWriteOutcome.Failed;
        }
        catch (Exception exception)
        {
            AppLog.Error("TaskScheduler", "Task Scheduler operation failed.", exception);
            return StartupTaskWriteOutcome.Failed;
        }
    }

    public void Delete()
    {
        dynamic service = ConnectToTaskService();
        dynamic rootFolder = service.GetFolder("\\");
        try { rootFolder.DeleteTask(WindowsTaskSchedulerStartupManager.TaskName, 0); }
        catch (COMException exception) when (exception.HResult == FileNotFoundHResult) { }
    }

    private static dynamic ConnectToTaskService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Task Scheduler could not be created.");
        service.Connect();
        return service;
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
