# Work Order — Post-PR #839 MSI Update: Correct Proven Version Verdicts and AppX Version Capture

**Date:** 2026-10-19  
**Product:** SteamAddonforClaw standalone Full PID1902 (no CTW integration)  
**Implementation owner:** Local Codex  
**Hardware validation owner:** Product owner **after merge**, not a PR blocker  
**Target:** One focused PR, reviewable internal commits; read-only MSI Update checks only

## 1. Goal and user decision

Correct the over-conservative `Unknown` verdicts identified on the real CG3EM / `v0.1.404.0` manual MSI Update captures without introducing false "Update available" conclusions.

The user specifically confirmed the intended interpretation:

- Controller **Firmware 4.19 → 4.19** is **Up to date**.
- **Goodix FingerPrinter** installed `3.12144.0.160` versus MSI `3.12144.0.100`: installed is newer; **Up to date**, not an available downgrade.
- **Intel Game Assist** installed `2.0.1801.30` versus MSI `1.9.602.30`: installed is newer; **Up to date**.
- **Intel Dynamic Tuning** `9.1.10009.1745` vs `9.1.10009.1745`: **Up to date**.
- **Intel Trusted Execution Technology** `1.21.18.0` vs `1.21.18.0`: **Up to date**.
- **DTS Driver** and **IntelliGo APO Driver** both find coherent installed versions equal to MSI. Treat these as supported *candidate* **Up to date** verdicts after verifying the bounded multiple-component match is genuinely from the same product family (Section 4.3), not as failed local version scans.
- **Intel NPU — DSA** installed PnP driver `32.0.100.5540` and selected DSA *package* `32.0.100.5540`: implement the narrowly admitted equality outcome in Section 4.4; do **not** accidentally assume package and driver versions are an ordered common domain.

The current `ComparisonStatus` contract (`Unknown`, `UpdateAvailable`, `UpToDate`) already exists. **Do not add another status or change the page design.** This order updates/overrides the strict `Unknown`-only restrictions for these specifically evidenced cases in:
`docs/work-order/2026-10-19_MSI_UPDATE_PR2_VERSION_COMPARISON_CENTERED_ACTION_UI_WORK_ORDER.md`.
All other PR2 safety conditions still apply.

## 2. Mandatory evidence / authority

Read these before editing:

