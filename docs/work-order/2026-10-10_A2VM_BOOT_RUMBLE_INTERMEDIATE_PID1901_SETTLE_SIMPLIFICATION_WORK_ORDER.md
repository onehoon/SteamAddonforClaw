# Work Order — A2VM 7/8 Boot Rumble Re-arm: Simplify Intermediate PID1901 Verification Without Changing Full1902 Safety

**Project:** SteamAddonforClaw (standalone Full1902)  
**Date:** 2026-10-10  
**Implementation:** Local Codex  
**Scope:** A2VM 7/8, *automatic one-time Windows-boot rumble prime only*  
**Out of scope:** EX/CG3EM, ordinary PID1901→PID1902 ownership, manual Restore Vibration, runtime recovery policy, shutdown mode, Steam/BPM presentation policy, and generic native-mode verification  
**Hardware verification:** User responsibility after merge; never a local-Codex deliverable or PR/CI blocker.

## 1. Goal

Fix the real A2VM 7/8 **0.1.353.0** boot failure/latency where the optional automatic rumble priming sequence

`PID1902 → PID1901 → PID1902`

can take too long or fail solely because the **intermediate PID1901** is not fully re-enumerated and globally re-captured within the normal strict five-second native-mode verification window. Startup may then enter first-acquisition recovery; Steam BPM can be visible for roughly 18–21 seconds before the SteamDeck presentation actually attaches.

Change the **intermediate boot-prime handoff only**:

1. Begin with existing exact Center M Disabled, VIIPER Ready, model and boot-marker admission.
2. From **initially strong PID1902**, send the existing verified-source, exact 64-byte XInput mode command.
3. For this **first leg only**, allow bounded PnP settling until **one fresh, strong MSI PID1901 vendor-command HID endpoint** is demonstrably ready and the former PID1902 has disappeared. Use a narrowly scoped controller/target probe instead of requiring a second global native-state snapshot of *all* Windows devices.
4. Carry the **fresh PID1901 identity** into the *unchanged* normal PID1901→PID1902 command and final Full1902 acquisition/proof.
5. Never mount VIIPER virtual output until final strong PID1902 identity, valid DirectInput input, exact HidHide isolation, Win+G suppression, and presentation admission all succeed.

**Do not move re-arm after first attach.** Do not create a short-lived virtual controller only to detach it immediately. Maintain the current first-attach ordering.

## 2. Read These Sources Before Coding

