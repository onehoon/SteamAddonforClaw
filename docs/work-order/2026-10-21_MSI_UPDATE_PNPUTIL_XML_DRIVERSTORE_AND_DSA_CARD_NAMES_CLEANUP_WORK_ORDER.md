# Work Order — MSI Update: Replace DISM with PnPUtil XML, Resolve Active Duplicate INFs, Clean Legacy Code, and Use MSI Names for Intel DSA Cards

**Date:** 2026-10-21  
**Repository:** `onehoon/SteamAddonforClaw`  
**Base:** latest `main`, after PR [#844](https://github.com/onehoon/SteamAddonforClaw/pull/844) squash merge (`dd7d306ade02a9b525729374f608cafc1f3798f6`).  
**Delivery:** **one PR, three logical commits** (see Section 6).  
**Executor:** local Codex implements and tests the code; product owner performs real-device validation **after merge**. Do **not** make an MSI Claw, administrator-run local command, actual Windows PnPUtil output, screenshot, or hardware validation a Codex/CI/PR review blocker.  
**Scope:** standalone Full PID1902 app; **CG3EM MSI Update inventory and Intel DSA driver-card display names only**. No CTW integration.

## 1. Required reading and precedence

Review actual current code and these documents before modifying anything:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-20_MSI_UPDATE_POST_842_CG3EM_PACKAGE_VERSION_REPAIR_WORK_ORDER.md`
- `docs/work-order/2026-10-20_MSI_UPDATE_CG3EM_DRIVERSTORE_TIMEOUT_PERFORMANCE_FIX_WORK_ORDER.md`
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs` (inventory, capture, matcher, composer)
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateDiagnosticSnapshot.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateDriverStoreTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateDiagnosticSnapshotTests.cs`

This work order supersedes the **DISM/PowerShell capture mechanism, JSON projection/parser, and old duplicate-package conflict rule** from the October 20 work orders. Preserve their exact CG3EM OEM package identity, INF/provider/class, version-domain, fail-closed, and snapshot privacy requirements unless explicitly changed here. Full1902 ownership/lifecycle rules remain authoritative and unchanged.

Microsoft PnPUtil reference: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax

**Important evidence boundary:** `/enum-drivers /format xml` and `/enum-drivers /class <class> /devices /format xml` were actually run successfully on the target Windows 11 CG3EM. This is observed target behavior; do not claim identical measured performance for every Windows installation.

## 2. Runtime evidence — user-executed commands, October 21

Previous CG3EM app v0.1.409.0 diagnostic JSONs:

- `GoogleDrive/Addon/Log/1021/msi-update-20261021-115342.296-381d3e76.json`
- `GoogleDrive/Addon/Log/1021/msi-update-20261021-115509.843-6b1b64e7.json`

Both: `driverStoreStatus=TimedOut`, `driverStoreElapsedMs=8060/8045`, no captured DriverStore packages, and the four OEM-package installed versions Unknown. MSI and Intel DSA remote queries succeeded; the Intel DSA matcher resolved four device families. There is no evidence of a broken remote catalog.

### 2.1 Measured read-only commands (actual user output)

| Command | Measured wall time | Evidence |
|---|---:|---|
| `pnputil.exe /enum-drivers` | **324 ms** | Localized text with INF/provider/class/version |
| `pnputil.exe /enum-drivers /format xml` | **294 ms** | Culture-independent structured XML |
| `pnputil.exe /enum-drivers /devices /format xml` | **7,501 ms** | Full driver/device association scan; too slow for the default path |
| `pnputil.exe /enum-drivers /class System /devices /format xml` | **1,399 ms** | Includes ISS HECI packages and Started/Disconnected binding |
| `pnputil.exe /enum-drivers /class Extension /devices /format xml` | **3,970 ms** | Includes Realtek extension packages and Started/Disconnected binding |

**Decision:** launch **`pnputil.exe` directly from the existing Windows Runtime**, not through PowerShell, and parse XML. First run the fast `/enum-drivers /format xml` **once**. Only when a trusted target original-INF/provider/class identity has **conflicting candidate versions**, resolve the affected class via `/class <System|Extension|AudioProcessingObject> /devices /format xml`, **once per needed class**, not per INF. Do not run full `/devices` unconditionally or repeat either query merely to chase timing.

### 2.2 Observed packages and bindings

| MSI row | Verified original INF / provider / class | Published package and version | Observed binding |
|---|---|---|---|
| Integrated Sensor Solution | `ishheci.inf` / `Intel` / `System` | `oem7.inf` — `5.8.30.0` | `Disconnected` |
| Integrated Sensor Solution | same | **`oem677.inf` — `5.8.61.0`** | **`Started`** |
| Realtek OED Driver | `intcoed_oemlibpath.inf` / `Realtek Semiconductor Corp.` / `Extension` | `oem439.inf` — `20.42.274.2` | `Disconnected` |
| Realtek OED Driver | same | **`oem642.inf` — `20.43.274.9`** | **`Started`** |
| Realtek Audio Driver | `rtksdcaxumsi.inf` / `Realtek` / `Extension` | **`oem690.inf` — `10.0.378.1`** | **`Started`** on three devices |
| Windows Studio Effects | `mep_audio_component.inf` / `Microsoft Corporation` / `AudioProcessingObject` | **`oem6.inf` — `3.0.8.0`** | Package present after user reinstalled effects; **no Started proof collected** |

Windows Studio Effects was absent from the earlier plain PnPUtil listing and appeared after the user's reinstall. Do **not** hardcode `oem6`, `oem677`, `oem642`, etc.; they vary by machine. Do not falsely describe WSE as proven active or the initial absence as proven total absence of the Windows feature.

Observed XML shape, to turn into **small synthetic test fixtures**:

```xml
<Driver DriverName="oem677.inf">
  <OriginalName>ishheci.inf</OriginalName>
  <ProviderName>Intel</ProviderName>
  <ClassName>System</ClassName>
  <DriverVersion>12/05/2025 5.8.61.0</DriverVersion>
  <Devices>
    <Device InstanceId="...">
      <Status>Started</Status>
    </Device>
  </Devices>
</Driver>
```

Other observed data: `DriverVersion` contains **date + whitespace + numeric dotted version** (e.g. `01/23/2026 20.43.274.9`). **Do not feed the whole date-prefixed string to numeric version comparison.** Derive and validate the terminal version token; do not depend on date format, system UI language or a specific `oem###.inf` number.

## 3. Target behavior: one bounded, read-only CG3EM inventory owner

### 3.1 Direct PnPUtil XML capture

Modify only the current CG3EM DriverStore capture slice in `MsiUpdateInstalledInventory`.

- Run Windows built-in `pnputil.exe` via `ProcessStartInfo` + `ArgumentList`; no PowerShell, DISM, shell text parsing, temporary output files, output-file writes, installation, download, or elevation prompt. Existing elevated Runtime remains the sole caller.
- Base args: `/enum-drivers /format xml`. Optional duplicate-resolution args: `/enum-drivers /class System /devices /format xml` or the needed class.
- Accept XML `Driver[@DriverName]`, `OriginalName`, `ProviderName`, `ClassName`, `DriverVersion`; for class lookups accept `Devices/Device/Status`. Support the target-observed element/attribute casing, ignore unrelated fields, and normalize INF names case-insensitively.
- Keep **exact four permitted** original INFs and existing exact provider/class matching. Retain the MSI `OemPackageIdentity` check in `ResolveCg3emDriverStorePackage`. No generic driver-name fuzzy mapping or cross-package fallbacks.
- Process returned Windows XML with a safe, bounded `XmlReader`/equivalent; disable DTD and external resolution. XML parsing should not require a specific root spelling if the observed `Driver` structure is present.
- Retain bounded stdout/stderr and finite row count, but **recalibrate the raw output cap**: unlike old PowerShell's prefiltered 4-INF JSON, `pnputil` emits the *entire third-party driver listing* before local filtering, so blindly keeping the old **64 KiB** raw stdout cap would reject normal successful machines. Set a realistic finite byte/character cap (for example a few MiB) and a matching XML reader document bound; maintain the small cap for *retained matching packages*. Do not treat the hundreds of unrelated `<Driver>` entries as exceeding the old **128 matching-row** limit.
- Distinguish valid XML with zero matching target rows (`Completed` with no packages) from subprocess failure, timeout, invalid XML, output truncation, or invalid allowlisted target row (`Unknown` and an appropriate existing/bounded warning). Never manufacture an `UpToDate` result for a failed capture.
- Honor manual cancellation. Use existing finite child-process cleanup pattern and real-world limits; cap the **overall optional base + class lookup work** to a reasonable manual-request budget (the prior 8-second budget is the initial policy target), not 8 seconds *per* class plus another 8 seconds for the base. Propagate user cancellation instead of presenting cancellation as a no-match.
- Preserve structured `MSI.DriverStoreCapture` logging, nullable/bounded `DriverStoreElapsedMs`, overall catalog timing, diagnostic retention/privacy, and CG3EM-only gating. Include class-lookup-failure evidence only in bounded, structured status/warning fields; never capture full XML or device instance IDs in the diagnostic JSON.

### 3.2 Resolve duplicate package versions by **started binding**, not numeric maximum

Keep current successful identity/number comparison behavior for a single exact matching package (or multiple packages with **the same comparable numeric version**). But when the same trusted original INF + provider + class has **different versions**:

1. Determine the needed class from existing static `MsiCg3emDriverStoreIdentity` (do not trust arbitrary XML strings to select a process argument).
2. Run a class-scoped `/devices` XML query only for the class(es) with differing candidates; reuse its result for all four targets in that class.
3. Cross-reference **published `oem###.inf` identity** from the trusted base listing with the class-scan `DriverName`; also revalidate original INF/provider/class so an unrelated package can never make a candidate active.
4. Consider a package **active** only if that precise driver node contains at least one `Devices/Device/Status` equal to `Started` (case-insensitive). Three Started devices under one Realtek INF are **one** active package, not three conflicting candidates.
5. A unique comparable version among the eligible Started packages is the installed version; disregard Disconnected-only old packages *for this selection*, but retain their bounded evidence if already part of diagnostics. If multiple Started packages conflict, none is Started, status is missing, identities disagree, class command fails/times out, or XML cannot be parsed, return installed **Unknown** for the ambiguous row.
6. No `OrderByDescending(Version).First()`, no default to catalog version, no use of the main ISS device `5.8.58.0` as HECI package `5.8.61.0`, and no selection from disconnected packages.
7. A partial class-query failure must **not erase independently proven single-package results** (e.g., WSE or Realtek Audio). Represent the distinction with the smallest change to the existing capture result/diagnostics rather than a new state machine. For ambiguity, keep exact failure semantics rather than silently counting the base capture as a fully successful match.
8. Do not require additional class lookups for WSE merely to prove a `Started` binding: the current product feature is matching a **DriverStore package version**, not certifying a running camera/audio feature.

A minimal implementation option (not a new framework): preserve `MsiUpdateInstalledDriverPackage` with one nullable, init-only `HasStartedDevice` property populated **only after a required class query**; `null` = not queried/unknown; `true` = Started; `false` = no Started in a successfully queried package node. Distinguish unavailable class-scan evidence from a confirmed `false`. Preserve old record constructors if practical. An equivalent simple representation inside the existing capture is acceptable.

### 3.3 Preserve exact comparison contracts

- `ishheci.inf` / Intel / System is the HECI **package** domain for MSI Integrated Sensor Solution. It is not the separate ISS base device version.
- `mep_audio_component.inf` / Microsoft Corporation / AudioProcessingObject is WSE; detect after reinstall.
- `intcoed_oemlibpath.inf` / Realtek Semiconductor Corp. / Extension is OED.
- `rtksdcaxumsi.inf` / Realtek / Extension is Realtek Audio.
- Versions `20.43.274.009` and `20.43.274.9` remain numerically equal. MSI `PackageVersion` remains the appropriate offered version for all four rows.
- Compare within the established `Package` domain only; preserve `Unknown` on invalid OEM package identity, provider/class mismatch, missing package/version, competing Started versions, and failed source.

## 4. Remove obsolete code, without refactoring unrelated MSI features

After the replacement and test migration, **delete unused legacy DriverStore capture machinery** from `MsiUpdateInstalledInventory.cs`:

- `DriverStorePowerShellScript` and PowerShell startup arguments, `Get-WindowsDriver` / DISM property projection and `ConvertTo-Json` pipeline.
- `ParseDriverStorePackagesJson`, JSON-property readers (`TryGetString(JsonElement,...)`, `TryGetProperty(JsonElement,...)`) and any JSON-only INF conversion helpers that no longer have another callsite.
- JSON-specific process-result parsing branches, old JSON row/overflow shape assumptions, unused imports/constants, and synthetic tests designed only for DISM JSON, replacing them with PnPUtil XML tests.
- Dead code only: do **not** remove `System.Text.Json` from other features if still required, shared WMI/PnP/registry helpers, OEM identities, defensive input bounds, process cancellation/cleanup, structured telemetry, `Task.WhenAll`, or snapshot schema fields.

Avoid inventing a general process executor, DriverStore service, redundant manager/interface/lock/epoch, caching layer, repeated retries, or SetupAPI/PInvoke path. A couple of small private parsing/process helpers within the existing owner is sufficient.

## 5. Intel DSA card titles — use MSI catalog names

Current culprit: `MsiUpdateCatalogComposer.MapIntelEntry()` creates `entry.PackageName + " — " + deviceName` (WMI/PnP name), making four UI cards extremely long.

**Target UI examples on this CG3EM:**

| Matched DSA family | Actual MSI OEM catalog card name |
|---|---|
| Graphics | `Intel Graphics Driver` |
| Wireless | `Intel Wireless LAN Driver` |
| Bluetooth | `Intel Bluetooth Driver` |
| NPU | `Intel NPU Driver` |

- When an MSI Intel-driver catalog row is **truly suppressed as the duplicate** of the corresponding resolved DSA device/family, transfer that actual MSI row's **`Name`** to the replacement Intel DSA card. This is a **display-title decision**, not a switch back to MSI version/download authority. On a different model, preserve that model's actual MSI alias, e.g. `Intel Arc Display Driver`; do not force CG3EM labels globally.
- Keep the **Intel DSA** package version/release date/source, hardware-specific PnP installed version, version-kind (`Driver` vs NPU `Package`), comparison policy, matched device identity and duplicate suppression unchanged. No suffix with WMI/PnP device description and no PnP hardware IDs in the card name.
- On absent/failed MSI catalog or when a DSA match does **not** correspond to a safely suppressed MSI row, use a **short stable family fallback** (`Intel Graphics Driver`, `Intel Wireless LAN Driver`, `Intel Bluetooth Driver`, `Intel NPU Driver`) without claiming it came from the unavailable OEM catalog.
- Do not transfer a conflicting MSI catalog name when `EvaluateIntelDuplicate` deliberately **retained** that MSI row (e.g. conflicting subsystem identity), or apply one MSI name to several distinct DSA devices only because they share a family. Preserve the existing distinct-device behavior without creating a new mapping framework.
- Prefer a small composer-local mapping built from the already-computed suppression outcomes and resolved Intel rows. Do not change public frontend DTO shape or UI layout unless there is an unavoidable binding defect.
- Ensure diagnostic snapshot of MSI rows still records the *original* source MSI names and original suppression reasons; DSA package IDs, versions and PnP evidence remain intact.

## 6. Proposed commits, scope and test plan

**One PR, three focused commits:**

1. `fix(msi-update): read CG3EM DriverStore via bounded PnPUtil XML`  
   Replace PowerShell/DISM, implement validated XML and finite subprocess logic, preserve existing telemetry/JSON schema; remove unused JSON-only production helpers and switch pure parser/process tests to synthetic XML.

2. `fix(msi-update): resolve conflicting OEM INF versions using Started bindings`  
   Trigger one `/class ... /devices /format xml` per actually ambiguous class, preserve only Started-package evidence for choosing among unequal versions, and handle failure/timeout/ambiguity conservatively. Do not re-query for singleton packages.

3. `fix(msi-update): display MSI catalog titles for Intel DSA replacements`  
   Preserve original matched MSI catalog names after safe duplicate suppression; short family-only fallback otherwise; update composer regressions and prune now-unreferenced WMI/PnP title helper if nothing else calls it.

**Automated test matrix (no PnPUtil process or hardware in CI):**

- XML parsing with real observed element names and `DriverName` attribute; case-insensitive original INF/provider/class; all four target package identities; date-prefixed `DriverVersion` including leading zeros.
- Full-driver XML with hundreds of unrelated packages should **not** hit the old filtered-JSON 64-KiB/128-total-row assumptions. Limits still reject oversized raw data, excessive *matching* rows, invalid/malformed XML, DTD, missing target fields, bad numeric versions, spoofed `oem###.inf`, unexpected exit code.
- Successful empty base listing vs failed/empty/truncated process result; cancellation propagation and finite timeout/kill behavior.
- Exact product fixture: ISS `oem7.inf 5.8.30.0 Disconnected` vs `oem677.inf 5.8.61.0 Started`; OED `oem439.inf 20.42.274.2 Disconnected` vs `oem642.inf 20.43.274.9 Started`; Realtek Audio `oem690.inf 10.0.378.1` with three Started devices; WSE single `oem6.inf 3.0.8.0` with **no device-status lookup**.
- No class scan for singletons or numerically equal version duplicates; one scan per needed class; stable output if class scan incomplete or times out. Conflicting Started packages or disconnected-only packages remain Unknown; **never highest-version wins**.
- Strict OEM `OemPackageIdentity`, original INF, provider and class; unrelated PnP devices cannot substitute for missing package; A2VM capture still `NotRequested`.
- Retain `MsiUpdateDriverStoreCapture` elapsed on success/failure, existing `MSI.DriverStoreCapture` event, diagnostic `DriverStoreElapsedMs`, no machine paths/instance IDs/full XML in persisted diagnostic, partial-warning truthfulness.
- DSA card tests for all four families and each actual suppressed MSI title; graphics alias variant; MSI unavailable fallback; non-suppressed identity conflict; multiple distinct DSA devices. Assert DSA versions, installed versions, origin labels, comparison types, suppression counts unaffected.
- Preserve MSI Center M SDK, Intel IPF, BIOS, controller firmware, CG3EM other mapped entries, A2VM matching, utility exclusions, `Task.WhenAll` behavior, WinUI/overlay contracts.

Run the existing solution Release build plus focused `MsiUpdate` tests and `git diff --check`. Do not add CI tests that invoke elevated PnPUtil or require a physical MSI Claw.

## 7. Expected post-merge verification (user only; never a PR blocker)

The product owner may re-run manual MSI Update on CG3EM and check the JSON/log for:

- `DriverStoreStatus`, `DriverStoreElapsedMs`, `MSI.DriverStoreCapture` and overall catalog `ElapsedMs` — compare to 8,045/8,060 ms old timeout; measured PowerShell timings were local experiments, **not a guaranteed application performance target**.
- The four package rows: ISS **5.8.61.0**, WSE **3.0.8.0** after reinstall, OED **20.43.274.9**, Realtek Audio **10.0.378.1** if the same INF evidence remains.
- DSA cards with concise MSI catalog names, while still showing Intel DSA latest versions and preserving source/status behavior.
- No CTW involvement or controller ownership, HidHide/VIIPER, virtual device, PID1901/PID1902 lifecycle changes.

**Acceptance priority:** correct and deterministic source matching, honest Unknown/fail-close under real I/O failures, removal of obsolete code and a simpler single owner. Do not block on speculative races or unrelated redesigns.
