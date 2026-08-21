using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawWmiTdpTransport : IMsiClawTdpTransport
{
    private const string Scope = @"\\.\root\WMI";
    private const string Path = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";
    private const int PackageLength = 32;

    public bool TryGetAp(int index, out byte[] payload) => TryInvoke("Get_AP", BuildPackage(index, 0), "Index", index, out payload);

    public bool TrySetData(int block, byte value)
    {
        var package = BuildPackage(block, value);
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method call started", ("Method", "Set_Data"), ("Block", block), ("PackageLength", package.Length));
        ManagementObject managementObject;
        try { managementObject = new ManagementObject(Scope, Path, null); }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure("Set_Data", "CreateManagementObject", exception, "Block", block); return false; }
        using (managementObject)
        {
        ManagementBaseObject? input;
        try { input = managementObject.GetMethodParameters("Set_Data"); }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure("Set_Data", "GetMethodParameters", exception, "Block", block); return false; }
        using (input)
        {
            ManagementBaseObject? data;
            try { data = input?["Data"] as ManagementBaseObject; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure("Set_Data", "InputDataExtraction", exception, "Block", block); return false; }
            if (data is null) { LogStage("Set_Data", "InputDataMissing", "Block", block); return false; }
            try { data["Bytes"] = package; input!["Data"] = data; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure("Set_Data", "InputSetup", exception, "Block", block); return false; }
            try { using var output = managementObject.InvokeMethod("Set_Data", input, null); return true; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure("Set_Data", "InvokeMethod", exception, "Block", block); return false; }
        }
        }
    }

    internal static byte[] BuildPackage(int block, byte value)
    {
        if ((uint)block > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(block));
        var package = new byte[PackageLength]; package[0] = (byte)block; package[1] = value; return package;
    }

    private static bool TryInvoke(string method, byte[] package, string field, int fieldValue, out byte[] payload)
    {
        payload = [];
        ManagementObject managementObject;
        try { managementObject = new ManagementObject(Scope, Path, null); }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "CreateManagementObject", exception, field, fieldValue); return false; }
        using (managementObject)
        {
        ManagementBaseObject? input;
        try { input = managementObject.GetMethodParameters(method); }
        catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "GetMethodParameters", exception, field, fieldValue); return false; }
        using (input)
        {
            ManagementBaseObject? data;
            try { data = input?["Data"] as ManagementBaseObject; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "InputDataExtraction", exception, field, fieldValue); return false; }
            if (data is null) { LogStage(method, "InputDataMissing", field, fieldValue); return false; }
            try { data["Bytes"] = package; input!["Data"] = data; }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "InputSetup", exception, field, fieldValue); return false; }
            ManagementBaseObject? output;
            try { output = managementObject.InvokeMethod(method, input, null); }
            catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "InvokeMethod", exception, field, fieldValue); return false; }
            using (output)
            {
                ManagementBaseObject? response;
                byte[]? bytes;
                try
                {
                    response = output?["Data"] as ManagementBaseObject;
                    if (response is null) { LogStage(method, "OutputDataMissing", field, fieldValue); return false; }
                    bytes = response["Bytes"] as byte[];
                }
                catch (Exception exception) when (IsExpectedWmiException(exception)) { LogFailure(method, "OutputExtraction", exception, field, fieldValue); return false; }
                if (bytes is null) { LogStage(method, "OutputBytesMissing", field, fieldValue); return false; }
                if (bytes.Length == 0) { LogStage(method, "OutputBytesEmpty", field, fieldValue); return false; }
                AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method response", ("Method", method), (field, fieldValue), ("BytesLength", bytes.Length), ("Flag", $"0x{bytes[0]:X2}"), ("PayloadLength", bytes.Length - 1));
                if (bytes[0] != 1) { LogStage(method, "OutputFlagRejected", field, fieldValue); return false; }
                payload = bytes[1..]; return true;
            }
        }
    }
    }

    private static bool IsExpectedWmiException(Exception exception) => exception is ManagementException or COMException or UnauthorizedAccessException;
    private static void LogStage(string method, string stage, string field, object value) => AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), (field, value), ("Stage", stage));
    private static void LogFailure(string method, string stage, Exception exception, string field, object value)
    {
        var values = new List<(string Name, object? Value)> { ("Method", method), (field, value), ("Stage", stage), ("ExceptionType", exception.GetType().Name), ("HResult", $"0x{exception.HResult:X8}"), ("Message", exception.Message) };
        if (exception is ManagementException management) values.Add(("ManagementStatus", management.ErrorCode));
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", values.ToArray());
    }
}
