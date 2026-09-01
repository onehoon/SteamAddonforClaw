# Full PID1902 Implementation — Controller Ownership Architecture

> **Status:** Design / implementation planning document  
> **Scope:** Future architecture for MSI Claw controller ownership and MSI Center M replacement  
> **Implementation state:** This document does **not** claim that the described architecture is implemented or hardware-validated.  
> **Important:** The application is still pre-release. Existing Steam-session routing behavior is not a compatibility contract and must not constrain the new product direction.

---

## Project direction

Steam Addon for Claw is changing from a Steam-routing add-on that coexists with MSI Center M into an integrated MSI Claw control platform.

The project should take authority over the major controller and device-control responsibilities that MSI Center M previously provided through the Center M application and MSI Game Bar widget, while preserving the existing Steam user experience for Steam games and Steam Big Picture Mode.

The intended product direction is:

> **When MSI Center M is Disabled, Steam Addon for Claw becomes the primary authority for the MSI Claw controller and the supported device-control features that were previously provided by MSI Center M.**
>
> **When MSI Center M is Enabled, the Addon leaves the physical controller on the MSI/stock path and continues to provide only the Addon features that do not require controller ownership.**
>
> **Steam routing remains a supported product feature, but Steam/BPM no longer decides whether the Addon owns the physical controller. Steam/BPM only selects the active virtual controller presentation while the Addon already owns the physical controller.**

This means the Addon should progressively replace the relevant MSI Center M responsibilities, including:

- the normal Xbox 360 controller presentation used outside Steam;
- physical controller mode ownership and input acquisition;
- controller isolation and virtual-controller presentation;
- TDP control;
- CPU Boost control;
- Windows power mode control;
- fan control / fan curve when implemented and validated;
- physical vibration-strength control when implemented and validated;
- other supported board-specific MSI Claw device settings that previously required the Center M application or Game Bar widget.

The controller model is:

```text
MSI Center M Enabled
    → MSI / stock controller authority
    → Addon does not take PID / DirectInput / HidHide / VIIPER controller ownership
    → TDP / CPU Boost / Power / Fan / other independent Addon features may still operate

MSI Center M Disabled
    → Addon controller authority
    → physical MSI Claw stays PID1902 / DirectInput while Addon owns it
    → physical gamepad collection stays isolated through HidHide
    → one long-lived VIIPER runtime owns the virtual-controller layer
    → Xbox360 is the normal/default presentation
    → Steam game / Steam Big Picture selects Steam Deck presentation
```

The important distinction is between **physical controller authority** and **virtual presentation policy**:

```text
Physical controller authority
    Center M Enabled  → MSI / Stock
    Center M Disabled → Addon

Virtual presentation while Addon owns the controller
    Steam/BPM inactive → Xbox360
    Steam/BPM active   → SteamDeck
```

The existing Steam routing experience is therefore retained from the user's perspective, but its internal role changes fundamentally.

Old meaning:

```text
Steam active
→ acquire physical controller
→ PID1902
→ Steam Deck

Steam inactive
→ release physical controller
→ PID1901
```

New meaning:

```text
Center M Disabled
→ physical controller already owned as persistent PID1902

Steam inactive
→ Xbox360 presentation

Steam active / BPM
→ SteamDeck presentation
```

Normal Xbox360 ↔ SteamDeck presentation changes must not require PID1901 ↔ PID1902 switching, DirectInput reacquisition, HidHide teardown/rebuild, or VIIPER runtime recreation.

This project direction should be used as the primary design and review lens for all future controller work.

Existing code may be reused when it provides proven low-level primitives or real lifecycle safety, but **existing orchestration is not authoritative merely because it already exists**. If an old Steam-session coordinator, external-takeover yield policy, route-bound ownership model, or compatibility layer conflicts with this direction, it may be simplified, repurposed, or removed.

The final product identity can be summarized as:

> **MSI Center M replacement for controller and core device-control responsibilities, plus first-class Steam integration.**

The target architecture should remain understandable in one diagram:

```text
                     MSI CENTER M SETTING
                            │
             ┌──────────────┴──────────────┐
             │                             │
          ENABLED                       DISABLED
             │                             │
      MSI / STOCK OWNER                ADDON OWNER
             │                             │
   controller stack untouched        PID1902 / DirectInput
   by Addon                           HidHide isolation
             │                             │
   independent Device features       one VIIPER runtime
   may still operate                      │
                                    ┌─────┴─────┐
                                    │           │
                                  Xbox360    SteamDeck
                                  Default     Steam/BPM
```

---

## 1. Executive summary

The product direction has changed fundamentally.

The old architecture treated Steam activity as the authority that temporarily borrowed the physical MSI Claw controller:

```text
Stock / PID1901
    ↓ Steam or BPM becomes active
PID1902 + DirectInput + HidHide + virtual Steam Deck
    ↓ Steam session ends
restore PID1901
```

That model was appropriate while Steam Addon for Claw had to coexist with MSI Center M and only needed the controller during Steam sessions.

That is no longer the intended product.

The new model is:

```text
MSI Center M Enabled
    → MSI / stock controller ownership
    → Addon does not mutate controller mode, HidHide, or VIIPER controller presentation
    → other Addon features may continue to operate

MSI Center M Disabled
    → Addon becomes the exclusive controller owner while the Addon process is running
    → physical MSI Claw remains PID1902 / DirectInput for the owned runtime
    → physical gamepad collection remains isolated through HidHide
    → one VIIPER runtime remains alive
    → Xbox360 and Steam Deck logical devices are both created for the process lifetime
    → exactly one virtual presentation is attached/live at a time
    → Xbox360 is the normal/default presentation
    → Steam game / Steam Big Picture selects Steam Deck presentation only
```

The single most important architectural change is:

> **Steam no longer decides whether the Addon owns the physical controller.**  
> **MSI Center M Enabled/Disabled decides physical controller authority.**  
> **Steam/BPM only decides which already-owned virtual presentation is exposed.**

This separation should drive all future implementation and cleanup decisions.

---

## 2. Non-goals and migration posture

### 2.1 Existing controller orchestration is not a preservation requirement

The current repository contains substantial logic built for the previous coexistence model, including concepts such as:

- Steam-session-bound native-mode ownership;
- route entry / route exit driving PID1901 ↔ PID1902 transitions;
- `ExternalNativeTakeover` causing the Addon to yield the current Steam routing session;
- route-bound Center M MainUI suppression;
- routing-session recovery boundaries;
- Steam-session cleanup deciding when native mode may be restored;
- dormant Game Bar/Xbox360 presentation work built around a Steam Deck outer route.

