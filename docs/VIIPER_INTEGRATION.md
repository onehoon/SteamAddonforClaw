# VIIPER Integration Contract

This document defines the current Addon integration with the canonical typed
VIIPER API. The active Steam virtual-output target is Steam Deck `28DE:1205`.

## Current status

| Item | Current contract |
| --- | --- |
| Canonical embedded API | `lib/viiper` typed ABI |
| Embedded VIIPER revision | `9ed7eeec6e92b3f54cd4ac6785da22db8725742d` |
| Active Steam output | Steam Deck `28DE:1205` only |
| Addon integration | Session, mapper, publisher, identity resolver, safety stage implemented |
| Hardware status | EX basic non-gyro input validated; lifecycle evidence remains pending |
| Rumble / haptics | Separate feature track |
| Gyro / IMU | Separate feature track |

The DLL, generated header, managed P/Invoke definitions, ABI tests, hashes,
and this contract must all refer to the same VIIPER revision.

`Dependencies/Viiper/viiper.lock.json` is the machine-readable identity record
for the embedded dependency: pinned commit, build entrypoint, and DLL/header
SHA-256 hashes. `scripts/verify-viiper.ps1` (run in CI and release) fails
closed if the lock file, `PROVENANCE.md`, the vendored DLL/header, and the
pin recorded above ever disagree. Normal Addon builds consume the vendored,
reviewed dependency as-is — they do not fetch VIIPER from any network source,
mutable or otherwise. Upgrading the pinned revision remains a separate,
reviewed change.

`scripts/update-viiper.ps1 -Commit <40-char-sha>` can fetch and independently
verify the canonical `onehoon/VIIPER` Windows build artifact for an exact,
already-built commit: it discovers a `push`/`main`/successful run at that
exact commit, downloads its canonical `libVIIPER-windows-amd64-Snapshot`
artifact, and validates the artifact's own manifest (`viiper-artifact.json`)
and recomputed DLL/header hashes. It only fetches and verifies into a
disposable staging directory (`artifacts/viiper-update/<commit>/`, gitignored)
-- it never modifies the vendored `Dependencies/Viiper` files, `viiper.lock.json`,
`PROVENANCE.md`, or any managed code, and it never runs as part of normal
CI/build/release, which stay network-independent of VIIPER. A mutable
"latest main" or dev-snapshot release identity is never treated as dependency
authority; only a successful `main` push run for the exact requested commit is
eligible. Actually adopting a verified artifact -- updating the vendored
files, the lock, provenance, and (if the ABI changed) the managed P/Invoke
bindings together -- is a separate, later, reviewed operation.

Phase 2B2 performed the first such adoption: `ec64282c69e5587466b950332d7983fd53a7d778`
-> `0b3627317d2008065d8ec231f94bf31af7527bbd`, using `scripts/update-viiper.ps1`
to fetch and verify the canonical artifact before anything was changed. That
revision adds exactly one native export, `SetSteamDeckOutputCallback`
(and its `SteamDeckOutputCallback` delegate typedef); `SteamDeckDeviceState`
and `SteamDeckDeviceRemoveResult` are unchanged. The managed
`ICanonicalViiperNativeApi` surface, `RequiredExports`, and callback-lifetime
rooting were updated in the same change to keep the native and managed ABI
aligned. No production caller registers the Steam Deck output callback yet;
Addon rumble/haptics handling remains a separate feature track.

### Automated dependency update PRs

`scripts/adopt-viiper.ps1` mechanically applies an already-verified
`update-viiper.ps1` staging payload to the vendored dependency identity: the
DLL/header, `viiper.lock.json`, `PROVENANCE.md`'s mechanical fields (its
marker-delimited "ABI review" section is reset to an evergreen human-review
placeholder, never synthesized), the documented pins, `THIRD_PARTY_NOTICES.md`'s
VIIPER source identity, `ViiperRuntimeInspector.ExpectedPayloadSha256`, and
`scripts/verify-publish-assets.ps1`'s expected hash. `scripts/verify-viiper.ps1`
is authoritative for all of these mechanical pins, not just the lock/provenance
pair.

The `.github/workflows/viiper-dependency-update.yml` workflow wires this into
an end-to-end pipeline: a `repository_dispatch` from `onehoon/VIIPER` (sent
only after a real `push`/`main`/successful canonical build completes) or a
manual `workflow_dispatch` with an exact commit triggers independent
re-verification via `update-viiper.ps1`, mechanical adoption via
`adopt-viiper.ps1`, and a Draft PR into `main`. The dispatch payload's commit
is only an input, never trusted as artifact authority -- the same
push/main/success/exact-artifact/manifest/hash trust chain applies regardless
of trigger. The workflow also fails closed on a downgrade (a target commit
behind the current pin is treated as a clean no-op, never adopted) and never
duplicates or force-pushes over an existing automation PR for the same
commit.

