# Steam Input Addon for Claw

> [!WARNING]
> This project is under active development and is not functional yet. Do not install or use it yet.

A lightweight Steam Input bridge for MSI Claw handheld PCs.

The addon exists to expose the built-in handheld controller to Steam while leaving remapping, macros, Action Sets/Layers, keyboard/mouse mapping, per-game layouts, and other controller configuration to **Steam Input**.

> [!IMPORTANT]
> **Architecture transition in progress — 2026-08-15**
>
> The current production implementation on `main` still uses the canonical VIIPER **Classic Steam Controller / Gordon** output (`28DE:1102`). That implementation is the preserved safety and lifecycle baseline.
>
> The new primary virtual-controller target is **Steam Deck** (`28DE:1205`). Steam Deck support is not yet production-ready. The VIIPER fork is first being extended with a canonical typed Steam Deck wrapper, then the Addon will adopt it side-by-side, perform real MSI Claw hardware validation, and only then cut production routing over from Gordon.
>
> The exact pre-transition Gordon documentation is archived under `docs/archive/gordon-baseline-2026-08-15/`.

> Unofficial project. Not affiliated with MSI or Valve.

---

## Current status

| Area | Status |
| --- | --- |
| Physical MSI Claw PID_1902 acquisition | Implemented |
| Native mode / HidHide / recovery safety shell | Implemented |
| Canonical VIIPER Gordon output | Current production baseline |
| Gordon output identity | `28DE:1102` |
| Steam Deck output identity | Target `28DE:1205` |
| VIIPER canonical Steam Deck typed wrapper | In progress on `onehoon/VIIPER:feature/canonical-steamdeck` |
| Addon Steam Deck binding/session/mapper | Not started; blocked on the VIIPER typed wrapper |
| Steam Deck hardware smoke test | Pending |
| OEM1 → Steam Deck Quick Access | Planned after basic Deck input validation |
| Gyro/accelerometer → Steam Deck IMU | Planned after basic Deck input validation |
| Game Bar temporary Xbox360 route | Planned after Deck cutover |

Current Addon baseline used for this transition:

```text
acdfd105f828dd78598a028d248c146b44833dc2
```

Current validated VIIPER production pin used by the Gordon path:

```text
db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
```

The development Steam Deck VIIPER branch was created from that validated VIIPER baseline. Do not treat the branch head as a new Addon pin until its canonical ABI is reviewed, built, and explicitly adopted.

---

# Product goal

The long-term routing model is:

```text
MSI Claw built-in controller
        ↓
PhysicalInput / ControllerState
        ↓
Steam Input Addon for Claw
        ↓
canonical libVIIPER typed Steam Deck
        ↓
Steam
        ↓
Steam Input
        ↓
Game
```

The Addon should remain a routing and safety layer, not a controller configuration suite.

It must not become:

- a general controller manager;
- a remapping or macro editor;
- a Steam Input replacement;
- a game profile database;
- a Handheld Companion replacement;
- a ClawTweaks replacement.

---

# Why the primary target is moving from Gordon to Steam Deck

The Gordon implementation proved the canonical VIIPER ownership, USB/IP, recovery, HidHide, PnP identity, and live-publisher architecture on real hardware. Those engineering results remain valid and are reused by the Steam Deck path.

Steam Deck is a better long-term logical controller for modern handheld PCs because it provides native fields for:

- both analog sticks;
- L3 and R3 independently;
- four rear buttons (`L4`, `R4`, `L5`, `R5`);
- Steam and Quick Access buttons;
- analog triggers plus digital trigger buttons;
- gyro / accelerometer / orientation fields;
- optional trackpad fields that can remain neutral on devices without trackpads.

This avoids Gordon-specific substitutions such as representing the physical right stick as the Gordon right pad and R3 as right-pad click.

The Steam Deck ABI itself must remain generic. The Addon may send neutral values for trackpads, but VIIPER must not remove Steam Deck trackpad fields merely because MSI Claw does not use them.

