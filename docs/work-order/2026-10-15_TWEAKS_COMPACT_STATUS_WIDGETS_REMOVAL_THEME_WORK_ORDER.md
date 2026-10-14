# Work Order — Tweaks Tab Compact Cards, Inline States, Widgets Removal, and Theme-Correct Safety Dialog

> Date: 2026-10-15
> Repository: onehoon/SteamAddonforClaw
> Baseline inspected: latest main, commit 78eaf436fe3fea2312f995ceefe80db897d1651b. Refresh main immediately before implementation.
> Product: standalone Full PID1902 Steam Addon for Claw (no CTW integration)
> Implementation: local Codex
> Delivery: ONE focused PR
> Physical MSI Claw verification: user-owned AFTER merge, never a required Codex task, CI requirement, or PR review/merge blocker.

## 1. Goal and user-approved decisions

Improve the existing WinUI 3 Main App Tweaks tab without altering its operational Windows tweak semantics:

1. Make all Tweaks SettingsCards as compact as the other Main App cards. Remove the artificial MinHeight=84 and the permanently reserved 18 px second description/status line.
2. Move every dynamic state/operation message to a label immediately LEFT of its own toggle. Preserve the existing regular one-line description underneath the card title.
3. Show **Enabled** or **Disabled** on the FIVE remaining Registry Tweaks in their normal verified states. The label describes the user-facing feature/behavior implied by the title, **not** the internal "tweak is applied" flag. Follow the exact table in section 4.
4. On Memory Integrity and Virtual Machine Platform, DO NOT add normal Enabled/Disabled labels. Show only meaningful exceptions (restart required, configured but not running, unsupported, managed, unavailable, updating) to the left of the toggle.
5. Completely remove **Disable Windows Widgets** from this application feature: visible card, catalog entry, unavailable snapshot entry, Runtime dispatch, OS capture/set and edition support helper, stale localization resources, and Widgets-specific tests. Do NOT undo or erase a Widgets registry policy which may already exist on a user's computer.
6. Fix the once-per-local-day Tweaks safety ContentDialog opening light when the Main App itself is dark. It must use the **effective Main App theme** in System / Light / Dark modes, without changing the warning gate or its wording.

One PR is appropriate: all changes form one small Tweaks presentation/feature-retirement task. No implementation PR has been requested from this document-writing task.

## 2. Required reading and source authority

Read the following, with actual current main implementation taking precedence over historical work orders:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md (respect its later overrides)
- docs/work-order/2026-10-13_WINDOWS_11_TWEAKS_TAB_WORK_ORDER.md
- docs/work-order/2026-10-13_WINDOWS_TWEAKS_WIDGETS_EDGE_STARTUP_DELAY_WORK_ORDER.md
- docs/work-order/2026-10-13_FASTER_MENUS_COMBINED_TOGGLE_WORK_ORDER.md
- docs/work-order/2026-10-14_MAIN_APP_TWEAKS_TAB_RESW_PR8_WORK_ORDER.md
- docs/work-order/2026-10-12_MAIN_APP_UI_SYSTEM_LIGHT_DARK_THEME_WORK_ORDER.md

Inspect actual code/tests:

- src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml
- src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml.cs
- src/SteamInputAddonforClaw.UI/MainWindow.xaml and MainWindow.xaml.cs
- src/SteamInputAddonforClaw.UI/MainUiStrings.cs
- src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
- src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
- src/SteamInputAddonforClaw.Contracts/Frontend/WindowsTweaksFrontendContracts.cs
- src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksRuntime.cs
- src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksOperatingSystem.cs
- tests/SteamInputAddonforClaw.UiTests/WindowsTweaksUiContractTests.cs
- tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
- tests/SteamInputAddonforClaw.UiTests/MainUiThemeTests.cs
- tests/SteamInputAddonforClaw.Tests/WindowsTweaksRuntimeTests.cs
- tests/SteamInputAddonforClaw.Tests/WindowsTweaksOperatingSystemContractTests.cs

Architecture boundaries: one Windows user, one interactive session. The existing elevated Runtime remains OS mutation owner and the Main UI remains a frontend. Do not change PID1901/PID1902 routing, HidHide, VIIPER, MSI Center M lifecycle, sleep/resume ownership, Overlay, QAM, or any controller code. No new state owner, manager, IPC abstraction, lock, epoch, or generalized theme/tweak system.

## 3. Exact retained cards and visual layout

The tab retains SEVEN cards in the existing order, grouped exactly as follows:

Gaming Performance:
1. Memory Integrity
2. Virtual Machine Platform

