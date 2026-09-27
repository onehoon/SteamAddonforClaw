# Work Order — PR13: Full1902 Safe Uninstall Entry, Owned Dependency Teardown, and Managed ClawHUD Cleanup

## Status

Implementation work order for the final user-facing Windows / VeloPack uninstall path that PR12 deliberately left unfinished.

Baseline:

~~~text
repository: onehoon/SteamAddonforClaw
branch: main
baseline: 871dd3d7c303b4889662b9beb18455e46135bbf7
date: 2026-09-27
architecture: standalone Full1902
~~~

This work order is based on the current Full1902 implementation, PR12 stock-safe uninstall core, current HidHide / usbip-win2 provisioning receipts, current managed ClawHUD lifecycle, and a real uninstall where Addon data/log files were removed but this Addon-owned directory remained:

~~~text
SteamInputAddonforClaw-Data\Runtime\ClawHUD
~~~

This is the previously reserved PR13 uninstall completion. CTW integration is out of scope.

---

# 1. Required design authorities

Read and preserve:

1. docs/Full 1902 Implementation/README.md
2. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
3. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
4. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
5. docs/work-order/PR12_STOCK_SAFE_UNINSTALL_CORE_WORK_ORDER.md
6. docs/work-order/CH_A2_CLAWHUD_MANAGED_PROCESS_IPC_AND_TOP_LEVEL_LIFECYCLE_WORK_ORDER_2026-09-21.md
7. docs/work-order/HIDHIDE_USBIP_EXTERNAL_INSTALLER_ACQUISITION_WORK_ORDER.md
8. docs/usbip2/PR1_USBIP_WIN2_PREREQUISITE_SELF_UPGRADE_WORK_ORDER.md
9. docs/work-order/USBIP_0981_DISABLED_BOOT_SELF_UPGRADE_ADMISSION_REPAIR_WORK_ORDER_2026-09-27.md

Inspect the current implementations of at least:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
src/SteamInputAddonforClaw/Install/VelopackAppPaths.cs
src/SteamInputAddonforClaw/Lifecycle/SingleInstanceGate.cs
src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
src/SteamInputAddonforClaw/ClawHud/ClawHudProcessController.cs
src/SteamInputAddonforClaw/ClawHud/ClawHudRuntimeAcquirer.cs
scripts/pack.ps1
~~~

---

# 2. Current production gaps

## 2.1 VeloPack fast callback cannot gate uninstall

Current code uses OnBeforeUninstallFastCallback to run UninstallBootstrap.RunFastCallbackOnly().

VeloPack's official hook contract states that the uninstall hook runs during the critical uninstall stage, has a bounded lifetime, must not show UI, and cannot tell VeloPack to cancel the uninstall.

Therefore:

> OnBeforeUninstallFastCallback cannot be the final Full1902 safety gate.

The supported Windows uninstall entry must run before Update.exe uninstall.

Official VeloPack references:

~~~text
https://docs.velopack.io/integrating/hooks
https://docs.velopack.io/reference/cs/Velopack/VelopackApp
https://docs.velopack.io/reference/cli/content/update-windows
~~~

Do not invent a callback return value or cancellation API.

## 2.2 HidHide and usbip-win2 are never removed today

PR12 intentionally deferred dependency package removal. That boundary was correct for PR12, but PR13 now owns the final dependency-removal policy.

## 2.3 Managed ClawHUD Runtime can survive uninstall

Managed ClawHUD lives under:

~~~text
SteamInputAddonforClaw-Data
└─ Runtime
   └─ ClawHUD
~~~

Current cleanup performs one recursive Directory.Delete and catches the exception. A normal Windows process/file-exit delay can therefore produce partial deletion: logs/settings disappear first, a locked ClawHUD file fails later, and the Runtime folder remains.

That exact residue has now been reproduced.

---

# 3. Goal

The supported uninstall flow must become:

~~~text
Windows Installed Apps / Uninstall
        ↓
Addon safe uninstall entry
        ↓
PR12 stock-safe preparation
        ↓
PID1901/XInput proven
HidHide Addon isolation released
Center M roots Enabled / Enabled / Automatic
Addon startup task removed
        ↓
