# Work Order — Runtime-Only C#/WinRT Embedded Projection PoC

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@f1b3cbd938f252a63ffe53e8287bff4208b8f11d`  
> **Date:** 2026-09-22  
> **Scope:** experimental Runtime-only C#/WinRT Embedded projection PoC  
> **Product:** standalone Steam Addon for Claw, Full1902  
> **Decision gate:** measure + hardware validate first; merge/adoption is a separate decision

---

## 1. Goal

Evaluate whether the **headless Runtime only** can replace the app-local full Windows SDK projection:

```text
Microsoft.Windows.SDK.NET.dll
26,341,408 bytes
25.12 MiB
```

with C#/WinRT embedded projection/runtime source for only the WinRT APIs actually used by:

```text
src/SteamInputAddonforClaw/
```

The PoC is successful only if it proves all of the following:

```text
Runtime builds normally
+
Runtime publish no longer contains Microsoft.Windows.SDK.NET.dll
+
Runtime publish does not depend on WinRT.Runtime.dll as a loose deployment dependency
+
real raw publish reduction is substantial
+
existing Runtime source architecture stays essentially unchanged
+
Full1902 / HID / PnP / FSE / Windows App Runtime probe / sensor diagnostics work on hardware
```

This is deliberately **not** a UI/Overlay conversion.

---

## 2. Why this PoC is technically plausible

Microsoft documents C#/WinRT Embedded support as compiling the required WinRT runtime and projection sources directly into the consuming app/library binary.

Official behavior:

```text
CsWinRTEmbedded=true
+
CsWinRTIncludes=<required WinRT types/namespaces>
→ generated projection/runtime source embedded into project DLL
→ no dependency on WinRT.Runtime.dll
→ no dependency on Microsoft.Windows.SDK.NET.dll
```

Microsoft explicitly describes this mode as appropriate when WinRT usage is self-contained inside the binary.

Current Runtime satisfies that structural condition:

- WinRT objects are consumed inside `SteamInputAddonforClaw`;
- Runtime converts them to app-owned primitive/record types;
- no `Windows.*` type is intentionally exposed through `SteamInputAddonforClaw.Contracts`;
- no `Windows.*` type is intentionally exposed through `SteamInputAddonforClaw.FrontendTransport`;
- UI and Overlay run as different processes and keep their existing projection assemblies.

References:

- https://learn.microsoft.com/windows/apps/develop/platform/csharp-winrt/
- https://github.com/microsoft/CsWinRT/blob/master/docs/embedded.md
- https://www.nuget.org/packages/Microsoft.Windows.CsWinRT/2.3.1
- https://github.com/microsoft/CsWinRT/releases/tag/2.3.1.260716.1

---

## 3. Current size baseline

PR581 removed the redundant loose FSE Home publish.

The latest directly comparable successful Release-layout CI measurement after that change was:

```text
Total publish:
213,134,969 bytes
203.26 MiB

Runtime:
33,479,952 bytes
31.93 MiB
```

Largest Runtime-root payload:

```text
Microsoft.Windows.SDK.NET.dll
26,341,408 bytes
25.12 MiB
```

UI and Overlay independently still contain their own copies:

```text
ui/Microsoft.Windows.SDK.NET.dll       25.12 MiB
overlay/Microsoft.Windows.SDK.NET.dll  25.12 MiB
```

Those copies are **out of scope**.

The PoC only attempts to remove:

```text
<PublishRoot>/Microsoft.Windows.SDK.NET.dll
```

Do not treat UI/Overlay copies as a PoC failure.

---

## 4. Current Runtime project contract

Current project:

```text
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
```

Current relevant properties:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
<PlatformTarget>x64</PlatformTarget>
```

Keep all of them unchanged.

Do **not**:

- switch to `net10.0`;
- switch to the CsWinRT 3.0 `.1` TFM;
- enable NativeAOT;
- enable trimming;
- make Runtime self-contained;
- change Runtime process architecture;
- change Windows App SDK version.

This PoC isolates one variable: C#/WinRT 2.x Embedded projection.

