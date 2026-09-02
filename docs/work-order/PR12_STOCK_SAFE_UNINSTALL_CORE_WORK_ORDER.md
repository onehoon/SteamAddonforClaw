# Work Order — PR12: Full1902 Stock-Safe Uninstall Core

## Status

Implementation work order for the next focused Full1902 lifecycle-safety PR after PR11 / PR #454.

PR11 was a hardware-validation corrective PR. PR12 returns to the remaining Full1902 product contract and implements the **Runtime-owned stock-restoration core required before uninstall**.

This PR is intentionally separate from the final Windows / Velopack uninstall-entry redesign. PR12 establishes and tests the one safe stock-restoration operation that a later installer-integration PR can call before file removal.

Before implementation, read and treat these documents as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`
- `docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR9_OWNED_PID1901_DRIFT_RECLAIM_WORK_ORDER.md`
- `docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR10_HIDHIDE_STARTUP_AUTHORITY_ADDENDUM.md`
- `docs/work-order/PR11_FULL1902_HARDWARE_VALIDATION_ROUTING_AND_STARTUP_FIXES_WORK_ORDER.md`

Also inspect the current `main` implementations of at least:

- `Install/UninstallBootstrap.cs`
- `Lifecycle/SingleInstanceGate.cs`
- `Hosting/RuntimeProcessApplication.cs`
- `Hosting/AddonProcessHost.cs`
- `CenterMStartup/CenterMRebootAuthorityTransition.cs`
- `CenterMStartup/CenterMStartupControl.cs`
- `Devices/MSI/Claw/MsiClawAddonPresentation.cs`
- `Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`
- `Startup/StockCenterMStartupBaseline.cs`
- `HidHide/AddonControllerHidHideBaseline.cs`
- `Install/StartupRegistration.cs`
- `Install/ElevatedStartupTaskSetup.cs`
- `Settings/StartupSettingsCoordinator.cs`
- the focused tests around those components.

The application is pre-release. Implement the current Full1902 authority contract directly. Do not preserve an uninstall behavior that can remove the Runtime while Addon controller authority is still active.

---

# 1. Goal

Add one **Runtime-owned, verified stock-restoration operation** that prepares the machine for product removal.

Before the Addon may intentionally disappear from the machine, the supported MSI Claw must be left in a usable stock-compatible state:

```text
virtual presentation retired
→ process-owned DirectInput released
→ physical MSI Claw proven PID1901 / XInput
→ Addon-owned HidHide controller isolation released
→ Center M startup roots proven exactly Enabled / Automatic
→ Addon mandatory startup registration may then be removed
→ StockSafeForUninstall
```

The critical invariant is:

> **Never remove or intentionally stop the only Addon controller Runtime while the machine can still be left as `PID1902 hidden + Center M Disabled`.**

PR12 must make the stock-restoration operation correct, reusable, idempotent where practical, and independently testable.

PR12 does **not** yet redesign the Windows Installed Apps / Velopack entry point that decides whether physical file removal proceeds. That integration is the next installer-focused PR.

---

# 2. Current production gap

The current Velopack hook is wired as:

```csharp
VelopackApp.Build()
    .OnBeforeUninstallFastCallback(_ => UninstallBootstrap.RunFastCallbackOnly())
    .Run();
