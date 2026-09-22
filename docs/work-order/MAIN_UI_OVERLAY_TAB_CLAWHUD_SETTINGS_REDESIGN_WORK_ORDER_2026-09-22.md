# Work Order — Main UI Overlay Tab and ClawHUD Settings Redesign

**Date:** 2026-09-22  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** main  
**Reviewed main:** 9acf9e7378a1562de471dac4375e3a4a9de62007  
**Recommended branch:** feature/main-ui-overlay-tab-clawhud  
**Expected PR count:** 1 focused UI PR

---

## 0. Purpose

Move the ClawHUD controls out of the generic Main UI **Settings** page and give Overlay/HUD functionality its own first-class **Overlay** navigation page.

The current Main UI puts all ClawHUD controls inside one `SettingsExpander`:

```
Settings
  -> ClawHUD SettingsExpander
       -> status TextBlock
       -> Enable HUD ToggleSwitch
       -> Retry Button
       -> Display Mode ComboBox
       -> HUD Size ComboBox
       -> Font ComboBox
       -> Alignment ComboBox
       -> Background ComboBox
       -> raw opacity Slider
       -> Intel VRR ToggleSwitch
       -> raw VRR result TextBlock
```

This is functionally wired, but it does not match the current WinUI 3 information architecture or the UI language used by Device / Controller / Profile.

Required end state:

```
Device
Controller
Profile
Overlay
How to Use

Settings   // NavigationView footer
```

The new **Overlay** page becomes the Main UI home for ClawHUD / on-screen overlay settings.

This PR is a frontend layout/ownership refactor only.

Do **not** redesign ClawHUD Runtime ownership, IPC, lifecycle, Full1902 controller authority, or the in-game Quick Settings Overlay.

---

# 1. Mandatory source review before coding

Read current main before changing code.

At minimum:

```
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md

src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml

src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs
```

Also inspect the current UI tests:

```
tests/SteamInputAddonforClaw.UiTests/MainNavigationStateTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Do not implement from this document against stale source if main has moved materially.

---

# 2. Relationship to CH-A3

CH-A3 remains authoritative for:

- ClawHUD top-level desired-state ownership;
- Managed Runtime ownership;
- FrontendClawHudSnapshot;
- nested mutation contracts;
- opacity Preview / Commit semantics;
- Standalone conflict handling;
- failure behavior;
- StateInvalidated propagation;
- Full1902 isolation.

This work order **supersedes only the Main UI placement/layout direction** from CH-A3.

Old Main UI placement:

```
Settings page
  -> one ClawHUD group/expander
```

New Main UI placement:

```
Overlay page
  -> native WinUI 3 SettingsCard-based overlay controls
```

The in-game Addon Overlay `Setting` tab from CH-A3 is NOT renamed, removed, or repurposed here.

---

# 3. Product information architecture

Main NavigationView order must become:

```
Device
Controller
Profile
Overlay
How to Use
```

The built-in NavigationView Settings item remains the footer destination.

Add:

```
MainNavigationPage.Overlay
Tag="Overlay"
OverlayPage
```

Recommended icon: use a stock WinUI SymbolIcon / FontIcon that visually represents a screen/display/overlay.

Do not introduce a custom icon asset for this PR.

---

# 4. New page files

Create:

```
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
```

Do not add:

```
ClawHudViewModel
OverlaySettingsManager
HudUiCoordinator
OverlaySettingsService
generic settings schema
new dependency-injection layer
```

The current page-level code-behind model is already used throughout this UI and is sufficient.

---

# 5. Visual language

The new page must use the same WinUI 3 / CommunityToolkit layout language as Device / Controller / Profile.

Use:

```
ScrollViewer Padding="32"
StackPanel Spacing="16"
TextBlock FontSize="24" FontWeight="SemiBold"
CommunityToolkit.WinUI.Controls.SettingsCard
ComboBox
ToggleSwitch
Slider
InfoBar
```

Do not bring over the legacy standalone ClawHUD.Settings WPF visual language.

Do not create:

- segmented custom button groups;
- custom toggle templates;
- custom card primitives;
- fixed 600x600-style compact desktop layout;
- raw controls stacked directly inside one SettingsExpander.

The Main UI already has a coherent WinUI 3 card language. Reuse it.

---

# 6. Page header

Use:

```
Overlay
Configure the performance overlay and display behavior.
```

Recommended structure:

```xml
<StackPanel Spacing="4">
    <TextBlock FontSize="24"
               FontWeight="SemiBold"
               Text="Overlay" />
    <TextBlock Opacity="0.7"
               Text="Configure the performance overlay and display behavior."
               TextWrapping="Wrap" />
