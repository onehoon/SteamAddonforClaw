# Work Order — Main App RESW PR8: Windows Tweaks Tab (Safety Notice, Settings and Status)

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Delivery:** One focused **PR8** against latest `main` after merged Main App localization PRs #785–#791 (foundation, Device, Controller, Steam, XBOX, ClawHUD, Shortcut/How to Use). Refresh HEAD before editing.  
**Real-device testing:** The user validates the MSI Claw **after merge**. Do not require local Codex/CI hardware testing or block the PR for missing physical verification.

## 0. Goal, scope and non-negotiable policies

Complete the existing **unpackaged WinUI 3 Main App** RESW localization of its **Tweaks** tab, currently implemented by `Views/WindowsTweaksPage.xaml` and `Views/WindowsTweaksPage.xaml.cs`:

1. Left navigation label `Tweaks`.
2. Page header and introduction; both section headings; **all eight settings-card headers and English description text**; preserve dynamic state labels and card composition.
3. All Main-UI-authored loading/acknowledgement/failure/Updating messages.
4. The **complete daily safety notice** (title, entire body, acknowledgement and cancel buttons) as translated UI-owned text.
5. Locally formatted typed Windows tweak state labels (e.g., pending restart, managed and unavailable).
6. The existing **Edge background description tooltip**, which duplicates a visual description and must not remain hardcoded English after a future Korean translation.

**Both `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw` must initially contain the exact current English for every new resource.** Do not write Korean translations now. In later work the user must be able to translate by editing the Korean RESW alone, without another C#/XAML edit. No in-app language selection, culture override, additional resource mechanism or secondary localization layer.

**Strict Main App UI-only boundary**: Do not modify the Windows Tweaks Runtime, policy/registry writers, VMP DISM/PowerShell feature implementation, Windows state observation, shared typed contracts, platform applicability or frontend IPC. Do not modify the separate Addon Quick Settings Overlay, real ClawHUD, CTW, or any controller authority component.

**Message ownership rule carried forward from PR2–PR7:** translate UI-owned strings, but leave `result.FailureMessage` and other Runtime/Frontend-owned raw diagnostic strings verbatim, including error codes, type IDs, `row.Detail` and log values. Do not normalize or parse producer strings for translation.

This PR is **not** a security-setting redesign. Preserve the safety warning and its gate before switches become available, the actual configured/effective state semantics, and no automatic Windows/Explorer restart.

## 1. Required source and architecture reading

Use the actual Full1902 document-precedence rules:

- `docs/Full 1902 Implementation/README.md`.
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.

Read the most relevant completed Tweaks feature/work orders as historical design context, giving **latest implementation precedence** to actual main source:

- `docs/work-order/2026-10-13_WINDOWS_11_TWEAKS_TAB_WORK_ORDER.md` — the **initial five-item design**, superseded by actual eight-item UI.
- `docs/work-order/2026-10-13_WINDOWS_TWEAKS_WIDGETS_EDGE_STARTUP_DELAY_WORK_ORDER.md` — three additional cards.
- `docs/work-order/2026-10-14_VMP_HVCI_MIT_IMPLEMENTATION_ALIGNMENT_WORK_ORDER.md` — recent Memory Integrity/VMP fixes; no OS changes in localization.
- `docs/work-order/2026-10-14_MAIN_UI_RESW_LOCALIZATION_PR1_DEPLOYMENT_SAFE_FOUNDATION_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_CLAWHUD_TAB_RESW_PR6_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_SHORTCUT_AND_HOW_TO_USE_RESW_PR7_WORK_ORDER.md`.

