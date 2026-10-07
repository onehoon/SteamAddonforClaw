# Work Order — Move Logging Controls to General Settings

> **Date:** 2026-10-08  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@2d22f5cdfa62fbfb72da8d11584b98ce9228a701`  
> **Product architecture:** standalone Full1902  
> **PR shape:** one focused PR, one logical commit  
> **Feature surface:** Main App → Settings / Developer Menu  
> **Goal:** make log level and log-folder access available to normal users without changing Runtime logging policy

---

# 1. Goal

Move the existing Logging settings card out of Developer Menu and into the normal Settings page.

Reason:

- normal users may need to provide logs for support/troubleshooting;
- hiding log controls behind Developer Menu makes that unnecessarily difficult;
- logging configuration is an app-level support setting, not a synthetic developer diagnostic;
- the existing product default is already `Info`, so this work should not change logging policy.

Target Settings layout:

~~~text
Settings

[Application updates]

[Windows Gaming Full Screen Experience]

[Show only current power source]

[Logging]
Choose log detail.

[ Info ▼ ] [ Open folder ]

[Required Components]

[Developer Menu]
~~~

Target Developer Menu header remains:

~~~text
Developer Menu                         [ Log Folder ]
~~~

The top Developer Menu `Log Folder` button must stay.

---

# 2. Required source review

Before implementation, read and preserve:

~~~text
docs/Full 1902 Implementation/README.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/work-order/FRESH_INSTALL_SETTINGS_LOGGING_AND_CONTROLLER_PREREQUISITE_REPAIR_WORK_ORDER_2026-10-03.md
~~~

Inspect current code:

~~~text
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs

src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs

src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/LogLevelBootstrap.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
~~~

Also inspect relevant UI tests under:

~~~text
tests/SteamInputAddonforClaw.UiTests/
~~~

---

# 3. Current implementation facts

## 3.1 Product default is already Info

Current settings authority already defines:

~~~csharp
public enum AppLogPreference { Off, Info, Debug }

public sealed record AppSettings(
    AppLogPreference LogLevel = AppLogPreference.Info,
    ...)
~~~

Fresh-install bootstrap also resolves a missing settings file to `Info`.

Existing explicit persisted values remain authoritative:

~~~text
Off   → remains Off
Info  → remains Info
Debug → remains Debug
~~~

Therefore this PR must **not** change:

- `AppSettings` defaults;
- `LogLevelBootstrap`;
- `SettingsStore`;
- `AppSettingsPolicy`;
- Runtime log filtering;
- frontend protocol;
- `FrontendLogLevel`.

This is a UI ownership move only.

## 3.2 Current Developer Menu text is stale

Current Developer card says:

~~~text
Logging
Off by default. Info enables runtime diagnostics; Debug is verbose and for developer use only.
~~~

This is no longer correct because the product default is `Info`.

Remove this card from Developer Menu rather than editing it in place.

## 3.3 Existing frontend path is already sufficient

The current UI already uses:

~~~csharp
_frontend.SetLogLevelAsync(...)
_frontend.GetBootstrapAsync()
bootstrap.Settings.LogLevel
bootstrap.LogDirectoryPath
~~~

No new Runtime RPC or settings contract is required.

---

# 4. PR structure

Use one PR with one logical commit:

~~~text
feat(settings): expose logging controls to normal users
~~~

Do not split this small ownership move into multiple commits unless current-main conflicts require it.

---

# 5. Settings page — add Logging card

Add the Logging card to:

~~~text
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
~~~

Place it after:

~~~text
Show only current power source
~~~

and before:

~~~text
Required Components
Developer Menu
~~~

Use exactly this compact user-facing copy:

~~~text
Header:
Logging

Description:
Choose log detail.
~~~

Do not add a long explanation.

The control row should remain one line:

~~~text
[ Off / Info / Debug ▼ ] [ Open folder ]
~~~

Recommended XAML direction:

~~~xml
<ctcontrols:SettingsCard
    Header="Logging"
    Description="Choose log detail.">
    <ctcontrols:SettingsCard.HeaderIcon>
        <SymbolIcon Symbol="List" />
    </ctcontrols:SettingsCard.HeaderIcon>

    <StackPanel
        Orientation="Horizontal"
        Spacing="8"
        VerticalAlignment="Center">
        <ComboBox
            x:Name="LogLevelComboBox"
            Width="120"
            SelectionChanged="LogLevelComboBox_SelectionChanged">
            <ComboBoxItem Content="Off" />
            <ComboBoxItem Content="Info" />
            <ComboBoxItem Content="Debug" />
        </ComboBox>

        <Button
            Content="Open folder"
            Click="OpenLogFolderButton_Click" />
    </StackPanel>
</ctcontrols:SettingsCard>
~~~

The exact XAML formatting may follow repository style.

Do not make this an Expander.

Do not add an InfoBar.

Do not add explanatory text under the controls.

The card should stay compact.

---

# 6. Settings page — own the logging UI state

Move the existing Main UI logging-control ownership from `DeveloperPage` to `SettingsPage`.

Add only the small state already needed by the current implementation:

~~~csharp
private bool _isInitializingLogLevel;
private FrontendLogLevel _lastKnownLogLevel;
private string _logDirectoryPath = string.Empty;
~~~

In:

~~~csharp
SettingsPage.Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
~~~

initialize:

~~~text
_frontend
_lastKnownLogLevel = bootstrap.Settings.LogLevel
_logDirectoryPath = bootstrap.LogDirectoryPath
LogLevelComboBox selection = bootstrap.Settings.LogLevel
~~~

Use the existing mapping:

~~~text
Off   → index 0
Info  → index 1
Debug → index 2
~~~

On a fresh install, the UI therefore naturally shows:

~~~text
Info
~~~

because the authoritative Runtime/settings snapshot already says `Info`.

Do not hard-code `SelectedIndex="1"` in XAML as a second default authority.

---

# 7. Log-level mutation behavior

Preserve the existing mutation behavior.

On selection change:

~~~text
ComboBox selection
→ map to FrontendLogLevel
→ _frontend.SetLogLevelAsync(level)
→ accept returned authoritative Settings snapshot
→ update _lastKnownLogLevel
→ render returned level
~~~

Conceptual code may remain equivalent to the existing Developer implementation:

~~~csharp
private async void LogLevelComboBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs args)
{
    if (_isInitializingLogLevel
        || _frontend is null
        || LogLevelComboBox.SelectedItem is not ComboBoxItem item
        || item.Content is not string value)
    {
        return;
    }

    var level = value switch
    {
        "Debug" => FrontendLogLevel.Debug,
        "Info" => FrontendLogLevel.Info,
        _ => FrontendLogLevel.Off,
    };

    try
    {
        var result = await _frontend.SetLogLevelAsync(level);
        _lastKnownLogLevel = result.LogLevel;
        SetLogLevel(_lastKnownLogLevel);
    }
    catch (Exception exception)
    {
        AppLog.Warn("Settings", "Log level update failed.", exception);
        await RefreshLoggingStateAsync();
    }
}
~~~

Naming may follow current code style.

Do not duplicate settings persistence in Main UI.

`SetLogLevelAsync` / Runtime settings authority remains the sole mutation path.

---

# 8. Failure rollback

Preserve the current practical rollback behavior.

If `SetLogLevelAsync` fails:

~~~text
try _frontend.GetBootstrapAsync()
→ refresh authoritative LogLevel
→ refresh LogDirectoryPath
→ re-render ComboBox

if refresh also fails
→ restore _lastKnownLogLevel
~~~

Do not add retry loops, epochs, mutation queues, or a new settings coordinator for this one dropdown.

A direct async mutation with authoritative refresh is enough.

---

# 9. Settings Log Folder button

Add:

~~~text
Open folder
~~~

to the right of the Logging dropdown.

Use the same log path supplied by:

~~~csharp
bootstrap.LogDirectoryPath
~~~

and the same practical Explorer launch behavior already used in Developer Menu.

Conceptual implementation:

~~~csharp
private void OpenLogFolderButton_Click(object sender, RoutedEventArgs args)
{
    try
    {
        Process.Start(
            new ProcessStartInfo(
                "explorer.exe",
                $"\"{_logDirectoryPath}\"")
            {
                UseShellExecute = true
            });
    }
    catch (Exception exception)
    {
        AppLog.Warn("Settings", "Log folder could not be opened.", exception);
    }
}
~~~

Add:

~~~csharp
using System.Diagnostics;
~~~

to `SettingsPage.xaml.cs` if required.

Do not add:

- a FolderPicker;
- a selectable custom log directory;
- a Copy Path button;
- a ZIP/export workflow;
- a support-upload feature.

This button only opens the existing authoritative log folder.

---

# 10. Developer Menu — remove only the Logging card

Remove from:

~~~text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
~~~

the bottom Logging card containing:

~~~text
Logging
Off / Info / Debug
~~~

Do not remove or move any actual developer diagnostic:

- Environment Discovery;
- PID1902 Input Cadence;
- Vibration Test;
- Gyro / Sensor Test;
- Fan Hardware Probe;
- Battery Charge Limit Test;
- GameInput System Button Probe;
- other current developer diagnostics.

---

# 11. Developer Menu — keep top Log Folder button

Keep the existing header exactly conceptually as it is:

~~~text
Developer Menu                         [ Log Folder ]
~~~

Keep:

~~~csharp
_logDirectoryPath = bootstrap.LogDirectoryPath;
OpenLogFolderButton_Click(...)
~~~

Developer Menu still benefits from immediate log-folder access while running diagnostics.

It is acceptable for both Settings and Developer Menu to have a button that opens the same folder.

Do **not** add a new shared `LogFolderLauncher`, service, interface, manager, or utility merely to deduplicate these two tiny handlers.

The duplicate two-button entry point is intentional UI access, not duplicated state authority.

---

# 12. DeveloperPage code cleanup

After the Logging card moves out, remove DeveloperPage members that exist only for log-level mutation:

~~~text
_isInitializingLogLevel
_lastKnownLogLevel
LogLevelComboBox_SelectionChanged
SetLogLevel(...)
log-level refresh/rollback code that has no other caller
~~~

Retain:

~~~text
_logDirectoryPath
OpenLogFolderButton_Click
bootstrap parameter needed for LogDirectoryPath
all developer diagnostic state
all diagnostic events
all diagnostic lifecycle behavior
~~~

Do not change Developer Menu lazy materialization.

Do not force DeveloperPage to initialize at MainWindow startup just because Settings now exposes logging.

---

# 13. MainWindow

No MainWindow architecture change should be necessary.

Current constructor already provides:

~~~csharp
SettingsContent.Initialize(_bootstrap, _frontend);
~~~

which is sufficient for:

~~~text
bootstrap.Settings.LogLevel
bootstrap.LogDirectoryPath
SetLogLevelAsync
GetBootstrapAsync
~~~

Current lazy Developer page initialization already provides its own bootstrap for the retained top Log Folder button.

Do not:

- add another Settings owner to MainWindow;
- move log mutation into MainWindow;
- eagerly materialize DeveloperPage;
- change frontend invalidation semantics for this PR.

---

# 14. Tests

Add/update focused UI tests only.

Prefer extending existing source/architecture coverage rather than creating UI automation infrastructure.

Verify at minimum:

## 14.1 Settings owns Logging

~~~text
SettingsPage.xaml
→ contains Header="Logging"
→ contains Description="Choose log detail."
→ contains Off / Info / Debug
→ contains Open folder
→ control order is ComboBox then button
~~~

## 14.2 Developer Menu no longer owns log level

~~~text
DeveloperPage.xaml
→ does not contain the Logging SettingsCard
→ does not contain LogLevelComboBox
~~~

## 14.3 Developer top Log Folder remains

~~~text
DeveloperPage.xaml
→ still contains header Log Folder button
→ still wires OpenLogFolderButton_Click
~~~

## 14.4 Settings initializes from authoritative snapshot

Cover the mapping helper or source contract proving:

~~~text
FrontendLogLevel.Off   → Off
FrontendLogLevel.Info  → Info
FrontendLogLevel.Debug → Debug
~~~

Do not invent a UI-side default.

The existing Runtime/settings tests remain the authority proving fresh-install default `Info`.

## 14.5 Mutation failure rollback

If the current UI test style permits a focused fake frontend test without significant infrastructure, verify:

~~~text
SetLogLevelAsync failure
→ authoritative refresh attempted
→ ComboBox returns to authoritative/last-known level
~~~

If that requires creating a large WinUI harness, keep this as manual/source-level validation instead.

Do not overbuild tests for this small UI move.

---

# 15. Explicit non-goals

Do not change:

~~~text
AppSettings.LogLevel default
LogLevelBootstrap
SettingsStore serialization
AppSettingsPolicy
AppLog filtering
log file naming
log retention
log directory location
FrontendLogLevel enum
SetLogLevel RPC
frontend protocol version
Runtime/controller lifecycle
Full1902 ownership
HidHide
VIIPER
PID1901/PID1902
Overlay
~~~

Do not add a fourth log level.

Keep exactly:

~~~text
Off
Info
Debug
~~~

---

# 16. Expected files

Likely production diff:

~~~text
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
~~~

Likely test diff:

~~~text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

or one existing/small focused UI test file if clearer.

Expected **no changes** to Runtime/settings/contracts.

---

# 17. Manual validation

## A. Normal Settings visibility

With Developer Menu disabled/hidden, open Settings.

Expected:

~~~text
Logging
Choose log detail.

[ Info ▼ ] [ Open folder ]
~~~

The Logging card must remain visible to ordinary users even when Developer Menu is unavailable.

## B. Default

On a fresh/default install:

~~~text
Logging → Info selected
~~~

No UI-side forced default should be involved.

## C. Change level

Verify:

~~~text
Info → Debug
Debug → Off
Off → Info
~~~

Restart Main App / Runtime according to normal test flow and confirm the persisted selection remains authoritative.

## D. Open folder from Settings

Press:

~~~text
Open folder
~~~

Expected:

~~~text
Explorer opens the existing Addon log directory
~~~

## E. Developer Menu

Enable/open Developer Menu.

Expected:

~~~text
top-right Log Folder button still exists
Logging level card is gone
all developer diagnostics remain
~~~

Press the top `Log Folder` button and verify it still opens the same directory.

## F. Compact layout

At normal Main App size verify:

- Logging card stays one normal card row;
- description remains one short line;
- dropdown and `Open folder` are on one line;
- no unnecessary vertical growth.

---

# 18. Validation commands

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
~~~

Preserve current publish verification.

---

# 19. Acceptance criteria

- [ ] Logging card is visible in normal Settings.
- [ ] Card header is `Logging`.
- [ ] Card description is exactly `Choose log detail.`.
- [ ] Dropdown contains only `Off`, `Info`, and `Debug`.
- [ ] `Open folder` button is directly beside the dropdown.
- [ ] Fresh/default state displays `Info` from the authoritative settings snapshot.
- [ ] Existing explicit Off/Info/Debug persistence behavior is unchanged.
- [ ] Settings uses existing `SetLogLevelAsync`.
- [ ] Failed mutation returns to authoritative/last-known state.
- [ ] Settings `Open folder` opens `bootstrap.LogDirectoryPath`.
- [ ] Developer Menu Logging card is removed.
- [ ] Developer Menu top `Log Folder` button remains.
- [ ] Developer diagnostics remain unchanged.
- [ ] No Runtime/settings/protocol changes.
- [ ] No new logging abstraction or manager.
- [ ] Full tests pass.

---

# 20. Final invariant

After this PR:

~~~text
ordinary user
→ Settings
→ Logging
→ choose Off / Info / Debug
→ Open folder
→ support log collection is easy

developer
→ Developer Menu
→ top Log Folder remains available
→ developer probes remain isolated
~~~

The product rule is:

> **Log collection is a normal support capability; synthetic hardware diagnostics remain developer-only.**
