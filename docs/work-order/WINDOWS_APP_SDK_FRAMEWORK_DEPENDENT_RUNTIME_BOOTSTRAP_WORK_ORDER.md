# Work Order — PR-B: Windows App SDK Framework-Dependent UI/Overlay Deployment

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@0c683cc1ee1dd8731922246f253c662295f6b79b`  
> **Date:** 2026-09-22  
> **Scope:** externalize the Windows App SDK runtime used by the WinUI desktop UI and Overlay  
> **Architecture baseline:** standalone Full1902 application; CTW integration is out of scope

---

## 1. Goal

Convert both WinUI processes from Windows App SDK self-contained deployment to framework-dependent deployment:

```text
SteamInputAddonforClaw.UI
SteamInputAddonforClaw.Overlay
```

and make the Addon install the required Windows App Runtime on demand when a user explicitly requests a WinUI surface.

Current projects explicitly force:

```xml
<SelfContained>false</SelfContained>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```

This means:

- .NET is already framework-dependent;
- Windows App SDK is still app-local/self-contained;
- UI and Overlay each carry a large duplicated Windows App SDK payload.

PR-B must change only the **Windows App SDK deployment model**.

Target:

```text
.NET 10 Runtime
→ remains framework-dependent

Windows App SDK
→ framework-dependent
→ system/shared Windows App Runtime

Main Runtime
→ still UI-independent
→ remains able to run controller lifecycle even if Windows App Runtime is absent

Main UI / Overlay
→ only launch after Windows App Runtime availability is proven
```

---

## 2. Source authority

Read the current Full1902 documents before implementation:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Windows App Runtime availability is **not** a controller prerequisite.

Do not modify:

- Center M authority;
- PID1901 / PID1902 transition policy;
- HidHide deterministic baseline;
- DirectInput ownership;
- VIIPER ownership or presentation;
- controller prerequisite setup;
- `FirstTimeSetupPolicy`;
- sleep / hibernate / resume behavior;
- PnP recovery;
- routing fail-close behavior.

If Windows App Runtime is unavailable:

```text
Main UI unavailable
Overlay unavailable

BUT

headless Runtime
controller authority
HidHide
DirectInput
VIIPER
Steam observation
tray
controller lifecycle
→ remain alive and authoritative
```

Do not turn a UI framework failure into a Full1902 controller failure.

---

## 3. Microsoft deployment model

The current UI/Overlay projects are unpackaged desktop WinUI applications:

```xml
<WindowsPackageType>None</WindowsPackageType>
<UseWinUI>true</UseWinUI>
```

Microsoft's framework-dependent deployment model for unpackaged apps requires:

1. Windows App Runtime packages installed on the machine;
2. the app bootstrapper to add the matching framework package to the process package graph.

For an unpackaged project with:

```xml
<WindowsPackageType>None</WindowsPackageType>
```

the Windows App SDK NuGet build targets provide bootstrapper auto-initialization.

Do not manually add a second bootstrapper initialization path inside UI or Overlay.

Official Microsoft references:

```text
https://learn.microsoft.com/windows/apps/package-and-deploy/deploy-overview
https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps
https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time
https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
```

---

## 4. Keep Windows App SDK 2.3.1

Current projects use:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
```

Keep that version unchanged in PR-B.

As of this work order Microsoft has newer Windows App SDK 2.x releases, but upgrading the SDK is a separate compatibility change.

Do not combine:

```text
deployment-model change
+
Windows App SDK version upgrade
```

in one PR.

The build-time SDK remains:

```text
Microsoft.WindowsAppSDK 2.3.1
```

---

## 5. Windows App Runtime 2.3.1 pinned installer metadata

For a machine that does not have a compatible Windows App Runtime 2.x package, use the official Microsoft x64 runtime installer corresponding to the project's current SDK baseline.

Pin:

```text
Product:
Windows App Runtime 2

Minimum supported framework version:
2.3.1.0

Framework package family:
Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe

Official Microsoft x64 installer:
https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe

Installer filename:
WindowsAppRuntimeInstall-x64.exe

SHA-256:
4011748DDF472B7E856D909FDFB4E9B19C3D23FCD8121039AC91F99D5FFA65DB

Silent installer arguments:
--quiet
```