Read current code and tests:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml
src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
src/SteamInputAddonforClaw.Contracts/Frontend/WindowsTweaksFrontendContracts.cs  (inspection only)
src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksRuntime.cs              (inspection only)
src/SteamInputAddonforClaw/WindowsTweaks/WindowsTweaksOperatingSystem.cs      (inspection only)
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/WindowsTweaksUiContractTests.cs
tests/SteamInputAddonforClaw.UiTests/MainNavigationIconContractTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.Tests/WindowsTweaksRuntimeTests.cs               (regression reference)
tests/SteamInputAddonforClaw.Tests/WindowsTweaksOperatingSystemContractTests.cs (regression reference)
```

Treat Full1902 as standalone, with **one Windows user/one interactive session**. Old CTW integration is not relevant. Do not introduce new process authority, abstraction layers, generic Windows tweaks engines, race-state machinery or changes to security operations in the name of translation.

## 2. Verified main-branch implementation inventory

### 2.1 Actual file names and UI shape

The nav item in `MainWindow.xaml` is:

```xml
<NavigationViewItem Content="Tweaks" Tag="Tweaks">
```

The page class is **`WindowsTweaksPage`**, not `TweaksPage`:

- `Views/WindowsTweaksPage.xaml` — about **90 lines**, eight cards, two section headings.
- `Views/WindowsTweaksPage.xaml.cs` — about **283 lines**, daily warning, typed state formatting, capture/mutation and result rendering.

The current eight cards, in their original order, are:

| ID (`FrontendWindowsTweakId`) | Existing English Header | Existing English description |
| --- | --- | --- |
| `MemoryIntegrity` | `Memory Integrity` | Configure Windows kernel memory protection. |
| `VirtualMachinePlatform` | `Virtual Machine Platform` | Enable or disable the Windows virtualization platform feature. |
| `InstantContextMenus` | `Faster Menus` | Disable menu animation and reduce submenu delay to 10 ms. |
| `ClassicContextMenu` | `Classic Context Menu` | Use the classic Windows 11 context menu for this user. |
| `DisablePasswordOnWake` | `Disable Password on Wake` | Return from sleep without requiring sign-in. |
| `DisableWindowsWidgets` | `Disable Windows Widgets` | Block the Windows Widgets experience where supported by Windows policy. |
| `DisableEdgeBackground` | `Disable Edge Background` | Disable Edge Startup Boost and background mode; existing processes may continue until closed. |
| `DisableStartupAppDelay` | `Disable Startup App Delay` | Remove configured Explorer startup-app launch delays; actual startup gains may vary. |

The first **two** are under `Gaming Performance` and the latter **six** under `Registry Tweaks`.

Each card currently contains a `SettingsCard.Description` **property element**, not a simple `Description="..."` attribute. The property-element body contains a static description `TextBlock` and a **separate dynamic state TextBlock** (e.g., `MemoryIntegrityState`). Do not flatten or replace this composition with an ordinary `Description` attribute. The dynamic state label is set by `FormatState`.

Eight switches remain bound to typed IDs with `TweakSwitch_Toggled`, initially `IsEnabled="False"`, no XAML `IsOn` default, empty `OnContent`/`OffContent` and `MinHeight="84"`. No additional settings or switch are permitted.

### 2.2 Safety gating and mutations (behavioral contracts)

`WindowsTweaksPage.ActivateCoreAsync`:

1. Shows the loading prompt and obtains `CaptureWindowsTweaksAsync`.
2. Checks `ShouldShowWarning(today, snapshot.WarningAcknowledgedLocalDate, _acceptedWarningDateThisProcess)`.
3. When required, **hides `TweaksPanel`** while the daily warning is pending.
4. Only after explicit acceptance, records `_acceptedWarningDateThisProcess`, calls `AcknowledgeWindowsTweaksWarningAsync(today)` and refreshes.
5. Only then shows controls and applies the resulting snapshot. Declining leaves the controls hidden.
6. A failed date persistence produces a UI-authored warning without undoing in-process acknowledgement.

This is a **safety-critical user-facing contract**. All five existing safety-notice paragraphs must remain substantively and initially **byte-for-byte English-equivalent**, in the same order, including the newer registry-policy/startup-delay warning.

`TweakSwitch_Toggled` immediately restores the current verified `ConfiguredEnabled` position, disables the mutating toggle, displays `Updating…`, submits `SetWindowsTweakAsync(id, requested)`, then adopts the **updated authoritative row** using `ReplaceTweak` and `ApplyTweakRow`. On exceptions it restores current row and displays a locally authored failure. Do not introduce optimistic state, refresh all eight rows after each edit, or issue new OS commands.

`RequestRefresh` and `DrainQueuedRefresh` use existing `_activationInProgress`/`_refreshInProgress`/`_refreshQueued`/`_mutatingIds` guards; preserve them. No new locks/epochs/owner needed.

## 3. Navigation and fixed XAML properties — `x:Uid` + dotted RESW keys

Add `x:Uid="MainNav_Tweaks"` only to the existing nav item. Preserve `Content="Tweaks"` as English XAML fallback and `Tag="Tweaks"` exactly, along with current icon, menu position and lazy page lifetime.

```xml
<NavigationViewItem x:Uid="MainNav_Tweaks" Content="Tweaks" Tag="Tweaks">
```

Resource: `MainNav_Tweaks.Content = Tweaks`.

For fixed page text add distinct, descriptive x:Uid values. Examples:

```xml
<TextBlock x:Uid="Tweaks_PageTitle" Text="Tweaks" ... />
<TextBlock x:Name="EntryPromptText"
           x:Uid="Tweaks_EntryPrompt"
           Text="Review the safety notice to open these controls." ... />
