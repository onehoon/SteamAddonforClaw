# Work Order — Overlay Profile Catalog + Shared Profile FPS/Resolution Parity

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** main  
**Target branch after merge:** main  
**Reviewed main:** 8737c3ad32eafe11f19868eb24150eb3982294af  
**Feature area:** Addon Overlay / Profile tab  
**Expected implementation PR count:** 2 focused PRs

---

## 0. Purpose

Complete the Addon Overlay Profile tab so it is useful both outside and inside a running game, while preserving the existing Shared Quick Settings product authority and Full1902 controller lifecycle.

Required user experience:

~~~text
No actual game running
    -> enter Overlay Profile tab
    -> automatically scan the existing Profile game catalog
    -> show installed Steam + non-Steam shortcut games as cards
    -> cards configured as Favorite in the Main App sort first
    -> then sort by game name, then AppId
    -> A / pointer on a card opens that game's Profile detail
    -> B from selected-game detail returns to the card catalog
    -> B from catalog closes the Overlay

Actual game running
    -> enter Overlay Profile tab
    -> show the actual running game's Profile detail directly
    -> do not show the catalog first
    -> B closes the Overlay
~~~

The Overlay catalog is deliberately simpler than the Main App Profile page.

Do not add:

- search;
- keyboard/touch-keyboard support;
- a manual Refresh button;
- cover images;
- Steam / Non-Steam source labels;
- Favorite editing;
- a Favorite star/button;
- X/Y shortcuts for Favorite;
- an Overlay-specific Profile persistence store.

Favorite is read-only catalog metadata in Overlay. The Main App remains the place where Favorite is changed.

Also close the current shared Profile product gap by adding:

- Intel FPS Limit;
- Resolution.

The detail page must continue to use the SAME QuickSettingsPageSnapshot(Profile) product and the existing generic Overlay Quick Settings binder/row primitives. Do not build a second Overlay-specific settings editor.

---

# 1. Mandatory source review before coding

Read the latest versions on the implementation branch before changing code.

## 1.1 Full1902 authority

At minimum:

    docs/Full 1902 Implementation/README.md
    docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
    docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
    docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

This work must not change:

    PID1901 <-> PID1902 ownership
    HidHide ownership/recovery
    VIIPER ownership/teardown
    physical DirectInput ownership
    Xbox360 <-> SteamDeck presentation switching
    Overlay neutral-capture ordering
    Sleep / Hibernate / Resume behavior
    Restart / Crash / Shutdown recovery
    PnP re-enumeration recovery
    routing rollback / fail-close behavior

The Overlay remains a presentation surface. Runtime remains the controller/lifecycle authority.

## 1.2 Existing Shared Frontend / Overlay design

Read:

    docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
    docs/shared-frontend/SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md
    docs/shared-frontend/SF_V2_07_OVERLAY_GENERIC_DEVICE_RENDERER_BINDING_WORK_ORDER.md
    docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
    docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
    docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md
    docs/work-order/OVERLAY_UI_FOUNDATION_PR_A_STRUCTURAL_REFACTOR_WORK_ORDER.md

This work order intentionally supersedes two old SF-V2-09 product assumptions for the Overlay Profile tab:

1. no-active-game Profile no longer renders only "No active game.";
2. the Overlay may now request the existing Runtime/frontend game catalog because the product requirement is a card catalog outside a running game.

It does NOT supersede SF-V2-09's core ownership rules:

- one shared Profile product definition;
- one typed mutation adapter;
- one Runtime/frontend profile authority;
- no Overlay-specific Profile store;
- no polling;
- no new controller authority;
- generic Overlay settings rendering.

## 1.3 Current implementation files

Review at minimum:

    src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
    src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs

    src/SteamInputAddonforClaw/Profiles/ProfileGameCatalog.cs
    src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
    src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
    src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

    src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
    src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
    src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

    src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

    src/SteamInputAddonforClaw.Overlay/App.xaml.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
    src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs

---

# 2. Reviewed current behavior

The following is already true on reviewed main and should be reused rather than rebuilt.

## 2.1 Main App Profile already has the desired catalog data and detail features

ProfilePage already:

- scans with IAddonFrontendControl.ScanProfileGamesAsync();
- displays a game catalog;
- sorts Favorite first, then Name, then AppId;
- captures arbitrary selected AppId with CaptureGameProfileAsync();
- edits Profile Enabled;
- edits TDP;
- edits Intel FPS Limit;
- edits CPU Boost;
- edits Windows Power Mode;
- edits Resolution;
- allows Favorite mutation in the Main App.

The Overlay should copy the product behavior that is relevant, not copy the Main App page implementation or its local state model.

## 2.2 Existing catalog already contains both Steam and non-Steam shortcuts

ProfileGameCatalogScanner already reads:

- Steam library folders / appmanifest files;
- Steam userdata shortcuts.vdf non-Steam shortcuts.

