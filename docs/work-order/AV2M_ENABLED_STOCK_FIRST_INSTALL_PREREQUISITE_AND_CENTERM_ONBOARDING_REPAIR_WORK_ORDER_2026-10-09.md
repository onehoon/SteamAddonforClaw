# Work Order — AV2M Fresh Install: Enabled-Stock Topology Timeout, Prerequisite Setup, and Center M Onboarding

> Date: 2026-10-09  
> Repository: onehoon/SteamAddonforClaw  
> Target: current main, standalone Full PID1902 application  
> Type: focused production defect repair; local Codex implements, the user owns subsequent real-hardware validation  
> Scope: first-install HidHide/usbip-win2 admission, stock-topology diagnostics, and safe Center M Disable-and-Restart onboarding

## 1. Goal

Fix a fresh-install failure on a supported MSI Claw AV2M/A2VM device where the app and all main UI pages load, but automatic controller-prerequisite setup does not launch and the expected Center M Disable confirmation never appears.

This is **not** a request to weaken Full1902 controller safety. There are two separate decisions:

1. Whether the existing trusted, elevated HidHide/usbip-win2 prerequisite installer may run **before the Addon acquires controller authority**.
2. Whether Center M may be disabled and the machine restarted, committing Addon controller authority **for the next boot**.

Allow the first decision only for the precisely proven, no-controller-mutation stock-startup failure window. Require renewed stock-controller safety proof for the second decision. If that proof cannot be obtained, remain with Center M Enabled; display an actionable failure rather than claiming the authority transition succeeded.

Do not add a second authority source, persisted first-run flag, new driver installer, background repair service, controller owner, or general-purpose recovery state machine.

## 2. Required architecture and prior work

Read and respect these documents in their current authority order:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/work-order/FRESH_INSTALL_SETTINGS_LOGGING_AND_CONTROLLER_PREREQUISITE_REPAIR_WORK_ORDER_2026-10-03.md
- docs/work-order/HIDHIDE_USBIP_EXTERNAL_INSTALLER_ACQUISITION_WORK_ORDER.md
- docs/work-order/USBIP_0981_DISABLED_BOOT_SELF_UPGRADE_ADMISSION_REPAIR_WORK_ORDER_2026-09-27.md

Full1902 is standalone. Ignore historical CTW integration and CTW process-control assumptions. Center M startup roots remain the sole reboot-bound controller-authority source of truth.

The October 3 work is already implemented on current main: default first-install Info logging and the narrow DisabledBootAdmission=PrerequisitesNotReady repair window allowing both HidHide and usbip-win2 when VIIPER is Ready. Preserve it.

The October 5 elevated Runtime model is current. Do not revive per-feature elevation layers or put network/driver work in Velopack fast callbacks.

## 3. Supplied AV2M evidence — actual log, not inferred package state

Reviewed attachments:

- SteamInputAddonforClaw-2026-10-09-162306.237-P30932-L9652baddb9.log (medium bootstrap)
- SteamInputAddonforClaw-2026-10-09-162306.526-P30920-L8a8af6cce7.log (elevated Runtime)
- ui-29700.log
- overlay-28680.log

Evidence in the elevated Runtime log, local time UTC+09:00:

~~~text
16:23:06  Version=0.1.346.0; elevated Runtime running
16:23:07  Hardware Status=Supported; DeviceModel=msi.claw.a2vm.8
16:23:07  CenterM.Startup State=Enabled; Server=True; Updater=True; Service=Automatic
16:23:07  CenterM.StartupAuthority State=Enabled; Action=StockPath
16:23:07  ControllerTopology wait started; TimeoutMs=5000; PollIntervalMs=350
16:23:12  ControllerTopology timeout; Action=Passive; Result=Indeterminate
16:23:12  Runtime starts and creates the default settings.json with LogLevel=Info
16:23:13  Frontend named-pipe server ready; UI starts successfully
16:23:18  Center M still Enabled
16:23:37  Center M still Enabled
~~~