The SHA-256 and installer command above are also published in Microsoft's Winget package manifest:

```text
microsoft/winget-pkgs
manifests/m/Microsoft/WindowsAppRuntime/2/2.3.1/Microsoft.WindowsAppRuntime.2.installer.yaml
```

That manifest identifies:

```text
PackageIdentifier: Microsoft.WindowsAppRuntime.2
PackageVersion: 2.3.1
PackageFamilyName: Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe
ElevationRequirement: elevationRequired
```

Do not use:

- a `latest` URL;
- Winget as a runtime dependency;
- Store UI;
- an unversioned arbitrary download;
- x86/ARM64 installers;
- a mirror.

The supported Addon architecture remains x64.

---

## 6. Compatible newer Windows App Runtime must be accepted

Windows App SDK 2.x changed the framework package identity to the major-version family:

```text
Microsoft.WindowsAppRuntime.2
```

and uses semantic versioning inside that family.

Microsoft's Windows App SDK 2.x deployment/versioning contract explicitly makes newer releases within the same major backwards-compatible.

Therefore the probe must use:

```text
stable Windows App Runtime 2 framework package
version >= 2.3.1.0
x64-compatible
→ Ready
```

Examples:

```text
2.3.0.0
→ UpdateRequired

2.3.1.0
→ Ready

2.4.0.0
→ Ready

2.5.1.0
→ Ready
```

Do not downgrade a machine that already has a newer compatible Windows App Runtime 2 package.

Do not require exact equality to 2.3.1.0.

Preview/experimental package families do not replace the stable runtime requirement.

---

## 7. Runtime probe

Add one narrow Windows App Runtime availability probe in the **main Runtime assembly**.

The main Runtime already targets Windows and already uses `Windows.Management.Deployment.PackageManager`; do not add `Microsoft.WindowsAppSDK` to the Runtime project just to perform this probe.

Preferred authority:

```text
Windows.Management.Deployment.PackageManager
→ current-user registered packages
→ exact stable framework package family
→ compatible x64 package
→ Version >= 2.3.1.0
```

Suggested status model:

```text
Ready
Missing
UpdateRequired
Indeterminate
```

Keep it small.

Conceptually:

```csharp
internal static class WindowsAppRuntimeMetadata
{
    internal static readonly Version MinimumFrameworkVersion = new(2, 3, 1, 0);

    internal const string FrameworkPackageFamilyName =
        "Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe";

    internal const string InstallerFileName =
        "WindowsAppRuntimeInstall-x64.exe";

    internal static readonly Uri InstallerDownloadUri =
        new("https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe");

    internal const string InstallerSha256 =
        "4011748DDF472B7E856D909FDFB4E9B19C3D23FCD8121039AC91F99D5FFA65DB";
}
```

The exact class/record names may be adjusted.

Do not build a generic package framework.

---

## 8. Package probe rules

Required behavior:

### Ready

```text
exact stable framework family exists
+ compatible x64 package exists
+ package version >= 2.3.1.0
→ Ready
```

### Missing

```text
stable framework family absent
→ Missing
```

An x86-only installation must not satisfy the x64 Addon.

### UpdateRequired

```text
stable framework family exists
but highest compatible x64 version < 2.3.1.0
→ UpdateRequired
```

### Indeterminate

```text
PackageManager inspection throws
or package identity/version cannot be safely interpreted
→ Indeterminate
```

`Indeterminate` must not be converted into a guessed success.

Do not enumerate every Windows App Runtime Main/Singleton/DDLM internal package and build a parallel Microsoft deployment validator.

For this supported Windows 11/x64 product, the stable Framework package is the availability authority. The official Microsoft runtime installer remains responsible for deploying the full supported runtime package set.

---

## 9. Do not add Windows App Runtime to controller prerequisite setup

Do **not** put this into:

```text
FirstTimeSetupPolicy
FrontendPrerequisiteSetupExecutor
ElevatedPrerequisiteSetup
RuntimePrerequisiteInspector
Required Components UI
HidHide/usbip provisioning receipts
```

Those are controller/runtime prerequisites.

Windows App Runtime is a **presentation prerequisite**.

The dependency graph is:

```text
Full1902 controller Runtime
    independent

WinUI surface requested
    ↓
Windows App Runtime availability
    ↓
UI.exe or Overlay.exe
```

