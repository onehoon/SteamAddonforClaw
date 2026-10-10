# Work Order — A2VM Boot Rumble Re-arm: Recover Partial PID1902 Arrival on the Second Leg

**Project:** SteamAddonforClaw — standalone Full1902  
**Date:** 2026-10-10  
**Implementation owner:** Local Codex  
**Field evidence:** `GoogleDrive/Addon/Log/AV2M/04`, Runtime **0.1.354.0**  
**Preceding change:** PR #741 (merged, squash commit `710d4ac4ae95bd9af94adfa506236a878f6f82db`)  
**Scope:** A2VM 7/8 **once-per-actual-Windows-boot** optional PID1902 → PID1901 → PID1902 rumble-prime **second leg only**  
**Not in scope:** EX/CG3EM, ordinary one-way PID1901→PID1902 startup, A2VM boot-prime first leg, manual Restore Vibration, normal recovery, shutdown, Steam/BPM routing, generic mode-controller timeout, Windows PnP enumeration implementation, HidHide/VIIPER ownership contracts.  
**Physical hardware checks:** Performed by the user **after merge**; never a local-Codex requirement, CI requirement, or PR review blocker.

## 1. Outcome Required

Fix a **real, repeated boot failure** in A2VM 8 Runtime 0.1.354.0 after PR #741:

1. The **first** PID1902→PID1901 boot-prime transition succeeds by the new 5-second additional PID1901 command-endpoint settling.
2. The **second** PID1901→PID1902 command succeeds; PID1901 disappears.
3. A PID1902 **parent/partial PnP node** becomes visible before the exact PID1902 control HID collection is ready.
4. The ordinary strict 5-second post-write transition times out with:
   - `Status=TargetDeviceDidNotAppear`
   - `WriteSucceeded=True`
   - `OldPidDisappeared=True`
   - `TargetPidPresent=True`
   - `TargetPidAppeared=False` (no exact target command HID candidate)
   - `ExactTargetTopologyProven=False`
5. The existing strict retry predicate insists on `!TargetPidPresent`; it therefore returns **no typed initial-acquisition retry reason**.
6. The startup host treats the incomplete PnP arrival as terminal, tears down its Ready canonical VIIPER Runtime, and never attaches SteamDeck despite the physical mode command already being issued successfully.

