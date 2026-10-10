using System.Diagnostics;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawInputSourceTests
{
    // These tests bound async waits for the background read loop's summary/state signals.
    // 10 seconds was tight enough that a contended CI thread pool could burn through it
    // before the background loop even finished its bounded read/retry allowance. Widen for
    // CI headroom; it's an upper bound, not an expected duration, so passing runs are
    // unaffected.
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StartPrepared_UsesExactDescriptorWithoutEnumeration()
    {
        var descriptor = Device(0x0DB0, 0x1902);
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([], device);
        var source = new MsiClawInputSource(enumerator);

        var result = source.StartPrepared(descriptor);

        Assert.True(result.Started);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(descriptor, enumerator.CreatedDescriptor);
        await source.StopAsync();
    }

    [Fact]
    public void Start_WhenDirectInputInitializationFails_ReturnsInitializationFailed()
    {
        var source = new MsiClawInputSource(() => throw new InvalidOperationException("DirectInput unavailable"));

        Assert.Equal(MsiClawInputStartStatus.InitializationFailed, source.StartPrepared(Device(0x0DB0, 0x1902)).Status);
    }

    [Fact]
    public void Start_WhenDeviceCreationFails_ReturnsCreateDeviceFailed()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)]) { CreateException = new InvalidOperationException("Create failed") };
        var source = new MsiClawInputSource(enumerator);

        Assert.Equal(MsiClawInputStartStatus.CreateDeviceFailed, source.StartPrepared(Device(0x0DB0, 0x1902)).Status);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public void Start_WhenDeviceCreationAndEnumeratorCleanupFail_PreservesTheCreationFailureResult()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)])
        {
            CreateException = new InvalidOperationException("Create failed"),
            DisposeException = new InvalidOperationException("Dispose failed")
        };
        var source = new MsiClawInputSource(enumerator);

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));

        Assert.Equal(MsiClawInputStartStatus.CreateDeviceFailed, result.Status);
        Assert.Equal(1, enumerator.DisposeCount);
    }

    [Fact]
    public void Start_WhenAcquireFails_CleansUpWithoutStartingPolling()
    {
        var device = new FakeDevice(State()) { AcquireException = new InvalidOperationException("Acquire failed") };
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));

        Assert.Equal(MsiClawInputStartStatus.AcquireFailed, result.Status);
        Assert.False(source.IsRunning);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public async Task Production_worker_uses_one_high_resolution_4ms_deadline_and_stops_before_returning()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi();
        var origin = 123_456L;
        Thread? observedWorker = null;
        device.ReadAttempted += _ => observedWorker ??= Thread.CurrentThread;
        await using var source = CreateSource(enumerator, timerApi, () => origin);

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));
        Assert.True(result.Started);
        await device.FirstReadAttempted.Task.WaitAsync(AwaitTimeout);

        var expectedPeriodTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(
            TimeSpan.FromMilliseconds(4), Stopwatch.Frequency);
        var expectedDueTime100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(
            expectedPeriodTicks, Stopwatch.Frequency);
        Assert.Equal(-expectedDueTime100ns, timerApi.LastDueTime100ns);
        Assert.NotNull(observedWorker);
        Assert.NotEqual(Environment.CurrentManagedThreadId, observedWorker!.ManagedThreadId);
        Assert.True(observedWorker.IsBackground);
        Assert.Equal(ThreadPriority.AboveNormal, observedWorker.Priority);
        Assert.Equal("SteamInputAddon.PID1902PhysicalInput", observedWorker.Name);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, device.AcquireCount);
        Assert.True(await source.WaitForFirstValidStateAsync(CancellationToken.None));

        await source.StopAsync();
        var readsAfterStop = device.ReadCount;
        timerApi.Pulse();

        Assert.Equal(readsAfterStop, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(1, timerApi.CancelCount);
    }

    [Fact]
    public async Task Production_worker_changes_8ms_and_4ms_periods_in_place_without_reacquiring_DirectInput()
    {
        var device = new FakeDevice(State(), State(), State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi();
        var origin = 123_456L;
        var now = origin;
        var requires250Hz = 0;
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.ReadAttempted += count =>
        {
            if (count == 2) secondRead.TrySetResult();
            if (count == 3) thirdRead.TrySetResult();
        };
        await using var source = CreateSource(enumerator, timerApi, () => Volatile.Read(ref now), () => Volatile.Read(ref requires250Hz) != 0);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await device.FirstReadAttempted.Task.WaitAsync(AwaitTimeout);

        var eightMsTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(8), Stopwatch.Frequency);
        Assert.Equal(-CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(eightMsTicks, Stopwatch.Frequency), timerApi.LastDueTime100ns);

        Volatile.Write(ref requires250Hz, 1);
        now = checked(origin + eightMsTicks);
        timerApi.Pulse();
        await secondRead.Task.WaitAsync(AwaitTimeout);
        Assert.True(SpinWait.SpinUntil(() => timerApi.ArmCount >= 2, AwaitTimeout));

        var fourMsTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), Stopwatch.Frequency);
        Assert.Equal(-CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(fourMsTicks, Stopwatch.Frequency), timerApi.LastDueTime100ns);

        Volatile.Write(ref requires250Hz, 0);
        now = checked(origin + eightMsTicks + fourMsTicks);
        timerApi.Pulse();
        await thirdRead.Task.WaitAsync(AwaitTimeout);
        Assert.True(SpinWait.SpinUntil(() => timerApi.ArmCount >= 3, AwaitTimeout));
        Assert.Equal(-CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(eightMsTicks, Stopwatch.Frequency), timerApi.LastDueTime100ns);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, device.AcquireCount);

        await source.StopAsync();
    }

    [Fact]
    public async Task Production_cadence_change_rearm_failure_uses_existing_poll_scheduler_fail_close()
    {
        var device = new FakeDevice(State(), State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi { FailOnArmNumber = 2 };
        var origin = 123_456L;
        var now = origin;
        var requires250Hz = 0;
        await using var source = CreateSource(enumerator, timerApi, () => Volatile.Read(ref now), () => Volatile.Read(ref requires250Hz) != 0);
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await device.FirstReadAttempted.Task.WaitAsync(AwaitTimeout);
        Volatile.Write(ref requires250Hz, 1);
        now = checked(origin + CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(8), Stopwatch.Frequency));
        timerApi.Pulse();

        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.PollSchedulerFailed, summary.StopReason);
        Assert.True(MsiClawPhysicalInputFaultPolicy.IsFatal(summary.StopReason, ownedSessionIdentityPresent: true));
        Assert.False(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public void Production_timer_creation_failure_cleans_acquired_input_and_fails_start()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi { FailCreate = true };
        var source = CreateSource(enumerator, timerApi);

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));

        Assert.Equal(MsiClawInputStartStatus.InitializationFailed, result.Status);
        Assert.False(source.IsRunning);
        Assert.Equal(0, device.ReadCount);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public void Production_timer_initial_arm_failure_cleans_acquired_input_and_fails_start()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi { FailOnArmNumber = 1 };
        var source = CreateSource(enumerator, timerApi);

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));

        Assert.Equal(MsiClawInputStartStatus.InitializationFailed, result.Status);
        Assert.False(source.IsRunning);
        Assert.Equal(0, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
        Assert.Equal(1, timerApi.CancelCount);
    }

    [Fact]
    public void Production_worker_start_failure_cleans_acquired_input_and_fails_start()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi();
        var source = CreateSource(enumerator, timerApi);
        source.WorkerThreadStartOverrideForTests = _ => throw new InvalidOperationException("simulated thread start failure");

        var result = source.StartPrepared(Device(0x0DB0, 0x1902));

        Assert.Equal(MsiClawInputStartStatus.InitializationFailed, result.Status);
        Assert.False(source.IsRunning);
        Assert.Equal(0, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
        Assert.Equal(1, timerApi.CancelCount);
    }

    [Fact]
    public async Task Production_timer_rearm_failure_stops_owned_session_through_existing_fault_policy()
    {
        var device = new FakeDevice(State(), State());
        var enumerator = new FakeEnumerator([], device);
        using var timerApi = new FakeWaitableTimerNativeApi { FailOnArmNumber = 2 };
        var origin = 123_456L;
        var now = origin;
        await using var source = CreateSource(enumerator, timerApi, () => Volatile.Read(ref now));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await device.FirstReadAttempted.Task.WaitAsync(AwaitTimeout);
        now = checked(origin + CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), Stopwatch.Frequency));
        timerApi.Pulse();
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.PollSchedulerFailed, summary.StopReason);
        Assert.True(MsiClawPhysicalInputFaultPolicy.IsFatal(summary.StopReason, ownedSessionIdentityPresent: true));
        Assert.False(source.IsRunning);
        Assert.Equal(2, device.ReadCount);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(1, timerApi.CancelCount);
    }

    [Fact]
    public async Task FirstReadFailure_StopsAndCleansUpWithoutLeavingAnOrphanedSession()
    {
        var device = new FakeDevice(new InvalidOperationException("Read failed"));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);
        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);
        await source.StopAsync();

        Assert.False(source.IsRunning);
        Assert.Equal(MsiClawInputStopReason.ReadStateFailed, summary.StopReason);
        Assert.Equal(1, summary.ReadFailures);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);
    }

    [Fact]
    public async Task PollingReadFailure_StopsAndCleansUp()
    {
        var device = new FakeDevice(State(), State(), new InvalidOperationException("Read failed"));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);
        var thirdReadAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.ReadAttempted += count =>
        {
            if (count >= 3) thirdReadAttempt.TrySetResult();
        };

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await thirdReadAttempt.Task.WaitAsync(AwaitTimeout);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.ReadStateFailed, summary.StopReason);
        Assert.Equal(1, summary.ReadFailures);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);
    }

    [Fact] // PR8 work order section 22.1: the completion callback the Full-1902 owner subscribes to
    public async Task UnexpectedCompletion_ObservesNeutralStateAndAStoppedSource()
    {
        var device = new FakeDevice(new InvalidOperationException("Read failed"));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var observed = new TaskCompletionSource<(ControllerState State, bool IsRunning, MsiClawInputStopReason StopReason)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.TestCompleted += (_, summary) => observed.TrySetResult((source.LatestState, source.IsRunning, summary.StopReason));

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        var snapshot = await observed.Task.WaitAsync(AwaitTimeout);

        Assert.NotEqual(MsiClawInputStopReason.Stopped, snapshot.StopReason);
        Assert.False(snapshot.IsRunning);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), snapshot.State);
    }

    [Fact]
    public async Task ShortButtonArray_StopsDiagnosticInsteadOfPollingNeutralState()
    {
        var device = new FakeDevice(new DirectInputState(new bool[16]));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.InvalidButtonLayout, summary.StopReason);
        Assert.Equal(1, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task ShortButtonArray_WithKnownInvalidRotations_StopsImmediatelyAsInsufficientButtonCount()
    {
        var device = new FakeDevice(InvalidInitialState(buttonCount: 16));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.InvalidButtonLayout, summary.StopReason);
        Assert.Equal(1, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public void EmptyPointOfViewCollectionDoesNotThrowAndIsTreatedAsNeutral()
    {
        // Unit-level rather than a full background-loop integration test: exercising the real
        // async poll loop end-to-end for this pulled in a race against
        // ControllerStateDiagnostics' shared, process-wide static throttle/tracking state from
        // other, non-serialized test collections, which was flaky (and once hung) in CI.
        var emptyPov = new DirectInputState(new bool[17], pointOfViewControllers: []);

        var exception = Record.Exception(() => MsiClawInputSource.ResolvePov(emptyPov));

        Assert.Null(exception);
        Assert.Equal(-1, MsiClawInputSource.ResolvePov(emptyPov));
    }

    [Fact]
    public async Task KnownInvalidInitialState_IsSkippedUntilTheFirstValidState()
    {
        var device = new FakeDevice(InvalidInitialState(), State(15));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);
        var validStateObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += (_, state) =>
        {
            if (state == new ControllerState(new AuxiliaryButtonState([false, true]))) validStateObserved.TrySetResult();
        };

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await validStateObserved.Task.WaitAsync(AwaitTimeout);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, true])), source.LatestState);
        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.Stopped, summary.StopReason);
        Assert.True(device.ReadCount >= 2);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);
    }

    [Fact] // Full1902 Suspend/Resume section 9 / 16.7
    public async Task ResetLatestStateToNeutral_neutralizes_the_snapshot_without_stopping_the_session_and_the_next_read_restores_it()
    {
        var device = new FakeDevice(State(15)); // steady non-neutral physical state (M2 held)
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);
        var summaryTask = ObserveSummary(source);
        var nonNeutral = new ControllerState(new AuxiliaryButtonState([false, true]));

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        Assert.True(SpinWait.SpinUntil(() => source.LatestState == nonNeutral, AwaitTimeout));
        var readsBefore = device.ReadCount;

        source.ResetLatestStateToNeutral();

        // The published snapshot is immediately neutral; the DirectInput session is untouched.
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount); // no reacquire / new session

        // The next successful poll writes the current mapped physical state straight back, even
        // though no StateChanged transition is raised (the value equals the previous mapped state).
        Assert.True(SpinWait.SpinUntil(() => source.LatestState == nonNeutral, AwaitTimeout));
        Assert.True(device.ReadCount > readsBefore);

        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(AwaitTimeout);
        Assert.Equal(MsiClawInputStopReason.Stopped, summary.StopReason);
        Assert.Equal(1, summary.TestSession); // same session throughout
    }

    [Fact]
    public async Task PersistentKnownInvalidInitialState_StopsAndCleansUpAfterBoundedAllowance()
    {
        var device = new FakeDevice(Enumerable.Repeat<object>(InvalidInitialState(), 17).ToArray());
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.InitialStateNotReady, summary.StopReason);
        Assert.Equal(17, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task KnownInvalidStateAfterFirstValidState_StopsFailClosed()
    {
        var device = new FakeDevice(State(), InvalidInitialState());
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.ReadPerformed += count => { if (count >= 2) secondRead.TrySetResult(); };

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await secondRead.Task.WaitAsync(AwaitTimeout);
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(MsiClawInputStopReason.InvalidButtonLayout, summary.StopReason);
        Assert.Equal(2, device.ReadCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_DoesNotCreateOrAcquireAnotherDevice()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        Assert.Equal(MsiClawInputStartStatus.AlreadyRunning, source.StartPrepared(Device(0x0DB0, 0x1902)).Status);
        await source.StopAsync();

        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, device.AcquireCount);
    }

    [Fact]
    public async Task StateChanged_RaisesForInitialSnapshotAndActualChangesOnly()
    {
        var device = new FakeDevice(State(), State(), State(), State(15), State(15));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var states = new List<ControllerState>();
        var changedStateObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += (_, state) =>
        {
            states.Add(state);
            if (state == new ControllerState(new AuxiliaryButtonState([false, true]))) changedStateObserved.TrySetResult();
        };

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await changedStateObserved.Task.WaitAsync(AwaitTimeout);
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, true])), source.LatestState);
        await source.StopAsync();
        Assert.Equal(new ControllerState(new AuxiliaryButtonState([false, false])), source.LatestState);

        Assert.Equal(
            [new ControllerState(new AuxiliaryButtonState([false, false])), new ControllerState(new AuxiliaryButtonState([false, true]))],
            states);
        var controls = new MsiClawDeviceAdapter().AuxiliaryControls;
        Assert.All(states, state => Assert.Equal(controls.Count, state.Auxiliary.Count));
        Assert.True(states[1].Auxiliary[controls.GetIndex(new SteamInputAddonforClaw.Devices.Abstractions.AuxiliaryControlId("msi.claw.m1"))]);
        Assert.False(states[1].Auxiliary[controls.GetIndex(new SteamInputAddonforClaw.Devices.Abstractions.AuxiliaryControlId("msi.claw.m2"))]);
    }

    [Theory]
    [MemberData(nameof(IndependentCases))]
    public async Task IndependentVerification_RequiresBothExclusiveButtonStates(object[] reads, bool expectedM1, bool expectedM2, bool expectedIndependent)
    {
        var device = new FakeDevice(reads);
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);
        var expectedReadsObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.ReadPerformed += readCount =>
        {
            if (readCount >= reads.Length) expectedReadsObserved.TrySetResult();
        };

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await expectedReadsObserved.Task.WaitAsync(AwaitTimeout);
        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.Equal(expectedM1, summary.M1Observed);
        Assert.Equal(expectedM2, summary.M2Observed);
        Assert.Equal(expectedIndependent, summary.Independent);
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return [new object[] { State(15) }, true, false, false];
        yield return [new object[] { State(16) }, false, true, false];
        yield return [new object[] { State(15), State(), State(16) }, true, true, true];
        yield return [new object[] { State(15, 16) }, true, true, false];
    }

    [Fact]
    public async Task CleanupFailures_DoNotPreventRemainingCleanupOrSummary()
    {
        var device = new FakeDevice(State()) { UnacquireException = new InvalidOperationException("Unacquire failed"), DisposeException = new InvalidOperationException("Dispose failed") };
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(AwaitTimeout);

        Assert.False(summary.CleanupSucceeded);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task DisposeWhileRunning_CancelsPollingAndDisposesAllResources()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await source.DisposeAsync();

        Assert.False(source.IsRunning);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public async Task StopAndDispose_WhenCalledConcurrently_CleanUpExactlyOnce()
    {
        var device = new FakeDevice(State());
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await Task.WhenAll(source.StopAsync(), source.StopAsync(), source.DisposeAsync().AsTask());

        Assert.False(source.IsRunning);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task SecondSession_UsesFreshResourcesAndDoesNotReceivePriorCleanup()
    {
        var first = new FakeEnumerator([Device(0x0DB0, 0x1902)], new FakeDevice(State()));
        var second = new FakeEnumerator([Device(0x0DB0, 0x1902)], new FakeDevice(State()));
        var enumerators = new Queue<FakeEnumerator>([first, second]);
        var source = new MsiClawInputSource(() => enumerators.Dequeue());

        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await source.StopAsync();
        Assert.True(source.StartPrepared(Device(0x0DB0, 0x1902)).Started);
        await source.StopAsync();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.Equal(1, first.Device!.UnacquireCount);
        Assert.Equal(1, second.Device!.UnacquireCount);
    }

    private static Task<MsiClawInputTestSummary> ObserveSummary(MsiClawInputSource source)
    {
        var completion = new TaskCompletionSource<MsiClawInputTestSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TestCompleted += (_, summary) => completion.TrySetResult(summary);
        return completion.Task;
    }

    private static MsiClawInputSource CreateSource(
        FakeEnumerator enumerator,
        FakeWaitableTimerNativeApi productionTimerApi,
        Func<long>? timestampProvider = null,
        Func<bool>? requires250Hz = null) =>
        new(
            () => enumerator,
            static () => throw new InvalidOperationException("The cadence diagnostic timer is not used by this test."),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi),
            timestampProvider,
            requires250Hz);

    private static DirectInputDeviceDescriptor Device(ushort vendorId, ushort productId, string? physicalIdentity = "USB\\MSI_ROOT", int? buttonCount = 17, string? pnpInstanceId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test", vendorId, productId, "\\\\?\\hid#vid_0db0&pid_1902&mi_00&col01#test#{00000000-0000-0000-0000-000000000000}", pnpInstanceId ?? "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST", physicalIdentity, 0x0001, 0x0005, buttonCount, 6);
    private static DirectInputState State(params int[] pressedButtons)
    {
        var buttons = new bool[17];
        foreach (var button in pressedButtons) buttons[button] = true;
        return new DirectInputState(buttons);
    }
    private static DirectInputState InvalidInitialState(int buttonCount = 128) => new(new bool[buttonCount], 32767, 32767, 32767, 32767, 32767, 32767, [-1]);

    private sealed class FakeEnumerator(IReadOnlyList<DirectInputDeviceDescriptor> devices, FakeDevice? device = null) : IDirectInputDeviceEnumerator
    {
        public int CreateCount { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? EnumerationException { get; init; }
        public Exception? CreateException { get; init; }
        public Exception? DisposeException { get; init; }
        public DirectInputDeviceDescriptor? CreatedDescriptor { get; private set; }
        public FakeDevice? Device { get; } = device;
        public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers() => EnumerationException is null ? devices : throw EnumerationException;
        public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor)
        {
            CreateCount++;
            CreatedDescriptor = descriptor;
            if (CreateException is not null) throw CreateException;
            return Device ?? new FakeDevice(State());
        }
        public void Dispose()
        {
            DisposeCount++;
            Disposed = true;
            if (DisposeException is not null) throw DisposeException;
        }
    }

    private sealed class FakeDevice(params object[] reads) : IDirectInputDevice
    {
        private readonly Queue<object> _reads = new(reads);
        private DirectInputState _last = State();
        public int AcquireCount { get; private set; }
        public int UnacquireCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ReadCount { get; private set; }
        public Exception? AcquireException { get; init; }
        public Exception? UnacquireException { get; init; }
        public Exception? DisposeException { get; init; }
        public event Action<int>? ReadPerformed;
        public event Action<int>? ReadAttempted;
        public TaskCompletionSource FirstReadAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Acquire()
        {
            AcquireCount++;
            if (AcquireException is not null) throw AcquireException;
        }
        public void Unacquire()
        {
            UnacquireCount++;
            if (UnacquireException is not null) throw UnacquireException;
        }
        public DirectInputState ReadState()
        {
            ReadCount++;
            ReadAttempted?.Invoke(ReadCount);
            FirstReadAttempted.TrySetResult();
            if (_reads.Count > 0)
            {
                var next = _reads.Dequeue();
                if (next is Exception exception) throw exception;
                _last = (DirectInputState)next;
            }
            ReadPerformed?.Invoke(ReadCount);
            return _last;
        }
        public void Dispose()
        {
            DisposeCount++;
            if (DisposeException is not null) throw DisposeException;
        }
    }

    private sealed class FakeWaitableTimerNativeApi : IWaitableTimerNativeApi, IDisposable
    {
        private readonly AutoResetEvent _signal = new(false);
        private int _createCount;
        private int _armCount;
        private int _cancelCount;

        public bool FailCreate { get; init; }
        public int FailOnArmNumber { get; init; }
        public int CreateCount => Volatile.Read(ref _createCount);
        public int ArmCount => Volatile.Read(ref _armCount);
        public int CancelCount => Volatile.Read(ref _cancelCount);
        public long LastDueTime100ns { get; private set; }

        public SafeWaitHandle CreateWaitableTimerEx(uint flags, uint desiredAccess)
        {
            Interlocked.Increment(ref _createCount);
            return new SafeWaitHandle(FailCreate ? IntPtr.Zero : _signal.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        }

        public bool SetWaitableTimerEx(SafeWaitHandle handle, long dueTime100ns, int periodMs)
        {
            LastDueTime100ns = dueTime100ns;
            var arm = Interlocked.Increment(ref _armCount);
            return arm != FailOnArmNumber;
        }

        public bool CancelWaitableTimer(SafeWaitHandle handle)
        {
            Interlocked.Increment(ref _cancelCount);
            return true;
        }

        public int GetLastWin32Error() => 5;
        public void Pulse() => _signal.Set();
        public void Dispose() => _signal.Dispose();
    }
}