```

`UninstallBootstrap.RunFastCallbackOnly()` currently does approximately:

```text
request running Runtime shutdown
→ delete Addon startup task
→ wait for Runtime single-instance ownership to disappear
→ clean local Addon-owned artifacts
→ return to Velopack
```

The Runtime uninstall request currently routes to:

```text
SingleInstanceGate uninstall event
→ RuntimeProcessApplication.RequestExitForUninstall()
→ ordinary Runtime shutdown
```

That is not a Full1902 authority release.

While Center M is Disabled, ordinary process shutdown intentionally preserves:

```text
DesiredAuthority = Addon
HidHide Disabled-mode baseline = persistent
physical PID may remain PID1902
```

Therefore the current uninstall path can conceptually produce:

```text
Center M roots Disabled
+ physical PID1902
+ exact PID1902 HidHide target active
+ virtual controller removed
+ Addon Runtime removed
```

which is explicitly forbidden by the Full1902 product contract.

This is a realistic lifecycle-safety gap, not a theoretical race.

---

# 3. Keep the authority model simple

Do not create another controller authority owner for uninstall.

The existing architecture already contains the required owners and primitives:

- `CenterMRebootAuthorityTransition` — ordered explicit authority release;
- `IMsiClawAddonPresentation.ReleaseForCenterMEnableAsync()` — neutral / publisher stop / detach / VIIPER teardown;
- `IMsiClawAddonPhysicalOwnership.ReleaseForCenterMEnableAsync()` — DirectInput release and same-device PID1901 restoration;
- `IStockCenterMStartupBaseline.EstablishAsync()` — current-world PID1901/XInput stock convergence and verification;
- `AddonControllerHidHideBaseline.ApplyEnabledModeBaseline()` — deterministic Addon controller-isolation release;
- `CenterMStartupControl.SetEnabledAsync(true)` — exact three-root Enable/Automatic mutation and read-back verification;
- `StartupSettingsCoordinator` / the existing bounded startup-task helper path — Addon-owned startup registration.

PR12 should compose these existing primitives.

Do **not** add:

- `UninstallControllerAuthorityManager`;
- a second PID owner;
- a second HidHide owner;
- a generalized teardown state machine;
- rollback epochs/barriers;
- a permanent privileged process;
- a Windows service;
- a generic Task Scheduler administration API.

One explicit stock-restoration path is sufficient.

---

# 4. Share the existing Center M Enable release core

The official `Enable Center M and Restart` flow already performs almost the same controller release required by uninstall.

Do not duplicate it in `UninstallBootstrap`.

Refactor the existing transition so both callers use one ordered stock-restoration core.

Conceptually:

```text
RestoreStockAuthorityAsync(reason)
    ↓
verified controller/runtime safety preflight
    ↓
retire virtual presentation
    ↓
release active physical ownership when present
    ↓
prove current physical MSI Claw is PID1901 / XInput
    ↓
release Addon HidHide controller baseline
    ↓
enable and verify Center M startup roots
    ↓
StockAuthorityRestored
```

Then:

```text
Enable Center M and Restart
→ RestoreStockAuthorityAsync(CenterMEnable)
→ request Windows restart
```

and the PR12 uninstall-preparation path becomes:

```text
PrepareForUninstallAsync
→ RestoreStockAuthorityAsync(Uninstall)
→ remove Addon startup registration only after stock authority is proven
→ UninstallPrepared
```

Exact method names are not mandated. Keep the implementation local and obvious.

A small result record/enum is acceptable if needed to return `Succeeded + Reason` to the Runtime/uninstall layer. Do not build a generalized transition framework.

---

# 5. Required stock-restoration ordering

The successful PR12 path must preserve this ordering:

```text
1. Capture current Center M startup-root state.
2. Reject Partial / Unavailable authority truth.
3. Verify no lower-level routing/native/recovery mutation is currently unsafe to interrupt.
4. Snapshot any safely provable persisted Addon-owned PID1902 hidden target.
5. Retire the virtual presentation if one exists.
6. Release active PR5 physical ownership if one exists.
7. Independently prove / establish current physical PID1901 stock baseline.
8. Clear the Addon HidHide controller baseline using the exact known owned target(s).
9. Enable Center M startup roots.
10. Re-read and prove roots are exactly Enabled / Automatic.
11. Only now remove the Addon startup registration.
12. Report UninstallPrepared.
```

Do not reorder these steps merely to reduce code.

In particular:

```text
startup task deletion
BEFORE
stock authority restoration
```

is forbidden.

The Runtime startup task is a safety guarantee while Addon authority can still exist.

---

# 6. Do not equate `NothingOwned` with `StockSafe`

This is an important PR12 requirement.

The current late-bound Center M Enable callback returns:

```text
_physicalOwnership == null
→ PhysicalOwnershipReleaseResult.NothingOwned
```

That is useful as a process-owner fact, but it is **not sufficient proof that the machine is physically stock-safe**.

A realistic Disabled-boot failure can occur after some persistent/physical state changed but before a process-lifetime ownership object was successfully committed.

Therefore PR12 must not implement:

```text
No _physicalOwnership object
→ assume PID1901
→ clear HidHide
→ enable Center M
→ uninstall success
```

Instead, after the presentation/physical-owner release stage, always use the existing current-world stock baseline to prove the final physical state:

```text
IStockCenterMStartupBaseline.EstablishAsync()
```

Required semantics:

```text
current PID1901 / XInput
→ verify and continue

