# Work Order — Controller/Device UI Polish and Active-Game Profile-First Overlay

> Date: 2026-10-04  
> Status: Ready for implementation  
> Scope: **One PR, exactly two reviewable implementation commits**  
> Repository baseline reviewed: `main` at `b7925f6129b24cfd5f8453f80261b408afe48972`  
> Product architecture: Standalone Full1902  
> CTW integration: out of scope

---

## 1. Goal

Deliver four small product/UX corrections in one PR while keeping the active-game Overlay startup change isolated in its own commit.

### Commit 1 — UI hierarchy / priority polish

1. Main WinUI3 `Joystick LED` uses the same `SettingsExpander` header pattern as TDP/CPU Boost/Power Mode:
   - LED feature name on the left;
   - Enabled toggle in the expander header content on the right;
   - normal expander chevron remains at the far right;
   - remove the redundant nested `Enabled` card.

2. Overlay `Joystick LED` uses one feature-header toggle row rather than:
   ~~~text
   Joystick LED
   Enabled [toggle]
   ~~~
   and hides its detail rows while LED is Off.

3. Overlay Joystick LED details and Vibration Strength motor rows use the existing `QamDetailIndent`.

4. Battery Charge Limit is the lowest-priority Device card/section in both:
   - Main WinUI3 Device page;
   - shared Device Quick Settings projection consumed by Overlay.

### Commit 2 — active Steam game opens Overlay on Profile

When the Runtime's existing active-game authority says:

~~~text
ActualRunningAppId != 0
~~~

a new Overlay Show starts on `Profile`.

When:

~~~text
ActualRunningAppId == 0
~~~

the existing behavior remains:

~~~text
startup tab = configured tab order[0]
~~~

This must **not** rewrite or transiently reorder the user's persisted tab order.

---

## 2. Mandatory commit structure

This is one PR but the implementation must be separated into exactly these logical commits so review can evaluate the active-game startup behavior independently.

### Commit 1

Suggested message:

~~~text
Polish controller controls and Device card priority
~~~

Contains only:

- Main UI Joystick LED header-toggle layout;
- Overlay Joystick LED hierarchy/collapse;
- Overlay LED/Vibration detail indentation;
- Battery Charge Limit moved to the bottom in Main UI and shared Device Quick Settings;
- focused tests for those changes.

Must not contain:

- Overlay protocol changes;
- active-game detection;
- Profile-first startup behavior;
- changes to `OverlayCommand`.

### Commit 2

Suggested message:

~~~text
Open active-game Overlay on Profile
~~~

Contains only:

- Runtime active-AppId Show decision;
- narrow Overlay Show command extension;
- current-Show startup selection override;
- stale-profile-safe loading behavior;
- protocol/version/transport tests required by this command change.

Do not squash these two implementation commits before opening the PR.

The final GitHub merge may still be Squash Merge under normal project policy.

---

## 3. Required architecture reading

Implementation must remain subordinate to the current Full1902 authority order:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

Relevant current frontend/Overlay work orders reviewed for this change:

~~~text
docs/work-order/OVERLAY_SHARED_PRODUCTION_CONTROLS_PARITY_WORK_ORDER_2026-10-04.md
docs/work-order/OVERLAY_300MS_DELAYED_COMMIT_AND_USER_DISMISS_FLUSH_WORK_ORDER_2026-10-04.md
docs/work-order/OVERLAY_PROFILE_CATALOG_AND_SHARED_PROFILE_PARITY_WORK_ORDER_2026-09-24.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md
docs/work-order/APP_UI_PR_A_NAVIGATION_AND_PAGE_OWNERSHIP_REORGANIZATION_WORK_ORDER.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
~~~

Later work orders override old text where behavior has already changed, including the current 300 ms Overlay delayed-commit policy.

---

# Part A — Commit 1: Controller / Device UI hierarchy polish

## 4. Main UI — Joystick LED must follow the existing WinUI3 SettingsExpander pattern

Current `ControllerPage.xaml` has:

~~~text
Joystick LED                    [chevron]
    Enabled                     [toggle]
    Brightness
    Color
~~~

That is inconsistent with the existing Main UI feature expanders such as TDP and CPU Boost.

The desired Main UI layout is:

