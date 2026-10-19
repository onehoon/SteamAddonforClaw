# Work Order — MSI Update PR2: Evidence-Based Version Comparison and Centered Per-Row Update Placeholder

**Date:** 2026-10-19  
**Scope:** One PR, based on merged PR #838 (diagnostic snapshots)  
**Product:** SteamAddonforClaw — standalone Full PID1902  
**Implementation:** Local Codex; code/build/unit/UI tests only. The product owner handles actual device checks after merge.

## 0. Locked user decision / overrides

Implement the originally planned **PR2 (read-only latest-vs-installed comparison)** and the newly agreed per-row layout **in the same PR**.

**This work order supersedes the “Do not add an Update button or blank placeholder” sentence in Section 8 of**
`docs/work-order/2026-10-18_MSI_UPDATE_INSTALLED_VERSIONS_INTEL_DSA_COMPARISON_PR1_PR2_WORK_ORDER.md`.

The user now explicitly wants the future **Update** control *created and rendered*, but **NOT wired to anything**:

- Render a real WinUI 3 **Button** labeled **Update** only on rows confidently classified `UpdateAvailable`.
- Set `IsEnabled = false` unconditionally in this PR. No `Click` handler, `Command`, `Navigate`, process launch, download, installation call, queue, placeholder side effect or dummy command.
- Do **not** render empty button slots for `UpToDate` or `Unknown` items. Those rows show only their status on the right.
- The button is a UI placeholder for a **future separately designed installation PR**. The `UpdateAvailable` status does not mean installer support exists.
- Do not invent future installation architecture, package acquisition or dispatch eligibility models in this PR.

### Exact card layout

All four existing tabs (BIOS/Firmware/Drivers/Utilities) use the **same** compact two-column card layout.

~~~text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Intel Arc Graphics                               ○ Update available     [ Update (disabled) ]  │
│ Installed: 32.0.101.8000 · Latest: 32.0.101.9000 · Intel DSA · Released 2026-10-10             │
└────────────────────────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Intel Wi-Fi Driver                                                       ✓ Up to date           │
│ Installed: 24.70.0.3 · Latest: 24.70.0.3 · Intel DSA · Released 2026-10-10                      │
└────────────────────────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Realtek Audio Driver                                                    ? Unknown             │
│ Installed: Unavailable · Latest: 6.0.9738.1 · MSI · Released 2026-10-10                        │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
~~~

**All versions and dates above are synthetic layout samples; do not hard-code them.**

- **Left column:** existing title at top + one combined metadata `TextBlock` directly below.
- Metadata item order (and separator) is **Installed · Latest · source · Released**.
- Source in the metadata is concise **Intel DSA** or **MSI**, not `Source: Intel DSA` or `Source: MSI`. Preserve underlying provenance and source-status logic.
- Keep existing honest strings when values are missing, and existing `Installed (estimated)` / `Latest Intel package` distinction where applicable. Localize punctuation/labels to both languages.
- **Right column:** status icon + short localized status + (only for `UpdateAvailable`) disabled Update button, on **ONE HORIZONTAL LINE**.
- Most importantly, the **entire right-hand action group is vertically centered against the FULL CARD CONTENT HEIGHT** — not aligned to the title or metadata separately. The status and button themselves are also vertically aligned with each other.
- Status and button **never** stack vertically in the normal card. No separate third/fourth status line.
- At normal MSI Claw 1920×1200 / 150% display scaling, aim for a single line of metadata. At narrower window sizes/DPI, let the **left** metadata/title wrap rather than overflowing or sacrificing right controls; do not add inner or horizontal scrollbars.
- Retain existing card density, theme-aware brushes, border, padding, typography and four TabView categories. No new top-level update panel, download button, or large color callout.

## 1. Source authority: read current code, not historic snippets

Mandatory sources:

1. `docs/Full 1902 Implementation/README.md` and current authority chain:
   - `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
   - `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
   - `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
   - `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
