# Work Order — Main App RESW Localization PR1: Deployment-Safe Foundation

**Date:** 2026-10-14  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation owner:** Local Codex  
**Target:** One focused PR against the latest `main` (re-read HEAD before implementation)  
**Product:** Standalone Full PID1902; one Windows user and one interactive session  
**Scope:** Main WinUI 3 UI project and narrowly scoped resource/publish tests only  
**Highest priority:** **PR1 alone must be independently buildable, publishable, installable by the user, and safe to release without PR2–PR5.**

## 1. Locked decisions and expected result

Implement only the RESW foundation for the **Main App UI**.

1. Support exactly two resource languages initially: **`en-US`** and **`ko-KR`**.
2. **All `ko-KR` values must initially be verbatim copies of `en-US` English values**. No Korean translations in this PR, including Developer Menu strings.
3. Windows' display language selects the UI resources through the standard unpackaged Windows App SDK resource mechanism. Korean Windows should select `ko-KR`; supported English Windows should select `en-US`; unsupported languages should fall back to `en-US`. Confirm the actual fallback qualifier/configuration in the generated PRI; do not assume that merely naming a folder makes this work.
4. **No language picker** in Settings. No persisted language preference, language switch command, app restart, UI rebuild, or Runtime notification. Language changes may take effect on the next normal Main UI launch.
5. **PR1 is intentionally a tiny end-to-end vertical slice**: add the two RESW files, bind at most two existing XAML presentation strings, and prove one Main UI C# resource lookup. Every other UI string remains hard-coded and behaves exactly as before.
6. PR2–PR5 will move strings from individual pages to RESW independently. A mixture of localized and hard-coded UI strings is fully supported, so there must be **no all-pages conversion dependency**.
7. Existing `How to Use` Markdown localization stays as-is: `docs/howtouse/README_KO.md` versus `README.md`. Do not migrate document contents into RESW, rewrite the document renderer, or alter remote loading.
8. Developer Menu is a future **resource-extraction-only** scope. English text must remain in both language files, and this PR does not touch Developer/diagnostic UI.
9. Separate WinUI Overlay, ClawHUD external runtime, FSE Home MSIX, and the privileged Full1902 Runtime are entirely outside PR1.

**Release invariance:** With both resources initially identical, visible labels, navigation behavior, command dispatch, theme, startup performance characteristics, and the controller/overlay lifecycle should not materially change when PR1 is installed alone.

## 2. Required source and authority reading

Read these files on current `main` before editing:

- `docs/Full 1902 Implementation/README.md` and its active precedence guidance.
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`: Runtime owns the controller; frontend is disposable.
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`: elevated Runtime launches frontend; no privilege changes.
- `docs/work-order/2026-10-13_MAIN_UI_DEVICE_FIRST_DEFERRED_PAGE_WARMUP_AND_ON_DEMAND_CATALOG_WORK_ORDER.md`: device-first initial UI and deferred page construction.
- `src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj`
- `src/SteamInputAddonforClaw.UI/Program.cs`, `App.xaml`, `App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml`, `MainWindow.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml`, `SettingsPage.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs`
- `scripts/publish-layout.ps1`, `scripts/verify-publish-assets.ps1`, `scripts/tests/verify-publish-assets.tests.ps1`
- `scripts/pack.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- `tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj` and relevant source-assertion tests.

Windows App SDK reference (pay special attention to **"Loading strings in unpackaged applications"**):  
https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings

Windows SDK `makepri dump` reference:  
https://learn.microsoft.com/en-us/windows/uwp/app-resources/makepri-exe-command-options

These Microsoft documents are implementation references, not evidence that our existing build already embeds the requested resource maps.

## 3. Confirmed existing code and risk analysis

### 3.1 Unpackaged Main UI and separate Runtime

`SteamInputAddonforClaw.UI.csproj` currently has:

```xml
<WindowsPackageType>None</WindowsPackageType>
<UseWinUI>true</UseWinUI>
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
<AssemblyName>SteamInputAddonforClaw.UI</AssemblyName>
```

The Main UI does **not** have an MSIX package manifest. It is independently published into `artifacts/publish/ui/`, while the Runtime is in the publish root and the Overlay in `artifacts/publish/overlay/`. Do not introduce packaging, a package identity, new runtime dependencies, a second Windows App SDK distribution strategy, or changes to privilege/elevation.

`Program.cs` initializes `UiSingleInstanceGate` and enters `Application.Start`; `App.xaml.cs` acquires the Runtime bootstrap before building `MainWindow`. `MainWindow.xaml` eagerly creates the Device page and defers other pages. Preserve all of that.

### 3.2 Existing PRI/XBF preservation is necessary but not sufficient

The UI project already defines `CopyMissingWinUIPublishAssets` after `Publish`, copying generated `*.xbf`, `SteamInputAddonforClaw.UI.pri`, and assets from `$(OutDir)` to `$(PublishDir)`.

`scripts/verify-publish-assets.ps1` already requires:
- `ui/SteamInputAddonforClaw.UI.exe` and `.dll`;
- `ui/SteamInputAddonforClaw.UI.pri`;
- `ui/App.xbf`, `ui/MainWindow.xbf`, and selected page XBF files.

**But currently it only tests PRI presence; it does not prove `en-US`, `ko-KR`, or localized key candidates are inside the final published PRI.** A stale PRI copied from `OutDir` could satisfy these existing checks while missing newly added RESW strings.

### 3.3 Existing publish/test chain

`scripts/publish-layout.ps1` performs `dotnet publish` for Runtime, UI, then Overlay into the same release layout. `scripts/pack.ps1` calls `publish-layout.ps1`, then the existing verifier, and finally Velopack packaging. CI does the publish layout and verification separately.

`scripts/tests/verify-publish-assets.tests.ps1` deliberately uses **placeholder fake PRI files**. Do not make this existing fixture unexpectedly fail by unconditionally parsing fake PRI bytes in the shared generic verifier without adapting the tests. Prefer a small **separate actual-PRI verification stage** called from the real UI publish path, as detailed below.

## 4. Minimal PR1 implementation

### 4.1 Two real RESW files in the Main UI project

Add:

```text
src/SteamInputAddonforClaw.UI/
  Strings/
    en-US/
      Resources.resw
    ko-KR/
      Resources.resw
```

Use proper WinUI `.resw` XML with `<root>` and `<data name="..." xml:space="preserve"><value>...</value></data>` entries (the standard resource schema as required by the tooling). Use `en-US` as the **resource default/fallback language**, not `ko-KR`.

Initial exact pilot entries in **both** files:

| RESW key | en-US value | ko-KR value | Consumer |
| --- | --- | --- | --- |
| `MainNav_Device.Content` | `Device` | `Device` | `MainWindow.xaml` |
| `HowToUse_Loading.Text` | `Loading documentation…` | `Loading documentation…` | `HowToUsePage.xaml` |
| `Settings_AppTheme_Description` | `Choose the main app appearance.` | `Choose the main app appearance.` | `SettingsPage.xaml.cs` |

The keys are descriptive and stable. For XAML `x:Uid`, the RESW suffix must match the actual element property, e.g. `.Content` for a `NavigationViewItem` and `.Text` for a `TextBlock`; C# uses an unsuffixed simple string key.

Do **not** extract all 291 previously inventoried XAML text candidates or all dynamic C# strings in PR1. Those belong to PR2–PR5. Do not create untranslated or speculative empty entries.

### 4.2 One safely localized startup/navigation XAML string

Modify only the existing Device navigation item by adding `x:Uid`, retaining the source `Content` as its safe English fallback:

```xml
<NavigationViewItem
    x:Name="DeviceNavigationItem"
    x:Uid="MainNav_Device"
    Content="Device"
    Tag="Device">
    ...
