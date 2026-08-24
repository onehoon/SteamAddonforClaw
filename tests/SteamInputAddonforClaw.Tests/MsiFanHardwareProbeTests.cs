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
        Assert.False(TdpHelperProtocol.IsSupported("SetFan", 0)); Assert.True(TdpHelperProtocol.IsSupported("SetFan", 1));
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
    public void Rmw_preserves_unowned_bytes_and_restores() { var t = new FakeFanTransport(); var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.True(r.Succeeded); Assert.Contains(t.Writes, x => x.Block == 1 && x.Payload[0] == 70 && x.Payload[7] == 84); }

    [Fact]
    public void Partial_apply_does_not_enable_ownership_and_requests_recovery() { var t = new FakeFanTransport { FailFan2Write = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212 && (x.Payload[0] & 0x80) != 0); Assert.Contains(t.Writes, x => x.Block == 212); }

    [Fact]
    public void GetAp_failure_prevents_guessed_state_write() { var t = new FakeFanTransport { FailAp212 = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.DoesNotContain(t.Writes, x => x.Block == 212); }

    [Fact]
    public void Readback_mismatch_fails() { var t = new FakeFanTransport { DoNotPersistFanWrites = true }; var r = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); }

    [Fact]
    public void Restore_failure_is_reported_as_failure() { var t = new FakeFanTransport { FailRestoreWrite = true }; var r = NewProbe(t).RestoreAuto("EX", "MS-1T91", "test"); Assert.False(r.Succeeded); Assert.Contains("FAIL", File.ReadAllText(r.ReportPath!)); }

    [Fact]
    public void Unsupported_model_does_not_touch_transport() { var t = new FakeFanTransport(); var r = NewProbe(t).Capture("unknown", "unknown", "test"); Assert.False(r.Succeeded); Assert.Empty(t.Writes); Assert.Empty(t.Reads); }

    private static MsiFanHardwareProbe NewProbe(FakeFanTransport transport) => new(transport, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")));

    private sealed class FakeFanTransport : IMsiClawTdpTransport
    {
        internal readonly List<(int Block, byte[] Payload)> Writes = []; internal readonly List<string> Reads = [];
        internal bool FailTemperature; internal bool FailFan2Write; internal bool FailAp212; internal bool DoNotPersistFanWrites; internal bool FailRestoreWrite;
        private readonly Dictionary<int, byte[]> _fans = new() { [1] = [70, 0, 40, 49, 58, 67, 75, 84], [2] = [71, 0, 40, 49, 58, 67, 75, 85] };
        private byte _ownership;
        public bool TryGetAp(int index, out byte[] payload) { Reads.Add($"AP{index}"); if (index == 1 && FailAp212) { payload = []; return false; } payload = index == 1 ? [_ownership] : [0x00, 0x01]; return true; }
        public bool TrySetData(int block, byte value) { Writes.Add((block, [value])); if (block == 212 && FailRestoreWrite) return false; if (block == 212) _ownership = value; return true; }
        public bool TryGetFan(int block, out byte[] payload) { Reads.Add($"Fan{block}"); if (_fans.TryGetValue(block, out var value)) { payload = (byte[])value.Clone(); return true; } payload = []; return false; }
        public bool TrySetFan(int block, byte[] payload) { if (block == 2 && FailFan2Write) return false; Writes.Add((block, (byte[])payload.Clone())); if (!DoNotPersistFanWrites) _fans[block] = (byte[])payload.Clone(); return true; }
        public bool TryGetTemperature(int index, out byte[] payload) { Reads.Add($"Temp{index}"); payload = FailTemperature ? [] : [47, 50, 57, 64, 71, 78]; return !FailTemperature; }
        public bool TryGetData(int block, out byte[] payload) { Reads.Add($"Data{block}"); payload = [0]; return true; }
    }
}