FrontendProfileGameCatalogEntry already carries:

    AppId
    Name
    Source
    Favorite

No new game scanner, database, cache, Steam parser, or Favorite store is required.

Overlay ignores Source for presentation.

## 2.3 Favorite already comes from the existing profile mutation authority

InProcessAddonFrontendControl.ScanProfileGamesAsync() already gets the authoritative Favorite AppId set and publishes Favorite on each FrontendProfileGameCatalogEntry.

Overlay must only sort by that field.

Do not call SetGameProfileFavoriteAsync() from Overlay.

## 2.4 Shared Overlay Profile is currently active-game-only

Current shared Profile capture requires the requested AppId to match CaptureActiveGameProfileAsync().

That is correct for the old active-game-only Overlay design, but it prevents this required flow:

~~~text
no game running
-> catalog card selected
-> capture/edit selected non-running game's Profile
~~~

The shared target contract must therefore be extended narrowly.

## 2.5 Shared Profile currently omits FPS and Resolution

FrontendGameProfileSnapshot already contains:

    FpsLimit
    Resolution

and the typed frontend already exposes the corresponding mutations.

However QuickSettingsPresentation.BuildProfile(), QuickSettingsSectionId / QuickSettingsRowId, and QuickSettingsMutationAdapter do not yet expose them.

This is a shared-product gap. Fix it in the shared Profile contract, not inside Overlay rendering.

## 2.6 Overlay navigation already has one explicit 2D exception

OverlayWindow.Navigation currently keeps normal Quick Settings navigation as a vertical logical-row model and branches narrowly for the Shortcut 2x2 grid using OverlayShortcutSelection.

Use the same design principle for the Profile catalog.

Do not generalize Shortcut + Profile into a navigation graph/framework.

## 2.7 B currently always requests Runtime-owned dismiss

App.xaml.cs currently maps OverlayNavigationAction.Back directly to SendBackDismissAsync().

This must change only enough to let selected Profile detail consume one local Back:

~~~text
selected non-running game detail
    B -> Profile catalog

Profile catalog
    B -> existing Runtime-owned Overlay dismiss

active running-game detail
    B -> existing Runtime-owned Overlay dismiss
~~~

Top-level dismiss authority remains Runtime-owned.

---

# 3. Product rules frozen by this work order

## 3.1 Profile presentation modes

Overlay needs only narrow presentation state:

~~~text
Catalog
SelectedDetail
ActiveDetail
~~~

This is Overlay presentation state only.

It is NOT:

- a Runtime authority;
- a second active-game source;
- a Profile persistence authority;
- a lifecycle state machine;
- a controller authority.

ActualRunningAppId remains the active-game authority.

Selected catalog AppId is valid only while no actual game is running.

## 3.2 Target selection policy

Use this exact policy:

~~~text
ActualRunningAppId > 0
    -> the only valid Profile target is ActualRunningAppId
    -> a different requested AppId fails closed

ActualRunningAppId == 0
    -> an explicit requested AppId > 0 may be captured/edited as an offline catalog target
~~~

Do not allow Overlay browsing/editing another game while an actual game is running.

Do not create a global SelectedProfileAuthority.

## 3.3 Runtime active-game publication always wins

If an actual game starts while Catalog or SelectedDetail is visible:

~~~text
cancel unsubmitted selected-game Quick Settings drafts
clear selected catalog AppId
switch to ActiveDetail
render the authoritative Runtime-published Profile page
~~~

If the actual game exits while the Profile tab is visible:

~~~text
cancel active Profile drafts
switch to Catalog
request a fresh catalog scan
~~~

No polling.

Use the existing ActualRunningAppIdChanged -> StateInvalidated -> Overlay Quick Settings publication path as the active-game transition signal.

## 3.4 Catalog refresh policy

There is no manual Refresh control.

When the Profile tab is entered while there is no actual game:

~~~text
request ScanProfileGamesAsync()
show loading/empty state as needed
render returned cards
~~~

Re-entering the Profile tab later may request a new scan again.

Do not add:

- a cache manager;
- scan timestamps;
- TTL logic;
- a refresh service;
- a periodic scan timer.

Keeping the last rendered list in Overlay memory is fine as presentation memory only. A new tab entry replaces it with the next scan result.

When an actual game is running, do not scan the catalog merely because Profile was selected. The active Profile page is enough.

## 3.5 Active game missing from prior catalog

Do not make catalog membership a prerequisite for active Profile display.

CaptureActiveGameProfileAsync already enriches a missing DisplayName by scanning the catalog when needed.

Keep the hierarchy:

~~~text
ActualRunningAppId = authority
profile snapshot = primary data
catalog scan = optional display-name enrichment
fallback name = Game {AppId} if still unresolved
~~~

## 3.6 Catalog visual scope

Cards only.

Each card shows only the game name.

Do not show:

- Favorite star;
- Favorite status text;
- Steam / Non-Steam;
- AppId unless a diagnostic fallback is necessary;
- cover image;
- search field;
- Refresh button.

Sort:

~~~text
Favorite descending
Name ascending, ordinal-ignore-case
AppId ascending
~~~

Use a three-column card layout, matching the existing Main App catalog direction and current Overlay width.

The last row may contain one or two cards.

Navigation is bounded / no-wrap.

## 3.7 Favorite ownership

Overlay:

    reads Favorite
    sorts by Favorite

Main App:

    owns Favorite editing

No new controller semantic action is needed.

Do not add X/Y to OverlayNavigationAction and do not change OQ4 consumed-control/release-to-resume behavior for this feature.

## 3.8 Cover/search deferred

Cover images and text search are explicitly out of scope.

Do not leave partially wired controls or speculative interfaces for them.

---

# 4. Implementation split

Implement as two focused PRs.

## PR-A — Shared Profile parity + offline target contract

Goal:

1. add Intel FPS Limit + Resolution to the shared Quick Settings Profile product;
2. allow an explicit Profile AppId to be captured/edited only when no actual game is running;
3. preserve fail-close behavior as soon as a different actual game exists.

PR-A must contain no Overlay catalog UI or catalog wire protocol.

## PR-B — Overlay Profile catalog + local detail navigation

Goal:

1. request existing game catalog on Profile tab entry when no game is running;
2. render/read-only-sort cards;
3. request selected offline Profile page;
4. reuse generic Quick Settings detail rendering;
5. implement the required B hierarchy;
6. let active-game publication override local selected detail.

PR-B builds on PR-A.

---

# 5. PR-A — Shared Profile FPS + Resolution parity

## 5.1 Extend the closed Quick Settings IDs

In QuickSettingsContracts.cs add Profile-only IDs.

Sections:

    ProfileFpsLimit
    ProfileResolution

Rows:

    ProfileFpsLimitEnabled
    ProfileFpsLimitAc
    ProfileFpsLimitDc
    ProfileResolution

Do not create parallel Overlay enums.

Do not renumber or reinterpret existing IDs in a way that silently changes existing serialized enum meanings. The current Overlay protocol is pre-release and string-enum JSON is used where applicable, but still append new members rather than reshuffling existing members.

## 5.2 BuildProfile section order

QuickSettingsPresentation.BuildProfile() must publish in this order:

~~~text
Profile General
TDP Control                    when Limits exists
Intel FPS Limit               when FpsLimit snapshot exists
CPU Boost
Windows Power Mode            when PowerMode exists
Resolution
~~~

This follows the existing Main App detail order.

Overlay and every other shared Quick Settings renderer must consume this same product output. Do not special-case Overlay.

## 5.3 Intel FPS Limit shared rows

Use the existing FrontendGameFpsLimitConfiguration.

Required controls:

~~~text
Intel FPS Limit
    Enabled      Toggle
    Plugged in   numeric Slider
    Battery      numeric Slider
~~~

Required range:

    minimum = 40
    maximum = 120
    step = 1

Commit policy:

    Enabled = Immediate
    AC/DC sliders = TrailingDebounce2000

Availability:

- if FpsLimit is null, omit the section;
- if FpsLimit.Available is false, retain a clear unavailable representation without fabricating editable values;
- show FpsLimit.UnavailableReason when useful;
- child rows are writable only when persistence is writable, Profile is enabled, FPS Limit is available, and the FPS feature is enabled where appropriate;
- the enable row itself follows the current Main App semantics and must remain safe when the feature reports unavailable.

Use the same AC/DC current-power-source visibility policy already used by TDP/CPU Boost/Power Mode:

    ProfileFpsLimitAc
    ProfileFpsLimitDc

must participate in QuickSettingsCurrentPowerSourceOnly filtering.

Do not add an FPS-specific Overlay renderer.

## 5.4 FPS mutation dispatch

QuickSettingsMutationAdapter must map:

    ProfileFpsLimitEnabled -> SetGameProfileFpsLimitEnabledAsync
    ProfileFpsLimitAc      -> SetGameProfileFpsLimitAcAsync
    ProfileFpsLimitDc      -> SetGameProfileFpsLimitDcAsync

Validate exact integer structure and 40..120 range before dispatch.

Malformed/out-of-range input:

    -> zero typed mutations
    -> fail closed
    -> return a fresh authoritative page

Do not clamp malformed transport input.

## 5.5 Resolution product mapping

Add one discrete Slider row:

    ProfileResolution

Exact product options and order:

~~~text
0  Do not change
1  1920 × 1200
2  1920 × 1080
3  1680 × 1050
4  1440 × 900
~~~

Mapping:

    0 -> null
    1 -> FrontendGameResolution(1920, 1200)
    2 -> FrontendGameResolution(1920, 1080)
    3 -> FrontendGameResolution(1680, 1050)
    4 -> FrontendGameResolution(1440, 900)

