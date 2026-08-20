# Steam Deck Runtime Roadmap

## Current state

The Addon has one active Steam virtual-output architecture: the canonical
Steam Deck typed VIIPER path with identity `28DE:1205`.

The embedded VIIPER revision is:

```text
onehoon/VIIPER@a6bb749199aa797da690c611d2f18edc5e770c1e
```

The Addon-side session, mapper, publisher, PnP identity resolver, and safety
stage are implemented. MSI Claw EX hardware has validated the basic non-gyro
controller input path. That result does not by itself validate every lifecycle,
recovery, native teardown, or failure-path condition.

Phase 2B1 tooling status: `scripts/update-viiper.ps1` can fetch and
independently verify the canonical Windows libVIIPER artifact for an exact,
already-built `onehoon/VIIPER` commit into a disposable staging directory.

Phase 2B2 performed the first canonical dependency adoption from
`ec64282c69e5587466b950332d7983fd53a7d778` to
`0b3627317d2008065d8ec231f94bf31af7527bbd` and aligned the managed Steam
Deck callback ABI. That adopted revision added exactly one export,
`SetSteamDeckOutputCallback`, and the managed `ICanonicalViiperNativeApi`
surface, `RequiredExports`, and callback-lifetime rooting were updated in the
same change. This is dependency/ABI adoption only -- it does not implement
rumble/haptics, does not change Steam routing/Game Bar/X360/lifecycle policy,
and does not claim any additional hardware validation. Basic non-gyro
hardware validation from before this adoption remains the only established
Steam Deck input hardware claim; SD3 lifecycle/recovery validation below
remains unstarted.

Phase 3 dependency automation: COMPLETE. The real end-to-end pipeline has
been demonstrated: VIIPER main push -> canonical Dev Snapshot Build -> Notify
Addon Dependency sender -> `repository_dispatch` -> Addon receiver -> exact
commit eligibility check -> exact push/main/success artifact rediscovery ->
manifest plus DLL/header hash verification -> mechanical dependency adoption
-> automated Draft dependency PR -> human ABI/runtime review -> manual merge
only. There is no automatic merge; every generated dependency PR still
requires human ABI/runtime review. This dependency-automation completion does
not advance SD3 hardware validation. SD3 lifecycle/recovery hardware
validation remains next.

## Active roadmap

### SD3 — lifecycle and recovery hardware validation

Status: **NEXT**

Addon-side stale startup virtual-output journal retirement now has a
current-world PnP evidence gate in code. This is safety hardening only and
does not advance SD3 hardware validation.

