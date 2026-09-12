# SF-V2-09 — Overlay Profile Publication + Generic Binding

> **Date:** 2026-09-12  
> **Status:** Implementation work order  
> **Reviewed production baseline:** `main` at `3b127d7da26fdc89c45ebc5204b1a2d36b61a7c2` after PR #509 / SF-V2-08  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **Roadmap authority:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Full 1902 authority:** `docs/Full 1902 Implementation/`  
> **Previous milestone:** SF-V2-08 complete — shared Profile projection/dispatch + QAM generic Profile migration  
> **Target milestone:** close Shared Frontend V2 Profile parity by binding the same shared Profile page into Overlay

---

## 1. Goal

Finish Shared Frontend V2 by making the existing Overlay **Profile** tab consume the exact same shared Quick Settings Profile product that QAM now consumes.

The target ownership is:

```text
Runtime active-game/profile authority
        ↓
IAddonFrontendControl.CaptureQuickSettingsPageAsync(Profile, AppId)
        ↓
QuickSettingsPresentation.BuildProfile(...)
        ↓
QuickSettingsPageSnapshot(Profile)
        ↓
existing .Overlay v7 QuickSettingsPageState transport
        ↓
existing Overlay generic Quick Settings binder/renderer
        ↓
Profile tab
```

Mutation direction:

```text
Overlay Toggle / Slider
        ↓
QuickSettingsMutationIntent(Profile, AppId, ...)
        ↓
existing .Overlay v7 mutation request/result
        ↓
AddonProcessHost Overlay admission
        ↓
IAddonFrontendControl.MutateQuickSettingAsync(...)
        ↓
QuickSettingsMutationAdapter
        ↓
existing typed Game Profile mutation methods
```

After this PR:

```text
QAM Device    = shared Quick Settings
QAM Profile   = shared Quick Settings
Overlay Device  = shared Quick Settings
Overlay Profile = shared Quick Settings
```

There must be **no second Overlay-specific Profile product definition**.

This is a binding/publication PR, not a Profile redesign.

---

## 2. Non-negotiable architecture

Keep the current authority hierarchy:

```text
Runtime feature/profile persistence = state + mutation authority
Shared Quick Settings projection     = product-definition authority
QAM                                  = Steam renderer / QAM admission
Overlay                              = WinUI renderer / OQ4 lifecycle admission
Main UI                              = independent management UI
```

Do **not** create:

- a second Profile store/model;
- an Overlay Profile ViewModel hierarchy;
- a Profile-specific renderer;
- a Profile-specific mutation dispatcher;
- a page registry/service/manager;
- a generic form engine;
- a new scheduler service;
- a new polling loop;
- a new controller authority;
- a new lifecycle epoch/barrier/state machine;
- a new cross-surface authority.

Reuse the existing shared contracts, central Profile mutation adapter, Overlay v7 wire, OQ4 capture lifecycle, and SF-V2-07 row primitives/binder.

A **small private page-surface state holder inside `OverlayWindow`** is acceptable if it directly removes Device/Profile duplication. It must not become a global manager/framework.

---

## 3. Full 1902 / OQ4 lifecycle constraints

This PR must not alter controller ownership or physical lifecycle behavior.

Production diff must not change:

```text
PID1901 ↔ PID1902 ownership
HidHide ownership/recovery
VIIPER ownership/teardown
physical DirectInput ownership
SteamDeck/Xbox360 presentation switching
OQ4 neutral capture ordering
OverlayControllerInputRouter ownership
Sleep / Hibernate / Resume
Restart / Crash / Shutdown recovery
PnP re-enumeration recovery
routing rollback / fail-close
```

Preserve the current OQ4 Show ordering:

```text
hold _visibleSurfaceTransition
→ retire Main UI if needed
→ require physical/presentation prerequisites
→ Overlay Show / Visible acknowledgement
→ PauseForOverlayAsync
→ prove publisher stopped/neutral
→ start OverlayControllerInputRouter
→ _overlayCaptureActive = true
→ capture committed
→ ONLY THEN fire-and-forget Quick Settings publication
```

The new Profile capture/publication must never delay or become a prerequisite for neutral controller capture.

Hide/Dismiss/Back/outside click must never wait for a 2-second slider debounce or page capture.

---

## 4. Reviewed baseline — current code is still Device-only in Overlay

The reviewed baseline is `main` at:

```text
3b127d7da26fdc89c45ebc5204b1a2d36b61a7c2
```

### 4.1 Current protocol versions

Keep:

```text
FrontendTransportProtocol.CurrentVersion = 28
OverlayTransportProtocol.CurrentVersion  = 7
```

The v7 Overlay wire already carries:

```text
QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
PageId
AppId
```

No new wire shape is required for Profile.

### 4.2 Current Runtime publication gap

`AddonProcessHost` currently binds only Device capture:

```csharp
_overlayController.BindQuickSettingsAuthority(
    captureDevicePage: token =>
        _frontendControl!.CaptureQuickSettingsPageAsync(
            QuickSettingsPageId.Device,
            appId: null,
            token),
    mutate: (intent, token) =>
        HandleOverlayQuickSettingsMutationAsync(intent, token));
```

`OverlayProcessController` currently owns:

```text
_captureQuickSettingsPage
_deviceRefreshGate
RefreshDeviceQuickSettingsAsync()
```

and captures/publishes only:

```text
QuickSettingsPageId.Device
```

### 4.3 Current Runtime mutation gap

`HandleOverlayQuickSettingsMutationAsync(...)` currently contains the intentional SF-V2-06 limitation:

```text
Profile is not exposed/admitted to Overlay
intent.PageId must be Device
```

SF-V2-09 must remove that historical Device-only exposure restriction while preserving normal Overlay lifecycle admission.

### 4.4 Current Overlay UI gap

`OverlayWindow` currently has one Device-only binding/render state:

```text
_deviceBinding
_devicePageContent
_deviceFailureText
_deviceToggleRows
_deviceSliderRows
_deviceRowShape
```

`ConfigureQuickSettings(...)` constructs only:

```text
OverlayQuickSettingsPageBinding(QuickSettingsPageId.Device, ...)
```