<TextBlock x:Uid="Tweaks_GamingPerformanceHeader" Text="Gaming Performance" ... />

<ctcontrols:SettingsCard x:Name="MemoryIntegrityCard"
                         x:Uid="Tweaks_MemoryIntegrityCard"
                         Header="Memory Integrity" MinHeight="84">
    <ctcontrols:SettingsCard.Description>
        <StackPanel Spacing="2">
            <TextBlock x:Uid="Tweaks_MemoryIntegrityDescription"
                       Text="Configure Windows kernel memory protection."
                       MaxLines="1" TextTrimming="CharacterEllipsis" />
            <TextBlock x:Name="MemoryIntegrityState" ... />
        </StackPanel>
    </ctcontrols:SettingsCard.Description>
    <ToggleSwitch ... />
</ctcontrols:SettingsCard>
```

Matching `Resources.resw` names:

```text
MainNav_Tweaks.Content                      = Tweaks
Tweaks_PageTitle.Text                      = Tweaks
Tweaks_EntryPrompt.Text                    = Review the safety notice to open these controls.
Tweaks_GamingPerformanceHeader.Text        = Gaming Performance
Tweaks_RegistryTweaksHeader.Text           = Registry Tweaks
Tweaks_MemoryIntegrityCard.Header          = Memory Integrity
Tweaks_MemoryIntegrityDescription.Text     = Configure Windows kernel memory protection.
```

Apply to **all eight SettingsCard Header properties and all eight inner static description TextBlocks**. Recommended consistent naming:

```text
Tweaks_VirtualMachinePlatformCard.Header
Tweaks_VirtualMachinePlatformDescription.Text
Tweaks_InstantContextMenusCard.Header
Tweaks_InstantContextMenusDescription.Text
Tweaks_ClassicContextMenuCard.Header
Tweaks_ClassicContextMenuDescription.Text
Tweaks_DisablePasswordOnWakeCard.Header
Tweaks_DisablePasswordOnWakeDescription.Text
Tweaks_DisableWindowsWidgetsCard.Header
Tweaks_DisableWindowsWidgetsDescription.Text
Tweaks_DisableEdgeBackgroundCard.Header
Tweaks_DisableEdgeBackgroundDescription.Text
Tweaks_DisableStartupAppDelayCard.Header
Tweaks_DisableStartupAppDelayDescription.Text
```

Do **not** add a `x:Uid` or hardcoded Text to dynamic `OperationMessageText` or the eight `<Id>State` TextBlocks. Those are populated by authoritative state results and `FormatState`. Keep all `x:Name`, event handlers, tooltip semantics, card order, original English text attributes and enum mapping unchanged. The user wants localization, not a redesign of section labels or card descriptions.

### 3.1 Edge background tooltip requires special handling

The `DisableEdgeBackground` static description TextBlock currently repeats **the same English message** in both `Text` and `ToolTipService.ToolTip` because its one-line description can be trimmed.

Only its `Text` is covered by the existing published-PRI verifier's ordinary dotted `UID.Text` resource-key path. If the tooltip stays hardcoded English, the Main App will show translated body text with an English hover tooltip after a Korean translation. This is a genuine UI localization gap.

**Preferred minimal solution (no special attached-property resource machinery):**

1. Add `x:Name="DisableEdgeBackgroundDescriptionText"` and `x:Uid="Tweaks_DisableEdgeBackgroundDescription"` to the existing TextBlock, preserving the source English `Text` and current English `ToolTipService.ToolTip` as fallbacks.
2. In `WindowsTweaksPage`'s existing constructor, immediately after `InitializeComponent()`, set the tooltip from the **same published UID Text resource** (no redundant separate sentence key), e.g.:

```csharp
public WindowsTweaksPage()
{
    InitializeComponent();
    ToolTipService.SetToolTip(
        DisableEdgeBackgroundDescriptionText,
        MainUiStrings.Get(
            "Tweaks_DisableEdgeBackgroundDescription/Text",
            "Disable Edge Startup Boost and background mode; existing processes may continue until closed."));
}
```

3. Confirm the C# resource path is **slash-separated** `Tweaks_DisableEdgeBackgroundDescription/Text`, while the **source** RESW key is dotted `Tweaks_DisableEdgeBackgroundDescription.Text`. `MainUiStrings.Get` prepends `Resources/` but never converts `.` to `/`.
4. This updates tooltip on page construction in the current OS language and preserves the existing static English fallback. No runtime language-switch handling is needed.
5. **No new tooltip manager, attached-property PRI verifier or extra resource key**. If a simpler verified WinUI binding provides equivalent text+tooltip consistency, it is acceptable, but the published PRI must actually contain it.

## 4. Local C# messages and typed state summaries

Use `MainUiStrings.Get` for every **locally authored** user-visible string in `WindowsTweaksPage.xaml.cs`. Give each message a complete, context-specific key. Preserve existing catch/refresh ordering, severity, visibility and producer string precedence.

### 4.1 Activation and mutation messages — exact source English

| Code path | Suggested resource key | Current English value |
| --- | --- | --- |
| `ActivateCoreAsync` while reading | `Tweaks_Loading` | Reading current Windows settings… |
| Safety dialog not yet accepted | `Tweaks_AcceptSafetyPrompt` | Review and accept the Windows Tweaks safety notice to view these controls. |
| Acknowledgement date failed to save | `Tweaks_AcknowledgementSaveFailed` | The notice was accepted, but its date could not be saved. It may be shown again after the app restarts. |
| `ActivateCoreAsync` catch | `Tweaks_LoadFailed` | Windows Tweaks could not be loaded. Select this tab again to retry. |
| `TweakSwitch_Toggled` pending | `Tweaks_Updating` | Updating… |
| `TweakSwitch_Toggled` catch | `Tweaks_ChangeUnconfirmed` | The change could not be confirmed. Reopen the Tweaks tab to refresh Windows state. |

Example, preserving direct state write and visibility:

```csharp
EntryPromptText.Text = MainUiStrings.Get(
    "Tweaks_Loading",
    "Reading current Windows settings…");

