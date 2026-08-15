# Steam Input Addon for Claw

Steam Input Addon for Claw exposes the built-in MSI Claw controller to Steam
through a canonical Steam Deck virtual output. The active output identity is
Steam Deck `VID=0x28DE`, `PID=0x1205`.

## Current status

| Area | Status |
| --- | --- |
| Physical MSI Claw PID_1902 acquisition | Implemented |
| Native mode / HidHide / recovery safety shell | Implemented |
| Active Steam virtual output | Steam Deck `28DE:1205` |
| VIIPER Steam Deck typed wrapper | Validated and embedded at `ec64282c69e5587466b950332d7983fd53a7d778` |
| Addon Steam Deck session / mapper / publisher | Implemented |
| EX hardware basic non-gyro controller input | Validated |
| Lifecycle and recovery hardware validation | Remaining validation track |
| Rumble / haptics | Separate feature track |
| Gyro / accelerometer | Separate feature track |
| Quick Access / OEM1 | Planned |
| Game Bar / typed Xbox360 route | Planned |

## Product goal

Expose the Claw's native controller to Steam while preserving the device's
normal Windows behavior outside an active Steam routing session. The Addon
must acquire only the verified MSI controller collection, establish a safe
native mode boundary, create one owned Steam Deck virtual device, publish
normalized input, and restore the live stock state during teardown.

The current active virtual output is the Steam Deck device at `28DE:1205`.
The basic non-gyro controller input path has been validated on MSI Claw EX
hardware. Lifecycle, recovery, and failure-path evidence remains separate
from that basic-input result.

## Initial MSI Claw mapping

| Physical control | Steam Deck output |
| --- | --- |
| A / B / X / Y | A / B / X / Y |
| D-pad | D-pad |
| LB / RB | LB / RB |
| analog LT / RT | analog LT / RT |
| digital LT / RT | digital L2 / R2 |
| left stick | left stick |
| right stick | right stick |
| L3 / R3 | L3 / R3 |
| M1 / M2 | rear-button mapping defined by the Deck mapper |
| Steam key | Steam |
| Quick Access key | Quick Access |

Trackpad and motion fields remain neutral until their separate feature tracks
are implemented and validated.

## Physical input contract

The Addon acquires the MSI Claw PID_1902 DirectInput gamepad only after exact
PnP identity and topology checks. VID/PID counts alone are insufficient.

The normalized input state preserves independent analog trigger travel and
digital full-pull trigger buttons, rear-button side identity, sticks, buttons,
and D-pad state. Output-specific policy is applied only after normalization.

## Safety and lifecycle invariants

- unknown device ownership fails closed;
- physical isolation is scoped to the verified MSI controller topology;
- virtual-device ownership is tracked by exact identity;
- startup uses live current-world state rather than replaying stale routing;
- suspend/resume and teardown restore a safe stock baseline;
- native, PnP, HidHide, publisher, and recovery failures do not select another
  output implementation or silently continue routing.

## Supported environment

- Windows 10/11 x64
- MSI Claw EX hardware for the validated basic-input path
- Steam running locally for Steam routing
- the pinned VIIPER runtime and supported usbip-win2 compatibility described in
  [`docs/VIIPER_INTEGRATION.md`](docs/VIIPER_INTEGRATION.md)

## Roadmap

```text
Current  Steam Deck 28DE:1205 active runtime
SD3      lifecycle, recovery, and failure-path hardware validation
SD4      production readiness review
SD5      OEM1 / Quick Access completion
SD6      gyro / accelerometer feature track
SD7      Game Bar / typed Xbox360 route
```

Rumble and haptics are tracked separately from the basic controller-input
validation. Do not treat one validated feature as validation of the remaining
hardware or lifecycle surface.

## Reference documents

- [`docs/VIIPER_INTEGRATION.md`](docs/VIIPER_INTEGRATION.md)
- [`docs/VIIPER_MIGRATION_TODO.md`](docs/VIIPER_MIGRATION_TODO.md)
- [`docs/VIIPER_IMPLEMENTATION_RULES.md`](docs/VIIPER_IMPLEMENTATION_RULES.md)
- [`docs/Reference Research_Steam Deck VIIPER SteamOutput Input Reports.txt`](docs/Reference%20Research_Steam%20Deck%20VIIPER%20SteamOutput%20Input%20Reports.txt)
- [`docs/Reference Research_Physical Input HidHide MSI Claw Isolation.txt`](docs/Reference%20Research_Physical%20Input%20HidHide%20MSI%20Claw%20Isolation.txt)

## Development policy

Keep changes small and independently reviewable. Runtime, ABI, generated
header, DLL, provenance, tests, and hardware-validation claims must remain
consistent. Hardware-dependent claims require actual device evidence.

## License

This project is licensed under GPL-3.0-or-later. See [`LICENSE`](LICENSE).
