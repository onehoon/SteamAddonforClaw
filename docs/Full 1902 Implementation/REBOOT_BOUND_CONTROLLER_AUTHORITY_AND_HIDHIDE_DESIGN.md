# Reboot-Bound Controller Authority and HidHide Design

> **Status:** Active reboot-bound authority architecture / core lifecycle implemented  
> **Scope:** MSI Center M Enabled/Disabled authority transitions, persistent PID1902 ownership, mandatory Addon Runtime lifetime, HidHide ownership, and virtual presentation selection  
> **Product direction:** Full PID1902 implementation / MSI Center M replacement controller architecture  
> **Implementation state:** Core authority transitions, PID1902 ownership, DirectInput/HidHide/VIIPER lifecycle, owned-state recovery, PnP return, suspend/resume, and stock-safe uninstall are implemented. Unexpected Runtime death auto-restart/lightweight keepalive and the final hardware validation matrix remain open.

---

## 1. Executive summary

MSI Center M Enabled/Disabled is a **reboot-bound controller-authority mode**, not a live controller-mode toggle.

The current product contract is:

```text
MSI Center M Enabled
    → MSI / stock controller authority
    → desired physical mode = PID1901
    → Addon controller stack is passive

MSI Center M Disabled
    → Addon Runtime is the mandatory controller authority
    → desired physical mode = PID1902
    → HidHide controller isolation persists
    → one VIIPER runtime owns the virtual-controller layer
    → exactly one virtual presentation is attached/live
```

The most important refinement is:

> **Center M Disabled is a durable authority choice, not merely an application runtime option.**
>
> **The Addon must continue to own the controller until the user explicitly chooses Enable Center M and Restart.**

Therefore Windows shutdown/restart is **not** an authority-release boundary.

While Center M remains Disabled:

```text
Windows shutdown/restart
    → do not intentionally restore PID1901
    → keep persistent HidHide configuration
    → next Addon Runtime startup inspects the actual physical PID
    → PID1902: keep it
    → PID1901: switch to PID1902
```

PID1901 restoration belongs to explicit authority release:

```text
Enable Center M and Restart
```

or another explicit stock-restoration path such as uninstall cleanup.

---

## 2. Why the reboot boundary still matters

The reboot requirement remains important for **intentional authority changes**.

Do not implement:

```text
Center M Enabled
→ kill MSI stack
→ immediately switch to PID1902
→ start Addon controller ownership
```

inside the same normal Windows session merely because HHC or ClawTweaks can support live switching.

The supported product transition is intentionally simpler:

```text
Current authority mode
    ↓
configure persistent next-boot state
    ↓
mandatory Windows restart
    ↓
next session starts under exactly one controller authority
```

This removes most transitional states involving:

- current-session Center M process teardown;
- Center M controller child-process races;
- live PID1901 → PID1902 handoff while the OEM stack is still running;
- HidHide mutation while both authorities are alive;
- same-session rollback complexity.

The reboot boundary applies to **authority changes**, not to every process restart or Windows lifecycle event.

---

## 3. Two authority modes

### 3.1 Center M Enabled

```text
Center M startup roots = Enabled / Automatic
Desired controller authority = MSI / Stock
Desired physical PID = PID1901
Addon DirectInput ownership = none
Addon physical HidHide ownership = none
Addon VIIPER controller presentation = none
```

Steam/BPM must not override this authority decision.

Independent Addon device features such as TDP, CPU Boost, Power Mode, fan control, telemetry, and other non-controller features may continue where supported.

### 3.2 Center M Disabled

```text
Center M startup roots = Disabled
Desired controller authority = Addon
Desired physical PID = PID1902
Addon Runtime = mandatory
HidHide Addon baseline = persistent
VIIPER Runtime = active while the Addon controller Runtime is alive
```

Presentation policy:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Steam/BPM selects presentation only. It never decides whether the physical controller is owned.

---

## 4. Center M Disabled means the Runtime is mandatory

