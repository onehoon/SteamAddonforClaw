# Work Order — Compact Overlay Width, Section Hierarchy, Toggle Alignment, and Numeric Hold-Repeat Polish

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `5d86580574c085bd38395005d348fca7f0b77549`  
**Feature area:** WinUI 3 Addon Overlay presentation only  
**Implementation shape:** **one PR, two focused commits**

---

## 0. Goal

Polish the current WinUI 3 Addon Overlay after the merged Profile catalog/shared Profile work.

The current functionality is correct, but real 1920×1200 / 150% usage exposed four presentation problems:

1. the Overlay is too wide;
2. game title / TDP / Intel FPS Limit / CPU Boost / Windows Power Mode / Resolution are visually too similar, so the Profile detail hierarchy is hard to scan;
3. ToggleSwitch tracks appear horizontally displaced from the right edge compared with value controls;
4. numeric `-` / `+` controls require one click per step instead of supporting press-and-hold repeat.

This PR must address those issues as a **shared Overlay presentation polish**, not as a Profile-only workaround.

Required result:

~~~text
Overlay surface
    960 DIP max width
        -> 720 DIP max width

Shared Quick Settings sections
    plain text groups
        -> subtle section cards with stronger section headers

Toggle rows
    default WinUI ToggleSwitch reserved width
        -> compact right-aligned switch track

Numeric steppers
    click once = one step only
        -> click once = one step
        -> press and hold = repeated steps

Profile catalog
    3 columns retained
    long titles must remain readable at the narrower width
~~~

No Runtime/controller/transport/product-contract behavior changes belong in this PR.

---

# 1. Mandatory source review before coding

Read the latest implementation-branch versions before editing.

## 1.1 Full1902 authority

At minimum:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

This UI polish must not change:

- PID1901 ↔ PID1902 ownership;
- HidHide ownership or normalization;
- physical DirectInput ownership;
- VIIPER ownership/teardown;
- Xbox360 ↔ SteamDeck presentation selection;
- Overlay capture/neutral ordering;
- Overlay Runtime-owned dismiss;
- Sleep / Hibernate / Resume;
- Restart / Crash / Shutdown;
- PnP recovery;
- routing rollback/fail-close behavior.

The Overlay remains a presentation surface only.

## 1.2 Existing Overlay architecture

Read the latest versions of:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs

src/SteamInputAddonforClaw.Overlay/OverlayRowChrome.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
~~~

Also read the current relevant UI tests before changing source-shaped contracts.

---

# 2. Reviewed current implementation

The reviewed `main` already has the correct ownership boundaries.

## 2.1 Width

Current geometry:

~~~csharp
internal const double MaxSurfaceWidthDip = 960.0;
~~~

Current XAML:

~~~xml
<Border
    x:Name="OpaquePanel"
    MaxWidth="960"
    ... />
~~~

At 1920×1200 and 150% DPI, the 960 DIP cap becomes 1440 physical pixels, which is visually too wide for the current controller-first layout.

The requested compact target is **720 DIP**.

At 150%:

~~~text
720 DIP × 1.5 = 1080 physical pixels
~~~

This is approximately 25% narrower than the current 1440-pixel capped surface.

## 2.2 Shared Device/Profile section renderer

`OverlayWindow.QuickSettings.cs` already owns one generic section renderer for Device and Profile.

Current sections are essentially:

~~~text
StackPanel
    optional heading text / feature-header toggle row
    optional message
    child rows
~~~

and are appended directly to the surface content.

This is the correct place to add **shared section-card hierarchy**.

Do not create a second Profile renderer.

## 2.3 Feature header toggle

`OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle()` already detects sections whose first visible row is a toggle.

The generic renderer already promotes that first toggle row into a feature header using the section label:

~~~text
TDP Control              [toggle]
    Plugged in · PL1      [-] 35 [+]
    Plugged in · PL2      [-] 45 [+]
~~~

Keep this product behavior.

The visual problem is hierarchy/chrome, not section ownership.

## 2.4 Toggle positioning

