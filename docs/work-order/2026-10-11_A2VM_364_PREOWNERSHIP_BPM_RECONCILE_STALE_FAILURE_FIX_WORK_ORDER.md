# Work Order — Fix Stale Pre-Ownership BPM Reconcile Failure Blocking A2VM Windows Session-End PID1901 Preparation

**Date:** 2026-10-11  
**Target repository:** `onehoon/SteamAddonforClaw`, latest `main`  
**Field build:** `0.1.364.0`, MSI Claw A2VM 8  
**Priority:** P1 — repeated, reachable Windows shutdown correctness  
**Implementation owner:** local Codex  
**Vehicle:** **one narrowly scoped PR** (host scheduling guard + focused automated tests; no hardware gate).  
**App model:** standalone Full PID1902. No CTW integration.

## 1. Goal and exact decision

Fix a verified startup-to-shutdown state mismatch:

```text
BPM auto-start / BigPictureChanged
  -> RequestControllerPresentationReconcile("BigPictureChanged")
  -> _presentationOwnership and _physicalOwnership exist,
     but physical.LiveInputSource is still null (PID1902 acquisition in progress)
  -> ReconcileControllerPresentationAsync returns false ("no live PR5 input source")
  -> _presentationReconcile retains a completed false task
  -> first physical PID1902 ownership and HidHide proofs complete
  -> normal initial presentation attach succeeds (DualSenseEdge)
  -> genuine Windows WM_ENDSESSION(TRUE)
  -> GetWindowsSessionEndAdmissionFailure rejects prior false reconcile
  -> "PresentationReconcileFailed"; no VIIPER retirement, STOP or PID1901 command
```

**Recommended correction: stop scheduling a pre-ownership reconcile that cannot do useful work.** In the existing `RequestControllerPresentationReconcile` admission, when there is **no committed live DirectInput source and no active presentation**, do **not** create/publish a reconcile task merely in response to a BPM / Steam / preference change. The normal first-attach path already reads the *fresh* Steam/BPM snapshot after physical PID1902 and HidHide proofs complete.

**Do not** globally reinterpret a real failed, canceled, faulted, or still-pending reconcile as success at Windows shutdown. Existing termination gates for genuine unsafe/incomplete presentation transitions must remain intact.

This issue was explicitly deferred from the two-commit PR #752; it is a separate fix, not another round of boot/re-enumeration or rumble work.

## 2. Required authority and source reading

Read the source-of-truth docs in `docs/Full 1902 Implementation/` following the authority order in its `README.md`:

1. `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Also refer to:

- `docs/work-order/2026-10-11_A2VM_BOOT_ACQUISITION_AND_SESSION_END_SIMPLIFICATION_TWO_COMMIT_WORK_ORDER.md` (PR #752; expressly left pre-ownership BPM reconcile out of scope);
- `docs/work-order/2026-10-11_A2VM_361_SHUTDOWN_PHYSICAL_RUMBLE_STOP_FAST_PATH_WORK_ORDER.md` (PR #757; already accelerated the STOP path and **must not regress**);
- `docs/work-order/2026-10-11_A2VM_V01357_WINDOWS_SESSION_END_PID1901_P0_P1_REPAIR_WORK_ORDER.md` (WM_ENDSESSION-only fail-close and 2-second deadline);
- the active DualSense Edge presentation architecture, where a BPM Steam-facing presentation may correctly be `DualSenseEdge` instead of `SteamDeck`.

Review these **current code paths together** before implementation:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
  - `RequestControllerPresentationReconcile`
  - `ReconcileControllerPresentationAsync`
  - `RunInitialControllerAcquisitionAsync` and deferred initial attach / recheck
  - `HandleWindowsSessionEnd`, `PrepareForWindowsSessionEndAsync`
  - `GetWindowsSessionEndAdmissionFailure`, `GetWindowsSessionEndPostReconcileFailure`
  - `TryBeginProcessShutdownCore` and `_presentationReconcile`
  - `OnBigPictureStateChanged` and `OnPowerResumeObserved`.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`: `LiveInputSource` is published only after verified PID1902 input and HidHide ownership.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`: `ActivePresentation`, `IsActivePresentationLive`, `AttachInitialPresentationAsync`, `ReconcileDesiredPresentationAsync`, and the current single-owner VIIPER retirement path.
- `tests/SteamInputAddonforClaw.Tests/AddonProcessHostWindowsSessionEndTests.cs`
- `tests/SteamInputAddonforClaw.Tests/ControllerBadgeLifecycleNotificationTests.cs`
- existing initial-ownership, presentation, resume/recovery, and native message tests relevant to changed behavior.

Current production facts: `_presentationReconcile` begins as `Task.FromResult(true)`. A BPM change calls `RequestControllerPresentationReconcile`, which currently schedules `Task.Run(() => ReconcileControllerPresentationAsync(...))` as soon as both *owner objects* exist, even while the physical source has not yet committed. When the source is null, `ReconcileControllerPresentationAsync` logs `Runtime presentation reconcile skipped; no live PR5 input source.` and **returns false**; this completed result remains in `_presentationReconcile` after independent successful initial attach.

`GetWindowsSessionEndAdmissionFailure` currently **intentionally rejects completed false / faulted / canceled reconciles**. A second check after closing shutdown admission drains an in-flight reconcile and confirms owners and publisher state. These protections exist for realistic native teardown/rollback safety and must not be removed wholesale.

## 3. Exact 0.1.364 field evidence

Drive folder: [`Addon/Log/AV2M/1011-5`](https://drive.google.com/drive/folders/1-gQM0CmZ25RhY-huz-vxn2KC3R2Wa1-e)

All six Runtime files in this folder reported `Version=0.1.364.0` in the application launch header. All six ultimately acquired PID1902 ownership and attached a virtual controller; one acquisition safely deferred then succeeded.

Two *genuine* Windows shutdown attempts reproduced this exact issue:

| Runtime | Reconcile before physical ownership | Subsequent verified live first attach | Windows end result |
| --- | --- | --- | --- |
| 21:42:03, P9048 | 21:42:14.594 `BigPictureChanged` requested; 21:42:14.606 `no live PR5 input source` | 21:42:28.722 `PhysicalOwnershipVerified`, 21:42:29.054 `InitialPresentationAttached Presentation=DualSenseEdge` | 21:42:51.678 `Outcome=Skipped Reason=PresentationReconcileFailed ElapsedMs=332` |
| 21:44:16, P9108 | 21:44:28.182 `BigPictureChanged` requested; 21:44:28.199 `no live PR5 input source` | 21:44:43.730 `PhysicalOwnershipVerified`, 21:44:43.927 `InitialPresentationAttached Presentation=DualSenseEdge` | 21:45:36.548 `Outcome=Skipped Reason=PresentationReconcileFailed ElapsedMs=265` |

- [P9048 log](https://drive.google.com/file/d/1fQyZm7yroTPk5Gt21j-GzKpvTRXlgq_H/view)
- [P9108 log](https://drive.google.com/file/d/12BHfGOsUS0aaS54YVPi8YShSFQ67GDo6/view)

In both cases, the startup fast mode cycle completed normally and the initial DualSense Edge publisher was live, but Windows session-end did **not** even reach `A2vmWindowsSessionEndAdmissionCompleted`, `A2vmWindowsSessionEndTypedDetachCompleted`, `A2vmWindowsSessionEndPhysicalStopCompleted` or `A2vmWindowsSessionEndModeCommandDispatch`. The preexisting *failed task result*, rather than the present healthy physical/presentation state, caused admission to skip.

Useful controls from the same folder:

- 21:37, P11708: Xbox360 startup completed, genuine session-end reached successful typed detach, VIIPER `Closed`, and physical STOP in **29 ms**. DirectInput cleanup confirmed, but the file ends before a final PID1901 outcome (do not call it successful).
- 21:47, P9072: first input did not arrive in the first readiness window; the existing `ImmediateRecheck` deferred path succeeded without repeating native boot-cycle writes; later BPM exit correctly switched DualSense Edge -> Xbox360, and an ordinary app restart fully closed VIIPER. Do not change this recovery.
- 21:48, P4620: ordinary restart acquired the already-owned PID1902 without an extra prime. No Windows session-end event recorded.
- 21:39, P10516: 1902->1901->1902 prime, exact HidHide, DualSense Edge, and BPM-exit switch succeeded, but **no** final Windows session-end record is present. Do not count it as shutdown success.

Native `libVIIPER.log` confirms success for the 21:37 typed detach and the 21:47 app-only restart; the two failure cases above never got as far as native detach, so don't diagnose their failure as a USB/IP transport problem.

## 4. Implementation — one existing scheduler guard, no new authority

**Preferred minimal change** in `AddonProcessHost.RequestControllerPresentationReconcile`, under its **existing** `_controllerOwnershipOperationSync` lock:

```csharp
else if (_presentationOwnership is { } presentation
    && _physicalOwnership is { } physical
    && (physical.LiveInputSource is not null
        || presentation.ActivePresentation is not null))
{
    previousBadge = CaptureCurrentControllerBadge();
    reconcile = Task.Run(() => ReconcileControllerPresentationAsync(
        presentation,
        physical,
        trigger,
        _startupCancellationTokenSource.Token));
    _presentationReconcile = reconcile;
}
```

The extra condition prevents creating a misleading completed-false reconcile when **neither a committed physical source nor an active virtual presentation exists**. For startup `BigPictureChanged`, this is a legitimate no-op: once `RunInitialControllerAcquisitionAsync` or its deferred continuation safely owns PID1902, the independent *existing* initial-attach path reads the current `SteamPresentationSnapshot` and chooses the desired `Xbox360`, `SteamDeck`, or `DualSenseEdge`. Nothing is queued or replayed, and no extra task/flag/timer is needed.

**Keep scheduling normal reconciliation** whenever:

- a committed input source exists (including actual live BPM enter/exit and preference changes);
- an active presentation still exists but the source is unavailable/lost; preserve the normal fail-close/recovery behavior rather than silently declaring this to be a no-op.

Do not blindly change the no-source return value in `ReconcileControllerPresentationAsync` to `true` (that would make a genuinely lost input source appear to have reconciled successfully). Do not reset `_presentationReconcile` to `Task.FromResult(true)` in the shutdown handler or after initial attach to hide arbitrary previous faults. Do not turn all completed-false reconcile outcomes into accepted shutdown admission.

If the exact source-code shape on the PR branch has changed, preserve the same limited **semantic guard**: *no running/published physical ownership yet AND no attached presentation -> do not publish a reconcile task*. Keep the existing owner gate and bounded shutdown checks.

**Observability:** optionally emit one lightweight **DEBUG** `PresentationReconcileDeferredUntilInitialAttach` event with the original trigger when this early no-owner skip occurs. Logging is not necessary if the initial-attach and physical-ownership events already make the path clear; avoid noisy repeated INFO/WARN. Do not add a new startup queue, need-to-reconcile flag, or auto retry loop.

## 5. Safety/functional contracts not to relax

Preserve the unchanged `PrepareForWindowsSessionEndAsync` protocol:

```text
Only genuine Windows WM_ENDSESSION(TRUE); not logoff:
  -> current A2VM model + exactly Disabled Center M authority
  -> no other physical-controller lifecycle/recovery/re-arm operation pending
  -> seal process shutdown admission
  -> drain the single genuinely in-flight presentation reconcile within the same 2s budget
  -> prove same live PID1902 source, same strong identity, exact hidden target
  -> prove VIIPER presentation still live, not suspend/overlay paused
  -> canonical publisher join / callback drain
  -> typed detach result successful; VIIPER Closed
  -> physical PID1902 rumble STOP confirmed
  -> DirectInput cleanup confirmed
  -> fresh unique PID1902 command HID; at most one write-only PID1901 command
