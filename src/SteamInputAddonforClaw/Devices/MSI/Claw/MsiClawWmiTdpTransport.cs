using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawWmiTdpTransport : IMsiClawTdpTransport
{
    private const string Scope = @"\\.\root\WMI";
    private const string Path = "MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'";
    private const int PackageLength = 32;

    public bool TryGetAp(int index, out byte[] payload) => TryInvoke("Get_AP", index, out payload);

    public bool TrySetData(int block, byte value)
    {
        var package = BuildPackage(block, value);
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method call started", ("Method", "Set_Data"), ("Block", block), ("PackageLength", package.Length));
        try
        {
            using var managementObject = new ManagementObject(Scope, Path, null);
            using var input = managementObject.GetMethodParameters("Set_Data");
            if (input?["Data"] is not ManagementBaseObject data)
            {
                AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", "Set_Data"), ("Block", block), ("Stage", "InputDataMissing"));
                return false;
            }

            data["Bytes"] = package;
            input["Data"] = data;
            using var _ = managementObject.InvokeMethod("Set_Data", input, null);
            return true;
        }
        catch (Exception exception) when (exception is ManagementException
            or COMException
            or UnauthorizedAccessException)
        {
            LogFailure("Set_Data", "InvokeMethod", exception, block);
            return false;
        }
    }

    internal static byte[] BuildPackage(int block, byte value)
    {
        if ((uint)block > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(block));

        var package = new byte[PackageLength];
        package[0] = (byte)block;
        package[1] = value;
        return package;
    }

    private static bool TryInvoke(string method, int index, out byte[] payload)
    {
        var package = BuildPackage(index, 0);
        return TryInvoke(method, package, out payload);
    }

    private static bool TryInvoke(string method, byte[] package, out byte[] payload)
    {
        payload = [];
        try
        {
            using var managementObject = new ManagementObject(Scope, Path, null);
            using var input = managementObject.GetMethodParameters(method);
            if (input?["Data"] is not ManagementBaseObject data)
            {
                AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), ("Stage", "InputDataMissing"));
                return false;
            }

            data["Bytes"] = package;
            input["Data"] = data;
            using var output = managementObject.InvokeMethod(method, input, null);
            if (output?["Data"] is not ManagementBaseObject response
                || response["Bytes"] is not byte[] bytes)
            {
                AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), ("Stage", "OutputDataMissing"));
                return false;
            }
            if (bytes.Length == 0) { AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), ("Stage", "OutputBytesEmpty")); return false; }
            AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method response", ("Method", method), ("Index", package[0]), ("BytesLength", bytes.Length), ("Flag", $"0x{bytes[0]:X2}"), ("PayloadLength", bytes.Length - 1));
            if (bytes[0] != 1) { AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", ("Method", method), ("Stage", "OutputFlagRejected"), ("Flag", $"0x{bytes[0]:X2}")); return false; }

            payload = bytes[1..];
            return true;
        }
        catch (Exception exception) when (exception is ManagementException
            or COMException
            or UnauthorizedAccessException)
        {
            LogFailure(method, "InvokeMethod", exception, package[0]);
            return false;
        }
    }

    private static void LogFailure(string method, string stage, Exception exception, int index)
    {
        var values = new List<(string Name, object? Value)> { ("Method", method), ("Index", index), ("Stage", stage), ("ExceptionType", exception.GetType().Name), ("HResult", $"0x{exception.HResult:X8}"), ("Message", exception.Message) };
        if (exception is ManagementException management) values.Add(("ManagementStatus", management.ErrorCode));
        AppLog.Debug("Profiles.Tdp.Wmi", "MSI_ACPI method failed", values.ToArray());
    }
}
