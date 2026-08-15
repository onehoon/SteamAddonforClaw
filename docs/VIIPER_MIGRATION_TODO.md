# Canonical VIIPER Migration TODO

This is the active implementation backlog for Steam Input Addon for Claw's canonical VIIPER integration.

As of 2026-08-15, the product direction changed from further Classic Steam Controller (Gordon) feature expansion to a **Steam Deck (`28DE:1205`) primary virtual-output target**.

The Gordon canonical migration remains the validated safety/lifecycle baseline. Its exact pre-transition documentation is archived under `docs/archive/gordon-baseline-2026-08-15/`.

`README.md` is the product-behavior source of truth. `VIIPER_INTEGRATION.md` defines the native integration contract. `VIIPER_IMPLEMENTATION_RULES.md` defines the mandatory source-reading and validation rules.

---

## Status legend

| State | Meaning |
| --- | --- |
| **BLOCKED** | Dependency not satisfied; do not start production implementation. |
| **TODO** | Ready to assign as a small implementation PR. |
| **IN PROGRESS** | Active implementation/review work. |
| **VALIDATED** | Implemented, reviewed, merged, and automated validation passed. |
| **HARDWARE** | Requires real MSI Claw validation before promotion. |
| **HISTORICAL** | Completed or superseded work retained as engineering evidence. |
| **DEFERRED** | Intentionally outside the current critical path. |

Rules:

1. Keep numbered steps small and reviewable.
2. Every production change needs focused automated tests.
3. Do not mix unrelated gyro, Game Bar, UI, recovery redesign, or refactors into an ABI/lifecycle PR.
4. Build the matching `libVIIPER.dll` and generated `dist/libVIIPER/libVIIPER.h` from the same VIIPER commit.
5. Never merge before explicit maintainer review.
6. Hardware validation is mandatory before a new virtual-output target becomes production default.

---

# 1. Current baselines

## Addon Gordon baseline

The preserved Addon transition baseline is:

```text
repository: onehoon/SteamInputAddonforClaw
commit:     acdfd105f828dd78598a028d248c146b44833dc2
```

At this point the production `SteamOutput` path uses canonical Gordon and includes the real-hardware publisher scheduling work through PR #161.

This baseline is no longer the target for new Gordon feature expansion. It remains the rollback/reference baseline while Steam Deck is proven.

## Validated VIIPER production pin

```text
repository: onehoon/VIIPER
commit:     db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
```

This is the validated Gordon-era canonical pin containing the hardened typed lifecycle, tracked USB/IP attach/detach ownership, caller-owned bus behavior, Gordon independent L2/R2 state, and classified Gordon remove result.

## Steam Deck VIIPER development branch

```text
repository: onehoon/VIIPER
branch:     feature/canonical-steamdeck
base:       db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
```

The branch is a development candidate only. Do not adopt its DLL/header into the Addon until the Steam Deck typed ABI PR is reviewed and an immutable commit is selected.

---

# 2. Historical Gordon migration

The following work remains valid engineering evidence and must not be discarded merely because the primary output target is changing.

```text
M0  Gordon independent L2/R2 corrective API
M1  exact usbip-win2 0.9.7.7 routing prerequisite gate
M2  canonical C# ABI definitions and verification
M3  ControllerState → typed Gordon mapper/parity
M4  canonical DLL/runtime/session production cutover
M5  real-hardware Gordon stabilization and publisher validation
```

**Status: HISTORICAL / BASELINE PRESERVED**

The important reusable results are:

- canonical `lib/viiper` rather than `clib`;
- typed opaque device handles;
- caller-owned bus lifetime;
- exact tracked localhost USB/IP attachment ownership;
- fail-closed unknown attach/detach outcomes;
- classified retryable/unsafe removal behavior;
- callback/transport drain rules where a typed device exposes callbacks;
- exact DLL/header/provenance coupling;
- Addon before/after PnP ownership evidence;
- recovery journal mutation ordering;
- HidHide safety and exact addon-owned output identity;
- neutral-before-live and stop-publisher-before-remove ordering.

The old planned Gordon feature steps for rumble, persistent Gordon Game Bar work, and Gordon IMU are **superseded as the primary roadmap**. Do not start new Gordon feature work solely to complete the old M6-M8 sequence.

---

# 3. Active Steam Deck migration

## SD0. Preserve Gordon baseline

**Status: VALIDATED**

Goal:

- keep an explicit repository backup of the current Gordon implementation;
- preserve the exact pre-transition documentation;
- stop broad Gordon feature expansion while Deck is evaluated.