The UI log confirms Frontend connected, bootstrap acquired, MainWindow initialized, and Frontend activated. Therefore app-wide startup failure, unsupported-model handling, and missing UI launch are not explanations.

There is **no** PrerequisiteSetup request/worker/install record and **no** CenterM.Authority Disable request in the supplied captured window.

Important evidence limits:

- The attached Info logs do **not** show a full runtime-prerequisite assessment, Windows package-probe results, VIIPER hash status, or the evaluated FirstTimeSetupStatus/Reason.
- They therefore do **not** prove that HidHide or usbip-win2 packages are absent, that VIIPER is Ready, or that a concrete PnP control HID was absent.
- The topology timeout is proven, but its underlying cause is **not** proven: possible categories include missing recognized MSI devices, present MSI devices without a matching control HID, misclassification/usage parsing, or an unstable relevant topology.
- Do not hard-code an AV2M-specific device-identity workaround without real PnP evidence.

## 4. Confirmed current code path and why both UI symptoms happen

### 4.1 StartupCoordinator (StockPath)

In src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs:

- Exactly Enabled Center M roots select StockPath.
- Startup waits for ControllerTopologyWaiter.WaitUntilStableAsync.
- A non-Stable result returns a StartupResult with ShouldStartRuntime=true and RecoverySafe=false.
- StockCenterMStartupBaseline.EstablishAsync is **not** called after this early return.
- The frontend and Runtime continue normally.

This means the attached startup returned before the stock-baseline owner attempted a mode switch. That no-mutation distinction must be preserved and represented explicitly in the narrow new setup permission; RecoverySafe=false alone is insufficient to identify it.

### 4.2 Existing permission covers only Disabled-boot prerequisite repair

In src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs:

~~~csharp
var allowPrerequisiteRepairWhileRecoveryUnsafe =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
    && startupResult.DisabledBootAdmission?.Outcome
        == DisabledBootAdmissionOutcome.PrerequisitesNotReady;
~~~

This flag is false for the attached Enabled/StockPath case.

src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs requires the passed window plus VIIPER Ready. src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs then rejects missing-component setup while RecoverySafe=false unless the narrow repair permission is true and real installation repair is required.

Thus for missing, legitimately installable packages the observed StockPath timeout makes setup non-installable.

### 4.3 UI automatic setup and Center M prompt share a chain

In src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs:

~~~csharp
if (snapshot.CanInstallRequiredComponents)
{
    if (_windowActivatedForUser)
        _ = RunPrerequisiteSetupAsync();
    else
        RequestPrerequisiteSetupActivation();
}
~~~

After the worker, HandlePrerequisiteSetupResultAsync invokes DeviceContent.ConfirmCenterMDisableAfterPrerequisiteSetupAsync **only** for Ready / Installed / RebootRequired.

In src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs, this method opens the normal Disable MSI Center M confirmation, which calls the existing reboot-bound authority transition upon user acceptance.

Therefore absent setup eligibility explains why the Center M dialog does not appear, without implying the dialog implementation is broken.

### 4.4 There is a second gate after the dialog

In src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs, DisableAsync captures fresh prerequisites and RecoverySafe and refuses Enabled -> Disabled authority mutation if RecoverySafe=false. The existing special recommit exception is strictly for a previously Disabled boot in its known prerequisite-repair window.

**Fixing only setup eligibility would still leave the user with a Disable dialog that cannot successfully commit after the attachment's startup timeout.** This work order must handle that path safely rather than making the frontend merely show an unusable popup.

## 5. Required change A — make the precise stock pre-baseline timeout distinguishable

In StartupCoordinator / StartupResult, expose one **process-lifetime, immutable fact** for this exact branch, with a name such as:

~~~csharp
bool StockTopologyUnreadyBeforeBaseline = false
~~~

Set true **only** when all of the following were already proven by normal startup:

- supported hardware assessment;
- exactly Enabled Center M startup roots;
- stock ControllerTopologyWaiter returned Indeterminate;
- execution returns **before** StockCenterMStartupBaseline.EstablishAsync (no stock PID/mode mutation attempt);
- no Disabled-boot controller admission or Addon physical/VIIPER ownership was started.

