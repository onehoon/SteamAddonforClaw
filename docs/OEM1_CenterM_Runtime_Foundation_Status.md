# OEM1 / Center M Native Runtime Foundation — Status

> **Superseded internals (Full1902 Cleanup A + B).** Cleanup A deleted
> `MsiClawRoutingComposition`, `AddonRoutingRuntime`, the `RoutingPipeline*` graph,
> and `CenterMMainUiRoutingGuardStage`. Cleanup B deleted the entire Center M
> dummy / MainUI suppression subsystem: the `SteamInputAddonforClaw.CenterMHelper`
> project and its `CenterMHelperSource` publish payload; `CenterMHelperOwnership` /
> `CenterMHelperStaging` / `CenterMHelperInvariant` / `CenterMOrphanedHelperRegistry`;
> the `CenterMMainUiRoutingGuard` / `CenterMMainUiRoutingRetirement` /
> `CenterMMainUiRoutingTerminator` / `CenterMMainUiMutexOwnership` /
> `CenterMMainUiWindowController` / `MainUiLifecycleObserver` / `SafeMainUiTerminator` /
> `TrackedCenterMMainUi` graph; and `CenterMOem1LifecycleCoordinator` /
> `CenterMOem1LifecycleRuntime` / `CenterMBackendProbe`. OEM1 / WING button action
> ownership now lives entirely in `MsiClawFrontButtonRuntime` (composed in
> `AddonProcessHost` on a Center M Disabled boot); native Win+G suppression is
> `WinGSuppressionGuard`, bound to the Addon controller-authority lifetime.
> `CenterMStartupHelper` and `CenterMStartup/**` (the elevated startup-root
> authority boundary) are unchanged. The PR notes below are historical; their
> references to the helper / MainUI / routing wiring no longer describe the tree.

This tracks the OEM1 (Center M button) remapping feature across PRs. See the
research handoff (`MSI_CenterM_OEM1_Remapping_Research_Design_Handoff_2026-08-14`)
for hardware test results, static reverse-engineering findings, and the
production design this foundation implements.

## Current main status

- Production OEM1 Event41 path is composed.
- Center M suppression lifecycle is production-composed.
- Configurable `Oem1MappingSettings` are persisted.
- Controller / Center M Button settings UI is implemented.
- Routing-active SteamQuickAccess selection uses actual `SteamOutputActive`.
- Hardware validation remains pending.

The PR sections below are chronological and may describe earlier states where
these pieces were intentionally dormant.

## PR1 — Native runtime primitives — MERGED (#234)

Adds the dormant primitives a future coordinator needs, under
`src/SteamInputAddonforClaw/CenterM/`:

- `WmiMsiEventSource` — observational MSI_Event WMI watcher (Event41/Event88),
  never suppresses or consumes either event.
- `CenterMBackendProbe` — read-only Launcher/Server/ControlMode presence probe.
- `CenterMAutoRunReader` — read-only AutoRun registry probe. Removed in the
  "OEM1 remove AutoRun" PR below; see that section.
- `MainUiLifecycleObserver` / `MainUiWindowRecognition` — result-state (not
  cause-based) real MainUI lifecycle classification: Absent,
  StartingOrHiddenNeverVisible, Visible, HiddenAfterVisible, Exited, Uncertain.
  `SeenVisible` is a hard invariant: a process can only become a
  HiddenAfterVisible/kill candidate after actually being observed visible.
- `TrackedCenterMMainUi` / `SafeMainUiTerminator` — exact-identity, retained-handle
  safe termination primitive for a real MainUI process. Never invoked by
  process name; every precondition failure fails open (no termination).
- `CenterMHelperOwnership` / `Win32HelperProcessNativeApi` — CREATE_SUSPENDED +
  private Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) helper ownership,
  in the mandatory order, with fail-closed cleanup at every step.
- `CenterMHelperInvariant` — the Armed-state authoritative invariant (only
  same-name process is the owned helper).
- `CenterMHelperStaging` + `SteamInputAddonforClaw.CenterMHelper` project — a
  dormant, windowless helper executable, staged at runtime to
  `%LOCALAPPDATA%\SteamInputAddonForClaw\Runtime\CenterM\MSI Center M.exe`.