```

- Keep the **2,000 ms** Windows session-end budget and existing no-OS-veto behavior.
- Completed *actual* reconcile failure, canceled/faulted reconcile, in-flight unresolved reconcile, unsafe native detach, publisher/feedback/STOP or physical cleanup failures remain fail-closed.
- No PID1901 write on app exit, Runtime restart/update, ordinary BPM exit, WM_QUERYENDSESSION, WM_ENDSESSION(FALSE), logoff, suspend/hibernate/resume, or wrong Center M authority.
- No changes to the once-per-Windows-boot A2VM `1902->1901->1902` rumble-prime exception or to the final first-input/PnP/HidHide safety checks. Rumble re-arm is not required on every boot; the 0.1.364 logs cannot settle its necessity.
- No changes to the PR #757 current-session exact HID rumble endpoint fast path: preserve the measured shutdown STOP reduction (361: 413–761 ms; 364: **29 ms**).
- No new authority, state bit, epoch, lock, gate, reconcile manager, extra session owner, artificial background job, or speculative race-proofing for improbable instruction-level interleavings.
- Supported lifecycle is a single user / single interactive Windows session. Realistic sleep/resume, physical loss/re-enumeration, startup defer/retry, game/BPM presentation switching, process restart/crash/shutdown remain in scope.

## 6. Focused tests

Extend existing tests in `tests/SteamInputAddonforClaw.Tests/` without creating a new host/reconcile manager or broad fixture architecture. Prefer a lightweight executable test through existing fakes/host seams where feasible; the existing source-contract tests can also assert placement/order of the guard.

1. **Reproduced BPM auto-start before PID1902 commit:** when presentation and physical owner objects exist but `LiveInputSource == null` and `ActivePresentation == null`, `BigPictureChanged` does **not** publish a new false `_presentationReconcile` task; the prior completed-success initial task remains intact.
2. **Fresh desired first attach:** after full PID1902/DirectInput/HidHide proof, initial attach reads `BigPictureActive=true` and selects the configured Steam-facing type, including DualSense Edge. No second stale BPM replay and no early virtual attach.
3. **Active reconciliation still works:** when the input source has committed, BPM exit / RunningAppID / presentation-preference updates still schedule tracked reconciliation and can switch from DualSense Edge/SteamDeck to Xbox360 and back under the presentation owner.
4. **Source actually lost with an existing presentation:** do *not* suppress the tracked reconcile solely because `LiveInputSource` is null. Keep the existing fail-close/physical-recovery contract.
5. **True errors and shutdown safety:** completed false from a real presentation reconcile, faulted/canceled task, unfinished operation hitting the deadline, inactive publisher, wrong physical identity, unsafe native detach, STOP failure, DirectInput cleanup failure -> no PID1901 command. The prior `AddonProcessHostWindowsSessionEndTests` assertions about in-flight drain, ownership revalidation, shutdown order, and deadline must remain true.
6. **Deferred initial-acquisition path:** a first-valid-input timeout followed by safe `ImmediateRecheck` retains exactly one boot-marker claim and no new mode write; eventual verified initial attach can use the current BPM snapshot. This is regression coverage only, not another implementation change.

Do not introduce a synthetic race test or a new production helper solely for testability. If a full `AddonProcessHost` construction is disproportionate, existing source-contract tests around the scheduler guard **plus** existing behavioral presentation, ownership, and session-end tests are acceptable, provided the tested guard precisely matches the observable 21:42/21:44 sequence.

Run affected tests, a build, and available CI. Actual A2VM reboot/shutdown verification is the **user's role after merge** and is **not** a local Codex/PR blocker.

## 7. Expected user-owned verification after code merge

On the same A2VM 8 hardware with BPM autostart:

- A premature `BigPictureChanged` while there is no live PID1902 source should no longer yield a stored completed-false reconciliation task (prefer an optional debug no-op event, or simply no scheduling).
- `A2vmInitialFastOwnershipCompleted Outcome=Owned` and `InitialPresentationAttached Presentation=DualSenseEdge` (or SteamDeck depending on preference) still follow only after exact HidHide proof.
- On a genuine Windows session end, log progression should now reach `A2vmWindowsSessionEndAdmissionCompleted`, publisher stopped, feedback drained, typed Detach success, VIIPER Closed, fast physical STOP, DirectInput cleanup, and `A2vmWindowsSessionEndModeCommandDispatch` / `CommandWritten` **if** Windows leaves enough time and native transport remains available.
- Do **not** equate a missing PID1901 final event, write-only command dispatch, or subsequent PID1901 observation with guaranteed firmware state at the exact instant Windows powered down.
- Real `UnsafeOutcomeUnknown` (native USB/IP disconnected before Detach) and genuine deadline failures remain possible and must be diagnosed separately, not hidden by this fix.

## 8. Deliverable and PR review boundary

One focused implementation PR: **host pre-ownership request guard plus regression tests**. A tiny accompanying active-doc clarification is optional only if necessary; this work order already states the exception. No broader redesign.

**Acceptance:** the 0.1.364 premature BPM event cannot poison `_presentationReconcile` with an inapplicable completed-false task; healthy verified first presentation and eventual session-end retirement use the existing safety path; real failed/in-flight reconciles and native teardown failures still block unsafe PID1901 mode writes. Code and automated tests are the PR gate; user-performed hardware testing follows merge.
