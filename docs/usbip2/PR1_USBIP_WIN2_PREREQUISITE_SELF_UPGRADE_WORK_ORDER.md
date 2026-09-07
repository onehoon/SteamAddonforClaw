# Work Order — USBIP-WIN2 PR1: Prerequisite Self-Upgrade Path

## Status

Implementation work order for the first USBIP-WIN2 migration-preparation PR.

This PR does **not** adopt usbip-win2 0.9.8.0 yet.

Its purpose is to make the existing Addon prerequisite owner capable of recognizing and safely upgrading an **older installed usbip-win2 package to the version bundled by a newer Steam Addon for Claw release**.

The concrete future deployment flow this PR must enable is:

```text
User already has Steam Addon for Claw + usbip-win2 0.9.8.0
    ↓
A later Steam Addon for Claw Velopack release bundles usbip-win2 0.9.9.0
    ↓
Velopack updates the Addon and carries the new usbip installer with it
    ↓
updated Addon starts
    ↓
installed usbip-win2 0.9.8.0 < bundled usbip-win2 0.9.9.0
    ↓
installation assessment = UpdateRequired
    ↓
existing prerequisite setup safety / user-consent / elevation path
    ↓
bundled 0.9.9.0 installer runs
    ↓
exact target package version is verified
    ↓
restart handling / receipt reconciliation continues through the existing owner
```

Do not create a second updater, driver manager, dependency supervisor, or Velopack-specific prerequisite installer.

---

## Code-review baseline