**None of this is wired into production startup.** `App`/`AddonRuntimeHost`/
`AddonRuntimeComposition` are unmodified. The helper is never started
automatically, Event41 never drives a custom action, and no real MainUI is
ever terminated automatically. Running the app today produces identical
OEM1/OEM2 behavior to before this PR.

## PR2-A — Coordinator / lifecycle (this PR)

Adds `CenterMOem1LifecycleCoordinator` under
`src/SteamInputAddonforClaw/CenterM/`, composing the PR1 primitives into the
`Disabled → NeedsSetup → Reconciling → Armed → NativeMainUiActive →
HiddenDebounce → FaultedNative` lifecycle: prerequisite reconciliation
(originally AutoRun/Launcher/Server; AutoRun was removed as an arming
prerequisite in the "OEM1 remove AutoRun" PR below), helper arm/disarm ordering (stage → own → post-start
`CenterMHelperInvariant` check, never publishing Armed before that check
passes), exact-handle helper liveness monitoring (a new
`CenterMHelperOwnership.PollLiveness()`/`HelperLivenessState`, backed by a new
`IHelperProcessNativeApi.PollLiveness` zero-timeout wait), real MainUI
detection/identity adoption via `TrackedCenterMMainUi`, immediate yield to a
newly-appeared real MainUI, a cancelable ~1-second visible→hidden debounce,
and final termination exclusively through the existing
`SafeMainUiTerminator`. All mutating lifecycle work is serialized behind a
single async gate so enable/disable, polling, debounce completion, suspend,
resume, and shutdown can never race each other's ownership/tracking
decisions.

**This PR remains dormant in production.** The coordinator is not
constructed or started by `AddonRuntimeCompositionFactory`,
`AddonRuntimeHost`, `AddonProcessHost`, `RuntimeProcessApplication`, or any UI
startup path. `SetDesiredEnabledAsync` defaults to `false` and production
never calls it with `true`. Starting the Addon normally still does not stage
or start the helper and does not terminate any real Center M process —
verified by a headless `--background` startup smoke test. Reaching the
`Armed` state in this PR means suppression-lifecycle readiness only: there is
still no OEM1 custom action, no gesture recognition, no Steam Quick Access
integration, and no settings/UI. No new hardware validation is claimed by
this PR; all coverage is deterministic automated tests with fake
dependencies.

## PR2-B1 — Gesture core (this PR)

Adds an isolated OEM1 semantic gesture recognizer for Single/Double results,
configurable double-click timing, deterministic delay testing, reset/cancellation,
and stale-timeout protection. It receives already-classified semantic presses
only; it is not wired into production runtime, Event41, lifecycle ownership, or
any action policy. Production OEM1 behavior therefore remains unchanged.

## PR2-B2 — Event / gesture policy bridge (this PR)

Adds an isolated bridge that forwards only OEM1/Event41 semantic events to the
PR2-B1 gesture core when an explicitly injected custom-authority boolean is
active. Authority loss synchronously resets pending gestures; OEM2/Event88 and
other semantic events are ignored without disturbing OEM1 state. Recognized
gestures become minimal `Oem1GesturePolicyRequest` values, with downstream
subscriber exceptions contained at the bridge boundary. The bridge is not
composed into production and does not query `CenterMOem1LifecycleCoordinator`.

No actual action is executed, Quick Access remains deferred to PR3+, and no
hardware validation is claimed. Production OEM1/OEM2 behavior remains
unchanged.

## PR3-A — Steam Deck Quick Access output overlay (this PR)

Adds a managed, output-only Quick Access synthetic-button primitive
(`SteamDeckSystemButtonOverlay`), merged into the existing Steam Deck publish
path, with a `CanonicalSteamDeckOutputStage.RequestQuickAccessPulse()`
forwarding seam for a future PR. It remains unwired to OEM1, gesture policy,
UI, IPC, or settings.

## PR3-B — Selectable OEM1 Quick Access action policy (this PR)

