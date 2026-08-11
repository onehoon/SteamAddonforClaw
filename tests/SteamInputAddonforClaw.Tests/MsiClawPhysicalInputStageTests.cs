using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;
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
        public int StartPreparedCount { get; private set; }
        public int StopCount { get; private set; }
        public DirectInputDeviceDescriptor? PreparedDescriptor { get; private set; }
        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartPreparedCount++;
            PreparedDescriptor = descriptor;
            IsRunning = true;
            return new(MsiClawInputStartStatus.Started, "Started");
        }
        public Task StopAsync() { StopCount++; IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
