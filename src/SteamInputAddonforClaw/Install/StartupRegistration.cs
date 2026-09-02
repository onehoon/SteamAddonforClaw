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
    int RunLevel,
    // PR10 review [P1]: the mandatory persistent handheld Runtime must survive being on battery and
    // must have no scheduler execution-time limit.
    bool DisallowStartIfOnBatteries,
    bool StopIfGoingOnBatteries,
    string ExecutionTimeLimit);

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
    // Null == no elevated repair path (unit tests, and the elevated `--ensure-startup-task` child
    // itself, which writes directly). Production wires SelfElevatedStartupTaskInvoker so a
    // missing/drifted task is repaired via one bounded elevated child WITHOUT a known-denied
    // parent-process write first (PR11 section 11).
    private readonly IElevatedStartupTaskInvoker? _elevatedInvoker;
    private readonly Action<TimeSpan> _sleep;
    // PR11 section 12: Task Scheduler can lag a normal-process read-back right after an elevated
    // create. A small bounded read-only settle absorbs that -- no repeated writes, no repeated
    // elevation, no unbounded retry.
    private readonly TimeSpan _readbackSettleWindow;
    private readonly TimeSpan _readbackSettleInterval;

    public WindowsTaskSchedulerStartupManager(
        Func<string>? stableExecutablePathProvider = null,
        Func<string>? currentUserIdentityProvider = null)
        : this(stableExecutablePathProvider, currentUserIdentityProvider, null, null) { }

    /// <summary>The production instance: a missing/drifted task is repaired via one bounded elevated
    /// child, then verified by an independent normal-process read-back (PR11 section 11).</summary>
    internal static WindowsTaskSchedulerStartupManager WithElevatedRepair() =>
        new(null, null, null, new SelfElevatedStartupTaskInvoker());

    internal WindowsTaskSchedulerStartupManager(
        Func<string>? stableExecutablePathProvider,
        Func<string>? currentUserIdentityProvider,
        IOwnedStartupTaskStore? taskStore,
        IElevatedStartupTaskInvoker? elevatedInvoker,
        Action<TimeSpan>? sleep = null,
        TimeSpan? readbackSettleWindow = null,
        TimeSpan? readbackSettleInterval = null)
    {
        _stableExecutablePathProvider = stableExecutablePathProvider ?? (() => VelopackAppPaths.StableExecutablePath);
        _currentUserIdentityProvider = currentUserIdentityProvider ?? (() => WindowsIdentity.GetCurrent().Name);
        _taskStore = taskStore ?? new WindowsOwnedStartupTaskStore();
        _elevatedInvoker = elevatedInvoker;
        _sleep = sleep ?? Thread.Sleep;
        _readbackSettleWindow = readbackSettleWindow ?? TimeSpan.FromSeconds(2);
        _readbackSettleInterval = readbackSettleInterval ?? TimeSpan.FromMilliseconds(150);
    }

    public StartupRegistrationResult Synchronize(bool enabled)
    {
        var stableExecutablePath = _stableExecutablePathProvider();
        AppLog.Info("TaskScheduler", "Startup task synchronization started.", ("Enabled", enabled), ("TaskName", TaskName), ("ExecutablePath", stableExecutablePath), ("Arguments", "--background"));
        RemoveLegacyRunValue();

        if (!enabled)
            return RemoveOwnedTask();

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

        // 2. Missing / materially drifted. The production Runtime has already proven on supported
        //    hardware that this write requires elevation, so when an elevated repair path exists,
        //    request it DIRECTLY -- no known-denied parent-process RegisterTaskDefinition first
        //    (PR11 section 11). A manager without an elevated invoker IS the elevated child (or a
        //    unit test) and writes directly.
        if (_elevatedInvoker is not null)
        {
            AppLog.Info("TaskScheduler", "Startup task elevated repair requested.", ("ElevatedRepairRequested", true));
            var outcome = _elevatedInvoker.EnsureOwnedTask();
            AppLog.Info("TaskScheduler", "Startup task elevated repair completed.", ("ElevatedRepairResult", outcome));
            if (outcome != ElevatedStartupTaskOutcome.Created)
                return StartupRegistrationResult.Failed();

            // 3. Never trust the elevated child's exit code alone -- prove the task by an independent
            //    normal-process read-back, allowing a small bounded settle for Task Scheduler lag.
            var settled = ReadBackVerifyWithBoundedSettle(configuration);
            AppLog.Info("TaskScheduler", "Startup task readback verification completed.", ("ReadbackVerified", settled));
            return settled ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Failed();
        }

        var write = _taskStore.Register(configuration);
        if (write != StartupTaskWriteOutcome.Registered)
        {
            if (write == StartupTaskWriteOutcome.AccessDenied)
                AppLog.Warn("TaskScheduler", "Startup task registration was denied and no elevated repair path is available.", null);
            return StartupRegistrationResult.Failed();
        }

        var reread = SafeRead();
        var verified = reread is not null && IsCompliant(reread, configuration);
        AppLog.Info("TaskScheduler", "Startup task registered.", ("ReadbackVerified", verified));
        return verified ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Failed();
    }

    /// <summary>PR12 section 11: remove the ONE Addon-owned task. Read-only first (already absent ->
    /// Success, no UAC). A denied delete uses the same bounded elevated child pattern as create, then
    /// an independent normal-process read-back must prove the task is gone.</summary>
    private StartupRegistrationResult RemoveOwnedTask()
    {
        // review [P1]: a Task Scheduler read failure must NOT be mistaken for verified absence.
        if (!TryRead(out var current))
        {
            AppLog.Warn("TaskScheduler", "Startup task removal aborted; the owned task could not be read.");
            return StartupRegistrationResult.Failed();
        }
        if (current is null)
        {
            AppLog.Info("TaskScheduler", "Startup task removal skipped; task is already absent.", ("TaskFound", false));
            return StartupRegistrationResult.Disabled();
        }

        if (_elevatedInvoker is not null)
        {
            AppLog.Info("TaskScheduler", "Startup task elevated removal requested.", ("ElevatedRepairRequested", true));
            var outcome = _elevatedInvoker.RemoveOwnedTask();
            AppLog.Info("TaskScheduler", "Startup task elevated removal completed.", ("ElevatedRepairResult", outcome));
            if (outcome != ElevatedStartupTaskOutcome.Removed)
                return StartupRegistrationResult.Failed();
            var gone = ReadBackVerifyAbsentWithBoundedSettle();
            AppLog.Info("TaskScheduler", "Startup task removal readback verification completed.", ("ReadbackVerified", gone));
            return gone ? StartupRegistrationResult.Disabled() : StartupRegistrationResult.Failed();
        }

        try { _taskStore.Delete(); }
        catch (Exception exception) { AppLog.Error("TaskScheduler", "Startup task deletion failed.", exception); return StartupRegistrationResult.Failed(); }
        var absent = TryRead(out var afterDelete) && afterDelete is null;
        AppLog.Info("TaskScheduler", "Startup task deleted.", ("ReadbackVerified", absent));
        return absent ? StartupRegistrationResult.Disabled() : StartupRegistrationResult.Failed();
    }

    /// <summary>PR11 section 12 / PR12 section 11: read-only, bounded. Re-reads on a short interval
    /// until the exact task contract verifies (or, for <paramref name="absent"/>, the task is gone)
    /// or the window expires. No repeated writes / elevation / unbounded retry.</summary>
    private bool ReadBackVerifyWithBoundedSettle(ScheduledTaskConfiguration configuration)
    {
        var attempts = Math.Max(1, (int)Math.Ceiling(_readbackSettleWindow / _readbackSettleInterval));
        for (var attempt = 1; ; attempt++)
        {
            var reread = SafeRead();
            if (reread is not null && IsCompliant(reread, configuration))
                return true;
            if (attempt >= attempts)
                return false;
            _sleep(_readbackSettleInterval);
        }
    }

    /// <summary>review [P1]: absence is only proven by a SUCCESSFUL read that returns no task. A
    /// transient read failure keeps settling and, if it never clears, reports NOT absent.</summary>
    private bool ReadBackVerifyAbsentWithBoundedSettle()
    {
        var attempts = Math.Max(1, (int)Math.Ceiling(_readbackSettleWindow / _readbackSettleInterval));
        for (var attempt = 1; ; attempt++)
        {
            if (TryRead(out var reread) && reread is null)
                return true;
            if (attempt >= attempts)
                return false;
            _sleep(_readbackSettleInterval);
        }
    }

    private OwnedStartupTaskState? SafeRead()
    {
        try { return _taskStore.Read(); }
        catch (Exception exception) { AppLog.Warn("TaskScheduler", "Startup task read failed.", exception); return null; }
    }

    /// <summary>Preserves the distinction between "read succeeded + task absent" (returns
    /// <see langword="true"/> with <paramref name="state"/> <see langword="null"/>) and "read failed"
    /// (returns <see langword="false"/>). A read exception must never be read as verified absence.</summary>
    private bool TryRead(out OwnedStartupTaskState? state)
    {
        try { state = _taskStore.Read(); return true; }
        catch (Exception exception)
        {
            AppLog.Warn("TaskScheduler", "Owned startup task read failed.", exception);
            state = null;
            return false;
        }
    }

    internal const string NoExecutionTimeLimit = "PT0S";

    internal static bool IsCompliant(OwnedStartupTaskState state, ScheduledTaskConfiguration configuration) =>
        state.Enabled
        && PathEquals(state.ActionPath, configuration.ExecutablePath)
        && string.Equals(state.ActionArguments.Trim(), "--background", StringComparison.Ordinal)
        && state.LogonType == TaskLogonInteractiveToken
        && state.RunLevel == 0
        && string.Equals(state.LogonTriggerUserId, configuration.UserId, StringComparison.OrdinalIgnoreCase)
        // A battery-restricted or execution-time-limited task is NOT a valid persistent handheld
        // Runtime guarantee -- treat it as drift and repair it (review [P1]).
        && !state.DisallowStartIfOnBatteries
        && !state.StopIfGoingOnBatteries
        && string.Equals(state.ExecutionTimeLimit, NoExecutionTimeLimit, StringComparison.OrdinalIgnoreCase);

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

            dynamic settings = definition.Settings;
            bool disallowOnBatteries = (bool)settings.DisallowStartIfOnBatteries;
            bool stopGoingOnBatteries = (bool)settings.StopIfGoingOnBatteries;
            string executionTimeLimit = (string)(settings.ExecutionTimeLimit ?? string.Empty);

            return new OwnedStartupTaskState(enabled, actionPath, actionArguments, logonTriggerUserId, logonType, runLevel,
                disallowOnBatteries, stopGoingOnBatteries, executionTimeLimit);
        }
        catch (COMException exception) when (exception.HResult == FileNotFoundHResult)
        {
            return null;
        }
        catch (Exception exception)
        {
            // review [P1]: a genuine read failure must surface as a failure, not as "task absent".
            // Only FileNotFound above is real absence.
            AppLog.Warn("TaskScheduler", "Owned startup task could not be read.", exception);
            throw;
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

            // A persistent handheld Runtime must start and keep running on battery, with no scheduler
            // execution-time limit (review [P1]).
            dynamic settings = taskDefinition.Settings;
            settings.DisallowStartIfOnBatteries = false;
            settings.StopIfGoingOnBatteries = false;
            settings.ExecutionTimeLimit = WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit;

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
