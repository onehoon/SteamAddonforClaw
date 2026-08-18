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

## PR2-B (not started) — Gesture / policy

Single/double-click OEM1 gesture recognition, configurable timing,
`Oem1Action` selection/dispatch. Steam Deck Quick Access mapping (VIIPER
`VIIPER_MIGRATION_TODO.md` item "SD5") remains PLANNED and is not touched or
validated by PR2-A.

## PR3+ (not started) — Steam Quick Access / settings / UI

Controller settings "Center M Button" action selector, status presentation,
named-pipe settings transport/persistence, localization.
