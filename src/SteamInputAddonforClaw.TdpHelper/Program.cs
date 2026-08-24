using System.Diagnostics;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using SteamInputAddonforClaw.TdpHelper;

if (args.Length != 1) return;
using var server = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try { await server.ConnectAsync(connectTimeout.Token); }
catch (OperationCanceledException) when (connectTimeout.IsCancellationRequested) { return; }
using var reader = new StreamReader(server);
using var writer = new StreamWriter(server) { AutoFlush = true };
var helperInfo = GetHelperInfo();

while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null) break;
    try
    {
        var request = JsonSerializer.Deserialize<Request>(line);
        if (request is null || !TdpHelperProtocol.IsSupported(request.Operation, request.Index))
        {
            await Write(new WmiResult(false, null, "PRE_WMI_PROTOCOL_FAIL", "ArgumentException", null, null, false, false, false, [], [], 0, 32));
            continue;
        }
        if (request.Operation == "GetHelperInfo")
        {
            await Write(new WmiResult(true, null, "HELPER_INFO", null, null, null, false, true, false, [], [], 0, 0));
            continue;
        }
        if (request.Operation == "GetMethodInventory")
        {
            await Write(new WmiResult(true, null, "METHOD_INVENTORY", null, null, null, false, true, false, GetRelevantMethods(), [], 0, 0));
            continue;
        }

        WmiResult result;
        try
        {
            result = request.Operation == "GetWmiVersion"
                ? InvokeWmiVersion()
                : await Task.Run(() => Invoke(
                    TdpHelperProtocol.GetWmiMethod(request.Operation),
                    request.Index,
                    request.Value,
                    request.Operation is "GetAp" or "GetFan" or "GetTemperature" or "GetThermal" or "GetData",
                    request.Payload is null ? null : Convert.FromBase64String(request.Payload))).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { return; }
        await Write(result);
    }
    catch (Exception exception)
    { await Write(Failure("PRE_WMI_PROTOCOL_FAIL", exception)); }
}

async Task Write(WmiResult result)
{
    var response = new Response(
        result.Ok,
        result.Payload is null ? null : Convert.ToBase64String(result.Payload),
        result.Stage,
        result.ExceptionType,
        result.HResult,
        result.ManagementStatus,
        result.UsedFallback,
        result.InvokeReturnedNormally,
        result.OutputObjectPresent,
        result.Methods,
        result.RequestPackage.Length == 0 ? null : Convert.ToBase64String(result.RequestPackage),
        result.LogicalPayloadLength,
        result.WmiPackageLength,
        helperInfo.ProcessId,
        helperInfo.Executable,
        helperInfo.Elevated,
        helperInfo.ProcessArchitecture,
        helperInfo.OsArchitecture);
    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
}

HelperInfo GetHelperInfo()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return new(Environment.ProcessId, Environment.ProcessPath ?? "SteamInputAddonforClaw.TdpHelper.exe",
        principal.IsInRole(WindowsBuiltInRole.Administrator), Environment.Is64BitProcess ? "x64" : "x86",
        RuntimeInformation.OSArchitecture.ToString());
}

string[] GetRelevantMethods()
{
    var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Get_WMI", "Get_Fan", "Set_Fan", "Get_Temperature", "Get_Thermal", "Get_AP", "Get_Data", "Set_Data",
        "Get_WMI_64", "Get_Thermal_64", "Set_Thermal_64", "Get_BIOS_64", "Set_BIOS_64", "Get_SMBUS_64", "Set_SMBUS_64"
    };
    using var type = new ManagementClass(@"\\.\root\WMI", "MSI_ACPI", null);
    return type.Methods.Cast<MethodData>().Select(m => m.Name).Where(relevant.Contains).OrderBy(x => x).ToArray();
}

WmiResult InvokeWmiVersion()
    => Invoke("Get_WMI", block: 1, value: 0, responseRequired: true, requestedPayload: null);