---

## 5. Use stable CsWinRT 2.3.1 only

Add a direct Runtime-project package reference to:

```xml
<PackageReference Include="Microsoft.Windows.CsWinRT"
                  Version="2.3.1"
                  PrivateAssets="all" />
```

Use stable `2.3.1`.

Do not use:

- CsWinRT 3.0 preview;
- older 1.x examples;
- arbitrary prerelease packages.

CsWinRT 2.3.1 is the current stable 2.x release and supports .NET 10-compatible use.

The direct package reference is intentionally private to this project.

Do not add it to:

- Contracts;
- FrontendTransport;
- QAM Host;
- UI;
- Overlay;
- FseHome;
- helpers;
- test projects.

---

## 6. Initial Embedded project configuration

Start with the smallest project-only change.

Conceptually:

```xml
<PropertyGroup>
  <CsWinRTEmbedded>true</CsWinRTEmbedded>
  <CsWinRTIncludes>
    Windows.Devices.Enumeration;
    Windows.Devices.HumanInterfaceDevice;
    Windows.Devices.Sensors;
    Windows.Management.Deployment;
  </CsWinRTIncludes>
</PropertyGroup>
```

The exact final include list must be derived from compilation, not guessed.

The directly referenced namespaces in current Runtime source are:

```text
Windows.Devices.Enumeration
Windows.Devices.HumanInterfaceDevice
Windows.Devices.Sensors
Windows.Management.Deployment
```

Expected transitive projection requirements may include narrower supporting namespaces such as:

```text
Windows.Foundation
Windows.Foundation.Collections
Windows.ApplicationModel
Windows.Storage
Windows.System
```

Add a supporting namespace **only when actual generated-code/compiler evidence requires it**.

Do not immediately use:

```text
Windows
Windows.Devices
Windows.Management
```

as broad umbrella includes merely to make errors disappear.

The purpose of the PoC is to embed only the projection surface actually required.

---

## 7. Windows metadata selection

Do not add a redundant `CsWinRTWindowsMetadata` property initially.

CsWinRT 2.3.1 derives Windows metadata from the current Windows target platform / Windows SDK ref pack for modern Windows TFMs.

Current TFM already pins:

```text
Windows 10.0.26100.0
```

Only if the actual build fails specifically because Windows metadata cannot be resolved should the PoC add:

```xml
<CsWinRTWindowsMetadata>10.0.26100.0</CsWinRTWindowsMetadata>
```

and document why it was required.

Do not point at developer-machine absolute Windows SDK paths.

CI must remain portable.

---

## 8. Do not downgrade C# language version

Older embedded documentation examples explicitly set:

```xml
<LangVersion>9</LangVersion>
```

Do **not** copy that setting into this .NET 10 project.

Keep the repository's current compiler/language behavior.

The current Runtime source may use newer C# syntax and there is no product reason to downgrade the language version solely because the original embedded feature documentation dates from an older .NET generation.

---

## 9. Runtime WinRT inventory — controller mode transition

File:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs
```

Direct WinRT usage:

```text
HidDevice.GetDeviceSelector(...)
DeviceInformation.FindAllAsync(...)
DeviceInformation.Id
DeviceInformation.Properties
```

Role:

```text
verified MSI Claw control collection
→ construct HID selector
→ enumerate exact interface
→ validate PnP instance/container identity
→ existing native HID transport sends/readbacks MSI mode command
```

This path is controller-critical.

The actual report I/O remains app-owned native HID transport; the Embedded conversion must not rewrite that architecture.

Do not replace `HidDevice` / `DeviceInformation` with a new abstraction merely for this PoC.

---

## 10. Runtime WinRT inventory — rumble endpoint discovery

File:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRumbleEndpointCatalog.cs
```

Direct WinRT usage:

```text
DeviceInformation.FindAllAsync(...)
DeviceInformation.Id
DeviceInformation.Properties
```

The existing authority remains:

```text
WinRT interface enumeration
+
SetupAPI-backed topology
+
native HID capability/open validation
```

Do not change rumble endpoint selection policy.

