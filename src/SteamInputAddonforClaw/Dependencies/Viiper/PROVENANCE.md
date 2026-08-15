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
runtime, including the `SetSteamDeckOutputCallback` output-callback binding
adopted in this revision (see "ABI changes in this revision" below). The
active virtual output identity is `VID=0x28DE`, `PID=0x1205`.

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

The generated header confirms the pinned Deck ABI layout, including:

```text
sizeof(SteamDeckDeviceState) = 76
sizeof(SteamDeckDeviceRemoveResult) = 4
L2Digital = 6, R2Digital = 7, R3 = 22, QuickAccess = 27
LPadX = 28, AccelX = 36, LTrigger = 56, LStickX = 60, RStickX = 64
```

These offsets and the struct size are unchanged from the previously-adopted
`ec64282c69e5587466b950332d7983fd53a7d778` revision. CI verifies the
committed hashes.

## ABI changes in this revision

Adopting `0b3627317d2008065d8ec231f94bf31af7527bbd` (from
`ec64282c69e5587466b950332d7983fd53a7d778`) adds exactly one new export
compared to the previous revision, confirmed by diffing the full exported
function list of both generated headers:

```text
SteamDeckOutputCallback     (new native delegate typedef)
SetSteamDeckOutputCallback  (new export)
```

No other export was added or removed, and `SteamDeckDeviceState` /
`SteamDeckDeviceRemoveResult` are byte-for-byte unchanged. This Addon
revision adds the managed P/Invoke binding and callback-lifetime rooting for
`SetSteamDeckOutputCallback` (`CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`) so the native and managed ABI move
atomically. No production caller registers the Steam Deck output callback
yet; Addon rumble/haptics handling remains a separate, later feature track.

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
