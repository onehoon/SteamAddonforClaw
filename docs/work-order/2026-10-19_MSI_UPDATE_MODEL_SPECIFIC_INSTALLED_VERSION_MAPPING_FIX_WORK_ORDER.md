# Work Order — MSI Update: Model-Specific Installed-Version Mapping and Comparison Fix (Post-PR #840)

**Date:** 2026-10-19  
**Target:** ONE small, reviewable PR on current main (PR #840 already squash-merged)  
**Product:** SteamAddonforClaw; standalone Full PID1902; Windows 11; supported Claw A2VM 7 / A2VM 8 / CG3EM  
**Coding:** Local Codex; **physical device validation is performed by the product owner AFTER merge and must not block PR review/merge.**

## 0. Locked product decision — this overrides earlier over-strict matching instructions

**This is NOT a general Windows driver-update detector. The MSI catalog is already selected for a specific supported MSI Claw model.** Stop trying to rediscover the relationship between known catalog product names and that model's Windows device/driver identity using a generic algorithm every time.

**Define the relationship explicitly, per supported model, in a small static mapping**:

```text
(model identity, MSI catalog item name/category)
  -> stable Windows installed-product identity/identities
  -> selected installed version(s) + known comparable MSI field
  -> existing UpToDate / UpdateAvailable / Unknown status
```

The **mapping definition is static**; the actual **installed version is read from Windows on EVERY MANUAL CHECK**, because Windows Update or manual updates can change it. Never hard-code current version numbers as truth.

**Scope is installed-version lookup + comparison + display fixes ONLY. Do not implement MSI download/install, execute helper installers, use installation URLs, connect the existing disabled Update button, change authorization, add update scheduling, or touch driver/firmware state.** All installer-related verification/planning is postponed to a separately requested PR.

This work order supersedes the generic identity strictness and CG3EM “unresolved unchanged” constraints in:
- `docs/work-order/2026-10-19_MSI_UPDATE_PR2_VERSION_COMPARISON_CENTERED_ACTION_UI_WORK_ORDER.md`
- `docs/work-order/2026-10-19_MSI_UPDATE_POST_839_VERSION_VERDICT_CORRECTIONS_WORK_ORDER.md`
insofar as they block model-evidenced installed versions. Keep the current read-only behavior, independent Intel DSA source precedence, UI, actual lifecycle safety, and source-failure handling.

**Priority is correct real-world identification, NOT maximum defensiveness.** Do not build a general matching engine, scoring framework, policy registry, or separate identity manager. One small per-model table plus reuse of existing PnP and Registry inventory is preferred.

## 1. Mandatory source reading

1. `docs/Full 1902 Implementation/README.md` and its authority references:
   - `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
   - `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
   - `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
   - `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
   Controller/power/HidHide/VIIPER authority is unrelated and must remain unchanged.
2. Current `main` implementation of:
   - `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs` — `MsiUpdateCatalogComposer`, `ResolveMsiDriverInstalledVersion`, `ResolveDriverCandidates`, `ResolveMsiComparisonStatus`, `CompareObservedMultiComponentMsiDriver`, `ResolveMsiInstalledVersion`.
   - `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` — `RequestMsiLiveUpdateCatalogAsync` currently has the **verified `hardware.Model`** before calling `Compose`.
   - `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateDiagnosticSnapshot.cs`.
   - `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs` — OEM per-model catalog parser.
   - `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml.cs` — existing UI only; no redesign.
   - `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs` and relevant catalog/diagnostic/UI tests.
3. **Actual new CG3EM v0.1.405.0 diagnostics** in `C:\GoogleDrive\Addon\Log\1019-MSIUPDATE\` (use the current two latest captures, NOT the earlier v0.1.403/404 files):
   - `msi-update-20261019-125953.549-e06e67b6.json`
   - `msi-update-20261019-130041.900-39b44896.json`.
   They show MSI+Intel DSA success, 81 locally selected PnP diagnostic rows, and no capture warnings, but 11 `NoUsableIdentity` plus Realtek audio `NoMatchingPresentDevice` under the 19 remaining MSI Driver catalog rows.
4. MSI OEM REA report `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`, especially the OEM update catalog semantics. Do **not** invoke Center M's mutable `DataSet` matching API.

## 2. Core bug: wrong matching abstraction

The existing chain is roughly:

```csharp
MSI DeviceId exact HWID -> MSI InfName exact INF ->
special Realtek codec / Intel GPU-WiFi-Bluetooth-NPU family ->
NoUsableIdentity / Unknown
```

That fails for **normal CG3EM catalog records** whose OEM `DeviceId` is merely `PCI\VEN_8086`, `PCI\VEN_10EC`, a sentinel or null, and whose `InfName` is absent. This is the **OEM record's incomplete metadata**, not evidence that the installed Windows driver is missing.

Also, requiring exactly one PnP row for a multi-device package and rejecting DTS/IntelliGo's correctly resolved 2/4 device groups due to a second `SWC\VEN_*&DEV_...` check are unnecessary blockers.

**Fix with known Claw model mapping, not more heuristic exceptions.**

## 3. Small model-keyed mapping with stable Windows identifiers

### 3.1 Scope and wiring

- Reuse the supported hardware model identity already resolved at the manual-check boundary: `hardware.Model` (`msi.claw.cg3em`, `msi.claw.a2vm.7`, `msi.claw.a2vm.8`). Pass it as one internal argument into the composer/mapper (e.g. optional last argument to `Compose` for existing tests and callers; no new IPC or frontend DTO).
- Keep a short **static model-specific table** (inside the existing MSI LiveUpdate matching area or at most one simple nearby source file for readability), indexed by normalized **model ID + MSI category + catalog name**. Use exact case-insensitive catalog name matching and existing name aliases where observed. Do not key on the item's current/version number or transient source index.
- Each mapped entry declares **stable target evidence** from the model: one/multiple known PnP HWIDs or suitable HWID prefix plus supplier + device class, or known exact installed software product name/publisher; specify which remote version field is comparable when established.
- Apply explicit mapping **first** for known model/product. Then reuse the existing conservative matching as fallback for products without a supported static rule. Intel DSA's own resolved GPU/Wi-Fi/Bluetooth/NPU rows stay independently matched with their current exact HWID path.
- The catalog model, OEM source, DSA suppressions, excluded Utilities and current four-tab composition must remain intact. **Do not copy CG3EM HWIDs into A2VM 7/8** without those models' own evidence; for unverified A2VM rows retain current matching rather than fake installed versions.
- Do not persist a mapping database or download/maintain mapping JSON. It is a stable small code table; changes are made alongside driver support when necessary.

### 3.2 CG3EM mapping candidates evidenced by 21:59/22:00 diagnostics

**The IDs below are collected Windows PnP candidates, NOT a license to use version-number coincidence as identity.** Codex must cross-check PnP provider and class, catalog name/type, the already collected `HardwareIds`/`CompatibleIds` and unique family selection in the current implementation. Admit confirmed rows only; mark other candidates unresolved with a concise rationale, not a made-up mapping.

| MSI catalog row | Stable Windows target evidence from real CG3EM log | Intended handling |
| --- | --- | --- |
| Intel HID Event Filter | Intel HIDCLASS `ACPI\INTC10CC`, observed `2.2.2.17` | Model map single device; normal version compare |
| Intel Chipset Driver | Intel SYSTEM `PCI\VEN_8086&DEV_E402/E422/E423`, one driver INF family, observed `10.1.56.28` | Static set of chipset components; group/version resolution |
| Intel Platform Monitoring Technology Driver | Intel SYSTEM `PCI\VEN_8086&DEV_B07D`, observed `7.3.11.3` | Single model mapping |
| Realtek Card Reader Driver | Realtek MTD `PCI\VEN_10EC&DEV_5264`, observed installed `10.0.26200.21389` vs OEM `10.0.26100.21378` | Single map; higher installed is UpToDate after comparable-domain admission |
| Intel Serial IO Driver | Intel SYSTEM `PCI\VEN_8086&DEV_E450` and `ACPI\INTC10BC` family, observed `30.100.2534.18` | Group same package; do not require only one PnP row |
| Intel IPF Driver | Intel SYSTEM `ACPI\INTC10D4/10D5/10D6/10D9` family, observed `2.3.20303.5058` | Group on stable identities, not catalog's partial `PCI\VEN_8086` |
| Intel Management Engine Driver | Intel SYSTEM `PCI\VEN_8086&DEV_E470`, observed `2603.9.4.0` | Single model mapping |
| Intel Smart Sound Technology driver | Intel MEDIA/SYSTEM `INTELAUDIO\VEN_8086&DEV_*` **only for the known audio controller components**, many observed `20.43.12741.0` | Define the real fixed set of observed component IDs; NEVER general-match all Intel audio devices |
| Integrated Sensor Solution | Two installed branches observed `5.8.58.0` / `5.8.61.0`; OEM `Version=5.8.58.0` but `PackageVersion=5.8.61.0` | **Unresolved until identifiable product/driver branch**; do not select by whichever equals latest |
| Windows Studio Effects | OEM `3.0.8.0`, insufficient corresponding PnP/package identity | Remain Unknown unless direct real model-specific identity can be established |
| Realtek OED Driver | OEM `20.43.274.9`, insufficient corresponding Windows package/component identity | Remain Unknown without a real target |
| Realtek Audio Driver | Existing Realtek codec-specific lookup misses on CG3EM; several unrelated Realtek SWC/MEDIA versions in snapshot | Check whether a definitive model audio codec/driver store identity exists; do not grab any Realtek driver |
| DTS Driver | Existing `ExactHardwareId`, 2 selected devices, installed `1.16.4.0`, OEM `1.16.4.0` | **Use same existing selected component group**; remove redundant & over-strict `DEV_` comparison admission; UpToDate on equal comparable version |
| IntelliGo APO Driver | Existing `ExactHardwareId`, 4 selected devices, installed `100.1.4.2709`, OEM same | As above; don't demand a synthetic `DEV_` identity the observed components don't expose |
| Goodix / Game Assist / Dynamic Tuning / Trusted Execution / PPM | Already resolved to actual specific PnP devices | **Preserve working matching + comparisons**; do not replace them with weaker static guesses |

CG3EM sensor/OED/Studio Effects rows may legitimately remain Unknown if the existing sanitized evidence cannot identify a meaningful installed product. The user does **not** want fake status or a giant general matcher to reduce an arbitrary Unknown counter.

**Do not hard-code current observed versions** (`2.2.2.17`, `10.1.56.28`, etc.) in production mapping. These are acceptance fixture values ONLY.

### 3.3 Stable vs unstable identifiers

Allowed durable anchors:
- Real hardware ID families and specific `VID/PID`, `VEN/DEV`, `ACPI\INTC*`, `SWC\VEN_*` with appropriate provider/class/product evidence.
- Existing Windows PnP `DriverProvider` / device class, product-specific component names.
- Exact MSI Center M Registry `DisplayName` + MSI `Publisher`, and existing GUID/UpgradeCode matches when present.
- A known package's **Original INF name** only if it is ALREADY reliably available; do not introduce an extra driver-store enumeration just to obtain it in this PR.

Do NOT hard-code:
- `oem605.inf`, `oem598.inf`, or any `oemNNN.inf` assigned by Windows — they change after installation/re-enumeration.
- Existing Windows versions, MSI catalog versions, PnP instance suffixes, WMI row indices, or snapshot diagnostic row IDs.
- MSI compressed archive filenames, mutable URLs, dates, download hashes or installation paths.
- Vendor-only identity (`PCI\VEN_8086`) without a product-specific anchor; that covers many unrelated Intel devices.

## 4. Installed-version and verdict policy — pragmatic read-only comparison

### 4.1 Separate identity from version

- Mapping first chooses **the component/device group**, based on static model+catalog identity and current Windows identity. **Never select a Windows candidate because its version equals the MSI latest version**; that makes stale OEM catalogs look current.
- Read the chosen PnP `DriverVersion` (or software `DisplayVersion` / AppX `Version`) from the existing inventory, on each manual check.
- When a product comprises multiple PnP components, **multiple devices alone are not ambiguity**. If the selected group yields one consistent installed version (or a known single driver package/INF group with a validated representative), report it as the installed version. If genuinely mixed incomparable versions remain, keep comparison Unknown and avoid pretending a single version exists.
- A reliable per-model mapping means installed version need not be labeled `(estimated)` solely because more than one selected device exists. Preserve the confidence label when product-family identity is still tentative.
- If mapping returns no Windows match, don't invent `NotInstalled`. Keep `Installed version unavailable` and `Unknown`; allow existing fallback only when it cannot contradict the defined product identity.

### 4.2 Version comparison

For a mapped, comparable product:
- Use the remote version field explicitly chosen by that item's mapping (where the field represents the **same driver/product version**), not an additional blanket rule requiring `DriverVersion` from the OEM.
- Numeric dotted compare, existing helper; installed < offered = `UpdateAvailable`; installed == offered **or installed > offered** = `UpToDate`.
- Do not compare unrelated package and driver versions when MSI's fields diverge: e.g. Integrated Sensor Solution `Version=5.8.58.0` vs `PackageVersion=5.8.61.0`. Preserve explanatory `Latest Intel package` / `Package` provenance when applicable.
- **Displayed Latest must correspond to the version the verdict actually compares.** If an OEM catalog field is a package release but only an installed component driver version is known, either explicitly select the verified matching driver-version field for both display and compare or retain `Unknown`. Do not show one latest value while comparing against a different hidden value.
- Identity must not be inferred from equal version strings alone. This is the only essential correctness boundary; no excessive extra locks/manager/epoch/installer-style hardware suitability gates.
- Leave BIOS board+hex and owned controller Firmware, Intel DSA exactly matched rows as they currently work; UWP latest=`UWP` has no numeric latest and retains Unknown.

### 4.3 MSI Center M installed version bug

CG3EM: local HKLM32 Uninstall Registry has publisher MSI, version `3.0.2608.3101`, and diagnostic correlation to `MSI Center M`; OEM catalog offers `3.0.2608.3101`. It remains `Unmatched` solely because OEM GUID does not equal Registry ProductCode.

Define the **known model+Utility name** mapping to an exact Windows Uninstall `DisplayName` + `Publisher`, with unique nonempty numeric `DisplayVersion`. Reuse already captured software rows; **do not require matching OEM catalog GUID when the known software identity is sufficient**. Avoid general substring matching or confusing MSI Center with MSI Center M.

On a unique verified identity, display installed version and compare the ordinary MSI Utility `Version` if it is numeric and same product-version domain. Do not enable/disable/restart Center M, and do not change Center M suppression/authority.

### 4.4 Existing UWP Utilities

Preserve the resolved WinRT AppX installed versions:
- Realtek Audio Control `2.53.394.0`
- DTS Audio Processing `1.11.3.0`

Because the OEM latest value is the sentinel `UWP`, latest numeric version is missing; keep `Unknown` status while **showing the actual installed version**.

## 5. Concrete minimal implementation shape

Use a short data-only table and small selection helpers **in the current composer matching owner**. Example below is illustrative and deliberately uses NO hard-coded versions:

```csharp
// Illustrative; adapt to existing project types; do not make a generic plugin registry.
private sealed record KnownTarget(
    string ModelId, string CatalogName, string DeviceClass,
    string Provider, string[] HardwareIds,
    FrontendMsiLiveUpdateVersionKind ComparableKind);

// Exact model/catalog entries. Include observed stable HWIDs, never oemNNN.inf.
// Prefer small explicit table rows, not callbacks / per-row matcher objects.
private static readonly KnownTarget[] KnownTargets = [
    new("msi.claw.cg3em", "Intel HID Event Filter",
        "HIDCLASS", "Intel", ["ACPI\\INTC10CC"], FrontendMsiLiveUpdateVersionKind.Catalog),
    new("msi.claw.cg3em", "Intel Management Engine Driver",
        "SYSTEM", "Intel", ["PCI\\VEN_8086&DEV_E470"], FrontendMsiLiveUpdateVersionKind.Catalog),
    new("msi.claw.cg3em", "Realtek Card Reader Driver",
        "MTD", "Realtek", ["PCI\\VEN_10EC&DEV_5264"], FrontendMsiLiveUpdateVersionKind.Package)
];
```

In the actual implementation use *the existing current* MSI `MsiLiveUpdateCatalogEntry`, PnP inventory and `MsiInstalledVersionResolution` types; do not introduce a second public API, DTO, generic provider interface or separate state authority.

Existing exact HWID/INF path may remain for non-mapped catalog entries. For mapped entries, use `ModelMappedDevice` (or a simple existing outcome), with accurate candidate count, version count and confidence, so diagnostics can explain decisions. If all target components share a version, do not automatically downgrade confidence to Heuristic merely because count > 1.

Thread `hardware.Model` into `MsiUpdateCatalogComposer.Compose` via a small internal trailing argument or similar minimal change. Existing call sites/tests may continue to omit it and preserve historical matching behavior.

**Avoid re-validating model mapping through the old catalog generic exact-HWID/INF admission again in `ResolveMsiComparisonStatus`**: that recreates the current bug. Instead, once the model mapping provides a coherent version and selected comparable field, compare it directly with the existing numeric helper. Model mapping is an alternate identity admission path, not another hint to the old matcher.

Minimal diagnostics tweak recommended: existing sanitized row projection already has `MappedEntry`, `matchOutcome`, `candidateCount`, `versionCount`. Add `comparisonStatus` to this existing diagnostic row if trivial; don't build new logging/collection infrastructure. Keep privacy allow-list, five-file retention, manual-trigger-only and existing redaction.

## 6. Acceptance tests, model isolation, regression

Add/update synthetic fixtures in `MsiUpdateInstalledVersionsTests.cs` and any directly affected diagnostic/UI tests.

**Core table test** for `msi.claw.cg3em` with the product names and stable HWIDs in Section 3:

- HID `2.2.2.17` == OEM `2.2.2.17` → **UpToDate**.
- Chipset 3 distinct components all `10.1.56.28` == OEM → **UpToDate**; one missing expected component should not create false installed version from an unrelated Intel device.
- Platform Monitoring `7.3.11.3` == OEM → **UpToDate**.
- Card Reader installed `10.0.26200.21389` > OEM `10.0.26100.21378` → **UpToDate**.
- Serial IO multi-device same `30.100.2534.18` == OEM → **UpToDate**; multiple PnP instances of same driver INF are normal.
- IPF group all `2.3.20303.5058` == OEM → **UpToDate**.
- Management Engine `2603.9.4.0` == OEM → **UpToDate**.
- Smart Sound group `20.43.12741.0` == OEM → **UpToDate** when confirmed grouped components are consistent.
- For any mapped family when installed lower than offered **and the selected MSI field is comparable** → **UpdateAvailable**; installed greater → **UpToDate**. No hard-coded version values in product table.
- **DTS 2** and **IntelliGo 4** currently matched components with one common installed version equal to OEM → UpToDate even though multi-component; a stray second DTS version elsewhere must not change the selected catalog group's verdict.
- MSI Center M unique exact Name+Publisher `3.0.2608.3101` == OEM → UpToDate; similarly named MSI Center or different publisher / two conflicting installs → Unknown.
- Unresolved ISS, Studio Effects, Realtek OED/Audio do not become UpToDate merely from a coincidentally equal version.
- UWP utility installed version remains visible with Unknown status; BIOS/Firmware/Intel DSA (incl. NPU) and existing exact Goodix/Game Assist mappings remain unchanged.
- Wrong hardware model `msi.claw.a2vm.7` / `msi.claw.a2vm.8` with CG3EM-only candidates **must NOT use CG3EM mapping**; they retain their existing current matching/Unknown behavior. Add model-specific rules for those models only when evidenced; do not fabricate targets.
- Case-insensitive catalog name, provider and class handling; reject vendor-only `PCI\VEN_8086` lookalike when not in the fixed target map; a new Windows driver version after the next scan is reflected immediately without modifying mappings.
- Broken/partial MSI response, Intel DSA source failures, category counts, Utility exclusions, no duplicated DSA rows and version-domain disagreement tests preserve existing behavior.
- Validate model-id threading and `ComparisonStatus` contract round trip if changed, no new IPC.
- Optional diagnostic snapshot test ensures `comparisonStatus` is privacy-safe and does not expose full PnP instance names/paths.

Run standard repository build and unit/UI test CI. **No actual Claw/A2VM/CG3EM hands-on testing is a local Codex requirement or PR merge blocker**; post-merge real-device verification belongs to the user.

## 7. Explicit exclusions and delivery

Do **not** implement:
- Download, install, MSI web URLs, package execution, installer selection, system modifications, driver store installation, controller flashing, Update button events (keep disabled), UAC flow.
- Generic fuzzy search/scoring engine, new general-purpose policy subsystem, dynamic mapping service, new cache/database, external JSON table, driver name NLP or Windows system-wide inference for unsupported models.
- New driver scan, SetupAPI/WMI enumeration, OEM `DataSet` matcher invocation, background queries, polling or cross-session authority.
- Any changes to HID/PID1902 routing, VIIPER native ownership, HidHide, resume/shutdown/restoration or Center M enable/disable.

**One PR**, preferably 2–3 logical commits: (1) model id + static mapping + installed selection, (2) comparison / DTS / Center M corrections, (3) focused tests and optional diagnostic column. Do not split unnecessarily; no artificial per-entry framework.

### Definition of done

On CG3EM, MSI-supplied **model-specific** catalog names resolve to their **known local Windows PnP/software product identities** without requiring the OEM catalog to carry a full HWID/INF, and report actual installed versions from Windows on every manual check. When field semantics are comparable, statuses are correct (`UpToDate` for equal or newer installed versions and `UpdateAvailable` for genuinely older installed versions). Genuine missing or incompatible identities stay Unknown. The UI and disabled Update button do not change, and **NO installation functionality exists**.