~~~text
Joystick LED              [On/Off] [chevron]
    Brightness
    Color
~~~

The toggle controls whether the LED feature is active.

The chevron controls whether the user is viewing the detailed settings.

These are separate UI roles.

### 4.1 Required XAML shape

Keep the existing `SettingsExpander`:

~~~xml
<ctcontrols:SettingsExpander
    x:Name="ControllerLedExpander"
    Header="Joystick LED"
    ...>
~~~

Move the existing:

~~~xml
<ToggleSwitch
    x:Name="ControllerLedEnabledToggle"
    ... />
~~~

out of the nested `SettingsCard Header="Enabled"` and make it the direct content of the `SettingsExpander`, matching the existing TDP/CPU Boost/Power Mode convention.

Conceptually:

~~~xml
<ctcontrols:SettingsExpander
    x:Name="ControllerLedExpander"
    Header="Joystick LED"
    Description="Set one static color and global brightness for all controller LEDs."
    IsExpanded="False">

    <ctcontrols:SettingsExpander.HeaderIcon>
        ...
    </ctcontrols:SettingsExpander.HeaderIcon>

    <ToggleSwitch
        x:Name="ControllerLedEnabledToggle"
        OffContent=""
        OnContent=""
        Toggled="ControllerLedEnabled_Toggled" />

    <ctcontrols:SettingsExpander.Items>
        <ctcontrols:SettingsCard Header="Brightness">
            ...
        </ctcontrols:SettingsCard>
        <ctcontrols:SettingsCard Header="Color">
            ...
        </ctcontrols:SettingsCard>
    </ctcontrols:SettingsExpander.Items>
</ctcontrols:SettingsExpander>
~~~

Remove the nested `SettingsCard Header="Enabled"`.

Do not create a custom expander control.

### 4.2 Main UI Off behavior

Preserve the current production behavior:

~~~text
LED Off
-> persisted brightness/RGB remain stored
-> Brightness slider disabled
-> Color button disabled

LED On
-> saved brightness/RGB become editable again
~~~

Do not erase or reset brightness/RGB on Off.

Do not add another LED settings record.

Do not automatically mutate brightness/RGB while only moving the toggle.

It is acceptable for the Main UI expander to remain expandable while Off; the detailed controls remain disabled. This is normal desktop configuration behavior and avoids coupling feature enable state to expansion state.

---

## 5. Overlay — Joystick LED becomes one feature-header toggle row

Current Overlay Controller rendering creates:

~~~text
Joystick LED
Enabled
Brightness
Color
Red
Green
Blue
~~~

using a static heading plus an `OverlayToggleRow("Enabled", ...)`.

Change the visible hierarchy to:

~~~text
Joystick LED                         [toggle]
    Brightness
    Color  [swatch]
    Red
    Green
    Blue
~~~

When Off:

~~~text
Joystick LED                         [Off]
~~~

Only the feature-header toggle remains visible.

### 5.1 Reuse existing row primitives

Use the existing:

~~~text
OverlayToggleRow
OverlayValueRow
OverlayRowSelection
QamDetailIndent
~~~

Do not add:

~~~text
OverlayFeatureCard
OverlayFeatureHeaderManager
OverlayExpandableSection
new Controller view model
new shared UI abstraction
~~~

A small local `StackPanel` reference for the LED detail container is sufficient.

### 5.2 Required structural direction

Do not create a static `CreateControllerSection("Joystick LED")` heading plus a separate `Enabled` row.

Instead the selectable toggle row itself must carry the strong feature label:

~~~csharp
_controllerLedEnabledRow =
    new OverlayToggleRow("Joystick LED", RequestControllerLedEnabled);
~~~

The LED section/card then conceptually contains:

~~~text
LED toggle row
LED detail stack
~~~

The detail stack contains:

~~~text
Brightness
Color preview
Red
Green
Blue
~~~

and uses:

~~~csharp
Margin = OverlayQamResources.Get(
    "QamDetailIndent",
    new Thickness(16, 0, 0, 0));
~~~

Do not invent a new indent resource.

### 5.3 Overlay Off behavior

On authoritative LED Off:

~~~text
detailStack.Visibility = Collapsed
Brightness/RGB rows remain non-selectable
saved values remain untouched
color swatch state may remain internally rendered
~~~

