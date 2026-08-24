namespace SteamInputAddonforClaw.TdpHelper;

public static class TdpHelperProtocol
{
    public static byte[] BuildPackage(int block, byte value, byte[]? payload = null)
    {
        if ((uint)block > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(block));
        if (payload is not null && payload.Length != 8) throw new ArgumentException("Set_Fan requires an 8-byte fan payload.", nameof(payload));
        var package = new byte[32]; package[0] = (byte)block;
        if (payload is null) package[1] = value; else Array.Copy(payload, 0, package, 1, payload.Length);
        return package;
    }
    public static string GetWmiMethod(string operation) => operation switch
    {
        "GetAp" => "Get_AP",
        "SetData" => "Set_Data",
        "GetFan" => "Get_Fan",
        "SetFan" => "Set_Fan",
        "GetTemperature" => "Get_Temperature",
        "GetThermal" => "Get_Thermal",
        "GetData" => "Get_Data",
        "GetWmiVersion" => "Get_WMI",
        _ => throw new ArgumentException("Unsupported TDP operation.", nameof(operation))
    };

    public static bool IsSupported(string operation, int index) => operation switch
    {
        "GetAp" => index is 0 or 1 or 2,
        "SetData" => index is 80 or 81 or 152 or 210 or 212,
        "GetFan" => index is 0 or 1 or 2,
        "SetFan" => index is 1 or 2,
        "GetTemperature" => index is 1 or 2,
        "GetThermal" => index is 1 or 2,
        "GetData" => index is 152 or 210 or 212,
        "GetWmiVersion" => index == 1,
        "GetMethodInventory" or "GetHelperInfo" => index == 0,
        _ => false
    };
}
