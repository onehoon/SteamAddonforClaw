# Work Order — Tray Restart Semantics + Overlay Menu Cleanup

> Date: 2026-09-04  
> Status: Ready for implementation  
> Baseline: `main` after PR #489 (`bc9272933176b3f1a4d8b17ef58b5a62a5243a75`)

## 1. Goal

Clean up the Runtime system-tray menu now that the Addon Quick Settings Overlay has a real physical front-button mapping path, and make ordinary Addon restart available as an explicit lifecycle action without weakening Full1902 controller-authority safety.

Target tray menu:

```text
Open
────────────
Restart Addon
```

Remove:

```text
Overlay POC: Toggle
Exit
```

## 2. Read Before Implementation

Read the current authority documents before editing lifecycle code:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md
docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md
docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md
docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md
docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_REVIEW_ADDENDUM.md
```

Later current-policy documents and this work order win where older work orders describe superseded user-facing tray behavior.

## 3. Current Source State

### 3.1 Current tray menu

`src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs` currently builds:

```text
Open
Overlay POC: Toggle
────────────
Restart
Exit
```

The constructor currently receives:

```csharp
Action open,
Action restart,
Action exit,
Func<UserTerminationDecision> terminationDecision,
Action? overlayToggle = null
```

and stores `_exit` / `_overlayToggle`.

### 3.2 Current tray wiring

`AddonProcessHost.TryInitializeTray(...)` currently creates `SystemTrayIcon` with:

```csharp
new SystemTrayIcon(
    _trayHostWindow.Handle,
    () => RequestFrontendOpen(FrontendOpenReason.Tray),
    restart,
    exit,
    EvaluateUserTermination,
    RequestOverlayToggle)
```

### 3.3 Current ordinary restart implementation

`RuntimeProcessApplication.RequestRestart()` already implements a replacement-process handoff:

```text
EvaluateUserTermination
→ launch same executable with --restart
→ old Runtime begins normal shutdown
→ replacement waits for the old single-instance lock to release
→ replacement starts through normal Runtime startup
```

`Program.Main()` recognizes `--restart`; while the old single-instance lock still exists, the replacement retries for up to 10 seconds rather than activating the old process and quitting.

### 3.4 Current Full1902 ordinary shutdown semantics

Normal Runtime teardown intentionally retires process-owned live resources while preserving reboot-bound durable Addon authority:

```text
front-button runtime retire
→ virtual presentation / VIIPER retire
→ physical DirectInput session release
→ PID1902 remains desired
→ persistent HidHide remains
```

Ordinary Runtime teardown must not perform PID1902 → PID1901 restoration.

### 3.5 Current problem

`RequestRestart()` and `RequestExit()` both call `AddonProcessHost.EvaluateUserTermination()`.

That composition applies `ControllerAuthorityMandatory` whenever MSI Center M is exactly Disabled, so the tray currently grays both Restart and Exit while Addon controller authority is active.

This is correct for a permanent user Exit, but overly broad for a controlled Runtime replacement.

## 4. Product Decision

### 4.1 Exit is no longer an ordinary user action

Do not expose a tray Exit command.

Under Full1902 Addon authority, a persistent ordinary Exit would leave the selected controller authority without its mandatory Runtime.

The product already has separate explicit lifecycle paths for controller-authority release and uninstall:

```text
Enable MSI Center M and Restart
→ release Addon controller authority through the existing reboot-bound transition

