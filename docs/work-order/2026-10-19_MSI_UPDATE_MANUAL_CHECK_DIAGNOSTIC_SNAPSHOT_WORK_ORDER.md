# Work Order — MSI Update Manual-Check Diagnostic Snapshots (Post-PR #837)

**Date:** 2026-10-19  
**Scope:** ONE bounded implementation PR; no update/matching behavior changes  
**Product:** SteamAddonforClaw, standalone Full PID1902, supported MSI Claw 7/8 AI+ A2VM and Claw 8 EX AI+ CG3EM  
**Owner:** Local Codex implementation + deterministic automated tests; product owner handles optional real-device verification **after merge**

## 1. Goal and locked decisions

When the user clicks the **existing Device → MSI Update → Check for updates** button, persist a bounded, privacy-safe JSON diagnostic snapshot of the **data already collected for that request**. The report must let us investigate why a given Driver/Utility row has an unknown *installed* version, why a *latest* version is absent/UWP, and why some MSI rows are hidden in favor of Intel DSA.

- Generate on **each completed manual check**, including partial source failure. No separate Developer-menu button, diagnostic UI, consent dialog or new polling schedule.
- **Reuse** the existing MSI/Intel DSA queries and the **same** captured Windows inventory objects; no second SetupAPI, WMI, Registry, AppX, OEM DLL or network scan just for diagnostics.
- Save under the canonical persistent Addon data root, not the Velopack `current` installation folder:
  `<AddonDataPaths.RootDirectory>/Diagnostics/MsiUpdates/msi-update-<UTC yyyyMMdd-HHmmss.fff>-<short unique suffix>.json`.
- Keep **five most recent successfully written** JSON snapshots in that dedicated directory, newest first. Prune only files owned by this feature. No retention process, scheduled worker, background task, runtime cache or state database.
- Saved files are for **manual diagnosis only**. Do not read them during subsequent version matching or update decisions. Every Check captures fresh values.
- Treat failed snapshot serialization/directory creation/write/prune as best-effort **diagnostic** failure, never a catalog/network/installed-version error; preserve the exact frontend result and source-status semantics.
- Keep one clear Runtime owner; no new IPC/Frontend contracts, UI cards or tabs. No changes to the OEM helper invocation, native controller mode, PnP ownership, HidHide, VIIPER, routing, setup or shutdown.
- Do **not** implement PR2 latest-vs-installed comparison or change version mapping, Intel DSA duplicate suppression, Utility exclusion, OEM latest-version display, or any installation/download action.

This is a **diagnostic foundation**, **not** a new matching algorithm.

## 2. Mandatory sources — read before coding

### Product and architecture

- `docs/Full 1902 Implementation/README.md` and current Full1902 authority documents:
  - `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
  - `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
  - `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
  - `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- Existing work orders:
  - `docs/work-order/2026-10-18_MSI_UPDATE_INSTALLED_VERSIONS_INTEL_DSA_COMPARISON_PR1_PR2_WORK_ORDER.md`
  - `docs/work-order/2026-10-18_MSI_UPDATE_POST_836_MATCHING_AND_UTILITY_EXCLUSIONS_WORK_ORDER.md`
- PR #837, already squash merged as `6c84ea5811e65ebfbccf1d3d3eb7f4d17ede44f9`. Review **current main** when implementing rather than assuming this commit is still HEAD.

### Primary updated REA

**Read the entire report, especially the latest Section 11 (11.1–11.10):**

`C:\GoogleDrive\Addon\MSI\MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md`

https://drive.google.com/file/d/1QYmnJBwEowwaJC017c7skHJDamD9zpEU/view

Also relevant: `MSI_CENTER_M_COMPREHENSIVE_REA_REPORT.md` in the same folder (reference only).

**REA conclusion B — Hybrid:** `SetupApiDeviceQuery.DeviceFastQuery.GetAllDevices()` is independently invocable but would duplicate Runtime's current PnP/WMI collection and measured ~2.89s cold / ~1.43s warm on a research PC. The MSI `DataSet` constructor/refresh/whole matching path has persistent file/log/process effects and produces filtered candidate lists, **not** a complete installed-version inventory. This PR must not call either path, alter MSI Center M state, or borrow broad OEM name heuristics. Existing Addon conservative matching remains authoritative.

### Real-device symptom baseline