These concepts may contain useful low-level primitives or proven lifecycle handling, but **their policy semantics are not requirements for the new architecture**.

If retaining an old coordinator, stage, state machine, or policy makes the new model more complicated, it should be simplified, repurposed, or removed.

The goal is not to preserve old behavior.

The goal is:

```text
one controller owner
one desired physical state
one VIIPER runtime
one active virtual presentation
one reconciliation path
one teardown path
```

### 2.2 Do not add compatibility complexity for unreleased behavior

The project is pre-release. There is no requirement to maintain behavior merely because it exists in current main.

Do not add compatibility layers such as:

- legacy route mode vs new ownership mode;
- dual policy engines;
- old/new takeover semantics selected by hidden flags;
- adapters whose only purpose is preserving dead architecture;
- additional managers solely to keep old terminology alive.

If the new controller platform replaces an old policy, delete the old policy once the new path is proven.

### 2.3 Do not over-engineer theoretical races

The implementation must protect real handheld lifecycle events:

- boot / login / app startup;
- shutdown / restart / crash;
- sleep / hibernate / resume;
- physical device loss;
- PnP re-enumeration;
- PID1901 ↔ PID1902 changes;
- HidHide drift;
- VIIPER ownership / teardown failures;
- actual Center M resurrection;
- actual DirectInput read/acquire failures.

It should not add epochs, barriers, generalized authority frameworks, or multiple state machines only to defend against instruction-level interleavings with no realistic user-impacting path.

---

## 3. Product authority model

There are two top-level controller modes.

### 3.1 MSI Center M Enabled — Stock controller mode

When MSI Center M startup configuration is Enabled, the Addon must remain passive with respect to the controller stack.

Expected behavior:

```text
MSI Center M = Enabled

Physical controller:
    stock MSI ownership

Addon controller mutations:
    PID switch            = none
    DirectInput ownership = none
    HidHide controller    = none
    VIIPER controller     = none
```

The Addon may still provide unrelated or independent features such as:

- TDP control;
- CPU Boost;
- Windows power mode;
- future fan controls;
- telemetry;
- other device/system features that do not require controller ownership.

Steam becoming active must no longer cause physical controller takeover while Center M remains Enabled.

#### Invariant

```text
CenterMEnabled
→ AddonMustNotOwnPhysicalController
```

### 3.2 MSI Center M Disabled — Addon controller mode

When MSI Center M startup configuration is Disabled, the user's product choice means:

> The user has chosen Steam Addon for Claw as the controller owner instead of MSI Center M.

The desired steady state while the Addon process is alive is:

```text
Center M startup configuration = Disabled
Physical MSI Claw               = PID1902 / DirectInput
DirectInput session             = acquired and healthy
Physical gamepad collection     = hidden from ordinary applications
VIIPER runtime                  = alive
Xbox360 logical device          = created
Steam Deck logical device       = created
Exactly one virtual device      = attached/live
```

Normal/default presentation:

```text
Xbox360 = attached + live
SteamDeck = detached + waiting
```

Steam/BPM presentation:

```text
Xbox360 = detached + waiting
SteamDeck = attached + live
```

#### Invariant

```text
CenterMDisabled
+ AddonRunning
+ SupportedPhysicalIdentityKnown
→ DesiredPhysicalMode = PID1902
```

PID1901 is not an alternate acceptable steady state in Addon-owned mode. It is a recoverable drift state.

---

## 4. Center M startup configuration vs effective runtime state

The startup configuration introduced by the Center M startup-control POC must not be confused with the current process state.

Disabling the startup roots changes configuration immediately but intentionally does not terminate the existing Center M session in that POC.

Therefore this state is possible:

```text
MSI_Center_M_Server task = Disabled
MSI_Center_M_Updater task = Disabled
MSI Foundation Service StartType = Disabled

BUT

existing Center M processes/services from the current Windows session are still alive
```

This means:

```text
CenterMStartupState.Disabled
!=
CenterMRuntimeQuiescent
```

A future controller-ownership implementation must not simply execute:

```text
if CenterMStartupState == Disabled:
    TakeControllerImmediately()
```

because the user may have changed the setting and restarted only the Addon, not Windows.

### Recommended admission rule

At Addon startup:

```text
Center M startup configuration = Disabled
AND
Center M runtime is quiescent
AND
hardware is supported
AND
recovery state is safe
→ controller ownership may begin
```

If startup configuration is Disabled but the old Center M runtime is still active because Windows has not restarted yet, the Addon should stay controller-passive and continue showing that a restart is required.

Do not introduce a custom persisted reboot epoch merely to distinguish these states if actual Windows process/service state can answer the question directly.

---

## 5. Known MSI Center M roots and runtime identities

HHC and current ClawTweaks research agree on the primary Center M startup roots.

### Scheduled Tasks

```text
MSI_Center_M_Server
MSI_Center_M_Updater
```

### Service

```text
MSI Foundation Service
```

### Known process identities used by HHC's Claw Center watcher

```text
MSI_Center_M_Server
MSI Center M
MCMOSDInfo
MSI Center OSD Info
```

HHC's `ISpaceWatcher.Disable()` performs:

```text
DisableTasks()
DisableServices()
KillProcesses()
```

and its Claw-specific watcher uses the exact task/service/process names above.

This is relevant because it demonstrates that process termination is not merely a theoretical fallback: disabling the OEM control-center runtime and terminating its current process tree is an established practical policy in a similar handheld controller application.

### `MSI_Center_M_Server_ControlMode`

Existing device research has also identified `MSI_Center_M_Server_ControlMode` as an important controller-related process.

Do not immediately build a huge list of every Center M child process.

Prefer a narrow policy:

1. terminate the known root process(es) that HHC already targets;
2. wait for exit;
3. verify that the controller-related child process is no longer present;
4. add an explicit fallback kill for a specific orphan only if real hardware proves that the root termination does not retire it.

The design target is **exact ownership enforcement**, not broad process destruction.

---

## 6. Center M resurrection policy

The previous architecture treated an externally caused PID1902 → PID1901 transition as a reason to yield the current Steam routing session.

That policy was correct only because the old product attempted to coexist with another controller authority.

In the new architecture, when Center M is Disabled:

```text
Addon owns controller
same physical MSI Claw changes PID1902 → PID1901
```

must be interpreted as:

```text
OwnedPhysicalStateDrift
```

not:

```text
ExternalAuthorityWon
```

### New policy

If the Addon has controller authority and the same strongly identified physical MSI Claw stabilizes as PID1901:

```text
1. neutralize the active virtual presentation
2. retire the dead physical-input publisher/session
3. verify Center M startup roots remain Disabled
4. quiesce/terminate the conflicting Center M runtime
5. switch the same physical MSI Claw back to PID1902
6. wait for PnP topology stabilization
7. reacquire DirectInput on the same physical identity
8. reconcile HidHide isolation
9. resume the previously selected virtual presentation
```

The Addon should **reclaim** the controller.

It should not yield until the next Steam session, because Steam is no longer the physical ownership authority.

---

## 7. Prevention first, recovery second

The preferred ownership enforcement model has layers.

### Layer 1 — persistent startup policy

When the user selects Center M Disabled:

```text
MSI_Center_M_Server        task disabled
MSI_Center_M_Updater       task disabled
MSI Foundation Service     startup disabled
```

### Layer 2 — MainUI launch prevention

The repository already contains a Center M MainUI routing guard / helper ownership mechanism that was built to prevent a newly launched real `MSI Center M.exe` from becoming operational during a route.

The new architecture may reuse the useful low-level prevention mechanism, but its lifetime should no longer be tied to a Steam route.

Conceptually:

```text
Addon controller ownership begins
→ Center M MainUI prevention armed

Addon controller ownership ends
→ Center M MainUI prevention disarmed
```

The prevention mechanism is not the controller authority itself. It protects the authority.

### Layer 3 — runtime quiesce / targeted process termination

If Center M runtime is already alive or somehow resurrects despite startup policy / MainUI prevention, the Addon may terminate the known conflicting runtime while Center M is Disabled.

### Layer 4 — physical-state reconciliation

If the controller still drifts to PID1901, reclaim PID1902 and rebuild the physical input path.

---

## 8. Do not implement a brute-force PID polling loop

Avoid architecture such as:

```text
while Addon is running:
    every 500 ms:
        check PID
        check Center M
        force PID1902
        kill anything suspicious
```

The repository already has or can naturally receive meaningful lifecycle triggers:

- application startup;
- physical DirectInput read failure;
- PnP device removal;
- PnP device arrival;
- resume;
- explicit Center M configuration change;
- controller ownership acquisition;
- controller ownership shutdown.

These events should converge through one reconciliation operation.

A low-rate process watchdog should only be added if hardware evidence shows a Center M resurrection path that is not caught quickly enough by existing ownership/input/PnP signals.

---

## 9. Physical input failure is already a strong detector

The current MSI Claw DirectInput source polls input frequently and stops the owned session when `ReadState()` fails.

A real PID1902 disappearance or device loss naturally produces:

```text
DirectInput session
    ↓ ReadState failure / device loss
physical input completion callback
```

The policy after that signal must change.

### Old policy

```text
physical input lost
→ inspect native state
→ same device now PID1901
→ classify ExternalNativeTakeover
→ fail-close / yield current Steam session
```

### New policy while Center M is Disabled

```text
physical input lost
→ neutral active virtual output
→ inspect stable physical state

same physical device + PID1901
    → owned native-mode drift
    → quiesce Center M
    → reclaim PID1902

same physical device + PID1902
    → reacquire DirectInput / repair isolation

no device present
    → enter recoverable physical-absence state
    → wait for PnP arrival

ambiguous or different physical identity
    → do not mutate
    → fail closed
```

---

## 10. PnP re-enumeration and complete device disappearance

A physical controller switching native mode may temporarily disappear from PnP while Windows tears down one topology and creates another.

The current Addon already has stable-state capture logic that can tolerate transient `DeviceNotFound` and mixed PID1901/PID1902 topology while waiting for the native-mode transition to settle.

That concept should remain.

### HHC reference behavior

HHC's controller subsystem treats arrival/removal as a normal lifecycle:

- it subscribes to XUSB/HID device arrival and removal;
- it hydrates already-present gaming devices during startup;
- it tracks expected power-cycle removals separately;
- it selects/reconnects controllers after the device returns.

The useful lesson is:

> **Do not attempt to keep a dead HID/DirectInput handle alive. Retire the old session, rediscover the returned physical device, verify identity, and bind a new session.**

### New Addon behavior for complete temporary absence

If PID1902 disappears and neither PID1901 nor PID1902 stabilizes within the normal transition allowance:

```text
DesiredAuthority = AddonOwned
PhysicalState = Missing
VirtualPresentation = keep attached but NEUTRAL
```

Do not immediately discard Addon ownership intent.

Do not repeatedly issue mode commands against an absent device.

Wait for the relevant MSI Claw PnP arrival.

On arrival:

```text
same strongly identified physical Claw?
    no  → remain fail-closed / do not mutate
    yes → inspect current native mode
```

If PID1902:

```text
acquire DirectInput
→ verify first valid input
→ reconcile HidHide
→ restore live virtual publisher
```

If PID1901:

```text
quiesce Center M runtime if needed
→ switch to PID1902
→ wait for PnP settle
→ acquire DirectInput
→ reconcile HidHide
→ restore live virtual publisher
```

---

## 11. Virtual output behavior during physical loss

When physical input authority is temporarily lost, do not necessarily destroy the virtual controller immediately.

Prefer:

```text
active virtual logical device remains attached
input report becomes neutral
```

This:

- avoids unnecessary virtual controller slot churn;
- reduces controller-disconnect/reconnect behavior inside games;
- preserves the selected presentation while the physical source is recovered.

Safety requirements:

- no stale button/axis state may remain live;
- rumble must be neutralized/stopped;
- if VIIPER ownership itself becomes unsafe or ambiguous, fail-close that layer rather than preserving a broken virtual device.

---

## 12. Persistent PID1902 ownership lifecycle

### 12.1 Startup — Center M Enabled

```text
Addon starts
→ supported hardware probe
→ Center M startup state = Enabled
→ controller ownership = Stock
→ do not initialize active controller takeover path
→ continue unrelated Addon features
```

No Steam event may override this decision.

### 12.2 Startup — Center M Disabled but current Center M runtime still alive

Typical case: user clicked Disable but restarted only the Addon.

```text
Addon starts
→ startup roots read Disabled
→ current Center M runtime still present
→ controller ownership remains passive
→ show Restart required / Center M runtime still active
```

### 12.3 Startup — Center M Disabled and runtime quiescent

```text
Addon starts
→ supported MSI Claw confirmed
→ Center M startup roots = Disabled
→ Center M runtime quiescent
→ recovery state safe
→ establish Addon controller ownership
```

---

## 13. Recommended ownership acquisition sequence

The exact function names may change, but the lifecycle order should be approximately:

```text
1. verify supported MSI Claw hardware
2. verify Center M startup configuration = Disabled
3. verify current Center M runtime is quiescent
4. verify recovery state allows forward mutation
5. initialize one canonical VIIPER runtime/server/bus
6. create Steam Deck logical device detached
7. create Xbox360 logical device detached
8. verify both logical devices exist and are detached
9. capture strong physical MSI Claw identity
10. journal/establish physical ownership boundary
11. switch physical device to DirectInput / PID1902 if not already there
12. wait for PnP re-enumeration to stabilize
13. select the exact primary MSI Claw DirectInput gamepad collection
14. acquire DirectInput
15. verify first valid physical input state
16. establish/reconcile HidHide isolation for the physical gamepad collection
17. arm Center M MainUI prevention for the owned lifetime
18. set Xbox360 neutral state
19. attach Xbox360
20. start physical → Xbox360 publishing
21. report Addon-owned controller state Ready
```

Xbox360 and Steam Deck should both be created for the long-lived VIIPER runtime, but they must **not** both be attached.

---

## 14. VIIPER process-lifetime model

Future production contract:

```text
VIIPER server         = process-lifetime while Addon owns controller
USB bus               = process-lifetime while Addon owns controller
Steam Deck logical    = created once
Xbox360 logical       = created once
```

Both logical devices should start detached.

Default steady state:

```text
Xbox360   = Attached + Live
SteamDeck = Detached + Waiting
```

Steam/BPM state:

```text
Xbox360   = Detached + Waiting
SteamDeck = Attached + Live
```

### Exactly-one-presentation invariant

At all stable points:

```text
Attached(Xbox360) XOR Attached(SteamDeck) == true
```

except short controlled transition boundaries where both are detached.

Do not create separate VIIPER runtimes or buses for the two presentations.

---

## 15. Presentation switching

### Xbox360 → Steam Deck

```text
Xbox360 live
→ send neutral Xbox360 state
→ stop/join Xbox360 publisher
→ detach Xbox360
→ attach SteamDeck
→ establish neutral SteamDeck state
→ start SteamDeck publisher
→ live SteamDeck
```

### Steam Deck → Xbox360

```text
SteamDeck live
→ send neutral SteamDeck state
→ stop/join SteamDeck publisher
→ detach SteamDeck
→ attach Xbox360
→ establish neutral Xbox360 state
→ start Xbox360 publisher
→ live Xbox360
```

Throughout both transitions:

```text
PID1902 remains unchanged
DirectInput remains acquired
HidHide remains unchanged
Center M prevention remains armed
VIIPER server/bus remain alive
```

---

## 16. Steam/BPM becomes a presentation selector only

Automatic policy:

```text
Steam game active OR Steam Big Picture active
    → desired presentation = SteamDeck

otherwise
    → desired presentation = Xbox360
```

Steam detection must not decide:

- whether PID1902 should be owned;
- whether HidHide should be active;
- whether DirectInput should be acquired;
- whether the Addon owns the physical controller;
- whether Center M should be allowed to reclaim native mode.

---

## 17. Recovery when PID1902 becomes PID1901

This is a normal recoverable ownership drift while Center M is Disabled.

Possible triggers:

- DirectInput `ReadState` failure;
- PnP removal of owned PID1902 collection;
- stable native-state probe showing same physical MSI Claw as PID1901;
- resume reconciliation showing same-device PID1901.

Recovery sequence:

```text
1. latch physical-source unavailable
2. neutralize active virtual output immediately
3. stop/join the physical input publisher/session
4. wait for native PnP topology to stabilize
5. verify strong identity of the same physical MSI Claw
6. verify Center M startup roots remain Disabled
7. terminate/quiesce conflicting Center M runtime if present
8. switch same physical device to DirectInput / PID1902
9. wait for stable PID1902 topology
10. reacquire exact DirectInput controller
11. verify first valid input
12. reconcile HidHide state
13. restore previously selected Xbox360/SteamDeck publisher
14. clear recoverable drift state
```

There is no `yield until Steam session end` policy in this mode.

Do not endlessly fight a live competitor. Quiesce the conflicting Center M runtime **before** reclaiming PID1902.

---

## 18. Recovery when the controller fully disappears

If no known PID1901 or PID1902 representation of the owned physical MSI Claw exists after the normal re-enumeration settle window:

```text
DesiredOwnership = AddonOwned
ActualPhysicalState = Missing
```

Actions:

```text
- virtual presentation remains neutral
- do not publish stale physical state
- stop rumble
- keep VIIPER presentation attached if VIIPER itself is healthy
- wait for relevant PnP arrival
```

On PnP arrival, rerun the same ownership reconciliation using strong physical identity.

If a different physical controller appears, do not mutate it.

If identity is ambiguous, fail closed.

---

## 19. Strong physical identity requirement

All reclaim operations that can mutate native mode must prove they are acting on the intended MSI Claw.

Allowed recovery:

```text
same physical MSI Claw strongly identified
→ native-mode reconciliation allowed
```

Unsafe recovery:

```text
multiple logical MSI controller candidates
identity confidence not strong
new/different physical root
unreadable/indeterminate topology beyond settle allowance
```

In unsafe cases:

```text
do not issue mode mutation
keep virtual output neutral
surface failure
fail closed
```

---

## 20. HidHide ownership

The physical MSI Claw gamepad collection must remain hidden from ordinary applications while the Addon owns PID1902, preventing double input alongside the virtual Xbox360/Steam Deck.

Important topology rule:

> Hide the exact gamepad collection, not the entire PID1902 device tree.

Reuse the existing ownership-aware HidHide primitives where they fit:

- readable-configuration validation;
- Addon whitelist ownership;
- journaled hidden entries;
- repair of missing Addon-owned entries;
- safe Active-state repair;
- safe rollback;
- refusal of ambiguous/foreign blocked states.

Do not implement a second independent HidHide authority.

### PID1901 transient visibility

During an unexpected PID1902 → PID1901 drift, PID1901 may briefly become visible to Windows while the virtual controller remains attached.

Do not preemptively create a second complicated PID1901 hiding policy unless real hardware testing shows user-visible double-input or slot damage during the reclaim interval.

Initial policy:

```text
neutral virtual output quickly
quiesce competitor
reclaim PID1902 quickly
restore normal isolation
```

---

## 21. Center M process termination philosophy

Center M Disabled is an explicit user choice to not use Center M as controller authority.

Therefore targeted Center M process termination in Addon-owned mode is allowed and may be required.

Principles:

- terminate only known MSI Center M identities relevant to the conflict;
- do not kill unrelated MSI software broadly;
- verify termination where practical;
- verify startup roots are still Disabled;
- reclaim PID1902 only after the competitor has been quiesced;
- do not create a constant high-frequency kill loop;
- use actual resurrection/drift events to trigger reconciliation.

Initial target set from the HHC Claw Center watcher:

```text
MSI_Center_M_Server
MSI Center M
MCMOSDInfo
MSI Center OSD Info
```

If hardware evidence proves additional controller-owning children survive and continue mutating mode, add only the exact required process identity.

---

## 22. Manual Center M launch while Addon owns the controller

Expected behavior when Center M is Disabled:

```text
user launches MSI Center M from Start menu
→ Addon-owned Center M prevention blocks operational MainUI if possible
→ if real conflicting runtime still becomes active, ownership reconciliation quiesces it
→ PID1902 remains or is reclaimed
→ Addon controller remains usable
```

The Addon must not silently yield controller authority merely because the user managed to start a disabled Center M executable.

If the user wants Center M ownership again, they should Enable Center M through the supported Addon UI transition.

---

## 23. Normal shutdown

Even while Center M is Disabled, an Addon process that exits normally should leave the machine with a usable physical controller.

Recommended fail-safe outcome:

```text
active virtual presentation neutral
→ stop/join virtual publisher
→ detach active virtual controller
→ stop physical publisher/input session
→ remove/restore Addon-owned HidHide state
→ restore physical controller to PID1901 / XInput
→ disarm Center M MainUI prevention
→ teardown VIIPER runtime
→ complete recovery journal
```

Final invariant:

```text
Addon process absent
→ physical MSI Claw usable in stock PID1901 state
```

Do not intentionally leave the user's physical controller hidden in PID1902 after the application has exited.

---

## 24. Crash and restart recovery

Crash recovery should prioritize a proven safe baseline before attempting a fresh ownership acquisition.

Recommended restart flow:

```text
Addon starts after crash
→ detect incomplete recovery ownership/journal
→ retire stale virtual-output ownership
→ repair/restore HidHide ownership
→ restore/prove safe physical stock state as required by recovery contract
→ mark recovery safe
```

Then re-evaluate product policy:

```text
Center M Enabled
    → remain stock/passive

Center M Disabled + runtime quiescent
    → start a NEW Addon ownership session
    → acquire PID1902
    → attach default Xbox360
```

Do not optimize abnormal recovery by skipping the safe recovery boundary merely because the eventual desired state is PID1902.

---

## 25. Suspend / hibernate / resume

Sleep/hibernate/resume is a supported real lifecycle and must remain robust.

### Suspend intent

While Addon owns the controller:

```text
neutral active virtual presentation
quiesce input/publisher as required
preserve only ownership state that can be safely reconciled after resume
```

Resume must not assume old device handles remain valid.

### Resume reconciliation

```text
Center M now Enabled?
    → release Addon controller ownership safely / remain stock

Center M still Disabled?
    → desired authority remains AddonOwned
```

Then inspect the physical controller:

```text
same physical device PID1902
    → reacquire/reconcile DirectInput + HidHide + selected presentation

same physical device PID1901
    → quiesce conflicting Center M runtime if present
    → reclaim PID1902
    → reacquire/reconcile

physical device missing
    → keep virtual neutral
    → wait for PnP arrival

identity ambiguous
    → fail closed
```

Steam/BPM state should only be consulted after physical ownership is healthy, to choose Xbox360 vs Steam Deck.

---

## 26. Presentation selector after resume/recovery

Whenever physical ownership becomes healthy again, determine desired virtual presentation from current policy:

```text
if Steam game active or BPM active:
    DesiredPresentation = SteamDeck
else:
    DesiredPresentation = Xbox360
```

If the currently attached logical device already matches, restart publishing into it.

If it differs, use the canonical neutral → detach → attach → neutral → live switch.

Do not mutate PID1902 for this decision.

---

## 27. One reconciliation path

Avoid creating separate long-lived authorities such as:

- `ControllerAuthorityManager`;
- `NativeTakeoverRecoveryManager`;
- `Pid1902Watchdog`;
- `CenterMReclaimManager`;
- `PnpRecoveryManager`;
- `PresentationRecoveryManager`.

Important lifecycle triggers should converge through one controller-owner reconciliation path.

Conceptually:

```text
ReconcileOwnedControllerAsync(trigger)
```

Possible triggers:

```text
Startup
Resume
PhysicalInputLost
PnPArrived
PnPRemoved
CenterMRuntimeDetected
ManualRecovery
```

The reconcile operation evaluates the same facts every time:

```text
1. Is Center M Disabled?
2. Is this supported MSI Claw hardware?
3. Is recovery mutation permission safe?
4. Is the physical identity strong and unambiguous?
5. What is current native mode: PID1902 / PID1901 / missing?
6. Is conflicting Center M runtime alive?
7. Is DirectInput alive?
8. Is HidHide ownership healthy?
9. Is VIIPER runtime healthy?
10. Which presentation should be attached/live?
```

This does not imply building a generalized declarative state-machine framework.

---

## 28. Desired-state classification

Useful conceptual states should remain small and product-facing.

### Stock

```text
Center M Enabled
Addon does not own physical controller
```

### Owned / Healthy

```text
Center M Disabled
same MSI Claw PID1902
DirectInput healthy
HidHide healthy
VIIPER healthy
one presentation attached/live
```

### Owned / Recovering Native Drift

```text
Center M Disabled
same MSI Claw currently PID1901
reclaim in progress
```

### Owned / Physical Missing

```text
Center M Disabled
physical MSI Claw temporarily absent
virtual presentation neutral
waiting for PnP recovery
```

### Unsafe / Ambiguous

```text
identity ambiguous
foreign controller conflict
HidHide ownership unsafe
recovery state unsafe
VIIPER ownership ambiguous
```

Do not multiply these into dozens of persistent enum states unless implementation evidence requires it.

---

## 29. Current low-level components likely worth reusing

The architecture change does **not** mean all existing code should be discarded.

### Native state / identity

- MSI native-state capture;
- strong physical identity resolution;
- stable PnP capture that tolerates transient DeviceNotFound/mixed topology;
- native mode switch primitive;
- native restore verification;
- recovery journal primitives.

### Physical input

- exact DirectInput device selection;
- acquire/first-valid-state verification;
- background input polling;
- physical input completion/fault signal;
- current physical identity projection.

### HidHide

- exact collection targeting;
- whitelist ownership;
- journaled Addon-owned hidden entries;
- health/reconcile behavior;
- safe rollback.

### VIIPER

- canonical native API loading;
- one server / one bus model;
- typed Steam Deck logical device;
- typed Xbox360 logical device support;
- attachment state query;
- attach/detach primitives;
- neutral report/state primitives;
- publisher foundations;
- teardown verification.

### Center M prevention

- existing MainUI suppression/ownership primitive may be reusable after changing its lifetime from route-bound to controller-ownership-bound.

Reuse proven primitives where they simplify the new design.

