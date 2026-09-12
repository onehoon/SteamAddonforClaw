using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawBatteryChargeLimitHardwareTests
{
    [Theory]
    [InlineData(0xD0, true, 80, true)]
    [InlineData(0x50, false, 80, true)]
    [InlineData(0xE4, true, 100, true)]
    [InlineData(0x64, false, 100, true)]
    [InlineData(0xD3, true, 83, false)]
    public void Decode_preserves_enable_bit_and_reports_product_value(byte raw, bool enabled, int percent, bool productValue)
    {
        var state = MsiClawBatteryChargeLimitHardware.Decode(raw);

        Assert.Equal(enabled, state.Enabled);
        Assert.Equal(percent, state.LimitPercent);
        Assert.Equal(raw, state.RawValue);
        Assert.Equal(productValue, state.IsProductValue);
    }

    [Theory]
    [InlineData(0xD0, 85, 0xD5)]
    [InlineData(0x50, 85, 0x55)]
    public void Set_percent_uses_read_modify_write_and_verifies(byte initial, int percent, byte expected)
    {
        var transport = new FakeTransport(initial);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(percent);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, transport.Writes.Single());
        Assert.Equal(expected, result.State!.Value.RawValue);
    }

    [Theory]
    [InlineData(0x50, true, 0xD0)]
    [InlineData(0xD0, false, 0x50)]
    public void Set_enabled_preserves_the_remembered_limit(byte initial, bool enabled, byte expected)
    {
        var transport = new FakeTransport(initial);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetEnabled(enabled);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, transport.Writes.Single());
        Assert.Equal(expected, result.State!.Value.RawValue);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(65)]
    [InlineData(70)]
    [InlineData(75)]
    [InlineData(80)]
    [InlineData(85)]
    [InlineData(90)]
    [InlineData(95)]
    [InlineData(100)]
    public void Every_product_value_is_accepted(int percent)
    {
        var transport = new FakeTransport(0x50);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(percent);

        Assert.True(result.Succeeded);
        Assert.Equal((byte)percent, transport.Writes.Single());
    }

    [Theory]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(99)]
    [InlineData(101)]
    public void Invalid_product_values_are_rejected_before_read_or_write(int percent)
    {
        var transport = new FakeTransport(0xD0);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(percent);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.InvalidTarget, result.Outcome);
        Assert.Empty(transport.Reads);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Read_failure_performs_no_write()
    {
        var transport = new FakeTransport(0xD0) { FailReads = true };
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(85);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.ReadFailed, result.Outcome);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Empty_read_payload_performs_no_write()
    {
        var transport = new FakeTransport(0xD0) { ReturnEmptyPayload = true };
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(85);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.ReadFailed, result.Outcome);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Set_data_failure_reports_write_failure()
    {
        var transport = new FakeTransport(0xD0) { FailWrites = true };
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(85);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.WriteFailed, result.Outcome);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public void Verification_read_failure_does_not_report_pre_write_state_as_current()
    {
        var transport = new FakeTransport(0xD0) { FailSecondRead = true };
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(85);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.VerificationFailed, result.Outcome);
        Assert.Null(result.State);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public void Readback_mismatch_reports_verification_failure()
    {
        var transport = new FakeTransport(0xD0) { PersistWrites = false };
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetPercent(85);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.VerificationFailed, result.Outcome);
        Assert.Equal((byte)0xD0, result.State!.Value.RawValue);
    }

    [Fact]
    public void Enable_rejects_an_out_of_product_range_remembered_value()
    {
        var transport = new FakeTransport(0xD3);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetEnabled(true);

        Assert.Equal(MsiBatteryChargeLimitMutationOutcome.InvalidTarget, result.Outcome);
        Assert.Equal(83, result.State!.Value.LimitPercent);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Disable_clears_enable_bit_while_preserving_an_out_of_range_value()
    {
        var transport = new FakeTransport(0xD3);
        var result = new MsiClawBatteryChargeLimitHardware(transport).SetEnabled(false);

        Assert.True(result.Succeeded);
        Assert.Equal((byte)0x53, transport.Writes.Single());
        Assert.Equal(83, result.State!.Value.LimitPercent);
    }

    private sealed class FakeTransport(byte initialValue) : IMsiClawTdpTransport
    {
        private byte _value = initialValue;
        private int _readCount;

        public List<int> Reads { get; } = [];
        public List<byte> Writes { get; } = [];
        public bool FailReads { get; init; }
        public bool ReturnEmptyPayload { get; init; }
        public bool FailSecondRead { get; init; }
        public bool FailWrites { get; init; }
        public bool PersistWrites { get; init; } = true;

        public bool TryGetAp(int index, out byte[] payload) { payload = []; return false; }
        public bool TrySetData(int block, byte value)
        {
            Writes.Add(value);
            if (FailWrites) return false;
            if (PersistWrites) _value = value;
            return true;
        }
        public bool TryGetData(int block, out byte[] payload)
        {
            Reads.Add(block);
            _readCount++;
            if (FailReads || FailSecondRead && _readCount > 1) { payload = []; return false; }
            payload = ReturnEmptyPayload ? [] : [_value];
            return true;
        }
    }
}