Do not weaken the existing exact MSI physical-root/topology checks just to simplify generated projection requirements.

---

## 11. Runtime WinRT inventory — FSE registration and package state

Files:

```text
src/SteamInputAddonforClaw/WindowsGaming/SteamFseRegistration.cs
src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
```

Direct WinRT usage:

```text
Windows.Management.Deployment.PackageManager
DeploymentOptions
Package
PackageId
PackageVersion
AddPackageAsync
RemovePackageAsync
FindPackagesForUser
```

This is production FSE behavior.

The PoC must not alter:

- fixed MSIX;
- fixed CER;
- package identity;
- version `1.0.0.0`;
- AUMID derivation;
- certificate handling;
- Developer Mode handling;
- AddPackageAsync/RemovePackageAsync ordering;
- FSE ON/OFF semantics.

Only the projection mechanism may change.

---

## 12. Runtime WinRT inventory — Windows App Runtime package probe

File:

```text
src/SteamInputAddonforClaw/Prerequisites/WindowsAppRuntimePrerequisite.cs
```

Direct WinRT usage:

```text
PackageManager.FindPackagesForUser(...)
Package.Id.FamilyName
Package.Id.Version
Package.Id.Architecture
```

This probe protects Main UI/Overlay startup.

PoC must preserve current semantics:

```text
Ready
Missing
UpdateRequired
Indeterminate
```

and must not change:

- Windows App Runtime 2.5.1 minimum;
- official installer URL/hash;
- explicit-user-request setup boundary;
- headless startup fail-open behavior.

---

## 13. Runtime WinRT inventory — sensor diagnostics

Files:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWinRtSources.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs
```

Direct WinRT usage includes:

```text
Gyrometer.GetDefault()
Accelerometer.GetDefault()
GetCurrentReading()
DeviceId
MinimumReportInterval
ReportInterval
reading Timestamp
Windows package discovery
```

This remains diagnostic/developer functionality, but it is valuable real-hardware coverage for Embedded sensor projection.

Do not change sensor selection/fallback policy in this PoC.

---

## 14. Self-contained WinRT type boundary

Before implementation, verify again that:

```text
src/SteamInputAddonforClaw.Contracts
src/SteamInputAddonforClaw.FrontendTransport
```

do not expose:

```text
Windows.*
WinRT.*
```

types in their contract surfaces.

If a real WinRT type crosses the Runtime assembly boundary, stop and report before inventing a new wrapper architecture.

The current reviewed baseline does not show such a leak.

Do not add a permanent architectural wrapper solely to make Embedded compile.

Existing app-owned records/interfaces are sufficient.

---

## 15. First implementation rule: no product C# behavior changes

Initial PoC change should be limited to:

```text
SteamInputAddonforClaw.csproj
publish verification/tests
measurement evidence
```

plus only the smallest build-contract test necessary.

Do not change controller/FSE/sensor production .cs files during the first Embedded attempt.

If compilation fails because a namespace is missing:

```text
add the narrow required CsWinRTIncludes entry
→ rebuild
```

Do not:

```text
rewrite code
add adapter
add facade
move WinRT code to another assembly
create a WinRT manager
create another process
```

to force the PoC through.

---

## 16. Build iteration strategy

Use this exact approach:

```text
Step 1
→ add CsWinRT 2.3.1 + CsWinRTEmbedded=true
→ include only direct namespaces

Step 2
→ clean restore/build Runtime
→ inspect compiler/generated projection failures

Step 3
→ add only required supporting namespace(s)

