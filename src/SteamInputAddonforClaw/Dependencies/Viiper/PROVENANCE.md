# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     ba63b9909f84bcabeddd4b1299beffe76ba04b4f
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
DLL SHA-256:              10a4a5e6df632dac0a3f53e34f6ec96b0a19bb67514aa6ef05e8f35c06eefba5
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `249c0cfa88154d77cd1683af03fb9d85ac6af426` ->
`ba63b9909f84bcabeddd4b1299beffe76ba04b4f`. The target is exactly one
upstream commit, `Harden canonical USB/IP attach invariants and diagnostics
(#35)`.

The canonical generated `libVIIPER.h` is byte-identical to the previously
reviewed header: its SHA-256 remains
`ff78cc701e4fb17a46aa74897210e23f80d73f6d3bbbb1e170bd278786f2a211`.
There are no new or removed exports, signature changes, enum or struct-layout
changes, callback ABI changes, or Steam Deck state-layout changes. The Addon
managed P/Invoke surface and `RequiredExports` therefore remain aligned and
require no adaptation.

The native delta hardens regression coverage around the existing canonical
attachment contract and enriches low-volume attach/detach timing diagnostics.
Attachment diagnostics now snapshot logical/export identity, tracked token
identity, and before/after attachment/server lifecycle state under the native
lifecycle lock, then emit after releasing that lock. The upstream contract and
PR explicitly classify this as behavior-neutral; the bool and classified
attach/detach APIs retain their existing semantics, including idempotent
attach/detach, sticky unsafe-unknown ownership, explicit reattach, and
`autoAttachLocalhost=false` detached-ready behavior. Creation does not schedule
background attachment.

The Xbox360 wrapper change extracts a thin internal helper so the same public
creation path can be exercised directly by focused tests; it does not alter
the public ABI or require Addon Xbox360 adoption. The new regression suite also
covers the Steam Deck detached-ready path using the existing public wrapper
semantics. No Steam Deck mapper, publisher, P/Invoke, callback rooting,
routing, PnP, HidHide, recovery, or lifecycle-policy code change is required
in the Addon for this dependency update.

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