Uninstall
→ existing stock-safe uninstall preparation
→ Runtime shutdown only after stock authority is independently proven
```

Do not route uninstall through ordinary tray shutdown.

### 4.2 Restart Addon is a Runtime replacement, not controller-authority release

Expose:

```text
Restart Addon
```

A successful ordinary restart must preserve the selected controller-authority intent.

If Center M is Disabled:

```text
PID1902 / persistent HidHide authority remains durable
old Runtime retires live process-owned resources
new Runtime starts
normal Full1902 startup/admission/reconciliation runs again
```

Do not convert Restart Addon into:

```text
PID1902 → PID1901
Center M startup roots enable
HidHide release
Windows reboot
```

Those belong to the existing explicit `Enable MSI Center M and Restart` authority transition only.

## 5. Remove Tray Overlay Toggle

PR #489 made `Quick Settings Overlay` a real selectable front-button action and promoted the production toggle seam to:

```text
AddonProcessHost.RequestOverlayToggle()
→ CoordinateOverlayToggleAsync()
```

Therefore the tray POC harness is obsolete.

Remove from `SystemTrayIcon`:

```text
_overlayToggle field
overlayToggle constructor parameter
"Overlay POC: Toggle" menu item
command id used only for Overlay
_overlayToggle() dispatch
related tray-only Overlay log messages
```

Remove the corresponding `RequestOverlayToggle` argument from `AddonProcessHost.TryInitializeTray(...)` wiring.

Do **not** delete or weaken `AddonProcessHost.RequestOverlayToggle()` itself. It is now the production front-button Overlay action seam used by PR #489.

Do not change Overlay process/capture lifecycle in this PR.

## 6. Remove Ordinary Tray Exit

Remove from `SystemTrayIcon`:

```text
_exit field
exit constructor parameter
"Exit" menu item
Exit command id / dispatch
```

Remove the ordinary `exit` delegate from `AddonProcessHost.TryInitializeTray(...)`.

`RuntimeProcessApplication.RequestExit()` exists for the ordinary tray Exit path. After removing all production callers, delete it if no other current product path uses it.

Keep:

```text
RequestExitForUninstall()
```

unchanged in behavior. It is a separate explicit lifecycle path and must still perform stock-safe uninstall preparation before Runtime shutdown.

Search before deletion and update any legitimate non-tray caller rather than assuming there is none.

## 7. Separate Restart Safety From Permanent Termination Policy

Do not let `ControllerAuthorityMandatory` block a controlled `Restart Addon` handoff.

However, Restart must still be blocked when there is a real live transition/shutdown hazard.

### 7.1 Required restart safety

Restart should be refused while at least these existing concrete conditions apply:

```text
controller-authority startup / reboot-bound transition is still committing
Runtime shutdown is already in progress
```

Reuse existing facts/gates. In particular, do not create a new restart state machine merely for this feature.

The current `AddonProcessHost` already has the concrete deferred Disabled-mode startup fact:

```csharp
_disabledControllerStartupPending
```

and the lower-level Runtime safety evaluation already reports `RuntimeShuttingDown`.

### 7.2 Recommended shape

Introduce a narrow restart-specific evaluation method, for example:

```csharp
internal UserTerminationDecision EvaluateUserRestart()
{
    if (Volatile.Read(ref _disabledControllerStartupPending) != 0)
    {
        return new(
            false,
            UserTerminationBlockReason.ControllerAuthorityTransition);
    }

    return _runtimeHost?.EvaluateUserTermination()
        ?? new(true, UserTerminationBlockReason.None);
}
```

Exact naming may differ, but semantics must be:

```text
Restart safety
= real process/authority transition safety only
≠ permanent Runtime mandatory-lifetime policy
```

`ControllerAuthorityMandatory` is a rule against leaving the Addon-owned mode without its Runtime; it is not a reason to prohibit replacing that Runtime with another instance.

Do not add:

```text
RestartManager
RuntimeHandoffManager
restart epoch
second controller authority
new persisted restart state
```

## 8. Keep the Existing Replacement-Process Restart Mechanism

Do not redesign the current `--restart` handoff unless code inspection finds a concrete bug.

Keep the current high-level sequence:

```text
1. evaluate restart safety
2. start replacement executable with --restart
3. keep the original launch arguments except duplicate --restart
4. begin old Runtime shutdown
5. replacement waits for the single-instance gate to release
6. replacement enters normal startup/reconciliation
```

This is intentionally different from launching a normal second instance, which would only activate the existing Runtime.

Preserve background/manual launch intent:

```text
--background --restart
→ replacement remains background