Unexpected termination of the owned PID_1902 physical-input DirectInput
session while routing is active now requests canonical routing fail-close
(`RoutingPipelineRuntimeCoordinator.FailClosedAsync()`, with the routing
safety fault latched first via the existing `IRoutingSafetySession`). This
protects against device re-enumeration, firmware/mode-manager mutation
(including MSI Center M's own DInput/MSI/XInput switching), and unexpected
HID/driver failure while Steam routing is active -- not just Center M
specifically. This is software safety hardening only and does not advance SD3
hardware validation; real hardware validation of this path remains required.

A routing-time MSI Center M MainUI launch guard (Phase 1) now arms/disarms
around routing entry/exit -- `Local\MSI Center M.exe` mutex ownership plus the
existing staged same-name helper, gating native-mode/PID1902 mutation on the
guard reaching `Armed`. This is software only and does not advance SD3
hardware validation. Required first hardware validation (precondition: real
MSI Center M MainUI fully exited):

1. Start Addon, enter Steam BPM/routing; confirm physical PID1902 active,
   virtual Steam Deck active, DirectInput controls working, and the Addon log
   shows the routing guard Armed.
2. Launch MSI Center M manually from the Windows Start Menu; observe for
   several seconds. PASS requires: no operational Center M MainWindow
   appears (or one briefly starts and exits immediately); the MSI MainUI log
   contains no `GotoMSIMode` for that launch; PID1902 remains present;
   PID1901 does not appear; the Addon DirectInput feeder stays alive;
   D-pad/buttons keep working; Steam Deck remains the active virtual
   controller; no routing teardown/re-entry occurs.
3. Exit BPM/routing; confirm physical native/XInput restoration completes and
   the routing guard disarms.
4. Launch Center M from the Start Menu again; PASS requires it opens normally
   and no stale helper/mutex keeps it blocked.

If the hardware test fails, classify the failure (real MainUI reached
`GotoMSIMode`; Center M stays blocked after routing exits; PID1902
disappeared despite no `GotoMSIMode`) before adding any process-kill logic --
do not immediately introduce automatic termination.

Phase 2 adds retirement of an already-running real MainUI (tray-resident or
visible) before the Phase-1 guard's helper/mutex arm, so routing entry is no
longer unconditionally refused merely because a real MainUI already exists.
This is software only and does not advance SD3 hardware validation. Required
hardware validation:

**Test A -- tray-resident Center M.** Precondition: Center M running in the
tray (MainUI window not visible), physical controller stock XInput/PID1901.
Start Addon, confirm real `MSI Center M.exe` alive in tray, enter Test Mode.
PASS: exact real MainUI detected; logs show the tray/hidden path; no minimize
request; XInput verified; exact `MSI Center M.exe` terminates;
Server/Launcher/ControlMode remain alive; Addon helper starts;
`Local\MSI Center M.exe` guard arms; PID1901 -> PID1902 occurs; Test Mode
becomes active; controls work normally. Exit Test Mode -- PASS: native XInput
restores, guard disarms, helper terminates, Center M launches normally again.

**Test B -- visible Center M.** Precondition: Center M MainUI visibly open.
Enter Test Mode or Steam routing. PASS: visible Center M minimizes normally
(`WM_SYSCOMMAND`/`SC_MINIMIZE`); no hard kill while still visible; XInput
confirmed before termination; only `MSI Center M.exe` is retired; MSI backend
processes remain; routing succeeds.

**Test C -- routing-active direct Start Menu launch.** While routing from
Test A or B is active, launch MSI Center M from the Windows Start Menu. PASS:
real MainUI does not become operational, no `GotoMSIMode`, PID1902 remains,
routing/input stays active -- confirms Phase 2 did not regress Phase 1.

**Test D -- routing exit.** Exit routing/Test Mode, confirm native XInput
restoration, launch Center M normally. PASS: real Center M launches and works
normally; no stale helper; no stale `Local\MSI Center M.exe`; no persistent
package/system policy change.

Complete real MSI Claw EX validation for:

- native-mode entry and restoration;
- exact `28DE:1205` PnP identity and ownership;
- publisher startup, heartbeat, and clean stop;
- Steam-session routing transitions;
- suspend/resume reconciliation;
- HidHide and recovery cleanup;
- Deck creation, native failure, and teardown fail-closed behavior.

Basic non-gyro controller input is already validated. Do not use this item to
claim rumble, haptics, gyro, or IMU support.

### SD4 — production readiness review

Status: **BLOCKED ON SD3 EVIDENCE**

Review the complete hardware evidence, release packaging, diagnostics, and
recovery behavior before calling the active Deck path production-ready.

Required properties:

- the active output remains exactly `28DE:1205`;
- ambiguous PnP or ownership state fails closed;
- native, publisher, HidHide, and teardown failures do not continue routing;
- shutdown and resume leave the physical device in a safe stock state;
- diagnostics identify the selected target and failure operation clearly.

### SD5 — OEM1 and Quick Access

Status: **PLANNED**

Map the validated OEM1 control to the Steam Deck Quick Access field after the
basic lifecycle gate is complete. A managed, output-only Quick Access
synthetic-button primitive (`SteamDeckSystemButtonOverlay`, merged into the
existing Steam Deck publish path in `CanonicalSteamDeckInputPublisher`) exists,
with `CanonicalSteamDeckOutputStage.RequestQuickAccessPulse()` as its forwarding
seam. A selectable OEM1 action-policy layer (`Oem1Action`, `Oem1ActionBindings`,
`Oem1ActionDispatcher`) now resolves an OEM1 gesture to an action and, for
`SteamQuickAccess`, calls that seam only when
`RoutingRuntimeStatusSnapshot.SteamOutputActive` is true — routing state alone
does not redefine OEM1 as Quick Access. Default bindings are Single ->
`SteamQuickAccess`, Double -> `None`. This dispatcher is not yet composed into
production startup: `Oem1EventGestureBridge`, `CenterMOem1LifecycleCoordinator`,
and `WmiMsiEventSource` remain dormant, and there is still no settings,
persistence, or UI to change the bindings. This does not advance SD5
completion, and no hardware validation is claimed.

A helper ownership convergence PR (`CenterM/CenterMMainUiRoutingGuard`,
`Devices/MSI/Claw/MsiClawRoutingComposition`) has since made the routing
guard's `CenterMHelperOwnership` a composition-owned shared instance that a
future OEM1 production composition can reuse without creating a second
same-name helper. This is ownership/refactoring only -- it does not compose
`CenterMOem1LifecycleCoordinator` into production and does not advance SD5
completion.

A lifecycle/composition PR (`CenterM/CenterMOem1LifecycleRuntime`,
`Devices/MSI/Claw/MsiClawRoutingComposition`) has since production-composed
`CenterMOem1LifecycleCoordinator` into the real MSI Claw runtime object
lifetime, sharing the SAME `CenterMHelperOwnership` as the routing guard, with
a single low-rate `PeriodicTimer`-style driver for the coordinator's
documented poll contract, suspend/resume wiring
(`IPowerSuspendParticipant`/a narrow new `IRuntimeResumeParticipant` seam on
`AddonRuntimeHost`), and orderly shutdown ordering. Production activation
remains explicitly OFF: normal Addon startup never calls
`SetDesiredEnabledAsync(true)`, so `DesiredEnabled`/`SuppressionReady` stay
false and no helper is ever staged/started merely because the Addon launched.
`WmiMsiEventSource`, `Oem1EventGestureBridge`, `Oem1ActionDispatcher`, and
`RequestQuickAccessPulse()` remain completely dormant -- this PR is
lifecycle/composition wiring only. This does not advance SD5 completion, and
no hardware validation is claimed.

A development-only E2E POC PR has since wired the full production path and
enabled real suppression: `WmiMsiEventSource -> Oem1EventGestureBridge ->
Oem1ActionDispatcher` are now production-composed (via a new
`IHandheldRoutingComposition.ConfigureOem1ActionPath` seam called from
`AddonRoutingRuntime.Create`), and `MsiClawRoutingComposition` requests
`CenterMOem1LifecycleCoordinator.SetDesiredEnabledAsync(true)` once WMI
observation actually starts. Normal OEM1 mapping (POC: `Single ->
SteamBigPicture`) is fully independent of Steam routing -- it works whether
routing is enabled, disabled, unavailable, or merely idle. Routing being
disabled does not disable OEM1 mapping or suppression. Only while canonical
Steam Deck routing is *actually active right now*
(`RoutingRuntimeStatusSnapshot.SteamOutputActive`, captured fresh, never
`Available`) does the routing-side POC action (`Single -> SteamQuickAccess`
via the existing `RequestQuickAccessPulse()`) take precedence; routing
becoming inactive returns the very next OEM1 press to normal mapping (Big
Picture) with no explicit re-arm step. `Oem1ActionBindings` now exposes two
independent binding domains (`NormalDefault`, `RoutingActiveDefault`) instead
of one combined default. Settings, persistence, and a final configurable
mapping framework are still not implemented -- both POC mappings remain
hard-coded. **This PR does not mark SD5 complete: no real-hardware
validation has been performed in this session** (no MSI Claw hardware was
available); all coverage is deterministic automated tests with fake
WMI/process/launch dependencies. Hardware validation (does OEM1 actually
suppress native Center M and launch Big Picture / pulse QAM on a physical MSI
Claw) remains pending before SD5 can be considered complete.

A mapping-framework PR has since replaced the hard-coded POC bindings with a
small configurable model plus settings UI. `Oem1ActionBindings` is gone; the
one source of truth is now `Oem1MappingSettings` in
`SteamInputAddonforClaw.Contracts/Oem1` -- the global "Center M Button
Remapping" switch plus four slots (`NormalSingle`, `NormalDouble`,
`RoutingSingle`, `RoutingDouble`), persisted through the existing
`AppSettings`/`SettingsStore` file. Actions are `None`, `SteamBigPicture`,
`SteamQuickAccess`, `KeyboardHotkey`, `LaunchApplication`, restricted per slot
by the single `Oem1ActionCapabilities` table that both the settings UI's
ComboBoxes and the dispatcher's pre-execution validation read. Domain
selection is unchanged and still reads only
`RoutingRuntimeStatusSnapshot.SteamOutputActive`; a routing transition still
never arms/disarms suppression. The remapping switch drives the EXISTING
`CenterMOem1LifecycleCoordinator` (no second lifecycle owner) and never erases
the saved bindings. UI is a Controller-page `SettingsCard` plus a Center M
Button detail page. **Still does not mark SD5 complete: no real-hardware
validation has been performed** -- all coverage remains deterministic
automated tests with fake WMI/process/launch/key-injection seams.