1. `docs/Full 1902 Implementation/README.md` and its Full1902 authority chain. No controller routing, HidHide, VIIPER, WING, PID1901↔PID1902, physical owner, power lifecycle or elevation-policy changes.
2. `docs/work-order/2026-10-19_MSI_UPDATE_PR2_VERSION_COMPARISON_CENTERED_ACTION_UI_WORK_ORDER.md`, implemented by squash-merged **PR #839**, and current `main` sources/tests.
3. `docs/work-order/2026-10-19_MSI_UPDATE_MANUAL_CHECK_DIAGNOSTIC_SNAPSHOT_WORK_ORDER.md`, implemented by merged PR #838.
4. `docs/work-order/2026-10-18_MSI_UPDATE_POST_836_MATCHING_AND_UTILITY_EXCLUSIONS_WORK_ORDER.md`: do not weaken strict HWID matching, existing DSA precedence or Utility exclusions.
5. MSI REA report, particularly Section 11.1–11.10: `C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`. Its OEM `DataSet` matching is **not** an independent safe API; keep the current Addon inventory/matcher.
6. **Actual device observations:** `C:\GoogleDrive\Addon\Log\1019-MSIUPDATE\`
   - `msi-update-20261019-110607.418-61ee7660.json`
   - `msi-update-20261019-110632.334-d4752888.json`
   - Runtime log `SteamInputAddonforClaw-2026-10-19-200535.746-P5320-L8c755779fd.log`.
   The two snapshots agree on MSI and local version data; query completion was successful for MSI + Intel DSA, with **zero** local inventory warnings.

The JSON contains *row evidence*, **not** serialized `ComparisonStatus`. Validate intended statuses through composer fixtures/tests, not by claiming the JSON itself stores them.

### CG3EM observed reference matrix

| Category/item | Installed (Addon match) | Latest (source) | Existing match | Required verdict |
|---|---|---|---|---|
| BIOS | `E1T91IMS.10D` | `E1T91IMS.10D` (MSI) | ExactBiosIdentity | UpToDate (no change) |
| Controller Firmware | `4.19` | `4.19` (MSI) | ExactControllerIdentity / Exact | **UpToDate** |
| Goodix FingerPrinter Driver | `3.12144.0.160` | `3.12144.0.100` (MSI PackageVersion and Version) | ExactHardwareId / Exact / 1 device | **UpToDate** |
| Intel Game Assist Driver | `2.0.1801.30` | `1.9.602.30` (MSI Version) | ExactHardwareId / Exact / 1 device | **UpToDate** |
| Intel Dynamic Tuning Driver | `9.1.10009.1745` | same (MSI Version) | ExactHardwareId / Exact / 1 device | **UpToDate** |
| Intel Trusted Execution Technology Driver | `1.21.18.0` | same (MSI PackageVersion and Version) | ExactHardwareId / Exact / 1 device | **UpToDate** |
| Intel PPM Provision Driver | `1.0.0.255` | same (MSI Version) | ExactHardwareId / Exact / 1 device | **UpToDate** |
| DTS Driver | `1.16.4.0` (estimated) | same (MSI Version) | ExactHardwareId / Heuristic / 2 devices / 1 version | **UpToDate if Section 4.3 product-family proof holds** |
| IntelliGo APO Driver | `100.1.4.2709` (estimated) | same (MSI PackageVersion and Version) | ExactHardwareId / Heuristic / 4 devices / 1 version | **UpToDate if Section 4.3 product-family proof holds** |
| Intel NPU — DSA | `32.0.100.5540` (PnP driver) | same (DSA Package) | exact DSA HWID / NPU | **UpToDate for proven equality only** |
| Intel Graphics/Wi-Fi/Bluetooth — DSA | same PnP/DSA driver versions | same | exact DSA HWID | UpToDate (unchanged) |

**Caveat:** Matching a `PackageVersion` against a PnP driver version is not generally valid. The matrix records what this real device exposes, not a general assertion that every MSI package version is the same as its installed driver version. Sections 4.2–4.4 strictly bound promotions.

### Explicit unresolved, unchanged

- 11+ MSI Driver rows with `NoUsableIdentity` (e.g. HID Event Filter, Chipset, Serial IO, IPF, Management Engine, Smart Sound) and CG3EM Realtek Audio `NoMatchingPresentDevice` **remain Unknown**; PnP rows with similar-looking version numbers do not establish package identity.
- MSI Center M Utility: local HKLM32 candidate has display version `3.0.2608.3101`, but its registry product identifier does not match the catalog GUID. Still `Unmatched`/Unknown. Center M being disabled does not imply it is uninstalled.
- Realtek Audio Control and DTS Audio Processing have catalog `Version=UWP`; this is **not** a latest numeric version, and cannot produce an update verdict.
- Do not misinterpret a newer *installed* version as "Update available". An installed version newer than the offered version yields `UpToDate` only when the domain is proven comparable; otherwise `Unknown`.

## 3. Minimal code change points

Work in current `main` at review time (line numbers may shift):

- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`
  - `MsiUpdateInstalledInventory.CaptureAppxPackages`: currently calls `package.Id.Version.ToString()` and stores the literal `Windows.ApplicationModel.PackageVersion`.
  - `MsiUpdateCatalogComposer.ResolveMsiComparisonStatus`: MSI drivers currently require `versionKind == Driver` and `entry.DriverVersion`; the CG3EM MSI rows never provide that field, despite valid catalog `Version`/`PackageVersion`.
  - `ResolveMsiDriverInstalledVersion`, `ResolveDriverCandidates`: preserve their exact/heuristic match provenance.
  - `MapIntelEntry`: existing NPU always `Unknown` due to Package version kind.
  - `ResolveMsiInstalledVersion` Utility branch currently requires literal catalog name equal to AppX package Id.Name/FamilyName, missing the two observed display-to-AppX identities.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: `CaptureOwnedControllerFirmwareVersionForMsiUpdateAsync` already captures owned, strong PID1902 control HID `VersionNumber` and formats `0x0419 → 4.19` under multiple owner checks. **Do not change it or re-read/re-enumerate/reconfigure the device**.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`: keep same manual request, concurrent MSI+DSA+inventory and best-effort diagnostic JSON creation; no new scan.
- Existing `MsiUpdatesPage.xaml.cs` right-side status + disabled Update button, one-line metadata and localizations are unchanged.

Do **not** add a generic status service, extra updater, additional WinRT/COM/OEM calls, per-model background worker or new IPC.

## 4. Required corrections

### 4.1 Controller Firmware: equality of same verified controller, not PnP firmware class

Currently `Firmware` always falls through to Unknown in `ResolveMsiComparisonStatus`.

Only admit the exact existing owned-control-HID path:

- `category == Firmware`, `IsControllerFirmware(entry)`.
- `match.Outcome == "ExactControllerIdentity"`, `Confidence == Exact`, `CandidateCount == 1`, `VersionCount == 1`, and both versions nonempty.
- Catalog version token must use the same explicitly supported dotted controller format as `CaptureOwnedControllerFirmwareVersionForMsiUpdateAsync`, e.g. `4.19`. Refuse sentinel, arbitrary string, hex-only `REV_xxxx`, malformed/ambiguous values.
- **Equal meaningful strings/numeric components → `UpToDate`.** For now differing firmware versions remain **Unknown**, not automatically UpdateAvailable/downgrade, until the firmware version ordering and model-specific package semantics are evidenced. This bounds real-world safety while fixing the user's actual example.
- If no strong owned HID/read, Center M enabled, temporary device loss/PnP re-enumeration, or any capture failure, preserve existing Unknown. No writes or owner-state changes.

Example acceptance: `4.19` vs `4.19` → UpToDate; `4.18` vs `4.19` → Unknown (until ordering is verified); `null`/heuristic`0x0419` → Unknown.

### 4.2 MSI PnP Driver: compare evidenced MSI Version/PackageVersion for exact single-device families

Do **not** universally replace the existing rule with "all PackageVersion is a driver version".

For the currently **observed specific MSI driver families**, allow comparable `Version` or `PackageVersion` when ALL checks hold:

1. Catalog category is Driver and a stable, positively evidenced known family: **Goodix FingerPrinter Driver, Intel Game Assist Driver, Intel Dynamic Tuning Driver, Intel Trusted Execution Technology Driver, Intel PPM Provision Driver**. Verify exact catalog product name and actual hardware provenance, not arbitrary name prefix.
2. Existing Addon resolution is `ExactHardwareId` or `ExactInf` **with provable precise present-device identity**, `Confidence==Exact`, exactly one distinct matching PnP device and one installed version. Keep MSI known Intel DSA-family fallback block from PR2: exact-match exception must not promote a retained MSI Intel Graphics/Wi-Fi/Bluetooth/NPU catalog row when DSA fails.
3. `entry.DriverVersion` remains preferred when genuinely present and typed Driver. If it is null, the candidate offered numeric version may be sourced from `Version` or `PackageVersion`, **only for the evidenced family** and only when `Version` and `PackageVersion`, if both nonempty and numeric, are equivalent. Conflicting catalog field values fail closed.
4. Parse valid bounded numeric tuples *before* comparison; retain original `FrontendMsiLiveUpdateVersionKind` and displayed Latest/Installed values. A newer installed number must map to UpToDate (not downgrade).
5. If exact matching, field meaning, format or family certainty is absent, retain Unknown. For other broad chipset, PPKG, UWP, Realtek audio, multi-package families, do not use accidental matching version numbers to grant a verdict.

Expected examples:

```text
Goodix FingerPrinter  3.12144.0.160 / 3.12144.0.100 → UpToDate
Intel Game Assist     2.0.1801.30   / 1.9.602.30    → UpToDate
Intel Dynamic Tuning  9.1.10009.1745 / same           → UpToDate
Intel Trusted Exec    1.21.18.0     / same            → UpToDate
Intel PPM Provision   1.0.0.255     / same            → UpToDate
Same proven family, installed older than offered      → UpdateAvailable
Unrelated family, vague vendor-only ID, two versions  → Unknown
```

Implementation sketch (not a new public API):

```csharp
// Keep the existing exact-resolution admission conditions.
// Only for the explicitly proven MSI driver families:
string? offered = entry.DriverVersion;
if (string.IsNullOrWhiteSpace(offered))
{
    if (!IsSupportedObservedSingleDeviceDriver(entry, match))
        return FrontendMsiLiveUpdateComparisonStatus.Unknown;
    if (!string.IsNullOrWhiteSpace(entry.PackageVersion)
        && !string.IsNullOrWhiteSpace(entry.Version)
        && !string.Equals(entry.PackageVersion, entry.Version, StringComparison.OrdinalIgnoreCase))
        return FrontendMsiLiveUpdateComparisonStatus.Unknown;
    offered = entry.PackageVersion ?? entry.Version;
}
return CompareNumericVersions(match.Version, offered);
```

Prefer a short local allowlist and existing helpers/enum. Avoid building a new generalized vendor-version mapping registry.

### 4.3 DTS / IntelliGo: multi-component same-version equality only

The real Addon results are **`ExactHardwareId` + `Confidence=Heuristic`** because 2 DTS and 4 IntelliGo PnP components were selected, but **`VersionCount=1`** and the returned versions exactly equal the MSI displayed version.

For **DTS Driver** and **IntelliGo APO Driver** only:

1. Confirm their candidates are actually selected by the existing catalog's *usable* driver identity (not only a broad vendor/provider name), are limited to expected DTS or IntelliGo software/audio components, and share the same *numeric* `DriverVersion`. Reuse already collected `inventory.PnpDevices` and existing `IsMsiDeviceIdMatch`/component match evidence.
2. Require `Outcome=="ExactHardwareId"`, `Confidence==Heuristic`, `CandidateCount >= 2`, `VersionCount==1`. Recompute/verify that the admitted components share the same version and approved provider/component family; do not trust a common number *alone*.
3. Require equal valid numeric MSI-offered `Version` or non-conflicting `PackageVersion` with the resulting installed version. **Equality → UpToDate** only. **Inequality → Unknown** for this heuristic multi-component class (no unsupported UpdateAvailable / downgrade conclusion).
4. Preserve the installed-version display's **`(estimated)`** label and the existing heuristic confidence; a UI UpToDate verdict does **not** elevate the underlying identity to `Exact`.
5. If matching components are mixed/unrelated, version-divergent, absent, loosely vendor-matched, or evidence is insufficient to show correct family correlation, **leave Unknown and document the specific reason**. Do not force a numeric result to satisfy an artificial count.

Tests should use realistic two-component DTS and four-component IntelliGo cases plus one mixed-version, one unrelated-component and one broad/vendor-only negative case.

### 4.4 Intel NPU — DSA: narrowly allow exact equality, no cross-domain ordering

On CG3EM: exact DSA hardware match, `Family=Npu`, `VersionKind=Package`, DSA package `32.0.100.5540`, installed `Device.DriverVersion=32.0.100.5540`.

- Admit **UpToDate only if** NPU family, exact `:HardwareId` resolution, `VersionKind==Package`, both strings valid same-length dotted numeric versions, **and equal**.
- Preserve the UI's `Latest Intel package` wording, exact HWID matching, existing Intel package selection, and `VersionKind=Package` (do **not** relabel as Driver).
- **When unequal**, remain Unknown in both directions. Do not promote package-vs-driver numerical ordering into UpdateAvailable/UpToDate. No new Intel DSA API, component parsing, downloaded packages or local driver registry heuristics.
- GPU, Wi-Fi and Bluetooth comparisons remain their already verified Driver/Driver comparisons.

This equality-only concession is scoped to the **same explicitly resolved NPU hardware identity**; a mere version-string collision on an unrelated device must remain Unknown.

### 4.5 Fix the proven WinRT AppX version serialization defect

Current `CaptureAppxPackages` uses:

```csharp
SafeValue(package.Id.Version.ToString()) ?? string.Empty
```

On the real machine, it **stores `Windows.ApplicationModel.PackageVersion`**, not the numeric version. Replace with the actual four unsigned numeric fields:

```csharp
var v = package.Id.Version;
var versionText = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
packages.Add(new(
    SafeValue(package.Id.Name) ?? string.Empty,
    SafeValue(package.Id.FamilyName) ?? string.Empty,
    versionText));