The controller owner is the background Addon Runtime, not the frontend window.

The frontend/UI may be opened and closed normally:

```text
Frontend window closes
→ controller Runtime continues

QAM closes
→ controller Runtime continues

UI is not visible
→ controller Runtime continues
```

But while Center M is Disabled, the product must not offer an ordinary action that intentionally stops the controller Runtime and leaves Windows running without another controller authority.

Therefore the Disabled-mode product contract is:

```text
intentional Runtime exit = not supported
Disable Addon startup = not supported
Quit controller background host = not supported
```

If the user wants to stop Addon controller ownership, the supported path is:

```text
Enable Center M and Restart
```

### 4.1 Startup registration becomes mandatory

The existing background startup task should no longer be treated as an optional preference while Center M is Disabled.

```text
Center M Enabled
→ Launch at startup may remain a normal user preference where appropriate

Center M Disabled
→ Addon Runtime startup task MUST be enabled
→ UI setting should be forced/locked on
```

Disable transition must verify that the Addon Runtime will start on the next logon before it disables the Center M startup roots.

### 4.2 Start simple: no new service/supervisor yet

The final product should recover from unexpected Runtime death because a hidden PID1902 controller without a live virtual presentation is a real user-impacting failure.

However, the first implementation should remain simple.

Do **not** add a new Windows service, watchdog daemon, supervisor process, heartbeat protocol, restart epoch, or generalized keepalive state machine merely to complete the first ownership POCs.

Initial contract:

```text
Center M Disabled
→ existing Addon background startup task is mandatory
→ intentional Runtime exit is blocked
→ unexpected Runtime death is a known recovery requirement
```

Automatic crash restart/keepalive can be added as a focused hardening PR after the basic controller ownership path is hardware-proven.

The architecture must leave room for that recovery, but early POCs should not be blocked on a new supervisor design.

---

## 5. HidHide is persistent Disabled-mode configuration

While Center M is Disabled:

```text
HidHide Installed     = yes
Inverse mode          = off
HidHide Active        = on
Addon executable      = whitelisted
Addon-owned PID1902 primary gamepad collection = required hidden target when known
Same-root PID1902 control/consumer collections = optional exact hidden targets when unique
```

This configuration persists across:

- Addon Runtime restart;
- Windows shutdown;
- Windows restart;
- normal logoff/logon;
- crash recovery.

It is removed when Addon controller authority is explicitly released, such as:

```text
Enable Center M and Restart
uninstall / explicit stock restoration
```

Do not treat persistent Disabled-mode HidHide configuration as a stale routing-session lease.

---

## 6. Exclusive authority and conflicting controller software

Addon Controller Mode is not a coexistence mode with HHC, ClawTweaks, or another controller middleware.

Before entering Disabled mode, perform a simple admission check.

If a known conflicting controller manager or unsupported foreign HidHide controller configuration is detected:

```text
Do not enter Addon Controller Mode.
Do not attempt runtime coexistence.
Ask the user to disable/remove the conflicting controller stack first.
```

After admission:

```text
Addon controller state is the desired state.
```

Do not build a generalized multi-owner HidHide/controller authority framework.

---

## 7. Disable flow — configure next boot, then restart

User action:

```text
Disable MSI Center M
```

Confirmation:

```text
Disable MSI Center M and switch controller authority
 to Steam Addon for Claw.

Windows must restart to apply this change.

[Cancel] [Disable and Restart]
```

There is no `Restart Later` option.

### 7.1 Preflight

Before persistent mutation, verify at minimum:

```text
supported MSI Claw
HidHide available/configurable
required helper/elevation path available
Addon background Runtime startup can be enabled/verified
VIIPER prerequisites available as required by install state
no unsupported conflicting controller manager environment
```

Failure:

```text
do not change authority mode
do not reboot
report the blocking reason
```

### 7.2 Persistent mutation order

Recommended simple flow:

```text
1. ensure/verify mandatory Addon Runtime startup task
2. apply/verify Disabled-mode HidHide baseline
3. disable/verify MSI_Center_M_Server task
4. disable/verify MSI_Center_M_Updater task
5. disable/verify MSI Foundation Service startup
6. request immediate Windows restart
```

### 7.3 No live takeover in the current session

Do not perform:

```text
PID1901 → PID1902
DirectInput acquisition
virtual-controller attach
controller publisher startup
```

as part of the Disable button action.

The current Windows session remains MSI-owned until reboot.

Existing Center M processes therefore do not need to be killed merely to complete the Disable transition.

---

## 8. Disabled boot startup sequence

The first Disabled boot and every later Disabled boot use the same logic.

Do not add a `FirstBootAfterDisable` authority state unless real hardware proves it is required.

Recommended startup:

```text
Windows logon
    ↓
mandatory Addon Runtime starts
    ↓
verify supported MSI Claw
    ↓
verify Center M startup roots == Disabled
    ↓
verify expected Addon HidHide baseline
    ↓
start Steam/BPM observation
    ↓
initialize canonical VIIPER Runtime
    ├─ Xbox360 created / detached
    └─ SteamDeck created / detached
    ↓
inspect current physical PID
    ↓
reconcile to desired PID1902
    ↓
DirectInput acquire
    ↓
resolve/verify exact PID1902 primary gamepad collection
    ↓
reconcile HidHide isolation
    ↓
verify physical isolation
    ↓
read freshest Steam/BPM state
    ↓
attach exactly one virtual presentation
```

Critical rule:

> **No virtual controller may be attached before physical PID1902 ownership and HidHide isolation are verified.**

---

## 9. Boot-time PID policy: desired state, not forced round-trip

Center M Disabled means:

```text
DesiredPhysicalPID = PID1902
```

Startup should inspect current reality.

### Current PID1902

```text
verify same supported MSI Claw
→ keep PID1902
→ do NOT force PID1902 → PID1901 → PID1902
→ acquire DirectInput
→ verify/reconcile HidHide
```

### Current PID1901

```text
verify same supported MSI Claw
→ switch PID1901 → PID1902
→ bounded PnP settle
→ verify same physical identity
→ acquire DirectInput
→ verify/reconcile HidHide
```

### Temporarily missing

Use bounded startup/PnP stabilization.

Do not mutate ambiguous hardware.

Do not attach a virtual controller until the supported physical controller is proven.

This policy intentionally does **not** depend on whether firmware preserves PID1902 across a reboot.

```text
Current = 1902 → keep
Current = 1901 → switch
```

is sufficient.

---

## 10. First-boot HidHide behavior

The first Disabled boot may not yet have a trusted exact PID1902 collection identity.

Safe sequence:

```text
HidHide Active = true
Addon whitelisted
VIIPER devices = detached

reconcile physical controller to PID1902
→ PnP settle
→ resolve exact primary PID1902 gamepad collection
→ add/verify exact hidden-device entry
→ verify physical isolation
→ only then attach virtual controller
```

Until the hidden entry is established:

```text
physical PID1902 may be visible
virtual controller remains detached
```

This avoids a physical + virtual double-input window.

After the exact PID1902 entry is learned and persisted, subsequent boots can use the existing HidHide rule immediately.

---

## 11. Windows shutdown/restart while Center M remains Disabled

Windows shutdown/restart is **not** an authority-release request.

Therefore do not perform an intentional physical mode switch merely because the OS is shutting down.

Recommended shutdown/restart behavior:

```text
active virtual presentation
→ neutral
→ stop publisher
→ detach/teardown virtual output as required
→ release process-owned DirectInput/native handles
→ keep persistent HidHide Disabled-mode configuration
→ DO NOT issue PID1902 → PID1901 solely for shutdown/restart
→ Windows exits
```

The physical device may remain PID1902, or firmware/Windows may later enumerate it as PID1901.

The next Addon Runtime startup does not care which occurred. It reconciles current reality to desired PID1902.

This removes unnecessary PID/PnP churn at every system restart.

