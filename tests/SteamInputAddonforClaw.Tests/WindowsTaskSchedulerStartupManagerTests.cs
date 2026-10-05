using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class WindowsTaskSchedulerStartupManagerTests : IDisposable
{
    private const string User = @"MACHINE\claw";
    private readonly string _exe = Path.Combine(Path.GetTempPath(), $"siafc-startup-{Guid.NewGuid():N}.exe");

    public WindowsTaskSchedulerStartupManagerTests() => File.WriteAllText(_exe, "stub");

    public void Dispose() { try { File.Delete(_exe); } catch { } }

    private OwnedStartupTaskState Compliant() =>
        new(true, _exe, "--background", User, 3, WindowsTaskSchedulerStartupManager.TaskRunLevelHighest,
            DisallowStartIfOnBatteries: false, StopIfGoingOnBatteries: false, ExecutionTimeLimit: "PT0S");

    private WindowsTaskSchedulerStartupManager Manager(FakeTaskStore store) =>
        new(() => _exe, () => User, store,
            sleep: _ => { }, readbackSettleWindow: TimeSpan.FromMilliseconds(30), readbackSettleInterval: TimeSpan.FromMilliseconds(10));

    [Fact]
    public void Existing_compliant_task_is_read_only()
    {
        var store = new FakeTaskStore { Current = Compliant() };

        Assert.True(Manager(store).Synchronize(true).Success);
        Assert.Equal(0, store.RegisterCalls);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public void Missing_task_is_registered_directly_once_and_exact_contract_is_verified()
    {
        var store = new FakeTaskStore();

        Assert.True(Manager(store).Synchronize(true).Success);

        Assert.Equal(1, store.RegisterCalls);
        Assert.NotNull(store.LastConfiguration);
        Assert.Equal(_exe, store.LastConfiguration!.ExecutablePath);
        Assert.Equal(User, store.LastConfiguration.UserId);
        Assert.Equal("--background", store.Current!.ActionArguments);
        Assert.Equal(WindowsTaskSchedulerStartupManager.TaskRunLevelHighest, store.Current.RunLevel);
        Assert.False(store.Current.DisallowStartIfOnBatteries);
        Assert.False(store.Current.StopIfGoingOnBatteries);
        Assert.Equal("PT0S", store.Current.ExecutionTimeLimit);
    }

    [Theory]
    [InlineData("args")]
    [InlineData("disabled")]
    [InlineData("path")]
    [InlineData("runlevel")]
    [InlineData("logontype")]
    [InlineData("trigger-user")]
    [InlineData("battery-disallow")]
    [InlineData("battery-stop")]
    [InlineData("execution-limit")]
    public void Drifted_task_is_repaired_with_one_direct_write(string drift)
    {
        var compliant = Compliant();
        var changed = drift switch
        {
            "args" => compliant with { ActionArguments = "--foreground" },
            "disabled" => compliant with { Enabled = false },
            "path" => compliant with { ActionPath = @"C:\Windows\other.exe" },
            "runlevel" => compliant with { RunLevel = 0 },
            "logontype" => compliant with { LogonType = 2 },
            "trigger-user" => compliant with { LogonTriggerUserId = @"OTHER\user" },
            "battery-disallow" => compliant with { DisallowStartIfOnBatteries = true },
            "battery-stop" => compliant with { StopIfGoingOnBatteries = true },
            _ => compliant with { ExecutionTimeLimit = "PT72H" },
        };
        var store = new FakeTaskStore { Current = changed };

        Assert.True(Manager(store).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
        Assert.True(WindowsTaskSchedulerStartupManager.IsCompliant(store.Current!,
            WindowsTaskSchedulerStartupManager.CreateTaskConfiguration(_exe, User)));
    }

    [Theory]
    [InlineData((int)StartupTaskWriteOutcome.AccessDenied)]
    [InlineData((int)StartupTaskWriteOutcome.Failed)]
    public void Direct_registration_failure_fails_without_any_secondary_elevation_attempt(int outcome)
    {
        var store = new FakeTaskStore { NextRegister = (StartupTaskWriteOutcome)outcome };

        Assert.False(Manager(store).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void Direct_registration_that_does_not_read_back_as_compliant_fails()
    {
        var store = new FakeTaskStore
        {
            RegisteredReadback = new(true, @"C:\wrong.exe", "--background", User, 3, 0, false, false, "PT0S")
        };

        Assert.False(Manager(store).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void A_lagging_readback_can_settle_without_repeating_the_write()
    {
        var store = new FakeTaskStore { CompliantOnReadNumber = 3, CompliantValue = Compliant() };

        Assert.True(Manager(store).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void Readback_that_never_becomes_compliant_fails_after_one_write()
    {
        var store = new FakeTaskStore
        {
            RegisteredReadback = new(false, _exe, "--background", User, 3, 1, false, false, "PT0S")
        };

        Assert.False(Manager(store).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void Missing_stable_executable_does_not_write_a_task()
    {
        File.Delete(_exe);
        var store = new FakeTaskStore();

        Assert.False(Manager(store).Synchronize(true).Success);
        Assert.Equal(0, store.RegisterCalls);
    }

    [Fact]
    public void Disable_when_task_is_absent_is_read_only_success()
    {
        var store = new FakeTaskStore();

        Assert.True(Manager(store).Synchronize(false).Success);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public void Disable_deletes_directly_once_and_verifies_absence()
    {
        var store = new FakeTaskStore { Current = Compliant() };

        Assert.True(Manager(store).Synchronize(false).Success);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Disable_aborts_delete_when_current_task_cannot_be_read()
    {
        var store = new FakeTaskStore { Current = Compliant(), FailReadsFrom = 1 };

        Assert.False(Manager(store).Synchronize(false).Success);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public void Disable_fails_when_absence_cannot_be_proven_after_delete()
    {
        var store = new FakeTaskStore { Current = Compliant(), FailReadsFrom = 2 };

        Assert.False(Manager(store).Synchronize(false).Success);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public void Disable_fails_when_direct_delete_throws()
    {
        var store = new FakeTaskStore { Current = Compliant(), DeleteException = new UnauthorizedAccessException() };

        Assert.False(Manager(store).Synchronize(false).Success);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public void Disable_fails_when_readback_still_finds_the_task()
    {
        var store = new FakeTaskStore { Current = Compliant(), KeepTaskOnDelete = true };

        Assert.False(Manager(store).Synchronize(false).Success);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public void Exact_com_missing_task_hresult_is_classified_as_absent()
    {
        var exception = new COMException("Task not found.", unchecked((int)0x80070002));
        Assert.True(WindowsOwnedStartupTaskStore.IsExactMissingTaskException(exception));
    }

    [Fact]
    public void Exact_file_not_found_hresult_is_classified_as_absent()
    {
        var exception = new FileNotFoundException("Task not found.");
        Assert.Equal(unchecked((int)0x80070002), exception.HResult);
        Assert.True(WindowsOwnedStartupTaskStore.IsExactMissingTaskException(exception));
    }

    [Fact]
    public void File_not_found_with_a_different_hresult_remains_a_read_failure()
    {
        var exception = new FileNotFoundExceptionWithHResult(unchecked((int)0x80070005));
        Assert.False(WindowsOwnedStartupTaskStore.IsExactMissingTaskException(exception));
    }

    private sealed class FakeTaskStore : IOwnedStartupTaskStore
    {
        public OwnedStartupTaskState? Current;
        public StartupTaskWriteOutcome NextRegister = StartupTaskWriteOutcome.Registered;
        public OwnedStartupTaskState? RegisteredReadback;
        public int CompliantOnReadNumber;
        public OwnedStartupTaskState? CompliantValue;
        public int FailReadsFrom;
        public int RegisterCalls;
        public int DeleteCalls;
        public int ReadCalls;
        public ScheduledTaskConfiguration? LastConfiguration;
        public Exception? DeleteException;
        public bool KeepTaskOnDelete;

        public OwnedStartupTaskState? Read()
        {
            ReadCalls++;
            if (FailReadsFrom > 0 && ReadCalls >= FailReadsFrom)
                throw new InvalidOperationException("Simulated Task Scheduler read failure.");
            if (CompliantOnReadNumber > 0 && ReadCalls >= CompliantOnReadNumber && CompliantValue is not null)
                Current = CompliantValue;
            return Current;
        }

        public StartupTaskWriteOutcome Register(ScheduledTaskConfiguration configuration)
        {
            RegisterCalls++;
            LastConfiguration = configuration;
            var intended = new OwnedStartupTaskState(true, configuration.ExecutablePath, "--background", configuration.UserId,
                WindowsTaskSchedulerStartupManager.TaskLogonInteractiveToken, WindowsTaskSchedulerStartupManager.TaskRunLevelHighest,
                false, false, WindowsTaskSchedulerStartupManager.NoExecutionTimeLimit);
            if (NextRegister == StartupTaskWriteOutcome.Registered)
                Current = RegisteredReadback ?? intended;
            return NextRegister;
        }

        public void Delete()
        {
            DeleteCalls++;
            if (DeleteException is not null) throw DeleteException;
            if (!KeepTaskOnDelete) Current = null;
        }
    }

    private sealed class FileNotFoundExceptionWithHResult : FileNotFoundException
    {
        public FileNotFoundExceptionWithHResult(int hresult) => HResult = hresult;
    }
}