```

Retain existing PackageManager user scope, enumeration limit, cancellation and exception handling. Do not introduce a second AppX collection.

**Production Utility name mapping (optional but useful, strictly bounded):** The JSON proves these installed AppX identities exist:

```text
MSI catalog: Realtek Audio Control
  AppX Name: RealtekSemiconductorCorp.RealtekAudioControl
  FamilyName: RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj

MSI catalog: DTS Audio Processing
  AppX Name: DTSInc.DTSAudioProcessing
  FamilyName: DTSInc.DTSAudioProcessing_t5j2fzbtdg37r
```

If implementing local installed-version display for these two Utilities in the same PR, use an **exact**, explicit catalog-name → AppX Name **and expected publisher family identity** correspondence, not general token overlap or the diagnostic projection's broad match hints. Apply at the existing Utility resolution boundary, keep distinct/conflicting versions Unknown, and prove a lookalike package/name is rejected. If publisher/package identity cannot be validated from production source, **do not broaden mapping**; the numeric capture fix alone is required.

Critically, catalog `Version="UWP"` supplies **no numeric latest version**. Even if local AppX installed version becomes visible, `ComparisonStatus` must remain **Unknown**, never UpToDate or UpdateAvailable.

## 5. Acceptance tests — synthetic, reproducible, no hardware gate

Use the existing:
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiLiveUpdateCatalogTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateDiagnosticSnapshotTests.cs` (if affected)
- Relevant UI/IPC tests only if their semantics actually change.

