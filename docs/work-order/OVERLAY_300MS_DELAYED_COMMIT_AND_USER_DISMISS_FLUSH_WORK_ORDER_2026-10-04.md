# Work Order — Overlay 300 ms Delayed Commit and User-Dismiss Flush

> Date: 2026-10-04  
> Status: Ready for implementation  
> Scope: One focused PR  
> Repository baseline reviewed: `main` at `5341cb37fd36679ff21784bb5c63285d562a3c5c`  
> Product architecture: Standalone Full1902  
> Target surface: `SteamInputAddonforClaw.Overlay`  
> Primary bug: a user can change a delayed Overlay setting and close/navigate away before debounce expiry, causing the visible edit to be discarded instead of committed

---

## 1. Goal

Fix the real user-visible Overlay commit problem:

~~~text
user changes a delayed setting
-> Overlay preview changes immediately
-> delayed commit is still waiting
-> user presses B / dismisses / leaves the selected Profile detail
-> current code cancels the unsubmitted draft
-> Runtime never receives the user's final value
~~~

At the same time, reduce the current Overlay delayed-commit latency:

~~~text
shared Device/Profile Quick Settings:
2000 ms -> 300 ms

Controller Vibration Strength:
500 ms -> 300 ms
~~~

The final product behavior must be:

~~~text
normal editing
-> immediate local preview
-> last edit + 300 ms
-> one existing Runtime mutation

normal user Back/dismiss before 300 ms
-> do NOT wait out the debounce interval
-> submit the latest pending draft immediately
-> let the existing Runtime mutation settle
-> then send the existing DismissRequested
-> normal OQ4 capture retirement continues

real stale context / target invalidation / teardown
-> cancel obsolete unsubmitted draft
-> do not commit against a stale AppId/context
~~~

This is a timing/policy correction around existing mutation owners.

It is **not** a new settings system, mutation queue, save service, or lifecycle authority.

---

## 2. Non-negotiable architecture rule

Reuse the existing paths only.

For generic Device/Profile Quick Settings:

~~~text
QuickSettingsPageSnapshot
-> OverlayQuickSettingsPageBinding
-> OverlayDelayedSliderCommit
-> QuickSettingsMutationIntent
-> existing Overlay named pipe
-> QuickSettingsMutationAdapter
-> existing production frontend/runtime authority
~~~

For Controller vibration:

~~~text
existing Overlay Controller vibration draft
-> existing ControllerVibrationStrengthEditRequested path
-> NamedPipeOverlayClient.SendControllerVibrationMutationAsync(...)
-> existing IAddonFrontendControl.SetControllerVibrationStrengthAsync(...)
-> existing controller vibration production authority
~~~

Do not create:

~~~text
OverlaySaveManager
OverlayCommitManager
OverlayDebounceManager
OverlayFlushCoordinator
PendingMutationRegistry
generic command bus
cross-feature mutation queue
new Runtime owner
new persistence
new named pipe
new protocol message solely for flush
new epoch/barrier/state machine
~~~

A small field/method inside an existing owner that is required to represent the one pending/in-flight operation is allowed.

Do not generalize unrelated Controller/ClawHUD/Shortcut code to make the diff look symmetric.

---

## 3. Required reading before implementation

Read the current Full1902 authority in the precedence defined by:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

This PR must not alter:

~~~text
Center M Enabled / Disabled authority
PID1901 <-> PID1902 ownership
HidHide ownership
VIIPER ownership
physical controller recovery
Sleep / Hibernate / Resume ownership
virtual presentation routing
fail-close controller lifecycle
~~~

Also read the active shared frontend / Overlay binding references:

~~~text
docs/shared-frontend/SF_V2_07_OVERLAY_GENERIC_DEVICE_RENDERER_BINDING_WORK_ORDER.md
docs/shared-frontend/SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md
docs/work-order/OVERLAY_SHARED_PRODUCTION_CONTROLS_PARITY_WORK_ORDER_2026-10-04.md
~~~

This work order **revises** the old normal-Hide policy that intentionally discarded unsubmitted user drafts.

Historical stale-context cancellation rules remain valid.

---

## 4. Current code facts confirmed at the reviewed baseline

### 4.1 One shared 2000 ms policy currently drives generic Overlay sliders