**Change only step 5 for the specific A2VM optional boot-cycle second leg.** Classify an exact, verified-write/old-PID-gone/**partial PID1902 target** as *eligible for the existing deferred first-acquisition recheck/Device Arrival watcher*. Keep the virtual controller **detached** until the **unchanged full final PID1902 proof** succeeds.

Do **not** convert a partial PID1902 devnode into a successful mode transition. This PR changes **retry eligibility**, not the evidence needed to own/publish physical input.

## 2. Source Authority and Files to Read First

Read the authoritative docs in precedence order:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Prior work orders, as **historical implementation context**:

- `docs/work-order/2026-10-10_A2VM_BOOT_NATIVE_MODE_TIMEOUT_AND_INITIAL_OWNERSHIP_RECOVERY_WORK_ORDER.md`
- `docs/work-order/2026-10-10_A2VM_BOOT_RUMBLE_INTERMEDIATE_PID1901_SETTLE_SIMPLIFICATION_WORK_ORDER.md`

Read actual code/tests on current `main` before editing:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`: `AcquireCoreAsync`, `IsTargetNotPresentAfterVerifiedWrite`, `IsBootRumblePid1901CommandEndpointPendingAfterVerifiedWrite`, `IsCrossModeTransitionProven`, `IsA2vmBootRumblePrimeModel`.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs`: exact transition result/status field semantics.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`: typed result when partial PnP parent exists but no exact HID candidate.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: `TryStartDisabledModeControllerAsync`, `InitialControllerAcquisitionDeferred`, same-owner immediate recheck, Device Arrival watcher and final attach.
- `tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs`
- `tests/SteamInputAddonforClaw.Tests/AddonProcessHostInitialControllerAcquisitionTests.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiClawModeControllerDiagnosticsTests.cs`

This is a standalone Full1902 application. **Do not reintroduce CTW ownership, services, IPC or integration.**

## 3. Confirmed 0.1.354.0 Evidence

The two failure sessions reproduce **the same second-leg classification error**:

| Runtime session | First PID1901 settle | Second PID1902 transition | Current startup outcome |
| --- | --- | --- | --- |
| 22:40, PID 9532 | Fresh Strong PID1901 after 6,394 ms from first write | At 22:41:07.771, write succeeded, old PID gone, `TargetPidPresent=True`, no exact topology after 5,525 ms | `InitialAcquisitionRetryReason=None`; VIIPER final teardown ~22:41:07.859 |
| 22:46, PID 9680 | Fresh Strong PID1901 after 7,585 ms from first write | At 22:46:38.995, write succeeded, old PID gone, `TargetPidPresent=True`, no exact topology after 5,411 ms | `InitialAcquisitionRetryReason=None`; VIIPER final teardown ~22:46:39.041 |

Both final target observations show:

```text
OldPidPresent=False
TargetPidPresent=True
TargetControlCandidateCount=0
LogicalCandidateCount=0
Result=TargetDeviceDidNotAppear
```

At 22:40, the final PID1902 probe took ~1,163 ms; at 22:46 ~1,257 ms. The exact HID collection still had not appeared at that observation. This is consistent with PnP re-enumeration still settling, **not proof of a firmware failure or a successful fully usable controller**.

Control sessions distinguish transient classification from actual native-mode failure:

- 22:38 (PID 9184): first-leg extra settle succeeds and second leg is fully proven; SteamDeck attaches at ~22:38:39.369.
- 22:43 (PID 9480): second leg times out with `TargetPidPresent=False`; existing `Pid1902TargetPidNotPresent` deferred acquisition is admitted, `ImmediateRecheck` eventually proves PID1902, and SteamDeck attaches ~22:44:15.393. Thus the **existing deferred recovery works** if an eligible typed reason is returned.
- 22:46 failure and 22:47 subsequent Runtime restart: PID1902 is already present on restart; no optional re-arm repeats, and Xbox360 attaches ~22:47:27.712, about 2.4 s after VIIPER readiness.

References (Google Drive):
- [22:40 failure](https://drive.google.com/file/d/1i7p-1xHP8xdBFt7Abs6qbNlejr6wmIB4/view)
- [22:43 deferred recovery](https://drive.google.com/file/d/1HW5B6PqSW9E-LlJwvl46VtP6rHSLNxWs/view)
- [22:46 failure](https://drive.google.com/file/d/1U2PFihRMkN0XVX7IJV4cIrp8U5tGeiBY/view)
- [libVIIPER lifecycle log](https://drive.google.com/file/d/1p-v0aa4CTkknSs9L1roChWOYdfUSHatp/view)

**Do not confuse this with a VIIPER attach fault:** in the two failed sessions no virtual controller attached; VIIPER tore down because startup rejected recovery eligibility. Actual VIIPER attach is fast in successful sessions.

## 4. Exact Defect in Current Code

`MsiClawAddonPhysicalOwnership.AcquireCoreAsync` has one common PID1901→PID1902 branch, called both by ordinary takeover and after the **A2VM boot-only first leg**:

```csharp
if (initialMode == MsiClawNativeMode.XInput)
{
    // ... call _switchMode(DirectInput, ...).
    if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
        return Fail("Pid1902TransitionFailed:" + transitionFailure, true,
            IsTargetNotPresentAfterVerifiedWrite(
                transition, MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput)
                ? MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent
                : MsiClawInitialAcquisitionRetryReason.None);
}
```

`IsTargetNotPresentAfterVerifiedWrite` requires both:

```csharp
!transition.TargetPidPresent
&& !transition.TargetPidAppeared
```

The observed target condition is **PID1902 devnode present, exact PID1902 control child absent**. Thus an otherwise verified command cannot enter the working startup recovery flow.

The existing local `bootRumbleCycleEnabled` flag in `AcquireCoreAsync` is the precise **scope gate**: it becomes true only for a strong initially PID1902 A2VM 7/8, once-per-boot attempt marker Claimed, successful exact PID1902 control HID preflight, and the preceding verified PID1901 handoff. It stays false for EX, CG3EM, A2VM initially PID1901, same-boot restarts after the attempt, and the manual restoration path.

## 5. Required Implementation — Minimal, Model/Boot Scoped

### 5.1 Extend typed **deferral eligibility** for only the second leg of the A2VM boot cycle

Keep `_switchMode(DirectInput, ...)` **and** `IsCrossModeTransitionProven` unchanged.

In the existing second-leg failure branch, classify **partial PID1902 command-endpoint arrival** as recoverable only if:

- `bootRumbleCycleEnabled` is true in this very invocation;
- `transition.Status == TargetDeviceDidNotAppear`;
- source mode is `XInput`, target mode is `DirectInput`;
- native HID write succeeded;
- old PID1901 disappeared;
- source identity was verified;
- PID1902 parent/partial target is present;
- no exact target PID1902 control HID appeared, and target topology was not proven.

**Suggested surgical code shape (adapt to the current source, do not replace unrelated logic):**

```csharp
var bootSecondLegPartialPid1902 =
    bootRumbleCycleEnabled
    && transition.Status == MsiClawModeTransitionStatus.TargetDeviceDidNotAppear
    && transition.FromMode == MsiClawNativeMode.XInput
    && transition.TargetMode == MsiClawNativeMode.DirectInput
    && transition.WriteSucceeded
    && transition.OldPidDisappeared
    && transition.SourceIdentityVerified
    && transition.TargetPidPresent
    && !transition.TargetPidAppeared
    && !transition.TargetTopologyVerified;

if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
{
    var retryEligible =
        IsTargetNotPresentAfterVerifiedWrite(
            transition, MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput)
        || bootSecondLegPartialPid1902;

    return Fail(
        "Pid1902TransitionFailed:" + transitionFailure,
        modeWriteIssued: true,
        retryEligible
            ? MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent
            : MsiClawInitialAcquisitionRetryReason.None);
}
```

Check the actual `Fail` method signature before pasting; it may require positional rather than the suggested named parameter.

`Pid1902TargetPidNotPresent` is the **existing retry enum** used for startup PID1902 endpoint-not-ready deferral, despite the narrower old name. Reuse it to activate the already-proven initial-acquisition recovery; do not add a new state machine, enum, watcher, owner or background polling loop merely to rename the condition. A bounded log label such as `PartialPid1902ControlEndpointPending` may clarify the distinction.

Do **not** generalize this rule in `IsTargetNotPresentAfterVerifiedWrite`, which is shared by other paths/tests. Do **not** relax the first-leg branch or `IsBootRumblePid1901CommandEndpointPendingAfterVerifiedWrite`.

### 5.2 Reuse the proven host path without lifecycle churn

An eligible result should travel via the **existing** `MsiClawPhysicalOwnershipResult.InitialAcquisitionRetryReason`:

```text
A2VM boot re-arm second leg:
  PID1901 → PID1902 WriteSucceeded / old PID1901 gone
  PID1902 parent present, command collection pending
      → acquisition Failed (NOT Owned)
      → typed Pid1902TargetPidNotPresent retry reason
      → AddonProcessHost.InitialControllerAcquisitionDeferred
      → one existing Device Arrival watcher + existing ImmediateRecheck
      → fresh AcquireAsync using the SAME owner and Ready VIIPER runtime
      → per-boot marker AlreadyClaimed: NO extra 1902→1901→1902 cycle
      → final PID1902 strong native state / DirectInput first valid input
      → exact HidHide baseline and readback
      → one SteamDeck/Xbox360 attach selected from current Steam/BPM state
```

No attached virtual controller may survive or be started during the pending state. The one-time marker remains consumed; **never repeat either completed native write solely because the topology probe was late**. On recheck, if PID1902 has become complete, keep it; if genuinely still absent or temporarily mixed, respect the current bounded/native-state proof and existing arrival path. Do not create a second retry worker.

Only if inspection demonstrates that existing `RunInitialControllerAcquisitionAsync` cannot preserve the pending recovery across a normal partial-devnode observation should Codex make a *small, typed, A2VM boot-specific* continuation at the existing host seam; explain the concrete path and test it. Avoid speculative generic PnP failure handling in this PR.

### 5.3 What must still fail closed

Continue returning `InitialAcquisitionRetryReason.None` (or preserving the existing stricter result) for:

- HID command write failure or unknown write result;
- old PID1901 still present / not proven gone;
- failed/weak/ambiguous source identity;
- unexpected from/to mode or status;
- multiple target control HID logical groups or `AmbiguousDevice`;
- wrong, conflicting, or verified unsafe PID1902 target;
- unrelated PnP read exceptions without typed transient evidence;
- loss of Center M Disabled authority, process shutdown/cancellation;
- unsafe DirectInput cleanup, failed physical ownership/isolation, HidHide, VIIPER or Win+G suppression proof.

The mode controller must still demand exactly one valid PID1902 target control collection for **transition success**. The full post-transition proof must still demand strong final PID1902 identity, valid DirectInput data, exact HidHide owned target/readback and presentation readiness before any virtual attach. Fail-close remains mandatory for truly unsafe conditions.

### 5.4 No extra delays or architecture

- Keep the generic **5-second post-write** transition verification window.
- Keep PR #741’s existing optional **5-second first-leg PID1901** settle behavior.
- Do **not** add an extra PID1902-specific fixed wait, second-stage state machine, new monitor, new synchronization, or global retry expansion. Existing immediate recheck + Device Arrival already demonstrated recovery at 22:43.
- Keep manual Restore Vibration and EX/CG3EM completely unchanged.

## 6. Required Focused Automated Tests

In `MsiClawAddonPhysicalOwnershipTests.cs`, reuse/extend the current harness:

1. **Regression from AV2M/04:** A2VM 8 starts strong PID1902, `Claimed`, first leg succeeds, second transition returns `TargetDeviceDidNotAppear`, `WriteSucceeded=true`, `OldPidDisappeared=true`, `SourceIdentityVerified=true`, `TargetPidPresent=true`, `TargetPidAppeared=false`, `TargetTopologyVerified=false`. Expect `IsOwned=false`, `InitialAcquisitionRetryReason=Pid1902TargetPidNotPresent`, no live input, no HidHide commit. The current harness can model this with `FailTransitionOnCall=2` and `FailureTargetPidPresent=true`.
2. **Deferred recovery completion:** on a fresh `AcquireAsync`, supply the now-strong, complete PID1902 native state. Expect `Owned`, exact original switch sequence `[XInput, DirectInput]` **once each**, no extra native-mode write, marker `AlreadyClaimed`, and final HidHide target set. This verifies the recovery completes rather than merely avoiding the error.
3. **Both A2VM variants:** parameterize the two scenarios above for `msi.claw.a2vm.7` and `msi.claw.a2vm.8`.
4. **EX/CG3EM parity:** with an otherwise identical `TargetPidPresent=true` partial PID1902 result on their ordinary native-mode takeover, retain existing terminal behavior. Do not claim boot marker or run optional round-trip.
5. **A2VM initial PID1901 parity:** initial mode PID1901 (no boot-only double-mode cycle), same partial PID1902 result: preserve current strict retry classifier unless explicitly required by a different approved work order.
6. **Negative proof matrix:** no typed retry for `WriteFailed`, `AmbiguousDevice`, incorrect modes, old PID still present, unverified source, target topology mismatch. Existing direct tests of `IsTargetNotPresentAfterVerifiedWrite` (including `TargetPidPresent=true` returning false) must remain valid.
7. **Host path:** extend the existing `AddonProcessHostInitialControllerAcquisitionTests.cs` contract tests only if the host changes. Verify a typed deferred result preserves the one Ready VIIPER owner, one watcher/immediate recheck, then one first presentation attach. Avoid duplicate host orchestration code.
8. **Preserved functionality:** successful boot-prime first-leg settle; manual Restore Vibration, PID1902 already-correct startup, full final ownership validation, HidHide/VIIPER teardown, Resume and same-boot restart behaviors unchanged.

Run the solution build, focused controller/host tests and existing automated suites as feasible. Report command/results in the implementation PR. **No real-device test is required for coding completion, CI, PR review, or merge.**

## 7. Minimal Diagnostics and Docs

Retain `NativeModeTransitionCompleted` fields. In the specific new second-leg deferred branch, emit one structured transition classification at INFO or WARN:

```text
Event=A2vmBootRumblePid1902PartialArrivalDeferred
Model=msi.claw.a2vm.8
TargetPidPresent=True
TargetPidAppeared=False
TargetTopologyVerified=False
WriteSucceeded=True
OldPidDisappeared=True
RetryReason=Pid1902TargetPidNotPresent
```

Keep per-poll trace at DEBUG; no new logging floods. Do not log a physical motor/vibration success based only on native-mode writes.

Update the **narrow A2VM exception paragraph** in the authoritative Full1902 README and one controller-lifecycle architecture doc, explaining that *after the second-leg verified write*, a partial PID1902 PnP arrival can defer first acquisition **without loosening the final proof**. Link to this work order. Preserve historical work orders as historical rather than rewriting their intended past implementation.

## 8. Definition of Done / PR Scope

- [ ] The two observed AV2M/04 partial-PID1902 second-leg failures are represented in automated tests and return the existing deferred initial-acquisition reason rather than terminal `None`.
- [ ] First acquisition remains **unowned, detached, and fail-closed** while waiting; the normal watcher/recheck eventually completes full PID1902 ownership with no duplicate mode write.
- [ ] A2VM first leg and PR #741 intermediate settle remain unchanged.
- [ ] EX/CG3EM, normal PID1901→1902 startup, A2VM initial PID1901, manual Restore Vibration and runtime ownership recovery retain previous semantics.
- [ ] No new state/authority/manager/watcher/epoch/global timeout.
- [ ] Relevant software tests pass; docs updated narrowly; create **one focused PR** for local Codex implementation.
- [ ] Physical hardware follow-up is expressly **user-owned after merge**, not a blocker.

### User-owned post-merge checks (informative, not PR blockers)

On A2VM 8, user may repeat Windows startup with Steam BPM autostart and verify first usable controller input, PID1902 stable presentation, actual left/right rumble, same-boot Runtime restart, and Sleep/Resume; A2VM 7 requires later user verification. No local Codex hardware sign-off is expected.
