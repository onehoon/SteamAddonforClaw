using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawPhysicalInputStageTests
{
    [Fact]
    public async Task ObserveOnly_EnumeratesWithoutAcquiring()
    {
        var enumerator = new FakeEnumerator([Device()]);
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => enumerator, input);
        var result = await stage.ObserveAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(1, enumerator.EnumerateCount);
        Assert.Equal(0, enumerator.CreateCount);
        Assert.Equal(0, input.StartPreparedCount);
    }

    [Fact]
    public async Task PrepareIsReadOnlyAndExecuteUsesExactPreparedDescriptor()
    {
        var descriptor = Device();
        var enumerator = new FakeEnumerator([descriptor]);
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => enumerator, input);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(0, enumerator.CreateCount);
        Assert.Equal(0, input.StartPreparedCount);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(descriptor, input.PreparedDescriptor);
        Assert.Equal(1, input.StartPreparedCount);
        Assert.Equal(new MsiClawPhysicalInputIdentity(descriptor.InstanceGuid, descriptor.DevicePath!, descriptor.PnpInstanceId!, descriptor.PhysicalIdentity!), stage.CurrentIdentity);
    }

    [Fact]
    public async Task ExecuteWithoutPrepare_FailsClosed()
    {
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("PhysicalInputNotPrepared", result.Reason);
        Assert.Equal(0, input.StartPreparedCount);
    }

    [Fact]
    public async Task RollbackStopsOnlyOwnedSessionAndIsIdempotent()
    {
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, input.StopCount);
        Assert.Null(stage.CurrentIdentity);
    }

    [Fact]
    public async Task PreExistingInputSession_IsNotStolen()
    {
        var input = new FakeInput { IsRunning = true };
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        var result = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("InputSourceAlreadyRunning", result.Reason);
        Assert.Equal(0, input.StopCount);
    }

    [Fact]
    public async Task Execute_RequiresFirstValidStateBeforeItSucceedsOrPublishesIdentity()
    {
        var input = new FakeInput { FirstValidStateObserved = false };
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("FirstValidStateNotObserved", result.Reason);
        Assert.Equal(1, input.StopCount);
        Assert.Null(stage.CurrentIdentity);
    }

    [Fact]
    public async Task Rollback_does_not_wait_for_rumble_before_stopping_input_source()
    {
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        var transport = new BlockingTransport();
        using var sink = new MsiClawRumbleSink(stage, transport, new TestResolver());
        stage.PhysicalSessionStarted += sink.BeginPhysicalSession;
        var retirementReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.PhysicalSessionRetiring += () => retirementReached.TrySetResult();
        stage.PhysicalSessionRetiring += sink.BeginPhysicalSessionRetirement;
        stage.PhysicalSessionRetired += sink.InvalidatePhysicalSession;
        // Rumble retirement is best-effort and must not be a prerequisite for stopping input.
        // Assert the ordering at the StopAsync boundary rather than racing the rollback
        // continuation after the first retirement event handler signals.
        input.BeforeStop = () => retirementReached.Task.IsCompleted;

        var write = Task.Run(() => sink.SetRumble(new(0xFF00, 0xFF00)));
        await transport.Entered.Task;
        var rollback = Task.Run(async () => await stage.RollbackMutationAsync(CancellationToken.None));
        await retirementReached.Task;
        transport.Release.Set();
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, (await write).Status);
        await rollback;
        Assert.Equal(1, input.StopCount);
        Assert.Equal(1, transport.InvalidateCount);
        Assert.Null(stage.CurrentIdentity);
    }

    [Fact]
    public async Task SuspendPause_stops_input_but_preserves_owned_identity_without_retirement()
    {
        var descriptor = Device();
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([descriptor]), input);
        var retiring = 0;
        var retired = 0;
        stage.PhysicalSessionRetiring += () => retiring++;
        stage.PhysicalSessionRetired += () => retired++;
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);

        var identity = stage.CurrentIdentity;
        var result = await stage.PauseForSuspendAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(input.IsRunning);
        Assert.Equal(identity, stage.CurrentIdentity);
        Assert.Equal(0, retiring);
        Assert.Equal(0, retired);
    }

    [Fact]
    public async Task ResumeAfterSuspend_retries_missing_topology_and_uses_fresh_descriptor()
    {
        var original = Device();
        var fresh = original with { InstanceGuid = Guid.NewGuid(), DevicePath = "HID\\FRESH", PnpInstanceId = "HID\\FRESH" };
        var enumerators = new Queue<FakeEnumerator>([
            new([original]), new([]), new([]), new([fresh]), new([fresh])]);
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => enumerators.Dequeue(), input, (_, _) => Task.CompletedTask);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        await stage.PauseForSuspendAsync(CancellationToken.None);

        var result = await stage.ResumeAfterSuspendAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(input.IsRunning);
        Assert.Equal(fresh, input.PreparedDescriptor);
        Assert.Equal(fresh.PhysicalIdentity, stage.CurrentIdentity!.PhysicalIdentity);
        Assert.Equal(fresh.InstanceGuid, stage.CurrentIdentity.InstanceGuid);
    }

    [Fact]
    public async Task ResumeAfterSuspend_rejects_different_physical_controller_without_mutation()
    {
        var original = Device();
        var foreign = original with { PhysicalIdentity = "USB\\FOREIGN" };
        var enumerators = new Queue<FakeEnumerator>([
            new([original]), new([foreign])]);
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => enumerators.Dequeue(), input, (_, _) => Task.CompletedTask);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        await stage.PauseForSuspendAsync(CancellationToken.None);

        var result = await stage.ResumeAfterSuspendAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(input.IsRunning);
        Assert.Equal(original.PhysicalIdentity, stage.CurrentIdentity!.PhysicalIdentity);
        Assert.Equal(original, input.PreparedDescriptor);
    }

    [Fact]
    public async Task Rollback_from_suspend_pause_retires_owned_session_and_clears_identity()
    {
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([Device()]), input);
        var retired = 0;
        stage.PhysicalSessionRetired += () => retired++;
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        await stage.PauseForSuspendAsync(CancellationToken.None);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(stage.CurrentIdentity);
        Assert.Equal(1, retired);
        Assert.False(input.IsRunning);
    }

    [Fact]
    public async Task ResumeAfterSuspend_requires_first_valid_state_and_stays_paused_on_failure()
    {
        var original = Device();
        var enumerators = new Queue<FakeEnumerator>([new([original]), new([original])]);
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => enumerators.Dequeue(), input, (_, _) => Task.CompletedTask);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        await stage.PauseForSuspendAsync(CancellationToken.None);
        input.FirstValidStateObserved = false;

        var result = await stage.ResumeAfterSuspendAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(input.IsRunning);
        Assert.Equal(original.PhysicalIdentity, stage.CurrentIdentity!.PhysicalIdentity);
    }

    [Fact]
    public async Task ResumeAfterSuspend_cancellation_stops_new_input_and_remains_paused()
    {
        var descriptor = Device();
        var input = new FakeInput();
        var stage = new MsiClawPhysicalInputStage(() => new FakeEnumerator([descriptor]), input, (_, _) => Task.CompletedTask);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        await stage.PauseForSuspendAsync(CancellationToken.None);
        input.FirstValidWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var resume = stage.ResumeAfterSuspendAsync(cancellation.Token).AsTask();
        await input.FirstValidWaitEntered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resume);
        Assert.False(input.IsRunning);
        Assert.NotNull(stage.CurrentIdentity);
        Assert.True((await stage.PauseForSuspendAsync(CancellationToken.None)).Succeeded);
        Assert.False(input.IsRunning);
    }

    private static DirectInputDeviceDescriptor Device() => new(
        Guid.NewGuid(), Guid.NewGuid(), "test", 0x0DB0, 0x1902,
        "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST", "HID\\INSTANCE", "USB\\MSI_ROOT", 0x0001, 0x0005, 17, 6, "Verified");

    private sealed class FakeEnumerator(IReadOnlyList<DirectInputDeviceDescriptor> devices) : IDirectInputDeviceEnumerator
    {
        public int EnumerateCount { get; private set; }
        public int CreateCount { get; private set; }
        public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers() { EnumerateCount++; return devices; }
        public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor) { CreateCount++; return new FakeDevice(); }
        public void Dispose() { }
    }

    private sealed class FakeDevice : IDirectInputDevice
    {
        public void Acquire() { }
        public void Unacquire() { }
        public DirectInputState ReadState() => new(new bool[17]);
        public void Dispose() { }
    }

    private sealed class FakeInput : IMsiClawPreparedInputSource
    {
        public event EventHandler<ControllerState>? StateChanged = delegate { };
        public bool IsRunning { get; set; }
        public bool FirstValidStateObserved { get; set; } = true;
        public int StartPreparedCount { get; private set; }
        public int StopCount { get; private set; }
        public Func<bool>? BeforeStop { get; set; }
        public DirectInputDeviceDescriptor? PreparedDescriptor { get; private set; }
        public TaskCompletionSource<bool>? FirstValidWait { get; set; }
        public TaskCompletionSource FirstValidWaitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartPreparedCount++;
            PreparedDescriptor = descriptor;
            IsRunning = true;
            return new(MsiClawInputStartStatus.Started, "Started");
        }
        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken)
        {
            if (FirstValidWait is null) return Task.FromResult(FirstValidStateObserved);
            FirstValidWaitEntered.TrySetResult();
            return FirstValidWait.Task.WaitAsync(cancellationToken);
        }
        public Task StopAsync() { Assert.True(BeforeStop?.Invoke() ?? true); StopCount++; IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestResolver : IMsiClawRumbleEndpointResolver
    {
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity) => new(identity.DevicePath, "Test");
    }

    private sealed class BlockingTransport : IMsiClawRumbleTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public int InvalidateCount { get; private set; }
        public MsiClawRumbleTransportResult Write(string path, ReadOnlySpan<byte> packet, int outputReportLength)
        {
            Entered.TrySetResult();
            Release.Wait();
            return new(true, "OK");
        }
        public void InvalidatePhysicalSession() => InvalidateCount++;
        public void Dispose() { }
    }
}
