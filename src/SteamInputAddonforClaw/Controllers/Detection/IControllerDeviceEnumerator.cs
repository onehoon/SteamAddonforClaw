namespace SteamInputAddonforClaw.Controllers.Detection;

public interface IControllerDeviceEnumerator
{
    IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices();
}
