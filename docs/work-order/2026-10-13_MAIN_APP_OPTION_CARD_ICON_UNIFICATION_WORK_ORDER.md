# Work Order — Unified Main App and Overlay Icon Policy (Option Cards + Main Navigation + Vertical Rail)

**Date:** 2026-10-13  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** latest `main`; review baseline `91b28a11ac02ea739673e8e88015daf2745df503`  
**Implementation shape:** one combined, focused icon-only PR; no separate navigation/rail icon PR  
**Product:** standalone Full1902 MSI Claw app; no CTW integration  

---

## 0. Objective and locked user decisions

Unify the icons of Main App option-card headers, the Main App left NavigationView, and the standalone Overlay's five-tab vertical rail **in one implementation PR**. This is a cosmetic consistency pass only; do not alter any control, feature, navigation semantics, or controller behavior.

Four product rules are mandatory:

1. **Exclude the entire Developer menu from this task.** Do not edit `DeveloperPage.xaml`, any developer diagnostic page, or the `Developer Menu` entry card within `SettingsPage.xaml`. Its current `Help` icon remains unchanged.
2. **One feature means one icon across tabs.** For example, TDP Control must have exactly the same `LightningBolt / E945` icon in Device, Steam game profiles, and XBOX game profiles. Likewise, global and per-game M1/M2 mapping use the same `Switch / E8AB` icon, and the global Steam Virtual Controller and Steam per-game Virtual Controller use `Game / E7FC`.
3. **Include the actual Steam and XBOX per-game profile settings cards.** The main list/catalog screens and their selected-game title cards are not new icon requirements. The profile editors in `ProfilePage.xaml` and `XboxPage.xaml` are in scope.
4. **Align the Main App left navigation and Overlay's vertical rail now, in this same PR.** The same tab identity/function should use the same Fluent glyph across both surfaces when directly comparable (Device, Controller, Shortcut, Settings). The Main App Steam/XBOX game libraries are separate from the Overlay's one active-game Profile; they intentionally use different semantic icons.

For distinct functions displayed on the same page, avoid reusing the same header icon. Cross-tab repetition is **required** for the same functional concept, not a defect.

The agreed change consists of **18 option-card substitutions + six explicit Main NavigationView icon substitutions + three Overlay rail glyph substitutions**. The sixth navigation substitution (Shortcut) is a `SymbolIcon` to equivalent `FontIcon` normalization, not a new visible glyph. Do not expand into a page redesign or new icon-management framework.

---

## 1. Mandatory reference review

