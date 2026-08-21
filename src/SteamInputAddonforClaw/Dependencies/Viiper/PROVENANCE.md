# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     77a8af547de2253862ede648a212c01d4dd950c1
Branch:     main
Entrypoint: just build-libVIIPER Release
```

This revision provides the typed Steam Deck ABI used by the active Addon
runtime (see "ABI review" below for the current revision's reviewed ABI
delta). The active virtual output identity is `VID=0x28DE`, `PID=0x1205`.

## Build attestation

The artifact was fetched and independently re-verified from the canonical
`onehoon/VIIPER` main-branch build for this exact commit using
`scripts/update-viiper.ps1`, which validates the artifact's own
`viiper-artifact.json` manifest plus recomputed DLL/header SHA-256 hashes
before anything is adopted. That canonical build itself was produced with the
literal official entrypoint:

```text
just build-libVIIPER Release
```

Artifact hashes (recomputed independently from the fetched files, matching
the canonical `viiper-artifact.json` manifest for this commit):

```text
Generated header SHA-256: 202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718
DLL SHA-256:              d07d2e5a622983aed6b9cc676b59b5b3a31a2b343015c4492fa5bdae74dd0cb6
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `49e5796b9f31f8ddb7009fde6f910c66837e2315` ->
`77a8af547de2253862ede648a212c01d4dd950c1`. The target is exactly one
canonical main commit, `Reduce Windows USBIP loopback attach latency (#44)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed Addon header. The Addon base and dependency-PR head vendored headers
have the same Git blob identity,
`2ab164e4d37c7cfde6e9a0771c3c5489183b0a03`, and the generated-header
SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no added or removed exports, signature changes, enum changes, struct
layout/packing changes, callback ABI changes, or Steam Deck/Xbox360 typed-state
layout changes. `SteamDeckDeviceState` remains 76 bytes with `LPadForce`,
`RPadForce`, `LStickForce`, and `RStickForce` at offsets 68/70/72/74. The
current Addon managed P/Invoke surface, 19-entry `RequiredExports`, classified
attach/detach bindings, callback rooting, Xbox360 typed bindings, and ABI tests
require no adaptation.

PR #44 changes only the Windows localhost endpoint used by USB/IP attach from
the hostname `localhost` to the numeric IPv4 loopback address `127.0.0.1`.
The change is applied consistently to the tracked native IOCTL path, the shared
command argument contract, and the legacy command fallback. The Addon already
creates its canonical VIIPER server on `127.0.0.1:3242`, so the new attach
endpoint matches the actual listener address and does not require any Addon
configuration or runtime change.

No attachment classification, native backend selection/fallback policy,
verified positive import-port token ownership, detach behavior, rollback,
server/bus ownership, lifecycle serialization, retryable/unknown result
handling, `close-failed` behavior, teardown ordering, callback contract, or PnP
readiness policy changes are included. `AttachUSBDeviceEx` continues to expose
the same classified result and the Addon continues to treat VIIPER attachment
state as native ownership evidence only, with separate exact Windows PnP
stabilization.

The upstream regression coverage verifies both command argument construction
and the native IOCTL host field use `127.0.0.1`. The canonical artifact comes
from the exact successful push/main run recorded above. This is a latency-path
implementation correction only; no hardware timing improvement is inferred
from automated validation, and existing hardware-validation claims remain
unchanged.

No Addon mapper, publisher, native binding, callback lifetime, feedback,
routing, Game Bar/Xbox360 presentation, PnP, HidHide, recovery, lifecycle, or
teardown code change is required for this dependency update.
<!-- AUTOMATION: END MANAGED ABI REVIEW SECTION -->

## Addon integration alignment

The following Addon files must remain aligned with this artifact:

- `CanonicalViiperNativeTypes.cs`
- `CanonicalViiperNativeApi.cs`
- `CanonicalViiperNativeAbiTests.cs`
- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`
- this provenance record
- `Dependencies/Viiper/viiper.lock.json` (machine-readable identity; verified by `scripts/verify-viiper.ps1`)

The active Addon composition uses the Steam Deck session, mapper, publisher,
identity resolver, and output stage. MSI Claw EX basic non-gyro controller
input is validated; lifecycle, recovery, rumble, haptics, gyro, and IMU
claims require separate evidence.
