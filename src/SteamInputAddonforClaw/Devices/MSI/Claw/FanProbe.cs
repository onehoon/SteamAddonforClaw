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

internal static class FanProbeLogic
{
    internal static bool TryNormalizeLogicalFanBlock(byte[] raw, out byte[] logical)
    {
        if (raw.Length < 8) { logical = []; return false; }
        logical = raw.AsSpan(0, 8).ToArray();
        return true;
    }

    internal static bool TrySelectSafeIncrement(IReadOnlyList<byte> duties, out int index, out byte next)
    {
        // Prefer the highest safe middle slot so the EX reference curve naturally tests 74 -> 75.
        for (var candidate = Math.Min(duties.Count - 2, 4); candidate >= 1; candidate--)
        {
            if (duties[candidate] < 75)
            {
                index = candidate;
                next = (byte)(duties[candidate] + 1);
                return true;
            }
        }
        index = -1;
        next = 0;
        return false;
    }

    internal static string ClassifyReadback(bool immediateMatch, bool laterMatch, bool targetRead) =>
        immediateMatch ? "IMMEDIATE_MATCH" : laterMatch ? "DELAYED_MATCH" : targetRead ? "MISMATCH" : "READ_FAILED";
}

/// <summary>Bounded developer-only MSI fan diagnostic. It owns no persistent fan state.</summary>
internal sealed class MsiFanHardwareProbe
{
    private readonly IMsiClawTdpTransport _transport;
    private readonly IMsiFanDiagnosticTransport? _diagnostics;
    private readonly string _reportDirectory;
    private readonly Action<TimeSpan> _delay;
    private readonly object _gate = new();
    private bool _running;
    private bool _hardwareWritesStarted;
    private byte[]? _originalFan1;
    private byte[]? _originalFan2;

    internal MsiFanHardwareProbe(IMsiClawTdpTransport transport, string reportDirectory, Action<TimeSpan>? delay = null)
    {
        _transport = transport;
        _diagnostics = transport as IMsiFanDiagnosticTransport;
        _reportDirectory = reportDirectory;
        _delay = delay ?? Thread.Sleep;
    }

    internal FanProbeResult Capture(string device, string board, string firmware) => Run(FanProbeOperation.Capture, device, board, firmware, WriteCapture);
    internal FanProbeResult AutomaticTest(string device, string board, string firmware) => Run(FanProbeOperation.AutomaticTest, device, board, firmware, WriteAutomaticTest);
    internal FanProbeResult RestoreAuto(string device, string board, string firmware) => Run(FanProbeOperation.RestoreAuto, device, board, firmware, WriteRestore);
    private bool WriteRestore(StringBuilder report, FanProbeModel model) => RestoreFirmwareAuto(report);