`OverlayToggleRow` currently creates a standard WinUI `ToggleSwitch` with:

~~~csharp
OnContent = null,
OffContent = null,
VerticalAlignment = VerticalAlignment.Center,
HorizontalAlignment = HorizontalAlignment.Right,
~~~

but does not override the control's default minimum width.

The WinUI ToggleSwitch template reserves horizontal space intended for normal On/Off-content presentation, so the visible track can appear noticeably left of the right edge even though the control itself is right-aligned.

Fix this at the **shared Toggle row primitive**, not with Profile-specific margins.

## 2.5 Numeric value buttons

`OverlayValueRow` currently creates both numeric and discrete controls through `CreateIconButton(...)`, which returns a normal `Button`.

The existing model already supports repeated semantic adjustment correctly:

~~~csharp
_model.RequestAdjust(+1);
_model.RequestAdjust(-1);
~~~

and `OverlayValueRowTests.RepeatedAdjustmentsContinueFromLocalPreviewWithoutReadback` already proves repeated adjustments accumulate from the current preview.

Therefore no new repeat timer/model/state machine is required.

Use WinUI's native `RepeatButton` for numeric steppers only.

## 2.6 Setting tab

The Setting tab currently contains two logical groups:

~~~text
ClawHUD
Tab Order
~~~

They are plain sections today.

They should receive the same **section-card visual language** as Device/Profile.

However their existing builders and mutation/navigation ownership stay unchanged.

## 2.7 Shortcut tab

Shortcut already renders four explicit cards in a 2×2 grid using `CardBackgroundFillColorDefaultBrush`.

Do not wrap Shortcut in another section card.

## 2.8 Profile catalog

Profile catalog already owns:

- three columns;
- Favorite-first ordering;
- bounded controller navigation;
- selection visibility/BringIntoView;
- card selection;
- selected-detail transition.

Keep the three-column navigation contract.

The narrower 720 DIP surface only requires long game names to render cleanly.

---

# 3. PR structure

Implement as **one PR with two commits**.

## Commit 1 — Compact geometry + shared row primitive polish

Scope:

1. 960 → 720 DIP surface cap;
2. compact right-aligned ToggleSwitch;
3. native hold-repeat for `NumericStepper`;
4. retain single-click behavior for `DiscreteChoice`;
5. adapt Profile catalog title rendering to the narrower width.

Suggested commit message:

~~~text
Polish compact Overlay geometry and shared controls
~~~

## Commit 2 — Shared section-card hierarchy

Scope:

1. Device/Profile generic Quick Settings sections;
2. Profile game/general heading hierarchy through the generic renderer;
3. Setting/ClawHUD section card;
4. Setting/Tab Order section card;
5. no double-card treatment for Shortcut;
6. no Controller placeholder redesign.

Suggested commit message:

~~~text
Add shared Overlay section hierarchy
~~~

Do not split this into multiple PRs. The surface width, right-side control geometry, and section hierarchy form one visual change and should be reviewed together.

---

# 4. Commit 1 — Surface width

## 4.1 Geometry constant

Change:

~~~csharp
OverlayWindowGeometry.MaxSurfaceWidthDip
~~~

from:

~~~text
960.0
~~~

to:

~~~text
720.0
~~~

Keep:

- existing taskbar/reference margin logic;
- existing floating gap;
- existing monitor-origin handling;
- existing DPI scaling;
- existing small-monitor clamping;
- centered X calculation;
- full current height behavior.

This PR changes **horizontal cap only**.

Do not reduce the vertical surface height.

## 4.2 XAML cap

Change:

~~~xml
OpaquePanel MaxWidth="960"
~~~

to:

~~~xml
OpaquePanel MaxWidth="720"
~~~

The XAML and geometry caps must remain identical.

Do not introduce a second configurable width setting.

Do not add a persisted user preference in this PR.

## 4.3 Reference-display expected geometry

For the product reference display:

~~~text
Monitor: 1920 × 1200
DPI:     144 / 150%
outer Y margin: existing 90 px
surface max width: 1080 px
centered X: (1920 - 1080) / 2 = 420 px
height: existing 1020 px
~~~