Adds a minimal semantic action-policy seam connecting the PR2-B2 gesture
bridge output to the PR3-A Quick Access primitive: `Oem1Action` (`None`,
`SteamQuickAccess`), `Oem1ActionBindings` (Single/Double gesture-to-action
selection, default Single -> `SteamQuickAccess`, Double -> `None`), and
`Oem1ActionDispatcher`, which resolves the bound action for a gesture and,
for `SteamQuickAccess`, calls `RequestQuickAccessPulse()` only when
`RoutingRuntimeStatusSnapshot.SteamOutputActive` is true at dispatch time. If
the bound action is `SteamQuickAccess` but Steam output is not active, this is
a clean no-op — no fallback launches Center M, starts Steam, or activates
routing. Routing state alone never redefines OEM1's action; the binding is
always consulted first, so a different binding (e.g. Double ->
`SteamQuickAccess`) works without changing the dispatcher.

The dispatcher is not composed into production startup. `Oem1EventGestureBridge`,
`CenterMOem1LifecycleCoordinator`, and `WmiMsiEventSource` remain dormant, and
there is still no settings, persistence, IPC, or UI to change the bindings.
No hardware validation is claimed by this PR.

## Routing-time MainUI launch guard, Phase 1 (this PR)

Adds `CenterMMainUiRoutingGuard`, a narrow routing-only primitive that prevents
a NEW real MSI Center M MainUI from becoming operational while Steam routing
is active, so the owned PID1902/DirectInput physical session stays
authoritative. RE (`MSI_COMPLETE_RESEARCH_RESULT.md`) confirmed the real
`MSI Center M.exe`'s own duplicate-instance guard is the mutex
`Local\MSI Center M.exe` (`MSI_Center_M.App.IsAlreadyRunning`), checked before
`MainWindow`/controller-mode initialization -- this PR has the Addon
transiently own that exact same resource for the routing lifetime
(`CenterMMainUiMutexOwnership`), alongside the existing same-name staged
helper (`CenterMHelperOwnership`/`CenterMHelperStaging`, reused as-is, not
duplicated).

Composed as a new `CenterMMainUiRoutingGuardStage` in `MsiClawRoutingComposition`,
scheduled first in `RoutingPipelineStageOrder.Forward` (before native-mode/
PID1902 mutation) and last in `RoutingPipelineStageOrder.Rollback` (after
native/physical restoration) -- routing cannot begin mutating the physical
controller until the guard reports `Armed`, and the guard is only released
after routing has already torn down. Every partial arm failure (helper failed
to start, mutex unavailable, invariant check failed, a foreign same-name
process appeared mid-arm) unwinds only what that attempt itself acquired and
never commits Armed.

This is a separate, narrower composition than `CenterMOem1LifecycleCoordinator`
(still fully dormant in production, unrelated to this PR) -- no OEM1
gesture/action/UI behavior is touched. **Helper ownership convergence (see the
PR below) has since made the routing guard's `CenterMHelperOwnership` a
composition-owned shared instance rather than a private one; a future
production OEM1 composition must still never create a second same-name
helper alongside it.**

## Routing-time MainUI existing-instance retirement, Phase 2 (this PR)

Phase 1 unconditionally refused to arm (`RealMainUiPresent`) whenever a real
MainUI process already existed -- hardware testing confirmed this also blocked
Test Mode (same canonical pipeline, `EnterOverride`) for the common case of a
tray-resident real Center M. Phase 2 replaces that unconditional refusal with
an explicit retirement step, `CenterMMainUiRoutingRetirement`, run inside the
same serialized `CenterMMainUiRoutingGuard.ArmAsync` transaction, before the
Phase-1 helper/mutex arm sequence:

- **tray-resident (hidden) MainUI**: verify the physical controller is
  already stable `XInput` (`ICenterMNativeModeProbe`, wrapping the SAME
  `MsiClawNativeStateManager` instance the real NativeMode stage uses --
  never a second one), then terminate the exact process.
