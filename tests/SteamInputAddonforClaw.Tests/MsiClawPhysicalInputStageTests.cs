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
    public async Task Rollback_uses_retirement_barrier_before_stopping_input_source()
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
        input.BeforeStop = () => true;

        var write = Task.Run(() => sink.SetRumble(new(0xFF00, 0xFF00)));
        await transport.Entered.Task;
        var rollback = Task.Run(async () => await stage.RollbackMutationAsync(CancellationToken.None));
        await retirementReached.Task;
        Assert.Equal(0, input.StopCount);
        transport.Release.Set();
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, (await write).Status);
        await rollback;
        Assert.Equal(1, input.StopCount);
        Assert.Equal(1, transport.InvalidateCount);
        Assert.Null(stage.CurrentIdentity);
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
        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartPreparedCount++;
            PreparedDescriptor = descriptor;
            IsRunning = true;
            return new(MsiClawInputStartStatus.Started, "Started");
        }
        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken) => Task.FromResult(FirstValidStateObserved);
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
