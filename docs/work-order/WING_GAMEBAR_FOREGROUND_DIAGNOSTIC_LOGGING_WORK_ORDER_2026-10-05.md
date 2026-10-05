# Work Order — WING / Xbox Game Bar Foreground Diagnostic Logging

## Status

Focused diagnostic-only work order for identifying why the physical MSI Claw WING / Gamebar Button can still surface Xbox Game Bar even though the current Full1902 WinGSuppressionGuard reports that native Win+G was suppressed.

Code-review baseline:

- repository: onehoon/SteamAddonforClaw
- branch: main
- commit: ab4dac64516e9c5a00a109c3e1fc8a0d3f070b61
- date: 2026-10-05

Before implementation, read and preserve the current authority contracts in:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md
- docs/work-order/FRONT_BUTTON_MAPPING_DIAGNOSTIC_LOGGING_WORK_ORDER.md

This PR must not change controller authority, presentation selection, WING/OEM1 mapping behavior, Game Bar suppression policy, Steam Input behavior, or the current Full1902 lifecycle.

## 1. Goal

Add enough DEBUG-only evidence to determine where Xbox Game Bar becomes foreground relative to the existing physical WING event sequence.

Current real-hardware evidence already shows this sequence:

    physical WING press
    → LWIN down observed by WinGSuppressionGuard
    → G down observed
    → SuppressionArmed=True
    → Suppressed=True
    → Win+G suppressed
    → modifier cleanup succeeds

    ~300 ms later
    → Event88Accepted
    → configured WING action dispatches

The existing log therefore already proves the observed low-level Win+G chord is being suppressed.

A mapping swap produced stronger evidence:

    WING    → Quick Settings Overlay
    CenterM → Steam Button

Observed behavior in the affected 007 First Light session:

    WING:
    → Xbox Game Bar appears
    → Addon Quick Settings Overlay also appears

    Center M:
    → Steam menu open sound is heard
    → Steam menu is not visible over the game
    → Xbox Game Bar does NOT appear

Therefore the current leading hypothesis is:

> Xbox Game Bar exposure is correlated with the physical WING path itself, not with the Addon's Steam-button pulse action.

This PR does not prove or fix that hypothesis. It adds the smallest diagnostic needed to classify the timing.

## 2. Important existing evidence

### 2.1 Native Win+G suppression is currently observable

WinGSuppressionGuard already logs Wing.Input keyboard events, SuppressionArmed=True, Suppressed=True, and Win+G suppressed.

The current implementation returns a non-zero hook result for the suppressed G-down.

Do not add another keyboard hook merely to reproduce evidence that already exists.

### 2.2 WING Event88 is a separate later path

WingEventGestureBridge.OnEvent(...) already logs Event88Accepted before gesture recognition and action dispatch.

Real hardware currently shows Event88 roughly 300 ms after the native Win+G chord.

### 2.3 Steam-button delivery is not the Game Bar trigger candidate for this diagnostic

Current evidence shows:

    Center M → SteamButton
    → Steam pulse is delivered
    → Steam menu sound occurs
    → Xbox Game Bar does not appear

Do not modify:

- SteamDeckSystemButtonOverlay
- Steam / Quick Access pulse timing
- FrontButtonActionExecutor
- WING/Center M mapping defaults
- Steam-domain selection

### 2.4 007 First Light AppID mismatch is context, not scope

The affected reproduction can have:

- non-Steam shortcut AppID: 3787532504
- runtime RunningAppID: 12658904

and Steam can lose the visible in-game Steam menu / normal shortcut session.

That AppID/session behavior is not part of this PR.

Do not add:

- AppID aliasing
- shortcut matching
- process ownership repair
- X360 fallback
- Steam overlay fixes

The 007 session is only a useful reproduction environment for the WING/Game Bar diagnostic.

## 3. Diagnostic design

### 3.1 Trigger only after accepted Event88

Add the diagnostic trigger at the existing accepted WING event seam:

src/SteamInputAddonforClaw/Wing/WingEventGestureBridge.cs

Current order is conceptually:

    AppLog.Debug("Wing.Event", "Event88Accepted", ("AuthorityEpoch", current.Epoch));
    _recognizer.OnPress(current.Epoch);

Keep that behavior unchanged.

Immediately after the existing Event88Accepted log, start one bounded DEBUG foreground probe.

Conceptually:

    Event88Accepted log
    → start WingGameBarDiagnosticProbe
    → continue recognizer.OnPress exactly as today

Exact naming is not mandated.

The probe must return immediately and must not block WMI/Event88 delivery.

Do not move the diagnostic into the low-level keyboard hook callback. The hook already gives the required Win+G suppressed timestamp and should remain as small as possible.

## 4. Bounded foreground probe

Add one small diagnostic helper, preferably under:

src/SteamInputAddonforClaw/GameBar/

Suggested name:

WingGameBarDiagnosticProbe.cs

This helper is diagnostic-only. It is not an authority, watcher, manager, or lifecycle owner.

Required behavior:

    Event88Accepted
    → if DEBUG logging is disabled: return immediately
    → queue a background diagnostic probe
    → capture foreground window immediately
    → observe for approximately 1 second
    → sample at a short fixed interval
    → log only foreground identity changes
    → emit one completion summary
    → exit

Recommended sampling:

- duration: approximately 1000 ms
- interval: approximately 25 ms

These values are diagnostic parameters, not product timing contracts.

A short bounded sample is sufficient to distinguish a user-visible Game Bar foreground transition without restoring the old permanent GameBarForegroundWatcher.

## 5. Foreground snapshot fields

For each distinct foreground HWND observed during the probe, log enough raw evidence to identify the surface.

Use best-effort Win32 inspection:

- GetForegroundWindow
- GetWindowThreadProcessId
- GetClassNameW
- GetWindowTextW
- Process.GetProcessById(...)

Recommended DEBUG category:

Wing.GameBarDiag

Recommended fields:

- ProbeId
- Trigger=Event88Accepted
- ElapsedMs
- Hwnd
- Pid
- ProcessName
- WindowClass
- WindowTitle

Example shape:

    [Wing.GameBarDiag] ForegroundSnapshot
    ProbeId=4
    Trigger=Event88Accepted
    ElapsedMs=0.3
    Hwnd=0x0000000000123456
    Pid=1234
    ProcessName=...
    WindowClass=...
    WindowTitle=...

When foreground identity changes:

    [Wing.GameBarDiag] ForegroundChanged
    ProbeId=4
    ElapsedMs=87.4
    Hwnd=...
    Pid=...
    ProcessName=...
    WindowClass=...
    WindowTitle=...

At completion:

    [Wing.GameBarDiag] ProbeCompleted
    ProbeId=4
    DurationMs=...
    ForegroundChangeCount=...
    FinalHwnd=...
    FinalPid=...
    FinalProcessName=...

The exact message names may differ, but one reproduction must be easy to reconstruct chronologically from the log.

## 6. Do not hard-code Game Bar identity

Do not make the diagnostic depend on assumptions such as:

- ProcessName == GameBar
- WindowTitle contains Xbox
- a specific HWND class name
- a specific package path

Log raw foreground identity instead.

The purpose of this PR is to discover which actual process/window becomes foreground on the tested Windows build.

Do not add a product IsGameBarWindow(...) policy based on this diagnostic.

## 7. Failure handling

All diagnostic inspection is best effort.

Expected failures include:

- foreground HWND becomes zero
- process exits between GetWindowThreadProcessId and Process.GetProcessById
- process metadata is inaccessible
- window title/class retrieval fails
- the process is a packaged/system process with restricted metadata

These must not affect WING action delivery.

Required policy:

    diagnostic failure
    → optionally log DEBUG diagnostic failure detail
    → continue normal Event88 recognition and action dispatch

Do not throw out of the probe into WingEventGestureBridge.

