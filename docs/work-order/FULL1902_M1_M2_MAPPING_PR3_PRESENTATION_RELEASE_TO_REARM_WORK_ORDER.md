# Work Order — Full1902 M1/M2 Mapping PR3: Presentation-Switch Release-to-Rearm Safety

## Status

Implementation work order for PR3 of the global M1/M2 back-button mapping feature.

Baseline reviewed against current `main`:

```text
06fcfcf8bb39bddbf0007158505a07a2cedd1380
feat: apply M1/M2 mappings to Xbox360 output (#555)
```

Current feature sequence:

```text
PR1  Contract + persistence + frontend transport foundation      [merged #554]
 ↓
PR2  Xbox360 M1/M2 software mapping                              [merged #555]
 ↓
PR3  Xbox360 ↔ SteamDeck held-button release-to-rearm safety     [this PR]
 ↓
PR4  Controller-page UI
```

This PR is a **narrow lifecycle-safety PR**.

It must fix one concrete supported handheld scenario:

> A physical M1/M2 press that is already held while the Addon changes virtual presentation must not automatically acquire the other presentation's different meaning.

Do not turn this into a general controller remapping state machine.

---

## 1. Mandatory architecture context

Read these before implementation:

```text
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md
docs/work-order/FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR1_CONTRACT_PERSISTENCE_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR2_LIVE_OUTPUT_WORK_ORDER.md
```

Current authority remains:

```text
Center M Disabled
→ Addon owns physical PID1902
→ one DirectInput source
→ one persistent HidHide baseline
→ one canonical VIIPER runtime

Steam/BPM inactive
→ Xbox360 presentation

Steam game or BPM active
→ SteamDeck presentation
```

Steam/BPM changes only the virtual presentation.

PR3 must not create another controller authority, another physical-input owner, or another VIIPER lifecycle.

---

## 2. Current code facts reviewed

### 2.1 PR2 is now production-live

PR2 merged the global Xbox360 back-button projection:

```text
StartupSettingsCoordinator.BackButtonMapping
        ↓
MsiClawAddonPresentation
        ↓
CanonicalXbox360InputPublisher
        ↓
Xbox360DeviceStateMapper
        ↓
M1/M2 X360 target overlay
```

The Xbox360 publisher reads the current mapping on every normal report.

That live-setting behavior remains correct and must not gain release-to-rearm semantics.

### 2.2 SteamDeck semantics remain native

Current `SteamDeckDeviceStateMapper` still maps:

```text
M1 / RightRear → R4
M2 / LeftRear  → L4
```

### 2.3 Existing PR7 switch owner already has the correct lifecycle boundary

Current `MsiClawAddonPresentation.ReconcileDesiredPresentationAsync(...)` already serializes the real presentation transition under its one existing `_gate`.

Current kind-change path is:

```text
capture fresh Steam/BPM desired kind inside owner gate
        ↓
RetireActivePresentationCoreAsync(...)
        ↓
old publisher stop + JOIN
        ↓
old synthetic pulse clear
        ↓
old feedback callback disarm + physical rumble STOP
        ↓
old typed device neutral + detach
        ↓
AttachXbox360Async(...) or AttachSteamDeckAsync(...)
        ↓
target typed device attach
        ↓
target neutral
        ↓
target publisher start
        ↓
commit active presentation
```

This is the correct place to add the M1/M2 semantic handoff rule.

Do not add a second switch coordinator.

---

## 3. Concrete bug after PR2

Example:

```text
saved X360 mapping:
M1 = A

Xbox360 active
M1 physically pressed and held
→ virtual A held

Steam/BPM becomes active
→ PR7 switches Xbox360 → SteamDeck
→ old X360 output is neutralized safely
→ SAME physical M1 is still held
→ first SteamDeck reports currently see M1 held
→ R4 becomes held without a new physical press
```

The inverse is equally real:

```text
SteamDeck active
M1 physically held
→ R4 held

Steam/BPM becomes inactive
→ PR7 switches SteamDeck → Xbox360
→ old Deck output is neutralized
→ SAME physical hold is still present
→ X360 M1 target (for example A) becomes held without a new physical press
```

This is not a theoretical instruction-level race.

It is a normal product lifecycle:

```text
user holds rear button
+
Steam game/BPM state changes
```

The outgoing meaning must end at the switch, and the incoming meaning must require one physical release before it can become active.

---

## 4. Required product behavior

### 4.1 Xbox360 → SteamDeck

Given:

```text
M1 mapping = A
Xbox360 active
M1 held
```

Switch to SteamDeck:

```text
old X360 publisher stops
→ old X360 neutral/detach
→ target Deck attaches neutral
→ Deck publisher starts
→ SAME held M1 does NOT assert R4
```

Then:

```text
M1 physically releases
→ M1 is rearmed

next M1 press
→ R4 normally
```

### 4.2 SteamDeck → Xbox360

Given:

```text
SteamDeck active
M1 held
→ R4
saved X360 M1 = A
```

Switch to Xbox360:

```text
old Deck publisher stops
→ old Deck neutral/detach
→ target X360 attaches neutral
→ X360 publisher starts
→ SAME held M1 does NOT assert A
```

Then:

```text
M1 release
→ rearm

next press
→ A normally
```

### 4.3 M1 and M2 are independent

If only M1 is held across the switch:

```text
M1 suppressed until release
M2 immediately usable
```

If both are held:

```text
M1 suppressed until M1 release
M2 suppressed until M2 release
```

Releasing one must not rearm the other.

---

## 5. Scope the gate to actual presentation-kind changes only

Arm the release-to-rearm behavior only for:

```text
Xbox360 → SteamDeck
SteamDeck → Xbox360
```

Do **not** arm it for:

```text
initial presentation attach
NoChange reconcile
Xbox360 → Xbox360 restart
SteamDeck → SteamDeck restart
Overlay pause/resume of the SAME presentation
Suspend pause/resume of the SAME presentation
live BackButtonMapping setting mutation
physical-input recovery that resumes the SAME semantic presentation
```

The reason is semantic, not structural:

> The gate exists only because M1/M2 mean different things between Xbox360 and SteamDeck.

If the semantic presentation kind did not change, a release requirement is unnecessary.

---

## 6. Keep the state inside the existing presentation owner

The one current presentation owner already knows:

- the outgoing presentation kind;
- the desired incoming kind;
- whether old retirement actually succeeded;
- the exact point before the new publisher begins;
- when the presentation is fully released.

Therefore keep the release-to-rearm state in `MsiClawAddonPresentation`.

Preferred minimal state:

```csharp
private int _suppressM1UntilRelease;
private int _suppressM2UntilRelease;
```

or equivalent two `volatile bool` fields.

There must be exactly two independent facts:

```text
M1 must release before it may publish in the new presentation
M2 must release before it may publish in the new presentation
```

Do not add:

```text
RearButtonStateMachine
BackButtonTransitionManager
presentation epoch
mapping epoch
button generation
button transition queue
press/release history list
per-presentation dictionary
extra SemaphoreSlim
extra lock
```

The existing presentation `_gate` remains the lifecycle serialization authority.

---

## 7. Cross-thread visibility: use the smallest primitive

The presentation owner arms/clears transition state from the lifecycle path, while the active publisher consumes/rearms it on its dedicated publication thread.

Use only the minimum memory-visibility primitive needed for these two facts.

Acceptable:

```text
Volatile.Read / Volatile.Write on two int flags
```

or:

```text
two volatile bool fields
```

Do not put the 250 Hz publication path under `_gate`.

Do not add a new lock around every controller report.

The practical ownership pattern is:

```text
kind switch
→ old publisher is stopped and JOINED
→ owner writes gate flags
→ new publisher starts
→ only that live publisher observes/clears the flags
```

A later kind switch again stops/joins the old publisher before the owner changes the flags.

That is enough for the supported lifecycle.

---

## 8. Arm only after successful retirement of the old presentation

Do not arm before the current publisher has been proven stopped and the old presentation retired.

Required sequence:

```text
previousKind != desiredKind
        ↓
RetireActivePresentationCoreAsync(...)
        ↓
require success
        ↓
sample current source.LatestState once
        ↓
M1 currently held? → arm M1 release gate
M2 currently held? → arm M2 release gate
        ↓
attach target presentation
```

Why sample **after** retirement:

- if the user releases the rear button while the old publisher is stopping, there is no reason to suppress the new meaning;
- the outgoing virtual device is already neutral/detached;
- there is one clear target-attach boundary.

### 8.1 Do not overengineer continuity proof

A button pressed in the tiny window after old retirement but before the one snapshot sample may also be conservatively treated as needing release.

That is acceptable.

Do not add:

- before/after dual snapshots;
- timestamps;
- edge sequence numbers;
- raw StateChanged subscription;
- barriers intended to prove the button was held on both sides of an exact instruction boundary.

The real safety property is:

> A rear button observed held at the new-presentation boundary cannot immediately assert the new semantic action.

One post-retire snapshot is sufficient.

---

## 9. Use semantic auxiliary slots

The canonical rear identities remain:

```text
M1 = AuxiliaryButtonSlot.RightRear
M2 = AuxiliaryButtonSlot.LeftRear
```

Do not use DirectInput button indexes 15/16 in the presentation owner or publishers.

Use a tiny safe helper for current raw state:

```csharp
private static bool IsRearPressed(ControllerState state, AuxiliaryButtonSlot slot)
{
    var index = (int)slot;
    return index >= 0
        && index < state.Auxiliary.Count
        && state.Auxiliary[index];
}
```

Equivalent code is fine.

A short/default auxiliary state means "not observed held" for this gate; it must not throw.

Do not modify the raw physical source.

---

## 10. Rearm on the first observed physical release

The live publisher already samples `LatestState` continuously.

Reuse that path.

Conceptually for M1:

```csharp
private bool ShouldSuppressM1(ControllerState state)
{
    if (Volatile.Read(ref _suppressM1UntilRelease) == 0)
        return false;

    if (IsRearPressed(state, AuxiliaryButtonSlot.RightRear))
        return true;

    Volatile.Write(ref _suppressM1UntilRelease, 0);
    return false;
}
```

M2 is symmetric.

Behavior:

```text
gate not armed
→ never suppress

gate armed + still held
→ suppress

gate armed + released
→ clear gate
→ release frame naturally produces no rear action

next press
→ not suppressed
```

Do not wait a fixed number of milliseconds.

Do not debounce.

Do not synthesize a release.

The physical release itself rearms the button.

---

## 11. Preferred publisher seam

Both output publishers need the same presentation-owned suppression decision.

Keep it narrow.

One acceptable shape is a single callback:

```csharp
Func<ControllerState, AuxiliaryButtonSlot, bool>
```

whose meaning is:

> for this exact current raw frame and rear slot, should this rear contribution be suppressed because a kind-switch release is still pending?

Publisher default when no callback is supplied:

```text
always false
```

This preserves all standalone publisher tests and non-production construction.

Conceptually:

```csharp
var state = _snapshot.LatestState;

var suppressM1 = _rearButtonSuppressionProvider(
    state,
    AuxiliaryButtonSlot.RightRear);

var suppressM2 = _rearButtonSuppressionProvider(
    state,
    AuxiliaryButtonSlot.LeftRear);
```

Equivalent two narrow callbacks are also acceptable if that produces simpler code.

Do not introduce an interface/manager solely to carry two booleans.

---

## 12. CanonicalXbox360InputPublisher integration

Current PR2 publish operation reads:

```text
current LatestState
+
current BackButtonMappingSettings
→ Xbox360DeviceStateMapper
```

PR3 adds only the two rear-suppression decisions:

```text
current LatestState
+
current BackButtonMappingSettings
+
suppress M1?
+
suppress M2?
→ Xbox360DeviceStateMapper
```

The current mapping provider must still be read on every report.

Do not change PR2 behavior where a live mapping mutation becomes effective without publisher restart.

### Important

Suppression applies to the **rear contribution only**.

Example:

```text
physical A is held
M1 target = A
M1 is release-gated

→ physical A MUST still publish A
→ only M1's additional A contribution is suppressed
```

Never clear the final X360 target bit globally just because a mapped rear source is suppressed.

---

## 13. Xbox360DeviceStateMapper integration

Extend the mapping-aware overload with two optional suppression facts.

One acceptable shape:

```csharp
internal static Xbox360DeviceState Map(
    ControllerState state,
    BackButtonMappingSettings mapping,
    bool suppressM1 = false,
    bool suppressM2 = false)
```

