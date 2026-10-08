# Full1902 Elevated Runtime Architecture

> **Status:** Implemented process-privilege architecture / hardware validation partially pending  
> **Date:** 2026-10-05  
> **Scope:** Process privilege model for the standalone Full1902 application, including WING / Xbox Game Bar suppression  
> **Product scope:** one interactive Windows user who is a member of Administrators, one interactive session; Fast User Switching, RDP, and multi-session are not supported

---

## 1. Decision summary

The Full1902 application will move to an **elevated Runtime process model**.

The intended process model is:

~~~text
SteamInputAddonforClaw.exe Runtime      High integrity / Administrator
    |
    +-- Main UI                        inherits Runtime token
    +-- Overlay                        inherits Runtime token
    +-- WinGSuppressionGuard           remains Runtime-owned
    +-- PID1902 / DirectInput ownership
    +-- HidHide / VIIPER ownership
    +-- MSI device-control coordination
    |
    +-- retained helper processes       inherit the Runtime's High token
~~~

The Main UI and Overlay are **not intentionally de-elevated** in this architecture.

This decision replaces the previously considered alternatives of:

- moving WING suppression into the TDP helper;
- adding another elevated WING-specific helper;
- keeping the Runtime medium-integrity and introducing cross-integrity UI/Overlay IPC;
- using uiAccess=true as the primary solution.

The immediate objective is to make the existing Runtime-owned Win+G suppression work for both ordinary and administrator-elevated games without adding another authority owner or another long-lived helper.

---

## 2. Why this decision was made

### 2.1 Administrator-elevated games are a supported product case

The product cannot assume that games always run at medium integrity.

Users may:

- enable "Run this program as an administrator";
- use launchers that elevate a game;
- inherit elevation through another launch path;
- change compatibility/elevation settings without the Addon knowing.

Therefore the product requirement is:

~~~text
physical WING press
+ Addon controller authority
+ game foreground at either normal or elevated integrity
=> native Xbox Game Bar must not surface
~~~

Administrator games are not an unsupported edge case.

### 2.2 2026-10-05 hardware/log evidence isolated the current failure

The relevant comparison is preserved in:

~~~text
Google Drive / Addon / Log / 1005 / 04
~~~

On the same Runtime lifetime and same game:

~~~text
007FirstLight.exe elevated
=> WING keyboard side effect was not observed by the current Runtime WH_KEYBOARD_LL path
=> Win+G suppression did not fire
=> Game Bar input-redirection activity occurred
~~~

After removing game elevation:

~~~text
007FirstLight.exe non-elevated
=> LWIN/G reached WinGSuppressionGuard
=> Win+G was suppressed
=> Event88 still arrived normally
=> Game Bar activation did not occur
~~~

This is strong practical evidence that the current production failure is an integrity-boundary problem, not a need for another controller authority or speculative GameInput routing layer.

The PR #679 GameInput System Button Probe remains useful as a diagnostic tool, but a GameInput Guide path is not required to explain this observed failure.

---

## 3. Runtime privilege model

### 3.1 Runtime is the privileged platform process

Full1902 already treats the Runtime as the persistent platform/controller owner while the UI is disposable.

The elevated model aligns privilege with that existing ownership:

~~~text
Runtime
  owns controller authority
  owns physical input
  owns HidHide/VIIPER lifecycle
  owns Win+G suppression
  coordinates privileged MSI/device operations
~~~

Elevation therefore belongs at the Runtime boundary rather than at a new WING-specific process boundary.

### 3.2 Main UI and Overlay inherit elevation

Current production launch paths create the Main UI and Overlay directly from the Runtime.

The accepted model is:

~~~text
Elevated Runtime
=> Main UI runs elevated through ordinary child-process inheritance
=> Overlay runs elevated through ordinary child-process inheritance
~~~

Do not add an unelevated-launch broker, token-copy helper, shell explorer trick, or new privilege-boundary abstraction for these two Addon-owned processes.

The Main UI and Overlay remain frontend surfaces only. Elevation does not grant them controller authority.

Authority remains in the Runtime.

### 3.3 Existing same-user IPC stays unchanged

Because Runtime, Main UI, and Overlay remain on the same integrity side of the process boundary, the current frontend/overlay named-pipe model should remain unchanged.