current PID1902 / DirectInput
→ existing strongly-validated stock-baseline switch to PID1901
→ fresh capture verifies PID1901
→ continue

missing / ambiguous / weak identity / failed mode mutation
→ fail closed
→ do not clear the final safety guarantees
→ do not report uninstall prepared
```

This reuses an existing real lifecycle primitive. Do not add a second native-mode restoration implementation.

---

# 7. Reuse the SAME native authority

`AddonStartupComposition` already creates `StockCenterMStartupBaseline` from the MSI Claw adapter's existing `MsiClawNativeStateManager`.

Reuse that existing instance through the current composition.

Do not construct another independent `MsiClawNativeStateManager` solely for uninstall.

The stock verification stage must remain bounded and use the current strong physical-identity rules already hardened by PR11.

Do not weaken same-mode validation and do not select a device by VID/PID first-match.

---

# 8. Recover the exact HidHide target before tearing down owner state

When a process-lifetime physical owner exists, `ReleaseForCenterMEnableAsync()` returns its exact committed `HiddenTarget`.

Use that target for the Enabled-mode HidHide cleanup.

However, PR12 must also handle a process where no active `_physicalOwnership` object exists but a valid persistent Disabled-mode HidHide baseline still proves one exact Addon-owned PID1902 primary collection.

Before owner teardown, use the already-existing read-only primitive where applicable:

```text
AddonControllerHidHideBaseline.TryGetSingleExistingOwnedTarget(
    MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId)
```

Preferred target selection:

```text
release result returned exact HiddenTarget
→ use it

else safely validated persisted Addon-owned target exists
→ use it

