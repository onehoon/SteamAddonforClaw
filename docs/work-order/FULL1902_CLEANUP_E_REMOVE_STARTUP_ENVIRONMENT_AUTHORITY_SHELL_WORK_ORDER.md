# Work Order — Full1902 Cleanup E: Remove the Startup Environment Authority Shell

## Status

Focused deletion/simplification work order for removing the remaining pre-Full1902 **startup controller-environment authority shell** after:

```text
PR #476 — Cleanup A: removed legacy Steam-session controller-routing authority
PR #477 — Cleanup B: removed legacy Center M dummy/MainUI suppression subsystem
PR #478 — Cleanup C: removed dead routing-specific power resume branches
PR #479 — Cleanup D: removed controller-software / third-party manager authority
```

This is a **startup authority-contract cleanup**. It is not a controller lifecycle redesign and not a replacement authority abstraction.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     42c38ae05714c935cf3a5d2985704777cec08c61
latest merged production PR: #479 — Full1902 Cleanup D
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_A_REMOVE_LEGACY_STEAM_ROUTING_AUTHORITY_WORK_ORDER_V2.md`
- `docs/work-order/FULL1902_CLEANUP_C_REMOVE_DEAD_POWER_ROUTING_BRANCHES_WORK_ORDER.md`
- `docs/work-order/FULL1902_CLEANUP_D_REMOVE_CONTROLLER_SOFTWARE_MANAGER_AUTHORITY_WORK_ORDER.md`

The application is pre-release. Do not preserve obsolete startup DTO fields, enum values, test seams, or log vocabulary for source compatibility.

---

# 1. Product invariant this cleanup must expose directly

The current product has exactly one startup controller-authority source:

```text
Center M startup-root state
```

with exactly these production meanings:

```text
Center M startup roots exactly Enabled
→ MSI / stock controller authority
→ desired physical controller state = PID1901
→ stock PID1901 baseline is applicable
→ Addon does not own PID1902 / DirectInput / VIIPER presentation

Center M startup roots exactly Disabled
→ Addon controller authority
→ desired physical controller state = PID1902
→ Disabled-boot admission must succeed before ownership activation
→ deterministic Addon HidHide baseline
→ persistent DirectInput ownership
→ one Addon-owned VIIPER presentation

Center M startup roots Partial / Unavailable
→ no controller authority selected
→ fail closed / passive
```

The authority is already carried by:

```csharp
StartupResult.CenterMStartupState
```

Do not add another startup authority enum, boolean, manager, state machine, or persisted authority setting.

Steam/BPM remains only a virtual-presentation selector while Addon authority is active:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

---

# 2. Goal

Remove the obsolete startup shell:

```text
ControllerEnvironmentMode
ControllerEnvironmentReadiness
IControllerEnvironmentWaiter(mode, ...)
ControllerEnvironmentWaiter environment-oriented naming/branching
StartupResult.EnvironmentMode
StartupResult.EnvironmentReadiness
StartupResult.LegacyRoutingAllowed
```

while preserving the real startup safety hidden inside it:

```text
supported MSI Claw hardware gate
bounded hardware-probe stabilization
three stable MSI topology snapshots
350 ms sample interval
5 s topology timeout
internal MSI Claw topology only
external-controller hotplug ignored
PID1901 XInput control-HID readiness
PID1902 DirectInput control-HID readiness
PnP/control-interface settling protection
Center M startup-root classification
Disabled-boot prerequisite / recovery-journal / HidHide admission
stock PID1901 baseline in Enabled authority
stock PID1901 resume baseline only in Enabled authority
```

Target end state:

```text
StartupResult
    ├─ ShouldStartRuntime
    ├─ RecoverySafe
    ├─ HardwareSupported / HardwareDeviceModel / HardwareStatus
    ├─ CenterMStartupState          ← sole startup authority fact
    └─ DisabledBootAdmission

ControllerTopologyWaiter
    └─ answers only: is the supported MSI Claw's relevant physical topology stable and usable?
