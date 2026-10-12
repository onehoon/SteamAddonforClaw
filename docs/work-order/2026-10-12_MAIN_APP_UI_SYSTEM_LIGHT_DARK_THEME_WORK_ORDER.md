# Work Order — Basic Main App UI Theme (System / Light / Dark)

**Date:** 2026-10-12  
**Status:** Ready for local Codex implementation  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** latest `main` (re-read before coding)  
**Implementation:** one focused PR  
**Product:** Standalone Full1902; one Windows user / one interactive session

---

## 0. Objective

Add only the native WinUI 3 **System / Light / Dark** appearance choice to the **Main App UI**.

Place one new **SettingsCard** directly below the existing **Application updates** card on the Settings page. The card contains a compact ComboBox and an appropriate built-in Fluent icon. Apply a selection immediately without requiring a Main UI or Runtime restart, and restore the saved selection when the Main UI is opened again.

Do not create an accent-color selector, custom theme, theme framework, custom Fluent control templates, or a separate Appearance page.

**Intentional exception:** The **How to Use document area stays white/light** for readability in all three modes. Its surrounding WinUI navigation, loading/error UI, and other normal application surfaces follow the selected Main UI theme.

**Scope boundary:** The standalone WinUI Quick Settings Overlay is a **different process/project** and is not changed. Neither Steam QAM nor Full1902 Runtime/controller behavior is part of this work.

## 1. Mandatory reference review

Read the current code on `main` before implementing:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md` (apply its current overrides)
- `src/SteamInputAddonforClaw.UI/App.xaml`
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs`
- `tests/SteamInputAddonforClaw.UiTests/HowToUseMarkdownRendererTests.cs`
- `src/SteamInputAddonforClaw/Install/AddonDataPaths.cs` (read the canonical data-root convention; do **not** reference the Runtime assembly from UI)
- `docs/work-order/1008_HOW_TO_USE_WEBVIEW2_LAZY_INITIALIZATION_AND_RENDER_COMPLETION_FIX_WORK_ORDER.md` (preserve WebView2 load/navigation reliability)

This is a **frontend-only appearance feature**. The Main UI is disposable; Full1902 Runtime controller ownership, HidHide/VIIPER, physical PID1901/PID1902, routing, Center M, suspend/resume, shutdown/restart, and recovery must not change. No CTW integration.

## 2. Verified current implementation (2026-10-12)

1. The Main App is an unpackaged WinUI 3 project: `src/SteamInputAddonforClaw.UI`, with `Microsoft.WindowsAppSDK` 2.5.1 and `CommunityToolkit.WinUI.Controls.SettingsControls` 8.2.250402.
2. `App.xaml` currently only registers `XamlControlsResources`; there is no existing Main App theme preference or custom palette.
3. `MainWindow.xaml` uses `<MicaBackdrop />`. Its top-level content is an unnamed `Grid` containing **both** the `NavigationView` and the prerequisite busy overlay. Theme requests should cover this entire Main UI tree.
4. `MainWindow.xaml.cs` constructs the content via `InitializeComponent()`, then initializes `SettingsContent` using the existing frontend bootstrap.
5. `SettingsPage.xaml` currently places `UpdateCard` first, followed immediately by `GamingHomeExpander`. Insert the new card **between these two existing controls**. Keep the remaining cards/order unchanged.
6. `SettingsPage.xaml.cs` already guards programmatic ComboBox state changes (e.g. logging/Gaming Home). Follow that simple pattern so initial selection does not execute a user-change handler.
7. `HowToUsePage` lazily creates WebView2 and uses `HowToUseMarkdownRenderer.BuildHtml()`. The renderer currently emits `html { color-scheme: light dark; }` and an `@media (prefers-color-scheme: dark)` branch. This conflicts with the new deliberate **always-light document** policy.
8. `HowToUseMarkdownRendererTests.App_html_shell_embeds_rendered_body_and_uses_local_light_dark_css` currently **expects** the old dark-mode CSS branch. Update that test in this PR.
9. `AddonDataPaths` locates the canonical persistent `SteamInputAddonforClaw-Data` folder as a **sibling of the installed application root**, not necessarily under `%LOCALAPPDATA%`. The Main UI already receives `bootstrap.LogDirectoryPath`; avoid silently inventing a second, incompatible `%LOCALAPPDATA%` data root.

## 3. Exact Settings card UX

New order:

```text
Settings

[Application updates]                  [Check / Install update]
[App theme                         🎨]  [System             v]
[Windows Gaming Full Screen Experience] ...
[Show only current power source]      ...
[Logging]                             ...
[Required Components]                 ...
[Developer Menu]                      ...
```