The row should be writable whenever the existing Main App would allow Resolution persistence. Do not unnecessarily couple Resolution writability to Profile Enabled if the existing Main App does not.

Use the existing generic discrete Slider renderer. Do not add a ComboBox-specific Overlay renderer.

If a persisted resolution is not one of these supported choices, do not silently coerce it to "Do not change".

Fail-safe presentation:

    retain the saved data in Runtime/persistence
    mark the shared row unavailable/non-writable
    surface a narrow unsupported-value message
    do not overwrite it until the user explicitly selects a supported value through a surface that can represent it

## 5.6 Resolution mutation dispatch

Validate only the closed option values 0..4.

Then call:

    SetGameProfileResolutionAsync(appId, mappedResolution, target.DisplayName, token)

Invalid option:

    -> zero typed mutations
    -> fail closed

Return the typed operation's returned snapshot re-projected through BuildProfile().

Never patch the submitted draft onto the old page.

---

# 6. PR-A — explicit offline selected Profile target

## 6.1 Extend capture policy, not ownership

Current CaptureProfileQuickSettingsPageAsync() only accepts the active game.

Change the shared Profile capture behavior to the exact target policy in section 3.2.

Conceptually:

~~~text
requested AppId <= 0
    -> unavailable

actual = ActualRunningAppId

actual > 0 && actual != requested
    -> unavailable / stale target

actual == requested
    -> capture authoritative active profile

actual == 0
    -> capture requested non-running profile
~~~

Do not weaken the different-running-game rejection.

## 6.2 Reuse one display-name enrichment path

Avoid duplicating catalog/name lookup logic.

A practical implementation is to make CaptureGameProfileAsync(appId) use the same "capture + enrich DisplayName only when blank" path currently used by CaptureActiveGameProfileAsync().

Fast path:

    profile already has DisplayName
    -> no catalog scan

Blank DisplayName:

    -> Scan ProfileGameCatalog
    -> find same AppId
    -> enrich snapshot if found
    -> otherwise return snapshot unchanged

CaptureActiveGameProfileAsync() should resolve ActualRunningAppId and reuse this path.

This keeps:

- one scanner;
- one naming rule;
- no catalog cache;
- no display-name field added to QuickSettingsMutationIntent.

## 6.3 Extend mutation target validation

QuickSettingsMutationAdapter.MutateProfileAsync() must support offline selected profiles without allowing stale cross-game writes.

Conceptually:

~~~text
intent AppId must be > 0

active = CaptureActiveGameProfileAsync()

if active.AppId > 0:
    if active.AppId != intent.AppId:
        fail closed, zero typed mutations
    target = active
else:
    target = CaptureGameProfileAsync(intent.AppId)

build fresh shared Profile page from target
validate edited row is Available + Writable
dispatch one typed operation
~~~

The validation target used for:

- current row writability;
- Profile display name;
- post-mutation projection;

must be the validated target snapshot, not an unrelated active snapshot.

## 6.4 Normal game-start lifecycle during an offline edit

Do not add an epoch, lock, barrier, lease, or second authority solely for a narrow instruction-level interleaving.

Required practical behavior:

- once ActualRunningAppId is nonzero and different, new offline mutations fail closed;
- existing StateInvalidated causes the Overlay to receive the actual active game's page;
- Overlay cancels unsubmitted local drafts when switching target;
- InProcessAddonFrontendControl already reconciles hardware only when the mutated AppId equals the actual running AppId.

A mutation explicitly submitted immediately before a game-start notification does not justify a new cross-feature transaction/state machine. Preserve simple convergence.

---

# 7. PR-B — Overlay Profile catalog transport

## 7.1 Do not connect Overlay to the Main UI frontend pipe

The Overlay process currently owns NamedPipeOverlayClient for the dedicated Runtime <-> Overlay pipe.

Preserve that architecture.

Do NOT create a NamedPipeAddonFrontendClient inside SteamInputAddonforClaw.Overlay.

Do NOT let Overlay instantiate ProfileGameCatalogScanner.

Required authority path:

~~~text
Overlay Profile tab
    -> dedicated Overlay pipe request
    -> Runtime / AddonProcessHost
    -> existing IAddonFrontendControl
    -> existing ScanProfileGamesAsync or CaptureQuickSettingsPageAsync
    -> dedicated Overlay pipe response
    -> Overlay presentation
~~~

## 7.2 Add only two narrow request flows

Bump OverlayTransportProtocol from v10 to v11.

Pre-release policy:

- no v10 compatibility shim;
- v10 peers fail handshake exactly like prior protocol revisions.

Add narrow messages equivalent to:

~~~text
Overlay -> Runtime
    ProfileCatalogRequest

Runtime -> Overlay
    ProfileCatalogState
        IReadOnlyList<FrontendProfileGameCatalogEntry>

