# Work Order — Main App RESW PR6: ClawHUD Settings Tab Only

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Baseline:** Latest `main` after the merged localization PRs #785 (foundation), #786 (Device), #787 (Controller), #788 (Steam), and #789 (XBOX). Re-read HEAD before editing.  
**Deliverable:** **One focused implementation PR6**, covering the whole ClawHUD **Main App settings page** (`OverlayPage`) and its navigation caption.  
**Real-device verification:** Exclusively the user's role **after merge**. Never require local Codex/CI to validate on an MSI Claw and never block this PR because device testing has not happened.

## 0. Locked scope — Main App UI, NOT the actual HUD

The user explicitly defined the product scope as **SteamAddonforClaw Main App UI localization only**. For ClawHUD, this is solely the **WinUI 3 settings tab authored by the Addon**:

```text
SteamAddonforClaw.UI (Main App)
  MainWindow.xaml: NavigationViewItem Content="ClawHUD", Tag="Overlay"
  Views/OverlayPage.xaml
  Views/OverlayPage.xaml.cs
       |
       | existing IAddonFrontendControl RPCs (unchanged)
       v
Addon Runtime: ClawHUD frontend state/settings (unchanged)
       |
       | existing IPC, dependency and process ownership (unchanged)
       v
ClawHUD standalone/managed executable: the real in-game HUD (OUT OF SCOPE)
```

Do **not** modify or localize:

- The original **ClawHUD** repository or its source, build artifacts, fonts or in-game HUD text.
- `src/SteamInputAddonforClaw.Overlay/**`, including the separate Addon Quick Settings/QAM Overlay UI and the `OverlayWindow.ClawHud` implementation.
- `src/SteamInputAddonforClaw/ClawHud/**`, including `ClawHudFrontendMapping.StatusMessage`, process ownership, acquisition, settings IPC, error classification and Runtime state machine.
- `src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs`, transport/protocol types or API contracts.
- Other Main App tabs (Device, Controller, Steam, XBOX, Shortcut, Tweaks, How to Use, Settings, Developer), except the single ClawHUD NavigationView item owned by `MainWindow.xaml`.

Do **not** add a second UI, language selector, shared translation facade, generic SettingsCard abstraction, dynamic language-switching state, arbitrary background localization service, XBOX/Steam page refactor, or new synchronization/ownership machinery.

**What must be localized:** all fixed and locally authored user-visible text within the Main App ClawHUD settings tab — menu label, header/subtitle, SettingsCard captions/descriptions, ComboBox option captions, Retry button, locally created status/fallback/error strings, and the locally formatted enum **status label** in the Intel VRR result.

**What must stay verbatim:** all Runtime/Frontend-authored `snapshot.StatusMessage`, `result.FailureMessage`, external `IntelVrrLastResult.Message`, `PanelName` and numeric range data; logging/exception diagnostic strings; RPC/enum identities. The frontend's authoritative message can remain English after PR6, consistent with PR2–PR5's ownership policy. This is deliberate, not a missing translation key.

Language policy: add to **both** `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw`, with **identical existing English values initially**. The user will translate Korean values later. Retain English XAML/C# fallbacks, the existing Windows language selection, published PRI and current `MainUiStrings` API. No Korean translations in this PR.

## 1. Source/document authority — read before implementation

Read the relevant **current** Full1902 documents in the documented precedence:

1. `docs/Full 1902 Implementation/README.md`.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.

For actual ClawHUD settings ownership and today's UI layout, read:

- `docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md` (Runtime owner, frontend IPC and setting semantics; earlier layout proposals are subordinate to code now in main).
- `docs/work-order/MAIN_UI_OVERLAY_TAB_CLAWHUD_SETTINGS_REDESIGN_WORK_ORDER_2026-09-22.md` (Main App `OverlayPage` UI ownership; current code/test layout is authoritative).
- `docs/work-order/2026-10-14_MAIN_UI_RESW_LOCALIZATION_PR1_DEPLOYMENT_SAFE_FOUNDATION_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_CONTROLLER_TAB_RESW_PR3_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_STEAM_TAB_RESW_PR4_WORK_ORDER.md`.
- `docs/work-order/2026-10-14_MAIN_APP_XBOX_TAB_RESW_PR5_WORK_ORDER.md`.