- **visible MainUI**: request a normal `WM_SYSCOMMAND`/`SC_MINIMIZE` (never
  `ShowWindow(SW_HIDE)`, so Center M runs its own real minimize lifecycle --
  `GamePadListener` stop -> `GoToXInputMode` -> `ExitProfileConfig` -- research:
  `MSI_Center_M_MainUI_ControlMode_RE_Report.md`), wait (bounded) for the
  window to actually hide, then the same XInput verification and termination
  as the tray path.

Termination itself goes through a new `CenterMMainUiRoutingTerminator` --
deliberately a SEPARATE type from `SafeMainUiTerminator`, whose existing OEM1
`SeenVisible` contract is untouched. `CenterMMainUiRoutingTerminator` reuses
the same identity/evidence shape and `SafeMainUiTerminator.PathMatchesExpectedPackage`,
but its authority comes from routing being about to take exclusive controller
ownership plus freshly re-verified identity/window/native-state evidence, not
from the OEM1 hidden-after-visible lifecycle -- a tray-only MainUI that was
never observed visible by this process (`StartingOrHiddenNeverVisible` in
`MainUiLifecycleObserver` terms) is a valid routing-retirement candidate even
though it still correctly refuses `SafeMainUiTerminator`'s own OEM1 API.
Only the exact retained `MSI Center M.exe` process handle is ever terminated;
`MSI_Center_M_Server.exe`, `MSI_Center_M_Launcher.exe`, and
`MSI_Center_M_Server_ControlMode.exe` are never touched.

Every uncertain/ambiguous outcome (enumeration uncertain, multiple same-name
candidates, identity/package-path mismatch, minimize command failure/timeout,
native mode never confirmed XInput, termination failure/timeout, another real
MainUI present after retirement) fails routing entry before any native-mode/
PID1902 mutation and before any helper/mutex commitment -- retirement never
guesses, and Test Mode and normal Steam routing share this exact behavior
(same canonical `CenterMGuard` stage, same `StockCenterM` plan).

**Hardware validation is required and has not been performed.** See
`docs/VIIPER_MIGRATION_TODO.md` SD3 for the required hardware test checklist.

## Center M helper ownership convergence (this PR)

Converges the two independent same-name `CenterMHelperOwnership` consumers
(`CenterMOem1LifecycleCoordinator`, still dormant, and the production-active
`CenterMMainUiRoutingGuard` above) onto a single shared authority, so a future
OEM1 production composition can eventually run both without ever creating two
competing same-name helpers. This PR is ownership/refactoring only -- it does
**not** enable OEM1 production behavior.

- `MsiClawRoutingComposition` now constructs exactly one `CenterMHelperOwnership`
  and passes that same instance into its default `CenterMMainUiRoutingGuard`
  via constructor injection (an externally supplied `CenterMHelperOwnership` is
  also accepted, for a future OEM1 production composition seam). This
  composition is the sole final disposer of that shared instance.
- `CenterMMainUiRoutingGuard.ArmAsync()` now makes a borrow-or-start decision:
  if the shared ownership is already `IsOperationallyOwned` (e.g. a future
  long-lived OEM1 owner already armed it), the guard borrows it -- it does not
  call `Start()` again -- and validates using the existing owned PID.
  Otherwise it stages and starts the helper itself, exactly as before. A
  retained-but-not-operational ownership (`IsOwned` true,
  `IsOperationallyOwned` false -- e.g. an unresolved `PartialCleanupUnconfirmed`
  residue) fails closed with a new `HelperOwnershipUnresolved` result: no
  replacement helper is started, and nothing is discarded or silently
  replaced.
- A per-arm `_helperStartedByCurrentArm` flag records whether that specific
  arm attempt started the helper. `DisarmAsync()` and terminal
  `DisposeAsync()` only stop/register the helper when this guard itself
  started it -- a borrowed helper always survives routing disarm and
  disposal, left operational for its external owner.
- Fresh post-acquisition same-name process snapshot and
  `CenterMHelperInvariant` verification remain mandatory in both the borrow
  and start paths before `Armed` is ever published.

