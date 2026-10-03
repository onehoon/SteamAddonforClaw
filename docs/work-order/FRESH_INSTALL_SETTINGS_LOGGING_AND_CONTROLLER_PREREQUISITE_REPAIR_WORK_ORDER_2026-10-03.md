# Work Order — Fresh-Install Settings Bootstrap, Default Info Logging, and HidHide/usbip-win2 Prerequisite Repair

> **Repository:** onehoon/SteamAddonforClaw  
> **Reviewed baseline:** main@3e4daa7d44c7d5edcbbd92f5c1f938da8f693595  
> **Date:** 2026-10-03  
> **Scope:** fresh-install settings/logging bootstrap plus the real first-install HidHide + usbip-win2 prerequisite setup deadlock  
> **Architecture:** standalone Full1902 application; CTW integration is out of scope

---

## 1. Goal

Fix two production-visible fresh-install defects without introducing a second settings owner, prerequisite manager, controller authority, installer state machine, or background repair service.

Required product behavior:

~~~text
brand-new Addon install
+ no existing settings.json
+ supported MSI Claw

→ create the canonical settings.json with the normal default settings
→ default LogLevel = Info
→ startup logging exists immediately on the first run
→ explicit user LogLevel=Off remains authoritative on later runs
~~~

and:

~~~text
brand-new / incomplete controller prerequisite state
+ HidHide missing
+ usbip-win2 missing
+ VIIPER ready
+ setup safety gates pass

→ frontend can offer the existing "Setup required" flow
→ user accepts
→ existing elevated prerequisite owner runs
→ HidHide is installed first when missing
→ usbip-win2 is installed/updated when required
→ exact package evidence is verified
→ reboot-required handling remains intact
~~~

The fix must preserve Full1902 controller fail-close behavior. It must not make controller ownership start before prerequisites are Ready.

---

## 2. Required authority documents

Read and preserve the current authority order before implementation:

1. docs/Full 1902 Implementation/README.md
2. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
3. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
4. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

Also read the current prerequisite work:

- docs/work-order/HIDHIDE_USBIP_EXTERNAL_INSTALLER_ACQUISITION_WORK_ORDER.md
- docs/usbip2/PR1_USBIP_WIN2_PREREQUISITE_SELF_UPGRADE_WORK_ORDER.md
- docs/work-order/USBIP_0981_DISABLED_BOOT_SELF_UPGRADE_ADMISSION_REPAIR_WORK_ORDER_2026-09-27.md

The 2026-09-27 repair remains valid for the already-provisioned-HidHide / old-usbip upgrade case. This work order extends the same bounded repair concept to a true first-install state where HidHide itself may also be missing.

Do not reintroduce CTW assumptions.

---

## 3. Current defect A — a fresh install intentionally produces no application log

### 3.1 Current startup path

Program.cs currently does:

~~~csharp
var persistedLogLevel = LogLevelBootstrap.Read(AddonDataPaths.SettingsPath);
AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(persistedLogLevel);

AppLog.Info("App", "Application startup entered.", ...);
~~~

Current LogLevelBootstrap behavior treats a missing settings file as Off.

Current AppSettings also declares:

~~~csharp
public sealed record AppSettings(
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SuppressDeveloperMenuWarning = false)
~~~

AppLog itself starts at Off.

Therefore a real clean install is:

~~~text
settings.json does not exist
→ LogLevelBootstrap.Read(...) = Off
→ AppLog threshold = Off
→ startup Info is filtered
→ prerequisite Info/Warn/Error is filtered
→ no log entry reaches BufferedEntryWriter
→ no launch log file is created
~~~

This is why the first-install prerequisite failure currently has no useful diagnostic trail.

### 3.2 Current settings behavior

SettingsStore.Load() currently does this when the file does not exist:

~~~csharp
if (!File.Exists(_settingsPath))
{
    AppLog.Info("Settings", "Settings file not found. Using defaults.");
    return new AppSettings();
}
~~~

It returns an in-memory default but does not persist the canonical settings file.

