# Work Order — Addon Log Content Cleanup After PR #803

> **Repository:** onehoon/SteamAddonforClaw  
> **Target:** main (base reviewed: 3f8d0eecc2f4c531afbca1c18d79615963d0ea60, PR #803 squash-merged)  
> **Date:** 2026-10-16  
> **Implementer:** Local Codex  
> **Delivery:** One focused PR (optionally two reviewable commits: repeated messages, then obsolete callback)  
> **Scope:** Existing Addon-owned log call sites and a retired Gordon-only diagnostic callback. **No behavior changes to sensing, routing, settings, controllers, IPC or startup.**

## 1. Purpose and authority

PR #803 already made Off / Info / Debug effective for Runtime, Main UI, warm hidden Overlay, and FSE Home, and moved appropriate INFO messages to DEBUG. **Build on that result. Do not reimplement or undo the PR #803 changes.** This follow-up removes noisy *messages themselves* from DEBUG and remaining routine INFO; it is not another threshold-setting project.

Read first:

- `docs/Full 1902 Implementation/README.md` (authoritative precedence);
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`;
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`;
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`;
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`;
- `docs/work-order/2026-10-16_ADDON_LOG_LEVEL_CONSISTENCY_AND_SEVERITY_HYGIENE_WORK_ORDER.md` (PR #803);
- `docs/work-order/FULL1902_0903_STATUS_AND_DIAGNOSTIC_LOG_CLEANUP_WORK_ORDER.md`;
- `docs/work-order/FULL1902_LIFECYCLE_LOG_HYGIENE_WORK_ORDER_2026-10-05.md`;
- `docs/work-order/FULL1902_CLEANUP_I_DISCONNECT_DEVELOPER_SYNTHETIC_STEAM_PRODUCTION_WIRING_WORK_ORDER.md` (parked diagnostics are intentionally preserved);
- `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md` for event-driven Xbox identity.

The Addon is standalone Full1902. **No ClawTweaks integration.** Supported product: one Windows user and one interactive session; do not add support infrastructure for RDP, Fast User Switching or hypothetical races. The physical owner, HidHide authority, typed VIIPER ownership, native PID1901↔PID1902 restoration, actual device loss/PnP re-enumeration, Sleep/Hibernate/Resume, restart/crash/shutdown, rollback and fail-close remain unchanged.

## 2. Evidence: why this specific cleanup

Real device log evidence: Google Drive `Addon/Log/AV2M/1015` and `Addon/Log/AV2M/1015-1`, 2026-10-15, collected **before** PR #803. These are log-entry counts across captured test sessions, **not production daily rates** or proof that the underlying functions are being polled:

| Message / category | Observed entries | Source-confirmed interpretation |
| --- | ---: | --- |
| XboxSession: `Candidate did not prove an exact XBOX game identity.` | 534 | 422 ordinary `NoPackage`; 112 `ConfigNegative` |
| Steam: `RunningAppID registry value read.` | 371 | all `RunningAppID=0`; direct reads occur from multiple consumers |
| CenterM.Startup: `Startup state captured.` | 288 | all `Disabled` in sample |
| Profiles: `Profile document loaded.` | 171 | same 2 Steam / 1 Xbox game counts |
| XboxSession: `Resolved live process MicrosoftGame.config location.` | 112 | repeated negative config location checks |
| Profiles.Tdp.Wmi: `MSI_ACPI compatibility fallback succeeded` | 85 | routine successful helper fallback |
| XboxSession: `A classified XBOX candidate process exited.` | 76 | non-active candidates |
| NativeMode: `Raw MSI HID write succeeded.` | 65 | normal transport 64/64-byte completion, not final device mode proof |
| HidHidePackageProbe: `HidHide package evidence found.` | 62 | same installed package/version |
| Status: `System status snapshot refreshed.` | 29 | 27 unchanged `Ready` snapshots |

These are targeted removals, **not a request to silence error or state-transition reporting**. Verify the exact HEAD symbols again when implementing.

### Important clarification — Steam is event-driven, not polling

`SteamRunningAppIdRegistrySource` uses `RegNotifyChangeKeyValue`. But `SteamSessionRuntime.ActualRunningAppId` delegates to `GetRunningAppId()`, and the presentation, profile, status, cadence and UI snapshot consumers can independently request its current value; 371 DEBUG messages do **not** mean a dedicated periodic Steam poll exists.

**Remove the unconditional successful-read DEBUG entry only.** Preserve actual registry reads, the registry change watch, `Changed` notification, resume refresh, current-value semantics, abnormal-read WARN, and all consumers. Do **not** introduce a registry cache, new state owner, debounce, polling loop, event redesign or synthetic AppID source as part of log cleanup.

## 3. Required direct source edits

### A. Remove routine success / unchanged snapshot messages

Make these narrow, direct deletions at the current call sites. Do **not** remove the operations that the messages describe:

1. `src/SteamInputAddonforClaw/Steam/SteamRunningAppIdRegistrySource.cs` — remove `AppLog.Debug("Steam", "RunningAppID registry value read.", ...)` within `GetRunningAppId()`. Keep `AppLog.Warn(... "RunningAppID registry read failed." ...)`.
2. `src/SteamInputAddonforClaw/CenterMStartup/CenterMStartupControl.cs` — remove the `"Startup state captured."` line from `Capture()`. PR #803 already demoted this line to DEBUG; do not undo unrelated severity changes. Keep read/classification and every meaningful Center M mutation/readback/outcome log.
3. `src/SteamInputAddonforClaw/Profiles/ProfileStore.cs` — remove `"Profile document loaded."` from the normal successful deserialization return. Keep first-run/default creation, invalid schema/parse/I/O warnings, and existing save outcome records. No persistence changes.
4. `src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs` — remove just `"HidHide package evidence found."` from the valid/installed branch. Retain invalid/conflicting package evidence and separate **actual HidHide application/target/driver mutation + verification** diagnostics.
5. `src/SteamInputAddonforClaw/Status/SystemStatusProvider.cs` — remove only `"System status snapshot refreshed."` at the end of normal `CaptureCore()`. Keep status computations and fallback/failure messages; a changed `AddonStatus` must still be observable through existing higher-level lifecycle/state events.
6. `src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs` — remove only `"Raw MSI HID write succeeded."` after a complete write. Keep early rejection, open failure, failed/partial write diagnostics, the return values, and **higher-level native-mode transition/admission/readback/failure logs**. A raw 64/64 transport completion is not PID1902 admission proof.
7. `src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs` — remove only per-invocation `"MSI_ACPI compatibility fallback succeeded"` from the successful helper response branch; preserve response. Keep helper/protocol failure evidence.
8. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawWmiTdpTransport.cs` — remove the routine `LogFallbackSucceeded` DEBUG call/helper on successful `Get_WMI` fallback. If the paired "fallback started" diagnostic has become redundant once the successful-path entry is removed, remove that routine start entry too, **but preserve all real exception, failure-stage, unsupported-response and rejected-payload logs**. No WMI invocation or fallback semantics change.

Prefer **deleting the existing log call**, not adding a cache, `HashSet`, per-message counters, throttler, logger wrapper, or "log once" owner.

### B. Trim ordinary Xbox process-scan negatives but preserve actionable failures

Files:

- `src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs`
- `src/SteamInputAddonforClaw/Xbox/Session/XboxGameProcessIdentityEvaluator.cs`

`ProcessCandidateAsync` inspects ordinary foreground/window process candidates. `NoPackage` is an expected negative; the usual `ConfigNegative` missing-`MicrosoftGame.config` result is also expected. Neither should produce a line per process. However `ProcessImageFailure`, `PackageIdentityFailure`, `ExecutableMismatch`, a positive match, or a substantive config read/parse error should remain diagnosable.

Concrete minimal option for the session-side diagnostic:

```csharp
if (inspection.Match is not { } match)
{
    if (inspection.Disposition is not (
        XboxGameProcessInspectionDisposition.NoPackage or
        XboxGameProcessInspectionDisposition.ConfigNegative))
    {
        AppLog.Debug("XboxSession", "Candidate identity inspection was inconclusive.",
            ("PID", processId),
            ("Disposition", inspection.Disposition),
            ("Reason", inspection.FailureReason));
    }
    return;
}
```

For the evaluator's `LogConfigResolution`, stop emitting it for the **ordinary unremarkable config-file-absent case**. Keep it for a matched config, executable mismatch, and substantive unexpected config resolution/read/malformed/oversized cases. Reuse the existing default missing-config reason (a named constant is fine if helpful), without adding a new classification state machine. For example:

```csharp
const string ConfigSimplyMissing =
    "MicrosoftGame.config was not found in the Package object's Effective/Installed locations.";
var configFailure = ConfigSimplyMissing;
// Existing inspection unchanged.
// ...
if (!string.Equals(configFailure, ConfigSimplyMissing, StringComparison.Ordinal))
    LogConfigResolution(generation, evidence, null, configFailure);
return Negative(XboxGameProcessInspectionDisposition.ConfigNegative, configFailure);
```

The exact final code should account for the currently constructed `ConfigLocationFailure` text so actual failed package/config resolution does not get hidden. **Do not change what constitutes a valid Xbox game.**

In `RetireProcess` remove the DEBUG `"A classified XBOX candidate process exited."` log only for **non-active** candidates. Keep their tracking/disposal, and **retain** the INFO `"Matched XBOX game process exited; active identity cleared."` on the active branch.

Preserve the event-driven Xbox observer, bounded startup/resume reconcile, process generation tracking, exact executable identity proof, accepted active-game INFO, real observation failures, profile selection and Steam-over-Xbox priority. No new process scans or polling.

### C. Retire the unused Gordon-era VIIPER D-pad callback only

Current source:

- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperDiagnosticLog.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs` `TryInitialize()`
- `tests/SteamInputAddonforClaw.Tests/CanonicalViiperDiagnosticLogTests.cs`

This addon-only callback was introduced to forward literal `VIIPER.DPad` messages during the **old Gordon / classic Steam Controller D-pad investigation** (Addon PR #157, 2026-08-15; VIIPER PR #15 not merged). The current Full1902 typed-runtime path passes the callback to `NewUSBServer` even though the active VIIPER implementation does not emit that Gordon-specific message. **Remove this unused forwarding shim and its dedicated tests only after confirming no surviving native producer for the literal prefix or other callback consumer in the current forks.**

Expected typed call site:

```csharp
// CanonicalViiperRuntime.TryInitialize:
if (!native.NewUSBServer(ref config, out serverHandle, logCallback: null))
{
    LogInitFailure("NewUSBServerFailed");
    return null;
}
```

If `logCallback:` is not the exact argument name at the call site, use the existing optional parameter convention (e.g. omit third argument). Keep the typed `ViiperLogCallback` ABI/delegate, generic `ICanonicalViiperNativeApi.NewUSBServer` signature, interop callback rooting for other consumers, and **all Steam Deck, Xbox360, DualSense Edge output / rumble callbacks**. Do not remove the physical `ControllerStateDiagnostics.LogDPadTransitionIfChanged`, `LogPovIfChanged`, or normalized input-change DEBUG logs; those remain usable for present-day PID1902 diagnostics.

Removing the callback does **not** disable the independent native VIIPER file sink (`libVIIPER.log`). Native Go logging level/retention and the proposed separate VIIPER file-level API are **out of scope**. Do not change CTW or `onehoon/VIIPER` in this PR.

## 4. Explicitly retained diagnostics and level policy

**INFO** must still let a developer reconstruct important user-visible and safety-relevant transitions, even without DEBUG: real controller admission, change/loss of physical ownership, active presentation attach/detach/switch, game activation/exit, resume outcome, Center M authority change, HidHide state commit/rollback, PID1901↔PID1902 mode result, rumble arm/disarm or physical STOP result, VIIPER native teardown/cleanup outcome, shutdown and user-invoked mutation outcome.

**WARN / ERROR** must still report failed/partial USB HID writes, failed driver/policy verification, native detach uncertainty, failed recovery, PnP/device loss, unrecoverable hardware/software operations and startup failures at the severity appropriate to their actual impact. **Do not systematically promote existing low-level DEBUG failures in a log-cleanup-only PR**; preserve their existing level unless code review reveals a clearly user-impacting error currently hidden.

**DEBUG**, where still useful:

- physical DirectInput POV, D-pad and changed-input diagnostic evidence;
- meaningful WING/Game Bar blocking decisions, M1/M2 mapping changes, unexpected native callback or app behavior;
- actual 125/250 Hz cadence **changes** (not extra per-tick logging);
- publisher performance heartbeat and measured timing behind PR #803's DEBUG gate;
- configuration/driver failures and profile mutation/reconcile evidence;
- non-repetitive UI/Overlay timings and abnormal setup paths.

**Do not broaden this PR** to remove multiple Overlay capture stages or rumble ownership records merely because each appears repeatedly during normal Show/Hide. The transition chain is needed to reconstruct teardown and neutral handoff. Likewise preserve startup failure investigation logs and the parked `DiagnosticSession`/`DiagnosticSessionTracker` sources/tests: the Full1902 Cleanup I decision intentionally disconnected them from production but did not authorize their deletion.

## 5. Implementation and verification contract

- **One focused PR**, with the repeated-log deletions and obsolete Gordon shim in separate commits if convenient. Keep diff reviewable; no logging-framework refactor, new preference, new settings persistence, new IPC or process launch changes.
- Inspect current code and run the existing build and unit/UI tests; local Codex reports code/test results in the PR. At minimum:
  - `dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore`
  - `dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-restore`
  - `dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore`
  - `git diff --check`
- Update any tests that hard-code deleted message strings. Use focused fake/contract tests where a meaningful invariant exists: Steam registry source still returns current value and handles read errors; Xbox negative candidates do not become matches while a real matched process still activates/retires; native initialization passes a null *diagnostic* callback but still initializes three typed devices, and failure unwind remains intact. Do not add dozens of brittle text-only tests for removed DEBUG lines.
- Retain the PR #803 Off/Info/Debug process-preference and FSE Home semantics. Do not add or remove disk files because of the preference; existing historical logs may remain.
- **Hardware/manual validation belongs solely to the user after merge.** Do **not** require physical MSI hardware, Steam BPM, real VIIPER/USBIP, Sleep/Resume, or a user-run device test for Codex acceptance or as a PR review blocker. A correct build, relevant deterministic tests, and source/lifecycle review suffice.

### Suggested minimal regression checks

1. Xbox fixture: ordinary `NoPackage` and missing-config candidates remain rejected without per-candidate logs. Malformed/denied config remains diagnosable. Positive game identity/exit and tracked-process disposal are unchanged.
2. Steam fixture: notification/watch/resume path and direct `GetRunningAppId()` behavior unchanged; no DEBUG successful-registry-read log.
3. Center M, HidHide, NativeMode and ProfileStore fixtures: unchanged outcomes, failures and readback values after log deletion.
4. VIIPER fake: `NewUSBServer` still receives the same config and creates three devices; only the obsolete diagnostic callback argument is now null. No special native callback root is removed from the interop API.
5. PR #803 log-level tests and UI/Overlay/FSE threshold tests stay green.

## 6. Acceptance / review

- [ ] Confirmed baseline is PR #803's merged `main` or newer; no revert of its logging threshold or diagnostic-level changes.
- [ ] Targeted noisy DEBUG messages are deleted rather than throttled or hidden behind a new state owner.
- [ ] Xbox ordinary negative/process-exit logs are removed; true recognition, unusual identity failures, matched exit and cleanup behavior remain.
- [ ] Steam remains **event-driven**, with direct current-value read and Resume policy unchanged; only read-success log disappears.
- [ ] Typed VIIPER init/teardown and all current virtual controllers operate on the same code paths. Only unused Gordon `VIIPER.DPad` managed forwarding is retired.
- [ ] Real ownership, fail-close, recovery and teardown outcomes remain reconstructable at appropriate severities.
- [ ] All existing relevant automated tests/build succeed; no physical-device prerequisite or gate.
- [ ] No CTW integration, VIIPER Go logger changes, feature refactor, performance "optimization", new synchronization/epoch/state machine, or stale-log deletion.

**Review emphasis:** focus on realistically reachable regression from a changed branch/side effect, native callback semantics, or lost evidence for actionable failures. Do not block for theoretical logger timing interleavings or speculative future use cases.
