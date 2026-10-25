# Work Order — Edge-to-Edge Quick Overlay, L-Shaped Rail, Clock, and Y-Target Footer

**Date:** 2026-10-25  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation base:** `main`, reviewed at `cdc90fe67e4a0f7fd3d595094cff69893a5197f9`  
**Feature:** Standalone WinUI 3 Quick Overlay presentation  
**Delivery:** One focused implementation PR  
**Hardware validation owner:** User, after merge (not an implementation/PR blocker)

---

## 0. Goal and exact decisions

Implement the first, **Overlay-only** visual/layout update. The final decisions below override older inset, footer, and rounded-corner suggestions.

1. Remove the external **top, bottom, and right** Overlay offsets. Keep the **existing left X coordinate** exactly; the window becomes wider by filling the former right inset.
2. Make the Overlay touch the target monitor's **top, bottom, and right physical edges**, using the monitor rectangle, not the work area.
3. **Remove all outer rounded window corners.** The Overlay is a straight-edged rectangular panel.
4. Introduce a bottom horizontal footer rail that extends **across the full Overlay width, including beneath the left 52-DIP vertical rail**. The visible vertical rail ends above this footer: an L-shaped two-part layout.
5. Do **not** visually distinguish the left rail, footer rail, and content background. **Preserve their current identical color and do not add borders, separators, gradients, or new shades.** The L shape is structural only.
6. The existing **LB hint + five real tab icons + RB hint** must remain at their **exact previous vertical screen-centered positions** for a 1200px-high monitor. Shrinking the vertical rail to make room for the footer must **not** move the cluster upward. Preserve icon dimensions, spacing, and tab behavior.
7. Add a small, horizontally centered, passive **24-hour local clock** (`23:22`) near the top of the 52-DIP left rail.
8. At the bottom, display **only** the real Xbox/controller **Y-button icon** immediately followed by the current configured tab name, for example: `[ButtonY glyph] Profile`. No label, colon, instructional sentence, or extra LB/RB footer hints.
9. Reuse the existing Runtime-owned Y Quick Jump settings state. No new persistent settings, authority, mapping semantics, process, or IPC.

**Out of scope:** moving or animating ClawHUD, ClawHUD alignment overrides, controller ownership/routing, new input features, or redesigning interior cards/tabs.

---

## 1. Required source and policy review

Recheck current `main` before edits. Historical UI design documents may have obsolete placement/rail descriptions; this work order supersedes those visual sections.

### Architecture/contract

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md`
- `docs/work-order/OVERLAY_RAIL_BUMPER_HINT_CLUSTER_ALIGNMENT_WORK_ORDER_2026-10-05.md`

This is an isolated frontend presentation change. The primary Full1902 Runtime remains the controller, routing, overlay visibility/capture, and settings authority. The Overlay must not gain any such ownership. Ignore legacy CTW integration descriptions; this is a standalone application.

### Implementation and tests

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs`
- `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
- `src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayQamVisualResourcesTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs` if affected

---

## 2. Verified baseline and consequences

Current `OverlayWindowGeometry`:

- `OuterEdgeInsetPx = 48` (physical px).
- `MaxSurfaceWidthDip = 452.0`.
- Right and top/bottom are inset by 48 physical pixels on the reference display.
- At 1920 × 1200, 150% (144 DPI), `Calculate(0,0,1920,1200,144)` returns **`OverlayRect(1194,48,678,1104)`**.

Current XAML:

- `OpaquePanel MaxWidth="452"` constrains internal surface width.
- `QamShell` has two columns: left rail 52 DIP and content `*`, no footer.
- `TabRailCluster` is `VerticalAlignment="Center"` and contains passive `PreviousTabBumperHint`, reorderable `TabStrip`, passive `NextTabBumperHint`.
- Existing `QamRailCornerRadius` is already **0**.
- `QamSurfaceBrush`, `QamRailBrush`, and `QamContentBrush` are **all `#FF0E141B`**. **Do not change these.**
- `OverlayWindow.Presentation.OnSurfaceHostSizeChanged` sets `OpaquePanel.Width` from available host width.
- `WindowInterop.Configure` applies `ApplyRoundedCorners(hwnd)`, currently setting `DWMWCP_ROUND = 2`. This must change even though the XAML rail corner radius is already zero.
- `ApplyFrontendSettingsState` in `OverlayWindow.Controller.cs` assigns `_frontendSettingsSnapshot` and `_frontendSettingsAvailable` and calls `ApplyQuickSettingsYButtonQuickJumpTarget`.
- `OverlayWindow.ClawHud.cs` has `FormatYButtonQuickJumpChoice` mapping all six target values and handles pending mutations.
- `OverlayWindow.Presentation.ShowForPocAsync` / `HideForPocAsync` already own the visual visibility lifecycle.

