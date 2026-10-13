# Work Order — Retire Four Developer Diagnostics, Preserve Production Controller Features

> **Date:** 2026-10-13  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Implementation:** local Codex; one focused PR (purely removed/related UI + IPC + Runtime + tests)  
> **Code baseline reviewed:** `main`, commit `8e9bcf7fc502ee018de54f9974ffae0e3b1ce8fe` (PR #778 merged); re-read current `main` before implementation  
> **Frontend protocol at review:** 79; bump once from the **actual** current value, to 80 if still 79  
> **Validation division:** local Codex runs build/code tests; the user performs physical MSI Claw validation *after merge*. No on-device/manual tests are required for the PR, CI, review or merge.

## 1. Exact requested retirement

Delete these four **developer-only tools**, including their visible UI, diagnostic-only Runtime logic, typed contract/RPC, and obsolete tests:

1. **A2VM LED Read Probe** — the “A2VM LED Profile Read-Only Probe” section inside the existing Vibration Test subpage.
2. **Battery Charge Limit Test** — its entire Developer Menu `SettingsCard`, child page and automated validation report runner.
3. **Xbox360 Terminal STOP Loop** — the corresponding section inside the existing Vibration Test subpage, together with diagnostic-only XInput burst replay and USB trace capture machinery.
4. **GameInput System Button Probe** — its entire Developer Menu `SettingsCard`, child page and diagnostic-only GameInput native callback registration.

**Important UI scope distinction:** Items (1) and (3) are **sections of the same existing `VibrationTestPage`**, not standalone Developer Menu cards. Removing both MUST NOT accidentally delete the remaining **“Vibration Profile 0/100 Probe”** (Apply Left 0 / Right 100, Restore Addon default 50 / 50). Keep that probe usable, including its explicit restore/status/transport-failure behavior.

After removal, the old “Vibration Test” card should remain as the navigation entry for the 0/100 profile probe, but **rename its title and description** to accurately describe the only remaining action, e.g. **“Vibration Profile 0/100 Probe”**. Rename the child page heading similarly. It remains clearly labeled a developer-only **physical hardware mutation**, with explicit restore instructions; do not imply it is read-only or restores the unknown hardware factory value.

Remove the entire Battery Charge Limit Test and GameInput System Button Probe SettingsCards, not merely their Open buttons, visibility, or enabled state.

Leave **Virtual Touchpad (Experimental), Environment Discovery, Gyro / Sensor Test, Fan Hardware Probe, Log Folder**, and other unrelated developer features unchanged.

## 2. Full1902 authority and hard exclusions

Read `docs/Full 1902 Implementation/README.md` and apply its precedence, particularly:

- `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Historical background, not an instruction to retain retired feature code:

- `docs/work-order/FULL1902_XBOX360_TERMINAL_STOP_LOOP_DIAGNOSTIC_WORK_ORDER_2026-09-26.md`
- `docs/work-order/FULL1902_XBOX360_LIES_OF_P_RUMBLE_REPLAY_DIAGNOSTIC_WORK_ORDER_2026-09-27.md`
- `docs/work-order/WING_GAMEINPUT_SYSTEM_BUTTON_DEVICE_IDENTITY_PROBE_WORK_ORDER_2026-10-05.md`
- `docs/work-order/2026-10-09_A2VM_LED_VIBRATION_DEVELOPER_DIAGNOSTICS_WORK_ORDER.md`
- `docs/work-order/DEVELOPER_DIAGNOSTIC_LAZY_MATERIALIZATION_WORK_ORDER_2026-10-06.md`
- `docs/work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md`

**Not in scope / MUST be preserved:**

- **Device > Battery charge limit**: persisted production configuration, capture/mutation RPCs `CaptureBatteryChargeLimitAsync`, `SetDeviceBatteryChargeLimitEnabledAsync`, `SetDeviceBatteryChargeLimitPercentAsync`, `MsiClawBatteryChargeLimitHardware`, `MsiClawBatteryChargeLimitRuntime`, startup/resume reconcile, profile/settings persistence and normal Quick Settings.
- **Controller > Joystick LED**: real supported static LED profile writes, saved settings, `MsiClawLedController.ApplyAsync`, `MsiClawLedProtocol.TryResolveRgbAddress`, `TryBuildStaticWrites`, supported RGB address map **including 0x024A for firmware 0x0230**, exact strong physical-identity checks and production apply/reconcile.
- **Production Xbox360/SteamDeck/DualSense Edge vibration**: `Xbox360RumbleFeedbackBridge` native callback registration, callback-root lifetime, motor magnitude conversion, physical sink writes, write-drain lock, 5-second dead-man safety STOP, feedback clear/drain/disposal, lifecycle STOP, native detach/teardown and fail-close. **Controller > Vibration Strength**, Left/Right physical motor tests and the **Restore Vibration** production recovery command `RunDeveloperRumbleRearmAsync` remain. Despite its historical “Developer” method name, that command is called by the Controller page and is **not** being retired.
- **WING / native Game Bar suppression** and supported elevated-game behavior, `WinGSuppressionGuard`, keyboard hooks, normal front-button mapping, Steam/Game Bar state routing. The removed GameInput probe is a separate explicitly started native callback diagnostic, **not** the production WING suppression owner.
- Center M Enabled/Disabled reboot-bound authority, PID1901↔PID1902 restoration, DirectInput physical source, HidHide ownership, VIIPER ownership/feedback, PnP re-enumeration, Sleep/Hibernate/Resume, process restart/crash/shutdown and fail-close. Standalone app: NO CTW integration.

No new authority, state machine, lock, manager, wrapper, retry framework, second callback owner or compatibility shim to replace dead tooling.

## 3. Verified UI / navigation removal closure

### 3.1 Developer Menu

`src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml`:

- Delete complete **Battery Charge Limit Test** and **GameInput System Button Probe** `<ctcontrols:SettingsCard>` elements.
- Keep the one vibration profile entry, changing **`Header="Vibration Test"`** and its Xbox360-terminal-STOP description to **`Header="Vibration Profile 0/100 Probe"`** with an accurate firmware-write warning. Preserve its Open navigation.
- Keep all unrelated cards and order.

`src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs`:

- Delete `BatteryChargeLimitTestRequested`, `GameInputSystemButtonProbeRequested` and their Open button handlers.
- Preserve the vibration profile navigation event/handler (rename for clarity only if it can be done without needless churn), and the other developer handlers.

### 3.2 Vibration page — KEEP, reduce to the 0/100 probe

`src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml` and `.xaml.cs`:

- Remove the **Xbox360 Terminal STOP Loop** heading, instructions, `StatusText`, `RunDetailsText`, `FailureText`, Start/Stop controls, periodic snapshot rendering and related transport invocation, timer, stop task, callbacks and state enum dependencies.
- Remove the **A2VM LED Profile Read-Only Probe** heading, instructions, `ReadA2vmLedProfileProbeButton`, `A2vmLedProfileProbeStatusText`, read handler and its operation method.
- Keep the “Vibration Profile 0/100 Probe” instructions/explicit risk notice, the `ApplyVibrationProfileProbeButton` and `RestoreVibrationProfileProbeButton`, the `FrontendControllerVibrationProfileWriteProbeMode` API, current result/error visibility and busy protection. Remove obsolete checks against `_snapshot.State == FrontendXbox360RumbleLoopState.Running`; no loop can run after retirement.
- Simplify the page's Activate/Deactivate/Initialize/Render only as appropriate for a simple one-shot action. Do not leave a 1-second `DispatcherTimer` or a permanent rumble snapshot merely to avoid touching navigation; do not invent another page manager. The active profile write's visible UI/busy state should continue functioning.
- Simplify or keep main-window page shutdown navigation only as needed; never allow the removal to bypass **fan probe** or **gyro probe** shutdown.

### 3.3 Delete two complete child pages and their battery-only runner

Delete:

- `src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitValidationRunner.cs`
- `src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml.cs`

Remove associated Developer-only navigation and lifetime plumbing in:

- `src/SteamInputAddonforClaw.UI/MainNavigationState.cs`: retired `MainNavigationPage` entries and `OpenBatteryChargeLimitTest` / `OpenGameInputSystemButtonProbe` / mouse-back cases. **Keep `VibrationTest`, `FanHardwareProbe` and `ClawSensorProbe` entries.**
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`: retired page fields, event subscriptions, `GetOrCreate` methods, `ShowPage` switch/activation/deactivation branches. Remove **developer battery-validation-only navigation guard** `IsBatteryValidationBlockingNavigation` and its `MainNavigationView_SelectionChanged` / pointer-back branches. Ordinary navigation must still work; the production Device battery setting needs no such developer validation runner.
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`: remove the invocation of `CloseGameInputSystemButtonProbeForUiShutdownAsync` after that method is deleted. **Keep other shutdown calls**, including `CloseVibrationTestForUiShutdownAsync` if it still owns required fan-page cleanup, and `CloseClawSensorProbeForUiShutdownAsync`.
- Check `MainWindow.xaml` and compile-generated XAML/code-behind references; current pages are created lazily in `MainWindow.xaml.cs`, not eager standalone XAML children.

## 4. Retire dedicated frontend/IPC contracts — one protocol increment

### `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`

Delete *only* these developer types and `IAddonFrontendControl` methods:

**A2VM LED read:**
- `FrontendControllerLedProfileReadProbeOutcome`
- `FrontendControllerLedProfileReadProbeResult`
- `RunControllerLedProfileReadProbeAsync`

**Battery developer test:**
- `FrontendBatteryChargeLimitTestSnapshot`
- `FrontendBatteryChargeLimitTestMutationOutcome`
- `FrontendBatteryChargeLimitTestMutationResult`
- `CaptureBatteryChargeLimitTestAsync`
- `SetBatteryChargeLimitTestEnabledAsync`
- `SetBatteryChargeLimitTestPercentAsync`

**Xbox360 rumble loop:**
- `FrontendXbox360RumbleLoopState`
- `FrontendXbox360RumbleLoopSnapshot`
- `CaptureXbox360RumbleLoopDiagnosticAsync`
- `StartXbox360RumbleLoopDiagnosticAsync`
- `StopXbox360RumbleLoopDiagnosticAsync`

**GameInput probe:**
- `CaptureGameInputSystemButtonProbeAsync`
- `StartGameInputSystemButtonProbeAsync`
- `StopGameInputSystemButtonProbeAsync`
- **Delete file** `src/SteamInputAddonforClaw.Contracts/Frontend/GameInputSystemButtonProbeContracts.cs` if all declarations are probe-only (they are in reviewed `main`).

**Keep, notably:**
- `FrontendBatteryChargeLimitSnapshot`, `FrontendBatteryChargeLimitMutationResult` and the production Device RPCs.
- `FrontendControllerVibrationProfileWriteProbeMode`, its outcome/result, and `RunControllerVibrationProfileWriteProbeAsync`.
- `FrontendControllerVibrationStrengthSnapshot` / motor test, `FrontendControllerLed` production settings and `FrontendDeveloperRumbleRearmResult` / `RunDeveloperRumbleRearmAsync`.

### `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`

Delete **these 10 active RPC methods**:

```text
RunControllerLedProfileReadProbe
CaptureBatteryChargeLimitTest
SetBatteryChargeLimitTestEnabled
SetBatteryChargeLimitTestPercent
CaptureXbox360RumbleLoopDiagnostic
StartXbox360RumbleLoopDiagnostic
StopXbox360RumbleLoopDiagnostic
CaptureGameInputSystemButtonProbe
StartGameInputSystemButtonProbe
StopGameInputSystemButtonProbe
```

Delete `SetBatteryChargeLimitTestEnabledRequest` / `SetBatteryChargeLimitTestPercentRequest`. Keep production Device charge-limit request records and unrelated methods.

Advance `FrontendTransportProtocol.CurrentVersion` exactly **once** (79 -> **80** if still 79). Add a new short “Version 80” historical comment explaining the four diagnostic retirements. Do not delete preceding version-history entries. No backwards-compatible RPC shim is needed for this pre-release app. Do not bump the separate Overlay transport protocol.

### `NamedPipeAddonFrontendClient.cs` / `NamedPipeAddonFrontendServer.cs`

- Remove all 10 client methods / server dispatch branches.
- Remove the retired no-argument methods from the server’s no-payload allowlist.
- Remove `rumbleLoopMayBeRunning` from the server’s per-connection lifetime, the special start/stop response tracking, and the disconnect-time `StopXbox360RumbleLoopDiagnosticAsync` call. **Preserve generic request cancellation, `probeSessionMayBeOpen` if used by retained gyro probe, and all ordinary connection teardown.**
- Preserve remaining vibration profile write, motor tests, Restore Vibration recovery, LED production settings and battery **production** RPCs.

### `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`

- Remove the six rumble + GameInput delegate fields/constructor parameters/assignments plus the LED read delegate; remove their method implementations/mapping helpers.
- Remove the three developer battery-test method implementations and associated **developer-only** `MapBatterySnapshot`, `UnavailableBatterySnapshot`, `MapBatteryMutationOutcome`, `SetBatteryChargeLimitTestAsync` if not used by production.
- Retain `_batteryChargeLimitHardware` and `_batteryChargeLimitRuntime` and all production Device/Quick Settings work. Determine whether `IsSupportedBatteryHardware` is shared before deleting any helper.
- Retain `RunExclusiveControllerOperationAsync` if still used by the surviving vibration profile probe, physical motor operations or the Controller Restore Vibration command; do not delete a shared concurrency/safety gate.

## 5. Diagnostic-only Runtime/service removal without harming lifecycle

### 5.1 A2VM LED read-only diagnostic

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: remove `controllerLedProfileReadProbe` composition argument and `RunA2vmLedProfileReadProbeAsync` host helper.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs`: remove only `MsiClawLedProfileReadProbeOutcome`, `MsiClawLedProfileReadProbeResult`, and `ReadA2vm230CandidateProfileAsync`. **Keep `MsiClawLedController` and its production `ApplyAsync`, physical identity resolution and validation.**
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedProtocol.cs`: remove candidate **read-request** construction, response parse/types/constants when exclusively consumed by the removed probe. **Keep `TryResolveRgbAddress`, `TryBuildStaticWrites` and the `0x024A` production RGB address mapping.** The shared `0x024A` value is real product data, not itself an obsolete probe.

### 5.2 Battery Charge Limit Test

- Remove only the Developer UI/runner/contracts/endpoints in sections 3–4.
- **Do not** remove the shared `MsiClawBatteryChargeLimitHardware` or `MsiClawBatteryChargeLimitRuntime` initialized by `AddonProcessHost` and used for production Device battery charge limit.
- Keep normal settings store, current battery limit startup/resume reconcile, real hardware writes/readbacks, and typed failure handling.
- Remove the obsolete developer-only report-writing/test chain, **not** global `UiLog` or logging filesystem logic.

### 5.3 Xbox360 terminal STOP loop and trace

- **Delete** `src/SteamInputAddonforClaw/Diagnostics/Xbox360RumbleLoopDiagnostic.cs`.
- **Delete** `src/SteamInputAddonforClaw/Diagnostics/Xbox360UsbTraceCapture.cs` if its only live consumer is the loop diagnostic (verified in current `main`).
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`:
  - Remove the loop-only public/interface capture/start/stop methods, fields `_rumbleLoopDiagnostic`, `_rumbleLoopStop`, `_rumbleLoopTask`, constructor factories/cadence parameters, readiness helper, `ObserveXbox360RumbleCallback`, and `CancelRumbleLoopForLifecycleLockedAsync` / `FinalizeRumbleLoopAfterLifecycleLockedAsync` / `ReleaseRumbleLoopTaskResourcesLocked`.
  - Remove the diagnostic-specific “already running” rejection in `RunDeveloperRumbleRearmAsync`, but **preserve that recovery action's real admission checks**, including VIIPER/feedback owner state and motor-test guard.
  - Remove now-dead loop stop/join/finalization calls from Overlay Pause, Suspend Pause, presentation retirement and dispose. **Do not remove or weaken the immediately adjacent production publisher-stop, feedback callback drain/disarm, physical STOP, VIIPER detach or fail-close operations.** Keep current production teardown ordering.
  - Keep `ArmFeedbackForActivePresentationLocked` and all regular rumble delivery, fault and safety-stop paths.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: remove loop capture/start/stop delegate hookup and corresponding host forwarding methods; remove the loop trace-capture factory passed when creating the presentation owner. Remove the **loop-only** running check in `RunDeveloperRumbleRearmCoreAsync`, but retain all other admission/fail-close and restoration behavior.
- `src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs`:
  - `Xbox360RumbleFeedbackBridge` is **production**: keep this class, its native callback, sequence/DEBUG production telemetry, physical write-drain lock, safety timeout, STOP-after-idle and dispose/teardown.
  - Remove only `diagnosticObserver` / `_diagnosticObserver` forwarding and `WriteDiagnosticPhysicalStop` if unused after the loop removal. Do not remove `_diagnosticRumbleSequence` **solely due to its name** if still used for production `ProductionXbox360RumbleRx` / write correlation DEBUG logging. Do not conflate the test's forced STOP with the production STOP.
- Be careful with methods containing both diagnostic finalization and production cleanup in one `try/finally`. Remove the diagnostic layer **without losing a production cleanup step**; no new lifecycle machinery should be introduced.

### 5.4 GameInput System Button Probe

- **Delete** `src/SteamInputAddonforClaw/Diagnostics/GameInputSystemButtonProbe.cs`, including its private native `IGameInput` COM callback interfaces / session factory: these belong to the standalone probe, not the supported WING guard.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: remove `_gameInputSystemButtonProbe`, lazy construction and capture/start/stop delegates, and its shutdown dispose.
- Retain **all production WING suppression, elevated-game support and front-button mapping code**, even if similarly named “GameInput” or “SystemButton”.

## 6. Tests — remove diagnostic assertions, preserve product coverage

**Delete diagnostic-exclusive suites**:

- `tests/SteamInputAddonforClaw.Tests/Xbox360RumbleLoopDiagnosticTests.cs`
- `tests/SteamInputAddonforClaw.Tests/Xbox360UsbTraceCaptureTests.cs`
- `tests/SteamInputAddonforClaw.Tests/GameInputSystemButtonProbeTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/BatteryChargeLimitValidationRunnerTests.cs`

**Update mixed suites narrowly**:

- `FrontendNamedPipeTransportTests.cs`: remove diagnostic battery/loop/GameInput/LED round-trips, invalid payload, disconnect loop-stop assertions, and fake control fields/methods; **retain separate production battery, vibration strength, vibration profile probe, LED settings, Restore Vibration and their validation tests**. A test currently combines developer and production battery calls: split/keep production assertions rather than deleting that test wholesale.
- `FrontendContractTests.cs`, `VibrationContractRemovalTests.cs`: retire obsolete positive expectations for the Xbox360 loop/other removed typed contract; retain the legacy-vibration-contract removal assertions. Add a compact negative reflection/wire test verifying the 10 retired method names and retired developer result types are absent.
- `ControllerLedTests.cs`: remove candidate LED **read** parsing/request/transport tests and fixtures exclusively used there; retain product LED write tests, RGB address map tests and firmware safety gates. `ControllerLedFrontendTests.cs` remove developer read assertions but preserve real LED settings/apply tests.
- `ControllerLedUiContractTests.cs`: remove “Read 0x024A Profile” presence requirement; keep production Controller LED UI coverage.
- `MsiClawAddonPresentationTests.cs` and `ControllerVibrationLifecycleContractTests.cs`: remove loop/replay/trace-specific expectations only; retain/adjust genuine production feedback arm/write/STOP, pause/suspend/teardown and Restore Vibration ownership tests.
- `ControllerVibrationStrengthUiTests.cs`: retain both motor buttons, Controller Restore Vibration, and developer 0/100 profile-probe contract tests.
- `UiArchitectureTests.cs`, `MainNavigationStateTests.cs`, `SettingsPageLoggingTests.cs`: update requested card order/absence, removed lazy-page cases and obsolete test-page references; keep vibration 0/100 page navigation, fan and gyro lifecycle tests. Rework tests that inspect a now-deleted source file, rather than leaving file-read failures.
- `FrontendNamedPipeTransportTests.cs` has **hardcoded protocol 79 inside JSON `InlineData`** in addition to `CurrentVersion` assertions. Advance these hardcoded values to actual current protocol too; keep malformed-method validation meaningful.
- Search for additional hardcoded protocol numbers in all core/UI tests (e.g. `CenterMStartupContractTests`, `FrontButtonTransportContractTests`, `OverlayDeviceQuickSettingsTransportTests`, `OverlayTransportTests`, `XboxGameSessionRuntimeHostTests`) and update only their frontend-version expectations. The Overlay protocol remains unchanged.

Tests that validate live normal GameInput/WING suppression, production battery charge-limit hardware/runtime, physical rumble safety STOP or Static LED writes must remain. Do **not** delete a production safety regression test just because a diagnostic name appears nearby.

Historical `docs/work-order/*` and research documents should stay intact.

## 7. Example code-shape guardrails (illustrative; adapt to current code)

Retain the minimal Vibration profile probe:
```csharp
private async void ApplyVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
    await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

private async void RestoreVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
    await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty);
```

Production rumble callback still arms directly, without the removed diagnostic observer:
```csharp
AddonPresentationKind.Xbox360 =>
    Xbox360RumbleFeedbackBridge.TryArm(
        _rumbleSink,
        cb => _viiper!.SetXbox360RumbleCallback(cb)),
```

Production device battery API stays separate from deleted developer-test API:
```csharp
// KEEP: production Device battery control
Task<FrontendBatteryChargeLimitSnapshot> CaptureBatteryChargeLimitAsync(CancellationToken cancellationToken = default);
Task<FrontendBatteryChargeLimitMutationResult> SetDeviceBatteryChargeLimitEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
Task<FrontendBatteryChargeLimitMutationResult> SetDeviceBatteryChargeLimitPercentAsync(int percent, CancellationToken cancellationToken = default);
```

Remove the loop-running special case without removing the production recovery command:
```csharp
// In RunDeveloperRumbleRearmCoreAsync: remove the
// CaptureXbox360RumbleLoopDiagnosticAsync(...) + Running condition only.
// Continue into the existing physical.CheckDeveloperRumbleRearmAdmissionAsync()
// and the already-implemented fail-closed presentation/physical restoration.
```

Do not implement a “diagnostic compatibility stub”, hidden checkbox, or a replacement manager.

## 8. Code-only validation and acceptance

Local Codex must:

1. Ensure the entire **Battery Charge Limit Test** and **GameInput System Button Probe** SettingsCards and child pages are gone; no navigation route, event, shutdown hook, lazy allocation or orphan XAML reference remains.
2. Ensure the Developer Menu still opens the sole **Vibration Profile 0/100 Probe**, applies the same explicit test profile, and offers the same explicit 50/50 restore; no Xbox360 Terminal STOP Loop or A2VM LED Read UI remains.
3. Ensure all 10 retired frontend RPCs, their typed request/results, client/server branches and diagnostic-only Runtime classes are absent. Current frontend protocol is bumped once and current-version tests are consistent. Keep the separate Overlay protocol unchanged.
4. Verify *by code/tests* that production Battery Charge Limit, Controller Joystick LED Static writes, motor-strength controls, physical test buttons, Controller Restore Vibration, Xbox360 rumble safety dead-man/STOP and WING suppression remain wired.
5. Verify existing realistic lifecycle safety by unit tests: feedback disarm/drain and STOP before teardown, motor-latch prevention, presentation pause/suspend/restart paths, fail-close and cleanup; no extra theoretical race defenses.
6. Run repository-supported Release solution build, core/UI unit suites, focused affected controller/frontend/transport tests, and `git diff --check`. Keep required CI successful.
7. Search executable source and current tests for all retired symbols, files and UI strings, distinguishing historical docs / legitimate production “battery”, “LED”, “rumble”, “GameInput” terms.
8. Keep all unrelated UI and supported Full1902 functions unchanged; no additional backend architecture/rewrite.

**Hardware testing is explicitly the user's post-merge responsibility**, not a blocker or obligation imposed on local Codex. A compile/test-clean implementation can be merged without an on-device manual validation run.

**Deliverable:** one focused PR retiring only these four developer tools. A large negative LOC delta is expected; do not split solely because deleting entire diagnostic-only files produces many removed lines.
