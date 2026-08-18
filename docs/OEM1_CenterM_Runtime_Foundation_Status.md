# OEM1 / Center M Native Runtime Foundation — Status

This tracks the OEM1 (Center M button) remapping feature across PRs. See the
research handoff (`MSI_CenterM_OEM1_Remapping_Research_Design_Handoff_2026-08-14`)
for hardware test results, static reverse-engineering findings, and the
production design this foundation implements.

## PR1 — Native runtime primitives — MERGED (#234)

Adds the dormant primitives a future coordinator needs, under
`src/SteamInputAddonforClaw/CenterM/`:

- `WmiMsiEventSource` — observational MSI_Event WMI watcher (Event41/Event88),
  never suppresses or consumes either event.
- `CenterMBackendProbe` — read-only Launcher/Server/ControlMode presence probe.
- `CenterMAutoRunReader` — read-only AutoRun registry probe. No write path exists yet.
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
(AutoRun/Launcher/Server), helper arm/disarm ordering (stage → own → post-start
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
gesture/action/UI behavior is touched. The routing guard owns its own
dedicated `CenterMHelperOwnership` instance; a future production OEM1
composition must not create a second same-name helper alongside it.

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

## PR3+ (not started) — settings / UI / production composition

Controller settings "Center M Button" action selector, status presentation,
named-pipe settings transport/persistence, localization, production
composition wiring OEM1 suppression on real user machines.