Overlay -> Runtime
    ProfilePageRequest
        AppId

Runtime -> Overlay
    ProfilePageResult
        AppId
        QuickSettingsPageSnapshot
~~~

Names may differ if a clearer existing naming convention is present, but preserve this scope.

Do not add a general RPC framework.

Do not turn QuickSettingsPageState into an ambiguous response for selected offline pages.

Runtime-initiated QuickSettingsPageState(Profile) means the current authoritative active-game state.

ProfilePageResult means a response to a local offline catalog selection.

Keeping those meanings separate lets the active-game publication always win without adding an epoch.

## 7.3 No request-id framework is required

The selected Profile response already carries AppId.

Apply a ProfilePageResult only if:

~~~text
Profile tab is still in SelectedDetail
selectedCatalogAppId == response.AppId
no authoritative active-game page has replaced it
~~~

Apply catalog state only while the no-active-game catalog presentation is still relevant.

A late result after:

- active game start;
- B back;
- tab leave;

is ignored.

Do not introduce a request epoch/generation solely for theoretical timing.

## 7.4 Preserve the single Overlay client reader

NamedPipeOverlayClient.RunAsync() remains the only pipe reader.

Request methods may write:

    SendProfileCatalogRequestAsync()
    SendProfilePageRequestAsync(appId)

Responses are delivered by the existing RunAsync read loop to narrow handlers.

Do not perform ReadAsync inside a send method.

## 7.5 Preserve dismiss/navigation responsiveness during scan

A Steam library scan can involve real filesystem I/O and must not block processing of DismissRequested/other Overlay wire frames on the server read loop.

Handle catalog/page request work with a small contained asynchronous helper using the existing connection lifetime and write gate.

Requirements:

- server read loop remains responsive;
- request task catches/logs its own failures;
- disconnect/session retirement prevents publishing to a retired connection;
- no new worker service;
- no scheduler;
- no request manager;
- no polling.

This is a normal user-visible lifecycle requirement, not a theoretical race defense.

## 7.6 Overlay frame-size bound

Current Overlay MaxFrameBytes is 64 KiB.

A real Steam library can contain enough games for a JSON catalog to exceed that bound.

Do not ship a catalog protocol that works only for small libraries.

Prefer the simple bounded solution for this local same-user pipe:

    increase OverlayTransportProtocol.MaxFrameBytes to 512 KiB

Keep the codec's explicit frame-length rejection.

Add a transport test with a large realistic catalog (for example approximately 1,000-2,000 normal entries) proving the message round-trips under the new bound.

Do not add chunking/pagination unless the actual implementation demonstrates that the bounded 512 KiB frame is insufficient for the supported product scope.

## 7.7 Structural validation

For ProfilePageRequest:

    AppId > 0

For ProfilePageResult:

    response AppId > 0
    QuickSettingsPage.PageId == Profile
    QuickSettingsPage.AppId == response AppId
    existing Quick Settings structural validation passes

For ProfileCatalogState:

- collection must not be null;
- each entry AppId > 0;
- Name must not be null/blank;
- Source must be a defined FrontendProfileGameSource;
- Favorite is ordinary bool metadata.

Transport validation does not become product authority.

---

# 8. PR-B — Runtime bindings

## 8.1 Reuse the one IAddonFrontendControl

Bind only narrow delegates from AddonProcessHost / OverlayProcessController.

Conceptually:

~~~text
scanProfileCatalog(token)
    -> _frontendControl.ScanProfileGamesAsync(token)

captureSelectedProfile(appId, token)
    -> _frontendControl.CaptureQuickSettingsPageAsync(
           QuickSettingsPageId.Profile,
           appId,
           token)
~~~

Do not instantiate another frontend control.

Do not instantiate another scanner.

A small sibling binding such as BindProfileCatalogAuthority(scan, captureSelectedProfile) is acceptable and preferable to inventing a provider/registry abstraction.

## 8.2 Keep Runtime-initiated active Profile publication unchanged in meaning

CaptureOverlayProfileQuickSettingsPageAsync() continues to derive its target from:

    _runtimeHost.ActualRunningAppId

When no actual game exists it may still publish an unavailable Profile page. In PR-B that unavailable state is interpreted by Overlay as "Catalog mode" rather than as the final user-facing body.

When an actual game exists:

    publish that game's authoritative Profile page

Do not publish an offline selected AppId through the Runtime-initiated active page path.

---

# 9. PR-B — Overlay Profile page composition

## 9.1 Build one Profile host with two existing-style surfaces

Replace the current Profile BuildQuickSettingsPage-only body with a narrow Profile host containing:

~~~text
Catalog surface
Detail surface
~~~

Detail surface reuses the existing generic Quick Settings Profile surface/binding.

Do not duplicate:

- Toggle rows;
- Slider rows;
- delayed-commit logic;
- row writability;
- mutation result handling;
- Profile product labels/ranges/options.

