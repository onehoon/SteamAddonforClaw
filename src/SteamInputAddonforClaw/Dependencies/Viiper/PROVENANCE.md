# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     efda3e80b00366d478ff93354f0af4c7cc4c95ee
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
DLL SHA-256:              9ef3923f59407af8d79bc5713ebcf632ae5dfe44a8432511af50690505422cd0
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `ba63b9909f84bcabeddd4b1299beffe76ba04b4f` ->
`efda3e80b00366d478ff93354f0af4c7cc4c95ee`. The target is two canonical
main commits ahead and therefore supersedes the intermediate `af2615e` update:

1. `af2615e80aec290ee61190c5da4813349b78ca56` — `Fix Windows version resources for untagged builds (#36)`.
2. `efda3e80b00366d478ff93354f0af4c7cc4c95ee` — `Complete classified removal parity for typed devices (#37)`.

PR #36 is build/resource tooling only. It hardens Windows version-resource
generation for semver, git-describe, four-component numeric versions, and raw
Git SHA inputs. It does not modify libVIIPER runtime, USB/IP lifecycle, typed
device behavior, or public ABI.

PR #37 is an additive public-ABI extension for typed families that the current
Addon does not bind: DualSense/DualSense Edge, DualShock 4, Nintendo Switch 2
Pro, Keyboard, and Mouse. The generated header adds one four-value classified
remove enum and one `Remove*DeviceEx` export for each family:

- `DSDeviceRemoveResult` / `RemoveDualSenseDeviceEx`;
- `DS4DeviceRemoveResult` / `RemoveDS4DeviceEx`;
- `NS2ProDeviceRemoveResult` / `RemoveNS2ProDeviceEx`;
- `KeyboardDeviceRemoveResult` / `RemoveKeyboardDeviceEx`;
- `MouseDeviceRemoveResult` / `RemoveMouseDeviceEx`.

Each enum preserves the established classified-removal values `SUCCESS = 0`,
`RETRYABLE_FAILURE = 1`, `UNSAFE_OUTCOME_UNKNOWN = 2`, and `INVALID = 3`.
The legacy bool `Remove*Device` exports remain available and are strict
`SUCCESS -> true` compatibility projections. The shared typed removal
lifecycle, caller-owned bus lifetime, callback teardown ordering, attachment
ownership, and fail-closed unsafe-unknown semantics are unchanged.

The current Addon `ICanonicalViiperNativeApi` and `RequiredExports` bind only
the generic server/bus/attachment surface plus the Steam Deck typed family.
They do not bind any of the five newly extended families. The Steam Deck ABI is
unchanged: no Steam Deck export, callback typedef, state field, field order,
packing, or enum change; `SteamDeckDeviceState` remains 76 bytes with
`LPadForce`, `RPadForce`, `LStickForce`, and `RStickForce` at offsets
68/70/72/74. Existing Addon Steam Deck P/Invoke definitions, callback rooting,
mapper, publisher, session, routing, PnP, HidHide, recovery, and lifecycle
policy therefore require no adaptation for this dependency update.

No hardware-validation claim is expanded. MSI Claw EX basic non-gyro Steam
Deck input remains the established claim; SD3 lifecycle/recovery evidence,
rumble/haptics, gyro/IMU, and Game Bar/Xbox360 validation remain separate work.
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
