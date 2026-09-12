using System.Text.Json;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawBatteryChargeLimitRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.BatteryRuntime.{Guid.NewGuid():N}");

    [Fact]
    public void Startup_bootstraps_a_product_observation_without_hardware_write()
    {
        var transport = new FakeTransport(0xD0);
        var runtime = CreateRuntime(transport);

        runtime.StartupReconcile();

        Assert.Empty(transport.Writes);
        var persisted = new ProfileStore(Path.Combine(_directory, "profiles.json")).Load().Document.Device.Battery.ChargeLimit;
        Assert.Equal(new DeviceBatteryChargeLimitSettings { Enabled = true, LimitPercent = 80 }, persisted);
        Assert.True(runtime.Snapshot.Initialized);
        Assert.Equal(80, runtime.Snapshot.DesiredLimitPercent);
    }

    [Fact]
    public void Startup_keeps_an_out_of_range_observation_unmanaged()
    {
        var transport = new FakeTransport(0xD3);
        var runtime = CreateRuntime(transport);

        runtime.StartupReconcile();

        Assert.Empty(transport.Writes);
        Assert.Null(new ProfileStore(Path.Combine(_directory, "profiles.json")).Load().Document.Device.Battery.ChargeLimit);
        Assert.False(runtime.Snapshot.Initialized);
        Assert.Equal(83, runtime.Snapshot.CurrentLimitPercent);
    }

    [Fact]
    public void Startup_bootstrap_reloads_before_save_and_preserves_a_concurrent_profile_edit()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(Path.Combine(_directory, "profiles.json"));
        store.Save(new ProfileDocument());
        var transport = new FakeTransport(0xD0);
        transport.OnFirstRead = () => store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Display = new DeviceDisplaySettings
                {
                    ExtensionData = new() { ["concurrentDisplayEdit"] = JsonDocument.Parse("true").RootElement.Clone() }
                }
            }
        });
        var runtime = new MsiClawBatteryChargeLimitRuntime(store, new ProfileMutationGate(), MsiClawDeviceModels.Claw8ExAiPlus.Id,
            new MsiClawBatteryChargeLimitHardware(transport));

        runtime.StartupReconcile();

        var document = store.Load().Document;
        Assert.Equal(80, document.Device.Battery.ChargeLimit!.LimitPercent);
        Assert.True(document.Device.Display.ExtensionData!["concurrentDisplayEdit"].GetBoolean());
    }

    [Fact]
    public void Disabled_reconcile_applies_enabled_bit_before_limit()
    {
        var transport = new FakeTransport(0xD0);
        var runtime = CreateRuntime(transport, new DeviceBatteryChargeLimitSettings { Enabled = false, LimitPercent = 85 });

        runtime.Reconcile("Test");

        Assert.Equal([0x50, 0x55], transport.Writes);
        Assert.True(runtime.Snapshot.Initialized);
        Assert.False(runtime.Snapshot.CurrentEnabled);
        Assert.Equal(85, runtime.Snapshot.CurrentLimitPercent);
    }

    [Fact]
    public void Enabled_reconcile_applies_limit_before_enabled_bit()
    {
        var transport = new FakeTransport(0x50);
        var runtime = CreateRuntime(transport, new DeviceBatteryChargeLimitSettings { Enabled = true, LimitPercent = 85 });

        runtime.Reconcile("Test");

        Assert.Equal([0x55, 0xD5], transport.Writes);
        Assert.True(runtime.Snapshot.CurrentEnabled);
        Assert.Equal(85, runtime.Snapshot.CurrentLimitPercent);
    }

    [Fact]
    public void Read_failure_after_persistence_does_not_write_hardware()
    {
        var transport = new FakeTransport(0xD0) { FailReads = true };
        var runtime = CreateRuntime(transport, new DeviceBatteryChargeLimitSettings { Enabled = true, LimitPercent = 80 });

        var result = runtime.SetPercent(85);

        Assert.Equal(BatteryChargeLimitMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Empty(transport.Writes);
        Assert.Equal(85, new ProfileStore(Path.Combine(_directory, "profiles.json")).Load().Document.Device.Battery.ChargeLimit!.LimitPercent);
    }

    [Fact]
    public void Invalid_target_is_rejected_without_profile_or_hardware_change()
    {
        var transport = new FakeTransport(0xD0);
        var runtime = CreateRuntime(transport, new DeviceBatteryChargeLimitSettings { Enabled = true, LimitPercent = 80 });

        var result = runtime.SetPercent(83);

        Assert.Equal(BatteryChargeLimitMutationOutcome.InvalidTarget, result.Outcome);
        Assert.Empty(transport.Writes);
        Assert.Equal(80, new ProfileStore(Path.Combine(_directory, "profiles.json")).Load().Document.Device.Battery.ChargeLimit!.LimitPercent);
    }

    private MsiClawBatteryChargeLimitRuntime CreateRuntime(FakeTransport transport, DeviceBatteryChargeLimitSettings? desired = null)
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(Path.Combine(_directory, "profiles.json"));
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Battery = new DeviceBatterySettings { ChargeLimit = desired } }
        });
        return new(store, new ProfileMutationGate(), MsiClawDeviceModels.Claw8ExAiPlus.Id,
            new MsiClawBatteryChargeLimitHardware(transport));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeTransport(byte initialValue) : IMsiClawTdpTransport
    {
        private byte _value = initialValue;
        private bool _firstRead = true;
        public List<byte> Writes { get; } = [];
        public bool FailReads { get; init; }
        public Action? OnFirstRead { get; set; }
        public bool TryGetAp(int index, out byte[] payload) { payload = []; return false; }
        public bool TrySetData(int block, byte value) { Writes.Add(value); _value = value; return !FailReads; }
        public bool TryGetData(int block, out byte[] payload)
        {
            if (FailReads) { payload = []; return false; }
            if (_firstRead)
            {
                _firstRead = false;
                OnFirstRead?.Invoke();
            }
            payload = [_value];
            return true;
        }
    }
}