## 9.2 Put Profile-specific code in a focused partial

Prefer a focused partial such as:

    OverlayWindow.Profile.cs

Keep existing structural refactor boundaries intact.

Do not grow OverlayWindow.xaml.cs back into a monolith.

## 9.3 Three-column catalog selection

Add one narrow pure selection model for the dynamic three-column Profile catalog, analogous in spirit to OverlayShortcutSelection.

For example:

    OverlayProfileCatalogSelection

It needs only:

    item count
    selected index
    columns = 3
    reset/select
    MoveUp
    MoveDown
    MoveLeft
    MoveRight

Rules:

- bounded;
- no wrap;
- handles incomplete final row;
- never selects an index >= item count;
- first card selected on catalog entry when cards exist.

Do not refactor Shortcut selection into a generalized navigation graph/grid framework.

## 9.4 Controller navigation routing

While Profile Catalog is visible:

    Up/Down/Left/Right -> Profile catalog selection
    A -> open selected game
    B -> not consumed locally; Runtime dismiss

While SelectedDetail is visible:

    Up/Down -> existing Quick Settings row selection
    Left/Right -> existing row adjustment
    A -> existing row activation
    B -> consume locally and return to Catalog

While ActiveDetail is visible:

    normal Quick Settings navigation
    B -> not consumed locally; Runtime dismiss

LB/RB remains top-level tab navigation.

If leaving SelectedDetail through LB/RB or pointer tab selection:

    cancel unsubmitted Profile drafts
    clear selected catalog AppId
    reset local Profile presentation to Catalog

On a later Profile re-entry with no game:

    request a fresh catalog scan

Do not preserve a hidden offline detail submenu across top-level tab changes.

## 9.5 Back dispatch

Change App.xaml.cs narrowly.

Conceptually:

~~~csharp
case OverlayNavigationAction.Back:
    if (_window?.TryHandleBack() != true)
        _ = SendBackDismissAsync();
    break;
~~~

TryHandleBack() returns true only for the local SelectedDetail -> Catalog transition.

Do not move top-level dismiss ownership into OverlayWindow.

Do not alter OQ4 release-to-resume behavior.

## 9.6 Pointer/touch

Pointer/touch on a catalog card:

- selects that card;
- opens the selected Profile detail.

No keyboard focus/search support is required.

Do not change WS_EX_NOACTIVATE / ShowWithoutActivation for this feature.

## 9.7 Catalog loading / empty / failure states

Keep this simple.

Loading:

    show a small "Loading games..." state

Empty:

    "No installed Steam games were found."

Request/scan failure:

    show a narrow failure message
    keep Overlay usable
    next Profile tab entry naturally retries

No manual retry/refresh button.

Do not close Overlay because the catalog scan failed.

---

# 10. Active-game transition behavior

These are normal supported product transitions and must be tested.

## 10.1 Catalog -> game starts

~~~text
Profile catalog visible
-> ActualRunningAppIdChanged
-> Runtime republishes Profile page
-> Overlay cancels catalog-selected state if any
-> ActiveDetail
~~~

A late ProfileCatalogState must not replace ActiveDetail.

## 10.2 SelectedDetail -> game starts

~~~text
offline game A detail visible
-> game B starts
-> Runtime publishes B Profile
-> cancel A unsubmitted slider drafts
-> clear A selected AppId
-> ActiveDetail(B)
~~~

A late ProfilePageResult(A) is ignored.

No extra epoch needed.

## 10.3 ActiveDetail -> game exits

~~~text
active game exits
-> Runtime publishes no-active/unavailable Profile
-> cancel active Profile drafts
-> Catalog
-> if Profile tab currently visible, request ScanProfileGamesAsync()
~~~

## 10.4 Game changes A -> B

Use existing active AppId invalidation semantics.

~~~text
ActiveDetail(A)
-> actual AppId changes to B
-> cancel A drafts
-> render ActiveDetail(B)
~~~

Existing QuickSettings mutation stale-context safety remains in force.

---

# 11. Mutation result / binder rules

The existing generic OverlayQuickSettingsPageBinding remains the detail mutation owner.

When Profile context changes:

- cancel unsubmitted delayed drafts before applying the new target;
- use AppId identity to reject stale mutation results;
- do not locally patch old values onto a new game;
- authoritative returned page wins.

Do not add a second Profile mutation dispatcher in Overlay.

Do not infer success from the local row state.

---

# 12. Main App behavior must remain unchanged

Do not remove or alter the existing Main App capabilities as part of this work.

Main App retains:

- search;
- manual Refresh;
- Favorite editing;
- its current catalog/card layout;
- full arbitrary game selection;
- all existing Profile editors.

Overlay intentionally has a smaller catalog UI.

Do not force Main App onto the Overlay card/presentation state model.

---

# 13. Files likely to change

