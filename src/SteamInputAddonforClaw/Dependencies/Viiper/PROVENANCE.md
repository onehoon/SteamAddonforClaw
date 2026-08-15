# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     a8a00efe7a5dce0c8d95de16795797a7daa7d82a
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
Generated header SHA-256: e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc
DLL SHA-256:              e07349aa76b9c1adf958607dfd147d9e599f0921c84bdbe593e9e53bde289e8c
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed cumulative generated-header delta from VIIPER
`9ed7eeec6e92b3f54cd4ac6785da22db8725742d` to
`a8a00efe7a5dce0c8d95de16795797a7daa7d82a` is:

- `USBDeviceAttachResult`: `VIIPER_ATTACH_SUCCESS = 0`,
  `VIIPER_ATTACH_RETRYABLE_FAILURE = 1`,
  `VIIPER_ATTACH_UNSAFE_OUTCOME_UNKNOWN = 2`, `VIIPER_ATTACH_INVALID = 3`.
- `USBDeviceDetachResult`: `VIIPER_DETACH_SUCCESS = 0`,
  `VIIPER_DETACH_RETRYABLE_FAILURE = 1`,
  `VIIPER_DETACH_UNSAFE_OUTCOME_UNKNOWN = 2`, `VIIPER_DETACH_INVALID = 3`.
- `AttachUSBDeviceEx(uintptr_t)`.
- `DetachUSBDeviceEx(uintptr_t)`.
- `USBDeviceAttachmentState`: `VIIPER_ATTACHMENT_DETACHED = 0`,
  `VIIPER_ATTACHMENT_ATTACHED = 1`,
  `VIIPER_ATTACHMENT_OUTCOME_UNKNOWN = 2`.
- `GetUSBDeviceAttachmentState(uintptr_t, USBDeviceAttachmentState*)`.

The existing `AttachUSBDevice(uintptr_t)` and `DetachUSBDevice(uintptr_t)`
bool exports remain available with compatibility semantics: they invoke the
same classified mutation operation and return `true` only for `SUCCESS`.
`GetUSBDeviceAttachmentState` is read-only and reports VIIPER's tracked
localhost attachment ownership only. `ATTACHED` is not Windows PnP, HID,
XInput, or Steam readiness; Addon-side exact PnP stabilization and ownership
checks remain required. `OUTCOME_UNKNOWN` is a fail-closed native ownership
state.

`SteamDeckDeviceState` layout, `SteamDeckDeviceRemoveResult`,
`SteamDeckOutputCallback`, and the existing typed Steam Deck
create/state/remove exports are unchanged. The VIIPER #25 generated-header
whitespace normalization is formatting-only and does not change the ABI.
All new exports and types are additive, so the existing Addon managed ABI
remains compatible. The current Addon production path does not call
`AttachUSBDeviceEx`, `DetachUSBDeviceEx`, or
`GetUSBDeviceAttachmentState`; managed adoption is intentionally deferred to
SD3 lifecycle/recovery work.
Replace this paragraph with the reviewed ABI delta -- including any changed
struct layout, offsets, or exports -- once confirmed.
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
