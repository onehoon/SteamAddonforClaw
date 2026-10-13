# Work Order — Retire Developer PID1902 Input Cadence Diagnostic (Including UI Card)

**Date:** 2026-10-13  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Delivery:** One focused implementation PR  
**Current source reviewed:** `main`, commit `a0676fe03fa90d616d80ee49c277beedc6686e31` (re-read latest `main` before making changes)  
**Frontend protocol at review:** `78` (advance once from the actual current value)  
**Hardware verification:** User's role after merge; **no physical-device validation is required for Codex, CI, PR review, or merge**.

## 1. Decision and exact scope

Completely remove **only** the Developer Menu feature named **`PID1902 Input Cadence`**.

This explicitly includes its entire Developer Menu **SettingsCard**, the **`Run 10s Test`** button, progress/status/result rendering, diagnostic-only state/collector/timer behavior, host/frontend entrypoints, named-pipe RPC, typed result, and diagnostic-specific tests.

This is **not** a request to remove, slow down, or rewrite the production Full1902 controller input cadence. The separate temporary **1 ms diagnostic oversampling mode** is retired; normal 250 Hz (4 ms) and adaptive idle 125 Hz (8 ms) polling must remain.

**Do not remove or modify any other Developer Menu feature.** In particular, keep Virtual Touchpad (Experimental), Environment Discovery, Vibration Test, Gyro / Sensor Test, Fan Hardware Probe, Battery Charge Limit Test, GameInput System Button Probe, the Log Folder button, and the Developer Menu entry/navigation.

No CTW integration. The product is standalone Full1902.

## 2. Architectural authority / references

Read the current code and these documents, in the authority order stated by the Full1902 README:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Diagnostic-specific historical references for understanding what is being retired (do **not** delete historical work orders):

- `docs/work-order/FULL1902_PID1902_CADENCE_DIAGNOSTIC_HIGH_RES_TIMER_FIX_WORK_ORDER.md`
- `docs/work-order/FULL1902_END_TO_END_250HZ_CONTROLLER_CADENCE_WORK_ORDER.md`
- `docs/work-order/FULL1902_ADAPTIVE_IDLE_125HZ_CONTROLLER_CADENCE_WORK_ORDER_2026-10-10.md`

Retain current authority and safety contracts: one real owned PID1902 DirectInput source, exact identity, initial valid input, state publication, production high-resolution polling, PnP loss/recovery, suspend/resume, HidHide ownership, VIIPER ownership/teardown, fail-close, and PID1901/PID1902 policies. Nothing in this cleanup changes controller authority or virtual presentation.

## 3. Source-backed current dependency closure

The following is verified in the reviewed `main`; update this inventory if recent commits changed the call graph.

### UI

- `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml`
  - Whole `<ctcontrols:SettingsCard Header="PID1902 Input Cadence" ...>` block.
  - `RunPid1902InputCadenceButton`, `Run 10s Test`, `Pid1902InputCadenceStatusText`.
- `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs`
  - `_isRunningPid1902InputCadenceDiagnostic`.
  - `RunPid1902InputCadenceButton_Click`, `SetPid1902InputCadenceStatus`, `FormatPid1902InputCadenceResult` and now-unused formatting helpers `FormatHz` / `FormatMilliseconds` if they have no other callers.

Delete the **card itself**, not just its contents, visibility, or enabled state. Preserve surrounding card order/layout and unrelated handlers.

### Typed frontend contracts

- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
  - Delete `FrontendPid1902InputCadenceOutcome`.
  - Delete `FrontendPid1902InputCadenceResult`, including its `Unavailable()` factory.
  - Delete the `IAddonFrontendControl.RunPid1902InputCadenceDiagnosticAsync` method/default implementation.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs`
  - Delete only the `IMsiClawPreparedInputSource.RunPid1902InputCadenceDiagnosticAsync` method/default.
  - Keep the production input interface, session/lifecycle status and physical identity contracts.

### Frontend control and Runtime host

- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
  - Delete the `_runPid1902InputCadenceDiagnostic` delegate field, optional constructor parameter, assignment and public request handler.
  - Do **not** delete the shared exclusive-controller-operation gate if any other retained feature uses it.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
  - Remove `runPid1902InputCadenceDiagnostic: RunPid1902InputCadenceDiagnosticAsync` from frontend control composition.
  - Delete the host-only `RunPid1902InputCadenceDiagnosticAsync` delegation/unavailable method.
  - Preserve physical ownership, first-acquisition/recovery and presentation composition.

### Named-pipe wire

- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
  - Delete `FrontendRpcMethod.RunPid1902InputCadenceDiagnostic` from the active enum.
  - Preserve existing historic version comments, including version 44 (history, not a live feature).
  - Bump `FrontendTransportProtocol.CurrentVersion` **once** from the current latest version (78 -> 79 only if still 78); add a short version comment explaining retirement of the typed Developer cadence RPC/result.
  - This is pre-release: no compatibility shim, reserved unused RPC handler, or alternate API merely for the deleted feature.
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
  - Delete the typed cadence request method.
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
  - Delete the cadence member from the no-payload validation list.
  - Delete its request dispatch branch.
  - Keep request cancellation, handshake enforcement, and other RPC dispatch behavior unchanged.

### Physical DirectInput source — remove diagnostic without breaking production

- **Delete file** `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputCadenceDiagnostic.cs` (the diagnostic-only `MsiClawInputCadenceCollector`).
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`:
  - Remove `CadenceDiagnosticPollInterval` (1 ms), `CadenceDiagnosticDuration` (10 s), `_cadenceDiagnosticTimerFactory`, the diagnostic methods, `EmptyCadenceResult`, `GetCadenceDiagnostic`, `ClearCadenceDiagnostic`, and `InputSession.CadenceDiagnostic`.
  - Remove `ResolvePollInterval(bool cadenceDiagnosticActive)` if now unused. **Do not remove `ResolveProductionPeriod(bool requires250Hz)`.**
  - Remove diagnostic observing, active-mode flags, temporary diagnostic timer wait/return-to-production branch, diagnostic cancellation/failure notifications and diagnostic-only logging.
  - Simplify `WaitForNextPoll` to use only the **existing production timer and deadline scheduling**. Preserve its initial pre-armed deadline behavior, current 4 ms/8 ms period selection, adaptive period transitions, cancellation, and physical polling scheduler failure classification.
  - Constructor simplification: remove the diagnostic timer factory parameter, but preserve the existing production timer injection, timestamp and 250 Hz requirement seams used by production-focused tests. Update their call sites as necessary; do not create a replacement wrapper/manager.
  - Keep `WindowsHighResolutionOneShotTimer` and `CanonicalPublisherDeadlineMath`: they are **production dependencies**.
  - Keep the useful one-shot DEBUG **production** input-cadence summary (`ProductionCadenceSummaryWindow`, production read count/observed Hz/target period). Remove only its obsolete suppression/check against an active Developer cadence test, so it measures ordinary production reads consistently.
  - Preserve DirectInput `ReadState`, state validation/mapping, M1/M2, state publication, first valid state completion, `SessionCompleted` and cleanup. Existing production log text containing the generic word "diagnostic" is not by itself grounds for wider logging/refactor changes.

The key implementation trap is that **the Developer 1 ms test shares the same input worker with real controller input**. Do not remove the actual worker/timer, remove its `ReadState` loop or replace the live input pipeline with a separate reader.

## 4. Tests / cleanup of expectations

Remove tests exclusively verifying the retired 1 ms diagnostic. Preserve production coverage even where an existing test file mixes production and diagnostic assertions.

Required examination:

- `tests/SteamInputAddonforClaw.Tests/MsiClawInputCadenceDiagnosticTests.cs`
  - The collector statistics/duplicate/median/P95, cancellation, timer error, round-trip DTO and 1 ms diagnostic tests are obsolete.
  - This file also asserts **production 4 ms and 8 ms periods**. Preserve equivalent production assertions in `MsiClawInputSourceTests.cs` or another **existing** production test file before deleting the diagnostic test file.
  - The file's diagnostic fake-timer classes are local. Confirm production test fixtures in `MsiClawInputSourceTests.cs` remain intact; they independently cover production timer creation/arming/failure.