Then:

```text
M1 rear contribution is active only when:
physical M1 held && !suppressM1

M2 rear contribution is active only when:
physical M2 held && !suppressM2
```

All ordinary physical X360 controls remain untouched.

Keep:

- additive OR behavior;
- duplicate M1/M2 target behavior;
- analog LT/RT full-pull overlay behavior;
- Guide behavior;
- unknown-target fail-neutral behavior.

No mapping persistence or validation change is needed.

---

## 14. CanonicalSteamDeckInputPublisher integration

SteamDeck also needs the release gate because the incoming semantic action may be R4/L4.

Current:

```text
LatestState
→ SteamDeckDeviceStateMapper
→ system-button overlay
→ VIIPER sink
```

PR3:

```text
LatestState
→ query M1/M2 release suppression
→ SteamDeckDeviceStateMapper(state, suppressM1, suppressM2)
→ system-button overlay
→ VIIPER sink
```

Do not apply the gate to:

- A/B/X/Y;
- D-pad;
- bumpers;
- triggers;
- sticks;
- Steam synthetic pulse;
- Quick Access synthetic pulse.

Only M1/R4 and M2/L4 need this semantic handoff safety.

Do not change the 4 ms scheduler, diagnostics, QoS, or sink failure policy.

---

## 15. SteamDeckDeviceStateMapper integration

Preferred shape:

```csharp
internal static SteamDeckDeviceState Map(
    ControllerState state,
    bool suppressM1 = false,
    bool suppressM2 = false)
```

Required rear mapping:

```text
R4 = M1 held && !suppressM1
L4 = M2 held && !suppressM2
```

Everything else remains byte-for-byte equivalent to current behavior.

The X360 global `BackButtonMappingSettings` must still never enter the SteamDeck mapper.

---

## 16. Production composition stays in MsiClawAddonPresentation

Current default publisher factories are created inside `MsiClawAddonPresentation`.

Wire the same owner-owned suppression callback to:

```text
default CanonicalXbox360InputPublisher
default CanonicalSteamDeckInputPublisher
```

The owner remains the only lifecycle state authority.

No `AddonProcessHost` change should be necessary unless an actual compile-time composition issue proves otherwise.

Do not move the gate up into `AddonProcessHost`.

Do not put it into `StartupSettingsCoordinator`.

---

## 17. Exact switch integration

Inside `ReconcileDesiredPresentationAsync(...)` preserve current ordering.

Conceptually:

```csharp
var previous = _activeKind;
var isKindChange = previous is not null && previous != desired;

if (previous is not null)
{
    if (!await RetireActivePresentationCoreAsync(...))
        return Failed(...);
}

if (isKindChange)
    ArmRearButtonReleaseGate(source.LatestState);

var attach = desired == AddonPresentationKind.Xbox360
    ? await AttachXbox360Async(source)
    : await AttachSteamDeckAsync(source);

if (!attach.Succeeded)
{
    if (isKindChange)
        ClearRearButtonReleaseGate();

    return Failed(...);
}
```

Exact naming may differ.

### 17.1 A same-kind structural restart is not a semantic switch

Current reconcile can enter retirement if:

```text
_activeKind == desired
but current publisher is not running
```

That path may retire/re-attach the same kind.

Do not arm release-to-rearm there.

Required check is semantic:

```text
previous != desired
```

not merely:

```text
a retire/attach happened
```

---

## 18. Clear stale gate state on successful full retirement

When a presentation is successfully retired to no active presentation, any old release gate must not become an unrelated future-attach condition.

At the successful end of `RetireActivePresentationCoreAsync(...)`, clear both flags.

During a kind switch, the sequence therefore becomes:

```text
successful old retire
→ clears any previous stale gate
→ switch code samples current rear state
→ arms fresh gate for this exact kind transition
→ target attach
```

This also naturally clears gate state for:

- publisher-fault retirement;
- Center M Enable release;
- Enter BIOS release;
- process teardown;
- successful fail-close retirement.

### 18.1 Failed retirement

If retirement fails and ownership evidence is retained:

```text
do not attach target
do not arm a new kind-switch gate
```