`ApplyQuickSettingsPage(...)` explicitly ignores any non-Device page.

`BuildPage(...)` gives Device the shared renderer but leaves Profile as the generic placeholder page.

Therefore the user's expectation is correct: **Profile is not currently wired into Overlay at all.**

### 4.5 Current binder is page-generic in mechanics, but Device-only in lifecycle assumptions

`OverlayQuickSettingsPageBinding` already correctly owns:

- authoritative page;
- independent row pending drafts;
- commit-group pending drafts;
- shared linked-slider constraint application;
- immediate Toggle submission;
- `row.CommitPolicy.DelayMilliseconds` trailing delay;
- typed failure/result settlement;
- transport/operation failure as a local message;
- Hide cancellation of unsubmitted drafts.

But it currently assumes one static Device context. It does **not yet** protect a changing Profile `(PageId, AppId)` context.

SF-V2-09 must add that narrow context safety before using the binder for Profile.

---

## 5. QAM is the behavioral reference for Profile interaction policy

SF-V2-08 made QAM Device/Profile use one shared renderer. Overlay must match its **product interaction semantics**, not its React implementation details.

The current QAM reference behavior is:

```text
Toggle
→ immediate mutation

Slider
→ immediate local preview
→ trailing debounce from row.commitPolicy.delayMilliseconds
→ currently 2000 ms

Independent slider
→ one-row pending draft

TDP grouped slider
→ one whole-section/group pending draft
→ linked PL1/PL2 correction applied immediately
→ one trailing grouped mutation

same-context authoritative refresh
→ still-valid writable pending draft remains visible
→ unrelated rows adopt fresh authority

fresh page makes edited row absent/non-writable
→ retire that stale pending draft

(PageId, AppId) context change
→ retire old unsubmitted pending work

late old-context mutation result
→ ignore for visible state
```

Overlay must follow these same semantics using the existing C# binder and WinUI row primitives.

Do not copy QAM JavaScript tables or feature strings into Overlay.

---

## 6. Visible Profile scope is already frozen by SF-V2-08

Overlay must render whatever `QuickSettingsPresentation.BuildProfile(...)` publishes.

Current shared Profile product is exactly:

```text
ProfileGeneral
  ProfileEnabled

ProfileTdp                         // only when snapshot.Limits exists
  ProfileTdpEnabled
  ProfileTdpAcPl1                  // when saved TDP feature enabled
  ProfileTdpAcPl2
  ProfileTdpDcPl1
  ProfileTdpDcPl2

ProfileCpuBoost
  ProfileCpuBoostEnabled
  ProfileCpuBoostAc                // when saved CPU Boost feature enabled
  ProfileCpuBoostDc

ProfilePowerMode                   // only when snapshot.PowerMode exists
  ProfilePowerModeEnabled
  ProfilePowerModeAc               // when saved Power Mode feature enabled
  ProfilePowerModeDc
```

Do **not** expose in this PR:

```text
Intel FPS Limit
Resolution
Fan Control
Battery Charge Limit
LED
Vibration strength
Center M authority
Developer controls
```

No Profile labels/options/ranges/order may be reconstructed in Overlay.

---

## 7. Runtime publication — publish both Device and Profile while Overlay is captured

The roadmap/architecture explicitly prefers publishing both low-rate pages instead of adding page-selection IPC or polling.

Target:

```text
Overlay Visible + OQ4 capture active
        ↓
RefreshQuickSettingsAsync()
        ↓
Device page capture + send
        ↓
Profile page capture + send
```

### 7.1 Keep publication event-driven

Publication triggers remain only:

1. successful OQ4 Show/capture commit;
2. relevant Runtime `StateInvalidated` while Overlay is visible/captured;
3. the narrow active-game-change backstop described in section 10 when a generic invalidation is intentionally suppressed during an Overlay mutation.

No timer.

No polling.

No "publish selected tab only" request.

### 7.2 Preferred narrow authority binding

Extend the current binding minimally, conceptually:

```csharp
internal void BindQuickSettingsAuthority(
    Func<CancellationToken, Task<QuickSettingsPageSnapshot>> captureDevicePage,
    Func<CancellationToken, Task<QuickSettingsPageSnapshot>> captureProfilePage,
    Func<QuickSettingsMutationIntent, CancellationToken, Task<QuickSettingsMutationResult>> mutate)
```

This is preferred over adding a page registry/dictionary/service.

There are exactly two current shared pages.

A generic `capture(pageId, appId)` delegate is also acceptable if it makes the code materially simpler, but do not introduce a page-provider abstraction solely for two calls.

### 7.3 Profile capture uses current Runtime active-game authority

Bind Profile capture from `AddonProcessHost` using the existing actual-running-AppId authority and existing generic frontend seam.

Conceptually:

```csharp
private Task<QuickSettingsPageSnapshot> CaptureOverlayProfilePageAsync(
    CancellationToken token)
{
    var appId = _runtimeHost?.ActualRunningAppId ?? 0;
    if (appId == 0)
    {
        return Task.FromResult(
            QuickSettingsPageSnapshot.Unavailable(
                QuickSettingsPageId.Profile,
                appId: null,
                message: "No active game."));
    }

    return _frontendControl!.CaptureQuickSettingsPageAsync(
        QuickSettingsPageId.Profile,
        appId,
        token);
}
```

Important:

- do not read `ProfileStore` directly;
- do not scan games in `OverlayProcessController` or Overlay UI;
- do not construct `FrontendGameProfileSnapshot` manually;
- reuse `CaptureQuickSettingsPageAsync(Profile, appId)` so its current-target revalidation and display-name enrichment remain authoritative;
- if the active game changes during capture, fail closed through the existing capture path and let the next event-driven refresh converge.

Do not add race machinery for a single instruction-level AppId change between reads.

### 7.4 No active game

When there is no active Steam game:

```text
Device page  → normal Device shared page
Profile page → Unavailable(Profile, no AppId, "No active game.")
```

The Profile tab remains visible.

Do not:

- hide the Profile tab;
- show Device rows inside Profile;
- auto-switch tabs;
- create a fake Profile for AppId 0.

