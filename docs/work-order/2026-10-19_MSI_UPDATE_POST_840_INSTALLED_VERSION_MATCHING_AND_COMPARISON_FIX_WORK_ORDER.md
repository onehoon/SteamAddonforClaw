# Work Order — Post-PR #840: Fix MSI Update Installed-Version Matching and Comparison (Read-Only)

**Date:** 2026-10-19  
**Target:** One focused PR on current `main` (PR #840 already squash-merged)  
**Owner:** Local Codex (implementation + automated code tests); physical-device verification is the **product owner's post-merge role**  
**Product:** Standalone SteamAddonforClaw, Full PID1902; **no CTW integration**

## 0. Locked scope and changed decision

Fix the **existing** MSI Update **manual lookup → installed-version identification → version comparison → per-item UI display** behavior. Nothing else.

The product owner explicitly rejects the progressively restrictive version-verdict policies in PR #839/#840. MSI supplies the official **supported-model catalog** and MSI package releases. The Addon is a read-only update-information display at this stage, **not** a second OEM hardware-compatibility certification engine.

**Supersession:** For this PR, the relaxed lookup/comparison policy in this order supersedes the overly strict single-device/Exact-only, typed-`DriverVersion`-only, MSI-family allowlist, and DTS/IntelliGo per-device-DEV restrictions in:
- `docs/work-order/2026-10-19_MSI_UPDATE_PR2_VERSION_COMPARISON_CENTERED_ACTION_UI_WORK_ORDER.md`;
- `docs/work-order/2026-10-19_MSI_UPDATE_POST_839_VERSION_VERDICT_CORRECTIONS_WORK_ORDER.md`.

Their unrelated model-specific catalog selection, source attribution, UI, lifecycle and safety contracts remain in force.

**DO NOT implement installation yet.** No MSI/Intel package download, installer, launch command, UAC, flash, shell/store action, installability checker, IPC method, new workflow, or changes to the future **Update** button. The button remains a real WinUI 3 Button **unconditionally disabled** for `UpdateAvailable` cards, with **no Click/Command**, as on current `main`.

This PR must actually improve the many `Installed version unavailable` results rather than merely adding another narrow list of status exceptions.

## 1. Read these authorities and evidence

1. `docs/Full 1902 Implementation/README.md` and its authority chain, including:
   - `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
   - `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
   - `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
   - `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
   No changes to routing, PID1901↔PID1902, sleep/resume, HidHide, VIIPER, physical ownership, Center M enable/disable, Runtime privilege or teardown.
2. `docs/work-order/2026-10-19_MSI_UPDATE_MANUAL_CHECK_DIAGNOSTIC_SNAPSHOT_WORK_ORDER.md` (PR #838), the PR #839 and #840 work orders above, and their current merged source/tests.
3. MSI OEM REA: `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`, especially **Section 11**. The OEM mutable `DataSet` update matcher is **not** an independent safe API. Keep the Addon's one inventory/composer authority; do not invoke Center M or add its matcher.
4. **Latest physical evidence, PR #840 build `0.1.405.0`:**
   `C:\GoogleDrive\Addon\Log\1019-MSIUPDATE\msi-update-20261019-125953.549-e06e67b6.json` and
   `msi-update-20261019-130041.900-39b44896.json`.
   Both have MSI Completed, Intel DSA succeeded, `inventoryWarnings=[]`, 81 diagnostic-selected PnP entries and the **same** source/matching data. All diagnostic `msiRows[*].installed` outcomes are pre-comparison matching evidence; the JSON **does not serialize** `ComparisonStatus`.

### Observed real symptoms

Out of 19 retained MSI Driver rows, **11 report `NoUsableIdentity` and one Realtek Audio row reports `NoMatchingPresentDevice`**, although Windows signed-driver data contains plausible vendor/component evidence for many. This is a **matching** bug/coverage gap, not a WMI enumeration failure. PR #840 only changed verdict admission for already matched rows.

Relevant CG3EM PnP candidates versus MSI catalog:
| MSI row | MSI published Latest | Windows signed-driver candidate(s) | Existing problem |
|---|---|---|---|
| Intel HID Event Filter | `2.2.2.17` | Intel HIDClass `2.2.2.17`, `oem605.inf`, ACPI INTC10CC | `NoUsableIdentity` |
| Intel Chipset Driver | `10.1.56.28` | Several Intel System devices, shared `oem598.inf`, `10.1.56.28` | `NoUsableIdentity`; do not require 1 PnP device |
| Intel Platform Monitoring Technology | `7.3.11.3` | Intel System device `7.3.11.3`, `oem618.inf` | `NoUsableIdentity` |
| Realtek Card Reader | `10.0.26100.21378` | Realtek MTD `10.0.26200.21389`, PCI VEN_10EC/DEV_5264 | `NoUsableIdentity`; installed is *newer* if same family |
| Intel Serial IO | `30.100.2534.18` | Intel System device group at `30.100.2534.18`, including `oem636.inf` | `NoUsableIdentity` |
| Intel IPF | `2.3.20303.5058` | Intel System entries `2.3.20303.5058` (`oem615.inf`) **and other `2.3.20306.4` entries** | `NoUsableIdentity`; avoid arbitrary maximum/version-equality selection |
| Intel Management Engine | `2603.9.4.0` | Intel System `2603.9.4.0`, `oem633.inf` | `NoUsableIdentity` |
| Intel Smart Sound | `20.43.12741.0` | Intel MEDIA/System INTELAUDIO-related component group, multiple INFs, consistent `20.43.12741.0` | `NoUsableIdentity` |
| Integrated Sensor Solution | `5.8.61.0` **Package**; `5.8.58.0` Catalog `Version` | Intel `5.8.61.0` and `5.8.58.0` present in different components | `NoUsableIdentity`; keep version-domain distinction |
| DTS Driver | `1.16.4.0` | Matched 2 DTS components `1.16.4.0`; other DTS components at `1.10.7.0` also exist | Installed `1.16.4.0` already found; post-840 comparison incorrectly `Unknown` |
| IntelliGo APO | `100.1.4.2709` | Matched 4 IntelliGo components at `100.1.4.2709` | Installed version found; post-840 comparison incorrectly `Unknown` |
| MSI Center M Utility | `3.0.2608.3101` | HKLM32 uninstall record MSI-published `3.0.2608.3101` | `Unmatched` due GUID-only lookup |

Other noted retained rows: Windows Studio Effects (`3.0.8.0` package), Realtek OED (`20.43.274.9`), Realtek Audio (`10.0.378.1`) need **correct package/component identification**, not arbitrary Realtek or camera names. Do not fabricate local versions if genuinely absent/undistinguishable.

Already-working rows must stay working: BIOS `E1T91IMS.10D`, exact-owned Controller Firmware `4.19`, Goodix installed `3.12144.0.160` > MSI `3.12144.0.100`, Intel Game Assist installed `2.0.1801.30` > MSI `1.9.602.30`, Dynamic Tuning / Trusted Execution / PPM equal versions, exact Intel DSA Graphics/Wi-Fi/Bluetooth and equality-only NPU. AppX Realtek Audio Control `2.53.394.0` and DTS Audio Processing `1.11.3.0` already read correctly; MSI `UWP` sentinel has **no numeric latest** and remains Unknown.

## 2. Verified code defects and fix boundaries

### 2.1 Driver lookup never reaches usable Windows family evidence

`src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`

Existing `ResolveMsiDriverInstalledVersion`:
1. Attempts `entry.DeviceId` via `IsMsiDeviceIdMatch`;
2. attempts `entry.InfName`;
3. special-cases Realtek codec;
4. uses `KnownMsiIntelDriverNames` only for Graphics/Wireless/Bluetooth/NPU;
5. otherwise `Unknown("NoUsableIdentity")`.

Many MSI catalog `DeviceId` fields are just `PCI\VEN_8086`, `PCI\VEN_10EC`, `NOT_FOUND` or empty; `InfName` is null. This **must not terminate lookup** while Windows has usable `DriverProvider`, `DeviceClass`, `DeviceName`/`FriendlyName`, HardwareIds and `InfName` in the already captured `MsiUpdateInstalledInventory.PnpDevices`.

**Required implementation**:
- Keep exact DeviceId/INF match precedence where it genuinely identifies the product.
- When missing/partial/not-found or no usable exact match, attempt **bounded family matching** for **the current MSI catalog driver families** using *combinations* of already-captured Windows metadata:
  - known product/component name tokens and semantic family (`HID Event Filter`, `Chipset`, `Serial IO`, `IPF`, `Management Engine`, `Smart Sound`, `Card Reader`, `ISS`, `Platform Monitoring`, `DTS`, `IntelliGo` etc.);
  - appropriate vendor/provider;
  - relevant Windows PnP class;
  - corroborating precise HWID, device-name, INF/driver-package evidence **when available**.
- `Type=Chipset` plus Intel provider, or `PCI\VEN_8086` by itself, is **NOT** a chipset/ME/IPF/Serial IO identity. Differentiating family matters more than making the candidate count exactly one.
- Do not use **numerical equality with MSI Latest** as the sole way to pick a local driver; that circular selection hides outdated components and can produce false UpToDate. Do not pick max/current/latest-like version merely because it makes the row look correct.
- Reuse existing captured inventory and current composer methods; implement small readable helper(s) or one small focused family rule table **within the same file**, with no new registry/service/manager/state framework. Avoid a growing collection of product-specific comparator exceptions.

**Multi-device package rule:**
- Many legitimate Intel driver packages bind to multiple PnP devices. Group by **demonstrated component family / known installed driver package (e.g. same INF/driver family)**, not by each PnP instance. If all selected **relevant components** have the same valid installed version, report that version (with `Heuristic` confidence when deduced from a family).
- If more than one legitimate version occurs, first distinguish unrelated subcomponents / separate driver packages using **product identity evidence**, not numeric preference. If one valid family/package remains, display it; if multiple different relevant package versions genuinely remain and cannot be distinguished, retain Unknown. Never silently choose a maximum, the first device, or the value matching MSI.
- A multi-device count or `Confidence.Heuristic` alone must **not** suppress an otherwise well-supported installed version or same-domain comparison.
- If there is a good family candidate but comparison-domain proof is insufficient, **display Installed (estimated)** and keep status `Unknown`; do not discard useful installed version solely due to a conservative comparison gate.
- Retain controlled unknown for no reliable package-family evidence, conflicting installed versions in one inseparable family, unsupported/absent item, or corrupt/unreadable Windows data. **Unknown is a result of actual ambiguity, not an automatic response to missing OEM HWID.**

### 2.2 Version comparison overconstrains the resolved family

Existing `ResolveMsiComparisonStatus` demands `match.Outcome ExactHardwareId/ExactInf`, `Confidence.Exact`, `CandidateCount==1`, and for MSI versions often a hardcoded `ObservedSingleDeviceDriverNames` admission. `CompareObservedMultiComponentMsiDriver` requires DTS/IntelliGo `SWC\VEN_XXX&DEV_...` even though this CG3EM diagnostic carries `SWC\VEN_DTSI` / `SWC\VEN_IGO` family identity.

**Required policy:**
1. A successfully resolved, sufficiently unambiguous product/driver-family version may be compared; **not just `Exact`/one PnP item**. Keep confidence in the frontend for honest `Installed (estimated)` display, but do not use it as the only verdict gate.
2. MSI provides the official **model-targeted** catalog. For a correctly identified same product/version domain, compare the numeric MSI offered version (`DriverVersion`, or applicable `Version`/`PackageVersion`) against local installed numeric `DriverVersion`. It is unreasonable to require MSI `DriverVersion` when every observed OEM Driver row lacks it. Existing `VersionKind` still tells UI which field was used.
3. Use ordinary numeric dotted-component ordering: installed < offered → `UpdateAvailable`; installed == or > offered → `UpToDate`. **Installed newer is not an UpdateAvailable downgrade**.
4. **Do not blindly compare unlike version domains** when the catalog explicitly distinguishes them. For example, the ISS catalog lists `Version=5.8.58.0` and `PackageVersion=5.8.61.0`; the published Latest is the **package** version. The implementation must not silently compare an unrelated component's PnP driver version against that package value. If no reliable cross-domain correspondence, show the best supported installed version and Unknown comparison.
5. Apply the **same** admission policy to matched exact and credible family/INF matches. Eliminate or simplify redundant per-family `ObservedSingleDeviceDriverNames`, `HasPreciseObservedMsiDriverIdentity`, one-component / special `DEV_` checks **only insofar as they prevent legitimate identified comparisons**. Do not introduce a replacement overdesigned verdict registry.
6. Keep BIOS hexadecimal same-board behavior, owned controller Firmware semantics, Intel DSA source precedence/duplicate suppression, NPU existing equality-only cross-kind behavior, UWP sentinel handling and the future-disabled Update button exactly as already implemented.
7. Avoid changing existing public/frontend enum, entry positional record or IPC protocol.

### 2.3 DTS / IntelliGo: remove unusable exact DEV test

Real CG3EM:
- `DTS Driver`: `ExactHardwareId`, 2 matching installed components at `1.16.4.0`, provider DTS; **other DTS `1.10.7.0` components coexist**.
- `IntelliGo APO Driver`: `ExactHardwareId`, 4 matching components at `100.1.4.2709`, provider IntelliGo.
- PnP diagnostic exposes `SWC\VEN_DTSI` and `SWC\VEN_IGO`, **without** the arbitrary `DEV_XXXX` structure required by `HasObservedAudioCatalogHardwareIdentity`.

Fix their existing matching/verdict path to use observed software-component family evidence plus provider/class/installed INF or concrete component identity. **Do not include every DTS package indiscriminately**: `1.10.7.0` is a demonstrably different installed component family; select the existing matched `1.16.4.0` family by independent package/INF/component evidence, never by matching the offered number.

When matched group is coherent and comparable, `Installed (estimated) 1.16.4.0` / `Latest 1.16.4.0` → `UpToDate`; similarly IntelliGo `100.1.4.2709` → `UpToDate`. If independent family distinction is impossible for DTS 1.10.7 vs 1.16.4, preserve Unknown rather than showing an invented verdict; tests must exercise the real mixed-family topology.

### 2.4 MSI Center M: use installed program identity, not GUID only

`ResolveMsiInstalledVersion` Utility path currently relies on matching MSI catalog GUID/UpgradeGuid against Registry ProductCode, missing the installed **MSI Center M** HKLM32 record even though Windows reports `3.0.2608.3101`.

For **MSI Center M**:
- Permit an **exact normalized DisplayName + MSI Publisher** installed-product match as a fallback when GUIDs are missing/mismatched, with a single unambiguous matching installed product and numeric DisplayVersion. It is an update-check display, not permission to restart Center M.
- Keep Registry GUID/UpgradeGuid and approved exact AppX identities as preferred strong matches. If the registry entry has an incompatible product title, non-MSI publisher, conflicting versions or multiple distinct matching products, do not infer a version from publisher alone.
- Compare MSI's `3.0.2608.3101` against the matching Registry `DisplayVersion` when the same product's comparable numeric domain is established. Equal → `UpToDate`.
- **Center M being Disabled must not be interpreted as NotInstalled**. Never start/enable/query MSI Center M processes/services or edit startup tasks.

Existing Realtek Audio Control and DTS Audio Processing AppX mapping/version display must not regress. MSI `UWP` sentinel remains missing latest numeric version, hence `Unknown` comparison.

## 3. Negative boundaries — relax correctly, not blindly

These are **real** incorrect-result risks, not hypothetical race defenses:

- **Multiple Intel System drivers:** Intel provider + System/Chipset class or catalog `PCI\VEN_8086` is not enough to map the same version to HID, chipset, IPF, DTT, Serial IO and ME. Require a meaningful product-family discriminator beyond generic vendor/category.
- **DTS 1.10.7.0 vs 1.16.4.0:** never choose `1.16.4.0` just because it equals MSI; different installed groups may coexist.
- **ISS 5.8.58.0 vs 5.8.61.0:** different local components and separate `Version`/`PackageVersion` semantics; don't fabricate UpToDate/UpdateAvailable.
- **Realtek audio / OED:** don't treat AppX Realtek Audio Control `2.53.394.0` or its separate Uninstall entry `6.0.9738.1` as Realtek Audio Driver `10.0.378.1` without a matching driver identity.
- **Realtek Card Reader:** genuine card reader PnP `10.0.26200.21389` is newer than MSI `10.0.26100.21378`; where their version domain is established, this is **UpToDate**, never an UpdateAvailable downgrade.
- **Unrelated software titles:** do not classify all MSI-published software/Windows Store AppX packages merely by shared `MSI` / `Intel` / `Realtek` / `DTS` tokens.
- **Absent installed product:** unknown is permitted; do not assert NotInstalled unless actually proven (no new status needed).
- **Intel DSA fail/unavailable:** keep MSI family fallback and do not claim it represents the globally latest Intel driver; retain existing source-precedence policy.
- **No new PnP/SetupAPI scan** merely to perform a second matcher pass. Existing WMI/registry/AppX inventory already captures the required evidence. Prefer local family predicates over a new data service.

## 4. Expected implementation footprint (current `main`)

Primary:
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`
  - `ResolveMsiDriverInstalledVersion`
  - `ResolveDriverCandidates` / group selection
  - `ResolveMsiInstalledVersion` Utility branch
  - `ResolveMsiComparisonStatus` + overrestrictive audio/known-family helpers
  - `MsiUpdateCatalogComposer.MapMsiEntry` remains the **one** provenance+verdict calculation point.
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs` (focused reproduction/negative tests).

Touch `MsiLiveUpdateCatalogClient.cs` only if an **actual parser bug** is proven, not to change published versions or endpoints. No reason to modify WinUI 3 page layout, resources, frontend contract, diagnostic file schema or Intel DSA client.

**Small illustrative direction only (not copy/paste without checking existing types):**

~~~csharp
// For an already captured MSI Driver catalog entry:
// 1) exact device/INF matches (when meaningful) take precedence;
// 2) otherwise match a product family using provider + component/class/name/INF;
// 3) group the relevant signed-driver components by proven package/family;
// 4) one coherent installed version => show it (Exact or Heuristic);
// 5) compare only if that displayed version and selected MSI-offered
//    version refer to the same product version domain.
// Do not require candidates.Count == 1 when multiple devices share one
// identified driver package.
// Do not select a candidate based on its version being == offeredVersion.
~~~

The actual work should prioritize **reducing code and duplicated one-off vetoes**, not adding a bigger selector framework. Keep one owner/one resolution path and preserve existing fail-safe comparisons where genuinely ambiguous.

## 5. Automated acceptance matrix (all hardware-free)

Use synthetic but **realistic CG3EM PnP fixture structure** informed by the 21:59 / 22:00 JSON, with correct provider/class, coherent or distinct INF, ID/name/version combinations. Tests must test **the actual production composer method**, not a parallel ad hoc matcher.

### Required positive cases
1. Intel HID Event Filter: named Intel HIDClass / recognized component, installed `2.2.2.17`, offered `2.2.2.17` → Installed populated and UpToDate.
2. Intel Chipset: 3 Intel System components sharing one relevant signed driver package/`oem598.inf`, `10.1.56.28` → Installed populated and UpToDate **despite 3 devices**.
3. Intel Serial IO: several relevant Intel components at `30.100.2534.18`, clear family/INF grouping → Installed populated and UpToDate.
4. Intel Platform Monitoring `7.3.11.3`, Management Engine `2603.9.4.0` and Smart Sound `20.43.12741.0`: matching provider plus meaningful family evidence → Installed populated and comparable UpToDate.
5. Realtek Card Reader: MTD/Realtek/PCI VEN_10EC-DEV_5264, installed `10.0.26200.21389` > offered `10.0.26100.21378` → UpToDate (not UpdateAvailable).
6. DTS `1.16.4.0` and IntelliGo `100.1.4.2709`: coherent **matched software-component package groups** with multiple PnP devices → Installed estimated and UpToDate. Also include DTS `1.10.7.0` from an **unrelated group** and verify it is not selected or used for wrong verdict.
7. MSI Center M with exact normalized name and MSI publisher in Registry, version `3.0.2608.3101`, catalog latest same, mismatched/no OEM GUID → Installed populated and UpToDate.
8. For credible same-family `installed < offered`, return UpdateAvailable and retain the existing **disabled** Update placeholder. For `installed > offered`, return UpToDate; check exact source/version-kind display remains unchanged.

### Required negative/regression cases
- Intel provider/category-only identity and no named/INF/family discriminator → Unknown, not a fabricated match.
- Multiple **relevant** different versions without defensible sub-family separation → Unknown; neither max nor first nor latest-equal chosen.
- ISS dual component `5.8.58`/`5.8.61` with MSI Catalog vs Package version distinction; no unsupported cross-domain ordering.
- DTS mixed installed package groups not attributable from evidence; no Version==Latest candidate selection.
- Missing Windows driver, name/provider mismatch, wrong class, unrelated HWID/INF, spoofed partial vendor ID, registry false-positive Center M title/publisher, two contradictory Center M installations.
- Realtek Audio, Realtek OED and Windows Studio Effects remain honest if component evidence is insufficient; never equate AppX/registry versions with PnP driver versions.
- No regressions: BIOS hexadecimal comparison, owned exact Controller Firmware `4.19`, Goodix, Game Assist, DTT, Trusted Execution, PPM, Intel DSA GPU/Wi-Fi/Bluetooth/NPU matching and suppression, Realtek/DTS AppX numeric versions, UWP sentinel, category counts, full one-line metadata, vertically centered action, Korean/English statuses and disabled Update button.
- Check default Unknown if insufficient version domain; existing serialization and PR #838 privacy-safe bounded JSON snapshot still work.

**Testing:** build solution, run existing Runtime/UI tests and CI. Hardware tests do **not** belong in required Local Codex validation or merge-blocker lists. User performs real A2VM/CG3EM checks after merge.

## 6. PR delivery and stop conditions

- **One PR**, optionally split into internal reviewable commits: (1) installed driver-family matching + grouping, (2) comparison gate cleanup + DTS/IntelliGo, (3) MSI Center M + regression tests.
- No new public contract, new manager, extra background scanning, matching plugin framework, retry/state machine/lock/epoch for theoretical races, or architectural change.
- Do **not** add more one-off numeric-identity whitelist entries just to make this CG3EM log green. Prefer small deterministic and understandable family predicates that work on all supported MSI Claw models; if a family truly needs model-specific HWID differentiation, make that a narrowly documented predicate in the existing matcher.
- No real MSI downloads/installations. No changes to Center M enable/disable behavior, controller routing, HidHide, VIIPER or shutdown/recovery.
- If existing Windows evidence cannot identify a specific package or distinguish version domains, leave that item Unknown **with honest Installed (estimated) where supported**; do not block the whole PR for unresolved rows.
- The PR is complete when the verified matching defects are fixed and automated positive/negative evidence passes. Physical hardware review is **after merge**, by the user.

**Success criterion:** A normal CG3EM manual check no longer returns `NoUsableIdentity` for well-identified installed driver families merely because MSI omitted a precise device ID or INF, and no longer returns `Unknown` solely due to `CandidateCount>1`, `Heuristic`, or DTS/IntelliGo's missing `DEV_XXXX`, while keeping genuinely ambiguous items honest.