2. `docs/work-order/2026-10-18_MSI_UPDATE_INSTALLED_VERSIONS_INTEL_DSA_COMPARISON_PR1_PR2_WORK_ORDER.md` (PR2 comparisons and non-goals; updated button decision overrides its old prohibition).
3. `docs/work-order/2026-10-18_MSI_UPDATE_POST_836_MATCHING_AND_UTILITY_EXCLUSIONS_WORK_ORDER.md`.
4. `docs/work-order/2026-10-19_MSI_UPDATE_MANUAL_CHECK_DIAGNOSTIC_SNAPSHOT_WORK_ORDER.md`; merged PR #838 and its tests, which are **not** comparison inputs.
5. Primary MSI REA, **updated Section 11**:
   `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`  
   https://drive.google.com/file/d/1QYmnJBwEowwaJC017c7skHJDamD9zpEU/view

**REA consequence:** MSI `DataSet` entire scanner is not a safe standalone read API; its comparisons are tied to broader mutable candidate lists and may not preserve exact installed-device identity. Keep the Addon's conservative matching, do not directly call the OEM matcher, re-enable Center M, or infer success from OEM name fallback.

Relevant current code (main after #838):

- `src/SteamInputAddonforClaw.Contracts/Frontend/MsiLiveUpdateContracts.cs`
  - `FrontendMsiLiveUpdateCatalogEntry`: `Name`, `Version` (latest), `InstalledVersion`, `LatestVersionSource`, `LatestVersionKind`, `InstalledVersionConfidence`.
  - No comparison outcome is currently exposed.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`
  - `IntelDsaHardwareMatcher.Resolve` selects exact DSA HWID/component.
  - `MsiUpdateCatalogComposer.Compose`, `MapMsiEntry`, `ResolveMsiInstalledVersion`, `ResolveMsiDriverInstalledVersion` and `MapIntelEntry` are the right boundary for computing per-entry verdicts **while existing match provenance remains available**.
  - `MsiInstalledVersionResolution` and `MsiUpdateCatalogRowDiagnostic` provide `Outcome`, `Confidence`, candidate counts, etc.
  - MSI Intel family duplicate suppression, Utility exclusions and diagnostic callbacks must remain unchanged.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs`
  - latest version is currently selected as `DriverVersion ?? PackageVersion ?? Version`; a `UWP` sentinel can yield no numeric latest version.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
  - exactly one explicit `RequestMsiLiveUpdateCatalogAsync` query; MSI + Intel DSA + installed inventory run together; after Compose, PR #838 saves privacy-safe JSON **best-effort**. Preserve this flow.
- `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml`, `.xaml.cs`
  - `RenderMsiLiveUpdateCategory` creates per-row cards with `StackPanel` title and one joined metadata `TextBlock`.
  - Existing `Grid(*, Auto)` is **already present**; right Auto column is unused. Fill it; no layout rewrite or extra row.
  - Existing page uses one outer `ScrollViewer`, handles navigation cancellation, and resets to NotChecked.
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw` and `ko-KR/Resources.resw`.
- Tests: `MsiUpdateInstalledVersionsTests.cs`, `MsiLiveUpdateCatalogTests.cs`, `MsiUpdateDiagnosticSnapshotTests.cs`, `MsiLiveUpdateCatalogUiTests.cs`, `FrontendNamedPipeTransportTests.cs` / existing frontend serialization tests.

## 2. Strict comparison contract (read-only)

Three outcomes in this PR:

- `UpdateAvailable`: trustworthy **same target** plus **same comparable version domain**, offered strictly newer than installed.
- `UpToDate`: trustworthy same target plus same comparable version domain, installed equals or exceeds offered.
- `Unknown`: any missing/uncertain target identity, incompatible version types, absent version, unsupported revision/format, ambiguous candidate, insufficient source data or uncertain applicability.

**Do not introduce a `NotInstalled` verdict** in this PR. Missing local match is `Unknown` rather than proof of absence.

**Critical distinction:** A numeric-looking newest **package version** is not automatically comparable with the **currently installed PnP driver version**. Numeric sorting is the final step, only after identity and version-domain admission.

### 2.1 Intel DSA GPU / Wi-Fi / Bluetooth

Admit numeric comparison only when:

1. `IntelDsaHardwareMatcher.Resolve` resolved a specific present device/component with compatible OS/non-beta and actual HWID match.
2. This row uses `FrontendMsiLiveUpdateVersionSource.IntelDsa` and `LatestVersionKind == Driver`.
3. Its candidate `Device.DriverVersion` exists and latest **component** `Version` is a valid comparable dotted numeric driver version.
4. The versions are of the same format/number of numeric components, and the component mapping is not ambiguous.

Use `UpdateAvailable` / `UpToDate` accordingly. Preserve exact Intel source labeling.

### 2.2 Intel NPU / package-version exception

`IntelDsaHardwareMatcher.Resolve` deliberately uses the DSA *package* Version for NPU because the `OtherDrivers` component Version can be null.

**Always `Unknown` for that existing package-vs-driver pairing** in this PR even if the numeric strings look comparable. Retain `Latest Intel package` wording and installed driver version display.

### 2.3 MSI PnP driver rows

Admit numeric comparison only if:

1. The existing local match identifies the **same PnP driver component** with a reliable exact identity and a unique comparable installed driver version.
2. The catalog's offered version is explicitly a **driver version** (not a package or vague display/catalog version), e.g. typed `DriverVersion` / `LatestVersionKind == Driver`.
3. The actual match provenance proves uniqueness; avoid trusting `Exact` enum alone without checking how it was produced.

Do not treat provider/class/name heuristic match, `HeuristicFamily`, multi-device one-version `ExactHardwareId` with downgraded confidence, exact-prefix-only family inference or ambiguous INF mapping as evidence of a comparable same component.

Allow `Unknown` for the real unresolved CG3EM MSI rows (the prior 12 unmatched Driver records). Do not “fix” them in this PR.

If Intel DSA was unavailable and the same Intel family fell back to MSI, preserve the MSI row and source but **do not assert it is the latest Intel version**; keep its overall update verdict `Unknown`.

### 2.4 BIOS

Admit only an installed local BIOS string and offered MSI BIOS image whose *exact board prefix* agrees, and where the existing match gives confident same-board identity. Compare the suffix as **hexadecimal**, not decimal or a generic version.

Examples: `E1T52IMS.10F < E1T52IMS.110`; `E1T52IMS.113 < E1T52IMS.114`.

Unknown if board prefix differs, either suffix is invalid, local is a `T` test build, multiple inconsistent local identities exist, firmware/ME tools are masquerading as a BIOS title, or the catalog category/row is not a genuine BIOS image.

Do not compare ME firmware/tool to BIOS version.

### 2.5 Controller firmware

**Unknown unless a verified, model-specific installed/live firmware version and an identical, meaningful MSI offered-version representation are proven comparable.** The current PnP `REV_xxxx` or heuristic firmware revision alone is insufficient to assert update availability.

It is acceptable, and preferable, for **all existing controller firmware cards to remain Unknown** in this PR if their semantic version correspondence cannot be proven from current code/REA. No HID writes, mode changes or additional device reads.

### 2.6 MSI Utilities, UWP and provisioning packages

- Registry-based Utilities: compare only if the existing resolution proves exact product GUID / UpgradeCode and single non-conflicting installed version, **and** the server offered version is known to be the same product-version domain as installed `DisplayVersion` or documented comparable `PackageVersion`.
- AppX: compare only if the existing matcher verifies the *exact* approved package identity and correct version domain. An OEM `UWP` sentinel is **Unknown**, not an available update; a diagnostic-only AppX family allow-list is not authority to change matcher logic here.
- No generic name similarity, guessed `PackageVersion` conversion or registry heuristic should produce `UpdateAvailable`.
- PPKG/Intel ME/driver-store staged versions require a verified corresponding local package-version domain; otherwise `Unknown`.

### 2.7 Numeric version helper

Implement a small deterministic helper for bounded decimal-dot components, with explicit equal-length comparison (do not silently zero-pad unequal tuple lengths or compare lexical strings). Reject noninteger components, overflow, vendor labels, ambiguous embedded spaces, package marketing suffixes and empty values. Treat installed > latest as `UpToDate`; no downgrade status.

Examples:

~~~csharp
// Illustrative helper; keep existing project naming/style and its real provenance checks.
static int? CompareNumericDotted(string? installed, string? offered)
{
    if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(offered))
        return null;

    var a = installed.Split('.');
    var b = offered.Split('.');
    if (a.Length != b.Length || a.Length is < 2 or > 6)
        return null;

    for (int i = 0; i < a.Length; i++)
    {
        if (!uint.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var av)
            || !uint.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bv))
            return null;
        if (av < bv) return -1;
        if (av > bv) return 1;
    }
    return 0;
}
~~~

The implementation must **validate every component before returning ordering**; this example illustrates syntax, not a substitute for a two-pass validation. Ensure a malformed trailing component never becomes an `UpdateAvailable` verdict because an earlier component differed.

No generic NuGet version framework, registry of comparers, version-engine service, or speculative special-version behavior.

## 3. Mapping and frontend contracts — minimal extension

Extend the existing entry contract with a single optional trailing comparison outcome, preserving construction sites and serialization:

~~~csharp
public enum FrontendMsiLiveUpdateComparisonStatus
{
    Unknown,
    UpdateAvailable,
    UpToDate
}

// Append to the existing FrontendMsiLiveUpdateCatalogEntry positional parameters:
FrontendMsiLiveUpdateComparisonStatus ComparisonStatus =
    FrontendMsiLiveUpdateComparisonStatus.Unknown
~~~

**Derive the status in Runtime/Composer, not by comparing two strings in UI.** At `MapMsiEntry`, the original category, typed OEM fields and `MsiInstalledVersionResolution`/outcome are available. At `MapIntelEntry`, the resolved component type and exact matched PnP device are available.

Choose the smallest implementation arrangement (one internal helper or tightly scoped methods in the composer); do not introduce a generic update orchestrator.

Preserve existing entry names, installed/latest displayed values, release dates, source, version kind, exact/estimated labels, category counts and provenance. Populate comparison status only for rows meeting Section 2. Failure/unavailable snapshots and legacy manually created entries default to `Unknown`.

**Diagnostics:** PR #838's JSON snapshot is evidence-only, never a comparison input. Existing row diagnostics, source-index correlation, suppression counts, privacy redaction, five-file retention and best-effort save remain unchanged. It is unnecessary to expand the diagnostic schema in this PR solely for the new comparison enum.

## 4. Per-card UI — compact metadata + centered action group

Modify `RenderMsiLiveUpdateCategory` and the existing resources; avoid replacing the entire page/template infrastructure.

### Left content

The current `StackPanel { Spacing = 4 }` and its two children are the right shape:

1. **Title:** item name, existing semi-bold presentation.
2. **Metadata:** one `TextBlock` with existing separator `string.Join(" · ", metadata)`, ordered:

   `Installed: <version> · Latest: <version> · Intel DSA · Released yyyy-mm-dd`

The localized versions of those labels should be natural and concise; for Korean use corresponding installed/latest/released labels. Preserve `Installed (estimated)` and `Latest Intel package` distinction. If absent, retain an explicit unavailability label, without inventing zero values.

**Source label:** output only `MSI` or `Intel DSA` in this compact metadata row. If the existing `MsiUpdates_SourceMsi` and `MsiUpdates_SourceIntelDsa` are used elsewhere, keep their old semantics and add tiny compact resource keys rather than stripping localized strings at runtime.

Do not force `TextWrapping=NoWrap` on the metadata: it must wrap only as needed in narrow windows. The default MSI Claw viewport should present it on a single row.

### Right action group

Keep:

~~~csharp
var cardContent = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
~~~

Attach the existing left `content` to `Grid.Column=0`.

Add ONE compact horizontal container at `Grid.Column=1`:

~~~csharp
// Example structure only; prefer existing WinUI 3 stock SymbolIcon + TextBlock + Button.
// A StackPanel is enough; no new custom control or template.
var actions = new StackPanel
{
    Orientation = Orientation.Horizontal,
    VerticalAlignment = VerticalAlignment.Center, // relative to ENTIRE Grid/card height
    HorizontalAlignment = HorizontalAlignment.Right,
    Spacing = 8
};
Grid.SetColumn(actions, 1);

// Add stock icon and localized status text first (non-wrapping).
// Only UpdateAvailable rows get the future disabled button:
if (entry.ComparisonStatus == FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable)
{
    actions.Children.Add(new Button
    {
        Content = MainUiStrings.Get("MsiUpdates_UpdateButton", "Update"),
        IsEnabled = false,                   // REQUIRED in PR2
        VerticalAlignment = VerticalAlignment.Center
        // Do not attach Click / Command or any other behavior.
    });
}
cardContent.Children.Add(actions);
~~~

Use a **small** native/icon text status indicator:
- `UpdateAvailable`: suitable subtle update/warning icon + **Update available** (KO: `업데이트 가능`).
- `UpToDate`: check icon + **Up to date** (KO: `최신 상태`).
- `Unknown`: help/neutral icon + short **Unknown** (KO: `확인 불가`); accessibility/tooltip may provide the fuller `Unable to determine` phrasing.
- Color is supplementary; labels must remain independently understandable. Use existing theme-aware resources rather than custom hardcoded neon color.
- Right-side status icon, text and Update button are **all on the same line**; no two-row status/action layout.
- Important: `VerticalAlignment.Center` belongs to the **whole right container**, and to the controls within it, not just the status TextBlock nested in the title row.
- Button exists as a **disabled real Button**. Do not simulate click via a Border/TextBlock, and do not add a handler that merely returns.
- Never show an enabled installer button for any category, including BIOS and firmware.

### Note on compact state versus future install support

This PR permits the *visual placeholder* on an `UpdateAvailable` card, but it **does not** introduce the future package-install decision, nor represent BIOS/flashing as currently supported. When a real per-row install feature is designed later, it will add a separate supported-install eligibility rule, package identity and verified execution path.

### Catalog disclaimer text

The current resource `MsiUpdates_CatalogOnly.Text` says "no update-availability verdict, downloads, or installs" — this will become **factually false** when status is rendered.

Revise both locales to say accurately that updates are **checked/read-only** and **download/installation are not yet supported**. Do not claim that the disabled button performs any operation.

## 5. Tests and acceptance — no pre-merge device gate

### Runtime/version comparison tests

Add direct fixture tests for:
- Intel GPU/Wi-Fi/Bluetooth exact same HWID/component with strictly newer/equal/installed newer `DriverVersion`.
- NPU `LatestVersionKind.Package` and installed driver version -> `Unknown` regardless of numeric order.
- Different source/version kinds; MSI catalog/package fields versus driver version -> `Unknown`.
- Unsupported/unmatched/ambiguous/heuristic MSI driver -> `Unknown` even if a numerical offered version is higher.
- MSI exact driver identity with matching typed `DriverVersion`: newer/equal/installed newer when reliable.
- Intel DSA unavailable with MSI Intel family fallback -> `Unknown` even if an MSI version appears newer.
- BIOS same-board hex suffix (e.g. `10F` < `110`), equal, installed newer; different board, `T` test, malformed -> `Unknown`.
- Controller firmware without proven version-domain mapping -> `Unknown`.
- Utility exact registry product identity + compatible version type -> comparison when proven; ambiguous/heuristic UWP or `UWP` sentinel -> `Unknown`.
- Decimal parsing: unequal tuple component count, labels, overflow, missing values, non-digit tokens, malformed trailing component after an earlier difference all result in `Unknown`.
- A case of partial MSI/Intel source failure that preserves valid independent row results; no global “everything current” claim.

### Contract / IPC tests

- Serialize and deserialize the appended comparison field using existing frontend named-pipe contract tests.
- Previously constructed entries with no explicit comparison value default to `Unknown`.
- Existing catalog source/category status, entries and counts remain consistent.
- Re-query, navigation reset/cancellation and the PR #838 diagnostics behavior are not changed.

### UI tests

- Both en-US and ko-KR RESW files contain localized statuses, short source label, button text and updated disclaimer.
- Build/layout source assertions plus, where practical, view tests for a narrow and typical viewport (no hardware or MSI network required).
- All four categories use the same row renderer; nothing special-case renders only Drivers.
- **Exact layout:** left title + metadata `TextBlock`; right horizontal status/icon + (optional) disabled Button; **right container vertically centered within full Grid**.
- Update button has `IsEnabled=false`, no Click/Command/navigation/installer path.
- `UpToDate` and `Unknown` show status only, no blank button slot.
- Status, title, metadata keep correct reading order/accessibility; unknown can show clear full phrase for screen readers.
- Retain compact one-metadata-line target, graceful wrapping, existing one-scroll layout, TabView counts and normal theme brushes.
- Update the existing `Catalog_projection_shows_installed_version_source_and_NPU_package_without_a_verdict` / catalog disclaimer assertions to the new PR2 behavior; do not just delete regression checks.

### Build / CI

Follow current repository build/test/publish CI requirements, including standard solution build, Runtime and UI tests. Use synthetic fixtures; no real MSI services/OEM cloud/driver installations/controller hardware in automated tests.

**Physical A2VM 7, A2VM 8 and EX/CG3EM checks remain product-owner post-merge activities and must NOT be blockers for code review or merge.**

## 6. Explicit exclusions and anti-overengineering

Do NOT add:

- Downloads, MSI/Intel update installer execution, firmware/BIOS flash, store launch, UAC/install flow, per-row update IPC, package queue, installer eligibility abstraction, dummy command or fake success notification.
- PnP device probing beyond the existing manual-check capture, additional OEM method calls, alternative matching pipeline or background scans.
- Any changed HidHide/VIIPER/controller/WING/MSI Center M authority, PID1901/PID1902 transition, sleep/resume logic, recovery, shutdown/teardown, runtime elevation policy.
- New generic versioning framework, broad ID heuristics, manager/state-machine/lock/epoch barrier for theoretical races, persisted update comparison cache or long-lived state.
- New driver-family rules to “fix” Unknown entries without A2VM/EX evidence from PR #838 diagnostics.
- “Update all”, aggregate up-to-date verdict, toasts, startup scans, auto-check or out-of-band telemetry.
- New visual components or artwork beyond existing native WinUI 3 icons/buttons.

Maintain one existing Runtime owner, single UI request path, current data-source precedence, exact/heuristic confidence and fail-closed comparison behavior.

## 7. PR delivery

- **One PR** for safe typed comparison + per-row status + disabled future Update button + localization/tests.
- Prefer reviewable internal commits such as (1) contracts and comparator, (2) centered UI/localization, (3) tests/polish. Do not split into separate PRs unless a concrete build/architecture blocker is discovered.
- Describe old PR2 “no button” line superseded by this user-approved disabled placeholder.
- Provide meaningful automated proof, especially no numeric false positives across domains and no functional button wiring.
- No real-device verification gate; user will inspect app appearance and the actual per-device update cards after merge.

**Acceptance summary:** Clicking existing **Check for updates** yields status on **every returned card**; left metadata is consolidated into one visual line when width allows; the **right horizontal status + disabled Update button** (only for confidently `UpdateAvailable`) is vertically centered against the card as a whole. No download/install functionality exists in PR2.