### 7.5 Rename/generalize the refresh gate, not its ownership

Current:

```text
_deviceRefreshGate
RefreshDeviceQuickSettingsAsync()
```

Target conceptually:

```text
_quickSettingsRefreshGate
RefreshQuickSettingsAsync()
```

Keep one gate for the whole small publication batch.

Reason:

```text
Show refresh
+
StateInvalidated refresh
```

must not interleave page batches unpredictably on the same Overlay connection.

This is transport publication serialization only, not a feature transaction/state manager.

### 7.6 Capture/send each page independently

One page failure must not suppress the other page.

Required behavior:

```text
Device capture throws
→ log
→ publish Unavailable(Device)
→ still capture/publish Profile

Profile capture throws
→ Device remains published
→ log
→ publish Unavailable(Profile)
```

Before/at every send, preserve the existing live:

```text
Ready + Visible
```

re-check through `SendQuickSettingsPageStateAsync(...)`.

If the session became hidden, stop intentional further publication for that refresh.

### 7.7 Publication order

Use a stable simple order:

```text
Device
Profile
```

No product meaning is attached to that order; it only makes tests/logs deterministic.

---

## 8. OQ4 Show ordering remains unchanged

Current host behavior after capture commit includes:

```csharp
_ = _overlayController.RefreshDeviceQuickSettingsAsync();
```

Replace that with the generic two-page refresh:

```csharp
_ = _overlayController.RefreshQuickSettingsAsync();
```

Do **not** move it earlier.

The page projection/capture remains best-effort **after** `_overlayCaptureActive = true` and neutral input capture is committed.

A slow Profile catalog-name enrichment/capture must never delay Overlay capture safety.

---

## 9. Runtime StateInvalidated publication

Keep the existing Runtime event path:

```text
_frontendControl.StateInvalidated
→ OnFrontendStateInvalidatedForOverlay
```

Admission remains:

```text
not shutting down
AND _overlayCaptureActive
AND Overlay visible
```

Then:

```text
fire-and-forget RefreshQuickSettingsAsync()
```

Keep the existing `_overlayQuickSettingsMutationInFlight` self-invalidation suppression. It is still useful because a typed mutation raises its own invalidation before returning its authoritative mutation result.

Do not let a redundant same-mutation refresh race the result page/failure message.

---

## 10. Real active-game change during an Overlay mutation — do not lose the Profile context refresh

SF-V2-06 could safely drop every `StateInvalidated` observed while `_overlayQuickSettingsMutationInFlight != 0` because Overlay exposed only the static Device context.

SF-V2-09 adds a real normal lifecycle:

```text
Overlay mutation is in flight
→ Steam game starts / exits / switches A → B
→ ActualRunningAppIdChanged raises StateInvalidated
→ generic invalidation handler sees mutation-in-flight and intentionally suppresses it
→ without a backstop, Overlay Profile can remain on the old AppId indefinitely
```

This is **not** a theoretical scheduler race; game start/exit/switch is normal product behavior.

Use the smallest fix. Do not add an epoch/state machine.

Preferred shape:

```csharp
private async Task<QuickSettingsMutationResult> HandleOverlayQuickSettingsMutationAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken token)
{
    // existing lifecycle admission...

    var activeAppIdBefore = _runtimeHost?.ActualRunningAppId ?? 0;
    Interlocked.Increment(ref _overlayQuickSettingsMutationInFlight);
    try
    {
        return await _frontendControl!
            .MutateQuickSettingAsync(intent, token)
            .ConfigureAwait(false);
    }
    finally
    {
        Interlocked.Decrement(ref _overlayQuickSettingsMutationInFlight);

        var activeAppIdAfter = _runtimeHost?.ActualRunningAppId ?? 0;
        if (activeAppIdAfter != activeAppIdBefore &&
            Volatile.Read(ref _processShutdownStarted) == 0 &&
            _overlayCaptureActive &&
            _overlayController.IsVisible)
        {
            _ = _overlayController.RefreshQuickSettingsAsync();
        }
    }
}
```

Exact code may differ.

The important invariant is:

```text
active AppId changed while generic invalidation was suppressed
→ request one post-mutation current-page refresh
```

Do not add a global deferred-refresh queue/counter unless current code proves the simple before/after AppId check cannot satisfy the lifecycle.

This check applies to **both Device and Profile mutations**, because a game may start while a Device mutation is in flight.

---

## 11. Profile mutation admission — remove only the historical Device-only restriction

Current Runtime admission already has the right surface boundary:

```text
Overlay server Ready + Visible   // transport/server
AND _overlayCaptureActive        // Runtime OQ4 authority
AND not shutting down
```

Keep it.

Remove the SF-V2-06 rule:

```text
intent.PageId must be Device
```

Allow:

```text
Device
Profile
```

Then dispatch through exactly:

```csharp
_frontendControl.MutateQuickSettingAsync(intent, token)
```

Do not duplicate Profile validation in `AddonProcessHost`.

`QuickSettingsMutationAdapter` already owns:

- Profile AppId required/non-zero;
- current active target validation;
- current projected row existence/availability/writability;
- strict independent toggle/slider shape;
- enum validation;
- exact five-entry Profile TDP group validation;
- typed mutation dispatch;
- authoritative result page.

### 11.1 Stale AppId is a normal typed failure

Example:

```text
Overlay Profile(A) visible
→ game changes to B
→ old A action reaches Runtime
```

Expected:

```text
zero mutation of B
QuickSettingsMutationResult.Succeeded = false
old A result context does not replace B UI
```

Do not silently retarget the intent to B.

---

## 12. Overlay App remains transport owner and becomes page-generic

`App.xaml.cs` already owns `NamedPipeOverlayClient` and forwards a narrow mutation delegate into the Window.

Keep that architecture.

### 12.1 Configure once, two page bindings

Keep one narrow delegate:

```csharp
_window.ConfigureQuickSettings(
    intent => _client.SendQuickSettingsMutationAsync(intent));
```

`OverlayWindow` may create one Device binding and one Profile binding from the same delegate.

Do not give either binding the transport client.

### 12.2 Accept both pages