Keep those authorities separate.

---

## 10. Reuse PR-A installer acquisition mechanics

PR-A introduced:

```text
PrerequisiteInstallerAcquisition
```

It already provides:

- bounded HTTPS streaming;
- trusted staging-file write mechanics;
- SHA-256 validation;
- stale staging replacement;
- temporary-file cleanup;
- no live-network dependency in unit tests.

Reuse this helper.

Do not create a second HTTP downloader for Windows App Runtime.

The Windows App Runtime setup path must still use its own fixed descriptor/metadata and must not become part of HidHide/usbip policy.

---

## 11. Privileged Windows App Runtime setup helper

The Microsoft installer requires elevation.

Add one narrow self-elevated helper mode, for example:

```text
SteamInputAddonforClaw.exe --ensure-windows-app-runtime
```

Dispatch it in `Program.Main` before entering the normal single-instance Runtime lifetime, alongside the existing fixed helper arguments.

Suggested owner:

```text
ElevatedWindowsAppRuntimeSetup
```

This is **not** another controller prerequisite manager.

Its only responsibility is:

```text
probe
→ if already Ready: success

otherwise:
→ ensure trusted ProgramData provisioning storage
→ download exact pinned Microsoft installer
→ SHA-256 verify
→ run --quiet
→ wait for installer exit
→ re-probe framework package
→ success only if Ready
→ delete staged installer best-effort
```

Do not:

- inspect controller hardware;
- inspect Steam activity;
- mutate HidHide;
- mutate Center M;
- create a provisioning receipt;
- write controller recovery state;
- reboot Windows;
- touch VIIPER.

---

## 12. Trusted staging

Reuse:

```text
%ProgramData%\SteamInputAddonforClaw\provisioning
```

and:

```csharp
ProvisioningStorageSecurity.EnsureTrustedStorage(...)
PrerequisiteInstallerAcquisition
```

The downloaded installer must not be staged in:

- user Temp;
- Downloads;
- Desktop;
- Velopack app directory.

The elevated helper should download directly into the existing protected ProgramData location.

The downloaded installer is ephemeral.

Required:

```text
download
→ SHA-256 verify
→ execute
→ best-effort delete in finally
```

Do not add a persistent Windows App Runtime installer cache.

---

## 13. No provisioning receipt is needed

Do not copy HidHide/usbip receipt machinery into this feature.

Windows App Runtime is:

- Microsoft-owned;
- system-shared;
- idempotently installable;
- not controller authority;
- easily re-probed from Windows package state.

Required retry model:

```text
setup interrupted/fails
→ next explicit WinUI request probes current package state

if Ready
→ continue

if Missing/UpdateRequired
→ user may retry setup
```

Do not create:

```text
WindowsAppRuntimeProvisioningReceipt
WindowsAppRuntimeJournal
InstallStarted state
PendingReboot state
reconciliation state machine
```

unless real installer behavior proves it is necessary.

---

## 14. Parent Runtime setup client

The normal Runtime needs one small owner that serializes explicit setup requests.

Conceptual flow:

```text
EnsureAvailableAsync()
    ↓
probe
    ├─ Ready
    │    → true
    │
    ├─ Missing / UpdateRequired
    │    → one bounded UAC helper launch
    │    → wait helper
    │    → parent re-probes
    │    → Ready only if Windows state proves it
    │
    └─ Indeterminate
         → false
         → no WinUI launch
```

Reuse the existing:

```text
IElevatedProcessRunner
ElevatedProcessRunner
```

instead of implementing another `runas` wrapper.

Use one in-process `SemaphoreSlim` or equivalent narrow gate so simultaneous:

```text
tray open
second-instance activation
Overlay button
```

cannot create multiple UAC/install requests.

This is a realistic user path and does not require another persisted state machine.

The product supports one user / one interactive session / one Runtime process; do not add a system-wide multi-session authority layer.

---

## 15. Critical launch-boundary rule

No WinUI process may be launched unless Windows App Runtime availability has been proven.

This applies to:

```text
Main UI
Overlay
```

but not:

```text
Runtime
QAM Host
FSE Home
TDP helper
ClawHUD
```

The setup/probe belongs at the WinUI process launch boundary.