Read source on HEAD:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs  (read-only contract reference)
src/SteamInputAddonforClaw/ClawHud/ClawHudFrontendMapping.cs              (read-only message-origin reference)
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
```

Current product assumptions are one Windows user/one interactive session, standalone Full1902. **CTW integration has been retired**. Controller/lifecycle safety must remain intact without inventing new speculative concurrency defenses for an unrelated localization change.

## 2. Verified current code structure and ownership boundaries

### 2.1 Main navigation

`MainWindow.xaml` has:

```xml
<NavigationViewItem Content="ClawHUD" Tag="Overlay">
```

Only the visible `Content="ClawHUD"` is localized. Keep `Tag="Overlay"` (and `MainNavigationPage.Overlay`, `OverlayPageHost`, `_overlayContent`, `GetOrCreateOverlayPage()`/lazy initialization and navigation activation/refresh) exactly as they are. Preserve menu order and the current ClawHUD icon.

Add `x:Uid="MainNav_ClawHud"` on this item, with `MainNav_ClawHud.Content` RESW values. **Do not change the source Content attribute** or change the navigation destination to `ClawHUD`.

### 2.2 Actual Main App view

`OverlayPage.xaml` is a WinUI UserControl (119 physical lines, roughly 6.5 KB), containing a top-level `ScrollViewer`/`StackPanel`, an `InfoBar`, and eight SettingsCards. `OverlayPage.xaml.cs` (373 physical lines) owns rendering, frontend call orchestration and the opacity preview/commit behavior.

Visible regions are:

1. `Overlay` title and `Configure the performance overlay and display behavior.` subtitle.
2. `ClawHudInfoBar` with a `Retry` ActionButton (status text dynamically assigned by code).
3. `Enable HUD` card with a dynamic `ClawHudStatusText` and enabled toggle.
4. `Display mode` card — `In game only` / `Always`.
5. `HUD size` card — `-2` / `-1` / `Default` / `+1` / `+2`.
6. `Font` card — `Unispace` / `Segoe UI Variable`.
7. `Alignment` card — `Left` / `Center` / `Right`.
8. `Background width` card — `Full width` / `Content width`.
9. `HUD opacity` card — dynamic `—` / percentage and slider.
10. `Intel VRR Range Fix` card — `ClawHudVrrResultText`, enabled toggle and dynamic VRR result.

There are **no separate static TextBlock section headings** labeled ClawHUD/Display/Appearance/Display compatibility in the current XAML, even though an older work order discussed possible sections. Do **not** add them just for localization; the current source and tests explicitly prohibit those extra headings.

All controls have stable existing event handlers and settings RPCs. UI localization must not alter the controls, resource styles, card icons or event subscriptions.

### 2.3 The authoritative state messages are NOT OverlayPage strings

`RenderClawHud(FrontendClawHudSnapshot snapshot)` currently does:

```csharp
ClawHudStatusText.Text = snapshot.StatusMessage;
ClawHudInfoBar.Message = snapshot.StatusMessage;
```

`ClawHudFrontendMapping.StatusMessage(ClawHudState)` in the Addon Runtime creates messages such as `Off`, `Starting…`, `Ready`, `ClawHUD Standalone is already running. Close it and retry.`, and Runtime failure messages.

**Do not** move that mapping into the UI or translate/replace `snapshot.StatusMessage`. Preserve current severity, visibility, retry availability and selected Runtime text exactly.

The StatusMessage path is explicitly excluded because the user requested this work to be Main App UI resource extraction **without changing Runtime** and because preceding localization PRs deliberately preserve authoritative external messages. **No new Client-side enum-to-Runtime-message map**.

## 3. XAML x:Uid resource extraction

Use the existing Windows App SDK pattern: each localized XAML element gets a unique `x:Uid`, with the original fixed English text left in place and a corresponding `<UID>.<Property>` key in **both** RESW files.

**Examples (add attributes to actual existing elements, do not replace/rebuild controls):**

```xml
<TextBlock x:Uid="ClawHud_PageTitle" FontSize="24" FontWeight="SemiBold" Text="Overlay" />

<Button x:Name="ClawHudRetryButton"
        x:Uid="ClawHud_RetryButton"
        Content="Retry"
        Click="ClawHudRetryButton_Click" />

<ctcontrols:SettingsCard x:Name="ClawHudEnabledCard"
                         x:Uid="ClawHud_EnableCard"
                         Header="Enable HUD">
