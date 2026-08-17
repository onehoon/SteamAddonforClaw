# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     a6bb749199aa797da690c611d2f18edc5e770c1e
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
DLL SHA-256:              efbaca96f2b0405d5c1a947bbe4771597b241a68aac190e0118b1393ddead771
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `bce7b4e20da6c80a706be9952dfbfd5eb6515b57` ->
`a6bb749199aa797da690c611d2f18edc5e770c1e`. The target is exactly one
canonical main commit, `Harden canonical libVIIPER API consistency verification
(#42)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed Addon header. Its SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`,
and the vendored header has the same Git blob identity on the dependency PR
head and its Addon base. There are no added or removed exports, signature
changes, enum changes, struct layout/packing changes, callback ABI changes, or
Steam Deck state-layout changes. `SteamDeckDeviceState` remains 76 bytes with
`LPadForce`, `RPadForce`, `LStickForce`, and `RStickForce` at offsets
68/70/72/74. The current Addon managed P/Invoke surface, 12-entry
`RequiredExports`, callback rooting, and ABI tests require no adaptation.

PR #42 is canonical build/tooling and documentation hardening rather than a
production lifecycle/runtime change. It adds `lib/viiper/exportverify`, which
parses non-test canonical Go source and derives the declared `//export` names.
The verifier requires an export directive to match its Go function name,
rejects duplicate or ambiguous directives, and then checks that every canonical
source export is present by exact name in the generated header and, on Windows,
the DEF and parsed DLL export table. Prefix-only matches and non-export/import
text do not satisfy the check. GNU and LLVM PE export-table formats are handled
within their bounded export-table sections.

This export-projection check is intentionally one-way: it proves every
canonical source export is projected to the artifacts, but it does not claim
that every symbol visible in those artifacts is a canonical source export.
`FORK_ARCHITECTURE.md` explicitly scopes the structural check this way and
retains the separate semantic ABI assertions as authoritative for signatures,
enums, layouts, and lifecycle contracts. The canonical source count reported by
PR #42 is 50, with zero source exports missing from the generated header,
Windows DEF, or Windows DLL export table.

The existing header postbuild step is also made fail-closed and deterministic:
source-directory read failures, Go parse failures, generated-header read
failures, and header write failures are returned as build errors, while a
successful repeated run is tested to produce identical output. Canonical CI now
runs the tooling tests and export-projection verification after the real shared
library build before packaging.

The accompanying `docs/libviiper/overview.md` corrections remove stale generic
claims that every libVIIPER API returns `bool`, that `CloseUSBServer` always
unconditionally frees everything, or that a NULL callback discards all logging.
The corrected overview points classified `*Ex` semantics and close-failed retry
behavior back to the fork API, and accurately describes the callback as an
optional observer independent from the Windows owned file sink. These are
documentation corrections to the already-reviewed canonical contracts, not new
runtime semantics.

No `lib/viiper` production mutation, attachment, removal, callback, transport,
or device implementation changed in this delta. Caller-owned bus lifetime,
per-server `lifecycleMu` serialization, exact attachment-token ownership,
classified retryable/unknown results, `close-failed`, post-unlock diagnostics,
and callback/transport drain rules therefore remain unchanged. The Addon
continues to use the existing bool attach/detach compatibility surface;
classified attachment/query adoption remains SD3 lifecycle/recovery work.

No Addon Steam Deck mapper, publisher, native binding, callback lifetime,
routing, PnP, HidHide, recovery, lifecycle policy, or planned Xbox360 route
change is required for this dependency update. No hardware-validation claim is
expanded. MSI Claw EX basic non-gyro Steam Deck input remains the established
claim; SD3 lifecycle/recovery evidence, rumble/haptics, gyro/IMU, and Game
Bar/Xbox360 validation remain separate work.
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
