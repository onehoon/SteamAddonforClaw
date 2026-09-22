# Work Order — PR-C: Remove Redundant Loose FSE Publish Payload

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@13ba7e9dd91039d159f91985549d4c2d7a670d80`  
> **Date:** 2026-09-22  
> **Scope:** release/publish payload cleanup only  
> **Product:** standalone Steam Addon for Claw, Full1902  
> **FSE package:** fixed Steam Big Picture Windows Gaming Home package `1.0.0.0`

---

## 1. Goal

Remove the redundant **loose FSE Home publish output** from the normal Addon release layout while preserving the already validated fixed FSE MSIX and all current Windows Full Screen Experience behavior.

Current release layout does both:

```text
dotnet publish SteamInputAddonforClaw.FseHome
→ fse\SteamInputAddonforClaw.FseHome.exe
→ fse\SteamInputAddonforClaw.FseHome.dll
→ fse\*.deps/runtime/projection payload

AND

copy fixed distribution artifacts
→ fse\SteamInputAddonforClaw.FseHome.msix
→ fse\SteamInputAddonforClaw.FseHome.cer
```

Only the fixed MSIX/CER are used by the production FSE registration path.

Target release layout:

```text
fse\
├─ SteamInputAddonforClaw.FseHome.msix
└─ SteamInputAddonforClaw.FseHome.cer
```

Do not rebuild or modify the fixed FSE package in this PR.

---

## 2. Why this is safe for Windows FSE

The current product flow was re-reviewed end to end.

### Enable flow

```text
Settings → Steam FSE ON
→ WindowsGamingHomeConfiguration.SetEnabledAsync()
→ exact owned package probe
→ when missing/old:
     SteamFseRegistrationClient.EnsureRegisteredAsync()
→ elevated --register-fse-home
→ SteamFseElevatedRegistration
→ fixed installed file:
     fse\SteamInputAddonforClaw.FseHome.msix
→ PackageManager.AddPackageAsync(...)
→ exact package readback
→ derive package.Id.FamilyName + "!App"
→ write GamingHomeApp
→ write StartupToGamingHome=true
```

The loose release files:

```text
fse\SteamInputAddonforClaw.FseHome.exe
fse\SteamInputAddonforClaw.FseHome.dll
...
```

are never used by that registration path.

### Windows boot/FSE activation flow

The fixed MSIX manifest declares:

```text
Identity Name = SteamInputAddonforClaw.FseHome
Publisher = CN=SteamInputAddonforClaw
Application Id = App
ProcessorArchitecture = x64
windows.gamingApp
Microsoft.appCategory.gamingHome_8wekyb3d8bbwe
```

and:

```xml
<Application
    Id="App"
    Executable="SteamInputAddonforClaw.FseHome.exe"
    EntryPoint="Windows.FullTrustApplication"
    uap18:RuntimeBehavior="win32App">
```

Therefore Windows activates the executable **contained in the registered MSIX package**, not the unrelated loose executable beside the MSIX in the Addon installation directory.

That packaged FSE launcher keeps its current narrow behavior:

```text
Windows Gaming Home activates package
→ packaged SteamInputAddonforClaw.FseHome.exe
→ steam://open/bigpicture
→ launcher exits
→ normal Runtime Steam/BPM observation handles Full1902 presentation
```

No loose `fse\*.exe` path participates in the actual Windows FSE activation contract.

---

## 3. Fixed FSE artifact is the authority

Current production package contract:

```text
PackageIdentityName:
SteamInputAddonforClaw.FseHome

ApplicationId:
App

PackageRelativePath:
fse\SteamInputAddonforClaw.FseHome.msix

CertificateRelativePath:
fse\SteamInputAddonforClaw.FseHome.cer

FixedPackageVersion:
1.0.0.0
```

Current fixed distribution artifacts must remain byte-for-byte unchanged in PR-C.

Pinned verification values already in `scripts/verify-publish-assets.ps1`:

```text
MSIX SHA-256:
9D4C46ABCC1324803AE5AB031B11EC8EF39057D77C9C04FCB243D80BC122F86B

