# Work Order — Full1902 Lifecycle Log Hygiene After 1005/07 Hardware Validation

> **Status:** Ready for implementation  
> **Date:** 2026-10-05  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Target branch:** `main`  
> **Reviewed main baseline:** `671e6088191e87e55028409f586791d3963e031a`  
> **Scope:** Logging classification/message cleanup only for already-correct Full1902 lifecycle behavior

---

## 0. Purpose

Clean up three misleading warning/error-looking log patterns confirmed by the real hardware validation set:

~~~text
Google Drive / Addon / Log / 1005 / 07
~~~

The validated session covered:

- Velopack steady-state update from 0.1.331.0 to 0.1.332.0;
- controlled Runtime restart;
- Full1902 PID1902/HidHide/VIIPER continuity;
- Sleep -> Resume;
- resume-time LED/vibration/TDP reapply;
- elevated 007 First Light WING / Win+G suppression;
- SafeUninstall;
- PID1902 -> PID1901 stock restoration;
- HidHide release;
- Center M restore;
- startup-task removal;
- usbip-win2 / HidHide owned prerequisite removal;
- Velopack uninstall handoff.

The hardware result is healthy.

This work order does **not** fix a functional lifecycle defect.

Its purpose is to make the log severity and event wording match the behavior that actually occurred so future log reviews do not repeatedly classify expected fail-close/teardown behavior as a production failure.

The three cleanup targets are:

~~~text
1. Full1902 Resume:
   generic Power.Recovery RemainPassive is logged as WARN even though
   the owner-specific Full1902 resume path already succeeded.

2. SafeUninstall / Center M enable:
   Device Arrival can request owned-input recovery while the physical owner
   is intentionally releasing authority. The owner correctly rejects the
   request as ReleasedForCenterMEnable, but logs it as recovery "failed".

3. Main UI during Runtime update/restart:
   the Runtime pipe closes as expected, App already observes the disconnect
   and shuts down, but in-flight UI refresh/diagnostic requests emit repeated
   WARN stack traces for FrontendTransportException.
~~~

No controller authority, recovery policy, update behavior, uninstall behavior, or UI transport behavior may change.

---

## 1. Read before implementation

Read and preserve the active contracts in:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md

docs/work-order/FULL1902_ELEVATED_RUNTIME_WORK_ORDER_2026-10-05.md
docs/work-order/FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_WORK_ORDER.md
docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md
~~~

Relevant current production files:

~~~text
src/SteamInputAddonforClaw/Power/PowerTransitionCoordinator.cs

src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.UI/App.xaml.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs

src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs

tests/SteamInputAddonforClaw.Tests/PowerTransitionTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
~~~

Product scope remains:

~~~text
one Windows user
one interactive session
Fast User Switching / RDP / multi-session unsupported
~~~

CTW integration is not part of the product or this work.

---

# 2. Non-goals

This PR must **not**:

- change Full1902 controller authority;
- change PID1901 / PID1902 transition behavior;
- change HidHide baseline/release behavior;
- change VIIPER attach/detach/teardown;
- change DirectInput recovery semantics;
- change Device Arrival scheduling/coalescing;
- change Sleep/Hibernate/Resume ordering;
- enable generic stock recovery for Center M Disabled;
- change `RecoverySafetyState`, `PowerMutationGate`, or recovery admission;
- add a new recovery state, epoch, gate, lock, manager, queue, or retry;
- suppress real recovery failures;
- change SafeUninstall ordering;
- change Velopack update/restart behavior;
- add Main UI reconnect/retry behavior;
- add a new frontend transport state authority;
- add an `IsConnected` contract solely for logging;
- classify protocol corruption/version mismatch as an expected disconnect;
- broadly convert warnings to debug without a concrete known-normal lifecycle reason.

This is a log-semantics PR.

---

# 3. Hardware evidence — 1005/07

## 3.1 Full1902 Sleep / Resume succeeded before the misleading warning

Runtime `P14828` recorded:

~~~text
22:47:57 Suspend
→ PresentationSuspendPauseStarted
→ ProductionRumbleCallbackDisarmed
→ PresentationSuspendPausedNeutral
→ Full1902 suspend participant quiesced
   Outcome=Paused
   Safe=True
