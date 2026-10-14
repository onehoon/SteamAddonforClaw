# Work Order — Main App RESW PR2: Device Tab Localization (Exclude Device Summary)

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw` — current `main` (re-read HEAD before implementing)  
**Product:** Standalone Full PID1902, one Windows user / one interactive session  
**Owner:** Local Codex  
**Delivery:** One focused PR following merged PR #785 (Main UI RESW foundation)  
**Release requirement:** This PR must be independently buildable, mergeable, and distributable; it must not require localization of any other tab.  
**Hardware verification:** Product owner after merge, not a required implementation or PR-review blocker.

## 0. Locked product decisions

1. Convert **the complete Device tab's UI-owned visible English text** to the existing Main App RESW structure **except for the entire top `DeviceSummaryCard`**.
2. In **both** `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw`, enter the **identical existing English** string values initially. Do **not** translate to Korean yet. Later manual edits of `ko-KR` and later English copy revisions must be possible by editing RESW values alone.
3. Keep the existing language-selection contract from PR #785: Windows display language, `en-US` default/fallback, **no app language selector**, persistence, language-monitoring service, or UI/Runtime restart workflow.
4. Absolutely no changes to the controller/Device feature behavior, event handlers, state/authority decisions, device operations, or UI layout. This is a **resource extraction and display-only PR**.
5. Do **not** touch the separate Quick Settings Overlay, other Main App tabs (other than the narrow resource-helper reuse noted below), IPC, Runtime, device settings schemas, or CTW integration.
6. Keep the names of device models, units, protocol identifiers, the domain enum values, and the exact current English visible wording. Localization keys are not identity values.

## 1. Read these sources before editing

### Architectural authority (current policy wins over historical work orders)

- `docs/Full 1902 Implementation/README.md` (document precedence).
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` and `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md` for the untouched Center M restart/authority boundary.
- `docs/work-order/2026-10-11_DEVICE_SUMMARY_LIVE_CONTROLLER_BADGE_WORK_ORDER.md` (explicitly **excluded** in this PR).

### Current code and PR #785 foundation