Before implementation, re-read the latest main and use the active document-authority precedence:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md` (including its current Steam/XBOX architecture override)
- `docs/work-order/2026-10-12_MAIN_APP_UI_SYSTEM_LIGHT_DARK_THEME_WORK_ORDER.md`
- `docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_B_CONTROLLER_STEAM_PROFILE_UI_EN_KO_GUIDES_WORK_ORDER_2026-10-11.md`
- `docs/work-order/1007_XBOX_M1_M2_UI_SEMANTICS_AND_ACTIVE_PROFILE_OVERLAY_FIX_WORK_ORDER.md`
- `docs/work-order/OVERLAY_FLUENT_RAIL_ICONS_AND_BUMPER_HINTS_WORK_ORDER_2026-10-05.md` (historical glyph values superseded by this work order only)
- `docs/work-order/OVERLAY_RAIL_BUMPER_HINT_CLUSTER_ALIGNMENT_WORK_ORDER_2026-10-05.md` (rail geometry and LB/RB hint placement remain active)
- `docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md` (update only the active glyph table in the implementation PR)

UI files to inspect:

```text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml
```

Review the relevant UI contract tests, especially:

```text
tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsExpanderUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/ControllerLedUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/MainWindowTests.cs
```

Authoritative glyph catalog: https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

The prescribed replacement glyph code points and descriptions below are standard Windows 11 Segoe Fluent Icons (not new asset dependencies). Reconfirm the selected code points against the catalog if the platform has changed.

---

## 2. Verified baseline and change budget

The six target pages currently have 39 in-scope top-level option cards with header icons, **excluding** the Settings > Developer Menu entry:

| Main App page | In-scope card count | Replace | Keep |
| --- | ---: | ---: | ---: |
| Device | 5 | 2 | 3 |
| Controller | 6 | 6 | 0 |
| Steam per-game editor (`ProfilePage.xaml`) | 7 | 2 | 5 |
| XBOX per-game editor (`XboxPage.xaml`) | 7 | 3 | 4 |
| Overlay | 8 | 3 | 5 |
| Settings (excluding Developer Menu) | 6 | 2 | 4 |
| **Total** | **39** | **18** | **21** |

These counts refer only to the **header icons** of the named option cards / expanders. Nested child cards without icons, the selected-game title card, user-defined Shortcut tile icons, and Developer diagnostics are not part of this card inventory. **Main navigation and rail are separately accounted for in sections 2.3 and 2.4.**

### 2.1 Exact 18 replacements (implementation contract)

Replace the `HeaderIcon` for **only** these entries:

| Source file | Card / expander header | Current | Required |
| --- | --- | --- | --- |
| `DevicePage.xaml` | MSI Center M | `FontIcon E7FC` (Game) | `FontIcon E71D` (AllApps) |
| `DevicePage.xaml` | Windows Power Mode | `FontIcon E83F` (Battery10) | `FontIcon E7E8` (PowerButton) |
| `ControllerPage.xaml` | Steam Virtual Controller | `SymbolIcon Contact` | `FontIcon E7FC` (Game) |
| `ControllerPage.xaml` | Gamebar Button | `SymbolIcon Contact` | `FontIcon E990` (XboxOneConsole) |
| `ControllerPage.xaml` | Center M Button | `SymbolIcon Help` | `FontIcon E8B0` (Click) |
| `ControllerPage.xaml` | M1 / M2 | `SymbolIcon Rotate` | `FontIcon E8AB` (Switch) |
| `ControllerPage.xaml` | Joystick LED | `SymbolIcon Setting` | `FontIcon E781` (LEDLight) |
| `ControllerPage.xaml` | Vibration Strength | `SymbolIcon Volume` | `FontIcon E877` (Vibrate) |
| `ProfilePage.xaml` | Windows Power Mode | `FontIcon E83F` (Battery10) | `FontIcon E7E8` (PowerButton) |
| `ProfilePage.xaml` | Minimum GPU Clock | `FontIcon E945` (LightningBolt) | `FontIcon E9D2` (AreaChart) |
| `XboxPage.xaml` | M1 / M2 Button Mapping | `FontIcon E713` (Settings) | `FontIcon E8AB` (Switch) |
| `XboxPage.xaml` | Windows Power Mode | `FontIcon E83F` (Battery10) | `FontIcon E7E8` (PowerButton) |
| `XboxPage.xaml` | Minimum GPU Clock | `FontIcon E945` (LightningBolt) | `FontIcon E9D2` (AreaChart) |
| `OverlayPage.xaml` | Enable HUD | `SymbolIcon Setting` | `FontIcon E7F4` (TVMonitor) |
| `OverlayPage.xaml` | Background width | `SymbolIcon FullScreen` | `FontIcon E745` (ResizeMouseWide) |
| `OverlayPage.xaml` | HUD opacity | `SymbolIcon FontColor` | `FontIcon E794` (Effects) |
| `SettingsPage.xaml` | Application updates | `SymbolIcon Rotate` | `FontIcon E777` (UpdateRestore) |
| `SettingsPage.xaml` | App theme | `SymbolIcon FontColor` | `FontIcon E7A1` (Contrast) |

Rationale for key choices:

- `E7FC / Game` expresses the virtual game controller and must match between the Controller global selector and the Steam per-game override. The `MSI Center M` app-management card is a **different** function and moves to `E71D / AllApps`.
- `E8AB / Switch` unifies global Controller M1/M2 mapping with XBOX per-game M1/M2 overrides.
- `E945 / LightningBolt` stays exclusively with TDP on Steam/XBOX profile pages; `Minimum GPU Clock` moves to `E9D2 / AreaChart`.
- `E83F` is **Battery10**, not an icon for Windows Power Mode; use `E7E8 / PowerButton` consistently on all three pages.
- `E781 / LEDLight` matches joystick LEDs more directly than Settings; `E877 / Vibrate` matches vibration rather than speaker volume.
- `E745 / ResizeMouseWide` conveys adjustable HUD background width; `E794 / Effects` is the chosen standard-glyph approximation for opacity. Do not introduce custom opacity assets.

### 2.2 Exact 21 existing icons to retain

**Device (3):**

| Header | Keep |
| --- | --- |
| TDP Control | `E945` LightningBolt |
| CPU Boost | `EEA1` CPU |
| Battery charge limit | `E86B` BatterySaver8 |

**Steam per-game editor (5):**

| Header | Keep |
| --- | --- |
| Virtual Controller | `E7FC` Game |
| TDP Control | `E945` LightningBolt |
| CPU Boost | `EEA1` CPU |
| Intel FPS Limit | `EC4A` SpeedHigh |
| Resolution | `E7F4` TVMonitor |

**XBOX per-game editor (4):**

| Header | Keep |
| --- | --- |
| TDP Control | `E945` LightningBolt |
| CPU Boost | `EEA1` CPU |
| Intel FPS Limit | `EC4A` SpeedHigh |
| Resolution | `E7F4` TVMonitor |

**Overlay (5):**

| Header | Keep |
| --- | --- |
| Display mode | `SymbolIcon View` |
| HUD size | `SymbolIcon FontSize` |
| Font | `SymbolIcon Font` |
| Alignment | `SymbolIcon AlignCenter` |
| Intel VRR Range Fix | `SymbolIcon Repair` |

**Settings (4):**

| Header | Keep |
| --- | --- |
| Windows Gaming Full Screen Experience | `SymbolIcon FullScreen` |
| Show only current power source | `SymbolIcon View` |
| Logging | `SymbolIcon List` |
| Required Components | `SymbolIcon Repair` |

**Developer Menu:** keep `SymbolIcon Help` in `SettingsPage.xaml` without any edit; excluded from all counts.

---


### 2.3 Main App left navigation — complete seven-item inventory

Source: `src/SteamInputAddonforClaw.UI/MainWindow.xaml`.

The explicit `NavigationView.MenuItems` currently contain seven items; the built-in Settings footer item is supplied by `NavigationView.SettingsItem`, not an explicit `NavigationViewItem` in the MenuItems collection.

| Main App entry | Existing icon | Required final icon | Work |
| --- | --- | --- | --- |
| Device (`Tag="Device"`) | `SymbolIcon AllApps` | **`FontIcon E772` Devices** | Replace |
| Controller (`Tag="Controller"`) | `SymbolIcon Repair` | **`FontIcon E7FC` Game** | Replace |
| Steam (`Tag="Profile"`) | `SymbolIcon Contact` | **`FontIcon E8F1` Library** | Replace |
| XBOX (`Tag="Xbox"`) | `SymbolIcon Play` | **`FontIcon E990` XboxOneConsole** | Replace |
| ClawHUD (`Tag="Overlay"`) | `SymbolIcon View` | **`FontIcon E7F4` TVMonitor** | Replace |
| Shortcut (`Tag="Shortcut"`) | `SymbolIcon Link` | **`FontIcon E71B` Link** | Equivalent glyph; normalize the icon declaration to match the Overlay rail |
| How to Use (`Tag="HowToUse"`) | `SymbolIcon Help` | **Unchanged: `SymbolIcon Help`** | Keep |
| Settings (built-in footer) | Framework-provided Settings gear | **Unchanged; effectively `E713` Settings** | Keep built-in footer; do not add an item |

`E8F1 / Library` is the product-appropriate Steam *game catalog* icon. `E990 / XboxOneConsole` is the XBOX catalog icon; do not use the same `Game` glyph as Controller within the Main App menu. `E7F4 / TVMonitor` ties ClawHUD's Main App navigation to its `Enable HUD` card. `How to Use` already expresses help correctly and does not need an icon change.

**Scope:** six existing `NavigationViewItem.Icon` substitutions. Keep all seven menu items, `Content`, `Tag`, `x:Name`, `SelectionChanged`, `SelectedItem`, the existing update-ready `SettingsItem.InfoBadge`, and all navigation behavior unchanged. Do not add a second Settings or Developer item.

### 2.4 Standalone Overlay vertical rail — complete five-tab inventory

Source: `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs`, existing local `GlyphFor(AddonQuickSettingsTabId)`.

| Rail tab ID | Current `GlyphFor` | Required final `GlyphFor` | Work |
| --- | --- | --- | --- |
| `Device` | `E945` LightningBolt | **`E772` Devices** | Replace |
| `Profile` | `E71D` AllApps | **`E8A5` Document** | Replace |
| `Controller` | `E7FC` Game | **`E7FC` Game** | Keep |
| `Shortcut` | `E75F` Dialpad | **`E71B` Link** | Replace |
| `Setting` | `E713` Settings | **`E713` Settings** | Keep |

The three substitutions above **supersede only** the `Device / Profile / Shortcut` glyph choices in the 2026-10-05 rail-icon and rail-alignment historical work orders. Their other contracts remain intact.

Rationale:

- `Device` is a whole-device-control tab, not a TDP-only tab; `LightningBolt / E945` remains assigned to TDP option cards.
- Overlay `Profile` is one active-game profile for either Steam or XBOX. It is **not** the Steam-only catalog, the XBOX-only catalog, or an AllApps launcher; `Document / E8A5` conveys the selected game's configuration.
- `Shortcut` must use `Link / E71B` in both Main App navigation and the Overlay rail, rather than the telephone Dialpad glyph.
- `Controller` remains `Game / E7FC`; `Setting` remains `Settings / E713`. All five rail tab icons remain distinct.

**Non-icon rail contracts remain frozen:** five typed tab identities; Runtime-authoritative reorder; clickable tab buttons; accessible labels and tooltips; button `Tag`; selected-tab background/foreground; `FontFamily = Segoe Fluent Icons`; `QamRailButtonSize = 52`; item height 64 DIP; glyph size 24 DIP; fixed passive `F10C / F10D` LB/RB bumper hints (16 DIP) and centered hint cluster; zero change to Overlay input, navigation protocol, position, geometry, or presentation lifecycle. **Do not modify `OverlayWindow.xaml` or the QAM visual-resource layout just for glyph substitutions.**

### 2.5 Consolidated change budget

| Area | Entries inventoried | Deliberate substitutions | Preserved |
| --- | ---: | ---: | ---: |
| Main App option-card headers (Developer excluded) | 39 | 18 | 21 |
| Main App explicit left navigation | 7 | 6 | 1 |
| Main App built-in Settings footer | 1 | 0 | 1 |
| Overlay vertical rail tabs | 5 | 3 | 2 |
| **Total** | **52** | **27** | **25** |

Of 27 source substitutions, the Main App Shortcut changes from `SymbolIcon Link` to `FontIcon E71B` but retains the same **semantic glyph**; **26 visual glyph changes** are expected. Keep the 2 LB/RB bumper hints intact and exclude them from the totals.

## 3. Cross-page semantic consistency (non-negotiable)

The final **option-card** UI must satisfy the following canonical per-feature mapping:

| Functional feature | Device | Controller | Steam game | XBOX game | Canonical glyph |
| --- | --- | --- | --- | --- | --- |
| Steam Virtual Controller / Virtual Controller override | — | Yes | Yes | — | `E7FC` Game |
| Global / per-game M1/M2 mapping | — | Yes | — | Yes | `E8AB` Switch |
| TDP Control | Yes | — | Yes | Yes | `E945` LightningBolt |
| CPU Boost | Yes | — | Yes | Yes | `EEA1` CPU |
| Windows Power Mode | Yes | — | Yes | Yes | `E7E8` PowerButton |
| Minimum GPU Clock | — | — | Yes | Yes | `E9D2` AreaChart |
| Intel FPS Limit | — | — | Yes | Yes | `EC4A` SpeedHigh |
| Resolution | — | — | Yes | Yes | `E7F4` TVMonitor |

### 3.1 Main navigation ↔ Overlay rail equality and intentional distinctions

| Surface / identity | Main App left nav | Overlay rail | Rule |
| --- | --- | --- | --- |
| Device | `E772` Devices | `E772` Devices | **Exact match** |
| Controller | `E7FC` Game | `E7FC` Game | **Exact match** |
| Shortcut | `E71B` Link | `E71B` Link | **Exact match** |
| Settings / Setting | Built-in Settings `E713` | `E713` Settings | **Same stock gear semantic; no new Main App item** |
| Steam / XBOX vs Profile | Steam `E8F1` Library; XBOX `E990` XboxOneConsole | unified active Profile `E8A5` Document | **Different function: do not force same glyph** |
| ClawHUD | `E7F4` TVMonitor | No equivalent rail tab | Main App matches its own `Enable HUD` card |
| How to Use | Existing `SymbolIcon Help` | No equivalent rail tab | Keep |

**Icon identity rule:** match exactly for the same page/feature identity; do not equate unrelated cards/menus just because they both reference the controller, a game, a document, or a monitor. Avoid duplicate glyphs between distinct entries within each single navigation surface.

Notes:

- `Gamebar Button` and `Steam Virtual Controller` must **not** share the same icon within Controller, although both relate to game controllers.
- `Center M Button` and `MSI Center M` are different settings and do not have to share an icon.
- `Enable HUD` in Overlay and `Resolution` in profile editors may both use `TVMonitor / E7F4` **because they are on separate pages and are different functions**. The same-function invariant matters more than global uniqueness.
- Do not add an icon to profile title/enable cards, nested child cards, or Shortcut tiles merely to make totals match.
- Do not rename existing headers or change visibility gating (including currently hidden/unsupported cards).

---

## 4. Minimal icon-only implementation

For the option cards, edit only the existing `SettingsCard.HeaderIcon` or `SettingsExpander.HeaderIcon` elements for the 18 named entries.

For a current `SymbolIcon` that must become a Fluent font glyph:

```xml
<ctcontrols:SettingsExpander.HeaderIcon>
    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE877;" />