manual launch + --restart
→ replacement retains the existing manual/frontend launch behavior
```

Do not force-open the Main UI as part of Restart Addon.

## 9. Update Semantics — Restart Addon Is Not an Explicit "Install Update" Command

Do not add a second update checker to the tray restart path.

The replacement process enters the same ordinary Addon startup path, so any existing startup-time update behavior should continue to occur exactly as it already does.

This PR must not duplicate Velopack download/apply logic inside `RequestRestart()`.

If the current update subsystem has already downloaded/staged an update and its existing policy applies that update on Runtime exit/restart, preserve that existing behavior; do not create a competing tray-specific mechanism.

Conversely, `Restart Addon` should not promise that selecting it always fetches or installs a newly available release unless the existing startup/update policy already guarantees that.

No new tray labels such as:

```text
Restart and Update
Check for Updates
Install Update
```

in this PR.

## 10. Tray Menu Construction

Target menu construction:

```csharp
AppendMenuW(menu, MF_STRING, OpenCommand, "Open");
AppendMenuW(menu, MF_SEPARATOR, 0, null);
AppendMenuW(menu, restartFlags, RestartCommand, "Restart Addon");
```

Use stable named command constants rather than retaining misleading numeric ids tied to deleted menu items if a tiny local cleanup improves readability.

Do not add a new tray-menu abstraction or command framework.

### 10.1 Restart enabled/disabled state

The Restart item should reflect the restart-specific decision, not the old ordinary-termination composition.

If blocked, keep the existing standard Windows gray menu behavior.

No custom dialog is required solely to explain a temporarily disabled Restart item.

## 11. Cleanup of Obsolete Termination Composition

After removing ordinary Exit and separating restart safety, inspect whether these are still used by any current production path:

```text
MandatoryControllerRuntimePolicy
UserTerminationComposition
UserTerminationBlockReason.ControllerAuthorityMandatory
SystemTrayIcon.TerminationMenuFlags(...)
```

Do not delete them blindly.

If `ControllerAuthorityMandatory` / the composition are now dead outside obsolete tray behavior/tests, remove the dead code and update comments/tests.

If another real current path still relies on them, retain the minimal live portion.

The goal is one clear lifecycle policy, not keeping historical abstractions solely because they existed in PR2.5.

## 12. Tests

Add/update focused tests for at least:

### 12.1 Tray layout

Assert the user-facing menu contract no longer contains:

```text
Overlay POC: Toggle
Exit
```

and contains:

```text
Open
Restart Addon
```

### 12.2 Restart under Addon controller authority

Verify that exact Center M Disabled / mandatory controller Runtime status by itself does **not** disable Restart Addon.

### 12.3 Real transition still blocks restart

Verify the current controller-authority transition pending condition blocks Restart Addon.

### 12.4 Runtime shutdown still blocks restart

Verify lower-level `RuntimeShuttingDown` remains blocking.

### 12.5 Existing replacement arguments

Keep or add coverage that:

```text
--restart is appended once
--background is preserved
```

and the replacement instance follows the existing single-instance wait behavior.

Do not add pathological instruction-level restart race tests unless they model a realistic product lifecycle failure.

### 12.6 Uninstall remains independent

Keep tests showing uninstall shutdown uses `RequestExitForUninstall()` / stock-safe preparation and is not coupled to tray Restart.

### 12.7 Overlay production seam remains

Add a source/contract check if useful to ensure deleting the tray Overlay callback does not delete `RequestOverlayToggle` or disconnect PR #489 front-button Overlay dispatch.

## 13. Likely Files

Expected primary files include:

```text
src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs
src/SteamInputAddonforClaw/Lifecycle/UserTerminationGuard.cs   [only if dead-policy cleanup is justified]

tests/SteamInputAddonforClaw.Tests/UserTerminationGuardTests.cs
tests/SteamInputAddonforClaw.Tests/ApplicationLifecyclePolicyTests.cs
[existing/new tray lifecycle tests as appropriate]
```

Search for all constructor/call sites and obsolete tray text before finalizing.

## 14. Non-Goals

Do not change:

```text
Full1902 PID1901/PID1902 authority model
HidHide baseline policy
Center M Enable/Disable-and-Restart workflow
VIIPER ownership design
Steam/BPM detection
front-button mapping model from PR #489
Overlay capture/navigation/visible-surface design
Velopack update policy
uninstall stock-safe policy
```

Do not add generalized lifecycle/state abstractions for theoretical races.

## 15. Acceptance Criteria

1. Tray shows exactly the intended user commands: `Open`, separator, `Restart Addon`.
2. `Overlay POC: Toggle` is removed from tray UI and tray wiring.
3. Ordinary tray `Exit` is removed.
4. Uninstall retains its dedicated stock-safe Runtime shutdown path.
5. `Restart Addon` is permitted while Center M is exactly Disabled when no real transition/shutdown hazard exists.
6. Restart remains blocked during the existing controller-authority transition-pending window.
7. Restart remains blocked once Runtime shutdown is already underway.
8. Existing `--restart` replacement-process/single-instance handoff is preserved.
9. Restart does not intentionally restore PID1901, release persistent HidHide, or enable Center M roots.
10. Old Runtime teardown preserves current Full1902 ordering and durable PID1902/HidHide semantics.
11. Replacement Runtime enters ordinary Full1902 startup/admission/reconciliation.
12. PR #489 front-button `Quick Settings Overlay` action still reaches `AddonProcessHost.RequestOverlayToggle()`.
13. No tray-specific update subsystem or duplicate Velopack logic is added.
14. Dead ordinary-exit / historical termination code is removed only where confirmed unused.
15. Debug build clean.
16. Release build clean.
17. Full test suite passes.

## 16. One-Sentence Design Rule

> **The tray may restart the mandatory Addon Runtime as a controlled replacement, but it may not permanently exit that Runtime or keep a redundant Overlay POC launcher now that Overlay is a real front-button action.**
