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

- A2VM 7/8 with **Center M exactly Disabled** reaches a usable **single** SteamDeck/Xbox360 presentation as soon as the **correct currently present PID1902 DirectInput gamepad collection**, a **live first-valid-state DirectInput source**, **PID1901 absence**, and the **exact HidHide isolation baseline** are confirmed.
- **Hardware family/model is proven only once at startup**: `MS-1T42` → A2VM 7; `MS-1T52` → A2VM 8. Do not repeatedly prove that the already-supported machine is an A2VM by reconstructing physical-root/Container-ID/Strong-Identity equivalence at every PID handoff.
- **Intermediate mode handoffs use fixed interface contracts** (VID/PID/Usage, exactly one currently present matching command HID) rather than fresh whole-device Strong-Identity proof. Do not first wait for every intermediate PnP transition to be declared fully successful, then repeat nearly the same proof during final acquisition.
- Neither a PID1901 intermediate milestone nor a successful HID command write is itself authorization to attach the virtual controller.
- The A2VM once-per-Windows-boot rumble-prime remains: initial PID1902 may require `1902 → 1901 → 1902`; initial PID1901 requires just `1901 → 1902`.
- The **separate** Windows-session-end PID1901 best-effort restore work order remains independent. Do not implement its Windows shutdown notification handling in this PR:
  `docs/work-order/2026-10-11_A2VM_WINDOWS_SESSION_END_BEST_EFFORT_PID1901_RESTORE_WORK_ORDER.md`.
- **No CTW integration.** HHC/CTW source is comparative research only.

### Non-negotiable realistic safety boundary

Keep one clear controller owner, the **once-at-startup A2VM model gate**, the Center M Disabled authority guard before native writes, **one unambiguous currently present HID command endpoint per required leg**, the **exact final PID1902 primary gamepad collection**, no remaining physical PID1901 at presentation time, successful DirectInput first input, exact HidHide owned-target application/readback, and canonical VIIPER readiness/one attached virtual device. These protect concrete normal handheld failures (duplicate gamepad input, no usable controller, wrong/stale command interface, failed teardown).

**Do not require separate Strong-Identity, physical-root, Container-ID or full native-state snapshot revalidation during each intermediate transition.** At the *final* DirectInput/HidHide boundary, read only the current device metadata necessary to select the exact supported gamepad collection and current HidHide targets; do not add another independent cross-PID identity-proof phase. A2VM hardware support is already established. Fail closed for **actually ambiguous multiple matching endpoints**, incompatible VID/PID/Usage, invalid final input, PID1901 still present at attachment, or failed HidHide/VIIPER; not for theoretical identity ambiguities that do not change which unique command HID is actually openable.

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

### 2.1 Hardware identity is fixed; complete Windows device paths are not a product constant

The support gate already checks the board model in `MsiClawDeviceModelResolver` against `MS-1T42` / `MS-1T52`. The supported MSI USB interface contract is:

| Device/interface | Match using **fixed prefix/fields** | Avoid using as a hardcoded identity |
| --- | --- | --- |
| MSI vendor | VID `0x0DB0` | — |
| XInput mode | PID `0x1901` | — |
| DirectInput mode | PID `0x1902` | — |
| PID1901 switch-command HID | Usage Page `0xFFA0`, Usage `0x0001` | Full HID instance suffix |
| Supported PID1902 switch-command HID | Usage Page `0xFFF0`, Usage `0x0040` | Full HID instance suffix |
| PID1902 physical DirectInput pad | `HID\\VID_0DB0&PID_1902&MI_00&COL01\\*` (gamepad usage) | Full PnP Instance ID / DirectInput Instance GUID |
| PID1902 auxiliary HidHide collection | Known `COL02` control and `MI_01&COL03` consumer prefixes | Current enumerated instance suffix |

All five inspected 2026-10-11 A2VM 8 boots returned the same full primary HID instance ID and USB physical-root string. **That is observed stability on one machine, not a guarantee** across driver reinstall, PnP rebuild, firmware or Windows changes. Resolve present endpoints by their **fixed interface identity and unique availability**, not by hardcoding the suffix, comparing repeated ancestor chains or persisting a runtime-selected full path as a universal constant.

