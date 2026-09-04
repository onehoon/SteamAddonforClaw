# Work Order — Full1902 Cleanup D: Remove Controller Software / Third-Party Manager Authority

## Status

Focused deletion/simplification work order for removing the remaining pre-Full1902 **controller-software / third-party-manager detection and compatibility authority** after:

```text
PR #476 — Cleanup A: removed legacy Steam-session controller-routing authority
PR #477 — Cleanup B: removed legacy Center M dummy/MainUI suppression subsystem
PR #478 — Cleanup C: removed dead routing-specific power resume branches
```

This is a **product-authority cleanup**, not a coexistence implementation and not a new controller-manager arbitration design.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     7d9c296b4acba238f10929d709b404b7cc91387e
latest merged production PR: #478 — Full1902 Cleanup C
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_A_REMOVE_LEGACY_STEAM_ROUTING_AUTHORITY_WORK_ORDER_V2.md`
- `docs/work-order/FULL1902_CLEANUP_B_REMOVE_LEGACY_CENTERM_DUMMY_HELPER_SUPPRESSION_WORK_ORDER.md`
- `docs/work-order/FULL1902_CLEANUP_C_REMOVE_DEAD_POWER_ROUTING_BRANCHES_WORK_ORDER.md`

The application is pre-release. Do not preserve obsolete controller-manager DTOs, probes, UI cards, or compatibility gates for source compatibility.

---

# 1. Product decision this cleanup implements

The current Full1902 controller authority is selected by **MSI Center M startup-root authority**, not by scanning installed/running third-party controller software.

Current product model:

```text
Center M startup roots exactly Enabled
→ MSI / stock controller authority
→ desired physical PID1901
→ Addon does not own physical controller / VIIPER presentation

Center M startup roots exactly Disabled
→ Addon controller authority
→ desired physical PID1902
→ persistent DirectInput ownership
→ deterministic Addon HidHide baseline
→ one persistent VIIPER runtime
→ exactly one live X360 or SteamDeck presentation
```

The three authority roots remain:

```text
MSI_Center_M_Server scheduled task
MSI_Center_M_Updater scheduled task
MSI Foundation Service startup type
```

Third-party controller software is **not** an authority input:

```text
ClawTweaks installed/running
Handheld Companion installed/running
Winhanced detected
MSI Center M UI/process/service runtime state
```

must no longer decide whether the Addon may enter/hold Full1902 authority.

The Addon does not promise coexistence with third-party controller managers. It simply stops trying to detect/arbitrate them as product authority.

Real supported lifecycle safety continues to react to **actual controller state and operation results**, for example:

```text
PID1902 → PID1901 drift while Addon authority is active
physical device loss / PnP re-enumeration
DirectInput session loss
HidHide operation/read-back failure
VIIPER operation/teardown failure
stock PID1901 restoration failure
startup-root mutation/read-back failure
```

Do not replace software detection with a new generalized process scanner, conflict manager, authority manager, or coexistence service.

---

# 2. Goal

Remove the obsolete graph:

```text
installed/running controller software probes
    ↓
ControllerSoftwareStatus[]
    ↓
ControllerSoftwareSnapshot
    ↓
ControllerManagerClassifier
    ↓
ControllerEnvironmentAssessmentProvider
    ↓
ControllerEnvironmentCompatibility
    ↓
startup / Disable-and-Restart / setup / provisioning / status gates
    ↓
Controller Software Status UI + frontend transport fields
```

Target end state:

```text
no ClawTweaks/HHC/Winhanced controller-manager authority scan
no MSI Center M process/runtime software-status authority scan
no ControllerManagerClassifier
no ControllerEnvironmentAssessmentProvider
no ControllerEnvironmentCompatibility policy
no manager gate in Disabled-boot admission
no manager gate in Disable Center M + Restart
no compatibility gate in first-time prerequisite setup
no compatibility gate in HidHide provisioning
no Controller Software Status card
no controller-software fields in frontend status contract
no Developer Environment Discovery dependency on production controller-software DTOs
```

Preserve:

```text
CenterMStartupControl
CenterMStartupHelper
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
ControllerEnvironmentWaiter physical MSI topology stabilization
Full1902 physical ownership / recovery
AddonControllerHidHideBaseline
MsiClawAddonPresentation / VIIPER
PowerTransitionWatcher / current resume behavior
RecoveryJournal behavior for now
```

---

# 3. Current-code proof — why this cleanup is required

## 3.1 Production still constructs third-party software detectors

Current `AddonStartupCompositionFactory` explicitly constructs:

```csharp
var controllerSoftwareProviders = new IControllerSoftwareStatusProvider[]
{
    new MsiCenterMSoftwareStatusProvider(),
    new ClawTweaksSoftwareStatusProvider(
        new ClawTweaksInstallationProbe(),
        new ClawTweaksRuntimeDetector()),
    new HandheldCompanionSoftwareStatusProvider(
        new HandheldCompanionRuntimeDetector()),
};