Current contract:

~~~csharp
public static readonly QuickSettingsCommitPolicy TrailingDebounce2000 =
    new(QuickSettingsCommitMode.TrailingDebounce, 2000);
~~~

The generic Overlay binding already reads:

~~~text
row.CommitPolicy.DelayMilliseconds
~~~

and does not own a separate hard-coded 2000 ms constant.

Therefore the timing change belongs in the existing shared product policy.

Do not add a second Overlay timing constant for Device/Profile.

### 4.2 Current shared delayed rows

At the reviewed baseline, the existing shared `TrailingDebounce2000` policy is used by the following production rows.

#### Device

~~~text
Battery Charge Limit
  Limit %

TDP Control
  AC PL1
  AC PL2
  DC PL1
  DC PL2

CPU Boost
  AC
  DC

Windows Power Mode
  AC
  DC
~~~

#### Profile

~~~text
TDP Control
  AC PL1
  AC PL2
  DC PL1
  DC PL2

CPU Boost
  AC
  DC

Windows Power Mode
  AC
  DC

Intel FPS Limit
  AC
  DC
~~~

These all change together from 2000 ms to 300 ms.

Do not special-case only TDP.

### 4.3 Existing Immediate controls stay Immediate

Do **not** convert these to delayed commit:

~~~text
Device/Profile Enabled toggles
Battery Charge Limit Enabled
TDP Enabled
CPU Boost Enabled
Power Mode Enabled
FPS Limit Enabled
Profile Resolution
M1/M2 mapping
Joystick LED edits
ClawHUD mutations
Tab Order
Shortcut execution
current-power-source toggle
~~~

This PR is not a global "delay every setting" redesign.

### 4.4 Main UI already demonstrates 300 ms for Profile TDP

Current Main UI `ProfilePage.xaml.cs` uses:

~~~csharp
await Task.Delay(300, token);
~~~

for Profile TDP editing.

That does not make Main UI the authority for Overlay commit mechanics, but it is concrete product evidence that 300 ms is already accepted for the same user-facing TDP edit class.

### 4.5 Current Hide deliberately discards the user's draft

Current `OverlayWindow.Presentation.cs`:

~~~csharp
foreach (var surface in _quickSettingsSurfaces.Values)
    surface.Binding?.CancelUnsubmittedDrafts();
~~~

The active comment explicitly says normal Hide drops unsubmitted Device/Profile drafts.

This is the reproduced bug source.

### 4.6 Profile local Back also deliberately discards the draft

Current `OverlayWindow.Profile.cs` selected-detail Back path:

~~~text
SelectedDetail
-> CancelUnsubmittedDrafts()
-> Catalog
~~~

Leaving SelectedDetail for another top-level tab also cancels the Profile draft.

A normal user navigation action must no longer silently discard a valid edit.

### 4.7 Controller vibration has a separate 500 ms timer

Current `OverlayWindow.Controller.cs`:

~~~csharp
await Task.Delay(500, token);
~~~

This path does not use `QuickSettingsCommitPolicy`.

It must be reduced to 300 ms separately, without introducing a common debounce framework solely to share the number.

### 4.8 B/Back handling already returns immediately to the pipe reader

Current `App.HandleNavigationAsync`:

~~~text
Runtime navigation frame
-> enqueue work to DispatcherQueue
-> return Task.CompletedTask
~~~

Root Back then starts `SendBackDismissAsync()` fire-and-forget.

This is important.

A flush that waits for a mutation response **must not be awaited by the named-pipe read-loop navigation callback itself**, because that same read loop receives the correlated mutation result.

The existing reader must remain free to receive mutation responses.

---

## 5. Product policy — 300 ms

Replace the shared policy:

~~~text
TrailingDebounce2000
~~~

with:

~~~text
TrailingDebounce300
~~~

defined once in:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
~~~

Expected shape:

~~~csharp
public static readonly QuickSettingsCommitPolicy TrailingDebounce300 =
    new(QuickSettingsCommitMode.TrailingDebounce, 300);
~~~

Update the comment so it describes the shared compact Quick Settings slider policy, not only old Device behavior.

Replace all production `QuickSettingsPresentation` uses of `TrailingDebounce2000` with `TrailingDebounce300`.

