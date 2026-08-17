# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     b55b435a63c85430d2a00949014d5c0892c8af67
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
DLL SHA-256:              db6fc51ddc17635e48b7192afc45040d6a3aa992a984111354566901b7a8e260
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `74e8448023e6f48b6e3dc8dbffd5278b53390e64` ->
`b55b435a63c85430d2a00949014d5c0892c8af67`. The target is exactly one
canonical main commit, `Make rejected mutation logging lifecycle-lock safe
(#40)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed header. Its SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`,
and the vendored header has the same Git blob identity as the prior Addon
`main` header. There are no added or removed exports, signature changes, enum
changes, struct layout/packing changes, callback ABI changes, or Steam Deck
state-layout changes. `SteamDeckDeviceState` remains 76 bytes with
`LPadForce`, `RPadForce`, `LStickForce`, and `RStickForce` at offsets
68/70/72/74. The current Addon managed P/Invoke surface, `RequiredExports`,
callback rooting, and ABI tests require no adaptation.

PR #40 changes diagnostic emission ordering rather than lifecycle results.
Rejected server-mutation warnings now use a two-phase boundary: bounded
operation+server-state de-duplication is decided while `lifecycleMu` is held,
then the captured warning is emitted only after that lock is released. The
successful typed state/callback mutation path still executes under
`lifecycleMu`; rejected mutation results remain rejected with the same server
state and ownership evidence.

Public typed create paths, including Steam Deck and Xbox360, now call the
shared create helper while holding `lifecycleMu`, then unlock before emitting
a captured rejection warning or rollback-failure diagnostic. Auto-attach,
logical-handle creation/finalization, attachment ownership, and rollback
results are unchanged. If rollback itself fails, the owning server still
transitions to `close-failed` under the lifecycle lock; only the already
captured error record is deferred until after unlock. `CreateUSBBus` follows
the same post-unlock warning boundary. Redundant rejected Attach/Detach and
`RemoveUSBBus` warning calls are removed where the existing canonical
post-unlock attachment/teardown diagnostics already report the rejected
result; this removes duplicate synchronous logging without changing the
operation result.

The focused regression suite checks rejected typed mutation and typed creation
remain de-duplicated and emit lock-free, rollback failure logging occurs only
after unlock while preserving `close-failed`, rejected `CreateUSBBus` logging
is lock-safe, and rejected known-ownership Attach/Detach still return the same
classified result while their canonical timing diagnostics remain lock-free.
This aligns with the Addon rule that logging-only changes remain
behavior-neutral and with the native contract that embedding callbacks must
not execute while the lifecycle lock is held.

The Addon's registered VIIPER callback remains narrowly filtered to the
existing `VIIPER.DPad` diagnostic prefix, so these generic rejection/rollback
records are not forwarded into the Addon product log. No Addon Steam Deck
mapper, publisher, native binding, callback lifetime, routing, PnP, HidHide,
recovery, lifecycle policy, or planned Xbox360 route change is required for
this dependency update. The Addon continues to use the existing bool
attach/detach compatibility surface; classified attachment/query adoption
remains SD3 lifecycle/recovery work.

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