So a fresh install can run with no settings.json at all until some later user mutation happens to call Save().

That is not the desired product contract.

---

## 4. Required change A1 — the product default log level is Info

Change the canonical AppSettings default:

~~~csharp
public sealed record AppSettings(
    AppLogPreference LogLevel = AppLogPreference.Info,
    bool SuppressDeveloperMenuWarning = false)
~~~

This is the default for a new installation only.

An existing valid settings file remains authoritative:

~~~text
"LogLevel": "Off"
→ remains Off

"LogLevel": "Info"
→ remains Info

"LogLevel": "Debug"
→ remains Debug
~~~

Do not silently rewrite an explicit existing Off preference to Info.

Do not add a separate FirstRunLogEnabled flag.

Do not add a second logging preference.

---

## 5. Required change A2 — missing settings.json must be created with canonical defaults

SettingsStore remains the one owner of the serialized settings shape.

When SettingsStore.Load() sees no settings file:

1. construct one AppSettings default instance;
2. persist it through the existing SettingsStore.Save() serializer;
3. return that same settings object.

Conceptual shape:

~~~csharp
if (!File.Exists(_settingsPath))
{
    var defaults = new AppSettings();

    try
    {
        Save(defaults);
        AppLog.Info("Settings", "Default settings file created.",
            ("Path", _settingsPath),
            ("LogLevel", defaults.LogLevel));
    }
    catch (IOException exception)
    {
        AppLog.Warn("Settings",
            "Default settings file could not be created; using in-memory defaults.",
            exception);
    }
    catch (UnauthorizedAccessException exception)
    {
        AppLog.Warn("Settings",
            "Default settings file could not be created; using in-memory defaults.",
            exception);
    }

    return defaults;
}
~~~

Use the current exception policy/style from SettingsStore; do not broaden exception handling unnecessarily.

Important requirements:

- use the existing Save() serializer;
- the created file must contain the same canonical fields a normal settings save contains;
- front-button defaults, M1/M2 defaults, Overlay tab order, ClawHUD flag, Developer flag, Quick Settings preference, Screenshot folder, and future normal AppSettings fields must come from the same AppSettings default object;
- do not create a reduced one-property bootstrap JSON such as only LogLevel;
- do not maintain a second hard-coded JSON template.

If default-file persistence fails, the app may continue with in-memory defaults. The failure must be logged once logging is available; do not crash a supported device solely because settings persistence failed.

---

## 6. Required change A3 — first-run logging must be Info before SettingsStore later reloads the file

Program currently needs the logging threshold before normal Runtime composition reaches SettingsStore.Load().

Keep this startup property simple.

Modify LogLevelBootstrap so that:

~~~text
settings file missing
→ Info

existing valid explicit Off
→ Off

existing valid Info
→ Info

existing valid Debug
→ Debug
~~~

The important first-run rule is:

~~~text
no settings.json
→ early process log threshold = Info
~~~

This ensures these first-run events are actually written:

~~~text
Application startup entered
Application launch header
hardware compatibility result
Center M authority result
prerequisite inspection
setup eligibility
setup prompt
elevated prerequisite setup result
~~~

Do not globally initialize AppLog to Info before reading an existing settings file. An existing user who explicitly selected Off must not get ordinary startup logs before their preference is read.

### Existing malformed/invalid-file behavior

This PR is not a settings-corruption redesign.

It is acceptable and preferred to keep the current conservative handling of an already-existing malformed or invalid settings file, provided that:

- missing-file fresh install resolves to Info;
- explicit valid Off remains Off;
- SettingsStore and LogLevelBootstrap do not disagree for the normal fresh-install path.

Do not broaden this PR into settings migration/versioning.

---

## 7. Current defect B — the current Disabled-boot repair exception is still usbip-only

The 2026-09-27 repair is already present in current main.

Current startup correctly has:

~~~text
DisabledBootAdmissionOutcome.PrerequisitesNotReady
~~~

and AddonProcessHost derives a startup repair window from:

~~~csharp
startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
&& startupResult.DisabledBootAdmission?.Outcome
    == DisabledBootAdmissionOutcome.PrerequisitesNotReady
~~~

That part should remain.

The remaining first-install hole is inside FrontendPrerequisiteSetupExecutor.

Current code:

~~~csharp
internal static bool AllowsUsbIpRepairWhileRecoveryUnsafe(
    bool startupRepairWindow,
    RuntimePrerequisiteAssessment prerequisites) =>
    startupRepairWindow
    && prerequisites.HidHide.Status == PrerequisiteStatus.Ready
    && prerequisites.Viiper.Status == PrerequisiteStatus.Ready;
~~~

This was deliberately shaped for:

~~~text
HidHide = Ready
usbip = old/missing
VIIPER = Ready
~~~

It does not cover a true first install:

~~~text
HidHide = Missing
usbip = Missing
VIIPER = Ready
RecoverySafe = false
DisabledBootAdmission = PrerequisitesNotReady
~~~

In that state:

~~~text
AllowsUsbIpRepairWhileRecoveryUnsafe = false
→ FirstTimeSetupPolicy sees RecoverySafe=false
→ SetupStatus=Blocked / RecoveryUnsafe
→ CanInstallRequiredComponents=false
→ no Setup required dialog
→ no UAC helper
→ HidHide remains missing
→ usbip remains missing
~~~

This is a real circular prerequisite-admission deadlock.

---

## 8. Safety boundary for the prerequisite fix

Do not make Disabled boot globally RecoverySafe.

Do not change:

~~~text
StartupResult.RecoverySafe
RecoverySafetyState
PowerMutationGate
PowerTransitionCoordinator
SystemStatusProvider recovery semantics
physical controller ownership
VIIPER attach/detach ownership
Center M authority
~~~

The narrow fact remains:

> Controller ownership stopped before acquisition because prerequisites were not Ready.

That fact may allow the existing prerequisite setup owner to install only controller prerequisites that its existing package policy already classifies as safely installable.

It does not permit PID1902 ownership.

It does not permit arbitrary driver repair.

It does not make VIIPER missing/corrupt repairable through this helper.

---

## 9. Required change B1 — rename the usbip-only repair permission to describe the real scope

The current internal name:

~~~text
AllowUsbIpRepairWhileRecoveryUnsafe
AllowsUsbIpRepairWhileRecoveryUnsafe
~~~

is no longer correct once first-install HidHide installation is supported.

Rename the internal-only contract to something narrow but accurate, for example:

~~~text
AllowPrerequisiteRepairWhileRecoveryUnsafe
AllowsPrerequisiteRepairWhileRecoveryUnsafe
~~~

or:

~~~text
AllowControllerPrerequisiteRepairWhileRecoveryUnsafe
~~~

Do not expose this through the public frontend named-pipe contract.

Do not persist it.

Do not add a new public controller state.

This is a process-lifetime setup-policy input only.

---

## 10. Required change B2 — preserve the existing startup repair window and VIIPER fail-close

AddonProcessHost should continue to derive the repair window only from the already-captured startup result:

~~~csharp
var prerequisiteRepairWindow =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
    && startupResult.DisabledBootAdmission?.Outcome
        == DisabledBootAdmissionOutcome.PrerequisitesNotReady;
~~~

Pass that fact into the existing FrontendPrerequisiteSetupExecutor.

Do not re-read Center M roots.

Do not reconstruct admission state from later status text.

Do not turn every RecoverySafe=false state into a repair opportunity.

VIIPER is not installed by ElevatedPrerequisiteSetup. Therefore a recovery-unsafe repair window must still require:

~~~text
VIIPER = Ready
~~~

before HidHide/usbip setup can become installable.

A missing/unusable/indeterminate VIIPER runtime must remain blocked because this setup owner cannot repair it.

---

## 11. Required change B3 — allow first-install HidHide and usbip package repair through the existing policy

FrontendPrerequisiteSetupExecutor already computes:

~~~text
HidHideInstallation
UsbIpWin2Installation
~~~

using the existing authoritative package probes and ComponentInstallationAssessmentPolicy.

Use those existing installation assessments. Do not infer installability only from raw runtime prerequisite status.

The recovery-unsafe exception should be available when:

~~~text
startup repair window = true
VIIPER = Ready

and at least one real package repair is required:

HidHideInstallation = Missing
OR
UsbIpWin2Installation = Missing
OR
UsbIpWin2Installation = UpdateRequired
~~~

Normal policy must still reject unsupported states such as:

~~~text
HidHide ExistingUnverified
HidHide Incompatible / Indeterminate
usbip ExistingUnverified
usbip Incompatible because installed version is newer than target
usbip malformed/unparseable package version
usbip package inspection failure
corrupt/indeterminate provisioning state
unresolved InstallStarted
pending reboot
unsupported/indeterminate hardware
Steam/BPM active
VIIPER not Ready
~~~

Do not create a new package-status enum.

Do not add a new installer policy.

---

## 12. Required change B4 — FirstTimeSetupPolicy must bypass RecoveryUnsafe only for a repairable HidHide/usbip operation

Current policy is usbip-only:

~~~csharp
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
~~~

Change it to the real two-component prerequisite scope.

Conceptual shape:

~~~csharp
var hidHideRepairRequired =
    input.HidHideInstallation.Status == ComponentInstallationStatus.Missing;

var usbIpRepairRequired =
    input.UsbIpWin2Installation.Status is
        ComponentInstallationStatus.Missing
        or ComponentInstallationStatus.UpdateRequired;

var prerequisiteRepairRequired =
    hidHideRepairRequired || usbIpRepairRequired;

if (!input.RecoverySafe
    && !(input.AllowPrerequisiteRepairWhileRecoveryUnsafe
         && prerequisiteRepairRequired))
{
    return new(
        FirstTimeSetupStatus.Blocked,
        FirstTimeSetupReason.RecoveryUnsafe,
        false);
}
~~~

Then let the existing later FirstTimeSetupPolicy checks continue to reject incompatible, unverified, indeterminate, pending-reboot, and unsafe receipt states.

The exception is not permission to install anything merely because RuntimePrerequisiteAssessment is not Ready.

The installation assessments remain authoritative.

---

## 13. Required behavior for the actual fresh-install case

The following exact case must become installable:

~~~text
Hardware = Supported
Center M startup roots = Disabled
DisabledBootAdmission = PrerequisitesNotReady
RecoverySafe = false
Steam/BPM = inactive
VIIPER = Ready

HidHide package = not installed
HidHide runtime prerequisite = Missing
HidHide installation assessment = Missing
HidHide receipt = None

usbip package = not installed
usbip runtime prerequisite = Missing
usbip installation assessment = Missing
usbip receipt = None

→ FirstTimeSetupStatus.Required
→ Reason=MissingComponents
→ CanInstallRequiredComponents=true
→ existing setup prompt is shown
~~~

After user accepts:

~~~text
existing ElevatedPrerequisiteSetup
→ hardware preflight
→ trusted provisioning storage
→ Initial Steam/session safety gate
→ HidHide package probe
→ acquire exact official HidHide installer
→ SHA-256 verify
→ HidHide silent install
→ exact package evidence / receipt
→ usbip package probe
→ acquire exact official usbip-win2 installer
→ SHA-256 verify
→ usbip silent install
→ exact package evidence / receipt
→ return Installed or RebootRequired
~~~

No new installer executable or coordinator is needed.

---

## 14. Preserve the existing prerequisite owner

ElevatedPrerequisiteSetup remains the sole mutation owner.

Do not move HidHide or usbip installation into:

~~~text
Velopack OnAfterInstallFastCallback
Velopack updater
Program installation hook
background downloader
Windows service
controller owner
startup admission
~~~

The Velopack fast callback must remain bounded and must not become a network/driver installer.

The existing explicit setup / UAC boundary remains correct.