---

# Initial MSI Claw → Steam Deck mapping target

The first non-gyro hardware smoke test should use the following direct semantic mapping:

```text
A / B / X / Y       → A / B / X / Y
D-pad               → D-pad
LB / RB             → L1 / R1
LT analog           → LTrigger
RT analog           → RTrigger
LT full-pull button → L2Digital
RT full-pull button → R2Digital
Left stick          → LStickX / LStickY
Right stick         → RStickX / RStickY
L3                   → L3
R3                   → R3
Back                 → Menu
Start                → Options
M1 (right rear)      → R4
M2 (left rear)       → L4
L5 / R5              → neutral initially
Steam                → neutral initially
Quick Access         → neutral during the first smoke test
Trackpads            → neutral
IMU                   → neutral during the first smoke test
```

The MSI physical trigger full-pull buttons remain independent from analog trigger travel. Do not derive the digital full-pull state from analog travel in the Addon mapper.

`OEM1 → QuickAccess` is a planned follow-up after basic Steam Deck input is proven on hardware. Do not consume unrelated native buttons merely to populate all four Steam Deck rear-button slots.

---

# Physical MSI Claw input contract

The current Stock MSI Center M routing path uses the built-in controller's DirectInput / PID_1902 representation and normalizes it into output-independent `ControllerState`.

Important preserved physical semantics include:

- A/B/X/Y and ordinary gamepad buttons;
- 8-way D-pad;
- independent LB/RB;
- analog LT/RT travel;
- independent LT/RT digital full-pull buttons;
- both analog sticks;
- L3/R3;
- M1/M2 rear buttons.

The physical input layer must not encode Gordon or Steam Deck policy. Output-specific mapping belongs after normalization.

See:

- `docs/Reference Research_Physical Input HidHide MSI Claw Isolation.txt`
- `docs/Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt`

---

# Gyro / accelerometer direction

Gyro integration remains capability-based and is not part of the first Steam Deck input smoke test.

A previous MSI Claw 8 EX AI+ CG3EM hardware session showed:

```text
Intel ISH present
firmware gyro-to-mouse working
Windows Sensor inventory absent
ISensorManager → 0x80070490
```

That result is now known to have been caused by the tested machine's driver state. Reinstalling the relevant sensor/ISH driver stack restored Windows sensor access. Therefore the old result must **not** be used to conclude that CG3EM lacks a host-readable Windows IMU path.

Production gyro work must re-enumerate the actual Windows sensor capabilities on the target machine and record the sensor identities, units, coordinate transforms, update rate, and stale-sample behavior before enabling Steam Deck IMU publication.

See:

- `docs/Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`

---

# Supported hardware

Current hardware gating remains limited to explicitly supported MSI Claw board identities.

- MSI Claw 7 AI+ A2VM / A2VMX — `MS-1T42`
- MSI Claw 8 AI+ A2VM — `MS-1T52`
- MSI Claw 8 EX AI+ CG3EM — `MS-1T91`

The Steam Deck migration does not weaken hardware identity checks. Adding other handhelds later requires an explicit physical-device capability definition and safety review rather than accepting arbitrary controllers by VID/PID alone.

---

# Supported environment

The current MVP supports **Stock MSI Center M** as the controller-management environment. Unsupported or ambiguous controller-management environments remain passive and fail closed.

The Addon must preserve the existing environment, recovery, prerequisite, PnP ownership, and HidHide boundaries while the virtual output target changes.

External physical controller presence is not a routing veto. Steam may handle multiple controllers.

---

# Routing authority

`EffectiveSteamSessionSource` remains the routing authority.

It resolves the effective Steam session in the existing priority order and may include:

- a real Steam game / Non-Steam Shortcut session;
- optional Steam Big Picture routing when explicitly enabled;
- Developer Test mode for development only.

