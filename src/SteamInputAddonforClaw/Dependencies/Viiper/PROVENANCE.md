# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     af2615e80aec290ee61190c5da4813349b78ca56
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
DLL SHA-256:              a7f5b5bc97987d64dacc2ddf189875c96a318be68a86417ec10f68a16f9bbf01
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `ba63b9909f84bcabeddd4b1299beffe76ba04b4f` ->
`af2615e80aec290ee61190c5da4813349b78ca56`. The target is exactly one
upstream commit, `Fix Windows version resources for untagged builds (#36)`.
Its source delta is limited to `.github/workflows/build_base.yml`,
`scripts/inject-version.ps1`, and the new focused
`scripts/tests/test-inject-version.ps1` regression suite.

The canonical generated `libVIIPER.h` is byte-identical to the previously
reviewed header: its SHA-256 remains
`ff78cc701e4fb17a46aa74897210e23f80d73f6d3bbbb1e170bd278786f2a211`.
There are no new or removed exports, signature changes, enum or struct-layout
changes, callback ABI changes, or Steam Deck state-layout changes. The Addon
managed P/Invoke surface, `RequiredExports`, callback rooting, and ABI tests
therefore require no adaptation.

The native library source and runtime lifecycle implementation are unchanged.
PR #36 only hardens Windows version-resource generation for untagged builds:
semver, git-describe, four-component versions, and Git SHA inputs are parsed
explicitly; raw SHA builds use numeric `0.0.0.0` while preserving the SHA in
the string ProductVersion; malformed or out-of-range values fail explicitly.
The canonical artifact authority remains the exact full Git commit plus
`viiper-artifact.json` and the DLL/header hashes. The DLL SHA-256 changes
because the generated Windows version resource changes, not because of an ABI,
typed-device, USB/IP, attachment, removal, callback, or routing change.

No Steam Deck mapper, publisher, managed ABI, callback, routing, PnP, HidHide,
recovery, lifecycle-policy, or Xbox360 integration change is required in the
Addon for this dependency update. No hardware-validation claim is expanded;
MSI Claw EX basic non-gyro Steam Deck input remains the established claim and
SD3 lifecycle/recovery, rumble/haptics, gyro/IMU, and Game Bar/Xbox360 remain
separate work.
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