So the reference geometry becomes:

~~~text
OverlayRect(420, 90, 1080, 1020)
~~~

Update all geometry tests from the actual calculation, not by loosening assertions.

---

# 5. Commit 1 — Shared ToggleSwitch alignment

## 5.1 Fix the primitive, not each caller

Modify `OverlayToggleRow`.

The ToggleSwitch must remain in the right-side Auto column.

Use the smallest native WinUI adjustment needed to stop the default ToggleSwitch minimum width from creating the visible offset.

Required baseline:

~~~csharp
_toggle = new ToggleSwitch
{
    OnContent = null,
    OffContent = null,
    MinWidth = 0,
    VerticalAlignment = VerticalAlignment.Center,
    HorizontalAlignment = HorizontalAlignment.Right,
};
~~~

If the current Windows App SDK ToggleSwitch template still reserves visibly unnecessary horizontal space after `MinWidth = 0`, use a **small explicit positive width for the compact switch control** based on the native track, rather than:

- negative margins;
- per-page offsets;
- Profile-only margins;
- absolute placement;
- Canvas coordinates.

The visual requirement is:

~~~text
right edge of ToggleSwitch track
≈ right edge of numeric/discrete value-control group
~~~

across:

- Device;
- Profile;
- Setting / ClawHUD.

## 5.2 Do not change toggle behavior

Preserve:

- authoritative `ApplyState()`;
- Toggled suppression;
- A/Accept controller activation;
- pointer/touch change behavior;
- availability/writability semantics;
- current selection chrome.

No new toggle model or renderer.

---

# 6. Commit 1 — Numeric press-and-hold repeat

## 6.1 Use native RepeatButton

Add the WinUI primitives namespace required for:

~~~text
Microsoft.UI.Xaml.Controls.Primitives.RepeatButton
~~~

For:

~~~text
OverlayValueButtonKind.NumericStepper
~~~

create `RepeatButton` controls for previous/next instead of normal `Button`.

Use these product timings:

~~~text
Delay    = 300 ms
Interval = 75 ms
~~~

Required behavior:

~~~text
short click
    -> exactly one semantic step

press and hold
    -> first step
    -> wait 300 ms
    -> repeat every ~75 ms until released or boundary reached
~~~

Use the existing Click event seam.

Do not create:

- DispatcherTimer;
- System.Threading.Timer;
- repeat cancellation token;
- repeat generation;
- repeat manager;
- background task.

`RepeatButton` owns pointer press/release repetition.

## 6.2 Discrete choices do NOT repeat

For:

~~~text
OverlayValueButtonKind.DiscreteChoice
~~~

retain normal `Button`.

Examples:

- CPU Boost;
- Windows Power Mode;
- Resolution;
- ClawHUD Display Mode;
- ClawHUD Font;
- ClawHUD Alignment;
- ClawHUD Background.

These small option sets should move one value per click/press.

## 6.3 Shared numeric behavior

The shared NumericStepper treatment applies wherever that primitive is already used.

Current examples include:

- Device/Profile TDP numeric controls;
- Profile Intel FPS Limit;
- ClawHUD HUD Size;
- ClawHUD Opacity;
- future callers that intentionally select `NumericStepper`.

Do not add feature-name checks such as:

~~~csharp
if (row.Label.Contains("TDP"))
if (row.RowId == ...)
~~~

The primitive type is the authority.

## 6.4 Preserve existing model semantics

Do not change:

~~~text
OverlayValueModel normalization
minimum / maximum
step snapping
CanDecrease / CanIncrease
local PreviewValue
controller Left/Right behavior
Quick Settings debounce contract
~~~

At boundaries, the current model already emits no duplicate callback and disables the unavailable direction.

Retain that behavior during hold-repeat.

## 6.5 Do not add controller auto-repeat here

This work is specifically for the on-screen previous/next controls.

Do not add another repeated D-pad/joystick timer to the controller-input path.

Existing controller navigation semantics stay unchanged.

---

