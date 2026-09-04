# Work Order — Full1902 Cleanup I: Disconnect Developer Synthetic Steam State from Production Runtime

## Status

Focused production cleanup after PR #483 / Cleanup H.

This work order deliberately **does not delete the Developer Test UI or its implementation code**. The goal is narrower:

> Preserve the Developer Test feature source/UI/tests for later Full1902 redesign, but remove its synthetic Steam-session semantics from the production Runtime object graph.

Code-review baseline used for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     ca1b064a890c4b42f97ef516510d6530031cc144
latest merged production PR: #483 — Full1902 Cleanup H
```

Read these first and use the authority order in the Full1902 README:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_H_REMOVE_LEGACY_RECOVERY_JOURNAL_COMPATIBILITY_SHELL_WORK_ORDER.md`

---

# 1. Product decision for Cleanup I

The Developer Test feature is being **parked, not removed**.

Current decision:

```text
Developer Test UI/source/tests
→ keep
→ may remain visible and toggleable
→ may remain temporarily functionally disconnected from controller presentation

Production controller / Steam runtime
→ must not consume Developer Test as a Steam fact
→ must not publish Developer Test as a synthetic Steam session
→ must not keep effective-session routing/diagnostic plumbing alive solely for Developer Test
```

This is consistent with the cleanup handoff's Developer policy:

```text
Developer source / experimental diagnostic implementation
        X
        │ disconnect from production authority/runtime graph
        X
Full1902 production controller ownership / presentation / recovery / status
```

Do not redesign the Developer Test feature in this PR.

Do not reconnect it to Full1902 presentation through another production seam.

A later dedicated Developer/diagnostic redesign can choose the correct way to exercise the current Full1902 presentation owner.

---

# 2. Current production coupling on `main`

Fresh reference closure on `ca1b064...` shows the old synthetic graph is still production-owned.

## 2.1 `SteamSessionRuntime`

Current production construction includes:

```text
SteamSessionWatcher
SteamBigPictureWatcher
DeveloperTestModeState
EffectiveSteamSessionSource
DiagnosticSessionTracker
```

The constructor currently creates:

```csharp
DeveloperTestModeState = new DeveloperTestModeState();
_effectiveSource = new EffectiveSteamSessionSource(
    _sessionWatcher,
    _bigPictureWatcher,
    DeveloperTestModeState);
```

and republishes synthetic effective transitions through:

```text
SteamSessionRuntime.StateChanged
→ AddonRuntimeHost.SteamSessionStateChanged
→ InProcessAddonFrontendControl.StateInvalidated
```

`OnEffectiveStateChanged(...)` also feeds:

```text
DiagnosticSessionTracker.Observe(
    raw RunningAppID,
    effective RunningAppID,
    effective source)
```

The effective source can synthesize:

```text
actual Steam inactive
+ BPM inactive
+ Developer Test enabled
→ SteamSessionState.CreateDeveloperTest()
```

This is routing-era semantics and is not a current Full1902 production fact.

## 2.2 Current Full1902 presentation already uses the correct facts

The current controller presentation path is already intentionally separate:

```text
ActualRunningAppId
ActualRunningAppIdChanged
BigPictureStateChanged
CapturePresentationSnapshot()
SteamPresentationSnapshot.WantsSteamDeck
```

and:

```text
WantsSteamDeck = RunningAppId != 0 || BigPictureActive
```

Developer Test is explicitly not part of `SteamPresentationSnapshot`.

Preserve that contract.

---

# 3. Required end state

After Cleanup I, the production Runtime graph should conceptually be:

```text
SteamRunningAppIdRegistrySource
        │
        ├─ current RunningAppID
        └─ actual RunningAppID change event

SteamBigPictureWatcher
        │
        ├─ current BPM state
        └─ BPM state change event

             ↓
SteamSessionRuntime
             ↓
raw/current SteamPresentationSnapshot
             ↓
Full1902 presentation owner
             ↓
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Developer Test becomes a disconnected UI/developer island:

```text
DeveloperPage
Frontend RPC: SetDeveloperTestMode
DeveloperTestModeState
EffectiveSteamSessionSource source/tests
DiagnosticSessionTracker source/tests

        X  no production Steam/presentation dependency