Registry Tweaks:
1. Faster Menus (ID InstantContextMenus)
2. Classic Context Menu
3. Disable Password on Wake
4. Disable Edge Background
5. Disable Startup App Delay

Retain the original English card titles and descriptions, original section headings and order, existing per-card tooltip behavior, and RESW localization structure. This is not a card naming redesign.

### 3.1 Remove the tall-card layout

Current WindowsTweaksPage.xaml contains eight SettingsCards each with MinHeight="84". Each SettingsCard.Description holds a StackPanel with a normal description TextBlock and a dynamic state TextBlock with Height="18" (e.g. InstantContextMenusState). This reserves a blank second line during normal operation and is why the page's cards are taller than the other tabs.

For ALL seven retained cards:

- Remove MinHeight="84".
- Keep the existing static one-line description TextBlock and its x:Uid / text / trimming.
- Remove the old dynamic state TextBlock FROM the Description slot and eliminate the now unnecessary Description StackPanel when possible. Set the Description to the existing description TextBlock or another equally simple single-line composition that builds correctly.
- Reuse the existing dynamic state x:Name (MemoryIntegrityState, VirtualMachinePlatformState, InstantContextMenusState, ClassicContextMenuState, DisablePasswordOnWakeState, DisableEdgeBackgroundState, DisableStartupAppDelayState), but relocate that TextBlock into the SettingsCard's right-hand content slot, immediately before the ToggleSwitch.
- Do NOT reserve fixed vertical height/blank space for status text. When there is no meaningful status, use Visibility.Collapsed rather than Text="" with a fixed height.
- Keep the normal description visible at all times and ensure the right-hand status+toggle do not crowd or truncate the card title. Preserve Fluent/SettingsCard style and the existing horizontal toggle alignment. Prefer simple existing layout controls, not custom templates.
- Check status overflow under a narrow Main App window and 150% DPI. Long messages such as "Explorer restart required" may be ellipsized if necessary; allow the full message in a standard tooltip. Do not introduce horizontal scroll or forced minimum card height.

Illustrative right-side XAML shape (adapt for actual SettingsCard property/content requirements; do not blindly duplicate every control):

~~~xml
<ctcontrols:SettingsCard x:Name="InstantContextMenusCard"
                         x:Uid="Tweaks_InstantContextMenusCard"
                         Header="Faster Menus">
    <ctcontrols:SettingsCard.Description>
        <TextBlock x:Uid="Tweaks_InstantContextMenusDescription"
                   Text="Disable menu animation and reduce submenu delay to 10 ms."
                   MaxLines="1" TextTrimming="CharacterEllipsis" />
    </ctcontrols:SettingsCard.Description>
    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <TextBlock x:Name="InstantContextMenusState"
                   VerticalAlignment="Center" FontSize="12" Opacity="0.8"
                   MaxWidth="180" TextTrimming="CharacterEllipsis"
                   Visibility="Collapsed" />
        <ToggleSwitch x:Name="InstantContextMenusSwitch"
                      VerticalAlignment="Center" IsEnabled="False"
                      OffContent="" OnContent=""
                      Toggled="TweakSwitch_Toggled" />
    </StackPanel>
</ctcontrols:SettingsCard>
~~~

Existing WindowsTweaksUiContractTests asserts the toggle is a *direct SettingsCard child*, MinHeight=84 and a 18 px status line. Those assertions MUST be updated to verify the NEW requested structure, not preserved by working around the test.

### 3.2 Status handling must follow readback, not optimistic toggle changes

Keep GetControls / GetId bindings and the existing frontend request flow. The current code:

- sets IsOn from row.ConfiguredEnabled only when non-null;
- sends toggle.IsOn unchanged to SetWindowsTweakAsync;
- reverts the toggle while the write is in progress;
- uses the returned UpdatedTweak/ApplyTweakRow for authoritative readback;
- uses FormatState for exceptions;
- shows Updating… while mutating.

Preserve every behavior above. The only change is where and when the status label is visible.

Suggested local display helper, using the existing row.State and same MainUiStrings resource pattern:

~~~csharp
internal static string? FormatState(FrontendWindowsTweakSnapshot row)
{
    if (row.State is FrontendWindowsTweakState.Enabled or FrontendWindowsTweakState.Disabled)
    {
        if (row.Id is FrontendWindowsTweakId.MemoryIntegrity
            or FrontendWindowsTweakId.VirtualMachinePlatform)
            return null;

        var negativeTitle = row.Id is FrontendWindowsTweakId.DisablePasswordOnWake
            or FrontendWindowsTweakId.DisableEdgeBackground
            or FrontendWindowsTweakId.DisableStartupAppDelay;

        var tweakApplied = row.State == FrontendWindowsTweakState.Enabled;
        var displayEnabled = negativeTitle ? !tweakApplied : tweakApplied;
        return displayEnabled
            ? MainUiStrings.Get("Tweaks_State_Enabled", "Enabled")
            : MainUiStrings.Get("Tweaks_State_Disabled", "Disabled");
    }

    // KEEP existing exception/pending/Mixed/Managed/Unknown handling below.
    // Do not convert exceptions to ordinary Enabled/Disabled.
    return row.State switch { /* existing specialized cases */ };
}
~~~