CER SHA-256:
663053482DA50F9017CC902CA5DF6E9BBFD5A6F06624B8608266318F54687390
```

Current repository MSIX blob size at the reviewed baseline:

```text
6,937,073 bytes
```

Do not change:

- the MSIX;
- the CER;
- their hashes;
- FSE package version;
- package identity;
- publisher;
- AUMID derivation;
- manifest;
- SCCD;
- Gaming Home capability;
- FSE registration behavior.

If either binary artifact changes, stop and treat that as a separate FSE artifact rebuild/versioning PR.

---

## 4. Important correction from the earlier PR-C idea

Do **not** change the FSE project's target framework in this PR.

Keep:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
```

Why:

- the current fixed MSIX was built from the existing Windows-targeted FSE project;
- changing the project TFM does not reduce the current release once loose publishing is removed;
- rebuilding the fixed MSIX from a different TFM changes the packaged binary payload;
- changing packaged payload should follow the existing FSE component-version/hardware-validation policy.

The FseHome source currently only launches:

```text
steam://open/bigpicture
```

and may be a good future candidate for `net10.0`, but that optimization belongs to the **next intentional FSE artifact rebuild**, not this release-layout cleanup.

Do not create source/artifact drift merely for a change that provides no additional size benefit in PR-C.

---

## 5. Full1902 authority must remain untouched

This PR is packaging-only.

Do not modify:

- PID1901/PID1902 policy;
- controller startup;
- HidHide;
- DirectInput ownership;
- VIIPER;
- X360/SteamDeck presentation;
- Steam/BPM observation;
- sleep/hibernate/resume;
- PnP recovery;
- controller teardown;
- Center M state;
- Game Bar/OEM button ownership;
- Overlay capture;
- Windows App Runtime 2.5.1 setup.

FSE remains an independent optional Windows integration.

The current architecture remains:

```text
Windows FSE
→ FseHome packaged launcher
→ Steam BPM

Steam/BPM observation
→ existing Full1902 presentation policy
```

Do not introduce any FSE/controller handshake.

---

## 6. Current duplicate in `publish-layout.ps1`

Current code owns:

```powershell
$fseProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\SteamInputAddonforClaw.FseHome.csproj'
...
$fseArguments = @(
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'false',
    '/p:Version=1.0.0'
)
...
dotnet publish $fseProject @fseArguments '--output' $fseOutput
```

Then it separately copies:

```text
Packaging\Distribution\SteamInputAddonforClaw.FseHome.msix
Packaging\Distribution\SteamInputAddonforClaw.FseHome.cer
```

Delete only the normal-release FSE `dotnet publish` branch.

Keep:

```text
$fseDistribution
$fseOutput
directory creation
fixed MSIX/CER existence checks
fixed artifact copies
```

The intended shape is conceptually:

```powershell
$fseDistribution = ...
$fseOutput = Join-Path $runtimeOutput 'fse'

New-Item -ItemType Directory -Path $fseOutput -Force | Out-Null

foreach ($fseArtifact in @(
    'SteamInputAddonforClaw.FseHome.msix',
    'SteamInputAddonforClaw.FseHome.cer'
)) {
    $source = Join-Path $fseDistribution $fseArtifact
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Fixed FSE distribution artifact was not found: $source"
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $fseOutput $fseArtifact) -Force
}
```

Do not run any FSE package build/signing operation from normal publish.

---

## 7. Keep FseHome project in the solution

Do not delete:

```text
src/SteamInputAddonforClaw.FseHome/
SteamInputAddonforClaw.FseHome.csproj
Program.cs
Packaging/
```

Do not remove the FseHome project from:

```text
SteamInputAddonforClaw.slnx
```

Current CI and Release both build the solution before publish/pack, so ordinary source compilation still catches FseHome source errors.

Required lifecycle:

```text
normal solution build
→ FseHome source compiles

normal release publish
→ do NOT generate loose FseHome deployment files
→ copy prevalidated fixed MSIX/CER only
```

This preserves code health without shipping a second copy of the FSE application.

---

## 8. Manual FSE artifact builder remains separate

Keep:

```text
scripts/package-fse-home.ps1
```

as the feature-local manual artifact builder.

Do not invoke it from:

- `publish-layout.ps1`;
- `pack.ps1`;
- ordinary PR CI;
- ordinary Release.

Do not modify it merely because normal release no longer publishes loose FSE files.

When an actual FSE package rebuild is intentionally required later:

```text
explicit FseHome publish
→ package-fse-home.ps1
→ makepri
→ makeappx
→ sign
→ replace fixed MSIX/CER
→ bump fixed FSE component version when payload changes
→ update pinned hashes
→ perform FSE hardware/reboot validation
```

That lifecycle is outside PR-C.

