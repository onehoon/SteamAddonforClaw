# Work Order — Steam Big Picture Windows Full Screen Experience Toggle

## Status

Implementation work order.

Target repository: `onehoon/SteamAddonforClaw`

Target branch: `main`

Feature surface: **Settings → Steam Big Picture Full Screen Experience**

This work order is based on:

- the current SteamAddonforClaw `main` source;
- the current Full PID1902 architecture documents under `docs/Full 1902 Implementation`;
- the current Settings/frontend transport structure;
- the proven AnyFSE implementation, especially:
  - `AppxManifest.xml`
  - `CustomCapability.SCCD`
  - `src/AppSettings/SettingsPages/LauncherPage.cpp::SaveControls()`
  - `src/App/GamingExperience.cpp`
  - `src/Tools/Packages.cpp`
  - `src/AppInstaller/AppInstaller_Install.cpp`
  - `src/AppInstaller/AppInstaller_Tools.cpp`
- the recent ClawTweaks FSE implementation only as a secondary packaging/lifecycle reference.

The product is standalone. Do **not** add CTW integration.

---

# 1. Goal

Add a single Settings toggle that configures Windows Gaming Full Screen Experience so Windows starts directly into **Steam Big Picture Mode**.

User-facing behavior:

```text
Steam Big Picture Full Screen Experience        [ ON ]

Start Windows directly in Steam Big Picture.
```

The feature must be deliberately simple.

## ON

When the toggle is enabled:

```text
GamingHomeApp = <our exact FSE Home AUMID>
StartupToGamingHome = true
```

The registered Gaming Home application launches:

```text
steam://open/bigpicture
```

and then exits.

## OFF

When the toggle is disabled:

```text
delete GamingHomeApp
StartupToGamingHome = false
```

The resulting state is ordinary Windows/Desktop startup with **no Gaming Home application configured by this feature**.

This is intentional. Do not preserve or restore another launcher when the user explicitly turns this toggle OFF.

## UX contract

The toggle itself must:

- apply immediately;
- not open Windows Settings;
- not show a confirmation dialog;
- not show a success dialog;
- not reboot Windows;
- not offer a “Restart now” action;
- not call `SetGamingFullScreenExperience(TRUE/FALSE)`;
- not attempt to switch the current desktop session into or out of FSE.

This setting controls the **next Windows startup/login experience**, not current-session posture.

---

# 2. Proven reference behavior: AnyFSE

Do not redesign the Windows Gaming Home selection mechanism.

AnyFSE already implements the required state mutation in:

`src/AppSettings/SettingsPages/LauncherPage.cpp::SaveControls()`

Its effective behavior is:

```cpp
if (launcher == None)
{
    DeleteValue(
        HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration,
        GamingHomeApp);

    WriteBool(
        HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration,
        StartupToGamingHome,
        false);
}
else
{
    WriteBool(..., StartupToGamingHome, fseOnStartup);
    WriteString(..., GamingHomeApp, selectedAumid);
}
```

Use the same Windows state contract.

Registry location:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration
```

Values:

```text
GamingHomeApp       REG_SZ
StartupToGamingHome boolean/DWORD semantics used by Windows/AnyFSE
```

Our feature has only two product states:

```text
ON
GamingHomeApp == our exact AUMID
StartupToGamingHome == true

OFF
everything else
```

For an explicit OFF mutation, converge to:

```text
GamingHomeApp absent
StartupToGamingHome == false
```

Do not add a duplicate persisted setting such as:

```text
SteamFseEnabled=true
```

Windows GamingConfiguration is the source of truth.

---

# 3. Gaming Home package contract

The existing WinUI frontend is unpackaged:

`src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj`

currently contains:

```xml
<WindowsPackageType>None</WindowsPackageType>
```

Do **not** convert the main UI or Runtime into the Gaming Home package.

Create one dedicated, tiny FSE Home executable/package.

Suggested project name:

```text
SteamInputAddonforClaw.FseHome
```

Its only runtime responsibility is:

```text
Windows Gaming FSE activates FseHome
    ↓
FseHome launches steam://open/bigpicture
    ↓