// Keep existing branch and no-optimistic-state behavior.
controls.State.Text = MainUiStrings.Get("Tweaks_Updating", "Updating…");
```

**Do not translate** `result.FailureMessage` from `SetWindowsTweakAsync`:

```csharp
SetOperationMessage(result.Succeeded ? null : result.FailureMessage);
```

This exact producer-precedence statement should remain unchanged. `SetOperationMessage` still hides blank messages and shows the Runtime's failure verbatim. `FrontendWindowsTweakSnapshot.Detail` and `FrontendWindowsTweaksSnapshot.Unavailable` producer descriptions remain untouched; those details are not currently injected into the card's compact `FormatState` text.

### 4.2 FormatState is Main UI-owned — localize by typed state, not raw text

`internal static string? FormatState(FrontendWindowsTweakSnapshot row)` directly authors seven different visible English outputs. These are not strings received from Runtime; they are selected from a typed enum `FrontendWindowsTweakState`. Localize the returned labels, **not the state enum or backing OS value**.

| Existing `FormatState` branch | Suggested key | Current English fallback |
| --- | --- | --- |
| `EnablePendingRestart` specifically for Memory Integrity, configured ON/not running | `Tweaks_State_ConfiguredNotRunning` | Configured — not running |
| Other enable/disable pending restart | `Tweaks_State_RestartRequired` | Restart required |
| `ExplorerRestartPending` | `Tweaks_State_ExplorerRestartRequired` | Explorer restart required |
| `Unsupported` | `Tweaks_State_Unsupported` | Unsupported |
| `Managed` | `Tweaks_State_Managed` | Managed |
| `Mixed` | `Tweaks_State_MixedApply` | Mixed — turn On to apply |
| fallback (including `Unknown`) | `Tweaks_State_Unavailable` | Unavailable |

**Keep exactly** the special Memory Integrity condition (`row.Id == MemoryIntegrity && ConfiguredEnabled == true && EffectiveEnabled == false`) and preserve the general `EnablePendingRestart` and `DisablePendingRestart` fallback to `Restart required`.

For `Enabled` and `Disabled`, `FormatState` currently returns **null**. The physical switch itself conveys state. **Do not invent `"On"/"Off"` labels** or change the null policy.

Example:

```csharp
FrontendWindowsTweakState.ExplorerRestartPending =>
    MainUiStrings.Get("Tweaks_State_ExplorerRestartRequired", "Explorer restart required"),