---

## 9. Update publish verifier contract

Current verifier incorrectly requires both:

```text
fse\SteamInputAddonforClaw.FseHome.exe
fse\SteamInputAddonforClaw.FseHome.dll
```

and the fixed MSIX/CER.

Remove loose EXE/DLL from `$requiredAssets`.

Continue requiring:

```text
fse\SteamInputAddonforClaw.FseHome.msix
fse\SteamInputAddonforClaw.FseHome.cer
```

Keep all existing fixed artifact validation:

- SHA-256;
- certificate subject;
- ZIP readability;
- internal `AppxManifest.xml`;
- internal `CustomCapability.SCCD`;
- exact identity;
- `Application Id="App"`;
- `windows.gamingApp`;
- Gaming Home custom capability;
- x64 architecture;
- fixed version `1.0.0.0`.

These checks are the evidence that the actual FSE package being shipped remains valid.

---

## 10. Reject accidental loose FSE republishing

Add one small negative release-layout check.

Preferred contract:

```text
fse\ directory may contain only:
- SteamInputAddonforClaw.FseHome.msix
- SteamInputAddonforClaw.FseHome.cer
```

Example:

```powershell
$allowedFseFiles = @(
    'SteamInputAddonforClaw.FseHome.msix',
    'SteamInputAddonforClaw.FseHome.cer'
)

$unexpectedFseFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $PublishDirectory 'fse') -Recurse -File |
    Where-Object { $_.Name -notin $allowedFseFiles }
)

if ($unexpectedFseFiles.Count -gt 0) {
    throw "FSE publish directory contains redundant loose payload: $($unexpectedFseFiles.Name -join ', ')"
}
```

Equivalent simple implementation is acceptable.

This is preferable to maintaining a long blacklist for:

- `Microsoft.Windows.SDK.NET.dll`;
- `WinRT.Runtime.dll`;
- `.deps.json`;
- `.runtimeconfig.json`;
- FseHome EXE/DLL;
- future generated loose dependencies.

The FSE release contract is intentionally exactly two fixed files.

---

## 11. Update verifier regression fixtures

In:

```text
scripts/tests/verify-publish-assets.tests.ps1
```

the valid fixture must contain:

```text
fse\SteamInputAddonforClaw.FseHome.msix
fse\SteamInputAddonforClaw.FseHome.cer
```

and must no longer contain:

```text
fse\SteamInputAddonforClaw.FseHome.exe
fse\SteamInputAddonforClaw.FseHome.dll
```

Add a focused negative test proving that an unexpected loose FSE file is rejected.

At minimum:

```text
add fse\SteamInputAddonforClaw.FseHome.exe
→ verifier fails
→ message identifies redundant/unsupported FSE loose payload
```

One representative negative case is enough if the production check is whitelist-based.

Do not add tests for every possible DLL name.

---

## 12. Update FSE packaging contract test

Update:

```text
tests/SteamInputAddonforClaw.Tests/SteamFsePackagingContractTests.cs
```

to encode the current release contract.

Required assertions:

```text
publish-layout.ps1
→ still references Packaging\Distribution
→ still copies SteamInputAddonforClaw.FseHome.msix
→ still copies SteamInputAddonforClaw.FseHome.cer
→ does NOT dotnet publish the FseHome project
```

Prefer checking meaningful tokens such as:

```csharp
Assert.DoesNotContain("$fseProject", layout, StringComparison.Ordinal);
Assert.DoesNotContain("FSE Home publish failed", layout, StringComparison.Ordinal);
Assert.Contains("SteamInputAddonforClaw.FseHome.msix", layout, StringComparison.Ordinal);
Assert.Contains("SteamInputAddonforClaw.FseHome.cer", layout, StringComparison.Ordinal);
```

Exact test shape may be adjusted to avoid brittle source matching.

Keep existing package identity/registration tests.

Do not rewrite FSE functional tests for a packaging-only change.

---

## 13. Improve publish-size classification

Current:

```text
scripts/report-publish-size.ps1
```

does not classify `fse/` explicitly, so FSE files fall under:

```text
Other / Unclassified
```

Since PR-C specifically changes FSE payload, add a direct classification:

```powershell
if ($path.StartsWith('fse/')) { return 'FSE Home' }
```

and add:

```text
FSE Home
```

to the component byte table.

Update:

```text
scripts/tests/report-publish-size.tests.ps1
```

accordingly.