Default false for every other constructor or branch.

Do **not** derive this from generic RecoverySafe=false, generic Enabled, a persisted first-run boolean, a new Center M scan, or a guess based on device model.

No new public frontend contract, persistence, authority enum, or extra state manager is needed. A single optional value on the existing StartupResult is preferred. Name it according to the current code's naming conventions.

If the underlying topology result distinguishes failures beyond Indeterminate after a small, evidence-based refinement, keep the admitted case equally narrow; do not include generic stock-baseline failures.

## 6. Required change B — allow safe prerequisite-only repair in that window

Extend only the existing Runtime-to-FrontendPrerequisiteSetupExecutor permission calculation.

Conceptual production wiring:

~~~csharp
var disabledBootRepair =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled
    && startupResult.DisabledBootAdmission?.Outcome
        == DisabledBootAdmissionOutcome.PrerequisitesNotReady;

var stockPreBaselineRepair =
    startupResult.CenterMStartupState == FrontendCenterMStartupState.Enabled
    && startupResult.StockTopologyUnreadyBeforeBaseline;

var allowPrerequisiteRepairWhileRecoveryUnsafe =
    disabledBootRepair || stockPreBaselineRepair;
~~~

Preserve FrontendPrerequisiteSetupExecutor's existing VIIPER Ready requirement and FirstTimeSetupPolicy's exact package-installation checks. The flag permits **only** the pre-existing HidHide/usbip-win2 setup executor.

Never make RecoverySafetyState globally Safe, or turn on physical PID1902, HidHide isolation, DirectInput, VIIPER, WING suppression for Addon authority, or a live controller-owner retry because this window exists.

Installation eligibility still requires:

- a supported hardware assessment at status capture and the elevated setup worker;
- VIIPER runtime Ready (the helper cannot install/repair VIIPER);
- real package assessments: HidHide Missing and/or usbip-win2 Missing/UpdateRequired;
- package/receipt and provisioning-storage states accepted by the existing policies;
- no active Steam/BPM routing session that blocks the existing setup contract;
- the existing elevated helper's hardware, trusted storage, installer-authenticity/hash, Steam-session, and execution safety gates.

Keep all non-repairable states blocked: unsupported/indeterminate hardware; Center M Partial/Unavailable; stock-baseline failed/attempted transition; blocked Disabled boot; HidHide/usbip ExistingUnverified, Incompatible, or Indeterminate; corrupt/indeterminate receipt; unresolved InstallStarted; pending reboot; missing/unusable/indeterminate VIIPER; actual active controller ownership; installer or Steam safety failure.

Do not reinstall an already matching installed package.

Do not change the pinned installers, downloading, SHA-256 verification, ProgramData staging, installation order, or uninstall ownership. Existing ElevatedPrerequisiteSetup remains the only driver mutation owner.

## 7. Required change C — independently prove Enabled-stock safety before authority commit

The exception in section 6 is **not** permission to disable Center M.

For a user-confirmed Disable-and-Restart request originating from this precise Enabled + StockTopologyUnreadyBeforeBaseline startup:

1. Keep the existing Device-page confirmation exactly as a user decision. Cancel => no backend mutation.
2. Re-capture Center M's actual startup roots; proceed only if still exactly Enabled.
3. Preserve existing checks for safe Runtime termination, fresh prerequisite Ready or exact current-boot pending evidence, and no Addon-owned physical/virtual controller session.
4. When normal recoverySafe is false **and only this specific known no-mutation stock-startup case applies**, run a **bounded, current-world stock readiness proof** through existing components:
   - wait again for a Stable MSI controller topology using the existing topology waiter/check;
   - after that succeeds, use the existing stock baseline owner to establish and verify the real PID1901/XInput stock baseline;
   - accept only positive, verified success; any Indeterminate, exception, cancellation, missing exact physical identity, or failed verification leaves authority untouched.