---

## 12. Controlled Addon Runtime restart while Disabled

A controlled Addon process restart caused by update/relaunch is also not an authority release.

Desired authority remains Addon.

```text
Runtime stops
→ do not intentionally restore PID1901 merely for process restart
→ persistent HidHide remains
→ new Runtime starts
→ inspect actual PID
→ keep/reclaim PID1902
→ rebuild DirectInput + VIIPER presentation
```

This path must still neutralize/retire process-owned virtual state safely before exit.

---

## 13. Intentional Runtime exit while Windows stays running

This is **not a supported normal user action** while Center M is Disabled.

Do not design a normal path such as:

```text
Exit Addon
→ restore PID1901
→ leave Center M Disabled
→ Windows continues
```

That creates a third unsupported authority state.

Instead:

```text
Center M Disabled
→ user cannot intentionally stop the controller Runtime
```

If the user wants the Addon controller Runtime to stop, the UI should direct them to:

```text
Enable Center M and Restart
```

The frontend itself may close; only the controller Runtime is mandatory.

---

## 14. Unexpected Runtime death

Unexpected Runtime death is a real reliability failure because it may leave:

```text
physical PID1902 hidden by HidHide
virtual presentation gone
```

The final product must recover automatically.

However, keep initial implementation simple:

- do not add a new Windows service/supervisor in the first ownership POCs;
- make the existing logon startup task mandatory while Disabled;
- block intentional Runtime stop;
- make restart/reconcile idempotent so a later keepalive mechanism can simply restart the Runtime;
- add automatic crash restart as a focused hardening step after the baseline path works on hardware.

When Runtime restarts after a crash:

```text
Center M Disabled
→ inspect current physical state
→ PID1902: reacquire
→ PID1901: reclaim PID1902
→ reconcile HidHide
→ create fresh VIIPER Runtime
→ restore current desired X360/Deck presentation
```

Do not require a stock PID1901 recovery round-trip first unless real hardware evidence proves one is required.

---

## 15. Steam/BPM presentation selection

Steam/BPM observation is a read-only fact source for presentation policy.

```text
Steam game active OR BPM active
    → SteamDeck

otherwise
    → Xbox360
```

The first presentation must be chosen from the freshest state immediately before first attach.

Do not hard-code boot to Xbox360 and then immediately switch if BPM was already active.

Normal presentation transition:

```text
current presentation
→ neutral
→ stop publisher
→ detach
→ attach target
→ send target neutral state
→ start target publisher
```

Throughout normal X360 ↔ SteamDeck switching:

```text
PID1902 unchanged
DirectInput unchanged
HidHide unchanged
VIIPER server/bus unchanged
```

---

## 16. Runtime PID1902 drift

While Center M is Disabled:

```text
same physical MSI Claw PID1902 → PID1901
```

is owned-state drift, not an authority transfer to respect.

Recovery:

```text
neutral virtual output
→ retire stale physical input session
→ verify same strong physical identity
→ verify Center M remains Disabled
→ quiesce conflicting Center M runtime if it resurrected
→ reclaim PID1902
→ bounded PnP settle
→ reacquire DirectInput
→ reconcile HidHide
→ resume current desired presentation
```

Do not restore the old `ExternalNativeTakeover → yield current Steam session` policy.

---

## 17. Physical device loss / PnP re-enumeration

Real PnP loss is supported lifecycle behavior.

If the physical controller temporarily disappears:

```text
DesiredAuthority = Addon
VirtualPresentation = attached but neutral if VIIPER itself is healthy
PhysicalInput = unavailable
```

Wait for relevant PnP arrival rather than issuing repeated mode commands against an absent device.

On return:

```text
same device + PID1902
→ reacquire DirectInput / repair HidHide

same device + PID1901
→ reclaim PID1902 / reacquire / repair

different or ambiguous device
→ do not mutate
→ fail closed
```

---

## 18. Sleep / hibernate / resume

Center M Disabled authority survives sleep/hibernate.

