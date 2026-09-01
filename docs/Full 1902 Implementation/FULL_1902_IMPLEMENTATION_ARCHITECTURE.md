# Full PID1902 Implementation — Controller Ownership Architecture

> **Status:** Design / implementation planning document  
> **Scope:** Future architecture for MSI Claw controller ownership and MSI Center M replacement  
> **Implementation state:** This document does **not** claim that every described path is already implemented or hardware-validated.  
> **Important:** The application is pre-release. Existing Steam-session routing behavior is not a compatibility contract and must not constrain the new product direction.

---

## Project direction

Steam Addon for Claw is becoming an integrated MSI Claw control platform, not a small Steam-routing helper that temporarily borrows the physical controller from MSI Center M.

The Addon should progressively replace the relevant MSI Center M responsibilities, including:

- normal Xbox 360 controller presentation outside Steam;
- controller physical-mode and input ownership;
- HidHide isolation and virtual-controller presentation;
- TDP;
- CPU Boost;
- Windows Power Mode;
- fan/fan-curve control when implemented;
- vibration-strength/device settings when implemented;
- other supported board-specific MSI Claw device controls.

The product identity is:

> **MSI Center M replacement for controller and core Device Control responsibilities, plus first-class Steam integration.**

---

## 1. Core authority contract

There are exactly two controller-authority modes.

### MSI Center M Enabled

```text
Controller authority = MSI / Stock
Desired physical PID = PID1901
Addon physical controller ownership = none
Addon DirectInput ownership = none
Addon physical HidHide ownership = none
Addon VIIPER controller presentation = none
```

Independent Addon device/utility features may continue where supported.

### MSI Center M Disabled

```text
Controller authority = Addon Runtime
Desired physical PID = PID1902
Addon Runtime = mandatory
DirectInput = Addon-owned
HidHide controller isolation = persistent Addon baseline
VIIPER Runtime = Addon-owned
Exactly one virtual presentation = attached/live
```

The key distinction is:

```text
Physical authority
    Center M Enabled  → MSI / Stock
    Center M Disabled → Addon Runtime

Virtual presentation while Addon owns the controller
    Steam/BPM inactive → Xbox360
    Steam/BPM active   → SteamDeck
```

Steam no longer decides whether the Addon owns the physical controller.

Steam/BPM only decides which virtual presentation is exposed.

---

## 2. Center M Disabled is a durable authority mode

The user's choice to Disable Center M means:

> **The Addon is responsible for the controller until the user explicitly returns authority to MSI.**

This is stronger than:

```text
Addon process happens to be running
→ PID1902
Addon process exits
→ stock
```

The intended contract is:

```text
Center M Disabled
→ Addon Runtime is required
→ PID1902 remains the desired physical state
→ HidHide remains configured
→ Windows shutdown/restart does not release authority
→ controlled Addon Runtime restart does not release authority
→ sleep/hibernate does not release authority
→ only an explicit authority-release action returns to PID1901
```

The primary supported authority-release action is:

```text
Enable Center M and Restart
```

Uninstall/explicit stock restoration is another exceptional release path.

---

## 3. Frontend lifetime and Runtime lifetime are different

The UI/frontend is not the controller owner.

```text
Frontend closes
→ Addon controller Runtime continues

QAM closes
→ Addon controller Runtime continues

No UI window is visible
→ Addon controller Runtime continues
```

While Center M is Disabled, the product must not expose a normal user action that intentionally stops the controller Runtime and leaves Windows running.

Unsupported normal state:

```text
Center M Disabled
+ Windows running
+ Addon controller Runtime intentionally absent
```

If the user wants to stop Addon controller ownership, the supported workflow is:

```text
Enable Center M and Restart
```

---

## 4. Mandatory Runtime startup while Disabled

The existing background Runtime startup registration becomes a required invariant of Disabled mode.

```text
Center M Enabled
→ startup may remain a normal preference where product UI allows it

Center M Disabled
→ Addon background Runtime startup MUST be enabled
→ user must not be able to disable the required startup path
```

The Disable transition must verify next-logon Runtime startup before it disables the Center M startup roots.

### Start simple

The final product should automatically recover if the Runtime dies unexpectedly.

However, initial Full PID1902 POCs should not immediately add:

- a Windows service;
- a supervisor executable;
- a heartbeat protocol;
- a watchdog manager;
- restart epochs/barriers;
- a generalized process-lifetime state machine.

