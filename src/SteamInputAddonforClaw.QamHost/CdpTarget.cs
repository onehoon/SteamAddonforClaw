namespace SteamInputAddonforClaw.QamHost;

/// <summary>One entry from Steam's CEF DevTools <c>/json</c> target list.</summary>
public sealed record CdpTarget(
    string Id,
    string Type,
    string Title,
    string Url,
    string? WebSocketDebuggerUrl);
