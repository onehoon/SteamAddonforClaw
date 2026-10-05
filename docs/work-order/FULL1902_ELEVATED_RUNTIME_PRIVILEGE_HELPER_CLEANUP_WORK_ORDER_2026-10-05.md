# Work Order — Full1902 Elevated Runtime Privilege / Helper Cleanup

> **Status:** Ready for implementation  
> **Date:** 2026-10-05  
> **Baseline:** PR #681 merged; the normal Full1902 Runtime is High integrity under the same interactive administrator user before Runtime/controller ownership begins.  
> **Goal:** Remove privilege-escalation layers that became redundant after the Runtime moved High, while preserving process boundaries that still provide real installer/cleanup/fault-containment value.

---

## 0. Required reading

Read before implementation:

- \`docs/Full 1902 Implementation/README.md\`
- \`docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md\`
- \`docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md\`
- \`docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md\`
- \`docs/work-order/FULL1902_ELEVATED_RUNTIME_WORK_ORDER_2026-10-05.md\`

This is a standalone Full1902 application. CTW integration is not part of this work.

The supported Windows model remains:

~~~text
one interactive Windows user
that user is a member of Administrators
one interactive session
no Fast User Switching
no RDP / multi-session support
~~~

Over-the-shoulder elevation into another administrator identity remains unsupported.

---

# 1. Cleanup principle

Before PR #681, several components used:

~~~text
Medium Runtime
=> runas
=> privileged child/helper
=> perform one privileged operation
~~~

That pattern is no longer automatically justified because the Runtime itself is now:

~~~text
Runtime [High]
~~~

The cleanup rule is:

> **Remove a child/helper when its only meaningful purpose was privilege escalation.**
>
> **Keep a child/helper when it still protects a real process boundary such as WMI hang containment, bounded COM/service mutation, installer lifetime, temporary-system-state cleanup, or external-command timeout.**
>
> For a retained child/helper, inherit the Runtime's High token instead of creating another UAC/elevation authority.

Do not optimize for the fewest processes at the expense of real lifecycle failure containment.

Do not retain a helper solely because it already exists if its only authority was "the Runtime is medium."

---

# 2. Current-code audit and decisions

## 2.1 Normal Runtime self-elevation — KEEP

Current:

~~~text
asInvoker packaged main EXE
=> Velopack bootstrap / special bootstrap
=> same-user SID validation
=> ShellExecute runas
=> Runtime High
~~~

Decision:

**KEEP.**

Reason:

- the packaged main EXE must remain \`asInvoker\` for Velopack;
- manual normal application launch still needs one UAC consent boundary;
- this is now the **one intentional privilege-escalation authority** for the application lifecycle.

After this cleanup, production source should not contain feature-local \`runas\` paths that duplicate this authority.

---

## 2.2 Startup-task self-elevation — REMOVE COMPLETELY

Current production composition:

~~~text
AddonRuntimeCompositionFactory
=> WindowsTaskSchedulerStartupManager.WithElevatedRepair()
=> SelfElevatedStartupTaskInvoker
=> runas SteamInputAddonforClaw.exe --ensure-startup-task / --remove-startup-task
=> ElevatedStartupTaskSetup
=> another WindowsTaskSchedulerStartupManager writes the task
~~~

This existed because the old Runtime was medium.

The Runtime is now already High before this composition exists.

Decision:

**DELETE the self-elevation layer.**

Keep:

- one \`WindowsTaskSchedulerStartupManager\`;
- exact fixed Addon task identity;
- \`TASK_LOGON_INTERACTIVE_TOKEN\`;
- \`TASK_RUNLEVEL_HIGHEST\`;
- priority 2;
- battery-safe settings;
- PT0S execution time;
- exact readback verification;
- bounded readback settle;
- fail closed on real Task Scheduler failure.

Delete:

~~~text
IElevatedStartupTaskInvoker
SelfElevatedStartupTaskInvoker
ElevatedStartupTaskOutcome
ElevatedStartupTaskSetup
--ensure-startup-task
--remove-startup-task
WithElevatedRepair()
_elevatedInvoker
all UAC-cancel semantics specific to task repair/removal
~~~

Production composition becomes conceptually:

~~~csharp
var startupRegistration = new WindowsTaskSchedulerStartupManager();
~~~

### Important readback detail

Do not regress the old helper path's bounded Task Scheduler settle behavior.

After a direct High-integrity \`Register(...)\`:

~~~text
write once
=> ReadBackVerifyWithBoundedSettle(...)
=> success only on exact contract
~~~

After direct \`Delete()\`:

~~~text
delete once
=> ReadBackVerifyAbsentWithBoundedSettle()
=> success only on proven absence
~~~

Do not replace the helper with retries or another task writer.

If a High Runtime still receives AccessDenied or another real operation failure, report failure. Do not create another elevation fallback.

---

## 2.3 SafeUninstall owned-prerequisite self-elevation — REMOVE COMPLETELY

Current after PR #681:

~~~text
Program
=> safe-uninstall passes the same-user elevation gate
=> SafeUninstall [High]
=> RunElevatedDependencyCleanup()
=> runas same SteamInputAddonforClaw.exe
=> --uninstall-owned-prerequisites
=> ElevatedOwnedPrerequisiteUninstallEntry
=> ElevatedOwnedPrerequisiteUninstall.Execute()
~~~

The second elevation is redundant.

It also does not provide strong fault isolation:

- the child wrapper waits up to six minutes;
- the actual registered uninstall runner already has its own five-minute per-process wait;
- on wrapper timeout the parent does not own a stronger rollback/kill guarantee;
- multiple dependency uninstall operations can legitimately exceed the wrapper's single aggregate wait.

Decision:

**DELETE the self-child and call the owned prerequisite cleanup directly from the already-High SafeUninstall coordinator.**

Target:

~~~text
SafeUninstall [High]
=> OwnedPrerequisiteUninstall.Execute()
=> exact ownership proofs
=> registered usbip/HidHide uninstallers remain external child processes
=> exact package readback
~~~

Delete:

~~~text
RunElevatedDependencyCleanup(...)
ElevatedOwnedPrerequisiteUninstallEntry
--uninstall-owned-prerequisites
ElevatedHelperWaitBudgetMilliseconds
UAC-cancel handling for this internal cleanup hop
~~~

Rename, if reference closure is clean:

~~~text
ElevatedOwnedPrerequisiteUninstall
=> OwnedPrerequisiteUninstall
~~~

The registered dependency uninstallers themselves remain child processes. Do not inline third-party uninstall executables into the Runtime.

Preserve:

- ownership receipts;
- exact package/version proof;
- fail-close preservation when ownership is not proven;
- 0 / 3010 handling;
- post-uninstall package verification;
- provisioning-directory cleanup;
- safe uninstall ordering.

---

## 2.4 Steam FSE registration worker — KEEP PROCESS, REMOVE RUNAS

Current:

~~~text
Runtime
=> SteamFseRegistrationClient
=> runas same main EXE --register-fse-home
=> temporary LocalMachine TrustedPeople certificate
=> temporary Developer Mode change
=> PackageManager registration
=> exact readback
=> finally restore Developer Mode / remove temporary certificate
~~~

Decision:

**KEEP the worker process. Remove only its feature-local privilege escalation.**

The process boundary still protects a real cleanup contract:

- Developer Mode is temporarily changed and restored in \`finally\`;
- a certificate may be temporarily inserted into LocalMachine TrustedPeople and removed in \`finally\`;
- the caller intentionally does not terminate the worker after it starts;
- on parent timeout the worker is deliberately left running so its cleanup can complete.

Inlining this into the persistent Runtime would weaken that failure boundary for no product benefit.

Target launch:

~~~text
Runtime [High]
=> same EXE child, inherited High token
=> --register-fse-home
~~~

Use ordinary child creation; no \`Verb="runas"\`.

Keep the current 60-second parent observation timeout and the policy that a timed-out worker is left alive for its own cleanup.

Preferred naming cleanup:

~~~text
SteamFseElevatedRegistration
=> SteamFseRegistrationWorker
~~~

The argument \`--register-fse-home\` may remain unchanged.

---

## 2.5 HidHide / usbip first-time prerequisite setup worker — KEEP PROCESS, REMOVE RUNAS

Current:

~~~text
Runtime
=> FrontendPrerequisiteSetupExecutor
=> ElevatedProcessRunner
=> runas same main EXE --elevated-prerequisite-setup
=> setup mutex
=> hardware/safety preflight
=> trusted staging
=> installer acquisition/hash verification
=> HidHide installer child
=> post-install verification / receipt transition
=> usbip installer child
=> post-install verification / receipt transition
=> worker exit-code protocol
~~~

Decision:

**KEEP the setup worker process. Remove the extra UAC/elevation layer.**

This process is not merely an elevation shim. It owns a substantial bounded installation transaction with:

- one setup mutex;
- provisioning receipts;
- trusted staging;
- two external installers;
- post-install polling;
- reboot-required classification;
- safety re-checks between stages.

Do not move that entire flow into the persistent Runtime just because the Runtime is now High.

Target:

~~~text
Runtime [High]
=> child main EXE inherits High
=> prerequisite setup worker
=> external installers inherit High from worker
~~~

Preferred naming cleanup:

~~~text
ElevatedPrerequisiteSetup
=> PrerequisiteSetupWorker
--elevated-prerequisite-setup
=> --prerequisite-setup-worker
~~~

Because the product is pre-release, no command-line compatibility shim is required.

Preserve the existing setup mutex, receipt states, exact ownership policy, safety gates, installer hashes, 0/3010/2/3 worker exit contract, and post-install verification.

---

## 2.6 Windows App Runtime setup worker — KEEP PROCESS, REMOVE RUNAS

Current:

~~~text
Runtime
=> WindowsAppRuntimePrerequisite.EnsureAvailableAsync()
=> ElevatedProcessRunner
=> runas same main EXE --ensure-windows-app-runtime
=> acquire verified installer
=> run installer
=> package readback
~~~

Decision:

**KEEP the worker process; remove feature-local elevation.**

Reason:

- it runs a real external runtime installer;
- it performs acquisition/hash/readback as one dedicated setup operation;
- there is no benefit in moving installer execution into the persistent Runtime.

Target:

~~~text
Runtime [High]
=> inherited-High worker child
=> Windows App Runtime installer
=> readback
~~~

Preferred naming:

~~~text
ElevatedWindowsAppRuntimeSetup
=> WindowsAppRuntimeSetupWorker
~~~

The existing \`--ensure-windows-app-runtime\` argument may remain because it does not encode the old privilege model.

---

## 2.7 Shared ElevatedProcessRunner — REPLACE WITH INHERITED-PRIVILEGE CHILD RUNNER

Current shared primitive:

~~~text
IElevatedProcessRunner
ElevatedProcessRunner
ProcessStartInfo
  UseShellExecute = true
  Verb = "runas"
ElevatedProcessResultKind.CancelledBeforeStart
~~~

Current production consumers include:

- prerequisite setup worker launch;
- Windows App Runtime setup worker launch;
- Xbox360 USB trace \`logman.exe\`;
- legacy HidHideProvisioner code.

Decision:

**Replace the elevation-specific primitive with a normal child-process runner that inherits the already-High Runtime token.**

Suggested names:

~~~text
IChildProcessRunner
ChildProcessRunner
ChildProcessResult
ChildProcessResultKind
~~~

Core launch:

~~~csharp
new ProcessStartInfo(fileName, arguments)
{
    UseShellExecute = false,
    CreateNoWindow = true
}
~~~

Do not use \`Verb="runas"\`.

Preserve:

- optional execution timeout;
- process-tree termination on the existing timed-out/caller-cancelled paths where currently supported;
- exit-code return;
- FailedToStart;
- TimedOut;
- cancellation-token behavior already relied upon by callers.

Remove the child-specific UAC result:

~~~text
CancelledBeforeStart / UacCancelled
~~~

A user can cancel the **application's one normal Runtime elevation prompt** before Runtime startup. Once the Runtime exists, internal worker launches must not create additional UAC prompts.

Do not add a new "privilege broker" interface.

---

## 2.8 Xbox360 USB trace / logman diagnostic — KEEP EXTERNAL PROCESS, REMOVE RUNAS SEMANTICS

Current diagnostic uses \`IElevatedProcessRunner\` to invoke \`logman.exe\`.

Decision:

**Keep the external command and timeout; use the inherited-High child runner.**

The external process is the diagnostic operation itself, not an elevation helper.

Remove mapping for:

~~~text
CancelledBeforeStart
=> UacCancelled
~~~

Preserve command timeout, termination, output parsing, and diagnostic failure reporting.

---

## 2.9 Legacy HidHideProvisioner graph — DELETE IF REFERENCE CLOSURE REMAINS TEST-ONLY

Fresh main search shows these production types have no production constructor/consumer outside their own file:

~~~text
IHidHideProvisioner
HidHideProvisioner
HidHideProvisioningResultKind
HidHideProvisioningContext
IHidHideProvisioningSafetyStateProvider
SystemStatusHidHideProvisioningSafetyStateProvider
~~~

Their direct external references are test-only.

Current first-time setup is owned by:

~~~text
FrontendPrerequisiteSetupExecutor
=> prerequisite setup worker
~~~

Decision:

**Delete this legacy provisioning graph in PR A if a final reference search still shows only tests.**

Do not delete shared HidHide package metadata, receipt storage, package probes, shortcut cleanup, or current Full1902 HidHide baseline code from the same file.

Delete the matching obsolete \`HidHideProvisionerTests\` coverage.

This deletion is useful here because the dead graph otherwise keeps obsolete child-UAC semantics alive after \`IElevatedProcessRunner\` is removed.

Do not invent a replacement HidHide provisioner.

---

## 2.10 Center M startup helper — KEEP PROCESS, REMOVE RUNAS / requireAdministrator

Current:

~~~text
Runtime
=> CenterMStartupHelperClient
=> runas SteamInputAddonforClaw.CenterMStartupHelper.exe
=> Task Scheduler COM writes
=> MSI Foundation Service ChangeServiceConfig
=> exact helper readback
=> parent independent readback
~~~

The helper is short-lived and the client has:

~~~text
30s connect timeout
30s response timeout
process termination after failure/timeout
~~~

Decision:

**Keep the process boundary, but stop treating it as an independent elevation authority.**

Reasons to keep it:

- it contains Task Scheduler COM + SCM mutation away from the persistent Runtime;
- the existing response timeout lets the parent abandon/terminate a stuck helper;
- the exact helper readback + parent readback policy is already a useful bounded operation boundary;
- deleting the process would remove practical failure containment for little simplification benefit.

Change:

~~~text
CenterMStartupHelper app.manifest:
requireAdministrator
=> asInvoker

CenterMStartupHelperClient:
UseShellExecute = true + Verb=runas
=> ordinary direct child launch inheriting Runtime High
~~~

Prefer:

~~~csharp
new ProcessStartInfo(_helperPath, pipeName)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    WorkingDirectory = AppContext.BaseDirectory
}
~~~

Preserve:

- one request / one response;
- CurrentUserOnly parent pipe;
- fixed SetEnabled operation;
- exact task/service mutation scope;
- helper-side readback;
- parent-side fresh readback;
- connect/response timeout;
- kill-on-stuck cleanup.

### Remove helper-local UAC cancellation state

\`CenterMStartupHelperOutcome.Cancelled\` currently exists only for the helper's own UAC prompt.

After this cleanup there is no helper UAC prompt.

Remove that helper outcome and its \`CenterMStartupControl\` branch.

Do **not** delete the overall frontend:

~~~text
FrontendCenterMStartupMutationOutcome.Cancelled
~~~

because \`CenterMRebootAuthorityTransition.RequestAsync(...)\` still legitimately returns Cancelled when the caller cancels during the read-only pre-mutation phase.

Delete/rewrite only stale text that says a Center M mutation was cancelled "at the Windows elevation prompt."

The ordered mutation phase remains Runtime-owned and non-cancellable after mutation begins.

---

## 2.11 TDP / Fan / Battery WMI helper — KEEP PROCESS, REMOVE RUNAS / requireAdministrator

Current:

~~~text
Runtime
=> one owned TdpHelperClient transport
=> runas SteamInputAddonforClaw.TdpHelper.exe
=> MSI_ACPI WMI calls
~~~

The helper has real fault containment:

~~~text
helper-side WMI operation WaitAsync(10s)
=> timeout exits helper

Runtime-side response timeout 15s
=> transport failure
=> CloseUnderLock()
=> kill helper if still alive
=> later request reconnects with a new helper
~~~

Decision:

**Keep the helper process. Do not inline MSI_ACPI WMI into Runtime in this cleanup.**

This is a real operation-failure boundary, not a theoretical race defense.

Change only the privilege model:

~~~text
TdpHelper app.manifest:
requireAdministrator
=> asInvoker

TdpHelperClient launch:
UseShellExecute=true + Verb=runas
=> UseShellExecute=false, inherited High token
~~~

Preserve:

- one Runtime-owned helper transport;
- CurrentUserOnly pipe;
- helper protocol allow-list;
- WMI fallback behavior;
- 10-second helper WMI timeout;
- 15-second client response timeout;
- kill/reconnect behavior;
- helper process identity/elevation diagnostic.

\`GetHelperInfo().Elevated\` should still report true in the supported product path because the helper inherits the High Runtime token.

Do not delete the TDP helper in this work order.

---

# 3. Resulting privilege architecture

After both PRs:

~~~text
Velopack bootstrap [asInvoker-compatible]
        |
        +-- normal app entry
                |
                +-- ONE same-user runas elevation gate
                        |
                        v
                Runtime [High]
                    |
                    +-- Main UI [High]
                    +-- Overlay [High]
                    |
                    +-- direct privileged Runtime operations
                    |     +-- owned startup task
                    |     +-- SafeUninstall coordinator
                    |
                    +-- inherited-High worker children
                    |     +-- FSE registration worker
                    |     +-- prerequisite setup worker
                    |     +-- Windows App Runtime setup worker
                    |     +-- logman diagnostic
                    |
                    +-- inherited-High fault-containment helpers
                          +-- CenterMStartupHelper
                          +-- TdpHelper
~~~

There is one privilege authority:

> **Program's normal Runtime elevation gate.**

Helpers may still exist, but they are execution/failure boundaries, not elevation authorities.

---

# 4. Program.cs ordering

Current retained special worker commands run before the normal Runtime elevation gate because they historically self-elevated.

After PR A, move retained privileged worker dispatch behind the one central elevation/SID gate.

Target ordering:

~~~text
VelopackApp.Build().Run()
=> approved Velopack uninstall fast-path
=> logging / safe uninstall registration bootstrap
=> parse originating SID handoff
=> verify same user
=> if Medium: central same-user runas and exit
=> High from here
=> SafeUninstall special mode
=> FSE registration worker special mode
=> prerequisite setup worker special mode
=> Windows App Runtime setup worker special mode
=> SingleInstanceGate
=> pending update apply
=> RuntimeProcessApplication
~~~

Removed special modes must no longer appear:

~~~text
--ensure-startup-task
--remove-startup-task
--uninstall-owned-prerequisites
~~~

Do not put Velopack fast hooks behind the elevation gate.

Do not add another special-mode privilege check once the central gate has passed.

---

# 5. Recommended implementation split

## PR A — Remove redundant same-EXE elevation hops

Scope:

1. direct High Runtime startup-task create/repair/delete;
2. delete \`ElevatedStartupTaskSetup\` / elevated startup invoker graph;
3. direct owned-prerequisite cleanup from High SafeUninstall;
4. delete owned-prerequisite self-child entrypoint;
5. move retained same-EXE worker dispatch after the central elevation gate;
6. replace \`IElevatedProcessRunner\` with inherited-High child runner;
7. update prerequisite setup worker, Windows App Runtime worker, FSE worker, and logman launches to inherit High;
8. remove per-feature UAC-cancel result semantics;
9. delete legacy \`HidHideProvisioner\` graph if final reference closure is still tests-only;
10. preserve all existing worker-process boundaries.

Expected primary files:

~~~text
src/SteamInputAddonforClaw/Program.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
src/SteamInputAddonforClaw/Install/StartupRegistration.cs
src/SteamInputAddonforClaw/Install/ElevatedStartupTaskSetup.cs              DELETE
src/SteamInputAddonforClaw/Install/SafeUninstall.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedOwnedPrerequisiteUninstall.cs
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs
src/SteamInputAddonforClaw/Frontend/FrontendPrerequisiteSetupExecutor.cs
src/SteamInputAddonforClaw/Prerequisites/PrerequisiteSetupPromptPolicy.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
src/SteamInputAddonforClaw/Prerequisites/WindowsAppRuntimePrerequisite.cs
src/SteamInputAddonforClaw/WindowsGaming/SteamFseRegistration.cs
src/SteamInputAddonforClaw/Diagnostics/Xbox360UsbTraceCapture.cs
tests/...
~~~

A small generic child-runner file is acceptable if it replaces the existing shared elevation runner rather than adding another abstraction.

### PR A must NOT

- change TDP helper process/manifest;
- change Center M helper process/manifest;
- inline prerequisite installers into Runtime;
- inline FSE registration into Runtime;
- redesign Velopack;
- touch external user EXE/PowerShell privilege policy.

---

## PR B — Retained helper processes inherit Runtime High

Scope:

1. TDP helper manifest \`requireAdministrator -> asInvoker\`;
2. TDP helper client removes \`runas\` and directly launches the child;
3. preserve WMI timeout/kill/reconnect contract;
4. Center M helper manifest \`requireAdministrator -> asInvoker\`;
5. Center M helper client removes \`runas\` and directly launches the child;
6. preserve connect/response timeout + kill + exact readback;
7. remove CenterM helper-specific UAC-cancel outcome/text;
8. update active architecture documentation to describe inherited-High helpers.

Expected files:

~~~text
src/SteamInputAddonforClaw.TdpHelper/app.manifest
src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs
src/SteamInputAddonforClaw.CenterMStartupHelper/app.manifest
src/SteamInputAddonforClaw.CenterMStartupHelper/Program.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMStartupHelperClient.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMStartupControl.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
tests/...
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
~~~

Do not delete either helper project.

---

# 6. Tests — PR A

## 6.1 Startup task

Replace elevated-invoker tests with direct High writer tests.

Required cases:

~~~text
already compliant
=> no write

missing
=> one Register
=> bounded exact readback
=> success

drifted
=> one Register
=> bounded exact readback
=> success

Register AccessDenied/Failed
=> fail closed
=> no secondary elevation fallback

delete existing
=> one Delete
=> bounded proven absence
=> success

delete/read failure
=> fail closed
~~~

No test should expect \`SelfElevatedStartupTaskInvoker\`.

## 6.2 Program ordering

Assert:

~~~text
Velopack first
central SID/elevation gate
SafeUninstall + retained setup-worker commands after elevation
SingleInstance after those worker dispatches
Runtime last
~~~

Assert removed task/uninstall helper arguments are absent from production Program dispatch.

## 6.3 Child process runner

Assert:

~~~text
UseShellExecute = false
no Verb = runas
original filename/arguments preserved
exit code returned
FailedToStart preserved
timeout still terminates existing process tree when configured
~~~

Do not create tests for a helper-local UAC prompt because there is no helper-local UAC prompt.

## 6.4 Prerequisite / Windows App Runtime / FSE

Preserve functional policy tests.

Update only privilege-launch assumptions.

The prerequisite worker exit-code mapping must continue to produce:

~~~text
0    => Installed
3010 => RebootRequired
2    => AlreadyInProgress
3    => Blocked
other/start failure => Failed
~~~

There is no internal \`UacCancelled\` result after Runtime startup.

## 6.5 Safe uninstall

Add direct-cleanup coverage:

~~~text
stock-safe preparation succeeds
=> OwnedPrerequisiteUninstall.Execute called directly
=> no self-child Process.Start/runas
=> bounded local cleanup
=> Velopack uninstall handoff
~~~

Preserve fail-close behavior on prerequisite cleanup failure.

---

# 7. Tests — PR B

## 7.1 Manifest contract

Update active manifest assertions:

~~~text
SteamInputAddonforClaw              asInvoker
SteamInputAddonforClaw.UI           asInvoker
SteamInputAddonforClaw.Overlay      asInvoker
SteamInputAddonforClaw.TdpHelper    asInvoker
SteamInputAddonforClaw.CenterMStartupHelper asInvoker
~~~

The normal Runtime is High because of the central Runtime elevation gate, not because any helper has a \`requireAdministrator\` manifest.

## 7.2 TDP helper

Preserve tests proving:

~~~text
Runtime owns pipe/server
helper only connects
protocol allow-list remains narrow
10s WMI worker timeout remains
15s Runtime response timeout remains
transport closes/kills helper on failure
next request can reconnect
GetHelperInfo still reports Elevated=true in supported real launch
~~~

Add a source/constructor test that the production helper launch contains no \`runas\`.

## 7.3 Center M helper

Preserve tests proving:

~~~text
fixed three-root scope only
helper exact readback
parent fresh readback
30s connect timeout
30s response timeout
stuck helper termination
Partial/Unavailable classification remains fail-closed
~~~

Remove tests that simulate cancellation of a helper UAC prompt.

Keep tests for frontend cancellation **before mutation** in \`CenterMRebootAuthorityTransition\`.

Update stale failure text that says the helper elevation prompt was cancelled.

---

# 8. Source-level cleanup invariant

After both PRs, a fresh source search under shipped production source should show:

~~~text
Verb = "runas"
~~~

only for the **one normal Runtime self-elevation gate** in \`Program.cs\`.

There should be no feature-local \`runas\` in:

~~~text
StartupRegistration
SafeUninstall
SteamFSE
prerequisite setup
Windows App Runtime setup
CenterMStartupHelperClient
TdpHelperClient
Xbox360UsbTraceCapture
~~~

Historical work orders may still contain old \`runas\` text. Do not rewrite historical documents merely to make grep output empty.

---

# 9. Hardware / lifecycle validation

## PR A

Validate:

### Startup task

~~~text
fresh install / first High Runtime
=> mandatory task created directly
=> exact Highest contract verified

delete/recreate task manually
=> Runtime repair succeeds without a second UAC prompt
~~~

### Prerequisite setup

On a test machine/state where setup is required:

~~~text
Runtime already High
=> Start setup
=> no second UAC prompt
=> worker process launches
=> installer flow completes
=> receipt/readback behavior unchanged
~~~

### Windows App Runtime

If setup path can be exercised:

~~~text
Runtime High
=> setup worker
=> no second UAC
=> package becomes Ready
~~~

### Steam FSE

~~~text
registration requested
=> no second UAC
=> worker performs package registration
=> Developer Mode restored
=> temporary certificate removed
=> package readback succeeds
~~~

### Safe uninstall

~~~text
SafeUninstall High
=> stock authority restored
=> startup task removed directly
=> owned dependencies cleaned directly by SafeUninstall coordinator
=> final Velopack uninstall handoff
~~~

## PR B

### Center M

Exercise both:

~~~text
Disable Center M and Restart
Enable Center M and Restart
~~~

Verify:

- no helper-local UAC prompt;
- exact task/task/service state;
- readback success/failure remains correct;
- timeout/failure remains fail-closed;
- restart path unchanged.

### TDP / Fan / Battery

Verify:

- TDP reads/writes;
- fan operations currently exposed;
- battery charge-limit operations;
- helper diagnostic reports Elevated=YES;
- no UAC prompt when helper first starts;
- helper recovery after forced/real helper loss still works.

### Power lifecycle

After both PRs:

~~~text
Sleep -> Resume
Hibernate -> Resume
~~~

Verify no regression in controller recovery, TDP/fan/battery helper reconnection, Center M authority, or WING suppression.

---

# 10. Explicit non-goals

Do not include:

- removal/inlining of TDP helper WMI implementation;
- removal/inlining of Center M helper process;
- a new service or broker;
- a new process manager;
- token duplication;
- Explorer-based de-elevation/elevation tricks;
- Fast User Switching / RDP / multi-session support;
- standard-user + alternate-admin credential support;
- UI/Overlay de-elevation;
- user-selected EXE/PowerShell de-elevation;
- controller-authority redesign;
- HidHide baseline redesign;
- VIIPER ownership redesign;
- PID1901/PID1902 lifecycle redesign;
- new retry/state-machine infrastructure.

External user application / PowerShell privilege policy remains the separate later cleanup already recorded by the elevated-Runtime architecture.

---

# 11. Acceptance criteria

The cleanup is complete when:

1. normal Runtime self-elevation remains the only application privilege-escalation authority;
2. startup-task repair/removal executes directly from High Runtime with exact bounded readback;
3. the startup-task self-elevation helper graph is deleted;
4. SafeUninstall directly owns prerequisite cleanup with no same-EXE elevation child;
5. retained same-EXE setup workers inherit High and do not show another UAC prompt;
6. FSE worker process remains separate and preserves temporary-system-state cleanup;
7. prerequisite setup worker remains separate and preserves receipts/safety/install verification;
8. Windows App Runtime setup worker remains separate;
9. shared child-process execution no longer contains \`Verb="runas"\`;
10. dead legacy HidHideProvisioner is removed if final reference closure confirms it remains test-only;
11. Center M helper process remains but is \`asInvoker\` and inherits High;
12. TDP helper process remains but is \`asInvoker\` and inherits High;
13. TDP WMI timeout/kill/reconnect containment is unchanged;
14. Center M helper timeout/readback containment is unchanged;
15. helper-local UAC cancellation states/messages are removed where no longer reachable;
16. no new privilege manager/broker/service is introduced;
17. all existing real Full1902 fail-close and lifecycle contracts continue to pass.

---

# 12. Final target

~~~text
ONE privilege elevation:
    Program normal Runtime gate

DIRECT High Runtime operations:
    startup task
    SafeUninstall orchestration

RETAINED inherited-High workers:
    FSE setup
    prerequisite setup
    Windows App Runtime setup
    diagnostic commands

RETAINED inherited-High fault-containment helpers:
    Center M startup mutation
    MSI_ACPI TDP/Fan/Battery WMI
~~~

The objective is not "remove every helper."

The objective is:

> **one privilege authority, while retaining only process boundaries that still protect real supported lifecycle or operation-failure behavior.**
