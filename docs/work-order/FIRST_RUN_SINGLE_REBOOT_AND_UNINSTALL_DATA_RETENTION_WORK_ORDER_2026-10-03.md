
# Work Order — First-Run Automatic Controller Prerequisites, Single-Reboot Center M Onboarding, and Uninstall Data Retention

## Status

Implementation work order for the current standalone Full1902 product.

Baseline:

~~~text
repository: onehoon/SteamAddonforClaw
branch: main
review date: 2026-10-03
architecture: standalone Full1902
~~~

CTW integration is out of scope.

This work order combines four user-facing lifecycle corrections that are related by first-run and uninstall behavior:

1. HidHide and usbip-win2 install automatically when safely installable; remove the redundant Addon Install / Not now dialog.
2. After prerequisite installation, show the existing explicit MSI Center M Disable and Restart confirmation.
3. Consume prerequisite reboot requirements in the same final Center M restart whenever exact current-boot package evidence makes this safe, so the normal first-run path uses one Windows restart total.
4. During uninstall, suppress usbip child restart UI and preserve user settings/profiles/shortcuts/logs while still removing Addon-owned machine/runtime state.

The target UX is:

~~~text
fresh install
→ Addon UI opens
→ required HidHide + usbip-win2 setup starts automatically
→ Windows UAC only
→ both prerequisite installers finish without restarting Windows
→ explicit MSI Center M Disable and Restart confirmation
→ one Windows restart
→ next boot performs normal Full1902 admission and ownership

uninstall
→ stock safety proven
→ Addon-owned prerequisites removed silently
→ only Addon restart notice shown
→ app binaries removed
→ settings/profiles/shortcuts/logs remain
~~~

---

# 1. Required design authorities

Read and preserve in this order:

1. docs/Full 1902 Implementation/README.md
2. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
3. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
4. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
5. docs/work-order/FRESH_INSTALL_SETTINGS_LOGGING_AND_CONTROLLER_PREREQUISITE_REPAIR_WORK_ORDER_2026-10-03.md
6. docs/work-order/PR12_STOCK_SAFE_UNINSTALL_CORE_WORK_ORDER.md
7. docs/work-order/PR13_FULL1902_SAFE_UNINSTALL_ENTRY_DEPENDENCY_AND_CLAWHUD_CLEANUP_WORK_ORDER_2026-09-27.md
8. docs/work-order/HIDHIDE_USBIP_EXTERNAL_INSTALLER_ACQUISITION_WORK_ORDER.md
9. docs/usbip2/PR1_USBIP_WIN2_PREREQUISITE_SELF_UPGRADE_WORK_ORDER.md

Review the current implementations of at least:

~~~text
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs

src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs

src/SteamInputAddonforClaw/Prerequisites/FirstTimeSetup.cs
src/SteamInputAddonforClaw/Prerequisites/PrerequisiteSetupPromptPolicy.cs
src/SteamInputAddonforClaw/Prerequisites/PrerequisiteSetupExecutionPolicy.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedOwnedPrerequisiteUninstall.cs
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs

src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw/Install/SafeUninstall.cs
src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
~~~

---

# 2. Current production behavior that must change

## 2.1 Redundant prerequisite confirmation

Current MainWindow flow is:

~~~text
CanInstallRequiredComponents=true
→ PromptForPrerequisiteSetupAsync
→ Setup required dialog
→ Install / Not now
→ RunPrerequisiteSetupAsync
→ Windows UAC
~~~

For Full1902, choosing Not now leaves mandatory controller prerequisites absent and the controller path unusable. Windows UAC is already the real user consent/elevation boundary.

## 2.2 Separate prerequisite reboot creates a two-reboot setup

Current MainWindow handles prerequisite RebootRequired by showing Restart now / Later and invoking shutdown.exe directly.

That creates:

~~~text
HidHide + usbip install
→ restart
→ user later disables Center M
→ second restart
~~~

The intended normal clean-install UX is:

~~~text
HidHide + usbip install
→ Center M confirmation
→ one final restart
~~~

## 2.3 Center M Disable currently requires current-boot prerequisite readiness