FseHome exits
```

The FSE Home must not:

- own controller state;
- wait for PID1902;
- wait for VIIPER;
- wait for HidHide;
- detect Steam processes;
- detect BPM windows;
- poll;
- retry indefinitely;
- start the Addon Runtime;
- become a watchdog;
- contain generic launcher selection;
- contain Playnite/Xbox/custom-launcher support;
- contain splash/video support;
- contain a second settings system.

The existing Runtime startup path remains independently responsible for Full1902 controller ownership.

The existing Steam/BPM watcher and presentation reconciliation remain responsible for:

```text
Steam/BPM inactive → Xbox360 presentation
Steam/BPM active   → SteamDeck presentation
```

FSE is only the Windows home/launch experience.

---

# 4. FSE Home launch implementation

Reuse the same launch semantic already used by:

`src/SteamInputAddonforClaw/CenterM/Oem1BigPictureLauncher.cs`

which launches:

```text
steam://open/bigpicture
```

with shell execution.

The FSE Home executable should be approximately equivalent to:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "steam://open/bigpicture",
    UseShellExecute = true
});
```

Then exit.

A launch failure may be logged if a usable logging location is available, but do not keep the Gaming Home process alive to implement retry machinery.

Do not duplicate Steam installation-path discovery or BPM polling in this project.

---

# 5. AppX/MSIX Gaming Home identity

Use the AnyFSE manifest structure as the reference.

The package must expose:

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension
      Name="windows.gamingApp"
      Id="App"
      DisplayName="Steam Big Picture"
      Description="Steam Big Picture for Steam Addon for Claw"
      PublicFolder="Public" />
</uap3:Extension>
```

and:

```xml
<uap4:CustomCapability
    Name="Microsoft.appCategory.gamingHome_8wekyb3d8bbwe" />
```

The package root must include the matching `CustomCapability.SCCD`.

Use AnyFSE's current SCCD structure as the reference:

```xml
<CustomCapabilityDescriptor ...>
  <CustomCapabilities>
    <CustomCapability Name="Microsoft.appCategory.gamingHome_8wekyb3d8bbwe" />
  </CustomCapabilities>
  <AuthorizedEntities AllowAny="true"/>
  <Catalog>FFFF</Catalog>
</CustomCapabilityDescriptor>
```

Also include the required full-trust capability and Win32 runtime behavior for the FSE Home executable.

The package's minimum supported Windows version must not be broader than the actual Gaming FSE support floor.

AnyFSE currently targets:

```text
10.0.26100.8039
```

The Addon already targets Windows SDK/build 26100. Preserve the AnyFSE support floor for this feature unless implementation-time evidence from the current Windows SDK proves the requirement has changed.

Do not make the entire Addon fail to run merely because FSE is unavailable on the current Windows build. The Settings card should instead be unavailable/disabled with a concise reason.

---

# 6. Package identity and AUMID

Define one fixed Addon-owned FSE package identity.

Example naming direction:

```text
Package identity: SteamAddonforClaw.FseHome
Application Id:   App
AUMID:            <actual PackageFamilyName>!App
```

Do **not** guess the Package Family Name.

The implementation/build pipeline must derive or verify the actual PFN/AUMID from the manifest publisher/identity and package registration result.

Keep the exact AUMID in one feature-local constant once the package identity is finalized.

The toggle's ON readback must compare against that exact AUMID.

Do not wildcard-match Gaming Home applications.

---

# 7. Package registration/provisioning

Registration is infrastructure. The ON/OFF toggle is user preference.

Keep these concepts separate:

```text
FSE Home package registered
    ≠
FSE startup enabled
```

The package may remain registered while the toggle is OFF.

Do not uninstall/reinstall the package on every toggle operation.

## Reference behavior

AnyFSE registers its Gaming Home package once and then changes `GamingHomeApp` / `StartupToGamingHome` independently.

AnyFSE's installer also temporarily enables Developer Mode when required for its custom-capability package, performs package registration, and restores the previous Developer Mode state afterward.

If the Addon uses the same development/custom-capability registration path, preserve the pre-existing machine state:

```text
Developer Mode already enabled
→ leave it enabled