Do not run the Windows App Runtime installer unconditionally at application startup.

Do not run it on every Windows boot.

Do not add a UAC prompt merely because the headless Runtime started.

---

## 16. Main UI launch path

Current ownership:

```text
AddonProcessHost
→ FrontendProcessLauncher
→ ui\SteamInputAddonforClaw.UI.exe
```

The main UI open path must become:

```text
user requests Main UI
→ if Runtime frontend transport not ready:
     preserve current pending-open behavior
→ once Runtime is ready:
     ensure Windows App Runtime
→ if Ready:
     preserve current Overlay-retirement ordering
     launch UI
→ if unavailable/setup fails:
     do not launch UI
     Runtime remains alive
```

### Important pending-open requirement

Current `FrontendProcessLauncher.MarkRuntimeReady()` directly releases a queued launch.

That direct release would bypass the new Windows App Runtime gate.

PR-B must close that bypass.

Use the smallest clean adjustment.

One acceptable shape is:

```text
FrontendProcessLauncher
→ continues to remember one pending open reason
→ MarkRuntimeReady marks readiness and returns/takes the pending reason
→ AddonProcessHost re-enters CoordinateFrontendOpenAsync(reason)
→ Windows App Runtime ensure gate
→ existing surface ordering
→ launcher starts UI
```

Exact method names are flexible.

Do not duplicate pending-open state in multiple classes.

Do not create an event bus or generalized launch pipeline.

---

## 17. Main UI failure UX

If an explicit Main UI request cannot establish Windows App Runtime:

```text
UAC cancelled
network unavailable
hash mismatch
installer failure
post-install probe not Ready
```

then:

- do not start `SteamInputAddonforClaw.UI.exe`;
- keep Runtime/controller lifecycle running;
- log the exact reason;
- show one concise native Win32 warning if appropriate for the explicit UI request.

A native warning is allowed because it does not depend on WinUI.

Do not build another fallback UI framework.

Do not show repeated background warnings on Windows startup.

---

## 18. Overlay warm startup must not trigger setup

Current behavior:

```text
InitializeRuntimeAsync
→ StartOverlayWarmupAsync
→ OverlayProcessController.StartAsync
```

After PR-B:

```text
StartOverlayWarmupAsync
→ read-only Windows App Runtime probe

Ready
→ normal Overlay warm startup

Missing / UpdateRequired / Indeterminate
→ skip warm startup
→ no UAC
→ no download
→ no effect on Full1902 Runtime
```

A background Windows startup must remain headless and must never raise a Windows App Runtime install UAC prompt solely because Overlay warmup exists.

---

## 19. Explicit Overlay request may provision

An explicit user request to open/toggle Overlay is a valid setup boundary.

Required order:

```text
explicit Overlay toggle
→ ensure Windows App Runtime
→ only if Ready:
     existing Main UI retirement
     existing Full1902 physical/presentation checks
     existing neutral capture transition
     Overlay start/show
```

If runtime setup fails:

```text
→ return before Main UI retirement
→ return before controller presentation neutralization/capture
→ do not start Overlay
→ controller remains unchanged
```

This ordering is important.

Do not enter the OQ4 capture lifecycle before the UI framework required to present the Overlay is available.

---

## 20. Framework-dependent project settings

Change both:

```text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
```

from:

```xml
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```

to explicit:

```xml
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
```

Keep:

```xml
<WindowsPackageType>None</WindowsPackageType>
<UseWinUI>true</UseWinUI>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
<PlatformTarget>x64</PlatformTarget>
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
```

Do not remove `WindowsPackageType=None`.

Do not add manual Bootstrap.Initialize calls to UI/Overlay.

Do not switch to MSIX packaging.

---

## 21. Do not remove required bootstrapper/projection files blindly

Framework-dependent does **not** mean every Microsoft-named file disappears from the UI folders.

For unpackaged framework-dependent apps, the small Windows App SDK bootstrapper component remains app-local.

The following are separate concerns:

```text
Windows App Runtime shared framework payload
vs
bootstrapper / managed projections / C#/WinRT support required by the app
```

Do not manually delete files from publish output merely because their names begin with `Microsoft.`.

In particular, PR-B must **not** attempt the separate `Microsoft.Windows.SDK.NET.dll` reduction work.

