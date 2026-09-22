# Work Order — Upgrade Windows App SDK 2.3.1 → 2.5.1

> **Repository:** onehoon/SteamAddonforClaw
> **Reviewed baseline:** main@b81aa292146be9c951687739c9135b1d4b787e66
> **Date:** 2026-09-22
> **Scope:** focused Windows App SDK / Windows App Runtime version upgrade only
> **Architecture baseline:** standalone Full1902 application; CTW integration is out of scope

---

## 1. Goal

Upgrade the production WinUI surfaces from Microsoft.WindowsAppSDK 2.3.1 to 2.5.1 while preserving the framework-dependent deployment architecture completed by PR #578.

This is not a return to app-local/self-contained Windows App SDK deployment.

Target architecture remains:

    SteamInputAddonforClaw.exe
      -> headless Runtime
      -> no Microsoft.WindowsAppSDK dependency
      -> remains operational without Windows App Runtime

    SteamInputAddonforClaw.UI.exe
    SteamInputAddonforClaw.Overlay.exe
      -> unpackaged WinUI
      -> framework-dependent
      -> WindowsAppSDKSelfContained=false
      -> shared machine-installed Windows App Runtime 2.x

The upgrade must also raise the Addon's on-demand Windows App Runtime prerequisite from 2.3.1 to 2.5.1 so the external runtime requirement matches the SDK used to build UI and Overlay.

---

## 2. Source authority

Read before implementation:

1. docs/Full 1902 Implementation/README.md
2. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
3. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
4. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
5. docs/work-order/WINDOWS_APP_SDK_FRAMEWORK_DEPENDENT_RUNTIME_BOOTSTRAP_WORK_ORDER.md
6. PR #578 — framework-dependent Windows App Runtime bootstrap
7. PR #579 — WinUI testhost isolation

The Full1902 controller Runtime remains authoritative independently of WinUI.

Do not modify controller ownership, HidHide, DirectInput, VIIPER, PID1901/PID1902, presentation switching, recovery, suspend/resume, startup authority, or fail-close policy for this SDK upgrade.

---

## 3. Current main — verified state

Both production WinUI projects are already framework-dependent.

Main UI:

    src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj

Current contract:

    WindowsPackageType=None
    UseWinUI=true
    RuntimeIdentifier=win-x64
    SelfContained=false
    WindowsAppSDKSelfContained=false
    Microsoft.WindowsAppSDK=2.3.1

Overlay:

    src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj

has the same production deployment contract and Microsoft.WindowsAppSDK=2.3.1.

The headless Runtime project must remain free of Microsoft.WindowsAppSDK and UseWinUI. Do not add the package to Runtime merely to share version metadata.

---

## 4. Why 2.5.1 is worth taking

Windows App SDK 2.5.1 is the current stable 2.x release, published 2026-09-16.

This PR does not need any new 2.5 API. The value is primarily reliability.

Relevant fixes between 2.3.1 and 2.5.1 include:

### 2.4.0

- touch/pen scroll input crash fix;
- Storage Picker focus restoration;
- MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY child-process inheritance isolation;
- generated C# entry-point helper fix when DISABLE_XAML_GENERATED_MAIN is defined.

The entry-point helper fix is directly relevant because both production WinUI projects define DISABLE_XAML_GENERATED_MAIN.

The child-process isolation fix is relevant because Runtime, UI and Overlay are separate processes.

### 2.5.1

- windowed-popup focus/pointer crash after popup island disposal;
- NavigationView negative MaxHeight resize crash;
- stale deferred NavigationViewItem flyout crash;
- pointer-position property-set updates while pressed;
- OEM/punctuation KeyboardAccelerator fail-fast fix;
- fractional-scale CommandBar overflow fix;
- deployment registration fix.

The Main UI currently uses NavigationView, so the NavigationView reliability fixes are directly relevant.

New features such as AppContentSearch and the self-contained .NET MSIX Windows Error Reporting extension are not reasons to expand this PR and must not be adopted here.

Official references:

    https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.5.1
    https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.4.0
    https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
    https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
    https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1