```

Matching source RESW examples:

```xml
<data name="ClawHud_PageTitle.Text" xml:space="preserve"><value>Overlay</value></data>
<data name="ClawHud_RetryButton.Content" xml:space="preserve"><value>Retry</value></data>
<data name="ClawHud_EnableCard.Header" xml:space="preserve"><value>Enable HUD</value></data>
```

### Required fixed-string inventory

| Region | Fixed values and property |
| --- | --- |
| Navigation | `MainNav_ClawHud.Content = ClawHUD` |
| Page | `Overlay` title; `Configure the performance overlay and display behavior.` subtitle |
| Retry | `Retry` button Content |
| Enable | `Enable HUD` card Header |
| Display mode | `Display mode` Header; `Choose when the HUD is visible.` Description; options `In game only`, `Always` |
| HUD size | `HUD size` Header; `Adjust the overall HUD scale.` Description; `Default` option |
| Font | `Font` Header; `Font used by the performance HUD.` Description; options `Unispace` and `Segoe UI Variable` (proper names remain the same English now) |
| Alignment | `Alignment` Header; `Horizontal alignment of HUD content.` Description; options `Left`, `Center`, `Right` |
| Background width | `Background width` Header; `Choose how far the HUD background extends.` Description; options `Full width`, `Content width` |
| Opacity | `HUD opacity` Header |
| Intel VRR | `Intel VRR Range Fix` Header |

`HUD size` numeric option captions `-2`, `-1`, `+1`, `+2` remain technical numeric inputs. It is fine to leave them as literal XAML. `Default` is prose and **must** become a RESW-backed ComboBoxItem `Content`. Preserve the exact five-item order and offsets.

**Do not attach `x:Uid` to purely dynamic or technical fields just to inflate coverage:**

- `ClawHudStatusText` (`snapshot.StatusMessage`), `ClawHudVrrResultText` (formatted result), `ClawHudOpacityValueText` (`—` or `nn%`).
- `ClawHudInfoBar.Message` (set in code), background-percent numeric value, technical font identifiers used by frontend enums or RPCs.
- Empty toggle `OnContent=""` / `OffContent=""`, `x:Name`, event-handler names, `Tag`, icons/styles, `SelectedIndex` and original UI bindings.
- There is no static Description attribute on `ClawHudEnabledCard` (its description is an embedded `ClawHudStatusText`), `ClawHudOpacityCard` (embedded percentage), or the Intel VRR card (embedded dynamic result). **Do not add new static descriptions or change existing composite `SettingsCard.Description` content.**

Maintain the **exact original icon, control order, selected-index semantics, slider range, and card composition**. In particular the Intel VRR `SettingsCard.Description` must continue to contain `ClawHudVrrResultText` and the toggle must remain right-aligned.

## 4. Locally authored code messages and fallback errors — include

Only `OverlayPage.xaml.cs` owns these messages; they do not originate from the external ClawHUD program or the Runtime snapshot.

### 4.1 UI-created initial and optimistic status

The page currently creates a local initial snapshot using `StatusMessage = "Off"` and a synthetic optimistic status during enable/disable:

```csharp
StatusMessage = desired ? "Starting…" : "Off",
```

Replace **only these UI-authored** values with `MainUiStrings.Get` (for example `ClawHud_LocalStatus_Off` and `ClawHud_LocalStatus_Starting`). Preserve the exact `DesiredEnabled` and `RuntimeState` values and the immediate optimistic rendering behavior.

The next authoritative `CaptureClawHudAsync` / `SetClawHudEnabledAsync` result still replaces them with **verbatim** `snapshot.StatusMessage`. Do not intercept the Runtime text after it arrives.

### 4.2 UI-created mutation failures

`ClawHudEnabledToggleSwitch_Toggled` currently creates the local fallback:

```csharp
StatusMessage = "ClawHUD could not be changed.",
```

when both the primary mutation and recovery capture fail. `SubmitClawHudMutationAsync` similarly creates:

```csharp
StatusMessage = "HUD setting could not be verified."
```

Extract those exact complete strings using dedicated `ClawHud_...` keys in `MainUiStrings.Get`. They are display messages, not a request to modify `ClawHudFrontendMapping`.

Keep the exact nested catch/capture/recovery behavior, `_clawHudOperationInProgress` gate, severity and visibility decisions. Do not translate the exception text or `AppLog` source messages.

### 4.3 Other supplied messages and code literals

`CommitClawHudOpacityAsync` and `SubmitClawHudMutationAsync` both use the existing producer-origin `result.FailureMessage ?? result.Snapshot.StatusMessage` precedence on failed mutation. **Retain this unchanged**, including empty-string behavior.

`SendClawHudOpacityPreviewAsync` uses a `"Unknown"` fallback **only inside diagnostic `AppLog.Warn`**. It is **not** a user-visible caption and **must not** be moved into RESW. Likewise all `"ClawHUD.UI"` categories and log event sentences are out of scope.

Keep empty strings, numeric `"%"/"—"` placeholders, the selected percentage formatting, and all actual frontend responses unchanged.

## 5. Intel VRR result: local enum label vs external text

`OverlayPage.FormatVrrResult(FrontendClawHudIntelVrrResult? result)` currently constructs the visible summary starting with `result.Status.ToString()`, then appends:

- `result.RangeBefore` and `result.RangeAfter` separated by ` → `;
- `result.PanelName`;
- `result.Message`;
- the existing ` · ` separators.

**Only the enum status's display label is locally authored**. RESW should cover it, while the panel, ranges and message **remain exact raw Runtime/ClawHUD data**. Keep the same order, punctuation/separators, empty/nonempty guards, and null result behavior.

The current typed enum `FrontendClawHudIntelVrrStatus` has these nine values:

```text
Disabled
Unavailable
UnsupportedPanel
AmbiguousDisplay
AlreadyCorrect
SkippedUserProfile
Applied
ApplyFailed
VerificationFailed
```

Simple recommended implementation without adding a new formatter class:

```csharp
var summary = MainUiStrings.Get(
    "ClawHud_VrrStatus_" + result.Status,
    result.Status.ToString());