The above is a suggested implementation pattern rather than a requirement to duplicate a switch statement. Do not flip row.ConfiguredEnabled, IsOn, the SetWindowsTweakAsync argument, Runtime state semantics, or registry values.

When updating the label, update visibility too:

~~~csharp
var label = FormatState(row);
stateText.Text = label ?? string.Empty;
stateText.Visibility = string.IsNullOrWhiteSpace(label)
    ? Visibility.Collapsed : Visibility.Visible;
~~~

When starting a mutation, also set the same left-of-toggle state TextBlock to Updating… and make it visible; otherwise the move from the old always-visible placeholder will make Updating… disappear. Keep previous "cannot confirm" top-level failure text handling and verified rollback. On completion, always restore the readback-driven label/visibility.

## 4. EXACT Enabled / Disabled mapping approved for Registry Tweaks

These labels are UI shorthand for the behavior described by the setting title. They are NOT raw registry DWORD values nor an assertion about all unrelated Windows preferences.

| Registry Tweaks card | Switch OFF label | Switch ON label |
| --- | --- | --- |
| Faster Menus | Disabled | Enabled |
| Classic Context Menu | Disabled | Enabled |
| Disable Password on Wake | Enabled | Disabled |
| Disable Edge Background | Enabled | Disabled |
| Disable Startup App Delay | Enabled | Disabled |

Important nuances:

- **Enable/disable wording is inverted for the three titles that begin with "Disable".** This inversion is DISPLAY ONLY. For those rows, internal FrontendWindowsTweakState.Enabled still means that the disable-tweak is applied.
- For Disable Edge Background and Disable Startup App Delay, switch OFF deletes only the Addon-owned policy/registry values. "Enabled" must NOT be represented to the user or tests as proof that Edge background activity is actually running or that Windows imposes a specific startup delay; Windows/app default behavior applies after the override is removed.
- For Disable Password on Wake, OFF is the existing explicit CONSOLELOCK=1 request and ON is the existing CONSOLELOCK=0 request; preserve AC/DC mixed and managed behavior.
- Faster Menus and Classic Context Menu are positively named tweaks: no inversion of their displayed normal-state labels.
- If Windows reports State=Mixed, Unknown, Unsupported, Managed, EnablePendingRestart, DisablePendingRestart or ExplorerRestartPending, show that existing special state instead of a misleading normal Enabled/Disabled label.
- For a row whose ConfiguredEnabled is null, preserve existing disabled/hidden toggle policy, but keep the exceptional text visible to the left in the right-hand content container.
- If there is a pending restart, do NOT claim the requested state is already effective by showing normal Enabled/Disabled in place of the pending status.

Use existing localized exception text, including:
- Tweaks_State_ConfiguredNotRunning
- Tweaks_State_RestartRequired
- Tweaks_State_ExplorerRestartRequired
- Tweaks_State_Unsupported
- Tweaks_State_Managed
- Tweaks_State_MixedApply
- Tweaks_State_Unavailable
- Tweaks_Updating

Add only two localizable status keys, in both en-US and ko-KR RESW, with English text in BOTH locales as in the current localization policy:

~~~text
Tweaks_State_Enabled  = Enabled
Tweaks_State_Disabled = Disabled
~~~

Avoid six separately phrased pairs, "Applied / Not Applied", "Blocked / Not blocked", "Restricted / Default", or other per-card status vocabularies. Do not add generic "Enabled / Disabled" to the two Gaming Performance cards.

## 5. Remove Disable Windows Widgets end-to-end

The user's MSI Claw Windows installation displays Unsupported for Disable Windows Widgets; this option is intentionally RETIRED, not moved to a new page or shown conditionally.

### 5.1 UI and localized resources

- Remove DisableWindowsWidgetsCard, DisableWindowsWidgetsState and DisableWindowsWidgetsSwitch from WindowsTweaksPage.xaml and their GetControls/GetId mappings.
- Remove Tweaks_DisableWindowsWidgetsCard.Header and Tweaks_DisableWindowsWidgetsDescription.Text from both RESW files.
- Ensure no orphan x:Uid, tooltip, mapping entry, or dead text remains.