`HandleQuickSettingsPageAsync(...)` is already wire-generic. Update comments/log categories from Device-only wording to generic Quick Settings wording where useful.

Forward:

```text
Device page
Profile page
```

to `OverlayWindow.ApplyQuickSettingsPage(page)`.

Undefined/unsupported page IDs are already rejected by the v7 structural wire contract; do not invent a second dynamic page system.

---

## 13. OverlayWindow — convert the existing Device renderer into one Device/Profile renderer

Do not write a second copy of the SF-V2-07 renderer.

Current Device-only fields/methods should be generalized only as much as needed.

Conceptual target:

```text
Device tab  ─┐
             ├─ one generic Quick Settings row renderer
Profile tab ─┘
```

### 13.1 Small local surface state holder is acceptable

A private/dumb holder can group the state that is currently repeated as Device fields, for example:

```csharp
private sealed class QuickSettingsSurface
{
    internal required QuickSettingsPageId PageId { get; init; }
    internal required OverlayTabId TabId { get; init; }
    internal required OverlayQuickSettingsPageBinding Binding { get; init; }
    internal required StackPanel Content { get; init; }
    internal required TextBlock FailureText { get; init; }
    internal Dictionary<QuickSettingsRowId, OverlayToggleRow> ToggleRows { get; } = new();
    internal Dictionary<QuickSettingsRowId, OverlaySliderRow> SliderRows { get; } = new();
    internal QuickSettingsRowShape[]? RowShape { get; set; }
}
```

Exact shape/name is optional.

This is acceptable because it replaces parallel Device/Profile renderer fields. It must remain Window-local and behavior-free except simple state grouping.

Do **not** create `QuickSettingsSurfaceManager`, service interfaces, factories, registries, or DI abstractions.

### 13.2 Build both pages through the same shell path

`BuildPage(...)` target:

```text
Setting  → existing tab-order editor
Shortcut → existing shortcut grid
Device   → BuildQuickSettingsPage(Device)
Profile  → BuildQuickSettingsPage(Profile)
others   → existing placeholder
```

Controller remains placeholder in this PR.

### 13.3 Remove the Profile placeholder

After this PR, `OverlayTabId.Profile` must no longer use `CreatePlaceholderPage(Profile)`.

When no game is active, it still uses the generic Profile Quick Settings page shell, but displays the page's unavailable message and zero selectable rows.

---

## 14. Generic renderer rules

Generalize the current Device methods conceptually:

```text
RenderDevicePage
→ RenderQuickSettingsPage(surface)

RebuildDeviceContent
→ RebuildQuickSettingsContent(surface, page)

UpdateDeviceRowValues
→ UpdateQuickSettingsRowValues(surface, page)

TryCreateDeviceRow
→ TryCreateQuickSettingsRow(surface, row)

CreateDeviceToggleRow
→ CreateQuickSettingsToggleRow(surface, row)

CreateDeviceSliderRow
→ CreateQuickSettingsSliderRow(surface, row)

SubmitDeviceToggleAsync
→ SubmitQuickSettingsToggleAsync(surface, rowId, desired)

ScheduleDeviceSlider
→ ScheduleQuickSettingsSlider(surface, rowId, desired)
```

Exact renames are not mandatory. One implementation path must serve both pages.

### 14.1 Row rendering remains metadata-driven

Continue mapping only:

```text
QuickSettingsControlKind.Toggle
→ OverlayToggleRow

QuickSettingsControlKind.Slider
→ OverlaySliderRow
```

All of these continue to come from the payload:

- label;
- section order;
- row order;
- available/writable;
- current value;
- numeric min/max/step/suffix;
- discrete options/value mapping;
- commit policy;
- commit group;
- linked constraints.

### 14.2 Discrete slider rule stays unchanged

Product value and slider index are different concepts.

```text
option.Value = product value
array index   = WinUI presentation position
```

Never assume CPU Boost/Power Mode enum value equals UI index.

### 14.3 Malformed/unknown row remains fail-closed

Keep `QuickSettingsRowRendering.IsWellFormed(...)` behavior.

A malformed row:

```text
→ not rendered
→ not selectable
→ cannot emit mutation
```

Do not make Profile more permissive than Device.

---

## 15. Profile section/header behavior

Render sections exactly in shared payload order.

The Profile General section label is already the game display name/fallback from `BuildProfile`:

```text
DisplayName
or
Game {AppId}
```

Overlay should render that section label using the same existing section-heading presentation used by the generic Device renderer.

Do not add a second hard-coded game-title header.

Do not add a hard-coded `Profile` product table.

---

## 16. Explicit unavailable Profile state

For:

```text
no active game
stale capture
capture failure
```

Profile must show a deliberate unavailable state.

Requirements:

- Profile tab remains visible;
- page message is visible when useful;
- zero Quick Settings rows are selectable;
- no mutation can be emitted;
- Device state is not reused;
- old game title/rows do not remain on screen as if current.

If Profile becomes unavailable while its tab is selected, selection becomes empty/normalized through the existing row-selection model.

---

## 17. Binder must become context-safe for Profile

This is the most important SF-V2-09 refinement to `OverlayQuickSettingsPageBinding`.

Device has static context:

```text
(Device, null)
```

Profile changes during normal use:

```text
(Profile, AppId A)
→ (Profile, AppId B)
→ (Profile, no target)
```

Treat context identity as:

```text
(PageId, AppId)
```

Do not add an epoch.

### 17.1 Validate expected page identity

A binding created for Device must not accept a Profile page and vice versa.

Use the constructor's existing expected `QuickSettingsPageId` as the narrow invariant.

Unexpected page identity should fail closed/ignore with a diagnostic rather than corrupting that tab's state.

### 17.2 Context change retires old local pending work

When an accepted page changes AppId context:

```text
Profile(A) → Profile(B)
Profile(A) → unavailable/no target
unavailable/no target → Profile(B)
```

retire the old context's pending local work.

Because Overlay uses **separate Device/Profile binder instances**, there is no need to copy QAM's global `(PageId, AppId, RowId|GroupId)` pending-key string scheme literally.

The simpler Overlay shape is preferred:

```text
one binder = one PageId
AppId changes inside Profile binder
→ dispose/clear old pending entries
```

Disposing an old entry is valid here:

- an unsubmitted delay is canceled;
- an already-submitted Runtime request is not magically canceled;
- its local settlement callback is suppressed;
- central Runtime mutation remains explicitly tied to its original AppId.

This gives the same product safety with less state.

### 17.3 Late immediate result must not overwrite a new game

Current immediate toggle path applies its returned page unconditionally.

Fix for Profile reuse:

```text
submit Profile(A) toggle
→ game changes to B
→ fresh Profile(B) page arrives
→ old A toggle returns
→ ignore A result for visible Profile state
```

Compare returned page context with the binding's current context before applying it.

Do not surface old A failure text on B.

### 17.4 Late delayed result must not overwrite a new game

Apply the same context rule in `OnSettled(...)`.

The existing generation check still protects newer edits **inside the same context**.

Context identity protects a different AppId.

Both are needed; do not replace one with the other.

---

## 18. Same-context authoritative refresh must prune invalid pending drafts

Copy the **behavior**, not the JavaScript implementation, of the QAM fix merged in PR #509.

When a fresh authoritative page for the same context arrives, inspect each pending entry's edited row.

If the row is:

```text
still present
AND Available == true
AND Writable == true
```

keep the pending draft visually authoritative.

Otherwise:

```text
cancel/retire that pending entry
```

This is required for the normal Profile lifecycle:

```text
edit Profile CPU/TDP/Power slider
→ before debounce settles, turn Profile master OFF
→ returned Profile page makes all subfeature rows non-writable
→ old child drafts are retired
→ no stale preview
→ no avoidable delayed "This row is not editable" failure later
```

Do this generically from row metadata.

Do **not** special-case:

```text
ProfileEnabled
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
```

The central `QuickSettingsMutationAdapter` writability check remains the final Runtime backstop.

### 18.1 Where pruning must run

Run it before installing a fresh same-context authoritative page from:

1. Runtime page publication (`ApplyAuthoritativePage`);
2. immediate mutation result;
3. delayed mutation result.

For a different context, retire the whole old context first instead.

---

## 19. Local failure-message lifecycle

Keep one local failure fact per page binding.

Required:

```text
typed mutation failure
→ apply authoritative returned page
→ show FailureMessage on that page

transport/operation failure
→ keep existing authoritative page
→ show narrow local failure

context changes to another AppId
→ old context failure is cleared

fresh independent authoritative refresh is accepted
→ stale local transport/failure banner is cleared
```

Do not build a toast/notification framework.

Device and Profile failure banners must be independent.

---

## 20. Debounce policy — identical to QAM/shared contract

There must be no Overlay-owned Profile delay constant.

Current shared policy:

```text
Toggle → Immediate
Slider → TrailingDebounce(2000 ms)
```

Overlay uses:

```csharp
row.CommitPolicy.DelayMilliseconds
```

through the existing `OverlayDelayedSliderCommit.Schedule(intent, delay)` path.

Do not add:

```text
PROFILE_SLIDER_COMMIT_DELAY_MS
OverlayProfileDelay
2000 hard-coded in Profile renderer
```

The current 2000 ms happens because `QuickSettingsCommitPolicy.TrailingDebounce2000` is the shared product policy.

### 20.1 CPU Boost / Power Mode

Each side remains independent:

```text
ProfileCpuBoostAc
ProfileCpuBoostDc
ProfilePowerModeAc
ProfilePowerModeDc
```

Each uses its own row-keyed pending draft/timer.

### 20.2 Profile TDP

All four sliders share:

```text
CommitGroupId = ProfileTdpConfiguration
```

Required behavior:

```text
first edit
→ seed entire ProfileTdp section
   ProfileTdpEnabled + 4 PL values

later edit before timeout
→ reuse one group draft
→ restart trailing delay

linked PL1/PL2 rule
→ apply immediately to local draft/preview

settle
→ emit exactly one five-value Profile mutation intent
```

No Overlay TDP tuple policy.

No label parsing.

No `PL1` / `PL2` product logic in binder.

---

## 21. Immediate Toggle behavior

Keep the SF-V2-07 rule:

```text
immediate feature Toggle
→ cancel unsubmitted pending work in that same section
→ submit immediately
```

Examples:

```text
ProfileTdpEnabled OFF
→ retire pending ProfileTdpConfiguration draft

ProfileCpuBoostEnabled OFF
→ retire pending AC/DC CPU drafts

ProfilePowerModeEnabled OFF
→ retire pending AC/DC Power drafts
```

For overall `ProfileEnabled`:

- do not invent cross-section dependency metadata;
- do not hard-code "cancel TDP + CPU + Power" logic;
- accept the authoritative OFF result;
- generic non-writable prune from section 18 retires the child drafts.

This exactly matches the QAM/shared policy established by SF-V2-08.

---

## 22. Device/Profile pending state isolation

Overlay has two persistent visible tabs, unlike QAM's one current page.

Use two page-local binding instances:

```text
Device binding
Profile binding
```

Therefore:

```text
pending Device CPU edit
≠
pending Profile CPU edit
```

They must not share:

- pending dictionaries;
- generation helpers;
- local failure banners;
- authoritative pages;
- row controls.

The mutation delegate may be shared because it is only transport forwarding.

Do not create one global Overlay pending dictionary solely to imitate QAM.

---

## 23. Hide / Back / outside-click / teardown

Current Hide calls:

```csharp
_deviceBinding?.CancelUnsubmittedDrafts();
```

After SF-V2-09:

```text
cancel unsubmitted drafts for Device binding
cancel unsubmitted drafts for Profile binding
```

Then continue OQ4 Hide immediately.

Requirements:

- never await debounce;
- never await an already-submitted feature mutation before hiding;
- already-submitted Runtime operation may finish independently;
- process/window teardown disposes both bindings and suppresses obsolete callbacks;
- reopen gets fresh Device + Profile pages from Runtime publication.

Do not make Profile mutation state an OQ4 teardown dependency.

---

## 24. Selection and scroll behavior

Preserve the proven SF-V2-07 behavior for both pages.

