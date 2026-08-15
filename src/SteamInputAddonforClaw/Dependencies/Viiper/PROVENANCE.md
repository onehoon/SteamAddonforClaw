# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     0b3627317d2008065d8ec231f94bf31af7527bbd
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
Generated header SHA-256: af9e08712fe9a33479e825ef5f8a6b2f0c283eb5e3e69027130484071049bced
DLL SHA-256:              b2050ea357a6b663a97c5ede9ab01a134162ccb3661d96e318897f40a29b59ea
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

This section holds the human-reviewed ABI delta for the currently embedded
revision. A future dependency-automation adoption resets this section to an
evergreen "review required" placeholder before anything is merged -- it is
never synthesized from a new header, and a human must replace the
placeholder with the actual reviewed delta (as below) before that PR can
merge.

Reviewed delta for `0b3627317d2008065d8ec231f94bf31af7527bbd` (adopted in
Phase 2B2, from `ec64282c69e5587466b950332d7983fd53a7d778`): diffing the full
exported function list of both generated headers shows exactly one added
export, `SetSteamDeckOutputCallback` (and its `SteamDeckOutputCallback`
delegate typedef); nothing else was added or removed.
`SteamDeckDeviceState`/`SteamDeckDeviceRemoveResult` are byte-for-byte
unchanged:

```text
sizeof(SteamDeckDeviceState) = 76
sizeof(SteamDeckDeviceRemoveResult) = 4
L2Digital = 6, R2Digital = 7, R3 = 22, QuickAccess = 27
LPadX = 28, AccelX = 36, LTrigger = 56, LStickX = 60, RStickX = 64
```

The managed `ICanonicalViiperNativeApi` surface, `RequiredExports`, and
callback-lifetime rooting (`CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`) were updated in the same change to bind
`SetSteamDeckOutputCallback` and keep the native and managed ABI aligned. No
production caller registers the Steam Deck output callback yet; Addon
rumble/haptics handling remains a separate, later feature track.
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