The current XAML wire/layout tests depend on the old two-column-only tree, `MaxWidth="452"`, and rounded DWM request. Adjust **the affected assertions** rather than attempting to retain obsolete layout solely for tests.

---

## 3. Exact outer window geometry

**Reference result (required):**

```text
Monitor (physical px):  0,0 -> 1920,1200
Scale:                  150% / 144 DPI
Before:                 X=1194, Y=48, Width=678, Height=1104
After:                  X=1194, Y=0,  Width=726, Height=1200
Left edge:              unchanged
Right edge:             1920 exactly
Top/bottom:             0 / 1200 exactly
```

Do **not** simply change `OuterEdgeInsetPx` to zero and retain the old 452-DIP width cap: that would move X rightward by 48px, violating the requirement.

### Recommended minimal calculation

Keep the existing **old left-edge selection policy** (old capped width and former right inset), then derive the new rectangle from that fixed X to the monitor's right edge:

```csharp
// Use the prior X-selection logic only to preserve the old left edge.
var oldRightInset = Math.Min(OuterEdgeInsetPx, monitorWidth);
var oldRight = monitorRight - oldRightInset;
var oldAvailableWidth = Math.Max(0, oldRight - monitorLeft);
var oldWidth = Math.Min(oldAvailableWidth, DipToPixels(MaxSurfaceWidthDip, effectiveDpi));
var x = oldRight - oldWidth;

// The new window fills all space from that X to the actual monitor right edge.
return new OverlayRect(x, monitorTop, monitorRight - x, monitorHeight);
```

Keep the existing zero-sized-monitor guard. Retain recognizable constants if they remain useful for the **legacy left-edge calculation**, but update naming/comments so `OuterEdgeInsetPx` is not misrepresented as a currently visible outer inset.

This preserves the prior left edge for all DPI values/monitor positions, including cases where the width was previously clamped. Do not introduce a second placement authority, a DPI manager, or manual pixel hacks in XAML.

Reference expectations on a 1920×1200 monitor:

| DPI | Left X | New width | Top | Height |
| --- | ---: | ---: | ---: | ---: |
| 96 | 1420 | 500 | 0 | 1200 |
| 120 | 1307 | 613 | 0 | 1200 |
| 144 | 1194 | 726 | 0 | 1200 |
| 168 | 1081 | 839 | 0 | 1200 |
| 192 | 968 | 952 | 0 | 1200 |

Nonzero monitor origin case: `Calculate(100,40,2020,1240,144)` -> `(1294,40,726,1200)`. For small monitors, clamp without negative dimensions or overflow; the **right/top/bottom edges remain the monitor edges**.

### XAML width constraint

Remove/update `OpaquePanel.MaxWidth="452"`. The parent `SurfaceHost` already supplies the actual new window width, and `OnSurfaceHostSizeChanged` applies it to the panel; ensure no child width cap leaves an unused 48px band or shifts the content. The screen-right flush invariant applies to the **visible surface**, not just the HWND bounding rectangle.

Keep multi-DPI rendering through the existing WinUI 3 `XamlRoot`/DPI infrastructure.

---

## 4. Full-width L-shaped footer and unchanged rail geometry

