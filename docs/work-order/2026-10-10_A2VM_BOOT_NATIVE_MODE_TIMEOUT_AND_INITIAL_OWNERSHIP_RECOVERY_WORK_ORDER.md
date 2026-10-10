# Work Order — Fix A2VM Boot Native-Mode Re-enumeration Timeout and Initial Ownership Recovery

**Date:** 2026-10-10  
**Repository:** onehoon/SteamAddonforClaw  
**Status:** Ready for local Codex implementation  
**Priority:** Production blocker before first release of the affected build  
**Target:** One focused code-and-automated-test PR  
**Hardware validation owner:** User, after merge. Real-device testing must not be required of local Codex and must not be a PR/merge blocker.

## 1. Objective and product scope

Fix the actual **0.1.352.0** MSI Claw A2VM 8 Windows-boot failure in which the mandatory standalone Full1902 Addon Runtime starts but **neither Xbox360 nor SteamDeck virtual controller is attached**. The controller stays unavailable until the user chooses **Restart Addon**.

The fix must retain the intended once-per-real-Windows-boot **A2VM 7/8 physical rumble re-arm** (verified PID1902 -> PID1901 -> PID1902 when starting at PID1902) rather than disabling the feature. It must solve both:

1. **Premature mode-transition verification timeout:** the five-second window begins before a potentially slow HID command write, so actual PnP target verification receives substantially less than five seconds.
2. **Unrecoverable first-start acquisition failure:** after an otherwise recoverable PID re-enumeration miss, the host tears down VIIPER, returns before starting the existing device-arrival watcher, and has no valid path to finish the first ownership/presentation commit when hardware settles later.

Implement this inside the existing physical owner, mode controller, Runtime host, VIIPER presentation owner, and single Windows Device Arrival watcher. Do not add a service, polling watchdog, new controller authority, persistent recovery journal, generalized manager, speculative race defenses, or an automatic extra PID1902/PID1901/PID1902 cycle.

**Product assumptions:** one Windows user; one interactive session; no Fast User Switching, RDP, or multi-session support. Standalone Full1902; **CTW integration is out of scope**.

## 2. Mandatory architecture precedence / source reading

Read current repository code before making edits. The following documents are authoritative, in this order where applicable:

1. **docs/Full 1902 Implementation/README.md** — precedence and A2VM exception.
2. **docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md** — mandatory Disabled-mode HidHide baseline / startup authority.
3. **docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md** — persistent authority and PID1901 stock restoration.
4. **docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md** — Full1902 physical ownership, lifecycle, VIIPER output.
5. **docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md** — highest-run-level Runtime process.
6. **docs/work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md** — A2VM one-boot prime behavior and fail-close proof.
7. **docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md** and **PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md** — existing *previously owned* input recovery and Device Arrival semantics.

This hotfix extends the **initial ownership acquisition** case, which PR8/PR10 intentionally did not support. It must not weaken any existing owned-session recovery contract.

Read the following code and tests:

- **src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs**
- **src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs**
- **src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs**
- **src/SteamInputAddonforClaw/Prerequisites/BootSession.cs**
- **src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs**
- **src/SteamInputAddonforClaw/Controllers/Detection/WindowsDeviceArrivalWatcher.cs**
- **src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs**
- **tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs**
- **tests/SteamInputAddonforClaw.Tests/MsiClawModeControllerDiagnosticsTests.cs**
- **tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs**
- **tests/SteamInputAddonforClaw.Tests/WindowsDeviceArrivalWatcherTests.cs**
- **tests/SteamInputAddonforClaw.Tests/BootSessionTests.cs**
- **tests/SteamInputAddonforClaw.Tests/Full1902WinGSuppressionAuthorityTests.cs**

Inspect related tests that assert the Runtime startup code's current statement order. Change only those assertions genuinely affected by this fix.

## 3. Field evidence: concrete reproduction, not a hypothetical race

All timestamps are local KST, **2026-10-10**. Use these specific Drive logs as source evidence; if a new log is available, prefer its latest timeline:

| Session | Log | Verified facts |
| --- | --- | --- |
| Prior success, 0.1.351.0 | https://drive.google.com/file/d/1FSxmOzrYQPt2FTmn77uVjH4f2r19VI1X/view | 05:01: physical PID1902 acquisition succeeded with ModeWriteIssued=False; Xbox360 publisher started |
| Successful 0.1.352.0 boot prime | https://drive.google.com/file/d/1BX27ARaxfgznDl2hRxX_FQ5pYFOaMYzA/view | 15:29: A2VM 8 PID1902 -> 1901 -> 1902 verified; Xbox360 attached |
| First 0.1.352.0 failure | https://drive.google.com/file/d/17hDu2lpS1o2Bipbg5u9W5As-0ydHrpyo/view | 15:34: PID1902 -> PID1901 succeeded; second PID1901 -> PID1902 HID write succeeded at 15:35:00.701; target PID1902 absent through 15:35:04.202; startup failed as Pid1902TransitionFailed:TargetDeviceDidNotAppear |
| Successful same-boot Restart Addon | https://drive.google.com/file/d/1m3USspNLvq2NzkR7FQopp52Fz1jEfZR9/view | 16:12: already PID1902; A2VM attempt AlreadyClaimed; ModeWriteIssued=False; Xbox360 attached |
| **Newest failed Windows boot** | https://drive.google.com/file/d/1rXzbZSOqGoung1_MoEmcr93Q8vxphx50/view | 16:15: initially PID1902 with Strong identity; once-per-boot A2VM prime Claimed; first PID1902 -> PID1901 command was a verified 64-byte HID write at **16:15:28.379**; old PID1902 disappeared, but target PID1901 remained absent through **16:15:31.647**; BootRumblePid1901TransitionFailed:TargetDeviceDidNotAppear |
| **Newest successful Restart Addon** | https://drive.google.com/file/d/1glxDMnK_fDox-5oSZnXKNT8de-OEo-n7/view | 16:16: starting physical mode was **PID1901/XInput** with Strong identity; attempt marker AlreadyClaimed; normal single PID1901 -> PID1902 succeeded at 16:16:56.492; first valid DirectInput state, HidHide baseline, Xbox360 attachment and publisher success at 16:16:57.863 |
| VIIPER evidence | https://drive.google.com/file/d/1-BbfM-JavwDpcJ-YvmDz98GjHKy7pgt1/view | 16:15 failed startup removed typed devices without an Xbox360 attach; 16:16 successful startup attached Xbox360 device 1-2 with native IOCTL |

**Quantitative mode timeout evidence:** In the 16:15 failure, first transition poll reports ElapsedMs=1762 while SinceCommandWriteMs=24. Therefore about **1.74 seconds** of the nominal five seconds were consumed before the successful write. The target was checked for only approximately **3.27 seconds** after the write. In the earlier 15:35 failure the reverse transition likewise timed out approximately 3.50 seconds after its successful write. The failure affects **both native-mode directions**, not just a specific A2VM control HID endpoint.

**What this does and does not prove:**

- Proven: mode command writes succeeded; old PID disappeared; target did not appear in the *existing shortened observation window*; initial acquisition returned failure; VIIPER was torn down; no initial PnP recovery watcher was started.
- Strongly supported: firmware/Windows eventually finished the requested mode transition, because the later app restart observed the requested target PID (most clearly PID1901 at 16:16).
- Not proven: the precise device-arrival timestamp after a timeout, or whether the underlying delay came from Windows PnP, firmware, or driver scheduling. Do **not** claim a particular firmware bug or assume the target always appears within five full seconds.
- No evidence implicates VIIPER transport attach or HidHide readback as the initiating fault. Both succeed on later normal acquisition.
- Overlay warm-up timeout in the 16:15 failed session is a separate observation; do not make it a controller acquisition blocker without a concrete causal link.

## 4. Verified source-code failure chain

### 4.1 MsiClawModeController — write latency consumes re-enumeration time

Current implementation, around lines 67–182:

~~~csharp
private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

