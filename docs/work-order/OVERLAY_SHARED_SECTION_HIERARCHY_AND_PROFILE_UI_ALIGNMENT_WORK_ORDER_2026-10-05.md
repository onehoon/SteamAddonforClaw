# Work Order — Unify Overlay Section Hierarchy, Restore Intel FPS Profile UI, and Align Profile Ordering

**Date:** 2026-10-05  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `7ecac4e23474c380daad1b6d196511deaa48d242`  
**Scope:** standalone WinUI 3 Overlay visual hierarchy + Main App Profile presentation alignment  
**Implementation shape:** one focused PR

---

# 0. Goal

The current Overlay has several visually similar setting groups that are implemented through different layout paths.

That is acceptable where the product types are genuinely different:

~~~text
Profile catalog
Shortcut tile grid
Setting expand/collapse behavior
~~~

but it is currently causing inconsistent presentation for controls that are the same visual type:

~~~text
top-level setting section
    -> detail rows
~~~

Concrete user-visible symptoms on current main:

1. PR669 puts the separator on every ordinary row, so PL1/PL2, M1/M2, Left/Right Motor, and ClawHUD detail rows are divided individually.
2. M1/M2 are not indented under the "M1 / M2" section header, while comparable detail rows are indented.
3. Generic Device/Profile sections and custom Controller sections duplicate slightly different detail-stack layout code.
4. Intel FPS Limit is still deliberately hidden in the Main App Profile page even though its production snapshot/mutation code is present.
5. Main App and Overlay Profile feature ordering is the old:
   `TDP -> Intel FPS -> CPU Boost -> Windows Power Mode -> Resolution`.
6. Device Overlay says `TDP`, while the Main App already says `TDP Control`.

This PR must align those items without turning the Overlay into a generalized UI framework.

Target visual hierarchy:

~~~text
Page Title

TDP Control                         <- first top-level option: no separator
  Plugged in · PL1
  Plugged in · PL2

────────────────────────────────
CPU Boost                           <- separator belongs to the section
  Plugged in

────────────────────────────────
Windows Power Mode
  Plugged in
~~~

Controller target:

~~~text
M1 / M2                             <- first section: no separator
  M1
  M2

────────────────────────────────
Joystick LED
  Brightness
  Color
  Red
  Green
  Blue

────────────────────────────────
Vibration Strength
  Left Motor
  Right Motor
~~~

The rule is:

> **Top-level option sections own separators. Detail rows do not.**

---

# 1. Mandatory project review

Read the current versions before implementation:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md

docs/work-order/OVERLAY_QAM_TITLE_SPACING_AND_ROW_SEPARATORS_WORK_ORDER_2026-10-04.md
docs/work-order/OVERLAY_PROFILE_TWO_COLUMN_AND_RESOLUTION_ALIGNMENT_WORK_ORDER_2026-10-04.md
~~~

This work order intentionally supersedes only the **row-level separator placement** from
`OVERLAY_QAM_TITLE_SPACING_AND_ROW_SEPARATORS_WORK_ORDER_2026-10-04.md`.

Keep the PR669 title-spacing decision:

~~~text
QamPageTitleMargin = 16,20,16,16
~~~

and keep its useful selection decision:

~~~text
ordinary row selection = selected background fill only
separator color is not selection state
~~~

Do not revert either of those.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayNumericSliderRow.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs

src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml

tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

If main moves from the reviewed SHA, re-read affected files and adapt this work order to the current implementation.

---

# 3. Architecture boundary

Do **not** try to make every Overlay page use one renderer.

The current split is valid:

~~~text
Overlay
├─ Device
│  └─ generic Quick Settings renderer
├─ Profile
│  ├─ catalog -> custom tile UI
│  └─ selected/active detail -> generic Quick Settings renderer
├─ Controller
│  └─ custom controller UI
├─ Shortcut
│  └─ custom tile UI
└─ Setting
   └─ custom expandable-card UI
~~~

The cleanup target is narrower:

~~~text
same semantic visual type
    -> same section-boundary rule
    -> same detail indent rule
    -> same ordinary row chrome
~~~

Do not create:

- `OverlaySectionViewModel`;
- `OverlaySectionBase`;
- a page registry;
- a generalized renderer for Controller/Setting/Shortcut;
- a new visual-state manager;
- a second navigation model;
- a new contract or transport schema for presentation-only metadata.

One or two tiny construction helpers on the existing `OverlayWindow` partial are enough.

---

# 4. Current implementation facts

## 4.1 PR669 separator is owned by every ordinary row

Current `OverlayRowChrome.Create(...)` uses:

~~~csharp
BorderThickness = OverlayQamResources.Get(
    "QamRowSeparatorThickness",
    new Thickness(0, 0, 0, 1)),
BorderBrush = OverlayQamResources.Brush("QamSeparatorBrush"),
~~~

Every `OverlayToggleRow`, `OverlayValueRow`, and `OverlayNumericSliderRow` uses this row chrome.

Result:

~~~text
TDP header row
PL1 row
PL2 row
CPU header row
...

all receive the same bottom line
~~~

That is the wrong semantic owner.

## 4.2 The generic Device/Profile renderer already knows parent/detail hierarchy

`BuildQuickSettingsSection(...)` already distinguishes:

~~~text
first visible Toggle + section label
    -> feature header row

remaining rows
    -> detailStack
       Margin = QamDetailIndent
~~~

So TDP/CPU/Power/FPS sections already have the correct conceptual hierarchy.

Resolution is the intentional non-toggle special case and already uses the same `QamDetailIndent` resource.

## 4.3 Controller duplicates the same hierarchy manually

Current Controller implementation has three top-level groups:

~~~text
M1 / M2
Joystick LED
Vibration Strength
~~~

but their detail construction differs.

Current M1/M2:

~~~text
mappingSection
  heading
  M1 row
  M2 row
~~~

No `QamDetailIndent`.

Current Vibration:

~~~text
vibrationSection
  heading
  details StackPanel
      Margin = QamDetailIndent
      Left Motor
      Right Motor
~~~

Current LED also creates its own indented detail StackPanel.

These are the same visual concept and should use one small shared detail-stack construction path.

## 4.4 Setting is a different interaction type, but its outer cards are still top-level sections

`ClawHUD`, `Quick Settings`, and `Tab Order` are custom expandable cards.

Keep their expand/collapse implementation.

However, their **outer containers** are top-level setting sections and should follow the same separator rule:

~~~text
ClawHUD       -> first: no separator
Quick Settings -> separator above
Tab Order      -> separator above
~~~

Do not add separators to ClawHUD internal rows.

## 4.5 Intel FPS Main App UI is hidden only by XAML presentation policy

Current `ProfilePage.xaml` declares:

~~~xml
<ctcontrols:SettingsExpander
    x:Name="IntelFpsExpander"
    Header="Intel FPS Limit"
    Visibility="Collapsed"
    ...>
~~~

The production code behind already exists:

- `FpsEnabledToggle_Toggled`;
- AC/DC FPS sliders;
- delayed FPS commit;
- mutation failure restoration;
- `snapshot.FpsLimit?.Available` gating;
- unavailable-description rendering.

This PR must expose the existing feature. Do not create another Intel FPS authority or mutation path.

## 4.6 Profile ordering differs from the new product decision

Current shared Overlay projection:

~~~text
ProfileGeneral
ProfileTdp
ProfileFpsLimit
ProfileCpuBoost
ProfilePowerMode
ProfileResolution
~~~

Current Main App XAML presents the same old feature order.

New required order:

~~~text
TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

`ProfileGeneral` / game title + profile-enable remains the context header before those options.

## 4.7 Main App already says TDP Control

Current Main App:

~~~text
DevicePage.xaml  -> Header="TDP Control"
ProfilePage.xaml -> Header="TDP Control"
~~~

Current Overlay Device projection has:

~~~csharp
new QuickSettingsRow(..., "TDP Control", ...)
...
return new QuickSettingsSection(
    QuickSettingsSectionId.DeviceTdp,
    "TDP",
    rows);
~~~

The generic renderer uses the section label as the visible feature-header label, so the Overlay shows `TDP`.

Only the shared Device section label needs correction.

---

# 5. Change A — move separators from rows to top-level sections

## 5.1 Remove row-level separator ownership

