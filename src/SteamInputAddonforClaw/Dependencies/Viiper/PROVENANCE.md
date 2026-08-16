# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     d1510dd559b284d9bebb50007d38b12d3ab5f822
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
DLL SHA-256:              304f85467069d48ebcfb7cda9c50f65a5f8b38c2e7bc597b832a6ba997fa9483
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `348bf6f8695e69c629cbb8c358173440d2d7588a` ->
`d1510dd559b284d9bebb50007d38b12d3ab5f822`. The upstream commit is test-only:
it changes only `device/steamdeck/steamdeck_test.go`, adding exhaustive Steam
Deck mapping regression tests. No VIIPER production implementation changed.

The canonical generated `libVIIPER.h` is byte-identical to the previously
reviewed embedded header: its SHA-256 remains
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`. No
exported function, callback typedef, enum, struct layout, field offset,
packing, P/Invoke, or `RequiredExports` change occurred. No managed ABI
adaptation is required, and none was made: `CanonicalViiperNativeApi.cs`,
`CanonicalViiperNativeTypes.cs`, and `CanonicalViiperNativeAbiTests.cs` are
unchanged.

The new upstream exhaustive tests independently confirmed the canonical
Steam Deck center-button semantics:

- `SteamDeckDeviceState.Menu` = the native `MENU` / `Start` semantic.
- `SteamDeckDeviceState.Options` = the native `VIEW` / `Back` semantic.

Cross-checking that against `SteamDeckDeviceStateMapper` surfaced a
pre-existing Addon consumer bug: the mapper had these two reversed (`Menu`
was driven from `Back`, `Options` from `Start`). This is corrected in the
same change as this dependency update, together with independent regression
tests (`Start_maps_to_SteamDeck_Menu`, `Back_maps_to_SteamDeck_Options`) that
set only one of Start/Back at a time, so the pair cannot silently swap again
undetected. `LStickForce`/`RStickForce` remain neutral (`0`); a separate
VIIPER protocol-level discrepancy involving those fields is intentionally
out of scope here and remains a separate investigation.

No other Steam Deck field mapping (A/B/X/Y, D-pad, L1/R1, L2Digital/
R2Digital, L3/R3, analog trigger scaling, stick axes, M1->R4/M2->L4) changed;
each was checked against the new upstream exhaustive tests and found already
correct.

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