**Production OEM1 composition is still not enabled by this PR.**
`CenterMOem1LifecycleCoordinator` is still not constructed or started by any
production composition root, and it does not yet receive the shared
`CenterMHelperOwnership` this PR introduces at the MSI Claw routing
composition level -- wiring the coordinator to that shared authority is left
to a later PR. WMI/gesture/action-dispatch/Quick Access production wiring
remains completely dormant, unchanged from PR3-B. No new hardware validation
is claimed: this is a software-only ownership/refactoring change, verified
with deterministic fake-backed unit tests only. Ordinary Steam routing
behavior when no long-lived OEM1 owner exists is unchanged --
`CenterMMainUiRoutingGuardStage` still arms first (before native-mode/
PID1902 mutation) and disarms last (after native/physical restoration), and
it still starts and stops its own helper exactly as before when nothing else
has already armed the shared ownership.

## PR2 (lifecycle production composition) — this PR

Production-composes the already-implemented `CenterMOem1LifecycleCoordinator`
into the real MSI Claw runtime object lifetime -- lifecycle/composition wiring
only, not feature activation.

- `MsiClawRoutingComposition` now constructs `CenterMOem1LifecycleCoordinator`
  with the SAME shared `CenterMHelperOwnership` instance it already hands to
  `CenterMMainUiRoutingGuard` (the ownership-convergence PR above) -- there is
  still exactly one production same-name helper authority, never two
  independent owners. An explicit MSI Claw environment-eligibility predicate
  is supplied (the coordinator's own default stays fail-open/false for any
  caller that omits one).
- A new, small production driver, `CenterM/CenterMOem1LifecycleRuntime`, is
  the coordinator's one lifetime owner: a single low-rate periodic loop invokes
  the coordinator's `PollHelperLivenessAsync()` seam on every tick, but a
  cleanly `Disabled`/`DesiredEnabled == false` coordinator deliberately no-ops
  so it cannot apply OEM1 cleanup policy to a routing-guard-owned shared
  helper. Once OEM1 custom authority is enabled, exact-handle liveness
  reconciliation becomes active. `PollTickAsync()` is called only while
  `CenterMMainUiRoutingGuard.IsArmed` is false, so the driver's normal
  MainUI-yield polling never fights the guard's transient
  `Local\MSI Center M.exe` launch-protection authority during Steam routing.
  The driver duplicates no coordinator state; the coordinator remains the sole
  authoritative state machine.
- Suspend/resume: `CenterMOem1LifecycleRuntime` forwards
  `IPowerSuspendParticipant.QuiesceForSuspendAsync` directly to the
  coordinator's existing suspend participant implementation, and implements a
  new narrow `Power.IRuntimeResumeParticipant` capability
  (`ReconcileAfterResumeAsync`) that `AddonRuntimeHost` now calls once per
  resume, independent of whether Steam/VIIPER routing's own resume
  reconciliation succeeded. `AddonRuntimeHost`/`IHandheldRoutingComposition`
  learn nothing MSI/CenterM-specific from this -- both capabilities are
  exposed only as optional, generic, nullable seams a composition may or may
  not supply.
- Shutdown ordering: `MsiClawRoutingComposition.DisposeAsync()` stops the
  periodic driver (cancel + join, so a timer callback can never still enter
  coordinator methods afterward) and disposes the coordinator before the
  routing guard's own terminal cleanup and the composition's final
  `CenterMHelperOwnership` disposal -- unchanged ownership-boundary rules
  from the convergence PR (a caller-injected shared instance is still never
  disposed here, and a borrowed helper is still never stopped by the guard).

**Production activation remains explicitly OFF in this PR.** Normal MSI
runtime composition constructs and starts the driver, but never calls
`SetDesiredEnabledAsync(true)` -- `DesiredEnabled` and `SuppressionReady` stay
false on ordinary startup, no helper is staged/started merely because the
Addon launched, and native OEM1/Center M behavior is unchanged for real
users. `WmiMsiEventSource`, `Oem1EventGestureBridge`, `Oem1ActionDispatcher`,
and `CanonicalSteamDeckOutputStage.RequestQuickAccessPulse()` all remain
completely dormant; Event41/Event88 production wiring is untouched. Tests
exercise the coordinator's explicit test-only activation seam
(`SetDesiredEnabledAsync`) to prove the full production composition graph
without ever enabling suppression in a real Addon process. No hardware
validation is claimed by this PR -- all coverage is deterministic automated
tests with fake dependencies.