Current CenterMRebootAuthorityTransition.DisableAsync requires:

~~~text
RecoverySafe=true
Prerequisites.IsRoutingReady=true
current-boot HidHide baseline inspect/apply succeeds
~~~

This remains correct for an ordinary already-ready transition.

A narrow additional path is needed when the Addon has just installed the exact supported prerequisite package and the only remaining condition is a Windows reboot.

## 2.4 Normal uninstall deletes all persistent user data

SafeUninstall currently performs complete SteamInputAddonforClaw-Data deletion before VeloPack handoff.

This removes:

~~~text
settings.json
profiles.json
shortcuts.json
logs~~~

and prevents post-uninstall diagnosis.

## 2.5 usbip child uninstaller can own restart UI

Current ElevatedOwnedPrerequisiteUninstall uses the exact registered usbip uninstaller but executes its arguments as registered. The current fixture uses /VERYSILENT but does not guarantee /SUPPRESSMSGBOXES /NORESTART.

The Addon already tracks RestartRequired and shows the final uninstall restart notice, so dependency child restart UI must be suppressed.

---

# 3. First-run product contract

The supported clean-install flow becomes:

~~~text
Main UI foreground/activated
→ fresh status capture
→ CanInstallRequiredComponents=true
→ no Addon Install / Not now dialog
→ automatic RunPrerequisiteSetupAsync
→ Windows UAC
    Cancel:
        stop setup for this process
        do not loop UAC immediately
        do not show Center M confirmation

    Approve:
        existing ElevatedPrerequisiteSetup
        HidHide install/verify
        usbip-win2 install/verify
        no child restart
        Installed or RebootRequired
        → explicit Center M confirmation
        → user chooses Disable and Restart
        → backend verifies next-boot commit safety
        → one Windows restart
        → normal DisabledBootAdmission on next boot
~~~

Center M must never be disabled automatically.

The user's explicit Disable and Restart choice remains mandatory.

---

# 4. Required change A — remove Addon prerequisite Install / Not now UI

When snapshot.CanInstallRequiredComponents is true:

1. preserve the existing foreground/activation behavior;
2. start the existing prerequisite setup automatically;
3. let the existing elevated child trigger Windows UAC;
4. keep ElevatedPrerequisiteSetup as the sole package mutation owner.

Remove the user-decision role of:

~~~text
_setupPromptActive
_setupPromptDeclinedForCurrentProcess
PromptForPrerequisiteSetupAsync
Setup required
Install
Not now
~~~

Rename/reuse only the process guards that remain necessary for automatic setup.

Do not move prerequisite installation into:

~~~text
VeloPack install/update callbacks
a service
a scheduled task
a background downloader
a second installer/bootstrap owner
~~~

---

# 5. Required change B — UAC cancel must not loop in the same process

Status refresh can occur repeatedly.

Required behavior:

~~~text
automatic prerequisite setup
→ UAC Cancel
→ FrontendPrerequisiteSetupResultKind.Cancelled
→ set one process-lifetime cancellation guard
→ later status refresh in same process does not immediately launch UAC again
~~~

A new application process may retry if setup remains safely installable.

Do not persist this guard.

Do not add timers, retry managers, epochs, or a setup state machine.

---

# 6. Required change C — prerequisite completion flows directly to Center M confirmation

After RunPrerequisiteSetupAsync returns:

### Ready / Installed

Proceed directly to the explicit Center M Disable confirmation.

### RebootRequired

Do not show the current generic prerequisite Restart required dialog.

Do not invoke shutdown.exe from MainWindow.

Proceed directly to the same explicit Center M Disable confirmation.

### Cancelled

Do not show Center M confirmation.

Set the process cancellation guard and return.

### Blocked / Failed / AlreadyInProgress

Preserve current error reporting and do not enter Center M transition.

### NotInstallable

Do not infer that authority can be changed. Preserve current status/error behavior.

---

# 7. Required change D — reuse the existing Center M confirmation path

The current DevicePage confirmation remains the product authority confirmation:

~~~text
Disable MSI Center M and switch controller authority to Steam Addon for Claw.

Windows must restart to apply this change.