Do not intentionally restore PID1901 merely for suspend.

Before suspend:

```text
neutral virtual output
quiesce process-owned I/O as required
```

After resume:

```text
Center M still Disabled
→ desired physical PID remains 1902
→ inspect current physical state
→ PID1902: reacquire/reconcile
→ PID1901: reclaim PID1902
→ missing: wait for PnP
→ ambiguous: fail closed
→ choose current X360/Deck presentation from current Steam/BPM state
```

Do not assume pre-suspend device handles remain valid.

---

## 19. Center M runtime resurrection

A normal Disabled boot should prevent Center M startup roots from launching.

If a conflicting Center M controller runtime appears later:

```text
CenterMRuntimeDetectedWhileDisabled
→ targeted quiesce of known relevant MSI processes
→ controller reconcile
```

Do not build a broad MSI process killer or high-frequency PID/process polling loop.

Use actual lifecycle signals: DirectInput loss, PnP change, resume, detected known runtime, or explicit reconciliation.

---

## 20. Enable flow — explicit authority release

This is the primary normal path that changes desired physical state back to PID1901.

User action:

```text
Enable MSI Center M
```

Confirmation:

```text
Restore MSI Center M controller authority.

Windows must restart to apply this change.

[Cancel] [Enable and Restart]
```

There is no `Restart Later` option.

Recommended order:

```text
1. neutral active virtual presentation
2. stop publisher
3. detach virtual controller
4. release DirectInput
5. restore same physical MSI Claw to PID1901
6. verify PID1901 stock mode
7. teardown VIIPER Runtime
8. remove/verify Addon Disabled-mode HidHide controller baseline
9. enable/verify MSI_Center_M_Server task
10. enable/verify MSI_Center_M_Updater task
11. set/verify MSI Foundation Service = Automatic
12. release mandatory Addon startup lock/policy as appropriate
13. request immediate Windows restart
```

Next boot:

```text
Center M Enabled
→ MSI / stock owner
→ Addon controller stack passive
```

---

## 21. Uninstall / explicit stock restoration

Uninstall is an exceptional explicit authority-release path.

If Center M is Disabled, uninstall cleanup must not leave a hidden PID1902 controller without the Addon Runtime.

Before removing the product, establish a stock-safe state:

```text
virtual output retired
DirectInput released
PID1901 restored and verified
Addon HidHide controller baseline removed
mandatory Addon startup registration removed
Center M startup policy restored according to supported uninstall contract
```

Do not treat ordinary Windows restart as equivalent to uninstall.

---

## 22. Failure handling during authority transition

Keep transition logic explicit and small.

```text
perform required persistent mutations
→ read back / verify
→ only then request reboot
```

If mutation fails:

```text
do not reboot as though success occurred
do not claim the new authority mode is active
surface the blocking reason
```

Do not build a generalized transaction framework unless real failures prove the simple ordered approach insufficient.

---

## 23. Authority source of truth

Do not add a second persisted boolean if actual Center M startup configuration already defines the authority mode.

```text
Center M roots exactly Enabled / Automatic
→ desired authority = MSI / Stock

Center M roots exactly Disabled
→ desired authority = Addon

Partial / mixed
→ invalid / needs repair
→ do not silently pick an owner
```

Mandatory Runtime startup registration is a required invariant of Disabled mode, not a separate authority source.

---

## 24. Steady-state invariants

### Enabled

```text
CenterMStartupConfiguration = Enabled
DesiredPhysicalPID          = PID1901
Controller owner             = MSI / Stock
Addon DirectInput            = none
Addon physical HidHide       = none
Addon virtual presentation   = none
```

### Disabled, healthy Runtime

```text
CenterMStartupConfiguration = Disabled
Addon Runtime               = running / mandatory
DesiredPhysicalPID          = PID1902
Physical MSI Claw           = same supported device
DirectInput                 = healthy
HidHide                     = Addon baseline active
Physical gamepad            = isolated
VIIPER Runtime              = healthy
Xbox360 logical device      = created
SteamDeck logical device    = created
Exactly one presentation    = attached/live
```