The palette pictogram above is conceptual. Implement with the verified WinUI `SymbolIcon` built-in **`Symbol="FontColor"`**; do not add an image asset or new icon package.

Requirements:

- Use a normal `ctcontrols:SettingsCard`, **not** an expander or new Settings page.
- Header: `App theme`.
- Description: `Choose the main app appearance.`
- Header icon: `<SymbolIcon Symbol="FontColor" />`.
- One ComboBox on the right, with exactly three choices, **in this order**: `System`, `Light`, `Dark`.
- Default when no valid saved value exists: **System**.
- Use the existing SettingsCard spacing, sizing, and Fluent appearance; keep the ComboBox compact, aligned with the other Settings controls.
- No accent-color UI or changed accent palette; retain Windows/WinUI Fluent accent behavior.

Example XAML shape (adapt names/width to compile with the current page):

```xml
<ctcontrols:SettingsCard
    x:Name="AppThemeCard"
    Header="App theme"
    Description="Choose the main app appearance.">
    <ctcontrols:SettingsCard.HeaderIcon>
        <SymbolIcon Symbol="FontColor" />
    </ctcontrols:SettingsCard.HeaderIcon>
    <ComboBox x:Name="AppThemeComboBox"
              Width="140"
              SelectionChanged="AppThemeComboBox_SelectionChanged">
        <ComboBoxItem Content="System" Tag="System" />
        <ComboBoxItem Content="Light" Tag="Light" />
        <ComboBoxItem Content="Dark" Tag="Dark" />
    </ComboBox>
</ctcontrols:SettingsCard>
```

Place this entire card **immediately after** `UpdateCard` and **before** `GamingHomeExpander`.

## 4. Theme application

Use the existing WinUI 3 theme resources and built-in Fluent controls. The one Main App root content element is the theme target.

- Give the outermost `MainWindow.xaml` Grid a name such as `MainAppRoot`.
- Apply the selected mode through `MainAppRoot.RequestedTheme` (a `FrameworkElement`), not a runtime write to `Application.RequestedTheme`.
- `Light` maps to `ElementTheme.Light`; `Dark` to `ElementTheme.Dark`.
- `System` must **actually reflect the Windows default app light/dark appearance**. Prefer the native `ElementTheme.Default` inheritance path when it correctly follows Windows on this WinUI version. Microsoft WinUI documentation includes a caveat about `Default` resolving to Dark in some circumstances: do not assume `Default` is a correct System implementation without checking its effective behavior. If necessary, resolve the current Windows **app** theme through a small direct WinUI/Windows API mechanism instead of creating a general theme-monitoring framework. Preserve any normal OS theme changes while the UI is open where supported.
- Apply the initial saved theme during MainWindow initialization **before showing the window**; UI must not briefly open in the wrong theme and then switch.
- Change the theme in place when the ComboBox changes. Do not restart the Main UI or Runtime.
- The top-level Grid includes the `PrerequisiteSetupBusyOverlay`; leave its deliberately white-on-dim progress message readable. Avoid blanket control foreground/background overrides.
- Preserve the existing `MicaBackdrop` and standard Fluent styling. Verify that panels, native controls, flyouts, navigation surfaces, and backdrop remain readable in both appearances. Address any concrete theme mismatch minimally using existing theme resources; do not build a separate Mica/backdrop subsystem or hardcode a new color scheme.

Example core operation:

```csharp
MainAppRoot.RequestedTheme = selectedMode switch
{
    MainUiTheme.Light => ElementTheme.Light,
    MainUiTheme.Dark => ElementTheme.Dark,
    _ => ElementTheme.Default // only if verified to mean Windows app appearance
};
```

Keep a single Main App theme selection owner (preferably `MainWindow`) and let `SettingsPage` request changes with one small event/callback. A generic UI preference manager or additional state coordinator is unnecessary.

## 5. Persistence / failure behavior

Selection must survive closing/reopening the Main UI, Runtime restarts, normal application restarts, and routine Velopack updates.

Implement the smallest **Main UI-owned** persisted value (`System`, `Light`, or `Dark`) in a separate file such as `main-ui-theme.txt` inside the **existing canonical** `SteamInputAddonforClaw-Data` root. Do not extend Runtime-owned `AppSettings`, `settings.json`, frontend IPC contracts, or controller-related state just for a visual preference.

