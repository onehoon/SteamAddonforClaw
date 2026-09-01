using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR11 sections 11-13 / 18: the Addon-owned Task Scheduler startup task is verified
/// read-only first (no rewrite / no UAC when compliant). A missing/materially-drifted task in the
/// production Runtime goes DIRECTLY to one bounded elevated child -- no known-denied parent
/// RegisterTaskDefinition first -- then an independent, bounded-settle normal-process read-back must
/// prove the exact task. The elevated `--ensure-startup-task` child (a manager with no elevated
/// invoker) writes directly and read-back verifies.</summary>
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

    // Deterministic bounded settle: no-op sleep, 4 read attempts (30ms / 10ms).
    private WindowsTaskSchedulerStartupManager Manager(FakeTaskStore store, FakeElevated? elevated) =>
        new(() => _exe, () => User, store, elevated,
            sleep: _ => { }, readbackSettleWindow: TimeSpan.FromMilliseconds(30), readbackSettleInterval: TimeSpan.FromMilliseconds(10));

    // ---- steady state ----

    [Fact]
    public void Existing_compliant_task_is_verified_without_a_rewrite_or_elevation()
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

    // ---- production manager: missing / drifted go straight to elevated ----

    [Fact] // PR11 section 11: NO known-denied parent RegisterTaskDefinition first
    public void Missing_task_in_the_production_manager_goes_straight_to_the_elevated_child()
    {
        var store = new FakeTaskStore { Current = null };
        var elevated = new FakeElevated { Store = store, OnInvoke = s => s.Current = Compliant() };

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, elevated.Calls);
        Assert.Equal(0, store.RegisterCalls);
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
    public void A_drifted_task_in_the_production_manager_is_repaired_via_the_elevated_child(string drift)
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
        var store = new FakeTaskStore { Current = drifted };
        var elevated = new FakeElevated { Store = store, OnInvoke = s => s.Current = Compliant() };

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, elevated.Calls);
        Assert.Equal(0, store.RegisterCalls);
    }

    // ---- elevated child / direct-write manager (no elevated invoker) ----

    [Fact]
    public void The_elevated_child_writes_directly_and_readback_verifies()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.Registered };

        var result = Manager(store, elevated: null).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void The_elevated_child_direct_write_that_reads_back_drifted_fails()
    {
        var store = new FakeTaskStore
        {
            Current = null,
            NextRegister = StartupTaskWriteOutcome.Registered,
            RegisteredReadback = new OwnedStartupTaskState(true, @"C:\wrong.exe", "--background", User, 3, 0, false, false, "PT0S"),
        };

        Assert.False(Manager(store, elevated: null).Synchronize(true).Success);
        Assert.Equal(1, store.RegisterCalls);
    }

    [Fact]
    public void A_direct_write_access_denied_with_no_elevated_path_fails()
        => Assert.False(Manager(new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.AccessDenied }, elevated: null).Synchronize(true).Success);

    // ---- bounded post-elevation readback settle (PR11 section 12) ----

    [Fact] // read #1 lags, a later read within the bounded window verifies
    public void A_lagging_readback_within_the_bounded_window_still_succeeds()
    {
        var store = new FakeTaskStore { Current = null, CompliantOnReadNumber = 3 }; // 1st verify-read + 2 settle reads
        store.CompliantValue = Compliant();
        var elevated = new FakeElevated { Store = store, Outcome = ElevatedStartupTaskOutcome.Created };

        var result = Manager(store, elevated).Synchronize(true);

        Assert.True(result.Success);
        Assert.Equal(1, elevated.Calls); // no repeated elevation
    }

    [Fact] // all reads stay non-compliant until the window expires
    public void A_readback_that_never_verifies_within_the_bounded_window_fails()
    {
        var store = new FakeTaskStore { Current = null }; // never becomes compliant
        var elevated = new FakeElevated { Outcome = ElevatedStartupTaskOutcome.Created };

        Assert.False(Manager(store, elevated).Synchronize(true).Success);
        Assert.Equal(1, elevated.Calls);
    }

    // ---- failure cases ----

    [Fact]
    public void A_cancelled_uac_prompt_is_a_registration_failure()
        => Assert.False(Manager(new FakeTaskStore { Current = null }, new FakeElevated { Outcome = ElevatedStartupTaskOutcome.Cancelled }).Synchronize(true).Success);

    [Fact]
    public void An_elevated_child_that_failed_is_a_registration_failure()
        => Assert.False(Manager(new FakeTaskStore { Current = null }, new FakeElevated { Outcome = ElevatedStartupTaskOutcome.Failed }).Synchronize(true).Success);

    [Fact]
    public void Missing_stable_executable_is_not_installed()
    {
        File.Delete(_exe);
        var store = new FakeTaskStore { Current = null };
        var elevated = new FakeElevated();

        Assert.False(Manager(store, elevated).Synchronize(true).Success);
        Assert.Equal(0, elevated.Calls);
        Assert.Equal(0, store.RegisterCalls);
    }

    [Fact] // PR11 section 13: the newly registered desired task is battery-safe with no execution limit
    public void A_newly_registered_task_records_battery_safe_settings_and_no_execution_limit()
    {
        var store = new FakeTaskStore { Current = null, NextRegister = StartupTaskWriteOutcome.Registered };

        Assert.True(Manager(store, elevated: null).Synchronize(true).Success);

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

    private sealed class FakeTaskStore : IOwnedStartupTaskStore
    {
        public OwnedStartupTaskState? Current;
        public StartupTaskWriteOutcome NextRegister = StartupTaskWriteOutcome.Registered;
        public OwnedStartupTaskState? RegisteredReadback;
        // Read lag: from the Nth Read() onward, Current becomes CompliantValue.
        public int CompliantOnReadNumber;
        public OwnedStartupTaskState? CompliantValue;
        public int RegisterCalls;
        public int DeleteCalls;
        public int ReadCalls;
        public OwnedStartupTaskState? LastRegistered;

        public OwnedStartupTaskState? Read()
        {
            ReadCalls++;
            if (CompliantOnReadNumber > 0 && ReadCalls >= CompliantOnReadNumber && CompliantValue is not null)
                Current = CompliantValue;
            return Current;
        }

        public StartupTaskWriteOutcome Register(ScheduledTaskConfiguration configuration)
        {
            RegisterCalls++;
            LastRegistered = new OwnedStartupTaskState(true, configuration.ExecutablePath, "--background", configuration.UserId, 3, 0,
                DisallowStartIfOnBatteries: false, StopIfGoingOnBatteries: false, ExecutionTimeLimit: "PT0S");
            if (NextRegister == StartupTaskWriteOutcome.Registered)
                Current = RegisteredReadback ?? LastRegistered;
            return NextRegister;
        }

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
