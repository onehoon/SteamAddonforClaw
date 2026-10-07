# Work Order — XBOX Overlay M1/M2 Enable Semantics + Profile-First Loading/No-Game UX Polish

> Date: 2026-10-07
> Repository: onehoon/SteamAddonforClaw
> Reviewed main: 9f68dec6d7c4bcae8897448d9fea122ff66e032e
> Product baseline: standalone Full1902
> Authority: docs/Full 1902 Implementation/README.md + docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
> Relevant completed work: PR #699 / #700 / #701 / #712
> Scope: fix the remaining XBOX Overlay M1/M2 UI semantics, remove the false no-game flash during Profile-first startup, make the real no-game empty state smaller/two-line, and publish Profile before Device when Profile is intentionally selected first.
> Protocol: no frontend/Overlay protocol bump.

---

## 1. Goal

Finish the Overlay-side UX correction that was intentionally left out of the earlier Main App M1/M2 fix.

Current user-visible problems:

~~~text
1. Main App XBOX:
   M1 / M2 Button Mapping [ON]
   → per-game override enabled

   Overlay XBOX Profile:
   Controller
   Use global M1 / M2 mapping [ON]
   → M1/M2 disabled

   The same feature has opposite visible toggle meaning.

2. Active XBOX game:
   Overlay opens on Profile,
   but "No game is currently running..." flashes briefly,
   then the real XBOX Profile appears.

3. Active XBOX Profile feels slower than Steam.
   Current post-show Quick Settings refresh always captures/publishes:
   Device → Profile
   even when Profile is the selected startup tab.

4. The real no-game message is visually too large and currently reads as one long wrapped sentence block.
~~~

Target:

~~~text
active XBOX game
→ Profile selected first
→ stale old Profile removed
→ blank/non-writable body while waiting
→ Profile published before Device
→ real XBOX Profile appears

XBOX Profile:
M1 / M2 Button Mapping                         [ON]
    M1                                          <target>
    M2                                          <target>

ON  = per-game override enabled / M1-M2 editable
OFF = global fallback / M1-M2 non-editable
~~~

Actual no-game state:

~~~text
No game is currently running.
Start a game to configure its profile.
~~~

The two sentences must render as exactly two lines on 1920x1200 at 150% scaling.

---

## 2. Supersession

The previous 1007 XBOX M1/M2 follow-up work order explicitly said not to redesign the Overlay M1/M2 wording in that PR.

That restriction is now superseded for this exact scope.

PR12 historically defined:

~~~text
Controller
Use global M1 / M2 mapping
M1
M2
~~~

The new product contract is:

~~~text
M1 / M2 Button Mapping                         [toggle]
M1
M2
~~~

with normal enable semantics matching the Main App.

Do not use this work to redesign unrelated Overlay tabs.

---

## 3. Full1902 boundaries

Do not change:

- PID1902 / PID1901 ownership;
- DirectInput physical ownership;
- HidHide baseline or recovery;
- VIIPER ownership/teardown;
- Xbox360 / SteamDeck presentation authority;
- sleep / hibernate / resume;
- restart / crash / shutdown recovery;
- Center M authority;
- WING / Game Bar suppression;
- XBOX active-game detection;
- Steam detection;
- ProfileStore schema.

This PR is a presentation/mutation-semantics/publication-order fix only.

---

## 4. Preserve the existing M1/M2 persistence contract

Do not change:

~~~text
XboxGameProfile.Controller.BackButtonMapping == null
→ use global BackButtonMappingSettings

XboxGameProfile.Controller.BackButtonMapping != null
→ explicit per-game mapping
~~~

The Main App already projects this as a normal feature toggle:

~~~text
OFF → null/global
ON  → explicit mapping
~~~

The Overlay must now do the same.

---

## 5. Use the existing generic Overlay feature-header renderer

Do not create a special XBOX renderer.

Current generic rendering already uses:

~~~text
OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(...)
~~~

When the first visible row is a Toggle, the renderer promotes it into the feature header and uses the section label as the visible header label.

Therefore use the existing generic path.

Required section label:

~~~text
M1 / M2 Button Mapping
~~~

No new BuildXboxM1M2Section, custom XBOX row class, or separate Overlay view is needed.

---

## 6. Correct QuickSettingsPresentation semantics

File:

~~~text
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
~~~

Current semantics:

~~~text
Value = configuration.UseGlobalMapping
M1/M2 writable = PersistenceWritable && !UseGlobalMapping
Section label = Controller
Row label = Use global M1 / M2 mapping
~~~