On authoritative LED On:

~~~text
detailStack.Visibility = Visible
Brightness/RGB rows become selectable when writable
~~~

The detail visibility follows the **authoritative current `_controllerLed.Enabled` state**, not an optimistic independent bool.

During the existing whole-record LED mutation in flight:

- preserve the current visibility derived from the current renderer state;
- keep mutation admission disabled as today;
- do not create a second pending LED state machine.

### 5.4 Row selection safety

The current Controller page stores all rendered rows in `_pageRows`.

It is acceptable to keep the LED detail row objects in that list while the detail stack is collapsed **only if** their existing `ApplyState(false, ...)` makes `IsSelectable()` false.

After an On -> Off mutation settles:

~~~text
selected row was Brightness/R/G/B
-> selection is re-normalized through existing OverlayRowSelection
-> hidden row cannot remain an actionable selection
-> selection lands on a valid selectable Controller row
~~~

Do not add a second filtered selection list.

---

## 6. Overlay — Vibration Strength detail indent

Current Overlay Vibration Strength rows are visually flush-left with their section heading:

~~~text
Vibration Strength
Left Motor
Right Motor
~~~

Change only layout:

~~~text
Vibration Strength
    Left Motor
    Right Motor
~~~

Use exactly the same existing `QamDetailIndent` resource used by generic Device/Profile feature details.

Do not change:

- Left/Right semantics;
- 300 ms delayed commit;
- dismiss flush;
- whole-pair mutation;
- Runtime authority;
- ranges.

A local detail `StackPanel` is enough.

---

## 7. M1 / M2 remains unchanged

Do not indent M1/M2 merely for symmetry.

Current:

~~~text
M1 / M2
M1
M2
~~~

remains in this PR.

The user request specifically targets Joystick LED and Vibration Strength child hierarchy.

Do not broaden Commit 1 into a Controller page redesign.

---

## 8. Battery Charge Limit — lowest priority in shared Device Quick Settings

Current shared Device section order is:

~~~text
Battery Charge Limit
TDP
CPU Boost
Windows Power Mode
~~~

Change it to:

~~~text
TDP
CPU Boost
Windows Power Mode
Battery Charge Limit
~~~

The change belongs in the existing single shared projection authority:

~~~text
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
~~~

Required `BuildDevice(...)` order:

~~~csharp
IReadOnlyList<QuickSettingsSection> sections =
[
    BuildTdpSection(snapshot.Tdp),
    BuildCpuBoostSection(snapshot.CpuBoost),
    BuildPowerModeSection(snapshot.PowerMode),
    BuildBatteryChargeLimitSection(snapshot.BatteryChargeLimit),
];
~~~

Do not special-case Overlay ordering in the renderer.

Overlay already renders shared `page.Sections` order.

### 8.1 No semantic Battery changes

Do not change:

- 60–100 range;
- step 5;
- initialization semantics;
- Enabled write rules;
- persistence;
- 300 ms commit policy;
- power-source visibility behavior;
- Runtime battery authority.

Only section priority/order changes.

---

## 9. Battery Charge Limit — Main UI card moves to bottom

Current Main UI Device page places Battery Charge Limit before TDP.

Move the existing entire Battery block:

~~~text
BatteryChargeLimitInfoBar
BatteryChargeLimitCard
~~~

to after:

~~~text
PowerModeInfoBar
PowerModeExpander
~~~

Desired Main UI order:

~~~text
Device summary
MSI Center M
TDP Control
CPU Boost
Windows Power Mode
Battery charge limit
~~~

Do not rewrite the Battery controls.

Do not change event handlers.

Do not move Battery to another page.

---

## 10. Commit 1 tests

Update focused tests rather than broad snapshot rewrites.

### 10.1 Main UI LED XAML contract

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/ControllerLedUiContractTests.cs
~~~

to prove:

- exactly one Joystick LED `SettingsExpander`;
- `ControllerLedEnabledToggle` is the expander's direct content/header action, not an item card;
- no `SettingsCard Header="Enabled"` remains for LED;
- Brightness and Color remain expander items;
- code-behind still disables Brightness/Color while Off.

### 10.2 Overlay Controller renderer