That is PR-C / later work.

---

## 22. Existing XAML publish preservation stays

Current UI/Overlay project targets explicitly preserve app-owned:

- XBF files;
- application PRI;
- UI assets.

Keep this behavior unless the real framework-dependent publish proves a specific rule is obsolete.

The application-owned files remain required:

```text
UI:
SteamInputAddonforClaw.UI.pri
App.xbf
MainWindow.xbf
Views\*.xbf
Assets\...

Overlay:
SteamInputAddonforClaw.Overlay.pri
App.xbf
OverlayWindow.xbf
```

Do not accidentally remove the application's own PRI/XBF while removing Windows App SDK runtime payload.

---

## 23. Publish verification must be updated from actual output

Current `scripts/verify-publish-assets.ps1` assumes the self-contained WinAppSDK layout and currently checks for generic WinMD presence in UI/Overlay.

After changing the projects to framework-dependent:

1. perform a clean Release publish;
2. inspect the real `ui\` and `overlay\` contents;
3. update the verifier to represent the actual framework-dependent contract.

Required principles:

### Continue requiring app-owned assets

Require:

- UI/Overlay exe;
- their managed app DLLs;
- app PRI;
- required XBF;
- required Assets.

### Stop requiring runtime payload merely because old self-contained publish had it

Do not force files such as Windows App Runtime PRI/WinMD/native binaries back into publish output just to satisfy the old verifier.

### Add focused negative assertions

After a real framework-dependent publish proves which self-contained runtime files disappear, add a small set of stable negative checks for those known Windows App SDK runtime payloads.

Do not maintain a giant fragile blacklist.

At minimum the test suite must prove:

```text
WindowsAppSDKSelfContained=false for UI
WindowsAppSDKSelfContained=false for Overlay

and

the actual framework-dependent publish no longer carries the prior large self-contained Windows App SDK runtime payload
```

Keep all existing:

- .NET self-contained runtime exclusions;
- VIIPER verification;
- HidHide/usbip installer exclusions;
- QAM checks;
- FSE checks.

---

## 24. Publish-size evidence

PR-A's merged release-layout measurement was:

```text
Total raw publish:
379,799,524 bytes
362.21 MiB
```

This is only a reference baseline.

Before implementation, re-measure current `main` with:

```text
scripts/report-publish-size.ps1
```

After PR-B, report:

- total bytes/MiB;
- UI bytes/MiB;
- Overlay bytes/MiB;
- largest files;
- final Velopack Setup size if the release-pack step is available.

Do not set an arbitrary byte target as a merge blocker.

Acceptance is:

```text
correct framework-dependent output
+
real measured reduction
+
clean-machine runtime provisioning works
```

not a guessed target size.

---

## 25. Expected size direction

Before PR-B, UI and Overlay are the dominant duplicated raw payloads.

Switching Windows App SDK to framework-dependent should remove a substantial amount of duplicated runtime content from both directories.

However:

- app XAML remains;
- CommunityToolkit remains where needed;
- C#/WinRT/Windows SDK managed projections may remain;
- `Microsoft.Windows.SDK.NET.dll` is explicitly not part of PR-B.

Do not claim that every UI/Overlay byte will disappear.

Measure the actual publish.

---

## 26. Windows App Runtime setup failure policy

### Network unavailable

```text
explicit WinUI request
→ Missing/UpdateRequired
→ download fails
→ no WinUI process
→ Runtime/controller continues
→ later explicit request may retry
```

### Hash mismatch

```text
downloaded bytes != pinned SHA-256
→ never execute installer
→ no WinUI process
→ Runtime continues
```

### UAC cancelled

```text
elevated helper not started
→ no WinUI process
→ Runtime continues
```

### Installer non-zero exit

```text
→ parent does not trust exit as success
→ re-probe
→ if not Ready: fail surface request
```

Prefer requiring both:

```text
helper reports success
+
parent readback == Ready
```

Do not rely only on installer exit code.

### Process crash during setup

No journal is required.

Next explicit request:

```text
probe current Windows package state
→ Ready: continue
→ Missing/UpdateRequired: retry allowed
```

---

## 27. Newer runtime behavior

This case must be explicitly tested.

Example:

```text
installed Windows App Runtime framework = 2.5.1.0
minimum required = 2.3.1.0

