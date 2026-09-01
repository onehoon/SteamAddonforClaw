# Work Order — PR3: Reboot-Bound Controller Authority Transition

## Status

Implementation work order for the next Full PID1902 PR after:

```text
PR1   Persistent Dual VIIPER Devices Foundation                 [merged]
  ↓
PR2   Addon-Owned Persistent HidHide Baseline Foundation        [merged]
  ↓
PR2.5 Mandatory Controller Runtime Lifetime Foundation           [merged]
  ↓
PR3   Reboot-Bound Controller Authority Transition               [this PR]
```

Current `main` baseline when this work order was prepared:

```text
87fcfb0e338e1dc22ef4a8c6695c54256dfd718e
PR2.5: mandatory controller Runtime lifetime foundation (#433)
```

### Numbering note

`docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` was written before PR2.5 was inserted into the implementation sequence and therefore still calls the reboot-bound transition slot `PR4` in its older sequence section.

The current sequence is authoritative:

```text
PR2 → PR2.5 → PR3 Reboot-Bound Controller Authority Transition
```

Do **not** create another intermediate PR merely to preserve the old numbering.

Before implementation, read and treat the following as current design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR1_PERSISTENT_DUAL_VIIPER_DEVICES_WORK_ORDER.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- current `main` implementation of:
  - `CenterMStartup/CenterMStartupControl.cs`
  - `HidHide/AddonControllerHidHideBaseline.cs`
  - `HidHide/HidHideDriverClient.cs`
  - `Settings/StartupSettingsCoordinator.cs`
  - `Lifecycle/UserTerminationGuard.cs`
  - `Hosting/AddonProcessHost.cs`
  - `Runtime/AddonRuntimeComposition.cs`
  - `Frontend/InProcessAddonFrontendControl.cs`
  - `SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
  - `SteamInputAddonforClaw.FrontendTransport/*`
  - `SteamInputAddonforClaw.UI/Views/DevicePage.xaml`
  - `SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs`

The project is pre-release. Do not add compatibility wrappers for obsolete Center M mutation or old controller-routing semantics.

---

## 1. Goal

Turn the existing MSI Center M Enable/Disable control into the first real **reboot-bound controller-authority transition** by composing the foundations already merged in PR2 and PR2.5.

PR3 must implement exactly this product contract:

```text
Center M Enabled
    ↓ user chooses Disable
[Cancel] [Disable and Restart]
    ↓
verify the current session is safe to prepare an authority change
    ↓
ensure/verify Addon background Runtime startup
    ↓
apply/verify the persistent Addon HidHide baseline
    ↓
disable/verify the three Center M startup roots
    ↓
request immediate Windows restart
```

and the reverse preparatory path that exists at this implementation stage:

```text
Center M Disabled
    ↓ user chooses Enable
[Cancel] [Enable and Restart]
    ↓
verify the current session is safe to prepare an authority release
    ↓
clear/verify the PR2 persistent Addon HidHide baseline
    ↓
enable/verify the three Center M startup roots
    ↓
mandatory Runtime policy automatically ceases to apply because the roots are Enabled
    ↓
request immediate Windows restart
```

PR3 is **not** the PID1902 ownership PR.

The current Windows session must not perform a live MSI → Addon controller takeover.

---

## 2. Non-negotiable authority model

### 2.1 The three Center M startup roots remain the persistent source of truth

Keep the current authority classification:

```text
MSI_Center_M_Server task   = Enabled
MSI_Center_M_Updater task  = Enabled
MSI Foundation Service     = Automatic
    → Center M Enabled / MSI-stock authority intent

MSI_Center_M_Server task   = Disabled
MSI_Center_M_Updater task  = Disabled
MSI Foundation Service     = Disabled
    → Center M Disabled / Addon authority intent

anything mixed
    → Partial / needs repair
```

Do **not** add:

```text
AddonControllerModeEnabled
DesiredControllerAuthority
CenterMReplacementEnabled
PendingAuthorityTransition
FirstBootAfterDisable
```

or any other persisted authority boolean/state.

Actual Windows Center M startup configuration remains the authority source.

### 2.2 Authority changes are reboot-bound

Do not implement a same-session handoff.

The supported product flow is:

```text
prepare persistent next-boot state
→ verify it
→ immediately restart Windows
→ next session starts under the selected authority intent
```

There is no `Restart Later` product mode.

### 2.3 Runtime owns the transition; UI only requests it

The Device page may:

- render the current authoritative snapshot;
- show confirmation;
- send the requested target (`Center M Enabled` or `Center M Disabled`);
- render a failure if the Runtime cannot complete the transition.

The UI must **not** directly:

- mutate HidHide;
- mutate the Addon startup task;
- call the Center M helper;
- inspect routing/native ownership;
- launch `shutdown.exe` for this feature.

The Runtime owns the ordered persistent mutation and the final restart request.

---

## 3. Explicit PR3 scope boundary

### 3.1 In scope

PR3 may:

- add one narrow reboot-bound authority-transition component;
- reuse the existing `CenterMStartupControl`;
- reuse the existing `StartupSettingsCoordinator` mandatory-startup path;
- production-wire the PR2 `AddonControllerHidHideBaseline` for the authority transition;
- perform one fresh conflict/admission assessment before entering Addon authority using existing environment detection primitives;
- reuse the existing lower-level Runtime lifecycle safety decision;
- add one small Windows-restart seam owned by the Runtime;
- update the Device-page confirmation and failure UX;
- update/rename the frontend mutation RPC so its semantics describe an authority transition rather than a plain startup-root setter;
- update focused transport/UI/unit tests.

### 3.2 Strictly out of scope

Do **not** implement any of the following in PR3:

- PID1901 → PID1902;
- PID1902 → PID1901 production restoration;
- physical controller mode commands;
- DirectInput acquisition/reacquisition;
- exact PID1902 primary-gamepad collection resolution;
- production hidden-device target discovery;
- physical isolation verification;
- Xbox360 attach;
- SteamDeck attach;
- first virtual presentation selection;
- publisher start/stop changes;
- runtime X360 ↔ SteamDeck switching;
- rumble or gyro changes;
- PID drift recovery;
- PnP-loss recovery redesign;
- suspend/resume redesign;
- Center M process killer/watchdog;
- crash supervisor/service/heartbeat;
- generalized transition transaction/rollback framework;
- uninstall redesign;
- removal of the old route-scoped routing/HidHide/recovery code.

Both persistent VIIPER logical devices remain detached by this PR's new authority-transition logic.

---

## 4. Reuse the foundations already merged

PR3 should compose existing owners instead of introducing parallel ones.

### 4.1 `CenterMStartupControl` remains the only Center M startup-root writer

`CenterMStartupControl` already owns:

```text
Capture()
SetEnabledAsync(bool)
```

and already performs the required exact read-back classification over:

```text
MSI_Center_M_Server
MSI_Center_M_Updater
MSI Foundation Service startup type
```

Do not move HidHide, reboot, controller policy, or startup-task logic into `CenterMStartupControl`.

It remains a narrow primitive used by the new transition owner.

### 4.2 `StartupSettingsCoordinator` remains the only Addon startup-registration writer

PR2.5 already established:

```text
ChangeLaunchAtWindowsStartup(true)
Repair()
IsLaunchAtWindowsStartupRequired
```

and the existing `WindowsTaskSchedulerStartupManager` remains the scheduled-task owner.

Use the existing `StartupSettingsCoordinator` instance from `AddonRuntimeComposition`.

Do not create another startup manager, scheduled-task writer, or persisted previous-preference field.

### 4.3 `AddonControllerHidHideBaseline` remains the persistent HidHide owner

PR2 already provides:

```text
InspectDisabledModeBaseline(targets)
ApplyDisabledModeBaseline(targets)
ApplyEnabledModeBaseline(targets)
```

PR3 production-wires it for the first time.

For PR3's Disable transition, the exact PID1902 physical target is intentionally not known yet, so use:

```text
requestedHiddenTargets = []
```

This is an explicit PR2-supported state:

```text
Inverse = false
Active  = true
Addon Runtime executable whitelisted
Hidden targets = 0
```

Do not invent a VID/PID wildcard.

Do not discover or hide a physical PID1902 collection in PR3.

### 4.4 The existing lower-level Runtime safety decision remains authoritative

The current `UserTerminationGuard` already protects real live ownership states such as:

```text
RoutingTransition
PendingRoutingCleanup
NativeModeActive
NativeRecoveryOwned
RecoveryMutationOwned
RuntimeShuttingDown
```

Before changing persistent controller authority intent, reuse that **lower-level** safety decision.

Do not begin PR3 persistent mutation while one of those existing ownership/cleanup states is active.

Important distinction:

`AddonProcessHost.EvaluateUserTermination()` now composes PR2.5's outer:

```text
ControllerAuthorityMandatory
```

when Center M is Disabled.

The official `Enable Center M and Restart` path must **not** be blocked by that outer mandatory-user-exit policy.

Therefore PR3 transition safety must use the underlying Runtime safety decision, conceptually:

```text
_runtimeHost.EvaluateUserTermination()
```

or an equivalent narrow delegate that excludes `ControllerAuthorityMandatory` while preserving every existing lower-level lifecycle block.

Do not bypass the real routing/native/recovery safety reasons.

---

## 5. New narrow transition owner

Add one small Runtime-owned component, suggested name:

```text
CenterMRebootAuthorityTransition
```

Exact naming may follow repository conventions, but keep the responsibility narrow.

Conceptual dependencies:

```text
CenterMRebootAuthorityTransition
    ├─ CenterMStartupControl
    ├─ StartupSettingsCoordinator
    ├─ AddonControllerHidHideBaseline
    ├─ lower-level Runtime safety snapshot/delegate
    ├─ fresh controller-environment/admission snapshot/delegate
    ├─ existing prerequisite/install-readiness fact where already available
    └─ Windows restart requester
```

Do not introduce:

- `ControllerAuthorityManager` hierarchy;
- generic transaction engine;
- rollback journal;
- transition epoch/generation;
- second state authority;
- generic hardware-mode framework.

A single in-memory `in progress` guard is acceptable if needed to reject accidental overlapping frontend requests. Do not persist it.

---

## 6. Disable and Restart — required flow

Target:

```text
Center M Disabled / Addon authority intent for next boot
```

### 6.1 Confirmation happens before the Runtime mutation request

Use a Device-page confirmation dialog equivalent to:

```text
Disable MSI Center M and switch controller authority to Steam Addon for Claw.

Windows must restart to apply this change.

[Cancel] [Disable and Restart]
```

`Cancel` must issue no backend mutation.

There is no `Restart Later` button.

### 6.2 Perform read-only preflight first

Before the first persistent mutation, verify the real prerequisites that PR3 can already prove.

At minimum:

1. supported MSI Claw / feature available;
2. current Center M startup snapshot is readable (`Enabled` or repairable `Partial`, not `Unavailable`);
3. existing lower-level Runtime lifecycle decision is safe;
4. one fresh existing controller-environment assessment does not report a conflicting unsupported controller stack for **entering** Addon authority;
5. PR2 HidHide inspection with zero hidden targets is `AlreadyCompliant` or `Applicable`, not `Conflict`/`Unavailable`;
6. existing required-component/install readiness does not report a known missing prerequisite required for the future Addon controller Runtime.

Use existing environment/prerequisite detection seams where they exist.

Do not add polling.

Do not initialize/attach VIIPER merely as a preflight probe.

Do not mutate anything during read-only preflight.

If preflight fails:

```text
no Addon startup mutation
no HidHide mutation
no Center M mutation
no reboot
return a concise blocking reason
```

### 6.3 Persistent mutation order

After preflight succeeds, perform exactly this ordered flow:

```text
1. Ensure/verify Addon launch-at-Windows-startup = true

2. Apply/verify PR2 Disabled-mode HidHide baseline with zero hidden targets

3. Set/verify Center M startup roots = Disabled

4. Request immediate Windows restart
```

#### Step 1 — mandatory Addon startup first

Call the existing `StartupSettingsCoordinator` path that sets launch-at-startup ON and synchronizes the owned startup task.

Success requires the existing registration operation to report success.

Do not disable Center M first and only then try to repair Addon startup.

The ordering invariant is:

> **The Runtime must prove it will start at the next logon before MSI Center M startup is disabled.**

If startup registration cannot be verified:

```text
stop
no HidHide mutation
no Center M mutation
no reboot
```

#### Step 2 — persistent HidHide baseline

Call:

```text
ApplyDisabledModeBaseline([])
```

Success requires:

```text
result.IsCompliant == true
```

which means either `Success` or `AlreadyCompliant` from the PR2 primitive.

If HidHide application/verification fails:

```text
stop
leave launch-at-startup ON if Step 1 already succeeded
no Center M mutation
no reboot
```

Do not build rollback machinery to restore the user's former launch-at-startup preference.

An extra enabled startup task is safe; disabling the only next-logon Runtime before authority transfer is not.

#### Step 3 — Center M roots

Call the existing:

```text
CenterMStartupControl.SetEnabledAsync(false, ...)
```

Success requires its authoritative returned snapshot to be exactly:

```text
FrontendCenterMStartupState.Disabled
```

and the existing mutation result to be successful.

If the helper is cancelled, fails, or verifies to `Partial`/`Unavailable`:

```text
stop
no reboot
return the actual latest Center M snapshot
leave the already-safe startup/HidHide preparation in place
allow retry or the opposite Enable repair action
```

Do not fabricate `Disabled` after a failed/cancelled helper call.

#### Step 4 — immediate restart

Only after Steps 1–3 are verified:

```text
request immediate Windows restart exactly once
```

At this point PR2.5's existing dynamic policy sees Center M as exactly Disabled and automatically makes:

```text
Runtime mandatory
Launch at startup mandatory
tray Restart/Exit blocked
```

No extra "authority active" flag is needed.

---

## 7. Enable and Restart — required PR3-stage flow

Target:

```text
Center M Enabled / MSI-stock authority intent for next boot
```

### 7.1 Confirmation

Use a confirmation dialog equivalent to:

```text
Restore MSI Center M controller authority.

Windows must restart to apply this change.

[Cancel] [Enable and Restart]
```

`Cancel` performs no mutation.

There is no `Restart Later` button.

### 7.2 Important PR3-stage limitation

The final Full PID1902 design eventually requires Enable to do:

```text
neutral virtual output
→ detach virtual presentation
→ release DirectInput
→ restore same physical MSI Claw to PID1901 and verify
→ teardown VIIPER
→ clear persistent HidHide baseline
→ enable Center M roots
→ reboot
```

Those physical/virtual ownership steps do **not** exist yet in the new architecture and must not be pulled into PR3.

At the PR3 implementation stage, the only persistent Addon controller state created by the new flow is the zero-target PR2 HidHide baseline plus mandatory startup policy.

Therefore PR3's Enable sequence is intentionally:

```text
1. verify existing lower-level Runtime lifecycle is safe
2. clear/verify PR2 Addon HidHide baseline for the zero-target PR3 state
3. enable/verify Center M startup roots
4. request immediate Windows restart
```

A later PID1902 ownership PR must extend the **front** of this Enable path with real virtual/DirectInput/PID1901 release before HidHide is cleared.

Do not create speculative callbacks/interfaces for those future steps in PR3.

### 7.3 Do not apply the entering-Addon conflict gate to authority release

A known HHC/ClawTweaks/foreign-controller environment should block **entering** Addon authority.

It must not automatically make the official `Enable Center M and Restart` release path impossible.

Enable still must respect:

- supported device applicability;
- readable Center M state;
- current lower-level Runtime ownership/cleanup safety;
- PR2 HidHide clear safety (including foreign-state fail-close behavior).

### 7.4 Clear PR2 baseline first

Call:

```text
ApplyEnabledModeBaseline([])
```

Success requires `IsCompliant == true`.

If it fails because HidHide contains unsupported foreign ownership/state:

```text
stop
preserve the foreign state
no Center M startup mutation
no reboot
surface the blocking reason
```

Do not destroy unknown HidHide entries merely to make authority release succeed.

### 7.5 Enable Center M roots

Call:

```text
CenterMStartupControl.SetEnabledAsync(true, ...)
```

Success requires exact read-back:

```text
FrontendCenterMStartupState.Enabled
```

Do not manually start:

- the Foundation Service;
- `MSI_Center_M_Server`;
- `MSI_Center_M_Updater`;
- `MSI Center M.exe`;
- Launcher/Game Bar components.

Windows/MSI startup on the next boot remains the owner of actual process start.

### 7.6 Mandatory startup policy releases automatically

When the Center M roots read back exactly `Enabled`, PR2.5 already evaluates:

```text
MandatoryControllerRuntimePolicy.IsMandatory(...) == false
```

Do not add a separate "release mandatory mode" state.

Do not automatically restore an old `LaunchAtWindowsStartup=false` preference.

The value forced to `true` during Disable may remain `true` after Enable. It becomes a normal editable preference again, and the user may turn it off later.

Do not persist `PreviousLaunchAtWindowsStartup` or similar rollback state.

---

## 8. Failure policy — ordered verification, no generalized rollback engine

PR3 deliberately uses simple ordered operations.

General rule:

```text
perform one required stage
→ verify actual result
→ only then continue
```

If any stage fails:

```text
do not request reboot as though success occurred
do not claim the requested authority is active
return the latest real Center M snapshot where available
surface a concise reason
allow explicit retry/repair
```

Do **not** add a generalized transaction/rollback framework.

Safe partial examples:

```text
startup ON succeeded
→ HidHide failed
```

Result:

```text
startup remains ON
Center M unchanged
no reboot
```

and:

```text
startup ON succeeded
→ zero-target HidHide baseline succeeded
→ Center M helper cancelled/failed
```

Result:

```text
startup remains ON
zero-target HidHide baseline remains
physical controller was not hidden by PR3
Center M actual roots are reported
no reboot
```

These states are safer than attempting speculative reverse mutations.

---

## 9. Frontend disconnect and cancellation semantics

The Runtime owns the transition after the user has confirmed it.

A frontend window disappearing must not become a new controller-authority decision.

Keep this simple:

- honor an already-cancelled request before persistent mutation begins;
- after the transition has accepted the request and begun persistent mutation, do not intentionally abort merely because the disposable frontend disconnects;
- UAC cancellation remains a normal `Cancelled` Center M helper result and must stop before reboot;
- do not add an epoch/barrier/background job system.

The implementation may use the caller token during read-only preflight and then use a Runtime-owned non-frontend cancellation scope for the short ordered persistent mutation.

This preserves the PR2.5 contract:

```text
frontend lifetime != Runtime/controller-authority lifetime
```

---

## 10. Windows restart requester

Add one small testable Runtime-owned seam, for example:

```csharp
internal interface IWindowsRestartRequester
{
    WindowsRestartRequestResult RequestRestart();
}
```

Exact shape may be simpler if repository conventions prefer a delegate.

The production implementation may use the existing Windows precedent:

```text
shutdown.exe /r /t 0
```

Do not add `/f` solely for this feature.

Do not add a privileged reboot helper unless the real implementation proves the normal local interactive-user restart request cannot work.

### Restart request failure

If all persistent mutations succeeded but the restart command cannot be launched/requested:

```text
do not rollback the verified persistent configuration
do not re-enable/disable roots in reverse
do not clear/reapply HidHide in reverse
return failure
show: configuration changed, but Windows restart could not be started; restart Windows manually
```

This is a real operation failure, but rollback would create more authority ambiguity than preserving the verified next-boot state.

---

## 11. Frontend contract

Keep `CaptureCenterMStartupAsync()` as the read-only Center M snapshot operation.

The existing frontend mutation name:

```text
SetCenterMStartupEnabledAsync(bool)
```

will no longer describe the product semantics after PR3 because the request now means:

```text
prepare controller authority transition
+ persistent HidHide/startup composition
+ Center M startup mutation
+ immediate restart
```

Prefer renaming/replacing the frontend mutation operation with a semantic name such as:

```text
RequestCenterMAuthorityTransitionAsync(bool centerMEnabled)
```

The internal primitive `CenterMStartupControl.SetEnabledAsync(bool)` remains unchanged.

Do not add a compatibility forwarding method solely for the old unreleased frontend contract.

If the Named Pipe RPC operation changes, bump the current frontend protocol version once and update its focused tests. Do not add multi-version compatibility logic for this pre-release change.

The existing 4-value mutation outcome shape may be reused if it still cleanly represents:

```text
Succeeded
Cancelled
Failed
Unavailable
```

`Succeeded` for the new authority-transition RPC means the persistent target was verified and the immediate restart request was successfully issued.

If the restart request fails after persistent mutation, return `Failed` with the real target snapshot and a manual-restart message.

---

## 12. Center M card UX changes

> **Later change (PR #442):** the Center M Enable/Disable card moved from the Device tab to the top
> of the **Controller** tab. Everything in this section still applies -- only the host page changed.
> Read "Device page" below as "Controller page".

The current PR1-era UI intentionally allows a startup-root mutation and then keeps a sticky:

```text
Restart Windows to apply this change.
```

That `Restart Later`-equivalent workflow is obsolete once PR3 lands.

### 12.1 Remove the sticky restart-later state

Remove the PR1-era UI-only state/behavior whose sole purpose is to remember a successful mutation until the user manually restarts, including the current `_centerMRestartRequired` flow if it is no longer used for another purpose.

A successful PR3 transition immediately requests Windows restart.

### 12.2 Button behavior

Preserve the clear explicit controls:

```text
[ Enable ] [ Disable ]
```

Do not replace them with an inverted `Disable Center M` toggle.

For a stable snapshot:

```text
Enabled  → Enable disabled, Disable available
Disabled → Disable disabled, Enable available
Partial  → both repair targets available
Unavailable → neither available
```

### 12.3 Confirmation required

Do not issue the transition RPC before the user confirms the corresponding restart dialog.

### 12.4 Busy/failure rendering

While the request is active:

- disable both buttons;
- prevent duplicate UI requests;
- preserve the current authoritative snapshot until a result arrives.

On failure/cancel:

- render the returned actual Center M snapshot;
- show the returned concise failure/cancel reason;
- re-enable the appropriate repair action(s).

On successful restart request, no normal long-lived success screen is required because Windows is expected to restart immediately.

---

## 13. Composition / wiring guidance

`AddonRuntimeComposition` already exposes its single `StartupSettingsCoordinator` instance.

A clean production wiring shape is:

```text
AddonProcessHost.InitializeRuntimeAsync
    ↓
create/use one shared CenterMStartupControl
    ↓
create normal AddonRuntimeComposition
    ↓
obtain composition.StartupSettings
    ↓
create one AddonControllerHidHideBaseline
    ↓
create one CenterMRebootAuthorityTransition
    ↓
pass read-only CenterMStartupControl + transition operation to InProcessAddonFrontendControl
```

Do not construct a second `CenterMStartupControl`.

Do not construct a second `StartupSettingsCoordinator`.

The transition component should receive only the narrow facts/actions it needs from Runtime/Startup environment rather than the entire `AddonProcessHost`.

For entering Addon authority, use one fresh existing controller-environment assessment at request time so a controller manager started after process startup is not silently ignored. Reuse the existing environment detector/provider; do not implement another HHC/ClawTweaks scanner.

---

## 14. Relationship to old route-scoped HidHide/routing code

The repository still contains the pre-Full-PID1902 route-scoped controller stack.

PR3 must not delete or rewrite it yet.

Instead, avoid authority overlap during transition:

```text
old route/native/recovery work currently active
→ lower-level Runtime safety decision blocks PR3 transition
→ user retries after the existing operation returns to safe idle
```

Do not try to merge the PR2 persistent baseline with the old recovery journal.

The PR2 primitive intentionally has no `RecoveryManager` / route-session ownership and must remain that way.

Do not make `StartupHidHideRecoveryCleaner` treat the new persistent baseline as stale route state.

Cleanup/replacement of the old routing semantics belongs after the new persistent ownership path is hardware-proven.

---

## 15. Logging

Keep logs concise and transition-oriented.

Recommended information:

```text
AuthorityTransition request: Enable/Disable
preflight result + blocking reason when any
mandatory startup verification result
HidHide baseline result/outcome/reason
Center M mutation outcome + final snapshot state
restart request result
```

Do not log continuously.

Do not add a polling loop.

Do not dump full HidHide configuration when the existing PR2 result already provides stable reason/outcome fields.

---

## 16. Required automated tests

Add focused tests for the new transition owner and update existing frontend/UI/transport tests as required.

### 16.1 Disable happy path — exact order

Pin the ordered call sequence:

```text
fresh/safe preflight
→ launch-at-startup ON verified
→ ApplyDisabledModeBaseline([]) verified compliant
→ CenterMStartupControl target Disabled verified
→ restart requested exactly once
```

Assert:

- no PID/native-mode call;
- no DirectInput call;
- no VIIPER attach call;
- no physical hidden target is invented.

### 16.2 Startup registration failure

```text
startup ON cannot be verified
→ HidHide not mutated
→ Center M not mutated
→ restart not requested
```

### 16.3 HidHide conflict/unavailable/failure

For `Conflict`, `Unavailable`, mutation failure, or verification failure:

```text
Center M roots unchanged by PR3 transition
restart not requested
```

If startup ON already succeeded, do not require rollback to the previous user preference.

### 16.4 Center M mutation failure / cancellation / partial read-back

For helper cancellation, helper failure, or final `Partial`/`Unavailable`:

```text
restart not requested
actual returned snapshot preserved
no fabricated target state
```

### 16.5 Existing live Runtime ownership blocks transition

Each realistically relevant lower-level block remains a transition blocker:

```text
RoutingTransition
PendingRoutingCleanup
NativeModeActive
NativeRecoveryOwned
RecoveryMutationOwned
RuntimeShuttingDown
```

Do not add tests for pathological instruction-level timing interleavings.

### 16.6 Enable is not blocked by PR2.5 mandatory user-exit policy

Pin this explicitly:

```text
Center M current state = Disabled
outer AddonProcessHost user termination would report ControllerAuthorityMandatory
but lower-level Runtime state is safe
→ Enable and Restart transition is allowed to proceed
```

This prevents accidentally using the composed outer user-exit decision as the authority-release gate.

### 16.7 Enable baseline clear failure

```text
ApplyEnabledModeBaseline([]) fails/conflicts
→ Center M is not enabled
→ restart not requested
→ foreign HidHide state preserved
```

### 16.8 Enable happy path

At PR3 stage:

```text
safe lower-level Runtime
→ ApplyEnabledModeBaseline([]) compliant
→ Center M Enabled exact read-back
→ restart requested once
```

Assert that PR3 did not add PID1901 restoration or VIIPER teardown behavior.

### 16.9 Partial state repair

Verify both directions can repair an operable `Partial` Center M startup configuration:

```text
Partial → requested Disabled exact target → restart
Partial → requested Enabled exact target → restart
```

subject to the corresponding preflight/HidHide safety requirements.

### 16.10 Restart requester failure

```text
persistent target verified
→ restart request fails
→ transition result = Failed
→ target snapshot remains real
→ no reverse mutation/rollback calls
```

### 16.11 UI confirmation

Pin:

- Cancel sends zero transition RPCs;
- Disable confirmation exposes `Disable and Restart` and no `Restart Later`;
- Enable confirmation exposes `Enable and Restart` and no `Restart Later`;
- buttons are busy-disabled during the RPC;
- failure renders returned snapshot/reason;
- obsolete sticky restart-later state is not used.

### 16.12 Frontend/transport contract

If the mutation RPC is renamed/replaced:

- round-trip the new request/result;
- update the protocol version exactly once;
- prove the old unreleased mutation forwarding path is not retained solely for compatibility;
- keep Center M mutation absent from QAM.

### 16.13 Architecture guards

Add narrow source/reflection tests only where they protect a real contract, for example:

- new transition owner has no dependency on PID switch/DirectInput/VIIPER attach classes;
- PR2 persistent HidHide primitive still has no `RecoveryManager` dependency;
- DevicePage does not directly start `shutdown.exe` for Center M authority transition;
- the production mutation path goes through the Runtime transition owner.

Do not add brittle tests that merely assert class names/LOC/layout without protecting a product contract.

---

## 17. Build / verification

Before considering the PR complete:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore --no-build
```

Requirements:

- Debug build: 0 warnings / 0 errors;
- Release build: 0 warnings / 0 errors;
- full automated test suite green except any explicitly existing known skip;
- `git diff --check` clean.

Do not weaken existing routing/native/HidHide/VIIPER safety tests to make PR3 pass.

---

## 18. Mandatory manual hardware validation

Unlike PR1/PR2/PR2.5 foundations, PR3 is the first PR that production-wires persistent HidHide configuration, Center M startup mutation, mandatory Runtime startup, and a Windows restart into one authority-changing user action.

Validate on a supported MSI Claw before treating the flow as hardware-proven.

### 18.1 Start from a clean Enabled baseline

Before the primary validation:

```text
MSI_Center_M_Server = Enabled
MSI_Center_M_Updater = Enabled
MSI Foundation Service = Automatic
Center M = normal stock session
Addon running
```

Because this project is pre-release, do not implement `FirstBootAfterDisable` or migration state solely to accommodate machines manually left Disabled by earlier development PRs.

A development machine already left Disabled may be returned to the clean Enabled baseline first and rebooted before testing PR3.

### 18.2 Disable and Restart

Test with Addon launch-at-startup initially OFF if practical, so the ordering is observable.

Press `Disable` and confirm `Disable and Restart`.

Before Windows exits / from logs after reboot, verify:

```text
Addon launch-at-startup was made ON before Center M roots became Disabled
PR2 HidHide baseline became:
    Active = true
    Inverse = false
    Addon executable whitelisted
    hidden target count = 0
Center M roots verified exactly Disabled
restart was requested
```

After reboot verify:

```text
Addon background Runtime starts automatically
Center M startup roots remain exactly Disabled
Center M normal startup does not run
PR2 persistent HidHide baseline remains present
```

Do **not** expect PR3 itself to establish persistent PID1902/DirectInput/X360 ownership yet.

### 18.3 Enable and Restart

From the PR3-stage Disabled baseline, press `Enable` and confirm `Enable and Restart`.

Verify before/after reboot:

```text
PR2 zero-target Addon HidHide baseline cleared/verified
Center M roots set to Enabled/Enabled/Automatic
restart requested
Center M returns through its normal Windows/MSI startup path after reboot
```

The Addon launch-at-startup preference may remain ON after Center M is Enabled; this is expected. It is simply editable again.

### 18.4 Real blocking state

If practical, also validate one real existing-routing busy case:

```text
existing route/native transition active
→ PR3 transition does not begin persistent mutation
→ user receives a retryable blocking result
```

Do not create artificial scheduler/interleaving stress tests for PR3.

---

## 19. Acceptance criteria

PR3 is complete only when all of the following are true:

1. The Center M Enable/Disable card (Device tab; moved to the Controller tab in PR #442) now means a confirmed reboot-bound authority transition, not a restart-later startup-root edit.
2. There is no `Restart Later` path.
3. UI Cancel performs zero persistent mutation.
4. Runtime owns the ordered transition and final restart request.
5. Disable proves Addon launch-at-startup ON before disabling Center M roots.
6. Disable applies/verifies the PR2 persistent HidHide baseline with exactly zero hidden physical targets.
7. Center M root mutation still goes only through the existing `CenterMStartupControl` and exact read-back verification.
8. Enable clears/verifies the PR2 zero-target baseline before enabling Center M roots at this PR3 stage.
9. PR2.5's `ControllerAuthorityMandatory` user-exit rule does not block the official `Enable Center M and Restart` path.
10. Existing lower-level routing/native/recovery safety states do block authority transition while real ownership/cleanup is active.
11. A known conflicting controller environment blocks entering Addon authority but does not automatically create a second multi-owner framework.
12. Any failed stage stops before reboot and reports actual state; no generalized rollback engine exists.
13. A restart-request failure preserves the verified persistent target and tells the user to restart Windows manually.
14. No previous launch-at-startup preference is persisted/restored as transition state.
15. No PID switch, DirectInput acquisition, exact physical HidHide target, VIIPER attach, publisher, rumble, gyro, or presentation switching is added by PR3.
16. The old route-scoped routing/HidHide/recovery implementation is not deleted or broadly refactored in this PR.
17. Focused tests cover the real production ordering/failure paths above.
18. Debug/Release builds and full tests are clean.
19. The Disable → reboot and Enable → reboot flows are manually validated on a supported MSI Claw.

---

## 20. Expected next PR — do not implement here

The next small PR should begin the **Disabled-boot admission** path.

Conceptually:

```text
mandatory Runtime starts after reboot
→ verify supported MSI Claw
→ verify Center M roots exactly Disabled
→ verify PR2 persistent HidHide baseline is acceptable
→ verify controller environment/admission facts
→ keep both VIIPER devices detached
→ report whether physical-ownership acquisition may proceed
```

Still no virtual attach in that admission PR.

The following ownership PR then performs:

```text
current PID1902 → keep
current PID1901 → switch to PID1902
bounded PnP settle
DirectInput acquire
resolve exact primary PID1902 collection
reconcile persistent HidHide target
verify physical isolation
```

Only after physical ownership/isolation is proven may a later PR attach the first X360 or SteamDeck presentation.

---

## 21. Final implementation rule

The PR3 invariant is:

> **When the user explicitly changes MSI Center M controller authority, the Runtime prepares and verifies the persistent next-boot Runtime/HidHide/Center-M configuration in a simple ordered flow and then immediately requests Windows restart. PR3 never performs a live physical-controller takeover in the current session.**

Keep the implementation small, explicit, and evidence-driven.

Protect real product lifecycle safety without adding abstractions for theoretical instruction-level races.