Update/add assertions in:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
~~~

to prove:

- LED feature toggle label is `Joystick LED`, not `Enabled`;
- LED detail container uses `QamDetailIndent`;
- Vibration detail container uses `QamDetailIndent`;
- LED detail visibility derives from `_controllerLed.Enabled`;
- detail rows are non-selectable while Off through existing row state;
- M1/M2 structure remains unchanged;
- no new navigation/manager layer exists.

### 10.3 Shared Device order

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
~~~

Expected exact section order becomes:

~~~text
DeviceTdp
DeviceCpuBoost
DevicePowerMode
DeviceBatteryChargeLimit
~~~

Any tests that currently assume Battery is `page.Sections[0]` must locate it by:

~~~csharp
SectionId == QuickSettingsSectionId.DeviceBatteryChargeLimit
~~~

unless the test is specifically testing exact section order.

Do not retain brittle positional assumptions unrelated to the ordering requirement.

### 10.4 Main UI Device XAML order

Add/adjust a narrow source/XAML contract test if needed to prove:

~~~text
PowerModeExpander
appears before
BatteryChargeLimitCard
~~~

Do not add a visual-test framework.

---

# Part B — Commit 2: Active-game Profile-first Overlay

## 11. Product rule

The Runtime already owns the actual active Steam AppId:

~~~csharp
_runtimeHost?.ActualRunningAppId
~~~

The existing active Profile publication uses that same authority.

New Show policy:

~~~text
ActualRunningAppId != 0
-> open Overlay with Profile selected

ActualRunningAppId == 0
-> open Overlay with configured order[0] selected
~~~

This is a **Show-session startup selection rule**.

It is not a new persisted preference.

---

## 12. Do NOT rewrite the persisted tab order

The user-visible tab order currently owns three things:

~~~text
tab strip order
LB/RB traversal order
Setting > Tab Order editor
~~~

and is persisted through the existing Runtime settings authority.

Therefore do **not** implement active-game startup by changing:

~~~text
_settings.AddonQuickSettingsTabOrder
AddonQuickSettingsTabOrderSnapshot
OverlayTabState.Order
~~~

to a transient Profile-first sequence.

That would make the visible tab strip / Setting editor differ from the user's saved preference and would make one-position tab-order mutations ambiguous while a game is running.

Frozen rule:

> Active-game Profile-first changes only which tab is selected for this Show. The actual tab order remains exactly the user's authoritative persisted order.

Example:

~~~text
persisted order:
Device / Controller / Shortcut / Profile / Setting

game running:
Show -> Profile selected

tab strip order:
Device / Controller / Shortcut / Profile / Setting

LB/RB:
follows that same persisted order
~~~

This is the safe implementation of the requested "Profile is startup slot 1 while in game" behavior.

---

## 13. Keep active-game authority in Runtime

Overlay.exe must not discover Steam games itself.

Do not add:

~~~text
Steam process scanning in Overlay
game AppId polling in Overlay
Steam Web API query
window-title heuristics
new active-game setting
~~~

Runtime decides immediately before issuing Show:

~~~csharp
var activeAppId = _runtimeHost?.ActualRunningAppId ?? 0;
var preferActiveProfile = activeAppId != 0;
~~~

The Overlay receives only the narrow Show semantic.

It does not become the AppId authority.

---

## 14. Narrow transport design — add ShowActiveProfile

Do not create a generic navigation/startup-context framework for one behavior.

Append one narrow command:

~~~csharp
internal enum OverlayCommand
{
    Show,
    ShowActiveProfile,
    Hide,
    Shutdown,
}
~~~

Meaning:

~~~text
Show
-> normal Show
-> startup selected tab = configured order[0]

ShowActiveProfile
-> normal Show lifecycle
-> startup selected tab = Profile
~~~

This is intentionally narrower than adding:

~~~text
InitialTabProvider
OverlayStartupContextManager
dynamic startup policy service
~~~

### 14.1 Protocol version

This command changes wire semantics.

Bump:

~~~text
OverlayTransportProtocol.CurrentVersion
14 -> 15
~~~

Update the protocol comment:

~~~text
Version 15: adds ShowActiveProfile for Runtime-authoritative active-game Profile-first Overlay startup.
~~~

