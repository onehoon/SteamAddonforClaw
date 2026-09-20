namespace SteamInputAddonforClaw.QamHost;

/// <summary>Identifies Steam's separate Quick Access renderer targets in the CEF target list.</summary>
public static class QuickAccessTargetSelector
{
    private const string QuickAccessTitlePrefix = "QuickAccess_uid";

    /// <summary>
    /// Returns every usable Quick Access page. Multiple entries are expected while a game overlay
    /// and the Big Picture surface coexist, so this method intentionally does not require uniqueness.
    /// </summary>
    public static IReadOnlyList<CdpTarget> SelectQuickAccessTargets(IReadOnlyList<CdpTarget> targets) =>
        targets
            .Where(IsQuickAccessTarget)
            .OrderBy(target => target.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Url, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsQuickAccessTarget(CdpTarget target)
    {
        if (!string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) ||
            !HasNumericUidTitle(target.Title))
        {
            return false;
        }

        return Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) &&
               uri.Query.Contains("parentpopup=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNumericUidTitle(string title)
    {
        if (!title.StartsWith(QuickAccessTitlePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = title[QuickAccessTitlePrefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsAsciiDigit);
    }
}
