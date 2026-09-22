# Work Order — Audit UI/Overlay Windows ML Payload Duplication

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@8ad94f9316d1cd44b80cc9d994594aadcb1301c7`  
> **Date:** 2026-09-22  
> **Scope:** investigation/audit only — no implementation, no branch, no commit, no PR  
> **Product:** standalone Steam Addon for Claw, Full1902  
> **Primary question:** Why do framework-dependent WinUI UI/Overlay publishes still carry a private Windows ML runtime, and is there an officially supported way to stop shipping it?

---

## 1. Hard execution rule

This task is **audit-only**.

Do not:

- modify tracked product files;
- create a feature branch;
- create a commit;
- create a pull request;
- delete DLLs from the real publish output as a proposed fix;
- add post-publish deletion hacks;
- change Windows App SDK version;
- change Full1902/controller code;
- change Runtime provisioning logic;
- change UI/Overlay behavior.

You may:

- restore/build/publish current `main`;
- generate MSBuild binary logs;
- inspect `obj/project.assets.json`;
- inspect generated `.nuget.g.props` / `.nuget.g.targets`;
- inspect NuGet package contents in the local global package cache;
- run `dotnet list package --include-transitive`;
- preprocess MSBuild projects;
- create disposable test/repro projects **outside the repository**;
- use command-line MSBuild properties in disposable experiments;
- produce a local audit report.

At the end:

```text
git status --short
```

must be clean.

If anything modified the worktree during experimentation, restore it before reporting.

---

## 2. Why this audit exists

PR578/PR580 moved Main UI and Overlay to Windows App SDK framework-dependent deployment and the current projects explicitly contain:

```xml
<WindowsPackageType>None</WindowsPackageType>
<UseWinUI>true</UseWinUI>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
```

Projects:

```text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
```

The Addon already provisions/accepts a shared Windows App Runtime 2.5.1+ before launching these WinUI processes.

Yet current Release publish still contains in **both** UI and Overlay:

```text
Microsoft.Windows.AI.MachineLearning.dll
onnxruntime.dll
DirectML.dll
```

Neither UI nor Overlay source currently appears to use ONNX Runtime, DirectML, or Windows ML APIs directly.

This audit must establish exactly why these files are copied.

Do not assume they are safe to remove merely because source search finds no direct API call.

---

## 3. Current measured baseline

Latest directly comparable successful publish after PR581:

```text
Total:
213,134,969 bytes
203.26 MiB

Runtime:
33,479,952 bytes
31.93 MiB

UI:
84,426,760 bytes
80.52 MiB

Overlay:
82,519,904 bytes
78.70 MiB

QAM Host:
904,589 bytes
0.86 MiB

FSE Home:
6,937,863 bytes
6.62 MiB
```

Largest relevant files:

```text
ui/Microsoft.Windows.SDK.NET.dll               26,341,408 bytes  25.12 MiB
overlay/Microsoft.Windows.SDK.NET.dll          26,341,408 bytes  25.12 MiB

ui/onnxruntime.dll                             21,659,280 bytes  20.66 MiB
overlay/onnxruntime.dll                        21,659,280 bytes  20.66 MiB

ui/DirectML.dll                                18,700,224 bytes  17.83 MiB
overlay/DirectML.dll                           18,700,224 bytes  17.83 MiB