```

The Developer toggle may continue to report `TestModeEnabled=true/false` to the UI, but it must have **zero effect** on:

```text
controller authority
PID1901/PID1902 ownership
HidHide
DirectInput
VIIPER attachment
X360/SteamDeck presentation selection
actual RunningAppID
BPM state
production status safety
power/recovery authority
```

---

# 4. Explicit preservation list — DO NOT DELETE

This PR is not a Developer cleanup by deletion.

Preserve the following source unless a tiny compile-only edit is required:

```text
src/SteamInputAddonforClaw/Developer/DeveloperTestModeState.cs
src/SteamInputAddonforClaw/Steam/EffectiveSteamSessionSource.cs
src/SteamInputAddonforClaw/Diagnostics/DiagnosticSession.cs
```

In particular, preserve:

```text
DeveloperTestModeState
DeveloperTestModeState.Changed
DeveloperTestModeState.SetEnabled(...)

EffectiveSteamSessionSource
SteamSessionStateChangedEventArgs
SteamSessionState / DeveloperTest state helpers used by this isolated feature

DiagnosticSession
DiagnosticSessionTracker
```

Preserve the Developer UI and frontend transport contract:

```text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs

FrontendDeveloperSnapshot
IAddonFrontendControl.SetDeveloperTestModeAsync(...)
FrontendRpcMethod.SetDeveloperTestMode
SetDeveloperTestModeRequest
NamedPipeAddonFrontendClient handling
NamedPipeAddonFrontendServer handling
```

The UI toggle should continue to be present.

It is acceptable that toggling it no longer changes controller/Steam behavior.

Do not hide, remove, rename, or redesign the UI in Cleanup I.

---

# 5. `SteamSessionRuntime` production simplification

File:

```text
src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs
```

Remove the production ownership/reference closure for the synthetic effective-session graph.

Expected removals from `SteamSessionRuntime`:

```text
DeveloperTestModeState property/ownership
_effectiveSource
_diagnosticSessions
StateChanged synthetic effective-session event
OnEffectiveStateChanged(...)
EffectiveSteamSessionSource subscription/unsubscription
DiagnosticSessionTracker completion owned by this Runtime
```

Fresh-grep before deleting each member, but current `main` shows these members exist only to keep the synthetic session path alive.

## 5.1 `SteamSessionWatcher`

Do **not** delete `SteamSessionWatcher.cs` in this PR.

`EffectiveSteamSessionSource` is intentionally retained for later Developer redesign and its unit tests still use `SteamSessionWatcher`.

However, production `SteamSessionRuntime` does not need to keep a `SteamSessionWatcher` instance merely to support the disconnected effective-session source.

If fresh closure confirms no current production need, remove `_sessionWatcher` from `SteamSessionRuntime` and keep the helper class itself as isolated source.

Do not replace it with another wrapper/manager.

## 5.2 Preserve actual Steam/BPM behavior

`SteamSessionRuntime` must continue to provide the current production facts:

```text
ActualRunningAppId
ActualRunningAppIdChanged
BigPictureStateChanged
IsBigPictureActive
CapturePresentationSnapshot()
StartActualObservation()
Refresh()
```

Do not change presentation semantics:

```text
RunningAppId != 0 OR BigPictureActive
→ WantsSteamDeck = true
```

Developer Test must not be introduced into this calculation.

## 5.3 Resume refresh must still refresh current facts

Cleanup I must not regress Sleep/Hibernate/Resume.

`AddonRuntimeHost.ReconcileFreshAfterResumeAsync(...)` still calls:

```text
SteamSessionRuntime.Refresh()
```

The post-cleanup `Refresh()` must still re-read/reconcile the production Steam/BPM facts needed after resume.

Use the smallest existing-source implementation.

Do not add polling, a timer, epoch, cache manager, or new watcher abstraction.

The important invariant is:

```text
Resume
→ refresh actual RunningAppID/BPM current facts
→ current Full1902 resume/presentation reconciliation can converge
```

---

# 6. `AddonRuntimeHost` must stop exposing synthetic Steam state

File:

```text
src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
```

Remove the synthetic Developer/effective-session production surface:

```text
DeveloperTestModeState => _steamRuntime.DeveloperTestModeState
SteamSessionStateChanged event
OnSteamSessionStateChanged(...)
_steamRuntime.StateChanged subscription
_steamRuntime.StateChanged unsubscription
```

Update comments that still describe `SteamSessionStateChanged` as a generic Runtime/UI event.

Preserve:

```text
ActualRunningAppId
ActualRunningAppIdChanged
CapturePresentationSnapshot()
StatusRefreshRequested
PowerResumeObserved
StartPowerObservation()
PrepareForShutdown()
ReconcileFreshAfterResumeAsync(...)
UserTerminationGuard
PowerMutationGate / RecoverySafetyState integration
```

No new host/interface is needed.

---

# 7. Keep Developer UI state, but own it outside production Steam Runtime

The UI/RPC still needs a `DeveloperTestModeState` instance so the existing toggle and `FrontendDeveloperSnapshot(TestModeEnabled)` remain coherent.

Current composition passes:

```text
_runtimeHost.DeveloperTestModeState
→ InProcessAddonFrontendControl
```

That coupling must be removed.

Preferred minimal shape:

```csharp
var developerTestModeState = new DeveloperTestModeState();

