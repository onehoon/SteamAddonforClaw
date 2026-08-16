# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     e10b5f02945b1322f33c33468e583546600ba000
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
DLL SHA-256:              e6d0a13f58bd204f9259634f208d362a4c7044c7697c3b3b0d5afde6fb66b275
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `d1510dd559b284d9bebb50007d38b12d3ab5f822` ->
`e10b5f02945b1322f33c33468e583546600ba000`. The final target restores and
retains the same canonical managed/native Steam Deck ABI already consumed by
the Addon: the generated `libVIIPER.h` is byte-identical to the previously
reviewed header and its SHA-256 remains
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`.
`SteamDeckDeviceState` therefore remains 76 bytes, ending with
`LPadForce`/`RPadForce`/`LStickForce`/`RStickForce` at offsets 68/70/72/74.
No exported function, callback typedef, enum, struct field order, field offset,
packing, managed P/Invoke signature, or `RequiredExports` entry changes versus
the currently embedded Addon contract. No managed ABI adaptation is required.

The revision does contain a native Steam Deck input-report transport correction:
VIIPER now always declares the full 64-byte input report length in report byte 3
and preserves the established final four-byte stick-sensor tail at bytes 60:64.
The intermediate 72-byte VIIPER revision in this commit range is not the adopted
ABI contract; `e10b5f0` restores the 76-byte state before this artifact is built.
The Addon already exposes the matching `LStickForce`/`RStickForce` managed
fields and currently leaves them neutral, so this wire correction requires no
mapper, publisher, P/Invoke, callback, routing, lifecycle, attachment, recovery,
or HidHide code change.

The exhaustive Steam Deck button/D-pad/Menu-View semantics remain unchanged,
including `Start -> Menu` and `Back -> Options`. No hardware-validation claim
is expanded: MSI Claw EX basic non-gyro input remains the established claim;
lifecycle/recovery, rumble/haptics, gyro, and IMU validation remain separate
work.
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
