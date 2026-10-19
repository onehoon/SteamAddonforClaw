# Work Order — Post-PR #840: EX/CG3EM Fixed MSI Driver Mapping and Version Verdict Correction

**Date:** 2026-10-19  
**Target:** One PR on current `main` (PR #840 already merged). Code by local Codex, hardware verification by product owner after merge.  
**Product:** Standalone Full PID1902 SteamAddonforClaw; no CTW integration.

## 0. Locked direction — replace generic heuristic discovery with device-specific static matching

**This order replaces/supersedes the earlier, overly generic matching proposal previously saved at this same path.** The product is **NOT** a universal PC driver updater. Its supported Claw models are fixed: `msi.claw.cg3em`, `msi.claw.a2vm.7`, `msi.claw.a2vm.8`.

The current evidence and this PR concern **EX = CG3EM**. Define a **small static mapping** from **MSI EX catalog driver name → known Windows hardware/component identifiers (from MSI's extracted EX driver package)**, then read the **currently installed version dynamically** from the existing Windows inventory. Do not rediscover the catalog-to-device relationship on every check via a generic vendor/name/score/heuristic engine.

- **Fixed:** supported model + MSI catalog name + package's meaningful hardware ID(s) / software component IDs / provider/class/representative INF-family evidence.
- **Dynamic on each manual check:** MSI catalog's published Latest version, Windows present device/installed driver version, actual match/no match, resulting `UpdateAvailable` / `UpToDate` / `Unknown`.
- **Never hardcode installed versions, MSI latest values, OS-assigned `oem###.inf` names, or specific installed DriverStore paths** as static lookup keys. These change after updates/reinstall.
- **Model isolation:** a CG3EM mapping must not accidentally apply to A2VM 7/8. Their mapping tables require their own package evidence later; leave their existing behavior unchanged in this PR. Keep current Intel DSA precedence unchanged.

### Explicit scope exclusions

**MSI Drivers for EX/CG3EM ONLY.** Do not implement, redesign or test functionality for:
- Intel DSA entries or Intel DSA duplicate/suppression logic (leave current code as-is).
- BIOS entries or controller Firmware entries (leave currently working logic as-is).
- UWP / Microsoft Store / AppX rows (leave as-is).
- Utilities including MSI Center M; their installers and registry mappings are not part of the supplied EX **Driver** source set.
- All downloads/installers/driver execution or BIOS/firmware flash. The Update button remains present but **disabled, without Click/Command**.

No additional Windows data collection, new IPC, background update checking, package cache, CTW integration or controller lifecycle changes.

## 1. Mandatory sources

Read together before implementation:
- `docs/Full 1902 Implementation/README.md` and authority documents in that folder (`FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`, `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`, `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`, `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`).
- Prior PR2/#839 and post-839/#840 work orders; this current order takes precedence over strict one-device/DEV/DriverVersion-only gates and earlier proposed universal family discovery.
- Current `main` code:
  - `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`, `MsiUpdateCatalogComposer` (`Compose`, `ResolveMsiDriverInstalledVersion`, `ResolveDriverCandidates`, `ResolveMsiComparisonStatus`, `MapMsiEntry`).
  - `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs` (source data, no parser change unless actual bug).
  - `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` (already knows `hardware.Model`; one manual request).
  - `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`.
- **Official MSI EX driver package extracts, already unpacked by user:** `GoogleDrive\Addon\MSI Update\Driver\EX` — [Google Drive EX folder](https://drive.google.com/drive/folders/16cehj5hLXcamcf49f_P2pgVHLUpfPhjv).
- **CG3EM installed evidence after PR #840 / app v0.1.405.0:** `GoogleDrive\Addon\Log\1019-MSIUPDATE\msi-update-20261019-125953.549-e06e67b6.json`, `msi-update-20261019-130041.900-39b44896.json`. Source query succeeds, 81 PnP rows collected, many existing MSI Driver rows still have `NoUsableIdentity`.
- MSI OEM REA `GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md` (Section 11). Do not call mutable MSI `DataSet` scanner.

**Very explicit:** The `EX` folder contains **both extracted directories and ZIP files**. **Read ONLY ALREADY-EXTRACTED directories, INF, BAT/TXT/readme where useful. Do not open, list ZIP internals, unzip or analyze archives.** No installer execution. EX package folders are **design-time references only**; the production app must not depend on Google Drive or local extracts.

## 2. EX MSI catalog → package mapping coverage

The EX extracted root has **19** MSI Driver folders. Each maps to a corresponding MSI catalog Driver title. For each, inspect extracted source INF files (`[Version]`, `DriverVer`, `[Manufacturer]`, Models sections, PnP HWID/CHID/SWC IDs) and record the **stable identifiers** required for runtime lookup.

| MSI EX catalog driver row | Unpacked EX folder name | Mapping intent |
|---|---|---|
| Windows Studio Effects | `Windows Studio Effects` | Inspect AudioComponent / CameraComponent / Extension INF; choose representative versioned driver component, not a random Intel Camera device |
| Intel HID Event Filter | `Intel HID Event Filter` | Match documented Intel HID ACPI device / HIDClass driver family |
| Integrated Sensor Solution | `Integrated Sensor Solution` | Distinguish ISS driver `5.8.58.0` from catalog `PackageVersion=5.8.61.0`; do not blindly compare different version domains |
| Intel Chipset Driver | `Intel Chipset Driver` | `PantherLakeSystem.inf`, `PantherLakeSystemNorthpeak.inf` (both `DriverVer=10.1.56.28`); match their *explicit Intel Panther Lake IDs*; multiple devices are legitimate |
| Intel Platform Monitoring Technology Driver | `Intel Platform Monitoring Technology Driver` | Extract PMT INF HWID(s), distinguish Intel System from other System-family drivers |
| Intel Serial IO Driver | `Intel Serial IO Driver` | Extract LPSS/Serial IO model IDs; multiple matching PnP devices are legitimate |
| Intel Dynamic Tuning Driver | `Intel Dynamic Tuning Driver` | Map extracted DTT component(s), preserve working exact Installed comparison |
| Intel IPF Driver | `Intel IPF Driver` | Extract IPF IDs and distinguish real `2.3.20303.5058` family from unrelated local `2.3.20306.4` components |
| Intel Management Engine Driver | `Intel Management Engine Driver` | Extract ME/CSME/MEI IDs and identify correct Intel System driver, not generic `PCI\VEN_8086` |
| Intel Trusted Execution Technology Driver | `Intel Trusted Execution Technology Driver` | Preserve working `ACPI\INTC1025` family match |
| Intel PPM Provision Driver | `Intel PPM Provision Driver` | `ipfppm_sw.inf` and its specific software component identity |
| Intel Game Assist Driver | `Intel Game Assist Driver` | Preserve current version lookup, do not downgrade installed newer build |
| IntelliGo APO Driver | `IntelliGo APO Driver` | Extract intelliGo APO `SWC\VEN_IGO` child component IDs and compare relevant group |
| Intel Smart Sound Technology driver | `Intel Smart Sound Technology driver` | Extract Intel SST/Audio/INTELAUDIO INF components; select a documented *representative version domain*, not arbitrary max |
| Realtek OED Driver | `Realtek OED Driver` | `IntcOED_OemLibPath.inf` has `DriverVer=20.43.274.009` and exact INTELAUDIO DSP IDs, including `SUBSYS_15101462`; do not conflate with main SST driver |
| Realtek Audio Driver | `Realtek Audio Driver` | `RtkSdcaXuMSI.inf` `DriverVer=10.0.378.1`, Class=Extension; compare matching Realtek SDCA XU extension/driver, **not** Realtek Audio Control AppX version or another codec driver's version |
| DTS Driver | `DTS Driver` | **EXACT RULE IN SECTION 3; official MSI page latest `1.16.4.0`** |
| Goodix FingerPrinter Driver | `Goodix FingerPrinter Driver` | `GoodixMocUsb.inf`: `Class=Biometric`, `DriverVer=3.12144.0.100`, USB `VID_27C6&PID_6090`; preserve installed newer `3.12144.0.160` |
| Realtek Card Reader Driver | `Realtek Card Reader Driver` | Map Realtek RTS card reader model (observed local `PCI\VEN_10EC&DEV_5264` / MTD), installed `10.0.26200.21389` vs MSI `10.0.26100.21378` -> UpToDate |

**Do not pretend IDs not yet extracted are known:** Local Codex must read each unpacked INF/model section and commit a short **static CG3EM mapping table**. A mapping that cannot be supported by source INF + observed Windows inventory must remain `Unknown` and be explained in the PR summary; do not create a placebo match to pass the matrix.

## 3. Definitive DTS Driver handling — MSI page version is authoritative

**Confirmed by user from MSI product support page:**
- Row `DTS Driver`
- **Published/latest version: `1.16.4.0`**
- Date: **2026-06-04**
- Package size: **6.23 MB**

The *current MSI catalog API* continues to supply Latest dynamically; **do not hardcode 1.16.4.0 into runtime code**. Treat those user values as verified evidence for expected mapping/tests.

**Direct inspection of the already extracted EX package (not ZIP):**

| INF / subpackage | INF DriverVer | Device/component ID | Role in DTS Driver mapping |
|---|---|---|---|
| `DTS_UWP_APO4x_Core_MSFT_1_16_4_0_25H2/dtsapo4x64.inf` | `1.16.4.0` | `SWC\VEN_DTSI&AID_DTSI2` | **Preferred representative DTS APO core** |
| `DTS_UWP_APO4x_Controller_MSFT_1_16_4_0_25H2/dtscontrollerx64.inf` | `1.16.4.0` | `SWC\VEN_DTSI&HID_DTSIAPO4xCONTROLLER` | Related DTS versioned component |
| `DTS_UWP_APO4x_RPCService_MSFT_1_16_4_0_25H2/dtsapo4xservice.inf` | `1.16.4.0` | `SWC\VEN_DTSI&HID_DTSIAPO4xSERVICE` | Related DTS versioned component |
| `DAP_UWP_APO4x_HSA_MSFT_1_10_7_0_25H2/dtsaudioprocessinghsa.inf` | `1.10.7.0` | `SWC\VEN_DTSI&HID_DTSIDAP` | **Exclude from representative DTS Driver version**: separate HSA/DAP companion |
| MSI extension files in this same package | May differ | Audio OEM extension-specific | Not the DTS APO core `Latest` used by this row; do not pick merely because numerically newer |

**Implementation:**
- Match the **known DTS APO core identity**, using stable `SWC\VEN_DTSI&AID_DTSI2` (actual installed PnP DeviceId, HardwareIds and CompatibleIds), appropriate DTS provider and SoftwareComponent/AudioProcessingObject class; allow equivalent, verified core subgroup if Windows enumerates it differently.
- If the core entry is absent but an unambiguous supported corresponding controller/service group is present, fall back to its **same version domain**; when core vs related components have conflicting versions that cannot be resolved, `Unknown`, not arbitrary pick.
- A versioned group may contain multiple PnP instances: don't require exactly one or a non-existent `&DEV_XXXX` suffix. Existing `HasObservedAudioCatalogHardwareIdentity()` regex that only admits `SWC\VEN_DTSI&DEV_...` is **not the right package identity**.
- A separate `DTS Audio Processing` UWP package version or `1.10.7.0` HSA component must **never overwrite** the MSI DTS Driver Installed version.
- Expected on supplied CG3EM capture: **Installed `1.16.4.0` / Latest `1.16.4.0` => UpToDate**. Do **not** select local candidates solely by equality with Latest. Identify DTS core family first, then compare its installed version with MSI's published version.

## 4. Minimal runtime implementation policy

### 4.1 Explicit model key and static rule selection

- The Frontend request already determines `hardware.Model` before Compose. Pass the resolved supported model to the composer via the **smallest internal signature change** (optional trailing argument or equivalent), rather than guessing CG3EM from a human-readable catalog title.
- Keep `MsiUpdateCatalogComposer` as the sole owner. Add a **compact CG3EM driver mapping table or bounded switch** keyed by exact normalized **MSI Driver catalog name**. Each row declares its stable Windows ID/class/provider or representative-component predicate(s). The source code must be explicit and reviewable; **no generic scoring model, fuzzy title engine, additional resolver interface, registry database, ML classifier or auto-discovery**.
- For `msi.claw.a2vm.7` / `msi.claw.a2vm.8`, do not apply CG3EM rules or assert they are the same board. Their current code path remains unchanged pending their own evidence.
- Preserve existing strongly matched `DeviceId` / `InfName` results; for CG3EM rows whose MSI API fields are incomplete or vendor-only, use the **static EX mapping** instead of immediately returning `NoUsableIdentity`. Avoid applying a broad `PCI\VEN_8086` or `SWC\VEN_DTSI` to every device of that vendor.
- INF filenames in the unpacked MSI reference are for **design-time identification**. Windows `Win32_PnPSignedDriver.InfName` commonly becomes `oem###.inf`. Match installed **stable IDs + provider/class** already captured by Windows, **never** expect `PantherLakeSystem.inf == oem598.inf` by bare string equality.
- A defined static mapping is a **key**, not a hardcoded version; each manual check reads the currently installed `DriverVersion`.

### 4.2 Grouped driver-package version and ordinary comparison

- **One product may install several related PnP devices.** After selecting only the mapped representative driver family/package, treat repeated device instances with the same version as one installed driver version; don't reject merely due to `CandidateCount>1` or `Confidence=Heuristic`.
- Exclude accessory components known to use separate version domains (e.g. DTS HSA, ISS driver vs package, Realtek extension vs codec).
- If multiple **relevant** installed component versions disagree and the mapping has no dependable representative, report `Unknown`, not first/maximum/local version coincidentally equal to Latest. `Installed (estimated)` is allowed when a coherent identified component/version exists but source-domain equivalence remains uncertain.
- For comparable **same-product** dotted numeric driver versions, compare the installed version with MSI catalog `DriverVersion` or its published `Version`/`PackageVersion` (as applicable to this defined row): installed < MSI → `UpdateAvailable`; installed == or > MSI → `UpToDate`. Installed-newer must never be called an update.
- When `Version` and `PackageVersion` are **different**, verify which corresponds to the representative INF before comparing; keep `Unknown` if it cannot be mapped without guessing. Do not use a local version equal to the MSI value as evidence for identity.
- This is display-only, not permission to launch the official MSI installer. Keep all Update buttons disabled.
- Simplify the redundant `ObservedSingleDeviceDriverNames` / `Exact` / one-device / DTS `&DEV_` / typed-`DriverVersion` veto paths that prevent **already mapped CG3EM drivers** from being compared. Do not alter other models' existing matching semantics.

### 4.3 No new collection or UI redesign

Continue using `MsiUpdateInstalledInventory`'s existing `PnpDevices` (provider, class, DeviceId, HardwareIds, CompatibleIds, friendly/name, DriverVersion, InfName). Do not introduce runtime downloads, static JSON loaded from disk, new SetupAPI/DriverStore scans, persistent DB or new frontend IPC. Existing manual request, diagnostic JSON, Korean/English labels, left metadata, centered right status and disabled Update button remain unchanged.

## 5. Acceptance and regression tests (synthetic, no hardware)

Tests **must drive the real production composer** and exercise supported `msi.claw.cg3em` with EX INF-derived static identity rules:

1. For all **19 EX catalog names**, prove explicit mapping exists or explicitly document truly unresolved package evidence; do **not** let `NoUsableIdentity` remain merely because MSI catalog has an empty/partial ID while the supported EX mapping exists.
2. Intel Chipset (multiple Panther Lake IDs and same version `10.1.56.28`), HID Event Filter (`2.2.2.17`), Serial IO (`30.100.2534.18`), Platform Monitoring (`7.3.11.3`), ME (`2603.9.4.0`), Smart Sound (`20.43.12741.0`), Realtek Card Reader (installed `10.0.26200.21389` > MSI `10.0.26100.21378`) resolve sensible installed versions and status using **device/package keys**, not coincidental version equality.
3. DTS: core installed `1.16.4.0` and MSI page/API `1.16.4.0` => UpToDate, even alongside DAP `1.10.7.0`. If core becomes `1.16.3.0` with same identity and MSI Latest remains `1.16.4.0`, => UpdateAvailable. If core identity not found, no fake UpToDate based on DAP. Test different candidate counts and IDs without `DEV_`.
4. IntelliGo: multiple recognized APO software components at `100.1.4.2709` => UpToDate; unrelated SWC/provider must not alter it.
5. Existing Goodix `3.12144.0.160` > MSI `3.12144.0.100` => UpToDate; Game Assist `2.0.1801.30` > MSI `1.9.602.30` => UpToDate; DTT/Trusted/PPM equals => UpToDate.
6. Explicitly reject broad Intel/Realtek/DTS-only vendor identity, unrelated Device/Class, duplicate version coincidences and multi-package conflicting versions. ISS `5.8.58.0` vs `5.8.61.0` package must not be miscompared. Realtek Audio Control AppX cannot stand in for Realtek Audio Driver.
7. **A2VM 7/8 isolation:** CG3EM-only rule must not run on other hardware; their BIOS/Firmware/Intel DSA and existing MSI matching behavior must not change.
8. Source selection, suppression, BIOS hexadecimal, owned PID1902 firmware, UWP sentinel, diagnostics privacy/retention, existing UI and disabled Update button are unchanged.
9. Run solution build and Runtime/UI tests. Actual device tests belong to the owner **after merge**, never a PR blocker.

**Expected PR organization:** One PR, optional internally separated commits (a) CG3EM package mapping table, (b) comparison simplification and DTS separation, (c) regression tests. Source should be **simpler** than the current layer of successive one-off gates; one static mapping, one installed-version owner, one verdict policy. No runtime ZIP handling and **no installation support**.

## 6. Additional background, explicitly not this PR

The same technique may later be applied to A2VM 7/8 using their **own extracted MSI model packages**. Do not infer their hardware IDs from CG3EM Panther Lake files. Non-Driver MSI Center M utility identity/Registry mapping may be handled separately; do not add it while focusing on the user's supplied EX Driver package evidence.
