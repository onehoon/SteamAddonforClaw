# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     566e4f88577a14c574ed7bf47e37bd75ea78f8d9
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
DLL SHA-256:              a4edca701979dbec3ed35ebd5e4cc0fb77819dab846367387761fe73dd7fd835
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed VIIPER delta from
`89ce1426883ea5001b5788000df272db7532f0e1` to
`566e4f88577a14c574ed7bf47e37bd75ea78f8d9` contains two internal Steam Deck
protocol-correctness fixes:

- `d8543793783d31b1f0f96c74157d9bca038f595e` corrects setting `0x09` to
  `MousePointerEnabled` semantics and removes the incorrect controller-mode
  side effect; `SetControllerMode` remains the sole controller-mode mutation
  path.
- `566e4f88577a14c574ed7bf47e37bd75ea78f8d9` corrects internal parsing of the
  Steam Deck `0xEB` rumble command (`RumbleType`, 16-bit intensity, left/right
  speeds, and signed gain bytes).

The canonical generated `libVIIPER.h` is byte-identical to the previously
reviewed embedded header: its SHA-256 remains
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`.
Neither VIIPER commit changes `lib/viiper`'s exported C declarations or typed
Steam Deck ABI surface. No exported function signature, callback typedef,
enum, struct layout, field offset, or packing change occurred.

The `RumbleCommand` model changed only inside `device/steamdeck`; it is not a
public C ABI type. `SetSteamDeckOutputCallback` continues to expose the same
raw normalized byte callback contract (`device handle`, `const uint8_t*`,
`uint32_t length`), so the Addon's managed callback/PInvoke surface and
`RequiredExports` remain compatible without adaptation.

Accordingly, no changes are required to `CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`, `CanonicalViiperNativeAbiTests.cs`, routing,
Steam Deck mapping, publisher behavior, attachment ownership, teardown, or
recovery policy. The dependency update changes only the vendored native
implementation plus its mechanical pin/provenance/hash alignment.

No hardware-validation claim is expanded. MSI Claw EX basic non-gyro input
remains the established claim; lifecycle/recovery, rumble/haptics, gyro, and
IMU validation remain separate work.
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
