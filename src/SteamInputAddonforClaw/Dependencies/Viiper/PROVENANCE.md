# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     348bf6f8695e69c629cbb8c358173440d2d7588a
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
Generated header SHA-256: e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc
DLL SHA-256:              eb0ad8742c75bcde619c82d5bfc4b8e7e93fabe9727494cd9baa219747234ee1
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed VIIPER delta from
`566e4f88577a14c574ed7bf47e37bd75ea78f8d9` to
`348bf6f8695e69c629cbb8c358173440d2d7588a` is an internal Steam Deck
host-output parser safety fix.

The VIIPER change is confined to `device/steamdeck/inputstate.go` plus focused
regression tests. `AsRumble`, `AsHaptic`, and `AsHapticPulse` now reject
captured commands shorter than the minimum parser length, and `AsPlayAudio`
rejects a payload length that exceeds the actual captured output length rather
than reading zero-filled bytes from the fixed backing buffer. The change does
not alter the normalized raw host-output bytes delivered through the canonical
C callback.

The canonical generated `libVIIPER.h` is byte-identical to the previously
reviewed embedded header: its SHA-256 remains
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`.
No `lib/viiper` exported C declaration changed. No exported function signature,
callback typedef, enum, struct layout, field offset, packing, or
`RequiredExports` change occurred.

`SetSteamDeckOutputCallback` therefore retains the same raw normalized byte
callback contract (`device handle`, `const uint8_t*`, `uint32_t length`). No
changes are required to `CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`, `CanonicalViiperNativeAbiTests.cs`, callback
lifetime rooting, routing, Steam Deck mapping, publisher behavior, attachment
ownership, teardown, or recovery policy.

This dependency update changes only the vendored native implementation plus
its mechanical pin/provenance/hash alignment. No hardware-validation claim is
expanded. MSI Claw EX basic non-gyro input remains the established claim;
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