Architecture/document precedence:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md`
- `docs/work-order/2026-10-10_A2VM_BOOT_NATIVE_MODE_TIMEOUT_AND_INITIAL_OWNERSHIP_RECOVERY_WORK_ORDER.md`

Relevant implementation:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs`
- `src/SteamInputAddonforClaw/Controllers/Detection/WindowsControllerDeviceEnumerator.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- Existing tests in `MsiClawAddonPhysicalOwnershipTests`, `MsiClawModeControllerDiagnosticsTests`, `MsiClawModeSwitchTests`, and relevant initial-acquisition host tests.

Historical *behavioral* references (not integrations):

- HHC `Valkirie/HandheldCompanion`, `HandheldCompanion/Devices/MSI/ClawA1M.cs`: same MSI `0x24` mode command; HID write success is not sufficient as our final Full1902 attach proof.
- CTW fork `onehoon/ClawTweaks-Dev`, `release/v0.3.98.0`, `XboxGamingBarHelper/Devices/MSIClaw/MSIClawHidController.cs`, `Startup/Program.MSIClaw.cs`: actual A2VM boot command-interface lateness and practical DInput settling. **Do not import CTW architecture.**

## 3. Confirmed Field Evidence / Diagnosis

Field data: `GoogleDrive/Addon/Log/AV2M/02` and `AV2M/03`; consider **only 0.1.353.0** Runtime logs for this work order.

- 19:23 Windows boot: the first PID1902→PID1901 HID write succeeded, but the **five-second post-write** verification expired with `TargetDeviceDidNotAppear`. The later recheck saw strong PID1901, then converted to PID1902 and attached SteamDeck. Late appearance, not a proven failed command.
- 19:20 Windows boot: the same first-leg timeout entered first-acquisition recovery; a later **global PnP read** threw `Unable to read PnP property 1`, resulting in `NativeStateCaptureStatus.Failed` and terminal startup teardown. The exact Win32 error and affected device are **not recorded**; do not assert a firmware or driver root cause.
- 19:30 BPM boot: BPM detected at ~19:30:19.110; SteamDeck attached ~19:30:39.705 (**~20.6 s**). First leg timed out; recovery eventually found PID1901 and completed PID1902.
- 19:33 BPM boot: BPM detected ~19:33:13.558; SteamDeck attached ~19:33:31.953 (**~18.4 s**). The first leg succeeded, but the second PID1901→PID1902 verification timed out and existing recovery completed later.
- In an already-running Windows session, a **manual** Restore Vibration completed both verified legs in about **1.6–1.7 s per leg** and restored physical ownership/presentation in about **5.6 s**. This is evidence of boot PnP variability, not proof that the shorter timing is possible immediately after every boot.
- `libVIIPER.log` attached the virtual device in tens of milliseconds; VIIPER transport and the 250 Hz publisher were not the principal source of the measured **pre-attach** delay.

**What is proven:** successful physical mode command writes followed by slow or unavailable PnP confirmation; successful late recovery in some sessions; a distinct global property-read exception in another session.

**What is not proven:** exact physical device first-ready timestamp in every session, native Win32 error code in the property exception, or any need to weaken the final PID1902 / HidHide / VIIPER contract.

## 4. Actual Code Path Reviewed

### 4.1 Current boot-only section

`MsiClawAddonPhysicalOwnership.AcquireCoreAsync` (approximately lines 241–370):

- Reads initial full stable native snapshot.
- Calls `BootSession.TryClaimA2vmRumbleAttempt` for exact `msi.claw.a2vm.7` / `msi.claw.a2vm.8`.
- If initially PID1902 and claimed, resolves exact PID1902 control HID.
- Calls common `_switchMode(XInput, initialIdentity, token)`.
- Requires `IsCrossModeTransitionProven` (write, old PID absent, target PID appeared, source identity, exact target topology).
- **Then calls `_captureStableNativeState` again for PID1901**, requiring a successful, globally enumerated, strong native state before sending `_switchMode(DirectInput,...)`.
- The final PID1902 full snapshot, `EnsureDirectInputGamepadModeAsync`, descriptor/strong identity, first valid DirectInput state, HidHide baseline, and host's presentation attach happen later.

### 4.2 Two distinct expensive/failure-prone observations

`MsiClawModeController.SwitchModeAsync` (approximately lines 175–240) polls exact target topology plus old PID disappearance for five seconds **after** the HID write. The timing budget itself was corrected in the earlier work order; do not revert that change.

`WindowsControllerDeviceEnumerator.EnumeratePresentDevices()` (approximately lines 23–80) walks **every present device** and reads `SPDRP_HARDWAREID` (property 1) and other properties. One unhandled PnP property failure can abort the whole snapshot. The enumerator **already offers** narrower overloads, including `EnumeratePresentDevices(vid,pid)` and `IsPresent(vid,pid)`; prefer those for the boot-only intermediate probe.

`MsiClawNativeStateManager.CaptureStableCurrentSnapshotAsync` retries missing/mixed-topology cases, but a generic `Failed` result from the broad property read exits rather than settling. **Do not turn all generic enumeration failures into success or globally change EX behavior.**

### 4.3 Existing delayed first acquisition must remain

`AddonProcessHost.TryStartDisabledModeControllerAsync` / `RunInitialControllerAcquisitionAsync` already retain **one Ready VIIPER owner, one physical owner and the existing Device Arrival watcher** after an exactly typed target-not-present result. Rechecks consume no new boot-prime marker. Preserve this mechanism; do not add a second watcher or retry supervisor.

## 5. Required Change — Only the A2VM Automatic Boot Prime

### 5.1 Hard scope gate

Only enter the relaxed **intermediate first-leg verification** when **all** hold:

```csharp
IsA2vmBootRumblePrimeModel(_hardwareDeviceModel)
&& initialMode == MsiClawNativeMode.DirectInput
&& bootAttempt == BootSessionAttemptResult.Claimed
&& exactPid1902ControlHidPreflightSucceeded
&& CenterMIsStillDisabled
```

The above is conceptual: use actual existing state, method names, and single owner gate. Avoid an independent authority flag.

No change to `_switchMode` semantics for EX/CG3EM, A2VM starting at PID1901, manual Restore Vibration, Center M enable/release, owned recovery, PnP loss, or suspend/resume.

### 5.2 Simplify the **intermediate PID1901** requirement, not the command

Retain the existing MSI HID writer and exact 64-byte `SwitchMode(XInput)` command. Never treat a successful `WriteFile` alone as successful re-arm.

For this boot-only first leg:

1. Resolve and validate the **pre-write PID1902 physical/controller identity** and exact command HID under the existing admission.
2. Issue **one** PID1902→PID1901 command. Do not resend it speculatively merely because PnP is slow.
3. If normal `_switchMode` verification succeeds, proceed to a **narrow** fresh PID1901 command-endpoint resolution. Do **not** follow it with another mandatory *global* `_captureStableNativeState` for the intermediate state.
4. If normal `_switchMode` returns **only** the documented, typed **write-succeeded / old PID disappeared / target not yet present** result, allow a **bounded A2VM-boot-only continuation** of PID1901 target settling. Do not restart the whole prime, reset the per-boot marker, or throw away the successful write.
5. Probe only the relevant MSI controller target using the existing narrow enumeration capability when practical:
   - expected VID `0x0DB0`, PID `0x1901`;
   - exact vendor-command HID UsagePage `0xFFA0`, Usage `0x0001`;
   - **one** eligible logical MSI control device, with **Strong** identity derived from the *fresh* PnP node;
   - old PID1902 no longer present (a genuine overlapping old/new mode remains transitional);
   - no alternative / ambiguous MSI physical controller or mismatched command endpoint.
6. Continue only with this **fresh PID1901 strong identity** as input to the existing `_switchMode(DirectInput, xInputIdentity, token)`. Across a real PID change the Windows physical-root string can change; **do not call `StronglyMatches` across 1902↔1901**. Use the controlled write, old-device disappearance, unique fresh target/control topology, and ensuing final PID1902 proof as the continuity evidence.
7. The next HID write may start only when the fresh target command endpoint has actually appeared and is uniquely identified. If it cannot be opened/written, the **existing mode writer's normal failure** still applies; never assume PID1901 merely because the prior write returned true.
8. Keep `IsCrossModeTransitionProven` and other strict general-purpose transition predicates unchanged. The relaxed intermediate evidence is explicitly private to this **one** A2VM boot-prime step; do not make a generic `SkipVerification` option available to unrelated callers.

**Implementation guidance:** favor a narrow helper within the existing mode-controller/physical-owner seam and existing `IControllerDeviceEnumerator`. If an injectable PID-target probe is necessary for the physical owner, inject the narrow existing enumerator operation in the smallest practical manner. Do not add a new controller manager, extra persistent state, separate authority, generic framework, or global bypass flag. Before adding an interface, check whether current code/dependencies can already expose the observed target identity directly.

### 5.3 Bounded timing, no blind fixed sleep

- Preserve the ordinary common mode-controller **5 s post-HID-write** behavior for all existing callers.
- The A2VM boot-only **first leg** may continue observing PID1901 after a typed late-arrival result, within a small **finite additional settle window** (suggested initial ceiling: **5–7 additional seconds**, i.e. approximately **10–12 seconds maximum after the original write** including the existing five-second probe). Choose the narrowest budget consistent with the observed ~8-second late PID1901 appearance and document it in code.
- Poll only until the command endpoint is ready; **exit immediately** on success. No additional unconditional 2.5-second sleeps.
- Account for cancellation, Center M authority loss and Runtime shutdown through existing lifecycle admission. Never begin the second native write after loss of authority.
- Do not expand the **second leg** or manual/EX timeouts in this change. A PID1901→1902 delay still uses the existing typed initial-acquisition/Device Arrival recovery introduced by PR #738.
- Avoid hammering broad PnP scans at 75 ms intervals in the extra boot-only window: use an appropriate modest bounded probe cadence with focused enumeration.

### 5.4 Transient PnP read faults during this boot operation

The 19:20 failure `Unable to read PnP property 1` must not be silently equated to a physically incorrect controller. In particular, a **broad unrelated-device property read** should not invalidate a uniquely established intermediate PID1901 command endpoint.

- Prefer the **target-scoped PID1901 observation** above, which avoids many unrelated-device reads in the first place.
- Where the A2VM **boot-prime continuation or immediately subsequent first-acquisition settle** encounters an objectively transient PnP read failure, permit bounded re-observation using the existing readiness/recovery path. If error classification is needed, preserve the native Win32 error and operation stage as structured evidence; do not parse the free-form English exception string to decide safety.
- **Do not** blanket-catch all exceptions, assume successful mode transitions, skip an ambiguous/weak physical identity, or change `EnumeratePresentDevices()` / `CaptureSnapshot()` global failure semantics for EX. If a shared enumerator adjustment is unavoidable, limit it to target-scoped probing and nonbehavioral diagnostics; prove unchanged ordinary callers in tests.
- Do not retry forever or rely only on Device Arrival when no new event is guaranteed; use the existing bounded immediate recheck and event-driven pending path where correctly admitted.
- If a concrete PnP error cannot be distinguished as transient, retain fail-close. Record the specific reason rather than inventing a recovery success.

### 5.5 Fail-close and recovery matrix

| Condition | Required action |
| --- | --- |
| A2VM PID1902 boot, marker Claimed, first write + timely fresh PID1901 command endpoint | Continue directly with **one** normal PID1901→PID1902 switch |
| First write proven, old PID gone, PID1901 appears after original 5 s but inside boot-only settle budget | Resume first leg; do not reissue PID1902→PID1901 or restart full acquisition |
| PID1901 still absent at bounded deadline after verified write | Keep first ownership uncommitted / virtual output detached; use existing typed first-acquisition Device Arrival recovery where supported |
| Source/control HID ambiguous, write failed, old PID still active, target ambiguous, identity weak, or unrelated target | Fail closed; do not issue speculative second write |
| PID1901→PID1902 target late | Use **unchanged** ordinary transition proof + existing deferred first-acquisition recovery; boot marker stays consumed |
| Final PID1902 strong identity, DirectInput first state, HidHide readback, Win+G or VIIPER proof fails | Fail closed as before; no virtual attach |
| Same-boot Restart Addon / app update / crash restart / PnP recovery / Resume | No extra optional prime; ordinary PID1902 reconciliation |
| A2VM initial PID1901 | One normal PID1901→PID1902 switch, no extra cycle |
| EX/CG3EM (any boot state) | **Byte-for-byte behavioral parity with current production path** |
| Manual `Restore Vibration` | Existing strict two-leg verification and presentation retirement unchanged |

An optional rumble prime must not destroy the ordinary controller-startup recovery path. However, once the first native command has *actually moved the hardware* away from PID1902, never attach a stale virtual controller or claim ownership until PID1902 is safely recovered.

## 6. Preserve These Contracts Absolutely

- Single Windows administrator user / single interactive session; no RDP, Fast User Switching, multi-session extensions.
- Center M Disabled ⇒ Addon is the PID1902/HidHide authority; no implicit stock PID1901 release on normal shutdown, exit or sleep.
- Exactly one owned physical input source and one canonical VIIPER presentation. Existing gates and actual teardown path remain authoritative.
- Exact strong **final PID1902** identity and primary collection, committed HidHide target/readback, valid first DirectInput state, Win+G suppression and publisher readiness before any virtual attach.
- VIIPER teardown/physical STOP/rumble endpoint lifecycle, rollback and recovery remain unchanged.
- No automatic repeat of the A2VM prime in the same real Windows boot; existing marker still consumed **before the first optional write**, never by ordinary ownership.
- No post-attach automatic re-arm, new UI setting, new user-visible state machine, new retry service, background polling watchdog, new locks, new epochs, or speculative race protection.
- Manual Restore Vibration and CG3EM support are entirely out of scope.

## 7. Automated Verification — Required for Local Codex

Extend existing focused unit tests rather than inventing a separate test application. Use fake enumerators, injected clock/delay, fake mode writer and existing owner harness. No physical device needed.

1. **Late intermediate PID1901:** A2VM 7 and A2VM 8 start Strong PID1902, marker Claimed; source write succeeds and old PID disappears; PID1901 control interface appears at ~8 s after write. Exactly **one XInput command** and **one DirectInput command** are sent; full PID1902 ownership commits and presentation remains eligible for one initial attach.
2. **Fast intermediate PID1901:** early target success must not wait out the optional settle window or add fixed delays.
3. **Unrelated PnP property exception:** model a failed broad global enumeration while fresh **narrow PID1901 target lookup is healthy**; boot-only middle step proceeds without requiring the broad snapshot. For any bounded first-acquisition transient-read recovery included in the implementation, test observed failure then fresh proof, and distinguish a permanent/ambiguous fault.
4. **Never promote mere write success:** old PID remains, control target missing, weak identity, multiple logical targets, wrong usage/page, or HID target cannot be written ⇒ no follow-up DirectInput command and no virtual attach.
5. **Bounded disappearance:** target never appears by the boot-only deadline ⇒ no blind mode-write retry, no unbounded waiting, no early VIIPER attachment. Preserve existing typed deferred first acquisition when its exact proof predicate holds.
6. **Second leg and final proof:** PID1901→PID1902 timeout/recovery stays as currently implemented; a final PID1902 identity mismatch, DirectInput failure, changed exact HidHide primary target or noncompliant HidHide still blocks attach.
7. **Marker/lifecycle:** no optional second cycle after same-boot Restart Addon, update restart, PnP callback or Resume; initial PID1901 performs normal one-way takeover; admission changes before a write prohibit the write.
8. **Regression:** EX/CG3EM `Already_pid1902_acquires_without_a_mode_write`, existing strict mode-controller diagnostics, manual re-arm tests, stock release, owner teardown and physical recovery retain their prior behavior. No common-mode verification regression.

Suggested files:

- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawModeControllerDiagnosticsTests.cs`
- Existing host initial-ownership/Device Arrival tests, only if touched by this PR.

