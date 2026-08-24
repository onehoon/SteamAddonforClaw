using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class TdpHelperClient : IAsyncDisposable, IMsiFanDiagnosticTransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);
    private readonly Lock _sync = new();
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _process;
    private readonly string _pipeName = $"SteamInputAddonforClaw.Tdp.{Environment.ProcessId}.{Guid.NewGuid():N}";

    public bool TryGetAp(int index, out byte[] payload) => Invoke(new("GetAp", index, 0), out payload);
    public bool TrySetData(int block, byte value) => Invoke(new("SetData", block, value), out _);
    public bool TryGetFan(int block, out byte[] payload) => Invoke(new("GetFan", block, 0), out payload);
    public bool TrySetFan(int block, byte[] payload) => Invoke(new("SetFan", block, 0, Convert.ToBase64String(payload)), out _);
    public bool TryGetTemperature(int index, out byte[] payload) => Invoke(new("GetTemperature", index, 0), out payload);
    public bool TryGetThermal(int index, out byte[] payload) => Invoke(new("GetThermal", index, 0), out payload);
    public bool TryGetData(int block, out byte[] payload) => Invoke(new("GetData", block, 0), out payload);

    public bool TryGetHelperInfo(out MsiFanHelperInfo info)
    {
        var response = InvokeDetailed(new("GetHelperInfo", 0, 0));
        info = response.HelperPid is int pid && response.HelperExecutable is not null && response.HelperElevated is bool elevated
            ? new(pid, response.HelperExecutable, elevated, response.ProcessArchitecture ?? "unknown", response.OsArchitecture ?? "unknown")
            : new(0, "unknown", false, "unknown", "unknown");
        return response.Ok;
    }

    public bool TryGetWmiVersion(out MsiFanWmiVersion version)
    {
        var response = InvokeDetailed(new("GetWmiVersion", 0, 0));
        var payload = response.Payload is null ? [] : Convert.FromBase64String(response.Payload);
        byte? major = payload.Length >= 2 && payload[0] >= 2 ? payload[0] : null;
        version = new(response.Ok, payload, major, major.HasValue ? payload[1] : null,
            response.Stage ?? "HelperProtocol", response.ExceptionType, response.HResult, response.ManagementStatus, response.UsedFallback);
        return response.Ok;
    }

    public bool TryGetMethodInventory(out string[] methods)
    {
        var response = InvokeDetailed(new("GetMethodInventory", 0, 0));
        methods = response.Methods ?? [];
        return response.Ok;
    }

    public MsiFanOperationResult InvokeFanDiagnostic(string operation, int block, byte[]? payload)
    {
        Response response;
        try
        {
            var request = EncodeDiagnosticRequest(operation, block, payload);
            response = InvokeDetailed(new(operation, block, request.Value, request.EncodedPayload));
        }
        catch (ArgumentException exception)
        {
            return new(false, operation, operation switch
            {
                "GetAp" => "Get_AP", "SetData" => "Set_Data", "GetFan" => "Get_Fan", "SetFan" => "Set_Fan",
                "GetTemperature" => "Get_Temperature", "GetThermal" => "Get_Thermal", "GetData" => "Get_Data", _ => operation
            }, block, [], [], 0, 32, "PRE_WMI_PROTOCOL_FAIL", exception.GetType().Name,
                exception.HResult, null, false, false, false);
        }
        return new(response.Ok, operation, operation switch
        {
            "GetAp" => "Get_AP", "SetData" => "Set_Data", "GetFan" => "Get_Fan", "SetFan" => "Set_Fan",
            "GetTemperature" => "Get_Temperature", "GetThermal" => "Get_Thermal", "GetData" => "Get_Data", _ => operation
        }, block, response.Payload is null ? [] : Convert.FromBase64String(response.Payload), response.RequestPackage is null ? [] : Convert.FromBase64String(response.RequestPackage),
            response.LogicalPayloadLength, response.WmiPackageLength, response.Stage ?? "HelperProtocol", response.ExceptionType,
            response.HResult, response.ManagementStatus, response.UsedFallback, response.InvokeReturnedNormally, response.OutputObjectPresent);
    }

    internal static (byte Value, string? EncodedPayload) EncodeDiagnosticRequest(string operation, int block, byte[]? payload)
    {
        if (operation == "SetData")
        {
            if (payload is not { Length: 1 })
                throw new ArgumentException("Set_Data requires one value byte.", nameof(payload));
            return (payload[0], null);
        }

        return (0, payload is null ? null : Convert.ToBase64String(payload));
    }

    private bool Invoke(Request request, out byte[] payload)
    {
        payload = [];
        lock (_sync)
        {
            try
            {
                var response = InvokeDetailedUnderLock(request);
                if (response?.Ok != true)
                {
                    AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI helper operation failed",
                        ("Operation", request.Operation), ("Index", request.Index),
                        ("Stage", response?.Stage ?? "HelperProtocol"),
                        ("ExceptionType", response?.ExceptionType),
                        ("HResult", response?.HResult is int hr ? $"0x{hr:X8}" : null),
                        ("ManagementStatus", response?.ManagementStatus),
                        ("UsedFallback", response?.UsedFallback));
                    return false;
                }
                if (response.UsedFallback)
                    AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI compatibility fallback succeeded",
                        ("Method", request.Operation), ("Index", request.Index),
                        ("Stage", response.Stage ?? "GetWmiFallback"),
                        ("ExceptionType", response.ExceptionType),
                        ("HResult", response.HResult is int hr ? $"0x{hr:X8}" : null),
                        ("ManagementStatus", response.ManagementStatus));
                payload = response.Payload is null ? [] : Convert.FromBase64String(response.Payload);
                return request.Operation is "SetData" or "SetFan" || payload.Length > 0;
            }
            catch { CloseUnderLock(); return false; }
        }
    }

    private Response InvokeDetailed(Request request)
    {
        lock (_sync)
        {
            try { return InvokeDetailedUnderLock(request); }
            catch { CloseUnderLock(); return new(false, null, "HelperProtocol"); }
        }
    }

    private Response InvokeDetailedUnderLock(Request request)
    {
        EnsureConnected();
        _writer!.WriteLine(JsonSerializer.Serialize(request));
        _writer.Flush();
        using var responseTimeout = new CancellationTokenSource(ResponseTimeout);
        var responseLine = _reader!.ReadLineAsync(responseTimeout.Token).AsTask().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<Response>(responseLine ?? "") ?? new(false, null, "HelperProtocol");
    }

    private void EnsureConnected()
    {
        if (_pipe?.IsConnected == true) return;
        CloseUnderLock();
        _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.TdpHelper.exe");
            _process = Process.Start(new ProcessStartInfo(path, _pipeName) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory })
                ?? throw new InvalidOperationException("TDP helper could not be started.");
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _pipe.WaitForConnectionAsync(connectTimeout.Token).GetAwaiter().GetResult();
            _reader = new StreamReader(_pipe);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
        }
        catch
        {
            CloseUnderLock();
            throw;
        }
    }

    private void CloseUnderLock()
    {
        try { _pipe?.Dispose(); } catch { }
        try { if (_process is { HasExited: false }) { _process.Kill(); _process.WaitForExit(1000); } } catch { }
        _pipe = null; _reader = null; _writer = null; _process?.Dispose(); _process = null;
    }

    public ValueTask DisposeAsync() { lock (_sync) CloseUnderLock(); return ValueTask.CompletedTask; }
    private sealed record Request(string Operation, int Index, byte Value, string? Payload = null);
    private sealed record Response(bool Ok, string? Payload, string? Stage = null, string? ExceptionType = null, int? HResult = null,
        int? ManagementStatus = null, bool UsedFallback = false, bool InvokeReturnedNormally = false,
        bool OutputObjectPresent = false, string[]? Methods = null, string? RequestPackage = null,
        int LogicalPayloadLength = 0, int WmiPackageLength = 0, int? HelperPid = null, string? HelperExecutable = null,
        bool? HelperElevated = null, string? ProcessArchitecture = null, string? OsArchitecture = null);
}
