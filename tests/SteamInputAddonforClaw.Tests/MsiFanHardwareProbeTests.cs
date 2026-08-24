using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.TdpHelper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiFanHardwareProbeTests
{
    [Fact]
    public void SetFan_package_wraps_payload_in_the_32_byte_WMI_envelope()
    {
        var payload = new byte[] { 70, 0, 40, 49, 58, 67, 75, 84 };
        var package = TdpHelperProtocol.BuildPackage(1, 0, payload);
        Assert.Equal(32, package.Length); Assert.Equal(1, package[0]); Assert.Equal(payload, package[1..9]); Assert.All(package[9..], value => Assert.Equal(0, value));
        Assert.False(TdpHelperProtocol.IsSupported("SetFan", 0)); Assert.True(TdpHelperProtocol.IsSupported("SetFan", 1)); Assert.True(TdpHelperProtocol.IsSupported("SetData", 152));
    }

    [Fact]
    public void Thirty_one_byte_raw_fan_response_is_normalized_to_the_first_eight_bytes()
    {
        var raw = new byte[] { 58, 70, 74, 76, 78, 80, 84, 94 }.Concat(new byte[23]).ToArray();
        Assert.True(FanProbeLogic.TryNormalizeLogicalFanBlock(raw, out var logical));
        Assert.Equal(8, logical.Length);
        Assert.Equal(new byte[] { 58, 70, 74, 76, 78, 80, 84, 94 }, logical);
        Assert.False(FanProbeLogic.TryNormalizeLogicalFanBlock(new byte[7], out _));
    }

    [Fact]
    public void Automatic_test_never_sends_trailing_raw_response_bytes_to_SetFan()
    {
        var transport = new FakeFanTransport { RawFanResponse = true };
        _ = NewProbe(transport).AutomaticTest("EX", "MS-1T91", "test");
        Assert.Contains(transport.Writes, x => x.Block is 1 or 2);
        Assert.All(transport.Writes.Where(x => x.Block is 1 or 2), write => Assert.Equal(8, write.Payload.Length));
    }

    [Theory]
    [InlineData(new byte[] { 70, 74, 76, 78, 80, 84 }, 1, 75)]
    [InlineData(new byte[] { 0, 40, 49, 58, 67, 75 }, 4, 68)]
    public void Selects_a_safe_middle_duty_increment(byte[] duties, int expectedIndex, byte expectedNext)
    {
        Assert.True(FanProbeLogic.TrySelectSafeIncrement(duties, out var index, out var next));
        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedNext, next);
    }

    [Theory]
    [InlineData("MS-1T42", "A2vm")]
    [InlineData("MS-1T52", "A2vm")]
    [InlineData("MS-1T91", "Cg3em")]
    [InlineData("unknown", "Unsupported")]
    public void Maps_authoritative_boards(string board, string expected) => Assert.Equal(expected, FanProbeModelMap.Resolve(board).ToString());

    [Fact]
    public void Capture_is_read_only_and_writes_hex_and_decimal()
    {
        var transport = new FakeFanTransport(); var probe = NewProbe(transport);
        var result = probe.Capture("EX", "MS-1T91", "test");
        Assert.True(result.Succeeded); Assert.Empty(transport.Writes);
        var report = File.ReadAllText(result.ReportPath!); Assert.Contains("HEX:", report); Assert.Contains("DEC:", report);
    }

    [Fact]
    public void Required_capture_read_failure_does_not_write() { var t = new FakeFanTransport { FailTemperature = true }; var r = NewProbe(t).Capture("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.Empty(t.Writes); }

    [Fact]
    public void Required_fan_capture_read_failure_does_not_report_success() { var t = new FakeFanTransport { FailFan1Read = true }; var r = NewProbe(t).Capture("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.Empty(t.Writes); }

    [Fact]
    public void Rmw_preserves_unowned_bytes_and_restores() { var t = new FakeFanTransport(); var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.True(r.Succeeded); Assert.Contains(t.Writes, x => x.Block == 1 && x.Payload[0] == 70 && x.Payload[7] == 84); }

    [Fact]
    public void Partial_apply_does_not_enable_ownership_and_requests_recovery() { var t = new FakeFanTransport { FailFan2Write = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212 && (x.Payload[0] & 0x80) != 0); Assert.Contains(t.Writes, x => x.Block == 212); }

    [Fact]
    public void GetAp_failure_prevents_guessed_state_write() { var t = new FakeFanTransport { FailAp212 = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212); }

    [Fact]
    public void Readback_mismatch_fails() { var t = new FakeFanTransport { DoNotPersistFanWrites = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); }

    [Fact]
    public void Restore_does_not_blind_write_when_ownership_read_fails_after_writes() { var t = new FakeFanTransport { FailApOnRestore = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212); }

    [Fact]
    public void Duty_above_conservative_limit_is_skipped_without_downward_clamp() { var t = new FakeFanTransport { HighDuty = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 1 && x.Payload.Length > 2 && x.Payload[2] < 100); }

    [Fact]
    public void Divergent_curves_do_not_enable_custom_ownership() { var t = new FakeFanTransport { DivergentFan2 = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212 && x.Payload.Length > 0 && (x.Payload[0] & 0x80) != 0); }

    [Fact]
    public void Coupled_fan_write_fails_closed_and_does_not_enable_ownership() { var t = new FakeFanTransport { CoupleFanWrites = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212 && x.Payload.Length > 0 && (x.Payload[0] & 0x80) != 0); }

    [Fact]
    public void Restore_failure_is_reported_as_failure() { var t = new FakeFanTransport { FailRestoreWrite = true }; var r = NewProbe(t).RestoreAuto("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.Contains("FAIL", File.ReadAllText(r.ReportPath!)); }

    [Fact]
    public void Unsupported_model_does_not_touch_transport() { var t = new FakeFanTransport(); var r = NewProbe(t).Capture("unknown", "unknown", "test"); Assert.False(r.Succeeded); Assert.Empty(t.Writes); Assert.Empty(t.Reads); }

    private static MsiFanHardwareProbe NewProbe(FakeFanTransport transport) => new(transport, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")));

    private sealed class FakeFanTransport : IMsiClawTdpTransport
    {
        internal readonly List<(int Block, byte[] Payload)> Writes = []; internal readonly List<string> Reads = [];
        internal bool FailTemperature; internal bool FailFan1Read; internal bool FailFan2Write; internal bool FailAp212; internal bool DoNotPersistFanWrites; internal bool FailRestoreWrite; internal bool FailApOnRestore; internal bool HighDuty; internal bool DivergentFan2; internal bool CoupleFanWrites; internal bool RawFanResponse;
        private readonly Dictionary<int, byte[]> _fans = new() { [1] = [70, 0, 40, 49, 58, 67, 75, 84], [2] = [71, 0, 40, 49, 58, 67, 75, 85] };
        private byte _ownership;
        public bool TryGetAp(int index, out byte[] payload) { Reads.Add($"AP{index}"); if (index == 1 && (FailAp212 || (FailApOnRestore && Writes.Any(x => x.Block == 1 || x.Block == 2)))) { payload = []; return false; } payload = index == 1 ? [_ownership] : [0x00, 0x01]; return true; }
        public bool TrySetData(int block, byte value) { Writes.Add((block, [value])); if (block == 212 && FailRestoreWrite) return false; if (block == 212) _ownership = value; return true; }
        public bool TryGetFan(int block, out byte[] payload) { Reads.Add($"Fan{block}"); if (block == 1 && FailFan1Read) { payload = []; return false; } if (_fans.TryGetValue(block, out var value)) { payload = (byte[])value.Clone(); if (block == 1 && HighDuty) payload[2] = 100; if (block == 2 && DivergentFan2) payload[3] = 50; if (RawFanResponse) payload = payload.Concat(new byte[23]).ToArray(); return true; } payload = []; return false; }
        public bool TrySetFan(int block, byte[] payload) { if (block == 2 && FailFan2Write) return false; Writes.Add((block, (byte[])payload.Clone())); if (!DoNotPersistFanWrites) _fans[block] = (byte[])payload.Clone(); if (CoupleFanWrites) { var other = block == 1 ? 2 : 1; var coupled = (byte[])_fans[other].Clone(); coupled[2] = (byte)(coupled[2] + 1); _fans[other] = coupled; } return true; }
        public bool TryGetTemperature(int index, out byte[] payload) { Reads.Add($"Temp{index}"); payload = FailTemperature ? [] : [47, 50, 57, 64, 71, 78]; return !FailTemperature; }
        public bool TryGetThermal(int index, out byte[] payload) { Reads.Add($"Thermal{index}"); payload = [44, 54, 64, 74, 82, 82]; return true; }
        public bool TryGetData(int block, out byte[] payload) { Reads.Add($"Data{block}"); payload = [0]; return true; }
    }
}