```

Register all nine `ClawHud_VrrStatus_<EnumName>` keys in both RESWs with the **exact current `ToString()` text** as English value. Use only the enum to select a resource key; do not translate or change the enum values or the operation's typed `FrontendClawHudIntelVrrResult.Status` field. Do not parse or translate free-form `result.Message`, panel names or measured ranges.

If PR6 keeps the VRR status identifier unchanged in English, this must still have the correct RESW key to allow a future Korean translation without a code edit. Avoid creating a generalized status-translation service.

## 6. Data/control semantics must be unaffected by translation

`OverlayPage` uses ordered ComboBox indices as its wire/UI mapping. **Captions must be presentation only**; no behavior may be derived from localized visible strings.

| UI control | Current identity/policy that must remain |
| --- | --- |
| `ClawHudDisplayModeComboBox` | index 0 = `InGameOnly`, index 1 = `Always` |
| `ClawHudSizeComboBox` | index 0..4 maps to `HudSizeOffset = SelectedIndex - 2` |
| `ClawHudFontComboBox` | index 0 = `Unispace`, index 1 = `SegoeUiVariable` |
| `ClawHudAlignmentComboBox` | index 0..2 maps to `FrontendClawHudAlignment` typed enum values |
| `ClawHudBackgroundComboBox` | index 0 = `FullWidth`, index 1 = `ContentWidth` |
| `ClawHudOpacitySlider` | Minimum 50, Maximum 100, StepFrequency 5; preview on edits, commit on pointer-release, capture-loss or key-up |
| `ClawHudIntelVrrToggleSwitch` | typed `FrontendClawHudMutationKind.IntelVrrRangeFixEnabled` |

Preserve `RenderClawHud` event suppression through `_applyingClawHudState`, mutation guards and toggles, explicit retry, InfoBar Error/Warning classification, desired-enabled vs actual-ready distinction, and `Activate` refresh. Do not alter `RefreshClawHudAsync` transport exception classification/logging.

**Opacity lifecycle is especially important:** retain the existing serialized `_opacityPreviewTail`, `_opacityPreviewGate`, `TryClaimOpacityCommit`, `CompleteOpacityCommit` and the queued follow-up commit behavior. Do not turn every preview into a committed write, alter the order of commits, or change the values because option labels are translated.

Preserve standalone-conflict handling: the Addon must not seize/kill a separately running ClawHUD executable, including when the Runtime reports `FrontendClawHudRuntimeState.StandaloneConflict`. This PR does not touch that process ownership.

No changes to networking, install/update, version lock, restart, sleep/resume, graphics/display API, overlay priority or controller lifecycle. Normal functional failures should continue to fall back safely exactly as prior.

## 7. Resource key conventions / correct PRI behavior

Use existing `MainUiStrings.Get(key, englishFallback)` only for programmatic lookups. Reuse the existing `MainUiStrings.cs` unchanged. `x:Uid` property resources are written in RESW as `ClawHud_DisplayModeCard.Header` etc, whereas direct C# reads for such a property **must use slash paths** (`ClawHud_DisplayModeCard/Header`) to match generated `/Resources/<UID>/<Property>` PRI URIs.

PR6 normally has **no need** for C# direct lookup of an x:Uid description property, because none of the static SettingsCard descriptions is overwritten by `RenderClawHud`. Avoid unnecessary C# reassignments and duplicated resource keys. If adding a required direct lookup, follow the slash form and test the published URI.

The existing `scripts/verify-ui-localization-pri.ps1` already knows `Text|Content|Header|Description|PlaceholderText` property resource keys, and `scripts/publish-layout.ps1` verifies the published Main UI PRI for both languages. **Do not edit those scripts unless the actual published artifact demonstrates a reproducible specific mismatch**. Do not bypass a failing candidate or create a second publish-verifier script.

Keep each RESW key nonempty, stable and unique in both languages. At PR6 delivery, both languages have exact current English values. Future tests **must not** enforce English/Korean **value equality**, identical translations or a frozen total key count.

The application remains unpackaged, uses the external Main UI `SteamInputAddonforClaw.UI.pri`, and picks its language using the existing Windows language qualifiers; no new packaging or culture settings.

## 8. Automated tests and practical source-assertion changes

Read the existing:

- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/MainNavigationIconContractTests.cs`.
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs`.
- relevant existing Main App UI `OverlayPage` opacity behavior tests in `UiArchitectureTests`.
- existing release/publish script tests, asset verifier tests, and resource key verification.

### Required localization tests

1. `MainNav_ClawHud.Content` exists with current English `ClawHUD`; the nav item retains `Tag="Overlay"`, icon `E7F4`, item ordering and `OverlayPageHost`.
2. `OverlayPage.xaml`'s fixed human-readable text properties have unique `x:Uid` values and matching dotted property resource keys in `en-US` and `ko-KR`. Verify nonempty values and the original English XAML attribute fallbacks; tests should not require translated Korean to equal English later.
3. Visible combo options retain their exact number and order: Display Mode (2), HUD size (5), Font (2), Alignment (3), Background (2). `Default` gets a key, numeric options remain literal and intact; option label translations do not affect SelectedIndex handling.
4. All locally synthesized `Off`, `Starting…`, `ClawHUD could not be changed.` and `HUD setting could not be verified.` strings have resource keys and use `MainUiStrings.Get`.
5. All nine typed Intel VRR status labels have resource keys matching existing `Status.ToString()` English fallbacks; the format still uses the original `PanelName`, `RangeBefore`, `RangeAfter` and `Message` unchanged.
6. `RenderClawHud` continues to put authoritative `snapshot.StatusMessage` into both status TextBlock and InfoBar; `result.FailureMessage` has original precedence and no Runtime message is rewritten.
7. Preserve opacity preview/commit/keyboard and retry, eligibility, and desired-state behavior tests; no new device test or hardware mock framework.
8. Publish the Main UI and inspect emitted `SteamInputAddonforClaw.UI.pri` so **each** new key has exactly one nonempty `en-US` and `ko-KR` candidate.

### Existing test adjustments that need attention

**`UiArchitectureTests` contains a source-literal assertion that will break after adding the navigation UID:**

```csharp
Assert.Contains("<NavigationViewItem Content=\"ClawHUD\" Tag=\"Overlay\">", xaml, StringComparison.Ordinal);
```

Update this particular assertion to inspect the actual `NavigationViewItem` via `XDocument` and check `x:Uid="MainNav_ClawHud"`, English `Content="ClawHUD"` and `Tag="Overlay"`. Keep negative checks for old `Content="Overlay"` behavior and all other navigation/host contracts. Do **not** remove or weaken navigation checks.

**`UiArchitectureTests.Overlay_page_owns_the_main_ui_clawhud_surface_and_settings_page_does_not`** currently finds the Intel VRR card using the exact leading text:

```csharp
var vrrCardStart = overlayXaml.IndexOf(
    "<ctcontrols:SettingsCard Header=\"Intel VRR Range Fix\"",
    StringComparison.Ordinal);
