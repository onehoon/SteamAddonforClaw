# VIIPER Integration Contract

This document defines the current Addon integration with the canonical typed
VIIPER API. The active Steam virtual-output target is Steam Deck `28DE:1205`.

## Current status

| Item | Current contract |
| --- | --- |
| Canonical embedded API | `lib/viiper` typed ABI |
| Embedded VIIPER revision | `ec64282c69e5587466b950332d7983fd53a7d778` |
| Active Steam output | Steam Deck `28DE:1205` only |
| Addon integration | Session, mapper, publisher, identity resolver, safety stage implemented |
| Hardware status | EX basic non-gyro input validated; lifecycle evidence remains pending |
| Rumble / haptics | Separate feature track |
| Gyro / IMU | Separate feature track |

The DLL, generated header, managed P/Invoke definitions, ABI tests, hashes,
and this contract must all refer to the same VIIPER revision.

## 1. Upstream authority

The architectural and API sources of truth are:

- `onehoon/VIIPER/FORK_ARCHITECTURE.md`
- `onehoon/VIIPER/docs/libviiper/fork-api.md`

The Addon uses `lib/viiper`. The legacy `clib` surface remains a compatibility
surface in VIIPER, but new Addon integration must use the typed canonical ABI.

## 2. Integration boundary

The Addon owns application policy and orchestration:

- MSI Claw device discovery and exact physical-input selection;
- normalized `ControllerState` production;
- Steam-session and routing policy;
- target-specific Steam Deck mapping;
- PnP identity and ownership verification;
- HidHide, recovery, suspend/resume, and teardown safety.

VIIPER owns the native virtual-device, USB/IP, report, callback, and typed
handle lifecycle described by its API contract. The Addon must not infer
native ownership from a successful function return when the native result is
unknown.

## 3. Process and lifetime model

1. Load the pinned `libVIIPER.dll` for process lifetime.
2. Create the typed Steam Deck session and its caller-owned USB resources.
3. Resolve and stabilize the exact `28DE:1205` PnP identity.
4. Verify Addon ownership and HidHide state before routing.
5. Publish normalized input through the Steam Deck mapper.
6. Stop publishing before logical removal.
7. Detach and remove only resources whose ownership is known.
8. Restore the physical MSI Claw stock state and persist recovery evidence.

Public teardown waits outside the canonical native lifecycle lock. Unknown
attachment or removal outcomes fail closed and preserve recovery evidence for a
later explicit reconciliation.

## 4. Steam Deck typed ABI

The pinned VIIPER revision provides:

```text
SteamDeckDeviceHandle
SteamDeckDeviceState
SteamDeckDeviceRemoveResult
CreateSteamDeckDevice
SetSteamDeckDeviceState
SetSteamDeckOutputCallback
RemoveSteamDeckDevice
RemoveSteamDeckDeviceEx
```

The Addon uses the generated header and the matching managed definitions from
the same build. The generic output callback remains available in the native
API; Addon rumble and haptics adoption is a separate feature track.

## 5. Steam Deck state mapping

The mapper preserves the normalized physical state and maps it to native Deck
fields. Analog trigger travel and digital full-pull trigger state remain
independent. Sticks, L3/R3, rear controls, Steam, and Quick Access use their
native semantic fields where the current feature scope supports them.

Trackpad and motion fields remain neutral until their separate feature tracks
are implemented and hardware-validated.

## 6. PnP identity and ownership

The resolver accepts only the exact Steam Deck vendor/product identity:

```text
VID = 0x28DE
PID = 0x1205
```

Instance identity comparisons are case-insensitive and ownership is tracked by
the exact resolved identity. No VID-only, friendly-name-only, or broad Valve
device match is sufficient. Missing, ambiguous, or unstable identity fails
closed.

## 7. Addon safety shell

The active routing pipeline preserves these boundaries:

- native MSI mode mutation is coordinated with the physical input stage;
- physical isolation is scoped to the verified topology;
- HidHide entries are preserved unless ownership is proven;
- routing epochs gate final state commits;
- startup and resume use live current-world state;
- publisher faults request runtime fail-closed reconciliation;
- teardown is retry-safe and never silently selects another output.

## 8. Steam session authority

The effective Steam session source combines the direct Steam watcher, Big
Picture state, developer settings, and session policy. Only the routing
coordinator may enter or leave the active pipeline. The Deck output stage does
not independently infer application policy.

## 9. Hardware validation boundary

The EX hardware result currently validates basic non-gyro controller input.
It does not claim lifecycle, recovery, suspend/resume, teardown, rumble,
haptics, gyro, or IMU support. Those are separate evidence requirements.

## 10. usbip-win2 compatibility

The pinned integration follows the VIIPER fork's supported usbip-win2 version
policy. Do not silently upgrade the package or infer runtime readiness from
installation evidence alone.

## 11. Update rule

Any ABI, struct layout, callback, ownership, attachment, transport, or
lifecycle change requires reviewing the VIIPER architecture/API documents,
the Addon rules, provenance, generated header, managed interop, tests, and
hardware-validation claims together.