---

## 30. Existing orchestration/policy likely to be replaced or removed

Treat the following as candidates for deletion/rewrite, not preserved architecture:

```text
Steam session starts physical ownership
Steam session ends physical ownership
ExternalNativeTakeover → yield current Steam session
External takeover latch cleared only at Steam-session boundary
Steam output active == physical controller authority
route-bound Center M guard lifetime
Steam route as outer owner of Xbox360 temporary presentation
physical PID rollback solely because Steam becomes inactive
```

If `RoutingPipelineRuntimeCoordinator` or `AddonRoutingRuntime` remains useful only after extensive semantic distortion, consider replacing/simplifying it rather than adding a second controller ownership layer around it.

Renaming can happen after behavior is stable; do not block POCs solely for naming cleanup.

---

## 31. Recommended conceptual owner

A future controller-platform owner may conceptually resemble:

```text
MsiClawControllerRuntime
```

The exact type name is not mandated.

Its responsibility should be the lifecycle of **one owned physical MSI Claw controller stack**:

```text
physical native mode
DirectInput session
HidHide isolation
VIIPER runtime
selected virtual presentation
ownership reconciliation
shutdown/recovery ordering
```

This does not mean every primitive should be implemented inside one huge class.

It means there should be **one top-level authority**, not multiple managers each deciding desired state independently.

---

## 32. Center M Enabled transition

Preferred simple flow:

```text
User selects Enable Center M
→ enable Server task
→ enable Updater task
→ MSI Foundation Service StartType = Automatic
→ tell user restart required
```

Do not immediately start Center M and simultaneously tear down the live Addon controller stack unless a later product requirement explicitly needs same-session mode switching.

After reboot:

```text
Center M Enabled
→ Addon stays controller-passive
```

---

## 33. Center M Disabled transition

Preferred flow:

```text
User selects Disable Center M
→ disable Server task
→ disable Updater task
→ MSI Foundation Service StartType = Disabled
→ tell user restart required
```

The current startup-control POC deliberately does not kill current-session Center M processes at button-click time.

That remains acceptable if controller ownership does not begin until after the effective runtime is quiescent / rebooted.

After reboot:

```text
Center M Disabled
→ Addon starts
→ no Center M runtime
→ Addon acquires PID1902 baseline
→ Xbox360 becomes default presentation
```

If immediate same-session transition is added later, design it as a separate explicit lifecycle rather than hiding it inside the startup-setting operation.

---

## 34. Failure policy

### Safe automatic recovery

Automatic recovery is appropriate when all of these are true:

```text
Center M is Disabled
same physical MSI Claw is strongly identified
recovery mutation permission is safe
failure is a known owned-state drift or temporary absence
```

Examples:

- PID1902 → same-device PID1901;
- DirectInput handle died during PnP transition;
- same-device PID1902 returned and needs reacquire;
- Addon-owned HidHide entry disappeared;
- physical device temporarily vanished and later returned.

### Fail closed / do not mutate

Do not automatically force recovery when:

- physical identity is ambiguous;
- multiple logical MSI controllers appear;
- the returning device is not strongly proven to be the same physical device;
- recovery journal/ownership is unsafe;
- HidHide configuration is unreadable/unsafe;
- VIIPER ownership cannot be verified;
- Windows power transition gate disallows mutation;
- mode switch/read-back cannot establish a known state.

Fail closed should neutralize user-visible virtual input rather than publish stale state.

---

## 35. Hardware validation priorities

Because real MSI Claw hardware is not always available during development, software changes should be split into small POCs with explicit hardware validation later.

### Validation A — boot baseline after Center M Disabled

Precondition:

```text
Center M startup roots Disabled
Windows restarted
```

Validate:

- Center M processes do not auto-start;
- Addon recognizes Disabled mode;
- physical controller becomes PID1902;
- PID1902 topology settles correctly;
- DirectInput acquisition succeeds;
- exact physical gamepad collection is hidden;
- Xbox360 virtual controller attaches;
- controls work normally;
- Steam Deck logical device exists but remains detached;
- no duplicate physical controller is user-visible;
- rumble behavior is known/recorded even if not yet final.

### Validation B — Center M Enabled boot

Validate:

- no PID mutation by Addon;
- no HidHide controller ownership;
- no VIIPER controller presentation takeover;
- stock MSI controller remains usable;
- unrelated Addon features still work.

### Validation C — repeated X360/Deck presentation switches

With persistent PID1902:

- perform approximately 100 manual switches;
- physical PID remains 1902;
- no DirectInput reacquire for normal presentation changes;
- HidHide state does not change;
- exactly one virtual device is attached;
- no stale virtual controller remains;
- no unusable controller after repeated switching;
- measure real blackout between last source live input and first destination live input.

Do not invent an arbitrary latency target before measurement.

### Validation D — forced PID1901 takeover

While Addon owns PID1902:

- intentionally reproduce a Center M/native takeover if possible;
- confirm DirectInput failure is detected;
- confirm virtual output becomes neutral;
- confirm same physical device is identified as PID1901;
- confirm conflicting Center M runtime is quiesced;
- confirm PID1902 is reclaimed;
- confirm DirectInput/HidHide recover;
- confirm previous virtual presentation resumes;
- confirm no permanent controller loss.

### Validation E — full device disappearance / re-enumeration

- force a realistic PnP loss/re-enumeration condition;
- confirm virtual output becomes neutral rather than stale;
- confirm returned physical device is rediscovered;
- confirm strong identity;
- confirm automatic reacquire/reclaim when safe.

### Validation F — manual Center M launch while Disabled

- while Addon owns PID1902, launch MSI Center M from Start menu;
- verify MainUI prevention blocks it if possible;
- if processes still start, verify targeted quiesce/recovery;
- PID1902 must remain or be reclaimed;
- controller remains usable.

### Validation G — suspend / hibernate / resume

Test from both Xbox360 and Steam Deck presentations:

- suspend;
- resume;
- physical controller returns in PID1902 or is reclaimed;
- DirectInput is reacquired as necessary;
- HidHide state is correct;
- one correct virtual presentation becomes live;
- no stale buttons/rumble;
- no controller loss.

### Validation H — crash recovery

- terminate Addon abnormally during owned PID1902 state;
- restart;
- verify recovery converges to a safe baseline;
- verify new ownership can be established again when Center M remains Disabled.

### Validation I — normal exit

- exit Addon normally while Center M Disabled;
- verify virtual controller retires cleanly;
- verify physical controller returns to PID1901 and is visible/usable;
- verify no stale HidHide or VIIPER ownership remains.

---

## 36. Recommended staged PR roadmap