`OverlayRowChrome` must return to ordinary row chrome only.

Remove use of:

~~~text
QamRowSeparatorThickness
QamSeparatorBrush
~~~

from `OverlayRowChrome.Create(...)`.

Restore a non-visible ordinary row border, for example by reusing the existing resources:

~~~csharp
BorderThickness = OverlayQamResources.Get(
    "QamSelectionBorderThickness",
    new Thickness(0)),
BorderBrush = OverlayQamResources.Brush("QamFocusBorderBrush"),
~~~

Do not restore border mutation inside `ApplyRowSelectionVisual()`.

Keep selection fill-only:

~~~csharp
rows[i].Container.Background =
    selected ? _rowSelectedFillBrush : RowUnselectedFillBrush;
~~~

This preserves the good PR669 separation between:

~~~text
row selected state
section divider state
~~~

## 5.2 Replace the row thickness resource with a section thickness resource

Keep:

~~~xml
<SolidColorBrush x:Key="QamSeparatorBrush" Color="#1AFFFFFF" />
~~~

Replace:

~~~xml
<Thickness x:Key="QamRowSeparatorThickness">0,0,0,1</Thickness>
~~~

with:

~~~xml
<Thickness x:Key="QamSectionSeparatorThickness">0,1,0,0</Thickness>
~~~

The divider belongs on the **top** edge of every non-first top-level option section.

Do not add per-row separator resources.

## 5.3 One shared section-card separator seam

Keep `CreateOverlaySectionCard(...)` as the existing common outer-section owner.

Add only a tiny helper/seam, conceptually:

~~~csharp
private static void SetOverlaySectionSeparator(Border card, bool visible)
{
    card.BorderBrush = OverlayQamResources.Brush("QamSeparatorBrush");
    card.BorderThickness = visible
        ? OverlayQamResources.Get(
            "QamSectionSeparatorThickness",
            new Thickness(0, 1, 0, 0))
        : new Thickness(0);
}
~~~

Exact naming may follow current conventions.

Do not add a class for this.

## 5.4 Separator width must match the top-level row boundary without moving content

The current body has 16-DIP horizontal padding, while ordinary rows use:

~~~text
QamRowMargin = -16,0,-16,0
~~~

so actionable rows extend to the wider section boundary.

A separator drawn only on the old section-card content box may appear 16 DIP too inset.

Preferred minimal geometry-preserving solution:

~~~text
section card outer margin = existing QamRowMargin
section card horizontal padding compensates by 16 DIP
child visual coordinates remain unchanged
separator spans the same width as the top-level row boundary
~~~

For example, `QamSectionPadding` may become:

~~~xml
<Thickness x:Key="QamSectionPadding">16,0,16,0</Thickness>
~~~

while `CreateOverlaySectionCard` uses:

~~~csharp
Margin = OverlayQamResources.Get(
    "QamRowMargin",
    new Thickness(-16, 0, -16, 0)),
Padding = OverlayQamResources.Get(
    "QamSectionPadding",
    new Thickness(16, 0, 16, 0)),
~~~

This should preserve the existing location of section headings and child controls:

~~~text
old card x + 0
new card (x - 16) + padding 16
=> same child x
~~~

and preserve row full-width behavior:

~~~text
child x - row margin 16
=> same full-width row x
~~~

An equivalent smaller implementation is acceptable if it demonstrably keeps the existing child alignment and gives the separator the intended full section width.

Do not change horizontal text/control alignment merely to place the line.

---

# 6. Change B — separator policy by page

## 6.1 Device generic Quick Settings

Current visible top-level order is:

~~~text
TDP Control
CPU Boost
Windows Power Mode
Battery Charge Limit
~~~

Required:

~~~text
TDP Control             no separator
CPU Boost               separator above
Windows Power Mode      separator above
Battery Charge Limit    separator above
~~~

Only currently rendered section cards count.

A section with zero visible rows and `Card == null` must not consume the "first section" slot.

## 6.2 Profile generic Quick Settings

`ProfileGeneral` is context, not a feature option:

~~~text
007 First Light     [profile toggle]
~~~

It must:

- have no separator;
- not count as the first top-level feature.

Then the **first currently rendered feature** has no separator.

