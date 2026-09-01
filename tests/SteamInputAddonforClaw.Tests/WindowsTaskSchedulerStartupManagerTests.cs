using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR10 addendum sections 15-19 / 21: the Addon-owned Task Scheduler startup task is
/// verified read-only first (no rewrite / no UAC when compliant), and a first, access-denied
/// registration falls back to one bounded elevated child that is trusted only after a non-elevated
/// readback proves the exact task.</summary>
[Collection("AppLog")]
public sealed class WindowsTaskSchedulerStartupManagerTests : IDisposable
{
    private const string User = @"MACHINE\claw";
    private readonly string _exe = Path.Combine(Path.GetTempPath(), $"siafc-startup-{Guid.NewGuid():N}.exe");

    public WindowsTaskSchedulerStartupManagerTests() => File.WriteAllText(_exe, "stub");

    public void Dispose() { try { File.Delete(_exe); } catch { } }

    private OwnedStartupTaskState Compliant() =>
        new(Enabled: true, ActionPath: _exe, ActionArguments: "--background",
            LogonTriggerUserId: User, LogonType: 3, RunLevel: 0,
            DisallowStartIfOnBatteries: false, StopIfGoingOnBatteries: false, ExecutionTimeLimit: "PT0S");

    private WindowsTaskSchedulerStartupManager Manager(FakeTaskStore store, FakeElevated? elevated = null) =>
        new(() => _exe, () => User, store, elevated);

    [Fact] // 21: existing compliant task -> readback only, no RegisterTaskDefinition, no UAC
    public void Existing_compliant_task_is_verified_without_a_rewrite()
    {
        var store = new FakeTaskStore { Current = Compliant() };
        var elevated = new FakeElevated();

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(0, store.RegisterCalls);
        Assert.Equal(0, elevated.Calls);
    }

    [Fact]
    public void Repeated_synchronize_after_a_compliant_task_never_registers_or_elevates()
    {
        var store = new FakeTaskStore { Current = Compliant() };
        var elevated = new FakeElevated();
        var manager = Manager(store, elevated);

        Assert.True(manager.Synchronize(true).Success);
        Assert.True(manager.Synchronize(true).Success);

        Assert.Equal(0, store.RegisterCalls);
        Assert.Equal(0, elevated.Calls);
    }

