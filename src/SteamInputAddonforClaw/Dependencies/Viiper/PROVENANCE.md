# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     3283d3a7bef190000cca583dd94375ab383c8c8f
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
Generated header SHA-256: e5062922db6745a143c0c395bb682b4df2a35bb7ab67107ea0668e55e5cc70a9
DLL SHA-256:              e961a8e315850070475c215fa3ac9fe5e4aee3dd296d60576bc3308825bc7ab8
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed generated-header delta from VIIPER
`9ed7eeec6e92b3f54cd4ac6785da22db8725742d` to
`3283d3a7bef190000cca583dd94375ab383c8c8f` adds the following classified
attachment results and exports:

- `USBDeviceAttachResult`: `Success = 0`, `RetryableFailure = 1`,
  `UnsafeOutcomeUnknown = 2`, `Invalid = 3`.
- `USBDeviceDetachResult`: `Success = 0`, `RetryableFailure = 1`,
  `UnsafeOutcomeUnknown = 2`, `Invalid = 3`.
- `AttachUSBDeviceEx(uintptr_t) -> USBDeviceAttachResult`.
- `DetachUSBDeviceEx(uintptr_t) -> USBDeviceDetachResult`.

The existing `AttachUSBDevice` and `DetachUSBDevice` bool exports remain
available with their compatibility semantics. There are no
`SteamDeckDeviceState` layout changes, no `SteamDeckDeviceRemoveResult`
changes, and no Steam Deck callback ABI changes in this adoption. Existing
Addon production code continues to use the legacy bool attach/detach surface;
the new classified `*Ex` APIs are intentionally not adopted in managed runtime
code in this dependency-only PR. Their use belongs to the separate SD3
lifecycle/recovery work. The current bool attach path remains conservative and
fail-closed, while device removal already uses `RemoveSteamDeckDeviceEx` for
retryable and unsafe outcome classification.
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