This work order was prepared against:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     97995dbcfc0e1aad627432e27e8bec171914613e
commit msg: Keep only the five newest releases (#501)
```

Before implementation, rebase or refresh against the latest `main` and re-check every touched path. If `main` has materially changed the prerequisite contract since this baseline, preserve the current owner model and adapt the implementation rather than mechanically applying the examples below.

---

## Required design authorities

Read these together before editing code:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Also inspect the current implementations and focused tests for:

- `src/SteamInputAddonforClaw/Prerequisites/PrerequisiteContracts.cs`
- `src/SteamInputAddonforClaw/Prerequisites/ComponentInstallationAssessmentPolicy.cs`
- `src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs`
- `src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2PrerequisiteInspector.cs`
- `src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs`
- `src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs`
- `src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs`
- `src/SteamInputAddonforClaw/Prerequisites/PrerequisiteSetupExecutionPolicy.cs`
- `src/SteamInputAddonforClaw/Prerequisites/ProvisioningReconciliationPolicy.cs`
- `src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs`
- `src/SteamInputAddonforClaw/Updates/SilentUpdateService.cs`
- `src/SteamInputAddonforClaw/Startup/SilentUpdateGate.cs`
- `src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`
- `scripts/pack.ps1`
- `scripts/verify-publish-assets.ps1`
- `tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs`
- `tests/SteamInputAddonforClaw.Tests/RuntimePrerequisiteInspectorTests.cs`
- `tests/SteamInputAddonforClaw.Tests/UsbIpWin2ProvisioningReceiptStoreTests.cs`

Historical work orders are implementation context only. Current Full1902 authority documents and current `main` take precedence.

---

# 1. Goal

Add one missing prerequisite lifecycle state:

```text
installed usbip-win2 version < bundled usbip-win2 version
→ UpdateRequired
```

and route that state through the **existing** prerequisite installation owner.

The final policy must be:

```text
usbip package inspection failed
→ Indeterminate
→ fail closed

usbip package not installed
+ no runtime-only evidence
→ Missing
→ existing first-install path

installed version == bundled version
→ Installed
→ no installer run

installed version < bundled version
→ UpdateRequired
→ installer may run through existing prerequisite safety/elevation path

installed version > bundled version
→ Incompatible
→ do not auto-downgrade

installed version malformed / unparseable
→ Incompatible or otherwise fail closed
→ do not guess version ordering

runtime evidence without a trusted package entry
→ ExistingUnverified / existing fail-closed behavior
```

This is a package **upgrade capability**, not a relaxation of runtime compatibility.

---

# 2. Current defect

The current application already does the distribution half correctly:

```text
SteamInputAddonforClaw.csproj
→ Dependencies\UsbIpWin2\USBip-<version>-x64.exe is publish content

scripts/pack.ps1
→ Velopack packages the full publish directory
```

Therefore a future Addon release can already carry a newer usbip-win2 installer to the user.

The defect is what happens **after** the updated Addon starts.

Current `ComponentInstallationAssessmentPolicy` treats every installed usbip-win2 version mismatch as:

```text
Incompatible / UnexpectedPackageVersion
```

Current `ElevatedPrerequisiteSetup` only permits:

```text
Installed
Missing
```

and only launches the usbip installer when:

```text
Status == Missing
```

So a normal future state such as:

```text
installed = 0.9.8.0
bundled   = 0.9.9.0
```

is blocked even though the newer installer is already present inside the updated Addon installation.

That means a package-only usbip update cannot currently be delivered end-to-end through the existing Velopack release process.

PR1 fixes that missing lifecycle path.

---

# 3. Important separation: installation compatibility vs runtime compatibility

This distinction is mandatory.

The current `UsbIpWin2PrerequisiteInspector` parses the installed package version and requires it to equal `UsbIpWin2PackageMetadata.BundledVersion` before returning runtime `Ready`.

Preserve that strict runtime rule.

For example, after a future Addon update:

```text
Addon / VIIPER built for bundled usbip = 0.9.9.0
installed usbip                     = 0.9.8.0
```

runtime prerequisite status should remain conceptually:

```text
UsbIpWin2VersionUnsupported / Incompatible
```

until the package upgrade completes.

Do **not** change `UsbIpWin2PrerequisiteInspector` to treat an older package as runtime-ready merely because it is upgradeable.

The two questions are different:

```text
Can the current Addon safely USE this usbip runtime now?
→ runtime prerequisite inspector
→ exact supported version only

Can the Addon safely REPLACE this installed package with its newer bundled package?
→ component installation assessment
→ older version may be UpdateRequired
```

This separation is especially important because usbip-win2 driver/userspace/native ABI changes can accompany version updates.

The Addon must fail closed for controller routing until the supported package is actually established.

---

# 4. Scope

This PR must implement only the generic installed-older-than-bundled upgrade path.

Required changes:

1. add `ComponentInstallationStatus.UpdateRequired`;
2. teach `ComponentInstallationAssessmentPolicy.AssessUsbIp(...)` to distinguish older/exact/newer package versions;
3. allow `UpdateRequired` through `FirstTimeSetupPolicy` when the existing safety conditions permit setup;
4. preserve `SteamActive`, `RecoveryUnsafe`, pending-reboot, corrupt/uncertain receipt, and unsupported-hardware gates;
5. make a failed upgrade attempt retryable when the system still clearly reports an older package;
6. allow `ElevatedPrerequisiteSetup` to run the bundled usbip installer for `Missing` **or** `UpdateRequired`;
7. extend the usbip provisioning receipt so a future upgrade attempt records that it began from an older installed package instead of pretending it was a missing-package install;
8. keep existing exact-version post-install verification and reboot reconciliation;
9. add focused regression tests for upgrade/downgrade/retry/receipt behavior.

---

# 5. Explicit non-goals

Do **not** include any of the following in PR1.

## 5.1 Do not adopt usbip-win2 0.9.8.0 yet

Keep current package metadata exactly at the current baseline:

```csharp
BundledVersion = new(0, 9, 7, 7)
InstallerFileName = "USBip-0.9.7.7-x64.exe"
InstallerSha256 = "51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA"
```

Do not replace the installer binary.

Do not update:

- `SteamInputAddonforClaw.csproj` dependency filename;
- `scripts/verify-publish-assets.ps1` usbip filename/hash;
- publish-asset tests for a new usbip version;
- third-party notice version text solely for 0.9.8.0;
- any release artifact for 0.9.8.0.

Those belong to the later adoption PR after VIIPER 0.9.8.0 support is complete.

## 5.2 No VIIPER changes

Do not modify:

- `libVIIPER.dll`;
- VIIPER provenance;
- usbip native attach structs;
- imported-port ownership;
- attach/detach fallback policy;
- `ErrAttachmentOutcomeUnknown` behavior;
- low-latency receive mode.

The next coordinated work will handle the usbip-win2 0.9.8.0 ABI and low-latency policy in VIIPER.

## 5.3 No Velopack updater redesign

Do not change `VelopackUpdateClient`, `SilentUpdateService`, or `SilentUpdateGate` merely to install a driver.

Velopack's responsibility remains:

```text
deliver new Addon files
including the bundled prerequisite installer
restart the updated Addon
```

The prerequisite owner's responsibility remains:

```text
inspect installed system package
obtain user consent through the existing setup UI
apply existing safety gates
run the bounded elevated prerequisite helper
verify the result
```

Do not run driver installation from the Velopack updater process.

Do not introduce silent privilege escalation or a permanently elevated updater.

## 5.4 No generalized package manager

Do not add abstractions such as:

- `DependencyUpgradeManager`;
- `DriverUpgradeCoordinator`;
- `PackageVersionGraph`;
- `PrerequisiteMigrationManager`;
- generalized semver/range support;
- compatibility matrices for hypothetical future packages.

One concrete usbip package comparison inside the existing prerequisite owner is enough.

## 5.5 No automatic downgrade

A user/system with a package newer than this Addon bundles is not a supported automatic mutation target.

```text
installed > bundled
→ Incompatible
→ fail closed
→ no installer launch
```

Do not assume that installing the older bundled package is safe.

---

# 6. Required implementation

## 6.1 Add `UpdateRequired` without renumbering existing values

Current enum:

```csharp
internal enum ComponentInstallationStatus
{
    Missing,
    Installed,
    ExistingUnverified,
    Incompatible,
    Indeterminate
}
```

Append the new value rather than inserting it between existing values:

```csharp
internal enum ComponentInstallationStatus
{
    Missing,
    Installed,
    ExistingUnverified,
    Incompatible,
    Indeterminate,
    UpdateRequired
}
```

Reason:

- avoid unnecessary integer-value churn in tests/logging/any incidental serialization;
- keep the change additive;
- enum ordering has no authority semantics.

Do not create a second usbip-specific status enum.

---

## 6.2 Specialize usbip installation assessment

`ComponentInstallationAssessmentPolicy` currently shares one mismatch rule between HidHide and usbip.

Do not change HidHide upgrade policy in this PR.

Only `AssessUsbIp(...)` needs ordered version comparison.

Target semantics:

```text
package inspection failed
→ Indeterminate / PackageInspectionFailed

package installed + version parse failed
→ Incompatible / UnexpectedPackageVersion (or another clear fail-closed reason)

installed == bundled
→ Installed / ExpectedPackagePresent

installed < bundled
→ UpdateRequired / OlderPackageVersion

installed > bundled
→ Incompatible / UnexpectedPackageVersion or InstalledPackageNewerThanBundled

package missing + runtime Missing
→ Missing / PackageAndRuntimeMissing

package missing + runtime evidence exists
→ ExistingUnverified / RuntimeEvidenceWithoutPackage
```

A minimal implementation is preferred.

Example shape:

```csharp
internal static ComponentInstallationAssessment AssessUsbIp(
    UsbIpWin2PackageState package,
    PrerequisiteAssessment runtime,
    string expectedVersion)
{
    if (!package.InspectionSucceeded)
        return new(
            PrerequisiteKind.UsbIpWin2,
            ComponentInstallationStatus.Indeterminate,
            "PackageInspectionFailed",
            package.Version);

    if (package.Installed)
    {
        if (!Version.TryParse(package.Version, out var installedVersion)
            || !Version.TryParse(expectedVersion, out var bundledVersion))
        {
            return new(
                PrerequisiteKind.UsbIpWin2,
                ComponentInstallationStatus.Incompatible,
                "UnexpectedPackageVersion",
                package.Version);
        }

        var comparison = installedVersion.CompareTo(bundledVersion);

        if (comparison == 0)
            return new(
                PrerequisiteKind.UsbIpWin2,
                ComponentInstallationStatus.Installed,
                "ExpectedPackagePresent",
                package.Version);

        if (comparison < 0)
            return new(
                PrerequisiteKind.UsbIpWin2,
                ComponentInstallationStatus.UpdateRequired,
                "OlderPackageVersion",
                package.Version);

        return new(
            PrerequisiteKind.UsbIpWin2,
            ComponentInstallationStatus.Incompatible,
            "UnexpectedPackageVersion",
            package.Version);
    }

    return runtime.Status == PrerequisiteStatus.Missing
        ? new(
            PrerequisiteKind.UsbIpWin2,
            ComponentInstallationStatus.Missing,
            "PackageAndRuntimeMissing")
        : new(
            PrerequisiteKind.UsbIpWin2,
            ComponentInstallationStatus.ExistingUnverified,
            "RuntimeEvidenceWithoutPackage",
            package.Version);
}
```

Exact helper structure may differ.

Do not introduce a general version-range abstraction just to implement this comparison.

### Important expected-version invariant

`expectedVersion` comes from the Addon's own `UsbIpWin2PackageMetadata.BundledVersion`.

If implementation keeps the current string signature, a malformed `expectedVersion` must fail closed rather than treating an installed package as upgradeable.

Changing only `AssessUsbIp` to accept a `Version` target is also acceptable if it produces a smaller and clearer diff at its two production callers and tests.

Do not broadly refactor HidHide version comparison.

---

## 6.3 Keep `UsbIpWin2PrerequisiteInspector` exact-version strict

No production relaxation is required here.

Current contract must remain effectively:

```csharp
if (version != UsbIpWin2PackageMetadata.BundledVersion)
    return new(
        PrerequisiteKind.UsbIpWin2,
        PrerequisiteStatus.Incompatible,
        "UsbIpWin2VersionUnsupported",
        package.Version);
```

Add or update a focused test if necessary to make this separation explicit:

```text
installed old usbip package
+ otherwise healthy service/device/filter
→ runtime prerequisite remains Incompatible

same package state evaluated for installation
→ UpdateRequired
```

This prevents future refactors from incorrectly making older drivers runtime-compatible merely because they are installable upgrade sources.

---

## 6.4 First-time/setup policy must treat `UpdateRequired` as installable

The existing setup owner already gates prerequisite mutation on:

- supported hardware;
- trusted provisioning state;
- recovery safety;
- Steam activity;
- pending reboot;
- corrupt/uncertain receipts.

Reuse all of it.

Do not add a separate `UsbIpUpgradePolicy`.

Expected result when usbip is the only incomplete component:

```text
HidHide installation = Installed
usbip installation   = UpdateRequired
hardware             = Supported
RecoverySafe         = true
Steam active         = false
receipt state        = safe

→ FirstTimeSetupStatus.Required
→ CanInstallRequiredComponents = true
```

When Steam is active:

```text
usbip = UpdateRequired
Steam active = true

→ FirstTimeSetupStatus.Required
→ reason = existing SteamActive behavior
→ CanInstallRequiredComponents = false
```

When recovery is unsafe:

```text
usbip = UpdateRequired
RecoverySafe = false

→ Blocked
→ no installer
```

The existing generic setup dialog can remain unchanged in PR1. Do not add a new UI page or dedicated update wizard.

---

## 6.5 Fix failed-upgrade retry semantics

This is easy to miss and is mandatory.

Current `FirstTimeSetupPolicy` allows an `AttemptFailed` receipt to become retryable only when component installation status is:

```text
Missing
Installed
```

With the new state, this sequence would otherwise dead-end:

```text
installed old version
→ UpdateRequired
→ installer attempt fails
→ receipt = AttemptFailed
→ old version is still installed
→ installation assessment = UpdateRequired
→ current AttemptFailed rule sees neither Missing nor Installed
→ Blocked forever
```

That is not acceptable for a normal real installer failure where the older package remains intact.

Update only the usbip retry rule so that:

```text
AttemptFailed + UpdateRequired
→ retryable through the normal setup flow
```

Conceptual change:

```csharp
if (input.Provisioning.UsbIpWin2 == ComponentProvisioningState.AttemptFailed
    && input.UsbIpWin2Installation.Status is not (
        ComponentInstallationStatus.Missing
        or ComponentInstallationStatus.Installed
        or ComponentInstallationStatus.UpdateRequired))
{
    return new(
        FirstTimeSetupStatus.Blocked,
        FirstTimeSetupReason.ProvisioningUncertain,
        false);
}
```

### Do not weaken unresolved `InstallStarted`

Preserve the current fail-closed handling for a genuinely unresolved in-progress/unknown-outcome receipt.

Do **not** change this merely because `UpdateRequired` exists:

```text
receipt = InstallStarted
+ target package not proven
→ remain blocked / unresolved
```

A crash exactly during driver installation can leave system mutation outcome uncertain. That is a real lifecycle condition, so the existing unknown-outcome boundary remains justified.

If the exact target package is later proven by the existing reconciliation path, it may reconcile to `Provisioned` as it does today.

Do not add speculative automatic rollback/retry machinery for arbitrary partial driver-install states in this PR.

---

## 6.6 Elevated prerequisite owner must allow `UpdateRequired`

Current usbip block permits only:

```csharp
Installed
Missing
```

Change the allowed set to:

```text
Installed
Missing
UpdateRequired
```

and the installer-launch condition from:

```text
Missing only
```

to:

```text
Missing OR UpdateRequired
```

Conceptual shape:

```csharp
if (usbInstallation.Status is not (
    ComponentInstallationStatus.Installed
    or ComponentInstallationStatus.Missing
    or ComponentInstallationStatus.UpdateRequired))
{
    // existing blocked behavior
    return 3;
}

...

if (usbInstallation.Status is
    ComponentInstallationStatus.Missing
    or ComponentInstallationStatus.UpdateRequired)
{
    // existing receipt + safety gate + installer + post-install verification path
}
```

Do not create a second process-launch path for upgrades.

The exact same bounded owner must continue to perform:

```text
receipt persist
→ safety re-check
→ hash-verified bundled installer launch
→ bounded post-install package inspection
→ exact target-version verification
→ receipt outcome persist
→ reboot result propagation
```

### Existing safety gates remain mandatory

Do not bypass:

- elevated setup mutex;
- hardware compatibility preflight;
- trusted provisioning receipt storage;
- initial safety gate;
- `BeforeUsbIpInstall` safety gate;
- Steam-active protection;
- bundled installer SHA-256 validation.

An upgrade is not a reason to weaken first-install safety.

---

# 7. Provisioning receipt design

## 7.1 Current limitation

Current `UsbIpWin2ProvisioningReceipt` assumes every valid installer attempt began from:

```text
PreProvisioningStatus == PrerequisiteStatus.Missing
```

That accurately describes first installation but not an upgrade.

A future real upgrade may begin from:

```text
Package installed          = true
Installed version          = older than bundled
Runtime prerequisite state = Incompatible because exact supported version changed
Installation state         = UpdateRequired
```

Do not write a fake `Missing` receipt for that operation.

---

## 7.2 Extend the existing receipt; do not create a second upgrade receipt type

Keep one usbip receipt owner and one receipt file:

```text
%ProgramData%\SteamInputAddonforClaw\provisioning\usbip-win2.json
```

Recommended additive extension:

```csharp
internal sealed record UsbIpWin2ProvisioningReceipt(
    int SchemaVersion,
    UsbIpWin2ProvisioningReceiptState State,
    Guid AttemptId,
    string InstallerVersion,
    string InstallerSha256,
    PrerequisiteStatus PreProvisioningStatus,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ObservedInstalledVersion,
    string? FailureReason = null,
    int? InstallerExitCode = null,
    ComponentInstallationStatus PreInstallationStatus = ComponentInstallationStatus.Missing,
    string? PreviousInstalledVersion = null)
```

The exact names may be adjusted, but the receipt must be able to distinguish:

```text
first install
→ PreInstallationStatus = Missing
→ PreviousInstalledVersion = null

upgrade
→ PreInstallationStatus = UpdateRequired
→ PreviousInstalledVersion = actual older package version
```

For an upgrade, preserve the actual pre-install runtime prerequisite state in `PreProvisioningStatus`; do not force it to `Missing` solely to satisfy old validation.

---

## 7.3 Preserve existing v1 receipts if possible

The application is pre-release, but this PR is specifically preparing the future Velopack upgrade contract. Do not create avoidable receipt breakage across an ordinary Addon update.

Preferred implementation:

- keep `CurrentSchemaVersion = 1`;
- add the new fields as backward-compatible optional/defaulted fields;
- make missing `PreInstallationStatus` deserialize to existing `Missing` behavior;
- make missing `PreviousInstalledVersion` deserialize to `null`;
- keep existing v1 first-install receipts valid.

Do **not** bump to schema 2 merely because two additive fields were introduced.

Only introduce an explicit schema migration if current `System.Text.Json` behavior or tests prove the additive/defaulted form cannot safely load existing receipts.

Do not build a general receipt migration framework.

---

## 7.4 Receipt validation rules

Preserve all existing common validation:

```text
schema recognized
AttemptId != Guid.Empty
InstallerVersion parses
InstallerSha256 is exactly 64 hex chars
```

Then validate the operation origin.

Conceptual rules:

```text
PreInstallationStatus == Missing
→ PreProvisioningStatus must retain existing Missing contract
→ PreviousInstalledVersion should be null

PreInstallationStatus == UpdateRequired
→ PreviousInstalledVersion must parse
→ InstallerVersion must parse
→ PreviousInstalledVersion < InstallerVersion

anything else
→ invalid receipt
```

Example shape:

```csharp
private bool HasValidOrigin()
{
    if (!Version.TryParse(InstallerVersion, out var targetVersion))
        return false;

    return PreInstallationStatus switch
    {
        ComponentInstallationStatus.Missing =>
            PreProvisioningStatus == PrerequisiteStatus.Missing
            && PreviousInstalledVersion is null,

        ComponentInstallationStatus.UpdateRequired =>
            Version.TryParse(PreviousInstalledVersion, out var previousVersion)
            && previousVersion.CompareTo(targetVersion) < 0,

        _ => false
    };
}
```

Do not permit a receipt that describes:

```text
PreviousInstalledVersion >= InstallerVersion
```

as an upgrade attempt.

That would accidentally encode downgrade or no-op mutation as a supported upgrade.

---

## 7.5 Receipt creation during elevated setup

When installer execution is about to begin, persist the actual assessment that caused the mutation.

Conceptual creation:

```csharp
var receipt = new UsbIpWin2ProvisioningReceipt(
    UsbIpWin2ProvisioningReceipt.CurrentSchemaVersion,
    UsbIpWin2ProvisioningReceiptState.InstallStarted,
    Guid.NewGuid(),
    UsbIpWin2PackageMetadata.BundledVersion.ToString(),
    UsbIpWin2PackageMetadata.InstallerSha256,
    usbPrerequisite.Status,
    DateTimeOffset.UtcNow,
    null,
    null,
    PreInstallationStatus: usbInstallation.Status,
    PreviousInstalledVersion:
        usbInstallation.Status == ComponentInstallationStatus.UpdateRequired
            ? usbIp.Version
            : null);
```

Adapt positional/named arguments to the final record definition.

The important invariant is that a future support log can answer:

```text
Was this a first install or an upgrade?
What package version was present before the attempt?
What exact bundled installer version/hash was attempted?
What exact package version was observed afterward?
Did it provision, require reboot, fail, or get cancelled?
```

Do not add a separate journal for the same facts.

---

# 8. Reconciliation and post-install behavior

## 8.1 Exact target version remains the success boundary

Do not loosen:

- `WaitForUsbIpPostInstallEvidence(...)`;
- `PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(...)`;
- `ReconcileUsbIpReceipt(...)`;
- `ProvisioningReconciliationPolicy` exact receipt-version requirement.

An update attempt succeeds only when the installed package matches the receipt's target `InstallerVersion` exactly according to the existing supported comparison contract.

Example:

```text
receipt target = 0.9.9.0
observed       = 0.9.8.0
→ not Provisioned

receipt target = 0.9.9.0
observed       = 0.9.9.0
→ package target established
```

Do not treat “at least target version” as success.

The Addon supports the package it bundled and validated, not arbitrary newer driver ABI.

---

## 8.2 Restart-required behavior remains unchanged

Preserve the current installer exit-code contract:

```text
0
→ verify exact installed package
→ Provisioned when successful

3010
→ InstalledPendingReboot
→ propagate restart-required result
→ reconcile after boot change using exact package evidence

other exit code
→ AttemptFailed
```

Do not add automatic system reboot specifically for a usbip update in PR1.

Use the existing prerequisite restart UX/lifecycle.

---

## 8.3 Failed attempt with older package still present

Required normal retry case:

```text
before attempt: installed 0.9.8.0, target 0.9.9.0
installer fails
old 0.9.8.0 package remains installed
receipt = AttemptFailed
next assessment = UpdateRequired

→ setup becomes retryable when normal safety gates permit
```

No extra rollback is required.

The existing package is still a known older package, not an unknown result.

---

## 8.4 Unknown installer outcome remains fail-closed

Preserve this distinct case:

```text
receipt = InstallStarted
exact target package cannot be proven
```

Do not automatically retry merely because the registry still appears to show an old version.

An interrupted driver installer may have changed service/device/driver state before the package registration reached its final state.

That is a realistic operation-failure boundary and is allowed to remain fail-closed under the current provisioning contract.

Do not add a rollback state machine, epoch, retry daemon, or driver transaction abstraction to solve theoretical permutations around this boundary.

---

# 9. Velopack deployment contract after PR1

No Velopack code change is needed in this PR.

The intended later release sequence is:

```text
Release N
Addon bundle contains usbip A
user machine has usbip A

Release N+1
Addon bundle changes to usbip B
Velopack downloads/applies Release N+1
new installer B is now in Dependencies\UsbIpWin2
Addon metadata target = B

updated Addon startup
→ package probe sees A
→ installation assessment sees A < B
→ UpdateRequired
→ runtime usbip prerequisite remains incompatible until B exists
→ prerequisite setup is offered when existing policy allows mutation
→ elevated helper installs B
→ exact B verified
→ Addon can become runtime-ready
```

This keeps responsibilities clear:

```text
Velopack = application file delivery/restart
Prerequisite owner = Windows driver/package mutation
```

Driver mutation must retain the existing user-consent/UAC boundary.

Do not make the Velopack updater itself an administrator or driver owner.

---

# 10. Logging requirements

Do not add high-volume logging.

Existing prerequisite logs already include:

- package installed/version/inspection state;
- installation assessment status/reason;
- prerequisite runtime status/reason;
- installer attempt ID/version;
- installer exit code;
- post-install observed package version;
- receipt state.

Ensure a future upgrade produces enough evidence to reconstruct:

```text
InstalledVersion=<old>
BundledVersion=<target or visible through Version/receipt>
InstallationStatus=UpdateRequired
InstallationReason=OlderPackageVersion
PreInstallationStatus=UpdateRequired
PreviousInstalledVersion=<old>
InstallerVersion=<target>
ReceiptState=<...>
ObservedInstalledVersion=<...>
```

If the current log line already emits the necessary assessment/receipt values, reuse it.

Only add fields where the upgrade origin would otherwise be invisible.

Do not log on every ordinary runtime status refresh just because the installed version is old.

---

# 11. Required tests

Tests are part of the implementation, not optional follow-up.

## 11.1 Component installation version-order tests

Add focused cases for usbip:

```text
installed 0.9.7.7 / expected 0.9.7.7
→ Installed

installed 0.9.7.6 / expected 0.9.7.7
→ UpdateRequired

installed 0.9.7.8 / expected 0.9.7.7
→ Incompatible

installed malformed / expected 0.9.7.7
→ Incompatible or explicit fail-closed result

inspection failed
→ Indeterminate

package missing + runtime Missing
→ Missing

package missing + runtime evidence
→ ExistingUnverified
```

Use synthetic versions. PR1 must not change bundled metadata merely to exercise the policy.

---

## 11.2 Runtime prerequisite remains strict

Pin the required separation:

```text
package version = older than BundledVersion
service installed = true
device present = true
driver usable = true
filter installed = true

UsbIpWin2PrerequisiteInspector
→ Incompatible / UsbIpWin2VersionUnsupported
```

Then separately prove installation assessment for the same older package is `UpdateRequired`.

Do not change runtime tests to expect `Ready` for an old version.

---

## 11.3 First-time/setup policy tests

At minimum cover:

### Update is installable when safe

```text
HidHide = Installed
usbip = UpdateRequired
hardware = Supported
RecoverySafe = true
Steam inactive
receipt = safe

→ Required
→ CanInstallRequiredComponents = true
```

### Steam active blocks mutation

```text
usbip = UpdateRequired
Steam active

→ Required / SteamActive
→ CanInstallRequiredComponents = false
```

### Recovery unsafe blocks mutation

```text
usbip = UpdateRequired
RecoverySafe = false

→ Blocked
```

### Failed update is retryable

```text
Provisioning.UsbIpWin2 = AttemptFailed
UsbIpWin2Installation = UpdateRequired

→ Required
→ CanInstallRequiredComponents = true
```

### Unresolved update remains fail-closed

```text
Provisioning.UsbIpWin2 = InstallStarted
UsbIpWin2Installation = UpdateRequired

→ Blocked
```

### Newer-than-bundled stays blocked

```text
UsbIpWin2Installation = Incompatible

→ Blocked
→ no install offer
```

### Pending reboot behavior is unchanged

```text
Provisioning.UsbIpWin2 = PendingReboot
same boot session
→ RestartRequired
```

---

## 11.4 Receipt tests

Extend `UsbIpWin2ProvisioningReceiptStoreTests`.

Required cases:

```text
legacy/current v1 first-install receipt without new fields
→ still loads as valid

new first-install receipt
PreInstallationStatus=Missing
PreviousInstalledVersion=null
→ valid

upgrade receipt
PreInstallationStatus=UpdateRequired
PreviousInstalledVersion=older than InstallerVersion
→ valid

upgrade receipt with PreviousInstalledVersion == InstallerVersion
→ invalid

upgrade receipt with PreviousInstalledVersion > InstallerVersion
→ invalid

upgrade receipt with malformed PreviousInstalledVersion
→ invalid

receipt with PreInstallationStatus=Installed/Incompatible/Indeterminate
→ invalid as an installer-attempt origin
```

Also preserve existing atomic replace/temp-file tests.

Do not weaken receipt hash validation.

---

## 11.5 Existing provisioning reconciliation tests

Existing exact-version reconciliation cases must continue to pass.

Specifically preserve the principle represented by current tests:

```text
observed exact receipt target
→ may reconcile

observed older or newer than receipt target
→ Preserve / unresolved
```

Do not change reconciliation to ordered “>= target” semantics.

---

# 12. Expected production behavior after PR1

With the current bundle still at 0.9.7.7:

```text
normal current installation with 0.9.7.7
→ no user-visible change
→ no installer runs
```

Synthetic/older package:

```text
installed 0.9.7.6
bundled   0.9.7.7
→ UpdateRequired
→ existing prerequisite setup can upgrade when safe
```

Newer package:

```text
installed 0.9.7.8
bundled   0.9.7.7
→ Incompatible
→ no automatic downgrade
```

The real first production use of this path will be the later package-adoption PR, expected to model:

```text
installed 0.9.7.7
bundled   0.9.8.0
→ UpdateRequired
```

That later PR is where real hardware/package validation of `0.9.7.7 → 0.9.8.0` will occur.

---

# 13. Full1902 lifecycle constraints

This prerequisite change must not alter controller authority.

Preserve:

```text
Center M Enabled
→ MSI / stock authority
→ Addon controller path passive

Center M Disabled
→ Addon Runtime authority
→ desired physical PID1902
→ persistent HidHide baseline
→ VIIPER owned by Addon Runtime
```

A usbip version mismatch must not trigger:

- PID1901/PID1902 authority changes;
- HidHide ownership changes;
- Center M startup-root mutation;
- presentation attach as a workaround;
- VIIPER fallback around an unsupported driver version.

If the updated Addon requires a newer usbip package and the old package is still installed:

```text
runtime prerequisite = not ready
→ controller presentation must remain fail-closed according to the existing Runtime prerequisite gates
→ package upgrade is handled by prerequisite setup
```

Do not weaken lifecycle safety to keep the controller active across an ABI/version mismatch.

---

# 14. Race / overengineering boundary

Follow the repository review policy.

Handle realistic lifecycle outcomes:

- normal Velopack application restart;
- installer exit 0;
- installer exit 3010;
- actual installer failure;
- UAC/user cancellation through existing behavior;
- old package remaining after failed upgrade;
- restart after pending-reboot receipt;
- unresolved `InstallStarted` outcome;
- package inspection failure.

Do not add special synchronization for theoretical timing combinations between:

- a status refresh and one exact receipt write;
- arbitrary UI callbacks and one package probe;
- unrelated asynchronous tasks crossing one instruction boundary.

The existing setup mutex, atomic receipt replacement, safety gates, and exact-version reconciliation remain the authority.

Do not create another lock/state machine/epoch solely to make manufactured interleavings impossible.

---

# 15. Files expected to change

Expected production files:

```text
src/SteamInputAddonforClaw/Prerequisites/PrerequisiteContracts.cs
src/SteamInputAddonforClaw/Prerequisites/ComponentInstallationAssessmentPolicy.cs
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs
src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
```

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs
tests/SteamInputAddonforClaw.Tests/RuntimePrerequisiteInspectorTests.cs
tests/SteamInputAddonforClaw.Tests/UsbIpWin2ProvisioningReceiptStoreTests.cs
```

Additional focused test files are allowed if they make the policy clearer without introducing production abstractions.

Files that should normally **not** change in PR1:

```text
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2PrerequisiteInspector.cs
src/SteamInputAddonforClaw/Updates/VelopackUpdateClient.cs
src/SteamInputAddonforClaw/Updates/SilentUpdateService.cs
src/SteamInputAddonforClaw/Startup/SilentUpdateGate.cs
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
Dependencies/UsbIpWin2/USBip-0.9.7.7-x64.exe
Dependencies/Viiper/libVIIPER.dll
```

A test-only edit around `UsbIpWin2PrerequisiteInspector` is allowed; changing its production exact-version policy is not.

---

# 16. Validation

Run the repository's current supported commands. At minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

If current CI also builds/tests Debug, run the same focused/full suite in Debug before opening the PR.

Because PR1 does not replace any packaged binary, a real usbip driver installation is **not** required for merge acceptance.

Do not modify the developer machine's installed usbip version merely to make this PR pass.

The later 0.9.8.0 adoption PR must perform the real hardware/package migration validation.

---

# 17. Acceptance criteria

PR1 is complete only when all of the following are true.

1. `UsbIpWin2PackageMetadata.BundledVersion` is still `0.9.7.7`.
2. The bundled installer binary/hash are unchanged.
3. `ComponentInstallationStatus` has an additive `UpdateRequired` state.
4. An older installed usbip package is classified as `UpdateRequired` by installation assessment.
5. An exact installed package is still `Installed`.
6. A newer installed package is still fail-closed as `Incompatible`.
7. A malformed package version does not become upgradeable.
8. `UsbIpWin2PrerequisiteInspector` still rejects every non-bundled version for runtime use.
9. `FirstTimeSetupPolicy` offers prerequisite setup for `UpdateRequired` only when the existing safety conditions allow it.
10. Steam-active behavior still prevents prerequisite mutation.
11. Recovery-unsafe behavior still prevents prerequisite mutation.
12. `AttemptFailed + UpdateRequired` is retryable.
13. unresolved `InstallStarted + UpdateRequired` remains fail-closed.
14. Elevated setup accepts `UpdateRequired` and uses the same existing usbip installer path as first installation.
15. Upgrade does not bypass the setup mutex, hardware preflight, receipt storage trust check, safety gates, or installer hash check.
16. The receipt distinguishes first install from upgrade and records the prior installed version for an upgrade.
17. Existing v1 first-install receipts remain readable if the additive receipt design can support that without a migration framework.
18. Post-install success still requires the exact target installer version.
19. Exit code 3010 still becomes `InstalledPendingReboot` and reconciles only after the established reboot boundary.
20. No automatic downgrade exists.
21. No Velopack updater redesign exists.
22. No VIIPER/low-latency/0.9.8.0 work is mixed into this PR.
23. Full Release build/tests pass.
24. `git diff --check` is clean.

---

# 18. Follow-up sequence after PR1

Do not implement these in PR1, but preserve this dependency order.

```text
PR1 — SteamAddonforClaw
Prerequisite self-upgrade capability
(current work order)

    ↓

PR2 — onehoon/VIIPER
usbip-win2 0.9.8.0 native ABI support
+ low-latency receive mode always enabled
+ preserve exact imported-port ownership / fail-close

    ↓

PR3 — SteamAddonforClaw
replace bundled usbip 0.9.7.7 → 0.9.8.0
replace validated libVIIPER.dll
update metadata/hash/provenance/publish assertions
exercise PR1 using the real 0.9.7.7 → 0.9.8.0 upgrade
perform Full1902 hardware lifecycle validation
```

PR3 should be the first real integration proof that:

```text
existing user install
usbip 0.9.7.7
    ↓ Velopack Addon update
new Addon bundle
usbip 0.9.8.0
    ↓
UpdateRequired
    ↓
elevated prerequisite upgrade
    ↓
exact 0.9.8.0 verified
    ↓
new VIIPER runtime allowed to use it
```

That is the deployment contract PR1 exists to establish.