# 7. Commit 1 — Profile catalog at 720 DIP

Keep:

~~~text
3 columns
8 DIP column spacing
existing Favorite-first order
existing bounded navigation
existing BringIntoView
~~~

Do not change to a two-column catalog merely because the surface is narrower.

## 7.1 Use an explicit TextBlock for game title content

Replace raw string Button content with a small title `TextBlock` so long game names can wrap.

Required behavior:

~~~text
TextWrapping = Wrap
MaxLines = 2
TextTrimming = CharacterEllipsis
HorizontalAlignment = Stretch/Left as appropriate
~~~

Keep the card selectable as the same `Button`.

No cover image.

No Favorite icon.

No source badge.

No search.

If necessary, modestly increase the existing catalog card minimum height only enough to support two title lines without clipping.

Do not change the 3-column selection mathematics.

---

# 8. Commit 2 — Shared section-card visual language

## 8.1 Product rule

Cards represent **logical sections**, not individual rows.

Correct:

~~~text
┌ TDP Control                         [toggle] ┐
│   Plugged in · PL1            [-] 35 [+]   │
│   Plugged in · PL2            [-] 45 [+]   │
└──────────────────────────────────────────────┘
~~~

Incorrect:

~~~text
[TDP Control toggle card]
[PL1 row card]
[PL2 row card]
~~~

Do not turn every editable row into its own card.

## 8.2 Shared section card chrome

Use the existing Fluent resource:

~~~text
CardBackgroundFillColorDefaultBrush
~~~

Recommended fixed visual values:

~~~text
CornerRadius: 8
Padding:      8
section-to-section spacing: 10
internal section spacing: preserve current compact 4-5 DIP rhythm
BorderThickness: 0 unless a native Fluent resource requires otherwise
~~~

Do not add hard-coded decorative colors when an existing Fluent brush already represents card surface.

Do not introduce a new theme manager.

## 8.3 Tiny helper is allowed; framework is not

If both the generic Quick Settings renderer and Setting builders need the identical 5-10 line Border construction, one private/static helper on the existing `OverlayWindow` partial is acceptable, for example:

~~~csharp
private static Border CreateOverlaySectionCard(UIElement child)
~~~

Do not add:

- `OverlaySectionManager`;
- `OverlayCardRenderer`;
- a base-class hierarchy;
- an interface;
- a theme service;
- a generic layout framework.

If a helper does not materially reduce duplication, keep the code local.

---

# 9. Commit 2 — Device/Profile generic Quick Settings

## 9.1 Wrap each rendered logical section

In `RebuildQuickSettingsContent()`, keep the existing section rendering logic but append the finished `sectionPanel` through the section-card chrome.

Conceptually:

~~~text
foreach section:
    build existing sectionPanel
    build existing feature-header/detail rows
    wrap sectionPanel in one subtle card
    add card to surface.Content
~~~

Do not change:

- row identity;
- `_pageRows`;
- pointer selection registration;
- fast-path identity;
- mutation binding;
- pending drafts;
- selection restoration;
- BringIntoView.

Card containers are non-selectable presentation chrome.

## 9.2 Stronger section headers

Feature-header labels such as:

- TDP Control;
- Intel FPS Limit;
- CPU Boost;
- Windows Power Mode;

must be visually stronger than their child rows.

Use the existing Fluent strong-body typography rather than inventing custom font assets.

A small change to `OverlayToggleRow` such as an optional "strong label" construction flag is acceptable if it keeps one shared row primitive.

Preferred result:

~~~text
section/feature header = SemiBold / BodyStrong-level
child label            = existing BodyText-level
~~~

Do not create separate `ProfileToggleRow` or `DeviceToggleRow`.

## 9.3 Profile General / game title

The first Profile section already supplies the game name as its section label and uses the shared Profile-enabled toggle as the header row.

Keep that product structure.

With:

- its own section card;
- strong header typography;
- top-of-page position;

the game title must clearly read as the current Profile identity rather than as another ordinary row.

Do not duplicate the game title outside the shared Quick Settings product solely for decoration.