**Firmware variant caveat:** `MsiClawHardware.IsA2vm230ObservedControlEndpointCandidate` detects a `REV_0230` candidate with `0x0001/0x0040`, but the current native mode-writer contract treats that *alternative* as **unverified**. Do **not** silently assume it is the supported `0xFFF0/0x0040` command HID, pick an arbitrary fallback, or change vendor commands without evidence. This POC simplifies the verified interface path only; if that endpoint is unavailable, defer/fail as before rather than guess.

**What is removed:** repeated cross-mode physical-root equality proofs, repeated Container-ID/Strong-Identity checks for already identified intermediate command endpoints, repeated whole-machine native snapshots, and duplicate PnP-settle proof. **What remains:** a currently present *single* correct command HID per write, actual final DirectInput input, the real PID1901-away condition, and compliant exact HidHide targets before virtual attach.

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
2. The first A2VM leg spends 5 seconds in generic `MsiClawModeController` target verification, then potentially another 5-second target-scoped settle. The latter needs only a **fresh unique PID1901 command HID** to send the second command; do not separately hold the intermediate leg for a full PID1902-disappearance/root/Container proof. **PID1901 absence is checked at the final PID1902 presentation boundary**, not confused with this intermediate handoff.
3. After the intermediate PID1901 command HID is proven, the next `_switchMode(DirectInput)` **re-enumerates the same source again** before the second write. The measured gap (1.57–4.59 s) is not all proven duplicate enumeration time, but this source reprobe is redundant if the handoff already holds the fresh, uniquely resolved control HID.
4. The second-leg `MsiClawModeController` spends up to 5 seconds proving a PID1902 command-HID transition. If it times out, `AddonProcessHost` starts an **additional deferred `AcquireAsync`**, which again performs final PID1902 snapshot, gamepad-mode readback, DirectInput acquire and HidHide proof. Four of five sessions paid this extra transition-fail/then-reacquire path.
5. For an A2VM initial-acquisition **write to DirectInput that succeeded**, the *first valid input from the exact supported PID1902 DirectInput collection*, plus the final no-PID1901 and HidHide proof, replaces the extra standalone GamepadMode-readback on **this path only**. Do **not** remove startup readback/normalization when the device starts already PID1902 without a new successful DInput write.
6. Existing mode-control resolution repeatedly requires Strong-Identity and physical-root/Container correlation even after A2VM hardware admission. For **this A2VM initial fast path only**, switch to a single fresh **VID/PID/Usage + unique currently present endpoint** lookup per command; avoid chaining the previous mode's root/container to the new mode's dynamically enumerated instance. Preserve ordinary strict writer behavior elsewhere.

**Do not** promise impossible fixed latency improvements: actual physical PID1901 enumeration took 5–8 seconds and cannot be eliminated by code alone. The larger improvement is achieved when the separately planned shutdown restore causes the next boot to start **already PID1901**, avoiding the entire first leg.

## 4. Required Implementation: Two Commands, One Final Readiness Proof

### 4.1 Faster Disabled-boot topology admission (simple prerequisite cleanup)

For the **A2VM 7/8 + Center M Disabled** boot admission path only, replace the default **three-consecutive-equal-topology-snapshots** requirement with **first unambiguous usable control-HID observation**. Keep the existing bounded wait when the exact control HID is missing; do **not** convert a temporarily absent Claw into an unconditional Ready.

- Preserve supported-hardware identification, exact Center M Disabled state, package prerequisites and HidHide baseline normalization/readback.
- Preserve strict **Enabled / stock onboarding and EX/CG3EM Disabled-boot** topology behavior; do not change the shared default globally.
- Retain existing `ControllerTopologyWaiter` and `StartupCoordinator`; a narrow readiness parameter/call-path on the same class is fine, but **no new startup manager/interface hierarchy**.
- Since the machine is already classified as A2VM 7/8, **one currently present supported VID/PID/Usage command HID** is enough for this pre-admission readiness. Do not require cross-mode root/Container matches or three identical PnP snapshots.
- If there are **multiple matching command HID endpoints** and no unambiguous selection, do not arbitrarily choose the first. A genuinely missing current endpoint still waits within the existing bound.
- Final `MsiClawAddonPhysicalOwnership` retains the authoritative exact primary gamepad input and HidHide proofs; do not repeat hardware-model identification here.

