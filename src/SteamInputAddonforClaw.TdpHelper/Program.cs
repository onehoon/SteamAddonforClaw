using System.IO.Pipes;
using System.Text.Json;
using System.Management;

if (args.Length != 1) return;
using var server = new NamedPipeServerStream(args[0], PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
await server.WaitForConnectionAsync();
using var reader = new StreamReader(server);
using var writer = new StreamWriter(server) { AutoFlush = true };
while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null) break;
    try
    {
        var request = JsonSerializer.Deserialize<Request>(line);
        if (request is null || request.Operation is not ("GetAp" or "SetData") || request.Index is < 0 or > 255) { await writer.WriteLineAsync(JsonSerializer.Serialize(new Response(false, null))); continue; }
        var result = Invoke(request.Operation, request.Index, request.Value, request.Operation == "GetAp");
        await writer.WriteLineAsync(JsonSerializer.Serialize(new Response(result.Ok, result.Payload is null ? null : Convert.ToBase64String(result.Payload))));
    }
    catch { await writer.WriteLineAsync(JsonSerializer.Serialize(new Response(false, null))); }
}

static (bool Ok, byte[]? Payload) Invoke(string method, int block, byte value, bool responseRequired)
{
    using var obj = new ManagementObject(@"\\.\root\WMI", "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'", null);
    ManagementBaseObject? input = null;
    ManagementBaseObject? data = null;
    try { input = obj.GetMethodParameters(method); data = input?["Data"] as ManagementBaseObject; }
    catch (ManagementException) { }
    if (input is null || data is null)
    {
        input?.Dispose(); data?.Dispose(); input = obj.InvokeMethod("Get_WMI", null, null);
        data = input?["Data"] as ManagementBaseObject;
    }
    using (input)
    using (data)
    {
        if (input is null || data is null) throw new InvalidOperationException();
    data["Bytes"] = BuildPackage(block, value); input["Data"] = data;
    using var output = obj.InvokeMethod(method, input, null);
    if (!responseRequired) return (true, null);
    if (output?["Data"] is not ManagementBaseObject response || response["Bytes"] is not byte[] bytes || bytes.Length < 2 || bytes[0] != 1) return (false, null);
    return (true, bytes[1..]);
    }
}
static byte[] BuildPackage(int block, byte value) { var p = new byte[32]; p[0] = (byte)block; p[1] = value; return p; }
record Request(string Operation, int Index, byte Value);
record Response(bool Ok, string? Payload);