The fix is to make that existing path reachable on a true first install.

---

## 15. Preserve external installer acquisition

Current release packaging intentionally does not bundle the HidHide and usbip-win2 installers.

Keep the existing external acquisition model:

~~~text
explicit prerequisite setup
→ elevated helper
→ download exact pinned official upstream installer
→ trusted ProgramData staging
→ SHA-256 verify
→ existing silent installer execution
→ exact package readback
→ delete staged installer
~~~

Do not put these installer EXEs back into the normal Velopack payload.

Do not change current pinned metadata unless latest main changed independently during implementation.

At reviewed baseline:

~~~text
HidHide target:
1.5.230.0
HidHide_1.5.230_x64.exe

usbip-win2 target:
0.9.8.1
USBip-0.9.8.1-x64.exe
~~~

---

## 16. Logging requirements for the fixed fresh-install lifecycle

With the new default Info level, a brand-new first run must leave enough evidence to diagnose prerequisite setup.

At minimum the log should naturally contain the existing Info/Warn events for:

~~~text
Application startup entered
Application launch header
hardware compatibility
Center M startup authority
runtime prerequisite assessment / setup status where currently logged
setup prompt shown
setup prompt accepted/declined
elevated setup requested
HidHide package probe
HidHide installation assessment
HidHide installer acquisition/install result
usbip package probe
usbip installation assessment
usbip installer acquisition/install result
reboot-required/final result
~~~

Do not add high-frequency logging.

Do not add duplicate log files.

Do not add a special first-install logger.

Use the existing AppLog only.

---

## 17. Required settings tests

Update/add focused tests.

### 17.1 AppSettings default

~~~text
new AppSettings().LogLevel
→ Info
~~~

Replace the current Off-default assertion.

### 17.2 Missing settings file creates canonical defaults

Use a temporary path:

~~~text
settings.json absent
→ SettingsStore.Load()
→ returns defaults
→ file now exists
→ persisted LogLevel == "Info"
→ persisted defaults match the returned AppSettings
~~~

At minimum verify the canonical fields that are currently serialized by Save() are present and valid.

Do not compare raw JSON formatting when semantic assertions are sufficient.

### 17.3 Early log bootstrap

Update LogLevelBootstrap tests:

~~~text
missing settings file
→ Info

explicit Off
→ Off

explicit Info
→ Info

explicit Debug
→ Debug
~~~

Keep existing malformed/invalid behavior unchanged unless implementation requires a narrowly documented adjustment.

### 17.4 Explicit Off survives restart

Add/retain regression coverage:

~~~text
existing settings.json with LogLevel=Off
→ Program bootstrap read resolves Off
→ SettingsStore.Load resolves Off
→ no default rewrite to Info
~~~

The fresh-install default must never overwrite an intentional user choice.

---

## 18. Required prerequisite-policy tests

### 18.1 True first-install pair

Add a focused FirstTimeSetupPolicy test:

~~~text
RecoverySafe=false
AllowPrerequisiteRepairWhileRecoveryUnsafe=true
HidHideInstallation=Missing
UsbIpWin2Installation=Missing
hardware=Supported
Steam inactive
safe receipts

→ Required
→ MissingComponents
→ CanInstallRequiredComponents=true
~~~

This is the key missing regression test.

### 18.2 HidHide-only missing

~~~text
RecoverySafe=false
repair window=true
HidHideInstallation=Missing
UsbIpWin2Installation=Installed
VIIPER ready

→ Required / installable
~~~

### 18.3 usbip-only missing remains supported

~~~text
RecoverySafe=false
repair window=true
HidHideInstallation=Installed
UsbIpWin2Installation=Missing

→ Required / installable
~~~

### 18.4 usbip upgrade remains supported

Preserve the current real upgrade regression:

~~~text
HidHideInstallation=Installed
UsbIpWin2Installation=UpdateRequired
RecoverySafe=false
repair window=true

→ Required / installable
~~~

Do not regress the 0.9.8.0 → 0.9.8.1 repair path.