_frontendControl = new InProcessAddonFrontendControl(
    ...,
    _runtimeHost,
    developerTestModeState,
    ...);
```

The exact local/field placement may follow current `AddonProcessHost` construction style.

Do not create:

```text
DeveloperTestManager
DeveloperStateService
DeveloperRuntimeHost
IDeveloperTestAuthority
new singleton framework
```

One plain `DeveloperTestModeState` instance owned only for the frontend/developer surface is sufficient.

## 7.1 UI behavior after disconnection

`InProcessAddonFrontendControl.SetDeveloperTestModeAsync(...)` may continue to:

```text
_developer.SetEnabled(enabled)
StateInvalidated?.Invoke(...)
return FrontendDeveloperSnapshot(_developer.IsEnabled)
```

This means:

```text
user toggles Developer Test
→ UI reflects enabled/disabled
→ developer state/logging still works
→ no production controller behavior changes
```

That is the intended temporary behavior.

---

# 8. `InProcessAddonFrontendControl` production event cleanup

File:

```text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Remove the frontend invalidation subscription that exists only for the synthetic effective-session event:

```csharp
_runtime.SteamSessionStateChanged += ...
```

Preserve current invalidation sources that represent real production facts:

```text
ActualRunningAppIdChanged
StatusRefreshRequested
PowerResumeObserved where currently used
other feature-local invalidations
```

The Developer Test RPC itself stays.

Do not remove `_developer` or `SetDeveloperTestModeAsync`.

---

# 9. `AddonProcessHost` composition change

File:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Replace the existing frontend dependency:

```text
_runtimeHost.DeveloperTestModeState
```

with one standalone frontend/developer state instance.

The state instance must not be passed into:

```text
SteamSessionRuntime
AddonRuntimeHost
MsiClawAddonPresentation
MsiClawAddonPhysicalOwnership
HidHide
VIIPER
PowerTransitionCoordinator
status provider
profile runtime
```

No controller/presentation owner should subscribe to `DeveloperTestModeState.Changed`.

---

# 10. Keep `EffectiveSteamSessionSource` as disconnected future code

File:

```text
src/SteamInputAddonforClaw/Steam/EffectiveSteamSessionSource.cs
```

Do not delete or redesign this file in Cleanup I.

It is acceptable for it to have zero production construction sites after this PR.

Its existing behavior can remain intact for later Developer-feature redesign:

```text
Actual Steam active → Actual
else BPM active      → BigPicture
else Developer Test  → DeveloperTest
else                 → inactive actual
```

That behavior is no longer a product semantic simply because the helper exists in source.

The architectural requirement is:

> no production Runtime/controller/status owner constructs or consumes it.

Do not add an `#if DEBUG` wrapper solely to justify keeping the source.

Do not move it into a new project in this PR.

---

# 11. Keep `DiagnosticSessionTracker` source, disconnect production use

File:

```text
src/SteamInputAddonforClaw/Diagnostics/DiagnosticSession.cs
```

Keep:

```text
DiagnosticSession
DiagnosticSessionTracker
ControllerStateDiagnostics
```

but production `SteamSessionRuntime` must no longer instantiate or feed `DiagnosticSessionTracker` with synthetic effective Steam state.

This preserves useful diagnostic source for later redesign without letting it dictate production state architecture.

Do not alter the physical input diagnostic helpers in this file:

```text
ControllerStateDiagnostics.LogChanges
D-pad transition diagnostics
DirectInput POV diagnostics
analog diagnostics
```

Those are unrelated and remain useful.

---

# 12. Test policy

## 12.1 Keep Developer feature tests

Do not delete tests merely because the corresponding source is temporarily disconnected.

In particular, preserve the focused tests for:

```text
DeveloperTestModeState
EffectiveSteamSessionSource
frontend NamedPipe SetDeveloperTestMode RPC
DeveloperPage behavior where present
DiagnosticSessionTracker pure behavior
```

`EffectiveSteamSessionSourceTests.cs` should remain as the specification for that parked helper unless a compile-only adjustment is unavoidable.

## 12.2 Remove/update production integration tests that assert the legacy coupling