Do not add a separate Profile-only game-title model.

---

# 10. Commit 2 — Setting tab section cards

## 10.1 ClawHUD

Wrap the existing `BuildClawHudPage(rows)` logical section in one section card.

Keep:

- existing `ClawHUD` heading;
- status message;
- Enable HUD row;
- Display Mode;
- HUD Size;
- Font;
- Alignment;
- Background;
- Opacity;
- Intel VRR Range Fix;
- VRR status;
- existing mutation-in-flight behavior.

Do not put each ClawHUD row in its own card.

## 10.2 Tab Order

Wrap the existing `BuildTabOrderEditorPage(rows)` logical section in one section card.

Keep:

- current five rows;
- current up/down controls;
- current selection order;
- current Runtime authoritative reorder;
- existing row selection semantics.

Do not change tab-order product behavior.

---

# 11. Tabs intentionally not given another wrapper

## 11.1 Shortcut

Do not wrap the Shortcut page in a section card.

Its four 2×2 tiles are already card surfaces with:

~~~text
CardBackgroundFillColorDefaultBrush
CornerRadius
selected/unselected border
~~~

A parent card would produce an unnecessary card-inside-card hierarchy.

Only the narrower overall Overlay width naturally affects Shortcut sizing.

## 11.2 Controller

Controller is not part of this polish until it owns real content.

Do not redesign a placeholder merely to make all five tabs structurally identical.

## 11.3 Profile catalog

Profile catalog game buttons are already cards.

Do not wrap the entire catalog grid in another card.

The section-card rule applies to **Profile detail**, not the catalog list.

---

# 12. Selection chrome and card chrome must stay distinct

Ordinary editable rows already use `OverlayRowChrome`:

~~~text
left selection accent
transparent background
54 DIP minimum row height
~~~

Keep it.

The new section card is only a containing surface.

Required visual hierarchy:

~~~text
section card background
    -> editable row
        -> selected row left accent / selected fill
~~~

Do not make section card borders react to row selection.

Do not move row selection authority to the section.

---

# 13. Expected Profile result

The exact visuals may follow native Fluent measurement, but the hierarchy should approximate:

~~~text
┌──────────────────────────────────────────────┐
│ A Plague Tale: Requiem                 [○]  │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│ TDP Control                            [●]  │
│                                              │
│   Plugged in · PL1             [-] 35 [+]  │
│   Plugged in · PL2             [-] 45 [+]  │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│ Intel FPS Limit                       [○]   │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│ CPU Boost                              [●]  │
│                                              │
│   Plugged in              [<] Efficient [>] │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│ Windows Power Mode                     [●]  │
│                                              │
│   Plugged in          [<] Best perf. [>]    │
└──────────────────────────────────────────────┘
~~~

The important properties are:

- game title immediately distinguishable;
- each feature clearly grouped;
- child controls visually belong to their feature;
- toggles align with the right-side value-control axis;
- surface is materially narrower.

---

# 14. Files likely to change

Commit 1:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs

tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayValueRowTests.cs   only if model coverage needs a small extension
~~~

Commit 2:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs      only if helper placement/use requires it
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs         if strong-label option is implemented there

tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs    if existing source-shape assertions require update
~~~

Do not broaden production scope merely to match this list.

---

# 15. Tests — geometry

Update `OverlayWindowGeometryTests` for the 720 DIP cap.

At minimum prove:

- reference 1920×1200 / 144 DPI becomes `OverlayRect(420, 90, 1080, 1020)`;
- 96 DPI max width = 720 px;
- 120 DPI max width = 900 px;
- 144 DPI max width = 1080 px;
- 168 DPI max width = 1260 px when monitor space permits;
- 192 DPI max width = 1440 px when monitor space permits;
- surface remains horizontally centered;
- nonzero monitor origins still work;
- larger reserved edge still wins for vertical/outer margin;
- tiny/zero-sized monitors never produce negative dimensions.

Do not weaken exact geometry tests into broad range assertions.

---

# 16. Tests — shared controls

## 16.1 Toggle source/shape regression

