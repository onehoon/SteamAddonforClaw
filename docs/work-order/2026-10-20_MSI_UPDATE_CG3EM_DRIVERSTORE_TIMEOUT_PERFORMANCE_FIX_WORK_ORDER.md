# Work Order — CG3EM MSI Update DriverStore Timeout and Scan-Latency Fix

**Date:** 2026-10-20  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** one **small PR**, preferably two logically separated commits, from current `main` (PR #843 has already been squash-merged).  
**Executor:** local Codex; code and automated tests only.  
**On-device validation:** user's responsibility **after merge**. Do **NOT** require a physical MSI Claw, live Windows DISM run, manual scan, screenshot, or real-hardware validation as an implementation requirement, review blocker, CI gate, or merge condition.  
**Scope:** standalone Full PID1902 app, read-only MSI Update scan. **No CTW integration.**

## 1. Problem and evidence

PR [#843](https://github.com/onehoon/SteamAddonforClaw/pull/843) added CG3EM-only read-only DriverStore package detection to resolve the exact installed package versions of:

- Integrated Sensor Solution → `ishheci.inf` / Intel / System
- Windows Studio Effects → `mep_audio_component.inf` / Microsoft Corporation / AudioProcessingObject
- Realtek OED Driver → `IntcOED_OemLibPath.inf` / Realtek Semiconductor Corp. / Extension
- Realtek Audio Driver → `RtkSdcaXuMSI.inf` / Realtek / Extension

The first two actual post-merge scans were recorded in:

- `GoogleDrive/Addon/Log/1020/msi-update-20261020-141825.391-337525e6.json`
- `GoogleDrive/Addon/Log/1020/msi-update-20261020-141851.839-ad37e2a7.json`

Both snapshots are from **app v0.1.408.0 / CG3EM**, and both show:

| Evidence | Observed |
|---|---|
| `sources.msiState` | `Completed` |
| Intel DSA | `succeeded: true`, four resolved mappings |
| `sources.inventoryWarnings` | [`Cg3emDriverStoreTimedOut`] |
| `inventory.driverStoreStatus` | `TimedOut` |
| `inventory.driverStoreTotalCount` / `driverStoreCapturedCount` | `0` / `0` |
| Four affected installed-version outcomes | `Cg3emDriverStoreUnavailable` / Unknown |
| MSI Center M | `MSI Center M SDK` exact registry match, `3.0.2608.3101` correctly resolved |
| Intel IPF | `2.3.20303.5058` still correctly resolved |

**Observed fact:** the DriverStore capture reached its programmed timeout on both scans, and the manual update check was noticeably slower. **Not proven from the JSON:** precisely how much time PowerShell startup vs. DISM enumeration spent, or whether removing `-All` alone will make every machine fast. Do not claim a fixed elapsed time or a proven successful DriverStore package match until there is runtime evidence.

### Confirmed code path on main

`src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`:
- `DriverStoreTimeout = TimeSpan.FromSeconds(20)`
- `DriverStorePowerShellScript` calls **`Get-WindowsDriver -Online -All -ErrorAction Stop`** then filters to only the four allowed original INF names.
- `Capture()` performs PnP/WMI and uninstall inventory first, then `CaptureCg3emDriverStore()`, and only then finishes the local inventory.
- `CaptureDriverStoreProcessAsync()` waits for the subprocess and returns `TimedOut` on expiry.

`src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`:
- `RequestMsiLiveUpdateCatalogAsync()` starts MSI, Intel DSA and local inventory tasks together, but **`Task.WhenAll(msiTask, intelTask, inventoryTask)`** holds the final UI/catalog result until the slow inventory completes.
- The existing `MSI catalog query completed` app log already includes overall `ElapsedMs`. The diagnostic JSON currently does **not** contain separate DriverStore elapsed timing.

Microsoft `Get-WindowsDriver` documentation says `-All` additionally includes **default/inbox drivers**; without it, the command enumerates third-party drivers, which is the necessary scope for the four `oem###.inf` package candidates:
https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsdriver

**Do not broaden the scan to all Windows default drivers merely to find four OEM INF package identities.**

## 2. Desired outcome

1. Remove unnecessary default/inbox-driver enumeration for CG3EM MSI Update.
2. Avoid the currently observed **20-second wait** for one optional package-inventory source even when MSI and Intel DSA queries succeeded.
3. Expose **bounded, privacy-safe timing information** to differentiate a slow optional DriverStore scan from slow MSI/Intel remote requests on subsequent diagnostics.
4. Preserve all PR #843 installed-version matching semantics and safety: an unavailable DriverStore source yields honest `Unknown` for the four package rows, never a fabricated `UpToDate`/`UpdateAvailable`.
5. Do not turn the local optional scan into a new background service/cache/driver installation or a separate owner.

## 3. Commit A — narrow the DriverStore enumeration

Suggested commit title: `perf(msi-update): enumerate third-party DriverStore packages only`

In `MsiUpdateInstalledInventory.DriverStorePowerShellScript` replace:

```powershell
Get-WindowsDriver -Online -All -ErrorAction Stop
```

with:

```powershell
Get-WindowsDriver -Online -ErrorAction Stop
```

Keep existing:
- the same four exact allowed `OriginalInfName` values;
- selection/projection of actual DISM result properties `Driver`, `OriginalFileName`, `ProviderName`, `ClassName` and `Version`;
- structured JSON output, bounds, normalization, provider/class guards, conservative duplicate/version reconciliation and diagnostics;
- existing use of one elevated same-user Runtime process (do not launch a new privileged worker);
- CG3EM-only gating.

**Important:** Filtering `Where-Object` after `Get-WindowsDriver` does not make the underlying DISM enumeration targeted. Removing `-All` reduces enumeration scope but is **not** a guarantee that the command finishes quickly. Do not add an unsupported `-Driver ishheci.inf` shortcut: the `-Driver` parameter expects a valid published driver/INF identity and can omit other needed OEM candidates. Do not switch to text/localized parsing or hardcode observed `oem677.inf`, which varies by device.

Existing INF/provider/class/package-version matching must stay untouched in this PR:
- `ishheci.inf`, `mep_audio_component.inf`, `IntcOED_OemLibPath.inf`, `RtkSdcaXuMSI.inf`;
- ISS `5.8.58.0` main sensor device is **not** the HECI package `5.8.61.0`;
- numerical `20.43.274.009` equals `20.43.274.9`;
- installed DriverStore package is not necessarily an *active bound* device.

## 4. Commit B — bound latency and record meaningful stage timings

Suggested commit title: `fix(msi-update): bound optional INF inventory latency and record duration`

### 4.1 Reduce the optional query's wait budget

A user-triggered scan must not routinely spend the old 20-second timeout waiting for one read-only, optional DriverStore capture. Reduce **`DriverStoreTimeout` to 8 seconds** as a clear initial upper budget for the external query, without changing the existing MSI and Intel DSA query policies. This is a **policy limit**, not a claim that the OEM package inventory will necessarily succeed within 8 seconds on every system.

- On timeout, retain `TimedOut` / `Cg3emDriverStoreTimedOut` and preserve `Unknown` on the four package rows.
- No retry loop, repeating enumeration for each INF, fallback to `-All`, second expensive command, or waiting again to improve the match.
- Honor manual-request cancellation, shutdown and existing bounded stdout/stderr/row limits. Ensure process cleanup does not introduce an **unbounded wait** if the launched child refuses to exit after cancellation/timeout; use the current best-effort kill and a small finite cleanup grace period rather than waiting forever.
- Be conservative with failure: do not treat `0` captured packages on `TimedOut` as successful proof of absence. No fallback to unrelated active PnP device versions.

If the implementation can reuse an already existing finite process-stop helper, do so. Do not introduce a process manager, new state machine, epoch, lock, queue, task registry, or speculative concurrency protection solely for this one-shot query.

### 4.2 Collect end-to-end elapsed durations; do not invent root cause

Add one **small, optional elapsed duration** (e.g. nullable `ElapsedMs` init-only property) to `MsiUpdateDriverStoreCapture`, populated with `Stopwatch` around the actual subprocess operation, including timeout/error paths. Preserve existing positional record constructors and test callsites where possible.

Log a single structured `MsiUpdates` event when the manual request collects a CG3EM DriverStore capture (also on failed `TimedOut`), e.g.:

```text
Event=MSI.DriverStoreCapture
Model=msi.claw.cg3em
Status=TimedOut | Completed | Unavailable | ...
ElapsedMs=<nonnegative bounded value>
PackageCount=<number>
```

The existing overall `MSI catalog query completed / ElapsedMs` event is already sufficient for total duration. Keep the instrumentation limited to these two timestamps unless another particular stage needs evidence. The diagnostic snapshot for the user-uploaded JSON should also include the **DriverStore elapsed milliseconds** as an optional/additive bounded numeric field alongside `DriverStoreStatus` and `DriverStoreCapturedCount`, so the next snapshot alone can confirm the new cap or successful fast enumeration.

- No command-line, full DriverStore dump, serial numbers, machine paths, user identity, or stderr body in diagnostic files.
- Keep snapshot limits, sanitization, retention, local-only storage and schema compatibility (additive field only).
- Non-CG3EM inventory must not emit misleading DriverStore timing or run the extra scan.
- Cancellation must not be transformed into success/failure if existing caller policy propagates it.

### 4.3 Do **not** change manual query completion architecture without evidence

Do not rewrite `Task.WhenAll`, detach the inventory scan into a background task, complete the UI before the inventory is available, or introduce late-updating catalog results. The requested bounded, narrower local enumeration should remain owned by the **existing single manual query**.

The existing `PartialFailure` aggregation for inventory warnings is currently truthful. Do not mask the `TimedOut` warning as success just to make the UI look green; separate status presentation may be evaluated independently if the product policy changes later.

## 5. Automated tests and review checklist

Add/update focused tests in:

- `tests/SteamInputAddonforClaw.Tests/MsiUpdateDriverStoreTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateDiagnosticSnapshotTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs` only if actually necessary.

Minimum checks:
1. Script still calls `Get-WindowsDriver -Online -ErrorAction Stop` and **does not** contain `Get-WindowsDriver -Online -All`, `-All` or a secondary broad fallback.
2. The four exact INF allowlist members and projected fields stay intact.
3. The new finite timeout constant/policy is covered without actually running DISM or sleeping for eight seconds in CI.
4. Elapsed duration is present for successful, timed-out and process-command-error results (via synthetic results or injectable small pure helper rather than spawning PowerShell during tests). No time fields for `NotRequested`.
5. Failed capture returns `Unknown` for all four rows. Existing successful package comparisons, conflicts, provider/class/original-INF checks and numeric dotted version logic remain correct.
6. Existing Center M registry match, Intel IPF, BIOS and controller firmware results, Intel DSA four-family precedence, UWP exclusions and A2VM behavior remain unaffected.
7. Parser still accepts empty and multiple package JSON, rejects malformed/unsafe rows and respects bounds. Cancellation remains honored.
8. No regression to snapshot size/retention or privacy sanitization.
9. CI `dotnet build` and MSI Update tests run on normal CI infrastructure; no actual hardware, administrator account, local PowerShell/DISM or user-supplied logs are required to pass.

**Non-blocking post-merge user verification (outside local Codex PR):**

On CG3EM, the product owner may manually run MSI Update twice and upload the resulting `Addon/Log` JSON. Compare `DriverStoreStatus`, `DriverStoreElapsedMs`, overall `MSI catalog query completed` log `ElapsedMs`, and four package match outcomes. A successful package match is desirable but **not required for PR merge**; if even the narrowed inventory repeatedly hits the shorter budget, assess a separately evidence-backed alternative source rather than blindly increasing timeout or adding architecture now.

## 6. Change exclusions and deliverables

- Single PR with two reviewable commits, PR title/description describing the real regression and bounded fallback.
- **No GUI/UI redesign** and no automatic driver installs/downloads.
- No CTW, Game Bar/OEM1/WING, controller routing or PID1901↔PID1902 state changes.
- No HidHide, VIIPER or startup/shutdown/recovery behavior modifications.
- No experimental Windows versions, RDP/multi-session support or speculative races.
- No repeated hardware validation request before approval/merge.
- Update tests or comments that still say `-All` is required. This order explicitly supersedes the `Get-WindowsDriver -Online -All` guidance in `docs/work-order/2026-10-20_MSI_UPDATE_POST_842_CG3EM_PACKAGE_VERSION_REPAIR_WORK_ORDER.md` **only for the DriverStore capture command and latency budget**; all identity/comparison safeguards in the prior order remain authoritative.

**Expected after merge:** the normal scan is no longer forced to wait 20 seconds for the optional OEM inventory; a successful third-party package query may resolve the four affected versions, and an unavailable/slow query must fail conservatively within the shorter budget, with timing evidence for diagnosis.