### 24.1 Same-shape value refresh

When row shape is unchanged:

```text
update existing WinUI controls in place
```

Do not tear down controls while a user is dragging another slider.

### 24.2 Structural refresh

When rows appear/disappear or kind changes:

- rebuild only that Quick Settings tab content;
- preserve selected `QuickSettingsRowId` when that same tab is visible and the row still exists/selectable;
- otherwise normalize through `OverlayRowSelection`;
- do not reset `BodyScroll` to top for an ordinary Runtime refresh.

### 24.3 AppId change

It is acceptable to preserve the same selected row identity across:

```text
Profile(A) → Profile(B)
```

when the row still exists/selectable, because binding context/values are replaced before the row can mutate B.

Do not preserve stale value/draft state.

### 24.4 No automatic tab switching

Game start/exit does not auto-select Profile/Device.

Overlay tab selection remains user/shell-owned.

---

## 25. Profile page visual parity requirements

On an active game with an enabled full Profile, Overlay must show the same shared labels/order/options as QAM because both consume the same page.

Expected current product sequence:

```text
<game title>
  Profile

TDP Control
  Plugged in · PL1
  Plugged in · PL2
  On battery · PL1
  On battery · PL2

CPU Boost
  Plugged in
  On battery

Windows Power Mode
  Plugged in
  On battery
```

Actual optional/child visibility still comes from the page payload.

Do not hard-code this sequence in Overlay code; this list is for validation only.

---

## 26. Transport remains v7 — no protocol bump

Expected:

```text
FrontendTransportProtocol = 28 unchanged
OverlayTransportProtocol  = 7 unchanged
```

No new message kind is needed.

Reuse:

```text
QuickSettingsPageState
QuickSettingsMutationRequest
QuickSettingsMutationResult
```

`NamedPipeOverlayServer.SendQuickSettingsPageStateAsync(page)` is already page-generic.

`NamedPipeOverlayClient` already forwards generic page snapshots.

Do not add:

```text
ProfilePageState
ProfileMutationRequest
ProfileMutationResult
```

Do not restore any old feature-specific Overlay wire API.

---

## 27. Production file scope

Expected focused production changes:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
  - bind Profile capture
  - publish both pages through generic refresh
  - allow Profile mutation after normal Overlay lifecycle admission
  - preserve mutation self-invalidation guard
  - add minimal active-AppId-change-during-mutation refresh backstop

src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
  - Device+Profile capture delegates
  - _deviceRefreshGate → generic Quick Settings refresh gate
  - RefreshDeviceQuickSettingsAsync → RefreshQuickSettingsAsync
  - sequential best-effort two-page publication

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
  - generic page logging/comments
  - continue forwarding both PageIds to Window

src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
  - remove Device-only assumptions/comments
  - page/AppId context validation
  - context-change pending retirement
  - same-context absent/non-writable pending prune
  - stale immediate/delayed settlement guard
  - correct page-local failure reset

src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
  - two page-local bindings/surfaces
  - replace Profile placeholder with generic renderer
  - generalize Device renderer methods/state instead of duplicating Profile code
  - cancel both pages' unsubmitted drafts on Hide
```

Potential comment-only or no production change:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
```

No wire shape change should be required.

---

## 28. Production files expected to remain unchanged

