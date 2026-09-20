namespace SteamInputAddonforClaw.QamHost;

/// <summary>Identifies likely Steam surfaces that can own the outer QAM host geometry.</summary>
public static class QamHostTargetSelector
{
    private static readonly string[] KnownHostTitles =
    [
        "Menu",
        "Steam Big Picture Mode",
        "SharedJSContext",
        "SP Overlay",
    ];

    /// <summary>
    /// Returns bounded, read-only diagnostic targets. QuickAccess and notification targets are
    /// intentionally excluded because their internal content geometry is measured separately.
    /// </summary>
    public static IReadOnlyList<CdpTarget> SelectQamHostTargets(IReadOnlyList<CdpTarget> targets) =>
            targets
            .Where(IsQamHostTarget)
            .OrderBy(GetSortOrder)
            .ThenBy(target => target.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Url, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsQamHostTarget(CdpTarget target) =>
        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) &&
        (KnownHostTitles.Contains(target.Title, StringComparer.OrdinalIgnoreCase) ||
         HasNumericUidTitle(target.Title, "MainMenu_uid"));

    private static int GetSortOrder(CdpTarget target)
    {
        var exactOrder = Array.IndexOf(KnownHostTitles, target.Title);
        if (exactOrder >= 0) return exactOrder;
        return HasNumericUidTitle(target.Title, "MainMenu_uid") ? KnownHostTitles.Length : KnownHostTitles.Length + 1;
    }

    private static bool HasNumericUidTitle(string title, string prefix)
    {
        if (!title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = title[prefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsAsciiDigit);
    }
}
