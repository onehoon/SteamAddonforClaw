using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using System.Text.Json;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class TdpRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TdpRuntimeTests.{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_directory, "profiles.json");

    public TdpRuntimeTests()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    [Fact]
    public async Task EnabledValueEditLogsCommitWithoutRepeatedEnableTransition()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        await using var runtime = Create(new ProfileStore(PathName), new FakeTransport(), TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);
        await runtime.DrainAsync();
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("TDP configuration committed", log);
        Assert.DoesNotContain("TDP control enabled", log);
    }

    [Fact]
    public async Task UserCommitCompletionReportsPhysicalSuccessSeparately()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        var result = runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) });
        Assert.NotNull(result.Completion);
        var completion = await result.Completion!;
        await runtime.DrainAsync();

        Assert.True(completion.Attempted);
        Assert.True(completion.Succeeded);
        Assert.Equal(TdpPowerSource.AC, completion.Source);
        Assert.Equal((21, 31), (completion.Pl1Watts, completion.Pl2Watts));
    }

    [Fact]
    public async Task UserCommitCompletionReportsPhysicalFailureWhenGetApFails()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport { GetApSucceeds = false };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        var result = runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) });
        Assert.NotNull(result.Completion);
        var completion = await result.Completion!;
        await runtime.DrainAsync();

        Assert.True(completion.Attempted);
        Assert.False(completion.Succeeded);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task EnableTransitionIsLoggedEvenWhenPowerSourceIsUnknown()
    {
        Save(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        await using var runtime = Create(new ProfileStore(PathName), new FakeTransport(), null);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("TDP control enabled", log);
        Assert.Contains("Current power source is unknown", log);
    }

    [Fact]
    public async Task MissingOrDisabledTdpDoesNotApplyOrRewrite()
    {
        var store = new ProfileStore(PathName);
        var transport = new FakeTransport();
        await using var runtime = Create(store, transport, TdpPowerSource.AC);

        runtime.StartupReconcile();
        await runtime.DrainAsync();

        Assert.Empty(transport.Operations);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task FirstEnableSeedsCenterMManualValuesAndPreservesIndependentPairs()
    {
        var seed = new DeviceTdpSettings { Enabled = true, Ac = Pair(30, 8), Dc = Pair(30, 8) };
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, () => seed);

        var result = runtime.SetEnabled(true);
        await runtime.DrainAsync();

        Assert.True(result.Succeeded);
        var persisted = new ProfileStore(PathName).Load().Document.Device.Performance.Tdp;
        Assert.Equal(seed.Ac, persisted?.Ac);
        Assert.Equal(seed.Dc, persisted?.Dc);
        Assert.Contains("SetData(81,8)", transport.Operations);
    }

    [Fact]
    public async Task FirstEnableWithoutCenterMManualValuesDoesNotPersistOrApply()
    {
        var transport = new FakeTransport();
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, () => null);

        var result = runtime.SetEnabled(true);

        Assert.Equal(TdpCommitOutcome.InvalidTarget, result.Outcome);
        Assert.False(File.Exists(PathName));
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task CaptureDoesNotReadCenterMManualValues()
    {
        var reads = 0;
        await using var runtime = Create(new ProfileStore(PathName), new FakeTransport(), TdpPowerSource.AC,
            () => { reads++; return new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }; });

        _ = runtime.CaptureSnapshot();

        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task InitializedToggleDoesNotReadCenterMManualValues()
    {
        Save(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var reads = 0;
        await using var runtime = Create(new ProfileStore(PathName), new FakeTransport(), TdpPowerSource.AC,
            () => { reads++; return null; });

        Assert.True(runtime.SetEnabled(true).Succeeded);

        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task CaptureSnapshot_fails_closed_for_out_of_range_persisted_values()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(31, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport();
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        var snapshot = runtime.CaptureSnapshot();

        Assert.True(snapshot.Available);
        Assert.False(snapshot.PersistenceWritable);
        Assert.Null(snapshot.Configuration);
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task EnabledAcStartupQueuesThePersistedAcPair()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        runtime.StartupReconcile();
        await runtime.DrainAsync();

        Assert.Contains("SetData(81,30)", transport.Operations);
        Assert.DoesNotContain("SetData(81,20)", transport.Operations);
    }

    [Fact]
    public async Task UnknownPowerSourceDoesNotGuessOrApply()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport();
        await using var runtime = Create(new ProfileStore(PathName), transport, null);

        runtime.StartupReconcile();
        await runtime.DrainAsync();

        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task CommitPersistsBeforeQueuedApplyAndPreservesCpuBoost()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(PathName);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    CpuBoost = new() { Enabled = true, Ac = SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Enabled, Dc = SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Disabled }
                }
            }
        });
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        await using var runtime = Create(store, transport, TdpPowerSource.AC);

        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        var persisted = store.Load().Document.Device.Performance;
        Assert.Equal(20, persisted.Tdp?.Ac.Pl1Watts);
        Assert.NotNull(persisted.CpuBoost);
        await runtime.DrainAsync();
        Assert.Contains("SetData(81,30)", transport.Operations);
    }

    [Fact]
    public void CpuBoostMutationReloadsFreshDocumentAndPreservesTdp()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var store = new ProfileStore(PathName);
        var policy = new FakeCpuBoostPolicy();
        var cpuBoost = new CpuBoostRuntime(store, policy, new ProfileMutationGate());
        cpuBoost.StartupReconcile();

        Assert.True(cpuBoost.SetDeviceCpuBoostAc(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Aggressive).Succeeded);
        var loaded = store.Load().Document.Device.Performance;
        Assert.Equal(20, loaded.Tdp?.Ac.Pl1Watts);
        Assert.Equal(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Aggressive, loaded.CpuBoost?.Ac);
    }

    [Fact]
    public async Task InvalidCommitDoesNotChangeDocumentOrApply()
    {
        Save(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var before = File.ReadAllText(PathName);
        var transport = new FakeTransport();
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        Assert.Equal(TdpCommitOutcome.InvalidTarget, runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(31, 8), Dc = Pair(10, 20) }).Outcome);
        Assert.Equal(before, File.ReadAllText(PathName));
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task CommittedRequestsExecuteInFifoOrder()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);
        await runtime.DrainAsync();

        var first = transport.Operations.IndexOf("SetData(81,30)");
        var second = transport.Operations.IndexOf("SetData(81,31)");
        Assert.True(first >= 0 && second > first);
    }

    [Fact]
    public async Task DisableRevokesPendingWorkWithoutRestoreWrite()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4], BlockFirstApply = true };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        await transport.FirstApplyStarted.Task;
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = false, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);
        transport.ReleaseFirstApply.Set();
        await runtime.DrainAsync();

        Assert.DoesNotContain("SetData(81,31)", transport.Operations);
        Assert.DoesNotContain("SetData(81,0)", transport.Operations);
    }

    [Fact]
    public async Task RevokedQueuedUserCompletionResolvesAsNotAttempted()
    {
        Save(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) });
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4], BlockFirstApply = true };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);

        _ = runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) });
        await transport.FirstApplyStarted.Task;
        var queued = runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(22, 32), Dc = Pair(10, 20) });
        Assert.NotNull(queued.Completion);

        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = false, Ac = Pair(22, 32), Dc = Pair(10, 20) }).Succeeded);
        transport.ReleaseFirstApply.Set();

        var completion = await queued.Completion!;
        Assert.False(completion.Attempted);
        Assert.False(completion.Succeeded);
    }

    [Fact]
    public async Task FailedDisableLeavesCommittedPendingWorkValid()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4], BlockFirstApply = true };
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        await transport.FirstApplyStarted.Task;
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);

        File.Delete(PathName);
        Directory.CreateDirectory(PathName);
        Assert.Equal(TdpCommitOutcome.PersistenceFailed,
            runtime.CommitGlobalTdp(new() { Enabled = false, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Outcome);
        transport.ReleaseFirstApply.Set();
        await runtime.DrainAsync();

        Assert.Contains("SetData(81,31)", transport.Operations);
    }

    [Fact]
    public async Task ReenableAfterDisableDoesNotTrustThePreviousHardwareCache()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        await using var runtime = new TdpRuntime(new ProfileStore(PathName), new ProfileMutationGate(),
            new HandheldDeviceModelId("msi.claw.a2vm.7"), hardware, () => TdpPowerSource.AC);
        var pair = Pair(20, 30);

        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = pair, Dc = Pair(10, 20) }).Succeeded);
        await runtime.DrainAsync();
        transport.Operations.Clear();

        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = false, Ac = pair, Dc = Pair(10, 20) }).Succeeded);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = pair, Dc = Pair(10, 20) }).Succeeded);
        await runtime.DrainAsync();

        Assert.Equal(["GetAp(0)", "SetData(80,8)", "SetData(81,30)", "SetData(80,20)"], transport.Operations);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("Power-limit cache invalidated", log);
        Assert.Contains("Reason=TdpDisabled", log);
    }

    [Fact]
    public async Task CommitPreservesTdpExtensionDataAtAllLevels()
    {
        Directory.CreateDirectory(_directory);
        var unknown = JsonDocument.Parse("{\"future\":true}").RootElement.Clone();
        new ProfileStore(PathName).Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    Tdp = new DeviceTdpSettings
                    {
                        Enabled = false,
                        ExtensionData = new() { ["futureTdp"] = unknown },
                        Ac = new TdpPowerPair { Pl1Watts = 20, Pl2Watts = 30, ExtensionData = new() { ["futureAc"] = unknown } },
                        Dc = new TdpPowerPair { Pl1Watts = 10, Pl2Watts = 20, ExtensionData = new() { ["futureDc"] = unknown } }
                    }
                }
            }
        });

        await using var runtime = Create(new ProfileStore(PathName), new FakeTransport(), TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(11, 21) }).Succeeded);
        var persisted = new ProfileStore(PathName).Load().Document.Device.Performance.Tdp!;

        Assert.True(persisted.ExtensionData!.ContainsKey("futureTdp"));
        Assert.True(persisted.Ac.ExtensionData!.ContainsKey("futureAc"));
        Assert.True(persisted.Dc.ExtensionData!.ContainsKey("futureDc"));
    }

    [Fact]
    public async Task ShutdownStopsAdmissionAndDrainsCurrentTail()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4], BlockFirstApply = true };
        var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC);
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) }).Succeeded);
        await transport.FirstApplyStarted.Task;
        Assert.True(runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(21, 31), Dc = Pair(10, 20) }).Succeeded);
        runtime.BeginShutdown();
        transport.ReleaseFirstApply.Set();
        await runtime.DisposeAsync();
        Assert.DoesNotContain("SetData(81,31)", transport.Operations);
        Assert.Equal(TdpCommitOutcome.Unavailable, runtime.CommitGlobalTdp(new() { Enabled = true, Ac = Pair(22, 32), Dc = Pair(10, 20) }).Outcome);
    }

    [Fact]
    public async Task EnabledGameTdpOverridesDeviceEvenWhenDeviceIsDisabled()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        uint appId = 12345;
        SaveProfile(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) },
            new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(21, 31), Dc = Pair(11, 21) } } });
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, actualAppIdSource: () => appId);

        runtime.ReconcileCurrent(true, false, "GameStart");
        await runtime.DrainAsync();

        Assert.Contains("SetData(81,31)", transport.Operations);
    }

    [Fact]
    public async Task GameExitFallsBackToEnabledDeviceAndBothUnmanagedDoesNotWrite()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        uint appId = 12345;
        SaveProfile(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) },
            new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(21, 31), Dc = Pair(11, 21) } } });
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, actualAppIdSource: () => appId);

        runtime.ReconcileCurrent(true, false, "GameStart");
        await runtime.DrainAsync();
        transport.Operations.Clear();
        appId = 0;
        runtime.ReconcileCurrent(true, false, "GameExit");
        await runtime.DrainAsync();
        Assert.Contains("SetData(81,30)", transport.Operations);

        transport.Operations.Clear();
        SaveProfile(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) }, null);
        runtime.ReconcileCurrent(true, false, "Unmanaged");
        await runtime.DrainAsync();
        Assert.Empty(transport.Operations);
    }

    [Fact]
    public async Task InvalidEnabledGameTdpIsNotSentToHardware()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4] };
        SaveProfile(new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 30), Dc = Pair(10, 20) },
            new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(31, 8), Dc = Pair(10, 20) } } });
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, actualAppIdSource: () => 12345);

        runtime.ReconcileCurrent(true, false, "InvalidGame");
        await runtime.DrainAsync();

        Assert.DoesNotContain(transport.Operations, operation => operation.StartsWith("SetData(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GameExitRevokesQueuedGameTdpWhenDeviceIsDisabled()
    {
        var transport = new FakeTransport { Ap = [0x00, 0x00, 0xC4], BlockFirstApply = true };
        uint appId = 12345;
        SaveProfile(new DeviceTdpSettings { Enabled = false, Ac = Pair(20, 30), Dc = Pair(10, 20) },
            new GameProfile { Enabled = true, Performance = new GamePerformanceOverrides { CpuBoost = new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(21, 31), Dc = Pair(11, 21) } } });
        await using var runtime = Create(new ProfileStore(PathName), transport, TdpPowerSource.AC, actualAppIdSource: () => appId);

        runtime.ReconcileCurrent(true, false, "GameStart");
        await transport.FirstApplyStarted.Task;
        runtime.ReconcileCurrent(true, false, "GameRefresh");
        appId = 0;
        runtime.ReconcileCurrent(true, false, "GameExit");
        transport.ReleaseFirstApply.Set();
        await runtime.DrainAsync();

        Assert.Equal(1, transport.Operations.Count(operation => operation == "SetData(81,31)"));
    }

    private TdpRuntime Create(ProfileStore store, FakeTransport transport, TdpPowerSource? source, Func<DeviceTdpSettings?>? seed = null, Func<uint>? actualAppIdSource = null) =>
        new(store, new ProfileMutationGate(), new HandheldDeviceModelId("msi.claw.a2vm.7"), new MsiClawTdpHardware(transport), () => source, seed, actualAppIdSource);

    private void Save(DeviceTdpSettings tdp)
    {
        Directory.CreateDirectory(_directory);
        new ProfileStore(PathName).Save(new ProfileDocument { Device = new DeviceSettings { Performance = new DevicePerformanceSettings { Tdp = tdp } } });
    }

    private void SaveProfile(DeviceTdpSettings deviceTdp, GameProfile? game)
    {
        Directory.CreateDirectory(_directory);
        new ProfileStore(PathName).Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { Tdp = deviceTdp } },
            Games = game is null ? [] : new() { ["12345"] = game }
        });
    }

    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeTransport : IMsiClawTdpTransport
    {
        public List<string> Operations { get; } = [];
        public byte[] Ap { get; set; } = [0x00, 0x00, 0xC0];
        public bool GetApSucceeds { get; set; } = true;
        public bool BlockFirstApply { get; set; }
        public TaskCompletionSource FirstApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirstApply { get; } = new(false);
        private int _applyCount;

        public bool TryGetAp(int index, out byte[] payload) { Operations.Add($"GetAp({index})"); payload = Ap; return GetApSucceeds; }
        public bool TrySetData(int block, byte value)
        {
            Operations.Add($"SetData({block},{value})");
            if (BlockFirstApply && Interlocked.Increment(ref _applyCount) == 1)
            {
                FirstApplyStarted.SetResult();
                ReleaseFirstApply.Wait(TimeSpan.FromSeconds(5));
            }
            return true;
        }
    }

    private sealed class FakeCpuBoostPolicy : ICpuBoostPowerPolicy
    {
        public CpuBoostSystemState Read() => new(true, CpuBoostSideReading.Known(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Enabled), CpuBoostSideReading.Known(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode.Disabled), null);
        public CpuBoostApplyResult Apply(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode? ac, SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode? dc) => CpuBoostApplyResult.NoOp;
    }
}
