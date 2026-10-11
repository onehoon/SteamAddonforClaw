# Work Order — P2-B: Remove XBOX Startup Reconcile from the First Full1902 Controller Critical Path

**Date:** 2026-10-11  
**Product:** SteamAddonforClaw, standalone Full1902  
**Baseline:** `main` at `282ac870ee40d8929e6151283f622b67384031f3` (PR #747 P2-A squash-merged)  
**Scope:** One focused implementation PR, shared EX/CG3EM + A2VM 7/8 code path  
**Owner:** Local Codex implements and runs software tests. Physical EX/A2VM verification is **user-owned after merge**, never a PR blocker.

## 1. Decision / executive contract

Make the first verified Full1902 Xbox360/SteamDeck publisher **higher priority than non-controller XBOX Game Pass process discovery**.

The current `InitializeRuntimeAsync()` blocks until the production `XboxGameSessionRuntime.StartAsync()` completes its window/process reconciliation. This delays entry into the native message loop, installation of the mandatory Win+G hook, and the deferred controller acquisition/presentation. No physical controller, HidHide, VIIPER or Steam/BPM requirement depends on XBOX game identity being ready first.

**Recommended narrow change:** Keep the existing single `XboxGameSessionRuntime` instance and all its subscriptions/consumers, but move its **awaited initial StartAsync** from `InitializeRuntimeAsync` into the **already-owned deferred startup flow, after the initial controller acquisition/first-presentation attempt**. Do not fire-and-forget it and do not rewrite the XBOX observer's internals. Its one existing startup window scan still executes and is awaited before Device/Profile startup reconciliation so that an already-running XBOX title receives its correct profile rather than a transient idle/default profile.

The controller-first order becomes:

~~~text
RunStartupAsync / Center M + required HidHide admission and startup-task checks
  -> InitializeRuntimeAsync:
       compose one XboxGameSessionRuntime and subscribe its existing events
       set conservative initial cached controller cadence (250 Hz until observation ready)
       finish normal Runtime composition / frontend transport
  -> message loop pumps / WH_KEYBOARD_LL Win+G hook installed
  -> StartDeferredRuntimeStartup, existing controller-critical attempt:
       VIIPER Ready
       PID1902 / DirectInput / HidHide ownership
       Win+G suppression verified
       freshest Steam/BPM snapshot
       first Xbox360 OR SteamDeck attach + publisher
       P2-A LED/vibration post-presentation follow-up
       clear existing Disabled-controller-startup admission
  -> StartClawHudStartup (unchanged existing fire-and-forget initiation)
  -> StartPowerObservation (keep prompt; do not delay power lifecycle behind XBOX scan)
  -> await XboxGameSessionRuntime.StartAsync (existing hooks and bounded initial scan)
       ActiveGameChanged / ObservationReadinessChanged still authoritative
  -> ReconcileDeviceProfileStartup (now uses the initial Xbox game fact, if any)
  -> StartBackgroundUpdate (preserve existing relative order after profile initialization)
~~~

**Do not hard-code X360 as the initial gamepad.** Steam RunningAppID/BPM alone selects the existing initial SteamDeck vs Xbox360 virtual presentation. XBOX active-game identity affects game profiles, M1/M2 overrides and adaptive 125/250 Hz cadence, **not** Xbox360-vs-Deck selection.

Do not create a new task/lock/manager/state authority merely to overlap startup. Moving one awaited call between phases is simpler, preserves startup completion and shutdown ownership, and places the optional work where the product wants it.

## 2. Verified code/root cause

### 2.1 Blocking chain in current main

`src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs`:

~~~csharp
_processHost.InitializeRuntimeAsync().GetAwaiter().GetResult();
_processHost.TryInitializeTray(RequestRestart);
_messageLoop.Run(() =>
{
    _processHost.StartRuntimeEventWatchers();
    _processHost.StartDeferredRuntimeStartup();
});
~~~

`src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`, `InitializeRuntimeAsync()`:

~~~csharp
_xboxGameSessionRuntime = new XboxGameSessionRuntime();
_xboxGameSessionRuntime.ActiveGameChanged += OnActiveXboxGameChanged;
_xboxGameSessionRuntime.ObservationReadinessChanged += OnXboxObservationReadinessChanged;
try
{
    await _xboxGameSessionRuntime.StartAsync(
        _startupCancellationTokenSource.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested)
{
    throw;
}
catch (Exception exception)
{
    AppLog.Warn("XboxSession",
        "Production XBOX game-session observer could not start; Runtime will continue without active XBOX detection.",
        exception);
}
UpdateControllerCadenceDecision("StartupObservationReconciled", logInitialDecision: true);
~~~

`src/SteamInputAddonforClaw/Xbox/Session/XboxGameSessionRuntime.cs`, `StartAsync()`:

~~~csharp
await StartWindowSourceAsync(_sessionCancellation.Token, cancellationToken)
    .ConfigureAwait(false);
Volatile.Write(ref _acceptWindowEvents,
    Volatile.Read(ref _eventSourceFailureObserved) == 0 ? 1 : 0);
var reconciled = await ReconcileSafelyAsync(
    cancellationToken, "startup").ConfigureAwait(false);
SetReadinessAfterReconcile(reconciled);
~~~

`ReconcileSafelyAsync` sends `ReconcileMessage` through the existing worker, enumerates up to `MaximumReconcileWindows = 512` candidates, inspects package/process identity and waits for the result. This **is a real awaited Windows I/O operation**, not just cheap callback registration.

The existing `StartDeferredRuntimeStartup()` already runs the controller attempt asynchronously **after the native hook was installed from the pumping message loop**, then starts ClawHUD/power/profile/update. Therefore the fix does not require rearchitecting the process main loop, Win+G installation, XBOX session internals, or VIIPER.

### 2.2 Log evidence: optional XBOX wait ahead of controller

The logs are **pre-P2-A** and from different versions/boot states; do not combine their later LED/vibration numbers with projected P2-B improvements.

| Sample | XBOX hooks started | Initial XBOX reconcile completed | XBOX initial scan elapsed | First virtual publisher | Notes |
| --- | --- | --- | ---: | --- | --- |
| EX/CG3EM 2026-10-08 23:42:15, v0.1.345 | 23:42:16.809 | 23:42:16.974 | ~164 ms | 23:42:18.293 X360 | Warm application restart; PID1902 already present |
| A2VM 8 2026-10-11 11:53, v0.1.357 | 11:53:27.750 | 11:53:30.376 | ~2,627 ms | 11:53:53.021 Deck | Cold Windows boot; A2VM boot-rumble PID1902/PID1901 path |
| A2VM 8 2026-10-11 11:55, v0.1.357 | 11:55:45.102 | 11:55:46.873 | ~1,771 ms | 11:56:06.293 Deck | Distinct Windows startup/physical re-enumeration timings |

Source references:

- [EX Runtime log](https://drive.google.com/file/d/1cSmCASUUTXEAddMiPBpHFz5hNWzp2D29/view)
- [A2VM 11:53 Runtime log](https://drive.google.com/file/d/1zLGLmBjdZlIrvzMp81qyZlP9v_e_u4CD/view)
- [A2VM 11:55 Runtime log](https://drive.google.com/file/d/1-sDj1F4pa3wTuecPLinfGDzvSY8M1FSj/view)

In the EX sample, Disabled admission was ready at 23:42:16.689; the first Win+G hook was installed at 23:42:17.048. In the 11:53 A2VM boot, admission was ready at 11:53:26.709 but the hook was installed only at 11:53:30.659; the XBOX scan occupies a substantial part of that interval. Source code establishes the serial dependency; timestamps alone do not prove every remaining gap is caused by XBOX.

**Claim only the removed critical-path dependency**, not an unconditional 2.6-second wall-clock saving, a guaranteed boot-to-input SLA, or elimination of the separately expensive A2VM hardware/PnP transitions. Existing XBOX scanning work still happens after initial controller publication; P2-B moves it rather than deleting it.

## 3. Read these design and feature references first

Active Full1902 authority:

- `docs/Full 1902 Implementation/README.md` (document precedence);
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` (strict Disabled admission, mandatory startup task and HidHide);
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`;
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` (§14 initial desired presentation);
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (Runtime process privilege and startup-task level).

Feature and recently merged phases:

- `docs/work-order/2026-10-11_FULL1902_P2A_FIRST_PRESENTATION_BEFORE_LED_VIBRATION_WORK_ORDER.md` (implemented/merged PR #747);
- `docs/work-order/FULL1902_ADAPTIVE_IDLE_125HZ_CONTROLLER_CADENCE_WORK_ORDER_2026-10-10.md` (critical: **250 Hz while XBOX observer readiness is unknown**, not 125 Hz);
- `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`;
- `docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR8_XBOX_SHARED_ACTIVE_PROFILE_TARGET_AND_LIVE_APPLY_WORK_ORDER_2026-10-06.md`;
- `docs/work-order/PR10_NON_STEAM_PER_GAME_M1_M2_CONTROLLER_OVERRIDE_FOUNDATION_XBOX_FIRST_CONSUMER_WORK_ORDER_2026-10-07.md` (historical startup assumption may need updating, not a policy that XBOX discovery must gate the controller);
- `docs/work-order/2026-10-11_A2VM_V01357_WINDOWS_SESSION_END_PID1901_P0_P1_REPAIR_WORK_ORDER.md` (separate completed PR #746; **do not alter**).

The product has one Windows user, one interactive session; FUS/RDP/multisession and hypothetical timing interleavings are out of scope. CTW is not a dependency.

## 4. Implementation instructions

### 4.1 Keep existing observer creation and subscriptions in InitializeRuntimeAsync

Keep these lines **where they are**, behind the existing `_headlessUninstallPreparation` guard:

~~~csharp
_xboxGameSessionRuntime = new XboxGameSessionRuntime();
_xboxGameSessionRuntime.ActiveGameChanged += OnActiveXboxGameChanged;
_xboxGameSessionRuntime.ObservationReadinessChanged += OnXboxObservationReadinessChanged;
~~~

Delete only the *immediate* `await _xboxGameSessionRuntime.StartAsync(...)` and its existing try/catch from this method. The `_xboxGameSessionRuntime` field must already exist for frontend profile target/display-name capture, the existing cadence callback provider, resume subscription, and shutdown/disposal ownership. Do not move observer construction behind the initial controller acquisition.

Immediately after these event subscriptions, or at the corresponding pre-controller-runtime point, preserve the once-per-startup cadence decision with **an explicit pending-observer trigger**:

~~~csharp
// No XBOX session scan has established reliable idle yet.
// Existing policy: select 250 Hz until a healthy observer can prove idle.
UpdateControllerCadenceDecision("StartupObserverPending", logInitialDecision: true);
~~~

The current `_controllerCadenceRequires250Hz = 1` default and
`RequiresFastControllerCadence(steam, xboxActive, xboxCanReliablyReportNoActiveGame)`
already implement this. **Do not accidentally keep a stale 125 Hz decision** from a previous idle probe. `ObservationReadinessChanged` later triggers `UpdateControllerCadenceDecision("XboxObserverReady")` or `"XboxObserverUnavailable"` and `ActiveGameChanged` updates the cache when a real XBOX title appears/exits.

Do not change `RequiresFastControllerCadence`, `MsiClawInputSource` timer logic, `CanonicalXbox360InputPublisher`, Steam/BPM presentation choice, or session detection.

### 4.2 Start and await the existing observer only after the controller critical attempt

At the reviewed main baseline, `StartDeferredRuntimeStartup()`:

~~~csharp
_deferredRuntimeStartup = Task.Run(async () =>
{
    try
    {
        await TryStartDisabledModeControllerAsync(
            _startupComposition!, _startupResult!, _runtimeStartupSettings!)
            .ConfigureAwait(false);
    }
    catch (Exception exception)
    {
        AppLog.Error("ControllerOwnership",
            "Deferred Disabled-mode controller startup threw; Runtime remains available.", exception);
    }
    finally
    {
        var previousBadge = CaptureCurrentControllerBadge();
        Volatile.Write(ref _disabledControllerStartupPending, 0);
        NotifyControllerBadgeIfChanged(previousBadge);
    }

    StartClawHudStartup();
    StartPowerObservation();
    ReconcileDeviceProfileStartup();
    StartBackgroundUpdate();
}, cancellationToken);
~~~

Change the post-controller section to start the existing non-controller work in this order:

~~~csharp
// Keep the original controller try/catch/finally and admission release unchanged.
if (cancellationToken.IsCancellationRequested ||
    Volatile.Read(ref _processShutdownStarted) != 0)
    return;

StartClawHudStartup();
StartPowerObservation(); // Do NOT wait for XBOX process probing before registering power events.

// StartAsync retains the same WinEvent hooks, bounded initial process scan,
// readiness callbacks and internal lifecycle gate. It runs after the initial
// publisher attempt so that it cannot hold up the first controller attach.
if (_xboxGameSessionRuntime is { } xboxObserver)
{
    try
    {
        await xboxObserver.StartAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return;
    }
    catch (Exception exception)
    {
        AppLog.Warn("XboxSession",
            "Production XBOX game-session observer could not start; Runtime will continue without active XBOX detection.",
            exception);
    }
}

if (cancellationToken.IsCancellationRequested ||
    Volatile.Read(ref _processShutdownStarted) != 0)
    return;

// Preserve active XBOX target recognition before the first Device/Profile
// startup reconcile. This avoids applying the default/idle profile to a game
// already running at startup and then immediately undoing that change.
ReconcileDeviceProfileStartup();
StartBackgroundUpdate();
~~~

Adapt to actual local code and keep its existing exception policy. The high-level contract matters more than incidental formatting.

**Why not simply `_ = xboxObserver.StartAsync(...)`?**

- Untracked observer startup may outlive host teardown or have unobserved exceptions.
- The existing `_deferredRuntimeStartup` task already provides a natural owner for a *late* await and `DisposeAsync` already joins it before disposing the XBOX observer.
- Starting the XBOX session only after the controller attempt means the process/window scan does not compete with a cold, PnP-heavy A2VM acquisition for startup work, nor can it prevent the message loop / Win+G hook from being installed.
- The observer internally serializes start/resume/stop with `_lifecycleGate`; no new general synchronization is needed.

**Why place `ReconcileDeviceProfileStartup()` after the observer?**

- The existing initial XBOX game detection may prove an already-running game and set `ActiveGame`.
- Before `_profileRuntimeStartupReady` becomes 1, `OnActiveXboxGameChanged` already reconciles effective M1/M2 mapping; the full performance profile is reconciled by `ReconcileDeviceProfileStartup()` using `CaptureActiveProfileTarget()`.
- Keeping this relative ordering avoids the visible behavior change of briefly applying the global/default profile and then reapplying the active XBOX profile.
- If the observer cannot start or its initial scan fails, normal profile startup must still continue. The failure stays XBOX-feature-local; it does not roll back an already-live Full1902 controller.

### 4.3 Shutdown / physical / power lifecycle invariants

Preserve these existing contracts:

- `RuntimeProcessApplication.Run()`: `InitializeRuntimeAsync` completes; tray registration; native message loop begins; `StartRuntimeEventWatchers()` installs `WH_KEYBOARD_LL` on its pumping thread; controller work begins from `StartDeferredRuntimeStartup()` off that thread.
- Center M Disabled authority and exact HidHide baseline must still be proven before owned physical PID1902 and VIIPER publication; no changes to the fail-closed conditions.
- `_disabledControllerStartupPending` must be cleared in its existing controller try/finally, not held open until XBOX scanning completes. Optional game discovery is not an authority transition.
- Stock/Center M Enabled: no initial Full1902 virtual controller, but the XBOX observer still starts after the no-op controller attempt; normal UI and XBOX profile capability remain available.
- Controller acquisition failure or deferred PnP wait: XBOX observer **still starts after the initial controller attempt returns**, even if no publisher was attached. Do **not** add a success-only `PublisherStarted` gate.
- `OnPowerResumeObserved` continues invoking `XboxGameSessionRuntime.ReconcileAfterResumeAsync` using existing semantics; no second startup/reconcile path. When no observer startup has been attempted, that method's current early return is allowed; the subsequent normal startup scan remains the first valid observation.
- Preserve `_startupCancellationTokenSource` propagation to the newly located `StartAsync` call. `TryBeginProcessShutdownCore` cancels it; `DisposeAsync` drains `_deferredRuntimeStartup` *before* `_xboxGameSessionRuntime.DisposeAsync()` and `_runtimeHost.DisposeAsync()`. No detached XBOX startup task.
- A real shutdown/cancellation must not start a new observer scan, run delayed Device/Profile startup writes or launch late background updates after shutdown. Only add the minimal existing-token/shutdown checks at the boundary (shown above), without new epoch/gate/worker/manager.
- An already established observer's real WinEvent failure retains its existing readiness-false, positive-game lifetime and resume cleanup behavior. Do not alter `XboxGameSessionRuntime.cs` merely to support this relocation.

### 4.4 Observability / logging

Reuse rather than duplicate:

- `XboxSession` / `Production XBOX game-session runtime started`;
- `XboxSession` / `Bounded production XBOX session reconcile completed` (reason=`startup`, enumerated count, active key);
- `ControllerCadence` / `CadenceDecisionChanged` with `StartupObserverPending`, `XboxObserverReady`, `XboxObserverUnavailable`;
- `ControllerPresentation` / `InitialPresentationAttached` and `First presentation attach completed`.

At most add a **single** appropriately scoped log if the local implementation lacks sufficient boundaries. Do **not** create a generic startup timing framework, per-process metric instrumentation or another status authority.

Expected order on a healthy Disabled boot:

~~~text
Admission ready
-> Native message loop entering
-> Win+G hook installed
-> VIIPER initialized
-> PID1902 ownership completed
-> Win+G suppression armed
-> InitialPresentationAttached (selected X360 or Deck)
-> LED / vibration applied (P2-A)
-> XBOX observer started
-> XBOX startup reconcile completed / readiness established
-> Device/Profile startup reconciliation
~~~

The existing `StartPowerObservation()` and ClawHUD scheduling may occur before XBOX startup but **after** the controller attempt. Log order should reflect actual startup success/failure rather than asserting a successful attach where none occurred.

## 5. Required tests (software only)

### 5.1 `tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeHostTests.cs`

The existing `Normal_runtime_owns_and_starts_the_xbox_session_after_the_headless_uninstall_guard` test **currently requires the XBOX `StartAsync` await between observer creation and frontend construction**. That must change; the old assertion protects the *undesired* startup serialization.

Update to assert:

1. Observer is created/subscribed once **in `InitializeRuntimeAsync`**, after the `_headlessUninstallPreparation` guard and before frontend construction.
2. There is **no** `await _xboxGameSessionRuntime.StartAsync(...)` within the old `InitializeRuntimeAsync` observer-construction interval or anywhere on its pre-message-loop path.
3. Initial `UpdateControllerCadenceDecision(..., logInitialDecision: true)` runs with the observer still not ready (not default 125).
4. `StartDeferredRuntimeStartup` awaits `TryStartDisabledModeControllerAsync` and clears `_disabledControllerStartupPending` before `StartPowerObservation`, before XBOX `StartAsync`, and before `ReconcileDeviceProfileStartup`.
5. XBOX `StartAsync` retains existing exception containment and uses `_startupCancellationTokenSource.Token` (or the exact same captured token).
6. Controller failure or stock-authority branch does not prevent the XBOX observer from being started afterward.
7. Keep the existing resume/disposal and event-handler contract assertions. `_xboxGameSessionRuntime.DisposeAsync()` stays after draining deferred startup and before disposing `_runtimeHost`.

Use the project's existing source-order contract pattern; **do not introduce a new full-host dependency injection layer** just to time an XBOX scan.

### 5.2 Existing XBOX session and cadence tests

Run and keep passing:

- `tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeTests.cs`:
  - an already-running foreground XBOX game is found on first startup;
  - one bounded window scan finds a game when foreground isn't it;
  - positive/negative initial readiness and source failure behavior;
  - process exit, matching generation and resume.
- `tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeHostTests.cs`: existing `Active_XBOX_game_changes_reconcile_mapping_before_the_performance_startup_gate` and Steam/BPM/XBOX/Resume cadence callbacks.
- `tests/SteamInputAddonforClaw.Tests/ClawHudLifecycleContractTests.cs`: ClawHUD remains behind the same controller-critical boundary.
- Relevant adaptive 125/250 Hz cadence tests and `AddonProcessHostInitialControllerAcquisitionTests.cs`.
- Verify no existing test/documentation wrongly asserts that the XBOX initial scan must finish **before** the first virtual controller attach.

A narrow host test should assert order using the real names `StartDeferredRuntimeStartup`, `StartPowerObservation`, `XboxGameSessionRuntime.StartAsync`, `ReconcileDeviceProfileStartup`, `StartBackgroundUpdate`. Do not use wall-clock thresholds in CI.

### 5.3 Essential behavior matrix

| Scenario | Expected behavior |
| --- | --- |
| EX, Center M Disabled, normal desktop | First selected Xbox360 publisher starts before XBOX initial scan; cadence conservatively 250 until observer proves idle, then 125 |
| EX or A2VM, Steam/BPM active | Initial SteamDeck publisher starts directly; stays fixed 250 Hz |
| A2VM slow PID1901/PID1902/PnP cycle | Same existing acquisition and fail-close; no XBOX scan in its pre-publisher critical path |
| XBOX game already running on startup | Startup XBOX scan eventually recognizes it; M1/M2 mapping and first Device/Profile reconcile use that game |
| No XBOX game + observer healthy | `CanReliablyReportNoActiveGame` becomes true and idle Xbox360/physical cadence may become 125 |
| Observer cannot start or verify idle | Controller keeps running; fallback 250 Hz, no false XBOX game identity, profile startup proceeds |
| Physical PID1902 absent/initial acquisition deferred | XBOX feature still starts after the bounded initial acquisition attempt returns, without attaching a virtual controller unsafely |
| Controlled Restart / shutdown while scanning | Cancellation/drain through existing deferred startup and observer dispose; no new observer work after shutdown |
| Sleep / Hibernate / Resume | Existing resume observer reconciliation remains the single owner; do not trigger second startup |
| Center M Enabled / stock authority | No Addon-owned presentation; XBOX session/normal frontend still work |
| Windows session end (PR #746) | Same exclusive A2VM safe-retirement + best-effort PID1901 path; no regressions, no new writes |

## 6. Explicit exclusions

This is **P2-B**, not another P2-A or general rewrite.

Do not change:

- PID1901 <-> PID1902 mode writers / A2VM once-per-Windows-boot rumble prime;
- the PR #746 Windows-session-end command or deadline;
- HidHide/VIIPER authority and teardown, DirectInput polling / 4ms/8ms timers;
- Win+G hook timing/thread, Steam/BPM initial selection;
- P2-A LED/vibration post-attach ordering;
- XBOX game-identity detection algorithm, bounded limit 512, WinEvent processing, process generations, or XBOX game-profile schema;
- Controller page / Overlay / UI / protocol; settings persistence;
- generic observer framework, parallel startup orchestration state machine, extra synchronization, retries, or speculative multi-user support.

The remaining pre-first-attach Frontend/Overlay construction may have a smaller measurable cost (EX sample ~subsecond). **Do not move it in this PR without separate measured justification**. Moving the XBOX scan removes the clearest large optional serial wait. A future narrowly scoped follow-up may reassess remaining startup time after new logs.

## 7. PR size, local verification and acceptance

**One independent PR, preferably one focused production commit plus tests/docs.**

Expected modified files:

1. `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
2. `tests/SteamInputAddonforClaw.Tests/XboxGameSessionRuntimeHostTests.cs`
3. Other source-order test files only if they concretely depend on the old pre-controller XBOX await.
4. Short change to active `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` and, if necessary, the XBOX architecture's *current* startup subsection; do not rewrite historical work orders.

Run normal local `dotnet restore`, `dotnet build`, focused XBOX/session/cadence/lifecycle tests, full software test suite and `git diff --check`. Realistic code behavior regressions may block review; **no hardware-only verification requirement or physical timing SLA may block merge**.

Deliver an actual code change: no XBOX startup scan blocking `InitializeRuntimeAsync` or native Win+G hook / first virtual publisher. Preserve existing observer start-result semantics, reliable-idle flag, XBOX game/profile mapping, shutdown ownership and an accurate failed-start fallback. Confirm CI pass, then the reviewer applies the project's regular squash-merge policy.

## 8. User-owned post-merge log validation (informational only)

After merge the user may inspect an EX session and an A2VM Windows startup and compare these exact markers:

- `ControllerAdmission ... Disabled-boot controller admission ready`
- `Runtime ... Native message loop entering`
- `Wing.Guard ... Win+G hook installed`
- `ControllerOwnership ... Physical ownership acquisition completed`
- `ControllerPresentation ... InitialPresentationAttached PublisherStarted=True`
- `XboxSession ... Production XBOX game-session runtime started`
- `XboxSession ... Bounded production XBOX session reconcile completed`
- `ControllerCadence ... CadenceDecisionChanged` pending / ready
- the normal Device/Profile startup and LED/vibration logs.

Expected: the initial XBOX scan no longer sits on the path to first presentation, and first publisher precedes the observer's first scan. Later XBOX title recognition changes M1/M2, profile and cadence through the existing events. The user, not Codex, is responsible for actual hardware timing and functionality checks following merge.