Developer Mode disabled
→ temporarily enable only for registration
→ restore disabled afterward
```

Never permanently enable Developer Mode just for this feature.

Do not add a permanent service or elevated broker.

## Important distribution constraint

The current SteamAddonforClaw Velopack release pipeline does not contain a general Authenticode/MSIX signing stage.

Do not silently invent a production signing secret or commit a private signing key.

Implement the smallest reproducible package-generation/registration path compatible with the repository's current unsigned distribution model and the AnyFSE custom-capability requirements.

If a development/test certificate is required by the chosen identity-package build:

- only the public certificate may be distributable;
- never commit a private PFX/password;
- keep signing inputs explicit in the build/release path;
- fail the packaging step clearly when the required signing input is absent.

Do not weaken Windows global security settings permanently.

## Toggle-time UX

The requested normal toggle path is silent:

- no ContentDialog;
- no Windows Settings launch;
- no reboot prompt.

Package registration should normally already be satisfied before the preference mutation.

If first-time package provisioning genuinely requires Windows elevation/UAC because of the chosen package/signing path, do not hide or bypass Windows UAC. Keep that provisioning operation bounded and one-time. Do not add an extra Addon confirmation dialog in front of it.

After package registration succeeds, future ON/OFF toggles must not elevate.

---

# 8. Windows state model

Create a small feature-local component, not a generalized Windows Gaming manager.

Suggested responsibility/name:

```text
WindowsGamingHomeConfiguration
```

or similarly narrow naming.

It owns only:

- checking FSE OS support;
- checking our FSE package registration;
- reading GamingConfiguration;
- enabling our Steam FSE startup;
- disabling Gaming Home startup;
- readback verification.

Suggested snapshot:

```csharp
internal sealed record SteamFseSnapshot(
    bool Available,
    bool Enabled,
    string? UnavailableReason);
```

Do not persist `Enabled`.

Capture rules:

```text
unsupported OS
→ Available=false
→ Enabled=false

our FSE package unavailable/unregistered
→ Available=false
→ Enabled=false

GamingHomeApp == our exact AUMID
AND StartupToGamingHome == true
→ Available=true
→ Enabled=true

otherwise
→ Available=true
→ Enabled=false
```

This intentionally means:

```text
another Gaming Home app selected
→ our Settings toggle displays OFF
```

No multi-owner arbitration is needed.

---

# 9. ON mutation

The ON operation must be bounded and transactional enough for the two registry values without inventing a state machine.

Required sequence:

```text
1. Verify supported Windows FSE build/API prerequisites.
2. Verify our FSE Home package is registered and resolvable.
3. Write StartupToGamingHome = true.
4. Write GamingHomeApp = our exact AUMID.
5. Read both values back.
6. Return the authoritative snapshot.
```

If either write/readback fails:

- return failure;
- re-read actual Windows state;
- render the actual state in UI;
- log the failure;
- do not claim ON.

A small best-effort rollback to the previous two values is acceptable if it can be implemented locally and clearly.

Do not create transaction journals, epochs, background reconciliation, or retry loops for these two HKCU values.

---

# 10. OFF mutation

OFF is intentionally simple.

Required sequence:

```text
1. Delete GamingHomeApp.
2. Write StartupToGamingHome = false.
3. Read back.
4. Return the authoritative snapshot.
```

Success means:

```text
GamingHomeApp absent
AND
StartupToGamingHome == false
```

Do not restore:

- Xbox app;
- AnyFSE;
- Playnite;
- ClawTweaks;
- a previously selected launcher.

The user's explicit OFF action means:

> Return Windows startup to the ordinary desktop with no Gaming Home startup configured by this feature.

Do not maintain a backup of the previous GamingHomeApp.

---

# 11. UI/frontend ownership

Follow the existing Settings architecture.

Current Settings page:

`src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml`

Current Settings mutations already go through:

`IAddonFrontendControl`

rather than directly mutating Runtime-owned/product state from XAML code-behind.

Add a narrow frontend contract for this feature.

Suggested contract shape:

```csharp
public sealed record FrontendSteamFseSnapshot(
    bool Available,
    bool Enabled,
    string? UnavailableReason);

public sealed record FrontendSteamFseMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendSteamFseSnapshot Snapshot);
```

Suggested frontend methods:

```csharp
Task<FrontendSteamFseSnapshot> CaptureSteamFseAsync(...);

Task<FrontendSteamFseMutationResult> SetSteamFseEnabledAsync(
    bool enabled,
    ...);