### SD6 — gyro and accelerometer

Status: **SEPARATE FEATURE TRACK**

Add Windows sensor acquisition, capability checks, calibration, and Steam Deck
motion mapping only after the dedicated hardware and lifecycle design is
approved.

### SD7 — Game Bar and typed Xbox360 route

Status: **PLANNED**

Define the Game Bar transition and typed Xbox360 composition without weakening
the active Deck lifecycle or recovery invariants.

Preparatory step: managed ABI foundation only (buttons/D-pad/sticks/triggers;
no rumble callback bound). `ICanonicalViiperNativeApi`/`CanonicalViiperNativeApi`
now expose the canonical typed Xbox360 surface (`CreateXbox360Device`,
`SetXbox360DeviceState`, `RemoveXbox360Device`, `RemoveXbox360DeviceEx`) and
the classified attachment surface (`AttachUSBDeviceEx`, `DetachUSBDeviceEx`,
`GetUSBDeviceAttachmentState`). Xbox360 production behavior remains
foundation-only: PR2b creates one detached-ready logical handle but does not
attach, publish, or use it for Game Bar behavior. OEM1 mapping/domain policy
is unchanged. Status remains PLANNED.

PR2a (foundation only): `CanonicalViiperRuntime`
(`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs`)
implements the intended process/runtime-lifetime persistent VIIPER owner --
one server, one caller-owned bus, one persistent Steam Deck logical device,
and one persistent Xbox360 logical device, all created once and left
detached (`autoAttachLocalhost: false`) -- plus classified final teardown
(`RemoveSteamDeckDeviceEx` / `RemoveXbox360DeviceEx` / `RemoveUSBBus` /
`CloseUSBServer`, each staged with exact
Success/RetryableFailure/UnsafeOutcomeUnknown/Invalid handling and
resumable retry). It is fully implemented and covered by deterministic
tests (`CanonicalViiperRuntimeTests`). PR2b composes it once in
`AddonRoutingRuntime`; `CanonicalSteamDeckSession` now borrows its persistent
Deck handle and production no longer creates/removes a server/bus/device per
route. Ordinary route exit leaves Deck and Xbox360 logical handles alive and
detached; only final runtime teardown removes the logical devices, bus, and
server. Initialization failure remains fail-closed and never falls back to
per-route creation. SD7 remains PLANNED; Xbox360 has no attach, publisher, or
Game Bar behavior here.

