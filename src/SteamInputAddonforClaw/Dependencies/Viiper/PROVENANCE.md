# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     89ce1426883ea5001b5788000df272db7532f0e1
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
DLL SHA-256:              a676f27299cf4c0f645f4fe4048ee8adafec40b18997b15c0135534995b44456
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

The reviewed generated-header delta from VIIPER
`cb29c1727996f50debfc7836c1febd6c70008811` to
`89ce1426883ea5001b5788000df272db7532f0e1` is: none. The canonical artifact's
`libVIIPER.h` remains byte-identical to the previously reviewed header (same
SHA-256,
`e6c1bddb3ef3bab27ec8744da44051ec9ea7e5a57f92dbc869a87f6d456aa9bc`).
The DLL SHA-256 changed to
`a676f27299cf4c0f645f4fe4048ee8adafec40b18997b15c0135534995b44456`
because VIIPER PR #27 adds internal embedded diagnostic-log ownership; it does
not change the public C ABI or exported typed-device contract.

VIIPER PR #27 ("Make embedded libVIIPER own its diagnostic log") changes the
canonical runtime's diagnostic persistence only. On Windows, `NewUSBServer`
now independently attempts to write a single daily `libVIIPER.log` beside the
loaded DLL. File persistence is bounded/non-blocking and asynchronous;
`CloseUSBServer` performs only a best-effort bounded flush after releasing its
lifecycle lock. The optional `VIIPERLogCallback` remains synchronous with the
same signature and semantics as an observer/mirror. Per-input/per-frame state
and publisher paths are not logged through this mechanism, and logging/file
failures do not alter attach/detach/removal classifications or lifecycle
results.

Confirmed by this review:

- no exported function signature was added, removed, or changed;
- no `SteamDeckDeviceState`, `SteamDeckDeviceRemoveResult`, callback typedef,
  enum, struct layout, field offset, or packing change occurred;
- `NewUSBServer`, `SetSteamDeckOutputCallback`, and the full typed Steam Deck
  ABI remain source/ABI compatible with the current managed bindings;
- the classified `AttachUSBDeviceEx` / `DetachUSBDeviceEx` and read-only
  `GetUSBDeviceAttachmentState` contracts are unchanged;
- `CanonicalViiperNativeApi.cs`, `CanonicalViiperNativeTypes.cs`,
  `CanonicalViiperNativeAbiTests.cs`, and `RequiredExports` require no managed
  adaptation;
- the Addon may continue passing `CanonicalViiperDiagnosticLog.Callback` to
  `NewUSBServer`; the callback remains rooted by the existing managed lifetime
  logic, while VIIPER's native file sink is an additional independent sink;
- no Addon routing, Steam Deck mapping, publisher, attachment-ownership,
  teardown, or recovery policy change is required by this dependency update;
- no hardware-validation claim is expanded. MSI Claw EX basic non-gyro input
  remains the established claim, and SD3 lifecycle/recovery validation remains
  next.
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