In particular, this architecture does **not** require a High-Runtime / Medium-frontend cross-integrity pipe design.

Do not replace PipeOptions.CurrentUserOnly or add custom pipe ACL machinery solely for this elevation transition.

---

## 4. WING / Xbox Game Bar suppression

### 4.1 Keep the existing Runtime owner

WinGSuppressionGuard remains the one low-level Win+G suppression primitive.

Do not move it into:

- SteamInputAddonforClaw.TdpHelper;
- Main UI;
- Overlay;
- a new helper/service;
- GameInput diagnostic code.

The desired architecture is simply:

~~~text
Elevated Runtime
    |
    +-- existing WinGSuppressionGuard
            |
            +-- WH_KEYBOARD_LL
            +-- LWIN/RWIN + G tracking
            +-- existing modifier cleanup
~~~

The existing Full1902 Policy-B authority binding remains valid:

~~~text
Center M Disabled / Addon authority
+ WinGSuppressionGuard armed
=> WING custom delivery may be active

Center M Enabled / stock authority
=> WinG suppression is not armed by Full1902
~~~

No second WING readiness state or suppression authority should be introduced.

### 4.2 Fail-close behavior remains unchanged

The existing Policy-B rule remains:

~~~text
Disabled-mode Addon authority
+ suppression cannot be installed/armed/proven
=> do not attach/resume live virtual presentation
~~~

Runtime elevation is intended to make the existing guard valid across normal and administrator-elevated foreground games.

It does not weaken the existing fail-close requirement.

---

## 5. Sleep / hibernate / resume

The privilege change must not redesign the established Full1902 power lifecycle.

Current authority remains:

~~~text
Center M Disabled
=> Addon controller authority survives Sleep/Hibernate
=> PID1902 remains desired
=> HidHide authority remains
=> Win+G suppression remains part of the same Runtime authority lifetime
~~~

The expected path remains:

~~~text
Suspend
=> neutral/pause presentation through the existing Full1902 suspend path
=> do not intentionally release controller authority
=> do not intentionally disarm/recreate Win+G suppression

Resume
=> existing PowerResumeObserved path
=> existing physical/presentation recovery
=> existing WinG suppression readiness gate before live presentation is resumed/re-attached
~~~

Do not add a new resume epoch, hook watchdog, periodic re-install loop, or helper handshake solely for theoretical timing interleavings.

If real hardware testing later proves that the Windows low-level hook is lost across a supported Sleep/Hibernate/Resume path, fix that concrete failure in the smallest appropriate follow-up.

Required validation for the elevation implementation includes actual Sleep and Hibernate cycles.

---

## 6. Startup and restart

### 6.1 Mandatory startup task must launch the elevated Runtime

The existing mandatory Full1902 startup-task architecture remains the launch authority while Center M is Disabled.

The task currently records/verifies least-privilege RunLevel. The elevation implementation must change the owned startup-task contract so the Runtime starts at the required elevated run level.

Conceptually:

~~~text
Windows logon
=> existing Addon-owned scheduled task
=> interactive user
=> highest run level
=> SteamInputAddonforClaw.exe --background
=> elevated persistent Runtime
~~~

The owned-task compliance check must require the High run level. Any incorrect run level is ordinary configuration drift and may be reconciled by the existing owned-task synchronization path.

Do not add a second startup task or startup service.

### 6.2 Controlled Runtime restart remains an authority-preserving restart

While Center M is Disabled:

~~~text
old elevated Runtime exits safely
=> no intentional PID1902 -> PID1901 release
=> persistent HidHide baseline remains
=> replacement elevated Runtime starts
=> ordinary Full1902 reconciliation rebuilds process-owned resources
~~~

Do not create a second elevation-specific controller lifecycle.

### 6.3 Manual launch behavior

The product is pre-release. There is no requirement to preserve a historical medium-integrity production Runtime.

The supported normal application lifecycle is:

~~~text
VelopackApp.Build().Run()
=> existing special command handling
=> normal application elevation gate
=> if already High: continue
=> if Medium: relaunch the same executable with ShellExecute "runas" and exit
=> elevated replacement starts again
=> elevation gate passes
=> only then SingleInstanceGate / pending-update / Runtime startup
~~~

