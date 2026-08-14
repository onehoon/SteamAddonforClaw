# Canonical VIIPER Migration TODO

This document is the implementation backlog for moving **Steam Input Addon for Claw** from its current legacy `clib` integration to the hardened canonical `onehoon/VIIPER/lib/viiper` API.

It is intentionally separate from [`VIIPER_INTEGRATION.md`](./VIIPER_INTEGRATION.md):

- `VIIPER_INTEGRATION.md` defines the pinned integration contract and engineering reference.
- `VIIPER_MIGRATION_TODO.md` tracks discovered gaps, implementation order, acceptance criteria, and hardware-validation work.

`README.md` remains the source of truth for product behavior. If this TODO conflicts with `README.md`, follow `README.md` and update this TODO.

---

## Status legend

| State | Meaning |
|---|---|
| **BLOCKED** | Must not start until a dependency below is resolved. |
| **TODO** | Ready to be assigned as a small implementation PR. |
| **IN PROGRESS** | Active implementation/review work. |
| **VALIDATED** | Implemented, reviewed, tests/CI passed, and merged. |
| **HARDWARE** | Requires physical MSI Claw validation before being promoted to validated production behavior. |
| **DEFERRED** | Intentionally outside the current migration phase. |

Rules for this backlog:

1. Do not combine multiple numbered migration steps into one PR unless this document is explicitly updated first.
2. Every production change must include focused automated tests.
3. Run affected tests before and after each change, then full tests and Release build.
4. Work on a task-specific branch based on the latest Addon `main`.
5. Push a Draft PR and document changes, tests, limitations, and manual-test requirements.
6. Never merge a PR before explicit user review.
7. Keep unrelated UI, mapping, gyro, Game Bar, recovery redesign, or refactors out of a migration PR unless the step explicitly requires them.

---

# 1. Current baselines

## Addon main reviewed for this plan

The latest reviewed Addon runtime baseline is:

```text
4f5aabb0a2c8e2080eadc45d8fa3a77c8e4a0e4c
```

The documentation contract was then merged as:

```text
33260eafb00c7cbffa59a009e265b3b0aa93e4f9
```

The runtime baseline includes PR #140, **optional Steam Big Picture routing**.

Important consequence: routing eligibility is no longer equivalent to only `RunningAppID != 0`.

`EffectiveSteamSessionSource` is the upstream authority for whether routing should be active. It resolves, in priority order:

```text
actual Steam session
→ optional Big Picture session
→ Developer Test session
→ inactive
```

The canonical VIIPER migration must not bypass this source or add a second Steam-session detector inside the VIIPER layer.

## VIIPER baseline reviewed for this plan

Current hardened VIIPER baseline:

```text
repository: onehoon/VIIPER
commit:     3bd042dbbec9120035f7e86c9f3cac1418202be6
```

This baseline contains the PR8-PR11 lifecycle/ownership/callback/transport hardening and VIIPER PR #13's independent L2/R2 Gordon state extension described in `VIIPER_INTEGRATION.md`. VIIPER M0 is merged and **VALIDATED**.

The canonical Gordon state now records independent `L2`/`R2` digital full-pull inputs. The final semantics are `digital full-pull = explicit L2/R2 OR analog saturation`; explicit L2/R2 does not change analog trigger magnitude. The canonical `SteamControllerDeviceState` ABI size is **62 bytes**.

Any future Addon DLL, generated header, or P/Invoke definition must come from the same pinned VIIPER commit/build: `3bd042dbbec9120035f7e86c9f3cac1418202be6` and its matching canonical build.

---

# 2. Resolved VIIPER API gap — independent trigger full-pull buttons

**Status: VALIDATED — VIIPER PR #13 merged**

## 2.1 Current Addon behavior

The MSI Claw DirectInput mapper preserves analog trigger travel and digital full-pull buttons independently:

```text
Buttons[6]  → LeftTriggerFull
Buttons[7]  → RightTriggerFull
RotationX   → analog left trigger
RotationY   → analog right trigger
```

`ControllerState` therefore contains both:

```text
GamepadButtons.LeftTriggerFull
GamepadButtons.RightTriggerFull
TriggerState.Left
TriggerState.Right
```