- `tests/SteamInputAddonforClaw.Tests/MsiClawInputSourceTests.cs`
  - Keep/adjust existing production poll scheduler, timer failure, session cleanup, adaptive cadence, and physical input tests for the constructor signature change.
  - Do not weaken realistic lifecycle/error regression coverage to make the cleanup pass.
- `tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs`
  - Remove only cadence round-trip, wrong-payload, cancellation tests and the corresponding `RecordingFrontendControl` members/method.
  - Keep unrelated transport tests and update protocol-version expectations consistently with the new current version.
  - Add/adjust a concise assertion that the retired `RunPid1902InputCadenceDiagnostic` enum member is absent, if useful within an existing protocol cleanup test.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`
  - `Developer_cards_have_unique_gyro_icon_and_requested_order` currently requires the cadence card between Environment Discovery and Vibration Test and checks `Run 10s Test`. Remove those obsolete expectations; retain the surrounding card-order and unrelated Developer Menu checks.
  - `Developer_menu_removes_M1_M2_tools_and_preserves_unrelated_diagnostics` currently expects `Header="PID1902 Input Cadence"`. Change it to expect its absence while still preserving unrelated diagnostics.
- `tests/SteamInputAddonforClaw.UiTests/SettingsPageLoggingTests.cs`
  - Remove the cadence card from the required Developer diagnostics collection, and optionally add a precise negative assertion confirming the card is absent.
- Search all current UI/contract/Runtime/test references and remove stale compile-time dependents. Do not delete unrelated tests simply because they reference the Developer page or generic controller cadence.

Keep historical `docs/work-order/*` and architecture/research documents as an implementation record; they are not active code, and this PR must not rewrite the past.

## 5. Scope exclusions and change policy

This is a **deletion-only feature retirement with the minimal production-loop simplification needed after removal**.

Do not change:

- production PID1902 250 Hz / 125 Hz adaptive cadence, high-resolution timer/deadline semantics, thread priority, first-input processing or raw button/axis mappings;
- SteamDeck / Xbox360 / DualSense Edge output publication or gyro scheduling;
- Center M authority, PID1901/PID1902 mode operations, HidHide, VIIPER, recovery, suspend/resume, crash/restart/shutdown behavior;
- joystick LED, rumble/feedback, battery, fan, WING or overlay behavior;
- developer logging level, other developer tools or settings persistence.

No new manager, authority, state machine, lock, timer abstraction, historical compatibility layer, or "generic diagnostics framework" to replace this single removed tool. Prefer the simplest existing production path.

## 6. Automated validation / acceptance

Local Codex should run available code-only tests/builds; physical testing belongs to the user after merge.

Acceptance criteria:

1. The entire `PID1902 Input Cadence` SettingsCard and `Run 10s Test` UI are gone; Developer Menu and all other cards remain.
2. No executable source uses the retired cadence DTO, entrypoint, 1 ms collector, per-session diagnostic flag/timer, or frontend RPC; historical docs may still mention them.
3. The client and server agree on the once-bumped frontend protocol version. No dead RPC method, request path, or obsolete test double remains.
4. The production DirectInput owner still has exactly one existing physical polling worker and a production high-resolution timer, with the same nominal 4 ms game/active and 8 ms idle periods, initial arm, adaptive transition and stop/cleanup behavior.
5. Existing physical input loss and scheduler failures retain their normal fail-closed recovery/lifecycle semantics; no diagnostic-specific mode or timer is required to operate the controller.
6. One-shot DEBUG production cadence summary remains meaningful for ordinary reads.
7. Relevant unit/UI/transport tests pass, and the solution builds in CI.
8. No physical MSI Claw, manual interaction, Sleep/Resume, game session or other on-device validation is required as a PR/merge blocker.

Suggested search terms after implementation: `RunPid1902InputCadence`, `FrontendPid1902InputCadence`, `MsiClawInputCadenceCollector`, `CadenceDiagnosticPollInterval`, `CadenceDiagnostic`, `PID1902 Input Cadence`, and `Run 10s Test`. Search **active source and tests** first; matches in historical work orders are expected.

**Deliverable:** One small PR implementing only this feature retirement, without changing behavior of the remaining Developer tools or the normal Full1902 controller lifecycle.