Examples:

Normal:

~~~text
ProfileGeneral       no separator / not counted
TDP Control          no separator (first feature)
CPU Boost            separator
Windows Power Mode   separator
Intel FPS Limit      separator
Resolution           separator
~~~

If TDP is absent because `Limits == null`:

~~~text
ProfileGeneral       no separator / not counted
CPU Boost            no separator (first rendered feature)
Windows Power Mode   separator
Intel FPS Limit      separator
Resolution           separator
~~~

Do not hard-code "TDP has no separator".

Derive it from the current rendered section order.

## 6.3 Generic renderer must re-apply separator ownership after shape/order reconciliation

Profile capabilities can add/remove sections, and generic sections can be rebuilt independently.

Therefore after:

~~~text
RebuildQuickSettingsContent(...)
ReconcileQuickSettingsSections(...)
ReorderQuickSettingsSectionCards(...)
~~~

the current card order must have section separators re-applied from current reality.

A small method such as:

~~~text
ApplyQuickSettingsSectionSeparators(surface, page)
~~~

is appropriate.

It must be stateless.

Do not add separator flags to:

- `QuickSettingsSection`;
- contracts;
- transport messages;
- Runtime snapshots.

This is purely Overlay presentation.

## 6.4 Controller custom sections

Use the same `SetOverlaySectionSeparator` helper on the existing outer section cards:

~~~text
M1 / M2              no separator
Joystick LED         separator
Vibration Strength   separator
~~~

The Controller page remains custom.

Do not migrate it into `QuickSettingsPresentation`.

## 6.5 Setting custom expandable cards

Keep the current Setting-card implementation and apply the same outer separator rule:

~~~text
ClawHUD          no separator
Quick Settings   separator
Tab Order        separator
~~~

No internal ClawHUD / Tab Order detail row receives a separator.

## 6.6 Explicit exclusions

Do not add this top-level separator to:

- PageTitle;
- left icon rail;
- Profile catalog tiles;
- Shortcut tiles;
- ClawHUD detail rows;
- M1 row;
- M2 row;
- Left Motor / Right Motor;
- TDP PL1/PL2;
- CPU/Power/FPS AC/DC detail rows;
- Resolution detail row itself.

---

# 7. Change C — one shared detail-stack layout for same-type detail rows

The generic renderer and custom Controller implementation currently create the same concept differently.

Add one tiny helper on the existing `OverlayWindow` partial, conceptually:

~~~csharp
private static StackPanel CreateOverlayDetailStack() => new()
{
    Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0),
    Margin = OverlayQamResources.Get(
        "QamDetailIndent",
        new Thickness(16, 0, 0, 0)),
};
~~~

Use it only for the semantic type:

~~~text
top-level option
  -> indented detail controls
~~~

## 7.1 Generic Quick Settings

Replace the duplicated feature-header detail construction with this helper.

Keep existing feature-header behavior:

~~~text
first visible Toggle
-> strong-label header row

remaining rows
-> shared detail stack
~~~

Keep Profile Resolution's already-approved detail indentation, but route it through the same indent/spacing authority where practical.

Do not change Resolution contracts or values.

## 7.2 Controller M1/M2

This is the concrete missing-indent bug.

Current:

~~~text
M1 / M2
M1
M2
~~~

Required:

~~~text
M1 / M2
    M1
    M2
~~~

Create one shared detail stack under the `M1 / M2` heading and add both mapping rows to it.

The logical row list must still contain exactly M1 then M2 in the same navigation order.

Do not add an edit mode or extra selection level.

## 7.3 Controller LED

The current LED detail stack already has the correct conceptual indent but uses its own construction and section-header spacing.

Use the shared detail-stack helper for:

~~~text
Brightness
Color preview
Red
Green
Blue
~~~

Keep the existing LED enabled row as the top-level feature header.

Keep LED child visibility semantics unchanged.

## 7.4 Controller Vibration

Use the same detail-stack helper for:

~~~text
Left Motor
Right Motor
~~~

under:

~~~text
Vibration Strength
~~~

Do not change the 300ms vibration commit behavior.

## 7.5 Setting expandable details remain separate

Do **not** force `CreateSettingCard(...)` detail contents onto this helper.