Required matrix:

1. Firmware: owned/Exact `4.19 == 4.19` → UpToDate; different version, null capture, weak confidence, wrong firmware identity, PID not matched → Unknown. No additional HID access or ownership mutation.
2. Goodix and Game Assist: exact identity + installed newer → UpToDate; installed equal → UpToDate; installed older → UpdateAvailable for these evidenced comparable domains. A different catalog field version, wrong HWID, ambiguous device, unsupported family → Unknown.
3. Dynamic Tuning / Trusted Execution / PPM: exact identity + same -> UpToDate. Mismatched conflicting `Version`/`PackageVersion`, malformed tuples -> Unknown.
4. DTS/IntelliGo: 2/4 expected components, matching provider/family, one common version equal to offered -> UpToDate **without dropping `Heuristic`**; mixed versions/unrelated/broad-only/inequality -> Unknown.
5. NPU: exact NPU Package/Driver string equality -> UpToDate; differing versions in either direction -> Unknown; wrong HWID/no installed/malformed -> Unknown; GPU/Wi-Fi/Bluetooth existing verdicts unchanged.
6. AppX: numeric `Major.Minor.Build.Revision` helper returns numeric dotted string rather than type name; optional exact Realtek/DTS Utility mapping rejects lookalikes and always preserves Unknown update verdict while OEM latest is `UWP`.
7. Preserve BIOS board-hex tests, current MSI/DSA duplicate suppression, excluded Utilities, unknown unrelated MSI driver rows, existing category/card counts, `VersionKind`, source attribution, old default `Unknown` contract, and PR #838 diagnostic JSON redaction/retention.
8. Check a **synthetic PR #839 CG3EM snapshot fixture**, verifying outputs for the concrete values in Section 2; avoid asserting all unmatched inventory becomes known, or enforcing a count as a substitute for correct evidence.