The desired **layout tree** is:

```text
QamShell (two rows)
├─ Main area: Star height
│  ├─ Left:  TabRail (52 DIP wide)
│  │  ├─ passive top clock "23:22"
│  │  └─ LB + five tabs + RB [same screen-center position as before]
│  └─ Right: existing PageTitle + BodyScroll + TabBody
└─ Footer: 36 DIP high; Grid.ColumnSpan=2 (FULL overlay width)
   └─ [controller ButtonY icon] [current configured target name]
```

The vertical rail **ends at the top edge** of the footer; the footer occupies the entire width below it. Do not add a left-side blank filler or restrict the footer to the content column.

**Background and edges (non-negotiable):**

```text
QamSurfaceBrush = #FF0E141B    KEEP
QamRailBrush    = #FF0E141B    KEEP
QamContentBrush = #FF0E141B    KEEP
```

Use the existing rail brush on the full-width footer and the existing brushes elsewhere; no color changes, contrast changes, divider line, border, shadow, gradient, or segmentation. The L shape is a **structural grid layout**, not a colored accent.

Keep the existing 52 DIP rail width, five-tab order/identity, LB/RB passive hint glyphs, sizing, tab selection/reordering, navigation/capture, and all body pages. Do not resurrect a large instructions footer.

### Keep the icon cluster at the original physical screen Y

**Critical regression risk:** simply centering `TabRailCluster` in the shortened main row shifts it upwards by half the footer height.

Reference case:

```text
Monitor height                           = 1200 physical px
Footer 36 DIP at 150%                    =   54 physical px
Main area height                         = 1146 physical px
Uncorrected main-area center             =  573 physical px   WRONG
Required original screen-centered point =  600 physical px
Shift required                            =  +27 physical px
                                          =  +18 DIP at 150%
```

Apply the equivalent of **`FooterHeight / 2` downward** to the centered LB + five tabs + RB group, with no changes to spacing between members. With a fixed 36-DIP footer, a simple 18-DIP `TranslateTransform.Y` (or other equivalent straightforward layout adjustment) is sufficient and scales correctly with DPI. Avoid introducing general layout managers or a separate geometry state system.

The clock is **outside** `TabRailCluster` and must not participate in its vertical measurement/centering. It must not force tabs downward or clip them.

If using a different footer height, ensure the offset is exactly half that height; ideally keep one local dimension contract. Preserve the full monitor-centered vertical location after show, DPI changes/reconfiguration, and normal UI updates.

---

## 5. Small 24-hour clock at top of vertical rail

Add a passive `TextBlock` near the **top** of the 52-DIP `TabRail` (suggested 12 DIP top inset; 12–13 DIP font, horizontally centered). Display **only** local time as `HH:mm`, for example `23:22`; no seconds, AM/PM, or accompanying label.

Example formatting:

```csharp
DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
```

One tiny Window-owned UI dispatcher timer is enough; no service, worker, IPC, or app-wide time authority. Required behavior:

- Set the clock text immediately when the Overlay **shows**, including a warm-process reopen.
- Update while visible (e.g. every 15–30 seconds; accuracy should be sufficient for a minute display).
- Stop updates when the Overlay **hides or closes**. Do not keep a hidden periodic UI workload.
- After sleep/resume, the next tick/reopen reads the real local wall clock; do not increment a cached counter.
- Make it noninteractive (`IsHitTestVisible="False"`), with no focus/tab navigation side effects.
- Use existing text brushes/typefaces; prevent clipping or wrapping within 52 DIP.

Place timing lifecycle hooks on `ShowForPocAsync`, `HideForPocAsync`, and `Closed` (as appropriate), which already own the actual window visibility. Keep failure behavior ordinary and nonfatal; a temporary clock update issue must not disrupt Overlay/controller ownership.

---

## 6. Bottom Y icon + configured tab name only

The footer is a **status display**, not a new command, button, setting, or input capture target.

### Icon

Use the real **Segoe Fluent Icons `ButtonY`** glyph:

```xml
<FontIcon FontFamily="Segoe Fluent Icons"
          Glyph="&#xF095;"
          FontSize="20"
          IsHitTestVisible="False" />
```

`F095` is the documented `ButtonY` pictogram (not the plain text character `Y`). Existing `F10C`/`F10D` LB/RB hint pictograms should remain unchanged.

Reference: Microsoft Segoe Fluent Icons icon list, https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font (PUA F000–F5FF / ButtonY).

Position the glyph and one short text label beside each other in the bottom bar; use compact horizontal padding/spacing. Maintain legibility on the 52-DIP left edge without adding large control chrome. Suggested text font size 14 DIP.

### Label contract

Use the existing `FormatYButtonQuickJumpChoice` or equivalent shared mapping. The only allowed visible strings are:

| `QuickOverlayYButtonTarget` | Footer text |
| --- | --- |
| Profile | `Profile` |
| Shortcut | `Shortcut` |
| Device | `Device` |
| Controller | `Controller` |
| Setting | `Setting` |
| Off | `Off` |
| Snapshot unavailable / invalid | `—` |

Examples of forbidden copy: `Y Button:`, `Quick Jump:`, `Assigned to:`, explanatory sentences, colon, parentheses, or a second footer hint.

Read the **authoritative** `_frontendSettingsSnapshot.YButtonQuickJumpTarget` guarded by `_frontendSettingsAvailable`. Do **not** use `_pendingYButtonQuickJumpTarget` to advertise an uncommitted mapping as already configured. The Setting-page editor may keep its existing optimistic pending display; the footer represents the **confirmed current mapping**.

Wire the footer refresh into `ApplyFrontendSettingsState` (which receives the Runtime snapshot and mutation completion) and/or existing `ApplyQuickSettingsYButtonQuickJumpTarget` after authoritative state is applied. It must update if the mapping changes while the Overlay is open, and on later shows. A mutation failure must not leave the footer at a rejected value. `Off` must remain explicit, not be confused with unavailable state.

No new settings field, second cached settings store, IPC message, enum, mapping authority, or input handler.

---

## 7. Remove outer rounded window corners

`WindowInterop.Configure` currently invokes:

```csharp
ApplyRoundedCorners(hwnd);
```

Its existing `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND=2)` explicitly requests rounded corners and must be replaced with **`DWMWCP_DONOTROUND=1`**.

Implement the minimal change, e.g.:

```csharp
private const int DwmcpDoNotRound = 1;

private static void ApplySquareCorners(nint hwnd)
{
    var preference = DwmcpDoNotRound;
    var result = DwmSetWindowAttribute(
        hwnd, DwmwaWindowCornerPreference,
        ref preference, (uint)Marshal.SizeOf<int>());
    // Keep an unsuccessful DWM preference best-effort and nonfatal;
    // update diagnostic wording accordingly.
}
```

Call this in the same Configure location. Rename/delete the old rounded-corner helper/constants/stale warning variable/messages and update corresponding tests. Do not modify unrelated DWM behavior, native show/hide/topmost order, outside-click dismissal, or overlay activation policy.

`QamRailCornerRadius` already equals `0`. It does **not** need a redundant visual overhaul. Outer corners are square; interior card-specific corner radii are **out of scope**.

---

## 8. Show/hide animation and user interaction policy

Maintain existing `AnimatedContent` composition animation, topmost/no-activate show/hide mechanism, and outside-click/Back dismissal behavior unless a concrete layout issue requires a minimal adjustment.

When fully visible, the surface must cover the requested top/right/bottom edges with no unpainted strip. Temporary movement/fade **during** the existing reveal animation is not grounds for a new animation framework. Do not change input routing, navigation model, active-page reset, or Runtime capture semantics.

The clock and footer are noninteractive. LB/RB remain passive hints for existing tab navigation. Y footer reports the existing Quick Jump mapping; it must not register or consume a second Y handler.

---

## 9. Required automated regression tests

Update and extend the existing suite, rather than creating a separate UI test framework.

