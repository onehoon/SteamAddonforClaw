# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     cb29c1727996f50debfc7836c1febd6c70008811
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
DLL SHA-256:              baa271a8859fc1864ea03898cec2ee3f708c85ae924e27ff7edd990e1ea57d33
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed generated-header delta from VIIPER
`a8a00efe7a5dce0c8d95de16795797a7daa7d82a` to
`cb29c1727996f50debfc7836c1febd6c70008811` is: none. The generated
`libVIIPER.h` is byte-identical between the two revisions (same SHA-256,
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`, and
directly diffed with zero output). The DLL SHA-256 changed
(`baa271a8859fc1864ea03898cec2ee3f708c85ae924e27ff7edd990e1ea57d33`) because
the change is internal instrumentation, not because any exported surface
changed.

Upstream VIIPER PR #26 ("Add USB attachment timing diagnostics") adds
optional, internal latency measurement around the existing classified
`attachDeviceLockedResult` / `detachDeviceLockedResult` call paths (canonical
Go layer) and the Windows native IOCTL / command-fallback backends
(`internal/server/api/autoattach_windows.go`,
`internal/server/api/autoattach_contract.go`). It touches only
`.go` sources and test files; no `.c`/`.h`/cgo export declaration changed, as
independently confirmed by both the generated-header diff above and the
upstream commit's own file list (`internal/server/api/autoattach_contract.go`,
`internal/server/api/autoattach_contract_test.go`,
`internal/server/api/autoattach_windows.go`,
`internal/server/api/autoattach_windows_test.go`,
`lib/viiper/attachment_timing_test.go`, `lib/viiper/viiper.go`). The upstream
commit message itself independently states the header and DLL export list
were confirmed byte-identical to pre-change `main`.

Confirmed by this review:

- no exported function signatures added, removed, or changed;
- no `SteamDeckDeviceState`, `SteamDeckDeviceRemoveResult`, or any other
  struct/enum layout change;
- `SetSteamDeckOutputCallback` and the rest of the typed Steam Deck ABI
  (`SteamDeckDeviceHandle`, `CreateSteamDeckDevice`,
  `SetSteamDeckDeviceState`, `RemoveSteamDeckDevice`,
  `RemoveSteamDeckDeviceEx`) are unchanged;
- the classified `AttachUSBDeviceEx` / `DetachUSBDeviceEx` and read-only
  `GetUSBDeviceAttachmentState` exports (and their result enums) reviewed as
  part of the prior `a8a00ef` adoption are unchanged;
- no managed P/Invoke adaptation is required: `CanonicalViiperNativeApi.cs`,
  `CanonicalViiperNativeTypes.cs`, and `CanonicalViiperNativeAbiTests.cs`
  needed no changes and were not touched by this adoption;
- no Addon routing, session, mapper, or lifecycle behavior changes -- this is
  a dependency identity/ABI review only;
- no hardware-validation claim is expanded by this adoption; MSI Claw EX
  basic non-gyro controller input remains the only established Steam Deck
  input hardware claim, and SD3 lifecycle/recovery validation remains next.
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