Do not leave a compatibility alias such as:

~~~csharp
TrailingDebounce2000 = TrailingDebounce300;
~~~

There is no need to preserve a misleading policy name in this pre-release codebase.

Tests/fixtures must migrate to the new policy name/value where they represent current production behavior.

Synthetic tests that intentionally construct another delay may continue doing so with an explicit `new QuickSettingsCommitPolicy(...)`.

---

## 6. Normal editing behavior

Preserve the existing trailing debounce semantics.

Required example:

~~~text
20
-> 21
-> 22
-> 23
-> 24
-> 25
   |
   + no more edits for 300 ms
   |
   -> one mutation containing final 25
~~~

For grouped TDP:

~~~text
PL1 edit
-> group draft seeded

PL2 edit inside 300 ms
-> same group draft updated
-> timer restarted
-> one final whole TDP configuration mutation
~~~

Preserve:

- current pending-key model;
- group draft seeding;
- linked PL1/PL2 correction;
- immediate local preview;
- authoritative settlement;
- AppId/context stale checks;
- current Runtime validation.

Do not issue a mutation on every D-pad repeat.

---

## 7. Add immediate flush to the existing delayed-commit helper

Extend:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
~~~

with the smallest awaitable flush seam.

Conceptual contract:

~~~csharp
internal Task FlushAsync();
~~~

Equivalent naming is acceptable.

### 7.1 No pending draft

~~~text
no current draft
-> completed task
-> no mutation
~~~

### 7.2 Unsubmitted current draft

~~~text
current generation is waiting in debounce timer
-> cancel only that timer delay
-> keep the SAME latest intent/generation
-> submit immediately through existing _commitAsync
-> run existing settlement path
-> no duplicate mutation
~~~

Do not create a new `QuickSettingsMutationIntent`.

The exact pending intent already stored by the helper is the one to submit.

### 7.3 Already-submitted current generation

If the current generation is already inside the existing mutation call:

~~~text
FlushAsync()
-> do not submit again
-> return/await the current generation's existing run task
~~~

This prevents:

~~~text
timer expires
-> mutation starts
-> B arrives
-> flush submits same mutation a second time
~~~

A single narrowly scoped current-run Task reference inside `OverlayDelayedSliderCommit` is acceptable if required.

Do not create a global pending-operation tracker.

### 7.4 Existing stale generation behavior stays

A newer slider edit still supersedes an older generation exactly as today.

Flush must not resurrect an older generation.

---

## 8. Add binding-level user flush

Extend:

~~~text
OverlayQuickSettingsPageBinding
~~~

with a narrow method such as:

~~~csharp
internal Task FlushPendingUserEditsAsync();
~~~

Required behavior:

~~~text
take a stable snapshot of the current pending entries
-> flush each current entry through its existing OverlayDelayedSliderCommit
-> no new intent construction
-> no new validation
-> no direct Runtime/hardware call
~~~

Prefer deterministic sequential submission inside one binding rather than launching a burst of parallel writes.

The existing client already serializes Quick Settings mutations, but there is no product benefit in creating parallel flush tasks only to wait on the same mutation gate.

Do not add a queue.

### 8.1 Flush is not Cancel

After this PR the distinction is explicit:

~~~text
FlushPendingUserEditsAsync
= normal user says "keep what I just changed"

CancelUnsubmittedDrafts
= current target/context is no longer valid, or teardown must discard obsolete UI work
~~~

Keep `CancelUnsubmittedDrafts()`.

Do not rename every cancel call to flush mechanically.

---

## 9. Root Back / normal user dismiss

Current root Back:

~~~text
Back
-> TryHandleBack() == false
-> SendBackDismissAsync()
-> DismissRequested
-> Runtime Hide
-> Hide cancels pending draft
~~~

Required:

~~~text
Back
-> TryHandleBack() == false
-> start one asynchronous user-dismiss flow OUTSIDE the pipe read-loop await chain
-> flush valid delayed user edits
-> wait only for actual existing mutation settlement(s), NOT the 300 ms debounce
-> SendDismissRequestedAsync()
-> Runtime unified Overlay retirement
~~~

### 9.1 Do not block the named-pipe reader

