# VIIPER Integration Contract

This document defines the current Addon integration with the canonical typed
VIIPER API. The sole Steam routing target is Steam Deck `28DE:1205`; typed
Xbox360 is a temporary Game Bar presentation only.

## Current status

| Item | Current contract |
| --- | --- |
| Canonical embedded API | `lib/viiper` typed ABI |
| Embedded VIIPER revision | `a6bb749199aa797da690c611d2f18edc5e770c1e` |
| Primary Steam routing target | Steam Deck `28DE:1205` |
| Temporary Game Bar presentation | Persistent typed Xbox360 logical device |
| Addon integration | Session, mapper, publisher, identity resolver, safety stage implemented |
| Hardware status | EX basic non-gyro input validated; lifecycle evidence remains pending |
| Rumble | Production callback/authority/STOP wiring implemented; hardware validation pending |
| Rumble / haptic feedback | Production two-motor translation/wiring implemented; hardware validation pending |
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
aligned. Production registers the Steam Deck output callback only after the
optional physical rumble capability succeeds at an explicit STOP preflight
through the real endpoint/handle/write path. If preflight is unavailable,
Steam Deck input routing continues without feedback and no callback is
registered. An armed session copies the synchronous normalized payload,
decodes ordinary 0xEB rumble, and gates physical writes through the shared
feedback authority. Teardown of an armed session revokes and drains feedback,
sends an explicit physical STOP, clears the callback, and only then performs
classified Steam Deck attachment detach; final logical removal belongs only to
runtime teardown. Steam Deck `0xEA` Haptic and `0x8F` Haptic Pulse are translated
through the existing two-motor physical feedback path. Audio/jingle and unknown
output commands remain unsupported; hardware validation remains pending.

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

The pinned VIIPER ABI includes classified `AttachUSBDeviceEx` /
`DetachUSBDeviceEx` and the read-only `GetUSBDeviceAttachmentState` query, and
the canonical typed Xbox360 surface (`CreateXbox360Device`,
`SetXbox360DeviceState`, `RemoveXbox360Device`, `RemoveXbox360DeviceEx`). The
managed ABI binding for all of these now exists in
`ICanonicalViiperNativeApi`/`CanonicalViiperNativeApi`. The compatibility
bool `AttachUSBDevice` / `DetachUSBDevice` compatibility surface remains
available, but production Deck routing now uses
the classified attachment/query surface. The persistent runtime creates one
detached-ready Xbox360 logical handle. While an eligible outer Steam route is
active, Game Bar foreground presentation may pause the existing Steam Deck
publisher, keep Deck attached-neutral, classified-attach the persistent
Xbox360 handle, and start the Xbox360 publisher. Leaving Game Bar retires
Xbox360 and resumes the same Deck publisher/session. The attachment state
query is VIIPER ownership evidence only, not Windows PnP, HID, XInput, or
Steam readiness; Game Bar/X360 hardware readiness validation remains pending.
The Xbox360 typed API in this PR covers
buttons/D-pad/sticks/triggers only -- no rumble callback is bound.

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
2. Initialize one `CanonicalViiperRuntime`: one server, one caller-owned bus,
   one detached-ready Steam Deck handle, and one detached-ready Xbox360 handle.
3. On Steam route entry, record recovery intent, classified-attach the same
   Deck handle, then resolve/stabilize exact `28DE:1205` PnP ownership.
4. Verify Addon ownership and HidHide state, then publish neutral and live input.
5. On route exit, stop publisher/feedback, clear callback, neutralize, perform
   classified Deck detach, verify exact PnP absence, and complete recovery.
6. Keep both logical handles and the bus/server alive while the Runtime lives.
7. Only after canonical routing shutdown succeeds, final Runtime teardown
   removes Deck/Xbox360, removes the bus, and closes the server.
8. Restore the physical MSI Claw stock state through the existing rollback and
   recovery path.

Public teardown waits outside the canonical native lifecycle lock. Unknown
attachment or removal outcomes fail closed and preserve recovery evidence for a
later explicit reconciliation.

**PR2a foundation / PR2b production composition:** the process/runtime-
lifetime persistent owner described in step 1-2 above is implemented as
`CanonicalViiperRuntime` -- one server, one caller-owned bus, one persistent
Steam Deck logical device, and one persistent Xbox360 logical device,
created once and left detached (`autoAttachLocalhost: false`), plus
classified final teardown of all four resources. It is fully implemented
and unit-tested. PR2b now composes it once in `AddonRoutingRuntime`.
`CanonicalSteamDeckSession` borrows the persistent Deck handle and uses
classified `AttachUSBDeviceEx`/`DetachUSBDeviceEx` per route. Final teardown
is performed only by the runtime owner after routing shutdown succeeds; no
second VIIPER server/bus owner exists in production.

## PR2b production composition

`AddonRoutingRuntime` owns one `CanonicalViiperRuntime` for its lifetime. It
owns one server, one caller-owned bus, and persistent detached-ready Deck and
Xbox360 logical handles. A Steam route creates only a short-lived session
wrapper that borrows the Deck handle, verifies `Detached`, then uses
classified `AttachUSBDeviceEx`. Route exit stops publisher/feedback, writes
neutral state, and uses classified `DetachUSBDeviceEx`; it does not remove
the logical device, bus, or server. PnP disappearance and recovery evidence
remain authoritative. Final runtime shutdown alone invokes the existing
staged logical-device, bus, and server teardown. Xbox360 remains detached and
unpublished while Game Bar presentation is inactive. During an active outer
Steam route, Game Bar foreground stops the Deck publisher, keeps Deck
attached-neutral, classified-attaches the persistent Xbox360 handle, and
starts the Xbox360 publisher. Leaving Game Bar stops/neutralizes/detaches
Xbox360 and resumes the same Deck publisher; Xbox360 is not an independent
routing target or fallback.

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
API; Steam Deck `0xEA` Haptic and `0x8F` Haptic Pulse are translated through
the production two-motor physical feedback path. Audio/jingle and unknown
output commands remain unsupported; hardware validation remains pending.

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
- unexpected loss of the owned physical-input session while routing is
  active -> stop routing / fail closed;
- teardown is retry-safe and never silently selects another output.

Stale previous-process Steam Deck virtual-output journal evidence is retired
only after bounded, cancellation-aware current-world PnP inspection proves
stable absence of every recorded owned identity and every new exact
`28DE:1205` identity that was not present before the mutation. Unresolved
identity evidence is not treated as proof that no virtual device was created.
Startup never replays previous-process VIIPER handles or creates a replacement
session to detach stale devices; ambiguous or unavailable PnP evidence
preserves the journal and keeps recovery unsafe.

## 8. Steam session authority

The effective Steam session source combines the direct Steam watcher, Big
Picture state, developer settings, and session policy. Only the routing
coordinator may enter or leave the active pipeline. The Deck output stage does
not independently infer application policy.

## 9. Hardware validation boundary

The EX hardware result currently validates basic non-gyro controller input.
It does not claim lifecycle, recovery, suspend/resume, teardown, rumble,
haptics, Game Bar/XInput readiness, gyro, or IMU support. Those remain separate
evidence requirements; haptic translation is implemented in software, but its
hardware validation remains pending.

## 10. usbip-win2 compatibility

The pinned integration follows the VIIPER fork's supported usbip-win2 version
policy. Do not silently upgrade the package or infer runtime readiness from
installation evidence alone.

## 11. Update rule

Any ABI, struct layout, callback, ownership, attachment, transport, or
lifecycle change requires reviewing the VIIPER architecture/API documents,
the Addon rules, provenance, generated header, managed interop, tests, and
hardware-validation claims together.