Build the solution and run existing Runtime/UI tests as in current CI. Update only tests whose prior `Unknown` expectation is *intentionally superseded* by this work order; never delete negative fail-closed tests to make the suite pass.

**No A2VM/CG3EM physical test is required of Local Codex, and physical testing is NOT a merge/review blocker.** The user will validate real card statuses on-device after merge.

## 6. Non-goals / anti-overengineering

- **No** update downloads/installers, Update-button wiring (it remains real but disabled), BIOS flashing, controller firmware writing, Windows Store opening, DSA package execution, “Update all”.
- **No** global relaxation allowing arbitrary `PackageVersion`/PnP `DriverVersion` cross-domain ordering.
- **No** wholesale matching of `NoUsableIdentity` rows based on equal numbers, software title similarity, broad vendor name, or provider-only heuristics. A separate evidence-backed work order may handle these later.
- **No** OEM `DataSet` construction/matcher or duplicate SetupAPI/WMI scan. No CTW integration or MSI Center M enable/disable behavior change.
- **No** HidHide/VIIPER/WING/physical ownership/recovery/power lifecycle changes. In particular, maintain the owned strong PID1902 read-only controller firmware capture's admission/fail-close behavior.
- **No** new state machine, epoch/lock/barrier, recurring poll, new background authority/service or generic version framework.
- **No** new JSON schema, request or IPC solely for verdict corrections; the existing diagnostics are evidence-only.

Aim for one minimal owner/comparator path and the smallest necessary explicit family admission policy; prefer verified correctness over suppressing all `Unknown` rows.

## 7. Delivery

Prepare **one PR**, grouped into reviewable commits if convenient:

1. Firmware + exact single-driver comparison corrections.
2. DTS/IntelliGo equality-only, Intel NPU equality-only.
3. AppX numeric capture (+ optional exact Utility identity mapping), regression tests.

In PR summary, clearly distinguish:
- which new verdicts are supported by **exact single-device evidence**;
- which are **constrained equality-only** with product-family evidence;
- which remain Unknown by design (unmatched rows, ambiguous versions, NPU mismatch, UWP latest sentinel).

Once CI and code review pass, squash merge without requiring physical device checks; product-owner post-merge verification remains a separate step.