The existing raw Classic Steam Controller report path deliberately keeps these semantics independent.

Example:

```text
left analog trigger = 64 / 255
LeftTriggerFull      = true
```

Current Addon Gordon report:

```text
analog trigger magnitude = 64 / 255 equivalent
L2 digital full-pull bit = ON
```

Existing regression tests explicitly require that the digital full-pull flag does **not** overwrite analog trigger magnitude.

## 2.2 Pre-M0 canonical VIIPER typed-state gap

The former canonical `SteamControllerDeviceState` exposed:

```text
L1, R1
LTrigger, RTrigger
```

but no explicit `L2` / `R2` digital fields. VIIPER PR #13 added those fields and updated the canonical ABI.

The Gordon implementation now derives the L2/R2 report bits from explicit `L2`/`R2` or analog `LTrigger`/`RTrigger` reaching the maximum raw value (`26000`).

Before VIIPER PR #13, a direct migration would have changed behavior:

```text
Claw state:
  analog = mid travel
  digital full pull = true

legacy Addon:
  analog remains mid travel
  full-pull bit = true

canonical typed VIIPER today:
  analog remains mid travel
  full-pull bit = false
```

This is an input-parity regression and must not be accepted as part of migration.

## 2.3 Validated VIIPER corrective change

Preferred contract:

```text
SteamControllerDeviceState:
    add L2
    add R2

InputState:
    add L2
    add R2

wire report:
    L2 report bit = explicit L2 || analog LTrigger reaches max
    R2 report bit = explicit R2 || analog RTrigger reaches max
```

This preserves both behaviors:

1. Addon can represent MSI Claw digital full-pull independently from analog magnitude.
2. Existing VIIPER consumers that only set analog to max still receive the traditional digital full-pull bit.

## 2.4 Validated VIIPER tests

The corrective VIIPER PR must include at least:

- explicit L2=true with mid-range analog left trigger → L2 bit set, analog fields unchanged;
- explicit R2=true with mid-range analog right trigger → R2 bit set, analog fields unchanged;
- L2/R2=false with analog max → existing auto-full behavior preserved;
- neither explicit full nor analog max → bit clear;
- canonical C ABI struct-size/field-offset verification updated;
- generated/header ABI verification updated;
- existing Gordon runtime/race tests still pass;
- `go test ./...`;
- focused `go test -race` for canonical/Gordon packages;
- `go vet ./...`;
- Windows canonical DLL/ABI CI green.

## 2.5 Addon consequence after the corrective VIIPER PR merge

The following are now the required inputs for the later Addon canonical ABI work:

- pinned VIIPER commit in `VIIPER_INTEGRATION.md` and this TODO baseline;
- future embedded DLL provenance;
- C# native state layout specification;
- any C ABI/header examples that include `SteamControllerDeviceState`.

Do **not** start the production canonical C# struct binding until the separate M1 gate is complete. M2 remains a later, independent ABI-only step.

---

# 3. Architecture that must survive migration

These are not migration targets. They are existing Addon responsibilities that must be preserved while the native VIIPER boundary changes.

## 3.1 Effective Steam session remains the routing authority

**Status: KEEP**

Do not make `ViiperRuntimeManager`, `ClassicSteamControllerOutputStage`, or any new native wrapper inspect `RunningAppID` directly.

Continue to let the existing routing stack decide entry/exit from the effective Steam session.

This preserves:

- ordinary Steam games;
- Non-Steam Shortcuts;
- optional Big Picture routing from PR #140;
- Developer Test mode;
- future session exclusions/policy implemented above the VIIPER layer.

## 3.2 Existing routing pipeline structure remains

**Status: KEEP**

Current Stock Center M pipeline remains conceptually:

```text
NativeMode
→ PhysicalInput
→ PhysicalIsolation
→ SteamOutput
```

Do not redesign the entire routing coordinator merely because VIIPER now has a stronger lifetime API.

## 3.3 Existing SteamOutput safety order remains the Addon-side shell

**Status: KEEP / adapt native calls only**

Preserve the existing high-level safety sequence:

```text
prepare / before PnP snapshot
→ record recovery mutation intent
→ mark addon-owned output identity uncertain
→ perform virtual-output mutation
→ resolve exact new Windows PnP identity
→ recovery ownership checkpoint
→ verify HidHide does not block addon output
→ neutral output
→ start live publisher

teardown:
stop live publisher
→ remove native virtual output
→ verify exact owned PnP node absent
→ clear addon ownership uncertainty
→ complete recovery mutation
```

Canonical VIIPER should strengthen the native operation inside this shell, not replace the shell.

## 3.4 Windows PnP identity remains Addon-owned evidence

**Status: KEEP**

`GetUSBDeviceIdentity` returns VIIPER logical bus/device identity only.

It must not replace:

- before/after PnP snapshots;
- InstanceId evidence;
- parent/ancestor/container correlation;
- addon-owned virtual-device tracking;
- recovery journal ownership evidence.

## 3.5 External-controller veto remains removed

**Status: KEEP**

Do not reintroduce the old external physical controller veto as part of canonical migration. The current product direction allows Steam to handle multiple controllers.

---

# 4. Migration sequence overview

The migration must be executed in this order.

```text
M0  VIIPER independent L2/R2 corrective API
 ↓
M1  exact usbip-win2 version routing gate
 ↓
M2  canonical C# ABI definitions and tests only
 ↓
M3  ControllerState → typed Gordon state mapper
 ↓
M4  canonical DLL payload + runtime/session lifecycle switch
 ↓
M5  physical MSI Claw routing proof + remove legacy clib/raw path
 ↓
M6  Gordon host-output / rumble integration
 ↓
M7  Game Bar persistent Gordon + typed Xbox360
 ↓
M8  gyro/IMU integration
```

Only M0-M5 are required for the initial non-gyro canonical migration.

M6-M8 are follow-up capabilities and must remain separate PRs.

---

# M0. VIIPER independent L2/R2 corrective API

**Status: VALIDATED — VIIPER PR #13 merged**

Repository: `onehoon/VIIPER`

Scope:

- only Gordon typed state/API/report behavior required for independent L2/R2;
- update canonical ABI tests/header expectations;
- no unrelated lifecycle, transport, Xbox360, SteamDeck, gyro, or Addon changes.

Acceptance:

- Section 2 semantics implemented;
- all VIIPER tests and CI green;
- PR reviewed and merged;
- new immutable VIIPER commit becomes the Addon canonical baseline.

After merge:

- update `VIIPER_INTEGRATION.md`;
- update this file;
- record the merged VIIPER PR #13 baseline and keep M0 `VALIDATED`.

---

# M1. Addon exact usbip-win2 version routing gate

**Status: TODO / next**

Repository: `onehoon/SteamInputAddonforClaw`

## Why

The current `UsbIpWin2PrerequisiteInspector` considers usbip-win2 Ready from service/UDE/filter evidence but does not prove the installed package version is the version validated by the VIIPER fork.

The Addon already bundles/provisions:

```text
usbip-win2 0.9.7.7
```

The canonical VIIPER integration contract requires exact supported-version validation before the first native attach.

## Scope

- add installed-version evidence to the usbip-win2 runtime prerequisite path;
- require exact supported version `0.9.7.7` for routing readiness;
- keep package installation/provisioning behavior intact unless a minimal compatibility adjustment is required;
- fail closed when version inspection is unavailable, conflicting, malformed, or unsupported;
- do not touch VIIPER native binding/runtime in this PR.

## Important behavior

Suggested outcomes:

```text
not installed
→ Missing

installed and exact 0.9.7.7 + service/device/filter healthy
→ Ready

installed but another version
→ Incompatible or Unusable according to existing prerequisite semantics

version cannot be established safely
→ Indeterminate
```

Do not silently accept `0.9.7.8+` based on numeric "newer is better" logic.

## Required tests

- exact 0.9.7.7 → Ready when device/service/filter evidence is also good;
- 0.9.7.8 → not Ready;
- older version → not Ready;
- missing DisplayVersion/version evidence → fail closed;
- malformed version → fail closed;
- package present but driver evidence unhealthy → existing Unusable semantics preserved;
- routing policy sees `PrerequisitesNotReady` for unsupported/indeterminate version;
- existing provisioning tests remain green.