Do **not** change `HandleNavigationAsync` into:

~~~csharp
await Flush...
await SendDismiss...
~~~

when that Task is returned to `NamedPipeOverlayClient.RunAsync`.

That would prevent the same reader from receiving the mutation result needed to complete the flush.

Required shape remains conceptually:

~~~text
HandleNavigationAsync
-> enqueue/start async user-dismiss operation
-> return immediately

async user-dismiss operation
-> await mutation result while pipe reader remains active
-> send existing DismissRequested
~~~

No new thread is required.

No `Task.Run` is required solely for this.

### 9.2 Repeated Back while dismiss is pending

Use the smallest existing/app-local admission fact so repeated B presses do not start duplicate flush+dismiss flows.

One simple bool/interlocked "user dismiss in progress" field is acceptable.

Do not build a dismissal queue/state machine.

---

## 10. Outside-click dismiss

Outside-click is also an intentional normal user dismiss.

It must use the same flush-before-`DismissRequested` policy as root B.

Current:

~~~text
OutsideClickDismissRequested
-> SendDismissRequestedAsync
~~~

Required:

~~~text
OutsideClickDismissRequested
-> same one user-dismiss flow
-> flush pending valid edits
-> SendDismissRequestedAsync
~~~

Do not duplicate Back and outside-click flush implementations.

One small existing-App helper for "normal user requested dismiss" is appropriate.

This helper is orchestration inside the existing `App` owner, not a new architectural layer.

---

## 11. Runtime-forced Hide remains cancellation-oriented

Do not turn every incoming Runtime Hide command into a save operation.

Examples that may cause a Hide/retirement outside a normal explicit user dismiss include lifecycle/ownership failure paths.

For a Runtime-issued Hide where the Overlay did **not** first complete the normal user-dismiss flush:

~~~text
HideForPocAsync
-> cancel obsolete unsubmitted generic Quick Settings drafts
-> cancel/retire any local delayed Controller vibration draft
-> continue existing OQ4 retirement
~~~

This preserves fail-close lifecycle behavior.

The Overlay must not commit a possibly stale game Profile merely because the Runtime is forcing the surface away.

This is the key policy split:

~~~text
normal user dismiss
-> FLUSH first, then ask Runtime to dismiss

Runtime/lifecycle forced hide
-> CANCEL unsubmitted local draft
~~~

No additional close-reason protocol field is needed because the normal user path performs its flush **before** sending the already-existing `DismissRequested`.

---

## 12. Profile SelectedDetail -> Catalog Back

Current local Back inside Profile SelectedDetail is a normal user navigation action, not target invalidation.

Change:

~~~text
SelectedDetail(A)
-> CancelUnsubmittedDrafts
-> Catalog
~~~

to:

~~~text
SelectedDetail(A)
-> immediately flush valid pending Profile draft for A
-> after the mutation settles, leave SelectedDetail
-> Catalog
~~~

The catalog transition must not retarget the pending mutation.

The existing AppId carried by the current Profile `QuickSettingsMutationIntent` remains authoritative.

### 12.1 Pipe-reader rule still applies

This flush must not be awaited as the returned `HandleNavigationAsync` Task.

Schedule the async local-Back transition from the UI dispatcher, then return the navigation handler immediately so the pipe reader can receive the mutation result.

### 12.2 Failure behavior

If the valid Profile mutation returns a typed failure or transport failure:

~~~text
do not silently claim success
-> preserve/log the existing local failure behavior
-> do not create another retry manager
~~~

The user may still return to Catalog after the operation settles; this PR does not add a modal save-failed workflow.

The important product rule is that the edit is **attempted**, not intentionally discarded.

---

## 13. Leaving SelectedDetail by LB/RB top-level tab change

Current `OnProfileTabSelectionChanged(false)` cancels a SelectedDetail draft.

That is also a normal user navigation action when triggered by LB/RB.

Do not silently discard the edit.

Required:

~~~text
user leaves Profile SelectedDetail through normal tab navigation
-> force current valid Profile draft into immediate submission
-> tab navigation may remain visually responsive
-> do not cancel that now-submitted current mutation
~~~

The tab switch does not retire Overlay capture, so it is not necessary to block the visual tab transition until settlement.

The key requirement is:

> submit the valid current draft before clearing SelectedDetail context.

Do not make every top-level tab switch globally flush unrelated bindings.

Only fix the current SelectedDetail path that explicitly destroys its own context/draft.

---

## 14. Genuine Profile context change remains cancel

Keep cancellation when the edit target itself has become stale.

Examples:

~~~text
SelectedDetail(A)
-> active game changes / Runtime now establishes B
-> A is no longer the current valid context for this surface
-> cancel/suppress obsolete A local draft as required by existing AppId safety

Profile(A) authoritative context replaced by Profile(B)
-> retire A pending generation
-> never retarget A values to B
~~~

Do not flush an obsolete draft simply because "flush on Back" exists.

Existing `IsStaleForCurrentContext`, AppId comparison, generation retirement, and Runtime adapter validation remain the safety boundary.

No new AppId epoch is required.

---

## 15. Controller Vibration Strength — 500 ms -> 300 ms

Current Controller vibration is a typed special-page path and intentionally remains outside generic `QuickSettingsPageSnapshot`.

Change only its local delayed commit timing:

~~~text
500 ms -> 300 ms
~~~

Keep:

~~~text
Left Motor and Right Motor separate
latest draft preserves sibling value
one whole pair mutation
existing ControllerVibrationSettings authority
existing named-pipe request/result
no Test operation
~~~

Do not migrate Controller vibration into generic Quick Settings solely for this timing change.

Do not create a generic debounce abstraction to share the number 300.

---

## 16. Controller Vibration — normal dismiss flush

Controller vibration has the same practical problem as generic sliders:

~~~text
user changes Left/Right
-> local 500/300 ms timer is pending
-> user immediately dismisses Overlay
-> Runtime retires capture
-> delayed request later arrives too late / is rejected
~~~

Normal user Back/outside-click must therefore force the latest pending vibration pair into the existing mutation path before `DismissRequested`.

### 16.1 Smallest implementation

Keep the existing Controller owner.

A focused implementation may:

1. mark whether the local vibration delay still owns an unsubmitted draft;
2. on normal user dismiss, cancel the delay only;
3. capture the latest `left/right` pair already held by the Controller renderer;
4. submit it immediately through the **existing** `SendControllerVibrationMutationAsync` path;
5. await that one existing mutation before sending `DismissRequested`.

Do not add another vibration DTO, authority, or manager.

### 16.2 Already-in-flight vibration mutation

Do not submit a duplicate pair.

If a vibration mutation has already started, normal user dismiss should wait on that existing one mutation task (or equivalent smallest existing completion seam) before sending `DismissRequested`.

A single App-local reference to the one current vibration send Task is acceptable.

Do not build a general mutation tracker.

### 16.3 Runtime-forced Hide

A Runtime-forced Hide may cancel an unsubmitted local vibration delay instead of applying it.

Do not write controller profile state during lifecycle teardown merely because a draft exists.

---

## 17. What is NOT part of the 300 ms conversion

The following Overlay value-looking controls are not delayed generic sliders and should not be changed merely because they use `OverlayValueRow`:

~~~text
Joystick LED Brightness
Joystick LED RGB
M1/M2 mapping
ClawHUD HUD Size
ClawHUD Opacity
ClawHUD enum/discrete rows
Profile Resolution
~~~

Their existing commit policies stay as implemented.

If a separate real user-visible issue is found in one of those immediate paths, handle it separately rather than broadening this PR.

---

## 18. Hide implementation after the policy change

Keep `HideForPocAsync()` non-blocking with respect to debounce.

It must **not** sleep 300 ms.

Normal user dismiss already flushes before Runtime sends Hide.

Therefore incoming Hide does only lifecycle cleanup:

~~~text
_isVisible = false
-> cancel any still-unsubmitted generic Quick Settings draft
-> cancel any still-unsubmitted Controller vibration delay
-> disarm outside-click
-> existing hide animation
-> native Hide
~~~

Already-submitted Runtime operations are not re-submitted.

Do not move mutation waiting into `HideForPocAsync()`.

This also avoids the named-pipe read-loop deadlock risk.

---

## 19. Transport / protocol

No wire-shape change is required.

Reuse:

~~~text
QuickSettingsMutationRequest / Result
ControllerVibrationMutationRequest / Result
DismissRequested
~~~

Expected versions remain the then-current values:

~~~text
FrontendTransportProtocol.CurrentVersion = 49
OverlayTransportProtocol.CurrentVersion  = 14
~~~

Do not bump protocol versions only because:

- debounce changed from 2000 to 300;
- an existing mutation is sent earlier;
- App waits for an existing correlated result before sending `DismissRequested`.

If implementation genuinely changes the serialized wire shape, stop and justify it rather than silently bumping.

---

## 20. Failure semantics

### Generic Quick Settings flush

If flush returns a normal typed `QuickSettingsMutationResult` failure:

~~~text
existing authoritative result page remains the truth
existing failure message path remains the truth
dismiss may continue after settlement
~~~

Do not retry automatically.

### Transport/operation failure

~~~text
log existing bounded failure
do not fabricate success
do not persist a second local copy
normal user dismiss may continue after the failed attempt
~~~

The purpose is to stop **intentional draft loss**, not to make Overlay impossible to close whenever a setting write fails.

### Stale target

Existing AppId/current-page safety wins.

Never force a stale Profile mutation just to satisfy flush semantics.

---

## 21. Tests — shared 300 ms policy

Update focused contract/product tests to prove:

1. production delayed Quick Settings rows use:
   ~~~text
   Mode = TrailingDebounce
   DelayMilliseconds = 300
   ~~~
2. no production `TrailingDebounce2000` remains;
3. immediate rows remain Immediate;
4. synthetic non-300 delay payloads are still honored by the generic binder;
5. the Overlay binder still reads `policy.DelayMilliseconds` instead of hard-coding 300.

Do not make CI literally sleep 300 ms.

Use the existing injected delay seam.

---

## 22. Tests — delayed helper flush

Add focused tests for `OverlayDelayedSliderCommit`.

### 22.1 Flush before timer

~~~text
Schedule(A, 300ms)
-> FlushAsync immediately
-> exactly one A mutation
-> no second mutation when original timer would have expired
~~~

### 22.2 Latest draft wins

~~~text
Schedule(A)
Schedule(B)
FlushAsync
-> exactly B submits
-> A never submits
~~~

### 22.3 Already in flight

~~~text
timer causes A submission
FlushAsync while A is in flight
-> no duplicate A
-> FlushAsync completes from the current A operation
~~~

### 22.4 No draft

~~~text
FlushAsync
-> no call
-> completes
~~~

### 22.5 Stale generation

Existing stale-generation suppression remains passing.

---

## 23. Tests — binding flush

Add to `OverlayQuickSettingsPageBindingTests`.

At minimum:

### 23.1 Profile TDP root-dismiss-equivalent flush

~~~text
Profile AppId=A
edit ProfileTdpAcPl1
do not release fake 300ms delay
FlushPendingUserEditsAsync
-> one Profile mutation immediately
-> intent AppId=A
-> whole ProfileTdpConfiguration group preserved
~~~

### 23.2 Device TDP flush

Same behavior for Device TDP group.

### 23.3 Independent slider flush

Use one non-grouped row such as CPU Boost / Power Mode / Battery Limit / FPS fixture.

~~~text
edit
flush
-> one final current value
~~~

### 23.4 Context change still cancels

~~~text
Profile A draft
-> authoritative context changes to B
-> old A draft retired
-> flush current binding
-> A does not submit
~~~

Do not weaken stale-context safety.

---

## 24. Tests — user dismiss orchestration

Add focused source/behavior coverage for `App` / Overlay user-dismiss flow.

Prove:

~~~text
Back root
-> starts flush+dismiss outside navigation-handler await chain

outside-click
-> uses the same flush+dismiss flow

DismissRequested
-> sent only after pending valid delayed edit mutation has settled

named-pipe reader remains available to receive mutation result
~~~

Also prove repeated user dismiss requests do not create duplicate dismiss/flush operations.

Do not invent a fake production close manager solely for testing.

---

## 25. Tests — Profile navigation

Cover:

### SelectedDetail -> Catalog

~~~text
pending Profile TDP draft
-> B
-> mutation submitted for selected AppId
-> Catalog transition follows
-> draft is not silently cancelled
~~~

