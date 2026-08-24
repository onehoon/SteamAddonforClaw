using System.Globalization;
using System.Text;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum FanProbeModel { Unsupported, A2vm, Cg3em }
internal enum FanProbeOperation { Capture, AutomaticTest, RestoreAuto }
internal sealed record FanProbeResult(bool Available, bool Succeeded, string Status, string? ReportPath, FanProbeModel Model, string Board);

internal static class FanProbeModelMap
{
    internal static FanProbeModel Resolve(string? board) => board?.Trim().ToUpperInvariant() switch
    {
        "MS-1T42" or "MS-1T52" => FanProbeModel.A2vm,
        "MS-1T91" => FanProbeModel.Cg3em,
        _ => FanProbeModel.Unsupported
    };
}

/// <summary>Bounded developer-only MSI fan diagnostic. It owns no persistent fan state.</summary>
internal sealed class MsiFanHardwareProbe
{
    private readonly IMsiClawTdpTransport _transport;
    private readonly string _reportDirectory;
    private readonly object _gate = new();
    private bool _running;
    internal MsiFanHardwareProbe(IMsiClawTdpTransport transport, string reportDirectory) { _transport = transport; _reportDirectory = reportDirectory; }
    internal FanProbeResult Capture(string device, string board, string firmware) => Run(FanProbeOperation.Capture, device, board, firmware, WriteCapture);
    internal FanProbeResult AutomaticTest(string device, string board, string firmware) => Run(FanProbeOperation.AutomaticTest, device, board, firmware, WriteAutomaticTest);
    internal FanProbeResult RestoreAuto(string device, string board, string firmware) => Run(FanProbeOperation.RestoreAuto, device, board, firmware, WriteRestore);

