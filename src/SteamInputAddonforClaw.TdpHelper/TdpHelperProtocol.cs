namespace SteamInputAddonforClaw.TdpHelper;

public static class TdpHelperProtocol
{
    public static string GetWmiMethod(string operation) => operation switch
    {
        "GetAp" => "Get_AP",
        "SetData" => "Set_Data",
        _ => throw new ArgumentException("Unsupported TDP operation.", nameof(operation))
    };

    public static bool IsSupported(string operation, int index) => operation switch
    {
        "GetAp" => index == 0,
        "SetData" => index is 80 or 81 or 210,
        _ => false
    };
}