**Managed ABI compatibility is never inferred or automatically adapted.**
The automation never edits `ICanonicalViiperNativeApi`, native delegate
definitions, `RequiredExports`, struct/enum layouts, callback lifetime logic,
or any runtime/session/mapper behavior. Every automated PR is opened as a
Draft and requires human review of the generated header diff and managed
interop before merging; if the ABI changed, the required managed changes are
added to the same PR, not merged separately. Normal Addon CI and release stay
network-independent of VIIPER -- only the dedicated dependency-update
workflow fetches the external canonical artifact.

## 1. Upstream authority

The architectural and API sources of truth are:

- `onehoon/VIIPER/FORK_ARCHITECTURE.md`
- `onehoon/VIIPER/docs/libviiper/fork-api.md`

The Addon uses `lib/viiper`. The legacy `clib` surface remains a compatibility
surface in VIIPER, but new Addon integration must use the typed canonical ABI.

## 2. Integration boundary

The Addon owns application policy and orchestration:

- MSI Claw device discovery and exact physical-input selection;
- normalized `ControllerState` production;
- Steam-session and routing policy;
- target-specific Steam Deck mapping;
- PnP identity and ownership verification;
- HidHide, recovery, suspend/resume, and teardown safety.

VIIPER owns the native virtual-device, USB/IP, report, callback, and typed
handle lifecycle described by its API contract. The Addon must not infer
native ownership from a successful function return when the native result is
unknown.

## 3. Process and lifetime model

1. Load the pinned `libVIIPER.dll` for process lifetime.
2. Create the typed Steam Deck session and its caller-owned USB resources.
3. Resolve and stabilize the exact `28DE:1205` PnP identity.
4. Verify Addon ownership and HidHide state before routing.
5. Publish normalized input through the Steam Deck mapper.
6. Stop publishing before logical removal.
7. Detach and remove only resources whose ownership is known.
8. Restore the physical MSI Claw stock state and persist recovery evidence.

Public teardown waits outside the canonical native lifecycle lock. Unknown
attachment or removal outcomes fail closed and preserve recovery evidence for a
later explicit reconciliation.

## 4. Steam Deck typed ABI

The pinned VIIPER revision provides:

```text
SteamDeckDeviceHandle
SteamDeckDeviceState
SteamDeckDeviceRemoveResult
CreateSteamDeckDevice
SetSteamDeckDeviceState
SetSteamDeckOutputCallback
RemoveSteamDeckDevice
RemoveSteamDeckDeviceEx
```

The Addon uses the generated header and the matching managed definitions from
the same build. The generic output callback remains available in the native
API; Addon rumble and haptics adoption is a separate feature track.

## 5. Steam Deck state mapping

The mapper preserves the normalized physical state and maps it to native Deck
fields. Analog trigger travel and digital full-pull trigger state remain
independent. Sticks, L3/R3, rear controls, Steam, and Quick Access use their
native semantic fields where the current feature scope supports them.

Trackpad and motion fields remain neutral until their separate feature tracks
are implemented and hardware-validated.

## 6. PnP identity and ownership

The resolver accepts only the exact Steam Deck vendor/product identity:

```text
VID = 0x28DE
PID = 0x1205
```

Instance identity comparisons are case-insensitive and ownership is tracked by
the exact resolved identity. No VID-only, friendly-name-only, or broad Valve
device match is sufficient. Missing, ambiguous, or unstable identity fails
closed.

## 7. Addon safety shell

The active routing pipeline preserves these boundaries:

- native MSI mode mutation is coordinated with the physical input stage;
- physical isolation is scoped to the verified topology;
- HidHide entries are preserved unless ownership is proven;
- routing epochs gate final state commits;
- startup and resume use live current-world state;
- publisher faults request runtime fail-closed reconciliation;
- teardown is retry-safe and never silently selects another output.

## 8. Steam session authority

The effective Steam session source combines the direct Steam watcher, Big
Picture state, developer settings, and session policy. Only the routing
coordinator may enter or leave the active pipeline. The Deck output stage does
not independently infer application policy.

## 9. Hardware validation boundary

The EX hardware result currently validates basic non-gyro controller input.
It does not claim lifecycle, recovery, suspend/resume, teardown, rumble,
haptics, gyro, or IMU support. Those are separate evidence requirements.

## 10. usbip-win2 compatibility

The pinned integration follows the VIIPER fork's supported usbip-win2 version
policy. Do not silently upgrade the package or infer runtime readiness from
installation evidence alone.

## 11. Update rule

Any ABI, struct layout, callback, ownership, attachment, transport, or
lifecycle change requires reviewing the VIIPER architecture/API documents,
the Addon rules, provenance, generated header, managed interop, tests, and
hardware-validation claims together.
