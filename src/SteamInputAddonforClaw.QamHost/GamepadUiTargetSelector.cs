namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Picks the Steam GamepadUI page out of the full CEF DevTools target list.
/// Bounded, deterministic selection only: never falls back to "first target".
/// </summary>
public static class GamepadUiTargetSelector
{
    private const string LoopbackHost = "steamloopback.host";

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

        return uri.AbsolutePath.Contains("gamepadui", StringComparison.OrdinalIgnoreCase)
            || target.Title.Contains("GamepadUI", StringComparison.OrdinalIgnoreCase);
    }
}