Preparatory runtime primitive step (PR2c): `CanonicalViiperRuntime` now
exposes classified Xbox360 attachment-state query, attach, neutral-before-
detach, and state-write primitives over the already persistent
detached-ready Xbox360 handle. No production caller exists yet; Xbox360
remains detached/unpublished and Game Bar switching remains PLANNED.

## Separate feature tracks

Rumble v1 production wiring is implemented, but hardware validation remains
pending. Haptics, gyro, and accelerometer behavior are not implied by the basic
non-gyro input validation. Each requires its own protocol, mapping, lifecycle,
and hardware evidence.

## Non-negotiable rules

- Steam Deck `28DE:1205` is the sole active Steam output target.
- Keep the exact VIIPER source, DLL, generated header, managed ABI, hashes, and
  provenance aligned.
- Use `lib/viiper` and the typed ABI for new integration work.
- Preserve caller-owned bus lifetime and explicit attachment ownership.
- Unknown attachment, removal, PnP, HidHide, and recovery outcomes fail closed.
- Do not claim hardware validation that was not performed.
- Do not add output selection or silent fallback to another implementation.

## Required references

Before any VIIPER implementation or review, read:

1. `docs/VIIPER_INTEGRATION.md`
2. `docs/VIIPER_IMPLEMENTATION_RULES.md`
3. `onehoon/VIIPER/FORK_ARCHITECTURE.md`
4. `onehoon/VIIPER/docs/libviiper/fork-api.md`

If the native ABI, ownership, callback, or lifecycle contract changes, update
the relevant documents and provenance in the same change.