### SelectedDetail -> another top-level tab

~~~text
pending Profile draft
-> LB/RB leaves Profile
-> current draft is submitted before SelectedDetail context is cleared
-> no retarget to another AppId
~~~

### Genuine context replacement

Still cancels/suppresses obsolete work.

---

## 26. Tests — Controller vibration

At minimum prove:

1. normal delayed value is 300 ms rather than 500 ms;
2. rapid Left/Right edits still collapse to the latest pair;
3. normal user dismiss while delay is pending submits exactly one latest pair immediately;
4. already-in-flight pair is not duplicated;
5. Runtime-forced Hide cancels an unsubmitted local vibration delay rather than applying it;
6. no Test operation is introduced;
7. existing controller authority / persistence tests remain unchanged.

---

## 27. Real lifecycle cases to preserve

This timing fix must not weaken real supported lifecycle behavior.

### Sleep / Hibernate / Resume

No new Overlay save/retry authority.

Existing Runtime owners reconcile hardware state.

### Physical device loss / PnP re-enumeration

Do not force Controller vibration writes during forced Hide/recovery.

Existing PID1902 ownership remains authoritative.

### Restart / Crash / Shutdown

No attempt to synchronously save arbitrary UI draft during process crash/teardown.

Persisted production owners remain the source of truth.

### Routing rollback / fail-close

No changes.

### Center M authority

No changes.

---

## 28. Expected production files

Likely focused changes:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs

src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
~~~

Expected tests:

~~~text
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDelayedSliderCommitTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQuickSettingsPageBindingTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/... focused dismiss/navigation tests as appropriate
~~~

Update active architecture comments/docs that still describe normal user Hide as intentionally discarding edits.

Do not rewrite every historical work order.

---

## 29. Explicit non-goals

Do not:

- modify Main UI debounce behavior;
- change TDP hardware policy/ranges;
- change game Profile persistence schema;
- change QuickSettingsMutationAdapter feature validation;
- change power-source visibility;
- change Overlay visual layout;
- add save confirmation UI;
- add a spinner/modal that blocks the whole Overlay;
- add automatic retries;
- make Hide sleep 300 ms;
- make every mutation globally transactional;
- add cross-feature ordering beyond what is required for current pending user edits;
- redesign Controller typed special pages;
- touch QamHost/CEF historical code.

---

## 30. Acceptance criteria

The PR is complete only when all are true.

1. All production shared Device/Profile delayed Quick Settings rows use 300 ms.
2. No current production `TrailingDebounce2000` policy remains.
3. TDP remains grouped and commits one final whole configuration.
4. Immediate toggles/resolution remain Immediate.
5. Controller vibration delayed commit is 300 ms.
6. Generic delayed edits still preview immediately.
7. Root B with an unsubmitted valid Device/Profile draft flushes it immediately before dismiss.
8. Outside-click dismiss does the same.
9. Profile SelectedDetail Back no longer silently discards a valid draft.
10. Leaving SelectedDetail through normal LB/RB navigation no longer silently discards a valid draft.
11. Controller vibration pending draft is committed on normal user dismiss.
12. Already-submitted current mutations are never duplicated by flush.
13. Runtime-forced Hide still cancels unsubmitted local drafts instead of forcing possibly stale writes.
14. Genuine Profile AppId/context changes still retire obsolete drafts.
15. `HideForPocAsync()` does not wait 300 ms.
16. The named-pipe read loop is never blocked waiting for a mutation result that it must itself receive.
17. Existing wire message kinds are reused; no protocol bump unless wire shape actually changes.
18. No new manager/coordinator/registry/queue/state authority is introduced.
19. Full1902 ownership/lifecycle behavior is unchanged.
20. Tests pass.

---

## 31. Review guardrail

The original failure is not theoretical.

It is directly reachable in normal handheld use:

~~~text
adjust TDP with D-pad
-> immediately press B to return to game
-> current 2-second draft is explicitly cancelled
-> visible setting never reaches Runtime
~~~

Fix that concrete path.

Do not expand the PR into speculative race defense.

The desired end state is simple:

> 300 ms while the user is still editing; immediate commit when the user intentionally leaves; cancel only when the target/context is genuinely no longer valid.