---

## 5. SDK and external Runtime must move together

Microsoft.WindowsAppSDK 2.5.1 depends on Microsoft.WindowsAppSDK.Runtime = 2.5.1.

Production is framework-dependent, so the runtime package is not copied into the application payload. The unpackaged WinUI bootstrapper resolves the stable framework package from the machine.

The existing Addon prerequisite currently accepts:

    Microsoft.WindowsAppRuntime.2
    x64
    version >= 2.3.1.0

That minimum must change with this SDK upgrade.

Target:

    Microsoft.WindowsAppRuntime.2
    x64
    version >= 2.5.1.0
    -> Ready

Expected classification after this PR:

    2.3.1.0 -> UpdateRequired
    2.4.0.0 -> UpdateRequired
    2.5.0.0 -> UpdateRequired
    2.5.1.0 -> Ready
    future compatible stable 2.x > 2.5.1.0 -> Ready

Do not require exact equality to 2.5.1.0.

Windows App SDK 2.x uses the stable framework family:

    Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe

The unpackaged bootstrap path uses a minimum version constraint and may resolve a newer compatible 2.x package. Preserve that roll-forward behavior. Do not implement an exact-minor runtime selector.

---

## 6. Production package changes

Change these SDK references only.

UI:

    src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
    Microsoft.WindowsAppSDK 2.3.1 -> 2.5.1

Overlay:

    src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
    Microsoft.WindowsAppSDK 2.3.1 -> 2.5.1

Preserve:

    WindowsPackageType=None
    UseWinUI=true
    SelfContained=false
    WindowsAppSDKSelfContained=false
    RuntimeIdentifier=win-x64

Do not add a manual Bootstrap initialization path. The existing unpackaged framework-dependent auto-bootstrap path remains authoritative.

---

## 7. Windows App Runtime prerequisite update

Update:

    src/SteamInputAddonforClaw/Prerequisites/WindowsAppRuntimePrerequisite.cs

Minimum version:

    new Version(2, 3, 1, 0)
    ->
    new Version(2, 5, 1, 0)

Keep the package family unchanged:

    Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe

Use the exact official Microsoft 2.5.1 x64 installer URL:

    https://aka.ms/windowsappsdk/2.5/2.5.1/windowsappruntimeinstall-x64.exe

This exact URL is also used by Microsoft's microsoft/windows-rs CI for its current 2.5.1 runtime setup.

Keep:

    InstallerFileName = WindowsAppRuntimeInstall-x64.exe
    SilentInstallerArguments = --quiet

### SHA-256 pin

The application security contract requires an exact SHA-256 before elevation/execution.

A current public package manifest reports the 2.5.1 x64 installer hash as:

    931A421E8DC3E6E67724806CB67FECDBB88DFE323F0170842EB4A4B4B149F1E2

Unlike the existing 2.3.1 pin, a Microsoft Winget 2.5.1 manifest was not present when this work order was prepared.

Therefore implementation MUST independently verify the hash from the exact official Microsoft URL before changing the production pin.

Suggested verification:

    Invoke-WebRequest -Uri "https://aka.ms/windowsappsdk/2.5/2.5.1/windowsappruntimeinstall-x64.exe" -OutFile ".\WindowsAppRuntimeInstall-x64.exe"
    Get-FileHash ".\WindowsAppRuntimeInstall-x64.exe" -Algorithm SHA256

Policy:

    computed SHA-256 == 931A421E...
      -> may commit the pin

    computed SHA-256 differs
      -> STOP
      -> do not guess or silently trust the observed public hash
      -> re-verify the official 2.5.1 artifact

Do not use a latest URL, Winget as a runtime dependency, Store UI, a mirror, x86/ARM64 installer, or an unverified executable.

---

## 8. Preserve PR #578 on-demand installation

Do not redesign the existing prerequisite architecture.

