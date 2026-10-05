# Full1902 Elevated Runtime Architecture

> **Status:** Accepted architecture decision / implementation pending  
> **Date:** 2026-10-05  
> **Scope:** Process privilege model for the standalone Full1902 application, including WING / Xbox Game Bar suppression  
> **Product scope:** one Windows user, one interactive session; Fast User Switching, RDP, and multi-session are not supported

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
    +-- existing privileged helpers    remain unchanged for now
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

Required validation for the elevation migration includes actual Sleep and Hibernate cycles.

---

## 6. Startup and restart

### 6.1 Mandatory startup task must launch the elevated Runtime

The existing mandatory Full1902 startup-task architecture remains the launch authority while Center M is Disabled.

The task currently records/verifies least-privilege RunLevel. The elevation migration must change the owned startup-task contract so the Runtime starts at the required elevated run level.

Conceptually:

~~~text
Windows logon
=> existing Addon-owned scheduled task
=> interactive user
=> highest run level
=> SteamInputAddonforClaw.exe --background
=> elevated persistent Runtime
~~~

A task still configured for the old least-privilege run level must be treated as drift and repaired through the existing owned-task synchronization path.

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

The final implementation work order must preserve the invariant that the primary Runtime reaches the required elevated state even when started outside the normal logon task path.

The exact bootstrap detail may be chosen during implementation, but the architecture must not permit a medium-integrity primary Runtime to continue into controller ownership.

A direct/manual launch may require UAC if no already-valid elevated Runtime/startup path can service the request.

Avoid adding a new permanent launcher executable solely to hide this behavior.

---

## 7. Existing privileged helpers remain unchanged in this migration

This architecture decision does **not** require immediate helper consolidation.

In particular, keep the existing TDP helper in the first elevation migration:

~~~text
SteamInputAddonforClaw.TdpHelper.exe
=> remains present
=> existing protocol remains present
=> existing WMI request/timeout behavior remains present
=> existing TDP/Fan/Battery behavior is not refactored
~~~

Although an elevated Runtime makes the helper's privilege boundary partially redundant, the helper also currently provides process isolation around MSI WMI operations.

That isolation and any future helper removal must be evaluated separately.

Do not combine the Runtime-elevation migration with:

- moving TDP/Fan/Battery WMI implementation into the Runtime;
- changing TDP helper timeout/failure behavior;
- renaming the helper;
- converting the helper into a WING owner;
- broad helper-process consolidation.

Other existing narrowly-scoped privileged setup helpers are likewise not required to be consolidated by this architecture migration.

---

## 8. Temporarily accepted external-process inheritance

An elevated Runtime changes the token inherited by processes it directly launches.

Current Runtime-owned user action paths include, among others:

- WING/OEM front-button LaunchApplication;
- Shortcut executable actions;
- Shortcut PowerShell actions;
- other Runtime-owned external action paths.

For the elevation migration, this consequence is **accepted temporarily**.

The first migration must not widen into a generic process-launch privilege refactor.

Therefore:

~~~text
Runtime elevation migration
=> external action execution semantics remain as they are today
=> if they inherit elevation from Runtime, that is a known temporary behavior
=> do not block the elevation migration on solving it
~~~

This is a deliberate scope decision, not an assertion that elevated external launches are the final product design.

---

## 9. Deferred privileged-process cleanup

After the elevated Runtime architecture is hardware-proven, perform a separate focused cleanup/design pass covering the privilege consequences that were intentionally deferred.

That later work should evaluate together:

### 9.1 User-launched application / PowerShell boundary

Decide the final policy for:

- front-button LaunchApplication;
- Shortcut executable actions;
- Shortcut PowerShell actions;
- URL/shell actions where relevant.

The likely product goal is to avoid silently elevating arbitrary user-selected programs merely because the Addon Runtime is privileged, but that mechanism is **not part of the first Runtime-elevation migration**.

Do not pre-build an unelevated launcher/broker before that follow-up has reviewed the actual supported action set.

### 9.2 TDP helper consolidation

Re-evaluate whether SteamInputAddonforClaw.TdpHelper should:

- remain as an MSI WMI fault-containment process; or
- be absorbed into the elevated Runtime if real operational evidence shows the process isolation is unnecessary.

This decision should consider real MSI WMI timeout/hang behavior, not code-size reduction alone.

### 9.3 Other helper cleanup

Only after the Runtime privilege model is stable should other existing privileged helpers be reviewed for redundancy.

Do not remove helpers merely because the Runtime is now elevated if the helper still protects a useful failure-isolation, packaging, uninstall, or bounded-operation boundary.

The goal is fewer redundant privilege boundaries without weakening lifecycle/failure safety.

---

## 10. Explicit non-goals of the first elevation migration

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

## 12. Required hardware validation

The elevation migration is not complete from unit tests alone.

Validate on a physical supported MSI Claw.

### 12.1 Privilege / startup

Verify:

~~~text
logon startup
=> Runtime elevated
=> Main UI launched by Runtime works
=> Overlay launched by Runtime works
=> no additional login-time UAC prompt from the already-configured startup task
~~~

Verify controlled Runtime restart returns to the same elevated state.

### 12.2 WING suppression matrix

At minimum:

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

### 12.3 Power lifecycle

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

### 12.4 Existing device features

Smoke-test existing helper-backed features without changing their implementation:

~~~text
TDP read/write
fan-related helper operations currently exposed
battery charge-limit path
~~~

The elevation migration must not regress their behavior.

---

## 13. Recommended implementation sequencing

### Phase A — Elevated Runtime migration

One focused implementation PR should cover only what is necessary to establish and verify the elevated Full1902 Runtime model:

~~~text
Runtime privilege requirement
+ startup-task highest-run-level contract/readback/repair
+ elevation verification/fail-close as needed
+ preserve UI/Overlay child launch
+ preserve existing WinGSuppressionGuard
+ tests
+ physical lifecycle validation
~~~

Do not use Phase A as an excuse to clean every now-redundant helper or external-launch path.

### Phase B — Privileged-process cleanup

After Phase A is proven on hardware, prepare a separate architecture/work order for:

~~~text
user EXE / PowerShell privilege boundary
+ TDP helper keep-vs-inline decision
+ any other now-redundant privileged helper review
~~~

Phase B must be evidence-driven and must preserve any useful process-failure isolation.

---

## 14. Relationship to existing Full1902 authority documents

This document changes the **process privilege model**, not the controller authority model.

The following remain authoritative for their existing scopes:

- HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md;
- REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md;
- FULL_1902_IMPLEMENTATION_ARCHITECTURE.md;
- the merged Full1902 WING / Game Bar Policy-B behavior represented by docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md.

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

 Existing TDP helper
    |
    +-- retained unchanged for first migration
    +-- helper consolidation deferred

 External user actions
    |
    +-- current behavior retained for first migration
    +-- final privilege boundary deferred to Phase B
~~~

The key simplification is:

> **Elevate the existing owner instead of creating another owner.**

The elevation migration should solve the administrator-game WING suppression failure by making the existing Full1902 Runtime authority valid at the required Windows integrity level, while deliberately postponing unrelated privileged-process cleanup until the new model is proven.