ui/Microsoft.Windows.AI.MachineLearning.dll       903,464 bytes   0.86 MiB
overlay/Microsoft.Windows.AI.MachineLearning.dll  903,464 bytes   0.86 MiB
```

Windows ML payload per process:

```text
21,659,280
+18,700,224
+   903,464
-----------
41,262,968 bytes
39.35 MiB
```

Across UI + Overlay:

```text
82,525,936 bytes
78.70 MiB raw
```

This is the main optimization candidate.

Do not confuse this with the separate `Microsoft.Windows.SDK.NET.dll` work.

---

## 4. Official Microsoft deployment model to verify against

Current Microsoft documentation states that Windows ML Runtime contains approximately:

```text
Microsoft.Windows.AI.MachineLearning.dll  ~1 MB
onnxruntime.dll                            ~20 MB
DirectML.dll                              ~20 MB
-----------------------------------------------
total                                     ~41 MB
```

Microsoft documents two deployment modes.

### Self-contained

```text
Windows ML Runtime binaries
→ shipped privately with the app
→ app grows by ~41 MB
```

### Framework-dependent

```text
Windows ML Runtime
→ supplied by installed Windows App SDK Runtime
→ shared system-wide
→ app does not need its own ~41 MB runtime copy
```

Microsoft currently documents for C# framework-dependent Windows ML:

```text
Microsoft.WindowsAppSDK.ML
+
Microsoft.WindowsAppSDK.Runtime
```

or alternatively the main:

```text
Microsoft.WindowsAppSDK
```

package where supported, with:

```xml
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
```

Current Microsoft references:

```text
https://learn.microsoft.com/windows/ai/new-windows-ml/distributing-your-app
https://learn.microsoft.com/windows/ai/new-windows-ml/onnx-versions
https://learn.microsoft.com/windows/apps/windows-app-sdk/deployment-architecture
https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
```

As of this audit baseline, Windows App SDK 2.5.1 is the current stable release.

Do not rely solely on generic documentation.

The audit must also inspect the actual NuGet/MSBuild behavior of **2.5.1** used by this repository.

---

## 5. Key question

Explain precisely why this repository currently gets:

```text
WindowsAppSDKSelfContained=false
```

but still publishes:

```text
Microsoft.Windows.AI.MachineLearning.dll
onnxruntime.dll
DirectML.dll
```

into each WinUI process directory.

Possible explanations include, but are not limited to:

- transitive Windows ML package defaults;
- a Windows App SDK 2.5.1 package target copying runtime assets regardless of the top-level property;
- different self-contained properties used by the ML package;
- a package graph that includes a self-contained ML package rather than a framework-dependent ML runtime reference;
- `CopyToOutputDirectory` / `CopyLocal` metadata inherited from a transitive package;
- WinUI/Windows App SDK feature package composition;
- a framework-dependent deployment bug/regression;
- repository-specific MSBuild property ordering;
- command-line publish properties overriding/evaluating too late;
- another transitive package unrelated to the main Windows App SDK package.

Do not select a hypothesis without evidence.

---

## 6. First pass — source/project audit

Confirm current source facts.

Inspect:

```text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
```

Search production source for:

```text
Microsoft.Windows.AI
Microsoft.ML.OnnxRuntime
onnxruntime
DirectML
ExecutionProvider
LearningModel
Windows ML
```

Report exact matches.

Differentiate:

```text
direct product API usage
vs
package/build metadata only
vs
documentation/test text
```

Do not conclude a runtime DLL is unused from source search alone.

---

## 7. Restore and package dependency graph

From a clean current-main tree:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet restore tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
```

For both:

```text
SteamInputAddonforClaw.UI.csproj
SteamInputAddonforClaw.Overlay.csproj
```

run:

```text
dotnet list <project> package --include-transitive
```

Record the full chain involving any of:

```text
Microsoft.WindowsAppSDK
Microsoft.WindowsAppSDK.Runtime
Microsoft.WindowsAppSDK.ML
Microsoft.Windows.AI.MachineLearning
Microsoft.ML.OnnxRuntime*
DirectML
Windows AI packages
```

Identify:

- which package introduces each dependency;
- direct vs transitive;
- exact package versions;
- whether UI and Overlay graphs are identical;
- whether CommunityToolkit in UI changes the graph relative to Overlay.

Overlay is especially useful because it has no CommunityToolkit package reference.

---

## 8. Inspect project.assets.json

Inspect:

```text
src/SteamInputAddonforClaw.UI/obj/project.assets.json
src/SteamInputAddonforClaw.Overlay/obj/project.assets.json
```

Find entries for:

```text
Microsoft.Windows.AI.MachineLearning.dll
onnxruntime.dll
DirectML.dll
```

For each file, identify:

- owning NuGet package;
- package version;
- asset category:
  - runtime;
  - native;
  - content;
  - build;
  - buildTransitive;
- RID-specific source path;
- whether it is represented as a runtime target;
- whether it is marked for copy-local/publish.

Produce an evidence table:

```text
Published file | Owning package | Package path | Assets category | RID | Direct/transitive origin
```

Do not infer package ownership merely from filename.

---

## 9. Inspect generated NuGet MSBuild files

Inspect per project:

```text
obj/<project>.csproj.nuget.g.props
obj/<project>.csproj.nuget.g.targets
```

Trace imported package `.props` / `.targets` that can influence ML deployment.

Then inspect the actual files under the NuGet global packages folder.

Find all targets containing relevant terms:

```text
WindowsAppSDKSelfContained
MachineLearning
WindowsML
onnxruntime
DirectML
CopyToOutputDirectory
CopyToPublishDirectory
ResolvedFileToPublish
RuntimeCopyLocalItems
ReferenceCopyLocalPaths
ContentWithTargetPath
```

Identify the exact MSBuild target/item that injects each ML runtime DLL into output/publish.

This is one of the primary audit deliverables.

---

## 10. Produce Release publishes with binary logs

Publish UI directly with a binary log using the same effective settings as normal release.

Example:

```text
dotnet publish src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -bl:artifacts/audit/ui-publish.binlog \
  -o artifacts/audit/ui
```

Do the same for Overlay:

```text
dotnet publish src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -bl:artifacts/audit/overlay-publish.binlog \
  -o artifacts/audit/overlay
```

From the binary log, trace:

```text
onnxruntime.dll
DirectML.dll
Microsoft.Windows.AI.MachineLearning.dll
```

back to the target/item responsible for copying it.

Required evidence:

```text
MSBuild target
item type
source path
destination path
relevant metadata
condition that evaluated true
property/properties controlling it
```

If no binlog viewer is installed, use available MSBuild structured-log tooling or a diagnostic text log.

Do not install unrelated permanent developer tools merely for this audit unless necessary.

---

## 11. Preprocessed project / evaluated properties

Generate/evaluate enough MSBuild state to answer:

```text
WindowsAppSDKSelfContained
SelfContained
WindowsPackageType
UseWinUI
RuntimeIdentifier
TargetFramework
```

and any ML-specific deployment/self-contained properties discovered in package targets.

Use:

```text
dotnet msbuild <project> /pp:<temp-file>
```

or equivalent evaluated-property tooling.

Do not commit preprocessed output.

If the ML package has a distinct property controlling self-contained/framework-dependent behavior, report:

- property name;
- default;
- evaluated value;
- source of the value;
- target condition.

---

## 12. Check whether UI and Overlay are actually framework-dependent at publish time

Do not rely only on project source.

Prove the effective Release publish sees:

```text
WindowsAppSDKSelfContained=false
SelfContained=false
RuntimeIdentifier=win-x64
```

for UI and Overlay.

Also inspect publish output for the known self-contained Windows App SDK payload that PR578 already rejects.

Current verifier intentionally rejects files such as:

```text
Microsoft.WindowsAppRuntime.dll
Microsoft.UI.dll
Microsoft.UI.Xaml.winmd
...
```

Confirm those remain absent.

This establishes whether:

```text
WinUI framework deployment = correctly framework-dependent
but
Windows ML deployment = unexpectedly private
```

or whether the entire deployment mode is mis-evaluated.

---

## 13. Compare actual installed Windows App Runtime 2.5.1 contents

On the audit machine, inspect the registered Windows App Runtime 2.5.1 framework package if available.

Determine whether it contains shared equivalents of:

```text
Microsoft.Windows.AI.MachineLearning.dll
onnxruntime.dll
DirectML.dll
```

Record:

- package family/full name;
- architecture;
- version;
- physical package location;
- matching file names and versions/hashes where practical.

Do not mutate or unregister the package.

If the local environment does not have the required runtime, report that limitation instead of changing system state unnecessarily.

---

## 14. Version/hash comparison

For each privately published DLL:

```text
ui/<file>
overlay/<file>
```

compare:

- size;
- file version;
- product version;
- SHA-256.

Confirm whether UI and Overlay copies are byte-identical.

If matching system-shared Windows App Runtime files exist, compare them too.

This helps distinguish:

```text
redundant duplicate copy of the shared runtime
```

from:

```text
a project-specific ML runtime version that cannot be satisfied by installed framework
```

Do not claim safe substitution solely from matching file names.

---

## 15. Minimal reproduction outside repository

Create a disposable minimal WinUI project outside the repository using the same essential settings:

```text
net10.0-windows10.0.26100.0
WindowsPackageType=None
UseWinUI=true
RuntimeIdentifier=win-x64
SelfContained=false
WindowsAppSDKSelfContained=false
Microsoft.WindowsAppSDK 2.5.1
```

Do not add CommunityToolkit.

Publish it.

Answer:

```text
Does a clean minimal Microsoft.WindowsAppSDK 2.5.1 framework-dependent WinUI app also receive:
- onnxruntime.dll
- DirectML.dll
- Microsoft.Windows.AI.MachineLearning.dll
?
```