</StackPanel>
```

---

# 7. Section structure

Use simple section headings plus SettingsCard rows.

Required sections:

```
ClawHUD

Display

Appearance

Display compatibility
```

Do not put every section inside SettingsExpander.

These are normal always-visible settings rows.

---

# 8. ClawHUD section

Create one top-level SettingsCard:

```
Header: Enable HUD
Description: dynamic current state / user-facing status
Right content: ToggleSwitch
```

Conceptual layout:

```
[icon] Enable HUD                                  [ ON ]
       Ready
```

The ToggleSwitch must bind/render from:

```
FrontendClawHudSnapshot.DesiredEnabled
```

Never bind the switch to:

```
RuntimeState == Ready
```

Required semantics remain:

```
DesiredEnabled=true + Runtime unavailable
-> switch stays ON
-> status explains unavailable state
```

Keep the CH-A3 top-level mutation path:

```
SetClawHudEnabledAsync(bool)
```

Do not expose a second internal ClawHUD HudEnabled switch.

---

# 9. Retry / error presentation

Remove the current permanent raw:

```
ClawHudStatusText
ClawHudRetryButton
```

pair from the normal layout.

Use a WinUI `InfoBar` for actionable failure states.

Examples:

```
StandaloneConflict
-> Warning InfoBar
-> "ClawHUD Standalone is already running. Close it and retry."
-> ActionButton = Retry

Unavailable
-> Warning/Error InfoBar
-> stable user-facing StatusMessage
-> ActionButton = Retry when retry is meaningful
```

Normal states such as:

```
Off
Starting…
Ready
```

should be reflected through the Enable HUD SettingsCard description/status, without a permanently visible InfoBar.

Retry must continue to call:

```
SetClawHudEnabledAsync(true)
```

Do not create a new retry service or process-scanning path.

---

# 10. Display section

Use one SettingsCard per option.

## 10.1 Display mode

```
Header: Display mode
Description: Choose when the HUD is visible.
Control: ComboBox
Values:
  In game only
  Always
```

Keep the existing enum/mutation:

```
FrontendClawHudDisplayMode
FrontendClawHudMutationKind.DisplayMode
```

Prefer showing `In game only` first in the UI because it is the normal gameplay-oriented setting, but enum mapping must remain explicit rather than relying on index coincidence.

## 10.2 HUD size

```
Header: HUD size
Description: Adjust the overall HUD scale.
Control: ComboBox
Values:
  -2
  -1
  Default
  +1
  +2
```

Do not replace this discrete five-step value with a continuous Slider.

Keep:

```
FrontendClawHudMutationKind.HudSizeOffset
range -2..+2
```

## 10.3 Font

```
Header: Font
Description: Font used by the performance HUD.
Control: ComboBox
Values:
  Unispace
  Segoe UI Variable
```

Keep the current producer contract unchanged.

## 10.4 Alignment

```
Header: Alignment
Description: Horizontal alignment of HUD content.
Control: ComboBox
Values:
  Left
  Center
  Right
```

Do not create Left / Center / Right segmented custom buttons.

---

# 11. Appearance section

## 11.1 Background width

```
Header: Background width
Description: Choose how far the HUD background extends.
Control: ComboBox
Values:
  Full width
  Content width
```

Keep:

```
FrontendClawHudBackgroundMode
FrontendClawHudMutationKind.BackgroundMode
```

## 11.2 HUD opacity

Use a SettingsCard containing a native Slider and visible current percent value.

Required range:

```
50..100
StepFrequency = 5
```

Recommended row concept:

```
HUD opacity                                      90%
[---------------------------●------]
```

Keep existing protocol semantics exactly:

```
Slider interaction
  -> PreviewOpacity

interaction settles / pointer released
  -> CommitOpacity
