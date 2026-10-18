# Work Order — MSI Update: Installed Versions, Intel DSA Integration, and Safe Update Comparison (PR1 + PR2)

**Date:** 2026-10-18  
**Repository:** onehoon/SteamAddonforClaw  
**Target:** current `main` after squash-merged PR #835; re-read HEAD before coding  
**Implementation owner:** Local Codex  
**Product:** Standalone Full PID1902 MSI Claw Addon, supported A2VM 7/A2VM 8/CG3EM, one Windows administrator user / one interactive session  
**Delivery:** **One architecture/work order, TWO independently buildable and mergeable PRs**, sequential. PR1 first, PR2 based on merged PR1. Keep PRs focused and split commits within each PR by data source/contract/tests as useful.  
**Hardware verification:** The user/product owner conducts real MSI Claw hardware checks **after merge**. Do **not** make physical-device testing a Local Codex requirement, PR blocker or required pre-merge CI step.

## 0. Locked user intent

The current MSI Update page already performs a manual, OEM MSI Live Update **published catalog** lookup and has four fixed, compact, equal-width tabs. Its next feature shall answer:

1. **Which version is actually installed on this Windows MSI Claw?**
2. **Which latest version is offered for this exact component/device?**
3. **Is there an evidence-backed newer version, is the installed version already current/newer, or is the comparison indeterminate?**

**Source priority is NOT "MSI for everything."** MSI's Intel driver catalog can lag Intel. For **Intel Arc GPU, Intel Wi-Fi, Intel Bluetooth, and Intel NPU**, **Intel DSA catalog takes precedence when a compatible, hardware-ID-matched candidate exists**. For BIOS, MSI controller firmware, MSI-specific/other drivers and MSI utilities, retain the OEM **MSI Live Update** catalog. Preserve which source supplied each "latest" version.

**Scope is lookup/display/comparison only. NO downloads or installations.** The term "installed version" refers to **locally detected current software/driver/firmware state**, not installing a new version.

User-approved UI/lifecycle contract remains:

- Only **Check for updates** explicitly starts a fresh remote + local inspection.
- Every genuine navigation back into MSI Update resets to **Not checked**, clears previous records/counts, and does not auto-query.
- Switching BIOS/Firmware/Drivers/Utilities **within** the same visit preserves the current result; no new fetch on tab change.
- No persistent result cache, last-check timestamp, update scheduler, startup probe, toast, task-tray badge, Beta opt-in UI, installer button or "Update all".
- Keep existing centered/equal-width native WinUI 3 TabView, consistent-width cards, one outer vertical scroll, and compact entry layout.
- Preserve catalog-only caution where matching/applicability is unknown. Never present an unsafe "update available" decision merely because a number is larger.

## 1. Required reading / authority

**Read current source**, not only the excerpts below:

### Full1902 policy / standalone boundary

- `docs/Full 1902 Implementation/README.md` (authority precedence)
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

No changes to controller physical ownership, Center M Enabled/Disabled authority, PID1901 ↔ PID1902, HidHide, VIIPER, Steam presentation, WING suppression, startup/restart/suspend/resume or stock-safe restoration. **No CTW integration**; CTW is read-only research/reference material.

### Previously completed MSI Update work

