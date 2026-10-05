# Work Order — XBOX PR3: Add Normal-Domain Xbox App Front-Button Action

> **Date:** 2026-10-05  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@69fe68899e2b390a12d30aa55a5433082e3bd055`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`  
> **Existing front-button authority:** `docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md`  
> **Purpose:** close XBOX PoC C with a real user-facing but isolated front-button action  
> **Scope:** WING / Gamebar Button and Center M Button, Normal domain only  
> **Out of scope:** Guide-button emulation, Xbox Game Bar, XBOX game profiles, XBOX catalog UI, active-game detection changes, presentation changes, Overlay changes

---

## 1. Goal

Add one explicit front-button action:

```text
Xbox
```

Internal action:

```csharp
FrontButtonAction.XboxApp
```

The action must be available to both physical front buttons, but **only in the Normal mapping domain**:

```text
Gamebar / WING
  Normal                  → Xbox allowed
  Steam Game / Big Picture → Xbox NOT allowed

Center M
  Normal                  → Xbox allowed
  Steam Game / Big Picture → Xbox NOT allowed
```

Pressing a button mapped to `Xbox` must activate the installed **Xbox PC app** directly through Windows packaged-app activation.

It must **not** synthesize or route through an Xbox Guide button.

---

## 2. Product-policy lock

This PR does not change the meaning of the existing mapping domains.

Current authority remains:

```text
Xbox360 / non-Steam presentation
→ FrontButtonDomain.Normal

SteamDeck / Steam Game / Big Picture presentation
→ FrontButtonDomain.Steam
```

Therefore the capability is:

```csharp
(FrontButtonAction.XboxApp, Normal: true, Steam: false)
```

Do not add Xbox to the Steam Game / Big Picture action list.

Do not change the frozen defaults:

```text
Normal Gamebar  = Steam Big Picture
Normal Center M = Quick Settings Overlay

Steam Gamebar   = Steam Button
Steam Center M  = Quick Settings Overlay
```

The user must explicitly select `Xbox`.

The existing same-domain duplicate-action rule also remains:

```text
Normal Gamebar = Xbox
Normal CenterM = Xbox
→ invalid
```

Only one of the two buttons may own the Xbox action in the same domain, exactly like other semantic actions today.

---

## 3. Microsoft platform basis

### 3.1 AUMID is the correct stable application identity

Microsoft documents Application User Model ID (AUMID) as the packaged application's runtime identity.

Important properties for this feature:

- AUMID identifies an application independently of its mutable installation path.
- For packaged apps it is derived from the package family name plus package-relative application ID.
- It is independent of package version and architecture.

Microsoft documentation:

- https://learn.microsoft.com/windows/configuration/find-the-application-user-model-id-of-an-installed-app
- https://learn.microsoft.com/windows/apps/desktop/modernize/package-identity-overview
- https://learn.microsoft.com/uwp/api/windows.applicationmodel.core.applistentry.appusermodelid

Current Xbox PC app evidence identifies:

```text
Package:
Microsoft.GamingApp

Package family:
Microsoft.GamingApp_8wekyb3d8bbwe

Package-relative application ID:
Microsoft.Xbox.App

AUMID:
Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
```

Recent Windows/Xbox field reports also show current `Microsoft.GamingApp` builds using:

```text
Faulting package-relative application ID:
Microsoft.Xbox.App
```

This work order therefore uses exactly one feature-local AUMID constant:

```csharp
private const string XboxAppAumid =
    "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";
```

Do not hard-code:

- `XboxPcApp.exe`;
- `XboxPcAppCE.exe`;
- a versioned `C:\Program Files\WindowsApps\Microsoft.GamingApp_...` path.

### 3.2 Use the documented desktop activation API

Microsoft documents:

```text
IApplicationActivationManager::ActivateApplication
```

as the desktop API that activates a packaged/Store application for the normal `Windows.Launch` contract in the current session.

Reference:

- https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-iapplicationactivationmanager
- https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication

Use:

```text
AUMID = Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
arguments = null / empty
options = AO_NONE
```

The API returns the process ID associated with the activation when successful.

The Addon Runtime is currently elevated by product design. This field test must therefore explicitly verify that the elevated Runtime can activate the Xbox app in the current interactive session.

