# Work Order — A2VM 0.1.357 Windows Session-End PID1901 Restore: P0 Dispatch Fix and P1 Safe Teardown/Timing Repair

**Project:** SteamAddonforClaw — standalone Full1902
**Date:** 2026-10-11
**Implementation owner:** Local Codex
**Vehicle:** ONE implementation PR with THREE logically separate commits
**Base reviewed:** main @ 3b7bb38b39bbc6c387ce8618cba45588e6ee4ae1 (PR #745)
**Field evidence:** GoogleDrive/Addon/Log/AV2M/1011-1, A2VM 8, Runtime 0.1.357.0
**Priority:** P0 + P1 only; no startup-performance P2 changes
**Hardware validation:** User responsibility after merge. Local Codex must not be required to test on real hardware; real-device testing is not a PR or merge blocker.

## 1. Goal and hard scope boundary

Repair the *existing* A2VM 7/8, Center M Disabled, genuine Windows shutdown/restart best-effort PID1901 command path. The observed 0.1.357.0 implementation has a verified C# interface dispatch defect and several distinct, realistic shutdown-retirement failures.

Do this in **one PR, three commits**:

1. **Commit 1 — P0: fix write-only XInput command interface dispatch and behavioral regression tests.**
2. **Commit 2 — P1: inspect and improve the current Windows-session-end presentation/VIIPER and in-flight presentation-reconcile shutdown handoff where a demonstrably safe code-level improvement exists; retain fail-close on unproven native outcomes.**
3. **Commit 3 — P1: add concise stage-level outcome/timing diagnostics and use the measured timings to refine the existing single shutdown budget, without blocking OS shutdown or allowing late PID mutation.**

This is a repair of the already-authorized exceptional Windows-session-end preparation. It is **NOT** a change of durable controller authority. It must not make application-only exit/restart, Velopack replacement, Sleep/Hibernate/Resume, user logoff, or session cancellation send PID1901.

**Do not include**: boot PID1902-to-1901-to-1902 simplification, LED/vibration startup deferral, virtual attachment reorder, new early startup scheduling, A2VM boot marker changes, additional startup timing optimization, new service/daemon/worker/process, alternate controller owner, alternative VIIPER instance, persistent restored-PID state, or CTW integration.

## 2. Required source-of-truth reading

Read the following existing active documents in their precedence order before coding:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/work-order/2026-10-11_A2VM_WINDOWS_SESSION_END_BEST_EFFORT_PID1901_RESTORE_WORK_ORDER.md (original contract; this work order repairs its first implementation)
- docs/work-order/2026-10-11_A2VM_INITIAL_CONTROLLER_FAST_PATH_HHC_CTW_SIMPLIFICATION_WORK_ORDER.md (existing startup behavior; intentionally NOT modified here)

Code and tests to inspect as a connected call graph:

- src/SteamInputAddonforClaw/Lifecycle/NativeTrayHostWindow.cs and WindowsSessionEndMessage
- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs: HandleWindowsSessionEnd, PrepareForWindowsSessionEndAsync, GetWindowsSessionEndAdmissionFailure, TryBeginProcessShutdownCore, RequestControllerPresentationReconcile
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs: PrepareForWindowsSessionEndAsync, RetireAsync, RetireActivePresentationCoreAsync
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs: DetachXbox360, TeardownAsync, tracked attachment and Unsafe semantics
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckSession.cs: DetachDevice and cleanup ownership
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs: PrepareForWindowsSessionEndAsync and cleanup proof
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs: IMsiClawModeController
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs: WriteXInputCommandAsync
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs: interface-typed modeController field and delegation
- src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs and WindowsMsiClawRawHidTransport.cs
- tests/SteamInputAddonforClaw.Tests/AddonProcessHostWindowsSessionEndTests.cs
- tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs
- tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
- tests/SteamInputAddonforClaw.Tests/CanonicalViiperRuntimeTests.cs
- related presentation/SteamDeck-session tests and native message classification tests

The product assumes one supported Windows user and one interactive session. Do not design for RDP, Fast User Switching, multi-session, speculative scheduler interleavings, or hypothetical future controllers.

## 3. Exact 0.1.357 field evidence

Log directory: https://drive.google.com/drive/folders/1BD5LhzpzI3d2Pwn94-xLFXBkbBbQYllS

| Session | Final Windows-session-end result | Direct evidence |
| --- | --- | --- |
| 11:51 process / 11:52 end | Skipped: PresentationRetirementUnproven, 612 ms | Xbox360 detach -> UnsafeOutcomeUnknown; native VIIPER transport already had a client disconnect |
| 11:53 process / 11:54 end | Skipped: PresentationRetirementUnproven, 629 ms | SteamDeck detach -> UnsafeOutcomeUnknown; native VIIPER transport already had a client disconnect |
| 11:55 process / 11:56 end | Skipped: WriteOnlyModeCommandUnavailable, 1,355 ms | VIIPER detach/teardown and DirectInput unacquire/dispose completed; mode-write dispatch returned interface default |
| 11:57 process / 11:58 end | Skipped: ControllerLifecycleOperationInProgress, 289 ms | BPM exit caused live SteamDeck -> Xbox360 presentation switch at 11:58:35.354, concurrent with real OS session-end at 11:58:35.524 |
| 11:59 | Ordinary application restart | Do NOT count as a Windows session-end restore attempt |
| 12:01 | No session-end record in file | Do NOT count as an observed restore attempt |

Specific source links:

- 11:51 Runtime: https://drive.google.com/file/d/1lh6KDmgDxD_l9geZdLIH_zmUDDrd4m70/view
- 11:53 Runtime: https://drive.google.com/file/d/1zLGLmBjdZlIrvzMp81qyZlP9v_e_u4CD/view
- 11:55 Runtime: https://drive.google.com/file/d/1-sDj1F4pa3wTuecPLinfGDzvSY8M1FSj/view
- 11:57 Runtime: https://drive.google.com/file/d/1fd_9VDszKQNRtHKh9wZ2s4-MZe7O7rrr/view
- 11:59 Runtime: https://drive.google.com/file/d/1Ev_4F7XRFchKdZBEkMIjuLnArJNodZSo/view
- 12:01 Runtime: https://drive.google.com/file/d/1zY1IeyXiZh_oKr2Xv4s9M4JN1CNcPtjF/view
- Native libVIIPER: https://drive.google.com/file/d/1gtnDazSG2JYVwzw8a6dOrVrxVsyl4wKn/view

**Native chronology relevant to P1:**

- At 11:52:14.544 the USB/IP client was disconnected; the later 11:52:15.441 Xbox360 detach produced unsafe-outcome-unknown. Native timing: openUs=508, ioctlUs=0.
- At 11:54:29.225 the client was disconnected; the later 11:54:29.427 SteamDeck detach produced unsafe-outcome-unknown. Native timing: discoveryUs=521, ioctlUs=0.
- At 11:56:30.547 the SteamDeck detach succeeded via IOCTL (ioctlUs=1114); VIIPER teardown completed at 11:56:30.571; DirectInput cleanup completed at 11:56:31.018; the command dispatch then reported WriteOnlyModeCommandUnavailable at 11:56:31.168.
- 11:58:35.354 begins SteamDeck -> Xbox360 switch; shutdown preparation skips during that in-flight lifecycle operation.

The sequence supports an actual native detach accessibility/ordering problem on OS session end, but **does not establish that simply extending the timeout would recover the device**. In the 11:51 session, transport disconnect preceded the estimated beginning of the 612-ms session-end callback. Preserve that limitation in diagnosis: a managed callback cannot guarantee it runs before Windows or USB/IP teardown.

All four real shutdown attempts ended Skipped; no verified CommandWritten occurred. Subsequent launches observed initial PID1902 before their own new mode writes. That is evidence of failure to prepare the device for the intended PID1901 first-boot shortcut, not a measurement of the controller's physical state at the exact instant power was lost.

## 4. Commit 1 — P0: eliminate interface default-implementation dispatch bug

### 4.1 Root cause is source-proven

IMsiClawModeController in MsiClawModeContracts.cs defines a default WriteXInputCommandAsync implementation returning:

~~~csharp
new MsiClawModeCommandWriteResult(
    false, false, "WriteOnlyModeCommandUnavailable");
~~~

MsiClawModeController currently declares a same-signature **internal** WriteXInputCommandAsync method. Under C#, the internal method does not implicitly implement a public interface member; calls through IMsiClawModeController select the default interface implementation.

MsiClawNativeStateManager holds an **IMsiClawModeController? modeController** and calls that member through the interface. AddonProcessHost supplies the native-state-manager delegate to the physical owner, so this behavior is reachable in production and exactly explains the 11:55 result.

### 4.2 Required fix

Make the existing concrete method public (or use a deliberate explicit interface implementation, if justified by existing code conventions). Prefer the smallest change:

~~~csharp
// MsiClawModeController.cs
public async Task<MsiClawModeCommandWriteResult>
    WriteXInputCommandAsync(
        MsiClawPhysicalIdentity expectedIdentity,
        CancellationToken cancellationToken)
{
    // Preserve the existing strong PID1902 validation,
    // unique source/command HID resolution,
    // and exactly one write-only XInput HID report.
}
~~~

Keep the real 64-byte vendor command implementation and native writer unchanged. Do not route shutdown through SwitchModeAsync: that method waits for a full PnP transition and violates the intended bounded shutdown contract.

**Required behavior test (not merely a source-text assertion):** instantiate the actual MsiClawModeController with a fake enumerator/resolver/writer, upcast it to IMsiClawModeController, call WriteXInputCommandAsync, and prove the fake writer received exactly one XInput write when the current strong PID1902 source is unique. Also verify the NativeStateManager interface-delegated entrypoint reaches that real method. Assert no post-write PnP wait.

Example shape (adapt to the existing test fixtures/types):

~~~csharp
IMsiClawModeController controller =
    new MsiClawModeController(enumerator, resolver, writer);

var result = await controller.WriteXInputCommandAsync(
    MsiClawPhysicalIdentity.From(ownedPid1902),
    CancellationToken.None);

Assert.True(result.WriteAttempted);
Assert.True(result.WriteSucceeded);
Assert.Equal(1, writer.WriteCount);
Assert.Equal(MsiClawNativeMode.XInput, writer.LastTarget);
~~~

Preserve safety negatives: weak/wrong PID identity, missing/ambiguous source command HID, failed transport write, cancellation -> no falsely reported success. Verify exactly one actual write, not only a method being callable.

After this commit, successfully retired presentation/physical owners must reach the *real* write-only path, subject to the existing remaining shutdown time.

## 5. Commit 2 — P1: improve safe session-end teardown without relaxing VIIPER ownership

### 5.1 Current owner chain (preserve)

For eligible WM_ENDSESSION(TRUE) only:

~~~text
NativeTrayHostWindow (single existing HWND / message pump)
  -> AddonProcessHost session-end admission + existing process shutdown gate
  -> MsiClawAddonPresentation.RetireAsync("WindowsSessionEnd")
       -> stop/join active publisher
       -> disarm/drain feedback, physical rumble STOP
       -> neutralize/detach active typed device
       -> prove canonical VIIPER teardown complete
  -> MsiClawAddonPhysicalOwnership.PrepareForWindowsSessionEndAsync
       -> exact strong PID1902 ownership / control HID checks
       -> stop + prove DirectInput cleanup
       -> write-only XInput command (exactly once)
  -> return to Windows, no PID1901 PnP wait
~~~

The order is a safety requirement, not optional polish. If a typed native detach reports UnsafeOutcomeUnknown or the canonical VIIPER state is Unsafe, do not write PID1901.

### 5.2 Failure A: native Detach arrived after USB/IP transport loss

Investigate the exact canonical/native detach path and diagnostics before implementing mitigation. The 11:51/11:53 native traces show **no successful native detach IOCTL** in those failed attempts; one stopped at device open and the other at discovery.

If code inspection identifies a concrete, safe, small improvement that can execute an already-authorized detach earlier **within the same WM_ENDSESSION retirement**, make that improvement using the existing one presentation owner and its gate. Preserve publisher join, feedback disarm/STOP, neutral before detach, and proof of actual detach and VIIPER close.

Do **not**:
- Treat 'client disconnected', 'device not found', RetryableFailure, Invalid, or UnsafeOutcomeUnknown as equivalent to proven Detached/Closed.
- Force PID1901 while VIIPER native state is unresolved, publisher might still write, or DirectInput cleanup is unproven.
- Add a second teardown manager or force-close USB/IP as a substitute for canonical proof.
- Mutate physical PID in WM_QUERYENDSESSION; shutdown may be cancelled after this message.
- Change the VIIPER native ABI, fallback to a second native device instance, or add Windows-service/preshutdown infrastructure solely to win an OS shutdown race without evidence.
- Promise success in the event that Windows has already removed the necessary native endpoint before the existing callback runs.

If no demonstrably safe early-detach optimization exists, **retain the current fail-close result** and record the observed missing OS-native resource as a limitation rather than manufacturing a success. That is an acceptable, evidence-based outcome for this subcase; do not add speculative complexity.

### 5.3 Failure B: live presentation switch during session end

The 11:57 case is a normal lifecycle path, not an artificially constructed instruction-level race. At the Windows shutdown boundary, Steam BPM may exit and initiate SteamDeck -> Xbox360 reconciliation.

Today GetWindowsSessionEndAdmissionFailure unconditionally skips if _presentationReconcile is not IsCompletedSuccessfully, even if the existing owner can safely finish that single operation and retire under its serialized gate shortly thereafter.

Examine a **narrow bounded-drain** option for the current presentation-reconcile task, using only the existing process shutdown gate and presentation owner serial gate:

1. Keep admission fail-close for active physical ownership recovery, uncommitted initial acquisition, Developer rumble re-arm, release/cleanup, unavailable authority, and unsafe/suspended/overlay state.
2. Once the Windows session-end operation is eligible, close further reconcile admission via the existing shutdown gate; do not permit new publisher attach after close.
3. If the *already running* presentation reconcile is the sole conflicting work, permit a **short, cancellation-aware wait** within the same single global 1.5-second starting budget; recheck actual owner/presentation state after it settles.
4. Proceed to RetireAsync only if the reconciled state is valid, publisher ownership is known, and the native owner can be retired normally. If it fails/cancels/times out or ownership remains uncertain, return Skipped without a mode write.
5. Avoid introducing a fresh semaphore, epoch, generation, manager, authority flag or generalized async queue. Do not block indefinitely on a switch that may itself need shutdown resources.

The host already tracks _presentationReconcile, already gates new reconcile on _processShutdownStarted, and its existing shutdown core cancels _startupCancellationTokenSource. **Inspect that actual cancellation behavior carefully**: do not simply await a task using the newly-cancelled token without verifying the current presentation remains safely retired, and do not allow a previous reconcile to reattach output after the physical PID command.

Implement only the smallest approach supported by the current owner/gate semantics. If the task cannot drain or the presentation cannot be proven safe within the deadline, skipping is correct.

### 5.4 Preserve normal non-session shutdown paths

Do not change canonical detach/VIIPER semantics globally just to make session-end logs say Success. Explicit Center M Enable-and-Restart/uninstall must retain their strict stock restoration; normal app restart, ordinary shutdown cleanup, overlay pause, publisher fault, Steam/BPM presentation switching, Suspend/Hibernate/Resume and real PnP loss/recovery must retain their existing contracts.

If the session-end changes introduce any shared owner-path change, write focused regression tests for those concrete affected paths.

## 6. Commit 3 — P1: measure the actual shutdown time and refine the single budget

### 6.1 Existing budget issue

AddonProcessHost.WindowsSessionEndPreparationBudget is currently 1,500 ms, covering Task.Run scheduling, admission, process-shutdown preparation, VIIPER retirement, DirectInput cleanup and final HID command write. The handler waits at most that long and does not veto Windows shutdown.

At 11:55, all cleanup finished but outcome was emitted at 1,355 ms, leaving only roughly **145 ms** of nominal budget to locate and write the required HID command if the dispatch bug is fixed. This demonstrates a *plausible time-budget constraint*; it does not prove that the hardware write needs more than 145 ms.

The 612/629-ms VIIPER-unsafe and 289-ms admission-failure observations are early exits, not successful path timing benchmarks.

### 6.2 Required concise diagnostics

Keep exactly one existing summary record per eligible notification:

~~~text
Event=A2vmWindowsSessionEndPid1901Prepare
Trigger=WindowsEndSession
Model=msi.claw.a2vm.8
Outcome=CommandWritten|Skipped|WriteFailed
Reason=...
ElapsedMs=...
PnP1901Verified=False
~~~

Add a **small number of low-volume stage completion records** (or one structured summarized stage breakdown), at INFO for successful stage boundaries and WARN only for real failures. Suggested measured stages:

- Admission + begin existing shutdown gate
- Publisher stop/join and feedback drain / physical rumble STOP
- Typed-device Detach result (Xbox360 or SteamDeck; distinguish native Retryable, Unsafe, Invalid and complete)
- VIIPER teardown proven Closed
- DirectInput stop + cleanup confirmed
- PID1902 exact command source resolution
- XInput HID write: attempted, succeeded/failed/unconfirmed
- remaining deadline budget immediately before the native command

Include StageElapsedMs or cumulative ElapsedMs, RemainingBudgetMs and a precise failure reason where available. Reuse the existing logs wherever sufficient; **do not duplicate high-frequency poll/callback traces**. Prefer a handful of per-shutdown milestones over a new generic telemetry subsystem.

On failed native detach, correlate the already-existing libVIIPER attachment-timing fields (discoveryUs/openUs/ioctlUs, before/after attachment/server state) with the Runtime owner-level event. Preserve the distinction between 'we did not issue a command' and 'a command was attempted but its completion/result is unconfirmed' — **do not mislabel a late/uncertain hardware write as proven never sent**.

### 6.3 Budget adjustment rules

Review the entire measured successful 11:55-style teardown path plus the time needed for *one* existing HID source re-resolution and write. Do not simply increase 1,500 ms to an arbitrary large timeout or wait for PID1901 PnP.

A modest, code-supported budget adjustment may be made in this commit if it improves the realistic 11:55 path while keeping the Windows shutdown callback deliberately short. Keep one bounded total budget, not independent 1.5-second waits at each stage; honor a cancellation/remaining-time guard **before** HID writing and after any uncancellable cleanup call.

If no field measurement of the corrected command write is yet available, document the remaining uncertainty and choose a conservative bounded adjustment only if grounded by the current writer's measured operations. Avoid claiming a proven quantitative optimal budget from the failed 0.1.357 logs.

The existing callback waits for the one operation and cancels on timeout. Ensure cancellation prevents a **new** physical mode write after the callback's deadline, even if a preceding cleanup operation returns late. A write whose native result was already in-flight at cancellation is not evidence that it never occurred; maintain correct diagnostic outcome terminology.

Never call normal full SwitchModeAsync here or wait for PID1901 to enumerate.

## 7. Required automated tests and acceptance

Tests should be behavior-level where practical; do not rely solely on source-grep checks.

**Commit 1:**
- Interface-typed IMsiClawModeController call invokes the real mode writer; test would fail on the original internal-method/default-interface version.
- End-to-end delegation through MsiClawNativeStateManager to the same one-write command.
- Strong owned PID1902 identity, exact 0xFFF0/0x0040 source, one XInput command, no PnP target wait.
- Weak/ambiguous/absent command HID, write failure/cancellation do not return CommandWritten.

**Commit 2:**
- Proven safe VIIPER detach then proven teardown -> DirectInput cleanup -> exactly one mode write, same canonical owner and order.
- Xbox360 and SteamDeck native UnsafeOutcomeUnknown -> never write PID1901; do not reinterpret an unavailable USB/IP client as successfully detached.
- Steam/BPM presentation reconcile already in progress at WM_ENDSESSION: after a short successful bounded drain and fresh valid state, teardown may proceed; when timeout/failure/unproven ownership, skip without native mode write.
- Existing normal Xbox360 <-> SteamDeck switching and Center M Enable release behavior retain unchanged safety.
- Real Windows message classification remains unchanged: WM_QUERYENDSESSION, cancelled WM_ENDSESSION(FALSE), user logoff, CLOSEAPP/Restart Manager, tray Restart/update, sleep/hibernate/resume -> no Windows-session-end mode write.

**Commit 3:**
- Single short global budget and single summary event; stage fields reflect actual execution order.
- Delay/timeout before command -> zero new mode writes after cancellation; in-flight unknown is not mislabeled positively.
- True command success reports **CommandWritten, PnP1901Verified=False**, never 'PID1901 verified'.
- Unexpected failures or unavailable devices do not block/veto Windows shutdown.

Run the relevant automated solution build/test suites locally in code; describe what passed and what, if anything, was not runnable. No real-device test is required for local Codex or PR merge.

## 8. Implementation constraints and PR delivery

- ONE implementation PR, with exactly the THREE logical implementation commits above. The documentation-only work-order commit on main is separate from these future implementation commits.
- Keep changes narrowly scoped to this Windows session-end exception and affected tests/docs. If a shared method must change, preserve all other callers' behavior and prove it in software tests.
- Existing primary host / physical owner / presentation owner / canonical VIIPER runtime remain the only authorities. No new state machine, new owner/service, redundant validation manager, arbitrary retry, or exotic race defenses.
- No forced PID1901 command after uncertain Detach, while publisher could still be active, before DirectInput cleanup, or during actual authority transition.
- No software path may clear the persistent HidHide Disabled-mode baseline or enable Center M on Windows shutdown.
- The user will perform the hardware follow-up **after merge**. Include a concise PR verification checklist for user-owned Start-menu Restart/Shut down, initial PID before any new mode write, CommandWritten vs Skipped, Xbox360/SteamDeck cases, ordinary app Restart, and Sleep/Resume. Do NOT make that checklist a code/CI/merge gate.
- Work against current latest main rather than trusting stale line numbers. Explain in PR description which 0.1.357 real failures were addressed versus which remain correctly fail-closed due to Windows/native teardown order.

## 9. Success criteria and practical expectations

A successful software implementation must:

1. Remove the proven 'WriteOnlyModeCommandUnavailable' production dispatch regression and test the real interface invocation.
2. Preserve the strict publisher -> VIIPER -> DirectInput -> command order and **never** promote unknown detach into safe.
3. Improve the existing in-flight presentation-switch shutdown path if a short bounded existing-gate handoff can prove safety; otherwise retain a precise Skipped outcome rather than add speculative sync.
4. Make the short Windows-session-end time budget and stop/write bottlenecks diagnosable and tuned on actual evidence.
5. Leave all non-Windows-shutdown PID1902, HidHide, VIIPER, startup rumble, stock release, power lifecycle, and normal presentation switching behavior intact.

**Do not claim this PR guarantees PID1901 at the next boot.** CommandWritten records one accepted outgoing HID command, not completed PnP re-enumeration. Only user-owned next-boot hardware logs can establish whether the initial observed PID is 1901 or 1902 and whether startup latency improves.

**Bottom line:** Fix the real dispatch defect first. Improve only realistically proven, safe shutdown owner handoffs. Measure what Windows/VIIPER actually allows rather than trading lifecycle safety for an optimistic PID1901 write.