Initial implementation should first guarantee:

```text
mandatory existing background startup task
+ no intentional Runtime quit while Disabled
+ restart/reconcile logic that is safe to invoke repeatedly
```

Automatic crash restart/keepalive can follow as a focused hardening PR after the basic controller path is hardware-proven.

Unexpected Runtime death remains a real product reliability requirement; it is not dismissed as theoretical.

---

## 5. Authority changes are reboot-bound

Intentional authority changes use Windows restart as a hard boundary.

### Disable Center M

```text
User selects Disable
→ [Cancel] or [Disable and Restart]
→ ensure/verify mandatory Addon Runtime startup
→ apply/verify persistent Addon HidHide baseline
→ disable/verify Center M startup roots
→ reboot
```

Do **not** perform live PID1901 → PID1902 controller takeover in the current MSI-owned session.

### Enable Center M

```text
User selects Enable
→ [Cancel] or [Enable and Restart]
→ retire active virtual presentation
→ release DirectInput
→ restore same physical MSI Claw to PID1901
→ verify stock mode
→ teardown VIIPER
→ remove Addon controller HidHide baseline
→ enable/verify Center M startup roots
→ reboot
```

There is no `Restart Later` mode.

Do not build a same-session MSI ↔ Addon handoff merely because other applications support one.

---

## 6. Center M startup roots define authority intent

Preferred source of truth:

```text
MSI_Center_M_Server task       Enabled
MSI_Center_M_Updater task      Enabled
MSI Foundation Service         Automatic
→ Center M Enabled / MSI authority

MSI_Center_M_Server task       Disabled
MSI_Center_M_Updater task      Disabled
MSI Foundation Service         Disabled
→ Center M Disabled / Addon authority
```

Mixed/Partial configuration is invalid and should not silently choose an owner.

Do not add a duplicate persisted boolean such as:

```text
AddonControllerModeEnabled=true
```

unless real implementation evidence proves the actual system configuration is insufficient.

Mandatory Addon Runtime startup is a required Disabled-mode invariant, not a second authority source.

---

## 7. Persistent PID1902 desired state

While Center M is Disabled:

```text
DesiredPhysicalPID = PID1902
```

Startup/recovery logic evaluates current reality and converges toward that state.

### Current physical controller is PID1902

```text
verify supported same MSI Claw
→ keep PID1902
→ do not force 1902 → 1901 → 1902
→ acquire/reacquire DirectInput
→ reconcile HidHide
```

### Current physical controller is PID1901

```text
verify supported same MSI Claw
→ switch 1901 → 1902
→ bounded PnP stabilization
→ verify same physical identity
→ acquire DirectInput
→ reconcile HidHide
```

### Physical device is temporarily missing

```text
retain desired Addon ownership
neutral virtual output if already attached
wait for relevant PnP arrival
```

Do not repeatedly issue mode commands against an absent device.

Do not mutate a different or ambiguously identified device.

---

## 8. Windows shutdown/restart is not authority release

The old fail-safe policy of deliberately restoring PID1901 for every clean process/Windows shutdown is no longer the desired architecture.

While Center M remains Disabled:

```text
Windows shutdown/restart begins
→ neutral/retire process-owned virtual output
→ stop publishers
→ release process-owned DirectInput/native handles as required
→ keep persistent HidHide Disabled-mode baseline
→ DO NOT issue PID1902 → PID1901 solely because Windows is shutting down/restarting
→ Windows exits
```

After next logon:

```text
mandatory Addon Runtime starts
→ inspect actual physical PID
→ PID1902: keep
→ PID1901: switch to PID1902
```

This removes unnecessary PnP/native-mode churn at every restart.

The architecture does not require firmware to preserve PID1902 across reboot. It only requires desired-state reconciliation.

---

## 9. Controlled Runtime restart while Disabled

An update/relaunch of the Addon Runtime does not release controller authority.

```text
controlled Runtime restart
→ neutral/retire process-owned virtual resources safely
→ do not intentionally restore PID1901 merely for restart
→ leave persistent HidHide baseline
→ new Runtime starts
→ inspect/reconcile actual PID to 1902
→ rebuild DirectInput + VIIPER presentation
```

This is distinct from `Enable Center M and Restart`.

---

## 10. Unexpected Runtime death

A crash while Addon-owned may leave:

```text
physical PID1902 hidden by HidHide
VIIPER presentation gone
```