Existing policy remains:

    background Runtime startup
    + Windows App Runtime missing/old
      -> no download
      -> no UAC
      -> Runtime/controller lifecycle remains alive

    explicit Main UI request
    or explicit Overlay request
    + runtime missing/old
      -> existing prerequisite gate
      -> trusted ProgramData staging
      -> exact official installer
      -> SHA-256 verification
      -> elevation
      -> --quiet
      -> parent package re-probe
      -> only then launch requested WinUI surface

Do not add another prerequisite manager, installer coordinator, persistent install state, second probe, package authority, background runtime updater, or new state machine.

Reuse WindowsAppRuntimePrerequisite, ElevatedWindowsAppRuntimeSetup, PrerequisiteInstallerAcquisition, trusted staging, and parent readback.

---

## 9. Update user-facing minimum-version wording

Current AddonProcessHost fallback warning says:

    Windows App Runtime 2.3.1 or newer is required to open this surface.

Update it to:

    Windows App Runtime 2.5.1 or newer is required to open this surface.

Do not otherwise change the Runtime-survives-UI-failure behavior.

---

## 10. Tests that encode the old version

### Runtime prerequisite tests

Update:

    tests/SteamInputAddonforClaw.Tests/WindowsAppRuntimePrerequisiteTests.cs

The version classification test must prove at least:

    2.3.1.0 x64 -> UpdateRequired
    2.4.0.0 x64 -> UpdateRequired
    2.5.0.0 x64 -> UpdateRequired
    2.5.1.0 x64 -> Ready
    newer stable 2.x x64 -> Ready

Keep current coverage for:

- no package;
- x86-only package;
- wrong family;
- malformed version;
- enumeration failure;
- already-ready no elevation;
- one setup attempt for concurrent surface requests;
- parent readback;
- hash validation;
- post-install readiness.

Do not add new race machinery. The existing one in-process setup gate is sufficient.

### UI architecture tests

PR #579 moved WinUI-dependent tests into:

    tests/SteamInputAddonforClaw.UiTests

Update:

    tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs

so the production package assertion expects Microsoft.WindowsAppSDK 2.5.1 instead of 2.3.1.

Keep assertions that production UI/Overlay remain:

    WindowsPackageType=None
    UseWinUI=true
    SelfContained=false
    WindowsAppSDKSelfContained=false
    win-x64
    no manual Bootstrap.Initialize

---

## 11. PR #579 test-only exception

Do not confuse testhost isolation with production deployment.

Current:

    tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj

intentionally references UI and Overlay with:

    AdditionalProperties: WindowsAppSDKSelfContained=true

This was introduced by PR #579 so the WinUI testhost does not enter the production framework-dependent bootstrap path before tests execute.

Preserve this test-only override unless 2.5.1 produces a concrete build/test failure that requires a focused adjustment.

Required distinction:

    Production UI/Overlay
      -> WindowsAppSDKSelfContained=false
      -> never bundle Windows App Runtime

    UiTests testhost
      -> existing self-contained ProjectReference override
      -> test infrastructure only
      -> must never flow into release publish/package

Do not remove the test isolation merely to make every project property look identical.

---

## 12. Third-party notice

Update THIRD_PARTY_NOTICES.md:

    Microsoft.WindowsAppSDK
    Version: 2.3.1
    ->
    Version: 2.5.1

Do not rewrite unrelated notice text in this focused PR.

---

## 13. Historical work orders remain historical

Do not edit older work orders merely because they mention 2.3.1.

In particular, docs/work-order/WINDOWS_APP_SDK_FRAMEWORK_DEPENDENT_RUNTIME_BOOTSTRAP_WORK_ORDER.md correctly records that PR #578 intentionally kept 2.3.1 while changing the deployment model.

Only current production code, current tests, and current notices should change.

---

## 14. No new 2.5 features

Do not adopt or enable:

- AppContentSearch;
- Windows AI additions;
- Windows Error Reporting manifest extensions;
- new haptics APIs;
- System Composition Engine migration;
- XamlOptionalChanges;
- new input architecture;
- new picker architecture;
- new navigation architecture.

Do not add a compatibility wrapper around 2.5.1.

This PR is dependency/runtime-baseline maintenance, not a WinUI redesign.

---