Managed ClawHUD fully stopped
        ↓
Addon-owned dependency decision
        ├─ owned usbip-win2 -> uninstall
        ├─ pre-existing/unknown usbip -> preserve
        ├─ owned HidHide -> uninstall
        └─ pre-existing/unknown HidHide -> preserve
        ↓
ProgramData provisioning state removed
        ↓
managed ClawHUD cache removed
        ↓
SteamInputAddonforClaw-Data removed
        ↓
ONLY NOW launch VeloPack Update.exe uninstall
~~~

Final rule:

> Restore and prove stock safety first, remove only what the Addon can prove it owns, and only then let VeloPack delete the application.

---

# 4. Preserve PR12 stock-safety ordering exactly

PR13 must call the existing PrepareForUninstallAsync path and preserve:

~~~text
virtual presentation retired
-> process DirectInput released
-> physical MSI Claw proven PID1901 / XInput
-> Addon-owned HidHide controller isolation released
-> Center M roots proven Enabled / Enabled / Automatic
-> Addon startup task removed
-> UninstallPrepared
~~~

Do not:

- clear HidHide first;
- delete the startup task first;
- remove application/data files first;
- infer stock safety from no process owner;
- treat NothingOwned as PID1901 proof;
- continue from Partial/Unavailable Center M truth;
- create another controller authority.

If stock preparation fails, do not start final VeloPack uninstall.

---

# 5. Replace Windows uninstall entry with one safe Addon entry

Current VeloPack 1.2.x source writes the non-MSI uninstall entry at:

~~~text
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\<AppId>
~~~

For this app:

~~~text
AppId = SteamInputAddonforClaw
~~~

VeloPack writes:

~~~text
UninstallString      = "<Root>\Update.exe" --uninstall
QuietUninstallString = "<Root>\Update.exe" --uninstall --silent
~~~

Use the stable root execution stub VeloPack already creates:

~~~text
<RootAppDir>\SteamInputAddonforClaw.exe
~~~

Required values:

~~~text
UninstallString:
"<RootAppDir>\SteamInputAddonforClaw.exe" --safe-uninstall

QuietUninstallString:
"<RootAppDir>\SteamInputAddonforClaw.exe" --safe-uninstall --silent
~~~

Use only:

~~~text
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\SteamInputAddonforClaw
~~~

Before rewriting, verify:

~~~text
InstallLocation == VelopackAppPaths.RootAppDirectory
entry belongs to this exact product/root
existing updater resolves under this RootAppDirectory
~~~

Do not enumerate/rewrite unrelated ARP entries.

VeloPack rewrites its uninstall entry during install/update. Add one small owner such as:

~~~text
SafeUninstallRegistration.EnsureCurrentInstallation()
~~~

Call it from:

~~~text
OnAfterInstallFastCallback
OnAfterUpdateFastCallback
ordinary startup repair
~~~

Fast callbacks do only this registry repair. No controller work.

Do not add a second installed uninstaller EXE unless the stable VeloPack root stub is proven unable to forward --safe-uninstall.

---

# 6. Add one explicit --safe-uninstall mode

Handle this after VelopackApp.Run() has processed VeloPack hook arguments, but before normal Runtime/tray/frontend startup.

Conceptually:

~~~csharp
if (args.Contains(SafeUninstall.Argument, StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = SafeUninstall.Run(
        silent: args.Contains("--silent", StringComparer.OrdinalIgnoreCase));
    return;
}
~~~

This mode must not:

~~~text
check/download updates
launch Main UI
initialize tray
start Overlay
start deferred Full1902 PID1902 ownership
start managed ClawHUD merely to uninstall it
~~~

One uninstall attempt at a time is enough. A single named mutex is acceptable.

Do not build a generalized uninstall state machine.

---

# 7. Existing Runtime case

If the ordinary Runtime is already running:

~~~text
safe uninstall entry
-> signal existing Runtime uninstall request
-> Runtime runs existing PR12 PrepareForUninstallAsync
-> Runtime exits only after success
~~~

Preserve current fail-close behavior:

~~~text
prepare fails
-> Runtime stays alive
-> startup task stays
-> final uninstall aborts
~~~

Wait boundedly for the current-user Runtime mutex to be released.

If it does not release, abort. Do not kill the Runtime to force removal.

---

# 8. Runtime disappearance is not final stock proof

PR12 already established:

~~~text
Runtime disappeared != stock-safe proven
~~~

A Runtime can crash.

After any existing Runtime is gone, independently re-prove current-world stock safety before dependency cleanup.

Preferred path:

~~~text
create headless AddonProcessHost
-> RunStartupAsync()
-> InitializeRuntimeAsync only as far as needed to construct existing authority transition
-> DO NOT StartRuntimeEventWatchers
-> DO NOT StartDeferredRuntimeStartup
-> DO NOT acquire a new PID1902 physical owner
-> PrepareForUninstallAsync()
-> require Succeeded=true
-> DisposeAsync()
~~~

If current InitializeRuntimeAsync starts unrelated deferred ownership, split only the smallest composition needed to reuse the existing CenterMRebootAuthorityTransition.

Do not duplicate the transition or create another native/HidHide/CenterM owner.

Final proof before dependency removal:

~~~text
PID1901
+ HidHide released
+ Center M Enabled
+ startup task absent
~~~

---

# 9. Managed ClawHUD must be fully stopped

Preserve CH-A2:

~~~text
Managed child
-> RequestShutdown
-> bounded wait
-> if proven owned child still alive: Kill(entireProcessTree:true)
~~~

Standalone ClawHUD is not owned and must not be killed.

Current StopAsync can request Kill and immediately treat the child as retired without independently waiting for the killed process to report exit.

For uninstall this can leave executable/DLL handles alive long enough for recursive deletion to fail.

After fallback kill, perform a short bounded WaitForExitAsync before declaring the child retired.

Do not add a process watcher.

For an adopted Managed instance without proven child ownership:

~~~text
RequestShutdown
-> require shutdown confirmation
-> never force kill without ownership proof
~~~

If shutdown cannot be confirmed, abort final uninstall.

---

# 10. Delete managed ClawHUD Runtime explicitly with bounded retry

The following path is fully Addon-owned:

~~~text
AddonDataPaths.ClawHudRuntimeRoot
= SteamInputAddonforClaw-Data\Runtime\ClawHUD
~~~

After ClawHUD shutdown is confirmed, delete this subtree before deleting the whole data root.

Use one small bounded deletion helper:

~~~text
recursive delete
catch IOException / UnauthorizedAccessException
wait about 150-250 ms
retry
maximum about 5 attempts
verify Directory.Exists(path) == false
~~~

This retry is justified by the reproduced normal Windows process-exit/file-lock lifecycle.

Do not add:

~~~text
Restart Manager
filesystem watcher
lock inspector
background cleanup service
unbounded retry loop
~~~

If the path still exists after bounded attempts:

~~~text
abort final uninstall
do not launch VeloPack
~~~

---

# 11. Full Addon data-root cleanup must be verified

Current fire-and-forget cleanup is insufficient.

Return a small result such as:

~~~text
Deleted
AlreadyAbsent
Failed
~~~

or a boolean plus reason.

Required ordering:

~~~text
managed ClawHUD subtree removed
-> finish useful uninstall logs
-> AppLog.Shutdown()
-> bounded delete SteamInputAddonforClaw-Data
-> verify root absent
~~~

Do not write normal AppLog entries after AppLog.Shutdown(), because that would recreate the directory being removed.

If the root cannot be removed, abort VeloPack uninstall.

---

# 12. Preserve Standalone ClawHUD and shared PresentMon Runtime

Delete only the Addon-managed cache.

Do not uninstall/delete:

~~~text
independently installed Standalone ClawHUD
Standalone ClawHUD user settings
Standalone ClawHUD startup registration
shared ClawHUD.PresentMonRuntime package/service
~~~

The shared PresentMon Runtime is not Addon uninstall ownership in this PR.

---

# 13. Dependency policy: remove only proven Addon-owned packages

Never uninstall HidHide or usbip merely because they are present.

Policy:

~~~text
proven Addon-installed dependency
-> eligible for uninstall

pre-existing dependency
-> preserve

unknown/corrupt ownership
-> preserve

externally changed package identity/version
-> preserve

dependency only upgraded by Addon
-> preserve unless durable original ownership proves Addon installed it first
~~~

Use existing provisioning receipts. Do not add a general package ownership database.

---

# 14. HidHide ownership evidence

Current HidHide receipt validity requires PreProvisioningStatus=Missing.

Therefore a valid completed trusted HidHide receipt is strong evidence the Addon originally installed HidHide.

Eligible ownership requires:

~~~text
valid trusted receipt
+ completed/provisioned install state
+ exact supported package identity currently present
~~~

If receipt is absent/corrupt/unsafe/indeterminate, preserve HidHide.

Do not infer ownership from DisplayName alone, version alone, current Active/Inverse state, or current whitelist entries.

---

# 15. usbip ownership must survive Addon-driven upgrades

Current usbip receipt stores:

~~~text
PreInstallationStatus
PreviousInstalledVersion
~~~

Meaning:

~~~text
Missing -> Addon installed from absence
UpdateRequired -> package already existed before this attempt
~~~

Later Addon upgrades overwrite the receipt and can lose original ownership.

Add the smallest durable field, for example:

~~~csharp
bool InstalledByAddon
~~~

Bump only the usbip receipt schema as required.

Migration from v1:

~~~text
v1 + PreInstallationStatus=Missing
-> InstalledByAddon=true

v1 + PreInstallationStatus=UpdateRequired
-> InstalledByAddon=false
~~~

The second mapping is deliberately conservative.

For later upgrades:

~~~text
prior trusted receipt InstalledByAddon=true
-> carry true

otherwise
-> false
~~~

Do not infer original ownership from timestamps or release history.

Do not restore/downgrade an older pre-existing usbip version on uninstall.

Example:

~~~text
user already had 0.9.7.x
Addon upgrades it to 0.9.8.x
Addon later uninstalled
-> preserve current usbip
-> never downgrade
~~~

---

# 16. Add one narrow elevated machine-wide uninstall helper

Reuse the existing self-elevation style.

Add one fixed argument such as:

~~~text
--uninstall-owned-prerequisites
~~~

This helper may only:

~~~text
load/validate provisioning receipts
inspect exact HidHide package identity
inspect exact usbip package identity
uninstall only proven Addon-owned dependencies
verify accepted uninstall result
remove %ProgramData%\SteamInputAddonforClaw\provisioning
remove empty %ProgramData%\SteamInputAddonforClaw
return small result/restart-required status
~~~

Do not make it a generic elevated command runner.

Do not accept arbitrary package names, registry paths, executable paths, or caller-supplied command strings.

---

# 17. Package-specific uninstall command resolution

Do not use winget uninstall.

## usbip-win2

Use only the exact existing key:

~~~text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\
{199505b0-b93d-4521-a8c7-897818e0205a}_is1
~~~

Extend the package-specific probe only as needed for:

~~~text
DisplayVersion
UninstallString
QuietUninstallString
~~~

Only run it when InstalledByAddon=true.

Prefer the registered quiet uninstall command.

If uninstall evidence is missing/malformed, do not guess a command.

## HidHide

Reuse the current exact candidate contract:

~~~text
DisplayName == HidHide
Publisher == Nefarius Software Solutions e.U.
normalized package version valid
~~~

Extend the candidate only as needed to retain exact subkey / Windows Installer evidence / registered uninstaller.

Prefer deterministic Windows Installer uninstall when exact MSI identity is proven; otherwise use the exact registered quiet uninstaller after exact-candidate validation.

If identity is ambiguous, preserve HidHide.

Do not execute an arbitrary same-name ARP command.

---

# 18. Dependency teardown ordering

Only after stock safety is proven:

~~~text
1. VIIPER/presentation already retired
2. PID1901 proven
3. Addon HidHide baseline inactive/released
4. Center M roots Enabled
5. Addon startup task absent
6. Managed ClawHUD stopped
7. uninstall proven Addon-owned usbip if eligible
8. uninstall proven Addon-owned HidHide if eligible
9. remove machine-wide provisioning state
10. delete managed ClawHUD cache
11. delete Addon data root
12. launch VeloPack final uninstall
~~~

Prefer usbip before HidHide.

Never remove either driver while Full1902 virtual/physical ownership is active.

---

# 19. Dependency failure policy

If proven owned dependency uninstall hits:

~~~text
UAC cancelled
uninstaller failed to start
uninstaller returned failure
package removal outcome cannot be verified
~~~

then:

~~~text
abort final Addon uninstall
preserve provisioning receipts
do not launch VeloPack
~~~

The machine is already stock-safe at this point, so retry is safe.

If a dependency was pre-existing or ownership is unknown:

~~~text
preserve dependency
continue uninstall
~~~

That is not a failure.

---

# 20. Reboot policy

HidHide and usbip are driver/kernel dependencies.

Do not claim package-registry removal means the loaded driver disappeared in the current boot.

If either dependency is actually removed:

~~~text
RestartRequired=true
~~~

Do not force an automatic restart.

Interactive uninstall: tell the user before final VeloPack handoff that Windows restart is required after uninstall completes.

Quiet uninstall: no UI and no automatic reboot.

Hardware acceptance requires a complete restart before declaring driver removal complete.

---

# 21. Remove machine-wide provisioning state

Current location:

~~~text
%ProgramData%\SteamInputAddonforClaw\provisioning
    hidhide.json
    usbip-win2.json
~~~

After dependency decisions/removal complete, elevated cleanup removes the exact provisioning directory and then its now-empty parent:

~~~text
%ProgramData%\SteamInputAddonforClaw
~~~

Do not recursively delete an unrelated ProgramData parent.

If exact Addon machine-wide state cannot be removed, fail so uninstall can be retried.

---

# 22. VeloPack final uninstall is the last destructive step

Only after:

~~~text
stock safety proven
dependency cleanup complete/preserved by policy
ProgramData provisioning state removed
Managed ClawHUD cache absent
SteamInputAddonforClaw-Data absent
~~~

launch:

~~~text
<RootAppDir>\Update.exe uninstall
~~~

Quiet path:

~~~text
<RootAppDir>\Update.exe uninstall --silent
~~~

Use the exact updater under current VeloPack root. Do not accept updater paths from command-line input.

---

# 23. Do not let the VeloPack fast hook recreate deleted data

Final VeloPack uninstall invokes the uninstall hook.

Use a non-persistent inherited environment marker when Safe Uninstall launches Update.exe, for example:

~~~text
STEAMADDON_SAFE_UNINSTALL_APPROVED=1
~~~

Then:

~~~text
OnBeforeUninstallFastCallback
+ marker present
-> no stock restoration
-> no dependency work
-> no normal AppLog initialization
-> no Addon data-root recreation
-> return immediately
~~~

If someone manually runs Update.exe uninstall without the marker, retain only best-effort bounded legacy behavior if desired, but do not claim it is cancellable or equivalent to the supported Windows uninstall entry.

Do not persist the approval marker.

---

# 24. Expected files

Likely modifications:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
src/SteamInputAddonforClaw/Install/VelopackAppPaths.cs
src/SteamInputAddonforClaw/Lifecycle/SingleInstanceGate.cs
src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/ClawHud/ClawHudProcessController.cs
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs
~~~

Small focused new files are acceptable:

~~~text
Install/SafeUninstallRegistration.cs
Install/SafeUninstall.cs
Prerequisites/ElevatedOwnedPrerequisiteUninstall.cs
~~~

Do not introduce UninstallManager, DependencyManager, CleanupSupervisor, a service, or a generic state machine.

---

# 25. Required tests — ARP entry

Prove:

~~~text
exact HKCU Addon key only
correct root validation
normal UninstallString -> stable stub --safe-uninstall
QuietUninstallString -> stable stub --safe-uninstall --silent
wrong InstallLocation -> no rewrite
unrelated ARP keys untouched
~~~

Verify repair after install hook, update hook, and ordinary startup.

Use isolated registry seams; never mutate a developer's real uninstall key in tests.

---

# 26. Required tests — stock-safe gate

Cover:

~~~text
running Runtime + prepare succeeds -> continue
running Runtime + prepare fails -> Runtime stays / uninstall aborts
running Runtime crashes -> mutex disappearance is not enough; independent proof still runs
no running Runtime -> independent stock proof still runs
Center M Partial/Unavailable -> abort
physical device missing/ambiguous -> preserve PR12 fail-close behavior
~~~

No artificial instruction-level race tests.

---

# 27. Required tests — ClawHUD cleanup

Cover:

~~~text
graceful managed child exit -> cache deletion allowed
fallback kill -> bounded WaitForExit required
adopted managed instance -> shutdown confirmation required
StandaloneConflict -> standalone process untouched
bounded directory delete transient failure -> eventual success
bounded delete exhausted -> VeloPack never launched
~~~

Assert shared PresentMon Runtime is untouched.

---

# 28. Required tests — dependency ownership

HidHide:

~~~text
valid completed Missing-origin receipt + exact package -> owned
no/corrupt/unsafe receipt -> preserve
package identity changed/ambiguous -> preserve
~~~

usbip:

~~~text
v1 Missing -> InstalledByAddon=true
v1 UpdateRequired -> false
v2 true + later Addon upgrade -> carry true
no prior owned receipt + UpdateRequired -> false
owned=true + exact current package -> eligible uninstall
owned=false/corrupt/unknown -> preserve
~~~

Do not add version-range ownership inference.

---

# 29. Required tests — elevated dependency cleanup

Use fake probes/process runners.

Cover:

~~~text
owned usbip only -> usbip uninstaller only
owned HidHide only -> HidHide uninstaller only
both owned -> usbip before HidHide
both pre-existing -> no package uninstall
UAC cancelled -> fail / receipts preserved
owned uninstaller failure -> fail / VeloPack not launched
success -> ProgramData receipt state removed
driver removed -> RestartRequired=true
~~~

Never run real driver uninstallers in CI.

---

# 30. Required tests — final data/VeloPack handoff

Cover:

~~~text
ClawHUD/data root delete succeeds -> Update.exe launched
data root delete fails -> Update.exe not launched
safe handoff sets STEAMADDON_SAFE_UNINSTALL_APPROVED=1
approved fast hook does not recreate data root
normal path -> Update.exe uninstall
quiet path -> Update.exe uninstall --silent
~~~

---

# 31. Real hardware acceptance A — reproduced ClawHUD residue

On MSI Claw:

~~~text
Center M Disabled
Full1902 active
managed ClawHUD enabled/Ready
managed Runtime cache exists
~~~

Uninstall through Windows Settings.

Before VeloPack removal prove:

~~~text
virtual controller gone
PID1901/XInput
Addon HidHide isolation released
Center M roots Enabled/Enabled/Automatic
Addon startup task absent
Managed ClawHUD and ClawHUD.EcHelper gone
SteamInputAddonforClaw-Data\Runtime\ClawHUD absent
SteamInputAddonforClaw-Data absent
~~~

After VeloPack:

~~~text
app install root absent
ARP entry absent
shortcuts absent
~~~

Exact regression gate:

> SteamInputAddonforClaw-Data\Runtime\ClawHUD must not remain.

---

# 32. Real hardware acceptance B — Addon-owned dependencies

Start with HidHide and usbip absent.

Install Addon, allow its prerequisite flow to install both, then uninstall.

Expected:

~~~text
stock controller restored first
owned usbip removal executed
owned HidHide removal executed
ProgramData Addon provisioning state removed
Addon data root removed
Addon application removed
restart-required notice shown
~~~

After full Windows restart verify both owned packages are absent and stock controller path works.

---

# 33. Real hardware acceptance C — pre-existing dependencies

Install supported HidHide and usbip before installing Addon.

After Addon uninstall:

~~~text
HidHide remains
usbip remains
Addon configuration ownership released
Addon ProgramData/data/app files removed
~~~

Preserved pre-existing dependencies are not failure.

---

# 34. Real hardware acceptance D — pre-existing usbip upgraded by Addon

Example:

~~~text
user has usbip 0.9.7.x
Addon upgrades it to 0.9.8.x
user uninstalls Addon
~~~

Expected:

~~~text
usbip preserved
no downgrade
no reconstruction of old version
~~~

---

# 35. Real hardware acceptance E — safe ARP entry survives updates

Install a pre-PR13 build, update to PR13, and verify the exact Addon ARP UninstallString and QuietUninstallString point to the stable Addon stub with --safe-uninstall.

Apply one more Addon update and verify OnAfterUpdate repair restores the safe entry after VeloPack rewrites it.

---

# 36. No overengineering

Do not add:

~~~text
Windows service
permanent elevated broker
Restart Manager
generic package manager
generic registry command executor
dependency reference-count database
general uninstall state machine
filesystem watcher
unbounded retry scheduler
second controller authority
new HidHide authority
new VIIPER owner
multi-user/RDP/Fast User Switching support
~~~

Supported product assumptions remain one Windows user and one interactive session.

The bounded process-exit/file-delete handling is justified by a real reproduced uninstall failure.

---

# 37. Out of scope

Do not change:

~~~text
VIIPER public ABI
usbip 0.9.8.1 native ABI selection
controller mapping
rumble protocol
SteamDeck/X360 presentation policy
QAM/Overlay semantics
ClawHUD nested HUD settings
shared PresentMon Runtime ownership
Center M enable/disable policy
update download/apply architecture
VeloPack version
~~~

Do not bundle HidHide or usbip installers back into the release package.

---

# 38. Verification

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Release
all test projects
scripts/tests/verify-publish-assets.tests.ps1
scripts/verify-publish-assets.ps1 on publish output
release packaging smoke
~~~

Focused coverage:

~~~text
SafeUninstallRegistration
safe uninstall coordinator
PR12 stock-prepare handoff
ClawHUD fallback-kill wait
bounded ClawHUD/data deletion
HidHide ownership
usbip ownership lineage
elevated dependency removal
ProgramData cleanup
VeloPack final handoff
~~~

---

# 39. Acceptance criteria

PR13 is complete only when:

1. Windows Installed Apps invokes the Addon safe uninstall entry.
2. The safe entry is restored after VeloPack install/update rewrites.
3. The VeloPack fast callback is not treated as cancellable.
4. VeloPack removal never starts before PR12 stock safety is independently proven.
5. Runtime absence/crash is not accepted as stock proof.
6. PID1901/XInput is proven.
7. Addon HidHide isolation is released.
8. Center M roots are Enabled/Enabled/Automatic.
9. Addon startup task is absent.
10. Managed ClawHUD is fully stopped before cache deletion.
11. Fallback kill waits for actual owned-child exit.
12. SteamInputAddonforClaw-Data\Runtime\ClawHUD is reliably removed.
13. Full SteamInputAddonforClaw-Data removal is verified.
14. AppLog cannot recreate the data root after final cleanup.
15. Standalone ClawHUD is untouched.
16. Shared ClawHUD PresentMon Runtime is untouched.
17. HidHide is uninstalled only with proven Addon ownership.
18. usbip is uninstalled only with proven Addon ownership.
19. usbip ownership survives later Addon-driven upgrades with one minimal durable field.
20. Pre-existing/unknown dependencies are preserved.
21. usbip is never downgraded on uninstall.
22. Proven owned dependency uninstall failure blocks final VeloPack removal.
23. ProgramData provisioning state is removed.
24. Driver removal marks Windows restart required.
25. No automatic reboot is forced.
26. No generalized manager/service/state-machine architecture is introduced.
27. Real MSI Claw hardware acceptance passes.

---

# 40. Final architecture

~~~text
Windows Installed Apps
        |
        v
stable Addon stub --safe-uninstall
        |
        +--> request existing Runtime PR12 preparation
        |
        +--> independent headless PR12 stock proof
        |       +--> PID1901
        |       +--> HidHide released
        |       +--> Center M Enabled
        |       +--> startup task absent
        |
        +--> stop/verify managed ClawHUD
        |
        +--> elevated owned-dependency cleanup
        |       +--> owned usbip only
        |       +--> owned HidHide only
        |       +--> remove provisioning receipts
        |
        +--> bounded delete managed ClawHUD cache
        |
        +--> AppLog.Shutdown
        |
        +--> bounded verify-delete Addon data root
        |
        +--> Update.exe uninstall
                |
                +--> VeloPack files / shortcuts / ARP removal
~~~