```

Do not localize `FrontendWindowsTweakId`/`FrontendWindowsTweakState` identifiers, alter configured/effective flags, enable disabled switches, or misrepresent pending changes as effective.

## 5. Safety notice — translate ONE complete warning body

`ShowDailyWarningAsync()` currently builds a **single user-visible 5-paragraph safety notice** by concatenating English string literals separated by `\n\n`. It is the user's daily gate to changing potentially security-sensitive Windows settings.

**Use one complete RESW body key** `Tweaks_SafetyNotice_Body`, not a list of independent translated sentence fragments. The English value and English fallback must preserve the entire existing notice, paragraph order, punctuation and every safety statement. The full English body is:

```text
Some changes need a Windows restart or Explorer restart to become fully effective. This app will not restart either automatically.

Memory Integrity strengthens kernel protection. Disabling it reduces protection. Enabling it can make incompatible drivers or apps malfunction and, rarely, prevent normal boot.

Virtual Machine Platform may be required by virtualization-dependent software; disabling it can stop that software from working.

Disabling password/sign-in on wake lets someone with physical access resume directly to the desktop.

These settings are reversible where Windows and policy permit. Windows policy or firmware locks can prevent a requested change. Registry policy controls may be restricted, and removing startup delay can make startup apps launch closer together.
```

Do not replace it with a shorter generic warning, omit driver/boot security risk, omit privacy risk from password-less wake, omit firmware/policy restriction, or omit registry/startup-delay caveats.

Example minimal C# implementation reusing the **original five source literals as the exact English fallback**:

```csharp
var content = MainUiStrings.Get(
    "Tweaks_SafetyNotice_Body",
    "Some changes need a Windows restart or Explorer restart to become fully effective. This app will not restart either automatically.\n\n"
    + "Memory Integrity strengthens kernel protection. Disabling it reduces protection. Enabling it can make incompatible drivers or apps malfunction and, rarely, prevent normal boot.\n\n"
    + "Virtual Machine Platform may be required by virtualization-dependent software; disabling it can stop that software from working.\n\n"
    + "Disabling password/sign-in on wake lets someone with physical access resume directly to the desktop.\n\n"
    + "These settings are reversible where Windows and policy permit. Windows policy or firmware locks can prevent a requested change. Registry policy controls may be restricted, and removing startup delay can make startup apps launch closer together.");