    [Fact]
    public void Missing_task_registers_without_elevation_when_the_write_is_allowed()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.Registered };
        var elevated = new FakeElevated();

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, store.RegisterCalls);
        Assert.Equal(0, elevated.Calls);
    }

    [Fact] // review [P1]: a non-elevated write that reports Registered is still proven by readback
    public void A_non_elevated_registration_that_reads_back_drifted_fails()
    {
        var store = new FakeTaskStore
        {
            Current = null,
            NextRegister = StartupTaskWriteOutcome.Registered,
            RegisteredReadback = new OwnedStartupTaskState(true, @"C:\wrong.exe", "--background", User, 3, 0, false, false, "PT0S"),
        };

        Assert.False(Manager(store, new FakeElevated()).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Theory] // drift: any of these must not read as compliant
    [InlineData("args")]
    [InlineData("disabled")]
    [InlineData("path")]
    [InlineData("runlevel")]
    [InlineData("logontype")]
    [InlineData("trigger-user")]
    [InlineData("battery-disallow")]  // review [P1]: default battery policy blocks a battery boot
    [InlineData("battery-stop")]
    [InlineData("execution-limit")]   // review [P1]: default finite (72h) limit terminates the Runtime
    public void A_drifted_task_is_repaired(string drift)
    {
        var c = Compliant();
        var drifted = drift switch
        {
            "args" => c with { ActionArguments = "--foreground" },
            "disabled" => c with { Enabled = false },
            "path" => c with { ActionPath = @"C:\Windows\other.exe" },
            "runlevel" => c with { RunLevel = 1 },
            "logontype" => c with { LogonType = 2 },
            "battery-disallow" => c with { DisallowStartIfOnBatteries = true },
            "battery-stop" => c with { StopIfGoingOnBatteries = true },
            "execution-limit" => c with { ExecutionTimeLimit = "PT72H" },
            _ => c with { LogonTriggerUserId = @"OTHER\user" },
        };
        var store = new FakeTaskStore { Current = drifted, NextRegister = StartupTaskWriteOutcome.Registered };

        var result = Manager(store, new FakeElevated()).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact] // 21: first installation -> access denied -> elevated child -> non-elevated readback proves it
    public void First_installation_uses_the_elevated_child_then_verifies_by_readback()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.AccessDenied };
        var elevated = new FakeElevated
        {
            Outcome = ElevatedStartupTaskOutcome.Created,
            OnInvoke = s => s.Current = Compliant(), // the elevated child created the task
        };
        elevated.Store = store;

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, elevated.Calls);
        Assert.True(store.ReadCalls >= 2); // verify before + readback after
    }

    [Fact]
    public void Elevated_child_reporting_success_but_leaving_no_compliant_task_fails()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.AccessDenied };
        var elevated = new FakeElevated { Outcome = ElevatedStartupTaskOutcome.Created }; // but never creates the task

        var result = Manager(store, elevated).Synchronize(true);

        Assert.False(result.Success);
    }

    [Fact]
    public void A_cancelled_uac_prompt_is_a_registration_failure()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.AccessDenied };
        var elevated = new FakeElevated { Outcome = ElevatedStartupTaskOutcome.Cancelled };

        Assert.False(Manager(store, elevated).Synchronize(true).Success);
    }

    [Fact]
    public void Access_denied_without_an_elevated_repair_path_fails()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.AccessDenied };

        Assert.False(Manager(store, elevated: null).Synchronize(true).Success);
    }

    [Fact]
    public void A_non_denied_registration_failure_does_not_invoke_the_elevated_child()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.Failed };
        var elevated = new FakeElevated();

        Assert.False(Manager(store, elevated).Synchronize(true).Success);
        Assert.Equal(0, elevated.Calls);
    }

    [Fact] // review [P1]: the newly registered desired task is battery-safe and has no execution limit
    public void A_newly_registered_task_records_battery_safe_settings_and_no_execution_limit()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.Registered };

        Assert.True(Manager(store, new FakeElevated()).Synchronize(true).Success);

        Assert.NotNull(store.LastRegistered);
        Assert.False(store.LastRegistered!.DisallowStartIfOnBatteries);
        Assert.False(store.LastRegistered.StopIfGoingOnBatteries);
        Assert.Equal("PT0S", store.LastRegistered.ExecutionTimeLimit);
    }

    [Fact]
    public void Disable_deletes_the_owned_task()
    {
        var store = new FakeTaskStore { Current = Compliant() };

        var result = Manager(store, new FakeElevated()).Synchronize(false);

        Assert.True(result.Success);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Missing_stable_executable_is_not_installed()
    {
        File.Delete(_exe);
        var store = new FakeTaskStore { Current = null };

        var result = Manager(store, new FakeElevated()).Synchronize(true);

        Assert.False(result.Success);
        Assert.Equal(0, store.RegisterCalls);
    }

    private sealed class FakeTaskStore : IOwnedStartupTaskStore
    {
        public OwnedStartupTaskState? Current;
        public StartupTaskWriteOutcome NextRegister = StartupTaskWriteOutcome.Registered;
        public OwnedStartupTaskState? RegisteredReadback; // what a "Registered" write actually leaves behind
        public int RegisterCalls;
        public int DeleteCalls;
        public int ReadCalls;

        public OwnedStartupTaskState? Read() { ReadCalls++; return Current; }

        public StartupTaskWriteOutcome Register(ScheduledTaskConfiguration configuration)
        {
            RegisterCalls++;
            LastRegistered = new OwnedStartupTaskState(true, configuration.ExecutablePath, "--background", configuration.UserId, 3, 0,
                DisallowStartIfOnBatteries: false, StopIfGoingOnBatteries: false, ExecutionTimeLimit: "PT0S");
            if (NextRegister == StartupTaskWriteOutcome.Registered)
                Current = RegisteredReadback ?? LastRegistered;
            return NextRegister;
        }

        public OwnedStartupTaskState? LastRegistered;

        public void Delete() { DeleteCalls++; Current = null; }
    }

    private sealed class FakeElevated : IElevatedStartupTaskInvoker
    {
        public ElevatedStartupTaskOutcome Outcome = ElevatedStartupTaskOutcome.Created;
        public int Calls;
        public Action<FakeTaskStore>? OnInvoke;
        public FakeTaskStore? Store;

        public ElevatedStartupTaskOutcome EnsureOwnedTask()
        {
            Calls++;
            if (Store is not null) OnInvoke?.Invoke(Store);
            return Outcome;
        }
    }
}