### 5.2 Frontend typed contracts and Runtime

- Remove FrontendWindowsTweakId.DisableWindowsWidgets as an exposed/defined tweak.
- Remove that definition from FrontendWindowsTweakCatalog.All (now seven definitions).
- Remove it from FrontendWindowsTweaksSnapshot.Unavailable (now seven fallback rows).
- Remove the Widgets ID from WindowsTweaksStateMapper's supportsMixed whitelist. Keep mixed handling for Faster Menus, Edge Background and Startup App Delay.
- Remove capture and mutation cases for Widgets from WindowsTweaksOperatingSystem.

IMPORTANT: The enum is currently serialized/numerically referenced by the current frontend tests. Before removal, values are:
- DisableWindowsWidgets = 5
- DisableEdgeBackground = 6
- DisableStartupAppDelay = 7

Preserve the **remaining** IDs numerically by pinning explicit values as appropriate:

~~~csharp
public enum FrontendWindowsTweakId
{
    MemoryIntegrity = 0,
    VirtualMachinePlatform = 1,
    InstantContextMenus = 2,
    ClassicContextMenu = 3,
    DisablePasswordOnWake = 4,
    // Value 5 is retired. Never assign it to a different tweak.
    DisableEdgeBackground = 6,
    DisableStartupAppDelay = 7,
}
~~~

No replacement for retired ID 5, and no renumbering of surviving IDs. The existing Runtime unknown-ID rejection path must continue to reject a numeric request for retired value 5 without OS writes. Adjust contract tests accordingly. Follow the existing transport protocol version policy if any cross-version incompatibility requires it; do not add speculative compatibility state or a new migration system.

### 5.3 OS adapter cleanup, safety and tests

Remove Widgets-only symbols in WindowsTweaksOperatingSystem.cs:

- WidgetsPolicyPath constant;
- CaptureDisableWindowsWidgets;
- SetDisableWindowsWidgets;
- ReadWidgetsPolicyEditionSupport and ResolveWidgetsPolicyEditionSupport;
- WindowsCurrentVersionPath ONLY if no other code still needs it.

Do NOT remove the generic ResolveZeroDwordRegistryValues helper: Edge Background and Startup App Delay still use it. Do not alter their two-value all-zero/all-absent/Mixed logic.

CRITICAL: **Retiring the UI/feature must not mutate the OS or revert a prior Widgets tweak.** Do not delete the HKLM policy value AllowNewsAndInterests, remove the Dsh policy key, or introduce an uninstall/upgrade cleanup for that value as part of this PR. No Windows policy writes for Widgets may remain reachable through the Addon.

Remove or replace Widgets-specific contract/tests, including the edition-support test and Widgets mutation/write-failure tests, without gutting coverage of the shared registry-value resolver. Existing single-value resolver test cases may remain after renaming local variables away from "widgets". Historical docs/work orders are records: do not retroactively rewrite them; this newer work order supersedes their Widgets scope.

## 6. Safety notice must follow the actual app theme

Verified current behavior:

- MainWindow.ApplyMainUiTheme sets MainAppRoot.RequestedTheme to Light, Dark or the resolved Windows app theme and separately sets AppWindow.TitleBar.PreferredTheme.
- WindowsTweaksPage.ShowDailyWarningAsync creates a ContentDialog with XamlRoot = XamlRoot but no explicit RequestedTheme.
- When the app's chosen theme differs from the operating-system default, the dialog can render in the wrong theme.

Apply the minimal correction INSIDE ShowDailyWarningAsync: set dialog.RequestedTheme to the current page's effective ActualTheme when creating the dialog (or the equivalent verified effective Main App theme).

~~~csharp
var dialog = new ContentDialog
{
    Title = MainUiStrings.Get("Tweaks_SafetyNotice_Title", "Windows Tweaks safety notice"),
    // Preserve current Content and buttons.
    RequestedTheme = ActualTheme,
    XamlRoot = XamlRoot,
};
~~~

The snippet only illustrates the new theme property: KEEP all existing Content, button labels, DefaultButton and dialog return-value handling. Do not use a hardcoded Dark theme. Do not write Application.RequestedTheme, add a global dialog manager, or add a theme-specific XAML resource palette.

Preserve EXACTLY:
- the five existing safety-warning paragraphs (including current English resources and matching fallback);
- acceptance/cancellation behavior and control gating;
- once per local calendar day logic, in-process accepted date, persisted acknowledgment and refresh;
- no automatic restart/Explorer restart;
- the rest of the Main App and the intentional How to Use light document area.