### 4.2 A2VM first leg — only wait until the next command is possible

On A2VM 7/8 initial PID1902 and once-per-real-Windows-boot marker `Claimed`:

1. Reuse the **already checked A2VM 7/8 model and Center M Disabled authority**. Determine the currently present PID and resolve **one** supported PID1902 `0xFFF0/0x0040` command HID using VID/PID/Usage. Do not revalidate the model via new whole-device Strong-Identity/Container/physical-root scans. Existing once-per-boot marker semantics stay.
2. Write **one** `SwitchMode(XInput)` through the existing MSI HID writer. Do **not** perform an independent full PID1901 transition proof first.
3. From that write, perform **one bounded target-scoped wait** for exactly **one currently present PID1901 `0xFFA0/0x0001` command HID**. Do **not** separately compare that new endpoint's Container ID, parent/root string or Strong-Identity to the vanished PID1902 source. Do not wait for *another* whole-device PID1902 disappearance proof at this intermediate step. Return the freshly resolved endpoint usable for the next write.
4. As soon as the correct fresh PID1901 command HID can be opened, issue **one** `SwitchMode(DirectInput)` through the existing HID command implementation. **Do not re-run the full global `ResolveSource` / Strong-Identity validation after the endpoint has just been uniquely resolved.** Make sure the WinRT/HID writer opens that **same freshly enumerated exact instance**, not the previous PID1902 handle or an arbitrary VID-matching device.
5. If the endpoint is absent, the opening/writing attempt fails, or there are multiple matches, **do not guess or force a write**. Keep virtual detached and use existing typed Device Arrival deferral for ordinary late enumeration. Do not repeat a successfully issued native write simply because a subsequent PnP check is late.

**No fixed 2-second/2.5-second sleep.** Use a bounded wait that can cover the observed 5–8-second A2VM PID1901 re-enumeration, but progresses immediately when the required endpoint is ready. A single approximately **10-second** bound *measured from the first successful command write* is a reasonable starting ceiling, not a minimum delay.

### 4.3 PID1901→PID1902 — send once, go straight to the final owner

This applies **both** when A2VM initially boots as PID1901 (including a successful preceding-session shutdown preparation), and as the second half of the A2VM optional boot prime:

1. Require exactly **one fresh, currently present PID1901 `0xFFA0/0x0001` command HID** and Center M Disabled. Do not require another Strong-Identity/root/Container proof of the known A2VM at this handoff.
2. Write **one** DirectInput mode command through the existing HID writer, targeting that unique **actual instance**. Successful **write** means only firmware command submission, not physical ownership.
3. **Do not spend a separate 5 seconds proving `MsiClawModeController.SwitchModeAsync` reached its full target topology and then capture the same topology again.** Proceed directly to final PID1902/DirectInput acquisition. Do not create a synthetic Strong-Identity handoff token just to replace the old root comparisons.
4. Wait for these **final requirements only** as part of the same bounded physical-ownership operation:
   - old PID1901 physical XInput mode is **absent** at the final attachment boundary;
   - one **currently present** PID1902 **primary gamepad collection** with exact supported VID/PID/Usage/`MI_00&COL01` pattern (the Windows instance suffix is dynamically obtained, **not** hardcoded);
   - that actual DirectInput device is successfully acquired and its **first valid gamepad input** received;
   - the **current exact** primary and applicable auxiliary HidHide target set is applied and **read back compliant** using present PnP metadata;
   - canonical VIIPER is Ready and only one virtual presentation is attached after all previous proofs.