Do not bump `FrontendTransportProtocol.CurrentVersion`.

---

## 15. OverlayProcessController Show seam

Keep the existing controller as process/transport owner.

A narrow signature such as:

~~~csharp
internal Task<bool> ShowAsync(bool preferActiveProfile = false)
~~~

is sufficient.

When showing:

~~~text
preferActiveProfile == false
-> OverlayCommand.Show

preferActiveProfile == true
-> OverlayCommand.ShowActiveProfile
~~~

Hide and Shutdown are unchanged.

Do not add a Show mode class.

Do not add a startup policy object.

### 15.1 Visible acknowledgement

`NamedPipeOverlayClient` must treat both:

~~~text
Show
ShowActiveProfile
~~~

as commands that settle to:

~~~text
OverlayState.Visible
~~~

Hide still settles to Hidden.

Shutdown still exits.

---

## 16. AddonProcessHost active-game decision

In the existing coordinated Overlay Show path, immediately before the current:

~~~csharp
await _overlayController.ShowAsync(...)
~~~

derive the preference from the existing active-game fact.

Conceptually:

~~~csharp
var preferActiveProfile = (_runtimeHost?.ActualRunningAppId ?? 0) != 0;

if (!await _overlayController.ShowAsync(preferActiveProfile).ConfigureAwait(false))
{
    ...
}
~~~

Do not change the existing lifecycle order:

~~~text
Show acknowledgement
-> pause presentation neutral
-> create/start Overlay input router
-> set _overlayCaptureActive
-> publish Device/Profile/other state
~~~

Do not capture Profile synchronously before Show.

Do not delay Show waiting for Profile snapshot work.

The existing OQ4 capture/fail-close sequence remains unchanged.

---

## 17. Overlay current-Show startup selection

Current shell behavior:

~~~text
ResetUiForShow()
-> _tabState.ResetForShow()
-> selected tab = order[0]
~~~

Extend this narrowly.

Conceptual behavior:

~~~text
normal Show:
_tabState.ResetForShow()

ShowActiveProfile:
_tabState.ResetForShow()
_tabState.Select(AddonQuickSettingsTabId.Profile)
~~~

The important invariant:

~~~text
_tabState.Order is never changed by ShowActiveProfile
~~~

No persisted preference changes.

No live tab strip reorder.

No Setting editor reorder.

A small optional bool parameter on:

~~~text
ShowForPocAsync(...)
ResetUiForShow(...)
~~~

is sufficient.

Do not make `OverlayTabState` a general startup-policy object.

---

## 18. Do not expose stale previous-game Profile content

The Overlay process is warm and survives Hide.

Therefore a previous active game's Profile page can remain in local presentation memory.

A `ShowActiveProfile` must not reveal that stale page while the current game's fresh Profile snapshot is still being published.

Before visual reveal for `ShowActiveProfile`:

~~~text
clear selected catalog target
clear previous active Profile AppId presentation fact
put Profile presentation in ActiveDetail/loading mode
apply a non-writable Unavailable/Loading current-game Profile page
select Profile
show Overlay
~~~

Then the existing post-capture:

~~~text
RefreshQuickSettingsAsync()
-> CaptureOverlayProfileQuickSettingsPageAsync()
-> ActualRunningAppId
-> fresh Profile page
-> ApplyActiveProfilePage(...)
~~~

replaces the loading page with authoritative current-game content.

Use existing:

~~~text
ProfilePresentationMode
QuickSettingsPageSnapshot.Unavailable(...)
ApplyProfileDetailPage(...)
ApplyActiveProfilePage(...)
~~~

Do not add another Profile renderer or settings owner.

A small focused method in `OverlayWindow.Profile.cs`, for example:

~~~csharp
PrepareActiveProfileFirstShow()
~~~

is acceptable.

### 18.1 Loading page must not mutate

The temporary page must expose no writable old-game rows.

The user may press controls immediately after the Overlay appears; before the fresh current-game page arrives those inputs must not be able to mutate the previous game's AppId.

### 18.2 If the game exits during Show

A realistic lifecycle convergence is already available.

If Runtime chose `ShowActiveProfile` and the game exits before Profile capture:

~~~text
ShowActiveProfile
-> loading Profile
-> CaptureOverlayProfileQuickSettingsPageAsync sees AppId 0
-> sends Profile Unavailable
-> ApplyActiveProfilePage clears active AppId
-> Profile falls back to catalog
~~~

No new epoch/state machine is required.

### 18.3 If a game starts just after a normal Show decision

If Runtime saw AppId 0 immediately before Show and the game starts just afterward:

~~~text
normal Show may begin on configured order[0]
-> existing Runtime invalidation republishes active Profile
~~~

Do not add race machinery solely to force a tab jump for this narrow timing overlap.

The next Overlay Show will use Profile-first normally.

This follows the project's race/overengineering policy.

---

## 19. Profile catalog behavior remains intact

Outside active-game Profile-first Show:

~~~text
no active game
+ user selects Profile
-> existing Profile catalog behavior
~~~

Selected catalog Profile editing remains unchanged.

Back-to-catalog behavior and the PR #667 pending-edit flush remain unchanged.

Do not convert catalog browsing into active-game-only behavior.

---

## 20. Tab order editor remains authoritative and unchanged

The Setting page continues to display and mutate the persisted tab order.

Example:

~~~text
saved:
Controller / Device / Profile / Shortcut / Setting

active game Show:
Profile selected

Setting > Tab Order still shows:
Controller / Device / Profile / Shortcut / Setting
~~~

If the user moves a tab, the current one-position mutation path remains the only mutation authority.

Do not persist Profile-first merely because a game is running.

---

## 21. Commit 2 transport validation

Update `OverlayWire` validation so a command frame is valid only when it carries the existing allowed command payload and no unrelated payload.

`ShowActiveProfile` must not require a new AppId field.

The AppId remains Runtime-owned and arrives through the existing active Profile page publication.

Do not add:

~~~text
ActiveAppId to OverlayWireMessage
Profile AppId to ShowActiveProfile
new ShowContext record
~~~

There is no need.

---

## 22. Commit 2 tests

### 22.1 Protocol / command transport

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
~~~

to prove:

1. protocol is v15;
2. `ShowActiveProfile` reaches the Overlay command handler;
3. it acknowledges `OverlayState.Visible`;
4. ordinary `Show` still acknowledges Visible;
5. Hide/Shutdown behavior is unchanged;
6. malformed mixed command payloads remain rejected.

### 22.2 Runtime Show decision

Update/add focused tests around:

~~~text
AddonProcessHost.CoordinateOverlayToggleAsync
~~~

Prove:

~~~text
ActualRunningAppId = 0
-> ShowAsync(false)

ActualRunningAppId > 0
-> ShowAsync(true)
~~~

Do not infer "Steam game" from process names in tests.

Use the existing Runtime-host AppId fact.

Update source-contract tests that currently match the exact parameterless:

~~~text
_overlayController.ShowAsync()
~~~

call.

Preserve the critical ordering assertion:

~~~text
Show acknowledgement
before
PauseForOverlayAsync
~~~

### 22.3 Overlay shell startup selection

Add focused tests/source contracts proving:

~~~text
Show
-> order[0] selected

ShowActiveProfile
-> Profile selected

ShowActiveProfile
-> _tabState.Order unchanged
~~~

Also prove subsequent LB/RB still traverses the persisted order.

### 22.4 Stale Profile protection

Test the warm-process case:

~~~text
previous session active Profile = AppId A
Hide
new session ShowActiveProfile for game B
fresh B snapshot not delivered yet

expected:
-> B session does NOT expose writable A controls
-> loading/unavailable Profile state is shown
-> after B page arrives, ActiveDetail is B
~~~

Also test:

~~~text
ShowActiveProfile
-> active game disappears before page capture
-> Unavailable active Profile
-> catalog fallback
~~~

Do not add scheduler-interleaving stress tests.

---

## 23. Expected production file scope

### Commit 1 likely files

~~~text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs

tests/SteamInputAddonforClaw.UiTests/ControllerLedUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs          (only if used for Device XAML order)
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
~~~

Main UI LED code-behind may require no functional change because it already uses:

~~~text
ControllerLedEnabledToggle
ControllerLedBrightnessSlider.IsEnabled = settings.Enabled
ControllerLedColorButton.IsEnabled = settings.Enabled
~~~