→ Ready
→ no download
→ no UAC
→ no downgrade
```

Do not pin package **presence** to exact 2.3.1 when Microsoft explicitly services Windows App Runtime 2.x in the shared major-version family.

The installer asset remains pinned to 2.3.1 only for machines requiring provisioning.

---

## 28. Background startup behavior

Required behavior on Windows startup:

### Runtime present

```text
--background
→ normal headless Runtime
→ Overlay warmup allowed
→ no UAC
```

### Runtime missing

```text
--background
→ normal headless Runtime
→ controller lifecycle continues
→ Overlay warmup skipped
→ no network request
→ no UAC
→ no native warning dialog
```

This is a hard product requirement.

A UI dependency must never turn mandatory Full1902 background Runtime startup into an interactive installer flow.

---

## 29. Explicit request after background startup

If Runtime booted headlessly while Windows App Runtime was absent:

```text
later tray click / second-instance activation / explicit Main UI request
→ EnsureAvailableAsync
→ install if needed
→ parent re-probe
→ launch UI when Ready
```

For Overlay:

```text
later explicit Overlay button
→ EnsureAvailableAsync
→ install if needed
→ only then enter existing Overlay capture/show path
```

No Runtime restart should be required solely because Windows App Runtime was installed.

---

## 30. Tests — package probe

Add focused unit tests for:

```text
no stable family
→ Missing

x86-only candidate
→ does not satisfy x64 requirement

2.3.0.0 x64
→ UpdateRequired

2.3.1.0 x64
→ Ready

2.4.0.0 x64
→ Ready

2.5.1.0 x64
→ Ready

malformed/unreadable package evidence
→ Indeterminate

PackageManager failure
→ Indeterminate
```

Use an injected package enumeration seam.

Do not make tests depend on the developer machine's installed Windows App Runtime.

---

## 31. Tests — elevated setup

Use fake HTTP/elevation/process/package probes.

Required cases:

1. already Ready → installer is not downloaded;
2. Missing → pinned descriptor is downloaded;
3. UpdateRequired → pinned descriptor is downloaded;
4. hash mismatch → installer never launches;
5. HTTP failure → installer never launches;
6. installer exit failure + not Ready → failure;
7. installer exit 0 + parent/elevated post-probe Ready → success;
8. installer exit 0 + post-probe not Ready → failure;
9. staging installer is best-effort deleted;
10. trusted ProgramData storage is required before acquisition.

Reuse `PrerequisiteInstallerAcquisitionTests` for generic download behavior instead of duplicating those tests.

---

## 32. Tests — surface launch boundaries

### Main UI

Prove:

```text
Runtime package Ready
→ Main UI launches normally

Missing + setup succeeds
→ setup occurs before UI process start

Missing + setup fails
→ UI process not started
→ Runtime remains available

pending InitialManualLaunch
→ cannot bypass Windows App Runtime gate when Runtime readiness is published

concurrent explicit UI requests
→ one setup attempt
```

Update `FrontendProcessLauncherTests` only as necessary for the small pending-open seam change.

### Overlay

Prove:

```text
background warmup + Ready
→ existing warm startup

background warmup + Missing
→ no setup helper
→ no UAC/elevation runner
→ no Overlay process

explicit toggle + Missing + setup succeeds
→ setup before Overlay process/capture

explicit toggle + setup fails
→ no Main UI retirement
→ no presentation neutralization
→ no Overlay process
```

Do not weaken existing OQ4 capture/order tests.

---

## 33. Tests — project/publish contract

Update/add architecture tests that prove:

```text
UI:
WindowsPackageType=None
UseWinUI=true
SelfContained=false
WindowsAppSDKSelfContained=false
Microsoft.WindowsAppSDK=2.3.1