[Cancel] [Disable and Restart]
~~~

Refactor only enough so MainWindow can request this same confirmation immediately after prerequisite setup.

Acceptable shape:

~~~text
DevicePage internal setup entry
→ same confirmation UI
→ same RequestCenterMAuthorityTransitionAsync(false)
~~~

Requirements:

- Cancel sends zero Center M mutation request.
- Primary sends exactly one request.
- normal Device-page button behavior remains unchanged.
- normal already-Disabled card may keep its redundant Disable button disabled.
- onboarding may still show this confirmation when Center M is already exactly Disabled.
- do not add a new frontend RPC solely for onboarding unless current code proves unavoidable.

---

# 8. Required change E — one-reboot verified pending-prerequisite policy

## 8.1 Ordinary Ready path remains unchanged

When current Runtime prerequisites are Ready and RecoverySafe is true, preserve existing Disable order:

~~~text
lower-level runtime safety
→ current prerequisite Ready proof
→ zero-target HidHide inspect
→ mandatory Addon startup registration
→ zero-target HidHide apply/readback
→ Center M roots Disabled/readback
→ immediate restart
~~~

Do not weaken this path.

## 8.2 Narrow next-boot pending path

A non-ready HidHide or usbip prerequisite may be accepted only for next-boot authority commit when the Addon can prove it is an exact current-boot installation pending only on restart.

This does not make current Runtime routing-ready.

This does not allow PID1902 takeover in the current session.

This does not allow VIIPER presentation in the current session.

Use existing package probes, package metadata, provisioning receipts, BootSession logic, and version policies.

For each pending prerequisite require at minimum:

~~~text
provisioning storage trusted
receipt valid and non-corrupt
receipt State == InstalledPendingReboot
receipt installer version == current pinned target
receipt installer SHA == current pinned installer SHA
receipt observed installed version == exact target
current package probe succeeds
current package exists/is installed
current package version/identity == exact supported target
receipt belongs to the current boot
~~~

VIIPER must still satisfy the current supported readiness contract. Missing/corrupt VIIPER is not a pending-reboot exception.

If evidence is missing, corrupt, ambiguous, stale, wrong-version, foreign, or storage is unsafe:

~~~text
do not disable Center M
do not request restart through authority transition
fail closed
~~~

Prefer one small pure policy/helper or one narrow inspector function.

Do not create a new manager/service/authority object.

---

# 9. Narrow Full1902 policy revision for combining the reboot

Earlier Full1902 design applies the zero-target HidHide baseline before disabling Center M.

Keep that rule for the ordinary Ready path.

For the exact verified InstalledPendingReboot path only:

~~~text
current session remains MSI/stock
no live PID1901→PID1902 takeover
no current-session physical hiding
no VIIPER attach
verify exact pending package evidence
→ verify mandatory Addon startup registration
→ set and verify Center M startup roots Disabled
→ immediate Windows restart
→ next boot runs existing DisabledBootAdmission
→ next boot proves actual prerequisite Ready state
→ next boot normalizes/readbacks HidHide baseline
→ only then PID1902 ownership/presentation may begin
~~~

This is the basis for a one-reboot onboarding flow.

Do not mark the next boot Ready from the receipt.

The next boot must inspect actual current Windows/device state again.

If next-boot prerequisites are not Ready, existing DisabledBootAdmission must block physical ownership and VIIPER.

---

# 10. Required change F — Center M already Disabled must still be reconfirmable after setup

After successful prerequisite setup, show the same Center M confirmation even when current startup roots are already exactly Disabled.

This is intentional onboarding confirmation.

Add a narrow idempotent Disable recommit behavior for RequestCenterMAuthorityTransitionAsync(false) when current state is already Disabled.

Required behavior:

~~~text
explicit user confirmation
→ current Center M state exactly Disabled
→ no unsafe lower-level mutation/transition in progress
→ prerequisites currently Ready OR exact current-boot pending evidence valid
→ mandatory Addon startup registration verified
→ do not apply zero-target HidHide baseline over a live PID1902 owner
→ do not release/reacquire physical ownership
→ do not issue redundant physical PID command
→ no redundant Center M startup write is required
→ request the final Windows restart
~~~

