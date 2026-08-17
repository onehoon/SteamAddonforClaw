# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     249c0cfa88154d77cd1683af03fb9d85ac6af426
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
Generated header SHA-256: ff78cc701e4fb17a46aa74897210e23f80d73f6d3bbbb1e170bd278786f2a211
DLL SHA-256:              4260c4b3690361658137c99c98500acadaafde4b9ea4fa7e350082cf184cecd6
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `e10b5f02945b1322f33c33468e583546600ba000` ->
`249c0cfa88154d77cd1683af03fb9d85ac6af426`. The target is exactly one
upstream commit, `Add classified Xbox360 removal parity (#34)`. Its canonical
source delta is limited to the Xbox360 typed wrapper, focused Xbox360 removal
tests, the generated-ABI/export CI checks, and the fork API documentation.

The generated `libVIIPER.h` delta adds only the four-value
`Xbox360DeviceRemoveResult` enum and the additive
`RemoveXbox360DeviceEx(Xbox360DeviceHandle)` export. The existing
`RemoveXbox360Device` compatibility bool export remains available and keeps
its prior signature. No Steam Deck type, struct field, field order, offset,
packing, callback typedef, or Steam Deck export changes. In particular,
`SteamDeckDeviceState` remains the established 76-byte ABI ending with
`LPadForce`/`RPadForce`/`LStickForce`/`RStickForce` at offsets 68/70/72/74.

The current Addon managed native surface binds the generic server/bus/
attachment functions and the Steam Deck typed family only; it does not yet
bind or require Xbox360 exports. Xbox360 composition remains the planned SD7
feature track. Therefore this additive, currently unused Xbox360 export does
not require a managed P/Invoke, `RequiredExports`, struct/enum, callback,
mapper, publisher, routing, attachment, recovery, or lifecycle code change in
this dependency PR. When SD7 adopts the typed Xbox360 family, its managed
surface should consume the classified removal API rather than inferring
ownership from the legacy bool result.

No hardware-validation claim is expanded. MSI Claw EX basic non-gyro Steam
Deck input remains the established claim; lifecycle/recovery, rumble/haptics,
gyro/IMU, and Game Bar/Xbox360 validation remain separate work.
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