```

Do not turn every ValueChanged event into a committed global mutation.

Preserve the existing serialized preview tail / commit-after-preview behavior unless current source has since replaced it with an equivalent authoritative mechanism.

Keyboard interaction must still result in a committed value; do not make commit depend exclusively on mouse pointer release.

---

# 12. Display compatibility section

Create one SettingsCard:

```
Header: Intel VRR Range Fix
Description: Restore the supported VRR range on affected Intel panels.
Control: ToggleSwitch
```

Keep:

```
FrontendClawHudMutationKind.IntelVrrRangeFixEnabled
```

Do not expose the raw producer result as a permanent multi-line diagnostic block such as:

```
Intel VRR: Applied
Panel: ...
Range: ...
Message: ...
```

Normal successful state should be compact.

If authoritative `IntelVrrLastResult` contains meaningful result data, show a concise summary in the card description or a secondary text element, for example:

```
Applied · 48–120 Hz → 40–120 Hz
```

When the result represents a warning/failure that matters to the user, show it through a WinUI InfoBar.

Do not invent new VRR policy or retry machinery in this PR.

---

# 13. Move existing ClawHUD page logic, do not duplicate it

Move the existing ClawHUD Main UI state/mutation logic out of:

```
SettingsPage.xaml.cs
```

into:

```
OverlayPage.xaml.cs
```

This includes the existing equivalents of:

```
_clawHudSnapshot
_clawHudOperationInProgress
_applyingClawHudState
_opacityPreviewTail
_opacityPreviewGate
_opacityCommitQueued

RequestClawHudRefresh
RefreshClawHudAsync
RenderClawHud
RetryClawHudAsync

ClawHudEnabledToggleSwitch_Toggled
DisplayMode mutation
HudSizeOffset mutation
Font mutation
Alignment mutation
BackgroundMode mutation
Intel VRR mutation

opacity Preview
opacity Commit
SubmitClawHudMutationAsync
```

There must be one Main UI owner for this logic after the refactor.

Do not leave a second hidden ClawHUD implementation in SettingsPage.

---

# 14. OverlayPage initialization

Follow the existing Main UI page initialization style.

OverlayPage must receive the existing:

```
IAddonFrontendControl
```

and perform authoritative capture through:

```
CaptureClawHudAsync()
```

No direct ClawHUD named-pipe access from the UI project.

No ClawHUD settings.ini access.

No duplicated persistence.

---

# 15. Navigation integration

Modify:

```
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
```

Add:

```
NavigationViewItem Content="Overlay" Tag="Overlay"
views:OverlayPage x:Name="OverlayContent"
MainNavigationPage.Overlay
"Overlay" => MainNavigationPage.Overlay
```

Update ShowPage visibility so exactly the selected top-level page is visible.

Current hierarchy behavior for:

- Settings -> Developer;
- Controller child pages;
- developer diagnostics;

must remain unchanged.

Overlay is a normal top-level page and needs no special back destination.

---

# 16. Main UI refresh ownership

Any current MainWindow path that refreshes ClawHUD through:

```
SettingsContent.RequestClawHudRefresh()
```

must move to:

```
OverlayContent.RequestClawHudRefresh()
```

State invalidation semantics remain unchanged.

Expected convergence:

```
ClawHUD child exits
-> existing StateInvalidated
-> Main UI refresh
-> Overlay page snapshot updates
-> DesiredEnabled remains true
-> RuntimeState becomes Unavailable
-> nested controls become disabled
```

Do not add polling.

---

# 17. Page activation

When navigating to Overlay, refreshing authoritative ClawHUD state is acceptable and preferred.

Recommended:

```
OverlayContent.Activate()
-> CaptureClawHudAsync()
-> Render authoritative snapshot
```

Leaving Overlay:

```
OverlayContent.Deactivate()
-> no process action
-> no ClawHUD stop
-> no setting reset
```

Do not connect page visibility to ClawHUD Runtime lifetime.

If a separate Activate/Deactivate method adds no value beyond one refresh call, keep it simple and reuse the existing navigation hook style rather than introducing a lifecycle framework.

---

# 18. Nested-control enable state

Nested controls are enabled only when:

```
RuntimeState == Ready
&& Settings != null
&& no current conflicting UI mutation
```

This applies to:

- Display mode;
- HUD size;
- Font;
- Alignment;
- Background width;
- HUD opacity;
- Intel VRR Range Fix.

The top-level Enable HUD toggle remains independently usable according to existing CH-A3 mutation rules.

When settings are unavailable:

- do not show stale previous values as authoritative;
- disable nested controls;
- preserve the user's top-level DesiredEnabled intent.

---

# 19. Settings page after migration

Delete the entire ClawHUD expander and all ClawHUD-specific state/mutation code from Main UI Settings.

Settings should remain focused on application/system configuration such as the current:

```
Application updates
Steam Big Picture Full Screen Experience
Show only current power source
Enter BIOS
Required Components
Developer Menu
```

Do not redesign those unrelated Settings items in this PR.

---

# 20. In-game Addon Overlay is out of scope

Do not modify the product meaning of:

```
SteamInputAddonforClaw.Overlay
Setting tab
Tab Order
Device/Profile Quick Settings
```

Do not add `Overlay` to:

```
QuickSettingsPageId
```

Do not add:

```
QuickSettingsPageId.Setting
QuickSettingsPageId.Overlay
QuickSettingsPageId.ClawHud
```

The new Main UI Overlay navigation page and the existing in-game Quick Settings Overlay are separate frontend surfaces over the same Runtime-owned authority.

---

# 21. Full1902 boundary

This UI refactor must not alter:

- Center M authority;
- PID1901 / PID1902;
- DirectInput;
- HidHide;
- VIIPER;
- controller presentation switching;
- Steam/BPM routing;
- suspend/resume controller reconcile;
- reboot-bound authority;
- controller fail-close behavior.

A ClawHUD UI failure remains feature-local.

No new controller recovery call is allowed from OverlayPage.

---

# 22. Expected production files

New:

```
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
```

Modified:

```
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
```

Likely tests:

```
tests/SteamInputAddonforClaw.UiTests/MainNavigationStateTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Modify additional tests only when required by actual existing coverage.