→ Suspend quiesce completed
   Outcome=Succeeded
   GateState=Closed
   FinalPowerState=Suspended
~~~

On Resume:

~~~text
22:49:18 ResumeSuspend
→ PresentationReconcileRequested Trigger=PowerResume
→ PresentationResumeRequested
→ ProductionRumbleCallbackArmed
→ PresentationResumeSamePublisher
→ Runtime presentation suspend-release completed
   Outcome=SamePublisherResumed
~~~

Only after the owner-specific Full1902 resume callback completed, the generic coordinator emitted:

~~~text
[WARN] [Power.Recovery]
Resume recovery is disabled because this process did not establish a safe startup boundary.
Action=RemainPassive
~~~

This warning is misleading for the supported Center M Disabled / Full1902 owner path.

The current architecture intentionally does:

~~~text
PowerResumeObserved
→ Full1902 owner-specific presentation/device reconcile

generic stock recovery
→ remains disabled / passive
~~~

The behavior is correct.

## 3.2 Resume remained healthy after the warning

The same session then successfully performed:

~~~text
Controller LED reapply
Controller vibration reapply
TDP Resume reconcile
Shift 0xC1 -> 0xC6 rewrite
PL1/PL2 writes
Global TDP apply Succeeded=True
~~~

The later elevated 007 First Light WING test also succeeded after Resume.

Therefore the generic `RemainPassive` line must not be presented as a product WARN when `recoveryEnabled == false` is the already-selected startup policy.

## 3.3 SafeUninstall authority release succeeded

During uninstall, the running Full1902 owner performed:

~~~text
Presentation release
→ VIIPER teardown succeeded

DirectInput unacquire
→ Success=True

PID1902 -> PID1901
→ NativeModeTransitionSucceeded
→ OldPidDisappeared=True

Physical ownership released to XInput
→ Succeeded=True

HidHide release
→ Active=False
→ HiddenTargetCount=0

Center M enable
→ Outcome=Succeeded
→ FinalState=Enabled

startup task removal
→ ReadbackVerified=True

UninstallStockPrepareCompleted
→ Outcome=Success
~~~

While the PID transition generated ordinary PnP arrival notifications, the existing recovery scheduler attempted:

~~~text
Trigger=DeviceArrival
Trigger=DeferredDeviceArrival
~~~

The physical owner correctly rejected both because release had already won the owner gate:

~~~text
Reason=ReleasedForCenterMEnable
ModeWriteIssued=False
~~~

That is the PR10-design expected sequence:

~~~text
release gets owner gate first
→ _releasedForEnable = true
→ later Device Arrival recovery enters
→ ReleasedForCenterMEnable
→ no forward ownership mutation
~~~

It is not a recovery failure requiring WARN severity.

## 3.4 Main UI transport warnings occurred only after the expected Runtime disconnect

During the Velopack apply/restart boundary, the Main UI first logged:

~~~text
[Frontend] Runtime disconnect observed.
~~~

The Runtime was intentionally exiting for update.

In-flight refreshes then produced repeated warning stacks such as:

~~~text
[SteamFSE] Main UI SteamFSE state refresh failed.
FrontendTransportException: Pipe connection closed.

[Status] System status refresh failed.
Reason=SnapshotCaptureFailed
FrontendTransportException: Pipe connection closed.

[ClawHUD.UI] Main UI ClawHUD state refresh failed.
FrontendTransportException: Pipe connection closed.

[Update] Main UI update state refresh failed.
FrontendTransportException: Pipe connection closed.

[XboxSessionDiagnostic]
Main UI diagnostic stop request failed.
FrontendTransportException: Client is not connected.
~~~

The next Runtime/UI lifetime was healthy.

These are redundant secondary effects of a transport shutdown already represented by the top-level Runtime disconnect event.

---

# 4. Change A — Reclassify disabled generic Resume recovery

## 4.1 Current code

`PowerTransitionCoordinator.HandleAsync(...)` currently contains:

~~~csharp
if (!_recoveryEnabled)
{
    State = PowerTransitionState.Unsafe;
    _recovery.Set(RecoverySafety.Unsafe);
    AppLog.Warn(
        "Power.Recovery",
        "Resume recovery is disabled because this process did not establish a safe startup boundary.",
        null,
        ("Action", "RemainPassive"));
    return;
}
~~~

