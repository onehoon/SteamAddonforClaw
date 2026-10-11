# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     0689d18416cf6cde9ba4fed04eb373a1a3383ddc
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
Generated header SHA-256: 2e70012583747384c18f7229e270f5dfc092a79d0625e6f0bc999efe6e5dbde0
DLL SHA-256:              e7eb94adb4df0347928507f28bcd7491029663ff47d06cd3d85c00899c718015
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

ABI compatibility is not inferred by the dependency automation. Review the
generated `libVIIPER.h` diff and the managed interop
(`CanonicalViiperNativeApi.cs`, `CanonicalViiperNativeTypes.cs`,
`CanonicalViiperNativeAbiTests.cs`) before merging this dependency update.
Replace this paragraph with the reviewed ABI delta -- including any changed
struct layout, offsets, or exports -- once confirmed.
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
