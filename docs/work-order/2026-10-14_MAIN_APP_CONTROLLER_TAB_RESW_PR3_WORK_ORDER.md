# Work Order — Main App RESW PR3: Controller Tab and Controller Navigation Label

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation baseline:** latest `main`, after merged PR #785 (RESW foundation) and PR #786 (Device tab). Re-read HEAD before coding.  
**Owner:** Local Codex  
**Delivery:** One focused implementation PR; independently mergeable and distributable  
**Platform:** Standalone Full PID1902, one Windows administrator user, one interactive session  
**Physical verification:** User-owned **after merge**, not a Codex requirement, CI gate, or review blocker.

## 0. Goal and locked decisions

Convert **all Controller-tab-owned human-readable UI text** from hardcoded English into the existing Main App `en-US`/`ko-KR` RESW + PRI pipeline, preserving the **current English text exactly** in both source files at PR3 delivery.

Do **not** translate any strings into Korean yet. The product owner will update `ko-KR/Resources.resw` separately; `en-US` may later be revised independently. Tests and Publish checks must not freeze resource **values** or the final count of keys.

Scope includes the Controller **left-navigation entry** in `MainWindow.xaml` plus the entire `ControllerPage.xaml` / `ControllerPage.xaml.cs` surface: Steam virtual-controller choice, button mapping, editable actions and their inline hotkey/launch configuration, M1/M2 back-button choices, joystick LED, vibration strength, and manual vibration recovery.

**Do not** change the Device tab, its excluded `DeviceSummaryCard`, other Main App pages, the external Overlay, Runtime, firmware/HID calls, persistent mapping formats, schemas, RPC contracts, routing ownership, or any device operation.

Every change must be **display-only**. Preserve button actions, editable/locked states, item selections, ordering, debounce/drag behavior, physical motor test safeguards, and restore-vibration recovery.

## 1. Mandatory architecture and source review