The state transitions are intentional and must remain unchanged.

## 4.2 Required change

Change only the log classification/message.

Recommended shape:

~~~csharp
if (!_recoveryEnabled)
{
    State = PowerTransitionState.Unsafe;
    _recovery.Set(RecoverySafety.Unsafe);

    AppLog.Info(
        "Power.Recovery",
        "Generic stock resume recovery skipped because startup did not enable stock recovery.",
        ("Event", "GenericResumeRecoverySkipped"),
        ("Action", "RemainPassive"));

    return;
}
~~~

Exact wording may be adjusted, but the log must communicate:

- this is an intentional passive branch;
- generic stock recovery is skipped;
- it is not claiming that the Full1902 owner-specific Resume path failed.

Do **not**:

- change `State`;
- change `RecoverySafety`;
- open the power gate;
- set `recoveryEnabled=true`;
- call `_establishBaseline`;
- move `_resumeObserved`;
- add another Resume callback.

The existing ordering must remain:

~~~text
accepted Resume
→ PowerResumeObserved / Full1902 owner callback
→ generic recovery-disabled branch
→ remain passive
~~~

## 4.3 Severity

Use `INFO`, not WARN.

This lifecycle fact is useful in normal production logs and should remain visible at the default log level.

Do not move it to ERROR/WARN.

DEBUG is acceptable only if the implementation has a stronger existing INFO line that makes the branch equally reconstructable; otherwise prefer INFO.

---

# 5. Change B — Classify ReleasedForCenterMEnable as an expected recovery skip

## 5.1 Current owner behavior is correct

Current `RecoverLostInputAsync(...)` uses the existing owner gate:

~~~csharp
await _gate.WaitAsync(cancellationToken);

if (Volatile.Read(ref _disposed) != 0)
    return RecoveryFail("OwnerDisposed");

if (_releasedForEnable)
    return RecoveryFail("ReleasedForCenterMEnable");

return await RecoverLostInputCoreAsync(cancellationToken);
~~~

The important invariant is already correct:

~~~text
ReleaseForCenterMEnableAsync wins the same owner gate
→ _releasedForEnable = true
→ a later arrival-triggered recovery cannot mutate forward
~~~

Do not alter that ownership mechanism.

## 5.2 Required owner-side logging change

Do not route `ReleasedForCenterMEnable` through the generic WARN-producing `RecoveryFail(...)` logger.

Return the same semantic result while logging it as a skip.

Recommended shape:

~~~csharp
if (_releasedForEnable)
{
    AppLog.Info(
        "ControllerOwnership",
        "Owned physical input recovery skipped because controller authority was already released for Center M enable.",
        ("Event", "OwnedPhysicalRecoverySkipped"),
        ("Reason", "ReleasedForCenterMEnable"),
        ("ModeWriteIssued", false));

    return new(
        MsiClawPhysicalOwnershipOutcome.Failed,
        "ReleasedForCenterMEnable",
        false,
        _ownedHiddenTargets);
}
~~~

Keeping the returned `Outcome=Failed` is intentional for this PR.

Do not change result-contract semantics merely to improve logging, because callers and tests already use the existing outcome contract.

All **actual** recovery failures must continue through the existing `RecoveryFail(...)` WARN path, including for example:

- owner not committed;
- physical device missing;
- identity mismatch;
- PID reclaim failure;
- DirectInput not resolved;
- HidHide verification failure;
- first valid state failure.

Only the already-released authority case is reclassified.

## 5.3 Required host-side completion wording

`AddonProcessHost.RecoverOwnedControllerPhysicalInputAsync(...)` currently logs:

~~~csharp
AppLog.Info(
    "ControllerOwnership",
    "Owned physical input recovery completed.",
    ("Trigger", trigger),
    ("Result", result.Outcome),
    ("Reason", result.Reason),
    ...);
~~~

For `ReleasedForCenterMEnable`, avoid printing a user-facing/log-review `Result=Failed` classification.

Recommended narrow projection:

~~~csharp
var logResult = result.Reason == "ReleasedForCenterMEnable"
    ? "Skipped"
    : result.Outcome.ToString();

AppLog.Info(
    "ControllerOwnership",
    "Owned physical input recovery request completed.",
    ("Trigger", trigger),
    ("Result", logResult),
    ("Reason", result.Reason),
    ("PrimaryHiddenTarget", result.PrimaryHiddenTarget ?? "None"),
    ("HiddenTargetCount", result.HiddenTargets.Count));
~~~

This changes only the log field.

Do not change:

~~~text
result.Outcome
result.Reason
result.IsOwned
presentation reconcile admission
LED/vibration reapply admission
pending Device Arrival handling
~~~

## 5.4 Do not suppress the Device Arrival

The 1005/07 hardware sequence proved that current convergence is safe:

~~~text
PnP arrival during intentional PID1902 -> PID1901 release
→ recovery request reaches the physical owner
→ physical owner sees ReleasedForCenterMEnable
→ ModeWriteIssued=False
→ stock restoration continues
~~~

Do **not** add:

- an earlier Device Arrival unsubscribe;
- another cleanup gate;
- another release epoch;
- an uninstall-specific PnP filter;
- debounce;
- a new recovery-block state;
- a special retry cancellation mechanism

solely to eliminate the two log entries.

The owner rejection is already the correct authority boundary.

Two INFO/DEBUG `Skipped` entries during the transition are preferable to new lifecycle synchronization.

---

# 6. Change C — Downgrade expected Main UI transport-disconnect fallout

## 6.1 Existing top-level disconnect owner remains App.xaml.cs

Current UI transport ownership already has:

~~~csharp
private void OnFrontendDisconnected(object? sender, EventArgs args)
{
    AppLog.Info("Frontend", "Runtime disconnect observed.", ...);

    if (_dispatcherQueue?.TryEnqueue(
        () => _ = ShutdownAndExitAsync("RuntimeDisconnected")) != true)
    {
        ...
    }
}
~~~

This is the authoritative disconnect event.

Do not change that shutdown ownership.

Do not add frontend reconnect/retry.

## 6.2 Background refreshes must not duplicate an expected transport closure as WARN

For background refresh/teardown operations that were already in flight when the Runtime disconnected, handle `FrontendTransportException` separately.

The key rule is:

~~~text
FrontendProtocolException
→ still WARN / unexpected

FrontendRemoteException
→ still existing WARN / feature request failure

other unexpected Exception
→ still WARN

FrontendTransportException caused by the disappearing Runtime transport
→ DEBUG, no warning stack spam
→ UI shutdown remains owned by App.OnFrontendDisconnected
~~~

`FrontendProtocolException` derives from `FrontendTransportException`, so catch protocol exceptions first where needed.

Recommended pattern:

~~~csharp
try
{
    ...
}
catch (FrontendProtocolException exception)
{
    AppLog.Warn("...", "... failed.", exception);
}
catch (FrontendTransportException exception)
{
    AppLog.Debug(
        "Frontend",
        "... refresh skipped because Runtime transport is unavailable.",
        ("Reason", exception.Message));
}
catch (Exception exception)
{
    AppLog.Warn("...", "... failed.", exception);
}
~~~

Do not inspect nested `IOException` text or hard-code strings such as:

~~~text
"Pipe connection closed."
"Client is not connected."
~~~

The frontend client already normalizes connection loss into `FrontendTransportException`.

Do not modify `NamedPipeAddonFrontendClient` merely to expose a new connection-state property for this cleanup.

## 6.3 Apply only to the observed background/teardown seams

At minimum inspect and update the current observed warning sites:

~~~text
MainWindow.RefreshSystemStatusAsync
    System status refresh

SettingsPage.RefreshAppUpdateAsync
    update state refresh

SettingsPage.RefreshSteamFseAsync
    SteamFSE state refresh

OverlayPage.RefreshClawHudAsync
    ClawHUD state refresh

XboxSessionDiagnosticPage
    capture/stop teardown request logging
~~~

For `XboxSessionDiagnosticPage.LogRequestFailure(...)`, keep user/diagnostic failures visible while treating transport disappearance as expected teardown.

Conceptually:

~~~csharp
private static void LogRequestFailure(string action, Exception exception)
{
    if (exception is FrontendProtocolException)
    {
        AppLog.Warn(...);
        return;
    }

    if (exception is FrontendTransportException)
    {
        AppLog.Debug(
            "XboxSessionDiagnostic",
            $"Main UI diagnostic {action} request skipped because Runtime transport is unavailable.",
            ("Reason", exception.Message));
        return;
    }

    AppLog.Warn(...);
}
~~~

Do not broadly sweep all UI exceptions in the repository.

User-initiated mutation failures and real feature failures must keep their current visibility unless the exact path is proven to be the same expected Runtime-disconnect fallout.

## 6.4 No transport behavior change

Do not change:

- pipe protocol version;
- `PipeOptions.CurrentUserOnly`;
- request IDs/correlation;
- pending request completion;
- disconnect event raising;
- Runtime/UI process ownership;
- UI shutdown timeout;
- Velopack update timing;
- Runtime restart timing;
- frontend process launch.

The only change is how expected transport teardown is logged by already-doomed background work.

---

# 7. Tests

Keep tests focused. Do not build a logging framework for this PR.

## 7.1 Power transition regression

Preserve the existing test:

~~~text
StartupUnsafeProcess_ResumeRemainsPassiveWithoutBaseline
~~~

It must continue proving:

~~~text
baselineCalls == 0
RecoverySafety == Unsafe
PowerTransitionState == Unsafe
gate remains closed
~~~

If cheap with the existing `AppLog` test support, additionally verify the recovery-disabled path no longer writes the old WARN text and emits the new passive/skip event at INFO.

Do not restructure `PowerTransitionCoordinator` just to make the log assertion easier.

## 7.2 Released-for-enable recovery regression

Preserve/extend the existing physical ownership test that verifies:

~~~text
ReleaseForCenterMEnable
→ later RecoverLostInputAsync
→ Reason contains ReleasedForCenterMEnable
~~~

The test must continue proving:

- returned result remains non-owned / existing contract unchanged;
- `Reason=ReleasedForCenterMEnable`;
- no PID1902 mode write is issued;
- no DirectInput restart occurs;
- no HidHide forward mutation occurs.

If cheap, assert that the old generic:

~~~text
OwnedPhysicalRecoveryFailed
~~~

WARN is not emitted for this specific reason.

Do not change the result type or introduce a new recovery outcome enum solely for logging.

## 7.3 Main UI transport classification

Do not introduce a fake pipe server framework solely to test log severity.

Use the smallest existing UI/source test seam available.

At minimum, review the final diff to prove:

- `FrontendProtocolException` remains WARN;
- generic unexpected exceptions remain WARN;
- only `FrontendTransportException` in the selected background/teardown seams is downgraded;
- `App.OnFrontendDisconnected` still owns shutdown.

If a focused UI test can inject `FrontendTransportException` through an existing fake `IAddonFrontendControl` without new infrastructure, add it.

Otherwise manual log acceptance below is sufficient for this logging-only part.

---

# 8. Manual validation

Use one current Debug/Info-capable build.

## A. Sleep -> Resume

With Center M Disabled / Addon authority:

1. Verify PID1902 / virtual presentation is live.
2. Sleep.
3. Resume.
4. Verify controller, presentation, LED/vibration/TDP behavior remains unchanged.
5. Inspect the log.

Expected:

~~~text
PowerResumeObserved / presentation Resume evidence
GenericResumeRecoverySkipped Action=RemainPassive
~~~

Not expected:

~~~text
[WARN] ... Resume recovery is disabled because this process did not establish a safe startup boundary.
~~~

## B. SafeUninstall from Addon authority

Start with:

~~~text
Center M Disabled
PID1902 owned
HidHide active
virtual presentation attached
~~~

Uninstall normally.

Expected authority convergence remains:

~~~text
virtual presentation released
PID1902 -> PID1901
HidHide cleared
Center M Enabled
startup task removed
Runtime exits
elevated SafeUninstall continues
owned prerequisites removed
Velopack handoff
~~~

If Device Arrival / DeferredDeviceArrival lands during PID transition:

Expected:

~~~text
OwnedPhysicalRecoverySkipped
Reason=ReleasedForCenterMEnable
ModeWriteIssued=False

