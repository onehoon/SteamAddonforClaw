# Work Order — P2-A: Prioritize the First Full1902 Presentation Before LED / Vibration Settings

**Date:** 2026-10-11  
**Repository:** `onehoon/SteamAddonforClaw`  
**Product:** Standalone Full1902 Steam Addon for Claw  
**Reviewed baseline:** `main` at `6c33dc8b1eaa0b43b7e5078f6415892c44bf40cc` (PR #746 squash merged)  
**Implementation:** One small, independent local-Codex PR; English work order  
**Targets:** Both **EX/CG3EM** (`msi.claw.cg3em`) and **A2VM** (`msi.claw.a2vm.7`, `msi.claw.a2vm.8`), retaining their existing hardware capability gates  
**Hardware checks:** The user performs hardware/boot validation after merge; **hardware testing must NOT be required of Codex or used as a review/merge blocker**.

## 1. Outcome

The first game-facing virtual controller must become available **as soon as the existing mandatory Full1902 authority, physical PID1902, DirectInput, HidHide, VIIPER, and Win+G suppression requirements are satisfied**.

Two late-added, optional, persisted hardware settings currently delay initial Xbox360 or SteamDeck publication:

~~~text
Owned live PID1902/DirectInput
  -> await LED static-profile apply
  -> await vibration-strength pair apply
  -> prove Win+G suppression
  -> attach Xbox360 OR SteamDeck
  -> start virtual input publisher
~~~

**P2-A changes only the position of those two awaits** relative to the first presentation. It does not skip the persisted settings; it runs the same existing best-effort apply calls **after the first presentation attach/publisher startup**.

Target initial path:

~~~text
Existing Disabled-mode startup/admission, exact HidHide baseline, VIIPER Ready
  -> physical owner AcquireAsync -> Owned, live first DirectInput state
  -> Win+G suppression armed/proven
  -> current Steam/BPM snapshot
  -> AttachInitialAsync(Xbox360 OR SteamDeck)
      -> typed device attach + neutral + matching publisher Start
  -> existing gyro/motion-reader reconcile
  -> existing front-button runtime composition
  -> await persisted LED apply [optional; after publication]
  -> await persisted vibration-strength apply [optional; after publication]
  -> return existing initial commit result
~~~

There is **no hard-coded Xbox360-first intermediate attach**: the current native Steam/BPM state chooses exactly one presentation. Do not attach Xbox360 and immediately switch to SteamDeck when BPM is already active.

## 2. Measured evidence and limits

Use the previously reviewed Runtime logs **as comparative evidence, not guaranteed performance targets**:

| Model / representative process | Startup context | Owned PID1902 -> first virtual publisher | Observed ordering |
| --- | --- | ---: | --- |
| EX/CG3EM, 2026-10-08 23:42:15, Runtime 0.1.345.0 | Existing PID1902; application restart | ~389 ms | Owned 23:42:17.904, LED 18.082, vibration 18.271, X360 published 18.293 |
| A2VM 8, 2026-10-11 11:53, Runtime 0.1.357.0 | Windows boot / longer native startup | ~1.47 s | Owned -> LED -> vibration -> selected first presentation |
| A2VM 8, 2026-10-11 11:55, Runtime 0.1.357.0 | Different native startup path | ~2.82 s | Owned -> LED -> vibration -> selected first presentation |

Evidence links:

- EX: https://drive.google.com/file/d/1cSmCASUUTXEAddMiPBpHFz5hNWzp2D29/view
- A2VM 11:53: https://drive.google.com/file/d/1zLGLmBjdZlIrvzMp81qyZlP9v_e_u4CD/view
- A2VM 11:55: https://drive.google.com/file/d/1-sDj1F4pa3wTuecPLinfGDzvSY8M1FSj/view

The full Owned-to-publisher gap includes small amounts of Win+G/presentation work, **not exclusively HID settings writes**. Moving the settings means the publisher no longer waits for that work; it does **not** remove their underlying execution time or prove an identical wall-clock saving.

EX's example is a warm application restart without a mode switch; A2VM boot samples involve model-specific PID1901/PID1902/PnP paths. Do not use these as a like-for-like device-speed benchmark.

This PR does not optimize the **earlier** Runtime initialization/XBOX initial scan, A2VM boot-rumble prime, PID1901 <-> PID1902 transitions, or admission/topology waits. Those belong to separately scoped work.

## 3. Mandatory design/source references

Read the **current** source and the following documents; where old historical work orders conflict, active Full1902 authority and current shipped code take precedence.

1. `docs/Full 1902 Implementation/README.md` (document authority order; A2VM boot exception).
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`, especially initial presentation policy (§14).
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
6. `docs/work-order/FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md` (historical feature behavior).
7. `docs/work-order/FULL1902_CONTROLLER_VIBRATION_PRODUCTION_PERSISTENCE_LIFECYCLE_WORK_ORDER_2026-10-04.md` (historical feature behavior).
8. `docs/work-order/2026-10-09_A2VM8_VIBRATION_AND_LED_PRODUCTION_ENABLEMENT_WORK_ORDER.md` (A2VM evidence and scoped enablement).
9. `docs/work-order/2026-10-11_A2VM_V01357_WINDOWS_SESSION_END_PID1901_P0_P1_REPAIR_WORK_ORDER.md` (separate, completed P0/P1 scope; must not be changed).

Read these production paths together:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`:
  - `TryStartDisabledModeControllerAsync`
  - `CompleteInitialControllerOwnershipAsync`
  - `RunInitialControllerAcquisitionAsync`
  - `ApplyOwnedControllerLedSettingsAsync`
  - `ApplyOwnedControllerVibrationSettingsAsync`
  - `RecoverOwnedControllerPhysicalInputAsync`
  - `OnPowerResumeObserved` and `ReapplyOwnedControllerHardwareSettingsAfterResumeAsync`
  - `StartDeferredRuntimeStartup` / shutdown lifecycle
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`: `AttachInitialAsync`, `AttachXbox360Async`, `AttachSteamDeckAsync`, publisher startup and failure handling
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`: existing PID1902 ownership and HidHide proof
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationStrengthClient.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationProfilePolicy.cs`

Important **code-vs-historical-doc distinction**: the current `MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified` includes `cg3em`, `a2vm.7`, and `a2vm.8`. An older A2VM8 enablement work order described A2VM7 as unsupported at that time. **Do not adjust the currently shipped model policy in P2-A**. LED remains strictly firmware/model-gated by its current implementation.

## 4. Precise implementation change

### 4.1 One shared initial-continuation method

At the reviewed baseline, `CompleteInitialControllerOwnershipAsync(InitialControllerAcquisitionContext context, string trigger)` is the **single shared continuation** for both:

- Immediate startup after `owner.AcquireAsync` returns Owned; and
- Deferred initial acquisition/re-enumeration through `RunInitialControllerAcquisitionAsync`.

Therefore change **this method once**, not separate EX and A2VM branches.

The current code, simplified:

~~~csharp
var source = context.Owner.LiveInputSource;
if (source is null || !source.IsRunning) { /* current fail-close */ }

// Current, incorrectly critical-path optional work:
await ApplyOwnedControllerLedSettingsAsync(
    context.StartupSettings.ControllerLed,
    _startupCancellationTokenSource.Token).ConfigureAwait(false);
await ApplyOwnedControllerVibrationSettingsAsync(
    context.StartupSettings.ControllerVibration,
    trigger,
    _startupCancellationTokenSource.Token).ConfigureAwait(false);

// Existing initial-acquisition freshness check
// Existing EnsureAddonAuthorityWinGSuppression() fail-close
// Existing StartControllerDeviceArrivalWatcher()
// Existing CapturePresentationSnapshot() + deferred freshness check
var presentationResult = await context.Presentation.AttachInitialAsync(
    source, snapshot, _startupCancellationTokenSource.Token).ConfigureAwait(false);

// Existing ReconcileMotionReadersAsync, front-button composition
// Return InitialControllerCommitResult
~~~

**Required revision:** remove only the two early apply awaits and their now-misleading ordering comment. Preserve the exact pre-attach live source, deferred ownership, Win+G, watcher, Steam/BPM snapshot, and `AttachInitialAsync` flow. Insert the same LED then vibration awaits **near the end of the method, after `AttachInitialAsync`, the first-presentation log, motion-reader reconciliation, and front-button runtime composition, but before the existing return**.

Suggested end-of-method excerpt (adapt surrounding code exactly; do not duplicate the calls):

~~~csharp
var presentationResult = await context.Presentation.AttachInitialAsync(
    source, snapshot, _startupCancellationTokenSource.Token).ConfigureAwait(false);

AppLog.Info("ControllerPresentation", "First presentation attach completed.",
    ("Succeeded", presentationResult.Succeeded),
    ("Presentation", presentationResult.Presentation?.ToString() ?? "None"),
    ("Reason", presentationResult.Reason),
    ("Trigger", trigger));

await ReconcileMotionReadersAsync(
    trigger == "Startup" ? "StartupPresentation" : "DeferredInitialPresentation")
    .ConfigureAwait(false);

// Keep the existing feature-local MsiClawFrontButtonRuntime.Create block HERE,
// with its current exception containment and before the optional HID writes.

// Optional persisted physical settings: these do NOT gate initial native attach,
// publisher startup, gyro admission, or front-button runtime composition.
// Existing helpers independently guard current Center M authority, live owned
// physical identity/model, exact control HID and cancellation.
_startupCancellationTokenSource.Token.ThrowIfCancellationRequested();
await ApplyOwnedControllerLedSettingsAsync(
    context.StartupSettings.ControllerLed,
    _startupCancellationTokenSource.Token).ConfigureAwait(false);
await ApplyOwnedControllerVibrationSettingsAsync(
    context.StartupSettings.ControllerVibration,
    trigger,
    _startupCancellationTokenSource.Token).ConfigureAwait(false);

return new(true, presentationResult.Presentation?.ToString(),
    presentationResult.Succeeded, presentationResult.Reason);
~~~

**Do not gate those two calls solely on `presentationResult.Succeeded`**. The old behavior applied these physical settings once when PID1902 ownership was healthy even if the subsequent virtual attachment failed. This work order moves **their order**, not their physical-owner eligibility. The existing per-feature owner/authority/identity guards decide whether a write is still allowed. However, a cancelled/stale/dead PID1902 session must not initiate any new HID write, and a failed/unsafe presentation must never be upgraded into a success merely because cosmetic settings wrote successfully.

Do not reorder LED versus vibration; the existing relative sequence is LED first, vibration second. Do not move these settings to run before `EnsureAddonAuthorityWinGSuppression()` through a different side path.

### 4.2 Keep the work owned and awaited (no new detached task)

**Important:** `await` itself is not the bug. Awaiting **before the first game-facing publisher exists** is the bug.

The two calls remain awaited in the **existing** controller-initialization task, but now only **after** the first attachment/publisher start has completed. That ensures their exceptions/cancellation remain handled by existing startup/recovery/disposal ownership rather than an untracked fire-and-forget HID task.

Do NOT create:

- a new Task.Run for each setting;
- a new LED/Vibration background manager or queue;
- an extra lock, epoch, startup-phase state machine, or independent PID1902 write owner;
- separate EX/A2VM implementations;
- a repeat/timeout/retry loop or a new hardware-discovery path.

The existing `MsiClawVibrationStrengthClient` owns its current transaction gate. LED and vibration already use exact strong PID1902 control HID resolution and the existing native transport. Preserve those contracts.

Note: `StartDeferredRuntimeStartup()` still awaits the controller startup task before **other deferred** work is launched. Consequently this scoped PR reduces **first publisher availability** latency; it does not guarantee that all UI/profile/ClawHUD background startup milestones become earlier. Avoid overstating total Runtime startup improvement. General Runtime critical-path/XBOX scan work is a separate P2-B PR.

### 4.3 Preserve the attached publisher during optional settings failures

An LED/vibration failure, unsupported firmware/model, temporary HID endpoint unavailable, or cancellation during shutdown must not:

- detach/recreate the Xbox360 or SteamDeck device;
- stop an already-healthy publisher as a response to optional-setting failure;
- attempt a PID mode switch;
- mutate HidHide, Center M ownership, or VIIPER;
- roll back saved settings;
- produce `InitialPresentationAttached` or `PublisherStarted` when the actual native attach failed.

The current host helpers already contain non-cancellation exceptions and return feature-local success/failure. Retain that. Keep current cancellation propagation/outer cleanup behavior; do not add broad `catch (Exception)` that masks an actual controller owner failure.

The virtual publisher's existing feedback callback and physical-rumble STOP lifecycle remain owned by `MsiClawAddonPresentation`. These optional profile writes use the separate control-HID setting path, must not redirect feedback, and must not create a second rumble transport or generic HID serializer.

### 4.4 Real lifecycle consistency

Follow existing owner/gate/teardown semantics across:

- real physical HID loss / PnP re-enumeration while optional apply is running;
- controlled Runtime Restart / Crash / Shutdown;
- Windows `WM_ENDSESSION(TRUE)` special A2VM-only path from PR #746;
- Center M Enable-and-Restart / uninstall release;
- Sleep/Hibernate/Resume;
- Steam/BPM Xbox360 <-> SteamDeck switching and Overlay pauses.

Do not redesign for implausible instruction-level races. A shutdown/cancellation already admitted through the existing task/cancellation token must prevent **new** feature writes where the current helpers and transport honor cancellation. If device loss makes the exact physical HID unavailable, existing feature-local write failure is acceptable; **never repair it by creating another controller owner or forcing PID1901**.

**Keep the other two LED/vibration lifecycle sites unchanged:**

- `RecoverOwnedControllerPhysicalInputAsync`: it currently reapplies settings after a **real** owned physical session recovery before requesting `PhysicalInputRecovered` presentation reconcile. This is outside P2-A. Do not inadvertently shift recovery order.
- `ReapplyOwnedControllerHardwareSettingsAfterResumeAsync`: existing 500ms settle, LED then vibration, post-resume; unchanged.

Normal Steam/BPM virtual presentation switching must still **not** trigger an LED/vibration reapply.

## 5. Tests — exact existing files and necessary changes

Update rather than remove the old source-level ordering assertions.

1. `tests/SteamInputAddonforClaw.Tests/ControllerLedLifecycleContractTests.cs`
   - Rename/rewrite `Startup_applies_persisted_led_after_owned_input_is_healthy_and_before_presentation`.
   - Require `owner.AcquireAsync` + healthy `LiveInputSource`, Win+G suppression, `AttachInitialAsync`, then LED apply.
   - Ensure recovery-only and power-resume LED tests remain unchanged.

2. `tests/SteamInputAddonforClaw.Tests/ControllerVibrationLifecycleContractTests.cs`
   - Rename/rewrite `Startup_applies_vibration_after_healthy_owned_input_and_before_first_presentation`.
   - Assert the single startup sequence is `Owned/DirectInput` -> Win+G -> first `AttachInitialAsync` -> LED -> vibration.
   - Preserve saved pair, `trigger` argument, model policy and exact control-HID gating.
   - Do not change the existing physical recovery/resume tests.

3. `tests/SteamInputAddonforClaw.Tests/AddonProcessHostInitialControllerAcquisitionTests.cs`
   - Update `Initial_commit_preserves_hardware_and_presentation_order_for_both_entry_paths`.
   - Assert the shared continuation is called exactly once on direct acquisition and via deferred initial acquisition; suppression and freshest Steam/BPM snapshot still occur before first attach; both optional settings occur strictly after the attach and front-button composition.
   - Preserve the existing first-attach count and deferred-context safety tests.

4. Search the test suite for any other source-order assumption placing LED or vibration before `EnsureAddonAuthorityWinGSuppression()`, `AttachInitialAsync`, or `MsiClawFrontButtonRuntime.Create`; update narrowly.

5. Preserve the current unit tests for the LED four-write Static profile, saved Off/color/brightness, vibration one-pair write, no SyncToROM, strong ownership, ambiguous HID, and failure containment. **Do not add a new factory/interface solely to inject a timing test**. Prefer the already-established source-contract assertions here plus existing behavior tests for the unchanged native feature writers.

### Representative order assertions (adapt to existing test helpers)

~~~csharp
var liveSource = continuation.IndexOf("if (source is null || !source.IsRunning)", StringComparison.Ordinal);
var suppression = continuation.IndexOf("EnsureAddonAuthorityWinGSuppression()", StringComparison.Ordinal);
var watcher = continuation.IndexOf("StartControllerDeviceArrivalWatcher()", StringComparison.Ordinal);
var attach = continuation.IndexOf("context.Presentation.AttachInitialAsync(", StringComparison.Ordinal);
var motion = continuation.IndexOf("ReconcileMotionReadersAsync(", StringComparison.Ordinal);
var frontButtons = continuation.IndexOf("MsiClawFrontButtonRuntime.Create", StringComparison.Ordinal);
var led = continuation.IndexOf("ApplyOwnedControllerLedSettingsAsync(", StringComparison.Ordinal);
var vibration = continuation.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(", StringComparison.Ordinal);

Assert.True(liveSource >= 0 && suppression > liveSource
    && watcher > suppression && attach > watcher
    && motion > attach && frontButtons > motion
    && led > frontButtons && vibration > led);
~~~

Use the method-boundary extraction helper already present; prevent false positives from finding other LED/vibration call sites such as recovery/resume.

### Acceptance scenarios for local automated review

- Healthy CG3EM and A2VM supported initial ownership: first virtual publisher is selected/started without awaiting LED/vibration apply.
- Existing Steam/BPM-active launch still selects SteamDeck directly; normal Windows selects Xbox360 directly.
- Deferred initial device-arrival acquisition uses the identical post-attach ordering and does not issue a second initial attach.
- Failed or unavailable optional hardware settings do **not** invalidate an already-started first publisher, change owner authority, roll back settings, or reattach.
- Initial attach failure remains a real `PublisherStarted=false` result and is not hidden by later optional settings; applicable physical-setting writes remain best-effort only under the existing owner gates.
- Center M Enabled, unavailable/weak physical session, failed HidHide, unarmed Win+G and VIIPER not Ready remain fail-closed for virtual attachment.
- Physical recovery, suspend/resume reapply and explicit settings mutations retain their current independent behavior.
- PR #746 session-end PID1901 path remains untouched; no new command on app-only Restart or Suspend.

Run `dotnet build SteamInputAddonforClaw.slnx` and the relevant existing solution tests (or existing repo CI equivalents) on the local source. Code/test regressions can block merge; user-owned hardware timing/functional verification cannot.

## 6. Documentation, delivery and boundaries

- **ONE small implementation PR**; no P2-B changes.
- Prefer one focused production code edit in `AddonProcessHost.CompleteInitialControllerOwnershipAsync`, the three existing contract test files, and a **short** update to the active `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` at the first-presentation section clarifying that LED/vibration are post-first-attach best-effort work. Do not rewrite historical LED/vibration work orders.
- Do not modify native VIIPER, HidHide normalization, mode-transition writers, first 250Hz input/gyro admission, startup-task registration, XBOX session runtime, UI, settings schema, PID1901 restoration, or A2VM boot-rumble marker.
- Keep stock authority, exact owner, and one canonical VIIPER lifetime with existing teardown paths. Avoid optional feature work delaying the first controller publisher again.
- PR description should separate `PublisherStarted` latency from total app-startup latency. Include focused build/test results, and state that actual EX/A2VM startup timing and LEDs/vibration behavior are **post-merge user validation**.

### User-owned post-merge verification (informational, NOT a Codex requirement)

For the actual EX and A2VM devices, compare the timestamp/order of `Physical ownership acquisition completed`, `Full1902WinGSuppressionArmed`, `InitialPresentationAttached`, LED settings apply and `ControllerVibrationSettingsApplied`. Confirm input is available once the first selected Xbox360/SteamDeck publisher starts; LED/vibration happen afterward; normal Restart, BPM first launch, physical loss/recovery, and Sleep/Resume remain healthy. Observe actual times without predeclaring a required number of milliseconds.

## 7. Definition of done

A **small, single-owner ordering change** that puts the first game-facing virtual controller ahead of optional persisted LED and vibration writes for **all existing Full1902 EX/A2VM models**, while preserving all mandatory physical/hardware/authority proofs, exact model eligibility, settings persistence, and realistic lifecycle safety.

**Do not turn P2-A into a broad startup refactor.** XBOX scanning/Runtime startup parallelism belongs to P2-B; A2VM session-end PID1901 restoration was handled by PR #746.