This documentation PR preserves the former README/TODO/integration/reference blobs under:

```text
docs/archive/gordon-baseline-2026-08-15/
```

Do not delete VIIPER's `device/steamcontroller` implementation or break `clib` compatibility as part of the Addon transition.

---

## SD1. Expose the existing Steam Deck implementation through canonical typed libVIIPER

**Status: IN PROGRESS**

Repository:

```text
onehoon/VIIPER
feature/canonical-steamdeck
```

Goal:

Expose existing `device/steamdeck` through the canonical typed `lib/viiper` ABI with the smallest surface needed for the first Addon input smoke test.

Initial required surface:

```text
SteamDeckDeviceHandle
SteamDeckDeviceState
CreateSteamDeckDevice
SetSteamDeckDeviceState
RemoveSteamDeckDevice
RemoveSteamDeckDeviceEx
```

Reuse shared canonical APIs:

```text
GetUSBDeviceIdentity
AttachUSBDevice
DetachUSBDevice
```

Required behavior:

- default Steam Deck identity `28DE:1205`;
- `device/steamdeck` remains the report-format authority;
- caller does not own the frame counter;
- typed remove leaves the caller-owned bus alive;
- wrong/stale/zero handles fail safely;
- classified remove uses the same canonical success/retryable/unsafe/invalid semantics;
- generated header and DLL are produced together;
- no Claw-specific fields are added to the Steam Deck ABI;
- trackpad fields remain in the generic ABI even though the Addon initially sends them neutral.

### Deliberately excluded from the first SD1 PR

The existing Steam Deck output callback path has not yet been hardened to the same callback-clear/capture synchronization contract as Gordon. The first minimal input wrapper therefore does **not** need to expose a canonical Steam Deck output callback.

Out of scope:

- rumble/haptics callback ABI;
- Addon code;
- gyro acquisition;
- OEM1 mapping;
- Game Bar;
- Gordon removal;
- Steam Deck protocol redesign.

Validation baseline:

```text
go test ./...
go test -race ./internal/server/usb ./lib/viiper
go vet ./...
git diff --check
just build-libVIIPER Release
```

Acceptance:

- Draft PR reviewed;
- canonical generated header contains the Steam Deck typed surface;
- Windows DLL exports match the header;
- ABI state layout is pinned by tests;
- an immutable VIIPER commit is selected for Addon adoption.

---

## SD2. Add side-by-side Steam Deck binding/session/mapper/publisher to the Addon

**Status: BLOCKED on SD1**

Repository:

```text
onehoon/SteamInputAddonforClaw
```

Goal:

Add a parallel Steam Deck path without deleting the Gordon production path.

Expected components:

```text
SteamDeck native C# ABI definitions
CanonicalSteamDeckSession
SteamDeckDeviceStateMapper
CanonicalSteamDeckInputPublisher
```

Do not aggressively generalize the proven Gordon path first. Add the Deck path in parallel, prove it, then extract common abstractions only where both implementations demonstrate the same invariant.

Required initial mapping:

```text
A/B/X/Y         → A/B/X/Y
D-pad           → D-pad
LB/RB           → L1/R1
LT/RT analog    → LTrigger/RTrigger
LT/RT full-pull → L2Digital/R2Digital independently
Left stick      → LStickX/Y
Right stick     → RStickX/Y
L3/R3           → L3/R3
Back/Start      → Menu/Options
M1 right rear   → R4
M2 left rear    → L4
L5/R5           → neutral initially
QuickAccess     → neutral initially
trackpads       → neutral
IMU             → neutral
```

Preserve the existing Addon recovery/PnP/HidHide safety shell and `EffectiveSteamSessionSource` authority.

Payload adoption must atomically update:

```text
VIIPER commit pin
libVIIPER.dll
matching generated libVIIPER.h
PROVENANCE.md / hashes
C# P/Invoke ABI
ABI tests
VIIPER_INTEGRATION.md
this TODO
```

---

## SD3. Real MSI Claw non-gyro Steam Deck smoke test

**Status: HARDWARE / BLOCKED on SD2**

First hardware proof must establish that the new output is visible and usable as a Steam Deck class device before production cutover.

Minimum checks:

- exact Addon-owned `28DE:1205` PnP creation and removal;
- Steam recognition;
- A/B/X/Y;
- D-pad directions and diagonals;
- LB/RB;
- LT/RT analog;
- independent LT/RT digital full-pull;
- left and right sticks;
- L3 and native R3;
- Back/Start;
- M1 → R4;
- M2 → L4;
- repeated routing enter/exit;
- Big Picture effective session behavior;
- no stale usable virtual controller after teardown;
- HidHide does not block the Addon-owned output;
- native MSI Center M controller recovery remains correct.

