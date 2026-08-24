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
    private bool _hardwareWritesStarted;
    internal MsiFanHardwareProbe(IMsiClawTdpTransport transport, string reportDirectory) { _transport = transport; _reportDirectory = reportDirectory; }
    internal FanProbeResult Capture(string device, string board, string firmware) => Run(FanProbeOperation.Capture, device, board, firmware, WriteCapture);
    internal FanProbeResult AutomaticTest(string device, string board, string firmware) => Run(FanProbeOperation.AutomaticTest, device, board, firmware, WriteAutomaticTest);
    internal FanProbeResult RestoreAuto(string device, string board, string firmware) => Run(FanProbeOperation.RestoreAuto, device, board, firmware, WriteRestore);

    private FanProbeResult Run(FanProbeOperation operation, string device, string board, string firmware, Func<StringBuilder, FanProbeModel, bool> action)
    {
        lock (_gate) { if (_running) return new(true, false, "Another fan probe operation is already running.", null, FanProbeModelMap.Resolve(board), board); _running = true; }
        var model = FanProbeModelMap.Resolve(board); var path = Path.Combine(_reportDirectory, $"MsiFanProbe_{DateTime.Now:yyyyMMdd_HHmmss}.txt"); _hardwareWritesStarted = false;
        var report = new StringBuilder(); var success = false; var status = "FAILED"; var handback = true;
        try
        {
            report.AppendLine("MSI Fan Hardware Probe"); report.AppendLine($"Operation: {operation.ToString().ToUpperInvariant()}"); report.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            report.AppendLine($"Device: {device}"); report.AppendLine($"Board: {board}"); report.AppendLine($"Probe model: {model}"); report.AppendLine($"BIOS/EC/Firmware: {firmware}");
            if (model == FanProbeModel.Unsupported) { report.AppendLine("PRECHECK: FAIL - unsupported board"); return Finish(path, report, false, status, model, board); }
            success = action(report, model); status = success ? "PASS" : "FAILED";
        }
        catch (Exception exception) { report.AppendLine($"EXCEPTION: {exception}"); }
        finally { if (operation == FanProbeOperation.AutomaticTest && _hardwareWritesStarted) { report.AppendLine("=== FIRMWARE HAND-BACK ==="); handback = RestoreFirmwareAuto(); report.AppendLine($"Result: {handback}"); report.AppendLine("Final state intentionally: MSI firmware Auto"); } }
        if (!handback) { success = false; status = "FAILED"; }
        return Finish(path, report, success && status == "PASS", status, model, board);
    }
    private FanProbeResult Finish(string path, StringBuilder report, bool success, string status, FanProbeModel model, string board)
    { report.AppendLine($"OVERALL: {status}"); Directory.CreateDirectory(_reportDirectory); File.WriteAllText(path, report.ToString()); lock (_gate) _running = false; return new(true, success, status, path, model, board); }

    private bool WriteCapture(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== BASELINE READS ==="); var fan0Ok = ReadFan(report, 0, false); var fan1Ok = ReadFan(report, 1, true); var fan2Ok = ReadFan(report, 2, true);
        try
        {
            Read(report, "Get_Temperature(1)", () => _transport.TryGetTemperature(1, out var p) ? p : throw new InvalidOperationException("Get_Temperature(1) failed"));
            Read(report, "Get_Temperature(2)", () => _transport.TryGetTemperature(2, out var p) ? p : throw new InvalidOperationException("Get_Temperature(2) failed"));
            Read(report, "Get_AP(1)", () => _transport.TryGetAp(1, out var p) ? p : throw new InvalidOperationException("Get_AP(1) failed"));
            Read(report, "Get_Data(152)", () => _transport.TryGetData(152, out var p) ? p : throw new InvalidOperationException("Get_Data(152) failed"));
        }
        catch (Exception exception) { report.AppendLine($"CAPTURE FAIL: {exception.Message}"); return false; }
        report.AppendLine($"Model-specific expectation: {(model == FanProbeModel.Cg3em ? "EX observations recorded, not enforced" : "A2VM reference values recorded, not enforced")}");
        return fan0Ok && fan1Ok && fan2Ok;
    }
    private bool WriteAutomaticTest(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== PREFLIGHT ===");
        if (!TryReadFan(1, out var fan1) || !TryReadFan(2, out var fan2) || !_transport.TryGetTemperature(1, out _) || !_transport.TryGetTemperature(2, out _) || !_transport.TryGetAp(1, out _) || !_transport.TryGetData(152, out var cooler)) { report.AppendLine("PRECHECK: FAIL; no writes performed"); return false; }
        report.AppendLine($"Cooler Boost block 152 before: 0x{cooler[0]:X2}");
        if ((cooler[0] & 0x80) != 0)
        {
            var requestedCooler = (byte)(cooler[0] & 0x7F); report.AppendLine($"Cooler Boost requested transition: 0x{cooler[0]:X2} -> 0x{requestedCooler:X2}");
            _hardwareWritesStarted = true; if (!_transport.TrySetData(152, requestedCooler) || !_transport.TryGetData(152, out var coolerAfter) || (coolerAfter[0] & 0x80) != 0) { report.AppendLine("Cooler Boost transition: FAIL"); return false; }
            report.AppendLine($"Cooler Boost after: 0x{coolerAfter[0]:X2}");
        }
        report.AppendLine("PRECHECK: PASS"); DescribeFan(report, 1, fan1); DescribeFan(report, 2, fan2);
        var fan1Ok = TestBlock(report, 1, fan1); var fan2Ok = fan1Ok && TestBlock(report, 2, fan2);
        if (!fan1Ok || !fan2Ok) { report.AppendLine("PARTIAL APPLY FAILURE: custom ownership not enabled"); return false; }
        var fan1Duties = fan1.Skip(1).Take(6).ToArray(); var fan2Duties = fan2.Skip(1).Take(6).ToArray();
        if (!fan1Duties.SequenceEqual(fan2Duties))
        {
            report.AppendLine("Shared curve test: SKIPPED - Fan 1 and Fan 2 curves differ; custom ownership was not enabled.");
            return false;
        }
        if (!TryGetSafeIncrement(fan1Duties[2], out var sharedPoint))
        {
            report.AppendLine($"Shared curve test: SKIPPED - current duty {fan1Duties[2]} is outside the conservative test range.");
            return false;
        }
        var curve = (byte[])fan1Duties.Clone(); curve[2] = sharedPoint;
        report.AppendLine("=== SHARED CURVE TEST ==="); var shared1 = TryWriteDutiesRmw(1, curve, out _, out _, out _); var shared2 = shared1 && TryWriteDutiesRmw(2, curve, out _, out _, out _);
        report.AppendLine($"Fan 1/Fan 2 shared curve verification: {(shared1 && shared2 ? "PASS" : "FAIL")}"); if (!(shared1 && shared2)) return false;
        var ownership = SetOwnership(true); report.AppendLine($"Custom ownership enable: {(ownership ? "PASS" : "FAIL")}"); if (!ownership) return false;
        return true;
    }
    private bool WriteRestore(StringBuilder report, FanProbeModel model) { var restored = RestoreFirmwareAuto(); report.AppendLine($"Firmware Auto hand-back: {(restored ? "PASS" : "FAIL")}"); return restored; }
    private bool TestBlock(StringBuilder report, int block, byte[] original)
    {
        if (original.Length < 8) { report.AppendLine($"Fan {block}: FAIL short payload"); return false; } var point = 2; var originalDuties = original.Skip(1).Take(6).ToArray(); if (!TryGetSafeIncrement(original[point], out var next)) { report.AppendLine($"Fan {block}: SKIPPED bounded delta; current duty {original[point]} is outside the conservative test range."); return true; }
        var otherBlock = block == 1 ? 2 : 1; if (!TryReadFan(otherBlock, out var otherBefore)) { report.AppendLine($"Fan {otherBlock} changed unexpectedly during Fan {block} write: UNKNOWN"); return false; } var otherDutiesBefore = otherBefore.Skip(1).Take(6).ToArray();
        var requestedDuties = (byte[])originalDuties.Clone(); requestedDuties[point - 1] = next; var wrote = TryWriteDutiesRmw(block, requestedDuties, out var back, out _, out var requestBoundaries); var dutiesVerified = wrote && back.Skip(1).Take(6).SequenceEqual(requestedDuties); report.AppendLine($"Fan {block} RMW point {point} {original[point]} -> {next}: request byte0/byte7 preserved: {requestBoundaries}; owned duty readback: {(dutiesVerified ? "PASS" : "FAIL")}");
        var otherUnchanged = TryReadFan(otherBlock, out var otherAfter) && otherAfter.Skip(1).Take(6).SequenceEqual(otherDutiesBefore); report.AppendLine($"Fan {otherBlock} changed unexpectedly during Fan {block} write: {(otherUnchanged ? "NO" : "YES/UNKNOWN")}"); if (!otherUnchanged) { _ = TryWriteDutiesRmw(otherBlock, otherDutiesBefore, out _, out _, out _); return false; }
        var restored = TryWriteDutiesRmw(block, originalDuties, out _, out _, out _); report.AppendLine($"Fan {block} restore: {(restored ? "PASS" : "FAIL")}"); return dutiesVerified && requestBoundaries && restored;
    }

    private bool TryWriteDutiesRmw(int block, byte[] duties, out byte[] readback, out byte[] requested, out bool requestBoundaries)
    {
        readback = []; requested = []; requestBoundaries = false;
        if (duties.Length != 6 || !TryReadFan(block, out var current)) return false;
        requested = (byte[])current.Clone(); duties.CopyTo(requested, 1); requestBoundaries = requested[0] == current[0] && requested[7] == current[7]; _hardwareWritesStarted = true;
        return _transport.TrySetFan(block, requested) && TryReadFan(block, out readback) && readback.Skip(1).Take(6).SequenceEqual(duties);
    }
    private bool RestoreFirmwareAuto()
    {
        if (!_transport.TryGetData(152, out var cooler) || cooler.Length == 0) return false;
        if ((cooler[0] & 0x80) != 0)
        {
            var requestedCooler = (byte)(cooler[0] & 0x7F);
            if (!_transport.TrySetData(152, requestedCooler) || !_transport.TryGetData(152, out var coolerVerify) || coolerVerify.Length == 0 || (coolerVerify[0] & 0x80) != 0) return false;
        }
        if (!_transport.TryGetAp(1, out var ap) || ap.Length == 0) return false;
        var requestedOwnership = (byte)(ap[0] & 0x7F);
        if (!_transport.TrySetData(212, requestedOwnership)) return false;
        return _transport.TryGetAp(1, out var verify) && verify.Length > 0 && (verify[0] & 0x80) == 0;
    }
    private bool SetOwnership(bool enabled)
    {
        if (!_transport.TryGetAp(1, out var ap) || ap.Length == 0) return false;
        var requested = enabled ? (byte)(ap[0] | 0x80) : (byte)(ap[0] & 0x7F);
        _hardwareWritesStarted = true; if (!_transport.TrySetData(212, requested)) return false;
        return _transport.TryGetAp(1, out var verify) && verify.Length > 0 && (((verify[0] & 0x80) != 0) == enabled);
    }
    private bool TryReadFan(int block, out byte[] payload) => _transport.TryGetFan(block, out payload) && payload.Length >= 8;
    private static bool TryGetSafeIncrement(byte current, out byte next)
    { next = current; if (current >= 75) return false; next = (byte)(current + 1); return true; }
    private bool ReadFan(StringBuilder report, int block, bool required) { if (TryReadFan(block, out var p)) { DescribeFan(report, block, p); return true; } report.AppendLine($"Get_Fan({block}): {(required ? "FAIL" : "UNAVAILABLE")}"); return !required; }
    private static void DescribeFan(StringBuilder report, int block, byte[] payload) => report.AppendLine($"Get_Fan({block}) HEX: {string.Join(" ", payload.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)))} DEC: {string.Join(" ", payload.Select(x => x.ToString(CultureInfo.InvariantCulture)))} Duties[1..6]: {string.Join(",", payload.Skip(1).Take(6))} byte0 preserved: {payload[0]} byte7 preserved: {payload[7]}");
    private static byte[] WithDuties(byte[] original, byte[] duties) { var copy = (byte[])original.Clone(); duties.CopyTo(copy, 1); return copy; }
    private static void Read(StringBuilder report, string name, Func<byte[]> read) { var p = read(); report.AppendLine($"{name} HEX: {string.Join(" ", p.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)))} DEC: {string.Join(" ", p)}"); }
}
