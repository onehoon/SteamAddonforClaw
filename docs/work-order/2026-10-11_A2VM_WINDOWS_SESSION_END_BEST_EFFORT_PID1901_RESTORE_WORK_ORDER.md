# Work Order — A2VM Best-Effort PID1901 Restore on Real Windows Session End

**Project:** SteamAddonforClaw — standalone Full1902  
**Date:** 2026-10-11  
**Implementation owner:** Local Codex  
**Implementation vehicle:** One focused PR  
**Status:** Proposed implementation; no production behavior changed by this document.

## 1. User Decision and Goal

**User decision:** Follow the simple, field-proven HHC-style **send XInput mode command when exiting** approach, but **only for Windows Start-menu shutdown or restart**. This Addon will not implement its own Power/Shut Down buttons. **Sleep, Hibernate, Resume and an ordinary Addon Runtime restart must not restore PID1901.**

Current A2VM 7/8 first-boot rumble arming may require:

```text
PID1902 → PID1901 → PID1902
```

The observed 0.1.355.0 A2VM 8 cold-start tests (`GoogleDrive/Addon/Log/AV2M/1011`) spent roughly **32 seconds from Runtime start to virtual controller attach** (five boot-priming sessions; ~21 seconds from BPM detection). Same-Windows-session Runtime restarts with no boot-prime were substantially faster. This POC moves the first mode transition **to the outgoing Windows session**, so a subsequent real Windows boot **might** begin in PID1901 and need only the existing single PID1901→PID1902 takeover.

**HHC reference behavior:** `Valkirie/HandheldCompanion/HandheldCompanion/Devices/MSI/ClawA1M.cs` `Close()` calls `SwitchMode(GamepadMode.XInput)` and releases its HID resources without waiting for PID1901 enumeration. `ClawA2VM` inherits this path. **HHC does not implement a dedicated verified Windows Start-menu shutdown hook**, so this PR cannot treat its normal `Close()` as evidence that every OS shutdown is covered. Its practical benchmark is **single, quick command write; no multi-second post-write PnP verification**.

## 2. Mandatory Authority Documents / Source

Read these **before implementation**, respecting the authority hierarchy and updating the affected text in the implementation PR:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` (especially §§ 9, 11, 12, 18 and 24)
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` (especially §§ 7–9, 18 and 19)
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
6. `docs/work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md`
7. `docs/work-order/2026-10-10_A2VM_BOOT_RUMBLE_SECOND_LEG_PARTIAL_PID1902_RECOVERY_WORK_ORDER.md`

Relevant existing production seams:

- `src/SteamInputAddonforClaw/Lifecycle/NativeTrayHostWindow.cs` — already-created hidden **top-level HWND** and its `WndProc`.
- `src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs` — existing subclass attached to the same HWND; preserve its normal tray behavior.
- `src/SteamInputAddonforClaw/Lifecycle/NativeMessageLoop.cs` — current process message pump; **do not build a second pump**.
- `src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs` — Windows session end must **not** be conflated with its app restart/uninstall/normal process exit.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` — owner of exactly one `_presentationOwnership` / `_physicalOwnership`, live runtime lifecycle and process-shutdown gate.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` — existing publisher stop, rumble STOP, detach/VIIPER retirement owner.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs` — serialized physical owner, strong PID1902 identity, existing DirectInput stop/cleanup primitives.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`, `MsiClawNativeStateManager.cs` and `WindowsMsiClawModeWriter.cs` — existing exact source command HID resolver and **64-byte mode-write** implementation.

Microsoft documentation:

- [WM_QUERYENDSESSION](https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-queryendsession)
- [WM_ENDSESSION](https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-endsession)
- [Shutting Down](https://learn.microsoft.com/en-us/windows/win32/shutdown/shutting-down)

**Explicit policy change:** Prior Full1902 docs say *never intentionally restore PID1901 during normal Windows shutdown*. This work order introduces **one tightly scoped A2VM pre-power-off physical preparation exception**, **not a Center M authority release**. Amend only those contradicted paragraphs in the implementation PR. Center M remains Disabled and the persistent HidHide baseline remains owned.

## 3. Strict Supported Scope

Trigger **only** for a real OS session-end notification corresponding to **Windows shutdown/restart**, including from the Windows Start menu:

- `WM_QUERYENDSESSION`: promptly allow shutdown; **do not** touch PID, publisher, DirectInput, HidHide, Center M or VIIPER here.
- `WM_ENDSESSION` with `wParam == FALSE`: shutdown was cancelled; **do nothing** and leave the running controller untouched.
- `WM_ENDSESSION` with `wParam == TRUE`: admit a best-effort session-end operation **only** when the flags denote OS shutdown/restart, **not** a user logoff (`ENDSESSION_LOGOFF`) or application replacement/restart-manager close (`ENDSESSION_CLOSEAPP`). Treat flags as a **bit mask**, not an equality comparison. If a forced/critical notification leaves insufficient execution time, skip without blocking shutdown.
- **A2VM 7 and A2VM 8 only**, and only if current Center M startup authority is exactly Disabled with one healthy, already-owned, strongly identified PID1902 physical controller.
- If no owned PID1902 session / unsupported model / malformed or ambiguous HID / incorrect authority / recovery or teardown already in progress, **skip** rather than attempt an invasive re-acquisition.

**Absolutely excluded**: Sleep, Hibernate, Modern Standby, Resume, lock/unlock, user logoff, Runtime tray Restart, Velopack update/relaunch, application exit, crash, uninstall, Center M Enable-and-Restart stock release, explicit/manual Restore Vibration, EX, CG3EM. Keep the existing handling of these operations unchanged.

No new Addon shutdown UI, Power command, Windows service, task, background helper, external supervisor, separate authority state, persistent “restored PID” marker, PnP watcher or retry state machine.

## 4. Required Minimal Behavior

### 4.1 Windows notification delivery

Use the **existing `NativeTrayHostWindow` top-level HWND** and its current window procedure, with the smallest callback connection into the current Runtime/`AddonProcessHost`.

Do **not** use `AppDomain.ProcessExit` or generic `DisposeAsync` as the trigger; they also run on Addon-only restart, update and unrelated termination. Do not create a UI window, message-only window, additional message pump or standalone helper just to receive the shutdown notification.

Recognize the constants:

```csharp
private const uint WM_QUERYENDSESSION = 0x0011;
private const uint WM_ENDSESSION = 0x0016;
private const uint ENDSESSION_CLOSEAPP = 0x00000001;
private const uint ENDSESSION_LOGOFF = 0x80000000;
```

Example eligibility predicate only (this must not be mistaken for a complete session-end operation):

```csharp
internal static bool IsRealWindowsPowerSessionEnd(uint message, nint wParam, nint lParam)
{
    if (message != WM_ENDSESSION || wParam == 0)
        return false;

    uint flags = unchecked((uint)lParam.ToInt64());
    return (flags & (ENDSESSION_LOGOFF | ENDSESSION_CLOSEAPP)) == 0;
}
```

The normal Windows message result must not be used to veto shutdown. Preserve the existing `SystemTrayIcon` subclass and icon callbacks. A window callback that cannot safely complete the bounded operation must just return.

### 4.2 A single bounded, safe handoff

On eligible notification, from the **existing** `AddonProcessHost` and **existing** presentation/physical owners:

1. Do a cheap admission check: A2VM 7/8, Center M exactly Disabled, live owned PID1902, Strong identity, exact current primary PID1902 target. Skip if setup/ownership is incomplete, recovery is actively in progress, teardown is unsafe, or the desired shutdown deadline cannot be respected.
2. Block new controller publication/recovery requests through the **existing** process/owner lifecycle gates before retirement; do not create a second flag-based authority layer.
3. Stop/join the **current** virtual publisher, disarm physical rumble callback and STOP physical rumble, neutralize and detach the current typed virtual controller, and retire VIIPER **through the existing presentation owner**. Do not issue a physical mode command unless output retirement is actually proven. Preserve safety when native VIIPER teardown fails.
4. Stop and prove cleanup of the process-owned PID1902 DirectInput session **through the existing physical owner and serialization gate**. If still running or cleanup is unproven, **do not** issue the mode command.
5. Resolve **one correct, still-present, strongly verified PID1902 command HID** (`0xFFF0/0x0040`; respect the existing A2VM endpoint constraints and physical-root identity) using existing enumerator/resolver. Do not use an arbitrary same-VID/PID HID, stale handle, weak identity or a non-owner writer.
6. Send **exactly one** existing `SwitchMode(XInput)`/PID1901 vendor HID command. Record whether the command write actually succeeded; do **not** wait for PID1901 device arrival, full transition completion, final snapshot or another 5-second verification window.
7. Return promptly so Windows can end the session. Never stop, postpone, cancel or veto the user-requested OS shutdown on a failure or timeout.

Reuse the existing writer implementation. Do **not** call `ReleaseForCenterMEnableAsync()` in its present form: it sets `_releasedForEnable`, performs strict post-write PnP verification and belongs to stock authority release. Similarly **do not call `MsiClawModeController.SwitchModeAsync()` in its present form** for this path: its verified transition includes a post-write 5-second window. Expose the narrow **write-only** operation through the existing mode-controller/physical-owner wiring if needed; avoid a duplicate HID command implementation or new globally callable mode-write API.

The goal is an HHC-like **fast single write**, **not** bypassing the physically safe publisher/DirectInput retirement contract.

### 4.3 Bounded execution, not fire-and-forget

A Windows shutdown notification does **not** guarantee arbitrary async work can finish. The **entire** attempt (from safe retirement through the one write) must have a deliberately short, measured budget (start with **~1–2 seconds maximum**, tune from existing stop/write characteristics). Do not wait for PID1901 PnP arrival at all.

- Never block `WM_QUERYENDSESSION`. The bounded operation belongs to `WM_ENDSESSION(TRUE)`.
- Do **not** kick off an unobserved `Task.Run` and immediately return from `WndProc`: Windows may kill the process before the HID write.
- Do **not** use `Task.WaitAsync(timeout)` over an uncancellable task that could **continue and issue a late mode command** after the window handler returns. Cooperatively check the short deadline/cancellation **before the physical write**, including if an earlier stop/retirement returned late.
- No additional retry after a failed write, no blocking user notification, and no multi-second PID1901 verification. If the short budget expires before safe retirement/write, **skip the write and let Windows shut down**.
- Do not hold the Win32 message thread longer than the bounded budget or create a deadlock by capturing that thread's synchronization context. Codex should inspect the current async continuations and select the simplest safe way to wait briefly for its **one** command result.
- Normal non-session-end `RuntimeProcessApplication` cleanup stays as it is.

**Important realistic failure rule:** unsafe/failed publisher neutralization, native detach/teardown, DirectInput stop, command HID identification or HID write must leave **no additional late mutation**. Windows shutdown itself still proceeds; the next boot uses the existing actual-PID reconcile. Do **not** clear HidHide or enable Center M to make this shutdown-only path easier.

### 4.4 No optimistic state authority

This is a **best-effort physical preparation**, not a controller authority switch.

- Center M Disabled and persistent HidHide (applications, hidden targets, global state) must **not** be cleared, normalized to a stock baseline or persisted differently for this feature.
- No persistent shutdown success flag and **no change** to `BootSession.TryClaimA2vmRumbleAttempt()`: next boot must observe actual PID. PID1901 → one existing PID1902 transition; PID1902 → existing A2VM boot re-arm fallback.
- Do not claim PID1901 was restored merely because the HID write succeeded; report **`CommandWritten`**, not **`Pid1901Verified`**.
- The normal controlled Runtime restart and all Sleep/Hibernate/Resume/owned-recovery paths keep PID1902 and must not run this logic.

## 5. Focused Automated Tests for Local Codex

Use existing test seams where possible; do not require real device tests before merge.

1. **Windows message classification:** `WM_QUERYENDSESSION`, `WM_ENDSESSION(FALSE)`, `WM_ENDSESSION(TRUE)`; bit-mask `ENDSESSION_LOGOFF`, `ENDSESSION_CLOSEAPP`, combined/critical flags. Windows shutdown/restart accepted only once.
2. **Model/authority scope:** A2VM 7/8 + Center M Disabled + strongly-owned PID1902 eligible; EX, CG3EM, Center M Enabled, missing/weak/ambiguous controller, during recovery and disposed owner skip with **zero** PID writes.
3. **Order:** one owned publisher safe retirement → one proven DirectInput stop → **one** XInput HID write → return without PID1901 PnP wait.
4. **Fail-close:** unsuccessful VIIPER detach/teardown or unproven DirectInput cleanup → **zero** XInput mode writes; shutdown message returns without waiting for repair.
5. **Write failure/late timeout:** no retry, no late writer after the window handler has finished; Windows is never vetoed.
6. **Non-Windows-shutdown lifecycle:** simulated Sleep/Hibernate/Resume, tray Restart, update/relaunch, normal process exit/uninstall do not issue an XInput write and do not change current owner semantics.
7. **Native message pump/tray regression:** existing tray window handle, `SystemTrayIcon` subclass, message-loop exit path still function.
8. **Boot fallback regression:** existing A2VM 1901→1902 and 1902→1901→1902 initial paths still work; no alteration of boot-marker or PR #741/#742 recovery.

Run focused solution build/tests and relevant existing software suites. Include practical diagnostics:

```text
Event=A2vmWindowsSessionEndPid1901Prepare
Trigger=WindowsEndSession
Model=msi.claw.a2vm.8
Outcome=CommandWritten | Skipped | WriteFailed
Reason=...
ElapsedMs=...
PnP1901Verified=False
```

Single result per qualifying notification, plus one brief preflight/retirement failure reason as needed; avoid verbose per-poll logs.

## 6. User-Owned Checks After Merge — NOT PR Blockers

After software validation and merge, the **user** may perform physical A2VM checks: Windows Start menu **Restart**, Start menu **Shut down** followed by cold boot, startup actual PID and time-to-first SteamDeck input, actual rumble, shutdown with another app prompting cancellation, normal Addon tray Restart, Sleep/Resume/Hibernate (must retain ordinary paths), and PID1902 fallback after an interrupted/failed optional write. Record actual PID and elapsed time; don't infer success from the command-write log.

**Do not require these real-device checks from local Codex, CI or PR review.**

## 7. Acceptance / Scope Boundary

- One focused PR implements **Windows system shutdown/restart only**, A2VM 7/8 only.
- One HHC-like **single** PID1901 command write if and only if output/input teardown was proven safe, **without** waiting for PID1901 PnP.
- No effect on Sleep/Hibernate/Resume, update/tray Runtime restart, stock authority release, uninstall, EX or CG3EM.
- Persistent Center M Disabled/HidHide authority remains intact; actual-PID next-boot reconciliation is unchanged.
- Any incomplete/unsafe step causes **skip** without blocking Windows shutdown or risking a late PID command.
- Update the contradictory **shutdown-only** paragraphs in the Full1902 README, reboot-bound design and overall Full1902 architecture **within the implementation PR**, retaining the strict ordinary lifecycle rules.
- Avoid any new manager/state machine/service/watcher/secondary authority and avoid timing-only race machinery.