else
→ no exact target is claimed
```

Do not infer ownership from a broad `VID_0DB0&PID_1902` match.

Do not remove arbitrary foreign hidden-device entries during Enabled/uninstall release.

---

# 9. HidHide release policy remains the September 1 authority

Use the existing `ApplyEnabledModeBaseline()` behavior.

Successful uninstall preparation must establish the current release policy:

```text
Addon Runtime whitelist entry removed
known exact Addon-owned hidden target(s) removed
Inverse = false
Active = false
verified official HidHideCLI registration preserved if present
verified official HidHideClient registration preserved if present
no historical third-party configuration reconstruction
```

Do not add:

- `PreviousApplications[]`;
- `PreviousHiddenDevices[]`;
- third-party owner maps;
- arbitrary whitelist backup/restore;
- broad hidden-device deletion.

If an exact Addon-owned target cannot safely be proven, do not invent one. `Active=false` still prevents the machine from being trapped behind active HidHide isolation; only entries safely known to be Addon-owned may be explicitly removed.

Every required mutation/read-back remains fail-closed.

---

# 10. Center M startup roots are the final authority truth

After physical stock mode and HidHide release are proven, call the existing:

```text
CenterMStartupControl.SetEnabledAsync(true, CancellationToken.None)
```

The final successful state must read back exactly:

```text
MSI_Center_M_Server task      Enabled
MSI_Center_M_Updater task     Enabled
MSI Foundation Service        Automatic
```

Anything else is not `UninstallPrepared`.

`Partial` or `Unavailable` must fail closed.

Do not silently choose MSI authority from a mixed state.

Do not add another persisted `AddonControllerModeEnabled` flag.

---

# 11. Startup task removal is LAST

Current `UninstallBootstrap.RunFastCallbackOnly()` attempts:

```text
WindowsTaskSchedulerStartupManager().Synchronize(false)
```

before Full1902 authority has been restored.

PR12 must establish the opposite product rule:

> **The mandatory Addon Runtime startup guarantee is not released until PID1901 + HidHide release + Center M Enabled are all proven.**

After `StockAuthorityRestored`:

```text
remove Addon-owned startup task
→ verify it is absent
→ only then report UninstallPrepared
```

Prefer routing this through the existing startup-registration ownership rather than creating another task writer.

If the current normal-user delete is denied on real Windows, extend the existing **bounded self-elevation pattern** with the smallest exact Addon-owned delete operation.

Acceptable shape if required:

```text
SteamInputAddonforClaw.exe --remove-startup-task <current-user>
```

or an equivalent tightly constrained action in the existing startup-task helper.

Requirements:

- only the fixed task `Steam Input Addon for Claw` may be deleted;
- no generic task name/path arguments;
- helper exits immediately;
- normal Runtime performs an independent read-back proving the task is absent;
- UAC cancellation/failure means uninstall preparation fails;
- no long-lived elevation;
- no service/broker.

Do not add this elevated delete path speculatively if the current exact-task deletion is proven to work without elevation in the supported installation. The implementation should use the smallest path real behavior requires.

---

# 12. Center M already Enabled path

Uninstall while Center M is already exactly Enabled / Automatic must also finish in a verified stock-safe state.

Do not merely assume:

```text
roots Enabled
→ physical PID must already be 1901
```

Use the existing stock baseline as the current-world proof.

Conceptually:

```text
roots exactly Enabled
→ no Addon controller authority should be entered
→ establish/verify StockCenterMStartupBaseline
→ release any remaining safely provable Addon HidHide entries
→ roots still exactly Enabled
→ remove Addon startup task
→ UninstallPrepared
```

This path should normally be idempotent and cheap because startup already converges the stock baseline.

Do not start Full1902 PID1902 ownership merely to uninstall.

---

# 13. Center M Disabled path

The normal Full1902-owned path is:

```text
roots exactly Disabled
→ Runtime remains controller authority during preparation
→ presentation retire
→ physical owner release if present
→ stock baseline independently proves PID1901
→ HidHide Enabled-mode baseline
→ Center M roots Enabled
→ readback verified
→ startup task removed
→ UninstallPrepared
```

Do not request a Windows restart from the stock-restoration core itself.

Uninstall is an exceptional explicit authority release. The later installer integration decides whether a restart is required for package/dependency cleanup.

PR12 must not force a reboot merely because the reusable `Enable Center M and Restart` caller does.

---

# 14. Partial / Unavailable authority state

If startup-root truth is:

```text
Partial
or
Unavailable
```

PR12 must not guess which controller authority is valid.

Result:

```text
PrepareForUninstallAsync
→ fail
→ preserve Runtime / startup registration
→ preserve recovery evidence
→ do not report StockSafe
```

Do not mutate a physical controller under ambiguous authority merely to make uninstall convenient.

A later UI/installer layer can tell the user that controller authority must be repaired before removal.

---

# 15. Failure policy

The stock-restoration operation is ordered and fail-closed.

Examples:

### Presentation release fails

```text
stop
→ do not release physical ownership
→ do not clear HidHide
→ do not enable Center M roots
→ keep startup task
```

### PID1901 restoration / stock baseline fails

```text
stop
→ do not claim stock-safe
→ HidHide remains protective as required by the still-running Runtime
→ keep startup task
```

### HidHide release fails after PID1901 is proven

```text
stop
→ do not delete startup task
→ do not report uninstall prepared
```

Center M roots should not be enabled before required Addon HidHide release is verified.

### Center M Enable fails / becomes Partial

```text
stop
→ keep startup task
→ do not report uninstall prepared
```

### Startup-task removal fails after stock authority is already restored

```text
stock controller remains usable
Center M remains Enabled
→ report uninstall preparation failure / incomplete cleanup
→ do not reverse back into Addon authority
```

Do not build a generalized rollback transaction engine.

Once a verified step has safely moved toward stock authority, do not create new PID/HidHide churn merely to restore the previous Addon state after a later cleanup failure.

---

# 16. Cancellation semantics

Follow the existing explicit authority-transition policy.

User/frontend cancellation may be honored during read-only preflight.

Once the first controller-authority mutation begins:

```text
presentation retirement / physical release / PID restore starts
→ Runtime owns completion
→ frontend disconnect or caller cancellation must not strand half-restored authority
```

Use `CancellationToken.None` across the ordered mutation section where the existing Center M Enable path already does so.

Do not add an epoch/barrier solely for cancellation timing.

---

# 17. Runtime ownership and invocation seam

The stock-restoration operation must run inside the normal Runtime while its real controller owners still exist.

Do not move PID/HidHide/VIIPER teardown logic into the Velopack fast callback process.

Expose one narrow Runtime-owned operation that the future safe-uninstall entry can request.

Conceptually:

```text
AddonProcessHost.PrepareForUninstallAsync()
```

or equivalent.

It should return a bounded result such as:

```text
Succeeded
Reason
```

The exact public/internal shape is implementation choice.

Do not expose generic controller mutation commands through `SingleInstanceGate`.

If PR12 needs to replace the current one-way uninstall `EventWaitHandle` with a result-capable narrow request seam for testing/future PR13 use, keep it strictly specific to:

```text
PrepareStockForUninstall
```

Do not build a generic command bus in this PR.

---

# 18. Relationship to current `UninstallBootstrap`

PR12 is the **core/preparation PR**, not the final Velopack interception PR.

The current fast callback is not an appropriate owner for long controller-authority mutation because Velopack fast callbacks are time-bounded lifecycle hooks and the app process exits after the callback.

Therefore in PR12:

- do not copy stock restoration into `RunFastCallbackOnly()`;
- do not rely on the fast callback as the sole proof that stock restoration completed;
- do not add UI/UAC-heavy authority orchestration to the fast callback;
- do not claim Windows Installed Apps uninstall is fully safe merely because PR12 merged.

A follow-up installer PR must route the real user uninstall through the PR12 preparation operation **before** Velopack irreversible file removal is allowed to begin.

The fast callback may remain as bounded, idempotent local cleanup until that follow-up changes its role.

---

# 19. PR13 boundary — explicitly out of scope

The following belongs to the next installer-integration PR, not PR12:

- intercepting / replacing the Windows Installed Apps uninstall entry;
- deciding how a failed stock-prepare result prevents package removal;
- launching Velopack `Update.exe uninstall` only after preparation succeeds;
- final user-facing uninstall failure UI;
- final Velopack registry/uninstall-command integration;
- dependency package removal policy;
- restart-after-uninstall policy.

PR12 must leave a clean, narrow callable contract for that work.

Do not solve PR13 inside PR12 with an undocumented registry hack.

---

# 20. Dependency uninstall is out of scope

Do not uninstall HidHide or usbip-win2 in PR12.

PR12 only releases **Addon controller ownership/configuration**.

The provisioning receipts already provide evidence for whether dependencies were originally missing, but dependency-package removal has separate ownership/reboot consequences and should be a later focused cleanup decision.

Specifically do not add:

```text
HidHide installer uninstall
usbip-win2 driver uninstall
broad driver/package deletion
```

to this PR.

The system may remain with HidHide installed but inactive and without Addon-owned controller isolation after Addon removal.

That is stock-safe.

---

# 21. Overlay work is not part of PR12

Overlay/OQ work is currently evolving independently.

PR12 must not perform legacy routing cleanup or restructure Overlay input ownership.

The only required compatibility rule is:

> `ReleaseForCenterMEnableAsync()` / final presentation teardown must still win even if the active virtual presentation is currently Overlay-paused/captured.

Reuse that existing teardown contract.

Do not modify semantic Overlay navigation, capture, visibility authority, QAM, or final controller-input routing merely to implement uninstall.

Legacy routing cleanup remains deferred until the final Overlay/controller-input architecture is settled.

---

# 22. Required implementation tests

Add focused tests for the real product contract.

At minimum cover:

## 22.1 Disabled + active Full1902 ownership — happy path

Verify strict order:

```text
presentation release
→ physical release
→ stock baseline verify PID1901
→ HidHide enabled baseline
→ Center M roots Enable
→ startup task removal
```

and final result succeeds.

## 22.2 Presentation release failure

Verify:

- physical release not attempted;
- stock baseline not mutated;
- HidHide not cleared;
- Center M roots not enabled;
- startup task not removed;
- result fails.

## 22.3 Physical release failure

Verify downstream HidHide/Center M/startup deletion do not run.

## 22.4 No active physical owner but current PID1902

This is critical.

Simulate:

```text
_physicalOwnership = null
current physical = PID1902
```

Verify PR12 does **not** treat `NothingOwned` as stock-safe.

`StockCenterMStartupBaseline` must establish/verify PID1901 before cleanup continues.

## 22.5 No active physical owner and current PID1901

Verify stock baseline reports already stock and cleanup proceeds idempotently.

## 22.6 Stock baseline failure

Missing/ambiguous/weak/failed mutation must prevent HidHide clear, Center M enable, and startup-task deletion.

## 22.7 Persisted exact HidHide target fallback

When no owner returns a target but one exact compliant primary PID1902 target is safely recoverable from the persistent baseline, verify that exact target is passed to Enabled-mode cleanup.

Do not accept a broad PID1902 string or unrelated hidden target.

## 22.8 HidHide release failure

Verify Center M roots and startup task are not mutated after failed HidHide verification.

## 22.9 Center M Enable failure / Partial readback

Verify startup task remains present and result fails.

## 22.10 Startup task removal failure after stock restore

Verify:

- result reports failure/incomplete preparation;
- do not reverse PID1901 back to PID1902;
- do not re-enable HidHide Addon isolation;
- do not disable Center M roots again.

## 22.11 Center M already Enabled

Verify:

- stock baseline is still independently proven;
- no Addon PID1902 acquisition starts;
- HidHide release is idempotent;
- startup task removal occurs only after stock proof.

## 22.12 Partial / Unavailable roots

Verify no physical/HidHide/startup mutation and fail closed.

## 22.13 Existing Enable-and-Restart behavior

Regression-test that the normal UI path still performs the same shared stock restoration and then requests exactly one Windows restart.

PR12 must not accidentally remove the reboot boundary from normal authority switching.

---

# 23. Existing test suites to preserve

Run the full repository test suite.

At minimum pay special attention to:

- `CenterMRebootAuthorityTransitionTests`
- `MsiClawAddonPresentationTests`
- `MsiClawAddonPhysicalOwnershipTests`
- `StockCenterMStartupBaselineTests`
- `AddonControllerHidHideBaselineTests`
- `CenterMStartupControlTests`
- `WindowsTaskSchedulerStartupManagerTests`
- `StartupSettingsCoordinatorMandatoryTests`
- `UninstallBootstrapTests`
- `AddonRuntimeHostTests`

Do not weaken tests that protect:

- PR11 cross-PID hardware behavior;
- same-mode strong identity validation;
- exact HidHide target rules;
- one virtual-presentation owner;
- Center M root exact-state classification;
- mandatory Disabled-mode Runtime startup.

---

# 24. Logging requirements

Add concise lifecycle evidence sufficient for later hardware validation.

Recommended events:

```text
Event=UninstallStockPrepareStarted
CenterMState=Enabled|Disabled|Partial|Unavailable

