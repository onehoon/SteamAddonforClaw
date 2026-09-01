# Reboot-Bound Controller Authority and HidHide Design

> **Status:** Design / implementation planning document  
> **Scope:** MSI Center M Enabled/Disabled authority transitions, boot-time PID1902 acquisition, HidHide ownership, and initial virtual-controller presentation selection  
> **Product direction:** Full PID1902 implementation / MSI Center M replacement controller architecture  
> **Implementation state:** This document describes the intended design. It does not claim that the described transition flow is already implemented or hardware-validated.

---

## 1. Executive summary

The MSI Center M Enabled/Disabled setting should be treated as a **reboot-bound system mode**, not as a live controller-mode switch.

The core product contract is:

```text
MSI Center M Enabled
    → MSI / stock controller authority
    → physical controller returns to PID1901
    → Addon controller stack is passive

MSI Center M Disabled
    → Addon controller authority
    → physical controller is acquired as PID1902 after boot
    → DirectInput + HidHide + VIIPER are owned by the Addon
    → exactly one virtual controller presentation is attached
```

Changing between these modes requires an immediate Windows restart.

There is no supported `Restart Later` state.

The user-facing actions should therefore be:

```text
Disable MSI Center M
    → confirm "Disable and Restart"
    → configure the next-boot Addon-owned environment
    → reboot immediately

Enable MSI Center M
    → confirm "Enable and Restart"
    → restore the next-boot MSI-owned environment
    → reboot immediately
```

This hard reboot boundary deliberately removes the need to support a complicated live ownership handoff between Center M and the Addon in the same Windows session.

The design goal is:

> **Configuration changes happen before reboot. Controller authority changes happen after reboot.**

This gives the project a much simpler and safer lifecycle contract than trying to match HHC/ClawTweaks-style live enable/disable behavior.

---

## 2. Why authority changes should require reboot

A live transition from MSI ownership to Addon ownership creates many unnecessary transitional states:

- Center M startup configuration is Disabled but Center M processes are still alive;
- `MSI_Center_M_Server_ControlMode` may still hold or mutate controller state;
- physical mode may change while MSI components are still shutting down;
- PID1901 → PID1902 PnP re-enumeration may overlap Center M teardown;
- HidHide may be changing while the physical controller is being re-enumerated;
- a virtual controller could be exposed before physical isolation is complete;
- the Addon may need to distinguish between expected and conflicting native-mode changes;
- rollback becomes harder if only part of the transition succeeds.

These states are possible to support, but they do not provide enough product value to justify the extra controller authority and recovery complexity.

Instead, use Windows restart as the ownership boundary:

```text
Current Windows session
    → configure desired next-boot mode only
    → do not perform live controller takeover
    → reboot

Next Windows session
    → start directly in the selected authority model
```

This removes an entire category of transitional races without weakening real handheld lifecycle safety.

The project should still handle real runtime failures after ownership has been established, including:

- sleep / hibernate / resume;
- physical device loss;
- PnP re-enumeration;
- PID1902 → PID1901 drift while Addon-owned;
- DirectInput session failure;
- HidHide drift;
- Center M runtime resurrection;
- crash / restart / shutdown.

The reboot requirement is specifically for **intentional authority changes**, not for ordinary runtime recovery.

---

## 3. Controller authority is exclusive

When Center M is Disabled, the Addon is not a cooperative secondary controller manager.

It is the primary controller authority.

The intended ownership model is:

```text
Center M Disabled
    ↓
Addon owns controller policy
    ↓
PID1902
DirectInput
HidHide controller isolation
VIIPER runtime
Virtual controller presentation
```

The runtime should not continuously reason about whether another controller manager owns part of the stack.

That would recreate the old coexistence architecture and make the new product direction unnecessarily complicated.

### 3.1 Admission policy for other controller managers

Coexistence with HHC, ClawTweaks, or another controller middleware should not be supported in Addon Controller Mode.

Before the user is allowed to switch to Center M Disabled / Addon-owned mode, the transition may perform a simple preflight check for known conflicting controller software.