Latest `v0.1.403.0` CG3EM Runtime log:
https://drive.google.com/file/d/1iugfxa4hNbuQlLDHcP1f-o3ZR6q8xDc6/view

- MSI and Intel DSA query both complete; no local inventory warnings.
- 23 displayed Driver entries = 19 retained MSI + four DSA rows; 11 with installed version, 12 without.
- Eleven retained rows report `NoUsableIdentity`; Realtek Audio reports `NoMatchingPresentDevice` under a **narrow A2VM** codec rule, not proven suitable for CG3EM.
- Utility: three rows retained (`Realtek Audio Control`, `DTS Audio Processing`, `MSI Center M`) all `Unmatched`; three exact Utility exclusions occurred.
- Current log does **not** contain raw OEM discriminants or raw latest-version fields; therefore an installed-match failure says nothing about whether the latest version was absent.
- Four MSI Intel family rows were suppressed by unique exact DSA-resolved family. No changes to this policy.

The report's Section 11.8 contains a **23 Driver + 3 Utility** per-row OEM/Addon gap matrix. Do not claim each of the 12 unknown packages is actually installed or has a one-to-one active driver.

## 3. Verified current code/data flow

`src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`:

- `RequestMsiLiveUpdateCatalogAsync` (near line 2175) is called by the existing manual UI operation.
- After supported-hardware admission it starts **three tasks together**:
  `QueryMsiCatalogSafelyAsync`, `_intelDsaCatalogClient.QueryAsync`, `CaptureMsiUpdateInventorySafelyAsync`.
- On completion, `MsiUpdateCatalogComposer.Compose(msi, intel, inventory, onIntelMatch, onDiagnostic)` produces the frontend snapshot and emits **bounded** per-row match outcomes (e.g. `NoUsableIdentity`, `NoMatchingPresentDevice`, `ExactHardwareId`, `HeuristicFamily`, `SuppressedByUniqueDsaFamily`, `ExcludedByPolicy`, `Unmatched`).
- It logs a final summary and returns the snapshot. **Insert diagnostic capture at this existing Runtime-owned boundary**; do not hook state invalidation, activation, frontend refresh or source clients globally.

`src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml.cs`:

- `MsiUpdatesCheckButton_Click` makes exactly the manual frontend request; cancellation is propagated on navigation. UI should remain untouched.

`src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs`:

- `MsiLiveUpdateCatalogResultParser.ParseCategory` parses OEM `SoftwareRecord` and retains `Name`, `Version`, `ReleaseDate`, `Description`, `DeviceId`, `Guid`, `UpgradeGuid`, `PackageVersion`, `DriverVersion`, `InfName`.
- `ValidateDeviceId` drops non-backslash markers such as `NOT_FOUND`; `ValidateGuid` drops sentinel strings such as `UWP`. The parser **does not retain** OEM `Type`, `Other`, `File`, `Siparam`, `Crc`, which REA Section 11 identifies as branch selectors. This is precisely the diagnostic evidence gap.
- `MsiUpdateCatalogComposer.MapMsiEntry` selects latest-version display as `DriverVersion ?? PackageVersion ?? Version` and maps literal `UWP` to unavailable. **Preserve this unchanged.**
- Helper `MsiLiveUpdateHelper/Program.cs` already returns bounded category responses. Do not modify its x86 OEM signing, request or trust boundary.

`MsiUpdateInstalledInventory.CaptureAsync` (same Runtime namespace) already includes:
`BiosVersions`, present PnP device identity and HWID/CompatibleID, signed-driver `DeviceClass/DeviceName/Provider/DriverVersion/InfName`, `Software` Registry key/ProductCode/UpgradeCode/DisplayName/DisplayVersion/Publisher, AppX `Name/FamilyName/Version`, controller firmware and bounded warning kinds. **Do not run another capture.**

`IntelDsaCatalogClient` returns packages/components/detection values; `IntelDsaHardwareMatcher.Resolve` projects actual matches. Do not save Intel ZIP/JSON, package URLs, nor unfiltered DSA catalog.

Storage authority: `src/SteamInputAddonforClaw/Install/AddonDataPaths.cs`. Use `AddonDataPaths.RootDirectory` (canonical VeloPack **sibling** `SteamInputAddonforClaw-Data` root); do **not** independently hardcode `%LOCALAPPDATA%` or derive from `AppContext.BaseDirectory`.