A cancelled or failed elevation request must leave the medium bootstrap unable to enter controller ownership.

The normal application's single-instance lifecycle is intentionally High-only. Do not add cross-integrity activation machinery solely to avoid a manual UAC prompt.

### 6.4 Velopack compatibility boundary

The packaged main executable remains:

~~~text
requestedExecutionLevel = asInvoker
uiAccess = false
~~~

This is a packaging/bootstrap requirement, not the Runtime privilege model.

Velopack owns the earliest process lifecycle and invokes the configured main executable for its own hooks. Therefore:

~~~text
packaged EXE / Velopack bootstrap
= asInvoker-compatible

normal Full1902 Runtime
= High integrity before Runtime ownership begins
~~~

Do not change the packaged main EXE to `requireAdministrator`.

Do not move the elevation gate ahead of `VelopackApp.Build().Run()`.

Fresh install:

~~~text
Velopack install
=> first normal app launch begins asInvoker
=> normal-app elevation gate requests UAC
=> elevated Full1902 Runtime starts
~~~

Steady-state update:

~~~text
elevated Runtime
=> launches Update.exe
=> updater inherits High
=> package apply
=> updater starts asInvoker main EXE
=> child inherits High
=> Runtime continues without another UAC
~~~

No historical Medium-to-High release migration or bridge release is required.

Do not change the Velopack package/NuGet version as part of the elevation implementation.

---

## 7. Retained helper processes inherit Runtime High

The initial Runtime elevation implementation kept existing helper processes and their protocols
unchanged. The subsequent privilege-helper cleanup removes their feature-local elevation while
preserving process boundaries that provide real operation-failure containment.

The TDP helper remains a separate child process:

~~~text
SteamInputAddonforClaw.TdpHelper.exe
=> remains present with an asInvoker manifest
=> launched as an ordinary child of the High Runtime
=> inherits the Runtime's High token
=> existing protocol and WMI request/timeout behavior remain present
=> existing TDP/Fan/Battery behavior is not refactored
~~~

The helper continues to provide process isolation around MSI WMI operations. Its diagnostic still
reports the inherited High token in the supported Runtime launch path.

The Center M startup helper is likewise retained as an asInvoker child inheriting High. Its fixed
three-root mutation scope, helper and parent readback, 30-second connect/response bounds, and
stuck-process termination remain unchanged.

FSE registration, prerequisite setup, and Windows App Runtime setup also retain their bounded worker
processes where those processes own installer or temporary-system-state cleanup contracts; they
inherit the Runtime token rather than requesting another UAC elevation.

Do not inline or remove a retained helper solely to reduce process count. In particular, do not:

- move TDP/Fan/Battery WMI implementation into the Runtime;
- change TDP helper timeout/failure behavior;
- change Center M helper timeout/readback/termination behavior;
- convert either helper into a WING owner;
- perform broad helper-process consolidation.

---

## 8. User-action process privilege boundary

The Runtime remains High because the existing Full1902/WING suppression owner needs that token. User-launched actions use a separate, bounded policy implemented by one `UserProcessLauncher` instance shared through the existing Runtime host by Shortcut and front-button execution.

~~~text
User EXE / Shortcut PowerShell, administrator option OFF
=> validated same-user, same-session Medium token

User EXE / Shortcut PowerShell, administrator option ON
=> existing High Runtime token

Steam URI / Xbox package
=> Explorer launched with the validated Medium token

HTTP(S) URL
=> `rundll32.exe url.dll,FileProtocolHandler` invokes the registered browser with the validated Medium token

Screenshot
=> existing NirCmdScreenshotCapture path; unchanged
~~~

The Medium path validates the UAC-linked limited token's user SID, session, elevation type, and exact Medium integrity before process creation. Literal user EXE / encoded PowerShell actions and HTTP(S) browser dispatch continue to use `CreateProcessAsUserW`, preserving the existing long command-line support. The fixed Explorer shell activations for Steam URI and Xbox package requests use `CreateProcessWithTokenW`; their command lines are bounded below that API's documented 1,024-character limit, and the validated token is required to belong to the caller's interactive session. The API choice is limited to these short shell targets; it is not a general fallback. The 0.1.342 logs recorded `Win32Exception` without the failing native stage or error code, so they do not establish `ERROR_PRIVILEGE_NOT_HELD` (1314) or any other specific cause. The implementation records the failing API stage and numeric Win32 code on failure. It does not use `UseShellExecute` from the High Runtime for user actions. A missing/invalid Medium token or any launch failure remains a failure; there is no automatic retry using High. The explicit administrator option likewise uses the existing High token without another UAC prompt and does not retry at Medium.