Change to:

~~~csharp
var configuration = snapshot.BackButtonMapping!;
var perGameEnabled = !configuration.UseGlobalMapping;
var mappingWritable = snapshot.PersistenceWritable && perGameEnabled;
~~~

Required first row:

~~~text
RowId       = ProfileBackButtonUseGlobal
Label       = M1 / M2 Button Mapping
ControlKind = Toggle
Value       = perGameEnabled
Writable    = snapshot.PersistenceWritable
Commit      = Immediate
~~~

Required section:

~~~text
SectionId = ProfileController
Label     = M1 / M2 Button Mapping
Rows      = toggle, M1, M2
~~~

Required projection:

~~~text
UseGlobalMapping = true
→ toggle OFF
→ current global fallback values displayed
→ M1/M2 non-writable

UseGlobalMapping = false
→ toggle ON
→ explicit per-game values displayed
→ M1/M2 writable when PersistenceWritable
~~~

Do not gate M1/M2 persistence on Profile.Enabled. Preconfiguration while the Profile master toggle is Off remains supported.

---

## 7. Keep the legacy row ID

Keep:

~~~text
QuickSettingsRowId.ProfileBackButtonUseGlobal
~~~

Do not rename or add a new row enum only for naming cleanup.

The row ID is an existing transport identity. Its displayed Boolean now means per-game mapping enabled even though its legacy enum name contains UseGlobal.

A short source comment is acceptable.

This avoids a needless frontend/Overlay protocol bump.

---

## 8. Invert mutation semantics too

File:

~~~text
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
~~~

Presentation and mutation must agree.

New Boolean meaning:

~~~text
true  = per-game mapping enabled
false = per-game mapping disabled / use global
~~~

Toggle OFF:

~~~text
false
→ SetXboxGameProfileBackButtonMappingAsync(key, null)
~~~

Toggle ON:

~~~text
true
→ read currently displayed M1/M2
→ validate both enum values
→ construct whole BackButtonMappingSettings
→ SetXboxGameProfileBackButtonMappingAsync(key, mapping)
~~~

When enabling from global mode, seed the explicit mapping from the currently displayed global fallback values already carried by FrontendGameBackButtonMappingConfiguration.Mapping.

Do not seed from hard-coded defaults.

---

## 9. Invert grouped M1/M2 admission

The delayed group keeps the same row IDs:

~~~text
ProfileBackButtonUseGlobal
ProfileBackButtonM1
ProfileBackButtonM2
~~~

After this PR:

~~~text
group Boolean == true
→ explicit mapping enabled
→ M1/M2 grouped mutation admitted

group Boolean == false
→ global fallback mode
→ grouped M1/M2 mutation rejected
~~~

Keep existing structural validation:

- one toggle row;
- one M1;
- one M2;
- no duplicates;
- no unrelated rows;
- valid Boolean;
- valid Xbox360BackButtonTarget values;
- whole mapping validation.

Do not add another debounce, queue, revision, lock, or mapping state object.

---

## 10. Existing generic binding remains authoritative

Keep the existing ProfileBackButtonMapping 300 ms grouped draft path.

Required case after inversion:

~~~text
per-game mapping ON
→ user changes M1/M2
→ delayed group draft exists
→ user turns M1/M2 feature OFF
→ pending group draft canceled
→ immediate false/null mutation submitted
~~~

Update test names/comments that still describe the visible toggle as Use Global.

No XBOX-specific binding state.

---

## 11. XBOX Profile capture itself is not the main delay source

Current code does not perform a fresh installed-XBOX catalog scan every time the Overlay Profile opens.

Steam capture:

~~~text
CaptureActiveGameProfileAsync
→ CaptureGameProfileAsync
→ GameProfileMutations.CaptureProfile
→ ProfileStore.Load
→ optional Steam display-name catalog enrichment when missing
~~~

XBOX capture:

~~~text
CaptureXboxGameProfileAsync
→ CaptureXboxGameProfile
→ XboxGameProfileMutations.CaptureProfile
→ ProfileStore.Load
→ active XboxGameSessionRuntime display name when saved name is blank
~~~

The visible delay is mainly caused by the Overlay show/publication sequence, not by XBOX profile identity resolution.

Current initial Show path:

~~~text
ShowActiveProfile
→ ResetUiForShow
→ Overlay show animation (~180 ms)
→ Visible acknowledgement
→ PauseForOverlayAsync
→ controller capture commit
→ RefreshQuickSettingsAsync
→ Device capture/publish
→ Profile capture/publish
→ Profile render
~~~