Do not touch frontend transport/protocol projects unless implementation evidence proves this UI-only move actually requires it.

It should not.

---

# 23. Required tests

## Navigation

Prove:

```
Tag="Overlay"
-> MainNavigationPage.Overlay
```

and existing:

```
Device
Controller
Profile
HowToUse
Settings
```

navigation still works.

## UI architecture/source contracts

Add focused assertions that:

```
MainWindow contains Overlay NavigationViewItem
MainWindow contains OverlayContent
OverlayPage uses SettingsCard
OverlayPage contains Enable HUD
OverlayPage contains Display mode
OverlayPage contains HUD size
OverlayPage contains Font
OverlayPage contains Alignment
OverlayPage contains Background width
OverlayPage contains HUD opacity
OverlayPage contains Intel VRR Range Fix
SettingsPage no longer contains ClawHudExpander
SettingsPage no longer owns ClawHUD mutation controls
```

Avoid brittle tests for exact whitespace or purely decorative icon glyphs.

## Authority regression

Retain/prove:

```
DesiredEnabled=true + Unavailable
-> top-level switch remains true

Settings == null
-> nested controls disabled

applying authoritative snapshot
-> no mutation emitted
```

If equivalent CH-A3 tests already cover the state logic, update them to reference OverlayPage rather than duplicate the same behavior.

---

# 24. Manual validation

On the current WinUI Main UI:

## Navigation

```
Device -> Controller -> Profile -> Overlay -> How to Use
```

Verify Overlay is a normal top-level page and Settings remains in the NavigationView footer.

## HUD Off

```
DesiredEnabled=false
-> Enable HUD Off
-> nested cards disabled
-> no ClawHUD process started solely by opening Overlay
```

## HUD On / Ready

```
Enable HUD On
-> Managed Runtime reaches Ready
-> nested controls enable
-> current authoritative values render
```

## Mutations

Verify:

```
Display mode
HUD size
Font
Alignment
Background width
HUD opacity
Intel VRR Range Fix
```

all reach the existing Runtime authority and survive page leave/re-enter.

## Opacity

Verify:

```
drag -> live preview
release/settle -> commit
leave Overlay
return Overlay
-> committed value remains
```

Also verify keyboard adjustment commits.

## Standalone conflict

```
DesiredEnabled=true
StandaloneConflict
-> Enable HUD remains On
-> nested controls disabled
-> warning InfoBar shown
-> Retry available
-> Standalone untouched
```

## Unexpected child exit

```
Ready -> ClawHUD child exits
-> Main UI remains alive
-> Enable HUD remains On
-> status becomes unavailable
-> nested controls disable
-> no controller lifecycle action
```

