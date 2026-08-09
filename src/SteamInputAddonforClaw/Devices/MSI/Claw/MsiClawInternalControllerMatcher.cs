using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public sealed class MsiClawInternalControllerMatcher : IInternalControllerMatcher
{
    public InternalControllerMatchResult Match(InternalControllerMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !context.Device.Present
            ? new(InternalControllerMatchStatus.NoMatch, "DeviceNotPresent")
            : MsiClawHardware.IsKnownController(context.Device.VendorId, context.Device.ProductId)
                ? new(InternalControllerMatchStatus.Match, "KnownMsiClawVidPid")
                : new(InternalControllerMatchStatus.NoMatch, "NotMsiClawController");
    }
}