Do not edit it merely for churn.

### Commit 2 likely files

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs

tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayShowFailureContractTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayTabStateTests.cs          (if startup selection helper is covered there)
tests/SteamInputAddonforClaw.UiTests/... focused Profile startup tests
~~~

Keep the actual diff smaller if existing seams allow it.

---

## 24. Explicit non-goals

This PR must not:

- alter Full1902 controller ownership;
- alter HidHide / VIIPER lifecycle;
- alter PID1901/PID1902 transitions;
- change LED persistence/hardware write format;
- reset saved LED RGB/brightness on Off;
- change vibration authority or 300 ms policy;
- change Battery Charge Limit behavior beyond placement;
- add a Battery page;
- add a new UI architecture layer;
- add a generic feature-card abstraction;
- add a new tab-order authority;
- rewrite persisted tab order while a game is running;
- add a "default tab" setting;
- restore last-selected tab across Shows;
- add active-game polling to Overlay;
- delay the OQ4 Show/capture path waiting for Profile capture;
- add epochs/barriers/state machines for narrow Show/game-transition timing.

---

## 25. Acceptance criteria — Commit 1

Commit 1 is complete only when:

1. Main UI Joystick LED has the toggle in the SettingsExpander header action position.
2. Main UI has no nested LED `Enabled` SettingsCard.
3. Main UI LED Brightness/Color preserve existing disable-on-Off behavior.
4. Overlay LED feature row is labeled `Joystick LED` with the toggle on the same row.
5. Overlay LED detail rows are hidden when Off.
6. Overlay LED detail rows preserve saved values while hidden.
7. Overlay LED detail rows use `QamDetailIndent`.
8. Overlay Vibration Left/Right rows use `QamDetailIndent`.
9. M1/M2 layout is unchanged.
10. Shared Device order is TDP -> CPU Boost -> Windows Power Mode -> Battery Charge Limit.
11. Overlay follows that shared order without renderer-specific reordering.
12. Main UI Battery Charge Limit appears after Windows Power Mode.
13. No Runtime authority or transport changes exist in Commit 1.
14. Focused tests pass.

---

## 26. Acceptance criteria — Commit 2

Commit 2 is complete only when:

1. `ActualRunningAppId != 0` causes `ShowActiveProfile`.
2. `ActualRunningAppId == 0` keeps ordinary `Show`.
3. Active-game Show selects Profile before visual reveal.
4. Ordinary Show still selects configured order[0].
5. The actual persisted tab order is never changed by active-game Show.
6. Tab strip ordering remains the user's configured order.
7. LB/RB traversal remains the user's configured order.
8. Setting > Tab Order still shows/mutates the persisted order.
9. A warm Overlay never exposes writable stale previous-game Profile controls during Profile-first startup.
10. Existing active Profile publication remains based on Runtime `ActualRunningAppId`.
11. If the game disappears before capture, Profile converges to catalog without new retry/state machinery.
12. Overlay protocol is v15; frontend protocol is unchanged.
13. `Show` and `ShowActiveProfile` both settle to Visible.
14. Existing Show -> neutral pause -> capture commit lifecycle order is unchanged.
15. Tests pass.

---

## 27. Review guardrail

Review this PR as two independent ideas even though it is one PR.

### Commit 1 review question

> Did we only improve hierarchy/priority using the existing UI primitives and shared Device projection?

### Commit 2 review question

> Does Runtime select Profile for an already-running game without corrupting tab-order preference or exposing stale previous-game controls?

Do not require additional synchronization for theoretical instruction-level overlaps.

Only treat a timing issue as blocking if it has a realistic supported lifecycle path with material user impact.

The intended final behavior is:

~~~text
Main UI:
Joystick LED               [toggle] [chevron]
    Brightness
    Color

Overlay Controller:
Joystick LED               [toggle]
    Brightness
    Color
    Red
    Green
    Blue

Vibration Strength
    Left Motor
    Right Motor

Device priority:
TDP
CPU Boost
Windows Power Mode
Battery Charge Limit

Overlay startup:
active AppId != 0 -> Profile selected
active AppId == 0 -> configured order[0] selected

Persisted tab order:
never changed by active-game startup
~~~