If a conflicting controller manager is active/configured:

```text
Do not enter Addon Controller Mode.
Do not attempt runtime coexistence.
Ask the user to disable/remove the conflicting controller manager first.
```

This is not a runtime ownership system.

It is only an admission gate.

Once Addon Controller Mode is active after reboot:

```text
Addon state is the desired controller state.
```

---

## 4. HidHide should be Addon-owned in Disabled mode

In the old Steam-session routing architecture, HidHide was a temporary resource borrowed during a routing session and rolled back at the end of that session.

That model no longer matches the product.

In the Full PID1902 architecture:

```text
Center M Disabled
→ HidHide controller isolation is part of the persistent Addon-owned controller configuration.
```

The lifetime of the HidHide configuration is therefore the lifetime of the **Center M Disabled product mode**, not the lifetime of one Steam session and not the lifetime of one Addon process.

Expected state while Center M is Disabled:

```text
HidHide Installed     = yes
Inverse mode          = off
HidHide Active        = on
Addon executable      = whitelisted
Addon-owned PID1902 primary gamepad collection = hidden
```

This state is allowed to persist across:

- Addon exit;
- Windows shutdown;
- Windows reboot;
- normal Addon restart.

The configuration is removed when the user explicitly switches back to Center M Enabled or uninstalls the Addon.

---

## 5. Prefer deterministic Addon HidHide baseline over coexistence

When Addon Controller Mode is active, the Addon should not carry complex runtime logic such as:

```text
Which hidden entry belongs to another app?
Which whitelist entry belongs to another app?
Who enabled HidHide Active?
Can we temporarily borrow Active?
Should we restore another controller manager's previous state?
```

That is the wrong authority model for this product direction.

Instead:

```text
Before entering Addon Controller Mode:
    reject unsupported/conflicting controller environments

After entering Addon Controller Mode:
    HidHide controller configuration follows the Addon baseline
```

The exact implementation should remain intentionally narrow and deterministic.

Do not build a generalized HidHide multi-owner manager.

---

## 6. Disable flow: configure first, then reboot

The Disable action should be implemented as a reboot-bound transition.

### 6.1 User interaction

The user selects:

```text
Disable MSI Center M
```

The Addon shows a blocking confirmation dialog such as:

```text
Disable MSI Center M and switch controller authority
 to Steam Addon for Claw.

Windows must restart to apply this change.

[Cancel] [Disable and Restart]
```

There is no `Restart Later` option.

If the user chooses Cancel:

```text
No persistent configuration is changed.
```

If the user chooses Disable and Restart, the Addon begins the transition.

### 6.2 Preflight

Before changing persistent configuration, verify at minimum:

```text
Supported MSI Claw hardware
Required elevated helper path available
HidHide available/configurable
VIIPER prerequisites available as required by product install state
No unsupported conflicting controller manager environment
```

A preflight failure means:

```text
Do not change authority mode.
Do not reboot.
Report the blocking reason.
```

### 6.3 Configure HidHide for next boot

Before reboot, prepare the Addon-owned HidHide baseline.

At minimum:

```text
InverseWhitelist = false
Addon executable is whitelisted
HidHide Active = true
```

If a previously known exact Addon-owned PID1902 primary gamepad collection exists, it may remain/additionally be present in the blocked list so the next PID1902 enumeration is immediately cloaked.

If no trusted exact PID1902 collection identity is known yet, do **not** invent a broad VID/PID-based hidden target merely to pre-cloak the first boot.

The exact target can be resolved after PID1902 appears on the next boot.

### 6.4 Disable Center M startup roots

Configure:

```text
Scheduled Task: MSI_Center_M_Server   = Disabled
Scheduled Task: MSI_Center_M_Updater  = Disabled
Service: MSI Foundation Service       = Disabled
```

The mutation must be read back and verified.

### 6.5 Do not perform live controller takeover

During the current Windows session, the Disable transition must **not** do any of the following:

```text
Do not switch PID1901 → PID1902.
Do not acquire DirectInput.
Do not attach Xbox360.
Do not attach Steam Deck.
Do not start controller publishers.
Do not try to create a live Addon controller session.
```