```

Register the RESW value with **actual double newline separators** (e.g. literal line breaks preserved in the value element), not backslash-n character pairs. Verify the value returned from published PRI has exactly four `\n\n` separators and the complete text; check resource XML escape/newline semantics carefully.

Add three more complete resources:

```text
Tweaks_SafetyNotice_Title    = Windows Tweaks safety notice
Tweaks_SafetyNotice_Accept   = I understand
Tweaks_SafetyNotice_Cancel   = Cancel
```

Then only replace the text-valued constructor properties:

```csharp
var dialog = new ContentDialog
{
    Title = MainUiStrings.Get("Tweaks_SafetyNotice_Title", "Windows Tweaks safety notice"),
    Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
    PrimaryButtonText = MainUiStrings.Get("Tweaks_SafetyNotice_Accept", "I understand"),
    CloseButtonText = MainUiStrings.Get("Tweaks_SafetyNotice_Cancel", "Cancel"),
    DefaultButton = ContentDialogButton.Primary,
    XamlRoot = XamlRoot,
};
return await dialog.ShowAsync() == ContentDialogResult.Primary;
```

Do **not** modify:

- `ShouldShowWarning` or the local `DateOnly` comparison.
- Accepted-this-process and persisted acknowledged date state, including the failure-to-save notice.
- The strict `if (!await ShowDailyWarningAsync()) return;` gate before `AcknowledgeWindowsTweaksWarningAsync(today)` and before setting `TweaksPanel.Visibility = Visible`.
- `ContentDialogButton.Primary` semantics or the consequence of Cancel.
- Actual Windows/Explorer reboot policy (none automatically) or any registry/OS mutation code.

**Safety copy itself should remain a single authority within RESW for each language**; the English C# fallback is intentionally duplicated solely for resource failure resilience. No second dynamically assembled UI notice or per-toggle popup.

## 6. Preserve actual supported Windows lifecycle and OS safety

Localization must not change:

- The eight approved typed `FrontendWindowsTweakId` entries or order; no new feature cards, no unsupported feature support.
- Runtime applicability filtering (even though current catalog marks all eight `AllSupportedWindows`); `GetCardVisibility(snapshot, id)` hides any missing row.
- `CaptureWindowsTweaksAsync` authoritative snapshot; no persisted/optimistic toggle guess.
- `ConfiguredEnabled` vs `EffectiveEnabled` and pending restart logic.
- `CanChange` disabled-state semantics for unsupported, managed, unknown and pending conditions.
- `SetWindowsTweakAsync` invocation and updated-row-only result handling, `ReplaceTweak`, and last verified rollback on exceptions.
- `_mutatingIds`, `_applyingSnapshot`, serialized activation/refresh behavior and visibility gates.
- The daily safety warning acknowledgement sequence and reentry after date changes.
- VMP feature enable/disable backend and Memory Integrity security registry state/rollback behavior (not owned by Main UI).
- No automatic reboot, Explorer restart, process launch, UAC elevation hop, Windows registry write or background polling in the UI.
- Sleep/hibernate/resume, Full1902 authority, HidHide, VIIPER teardown, PID1901↔PID1902, recovery and fail-close paths.
- Logging messages, typed IDs, Runtime `FailureMessage` or `Detail` text.

A present-day supported device may report a pending reboot after VMP or HVCI changes. **Localizing the label must not change which state is reported or whether it is safe to toggle**. No race-defensive architecture is justified by this display-only change.

## 7. Tests: update brittle assertions without weakening contracts

Existing source tests in `tests/SteamInputAddonforClaw.UiTests/WindowsTweaksUiContractTests.cs` (currently ~239 lines) explicitly assert:

- Menu position/tags and Device as default.
- Exactly eight cards in a specified order, each with `x:Name`, one direct ToggleSwitch child, `IsEnabled=False`, absent `IsOn`, `MinHeight=84` and dynamic state TextBlock.
- Original English `Header` and descriptions, section grouping, and no extra widths.
- `ShouldShowWarning` date behavior, no unknown/pending = Off.
- `FormatState` returns `Mixed — turn On to apply`, `Restart required` and null for Enabled.
- The warning gate precedes acknowledgement and display, with expected English key phrases in the safety notice.
- Only updated row gets applied, no `CaptureWindowsTweaksAsync` in the mutation handler; original raw `result.FailureMessage` passes through.
- Page navigation does not skip Shortcut deactivation.

**Preserve the original English XAML Header and Text attributes** after `x:Uid` insertion, so most structural source assertions still pass without modification.

Existing `FormatState` behavioral tests currently assert English literal results. On initial PR8 build, both locale resource values are English, so results should remain identical. For future translation-friendly tests, prefer asserting resource-key wiring and typed state distinction without requiring the Korean resource value to remain English forever. Keep the special Memory Integrity state branch tested separately.

Existing tests currently search for phrases `"Registry policy controls may be restricted"` and `"startup apps launch closer together"` in C# source; preserving the complete English fallback from `5` allows those assertions to remain valid. Do **not** delete them or weaken the safety content because they are relevant user-protection requirements.

### 7.1 New resource tests

Add focused tests to `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` and, where appropriate, `WindowsTweaksUiContractTests.cs`:

1. `MainNav_Tweaks.Content` maps to the same `Tag="Tweaks"` item with unchanged icon and menu order.
2. All static `x:Uid` values on `WindowsTweaksPage.xaml` are unique; XAML `Text` and `Header` resources exist in both languages with nonblank candidates and preserve exact English fallback text. **Do not** give `x:Uid` to runtime-generated state fields.
3. Exactly eight cards, two original sections, eight direct child toggles and unchanged `TweakSwitch_Toggled` connections, MinHeight/initial enabled/default structure.
4. All six locally authored activation/mutation messages have matching RESW keys; `result.FailureMessage` remains unmodified and unchanged in priority.
5. All seven non-null `FormatState` string results are keyed and the typed switch semantics remain intact, particularly Memory Integrity's `Configured — not running` special case, distinct Explorer/Windows restart, Managed/Mixed/Unsupported/Unavailable, and null for Enabled/Disabled.
6. `Tweaks_SafetyNotice_Body` exists in `en-US`/`ko-KR`, has five complete paragraphs and four double-newline separators in the same order, contains all original warnings, and is the actual message passed to `ContentDialog`. Preserve the `ShouldShowWarning` and acknowledgement gating tests.
7. `Tweaks_SafetyNotice_Title/Accept/Cancel` are RESW-backed; warning content no shorter/missing message.
8. The Edge description and its Tooltip both use the **same** `Tweaks_DisableEdgeBackgroundDescription.Text` resource with the correct C# slash lookup, preserving the source English tooltip fallback.
9. No new runtime changes, no additional XAML cards, no unapproved resource changes in other tabs.
10. **Published PRI**, not just source XML, resolves each newly registered UI key to **exactly one nonempty `en-US` and `ko-KR` candidate**. Explicitly check the multiline notice's paragraph separators from published PRI.

**Do not** permanently assert `en-US`/`ko-KR` resource **value equality** in tests. The two key sets must be identical and both nonempty; initial resource content equality is an implementation-phase check only, otherwise future translation would break CI.

### 7.2 Existing PRI handling

The existing `scripts/verify-ui-localization-pri.ps1` already recognizes standard dotted `<Uid>.Text`, `<Uid>.Header`, `Content`, `Description` and `PlaceholderText`, maps them to `/Resources/<Uid>/<Property>`, and checks all RESW C# keys as normal `/Resources/<key>`. It also enforces one nonempty candidate in `en-US` and `ko-KR`.

**Use that script unchanged**. The tooltip strategy in `3.1` uses an existing `UID.Text` resource, so there is no special attached-property PRI key to teach the verifier. Do not duplicate a new script, bypass missing PRI candidates, change resource language defaults or add MSIX packaging.

No new UI container should be needed. Compile and validate the existing unpackaged deployment/publish shape.

## 8. Expected file changes and automated validation

**Expected source changes:**

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml
src/SteamInputAddonforClaw.UI/Views/WindowsTweaksPage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/WindowsTweaksUiContractTests.cs    (only as needed)
other directly affected existing Main App UI source/PRI tests          (only as needed)
```