## Out of scope

- replace embedded `libVIIPER.dll`;
- canonical P/Invoke;
- Gordon state mapping;
- Game Bar/Xbox360;
- UI redesign.

---

# M2. Canonical C# ABI definitions and verification only

**Status: BLOCKED by M1**

## Goal

Introduce the canonical native ABI into the Addon codebase without switching production routing to it yet.

This is intentionally a compile/test-only migration step.

## Add

A small canonical native layer under the existing VIIPER area. Suggested conceptual split:

```text
VirtualOutput/Viiper/
    CanonicalViiperNativeApi.cs
    CanonicalViiperNativeTypes.cs
```

Names may be refined to fit current conventions.

## Required native surface

Server:

```text
NewUSBServer
CloseUSBServer
```

Bus:

```text
CreateUSBBus
RemoveUSBBus
```

Generic typed device:

```text
GetUSBDeviceIdentity
AttachUSBDevice
DetachUSBDevice
```

Gordon:

```text
CreateSteamControllerDevice
SetSteamControllerDeviceState
SetSteamControllerOutputCallback
RemoveSteamControllerDevice
```

Xbox360 declarations may be postponed to M7 unless adding their types is trivial and does not expand this PR's production scope.

## C# interop invariants

- one-byte cgo boolean boundary represented explicitly as `byte`;
- opaque native handles represented as `nuint`/`UIntPtr`;
- Gordon buttons represented as bytes, not managed `bool` fields;
- natural native struct alignment; do not guess `Pack=1`;
- explicit `CallingConvention.Cdecl`;
- callbacks have stable managed delegate types;
- absolute bundled DLL load path only;
- native module remains process-lifetime after load;
- no unrestricted DLL search-path fallback.

## Required tests

- `Marshal.SizeOf` for every bound public struct;
- `Marshal.OffsetOf` for Gordon fields, especially around new L2/R2 fields and 16-bit values;
- native one-byte boolean delegate signatures reflected by testable wrapper behavior;
- exact required export-name inventory;
- delegate lifetime holder tests where possible without loading the production DLL;
- legacy production `ViiperNativeApi` remains untouched and current tests still pass.

## Explicit non-goal

Production routing must still use the existing legacy path after M2 merges.

---

# M3. Typed Gordon state mapper with legacy raw-report parity oracle

**Status: BLOCKED by M2**

## Goal

Create the Addon-side logical mapping:

```text
ControllerState
→ SteamControllerDeviceState
```

without switching the production native runtime yet.

## Preserve current mapping semantics

At minimum verify:

```text
A/B/X/Y
D-pad
LB/RB
Back      → Gordon Menu
Start     → Gordon Options
LeftStickClick → L3
RightStickClick → RPadPress
M1/M2 rear buttons → independent Gordon grips according to existing project mapping
right-stick coordinates → RPad coordinates
right-stick activity/click → existing RPadTouch/RPadPress semantics
left analog trigger → 0..26000
right analog trigger → 0..26000
LeftTriggerFull → explicit L2
RightTriggerFull → explicit R2
```

Do not move deadzone/sensitivity/remapping into the Addon. Those remain Steam Input responsibilities.

## Raw report builder as migration oracle

Keep `ClassicSteamControllerReportBuilder` temporarily.

For non-gyro inputs, construct:

1. current raw Addon report;
2. new typed Gordon state;
3. expected Gordon wire semantics from the pinned VIIPER implementation.

Use this to pin parity for important vectors before production cutover.

The raw builder is removed only after physical routing proof in M5.

## Required tests

- complete neutral vector;
- A/B/X/Y and D-pad;
- M1/M2 independent grips;
- right-stick touch threshold behavior preserved;
- right-stick full negative/positive deflection;
- analog triggers at 0, mid, 254, 255;
- digital trigger full independent from analog magnitude;
- Back/Start/L3/RPadPress;
- no gyro values populated yet;
- no frame field owned by Addon.

---

# M4. Canonical DLL payload and runtime/session lifecycle cutover

**Status: BLOCKED by M1 + M2 + M3**

