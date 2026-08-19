namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Picks the Steam GamepadUI page out of the full CEF DevTools target list.
/// Bounded, deterministic selection only: never falls back to "first target".
/// </summary>
public static class GamepadUiTargetSelector
{
    private const string LoopbackHost = "steamloopback.host";

    // Steam's GamepadUI page does not identify itself with "gamepadui" in the URL or title.
    // It is a steamloopback.host page under /routes/... or the /index.html shared context,
    // presented under one of Steam's shared-context titles.
    private static readonly string[] SharedContextTitles =
    [
        "SharedJSContext",
        "Steam Shared Context presented by Valve™",
        "Steam",
        "SP",
    ];

    /// <summary>Returns the GamepadUI target, or null if none can be identified confidently.</summary>
    public static CdpTarget? SelectGamepadUiTarget(IReadOnlyList<CdpTarget> targets)
    {
        var candidates = targets
            .Where(IsPageTarget)
            .Where(HasUsableWebSocketUrl)
            .Where(IsGamepadUiCandidate)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsPageTarget(CdpTarget target) =>
        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase);

    private static bool HasUsableWebSocketUrl(CdpTarget target) =>
        !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl);

    private static bool IsGamepadUiCandidate(CdpTarget target)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Host.Equals(LoopbackHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathMatches = uri.AbsolutePath.StartsWith("/routes/", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Equals("/index.html", StringComparison.OrdinalIgnoreCase);

        var titleMatches = SharedContextTitles.Contains(target.Title, StringComparer.OrdinalIgnoreCase);

        return pathMatches && titleMatches;
    }
}