var started = _now();
var deadline = started + _timeout;

// HID WriteAsync happens here, including device enumeration and native HID write...
if (await writer.WriteAsync(control!, target, cancellationToken))
{
    commandWrittenAt = Stopwatch.GetTimestamp();
    break;
}

// PnP polling uses the SAME deadline computed before WriteAsync:
while (_now() < deadline)
{
    // confirm old PID disappeared and exactly one target logical control group appeared
}
~~~

This verifies the real startup cutoff before the target settles. Merely increasing the one global timeout conceals this contract error.

### 4.2 MsiClawAddonPhysicalOwnership — an optional boot-prime miss aborts initial ownership

Current **AcquireCoreAsync**:

- lines approximately 249–313: once-per-boot A2VM marker and optional first PID1902 -> PID1901 transition;
- lines approximately 297–300: a failed/expired first transition returns **BootRumblePid1901TransitionFailed**;
- lines approximately 315–333: normal PID1901 -> PID1902 acquisition, also used for the second half of the prime;
- lines approximately 328–329: failed/expired second transition returns **Pid1902TransitionFailed**;
- first healthy DirectInput state and exact HidHide proof come only **after** both transitions succeed.

This fail-close behavior is correct **for virtual publication**, but the caller currently treats it as terminal for the whole Runtime lifetime.

### 4.3 AddonProcessHost — failure retires canonical VIIPER and skips the watcher

At **TryStartDisabledModeControllerAsync**, around lines 968–1027:

~~~csharp
var acquired = await owner.AcquireAsync(_startupCancellationTokenSource.Token);
if (!acquired.IsOwned)
{
    await presentation.ReleaseForCenterMEnableAsync(_startupCancellationTokenSource.Token);
    return;
}

// Much later, only in the success branch:
StartControllerDeviceArrivalWatcher();
await presentation.AttachInitialAsync(source, snapshot, token);
~~~

**ReleaseForCenterMEnableAsync** calls **RetireAsync("CenterMEnable")**, whose implementation fully tears down canonical VIIPER; the misleading "CenterMEnable" label in this failure path does **not** mean Center M was activated.

**OnControllerDeviceArrived** currently always calls the PR8/PR10 *owned* recovery scheduler. **RecoverLostInputAsync** requires a previously committed strong physical identity and owned target; immediately reusing it for an uncommitted startup fails with OwnerNotCommitted.

**ReconcileControllerPresentationAsync** likewise returns immediately when LiveInputSource is null. This explains why later Resume notifications do not repair the failed boot.

## 5. Required implementation A — correctly budget both transition phases

Change **MsiClawModeController.SwitchModeAsync** so:

1. Preserve the existing **bounded HID write / source re-resolution window**. Do not allow infinite write retries and do not loosen control-HID identity checks.
2. Once a real command write reports success, start a **fresh bounded PnP re-enumeration verification window**. Its deadline must be relative to the successful command write, not to SwitchModeAsync entry.
3. Apply this to **both** XInput and DirectInput targets and all callers using the same mode controller, including the explicit manual re-arm and stock-safe PID1901 restoration.
4. Preserve exact success proof: successful command write, old PID disappearance, exactly one target logical control group, verified source and target topology. Retain target PID vs target control candidate diagnostics; no "PID merely appeared = success" shortcut.
5. Preserve cancellation, failure reason categorization, source ambiguity / invalid topology fail-close, and normal mode writer selection behavior.
6. Keep the current default verification budget **five seconds after the confirmed write** unless a directly supported source contract requires otherwise. Do not add arbitrary multi-minute waits. If the existing constructor timeout is used by tests, document how it now independently bounds each phase.

Illustrative logic only; adapt carefully to the existing injected clock, writer retry loop, and result metadata:

~~~csharp
var writeDeadline = started + _timeout;

// Existing verified-source resolution and bounded writer loop.
// On a successful WriteAsync, capture commandWrittenAt (monotonic stopwatch).

var verificationDeadline = _now() + _timeout;
while (_now() < verificationDeadline)
{
    // Existing exact PnP proof, using the same physical/logical predicates.
    // Existing cancellation and bounded poll delay.
}

