using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawInputSourceTests
{
    [Theory]
    [InlineData(0x0DB0, 0x1901)]
    [InlineData(0x0DB0, 0x1903)]
    [InlineData(0x045E, 0x028E)]
    public void Start_WhenPid1902IsMissing_DoesNotCreateOrAcquireAnyDevice(ushort vendorId, ushort productId)
    {
        var enumerator = new FakeEnumerator([Device(vendorId, productId)]);
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.Pid1902NotFound, result.Status);
        Assert.Equal(0, enumerator.CreateCount);
        Assert.True(enumerator.Disposed);
    }

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
    public void Start_WhenPid1902IsMissingAndEnumeratorCleanupFails_PreservesTheNotFoundResult()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1901)]) { DisposeException = new InvalidOperationException("Dispose failed") };
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.Pid1902NotFound, result.Status);
        Assert.Equal(1, enumerator.DisposeCount);
    }

    [Fact]
    public void Start_SelectsOnlyPid1902_WhenOtherControllersAlsoExist()
    {
        var selected = Device(0x0DB0, 0x1902);
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([Device(0x045E, 0x028E), selected], device);
        var source = new MsiClawInputSource(enumerator);

        Assert.True(source.Start().Started);

        Assert.Equal(selected, enumerator.CreatedDescriptor);
    }

    [Fact]
    public void Start_WhenPid1902CandidatesHaveDifferentPhysicalIdentities_DoesNotCreateOrAcquireAnyDevice()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_A"), Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_B")]);
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.Indeterminate, result.Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public void Start_WhenPid1902CandidatesAreAliasesOfOnePhysicalRoot_SelectsOneDevice()
    {
        var first = Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_ROOT");
        var second = Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_ROOT");
        var enumerator = new FakeEnumerator([first, second], new FakeDevice(State()));

        var result = new MsiClawInputSource(enumerator).Start();

        Assert.True(result.Started);
        Assert.Equal(1, enumerator.CreateCount);
        Assert.Contains(enumerator.CreatedDescriptor, new[] { first, second });
    }

    [Fact]
    public void Start_WhenPid1902CandidatesSharePhysicalIdentityButHaveDifferentPnpInstances_DoesNotCreateOrAcquireAnyDevice()
    {
        var first = Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_ROOT", pnpInstanceId: "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST");
        var second = Device(0x0DB0, 0x1902, physicalIdentity: "USB\\MSI_ROOT", pnpInstanceId: "HID\\VID_0DB0&PID_1902&MI_00&COL02\\TEST");
        var enumerator = new FakeEnumerator([first, second]);

        var result = new MsiClawInputSource(enumerator).Start();

        Assert.Equal(MsiClawInputStartStatus.Indeterminate, result.Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public void Start_WhenPid1902CandidateHasNoVerifiedPhysicalIdentity_DoesNotCreateOrAcquireAnyDevice()
    {
        var descriptor = Device(0x0DB0, 0x1902, physicalIdentity: null);
        var enumerator = new FakeEnumerator([descriptor]);

        Assert.Equal(MsiClawInputStartStatus.Indeterminate, new MsiClawInputSource(enumerator).Start().Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public void Start_WhenSelectedDeviceReportsTooFewButtons_DoesNotCreateOrAcquireAnyDevice()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902, buttonCount: 16)]);

        Assert.Equal(MsiClawInputStartStatus.Indeterminate, new MsiClawInputSource(enumerator).Start().Status);
        Assert.Equal(0, enumerator.CreateCount);
    }

    [Fact]
    public void Start_WhenDirectInputInitializationFails_ReturnsInitializationFailed()
    {
        var source = new MsiClawInputSource(() => throw new InvalidOperationException("DirectInput unavailable"));

        Assert.Equal(MsiClawInputStartStatus.InitializationFailed, source.Start().Status);
    }

    [Fact]
    public async Task DirectInputEnumerator_IsCreatedLazily_WhenTheDiagnosticStarts()
    {
        var factoryCalls = 0;
        var source = new MsiClawInputSource(() =>
        {
            factoryCalls++;
            return new FakeEnumerator([Device(0x0DB0, 0x1902)], new FakeDevice(State()));
        });

        Assert.Equal(0, factoryCalls);
        Assert.True(source.Start().Started);
        await source.StopAsync();

        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Start_WhenEnumerationFails_ReturnsEnumerationFailed()
    {
        var enumerator = new FakeEnumerator([]) { EnumerationException = new InvalidOperationException("Enumeration failed") };
        var source = new MsiClawInputSource(enumerator);

        Assert.Equal(MsiClawInputStartStatus.EnumerationFailed, source.Start().Status);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public void Start_WhenEnumerationAndEnumeratorCleanupFail_PreservesTheEnumerationFailureResult()
    {
        var enumerator = new FakeEnumerator([])
        {
            EnumerationException = new InvalidOperationException("Enumeration failed"),
            DisposeException = new InvalidOperationException("Dispose failed")
        };
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.EnumerationFailed, result.Status);
        Assert.Equal(1, enumerator.DisposeCount);
    }

    [Fact]
    public void Start_WhenDeviceCreationFails_ReturnsCreateDeviceFailed()
    {
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)]) { CreateException = new InvalidOperationException("Create failed") };
        var source = new MsiClawInputSource(enumerator);

        Assert.Equal(MsiClawInputStartStatus.CreateDeviceFailed, source.Start().Status);
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

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.CreateDeviceFailed, result.Status);
        Assert.Equal(1, enumerator.DisposeCount);
    }

    [Fact]
    public void Start_WhenAcquireFails_CleansUpWithoutStartingPolling()
    {
        var device = new FakeDevice(State()) { AcquireException = new InvalidOperationException("Acquire failed") };
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);

        var result = source.Start();

        Assert.Equal(MsiClawInputStartStatus.AcquireFailed, result.Status);
        Assert.False(source.IsRunning);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
        Assert.True(enumerator.Disposed);
    }

    [Fact]
    public async Task FirstReadFailure_StopsAndCleansUpWithoutLeavingAnOrphanedSession()
    {
        var device = new FakeDevice(new InvalidOperationException("Read failed"));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.Start().Started);
        var summary = await summaryTask.WaitAsync(TimeSpan.FromSeconds(2));
        await source.StopAsync();

        Assert.False(source.IsRunning);
        Assert.Equal(MsiClawInputStopReason.ReadStateFailed, summary.StopReason);
        Assert.Equal(1, summary.ReadFailures);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task PollingReadFailure_StopsAndCleansUp()
    {
        var device = new FakeDevice(State(), State(), new InvalidOperationException("Read failed"));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.Start().Started);
        var summary = await summaryTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MsiClawInputStopReason.ReadStateFailed, summary.StopReason);
        Assert.Equal(1, summary.ReadFailures);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task ShortButtonArray_StopsDiagnosticInsteadOfPollingNeutralState()
    {
        var device = new FakeDevice(new DirectInputState(new bool[16]));
        var source = new MsiClawInputSource(new FakeEnumerator([Device(0x0DB0, 0x1902)], device));
        var summaryTask = ObserveSummary(source);

        Assert.True(source.Start().Started);
        var summary = await summaryTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MsiClawInputStopReason.InvalidButtonLayout, summary.StopReason);
        Assert.Equal(1, device.ReadCount);
        Assert.Equal(1, device.UnacquireCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_DoesNotCreateOrAcquireAnotherDevice()
    {
        var device = new FakeDevice(State());
        var enumerator = new FakeEnumerator([Device(0x0DB0, 0x1902)], device);
        var source = new MsiClawInputSource(enumerator);

        Assert.True(source.Start().Started);
        Assert.Equal(MsiClawInputStartStatus.AlreadyRunning, source.Start().Status);
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

        Assert.True(source.Start().Started);
        await changedStateObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await source.StopAsync();

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

        Assert.True(source.Start().Started);
        await expectedReadsObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(TimeSpan.FromSeconds(2));

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

        Assert.True(source.Start().Started);
        await source.StopAsync();
        var summary = await summaryTask.WaitAsync(TimeSpan.FromSeconds(2));

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

        Assert.True(source.Start().Started);
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

        Assert.True(source.Start().Started);
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

        Assert.True(source.Start().Started);
        await source.StopAsync();
        Assert.True(source.Start().Started);
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

    private static DirectInputDeviceDescriptor Device(ushort vendorId, ushort productId, string? physicalIdentity = "USB\\MSI_ROOT", int? buttonCount = 17, string? pnpInstanceId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test", vendorId, productId, "\\\\?\\hid#vid_0db0&pid_1902&mi_00&col01#test#{00000000-0000-0000-0000-000000000000}", pnpInstanceId ?? "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST", physicalIdentity, 0x0001, 0x0005, buttonCount, 6);
    private static DirectInputState State(params int[] pressedButtons)
    {
        var buttons = new bool[17];
        foreach (var button in pressedButtons) buttons[button] = true;
        return new DirectInputState(buttons);
    }

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
}