Read and apply the document-authority precedence in:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`

Current implementation and regression references:

- `docs/work-order/2026-10-14_MAIN_APP_DEVICE_TAB_RESW_PR2_WORK_ORDER.md` — the PR2 resource-extraction model and independent release policy.
- `docs/work-order/2026-10-09_CONTROLLER_VIBRATION_SLIDER_DRAG_UX_FIX_WORK_ORDER.md` — retain continuous dragging, pointer gesture and draft rules.
- `docs/work-order/2026-10-10_A2VM_FAMILY_BOOT_RUMBLE_REARM_LED_VIBRATION_AND_RECOVERY_UI_WORK_ORDER.md` — manual re-arm is explicit recovery, **not** stock authority release.
- `docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_B_CONTROLLER_STEAM_PROFILE_UI_EN_KO_GUIDES_WORK_ORDER_2026-10-11.md` — selector and Steam activity admission policy.
- `docs/work-order/2026-10-13_MAIN_UI_DEVICE_FIRST_DEFERRED_PAGE_WARMUP_AND_ON_DEMAND_CATALOG_WORK_ORDER.md` — do not change lazy Controller page materialization/activation.
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml` and `MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml` and `ControllerPage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/BackButtonMappingUiOptions.cs`
- `src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs`
- `src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs`
- `src/SteamInputAddonforClaw.UI/MainUiStrings.cs`
- `src/SteamInputAddonforClaw.UI/Strings/{en-US,ko-KR}/Resources.resw`
- `scripts/publish-layout.ps1` and `scripts/verify-ui-localization-pri.ps1`
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs`
- `tests/SteamInputAddonforClaw.Tests/FrontButtonUiLayoutTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs`

WinUI 3 localization reference: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings

## 2. Verified current source and exact boundary

### 2.1 Main navigation

`MainWindow.xaml` presently has:

```xml
<NavigationViewItem Content="Controller" Tag="Controller">
```

Unlike `DeviceNavigationItem` (already localized as `MainNav_Device.Content`), the Controller entry has no `x:Uid`. Add one **without changing the Tag, item order, navigation enum, selection handlers, lazy Controller page host, or the startup Device-first flow**.

### 2.2 Controller XAML

`ControllerPage.xaml` is ~211 lines with:

1. Controller heading and subtitle.
2. `SteamVirtualControllerCard`, two-line description, Steam Deck / DualSense Edge selection and `SteamVirtualControllerInfoBar`.
3. `MappingUnavailableText` and the Button Mapping section.
4. `Gamebar Button` + `Center M Button` expanders, each with `Normal` and `Steam Game / Big Picture` domains.
5. `BackButtonMappingExpander` with M1/M2 and an Xbox360-only explanation.
6. `ControllerSettingsHeader`, `ControllerLedExpander`, brightness/color and `Choose color` (conditionally visible).
7. `VibrationStrengthExpander`, two motor rows with Test buttons, `Vibration Recovery` + `Restore Vibration` and a warning tooltip, plus `VibrationStrengthInfoBar`.

Do not modify visibility, parent-child placement, icons, event wires, control size, slider bounds, `IsExpanded` flags, or the Controller LED/vibration hierarchy.

### 2.3 C#-created visible text

`ControllerPage.xaml.cs` is ~1,148 lines and constructs many strings dynamically:

- `SetSteamActivity(bool?)`: active/idle/unknown info.
- `SteamVirtualControllerComboBox_SelectionChanged`: saved choice unchanged / rollback uncertain InfoBar messages.
- `DescribeAction(FrontButtonAction)`: seven selectable action names (`Quick Settings Overlay`, `Steam Big Picture`, `Steam Button`, `Steam Quick Access`, `Keyboard / Hotkey`, `Launch Application`, `Xbox`); the capability list is built from the **shared contract**, not the text.
- Nested `BindingEditor`: `Ctrl`/`Shift`/`Alt`/`Win` key legends, executable path and arguments placeholders, `Run as administrator`, `Browse…`, and the privilege explanation.
- `PopulateBackButtonTargets`: the shared `BackButtonMappingUiOptions.AddTargets` adds `ComboBoxItem.Content` from `BackButtonMappingLabels.GetDisplayName` and stores the actual `Xbox360BackButtonTarget` enum in `Tag`. This helper is also called by **XboxPage**, which is not in this PR.
- Vibration controls: `Restoring…` busy button and `Restore Vibration` reset; local succeeded/unavailable/failed re-arm messages, save/test/refresh-uncertain messages; dynamic percentages and em-dash placeholders.
- Runtime-owned `snapshot.Status`, `result.FailureMessage`, log/event names, file paths, enums and settings keys must remain **unchanged**.

Current `MainUiStrings.Get(key, englishFallback)` and `Format(key, englishFallback, args)` already provide a lazy published-PRI lookup with safe English fallback. **Reuse them**. Do not create a new helper, resource manager, storage system, locale override or background resource-loading task.

## 3. XAML extraction rules

For each Controller-owned fixed visible `Text`, `Content`, `Header` and `Description` value, add a unique `x:Uid` and the matching `<Uid>.<Property>` entry to **both** existing RESW files. Keep the original English property literal for continuity.

Example:

```xml
<NavigationViewItem x:Uid="MainNav_Controller" Content="Controller" Tag="Controller" />

<ctcontrols:SettingsCard
    x:Name="SteamVirtualControllerCard"
    x:Uid="Controller_SteamVirtualControllerCard"
    Header="Steam Virtual Controller" />