## 4. Required snapshot contents (allow-listed, not a raw dump)

Use a simple, explicitly defined, versioned JSON projection (suggest `schemaVersion: 1`). The schema below is illustrative; minor naming differences are fine, but its semantics and redaction requirements are mandatory.

### A. Query context

- `capturedAtUtc`, `appVersion`, recognized supported `hardwareModelId` (A2VM 7/8 or CG3EM), MSI request model name **only if safely normalized**, `os`, optional `countryCode`.
- Catalog `state/errorKind` and **per-category** `state/errorKind/count`; Intel DSA `succeeded/errorKind/catalogCreatedUtc`; installed inventory **warning kinds**.
- Original **unfiltered MSI** item counts, exclusions count, retained MSI and appended DSA counts.
- Never include device serial, BIOS identifying number, current Windows username, MachineGuid, paths, auth headers, AWS signed request or complete helper stdout.

### B. Each original MSI catalog row (all 4 categories)

Record `sourceCategory` + **stable source ordinal within its response category** (e.g. `sourceIndex`) so duplicated names can be analyzed independently, including entries filtered from UI. Include:

- `name` (bounded catalog title), `version` (OEM DisplayVersion/version raw **value**, including `UWP`), `driverVersion`, `packageVersion`, `releaseDate`, plus the **actual frontend latest version and latest kind**, if mapped.
- Existing parsed `deviceId` **only as a safe normalized hardware identity projection** and a flag telling whether the original field was present/valid/rejected/was a known marker (e.g. `NOT_FOUND`). Do not write arbitrary original DeviceID or full PnP instance strings.
- `guid`, `upgradeGuid` if valid product metadata; and the **raw-field classification** (valid / UWP sentinel / NOT_FOUND sentinel / missing / other) for `GUID`, `Upgrade_Guid` and `Crc`, where present. This is needed to understand OEM's UWP/PPKG/DISM branches even when existing `ValidateGuid` drops the marker.
- OEM `Type` (bounded scalar), `Other` **only as safely allow-listed scalar/marker or bounded field-kind classification**, `File` as extension/source/Store-link *kind* (never raw URL/path), `Siparam` as marker/command type only (never full installer command/arguments), and `Crc` as bounded version/sentinel/CRC presence metadata (do not preserve arbitrary opaque content).
- `uiDisposition`: retained / excluded Utility / suppressed duplicate MSI / appended DSA (latter DSA rows are listed separately); source-specific reason, count and matching confidence if matched.
- `installedVersion`, `installedConfidence`, and `matchOutcome` for mapped rows; null with reason when unavailable.
- Explicit `latestUnavailableReason` distinct from `matchOutcome`: e.g. no version fields, `UWP` sentinel, category failed, or mapped value present. Do not label a missing latest version as a local match failure.

**Important:** Extend only the **internal** parsed catalog/diagnostic DTOs as necessary to carry these missing discriminants from the already-parsed helper response; do not change existing matching conditions or frontend projection. Do not persist the helper response JSON verbatim. Values can be safely classified **while the bounded JSON DOM is already open**. Preserve existing validation for fields used for matching. If an unexpected raw field is an array/object/path/URL/free-form command, record its type or `redacted` marker rather than flattening it to text.

### C. Local inventory evidence (from the same manual-check capture)

Persist a **bounded, relevant** diagnostic view, not an entire Windows software inventory:

- Relevant present PnP/driver rows: snapshot-local `rowId`/ordinal, device class, OS driver class, generic/bounded device descriptor **only when it contains no user-assigned name**, driver provider, installed driver version and safe INF basename; standardized hardware-ID components sufficient for matching (bus + vendor/device or equivalent ACPI/HDAUDIO/INTELAUDIO tokens; optionally SUBSYS/MI/REV when non-identifying). Only persist safe **hardware-type** prefixes; strip the instance suffix and any serial/opaque identifiers.
- Include relevant signed-driver rows **even when MSI has no usable identity**: use deterministic broad evidence filtering by MSI driver families/provider/class (Intel, Realtek, Goodix, DTS and others present in OEM response), **for diagnostics only**. If a candidate cannot be selected confidently, record enough bounded class/provider/version/INF information to explain the gap; **do not mark it as matched**.
- Relevant Uninstall registry entries for the three retained Utilities and their corresponding vendor/product families: safe display name and publisher, display version, validated ProductCode/UpgradeCode, registry view/source **kind** (if readily available). No arbitrary RegistryKeyName except validated product GUID; no raw registry paths.
- Relevant AppX packages: safe package Name/FamilyName/Version, only those associated with MSI/Realtek/DTS or catalog identity evidence. No package full install paths, user SIDs or user list.
- BIOS version, controller revision with existing `Exact/Heuristic` provenance **only as already returned by inventory**.
- If a value cannot be safely represented, omit/redact that field and add a compact reason flag. **Do not sacrifice privacy to fill a diagnostic field.**