## Settings regression

Open Settings and verify unrelated existing controls still work.

---

# 25. Build / test validation

Run the normal current repository baseline.

At minimum:

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-build
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build -p:IsTestProject=true

dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-build
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build -p:IsTestProject=true
```

Also keep the repository's current publish verification green if the normal CI executes it.

---

# 26. Explicit non-goals

Do NOT include:

- ClawHUD Runtime redesign;
- ClawHUD protocol bump;
- frontend transport protocol bump;
- new IPC operation;
- ClawHUD settings.ini parsing/writing;
- duplicate ClawHUD persistence in SteamAddon;
- StartWithWindows ClawHUD control;
- process watchdog;
- automatic crash restart loop;
- polling;
- new state manager;
- new generic settings framework;
- custom WinUI controls;
- custom segmented controls;
- redesign of Device;
- redesign of Controller;
- redesign of Profile;
- redesign of unrelated Settings cards;
- redesign of the in-game Quick Settings Overlay;
- QuickSettingsPageId changes;
- QAM work;
- Full1902 controller changes.

---

# 27. Overengineering policy

This is a UI ownership/layout cleanup.

Prefer:

```
one OverlayPage
one existing frontend authority
one existing mutation path
one existing StateInvalidated flow
```

Do not add state/lock/epoch/manager abstractions for theoretical callback interleavings.

Preserve the already implemented mutation guards and opacity ordering because they protect real user interactions.

Do not add additional concurrency machinery unless a realistic production failure is demonstrated.

---

# 28. Acceptance checklist

## Navigation

- [ ] Main Navigation order is Device / Controller / Profile / Overlay / How to Use.
- [ ] Settings remains the NavigationView footer item.
- [ ] MainNavigationPage.Overlay exists.
- [ ] Existing child/back navigation remains unchanged.

## Overlay page

- [ ] New OverlayPage.xaml exists.
- [ ] Uses WinUI 3 / CommunityToolkit SettingsCard layout.
- [ ] Has page title + short description.
- [ ] Has ClawHUD / Display / Appearance / Display compatibility sections.
- [ ] No custom segmented control system is introduced.

## ClawHUD

- [ ] Enable HUD is a SettingsCard + native ToggleSwitch.
- [ ] Toggle renders DesiredEnabled, not Ready state.
- [ ] Failure/retry uses InfoBar rather than permanent raw status/button stacking.
- [ ] Display mode uses ComboBox.
- [ ] HUD size uses discrete ComboBox.
- [ ] Font uses ComboBox.
- [ ] Alignment uses ComboBox.
- [ ] Background width uses ComboBox.
- [ ] HUD opacity uses SettingsCard + native Slider + current percent.
- [ ] Opacity Preview/Commit behavior is preserved.
- [ ] Intel VRR Range Fix uses SettingsCard + native ToggleSwitch.
- [ ] VRR normal result presentation is compact.
- [ ] Warning/failure result can use InfoBar.

## Ownership

- [ ] SettingsPage no longer contains ClawHUD UI.
- [ ] SettingsPage no longer owns ClawHUD Main UI mutation state.
- [ ] OverlayPage is the single Main UI ClawHUD owner.
- [ ] Existing FrontendClawHudSnapshot remains authoritative.
- [ ] Existing frontend mutation contract remains unchanged.
- [ ] No direct ClawHUD IPC from UI.
- [ ] No duplicate persistence.

## Regression

- [ ] In-game Overlay Setting tab remains unchanged.
- [ ] QuickSettingsPageId remains unchanged.
- [ ] Full1902 controller lifecycle remains unchanged.
- [ ] Main UI navigation tests pass.
- [ ] UI architecture tests pass.
- [ ] full Debug tests pass.
- [ ] full Release tests pass.

---

# 29. Final implementation principle

The intended ownership after this PR is:

```
Main UI Navigation
  -> OverlayPage
       -> IAddonFrontendControl
            -> existing ClawHUD frontend contract
                 -> Addon ClawHUD process/runtime owner
                      -> ClawHUD Control IPC
                           -> authoritative ClawHUD settings
```

The purpose of this PR is not to add another ClawHUD layer.

It is to put the already-working ClawHUD authority behind the correct WinUI 3 product surface and make that surface visually consistent with the rest of Steam Addon for Claw.