### Geometry

Revise `OverlayWindowGeometryTests.cs` to assert:

1. Exact new reference result: `OverlayRect(1194, 0, 726, 1200)`.
2. The DPI matrix in section 3; X stays at the old location, right edge equals monitor right, top/bottom flush.
3. Nonzero and negative monitor origins where applicable.
4. Very small monitor and zero-sized monitor are safe: no negative dimensions, no values outside monitor bounds.
5. Previous “physical inset” assertions are removed/replaced by the new contract.

### Layout/visual wiring

Revise `OverlayDeviceRendererWiringTests.cs` and affected resource tests to check:

1. A main row plus **full-width spanning** footer below the main row.
2. Vertical rail stays **52 DIP** and terminates above footer.
3. Footer uses `QamRailBrush` and all original relevant brush values remain unchanged at `#FF0E141B`.
4. `TabRailCluster` still contains **exactly** LB hint, existing `TabStrip`, RB hint in order; icons, behavior, and spacing unchanged.
5. Center offset equals **half footer height**, without positioning the clock in the centered cluster.
6. Clock exists in the upper rail, has 24-hour display contract, and is passive.
7. Footer `FontIcon` uses Segoe Fluent Icons `F095`; there is one target-name text label and **no extra instructional copy**.
8. Old `MaxWidth="452"` cap is not present in a form that cuts off the newly widened surface.
9. WindowInterop requests `DWMWCP_DONOTROUND`, not `DWMWCP_ROUND`. Existing nonfatal DWM failure policy stays.
10. Where existing tests parse a direct `TabRail` child `StackPanel`, adapt them to the new clock/centering container without weakening the three-item cluster invariant.

### Settings projection/visibility

Add focused source/logic tests where practical:

- Runtime snapshot for each target produces the exact short label.
- `Off` is rendered as `Off`; unavailable/invalid data does not masquerade as `Off`.
- Pending Y mutation does not falsely change the **confirmed** footer label.
- A successful authoritative settings update refreshes the footer; a failure leaves/returns the confirmed value.
- Clock refresh starts with show, stops on hide/close, and refreshes on reopen.

Use existing patterns (including source/wiring checks where WinUI cannot instantiate in unit tests). Do not add production interfaces, artificial race tests, or test-only synchronization just to make assertions possible.

Run the repository's relevant build and existing automated tests available to local Codex, and report their actual results in the PR description. **Physical hardware/on-device visual verification is the user's post-merge responsibility and must not be a completion requirement or review blocker.**

---

## 10. Scope limits and acceptance criteria

### Required

- At 1920×1200 / 150%: final HWND/surface `(1194,0,726,1200)`; square corners.
- On other supported DPI scales: old X preserved, top/right/bottom touch physical monitor edges.
- Existing background colors unchanged; neither the vertical nor horizontal rail is visually distinguished from content.
- Bottom 36-DIP bar spans both columns; vertical rail is shortened above it.
- LB + tabs + RB cluster stays centered at **absolute monitor Y=600px** in reference configuration, rather than moving to Y=573px.
- Small `HH:mm` 24-hour clock in top rail; visible-only updates.
- Bottom status is **real Y-button pictogram + current authoritative tab name** and nothing else.
- Original tab ordering, mouse/gamepad navigation, button semantics, settings mutation flow, window lifecycle, and Full1902 safety remain intact.
- Targeted tests compile/pass; existing related tests updated.

### Explicitly not required

- ClawHUD relocation/animation or control protocol changes.
- UI recoloring or separator design.
- Additional settings persistence or Runtime messages.
- New panel/window management abstractions.
- Tests for pathological instruction-level timing interleavings.
- Hardware access or physical testing by local Codex before merge.

### Handoff

Implement on a normal feature branch and open **one PR**. Include a concise change summary, automated test results, and note that **user-performed physical/UI validation follows merge**. Avoid unrequested cleanup or speculative lifecycle complexity. The Overlay remains a disposable presentation process under the existing elevated Full1902 Runtime authority.
