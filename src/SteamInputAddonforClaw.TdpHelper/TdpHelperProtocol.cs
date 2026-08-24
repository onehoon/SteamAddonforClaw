namespace SteamInputAddonforClaw.TdpHelper;

public static class TdpHelperProtocol
{
    public static string GetWmiMethod(string operation) => operation switch
    {
        "GetAp" => "Get_AP",
        "SetData" => "Set_Data",
        "GetFan" => "Get_Fan",
        "SetFan" => "Set_Fan",
        "GetTemperature" => "Get_Temperature",
        "GetData" => "Get_Data",
        _ => throw new ArgumentException("Unsupported TDP operation.", nameof(operation))
    };

    public static bool IsSupported(string operation, int index) => operation switch
    {
        "GetAp" => index is 0 or 1 or 2 or 212,
        "SetData" => index is 80 or 81 or 210 or 212,
        "GetFan" => index is 0 or 1 or 2,
        "SetFan" => index is 0 or 1 or 2,
        "GetTemperature" => index is 1 or 2,
        "GetData" => index == 152,
        _ => false
    };
}
