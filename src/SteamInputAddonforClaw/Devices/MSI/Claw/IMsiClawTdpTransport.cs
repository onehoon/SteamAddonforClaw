namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal interface IMsiClawTdpTransport
{
    bool TryGetAp(int index, out byte[] payload);
    bool TrySetData(int block, byte value);
}