Overlay:
same
```

Update:

```text
scripts/tests/verify-publish-assets.tests.ps1
```

to represent the new framework-dependent layout.

Do not fake a self-contained Windows App SDK payload in the "valid" publish fixture after PR-B.

The fixture should contain only files actually required by the new publish contract.

---

## 34. Manual validation — installed runtime

On a supported MSI Claw with compatible Windows App Runtime already installed:

1. start Addon manually;
2. verify no UAC;
3. verify no runtime download;
4. verify Main UI opens;
5. verify Overlay warm process becomes Ready;
6. toggle Overlay;
7. verify controller capture/show behavior is unchanged;
8. close/reopen UI/Overlay;
9. restart Addon in background mode;
10. verify no install attempt.

Full1902 routing behavior must remain unchanged.

---

## 35. Manual validation — clean runtime environment

Use a disposable VM/test image or a machine where removing Windows App Runtime will not break unrelated applications.

Do not uninstall a shared Windows App Runtime from a normal development workstation merely to satisfy this test.

Validate:

```text
Windows App Runtime 2 compatible framework absent
→ Addon --background
→ no UAC / no download
→ Runtime remains alive

then explicit Main UI request
→ one UAC
→ official 2.3.1 x64 installer download
→ SHA-256 verified
→ --quiet install
→ parent re-probe Ready
→ Main UI launches
```

Then restart the Addon:

```text
→ no second UAC
→ no second install
→ normal Overlay warmup
```

---

## 36. Manual validation — failure cases

Validate at least:

### UAC cancel

```text
explicit Main UI request
→ cancel UAC
→ UI does not launch
→ Runtime remains healthy
```

### Offline

```text
runtime missing
→ explicit UI request
→ acquisition fails
→ UI does not launch
→ controller Runtime remains healthy
```

### Overlay first

```text
runtime missing
→ explicit Overlay request
→ setup happens before any capture/presentation mutation
```

### Newer runtime

If practical:

```text
Windows App Runtime 2.4+ / 2.5+
→ probe Ready
→ no 2.3.1 install attempt
→ UI/Overlay work normally
```

---

## 37. Files expected to change

Expected production files:

```text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/FrontendProcessLauncher.cs
scripts/verify-publish-assets.ps1
THIRD_PARTY_NOTICES.md   (only if deployment wording requires clarification)
```

One focused new file or a very small pair is acceptable, e.g.:

```text
src/SteamInputAddonforClaw/Prerequisites/WindowsAppRuntimePrerequisite.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedWindowsAppRuntimeSetup.cs
```

If both responsibilities fit clearly in one file, prefer one file.

Expected tests may include:

```text
tests/SteamInputAddonforClaw.Tests/WindowsAppRuntimePrerequisiteTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendProcessLauncherTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/UiArchitectureTests.cs
scripts/tests/verify-publish-assets.tests.ps1
```

Do not touch unrelated controller files merely to satisfy coverage.

---

## 38. Explicit non-goals

Do not include:

- Windows App SDK 2.4/2.5 SDK upgrade;
- `Microsoft.Windows.SDK.NET.dll` reduction;
- FSE Home TFM cleanup;
- .NET Runtime deployment changes;
- QAM Host changes;
- HidHide/usbip provisioning changes;
- VIIPER changes;
- ClawHUD changes;
- Velopack architecture redesign;
- new installer technology;
- MSIX conversion for main UI;
- Store dependency;
- Winget runtime dependency;
- generic dependency manager;
- multi-user/session support;
- controller authority changes;
- new controller recovery/race machinery.

PR-C will handle the remaining Windows SDK projection/FSE size investigation separately.

---

## 39. Race / overengineering boundary

Follow the repository race/overengineering policy.

Real paths worth handling:

- user double-clicks while tray UI request is also pending;
- Main UI request and Overlay request occur close together;
- UAC cancellation;
- network failure;
- runtime installer process failure;
- Runtime shutdown while setup is in progress;
- system already has a newer compatible Windows App Runtime;
- Windows App Runtime missing on a background boot.

One narrow in-process setup gate is sufficient.

Do not add:

- epochs;
- leases;
- cross-session arbitration;
- package authority manager;
- persistent desired-state database;
- multi-process installer coordinator for unsupported multi-session scenarios.

The supported product model is one user / one interactive session / one primary Addon Runtime.

---

## 40. Validation commands

Run at minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore

pwsh -NoProfile -File scripts/tests/verify-publish-assets.tests.ps1
pwsh -NoProfile -File scripts/tests/report-publish-size.tests.ps1
pwsh -NoProfile -File scripts/tests/release-metadata.tests.ps1

git diff --check
```

Also run the normal Release publish-layout path and:

```text
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
```