```

Wire them through the existing frontend transport only as required by the current `IAddonFrontendControl` contract.

Do not create a separate FSE IPC channel.

Do not add FSE to QAM or Overlay in this PR.

---

# 12. Settings card

Add the card to:

`src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml`

Use the existing `CommunityToolkit.WinUI.Controls.SettingsCard` style.

Recommended copy:

```text
Header:
Steam Big Picture Full Screen Experience

Description:
Start Windows directly in Steam Big Picture.
```

Content:

```text
ToggleSwitch
```

Do not add:

- an Apply button;
- a Restart button;
- a Windows Settings button;
- a launcher dropdown;
- an advanced expander;
- a confirmation dialog.

When unavailable, disable the toggle and place a concise reason in the card description.

Do not expose package identity, registry values, Developer Mode, SCCD, or AUMID to normal users.

---

# 13. Toggle interaction

Follow the existing SettingsPage pattern used by `QuickSettingsPowerSourceToggleSwitch`:

- guard programmatic toggle updates;
- keep last authoritative snapshot;
- issue one mutation for a real user toggle;
- apply the returned authoritative snapshot;
- on exception/failure, refresh and restore the real state.

Suggested fields:

```csharp
private bool _applyingSteamFseState;
private FrontendSteamFseSnapshot _steamFseSnapshot = ...;
private int _steamFseMutationInProgress;
```

Do not add debounce timers.

Disable the control while the mutation is in flight so repeated clicks do not create overlapping writes.

On success:

- update silently;
- no popup.

On failure:

- restore the actual readback state;
- log;
- use the card description for a concise failure/unavailable message.

Do not show a ContentDialog merely because the registry mutation failed.

---

# 14. Startup/controller lifecycle boundary

This feature must not modify the Full1902 controller authority architecture.

The current Full1902 contract remains:

```text
Center M Disabled
→ Addon Runtime owns PID1902
→ HidHide remains the persistent physical isolation baseline
→ VIIPER owns one virtual presentation
```

and:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

When Windows starts in FSE:

```text
Windows starts
├─ existing Addon Runtime startup task starts Runtime
│    └─ Full1902 startup/reconcile runs normally
│
└─ Windows Gaming Home starts FseHome
     └─ steam://open/bigpicture
          └─ existing BPM detection eventually reports active
               └─ existing presentation owner converges to SteamDeck
```

Do not add ordering synchronization between these branches unless real hardware testing demonstrates a user-visible failure.

Specifically, do not add:

- a “wait until controller ready” protocol;
- Runtime/FseHome handshake;
- named-event barriers;
- startup epochs;
- controller-ready files;
- FSE-specific controller state.

The existing owner/reconcile structure should converge safely.

This follows the project's anti-overengineering policy.

---

# 15. Steam not installed / launch failure

Do not turn this PR into a Steam installation manager.

The FSE Home should attempt the existing URI:

```text
steam://open/bigpicture
```

If the URI cannot be resolved or launch fails:

- exit;
- do not loop forever;
- do not launch Windows Settings;
- do not alter Full1902 controller ownership.

The main Settings card may report FSE availability based on Windows/package state. Steam installation detection is optional only if an existing cheap Steam probe can be reused without adding new discovery machinery.

Do not block implementation on a new Steam install-path resolver.

---

# 16. Uninstall cleanup

Uninstall must not leave Windows configured to boot into an Addon Gaming Home package that is being removed.

Integrate feature-local cleanup into the existing uninstall preparation/cleanup path.

Before unregistering/removing the Addon FSE package:

```text
delete GamingHomeApp
StartupToGamingHome = false
```

This cleanup is safe and intentionally restores ordinary Windows startup.

Then unregister/remove only the exact Addon-owned FSE package identity.

Do not wildcard-remove other Gaming Home packages.

Do not touch other applications' packages.

Do not make FSE cleanup a prerequisite for controller stock restoration. Controller authority cleanup remains owned by the existing Full1902 uninstall path.

If the uninstall callback cannot safely perform package removal because of Velopack fast-callback constraints, perform the bounded GamingConfiguration cleanup in the earliest safe existing uninstall preparation path and leave package removal to the package-provisioning cleanup path. Do not destabilize PR12 stock-safe uninstall semantics.

---

# 17. Update behavior

Velopack updates must not reset the user's Windows Gaming Home preference.

If FSE is ON before an app update:

```text
GamingHomeApp remains our exact AUMID
StartupToGamingHome remains true
```

If FSE is OFF:

```text
do not turn it on during update
```

Keep the package identity stable across releases.

Do not version the AUMID by application version.

Package payload updates may update the package version, but identity/PFN/Application Id must remain stable.

---

# 18. OS support check

Do not rely only on `Environment.OSVersion.Version` if the current repository already has a more reliable Windows version helper.

Required feature floor from the current AnyFSE manifest:

```text
Build 26100
UBR >= 8039
```

Equivalent check:

```text
build > 26100
OR
(build == 26100 AND UBR >= 8039)
```

Also allow the package/API registration probe to fail closed.

Unsupported result:

```text
Available = false
Enabled = false
```

The rest of SteamAddonforClaw continues normally.

---

# 19. Logging

Add concise feature-local logs.

Examples:

```text
SteamFSE capture
- supported
- package registered
- selected home is ours
- startup enabled