PR-A likely:

    src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
    src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
    src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
    src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

    tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
    tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
    tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
    tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs

PR-B likely:

    src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
    src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

    src/SteamInputAddonforClaw.Overlay/App.xaml.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs          new if useful
    src/SteamInputAddonforClaw.Overlay/OverlayProfileCatalogSelection.cs new if useful

    tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
    tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
    tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayQuickSettingsContractTests.cs
    tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
    tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs

Exact files may differ after inspecting current branch, but do not broaden scope merely to match this list.

---

# 14. Tests — PR-A

Add/extend focused tests.

## 14.1 Presentation

Assert:

- Profile section ordering includes FPS and Resolution in the frozen order;
- FPS toggle/AC/DC rows have correct IDs;
- FPS numeric range is 40..120 step 1;
- FPS toggle is Immediate;
- FPS sliders use TrailingDebounce2000;
- FPS AC/DC rows participate in current-power-source visibility;
- unavailable FPS does not become writable;
- Resolution options exactly match the five frozen values;
- null Resolution maps to "Do not change";
- known Resolution maps to the correct option;
- unknown persisted Resolution is not silently coerced.

## 14.2 Mutation adapter

Assert valid dispatch for:

- FPS enable;
- FPS AC;
- FPS DC;
- Resolution null;
- all four concrete Resolution choices.

Assert zero typed mutations for:

- FPS < 40;
- FPS > 120;
- wrong value kind;
- invalid Resolution code;
- AppId 0/null;
- a different actual active AppId.

## 14.3 Offline target

Assert:

~~~text
no active game + explicit AppId
    -> capture shared Profile succeeds for that AppId

no active game + explicit AppId + valid mutation
    -> exactly one typed mutation

active game == requested AppId
    -> existing behavior still succeeds

active game != requested AppId
    -> capture unavailable / mutation fails closed
    -> zero typed mutations
~~~

Assert blank DisplayName enrichment uses the existing catalog and missing catalog entry remains safe.

Do not write scheduler-interleaving tests that force impossible instruction-level races.

---

# 15. Tests — PR-B

## 15.1 Protocol

Assert:

- Overlay protocol is v11;
- v10 peer is rejected;
- ProfileCatalogRequest/State round-trip;
- ProfilePageRequest/Result round-trip;
- AppId 0 request is rejected structurally;
- mismatched ProfilePageResult AppId/page AppId is rejected;
- malformed catalog entry is rejected;
- large realistic catalog round-trips under the bounded 512 KiB frame;
- oversized frames remain rejected;
- one RunAsync reader remains the only reader.

## 15.2 Server/session lifecycle

Assert:

- catalog request reaches the bound existing frontend authority;
- selected-page request reaches shared CaptureQuickSettingsPageAsync(Profile, AppId);
- a scan failure returns/logs a narrow failure state and does not crash/close Overlay;
- hidden/retired/disconnected session does not receive a late catalog/page result;
- catalog scan work does not block DismissRequested handling.

Focus on concrete session lifecycle, not theoretical instruction-level races.

## 15.3 Catalog ordering

Given:

~~~text
Game Z  Favorite=false
Game B  Favorite=true
Game A  Favorite=true
Game C  Favorite=false
~~~

render order must be:

~~~text
Game A
Game B
Game C
Game Z
~~~

Do not assert/show Favorite visual controls.

## 15.4 Three-column navigation

Test:

- first item on entry;
- Left/Right bounded;
- Up/Down by three;
- incomplete final row;
- no wrap;
- no out-of-range selection;
- pointer selection;
- A opens selected card.

## 15.5 Back hierarchy

Test:

~~~text
Catalog + B
    -> Window does not consume
    -> App sends existing Runtime dismiss

SelectedDetail + B
    -> Window consumes
    -> Catalog
    -> no Runtime dismiss

ActiveDetail + B
    -> Window does not consume
    -> Runtime dismiss
~~~

## 15.6 Active-game transitions

Test:

- Catalog -> ActiveDetail when Runtime Profile page for an active AppId arrives;
- SelectedDetail(A) -> ActiveDetail(B), with A unsubmitted drafts cancelled;
- late catalog result does not replace ActiveDetail;
- late selected page result does not replace ActiveDetail;
- ActiveDetail -> Catalog when no-active page arrives;
- visible Profile requests a catalog scan on that transition;
- leaving SelectedDetail for another top-level tab cancels drafts and resets to Catalog;
- re-entering Profile with no game requests a fresh scan.

No epoch/generation framework is required for these tests.

---

# 16. Regression requirements

Run the existing relevant suites, including at minimum:

    QuickSettingsPresentationTests
    QuickSettingsMutationAdapterTests
    QuickSettingsInProcessSeamTests
    AddonQuickSettingsSurfaceParityTests
    OverlayTransportTests
    OverlayDeviceQuickSettingsTransportTests
    AddonProcessHostOverlayQuickSettingsContractTests
    FrontendNamedPipeTransportTests
    OverlayDeviceRendererWiringTests
    UiArchitectureTests
    Overlay row/selection tests
    OQ4 Overlay controller capture/navigation tests