This is the first PR that actually changes production VIIPER routing.

Keep it focused on lifecycle parity. Do not add Game Bar/Xbox360 or gyro here.

## 4.1 Embedded dependency update

Replace the old payload built from `./clib/` with a Release Windows amd64 DLL built from the newly pinned canonical `./lib/viiper` baseline.

Update together:

```text
Dependencies/Viiper/libVIIPER.dll
Dependencies/Viiper/libVIIPER.h
Dependencies/Viiper/PROVENANCE.md
Dependencies/Viiper/LICENSE.txt if required
ViiperRuntimeInspector expected SHA-256
verify-publish-assets.ps1 expected SHA-256
THIRD_PARTY_NOTICES.md if needed
VIIPER_INTEGRATION.md baseline/hash/build recipe
```

Provenance must record:

- exact VIIPER repository;
- exact immutable commit/tag;
- build command from `./lib/viiper`;
- Go/toolchain information;
- final DLL SHA-256.

Do not reuse the old `04FD...` hash.

## 4.2 First production canonical lifetime

For the initial migration, prefer **effective Steam session lifetime** for the canonical server and bus rather than immediately making them process-lifetime.

Entry:

```text
effective Steam session becomes eligible
→ existing routing pipeline prepares SteamOutput
→ recovery intent / PnP-before evidence
→ NewUSBServer
→ CreateUSBBus
→ CreateSteamControllerDevice(autoAttach=false, 28DE:1102)
→ GetUSBDeviceIdentity for diagnostics only
→ AttachUSBDevice
→ resolve Windows PnP ownership
→ recovery ownership checkpoint
→ HidHide output inspection
→ typed neutral state
→ start typed live publisher
```

Exit:

```text
stop typed live publisher
→ RemoveSteamControllerDevice
   (callback clear + exact detach + managed transport drain + logical finalize)
→ verify owned Windows PnP node absent
→ clear Addon ownership uncertainty
→ complete recovery mutation
→ RemoveUSBBus
→ CloseUSBServer
```

The exact point at which bus/server cleanup is allowed relative to recovery completion must be implemented conservatively so a bus/server cleanup failure cannot falsely claim the routing session is fully clean.

## 4.3 Do not use autoAttach

Addon contract:

```text
autoAttachLocalhost = false
```

Creation and Windows-visible attach must remain separate operations so failure/recovery boundaries are explicit.

## 4.4 Runtime ownership model

Replace legacy `uint deviceId` ownership as the mutation key with a typed native handle.

Suggested logical owner record:

```text
SteamControllerHandle : nuint
LogicalBusId          : uint
LogicalDeviceId       : uint  // diagnostics only
```

All typed mutations use the native handle.

Logical bus/device IDs are not Windows PnP identities.

## 4.5 Strong cleanup state

The new runtime wrapper must not expose only a best-effort `StopIfUnused(): void` model.

It must retain enough state to distinguish at least:

```text
inactive / clean
active
cleanup pending
unrecoverable/unsafe native ownership outcome
```

Examples:

- typed device removed, bus remove failed → do not pretend runtime cleanup is complete;
- Phase B server close failed → retain the server handle and retry `CloseUSBServer`; do not replay completed Phase A;
- attachment outcome unknown → fail closed and do not blind-retry destructive operations;
- cleanup-pending runtime → reject a new routing entry until cleanup succeeds or process recovery policy takes over.

Do not persist VIIPER handles to disk. Native handles are process-local.

## 4.6 Typed publisher

Replace:

```text
ControllerState
→ raw 64-byte report builder
→ viiper_device_set_input
```

with:

```text
ControllerState
→ typed Gordon state mapper
→ SetSteamControllerDeviceState
```

Keep the existing 4ms/250Hz publication cadence initially unless physical validation shows a reason to change it.

Addon no longer owns Gordon frame progression.

VIIPER owns:

- report serialization;
- frame increment;
- default battery behavior;
- Gordon protocol details;
- eventual gyro report gating according to host settings.

## 4.7 Neutral behavior

Neutral state must be sent through `SetSteamControllerDeviceState` before live publishing begins.

Do not rely on a zeroed raw 64-byte report generated by Addon after cutover.