```

```xml
<data name="MainNav_Controller.Content" xml:space="preserve"><value>Controller</value></data>
<data name="Controller_SteamVirtualControllerCard.Header" xml:space="preserve"><value>Steam Virtual Controller</value></data>
```

Use unique, meaningful Controller-prefixed keys, including **every** appropriate instance of repeated text `Normal`, `Steam Game / Big Picture` and `Test` because the corresponding XAML elements have independent identity. It is fine for different keys to have the same English value.

Extract the following fixed XAML messages (inventory is a minimum, not permission to skip other user-facing literals):

| Region | Required visible strings |
| --- | --- |
| Navigation | `Controller` nav item (keep `Tag="Controller"`) |
| Page | `Controller` title; `Configure controller buttons and controller-specific settings.` |
| Virtual selector | `Steam Virtual Controller`; `Choose the virtual controller used by Steam.`; initial activity explanatory line; `Steam Deck (Default)`; `DualSense Edge` |
| Mapping | `Unavailable`; `Button Mapping`; `Gamebar Button` + description; `Center M Button` + description; `Normal` and `Steam Game / Big Picture` on both expanders |
| Rear buttons | `M1 / M2`; Xbox360-only/Steam R4-L4 explanation; `M1` and `M2` headers |
| LED | `Controller Settings`; `Joystick LED`; static color/brightness description; `Brightness`; `Color`; `Choose color` |
| Vibration | `Vibration Strength` + description; `Left Motor` / `Right Motor`; both `Test` buttons; `Vibration Recovery`; `Restore Vibration` |
| Recovery help | Full `RestoreVibrationButton` tooltip: `Temporarily disconnects virtual controller input. Save and close your game first.` |

For a native attached tooltip property, **do not assume** an `x:Uid` key like `Controller_RestoreVibrationButton.ToolTipService.ToolTip` will be indexed/looked up correctly by the existing publish-verifier path (`Text|Content|Header|Description` are its current recognized XAML suffixes). Safest narrow option: preserve the XAML tooltip literal, and once in the Controller page constructor after `InitializeComponent()` apply the localized text via `ToolTipService.SetToolTip(RestoreVibrationButton, MainUiStrings.Get(...))`. Then use a plain `Controller_...Tooltip` RESW key, not a speculative attached-property syntax. Keep the same tooltip semantics, including **no confirmation dialog** added to recovery.

`Steam Deck`, `DualSense Edge`, `Xbox`, `M1`, `M2` and protocol/button nomenclature remain identical English initially. Numeric percentages, the em dash `—`, `A/B/X/Y`, `PL1/PL2`, and non-prose technical keycaps are not required to be separately translated.

Do not put `x:Uid` on sliders, framework layout elements, controls without translatable properties, or any `Tag`/`x:Name` used by selection and event wiring.

## 4. C# text extraction — mandatory

### 4.1 Steam virtual-controller selection

Localize the three `SetSteamActivity` display strings and the two local selector-mutation InfoBar messages. Preserve exactly:

- `_steamActive == false` is required to edit;
- `true` and `null` block editing;
- `ComboBoxItem.Tag` values `SteamDeck` / `DualSenseEdge` are **not translated** and still parse into `SteamVirtualControllerPreference`;
- mutation guard and rollback re-read; `SteamActivityRefreshRequested` propagation;
- preexisting `SteamVirtualControllerInfoBar` warning behavior.

Do not modify Steam profile's separate per-game selector (`ProfilePage` is a later PR).

### 4.2 Front-button mapping actions

Replace only the human-readable `DescribeAction` branches by RESW lookups. Keep `FrontButtonAction` enum instances as the `ComboBoxItem.Tag`, and keep `FrontButtonActionCapabilities.ActionsFor(domain)` as the **sole source** for offered actions and order.

Example:

```csharp
private static string DescribeAction(FrontButtonAction action) => action switch
{
    FrontButtonAction.XboxApp =>
        MainUiStrings.Get("Controller_Action_Xbox", "Xbox"),
    // Other known actions use their corresponding English fallback.
    _ => action.ToString()
};
```

Do not add a `None` option, allow same-domain duplicate assignments, remove Win+G restrictions, change actions' capabilities, or touch mapping persistence/`MainWindow.QueueFrontButtonMutation` and save-drain behavior.

### 4.3 Inline BindingEditor

Localize `Application path` and `Arguments (optional)` placeholders, `Run as administrator`, `Browse…`, and the complete admin/normal-privilege explanation.

The physical keyboard modifier labels `Ctrl`/`Shift`/`Alt`/`Win` may remain the existing **keycap identifiers**: these are not sentences. Likewise the `FrontButtonHotkeyKey` values supplied by `Enum.GetValues` (`A`, `PageUp`, `F1`, etc.) are physical-key identifiers and must not be changed, re-sorted or translated through their underlying enum/`Tag`. Do **not** turn visible labels or placeholder text into the stored hotkey value.

If localization for special-key names is needed later, it must be display-only; this PR does not create a keyboard-layout-aware translation framework.

Keep `BindingEditor` nested within ControllerPage and do not touch its `Load/Capture`, `RefreshHotkeyAvailability`, `DisablePartnerAction`, executable file picker, modifier events or callbacks.

### 4.4 M1/M2 back-button targets — avoid Xbox tab side effects

`BackButtonMappingUiOptions.AddTargets` is shared with `XboxPage`, so **do not globalize its labels by editing the shared contract or changing its default output in this Controller-only PR**.

In ControllerPage's existing `PopulateBackButtonTargets()`, continue to call `BackButtonMappingUiOptions.AddTargets` and then apply display-only localization to `ComboBoxItem.Content` based on the existing typed `Tag`. For each valid `Xbox360BackButtonTarget`, obtain the **original visible fallback** from `BackButtonMappingLabels.GetDisplayName(target)` and a stable Controller-specific key, e.g.:

```csharp
private static void LocalizeControllerBackTargets(ComboBox comboBox)
{
    foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
    {
        if (item.Tag is not Xbox360BackButtonTarget target) continue;
        item.Content = MainUiStrings.Get(
            $"Controller_BackTarget_{target}",
            BackButtonMappingLabels.GetDisplayName(target));
    }
}
```

Use this only after the two `AddTargets` calls **and before** `ApplyBackButtonMapping` selects the saved targets. Keep typed `Tag`, enum order, current M1/M2 mapping, `SelectTarget` and `BackButtonMappingEditRequested` behavior unchanged. Verify matching `Controller_BackTarget_<EnumName>` keys exist for **all 18** enumerated targets (e.g. `Disabled`, `DPadUp`, `LeftBumper`, `XboxGuide`). This is UI display localization, **not** a change to the shared Contracts label table.

### 4.5 Joystick LED

`ControllerLedExpander` lives under the preexisting conditionally visible Controller Settings surface. Extract its XAML strings whether or not the current machine makes the LED group visible. Do not change LED availability, enabled toggle, color/brightness persistence, `ColorPicker` semantics, swatch, or status presentation. No LED firmware/protocol changes.

### 4.6 Vibration and Restore Vibration

Localize **locally authored, visible** messages in:

- `RestoreVibrationButton_Click`: `Restoring…` temporary button and `Restore Vibration` final button. Use the same `Controller_RestoreVibrationButton.Content` resource key for the initial XAML and the final C# reset; if the Korean value changes later, the restored caption must match the initial caption.
- Re-arm result messages: success (`Controller restored. Test both motors to confirm vibration.`), temporarily unavailable, and failure (`Could not restore the controller. See the application log.`).
- `CommitVibrationStrengthAsync`: local apply-now failure fallback and uncertain-save fallback.
- `TestVibrationMotorAsync`: local physical-test failure fallback and uncertain-test fallback.
- `RefreshVibrationStrengthAsync`: locally created `The saved vibration setting could not be loaded.` text (both the `Unavailable(...)` snapshot passed by the UI and the visible InfoBar message); keep them consistent.

**Never modify** `result.Status`/`snapshot.Status`/`result.FailureMessage` passthrough, or anything supplied by the Runtime. Existing authoritative failure-message precedence remains.

**Critical safety invariants:**

- Manual `RunDeveloperRumbleRearmAsync()` is an explicit recovery action, **not** Center M enabling, stock PID1901 release, or a general reset.
- `CanStartVibrationRearm` conditions and pending-edit/test guard remain unchanged; the UI cannot start a recovery while a pointer gesture, draft save, motor test, or recovery is in progress.
- `CanShowRumbleRearmMessage` preserves any preexisting open **error**; re-arm messages do not overwrite it.
- Leave `AppLog.Info` / `AppLog.Warn`, event names and result status fields English and unchanged.
- Vibration test only uses the existing `TryStartVibrationMotorTest` guards; do not alter test admission, controller state authority, or severity/failure precedence.
- Do not touch pointer event subscriptions, 300ms settling, slider drag/gesture tracking, draft preservation, async mutation flow, `Deactivate()` / `Unloaded` cancellation, or shutdown handling.
- Leave `LeftVibrationStrengthPercentText` / `RightVibrationStrengthPercentText` output `NN%` and `—` as invariant numeric/unit format.

All initial en-US/ko-KR values remain the **exact current English user-visible text**, including punctuation and ellipsis character (`…`). Never modify text content merely to improve English wording.

## 5. Resource organization and runtime behavior

- Append meaningful `Controller_*` keys plus `MainNav_Controller.Content` to the **existing** two RESW files; do not add a third language or a Controller-specific RESW tree.
- `x:Uid` property keys use `Name.Text`, `Name.Content`, `Name.Header` or `Name.Description`, as applicable.
- Dynamically generated strings/tooltip/back-button values use direct plain keys and `MainUiStrings.Get(key, englishFallback)`.
- The source English and Korean dictionaries must have the same unique keys and nonempty values, **not** forcibly equal values in later builds.
- Do not instantiate `ResourceManager` directly in ControllerPage or copy the old Settings PR1 implementation; reuse `MainUiStrings` from PR2.
- Do not put RESW values into enum fields, `Tag`, `x:Name`, serialized action/settings values, or user-editable executable path/arguments.
- Do not add a language selector, app-level `PrimaryLanguageOverride`, app preference store, localization events, page reload, or Runtime-assisted localization.
- The UI is disposable and may be reopened repeatedly; the existing lazy Controller page construction is preserved. A lookup failure falls back to English and must not block page construction or controller interaction.
- Preserve existing admin/high-integrity Runtime-owned launches and external file picker behavior. All labels are display-only.
- No extra polling, state authority, synchronization, wrappers, or new project dependencies.

## 6. PRI / release and source validation

PR2 already extended `scripts/verify-ui-localization-pri.ps1` to enumerate **every source key** from `en-US` and `ko-KR`, compare key sets, and verify actual published UI PRI contains one nonempty candidate of each supported language with `en-US` as default.

Therefore **do not extend a hardcoded URI inventory per tab**. New Controller keys should pass the current script automatically. Modify that script **only** if a concrete new key type cannot be resolved by the existing `Text|Content|Header|Description` path logic (preferred tooltip handling in `3` intentionally avoids this).

Validation:

1. All Controller `x:Uid` properties resolve to actual source keys; no duplicate UID/key, no missing language candidate.
2. Every Controller-local C# key, including conditionally selected labels, dynamic back-target enum names and the restore-button reset key, is present in both RESW files. Add focused tests for dynamic key constructions that ordinary string-regex scans miss.
3. New resource candidates actually appear in published `ui/SteamInputAddonforClaw.UI.pri`, not only in source RESW.
4. Do not manually generate/overwrite PRI with `makepri new` or change existing WinUI XBF preservation, Velopack layout, `WindowsPackageType=None` or external Windows App SDK runtime.
5. No need to change CI or release workflow YAML if the existing shared `publish-layout.ps1` validation works.

## 7. Tests — preserve behavior-oriented assertions

Update tests only when current English string assertions are made stale by RESW extraction, while preserving the underlying behavior assertions.

**Important existing tests verified in current main:**

- `tests/SteamInputAddonforClaw.Tests/FrontButtonUiLayoutTests.cs` asserts Gamebar precedes Center M, two domains, no retired WING/`None` options, shared capability catalog and Win+G prohibition. Its source assertion for `FrontButtonAction.XboxApp => "Xbox"` must be updated to the new localized display form **without relaxing the enum/capability contract**.
- `tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs` asserts Controller global Steam selector order, literal tags `SteamDeck` / `DualSenseEdge`, idle-state mutation rules and profile separation. Preserve these checks; extend resource assertions for relevant labels.
- `tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs` explicitly checks literal `RestoreVibrationButton.Content = "Restoring…"` and recovery text. Update them to assert that the same keys are used in busy/final transitions, while retaining all safety guards, status logging, error-preservation and RPC-result checks.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` validates the Controller page's composition / UI ownership / quick-settings lifecycle and must continue passing.
- `tests/SteamInputAddonforClaw.UiTests/XboxCatalogPageUiTests.cs` depends on the shared `BackButtonMappingUiOptions.AddTargets` behavior. Verify Xbox remains untouched; do not translate shared options as a side effect.
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs` verifies RESW key parity, supported language folders, and sample source-to-PRI shape. Add Controller navigation/XAML and dynamic-key coverage rather than pinning a fixed overall key count or specific translated values.

Recommended new tests:

- Unique Controller XAML UIDs, expected property key coverage, and unchanged stable navigation `Tag` / action `Tag` values.
- Controller action label key mapping includes all seven enum variants and retains capabilities/normal+Steam action order.
- All `Xbox360BackButtonTarget` enum labels have corresponding `Controller_BackTarget_<name>` resource keys, but Xbox shared helper/contract behavior is unchanged.
- Controller `BindingEditor` preserves `Tag` on action/key values, modifier and Win+G guards, and the launch-application privilege flag.
- Steam selector active/unknown locks and uncertain-write rollback; localization changes messages only.
- Vibration busy/reset captions use the same resource key as the initial button, error-priority handling and save/test/re-arm admission remain.
- English fallback succeeds for a missing Controller key (`MainUiStrings.Get`); no physical hardware needed.

Use the existing framework and test folders; do not construct a generic UI test harness or claim actual device behavior without evidence.

## 8. Acceptance criteria and release independence

**Before PR completion, prove in code/build/tests:**

- `en-US` and `ko-KR` contain matching nonempty keys; new values are initially **identical original English**.
- Controller navigation and all nontechnical Controller page-owned prose is connected to RESW, including dynamic editors/options, error/info labels and recovery tooltip.
- All prior Device tab localization and its **excluded DeviceSummaryCard** remain untouched.
- Front-button and M1/M2 mapping selected enum/Tag identities, partner disable rules, no Win+G, persisted payload, and MainWindow save-chain semantics remain unchanged.
- Steam virtual-controller mutation admission, selection fallback, and presentation-profiles mapping remain unchanged.
- LED and vibration control policy, guards, drafts, tooltip/recovery semantics, and Runtime-owned errors remain unchanged.
- All previously existing tests and new focused UI resource tests pass.
- Build/restore the solution and relevant UI/Core test projects in Release; run existing CI checks; execute `scripts/publish-layout.ps1` and `scripts/verify-publish-assets.ps1`, including **real published PRI** verification for both languages.
- Standalone PR3 can ship without waiting for Steam, Xbox, ClawHUD, Shortcut, Tweaks, Settings, How to Use or Developer localization.

Physical MSI Claw verification (including controller mapping, DualSense selection, motor tests, manual re-arm, sleep/resume and PID1902 re-enumeration) belongs to the **user after merge** and is **not** a local Codex implementation mandate or PR blocker.

## 9. Expected changed files

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/ControllerVibrationStrengthUiTests.cs
tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs         (if needed)
tests/SteamInputAddonforClaw.Tests/FrontButtonUiLayoutTests.cs
other directly affected UI source-contract tests                         (only if needed)
```

**Do not change:** `src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs`, `BackButtonMappingUiOptions.cs`, `SteamInputAddonforClaw.Contracts/*`, the separate Overlay, Runtime, `DeviceSummaryPresentation.cs` or settings/persistence, unless a proven compile/test requirement invalidates this strictly display-only plan.

## 10. Handoff

Local Codex should deliver one PR with a concise description of converted Controller regions and any intentionally invariant key legends, resource-key parity and actual published-PRI proof, full build/test/publish command results, and confirmation that **no behavior/lifecycle changes** are included. It must not require on-device testing from Codex. If the current HEAD differs materially, use the smallest correction and explain it in the implementation PR instead of enlarging the architecture.

This commit supplies the **work order only**; implementation remains local Codex's responsibility.
