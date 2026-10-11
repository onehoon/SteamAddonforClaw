# Work Order — A2VM Initial Full1902 Fast Path: HHC/CTW-Level Mode Handoff Without Repeated PnP Proof

**Project:** SteamAddonforClaw, standalone Full PID1902  
**Date:** 2026-10-11  
**Owner:** Local Codex  
**Target:** One focused implementation PR on top of the current main branch  
**Field basis:** `GoogleDrive/Addon/Log/AV2M/1011`, Runtime 0.1.355.0 (A2VM 8)  
**Status:** Work order only; code changes belong in the implementation PR.

## 1. Explicit Product Decision

This product is **not yet released**. Stop retaining expensive initial-connection verification merely because it exists or used to be a safety gate. Use the proven **HHC/CTW practice** of *issue the MSI HID mode command and proceed when the actual required interface becomes usable*.

**The desired product behavior:**

- A2VM 7/8 with **Center M exactly Disabled** reaches a usable **single** SteamDeck/Xbox360 presentation as soon as **one strongly identified physical PID1902 controller**, a **live first-valid-state DirectInput source**, **PID1901 absence**, and the **exact HidHide isolation baseline** are confirmed.
- Do **not** first wait for every intermediate PnP transition to be declared fully successful, then repeat nearly the same proof during the final owned-input acquisition.
- Neither a PID1901 intermediate milestone nor a successful HID command write is itself authorization to attach the virtual controller.
- The A2VM once-per-Windows-boot rumble-prime remains: initial PID1902 may require `1902 → 1901 → 1902`; initial PID1901 requires just `1901 → 1902`.
- The **separate** Windows-session-end PID1901 best-effort restore work order remains independent. Do not implement its Windows shutdown notification handling in this PR:
  `docs/work-order/2026-10-11_A2VM_WINDOWS_SESSION_END_BEST_EFFORT_PID1901_RESTORE_WORK_ORDER.md`.
- **No CTW integration.** HHC/CTW source is comparative research only.

### Non-negotiable realistic safety boundary

Keep one clear controller owner, the Center M Disabled authority guard, strong/unique MSI Claw physical identity, exact target collection, no remaining PID1901 XInput physical gamepad at presentation time, successful DirectInput first input, persistent HidHide owned-target application/readback, and canonical VIIPER readiness/one attached virtual device. These protect concrete normal handheld failures (duplicate gamepad input, no usable controller, wrong device, stale native handle, failed teardown). **Remove duplicate intermediate checks, not this final safety proof.**

Do not engineer theoretical fine-grained race defense, new lock/state/epoch/barrier/manager or additional full state capture to replace the old ones.

## 2. Mandatory Reading / Authority

Read before editing, in precedence order:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Past implementations to supersede **only where expressly described here**:

- `docs/work-order/2026-10-10_A2VM_BOOT_RUMBLE_INTERMEDIATE_PID1901_SETTLE_SIMPLIFICATION_WORK_ORDER.md` (PR #741)
- `docs/work-order/2026-10-10_A2VM_BOOT_RUMBLE_SECOND_LEG_PARTIAL_PID1902_RECOVERY_WORK_ORDER.md` (PR #742)
- `docs/work-order/2026-10-10_A2VM_BOOT_NATIVE_MODE_TIMEOUT_AND_INITIAL_OWNERSHIP_RECOVERY_WORK_ORDER.md`
- `docs/work-order/FULL1902_PRIMARY_PID1902_PNP_FIRST_DIRECTINPUT_SETTLE_OPTIMIZATION_WORK_ORDER.md`

Code:

- `src/SteamInputAddonforClaw/Startup/ControllerTopologyWaiter.cs`
- `src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs` — **Disabled** admission, not Enabled/stock onboarding
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs` — `AcquireCoreAsync`, `ResolveBootRumblePid1901EndpointAsync`, `EnsureDirectInputGamepadModeAsync`, `ResolveDirectInputDescriptorAsync`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs` — source/target command HID resolution, write and **two independent 5-second phases**
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeStateManager.cs` — 5-second transient final snapshot window
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` — `RunInitialControllerAcquisitionAsync`, typed deferred acquisition/Device Arrival, presentation setup
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` — canonical VIIPER owner
- Existing unit/host tests for these components.

HHC direct source: `Valkirie/HandheldCompanion/HandheldCompanion/Devices/MSI/ClawA1M.cs` `Open()` + `SwitchMode()` (single HID write plus fixed settling), `ClawA2VM.cs` inherits it.

CTW source: `onehoon/ClawTweaks-Dev`, `release/v0.3.98.0`, `XboxGamingBarHelper/Labs/ClawButtonMonitor.cs` (`OpenClawInterfaces`, direct command interface acquisition, `SettleAndAcquire` of actual DirectInput joystick) and `MSIClawHidController.cs`.

**Important interpretation:** HHC's fixed 2-second sleep and CTW's fixed ~2.5-second settle are not correctness proofs. In the observed A2VM 8 firmware, the first PID1901 command HID may require **5–8 seconds after the first write**. Therefore **do not copy their fixed sleep duration**. Copy their **simple observable-ready strategy**. CTW also documents a historical double-XInput interval when the virtual pad was attached before physical XInput disappeared; **do not copy that ordering**.

## 3. Measured Bottleneck: 0.1.355.0

Five observed successful boot-prime sessions:

| Clock slice | 09:10 | 09:12 | 09:22 | 09:25 | 09:27 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Runtime process start → VIIPER Ready | 8.79 s | 7.63 s | 7.89 s | 8.47 s | 8.28 s |
| first 1902→1901 write → fresh PID1901 command HID confirmed | 7.49 s | 7.74 s | 6.95 s | 7.36 s | 5.14 s |
| PID1901 confirmed → second 1901→1902 command written | 4.59 s | 2.41 s | 1.57 s | 1.77 s | 2.34 s |
| second write → final PID1902 owned | 3.96 s | 10.90 s | 11.38 s | 10.84 s | 12.40 s |

**Actual improvements to pursue:**

1. `ControllerTopologyWaiter` reports first usable controller topology at ~0.77 s but by default requires **three equal successful samples**; 09:12 reaches Stable at **2.89 s**. This is a ~2-second avoidable *pre-ownership* barrier in an already-supported, Center M Disabled boot. Final ownership separately proves exact hardware and isolation.
2. The first A2VM leg spends 5 seconds in generic `MsiClawModeController` target verification, then potentially another 5-second target-scoped settle. The latter only needs a **fresh unique PID1901 command endpoint, with PID1902 absent**, to send the second command; do not perform two separate phases of similar observation.
3. After the intermediate PID1901 command HID is proven, the next `_switchMode(DirectInput)` **re-enumerates the same source again** before the second write. The measured gap (1.57–4.59 s) is not all proven duplicate enumeration time, but this source reprobe is redundant if the handoff already holds the fresh, uniquely resolved control HID.
4. The second-leg `MsiClawModeController` spends up to 5 seconds proving a PID1902 command-HID transition. If it times out, `AddonProcessHost` starts an **additional deferred `AcquireAsync`**, which again performs final PID1902 snapshot, gamepad-mode readback, DirectInput acquire and HidHide proof. Four of five sessions paid this extra transition-fail/then-reacquire path.
5. For an A2VM initial-acquisition **write to DirectInput that succeeded**, the actual *first valid DirectInput gamepad input* together with strong correct PID1902 identity makes a second standalone GamepadMode-readback a candidate for removal on **this path only**. Do **not** remove startup readback/normalization when the device starts already PID1902 without a new successful DInput write.

**Do not** promise impossible fixed latency improvements: actual physical PID1901 enumeration took 5–8 seconds and cannot be eliminated by code alone. The larger improvement is achieved when the separately planned shutdown restore causes the next boot to start **already PID1901**, avoiding the entire first leg.

## 4. Required Implementation: Two Commands, One Final Readiness Proof

### 4.1 Faster Disabled-boot topology admission (simple prerequisite cleanup)

For the **Center M Disabled** boot admission path only, replace the default **three-consecutive-equal-topology-snapshots** requirement with **first unambiguous usable control-HID observation**. Keep the existing bounded wait when the exact control HID is missing; do **not** convert a temporarily absent Claw into an unconditional Ready.

- Preserve supported-hardware identification, exact Center M Disabled state, package prerequisites and HidHide baseline normalization/readback.
- Preserve strict **Enabled / stock onboarding** topology behavior; do not change the shared default globally if doing so would weaken stock authority changes.
- Retain existing `ControllerTopologyWaiter` and `StartupCoordinator`; a narrow readiness parameter/call-path on the same class is fine, but **no new startup manager/interface hierarchy**.
- One present supported MSI controller with its expected command HID is enough for this *pre-admission* readiness. **Ambiguous/multiple physical roots remain indeterminate**; do not admit them based on just the first matching node.
- As before, final `MsiClawAddonPhysicalOwnership` owns the last word on strong identity, usable input and HidHide targets.

### 4.2 A2VM first leg — only wait until the next command is possible

On A2VM 7/8 initial PID1902 and once-per-real-Windows-boot marker `Claimed`:

1. Capture the current strong PID1902, require Center M Disabled and one exact supported PID1902 command HID. The existing initial admission and marker semantics stay.
2. Write **one** `SwitchMode(XInput)` using the existing writer/source resolution. Do **not** run an additional independent 5-second *complete PID1901 transition verification* before looking for the PID1901 endpoint.
3. From that write, perform **one bounded target-scoped wait** for **one fresh, strongly identified** PID1901 `0xFFA0/0x0001` command HID **and absence of the previous PID1902 device**. Return the endpoint evidence itself, not just a boolean that forces another expensive global source lookup.
4. As soon as that evidence is true, issue **one** `SwitchMode(DirectInput)` on the **fresh PID1901 control HID** through the existing HID writer. **Do not re-run full global `ResolveSource` and then wait for intermediate PID1901 success again.**
5. On missing/ambiguous endpoint or failed first write: keep virtual detached, preserve existing typed Device Arrival recovery when evidence is a normal transient absence, and **do not retry the native write speculatively**.

**No fixed 2-second/2.5-second sleep.** Use a bounded wait that can cover the observed 5–8-second A2VM PID1901 re-enumeration, but progresses immediately when the required endpoint is ready. A single approximately **10-second** bound *measured from the first successful command write* is a reasonable starting ceiling, not a minimum delay.

### 4.3 PID1901→PID1902 — send once, go straight to the final owner

This applies **both** when A2VM initially boots as PID1901 (including a successful preceding-session shutdown preparation), and as the second half of the A2VM optional boot prime:

1. Require one **fresh uniquely resolved** strong PID1901 command HID, Center M Disabled, and one expected physical MSI target.
2. Write **one** DirectInput mode command through the existing writer. Successful **write** means only that the firmware command was submitted; it does not mean the controller is already owned.
3. **Do not spend a separate 5 seconds proving `MsiClawModeController.SwitchModeAsync` reached its full target topology and then call the normal final capture again.** Proceed directly into the one final PID1902/DirectInput acquisition path.
4. Wait for the actual final requirements as part of that *same* bounded physical-ownership operation:
   - PID1901 old physical XInput mode is no longer present;
   - supported, unique **Strong PID1902** physical identity with exact primary DirectInput collection;
   - correct DirectInput device actually acquired and **first valid gamepad input** received;
   - exact persistent HidHide target set applied and **read back compliant**;
   - VIIPER Ready before attaching the one desired virtual presentation.
5. When this path has **a confirmed newly-written DirectInput mode command** and all final actual-input requirements above pass, do not separately spend time on a redundant GamepadMode readback merely to reconfirm the write. When the device was **already PID1902** at startup without this write, retain existing explicit GamepadMode readback/normalization (same for non-boot recovery and developer/manual re-arm).
6. Track a **single bounded final readiness window** from the successful DirectInput write, e.g. a **12-second maximum** rather than sequential `5s transition verification + 5s snapshot wait + 3s descriptor settle`. The window is a ceiling; *immediately publish upon completion of all final proofs*, and do not use a blind sleep. Reuse the existing owner and input-source operations; do not build a second state machine or polling service.
7. If the bound expires with the PID1902 device/child *still normally enumerating*, return the **existing typed initial-acquisition deferral** and let the **same** host's Device Arrival watcher recheck. The marker remains consumed, so no boot cycle/write repeats. If a strong unsafe condition is discovered (wrong/ambiguous device, old PID1901 remains concurrently at final attach, failed cleanup, impossible ownership, HidHide readback mismatch), fail closed and do not attach.

This **replaces the separate second-leg “transition timeout → deferred reacquire” hot path with one final acquisition attempt**. Keep the existing host watcher as the fallback for genuinely late hardware; it is not an unconditional second stage.

### 4.4 Existing code boundaries — simplify, do not clone

Prefer extending the existing `MsiClawModeController` / `MsiClawNativeStateManager` with the smallest **A2VM initial-only verified-source, write-once** primitive needed to reuse the exact PID1901 control HID returned from the first-leg target-scoped observation.

- Reuse `IMsiClawModeWriter` and existing exact HID resolver; **no duplicate Windows HID write implementation**.
- Do **not** globally relax normal `SwitchModeAsync` success predicates: manual restore, EX/CG3EM and explicit stock restore still need their existing contracts.
- All orchestration stays within `MsiClawAddonPhysicalOwnership.AcquireCoreAsync` and the already-existing host first-acquisition path.
- Remove old A2VM initial-only double-settle predicates/pending flags/recheck branches that become **provably unreachable** after the new single final wait. Do not retain a legacy state machine alongside the replacement.
- Preserve existing publisher/VIIPER single ownership, HidHide baseline, PnP return, Sleep/Hibernate/Resume, PID1901 drift recovery, Restart/Crash/Shutdown, stock release and physical cleanup safety.
- Keep the separate shutdown PID1901 preparation work order independent; do **not** add anything on Windows session-end in this PR.

## 5. Test Matrix for Local Codex

Replace tests asserting the **superseded A2VM initial sequence**; retain tests for preserved non-A2VM and real lifecycle contracts.

1. A2VM initial PID1902, boot marker Claimed: exactly **one** XInput write, delayed unique PID1901 command child, exactly **one** DirectInput write on that fresh endpoint, then final PID1902+first-valid-input+HidHide proof → one VIIPER attach. No second generic global PID1901 resolver needed.
2. A2VM initial PID1901 (e.g. after the new optional Windows shutdown restoration): exactly one DInput write, no XInput write and no intermediate 5-second full-transition verification.
3. PID1901 control HID becomes available 1 s / 7 s after write: both proceed **immediately at actual readiness**, no minimum fixed sleep and no prematurely terminal 5-second failure. Absence or identity ambiguity must never lead to a speculative second write.
4. PID1902 parent arrives before exact HID/DirectInput; final owner waits once, no virtual attach while unready, and eventually attaches on proven first input + HidHide, with no duplicate DInput write.
5. Delayed PID1902 past the final single budget: existing Device Arrival deferred recovery keeps VIIPER Ready, boot marker consumed and both native writes **not repeated** on retry; eventual exact Full1902 attach.
6. Old PID1901 still present at would-be final attach, two conflicting physical roots, missing strong identity, incomplete DirectInput, HidHide readback failure, VIIPER unsafe teardown → **no live virtual attach**.
7. A2VM **already PID1902**, boot marker AlreadyClaimed: no extra native write; existing GamepadMode direct-input normalization and ownership path still work. Same-boot Runtime restart unchanged.
8. EX / CG3EM, manual Restore Vibration, Enable-and-Restart release, power suspend/resume and unexpected owned-session recovery: **no semantic changes**.
9. Disabled-boot ready topology on its first observation no longer requires three identical snapshots; missing/ambiguous exact MSI control HID still waits/blocks as appropriate. Enabled/stock onboarding retains prior proof policy.
10. Source command write fails; process shutting down; native HID handle disappears; PnP read failure or incorrect PID after write: no accidental success, no infinite/duplicated retries.
11. Existing production controller/host tests pass after updating *only obsolete A2VM initial-flow assumptions*. Include timing phase logs to prove the second full mode-verify window has been removed.

**Automated build/tests are Local Codex's responsibility. Physical hardware validation is done by the user after merge and MUST NOT be required for implementation completion, CI PASS or PR merge.**

## 6. Minimal Diagnostics and Acceptance

Keep/introduce only **milestone** logs (no 100-ms flood at INFO):

```text
Event=A2vmInitialFastModeCommandWritten   Leg=ToXInput|ToDirectInput
Event=A2vmInitialPid1901HidReady         SinceFirstWriteMs=...
Event=A2vmInitialPid1902InputReady       SinceSecondWriteMs=...
Event=A2vmInitialFastOwnershipCompleted  Outcome=Owned|Deferred|Failed
      WriteCount=0|1|2  FinalPid1901Absent=...  FirstValidState=...
      HidHideCompliant=...  ElapsedMs=...
```

Preserve existing native command/debug traces and failure reasons where still applicable. Do **not** claim actual physical motor vibration success from two successful command writes.

Acceptance:

- [ ] No 3-snapshot wait on already-usable **Disabled** boot topology; strict Enabled stock path unchanged.
- [ ] No first-leg generic 5-second full-transition-verify *followed by* an additional PID1901 endpoint settle. There is **one target-scoped wait** to a usable PID1901 command child.
- [ ] No second-leg generic 5-second full-transition-verify *followed by* a separate full PID1902 ownership attempt. There is **one final PID1902/DirectInput/HidHide admission**.
- [ ] One command write per required leg; final actual-usage proof gates all attaches; no stale control HID, speculative retry or duplicate native mutation.
- [ ] A2VM 7/8 boot only; all normal lifecycle safety and EX/CG3EM unchanged.
- [ ] Existing host Device Arrival fallback preserved; obsolete A2VM-only branches removed rather than carried along.
- [ ] Update `docs/Full 1902 Implementation/README.md` and affected A2VM/Disabled startup portions of the authority architecture to state the **new initial proof path**, preserving stronger rules elsewhere.
- [ ] Run software-only automated tests and CI; physical testing remains the user's post-merge role.

### User-owned post-merge validation (not a blocker)

User may compare A2VM 8 cold-boot time from Runtime start and BPM detection, SteamDeck first input time, vibration, next boot initially PID1901 (if shutdown work order implemented), PID1902 fallback after an interrupted shutdown, same-Windows-session Runtime restart, and Sleep/Resume. For accurate measurement, report both command-write timestamps and final physical/virtual readiness rather than total process launch time alone.
