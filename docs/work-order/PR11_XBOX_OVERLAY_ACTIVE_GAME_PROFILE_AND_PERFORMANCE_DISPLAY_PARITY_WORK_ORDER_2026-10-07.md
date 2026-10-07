# Work Order — PR11: XBOX Overlay Active-Game Profile + Performance/Display Parity

> **Date:** 2026-10-07  
> **Repository:** \`onehoon/SteamAddonforClaw\`  
> **Reviewed main:** \`main@17f2030e2cc852bf6547d3ae1bf6dcd64abad76f\`  
> **Architecture authority:** \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md\`  
> **Full1902 authority:** \`docs/Full 1902 Implementation/README.md\` and its active precedence chain  
> **Current frontend protocol:** \`FrontendTransportProtocol.CurrentVersion = 59\`  
> **Current Overlay protocol:** \`OverlayTransportProtocol.CurrentVersion = 15\`  
> **Previous XBOX phase:** PR #699 completed non-Steam per-game M1/M2 persistence/apply for XBOX  
> **Scope:** retire the Overlay's offline Steam profile catalog, make the Profile tab active-game-only, replace the Steam-only \`uint AppId\` Quick Settings profile context with one narrow Steam/XBOX target contract, project an active XBOX profile through the same generic Profile Quick Settings renderer, and dispatch Profile Enabled / CPU Boost / TDP / Power Mode / Intel FPS Limit / Resolution edits through the already-existing typed Steam/XBOX mutation authorities  
> **Explicitly deferred to PR12:** XBOX Overlay per-game M1/M2 rows and final X5 acceptance

---

## 1. Goal

Complete the XBOX Overlay path for the settings that already share the common performance/display runtime owners.

The required product flow is:

~~~text
Steam active
→ Runtime active target = Steam AppID
→ Overlay Profile
→ existing Steam typed profile mutations
→ existing shared CPU/TDP/Power/FPS/Resolution runtimes

XBOX active
→ Runtime active target = XBOX canonical key
→ Overlay Profile
→ existing XBOX typed profile mutations
→ SAME shared CPU/TDP/Power/FPS/Resolution runtimes

no recognized active game
→ Overlay Profile shows only:
   "No game is currently running. Start a game to configure its profile."
~~~

Do not add another hardware/apply owner.

---

## 2. Locked product decisions

### 2.1 Overlay Profile is active-game-only

Remove the existing Overlay installed Steam catalog / offline selected-profile workflow.

After PR11, Overlay Profile has only two visual states:

~~~text
Active profile detail
or
No-active-game empty state
~~~

Offline/pre-launch profile editing remains in the Main App:

~~~text
Main App Steam
→ Steam installed/non-Steam shortcut catalog

Main App XBOX
→ installed XBOX catalog

Overlay
→ current active game only
~~~

Do not add an XBOX catalog to Overlay.

### 2.2 No Custom EXE support

Do not add arbitrary executable registration, matching, or profile persistence.

Product policy is:

~~~text
ordinary Win32 game not natively detected
→ add it to Steam as a Non-Steam Game
→ use the existing Steam path
~~~

Epic/GOG native detection remains deferred.

### 2.3 Detection and application remain separate

Do not move Steam/XBOX detection into Quick Settings.

~~~text
SteamSessionRuntime.ActualRunningAppId
XboxGameSessionRuntime.ActiveGame
        ↓
CaptureActiveProfileTarget()
        ↓
frontend profile projection
        ↓
existing typed mutation authority
        ↓
existing shared apply runtimes
~~~

Quick Settings receives a derived target context only.

---

## 3. Current code facts

Reviewed \`main@17f2030e2cc852bf6547d3ae1bf6dcd64abad76f\`.

### 3.1 Overlay Profile is still Steam-only

Current active capture:

~~~text
CaptureOverlayProfileQuickSettingsPageAsync
→ _runtimeHost.ActualRunningAppId
→ 0 = "No active game"
→ Steam AppId Profile capture
~~~

Therefore a correctly detected/applied XBOX game currently has no Overlay Profile page.

### 3.2 Shared Quick Settings Profile identity is hard-coded to uint AppId

Current contracts:

~~~csharp
QuickSettingsPageSnapshot(
    QuickSettingsPageId PageId,
    uint? AppId,
    ...)

QuickSettingsMutationIntent(
    QuickSettingsPageId PageId,
    uint? AppId,
    ...)
~~~

This cannot represent the canonical XBOX string key.

Do not encode an XBOX key into uint.

### 3.3 XBOX typed persistence/mutation authority already exists

Reuse exactly these existing methods:

~~~text
CaptureXboxGameProfileAsync
SetXboxGameProfileEnabledAsync
SetXboxGameProfileCpuBoostEnabledAsync
SetXboxGameProfileCpuBoostAcAsync
SetXboxGameProfileCpuBoostDcAsync
SetXboxGameProfileTdpEnabledAsync
SetXboxGameProfileTdpAsync
SetXboxGameProfilePowerModeEnabledAsync
SetXboxGameProfilePowerModeAcAsync
SetXboxGameProfilePowerModeDcAsync
SetXboxGameProfileFpsLimitEnabledAsync
SetXboxGameProfileFpsLimitAcAsync
SetXboxGameProfileFpsLimitDcAsync
SetXboxGameProfileResolutionAsync
~~~

Do not create Overlay-specific XBOX profile mutation methods.

### 3.4 Existing hardware runtime sharing is already complete

PR11 must not alter the common runtime ownership:

~~~text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
GameDisplayResolutionRuntime
~~~

XBOX mutations already live-reconcile through those same instances when the XBOX key is the effective active target.

---

## 4. Introduce one narrow Quick Settings profile target contract

Replace the shared Quick Settings Profile context's Steam-only \`uint? AppId\` with one explicit transient target.

Recommended contract:

~~~csharp
public enum QuickSettingsProfileTargetKind
{
    Steam,
    Xbox
}

public sealed record QuickSettingsProfileTarget(
    QuickSettingsProfileTargetKind Kind,
    uint? SteamAppId = null,
    string? XboxGameKey = null);
~~~

Add one structural validator.

Required validity:

~~~text
Steam
→ SteamAppId > 0
→ XboxGameKey == null

Xbox
→ SteamAppId == null
→ XboxGameKey non-empty

Device page
→ ProfileTarget == null
~~~

This is not:

- persisted game identity;
- a universal game platform framework;
- a replacement for ActiveProfileTarget;
- an input to hardware runtimes.

It exists only for Quick Settings page/mutation correlation across frontend transport boundaries.

---

## 5. Update shared Quick Settings contracts

Change:

~~~csharp
QuickSettingsPageSnapshot.AppId
~~~

to:

~~~csharp
QuickSettingsPageSnapshot.ProfileTarget
~~~

and change:

~~~csharp
QuickSettingsMutationIntent.AppId
~~~

to:

~~~csharp
QuickSettingsMutationIntent.ProfileTarget
~~~

Expected shape:

~~~csharp
public sealed record QuickSettingsPageSnapshot(
    QuickSettingsPageId PageId,
    QuickSettingsProfileTarget? ProfileTarget,
    bool Available,
    string? Message,
    IReadOnlyList<QuickSettingsSection> Sections,
    IReadOnlyList<QuickSettingsLinkedSliderConstraint> LinkedSliderConstraints);
~~~

Device page always uses null.

Profile page uses the exact active Steam/XBOX target that produced the page.

Unavailable Profile may carry:

- the attempted valid target for a stale/failure settlement; or
- null when there is no active game.

Update \`QuickSettingsPageSnapshot.Unavailable\` accordingly.

---

## 6. Frontend protocol bump

Because the shared Quick Settings contracts are serialized on the desktop frontend pipe, bump:

~~~text
FrontendTransportProtocol.CurrentVersion
59 → 60
~~~

Update comments/tests.

No compatibility shim is required; this project is unreleased.

---

## 7. Overlay protocol bump and catalog retirement

Bump:

~~~text
OverlayTransportProtocol.CurrentVersion
15 → 16
~~~

The existing Profile catalog/offline selected-profile messages become dead product surface and must be removed.

Remove:

~~~text
OverlayWireMessageKind.ProfileCatalogRequest
OverlayWireMessageKind.ProfileCatalogState
OverlayWireMessageKind.ProfilePageRequest
OverlayWireMessageKind.ProfilePageResult

OverlayProfileCatalogState
OverlayProfilePageRequest
OverlayProfilePageResponse
~~~

Remove matching:

- client handlers;
- server handlers;
- message payload members;
- validation branches;
- BindProfileCatalogAuthority;
- OverlayProcessController catalog delegates;
- tests that exist only for the retired catalog/page request wire.

Keep:

~~~text
QuickSettingsPageState
QuickSettingsMutationRequest
QuickSettingsMutationResult
OverlayCommand.ShowActiveProfile
~~~

\`ShowActiveProfile\` remains useful: it selects Profile first, which now shows either the active game or the no-game empty state.

---

## 8. IAddonFrontendControl Quick Settings signature

Change the generic Quick Settings capture seam to use the new profile target:

~~~csharp
Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(
    QuickSettingsPageId pageId,
    QuickSettingsProfileTarget? profileTarget = null,
    CancellationToken cancellationToken = default);
~~~

Device:

~~~text
(Device, null)
→ current Device projection
~~~

Profile:

~~~text
(Profile, valid active target)
→ current active Steam or XBOX profile projection

(Profile, null)
→ unavailable / no active game
~~~

Do not preserve offline profile capture through this generic Quick Settings seam.

Main App offline editing already has the dedicated Steam/XBOX typed APIs.

---

## 9. Runtime target conversion

Keep \`ActiveProfileTarget\` internal and authoritative.

Add one narrow converter, conceptually:

~~~csharp
private QuickSettingsProfileTarget? CaptureActiveQuickSettingsProfileTarget()
{
    var target = CaptureActiveProfileTarget();
    return target.Kind switch
    {
        ActiveProfileTargetKind.Steam => QuickSettingsProfileTarget.ForSteam(target.SteamAppId),
        ActiveProfileTargetKind.Xbox => QuickSettingsProfileTarget.ForXbox(target.XboxGameKey!),
        _ => null,
    };
}
~~~

Equivalent code is acceptable.

Do not expose \`ActiveProfileTarget\` through public contracts.

---

## 10. Active Profile capture

Rewrite \`CaptureOverlayProfileQuickSettingsPageAsync\`:

~~~text
target = CaptureActiveQuickSettingsProfileTarget()

target == null
→ QuickSettingsPageSnapshot.Unavailable(
     Profile,
     null,
     "No game is currently running. Start a game to configure its profile.")

target != null
→ _frontendControl.CaptureQuickSettingsPageAsync(Profile, target)
~~~

No direct \`ActualRunningAppId\` test remains in this method.

---

## 11. Steam/XBOX Profile projection

### 11.1 Steam

Preserve current visible Steam Profile semantics:

- Profile Enabled;
- TDP;
- CPU Boost;
- Windows Power Mode;
- Intel FPS Limit;
- Resolution;
- AC/DC visibility policy;
- limits/options/labels;
- mutation availability.

### 11.2 XBOX

Add XBOX projection using \`FrontendXboxGameProfileSnapshot\`.

PR11 XBOX Overlay rows are exactly:

~~~text
Profile Enabled
TDP
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

Do not add M1/M2 in PR11.

### 11.3 Presentation code reuse

Do not duplicate all section construction once for Steam and once for XBOX.

It is acceptable to introduce one **private presentation-only projection** inside \`QuickSettingsPresentation\` if necessary, containing only the common fields already rendered by the Profile page.

Example concept:

~~~text
ProfileQuickSettingsProjection
  DisplayName
  Enabled
  CpuBoost
  Tdp
  TdpLimits
  PowerMode
  FpsLimit
  Resolution
  PersistenceWritable
~~~

Then:

~~~text
FrontendGameProfileSnapshot      ─┐
                                 ├→ private projection → existing section builders
FrontendXboxGameProfileSnapshot ─┘
~~~

Do not use this as a new persistence DTO or runtime apply model.

Do not merge the existing Steam/XBOX frontend profile contracts.

---

## 12. Active-target admission

A Profile Quick Settings request is valid only for the Runtime's current active target.

Inside \`InProcessAddonFrontendControl\`:

~~~text
requested QuickSettingsProfileTarget
vs
_activeProfileTargetSource()
~~~

must match exactly before a page is returned writable.

Examples:

~~~text
requested Steam:123
current Steam:123
→ valid

requested Steam:123
current Xbox:key
→ unavailable

requested Xbox:keyA
current Xbox:keyA
→ valid

requested Xbox:keyA
current Xbox:keyB
→ unavailable

requested target
current None
→ unavailable
~~~

Do not allow offline Quick Settings mutation now that Overlay catalog mode is retired.

---

## 13. Mutation adapter dispatch

Update \`QuickSettingsMutationAdapter.MutateProfileAsync\` to use \`QuickSettingsProfileTarget\`.

Required sequence:

~~~text
validate target structure
→ verify requested target is still current
→ capture fresh current page
→ verify EditedRowId is Available + Writable
→ dispatch by target kind
→ return freshly projected authoritative page
~~~

Steam target:

~~~text
ProfileEnabled        → SetGameProfileEnabledAsync
CpuBoost...           → SetGameProfile...
Tdp...                → SetGameProfile...
PowerMode...          → SetGameProfile...
Fps...                → SetGameProfile...
Resolution            → SetGameProfileResolutionAsync
~~~

Xbox target:

~~~text
ProfileEnabled        → SetXboxGameProfileEnabledAsync
CpuBoost...           → SetXboxGameProfile...
Tdp...                → SetXboxGameProfile...
PowerMode...          → SetXboxGameProfile...
Fps...                → SetXboxGameProfile...
Resolution            → SetXboxGameProfileResolutionAsync
~~~

Do not call ProfileStore directly.

Do not call hardware runtimes directly.

---

## 14. Stale mutation / active-target change

The Overlay binding currently treats \`(PageId, AppId)\` as the page context.

Change the staleness identity to:

~~~text
(PageId, ProfileTarget)
~~~

If the Runtime replaces:

~~~text
Xbox:A → Xbox:B
Steam:123 → Xbox:A
Xbox:A → None
~~~

while a delayed edit is pending/in flight:

- unsubmitted drafts for the old target are canceled;
- a late old-target result must not replace the new page;
- a late old-target exception must not paint an error over the new page.

Reuse the existing page-binding generation/pending machinery.

Do not add an epoch manager.

---

## 15. Mutation backstop in AddonProcessHost

Current code captures only:

~~~text
activeAppIdBefore
activeAppIdAfter
~~~

around an Overlay mutation.

Replace it with:

~~~text
activeTargetBefore = CaptureActiveProfileTarget()
activeTargetAfter  = CaptureActiveProfileTarget()
~~~

If they differ after the mutation and Overlay is still visible/captured:

~~~text
→ RefreshQuickSettingsAsync()
~~~

This catches XBOX start/exit/switch as well as Steam changes.

No new state machine.

---

## 16. Refresh visible Overlay when active target changes

A visible Overlay must not wait for an unrelated later settings invalidation.

After normal active-target transitions:

~~~text
OnActualRunningAppIdChanged
OnActiveXboxGameChanged
~~~

request a best-effort Quick Settings refresh when:

~~~text
_overlayCaptureActive
&& _overlayController.IsVisible
~~~

Reuse \`RefreshQuickSettingsAsync()\`.

Do not create another profile-refresh transport message.

The refresh may include Device + Profile as it already does; correctness matters more than a tiny extra snapshot at these low-frequency lifecycle events.

---

## 17. Overlay Profile UI simplification

Delete the catalog/selected-detail state machine.

Remove concepts such as:

~~~text
ProfilePresentationMode.Catalog
ProfilePresentationMode.SelectedDetail
_selectedCatalogAppId
_profileCatalog
_profileCatalogGrid
_profileCatalogCards
ProfileCatalogRequestRequested
ProfilePageRequestRequested
selected-profile Back behavior
selected-profile tab-leave flush path
~~~

Profile tab owns:

~~~text
_profileDetailRoot
_noActiveGameMessage
active profile page binding
~~~

The generic Quick Settings page renderer remains the active-detail renderer.

---

## 18. No-active-game empty state

When Profile page is unavailable because there is no active target, do not render the generic unavailable section/card layout.

Show one standalone centered text block:

> **No game is currently running. Start a game to configure its profile.**

Required presentation:

~~~text
HorizontalAlignment = Center
VerticalAlignment   = Center
TextAlignment       = Center
TextWrapping        = Wrap
FontSize            ≈ 20 DIP
~~~

Use the existing Overlay theme text brush/style where practical.

The message should be larger than ordinary row labels but not a giant page title.

Do not show:

- Steam catalog cards;
- XBOX catalog cards;
- disabled stale Profile rows;
- Device settings behind the message.

When a game starts while Overlay remains open:

~~~text
centered message
→ active profile detail
~~~

When the game exits:

~~~text
active profile detail
→ centered message
~~~

---

## 19. ShowActiveProfile behavior

Keep \`OverlayCommand.ShowActiveProfile\`.

Behavior:

~~~text
active Steam/XBOX game
→ show Overlay with Profile selected and active detail visible

no active game
→ show Overlay with Profile selected and centered no-game message
~~~

Do not fall back to catalog.

---

## 20. M1/M2 explicitly deferred to PR12

PR11 must not add the XBOX per-game Controller section.

The architecture is already locked for PR12:

~~~text
Active XBOX Profile

Profile / game heading
Profile Enabled

Controller
  Use global M1 / M2 mapping
  M1
  M2

TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

The Controller section must be above the existing performance/display sections.

Steam active Profile must not show those rows.

The separate Overlay Controller tab remains the global M1/M2 fallback editor.

This deferral keeps PR11 focused on:

- active target identity;
- transport;
- catalog retirement;
- performance/display parity.

---

## 21. Source files expected to change

Likely:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.Validation.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs

src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
~~~

Remove retired catalog-only helper files if they become unused.

Do not rename unrelated Steam Main App profile types/files in this PR.

---

## 22. Contract tests

Add/adjust tests for \`QuickSettingsProfileTarget\`:

1. valid Steam target;
2. valid XBOX target;
3. Steam target with Xbox key rejected;
4. Steam target with AppId 0/null rejected;
5. XBOX target with Steam AppId rejected;
6. XBOX empty key rejected;
7. Device page target must be null;
8. available Profile page requires a structurally valid target.

---

## 23. Quick Settings presentation tests

Required:

### Steam

Current section/row order and semantics remain unchanged.

### XBOX

Verify:

~~~text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

where supported.

Verify:

- XBOX DisplayName becomes the section heading;
- Profile Enabled controls child writability exactly like Steam;
- TDP limits come from the same runtime policy;
- current-power-source visibility applies identically;
- no per-game M1/M2 rows exist yet in PR11.

---

## 24. Mutation dispatch tests

For every Profile row family, verify both target kinds dispatch to the correct existing typed method.

Required Steam/XBOX coverage:

- Profile Enabled;
- CPU Boost enable / AC / DC;
- TDP enable / grouped values;
- Power Mode enable / AC / DC;
- FPS enable / AC / DC;
- Resolution.

Also verify:

~~~text
requested target != current target
→ zero typed mutations
→ unavailable/stale settlement
~~~

and:

~~~text
current target changes after page capture but before mutation dispatch
→ mutation fails closed
~~~

Do not add pathological instruction-level race machinery; just test the existing real stale-context boundary.

---

## 25. Overlay transport tests

Bump Overlay v15 → v16.

Verify:

- v15 peer rejected;
- QuickSettings page with Steam target round-trips;
- QuickSettings page with XBOX target round-trips;
- mutation intent with Steam target round-trips;
- mutation intent with XBOX target round-trips;
- malformed profile target rejected;
- Device target non-null rejected;
- old ProfileCatalog/ProfilePage message kinds/types are absent.

---

## 26. Frontend transport tests

Bump Frontend v59 → v60.

Verify shared Quick Settings capture/mutation DTO serialization with the new target contract.

No other frontend feature behavior changes.

---

## 27. Overlay UI tests

Update source/UI tests to prove:

- no catalog grid/cards;
- no Profile catalog request events;
- no selected/offline Profile navigation mode;
- Profile tab has one active detail surface;
- no-active-game centered message exists with the exact text:
  \`No game is currently running. Start a game to configure its profile.\`
- message is centered horizontally and vertically;
- font size is approximately 20 DIP;
- active page hides the message;
- unavailable/no-target page shows the message and hides profile rows;
- \`ShowActiveProfile\` selects Profile whether or not a game is active.

---

## 28. Visible lifecycle validation

### No game → XBOX

~~~text
Overlay visible on Profile
→ centered no-game message

launch XBOX title
→ XboxGameSessionRuntime.ActiveGame set
→ active target becomes Xbox:key
→ visible Overlay refreshes
→ XBOX Profile rows appear
~~~

### XBOX → no game

~~~text
game exits
→ ActiveGame clears
→ shared performance runtimes return to Device/global policy
→ visible Overlay refreshes
→ centered no-game message
~~~

### Steam → XBOX

If this real transition is produced during testing:

~~~text
Steam target
→ Xbox target
→ stale Steam drafts/results cannot overwrite Xbox page
~~~

No new multi-game arbitration is required.

### XBOX Alt+Tab

Alt+Tab away while XBOX game process remains alive:

~~~text
ActiveXboxGame retained
→ XBOX profile remains visible/effective
~~~

### Resume

If Overlay is reopened after resume while the XBOX game is still running:

~~~text
bounded XboxGameSessionRuntime reconcile
→ active target restored
→ XBOX Profile page captured
~~~

---

## 29. Regression locks

Must remain unchanged:

~~~text
SteamSessionRuntime.ActualRunningAppId authority
XboxGameSessionRuntime identity/classifier
ActiveProfileTarget selection priority
ProfileDocument.Games
ProfileDocument.XboxGames
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
GameDisplayResolutionRuntime
global M1/M2 Overlay Controller tab
per-game XBOX M1/M2 persistence/apply from PR699
CanonicalXbox360InputPublisher
SteamDeck presentation
PID1902 / HidHide / VIIPER ownership
~~~

PR11 does not change controller presentation.

---

## 30. Overengineering constraints

Do not add:

- IGamePlatform;
- universal persisted game ID;
- generic profile provider hierarchy;
- Overlay profile manager;
- separate XBOX Quick Settings renderer;
- separate XBOX hardware apply path;
- active-profile epoch manager;
- background polling;
- Custom EXE support;
- Epic/GOG.

One narrow transport target record is sufficient.

---

## 31. Required validation commands

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

Also perform source audits:

~~~text
ProfileCatalogRequest
ProfileCatalogState
ProfilePageRequest
ProfilePageResult
_selectedCatalogAppId
ProfilePresentationMode.Catalog
ProfilePresentationMode.SelectedDetail
~~~

Expected production-source count after PR11: zero.

Search:

~~~text
QuickSettingsPageSnapshot.AppId
QuickSettingsMutationIntent.AppId
~~~

Expected production-source count after migration: zero.

---

## 32. Completion condition

PR11 is complete when:

~~~text
Runtime active target
        │
        ├─ Steam AppID
        ├─ XBOX key
        └─ None
        ↓
QuickSettingsProfileTarget
        ↓
one generic Profile Quick Settings projection
        ↓
Overlay Profile
        ↓
existing typed Steam/XBOX mutation methods
        ↓
same shared performance/display runtimes
~~~

and:

~~~text
No game
→ centered:
  "No game is currently running. Start a game to configure its profile."

Overlay catalog/offline editing
→ removed

XBOX M1/M2 Profile rows
→ not yet in PR11
→ PR12, above performance/display sections

Overlay Controller M1/M2
→ still global fallback editor
~~~

No additional game-detection or hardware-apply architecture is introduced.