5. On a newly successful DirectInput mode write plus the final actual-input and HidHide proofs above, **skip the redundant GamepadMode readback and extra whole-native-state/Strong-Identity/root/Container re-proving**. Collect the minimum device metadata needed for exact primary collection and HidHide target binding **once**, not separate identity-validation rounds. When the controller starts already PID1902 without this write, retain the existing explicit GamepadMode readback/normalization (as for non-boot recovery and manual re-arm).
6. Track a **single bounded final readiness window** from the successful DirectInput write, e.g. a **12-second maximum** rather than sequential `5s transition verification + 5s snapshot wait + 3s descriptor settle`. The window is a ceiling; *immediately publish upon completion of all final proofs*, and do not use a blind sleep. Reuse the existing owner and input-source operations; do not build a second state machine or polling service.
7. If the bound expires with the PID1902 device/child *still normally enumerating*, return the **existing typed initial-acquisition deferral** and let the **same** host's Device Arrival watcher recheck. The marker remains consumed, so no boot cycle/write repeats. Fail closed for **actual** wrong/ambiguous matching interfaces, old PID1901 still present at final attach, failed DirectInput acquisition, failed cleanup, HidHide readback mismatch or unsafe VIIPER state. Do not fail merely because an intermediate PID's root/container string differs from an earlier mode's root.

This **replaces the separate second-leg “transition timeout → deferred reacquire” hot path with one final acquisition attempt**. Keep the existing host watcher as the fallback for genuinely late hardware; it is not an unconditional second stage.

### 4.4 Existing code boundaries — simplify, do not clone

Prefer extending the existing `MsiClawModeController` / `MsiClawNativeStateManager` with the **smallest A2VM-initial-only, unique-interface, write-once** entry point. Reuse the exact PID1901 endpoint returned by the first-leg wait, rather than rediscovering and re-proving the whole physical root for the second write.

- Reuse the existing `IMsiClawModeWriter`, Windows HID transport and device selector; **no duplicate 64-byte command implementation**.
- Inspect the existing `WindowsMsiClawModeWriter`: its general `WriteGamepadModeAsync` currently performs `StronglyMatches` on `MsiClawPhysicalIdentity` and matches a WinRT `DeviceInformation` by Instance ID / Container ID. **For this A2VM initial fast path only**, pass/select the single freshly enumerated supported VID/PID/Usage HID and match the **actual opened device's current Instance ID**. Do not make the **cross-mode Container/Root/Strong-Identity comparison** a prerequisite. Avoid weakening unrelated general writer callers.
- **Keep uniqueness and exact endpoint type checks.** A2VM board identity does *not* mean an arbitrary PID1902/VID0DB0 interface is interchangeable with the command or gamepad collection. The A2VM `REV_0230` alternate Usage candidate remains **unverified**: no blind fallback.
- Do **not** globally relax ordinary `SwitchModeAsync` success predicates: manual restore, EX/CG3EM and explicit stock restore retain their existing contracts.
- All orchestration stays within `MsiClawAddonPhysicalOwnership.AcquireCoreAsync` and the existing host initial-acquisition path. Do not add a separate controller owner, interface hierarchy, extra lifecycle state machine or redundant physical-ID registry.
- Remove old A2VM initial-only double-settle predicates/pending flags/recheck branches that become **provably unreachable** after the new single final wait. Do not retain a legacy state machine alongside the replacement.
- Preserve existing publisher/VIIPER single ownership, HidHide baseline, PnP return, Sleep/Hibernate/Resume, PID1901 drift recovery, Restart/Crash/Shutdown, stock release and physical cleanup safety.
- Keep the separate shutdown PID1901 preparation work order independent; **do not** add Windows session-end behavior in this PR.

## 5. Test Matrix for Local Codex

Replace tests asserting the **superseded A2VM initial sequence**; retain tests for preserved non-A2VM and real lifecycle contracts.

