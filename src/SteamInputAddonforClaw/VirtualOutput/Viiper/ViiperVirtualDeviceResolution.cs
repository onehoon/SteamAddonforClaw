using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum ViiperVirtualDeviceResolutionStatus { Resolved, NoNewCandidate, Ambiguous }

internal sealed record ViiperVirtualDeviceResolution(ViiperVirtualDeviceResolutionStatus Status, IReadOnlyList<ControllerDeviceInfo> Devices, string Reason)
{
    internal bool Succeeded => Status == ViiperVirtualDeviceResolutionStatus.Resolved;
}