Because the WinUI wrapper cannot be instantiated in the existing test environment, extend the current source/composition regression to prove:

- `OverlayToggleRow` sets the compact minimum-width contract;
- it remains right-aligned;
- it still uses one shared primitive for Device/Profile/ClawHUD;
- no Profile-specific toggle offset/margin was added.

Keep the pure `OverlayToggleModel` tests unchanged unless behavior truly changes.

## 16.2 Numeric RepeatButton regression

Extend source/composition coverage to prove:

~~~text
NumericStepper -> RepeatButton
DiscreteChoice -> Button
Delay = 300
Interval = 75
~~~

and that both paths still call the existing:

~~~text
_model.RequestAdjust(-1)
_model.RequestAdjust(+1)
~~~

seam.

Do not duplicate the numeric model for repeat behavior.

The existing pure test:

~~~text
RepeatedAdjustmentsContinueFromLocalPreviewWithoutReadback
~~~

must continue to pass.

Add a small pure test only if useful to prove repeated adjustments stop naturally at a boundary; do not build a fake timer test.

---

# 17. Tests — section hierarchy

Extend the existing source/composition tests to prove:

- generic Device/Profile sections are wrapped in one card each;
- section cards use `CardBackgroundFillColorDefaultBrush`;
- feature-header toggle labels use stronger typography;
- child rows still use the same generic row primitives;
- no Device-specific or Profile-specific section renderer class exists;
- Setting wraps ClawHUD once;
- Setting wraps Tab Order once;
- Shortcut does **not** receive the generic section wrapper;
- Profile catalog does **not** receive a parent section wrapper;
- ordinary row selection still uses `OverlayRowChrome`.

Do not create screenshot/golden-image tests solely for this PR.

---

# 18. Tests — Profile catalog narrow-width adaptation

Add/extend source/composition coverage proving:

- catalog still defines three star columns;
- game title content is a TextBlock rather than a raw string;
- title wraps;
- title is bounded to two lines;
- overflow trims rather than expanding the Overlay horizontally;
- existing controller selection and BringIntoView path remains.

Do not change `OverlayProfileCatalogSelection` merely for the new width.

---

# 19. Manual validation — required before merge/release

This change is visual and the WinUI row wrappers require a real XAML host, so perform a short handheld validation after automated tests.

Reference device condition:

~~~text
1920 × 1200
Windows scaling: 150%
~~~

Verify:

## Surface

- width is visibly ~25% narrower than current 960-DIP build;
- surface remains centered;
- top/bottom floating margins are unchanged;
- rounded corners/animation remain correct;
- no clipping at left/right edges.

## Tab strip

- all five current tab labels fit;
- LB/RB visual bumper hints fit;
- selected-tab indicator remains centered/aligned.

## Profile catalog

- 3 columns remain usable;
- long game names can use up to two lines;
- long titles do not force horizontal expansion;
- D-pad selection stays visible while scrolling;
- A opens selected Profile.

## Profile detail

- game title clearly separates from TDP/FPS/etc.;
- TDP, FPS, CPU Boost, Power Mode, Resolution read as separate groups;
- child rows clearly belong to their parent feature;
- selected-row accent is still obvious;
- card background does not hide selection.

## Toggle alignment

Compare a feature toggle against the `-`/`+` or `<`/`>` row below it.

The visible switch track should end on approximately the same right-side alignment axis as the value-control group.

No negative-margin visual hacks.

## Hold repeat

For a numeric stepper:

~~~text
tap +
    -> +1 step

hold +
    -> repeat begins after ~300 ms
    -> then advances rapidly at ~75 ms intervals

release
    -> stops immediately

hold at max
    -> no overflow / duplicate semantic changes
~~~

Test at least:

- one TDP numeric control;
- one Intel FPS Limit numeric control;
- one numeric ClawHUD control.

For a discrete choice such as CPU Boost or Resolution:

~~~text
hold >
    -> must NOT behave as the numeric rapid-repeat control
~~~

## Other tabs