- `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/DeviceSummaryPresentation.cs` (**read-only**)
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml[.cs]` (do not change navigation/status behavior)
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs` (PR1 imperative ResourceManager getter, see `5)
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw`
- `src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw`
- `scripts/publish-layout.ps1`, `scripts/verify-ui-localization-pri.ps1`, `scripts/verify-publish-assets.ps1`
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs`
- `tests/SteamInputAddonforClaw.UiTests/TdpFrontendContractTests.cs`

Microsoft source: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings . In particular, XAML property resources use `x:Uid` and keys such as `Name.Header` or `Name.Text`, while code-read strings use a plain key. Keep the existing **unpackaged** WinUI 3 PRI lookup and publish chain, rather than generating or overwriting a separate PRI.

## 2. Verified current UI/code shape

`DevicePage.xaml` contains the page title, then `DeviceSummaryCard`, followed by these five user-action areas in order:

1. `CenterMStartupCard` with `CenterMStartupInfoBar`; direct Enable/Disable buttons and a reboot-bound `ContentDialog` in code-behind.
2. `TdpExpander` and `TdpInfoBar`, AC/DC PL1/PL2 controls; result strings `Success`/`Fail`.
3. `CpuBoostExpander` and `CpuBoostInfoBar`; seven `CpuBoostModeItem` labels constructed in C#.
4. `PowerModeExpander` and `PowerModeInfoBar`; three `PowerModeItem` labels constructed in C#.
5. `BatteryChargeLimitCard` and `BatteryChargeLimitInfoBar`; composed current/desired messages, percentage display, error/info text.

`DevicePage.xaml.cs` owns `Activate()/Deactivate()`, a single aggregate `CaptureDeviceQuickSettingsAsync()`, separate Center M snapshots, AC/DC selection-suppression flags, TDP debounce/generation/draft preservation, battery slider commits, backend failure-message precedence, and reboot-bound Center M transition UX. These are **behavioral contracts** and remain intact.

PR #785 already created `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw`, added two XAML `x:Uid` samples, and implemented a small, directly-used `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` lookup in `SettingsPage.xaml.cs::GetDefaultAppThemeDescription()`. It also added a real published-PRI verifier, but its current `$requiredPilotUris` checks only three initial keys. All are reused; do **not** introduce another resource format, resource manager, locale authority, package mode, or independent PRI.

## 3. Explicit exclusion — the entire top device-identity/status card

**Do not localize, restructure, or touch anything inside `DeviceSummaryCard`**, including:

- `DeviceManufacturerText`, `DeviceModelText`, `DeviceSupportText`, `DeviceBoardGpuText`.
- `DevicePage.RenderDeviceSummary(FrontendStatusSnapshot)` — including the literal `Board: … · GPU: …` format.
- `DeviceSummaryPresentation.FormatManufacturerForDisplay` / `FormatControllerBadge` (including `Unsupported`, `Compatibility unknown`, and live controller-presentation labels).
- MainWindow's `DeviceContent.RenderDeviceSummary(...)` call path and associated tests.
- Underlying device/model/GPU strings, hardware/ControllerBadge enums, snapshot contents, or state polling.

The **separate “Device” page heading above the card** is in scope and should be given its own XAML `x:Uid`. PR #785's main navigation `MainNav_Device.Content` key remains unchanged.

## 4. Exact extraction scope and resource strategy

### 4.1 XAML fixed texts — include

Apply meaningful **unique per-element `x:Uid`** values to visible strings in `DevicePage.xaml`, with matching `<Uid>.<Property>` keys in **both** RESW files. Retain the existing English XAML literal as a readability/display fallback; do not change `Tag`, `x:Name`, handlers, binding identifiers or layout.

Inventory:

| Region | Visible source properties |
| --- | --- |
| Page heading | `Device` (`TextBlock.Text`) |
| MSI Center M | `MSI Center M` header; startup/restart description; initial `Status: —` placeholder; `Enable` / `Disable` button contents |
| TDP | `TDP Control` header; power-limit description; `Plugged in` / `On battery` card headers |
| CPU Boost | `CPU Boost` header; boost-mode description; `Plugged in` / `On battery` card headers |
| Windows Power Mode | `Windows Power Mode` header; per-power-rail description; `Plugged in` / `On battery` card headers |
| Battery | `Battery charge limit` card header |

Example:

```xml
<TextBlock x:Uid="Device_PageTitle" FontSize="24" FontWeight="SemiBold" Text="Device" />

<ctcontrols:SettingsExpander x:Name="TdpExpander"
    x:Uid="Device_TdpExpander"
    Header="TDP Control"
    Description="Configure processor power limits for plugged-in and battery operation."
    IsExpanded="False" />
```

Corresponding source key shapes:

```xml
<data name="Device_PageTitle.Text" xml:space="preserve"><value>Device</value></data>
<data name="Device_TdpExpander.Header" xml:space="preserve"><value>TDP Control</value></data>
<data name="Device_TdpExpander.Description" xml:space="preserve">
  <value>Configure processor power limits for plugged-in and battery operation.</value>
</data>
```

For `SettingsCard` / `SettingsExpander` from CommunityToolkit, verify that their `Header` and `Description` properties accept `x:Uid` localization in the current WinUI 3/Toolkit build. If an actual incompatibility is demonstrated, use a **small Device-only imperative resource assignment** instead of custom templates, duplicated state, or changing UI structure.

`PL1`, `PL2`, `W`, `%`, em dashes used as numeric placeholders, and other fixed engineering/unit notations may remain literal: they are not language-dependent prose. Do not change numeric/slider behavior or spacing. Do not add resource properties for `Tag`, `Name`, `Visibility`, `IsEnabled`, slider limits, mode values, or icon glyphs.

### 4.2 C# strings — include the entire Device-owned surface

Move the Device page's **locally authored visible strings** into plain (`no .Text/.Header suffix`) RESW keys. At minimum account for all of the following call sites:

**Center M (`RenderCenterMStartup`, `RequestCenterMTransitionAsync`):**
- All four complete `Status: Enabled` / `Disabled` / `Needs attention` / `Unavailable` status strings (and initial `Status: —` XAML placeholder).
- Inconsistent-startup warning and local unavailable fallback.
- Enable/Disable confirmation **titles**, **multi-paragraph contents**, **primary-button texts**, and `Cancel`. Preserve the exact existing embedded blank line between paragraphs and existing default-close behavior.
- Restarting-Windows success message; local cancelled/failure/connection-interrupted fallbacks.
- Existing `snapshot.FailureMessage` / `result.FailureMessage` take precedence unchanged; especially preserve the post-failed-Disable cleanup opportunity and Enabled-state Enable-button exception.

**TDP (`RenderTdp`, `RunTdpMutationAsync`, `TdpEnabledToggleSwitch_Toggled`):**
- Local unavailable, persistence-unwritable, mutation-failed and runtime-disconnected InfoBar strings.
- The ephemeral result labels `Success` / `Fail` from hardware application. Do not change result timing or which AC/DC result fields are written.
- Leave numeric TDP value and `W` unit formatting unchanged.

**CPU Boost (`Modes`, `Render`, `RunCpuBoostMutationAsync`):**
- Every one of the seven selection labels: `Disabled`, `Enabled`, `Aggressive`, `Efficient Enabled`, `Efficient Aggressive`, `Aggressive At Guaranteed`, `Efficient Aggressive At Guaranteed`.
- Local settings-unwritable and mutation-failed/Runtime-disconnected InfoBars.
- The `The last CPU Boost change could not be applied to Windows: {failure}` prefix **as a complete format template**. Append/pass the authoritative failure text unchanged; do not attempt to translate the failure payload.

**Windows Power Mode (`PowerModes`, `RenderPowerMode`, `RunPowerModeMutationAsync`):**
- Three label texts `Best power efficiency`, `Balanced`, `Best performance`; keep underlying `WindowsPowerMode` values unchanged.
- Local unavailable/initialization-failed, mutation-failed, Runtime-disconnected fallback strings.

**Battery charge limit (`RenderBatteryChargeLimit` and mutation handlers):**
- Current hardware present and unavailable clauses; desired-state enabled/disabled wording; uninitialized/ownership-instruction wording; use parameterized resource templates for variable percentages.
- Battery unavailable/persistence-unwritable/mismatch-to-desired InfoBar text.
- Local mutation-failure and interrupted-Runtime messages (both percent and enable paths).
- Keep raw percentage values, 60–100% input policy, desired/current comparison, and `snapshot.LastFailure` precedence unchanged.

**Do not skip strings because they live in an exceptional/rarely visible branch**; this PR aims to finish this one tab's UI-owned text. Conversely, **do not move logs, exception metadata, enums or backend-provided values into RESW**. `snapshot.LastFailure`, `snapshot.FailureMessage`, `result.FailureMessage` and similar Runtime-provided human-readable fields remain passed through exactly as today. Localizing those messages would require a separate typed error-code/contract design and is out of scope.

### 4.3 Localized format strings

For variable UI text, store the **whole sentence/fragment** with numbered .NET placeholders, not English prefixes concatenated to dynamic values.

Example for the current/desired battery text:

```xml
<data name="Device_Battery_CurrentKnown" xml:space="preserve">
  <value>Current hardware: {0}%</value>
</data>
<data name="Device_Battery_CurrentUnavailable" xml:space="preserve">
  <value>Current hardware: unavailable</value>
</data>
<data name="Device_Battery_DesiredKnown" xml:space="preserve">
  <value>Desired: {0}% ({1})</value>
</data>
```

```csharp
var current = snapshot.CurrentLimitPercent is { } currentPercent
    ? MainUiStrings.Format("Device_Battery_CurrentKnown",
        "Current hardware: {0}%", currentPercent)
    : MainUiStrings.Get("Device_Battery_CurrentUnavailable",
        "Current hardware: unavailable");
```

Retain the ` · ` separator and output order used by `BatteryChargeLimitStatusText.Text` (or localize the whole composite expression without changing its resulting PR2-English output). Preserve newlines in Center M confirmation messages exactly. Test format arity/validity when translating later; user-edited ko-KR values must not need C# changes.

## 5. Imperative resource retrieval — minimum reusable Main UI helper

The pilot in `SettingsPage.xaml.cs::GetDefaultAppThemeDescription()` directly instantiates `new ResourceManager(priPath)` and reads `"Resources/Settings_AppTheme_Description"`, with an English fallback. PR2 adds many Device C# lookups. Avoid constructing and parsing a new PRI resource manager on **every slider/status redraw**.

Create **one small Main UI-local static utility** (for example `src/SteamInputAddonforClaw.UI/MainUiStrings.cs`) that:
- Reuses the **same** `SteamInputAddonforClaw.UI.pri` path and `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` API already proven in PR #785.
- Lazily holds a single UI-process resource manager/map, initializing safely on first use; a missing/broken PRI or key returns the existing English fallback **without causing an exception from a Device render/mutation path**.
- Exposes only `Get(key, englishFallback)` and optionally `Format(key, englishFallback, args...)`. If formatting of a user-edited resource is malformed, use the valid English fallback instead of crashing the UI.
- Does **not** introduce app-language state, a language override, a new library, DI, managers per feature, background loading, or resource scanning at startup.
- Keeps resource keys stable and human-readable.
- Preferably lets the existing Settings theme description use this same helper, **without changing** its `failureDescription ?? default` priority or observable English behavior. Keep the narrow `GetDefaultAppThemeDescription()` wrapper if appropriate; update its existing source-based test to check equivalent behavior in the helper.

Illustrative shape (adapt namespace and SDK use to compile):

```csharp
internal static class MainUiStrings
{
    private static readonly Lazy<ResourceManager?> Resource = new(() =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.UI.pri");
            return new ResourceManager(path);
        }
        catch (Exception) { return null; }
    });

    internal static string Get(string key, string englishFallback)
    {
        try
        {
            var value = Resource.Value?.MainResourceMap
                .GetValue("Resources/" + key).ValueAsString;
            return string.IsNullOrEmpty(value) ? englishFallback : value;
        }
        catch (Exception) { return englishFallback; }
    }

    internal static string Format(string key, string englishFallback, params object?[] args)
    {
        try { return string.Format(System.Globalization.CultureInfo.CurrentUICulture, Get(key, englishFallback), args); }
        catch (FormatException)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentUICulture, englishFallback, args);
        }
    }
}
```

This is **not** an instruction to introduce generalized localization architecture. Use only this minimum shared lookup because `Settings` PR1 already performs the same resource operation, and repeated direct `new ResourceManager` calls from Device render paths would be avoidable churn.

## 6. Invariants and anti-regression requirements

Preserve all of the following verbatim in semantics:

- `DevicePage` remains the first eager page, while other Main UI pages retain deferred/on-demand warm-up. No new startup localization scan or page creation.
- `RefreshAsync()` uses the existing aggregate frontend snapshot once, handles transport failure exactly as today, and never changes physical controller/device authority.
- `RenderCenterMStartup` keeps automatic unsupported-device card collapse and correct state-dependent Enable/Disable availability.
- Center M confirmation cancellation does not issue an RPC; confirmation primary issues **exactly the existing** request followed by the existing reboot-bound behavior. No new restart mode or rollback.
- Center M backend `FailureMessage` precedence, including the explicit failed-disable restoration path, remains intact.
- CPU Boost and Power Mode dropdown options retain enum identities and original order. Resource strings are **labels only**, not persisted selections or binding values.
- TDP dirty drafts, generation/debounce, per-power-rail hardware result placement, slider limits, and mutation suppression stay the same.
- Battery charge-limit percentage rounding, commit guards, pending mutation flags, status/current/desired ordering, and correct InfoBar severity stay the same.
- `SetInfoBarOpen` collapses closed InfoBars fully and keeps the current spacing behavior.
- Device summary identity/support/badge is completely unaffected.
- PR1's `MainNav_Device.Content` and other existing localization samples remain working.
- No RESW key may become a business state authority, persisted value, RPC field, enum identifier, log/event category, or controller protocol constant.

## 7. Verify source RESW and **published** PRI for every newly added key

PR #785's `scripts/verify-ui-localization-pri.ps1` currently checks exactly three resource URIs by a literal pilot list; that is not sufficient proof for the many new Device values.

**Extend the existing verifier, not a parallel script**, to cover the newly added Device keys in both languages in the *actual* `ui/SteamInputAddonforClaw.UI.pri` produced by `dotnet publish`. Prefer obtaining the expected keys directly from the two source RESW files (same script repository root) so later tab PRs do not require editing a growing hardcoded pilot list every time.

Proofs required:
1. Both `en-US` and `ko-KR` files exist, are parseable and contain the same distinct nonempty key set; PR1's keys still exist.
2. Published PRI has the expected UI primary resource map and `en-US` as default.
3. Every source key can be resolved in published PRI under `Resources/…` (for XAML `Uid.Property` convert to the observed `Uid/Property` URI shape; for C# plain key use the direct name).
4. Each resource has exactly one nonempty `en-US` and `ko-KR` candidate. Do not pin the strings to fixed English values, assume exactly N keys, or require the two language *values* to be identical in perpetuity. Existing PR1 resource samples remain intact.
5. Do not regenerate or overwrite the WinUI-generated PRI using independent `makepri new`; only inspect the published artifact using the existing SDK `makepri dump` path.
6. Preserve the CI/Release shared `scripts/publish-layout.ps1` route and existing XBF/PRI preservation target. Add only the targeted verification/tests required; do not change MSIX/Velopack layout or runtime dependency mode.

If dynamic verification of all keys cannot be implemented reasonably with the observed `makepri dump` schema, explicitly list the Device keys in the **existing** verifier instead, but never hardcode English values or reject new keys. Prefer a simple reliable solution to a general-purpose PRI parsing framework.

## 8. Tests and completion criteria

Required code-side gates (no physical-device verification as implementation/review blocker):

- `dotnet restore` / Release build for the solution and the UI tests project; all existing xUnit and CI script checks continue to pass.
- Add/edit focused UI source tests to confirm every scoped Device XAML user-facing text is represented by an appropriate `x:Uid` property key and that the `DeviceSummaryCard` subtree has **no new `x:Uid`** or altered children/properties.
- Add tests for C# keys, per-mode selection label count/order/enum identity, Center M dialog button/action preservation and backend failure-message precedence (source-contract assertions are acceptable where UI integration tests are impractical).
- RESW tests verify equal key sets and nonempty values in both languages; format-string tests verify compatible numbered placeholders and safe formatting of the actual initial values. **Do not assert equality of en-US/ko-KR values after PR2**; translators will edit Korean independently later.
- Run `scripts/publish-layout.ps1` and `scripts/verify-publish-assets.ps1` so actual published Main UI PRI and all normal publish assets are checked. The new Device keys are present with both language candidates in PRI.
- Verify no changes to `DeviceSummaryPresentation.cs`, `RenderDeviceSummary()`, MainWindow controller/status routing, Runtime, Overlay, or IPC contracts.
- User-facing English presentation must remain text-identical in PR2, including confirmation dialog wording, line breaks, error priority, no unexpected null/blank labels, selection order, and per-power-source status formats.
- PR2 alone can be merged and packaged without any other tab-resourcing PR.
- Physical/on-device language display and full handheld lifecycle checks remain **user-owned after merge**. Do **not** block a correct PR solely because the local Codex has no MSI Claw hardware.

## 9. Target change surface and exclusions

Expected edits:

```text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw
src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw
src/SteamInputAddonforClaw.UI/MainUiStrings.cs      (new minimal helper, or equivalent)
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs  (optional narrow PR1 helper reuse)
scripts/verify-ui-localization-pri.ps1              (extend existing checker)
tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs   (only if existing source tests need updates)
other narrowly relevant UI resource/publish tests
```

No `csproj` change is expected unless the current WinUI build concretely requires one. No new dependencies or resource-file trees; keep **one RESW file per supported language** for the Main UI.

If a concrete current-main conflict is found while implementing, preserve the locked intent and explain the smallest deviation in the implementation PR. Do not introduce broader refactoring to "improve" the controller/Device code.

## 10. Completion handoff

Local Codex should submit one PR with a short implementation summary, explicit note that **only the Device tab excluding its summary card** was converted, a complete list of verified build/test/publish commands and results, and an explicit statement that on-device validation belongs to the user after merge. The work order is a design/spec artifact only; do not make implementation changes in the documentation commit.
