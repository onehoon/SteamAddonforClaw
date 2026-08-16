# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     522d573f67a693500ef96174aef318f62e8caeef
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
Generated header SHA-256: ad57285889553a6c92d974e47843cc6946da801604e696b4958211e09800c720
DLL SHA-256:              38e30e20eba4572b4cb91687993fcda5df97c23abc277c58c325a18fa0c2d8f8
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `d1510dd559b284d9bebb50007d38b12d3ab5f822` ->
`522d573f67a693500ef96174aef318f62e8caeef`. **This is a breaking
`SteamDeckDeviceState` ABI change**, not a mechanical no-op adoption.

Upstream removed the non-canonical `LStickForce`/`RStickForce` tail fields
from the canonical Steam Deck state struct -- they had no corresponding
field in the declared Valve/SDL/Linux Steam Deck payload. The native struct
shrank from 76 to 72 bytes; `LPadForce` (offset 68) and `RPadForce`
(offset 70) are now the native tail. Every preceding field offset is
unchanged. No Steam Deck export was added or removed, and no callback
typedef changed -- `SetSteamDeckDeviceState(nuint, SteamDeckDeviceState)`
retains its exact signature (the struct is still passed by value; the
narrower layout is the only difference), so `RequiredExports` required no
change.

The Addon's managed `SteamDeckDeviceState` (`CanonicalViiperNativeTypes.cs`)
was updated to the matching 72-byte definition, with `LStickForce`/
`RStickForce` removed and every surviving field left in its original
declared order. `SteamDeckDeviceStateMapper`'s references to the removed
fields were removed; trackpad pressure (`LPadForce`/`RPadForce`) remains a
valid canonical field and stays neutral, unchanged, in the current Addon
feature scope. `CanonicalViiperNativeAbiTests` was updated to pin the
72-byte size and the corrected tail offsets, and a new regression
(`SteamDeckDeviceState_DoesNotExposeRemovedStickForceTailFields`) asserts
via reflection that `LStickForce`/`RStickForce` are absent from the managed
type, so an accidental reintroduction of the obsolete 76-byte tail fails
loudly.

Routing, lifecycle, attachment, and recovery behavior are unchanged -- this
PR is VIIPER dependency adoption plus exact managed ABI alignment for the
72-byte Steam Deck state, nothing more. No hardware-validation claim is
expanded: MSI Claw EX basic non-gyro input remains the established claim;
lifecycle/recovery, rumble/haptics, gyro, and IMU validation remain separate
work.
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
