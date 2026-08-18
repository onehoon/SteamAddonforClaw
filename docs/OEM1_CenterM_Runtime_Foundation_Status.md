# OEM1 / Center M Native Runtime Foundation — Status

This tracks the OEM1 (Center M button) remapping feature across PRs. See the
research handoff (`MSI_CenterM_OEM1_Remapping_Research_Design_Handoff_2026-08-14`)
for hardware test results, static reverse-engineering findings, and the
production design this foundation implements.

## PR1 — Native runtime primitives (this PR)

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

## PR2 (not started) — Coordinator / state machine

Will connect these primitives into the `Disabled → NeedsSetup → Native →
Arming → Armed → MainUiActive → PendingMainUiExit → FaultedNative` state
machine, using cause-agnostic facts only (`RealMainUiVisible`,
`RealMainUiHiddenAfterVisible`, `RealMainUiExited`, ...) — never inferring
whether an OEM1 press, the window's X button, or a tray Exit produced a given
observed state.

## PR3+ (not started) — User-facing action / UI

Controller settings "Center M Button" action selector, `Oem1Action`
persistence and execution, status presentation, localization.