1. A2VM initial PID1902, boot marker Claimed: exactly **one** XInput write, delayed unique PID1901 command child, exactly **one** DirectInput write on that fresh endpoint, then final PID1902 exact primary-collection + first-valid-input + HidHide proof → one VIIPER attach. **No second generic global PID1901 resolver and no cross-PID root/Container/Strong-Identity comparisons.**
2. A2VM initial PID1901 (e.g. after the new optional Windows shutdown restoration): exactly one DInput write, no XInput write and no intermediate 5-second full-transition verification.
3. PID1901 control HID becomes available 1 s / 7 s after write: both proceed **immediately at actual readiness**, no minimum fixed sleep and no prematurely terminal 5-second failure. A changed Container ID / physical-root / Windows instance suffix across a normal PID transition **does not block** a newly unique supported command HID; absence, wrong Usage or **multiple competing** command HID candidates must never lead to a speculative second write.
4. PID1902 parent arrives before exact HID/DirectInput; final owner waits once, no virtual attach while unready, and eventually attaches on proven first input + HidHide, with no duplicate DInput write.
5. Delayed PID1902 past the final single budget: existing Device Arrival deferred recovery keeps VIIPER Ready, boot marker consumed and both native writes **not repeated** on retry; eventual exact Full1902 attach.
6. Old PID1901 still present at would-be final attach, two simultaneously matching primary/gamepad command interfaces with no unique selection, unsupported Usage, incomplete DirectInput, missing current exact HidHide target, HidHide readback failure, VIIPER unsafe teardown → **no live virtual attach**. Do not reject a single valid endpoint solely for a changed root/container identity across mode transitions.
7. A2VM **already PID1902**, boot marker AlreadyClaimed: no extra native write; existing GamepadMode direct-input normalization and ownership path still work. Same-boot Runtime restart unchanged.
8. EX / CG3EM, manual Restore Vibration, Enable-and-Restart release, power suspend/resume and unexpected owned-session recovery: **no semantic changes**.
9. Disabled-boot ready topology on its first observation no longer requires three identical snapshots; missing/ambiguous exact MSI control HID still waits/blocks as appropriate. Enabled/stock onboarding retains prior proof policy.
10. Source command write fails; process shutting down; native HID handle disappears; PnP read failure or incorrect PID after write: no accidental success, no infinite/duplicated retries.
11. Fixed VID/PID/Usage/collection patterns work when **full instance suffixes and DirectInput Instance GUIDs change** after PnP re-enumeration, but never accept another Usage/collection or silently accept the unverified `REV_0230` alternate control HID.
12. Verify the A2VM specific command-write entry does **not** execute the old full root/Container/StronglyMatches source reproving at each intermediate stage, while ordinary EX/CG3EM, manual re-arm and stock mode transition tests retain their stronger checks.
13. Existing production controller/host tests pass after updating *only obsolete A2VM initial-flow assumptions*. Include timing phase logs proving the second full mode-verify window was removed.

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
- [ ] A2VM 7/8 board model is verified **once** at startup. Intermediate PID1902/PID1901 command stages match **unique currently present supported VID/PID/Usage endpoints** without repeated Strong-Identity, full physical-root, Container-ID comparisons or global native-state snapshots.
- [ ] Full Windows HID instance suffix / DirectInput GUID are **discovered dynamically**, never hardcoded. The currently unverified `REV_0230` alternate command HID remains unsupported absent separate proof.
- [ ] One command write per required leg; final **exact PID1902 primary-collection + live first input + PID1901 absence + HidHide readback** gates all attaches; no stale HID handle, speculative retry or duplicate native mutation.
- [ ] A2VM 7/8 initial boot only; all normal lifecycle safety and EX/CG3EM unchanged.
- [ ] Existing host Device Arrival fallback preserved; obsolete A2VM-only branches removed rather than carried along.
- [ ] Update `docs/Full 1902 Implementation/README.md` and affected A2VM/Disabled startup portions of the authority architecture to state the **once-only model gate, fixed-interface intermediate lookup, and single final DirectInput/HidHide proof**, preserving stronger checks for other lifecycles.
- [ ] Run software-only automated tests and CI; physical testing remains the user's post-merge role.

### User-owned post-merge validation (not a blocker)

User may compare A2VM 8 cold-boot time from Runtime start and BPM detection, SteamDeck first input time, vibration, next boot initially PID1901 (if shutdown work order implemented), PID1902 fallback after an interrupted shutdown, same-Windows-session Runtime restart, and Sleep/Resume. For accurate measurement, report both command-write timestamps and final physical/virtual readiness rather than total process launch time alone.