Keep POCs small enough to review and reason about. Prefer <500 LOC per PR when practical, but lifecycle correctness is more important than an arbitrary line limit.

### PR1 — Center M startup control POC

Scope:

- Device-page Center M card;
- read startup state from the two tasks + Foundation Service;
- Enable/Disable startup configuration;
- restart-required UX;
- no controller takeover yet.

Do not treat the POC's exact implementation as a final controller architecture contract.

### PR2 — Addon controller ownership startup baseline

Goal:

```text
Center M Enabled → controller passive
Center M Disabled + runtime quiescent → Addon-owned PID1902 baseline
```

Implement only the startup baseline:

- authoritative admission from Center M state;
- Center M runtime quiescence check;
- persistent VIIPER runtime;
- create both Xbox360 and Steam Deck logical devices detached;
- PID1902 acquisition;
- DirectInput acquisition;
- HidHide isolation;
- attach/live Xbox360 default;
- Steam Deck waiting/detached;
- normal shutdown restores stock PID1901.

No automatic Steam presentation switching yet.

### PR3 — owned-state recovery

Implement:

- PID1902 input loss detection routed to new ownership reconcile policy;
- same-device PID1901 classified as recoverable owned-state drift;
- targeted Center M runtime quiesce/kill;
- PID1902 reclaim;
- DirectInput reacquire;
- HidHide reconcile;
- full device missing → neutral + wait for PnP arrival;
- PnP arrival recovery;
- resume reconciliation.

Remove/disable obsolete `ExternalNativeTakeover → yield Steam session` behavior for Addon-owned mode.

Do not preserve dead Steam-session semantics solely for compatibility.

### PR4 — manual presentation switching POC

Developer-only/manual action:

```text
Xbox360 ↔ SteamDeck
```

Requirements:

- no PID change;
- no DirectInput reacquire;
- no HidHide change;
- VIIPER runtime remains alive;
- exactly one logical device attached;
- neutral ordering proven;
- repeated switching test coverage.

### PR5 — automatic presentation selector

Policy:

```text
Steam game / BPM active → SteamDeck
otherwise → Xbox360
```

Steam becomes presentation policy only.

### PR6+ — cleanup and simplification

After the new architecture is hardware-proven:

- delete obsolete Steam-session native-mode ownership paths;
- delete obsolete external-takeover yield semantics;
- delete route-bound Center M ownership logic replaced by controller-lifetime ownership;
- remove dormant/dead coordinator/state contracts no longer used;
- rename remaining runtime types to reflect controller-platform semantics if useful;
- update docs/tests to the new one-owner model.

Do not keep duplicate old/new authorities after migration.

---

## 37. Tests that should exist before product completion

### Top-level authority tests

```text
CenterM Enabled
→ no native mutation
→ no physical isolation
→ no virtual controller attach

CenterM Disabled + runtime quiescent
→ Addon ownership admitted

CenterM Disabled + old runtime still alive / restart pending
→ ownership not admitted yet
```

### VIIPER tests

- one server/bus;
- both logical devices created once;
- both initially detached;
- attach X360 only;
- switch X360 → Deck with neutral ordering;
- switch Deck → X360 with neutral ordering;
- never both attached in stable state;
- teardown retires both safely.

### Native drift tests

```text
Owned PID1902 + same physical PID1901
→ reclaim requested
→ no Steam-session yield policy
```

### Device disappearance tests

```text
Owned PID1902 disappears
→ virtual neutral
→ ownership intent retained

same physical PID1902 returns
→ reacquire

same physical PID1901 returns
→ reclaim PID1902

different/ambiguous device returns
→ no mutation
```

### Center M resurrection tests

- disabled roots + conflicting known process → targeted quiesce before reclaim;
- known process exit failure → do not repeatedly mode-fight indefinitely;
- startup roots unexpectedly changed from Disabled → surface policy drift and fail safely.

### HidHide tests

- owned entry missing → repair;
- unsafe foreign/inverse state → fail closed;
- physical collection identity changed after PnP → old identity not blindly reused.

### Shutdown tests

- virtual neutral/detach before final teardown;
- physical input stops;
- HidHide ownership releases;
- PID1901 restored and verified;
- recovery ownership cleared only after successful cleanup.

### Power tests

- suspend while Xbox360 selected;
- suspend while Deck selected;
- resume with PID1902 still present;
- resume with same-device PID1901;
- resume with device temporarily absent.

---

## 38. Logging / diagnostics requirements

The new architecture should make controller ownership understandable from logs without requiring a debugger.

### Authority

```text
ControllerAuthorityEvaluated
ControllerOwnershipAdmitted
ControllerOwnershipPassiveCenterMEnabled
ControllerOwnershipBlockedRestartRequired
```

### Physical native mode

```text
OwnedPhysicalModeHealthy
OwnedPhysicalModeDriftedToXInput
OwnedPhysicalDeviceMissing
OwnedPhysicalDeviceReturned
OwnedPhysicalModeReclaimStarted
OwnedPhysicalModeReclaimSucceeded
OwnedPhysicalModeReclaimFailed
```

### Center M

```text
CenterMRuntimeDetectedWhileDisabled
CenterMRuntimeQuiesceStarted
CenterMRuntimeQuiesceSucceeded
CenterMRuntimeQuiesceFailed
CenterMStartupPolicyDrifted
```

### Input

```text
PhysicalInputLost
PhysicalInputReacquireStarted
PhysicalInputReacquireSucceeded
PhysicalInputReacquireFailed
```

### HidHide

```text
PhysicalIsolationHealthy
PhysicalIsolationRepaired
PhysicalIsolationUnsafe
```

### Presentation

```text
PresentationRequested=Xbox360|SteamDeck
PresentationSourceNeutral
PresentationSourceDetached
PresentationDestinationAttached
PresentationLive
PresentationRecoveryNeutral
```

For manual POC measurements, log timestamps around:

```text
switch requested
source neutral accepted
source detached
destination attached
destination first live input
```

This allows measurement of actual controller blackout.

---

## 39. UI semantics

### Device page — MSI Center M card

Conceptually:

```text
MSI Center M

Enabled
  MSI Center M owns the stock controller.
  Steam Addon controller ownership is inactive.

Disabled
  Steam Addon owns the MSI Claw controller while the app is running.
  Restart Windows after changing this setting.
```

Avoid wording that suggests Center M Disabled merely suppresses one process while Steam routing still behaves as before.

It changes the controller ownership model.

### Potential future controller status

When Disabled and owned:

```text
Controller owner: Steam Addon
Physical mode: DirectInput (PID1902)
Presentation: Xbox 360 / Steam Deck
```

When Enabled:

```text
Controller owner: MSI / Stock
```

