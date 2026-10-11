# Work Order — A2VM Boot Acquisition and Windows Session-End PID1901 Simplification (One PR, Two Commits)

Date: 2026-10-11
Target: `onehoon/SteamAddonforClaw`, Full PID1902 standalone application
Implementation owner: local Codex
PR shape: **one pull request with exactly two logical implementation commits**. Keep each commit individually compilable and include its focused tests/documentation adjustments in that commit.

## 0. Decision and purpose

Fix the two production-reachable A2VM 7/8 repeated-reboot reliability issues evidenced by the October 11 logs **without building another lifecycle state machine or loosening actual physical/virtual-controller safety boundaries**:

1. **Commit 1 — A2VM boot: simplify final PID1902 readiness / correct late-arrival handling.**
2. **Commit 2 — Windows session end: prioritize the native VIIPER detach/close window and shorten the safe PID1901 preparation sequence.**

Do not split into two PRs. Do not mix the changes into one implementation commit. The PR review must be able to inspect and, if necessary, revert each logical change independently.

Do not implement CTW integration. HHC and CTW-fork code are **comparative references only**, not additional owners or required dependencies. One Windows user and one interactive session are supported; no RDP / Fast User Switching / multi-session work.

### Required authority documents

Read the documents in `docs/Full 1902 Implementation/` before editing, following the precedence in its `README.md`:

- `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- The active A2VM exception paragraphs in `docs/Full 1902 Implementation/README.md`
- `docs/work-order/2026-10-11_A2VM_INITIAL_CONTROLLER_FAST_PATH_HHC_CTW_SIMPLIFICATION_WORK_ORDER.md`
- `docs/work-order/2026-10-11_A2VM_V01357_WINDOWS_SESSION_END_PID1901_P0_P1_REPAIR_WORK_ORDER.md`

The current one-boot rumble marker, strong ownership, Center M Disabled authority, durable PID1902/HidHide policy, and VIIPER canonical ownership remain intact. Keep Windows shutdown best-effort and non-vetoing.

## 1. Evidence and comparison

Google Drive logs (A2VM 8, October 11, 2026):

- `Addon/Log/AV2M/1011-2` — Steam BPM autostart ON: 4/6 Windows boots acquired a virtual controller; 2/6 failed.
- `Addon/Log/AV2M/1011-3-No BPM` — Steam BPM autostart OFF: 4/5 Windows boots succeeded; 1/5 failed.
- In both groups the late DirectInput-ready failures occurred with the final PID1902 write near the edge of the current 12-second final window:
  - BPM ON, 14:55: first valid input after approximately 12.26 seconds; `FinalReadinessDeadlineExpiredBeforeHidHide`.
  - BPM OFF, 15:15: first valid input after approximately 11.81 seconds; `FinalReadinessDeadlineExpiredBeforeHidHide`.
  - The recorded result had `FirstValidState=True`, `FinalPid1901Absent=True`, `HidHideCompliant=False` because the HidHide operation had **not yet been attempted**. This is not evidence of an actual HidHide readback mismatch.
  - Another BPM-ON boot, 14:47, ended `FinalReadinessDeadlineExpired` before the first valid input.
  - These timeout exits currently report `InitialAcquisitionRetryReason=None`; existing deferred acquisition therefore is not entered.
- Windows end, BPM OFF: of four eligible shutdowns, one issued the PID1901 command and three aborted because Xbox360 VIIPER detach was `UnsafeOutcomeUnknown`.
- `libVIIPER.log` shows the USB/IP client connection disappearing **before** the failed detach attempt in those cases. In the 15:14 and 15:21 samples, physical rumble STOP/endpoint resolution between feedback drain and detach consumed approximately hundreds of milliseconds. In the 15:12 sample transport loss preceded the successful shutdown-admission continuation, so no ordering change can guarantee success in every shutdown.
- BPM ON adds a separate, real pre-ownership `BigPictureChanged` / stale `PresentationReconcileFailed` issue. **That P2 defect is out of scope for this two-commit PR**; do not silently fold it into shutdown simplification.

Comparative source evidence:

- HHC: `Valkirie/HandheldCompanion/HandheldCompanion/Devices/MSI/ClawA1M.cs`: simple mode HID write, fixed settle, controller connection and input managed separately.
- CTW fork: `onehoon/ClawTweaks-Dev`, `release/v0.3.98.0`, `XboxGamingBarHelper/Labs/ClawButtonMonitor.cs`: mode write, HID arrival polling, DirectInput acquire, later retry via monitor loop; its Stop path retires VIIPER before physical XInput restoration.
- CTW's `Doku/DIAG_HidHide_BootRace_DoubleInput.md` documents a real double-input fault caused by starting virtual output before the physical PID1902 hide target had become compliant. **Do not copy that unsafe shortcut.**

## 2. Commit 1 — simplify A2VM initial PID1902 readiness

Suggested commit title:

`fix(a2vm): accept late valid DirectInput and defer genuine boot enumeration delays`

### Relevant code

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `AcquireA2vmInitialFastFinalOwnershipAsync`
  - `CompleteA2vmInitialFastFailure`
  - existing typed `MsiClawInitialAcquisitionRetryReason`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
  - `CanKeepInitialControllerAcquisitionDeferred`
  - `RunInitialControllerAcquisitionAsync`
  - existing `RequestOwnedControllerRecovery` / Device Arrival watcher
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`
  - `StopAndConfirmCleanupAsync`
- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs` and any focused host tests

### Implementation contract

1. **Separate the readiness wait limit from final committed safety proofs.** Continue to use the existing 12-second bounded wait for the normal PID1902 PnP/DirectInput first-valid-input arrival. Once the *correct* live PID1902 DirectInput source has yielded a valid first state, allow the immediately following exact PID1901-absence / PnP target / HidHide application-and-readback stage to complete. Do not reject a valid, safely obtained input solely because the same arrival clock has just expired during this final verification. Keep cancellation and existing bounded underlying operations; do not replace them with unbounded looping or a second arbitrary retry budget.
2. **Keep mandatory checks** before publishing ownership or a virtual controller: Center M remains exactly Disabled; PID1901 does not coexist at final admission; one exact current `VID_0DB0&PID_1902&MI_00&COL01` primary input target and matching DirectInput identity; valid first input; correct current HidHide targets and compliant readback; supported model and VIIPER readiness. Ambiguity, changed device identity, incompatible usage, real HidHide failure, unexpected exceptions, unsafe native state, and unproven native/DirectInput cleanup remain fail-closed.
3. **Classify genuine late normal enumeration separately from defects.** When the initial final readiness budget expires because the PID1902 descriptor/input has not yet arrived (or a valid first sample is simply pending), use the *existing typed* initial-acquisition deferral only when the result is identifiable as normal late PnP/input readiness and any started DirectInput source was safely stopped with confirmed cleanup. Reuse `Pid1902TargetPidNotPresent` where semantically appropriate; add a narrowly scoped enum reason only if that existing reason would be misleading for an already-enumerated but not-yet-readable device. Avoid generic retry for failed mode writes, unsupported/mismatched endpoints, real input failures, HidHide errors, or cleanup uncertainty.
4. **Do not swallow cleanup failure and then retry.** `SafeStopAsync()` currently logs exceptions but provides no cleanup proof; use the existing `StopAndConfirmCleanupAsync` where an actually started source will be deferred. If cleanup is unproven, remain fail-closed and prevent Device Arrival from reopening a second native session. Use existing owner/recovery fields; do not add a manager/epoch/secondary owner.
5. **Do not repeat boot-rumble mode writes** while deferred. The one-boot marker stays consumed. The existing host immediate-recheck and future Device Arrival paths are the only follow-ups. No spin loop, additional timer, unconditional native XInput detour, or eager publisher attach.
6. Avoid redundant snapshots where the same current PnP data is already available, but retain the **one fresh final device/HidHide-target validation** necessary to bind the published input to the actual currently hidden collection. Optimize only demonstrably redundant queries; do not discard final identity checks.
7. Update the A2VM exception contract in the authoritative Full1902 docs as needed: clarify what the 12-second ceiling bounds, what happens to an in-progress safe final proof, and when late enumeration is deferred. Do not accidentally change EX/CG3EM, stock/enabled onboarding, ordinary restart/recovery, or sleep/resume behavior.

### Focused tests (Commit 1)

- Valid first state arrives at ~11.8 seconds and final exact HidHide readback completes just across the 12-second boundary -> ownership/presentation-eligibility succeeds (no false timeout).
- First valid state or required PID1902 collection still absent at deadline -> typed deferred result; later existing immediate recheck / Device Arrival can succeed **without additional native mode writes**.
- Deadline while a DirectInput session was started -> proven stop/cleanup before deferral; failed cleanup -> fail-close and no second acquire.
- PID1901 still present, wrong/ambiguous primary target, DirectInput identity mismatch, invalid state, actual HidHide readback mismatch, authority loss, cancellation -> no virtual attach, no inappropriate deferred retry.
- EX/CG3EM and already-PID1902 start paths retain current behavior.

## 3. Commit 2 — shorten safe Windows session-end PID1901 preparation

Suggested commit title:

`fix(a2vm): prioritize safe VIIPER retirement before shutdown PID1901 write`

### Relevant code

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
  - `HandleWindowsSessionEnd`
  - `PrepareForWindowsSessionEndAsync`
  - `GetWindowsSessionEndAdmissionFailure`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`
  - `RetireActivePresentationCoreAsync`
  - `RetireAsync`
  - `TryDisarmFeedbackLocked`, `RequestPhysicalRumbleStopLocked`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
  - `PrepareForWindowsSessionEndAsync`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs`
  - existing canonical typed detach / teardown behavior (do not weaken)
- Existing `AddonProcessHostWindowsSessionEndTests`, `MsiClawAddonPresentationTests`, and physical owner tests

### Implementation contract

1. Keep this a **real `WM_ENDSESSION(TRUE)` only**, best-effort, short, bounded exception. Never issue PID1901 for `WM_QUERYENDSESSION`, `WM_ENDSESSION(FALSE)`, user logoff, app-only exit/restart, updater replacement, sleep/hibernate, resume, or mere BPM close. Do not veto or indefinitely stall Windows shutdown.
2. Keep one canonical output owner, one native VIIPER detach path, one physical owner, and the current global ~2-second Windows-session-end budget; no new service, window, thread-based detach proxy, independent watchdog, authority flag, or second VIIPER instance.
3. **Reorder only this shutdown path** where the code supports it, to minimize the interval in which Windows can tear down USB/IP before native detach:
   - close new presentation/reconcile admission through the existing shutdown gate;
   - stop and join publisher;
   - disarm/drain native feedback callbacks so there can be **no later physical feedback write**;
   - try the existing canonical typed VIIPER Detach promptly, then prove VIIPER `Closed`;
   - while the already-owned PID1902 identity/source is still available, issue the existing bounded physical rumble STOP and confirm it **before** DirectInput cleanup and PID1901 mode mutation;
   - stop and **confirm** DirectInput cleanup;
   - one narrowly scoped PID1902 command HID resolution and at most one XInput mode-write attempt, with no PID1901 PnP wait.
4. The current implementation calls physical rumble STOP before Detach; this can spend ~300–500ms on endpoint resolution while USB/IP is being removed. Preserve rumble-latch safety, but move that STOP to after safe native detach/close **only for Windows session end** if callback-drain, runtime/native ownership and rumble-sink identity guarantees prove this is safe. If code inspection shows the STOP cannot safely run after Detach/Close, retain its existing order and document the evidence rather than weakening rumble safety. In any case, **do not issue PID1901 without a confirmed physical STOP**. If native detach fails, best-effort STOP may still be appropriate when the original physical source is verifiably owned, but the PID1901 command must remain blocked.
5. If typed Detach returns `UnsafeOutcomeUnknown`, `Invalid`, retryable/not detached, or the canonical runtime does not prove `Closed`, fail close. Never equate an already disconnected USB/IP client with proven detach. Do not force a mode write, change native ABI, detach through a second object, or classify an unconfirmed command as unsent.
6. Review redundant preflight reads in `MsiClawAddonPhysicalOwnership.PrepareForWindowsSessionEndAsync`: strong owner/live-source identity should not be repeatedly recomputed for no useful purpose. Keep the final **fresh unique current PID1902 command HID** resolution and physical teardown proofs; skip speculative refactoring of the existing mode writer if it would widen scope. Prefer a small modification over an additional interface or abstraction.
7. Maintain failure diagnostics distinguishing: shutdown admission blocked, publisher/feedback failure, native typed Detach/VIIPER Close failure, physical STOP failure, DirectInput cleanup unproven, no command attempted, mode command attempted/unconfirmed, command written. Preserve existing key event names (`A2vmWindowsSessionEndPid1901Prepare`, `A2vmWindowsSessionEndTypedDetachFailed`, etc.) and add stage timing only where useful for the observed failures.
8. Do not promise that all shutdown attempts succeed. The 15:12 log proves that Windows/USB-IP teardown can precede this handler; fail-close remains a legitimate outcome.

### Focused tests (Commit 2)

- Correct sequence when native device remains available: publisher joined -> feedback disarmed/drained -> typed detach proven -> VIIPER closed -> physical STOP confirmed -> DirectInput cleanup confirmed -> one PID1901 command.
- Feedback or publisher drain failure -> no unsafe detach / no PID write.
- Xbox360 and SteamDeck detach/close `UnsafeOutcomeUnknown` or invalid -> no PID1901 command; no false native success.
- Physical STOP failure, underlying DirectInput cleanup failure, authority/identity mismatch, cancellation, deadline elapsed -> no late PID mode write.
- Successful best-effort PID1901 command is logged as **write-only**, not confirmed PID1901 PnP state.
- Ordinary app exit/restart, installer handoff, sleep/resume, rejected/cancelled Windows end and Center M Enable/Uninstall retain their independent policies.
- Ensure Commit 1 remains green and Windows startup/owned recovery remain unaffected.

## 4. Scope and review criteria

- Two logical implementation commits in **one PR**. If tests/docs are changed, keep those changes with the commit they validate rather than adding a third catch-all implementation commit.
- No GUI redesign, no change to publisher kind selection, no BPM autostart workaround, no new stock authority behavior, no generic controller refactor.
- Do not add new synchronous polling of DirectInput COM during PnP settle; CTW fork documented an actual heap-corruption incident from excessive transient COM enumeration.
- Avoid speculative race hardening, duplicate cleanup state, repeated validations, extra locks/epochs, arbitrary retry loops and pathological interleaving tests.
- Run affected automated tests and build/CI as appropriate. **Hardware/real-device repeated reboot verification is the user's post-merge role; do not require real-device testing from local Codex or treat its absence as a PR blocker.**
- Reviews block only on realistically reachable and materially harmful safety/correctness regressions. Particularly protect HidHide double-input prevention, publisher/feedback neutral/STOP, VIIPER ownership, fail-close/rollback, PID1901↔PID1902 hardware mode boundaries, suspend/resume, crash/restart, and actual PnP loss.

## 5. Expected observable outcomes for later user testing

The user's subsequent BPM ON and BPM OFF repeated-reboot logs should distinguish:

- `A2vmInitialPid1902InputReady` near/after the 12-second arrival budget, followed by successful exact HidHide proof rather than false timeout;
- genuine late PID1902 readiness reported as deferred, cleanly recovered on existing owner wake-ups with no extra mode writes;
- `A2vmWindowsSessionEndPublisherStopped`, feedback callback drain, native typed detach and `A2vmWindowsSessionEndViiperClosed` appearing earlier relative to `WM_ENDSESSION`, with physical STOP and DirectInput cleanup before `A2vmWindowsSessionEndModeCommandDispatch`;
- separate counts for `CommandWritten`, `PresentationRetirementUnproven`, `PhysicalRumbleStopUnconfirmed`, `DeadlineExceeded`, and stale BPM Reconcile failures (the latter remains out of scope).

A log message or unit test is **not** a substitute for eventual user hardware confirmation; CI/code quality is the merge gate, followed by the user's real-device verification.