Test selected Dark -> Dark dialog; selected Light -> Light dialog; System -> current resolved Windows app theme. Tests may be static/pure-source contract tests plus platform-available UI build; no physical-device demonstration or screenshot is a merge gate. The chosen mode should be observed at dialog creation so an app-theme change before opening Tweaks is reflected.

## 7. Focused deterministic verification updates

Update old assertions that encode the prior eight/tall layout; add only targeted tests for new behavior:

### 7.1 WindowsTweaksUiContractTests.cs
- Assert EXACTLY seven cards in the specified order and groups (two Gaming Performance + five Registry Tweaks).
- Assert no Widgets card, no MinHeight=84, no fixed 18 px status line, and one-line description retained.
- Assert dynamic state TextBlocks are paired immediately BEFORE their toggle on the right side of the same SettingsCard.
- Assert existing names/getters/handlers still map all seven IDs.
- Verify the warning gate is unchanged and the dialog explicitly takes the effective theme.
- Preserve the verified readback-driven mutation contract; existing tests which assert FormatState(enabled) == null must be updated for registry rows. Memory Integrity / VMP remain null for normal states.
- Test Updating… is made visible while an operation is in flight, and hides/replaces appropriately after completion.

### 7.2 WindowsTweaksRuntimeTests.cs / WindowsTweaksOperatingSystemContractTests.cs
- Catalog contains exactly the seven retained IDs in order; no Widgets ID; surviving numeric values remain 6 and 7.
- Retired numeric ID 5 cannot result in any OS write; Runtime returns its normal unsupported/unknown result.
- Keep existing checks for AC/DC password-on-wake, Faster Menus, Classic Context Menu, Edge Background, Startup App Delay, Memory Integrity and VMP. No writer-behavior changes.
- Keep mixed handling for Faster Menus, Edge Background and Startup App Delay; preserve readback fail/partial failure behavior.
- Remove Widgets-only support/capture/mutation tests and retain generalized all-zero/all-absent/Mixed resolver tests.

### 7.3 MainUiLocalizationResourceTests.cs
- Update fixed-x:Uid list to seven cards.
- Remove Widgets resource expectations.
- Add localization assertions for Tweaks_State_Enabled and Tweaks_State_Disabled, exactly matching fallback English in both locales.
- Keep warning body/5-paragraph tests and existing Edge tooltip localization tests.
- Preserve the no-x:Uid rule for dynamic state and operation-error labels.

### 7.4 Other tests and checks
- Run the affected UiTests and WindowsTweaks Runtime/OS contract tests plus the solution's applicable build/test/RESW/PRI checks.
- Account for changed SettingsCard child structure in source-structure tests.
- Existing lifecycle/request ordering (no optimistic write, verified readback, no per-toggle popup/restart, no auto action on tab open) remains unchanged.
- If normal local Windows automated tests cannot execute in the Codex environment, provide the exact build/test result and concrete reason; do not replace tests with unsupported hardware assumptions.

## 8. Scope limits and acceptance criteria

OUT OF SCOPE:
- Changing a registry DWORD's meaning or setter, toggling/inverting IsOn, altering SetWindowsTweakAsync, or adding new tweaks.
- Hiding only the Widgets card while leaving a reachable internal Widgets mutation feature.
- OS migration, cleanup, or restoration of historical Widgets policies.
- Changes to MainWindow theme preference storage, Runtime/IPC architecture, or the entire application's other dialogs.
- New generic configuration/status abstractions, retries, extra owner state, or theoretical race handling.
- Hardware validation by Codex/CI, or holding merge for post-merge MSI Claw testing.

ACCEPTANCE:
1. Seven cards, correct order and section ownership; Widgets completely absent from functionality, but OS Widgets policy not touched.
2. All cards have compact standard SettingsCard sizing: no always-empty second status row, no MinHeight=84.
3. Every dynamic status is immediately left of its switch; no status appears under description.
4. The five Registry Tweaks display exactly the Enabled/Disabled mapping from section 4 in ordinary verified state; two Gaming Performance cards show only exceptions.
5. Pending/unknown/managed/mixed/unavailable/updating stay clearly visible and never masquerade as verified Enabled/Disabled.
6. Daily safety dialog follows Main App System/Light/Dark selection; existing safety/acknowledgment logic unchanged.
7. The seven surviving controls preserve exact OS operation, readback, errors, lifecycle and frontend semantics.
8. Focused automated tests and applicable build/localization checks pass; final PR review is code/test based. User handles real-device verification after merge.

Deliver the code and focused tests as ONE PR. In the PR description, explicitly list the visible UX changes, Widgets retirement/no-registry-cleanup behavior, the saved numeric IDs, and automated verification performed.
