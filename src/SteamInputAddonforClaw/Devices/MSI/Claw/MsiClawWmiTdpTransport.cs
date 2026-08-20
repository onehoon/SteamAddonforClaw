using System.Management;
using System.Runtime.InteropServices;

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
        try
        {
            using var managementObject = new ManagementObject(Scope, Path, null);
            using var input = managementObject.GetMethodParameters("Set_Data");
            if (input?["Data"] is not ManagementBaseObject data)
                return false;

            data["Bytes"] = package;
            input["Data"] = data;
            using var _ = managementObject.InvokeMethod("Set_Data", input, null);
            return true;
        }
        catch (Exception exception) when (exception is ManagementException
            or COMException
            or UnauthorizedAccessException)
        {
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
                return false;

            data["Bytes"] = package;
            input["Data"] = data;
            using var output = managementObject.InvokeMethod(method, input, null);
            if (output?["Data"] is not ManagementBaseObject response
                || response["Bytes"] is not byte[] bytes
                || bytes.Length < 1
                || bytes[0] != 1)
                return false;

            payload = bytes[1..];
            return true;
        }
        catch (Exception exception) when (exception is ManagementException
            or COMException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