## PR3 — Production E2E POC: independent normal mapping + routing-active QAM + real suppression — this PR

**Development-only E2E POC. Not release-ready.** This is the first PR that
turns on real OEM1 production suppression and a real replacement action on
real MSI Claw hardware, to validate the complete path end-to-end:
`Physical OEM1 -> MSI Event41 -> WmiMsiEventSource -> Oem1EventGestureBridge
-> Oem1ActionDispatcher -> real replacement action -> real Center M
suppression`.

**The central architectural rule: normal OEM1 mapping and Steam routing are
independent features.** Normal OEM1 mapping does not require routing to be
enabled, available, initialized, eligible, or active. Only while canonical
Steam Deck routing is *actually active right now* does a routing-side mapping
get selected instead -- and the moment routing stops being active, the very
next OEM1 press goes back to normal mapping, with no explicit re-arm step.

- POC normal mapping (used whenever canonical Steam Deck routing is not
  actually active right now, including routing disabled/unavailable/idle/no
  game routed/Steam not running): `Single -> SteamBigPicture` (new
  `Oem1Action` member; `Process.Start("steam://open/bigpicture")`),
  `Double -> None`.
- POC routing-active mapping (used only while
  `RoutingRuntimeStatusSnapshot.SteamOutputActive == true`, captured fresh at
  every dispatch, never cached, never gated on `Available`):
  `Single -> SteamQuickAccess` (via the existing, unmodified
  `CanonicalSteamDeckOutputStage.RequestQuickAccessPulse()`), `Double -> None`.
- `Oem1ActionBindings` now exposes two independent default instances
  (`NormalDefault`, `RoutingActiveDefault`) instead of one combined `Default`
  -- two separate binding/resolve domains, never one action that internally
  decides based on routing state. `Oem1ActionDispatcher.Dispatch` selects the
  domain first (`SteamOutputActive` fact only, never `Available`), then
  resolves the gesture within that domain, and returns `false` only when a
  bound action was actually invoked and its execution threw (an actual
  replacement-backend failure, distinct from routing simply being
  unavailable/inactive, which is normal and never a failure).
- **Real OEM1 suppression is now enabled in production**, independent of any
  routing setting: `MsiClawRoutingComposition` wires the previously-dormant
  Event41 chain (`WmiMsiEventSource -> Oem1GestureRecognizer ->
  Oem1EventGestureBridge -> Oem1ActionDispatcher`) via a new
  `IHandheldRoutingComposition.ConfigureOem1ActionPath(captureRoutingStatus,
  requestQuickAccessPulse)` seam that `AddonRoutingRuntime.Create` calls with
  the only two routing/output-layer facts OEM1 needs -- fresh routing status
  and the QAM pulse primitive. If WMI observation starts successfully, the
  composition requests `CenterMOem1LifecycleCoordinator.SetDesiredEnabledAsync(true)`;
  if WMI startup fails, OEM1 stays feature-local-disabled (native Center M
  remains available, the rest of the Addon runtime is unaffected). The
  existing coordinator/`CenterMOem1LifecycleRuntime` remain the single
  lifecycle authority -- no second state machine, no second poller. A new,
  narrow, optional `CenterMOem1LifecycleRuntime` callback
  (`onReconciled`, invoked after every poll tick and after resume
  reconciliation; `onSuspending`, invoked at the start of suspend) lets the
  composition mirror the coordinator's own `SuppressionReady` onto the
  gesture bridge's custom authority -- bridge authority is never driven by
  the routing setting, only by the coordinator's own state.
- Routing setting OFF (core acceptance test): OEM1 custom feature stays
  active, Center M suppression stays Armed, and pressing OEM1 still reaches
  normal mapping (Big Picture) -- the absence of an active routing session
  never disables the bridge, suppression, or the helper.