// Same TargetDeviceDidNotAppear / OldDeviceDidNotDisappear fail-close result.
~~~

The purpose is **not** to guarantee hardware enumeration by a deadline. It is to give target PnP verification the correctly budgeted time and then let the initial-acquisition recovery path handle a legitimately later device arrival.

### Required transition diagnostics

Retain current per-poll DEBUG evidence and emit a concise, once-per-transition completion/failure record at a level usable in production logs, including:

- SourceMode and TargetMode;
- whether the command write succeeded;
- elapsed time before successful write;
- elapsed time since successful write;
- old PID disappeared / target PID present / exact target topology proven;
- resulting status and failure reason.

Avoid new high-frequency INFO polling.

## 6. Required implementation B — recover a *first* PID transition timeout without restarting Runtime

### 6.1 Bound the admission to actual recoverable cases

A failed first acquisition must **never** be reported as owned and must **never** attach a virtual controller until the existing full acquisition proofs pass. But a failure caused by known transient mode re-enumeration/target-not-yet-present should be eligible for a future independent **Device Arrival** wake-up and a fresh ordinary **AcquireAsync** attempt.

The evidence-backed cases at minimum are:

- **BootRumblePid1901TransitionFailed:TargetDeviceDidNotAppear**
- **Pid1902TransitionFailed:TargetDeviceDidNotAppear**

Consider genuinely equivalent initial PnP not-yet-present results only if the existing typed status/proof makes them unambiguous. Do **not** blindly retry all failed acquisition reasons. In particular, refuse automatic retry of:

- ambiguous or insufficient physical identity, multiple physical roots, wrong control usage;
- verified invalid topology or changed current controller authority;
- unproven DirectInput cleanup / native resource ownership;
- HidHide write/readback failure, suppression failure, VIIPER Unsafe / teardown failure;
- explicit Center M Enable transition, process shutdown, or cancellation.

If there is a small existing structured status seam, prefer that over parsing arbitrary human-readable log text. A narrow owner/result predicate is acceptable. Do not create a second authority model to classify failures.

### 6.2 Reuse exactly one physical owner and one Device Arrival watcher

Keep the **same MsiClawAddonPhysicalOwnership** and **same MsiClawAddonPresentation** instances while awaiting a recoverable first-start PnP return.

For an eligible failed initial acquisition:

1. Leave presentation **unattached** and publisher **stopped**; do not publish stale/placeholder input.
2. Keep the already-initialized, **Ready** canonical VIIPER Runtime available for eventual first attachment. **Do not call ReleaseForCenterMEnableAsync on this eligible defer path:** that closes VIIPER and makes the later first attach impossible.
3. Start the **existing** WindowsDeviceArrivalWatcher even though initial ownership has not yet committed. It is only a trigger; it is not proof of the controller's identity.
4. After the watcher is registered, perform **one immediate fresh-state recheck/acquisition** (or a semantically equivalent bounded admission attempt), covering a PID target that arrived between the original timeout and registration. If still absent, leave the Runtime available and await a later genuine Device Arrival.
5. On Device Arrival, request one serialized initial **AcquireAsync** attempt using the existing host physical-ownership scheduling seam and physical owner gate. **Do not use RecoverLostInputAsync** for never-owned startup.
6. A2VM boot attempt marker remains consumed. **Do not repeat the optional PID1902 -> PID1901 -> PID1902 prime** during retry. Starting at PID1901 legitimately requires its ordinary **single** PID1901 -> PID1902 acquisition; starting at PID1902 requires no extra native-mode write.
7. Coalesce overlapping startup/arrival requests using the existing host synchronization and one in-flight task where practical. Respect the already-existing deferred Device Arrival behavior for routine PnP notifications; do not require epoch/barrier/manager machinery solely for improbable interleavings.
8. When first ownership finally commits, the single watcher naturally becomes the normal PR8/PR10 owned-session recovery trigger. Avoid starting a duplicate watcher.