Shortcut EXE and PowerShell actions persist an optional per-action `runAsAdministrator` boolean; an absent legacy value means `false`. The front-button `LaunchApplication` binding uses an optional final `RunAsAdministrator` field with the same default. Built-in Steam, Big Picture, Xbox, and browser URL actions expose no administrator option and use the Medium shell route. Only literal `.exe` actions can use the direct-process path; PowerShell is restricted to the existing Shortcut PowerShell action.

Steam URI and packaged-app requests are dispatched by starting Explorer as the interactive user's Medium process, preserving the existing Steam URI and Xbox AUMID. HTTP(S) URLs use the Windows `rundll32.exe url.dll,FileProtocolHandler` protocol-handler path with the complete escaped URI passed directly, avoiding both Explorer's second-layer parsing and command-interpreter expansion while retaining the validated Medium token. Neither path changes the integrity of an already-running target such as Steam. Cold-start Steam, Xbox activation, default-browser URL dispatch, and the no-normal-Explorer Windows/Steam FSE case still require physical validation before release; the implementation must not be described as proven for those cases until that matrix is completed.

This policy does not introduce another controller authority, resident broker, service, token cache, or child-process lifetime owner. Shortcut retains its TileId-based authority and failure/Overlay-retirement behavior; the front-button executor retains its current action validation and caller-specific failure handling.

---

## 9. Completed privilege-helper cleanup and remaining follow-up

The post-elevation privilege-helper review is complete.

Implemented decisions:

~~~text
Normal Runtime elevation
=> KEEP as the one application privilege-escalation authority

Startup task self-elevation
=> REMOVED
=> High Runtime writes/verifies the owned task directly

SafeUninstall prerequisite self-elevation
=> REMOVED
=> already-High SafeUninstall performs owned prerequisite cleanup directly

TDP helper
=> KEEP process boundary
=> asInvoker
=> inherits Runtime High
=> WMI timeout / kill / reconnect containment retained

Center M startup helper
=> KEEP process boundary
=> asInvoker
=> inherits Runtime High
=> connect/response timeout, termination, helper readback and parent readback retained

FSE / prerequisite / Windows App Runtime workers
=> KEEP bounded worker processes
=> inherit Runtime High
=> no feature-local UAC authority
~~~

TDP helper consolidation is **not** an open item. It is intentionally retained because its separate process contains real MSI WMI timeout/failure behavior. Center M helper is likewise intentionally retained for bounded Task Scheduler/SCM mutation and stuck-helper containment.

Remaining follow-up:

1. complete the physical user-action Medium/High token and Windows/Steam FSE activation matrix, plus the elevated-WING and power-lifecycle validation matrix;
2. do not revisit helper inlining without concrete operational evidence.

---

## 10. Explicit non-goals of the first elevation implementation

The following list records the scope of the original elevation implementation. Its former “external application de-elevation” item is superseded by the current user-action policy in sections 8 and 13; the historical scope decision itself is retained.

Do not include these in the first implementation PR unless a strictly required compile/test fix forces a tiny mechanical change:

- TDP helper removal;
- TDP/Fan/Battery WMI redesign;
- Center M helper redesign;
- external application de-elevation;
- PowerShell action de-elevation;
- Shortcut architecture redesign;
- UI/Overlay de-elevation;
- custom cross-integrity pipe ACL work;
- uiAccess=true;
- new Windows service;
- new WING helper;
- new suppression manager;
- GameInput production routing;
- PID1901/PID1902 policy changes;
- HidHide policy changes;
- VIIPER ownership changes;
- presentation switching changes;
- new suspend/resume state machine.

---

## 11. Implementation invariants

The first Runtime-elevation implementation must preserve these facts:

1. There is still one Full1902 controller authority: the Runtime.
2. Center M Enabled vs Disabled remains the sole controller-authority policy.
3. WinGSuppressionGuard remains Runtime-owned.
4. No additional WING hook or WING helper exists.
5. Admin-elevated games are supported for WING/Game Bar suppression.
6. Main UI and Overlay remain disposable frontends and gain no controller authority.
7. Main UI/Overlay IPC topology remains unchanged.
8. Existing TDP helper behavior remains unchanged.
9. Sleep/Hibernate/Resume continues through the existing Full1902 lifecycle.
10. PID1902/PID1901, HidHide, DirectInput, VIIPER, and presentation teardown ordering remain unchanged.
11. Startup task repair recognizes the new elevated Runtime contract.
12. A medium-integrity Runtime must never be allowed to become the active Full1902 controller owner after the migration.

---

## 12. Hardware validation status and remaining matrix

The process-privilege implementation and helper cleanup are merged, but final product validation still requires physical MSI Claw coverage.

### 12.1 Already evidenced on hardware/logs

The 2026-10-05 `Addon/Log/1005/06` validation established:

~~~text
manual asInvoker bootstrap
=> same-user elevation
=> High Runtime starts

reboot/logon
=> Highest startup task
=> High Runtime starts directly

controlled Runtime restart
=> replacement Runtime remains High

Disabled-mode startup
=> PID1901 -> PID1902 reconcile succeeds
=> DirectInput / HidHide / VIIPER establish normally

MSI Quick Settings process resurrection
=> exact package is identified
=> elevated Runtime can terminate the relevant processes
=> FailureCount=0
~~~

The same log set showed no material Runtime/UI/Overlay/VIIPER WARN/ERROR associated with the elevation change.

### 12.2 Elevated WING suppression matrix — still required

At minimum validate:

~~~text
Windows desktop
normal game
administrator-elevated game
Steam BPM
affected 007FirstLight.exe elevated
affected 007FirstLight.exe non-elevated
~~~

For every Addon-authority case:

~~~text
WING physical press
=> native Game Bar does not surface
=> WMI Event88 / configured WING action still works
=> no stuck Win modifier
~~~

The pre-elevation `1005/04` A/B logs established the integrity-boundary cause, but the post-elevation build still needs the physical elevated-game WING press proof.

### 12.3 Power lifecycle — still required

Validate:

~~~text
Sleep -> Resume
Hibernate -> Resume
~~~

while Center M is Disabled.

After each resume:

~~~text
physical controller ownership recovers normally
virtual presentation resumes/reconciles normally
Win+G suppression remains effective
WING Event88 action remains functional
~~~

Include at least one administrator-elevated foreground game after resume.

### 12.4 Retained helper smoke — required after helper cleanup

Validate the merged inherited-High helper model:

~~~text
TDP read/write
fan-related helper operations currently exposed
battery charge-limit path
TdpHelper diagnostic Elevated=YES
no helper-local UAC prompt

Disable Center M and Restart
Enable Center M and Restart
no CenterM helper-local UAC prompt
exact task/service readback remains correct
~~~

### 12.5 Update / uninstall

Before release, also exercise:

- one steady-state Velopack update between builds using the new High Runtime model;
- SafeUninstall from Addon authority through verified stock restoration and final uninstall handoff.

---

## 13. Implementation status and next work

### Completed

The elevated Runtime implementation and privilege-helper cleanup are complete:

~~~text
PR681
=> central High Runtime model
=> same-user elevation gate
=> Highest startup task contract
=> elevated Runtime owns existing WING suppression

PR683
=> redundant same-EXE / feature-local elevation cleanup
=> startup task and SafeUninstall direct High operations
=> retained setup/registration workers inherit High

PR685
=> TDP helper remains separate, asInvoker, inherits High
=> Center M helper remains separate, asInvoker, inherits High
=> helper-local UAC semantics removed

User-action process launch
=> one Host-shared UserProcessLauncher
=> default EXE / PowerShell / shell activation uses verified same-user Medium
=> explicit EXE / PowerShell administrator option uses existing High Runtime token
=> Shortcut and front-button preferences default to false and preserve existing data
=> Frontend protocol v69 carries the Shortcut editor preference
=> screenshot and controller authority remain unchanged
~~~

