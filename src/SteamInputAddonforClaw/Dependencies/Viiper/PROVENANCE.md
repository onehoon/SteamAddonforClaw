# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     49e5796b9f31f8ddb7009fde6f910c66837e2315
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
DLL SHA-256:              2db4da332a012ae1212f64b2e1cb0d2a27d6b1b723dd01bd4367ec342f85c9e3
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `a6bb749199aa797da690c611d2f18edc5e770c1e` ->
`49e5796b9f31f8ddb7009fde6f910c66837e2315`. The target is exactly one
canonical main commit, `Correct Steam Deck 0x83 attributes (#43)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed Addon header. The Addon base and dependency-PR head vendored headers
have the same Git blob identity,
`2ab164e4d37c7cfde6e9a0771c3c5489183b0a03`, and the generated-header
SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`.
There are no added or removed exports, signature changes, enum changes, struct
layout/packing changes, callback ABI changes, or Steam Deck/Xbox360 typed-state
layout changes. `SteamDeckDeviceState` remains 76 bytes with `LPadForce`,
`RPadForce`, `LStickForce`, and `RStickForce` at offsets 68/70/72/74. The
current Addon managed P/Invoke surface, 19-entry `RequiredExports`, classified
attach/detach bindings, callback rooting, Xbox360 typed bindings, and ABI tests
require no adaptation.

PR #43 changes only the Steam Deck device-side handling of feature command
`GET_ATTRIBUTES_VALUES (0x83)` plus constants and focused tests. The virtual
Deck now returns the observed real-device nine-entry / 45-byte payload in the
exact order `01 -> 02 -> 0A -> 04 -> 09 -> 0B -> 0D -> 0C -> 0E`, with VIIPER
framing `83 2D ...`. ProductID remains derived from the device descriptor so a
caller-supplied PID override is preserved; the remaining corrected values and
unknown tags follow the reviewed real-device reference payload. Regression
coverage checks the byte-exact default response, zero padding, and custom-PID
behavior.

This feature response is generated inside `device/steamdeck` when the USB host
queries the virtual controller. SteamInputAddonforClaw does not construct,
parse, cache, or otherwise own the `0x83` payload, so no Addon mapper,
publisher, native binding, callback, feedback, routing, PnP, HidHide, or
recovery code needs to change for this correction.

No descriptor, input-report, output-callback, attachment, removal, USB/IP,
transport, server/bus ownership, lifecycle, or teardown implementation changes
are included in the delta. Caller-owned bus lifetime, persistent detached-ready
Steam Deck/Xbox360 logical devices, classified attachment ownership,
per-server lifecycle serialization, exact-token detach, `close-failed`,
post-unlock diagnostics, callback draining, and fail-closed unknown-outcome
semantics remain unchanged.

This is a Steam Deck protocol-fidelity correction, not a rumble or haptics
implementation change. The corrected `0x83` response may affect how Steam
classifies or interacts with the virtual Deck, but no feedback improvement or
hardware behavior is inferred from the software change. Existing hardware
validation claims remain unchanged; any effect on Steam feedback traffic still
requires separate real-hardware A/B evidence.
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