### Disabled during Windows shutdown/restart

```text
Desired authority = Addon
Desired PID       = PID1902
Persistent HidHide baseline remains
No deliberate PID1901 restore solely for OS shutdown/restart
```

There is no normal steady state:

```text
Center M Disabled
+ Windows running
+ Addon controller Runtime intentionally absent
```

---

## 25. Architectural consequences for current code

Useful low-level primitives may remain:

- strong MSI Claw physical identity;
- bounded PnP stabilization;
- native PID switch/readback;
- DirectInput input source;
- exact primary PID1902 collection resolution;
- HidHide inspect/mutation/verification;
- canonical dual-device VIIPER Runtime;
- X360 and SteamDeck publishers/mappers;
- Steam RunningAppID/BPM observation;
- suspend/resume hooks;
- targeted Center M suppression primitives.

Old policy semantics may be replaced:

- Steam-session physical ownership;
- route-end PID1901 restoration;
- route-scoped HidHide leases;
- startup cleanup that assumes all Addon HidHide state is stale;
- `ExternalNativeTakeover → yield`;
- route-bound Center M guard lifetime;
- normal Addon process exit as a supported Disabled-mode authority release.

Do not add compatibility wrappers merely to preserve unreleased architecture.

---

## 26. Implementation status and remaining hardening

The original staged ownership implementation is complete.

~~~text
Persistent dual VIIPER devices                         DONE
Addon-owned persistent HidHide baseline                DONE
Mandatory Runtime / startup contract                    DONE
Reboot-bound Center M authority transition              DONE
Disabled-boot admission                                 DONE
PID1902 + DirectInput ownership                         DONE
First presentation attach                               DONE
Runtime X360 <-> SteamDeck switching                    DONE
Owned DirectInput recovery                              DONE
PID1901 drift reclaim                                   DONE
Physical disappearance / PnP return                     DONE
Suspend / Hibernate / Resume recovery                   DONE
Stock-safe uninstall                                    DONE
Legacy routing compatibility cleanup                    DONE
Elevated Runtime migration + helper privilege cleanup   DONE
~~~

### Current owned-state recovery

The current implementation already reuses one physical owner/reconcile path for realistic controller failures:

~~~text
DirectInput session loss
same-device PID1901 drift
physical device disappearance
device-arrival recovery
resume reconciliation
~~~

Do not add duplicate recovery managers, broad polling loops, or a second authority state machine.

For the currently observed MSI Quick Settings resurrection path, event-driven exact-package re-quiesce exists. Broader Center M process suppression must remain evidence-driven; do not introduce a general process killer merely because older roadmap text listed "Center M resurrection."

### Remaining product hardening

One controller-lifetime gap remains:

~~~text
unexpected Runtime process death
=> automatic Runtime restart / lightweight keepalive
~~~

This is a real supported lifecycle problem because Addon authority may leave PID1902 hidden while process-owned VIIPER presentation disappears.

The next design should restart the **existing Runtime owner** and let its already-implemented startup/recovery paths converge the controller. Prefer the smallest reliable mechanism demonstrated by product testing.

Do not assume a Windows service, watchdog daemon, supervisor hierarchy, heartbeat protocol, epochs, or barriers are required.

### Separate privilege follow-up

The High Runtime's user-launched EXE/PowerShell inheritance is tracked separately by the elevated-Runtime architecture. It is not part of reboot-bound controller authority.

---

## 27. Validation priorities

### Already covered by implemented architecture

The product now has implementation for:

- reboot-bound Disable/Enable authority transitions;
- Disabled boot from PID1901 or PID1902;
- exact HidHide isolation before live virtual presentation;
- X360 / SteamDeck presentation switching;
- owned DirectInput recovery;
- PID1901 drift reclaim;
- physical PnP loss/re-arrival;
- suspend/resume reconciliation;
- stock-safe uninstall;
- elevated Runtime startup and controlled Runtime restart.