Do not add a second elevation helper or unelevated broker preemptively.

---

## 4. HHC reference — useful evidence, not the implementation

Current Handheld Companion has a Game Bar command that uses an AUMID:

```csharp
private const string aumid =
    "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe!App";

Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"shell:AppsFolder\\{aumid}",
    UseShellExecute = true
});
```

Reference:

```text
Valkirie/HandheldCompanion
HandheldCompanion/Commands/Functions/Windows/GameBarCommands.cs
```

This confirms that a handheld front-button command can target a packaged Windows app through its AUMID instead of a mutable WindowsApps executable path.

However, **do not copy this command path** for SteamAddonforClaw.

Reasons:

1. HHC's command targets **Xbox Game Bar**:
   ```text
   Microsoft.XboxGamingOverlay_8wekyb3d8bbwe!App
   ```
2. This feature targets the **Xbox PC app**:
   ```text
   Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
   ```
3. SteamAddonforClaw already has an explicit architecture decision that Xbox Game Bar remains suppressed.
4. The documented `IApplicationActivationManager` API avoids adding an `explorer.exe shell:AppsFolder` indirection.

HHC is reference evidence only.

---

## 5. Explicitly forbidden launch mechanisms

The Xbox action must **not** use any of the following.

### 5.1 No Guide-button emulation

Do not implement:

```text
XInput Guide
Xbox360 Guide bit
SteamDeck system-button pulse
virtual-controller Guide press
```

The user explicitly wants the Xbox **application**, not Guide semantics.

This action must not enter:

```text
CanonicalXbox360InputPublisher
SteamDeckSystemButtonOverlay
Steam Button pulse
Quick Access pulse
```

### 5.2 No Game Bar

Do not use:

```text
Win+G
ms-gamebar:
Microsoft.XboxGamingOverlay
Xbox Game Bar widget APIs
Game Bar process activation
```

Existing `WinGSuppressionGuard` remains unchanged.

A WING button mapped to `Xbox` means:

> launch/activate the Xbox PC application while native Win+G remains suppressed.

### 5.3 No mutable executable path

Do not use:

```text
C:\Program Files\WindowsApps\Microsoft.GamingApp_<version>\XboxPcApp.exe
C:\Program Files\WindowsApps\Microsoft.GamingApp_<version>\XboxPcAppCE.exe
```

No WindowsApps ACL changes or ownership changes are allowed.

### 5.4 No shell/protocol workaround

Do not introduce:

- PowerShell;
- `explorer.exe shell:AppsFolder`;
- a generated `.lnk`;
- URI guessing;
- registry launch commands;
- package-database scraping.

Use the direct packaged-app activation API.

---

## 6. Contract change

Update:

```text
src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs
```

Add:

```csharp
public enum FrontButtonAction
{
    QuickSettingsOverlay,
    SteamBigPicture,
    SteamButton,
    SteamQuickAccess,
    KeyboardHotkey,
    LaunchApplication,
    XboxApp
}
```

Keep the exact numeric values of all existing enum members stable.

Therefore **append** `XboxApp`; do not insert it in the middle if that would renumber persisted enum values.

Then update the capability catalog:

```csharp
private static readonly (FrontButtonAction Action, bool Normal, bool Steam)[] Catalog =
[
    (FrontButtonAction.QuickSettingsOverlay, true, true),
    (FrontButtonAction.SteamBigPicture, true, false),
    (FrontButtonAction.XboxApp, true, false),
    (FrontButtonAction.SteamButton, false, true),
    (FrontButtonAction.SteamQuickAccess, false, true),
    (FrontButtonAction.KeyboardHotkey, true, true),
    (FrontButtonAction.LaunchApplication, true, true)
];
```

The declaration order controls UI order.

Recommended Normal UI order:

```text
Quick Settings Overlay
Steam Big Picture
Xbox
Keyboard / Hotkey
Launch Application
```

Steam UI remains:

```text
Quick Settings Overlay
Steam Button
Steam Quick Access
Keyboard / Hotkey
Launch Application
```

No Xbox entry.

---

## 7. Add one feature-local Xbox app launcher

Add a small file, recommended:

```text
src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs
```