**Important actual lifecycle condition:** the arrival may occur during a mode transition or the immediate post-failure admission. The solution must not discard the *only* practical recovery signal. Starting the existing watcher before the fresh recheck, and using existing one-in-flight/deferred-arrival semantics, is sufficient; no polling daemon is justified.

### 6.3 Complete the entire first attach, not merely DirectInput acquisition

The initial success continuation currently lives below the early-return branches of TryStartDisabledModeControllerAsync. A successful delayed AcquireAsync must execute the **same single initial-commit continuation**, preserving:

1. live input source running and first valid DirectInput state;
2. exact primary PID1902 + strong physical identity + HidHide baseline verified;
3. motion reader / model setup under the existing presentation lifecycle;
4. saved controller LED and vibration-strength apply after verified PID1902 ownership;
5. **Win+G suppression arm and proof before any virtual attach**;
6. fresh Steam/BPM snapshot for correct first Xbox360 vs SteamDeck selection;
7. one initial VIIPER attach and publisher start; exactly one active typed device;
8. existing OEM1/WING front-button Runtime creation and callbacks, without duplicates.

Factor the existing successful tail into **one narrow host helper** if necessary, called by both immediate success and deferred initial success. This is preferable to copying dozens of lines. Do not introduce a new high-level ControllerLifecycleManager or duplicate presentation authority.

When initial ownership has not yet succeeded, ReconcileDesiredPresentationAsync is not a substitute for the entire initial-commit continuation; it cannot independently set up missing host-owned startup pieces. It may be used inside a correctly completed continuation only if its existing preconditions and actual first-attachment behavior are proven.

### 6.4 Keep unrelated failure paths and teardown strict

- Do not generally remove ReleaseForCenterMEnableAsync from all failure branches. Limit the new deferred path to specifically recoverable initial PnP state. Existing irrevocable fail-close paths must remain safe.
- Preserve process-shutdown order: watcher disposed / new work blocked; pending initial acquisition and owned recovery drained; publisher/VIIPER torn down **before** physical DirectInput owner disposal.
- Preserve real Center M Enable-and-Restart: fail-close virtual output, restore verified stock PID1901, normalize HidHide as specified by current authority policy, and do not permit later deferred startup acquisition.
- Preserve unsafe native cleanup blocking and explicit cancellation.
- If VIIPER becomes Unsafe or can no longer safely attach, do not reconstruct it silently with a second owner; fail closed.
- Sleep/Hibernate/Resume must not trigger another boot rumble prime. If a pending initial acquisition survives a normal Resume, it may proceed only through an existing valid fresh-state/authority proof and safe presentation admission; never resume a publisher against a missing source.
- No extra retries of manual rumble restore, no spurious force-mode cycle on unrelated PnP arrival, no Center M startup-root mutation.

## 7. Tests — software-executable, mandatory for local Codex

Extend current unit/integration-style tests with deterministic fake clocks, fake PnP sequences, fake writers, and existing host seams. **No physical MSI Claw is required for PR acceptance.**

### 7.1 Native mode timeout tests

For **both** PID1902 -> PID1901 and PID1901 -> PID1902:

- Simulate a successful but slow writer (e.g. around 1.7 s before WriteAsync completes) followed by target arrival >3.3 s but <5 s after write. The transition **must now succeed**, including all old/target proof flags.
- No target within the entire new post-write verification window still yields TargetDeviceDidNotAppear, with no false ownership success.
- Source ambiguous / wrong target HID usage / old PID still present / multiple logical target roots remain fail closed.
- Command repeatedly failing remains bounded by the original write phase. Cancellation still interrupts observation.
- Keep existing MsiClawModeControllerDiagnosticsTests expectations for target PID visibility vs strict control topology.

### 7.2 Boot marker and physical owner tests