Run the repository's existing restore/build and relevant automated tests locally and report results in the PR. Fix failures caused by this change; do not require real device access.

## 8. Minimal Diagnostics / Acceptance

Use structured events consistent with existing `A2vmBootRumbleAttemptMarker`, `NativeModeTransitionCompleted`, `A2vmBootRumblePid1901Verified`, `Pid1902TransitionVerified`, and `InitialControllerAcquisitionDeferred`. For the **intermediate** boot-only flow, log enough to answer:

- First command write succeeded? `SinceCommandWriteMs` at target observation?
- Did ordinary five-second proof succeed or did the bounded boot-only continuation run?
- When was **one strong PID1901 command endpoint** observed and old PID1902 absent?
- Did transient narrow PnP observation fail (with native error code if known), or was the device genuinely absent/ambiguous?
- Did the second PID1901→PID1902 transition complete and did final Full1902 attach succeed?

Avoid per-75ms INFO log floods; one start/terminal line and DEBUG-only cadence where useful. Do not claim that motor vibration was physically verified merely because the re-arm cycle and presentation completed.

**Code-review acceptance:**

- Only A2VM 7/8 *boot-only intermediate* verification policy is relaxed.
- Delayed, actual target arrival is handled without repeating the first mode write and without first-acquisition teardown from avoidable intermediate global PnP reads.
- Actual device mismatch, unsafe cleanup, final ownership/isolation violations remain blocking.
- EX, CG3EM, manual re-arm, normal one-way takeover, stock release, suspend/resume and shutdown behavior are unchanged.
- No new lifecycle authority, unnecessary abstraction or speculative synchronization.
- Code builds; focused automated tests pass.
- Update the **authoritative Full1902 A2VM exception paragraphs** (at least the `docs/Full 1902 Implementation/README.md` and `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` if behavior changes) to distinguish the *relaxed intermediate PID1901 command-readiness proof* from the *strict final Full1902 proof*. Preserve historical work orders as history rather than silently rewriting them.

### User-only post-merge hardware checks (not Codex / CI / PR review blockers)

The user will separately check A2VM 8 Windows boot with Steam BPM autostart, first-button usability time, actual left/right motor vibration, same-boot Restart Addon, power cycle, Sleep/Resume and restore button. A2VM 7 requires its own eventual user validation. **Do not ask local Codex to perform physical hardware validation; do not block merge on it.**

## 9. Local Codex Execution Sequence

1. Inspect implementation and existing tests listed in §2; identify the smallest source-level seam for a fresh narrow PID1901 command-endpoint observation.
2. Implement **only** the gated A2VM boot first-leg continuation and removal of redundant intermediate global snapshot dependency.
3. Keep the second leg, final proof, initial-acquisition recovery, VIIPER/HidHide cleanup and other hardware models unchanged.
4. Add focused simulated late-arrival / transient-enumeration / fail-close / regression tests.
5. Build and run relevant software tests. Update the authoritative A2VM exception documentation and create one focused PR. Provide the diff/test summary; leave real-device testing to the user after merge.