Do not convert diagnostic failures into warnings unless they indicate an actual product failure outside the diagnostic itself.

## 8. Keep the probe off the low-level keyboard hook

Do not add window/process inspection inside:

- WinGSuppressionGuard.HookCallback(...)
- WinGSuppressionGuard.ProcessKey(...)

The existing hook timestamps are already sufficient.

The new probe starts later at Event88Accepted.

That gives the required chronology without making the WH_KEYBOARD_LL callback heavier.

Do not add process enumeration, foreground polling, Process.GetProcessById, title/class queries, or async work inside the keyboard hook callback.

## 9. Overlapping WING presses

Keep this simple.

Each accepted Event88 may start its own bounded probe and receive a monotonically increasing ProbeId.

Do not add:

- a probe manager
- a persistent queue
- cancellation ownership
- debounce state
- an epoch/barrier
- a long-lived foreground subscription

If two short diagnostic probes overlap during intentionally rapid testing, their ProbeId values are sufficient to separate the log lines.

This is diagnostic code, not a product authority.

## 10. Explicitly prohibited legacy resurrection

The repository still contains historical references to the old Game Bar foreground presentation architecture.

Do not restore or reuse:

- GameBarForegroundWatcher
- GameBarForegroundPresentationDelivery
- AddonRoutingRuntime.HandleGameBarForegroundChangedAsync
- EnterXbox360PresentationAsync
- ExitXbox360PresentationAsync

Do not attach X360 when Game Bar becomes foreground.

Do not create a permanent SetWinEventHook foreground observer.

The old Game Bar presentation path is intentionally not production authority in Full1902.

The new code must be a short-lived DEBUG observation only.

## 11. No behavior changes

This PR must not change:

- whether WinGSuppressionGuard suppresses a key
- EnsureArmed() / IsArmed semantics
- WING route authority
- Event88 acceptance
- WING gesture recognition
- front-button domain selection
- front-button mapping resolution
- Overlay action execution
- Steam Button execution
- Quick Access execution
- Xbox360 / SteamDeck presentation choice
- Overlay capture ownership
- PID1901 / PID1902 handling
- HidHide
- DirectInput
- VIIPER ownership
- Steam RunningAppID handling
- Big Picture behavior

The only product-code effect is additional DEBUG evidence.

## 12. Tests

Keep tests focused on proving the diagnostic cannot change runtime behavior.

### 12.1 Event88 delivery remains unchanged

Existing WingEventGestureBridge tests must continue to prove:

    accepted Event88
    → recognizer receives the press exactly once
    → diagnostic side work does not replace semantic delivery

If a diagnostic seam is injectable for tests, prove:

    diagnostic callback throws
    → Event88 gesture/action path still continues

Do not create a large test abstraction solely for diagnostic code.

### 12.2 Foreground snapshot helper

If the helper is structured for deterministic unit testing, cover only the useful basics:

- same foreground HWND/PID repeated → no duplicate ForegroundChanged line
- changed HWND/PID → change is emitted
- process/window metadata failure → contained
- probe completes after the bounded interval

If deterministic testing would require a large fake Win32 framework, skip that complexity and keep the helper small.

### 12.3 Regression guards

Preserve existing tests around:

- WinGSuppressionGuard
- Full1902WinGSuppressionAuthorityTests
- MsiClawFrontButtonRuntimeTests
- front-button mapping/dispatch tests

No existing test should need semantic relaxation to make this PR pass.

## 13. Manual reproduction

Use the known affected 007 First Light state if convenient.

Preferred diagnostic mapping during reproduction:

    Steam domain:
    WING    = Quick Settings Overlay
    CenterM = Steam Button

This mapping cleanly separates the physical button from the executed action.

### Test A — Center M control

1. Enter the affected game state.
2. Ensure Xbox Game Bar is closed.
3. Press Center M once.
4. Confirm the Steam menu sound may occur.
5. Confirm Xbox Game Bar does not appear.
6. Confirm no Wing.GameBarDiag probe is started because there was no WING Event88.