Do not require gyro, QAM, rumble, or Game Bar for this gate.

---

## SD4. Production cutover from Gordon to Steam Deck

**Status: BLOCKED on SD3**

Goal:

Make Steam Deck the production `SteamOutput` target only after the SD3 hardware gate passes.

Requirements:

- same routing eligibility authority;
- same recovery ownership ordering;
- same neutral-before-live rule;
- same publisher stop-before-remove rule;
- exact target-specific PnP identity (`28DE:1205`);
- no fallback that silently creates both Gordon and Deck in normal production routing.

Keep the Gordon implementation available as a short-term reference/fallback during initial Deck stabilization.

---

## SD5. OEM1 → Steam Deck Quick Access

**Status: TODO after SD4**

Goal:

Represent a safely acquired physical OEM1 control as Steam Deck `QuickAccess` without stealing unrelated buttons or changing native firmware policy accidentally.

Requirements before production mapping:

- establish the physical OEM1 event source and ownership contract;
- prove it can be observed without breaking MSI Center M recovery/safety behavior;
- keep the physical input abstraction device-capability based rather than hard-coding a Gordon-specific auxiliary slot;
- focused mapper tests;
- real Steam Quick Access behavior validation.

---

## SD6. Windows Sensor IMU → Steam Deck motion

**Status: TODO / HARDWARE**

The earlier CG3EM `ISensorManager → 0x80070490` result is no longer evidence that the hardware lacks a host-readable IMU. The tested EX machine recovered Windows sensor access after the relevant driver stack was reinstalled.

See:

```text
docs/Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt
```

Architecture target:

```text
Windows sensor source(s)
        ↓
normalized MotionState
        ↓
latest valid sample snapshot
        ↓
SteamDeckDeviceState IMU fields
        ↓
canonical VIIPER
```

Requirements:

- capability discovery, not model-name assumptions;
- record sensor identity and units from real hardware;
- normalize angular velocity/acceleration in an output-independent `MotionState`;
- define stale-sample/freshness behavior;
- do not derive raw motion from firmware gyro-to-mouse output;
- leave motion neutral when no valid fresh raw sample exists;
- initially send raw accel/gyro only unless a real orientation source/fusion contract is proven.

---

## SD7. Game Bar route with persistent Steam Deck + typed Xbox360

**Status: BLOCKED on SD4**

Planned behavior during an effective Steam session:

```text
normal Steam foreground
  → same Steam Deck device live

Game Bar foreground
  → same Steam Deck device remains attached but neutral
  → typed Xbox360 device live

Game Bar leaves foreground
  → Xbox360 released/neutralized according to the final lifecycle design
  → same Steam Deck device live again
```

Do not recreate/hotplug the Steam Deck merely because Game Bar gains foreground.

Exact Xbox360 attach/detach reuse policy must be reviewed separately against the current typed VIIPER lifecycle.

---

## SD8. Retire Addon Gordon production path

**Status: BLOCKED on post-SD4 stability**

Only after Steam Deck production routing survives repeated real-hardware sessions, suspend/resume, and teardown validation:

- remove obsolete Addon Gordon-only mapper/publisher/session/report-builder paths that are no longer needed;
- retain historical docs under the archive;
- do not delete VIIPER's Gordon implementation merely because the Addon no longer uses it;
- do not break `clib` compatibility consumers;
- update README/integration docs so Gordon is clearly historical rather than an alternate production target.

---

# 4. Parallel feature tracks

The following are useful but not blockers for the initial Deck cutover:

- Steam Deck host-output callback hardening;
- rumble/haptics integration;
- broader handheld capability abstraction for devices with four rear buttons or additional OEM controls;
- later non-MSI device support;
- performance/memory optimization unrelated to virtual-output correctness;
- localization.

Each should receive its own design/PR rather than expanding SD1-SD4.

---

# 5. Non-negotiable references for every VIIPER task

Before implementing or reviewing VIIPER work, read:

```text
onehoon/VIIPER/FORK_ARCHITECTURE.md
onehoon/VIIPER/docs/libviiper/fork-api.md
SteamInputAddonforClaw/docs/VIIPER_MIGRATION_TODO.md
SteamInputAddonforClaw/docs/VIIPER_INTEGRATION.md
SteamInputAddonforClaw/docs/VIIPER_IMPLEMENTATION_RULES.md
```

For Steam Deck report details also read:

```text
SteamInputAddonforClaw/docs/Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt
```

No implementation should proceed from memory alone.