The VIIPER layer must not inspect `RunningAppID` directly or create a second Steam-session authority.

---

# Safety invariants that survive the Steam Deck transition

Changing Gordon to Steam Deck must **not** redesign the Addon's safety shell.

Preserve the existing high-level sequence:

```text
prepare / capture before-PnP evidence
→ record recovery mutation intent
→ mark addon-owned virtual-output identity uncertain
→ create and attach the typed VIIPER output
→ resolve the exact new Windows PnP identity
→ checkpoint addon ownership
→ verify HidHide does not block the addon-owned output
→ publish neutral state
→ start live publisher
```

Teardown remains:

```text
stop live publisher
→ detach/remove the typed virtual output
→ verify the exact owned PnP node is absent
→ clear ownership uncertainty
→ complete the recovery mutation
```

Additional invariants:

- use canonical `lib/viiper`, not legacy `clib`, for new work;
- typed handles are opaque process-local capability tokens;
- typed device removal does not implicitly remove the caller-owned bus;
- unknown attach/detach outcomes fail closed;
- DLL and generated header must come from the same VIIPER commit/build;
- exact usbip-win2 compatibility remains gated to the validated version until a later version is explicitly proven;
- PnP ownership remains Addon evidence and is not replaced by VIIPER logical bus/device IDs;
- suspend/resume must not trust pre-suspend native handles or recreate a previous virtual device from stale journal state.

---

# Steam Deck migration sequence

The active migration backlog is maintained in `docs/VIIPER_MIGRATION_TODO.md`.

Current high-level order:

```text
SD0  preserve Gordon baseline and archive its documentation
SD1  expose existing device/steamdeck through canonical typed libVIIPER
SD2  add side-by-side Addon Steam Deck ABI/session/mapper/publisher
SD3  real MSI Claw non-gyro Steam Deck smoke test
SD4  cut production SteamOutput from Gordon to Steam Deck
SD5  map OEM1 to Steam Deck Quick Access
SD6  add Windows Sensor IMU → Steam Deck gyro/accelerometer
SD7  add Game Bar neutral-Deck + typed Xbox360 route
SD8  retire the Addon Gordon production path after Deck stability is proven
```

Gordon rumble/gyro expansion is no longer a prerequisite for the product direction. Rumble/haptics can be added to the Steam Deck path as a separate feature track after the basic input and lifecycle cutover is stable.

---

# VIIPER engineering rules

Every VIIPER implementation or review for this project must read, from the selected VIIPER revision:

1. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
2. `onehoon/VIIPER/docs/libviiper/fork-api.md`
3. this repository's `docs/VIIPER_MIGRATION_TODO.md`
4. this repository's `docs/VIIPER_INTEGRATION.md`
5. this repository's `docs/VIIPER_IMPLEMENTATION_RULES.md`

For exact ABI work, use `dist/libVIIPER/libVIIPER.h` generated by the **same build** as the DLL. Do not use the repository-root legacy `libviiper.h` as the canonical Addon contract.

---

# Reference documents

Current references:

- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`
- `docs/VIIPER_IMPLEMENTATION_RULES.md`
- `docs/Reference Research_Physical Input HidHide MSI Claw Isolation.txt`
- `docs/Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt`
- `docs/Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`

Historical Gordon references remain available at their original paths with a historical notice, and the exact pre-transition versions are preserved under:

```text
docs/archive/gordon-baseline-2026-08-15/
```

---

# Development policy

- Make small, reviewable PRs.
- Include focused automated tests for production behavior changes.
- Build and validate the exact native payload before embedding it.
- Keep recovery evidence on ambiguous teardown; do not guess ownership.
- Never merge a migration PR before explicit maintainer review.
- Hardware validation is required before promoting a new virtual-output target to production.

---

# License

AGPL-3.0-only for the Steam Input Addon for Claw project. Retained third-party components and reference material remain under their own licenses; direct reuse of third-party source requires an individual compatibility and license review.