## Required automated tests

- server created exactly once per routing session;
- caller-owned bus created explicitly;
- Gordon created with `autoAttach=false` and `28DE:1102`;
- Attach happens after recovery intent is established;
- PnP ownership resolution remains after Attach, not before;
- neutral typed state before live publisher;
- live publisher uses typed state API;
- publisher stops before typed Remove;
- typed Remove failure prevents recovery completion;
- PnP absence still required after successful Remove;
- bus cleanup failure is surfaced and retained;
- server Phase-B close failure can be retried without replaying logical teardown from the Addon wrapper;
- new routing entry rejected while cleanup is pending;
- `Dispose` does not silently discard failed native cleanup state without logging/fail-close handling;
- Big Picture effective session enters/exits the same routing pipeline without native-layer special cases;
- ordinary Steam and Developer Test behavior remains.

---

# M5. Physical routing proof and legacy-path removal

**Status: HARDWARE / BLOCKED by M4**

Do not delete all legacy/reference code in M4. First use the canonical path on physical MSI Claw hardware.

## Required physical checks

### Canonical device creation

- Steam sees one Classic Steam Controller;
- VID/PID is `28DE:1102`;
- PnP identity resolver proves the new node belongs to this Addon mutation;
- no duplicate Gordon node remains after session exit.

### Input parity

- A/B/X/Y;
- D-pad;
- LB/RB;
- Back/Start;
- left-stick click;
- right-stick click / right-pad press;
- right-stick pad coordinates and touch behavior;
- analog LT/RT full travel;
- independent digital full-pull L2/R2 at mid analog travel;
- M1/M2 are independent Steam Input grips.

### Session behavior

- ordinary Steam game;
- Non-Steam Shortcut;
- optional Big Picture routing enabled;
- Big Picture → game → Big Picture transition does not unnecessarily tear down routing if the effective session remains active;
- session exit removes Gordon cleanly;
- repeated enter/exit cycles do not leak devices, buses, ports, or native state.

### Recovery / safety

- HidHide does not block addon-owned Gordon;
- Stock Center M native state restored after clean exit;
- failure path leaves recovery evidence rather than claiming success;
- no hidden internal Claw state remains after clean rollback.

## After hardware proof

A small cleanup PR may then remove legacy-only production code:

- old `clib` export binding;
- `viiper_init` / `viiper_device_add_ex` runtime path;
- raw production `viiper_device_set_input` path;
- old runtime tests that no longer describe production semantics.

`ClassicSteamControllerReportBuilder` may either be deleted or retained only as a clearly labeled protocol/reference test helper if it still provides unique validation value.

Do not remove reference research documents merely because production no longer serializes raw reports.

---

# M6. Gordon host-output callback and vibration/haptics

**Status: DEFERRED until canonical non-gyro routing is stable**

Goal:

```text
SetSteamControllerOutputCallback
→ copy 64-byte host payload immediately
→ parse only commands needed by Addon/hardware output
→ route supported rumble/haptic behavior to MSI Claw native capability
```

Requirements:

- managed delegate retained through Remove completion;
- callback never lets managed exceptions cross the unmanaged boundary;
- callback does minimal synchronous work;
- copy the native buffer before returning;
- teardown relies on VIIPER callback clear/drain guarantees;
- no callback-driven reentrant Addon global lock deadlocks.

Rumble implementation details require a separate source/hardware review before this task is assigned.

---

# M7. Game Bar persistent Gordon + typed Xbox360

**Status: DEFERRED until M5/M6 foundation is stable**

Target product behavior remains:

```text
normal Steam effective session:
    Gordon attached + live
    Xbox360 off

Game Bar foreground:
    same Gordon remains attached
    Gordon receives neutral states
    temporary/logical Xbox360 becomes active
    live Claw input → Xbox360

Game Bar exits:
    Xbox360 off/removed
    same Gordon resumes live states
```

Do not disconnect/recreate Gordon merely because Game Bar foreground changed.

This step will decide whether the canonical server/bus lifetime should remain session-lifetime while multiple typed devices coexist, or whether a longer-lived server/bus yields a cleaner implementation. Make that decision from the post-M5 production code rather than pre-optimizing M4.

