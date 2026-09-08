# Work Order — USBIP-WIN2 0909: Fix Disabled-Boot Self-Upgrade Admission Deadlock

## Status

Focused production-blocker work order for the first real-hardware failure found after adopting usbip-win2 `0.9.8.0` and the matching VIIPER low-latency runtime.

This work order fixes the normal upgrade path:

```text
installed usbip-win2 = 0.9.7.7
bundled usbip-win2   = 0.9.8.0
Center M             = Disabled

→ Addon must offer the existing prerequisite setup flow
→ user accepts
→ bounded elevated helper installs 0.9.8.0
→ exact 0.9.8.0 package is verified
→ restart/reboot handling continues through the existing owner
```

The current build does **not** reach the installer. It blocks before prerequisite setup can be offered.

Do not treat this as a VIIPER attach/low-latency defect until the package upgrade has actually completed and the Runtime reaches the VIIPER attach path.

---

## Code-review baseline

Prepared against:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     b00d5046cc7a7fc9ce2f071fd67f77a181ddf0dd
commit msg: VIIPER dependency update: e00fbf0 (#503)
```

PR `#503` has already atomically adopted:

```text
usbip-win2 bundled version = 0.9.8.0
installer                  = USBip-0.9.8.0-x64.exe
installer SHA-256          = 81F426741F7EE2ED991FEBE24A22DACA8400B6AE2F171054E3FB404897E15D39
VIIPER commit              = e00fbf01277a2c354a32b0e54418a9bd917a05ae
VIIPER DLL SHA-256         = 0ECE53486DE369167B92482957FF0B41BB2CE760A2D534D066DC68BE33768F75
```

Before implementation, refresh against the latest `main`. Preserve the current Full1902 owner model if nearby code has moved.

---

# 1. Required design authorities

Read these together before editing controller/prerequisite startup code, using the authority order in the Full1902 README:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Also read the existing usbip upgrade work order because this is a correction to one missed Full1902 lifecycle interaction, not a replacement design:

- `docs/usbip2/PR1_USBIP_WIN2_PREREQUISITE_SELF_UPGRADE_WORK_ORDER.md`

Inspect current implementations and focused tests for at least:

- `src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs`
- `src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs`
- `src/SteamInputAddonforClaw/Prerequisites/ComponentInstallationAssessmentPolicy.cs`
- `src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2PrerequisiteInspector.cs`
- `src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs`
- `tests/SteamInputAddonforClaw.Tests/DisabledBootControllerAdmissionTests.cs`
- `tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs`
- `tests/SteamInputAddonforClaw.Tests/AddonProcessHostStartupTests.cs`
- relevant frontend/setup contract tests if touched by the final implementation shape.

Historical work orders are implementation context only. Current Full1902 authority documents and current `main` take precedence.

---

# 2. Real hardware incident

Hardware log set:

```text
C:\GoogleDrive\Addon\Log\0909
```

Observed Addon build:

```text
Version=0.1.224.0
Center M startup state=Disabled
hardware=Supported / msi.claw.cg3em
HidHide=Ready
VIIPER=Ready
installed usbip-win2 confirmed by user = 0.9.7.7
Addon bundled usbip-win2 = 0.9.8.0
```

The first startup shows:

```text
2026-09-08T22:18:26.941...
[ControllerAdmission]
Disabled-boot controller admission blocked.
Reason="Prerequisites HidHide=Ready UsbIpWin2=Incompatible Viiper=Ready"
```

The Runtime correctly refuses physical ownership:

```text
[ControllerOwnership]
Physical acquisition not started; Disabled-boot admission is not Ready.
Admission=Blocked
```

When the frontend is opened later, status still reports:

```text
HidHide=Ready
UsbIpWin2=Incompatible
Viiper=Ready
AddonStatus=Indeterminate
```

A controlled Runtime restart reproduces the same admission block:

```text
2026-09-09T00:56:10.369...
[ControllerAdmission]
Disabled-boot controller admission blocked.
Reason="Prerequisites HidHide=Ready UsbIpWin2=Incompatible Viiper=Ready"
```

No `PrerequisiteSetupPrompt` / usbip installer launch occurs.

This is a normal production lifecycle, not a synthetic race:

```text
old released Addon installed usbip 0.9.7.7
→ Velopack updates Addon to the build that bundles 0.9.8.0
→ Center M is still Disabled because Addon authority is durable
→ updated Runtime starts before the prerequisite package is upgraded
```

The user is then stranded with no Addon controller presentation and no automatic path to install the package the new Addon already carries.

---

# 3. Root cause

The package/install assessment itself is correct.

Current usbip policy correctly separates:

```text
runtime compatibility
0.9.7.7 != bundled 0.9.8.0
→ PrerequisiteStatus.Incompatible
→ controller Runtime must NOT use that usbip runtime

installation compatibility
0.9.7.7 < bundled 0.9.8.0
→ ComponentInstallationStatus.UpdateRequired
→ existing prerequisite owner may replace it
```

Do not weaken that separation.

The deadlock comes from combining Full1902 Disabled-boot admission with the old first-time setup recovery gate.

Current flow is effectively:

```text
Center M Disabled
    ↓
DisabledBootControllerAdmission.Evaluate()
    ↓
runtime prerequisites must all be Ready
    ↓
usbip 0.9.7.7 is runtime-Incompatible for a 0.9.8.0 Addon
    ↓
admission = Blocked
    ↓
StartupCoordinator.RunDisabledBootAdmissionAsync(...)
returns StartupResult.RecoverySafe = false
    ↓
Addon Runtime stays alive, but physical ownership is intentionally not started
    ↓
FrontendPrerequisiteSetupExecutor evaluates package state
usbip installation = UpdateRequired
    ↓
FirstTimeSetupPolicy hits !RecoverySafe first
    ↓
RecoveryUnsafe / Blocked
CanInstallRequiredComponents = false
    ↓
no setup prompt
    ↓
0.9.8.0 installer never runs
```

So the product currently says both:

```text
"0.9.7.7 cannot be used until upgraded"
```

and:

```text
"the upgrade cannot run because controller recovery is not safe"
```

but the controller recovery fact is false precisely because the prerequisite was intentionally blocked before physical ownership began.

This is a circular admission deadlock.

---

# 4. Safety interpretation

The correct fix is **not** to mark the entire Disabled Runtime as recovery-safe.

Do not change:

```text
StartupResult.RecoverySafe
RecoverySafetyState
PowerMutationGate
PowerTransitionCoordinator
SystemStatusProvider recovery semantics
```

merely to make the installer prompt appear.

Those facts are consumed outside prerequisite setup and must not become a disguised package-install permission flag.

The narrow product fact we need is already present in the startup lifecycle:

> **The Disabled-boot controller admission stopped at the prerequisite stage, so this process lifetime will not begin PID1902 physical acquisition or attach a live VIIPER presentation.**

That is exactly the safe window in which the existing prerequisite owner may repair an installable usbip package.

The existing elevated helper must still independently enforce its current mutation safety gates immediately before the installer runs.

Therefore the intended model is:

```text
RecoverySafe=false
+ exact Center M Disabled startup
+ Disabled-boot admission stopped because prerequisites are not Ready
+ usbip installation assessment is Missing or UpdateRequired
+ all existing setup receipt/hardware/Steam safety gates pass

→ prerequisite repair may be offered

but

RecoverySafe=false
+ admission blocked for topology/HidHide/other safety reason

→ remain blocked

and

usbip installed version > bundled
or malformed/unverified package

→ remain blocked
→ never auto-downgrade
```

Do not add a second controller authority, a new persisted mode, a package manager, or a generalized recovery state machine.

---

# 5. Required implementation approach

Use the smallest explicit extension of the **existing Disabled-boot admission result** so prerequisite setup can distinguish:

```text
controller admission cannot proceed because Runtime prerequisites are not Ready
```

from:

```text
controller admission cannot proceed because another safety fact failed
```

A preferred minimal shape is to add one in-memory admission outcome:

```csharp
internal enum DisabledBootAdmissionOutcome
{
    NotApplicable,
    Ready,
    PrerequisitesNotReady,
    Blocked,
}
```

This is **not** a third controller-authority mode.

It is only an in-process classification of why the next PID1902 ownership stage may not run.

`IsReady` must remain true only for `Ready`.

Do not persist this outcome.

Do not add epochs, locks, managers, inspectors, or a second startup state database.

If the latest `main` has evolved such that the same distinction is already available through an equally narrow typed fact, reuse it instead of adding a duplicate enum value.

---

# 6. Required change A — classify prerequisite-only admission stop explicitly

Current `DisabledBootControllerAdmission.Evaluate()` returns generic `Blocked(...)` immediately when `prerequisites.IsRoutingReady == false`.

Change only that branch to return the typed prerequisite-not-ready outcome.

Conceptual shape:

```csharp
internal sealed record DisabledBootControllerAdmissionResult(
    DisabledBootAdmissionOutcome Outcome,
    string Reason)
{
    internal bool IsReady => Outcome == DisabledBootAdmissionOutcome.Ready;

    internal static DisabledBootControllerAdmissionResult PrerequisitesNotReady(
        RuntimePrerequisiteAssessment prerequisites)
    {
        var reason =
            $"Prerequisites HidHide={prerequisites.HidHide.Status} " +
            $"UsbIpWin2={prerequisites.UsbIpWin2.Status} " +
            $"Viiper={prerequisites.Viiper.Status}";

        AppLog.Warn(
            "ControllerAdmission",
            "Disabled-boot controller admission requires prerequisite repair.",
            null,
            ("Result", "PrerequisitesNotReady"),
            ("Reason", reason));

        return new(
            DisabledBootAdmissionOutcome.PrerequisitesNotReady,
            reason);
    }
}
```

Then:

```csharp
if (!prerequisites.IsRoutingReady)
    return DisabledBootControllerAdmissionResult.PrerequisitesNotReady(prerequisites);
```

Keep genuine failures as `Blocked`, including:

```text
prerequisite inspection threw / unavailable
controller topology not stable
HidHide baseline normalization threw
HidHide baseline normalization/readback not compliant
```

Do not convert those to prerequisite-repair admission.

### Physical ownership behavior must remain unchanged

`TryStartDisabledModeControllerAsync(...)` already requires:

```csharp
startupResult.DisabledBootAdmission?.IsReady == true
```

Therefore `PrerequisitesNotReady` must still result in:

```text
no VIIPER runtime attach
no PID1901 -> PID1902 mode mutation
no DirectInput acquisition
no live virtual presentation
```

The release seam for `Enable Center M and Restart` must remain available exactly as it is today.

---

# 7. Required change B — pass the startup admission fact only to the existing prerequisite evaluator

Do not change global recovery state.

At `AddonProcessHost` composition time, derive one process-lifetime prerequisite-repair permission from the already-captured startup result:

```csharp
var disabledBootPrerequisiteRepairWindow =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
    && startupResult.DisabledBootAdmission?.Outcome
        == DisabledBootAdmissionOutcome.PrerequisitesNotReady;
```

Pass this only into the existing `FrontendPrerequisiteSetupExecutor`.

Preferred minimal shape:

```csharp
var setupExecutor = new FrontendPrerequisiteSetupExecutor(
    allowUsbIpRepairWhileRecoveryUnsafe:
        disabledBootPrerequisiteRepairWindow);

_frontendControl = new InProcessAddonFrontendControl(
    composition.StartupSettings,
    composition.StatusProvider,
    _runtimeHost,
    new DeveloperTestModeState(),
    setupExecutor: setupExecutor,
    ...);
```

Exact constructor placement may differ after refreshing latest `main`.

Do not add the fact to the public frontend protocol unless the current code structure genuinely requires it. This is an in-process setup-policy input, not user-facing authority state.

Do not re-read Center M startup roots just to reconstruct the startup classification. Use the existing `StartupResult` fact already owned by `AddonProcessHost`.

---

# 8. Required change C — allow only an actually installable usbip repair to bypass the RecoveryUnsafe setup block

Extend the internal `FirstTimeSetupInput` with one narrow, default-false input, for example:

```csharp
internal sealed record FirstTimeSetupInput(
    HardwareCompatibilityAssessment HardwareCompatibility,
    bool RecoverySafe,
    SteamSessionState Steam,
    PrerequisiteAssessment HidHide,
    PrerequisiteAssessment UsbIpWin2,
    ComponentInstallationAssessment HidHideInstallation,
    ComponentInstallationAssessment UsbIpWin2Installation,
    ProvisioningStateAssessment Provisioning,
    bool AllowUsbIpRepairWhileRecoveryUnsafe = false);
```

The exact name may differ, but the meaning must stay narrow:

```text
The startup controller admission stopped at prerequisites before physical ownership began,
and the setup owner may evaluate whether usbip is an installable repair target.
```

Then preserve the current RecoveryUnsafe fail-close rule except for an actual usbip install/upgrade operation:

```csharp
var usbIpRepairRequired = input.UsbIpWin2Installation.Status is
    ComponentInstallationStatus.Missing
    or ComponentInstallationStatus.UpdateRequired;

if (!input.RecoverySafe
    && !(input.AllowUsbIpRepairWhileRecoveryUnsafe && usbIpRepairRequired))
{
    return new(
        FirstTimeSetupStatus.Blocked,
        FirstTimeSetupReason.RecoveryUnsafe,
        false);
}
```

### Important: do not broaden this exception

This PR is specifically for the usbip self-upgrade deadlock.

Do **not** use this exception for:

```text
UsbIp ExistingUnverified
UsbIp Incompatible because installed version is newer than bundled
UsbIp malformed version
UsbIp package inspection failure
VIIPER Missing/Unusable/Indeterminate
HidHide baseline normalization failure
controller topology failure
arbitrary RecoverySafe=false states
```

Do not make all `RecoveryUnsafe` prerequisite setup installable.

Do not make `PrerequisiteStatus.Incompatible` itself installable. Only the existing **installation assessment** may say `UpdateRequired`.

This preserves the key PR1 distinction:

```text
runtime prerequisite = Incompatible
installation assessment = UpdateRequired
```

---

# 9. Required change D — preserve every existing mutation safety gate

The fix is only about reaching the existing setup owner.

Do not remove or relax any existing checks in `ElevatedPrerequisiteSetup`.

The actual usbip installer run must still pass through:

```text
setup mutex
→ supported-hardware preflight
→ trusted provisioning storage
→ existing provisioning receipt reconciliation
→ Initial safety gate
→ installation assessment == Missing or UpdateRequired
→ receipt persisted before mutation
→ BeforeUsbIpInstall safety gate
→ bundled installer exists
→ bundled installer SHA-256 verified
→ bounded process launch
→ exact target package post-install verification
→ receipt outcome persisted
→ reboot requirement propagated
```

Preserve Steam/BPM protection.

Preserve exact target-version verification.

Preserve failed-upgrade retry behavior.

Preserve unresolved `InstallStarted` fail-close behavior.

Preserve no-downgrade behavior.

No separate usbip upgrade runner is required.

---

# 10. Do not change the runtime compatibility rule

`UsbIpWin2PrerequisiteInspector` must remain strict:

```text
installed 0.9.7.7
Addon expects 0.9.8.0
→ runtime prerequisite Incompatible
→ controller ownership does not start
```

Only after the exact installed package becomes `0.9.8.0` may runtime readiness be evaluated as Ready.

Do not temporarily accept 0.9.7.7 so routing can continue during the upgrade.

The new VIIPER binary was intentionally built for the usbip-win2 `0.9.8.0` ABI and low-latency request contract.

Fail closed until the matching system package is established.

---

# 11. Do not change RecoverySafety / power semantics

Explicitly out of scope:

```text
StartupResult.RecoverySafe=true on Disabled boot
RecoverySafetyState behavior changes
PowerMutationGate opening changes
PowerTransitionCoordinator changes
resume epoch/barrier changes
SystemStatusProvider recovery logic changes
Full1902 physical owner changes
VIIPER attach/detach changes
HidHide deterministic baseline algorithm changes
Center M authority-transition changes
```

A tempting but incorrect patch is:

```csharp
// DO NOT DO THIS
return new StartupResult(
    true,
    RecoverySafe: admission.Outcome == DisabledBootAdmissionOutcome.PrerequisitesNotReady,
    ...);
```

That would overload a broader safety fact merely to grant installer permission and could affect unrelated Runtime/power behavior.

Keep the exception inside the prerequisite setup decision where it belongs.

---

# 12. No overengineering

Do not add:

- `DriverUpgradeManager`;
- `PrerequisiteRepairCoordinator`;
- new Windows service;
- watchdog/supervisor;
- controller authority enum beyond the existing Center M Enabled/Disabled authority;
- persisted upgrade mode;
- additional receipt schema solely for this fix;
- retry scheduler;
- package version range abstraction;
- extra lock/epoch/barrier/state machine;
- auto-uninstall-before-upgrade manager;
- 0.9.7.7 compatibility bridge;
- dual VIIPER ABI selection.

The current installer already self-replaces the old usbip package and PR1 already owns the upgrade receipt and exact-version verification.

This PR only repairs the blocked entry path.

---

# 13. Required tests

## 13.1 DisabledBootControllerAdmission tests

Update/add focused tests proving:

### Exact runtime prerequisites Ready

```text
HidHide=Ready
UsbIp=Ready
Viiper=Ready
HidHide baseline compliant
→ Outcome=Ready
→ IsReady=true
```

### Runtime prerequisite mismatch

```text
HidHide=Ready
UsbIp=Incompatible
Viiper=Ready
→ Outcome=PrerequisitesNotReady
→ IsReady=false
→ HidHide baseline normalization must not run after the early prerequisite stop
```

### Genuine admission failures remain Blocked

At minimum:

```text
prerequisite inspector throws
→ Blocked

prerequisites Ready
+ HidHide baseline normalization throws
→ Blocked

prerequisites Ready
+ HidHide baseline readback not compliant
→ Blocked
```

Do not reclassify these as setup-repair windows.

---

## 13.2 FirstTimeSetupPolicy tests

Preserve the existing generic rule:

```text
RecoverySafe=false
+ no explicit Disabled prerequisite-repair permission
→ Blocked
```

Keep or strengthen the current test equivalent to:

```csharp
RecoveryUnsafeUsbIpUpgrade_IsBlocked()
```

Then add the real Full1902 exception:

```text
RecoverySafe=false
AllowUsbIpRepairWhileRecoveryUnsafe=true
UsbIpInstallation=UpdateRequired (0.9.7.7 < 0.9.8.0)
HidHideInstallation=Installed
hardware=Supported
Steam inactive
receipts safe

→ FirstTimeSetupStatus.Required
→ CanInstallRequiredComponents=true
```

Also prove:

```text
same repair window + UsbIpInstallation=Missing
→ Required / installable
```

and fail closed for:

```text
repair window + UsbIpInstallation=Incompatible (newer package)
→ Blocked / not installable

repair window + UsbIpInstallation=ExistingUnverified
→ Blocked / not installable

repair window + UsbIpInstallation=Indeterminate
→ Blocked / not installable

repair window + usbip exact Installed + RecoverySafe=false
→ still Blocked
```

The last test is important: the new startup fact is **not** permission to ignore recovery safety once there is no package repair to perform.

### Preserve existing safety precedence

Add/retain coverage for:

```text
repair window + Steam active
→ Required with existing SteamActive reason
→ CanInstallRequiredComponents=false

repair window + unresolved InstallStarted receipt
→ Blocked

repair window + corrupt/indeterminate provisioning receipt
→ Blocked

repair window + PendingReboot
→ RestartRequired

unsupported/indeterminate hardware
→ never offer mutation
```

---

## 13.3 Frontend/setup integration test

Add one focused test at the smallest current seam proving the actual product path, not only isolated policy methods.

Target scenario:

```text
StartupResult:
  CenterMStartupState = Disabled
  DisabledBootAdmission = PrerequisitesNotReady
  RecoverySafe = false

status/package facts:
  HidHide package/runtime = ready
  usbip installed package = 0.9.7.7
  bundled target = 0.9.8.0
  usbip runtime = Incompatible / UsbIpWin2VersionUnsupported
  VIIPER = Ready
  Steam inactive

→ frontend status snapshot exposes CanInstallRequiredComponents=true
```

If current test seams make a full `AddonProcessHost` test disproportionately large, a focused `FrontendPrerequisiteSetupExecutor` + `FirstTimeSetupPolicy` integration test is acceptable.

Do not build new test-only product abstractions solely to simulate the whole UI.

---

## 13.4 Physical ownership non-regression

Prove that `PrerequisitesNotReady` does **not** start controller acquisition.

Expected behavior remains:

```text
PrerequisitesNotReady
→ Create/release seam may exist as current design requires
→ AcquireAsync not called
→ VIIPER live presentation not attached
```

Use the existing startup/host seam if a focused test already exists.

Do not add theoretical timing tests around a setup dialog racing a one-instruction controller acquire. The startup admission is process-lifetime input and physical acquisition is already gated by `IsReady`.

---

# 14. Build/test validation

Run the repository's normal formatting/build/test verification required by current `main`.

At minimum verify:

```text
Release build succeeds
0 warnings / 0 errors if that is the current repository baseline
full SteamInputAddonforClaw.Tests suite passes
focused prerequisite/admission tests pass
publish asset verification remains unchanged and passes
```

Do not modify bundled installer/hash/provenance as part of this bug fix unless current main has changed unexpectedly.

Expected metadata remains:

```text
BundledVersion = 0.9.8.0
InstallerFileName = USBip-0.9.8.0-x64.exe
InstallerSha256 = 81F426741F7EE2ED991FEBE24A22DACA8400B6AE2F171054E3FB404897E15D39
```

---

# 15. Real hardware acceptance test

This PR is not complete until the original upgrade lifecycle is retested on the MSI Claw.

Start from the exact reproduced state:

```text
Center M = Disabled
installed usbip-win2 = 0.9.7.7
updated Addon bundles 0.9.8.0
```

Expected sequence:

```text
updated Addon starts
→ controller admission reports prerequisites not ready
→ no PID1902 physical acquisition / no VIIPER presentation yet
→ frontend setup prompt is offered
→ user accepts
→ UAC/elevated prerequisite helper runs
→ 0.9.8.0 bundled installer launches
→ package probe confirms exact 0.9.8.0
→ result is Installed or RebootRequired according to real installer outcome
→ if restart/reboot required, complete it
→ next Disabled boot admission reaches Ready
→ physical PID1902 ownership starts
→ canonical VIIPER Runtime initializes
→ Xbox360 presentation attaches when Steam/BPM inactive
→ SteamDeck presentation can be selected when Steam/BPM active
```

After the upgrade succeeds, validate at minimum:

```text
physical input works
no physical + virtual double input
Xbox360 presentation works
SteamDeck presentation transition works
rumble/output works
controlled Runtime restart recovers
Sleep/Resume recovers
```

If the package upgrades successfully but VIIPER attach then fails, treat that as a **separate post-upgrade runtime/ABI investigation**. Do not conflate it with this admission fix.

---

# 16. Expected logging after the fix

Before setup:

```text
[ControllerAdmission]
... Result=PrerequisitesNotReady
... UsbIpWin2=Incompatible
```

Then, after frontend activation and user consent, existing setup logs should show the real package assessment and installer path, conceptually:

```text
usbip-win2 package probe completed.
Installed=True Version=0.9.7.7

usbip-win2 prerequisite probe completed.
Status=Incompatible Reason=UsbIpWin2VersionUnsupported

usbip-win2 installation assessment completed.
InstallationStatus=UpdateRequired
InstallationReason=OlderPackageVersion
PackageVersion=0.9.7.7

usbip-win2 installation receipt persisted.
Version=0.9.8.0
PreInstallationStatus=UpdateRequired
PreviousInstalledVersion=0.9.7.7

Prerequisite installer launch started.
Component=usbip-win2

Prerequisite installer process exited.
...

usbip-win2 installation result recorded.
PackageVersion=0.9.8.0
```

Do not add high-frequency duplicate status logs solely for this fix.

---

# 17. Acceptance criteria

The PR is acceptable only when all of the following are true:

1. A real Disabled-mode user with installed usbip `0.9.7.7` and bundled target `0.9.8.0` is no longer stranded.
2. Runtime compatibility remains exact-version fail-closed before the upgrade.
3. Disabled-boot physical ownership does not start while prerequisites are not Ready.
4. Prerequisite setup is offered only because startup admission stopped at prerequisites before ownership began.
5. The exception applies only when usbip installation assessment is `Missing` or `UpdateRequired`.
6. Newer/malformed/unverified usbip installations remain non-installable and are never auto-downgraded.
7. Real recovery-unsafe states still block prerequisite mutation.
8. `StartupResult.RecoverySafe`, `RecoverySafetyState`, power gates, and resume logic are not relaxed to solve the UI/setup deadlock.
9. Existing elevated helper safety, receipt, hash, exact-version verification, and reboot handling remain intact.
10. No new persisted controller/package authority is introduced.
11. Full tests pass.
12. Real MSI Claw test proves `0.9.7.7 → 0.9.8.0` reaches the installer and next boot can proceed to the Full1902 controller path.

---

# 18. Final product invariant

After this fix:

```text
Center M Disabled
+ old but safely upgradeable usbip package

→ old package is NOT runtime-compatible
→ controller ownership remains fail-closed
→ but the Addon can still repair the prerequisite through its existing bounded setup owner
→ exact bundled package is established
→ next controller admission can proceed normally
```

The invariant is:

> **A prerequisite that intentionally prevents Full1902 controller acquisition must not also make its own bounded repair path unreachable. The Addon must preserve controller fail-close safety while still allowing the existing, user-consented prerequisite owner to replace an older supported usbip package before any physical ownership or virtual presentation begins.**