The legacy `CenterM` folder currently contains the shared front-button action executors for both physical buttons. Do not refactor folders/names in this PR.

The new class must be stateless and do one thing:

```text
XboxApp AUMID
→ IApplicationActivationManager
→ ActivateApplication
→ return / throw
```

Conceptual implementation:

```csharp
using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.CenterM;

internal static class FrontButtonXboxAppLauncher
{
    internal const string XboxAppAumid =
        "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    private static readonly Guid ActivationManagerClsid =
        new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    internal static void Launch()
    {
        var activationType = Type.GetTypeFromCLSID(
            ActivationManagerClsid,
            throwOnError: true)!;

        var instance = Activator.CreateInstance(activationType)
            ?? throw new InvalidOperationException(
                "Windows application activation manager was unavailable.");

        try
        {
            var manager = (IApplicationActivationManager)instance;
            var hr = manager.ActivateApplication(
                XboxAppAumid,
                null,
                ActivateOptions.None,
                out var processId);

            Marshal.ThrowExceptionForHR(hr);

            AppLog.Info(
                "FrontButtons.XboxApp",
                "Xbox app activation requested.",
                ("Aumid", XboxAppAumid),
                ("ProcessId", processId));
        }
        finally
        {
            if (Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);
    }

    private enum ActivateOptions
    {
        None = 0
    }
}
```

The implementation may use an equivalent correct COM declaration already available in the project, if one exists by implementation time.

Do not create:

- `IXboxAppLauncher`;
- a launcher manager;
- a packaged-app abstraction hierarchy;
- a retry service;
- a package monitor;
- a new process owner.

One static feature-local launcher is enough.

### HRESULT behavior

A failed activation must be a real action execution failure.

Use:

```csharp
Marshal.ThrowExceptionForHR(hr);
```

or equivalent.

Do not silently report success if Windows refused activation.

The existing `FrontButtonActionExecutor` catch/failure policy should remain the single outer action failure boundary.

---

## 8. Wire the shared action executor once

Update:

```text
src/SteamInputAddonforClaw/CenterM/FrontButtonActionExecutor.cs
```

Add one delegate:

```csharp
private readonly Action _launchXboxApp;
```

Constructor:

```csharp
internal FrontButtonActionExecutor(
    Action requestOverlayToggle,
    Action launchBigPicture,
    Func<bool> tryRequestSteamPulse,
    Func<bool> tryRequestQuickAccessPulse,
    Action? launchXboxApp = null,
    Action<FrontButtonHotkeyBinding>? sendHotkey = null,
    Action<FrontButtonLaunchApplicationBinding>? launchApplication = null)
{
    ...
    _launchXboxApp = launchXboxApp ?? FrontButtonXboxAppLauncher.Launch;
}
```

Switch:

```csharp
case FrontButtonAction.XboxApp:
    _launchXboxApp();
    return true;
```

That is the **only** execution switch required.

Do not add Xbox logic independently to:

- `WingActionDispatcher`;
- `Oem1ActionDispatcher`.

Both physical buttons already share `FrontButtonActionExecutor`.

---

## 9. Runtime composition

Update:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
```

For tests, add one optional seam alongside the existing Big Picture / hotkey / application overrides:

```csharp
Action? launchXboxAppOverride = null
```

and compose:

```csharp
var actionExecutor = new FrontButtonActionExecutor(
    requestOverlayToggle: requestOverlayToggle,
    launchBigPicture: launchBigPictureOverride ?? Oem1BigPictureLauncher.Launch,
    tryRequestSteamPulse: tryRequestSteamPulse,
    tryRequestQuickAccessPulse: tryRequestQuickAccessPulse,
    launchXboxApp: launchXboxAppOverride ?? FrontButtonXboxAppLauncher.Launch,
    sendHotkey: sendHotkeyOverride,
    launchApplication: launchApplicationOverride);
```

Do not give the launcher ownership of:

- Xbox processes;
- Xbox package lifecycle;
- XBOX active-game detection;
- XBOX profile state.

It is fire-and-forget activation only.

---

## 10. Main App mapping UI

Update:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
```

Add the user-facing label:

```csharp
FrontButtonAction.XboxApp => "Xbox",
```