Setting cards use `QamDetailMargin` because they are expand/collapse card interiors, not ordinary always-visible feature-child stacks.

This is a real semantic difference and should remain.

---

# 8. Change D — expose Intel FPS Limit in the Main App Profile page

In:

~~~text
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
~~~

remove:

~~~xml
Visibility="Collapsed"
~~~

from `IntelFpsExpander`.

Do not add a code-behind visibility toggle.

The expander should remain visible as a normal Profile feature. Its editability is already governed by the authoritative snapshot.

Preserve:

~~~text
FpsEnabledToggle.IsEnabled =
    snapshot.Exists
    && snapshot.Enabled
    && snapshot.PersistenceWritable
    && snapshot.FpsLimit?.Available == true
~~~

and preserve the existing description policy:

~~~text
Available
-> "Uses Intel's official API. Some games may not support FPS limiting."

Unavailable
-> snapshot unavailable reason or the current fallback text
~~~

Keep:

- `IsExpanded="False"`;
- current icon;
- existing AC/DC sliders;
- existing 40-120 range;
- existing mutation callbacks;
- delayed FPS commit;
- mutation failure restoration.

No Runtime or transport change should be needed.

---

# 9. Change E — align Profile feature order in Main App and Overlay

Required user-facing order on both surfaces:

~~~text
1. TDP Control
2. CPU Boost
3. Windows Power Mode
4. Intel FPS Limit
5. Resolution
~~~

## 9.1 Shared Overlay projection

Change only the section-add order in:

~~~text
QuickSettingsPresentation.BuildProfile(...)
~~~

Required conceptual sequence:

~~~csharp
var sections = new List<QuickSettingsSection>
{
    BuildProfileGeneralSection(snapshot),
};

if (snapshot.Limits is not null)
    sections.Add(BuildProfileTdpSection(snapshot));

sections.Add(BuildProfileCpuBoostSection(snapshot));

if (snapshot.PowerMode is not null)
    sections.Add(BuildProfilePowerModeSection(snapshot));

if (snapshot.FpsLimit is not null)
    sections.Add(BuildProfileFpsLimitSection(snapshot));

sections.Add(BuildProfileResolutionSection(snapshot));
~~~

Keep the current optional-capability rules.

Do not fabricate a Power Mode or FPS section when its snapshot is null.

If an FPS snapshot exists but reports `Available=false`, preserve the current disabled/unavailable section behavior.

## 9.2 Main App Profile XAML

Reorder the existing blocks in `ProfilePage.xaml` to:

~~~text
TdpExpander
CpuBoostExpander
PowerModeExpander
IntelFpsExpander
Resolution SettingsCard
~~~

Do not duplicate or recreate controls.

Move the existing XAML blocks intact except for removing the FPS hidden visibility.

## 9.3 No mutation-order coupling

This is display order only.

Do not change:

- mutation RPC order;
- debounce ownership;
- Runtime reconciliation order;
- profile persistence schema;
- profile JSON ordering assumptions.

---

# 10. Change F — standardize user-visible TDP naming to "TDP Control"

The canonical user-facing name is now:

~~~text
TDP Control
~~~

Main App Device and Profile already use that label.

Change the Device shared Quick Settings section:

Current:

~~~csharp
return new QuickSettingsSection(
    QuickSettingsSectionId.DeviceTdp,
    "TDP",
    rows);
~~~

Required:

~~~csharp
return new QuickSettingsSection(
    QuickSettingsSectionId.DeviceTdp,
    "TDP Control",
    rows);
~~~

Do not rename enum members, row IDs, runtime classes, log categories, persisted JSON properties, or internal symbols solely for presentation consistency.

This is a user-facing label change only.

Profile already uses `TDP Control`; keep it.

---

# 11. Tests

Update existing tests. Do not add a separate UI test framework.

## 11.1 `QuickSettingsPresentationTests`

Update exact Profile section order to:

~~~text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

Add/assert:

~~~text
DeviceTdp section Label == "TDP Control"
ProfileTdp section Label == "TDP Control"
~~~

Preserve all existing row-order, commit-policy, linked-slider, availability, and optional-section tests.

## 11.2 `UiArchitectureTests`