Do not treat these as unimplemented roadmap items.

### Remaining release validation

#### Elevated WING

Validate physical WING behavior across:

~~~text
desktop
normal game
administrator-elevated game
Steam BPM
007FirstLight.exe elevated
007FirstLight.exe non-elevated
~~~

Native Game Bar must not surface under Addon authority, while Event88/configured action remains functional and no Win modifier sticks.

#### Sleep / Hibernate

Validate:

~~~text
Sleep -> Resume
Hibernate -> Resume
~~~

with Center M Disabled and verify controller/presentation recovery plus WING suppression after resume.

#### Retained inherited-High helpers

Validate:

- TDP/Fan/Battery helper operations;
- helper diagnostic remains elevated through token inheritance;
- no TDP helper-local UAC;
- Center M Disable/Enable and exact task/service readback;
- no Center M helper-local UAC.

#### Velopack / uninstall

Validate one real steady-state update/restart and one SafeUninstall path from Addon authority.

### Remaining implementation + validation: Runtime crash

After automatic Runtime restart/lightweight keepalive is implemented:

~~~text
force-kill Runtime while Center M Disabled
=> replacement Runtime starts without user intervention
=> startup/reconcile handles actual PID1902 or PID1901
=> HidHide / DirectInput / VIIPER return to a usable state
~~~

This is the remaining major reboot-bound controller-lifetime hardening item.

---

## 28. Review rules

Review future PRs against the real product lifecycle.

Blocking examples:

- Disabled mode allows the user to intentionally stop the only controller Runtime and leave Windows running;
- Disabled transition can complete without guaranteeing next-logon Addon Runtime startup;
- Windows restart unnecessarily forces PID1901 and introduces avoidable PnP churn;
- current PID1902 is needlessly round-tripped through PID1901 on startup;
- virtual controller attaches before physical isolation;
- Enable transition leaves PID1902/HidHide ownership behind;
- real PnP/resume/crash failures cannot converge to a usable controller.

Do not block for theoretical instruction-level races that do not map to supported handheld lifecycle behavior.

Do not add extra manager/state/epoch abstractions unless they protect a realistic failure path.

---

## 29. Final design summary

The controller authority model is intentionally simple:

```text
                    MSI CENTER M SETTING
                           │
              ┌────────────┴────────────┐
              │                         │
           ENABLED                   DISABLED
              │                         │
       MSI / STOCK OWNER          ADDON RUNTIME OWNER
              │                         │
           PID1901                  PID1902 desired
                                        │
                                     HidHide
                                        │
                                     VIIPER
                                  ┌─────┴─────┐
                                  │           │
                                X360       SteamDeck
                               normal      Steam/BPM
```

Disabled mode lifetime:

```text
Disable and Restart
        ↓
Addon Runtime becomes mandatory
        ↓
PID1902 remains desired
        ↓
Windows shutdown/restart does NOT release authority
        ↓
next Runtime startup reconciles actual PID to 1902
        ↓
...
        ↓
Enable Center M and Restart
        ↓
PID1901 restored
Addon HidHide ownership removed
MSI authority restored
```

Final principles:

1. **Center M Disabled means the Addon Runtime is the controller authority, not merely an optional app.**
2. **The frontend may close; the controller Runtime must remain.**
3. **PID1902 is the desired physical state for the entire Disabled-mode lifetime.**
4. **Windows shutdown/restart is not an authority-release boundary and should not deliberately force PID1901.**
5. **Startup keeps PID1902 if already present and switches PID1901 only when necessary.**
6. **HidHide remains persistent across reboot.**
7. **Steam/BPM only chooses X360 vs SteamDeck.**
8. **Explicit Enable Center M / uninstall is the normal PID1901 restoration boundary.**
9. **Unexpected Runtime death is a real recovery requirement, but initial POCs should not add a new service/supervisor prematurely.**
10. **Start simple: mandatory existing background startup first, lightweight keepalive hardening later.**
