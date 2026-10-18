namespace SteamInputAddonforClaw.MsiLiveUpdateHelper;

internal static class MsiLiveUpdateProductRegistration
{
    internal static bool IsSupportedDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        return string.Equals(normalized, "MSI Center M", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "MSI Center M SDK", StringComparison.OrdinalIgnoreCase);
    }
}
