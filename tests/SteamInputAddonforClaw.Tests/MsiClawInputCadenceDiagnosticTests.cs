using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawInputCadenceDiagnosticTests
{
    [Fact]
    public void Production_poll_interval_is_8ms_and_diagnostic_interval_is_1ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(8), MsiClawInputSource.ResolvePollInterval(false));
        Assert.Equal(TimeSpan.FromMilliseconds(1), MsiClawInputSource.ResolvePollInterval(true));
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
        var source = new MsiClawInputSource(enumerator);

        Assert.True(source.StartPrepared(Device()).Started);
        var result = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, result.Outcome);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Equal(TimeSpan.FromMilliseconds(8), MsiClawInputSource.ResolvePollInterval(false));
        await source.StopAsync();
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
        var source = new MsiClawInputSource(new TestEnumerator());
        Assert.True(source.StartPrepared(Device()).Started);
        using var cancellation = new CancellationTokenSource();
        var first = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1), cancellation.Token);

        var second = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(FrontendPid1902InputCadenceOutcome.AlreadyRunning, second.Outcome);

        cancellation.Cancel();
        var cancelled = await first;
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(8), MsiClawInputSource.ResolvePollInterval(false));

        var next = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, next.Outcome);
        await source.StopAsync();
    }

    [Fact]
    public async Task Source_loss_fails_the_diagnostic_without_reusing_a_later_session()
    {
        var device = new FailingAfterFirstReadDevice();
        var enumerators = new Queue<IDirectInputDeviceEnumerator>([new TestEnumerator(device), new TestEnumerator()]);
        var source = new MsiClawInputSource(() => enumerators.Dequeue());
        Assert.True(source.StartPrepared(Device()).Started);
        await device.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromSeconds(1));
        device.AllowFailure.TrySetResult();
        var result = await diagnostic.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendPid1902InputCadenceOutcome.Failed, result.Outcome);
        Assert.True(SpinWait.SpinUntil(() => !source.IsRunning, TimeSpan.FromSeconds(5)));
        Assert.False(source.IsRunning);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.DisposeCount);

        Assert.True(source.StartPrepared(Device()).Started);
        var nextSession = await source.RunPid1902InputCadenceDiagnosticAsync(TimeSpan.FromMilliseconds(25));
        Assert.Equal(FrontendPid1902InputCadenceOutcome.Completed, nextSession.Outcome);
        await source.StopAsync();
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
        private readonly TestDevice _device = device ?? new TestDevice();
        public int CreateCount { get; private set; }
        public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers() => [];
        public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor) { CreateCount++; return _device; }
        public void Dispose() { }
    }

    private class TestDevice : IDirectInputDevice
    {
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Acquire() => AcquireCount++;
        public virtual DirectInputState ReadState() => State();
        public void Unacquire() { }
        public void Dispose() => DisposeCount++;
    }

    private sealed class FailingAfterFirstReadDevice : TestDevice
    {
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        public override DirectInputState ReadState()
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                FirstRead.TrySetResult();
                return State();
            }

            AllowFailure.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("simulated DirectInput loss");
        }
    }
}