```

There must be no environment/controller-manager authority concept left in production startup.

---

# 3. Current-code proof — why Cleanup E is now safe and necessary

## 3.1 `ControllerEnvironmentMode` no longer represents a real production decision

After Cleanup D, the enum is intentionally left in `ControllerEnvironmentWaiter.cs` with this comment:

```text
the third-party controller-manager detection graph that once produced other modes is gone;
production always resolves StockCenterM
```

The enum still exposes:

```text
Unsupported
StockCenterM
ClawTweaks
HHCManaged
Indeterminate
```

but production no longer has any ClawTweaks/HHC/environment detector that can produce those authority modes.

In the current `StartupCoordinator`, supported hardware reaches:

```csharp
const ControllerEnvironmentMode environmentMode = ControllerEnvironmentMode.StockCenterM;
```

Both Center M Enabled and Center M Disabled startup paths call the same waiter using:

```csharp
ControllerEnvironmentMode.StockCenterM
```

The mode parameter therefore does not select behavior anymore. It only keeps a deleted authority model present in signatures/tests.

## 3.2 `ControllerEnvironmentReadiness` is now physical topology readiness

Current `ControllerEnvironmentWaiter` does not evaluate software/controller-manager state. Its real implementation is:

```text
enumerate present PnP controller devices
→ narrow to internal MSI Claw topology
→ require a resolvable PID1901 or PID1902 control HID
→ require the same relevant topology snapshot repeatedly
→ return Stable only after required stable snapshots
→ timeout/inspection failure => Indeterminate
```

That is a real lifecycle safeguard and must remain.

The current enum:

```csharp
ControllerEnvironmentReadiness { NotApplicable, Stable, Indeterminate }
```

still carries `NotApplicable` only because the old mode parameter can say `Unsupported`.

Production already performs the supported-hardware gate before calling the waiter, so this special `Unsupported` waiter mode is no longer a production responsibility.

## 3.3 `StartupResult.EnvironmentMode` is no longer an authority source

Production uses `StartupResult.EnvironmentMode` only in `AddonProcessHost` for:

1. a startup log line; and
2. an additional TDP/profile model gate:

```csharp
if (startupResult.EnvironmentMode == ControllerEnvironmentMode.StockCenterM
    && startupResult.HardwareDeviceModel is { } tdpModel
    && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
```

After Cleanup D, every supported path that reaches runtime initialization already reports `StockCenterM`, including Disabled-mode Addon authority.

Therefore this `EnvironmentMode == StockCenterM` condition is no longer meaningful. TDP/profile availability is a supported-hardware/model fact, not controller authority.

The correct remaining gate is the already-carried hardware model:

```csharp
startupResult.HardwareDeviceModel is { } tdpModel
&& MsiClawTdpPolicy.TryResolve(tdpModel, out _)
```

Do not replace the environment check with `CenterMStartupState == Enabled`; TDP support is not stock-controller-authority-only.

## 3.4 `StartupResult.EnvironmentReadiness` has no downstream behavior

Fresh production reference review shows `startupResult.EnvironmentReadiness` is used only in the `AddonProcessHost` startup log.

The readiness result is still needed **inside `StartupCoordinator`** to decide whether startup may continue, but it does not need to remain in the cross-component `StartupResult` after that decision has already been made.

## 3.5 `LegacyRoutingAllowed` is a stale name for one current stock-authority fact

Current `StartupResult` still contains:

```csharp
bool LegacyRoutingAllowed = true
```

The legacy routing owner it once selected was deleted in Cleanup A.

The only remaining production consumer is the runtime-factory call:

```csharp
stockCenterMAuthority: startupResult.LegacyRoutingAllowed
```

That boolean now gates only the stock PID1901 resume baseline.

The actual authority fact is already present as:

```csharp
startupResult.CenterMStartupState
```

Therefore remove `LegacyRoutingAllowed` and derive the existing runtime-factory argument directly at the call site:

```csharp
stockCenterMAuthority:
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Enabled
```

Do not add `StockCenterMAuthority` to `StartupResult`. That would duplicate the same authority fact under a second representation.

---

# 4. Scope boundary

## In scope

1. delete `ControllerEnvironmentMode` completely;
2. remove `StartupResult.EnvironmentMode`;
3. remove `StartupResult.EnvironmentReadiness`;
4. remove `StartupResult.LegacyRoutingAllowed`;
5. rename/simplify the environment waiter into a topology-only waiter;
6. remove the obsolete waiter `mode` parameter and mode switch;
7. remove the waiter `NotApplicable` result if no real caller remains;
8. derive stock resume-baseline authority directly from `CenterMStartupState == Enabled` at the existing runtime composition boundary;
9. remove the tautological `EnvironmentMode == StockCenterM` TDP/profile gate;
10. update startup logs/comments/tests so they describe hardware topology and Center M startup-root authority rather than a controller environment;
11. preserve all real topology stabilization behavior and fail-close paths;
12. fresh zero-residue reference search for the deleted startup environment symbols.

## Explicitly out of scope

Do not combine Cleanup E with:

```text
RecoveryJournal deletion/redesign
RecoveryManager mutation/lease cleanup
StartupHidHideRecoveryCleaner cleanup
MachineRecoverySafetyInspector cleanup
StartupVirtualOutputRecoveryInspector cleanup
AddonOwnedVirtualDeviceTracker cleanup
M1M2 diagnostic cleanup
power architecture changes
PID1901/PID1902 ownership changes
HidHide baseline changes
VIIPER presentation changes
PnP recovery changes
Center M startup-root mutation changes
first-time setup/provisioning redesign
TDP/profile behavior redesign
front-button policy changes
```

Cleanup E should be a small startup-contract simplification before the larger RecoveryJournal reference-closure pass.

---

# 5. Required change A — replace the environment waiter with a topology waiter

Current file:

```text
src/SteamInputAddonforClaw/Startup/ControllerEnvironmentWaiter.cs
```

Preferred rename:

```text
src/SteamInputAddonforClaw/Startup/ControllerTopologyWaiter.cs
```

Rename the production types:

```text
IControllerEnvironmentWaiter
→ IControllerTopologyWaiter

ControllerEnvironmentWaiter
→ ControllerTopologyWaiter

ControllerEnvironmentReadiness
→ ControllerTopologyReadiness
```

Delete:

```text
ControllerEnvironmentMode
```

### 5.1 Simplify the interface

Current:

```csharp
Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(
    ControllerEnvironmentMode mode,
    CancellationToken cancellationToken);
```

Target:

```csharp
Task<ControllerTopologyReadiness> WaitUntilStableAsync(
    CancellationToken cancellationToken);
```

Do not add an `authority`, `mode`, `expectedPid`, `centerMEnabled`, or similar parameter.

The waiter does not decide authority. It only determines whether the supported MSI Claw's relevant physical topology is stable enough for the caller's next step.

### 5.2 Simplify the readiness result

Preferred target:

```csharp
internal enum ControllerTopologyReadiness
{
    Stable,
    Indeterminate
}
```

Delete `NotApplicable` unless fresh code review finds a real non-test production caller requiring it.

Unsupported/indeterminate hardware is already handled before the waiter is called.

Do not turn `Indeterminate` into success. Topology inspection exception/timeout remains fail closed.

### 5.3 Remove mode-specific implementation branches

Delete:

```text
if (mode == ControllerEnvironmentMode.Unsupported) ... NotApplicable
CreateRelevantTopologySnapshot(mode)
mode switch { StockCenterM => ..., _ => false }
Mode=... logging
```

The readiness predicate becomes directly:

```text
relevant internal MSI devices exist
AND
PID1901 XInput control HID OR PID1902 DirectInput control HID is resolvable
```

Do not force a single PID here.

Why both remain valid:

```text
Center M Enabled startup may currently be PID1901
Center M Disabled startup may currently be PID1902
PnP/mode-transition timing may expose either valid control-HID topology while the startup caller is stabilizing the physical MSI device
```

The waiter proves topology readiness, not desired final authority state.

---

# 6. Required change B — preserve the exact real topology stabilization contract

The current waiter contains important real-hardware protections. Preserve them functionally.

## 6.1 Stable-snapshot requirement

Keep the existing default:

```text
required stable snapshots = 3
sample interval           = 350 ms
timeout                   = 5 s
```

Do not reduce this to a one-shot PnP read as part of cleanup.

## 6.2 Internal MSI Claw filtering

Preserve:

```csharp
_classifier.IsInternalHandheld(device, topology)
```

and the principle that unrelated controllers do not participate in the stability snapshot.

External Xbox/DualSense/Steam Controller connect/disconnect noise must not reset or block MSI Claw startup stabilization.

## 6.3 Control-HID requirement

Preserve `HasResolvableControlHid(...)` semantics:

```text
MsiClawNativeMode.XInput topology
OR
MsiClawNativeMode.DirectInput topology
```

A generic gamepad usage interface alone is not sufficient.

This is a real startup/PnP regression guard and must not be simplified away.

## 6.4 PnP settling

Keep relevant-device snapshot identity comparison over the internal MSI topology so that a changing control interface cannot reach Stable merely because the gamepad collection is already present.

## 6.5 Failure behavior

Preserve:

```text
cancellation → propagate OperationCanceledException
enumeration/inspection exception → log + Indeterminate
timeout → Indeterminate
```

No retries beyond the existing bounded waiter are needed.

Do not add epoch/barrier/lock/state-machine machinery for hypothetical instruction-level races.

---

# 7. Required change C — simplify `StartupCoordinator`

File:

```text
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
```

Rename dependency:

```text
_environmentWaiter
→ _topologyWaiter
```

and constructor type:

```text
IControllerTopologyWaiter
```

## 7.1 Supported hardware gate remains first

Preserve the existing order:

```text
Update gate
→ bounded hardware compatibility stabilization
→ unsupported / indeterminate hardware returns without topology work
→ capture Center M startup roots
→ select Enabled / Disabled / Partial-Unavailable branch
```

Do not make the topology waiter responsible for unsupported-hardware classification.

## 7.2 Enabled authority path

Current Cleanup-D code creates:

```csharp
const ControllerEnvironmentMode environmentMode = ControllerEnvironmentMode.StockCenterM;
```

Delete it.

Use:

```csharp
var topologyReadiness =
    await _topologyWaiter.WaitUntilStableAsync(cancellationToken);
```

Only `Stable` may proceed to `StockCenterMStartupBaseline.EstablishAsync(...)`.

If topology is `Indeterminate`, preserve the current passive/fail-closed result and do not run stock baseline or stale-journal retirement.

## 7.3 Disabled authority path

Current `RunDisabledBootAdmissionAsync(...)` also passes `StockCenterM` to the same waiter.

Remove the mode argument.

Preserve order:

```text
stable physical MSI topology
→ DisabledBootControllerAdmission.Evaluate()
→ prerequisite / recovery-journal / HidHide baseline checks
→ result carried to Full1902 physical ownership startup
```

Do not move HidHide normalization into the topology waiter.

Do not call stock PID1901 baseline in Disabled mode.

## 7.4 Partial / Unavailable startup roots

Preserve:

```text
no controller owner selected
RecoverySafe = false
Runtime may remain alive/passive as currently designed
```

Do not run the topology waiter merely to make these states look more determined. The startup-root authority itself is already fail-closed.

---

# 8. Required change D — simplify `StartupResult`

Current record contains:

```csharp
bool ShouldStartRuntime,
ControllerEnvironmentMode EnvironmentMode,
ControllerEnvironmentReadiness EnvironmentReadiness,
bool RecoverySafe,
bool HardwareSupported,
HandheldDeviceModelId? HardwareDeviceModel,
HardwareCompatibilityStatus? HardwareStatus,
FrontendCenterMStartupState CenterMStartupState,
bool LegacyRoutingAllowed,
DisabledBootControllerAdmissionResult? DisabledBootAdmission
```

Delete:

```text
EnvironmentMode
EnvironmentReadiness
LegacyRoutingAllowed
```

Target shape should carry only facts that still have a downstream production consumer, conceptually:

```csharp
internal sealed record StartupResult(
    bool ShouldStartRuntime,
    bool RecoverySafe = false,
    bool HardwareSupported = false,
    HandheldDeviceModelId? HardwareDeviceModel = null,
    HardwareCompatibilityStatus? HardwareStatus = null,
    FrontendCenterMStartupState CenterMStartupState = FrontendCenterMStartupState.Unavailable,
    DisabledBootControllerAdmissionResult? DisabledBootAdmission = null);
```

Exact parameter ordering may be adjusted for clarity, but prefer named arguments at call sites where boolean meaning is not obvious.

### Important

Do **not** add replacements such as:

```text
StartupAuthorityMode
ControllerAuthorityMode
StockCenterMAuthority
AddonAuthoritySelected
TopologyStable
```

as new `StartupResult` fields.

Current authority is already fully represented by `CenterMStartupState`.

The coordinator consumes topology readiness internally before creating the final result; no second exported readiness field is needed.

---

# 9. Required change E — remove `LegacyRoutingAllowed` without losing stock resume safety

File:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Current production wiring:

```csharp
stockCenterMAuthority: startupResult.LegacyRoutingAllowed
```

Replace it directly with the current authority source:

```csharp
stockCenterMAuthority:
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Enabled
```

or an equivalent local expression without storing another authority field.

### Preserve `AddonRuntimeCompositionFactory.stockCenterMAuthority`

The runtime factory's narrow boolean is still useful because it controls only one behavior:

```text
should resume establish the stock PID1901 baseline?
```

Do not remove that resume safety in Cleanup E.

Current intended behavior remains:

```text
Center M Enabled
→ stockCenterMAuthority = true
→ resume must prove stock PID1901 baseline

Center M Disabled
→ stockCenterMAuthority = false
→ resume must not call stock PID1901 baseline
→ Full1902 owned-controller recovery owns the Disabled path

Partial / Unavailable
→ stockCenterMAuthority = false
```

The runtime-factory boolean is a derived call-boundary fact, not a second startup authority source.

Do not move Center M startup-root probing into `AddonRuntimeCompositionFactory`.

---

# 10. Required change F — remove the stale `EnvironmentMode` TDP/profile gate

File:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Current code:

```csharp
if (startupResult.EnvironmentMode == ControllerEnvironmentMode.StockCenterM
    && startupResult.HardwareDeviceModel is { } tdpModel
    && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
```

After Cleanup D the first condition is tautological for supported runtime startup and does not represent TDP eligibility.

Change to hardware/model-only gating:

```csharp
if (startupResult.HardwareDeviceModel is { } tdpModel
    && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
```

Preserve the current model assignment and game-profile behavior after this gate.

Do not bind TDP/profile availability to Center M Enabled/Disabled. Those are controller-authority states, not TDP hardware capability states.

---

# 11. Required change G — clean startup logging vocabulary

Current host log:

```text
Starting runtime. Environment=...; Readiness=...
```

Remove environment/readiness vocabulary after those fields disappear.

Prefer facts that actually remain, for example:

```text
CenterMStartupState
HardwareStatus / HardwareDeviceModel when useful
DisabledBootAdmission outcome when already logged by that path
```

Do not add a new aggregate `AuthorityState` object only to improve a log line.

Inside the topology waiter, rename log categories/messages away from generic `Environment` where practical in the touched file, for example:

```text
ControllerTopology
Topology readiness wait started
Topology readiness poll
Topology readiness stable
Topology readiness timeout
```

Keep logs concise. This PR is not a broad logging rewrite.

---

# 12. Required change H — update composition only mechanically

File:

```text
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
```

Current construction:

```csharp
new ControllerEnvironmentWaiter(deviceEnumerator, classifier)
```

Change to:

```csharp
new ControllerTopologyWaiter(deviceEnumerator, classifier)
```

No new service/provider is needed.

Do not introduce:

```text
StartupAuthorityResolver
TopologyAuthorityCoordinator
ControllerStartupManager
EnvironmentFacade
ControllerLifecycleBootstrapper
```

Cleanup E should reduce the startup contract, not replace one name with a larger abstraction.

---

# 13. Tests — preserve real lifecycle coverage, remove legacy authority assertions

## 13.1 Rename the waiter test file

Current:

```text
tests/SteamInputAddonforClaw.Tests/ControllerEnvironmentWaiterTests.cs
```

Preferred:

```text
tests/SteamInputAddonforClaw.Tests/ControllerTopologyWaiterTests.cs
```

Rename the test class accordingly.

## 13.2 Preserve these real regression tests semantically

Keep equivalent coverage for:

```text
internal MSI handheld absent → Indeterminate
stable gamepad + PID1902 control HID → Stable
stable gamepad + PID1901 control HID → Stable
gamepad only, no control HID → not Stable
external controller hotplug noise → does not reset/block stabilization
MSI control interface keeps changing → does not report Stable
MSI control interface settles after several polls → eventually Stable
cancellation behavior if currently covered elsewhere / add only if missing and simple
```

Update calls from:

```csharp
WaitUntilStableAsync(ControllerEnvironmentMode.StockCenterM, token)
```

to:

```csharp
WaitUntilStableAsync(token)
```

and assertions to `ControllerTopologyReadiness`.

## 13.3 Delete the obsolete unsupported-mode waiter test

Current test:

```text
UnsupportedWaiter_ReturnsNotApplicableWithoutEnumerating
```

exists only because the old waiter accepts `ControllerEnvironmentMode.Unsupported`.

Delete it when `NotApplicable`/mode support is removed.

Retain the stronger production-boundary tests proving:

```text
unsupported hardware
→ StartupCoordinator never calls the topology waiter
```

That is the correct owner of unsupported-hardware behavior.

## 13.4 Update `StartupCoordinatorTests`

Remove assertions on:

```text
result.EnvironmentMode
result.EnvironmentReadiness
result.LegacyRoutingAllowed
```

Replace them with assertions on real current behavior:

```text
ShouldStartRuntime
RecoverySafe
HardwareStatus / HardwareSupported / HardwareDeviceModel
CenterMStartupState
DisabledBootAdmission
whether topology waiter ran
whether stock baseline ran
whether journal cleanup/retirement ran
```

Do not retain a fake environment mode merely to keep old test arrangements intact.

Rename test doubles:

```text
FakeEnvironmentWaiter → FakeTopologyWaiter
FixedEnvironmentWaiter → FixedTopologyWaiter
ThrowingEnvironmentWaiter → ThrowingTopologyWaiter
```

or equivalent.

## 13.5 Update `UnsupportedHardwareStartupGateTests`

Remove environment-mode/readiness output assertions.

Preserve the important ordering:

```text
Update gate
→ hardware compatibility
→ unsupported/indeterminate returns
→ topology waiter not invoked
```

For supported hardware, verify the topology waiter is invoked.

## 13.6 Update host/startup construction tests

Any test constructing:

```csharp
new StartupResult(true, ControllerEnvironmentMode..., ControllerEnvironmentReadiness...)
```

must construct only the retained fields.

Do not add test-only compatibility constructors to `StartupResult`.

---

# 14. Required architecture assertions

After implementation, add/update small existing architecture guards where useful so the following production facts remain obvious:

```text
ControllerEnvironmentMode                  → zero source/test references
ControllerEnvironmentReadiness             → zero source/test references
IControllerEnvironmentWaiter               → zero source/test references
ControllerEnvironmentWaiter                → zero source/test references
StartupResult.EnvironmentMode              → absent
StartupResult.EnvironmentReadiness         → absent
StartupResult.LegacyRoutingAllowed         → absent
```

Expected current replacement references:

```text
ControllerTopologyWaiter
IControllerTopologyWaiter
ControllerTopologyReadiness
StartupResult.CenterMStartupState
```

Do not enforce historical docs/work-orders to have zero text references. Historical documents may retain old terminology as implementation history.

Source/test cleanup is the requirement.

---

# 15. Do not weaken `RecoverySafe` in this PR

`RecoverySafe` is still part of current startup/runtime power/setup behavior.

Even though RecoveryJournal is a later major cleanup candidate, Cleanup E must not remove or reinterpret:

```text
StartupResult.RecoverySafe
RecoverySafetyState
PowerTransitionCoordinator incomplete-recovery fail-close
current setup/status recovery safety
stock startup stale-journal handling
Disabled-boot recovery-journal block
```

Changing those together with the environment shell would mix two separate architectural decisions.

---

# 16. Do not collapse Center M startup-root authority into `ShouldStartRuntime`

`ShouldStartRuntime` answers:

```text
should the process Runtime continue running?
```

It does **not** answer:

```text
who owns the physical controller?
```

For example, Partial/Unavailable authority may still keep the Runtime alive/passive.

Therefore preserve `CenterMStartupState` independently.

Do not replace the authority model with:

```text
ShouldStartRuntime == true → Addon authority
```

That would be incorrect.

---

# 17. No frontend protocol bump

Cleanup E changes internal startup/runtime composition only.

It does not change:

```text
FrontendStatusSnapshot
named-pipe DTO shape
frontend RPC methods
frontend settings contract
```

Therefore do not bump `FrontendTransportProtocol.CurrentVersion` solely for this cleanup.

If implementation discovers an actual frontend contract change, stop and justify it separately rather than silently broadening Cleanup E.

---

# 18. Manual MSI Claw smoke matrix

If supported hardware is available, run at least the following focused smoke checks.

## 18.1 Center M Enabled boot

Expected:

```text
startup roots classify Enabled
supported MSI topology stabilizes
stock PID1901 baseline establishes
Runtime starts normally
no ControllerEnvironmentMode/environment-detection log path
sleep/resume still performs stock PID1901 baseline verification
```

## 18.2 Center M Disabled boot

Expected:

```text
startup roots classify Disabled
same topology waiter stabilizes PID1901 or PID1902 control-HID topology as applicable
DisabledBootControllerAdmission runs
stock PID1901 startup baseline is NOT invoked
Addon Full1902 ownership can proceed only after admission Ready
sleep/resume does NOT invoke stock PID1901 baseline
```

## 18.3 Partial/Unavailable startup roots

Expected:

```text
no controller owner selected
no stock baseline
no Disabled-mode physical ownership activation
Runtime remains passive according to current policy
```

## 18.4 PnP/control-HID settling

If reasonably reproducible during boot/resume testing, confirm the cleanup has not made the startup topology check one-shot or gamepad-interface-only.

If hardware is unavailable, report manual smoke as blocked. Do not add production complexity merely to simulate hardware lifecycle beyond the existing tests.

---

# 19. Automated validation

Run:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Also run targeted suites for at least:

```text
ControllerTopologyWaiterTests
StartupCoordinatorTests
UnsupportedHardwareStartupGateTests
AddonProcessHostStartupTests
power/resume tests that cover stockCenterMAuthority
```

Fresh reference-closure search in production source and tests:

```text
ControllerEnvironmentMode
ControllerEnvironmentReadiness
IControllerEnvironmentWaiter
ControllerEnvironmentWaiter
EnvironmentMode
EnvironmentReadiness
LegacyRoutingAllowed
```

Expected for production source/tests:

```text
zero references to deleted startup environment symbols
```

Historical docs may still contain them.

Also confirm:

```text
CenterMStartupState still has live production consumers
stockCenterMAuthority still gates only stock PID1901 resume baseline
ControllerTopologyWaiter retains both PID1901 and PID1902 control-HID recognition
external-controller noise regression still passes
```

---

# 20. Review checklist

A reviewer should verify all of the following before merge.

## Authority

- [ ] `CenterMStartupState` is the only exported startup controller-authority fact.
- [ ] `LegacyRoutingAllowed` is gone.
- [ ] No replacement authority boolean/enum was added to `StartupResult`.
- [ ] stock PID1901 resume baseline is derived from `CenterMStartupState == Enabled`.
- [ ] Disabled/Partial/Unavailable never select the stock resume baseline.

## Topology

- [ ] `ControllerEnvironmentMode` is gone.
- [ ] the waiter has no mode/authority parameter.
- [ ] topology still requires the MSI Claw internal device set.
- [ ] gamepad-only topology cannot report Stable.
- [ ] PID1901 XInput control HID can satisfy readiness.
- [ ] PID1902 DirectInput control HID can satisfy readiness.
- [ ] external controller hotplug does not affect MSI stabilization.
- [ ] changing PnP control-HID identity prevents premature Stable.
- [ ] timeout/inspection failure remains fail closed.

## Startup result / host

- [ ] `EnvironmentMode` removed from `StartupResult`.
- [ ] `EnvironmentReadiness` removed from `StartupResult`.
- [ ] TDP/profile model gating no longer depends on controller environment.
- [ ] no frontend protocol bump unless an actual frontend DTO changed.

## Scope discipline

- [ ] RecoveryJournal architecture unchanged.
- [ ] DisabledBootControllerAdmission safety checks unchanged except for naming/call signature needed by topology cleanup.
- [ ] stock startup baseline behavior unchanged.
- [ ] Full1902 physical/PnP/HidHide/VIIPER ownership behavior unchanged.
- [ ] no new manager/facade/state-machine abstraction introduced.

---

# 21. Completion criteria

Cleanup E is complete when the production startup model reads approximately as:

```text
Update gate
→ supported hardware stabilization
→ Center M startup-root authority capture

Enabled
→ physical MSI topology stable?
→ yes: establish stock PID1901 baseline
→ resolve current stale-startup recovery state
→ Runtime

Disabled
→ physical MSI topology stable?
→ yes: DisabledBootControllerAdmission
→ Runtime remains mandatory
→ Full1902 physical ownership activation proceeds only from Ready admission

Partial / Unavailable
→ no controller owner selected
→ passive/fail closed
```

and there is no longer any production concept equivalent to:

```text
ControllerEnvironmentMode
ClawTweaks/HHC startup environment
EnvironmentReadiness exported from startup
LegacyRoutingAllowed
```

The intended end state is not merely fewer lines. It is:

> **Center M startup roots are the single startup controller-authority fact; physical topology stabilization is a separate, narrow safety check; and no deleted routing/controller-manager authority vocabulary remains between them.**