This is reporting only; do not make size reporting an authority over packaging.

---

## 14. Do not change `pack.ps1`, CI, or Release architecture unnecessarily

Current latest-main behavior already does the correct high-level separation:

```text
CI/Release
→ build solution
→ tests
→ publish-layout
→ verify publish assets
→ report size
→ package release
```

There is no current FSE makeappx/signing step in normal CI/Release.

Therefore PR-C should not rework:

```text
.github/workflows/ci.yml
.github/workflows/release.yml
scripts/pack.ps1
```

unless a minimal test expectation needs adjustment.

Do not reintroduce:

- FSE certificate generation;
- PFX secrets;
- makeappx in release;
- signtool in release;
- fixed package rebuild on every Addon release.

---

## 15. Files that must remain unchanged

Unless implementation proves an unavoidable compile-only adjustment, do not change:

```text
src/SteamInputAddonforClaw/WindowsGaming/SteamFseRegistration.cs
src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
src/SteamInputAddonforClaw.FseHome/Program.cs
src/SteamInputAddonforClaw.FseHome/SteamInputAddonforClaw.FseHome.csproj
src/SteamInputAddonforClaw.FseHome/Packaging/AppxManifest.xml
src/SteamInputAddonforClaw.FseHome/Packaging/CustomCapability.SCCD
src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/SteamInputAddonforClaw.FseHome.msix
src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/SteamInputAddonforClaw.FseHome.cer
scripts/package-fse-home.ps1
```

This list is deliberate.

PR-C should be demonstrably incapable of changing how FSE registers or boots Steam BPM.

---

## 16. Expected files to change

Primary expected changes:

```text
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
scripts/report-publish-size.ps1
scripts/tests/report-publish-size.tests.ps1
tests/SteamInputAddonforClaw.Tests/SteamFsePackagingContractTests.cs
```

No new production C# type should be necessary.

Do not create a new packaging abstraction.

---

## 17. Size measurement

Before implementation, run a clean Release publish on current main and record:

```text
Total
FSE Home / Other-Unclassified contribution
largest fse\ loose files
```

After implementation, run the same report.

Required evidence:

```text
before:
fse\ fixed MSIX/CER
+ loose FseHome publish payload

after:
fse\ fixed MSIX/CER only
```

Report exact:

- total bytes;
- total MiB;
- FSE Home bytes/MiB;
- total reduction bytes/MiB.

Do not hard-code an expected savings as a pass/fail threshold.

The success criterion is removal of all redundant loose FSE files while keeping the fixed MSIX/CER unchanged.

---

## 18. Functional proof without unnecessary FSE rebuild

Because the fixed FSE MSIX is not modified, hardware FSE requalification should not be required as a merge blocker for PR-C.

Required proof is structural:

1. published MSIX SHA remains exactly pinned;
2. published CER SHA remains exactly pinned;
3. verifier parses the same MSIX manifest;
4. registration code still points only to `fse\SteamInputAddonforClaw.FseHome.msix`;
5. source diff contains no FSE registration/runtime behavior change.

If convenient, one hardware smoke test may still be performed:

```text
existing/clean FSE registration
→ enable
→ Windows Gaming Home recognizes Steam Big Picture entry
→ reboot/FSE activation
→ Steam BPM opens
```

but do not rebuild the MSIX just to perform this validation.

---

## 19. Regression cases to protect

Required tests/inspection must protect these realistic cases.

### First FSE enable

```text
fixed MSIX/CER present
→ elevation
→ temporary cert/developer-mode flow
→ package registration
→ exact AUMID readback
→ ON
```

No code change expected.

### Existing registered FSE

```text
package already registered
→ no dependence on loose files
→ ON/OFF remains registry-only
```

### Addon update

```text
new Velopack Addon release
→ fixed FSE package files still shipped
→ registered package remains valid
→ no loose FSE payload needed
```

### Uninstall

```text
exact FSE package cleanup
→ still owned by package probe/remove logic
→ no dependence on loose files
```

### Fixed artifact missing

```text
verifier fails release
```

Do not add runtime fallback to loose FSE executable.

---

## 20. Explicit non-goals

Do not include:

- FseHome `net10.0` retarget;
- fixed MSIX rebuild;
- fixed package version bump;
- FSE signing changes;
- FSE manifest changes;
- FSE SCCD changes;
- certificate changes;
- Developer Mode changes;
- AUMID changes;
- FSE registration changes;
- Windows GamingConfiguration changes;
- FSE UI changes;
- Windows App SDK changes;
- `Microsoft.Windows.SDK.NET.dll` optimization for Runtime/UI/Overlay;
- C#/WinRT embedded projections;
- Full1902 controller changes;
- general release pipeline refactor.

---

## 21. Future follow-up — not PR-C

The FseHome source is currently only:

```text
System.Diagnostics.Process.Start
+ steam://open/bigpicture
```

so a future fixed-artifact rebuild may evaluate:

```xml
<TargetFramework>net10.0</TargetFramework>
```

instead of the current Windows TFM.

Microsoft documents that OS-specific TFMs expose OS-specific APIs while the base `net10.0` TFM contains the base .NET API surface, and that the OS version encoded in a TFM selects compile-time platform APIs rather than defining runtime OS support.

However, that change must be handled as an FSE artifact change because it changes the packaged managed payload.

Future work must therefore:

```text
retarget source
→ build new FSE package
→ bump fixed FSE component version
→ sign with FSE certificate
→ update MSIX/CER hashes if applicable
→ verify package registration
→ verify Windows FSE picker
→ reboot
→ verify packaged launcher opens Steam BPM
```

Do not fold that into PR-C.

Reference:

```text
https://learn.microsoft.com/dotnet/standard/frameworks
https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-behind-the-scenes
```

---

## 22. Validation commands

Run at minimum:

```text
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

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/report-publish-size.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/release-metadata.tests.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 \
  -Version 0.1.0 \
  -Configuration Release \
  -PublishDirectory artifacts/publish

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 \
  -PublishDirectory artifacts/publish

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/report-publish-size.ps1 \
  -PublishDirectory artifacts/publish

git diff --check
```

Also manually inspect:

```text
artifacts\publish\fse\
```

Expected exact file set:

```text
SteamInputAddonforClaw.FseHome.msix
SteamInputAddonforClaw.FseHome.cer
```

No other file should exist under the published `fse\` directory.

---

## 23. Acceptance criteria

PR-C is complete only when all are true.

1. `scripts/publish-layout.ps1` no longer calls `dotnet publish` for FseHome.
2. FseHome remains in the normal solution build.
3. Published `fse\` contains exactly MSIX + CER.
4. No loose FseHome EXE/DLL/deps/runtime/projection payload ships.
5. Fixed MSIX SHA remains `9D4C46ABCC1324803AE5AB031B11EC8EF39057D77C9C04FCB243D80BC122F86B`.
6. Fixed CER SHA remains `663053482DA50F9017CC902CA5DF6E9BBFD5A6F06624B8608266318F54687390`.
7. Fixed FSE version remains `1.0.0.0`.
8. Package identity/publisher/Application Id remain unchanged.
9. Manifest still declares `windows.gamingApp`.
10. SCCD still declares the Gaming Home custom capability.
11. Registration still reads only the fixed MSIX/CER installed paths.
12. AUMID still comes from actual registered `FamilyName + "!App"`.
13. FSE ON/OFF behavior is unchanged.
14. No FSE product/runtime C# code changes are introduced.
15. FseHome project TFM remains unchanged in this PR.
16. FSE package binaries are not rebuilt.
17. Publish verifier rejects any unexpected loose FSE payload.
18. Publish verifier still deeply validates fixed MSIX/CER.
19. Publish-size report classifies FSE explicitly.
20. Before/after raw publish size is recorded.
21. Full headless and UI test suites pass.
22. Release publish verification passes.
23. `git diff --check` is clean.

---

## 24. Final architecture after PR-C

```text
Repository
│
├─ FseHome source project
│    └─ compiled by normal solution build
│
├─ Packaging/Distribution
│    ├─ fixed validated FSE MSIX
│    └─ fixed public CER
│
└─ normal Addon publish
     │
     └─ fse\
          ├─ SteamInputAddonforClaw.FseHome.msix
          └─ SteamInputAddonforClaw.FseHome.cer
                    │
                    ▼
            first user FSE ON
                    │
                    ▼
         elevated package registration
                    │
                    ▼
          Windows Gaming Home / FSE
                    │
                    ▼
       packaged FseHome.exe from MSIX
                    │
                    ▼
        steam://open/bigpicture
                    │
                    ▼
                Steam BPM
```

The invariant is:

> **Ship the one package Windows actually registers. Do not also ship an unused loose copy of the same FSE application.**