var controllerEnvironmentAssessmentProvider =
    new ControllerEnvironmentAssessmentProvider(controllerSoftwareProviders);
```

That provider is then passed to startup, Disabled-boot admission, Runtime status, and Disable-and-Restart composition.

This is not merely dead UI code. It still participates in real mutation admission.

## 3.2 Disabled-boot admission still blocks on manager classification

Current `DisabledBootControllerAdmission.Evaluate()` begins with:

```text
capture controller-manager assessment
→ only ControllerManagerKind.None may continue
→ detected / indeterminate / exception blocks Addon authority
```

After that, the same method performs still-current safety checks:

```text
Runtime prerequisites
RecoveryJournal evidence
HidHide baseline normalize + read-back verify
```

Cleanup D removes only the obsolete manager step. The other checks remain.

## 3.3 Disable Center M + Restart still has a live controller-manager gate

Current `CenterMRebootAuthorityTransition` owns:

```csharp
private readonly Func<bool> _conflictingControllerEnvironment;
```

and `DisableAsync()` rejects the authority transition when that callback reports a detected/unverified manager.

This is a real product gate and conflicts with the current policy.

Remove the gate; preserve the actual current mutation-safety checks and ordered reboot-bound transition.

## 3.4 First-time setup and provisioning still consume stock-era compatibility

Current `FirstTimeSetupInput` contains:

```text
ControllerEnvironmentCompatibilityAssessment Compatibility
```

and blocks on:

```text
CompatibilityUnsupported
CompatibilityIndeterminate
```

Current `HidHideProvisioningContext` also contains compatibility and requires:

```csharp
context.Compatibility.AllowsMutation
```

The elevated prerequisite helper independently recreates the old policy by constructing:

```text
MsiCenterMSoftwareStatusProvider
CurrentControllerEnvironmentCompatibilityPolicy
```

before installing HidHide / usbip-win2.

All of these are the same obsolete authority dependency and must be removed in the same reference closure.

## 3.5 Status/frontend still exposes the old model

Current `SystemStatusSnapshot` carries:

```text
ControllerSoftware
Compatibility
```

and `FrontendStatusSnapshot` carries:

```text
ControllerSoftware
ControllerEnvironmentStatus
ControllerEnvironmentReason
```

The desktop Status page renders a `Controller Software` expander.

These fields no longer describe current Full1902 authority and should not survive solely as historical diagnostics.

---

# 4. Scope boundary

## In scope

1. remove production third-party controller software installation/runtime detection;
2. remove controller-manager classification;
3. remove controller-environment compatibility policy and assessment provider;
4. remove manager-based Disabled-boot admission;
5. remove manager-based Disable-and-Restart admission;
6. remove compatibility from first-time setup/provisioning safety policy;
7. remove MSI Center M process/runtime software-status probing when its reference closure becomes empty;
8. remove controller-software and compatibility status/frontend DTOs;
9. remove the Controller Software Status UI group;
10. disconnect/delete the old Environment Discovery `Current Detection` subsection if it is the final consumer of these production DTOs;
11. remove tests that exist only for the deleted manager/software compatibility architecture;
12. bump the desktop/QAM frontend protocol because `FrontendStatusSnapshot` changes shape.

## Explicitly out of scope — next cleanup

Do **not** turn this PR into the final cleanup of:

```text
ControllerEnvironmentMode
ControllerEnvironmentReadiness
StartupResult.EnvironmentMode
StartupResult.LegacyRoutingAllowed
IControllerEnvironmentWaiter mode parameter
```

The handoff identifies these as the next legacy abstraction cluster.

Cleanup D may make the minimum mechanical adjustment needed to keep the real topology waiter compiling, but the final collapse/removal of `ControllerEnvironmentMode` is a separate cleanup.

Also out of scope:

- RecoveryJournal / `RecoverySafe` redesign or deletion;
- power coordinator redesign;
- PID1901/PID1902 transition redesign;
- PnP recovery changes;
- HidHide baseline ownership changes;
- VIIPER ownership changes;
- user termination redesign;
- final WING/OEM1 mapping policy;
- Overlay button policy;
- rumble / gyro / M1/M2 work;
- adding third-party coexistence support.

---

# 5. Required change A — remove software-provider production composition

File:

```text
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
```

Remove production construction of:

```text
MsiCenterMSoftwareStatusProvider
ClawTweaksSoftwareStatusProvider
HandheldCompanionSoftwareStatusProvider
ClawTweaksInstallationProbe
ClawTweaksRuntimeDetector
HandheldCompanionRuntimeDetector
ControllerEnvironmentAssessmentProvider
```

Remove `ControllerEnvironmentAssessmentProvider` from `AddonStartupComposition` if no current consumer remains after this PR.

Update construction of:

```text
DisabledBootControllerAdmission
StartupCoordinator
AddonRuntimeCompositionFactory
CenterMRebootAuthorityTransition wiring through AddonProcessHost
```

to no longer require this provider.

Do not replace it with:

```text
ControllerConflictInspector
ThirdPartyManagerDetector
ControllerAuthorityScanner
ControllerSoftwareService
```

---

# 6. Required change B — simplify Disabled-boot admission

File:

```text
src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
```

Remove constructor dependency:

```text
IControllerEnvironmentAssessmentProvider
```

Delete the complete manager-assessment step:

```text
Capture().Manager.Kind
ControllerManagerKind.None requirement
ControllerManagerAssessmentUnavailable
ControllerManager=<kind>
manager-specific logging
```

Do not replace it with a new process/package scan.

Preserve the remaining order and fail-closed behavior:

```text
1. Runtime prerequisite inspection
2. RecoveryJournal read / NoRecoveryNeeded requirement
3. deterministic Addon HidHide baseline normalize + read-back verification
4. Ready only after all retained checks succeed
```

Update comments that still describe the result as read-only if necessary: current implementation already performs HidHide normalization, so documentation should match actual current behavior.

Do not remove `loadRecoveryJournal` in this PR.

Do not weaken HidHide verification.

---

# 7. Required change C — remove third-party manager gate from Disable-and-Restart

Files:

```text
src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Delete:

```csharp
private readonly Func<bool> _conflictingControllerEnvironment;
```

and the matching constructor parameter/assignment.

Delete the `DisableAsync()` check that rejects Addon authority because another controller manager is installed/running/indeterminate.

Delete:

```text
AddonProcessHost.IsConflictingControllerEnvironment(...)
```

and the composition callback that calls it.

### Preserve the real Disable-and-Restart safety chain

The retained preflight/mutation order must still include equivalent current protections:

```text
lower-level Runtime termination/mutation safety
current prerequisite capture
RecoverySafe requirement
HidHide/USBIP2/VIIPER readiness
current HidHide baseline inspection
mandatory Addon startup registration proof
persistent zero-target HidHide baseline apply + verify
Center M startup roots → Disabled + read-back verify
immediate restart request
```

Do not weaken `Enable Center M and Restart` / stock restoration.

Do not change the rule that conflicting third-party software must never block the official stock-release path; after Cleanup D there is simply no such detection gate on either path.

Rewrite stale error text mentioning "routing, native-mode" only if its underlying termination decision no longer contains those concepts after fresh reference review. Do not broaden this PR into termination cleanup.

---

# 8. Required change D — delete the controller-manager classification graph

Delete after fresh reference closure:

```text
src/SteamInputAddonforClaw/Status/ControllerEnvironmentAssessmentProvider.cs
src/SteamInputAddonforClaw/Status/ControllerManagerClassification.cs
src/SteamInputAddonforClaw/Status/ControllerEnvironmentCompatibility.cs
```

This removes concepts including:

```text
ControllerEnvironmentAssessmentSnapshot
IControllerEnvironmentAssessmentProvider
ControllerEnvironmentAssessmentProvider
ControllerManagerKind
ControllerManagerClassificationReason
ControllerManagerClassification
ControllerManagerClassifier
ControllerEnvironmentCompatibilityStatus
ControllerEnvironmentCompatibilityReason
ControllerEnvironmentCompatibilityAssessment
ControllerSoftwareSnapshot
IControllerEnvironmentCompatibilityPolicy
CurrentControllerEnvironmentCompatibilityPolicy
```

Do not keep compatibility enums/records only for unit tests or frontend serialization.

Do not reinterpret these old values as Full1902 authority state.

---

# 9. Required change E — delete third-party software detector/probe graph

## 9.1 Controller software providers

Delete after reference closure:

```text
src/SteamInputAddonforClaw/Status/ControllerSoftwareStatusProviders.cs
```

This should remove the old provider/probe support types that become unused, including as applicable:

```text
ClawTweaksSoftwareStatusProvider
HandheldCompanionSoftwareStatusProvider
MsiCenterMSoftwareStatusProvider
ApplicationInstallationInfo
IApplicationInstallationProbe
InstalledApplicationRegistration
IUninstallRegistrationSource
WindowsUninstallRegistrationSource
UninstallRegistrationInstallationProbe
HandheldCompanionInstallationProbe
MsiCenterMInstallationProbe
ControllerSoftwareStatusSorter
```

## 9.2 ClawTweaks / HHC environment detector

Current file:

```text
src/SteamInputAddonforClaw/Startup/ClawTweaksEnvironmentDetector.cs
```

contains both the obsolete detector graph and the still-temporarily-referenced `ControllerEnvironmentMode` enum.

Delete the obsolete pieces:

```text
ClawTweaksState
ControllerEnvironment record
IHandheldCompanionRuntimeDetector
IClawTweaksRuntimeDetector
ClawTweaksInstallationInfo
IClawTweaksInstallationProbe
ClawTweaksInstallationProbe
ClawTweaksRuntimeDetector
HandheldCompanionRuntimeDetector
ClawTweaksEnvironmentDetector
KnownExecutablePaths scan
```

Do **not** keep this whole file just to host one enum.

Mechanically move `ControllerEnvironmentMode` to an already-existing startup-adjacent file that still needs it, preferably:

```text
ControllerEnvironmentWaiter.cs
```

or `StartupCoordinator.cs`.

Do not create a new `ControllerEnvironmentTypes.cs` solely for an abstraction scheduled for removal in the next cleanup.

Keep the enum shape in Cleanup D unless compile/reference closure makes a smaller mechanical reduction unavoidable. Its final removal is the next cleanup.

## 9.3 MSI Center M runtime software-status detection

Fresh baseline review shows the old runtime/process detector is consumed by the old `MsiCenterMSoftwareStatusProvider` and tests, not by startup-root authority.

After confirming reference closure, delete:

```text
src/SteamInputAddonforClaw/Status/MsiCenterMRuntimeDetection.cs
```

and the `MsiCenterMIdentity` constants that exist only for that software-status path.

### Critical distinction

Deleting this does **not** delete or weaken:

```text
CenterMStartupControl
CenterMStartupHelper
three startup-root readers/writers
MSI Foundation Service startup-mode mutation
stock PID1901 baseline
```

Do not replace process/runtime detection with another Center M liveness scanner.

---

# 10. Required change F — simplify StartupCoordinator without doing Cleanup E

File:

```text
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
```

Remove:

```text
_environmentAssessmentProvider
constructor parameter
initial controller-software environment Capture()
StartupControllerEnvironmentMapper.Map(...)
branches whose only input was that assessment
```

Current `StartupControllerEnvironmentMapper.Map(...)` ignores its input and always returns:

```text
ControllerEnvironmentMode.StockCenterM
```

Therefore production behavior can be preserved by directly using the current temporary mode where the still-existing topology waiter/result contract requires it:

```csharp
ControllerEnvironmentMode.StockCenterM
```

Do not introduce a replacement environment assessment.

Delete after reference closure:

```text
src/SteamInputAddonforClaw/Startup/StartupControllerEnvironmentMapper.cs
```

### Preserve real startup behavior

Keep:

```text
update gate
hardware compatibility stabilization
Center M startup-root authority capture
Disabled branch selection
Partial/Unavailable passive handling
physical MSI Claw topology stabilization
stock baseline on Enabled authority
current stale-recovery handling
```

