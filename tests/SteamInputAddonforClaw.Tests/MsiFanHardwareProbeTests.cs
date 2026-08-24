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
    public void SetData_diagnostic_request_uses_value_field_without_fan_payload()
    {
        var request = TdpHelperClient.EncodeDiagnosticRequest("SetData", 212, [0x80]);
        var package = TdpHelperProtocol.BuildPackage(212, request.Value, request.EncodedPayload is null ? null : Convert.FromBase64String(request.EncodedPayload));

        Assert.Equal(0x80, request.Value);
        Assert.Null(request.EncodedPayload);
        Assert.Equal(32, package.Length);
        Assert.Equal(212, package[0]);
        Assert.Equal(0x80, package[1]);
        Assert.All(package[2..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Wmi_version_decoder_uses_source_backed_major_and_minor_positions()
    {
        var decoded = TdpHelperClient.DecodeWmiVersionPayload([1, 3, 7]);

        Assert.Equal((byte?)3, decoded.Major);
        Assert.Equal((byte?)7, decoded.Minor);
        Assert.True(TdpHelperProtocol.IsSupported("GetWmiVersion", 1));
        Assert.False(TdpHelperProtocol.IsSupported("GetWmiVersion", 0));
    }

    [Theory]
    [InlineData(true, true, true, "IMMEDIATE_MATCH")]
    [InlineData(false, true, true, "DELAYED_MATCH")]
    [InlineData(false, false, true, "MISMATCH")]
    [InlineData(false, false, false, "READ_FAILED")]
    public void Readback_classification_preserves_immediate_and_delayed_distinctions(bool immediate, bool later, bool read, string expected)
        => Assert.Equal(expected, FanProbeLogic.ClassifyReadback(immediate, later, read));

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
    public void Automatic_preflight_failure_reports_unchanged_state_without_claiming_auto()
    {
        var t = new FakeFanTransport { FailTemperature = true };
        var result = NewProbe(t).AutomaticTest("EX", "MS-1T91", "test");

        Assert.False(result.Succeeded);
        Assert.Contains("FINAL STATE: UNCHANGED (no hardware writes performed)", File.ReadAllText(result.ReportPath!));
    }

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
    public void Physical_response_uses_only_bounded_10_to_75_duties_and_restores_auto()
    {
        var t = new FakeFanTransport();
        var r = new MsiFanHardwareProbe(t, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")), _ => { }).PhysicalResponse("EX", "MS-1T91", "test");
        Assert.True(r.Succeeded);
        Assert.Contains(t.Writes, x => x.Block == 1 && x.Payload.Skip(1).Take(6).All(v => v == 75));
        Assert.Contains(t.Writes, x => x.Block == 1 && x.Payload.Skip(1).Take(6).All(v => v == 10));
        Assert.All(t.Writes.Where(x => x.Block is 1 or 2 && x.Payload.Skip(1).Take(6).Any(v => v < 10)), x => Assert.Equal(new byte[] { 0, 40, 49, 58, 67, 75 }, x.Payload.Skip(1).Take(6)));
        Assert.Contains("FINAL STATE: AUTO", File.ReadAllText(r.ReportPath!));
    }

    [Theory]
    [InlineData(75, 40, "DECREASED")]
    [InlineData(40, 10, "DECREASED")]
    [InlineData(10, 75, "INCREASED")]
    [InlineData(null, 10, "INCONCLUSIVE")]
    public void Physical_directional_classification_is_conservative(int? before, int? after, string expected)
        => Assert.Equal(expected, FanProbeLogic.ClassifyDirectionalResponse(before, after));

    [Fact]
    public void Suspend_resume_arm_requires_resume_cleanup_and_returns_auto()
    {
        var t = new FakeFanTransport();
        var probe = new MsiFanHardwareProbe(t, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")), _ => { });
        var armed = probe.ArmSuspendResume("EX", "MS-1T91", "test");
        Assert.True(armed.Succeeded); Assert.Equal("ARMED", armed.Status);
        var resumed = probe.CompleteSuspendResumeAfterResume();
        Assert.True(resumed.Succeeded); Assert.Contains("FINAL STATE: AUTO", File.ReadAllText(resumed.ReportPath!));
    }

    [Fact]
    public void Restore_auto_cancels_an_armed_suspend_test_before_sleep()
    {
        var t = new FakeFanTransport();
        var probe = new MsiFanHardwareProbe(t, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")), _ => { });
        Assert.Equal("ARMED", probe.ArmSuspendResume("EX", "MS-1T91", "test").Status);
        var result = probe.RestoreAuto("EX", "MS-1T91", "test");
        Assert.True(result.Succeeded); Assert.Contains("CANCEL BEFORE SUSPEND", File.ReadAllText(result.ReportPath!));
    }

    [Fact]
    public void Cooler_cleanup_failure_does_not_skip_ownership_release()
    {
        var t = new FakeFanTransport { InitialOwnership = 0x80, FailCoolerRead = true };
        var result = NewProbe(t).RestoreAuto("EX", "MS-1T91", "test");

        Assert.False(result.Succeeded);
        Assert.Contains(t.Writes, x => x.Block == 212 && x.Payload[0] == 0);
        Assert.Contains("Cooler Boost final verification: READ_FAILED", File.ReadAllText(result.ReportPath!));
    }

    [Fact]
    public void Cooler_cleanup_verifies_the_cleared_value()
    {
        var t = new FakeFanTransport { InitialCooler = 0x80, InitialOwnership = 0x80 };
        var result = NewProbe(t).RestoreAuto("EX", "MS-1T91", "test");

        Assert.True(result.Succeeded);
        Assert.Contains("Cooler Boost final verification: OFF", File.ReadAllText(result.ReportPath!));
    }

    [Fact]
    public void Unsupported_model_does_not_touch_transport_or_diagnostic_helper()
    {
        var t = new DiagnosticSpyTransport();
        var r = new MsiFanHardwareProbe(t, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N"))).Capture("unknown", "unknown", "test");

        Assert.False(r.Succeeded);
        Assert.Empty(t.Writes);
        Assert.Empty(t.Reads);
        Assert.Equal(0, t.HelperInfoCalls);
        Assert.Equal(0, t.WmiVersionCalls);
        Assert.Equal(0, t.MethodInventoryCalls);
    }

    private static MsiFanHardwareProbe NewProbe(FakeFanTransport transport) => new(transport, Path.Combine(Path.GetTempPath(), "MsiFanProbeTests", Guid.NewGuid().ToString("N")));

    private sealed class DiagnosticSpyTransport : IMsiClawTdpTransport, IMsiFanDiagnosticTransport
    {
        internal readonly List<(int Block, byte[] Payload)> Writes = [];
        internal readonly List<string> Reads = [];
        internal int HelperInfoCalls;
        internal int WmiVersionCalls;
        internal int MethodInventoryCalls;

        public bool TryGetAp(int index, out byte[] payload) { Reads.Add($"AP{index}"); payload = []; return false; }
        public bool TrySetData(int block, byte value) { Writes.Add((block, [value])); return false; }
        public bool TryGetFan(int block, out byte[] payload) { Reads.Add($"Fan{block}"); payload = []; return false; }
        public bool TrySetFan(int block, byte[] payload) { Writes.Add((block, payload)); return false; }
        public bool TryGetTemperature(int index, out byte[] payload) { Reads.Add($"Temp{index}"); payload = []; return false; }
        public bool TryGetThermal(int index, out byte[] payload) { Reads.Add($"Thermal{index}"); payload = []; return false; }
        public bool TryGetData(int block, out byte[] payload) { Reads.Add($"Data{block}"); payload = []; return false; }
        public bool TryGetHelperInfo(out MsiFanHelperInfo info) { HelperInfoCalls++; info = new(0, "", false, "", ""); return false; }
        public bool TryGetWmiVersion(out MsiFanWmiVersion version) { WmiVersionCalls++; version = new(false, [], null, null, "", null); return false; }
        public bool TryGetMethodInventory(out string[] methods) { MethodInventoryCalls++; methods = []; return false; }
        public MsiFanOperationResult InvokeFanDiagnostic(string operation, int block, byte[]? payload) => throw new InvalidOperationException("Unsupported board must not invoke diagnostic operations.");
    }

    private sealed class FakeFanTransport : IMsiClawTdpTransport
    {
        internal readonly List<(int Block, byte[] Payload)> Writes = []; internal readonly List<string> Reads = [];
        internal bool FailTemperature; internal bool FailFan1Read; internal bool FailFan2Write; internal bool FailAp212; internal bool DoNotPersistFanWrites; internal bool FailRestoreWrite; internal bool FailApOnRestore; internal bool FailCoolerRead; internal bool HighDuty; internal bool DivergentFan2; internal bool CoupleFanWrites; internal bool RawFanResponse;
        internal byte InitialOwnership; internal byte InitialCooler;
        private readonly Dictionary<int, byte[]> _fans = new() { [1] = [70, 0, 40, 49, 58, 67, 75, 84], [2] = [71, 0, 40, 49, 58, 67, 75, 85] };
        private byte _ownership;
        private byte _cooler;
        private bool _initialized;
        public bool TryGetAp(int index, out byte[] payload) { EnsureInitialState(); Reads.Add($"AP{index}"); if (index == 1 && (FailAp212 || (FailApOnRestore && Writes.Any(x => x.Block == 1 || x.Block == 2)))) { payload = []; return false; } payload = index == 1 ? [_ownership] : [0x00, 0x01]; return true; }
        public bool TrySetData(int block, byte value) { EnsureInitialState(); Writes.Add((block, [value])); if (block == 212 && FailRestoreWrite) return false; if (block == 212) _ownership = value; if (block == 152) _cooler = value; return true; }
        public bool TryGetFan(int block, out byte[] payload) { Reads.Add($"Fan{block}"); if (block == 1 && FailFan1Read) { payload = []; return false; } if (_fans.TryGetValue(block, out var value)) { payload = (byte[])value.Clone(); if (block == 1 && HighDuty) payload[2] = 100; if (block == 2 && DivergentFan2) payload[3] = 50; if (RawFanResponse) payload = payload.Concat(new byte[23]).ToArray(); return true; } payload = []; return false; }
        public bool TrySetFan(int block, byte[] payload) { if (block == 2 && FailFan2Write) return false; Writes.Add((block, (byte[])payload.Clone())); if (!DoNotPersistFanWrites) _fans[block] = (byte[])payload.Clone(); if (CoupleFanWrites) { var other = block == 1 ? 2 : 1; var coupled = (byte[])_fans[other].Clone(); coupled[2] = (byte)(coupled[2] + 1); _fans[other] = coupled; } return true; }
        public bool TryGetTemperature(int index, out byte[] payload) { Reads.Add($"Temp{index}"); payload = FailTemperature ? [] : [47, 50, 57, 64, 71, 78]; return !FailTemperature; }
        public bool TryGetThermal(int index, out byte[] payload) { Reads.Add($"Thermal{index}"); payload = [44, 54, 64, 74, 82, 82]; return true; }
        public bool TryGetData(int block, out byte[] payload) { EnsureInitialState(); Reads.Add($"Data{block}"); if (block == 152 && FailCoolerRead) { payload = []; return false; } if (block == 152) { payload = [_cooler]; return true; } payload = [0]; return true; }

        private void EnsureInitialState()
        {
            if (_initialized) return;
            _ownership = InitialOwnership;
            _cooler = InitialCooler;
            _initialized = true;
        }
    }
}