- Device uses the same section-card/toggle alignment;
- Setting shows one ClawHUD card and one Tab Order card;
- Shortcut remains its existing tile layout without an extra parent card;
- Controller placeholder is unchanged.

---

# 20. Automated validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test -c Debug --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-restore
git diff --check
~~~

Also run the repository's normal CI before merge.

Relevant suites include:

~~~text
OverlayWindowGeometryTests
OverlayToggleRowTests
OverlayValueRowTests
OverlayDeviceRendererWiringTests
UiArchitectureTests
Overlay navigation/selection tests
Overlay Quick Settings binding tests
~~~

---

# 21. Explicit non-goals

Do not include:

- Runtime changes;
- frontend transport changes;
- Overlay protocol version changes;
- controller input routing changes;
- new D-pad repeat machinery;
- new controller capture state;
- HidHide changes;
- VIIPER changes;
- PID1901/PID1902 changes;
- Profile product-contract changes;
- Favorite behavior changes;
- Profile search;
- cover images;
- catalog source labels;
- catalog column-count changes;
- new Overlay width setting;
- user-configurable card theme;
- new navigation abstraction;
- new UI manager/service;
- a general card framework;
- animation redesign;
- vertical geometry redesign.

---

# 22. Overengineering guard

This is a presentation polish PR.

Prefer direct changes to:

~~~text
OverlayWindowGeometry
OverlayWindow.xaml
OverlayToggleRow
OverlayValueRow
existing Quick Settings section renderer
existing Setting builders
existing Profile catalog card construction
~~~

Do not add architecture merely because several controls share a visual rule.

A tiny existing-partial helper for one repeated Border construction is acceptable.

A new manager/interface/base-class/theme system is not.

Do not add state, epochs, locks, timers, or background workers.

Native `RepeatButton` is specifically preferred because it eliminates the need for custom repeat state.

---

# 23. Acceptance checklist

## Commit 1

- [ ] `MaxSurfaceWidthDip == 720.0`.
- [ ] `OpaquePanel.MaxWidth == 720`.
- [ ] Existing vertical/margin geometry is unchanged.
- [ ] ToggleSwitch compact width is fixed in the shared primitive.
- [ ] No Profile-only toggle offset exists.
- [ ] NumericStepper uses native `RepeatButton`.
- [ ] Numeric repeat uses 300 ms delay / 75 ms interval.
- [ ] DiscreteChoice remains a normal one-step Button.
- [ ] No custom repeat timer/state exists.
- [ ] Profile catalog remains three columns.
- [ ] Long game titles wrap to at most two lines.
- [ ] Existing Profile catalog navigation is unchanged.

## Commit 2

- [ ] Device logical sections render as subtle cards.
- [ ] Profile detail logical sections render as subtle cards.
- [ ] Profile game title is visually distinct from child settings.
- [ ] Feature headers use stronger typography than child rows.
- [ ] Existing generic Device/Profile renderer remains one renderer.
- [ ] ClawHUD is one Setting card.
- [ ] Tab Order is one Setting card.
- [ ] Shortcut has no new parent/double card.
- [ ] Controller placeholder is unchanged.
- [ ] Editable row selection chrome remains owned by `OverlayRowChrome`.
- [ ] No new manager/framework/authority introduced.

## Regression

- [ ] All Overlay UI tests pass.
- [ ] Full solution tests pass.
- [ ] No Runtime/transport/controller lifecycle file needed for the feature.
- [ ] Hardware validation at 1920×1200 / 150% confirms visual alignment and hold-repeat.

---

# 24. Final implementation principle

The target is not a new Overlay design system.

It is a compact refinement of the existing one:

~~~text
one Overlay surface
    -> narrower 720 DIP cap

one shared row primitive set
    -> compact toggle
    -> native numeric hold-repeat

one generic Device/Profile renderer
    -> one card per logical section
    -> stronger header / quieter child rows

Setting
    -> same section-card visual language

Shortcut
    -> keep its existing cards

controller/runtime authority
    -> unchanged
~~~

Keep the PR visually meaningful but architecturally small.