Required future pieces:

- `GameBarDetector` using WinEvent foreground + process/package identity;
- typed Xbox360 C# ABI;
- Xbox360 state mapper;
- Xbox360 rumble callback;
- Gordon neutral/live mode switch without hotplug;
- duplicate-input prevention;
- rollback/recovery semantics for partial Xbox360 failures.

---

# M8. Gyro / IMU routing

**Status: DEFERRED / HARDWARE RESEARCH REQUIRED**

Canonical VIIPER Gordon typed state already has:

```text
AccelX/Y/Z
GyroX/Y/Z
GyroQuatW/X/Y/Z
```

That is only the transport capability.

The Addon still needs a validated MSI sensor source, scaling, orientation, calibration, and model capability plan.

Keep EX/CG3EM and A2VM handling separate until hardware/source validation proves they can share one implementation.

Do not invent gyro values from controller axes and do not enable gyro for A2VM merely because the native state struct has fields.

---

# 5. Important implementation decisions already made

These decisions should not be reopened casually during individual migration PRs.

## D1 — Mapping/remapping remains Steam Input's responsibility

Addon supplies physical normalized state and the virtual controller. It does not add:

- per-game mapping profiles;
- macro editor;
- turbo editor;
- keyboard/mouse remapping UI;
- Action Set/Layer logic;
- long/double-press logic.

## D2 — DirectInput remains the preferred MSI Claw source

M1/M2 are independent only on the DirectInput path and must never be reconstructed from XInput.

## D3 — `EffectiveSteamSessionSource` controls routing lifetime

Do not duplicate Steam detection in canonical VIIPER runtime code.

## D4 — explicit Gordon attach

Always create Gordon with:

```text
autoAttachLocalhost=false
VID=0x28DE
PID=0x1102
```

then explicitly call `AttachUSBDevice` under Addon recovery ownership.

## D5 — caller-owned bus means explicit Addon policy

Typed device removal must not implicitly define bus lifetime.

Initial migration may still choose to remove bus/server at the end of each effective Steam session. That is an explicit Addon decision and is compatible with caller-owned semantics.

## D6 — no native handle persistence

Recovery journal stores OS-visible mutations and ownership evidence, not cgo/native handles.

## D7 — canonical VIIPER owns Gordon wire serialization

After production cutover:

- Addon owns normalized logical input state;
- VIIPER owns frame numbers and 64-byte Gordon wire reports.

## D8 — hardware proof before deleting all legacy parity tools

Keep enough of the existing raw path to compare behavior until the typed route is proven on MSI Claw hardware.

---

# 6. Known current-code mismatches to remove during migration

Track these explicitly so they are not forgotten.

## Legacy native exports

Current production Addon still binds:

```text
viiper_init
viiper_shutdown
viiper_bus_create
viiper_bus_remove
viiper_device_add_ex
viiper_device_remove
viiper_device_set_input
viiper_device_set_feedback_callback
viiper_list_device_types
viiper_last_error
viiper_free_string
```

Target is canonical typed exports.

## Legacy embedded payload

Current Addon dependency provenance still points to:

```text
steam-input-addon-baseline-1
commit 209c882009caea4f3baf322b9b6020c1a921feed
build ./clib/
```

Target payload must be rebuilt from the final canonical VIIPER commit after M0.

## Legacy runtime lifetime

Current `ViiperRuntimeManager`:

- stores bus/device IDs rather than typed device handles;
- automatically removes the bus when the final device is removed;
- shuts down the logical native wrapper when unused;
- uses raw report submission;
- cannot express canonical close-failed/cleanup-pending semantics strongly enough.

M4 replaces this model.

## Legacy publisher

Current publisher:

- generates a raw 64-byte Gordon report every tick;
- owns/report-tests Gordon frame progression;
- calls raw `SetInput`.

M3/M4 replace this with typed state submission.

## Current usbip prerequisite

Current runtime prerequisite checks service/UDE/filter presence but not the exact validated package version. M1 closes this gap.

---

# 7. Failure-handling requirements for the canonical Addon wrapper

These are acceptance requirements for M4 and later.