Interpretation:

### If YES

The behavior likely belongs to Windows App SDK/NuGet packaging rather than this repository.

### If NO

The repository has an additional dependency/property/target causing it.

Delete the disposable project after collecting evidence.

---

## 16. Controlled property experiments outside the repository

If package targets reveal an official documented property or package combination affecting Windows ML deployment, test it only in:

- the disposable minimal repro; or
- a temporary copy of the UI/Overlay project outside the repo.

Do not edit tracked product csproj files.

For each candidate configuration, record:

```text
Build PASS/FAIL
Publish PASS/FAIL
ML files present/absent
WinUI files present/absent
bootstrap/runtime dependency behavior
output size
```

Only test official or package-defined switches discovered from actual targets/documentation.

Do not invent unsupported MSBuild metadata hacks.

---

## 17. Explicitly evaluate the documented framework-dependent Windows ML path

Microsoft currently documents a framework-dependent C# Windows ML path using:

```text
Microsoft.WindowsAppSDK.ML
+
Microsoft.WindowsAppSDK.Runtime
```

and also documents the main `Microsoft.WindowsAppSDK` package as an alternative for current releases.

Audit whether:

```text
Microsoft.WindowsAppSDK 2.5.1
```

is expected to establish that same framework-dependent ML relationship automatically.

Determine from actual package metadata/targets:

- whether it references `Microsoft.WindowsAppSDK.ML`;
- whether it references `Microsoft.WindowsAppSDK.Runtime`;
- whether it references `Microsoft.Windows.AI.MachineLearning`;
- whether the ML package is defaulting to self-contained despite the parent app being framework-dependent.

Report exact evidence.

---

## 18. Do not assume Windows ML is unused

Even though source search currently shows no direct ML calls, the Windows App SDK/WinUI runtime could theoretically load or depend on part of the AI/ML package indirectly.

Audit before recommending removal.

Check:

- managed assembly references;
- import tables where useful;
- deps.json;
- runtimeconfig/dependency context;
- package graph;
- Windows App SDK package targets;
- official documentation.

The question is not:

> Do we call ONNX APIs?

The question is:

> Does the supported WinUI/Windows App SDK runtime contract require these private files in this deployment mode?

---

## 19. deps.json inspection

Inspect:

```text
ui/SteamInputAddonforClaw.UI.deps.json
overlay/SteamInputAddonforClaw.Overlay.deps.json
```

Determine whether ML/ONNX packages are represented as runtime dependencies.

Record exact package/library entries related to:

```text
Microsoft.Windows.AI.MachineLearning
Microsoft.WindowsAppSDK.ML
Microsoft.ML.OnnxRuntime
DirectML
```

Distinguish:

```text
dependency declared in deps.json
vs
native/content asset copied only by MSBuild target
```

This distinction matters for removal options.

---

## 20. Audit Windows App SDK runtime provisioning compatibility

Current Runtime prerequisite logic accepts:

```text
Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe
x64
version >= 2.5.1.0
```

Do not change it.

Determine whether the current official Windows App Runtime installer used by this project actually installs package(s) required for framework-dependent Windows ML.

Answer specifically:

```text
Does the currently provisioned Windows App Runtime 2.5.1 satisfy the documented shared Windows ML runtime dependency?
```

If additional package families are required beyond what the current installer installs, identify them.

Do not install new dependencies during this audit unless a disposable environment makes it safe and it is necessary to confirm package contents.

---

## 21. Candidate solution categories to classify

At the end, classify realistic options without implementing them.

### A. Already-supported property/configuration correction

Example category:

```text
current package supports framework-dependent ML
but one project/property is misconfigured
```

If proven, identify the exact minimal configuration change.

### B. Supported package composition change

Example category:

```text
replace broad package relationship with Microsoft-supported component/runtime package combination
```

Only recommend if package metadata/docs prove WinUI requirements remain supported.

Do not invent package names or assume component packages are interchangeable.

### C. Windows App SDK 2.5.1 packaging bug/regression

If minimal repro reproduces the issue despite correct documented settings, identify:

- evidence;
- likely package target involved;
- whether another currently supported release behaves differently if easy to test in disposable repro;
- relevant upstream issue/search result if found.

Do not upgrade the product as part of this audit.

### D. Files are genuinely required app-local

If the official deployment contract requires these files privately for this exact configuration, say so.

