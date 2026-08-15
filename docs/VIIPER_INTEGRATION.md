# VIIPER Integration Contract

This document is the engineering contract for integrating `onehoon/VIIPER` into **Steam Input Addon for Claw**.

`README.md` remains the source of truth for product behavior and routing policy. `VIIPER_MIGRATION_TODO.md` is the active implementation backlog. `VIIPER_IMPLEMENTATION_RULES.md` defines the mandatory source-reading and validation rules.

The exact pre-Steam-Deck-transition version of this document is preserved under `docs/archive/gordon-baseline-2026-08-15/`.

---

## Document status — 2026-08-15

| Item | Status |
| --- | --- |
| Canonical embedded API | `lib/viiper` required |
| Current Addon-embedded VIIPER pin | `ec64282c69e5587466b950332d7983fd53a7d778` on `onehoon/VIIPER:main` (SD2 adopted; includes Gordon + Steam Deck typed ABI) |
| Selected VIIPER pin for Steam Deck SD2 | `ec64282c69e5587466b950332d7983fd53a7d778` on `onehoon/VIIPER:main` |
| Current Addon active runtime virtual output | Steam Deck `28DE:1205` exclusively; Gordon `28DE:1102` is retained reference/rollback code and is not selected by runtime composition |
| Current Addon transition baseline | `acdfd105f828dd78598a028d248c146b44833dc2` |
| New primary target architecture | Steam Deck `28DE:1205` |
| Steam Deck canonical typed wrapper | **VALIDATED / MERGED** via VIIPER PR #16 |
| Addon Steam Deck native binding | **SD2 ADOPTED / ACTIVE** |
| Steam Deck hardware validation | **HARDWARE-GATED** (SD3 real MSI Claw smoke test still pending) |
| OEM1 → Quick Access | Planned after basic Deck validation |
| Steam Deck IMU | Planned after basic Deck validation |
| Game Bar typed Xbox360 | Planned after Deck cutover |

The former `feature/canonical-steamdeck` branch has been merged and removed. Do not use a deleted branch as an integration authority. For SD2, the selected immutable native source revision is `onehoon/VIIPER@ec64282c69e5587466b950332d7983fd53a7d778`.

SD2 has atomically adopted `ec64282c69e5587466b950332d7983fd53a7d778` as the Addon's embedded VIIPER pin: the Release `libVIIPER.dll`, the matching generated `libVIIPER.h`, the C# P/Invoke ABI, ABI tests, `PROVENANCE.md`/hashes, and this document were all updated together in the same change. The Gordon-era `db70bdedbe36846c665c841ea9f6ae9bf01d0d3d` payload is no longer embedded; Gordon's own ABI/behavior is unchanged by this commit (see `src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md` for the full build attestation).

---

# 1. Mandatory upstream VIIPER authority

Every Addon-driven VIIPER change must first read, from the same selected VIIPER revision:

```text
onehoon/VIIPER/FORK_ARCHITECTURE.md
onehoon/VIIPER/docs/libviiper/fork-api.md
```

The fork architecture is authoritative for ownership and lifecycle intent. The API guide is authoritative for the consumer-facing canonical C ABI contract.

For exact layout/signatures, the authority is:

```text
dist/libVIIPER/libVIIPER.h
```

produced by the **same `just build-libVIIPER Release` build** as the DLL being embedded.

Do not use the repository-root legacy `libviiper.h` as the Addon's canonical ABI source.

---

# 2. Integration boundary

The Addon intentionally keeps VIIPER behind a narrow native boundary:

```text
physical handheld input
        ↓
Addon ControllerState / future MotionState
        ↓
Addon routing + recovery + PnP ownership + HidHide safety
        ↓
small canonical libVIIPER typed binding
        ↓
typed virtual Steam output
        ↓
Windows / Steam
```

VIIPER must not become a second Addon routing-policy state machine.

The Addon owns:

- effective Steam-session policy;
- physical MSI controller acquisition;
- environment compatibility;
- recovery journal ordering;
- Windows PnP before/after evidence;
- exact Addon-owned virtual-output identity;
- HidHide mutation and recovery;
- live/neutral routing policy;
- suspend/resume policy.

VIIPER owns:

- canonical typed native device objects;
- USB server/bus/device lifetime primitives;
- device report encoding/protocol behavior;
- tracked localhost USB/IP attach/detach mechanics;
- typed native handle validity;
- transport teardown guarantees documented by the selected fork revision.

---

# 3. Canonical lifetime model

New integration must use `lib/viiper`, not `clib`.

Preferred lifetime model:

```text
Addon process lifetime
    └─ loaded libVIIPER.dll

embedded runtime lifetime
    └─ USB server
       └─ caller-owned USB bus
          ├─ typed Steam output device
          └─ typed Xbox360 device when required later

Windows exposure
    └─ explicit tracked AttachUSBDevice / DetachUSBDevice

routing lifetime
    └─ neutral/live state publication
```

Critical invariants:

- native handles are opaque process-local capability tokens;
- stale/zero/wrong-type handles fail safely;
- typed device removal removes the logical device, not its caller-owned bus;
- `RemoveUSBBus` / `CloseUSBServer` own bus/server teardown;
- successful tracked attachment records exact backend/port ownership;
- unknown attach/detach outcomes fail closed;
- public lifecycle operations follow the selected canonical transport-drain contract;
- completed teardown is not destructively replayed during retry.

The DLL module should be treated as process-lifetime once loaded. Do not unload a cgo DLL while native callbacks or delegates may still reference it.

---

# 4. Validated Gordon baseline

The preserved transition baseline used the canonical Gordon path:

```text
Addon commit: acdfd105f828dd78598a028d248c146b44833dc2
embedded VIIPER pin: db70bdedbe36846c665c841ea9f6ae9bf01d0d3d
output: Classic Steam Controller 28DE:1102
```

The Gordon work validated the safety/lifecycle architecture that the Steam Deck path must reuse:

- canonical typed handles;
- caller-owned bus lifetime;
- tracked attach/detach ownership;
- fail-closed unknown outcomes;
- classified Gordon removal;
- matching generated header/DLL/provenance;
- Addon PnP ownership evidence;
- neutral-before-live publication;
- publisher stop-before-remove;
- HidHide safety;
- recovery mutation ordering.

The existing Gordon implementation is now a **preserved baseline**, not the long-term primary virtual-controller target.

Do not delete Gordon from VIIPER or break `clib` compatibility during Steam Deck migration.

---

# 5. Steam Deck target contract

The new primary virtual-output target is the existing VIIPER Steam Deck implementation:

```text
device/steamdeck
default VID: 28DE
default PID: 1205
input report length: 64 bytes
```

The canonical typed Steam Deck wrapper is now present in VIIPER `main` at:

```text
onehoon/VIIPER
ec64282c69e5587466b950332d7983fd53a7d778
PR #16 — Expose Steam Deck through canonical libVIIPER API
```

The old development branch was merged and removed. SD2 must consume the immutable `main` commit above, not a branch head.

## 5.1 Validated minimal typed ABI

The canonical Steam Deck surface available for the first Addon input smoke test is:

```text
SteamDeckDeviceHandle
SteamDeckDeviceState
SteamDeckDeviceRemoveResult
CreateSteamDeckDevice
SetSteamDeckDeviceState
RemoveSteamDeckDevice
RemoveSteamDeckDeviceEx
```

It reuses the existing shared canonical APIs:

```text
GetUSBDeviceIdentity
AttachUSBDevice
DetachUSBDevice
```

There are no redundant Steam Deck-specific attach/detach functions.

The Steam Deck state ABI remains generic and represents the existing semantic `device/steamdeck.InputState`. It is not an MSI-Claw-specific struct.

The Addon may send neutral trackpad values, but trackpad fields remain part of the generic Steam Deck state contract.

Frame ownership remains inside `device/steamdeck`; the external caller does not own or manually increment the report frame.

The validated ABI layout at `ec64282...` includes:

```text
sizeof(SteamDeckDeviceState) = 76
sizeof(SteamDeckDeviceRemoveResult) = 4
```

Critical state offsets are pinned in VIIPER tests and the Windows canonical CI verifies the generated header signatures and the four Steam Deck DLL exports.

## 5.2 Output callback intentionally deferred

The initial Addon smoke test does not require a host-output callback.

The current Steam Deck implementation's callback storage/dispatch must be reviewed and brought to the documented canonical callback synchronization contract before a public `SetSteamDeckOutputCallback` is adopted.

Therefore the validated minimal typed input wrapper deliberately omits output callback, rumble, and haptics ABI. Do not weaken the callback teardown contract merely to expose an early callback.

---

# 6. Addon Steam Deck state mapping contract

The first non-gyro mapper should preserve physical semantics directly:

```text
A/B/X/Y         → A/B/X/Y
D-pad           → DPadDown/Left/Right/Up
LB/RB           → L1/R1
LT full-pull    → L2Digital
RT full-pull    → R2Digital
LT analog       → LTrigger
RT analog       → RTrigger
Left stick      → LStickX/LStickY
Right stick     → RStickX/RStickY
L3              → L3
R3              → R3
Back            → Menu
Start           → Options
M1 right rear   → R4
M2 left rear    → L4
L5/R5           → false initially
Steam           → false initially
QuickAccess     → false initially
trackpad state  → neutral
IMU             → neutral during the first smoke test
```

The digital full-pull buttons remain independent of analog trigger travel. Do not collapse them into an analog threshold in the Addon mapper.

The main architectural benefit over Gordon is that right stick and R3 are native Steam Deck fields rather than Gordon right-pad substitutions.

`OEM1 → QuickAccess` is a separate follow-up because acquiring the physical OEM1 event safely is an Addon/device-capability concern, not part of the basic Deck ABI.

---

# 7. Addon adoption of the selected VIIPER pin

The selected native source revision for SD2 is:

```text
onehoon/VIIPER@ec64282c69e5587466b950332d7983fd53a7d778
```

Build the matching canonical payload with:

```text
just build-libVIIPER Release
```

Before copying any payload into the Addon, run the expected VIIPER validation gate:

```text
go test ./...
go test -race ./internal/server/usb ./lib/viiper
go vet ./...
git diff --check
just build-libVIIPER Release
```

Adoption into the Addon must update atomically:

```text
selected VIIPER commit pin
generated dist/libVIIPER/libVIIPER.h
libVIIPER.dll
PROVENANCE.md / hashes
C# native struct/signatures
ABI size/offset/export tests
VIIPER_INTEGRATION.md
VIIPER_MIGRATION_TODO.md
```

Never combine a DLL from one commit with a header or managed layout from another.

SD2 has completed this atomic adoption:

```text
Addon-embedded VIIPER pin (Gordon + Steam Deck): ec64282c69e5587466b950332d7983fd53a7d778
```

`src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md` records the DLL/header SHA-256
hashes and full build attestation for this commit.

---

# 8. PnP identity and ownership

`GetUSBDeviceIdentity` returns the logical VIIPER bus/device identity. It does **not** replace Windows PnP ownership evidence.

The Addon must continue to use:

- before/after PnP snapshots;
- exact instance/container/ancestor correlation;
- addon-owned virtual-device tracking;
- recovery ownership evidence.

When Deck becomes the active target, target-specific identity checks must use the exact expected Steam Deck identity (`28DE:1205`) rather than a generic "Valve device" match.

Do not let Gordon `28DE:1102` assumptions silently leak into Deck identity resolution.

---

# 9. Existing Addon safety shell remains authoritative

The native target changes; the Addon safety shell does not.

Required entry sequence:

```text
prepare + before-PnP snapshot
→ persist recovery mutation intent
→ mark Addon output identity uncertain
→ create/attach typed output
→ resolve exact new Windows PnP identity
→ checkpoint ownership
→ verify HidHide safety
→ publish neutral state
→ start live publisher
```

Required teardown sequence:

```text
stop live publisher
→ native typed detach/remove
→ verify exact owned Windows PnP node absent
→ clear ownership uncertainty
→ complete recovery mutation
```

If ownership becomes ambiguous, retain evidence and fail closed rather than guessing.

---

# 10. Effective Steam session remains the routing authority

The VIIPER binding/session must not inspect `RunningAppID` directly.

Continue to use the Addon's existing `EffectiveSteamSessionSource` decision, which may represent:

```text
real Steam session
→ optional supported Big Picture session
→ Developer Test session
→ inactive
```

VIIPER is an output mechanism, not a Steam-session detector.

---

# 11. Gyro / MotionState contract

Gyro is not part of the first Deck smoke test.

The old CG3EM session where Windows Sensor inventory was absent and `ISensorManager` returned `0x80070490` is now known to have been affected by the machine's driver state. After sensor/ISH driver reinstallation, Windows sensor access recovered.

Therefore production motion architecture should be capability-based:

```text
Windows sensor source
        ↓
normalized Addon MotionState
        ↓
latest fresh motion snapshot
        ↓
SteamDeckDeviceState IMU fields
```

Requirements:

- identify the actual sensor(s) on hardware;
- record units and coordinate basis;
- normalize motion outside the Steam Deck mapper;
- define freshness/stale-sample behavior;
- never use firmware gyro-to-mouse output as a fake raw IMU source;
- send neutral motion when no valid fresh raw sample exists;
- do not invent a quaternion if no real orientation/fusion source is available.

See `Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`.

---

# 12. Game Bar follow-up

After Steam Deck is production-stable, the intended Game Bar architecture remains:

```text
Steam session / normal foreground
    → same Steam Deck device live

Game Bar foreground
    → same Steam Deck stays attached but neutral
    → typed Xbox360 live

Game Bar leaves foreground
    → Xbox360 released/neutralized according to the final lifecycle contract
    → same Steam Deck live again
```

Do not recreate or hotplug the Steam Deck solely because Game Bar gains foreground.

This is a separate feature track and must be reviewed against the then-current typed Xbox360 and attachment contracts.

---

# 13. usbip-win2 compatibility

The canonical Windows attachment path remains pinned to the exact usbip-win2 version validated by the VIIPER fork, currently:

```text
v0.9.7.7
commit 7c219953101cc5d0ec9a0bcb3eb87259cf72bedd
```

Do not assume a newer version is compatible merely because the version number is higher. A different version requires explicit native ABI and real-runtime validation before the Addon routing prerequisite gate may accept it.

---

# 14. Update rule for this document

Update this integration contract whenever any of the following changes:

- selected VIIPER commit;
- canonical Steam Deck state layout;
- exported typed function set;
- attachment/lifecycle semantics;
- usbip-win2 compatibility;
- Addon native payload/hash;
- production virtual-output identity;
- motion/output callback contract.

Do not document a branch-only behavior as validated production behavior.
