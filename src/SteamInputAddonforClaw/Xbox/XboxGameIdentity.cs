namespace SteamInputAddonforClaw.Xbox;

internal sealed record XboxGameIdentity(
    string Key,
    string DisplayName,
    string? StoreId,
    string? PackageFamilyName)
{
    internal static string CreateKey(string? storeId, string? packageFamilyName, MicrosoftGameConfig config)
    {
        if (!string.IsNullOrWhiteSpace(storeId))
            return $"store:{storeId.Trim()}";
        if (!string.IsNullOrWhiteSpace(packageFamilyName))
            return $"pfn:{packageFamilyName.Trim()}";

        static string Normalize(string value) => Uri.EscapeDataString(value.Trim().ToUpperInvariant());
        return $"identity:{Normalize(config.IdentityName)}|{Normalize(config.IdentityPublisher)}|{Normalize(config.IdentityResourceId ?? string.Empty)}";
    }
}