SteamFSE enable requested
SteamFSE enable verified

SteamFSE disable requested
SteamFSE disable verified

SteamFSE package registration failed
SteamFSE readback verification failed
```

Do not log unnecessary full registry dumps.

Do not poll/log repeatedly in the background.

---

# 20. Tests

Add focused tests around the product contract.

Do not attempt to run actual Windows Gaming FSE inside unit tests.

Use small injectable registry/package probes where necessary, but do not create a generalized abstraction framework.

Required tests:

## State capture

1. Our AUMID + StartupToGamingHome=true → Enabled.
2. Our AUMID + StartupToGamingHome=false → Disabled.
3. Different AUMID + startup=true → Disabled.
4. GamingHomeApp absent → Disabled.
5. Unsupported OS → unavailable.
6. Package missing → unavailable.

## Enable

7. ON writes startup=true and exact Addon AUMID.
8. ON verifies readback.
9. Failed readback does not report success.

## Disable

10. OFF deletes GamingHomeApp.
11. OFF writes StartupToGamingHome=false.
12. OFF readback requires GamingHomeApp absent and startup=false.
13. OFF does not preserve/restore another launcher.

## UI

14. Programmatic rendering does not trigger a mutation.
15. User toggle invokes exactly one mutation.
16. Toggle is disabled while mutation is in progress.
17. Failed mutation restores authoritative state.
18. Unavailable state disables the toggle and renders the reason.

## Packaging/build

19. Publish verification requires the FSE Home/package assets.
20. Package manifest contains `windows.gamingApp`.
21. Package manifest contains `Microsoft.appCategory.gamingHome_8wekyb3d8bbwe`.
22. `CustomCapability.SCCD` is present in the package.
23. FSE Home project remains independent from controller/VIIPER/HidHide implementation assemblies where possible.

## Uninstall

24. Cleanup clears GamingHomeApp and StartupToGamingHome.
25. Cleanup targets only the exact Addon FSE package identity.

---

# 21. Manual validation

Perform on a supported Windows 11 Gaming FSE build.

## Registration

Verify:

```powershell
Get-AppxPackage
```

shows the exact Addon FSE package and determine its real PackageFamilyName/AUMID.

Verify the package appears as a `windows.gamingApp` candidate.

## OFF baseline

Before enabling:

```text
GamingHomeApp absent
StartupToGamingHome = false
Settings toggle = OFF
```

Restart Windows.

Expected:

```text
ordinary Windows/Desktop startup
```

## ON

Enable the Settings toggle.

Expected immediately:

```text
no Addon confirmation popup
no Windows Settings page
no reboot
toggle remains ON after readback
GamingHomeApp = exact Addon FSE AUMID
StartupToGamingHome = true
```

Restart Windows manually.

Expected:

```text
Windows Gaming FSE
→ Addon FseHome
→ Steam Big Picture
```

Verify the existing Full1902 Runtime independently converges to the correct SteamDeck presentation when BPM becomes active.

Do not require FseHome to coordinate this.

## OFF

Disable the toggle.

Expected immediately:

```text
no confirmation popup
no reboot
GamingHomeApp absent
StartupToGamingHome = false
toggle = OFF
```

Restart Windows manually.

Expected:

```text
ordinary Windows/Desktop startup
```

## Update

With FSE ON, perform a normal Velopack update.

Expected:

```text
same FSE package identity
GamingHomeApp still resolves
startup remains enabled
```

## Uninstall

With FSE ON, uninstall the Addon through the supported uninstall path.

Expected:

```text
GamingHomeApp removed
StartupToGamingHome = false
Addon FSE package removed/unregistered
ordinary Windows startup remains possible
Full1902 stock-safe uninstall behavior unchanged
```

---

# 22. Explicit non-goals

Do not implement in this work order:

- current-session Enter FSE button;
- `SetGamingFullScreenExperience(TRUE/FALSE)`;
- automatic reboot;
- restart prompt;
- Windows Settings launcher;
- generic FSE launcher selector;
- Playnite;
- Xbox launcher selection;
- custom executable launcher;
- splash/video;
- FSE hotkeys;
- controller-ready handshake;
- controller authority changes;
- new Steam/BPM detector;
- FSE-specific HidHide rules;
- FSE-specific VIIPER owner;
- background FSE watcher;
- polling;
- retry state machine;
- restoration of a previously selected third-party GamingHomeApp.

---

# 23. Implementation shape

Prefer one focused PR if the package/build work remains reasonably reviewable.

Expected areas:

```text
src/SteamInputAddonforClaw.FseHome/
    tiny Steam BPM launcher

