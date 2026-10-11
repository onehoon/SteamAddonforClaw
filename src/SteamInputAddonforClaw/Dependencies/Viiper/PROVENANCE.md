# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     0689d18416cf6cde9ba4fed04eb373a1a3383ddc
Branch:     main
Entrypoint: just build-libVIIPER Release
```

This revision provides the typed Steam Deck and optional DualSense Edge ABI
used by the active Addon runtime. The active Steam Deck identity is
`VID=0x28DE`, `PID=0x1205`; this identity applies only to Steam Deck and does
not describe the Edge device.

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
Generated header SHA-256: 2e70012583747384c18f7229e270f5dfc092a79d0625e6f0bc999efe6e5dbde0
DLL SHA-256:              e7eb94adb4df0347928507f28bcd7491029663ff47d06cd3d85c00899c718015
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

This source-alignment review compares the checked-in generated
`Dependencies/Viiper/libVIIPER.h` with `CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`, and `CanonicalViiperNativeAbiTests.cs` for the
pinned artifact above.

- `DSDeviceHandle` is native `uintptr_t` and managed `nuint`. The generated
  header also retains classified `AttachUSBDeviceEx` / `DetachUSBDeviceEx` and
  `GetUSBDeviceAttachmentState`, alongside typed Steam Deck and Xbox360 state,
  callback, and removal exports.
- The Edge exports `CreateDualSenseEdgeDevice`, `SetDualSenseDeviceState`,
  `SetDualSenseOutputCallback`, and `RemoveDualSenseDeviceEx` are present in the
  pinned generated header. The Addon resolves them lazily and keeps them out of
  `RequiredExports`; a DLL without those optional symbols can still support
  ordinary startup, while an explicit Edge request reports the missing
  capability.
- `DSDeviceState` is passed by value with native default alignment and managed
  sequential layout. On x64 the struct is 36 bytes; the checked-in ABI tests
  assert `Buttons` at offset 4, `DPad` at 8, `GyroX` at 24, and `AccelX` at 30.
- `DSDeviceRemoveResult` is a four-byte C enum mirrored with values Success=0,
  RetryableFailure=1, UnsafeOutcomeUnknown=2, and Invalid=3.
- `DSOutputCallback` is a C `cdecl` callback with a pointer-width handle and
  six byte parameters: small/large rumble, RGB LED values, and player LEDs. The
  managed delegate and ABI tests assert that signature. The Addon uses only
  ordinary two-motor rumble and ignores LED values; it does not translate
  LED-only commands, adaptive triggers, audio, or specialized haptics.

These checked-in source and ABI tests establish the managed/native contract;
they do not establish Sony recognition, Steam Input compatibility, or physical
controller behavior. This review records the current contract and does not
claim a complete list of changes relative to an earlier header revision.
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

The active Addon composition uses one canonical runtime: Xbox360 for Windows
mode and Steam Deck (default) or optionally selected DualSense Edge for
Steam/Big Picture, with only one attached output and publisher at a time. Edge
logical-device creation remains lazy. The Steam Deck identity `28DE:1205` is
Deck-only. MSI Claw EX basic non-gyro controller input is validated; physical
Edge recognition, controls, motion orientation, PS/QAM, rumble, and lifecycle
require separate user validation after merge.