```

Adding `x:Uid` to the Intel VRR card can make that brittle string search fail. Instead, locate the card by `x:Uid` through `XDocument`, then assert `Header="Intel VRR Range Fix"`, its composite `SettingsCard.Description`, `ClawHudVrrResultText`, right-aligned toggle, and empty toggle captions. Keep `Opacity`, `Retry` and Main App-only ownership assertions.

**`OptionCardIconContractTests`** enumerates eight approved ClawHUD option-card icons, selected by the source English `Header`. Preserve all the English XAML `Header` attributes and unchanged icons so this can pass without a new icon policy or reordering.

**`MainNavigationIconContractTests`** checks `Content`, `Tag` and icon, not exact attribute order; keep the current values.

Existing `UiArchitectureTests` also asserts that no extra static ClawHUD/Display/Appearance/Display compatibility section headings exist; preserve that structural invariant instead of reviving an older UI layout request.

Keep source-contract tests narrowly focused and do not create overly broad/full-file string snapshots or a production abstraction just to satisfy tests. If tests are brittle because of an added UID, update the test to inspect the actual attribute/identity.

## 9. Implementation boundaries / expected diff

**Expected files:**

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
other directly affected Main App UI source/PRI contract tests (only if necessary)
```

Normally **unchanged**, including:

```text
src/SteamInputAddonforClaw.UI/MainUiStrings.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw/ClawHud/**
src/SteamInputAddonforClaw.Contracts/**
src/SteamInputAddonforClaw.FrontendTransport/**
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw.UI/Views/SettingsPage.*
scripts/verify-ui-localization-pri.ps1
scripts/publish-layout.ps1
```

Do not modify a separate `onehoon/ClawHUD` original source repo. **PR6 is an Addon-repository-only localization patch.**

## 10. Code-only validation / release-readiness

Local Codex should run the standard repository's **automated** checks feasible in its environment:

1. `dotnet restore SteamInputAddonforClaw.slnx` and relevant UI test restoration.
2. Release solution and UI test builds.
3. Existing Core/UI tests, especially new Main UI localization tests, navigation/icon tests, Overlay/ClawHUD profile and opacity tests.
4. Existing PowerShell/CI verification scripts.
5. `scripts/publish-layout.ps1`, which verifies **the published** `ui/SteamInputAddonforClaw.UI.pri` and its `en-US` default plus both nonempty language candidates for every key. Inspect representative `ClawHud_...` keys via `makepri dump` if useful.
6. `scripts/verify-publish-assets.ps1` and other existing CI publish asset checks.
7. `git diff --check`, and inspect the diff for accidental changes outside the Main App.

**Acceptance conditions:**

- Entire Main App ClawHUD settings tab's locally owned text can later be translated by editing only `ko-KR/Resources.resw`.
- Both languages initially display the same current English; visible UI layout and operational behavior are unchanged.
- Runtime/Frontend `StatusMessage`, `FailureMessage`, external Intel VRR details, logs and original ClawHUD in-game UI remain untouched.
- Existing frontend RPC, state and process ownership, start/retry, mutations, opacity preview/commit, Intel VRR toggle and screen selection behave identically.
- Build/tests/published PRI pass without new libraries or speculative safety abstractions.
- **Physical testing is not a local Codex requirement and is not a code-review blocker; the user validates on the physical device after merge.**

## 11. Local Codex PR submission

Submit **one** PR6. In its description report:

- XAML/nav areas localized and number of new RESW keys;
- the distinction between UI-created status/failure strings and verbatim Runtime-supplied status/failure data;
- Intel VRR enum status localization versus untouched panel/range/raw messages;
- preserved ComboBox index/enum mappings, opacity preview/commit and retry behavior;
- Release build, automated tests and **published** PRI validation results;
- no ClawHUD upstream/original repo, Addon in-game Overlay or Runtime modifications.

This work order is documentation-only; local Codex implements the PR. MSI Claw physical verification occurs separately under the user's post-merge role.