</NavigationViewItem>
```

The `MainNav_Device.Content` RESW candidate is `Device` in both languages. Preserve `Tag="Device"`, `x:Name`, icon, ordering, events, and `MainNavigationPage` selection behavior. **Do not localize navigation `Tag` values or any stable command/enum identifiers.**

This deliberately tests the actual MainWindow XAML/PRI path used on normal Main UI startup.

### 4.3 One safely localized lazily loaded XAML string

In `HowToUsePage.xaml`, add only `x:Uid="HowToUse_Loading"` to the existing `TextBlock Text="Loading documentation…"`. Leave the inline English `Text` intact as a fallback. Do not change HowToUse page lifecycle, WebView2, remote URLs, document source choice, retry behavior, or `CultureInfo.CurrentUICulture` in PR1.

### 4.4 Small C# resource lookup proving the code path

Prove that an actual Main UI C# consumer can read a simple `.resw` key by using `Settings_AppTheme_Description` for the existing fallback app-theme description in `SettingsPage.xaml.cs`.

Current baseline: `DefaultAppThemeDescription = "Choose the main app appearance."` and `SetAppThemeSelection(...)` assigns `AppThemeCard.Description = failureDescription ?? DefaultAppThemeDescription;`.

For the ordinary (no failure description) case, resolve the key via a **minimal Main-UI-only resource lookup**; retain the exact existing English constant as the fallback if that resource cannot be read. For example, conceptually:

```csharp
AppThemeCard.Description = failureDescription
    ?? MainUiStrings.GetString(
        "Settings_AppTheme_Description",
        DefaultAppThemeDescription);