    private FanProbeResult Run(FanProbeOperation operation, string device, string board, string firmware, Func<StringBuilder, FanProbeModel, bool> action)
    {
        lock (_gate)
        {
            if (_running) return new(true, false, "Another fan probe operation is already running.", null, FanProbeModelMap.Resolve(board), board);
            _running = true;
        }

        var model = FanProbeModelMap.Resolve(board);
        var path = Path.Combine(_reportDirectory, $"MsiFanProbe_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var report = new StringBuilder();
        var success = false;
        var status = "FAILED";
        var handback = true;
        _hardwareWritesStarted = false;
        _originalFan1 = null;
        _originalFan2 = null;
        try
        {
            report.AppendLine("MSI Fan Hardware Probe");
            report.AppendLine($"Operation: {operation.ToString().ToUpperInvariant()}");
            report.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            report.AppendLine($"Device: {device}");
            report.AppendLine($"Board: {board}");
            report.AppendLine($"Probe model: {model}");
            report.AppendLine($"BIOS/EC/Firmware: {firmware}");
            if (model == FanProbeModel.Unsupported)
            {
                report.AppendLine("PRECHECK: FAIL - unsupported board");
                return Finish(path, report, false, status, model, board);
            }
            WriteEnvironment(report);
            success = action(report, model);
            status = success ? "PASS" : "FAILED";
        }
        catch (Exception exception)
        {
            report.AppendLine($"EXCEPTION: {exception}");
        }
        finally
        {
            if (operation == FanProbeOperation.AutomaticTest && _hardwareWritesStarted)
            {
                report.AppendLine("=== RESTORE TABLES ===");
                var tables = RestoreOriginalTables(report);
                report.AppendLine("=== FIRMWARE HAND-BACK ===");
                handback = RestoreFirmwareAuto(report);
                report.AppendLine($"Table restore: {(tables ? "PASS" : "FAIL")}");
                report.AppendLine($"Firmware Auto hand-back: {(handback ? "PASS" : "FAIL")}");
                report.AppendLine($"FINAL STATE: {(handback ? "AUTO" : "UNKNOWN")}");
                WriteFinalSnapshot(report);
                if (!tables) success = false;
            }
        }
        if (operation == FanProbeOperation.AutomaticTest && !_hardwareWritesStarted)
            report.AppendLine("FINAL STATE: UNCHANGED (no hardware writes performed)");
        if (!handback) { success = false; status = "FAILED"; }
        return Finish(path, report, success && status == "PASS", success && status == "PASS" ? "PASS" : "FAILED", model, board);
    }

    private FanProbeResult Finish(string path, StringBuilder report, bool success, string status, FanProbeModel model, string board)
    {
        report.AppendLine($"OVERALL: {status}");
        Directory.CreateDirectory(_reportDirectory);
        File.WriteAllText(path, report.ToString());
        lock (_gate) _running = false;
        return new(true, success, status, path, model, board);
    }

    private void WriteEnvironment(StringBuilder report)
    {
        report.AppendLine("=== HELPER ===");
        if (_diagnostics is null || !_diagnostics.TryGetHelperInfo(out var helper))
            report.AppendLine("Helper diagnostics: UNAVAILABLE");
        else
        {
            report.AppendLine($"Helper PID: {helper.ProcessId}");
            report.AppendLine($"Helper executable: {helper.Executable}");
            report.AppendLine($"Helper elevated/admin: {(helper.Elevated ? "YES" : "NO")}");
            report.AppendLine($"Process architecture: {helper.ProcessArchitecture}");
            report.AppendLine($"OS architecture: {helper.OsArchitecture}");
        }
        report.AppendLine("=== WMI ENVIRONMENT ===");
        if (_diagnostics is not null && _diagnostics.TryGetWmiVersion(out var version))
        {
            report.AppendLine($"WMI version raw response: {Hex(version.RawPayload)}");
            report.AppendLine($"WMI major: {version.Major?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            report.AppendLine($"WMI minor: {version.Minor?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            report.AppendLine("WMI3 dispatch: observed only; no speculative Fan-specific method added");
        }
        else report.AppendLine("WMI version: unavailable");
        if (_diagnostics is not null && _diagnostics.TryGetMethodInventory(out var methods))
            report.AppendLine($"Relevant method inventory: {(methods.Length == 0 ? "none" : string.Join(", ", methods))}");
        else report.AppendLine("Relevant method inventory: unavailable");
    }

    private bool WriteCapture(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== BASELINE ===");
        WriteReferenceBaseline(report, model);
        var baseline = CaptureBaseline(report, requireFanTables: true);
        report.AppendLine($"Model-specific expectation: {(model == FanProbeModel.Cg3em ? "EX observations recorded, not enforced" : "A2VM reference values recorded, not enforced")}");
        return baseline;
    }

    private bool CaptureBaseline(StringBuilder report, bool requireFanTables)
    {
        ReadFan(report, 0, false);
        var fan1 = ReadFan(report, 1, requireFanTables);
        var fan2 = ReadFan(report, 2, requireFanTables);
        var reads = true;
        foreach (var index in new[] { 1, 2 })
        {
            reads &= ReadPayload(report, $"Get_Temperature({index})", "GetTemperature", index, () => _transport.TryGetTemperature(index, out var p) ? p : null);
            reads &= ReadPayload(report, $"Get_Thermal({index})", "GetThermal", index, () => _transport.TryGetThermal(index, out var p) ? p : null);
        }
        foreach (var index in new[] { 0, 1, 2 })
            reads &= ReadPayload(report, $"Get_AP({index})", "GetAp", index, () => _transport.TryGetAp(index, out var p) ? p : null);
        foreach (var block in new[] { 152, 210, 212 })
            reads &= ReadPayload(report, $"Get_Data({block})", "GetData", block, () => _transport.TryGetData(block, out var p) ? p : null);
        if (fan1.Logical is not null) _originalFan1 = fan1.Logical;
        if (fan2.Logical is not null) _originalFan2 = fan2.Logical;
        return fan1.Success && fan2.Success && reads;
    }

    private bool WriteAutomaticTest(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("=== PREFLIGHT ===");
        WriteReferenceBaseline(report, model);
        if (!CaptureBaseline(report, requireFanTables: true) || _originalFan1 is null || _originalFan2 is null)
        {
            report.AppendLine("PRECHECK: FAIL; no fan writes performed");
            return false;
        }
        if (!_transport.TryGetData(152, out var cooler) || cooler.Length == 0)
        {
            report.AppendLine("Cooler Boost: READ_FAILED; no fan writes performed");
            return false;
        }
        report.AppendLine($"Cooler Boost before: 0x{cooler[0]:X2}");
        if ((cooler[0] & 0x80) != 0)
        {
            _hardwareWritesStarted = true;
            var requestedCooler = (byte)(cooler[0] & 0x7F);
            if (!_transport.TrySetData(152, requestedCooler) || !_transport.TryGetData(152, out var coolerAfter) || coolerAfter.Length == 0 || (coolerAfter[0] & 0x80) != 0)
            {
                report.AppendLine("Cooler Boost transition: FAIL");
                return false;
            }
            report.AppendLine($"Cooler Boost after: 0x{coolerAfter[0]:X2}");
        }
        report.AppendLine("PRECHECK: PASS");
        var fan1 = TestBlock(report, 1, _originalFan1);
        var fan2 = fan1 && TestBlock(report, 2, _originalFan2);
        if (!fan1 || !fan2)
        {
            report.AppendLine("PARTIAL APPLY FAILURE: custom ownership not enabled");
            return false;
        }

        var shared = _originalFan1.Skip(1).Take(6).ToArray();
        if (!_originalFan1.Skip(1).Take(6).SequenceEqual(_originalFan2.Skip(1).Take(6)))
        {
            report.AppendLine("=== SHARED CURVE ===");
            report.AppendLine("SKIPPED: current fan curves differ; no synthesized per-model curve used");
            return false;
        }
        if (!FanProbeLogic.TrySelectSafeIncrement(shared, out var sharedIndex, out var sharedNext))
        {
            report.AppendLine("=== SHARED CURVE ===");
            report.AppendLine("SKIPPED: no safe current duty below 75");
            return false;
        }
        var temporaryDuties = (byte[])shared.Clone();
        temporaryDuties[sharedIndex] = sharedNext;
        var temporary = WithDuties(_originalFan1, temporaryDuties);
        report.AppendLine("=== SHARED CURVE ===");
        report.AppendLine($"Current duties: {string.Join(",", shared)}");
        report.AppendLine($"Temporary duties: {string.Join(",", temporary)}");
        if (!WriteLogicalBlock(report, 1, temporary, "Shared Fan 1") || !WriteLogicalBlock(report, 2, temporary, "Shared Fan 2"))
        {
            report.AppendLine("Shared curve verification: FAIL");
            return false;
        }
        report.AppendLine("Shared curve verification: PASS");

        report.AppendLine("=== CUSTOM OWNERSHIP ===");
        var ownership = SetOwnership(report, true);
        report.AppendLine($"Custom ownership: {(ownership ? "PASS" : "FAIL")}");
        if (!ownership) return false;
        ObserveOwnership(report);
        return true;
    }

    private static void WriteReferenceBaseline(StringBuilder report, FanProbeModel model)
    {
        report.AppendLine("Known reference (comparison only; not enforced):");
        report.AppendLine(model == FanProbeModel.Cg3em
            ? "EX Fan 1/2 logical: 58 | 70 74 76 78 80 84 | 94; temperature labels: 47 / 50 / 57 / 64 / 71 / 78; ownership OFF; Cooler Boost OFF"
            : "A2VM reference Fan logical: 0 | 40 49 58 67 75 | model-specific current values are not enforced");
    }

    private bool TestBlock(StringBuilder report, int block, byte[] original)
    {
        report.AppendLine($"=== FAN {block} ===");
        if (!WriteLogicalBlock(report, block, original, $"Fan {block} same-value")) return false;
        var duties = original.Skip(1).Take(6).ToArray();
        if (!FanProbeLogic.TrySelectSafeIncrement(duties, out var index, out var next))
        {
            report.AppendLine("Changed duty: SKIPPED - no safe point below 75");
            return false;
        }
        var requested = (byte[])duties.Clone();
        requested[index] = next;
        report.AppendLine($"Changed duty: index {index + 1}, {duties[index]} -> {next}");
        var changed = WriteLogicalBlock(report, block, WithDuties(original, requested), $"Fan {block} changed");
        var otherBlock = block == 1 ? 2 : 1;
        var otherUnchanged = TryReadFan(otherBlock, out _, out var otherAfter) &&
            otherAfter.Skip(1).Take(6).SequenceEqual((block == 1 ? _originalFan2 : _originalFan1)!.Skip(1).Take(6));
        report.AppendLine($"Fan {otherBlock} changed unexpectedly during Fan {block} write: {(otherUnchanged ? "NO" : "YES/UNKNOWN")}");
        var restore = WriteLogicalBlock(report, block, original, $"Fan {block} restore");
        report.AppendLine($"Fan {block} restore: {(restore ? "PASS" : "FAIL")}");
        return changed && otherUnchanged && restore;
    }

    private bool WriteLogicalBlock(StringBuilder report, int block, byte[] logical, string label)
    {
        if (logical.Length != 8)
        {
            report.AppendLine($"{label}: PRE_WMI_PROTOCOL_FAIL logical payload length={logical.Length}");
            return false;
        }
        if (!TryReadFan(block, out var currentRaw, out var current)) return false;
        var requested = (byte[])current.Clone();
        logical.AsSpan(1, 6).CopyTo(requested.AsSpan(1, 6));
        report.AppendLine($"{label} raw Get_Fan response length: {currentRaw.Length}");
        report.AppendLine($"{label} logical before block: {Hex(current)}");
        report.AppendLine($"{label} requested logical block: {Hex(requested)}");
        report.AppendLine($"{label} byte0 preserved: {requested[0] == current[0]}; byte7 preserved: {requested[7] == current[7]}");
        var operation = SetFan(report, block, requested, label);
        if (!operation) return false;
        var observations = ObserveReadback(report, block, requested, label);
        report.AppendLine($"{label} classification: WMI_INVOKE_OK_READBACK_{observations switch { "IMMEDIATE_MATCH" => "PASS", "DELAYED_MATCH" => "DELAYED_MATCH", "READ_FAILED" => "FAIL", _ => "MISMATCH" }}");
        return observations is "IMMEDIATE_MATCH" or "DELAYED_MATCH";
    }

    private bool SetFan(StringBuilder report, int block, byte[] requested, string label)
    {
        _hardwareWritesStarted = true;
        if (_diagnostics is null)
        {
            var ok = _transport.TrySetFan(block, requested);
            report.AppendLine($"{label} Stage: {(ok ? "WMI_INVOKE_OK" : "WMI_INVOKE_FAIL")}");
            report.AppendLine($"{label} Logical payload length: {requested.Length}; WMI package length: 32");
            report.AppendLine($"{label} Package: {Hex(BuildPackageForReport(block, requested))}");
            return ok;
        }
        var result = _diagnostics.InvokeFanDiagnostic("SetFan", block, requested);
        report.AppendLine($"{label} Operation: Set_Fan; Block: {block}");
        report.AppendLine($"{label} Stage: {result.Stage}; Exception type: {result.ExceptionType ?? "none"}; HRESULT: {(result.HResult is int hr ? $"0x{hr:X8}" : "none")}");
        report.AppendLine($"{label} ManagementStatus: {result.ManagementStatus?.ToString() ?? "none"}; UsedFallback: {result.UsedFallback}");
        report.AppendLine($"{label} Invoke returned normally: {result.InvokeReturnedNormally}; Output object present: {result.OutputObjectPresent}");
        report.AppendLine($"{label} Logical payload length: {result.LogicalPayloadLength}; WMI package length: {result.WmiPackageLength}");
        report.AppendLine($"{label} Package: {Hex(result.RequestPackage)}");
        return result.Succeeded && result.InvokeReturnedNormally;
    }

    private string ObserveReadback(StringBuilder report, int block, byte[] expected, string label)
    {
        var other = block == 1 ? 2 : 1;
        var samples = new[] { ("T+0 ms", TimeSpan.Zero), ("T+50 ms", TimeSpan.FromMilliseconds(50)), ("T+250 ms", TimeSpan.FromMilliseconds(200)) };
        var immediateMatch = false;
        var laterMatch = false;
        var targetRead = false;
        for (var i = 0; i < samples.Length; i++)
        {
            var (name, wait) = samples[i];
            if (wait > TimeSpan.Zero) _delay(wait);
            var target = TryReadFan(block, out _, out var actual);
            var otherRead = TryReadFan(other, out _, out var otherActual);
            targetRead |= target;
            var matches = target && actual.Skip(1).Take(6).SequenceEqual(expected.Skip(1).Take(6));
            if (matches)
            {
                if (i == 0) immediateMatch = true;
                else laterMatch = true;
            }
            report.AppendLine($"{label} {name}: {(target ? Hex(actual) : "READ_FAILED")}; other fan: {(otherRead ? Hex(otherActual) : "READ_FAILED")}");
        }
        var verdict = FanProbeLogic.ClassifyReadback(immediateMatch, laterMatch, targetRead);
        report.AppendLine($"{label} readback verdict: {verdict}");
        return verdict;
    }

    private bool SetOwnership(StringBuilder report, bool enabled)
    {
        if (!_transport.TryGetAp(1, out var ap) || ap.Length == 0) { report.AppendLine("Ownership before: READ_FAILED"); return false; }
        var requested = enabled ? (byte)(ap[0] | 0x80) : (byte)(ap[0] & 0x7F);
        report.AppendLine($"Ownership before: 0x{ap[0]:X2}; requested: 0x{requested:X2}; Set_Data(212)");
        _hardwareWritesStarted = true;
        var ok = _diagnostics is null ? _transport.TrySetData(212, requested) : _diagnostics.InvokeFanDiagnostic("SetData", 212, [requested]).Succeeded;
        if (!ok || !_transport.TryGetAp(1, out var after) || after.Length == 0) return false;
        report.AppendLine($"Ownership after: 0x{after[0]:X2}; bit7={(after[0] & 0x80) != 0}");
        return (((after[0] & 0x80) != 0) == enabled);
    }

    private void ObserveOwnership(StringBuilder report)
    {
        report.AppendLine("=== SHORT LIVE OBSERVATION ===");
        foreach (var (name, wait) in new[] { ("T+0", TimeSpan.Zero), ("T+250", TimeSpan.FromMilliseconds(250)), ("T+750", TimeSpan.FromMilliseconds(500)) })
        {
            if (wait > TimeSpan.Zero) _delay(wait);
            report.AppendLine(name);
            ReadFan(report, 0, false); ReadFan(report, 1, false); ReadFan(report, 2, false);
            ReadPayload(report, "Get_AP(1)", "GetAp", 1, () => _transport.TryGetAp(1, out var p) ? p : null);
            ReadPayload(report, "Get_Data(212)", "GetData", 212, () => _transport.TryGetData(212, out var p) ? p : null);
        }
    }

    private bool RestoreOriginalTables(StringBuilder report)
    {
        var fan1 = _originalFan1 is null || WriteLogicalBlock(report, 1, _originalFan1, "Final Fan 1 restore");
        var fan2 = _originalFan2 is null || WriteLogicalBlock(report, 2, _originalFan2, "Final Fan 2 restore");
        return fan1 && fan2;
    }

    private bool RestoreFirmwareAuto(StringBuilder report)
    {
        var coolerOk = true;
        if (!_transport.TryGetData(152, out var cooler) || cooler.Length == 0)
        {
            coolerOk = false;
            report.AppendLine("Cooler Boost final verification: READ_FAILED");
        }
        else if ((cooler[0] & 0x80) != 0)
        {
            var cleared = _transport.TrySetData(152, (byte)(cooler[0] & 0x7F))
                && _transport.TryGetData(152, out var verifyCooler)
                && verifyCooler.Length > 0
                && (verifyCooler[0] & 0x80) == 0;
            coolerOk = cleared;
            report.AppendLine($"Cooler Boost final verification: {(cleared ? "OFF" : "FAIL")}");
        }
        else report.AppendLine("Cooler Boost final verification: OFF");

        var ownershipOk = false;
        if (!_transport.TryGetAp(1, out var ap) || ap.Length == 0)
            report.AppendLine("Ownership final verification: READ_FAILED");
        else
        {
            var requested = (byte)(ap[0] & 0x7F);
            var released = _diagnostics is null ? _transport.TrySetData(212, requested) : _diagnostics.InvokeFanDiagnostic("SetData", 212, [requested]).Succeeded;
            ownershipOk = released && _transport.TryGetAp(1, out var verify) && verify.Length > 0 && (verify[0] & 0x80) == 0;
            report.AppendLine($"Ownership final verification: {(ownershipOk ? "OFF" : "FAIL")}");
        }
        return coolerOk && ownershipOk;
    }

    private void WriteFinalSnapshot(StringBuilder report)
    {
        report.AppendLine("=== FINAL SNAPSHOT ===");
        ReadFan(report, 0, false);
        ReadFan(report, 1, false);
        ReadFan(report, 2, false);
        ReadPayload(report, "Get_AP(1)", "GetAp", 1, () => _transport.TryGetAp(1, out var p) ? p : null);
        ReadPayload(report, "Get_Data(152)", "GetData", 152, () => _transport.TryGetData(152, out var p) ? p : null);
        ReadPayload(report, "Get_Data(212)", "GetData", 212, () => _transport.TryGetData(212, out var p) ? p : null);
    }

    private (bool Success, byte[]? Logical) ReadFan(StringBuilder report, int block, bool required)
    {
        if (TryReadFan(block, out var raw, out var logical))
        {
            DescribeFan(report, block, raw, logical);
            if (block == 0 && logical.Length >= 2)
            {
                var denominator = logical[0] - logical[1];
                report.AppendLine($"Fan 0 RPM: {(denominator == 0 ? "unavailable / 0" : Math.Abs(480000 / denominator).ToString(CultureInfo.InvariantCulture))}");
            }
            return (true, logical);
        }
        report.AppendLine($"Get_Fan({block}): {(required ? "FAIL" : "UNAVAILABLE")}");
        return (false, null);
    }

    private bool ReadPayload(StringBuilder report, string name, string operation, int index, Func<byte[]?> fallback)
    {
        byte[]? payload = null;
        if (_diagnostics is not null)
        {
            var result = _diagnostics.InvokeFanDiagnostic(operation, index, null);
            payload = result.Succeeded ? result.Payload : null;
            report.AppendLine($"{name}: {(payload is null ? result.Stage : Hex(payload))}");
        }
        else
        {
            payload = fallback();
            report.AppendLine($"{name}: {(payload is null ? "READ_FAILED" : Hex(payload))}");
        }
        return payload is not null;
    }

    private bool TryReadFan(int block, out byte[] raw, out byte[] logical)
    {
        if (!_transport.TryGetFan(block, out raw)) { logical = []; return false; }
        return FanProbeLogic.TryNormalizeLogicalFanBlock(raw, out logical);
    }

    private void DescribeFan(StringBuilder report, int block, byte[] raw, byte[] logical) =>
        report.AppendLine($"Get_Fan({block}) Raw response payload: {raw.Length} bytes\nRaw HEX: {Hex(raw)}\nRaw DEC: {string.Join(" ", raw)}\nLogical fan block: {logical.Length} bytes\nLogical HEX: {Hex(logical)}\nLogical DEC: {string.Join(" ", logical)}\nDuties[1..6]: {string.Join(",", logical.Skip(1).Take(6))}\nbyte0: {logical[0]} byte7: {logical[7]}");

    private static byte[] WithDuties(byte[] original, byte[] duties)
    {
        var copy = (byte[])original.Clone();
        if (duties.Length == 6) duties.AsSpan().CopyTo(copy.AsSpan(1, 6));
        else duties.AsSpan(1, 6).CopyTo(copy.AsSpan(1, 6));
        return copy;
    }

    private static byte[] BuildPackageForReport(int block, byte[] logical)
    {
        var package = new byte[32]; package[0] = (byte)block; logical.CopyTo(package, 1); return package;
    }

    private static string Hex(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
}