against the real output.

If the release packaging command is available locally/CI, record the final Velopack Setup size as well.

---

## 41. Acceptance criteria

PR-B is complete only when all of the following are true.

1. UI remains `Microsoft.WindowsAppSDK 2.3.1`.
2. Overlay remains `Microsoft.WindowsAppSDK 2.3.1`.
3. UI explicitly has `WindowsAppSDKSelfContained=false`.
4. Overlay explicitly has `WindowsAppSDKSelfContained=false`.
5. Both remain unpackaged with `WindowsPackageType=None`.
6. Both remain .NET framework-dependent with `SelfContained=false`.
7. The main Runtime does not gain a Microsoft.WindowsAppSDK NuGet dependency.
8. Windows App Runtime minimum framework version is pinned to 2.3.1.0.
9. The official versioned Microsoft x64 installer URL is pinned.
10. SHA-256 is pinned to `4011748DDF472B7E856D909FDFB4E9B19C3D23FCD8121039AC91F99D5FFA65DB`.
11. Installer execution uses `--quiet`.
12. Compatible newer stable Windows App Runtime 2.x is accepted.
13. A newer runtime is never downgraded.
14. x86-only package evidence does not satisfy the x64 Addon.
15. Background Runtime startup never downloads or elevates solely for WinUI.
16. Background Runtime startup remains operational when WinApp Runtime is absent.
17. Overlay warmup is skipped without UAC when WinApp Runtime is absent.
18. Explicit Main UI request can install the runtime on demand.
19. Explicit Overlay request can install the runtime on demand.
20. Main UI launch never bypasses the runtime availability gate.
21. A queued InitialManualLaunch cannot bypass the gate when `MarkRuntimeReady` releases it.
22. Overlay setup completes before any Main UI retirement or controller capture mutation.
23. Setup failure never mutates Full1902 controller presentation.
24. UAC cancellation leaves Runtime alive.
25. Network/hash/install failure leaves Runtime alive.
26. Parent Runtime re-probes Windows package state after the elevated helper exits.
27. Helper exit code alone is not treated as Runtime readiness.
28. PR-A's `PrerequisiteInstallerAcquisition` is reused.
29. Protected ProgramData staging is reused.
30. No Windows App Runtime provisioning receipt/state machine is introduced.
31. No controller `FirstTimeSetupPolicy` changes are made.
32. Publish output no longer carries the prior self-contained Windows App SDK runtime payload.
33. App-owned PRI/XBF/assets remain present.
34. Required bootstrapper/projection files are not manually deleted.
35. `Microsoft.Windows.SDK.NET.dll` optimization is not included.
36. FSE Home optimization is not included.
37. Publish verifier represents the real framework-dependent layout.
38. Raw publish size before/after is recorded.
39. Full Debug/Release tests pass.
40. Real Release publish verification passes.
41. `git diff --check` is clean.

---

## 42. Final architecture after PR-B

```text
SteamInputAddonforClaw.exe
(headless Runtime; no WinUI dependency)
        |
        +---------------- controller lifecycle ----------------+
        |                                                     |
        |   Center M / PID1902 / HidHide / DirectInput / VIIPER
        |   continue independently of Windows App Runtime
        |
        +---------------- presentation surfaces ---------------+
                              |
                    Windows App Runtime probe
                              |
               +--------------+--------------+
               |                             |
             Ready                     Missing / old
               |                             |
               |                    explicit user request?
               |                      |             |
               |                     no            yes
               |                      |             |
               |                   no-op      elevated helper
               |                                |
               |                    protected ProgramData
               |                                |
               |                    pinned Microsoft URL
               |                                |
               |                       SHA-256 verify
               |                                |
               |                    WindowsAppRuntimeInstall
               |                           --quiet
               |                                |
               |                         parent re-probe
               |                                |
               +--------------- Ready ----------+
                               |
                      +--------+--------+
                      |                 |
                   Main UI          Overlay
                 framework-       framework-
                  dependent        dependent
```

The architectural rule is:

> **The controller Runtime must not depend on WinUI, and WinUI must not carry its own Windows App Runtime.**

PR-B externalizes the shared Windows App Runtime while preserving the existing standalone Full1902 controller lifecycle and all current UI/Overlay ownership boundaries.
