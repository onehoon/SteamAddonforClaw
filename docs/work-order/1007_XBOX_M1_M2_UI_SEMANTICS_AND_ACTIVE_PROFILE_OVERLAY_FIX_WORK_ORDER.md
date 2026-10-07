# Work Order — XBOX M1/M2 Main-App Toggle Semantics + Active XBOX Overlay Profile-First Fix

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@60b5fe2ead4001f164bf6e00e69fe6d54f0a4f55`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and its active precedence chain  
> **Relevant completed work:** PR #699 / #700 / #701 and `PR12_XBOX_OVERLAY_PER_GAME_M1_M2_IMPLEMENTATION_CLOSURE_WORK_ORDER_2026-10-07.md`  
> **Scope:** correct the Main App XBOX M1/M2 editor's user-facing toggle semantics and fix Overlay first-tab selection for an active XBOX game.  
> **No protocol bump:** no frontend or Overlay wire contract changes are required.

---

## 1. Goal

Fix two post-implementation UX/correctness issues without changing the already-complete XBOX profile, M1/M2 persistence, controller-output, or Full1902 ownership architecture.

### Main App XBOX profile

Current visible layout is effectively:

~~~text
Controller                         Use global M1 / M2 mapping [ON]

M1                                                   Disabled
M2                                                   Disabled
~~~

The toggle is technically behaving according to the persisted `UseGlobalMapping` flag, but its visible semantics are inverted relative to every other feature card:

~~~text
toggle ON
→ use global mapping
→ M1/M2 editor disabled

toggle OFF
→ use per-game override
→ M1/M2 editor enabled
~~~

Change the user-facing model to:

~~~text
M1 / M2 Button Mapping                              [ON]
Optional per-game M1 / M2 mapping for Xbox 360 output.
Off uses the global mapping.

M1                                                   <target>
M2                                                   <target>
~~~

with normal product semantics:

~~~text
toggle ON
→ per-game M1/M2 override enabled
→ M1/M2 editable

toggle OFF
→ no per-game override
→ global M1/M2 fallback is used
→ M1/M2 non-editable
~~~

### Overlay first tab

Current Overlay show admission still decides whether to select Profile first from Steam-only `ActualRunningAppId`:

~~~csharp
var activeAppId = _runtimeHost?.ActualRunningAppId ?? 0;
var preferActiveProfile = activeAppId != 0;
~~~

Therefore:

~~~text
Steam game active
→ Profile selected first

XBOX game active
→ preferActiveProfile == false
→ configured first tab selected instead
~~~

This contradicts the already-implemented active Steam/XBOX profile authority.

Required behavior:

~~~text
active Steam game
→ Profile first

active XBOX game
→ Profile first

no recognized active game
→ configured Overlay first tab
~~~

---

## 2. Authority and scope rules

This PR is a narrow post-implementation correction.

It must **not** redesign any of the following:

- XBOX game detection;
- XBOX profile persistence;
- active-profile target selection;
- global M1/M2 persistence;
- per-game M1/M2 persistence;
- effective M1/M2 reconciliation;
- `CanonicalXbox360InputPublisher`;
- SteamDeck presentation;
- PID1902 ownership;
- HidHide;
- VIIPER;
- Full1902 lifecycle;
- Overlay Quick Settings product contracts;
- Overlay transport protocol.

The active architecture remains:

~~~text
XboxGameSessionRuntime.ActiveGame
        ↓
ActiveProfileTarget.Xbox
        ↓
ProfileDocument.XboxGames
        ↓
FrontendXboxGameProfileSnapshot
        ↓
existing typed mutation authorities
~~~

For M1/M2 specifically:

~~~text
mapping == null
→ use global BackButtonMappingSettings

mapping != null
→ explicit per-game Xbox360 M1/M2 mapping
~~~

Do not change this persistence contract.

---

## 3. Current code facts

### 3.1 Main App XBOX XAML

Current file:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
~~~

Current controller section:

~~~xml
<ctcontrols:SettingsExpander
    x:Name="ControllerMappingExpander"
    Header="Controller"
    Description="Optional per-game M1 / M2 mapping for Xbox 360 output."
    IsExpanded="False">

    ...

    <ToggleSwitch
        x:Name="UseGlobalBackButtonMappingToggle"
        Header="Use global M1 / M2 mapping"
        IsOn="True"
        ... />
~~~

This is the direct source of the confusing visible wording.

### 3.2 Main App XBOX render semantics

Current `RenderBackButtonMapping`:

~~~csharp
UseGlobalBackButtonMappingToggle.IsOn = configuration.UseGlobalMapping;

M1BackButtonTargetComboBox.IsEnabled =
M2BackButtonTargetComboBox.IsEnabled =
    snapshot.PersistenceWritable && !configuration.UseGlobalMapping;
~~~

This is internally consistent with `UseGlobalMapping`, but the visual toggle therefore means the opposite of "enable this feature".

### 3.3 Existing persistence semantics are already correct

Current toggle mutation eventually calls:

~~~csharp
SetXboxGameProfileBackButtonMappingAsync(key, mapping)
~~~

where:

~~~text
mapping == null
→ global fallback

mapping != null
→ per-game override
~~~

Keep this exactly.

When `UseGlobalMapping == true`, the XBOX snapshot already carries the current global fallback inside:

~~~text
configuration.Mapping
~~~

so enabling the per-game override can seed from exactly what is currently displayed.

Do not substitute `BackButtonMappingSettings.Default`.

### 3.4 Overlay has the correct unified active-target authority already

Current host already owns:

~~~csharp
private ActiveProfileTarget CaptureActiveProfileTarget()
{
    var appId = _runtimeHost?.ActualRunningAppId ?? 0;
    if (appId != 0) return ActiveProfileTarget.ForSteam(appId);

    var activeXboxGame = _xboxGameSessionRuntime?.ActiveGame;
    return activeXboxGame is null
        ? ActiveProfileTarget.None
        : ActiveProfileTarget.ForXbox(activeXboxGame.Key);
}
~~~

and:

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

Overlay Profile capture already uses this shared Steam/XBOX target.

Do not add another target detector.

### 3.5 The bug is only in first-show preference

Current `CoordinateOverlayToggleAsync` still contains:

~~~csharp
var activeAppId = _runtimeHost?.ActualRunningAppId ?? 0;
var preferActiveProfile = activeAppId != 0;

if (!await _overlayController.ShowAsync(preferActiveProfile).ConfigureAwait(false))
{
    ...
}
~~~

That Steam-only condition is the reason an active XBOX game does not select Profile first.

---

## 4. Main App UX correction

### 4.1 Rename the section

Change:

~~~text
Controller
~~~

to:

~~~text
M1 / M2 Button Mapping
~~~

The section is not a general controller configuration area. It contains only the per-game rear-button override.

Recommended XAML:

~~~xml
Header="M1 / M2 Button Mapping"
Description="Enable a per-game M1 / M2 override for Xbox 360 output. Off uses the global mapping."
~~~

Keep the existing icon unless there is already a more appropriate established button/remap icon in this UI. Do not start an icon redesign in this PR.

### 4.2 Remove the visible "Use global..." header

The right-side text:

~~~text
Use global M1 / M2 mapping
~~~

must no longer be shown.

The section header + description define the feature, and the right-side switch becomes the normal enable/disable switch.

Recommended shape:

~~~xml
<ToggleSwitch
    x:Name="PerGameBackButtonMappingEnabledToggle"
    IsOn="False"
    OffContent=""
    OnContent=""
    Toggled="PerGameBackButtonMappingEnabledToggle_Toggled"/>
~~~

Renaming the control is recommended because leaving a control named `UseGlobalBackButtonMappingToggle` while inverting its visible meaning makes future maintenance unnecessarily error-prone.

Do not create a new view model or wrapper solely for this inversion.

---

## 5. Main App semantic inversion

The persisted/runtime field remains:

~~~text
configuration.UseGlobalMapping
~~~

The UI toggle becomes:

~~~text
PerGameOverrideEnabled = !configuration.UseGlobalMapping
~~~

### Render

Conceptually:

~~~csharp
var perGameEnabled = !configuration.UseGlobalMapping;

PerGameBackButtonMappingEnabledToggle.IsOn = perGameEnabled;

BackButtonMappingUiOptions.SelectTarget(
    M1BackButtonTargetComboBox,
    configuration.Mapping.M1);

BackButtonMappingUiOptions.SelectTarget(
    M2BackButtonTargetComboBox,
    configuration.Mapping.M2);

PerGameBackButtonMappingEnabledToggle.IsEnabled =
    snapshot.PersistenceWritable;

M1BackButtonTargetComboBox.IsEnabled =
M2BackButtonTargetComboBox.IsEnabled =
    snapshot.PersistenceWritable && perGameEnabled;
~~~

### Toggle ON

When user turns the new toggle ON:

~~~text
current snapshot is using global mapping
→ M1/M2 controls already display current global fallback
→ read those displayed values
→ validate
→ construct BackButtonMappingSettings
→ SetXboxGameProfileBackButtonMappingAsync(key, mapping)
~~~

This converts the currently visible fallback into an explicit per-game override.

### Toggle OFF

When user turns the new toggle OFF:

~~~text
→ SetXboxGameProfileBackButtonMappingAsync(key, null)
→ runtime/persistence returns to global fallback
→ M1/M2 remain visible
→ M1/M2 become non-editable
~~~

### Selection changes

M1/M2 selection changes are admitted only while:

~~~text
per-game toggle ON
+ persistence writable
+ valid selected game/snapshot
~~~

Equivalent conceptual guard:

~~~csharp
if (!_active
    || _suppressControllerEvents
    || !PerGameBackButtonMappingEnabledToggle.IsOn
    || _frontend is null
    || _selectedGame is null
    || _snapshot is not { PersistenceWritable: true })
{
    return;
}
~~~

Continue saving one complete `BackButtonMappingSettings` record through the existing ordered save chain.

Do not add a second debounce, queue, version tracker, or mapping state object.

---

## 6. Preserve preconfiguration while Profile is disabled

Current product behavior intentionally allows controller-profile persistence independent of the Profile Enabled toggle.

Keep:

~~~text
Profile disabled
+ persistence writable
→ user can still enable/configure the per-game M1/M2 override
→ settings persist
→ mapping only becomes effective when existing effective-profile policy allows it
~~~

Do not make the new M1/M2 switch depend on:

~~~text
snapshot.Enabled
~~~

The Main App is also the offline/pre-launch editor.

---

## 7. Overlay Profile-first correction

In:

~~~text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
~~~

replace the Steam-only preference calculation.

Preferred implementation:

~~~csharp
var preferActiveProfile =
    CaptureActiveQuickSettingsProfileTarget() is not null;

if (!await _overlayController.ShowAsync(preferActiveProfile).ConfigureAwait(false))
{
    ...
}
~~~

Equivalent use of `CaptureActiveProfileTarget().Kind != ActiveProfileTargetKind.None` is acceptable, but reuse an existing active-target authority.

Do **not** query:

- process/window heuristics;
- XBOX catalog;
- package state directly;
- a second Xbox session source.

Do not add polling.

The flow remains:

~~~text
Runtime active-target authority
→ choose Show vs ShowActiveProfile
→ existing OverlayCommand
→ existing ResetUiForShow(preferActiveProfile)
→ existing Profile tab
~~~

No new Overlay command is needed.

---

## 8. Required first-show behavior

After the fix:

### Steam active

~~~text
CaptureActiveQuickSettingsProfileTarget()
→ Steam target
→ preferActiveProfile = true
→ OverlayCommand.ShowActiveProfile
→ Profile selected first
~~~

### XBOX active

~~~text
CaptureActiveQuickSettingsProfileTarget()
→ XBOX target
→ preferActiveProfile = true
→ OverlayCommand.ShowActiveProfile
→ Profile selected first
~~~

### No game

~~~text
CaptureActiveQuickSettingsProfileTarget()
→ null
→ preferActiveProfile = false
→ OverlayCommand.Show
→ persisted/configured first tab selected
~~~

Do not change persisted tab order when a game is active.

Active-game Profile-first is a per-show selection policy only.

---

## 9. Do not change the Overlay XBOX M1/M2 wording in this PR

The user's reported confusing switch is the **Main App XBOX game-profile editor** shown in `XboxPage.xaml`.

The active XBOX Overlay Profile currently exposes the explicit row:

~~~text
Use global M1 / M2 mapping
M1
M2
~~~

through generic Quick Settings.

That implementation was intentionally defined by PR12 and is not required to fix the screenshot issue.

Therefore this PR should not opportunistically redesign the Overlay Profile Controller section.

If later desired, Overlay semantics can be normalized in a separate focused UI change.

---

## 10. Tests — Main App XBOX M1/M2

Primary existing coverage:

~~~text
tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs
~~~

Update the existing XBOX controller-editor test rather than adding a parallel duplicate test family.

Required assertions:

1. XAML contains:

~~~text
Header="M1 / M2 Button Mapping"
~~~

2. XAML no longer presents:

~~~text
Header="Use global M1 / M2 mapping"
~~~

3. The new toggle exists and has no misleading global-mapping label.

4. Render explicitly inverts the persisted flag:

~~~text
toggle IsOn = !configuration.UseGlobalMapping
~~~

5. M1/M2 writability follows:

~~~text
snapshot.PersistenceWritable
&& perGameEnabled
~~~

6. Toggle OFF submits:

~~~text
mapping = null
~~~

7. Toggle ON reads the currently displayed M1/M2 pair and submits one complete:

~~~text
BackButtonMappingSettings
~~~

8. M1/M2 selection changes are ignored while the per-game toggle is OFF.

9. Existing ordered save-chain behavior remains:

~~~text
_backButtonSaveChain
_backButtonEditVersion
RunAfterPreviousAsync
SetXboxGameProfileBackButtonMappingAsync
~~~

Do not remove the rapid whole-mapping ordering test.

---

## 11. Tests — Overlay active XBOX Profile-first

Existing source-level test that currently encodes the bug:

~~~text
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayShowFailureContractTests.cs
~~~

It currently searches for:

~~~csharp
var activeAppId = _runtimeHost?.ActualRunningAppId ?? 0;
var preferActiveProfile = activeAppId != 0;
~~~

Update this test to assert the unified active-target source is used before:

~~~csharp
_overlayController.ShowAsync(preferActiveProfile)
~~~

Required assertions should establish:

~~~text
preferActiveProfile is derived from existing Steam/XBOX active-target authority
→ calculation occurs before ShowAsync
→ Show acknowledgement still occurs before presentation pause/capture
~~~

Keep the existing critical ordering invariant:

~~~text
Show acknowledgement
→ PauseForOverlayAsync
→ controller input router start
→ _overlayCaptureActive = true
~~~

The failure path must still return before any controller capture or presentation pause.

Also add/update focused coverage in the existing Overlay Quick Settings host contract tests to prove the shared target converter recognizes both:

~~~text
Steam target
XBOX target
~~~

and returns no target only for `ActiveProfileTarget.None`.

Do not introduce an integration-test harness solely for this tiny fix.

---

## 12. Expected production files

Expected production changes should be limited to approximately:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
~~~

Expected test changes:

~~~text
tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayShowFailureContractTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayQuickSettingsContractTests.cs
~~~

If production changes spread into profile storage, Xbox session detection, Quick Settings transport, controller publisher, HidHide, or VIIPER, stop and re-evaluate.

---

## 13. No protocol changes

Do not change:

~~~text
FrontendTransportProtocol.CurrentVersion
OverlayTransportProtocol.CurrentVersion
~~~

No DTO or wire enum changes are needed.

Both fixes are local presentation/host-policy corrections over existing contracts.

---

## 14. No persistence migration

Do not migrate existing profile JSON.

Existing persisted meaning stays:

~~~text
Controller.BackButtonMapping == null
→ global mapping

Controller.BackButtonMapping != null
→ explicit per-game mapping
~~~

The UI simply presents this as:

~~~text
Per-game M1/M2 toggle OFF
→ null

Per-game M1/M2 toggle ON
→ non-null mapping
~~~

Existing users therefore retain exact behavior after update.

---

## 15. Lifecycle / Full1902 safety

This PR must not alter:

- physical PID1902 acquisition/reacquisition;
- PID1901 restoration;
- HidHide baseline;
- VIIPER server/bus/device lifecycle;
- Xbox360 ↔ SteamDeck presentation switching;
- Overlay neutral/pause/capture ordering;
- sleep/hibernate/resume behavior;
- restart/crash/shutdown recovery;
- Center M authority.

The Overlay fix changes only which already-existing Overlay tab is selected on show.

The Main App fix changes only how the existing optional XBOX profile mapping is presented and translated to the already-existing nullable persistence contract.

No new race-defense mechanism is justified.

---

## 16. Overengineering guardrails

Do not add:

- `M1M2MappingViewModel` solely for this toggle;
- a new profile option type;
- a new persistence flag such as `PerGameMappingEnabled`;
- a second active-game observer;
- a second XBOX session cache;
- an Overlay-first-tab manager;
- an Overlay selection epoch;
- locks/barriers for tab selection;
- another M1/M2 save queue;
- another controller mapping runtime;
- another controller-output owner.

Required model is only:

~~~text
UI toggle = inverse of existing UseGlobalMapping
+
Overlay first-show preference = existing unified active target != none
~~~

---

## 17. Manual validation

After automated tests, perform a short visible validation.

### Main App XBOX page

1. Open XBOX page.
2. Select an installed game.
3. Expand **M1 / M2 Button Mapping**.
4. With toggle OFF:
   - M1/M2 visible;
   - M1/M2 non-editable;
   - global fallback is effective.
5. Turn toggle ON:
   - M1/M2 become editable;
   - current displayed global values seed the per-game override.
6. Change M1 and M2.
7. Navigate away/back or restart frontend.
8. Confirm explicit values persist.
9. Turn toggle OFF.
10. Confirm per-game override is removed and global fallback is effective again.

### Overlay first tab

1. No game active:
   - Overlay opens on configured first tab.
2. Steam game active:
   - Overlay opens on Profile.
3. XBOX game active:
   - Overlay opens on Profile.
4. Close/reopen while XBOX remains active:
   - Profile remains first on every show.
5. Exit XBOX game:
   - next Overlay show returns to configured first-tab policy.

Do not modify persisted tab order during any of these checks.

---

## 18. Required automated validation

Run:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

---

## 19. Completion condition

This work is complete when all of the following are true:

~~~text
Main App XBOX profile:

"M1 / M2 Button Mapping"                    [OFF]
→ global fallback
→ M1/M2 non-editable

"M1 / M2 Button Mapping"                    [ON]
→ explicit per-game override
→ M1/M2 editable
~~~

and:

~~~text
Overlay show:

Steam active → Profile first
XBOX active  → Profile first
No game      → configured first tab
~~~

while:

~~~text
nullable BackButtonMapping persistence contract unchanged
SetXboxGameProfileBackButtonMappingAsync remains the mutation authority
existing ordered M1/M2 save chain remains
CaptureActiveProfileTarget remains the active-game authority
Overlay Show/Pause/Capture ordering remains unchanged
CanonicalXbox360InputPublisher remains the only Xbox360 publisher
PID1902 / HidHide / VIIPER ownership remains untouched
no frontend/Overlay protocol bump
~~~