### Test B — WING

1. Close Xbox Game Bar and Addon Overlay.
2. Press WING once.
3. Confirm the Addon Overlay appears according to the mapping.
4. If Xbox Game Bar also appears, capture the Runtime log.
5. Inspect this chronology:

    Wing.Input / Win+G suppressed
            ↓
    Wing.Event / Event88Accepted
            ↓
    Wing.GameBarDiag / immediate foreground snapshot
            ↓
    Wing.GameBarDiag / foreground change(s)
            ↓
    Wing.Action / QuickSettingsOverlay

The log should show whether the Game Bar surface is already foreground by the time Event88 is accepted or becomes foreground afterward.

### Test C — action independence

Optionally restore:

    WING = Steam Button

and repeat once.

The diagnostic should allow comparison without changing the probe design.

## 14. Interpretation rules

This PR gathers evidence. Do not encode these outcomes as product behavior yet.

### Outcome A

    Win+G suppressed
    → by Event88Accepted the Game Bar process/window is already foreground

Interpretation:

> Strong evidence that Game Bar activation originates from a physical WING/native path that occurs before Addon Event88 action dispatch.

### Outcome B

    Win+G suppressed
    → Event88Accepted
    → only afterward foreground changes to the Game Bar surface

Interpretation:

> The activation happens after Event88 timing and requires another investigation step; do not assume the Addon action caused it unless action-specific evidence supports that.

### Outcome C

    Game Bar is visibly shown
    but foreground probe never observes its surface

Interpretation:

> Foreground ownership is not sufficient evidence for this surface; capture the result and design the next targeted diagnostic separately. Do not expand this PR into UI Automation, ETW, package event tracing, or global hooks.

## 15. No overengineering

Do not add:

- a permanent foreground watcher
- a GameBarDiagnosticManager
- a new authority/state machine
- WING diagnostic persistence
- registry/GPO Game Bar modification
- UI Automation
- ETW sessions
- package activation hooks
- process-wide WMI process-start monitoring
- retry loops
- a Windows service/helper
- extra locks/epochs/barriers for theoretical timing races

The current issue is a concrete real-hardware behavior.

The purpose of this PR is only to obtain the missing timestamped foreground evidence needed for the next decision.

## 16. Validation

Run at minimum:

- dotnet build SteamInputAddonforClaw.slnx -c Debug
- dotnet build SteamInputAddonforClaw.slnx -c Release
- dotnet test SteamInputAddonforClaw.slnx -c Release
- git diff --check

Also review the final diff to confirm:

- no GameBarForegroundWatcher resurrection
- no presentation switching changes
- no Steam pulse changes
- no WinG suppression semantic changes
- no front-button mapping changes
- no AppID/session workaround
- no new product authority

## 17. Acceptance criteria

- [ ] An accepted WING Event88 starts a DEBUG-only bounded foreground diagnostic probe.
- [ ] The probe captures an immediate foreground snapshot.
- [ ] The probe observes foreground HWND/PID changes for approximately one second.
- [ ] Each change records HWND, PID, process name, window class, and window title on a best-effort basis.
- [ ] Probe lines carry a ProbeId and elapsed time.
- [ ] The existing Win+G suppressed log remains unchanged and provides the earlier timing anchor.
- [ ] Existing Event88Accepted, gesture, action, Overlay, and Steam-pulse logs remain intact.
- [ ] Diagnostic failure cannot block or alter WING action delivery.
- [ ] No extra work is added inside the low-level keyboard hook callback.
- [ ] Center M presses do not start the WING diagnostic.
- [ ] No permanent foreground watcher or legacy Game Bar presentation path is restored.
- [ ] No controller/presentation/AppID/Game Bar policy behavior changes are included.
- [ ] One real-hardware reproduction log is sufficient to determine whether the visible Game Bar foreground transition occurs before/by Event88 or after Event88.
