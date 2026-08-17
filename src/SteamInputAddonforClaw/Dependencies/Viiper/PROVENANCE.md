# libVIIPER.dll provenance

## Embedded artifact

The Addon embeds the Release `libVIIPER.dll`, generated header, and matching
licenses built from:

```text
Repository: onehoon/VIIPER
Commit:     bce7b4e20da6c80a706be9952dfbfd5eb6515b57
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
DLL SHA-256:              bba7bd7466842642bcdbe408047ad7496cf8015d31651d44540b650966439a8d
```

CI verifies the committed hashes match this record and the vendored files.

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Reviewed VIIPER `b55b435a63c85430d2a00949014d5c0892c8af67` ->
`bce7b4e20da6c80a706be9952dfbfd5eb6515b57`. The target is exactly one
canonical main commit, `Make attachment backend logging lifecycle-lock safe
(#41)`.

The generated canonical `libVIIPER.h` is byte-identical to the previously
reviewed header. Its SHA-256 remains
`202444479f20cd599d0ad48890fc644dd3085f9c6ade1e00fa404e689d88f718`,
and the dependency PR does not modify the vendored header. There are no added
or removed exports, signature changes, enum changes, struct layout/packing
changes, callback ABI changes, or Steam Deck state-layout changes.
`SteamDeckDeviceState` remains 76 bytes with `LPadForce`, `RPadForce`,
`LStickForce`, and `RStickForce` at offsets 68/70/72/74. The current Addon
managed P/Invoke surface, 12-entry `RequiredExports`, callback rooting, and ABI
tests require no adaptation.

PR #41 extends the lock-safe diagnostic boundary introduced by the preceding
lifecycle logging hardening to the canonical tracked attachment backend.
Backend `slog` records produced while the native attach/detach operation is
serialized under the owning server's `lifecycleMu` are captured into an
internal deferred batch instead of invoking the real logger/callback while the
lock is held. After the authoritative lifecycle mutation is committed and the
lock is released, the captured records are synchronously replayed to the real
logger before the public lifecycle API returns. The callback therefore remains
synchronous from the caller's perspective while no embedding callback executes
under `lifecycleMu`.

The deferred logger is internal implementation state only. The server-scoped
capture logger is installed and consumed within the same serialized lifecycle
boundary, cleared before unlock, and then replayed after unlock. Explicit
Attach/Detach, typed Create with auto-attach, typed Remove, `RemoveUSBBus`, and
`CloseUSBServer` all propagate the captured backend records through their
existing canonical mutation/teardown paths. This does not create a second
attach/detach path and does not weaken per-server lifecycle serialization.

Attachment ownership and result semantics remain unchanged. Successful attach
still commits the verified backend and positive import port; detach still uses
that exact stored token. Retryable failure preserves known ownership and an
active server, while an unsafe unknown outcome remains sticky, transitions the
owning server to `close-failed`, retains diagnostic evidence, and does not
perform a destructive retry. Typed remove, bus remove, and close retain their
existing caller-owned bus, partial-close, transport-drain, and fail-closed
semantics.

Diagnostic ordering is explicit: captured backend records replay after unlock
and, where teardown requires it, after the existing transport drain, but before
the canonical attachment-timing or teardown summary for that operation. The
canonical operation `totalUs` value is snapshotted before synchronous replay so
callback/log-handler latency is not reclassified as native lifecycle time.
Focused regressions cover explicit attach/detach success and classified
failure, exact-token retention, sticky unknown ownership, typed create/remove,
multi-device bus removal ordering, server close, lock-free replay, structured
record timestamps/levels/attributes/groups, destination `Enabled` filtering,
and backend-record-before-summary ordering.

This remains low-volume lifecycle diagnostic work only; no per-input/per-frame
logging is introduced. The guarantee is deliberately scoped to the canonical
`lib/viiper` tracked attachment path and does not make a new claim about the
legacy `clib`/TCP/server logging stack.

The Addon's registered VIIPER callback remains narrowly filtered to the
existing `VIIPER.DPad` diagnostic prefix, so generic backend attachment logs
are not forwarded into the Addon product log. No Addon Steam Deck mapper,
publisher, native binding, callback lifetime, routing, PnP, HidHide, recovery,
lifecycle policy, or planned Xbox360 route change is required for this
dependency update. The Addon continues to use the existing bool attach/detach
compatibility surface; classified attachment/query adoption remains SD3
lifecycle/recovery work.

No hardware-validation claim is expanded. MSI Claw EX basic non-gyro Steam
Deck input remains the established claim; SD3 lifecycle/recovery evidence,
rumble/haptics, gyro/IMU, and Game Bar/Xbox360 validation remain separate work.
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