The current session remains MSI-owned until reboot.

### 6.6 Center M process termination is not required for this transition

Because the current session does not perform controller takeover, existing Center M processes do not need to be killed merely to finish the Disable action.

The desired sequence is intentionally simpler:

```text
Disable persistent startup roots
Configure HidHide next-boot baseline
Verify
Reboot
```

This eliminates the need to synchronize current-session Center M teardown with PID1902 acquisition.

### 6.7 Reboot is mandatory

After all persistent mutations have been verified:

```text
Request immediate Windows restart.
```

The transition is not considered successfully applied until the reboot boundary is crossed and the next startup verifies the Disabled configuration.

If the reboot request itself fails, the application must not silently return to normal operation while pretending the transition is complete.

The UI should remain in a clear restart-required / transition-not-complete state or attempt safe rollback according to the final implementation policy.

Avoid introducing an elaborate transaction engine solely for this path, but do not report success when the required reboot could not be initiated.

---

## 7. First boot after Center M Disable

The first Disabled boot and every later Disabled boot should use the same controller ownership logic.

Do not introduce a separate `FirstBootAfterDisable` state unless real hardware demonstrates a requirement that cannot be derived from current system state.

The durable user intent is simply:

```text
Center M startup configuration = Disabled
```

The boot-time controller runtime should converge to the same desired state on every boot.

---

## 8. Disabled boot startup sequence

Recommended high-level sequence:

```text
Windows login
    ↓
Addon auto-start
    ↓
Verify supported MSI Claw
    ↓
Verify Center M startup configuration == Disabled
    ↓
Verify expected Addon HidHide baseline
    ↓
Start Steam / BPM observation
    ↓
Initialize canonical VIIPER runtime
    ├─ Xbox360  CREATED / DETACHED
    └─ SteamDeck CREATED / DETACHED
    ↓
Inspect physical controller mode
    ↓
Reconcile physical controller to PID1902
    ↓
Acquire DirectInput
    ↓
Resolve exact PID1902 primary gamepad collection
    ↓
Ensure HidHide target is blocked
    ↓
Verify physical isolation
    ↓
Capture current desired presentation
    ↓
Attach exactly one virtual controller
```

The important ordering contract is:

> **Do not attach any virtual controller until PID1902 input and HidHide isolation are both verified.**

---

## 9. Boot-time PID handling

### 9.1 If the physical controller is PID1901

This is expected after a normal Windows boot.

Sequence:

```text
Strongly identify supported MSI Claw
→ switch same device to PID1902 / DirectInput mode
→ wait for bounded PnP stabilization
→ verify same physical identity
→ acquire DirectInput
```

### 9.2 If the physical controller is already PID1902

Do not force an unnecessary round trip through PID1901.

Sequence:

```text
Verify supported same physical MSI Claw
→ acquire DirectInput
→ continue isolation reconciliation
```

### 9.3 If the physical controller is temporarily missing

At immediate login, PnP/WMI may not yet be ready.

Use a bounded startup stabilization window consistent with the existing product philosophy.

Do not introduce an infinite boot polling loop.

If the supported controller does not become identifiable within the bounded admission window:

```text
Do not mutate ambiguous hardware.
Do not attach a virtual controller.
Remain fail-closed/passive and surface the failure.
```

---

## 10. First-boot HidHide behavior

The first Disabled boot may not yet have an exact persisted PID1902 collection identity.

That is acceptable.

Safe first-boot sequence:

```text
HidHide Active = true
Addon whitelisted
Virtual devices = both detached

PID1901 → PID1902
→ wait for PnP settle
→ resolve exact PID1902 primary gamepad collection
→ add exact blocked device entry
→ verify HidHide isolation
→ only then attach virtual controller
```

During the short interval before the hidden entry is added:

```text
Physical PID1902 may be visible
Virtual controller is NOT attached
```

Therefore the design avoids the dangerous state:

```text
visible physical controller
+
visible virtual controller
```

which would create a real double-input window.

---

## 11. Subsequent Disabled boots