**Do not modify** these unless a demonstrated compile/test defect requires a minimal follow-up:

```text
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.Contracts/**
src/SteamInputAddonforClaw/WindowsTweaks/**
src/SteamInputAddonforClaw/Frontend/**
src/SteamInputAddonforClaw.FrontendTransport/**
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw.UI/Views/SettingsPage.*
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
```

**Local Codex automated checks, not hardware testing:**

1. `dotnet restore SteamInputAddonforClaw.slnx` and Release solution/UI tests build.
2. Existing UI and core tests, especially `WindowsTweaksUiContractTests`, `MainUiLocalizationResourceTests`, navigation/icon tests and Windows Tweaks Runtime tests.
3. Existing CI PowerShell regression scripts, release metadata/source checks and `git diff --check`.
4. `scripts/publish-layout.ps1` with actual published `ui/SteamInputAddonforClaw.UI.pri` generation and existing `verify-ui-localization-pri.ps1`.
5. `scripts/verify-publish-assets.ps1` and related publish asset/size checks.
6. Verify both initial resource candidates render the current exact English strings and the safety text has original paragraph boundaries.

**Acceptance:**

- Main App Tweaks nav, all eight cards, two sections, states, loading/mutation messages and complete safety notice are RESW-backed.
- Edge description tooltip is translated through the same local resource as its description.
- Both locale resources initially contain English and can be independently translated later.
- The daily warning is still presented at correct time, contains identical safety meaning, cannot be bypassed by text changes and is still acknowledged only after explicit confirmation.
- Typed state, actual OS changes, result/failure priority, reboot policy, frontend/Runtime ownership and other tab behavior are untouched.
- Build, CI test and published PRI validation succeed.
- **Hardware validation is the user's post-merge responsibility and is not a PR blocker.**

## 9. Local Codex PR submission instructions

Open **one PR8**. PR description must briefly report:

1. Count of added `Tweaks_*` resources and all modified Main App UI surfaces.
2. Daily safety notice full-body resource and unchanged gate/date acknowledgement.
3. Enum state display labels, producer failure/Detail untouched, and configured/effective correctness.
4. Edge tooltip use of the existing description `UID.Text` and successful published PRI URI verification.
5. No changes to Runtime, Windows security settings implementation, contracts, separate Overlay or CTW.
6. Build, tests, existing CI checks and published resource verifier outcomes.

Do not require the local Codex to test on a handheld or wait for post-merge user testing. The intended result is a narrowly scoped and release-ready localization PR.
