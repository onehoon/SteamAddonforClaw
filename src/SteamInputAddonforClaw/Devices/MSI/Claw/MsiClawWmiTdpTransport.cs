using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawWmiTdpTransport : IMsiClawTdpTransport
{
    private const string Scope = @"\\.\root\WMI";
    private const string Path = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";
    private const int PackageLength = 32;

    public bool TryGetAp(int index, out byte[] payload) =>
        TryInvoke("Get_AP", BuildPackage(index, 0), "Index", index, true, out payload);

    public bool TrySetData(int block, byte value) =>
        TryInvoke("Set_Data", BuildPackage(block, value), "Block", block, false, out _);

    internal static byte[] BuildPackage(int block, byte value)
    {
        if ((uint)block > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(block));
        var package = new byte[PackageLength];
        package[0] = (byte)block;
        package[1] = value;
        return package;
    }

    private static bool TryInvoke(string method, byte[] package, string field, int fieldValue, bool requireResponsePayload, out byte[] payload)
    {
        payload = [];
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method call started", ("Method", method), (field, fieldValue), ("PackageLength", package.Length));
        ManagementObject managementObject;
        try { managementObject = new ManagementObject(Scope, Path, null); }
        catch (Exception exception) when (IsExpectedWmiException(exception))
        { LogFailure(method, "CreateManagementObject", exception, field, fieldValue); return false; }

        using (managementObject)
        {
            ManagementBaseObject? input = null;
            ManagementBaseObject? data = null;
            try { input = managementObject.GetMethodParameters(method); data = input?["Data"] as ManagementBaseObject; }
            catch (Exception exception) when (IsExpectedWmiException(exception))
            { LogFailure(method, "GetMethodParameters", exception, field, fieldValue); }

            if (input is null || data is null)
            {
                LogStage(method, "GetWmiFallback", field, fieldValue);
                input?.Dispose();
                input = null;
                data = null;
                try
                {
                    input = managementObject.InvokeMethod("Get_WMI", null, null);
                    data = input?["Data"] as ManagementBaseObject;
                    if (input is not null && data is not null)
                        LogFallbackSucceeded(method, field, fieldValue);
                }
                catch (Exception exception) when (IsExpectedWmiException(exception))
                { LogFailure(method, "GetWmiFallback", exception, field, fieldValue); }
            }

            using (input)
            using (data)
            {
                if (input is null || data is null)
                { LogStage(method, "InputDataUnavailable", field, fieldValue); return false; }
                try { data["Bytes"] = package; input["Data"] = data; }
                catch (Exception exception) when (IsExpectedWmiException(exception))
                { LogFailure(method, "InputSetup", exception, field, fieldValue); return false; }

                ManagementBaseObject? output;
                try { output = managementObject.InvokeMethod(method, input, null); }
                catch (Exception exception) when (IsExpectedWmiException(exception))
                { LogFailure(method, "InvokeMethod", exception, field, fieldValue); return false; }

                using (output)
                {
                    if (!requireResponsePayload)
                        return true;

                    try
                    {
                        if (output?["Data"] is not ManagementBaseObject response ||
                            response["Bytes"] is not byte[] bytes || bytes.Length < 1)
                        { LogStage(method, "OutputDataMissing", field, fieldValue); return false; }
                        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method response", ("Method", method), (field, fieldValue), ("BytesLength", bytes.Length), ("Flag", $"0x{bytes[0]:X2}"), ("PayloadLength", bytes.Length - 1));
                        if (bytes[0] != 1)
                        { LogStage(method, "OutputFlagRejected", field, fieldValue); return false; }
                        payload = bytes[1..];
                        return true;
                    }
                    catch (Exception exception) when (IsExpectedWmiException(exception))
                    { LogFailure(method, "OutputExtraction", exception, field, fieldValue); return false; }
                }
            }
        }
    }

    private static bool IsExpectedWmiException(Exception exception) =>
        exception is ManagementException or COMException or UnauthorizedAccessException;

    private static void LogStage(string method, string stage, string field, object value) =>
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), (field, value), ("Stage", stage));

    private static void LogFallbackSucceeded(string method, string field, object value) =>
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI compatibility fallback succeeded",
            ("Method", method), (field, value), ("Stage", "GetWmiFallback"));

    private static void LogFailure(string method, string stage, Exception exception, string field, object value)
    {
        var values = new List<(string Name, object? Value)> { ("Method", method), (field, value), ("Stage", stage), ("ExceptionType", exception.GetType().Name), ("HResult", $"0x{exception.HResult:X8}"), ("Message", exception.Message) };
        if (exception is ManagementException management) values.Add(("ManagementStatus", management.ErrorCode));
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", values.ToArray());
    }
}