After the exact PID1902 collection has been established and left in the persistent Addon HidHide baseline, subsequent boots become simpler.

Example:

```text
Windows boot
Physical controller starts PID1901
HidHide Addon baseline already active
PID1902 target already registered

Addon starts
→ PID1901 → PID1902
→ PID1902 enumerates into an already configured HidHide rule
→ physical gamepad is cloaked immediately
→ DirectInput remains available to the whitelisted Addon
→ verify isolation
→ attach desired virtual presentation
```

This is one of the primary reasons to keep the Addon HidHide baseline persistent across reboot.

Do not remove and recreate the same PID1902 hidden entry on every ordinary shutdown/startup cycle.

---

## 12. Normal Addon exit while Center M remains Disabled

Center M Disabled is a durable product mode, but physical controller ownership still belongs to the running Addon process.

Therefore normal Addon shutdown should leave the machine in a safe physical state.

Recommended normal shutdown:

```text
Active virtual presentation
→ neutral
→ publisher stop
→ detach

DirectInput session
→ release

Physical MSI Claw
→ restore PID1901

VIIPER
→ teardown

Addon exits
```

The persistent HidHide configuration may remain in place:

```text
Addon whitelist stays configured
PID1902 hidden entry stays configured
HidHide Active stays configured for Disabled mode
```

Because the physical controller has been restored to PID1901, the PID1902 hidden entry is dormant until the next Addon-owned startup.

This gives the next boot/startup a pre-cloak path without leaving the user with a hidden PID1902 controller after a clean Addon shutdown.

---

## 13. Crash / restart behavior

A crash may leave runtime state partially owned.

On the next Addon startup, the durable product intent is still:

```text
Center M Disabled
→ Addon should own the controller once the runtime is healthy again
```

Startup should reconcile current reality instead of assuming a particular previous state.

Possible current physical states include:

```text
PID1901
PID1902
temporarily missing during PnP
```

The startup reconcile should:

```text
verify Disabled authority mode
→ verify safe/known physical identity
→ recover or normalize stale transient runtime state
→ establish PID1902
→ acquire DirectInput
→ reconcile Addon HidHide baseline
→ initialize fresh VIIPER runtime
→ attach presentation based on current Steam/BPM fact
```

Persistent HidHide configuration is not inherently stale just because a previous process exited unexpectedly.

The old Steam-session rule of "startup means remove stale HidHide routing state" must therefore be revisited for the Full PID1902 architecture.

---

## 14. Presentation selection must be dynamic at first attach

The first virtual controller after boot must **not** be hard-coded to Xbox360.

The normal/default policy remains:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → Steam Deck
```

But the Addon may start at the same time Steam is starting directly into Big Picture Mode, or a Steam game may become active while physical controller ownership is still being acquired.

Therefore the correct policy is:

> **Choose the first presentation from the freshest available Steam/BPM state immediately before the first attach.**

Do not decide the presentation at the beginning of the ownership acquisition sequence and carry that stale decision forward.

---

## 15. Boot-time Steam/BPM observation

Steam/BPM observation is now a read-only fact source for presentation policy.

It is no longer the authority that decides whether the physical controller should be owned.

Therefore observation can begin early in startup and independently of physical controller acquisition.

Conceptually:

```text
Startup
├─ observe RunningAppID / Steam session / BPM
│
└─ acquire Addon controller ownership
```

Once the physical controller is ready and isolated:

```text
capture latest presentation desire
```

Decision:

```text
Steam game active OR BPM active
    → Steam Deck

otherwise
    → Xbox360
```

If the product retains a user-level Steam routing/presentation toggle, it may remain a presentation eligibility input:

```text
SteamPresentationEnabled
AND (Steam game active OR BPM active)
    → Steam Deck

otherwise
    → Xbox360
```

This setting must not control physical PID1902 ownership while Center M is Disabled.

---

## 16. First attach ordering

Recommended final acquisition boundary:

```text
Physical PID1902 verified
DirectInput acquired
HidHide exact physical target verified hidden
Canonical VIIPER dual runtime Ready
Xbox360 detached
SteamDeck detached
    ↓