Do not change unless a concrete implementation blocker proves it necessary:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.QamHost/**
src/SteamInputAddonforClaw.UI/**
FrontendWire.cs
NamedPipeAddonFrontendClient.cs
NamedPipeAddonFrontendServer.cs
Full1902 physical ownership code
HidHide owners
VIIPER owners
presentation ownership
OQ4 input router
```

SF-V2-08 already implemented the shared Profile product and central dispatch. SF-V2-09 should **consume** them, not revise them.

If implementation begins editing QAM or Profile product policy, stop and prove why.

---

## 29. Required tests — Overlay transport/publication

Extend `OverlayTransportTests` / focused Overlay transport tests.

At minimum prove:

### 29.1 Visible refresh publishes two pages

Given Ready + Visible:

```text
RefreshQuickSettingsAsync()
→ Device page frame
→ Profile page frame
```

Assert stable page order Device then Profile.

### 29.2 No active game

Profile capture returns explicit unavailable page:

```text
PageId = Profile
Available = false
Sections = []
```

while Device still publishes normally.

### 29.3 Independent capture failure

- Device capture failure publishes `Unavailable(Device)` and Profile still publishes;
- Profile capture failure publishes `Unavailable(Profile)` and Device remains published.

### 29.4 Hidden session

A refresh started visible but hidden during capture must not intentionally keep publishing to the hidden session.

Preserve the existing write-time Ready/Visible re-check.

### 29.5 One refresh gate

Concurrent Show refresh + StateInvalidated refresh remain sequential.

Do not add per-page competing refresh tasks.

### 29.6 No polling

Source/behavior tests must continue to prove no periodic refresh loop.

---

## 30. Required tests — Runtime Overlay mutation admission

Add/update focused host/transport tests to prove:

```text
Overlay hidden/not captured
→ Device intent rejected
→ Profile intent rejected

Overlay captured + visible
→ Device intent reaches shared adapter
→ Profile intent reaches shared adapter
```

Also prove:

- no direct `GameProfileMutations` call from Overlay path;
- no direct ProfileStore/hardware mutation from Overlay path;
- stale Profile AppId returns shared adapter failure and does not retarget current game;
- unknown/invalid PageId remains structurally rejected by existing wire validation.

### 30.1 Active AppId changed while mutation invalidation was suppressed

Add the narrowest feasible regression for section 10:

```text
activeApp before mutation = A
mutation in flight
activeApp becomes B
mutation completes/throws
→ current Overlay Quick Settings refresh requested once after the guard drops
```

Do not introduce a production abstraction solely to make this test easy. If `AddonProcessHost` composition makes a direct deterministic unit test disproportionate, use the smallest existing test seam/source-contract assertion plus behavior coverage of the resulting publication path.

---

## 31. Required tests — generic Overlay binder context safety

Extend `OverlayQuickSettingsPageBindingTests` beyond Device-only assumptions.

Create representative Profile pages with real Profile row/group IDs and AppIds.

At minimum prove:

### 31.1 Profile intent carries AppId

For:

```text
Profile AppId = 480
ProfileCpuBoostAc slider edit
```

submitted intent must contain:

```text
PageId = Profile
AppId = 480
EditedRowId = ProfileCpuBoostAc
```

### 31.2 Profile TDP group

One grouped edit emits exactly:

```text
ProfileTdpEnabled = true
ProfileTdpAcPl1
ProfileTdpAcPl2
ProfileTdpDcPl1
ProfileTdpDcPl2
```

with:

```text
PageId = Profile
AppId = target AppId
```

and one mutation call.

### 31.3 Context change retires old draft

```text
Profile(A) pending slider
→ ApplyAuthoritativePage(Profile(B))
→ A pending draft/timer retired
→ B page shows only B authoritative state
```

### 31.4 Late delayed A result cannot replace B

```text
A delayed mutation already submitted
→ B page accepted
→ A result completes
→ B authoritative page remains current
→ A failure message not shown on B
```

### 31.5 Late immediate A result cannot replace B

Same rule for Toggle mutation.

### 31.6 Same-context valid pending survives refresh

```text
Profile(A) pending CPU slider still writable
→ fresh Profile(A) page
→ pending desired value remains effective visually
→ unrelated rows adopt fresh authority
```

### 31.7 Fresh page prunes non-writable/absent pending row

Prove both:

```text
edited row absent
→ pending retired

edited row present but Writable=false
→ pending retired
```

### 31.8 Profile master OFF scenario

Deterministically reproduce:

```text
pending ProfileCpuBoostAc / ProfileTdp / ProfilePowerMode draft
→ ProfileEnabled OFF result page
→ child row non-writable/absent
→ pending entry removed
→ no later child commit
```

No feature-name special case in the pruning helper.

### 31.9 Context failure lifecycle

- old local failure clears on AppId change;
- fresh accepted authoritative page clears stale transport-local failure;
- typed failure settlement still shows its own `FailureMessage`.

### 31.10 Device regression

Existing Device tests remain green unchanged in behavior:

- independent drafts;
- grouped Device TDP;
- linked preview;
- 2-second metadata delay;
- typed failure authority;
- transport failure behavior;
- immediate Toggle busy gate;
- Hide cancellation.

---

## 32. Required tests — OverlayWindow generic renderer wiring

Update/rename `OverlayDeviceRendererWiringTests` only as needed; do not duplicate a separate Profile test framework.

Prove from production structure that:

- Device and Profile both use the same generic renderer path;
- Profile is no longer `CreatePlaceholderPage(Profile)`;
- no Profile-specific `OverlayToggleRow`/`OverlaySliderRow` construction table exists;
- no hard-coded CPU Boost option table exists in Overlay;
- no hard-coded Power Mode option table exists in Overlay;
- no hard-coded TDP limits/gap tuple exists in Overlay;
- no Profile literal delay policy exists;
- no FPS Limit or Resolution row is added;
- `OverlayWindow` still does not own `NamedPipeOverlayClient`;
- binder still does not own `NamedPipeOverlayClient`;
- Device/Profile surface state is page-local;
- selection remains wired through existing `OverlayRowSelection`.

---

## 33. Required tests — current Profile row parity

Use existing `QuickSettingsPresentationTests` as the product-definition authority; do not duplicate all Profile product tests in Overlay.

Overlay-specific validation only needs to prove it renders the provided shared page in payload order.

Do not re-test product policy by hard-coding all Profile labels/ranges in renderer tests if `QuickSettingsPresentationTests` already own that truth.

The parity guarantee should be architectural:

```text
QAM     consumes QuickSettingsPageSnapshot(Profile)
Overlay consumes QuickSettingsPageSnapshot(Profile)
```

not two separately maintained expected product tables.

---

## 34. Manual MSI Claw validation

### 34.1 No game active

Open Overlay:

- Device tab behaves exactly as before;
- Profile tab is still present;
- Profile shows explicit unavailable/no-active-game state;
- Profile has no selectable/mutable rows;
- tab navigation remains normal.

### 34.2 Active Steam game with no saved Profile

Launch a game with no saved Profile:

- Profile tab updates event-driven without reopening Overlay;
- game title is correct;
- Profile toggle is visible OFF;
- enabling Profile creates/enables the correct AppId only;
- no FPS Limit;
- no Resolution row.

### 34.3 Enabled Profile

Validate exact current shared controls:

```text
Profile toggle
TDP toggle + AC/DC PL1/PL2
CPU Boost toggle + AC/DC
Windows Power Mode toggle + AC/DC
```

Compare order/labels/options with QAM.

### 34.4 Debounce parity

CPU Boost/Power:

- Left/Right or touch updates local preview immediately;
- only latest value commits after approximately 2 seconds;
- each side commits independently.

TDP:

- local preview immediate;
- linked PL companion correction immediate;
- one shared group timer;
- latest whole group commits after approximately 2 seconds;
- reopened/refreshed page shows Runtime truth.

### 34.5 Profile feature Toggle while child draft pending

For TDP/CPU/Power:

```text
move slider
→ before 2 s, turn that feature OFF
```

Expected:

- same-section pending draft canceled;
- feature turns OFF immediately after settlement;
- no delayed child commit later.

### 34.6 Profile master OFF while child draft pending

```text
move CPU/TDP/Power slider
→ before 2 s, turn Profile OFF
```

Expected:

- master turns OFF;
- fresh page makes child controls non-writable;
- generic prune removes child drafts;
- no stale preview;
- no delayed "row is not editable" error.

### 34.7 Game A → Game B while Profile tab open

```text
Game A active
→ Profile(A) visible
→ switch to Game B
```

Expected:

- event-driven Profile(B) replaces A;
- A pending unsubmitted work retired;
- A late result cannot replace B;
- A action can never mutate B.

### 34.8 Game exits to no game

Profile becomes unavailable without hiding/aliasing the Device page.

### 34.9 Change game while mutation is in flight

Exercise at least once with a deliberately slow/apply-heavy mutation if practical:

```text
mutation in flight
→ game exits/switches
```

Expected:

- Overlay converges to the new Profile context;
- self-invalidation suppression does not leave the old game indefinitely displayed.

### 34.10 Hide during pending/submitted Profile mutation

For both unsubmitted debounce and already-submitted mutation:

- B/Back hides immediately;
- outside click hides immediately;
- no 2-second close delay;
- controller publisher/capture lifecycle remains correct;
- reopen shows Runtime truth.

---

## 35. Logging

Keep logs narrow and useful.

Prefer generic categories/messages such as:

```text
Overlay Quick Settings page capture failed
Overlay Quick Settings page publish failed
Quick Settings page received
```

Include where useful:

```text
PageId
AppId
Available
SectionCount
```

Do not add per-slider Info spam.

Normal value previews should not become production log noise.

---

## 36. Implementation order

Recommended sequence:

1. **Genericize `OverlayQuickSettingsPageBinding` context semantics first**
   - expected PageId check;
   - AppId context change retirement;
   - same-context non-writable/absent prune;
   - stale immediate/delayed settlement guard;
   - failure lifecycle;
   - add pure binder tests.

2. **Generalize `OverlayWindow` Device renderer**
   - one small page-surface holder if useful;
   - build Device + Profile through the same renderer;
   - keep Controller/Shortcut/Setting behavior unchanged;
   - add wiring tests.

3. **Extend Runtime publication**
   - bind Device + Profile capture;
   - generic refresh gate/method;
   - publish both pages sequentially;
   - add transport tests.

4. **Open Profile mutation admission**
   - remove Device-only historical gate;
   - continue shared adapter dispatch;
   - keep OQ4 lifecycle admission;
   - add stale AppId/admission tests.

5. **Add active-AppId-change-during-mutation backstop**
   - use the minimal before/after active AppId check;
   - no deferred-refresh subsystem.

6. **Update App comments/logging**
   - generic Quick Settings wording only.

7. **Run full suite and source cleanup search**.

Do not start by duplicating Device renderer code into Profile.

---

## 37. Source cleanup checklist

After implementation, production source should no longer contain current-lifecycle statements equivalent to:

```text
Profile publication is not rendered until SF-V2-09
Ignoring a non-Device Quick Settings page
Profile is not exposed/admitted to the Overlay
Only the Device page is captured/exposed to Overlay
RefreshDeviceQuickSettingsAsync
_deviceRefreshGate
```

Device-prefixed names may remain only where the concept is genuinely Device-specific, not where code now serves both pages.

Search Overlay production code for accidental Profile product duplication:

```text
"Intel FPS Limit"
"Resolution"
"Efficient Aggressive"
"Best performance"
"Plugged in · PL1"
"8, 30"
"8, 35"
"2000"   // Profile renderer policy source must not appear as a new literal
```

Some strings can legitimately exist elsewhere in the repository/product authority. The requirement is **no duplicate product authority in Overlay renderer/binder**.

---

## 38. Full validation before PR completion

Run at minimum:

```text
dotnet build                       // Debug
dotnet build -c Release
dotnet test                        // full suite
git diff --check
```

Pay particular attention to:

```text
OverlayQuickSettingsPageBindingTests
Overlay renderer wiring tests
OverlayTransportTests
OverlayDeviceQuickSettingsTransportTests
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QuickSettingsInProcessSeamTests
QamFrontendContractTests
QamFrontendBridgeTests
Full1902 lifecycle/controller ownership tests
OQ4/OQ5 navigation/lifecycle tests
```

Do not weaken Device/QAM tests to accommodate the Overlay migration.

---

## 39. Acceptance criteria

SF-V2-09 is complete only when all are true:

### Shared product

- Overlay Profile renders `QuickSettingsPageSnapshot(Profile)` directly;
- no Profile-specific product definition exists in Overlay;
- current visible Profile scope matches QAM by construction;
- FPS Limit and Resolution remain hidden/out of scope.

### Publication

- visible/captured Overlay receives both Device and Profile pages;
- publication is event-driven only;
- no active game produces explicit unavailable Profile page;
- one page capture failure does not suppress the other.

### Mutation

- Profile Toggle/Slider mutations use existing v7 generic request/result;
- Overlay lifecycle admission remains intact;
- `QuickSettingsMutationAdapter` remains sole Profile product mutation validator/dispatcher;
- stale AppId never mutates the new game.

### Binder lifecycle

- Profile(A) → Profile(B) retires old local pending work;
- late A immediate result cannot replace B;
- late A delayed result cannot replace B;
- same-context valid drafts survive normal authoritative refresh;
- absent/non-writable fresh rows prune stale drafts;
- Profile master OFF removes stale child previews/timers without feature-specific cancellation logic.

### Interaction

- Toggle immediate;
- sliders use shared metadata delay (currently 2000 ms);
- CPU/Power sides independent;
- Profile TDP one grouped draft + linked preview + one commit;
- touch/controller navigation uses existing row primitives/selection.

### OQ4 safety

- Show publication remains after capture commit;
- Hide never waits on debounce/mutation;
- crash/disconnect/input-loss teardown remains unchanged;
- no controller-authority change.

### Protocol

- Frontend protocol remains 28;
- Overlay protocol remains 7;
- no compatibility shim/dual path.

---

## 40. Final architecture after SF-V2-09

```text
                         Runtime authorities
                 ┌─────────────┴─────────────┐
                 │                           │
       Device typed features        Game Profile typed features
                 │                           │
                 └─────────────┬─────────────┘
                               ↓
                 Shared Quick Settings product
              Device page             Profile page
                    │                       │
              ┌─────┴───────────────────────┴─────┐
              │                                   │
              ↓                                   ↓
        .Frontend/.Qam                      .Overlay v7
              │                                   │
        generic QAM renderer                generic WinUI renderer
              │                                   │
              └──────── same rows/policy ─────────┘
```

Surface differences remain where they belong:

```text
QAM admission/lifecycle     = QAM-owned
Overlay admission/lifecycle = OQ4/Runtime-owned
rendering toolkit           = surface-owned
product definition          = shared
Runtime state/mutation      = shared authority
```

That is the end-state this PR should reach.
