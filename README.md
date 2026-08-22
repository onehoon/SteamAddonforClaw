# Steam Addon for Claw

> [!WARNING]
> **This project is still under active development. Do not download or install it yet.**
>
> The current code is not release-ready. Do not install source archives, CI/Actions
> artifacts, or development builds. Hardware and lifecycle validation is still in
> progress. Please wait for an official release.

Steam Input Addon for Claw exposes the built-in MSI Claw controller to Steam
through a canonical Steam Deck virtual output. The active output identity is
Steam Deck `VID=0x28DE`, `PID=0x1205`.

## Current status

| Area | Status |
| --- | --- |
| Physical MSI Claw PID_1902 acquisition | Implemented |
| Native mode / HidHide / recovery safety shell | Implemented |
| Active Steam virtual output | Steam Deck `28DE:1205` |
| VIIPER Steam Deck typed wrapper | Validated and embedded at the pinned VIIPER revision |
| Addon Steam Deck session / mapper / publisher | Implemented |
| EX hardware basic non-gyro controller input | Validated |
| Lifecycle and recovery hardware validation | Remaining validation track |
| Rumble / haptic feedback | Production two-motor translation/wiring implemented; hardware validation pending |
| Audio / jingle feedback | Unsupported |
| Gyro / accelerometer | Separate feature track |
| Quick Access / OEM1 | Production wiring + configurable mappings implemented; hardware validation pending |
| Steam-native routing presentation | Steam Deck `28DE:1205` is the single active virtual-controller presentation |

## Product goal

Expose the Claw's native controller to Steam while preserving the device's
normal Windows behavior outside an active Steam routing session. The Addon
must acquire only the verified MSI controller collection, establish a safe
native mode boundary, create one owned Steam Deck virtual device, publish
normalized input, and restore the live stock state during teardown.

The canonical Steam Deck is the single production virtual-controller
presentation for an active Steam routing session. Game Bar foreground does not
select another virtual controller.

During active Steam routing:

- WING defaults to the Steam Button;
- Center M / OEM1 defaults to Steam Quick Access;
- Addon quick controls are integrated into Steam Quick Access Menu;
- native Win+G/Game Bar activation is protected while the route is owned.

Outside routing, native Windows and Game Bar behavior is restored.

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
SD5      OEM1 / Quick Access software implemented; hardware validation pending
SD6      gyro / accelerometer feature track
```

Rumble and haptic commands are translated through the production two-motor
feedback path, but hardware validation remains pending. Do not treat one
validated feature as validation of the remaining hardware or lifecycle surface.

## Frontend transport foundation

The Runtime is a headless native process that owns controller/routing state, the
named-pipe server, and the native tray. The frontend is a separate disposable
WinUI process that owns presentation and uses the named-pipe client. The
transport uses a user/session-scoped endpoint, CurrentUserOnly pipe access,
versioned bounded JSON frames, typed RPC methods, cancellation, reconnect
handling, and `StateInvalidated` notifications.

Unknown string RPC method names are represented as `Unknown` and return
`UnsupportedMethod`; missing, null, numeric, malformed, or structurally invalid
requests return `InvalidMessage` without invoking frontend operations.

The current transport test suite covers all frontend operations and these wire
failure/reconnect/concurrency cases. Release packaging places the self-contained
UI publish under `ui/` beside the headless Runtime; hardware/manual validation
remains separate follow-up work.

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

This project is licensed under `AGPL-3.0-only`.