Also run normal solution build/test CI expected by the repository.

Regression acceptance:

- Device Quick Settings unchanged;
- existing Setting/Shortcut/Controller tabs unchanged;
- Main UI Profile behavior unchanged;
- Overlay top-level B still retires capture through Runtime;
- no game/controller input leak introduced;
- no controller lifecycle changes;
- no new polling;
- no new background service;
- no new controller/hardware authority;
- no HidHide/VIIPER/PID1902 changes.

---

# 17. Explicit non-goals

This work order does not include:

- cover art;
- SteamGridDB integration;
- image cache;
- Profile search;
- keyboard input;
- touch keyboard;
- Favorite editing in Overlay;
- Steam/Non-Steam badge;
- manual Refresh;
- game launch from the card;
- Profile delete/rename UI;
- controller X/Y shortcut additions;
- generic grid-navigation framework;
- generalized Overlay RPC framework;
- catalog pagination/chunking unless the chosen bounded frame is demonstrated insufficient;
- a Profile cache service;
- a selected-game Runtime authority;
- any CTW integration;
- any change to Full1902 controller ownership.

---

# 18. Overengineering / race review rule

Use the repository's production-race policy.

Protect real user lifecycle:

- actual game starts/exits/changes;
- Overlay Show/Hide/Dismiss;
- Overlay process disconnect/crash;
- tab leave/re-entry;
- operation/scan failure;
- app shutdown;
- real transport retirement.

Do not add state/locks/epochs/barriers for cases that require an artificial instruction-level interleaving and have no realistic user-visible harmful path.

A small local presentation mode + selected AppId + existing Quick Settings target validation is enough.

The target architecture remains:

~~~text
one Runtime active-game authority
one frontend Profile persistence/mutation authority
one shared Quick Settings Profile product
one Overlay transport reader
one generic Overlay detail renderer
one local card-selection model
one Runtime-owned top-level dismiss path
~~~

---

# 19. Acceptance checklist

## PR-A

- [ ] Shared Profile adds Intel FPS Limit.
- [ ] Shared Profile adds Resolution.
- [ ] Main UI Resolution option order/values are preserved.
- [ ] FPS uses 40..120 and existing typed frontend mutations.
- [ ] FPS AC/DC respects current-power-source-only visibility.
- [ ] No Overlay-specific FPS/Resolution renderer exists.
- [ ] Explicit non-running AppId capture works only when ActualRunningAppId == 0.
- [ ] Different running AppId fails closed.
- [ ] Offline mutation never applies hardware reconcile to an unrelated running game.
- [ ] Display-name enrichment reuses one scanner path.
- [ ] Existing active-game Profile behavior remains intact.

## PR-B

- [ ] No game -> entering Profile automatically scans and shows cards.
- [ ] No manual Refresh button.
- [ ] No search.
- [ ] No cover images.
- [ ] No Steam/Non-Steam labels.
- [ ] No Favorite controls.
- [ ] Main App Favorite entries sort to the top.
- [ ] Favorite tie is Name, then AppId.
- [ ] Three-column controller navigation works.
- [ ] A/pointer opens selected game detail.
- [ ] SelectedDetail B returns to Catalog.
- [ ] Catalog B closes through existing Runtime dismiss.
- [ ] ActiveDetail B closes through existing Runtime dismiss.
- [ ] Running game opens directly as ActiveDetail.
- [ ] Running-game publication overrides catalog/offline detail.
- [ ] Game exit returns visible Profile to Catalog and scans.
- [ ] Offline detail uses existing generic Quick Settings renderer.
- [ ] Overlay protocol v11 preserves one reader.
- [ ] Large normal catalogs are not rejected by the old 64 KiB frame limit.
- [ ] Scan/page request work does not block Overlay dismiss handling.
- [ ] No polling/service/cache manager/navigation framework added.
- [ ] Full1902 controller lifecycle remains unchanged.

---

# 20. Final implementation principle

Do not turn the Profile catalog into a second profile system.

The intended structure is:

~~~text
Main App
    Favorite editing / search / management
            |
            v
existing Profile catalog + persistence
            |
            +-----------------------------+
            |                             |
            v                             v
Main App detail                  Overlay card catalog
                                      |
                                      | selected AppId only when no game
                                      v
                            shared Quick Settings Profile
                                      |
                   +------------------+------------------+
                   |                  |                  |
                  TDP          Intel FPS Limit      CPU Boost
                                      |
                              Power Mode / Resolution
                                      |
                                      v
                          existing typed mutations

ActualRunningAppId > 0
    -> overrides local catalog selection
    -> same shared Profile detail
~~~

Keep the implementation small around this existing authority chain.