The process architecture is therefore no longer in a "migration pending" state. The user-action launch code is implemented, including the bounded `CreateProcessWithTokenW` route for fixed Steam/Xbox Explorer activations and stage/error diagnostics; the 0.1.342 failure's native cause remains unproven. Actual Medium/High token, cold-start Steam/Xbox, and Windows/Steam FSE verification remains a release-validation item.

### Remaining process-privilege validation

The user-launched external-action policy is implemented. Fixed Steam/Xbox Explorer shell dispatch uses the validated same-session Medium token through `CreateProcessWithTokenW`; long EXE/PowerShell and HTTP(S) commands retain `CreateProcessAsUserW`. Unit tests cover API-stage/error propagation and prohibit a High retry, but do not prove that an elevated Claw Runtime's token satisfies the selected API requirements. Before release, physical validation must demonstrate the actual child integrity and supported shell behavior for Shortcut EXE/PowerShell, front-button EXE, Steam cold start, Xbox package activation, HTTP(S) browser launch, Windows desktop, and Windows/Steam FSE. Keep this validation separate from controller-authority changes.

### Separate Full1902 reliability work

Unexpected Runtime death auto-restart / lightweight keepalive remains a separate Full1902 reliability requirement defined by the controller authority documents. Do not conflate it with privilege-helper cleanup.



## 14. Relationship to existing Full1902 authority documents

This document changes the **process privilege model**, not the controller authority model.

The following remain authoritative for controller authority and lifecycle rules within their existing scopes:

- HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md;
- REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md;
- FULL_1902_IMPLEMENTATION_ARCHITECTURE.md;
- the merged Full1902 WING / Game Bar Policy-B behavior represented by docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md.

For `HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`, its startup-task owner, identity, trigger, and repair/readback contract remain authoritative. This architecture supersedes only the conflicting primary-Runtime privilege and startup-task RunLevel assumptions.

The supported interactive Windows user must itself be an administrator. The elevation prompt is for consent under that same user identity; do not support over-the-shoulder elevation into a different administrator account. A replacement Runtime whose user SID differs from the originating interactive user must exit before `SingleInstanceGate` or controller ownership.

Where an older document assumes that SteamInputAddonforClaw.exe is medium-integrity/asInvoker, this document establishes the new target:

~~~text
Full1902 primary Runtime
=> privileged/elevated process
~~~

It does **not** change:

~~~text
Center M Disabled
=> Addon authority

Center M Enabled
=> MSI / stock authority
~~~

---

## 15. Final architecture snapshot

~~~text
Windows logon
    |
    +-- existing Addon startup task
            |
            +-- highest run level
                    |
                    v
        SteamInputAddonforClaw Runtime [High]
        (packaged EXE manifest remains asInvoker; normal app entry elevates before Runtime lifecycle)
                    |
        +-----------+-----------+-------------------+
        |                       |                   |
        v                       v                   v
 Main UI [High]          Overlay [High]      Controller platform
                                                |
                                                +-- PID1902 / DirectInput
                                                +-- HidHide
                                                +-- VIIPER
                                                +-- WinGSuppressionGuard
                                                +-- Full1902 recovery
                                                +-- Device/profile coordination

 Retained helper processes [High inherited from Runtime]
    |
    +-- TdpHelper [asInvoker; WMI fault containment retained]
    +-- CenterMStartupHelper [asInvoker; bounded mutation/readback retained]
    +-- setup/registration workers [existing transaction cleanup retained]

External user actions
    |
    +-- Shortcut / front-button EXE and PowerShell
    |       +-- default: validated same-user Medium token
    |       +-- explicit administrator option: existing Runtime High token
    +-- Steam URI / Xbox package
    |       +-- Explorer shell dispatch with validated Medium token
    +-- HTTP(S) URL
    |       +-- rundll32 URL protocol handler / registered browser with validated Medium token
    +-- Screenshot
            +-- existing NirCmdScreenshotCapture path
~~~

The key simplification is:

> **Elevate the existing owner instead of creating another owner.**

The elevation implementation should solve the administrator-game WING suppression failure by making the existing Full1902 Runtime authority valid at the required Windows integrity level, while deliberately postponing unrelated privileged-process cleanup until the new model is proven.