- Reuse the existing UI bootstrap information where feasible to locate the canonical data root (the supplied log directory is under that data root in the normal installed path). Check the actual path contract; do not hardcode `AppContext.BaseDirectory` as if it were necessarily the Velopack root, or unconditionally replace the data root with `%LOCALAPPDATA%`.
- UI-only enum and a tiny parse/load/save helper are sufficient. Do not invent a generic JSON configuration layer for one 3-value preference.
- Absent/unreadable/unknown/corrupt saved value -> System, without blocking Main UI startup.
- A failed save must not silently claim persistence: log the failure and restore/retain the last successfully stored selection, with a short user-readable description on the card. Do not crash or impair controller Runtime.
- Initialize ComboBox programmatically with its change handler suppressed, then use the same existing Main UI thread path for user edits. No locks, async work queues, polling, IPC, or race machinery for this local preference.
- Ensure settings on the actual main UserControl and MainWindow reflect the same active theme when navigating away and returning.

## 6. How to Use: always-light document only

Change only the **document renderer/WebView surface** necessary for stable readability:

1. In `HowToUseMarkdownRenderer.BuildHtml()`, replace `html { color-scheme: light dark; }` with a light-only scheme; set the `html` document canvas background white as well as the existing white `body`.
2. Remove the `@media (prefers-color-scheme: dark)` CSS branch entirely.
3. Keep current light text, link, code/preformatted, blockquote, and table styles. **Do not** create a separate theme mechanism for code blocks/tables: they are simply Markdown elements of the How to Use document.
4. When lazily creating `WebView2`, set `DefaultBackgroundColor` to white before navigation (WinUI 3 WebView2 exposes this property) so the blank document surface does not flash dark.
5. Do not change lazy creation, remote Markdown selection/download, successful-navigation completion logic, link routing, retry flow, or how English/Korean guide sources are selected.
6. The enclosing native `HowToUsePage` controls (loading spinner, failure message, Retry) remain normal app-theme controls. The rendered HTML/document background is the intentional light-only exception.

Minimal CSS direction:

```css
html { color-scheme: light; background: #ffffff; }
body { color: #202124; background: #ffffff; }
/* existing light document styles remain; no dark media override */
```

## 7. Tests / verification (local Codex responsibility)

Add or adjust ordinary automated tests where appropriate:

- The saved preference round-trips `System`, `Light`, `Dark`.
- Missing, malformed, or unknown preference falls back to `System`.
- Programmatic initial ComboBox setup does not trigger persistence/change callbacks.
- A failed preference write does not produce a misleading newly saved state or prevent UI creation.
- Settings XAML contains the single App theme card with the valid Fluent icon and the three choices, positioned **between** Application updates and Windows Gaming Full Screen Experience.
- Root theme application code covers all Main App content and does not mutate `Application.RequestedTheme` while running.
- Update `HowToUseMarkdownRendererTests` to expect `color-scheme: light`, white document canvas, and **no** dark-mode media CSS. Preserve tests for Markdown rendering, anchor routing, and external navigation.
- Confirm no new RPC, Runtime settings changes, or Overlay XAML/theme changes.

Run the appropriate existing UI test project and normal solution build/test commands supported by the repository. The implementer should investigate and fix any actual compile/test regressions introduced by this work.

**Physical MSI Claw / on-device light-dark-Mica validation is the user's post-merge responsibility, not a local Codex deliverable and not a PR blocker.** Do not demand a hardware signoff to complete this PR.

## 8. Strict non-goals

- No app-specific accent colors, MSI/Steam/OLED skins, theme packs, custom resource dictionaries, custom controls or control-template rewrites.
- No Windows title-bar replacement, window layout redesign, navigation reorganization, or unrequested Settings changes.
- No modifications to the standalone `SteamInputAddonforClaw.Overlay` appearance, Steam QAM, or ClawHUD rendering.
- No change to physical/controller Runtime ownership, Center M, HidHide, VIIPER, routing, device profiles, Steam/XBOX profiles, suspend/resume, startup tasks, uninstall logic, or IPC protocols.
- No CTW integration or old CTW architecture.
- No future-proofed theme architecture; use the smallest practical owner and persistence path.

## 9. Completion criteria

One focused PR is complete when:

1. Settings shows **App theme** as the **second card**, directly after **Application updates**, with **FontColor** icon and `System / Light / Dark` ComboBox.
2. Main App WinUI controls change theme immediately and consistently; `System` follows the effective Windows app appearance.
3. The choice is restored when the Main UI is reopened, without affecting Runtime or Overlay.
4. How to Use's **rendered document always remains white with dark text** regardless of OS/app theme.
5. Existing How to Use lazy WebView2 and guide content/navigation continue to work.
6. Automated build/tests pass, with no unrelated changes or device-only merge blocker.

## 10. References

- [WinUI FrameworkElement.RequestedTheme](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.requestedtheme)
- [WinUI Symbol enum (FontColor)](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.symbol)
- [WinUI WebView2.DefaultBackgroundColor](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.webview2.defaultbackgroundcolor)
