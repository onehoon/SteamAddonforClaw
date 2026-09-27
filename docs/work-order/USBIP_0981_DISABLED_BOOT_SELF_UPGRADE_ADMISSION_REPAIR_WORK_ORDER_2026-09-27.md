# Work Order — usbip-win2 0.9.8.1 Disabled-Boot Self-Upgrade Admission Repair

## Status

Production-blocker work order for the real MSI Claw upgrade failure reproduced on 2026-09-27 after Addon `0.1.293.0` adopted usbip-win2 `0.9.8.1` and VIIPER `973f072365cd40ac6c0a03d5d07b8eedf1a8b338`.

This is **not** a new controller architecture and is **not** a VIIPER ABI defect.

It is the same prerequisite-admission deadlock previously documented for the older `0.9.7.x -> 0.9.8.0` migration, now reproduced on the current Full1902 code with:

```text
installed usbip-win2 = 0.9.8.0
Addon target         = 0.9.8.1
Center M              = Disabled
Addon                 = 0.1.293.0
```

The fix must preserve Full1902 fail-close controller ownership while making the already-existing, user-consented prerequisite installer reachable.

---

## 1. Current baseline

Prepare the implementation against current `main`:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
baseline:   9a1b6f22eaffa43b608010756032bcc2d68280d5
commit:     VIIPER dependency update: 973f072 + usbip-win2 0.9.8.1 (#624)
```

Current pinned dependency state:

```text
usbip-win2 target       = 0.9.8.1
installer               = USBip-0.9.8.1-x64.exe
installer SHA-256       = 38CAD6D4432B52D5BB9409D9AD03B72FDFFC4ADA4CD3A48FBECA1A2752A8518A
VIIPER source revision  = 973f072365cd40ac6c0a03d5d07b8eedf1a8b338
```

Do not modify the VIIPER managed/native public ABI in this PR.

PR #624 already confirmed that the generated `libVIIPER.h` public contract is unchanged.

---

## 2. Required design authority

Read these before implementation and preserve their precedence:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Also read:

- `docs/work-order/USBIP_0909_DISABLED_BOOT_SELF_UPGRADE_ADMISSION_FIX_WORK_ORDER.md`
- `docs/usbip2/PR1_USBIP_WIN2_PREREQUISITE_SELF_UPGRADE_WORK_ORDER.md`

The older 0909 work order is the direct design ancestor of this fix. Reuse its narrow safety model; do not invent a generalized repair framework.

CTW integration is out of scope. This is the standalone Full1902 app.

---

## 3. Real hardware evidence

Primary log source:

```text
GoogleDrive/Addon/Log/0927
```

Relevant startup sequence after Velopack updated the Addon:

```text
2026-09-27 21:02:47
Update.PendingApplyDetected
Update.PendingApplyScheduled

2026-09-27 21:02:51
Application launch header.
Version=0.1.293.0

ControllerAdmission:
Disabled-boot controller admission blocked.
Reason="Prerequisites HidHide=Ready UsbIpWin2=Incompatible Viiper=Ready"

Status:
HidHide=Ready
UsbIpWin2=Incompatible
Viiper=Ready
AddonStatus=Indeterminate
```

The same state repeats across later controlled Runtime restarts at approximately:

```text
21:03:18
21:04:36
```

No prerequisite-install path appears anywhere in the 0927 logs:

```text
no PrerequisiteSetupPrompt
no PrerequisiteSetup requested
no usbip installation assessment log
no usbip provisioning receipt write
no prerequisite installer launch
```

This proves:

1. the new Addon correctly rejects installed `0.9.8.0` as runtime-incompatible with target `0.9.8.1`;
2. controller ownership correctly stays fail-closed;
3. the bounded prerequisite-repair path is unreachable.

This is a normal supported lifecycle and therefore a production blocker:

```text
old Addon owns controller with Center M Disabled
-> Addon self-updates
-> new Addon requires newer usbip package
-> old usbip is no longer runtime-compatible
-> controller ownership cannot start
-> prerequisite updater must repair usbip before ownership resumes
```

---

## 4. Current root cause

The package ordering logic is already correct.

Current code correctly classifies:

```text
installed 0.9.8.0
target    0.9.8.1

runtime prerequisite:
    Incompatible

installation assessment:
    UpdateRequired / OlderPackageVersion
```

Do not change that.

The deadlock is created by two other current behaviors.

### 4.1 Disabled-boot admission collapses prerequisite mismatch into generic Blocked

Current `DisabledBootControllerAdmission.Evaluate()`:

```csharp
if (!prerequisites.IsRoutingReady)
    return DisabledBootControllerAdmissionResult.Blocked(
        $"Prerequisites HidHide={prerequisites.HidHide.Status} " +
        $"UsbIpWin2={prerequisites.UsbIpWin2.Status} " +
        $"Viiper={prerequisites.Viiper.Status}");
```

The result does not preserve the fact that ownership stopped specifically before physical acquisition because prerequisites were not Ready.

### 4.2 FirstTimeSetupPolicy rejects RecoverySafe=false before it can offer the usbip repair

Current policy includes:

```csharp
if (!input.RecoverySafe)
    return new(
        FirstTimeSetupStatus.Blocked,
        FirstTimeSetupReason.RecoveryUnsafe,
        false);
```

All Center M Disabled startup paths intentionally carry:

```text
RecoverySafe = false
```

Therefore the real flow becomes:

```text
usbip 0.9.8.0 < target 0.9.8.1
-> runtime prerequisite Incompatible
-> DisabledBootAdmission = Blocked
-> RecoverySafe = false
-> FirstTimeSetup = Blocked / RecoveryUnsafe
-> CanInstallRequiredComponents = false
-> frontend never shows setup prompt
-> 0.9.8.1 installer never runs
```

This is a circular prerequisite-admission deadlock.

---

## 5. Safety rule

Do **not** solve this by making Disabled boot globally recovery-safe.

Do not change:

```text
StartupResult.RecoverySafe
RecoverySafetyState
PowerMutationGate
PowerTransitionCoordinator
SystemStatusProvider recovery semantics
sleep/resume barrier behavior
```

The narrow safe fact is:

> Disabled-boot ownership stopped at the prerequisite gate before PID1902 acquisition or VIIPER presentation could start.

That fact may permit only the existing usbip prerequisite repair path.

It must not grant general controller mutation authority.

Required behavior:

```text
Center M Disabled
+ admission stopped specifically because prerequisites are not Ready
+ usbip installation assessment = Missing or UpdateRequired
+ existing setup safety checks pass
-> offer existing prerequisite setup

but

Center M Disabled
+ topology failure / HidHide normalization failure / prerequisite inspection failure
-> remain blocked

and

usbip newer than target / malformed / unverified / inspection failed
-> remain blocked
-> never auto-downgrade
```

---

## 6. Required change A — restore a typed prerequisite-only admission outcome

Add one non-persisted in-memory outcome:

```csharp
internal enum DisabledBootAdmissionOutcome
{
    NotApplicable,
    Ready,
    PrerequisitesNotReady,
    Blocked,
}
```

`IsReady` must remain:

```csharp
internal bool IsReady =>
    Outcome == DisabledBootAdmissionOutcome.Ready;
```

Add a narrow result constructor/helper, for example:

```csharp
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
```

Then change only the known prerequisite-not-ready branch:

```csharp
if (!prerequisites.IsRoutingReady)
    return DisabledBootControllerAdmissionResult.PrerequisitesNotReady(prerequisites);
```

Keep these as genuine `Blocked`:

```text
prerequisite inspector throws
topology did not stabilize
HidHide baseline normalization throws
HidHide baseline readback/compliance fails
other actual admission failures
```

Do not create another controller authority state.

---

## 7. Required change B — derive one process-lifetime usbip repair window from StartupResult

Use the already-captured startup result.

Do not re-read Center M roots.

At the existing frontend/setup composition point in `AddonProcessHost`, derive:

```csharp
var allowUsbIpRepairWhileRecoveryUnsafe =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
    && startupResult.DisabledBootAdmission?.Outcome
        == DisabledBootAdmissionOutcome.PrerequisitesNotReady;
```

Pass this fact only into the existing `FrontendPrerequisiteSetupExecutor`.

Preferred shape:

```csharp
var setupExecutor = new FrontendPrerequisiteSetupExecutor(
    allowUsbIpRepairWhileRecoveryUnsafe);

_frontendControl = new InProcessAddonFrontendControl(
    ...,
    setupExecutor: setupExecutor,
    ...);
```

Use the actual current constructor/wiring location from `main`.

Do not put this fact into the public frontend named-pipe contract.

Do not persist it.

Do not recompute it from later status snapshots.

---

## 8. Required change C — carry the narrow flag into FirstTimeSetupPolicy

Extend the private/internal setup input only:

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

Store the constructor flag in `FrontendPrerequisiteSetupExecutor` and pass it into `FirstTimeSetupInput`.

No public API or transport protocol change is required.

---

## 9. Required change D — bypass RecoveryUnsafe only for a real usbip repair

The exception must be based on the **installation assessment**, not merely the runtime prerequisite status.

Use:

```csharp
var usbIpRepairRequired =
    input.UsbIpWin2Installation.Status is
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

Then allow the existing policy to continue through its normal checks.

Do not broaden the exception to:

```text
UsbIp ExistingUnverified
UsbIp Incompatible installation assessment
UsbIp Indeterminate
newer installed usbip
malformed installed version
package inspection failure
VIIPER failure
HidHide failure
arbitrary RecoverySafe=false
```

The important distinction remains:

```text
runtime prerequisite Incompatible
+ installation assessment UpdateRequired
= safe candidate for the existing bounded upgrade owner
```

A runtime `Incompatible` result alone is never permission to install.

---

## 10. Preserve setup safety precedence

The repair window must not bypass other existing setup fail-close checks.

Preserve:

```text
unsupported hardware
indeterminate hardware
corrupt provisioning receipt
indeterminate provisioning storage
unresolved InstallStarted
pending reboot
Steam/BPM active
newer/unverified/malformed usbip package
```

In particular, when Steam is active:

```text
repair window
+ UpdateRequired
+ Steam active
-> existing SteamActive behavior
-> no mutation
```

The frontend prompt may only become installable when the current policy would otherwise permit the prerequisite setup operation.

---

## 11. Preserve physical ownership fail-close behavior

`PrerequisitesNotReady` must never make the controller path Ready.

Existing controller startup must still require:

```csharp
startupResult.DisabledBootAdmission?.IsReady == true
```

Therefore this outcome must produce:

```text
no PID1901 -> PID1902 mutation
no DirectInput acquisition
no VIIPER attach
no live virtual presentation
```

until the exact usbip target is installed and a later startup admission reaches `Ready`.

Do not weaken this invariant to make testing easier.

---

## 12. Preserve existing elevated installer owner

Do not create a separate upgrade runner.

The current `ElevatedPrerequisiteSetup` must remain the only mutation owner.

Preserve its existing sequence:

```text
setup mutex
-> supported-hardware verification
-> trusted provisioning storage
-> receipt reconciliation
-> initial safety gate
-> installation assessment
-> receipt persisted before mutation
-> BeforeUsbIpInstall safety gate
-> download/acquire official installer
-> SHA-256 verification
-> bounded elevated installer launch
-> exact target package readback
-> result persisted
-> reboot requirement propagated
```

Do not bypass:

```text
USBip installer SHA verification
exact DisplayVersion verification
3010 / pending-reboot handling
failed-upgrade receipt behavior
no-downgrade behavior
```

---

## 13. Do not change dependency metadata in this PR

The correct target is already on `main`.

Do not change:

```text
BundledVersion = 0.9.8.1
InstallerFileName = USBip-0.9.8.1-x64.exe
InstallerSha256 = 38CAD6D4432B52D5BB9409D9AD03B72FDFFC4ADA4CD3A48FBECA1A2752A8518A
VIIPER revision = 973f072365cd40ac6c0a03d5d07b8eedf1a8b338
```

Do not modify:

```text
CanonicalViiperNativeApi
RequiredExports
CanonicalViiperNativeTypes
managed callback/rooting ABI
libVIIPER public C ABI
```

The dependency adoption itself is not the bug.

---

## 14. Required tests

### 14.1 DisabledBootControllerAdmissionTests

Add/update focused tests:

```text
all runtime prerequisites Ready
+ HidHide baseline compliant
-> Outcome=Ready
-> IsReady=true

UsbIp runtime Incompatible
-> Outcome=PrerequisitesNotReady
-> IsReady=false
-> HidHide baseline normalization not called after early prerequisite stop

prerequisite inspector throws
-> Outcome=Blocked

prerequisites Ready
+ HidHide normalization throws
-> Outcome=Blocked

prerequisites Ready
+ HidHide baseline noncompliant
-> Outcome=Blocked
```

The actual 0927 state must be represented:

```text
HidHide=Ready
UsbIpWin2=Incompatible
Viiper=Ready
-> PrerequisitesNotReady
```

### 14.2 FirstTimeSetupPolicyTests

Preserve:

```text
RecoverySafe=false
+ repair flag=false
-> Blocked / RecoveryUnsafe
```

Add:

```text
RecoverySafe=false
repair flag=true
UsbIpInstallation=UpdateRequired
InstalledVersion=0.9.8.0
TargetVersion=0.9.8.1
HidHide installed
Steam inactive
safe receipts/storage
-> Required
-> CanInstallRequiredComponents=true
```

Also add:

```text
repair flag=true + UsbIpInstallation=Missing
-> Required / installable
```

Fail closed for:

```text
repair flag=true + UsbIpInstallation=Incompatible
repair flag=true + UsbIpInstallation=ExistingUnverified
repair flag=true + UsbIpInstallation=Indeterminate
repair flag=true + exact 0.9.8.1 already Installed + RecoverySafe=false
```

The last case proves this is not a general RecoverySafe bypass.

Preserve/strengthen:

```text
Steam active -> no install
PendingReboot -> RestartRequired
InstallStarted unresolved -> Blocked
corrupt receipt/storage -> Blocked
unsupported/indeterminate hardware -> no mutation
```

### 14.3 Frontend/setup bridge

Use the smallest existing seam.

Prove:

```text
StartupResult:
  CenterMStartupState=Disabled
  DisabledBootAdmission=PrerequisitesNotReady
  RecoverySafe=false

package/runtime:
  installed usbip=0.9.8.0
  target usbip=0.9.8.1
  runtime usbip=Incompatible
  installation=UpdateRequired
  HidHide ready
  VIIPER ready
  Steam inactive

-> frontend snapshot CanInstallRequiredComponents=true
```

Prefer existing `FrontendPrerequisiteSetupBridgeTests` / `InProcessAddonFrontendControl` seams.

Do not create a new test-only manager.

### 14.4 Physical ownership non-regression

At the existing host/startup seam prove:

```text
DisabledBootAdmission=PrerequisitesNotReady
-> physical acquisition not started
-> presentation attach not started
```

Only `Ready` may start Full1902 ownership.

---

## 15. Expected files

Likely production files:

```text
src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs
src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs
```

Likely tests:

```text
tests/SteamInputAddonforClaw.Tests/DisabledBootControllerAdmissionTests.cs
tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendPrerequisiteSetupBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostStartupTests.cs
```

Touch additional files only when current code structure genuinely requires it.

Do not change installer metadata, VIIPER dependency files, power/recovery architecture, or public frontend contracts.

---

## 16. Expected logging after the fix

Before setup:

```text
[ControllerAdmission]
Result=PrerequisitesNotReady
Reason="Prerequisites HidHide=Ready UsbIpWin2=Incompatible Viiper=Ready"
```

When the UI evaluates setup:

```text
SetupStatus=Required
CanInstallRequiredComponents=true
```

After user acceptance, existing logs should show:

```text
[PrerequisiteSetupPrompt] Action=Accepted

[PrerequisiteSetup]
UsbIpWin2Status=Incompatible
SetupStatus=Required

usbip package:
Installed=True
Version=0.9.8.0

installation assessment:
Status=UpdateRequired
Reason=OlderPackageVersion

installer target:
0.9.8.1
```

Then the existing elevated helper should record download/acquisition, SHA verification, installer execution, exact `0.9.8.1` package evidence, and either `Installed` or `RebootRequired`.

Do not add high-frequency polling logs solely for this fix.

---

## 17. Hardware acceptance test

Use the same real MSI Claw that produced `GoogleDrive/Addon/Log/0927`.

Starting state:

```text
Center M Disabled
Addon 0.1.293.0 or later containing this fix
installed usbip-win2 = 0.9.8.0
target usbip-win2    = 0.9.8.1
```

Expected lifecycle:

```text
Addon starts
-> Disabled admission = PrerequisitesNotReady
-> no physical ownership / no VIIPER presentation
-> frontend setup prompt appears
-> user chooses Install
-> UAC helper starts
-> official USBip-0.9.8.1-x64.exe is acquired and verified
-> installer runs
-> exact package evidence becomes 0.9.8.1
-> reboot requirement is honored
-> complete Windows restart if requested/required
-> next Disabled startup
-> runtime prerequisite UsbIpWin2=Ready
-> DisabledBootAdmission=Ready
-> Full1902 physical ownership starts
-> exactly one virtual presentation becomes live
```

After the reboot, validate:

```text
physical input works
no physical + virtual double input
Xbox360 presentation works
SteamDeck presentation transition works
rumble/output works
controlled Runtime restart works
Sleep/Resume works
```

The usbip-win2 0.9.8.1 installer declares a restart requirement in its supported install lifecycle. Do not treat a newly written registry version before completing the required Windows restart as proof that the loaded driver matches the package.

---

## 18. No overengineering

Do not add:

- `PrerequisiteRepairManager`;
- `UsbIpUpgradeManager`;
- another controller authority;
- persisted repair mode;
- new receipt schema;
- retry scheduler;
- package-version range framework;
- driver hash/version authority;
- new service;
- watchdog;
- epoch/barrier/state machine;
- speculative race synchronization.

The supported real lifecycle is sufficient:

```text
startup captured once
-> admission classified once
-> prerequisite-only repair permission derived once
-> existing setup owner performs the bounded repair
-> reboot
-> next startup re-evaluates the real world
```

---

## 19. Verification

Run the repository's current normal checks.

At minimum:

```text
dotnet build -c Release
dotnet test -c Release
publish asset verification
startup/process smoke coverage
```

Also run focused tests for:

```text
DisabledBootControllerAdmission
FirstTimeSetupPolicy
FrontendPrerequisiteSetupBridge
AddonProcessHost startup/controller acquisition gating
```

Do not merge merely because unit tests can synthesize the state. The original real-hardware `0.9.8.0 -> 0.9.8.1` lifecycle must be re-tested because that is the production failure this PR exists to fix.

---

## 20. Acceptance criteria

The PR is acceptable only if all are true:

1. Installed usbip `0.9.8.0` remains runtime-incompatible with an Addon requiring `0.9.8.1`.
2. Controller ownership remains fail-closed while usbip is not Ready.
3. The Disabled startup result distinguishes prerequisite-only stop from real admission failure.
4. Only prerequisite-only stop creates the narrow usbip repair window.
5. `RecoverySafe` semantics are not weakened.
6. The repair window applies only to `Missing` or `UpdateRequired` usbip installation states.
7. Newer, malformed, unverified, or indeterminate usbip installations remain blocked and are never downgraded.
8. Steam-active, pending-reboot, corrupt-receipt, unsafe-storage, and unresolved-install protections remain intact.
9. The existing elevated prerequisite helper remains the only installer mutation owner.
10. No PID1902/DirectInput/VIIPER presentation starts from `PrerequisitesNotReady`.
11. No public frontend protocol or VIIPER ABI change is introduced.
12. Real MSI Claw validation proves `0.9.8.0 -> 0.9.8.1` reaches the installer and, after required restart, returns to normal Full1902 ownership.

---

## 21. Final invariant

After this fix:

```text
Center M Disabled
+ old but safely upgradeable usbip package
-> old package is rejected for Runtime use
-> controller ownership remains closed
-> existing prerequisite owner can repair usbip
-> exact target package is established
-> required restart completes
-> next startup can admit Full1902 normally
```

The product rule is:

> A prerequisite mismatch that intentionally prevents Full1902 controller acquisition must not also make its own bounded, user-consented repair path unreachable.