Important:

- an already-Disabled active Runtime may have an exact hidden PID1902 target; never overwrite it with an empty target set simply to reuse the Enabled→Disabled code path.
- Partial or Unavailable Center M state remains fail-closed.
- do not expose the already-Disabled Device card as a general repeated restart button.
- this recommit exists to complete explicit onboarding safely.

---

# 11. Do not add persisted onboarding authority state

Do not add:

~~~text
FirstRunManager
ControllerSetupCompleted
AddonControllerModeEnabled
OnboardingState
CenterMSetupPending
~~~

The required flow is immediate:

~~~text
automatic prerequisite setup returns
→ show Center M confirmation in the same frontend process
~~~

If the user cancels Center M confirmation:

~~~text
Center M state unchanged
no forced restart
no later automatic authority mutation
~~~

The existing Device-page control remains the later manual path.

---

# 12. Preserve prerequisite installer no-restart behavior

Current install arguments are already correct and must remain:

HidHide:

~~~text
/exenoui /qn /norestart
~~~

usbip-win2:

~~~text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /TYPE=compact /NOICONS
~~~

When a child returns 3010:

~~~text
record InstalledPendingReboot
do not let child restart
do not show separate prerequisite restart UI
attempt the explicit Center M flow only if next-boot pending evidence is exact
consume reboot in final Disable and Restart
~~~

---

# 13. Required change G — suppress usbip uninstall restart UI

Keep current exact ownership and registered-uninstaller identity verification.

After exact usbip registered command resolution, make the effective command silent/no-message/no-restart for the pinned Inno Setup package:

~~~text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
~~~

Do not modify RegisteredUninstallCommandResolver globally only for this behavior.

Prefer package-specific argument hardening immediately before RunUninstaller("usbip-win2", ...).

Continue to:

~~~text
accept exit 0 or 3010 only
re-probe package after uninstall
require package entry absent
set Addon-level RestartRequired=true after actual proven removal
~~~

HidHide behavior is unchanged unless real validation shows a separate child-UI problem. The MSI path already uses /qn /norestart.

Do not build a generalized uninstaller parser. Narrow normalization of a contradictory known Inno restart switch is acceptable if current package evidence requires it.

---

# 14. Required change H — Addon remains the sole uninstall restart-notification owner

Interactive uninstall:

~~~text
stock-safe preparation succeeds
→ dependency cleanup succeeds
→ Addon-owned driver removal requires restart
→ Addon shows one final restart-required notice
→ child uninstallers show no restart dialog
→ child uninstallers never restart Windows
→ VeloPack uninstall continues
~~~

Silent uninstall:

~~~text
no UI
no automatic restart
~~~

Do not remove RestartRequired semantics from OwnedPrerequisiteUninstallResult.

---

# 15. Required change I — preserve user data and logs on normal uninstall

Normal uninstall is not a factory reset.

Preserve when present:

~~~text
SteamInputAddonforClaw-Datasettings.json
SteamInputAddonforClaw-Dataprofiles.json
SteamInputAddonforClaw-Datashortcuts.json
SteamInputAddonforClaw-Datalogs~~~

Reasons:

- settings survive reinstall;
- Device/Game profiles survive reinstall;
- shortcuts survive reinstall;
- uninstall logs remain available for diagnosis.

Do not copy these to a backup location.

Do not rewrite them during uninstall.

The existing persistent sibling data root is already outside VeloPack application binaries.

---

# 16. Required change J — remove recursive data-root deletion from all normal uninstall paths

SafeUninstall must no longer delete AddonDataPaths.RootDirectory before VeloPack handoff.

Refactor the current final helper so it does:

~~~text
finish safety/runtime cleanup
→ log final uninstall preparation result and preservation decision
→ flush/shutdown AppLog
→ keep SteamInputAddonforClaw-Data
→ launch VeloPack uninstall
~~~

UninstallBootstrap.RunFastCallbackOnly must also never recursively remove the user data root.

Current RunBoundedLocalCleanup has a deleteDataRoot behavior/default. Remove or neutralize that normal-uninstall responsibility.