- `docs/work-order/2026-10-18_MSI_LIVE_UPDATE_CATALOG_READ_ONLY_DEVICE_UI_WORK_ORDER.md` (historical original catalog work; Device expander placement superseded)
- `docs/work-order/2026-10-18_MSI_UPDATE_PAGE_COMPACT_UI_AND_FRESH_ENTRY_WORK_ORDER.md` (active UI/re-entry policy, implemented by #835)
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` (`RequestMsiLiveUpdateCatalogAsync`)
- `src/SteamInputAddonforClaw.Contracts/Frontend/MsiLiveUpdateContracts.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml` and `.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw` and `ko-KR/Resources.resw`
- `tests/SteamInputAddonforClaw.Tests/MsiLiveUpdateCatalogTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/MsiLiveUpdateCatalogUiTests.cs`
- `tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs`
- `src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs`
- `src/SteamInputAddonforClaw/Controllers/Detection/WindowsControllerDeviceEnumerator.cs`
- Existing controller control-HID attribute/HidDevice paths and read-only firmware-version logic before reusing them.

### Reverse engineering evidence

**`MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md` (2026-10-18)**, especially sections **3, 4, 7, 8, 9**. Locate this supplied research artifact through authorized Project/Library/Google Drive access, not a public search. The REA documents the distinction between a raw server catalog and **installed/applicable** updates. Do not treat every returned row as an applicable installed product.

In the REA, `DataSet.GetDrvLst_FromLocalComputer` and `SysInfo.GetPnPSignedDriverList_SetupApi` / WMI inspect local PnP drivers; uninstall registry and Get-AppxPackage cover applications; `DataSet.GetCurrentGamePadFwVersion` derives a PnP `REV_` display version rather than directly querying controller firmware; `ServerHasNewerVersion`, `CompareVersionByHardWareIdInSetupApi`, `CompareVersionByGuidInReg`, etc. match the source appropriate to the record. Missing controller revision can lead MSI's own code to display a fallback `1.31`: **do not import that invented installed-version fallback**.

The A2VM REA remote sample `Claw 8 AI+ A2VM / kr / win11` returned 2 BIOS, 1 firmware, 21 driver, 7 utility records. These are **catalog counts** and **not local update-available counts**. The `Intel ME FW Update Tool` record appears in BIOS results but is **not itself the BIOS firmware image**. MSI's BIOS-version check requires the **same board prefix**, hex revision comparison and special care for test (`T`) BIOS builds.

### CTW-dev fork (reference; never a runtime dependency)

Inspect branch `release/v0.3.98.0` (or locate its newer applicable equivalent, documenting changes if different):

- https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Services/MsiClawDriverCheckService.cs
- https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Services/DriverMatchUtil.cs
- https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Services/IntelDsaCatalogService.cs
- https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Devices/MSIClaw/MsiClawLedController.cs

CTW uses a **curated MSI manifest + Intel DSA data**, and detects local versions with WMI/Registry/HID. We already have a working **OEM MSI catalog**, so **do not copy CTW's curated manifest, startup caching, update notifications, install/download path, helper IPC or UI**. Its `DriverMatchUtil` fuzzy token strategy is not sufficient by itself to assert "Installed", "Not installed" or "Update available". It can incorrectly select unrelated Intel software; favor exact hardware identity or explicitly proven component-specific match. The CTW source has its own licensing; use it as behavior/reference evidence rather than wholesale copying.

### User-supplied Intel archive

`idsa-en.zip` (user attachment in this conversation, 2026-10-18); use it as **development/test evidence**, not as a bundled runtime catalog.

Verified archive contents/shape:

- ZIP includes `meta.json` and `software-configurations.json` **alongside DLLs and other files**.
- `meta.json.Created = 2026-10-16T11:00:03.6528776Z`; `software-configurations.json`: **33 packages / 102 components**. This is a snapshot, not proof of what the network will return later.
- Typical package fields: `Id`, `Name`, `Version`, `DisplayReleaseDate`, `IsBeta`, `Url`, `Files`, `Components`.
- Component fields: `Category`, `Version`, `DetectionValues`, `OperatingSystemOverrides`, etc.
- Example Arc package `930514` has package version `32.0.101.9150 WHQL Certified`, Graphics component version `32.0.101.9150`; Wi-Fi package `926939` version `24.70.0` has Wireless component `24.70.0.3`; Bluetooth package `927576` version `24.70.0` has Bluetooth component `24.70.0.4`.
- **NPU package `927127`** is named `Intel® NPU Driver - Windows*`, package `Version=32.0.100.5540`, `IsBeta=false`, component `Category=OtherDrivers`, `Version=null`, DetectionValues including `VEN_8086&DEV_643E`, `VEN_8086&DEV_7D1D`, `VEN_8086&DEV_AD1D`, `VEN_8086&DEV_B03E`, `VEN_8086&DEV_FD3E`. **This is NOT sufficient proof that the package Version equals every installed PnP driver's DriverVersion.** Use it for the **latest package** display, but withhold a numeric newer/up-to-date decision unless comparable-version semantics are proven.
- Intel DSA records can use wildcards within HWID segments, for example `VEN_8086&DEV_A840&SUBSYS_*8086`; matching must be segment-aware, not substring/name matching.
- The ZIP contains executable DLL resources. **Only read the intended JSON entries; never load/execute the archive's binaries.**

## 2. Verified current implementation constraints

Current `MsiLiveUpdateCatalogResultParser.ParseCategory` drops all but `name/title`, `displayVersion/version`, `releaseDate/release_date`, `description`. Internal record is:

~~~csharp
internal sealed record MsiLiveUpdateCatalogEntry(
    string Name, string? Version, string? ReleaseDate, string? Description);
~~~

Frontend record is:

~~~csharp
public sealed record FrontendMsiLiveUpdateCatalogEntry(
    string Name, string? Version, string? ReleaseDate, string? Description);
~~~

`MsiLiveUpdateCatalogContractMapper` is in the same `MsiLiveUpdateCatalogClient.cs` file. The **original OEM helper returns raw JSON** in each category and the Runtime does the parsing; do not change original MSI DLL/credentials, x86 helper, privilege model or transport just to add fields.

The Runtime has existing usable local read paths, though they are not a directly reusable ready-made MSI installed-version matcher:

- BIOS read in `InProcessAddonFrontendControl.ReadFanProbeFirmwareIdentity` uses `HKLM\HARDWARE\DESCRIPTION\System\BIOS\BIOSVersion` (currently returns a fan-probe display string; do not reuse that **string presentation** as a typed BIOS version).
- `EnvironmentDiscoveryReportGenerator` already enumerates 32/64-bit HKLM + HKCU Uninstall registry entries and `PackageManager().FindPackagesForUser(string.Empty)` for AppX/UWP.
- `WindowsControllerDeviceEnumerator` already uses SetupAPI and exposes PnP Hardware/Compatible IDs.
- Existing controller LED/back-button read code accesses PID1902 HID version attributes. This is **controller HID revision**, not a license to change the physical native mode for update checking.

`MsiUpdatesPage` currently caches one page instance, resets it on genuine navigation via `Activate`, cancels in-flight request via `Deactivate`, shows four tabs, and renders a compact card with a `Grid(*,Auto)` future action area. Retain all of that and display the new information inside the existing cards.

## 3. Desired domain/source rules

| Row family | Primary latest version source | Local installed source | Comparison policy |
| --- | --- | --- | --- |
| Intel Arc GPU | **Intel DSA** | exact Intel display PnP device + `DriverVersion` | Component version vs same-device installed driver version |
| Intel Wi-Fi | **Intel DSA** | exact Intel NET/WLAN PnP device + `DriverVersion` | Component version vs same-device installed driver version |
| Intel Bluetooth | **Intel DSA** | exact Intel Bluetooth device/USB PnP + `DriverVersion` | Component version vs same-device installed driver version |
| Intel NPU / AI Boost | **Intel DSA** (`OtherDrivers`, identified by NPU package + Intel NPU HWID) | exact Intel NPU PnP device and `DriverVersion` | Show installed driver & latest **package** version; compare only if same-version semantics are separately proven |
| BIOS image | **MSI Live Update** | exact board-specific BIOSVersion registry | Same board prefix; MSI hex suffix; no speculative comparison for test builds |
| Controller firmware | **MSI Live Update** | read-only exact physical Claw HID bcdDevice/revision or corresponding PnP REV | Version only when exact device/model relation is established; no default 1.31; no PID switch |
| MSI/other drivers | **MSI Live Update** | matched SetupAPI/WMI PnP driver, INF, class/provider or catalog GUID | Compare only with a reliable same-component/driver version |
| MSI desktop utilities | **MSI Live Update** | matching exact uninstall product code/UpgradeCode or other documented vendor key | Compare if same product and same version type |
| UWP utilities | **MSI Live Update** | Windows PackageManager with exact package family/name where known | Show installed package Version; MSI `UWP` sentinel is **not numeric version**; no fabricated numeric update decision |
| Intel ME FW Update Tool | **MSI Live Update** | dedicated proven ME firmware/package installed version only if available | **Never use BIOSVersion as installed ME version**; otherwise Unknown |

**Precedence and de-duplication:**

1. Build candidates from both sources for this **one user-initiated check**.
2. Identify the relevant **actual installed Intel hardware** via PnP identity (Vendor=8086 or appropriate USB VID; class/function). Match against DSA `DetectionValues`. Do not select by a mere substring of a marketing device name.
3. For matched GPU/Wi-Fi/Bluetooth/NPU domains, prefer a valid, non-Beta, compatible DSA record. **Do not also show a duplicate MSI card** for that exact same resolved device/component. The four UI tabs remain unchanged; Intel rows appear in **Drivers**.
4. If DSA cannot be queried or no valid HWID match exists, keep the **original MSI record** when present, **label its source MSI**, and do not imply MSI is Intel's latest or that Intel is current. If no usable MSI row exists, show a bounded unknown/unavailable state rather than fake version `0` or a fabricated entry.
5. Do not broadly remove all records containing "Intel"; Intel chipset, IPF, ME, Serial IO, DTT, SST and other distinct OEM driver components remain MSI-sourced unless an exact policy explicitly moves them.
6. Preserve published records for genuinely unknown/not detected components; **not detected != not installed**. Do not infer user hardware from the remote model catalog alone.
7. Do not hard-code screenshot/ZIP sample versions, number of cards, or counts. The **tab count is the number of visible resolved entries returned in the combined snapshot**; it is not the number of updates. The exact 2/1/21/7 values from the A2VM MSI-only sample are not an eternal UI contract.
8. Preserve separate per-source partial-error information so MSI failure does not blank successful Intel results (and vice versa); avoid a new general update-engine state machine.

## 4. Intel DSA query design (Runtime only)

Use the CTW-confirmed data endpoint as a **best-effort data feed**:

~~~text
GET https://dsadata.intel.com/data/en
=> ZIP bytes (bounded download, timeout, cancellation)
=> open only software-configurations.json and optionally meta.json
=> parse packages and Components / DetectionValues
=> compare against locally present hardware
~~~

- Do **not** install or run Intel DSA; do not call an Intel software installer; do not launch anything.
- Intel DSA endpoint is a **non-contractual data source** (not a guaranteed stable public SDK). Network 403/404/timeout, bad ZIP, missing JSON, changed schema, oversized response, or no HWID match must yield **IntelUnavailable/Unknown**, without losing successful MSI data.
- Use a reasonable bounded HTTP deadline (for example the CTW ~20 s budget), max compressed size (~4 MiB or another justified bound), max JSON entry size/depth, entry counts and cancellation support. A single parsed ZIP per Check; no disk writes or persistent cache, no background loop. Dispose HTTP response/streams.
- ZIP also contains `DSARules.dll` etc.; **never execute/load them**, unpack to disk, or attempt a complete Intel DSA rule-engine reimplementation. Read JSON only.
- Extract `Id`, `Name`, package `Version`, `DisplayReleaseDate`, `IsBeta`, `Url`, component `Category`, `Version`, `DetectionValues` and any explicit OS component restrictions relevant to choosing a candidate.
- Exclude Beta by default (`IsBeta=true`); **do not invent a new Beta toggle in this work**.
- Match **component category + hardware identity**. GPU `Graphics`; Wi-Fi `Wireless`; Bluetooth `Bluetooth`; NPU requires explicit **NPU package identity / appropriate `OtherDrivers` component**, not every `OtherDrivers` row.
- Support PCI and USB identification, normalized case, manufacturer/device class, `VEN_`/`DEV_` and `VID_`/`PID_`, optional `SUBSYS`/`REV` and segment-local `*` wildcard. Reject broad one-token matches that accidentally target unrelated devices; do not misinterpret wildcard across `&` or `\\`.
- Filter unsupported OS when explicit per-package/component OS restrictions prove a mismatch. The **full proprietary DSA rules engine is outside scope**; do not claim all DSA applicability constraints have been evaluated.
- Prefer the component version for GPU/Wi-Fi/Bluetooth rather than the package version: e.g. Wi-Fi package `24.70.0` vs driver `24.70.0.3`, Bluetooth package `24.70.0` vs driver `24.70.0.4`.
- **NPU exception:** component `Version=null`. Use the package `Version` labeled as "Latest Intel package", preserve that version's type, and mark comparison **Unknown** until a defensible local-versus-package equivalence is shown. Do not synthesize a component version from the package version just to set "Update available".
- Select the newest compatible candidate of the *same device and component family* using numeric version comparison only for comparable typed versions; never choose a package for another GPU generation just because its number is larger.
- Capture `PackageId`, component category, source, version kind, matched local hardware identity and match reason as **minimal diagnostic provenance**, without displaying raw device instance identifiers in the UI or persisting PnP inventory. Maintain existing transport input/output size limits.

### Suggested narrowly-scoped code placement

- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/IntelDsaCatalogClient.cs` (read-only ZIP fetch/parser)
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs` (short-lived Windows installed-version snapshot)
- Keep simple matching/comparison helpers under existing `Devices/MSI/LiveUpdate/` rather than creating new shared architectures/feature managers, generic provider plugin interfaces or another background service.

These names are suggestions, not mandatory additional layers. Keep interfaces only when directly needed for deterministic tests; prefer injecting narrowly scoped HttpClient/data providers at test seams rather than adding a factory framework.

## 5. MSI catalog identity preservation

The current OEM helper already delivers the raw category response to `MsiLiveUpdateCatalogResultParser`. Extend the **Runtime-side record/parser** with only metadata actually useful for installed-version matching, following actual response spelling/aliases; for example:

~~~csharp
internal sealed record MsiLiveUpdateCatalogEntry(
    string Name,
    string? Version,
    string? ReleaseDate,
    string? Description,
    string? DeviceId = null,
    string? Guid = null,
    string? UpgradeGuid = null,
    string? PackageVersion = null,
    string? DriverVersion = null,
    string? InfName = null);
~~~

This is an **illustrative shape**, not an order to assume all fields exist in every OEM record. The REA reports `deviceid`, `upgradeGuid`, `packageVersion`, `driverVersion`, `GUID` and other types; confirm against representative raw tests/sample response before mapping. If missing, do not invent an identifier. Keep source payload length/character sanitization and parser bounds.

- Preserve `name`, `version`, `releaseDate`, `description` compatibility and original four category ordering.
- Do not pass executable commands, MSI download URLs, sensitive request data or unbounded raw record JSON into the frontend solely for comparison.
- Preserve the OEM DLL's installed-path security, HKLM SDK registration, signed request implementation, elevated worker boundary and helper arguments untouched.
- Map to the existing frontend response as **optional additional safe fields**. The frontend UI should receive only the provenance/display and comparison fields actually needed. Avoid duplicating the entire raw MSI/DSA JSON inside an RPC snapshot.

## 6. Installed-version inventory (on manual Check only)

Collect a **single short-lived local snapshot per explicit Check**, before applying source-specific comparisons. Work in the existing elevated Runtime, which already owns Frontend operations; never give the UI direct device/registry authority.

- **BIOS:** read `HKLM\HARDWARE\DESCRIPTION\System\BIOS\BIOSVersion` and/or the existing Win32_BIOS provider as appropriate. Preserve the actual board-prefix string. Identify a BIOS image row by supported catalog metadata, not simply because the request tab is "BIOS".
- **Controller FW:** prefer the existing exact-controller control HID's read-only version attribute when accessible under current supported authority. HID `0x0230` displays `2.30` where BCD interpretation is established; match exact Claw VID/PID/physical identity. Under Center M/stock authority, or if the control HID is unavailable, a matching PnP `REV_####` is a **heuristic**: preserve source/confidence. Do **not** switch PID1901/1902, seize an unavailable controller, open a new write session, issue any feature reports or mutate controller state just to read this version.
- **PnP drivers:** WMI `Win32_PnPSignedDriver` (`DeviceID`, `HardWareID`, `DeviceClass`, `DeviceName`, `DriverProviderName`, `DriverVersion`, INF when exposed) plus narrowly scoped existing SetupAPI enumeration where needed. Avoid launching `pnputil`/DISM and parsing whole CLI output if WMI/SetupAPI supplies the exact evidence. Do not treat the physical PID1902 Microsoft HID driver as the same component as a vendor controller firmware package.
- **Classic software:** HKLM 64/32 and HKCU Uninstall `DisplayName`, `DisplayVersion`, `Publisher`, product GUID when available; use exact known registry values (e.g. MSI Center M `Package_Version`) when appropriate. Read-only.
- **UWP:** use existing Windows `PackageManager` approach to obtain package Name/Family/Version, rather than spawning PowerShell for ordinary reads. Use a correct *exact package identity* to match; avoid string similarity alone.
- **Provisioning/ME:** where a specific row has a validated source/key, read it; otherwise retain Unknown. Do not create a generic unknown package scanner.
- **Partial failures:** inaccessible WMI/AppX/registry or unavailable physical device are ordinary supported scenarios; return per-source unavailable evidence rather than failing the whole query.
- Do not use data from a previous navigation or old local report. Don't reuse `EnvironmentDiscoveryReportGenerator.Generate()` indiscriminately: that is a **full diagnostic sweep** including unrelated processes/services, not a cheap MSI Update snapshot. Reuse its narrow readers/patterns only.

## 7. Comparison semantics (implemented in PR2)

**A reliable identity match is a prerequisite for a meaningful version comparison.**

Recommended exposed comparison outcomes:

| Outcome | Meaning / condition |
| --- | --- |
| `UpdateAvailable` | Exact same target device/package and **comparable installed and latest versions**; latest is strictly newer |
| `UpToDate` | Exact same target and comparable values; installed equal to or newer than latest |
| `Unknown` | Missing/ambiguous identity, incompatible version types, partial query, unavailable local source, unparseable/version sentinel, uncertain applicability |
| `NotInstalled` | **Only** positively verified absence by exact package identity with a reliable complete local inventory; otherwise use Unknown. Do not use absent fuzzy name match as proof. |

Do not tell users "Update available" for a merely newer **package** than an unrelated PnP **driver**, a different device generation, a different MSI BIOS board, an MSI `UWP` sentinel, or an older MSI Intel row when the newest DSA query failed.

### Version comparison details

1. **PnP component version**: parse explicit dotted-number component tuples from the exact matching driver and the latest source, with bounded segment values. Avoid silently padding ambiguous major/minor-only vs full four-part versions with zeros unless format compatibility is proven.
2. **MSI BIOS**: e.g. `E1T52IMS.113` vs `E1T52IMS.114` with equal board prefix and hex-encoded suffix; `E1T52IMS.10F` precedes `E1T52IMS.110`. Different prefixes => Unknown, never cross-board update. Local test-build suffix `T` => Unknown (no ordinary "update available" badge).
3. **Controller firmware**: use exact validated BCD/PnP revision-to-semantic version mapping; a fallback/default, wrong PID or unknown model mapping => Unknown.
4. **MSI utilities**: numeric version comparisons only when exact product and identical version types; Store/UWP sentinel is not a number.
5. **NPU**: current reference ZIP has package version `32.0.100.5540` and **null** component version. A precise NPU HWID match gives a **candidate package**, but **not automatically** a comparable installed driver-version pair. Show both clearly and hold comparison Unknown unless Local Codex finds an evidence-backed version contract in actual Intel metadata/source.
6. `Installed > offered` is **UpToDate** (or "Installed newer" if the existing UI can show this without adding another state), never an available downgrade.
7. If no comparison can be made, prefer an honest label like **"Unable to determine"**; avoid "Not installed" except proven absence. An unknown state must never count toward the update-available total.

Implement the smallest direct category-specific comparison functions; no generic version-framework/strategy registry needed.

## 8. UI behavior

Use existing four fixed localized tabs and the same one-scroll compact cards from PR #835.

**After a successful manual scan (illustrative data only):**

~~~text
MSI Update                                           [Check for updates]
Published sources: MSI / Intel DSA

[ BIOS (2) ][ Firmware (1) ][ Drivers (N) ][ Utilities (7) ]

Intel Arc Graphics                                      Intel DSA
Installed: 32.0.101.xxxx  ·  Latest: 32.0.101.yyyy
Released: yyyy-mm-dd                   [Update available]  <-- PR2 only

Realtek Audio Driver                                        MSI
Installed: 6.0.xxxx  ·  Latest: 6.0.yyyy
Released: yyyy-mm-dd                 [Unable to determine] <-- if ambiguous
~~~

Retain a compact layout: title, one metadata row where it fits, optional date/source/status; wrap gracefully at 1920×1200 / 150% and other DPI. Keep the existing `Grid(*,Auto)` future right-action area. **Do not add an Update button or blank placeholder**. Do not reintroduce descriptions, inner scrolling, redundant last-checked text, new top-level tabs, or enlarged multi-card nesting.

- Add localized `Installed`, `Latest`, `Latest Intel package`, `Source: Intel DSA`, `Source: MSI`, `Update available`, `Up to date`, `Unable to determine` and per-source failures to **both** RESW files using the current Main UI string access convention.
- Before Check, or after navigation away and back, show **no previous installed/latest versions and no previous counts**.
- A second Check during the same visit must fetch both remote sources again and recollect local inventory. Inner tab changes must not.
- Keep the existing page-local request CancellationTokenSource and active-request identity check from #835. Propagate cancellation to HTTP and local scan where feasible; when navigated away, discard old results. No new gate/epoch/manager.
- Distinguish MSI and Intel per-source query errors. Partial success must show successful results with a concise explanation that some comparisons are unavailable; do not claim "Everything is up to date" when any required source is missing.
- For an Intel row that falls back to MSI after DSA failure, show **MSI as the source**, not `Intel DSA`, and do not assert it is the newest overall Intel version.

## 9. PR split — required

### PR1 — Read-only installed-version + Intel DSA data foundation and UI projection

Implement together:

1. Extend MSI raw-response parsing to retain **validated, bounded matching identifiers** as optional internal fields; preserve existing card/category behavior on legacy responses.
2. Add bounded, cancellable **Intel DSA ZIP/JSON fetch+parser** and precise hardware-ID matching for GPU, Wi-Fi, Bluetooth and NPU, including Beta exclusion and NPU package-version exception.
3. Add short-lived local installed inventory (BIOS / controller revision / PnP / Uninstall / AppX) using existing Windows read paths and exact supported identifiers. No device mutations.
4. Integrate both sources into **one explicit manual Check RPC**, composing resolved Intel rows into the **Drivers** tab while keeping other MSI records and source/failure provenance. **Keep existing request method/pipe**; extend its result minimally instead of adding a second disconnected frontend query.
5. Display **installed version, offered version and source**, clearly distinguishing "latest driver" from "latest package" for NPU. **No UpdateAvailable / UpToDate status yet**; do not preempt PR2 with speculative partial comparisons.
6. Retain PR #835 fresh-entry lifecycle, cancellation and single-scroll UI.

**PR1 acceptance:** Full build/test/publish green; deterministic fixture tests establish exact matching, correct Intel source precedence, component vs package version handling, partial failures and serialization; MSI helper behavior unchanged; UI displays installed/source/latest values but **no update verdict**. PR1 can be merged and released independently.

### PR2 — Safe, evidence-backed version comparisons and update-state UI

Based on merged PR1:

1. Implement the strict typed comparison rules in sections 3/7 using the identity provenance available from PR1.
2. Add per-entry outcome `UpdateAvailable / UpToDate / Unknown` (plus `NotInstalled` **only if proven**) and the localized status display. For NPU, remain Unknown if no comparable version type is established.
3. Preserve precise source labeling, MSI-vs-Intel fallback behavior, category counts as **entries**, not available-update counts.
4. Ensure all uncertain/failed source cases render a truthful state and are **not counted as available updates**. No "Update all" UX.
5. Add comparison/transport/UI regression tests. Keep controller ownership and packaging behavior untouched.

**PR2 acceptance:** Full build/test/publish green; same-board BIOS hex handling, newer/equal/installed-newer, mismatched models, malformed version, MSI UWP sentinel, NPU null component version, no-false-positive Unknown, and mixed-source partial failures are covered. PR2 is independently mergeable with no pending hardware gate.

## 10. Automated test matrix (Local Codex)

Use small deterministic **code/test fixtures**. The user-supplied DSA archive is evidence, not a license to commit the full Intel ZIP (it contains DLLs) or redistribute it. Create minimal synthetic JSON/ZIP fixture(s) built in tests.

**PR1**
- ZIP download 200 success, network timeout/non-200, invalid ZIP, missing `software-configurations.json`, invalid JSON, oversize, cancellation.
- Parse `meta.json` optional; package/category parsing including IsBeta, display release date and aliases.
- GPU/Wi-Fi/Bluetooth component version vs package version; e.g. Wi-Fi `24.70.0` vs `24.70.0.3`, Bluetooth `24.70.0` vs `24.70.0.4`.
- NPU `OtherDrivers` component Version null, package `32.0.100.5540`, hardware-specific match; prevent unrelated `OtherDrivers` capture.
- Wildcard detection `SUBSYS_*` does **not** cross segments, and more specific compatible IDs take precedence over weak IDs.
- Hardware-ID mismatch never produces an Intel row for the wrong device; distinct Intel domain rows do not swallow MSI IPF/ME/chipset components.
- MSI raw legacy parser still accepts source records with no new metadata; optional fields survive mapper/frontend pipe serialization.
- WMI/PnP/Registry/AppX partial failures result in Unknown evidence, not fake installed version.
- New manual check collects fresh values; leaving page cancels/discards in-flight combined result; inner tab switches do not query.
- App en-US/ko-KR resources and .xbf/PRI publish layout validated.

**PR2**
- True newer/same/installed-newer numeric driver versions with exact component identity.
- Same-prefix BIOS hex: `10F` < `110`; different board or local test suffix `T` => Unknown.
- No attempt to compare `Intel ME FW Update Tool` against BIOSVersion.
- No cross-PID false controller-firmware comparison; inaccessible physical device => Unknown.
- Missing/ambiguous product GUID or duplicate device matches => Unknown, not NotInstalled.
- MSI UWP sentinel `UWP` not comparable to numeric AppX package version.
- NPU null component version does not become a false UpdateAvailable claim simply because package version is higher.
- DSA failure with MSI fallback exposes source MSI, Intel freshness unknown; partial source failures do not erase other successful entries.
- Tab counts remain visible-entry counts, not available count; no install buttons/download side effects.
- Standard build, UI tests, Runtime tests, existing helper/security tests and publish checks succeed.

Include source-level tests where appropriate but do not rely *only* on substring assertions for version matching and precedence: pure helper tests with realistic minimal identity fixtures are needed. Do not make actual hardware, DSA availability or MSI cloud availability CI prerequisites.

## 11. Explicit non-goals / overengineering guard

Not in either PR:

- Driver downloads, ZIP extraction to disk, EXE/MSI launch, BIOS flash, firmware flash, Microsoft Store launch, driver rollback, silent installs, Windows Update orchestration or installer progress UI.
- Intel DSA installation/service/browser automation or direct execution of DSARules DLLs.
- MSI SDK/HID write, original OEM request signing and helper discovery changes.
- Auto-check at startup/open, last checked persistence, background scanner, telemetry, shared server cache, per-driver ignore/mute, Beta toggle, NPU generic package manager, taskbar notifications, global update worker.
- CTW integration or CTW's persisted manifest/caches/driver updater UI.
- A second system-wide PnP/driver inventory owner or full EnvironmentDiscovery diagnostic scan on every click.
- Additional ownership gates, retry loops, epoch/state machines, lock layers or abstraction solely for theoretical interleavings; **existing frontend cancellation/operation gate is sufficient** for normal navigation and normal operation failures.
- Unsupported multi-session/RDP/Fast User Switching/multi-admin identities.

The goal is **one simple read-only update comparison feature owned by the existing Runtime**, not an update-management framework.

## 12. PR description / implementation handoff

Each PR must identify exactly:

1. Files changed and source boundaries respected.
2. Actual source and version type for GPU/Wi-Fi/Bluetooth/NPU, and why a row is comparable or Unknown.
3. Any MSI row where the OEM response lacked identifiers; list what remains unresolved rather than inventing a mapping.
4. How the current single manual Check, per-visit reset, cancellation and partial failures were preserved.
5. Automated tests and build/publish results.
6. Whether a feature is unverified on hardware: **document only as product-owner post-merge validation**, never as a blocker.

**Final functional requirement:** Users should be able to read the actual installed version and confidently understand **which provider** offers a comparison version. Only a verified same-device, same-version-type comparison may claim an available update. Every other case must remain transparently indeterminate, without touching MSI Center M or Full1902 controller lifecycle.