That is a real failure because the user may temporarily lose the controller.

The product must eventually restart/recover the Runtime automatically.

Initial implementation should remain simple:

- mandatory logon startup task;
- intentional Runtime stop disallowed while Disabled;
- startup reconciliation must work from either PID1901 or PID1902;
- persistent HidHide must not be mistaken for stale route state;
- no mandatory supervisor/service in the first POCs.

A later focused hardening PR can add the smallest reliable automatic restart mechanism supported by real product testing.

After Runtime restart:

```text
Center M Disabled
→ current PID1902: reacquire
→ current PID1901: reclaim PID1902
→ reconcile HidHide
→ create fresh canonical VIIPER Runtime
→ attach current desired X360/Deck presentation
```

Do not require a stock PID1901 round-trip first unless hardware evidence proves one is necessary.

---

## 11. HidHide ownership

HidHide is persistent controller infrastructure in Disabled mode, not a Steam-session lease.

Desired baseline:

```text
HidHide readable/configurable
Inverse whitelist = false
Active = true
Addon executable = whitelisted
Exact Addon-owned PID1902 primary gamepad collection = hidden when known
```

The baseline persists across:

- Runtime restart;
- Windows shutdown/restart;
- crash recovery;
- normal logoff/logon.

It is removed on explicit authority release.

### Exact-target rule

Hide the exact physical gamepad collection, not the entire PID1902 device tree.

Do not invent broad VID/PID wildcard hiding if the exact target is not yet known.

### First Disabled boot

If the exact PID1902 target is not yet known:

```text
HidHide Active + Addon whitelist prepared
VIIPER devices remain detached
→ acquire/reconcile PID1902
→ resolve exact physical gamepad collection
→ add/verify exact HidHide target
→ only then attach virtual presentation
```

This prevents physical + virtual double input.

---

## 12. Exclusive controller environment

Center M Disabled / Addon Controller Mode is not intended to coexist with HHC, ClawTweaks, or another controller middleware.

Before committing Disabled mode:

```text
known conflicting controller manager / unsafe foreign HidHide state
→ refuse admission
→ ask user to disable/remove the conflicting stack
```

After admission, do not continuously reason about shared ownership.

Do not create a generalized HidHide multi-owner manager or controller-authority arbitration framework.

---

## 13. Canonical VIIPER runtime

One Addon controller Runtime owns:

```text
one VIIPER server
one caller-owned bus
one persistent Xbox360 logical device
one persistent SteamDeck logical device
```

Both devices are created detached.

Stable presentation invariant:

```text
Attached(Xbox360) XOR Attached(SteamDeck) == true
```

Short controlled transitions may have both detached.

Never intentionally attach both.

---

## 14. Presentation policy

Normal/default:

```text
Steam/BPM inactive
→ Xbox360 attached/live
→ SteamDeck detached
```

Steam/BPM:

```text
Steam game active OR BPM active
→ Xbox360 detached
→ SteamDeck attached/live
```

Steam/BPM never changes:

- PID1902 ownership;
- DirectInput ownership;
- HidHide physical isolation;
- Center M authority;
- VIIPER server/bus ownership.

### First presentation after startup

Do not hard-code X360 at boot.

Immediately before first attach:

```text
read freshest Steam/BPM state
→ choose X360 or SteamDeck
```

This handles Steam starting directly into BPM without an unnecessary X360 attach/switch cycle.

---

## 15. Presentation switching

X360 → SteamDeck:

```text
X360 neutral
→ stop/join X360 publisher
→ detach X360
→ attach SteamDeck
→ SteamDeck neutral
→ start SteamDeck publisher
```

SteamDeck → X360 uses the same inverse pattern.

Throughout normal presentation switching:

```text
PID1902 unchanged
DirectInput unchanged
HidHide unchanged
VIIPER server/bus unchanged
```

Do not add another physical-ownership cycle around presentation changes.

---

## 16. Center M resurrection / PID drift

While Center M is Disabled, same-device PID1902 → PID1901 is:

```text
OwnedPhysicalStateDrift
```

not:

```text
ExternalAuthorityWon
```

Recovery:

```text
neutral active virtual output
→ stop stale physical-input path
→ verify strong same-device identity
→ verify Center M remains Disabled
→ targeted quiesce of conflicting Center M runtime if present
→ switch same device back to PID1902
→ bounded PnP stabilization
→ reacquire DirectInput
→ reconcile HidHide
→ resume current desired presentation
```