Step 4
→ repeat until clean build
```

Capture the final effective include list in the PR description.

Do not solve missing types by embedding the entire Windows SDK namespace surface.

If the required include set becomes unexpectedly broad, report that as a PoC result before continuing.

---

## 17. Generated-source inspection

After the first successful build, inspect:

```text
obj/<Configuration>/<TFM>/<RID>/Generated Files/CsWinRT/
```

or the actual CsWinRT generated-source directory produced by the current toolchain.

Record:

- generated source file count;
- generated source aggregate size;
- namespaces/types projected;
- whether obvious unrelated Windows SDK namespaces were pulled in.

This is diagnostic evidence only.

Do not commit generated CsWinRT source files.

Do not move generated source into the repository.

---

## 18. Runtime publish success contract

Run a clean Release publish using the repository's real layout:

```text
scripts/publish-layout.ps1
```

PoC success requires Runtime root to contain neither:

```text
Microsoft.Windows.SDK.NET.dll
WinRT.Runtime.dll
```

as app-local Runtime projection/runtime dependencies.

Important:

```text
ui/Microsoft.Windows.SDK.NET.dll
overlay/Microsoft.Windows.SDK.NET.dll
```

remain expected and must not be rejected.

This PoC only owns the publish root.

---

## 19. Verify deps.json, not file presence alone

Inspect the published Runtime dependency manifest:

```text
SteamInputAddonforClaw.deps.json
```

The Runtime must not retain a deployment dependency requiring:

```text
Microsoft.Windows.SDK.NET
WinRT.Runtime
```

even if the physical DLL happened not to copy.

The result should prove that projection/runtime code is genuinely embedded rather than that a publish copy step was accidentally suppressed.

Do not manually delete either DLL after publish.

Manual post-publish deletion is not an acceptable implementation.

---

## 20. Publish verifier change — only after actual Embedded publish succeeds

After a real clean Embedded publish proves the expected output, update:

```text
scripts/verify-publish-assets.ps1
```

with focused Runtime-root negative gates.

Conceptually:

```text
<PublishRoot>/Microsoft.Windows.SDK.NET.dll
→ forbidden

<PublishRoot>/WinRT.Runtime.dll
→ forbidden
```

Do not recurse those checks into:

```text
ui/
overlay/
```

because they are explicitly outside this PoC.

Also add a narrow `deps.json` assertion if it can be made robustly without a fragile broad string blacklist.

---

## 21. Publish verifier regression tests

Update:

```text
scripts/tests/verify-publish-assets.tests.ps1
```

Required new cases:

1. valid fixture without Runtime-root SDK projection → success;
2. add root `Microsoft.Windows.SDK.NET.dll` → verifier rejects it;
3. add root `WinRT.Runtime.dll` → verifier rejects it.

Keep the existing QAM-specific projection rejection.

Do not alter UI/Overlay fixture expectations for this PoC.

Do not add dozens of generated-file blacklist cases.

---

## 22. Architecture/project test

Add one focused test or extend an appropriate architecture test to prove the Runtime project contract:

```text
Microsoft.Windows.CsWinRT = 2.3.1
CsWinRTEmbedded = true
TFM remains net10.0-windows10.0.26100.0
SelfContained remains false
RuntimeIdentifier remains win-x64
```

Also verify the project does not switch to:

```text
net10.0-windows10.0.26100.1
```

in this PoC.

Avoid brittle tests that duplicate the entire csproj text.

---

## 23. Test-project scope

Do not enable Embedded in:

```text
tests/SteamInputAddonforClaw.Tests
tests/SteamInputAddonforClaw.UiTests
```

The headless test project may still have its own normal Windows SDK testhost/reference projection due to its Windows TFM.

That does not invalidate the product Runtime PoC.

The success metric is the published production Runtime root.

Do not distort test architecture merely to remove projection files from test output.

---

## 24. Baseline and after-size measurement

Before the Embedded change, record from a clean current-main Release publish:

```text
Total bytes / MiB
Runtime bytes / MiB
SteamInputAddonforClaw.dll bytes
Microsoft.Windows.SDK.NET.dll bytes
WinRT.Runtime.dll bytes if present
```

Known reference baseline:

```text
Total:
213,134,969 bytes
203.26 MiB

Runtime:
33,479,952 bytes
31.93 MiB