This PR is **not** authorization to delete `ControllerEnvironmentWaiter` because its physical topology stabilization is real startup safety.

---

# 11. Required change G — remove compatibility from first-time setup policy

Files:

```text
src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs
src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs
```

Remove from `FirstTimeSetupInput`:

```text
ControllerEnvironmentCompatibilityAssessment Compatibility
```

Remove reasons:

```text
CompatibilityUnsupported
CompatibilityIndeterminate
```

Remove policy branches that block setup based on those values.

Update `FrontendPrerequisiteSetupExecutor` so it constructs setup input from retained facts only.

### Preserve current setup safety

Keep the existing real gates for:

```text
hardware supported/indeterminate
provisioning receipt/storage integrity
RecoverySafe
pending reboot
existing-unverified/incompatible component installs
Steam active while install is needed
HidHide / usbip-win2 package/runtime evidence
```

Do not make setup unconditional.

---

# 12. Required change H — remove compatibility from HidHide provisioning

File:

```text
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs
```

Change:

```text
HidHideProvisioningContext
SystemStatusHidHideProvisioningSafetyStateProvider
HidHideProvisioner.AllowsInstall(...)
```

so compatibility is no longer carried or checked.

Current:

```csharp
context.SetupAllowed
&& context.Compatibility.AllowsMutation
&& !context.Steam.IsActive
&& context.HidHide.Status == PrerequisiteStatus.Missing
```

Target conceptually:

```csharp
context.SetupAllowed
&& !context.Steam.IsActive
&& context.HidHide.Status == PrerequisiteStatus.Missing
```

while preserving the rest of the provisioning state machine/receipt validation/install verification unchanged.

Do not remove the Steam-active safety gate or package-integrity checks.

---

# 13. Required change I — remove compatibility from elevated prerequisite helper

File:

```text
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
```

Current `EvaluateSafetyGate()` separately reconstructs the obsolete stock-era policy:

```csharp
new MsiCenterMSoftwareStatusProvider()
→ CurrentControllerEnvironmentCompatibilityPolicy
→ compatibility.AllowsMutation
```

Delete only this compatibility/software-status portion.

Preserve the retained elevated safety gate:

```text
supported hardware proof
MachineRecoverySafetyInspector / recovery-safe requirement
Steam RunningAppID / Big Picture safety gate
trusted provisioning storage
installer/package hash and post-install verification
```

Do not add a replacement Center M process check.

---

# 14. Required change J — simplify Runtime system status

Files:

```text
src/SteamInputAddonforClaw/Status/SystemStatusContracts.cs
src/SteamInputAddonforClaw/Status/SystemStatusProvider.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
```

## 14.1 Remove old status contracts

Delete from `SystemStatusContracts.cs` after reference closure:

```text
ControllerSoftwareKind
SoftwareInstallationStatus
SoftwareRuntimeStatus
ControllerSoftwareStatus
IControllerSoftwareStatusProvider
```

Remove from `SystemStatusSnapshot`:

```text
ControllerSoftware
Compatibility
```

Preserve:

```text
Device
HardwareCompatibility
Prerequisites
Steam
Addon
RecoverySafe
```

## 14.2 Remove environment provider from SystemStatusProvider

Remove:

```text
IControllerEnvironmentAssessmentProvider environmentAssessmentProvider
software-provider overload
Capture().Software
Capture().Compatibility
FormatCompatibilityReason(...)
```

Simplify `MapNonOwnedStatus(...)` to use only current facts it actually owns:

```text
RecoverySafe
HardwareCompatibility
RuntimePrerequisites
```

Do not add a new current-process Center M detector.

A healthy non-owned status may continue to report:

```text
Passive — MSI Center M owns the controller.
```

when the current host is in the non-owned path.

The Full1902 owned-controller status override remains the authority for positively-proven Disabled-mode ownership.

## 14.3 Remove runtime factory dependency

Remove `IControllerEnvironmentAssessmentProvider` from `AddonRuntimeCompositionFactory.Create(...)` and its call sites.

Preserve:

```text
SteamSessionRuntime
PowerMutationGate
RecoverySafetyState
stockCenterMAuthority resume baseline
Full1902 Addon-status override
```

---

# 15. Required change K — remove controller software from frontend contract and Status UI

## 15.1 Runtime → frontend mapper

File:

```text
src/SteamInputAddonforClaw/Frontend/FrontendSnapshotMapper.cs
```

Remove mapping of:

```text
ControllerSoftware
ControllerEnvironmentStatus
ControllerEnvironmentReason
MapInstallation
MapRuntime
MapControllerEnvironmentStatus
```

Keep mapping current device/hardware/prerequisite/Steam/Addon/setup/recovery fields.

## 15.2 Frontend contract

File:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
```

Delete after reference closure:

```text
FrontendControllerEnvironmentStatus
FrontendSoftwareInstallationStatus
FrontendSoftwareRuntimeStatus
FrontendSoftwareSnapshot
```

Remove from `FrontendStatusSnapshot`:

```text
ControllerSoftware
ControllerEnvironmentStatus
ControllerEnvironmentReason
```

Do not retain optional/defaulted legacy fields just to keep protocol compatibility. The product is pre-release and the wire protocol already has version negotiation.

## 15.3 Status page

Files:

```text
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml.cs
src/SteamInputAddonforClaw.UI/StatusPresentation.cs
```

Delete:

```text
ControllerSoftwareExpander
controller-software card rendering block
FormatControllerSoftwareStatus(...)
ControllerEnvironmentStatus-based warning condition
```

Keep:

```text
Device status
Steam status
Addon warning InfoBar
Routing Components / prerequisite status
```

Do not replace the deleted card with a "Third-party software not monitored" card.

---

# 16. Frontend transport protocol bump

File:

```text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
```

Current baseline:

```csharp
FrontendTransportProtocol.CurrentVersion = 21;
```

Because `FrontendStatusSnapshot` loses serialized members, bump:

```text
21 → 22
```

Update the adjacent protocol-version comment to describe this Cleanup-D status-contract change.

An older v21 frontend must fail the handshake rather than connect and deserialize a different status payload shape.

Update exact-value contract tests such as the current Center M startup transport-contract assertion.

Do not bump Overlay transport protocol; it is a separate contract.

---

# 17. Developer Environment Discovery — do not preserve production DTOs for it

Current Developer Environment Discovery contains a `Current Detection` subsection built from:

```text
ControllerEnvironmentAssessmentProvider
MsiCenterMSoftwareStatusProvider
StartupControllerEnvironmentMapper
ControllerSoftwareStatus
ControllerEnvironment
```

That subsection is not justification to keep the production compatibility model.

Preferred Cleanup-D result:

```text
remove the legacy Current Detection subsection/record/writer output
keep the rest of Environment Discovery intact
```

Affected files may include:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryContracts.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportWriter.cs
tests/SteamInputAddonforClaw.Tests/EnvironmentDiscoveryReportTests.cs
```

A temporary "Unavailable" message is acceptable only if it can be done entirely inside Diagnostics without recreating production software/manager DTOs.

Do not move `ControllerSoftwareStatus` into Diagnostics as a compatibility workaround.