Preferred final contract:

~~~text
RunBoundedLocalCleanup
= bounded feature/machine ownership cleanup only
= never user-data reset
~~~

AddonDataPaths.DeleteFullResetRoot may remain only if a real explicit reset/factory-reset caller exists or is intentionally retained as a reset primitive. It must not be called by normal uninstall.

---

# 17. Continue deleting real Addon-owned runtime/machine artifacts

User-data preservation must not weaken uninstall cleanup.

Continue removing/retiring, where ownership is proven:

~~~text
Addon-owned HidHide package
Addon-owned usbip-win2 package
ProgramData provisioning state
mandatory Addon startup task after stock restoration
legacy Steam CEF ownership marker
owned Intel FPS limiter state after cleanup
owned Windows Gaming Home/FSE state
SteamInputAddonforClaw-DataRuntimeClawHUD
~~~

PR12 stock restoration order remains authoritative:

~~~text
virtual presentation retired
→ DirectInput/physical ownership released
→ PID1901 independently proven
→ Addon HidHide isolation released
→ Center M roots Enabled/readback
→ startup task removed
~~~

Do not delete evidence first and then assume cleanup succeeded.

---

# 18. Retained uninstall log contract

Before final VeloPack handoff, the log should contain enough evidence to diagnose:

~~~text
safe uninstall entry
stock preparation result
PID1901 proof
HidHide authority release
Center M Enabled/readback
startup task removal
Managed ClawHUD stop
usbip uninstall result
HidHide uninstall result
ProgramData provisioning cleanup
bounded local ownership cleanup
ClawHUD runtime deletion
RestartRequired value
user-data/log preservation decision
VeloPack handoff starting
~~~

No post-uninstall supervisor is required.

It is sufficient that Addon safety-critical teardown is diagnosable after binaries are removed.

---

# 19. Failure policy

## UAC Cancel

~~~text
no completed prerequisite setup
no Center M confirmation
no immediate same-process UAC loop
no authority mutation
~~~

## Prerequisite install/verification failure

~~~text
no Center M authority commit
no forced restart
~~~

## Center M confirmation Cancel

~~~text
keep actual prerequisite state
keep current Center M authority
do not force restart
~~~

## Pending-reboot proof invalid

~~~text
do not disable Center M
do not infer next-boot readiness
~~~

## Next boot after committed one-reboot setup is not actually Ready

~~~text
DisabledBootAdmission != Ready
→ no PID1902 physical acquisition
→ no VIIPER presentation
→ Runtime/UI remain available for repair
~~~

## Uninstall machine-state failure

If stock safety, dependency removal, or required owned-state cleanup cannot be proven:

~~~text
fail closed
do not launch final VeloPack uninstall
~~~

Preserving settings/logs does not weaken machine-state safety.

---

# 20. No overengineering

Do not add:

~~~text
FirstRunManager
OnboardingManager
new Windows service
driver supervisor
persistent authority/onboarding epoch
generic rollback transaction
generic uninstaller framework
filesystem watcher
post-uninstall monitor
multi-session support
~~~

Keep the existing owners:

~~~text
ElevatedPrerequisiteSetup
    package mutation

CenterMRebootAuthorityTransition
    reboot-bound controller authority

DisabledBootControllerAdmission
    next-boot runtime admission

ElevatedOwnedPrerequisiteUninstall
    owned dependency removal

SafeUninstall / UninstallBootstrap
    uninstall sequencing
~~~

At most add one small pure/narrow next-boot prerequisite verification helper to distinguish:

~~~text
current Runtime Ready
vs
exact current-boot InstalledPendingReboot safe for next-boot commit
~~~

---

# 21. Required focused tests

## 21.1 Automatic setup UI

Update UI tests so they prove:

~~~text
CanInstallRequiredComponents=true still triggers setup
old Setup required / Install / Not now dialog is absent
foreground activation still occurs before automatic elevation
only one setup runs concurrently
UAC Cancel prevents same-process immediate retry
Installed leads to Center M confirmation
RebootRequired leads to Center M confirmation
RebootRequired no longer calls prerequisite restart dialog/shutdown.exe
Blocked/Failed do not invoke Center M authority transition
~~~