---

## 40. Relation to Center M replacement features

This architecture is the foundation for moving more Center M hardware features into the Addon.

The Device page can evolve into the MSI Claw hardware-control surface for features such as:

- vibration strength;
- fan control / fan curve;
- additional controller hardware settings;
- TDP;
- CPU Boost;
- Windows power mode;
- other supported board-specific hardware controls.

This reinforces the product meaning:

```text
Center M Enabled
→ use MSI's control center / stock controller stack

Center M Disabled
→ Steam Addon becomes the Claw control/controller platform
```

Physical motor-strength settings belong to device/hardware authority; virtual X360/SteamDeck rumble transport belongs to the controller/presentation path. Keep those responsibilities distinct even though both involve vibration.

---

## 41. Open implementation questions requiring hardware evidence

### 41.1 Exact Center M kill set

Starting point is the HHC process set.

Need hardware evidence for whether killing `MSI_Center_M_Server` reliably retires controller-related children such as `MSI_Center_M_Server_ControlMode`.

### 41.2 MainUI guard sufficiency

Need to test manual Start-menu launch while Addon owns PID1902.

If the existing same-name guard prevents operational MainUI before native-mode mutation, process-kill frequency can remain low.

If not, targeted runtime termination becomes a stronger required path.

### 41.3 PID1901 transient double-input

Need to observe whether the brief PID1901 interval during forced takeover causes actual user-visible double input or controller slot churn while the virtual controller remains attached.

Do not add a second PID1901 hiding scheme without evidence.

### 41.4 X360 physical rumble

Revalidate the complete rumble path with the new default Xbox360 presentation.

The architecture must eventually support physical vibration-strength control independent of whether X360 or Steam Deck is selected.

### 41.5 Resume native mode

Measure whether supported Claw firmware tends to preserve PID1902 through sleep/hibernate or reappears as PID1901. The reconcile policy supports either outcome; implementation should optimize only after evidence.

---

## 42. Design rules for future PR reviews

Review future controller PRs against the new product contract, not historical architecture.

Ask:

1. Does this code preserve one clear physical controller owner?
2. Does Center M Enabled keep the controller stock/passive?
3. Does Center M Disabled converge toward PID1902 ownership?
4. Can a real Center M resurrection be removed and the controller reclaimed?
5. Can real PnP loss/re-enumeration recover without losing the controller permanently?
6. Does virtual presentation switching avoid PID/HidHide/DirectInput churn?
7. Is exactly one virtual presentation active?
8. Does shutdown/recovery leave a usable physical controller?
9. Is ambiguous identity handled fail-closed?
10. Is the implementation introducing extra authority/state only for theoretical races?
11. Is old Steam-session orchestration being retained because it is still useful, or only because it already exists?
12. Does the change advance the Center M replacement + Steam integration direction rather than preserve obsolete coexistence behavior?

A PR should not be blocked for theoretical scheduler interleavings that do not map to realistic handheld lifecycle behavior.

A PR **should** be blocked for realistic failures such as:

- PID1901 takeover leaves Addon controller permanently dead;
- Center M remains alive and repeatedly fights for native mode;
- physical device re-enumeration cannot rebind input;
- HidHide leaves the user's controller inaccessible;
- shutdown leaves PID1902 hidden after Addon exits;
- both virtual devices become attached;
- stale virtual input remains live after physical loss;
- resume cannot converge to a usable state;
- a different/ambiguous physical device can receive native-mode mutation.

---

## 43. Final target architecture

```text
                        MSI CENTER M SETTING
                               │
                ┌──────────────┴──────────────┐
                │                             │
             ENABLED                       DISABLED
                │                             │
         STOCK / MSI OWNER              ADDON OWNER
                │                             │
    no PID/HidHide/VIIPER takeover      physical PID1902
                │                       DirectInput acquired
      Addon Device features             HidHide isolation
       may still operate                Center M suppressed
                                              │
                                      one VIIPER runtime
                                              │
                                  ┌───────────┴───────────┐
                                  │                       │
                                Xbox360                SteamDeck
                              default/non-Steam       Steam/BPM
                                  │                       │
                                  └───────────┬───────────┘
                                              │
                                  exactly one attached/live
```

Runtime drift recovery:

```text
Owned PID1902
    │
    ├─ healthy ───────────────────────────────→ continue
    │
    ├─ same device becomes PID1901
    │      → neutral virtual output
    │      → quiesce Center M runtime
    │      → reclaim PID1902
    │      → reacquire DirectInput
    │      → repair HidHide
    │      → resume selected presentation
    │
    └─ device disappears
           → neutral virtual output
           → wait for PnP arrival
           → verify same strong physical identity
           → reacquire/reclaim as necessary
```

Normal application exit:

```text
Addon-owned PID1902
→ neutral/detach virtual output
→ stop physical input
→ release HidHide
→ restore PID1901
→ disarm Center M prevention
→ teardown VIIPER
→ physical controller remains usable without Addon
```

---

## 44. Final design principles

1. **The product direction is MSI Center M replacement for controller/core Device Control responsibilities plus first-class Steam integration.**
2. **Center M Enabled/Disabled is the physical controller authority decision.**
3. **Steam/BPM is only a virtual presentation selector once Addon ownership exists.**
4. **PID1902 is persistent for the entire Addon-owned runtime.**
5. **DirectInput and HidHide are persistent physical ownership infrastructure, not Steam-session resources.**
6. **Xbox360 is the default presentation; Steam Deck is the Steam/BPM presentation.**
7. **Both virtual logical devices may be created once, but exactly one is attached/live.**
8. **PID1901 during Addon ownership is recoverable drift, not a reason to yield.**
9. **A resurrected Center M runtime may be terminated because Disabled means the user chose Addon ownership.**
10. **Do not fight a live competitor endlessly: quiesce it first, then reclaim PID1902.**
11. **Physical device disappearance is recoverable; neutralize output and rebind on PnP return.**
12. **Strong physical identity gates native-mode mutation.**
13. **Ambiguous ownership fails closed.**
14. **Normal Addon shutdown restores a usable stock physical controller.**
15. **Crash recovery proves safety before starting a new ownership session.**
16. **Reuse proven low-level primitives, not obsolete policy semantics.**
17. **Do not preserve old Steam-routing orchestration merely because it exists.**
18. **Prefer one clear owner, one reconcile path, and one teardown path over layered managers.**
19. **Protect real handheld lifecycle failures without building machinery for purely theoretical races.**
20. **Controller and Device features should progressively move under Addon authority as they are implemented and hardware-validated, rather than retaining Center M merely to provide those functions.**

This is the intended direction for the Full PID1902 implementation track.