```

`MainUiStrings.GetString` above is an illustrative name, not a required architecture or an API supplied by the repo. Implement as a tiny local utility or direct lookup, whichever is simpler. Use the current Windows App SDK resource APIs that compile with **Microsoft.WindowsAppSDK 2.5.1**. Critically, for an unpackaged app do not assume the parameterless `ResourceLoader` has an automatically usable default view; where required, load the **actual UI PRI** explicitly via the documented `ResourceManager`/`ResourceLoader` path. Confirm the exact URI/resource-map path by inspecting `makepri dump`, not by guessing.

Any resource lookup failure in this **single pilot caller** must yield the previous English UI text without throwing during startup/page construction. Preserve the existing `failureDescription` priority. Do not add a general string service, dependency injection, logging flood, localization state machine, or automatic language override.

### 4.5 Correct PRI generation without breaking existing XAML resources

Check whether the existing WinUI/MSBuild targets automatically discover and merge `Strings/*/Resources.resw` into `SteamInputAddonforClaw.UI.pri` in the current **unpackaged** configuration. If yes, use the existing targets and avoid custom `MakePri new` steps.

If automatic generation does not include the RESW resources, implement the smallest **build-integrated** deterministic fix. Critical constraints:

- Rebuild resources on a changed `.resw`; no stale incremental PRI.
- Preserve the PRI resource entries needed by current WinUI XAML/XBF controls, not only the three new strings.
- Keep a single valid application PRI in the actual UI output location, and copy that current PRI into the publish `ui/` folder.
- Preserve reference/NuGet PRI resources and other existing local PRI behavior as required; do not replace the project's generated PRI with a smaller, strings-only PRI.
- For an explicitly generated unpackaged PRI, the Microsoft documented configuration may need to omit the `<packaging>` section and retain the necessary indexers. Use that only if actually needed, and confirm its root resource map, default language, and resource candidates.
- Do not copy raw RESW files at runtime as an alternative localization system, add `.resx`/`ResourceManager` from `System.Resources`, or create a second unmanaged translation directory.
- No modifications to `SteamInputAddonforClaw.Overlay.csproj`, FSE MSIX, Runtime, or shared IPC contracts.

## 5. Mandatory local Codex verification — build and resource contents, **not installation**

The developer PC **cannot install this application for field verification**. Consequently:

**Local Codex MUST build, test, publish into a staging directory, and inspect the generated/published PRI and assets. Local Codex MUST NOT install the application, require a real MSI Claw, switch physical PID modes, or perform a mandatory hardware/field validation step. Installation and all device-level checks are performed later by the user after merge. Hardware testing is not a PR/review/merge blocker.**

### 5.1 Expected local commands (adapt to current checkout and restore state)

```powershell
dotnet restore SteamInputAddonforClaw.slnx
dotnet restore tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-build --no-restore

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 -Version 0.1.0 -Configuration Release -PublishDirectory artifacts/publish -NoRestore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 -PublishDirectory artifacts/publish
```

If the already checked-in CI build chain has additional relevant tests, run them. **`dotnet publish` is required; producing a normal `dotnet build` PRI alone is not enough.** Do not use Velopack installation, execute the installed UI, or require a real user session for this task.

### 5.2 Inspect the actual final published PRI, not just its filename

Use Windows SDK `MakePri.exe dump` on:

```text
artifacts/publish/ui/SteamInputAddonforClaw.UI.pri
```

Example command, after locating the SDK-installed `makepri.exe`:

```powershell
& $makePri dump /if "artifacts/publish/ui/SteamInputAddonforClaw.UI.pri" /of "$env:TEMP/SteamAddon-UI-PRI.xml" /dt Detailed /o
```

Verify **both language candidates of each pilot key**, using the actual names/URI shape seen in the dump. The precise XML dump structure or qualifier casing may differ; inspect it rather than hardcoding an unsupported path assumption:

- `MainNav_Device.Content` candidate value `Device` for `en-US` and `ko-KR`;
- `HowToUse_Loading.Text` candidate value `Loading documentation…` for both languages;
- `Settings_AppTheme_Description` candidate value `Choose the main app appearance.` for both languages;
- `en-US` is the configured/default fallback and an unsupported display language does not have an intentionally configured `ko-KR` fallback;
- Existing `App.xbf`, `MainWindow.xbf`, `Views/HowToUsePage.xbf`, and normal other UI XBF files remain present;
- The packaged `ui/` folder contains one correct **current** application PRI, not an outdated/stale one or only a dependency PRI;
- `overlay/SteamInputAddonforClaw.Overlay.pri` and other Overlay artifacts remain untouched/present;
- Framework-dependent publishing (`SelfContained=false`, external Windows App SDK) is unchanged.

**A positive dump showing one language, or only generic framework resources, is a failure.** Build success, a nonzero PRI size, and an existing generic asset verifier pass are necessary but insufficient.

### 5.3 Wire the real PRI check into the existing CI/release publish path

Introduce a small dedicated script if needed, e.g. `scripts/verify-ui-localization-pri.ps1`, that checks the **actual published UI PRI** with `makepri dump` and fails if either language or any PR1 sentinel string is absent or wrong. Invoke it from the existing canonical publish path (prefer `scripts/publish-layout.ps1` immediately after the Main UI `dotnet publish`). This path is already used by both:

- `.github/workflows/ci.yml` -> `scripts/publish-layout.ps1`;
- `.github/workflows/release.yml` -> `scripts/pack.ps1` -> `scripts/publish-layout.ps1`.

Confirm these paths by reading the current YAML and scripts. **Include any CI/release workflow adjustment needed to guarantee the new check runs, but do not modify YAML merely to add a redundant call** if the existing shared publish path is sufficient.

Keep one authoritative check; do not run the same full PRI dump repeatedly through separate publish, verification, CI, and release steps. Reuse Windows SDK tool discovery conventions where practical, not a hard-coded SDK installation version. Missing `makepri.exe`, a dump error, or a missing/incorrect language candidate must produce a clear nonzero exit code in local verification and in the eventual GitHub CI/release execution; never skip verification silently.

The generic `scripts/verify-publish-assets.ps1` tests intentionally use **fake PRI placeholder files**. Preserve those fixture tests and do not break them by parsing mock PRI files as real binary assets. No installation on the developer PC is needed.

**The developer does not need to run an actual GitHub-hosted CI or Release job while implementing this work order.** The user will trigger/observe the real GitHub CI later. Implementation must make the shared verification reachable and fail-closed from both workflow call paths, then verify it locally.

### 5.4 Mandatory local CI-equivalent validation before handing off PR1

**Local Codex MUST run the non-installing, non-device-dependent portions of the checked-in GitHub CI workflow on the developer Windows PC**, not just `dotnet build`. Use the current `.github/workflows/ci.yml` as the source of truth if it changes.

Run the following in the local repository root using the same release configuration as CI (adapt shell invocation to the actual PowerShell version):

```powershell
# CI preparation and dependency/tooling checks:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-viiper.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-viiper.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/update-viiper.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/adopt-viiper.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/viiper-existing-automation-check.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/viiper-base-drift-check.tests.ps1

dotnet restore SteamInputAddonforClaw.slnx
dotnet restore tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore

# Run every test project in tests/, matching GitHub CI:
$projects = @(Get-ChildItem -Path tests -Filter *.csproj -Recurse -File)
foreach ($project in $projects) {
    dotnet test $project.FullName -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Test failed: $($project.FullName)" }
}

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/release-metadata.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/report-publish-size.tests.ps1

# Publish the same Runtime + UI + Overlay staging layout used by CI:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 -Version 0.1.0 -Configuration Release -PublishDirectory artifacts/publish -NoRestore

# Preserve the existing asset verifier and size reporting:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 -PublishDirectory artifacts/publish
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/report-publish-size.ps1 -PublishDirectory artifacts/publish

# The shared publish path must also automatically run the new real-PRI
# en-US / ko-KR content verification from Section 5.3.
```

Check **each command's exit status**; do not continue treating a failed command as a pass. Use the existing project's CI commands for reference, but adjust control flow if the local shell would otherwise ignore a failure.

**Important deliberate exception:** The existing GitHub CI `Startup process smoke test` invokes the privileged Runtime executable with `--background`. Do **not** run that process on the developer PC under this work order: it may exercise real controller/runtime initialization and is unnecessary for a Main UI RESW-only change. Also do **not** install Velopack output or execute any live HID/VIIPER/controller operations. The user's post-merge MSI Claw checks cover real application/device behavior.

For a narrowly scoped failure unrelated to this PR, record the exact command and reason in the PR description. Do not silently report full local CI-equivalent success. The expected handoff includes a concise table/report: build PASS/FAIL, all relevant automated tests PASS/FAIL, local publish PASS/FAIL, generic published-asset verification PASS/FAIL, actual `makepri dump` both-language verification PASS/FAIL, and any justified omissions.

**Running the actual GitHub Actions CI/Release workflow is NOT an implementation prerequisite here.** Later PR review/merge follows the project's normal CI policy; do not claim remote CI PASS based on local execution alone.

## 6. Focused tests

Add or update a small number of source/XML-level tests in `tests/SteamInputAddonforClaw.UiTests/`:

1. Both valid RESW XMLs exist and have the same key set; exactly `en-US` and `ko-KR` are introduced. All current `ko-KR` values are identical to `en-US` values.
2. The expected three pilot keys and values exist.
3. `MainWindow.xaml` has the exact new `x:Uid` on `DeviceNavigationItem`, preserving `Tag="Device"` and `Content="Device"`.
4. `HowToUsePage.xaml` has `x:Uid` on the loading text, preserving the literal fallback text.
5. The C# code path preserves the previous `DefaultAppThemeDescription` fallback and the higher-priority `failureDescription`. Any existing source-structure assertions for `SettingsPage` and MainWindow continue to pass.
6. The **actual PRI payload verification**, separately from fake test fixtures, runs during CI/release publish and detects missing `ko-KR` or missing key candidates.

If the current test suite has literal expectations affected by the pilot change, update only those necessary expectations. Avoid brittle tests based on specific whitespace or generated compiler output.

## 7. Non-goals / prohibited changes

- **No installation or hardware verification on the Codex PC.** The user owns post-merge installation and real-device verification.
- No immediate translation to Korean. No English copyediting in this PR. The owner will later edit `ko-KR` by hand and use AI to update `en-US` wording.
- No new Settings language dropdown or restart behavior.
- No localization of all screens. The rest of the Main UI stays literal English until PR2–PR5.
- No overhaul of `HowToUseMarkdownRenderer`; the guide body already has a separate Korean/English Markdown path.
- No new localization work in Developer/diagnostic pages; later extraction keeps both languages' values English.
- No changes to Overlay/ClawHUD/FSE Home.
- No Core/Runtime/Contracts/FrontendTransport changes, new IPC messages, PID1901/PID1902, HidHide, VIIPER, device/driver ownership, startup tasks, privilege manifest, power, suspend/resume, or recovery.
- No application `PrimaryLanguageOverride`, per-thread culture forcing, arbitrary `ResourceContext` global mutation, registry probing, locale cache, general-purpose localizer DI, language watcher/timer, or extra processes.
- No assumption that an `x:Uid` property name remains valid when applied to an unrelated control type.

## 8. Acceptance checklist — merge-safe PR1 independently

- [ ] PR1 modifies Main UI resources, at most the two specified XAML controls and one C# pilot text path, and only needed publishing/tests; no controller/Runtime/Overlay changes.
- [ ] There are exactly two initial RESW languages, `en-US` and `ko-KR`; all values are English and exactly equal.
- [ ] Windows display language selects resources by the normal unpackaged resource mechanism; English fallback is correctly established in the actual PRI.
- [ ] Existing inline UI text is retained for the two `x:Uid` pilot controls and the C# caller keeps a deterministic English fallback.
- [ ] Existing Main UI entry point, navigation identity, page warmup, data binding, settings updates, and Theme event behavior remain intact.
- [ ] Normal `dotnet build`, relevant tests, **`dotnet publish`**, and existing publish-asset verification pass.
- [ ] `makepri dump` (or an equivalently concrete Windows SDK PRI inspection) proves all three pilot keys have **both** `en-US` and `ko-KR` candidates in the **final published** `SteamInputAddonforClaw.UI.pri`.
- [ ] The shared publish script executes the real PRI-content verification and is referenced from **both** CI and Release workflow call paths, without breaking synthetic fake-PRI tests; any necessary workflow integration changes are included in PR1.
- [ ] Local Codex has run the checked-in CI's non-installing, non-device-dependent restore/build/test/publish/verifier sequence and reported command outcomes. No real GitHub-hosted CI run or privileged Runtime startup smoke test is required from the developer PC.
- [ ] UI remains framework-dependent; XBFs, application PRI, Overlay payload, fixed FSE MSIX, and Velopack staging layout remain intact.
- [ ] No install instructions or field-device steps are required from Codex. Record commands/results for build, tests, publish, and PRI inspection in the PR description.
- [ ] PR1 may be released **without** PR2, PR3, PR4, or PR5. Later PRs add independent RESW keys and bindings without forcing any simultaneous migration.

**Failure policy:** If the PRI merge or unpackaged C# lookup cannot be proven in the compiled/published output, do not merge a nominally complete but unverified foundation. Fix the minimal actual resource-generation/lookup issue inside PR1; do not try to sidestep it with a new translation framework or conversion of unrelated screens.

**User-owned post-merge role:** The user installs the release on an actual MSI Claw, checks display language behavior and UI appearance, and performs hardware/lifecycle validation. Those device-level observations may motivate a later repair PR, but are **not blockers** for a code-correct PR1 with passing build/CI/resource verification.