Microsoft.Windows.SDK.NET.dll:
26,341,408 bytes
25.12 MiB
```

After Embedded:

record the same values.

Also report:

```text
SteamInputAddonforClaw.dll growth
net Runtime reduction
net total publish reduction
```

Do not claim the full 25.12 MiB as saved until measured.

The embedded generated source will increase `SteamInputAddonforClaw.dll`.

---

## 25. Optional packaged-size measurement

If the normal local packaging path is available without unrelated release work, also run:

```text
scripts/pack.ps1
```

and compare:

- full nupkg;
- Portable ZIP;
- Setup EXE.

This is useful evidence but not required to prove Embedded correctness.

Raw publish reduction is the primary PoC metric.

---

## 26. Full1902 hardware validation — mandatory before adoption

Compilation and unit tests are not sufficient.

The following is controller-critical because current PID mode transition uses WinRT HID/device enumeration.

### A. Disabled-mode acquisition from PID1901

Start from the supported stock/MSI-authority state.

Perform the normal product transition:

```text
Center M Enabled / PID1901
→ Disable Center M and Restart
→ next boot Runtime authority
→ PID1901 → PID1902
→ exact controller isolation
→ VIIPER presentation
```

Verify:

- exact physical device is selected;
- mode write succeeds;
- PID1902 appears;
- no wrong-device write;
- HidHide convergence remains correct;
- exactly one virtual controller presentation is exposed.

---

## 27. Full1902 hardware validation — existing PID1902 restart

While Addon authority is active:

```text
PID1902
→ controlled Runtime restart/update-style restart
→ Runtime returns
→ reacquire same PID1902
→ no intentional 1902→1901→1902 round trip
```

Verify DirectInput/VIIPER/HidHide behavior is unchanged.

Embedded must not affect desired-state authority.

---

## 28. Full1902 hardware validation — sleep/hibernate/resume

Validate normal supported lifecycle:

```text
Addon authority + PID1902
→ Sleep
→ Resume
→ controller remains/reconverges safely
```

and, where practical:

```text
Hibernate
→ Resume
```

Check:

- device enumeration;
- physical input reacquisition;
- virtual presentation;
- no duplicate controller;
- no stuck/neutral presentation;
- no incorrect stock restoration.

Do not add new synchronization machinery for artificial instruction-level races.

---

## 29. Full1902 hardware validation — PnP re-enumeration/device loss

Use an existing realistic product/device test procedure that causes the MSI controller interfaces to disappear/re-enumerate.

Validate:

```text
temporary device loss/re-enumeration
→ current owner remains Addon
→ exact physical device returns
→ Runtime converges
```

The Embedded PoC must not change PnP authority/recovery code.

Do not invent unsupported multi-device or multi-session scenarios.

---

## 30. X360 ↔ SteamDeck presentation regression

While PID1902 authority is stable:

```text
Steam/BPM inactive
→ X360

Steam/BPM active
→ SteamDeck