Do not serialize `MsiUpdateInstalledInventory`, `MsiLiveUpdateCatalogQueryResult`, PnP `DeviceId`, `IntelDsaPackage`, `FrontendMsiLiveUpdateCatalogSnapshot` or any OEM raw JSON **directly**: their present/future members are not a privacy-reviewed diagnostic contract.

### D. Intel DSA outcome evidence

- For resolved matches only: `packageId`, relevant family (Graphics/Wireless/Bluetooth/NPU), package/component **version**, version kind, release date, and safe **hardware-type identity**, with an optional snapshot-local PnP `rowId`.
- Include the existing duplicate suppression outcomes and their `CandidateCount/VersionCount`.
- Exclude URLs, unrestricted detection-value arrays, archive data and raw machine identifiers.

### E. Representative schema (not mandatory exact property spelling)

~~~json
{
  "schemaVersion": 1,
  "capturedAtUtc": "2026-10-19T00:00:00Z",
  "hardwareModelId": "msi.claw.cg3em",
  "sources": {
    "msi": "Completed",
    "intelDsa": "Completed",
    "inventoryWarnings": []
  },
  "msiRows": [
    {
      "sourceCategory": "Driver",
      "sourceIndex": 0,
      "name": "Intel HID Event Filter",
      "oem": {
        "version": "2.2.2.11",
        "driverVersion": null,
        "packageVersion": null,
        "type": "Chipset",
        "deviceIdKind": "Missing",
        "guidKind": "Missing",
        "fileKind": "Archive",
        "siparamKind": "RedactedCommand"
      },
      "latest": { "value": "2.2.2.11", "unavailableReason": null },
      "installed": { "value": null, "confidence": null, "matchOutcome": "NoUsableIdentity" },
      "uiDisposition": "Retained"
    }
  ],
  "localPnP": [
    {
      "rowId": 1,
      "deviceClass": "System",
      "driverProvider": "Intel",
      "driverVersion": "1.2.3.4",
      "infName": "oem42.inf",
      "hardwareTypes": ["PCI\\VEN_8086&DEV_1234"]
    }
  ]
}
~~~

The example values are **synthetic**, not claims about the actual user's Intel HID Event Filter driver or OEM Type field. Do not hardcode any illustrated model, field value or family fallback in production.

## 5. Correlating diagnostics to exact source rows

The current `MsiUpdateCatalogMatchDiagnostic` carries category, name, source, outcome, confidence and candidate/version counts but **no original source ordinal**. Names are not guaranteed unique.

Use a minimal approach such as an optional `SourceIndex` field on that internal diagnostic record, emitted by `MsiUpdateCatalogComposer.Compose` while iterating source rows. Preserve the **original pre-filter index** through Utility exclusion and Intel DSA duplicate suppression. Ensure ordinary onDiagnostic DEBUG logging behavior stays the same. For filtered/excluded rows that do not call the installed matcher, record an explicit disposition/reason rather than fabricating an installed match outcome.

A small internal projection callback is acceptable if genuinely simpler than altering the diagnostic record. Do **not** create a parallel composer, clone the matching decision logic, or re-run the matcher to build JSON. The final frontend `Entries` list has a different order/count from the raw MSI list due to filter + DSA append; do not zip the two arrays by index.

## 6. Saving and retention (Runtime only)

Add one small Runtime-owned serializer/store (e.g. `Devices/MSI/LiveUpdate/MsiUpdateDiagnosticSnapshot.cs`, with projection/store together unless separation genuinely improves clarity).