| Failure | Required Addon behavior |
|---|---|
| canonical DLL missing/hash mismatch | routing not ready; remain passive |
| required canonical export missing | fail closed; no routing mutation |
| usbip-win2 not exact supported version | routing not ready; no Attach |
| `NewUSBServer` fails | no native/MSI routing entry continuation |
| `CreateUSBBus` fails | close server if safely possible; report failure |
| Gordon logical create fails before Attach | no Windows PnP Gordon expected; safely clean server/bus |
| Attach known failure | clean logical device/bus/server if ownership is known; restore Addon-side native mutations |
| Attach outcome unknown | fail closed; preserve recovery evidence; no blind destructive retry |
| PnP ownership cannot be proven | typed Remove + verified absence path; preserve uncertainty if absence cannot be established |
| typed state publication rejected | stop publisher and fail-close routing |
| typed Remove known logical failure | handle may remain; transport already drained; retain cleanup-pending state and retry remove, never reattach same handle |
| PnP node remains after successful Remove | fail recovery completion; preserve evidence |
| RemoveUSBBus failure | runtime cleanup pending; do not claim fully stopped |
| Close Phase B failure | retain server handle; retry `CloseUSBServer`; do not replay completed Phase A |
| managed callback exception | catch/log on managed side; never escape across native callback |
| Addon process crash | process-local native handles are gone; startup recovery cleans persistent MSI/HidHide evidence only |

---

# 8. CI expectations during migration

Every Addon migration PR must use the repository's normal CI and, where relevant, add focused tests for the new native boundary.

Minimum per PR:

```text
run related tests before change
add focused regression tests
run related tests after change
dotnet test
dotnet build -c Release
git diff --check
push task branch
open Draft PR
```

For M2/M4 additionally validate:

- canonical interop struct size/offset tests;
- canonical export inventory;
- embedded DLL load by exact path;
- published asset hash verification;
- provenance/header/DLL consistency;
- no accidental legacy export use remains in production after M4.

For M4/M5, physical MSI Claw testing is an explicit required follow-up and must be listed in the PR body even if automation is fully green.

---

# 9. Task tracking table

Update this table whenever a step changes state.

| ID | Task | State | Dependency | Notes |
|---|---|---|---|---|
| M0 | VIIPER independent L2/R2 typed Gordon API | **VALIDATED** | none | VIIPER PR #13 merged at `3bd042d...`; canonical state ABI size 62 |
| M1 | exact usbip-win2 0.9.7.7 routing gate | **TODO / NEXT** | M0 | No native ABI changes |
| M2 | canonical C# ABI definitions/tests | **BLOCKED** | M1 | No production wiring |
| M3 | typed Gordon state mapper/parity tests | **BLOCKED** | M2 | Keep raw builder as oracle |
| M4 | canonical payload/runtime production cutover | **BLOCKED** | M1, M2, M3 | First production canonical route |
| M5 | physical routing proof + legacy cleanup | **BLOCKED / HARDWARE** | M4 | Required before deleting parity path |
| M6 | Gordon host-output / rumble | **DEFERRED** | M5 | Separate research/PR |
| M7 | Game Bar Gordon-neutral + typed X360 | **DEFERRED** | M5 | Minimize Gordon hotplug |
| M8 | gyro/IMU routing | **DEFERRED / HARDWARE** | M5 | EX/A2VM capability-specific |

---

# 10. How to resume this work in a new conversation

A new implementation/review session should read, in order:

```text
1. README.md
2. docs/VIIPER_INTEGRATION.md
3. docs/VIIPER_MIGRATION_TODO.md
4. latest Addon main code for the task's touched area
5. pinned VIIPER source for the exact canonical API being consumed
```

Then:

1. confirm the latest Addon `main` SHA;
2. confirm the latest pinned VIIPER SHA in `VIIPER_INTEGRATION.md`;
3. pick only the first non-VALIDATED dependency-ready task from the table above;
4. do not assume this TODO is current if `main` has materially changed — reconcile and update it first;
5. implement only that task plus its required tests;
6. open a Draft PR and leave subsequent tasks untouched.

This file is a living migration backlog. Findings discovered during implementation should be added here before broadening a PR's scope.