Do not add Profile caching or another XBOX game detector.

---

## 12. The false no-game flash is directly caused by Overlay code

File:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
~~~

Current BuildProfilePage creates the status TextBlock with NoActiveGameMessage as its initial text.

Current PrepareActiveProfileFirstShow also injects:

~~~csharp
QuickSettingsPageSnapshot.Unavailable(
    QuickSettingsPageId.Profile,
    null,
    NoActiveGameMessage)
~~~

before the authoritative Profile page arrives.

Therefore an already-confirmed active XBOX game currently follows:

~~~text
Runtime knows game exists
→ ShowActiveProfile
→ Overlay explicitly renders "No game..."
→ later authoritative XBOX page arrives
→ replace message
~~~

Fix this sequence.

---

## 13. Add one narrow Overlay-local pending flag

A single local presentation flag is justified:

~~~csharp
private bool _activeProfileInitialLoadPending;
~~~

It distinguishes only:

~~~text
Profile-first Show is waiting for the authoritative page
vs
Runtime authoritatively reported no active game
~~~

It is not a new game/profile authority.

Do not create a loading enum/state machine/manager.

---

## 14. PrepareActiveProfileFirstShow must show blank, not No Game

Required flow:

~~~text
ShowActiveProfile
→ clear stale previous Profile controls
→ set _activeProfileInitialLoadPending = true
→ apply a non-writable temporary unavailable page
→ hide Profile detail
→ hide status message
→ select Profile
→ reveal Overlay
~~~

A temporary internal message such as:

~~~text
Loading the active game profile.
~~~

may be used inside the temporary snapshot, but it must not be user-visible.

Conceptual implementation:

~~~csharp
_activeProfileInitialLoadPending = true;

var loading = QuickSettingsPageSnapshot.Unavailable(
    QuickSettingsPageId.Profile,
    null,
    "Loading the active game profile.");

surface.Binding?.ApplyAuthoritativePage(loading);
RenderQuickSettingsPage(surface);
ApplyProfilePresentation(loading);
~~~

This still retires stale writable controls from the previous game.

---

## 15. ApplyProfilePresentation must honor pending before anything else

Required first branch:

~~~csharp
if (_activeProfileInitialLoadPending)
{
    _profileDetailRoot.Visibility = Visibility.Collapsed;
    _profileStatusMessage.Visibility = Visibility.Collapsed;
    return;
}
~~~

This is necessary because the Show path later calls:

~~~text
ApplySelectedTabVisualState
→ OnProfileTabSelectionChanged(true)
→ ApplyProfilePresentation(binding.AuthoritativePage)
~~~

Without the pending guard, the temporary unavailable page would immediately show a status message again.

---

## 16. Clear pending only when a real Profile page is published

In ApplyActiveProfilePage:

~~~text
page.PageId must be Profile
→ _activeProfileInitialLoadPending = false
→ apply authoritative page
→ render
→ ApplyProfilePresentation
~~~

Then:

~~~text
Available + valid ProfileTarget
→ Profile detail visible
→ status hidden

Unavailable + no active target
→ no-game status visible

Unavailable + active target + failure message
→ failure status visible
~~~

The Overlay must not perform another game-detection query.

---

## 17. No-game authority remains Runtime-owned

Keep:

~~~text
AddonProcessHost.CaptureOverlayProfileQuickSettingsPageAsync
→ CaptureActiveQuickSettingsProfileTarget()

target == null
→ authoritative no-game page

target != null
→ capture exact Steam/XBOX Profile
~~~

The no-game message must only become visible from that authoritative no-target result.

Do not query Steam, XboxGameSessionRuntime, packages, processes, windows, or catalogs from the Overlay process.

---

## 18. No-game copy must be explicitly two lines

Displayed no-game copy:

~~~text
No game is currently running.
Start a game to configure its profile.
~~~

Use one explicit line break between the two sentences.

The Runtime may keep its existing semantic one-line message if desired; the Overlay can format the known no-game result for display.

Do not add localization infrastructure in this PR.

---

## 19. Reduce no-game/status font size

Current status text forces FontSize 20.

Target:

~~~text
FontSize = 16 DIP
HorizontalAlignment = Center
VerticalAlignment = Center
TextAlignment = Center
~~~

Keep the existing QAM body style, but remove the duplicate 20 DIP overrides.

Primary manual target:

~~~text
1920 x 1200
150% Windows scaling
~~~

Required visual result:

~~~text
No game is currently running.          ← one line
Start a game to configure its profile. ← one line
~~~

No third wrapped line and no clipping.

Non-no-game failure messages may retain normal wrapping.

Do not shrink below 16 DIP just to defend unsupported pathological widths.

---

## 20. Publish Profile before Device on Profile-first Show

File:

~~~text
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
~~~

Current:

~~~text
RefreshQuickSettingsAsync
→ Device
→ Profile
~~~

Change narrowly to:

~~~csharp
RefreshQuickSettingsAsync(bool profileFirst = false)
~~~

Required:

~~~text
profileFirst == false
→ Device
→ Profile

profileFirst == true
→ Profile
→ Device
~~~

Keep the existing _quickSettingsRefreshGate and sequential page capture/publish behavior.

Do not parallelize Device/Profile.

Do not add another gate.

---

## 21. Reuse the existing preferActiveProfile local fact

Current AddonProcessHost Show path already derives:

~~~csharp
var preferActiveProfile =
    CaptureActiveQuickSettingsProfileTarget() is not null;
~~~

and passes it to ShowAsync.

After successful Show → Pause → capture commit, call:

~~~csharp
_ = _overlayController.RefreshQuickSettingsAsync(preferActiveProfile);
~~~

instead of the unconditional default refresh.

Normal later StateInvalidated and active-target refreshes may continue using:

~~~csharp
RefreshQuickSettingsAsync()
~~~

This keeps the optimization local to initial Profile-first Show.

No persistent tab-priority state is needed.

---

## 22. Preserve lifecycle ordering

Do not move Profile capture before:

- Overlay Visible acknowledgement;
- presentation neutral/pause;
- controller capture commit.

Required lifecycle remains:

~~~text
Show acknowledged
→ presentation pause/neutral
→ controller capture committed
→ best-effort Quick Settings publication
~~~

Only the Device-vs-Profile order inside the post-commit batch changes.

This is not a reason to redesign Full1902 capture/lifecycle.

---

## 23. Expected production files

Expected:

~~~text
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
~~~

If changes spread into these areas, stop and re-evaluate:

~~~text
ProfileStore
XboxGameProfileMutations
XboxGameSessionRuntime
ActiveProfileTarget
CanonicalXbox360InputPublisher
VIIPER
HidHide
PID1902 ownership
transport DTO/message definitions
~~~

---

## 24. Presentation tests

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
~~~

Required global-fallback case:

~~~text
UseGlobalMapping = true
Mapping = LB/RB
PersistenceWritable = true

expected:
section label = M1 / M2 Button Mapping
toggle = false
M1 = LB
M2 = RB
M1/M2 writable = false
~~~

Required explicit case:

~~~text
UseGlobalMapping = false
Mapping = X/Y
PersistenceWritable = true

expected:
section label = M1 / M2 Button Mapping
toggle = true
M1 = X
M2 = Y
M1/M2 writable = true
~~~

Also prove:

- Profile disabled does not prevent explicit M1/M2 editing when persistence is writable;
- PersistenceWritable false disables header + M1/M2;
- Steam Profile still has no ProfileController section.

---

## 25. Mutation tests

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
~~~

Required:

1. toggle true → submit explicit mapping seeded from current M1/M2;
2. toggle false → submit null;
3. M1 grouped edit with Boolean true → whole mapping;
4. M2 grouped edit with Boolean true → whole mapping;
5. grouped edit with Boolean false → reject / zero mutation;
6. invalid/missing/duplicate group rows → reject;
7. Steam target + ProfileBackButton row → reject;
8. stale XBOX target → existing fail-closed admission.

Rename tests/comments away from visible Use Global semantics.

---

## 26. Binding tests

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayQuickSettingsPageBindingTests.cs
~~~

Change the existing pending-draft toggle case to:

~~~text
per-game mapping ON
→ pending M1/M2 group draft
→ user toggles feature OFF
→ pending group canceled
→ one immediate OFF mutation
~~~

The group Boolean must now be true while M1/M2 editing is enabled.

Keep existing target-retirement and late-result protection.

---

## 27. Loading/no-game UI tests

Update/add focused coverage in:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

or one small Profile presentation test file.

Prove:

~~~text
PrepareActiveProfileFirstShow
→ pending true
→ old controls retired
→ detail hidden
→ status hidden

OnProfileTabSelectionChanged(true) while pending
→ still blank
→ no No Game text
→ no Loading text
→ no Unavailable text

