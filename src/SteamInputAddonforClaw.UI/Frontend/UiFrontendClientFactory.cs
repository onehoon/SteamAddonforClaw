using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.UI.Frontend;

internal static class UiFrontendClientFactory
{
    internal static NamedPipeAddonFrontendClient Create() =>
        new(FrontendPipeEndpoint.CreateForCurrentUser());
}
