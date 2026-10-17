# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     6e952500165673c2c27e1b4723aab49e81133e2c
Branch:     main
Entrypoint: just build-libVIIPER Release
```

This revision provides the typed Steam Deck and optional DualSense Edge ABI
used by the active Addon runtime. The active Steam Deck identity is
`VID=0x28DE`, `PID=0x1205`; this identity applies only to Steam Deck and does
not describe the Edge device.

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
DLL SHA-256:              79d4a0853330eaa822cc260fc0525ae0b99d665f9bc64031105ac8f1190da3d7
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

The active Addon composition uses one canonical runtime/server and one
caller-owned USB bus. Xbox360, Steam Deck, and DualSense Edge typed logical
devices are all created and verified detached during shared-runtime
initialization; all three are required for runtime readiness. Only the
selected device is attached and published: Xbox360 for regular Windows use,
or Steam Deck (default) / optionally selected DualSense Edge for Steam/Big
Picture. Edge attachment and publisher activation are deferred until
selection; Edge logical-device creation is not lazy. The Steam Deck identity
`28DE:1205` is Deck-only. MSI Claw EX basic non-gyro controller input is
validated; physical Edge recognition, controls, motion orientation, PS/QAM,
rumble, and lifecycle require separate user validation after merge.