    private FanProbeResult Run(FanProbeOperation operation, string device, string board, string firmware, Action<StringBuilder, FanProbeModel> action)
    {
        lock (_gate) { if (_running) return new(true, false, "Another fan probe operation is already running.", null, FanProbeModelMap.Resolve(board), board); _running = true; }
        var model = FanProbeModelMap.Resolve(board); var path = Path.Combine(_reportDirectory, $"MsiFanProbe_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var report = new StringBuilder(); var success = false; var status = "FAILED"; var handback = true;
        try
        {
            report.AppendLine("MSI Fan Hardware Probe"); report.AppendLine($"Operation: {operation.ToString().ToUpperInvariant()}"); report.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            report.AppendLine($"Device: {device}"); report.AppendLine($"Board: {board}"); report.AppendLine($"Probe model: {model}"); report.AppendLine($"BIOS/EC/Firmware: {firmware}");
            if (model == FanProbeModel.Unsupported) { report.AppendLine("PRECHECK: FAIL - unsupported board"); return Finish(path, report, false, status, model, board); }
            action(report, model); success = !report.ToString().Contains("FAIL", StringComparison.Ordinal); status = success ? "PASS" : "FAILED";
        }
        catch (Exception exception) { report.AppendLine($"EXCEPTION: {exception}"); }
        finally { if (operation == FanProbeOperation.AutomaticTest) { report.AppendLine("=== FIRMWARE HAND-BACK ==="); handback = RestoreFirmwareAuto(); report.AppendLine($"Result: {handback}"); report.AppendLine("Final state intentionally: MSI firmware Auto"); } }
        if (!handback) { success = false; status = "FAILED"; }
        return Finish(path, report, success && status == "PASS", status, model, board);
    }
    private FanProbeResult Finish(string path, StringBuilder report, bool success, string status, FanProbeModel model, string board)
    { report.AppendLine($"OVERALL: {status}"); Directory.CreateDirectory(_reportDirectory); File.WriteAllText(path, report.ToString()); lock (_gate) _running = false; return new(true, success, status, path, model, board); }

    private void WriteCapture(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== BASELINE READS ==="); for (var i = 0; i <= 2; i++) ReadFan(report, i, i != 0);
        Read(report, "Get_Temperature(1)", () => _transport.TryGetTemperature(1, out var p) ? p : throw new InvalidOperationException("Get_Temperature(1) failed"));
        Read(report, "Get_Temperature(2)", () => _transport.TryGetTemperature(2, out var p) ? p : throw new InvalidOperationException("Get_Temperature(2) failed"));
        Read(report, "Get_AP(1)", () => _transport.TryGetAp(1, out var p) ? p : throw new InvalidOperationException("Get_AP(1) failed"));
        Read(report, "Get_Data(152)", () => _transport.TryGetData(152, out var p) ? p : throw new InvalidOperationException("Get_Data(152) failed"));
        report.AppendLine($"Model-specific expectation: {(model == FanProbeModel.Cg3em ? "EX observations recorded, not enforced" : "A2VM reference values recorded, not enforced")}");
    }
    private void WriteAutomaticTest(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== PREFLIGHT ===");
        if (!TryReadFan(1, out var fan1) || !TryReadFan(2, out var fan2) || !_transport.TryGetTemperature(1, out _) || !_transport.TryGetTemperature(2, out _) || !_transport.TryGetAp(1, out _) || !_transport.TryGetAp(212, out _) || !_transport.TryGetData(152, out var cooler)) { report.AppendLine("PRECHECK: FAIL; no writes performed"); return; }
        report.AppendLine($"Cooler Boost block 152 before: 0x{cooler[0]:X2}");
        if ((cooler[0] & 0x80) != 0)
        {
            var requestedCooler = (byte)(cooler[0] & 0x7F); report.AppendLine($"Cooler Boost requested transition: 0x{cooler[0]:X2} -> 0x{requestedCooler:X2}");
            if (!_transport.TrySetData(152, requestedCooler) || !_transport.TryGetData(152, out var coolerAfter) || (coolerAfter[0] & 0x80) != 0) { report.AppendLine("Cooler Boost transition: FAIL"); return; }
            report.AppendLine($"Cooler Boost after: 0x{coolerAfter[0]:X2}");
        }
        report.AppendLine("PRECHECK: PASS"); DescribeFan(report, 1, fan1); DescribeFan(report, 2, fan2);
        var fan1Ok = TestBlock(report, 1, fan1); var fan2Ok = fan1Ok && TestBlock(report, 2, fan2);
        if (!fan1Ok || !fan2Ok) { report.AppendLine("PARTIAL APPLY FAILURE: custom ownership not enabled"); return; }
        var curve = fan1.Skip(1).Take(6).ToArray(); curve[2] = (byte)Math.Min(75, curve[2] + 1);
        report.AppendLine("=== SHARED CURVE TEST ==="); var shared1 = _transport.TrySetFan(1, WithDuties(fan1, curve)) && TryReadFan(1, out var after1) && after1.Skip(1).Take(6).SequenceEqual(curve); var shared2 = shared1 && _transport.TrySetFan(2, WithDuties(fan2, curve)) && TryReadFan(2, out var after2) && after2.Skip(1).Take(6).SequenceEqual(curve);
        report.AppendLine($"Fan 1/Fan 2 shared curve verification: {(shared1 && shared2 ? "PASS" : "FAIL")}"); if (!(shared1 && shared2)) return;
        var ownership = SetOwnership(true); report.AppendLine($"Custom ownership enable: {(ownership ? "PASS" : "FAIL")}"); if (!ownership) return;
    }
    private void WriteRestore(StringBuilder report, FanProbeModel model) => report.AppendLine($"Firmware Auto hand-back: {RestoreFirmwareAuto()}");
    private bool TestBlock(StringBuilder report, int block, byte[] original)
    {
        if (original.Length < 8) { report.AppendLine($"Fan {block}: FAIL short payload"); return false; } var point = 2; var next = (byte)Math.Min(75, original[point] + 1); if (next == original[point]) { report.AppendLine($"Fan {block}: SKIPPED bounded delta"); return true; }
        var otherBlock = block == 1 ? 2 : 1; var otherBefore = TryReadFan(otherBlock, out var otherPayload) ? otherPayload : null;
        var requested = (byte[])original.Clone(); requested[point] = next; var wrote = _transport.TrySetFan(block, requested); byte[] back = []; var read = wrote && TryReadFan(block, out back); var verified = read && back.Length >= 8 && back[point] == next && back[0] == original[0] && back[7] == original[7]; report.AppendLine($"Fan {block} RMW point {point} {original[point]} -> {next}: {(verified ? "PASS" : "FAIL")}; byte0/byte7 preserved: {verified}");
        var otherAfter = TryReadFan(otherBlock, out var otherPayloadAfter) ? otherPayloadAfter : null; var unexpected = otherBefore is null || otherAfter is null ? "UNKNOWN" : otherBefore.SequenceEqual(otherAfter) ? "NO" : "YES"; report.AppendLine($"Fan {otherBlock} changed unexpectedly during Fan {block} write: {unexpected}");
        var restored = _transport.TrySetFan(block, original) && TryReadFan(block, out var final) && final!.Skip(1).Take(6).SequenceEqual(original.Skip(1).Take(6)); report.AppendLine($"Fan {block} restore: {(restored ? "PASS" : "FAIL")}"); return verified && restored;
    }
    private bool RestoreFirmwareAuto() { if (!_transport.TryGetAp(212, out var ap) || ap.Length == 0) return false; return _transport.TrySetData(212, (byte)(ap[0] & 0x7F)); }
    private bool SetOwnership(bool enabled) { if (!_transport.TryGetAp(212, out var p) || p.Length == 0) return false; return _transport.TrySetData(212, enabled ? (byte)(p[0] | 0x80) : (byte)(p[0] & 0x7F)); }
    private bool TryReadFan(int block, out byte[] payload) => _transport.TryGetFan(block, out payload) && payload.Length >= 8;
    private void ReadFan(StringBuilder report, int block, bool required) { if (TryReadFan(block, out var p)) DescribeFan(report, block, p); else report.AppendLine($"Get_Fan({block}): {(required ? "FAIL" : "UNAVAILABLE")}"); }
    private static void DescribeFan(StringBuilder report, int block, byte[] payload) => report.AppendLine($"Get_Fan({block}) HEX: {string.Join(" ", payload.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)))} DEC: {string.Join(" ", payload.Select(x => x.ToString(CultureInfo.InvariantCulture)))} Duties[1..6]: {string.Join(",", payload.Skip(1).Take(6))} byte0 preserved: {payload[0]} byte7 preserved: {payload[7]}");
    private static byte[] WithDuties(byte[] original, byte[] duties) { var copy = (byte[])original.Clone(); duties.CopyTo(copy, 1); return copy; }
    private static void Read(StringBuilder report, string name, Func<byte[]> read) { var p = read(); report.AppendLine($"{name} HEX: {string.Join(" ", p.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)))} DEC: {string.Join(" ", p)}"); }
}
