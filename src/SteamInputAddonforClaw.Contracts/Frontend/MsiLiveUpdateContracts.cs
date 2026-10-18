namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendMsiLiveUpdateCatalogState { NotChecked, Checking, Completed, PartialFailure, Unavailable, Failed }
public enum FrontendMsiLiveUpdateCatalogCategory { Bios, Firmware, Driver, Utility }
public enum FrontendMsiLiveUpdateCatalogCategoryState { Success, Empty, Failed }

public sealed record FrontendMsiLiveUpdateCatalogEntry(string Name, string? Version, string? ReleaseDate, string? Description);
public sealed record FrontendMsiLiveUpdateCatalogCategorySnapshot(
    FrontendMsiLiveUpdateCatalogCategory Category,
    FrontendMsiLiveUpdateCatalogCategoryState State,
    IReadOnlyList<FrontendMsiLiveUpdateCatalogEntry> Entries,
    string? ErrorKind = null);
public sealed record FrontendMsiLiveUpdateCatalogSnapshot(
    FrontendMsiLiveUpdateCatalogState State,
    string? ProductTitle,
    string? CountryCode,
    string? OperatingSystem,
    IReadOnlyList<FrontendMsiLiveUpdateCatalogCategorySnapshot> Categories,
    DateTimeOffset? CheckedAtUtc,
    string? ErrorKind = null)
{
    public static FrontendMsiLiveUpdateCatalogSnapshot Unavailable(string errorKind = "CatalogUnavailable") =>
        new(FrontendMsiLiveUpdateCatalogState.Unavailable, null, null, null, [], null, errorKind);
}