Tests that currently require production Runtime to publish Developer Test as a Steam session must be removed or rewritten.

Known current examples include assertions around:

```text
SteamSessionRuntime.DeveloperTestModeState_transition_is_published_through_the_owned_state_graph
AddonRuntimeHost.SteamSessionStateChanged driven by Developer Test
```

Those tests protect the architecture being removed and must not keep it alive.

## 12.3 Required new/updated tests

Add focused coverage proving the new boundary.

At minimum:

### A. Production runtime has no Developer synthetic-session dependency

Use a focused architecture/source test or equivalent structural assertion proving `SteamSessionRuntime` does not own/reference:

```text
DeveloperTestModeState
EffectiveSteamSessionSource
DiagnosticSessionTracker
```

Do not implement a heavyweight reflection framework for this; follow the repository's existing architecture-test style.

### B. `AddonRuntimeHost` has no synthetic Steam-session surface

Prove there is no production `SteamSessionStateChanged` forwarding path and no `DeveloperTestModeState` property on `AddonRuntimeHost`.

### C. Developer toggle UI/RPC remains functional

Construct `InProcessAddonFrontendControl` with a standalone `DeveloperTestModeState` and verify:

```text
SetDeveloperTestModeAsync(true)
→ returned FrontendDeveloperSnapshot.TestModeEnabled == true

SetDeveloperTestModeAsync(false)
→ returned FrontendDeveloperSnapshot.TestModeEnabled == false
```

This test should not require a live `AddonRuntimeHost`.

### D. Developer toggle cannot select SteamDeck

Preserve/adjust the current presentation-boundary test so the product invariant remains explicit:

```text
Developer Test enabled
+ RunningAppId = 0
+ BPM = false
→ SteamPresentationSnapshot.WantsSteamDeck == false
```

If `SteamSessionRuntime` no longer owns Developer state, prove the stronger architectural fact instead: the standalone Developer state is not an input to `CapturePresentationSnapshot()`.

### E. Actual Steam and BPM still work

Keep/add tests proving:

```text
actual RunningAppId != 0
→ WantsSteamDeck

BPM active with RunningAppId == 0
→ WantsSteamDeck

actual/BPM inactive
→ Xbox360 desired presentation
```

### F. Resume refresh remains safe

Keep the existing power/resume tests from Cleanup H and ensure the Steam runtime refresh path still works after removing the effective-session graph.

Do not weaken:

```text
PowerResumeObserved
resume current-world reconcile
PowerMutationGate fail-close
RecoverySafetyState
```

---

# 13. Explicit out of scope

Do **not** include any of the following in Cleanup I:

```text
DeveloperPage removal
Developer Test toggle removal
FrontendDeveloperSnapshot removal
SetDeveloperTestMode RPC removal
frontend protocol bump
Developer Test redesign/reconnection to Full1902
new developer-only presentation injection API

EffectiveSteamSessionSource deletion
DeveloperTestModeState deletion
DiagnosticSessionTracker deletion
SteamSessionWatcher deletion solely because production no longer constructs it

legacy rumble/FeedbackAuthority cleanup
SteamDeckRumbleFeedbackBridge cleanup
vibration UI redesign
MsiClawRumbleSink / packet primitive cleanup

RoutingTransition enum rename
broad RecoverySafety naming cleanup

Center M authority changes
HidHide changes
PID1901/PID1902 ownership changes
DirectInput changes
VIIPER attach/detach changes
front-button behavior changes
QAM policy changes
power barrier/epoch changes
PnP recovery changes
uninstall changes
profile behavior changes
```

Rumble/feedback and historical naming tails can be handled in a later focused cleanup after this production Developer-session coupling is closed.

---

# 14. Lifecycle invariants that must remain unchanged

Cleanup I is a structural cleanup, not a controller behavior change.

Preserve all supported lifecycle behavior:

```text
Sleep / Hibernate / Resume
Restart / Crash / Shutdown
physical device loss / PnP re-enumeration
PID1901 ↔ PID1902 restoration/reclaim
routing/authority rollback and fail-close where current
HidHide deterministic ownership + teardown
VIIPER ownership / neutral / detach / PendingCleanup handling
actual operation failure handling
```

Especially preserve the PR #483 fix:

```text
Center M Disabled
→ Sleep/Hibernate
→ Resume
→ PowerResumeObserved still fires for the accepted resume cycle
→ current Full1902 owned-controller reconcile runs
→ generic power gate remains fail-closed where required
```

Do not add defensive synchronization for theoretical instruction-level races.

---

# 15. Overengineering guard

This cleanup should make the graph smaller.