Do not mutate lifecycle state as though the kind switch succeeded.

---

## 19. Clear the freshly armed gate if target attach fails

A successful old retirement followed by a failed target attach leaves no live incoming presentation.

In that case:

```text
clear M1/M2 release gate
→ return existing TargetAttachFailed result
```

Reason:

- no new semantic presentation became live;
- a later event that attaches from `None` is a fresh attach, not continuation of the failed kind switch;
- stale suppression must not survive indefinitely while no publisher exists.

Do not add a timer to expire it.

---

## 20. Initial attach behavior

Initial attach is not a semantic handoff from another virtual controller.

Required:

```text
process starts
M1 already physically held
initial desired = Xbox360
→ current normal X360 mapping behavior

process starts
M1 already physically held
initial desired = SteamDeck
→ current normal R4 behavior
```

PR3 does not change initial-attach policy.

Do not invent a startup "all controls must release" gate.

---

## 21. Live mapping changes remain immediate

PR2 deliberately defines:

```text
X360 active
M1 physically held
M1 mapping A
→ A

user changes M1 mapping to B
→ next normal report may become B
```

Keep this.

Do not arm release-to-rearm merely because `BackButtonMappingSettings` changed.

PR3 is only about:

```text
presentation kind A
→ presentation kind B
```

not mapping-edit semantics.

---

## 22. Disabled X360 target still participates in presentation handoff safety

Even if:

```text
M1 X360 target = Disabled
```

a held M1 should still require release when X360 changes to SteamDeck.

Otherwise:

```text
X360: M1 has no virtual action
→ user holds M1
→ switch to Deck
→ R4 appears without a new press
```

So arm from **raw physical M1/M2 state**, not from whether the current X360 mapping is enabled.

The gate is about rear-button semantic continuity, not mapping configuration.

---

## 23. Overlay pause/resume

Do not arm a new gate for Overlay pause/resume because the presentation kind is unchanged.

If a kind-switch gate was already armed before the Overlay opened:

- the existing gate may remain pending;
- when publication resumes, the first observed released raw state clears it normally.

Do not add raw input history merely to detect an unobserved release/re-press while publication was paused.

That exact timing combination does not justify another state/history system.

---

## 24. Suspend/Resume

Do not add new Suspend/Resume production branches for this PR.

Existing Full1902 Suspend path already owns:

```text
publisher stop/join
→ neutral same attached device
→ reset physical LatestState to neutral at Resume boundary
→ resume/reconcile through existing owner
```

A pre-existing rear release gate may naturally clear when a neutral raw snapshot is observed after Resume.

Do not make this PR redefine general held-control behavior across Sleep.

Do not add a suspend-specific M1/M2 epoch.

---

## 25. Physical device loss / PnP recovery

Do not add a rear-button recovery subsystem.

Existing PR8/PR9/PR10 physical recovery owns:

- DirectInput loss;
- PnP disappearance/re-enumeration;
- strong identity;
- HidHide reconciliation;
- source restart;
- presentation recovery.

If recovery results in the same semantic presentation, PR3 does not arm a new gate.

If a later normal PR7 reconcile performs a real Xbox360 ↔ SteamDeck kind change, the standard PR3 switch boundary handles it.

---

## 26. No change to physical input

Do not modify:

```text
MsiClawControllerStateMapper
MsiClawInputSource raw state semantics
ControllerState
AuxiliaryButtonState
DirectInput polling
Buttons[15]/Buttons[16] mapping
```

Do not overwrite `LatestState` merely to hide M1/M2.

The suppression is output-local.

This is important because the same raw physical snapshot is shared by both virtual presentation domains and diagnostics.

---

## 27. No settings/frontend change

Do not modify:

```text
BackButtonMappingSettings
Xbox360BackButtonTarget
SettingsStore
StartupSettingsCoordinator
FrontendSettingsSnapshot
FrontendBootstrapSnapshot
SetBackButtonMapping RPC
NamedPipe frontend transport
FrontendTransportProtocol.CurrentVersion
```

Frontend protocol remains:

```text
v38
```

PR4 will add the Main UI editor.

---

## 28. Expected production touch set

Expected production files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs

src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs

src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs
```

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs
tests/SteamInputAddonforClaw.Tests/Xbox360DeviceStateMapperTests.cs
tests/SteamInputAddonforClaw.Tests/CanonicalSteamDeckInputPublisherTests.cs
tests/SteamInputAddonforClaw.Tests/SteamDeckDeviceStateMapperTests.cs
```

Do not broaden into settings/UI/profile files.

---

## 29. Required Xbox360 mapper tests

### 29.1 Suppress M1 rear contribution only

Given:

```text
M1 target = A
M1 held
suppressM1 = true
```

Expected:

```text
M1 contributes no A
```

### 29.2 Physical target remains active

Given:

```text
physical A held
M1 target = A
M1 held
suppressM1 = true
```

Expected:

```text
A remains held from the physical A button
```

This test is mandatory.

Do not implement suppression by clearing the final target bit.

### 29.3 Independent M1/M2 suppression

Example:

```text
M1=A
M2=RB
both physically held
suppressM1=true
suppressM2=false
→ only RB
```

And inverse.

### 29.4 Trigger target suppression

Example:

```text
physical LT = 91
M1 target = LeftTrigger
M1 held
suppressM1 = true
→ LT remains 91, not 255
```

### 29.5 Default suppression false preserves PR2

Existing PR2 mapping tests must continue passing without specifying suppression.

---

## 30. Required SteamDeck mapper tests

### 30.1 M1 held + suppressed

```text
M1 held
suppressM1=true
→ R4=0
```

### 30.2 M2 held + suppressed

```text
M2 held
suppressM2=true
→ L4=0
```

### 30.3 Independence

```text
M1 and M2 held
suppressM1=true
suppressM2=false
→ R4=0
→ L4=1
```

and inverse.

### 30.4 Other controls unaffected

A/B/D-pad/etc. must remain unchanged when rear suppression is true.

---

## 31. Required publisher tests

Both canonical publishers need focused tests proving they actually consume the current suppression decision on each report.

### 31.1 Xbox360 publisher

Manual-tick sequence:

```text
raw M1 held
suppression provider → true
tick
→ configured M1 X360 target absent

suppression provider → false
next tick with same raw M1 held
→ configured target present
```

The publisher must not restart.

### 31.2 SteamDeck publisher

Manual-tick sequence:

```text
raw M1 held
suppression provider → true
tick
→ R4 absent

suppression provider → false
next tick
→ R4 present
```

### 31.3 Default provider

Construct either publisher without suppression provider.

Behavior must remain current baseline.

### 31.4 Existing fault/scheduler semantics

All current:

- sink rejection;
- exception;
- stop/join;
- restart;
- high-resolution timer;
- QoS;
- heartbeat

tests must remain valid.

---

## 32. Required presentation-owner tests

These are the core PR3 tests.

Use `FakeSource.LatestState` to control rear state deterministically.

If necessary, keep the owner suppression resolver `internal` so tests can call the exact production callback directly. Do not expose a public product API solely for tests.

### 32.1 Xbox360 → SteamDeck, M1 held

```text
initial X360
source M1 held
switch to Deck
→ switch succeeds
→ M1 suppression active

evaluate current held state
→ suppress=true

source M1 released
→ suppress=false and gate clears

source M1 pressed again
→ suppress=false
```

This proves release-to-rearm rather than permanent suppression.

### 32.2 SteamDeck → Xbox360 inverse

Same proof in the other direction.

### 32.3 M1/M2 independent

Arm both.

Release M1 only:

```text
M1 gate cleared
M2 gate remains
```

Then release M2.

### 32.4 No held rear buttons

Kind switch with both released:

```text
no suppression armed
```

### 32.5 NoChange reconcile

Hold M1 while desired kind remains current:

```text
NoChange
→ no release gate introduced
```

### 32.6 Same-kind structural restart

If the existing test seams can force:

```text
active Xbox360
publisher not running
desired Xbox360
→ retire/re-attach same kind
```

prove no semantic release gate is armed.

Do not create new production state solely to manufacture this test if the existing seam cannot express it cleanly.

### 32.7 Retirement failure

```text
M1 held
kind change desired
old publisher join or detach fails
→ target not attached
→ no new switch gate armed
```

### 32.8 Target attach failure

