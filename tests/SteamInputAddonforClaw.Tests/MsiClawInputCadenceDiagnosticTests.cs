using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawInputCadenceDiagnosticTests
{
    [Fact]
    public void Production_poll_interval_is_4ms_and_diagnostic_interval_is_1ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(4), MsiClawInputSource.ResolvePollInterval(false));
        Assert.Equal(TimeSpan.FromMilliseconds(1), MsiClawInputSource.ResolvePollInterval(true));
        Assert.Equal(TimeSpan.FromMilliseconds(4), MsiClawInputSource.ResolveProductionPeriod(requires250Hz: true));
        Assert.Equal(TimeSpan.FromMilliseconds(8), MsiClawInputSource.ResolveProductionPeriod(requires250Hz: false));
    }

    [Fact]
    public void Raw_state_equality_includes_axes_buttons_and_pov()
    {
        var baseline = State();

        Assert.True(MsiClawInputCadenceCollector.RawStateEquals(baseline, State()));
        Assert.False(MsiClawInputCadenceCollector.RawStateEquals(baseline, State(x: 1)));
        Assert.False(MsiClawInputCadenceCollector.RawStateEquals(baseline, State(button: 2)));
        Assert.False(MsiClawInputCadenceCollector.RawStateEquals(baseline, State(pov: 9000)));
    }

    [Fact]
    public async Task Collector_counts_duplicates_and_produces_deterministic_interval_statistics()
    {
        var collector = new MsiClawInputCadenceCollector(TimeSpan.FromMilliseconds(10), Timestamp(0));
        collector.Observe(State(), Timestamp(0));
        collector.Observe(State(), Timestamp(1));
        collector.Observe(State(x: 1), Timestamp(5));
        collector.Observe(State(button: 2), Timestamp(7));
        collector.Observe(State(pov: 9000), Timestamp(10));

        var result = await collector.Completion;

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, result.Outcome);
        Assert.Equal(5, result.SuccessfulReadCount);
        Assert.Equal(4, result.DistinctStateCount);
        Assert.Equal(1, result.DuplicateReadCount);
        Assert.Equal(20d, result.DuplicatePercent);
        Assert.Equal(10L, result.ActualDurationMs);
        Assert.Equal(300d, result.DistinctStateHz);
        Assert.Equal(2d, result.MinDistinctIntervalMs);
        Assert.Equal(10d / 3d, result.MeanDistinctIntervalMs!.Value, 5);
        Assert.Equal(3d, result.MedianDistinctIntervalMs);
        Assert.Equal(5d, result.P95DistinctIntervalMs);
        Assert.Equal(5d, result.MaxDistinctIntervalMs);
    }

    [Fact]
    public void Percentile_uses_nearest_rank_and_empty_statistics_are_nullable()
    {
        Assert.Equal(5d, MsiClawInputCadenceCollector.PercentileNearestRank([1d, 2d, 3d, 4d, 5d], 0.95));
        Assert.Equal(3d, MsiClawInputCadenceCollector.Median([1d, 2d, 4d, 5d]));
        Assert.Null(MsiClawInputCadenceCollector.Median([]));
        Assert.Null(MsiClawInputCadenceCollector.PercentileNearestRank([], 0.95));
    }

    [Fact]
    public void Cadence_result_round_trips_as_json()
    {
        var value = new FrontendPid1902InputCadenceResult(
            FrontendPid1902InputCadenceOutcome.Completed, "Completed", 10_000, 10_003, 7_842, 784.0,
            1_247, 6_595, 84.1, 124.8, 6.94, 8.01, 8.00, 8.46, 11.21);

        var restored = JsonSerializer.Deserialize<FrontendPid1902InputCadenceResult>(JsonSerializer.Serialize(value));

        Assert.Equivalent(value, restored, strict: true);
    }

    [Fact]
    public async Task Source_uses_one_live_session_and_restores_normal_polling_after_completion()
    {
        var enumerator = new TestEnumerator();
        using var timerApi = new FakeWaitableTimerNativeApi();
        using var productionTimerApi = new FakeWaitableTimerNativeApi { AutoPulse = false };
        var requires250Hz = 1;
        var productionIntervals = new ConcurrentQueue<long>();
        await using var source = new MsiClawInputSource(
            () => enumerator,
            () => new WindowsHighResolutionOneShotTimer(timerApi),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi),
            requires250Hz: () => Volatile.Read(ref requires250Hz) != 0)
        {
            ArmForDeadlineOverrideForTests = (timer, deadline, now) =>
            {
                productionIntervals.Enqueue(deadline - now);
                timer.ArmRelative(TimeSpan.FromSeconds(30));
            },
        };

        Assert.True(source.StartPrepared(Device()).Started);
        await enumerator.Device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var diagnostic = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(250));
        productionTimerApi.Pulse();
        await timerApi.FirstArm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref requires250Hz, 0);
        var result = await diagnostic;
        await productionTimerApi.SecondArm.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, result.Outcome);
        Assert.True(timerApi.SetWaitableTimerExCallCount > 0);
        Assert.Equal(-TimeSpan.FromMilliseconds(1).Ticks, timerApi.ArmedDueTime100ns);
        Assert.Equal(1, timerApi.CancelWaitableTimerCallCount);
        Assert.Equal(1, productionTimerApi.CreateWaitableTimerExCallCount);
        Assert.True(productionTimerApi.SetWaitableTimerExCallCount >= 2);
        Assert.Contains(
            CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), Stopwatch.Frequency),
            productionIntervals);
        Assert.Contains(
            CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(8), Stopwatch.Frequency),
            productionIntervals);
        Assert.NotEqual(timerApi.SignalHandle, productionTimerApi.SignalHandle);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
        Assert.True(source.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(8), MsiClawInputSource.ResolveProductionPeriod(requires250Hz: false));
    }

    [Fact]
    public async Task Source_without_a_live_session_returns_unavailable()
    {
        var source = new MsiClawInputSource(new TestEnumerator());

        var result = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Source_rejects_a_second_request_and_cancellation_clears_diagnostic_state()
    {
        var enumerator = new TestEnumerator();
        using var timerApi = new FakeWaitableTimerNativeApi();
        using var productionTimerApi = new FakeWaitableTimerNativeApi { AutoPulse = false };
        await using var source = new MsiClawInputSource(
            () => enumerator,
            () => new WindowsHighResolutionOneShotTimer(timerApi),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi));
        Assert.True(source.StartPrepared(Device()).Started);
        await enumerator.Device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var first = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1), cancellation.Token);

        productionTimerApi.Pulse();
        await timerApi.FirstArm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FrontendPid1902InputCadenceOutcome.AlreadyRunning, second.Outcome);

        cancellation.Cancel();
        var cancelled = await first;
        await productionTimerApi.SecondArm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(1, timerApi.CancelWaitableTimerCallCount);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);

        var nextTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var next = await nextTask;
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, next.Outcome);
        await productionTimerApi.ThirdArm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, timerApi.CreateWaitableTimerExCallCount);
        Assert.Equal(2, timerApi.CancelWaitableTimerCallCount);
        Assert.Equal(1, productionTimerApi.CreateWaitableTimerExCallCount);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
    }

    [Fact]
    public async Task Timer_creation_failure_fails_only_the_diagnostic_and_allows_a_later_run()
    {
        var enumerator = new TestEnumerator();
        using var failingTimerApi = new FakeWaitableTimerNativeApi { FailCreate = true };
        using var workingTimerApi = new FakeWaitableTimerNativeApi();
        using var productionTimerApi = new FakeWaitableTimerNativeApi { AutoPulse = false };
        var timerFactoryCalls = 0;
        await using var source = new MsiClawInputSource(
            () => enumerator,
            () => new WindowsHighResolutionOneShotTimer(Interlocked.Increment(ref timerFactoryCalls) == 1 ? failingTimerApi : workingTimerApi),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi));

        Assert.True(source.StartPrepared(Device()).Started);
        await enumerator.Device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var failedTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var failed = await failedTask;

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Failed, failed.Outcome);
        Assert.Contains("high-resolution timer", failed.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
        productionTimerApi.Pulse();
        await enumerator.Device.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var recoveredTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var recovered = await recoveredTask;

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, recovered.Outcome);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
        Assert.Equal(1, workingTimerApi.CancelWaitableTimerCallCount);
    }

    [Fact]
    public async Task Timer_arm_failure_fails_only_the_diagnostic_and_allows_a_later_run()
    {
        var enumerator = new TestEnumerator();
        using var failingTimerApi = new FakeWaitableTimerNativeApi { FailArm = true };
        using var workingTimerApi = new FakeWaitableTimerNativeApi();
        using var productionTimerApi = new FakeWaitableTimerNativeApi { AutoPulse = false };
        var timerFactoryCalls = 0;
        await using var source = new MsiClawInputSource(
            () => enumerator,
            () => new WindowsHighResolutionOneShotTimer(Interlocked.Increment(ref timerFactoryCalls) == 1 ? failingTimerApi : workingTimerApi),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi));

        Assert.True(source.StartPrepared(Device()).Started);
        await enumerator.Device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var failedTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var failed = await failedTask;

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Failed, failed.Outcome);
        Assert.Contains("timer failed", failed.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, failingTimerApi.SetWaitableTimerExCallCount);
        Assert.Equal(1, failingTimerApi.CancelWaitableTimerCallCount);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
        productionTimerApi.Pulse();
        await enumerator.Device.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var recoveredTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var recovered = await recoveredTask;

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, recovered.Outcome);
        Assert.True(source.IsRunning);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(1, enumerator.Device.AcquireCount);
        Assert.Equal(1, workingTimerApi.CancelWaitableTimerCallCount);
    }

    [Fact]
    public async Task Source_loss_fails_the_diagnostic_without_reusing_a_later_session()
    {
        var device = new FailingAfterFirstReadDevice();
        var enumerators = new Queue<IDirectInputDeviceEnumerator>([new TestEnumerator(device), new TestEnumerator()]);
        using var timerApi = new FakeWaitableTimerNativeApi();
        using var productionTimerApi = new FakeWaitableTimerNativeApi { AutoPulse = false };
        await using var source = new MsiClawInputSource(
            () => enumerators.Dequeue(),
            () => new WindowsHighResolutionOneShotTimer(timerApi),
            () => new WindowsHighResolutionOneShotTimer(productionTimerApi));
        Assert.True(source.StartPrepared(Device()).Started);
        await device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1));
        productionTimerApi.Pulse();
        device.AllowFailure.TrySetResult();
        var result = await diagnostic.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Failed, result.Outcome);
        Assert.True(SpinWait.SpinUntil(() => !source.IsRunning, TimeSpan.FromSeconds(5)));
        Assert.False(source.IsRunning);
        Assert.Equal(1, timerApi.CancelWaitableTimerCallCount);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.DisposeCount);

        Assert.True(source.StartPrepared(Device()).Started);
        var nextSessionTask = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        productionTimerApi.Pulse();
        var nextSession = await nextSessionTask;
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, nextSession.Outcome);
    }

    private static DirectInputState State(int? x = null, int? button = null, int? pov = null)
    {
        var buttons = new bool[17];
        if (button is { } index) buttons[index] = true;
        return new DirectInputState(buttons, X: x, pointOfViewControllers: pov is { } value ? [value] : [-1]);
    }

    private static DirectInputDeviceDescriptor Device() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test", 0x0DB0, 0x1902,
            "\\\\?\\hid#vid_0db0&pid_1902&mi_00&col01#test",
            "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST", "USB\\MSI_ROOT", 0x0001, 0x0005, 17, 6);

    private static long Timestamp(double milliseconds) => (long)(milliseconds / 1000d * Stopwatch.Frequency);

    private sealed class TestEnumerator(TestDevice? device = null) : IDirectInputDeviceEnumerator
    {
        internal TestDevice Device { get; } = device ?? new TestDevice();
        public int CreateCount { get; private set; }
        public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers() => [];
        public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor) { CreateCount++; return Device; }
        public void Dispose() { }
    }

    private class TestDevice : IDirectInputDevice
    {
        private int _readCount;
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ReadCount => Volatile.Read(ref _readCount);
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Acquire() => AcquireCount++;
        public virtual DirectInputState ReadState()
        {
            RecordRead();
            return State();
        }
        protected int RecordRead()
        {
            var count = Interlocked.Increment(ref _readCount);
            if (count == 1) FirstRead.TrySetResult();
            if (count == 2) SecondRead.TrySetResult();
            return count;
        }
        public void Unacquire() { }
        public void Dispose() => DisposeCount++;
    }

    private sealed class FailingAfterFirstReadDevice : TestDevice
    {
        public TaskCompletionSource AllowFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DirectInputState ReadState()
        {
            if (RecordRead() == 1)
            {
                return State();
            }

            AllowFailure.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("simulated DirectInput loss");
        }
    }

    private sealed class FakeWaitableTimerNativeApi : IWaitableTimerNativeApi, IDisposable
    {
        private readonly AutoResetEvent _signal = new(false);
        private readonly Timer _pulseTimer;

        internal bool FailCreate;
        internal bool FailArm;
        internal bool AutoPulse { get; set; } = true;
        internal int CreateWaitableTimerExCallCount;
        internal int SetWaitableTimerExCallCount;
        internal int CancelWaitableTimerCallCount;
        internal long ArmedDueTime100ns;
        internal TaskCompletionSource FirstArm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondArm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ThirdArm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IntPtr SignalHandle => _signal.SafeWaitHandle.DangerousGetHandle();

        internal FakeWaitableTimerNativeApi()
        {
            _pulseTimer = new Timer(static state =>
            {
                var signal = (AutoResetEvent)state!;
                try { signal.Set(); }
                catch (ObjectDisposedException) { }
            }, _signal, Timeout.Infinite, Timeout.Infinite);
        }

        public SafeWaitHandle CreateWaitableTimerEx(uint flags, uint desiredAccess)
        {
            CreateWaitableTimerExCallCount++;
            return new SafeWaitHandle(FailCreate ? IntPtr.Zero : _signal.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        }

        public bool SetWaitableTimerEx(SafeWaitHandle handle, long dueTime100ns, int periodMs)
        {
            SetWaitableTimerExCallCount++;
            ArmedDueTime100ns = dueTime100ns;
            if (SetWaitableTimerExCallCount == 1) FirstArm.TrySetResult();
            if (SetWaitableTimerExCallCount == 2) SecondArm.TrySetResult();
            if (SetWaitableTimerExCallCount == 3) ThirdArm.TrySetResult();
            if (FailArm) return false;
            if (AutoPulse) _pulseTimer.Change(1, Timeout.Infinite);
            return true;
        }

        public bool CancelWaitableTimer(SafeWaitHandle handle)
        {
            CancelWaitableTimerCallCount++;
            _pulseTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return true;
        }

        public int GetLastWin32Error() => 5;
        internal void Pulse() => _signal.Set();

        public void Dispose()
        {
            _pulseTimer.Dispose();
            _signal.Dispose();
        }
    }
}