Update `Profile_feature_order_collapsed_defaults_and_resolution_contract_are_explicit`.

Required assertions:

- `IntelFpsExpander` declaration does **not** contain `Visibility="Collapsed"`;
- TDP comes before CPU Boost;
- CPU Boost comes before Windows Power Mode;
- Windows Power Mode comes before Intel FPS Limit;
- Intel FPS Limit comes before Resolution;
- all feature expanders still default collapsed;
- Resolution remains a normal SettingsCard;
- current icons remain.

Add or keep a Device UI assertion proving:

~~~text
Device TDP Header == "TDP Control"
~~~

so Main App and shared Overlay naming cannot drift again silently.

## 11.3 `OverlayQamVisualResourcesTests`

Replace PR669 row-separator expectations.

Required:

~~~text
QamSeparatorBrush == #1AFFFFFF
QamSectionSeparatorThickness == 0,1,0,0
QamRowSeparatorThickness does not exist
~~~

`OverlayRowChrome` must not consume `QamSeparatorBrush` or `QamSectionSeparatorThickness`.

Keep the existing test proving:

~~~text
ApplyRowSelectionVisual
-> changes row Background
-> does not change row BorderBrush
~~~

Add source-contract coverage that the section-card path owns the separator.

Keep:

~~~text
QamPageTitleMargin == 16,20,16,16
~~~

unchanged.

## 11.4 `OverlayDeviceRendererWiringTests`

Cover the generic section behavior:

- same shared `CreateOverlaySectionCard`;
- shared detail-stack helper is used for feature child rows;
- section separators are re-applied after rebuild/reconcile/order changes;
- `ProfileGeneral` is excluded from feature separator counting;
- first rendered feature does not get a separator;
- later rendered features do.

Do not encode a special rule saying `ProfileTdp` is always first.

The test should allow CPU Boost to become first when TDP is omitted.

## 11.5 `OverlayControllerRendererTests`

Update current manual-layout expectations.

Required:

~~~text
M1 / M2
-> one shared QamDetailIndent detail stack
-> M1 then M2

Joystick LED
-> same detail-stack helper for child controls

Vibration Strength
-> same detail-stack helper for Left/Right Motor
~~~

Also assert section separator assignment:

~~~text
M1 / M2            false
Joystick LED       true
Vibration Strength true
~~~

Do not weaken tests for controller row capabilities, mutation availability, or navigation order.

## 11.6 Setting source-contract coverage

Add to the most appropriate existing Overlay UI test:

~~~text
ClawHUD        no top separator
Quick Settings top separator
Tab Order      top separator
~~~

and verify no row-level separator is introduced inside expanded ClawHUD details.

---

# 12. Manual validation

Primary hardware target:

~~~text
MSI Claw
1920 x 1200
150% Windows scaling
~~~

Validate screenshots/real hardware for all relevant pages.

## Device Overlay

Expected:

~~~text
TDP Control
  PL1
  PL2

separator
CPU Boost
  current power-source detail

separator
Windows Power Mode
  current power-source detail

separator
Battery Charge Limit
  Limit
~~~

No detail row divider.

## Profile Overlay

Expected order:

~~~text
game title / profile enable

TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

Expected separator policy:

~~~text
game title context -> none
first rendered feature -> none
every later rendered feature -> one top separator
~~~

Test at least one profile where TDP is unavailable/omitted so CPU Boost becomes the first feature and receives no separator.

## Controller Overlay

Expected:

~~~text
M1 / M2
    M1
    M2

separator
Joystick LED

separator
Vibration Strength
    Left Motor
    Right Motor
~~~

M1/M2 left edge should align with the nested/detail rows used elsewhere.

## Setting Overlay

Expected:

~~~text
ClawHUD
    details without row separators

separator
Quick Settings

separator
Tab Order
~~~

Expand/collapse behavior must remain unchanged.

## Main App Profile

Verify:

~~~text
TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

Intel FPS Limit must be visible.

When Intel FPS capability is unavailable:

- the expander remains visible;
- controls are disabled according to current snapshot logic;
- unavailable text remains meaningful;
- no mutation can be sent from disabled controls.

---

# 13. Regression / lifecycle constraints