### E. Unsupported manual exclusion

If the only path is:

```text
Remove-Item DirectML.dll / onnxruntime.dll after publish
ExcludeAssets hack
custom target mutating publish output
```

without official support, classify it as unsupported/high-risk.

---

## 22. Size scenarios to calculate

Calculate raw publish size for these hypothetical outcomes.

### Scenario 0 — current

```text
Total = ~203.26 MiB
private Windows ML across UI+Overlay = ~78.70 MiB
```

### Scenario 1 — remove private Windows ML from both

Calculate:

```text
current total
- exact six ML file sizes
```

Use exact artifacts when available.

### Scenario 2 — only DirectML becomes removable

Calculate:

```text
2 × DirectML.dll
```

Microsoft documents a DirectML exclusion technique for **self-contained Windows ML** but explicitly warns that technique is not officially supported and depends on internal package layout.

That workaround is not automatically valid here.

Use it only as a size reference unless supported configuration evidence proves otherwise.

### Scenario 3 — only one surface can use shared ML

Calculate UI and Overlay separately if their package graphs differ.

Estimate full nupkg/Setup reduction only if a normal local pack measurement is cheap.

Do not present compressed estimates as exact.

---

## 23. Validate future startup semantics

Current architecture:

```text
headless Runtime
→ independent of Windows App Runtime

explicit UI/Overlay request
→ Runtime proves Windows App Runtime 2.5.1+
→ only then launches WinUI process
```

Any future shared Windows ML solution must preserve:

```text
no WinUI process starts before the shared Windows App Runtime dependency is Ready
```

The audit should say whether the current prerequisite probe is sufficient for the discovered ML dependency.

Do not design a second Windows ML prerequisite installer unless evidence proves the existing Windows App Runtime 2.5.1 installer does not carry the necessary runtime.

---

## 24. Full1902 boundaries

This investigation is UI deployment only.

Do not touch or redesign:

- Center M authority;
- PID1901/PID1902 transitions;
- HidHide;
- DirectInput;
- VIIPER;
- controller presentation;
- sleep/hibernate/resume;
- PnP recovery;
- rumble;
- FSE;
- gyro/sensors;
- controller shutdown/teardown.

If a future ML payload change only affects UI/Overlay process launch, it should remain presentation-local.

Do not add a controller prerequisite because of UI package optimization.

---

## 25. No dedup/shared-folder workaround

Do not recommend:

```text
common\onnxruntime.dll
common\DirectML.dll
common assembly/native probing path
symlink
hardlink
junction
PATH mutation
custom DLL search path
SetDllDirectory
AddDllDirectory
copy-on-first-run
```

merely to share the two private copies.

The goal is to use Microsoft's supported shared runtime if available, not create another app-owned dependency loader architecture.

---

## 26. No post-publish delete as final answer

Do not report success because:

```text
publish
→ Remove-Item onnxruntime.dll
→ UI happened to start
```

A successful manual deletion can be diagnostic evidence only.

For a production recommendation, require one of:

- documented supported framework-dependent behavior;
- explicit package target/property contract;
- verified official component-package deployment model.

If manual deletion is tested in a disposable copy, clearly label it unsupported diagnostic evidence.

---

## 27. Optional launch smoke in disposable output

If safe and useful, launch disposable audit copies of UI/Overlay only after the normal Runtime/precondition environment is available.

Check for:

- immediate process crash;
- `DllNotFoundException`;
- `FileNotFoundException`;
- WinRT activation failure;
- bootstrap failure.

Do not treat a short launch as proof that every WinUI feature is safe.

This is secondary to package/MSBuild evidence.

---

## 28. Audit report structure

Produce a report with these sections.

### Executive conclusion

Use one of:

```text
SUPPORTED OPTIMIZATION PATH FOUND
SUPPORTED BUT REQUIRES VERSION/PACKAGE CHANGE
UPSTREAM/PACKAGING ISSUE SUSPECTED
PRIVATE ML RUNTIME APPEARS REQUIRED
INCONCLUSIVE
```

Do not use a numeric score.

### Current dependency chain

Exact direct/transitive packages.

### Exact copy owner

For each of the three DLLs:

```text
package
target
item
condition
source
destination
```

### Effective deployment properties

UI and Overlay.

### Minimal repro result

Does clean WASDK 2.5.1 reproduce?

### System runtime comparison

Does installed Windows App Runtime provide matching shared ML payload?