Use existing source-level/pure policy test style; do not add a heavyweight UI automation framework.

## 21.2 Center M confirmation reuse

Prove:

~~~text
Cancel → zero RequestCenterMAuthorityTransitionAsync(false)
Primary → exactly one request
already Disabled onboarding → confirmation is still reachable
normal already-Disabled card button may remain disabled
~~~

## 21.3 Ordinary Ready transition regression

Keep tests proving:

~~~text
safety
→ current admission Ready
→ startup registration
→ HidHide baseline apply
→ Center M Disable
→ restart
~~~

Do not remove baseline application from the ordinary path.

## 21.4 Verified pending transition

Cover realistic cases:

~~~text
HidHide pending only
usbip pending only
both pending
~~~

When evidence is exact and VIIPER contract is satisfied:

~~~text
next-boot commit allowed
no false current-runtime Ready claim
no current-session PID takeover
no current-session pending HidHide baseline apply
Center M roots Disabled/readback
one restart request
~~~

Reject before Center M mutation for representative evidence failures:

~~~text
corrupt/missing receipt
wrong installer version
wrong installer hash
observed version mismatch
package probe failure
package version mismatch
pending receipt from prior boot
unsafe provisioning storage
VIIPER unavailable
~~~

Avoid combinatorial test explosion.

## 21.5 Already-Disabled recommit

Prove:

~~~text
Center M exactly Disabled
+ explicit Disable confirmation
+ prerequisites Ready or exact pending evidence
→ startup registration verified
→ no empty-target HidHide rewrite
→ no physical ownership churn
→ no redundant Center M startup mutation needed
→ one restart request
~~~

Partial/Unavailable must fail closed.

## 21.6 usbip uninstall

Update current test fixture expectations so the same exact uninstaller executable receives effective:

~~~text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
~~~

Cover quiet command and fallback UninstallString path.

Keep exit/readback behavior unchanged.

## 21.7 Persistent data retention

Create temporary data containing:

~~~text
settings.json
profiles.json
shortcuts.json
logslaunch.log
RuntimeClawHUD...
~~~

After successful safe uninstall preparation + handoff:

~~~text
settings.json exists
profiles.json exists
shortcuts.json exists
logslaunch.log exists
RuntimeClawHUD absent
VeloPack launch requested
safe-uninstall approval marker set
~~~

Also prove fast-callback bounded cleanup never recursively deletes the data root.

If VeloPack launch fails, persistent user data must still remain.

---

# 22. Real hardware validation

## Scenario A — clean install, Center M Enabled

Start:

~~~text
HidHide missing
usbip-win2 missing
Center M Enabled
~~~

Expected:

~~~text
Addon opens
→ no Install / Not now dialog
→ UAC
→ prerequisite installers silent/no restart
→ Center M Disable and Restart confirmation
→ user confirms
→ exactly one Windows restart
→ next boot prerequisite Ready proof
→ DisabledBootAdmission Ready
→ HidHide baseline exact
→ PID1902 owned
→ expected X360/SteamDeck presentation
~~~

## Scenario B — installer returns 3010

Expected:

~~~text
no intermediate prerequisite reboot dialog
exact InstalledPendingReboot evidence retained
Center M confirmation
one final restart total
~~~

## Scenario C — Center M already Disabled

Expected:

~~~text
automatic prerequisite setup
→ Center M confirmation still shown
→ user confirms
→ no current-session empty-target HidHide overwrite
→ no redundant physical takeover
→ one restart
→ normal Disabled boot reconcile
~~~

## Scenario D — UAC Cancel

Expected:

~~~text
no Center M confirmation
no authority mutation
no same-process UAC loop
~~~

## Scenario E — Center M Cancel

Expected:

~~~text
no Center M mutation
no forced restart
~~~

## Scenario F — uninstall

Expected:

~~~text
stock PID1901 restored/proven
Center M Enabled/readback
startup task removed
usbip child shows no restart UI
HidHide child shows no restart UI
Addon shows one restart notice
ClawHUD cache removed
VeloPack removes app
no dependency child auto-restart
~~~