Do not restore the old `ExternalNativeTakeover → yield until Steam session end` policy.

Prefer exact known MSI Center M process targeting over broad process killing.

---

## 17. Physical device disappearance

If neither PID1901 nor PID1902 of the owned physical MSI Claw is present after the normal settle window:

```text
DesiredAuthority = Addon
ActualPhysicalState = Missing
VirtualPresentation = neutral if VIIPER itself remains healthy
```

Wait for PnP arrival.

On arrival:

```text
same strong physical identity + PID1902
→ reacquire DirectInput / HidHide

same strong physical identity + PID1901
→ reclaim PID1902 / reacquire / HidHide

different or ambiguous identity
→ do not mutate
→ fail closed
```

Do not publish stale input while physical source is unavailable.

---

## 18. Sleep / hibernate / resume

Suspend does not release Disabled-mode authority.

```text
before suspend
→ neutral output
→ quiesce process-owned input as required
→ do not intentionally restore PID1901 merely for sleep
```

After resume:

```text
Center M still Disabled
→ desired PID = 1902
→ PID1902: reacquire/reconcile
→ PID1901: reclaim 1902
→ missing: wait for PnP
→ ambiguous: fail closed
→ choose current virtual presentation from current Steam/BPM fact
```

Do not assume old device handles survive power transitions.

---

## 19. One reconciliation path

Important lifecycle triggers should converge toward one top-level controller-owner reconcile operation, conceptually:

```text
ReconcileOwnedControllerAsync(trigger)
```

Possible real triggers:

- startup;
- controlled Runtime restart;
- resume;
- DirectInput failure;
- PnP removal/arrival;
- PID1901 drift;
- Center M runtime resurrection;
- crash recovery.

The reconcile path evaluates current facts:

```text
Center M Disabled?
Supported MSI Claw?
Strong physical identity?
Current PID = 1902 / 1901 / missing?
DirectInput healthy?
HidHide healthy?
VIIPER healthy?
Desired presentation?
```

This does not justify a generalized declarative state-machine framework.

Avoid parallel authority managers such as:

- `ControllerAuthorityManager`;
- `Pid1902Watchdog`;
- `NativeTakeoverRecoveryManager`;
- `PresentationRecoveryManager`.

One top-level owner should decide desired state.

---

## 20. Explicit authority release

### Enable Center M and Restart

This is the primary normal release path.

```text
neutral/detach active virtual controller
→ release DirectInput
→ restore same MSI Claw to PID1901
→ verify stock mode
→ teardown VIIPER
→ remove Addon controller HidHide baseline
→ enable Center M startup roots
→ release mandatory Addon-startup constraint as appropriate
→ reboot immediately
```

### Uninstall / forced stock cleanup

If the product is removed while Disabled, uninstall must restore a usable stock environment before the mandatory Runtime disappears.

Do not uninstall into:

```text
PID1902 hidden
+ no Addon Runtime
```

---

## 21. Components worth reusing

### Native / physical identity

- MSI native-state capture;
- strong physical identity resolution;
- bounded PnP stabilization;
- native mode switch/readback primitives.

### Physical input

- exact DirectInput selection;
- acquire/first-valid-state verification;
- current input polling/fault signal.

### HidHide

- control-device client;
- exact collection targeting;
- path normalization;
- readback verification;
- persistent baseline primitive from PR2.

### VIIPER

- canonical typed API;
- one server / one bus;
- persistent X360 + SteamDeck devices;
- typed attach/detach;
- neutral state;
- publishers;
- verified teardown.

### Steam/BPM

- RunningAppID event observation;
- BPM event observation;
- effective session fact source.

### Center M

- exact startup-root control;
- proven MainUI/controller-process suppression primitives where useful.

Reuse low-level safety primitives, not obsolete route policy.

---

## 22. Existing policy likely to be replaced

Candidates for deletion/rewrite after new ownership is proven:

```text
Steam session starts physical ownership
Steam session ends physical ownership
route end restores PID1901
route-scoped HidHide lease/rollback
startup removes all Addon HidHide as stale
ExternalNativeTakeover → yield current Steam session
route-bound Center M ownership lifetime
Game Bar X360 presentation nested inside outer Steam route
normal Disabled-mode Addon exit → stock controller
```

