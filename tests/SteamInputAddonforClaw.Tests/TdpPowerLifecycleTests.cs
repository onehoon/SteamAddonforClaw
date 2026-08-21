using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class TdpPowerLifecycleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TdpPowerLifecycleTests.{Guid.NewGuid():N}");
    private string ProfilePath => Path.Combine(_directory, "profiles.json");

    public TdpPowerLifecycleTests()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    [Fact]
    public async Task CoalescedLifecycleReasonsRemainVisibleInSettledTrace()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource();
        var delay = new FakeDelay();
        await using var runtime = CreateRuntime(new FakeTransport { Ap = [0, 0, 0xC4] }, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);

        watcher.ScheduleCenterMReconcile();
        watcher.Observe(TdpPowerNotification.PowerSourceChanged);
        delay.Release();
        await watcher.DrainPendingAsync();
        await runtime.DrainAsync();
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("Reason=CenterM+PowerSourceChanged", log);
        Assert.Contains("Invalidate=True", log);
    }

    [Fact]
    public async Task StartupIsSettledAndInitialPowerCallbackIsCoalesced()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource();
        var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);

        watcher.ScheduleStartup();
        Assert.Empty(transport.Operations);
        watcher.Observe(TdpPowerNotification.PowerSourceChanged);
        await Task.Yield();
        delay.Release();
        await watcher.DrainPendingAsync();
        await runtime.DrainAsync();

        Assert.Equal(1, transport.Operations.Count(x => x == "GetAp(0)"));
    }

    [Fact]
    public async Task AcDcBoundarySelectsCurrentRailWithoutPersistence()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource { Current = TdpPowerSource.AC };
        var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => source.Current);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup();
        await Task.Yield();
        delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        var before = File.ReadAllText(ProfilePath);
        transport.Operations.Clear();
        source.Current = TdpPowerSource.DC;
        delay.Reset(); watcher.Observe(TdpPowerNotification.PowerSourceChanged); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();

        Assert.Contains("SetData(81,20)", transport.Operations);
        Assert.Equal(before, File.ReadAllText(ProfilePath));
    }

    [Fact]
    public async Task AcDcBoundaryInvalidatesCacheEvenWhenThePairIsUnchanged()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(20, 30) });
        var source = new FakeSource { Current = TdpPowerSource.AC }; var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => source.Current);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); await Task.Yield(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        transport.Operations.Clear(); source.Current = TdpPowerSource.DC;

        delay.Reset(); watcher.Observe(TdpPowerNotification.PowerSourceChanged); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();

        Assert.Equal(["GetAp(0)", "SetData(80,8)", "SetData(81,30)", "SetData(80,20)"], transport.Operations);
    }

    [Fact]
    public async Task SameSourceNotificationIsDedupedAndRapidChurnUsesFinalSource()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource { Current = TdpPowerSource.AC };
        var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => source.Current);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); await Task.Yield(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        transport.Operations.Clear();

        delay.Reset(); watcher.Observe(TdpPowerNotification.PowerSourceChanged); source.Current = TdpPowerSource.DC;
        watcher.Observe(TdpPowerNotification.PowerSourceChanged); source.Current = TdpPowerSource.AC;
        delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();

        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task ResumeForcesFullReapplyAndDuplicateResumeIsIgnored()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource(); var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync(); transport.Operations.Clear();

        watcher.Observe(TdpPowerNotification.Suspend);
        delay.Reset(); watcher.Observe(TdpPowerNotification.ResumeAutomatic); watcher.Observe(TdpPowerNotification.ResumeSuspend); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();

        Assert.Equal(["GetAp(0)", "SetData(80,8)", "SetData(81,30)", "SetData(80,20)"], transport.Operations);
    }

    [Fact]
    public async Task ResumeWithoutSuspendIsAcceptedOnceUntilNextSuspend()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource(); var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.Observe(TdpPowerNotification.ResumeAutomatic); watcher.Observe(TdpPowerNotification.ResumeSuspend); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Equal(1, transport.Operations.Count(x => x == "GetAp(0)"));
    }

    [Fact]
    public async Task DisabledTdpAndShutdownDuringSettleDoNotWriteHardware()
    {
        Save(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource(); var delay = new FakeDelay(); var transport = new FakeTransport();
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); watcher.Dispose(); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task RegistrationFailureDoesNotPreventStartupSettle()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource { RegisterSucceeds = false }; var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);

        Assert.False(watcher.Start());
        watcher.ScheduleStartup(); await Task.Yield(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Contains("GetAp(0)", transport.Operations);
    }

    [Fact]
    public async Task FailedLifecycleApplyRemainsRetryableOnSameSource()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource(); var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => source.Current);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        transport.Operations.Clear(); transport.FailWrites = true;
        runtime.ReconcileCurrent(true, true, "ForcedLifecycle"); await runtime.DrainAsync();
        var firstAttemptCount = transport.Operations.Count(x => x == "GetAp(0)");
        transport.FailWrites = false;
        runtime.ReconcileCurrent(false, false, "PowerSourceChanged"); await runtime.DrainAsync();

        Assert.True(transport.Operations.Count(x => x == "GetAp(0)") > firstAttemptCount);
    }

    [Fact]
    public void NativePowerSettingImportsRemainBoundToPowrProf()
    {
        foreach (var methodName in new[] { "PowerSettingRegisterNotification", "PowerSettingUnregisterNotification" })
        {
            var method = typeof(WindowsTdpPowerNotificationSource).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            var import = method?.GetCustomAttribute<DllImportAttribute>();
            Assert.NotNull(method);
            Assert.NotNull(import);
            Assert.Equal("powrprof.dll", import!.Value);
            Assert.Equal(typeof(uint), method!.ReturnType);
        }
    }

    [Fact]
    public async Task OlderApplySuccessCannotClearNewerUnknownSourceDirtyState()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        TdpPowerSource? source = TdpPowerSource.AC;
        var transport = new FakeTransport { Ap = [0, 0, 0xC4], BlockFirstApply = true };
        await using var runtime = CreateRuntime(transport, () => source);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        await transport.FirstApplyStarted.Task;

        source = null;
        runtime.ReconcileCurrent(false, false, "PowerSourceChanged");
        transport.ReleaseFirstApply.Set();
        await runtime.DrainAsync();

        source = TdpPowerSource.AC;
        runtime.ReconcileCurrent(false, false, "PowerSourceChanged");
        await runtime.DrainAsync();
        Assert.True(transport.Operations.Count(x => x == "GetAp(0)") >= 2);
    }

    [Fact]
    public async Task RuntimeShutdownBarrierRejectsPendingLifecycleAdmission()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource(); var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, TdpPowerSource.AC);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup();
        runtime.BeginShutdown();
        delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task ResumeUnknownSourceRetainsCacheInvalidationForLaterRetry()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var source = new FakeSource { Current = TdpPowerSource.AC }; var delay = new FakeDelay();
        var transport = new FakeTransport { Ap = [0, 0, 0xC4] };
        await using var runtime = CreateRuntime(transport, () => source.Current);
        using var watcher = CreateWatcher(runtime, source, delay);
        watcher.ScheduleStartup(); delay.Release(); await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        transport.Operations.Clear();

        watcher.Observe(TdpPowerNotification.Suspend);
        source.Current = null; delay.Reset(); watcher.Observe(TdpPowerNotification.ResumeAutomatic); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Empty(transport.Operations);

        source.Current = TdpPowerSource.AC; delay.Reset(); watcher.Observe(TdpPowerNotification.PowerSourceChanged); delay.Release();
        await watcher.DrainPendingAsync(); await runtime.DrainAsync();
        Assert.Equal(["GetAp(0)", "SetData(80,8)", "SetData(81,30)", "SetData(80,20)"], transport.Operations);
    }

    private TdpRuntime CreateRuntime(FakeTransport transport, TdpPowerSource source) => CreateRuntime(transport, () => source);
    private TdpRuntime CreateRuntime(FakeTransport transport, Func<TdpPowerSource?> source) =>
        new(new ProfileStore(ProfilePath), new ProfileMutationGate(), new HandheldDeviceModelId("msi.claw.a2vm.7"), new MsiClawTdpHardware(transport), source);
    private static TdpPowerLifecycleWatcher CreateWatcher(TdpRuntime runtime, FakeSource source, FakeDelay delay) =>
        new(runtime, source, delay.WaitAsync);
    private void Save(DeviceTdpSettings tdp)
    {
        Directory.CreateDirectory(_directory);
        new ProfileStore(ProfilePath).Save(new ProfileDocument { Device = new DeviceSettings { Performance = new DevicePerformanceSettings { Tdp = tdp } } });
    }
    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };
    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeSource : ITdpPowerNotificationSource
    {
        public TdpPowerSource? Current { get; set; } = TdpPowerSource.AC;
        public bool RegisterSucceeds { get; set; } = true;
        public event Action<TdpPowerNotification>? Notification;
        public bool TryRegister(out int nativeError) { nativeError = RegisterSucceeds ? 0 : 5; return RegisterSucceeds; }
        public void Emit(TdpPowerNotification notification) => Notification?.Invoke(notification);
        public void Dispose() { }
    }

    private sealed class FakeDelay
    {
        private TaskCompletionSource _release = NewSource();
        public Task WaitAsync(TimeSpan _, CancellationToken cancellationToken) => _release.Task.WaitAsync(cancellationToken);
        public void Release() => _release.TrySetResult();
        public void Reset() => _release = NewSource();
        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeTransport : IMsiClawTdpTransport
    {
        public byte[] Ap { get; set; } = [0, 0, 0xC0];
        public bool FailWrites { get; set; }
        public bool BlockFirstApply { get; set; }
        public TaskCompletionSource FirstApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirstApply { get; } = new(false);
        public List<string> Operations { get; } = [];
        public bool TryGetAp(int index, out byte[] payload) { Operations.Add($"GetAp({index})"); payload = Ap; return true; }
        public bool TrySetData(int block, byte value)
        {
            Operations.Add($"SetData({block},{value})");
            if (BlockFirstApply && FirstApplyStarted.Task.Status == TaskStatus.WaitingForActivation)
            {
                FirstApplyStarted.TrySetResult();
                ReleaseFirstApply.Wait(TimeSpan.FromSeconds(5));
            }
            return !FailWrites;
        }
    }
}
