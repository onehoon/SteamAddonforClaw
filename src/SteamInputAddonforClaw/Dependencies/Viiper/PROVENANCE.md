# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     74e8448023e6f48b6e3dc8dbffd5278b53390e64
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
DLL SHA-256:              137fb9190d0d1e1f12f3dcf3fe3637e9d2ae0987a82d915cd107a09292073c8f
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `6cecc1feb6a14f2e9b6d879abb58374a34a99271` ->
`74e8448023e6f48b6e3dc8dbffd5278b53390e64`. The target is exactly one
canonical main commit, `Complete lifecycle teardown diagnostics (#39)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed header: its SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no added/removed exports, signature changes, enum changes, struct
layout or packing changes, callback ABI changes, or Steam Deck state-layout
changes. `SteamDeckDeviceState` remains 76 bytes with the existing force
fields at offsets 68/70/72/74. The current Addon managed P/Invoke surface,
`RequiredExports`, callback rooting, and ABI tests therefore require no
adaptation.

PR #39 completes low-volume canonical teardown diagnostics for typed
`Remove*Device` / `Remove*DeviceEx`, public `RemoveUSBBus`, and public
`CloseUSBServer`. The diagnostic data is internal and is not a public ABI
contract. It records operation/phase/classified result, authoritative logical
identity and attachment token evidence where relevant, server/attachment state
transitions, backend-call/error evidence, counts, and total duration.
Production lifecycle and ownership semantics remain unchanged.

The implementation preserves the existing mutation paths. Typed removal now
calls the already-canonical `detachDeviceLockedResult` directly only so it can
capture whether the detach backend was invoked; the old bool helper was a
strict `...Result == SUCCESS` projection. Bus preflight/detach helpers now
return the actual failing device and diagnostic snapshot in addition to the
same pass/fail outcome. `CloseUSBServer` keeps the same active / closing /
close-failed / closed transitions, partial-close behavior, retry semantics,
caller-owned bus rules, and sticky unsafe-unknown handling. Invalid handles
remain non-mutating and are logged through the library-owned fallback logger.

Safety-sensitive ordering is preserved: authoritative teardown snapshots are
taken while holding the owning server lifecycle lock, but teardown records are
emitted only after `lifecycleMu` is released and, where applicable, after the
required transport drains. The regression suite explicitly checks lock-free
log emission as well as attached/detached success, known detach failure,
unknown detach, logical removal failure, wrong-family rejection, missing bus,
actual failing-device attribution, bus backend reconciliation, server-close
success/failure/retry, and deterministic unknown-attachment attribution.
Routine teardown diagnostics are low-volume and do not add per-input/per-frame
logging.

The Addon's registered VIIPER callback remains narrowly filtered to the
existing `VIIPER.DPad` diagnostic prefix, so these new canonical teardown
records are not forwarded into the Addon product log. VIIPER's own native
`libVIIPER.log` gains the richer teardown evidence as intended.

No Addon Steam Deck mapper, publisher, native binding, callback lifetime,
routing, PnP, HidHide, recovery, lifecycle policy, or planned Xbox360 route
change is required for this dependency update. The Addon continues to use the
existing bool attach/detach compatibility surface; classified attachment/query
adoption remains SD3 lifecycle/recovery work.

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