No new XAML card or editor is required.

The existing `BindingEditor.ShowConfigurationFor` already only displays configuration UI for:

```text
KeyboardHotkey
LaunchApplication
```

Therefore `XboxApp` should naturally show no sub-configuration.

Because every ComboBox is populated from:

```csharp
FrontButtonActionCapabilities.ActionsFor(domain)
```

the Normal-only capability table automatically guarantees:

- WING Normal shows Xbox;
- Center M Normal shows Xbox;
- WING Steam does not;
- Center M Steam does not.

Do not hand-filter Xbox separately in UI code.

---

## 11. Persistence / frontend compatibility

No new settings object is needed.

Existing:

```text
FrontButtonBinding.Action
→ FrontButtonAction
```

already persists and transports the semantic action.

Therefore this PR must not add:

- an Xbox AUMID setting;
- an Xbox executable-path field;
- another frontend DTO;
- another RPC;
- another settings store.

The Xbox AUMID is product behavior, not user configuration.

Keep it in the one feature-local launcher.

No profile schema change is required.

Existing saved mappings remain valid.

---

## 12. Do not alter Full1902 / Steam / XBOX detection

This PR must not change:

```text
PID1901 / PID1902 authority
HidHide
VIIPER ownership
Xbox360 / SteamDeck presentation selection
SteamSessionRuntime
XBOX active-game WinEvent detector
XboxGameSessionDiagnostic
XBOX installed catalog
profile persistence
M1/M2 mapping
Overlay transport
WinGSuppressionGuard
WING Event88 observation
Center M Event41 observation
gesture policy
```

The new action is just another leaf under the already-owned front-button executor.

---

## 13. Tests

### 13.1 Capability contract

Update `FrontButtonMappingContractTests`.

Required:

```csharp
Assert.Contains(
    FrontButtonAction.XboxApp,
    FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Normal));

Assert.DoesNotContain(
    FrontButtonAction.XboxApp,
    FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Steam));

Assert.True(
    FrontButtonActionCapabilities.Supports(
        FrontButtonAction.XboxApp,
        FrontButtonDomain.Normal));

Assert.False(
    FrontButtonActionCapabilities.Supports(
        FrontButtonAction.XboxApp,
        FrontButtonDomain.Steam));
```

Update the exact expected action lists.

Keep default-mapping tests unchanged.

### 13.2 Both physical buttons dispatch through the shared executor

Update `FrontButtonDispatchTests`.

Extend the fake seams:

```csharp
public int Xbox;
```

and executor:

```csharp
launchXboxApp: () => Xbox++
```

Required tests:

1. WING / Gamebar Normal + Xbox binding → Xbox delegate invoked exactly once.
2. Center M Normal + Xbox binding → Xbox delegate invoked exactly once.
3. Steam-domain Xbox binding bypassing persistence validation → refused and Xbox delegate not called.
4. Xbox action does not pulse Steam Button.
5. Xbox action does not pulse Steam Quick Access.
6. Xbox action does not launch Big Picture.
7. Xbox action does not toggle Overlay.

### 13.3 Same-domain uniqueness remains

Add/retain coverage proving:

```text
Normal.Gamebar = Xbox
Normal.CenterM = Xbox
→ validation failure
```

Do not special-case Xbox around the existing uniqueness rule.

### 13.4 UI contract

Verify the generated action sets guarantee:

```text
Normal editors
→ Xbox visible

Steam editors
→ Xbox absent
```

No separate UI list should exist.

### 13.5 No real Xbox activation in unit tests

Do not launch the real Xbox app from CI/unit tests.

The real COM activation path is covered by:

- compilation;
- shared executor seam tests;
- required target-device field test below.

Do not build a large COM-mocking abstraction just to unit-test one Windows API call.

---

## 14. Required field validation — closes PoC C

Test on the supported MSI Claw with the current elevated Runtime.

Xbox Game Bar should remain disabled/suppressed.

### Test A — WING Normal

Configure:

```text
Gamebar Button
Normal
→ Xbox
```

Keep Center M Normal on a different action.

With Steam/BPM inactive:

```text
press WING
→ Xbox PC app opens/activates
```

Required evidence:

```text
FrontButton action = XboxApp
AUMID = Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
ActivateApplication succeeds
returned ProcessId != 0 when Windows returns one
Xbox PC app is visibly activated
Xbox Game Bar does not appear
```

### Test B — Center M Normal

Configure:

```text
Center M Button
Normal
→ Xbox
```

Keep WING Normal on a different action.

Press Center M:

```text
→ same Xbox PC app activation path
→ no Game Bar
→ no Guide-button injection
```

### Test C — already-running Xbox app

Leave Xbox already running and press the mapped button again.

Expected:

```text
ActivateApplication
→ existing Xbox app activation semantics
→ no duplicate process-management code in Addon
```

The Addon must not enumerate/kill/toggle Xbox processes.

### Test D — Steam domain isolation

Enter Steam Game / Big Picture / SteamDeck presentation.

Verify both Steam-domain ComboBoxes do not offer `Xbox`.

Existing Steam mappings must continue unchanged.

### Test E — Game Bar suppression

With the Xbox action working:

```text
Win+G suppression remains armed
press mapped WING/Center M Xbox action
→ Xbox PC app opens
→ Xbox Game Bar does not open
```

This is the core distinction from Guide/Game Bar behavior.

---

## 15. Failure policy

If `ActivateApplication` fails:

```text
throw / surface HRESULT
→ existing FrontButtonActionExecutor catch
→ existing button-specific failure policy
→ log
```

Do not add retries.

Do not fall back to:

- Guide;
- Win+G;
- `ms-gamebar:`;
- explorer shell activation;
- Xbox executable path;
- PowerShell.

If field validation shows the AUMID no longer matches the supported Windows image, first inspect the actual installed `Microsoft.GamingApp` AUMID and update the single feature-local constant.

Do not create a dynamic package-discovery subsystem unless actual product evidence shows the stable AUMID is insufficient.

---

## 16. Acceptance criteria

The PR is code-complete when all are true:

1. `FrontButtonAction.XboxApp` exists without renumbering existing enum values.
2. Xbox is supported in Normal domain only.
3. Xbox is not supported/offered in Steam Game / Big Picture domain.
4. Both WING and Center M can resolve the action through the existing shared executor.
5. There is one feature-local Xbox app launcher.
6. The launcher targets `Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App`.
7. The launcher uses documented packaged-app activation.
8. No Guide/XInput/system-button injection is used.
9. No Xbox Game Bar / Win+G / `ms-gamebar` path is used.
10. No versioned WindowsApps executable path is used.
11. No WindowsApps ACL/ownership change is used.
12. Existing default mappings are unchanged.
13. Existing same-domain uniqueness remains unchanged.
14. Existing WING suppression remains unchanged.
15. Steam/BPM presentation policy is unchanged.
16. Full1902 controller ownership is unchanged.
17. XBOX detection/catalog/profile code is unchanged.
18. Focused tests pass.
19. Full test suite passes.
20. MSI Claw field validation proves both WING Normal and Center M Normal can activate the Xbox PC app while Game Bar remains suppressed.

---

## 17. PoC C closure rule

This PR intentionally implements one isolated part of the later X6 front-button feature early because it is self-contained and directly answers the remaining X0 platform question.

After the target-device field test passes, update the canonical XBOX architecture field-validation status to:

```text
PoC A — installed XBOX catalog
PASS

PoC B — event-driven active XBOX identity/lifecycle
PASS

PoC C — Xbox app packaged activation
PASS
```

At that point X0 is closed and X1 product implementation may proceed.

Do not expand this PR into the rest of X6.

---

## 18. Final implementation principle

The intended path is:

```text
WING or Center M physical press
        ↓
existing domain selection
        ↓
Normal domain
        ↓
FrontButtonAction.XboxApp
        ↓
shared FrontButtonActionExecutor
        ↓
FrontButtonXboxAppLauncher
        ↓
IApplicationActivationManager::ActivateApplication
        ↓
Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
        ↓
Xbox PC app
```

Not:

```text
Guide button
→ Game Bar
→ Xbox
```

and not:

```text
Win+G
→ Game Bar
```

The Xbox action is a direct packaged-application launch action and remains independent of controller presentation and XBOX game detection.
