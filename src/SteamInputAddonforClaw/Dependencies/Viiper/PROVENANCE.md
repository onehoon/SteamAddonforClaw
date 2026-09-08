# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     61b6fc236bf71ff4f723223373eabd39c8676ca2
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
DLL SHA-256:              5f2ce963b8ada1fde78bf4a1c25bf063503d761e3d42da6bb418fe735ce1f948
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `e00fbf01277a2c354a32b0e54418a9bd917a05ae` ->
`61b6fc236bf71ff4f723223373eabd39c8676ca2`. The generated canonical
`libVIIPER.h` is byte-identical to the previously embedded Addon header; its
SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no added or removed exports, C signature changes, enum changes,
typed Steam Deck/Xbox360 state-layout changes, callback-lifetime changes, or
managed P/Invoke / `RequiredExports` changes required by this update.

The runtime delta is internal to VIIPER's Windows usbip-win2 `0.9.8.0`
`PLUGIN_HARDWARE` request. The previous binding incorrectly flattened the C++
multiple-inheritance layout and omitted the three-byte tail padding of the
`imported_device_location` base subobject. The corrected Microsoft x64 ABI is:

```text
Size offset       = 0
PortOutput offset = 4
BusID offset      = 8
Service offset    = 40
Host offset       = 72
base tail padding = 1097..1099
Serial offset     = 1100
WskEvents offset  = 1116
struct size       = 1120
output prefix     = 8
```

`WskEvents=true` remains the fixed low-latency policy. Native attach ownership,
exact positive imported-port retention, exact-port detach, known-failure
fallback, and unknown-outcome fail-close behavior are unchanged. The
additional `DeviceIoControl` error log is diagnostic-only and does not alter
classification or lifecycle behavior.

SteamAddonforClaw continues to pin usbip-win2 `0.9.8.0`; no Addon managed ABI
change is required because the generated public VIIPER header did not change.
This dependency update does not claim new hardware validation; MSI Claw
runtime validation is still required after adoption.
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
