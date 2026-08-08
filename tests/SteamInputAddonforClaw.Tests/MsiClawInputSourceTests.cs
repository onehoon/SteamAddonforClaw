using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawInputSourceTests
{
    [Fact]
    public void Start_WhenPid1902IsMissing_DoesNotCreateOrAcquireAnyDevice()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1901)]);
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.Pid1902NotFound, result.Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public void Start_WhenMultiplePid1902CandidatesExist_DoesNotCreateOrAcquireAnyDevice()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902), Device(0x0DB0, 0x1902)]);
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.Indeterminate, result.Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public async Task StartAndStop_MapsM1AndM2AndCleansUpDevice()
    {
        var device = new FakeDevice([State(15), State(16)]);
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var states = new List<ControllerState>();
        source.StateChanged += (_, state) => states.Add(state);

        Assert.True(source.Start().Started);
        await Task.Delay(40);
        await source.StopAsync();

        Assert.Contains(new ControllerState(true, false), states);
        Assert.Contains(new ControllerState(false, true), states);
        Assert.Equal(1, device.AcquireCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.True(device.Disposed);
    }

    private static DirectInputDeviceDescriptor Device(ushort vendorId, ushort productId) => new(Guid.NewGuid(), Guid.NewGuid(), "Test", vendorId, productId);
    private static DirectInputState State(int button) { var buttons = new bool[17]; buttons[button] = true; return new DirectInputState(buttons); }

    private sealed class FakeEnumerator(IReadOnlyList<DirectInputDeviceDescriptor> devices, FakeDevice? device = null) : IDirectInputDeviceEnumerator
    {
        public int CreateCount { get; private set; }
        public IReadOnlyList<DirectInputDeviceDescriptor> EnumerateGameControllers() => devices;
        public IDirectInputDevice CreateDevice(DirectInputDeviceDescriptor descriptor) { CreateCount++; return device ?? new FakeDevice([]); }
        public void Dispose() { }
    }

    private sealed class FakeDevice(IEnumerable<DirectInputState> states) : IDirectInputDevice
    {
        private readonly Queue<DirectInputState> _states = new(states);
        private DirectInputState? _last;
        public int AcquireCount { get; private set; }
        public int UnacquireCount { get; private set; }
        public bool Disposed { get; private set; }
        public void Acquire() => AcquireCount++;
        public void Unacquire() => UnacquireCount++;
        public DirectInputState ReadState()
        {
            if (_states.Count > 0)
            {
                _last = _states.Dequeue();
            }

            return _last ?? throw new InvalidOperationException("No test state is available.");
        }
        public void Dispose() => Disposed = true;
    }
}