Steam/BPM exits
→ X360
```

This does not directly exercise a WinRT API, but it verifies the overall controller Runtime was not destabilized by the projection change.

---

## 31. Rumble validation

Exercise current rumble behavior after PID1902 ownership.

Required evidence:

- endpoint enumeration still finds the exact expected interface;
- SetupAPI topology validation remains unchanged;
- HID capability read succeeds;
- normal rumble plays;
- rumble stops normally;
- no duplicate/infinite rumble regression.

This specifically validates the Embedded `Windows.Devices.Enumeration` projection path used by rumble discovery.

---

## 32. FSE validation

The Runtime Embedded projection owns the `PackageManager` API used by FSE.

At minimum validate:

### Existing package

```text
registered FSE package
→ Settings capture
→ correct Enabled/Disabled readback
```

### First registration, on a safe test setup

Where practical, begin without the exact owned FSE package and perform:

```text
FSE ON
→ elevated registration
→ fixed MSIX AddPackageAsync
→ parent exact package readback
→ actual FamilyName + "!App"
→ GamingHomeApp
→ StartupToGamingHome=true
```

Then verify Windows Gaming Home / FSE can still launch the packaged FseHome and Steam BPM.

Do not rebuild the FSE MSIX for this PoC.

---

## 33. Windows App Runtime probe validation

The Runtime also uses `PackageManager` to determine whether the shared Windows App Runtime 2.5.1+ is available.

Validate on the normal hardware environment:

```text
Windows App Runtime 2.5.1+ installed
→ package probe == Ready
→ Main UI launches
→ Overlay launches
→ no unnecessary setup/UAC
```

If a disposable environment is available, separately validate Missing/UpdateRequired behavior, but do not uninstall shared Windows App Runtime from a normal workstation solely for this PoC.

---

## 34. Sensor diagnostic validation

Run the existing developer sensor/gyro diagnostic path.

Validate:

```text
Gyrometer.GetDefault()
Accelerometer.GetDefault()
GetCurrentReading()
DeviceId
MinimumReportInterval
ReportInterval set/reset
Timestamp mapping
```

The generated Embedded projection must preserve the current reading units/behavior.

Do not change diagnostic fallback or candidate-selection logic.

---

## 35. Environment Discovery validation

Run the existing environment discovery/report path that uses:

```text
PackageManager
Gyrometer
Accelerometer
```

Confirm it completes without:

- type-load errors;
- COM activation errors;
- FileLoadException;
- InvalidCastException;
- missing projection methods.

This is a useful broad Runtime WinRT smoke test.

---

## 36. Startup smoke

Keep the existing CI startup smoke:

```text
SteamInputAddonforClaw.exe --background
→ process remains alive
```

But do not treat the 5-second process survival test as proof of WinRT correctness.

Hardware validation remains mandatory before adoption.

---

## 37. Realistic failure handling

A broken projection may present as:

- build failure;
- type load failure;
- COM/WinRT activation failure;
- async-operation projection failure;
- package enumeration failure;
- HID enumeration failure;
- sensor read failure.

Do not convert these into new fallback state machines.

For the PoC:

```text
identify exact missing/broken projection
→ determine whether a narrow include fixes it
→ otherwise report PoC failure
```

Do not mask Embedded projection errors by silently bypassing controller/FSE safety checks.

---

## 38. Stop conditions

Stop the PoC and report instead of escalating architecture if any of these becomes true:

1. Runtime product C# requires substantial behavior rewrite merely to compile with Embedded.
2. A new wrapper/manager/facade assembly is required solely for projection ownership.
3. WinRT types must be moved across assembly boundaries.
4. Include scope must be widened to essentially the full Windows SDK projection.
5. Runtime publish still requires `Microsoft.Windows.SDK.NET.dll`.
6. Runtime publish still requires loose `WinRT.Runtime.dll`.
7. Full1902 PID1901↔PID1902 transition regresses.
8. PnP/recovery regresses.
9. FSE package registration/readback regresses.
10. Windows App Runtime probe regresses.
11. meaningful sensor/DeviceInformation runtime failures appear.
12. size reduction is too small to justify the added build dependency/maintenance after real measurement.

Do not add complexity just to claim the PoC succeeded.

---

## 39. Overengineering guardrail

This PoC must not add:

- another controller authority;
- another lifecycle state;
- projection service;
- projection manager;
- Runtime broker;
- common shared DLL probing scheme;
- assembly resolver;
- custom AssemblyLoadContext;
- symlink/hardlink deduplication;
- external Windows SDK projection installer;
- service;
- IPC;
- retry daemon;
- new locks/epochs/barriers for theoretical races.

The desired result is:

```text
same Runtime architecture
+
different build-time WinRT projection strategy
+
smaller deployment
```

If the implementation cannot remain that simple, the optimization should be rejected.

---

## 40. Explicit non-goals

Do not include:

- UI C#/WinRT Embedded;
- Overlay C#/WinRT Embedded;
- FseHome retarget/rebuild;
- CsWinRT 3.0 preview;
- `.1` TFM migration;
- NativeAOT;
- PublishTrimmed;
- ReadyToRun changes;
- Windows App SDK upgrade;
- Windows App Runtime provisioning changes;
- Full1902 refactor;
- HID transport rewrite;
- rumble refactor;
- FSE behavior change;
- gyro behavior change;
- dependency sharing across processes;
- common projection directory;
- manual DLL deletion in packaging.

---

## 41. Expected files to change

Initial expected production/config changes:

```text
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
```

Possibly one focused architecture/project contract test under:

```text
tests/SteamInputAddonforClaw.Tests/
```

No controller/FSE/sensor production `.cs` file is expected to change.

If those files start changing, stop and re-evaluate whether the PoC is becoming product redesign.

---

## 42. Validation commands

Run from a clean tree/worktree.

### Restore

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet restore tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
```