Do not replace the removed synthetic production coupling with:

```text
DeveloperSteamSessionAdapter
DeveloperPresentationAuthority
SyntheticSessionManager
SteamFactAggregatorV2
DeveloperRuntimeService
new event bus
new persisted developer state
new authority enum
new state machine
new polling loop
```

Preferred architecture:

```text
production = actual current facts only
Developer code = parked/disconnected
```

A plain standalone `DeveloperTestModeState` for the existing UI is sufficient.

---

# 16. Expected production reference closure after Cleanup I

Fresh-grep at the end.

Production code should have no path equivalent to:

```text
DeveloperTestModeState
→ EffectiveSteamSessionSource
→ SteamSessionRuntime.StateChanged
→ AddonRuntimeHost.SteamSessionStateChanged
→ frontend/runtime production state
```

Expected remaining references are allowed in:

```text
Developer source
Developer UI/frontend state handling
EffectiveSteamSessionSource parked source
pure unit tests for those parked features
historical docs/work orders
```

No current controller owner or presentation owner may consume the Developer Test state.

---

# 17. Likely files to modify

Fresh closure decides the exact final list, but the expected production edit set is small:

```text
src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Likely test adjustments:

```text
tests/SteamInputAddonforClaw.Tests/SteamSessionRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/AddonRuntimeHostTests.cs
tests/SteamInputAddonforClaw.Tests/SteamPresentationSnapshotTests.cs
relevant InProcessAddonFrontendControl / Developer frontend tests
```

Preserve, rather than delete:

```text
src/SteamInputAddonforClaw/Developer/DeveloperTestModeState.cs
src/SteamInputAddonforClaw/Steam/EffectiveSteamSessionSource.cs
src/SteamInputAddonforClaw/Diagnostics/DiagnosticSession.cs
tests/SteamInputAddonforClaw.Tests/EffectiveSteamSessionSourceTests.cs
frontend transport/UI Developer Test code/tests
```

---

# 18. Validation

Required before PR completion:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
```

Use the repository's normal solution/project commands if the exact invocation differs.

Also run:

```text
git diff --check
```

Reference-closure checks must prove:

```text
SteamSessionRuntime does not construct EffectiveSteamSessionSource
SteamSessionRuntime does not own DeveloperTestModeState
SteamSessionRuntime does not own DiagnosticSessionTracker
AddonRuntimeHost does not expose DeveloperTestModeState
AddonRuntimeHost does not expose/forward SteamSessionStateChanged
InProcessAddonFrontendControl does not subscribe to runtime synthetic Steam-session changes
Full1902 presentation still derives from actual RunningAppID + BPM only
DeveloperPage / SetDeveloperTestMode RPC still exist
EffectiveSteamSessionSource source/tests still exist
DiagnosticSessionTracker source/tests still exist
```

Full Release suite must pass.

No frontend protocol bump.

---

# 19. Acceptance criteria

Cleanup I is complete when all of the following are true:

1. Production Full1902 presentation uses only actual RunningAppID + actual BPM.
2. Developer Test cannot become a synthetic production Steam session.
3. `SteamSessionRuntime` no longer owns `DeveloperTestModeState`, `EffectiveSteamSessionSource`, or `DiagnosticSessionTracker`.
4. `AddonRuntimeHost` no longer exposes synthetic Steam-session or Developer Test state.
5. Frontend no longer invalidates production state from `SteamSessionStateChanged`.
6. Existing Developer Test UI remains visible.
7. `SetDeveloperTestModeAsync` and its named-pipe contract remain intact.
8. The UI can still toggle/report `FrontendDeveloperSnapshot.TestModeEnabled` using a standalone Developer state object.
9. `DeveloperTestModeState`, `EffectiveSteamSessionSource`, and `DiagnosticSessionTracker` source remain in the repository for later redesign.
10. Focused tests for those parked Developer helpers remain.
11. Actual Steam/BPM presentation switching remains unchanged.
12. Sleep/Hibernate/Resume behavior from PR #483 remains intact.
13. No controller authority, HidHide, DirectInput, VIIPER, PnP, or teardown behavior is weakened.
14. No replacement manager/authority/state machine is introduced.
15. Debug/Release builds and full Release tests pass.

---

# 20. PR scope summary

The intended diff should read as:

```text
keep Developer feature
keep Developer UI
keep Developer RPC
keep parked helper/test code

remove production synthetic Developer Steam-session wiring

production Steam facts
= actual RunningAppID + actual Big Picture only
```

This PR should be a deletion/disconnection cleanup, not a new architecture project.
