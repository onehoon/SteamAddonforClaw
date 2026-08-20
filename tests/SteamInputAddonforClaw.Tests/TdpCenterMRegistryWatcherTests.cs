using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class TdpCenterMRegistryWatcherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TdpCenterMRegistryWatcherTests.{Guid.NewGuid():N}");
    private string ProfilePath => Path.Combine(_directory, "profiles.json");

    [Fact]
    public void ProductionQueriesUseTheExactConfirmedUserScenarioValues()
    {
        Assert.Equal(@"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\User Scenario", WindowsTdpCenterMRegistryEventSource.KeyPath);
        foreach (var value in new[] { "Mode", "ManualPL1AC", "ManualPL2AC", "ManualPL1DC", "ManualPL2DC" })
        {
            var query = WindowsTdpCenterMRegistryEventSource.BuildQuery(value);
            Assert.Contains($"ValueName = '{value}'", query);
            Assert.Contains("Hive = 'HKEY_LOCAL_MACHINE'", query);
        }
    }

    [Fact]
    public async Task CenterMEventForcesFullReassertEvenWhenPairIsUnchanged()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(20, 30) });
        var delay = new FakeDelay(); var power = new FakePowerSource();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => power.Current);
        using var lifecycle = new TdpPowerLifecycleWatcher(runtime, power, delay.WaitAsync);
        var center = new FakeRegistrySource("Mode");
        using var watcher = new TdpCenterMRegistryWatcher(lifecycle.ScheduleCenterMReconcile, [center]);

        lifecycle.ScheduleStartup(); delay.Release(); await lifecycle.DrainPendingAsync(); await runtime.DrainAsync();
        transport.Operations.Clear();
        watcher.Start(); delay.Reset(); center.Raise(); await Task.Yield(); delay.Release();
        await lifecycle.DrainPendingAsync(); await runtime.DrainAsync();

        Assert.Equal(["GetAp(0)", "SetData(80,8)", "SetData(81,30)", "SetData(80,20)"], transport.Operations);
    }

    [Fact]
    public async Task CenterMBurstCoalescesBeforeTheExistingFifo()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(20, 30) });
        var delay = new FakeDelay(); var power = new FakePowerSource(); var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => power.Current);
        using var lifecycle = new TdpPowerLifecycleWatcher(runtime, power, delay.WaitAsync);
        var mode = new FakeRegistrySource("Mode"); var pl1 = new FakeRegistrySource("ManualPL1AC"); var pl2 = new FakeRegistrySource("ManualPL2AC");
        using var watcher = new TdpCenterMRegistryWatcher(lifecycle.ScheduleCenterMReconcile, [mode, pl1, pl2]);
        lifecycle.ScheduleStartup(); delay.Release(); await lifecycle.DrainPendingAsync(); await runtime.DrainAsync(); transport.Operations.Clear();

        watcher.Start(); delay.Reset(); mode.Raise(); pl1.Raise(); pl2.Raise(); await Task.Yield(); delay.Release();
        await lifecycle.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Equal(1, transport.Operations.Count(x => x == "GetAp(0)"));
    }

    [Fact]
    public async Task DisabledTdpIsPassiveAndOneFailedValueDoesNotDisableOthers()
    {
        Save(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(20, 30) });
        var delay = new FakeDelay(); var power = new FakePowerSource(); var transport = new FakeTransport();
        await using var runtime = CreateRuntime(transport, () => power.Current);
        using var lifecycle = new TdpPowerLifecycleWatcher(runtime, power, delay.WaitAsync);
        var mode = new FakeRegistrySource("Mode"); var ai = new FakeRegistrySource("AIModeM", false);
        using var watcher = new TdpCenterMRegistryWatcher(lifecycle.ScheduleCenterMReconcile, [mode, ai]);

        Assert.True(watcher.Start());
        delay.Reset(); mode.Raise(); await Task.Yield(); delay.Release(); await lifecycle.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Empty(transport.Operations);
        watcher.Dispose(); Assert.Equal(1, mode.DisposeCount); Assert.Equal(1, ai.DisposeCount);
    }

    private TdpRuntime CreateRuntime(FakeTransport transport, Func<TdpPowerSource?> source) =>
        new(new ProfileStore(ProfilePath), new ProfileMutationGate(), new HandheldDeviceModelId("msi.claw.a2vm.7"), new MsiClawTdpHardware(transport), source);
    private void Save(DeviceTdpSettings tdp)
    {
        Directory.CreateDirectory(_directory);
        new ProfileStore(ProfilePath).Save(new ProfileDocument { Device = new DeviceSettings { Performance = new DevicePerformanceSettings { Tdp = tdp } } });
    }
    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeRegistrySource(string valueName, bool startSucceeds = true) : ITdpCenterMRegistryEventSource
    {
        public string ValueName { get; } = valueName;
        public event Action? Changed;
        public int DisposeCount { get; private set; }
        public bool TryStart(out Exception? error) { error = startSucceeds ? null : new InvalidOperationException("Unavailable"); return startSucceeds; }
        public void Raise() => Changed?.Invoke();
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakePowerSource : ITdpPowerNotificationSource
    {
        public TdpPowerSource? Current { get; set; } = TdpPowerSource.AC;
        public event Action<TdpPowerNotification>? Notification { add { } remove { } }
        public bool TryRegister(out int nativeError) { nativeError = 0; return true; }
        public void Dispose() { }
    }

    private sealed class FakeDelay
    {
        private TaskCompletionSource _release = NewSource();
        public Task WaitAsync(TimeSpan _, CancellationToken token) => _release.Task.WaitAsync(token);
        public void Release() => _release.TrySetResult();
        public void Reset() => _release = NewSource();
        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeTransport : IMsiClawTdpTransport
    {
        public byte[] Ap { get; set; } = [0, 0, 0xC0];
        public List<string> Operations { get; } = [];
        public bool TryGetAp(int index, out byte[] payload) { Operations.Add($"GetAp({index})"); payload = Ap; return true; }
        public bool TrySetData(int block, byte value) { Operations.Add($"SetData({block},{value})"); return true; }
    }
}