- New A2VM 8 or 7 Windows boot starting PID1902: marker Claimed -> exactly one optional real cycle; successful ownership after both proven transitions.
- First optional transition timed out, later first-start reacquisition: marker AlreadyClaimed -> no optional reverse cycle; normal one-way PID1901 -> PID1902 if actual mode is PID1901.
- Second optional transition timed out, later first-start reacquisition: no repeated optional cycle; if the hardware is now PID1902, attach without another mode write.
- Existing CG3EM/unknown model path never primes automatically.
- The same-boot Restart Addon, Suspend/Resume, and unrelated PnP arrival never cause an extra optional rumble cycle.
- Never treat a target merely present, without exact logical/physical proof, as ownership committed.

### 7.3 Host deferred-initial-acquisition tests

Verify a recoverable startup failure:

- does **not** attach Xbox360/SteamDeck prematurely;
- does **not** close a healthy canonical VIIPER runtime;
- starts exactly one existing Device Arrival watcher;
- performs one fresh post-registration check to cover late arrival;
- completes ownership and the whole initial commit after a later real Device Arrival, without restarting Runtime;
- proves suppression before first virtual attach; applies LED/vibration once; starts front-button callbacks once; selects current Steam/BPM presentation;
- switches subsequent Device Arrival semantics back to existing previously-owned recovery and prevents a second initial attach.

Verify nonrecoverable startup failures remain fail-closed. Verify shutdown/Enable-and-Restart cancels pending initial work and tears down once. Verify already-owned PR8/PR9/PR10 recovery, physical loss, PID1901 drift reclaim, HidHide restoration, VIIPER teardown, and failed native cleanup behavior are unchanged.

Avoid brittle tests demanding a particular private helper name or unnecessary internal state. Check observable lifecycle ordering and effects.

## 8. Logging / docs / acceptance

Add a compact event describing a **deferred initial acquisition**: initial failure reason, mode write issued, watcher admission/result, and whether an immediate or Device Arrival retry was requested. Add a **deferred initial acquisition completed** event with fresh PID mode, whether normalization wrote a command, ownership verified, virtual presentation, and publisher outcome. On a failed retry, log the concrete reason without repeatedly emitting unrelated polling information.

Do not log **CenterMEnable** as if Center M was actually enabled for recoverable first-start failure; this path must no longer run that teardown.

Update the narrow current A2VM boot work order / authoritative Full1902 text **only if implementation behavior needs a clarification**: a proven timeout still fails closed for output but allows a later safe fresh-state initial acquisition without re-running the optional boot rumble prime. Do not rewrite unrelated old historical work orders.

**Merge-ready acceptance:**

- The 16:15 pattern cannot fail permanently merely because the PID1901 device appears after the original three-second effective observation period.
- The 15:35 reverse-direction pattern receives the same properly budgeted verification and late-arrival recovery.
- The normal 0.1.351-style already-PID1902 path and the A2VM 16:16 normal one-way normalization remain intact.
- No virtual output without proven PID1902 DirectInput, HidHide, suppression, and native owner readiness.
- One owner, one watcher, one final teardown path; no duplicate VIIPER/session/publisher.
- A2VM boot prime stays once per actual Windows boot; physical PID never intentionally reverts to PID1901 at normal shutdown.
- Existing automated build/tests pass; meaningful new regression tests pass.
- **Real-device post-merge verification is the user's responsibility and is explicitly not a coding-PR blocker.**

## 9. Suggested local Codex sequence

1. Confirm current main source/tests and the two 16:15/16:16 runtime logs.
2. Fix MsiClawModeController's write vs post-write verification deadlines; add exact focused tests.
3. Implement narrowly scoped **uncommitted first-start PnP retry** using existing owner/watcher and VIIPER Ready instance.
4. Reuse the single first-acquisition success continuation; preserve suppression, exact HidHide, LED/vibration, front-button, and presentation ordering.
5. Add host/owner software regression tests, check cleanup and concurrency only for realistic startup/PnP/shutdown/Resume paths.
6. Run project-targeted and relevant full automated tests; prepare one PR with a short behavior summary, test evidence, and updated documentation only where appropriate.

**Do not treat this as a justification for a new general lifecycle framework. The failure is concrete: an incorrectly shared timeout deadline plus no resumption of first acquisition after real PnP delay.**