The project is unreleased. Do not keep duplicate old/new authorities for compatibility.

---

## 23. Recommended conceptual owner

A future top-level owner may conceptually be named:

```text
MsiClawControllerRuntime
```

Exact naming is not mandated.

Its responsibility is one MSI Claw controller stack:

```text
physical desired mode
DirectInput
HidHide isolation
VIIPER Runtime
current presentation
reconcile
explicit authority release
```

This does not mean every low-level primitive belongs in one huge class.

It means desired controller state has one authority.

---

## 24. Small-PR roadmap

Prefer small, independently reviewable PRs. Roughly 100–400 LOC per PR is useful where practical, but not a hard correctness limit.

Current sequence:

```text
PR1  Persistent dual VIIPER devices                     [done]
  ↓
PR2  Persistent Addon-owned HidHide baseline
  ↓
PR3  Mandatory Runtime / startup contract
  ↓
PR4  Reboot-bound Center M authority transition
  ↓
PR5  Disabled-boot admission
  ↓
PR6  PID1902 + DirectInput ownership
  ↓
PR7  First presentation attach
  ↓
PR8  Runtime X360 ↔ SteamDeck switching
  ↓
PR9+ Owned-state recovery / keepalive hardening / cleanup
```

### PR2 — HidHide baseline

Foundation only.

No PID/Center M/reboot/Runtime-lifetime/VIIPER presentation work.

### PR3 — Mandatory Runtime/startup contract

Small product-lifetime foundation:

- existing background Runtime startup can be forced/verified;
- Disabled mode can require it;
- frontend lifetime remains independent;
- user cannot intentionally turn off required Runtime/startup once wiring reaches Disabled mode.

No Windows service or supervisor yet.

No PID1902 work.

### PR4 — reboot-bound authority transition

Compose:

```text
persistent HidHide baseline
mandatory Runtime startup
existing Center M startup control
Disable and Restart / Enable and Restart UX
```

No live PID takeover in Disable flow.

### PR5 — Disabled-boot admission

Facts/gate only. No physical mutation.

### PR6 — PID1902 + DirectInput

```text
current 1902 → keep
current 1901 → switch
PnP settle
DirectInput
exact HidHide target
physical isolation verify
```

Both virtual devices remain detached.

### PR7 — first presentation attach

Fresh Steam/BPM state immediately before attach.

### PR8 — runtime presentation switching

X360 ↔ SteamDeck with no PID/HidHide/DirectInput churn.

### PR9+ — real lifecycle hardening

Split by actual need:

- unexpected Runtime death auto-restart/keepalive;
- PID drift;
- device loss/re-arrival;
- DirectInput loss;
- HidHide drift;
- Center M resurrection;
- suspend/resume;
- legacy routing cleanup.

Do not pre-build all recovery machinery into the first ownership PR.

---

## 25. Hardware validation priorities

### Disabled boot

Validate both current states:

```text
boot/current PID1902 → no 1901 round-trip
boot/current PID1901 → switch to PID1902
```

Then verify:

- DirectInput acquisition;
- exact HidHide isolation;
- no physical + virtual double input;
- correct initial X360/Deck presentation.

### Windows restart while Disabled

Verify:

- no intentional PID1901 command solely for shutdown/restart;
- persistent HidHide baseline survives;
- mandatory Runtime starts again;
- current physical PID is reconciled to 1902;
- controller becomes usable again.

### Presentation switching

Repeated X360 ↔ SteamDeck switching must not change:

- PID1902;
- DirectInput session;
- HidHide state;
- VIIPER server/bus.

### Power lifecycle

Validate suspend/resume and hibernate/resume from both presentations.

### Fault lifecycle

Validate:

- PID1902 → PID1901 drift;
- physical PnP loss/re-arrival;
- DirectInput failure;
- Center M resurrection;
- unexpected Runtime process death/restart.

### Enable restoration

Verify:

- PID1901 restored;
- Addon HidHide baseline removed;
- Center M startup roots restored;
- stock controller works after reboot.

---

## 26. Realistic failure policy

Automatic recovery is appropriate when:

```text
Center M Disabled
same physical MSI Claw strongly identified
current state is a known owned-state drift or temporary absence
```

Examples:

- same-device PID1901 drift;
- DirectInput loss caused by re-enumeration;
- same-device PID1902 return;
- missing Addon HidHide target;
- temporary physical disappearance.

Fail closed when:

- physical identity is ambiguous;
- returning device is not proven to be the same MSI Claw;
- HidHide state is unsafe/unreadable;
- VIIPER ownership is ambiguous;
- mode mutation cannot establish a known result.

Fail closed means neutral user-visible virtual input, not stale input publication.

Do not add synchronization machinery solely for artificial instruction-level races.

---

## 27. Review rules

Review controller PRs against this product contract:

1. Is there one physical controller authority?
2. Does Center M Enabled keep the controller stock/passive?
3. Does Center M Disabled make the Addon Runtime mandatory?
4. Does Disabled mode converge toward PID1902 without needless 1901 round-trips?
5. Does Windows shutdown/restart avoid releasing authority unnecessarily?
6. Can current PID1902 simply be retained?
7. Does virtual presentation switching avoid physical ownership churn?
8. Is exactly one virtual presentation active?
9. Are HidHide and VIIPER ordering safe against double input?
10. Can real PnP/resume/PID-drift failures converge?
11. Does explicit Enable restore PID1901 and MSI authority cleanly?
12. Is ambiguous physical identity fail-closed?
13. Is complexity protecting a real lifecycle or only a theoretical race?
14. Is obsolete Steam-route policy being preserved only because it exists?

Blocking examples include:

- Disabled mode can intentionally stop the only controller Runtime;
- mandatory Runtime startup is not guaranteed before disabling Center M;
- restart unnecessarily forces PID1901;
- startup round-trips an already-correct PID1902 through PID1901;
- virtual output appears before physical isolation;
- Enable leaves PID1902/HidHide ownership behind;
- stale input remains live after physical loss;
- a different/ambiguous device can receive native-mode mutation.

---

## 28. Final target architecture

```text
                         MSI CENTER M SETTING
                                │
                 ┌──────────────┴──────────────┐
                 │                             │
              ENABLED                       DISABLED
                 │                             │
          MSI / STOCK OWNER              ADDON RUNTIME OWNER
                 │                             │
              PID1901                     PID1902 desired
                                               │
                                            HidHide
                                               │
                                      one VIIPER Runtime
                                               │
                                   ┌───────────┴───────────┐
                                   │                       │
                                 Xbox360                SteamDeck
                               normal/default           Steam/BPM
                                   │                       │
                                   └───────────┬───────────┘
                                               │
                                   exactly one attached/live
```

Disabled-mode lifetime:

```text
Disable and Restart
        ↓
Addon Runtime mandatory
        ↓
PID1902 desired continuously
        ↓
Windows restart / sleep / controlled Runtime restart
        ↓
reconcile current physical state back to desired PID1902
        ↓
...
        ↓
Enable Center M and Restart
        ↓
PID1901 restored
HidHide controller ownership removed
MSI authority restored
```

---

## 29. Final design principles

1. **The Addon is becoming the MSI Center M replacement for controller/core Device Control responsibilities.**
2. **Center M Enabled/Disabled decides physical controller authority.**
3. **Center M Disabled means the background Addon Runtime is mandatory until explicit authority release.**
4. **The frontend may close independently from the controller Runtime.**
5. **PID1902 is the desired physical state for the entire Disabled-mode lifetime.**
6. **Windows shutdown/restart is not an authority-release boundary.**
7. **Do not deliberately restore PID1901 merely to reboot and then immediately return to PID1902.**
8. **At startup, keep PID1902 if already present; switch PID1901 only when necessary.**
9. **DirectInput and HidHide are persistent physical-ownership infrastructure, not Steam-session resources.**
10. **HidHide configuration persists across restart while Disabled.**
11. **Xbox360 is normal/default presentation; SteamDeck is Steam/BPM presentation.**
12. **Steam/BPM changes presentation only, never physical authority.**
13. **Exactly one virtual logical device is attached/live at a stable point.**
14. **PID1901 during Disabled runtime is recoverable owned-state drift, not a reason to yield.**
15. **Physical disappearance is recoverable; neutralize output and rebind on safe PnP return.**
16. **Strong physical identity gates native-mode mutation.**
17. **Ambiguous ownership fails closed.**
18. **Explicit Enable Center M / uninstall is the normal stock-restoration boundary.**
19. **Unexpected Runtime death needs real recovery, but do not overbuild a service/supervisor before the basic path is proven.**
20. **Prefer one owner, one desired physical state, one presentation owner, one reconcile path, and simple focused PRs.**