This PR must not change:

- Full1902 authority;
- Center M Enabled/Disabled transition;
- PID1901/PID1902 restoration/reconciliation;
- HidHide state;
- VIIPER ownership;
- DirectInput;
- Xbox360/SteamDeck presentation;
- physical device loss/re-enumeration;
- Sleep/Hibernate/Resume;
- shutdown/restart/crash recovery;
- routing rollback/fail-close;
- Overlay capture/release;
- show/hide/no-activate behavior;
- profile persistence format;
- Quick Settings transport schema;
- delayed commit timing;
- M1/M2 mutation semantics;
- LED mutation semantics;
- vibration commit semantics;
- Intel FPS Runtime ownership.

This is presentation hierarchy plus one already-implemented Main App feature exposure.

---

# 14. Non-goals

Do not:

- convert Controller to the generic Quick Settings renderer;
- convert Setting cards to the generic renderer;
- convert Profile catalog to generic sections;
- change Shortcut layout;
- create a new reusable visual-control library;
- add a generalized section descriptor;
- put separator metadata into contracts;
- change section vertical density beyond what is needed to relocate the divider;
- add detail-row separators;
- add another indent resource for M1/M2;
- add a second Intel FPS implementation;
- rename internal TDP types/classes/enums for UI wording.

---

# 15. Expected production-file scope

Expected changes should remain roughly within:

~~~text
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs

src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
~~~

`ProfilePage.xaml.cs` should normally require no behavioral change.

If implementation starts touching Runtime, transport, controller authority, profile persistence, or multiple new abstractions, stop and re-evaluate the design.

---

# 16. Documentation update

Update current design wording where it is user-facing/current authority:

~~~text
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
~~~

Change the Profile example from `TDP` to `TDP Control` and keep the new feature order consistent where order is presented.

If the current Overlay UI design document contains a normative visible label list, use `TDP Control` there as well.

Do not rewrite historical work orders merely to match the new naming/order.

---

# 17. Acceptance criteria

The PR is complete when all of the following are true:

1. PR669 page-title margin remains `16,20,16,16`.
2. `QamSeparatorBrush` remains the low-contrast separator color.
3. Ordinary rows no longer draw separators.
4. Ordinary row selection remains fill-only.
5. Separator ownership is top-level section/card only.
6. The first top-level option has no separator.
7. Every later visible top-level option has exactly one separator above it.
8. ProfileGeneral/game title is context and does not consume the first-feature separator slot.
9. Optional Profile sections do not break first-feature separator selection.
10. M1/M2 use the same existing detail indent as comparable detail rows.
11. Generic feature details, Controller mapping details, LED details, and vibration details share one small detail-stack layout authority where semantically identical.
12. Setting expand/collapse details remain on their appropriate separate layout path.
13. Main App Intel FPS Limit is visible again.
14. Intel FPS existing availability/mutation behavior is unchanged.
15. Main App and Overlay Profile order is:
    `TDP Control -> CPU Boost -> Windows Power Mode -> Intel FPS Limit -> Resolution`.
16. Device Overlay says `TDP Control`.
17. Main App Device/Profile continue to say `TDP Control`.
18. No Runtime/transport/lifecycle contract changes are introduced.
19. Existing build/tests plus the updated source-contract tests pass.
20. Hardware review confirms the section hierarchy is visually consistent without turning detail rows into individually separated cards.

---

# 18. Implementation principle

The target is not "all pages use the same code."

The target is:

> **When two controls represent the same visual hierarchy, they use the same existing layout authority. When they represent genuinely different product interactions, they remain separate.**

Preferred final ownership:

~~~text
QamOverlayResources.xaml
    -> visual metrics / separator brush

OverlayRowChrome
    -> ordinary row chrome only

CreateOverlayDetailStack
    -> shared indent + detail-row spacing

CreateOverlaySectionCard / SetOverlaySectionSeparator
    -> top-level section boundary

QuickSettings generic renderer
    -> Device/Profile data-driven section ordering

Controller custom builder
    -> controller-specific content using shared section/detail presentation seams

Setting custom builder
    -> expand/collapse behavior using shared outer section boundary only
~~~

Keep the implementation small and explicit.
