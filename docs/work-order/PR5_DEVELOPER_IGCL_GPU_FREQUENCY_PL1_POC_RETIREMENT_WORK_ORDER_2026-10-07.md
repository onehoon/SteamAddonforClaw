# Work Order — PR5: Retire Developer IGCL GPU Frequency / PL1 PoC

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@b9077a4cf505235c4bdda77fa7fbcacce6f90455` (PR #706 merged)  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the active Full1902 architecture chain  
> **Current Frontend protocol:** `FrontendTransportProtocol.CurrentVersion = 64`  
> **Overlay protocol:** unchanged by this PR  
> **Scope:** remove the completed Developer-only Intel IGCL GPU Frequency / PL1 proof-of-concept, remove its frontend/RPC/UI/native implementation and tests, and remove the production Minimum GPU Clock conflict guards that existed only because the PoC was a competing frequency writer  
> **Out of scope:** production Minimum GPU Clock behavior changes, production IGCL file split/refactor, Intel FPS limiter changes, production GPU PL1/TDP support, controller architecture, CTW integration

---

## 1. Goal

Retire the completed Developer-only Intel IGCL GPU Frequency / Power Limit PoC.

The original PoC answered the required hardware questions:

~~~text
official IGCL GPU frequency domain can be discovered
frequency range can be read/written/read back
Max/Max can be tested
original test range can be restored during the diagnostic lifetime

IGCL GPU power domain can be discovered
PL1 can be test-written/read back
original test power limits can be restored during the diagnostic lifetime
~~~

Production Minimum GPU Clock now exists and PR #706 established its final product policy:

~~~text
active enabled Steam/XBOX game
+ Minimum GPU Clock enabled
    → apply the game's AC/DC minimum-frequency target

otherwise
    → min = -1
    → Intel factory minimum-frequency policy
~~~

The Developer PoC is no longer needed.

After this PR there must be no second developer-owned Intel frequency/power writer competing with production Minimum GPU Clock.

The desired ownership is:

~~~text
Production game Minimum GPU Clock
    → IntelGpuMinimumClockRuntime
    → IntelGpuMinimumClockControl
    → IGCL minimum-frequency SetRange

Developer IGCL frequency / PL1 writer
    → REMOVED
~~~

---

## 2. Why this cleanup should happen now

The current tree still carries two independent IGCL mutation stacks.

### Production

~~~text
Profiles/Performance/IntelGpuMinimumClock.cs
    IntelGpuMinimumClockRuntime
    IntelGpuMinimumClockControl
    production game-only minimum-frequency policy
~~~

### Developer PoC

~~~text
Diagnostics/IntelGpuIgclProbe.cs
    separate lazy ControlLib.dll session
    separate Intel adapter discovery
    separate frequency-domain discovery
    Max/Max write + restore
    frequency state read
    separate power-domain discovery
    PL1 write + restore
~~~

Because both can write the frequency range, production code also carries conflict machinery:

~~~text
InProcessAddonFrontendControl
    _developerGpuFrequencyProbeModified
    ShouldBlockDeveloperFrequencyMutation(...)

IntelGpuMinimumClockRuntime
    BlocksDeveloperFrequencyMutation()

Steam/XBOX profile enable/mutation paths
    refuse production GPU-floor changes while Developer probe owns a changed range
~~~

That complexity protects only a temporary PoC.

Retiring the PoC lets the product return to one clear production frequency owner.

---

## 3. Historical documents remain historical

Do **not** delete or rewrite these historical work orders:

~~~text
docs/work-order/IGCL_GPU_FREQUENCY_DEVELOPER_POC_WORK_ORDER_2026-10-06.md
docs/work-order/DEVELOPER_DIAGNOSTIC_LAZY_MATERIALIZATION_WORK_ORDER_2026-10-06.md
docs/work-order/PR1_INTEL_MINIMUM_GPU_CLOCK_PRODUCTION_CORE_WORK_ORDER_2026-10-07.md
docs/work-order/PR2_DEVICE_MINIMUM_GPU_CLOCK_PERSISTENCE_UI_LIFECYCLE_WORK_ORDER_2026-10-07.md
docs/work-order/PR3_GAME_PROFILE_MINIMUM_GPU_CLOCK_OVERRIDE_WORK_ORDER_2026-10-07.md
docs/work-order/PR4_GAME_ONLY_MINIMUM_GPU_CLOCK_FACTORY_RELEASE_POLICY_CORRECTION_WORK_ORDER_2026-10-07.md
~~~

They document how the feature was proven and evolved.

This PR supersedes only the **live Developer PoC implementation**, not repository history.

---

## 4. Delete the Developer IGCL probe implementation

Delete:

~~~text
src/SteamInputAddonforClaw/Diagnostics/IntelGpuIgclProbe.cs
~~~

This removes the PoC-only types and native ABI surface, including conceptually:

~~~text
IntelGpuIgclProbe
IntelGpuIgclProbePolicy
IntelGpuPowerLimits
IntelGpuPowerCapability
IgclProbeException

probe-local IntelGpuFrequencyRange

lazy diagnostic ControlLib.dll load
ctlInit / ctlClose
ctlEnumerateDevices
ctlGetDeviceProperties

ctlEnumFrequencyDomains
ctlFrequencyGetProperties
ctlFrequencyGetRange
ctlFrequencySetRange
ctlFrequencyGetState

ctlEnumPowerDomains
ctlPowerGetProperties
ctlPowerGetLimits
ctlPowerSetLimits

SetMaxMax
RestoreOriginalFrequency
SetTestPl1
RestoreOriginalPower

FrequencyModifiedByProbe
PowerModifiedByProbe
probe-owned original range/power-limit capture
probe shutdown restore
~~~

Do not move any of this PoC implementation into another Developer helper.

Do not retain a read-only half of the probe.

The evidence-gathering feature is complete; remove the whole diagnostic.

---

## 5. Do not remove production IGCL paths

This PR must **not** remove or rewrite the production Intel paths.

Keep:

~~~text
Profiles/Performance/IntelGpuMinimumClock.cs
    IntelGpuMinimumClockPolicy
    IntelGpuMinimumClockRuntime
    IntelGpuMinimumClockControl

Profiles/Performance/IntelFrameLimiter.cs
    existing production Intel FPS IGCL implementation
~~~

Both production features may continue loading the driver-supplied:

~~~text
%SystemRoot%\System32\ControlLib.dll
~~~

The fact that `IntelGpuIgclProbe.cs` also used ControlLib does not make ControlLib itself diagnostic-only.

Do not perform the planned production Minimum GPU Clock file split in this PR.

That is the next cleanup after this retirement.

---

## 6. Delete the Developer probe frontend contract

Delete:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/IntelGpuFrequencyProbeContracts.cs
~~~

This removes:

~~~text
FrontendIntelGpuFrequencyProbeOperation
FrontendIntelGpuFrequencyProbeSnapshot
~~~

Remove the corresponding methods from `IAddonFrontendControl`:

~~~text
CaptureIntelGpuFrequencyProbeAsync(...)
RunIntelGpuFrequencyProbeAsync(...)
~~~

Do not leave default Unavailable implementations as compatibility shims.

The app is pre-release and the frontend protocol is closed/versioned.

---

## 7. Remove Developer probe RPCs

From:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
~~~

remove live RPC members:

~~~text
FrontendRpcMethod.CaptureIntelGpuFrequencyProbe
FrontendRpcMethod.RunIntelGpuFrequencyProbe
~~~

Remove the request payload:

~~~text
RunIntelGpuFrequencyProbeRequest
~~~

Remove corresponding dispatch/client code from:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
~~~

Do not leave unsupported forwarding or tombstone RPC methods.

### 7.1 Protocol version

Bump:

~~~text
FrontendTransportProtocol.CurrentVersion
64 → 65
~~~

Keep the existing historical Version 57 comment that records when the Developer IGCL probe was introduced.

Add a new Version 65 comment, conceptually:

~~~text
Version 65: retire the completed Developer-only Intel IGCL GPU Frequency / PL1 probe,
including its snapshot, operations, and capture/run RPCs. Production game-profile
Minimum GPU Clock remains unchanged.
~~~

Do not bump the Overlay protocol.

The probe was Main Developer UI only.

---

## 8. Remove the Developer UI card completely

From:

~~~text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
~~~

remove the entire:

~~~text
Intel GPU Frequency / IGCL Probe
~~~

card, including:

~~~text
Refresh
Set Max / Max + Readback
Restore Original Frequency
PL1 NumberBox
Set Test PL1 + Readback
Restore Original Power
details/status text
~~~

From:

~~~text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
~~~

remove all probe state and handlers, including conceptually:

~~~text
_isRunningIntelGpuFrequencyProbe
_intelGpuFrequencyProbeSnapshot

RenderIntelGpuFrequencyProbe()
UpdateIntelGpuFrequencyProbeButtons()
RunIntelGpuFrequencyProbeOperationAsync()
TryGetTestPl1()

RefreshIntelGpuFrequencyProbeButton_Click
SetIntelGpuMaxMaxButton_Click
RestoreIntelGpuFrequencyButton_Click
SetIntelGpuTestPl1Button_Click
RestoreIntelGpuPowerButton_Click
IntelGpuTestPl1NumberBox_ValueChanged

FormatIntelGpuFrequencyProbeStatus()
FormatExternalRange()
FormatVoltage()
FormatMw()
FormatEnabledLimit()
FormatThrottleReasons()
Verification()
FormatResult()
YesNo()
~~~

Also remove `RenderIntelGpuFrequencyProbe()` from Developer-page initialization.

Remove now-unused `using` directives such as `System.Globalization` / `System.Text` only if they are no longer used elsewhere in that file.

Do not redesign or reorganize the rest of DeveloperPage in this PR.

Other developer diagnostics remain unchanged.

---

## 9. Remove the probe lifetime from InProcessAddonFrontendControl

From:

~~~text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

remove:

~~~text
private IntelGpuIgclProbe? _intelGpuFrequencyProbe;
~~~

Remove lazy materialization:

~~~text
_intelGpuFrequencyProbe ??= new IntelGpuIgclProbe()
~~~

Remove:

~~~text
CaptureIntelGpuFrequencyProbeAsync(...)
RunIntelGpuFrequencyProbeAsync(...)
~~~

Remove shutdown cleanup:

~~~text
if (_intelGpuFrequencyProbe is not null)
{
    _intelGpuFrequencyProbe.Dispose();
}
~~~

This does not weaken production shutdown safety because the removed object is the diagnostic itself.

Do not replace it with another diagnostic/session owner.

---

## 10. Remove production conflict machinery that existed only for the PoC

This is required cleanup, not an optional refactor.

### 10.1 InProcessAddonFrontendControl

Remove:

~~~text
private readonly Func<bool> _developerGpuFrequencyProbeModified;
~~~

Remove constructor parameter:

~~~text
Func<bool>? developerGpuFrequencyProbeModified = null
~~~

Remove assignment:

~~~text
_developerGpuFrequencyProbeModified = ...
~~~

Remove:

~~~text
ShouldBlockDeveloperFrequencyMutation(...)
~~~

Remove all Steam/XBOX production mutation gates whose only purpose is to coexist with the probe.

Current examples include:

~~~text
SetGameProfileEnabledAsync(...)
    if enabling a profile whose GPU Min is enabled
    AND Developer probe modified frequency
    → Unavailable

MutateGameGpuMinimumClock(...)
    if Developer probe modified frequency
    → Unavailable

SetXboxGameProfileEnabledAsync(...)
    equivalent Developer-probe gate

MutateXboxGameGpuMinimumClock(...)
    equivalent Developer-probe gate
~~~

After this PR, those production mutation paths must depend only on the production Minimum GPU Clock runtime/capability and normal profile rules.

Do not replace the removed guard with a generic "external IGCL owner" detector.

### 10.2 IntelGpuMinimumClockRuntime

Remove:

~~~text
BlocksDeveloperFrequencyMutation()
~~~

It is no longer part of production Minimum GPU Clock responsibility.

That method currently scans profile persistence only to answer a question from the Developer probe.

Once the probe is gone, keeping the method would be dead coupling.

---

## 11. Final production game-profile behavior must remain unchanged

The cleanup must not change PR #706 semantics.

Required behavior remains:

~~~text
active enabled Steam/XBOX profile
+ GameGpuMinimumClockSettings.Enabled == true
→ apply selected current AC/DC floor

otherwise
→ release min = -1
→ verify Intel factory minimum-frequency policy

unsupported saved target
→ release min = -1
→ fail closed

unknown/unreadable AC/DC
→ release min = -1
→ fail closed

resume
→ existing 2.5 s settle
→ same game-only reconcile

uninstall
→ always min = -1
→ readback verify
→ failure blocks uninstall
~~~

Do not modify:

- selectable-clock policy;
- recommended default calculation;
- AC/DC behavior;
- 300 ms profile slider debounce;
- Steam/XBOX profile UI;
- Profile Quick Settings GPU rows;
- startup factory release;
- resume recovery;
- uninstall fail-close;
- maximum-frequency preservation semantics.

---

## 12. No production IGCL PL1 feature in this PR

Deleting the Developer PoC also removes its IGCL power-limit experiment.

That does **not** mean:

~~~text
port SetTestPl1 into production
add GPU PL1 to Device
add GPU PL1 to Game Profile
merge PL1 into TDP
~~~

There is currently no approved production IGCL PL1 product policy.

Existing production TDP behavior is separate and must remain unchanged.

After this PR there should be no live `ctlPowerSetLimits` use that exists solely for this retired probe.

If GPU power-limit control is ever productized, it requires a separate design/work order.

---

## 13. No migration/recovery layer for historical PoC state

Do not add:

- a PoC ownership marker;
- a PoC cleanup registry key;
- a startup probe-state migration;
- a PL1 recovery database;
- a max-frequency reset manager.

The original Developer PoC was intentionally an in-memory developer diagnostic.

Normal Runtime shutdown already attempts its restore before this code is removed.

A historical hard-crash developer test does not justify adding permanent production state solely to retire the experiment.

Production Minimum GPU Clock continues to own only the minimum-frequency side according to PR #706.

Do not reset GPU maximum frequency or IGCL PL1 as part of normal production startup merely because this PoC once existed.

---

## 14. Tests to delete

Delete:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuIgclProbeTests.cs
~~~

Those tests exist only for the retired PoC:

- probe-local IGCL x64 ABI layout;
- Max/Max construction;
- original range restore;
- PL1 range validation;
- PL1 mutation/restore helpers.

Do not migrate these tests into production Minimum GPU Clock tests unless they test a production type/contract that still exists.

Production `IntelGpuMinimumClockControl` already has its own ABI/policy coverage.

---

## 15. Tests to simplify/update

### 15.1 IntelGpuMinimumClockFrontendTests

Keep production game-profile tests.

Remove Developer-probe coexistence cases such as:

~~~text
developerModified = true
Developer probe blocks Steam GPU mutation
Developer probe blocks whole-profile enable
Production ownership blocks Developer frequency operations
~~~

Simplify the test helper so it no longer accepts/injects:

~~~text
developerGpuFrequencyProbeModified
~~~

### 15.2 XboxGameProfileFrontendTests

Remove the Developer-probe conflict test and any `developerModified` test seam.

Keep normal XBOX GPU Minimum Clock mutation/live-apply coverage.

### 15.3 IntelGpuMinimumClockTests

Remove tests for:

~~~text
BlocksDeveloperFrequencyMutation()
~~~

Keep all production game-only/factory-release/resume/uninstall tests.

### 15.4 FrontendNamedPipeTransportTests

Remove:

- Developer Intel GPU probe capture RPC tests;
- Developer Intel GPU probe run RPC tests;
- probe request payload validation;
- fake frontend probe state/operation counters;
- fake `CaptureIntelGpuFrequencyProbeAsync`;
- fake `RunIntelGpuFrequencyProbeAsync`.

Update Frontend protocol expectations to 65.

### 15.5 FrontendContractTests

Remove contract/enum expectations for:

~~~text
FrontendIntelGpuFrequencyProbeSnapshot
FrontendIntelGpuFrequencyProbeOperation
CaptureIntelGpuFrequencyProbeAsync
RunIntelGpuFrequencyProbeAsync
~~~

Add/keep a focused contract-retirement assertion that the live frontend surface no longer contains those names.

Update protocol expectation to 65.

### 15.6 QuickSettingsInProcessSeamTests

Remove the test that exists only to prove the IGCL probe is lazily unmaterialized after shutdown, including reflection over:

~~~text
_intelGpuFrequencyProbe
~~~

Do not alter unrelated Quick Settings behavior.

### 15.7 UiArchitectureTests

The current diagnostic lazy-materialization test combines GameInput and Intel IGCL checks.

Rewrite it so it continues to validate the remaining diagnostics without expecting `IntelGpuIgclProbe`.

Remove the Developer-page assertions that require:

~~~text
Intel GPU Frequency / IGCL Probe
Set Max / Max + Readback
Restore Original Frequency
Set Test PL1 + Readback
Restore Original Power
CaptureIntelGpuFrequencyProbeAsync
RunIntelGpuFrequencyProbeAsync
~~~

A small absence assertion is appropriate:

~~~text
DeveloperPage.xaml does not contain "Intel GPU Frequency / IGCL Probe"
DeveloperPage.xaml.cs does not contain "IntelGpuFrequencyProbe"
~~~

Do not turn this into a large snapshot test.

### 15.8 Protocol-version tests

Search the full repository for hardcoded Frontend protocol version 64 expectations and update them to 65.

Likely affected contract/architecture tests include the same protocol guards touched by PR #706, such as:

~~~text
AddonQuickSettingsSurfaceParityTests
CenterMStartupContractTests
FrontButtonTransportContractTests
OverlayDeviceQuickSettingsTransportTests
OverlayTransportTests
VibrationContractRemovalTests
XboxGameSessionRuntimeHostTests
FrontendNamedPipeTransportTests
FrontendContractTests
~~~

Use repository search rather than relying only on this list.

Overlay protocol expectations remain unchanged.

---

## 16. Required repository-wide removal search

Before finalizing the PR, search the live source/test trees for all of:

~~~text
IntelGpuIgclProbe
IntelGpuIgclProbePolicy
FrontendIntelGpuFrequencyProbe
CaptureIntelGpuFrequencyProbe
RunIntelGpuFrequencyProbe
RunIntelGpuFrequencyProbeRequest
SetMaxMax
RestoreOriginalFrequency
SetTestPl1
RestoreOriginalPower
developerGpuFrequencyProbeModified
_developerGpuFrequencyProbeModified
ShouldBlockDeveloperFrequencyMutation
BlocksDeveloperFrequencyMutation
FrequencyModifiedByProbe
PowerModifiedByProbe
~~~

Expected result:

~~~text
src/...
tests/...
→ zero live references
~~~

Historical `docs/work-order` references are allowed and should remain.

Also search live source for:

~~~text
ctlPowerSetLimits
ctlPowerGetLimits
ctlEnumPowerDomains
~~~

Any remaining occurrence must belong to another intentionally supported production feature. Do not keep PoC-only power-control code accidentally.

---

## 17. Expected files to change

At minimum review/change:

### Delete

~~~text
src/SteamInputAddonforClaw/Diagnostics/IntelGpuIgclProbe.cs
src/SteamInputAddonforClaw.Contracts/Frontend/IntelGpuFrequencyProbeContracts.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuIgclProbeTests.cs
~~~

### Production/frontend cleanup

~~~text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
~~~

### Main UI

~~~text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
~~~

### Tests

At minimum:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/XboxGameProfileFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

Plus every test that hardcodes Frontend protocol version 64.

Exact file count may differ after repository search.

---

## 18. Explicit non-goals

Do not:

- change production Minimum GPU Clock product policy;
- change `min = -1` factory-release semantics;
- change maximum-frequency behavior;
- change game profile persistence;
- change Steam/XBOX active profile resolution;
- change AC/DC observation;
- change resume timing;
- change uninstall sequencing;
- change Intel FPS behavior;
- share/refactor Intel FPS native IGCL code;
- split `IntelGpuMinimumClock.cs`;
- add production GPU PL1;
- merge IGCL PL1 with MSI TDP;
- create a generic Intel GPU service;
- create a shared GPU manager/provider interface;
- create an external-IGCL-owner detector;
- add migration state for the retired PoC;
- touch Full1902 controller ownership;
- touch CTW.

This is a deletion/decoupling PR.

---

## 19. Acceptance criteria

The PR is complete only when all of the following are true:

1. Developer Menu no longer shows the Intel GPU Frequency / IGCL Probe card.
2. `IntelGpuIgclProbe.cs` is deleted.
3. `IntelGpuFrequencyProbeContracts.cs` is deleted.
4. No live Developer IGCL frequency/PL1 snapshot or operation contract remains.
5. No live Capture/Run Intel GPU probe RPC remains.
6. Frontend protocol is 65.
7. Overlay protocol is unchanged.
8. `InProcessAddonFrontendControl` no longer owns/materializes/disposes an `IntelGpuIgclProbe`.
9. `developerGpuFrequencyProbeModified` and its backing field are removed.
10. `ShouldBlockDeveloperFrequencyMutation` is removed.
11. `IntelGpuMinimumClockRuntime.BlocksDeveloperFrequencyMutation` is removed.
12. Steam game GPU Minimum Clock mutations no longer contain Developer-probe conflict branches.
13. XBOX game GPU Minimum Clock mutations no longer contain Developer-probe conflict branches.
14. Production game-only Minimum GPU Clock behavior from PR #706 is unchanged.
15. Production startup still releases to `min = -1` when no active enabled game owns a floor.
16. Production game exit still releases to `min = -1`.
17. Production resume recovery remains unchanged.
18. Production uninstall still always verifies `min = -1` before continuing.
19. Production Intel FPS remains unchanged.
20. No production GPU maximum-frequency owner is introduced.
21. No production IGCL PL1 feature is introduced.
22. Probe-only tests are deleted.
23. Production GPU tests remain and pass after conflict-test removal.
24. Named-pipe/frontend tests prove the retired RPC surface is gone.
25. Repository search shows no live source/test references to the retired probe names.
26. No new wrapper/manager/state/lock/retry abstraction is introduced.

---

## 20. Validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx --no-restore -m:1 -p:UseSharedCompilation=false

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false

git diff --check
~~~

No special MSI Claw hardware validation is required solely to prove deletion of the Developer PoC.

However, this PR must not be merged if automated tests reveal a regression in the production game-only Minimum GPU Clock path.

---

## 21. Final target architecture

After this PR:

~~~text
Developer Menu
    └─ no Intel GPU frequency / PL1 writer

Steam/XBOX active game profile
    ↓
GameGpuMinimumClockSettings
    ↓
IntelGpuMinimumClockRuntime
    ↓
IntelGpuMinimumClockControl
    ↓
IGCL frequency range
    ↓
minimum side only

no active enabled game floor
    ↓
min = -1
    ↓
Intel factory minimum-frequency policy
~~~

The cleanup objective is simple:

> **Remove the completed evidence-gathering IGCL writer and all compatibility code that existed only because it could compete with the production game-only Minimum GPU Clock owner.**

After this PR, production frequency control has one clear owner and one product policy.