```text
old retire succeeds
M1 held
gate arms
target attach fails
→ gate cleared
→ later fresh attach is not suppressed by stale PR3 state
```

### 32.9 Initial attach

M1 held on initial attach:

```text
no switch gate
```

### 32.10 Full retirement

After a gate has been armed, successful:

```text
Center M release / explicit retirement / process teardown core
```

must clear stale M1/M2 gate state.

A focused direct retirement test is enough; do not duplicate every release entrypoint.

---

## 33. Do not test pathological instruction-level races

Do not add tests requiring exact interleavings such as:

```text
M1 release on one CPU instruction
exactly between Volatile.Read and mapper call
while a second Steam event enters
```

or:

```text
release and re-press exactly between old detach and post-retire snapshot
```

The supported product contract is deterministic at report/switch boundaries, not at arbitrary CPU instruction boundaries.

Existing owner serialization + publisher stop/join + two volatile release facts are sufficient.

---

## 34. Hardware validation

After automated tests pass, validate on a real MSI Claw.

Use an X360 mapping such as:

```json
"BackButtonMapping": {
  "M1": "A",
  "M2": "RightBumper"
}
```

### 34.1 Xbox360 → SteamDeck held M1

```text
X360 active
hold M1
verify A held
while still holding M1, cause normal Steam/BPM state to select Deck
→ A releases during old neutralization
→ R4 must NOT appear in new Deck presentation

release M1
press M1 again
→ R4 appears
```

Use keyboard/Steam UI to cause the presentation change if that makes it easier to keep M1 continuously held.

### 34.2 SteamDeck → Xbox360 held M1

```text
Deck active
hold M1
verify R4
while still holding M1, exit the Steam/BPM condition
→ R4 releases
→ configured X360 A must NOT appear

release M1
press again
→ A appears
```

### 34.3 Independent M2

Repeat with M2 or hold both and verify independent release/rearm.

### 34.4 Normal quick switch with no rear held

Verify there is no regression to ordinary presentation switching latency or input.

---

## 35. Logging

No per-frame logging.

No per-frame:

```text
M1 suppressed
M2 suppressed
M1 rearmed
```

logs at 250 Hz.

At most, one transition-level debug log when a kind switch arms the gate is acceptable:

```text
Presentation rear-button rearm gate armed
Previous=Xbox360
Next=SteamDeck
M1Held=true
M2Held=false
```

and optionally one debug log when the gate fully clears.

Logging is not required for correctness.

Do not add Info-level spam for individual rear releases.

---

## 36. Failure policy

### Old presentation retirement fails

```text
keep existing ownership evidence
do not attach target
do not arm a new switch gate
```

Use current PR7 failure behavior.

### Target attach fails after gate arm

```text
clear freshly armed gate
leave current existing PR7 failed/no-active state
no fallback
```

### Suppression provider throws

It should be simple enough not to throw.

If an unexpected exception does occur inside a canonical publisher's normal publish operation, existing publisher fault handling remains authoritative.

Do not add a stale-state fallback loop.

### Unknown/short auxiliary state

Treat rear button as not held for gate purposes.

Do not fault the publisher solely because the release gate cannot find a rear slot.

---

## 37. Anti-overengineering constraints

This PR must remain smaller than the feature it protects.

Do not add:

```text
RearButtonManager
BackButtonRuntime
ButtonTransitionCoordinator
new lifecycle owner
new background task
new polling timer
new event subscription to MsiClawInputSource.StateChanged
button edge queue
button history
timestamp continuity proof
transition epoch
generation counter
per-presentation state object
additional SemaphoreSlim
additional lock in 250 Hz path
settings notification/restart mechanism
```

Required architecture is only:

```text
existing MsiClawAddonPresentation
    |
    +-- two suppress-until-release facts
    |
    +-- actual kind-change boundary arms from current raw state
    |
    +-- existing X360 publisher consumes
    |
    +-- existing Deck publisher consumes
```

---

## 38. Non-goals

Explicitly out of scope:

- PR4 Main UI;
- per-game mappings;
- keyboard/mouse/macros;
- firmware M1/M2 profile writes;
- changing X360 target catalog;
- changing live setting-mutation semantics;
- release-to-rearm for ordinary A/B/X/Y;
- global "all buttons must release" policy;
- startup-held-button suppression;
- Overlay controller-navigation release gate;
- new Suspend/Resume policy;
- new physical recovery policy;
- PID1901/PID1902 changes;
- HidHide changes;
- VIIPER attachment policy changes;
- rumble changes;
- WING/OEM1 changes;
- Win+G suppression changes;
- Steam/BPM detection changes;
- frontend transport/schema changes;
- profile schema changes.

---

## 39. Acceptance criteria

PR3 is complete only when all of the following are true.

### Semantic safety

- [ ] X360 M1 held across X360→Deck cannot become R4 until release.
- [ ] X360 M2 held across X360→Deck cannot become L4 until release.
- [ ] Deck M1 held across Deck→X360 cannot become configured X360 target until release.
- [ ] Deck M2 held across Deck→X360 cannot become configured X360 target until release.
- [ ] M1/M2 rearm independently.
- [ ] Next press after release works normally.

### Scope

- [ ] Gate arms only when `previous != desired`.
- [ ] Initial attach does not arm.
- [ ] NoChange does not arm.
- [ ] Same-kind restart does not arm.
- [ ] Live BackButtonMapping mutation does not arm.
- [ ] Overlay same-kind pause/resume does not arm.
- [ ] Suspend same-kind pause/resume receives no new PR3 branch.

### Ownership/lifecycle

- [ ] Existing `MsiClawAddonPresentation._gate` remains the only presentation lifecycle gate.
- [ ] Old publisher is still stopped/joined before detach.
- [ ] Old presentation is still retired before gate sampling/target attach.
- [ ] No target attach occurs after failed retirement.
- [ ] Failed target attach clears freshly armed rear suppression.
- [ ] Successful full retirement clears stale suppression.
- [ ] PID/HidHide/physical ownership are unchanged.

### Output isolation

- [ ] X360 suppression removes only M1/M2 rear contributions.
- [ ] Physical X360 target buttons remain active.
- [ ] X360 triggers preserve physical analog values when rear trigger overlay is suppressed.
- [ ] SteamDeck suppression affects only R4/L4.
- [ ] Steam synthetic/QuickAccess overlay is unchanged.

### Simplicity

- [ ] Only two independent release facts are introduced.
- [ ] No new manager/state machine/epoch/history queue exists.
- [ ] No new per-report lock exists.
- [ ] No new input polling/event subscription exists.

### Contract preservation

- [ ] Frontend protocol remains v38.
- [ ] PR1 settings contract is unchanged.
- [ ] PR2 live mapping semantics remain unchanged outside actual kind switches.
- [ ] SteamDeck still uses native M1→R4 / M2→L4 after rearm.

### Verification

- [ ] Focused X360 mapper tests pass.
- [ ] Focused Deck mapper tests pass.
- [ ] Both canonical publisher tests pass.
- [ ] Presentation switch/rearm tests pass.
- [ ] Existing PR7 switch tests pass.
- [ ] Existing Suspend/Resume tests pass.
- [ ] Existing Overlay pause/resume tests pass.
- [ ] Full test suite passes.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Hardware held-across-switch smoke test passes when practical.

---

## 40. Expected end state after PR3

Normal Xbox360:

```text
raw M1/M2
    ↓
no transition gate pending
    ↓
PR2 X360 mapping
    ↓
configured X360 targets
```

Normal SteamDeck:

```text
raw M1/M2
    ↓
no transition gate pending
    ↓
M1→R4
M2→L4
```

Held across actual kind switch:

```text
M1 held in old presentation
        ↓
old publisher stop/join
        ↓
old device neutral/detach
        ↓
post-retire raw snapshot still says M1 held
        ↓
M1 suppress-until-release = true
        ↓
new presentation attaches neutral
        ↓
new publisher runs
        ↓
M1 contribution suppressed
        ↓
physical M1 release observed
        ↓
gate clears
        ↓
next physical M1 press uses the new presentation meaning
```

The product then has one clear rule:

> **M1/M2 may change meaning between Xbox360 and SteamDeck, but one uninterrupted physical hold can never silently cross that semantic boundary.**

PR4 can then expose the already-complete global X360 M1/M2 setting in the Controller UI without needing to own any lifecycle logic.