</ctcontrols:SettingsExpander.HeaderIcon>
```

For an existing `FontIcon`, change only `Glyph`, e.g.:

```xml
<ctcontrols:SettingsExpander.HeaderIcon>
    <FontIcon Glyph="&#xE9D2;" />
</ctcontrols:SettingsExpander.HeaderIcon>
```

For the Main App `NavigationViewItem.Icon` entries, use the existing slot, e.g.:

```xml
<NavigationViewItem.Icon>
    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE772;" />
</NavigationViewItem.Icon>
```

Make six direct icon substitutions in `MainWindow.xaml`; leave `How to Use` and the built-in Settings footer alone. Do not alter any `NavigationViewItem` identity, its `Tag`, its selected state, or code-behind.

For the Overlay rail, change **only three return strings** inside the existing `GlyphFor` switch in `OverlayWindow.Shell.cs`:

```csharp
private static string GlyphFor(AddonQuickSettingsTabId id) => id switch
{
    AddonQuickSettingsTabId.Device => "\uE772",     // Devices
    AddonQuickSettingsTabId.Profile => "\uE8A5",    // Document
    AddonQuickSettingsTabId.Controller => "\uE7FC", // Game, unchanged
    AddonQuickSettingsTabId.Shortcut => "\uE71B",   // Link
    AddonQuickSettingsTabId.Setting => "\uE713",    // Settings, unchanged
    _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Overlay tab identity."),
};
```

The existing WinUI 3 `FontIcon` default glyph font is Segoe Fluent Icons; adding `FontFamily` explicitly on converted `SymbolIcon` elements is acceptable. Existing unchanged `FontIcon` elements do **not** need formatting churn. Do not modify margins, padding, `FontSize`, foreground colors, height, typography, visual states, card hierarchy, control order, or any control bindings.

Use built-in glyphs only; no third-party icons, SVGs, images, fonts, NuGet packages, markup-extension library, shared icon resources, enums, converters, factories, mapping managers, or new wrappers.

---

## 5. Preserve all existing behavior

This is a cosmetic icon-only change to the Main App XAML and one existing Overlay local glyph switch. Explicitly preserve:

- `DevicePage` Center M actions; TDP, CPU Boost, Windows Power Mode, and battery control values and toggle behavior;
- `ControllerPage` global Steam controller selection; Gamebar/Center M key remapping; M1/M2 mapping; joystick LED and vibration behavior;
- `ProfilePage` Steam game-specific controller override, profile enable/disable, target ordering, TDP, CPU, power, GPU, FPS, and resolution bindings;
- `XboxPage` XBOX-specific M1/M2 override/global fallback and all per-game profile mutations;
- `OverlayPage` HUD/VRR settings, scaling, opacity slider, display mode, and event handlers;
- `SettingsPage` update checks, theme selection, gaming home selection, log settings, and Developer Menu navigation;
- all displayed headers, explanations, visibility/enabling conditions, card/expander expansion states, and right-aligned controls;
- Main App NavigationView selection, default page, built-in Settings footer, update-ready dot badge and icon, menu visibility, and page activation;
- Overlay rail tab identity, Runtime-owned order/reorder behavior, click and LB/RB navigation, hint glyphs, geometry, selected state, and all other Overlay rendering and behavior.

No runtime, frontend transport, protocol, persistence, profile serialization, Steam QAM, Overlay functionality/window behavior beyond three icon glyph values, Shortcut **tile** icon catalog, Device authority, controller native/VIIPER/HidHide lifecycle, or CTW integration changes.

No migration or new feature flags. No new background work or lifetime handling.

---

## 6. Tests and verification

### 6.1 Existing tests

Inspect existing UiTests before editing. Some tests parse XAML directly and may assert icon element types or glyph values. Keep existing behavior assertions and update only icon-specific expectations that legitimately change.

Examples:

- Steam per-game Virtual Controller must remain `FontIcon` `E7FC`; the Controller global selector must now also resolve to `E7FC`.
- If any assertion expects the old `SymbolIcon` under the changed headers, update it to inspect the new `FontIcon`.
- Update the existing source assertions in `OverlayDeviceRendererWiringTests.cs` for the three changed `GlyphFor` cases; keep unchanged Controller/Setting rail assertions and all visual/navigation behavior assertions.
- Preserve the existing Main App navigation, Settings footer, and update badge assertions in `MainWindowTests.cs`/`UiArchitectureTests.cs`.
- Tests for layout, control existence, event handlers, expander structure, update/theme behavior, and unrelated pages must remain meaningful.

### 6.2 Focused automated icon checks

Add a small, source-level XAML contract test in the existing UI test project (or extend one existing suitable test file) to cover the actual product invariant.

Suggested approach:

1. Use `XDocument` to read the six target XAML files, as the existing test project already does.
2. Find the intended `SettingsCard` or `SettingsExpander` by its exact `Header` within the corresponding page, without assuming file order.
3. Read exactly its direct `SettingsCard.HeaderIcon` or `SettingsExpander.HeaderIcon` child and the contained `FontIcon.Glyph`. Note that XML parsing converts `&#xE945;` to the character `\uE945`.
4. Verify all eight shared-feature rows in section 3 using the expected glyphs. At minimum cover the cross-page equalities for TDP, CPU Boost, Windows Power Mode, M1/M2, Steam Virtual Controller, Minimum GPU Clock, Intel FPS Limit, and Resolution.
5. Check the six changed Controller header icons and the changed Device/Overlay/Settings headers; ensure different Controller features do not share the same header glyph.
6. Parse `MainWindow.xaml` and check the exact six updated explicit menu icons plus unchanged `How to Use` and its tags. Check Device, Controller, Shortcut equality against the rail's authoritative glyph mapping; document that Steam/XBOX are distinct from the unified Overlay Profile, and verify all five final rail values in the existing source-contract test. Do not add an explicit Settings nav item.
7. Optionally assert `Developer Menu` still has `SymbolIcon Help`, but do **not** touch `DeveloperPage.xaml` or turn the developer diagnostics into the subject of this new test.

