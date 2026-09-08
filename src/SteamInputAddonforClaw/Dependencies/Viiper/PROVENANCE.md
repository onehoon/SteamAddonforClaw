# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     e00fbf01277a2c354a32b0e54418a9bd917a05ae
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
DLL SHA-256:              0ece53486de369167b92482957ff0b41bb2ce760a2d534d066dc68be33768f75
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `77a8af547de2253862ede648a212c01d4dd950c1` ->
`e00fbf01277a2c354a32b0e54418a9bd917a05ae`. The generated canonical
`libVIIPER.h` is byte-identical to the previously reviewed Addon header;
the header SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no added or removed exports, C signature changes, enum changes,
Steam Deck or Xbox360 typed-state layout changes, or managed P/Invoke changes.
The existing typed-handle ownership, callback lifetime, classified attachment
and teardown contracts remain unchanged.

The compatibility change is internal to the Windows usbip-win2 attach path.
VIIPER now targets usbip-win2 `0.9.8.0`, commit
`83bd1f781d57ed6efdf15530c55710cf5d4482bc`, and its `plugin_hardware`
request includes the required `Serial[16]` field at offset 1097 and
`WskEvents` at offset 1113, for a complete request size of 1116 bytes.
The request sets `WskEvents=true`; the command fallback likewise selects
`--receive-mode=low-latency`. This PR bundles and validates the matching
official usbip-win2 `0.9.8.0` x64 installer, so the native ABI and provisioned
package move together atomically. Older or later usbip-win2 versions are not
claimed compatible, and no compatibility bridge or automatic downgrade is
introduced.

The Addon managed ABI remains unchanged because the generated public header
did not change. The focused VIIPER native ABI tests verify the 0.9.8.0
request size, field offsets, WSK policy, and low-latency command arguments;
the Addon prerequisite tests verify the `0.9.7.7` -> `0.9.8.0`
`UpdateRequired` path and preserve exact-version runtime fail-close behavior.
No additional hardware validation is claimed by this dependency update.
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
