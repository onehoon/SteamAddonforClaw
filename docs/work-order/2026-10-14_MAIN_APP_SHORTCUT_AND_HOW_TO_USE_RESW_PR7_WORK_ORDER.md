# Work Order — Main App RESW PR7: Shortcut Editor + How to Use Shell

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation:** Local Codex; physical handheld verification: user after merge  
**Starting point:** Latest `main` after PRs #785–#790 (Main UI localization foundation, Device, Controller, Steam, XBOX, ClawHUD). Re-read current HEAD before editing.  
**Delivery:** **One focused PR7** comprising the entire Main App Shortcut page/editor plus the three remaining `How to Use` shell strings.  
**Physical validation policy:** Do **not** require Codex or PR CI to run tests on an MSI Claw. Merge decisions are made from code and automation; the user performs real-device testing afterward.

## 0. Product scope and locked policy

Only the **standalone Full PID1902 Addon Main App WinUI3 UI** is being localized. This work order is **not** a request to change the in-game Addon Quick Settings Overlay (QAM), the external ClawHUD in-game renderer, any standalone apps or CTW.

Preserve the existing two-language resource system:

- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`
- `src/SteamInputAddonforClaw.UI/MainUiStrings.cs` with `Get(key, englishFallback)` and `Format(key, englishFallback, args...)`
- unpackaged Windows App SDK `SteamInputAddonforClaw.UI.pri` with Windows language selection.

All **new English and Korean resource values must initially be the existing English**. No actual Korean translation, settings language selector, custom culture manager, local cache, new generic localization service or runtime restart logic.

**Critical distinction specific to Shortcut:** UI **display labels** may be translated; **persisted Shortcut Title, TileId, action kind/type, executable/script/URL parameters, screenshot folder, icon glyph, reorder index, and user-entered text must not be rewritten**. A localized action-picker caption is not a new persisted action type.

What to translate:

1. Main navigation `Shortcut` and `How to Use` captions.
2. Shortcut `ShortcutPage.xaml` fixed user-facing labels, empty states, tile buttons and applicable accessibility names.
3. All Main App Shortcut editor `ContentDialog` text, dynamically constructed input labels, hints, built-in action descriptions, action choice captions, icon chooser names, tooltips, and locally authored InfoBar/error messages.
4. **Fixed built-in target summaries** on Main App Shortcut tiles, selected from the already typed `FrontendShortcutEditorActionKind` without modifying producer data; retain all dynamic/raw producer fields.
5. How to Use error `How to Use could not be loaded.` and `Retry` (existing `Loading documentation…` RESW already done).

What **not** to translate or alter:

- Runtime-origin `FrontendShortcutEditorSnapshot.FailureMessage`, `FrontendShortcutMutationResult.FailureMessage`, `FrontendShortcutEditorAction.ValidationMessage`, unsupported `TypeId`, and raw errors.
- User names and stored `FrontendShortcutEditorTile.Title`, script contents, executable paths, URL/host, file names, folder paths and the existing **persisted default-title policy**.
- Separate Addon `src/SteamInputAddonforClaw.Overlay/**`, Runtime `src/SteamInputAddonforClaw/Shortcuts/**` and `src/SteamInputAddonforClaw.Contracts/**`.
- How to Use downloaded Markdown content, source URL selection and WebView2 link/navigation implementation.
- `AppLog` event names/messages, internal `Tag`/enum/mutation identity, layout/visual behavior and unsupported future/multi-session features.

Do not add a `ShortcutViewModel`, `ShortcutTranslator` abstraction hierarchy, wrapper RPC protocol or concurrent editor state machine. One or two **short existing-page-local display helpers** are sufficient.

## 1. Mandatory reading: architecture and prior RESW contracts

Start with:

1. `docs/Full 1902 Implementation/README.md`, then documents in the actual precedence order.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
6. `docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md`.
7. `docs/work-order/SHORTCUT_FOUNDATION_PR_C_RUNTIME_EXTERNAL_ACTION_ENGINE_WORK_ORDER_2026-09-24.md`.
8. `docs/work-order/SHORTCUT_FOUNDATION_PR_D_MAIN_APP_SHORTCUT_EDITOR_WORK_ORDER_2026-09-24.md`.
9. `docs/work-order/SHORTCUT_FOUNDATION_PR_E_DYNAMIC_OVERLAY_SHORTCUT_GRID_WORK_ORDER_2026-09-24.md`.
10. `docs/work-order/2026-10-13_OVERLAY_SHORTCUT_VERTICAL_ICON_TITLE_AND_XBOX_DEFAULT_LABEL_WORK_ORDER.md`.
11. `docs/work-order/2026-10-14_MAIN_APP_XBOX_TAB_RESW_PR5_WORK_ORDER.md`.
12. `docs/work-order/2026-10-14_MAIN_APP_CLAWHUD_TAB_RESW_PR6_WORK_ORDER.md`.
13. `docs/work-order/2026-10-14_MAIN_UI_RESW_LOCALIZATION_PR1_DEPLOYMENT_SAFE_FOUNDATION_WORK_ORDER.md`.

The archived Shortcut design documents provide intent. **Current main source/test contracts win where older plans differ.** Ignore obsolete CTW integration passages.

Implementation files to inspect before editing:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutIconCatalog.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs                 (read only)
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/MainNavigationIconContractTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs             (regression reference)
tests/SteamInputAddonforClaw.Tests/ShortcutIconCatalogTests.cs         (regression reference)
```

References, for implementation semantics only:

- Microsoft WinUI x:Uid RESW property resources: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings
- Microsoft WinUI `x:Bind` static function calls, including in typed `DataTemplate`: https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/function-bindings

## 2. Verified current source inventory: why this tab is different

`ShortcutPage.xaml`: **145 physical lines**, page title, subtitle, Add Shortcut button, InfoBar, three-column ListView of repeated Shortcut tiles, and empty state. Tile template binds:

```xml
<TextBlock Text="{x:Bind Title}" />
<TextBlock Text="{x:Bind TargetSummary}" />
<TextBlock Text="{x:Bind Action.ValidationMessage}" />
<Button Content="Edit" Tag="{x:Bind TileId}" ... />
<Button Content="Delete" Tag="{x:Bind TileId}" ... />
```

The tile `Title` and `Action.ValidationMessage` are **not owned by XAML** and must not be overwritten with a static UID resource. Tile drag-glyph and icon-glyph accessibility labels currently contain English; see `6`.

`ShortcutPage.xaml.cs`: **1,207 physical lines**. `ShowEditorAsync` beginning at about line 127 creates **the entire Add/Edit ContentDialog imperatively in C#**, including icon choices and seven action panels. Do not attempt to "convert the editor into XAML" to use `x:Uid`. Use `MainUiStrings.Get` for dynamically created C# controls.

`ShortcutIconCatalog.cs` (contracts): **32 named icon choices**; each item has a stable glyph and original English `Name`. Both the icon chooser and the icon-preview name use this catalog. The catalog must remain shared, immutable and English; the Main App is responsible for mapping icons to localized **presentation** captions.

`ShortcutRuntime.cs` (read only) projects persisted Shortcut titles and generates `TargetSummary` and `Action.ValidationMessage` in `FrontendShortcutEditorTile`. A subset of target summaries represents well-known built-in actions, while others are free-form file names, URL hosts or unsupported contract details; do not translate a raw `TargetSummary` indiscriminately.

`HowToUsePage.xaml`: **28 physical lines**, `HowToUse_Loading.Text` already RESW-backed. Only nav caption, failure TextBlock and Retry Button remain. `HowToUseMarkdownRenderer.ResolveSource(CultureInfo.CurrentUICulture)` already selects:

- Korean: `docs/howtouse/README_KO.md` from GitHub raw;
- non-Korean: root `README.md` from GitHub raw.

Do not alter this document content, network workflow, WebView2 initialization/retry gating or link security.

## 3. MainNavigationView and static Shortcut/HowToUse XAML

### 3.1 Navigation labels only

`MainWindow.xaml` currently includes:

```xml
<NavigationViewItem Content="Shortcut" Tag="Shortcut">
<NavigationViewItem Content="How to Use" Tag="HowToUse">
```

Add stable distinct x:Uid values, e.g.:

```xml
<NavigationViewItem x:Uid="MainNav_Shortcut" Content="Shortcut" Tag="Shortcut">
<NavigationViewItem x:Uid="MainNav_HowToUse" Content="How to Use" Tag="HowToUse">
```

RESW source keys:

```text
MainNav_Shortcut.Content = Shortcut
MainNav_HowToUse.Content = How to Use
```

**Never change Tag, ordering, icons or `MainNavigationPage.Shortcut` / `MainNavigationPage.HowToUse`.** Keep English literal source Content in XAML as fallbacks.

### 3.2 Static Shortcut page

Add x:Uid only to existing fixed-text UI elements; keep source English fallback and all structure. Suggested keys:

| Existing source text | Suggested key |
| --- | --- |
| `Shortcut` title | `Shortcut_PageTitle.Text` |
| `Create and organize actions shown in the Shortcut overlay page.` | `Shortcut_PageSubtitle.Text` |
| `＋ Add Shortcut` | `Shortcut_AddButton.Content` |
| `Edit` (tile template button) | `Shortcut_EditButton.Content` |
| `Delete` (tile template button) | `Shortcut_DeleteButton.Content` |
| `No shortcuts yet.` | `Shortcut_EmptyTitle.Text` |
| `Add a Shortcut to get started.` | `Shortcut_EmptySubtitle.Text` |

Keep `ShortcutCardTemplate` a typed `DataTemplate x:DataType="frontend:FrontendShortcutEditorTile"`, tile `Tag="{x:Bind TileId}"`, `Action.Editable` binding, action summary/validation binding policy (see `5`), 3-column `ItemsWrapGrid`, drag surface and animations exactly intact.

Repeated template items can share one static `x:Uid` per templated element; avoid constructing new per-instance resource IDs. Do not assign one UID to the entire card because some children have data-bound `Text` that must remain authoritative.

### 3.3 How to Use shell only

`HowToUsePage.xaml` needs x:Uid and RESW for:

```text
HowToUse_Failure.Text   = How to Use could not be loaded.
HowToUse_Retry.Content  = Retry
```

Already present: `HowToUse_Loading.Text = Loading documentation…`. Do not duplicate it, change its name or introduce C# lookup for it.

**No changes needed** to `HowToUsePage.xaml.cs` or `HowToUseMarkdownRenderer.cs`. Preserve the remote Markdown URL selection (Korean/English), lazy WebView2, navigation correlation, `RetryButton_Click` and source log semantics.

## 4. Imperative Add/Edit dialog: localize all UI-created text

`ShowEditorAsync(FrontendShortcutEditorTile? existing)` creates WinUI controls and shows an Add/Edit `ContentDialog`. Replace every **user-facing constant** in the following inventory with a `MainUiStrings.Get("Shortcut_...", exactEnglish)` lookup; use stable, context-specific keys and reuse a key where the exact semantic context is identical. Avoid duplicating the same phrase in unrelated format keys.

### 4.1 General dialog, icon actions and action type choices

| Group | Exact existing English source |
| --- | --- |
| Title input | `Title`, `Shortcut name` |
| Action picker header | `Action type` |
| Icon field | `Icon`, `Change icon`, `Use default` |
| Action type options | `Application (.exe)`, `PowerShell`, `Website (URL)`, `Steam Big Picture`, `Steam`, `XBOX`, `Screenshot` |
| Close toggle | `Close Overlay after launch`, `Close Overlay`, `Keep Overlay open` |
| Dialog | `Add Shortcut` / `Edit Shortcut`, `Save`, `Cancel` |

**Selection identity is never the localized label:** keep every `ComboBoxItem.Tag = FrontendShortcutEditorActionKind` and action ordering/existence rules, including Screenshot only once.

Example:

```csharp
AddActionChoice(
    actionPicker,
    MainUiStrings.Get("Shortcut_Action_Executable", "Application (.exe)"),
    FrontendShortcutEditorActionKind.Executable);
```

Do not change the `AddActionChoice` helper's use of `Tag = kind` or infer a kind by comparing `Content` text.

### 4.2 Inputs and hints, by action

| Panel | Exact current source strings |
| --- | --- |
| Executable | `Executable path`, `C:\Path\Application.exe`, `Browse…`, `Arguments`, `Optional arguments`, `Run as administrator` |
| PowerShell | `Run as administrator`, `Script`, `Enter a PowerShell script` |
| Both privileged panels | `Runs with administrator privileges; otherwise requests normal user privileges.` |
| Website | `URL`, `https://…` |
| Screenshot | `Browse…`, `Use default`, `Open folder`, `Captures the primary display as JPEG.`, `Save folder` |
| Built-ins | `Opens Steam Big Picture.`, `Opens the Steam client.`, `Opens the XBOX app.` |

Technical format placeholders (`C:\Path\Application.exe`, `https://…`) may remain literal as clearly illustrative paths/schemes; their input `Header` and explanatory `PlaceholderText` (e.g. `Optional arguments`) must be RESW-backed. If moving example placeholders into RESW for complete coverage, keep exact defaults and do not interpret them as actual paths or URLs.

`ResolveScreenshotFolder` returns user/system paths and **must not** localize folder names or change the fallback path `Pictures\Screenshots`.

Do not change executable launch privilege behavior, PowerShell execution, selected screenshot folder staging, `_dialogOpen`, `XamlRoot`, WinUI file/folder pickers, or built-in action kind mapping.

### 4.3 Add/Edit dialog default titles are stored data — KEEP ENGLISH

**Do not localize or modify this method:**

```csharp
internal static string? GetDefaultTitle(FrontendShortcutEditorActionKind kind) => kind switch
{
    FrontendShortcutEditorActionKind.SteamBigPicture => "Steam Big Picture",
    FrontendShortcutEditorActionKind.SteamClient => "Steam",
    FrontendShortcutEditorActionKind.XboxApp => "XBOX",
    FrontendShortcutEditorActionKind.ScreenshotFullscreen => "Screenshot",
    _ => null
};
```

The editor uses `GetDefaultTitle` to populate `titleBox.Text` **only while creating a new shortcut and only if the user has not overridden the suggested title**. That title is eventually passed as `FrontendShortcutMutationIntent.Title`, persisted to `shortcuts.json`, and displayed in the separate Addon Overlay.

Localization of `"Steam"`/`"XBOX"`/`"Screenshot"` in the *action choice* is not permission to change this stored default title. Do not translate the result of `GetDefaultTitle`, retroactively rename old shortcuts, alter `ShouldApplyDefaultTitle`, or special-case Korean persisted titles. The user can rename a shortcut manually.

Existing fresh-install default tiles are persisted as `Screenshot`, `Steam`, `Steam Big Picture` and `XBOX`; they also must remain unchanged.

## 5. Main App tile TargetSummary: local fixed summaries only

Existing data template shows `{x:Bind TargetSummary}` from the **Runtime projection**. No blanket replacement or textual matching/rewrite of arbitrary Runtime data is allowed. Existing producer behavior in `ShortcutRuntime.GetEditorTargetSummary`:

| Kind | Runtime summary | Main App PR7 behavior |
| --- | --- | --- |
| `SteamBigPicture` | `Steam Big Picture` | Known fixed summary — may localize by typed kind |
| `SteamClient` | `Steam client` | Known fixed summary — localize by typed kind |
| `XboxApp` | `XBOX app` | Known fixed summary — localize by typed kind |
| `ScreenshotFullscreen` | `Fullscreen screenshot` | Known fixed summary — localize by typed kind |
| `PowerShell` | `PowerShell` | Known fixed summary — localize by typed kind |
| `Executable` | executable file name or `Application` | **Keep raw TargetSummary** (not necessarily static) |
| `Url` | URL host or `Website` | **Keep raw TargetSummary** (not necessarily static) |
| `Unsupported` | `Unsupported in this version · <TypeId>` | **Keep raw TargetSummary** and embedded type ID |

Implement the known fixed cases **locally in the Main App**, with typed `tile.Action.Kind`, a short page-local display function and simple static `x:Bind` function call inside the existing typed `ShortcutCardTemplate`. Do not modify the contracts, projected tile record, persisted `Title`, Runtime summary generation or the in-game Overlay.

Illustrative supported WinUI x:Bind function-binding form:

```xml
xmlns:views="using:SteamInputAddonforClaw.Views"
...
<TextBlock Text="{x:Bind views:ShortcutPage.GetDisplayTargetSummary(Action.Kind, TargetSummary)}"
           TextTrimming="CharacterEllipsis" />
```

Implement `GetDisplayTargetSummary` as a **single** `ShortcutPage` static method (expose only as required by generated XAML; do not introduce a converter class). Use `MainUiStrings.Get` and return the original `sourceSummary` for `Executable`, `Url`, `Unsupported` and any unexpected/future action kind.

Example switch branch:

```csharp
FrontendShortcutEditorActionKind.SteamClient =>
    MainUiStrings.Get("Shortcut_Target_SteamClient", "Steam client"),
```

**Important:** treat those as UI projection choices by known typed action kind, not a mutation of authoritative runtime `TargetSummary`. Ensure the static function binding compiles in the current WinUI generated binding code; if implementation-specific syntax differs, use the simplest equivalent local presentation binding without a new ViewModel/converter or mutation of `FrontendShortcutEditorTile`.

Keep `Text="{x:Bind Title}"` and `Text="{x:Bind Action.ValidationMessage}"` **unchanged**. Unsupported validation messages are Runtime-provided and remain raw. The same `ShortcutCardTemplate` drives normal cards **and drag preview**; both must display the same translated fixed summary.

## 6. Icon chooser: 32 contract names, stable glyph identity, accessibility

### 6.1 Display names only; never change the catalog

`ShortcutIconCatalog.Choices` is a shared 32-entry contract with `ShortcutIconChoice(Name, Glyph)`. The catalog is in `SteamInputAddonforClaw.Contracts`, **not** the UI; cannot directly add `MainUiStrings` calls there.

The Main App uses:

- icon picker `choice.Name` TextBlock caption;
- `AutomationProperties.SetName(choiceButton, choice.Name)`;
- `ToolTipService.SetToolTip(choiceButton, choice.Name)`;
- `ShortcutIconCatalog.GetEffectiveName(kind, stagedIconGlyphOverride)` for the preview caption;
- `AutomationProperties.SetName(iconPreview, $"{iconName.Text} Shortcut icon")` for preview accessibility.

Resolve one localized name per **stable glyph**, without modifying `choice.Name` or using a translated label as identity. Prefer a simple page-local method using a stable **glyph-hex suffix**:

```csharp
private static string GetLocalizedIconName(ShortcutIconChoice choice) =>
    MainUiStrings.Get(
        $"Shortcut_Icon_{(int)choice.Glyph[0]:X4}",
        choice.Name);
```

For preview, obtain the already selected **effective glyph** from `ShortcutIconCatalog.GetEffectiveGlyph`, then look up the corresponding authoritative choice via `TryGetChoice` (the catalog's validated fallback glyph handles missing overrides). Pass that choice through the same `GetLocalizedIconName`.

Add matching entries for all **32 distinct catalog glyphs** to `en-US` and `ko-KR` with their original `choice.Name` English values. Examples:

```text
Shortcut_Icon_E71D = All apps
Shortcut_Icon_E713 = Settings
Shortcut_Icon_E722 = Camera
```

**Verify the full glyph/name catalog in the current source**; examples above are not an exhaustive list. No positional `Choices[index]` translation identity, duplicate dictionary of English names in a second manager, text-based glyph matching or contract enum changes.

Use the one resolved name consistently for picker text, picker tooltip, picker automation label and preview caption. `stagedIconGlyphOverride` and `IconGlyphOverride` must remain glyphs. Preserve `ShortcutIconCatalog.GetDefaultGlyph`, effective-glyph fallback, picker grid columns, flyout staging, `Use default` override clearing and selection click behavior.

### 6.2 Accessibility text is also Main App UI

The template currently contains:

```text
AutomationProperties.Name = Drag to reorder this Shortcut
AutomationProperties.Name = Shortcut icon
```

and the preview uses `"<localized icon name> Shortcut icon"`. These phrases are locally authored accessibility text, not persisted contract data.

Localize them using the simplest verified WinUI route:

- For static attached `AutomationProperties.Name` in XAML, the official WinUI RESW attached-property form is `<Uid>.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name`. Verify the exact emitted property resource path in the published PRI, because the existing verifier only recognizes `Text|Content|Header|Description|PlaceholderText` dotted keys. **If using this form, narrowly extend the existing verification script to handle these two known attached resources and their language candidates; do not silently skip them.**
- Alternatively, use a static `x:Bind` function on `ShortcutPage` that returns `MainUiStrings.Get` for the two labels, preserving the original English fallback in C# and testing generated bindings. Prefer whichever compiles cleanly **without creating a new generic localization system**.
- Use a **whole-sentence** local format key for preview automation, e.g. `Shortcut_IconPreview_A11y = {0} Shortcut icon`:

```csharp
AutomationProperties.SetName(
    iconPreview,
    MainUiStrings.Format(
        "Shortcut_IconPreview_A11y",
        "{0} Shortcut icon",
        localizedIconName));
```

Do not concatenate untranslated fragments around a translated name. Preserve the drag gesture, drag glyph vs action glyph distinction and UI automation names associated with their original controls.

## 7. Add/Edit/Delete operations: all locally-authored errors and prompts

The following existing page-authored English strings must be keyed through `MainUiStrings.Get`, reusing keys at multiple existing handlers when the sentence is identical.

| Source place | Exact current UI-authored string |
| --- | --- |
| `CaptureAndRenderAsync` and `ShowEditorAsync` capture exception | `Shortcut settings could not be loaded.` |
| `Render` local availability fallback | `Shortcut editing is unavailable.` |
| `ShowEditorAsync` stale tile | `Shortcut tile was not found.` |
| Browse Screenshot picker | `The folder picker could not be opened.` |
| Screenshot folder absent | `The effective Screenshot folder is unavailable.` |
| Open Screenshot folder failure | `The Screenshot folder could not be opened.` |
| `BrowseForExecutableAsync` | `The application picker could not be opened.` |
| `ApplyMutationAsync` oversized local payload | `One or more fields exceed the supported Shortcut editor size.` |
| `ApplyMutationAsync` mutation failure/catch | `Shortcut changes could not be saved.` |
| `DeleteTileButton_Click` dialog title | `Delete Shortcut?` |
| `DeleteTileButton_Click` dialog body | `Delete ‘{tile.Title}’? This cannot be undone.` |
| `DeleteTileButton_Click` buttons | `Delete`, `Cancel` |

**Use `MainUiStrings.Format` for the deletion body, with one whole-sentence resource and the user-supplied title as a parameter.**

```csharp
Content = MainUiStrings.Format(
    "Shortcut_Delete_ConfirmationBody",
    "Delete ‘{0}’? This cannot be undone.",
    tile.Title),
```

Keep the exact curly-quotes English result initially, the actual user title unchanged, and the `ContentDialogResult.Primary` gate. Do not interpolate source text and then attempt translation on the combined, user-dependent string.

Error handling semantics must remain intact:

```csharp
ShowMessage(
    result.FailureMessage
    ?? MainUiStrings.Get("Shortcut_SaveFailed", "Shortcut changes could not be saved."),
    InfoBarSeverity.Error);
```

Preserve current `snapshot.FailureMessage ?? localFallback`, `result.FailureMessage ?? localFallback`, InfoBar severity, failed refresh/retry behavior and original null-coalescing policy. **Never translate `snapshot.FailureMessage`, `result.FailureMessage` or `Action.ValidationMessage` by string comparison.**

Do not convert `AppLog` strings (including reorder lifecycle `"Canceled"`, `"CaptureLost"`, `"Unavailable"`, `"Succeeded"`, `"Rejected"`, `"RuntimeRejected"` and telemetry keys) to RESW.

## 8. Absolutely preserve Shortcut functionality and ordering

This work is **display-only** and must not disturb supported product paths.

### Editor / persistence

- `_active`, `_dialogOpen`, `_operationInProgress`, `_refreshInProgress` and `RequestRefresh` gating remain.
- Re-capture authoritative `FrontendShortcutEditorSnapshot` when opening an editor, preserve stale tile checks.
- `Title` is always stored exactly as entered; no automatic translation or case manipulation; preexisting user titles untouched.
- Current `ShouldApplyDefaultTitle` only auto-suggests a default for a new Shortcut when the title has not been overridden; no change.
- All `Action.Kind`, `TileId`, `IconGlyphOverride`, `CloseOverlayAfterLaunch`, `FrontendShortcutMutationIntent` fields remain typed and unmodified.
- Keep screenshot action uniqueness, `stagedScreenshotFolder` (including cancel/discard), `ResolveScreenshotFolder`, browse/use-default/open-folder paths; don't add a new settings field or store.
- `RunAsAdministrator` and normal-user launch policy remain; no code change to process-launch privilege or routing.

### Tile list / drag/drop

- Keep three equal-width columns, existing drag-threshold 8 DIP, pointer capture/release/cancel, drag preview and drop indicator, and Runtime-authoritative reorder.
- No local `_tiles.Move` or extra mutation on pointer move, no change to `Move` handling, logging or screen/overlay behavior.
- The translator reads typed enum/glyph and immutable strings; it does not create a separate tile collection, mutate the returned snapshot or introduce asynchronous jobs.

### Bound data / security

- `TileId` GUID, `TypeId`, actual executable paths/scripts/URLs, configured screenshot folder, Runtime validation/failure text and user-provided title remain exact.
- `ShortcutIconCatalog` and `ShortcutRuntime` stay unchanged; do not modify their public interfaces for localization.
- No new IPC fields or data schema migration.

### How to Use

- `StartDocumentationLoad`, `EnsureDocumentationWebView`, `LoadDocumentationAsync`, navigation correlation and timeout, Markdown sanitization and external-link checks, file source selection and retry behavior must remain as-is.
- This PR changes only XAML English fallbacks and RESW for How to Use shell; it must not replace remote docs with embedded HTML or add a language picker.

## 9. Source tests that REQUIRE attention

Existing source-contract tests are comprehensive; do not delete them simply because localization changes a literal. Review at least:

- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/MainNavigationIconContractTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs`.
- `tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs`.
- `tests/SteamInputAddonforClaw.Tests/ShortcutIconCatalogTests.cs`.
- Existing HowToUse tests (search current repo by `HowToUseMarkdownRenderer`, `ResolveSource` and `RetryButton_Click`).

Particularly fragile `UiArchitectureTests` assertions:

**`Main_app_shortcut_editor_is_separate_from_the_overlay_settings_page`**

- `Assert.Contains("Add Shortcut", shortcutXaml)` — should continue passing because original English `Content="＋ Add Shortcut"` is preserved.
- `PointerReleased`, `FolderPicker`, staged folder semantics, mutation call and no `ShortcutStore` direct access remain intact.

**`Main_app_shortcut_picker_exposes_parameterless_builtins_and_creation_only_titles`**

- Source tests currently assert `AddActionChoice(actionPicker, "Steam Big Picture"...)`, `"Steam"`, `"XBOX"`, `"Screenshot"` and `CreateBuiltInActionPanel("Opens the XBOX app.")`. These will change when text arguments become localized `Get` calls. Replace exact call-text tests with checks of `MainUiStrings.Get` keyed English fallbacks and preserved enum-to-panel identity, Screenshot eligibility and `GetDefaultTitle`/`ShouldApplyDefaultTitle` semantic results.
- Keep **all** assertions that `GetDefaultTitle(SteamBigPicture) == "Steam Big Picture"`, `SteamClient == "Steam"`, `XboxApp == "XBOX"` and `ScreenshotFullscreen == "Screenshot"`. These are **persistence invariants**, not candidates for translation.

**`Shortcut_icon_picker_stages_curated_choices_and_keeps_card_reorder_separate`**

- Existing tests demand `Content = "Change icon"` and `Content = "Use default"`, and direct `choice.Name` tooltip/automation uses; localization must update these source-based assertions while keeping the 32 source glyphs, picker layout, staging, `IconGlyphOverride`, preview and drag distinct.
- Existing `AutomationProperties.Name="Drag to reorder this Shortcut"` test may need to assert the localized attached property resource or `x:Bind` helper instead of a literal, while retaining the control's accessibility name.
- Preserve reorder pointer handlers, storyboard/lift/drop-preview, 3-column sizing, screenshot interaction exclusions and no direct collection mutation assertions.

**`MainNavigationIconContractTests`**

- Keep all original `Content` English XAML attributes, tags and icons. Adding `x:Uid` must not change menu/icon identity.

### New resource and functional-contract tests

1. Both `en-US`/`ko-KR` share **identical resource key sets**, with every new key nonempty. At PR7 delivery values are English in both; tests must not indefinitely assert language **value equality** after future Korean translation.
2. Main navigation `MainNav_Shortcut.Content` and `MainNav_HowToUse.Content` exist; keep original `Tag` and icon/order.
3. Static Shortcut and How to Use XAML UIDs are unique and the original English fallback attributes are retained, with correct property keys. Do **not** place x:Uid on bound `Title`/`ValidationMessage` text.
4. Every **seven action choices** maps the same typed `FrontendShortcutEditorActionKind` and initial order; `Screenshot` availability filtering and disabled/existing cases unchanged.
5. `GetDefaultTitle` and `ShouldApplyDefaultTitle` remain unchanged, with fixed persisted default titles; editing/custom titles remain literal user data.
6. For **all 32 `ShortcutIconCatalog.Choices`**, check unique glyph identity, matching `Shortcut_Icon_<glyphhex>` resource keys in both languages, exact original English fallback, unchanged effective glyph/override and consistent localized picker/preview/tooltip/automation names.
7. Static built-in target summaries localize by typed kind only; `Executable` filename, `Url` host, `Unsupported` raw string and `ValidationMessage` remain unchanged. Verify both normal ListView and drag-preview still use the same typed template.
8. Locally authored InfoBar errors/dialog text are covered, including `MainUiStrings.Format` with `{0}` for deletion and preservation of the actual tile title, exact producer `FailureMessage` precedence and original InfoBar severity.
9. How to Use already-bound `HowToUse_Loading` remains unchanged. Both remaining shell fields have RESW entries. `ResolveSource` still picks Korean README and English default and the existing WebView2/navigation/security tests pass.
10. Published UI PRI contains one nonempty `en-US` and `ko-KR` candidate per key. If supporting attached Automation property keys, validate their actual generated URI and update the script **narrowly**, rather than bypassing/ignoring failures.

**Published PRI requirement:** run `scripts/publish-layout.ps1`, which calls `scripts/verify-ui-localization-pri.ps1` against the deployed `ui/SteamInputAddonforClaw.UI.pri`. Source resource equality alone does not establish runtime localization in the unpackaged app.

## 10. Expected implementation diff

Primary files:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
other directly affected existing Main App UI source/PRI contract tests, only if necessary
```

Usually **no changes** to:

```text
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs
src/SteamInputAddonforClaw.Contracts/**
src/SteamInputAddonforClaw/Shortcuts/**
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw/ClawHud/**
src/SteamInputAddonforClaw.UI/Views/DevicePage.*
src/SteamInputAddonforClaw.UI/Views/ProfilePage.*
src/SteamInputAddonforClaw.UI/Views/XboxPage.*
src/SteamInputAddonforClaw.UI/Views/OverlayPage.*
```

`scripts/verify-ui-localization-pri.ps1` should stay unchanged for normal x:Uid ``Text/Content/Header/Description/PlaceholderText` and direct C# string keys. Only change it if adding an attached `AutomationProperties.Name` x:Uid key requires a specific, **verified** special-property URI path; prefer a simple C# function binding if that is more reliable.

Do not create new project or assembly references, introduce new localization resources outside the two files, or change public contracts.

## 11. Automated validation, acceptance and submission

Run the existing tools feasible in local Codex / CI:

1. `dotnet restore SteamInputAddonforClaw.slnx` and UI test project restore.
2. Release build of solution and UI test project (including generated `x:Bind` code).
3. Existing Core and UI test suites, particularly Main UI localization, icon/name selection, Shortcut reorder, shortcut mutation and HowToUse/WebView tests.
4. Existing CI PowerShell regressions and `git diff --check`.
5. `scripts/publish-layout.ps1` and `scripts/verify-ui-localization-pri.ps1` on the **published** resource map, with every new key present for both locales and English fallback.
6. `scripts/verify-publish-assets.ps1`, normal publish size reporting and startup smoke checks where available.

**Definition of done:**

- All locally authored Main App Shortcut/HowToUse UI labels, action picker/dialog choices, icon names, user-facing local prompts/errors and relevant accessibility names are RESW-backed; Korean can be added later without C# logic changes.
- Both language candidates initially contain existing English exactly; no new localization framework or app language selector.
- New translations **never alter persisted title/shortcut IDs/typed actions/icons/reorder/screenshot settings**, nor does any producer `FailureMessage`, validation, dynamic file/URL or actual user title get rewritten.
- Shortcut QAM Overlay, separate ClawHUD, Full1902 controller authority and runtime code remain untouched.
- Existing UI layout, 3-column drag reordering, screenshot folder cancellation/validation, save/delete confirmation, default-title semantics and WebView2 HowToUse behavior remain identical.
- Build/tests and **published PRI** pass, and hardware checks are not required for merge.

Open **one PR7**. In its PR description report source areas and added resource count, explicitly describe why persisted default titles and raw Runtime strings were not localized, include the icon catalog and typed target-summary UI-only handling, and list automated tests plus published PRI verification results.

**Local Codex is responsible for implementation/automated checks; the user alone is responsible for MSI Claw physical verification after merge.**