Keep the test simple; no UI automation infrastructure, screenshot comparison, new icon registry, generic XAML inspector, or production dependency is necessary.

### 6.3 Local automated commands

Use the project's applicable existing build/test workflow. On a prepared Windows repository with restored dependencies, expected commands include:

```powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
```

Run the broader test suite where normally required by CI, and report executed results in the PR summary.

**Physical hardware/manual validation is the user's role after merge. It is NOT required from local Codex and is NOT a PR review or merge blocker.** Do not demand an MSI Claw device, actual input/rumble checks, or manual UI screenshots to finish this icon-only PR.

---

## 7. Implementation scope and acceptance criteria

A single PR should normally touch only:

```text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
tests/SteamInputAddonforClaw.UiTests/* (only necessary icon contract adjustments/additions)
docs/overlayui/QAM_VISUAL_REFERENCE_2026-10-02.md (update the current rail icon mapping only)
```

Acceptance checklist:

- [ ] Exactly the 18 named option-card header icons match section 2.1; the other 21 in-scope icons remain unchanged.
- [ ] Main App's six explicit nav substitutions match section 2.3; `How to Use` remains `SymbolIcon Help`.
- [ ] The built-in Settings footer item remains built-in, with existing update-ready badge and navigation behavior.
- [ ] Overlay rail's three changed and two retained glyphs match section 2.4 exactly; LB/RB F10C/F10D hints remain unchanged.
- [ ] Device/Controller/Shortcut navigation glyphs match exactly between the Main App and Overlay rail; the distinct Steam/XBOX/active Profile identity is respected.
- [ ] Same-feature global/per-game icons match section 3 across all specified pages.
- [ ] TDP and Minimum GPU Clock no longer share `E945` on either Steam or XBOX game pages.
- [ ] The Controller page no longer reuses `Contact` for two different features; all six recommended header icons are distinct.
- [ ] `DeveloperPage.xaml`, diagnostic pages, and the Settings `Developer Menu` card remain untouched.
- [ ] No other icons, including unrelated Main App icons, Shortcut **tiles**, Overlay content controls, or Steam QAM, are changed.
- [ ] Existing card and expander layout, handlers, ordering, profile binding, and user settings behavior are preserved.
- [ ] UI source-level glyph assertions and existing relevant automated tests pass.
- [ ] No additional state, dependencies, protocol, or lifecycle changes are introduced.
- [ ] No mandatory physical/manual validation gate is imposed on local Codex or PR merge.

**Done means one PR with 18 option-card icon substitutions, six Main App left-navigation icon substitutions, three Overlay rail glyph substitutions, and focused automated UI assertions and active rail glyph documentation—not a UI redesign.**
