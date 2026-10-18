> **SUPERSEDED / CONSOLIDATED (2026-10-18):** Do not implement this earlier utility-only work order as a separate PR. The active single-PR instruction now combines the three Utility exclusions with PR #836 follow-up MSI/Intel DSA duplicate consolidation, local installed-version match improvements and bounded diagnostics: [Post-836 combined work order](docs/work-order/2026-10-18_MSI_UPDATE_POST_836_MATCHING_AND_UTILITY_EXCLUSIONS_WORK_ORDER.md). The historical text below is retained for context only.

---

# Work Order — Exclude Three MSI Utility Catalog Rows Before Installed-Version Matching

**Date:** 2026-10-18  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Scope:** One small code PR on current `main` after PR #836. No hardware validation requirement before merge.

## Requested behavior

In the **MSI Update → Utilities** tab only, never display or locally resolve the installed version of **exactly** these MSI remote catalog entries:

1. `MSI App Player Installer CN`
2. `Intel Graphics Software`
3. `MSI App Player Installer`

The user explicitly wants both the card and its **per-entry installed-version/applicability lookup** skipped. The raw MSI remote category request and response parsing must **not** be filtered or disabled; the OEM API is queried by category and returns all utility records. Do **not** change the Intel DSA query, driver deduplication, or the BIOS/Firmware/Drivers categories.

Treat these as three **distinct exact names**. Compare `entry.Name.Trim()` using `StringComparison.OrdinalIgnoreCase` / an equivalent ordinal-ignore-case set. Do **not** filter substrings or broader families (`"Intel Graphics"`, `"MSI App Player"`, `"Intel"`), because that would accidentally suppress unrelated rows such as `Intel Graphics Command Center`. Don't invent additional synonyms without actual catalog evidence.

## Current code path and recommended minimal fix

Read the current Full1902 authority docs and the active MSI Update work order first:

- `docs/Full 1902 Implementation/README.md`
- `docs/work-order/2026-10-18_MSI_UPDATE_INSTALLED_VERSIONS_INTEL_DSA_COMPARISON_PR1_PR2_WORK_ORDER.md`
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs` — `MsiUpdateCatalogComposer.Compose`, `MapMsiEntry`, `ResolveMsiInstalledVersion`
- `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml.cs` — category headers and card rendering
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`

Today `MsiUpdateCatalogComposer.Compose` starts from `source.Entries.ToList()` and maps each item through `MapMsiEntry`, which invokes `ResolveMsiInstalledVersion` for Utility entries. Filter only the **Utility** category's source rows **before** this mapping. Example:

```csharp
private static bool IsExcludedUtility(MsiLiveUpdateCatalogEntry entry) =>
    entry.Name.Trim() is { } name &&
    (name.Equals("MSI App Player Installer CN", StringComparison.OrdinalIgnoreCase)
     || name.Equals("Intel Graphics Software", StringComparison.OrdinalIgnoreCase)
     || name.Equals("MSI App Player Installer", StringComparison.OrdinalIgnoreCase));

// In Compose, before Select(MapMsiEntry):
var sourceEntries = sourceCategory == MsiLiveUpdateCatalogCategory.Utility
    ? source.Entries.Where(entry => !IsExcludedUtility(entry)).ToList()
    : source.Entries.ToList();
```

The code sample is illustrative; keep the smallest readable change consistent with existing style. Filtering in `MsiUpdatesPage` is **not** sufficient: doing so would still call each excluded record's local installed-version matching in Runtime and distort category counts. Filtering in the OEM API helper/parser is also **not** desired because the remote response must remain unchanged.

The current local inventory collector reads shared Windows Uninstall/AppX information once to support the **remaining** utility records. Do not add per-app exclusions or an extra scanner there; the shared OS enumeration may still see these installed packages, but **no excluded catalog row must invoke `ResolveMsiInstalledVersion` or be passed into a per-entry match**. No extra Registry/WMI/AppX lookups should be introduced for these exclusions.

## Expected UI and state

- Excluded rows absent from Utilities cards and frontend serialized Utility entries.
- Utilities tab header count reflects **remaining visible entries**, not raw OEM server count.
- All existing utility rows other than the exact three names remain visible and locally matched as before.
- Preserve source status, per-category failures, cancellation, manual check/fresh-entry reset and partial-success policies.
- When the MSI Utility category is successfully returned but all entries are excluded, show the ordinary empty list/card state, **not** a network/category failure. Update category state to `Empty` if this is needed to remain consistent with existing category rendering, without changing `MsiSource` completion status.
- No new UI toggle, persistence, IPC schema, helper API, background check or installation code.

## Automated tests and merge criteria

Add focused unit tests to `MsiUpdateInstalledVersionsTests.cs`:

1. A successful synthetic Utility category containing all three excluded names plus `MSI Center M` and `Intel Graphics Command Center` yields **only** the last two Utility frontend rows. Confirm the three excluded names are absent.
2. Repeat with mixed case and outer whitespace; exact match only. `MSI App Player` and `Intel Graphics Command Center` remain visible.
3. Confirm the Utility category's count reflects the filtered entries, and excluded records have no projected InstalledVersion. Because all three are removed **before** `MapMsiEntry`, this verifies the exclusion point; do not add a test-only fake local query counter or extra abstraction just to prove lack of matching.
4. All-excluded Utility category is displayed as empty, **not** failed. MSI source remains successful.
5. Unchanged BIOS/Firmware/Drivers and Intel DSA behavior.

Run existing relevant .NET/unit/UI tests and build. **Actual MSI Claw hardware tests are the user's post-merge responsibility, not a PR merge blocker.**

## Non-goals

Do not change MSI remote catalog query requests; do not add server-side filters, change the OEM DLL/helper, collect fewer unrelated shared PnP entries, alter Full1902 controller ownership, modify other updater tabs or introduce any extra abstraction/manager/state. This is strictly a **small Runtime Utility-category projection filter**.
