using System.IO.Pipes;
using System.Text.Json;
using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.TdpHelper;

if (args.Length != 1) return;
using var server = new NamedPipeServerStream(args[0], PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try { await server.WaitForConnectionAsync(connectTimeout.Token); }
catch (OperationCanceledException) when (connectTimeout.IsCancellationRequested) { return; }
using var reader = new StreamReader(server);
using var writer = new StreamWriter(server) { AutoFlush = true };
while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null) break;
    try
    {
        var request = JsonSerializer.Deserialize<Request>(line);
        if (request is null || !TdpHelperProtocol.IsSupported(request.Operation, request.Index)) { await writer.WriteLineAsync(JsonSerializer.Serialize(new Response(false, null))); continue; }
        WmiResult result;
        try
        {
            result = await Task.Run(() => Invoke(
                TdpHelperProtocol.GetWmiMethod(request.Operation),
                request.Index,
                request.Value,
                request.Operation == "GetAp")).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            return;
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(new Response(result.Ok, result.Payload is null ? null : Convert.ToBase64String(result.Payload), result.Stage, result.ExceptionType, result.HResult, result.ManagementStatus, result.UsedFallback)));
    }
        catch (Exception exception)
        { await writer.WriteLineAsync(JsonSerializer.Serialize(Failure("Protocol", exception))); }
}

static WmiResult Invoke(string method, int block, byte value, bool responseRequired)
{
    try
    {
        using var obj = new ManagementObject(@"\\.\root\WMI", "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'", null);
        ManagementBaseObject? input = null;
        ManagementBaseObject? data = null;
        var usedFallback = false;
        try { input = obj.GetMethodParameters(method); data = input?["Data"] as ManagementBaseObject; }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { }
        if (input is null || data is null)
        {
            usedFallback = true;
            try
            {
                input?.Dispose(); data?.Dispose(); input = obj.InvokeMethod("Get_WMI", null, null);
                data = input?["Data"] as ManagementBaseObject;
            }
            catch (Exception exception) when (IsExpectedWmiException(exception))
            { return Failure("GetWmiFallback", exception, usedFallback); }
        }
        using (input)
        using (data)
        {
            if (input is null || data is null) return Failure("InputDataUnavailable", null, usedFallback);
            try { data["Bytes"] = BuildPackage(block, value); input["Data"] = data; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("InputSetup", exception, usedFallback); }
            using var output = obj.InvokeMethod(method, input, null);
            if (!responseRequired) return new(true, null, null, null, null, null, usedFallback);
            if (output?["Data"] is not ManagementBaseObject response || response["Bytes"] is not byte[] bytes || bytes.Length < 2)
                return Failure("OutputDataMissing", null, usedFallback);
            if (bytes[0] != 1) return Failure("OutputFlagRejected", null, usedFallback);
            return new(true, bytes[1..], null, null, null, null, usedFallback);
        }
    }
    catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("InvokeMethod", exception); }
}
static bool IsExpectedWmiException(Exception exception) =>
    exception is ManagementException or COMException or UnauthorizedAccessException;
static WmiResult Failure(string stage, Exception? exception, bool usedFallback = false) => new(false, null, stage, exception?.GetType().Name, exception?.HResult, exception is ManagementException management ? (int)management.ErrorCode : null, usedFallback);
static byte[] BuildPackage(int block, byte value) { var p = new byte[32]; p[0] = (byte)block; p[1] = value; return p; }
record Request(string Operation, int Index, byte Value);
record Response(bool Ok, string? Payload, string? Stage = null, string? ExceptionType = null, int? HResult = null, int? ManagementStatus = null, bool UsedFallback = false);
record WmiResult(bool Ok, byte[]? Payload, string? Stage, string? ExceptionType, int? HResult, int? ManagementStatus, bool UsedFallback);