### 18.5 no real repair still remains blocked

~~~text
RecoverySafe=false
repair window=true
HidHideInstallation=Installed
UsbIpWin2Installation=Installed

→ Blocked / RecoveryUnsafe
~~~

This proves the new flag is not a general RecoverySafe bypass.

### 18.6 unsafe package states remain blocked

Cover at minimum:

~~~text
HidHide ExistingUnverified
usbip ExistingUnverified
usbip Incompatible/newer-than-target
usbip Indeterminate
corrupt provisioning state
unresolved InstallStarted
PendingReboot
~~~

### 18.7 VIIPER not Ready remains blocked

At FrontendPrerequisiteSetupExecutor level prove:

~~~text
startup repair window=true
HidHide Missing
usbip Missing
VIIPER Missing/Unusable/Indeterminate

→ recovery-unsafe repair permission=false
→ setup remains non-installable
~~~

This prevents the controller-prerequisite installer from pretending it can repair a missing bundled VIIPER runtime.

---

## 19. Required frontend/setup integration test

Add one test at the existing FrontendPrerequisiteSetupBridgeTests or the smallest current equivalent.

Represent the real first-install Disabled state:

~~~text
Startup repair permission = true
RecoverySafe = false
HidHide = Missing
usbip = Missing
VIIPER = Ready
Steam inactive
package installation assessments = Missing / Missing
receipts safe
~~~

Expected:

~~~text
CaptureStatus / setup evaluation
→ SetupStatus=Required
→ CanInstallRequiredComponents=true

RunPrerequisiteSetupAsync
→ existing IFrontendPrerequisiteSetupExecutor.RunAsync invoked once
→ existing elevated owner is the only launch path
~~~

Do not add a test-only production manager.

---

## 20. Elevated helper non-regression

Do not weaken ElevatedPrerequisiteSetup.

Retain existing behavior:

~~~text
HidHide Missing
→ install HidHide first

then

usbip Missing/UpdateRequired
→ install usbip

existing exact package
→ do not reinstall

HidHide unexpected/existing-unverified state
→ fail closed

usbip newer than target
→ fail closed / no downgrade

installer acquisition/hash failure
→ fail

Steam safety gate failure
→ fail

3010
→ RebootRequired
~~~

If current tests do not cover both components missing in one logical first-install sequence, add the smallest focused execution-policy/helper test possible without introducing a new product abstraction.

---

## 21. Physical ownership non-regression

The controller path must remain blocked until prerequisites are genuinely Ready.

Keep:

~~~csharp
startupResult.DisabledBootAdmission?.IsReady == true
~~~

as the requirement before physical acquisition.

Therefore:

~~~text
PrerequisitesNotReady
+ first-install repair offered

→ no PID1901→PID1902 command
→ no DirectInput ownership
→ no HidHide controller target ownership mutation from the physical owner
→ no VIIPER live presentation attach
~~~

Only a later startup/reconcile with prerequisite admission Ready may enter the normal Full1902 controller path.

Do not couple the setup dialog directly to controller acquisition.

---

## 22. Expected production files

Likely files:

~~~text
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/LogLevelBootstrap.cs
src/SteamInputAddonforClaw/Program.cs                (only if current startup wiring needs a small adjustment)

src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs
src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
~~~

AddonProcessHost may need only naming/wiring changes because the PrerequisitesNotReady repair window already exists on reviewed main.

Do not touch DisabledBootControllerAdmission unless fresh latest-main review shows a regression. The typed PrerequisitesNotReady outcome is already implemented on reviewed baseline.

Likely tests:

~~~text
tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
tests/SteamInputAddonforClaw.Tests/LogLevelBootstrapTests.cs
tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendPrerequisiteSetupBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
~~~

Touch additional files only when current code genuinely requires it.

---

## 23. Do not overengineer

Do not add:

- FirstRunManager;
- SettingsBootstrapService;
- separate first-run JSON schema;
- separate first-run log file;
- PrerequisiteRepairManager;
- HidHideInstallManager;
- UsbIpInstallManager;
- background driver repair worker;
- Windows service;
- retry scheduler;
- repair epochs/generations;
- new controller authority;
- new persisted repair state;
- second provisioning receipt format;
- general package manager;
- installer cache manager.

The goal is not to build a generic first-run framework.

The goal is:

~~~text
one settings authority
one log preference
one prerequisite setup owner
one controller authority model
~~~

with the existing paths made correct for a clean installation.

---

## 24. Validation

Run the normal current-main build/test verification.

At minimum:

~~~text
Release build succeeds
repository warning baseline does not regress
full SteamInputAddonforClaw.Tests suite passes
focused settings/log bootstrap tests pass
focused prerequisite first-install tests pass
UI architecture tests pass
publish asset verification passes
~~~

No installer payload should reappear in the normal publish output.

---

## 25. Real fresh-install acceptance test

Test on a supported MSI Claw from a genuinely clean Addon state.

Before test:

~~~text
Steam Addon for Claw uninstalled
Addon settings/data removed as appropriate for a clean-install test
HidHide not installed
usbip-win2 not installed
MSI Center M present
~~~

Test both controller-authority starting states if practical, but the Disabled-authority case is mandatory for this regression because that is where RecoverySafe=false currently deadlocks setup.

Expected first run:

~~~text
1. App starts.
2. settings.json is created automatically.
3. settings.json contains LogLevel=Info.
4. a normal launch log file exists immediately.
5. log contains startup/hardware/authority evidence.
6. status detects HidHide Missing and usbip Missing.
7. when the startup prerequisite-repair window is valid and VIIPER is Ready,
   Setup required becomes installable.
8. user accepts.
9. UAC helper launches.
10. HidHide official pinned installer is acquired, hash-verified, installed, and verified.
11. usbip-win2 official pinned installer is acquired, hash-verified, installed, and verified.
12. staged installer files are cleaned up.
13. Installed/RebootRequired result is shown correctly.
14. after required restart, runtime prerequisite inspection reaches Ready.
15. only then may Full1902 controller ownership/presentation start.
~~~

Also verify explicit logging preference:

~~~text
set LogLevel=Off
restart app
→ existing settings remains Off
→ it is not rewritten to Info
~~~

---

## 26. Acceptance criteria

The PR is acceptable only when all of the following are true:

1. A clean install creates the canonical settings.json without requiring a user settings mutation.
2. The fresh-install default LogLevel is Info.
3. First-run startup logs are actually written before prerequisite troubleshooting is needed.
4. Existing explicit LogLevel=Off remains authoritative.
5. No second settings serializer/template is introduced.
6. A Disabled-boot first-install state with HidHide Missing + usbip Missing + VIIPER Ready can reach the existing setup prompt.
7. The existing elevated helper installs HidHide and usbip in its current bounded sequence.
8. The existing usbip self-upgrade path remains working.
9. VIIPER-not-ready and genuinely unsafe prerequisite/package states remain blocked.
10. RecoverySafe is not globally relaxed.
11. Physical PID1902 ownership does not begin while prerequisites are not Ready.
12. Velopack fast callbacks do not become driver/network installers.
13. HidHide/usbip installer payloads remain externally acquired and hash-verified.
14. No new manager/service/state machine/authority is added.
15. Full tests pass.
16. A real clean-install MSI Claw test confirms settings, first log creation, prerequisite setup, reboot handling, and post-reboot controller operation.

---

## 27. Final invariant

After this change:

~~~text
fresh install
→ canonical settings exist
→ Info logging exists by default
→ first-install failures are diagnosable

and

Full1902 prerequisite admission blocks controller ownership
→ but does not block its own existing user-consented HidHide/usbip repair path
→ exact prerequisites are established
→ only then controller ownership may proceed
~~~

The product invariant is:

> A fresh installation must be observable and self-completing through the existing bounded prerequisite setup path. Missing HidHide/usbip must never strand the user merely because Full1902 correctly kept controller ownership fail-closed before those prerequisites were installed.