Acquire presentation mutation gate
    ↓
Read latest Steam/BPM state
    ↓
Choose desired presentation
    ↓
Attach selected device
    ↓
Send neutral state
    ↓
Start selected publisher
    ↓
Release presentation gate
```

After startup, later Steam/BPM events use the same desired-vs-current presentation reconcile path.

Do not create a separate startup-only controller switching architecture.

---

## 17. Do not over-engineer startup presentation races

A Steam/BPM event may occur while the first controller presentation is being attached.

The product does not need epochs, multi-phase commit barriers, or a generalized presentation state machine solely for that narrow interleaving.

Use simple convergence:

```text
Read latest desired state before attach
Perform one serialized presentation mutation
If a state-change event occurred during the mutation,
run normal presentation reconcile afterward
```

The final invariant is what matters:

```text
DesiredPresentation == CurrentPresentation
```

This is sufficient for realistic handheld lifecycle behavior.

---

## 18. Runtime PID1902 drift after Disabled boot

The reboot boundary simplifies intentional mode changes, but the runtime must still handle real faults.

While Center M is Disabled and the Addon owns the controller:

```text
same physical MSI Claw PID1902 → PID1901
```

is not an ownership transfer to respect.

It is owned-state drift.

Recommended recovery:

```text
neutral active virtual presentation
→ retire stale DirectInput session / publisher
→ verify same strong physical identity
→ verify Center M remains configured Disabled
→ quiesce any conflicting Center M runtime if it unexpectedly resurrected
→ reclaim same device to PID1902
→ wait PnP settle
→ reacquire DirectInput
→ reconcile HidHide
→ resume desired current presentation
```

The old `ExternalNativeTakeover → YieldCurrentSteamSession` policy is not part of this architecture.

---

## 19. Center M runtime resurrection after Disabled boot

A normal Disabled boot should prevent Center M startup roots from launching.

If a Center M controller process nevertheless appears later, that is a runtime integrity failure, not a normal coexistence state.

The Addon may use targeted process quiescence/recovery while Disabled.

Prefer exact known conflicting roots and verified child retirement.

Do not build a broad MSI process killer.

The runtime recovery policy is independent from the Disable transition itself:

```text
Disable transition before reboot
    → no current-session kill required

Unexpected Center M resurrection during Addon-owned runtime
    → targeted quiesce + controller reconcile
```

---

## 20. Enable flow: restore stock authority, then reboot

Switching back to Center M Enabled is also reboot-bound.

### 20.1 User interaction

The user selects:

```text
Enable MSI Center M
```

Show a blocking confirmation dialog such as:

```text
Restore MSI Center M controller authority.

Windows must restart to apply this change.

[Cancel] [Enable and Restart]
```

There is no `Restart Later` option.

### 20.2 Current-session controller teardown

Because the Addon currently owns the controller in Disabled mode, it must first return the current session to a safe stock physical state before reboot.

Recommended order:

```text
active virtual presentation
→ neutral
→ stop publisher
→ detach

DirectInput
→ release

Physical MSI Claw
→ PID1901
→ verify stock mode

VIIPER
→ teardown
```

### 20.3 Remove Addon controller HidHide baseline

Remove the Addon controller configuration used for Disabled mode.

At minimum:

```text
remove Addon PID1902 blocked target(s)
remove Addon controller whitelist entry if no longer required
set HidHide Active to the clean Enabled-mode baseline
verify no Addon controller isolation remains
```

The final exact Enabled-mode HidHide cleanup policy should be deterministic and must not leave the stock PID1901 controller dependent on the Addon.

### 20.4 Re-enable Center M startup roots

Configure:

```text
Scheduled Task: MSI_Center_M_Server   = Enabled
Scheduled Task: MSI_Center_M_Updater  = Enabled
Service: MSI Foundation Service       = Automatic
```

Read back and verify the exact configuration.

### 20.5 Reboot immediately

After successful teardown and persistent configuration change:

```text
Restart Windows immediately.
```

Next boot should start in:

```text
Center M Enabled
→ MSI / stock controller authority
→ Addon controller stack passive
```

---

## 21. No mode change without reboot

The UI contract should be explicit:

```text
Cancel
→ no change

