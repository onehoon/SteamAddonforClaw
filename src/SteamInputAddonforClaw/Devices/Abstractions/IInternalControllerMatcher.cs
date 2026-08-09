using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Devices.Abstractions;

public enum InternalControllerMatchStatus { Match, NoMatch, Indeterminate }

public sealed record InternalControllerMatchResult(InternalControllerMatchStatus Status, string Reason);

public sealed record InternalControllerMatchContext(
    ControllerDeviceInfo Device,
    IReadOnlyList<ControllerDeviceInfo> Ancestors);

public interface IInternalControllerMatcher
{
    InternalControllerMatchResult Match(InternalControllerMatchContext context);
}