Also fresh-grep and delete if still unreferenced:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawTweaksCompatibilitySnapshotLogger.cs
```

This file is not current Full1902 authority and should not be retained merely because it may be useful someday.

---

# 18. Documentation distinction — unsupported coexistence vs automatic detection

Cleanup D changes this:

```text
Addon detects third-party managers and blocks based on their presence/state
```

into this:

```text
Addon does not use third-party manager presence/state as controller authority input
```

It does **not** assert:

```text
ClawTweaks/HHC coexistence is officially supported
```

Therefore do not opportunistically remove user-facing safety warnings in `README.md` / Korean guide solely because automatic detection is gone.

Those warnings can continue to say simultaneous use is unsupported.

Also do not delete ClawTweaks/HHC references from:

```text
THIRD_PARTY_NOTICES.md
reverse-engineering/reference documents
historical work orders
```

where they are evidence/licensing/history rather than product authority code.

---

# 19. Tests — delete architecture-only coverage

Fresh-grep before implementation and remove tests whose only subject is the deleted software/manager compatibility architecture.

Expected direct deletion candidates include, if still present and reference closure confirms:

```text
ControllerEnvironmentAssessmentProviderTests.cs
ControllerEnvironmentCompatibilityTests.cs
ControllerManagerClassification tests
controller-software provider/probe tests
ClawTweaks/HHC environment detector tests
MSI Center M runtime software-status detector tests
```

Do not rewrite them to test a new manager abstraction.

---

# 20. Tests — update retained lifecycle/setup coverage

Update retained tests rather than deleting real safety coverage.

## 20.1 DisabledBootControllerAdmissionTests

Remove manager-based fixtures/tests:

```text
manager detected → blocked
manager indeterminate → blocked
manager assessment throws → blocked
```

Keep/add equivalent coverage proving:

```text
prerequisite inspection failure blocks
prerequisites not ready block
RecoveryJournal read failure blocks
RecoveryJournal remaining blocks
HidHide normalization failure/noncompliance blocks
all retained checks verified → Ready
```

## 20.2 CenterMRebootAuthorityTransitionTests

Delete tests for:

```text
AddonProcessHost.IsConflictingControllerEnvironment
third-party manager blocks Disable-and-Restart
```

Preserve tests proving:

```text
lower-level mutation safety blocks at correct point
RecoverySafe=false blocks before mutation
missing/unready prerequisites block
unsafe HidHide state blocks
startup registration is verified before Center M roots are disabled
HidHide baseline is applied/verified before roots are disabled
root mutation/read-back failure is fail-closed
Enable-and-Restart stock restoration is unchanged
uninstall stock restoration is unchanged
```

## 20.3 StartupCoordinatorTests / UnsupportedHardwareStartupGateTests

Remove environment-assessment expectations and obsolete event-order entries.

Preserve:

```text
update gate
hardware stabilization
unsupported/indeterminate hardware handling
Center M startup-root authority branch
Partial/Unavailable passive behavior
physical topology stabilization
Disabled admission
stock PID1901 baseline
stale recovery behavior
```

## 20.4 FirstTimeSetupPolicyTests / provisioning tests

Remove compatibility input/reason cases.

Preserve package/provisioning/recovery/hardware/Steam safety cases.

## 20.5 SystemStatus / frontend / UI tests

Remove controller-software and compatibility expectations.

Update status snapshots and transport round-trip fixtures to the new shape.

Preserve Full1902 status evaluator, Addon warning, hardware, prerequisite, Steam, recovery, and setup tests.

## 20.6 Protocol tests

Assert:

```text
FrontendTransportProtocol.CurrentVersion == 22
```

where exact version assertions are intentionally maintained.

---

# 21. Required fresh reference-closure search

Before editing, and again before finalizing, search current source/tests for at least:

```text
IControllerEnvironmentAssessmentProvider
ControllerEnvironmentAssessmentProvider
ControllerEnvironmentAssessmentSnapshot
ControllerManagerKind
ControllerManagerClassification
ControllerManagerClassifier
ControllerEnvironmentCompatibility
ControllerSoftwareStatus
ControllerSoftwareKind
IControllerSoftwareStatusProvider
ClawTweaksSoftwareStatusProvider
HandheldCompanionSoftwareStatusProvider
MsiCenterMSoftwareStatusProvider
ClawTweaksInstallationProbe
ClawTweaksRuntimeDetector
HandheldCompanionRuntimeDetector
MsiCenterMRuntimeDetector
StartupControllerEnvironmentMapper
IsConflictingControllerEnvironment
FrontendSoftwareSnapshot
FrontendControllerEnvironmentStatus
ControllerSoftwareExpander
FormatControllerSoftwareStatus
CompatibilityUnsupported
CompatibilityIndeterminate
```

Expected post-cleanup result in current production/test source:

```text
no references to the deleted controller-software/manager/compatibility contracts
```

Historical docs may still contain those names and are not a failure.

`ControllerEnvironmentMode` may remain temporarily under the explicit Cleanup-E boundary above.

---

# 22. Lifecycle invariants that must not regress

This cleanup is acceptable only if the current supported product lifecycle remains intact.

## Center M Enabled boot

```text
startup roots Enabled
→ stock authority path
→ physical MSI topology stabilization
→ stock PID1901 baseline verification
→ no Full1902 owned PID1902 presentation
```

No Center M process-running scan is required to decide authority.

## Center M Disabled boot

```text
startup roots Disabled
→ Disabled admission
→ prerequisites verified
→ recovery evidence verified
→ deterministic HidHide baseline normalized/read-back verified
→ Full1902 physical ownership acquisition continues in current owner
```

No ClawTweaks/HHC/Winhanced scan occurs.

## Disable Center M + Restart

```text
real runtime safety
→ prerequisites/recovery/HidHide safety
→ startup registration proof
→ HidHide baseline proof
→ disable + verify three Center M startup roots
→ restart
```

No third-party manager scan occurs.

## Enable Center M + Restart / uninstall

Preserve exactly the existing stock-safe restoration sequence:

```text
retire virtual presentation
release Addon physical ownership
independently prove PID1901/XInput
release Addon HidHide baseline
Enable + verify Center M roots
stock authority established
```

No new shortcut or broad process-kill path.

## Sleep / Hibernate / Resume

No changes to:

```text
PowerTransitionWatcher
power epoch/barrier
stock-authority resume baseline
Full1902 owned-controller recovery
PnP return handling
```

---

# 23. Anti-overengineering requirements

Do not add:

```text
third-party controller manager registry
controller software service/facade
process/package authority cache
new compatibility enum under another name
new conflict state machine
new authority wrapper
background process watcher
periodic third-party process polling
```

Do not add locks/epochs/barriers for hypothetical timing combinations caused only by removing these detectors.

The supported product contract is simpler:

```text
startup-root state selects authority
actual hardware/driver/native operations determine whether that authority remains safe
```

One current authority fact is better than several indirect software-presence heuristics.

---

# 24. Validation

At minimum run:

```text
Debug build
Release build
full Release test suite
git diff --check
```

Also verify reference closure with `git grep` or equivalent for every symbol listed in section 21.

Validate frontend transport/build after protocol v22 change.

Recommended focused tests include retained suites around:

```text
StartupCoordinator
DisabledBootControllerAdmission
CenterMRebootAuthorityTransition
FirstTimeSetupPolicy
HidHide provisioning
Elevated prerequisite setup safety
SystemStatusProvider
FrontendSnapshotMapper
Frontend contract serialization
Named-pipe protocol handshake
StatusPresentation
Full1902 physical ownership/recovery
```

If hardware is available, a useful smoke matrix is:

```text
Center M Enabled → boot → stock PID1901 path
Center M Disabled → boot → Addon PID1902 path
Disable Center M + Restart
Enable Center M + Restart
sleep/resume once in each authority mode
```

The PR does not require installing ClawTweaks/HHC to prove the deletion. The intended result is that no product code consults them.

---

# 25. Completion criteria

Cleanup D is complete only when all of the following are true:

- production no longer constructs or calls third-party controller software detectors;
- Disabled-boot admission has no manager classification gate;
- Disable Center M + Restart has no manager conflict gate;
- first-time setup has no controller-environment compatibility gate;
- HidHide provisioning has no controller-environment compatibility gate;
- elevated prerequisite setup does not reconstruct MSI Center M software compatibility;
- `ControllerEnvironmentAssessmentProvider` / manager classifier / compatibility policy are gone;
- controller-software provider/probe graph is gone after reference closure;
- old MSI Center M runtime software-status detector is gone if no independent consumer is found;
- SystemStatusSnapshot no longer carries ControllerSoftware/Compatibility;
- FrontendStatusSnapshot no longer carries ControllerSoftware/ControllerEnvironment fields;
- Status UI no longer has Controller Software expander;
- frontend transport protocol is v22;
- Developer Environment Discovery does not keep production legacy DTOs alive;
- real topology stabilization, startup-root authority, prerequisite/recovery/HidHide/VIIPER safety remain intact;
- no replacement manager/compatibility abstraction is introduced;
- full tests/build pass.

Target architecture after this PR:

```text
one authority selector: Center M startup-root state
one physical Addon owner when Disabled
one stock baseline path when Enabled
one deterministic HidHide owner under Addon authority
one VIIPER presentation authority
actual lifecycle failures handled by their current owners
no software-presence arbitration layer
```

---

# 26. Follow-up cleanup boundary

After Cleanup D lands, perform a fresh code review for the next focused cleanup of the remaining startup-era abstractions, especially:

```text
ControllerEnvironmentMode
ControllerEnvironmentReadiness naming/signature if it can become plain MSI topology readiness
StartupResult.EnvironmentMode
StartupResult.LegacyRoutingAllowed
stale "routing" comments/reasons that now represent stock-authority or topology facts
```

Do not preemptively solve that entire cluster in Cleanup D unless a minimal compile closure requires a mechanical move.