Disable and Restart
→ commit Disabled next-boot configuration
→ reboot

Enable and Restart
→ commit Enabled next-boot configuration
→ reboot
```

Do not support:

```text
Apply without reboot
Restart later
Temporary live Disabled mode
Temporary live Enabled mode
```

This is an intentional product simplification.

---

## 22. Failure handling during mode transition

The transition should remain simple, but must not report success if the persistent configuration is incomplete.

General rule:

```text
Perform required persistent mutations
→ read back / verify
→ only then request reboot
```

If a required mutation fails:

```text
Do not reboot as though the transition succeeded.
Do not claim the new authority mode is active.
Surface a clear failure.
```

If practical, restore the previous known-safe configuration when a transition fails partway through.

Do not create a large generalized transaction framework solely for this feature.

A small, explicit ordered transition with readback verification is preferred.

---

## 23. Authority state after reboot is determined from actual configuration

Do not rely on an extra duplicated application boolean such as:

```text
AddonControllerModeEnabled=true
```

if the actual Center M startup configuration already defines authority.

Preferred authority interpretation:

```text
Center M startup roots exactly Disabled
→ desired controller authority = Addon

Center M startup roots exactly Enabled / Automatic
→ desired controller authority = MSI / Stock

Mixed / Partial configuration
→ invalid / needs repair
```

This keeps one persistent authority source of truth.

A partial state must not silently choose one controller owner.

---

## 24. Recommended steady-state invariants

### Center M Enabled

```text
CenterMStartupConfiguration = Enabled
Physical controller         = MSI / stock path
Desired PID                 = PID1901
Addon DirectInput owner     = no
Addon physical HidHide      = no
Addon VIIPER presentation   = no
```

### Center M Disabled, Addon running

```text
CenterMStartupConfiguration = Disabled
Physical controller         = same supported MSI Claw
Desired PID                 = PID1902
DirectInput                 = healthy
HidHide                     = Addon baseline active
Physical primary gamepad    = isolated
VIIPER runtime              = healthy
Xbox360 logical device      = created
SteamDeck logical device    = created
Exactly one presentation    = attached/live
```

Presentation:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

### Center M Disabled, Addon intentionally not running after clean shutdown

Recommended safe physical state:

```text
Physical controller = PID1901
Virtual presentation = none
VIIPER runtime = none
Persistent Addon HidHide PID1902 rule may remain dormant
```

---

## 25. Architectural consequences for existing code

Several existing components were built for the old Steam-session-bound routing model.

They should be treated as sources of useful low-level primitives, not as preservation constraints.

### Existing behavior that likely needs semantic replacement

Examples include:

- route-scoped HidHide acquisition and rollback;
- startup cleanup that assumes Addon-owned HidHide state is stale merely because the previous routing session ended unexpectedly;
- Steam activity as physical controller ownership authority;
- route entry/exit driving PID1901 ↔ PID1902;
- `ExternalNativeTakeover` causing Steam-session yield;
- Game Bar/X360 presentation logic assuming an outer Steam Deck route.

### Existing behavior worth reusing where appropriate

Examples include:

- strong MSI Claw physical identity checks;
- bounded PnP stabilization;
- PID1901 ↔ PID1902 native-mode primitives;
- DirectInput input source;
- exact MSI PID1902 primary HID collection resolution;
- HidHide inspect/add/remove/active primitives;
- HidHide verification logic;
- canonical VIIPER runtime and typed device lifecycle;
- X360 and Steam Deck publishers/mappers;
- Steam RunningAppID event observation;
- BPM event observation;
- suspend/resume hooks;
- recovery logging and fail-close behavior where it protects real lifecycle safety.

Do not preserve old orchestration merely because tests currently encode it.

---

## 26. Recommended implementation sequence

A clean implementation sequence would be:

### PR A — Reboot-bound authority transition

Implement only the persistent mode-change boundary:

```text
Disable and Restart
Enable and Restart
Center M startup configuration readback
mandatory reboot UX
no Restart Later path
```

Include HidHide baseline preparation/cleanup as required for the selected mode, but do not implement live PID1902 controller ownership as part of the Disable transition.

### PR B — Disabled boot admission and PID1902 ownership

On startup when Center M is verified Disabled:

```text
verify supported hardware
verify Disabled-mode HidHide baseline
initialize dual VIIPER runtime detached
PID1901 → PID1902 if needed
acquire DirectInput
resolve exact physical HID target
ensure/verify isolation
```

Still no automatic presentation switching beyond the first selected presentation if PR size requires separation.

### PR C — First dynamic presentation attach

After physical ownership is ready:

```text
fresh Steam/BPM snapshot
→ X360 or SteamDeck
→ attach exactly one
→ start corresponding publisher
```

### PR D — Runtime presentation reconcile

Wire later Steam/BPM state changes to:

```text
X360 ↔ SteamDeck
```

without changing physical PID1902 ownership, DirectInput, HidHide, or VIIPER server/bus ownership.

### PR E — Owned-state recovery

Add real lifecycle recovery for:

```text
PID1902 → PID1901 drift
physical device loss / re-arrival
DirectInput session failure
HidHide drift
Center M runtime resurrection
resume
```

### Cleanup

Remove obsolete Steam-session physical routing contracts and dead coexistence policies once the new controller owner is proven.

---

## 27. Hardware validation matrix

The eventual implementation should be validated on supported MSI Claw hardware with at least the following cases.

### Authority transition

```text
Enabled → Disable and Restart
Disabled → Enable and Restart
Cancel from each confirmation
persistent configuration mutation failure
reboot request failure
```

### First Disabled boot

```text
physical starts PID1901
physical unexpectedly already PID1902
PID1902 exact HidHide entry already known
PID1902 exact HidHide entry not yet known
Steam not running
Steam desktop running
Steam game already active
Steam starting directly into BPM
Steam/BPM becomes active during controller acquisition
```

### Subsequent Disabled boots

Verify:

```text
persistent HidHide baseline survives reboot
PID1902 is cloaked before virtual attach
no physical + virtual double-input window
exactly one virtual presentation becomes visible
```

### Enabled restoration

Verify:

```text
virtual controller disappears
physical controller returns PID1901
Addon-owned HidHide controller isolation is removed
Center M startup roots are restored
Center M behaves normally after reboot
```

### Runtime lifecycle

Later recovery PRs must additionally validate:

```text
sleep / resume
hibernate / resume
physical unplug-equivalent PnP disappearance/re-enumeration
PID1902 → PID1901 drift
DirectInput fault
Addon crash / restart
Windows shutdown / restart
```

---

## 28. Final design summary

The intended product rule is simple:

> **Center M Enabled/Disabled is not a live toggle. It selects the controller authority for the next Windows boot.**

Changing authority requires reboot.

```text
Enable / Disable request
        ↓
configure persistent next-boot state
        ↓
mandatory restart
        ↓
next boot enters exactly one controller authority model
```

For Addon-owned mode:

```text
Center M Disabled
        ↓
Addon owns HidHide baseline
        ↓
Addon acquires PID1902 / DirectInput after boot
        ↓
physical isolation verified
        ↓
VIIPER already owns both detached typed devices
        ↓
fresh Steam/BPM fact selects first presentation
        ↓
Xbox360 OR SteamDeck attached
```

The most important simplifications are:

1. **No live Center M ↔ Addon authority handoff.**
2. **No Restart Later mode.**
3. **No runtime coexistence with another controller authority.**
4. **HidHide is part of the persistent Addon controller configuration while Center M is Disabled.**
5. **Virtual presentation is attached only after physical isolation is verified.**
6. **The first presentation is selected from the latest Steam/BPM state, not hard-coded to Xbox360.**
7. **Steam/BPM changes virtual presentation only; they never decide physical PID1902 ownership.**

This should be the baseline contract for the next Full PID1902 implementation work.