### Supported options

Evidence-based only.

### Unsupported/hacky options rejected

List them explicitly.

### Exact size opportunity

Raw bytes/MiB.

### Recommended next PoC

If a supported path exists, define the **smallest possible future PoC**, but do not implement it.

---

## 29. Preferred local report output

Create a local audit report:

```text
docs/research/WINDOWS_ML_UI_OVERLAY_PAYLOAD_AUDIT_2026-09-22.md
```

If `docs/research` is not an existing convention, use:

```text
docs/WINDOWS_ML_UI_OVERLAY_PAYLOAD_AUDIT_2026-09-22.md
```

The report may remain uncommitted.

Do **not** create a commit or PR.

If the local Codex environment prefers chat-only output, that is acceptable, but preserve all requested evidence.

---

## 30. Evidence commands to run

At minimum:

```text
git status --short

dotnet restore SteamInputAddonforClaw.slnx

dotnet list src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj package --include-transitive
dotnet list src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj package --include-transitive

dotnet publish src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj \
  -c Release -r win-x64 --self-contained false \
  -o artifacts/audit/ui \
  -bl:artifacts/audit/ui-publish.binlog

dotnet publish src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj \
  -c Release -r win-x64 --self-contained false \
  -o artifacts/audit/overlay \
  -bl:artifacts/audit/overlay-publish.binlog
```

Run the repository real layout once:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass \
  -File scripts/publish-layout.ps1 \
  -Version 0.1.0 \
  -Configuration Release \
  -PublishDirectory artifacts/audit/full
```

Then:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass \
  -File scripts/report-publish-size.ps1 \
  -PublishDirectory artifacts/audit/full
```

Inspect hashes:

```powershell
Get-FileHash artifacts/audit/ui/onnxruntime.dll -Algorithm SHA256
Get-FileHash artifacts/audit/overlay/onnxruntime.dll -Algorithm SHA256

Get-FileHash artifacts/audit/ui/DirectML.dll -Algorithm SHA256
Get-FileHash artifacts/audit/overlay/DirectML.dll -Algorithm SHA256

Get-FileHash artifacts/audit/ui/Microsoft.Windows.AI.MachineLearning.dll -Algorithm SHA256
Get-FileHash artifacts/audit/overlay/Microsoft.Windows.AI.MachineLearning.dll -Algorithm SHA256
```

Use equivalent commands if paths differ.

---

## 31. Useful NuGet-cache searches

Find the global package folder:

```text
dotnet nuget locals global-packages --list
```

Search relevant package `.props`, `.targets`, and `.nuspec` files for:

```text
onnxruntime.dll
DirectML.dll
Microsoft.Windows.AI.MachineLearning.dll
WindowsAppSDKSelfContained
SelfContained
MachineLearning
WindowsML
CopyToOutputDirectory
CopyToPublishDirectory
RuntimeCopyLocalItems
ResolvedFileToPublish
```

Record exact package file and target context.

Do not edit global NuGet packages.

---

## 32. Optional clean-cache confirmation

If results look inconsistent because old package state may be involved, use an isolated NuGet packages directory for a disposable restore rather than deleting the developer's global cache.

Example concept:

```text
NUGET_PACKAGES=<temporary directory>
dotnet restore ...
```

This can prove the behavior is not stale-cache contamination.

Do not destroy the user's normal NuGet cache.

---

## 33. Expected decision value

This audit is worth doing because the maximum raw opportunity is approximately:

```text
78.70 MiB
```

across UI and Overlay.

However, size alone is not enough.

A future implementation is justified only if the audit identifies a supported/simple deployment contract such as:

```text
small csproj/package-property correction
→ framework-dependent shared Windows ML
→ existing Windows App Runtime prerequisite remains authoritative
```

If achieving the reduction requires:

```text
custom native DLL probing
manual publish deletion
shared app-owned runtime directory
new bootstrapper
new installer authority
```

then recommend no change.

---

## 34. Final stop condition

The task ends with the audit report.

Do not implement the recommended solution.

Do not create a PR.

Do not opportunistically fix unrelated warnings.

Do not modify product code.

Required final state:

```text
repository tracked worktree clean
+
evidence-based audit report
+
clear recommendation for or against a future PoC
```

---

## 35. Final principle

> **First prove why Windows ML is private despite framework-dependent Windows App SDK. Only then decide whether there is a supported 78.70 MiB optimization.**
