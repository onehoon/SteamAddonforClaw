# Work Order — Main App Option Card Icon Unification (Device / Controller / Steam / XBOX / Overlay / Settings)

**Date:** 2026-10-13  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** latest `main`; review baseline `91b28a11ac02ea739673e8e88015daf2745df503`  
**Implementation shape:** one focused UI-only PR  
**Product:** standalone Full1902 MSI Claw app; no CTW integration  

---

## 0. Objective and locked user decisions

Correct mismatched or repeated option-card icons across the Main App without altering any control, feature, or controller behavior.

Three product rules are mandatory:

1. **Exclude the entire Developer menu from this task.** Do not edit `DeveloperPage.xaml`, any developer diagnostic page, or the `Developer Menu` entry card within `SettingsPage.xaml`. Its current `Help` icon remains unchanged.
2. **One feature means one icon across tabs.** For example, TDP Control must have exactly the same `LightningBolt / E945` icon in Device, Steam game profiles, and XBOX game profiles. Likewise, global and per-game M1/M2 mapping use the same `Switch / E8AB` icon, and the global Steam Virtual Controller and Steam per-game Virtual Controller use `Game / E7FC`.
3. **Include the actual Steam and XBOX per-game profile settings cards.** The main list/catalog screens and their selected-game title cards are not new icon requirements. The profile editors in `ProfilePage.xaml` and `XboxPage.xaml` are in scope.

For distinct functions displayed on the same page, avoid reusing the same header icon. Cross-tab repetition is **required** for the same functional concept, not a defect.

The user approved the 18 replacements below. Do not expand the task into a page redesign or introduce a new icon-management framework.

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

UI files to inspect:

```text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
```

Review the relevant UI contract tests, especially:

```text
tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsExpanderUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/ControllerLedUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs
```

Authoritative glyph catalog: https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

The 18 replacement glyph code points and descriptions below have been checked against Microsoft's catalog. They are not new asset dependencies.

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

These counts refer only to the **header icons** of the named option cards / expanders. Nested child cards without icons, the selected-game title card, tab/nav icons, user-defined Shortcut tile icons, and Developer diagnostics are not part of this inventory.

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

## 3. Cross-page semantic consistency (non-negotiable)

The final UI must satisfy the following canonical per-feature mapping:

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

Notes:

- `Gamebar Button` and `Steam Virtual Controller` must **not** share the same icon within Controller, although both relate to game controllers.
- `Center M Button` and `MSI Center M` are different settings and do not have to share an icon.
- `Enable HUD` in Overlay and `Resolution` in profile editors may both use `TVMonitor / E7F4` **because they are on separate pages and are different functions**. The same-function invariant matters more than global uniqueness.
- Do not add an icon to profile title/enable cards, nested child cards, or Shortcut tiles merely to make totals match.
- Do not rename existing headers or change visibility gating (including currently hidden/unsupported cards).

---

## 4. Minimal XAML implementation

Only edit the existing `SettingsCard.HeaderIcon` or `SettingsExpander.HeaderIcon` elements for the 18 named entries.

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

The existing WinUI 3 `FontIcon` default glyph font is Segoe Fluent Icons; adding `FontFamily` explicitly on converted `SymbolIcon` elements is acceptable. Existing unchanged `FontIcon` elements do **not** need formatting churn. Do not modify margins, padding, `FontSize`, foreground colors, height, typography, visual states, card hierarchy, control order, or any control bindings.

Use built-in glyphs only; no third-party icons, SVGs, images, fonts, NuGet packages, markup-extension library, shared icon resources, enums, converters, factories, mapping managers, or new wrappers.

---

## 5. Preserve all existing behavior

This is a cosmetic Main App XAML change. Explicitly preserve:

- `DevicePage` Center M actions; TDP, CPU Boost, Windows Power Mode, and battery control values and toggle behavior;
- `ControllerPage` global Steam controller selection; Gamebar/Center M key remapping; M1/M2 mapping; joystick LED and vibration behavior;
- `ProfilePage` Steam game-specific controller override, profile enable/disable, target ordering, TDP, CPU, power, GPU, FPS, and resolution bindings;
- `XboxPage` XBOX-specific M1/M2 override/global fallback and all per-game profile mutations;
- `OverlayPage` HUD/VRR settings, scaling, opacity slider, display mode, and event handlers;
- `SettingsPage` update checks, theme selection, gaming home selection, log settings, and Developer Menu navigation;
- all displayed headers, explanations, visibility/enabling conditions, card/expander expansion states, and right-aligned controls.

No runtime, frontend transport, protocol, persistence, profile serialization, Steam QAM, standalone WinUI Quick Settings Overlay window, navigation, Shortcut icon catalog, Device authority, controller native/VIIPER/HidHide lifecycle, or CTW integration changes.

No migration or new feature flags. No new background work or lifetime handling.

---

## 6. Tests and verification

### 6.1 Existing tests

Inspect existing UiTests before editing. Some tests parse XAML directly and may assert icon element types or glyph values. Keep existing behavior assertions and update only icon-specific expectations that legitimately change.

Examples:

- Steam per-game Virtual Controller must remain `FontIcon` `E7FC`; the Controller global selector must now also resolve to `E7FC`.
- If any assertion expects the old `SymbolIcon` under the changed headers, update it to inspect the new `FontIcon`.
- Tests for layout, control existence, event handlers, expander structure, update/theme behavior, and unrelated pages must remain meaningful.

### 6.2 Focused automated icon checks

Add a small, source-level XAML contract test in the existing UI test project (or extend one existing suitable test file) to cover the actual product invariant.

Suggested approach:

1. Use `XDocument` to read the six target XAML files, as the existing test project already does.
2. Find the intended `SettingsCard` or `SettingsExpander` by its exact `Header` within the corresponding page, without assuming file order.
3. Read exactly its direct `SettingsCard.HeaderIcon` or `SettingsExpander.HeaderIcon` child and the contained `FontIcon.Glyph`. Note that XML parsing converts `&#xE945;` to the character `\uE945`.
4. Verify all eight shared-feature rows in section 3 using the expected glyphs. At minimum cover the cross-page equalities for TDP, CPU Boost, Windows Power Mode, M1/M2, Steam Virtual Controller, Minimum GPU Clock, Intel FPS Limit, and Resolution.
5. Check the six changed Controller header icons and the changed Device/Overlay/Settings headers; ensure different Controller features do not share the same header glyph.
6. Optionally assert `Developer Menu` still has `SymbolIcon Help`, but do **not** touch `DeveloperPage.xaml` or turn the developer diagnostics into the subject of this new test.

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
tests/SteamInputAddonforClaw.UiTests/* (only necessary icon contract adjustments/additions)
```

Acceptance checklist:

- [ ] Exactly the 18 named option-card header icons match section 2.1.
- [ ] The other 21 in-scope icons remain unchanged.
- [ ] Same-feature global/per-game icons match section 3 across all specified pages.
- [ ] TDP and Minimum GPU Clock no longer share `E945` on either Steam or XBOX game pages.
- [ ] The Controller page no longer reuses `Contact` for two different features; all six recommended header icons are distinct.
- [ ] `DeveloperPage.xaml`, diagnostic pages, and the Settings `Developer Menu` card remain untouched.
- [ ] No other icons, including navigation, Shortcut tiles, Quick Settings Overlay controls, or Steam QAM, are changed.
- [ ] Existing card and expander layout, handlers, ordering, profile binding, and user settings behavior are preserved.
- [ ] UI source-level glyph assertions and existing relevant automated tests pass.
- [ ] No additional state, dependencies, protocol, or lifecycle changes are introduced.
- [ ] No mandatory physical/manual validation gate is imposed on local Codex or PR merge.

**Done means 18 deliberate XAML header-icon substitutions, consistent feature semantics across Device/Controller/Steam/XBOX, and appropriately updated automated UI contracts—not a UI redesign.**