5. Use that successful proof **locally for this one explicit authority transition**, not as a global RecoverySafe=true flip, persistent setting, or bypass for unrelated requests.
6. Only then run the existing ordered mandatory startup-registration, HidHide zero-target baseline (or exact pending-reboot special path), Center M root mutation/read-back, and Windows restart.

Prefer reusing StartupCoordinator's current waiter + StockCenterMStartupBaseline within the established AddonStartupComposition. A tiny callable verification method/delegate in those existing owners is preferable to constructing a second device scanner/manager. In the transition, a nullable/injected bounded verification callback and one narrow origin fact is sufficient if needed for isolated tests.

Conceptual acceptance:

~~~csharp
var stockOnboardingProof = false;
if (!admission.RecoverySafe
    && snapshot.State == FrontendCenterMStartupState.Enabled
    && _stockTopologyUnreadyBeforeBaseline
    && !activeControllerOwnership)
{
    stockOnboardingProof =
        await _verifyCurrentStockTopologyAndBaseline(cancellationToken)
            .ConfigureAwait(false);
}

if (!admission.RecoverySafe
    && !disabledBootRepairCommit
    && !stockOnboardingProof)
{
    return Fail(snapshot, "Controller stock baseline is not verified; MSI Center M remains Enabled. Retry after the controller is available.");
}
~~~

Place the proof at the correct existing preflight boundary; do not duplicate existing readiness/identity validation or run it merely on opening the popup. The actual transition code and existing pending-reboot handling take precedence over this schematic.

**If actual PnP state never becomes provably safe, this work order must NOT force Disable or reboot.** A failure message and an Enabled stock controller are the correct safety result; a successful fresh installation alone is not proof of readiness.

Keep the current Disabled-boot onboarding recommit semantics unchanged. Do not accidentally extend this stock proof path to Disabled, Partial, or Unavailable authority states.

## 8. Required change D — identify the real AV2M topology timeout class without guessing

The attached log reports only a 5-second timeout; per-poll readiness evidence is Debug, not included at first-install default Info.

In src/SteamInputAddonforClaw/Startup/ControllerTopologyWaiter.cs, add **one bounded terminal Info/Warn diagnostic** for an unsuccessful wait, using facts already observed in that loop. Include concise non-sensitive evidence sufficient to distinguish:

- no present MSI VID/PID 1901/1902/1903 candidates;
- MSI candidates present but not classified as internal;
- internal MSI candidates present but required mode-specific control HID usage/page not resolved;
- matching control HID present but topology snapshot never stable for the required count;
- enumeration failure (preserve the exception's current warning path).

Useful fields include candidate/recognized counts, recognized PID1901/PID1902 control-HID flags, final consecutive stable count, attempts, and a short machine-readable reason.

**Never log raw full PnP instance IDs, serials, user paths, or every poll at Info.** Retain Debug for detailed bounded development diagnosis. Diagnostics must not alter whether startup is Stable.

Review the **existing** WindowsControllerDeviceEnumerator, MSI internal matcher, ControllerDeviceClassifier.IsInternalHandheld, MsiClawModeTopology control-HID usage/page, and actual AV2M model identification. If a deterministic normal-hardware mismatch is demonstrated by available code/tests or a supplied PnP fixture, fix it narrowly with tests. Do not assume an A2VM-specific HID layout from the present logs, weaken strong device identity, or treat generic gamepad interfaces as a substitute for the required control HID merely to remove the timeout.

Do not respond to the timeout by blindly raising the global wait duration, creating endless retries, or forcing PID1901/1902 switching before the stock baseline is verified.

## 9. Required change E — make a blocked first install diagnosable at Info

Currently SystemStatusProvider prints prerequisite status only at Debug; FirstTimeSetup evaluation and automatic setup eligibility can silently fail at default fresh-install Info.

At the existing frontend status/setup evaluation boundary, add a **transition-only or bounded first evaluation** Info log containing:

~~~text
HardwareStatus
CenterMStartupState / the known stock-pre-baseline repair window
RecoverySafe
ViiperPrerequisiteStatus
HidHidePrerequisiteStatus / HidHideInstallationStatus / Reason
UsbIpPrerequisiteStatus / UsbIpInstallationStatus / Reason
Receipt/provisioning reason when relevant
FirstTimeSetupStatus / Reason
CanInstallRequiredComponents
~~~

Log the initial relevant decision and meaningful changes, not a high-frequency line on every UI refresh. No redundant logging manager, polling loop, or separate log file. Do not suppress explicit user-selected Off logging.

Reuse current AppLog and the canonical evaluation owner. Prefer a small single structured Info event at the setup decision boundary, with low-noise deduplication only if the existing logging mechanism already supports it or if the noise is demonstrated in normal UI refresh.

The frontend must still render Settings > Required Components accurately when setup is blocked. Do not report "installed" merely because a process launched. On setup worker failure, preserve the existing warning/info bar and allow the existing user flow to recover on a subsequent attempt/relaunch where safe.

## 10. Expected outcomes

### 10.1 The attached AV2M class — stock topology still unavailable at first startup

~~~text
Hardware=Supported (msi.claw.a2vm.8)
Center M=Enabled
ControllerTopology=Indeterminate before stock baseline
RecoverySafe=false
No previous stock-baseline mutation attempt

HidHide package=Missing, runtime=Missing
usbip-win2 package=Missing, runtime=Missing
VIIPER=Ready, Steam inactive, receipts/storage safe

=> FirstTimeSetup=Required / MissingComponents / installable
=> existing MainWindow automatic prerequisite worker runs
=> elevated helper installs matching missing components
=> result Ready/Installed/RebootRequired
=> existing Disable Center M confirmation is shown
=> user may Cancel without authority mutation
=> if confirmed: CURRENT stock-topology + PID1901 verification required
=> only verified success commits Center M Disabled and reboot
=> otherwise remain Enabled and present actionable failure
~~~

### 10.2 If the packages are already installed

No redundant install. Keep the existing setup Complete/blocked interpretation. Do not manufacture an install-success event merely to force the Center M popup. A separate first-run "always disable Center M" automation is out of scope.

### 10.3 Other states

- Disabled boot + PrerequisitesNotReady: existing repair window still works.
- Disabled boot + Blocked: no new repair bypass or authority mutation.
- Center M Partial/Unavailable: no stock-topology repair exception.
- Enabled + stock baseline attempted/failed: no no-mutation exception.
- Unsafe receipt/package/VIIPER: fail closed.
- Steam/BPM active: follow existing prerequisite worker and setup safety policy.
- Pending-reboot package: maintain exact current-boot receipt checks and existing next-boot commit semantics.
- Physical device absent at the explicit Disable confirmation: no Center M mutation or restart.

## 11. Focused code/tests

Keep the patch localized. Expected touch points (verify against current main before editing):

- Startup/StartupCoordinator.cs and its existing StartupResult
- Hosting/AddonProcessHost.cs
- Frontend/FrontendPrerequisiteSetupExecutor.cs
- Prerequisites/FirstTimeSetup.cs only if truly required; its existing repairable-package guards should normally be reused
- Startup/ControllerTopologyWaiter.cs
- CenterMStartup/CenterMRebootAuthorityTransition.cs
- Startup/AddonStartupComposition.cs if necessary to expose/reuse the existing proof
- Frontend logging/evaluation seam
- UI/Views/DevicePage.xaml.cs and UI/MainWindow.xaml.cs **only** if an actual UI regression is proved; current chained prompt already exists

Required automated coverage, using existing fakes and the smallest seams:

1. Startup: Enabled + supported + topology Indeterminate => Runtime starts, RecoverySafe=false, new no-mutation repair fact=true, StockCenterMStartupBaseline never invoked.
2. Startup: Enabled + Stable + baseline success => RecoverySafe=true, repair fact=false.
3. Startup: Enabled + Stable + baseline failure => RecoverySafe=false, repair fact=false, no setup exception.
4. Startup: Disabled / Partial / Unavailable / unsupported => new stock repair fact=false.
5. First-time setup: the exact Enabled-stock timeout fact + VIIPER Ready + Missing/Missing + safe receipts + Steam inactive => Required, MissingComponents, installable, existing worker invoked exactly once.
6. Same window: HidHide-only Missing, usbip-only Missing, and usbip UpdateRequired remain independently installable; installed packages are not reinstalled.
7. Same window: VIIPER not Ready, incompatible/unverified package, uncertain receipt, pending reboot, Steam active, unsupported hardware => no unsafe installer invocation.
8. Existing Disabled-boot HidHide+usbip first install and usbip self-upgrade tests still pass.
9. Transition: user Cancel => zero mutation and zero restart.
10. Transition: Enabled stock timeout + installer succeeded + refreshed topology Stable + verified stock PID1901 => existing ordered startup/HidHide/Center M/restart sequence succeeds once.
11. Transition: refreshed topology Indeterminate, baseline unavailable, capture/verification failure, missing strong identity => no Center M root mutation and no restart.
12. Transition: Enabled + stock baseline previously attempted and failed => cannot invoke this special verification bypass.
13. Transition: Disabled and Partial cases remain governed by their existing policies; no acceptance through Enabled-only proof.
14. Topology diagnostics: no recognized MSI candidates vs classified candidates but control HID missing vs stable-control-HID present yet changing snapshot; no generic gamepad/foreign-controller admission.
15. Info setup decision: the initial blocked/repairable case can be understood without Debug and repeated UI refresh does not flood the log.

Where existing tests encode the normal safe recovery invariant, retain them; do not rewrite broad controller ownership behavior to satisfy this narrow onboarding bug.

Run the available solution build and focused/full relevant automated tests in local Codex. Unit/integration tests may use fakes; **physical device validation is the user's role after merge and must not be a PR blocker**.

## 12. Safety / architecture non-regression checklist

- [ ] Center M startup roots are the only authority source, never a stored first-run toggle.
- [ ] Enabled means MSI stock authority; no live Addon PID1902 takeover during setup.
- [ ] Required driver installation does not claim RecoverySafe or authorize controller ownership.
- [ ] Bounded, freshly verified stock topology + PID1901 are required before the special Enabled->Disabled commit.
- [ ] No unsafe Center M root write / restart on unverified stock state.
- [ ] Correct Windows startup-task registration is verified before disabling Center M.
- [ ] HidHide normalization, exact target, official CLI/Client entries, and pending-reboot rules remain unchanged.
- [ ] VIIPER ownership/teardown and physical input lifecycle remain unchanged.
- [ ] Sleep/Hibernate/Resume, crash/restart, PnP loss, recovery and uninstall stock restoration retain their existing guards.
- [ ] No CTW integration, future multi-session support, generalized authority manager, or pathological-race machinery added.
- [ ] No arbitrary driver repair or installer acquisition/packaging changes.
- [ ] Missing hardware evidence stays marked unknown rather than "fixed" by fabricated AV2M IDs.
- [ ] User-owned real-device testing is explicitly post-merge, not a requirement for Codex's PR.

## 13. Acceptance and implementation report

The code PR is complete when:

1. An accurately simulated AV2M Enabled-stock startup timeout no longer deadlocks an otherwise safely installable HidHide/usbip first install.
2. The existing main-window setup-complete event leads to the existing Center M Disable dialog.
3. A user-confirmed Disable succeeds **only** when newly verified stock controller state and all existing transition guards pass; otherwise MSI authority remains Enabled with a clear reason.
4. No new route into PID1902 ownership, HidHide active isolation, or driver installation is opened for unrelated RecoverySafe=false states.
5. Failure logs at Info identify the setup decision and topology failure class well enough for a later user-supplied AV2M log to resolve the outstanding PnP uncertainty.
6. Focused build/automated tests pass and the implementation summary describes any unresolved physical-device question without pretending it was verified.

Deliver one focused PR unless real code inspection shows an unavoidable tightly coupled prerequisite; avoid splitting into speculative infrastructure PRs. Include in the PR description the source-log timeline, the two independent safety gates, the updated tests, and explicitly state that hardware validation is user-owned after merge.
