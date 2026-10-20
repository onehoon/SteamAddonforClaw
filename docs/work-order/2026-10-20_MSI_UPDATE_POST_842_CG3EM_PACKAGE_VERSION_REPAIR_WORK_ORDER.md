# Work Order — Post-PR #842: CG3EM MSI Update Installed-Package Version Repair

**Date:** 2026-10-20  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** **ONE pull request, FOUR logically separate commits** on fresh `main`.  
**Executor:** local Codex writes code and automated tests.  
**Hardware validation:** performed by the product owner **after merge**; NEVER a Codex implementation requirement or PR-review/merge blocker.  
**Product:** standalone Full PID1902 MSI Claw app. **No CTW integration.**

## 1. Goal and change boundary

Correct every outstanding **evidence-backed** MSI Update installed-version problem in the 2026-10-20 CG3EM `0.1.407.0` diagnostic captures, in ONE PR:

1. MSI Center M: installed `MSI Center M SDK` is missed by the exact/regex name fallback.
2. Windows Studio Effects: `Cg3emMappedNoMatchingPresentDevice` despite an OEM package identity for an AudioProcessingObject-class INF.
3. Realtek OED Driver: same outcome for an Extension-class INF.
4. Realtek Audio Driver: same outcome for another Extension-class INF.
5. Integrated Sensor Solution (ISS): the app pairs the installed main ISS component version `5.8.58.0` with the offered **HECI package** version `5.8.61.0`, although the correct package domain is distinguishable.

Keep current working results and exact Intel DSA replacement unchanged. Do NOT solve these by generic fuzzy title matching, choosing the highest installed version, treating all Intel/Realtek devices as one family, or assuming that a package staged in DriverStore is the currently bound active device.

The desired implementation is deliberately small: **existing composer is the sole matching authority; existing manual inventory gets one narrow, read-only DriverStore package source; only the four specifically evidenced CG3EM Driver rows consume that source; Center M uses a verified literal name alias.** No new application-wide owner, service, helper EXE, persistent DB/cache, scanning daemon, or generalized driver-updater framework.

## 2. Mandatory sources / priority

Read these before edits:

- `docs/Full 1902 Implementation/README.md`, `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`, and `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`. Their controller/lifecycle and runtime-privilege boundaries are unchanged.
- `docs/work-order/2026-10-19_MSI_UPDATE_POST_840_INSTALLED_VERSION_MATCHING_AND_COMPARISON_FIX_WORK_ORDER.md` (CG3EM fixed mapping, representative-component rules).
- PR [#841](https://github.com/onehoon/SteamAddonforClaw/pull/841), PR [#842](https://github.com/onehoon/SteamAddonforClaw/pull/842) and their merged source/tests; this work order **supersedes** their insufficient matching rule for the five named cases.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`: `MsiUpdateInstalledInventory` / `CaptureAsync` / `MsiUpdateCatalogComposer` / `ResolveCg3emMappedDriver` / `ResolveCg3emComparisonVersion` / `ResolveMsiCenterMRegistryCandidate` / `MapMsiEntry`.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs`: preserved `OemPackageIdentity`, `PackageVersion`, `Version` and diagnostics.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateDiagnosticSnapshot.cs` and `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`.
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`, `MsiLiveUpdateCatalogTests.cs`, `MsiUpdateDiagnosticSnapshotTests.cs`.
- **Primary runtime evidence**: `GoogleDrive/Addon/Log/1020/msi-update-20261020-123353.232-1cd020ce.json` and `msi-update-20261020-123421.881-4246cbc8.json`. These files contain equivalent data, with 81 local present PnP rows, one projected uninstall software row, no inventory warnings, 31 MSI OEM catalog rows, and four resolved Intel DSA mappings.
- **OEM/static evidence**: `GoogleDrive/Addon/MSI/MSI_CENTER_M_LIVE_UPDATE_THREE_MODEL_FINAL_REA_REPORT_2026-10-20.md` and `MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`. Already-extracted reference drivers: `C:\GoogleDrive\Addon\MSI Update\Driver\EX` (the earlier #841 order gives the precise package folder names). The extracted INF files are optional **read-only design-time** confirmation if present on the local Codex host; never require extraction, installation or execution of downloaded payloads.
- Microsoft documented read-only driver-store enumeration: https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsdriver and https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/take-inventory-of-an-image-or-component-using-dism . `Get-WindowsDriver -Online -All` enumerates original INF name, published `oem###.inf`, provider, class and INF `DriverVer` version. This is **package inventory**, not proof of active PnP binding.

Preserve the real product boundary: **1 admin Windows user / 1 interactive session / supported CG3EM and A2VM 7/8**. No speculative multi-session, RDP or Fast User Switching support.

## 3. Captured evidence and exact acceptance matrix

### 3.1 MSI Center M (CG3EM)

MSI Utility catalog: `Name="MSI Center M"`, `Version=PackageVersion=3.0.2608.3101`, OEM GUID `0da6126a-8430-4367-b282-aa285afc2f10`.

Real local uninstall row in both 2026-10-20 snapshots:

| Local uninstall field | Observed |
|---|---|
| `DisplayName` | **`MSI Center M SDK`** |
| `Publisher` | `MSI` |
| `DisplayVersion` | `3.0.2608.3101` |
| Source | `HKLM32` |
| ProductCode / UpgradeCode | null / null |
| Current match outcome | `CenterMNoKnownDisplayNameMatch` |

**Result required:** stable exact known-name + verified publisher + unique non-conflicting registry candidate resolves `3.0.2608.3101`; valid catalog/package-domain comparison returns `UpToDate`. Do not invent GUID linkage, use versions to establish identity, or match `MSI Center M SDK Updater` and other lookalikes. Existing GUID-first branch remains authoritative.

### 3.2 CG3EM Driver rows

| OEM catalog row | OEM `Version` | OEM `PackageVersion` | Preserved `OemPackageIdentity` | Current local result |
|---|---|---|---|---|
| Integrated Sensor Solution | `5.8.58.0` | **`5.8.61.0`** | `dism_ishheci.inf_System_Intel` | `5.8.58.0` from ISS device, misleading cross-domain display |
| Windows Studio Effects | `3.0.8.0` | `3.0.8.0` | `dism_mep,audio,component.inf_AudioProcessingObject_Microsoft` | Unknown / no present PnP match |
| Realtek OED Driver | `20.43.274.9` | `20.43.274.9` | `dism_intcoed,oemlibpath.inf_Extension_Realtek` | Unknown / no present PnP match |
| Realtek Audio Driver | `10.0.378.1` | `10.0.378.1` | `dism_rtksdcaxumsi.inf_Extension_Realtek` | Unknown / no present PnP match |

The original MSI EX extraction confirms `IntcOED_OemLibPath.inf` is an **Extension** package with `DriverVer=20.43.274.009`; numeric equality with `20.43.274.9` must work. `RtkSdcaXuMSI.inf` is also **Extension**, `10.0.378.1`. WSE package includes related Microsoft MEP/audio INF components at `3.0.8.0`; its *specific* original-INF name(s), provider and class must be verified against the extracted INF / REA before adding permitted alternatives. **Do not naively split the comma-containing OEM identity `mep,audio,component.inf` into unrelated tokens or assume that punctuation is a fuzzy wildcard.**

The two supplied local snapshots do **not** contain a full DriverStore inventory. They prove failure of the current *present PnP* mapper but do not prove installed DriverStore matches. New matching must therefore be conditional on actual runtime evidence, not hardcoded success.

### 3.3 ISS — exact distinct component evidence (important)

| Local present device | Local version | Published INF | Observed identity |
|---|---|---|---|
| Intel Integrated Sensor Solution | `5.8.58.0` | `oem613.inf` | `PCI\VEN_8086&DEV_E445` |
| Intel ISS HID Bus | `5.8.58.0` | `oem679.inf` | ISS HID bus |
| Intel ISS HID Device | `5.8.58.0` | `oem680.inf` | ISS HID |
| **Intel HECI Protocol Device** | **`5.8.61.0`** | **`oem677.inf`** | System / Intel, local abbreviated hardware token |

These are different *components* in one OEM distribution. The fact that HECI's version equals the catalog `PackageVersion` strongly suggests it is the intended package version domain, but version equality alone is **not** proof of identity. Verify `oem677.inf` maps to original `ishheci.inf` using the new installed DriverStore evidence before accepting that HECI entry. Do not infer the original INF name from `oem677.inf`'s ordinal.

**ISS outcome contract:**
- If the exact OEM package original INF + provider + class is found unambiguously, use **its installed package version** (expected in this snapshot: `5.8.61.0`) against OEM `PackageVersion` (`5.8.61.0`). Compare within that same domain.
- Keep existing ISS-device `5.8.58.0` evidence available for diagnostics, but NEVER treat it as the installed version of the `5.8.61.0` offered **HECI package**.
- If the package evidence cannot be confirmed or versions conflict, show installed **Unknown** with a clear domain/identity reason, not `5.8.58.0` vs `5.8.61.0` and not a false `UpdateAvailable`. The current `comparable: false` is a safety measure; only opt into package-domain comparison for proven `ishheci.inf` matches, preserving conservative behavior otherwise.
- No arbitrary numeric-max selection. Do not change the independent ISS device's active version, driver or controller lifecycle.

## 4. One-PR / four-commit implementation order

### Commit 1 — `fix(msi-update): recognize MSI Center M SDK registry identity`

Update `MsiUpdateCatalogComposer.IsKnownMsiCenterMDisplayName` with the **observed literal alias** `MSI Center M SDK`. Keep `MSI Center M` literal support and any already-verified version-suffixed title support, the verified `MSI`/`MSI Corporation` publisher gate, one-candidate requirement, conflicting ProductCode/UpgradeCode/registry-key protection, and existing catalog package equality guard.

Use case-insensitive exact normalized strings (trim), no generic `Contains("MSI Center M")`, substring, fuzzy matching, or title-prefix wildcard. `MSI Center M SDK Updater` / `Other MSI Center M SDK` / wrong publisher / multiple verified candidates / conflicting explicit GUID MUST remain Unknown.

Add focused composer tests with real captured `MSI Center M SDK` + HKLM32 + null product/upgrade code and identical `3.0.2608.3101`, plus older/newer numeric versions and all negative cases. Ensure actual comparison is `UpToDate` for matched equal catalog/package versions, not merely that `InstalledVersion` is populated.

### Commit 2 — `feat(msi-update): capture bounded read-only CG3EM DriverStore package evidence`

**One small addition to the existing `MsiUpdateInstalledInventory` and its owner; no new manager or worker.**

- Extend the existing installed-inventory record with optional, **backwards-compatible** package rows (existing positional test/production constructions must continue to compile), e.g. `MsiUpdateInstalledDriverPackage(PublishedInfName, OriginalInfName, Provider, Class, Version)` plus a default-empty init-only property on the current record.
- Capture the packages **only on a CG3EM manual MSI Update request**. `RequestMsiLiveUpdateCatalogAsync` already has `hardware.Model`; thread this key into the existing capture method rather than repeating detection. A2VM 7/8 take their existing path unchanged.
- Prefer the supported Windows PowerShell DISM cmdlet `Get-WindowsDriver -Online -All` with a **machine-readable projection** of published INF, original INF, class, provider and version. Parse the object's actual property names verified against Windows DISM output/official documentation. Use an already-available process-launch helper when suitable, or one local bounded read-only invocation. Avoid locale-dependent plain-text `dism.exe`/`pnputil` table parsing. Treat JSON single-object/array output, empty output and missing fields correctly.
- Use no network, no installer, no INF registration, no `pnputil -add-driver` / install / delete / enum-side effects, no MSI Center M executable and no mutable OEM `DataSet` scanner. Process should run under existing elevated Runtime token. No new elevation, service, IPC, long-running process, polling, persistent storage or cache.
- Query once per user-initiated check, not once per candidate, and **concurrently with the already existing independent MSI and Intel DSA queries**, preserving current composition sequence. Bound duration, output size and rows; honor the existing caller cancellation, dispose process resources, avoid leaving a hung child. A read failure or unsupported/blocked PowerShell/DISM environment yields `DriverStoreUnavailable` or a similarly specific inventory warning, **not** an overall failed MSI catalog or blocked frontend. No operation silently substitutes a different package for missing evidence. Protect normal release/shutdown cancellation.
- Normalize original INF by **basename, case-insensitive**, normalized actual INF filename, NOT `oem###.inf` numeric identity. Limit to a **small allowlist of verified INF names/families for these four CG3EM catalog rows**; do not retain arbitrary paths, command lines or device serials. Preserve publisher/class/version/read outcome. Do not hardcode installed/local numeric versions.
- A2VM, BIOS, controller firmware, Intel DSA, other CG3EM driver rows and all Core Full1902 device ownership must not depend on this new capture.

For diagnostics, extend `MsiUpdateDiagnosticSnapshotBuilder` minimally with the selected relevant package evidence (original INF name, published name, provider, class, version; plus capture count/warning and why a row did/not match). The existing local file-only diagnostic, size cap, retention, truncation and safe value validation remain. Do not create network telemetry or exhaustively dump the Windows DriverStore.

**Unit tests (no actual DISM invocation):** parse/mock projected output; normal list, single object, missing/null/malformed fields, mixed casing, path-to-basename normalization, excessive output, cancellation/error, duplicate packages and non-Windows fallback. Assert the constructed inventory supports all existing callers/tests.

### Commit 3 — `fix(msi-update): resolve CG3EM WSE and Realtek extension package versions`

For exactly these three **CG3EM** catalog names, use `entry.OemPackageIdentity` that the parser already retains. Require the expected nonempty shape/known identity and an exact, narrow **OEM OriginalInfName + class + provider** match against the DriverStore packages captured in Commit 2. No generic DISM heuristic or arbitrary `PackageVersion` equality matching.

- Windows Studio Effects: verified Microsoft AudioProcessingObject MEP/audio component original INF(s) corresponding to `3.0.8.0`. Reference actual extracted INF header(s), not an unproved comma split. If several components together define the package, require them to be coherent or retain Unknown.
- Realtek OED: `IntcOED_OemLibPath.inf`, class `Extension`, Realtek provider, catalog package `20.43.274.9`; normalize dotted numeric components so INF `20.43.274.009` compares equal.
- Realtek Audio: `RtkSdcaXuMSI.inf`, class `Extension`, Realtek provider, package `10.0.378.1`. Do not replace this with the present codec/media driver, `Realtek Audio Control` AppX, other Realtek service or manufacturer-generic component.
- Reuse `MsiUpdateCatalogComposer` and its `Cg3em`-only branch; keep existing static PnP matching for **other** drivers. For the three rows, absent package → Unknown (with precise `NoMatchingInstalledDriverPackage` or equivalent), unavailable capture → Unknown, duplicate inconsistent versions → Unknown, invalid/incoherent exact package identity → Unknown.
- When a unique verified installed **package** version exists, report it as **package** evidence and compare ONLY against MSI's numeric `PackageVersion` in the same domain. Existing `LatestVersion` remains supplied dynamically by MSI. `UpToDate`/`UpdateAvailable` should describe the package installed in DriverStore (not assert that an extension INF is actively attached to a device). Do not imply active-device binding in diagnostics, logs or UI. No comparing the extension against `Intel SST` or other audio device's version.
- The currently supported UI status/installed/latest DTOs may be reused; do not add an elaborate new UI or feature-wide contract just to express evidence origin. Prefer clear match outcomes (`Cg3emInstalledInfPackageMatch` etc.) and conservative Unknown cases.

Tests per row: missing package, one verified same-version package, older/newer package, wrong OriginalInfName, wrong provider/class, multiple conflicting versions, case-insensitive INF names and numeric dotted equivalence (including `274.009` ↔ `274.9`). Negative tests MUST prove that matching a plain Intel/Realtek provider or matching only `3.0.8.0` is insufficient.

### Commit 4 — `fix(msi-update): align ISS package and installed HECI version domains`

Special-case the CG3EM catalog row `Integrated Sensor Solution` inside the existing CG3EM mapping/comparison authority, using the exact preserved OEM `dism_ishheci.inf_System_Intel` identity to locate a verified installed `ishheci.inf` System / Intel package. Read its **installed package Version**; do NOT use the active `PCI\VEN_8086&DEV_E445` ISS main device's `5.8.58.0` against the package `5.8.61.0`.

- Keep the existing main ISS/HID-family mapping as independent device evidence, not as the installed *HECI package* version. The static row may retain `comparable:false` for PnP-only match; the new explicit, proven INF-package resolver provides the only comparable package-domain result.
- If the HECI INF is installed at `5.8.61.0` and MSI offers `5.8.61.0`, `Installed=5.8.61.0`, `Latest=5.8.61.0`, `UpToDate`, with a distinct package-match outcome.
- If the exact original INF/class/provider isn't present or read is unavailable, `Installed=Unknown` / comparison `Unknown`. If both ISS main and HECI components are present but have different versions, do **not** report that as version conflict (different domains). If **two matching HECI package candidates** have conflicting versions, stay Unknown rather than picking a higher one.
- Validate the comparison gate: it must check the match source and numeric package-version domain (not simply flip all ISS mappings to `comparable:true`). Preserve the offered row's `Version=5.8.58.0` and `PackageVersion=5.8.61.0` independently in the original diagnostic; do not rewrite MSI source data or label the former erroneous.
- If the original INF-to-`oem677.inf` linkage cannot be established at runtime, stay Unknown and expose diagnostic evidence; the equality of `5.8.61.0` numbers alone does not establish identity.

Tests with synthetic captured CG3EM inventory (no actual hardware): `5.8.58.0` ISS PnP + `5.8.61.0` HECI DriverStore ⇒ correct UpToDate; active ISS-only ⇒ Unknown rather than a mismatched installed version; wrong INF/vendor/class ⇒ Unknown; duplicate inconsistent HECI packages ⇒ Unknown; HECI older/newer ⇒ numeric package status; A2VM unaffected.

## 5. Cross-cutting safeguards, regression tests and verification

**Safety/regressions to preserve:**

- This is **read-only MSI Update information retrieval**. No Update button activation; installer/download/CRC/routing/enumeration mutations remain out of scope. Existing disabled Update placeholder stays disabled.
- Stay within supported standalone Full1902; do not modify the PID1901/1902 mode, Steam vs Xbox360 virtual presentation, HidHide or VIIPER ownership, Center M enable/disable/restart, WING, TDP, sleep/hibernate/resume, startup or teardown paths.
- Keep BIOS `E1T91IMS.10D`, controller firmware `4.19`, existing confirmed CG3EM package matches, already corrected IPF `2.3.20303.5058`, and Intel DSA four-family precedence/results.
- Preserve **UWP exclusion** (CG3EM three UWP items plus prior two existing utility exclusions), 31 raw catalog rows, truthful source counts, and no AppX installed matching.
- A2VM 7/8 should not run CG3EM DriverStore matching or reinterpret their separate MSI catalog rows. No unsupported-device paths.
- Fail conservative on missing evidence, cancellation, process failure, duplicate disagreeing packages or invalid version strings. No false `UpToDate` due to catalog-version equality, no impossible update warning due to cross-package domains.
- Do not add defensive locks/epochs/authority wrappers solely for theoretical races; reuse existing ownership, async cancellation and one-shot manual query.

**Automated review gate (Codex):**

1. Unit tests for each of the four commits, including negative evidence and regression matrices.
2. Run existing solution build/tests relevant to MSI Update and frontend on supported CI target(s). No machine-installed MSI apps, GPU/PnP hardware, PowerShell on CI or administrator/elevated test session may be assumed.
3. Keep the new Windows package-reader parser testable from supplied structured strings/objects; real `Get-WindowsDriver` execution is **not** required in CI tests.
4. Inspect file diffs for privacy scope, no installation paths, no new persistent cache, and no expanded controller authority. Document any **evidence-limited Unknown** cases explicitly; code/tests should be mergeable without connected hardware.

**NOT a PR blocker:** physical MSI Claw reproduction, post-build MSI live request, on-device Windows DriverStore enumeration, device installation/reinstallation or other user-performed hardware verification. Product owner will test **after merge** and provide a new diagnostic snapshot if further work is needed.

## 6. Acceptance criteria and deliverable

A single PR with **four separately reviewable commits in the order listed above**; each commit includes its own directly related automated tests. The PR description must include a concise table of five target cases, intended evidence/source domain, before vs code-level expected result, preserved regression checks and any remaining Unknowns. Do not claim on-device success without evidence.

**Expected outcomes with complete, matching installed DriverStore evidence:**

| CG3EM row | Correct installed-version basis | Example expected state from observed package versions |
|---|---|---|
| MSI Center M | Exact registry `MSI Center M SDK` / MSI / `DisplayVersion` | `3.0.2608.3101` → `UpToDate` |
| ISS | DriverStore original `ishheci.inf` / System / Intel / `Version` | `5.8.61.0` → `UpToDate` |
| Windows Studio Effects | Verified original MEP INF(s) / AudioProcessingObject / Microsoft | `3.0.8.0` → `UpToDate` when actually present |
| Realtek OED | DriverStore original `IntcOED_OemLibPath.inf` / Extension / Realtek | `20.43.274.009` → `UpToDate` when actually present |
| Realtek Audio | DriverStore original `RtkSdcaXuMSI.inf` / Extension / Realtek | `10.0.378.1` → `UpToDate` when actually present |

**Do not bake these observed versions into production matching.** On a machine missing the packages, retain honest Unknown. There is no requirement that all five display UpToDate on a CI machine or before post-merge hardware verification.

### Work order authority

This order supplements/supersedes the portion of the October 19/20 MSI Update work orders and PR #842 that chose an unproven name alias or compared different package/version domains. It does NOT supersede Full1902 architecture authority or undo successful prior MSI Update features. Implement against fresh `main`, not a stale PR branch.