### Build

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-restore
dotnet build tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
```

### Tests

```text
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug --no-build --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-build --no-restore
```

### Script tests

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/report-publish-size.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/release-metadata.tests.ps1
```

### Release layout

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 \
  -Version 0.1.0 \
  -Configuration Release \
  -PublishDirectory artifacts/publish
```

### Verify

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 \
  -PublishDirectory artifacts/publish
```

### Size

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/report-publish-size.ps1 \
  -PublishDirectory artifacts/publish
```

### Diff

```text
git diff --check
```

---

## 43. Evidence required in the PoC PR description

Report exact before/after values.

Required table:

```text
Metric                              Before          After
---------------------------------------------------------
Total publish bytes
Total publish MiB
Runtime bytes
Runtime MiB
SteamInputAddonforClaw.dll bytes
Microsoft.Windows.SDK.NET.dll       26,341,408      absent
WinRT.Runtime.dll                   <measured>      absent
```

Also include:

```text
final CsWinRTIncludes list
CsWinRT package version
generated source file count/size
build/test result
hardware validation completed / not completed
known limitations
```

Do not report only percentage reduction.

---

## 44. PoC acceptance gate

The implementation phase is complete when:

1. Runtime uses stable `Microsoft.Windows.CsWinRT 2.3.1`.
2. Runtime has `CsWinRTEmbedded=true`.
3. include set is narrow and evidence-derived.
4. current `net10.0-windows10.0.26100.0` TFM is unchanged.
5. Runtime product behavior code remains essentially unchanged.
6. Debug and Release builds pass.
7. headless and UI test suites pass.
8. Release publish succeeds.
9. Runtime root no longer contains `Microsoft.Windows.SDK.NET.dll`.
10. Runtime root no longer requires loose `WinRT.Runtime.dll`.
11. Runtime `deps.json` confirms those deployment dependencies are gone.
12. UI/Overlay projection payload remains untouched.
13. actual raw size reduction is measured.
14. startup smoke passes.
15. hardware Full1902 tests are documented.

---

## 45. Adoption gate — do not automatically merge because the PoC builds

After implementation, report results before treating this as production-ready.

Recommended decision:

### Candidate for production adoption

Only if:

```text
meaningful measured size reduction
+
no product-code redesign
+
PID1901↔PID1902 PASS
+
PID1902 controlled restart PASS
+
Sleep/Resume PASS
+
PnP/re-enumeration PASS
+
rumble PASS
+
FSE PackageManager paths PASS
+
Windows App Runtime probe PASS
+
sensor/environment discovery PASS
```

### Reject / revert PoC

If the optimization requires disproportionate architecture or produces any realistic controller/FSE lifecycle regression.

The PoC branch may be discarded cleanly.

---

## 46. CsWinRT 3.0 note

This PoC intentionally uses CsWinRT 2.3.1 Embedded.

CsWinRT 3.0 preview is a separate future architecture and removes the 2.x Embedded mode.

Therefore:

```text
current PoC
→ measure whether 2.x Embedded provides worthwhile deployment reduction now

future CsWinRT 3.0 stable
→ separate migration/size PoC
→ do not assume this 2.x project configuration carries forward
```

This is acceptable because the Runtime source remains normal WinRT-consuming C#; the Embedded-specific change is kept in build configuration rather than product architecture.

---

## 47. Final principle

```text
Do not redesign Runtime to save 25 MiB.

Ask CsWinRT to generate only the WinRT projection Runtime already needs.

If that works transparently and hardware behavior stays identical, keep it.

If it requires architectural contortions, discard it.
```
