# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     6cecc1feb6a14f2e9b6d879abb58374a34a99271
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
DLL SHA-256:              5bcfdb8e2c93baf682e419ca74c7931be213dc56d046c671ca127f7732289dd3
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `efda3e80b00366d478ff93354f0af4c7cc4c95ee` ->
`6cecc1feb6a14f2e9b6d879abb58374a34a99271`. The target is exactly one
canonical main commit, `Harden typed lifecycle serialization and server
isolation (#38)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed header: its SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no new or removed exports, signature changes, enum changes, struct
layout/packing changes, callback ABI changes, or Steam Deck state-layout
changes. `SteamDeckDeviceState` therefore remains 76 bytes with the existing
force fields at offsets 68/70/72/74. The current Addon managed P/Invoke
surface, `RequiredExports`, callback rooting, and ABI tests require no
adaptation.

PR #38 strengthens deterministic regression coverage for the existing
server-scoped lifecycle contract used by the planned long-lived Steam Deck +
Xbox360 composition. Production lifecycle semantics remain unchanged. The
existing nil-by-default internal attach lock-attempt test seam is consolidated
into `onLifecycleLockAttempt(operation)` and observed immediately before the
owning server `lifecycleMu` is acquired for attach, detach, typed remove, and
public close. In normal production the hook is nil, so the added calls are
behavior-neutral and do not alter ownership or sequencing.

The focused tests prove same-server serialization across attach/detach/remove/
close, exact committed attachment-token consumption, sticky unsafe-unknown
fail-closed behavior, public close serialization and retry semantics, caller-
owned bus preservation, Deck + Xbox360 coexistence on one bus, server-wide
`close-failed`, and isolation between separate `USBServerHandle` instances.
The documentation also makes explicit that typed mutations on one server
serialize at that server lifecycle boundary, while separate server handles
have independent lifecycle state; same-process VirtualBus BusID allocation is
process-global and therefore requires distinct BusIDs.

No Addon Steam Deck mapper, publisher, native binding, callback, routing, PnP,
HidHide, recovery, lifecycle-policy, or Xbox360 integration change is required
for this dependency update. The Addon still uses the existing Steam Deck typed
surface and bool attach/detach compatibility calls; adoption of the classified
attachment/query APIs and typed Xbox360 route remains separate planned work.

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