completion Result=Skipped
~~~

Not expected:

~~~text
[WARN] Owned physical input recovery failed.
Reason=ReleasedForCenterMEnable
~~~

Real recovery failures must still produce WARN.

## C. Velopack update / controlled Runtime restart with Main UI open

Open the Main UI and leave Settings/diagnostic surfaces active enough to have in-flight refreshes.

Perform the normal update/restart path.

Expected:

~~~text
Frontend connected
...
Runtime disconnect observed
UI exits
new Runtime/UI lifetime starts normally
~~~

At DEBUG level, transport-unavailable skip messages may exist.

At INFO/default production review, do not produce a burst of WARN stack traces solely because the old Runtime pipe was intentionally closed.

Not expected:

~~~text
Main UI update state refresh failed
FrontendTransportException: Pipe connection closed

Main UI SteamFSE state refresh failed
FrontendTransportException: Pipe connection closed

Main UI ClawHUD state refresh failed
FrontendTransportException: Pipe connection closed

System status refresh failed
FrontendTransportException: Pipe connection closed

Main UI diagnostic stop request failed
FrontendTransportException: Client is not connected
~~~

---

# 9. Expected production files

Likely production changes:

~~~text
src/SteamInputAddonforClaw/Power/PowerTransitionCoordinator.cs

src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs
~~~

Possible focused test changes:

~~~text
tests/SteamInputAddonforClaw.Tests/PowerTransitionTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs

tests/SteamInputAddonforClaw.UiTests/*
    only if an existing cheap seam fits
~~~

Expected **no** production changes to:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Install/StartupRegistration.cs
src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw/VirtualOutput/*
src/SteamInputAddonforClaw/HidHide/*
src/SteamInputAddonforClaw/Input/DirectInput/*
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
~~~

If implementation starts changing those files, re-check scope.

---

# 10. Validation commands

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Review the final diff for accidental lifecycle changes.

---

# 11. Acceptance criteria

- [ ] `recoveryEnabled == false` Resume keeps exactly the same gate/state/recovery behavior.
- [ ] The normal passive generic Resume branch is no longer emitted as WARN.
- [ ] The log clearly identifies generic stock recovery as skipped/passive.
- [ ] Full1902 owner-specific Resume ordering is unchanged.
- [ ] `ReleasedForCenterMEnable` continues to block forward recovery with `ModeWriteIssued=False`.
- [ ] `ReleasedForCenterMEnable` no longer emits generic `OwnedPhysicalRecoveryFailed` WARN.
- [ ] Host completion logs project that specific result as `Skipped`, without changing the returned result contract.
- [ ] Real owned-input recovery failures remain WARN.
- [ ] Device Arrival scheduling/coalescing is unchanged.
- [ ] No new uninstall/recovery synchronization state is added.
- [ ] Main UI Runtime disconnect is still logged once by the existing top-level disconnect handler.
- [ ] Expected `FrontendTransportException` fallout from selected background refresh/teardown paths is DEBUG/non-warning.
- [ ] `FrontendProtocolException`, remote request failures, and other unexpected exceptions remain WARN.
- [ ] No frontend reconnect/retry/state authority is introduced.
- [ ] Velopack update/restart behavior is unchanged.
- [ ] SafeUninstall behavior is unchanged.
- [ ] Sleep/Hibernate/Resume behavior is unchanged.
- [ ] Full test suite passes.

---

# 12. Overengineering guard

This work order exists because the current implementation is already converging correctly on real hardware.

Do not turn log cleanup into lifecycle redesign.

In particular:

~~~text
Expected arrival during Center M release
→ allow current owner gate to reject it
→ log Skipped
→ done

Expected UI pipe closure during Runtime replacement
→ top-level disconnect owner shuts UI down
→ background refresh logs Debug
→ done

Full1902 generic stock recovery disabled
→ owner-specific Resume path already runs
→ generic branch logs passive INFO
→ done
~~~

Do not add state, locks, epochs, barriers, managers, watchdogs, reconnect loops, or additional authority merely to make the log quieter.

The target is:

> **Keep the proven lifecycle exactly as-is; make the log describe it correctly.**