After uninstall verify:

~~~text
%LOCALAPPDATA%SteamInputAddonforClaw-Datasettings.json remains
profiles.json remains if created
shortcuts.json remains if created
logs remains and contains final uninstall log
~~~

## Scenario G — reinstall

Expected:

~~~text
retained settings/profiles/shortcuts load normally
old logs remain subject only to normal log-retention policy
Windows/package/controller facts are re-inspected
missing owned prerequisites install again automatically
~~~

Retained settings are never authority evidence.

---

# 23. Expected implementation footprint

Likely production files:

~~~text
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs

src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw/Prerequisites/PrerequisiteSetupExecutionPolicy.cs
and/or one small next-boot prerequisite verification helper
src/SteamInputAddonforClaw/Prerequisites/ElevatedOwnedPrerequisiteUninstall.cs

src/SteamInputAddonforClaw/Install/SafeUninstall.cs
src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs only if helper/comments need adjustment
~~~

Prefer no change to:

~~~text
public IAddonFrontendControl protocol shape
frontend named-pipe RPC enum/version
physical ownership owner
VIIPER owner
DisabledBootControllerAdmission
~~~

unless implementation evidence requires it.

Likely tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendPrerequisiteSetupBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/PrerequisiteSetupPromptPolicyTests.cs
tests/SteamInputAddonforClaw.Tests/CenterMRebootAuthorityTransitionTests.cs
tests/SteamInputAddonforClaw.Tests/ElevatedOwnedPrerequisiteUninstallTests.cs
tests/SteamInputAddonforClaw.Tests/SafeUninstallCleanupTests.cs
tests/SteamInputAddonforClaw.Tests/UninstallBootstrapTests.cs
~~~

---

# 24. Acceptance criteria

Implementation is complete only when:

1. Missing safely-installable HidHide/usbip automatically invokes existing setup after foreground activation.
2. Addon Install / Not now prerequisite dialog is gone.
3. UAC remains required and Cancel does not loop in the same process.
4. prerequisite child installers never restart Windows.
5. successful prerequisite setup always leads to explicit Center M confirmation, including already-Disabled Center M.
6. Center M never disables automatically.
7. exact current-boot InstalledPendingReboot can be accepted for next-boot commit without being reported current-runtime Ready.
8. stale/unverified/wrong-version pending state still blocks.
9. ordinary Ready Disable keeps current HidHide baseline apply/readback behavior.
10. already-Disabled recommit never overwrites a live hidden-target baseline with an empty set.
11. normal clean-install success uses one Windows restart total.
12. next boot still requires fresh DisabledBootAdmission before PID1902/VIIPER.
13. usbip uninstall is silent/no-message/no-restart while keeping exact executable identity and readback verification.
14. actual owned driver removal still sets Addon RestartRequired.
15. interactive uninstall has only the Addon restart notice; silent uninstall has no UI.
16. normal uninstall never recursively deletes SteamInputAddonforClaw-Data.
17. settings.json, profiles.json, shortcuts.json, and logs survive uninstall.
18. Managed ClawHUD runtime/cache and proven Addon-owned machine state are still removed.
19. PR12 stock restoration/fail-close ordering is unchanged.
20. reinstall reuses user configuration but derives authority/prerequisite state from current Windows reality.
21. no new generalized manager or speculative race-defense architecture is introduced.

---

# 25. Review focus

Focus review on realistic lifecycle behavior:

~~~text
clean install
UAC cancel
installer failure / 3010
one-reboot authority commit
Center M already Disabled
restart and DisabledBootAdmission
uninstall stock restoration
owned dependency teardown
VeloPack handoff
retained settings/logs
reinstall
~~~

Do not block on pathological instruction-level timing combinations.

Critical invariants:

> No physical Addon controller ownership without fresh next-boot proof.

> No automatic Center M authority mutation without explicit user confirmation.

> No unnecessary prerequisite reboot before the already-required authority reboot when exact pending evidence makes one reboot safe.

> Normal uninstall removes Addon-owned machine/runtime state but preserves user configuration and diagnostic history.