Event=UninstallPresentationRelease
Outcome=...

Event=UninstallPhysicalRelease
Outcome=...
HiddenTarget=...

Event=UninstallStockBaseline
Outcome=...
ModeWriteIssued=true|false
Reason=...

Event=UninstallHidHideRelease
Outcome=...
Reason=...

Event=UninstallCenterMEnable
Outcome=...
FinalState=...

Event=UninstallStartupTaskRemoval
Outcome=...
ReadbackVerified=...

Event=UninstallStockPrepareCompleted
Outcome=Success|Failed
Reason=...
```

Do not log raw controller input reports.

Do not add high-frequency polling for uninstall diagnostics.

---

# 25. Hardware validation after PR12

PR12 core should be hardware-tested directly before PR13 wires real package removal.

Use a developer/test invocation of the preparation operation or the smallest temporary harness already available; do **not** actually delete the application as part of initial PR12 validation.

Validate at least:

## A. Center M Disabled / X360 presentation

```text
PID1902 + X360 live
→ invoke stock-uninstall preparation
→ X360 disappears cleanly
→ PID1901 appears
→ HidHide Active=false
→ Addon hidden target removed when known
→ Center M roots Enabled/Automatic
→ startup task removed
→ stock controller works
```

## B. Center M Disabled / SteamDeck presentation

Same expected final stock state.

## C. Center M already Enabled

Verify no unnecessary PID churn when already PID1901.

## D. Failure test where practical

Cancel the Center M/startup-task elevation path or inject a controlled test failure and verify the Runtime does not report uninstall prepared.

Do not invent artificial instruction-level races solely for validation.

---

# 26. Acceptance criteria

PR12 is complete when all of the following are true:

1. There is one Runtime-owned stock-restoration operation reusable by both Center M Enable and uninstall preparation.
2. `Enable Center M and Restart` still restores stock authority and then requests restart exactly as before.
3. Uninstall preparation does not duplicate PID/HidHide/Center M release code in `UninstallBootstrap`.
4. A missing `_physicalOwnership` object is not accepted as proof of stock PID1901.
5. The existing stock baseline independently proves current PID1901 before uninstall preparation can succeed.
6. Exact safely provable Addon-owned HidHide target(s) are removed and HidHide is left `Inverse=false`, `Active=false`.
7. Center M startup roots read back exactly Enabled / Enabled / Automatic.
8. The mandatory Addon startup task is removed only after stock authority is proven.
9. Any unsafe/ambiguous physical state fails closed.
10. Partial/Unavailable Center M root truth fails closed.
11. No new controller authority manager/state-machine/supervisor is introduced.
12. No dependency packages are uninstalled.
13. Overlay/QAM/legacy-routing architecture is not refactored.
14. Focused tests and the full suite pass.
15. The PR/documentation clearly states that final Velopack/Windows uninstall interception remains PR13 work and that PR12 alone must not be advertised as end-to-end uninstall safety.

---

# 27. Review guidance

Review this PR against realistic supported lifecycle behavior.

Blocking findings include:

- uninstall preparation can succeed while physical PID1902 is not proven restored to PID1901;
- `NothingOwned` is treated as stock proof;
- HidHide is cleared before physical stock mode is proven;
- Center M roots are enabled before required HidHide release verification;
- Addon startup registration is removed before stock authority is established;
- Partial/Unavailable authority state is silently treated as MSI/stock;
- a different/ambiguous physical device can receive the stock mode mutation;
- existing `Enable Center M and Restart` ordering/regression is broken;
- a failed late cleanup step causes code to re-enter Addon authority and creates additional PID/HidHide churn;
- a new generalized authority/state framework is introduced without protecting a realistic product lifecycle.

Do not block for purely theoretical instruction-level races where existing owner gates and ordered teardown converge safely under the supported one-user / one-interactive-session product model.

---

# 28. Final architectural shape after PR12

```text
                  ┌──────────────────────────────┐
                  │ Runtime stock restore core   │
                  └──────────────┬───────────────┘
                                 │
                 presentation / physical / stock
                 PID1901 / HidHide / Center M
                                 │
                    verified Stock Authority
                   ┌─────────────┴─────────────┐
                   │                           │
     Enable Center M and Restart       Prepare For Uninstall
                   │                           │
        request Windows restart          remove startup task
                                               │
                                      UninstallPrepared
                                               │
                                  [PR13 installer integration]
```

The important result is not a new abstraction hierarchy.

The result is one clear rule:

> **The Addon may disappear only after the current MSI Claw is proven stock-safe and MSI authority is restored.**