src/... feature-local Windows Gaming Home configuration
    registry read/write
    OS/package probe
    package registration/provisioning seam

src/SteamInputAddonforClaw.Contracts/
    frontend snapshot/mutation contracts

src/SteamInputAddonforClaw/Frontend/
    IAddonFrontendControl implementation

src/SteamInputAddonforClaw.FrontendTransport/
    existing transport additions if required

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
    one SettingsCard + ToggleSwitch

FSE package assets/
    AppxManifest.xml
    CustomCapability.SCCD
    required logos/assets

scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
    publish/package verification

existing uninstall path
    safe OFF + exact package unregister

tests/
    state/mutation/UI/package/uninstall tests
```

Do not split this into multiple architectural layers merely to reduce file size.

If package signing/provisioning requires a release-pipeline prerequisite that cannot be implemented safely without a private signing input, keep that requirement explicit and isolated. Do not fake a production certificate and do not weaken Windows security to make CI green.

---

# 24. Acceptance criteria

The PR is complete when all of the following are true:

1. Settings contains exactly one Steam Big Picture FSE toggle.
2. ON directly selects the Addon FSE AUMID and enables startup-to-gaming-home.
3. OFF deletes GamingHomeApp and sets StartupToGamingHome=false.
4. Toggle state is derived from Windows state, not a duplicate Addon preference.
5. No Windows Settings page is opened.
6. No Addon confirmation/success dialog is shown for normal ON/OFF.
7. No reboot is initiated or requested by this feature.
8. No current-session FSE API transition is performed.
9. Windows FSE activates a tiny Addon Gaming Home executable which launches `steam://open/bigpicture` and exits.
10. Main WinUI UI remains unpackaged.
11. Full1902 controller authority remains unchanged.
12. Existing BPM detection remains the sole input deciding Xbox360 ↔ SteamDeck presentation.
13. Package registration is not repeated on every toggle.
14. Update preserves the stable FSE identity.
15. Uninstall clears FSE startup and unregisters only the Addon package.
16. Unsupported Windows/package-registration state fails closed without breaking the rest of the Addon.
17. No new manager/state machine/background watcher is introduced without a demonstrated product need.
18. Build/publish verification catches missing FSE package assets.
19. Focused tests pass.
20. Existing full test suite passes.

---

# 25. Design principle

Keep the implementation equivalent to the proven AnyFSE model:

```text
registered windows.gamingApp identity
        +
GamingHomeApp
        +
StartupToGamingHome
        +
tiny launcher
```

SteamAddonforClaw already has the controller lifecycle, Steam/BPM detection, and SteamDeck presentation machinery.

Do not duplicate any of those responsibilities in FSE.

The desired product architecture is:

```text
Windows owns FSE startup policy
FseHome owns one Steam BPM launch
Runtime owns controller lifecycle
existing Steam watcher owns BPM fact
existing presentation owner owns X360/SteamDeck selection
```

One owner per responsibility.