ApplyActiveProfilePage(available active target)
→ pending false
→ detail visible
→ status hidden

ApplyActiveProfilePage(authoritative no-target unavailable)
→ pending false
→ detail hidden
→ no-game status visible

ApplyActiveProfilePage(active-target failure)
→ pending false
→ exact failure status visible
~~~

Do not add scheduler-interleaving stress tests.

---

## 28. No-game visual tests

Prove source/helper contract:

~~~text
FontSize = 16
HorizontalAlignment.Center
VerticalAlignment.Center
TextAlignment.Center
~~~

and the no-game display text has exactly one explicit line break:

~~~text
No game is currently running.
Start a game to configure its profile.
~~~

Do not write brittle synthetic pixel/font-measurement tests.

Manual validation covers actual fitting.

---

## 29. Publication-order tests

Update the nearest existing focused tests:

~~~text
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayQuickSettingsContractTests.cs
~~~

Prove:

~~~text
RefreshQuickSettingsAsync()
→ Device capture/publish before Profile

RefreshQuickSettingsAsync(profileFirst: true)
→ Profile capture/publish before Device
~~~

And in CoordinateOverlayToggleAsync:

~~~text
preferActiveProfile derived before Show
Show acknowledged
Pause succeeds
capture committed
RefreshQuickSettingsAsync(preferActiveProfile)
~~~

The refresh must remain after capture commit.

---

## 30. Protocol policy

Do not change:

~~~text
FrontendTransportProtocol.CurrentVersion
OverlayTransportProtocol.CurrentVersion
~~~

No enum member, enum name, DTO field, RPC, wire-message kind, or payload shape changes.

The legacy ProfileBackButtonUseGlobal row identity is intentionally retained.

---

## 31. Manual validation

### Active XBOX game

~~~text
Open Overlay
→ Profile selected first
→ no "No game..." flash
→ blank during any capture delay
→ Profile content appears

M1 / M2 Button Mapping [OFF]
→ M1/M2 visible, disabled, global fallback

turn ON
→ M1/M2 editable
→ values seeded from displayed global fallback

change M1/M2
→ close/reopen
→ explicit values persist

turn OFF
→ global fallback resumes
~~~

### Active Steam game

~~~text
Open Overlay
→ Profile selected first
→ same blank-loading behavior
→ no false no-game flash
→ Steam Profile appears
~~~

### No active game

~~~text
Profile empty state:

No game is currently running.
Start a game to configure its profile.
~~~

At 1920x1200 / 150%:

- exactly two lines;
- one sentence per line;
- no third wrapped line;
- no clipping;
- clearly smaller than the old 20 DIP state.

---

## 32. Required validation commands

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

---

## 33. Overengineering guardrails

Do not add:

- generic loading-state framework;
- Profile state machine;
- second active-game authority;
- profile cache/preload service;
- polling;
- new wire message;
- new row enum just for naming;
- another save/debounce generation layer;
- another synchronization gate;
- XBOX-specific renderer controls;
- Device/Profile parallel capture;
- lifecycle reordering to chase tiny timing gains.

The intended solution is only:

~~~text
M1/M2:
invert existing Overlay projection + mutation semantics

Profile startup:
one Overlay-local pending Boolean
→ blank until authoritative result

No game:
show only after Runtime no-target result
→ explicit two-line 16 DIP centered message

Responsiveness:
reuse preferActiveProfile
→ initial Profile-first refresh publishes Profile before Device
~~~

---

## 34. Completion condition

Complete when:

~~~text
XBOX Overlay:
M1 / M2 Button Mapping [OFF]
→ global fallback
→ M1/M2 read-only

M1 / M2 Button Mapping [ON]
→ explicit per-game override
→ M1/M2 editable
~~~

and:

~~~text
active Steam/XBOX Profile-first Show
→ no stale old controls
→ no false no-game message
→ blank pending body
→ Profile before Device
→ authoritative Profile appears
~~~

and:

~~~text
actual no-game state
→ centered 16 DIP
→ exactly two displayed lines:
   No game is currently running.
   Start a game to configure its profile.
~~~

while:

~~~text
ProfileStore schema unchanged
XboxGameProfileMutations unchanged
ActiveProfileTarget unchanged
XBOX session detector unchanged
generic Quick Settings binding retained
generic feature-header renderer retained
Show → neutral/pause → capture commit lifecycle order unchanged
PID1902 / HidHide / VIIPER untouched
no protocol bump
~~~