- Suspend/resume/shutdown ordering from PR2 is preserved exactly; suspend now
  additionally revokes custom gesture admission immediately (via
  `onSuspending`), and `MsiClawRoutingComposition.DisposeAsync()` now revokes
  bridge authority and disposes the event/gesture/action path before the
  existing coordinator/driver teardown.
- OEM2/Event88 is untouched -- it never reaches the bridge (rejected before
  gesture recognition, unchanged from PR2-B2).

**Still not implemented in this PR (unchanged non-goals):** settings UI,
persistence, the final configurable mapping framework, keyboard/gamepad/EXE
mapping, per-gesture UI, AutoRun writer, OEM2 mapping, an E0 39 keyboard
hook, and any new BPM/Steam process polling.

**No real-hardware validation has been performed in this session.** All
coverage (`Oem1ActionDispatcherTests`,
`MsiClawRoutingCompositionOem1ActionPathTests`, plus the unmodified existing
`CenterMOem1LifecycleCoordinator`/`Oem1EventGestureBridge`/
`Oem1GestureRecognizer` suites) is deterministic automated tests with fake
WMI/process/launch dependencies -- no real WMI, no real process launches, no
real MSI Claw hardware. Real-hardware validation (SD5: does OEM1 actually
suppress native Center M and launch Big Picture / pulse QAM on a physical MSI
Claw) remains pending and must happen before this POC is considered anything
beyond a development E2E validation vehicle.

## PR2 (this branch) — remove OEM1 AutoRun prerequisite

Real-hardware testing showed OEM1 remapping getting stuck at
`AutoRun=Unknown → NeedsSetup → SuppressionReady=False`, because the Windows
registry "AutoRun" value the coordinator required as an arming prerequisite is
unreliable/unknown in practice. The existing same-name-process +
exact-helper-ownership suppression mechanism routing already relies on works
correctly independent of that registry value, so AutoRun is removed as an
OEM1 arming authority entirely rather than replaced with another state
machine:

- `CenterMAutoRunReader` (the reader class and `CenterMAutoRunState` enum) is
  deleted.
- `CenterMOem1LifecycleCoordinator` no longer takes an `autoRunReader`
  constructor parameter, no longer gates `ReconcileCore`/
  `RefreshSuppressionPrerequisites`/the steady-state Armed poll on AutoRun, and
  the `CenterMOem1LifecycleSnapshot.AutoRunState` field is removed. Arming now
  depends only on: supported MSI Claw + RemappingEnabled + Launcher/Server
  readiness + safe same-name process topology + exact owned helper.
- The elevated `AutoRun=1 → 0` HKLM registry mutation in
  `ElevatedPrerequisiteSetup` is deleted, along with the
  `CenterMAutoRunOwnedByAddon`/`CenterMAutoRunMutationPending`/
  `OriginalAutoRun`/`AppliedAutoRun` settings fields (no migration needed --
  this repo has no released version using them).
- `FirstTimeSetupPolicy` no longer reports `Indeterminate`/`Required` purely
  because of AutoRun state; HidHide/usbip-win2 first-time setup is unaffected.

**Unchanged by this PR:** the Center M launch UX (a direct real Center M
launch still yields to native MainUI in Normal/no-routing; routing/PID1902
protection via `CenterMMainUiRoutingGuard` is untouched), the shared
`CenterMHelperOwnership` composition, VIIPER, rumble, HidHide/usbip-win2
setup, and the OEM1 mapping/UI/dispatch model.

**No real-hardware validation has been performed for this change.** It
addresses a real-hardware-observed stuck state, but the fix itself has only
been exercised by the deterministic automated test suite (fakes for process
snapshot/helper/window/identity/terminate primitives) -- validating on a
physical MSI Claw remains a required follow-up.

## Mapping framework and settings UI — IMPLEMENTED

The configurable mapping framework now provides:

- a global remapping switch;
- `NormalSingle`, `NormalDouble`, `RoutingSingle`, and `RoutingDouble` slots;
- persistence through the existing settings store;
- capability validation shared by settings UI and dispatch;
- Controller settings and the Center M Button detail page;
- one existing Center M lifecycle owner, with no duplicate suppression owner.

Hardware validation remains pending.
