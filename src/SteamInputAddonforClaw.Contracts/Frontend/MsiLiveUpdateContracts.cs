namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendMsiLiveUpdateCatalogState { NotChecked, Checking, Completed, PartialFailure, Unavailable, Failed }
public enum FrontendMsiLiveUpdateCatalogCategory { Bios, Firmware, Driver, Utility }
public enum FrontendMsiLiveUpdateCatalogCategoryState { Success, Empty, Failed }
public enum FrontendMsiLiveUpdateSourceState { Completed, PartialFailure, Unavailable, Failed }
public enum FrontendMsiLiveUpdateVersionSource { Msi, IntelDsa }
public enum FrontendMsiLiveUpdateVersionKind { Catalog, Driver, Package }
public enum FrontendMsiLiveUpdateInstalledVersionConfidence { Exact, Heuristic }

public sealed record FrontendMsiLiveUpdateCatalogEntry(
    string Name,
    string? Version,
    string? ReleaseDate,
    string? Description,
    string? InstalledVersion = null,
    FrontendMsiLiveUpdateVersionSource LatestVersionSource = FrontendMsiLiveUpdateVersionSource.Msi,
    FrontendMsiLiveUpdateVersionKind LatestVersionKind = FrontendMsiLiveUpdateVersionKind.Catalog,
    FrontendMsiLiveUpdateInstalledVersionConfidence? InstalledVersionConfidence = null);
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
    string? ErrorKind = null,
    FrontendMsiLiveUpdateSourceSnapshot? MsiSource = null,
    FrontendMsiLiveUpdateSourceSnapshot? IntelDsaSource = null,
    IReadOnlyList<string>? LocalInventoryWarnings = null)
{
    public static FrontendMsiLiveUpdateCatalogSnapshot Unavailable(string errorKind = "CatalogUnavailable") =>
        new(FrontendMsiLiveUpdateCatalogState.Unavailable, null, null, null, [], null, errorKind);
}

public sealed record FrontendMsiLiveUpdateSourceSnapshot(
    FrontendMsiLiveUpdateSourceState State,
    string? ErrorKind = null);