## 15. Known 2.5.1 issue — no speculative workaround

At work-order preparation time, microsoft/WindowsAppSDK issue #6774 reports an AppNotificationManager.Register problem for self-contained unpackaged apps.

Current production Addon is framework-dependent and repository search found no AppNotificationManager usage.

Therefore do not block this upgrade for that issue, add notification workarounds, or reintroduce self-contained production deployment.

If implementation discovers a real affected Addon path, stop and report the concrete path instead of adding speculative handling.

---

## 16. Expected files

Expected production changes:

    src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
    src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
    src/SteamInputAddonforClaw/Prerequisites/WindowsAppRuntimePrerequisite.cs
    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
    THIRD_PARTY_NOTICES.md

Expected test changes:

    tests/SteamInputAddonforClaw.Tests/WindowsAppRuntimePrerequisiteTests.cs
    tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs

Modify another file only when a concrete restore/build/test/publish failure proves it is required by the 2.5.1 upgrade.

Do not touch unrelated controller/runtime code for cleanup.

---

## 17. Restore/build/test validation

PR #579 intentionally keeps the WinUI test project outside SteamInputAddonforClaw.slnx.

Run the headless solution and WinUI test project separately.

At minimum:

    dotnet restore SteamInputAddonforClaw.slnx
    dotnet restore tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj

    dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
    dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

    dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-restore
    dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore

    dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build --no-restore
    dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore

    dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-build --no-restore
    dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-build --no-restore

    pwsh -NoProfile -File scripts/tests/verify-publish-assets.tests.ps1
    pwsh -NoProfile -File scripts/tests/report-publish-size.tests.ps1
    pwsh -NoProfile -File scripts/tests/release-metadata.tests.ps1

    git diff --check

Also run the normal Release publish-layout path followed by:

    scripts/verify-publish-assets.ps1
    scripts/report-publish-size.ps1

---

## 18. Publish contract — critical

The Release publish/package must continue to prove that Windows App Runtime is not bundled.

After the upgrade:

    UI publish
    Overlay publish
      -> app-owned EXE/DLL
      -> app-owned PRI/XBF/assets
      -> required bootstrap/projection build artifacts only
      -> no self-contained Windows App Runtime payload

Do not weaken the existing publish verifier to make 2.5.1 pass.

If 2.5.1 changes the expected framework-dependent output shape, inspect the actual SDK build output and adjust the verifier narrowly to represent the supported Microsoft framework-dependent layout.

Do not blindly delete files named Microsoft.*.

Preserve app-owned PRI/XBF/assets and required framework-dependent bootstrap/projection files.

Record the Release publish footprint after the upgrade. A small dependency/build-output movement is acceptable; a return toward the pre-#578 self-contained UI/Overlay size is not.

---

## 19. Manual smoke validation

On a supported Windows 11 x64 machine validate:

### Already on 2.5.1+

    runtime >= 2.5.1
      -> background Addon starts normally
      -> Main UI opens without UAC/download
      -> Overlay opens without UAC/download

### Older stable 2.x installed

Preferably test with 2.3.1 or 2.4.0:

    background Runtime starts
      -> no UAC
      -> no forced install

    explicit UI/Overlay request
      -> UpdateRequired
      -> install official 2.5.1 runtime
      -> parent re-probe Ready
      -> requested surface opens

### UI smoke

Check:

- Main UI startup;
- Main NavigationView page switching;
- repeated window resize;
- Settings navigation;
- ComboBox/Flyout/Popup interactions;
- touch scroll on the Claw display;
- 150% display scale;
- close/reopen Main UI while Runtime remains alive.

### Overlay smoke

Check:

- Overlay warmup;
- repeated open/close;
- Main UI ↔ Overlay handoff;
- controller navigation/value rows;
- popup/flyout surfaces if any;
- outside-click close;
- process shutdown/restart.

Do not turn this SDK PR into a general UI polish pass.

---

## 20. Full1902 regression boundary

This version upgrade must not alter:

- Center M Disabled durable Addon authority;
- mandatory Runtime startup;
- PID1902 acquisition;
- GamepadMode=2 invariant;
- DirectInput ownership;
- HidHide baseline;
- VIIPER ownership/teardown;
- Xbox360 ↔ SteamDeck presentation switching;
- physical-device recovery;
- sleep/hibernate/resume;
- shutdown/restart;
- Center M re-enable stock restoration;
- routing fail-close behavior.

Key invariant:

    WinUI unavailable != controller Runtime unavailable

A Windows App Runtime install/update failure may block the requested UI surface. It must not release, reset, or mutate controller authority.

---

## 21. Race / overengineering boundary

No new race-defense architecture is required.

Preserve the existing setup gate, frontend launch gate, Overlay/Main UI ordering, and parent package re-probe.

Real failures to keep handling:

- UAC cancel;
- network failure;
- hash mismatch;
- installer failure;
- shutdown during explicit setup;
- old runtime requiring update;
- missing runtime at background startup.

Do not add runtime-version epochs, dependency leases, cross-process package managers, multi-session installer authority, new retry state machines, or exact-minor runtime-selection machinery.

The supported product remains one Windows user / one interactive session.

---

## 22. Acceptance criteria

The PR is complete only when all are true:

1. UI references Microsoft.WindowsAppSDK 2.5.1.
2. Overlay references Microsoft.WindowsAppSDK 2.5.1.
3. Production UI remains WindowsAppSDKSelfContained=false.
4. Production Overlay remains WindowsAppSDKSelfContained=false.
5. Both remain SelfContained=false.
6. Both remain unpackaged with WindowsPackageType=None.
7. Main Runtime still has no Microsoft.WindowsAppSDK dependency.
8. Runtime minimum becomes 2.5.1.0.
9. Stable framework family remains Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe.
10. 2.3.1 and 2.4.0 no longer satisfy the UI prerequisite.
11. 2.5.1 satisfies it.
12. Newer compatible stable 2.x still satisfies it.
13. No runtime downgrade behavior is introduced.
14. Official versioned 2.5.1 x64 installer URL is pinned.
15. Installer SHA-256 is independently verified before the production pin is changed.
16. Existing hash-before-execute policy remains intact.
17. Background startup still never downloads/elevates solely for WinUI.
18. Explicit UI/Overlay requests still use the existing on-demand install gate.
19. Parent package readback remains required after installation.
20. User-facing runtime minimum wording says 2.5.1.
21. UiArchitectureTests expects 2.5.1.
22. Runtime prerequisite tests reflect the new minimum.
23. PR #579 test-only self-contained override remains isolated from production.
24. Release publish does not contain a self-contained Windows App Runtime payload.
25. App-owned PRI/XBF/assets remain present.
26. THIRD_PARTY_NOTICES.md identifies Microsoft.WindowsAppSDK 2.5.1.
27. Historical work orders are not rewritten.
28. No new 2.5 feature is adopted.
29. No Full1902 controller lifecycle behavior is changed.
30. Debug and Release headless builds/tests pass.
31. Debug and Release WinUI test builds/tests pass separately.
32. Release publish verification passes.
33. Publish-size report does not show accidental SDK rebundling.
34. git diff --check is clean.

---

## 23. Intended implementation shape

The intended diff should remain small:

    UI csproj
      2.3.1 -> 2.5.1

    Overlay csproj
      2.3.1 -> 2.5.1

    WindowsAppRuntimeMetadata
      minimum 2.3.1.0 -> 2.5.1.0
      installer URL 2.3.1 -> 2.5.1
      verified SHA-256 -> 2.5.1 artifact

    AddonProcessHost
      user-facing minimum 2.3.1 -> 2.5.1

    tests
      expected SDK/minimum/runtime classification -> 2.5.1

    THIRD_PARTY_NOTICES
      2.3.1 -> 2.5.1

Do not expand this into another deployment refactor.

The architectural rule after the PR remains:

> **Build against Windows App SDK 2.5.1, run against a separately installed compatible Windows App Runtime 2.5.1-or-newer, and never make WinUI a dependency of the Full1902 controller Runtime.**