- Accept the **already collected** model, MSI result, Intel result, inventory, Compose snapshot and row diagnostics. Inject an output root in tests; production root derives from `AddonDataPaths.RootDirectory`.
- Serialize an allow-listed DTO with `System.Text.Json`. Use bounded fields and row counts consistent with existing parser/inventory limits; ensure no unbounded file growth (apply practical size cap to output, e.g. 1–2 MiB, with a clear `Truncated` field when needed, rather than dumping the entire inventory).
- Create the exact owned directory if needed; write UTF-8 to a unique `.tmp` in that same directory; atomically move/rename to `.json` only after a successful serialization/write. Clean up owned temporary file on failure, best effort.
- Use UTC sortable names + a short uniqueness suffix, to avoid overwriting repeated clicks within one second.
- After a **successful write**, prune `msi-update-*.json` files in **that exact owned folder** to the newest five. Never delete unrelated JSON, subdirectories, other Addon files or logs. Prune errors are nonfatal; don't add elaborate retry/lock/epoch machinery for theoretical concurrency.
- Handle access denied, disk full, malformed local values, serialization failures and retention failures as best-effort. Record one sanitized `MsiUpdates` INFO entry with filename and row counts on success, or one bounded WARN with exception **type/error kind only** on failure. No full path or row-by-row raw report in ordinary logs.
- On manual-check cancellation (e.g. the UI navigates away), propagate cancellation per existing behavior; don't create a fake completed snapshot or force a report write. If cancellation occurs during a write, ensure a partial `.json` is not published.
- Unsupported/unresolved hardware keeps existing early return and **does not** write a diagnostic snapshot. Failed/partial network responses after supported-device admission **do** produce a report of whatever data was available, explicitly marked by source/category status.
- Snapshot writing must not turn a completed query into a failed UI request or delay frontend completion with a fresh expensive scan. No new transport fields.

## 7. Source-level implementation hints

1. In `MsiLiveUpdateCatalogClient.cs`, add only the **diagnostic representation** of required OEM discriminants to the internal record/parse path. Parse `Type`, `Other`, `File`, `Siparam`, `Crc`, and raw-identity **presence/marker classification**, while keeping the existing `ValidateDeviceId` / `ValidateGuid` matching inputs unchanged. Avoid forwarding raw URLs or command lines to the persistent DTO.
2. In `MsiUpdateInstalledInventory.cs`, extend diagnostics with stable source ordinals/dispositions where necessary. Keep Intel DSA selection, known family lists, exact/heuristic confidence and Utility filtering byte-for-byte behavior equivalent.
3. In `InProcessAddonFrontendControl.RequestMsiLiveUpdateCatalogAsync`, collect the already existing onDiagnostic/onIntelMatch observations in local per-request collections (still perform current AppLog DEBUG callbacks). After Compose, invoke the small store **best-effort**, then return the **same** snapshot. Do not alter `IAddonFrontendControl` signatures or use a second frontend RPC.
4. Add a focused storage/projection class and deterministic tests using in-memory fixtures and a temp folder. Keep private helper inputs, raw WMI data and platform PnP enumeration out of test execution.
5. Confirm `Add-onDataPaths` resolution and storage location. If a one-line tested `ResolveMsiUpdateDiagnosticDirectory(rootAppDirectory)` helper is useful, add it there; otherwise use `Path.Combine(AddonDataPaths.RootDirectory, "Diagnostics", "MsiUpdates")` directly without a generic storage registry.

## 8. Tests — mandatory automated code verification, NOT hardware gating

Use existing `tests/SteamInputAddonforClaw.Tests/MsiLiveUpdateCatalogTests.cs`, `MsiUpdateInstalledVersionsTests.cs`, and `AddonDataPathsTests.cs` patterns; add a narrowly named `MsiUpdateDiagnosticSnapshotTests.cs` only if that reduces clutter.

Required deterministic coverage:

1. Existing all-four-category parsing and UI result remains identical before and after adding diagnostic fields; existing latest precedence and literal `UWP` display rule unchanged.
2. Parse `Type`, `Other` and **marker/presence classifications** for DeviceID (`NOT_FOUND`, bare partial, valid PnP identity), GUID/UpgradeGUID/Crc (`UWP` sentinel), `File`/ `Siparam` without persisting their raw path/URL/command contents. Test invalid/malformed optional types fail diagnostically **without rejecting an otherwise valid catalog row**.
3. Two same-name catalog entries remain distinguishable by category/source ordinal and diagnostics even after Utility exclusions and DSA suppression; no newly inferred matches.
4. Successful manual check stores all four category states/counts, Intel DSA source and resolved-match metadata, existing version fields, local PnP/registry/AppX safe projection and match outcomes with correct identities and confidence.
5. `NoUsableIdentity`, `NoMatchingPresentDevice`, `Unmatched`, `ExcludedByPolicy`, `SuppressedByUniqueDsaFamily`, and `ExactHardwareId` appear correctly in the diagnostic projection. Latest-unavailable classifications are independent of installed-match outcomes.
6. Synthetic hardware instance ID containing a serial segment, an installed app path / username, device name containing a user-assigned label, a raw helper response containing a URL / query token, and a catalog `Siparam` containing an executable command **never appear** in emitted JSON or logs. Test the allow-list, not merely a regex applied to a serialized raw DTO.
7. A2VM 7, A2VM 8 and CG3EM model labels use the same serializer/schema. No hardcoded A2VM Realtek identity is inserted into CG3EM data.
8. Failure/partial source results persist accurate failure states and no invented latest/version; unsupported device and request cancellation do not create a false completed file.
9. Store path is under canonical data root (not `current`), successful file is valid JSON, five newest retained after six writes, unrelated files remain untouched, and broken write/prune cannot affect the original frontend snapshot.
10. Tests must not invoke MSI DLL, install package, modify real registry, launch Windows maintenance commands, change controller state, or access user hardware.

Run `dotnet build SteamInputAddonforClaw.slnx --no-restore -m:1 -p:UseSharedCompilation=false` and relevant Runtime/UI tests as appropriate for the repository's current CI. If build/test issues arise, distinguish introduced defects from pre-existing/environmental failures.

**The user owns post-merge physical validation. Do not require actual A2VM/CG3EM hardware tests before PR approval or merge.**

## 9. Scope and anti-overengineering rules

- One PR focused on *observability of existing manual Check*; no new Device/Developer UI.
- No separate OEM reflection method calls, `DeviceFastQuery`, MSI `DataSet` or OEM services.
- No additional inventory scans, scheduled scans, caches used for update logic or auto-update verdicts.
- No new state machine/manager/epoch/lock/retry/recovery service invented for pathological timing. Respect ordinary navigation cancellation, process shutdown, IO failure and existing ownership boundaries.
- No unrelated WinUI page styling or extra telemetry endpoint. All diagnostics remain local until manually shared.
- Do not persist *raw* full signed MSI catalog bodies, DSA ZIPs/detection arrays, arbitrary install scripts/URLs, entire Windows inventories, user/machine serial identifiers or file system paths.
- Do not reinterpret an OEM DISM/PPKG candidate version as the installed **active-device** driver version.
- Tests cover code invariants; real-device matching investigations will follow **after** this PR is merged and the user captures A2VM/EX snapshots.

## 10. PR handoff / acceptance checklist

A merge-ready implementation PR must demonstrate:

- [ ] Manual Check produces one JSON diagnostic snapshot from existing query/inventory data; no implicit scans.
- [ ] Valid row-level correlation of original OEM source ordinal with retained/excluded/suppressed records, including duplicate names.
- [ ] Both missing **installed** values and missing **latest** values are explained without conflation.
- [ ] Essential OEM branch discriminants are represented safely, including sentinel values currently discarded by the parser.
- [ ] Sufficient **sanitized** local PnP, software and AppX evidence for A2VM/CG3EM follow-up, without leaking instance IDs/serials, arbitrary paths, commands or authentication data.
- [ ] Output directory uses canonical `AddonDataPaths.RootDirectory`; retention exactly five completed report files; no interference with unrelated files.
- [ ] Write/prune failure and partial MSI/DSA/source failures preserve original UI result and source-state semantics.
- [ ] No change to current user-facing MSI/Intel DSA matching, Utility filters, version labels or frontend protocol.
- [ ] Deterministic unit tests and standard build/CI pass.
- [ ] PR description records evidence from REA Section 11 and the v0.1.403 CG3EM log; notes that **physical device tests are post-merge by the product owner**.

**Final intent:** Collect the missing identity/version evidence in a single privacy-safe diagnostic artifact per manual update check. Do not try to solve the still-unknown matches in this PR.