WmiResult Invoke(string method, int block, byte value, bool responseRequired, byte[]? requestedPayload)
{
    byte[] package;
    try { package = TdpHelperProtocol.BuildPackage(block, value, requestedPayload); }
    catch (Exception exception) { return Failure("PRE_WMI_PROTOCOL_FAIL", exception, false, false, false, [], 32); }

    try
    {
        using var obj = new ManagementObject(@"\\.\root\WMI", "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'", null);
        ManagementBaseObject? input = null;
        ManagementBaseObject? data = null;
        var usedFallback = false;
        Exception? fallbackCause = null;
        try { input = obj.GetMethodParameters(method); data = input?["Data"] as ManagementBaseObject; }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { fallbackCause = exception; }
        if (input is null || data is null)
        {
            usedFallback = true;
            try { input?.Dispose(); data?.Dispose(); input = obj.InvokeMethod("Get_WMI", null, null); data = input?["Data"] as ManagementBaseObject; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("WMI_INVOKE_FAIL", exception, usedFallback, false, false, package, 32); }
        }
        using (input)
        using (data)
        {
            if (input is null || data is null) return Failure("WMI_INVOKE_FAIL", null, usedFallback, false, false, package, 32);
            try { data["Bytes"] = package; input["Data"] = data; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("WMI_INVOKE_FAIL", exception, usedFallback, false, false, package, 32); }
            ManagementBaseObject? output;
            try { output = obj.InvokeMethod(method, input, null); }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("WMI_INVOKE_FAIL", exception, usedFallback, false, false, package, 32); }
            using (output)
            {
                if (!responseRequired)
                    return new(true, null, "WMI_INVOKE_OK", fallbackCause?.GetType().Name, fallbackCause?.HResult,
                        fallbackCause is ManagementException m1 ? (int)m1.ErrorCode : null, usedFallback,
                        true, output is not null, [], package, requestedPayload?.Length ?? 0, 32);
                if (output?["Data"] is not ManagementBaseObject response || response["Bytes"] is not byte[] bytes || bytes.Length < 2)
                    return Failure("WMI_INVOKE_FAIL", null, usedFallback, true, output is not null, package, 32);
                if (bytes[0] != 1) return Failure("WMI_INVOKE_FAIL", null, usedFallback, true, true, package, 32);
                return new(true, bytes[1..], "WMI_INVOKE_OK", fallbackCause?.GetType().Name, fallbackCause?.HResult,
                    fallbackCause is ManagementException m2 ? (int)m2.ErrorCode : null, usedFallback,
                    true, true, [], package, requestedPayload?.Length ?? 0, 32);
            }
        }
    }
    catch (Exception exception) when (IsExpectedWmiException(exception)) { return Failure("WMI_INVOKE_FAIL", exception, false, false, false, package, 32); }
}

bool IsExpectedWmiException(Exception exception) => exception is ManagementException or COMException or UnauthorizedAccessException;

WmiResult Failure(string stage, Exception? exception, bool usedFallback = false, bool invokeReturnedNormally = false,
    bool outputObjectPresent = false, byte[]? requestPackage = null, int wmiPackageLength = 32) =>
    new(false, null, stage, exception?.GetType().Name, exception?.HResult,
        exception is ManagementException management ? (int)management.ErrorCode : null, usedFallback,
        invokeReturnedNormally, outputObjectPresent, [], requestPackage ?? [], 0, wmiPackageLength);

record Request(string Operation, int Index, byte Value, string? Payload = null);
record Response(bool Ok, string? Payload, string? Stage = null, string? ExceptionType = null, int? HResult = null,
    int? ManagementStatus = null, bool UsedFallback = false, bool InvokeReturnedNormally = false,
    bool OutputObjectPresent = false, string[]? Methods = null, string? RequestPackage = null,
    int LogicalPayloadLength = 0, int WmiPackageLength = 0, int? HelperPid = null, string? HelperExecutable = null,
    bool? HelperElevated = null, string? ProcessArchitecture = null, string? OsArchitecture = null);
record HelperInfo(int ProcessId, string Executable, bool Elevated, string ProcessArchitecture, string OsArchitecture);
record WmiResult(bool Ok, byte[]? Payload, string? Stage, string? ExceptionType, int? HResult, int? ManagementStatus,
    bool UsedFallback, bool InvokeReturnedNormally, bool OutputObjectPresent, string[] Methods, byte[] RequestPackage,
    int LogicalPayloadLength, int WmiPackageLength);
