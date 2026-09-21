# Work Order — Full1902 M1/M2 Xbox360 Mapping PR2: Live Software Output Mapping

## Status

Implementation work order for PR2 of the global M1/M2 back-button mapping feature.

Baseline reviewed against current `main`:

```text
5249c3c065d820774355cf0d90773f378d0fdc84
feat: add Full1902 back-button mapping contract (#554)
```

PR1 is merged and already provides:

```text
BackButtonMappingSettings
Xbox360BackButtonTarget
settings.json persistence
StartupSettingsCoordinator.BackButtonMapping
SetBackButtonMapping frontend RPC
Frontend protocol v38
```

This PR connects that existing global setting to the **live Xbox360 output path only**.

Planned feature sequence:

```text
PR1  Contract + persistence + frontend transport foundation      [merged #554]
 ↓
PR2  Xbox360 M1/M2 software mapping                              [this PR]
 ↓
PR3  Xbox360 ↔ SteamDeck held-button release-to-rearm safety
 ↓
PR4  Controller-page UI
```

The application is pre-release. Implement the direct product contract; do not add compatibility layers or a generic remapping framework.

---

## 1. Mandatory architecture context

Read these before implementation:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md

docs/RE_MSI_PID1902_M1_M2_BackButton.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR1_CONTRACT_PERSISTENCE_WORK_ORDER.md
docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md
docs/work-order/FULL1902_SUSPEND_RESUME_NEUTRAL_PRESENTATION_WORK_ORDER.md
```

Current Full1902 authority remains:

```text
Center M Disabled
→ Addon owns physical PID1902
→ one DirectInput source
→ persistent HidHide baseline
→ one canonical VIIPER runtime

Steam/BPM inactive
→ Xbox360 presentation

Steam game or BPM active
→ SteamDeck presentation
```

Steam/BPM changes only the virtual presentation. It must not change physical ownership or the global persisted M1/M2 setting.

---

## 2. Current code facts

### 2.1 Physical source already captures M1/M2

Current physical mapping is already established:

```text
DirectInput Buttons[15]
→ M1
→ ControllerState.Auxiliary[RightRear]

DirectInput Buttons[16]
→ M2
→ ControllerState.Auxiliary[LeftRear]
```

Do not change this.

### 2.2 SteamDeck already owns its own rear-button semantics

Current `SteamDeckDeviceStateMapper` maps:

```text
M1 / RightRear → R4
M2 / LeftRear  → L4
```

Do not route the global Xbox360 setting through the SteamDeck mapper.

### 2.3 Xbox360 currently ignores auxiliary buttons

Current `Xbox360DeviceStateMapper` maps ordinary gamepad buttons/sticks/triggers and ignores `ControllerState.Auxiliary`.

That is the exact boundary PR2 must extend.

### 2.4 Xbox360 publisher is the continuous output path

Current production flow is:

```text
MsiClawInputSource.LatestState
        ↓
CanonicalXbox360InputPublisher
        ↓
Xbox360DeviceStateMapper.Map(...)
        ↓
CanonicalViiperRuntime.SetXbox360State(...)
```

The publisher runs at the existing ~250 Hz / 4 ms schedule.

Do not add a second publication path for M1/M2.

---

## 3. Goal

When Xbox360 presentation is active, map physical M1/M2 presses to the global `BackButtonMappingSettings` target from PR1.

Examples:

```text
Saved:
M1 = A
M2 = RightBumper

physical M1 held
→ virtual Xbox360 A held

physical M2 held
→ virtual Xbox360 RB held
```

When SteamDeck presentation is active:

```text
same saved settings
→ ignored by SteamDeck output
→ M1 remains R4
→ M2 remains L4
```

No firmware remap and no raw-state mutation.

---

## 4. Core design decision: mapping belongs at the Xbox360 projection boundary

Do **not** rewrite `ControllerState`.

Bad:

```text
M1=A setting
→ change raw ControllerState.Buttons.A=true
→ SteamDeck mapper sees fake A
```

Required:

```text
raw ControllerState
        |
        +-----------------------------+
        |                             |
        v                             v
Xbox360DeviceStateMapper        SteamDeckDeviceStateMapper
+ BackButtonMappingSettings     ignores X360 mapping
        |                             |
        v                             v
mapped X360 output              M1→R4 / M2→L4
```

The persisted setting is an output projection rule, not physical input state.

---

## 5. Live setting updates: read current mapping on every Xbox360 publish

PR1 already exposes the current value through:

```csharp
StartupSettingsCoordinator.BackButtonMapping
```

The setting can change at runtime through the existing frontend RPC.

Do not capture the mapping only when the Xbox360 publisher is created.

Required behavior:

```text
publisher tick N
→ read LatestState
→ read current BackButtonMapping
→ map
→ publish

settings mutation

publisher tick N+1
→ read SAME live source
→ read NEW BackButtonMapping
→ map with new setting
```

This means no publisher restart is required for an M1/M2 setting change.

### 5.1 Preferred wiring

Add one narrow provider to the production Xbox360 publisher path:

```csharp
Func<BackButtonMappingSettings>
```

Conceptually:

```csharp
var state = _snapshot.LatestState;
var mapping = _backButtonMappingProvider();
var mapped = Xbox360DeviceStateMapper.Map(state, mapping);
```

Production should bind that provider to the already-existing `StartupSettingsCoordinator` instance.

Conceptually in `AddonProcessHost`:

```csharp
new MsiClawAddonPresentation(
    viiper,
    rumbleSink,
    backButtonMappingProvider: () => startupSettings.BackButtonMapping);
```

Exact parameter placement/naming may differ.

### 5.2 Keep presentation-owner test seams small

Current `MsiClawAddonPresentation` has test-injectable publisher factories.

Do not make every test fake understand the mapping setting if it does not need to.

Preferred structure:

- add one optional/narrow back-button mapping provider to `MsiClawAddonPresentation`;
- default it to `BackButtonMappingSettings.Default` for tests/passive construction;
- only the production default Xbox360 publisher factory captures it;
- keep the existing custom test publisher-factory shape unchanged if practical.

Do not add a `BackButtonManager`.

### 5.3 No new settings event is required

Do not add a change event merely to restart or reconfigure the publisher.

The publisher reads the current immutable settings record on every report.

Do not add:

```text
BackButtonMappingChanged subscriber graph
publisher restart on settings save
mapping generation/epoch
mapping lock
background watcher
```

The current whole-record settings owner remains the one authority.

---

## 6. Xbox360 mapper API

Extend `Xbox360DeviceStateMapper` with a mapping-aware overload.

Preferred shape:

```csharp
internal static Xbox360DeviceState Map(ControllerState state)
    => Map(state, BackButtonMappingSettings.Default);

internal static Xbox360DeviceState Map(
    ControllerState state,
    BackButtonMappingSettings mapping)
{
    ...
}
```

The existing no-settings overload should remain a useful disabled/default projection for tests and any non-production caller that intentionally wants baseline behavior.

Production `CanonicalXbox360InputPublisher` must call the mapping-aware overload.

Do not make the mapper read global settings directly.

It must remain a pure projection:

```text
ControllerState + BackButtonMappingSettings
→ Xbox360DeviceState
```

---

## 7. Additive / OR semantics

M1/M2 mappings must be additive to the physical standard controls.

Never make a rear mapping consume, toggle, or overwrite a physical button.

Example:

```text
physical A = held
M1 target = A
M1 = held
→ virtual A = held

release M1 while physical A is still held
→ virtual A remains held

release physical A
→ virtual A releases
```

Same-target rear mappings are valid:

```text
M1 = A
M2 = A

M1 held, M2 released
→ A held

M1 released, M2 held
→ A held

both held
→ A held

both released
→ A released
```

No reference counting is needed.

Each report is recomputed from current physical state.

---

## 8. Target mapping table

Implement every target already merged in PR1.

### 8.1 Digital button targets

```text
Xbox360BackButtonTarget.A               → Xbox360ButtonBits.A
Xbox360BackButtonTarget.B               → Xbox360ButtonBits.B
Xbox360BackButtonTarget.X               → Xbox360ButtonBits.X
Xbox360BackButtonTarget.Y               → Xbox360ButtonBits.Y

DPadUp                                  → DPadUp
DPadRight                               → DPadRight
DPadDown                                → DPadDown
DPadLeft                                → DPadLeft

LeftBumper                              → LeftShoulder
RightBumper                             → RightShoulder

LeftStickClick                          → LeftThumb
RightStickClick                         → RightThumb

View                                    → Back
Menu                                    → Start

XboxGuide                               → Guide
```

`Disabled` adds nothing.

### 8.2 Trigger targets

Xbox360 trigger state is analog byte state, not a button bit.

Required:

```text
M1/M2 target = LeftTrigger
+ rear button held
→ LT = 255

M1/M2 target = RightTrigger
+ rear button held
→ RT = 255
```

This is additive to the physical analog trigger.

Example:

```text
physical LT = 93
M1 target = LeftTrigger
M1 released
→ LT = 93

M1 held
→ LT = 255

M1 released again
→ LT = current physical value
```

Do not mutate `GamepadButtons.LeftTriggerFull` / `RightTriggerFull`.

The X360 ABI already carries LT/RT as analog bytes.

### 8.3 Guide semantics

PR1 already merged `XboxGuide` as a valid explicit X360 target and the VIIPER ABI exposes `Xbox360ButtonBits.Guide`.

For PR2, treat it like the other digital X360 targets:

```text
rear held
→ Guide bit held
rear released
→ Guide bit released
```

Do not invent tap/pulse timing or a second Guide dispatcher in this PR.

Do not change the existing WING/native Win+G suppression authority. That policy remains independently owned by the existing Full1902 guard.

---

## 9. Read M1/M2 from the existing auxiliary slots

The current canonical identity is:

```text
AuxiliaryButtonSlot.LeftRear  = M2
AuxiliaryButtonSlot.RightRear = M1
```

Use those semantic enum values.

Do not hard-code raw DirectInput indices 15/16 in the Xbox360 mapper.

Conceptually:

```csharp
var m1Pressed = IsPressed(state.Auxiliary, AuxiliaryButtonSlot.RightRear);
var m2Pressed = IsPressed(state.Auxiliary, AuxiliaryButtonSlot.LeftRear);
```

### 9.1 Neutral/default safety

The production `MsiClawInputSource` neutral snapshot currently allocates the complete M1/M2 auxiliary shape, so normal Suspend/Resume and physical-source cleanup are already safe.

Still, the pure mapper should not needlessly crash if it receives a `default(ControllerState)` or another auxiliary state with fewer slots.

Use a tiny bounds check:

```csharp
private static bool IsPressed(AuxiliaryButtonState state, AuxiliaryButtonSlot slot)
{
    var index = (int)slot;
    return index >= 0 && index < state.Count && state[index];
}
```

Equivalent simple code is fine.

Do not alter `AuxiliaryButtonState` just for this.

---

## 10. Unknown target safety

PR1 validates persisted/direct mutations, so production should only see defined targets.

Nevertheless the output mapper is a 250 Hz fail-close boundary. An unknown enum value should not crash the publisher.

Required:

```text
unknown Xbox360BackButtonTarget
→ add no output
→ no per-frame exception
→ no per-frame log
```

Do not add repeated runtime validation/logging to every report.

A switch `default` that performs no mapping is sufficient.

Persistence/frontend validation remains the user-input validation authority.

---

## 11. CanonicalXbox360InputPublisher changes

Add the narrow mapping provider as an optional/defaulted constructor dependency so existing tests remain simple.

One acceptable shape:

```csharp
private readonly Func<BackButtonMappingSettings> _backButtonMappingProvider;

internal CanonicalXbox360InputPublisher(
    IControllerStateSnapshotSource snapshot,
    Func<Xbox360DeviceState, bool> setState,
    IInputReportTickSource? ticks = null,
    Action<Exception>? fault = null,
    Func<long>? timestampProvider = null,
    Func<BackButtonMappingSettings>? backButtonMappingProvider = null)
{
    ...
    _backButtonMappingProvider =
        backButtonMappingProvider ?? static () => BackButtonMappingSettings.Default;
}
```

Exact argument order may differ; use named arguments at production call sites where helpful.

Then the one shared publish operation becomes conceptually:

```csharp
private bool PublishCurrentStateOnce()
{
    var mapped = Xbox360DeviceStateMapper.Map(
        _snapshot.LatestState,
        _backButtonMappingProvider());

    ...
}
```

Do not modify:

- 4 ms cadence;
- worker QoS;
- timer scheduling;
- stop/join semantics;
- sink rejection behavior;
- fault callback behavior;
- neutral ownership;
- attachment ownership.

No mapping code belongs in the worker/timer infrastructure beyond reading the provider and calling the mapper.

---

## 12. MsiClawAddonPresentation wiring

The presentation owner should only carry the narrow provider far enough to construct its default Xbox360 publisher.

It must not become the mapping engine.

Conceptually:

```text
MsiClawAddonPresentation
    owns presentation lifecycle
    |
    +-- default Xbox360 publisher factory
            |
            +-- current back-button mapping provider
```

Do not change:

- desired presentation policy;
- attach/detach ordering;
- publisher retirement;
- overlay pause;
- suspend pause;
- rumble callback lifetime;
- SteamDeck session;
- publisher fault cleanup.

PR2 is not a lifecycle rewrite.

---

## 13. AddonProcessHost production binding

The current Disabled-mode startup method already receives the exact Runtime-owned:

```text
StartupSettingsCoordinator startupSettings
```

Use that same instance.

Production binding should be equivalent to:

```text
startupSettings.BackButtonMapping
        ↓
Func<BackButtonMappingSettings>
        ↓
MsiClawAddonPresentation
        ↓
CanonicalXbox360InputPublisher
        ↓
Xbox360DeviceStateMapper
```

Do not fetch settings from disk on the publisher thread.

Do not create another `SettingsStore`.

Do not copy the initial value into a second mutable field.

---

## 14. Runtime setting mutation behavior

PR1 already exposes:

```text
SetBackButtonMappingAsync(...)
→ StartupSettingsCoordinator.ChangeBackButtonMapping(...)
→ settings.json
→ current AppSettings
```

After PR2, a successful mutation while Xbox360 is active must be visible on a subsequent publisher report without:

- detach;
- attach;
- neutral cycle;
- publisher restart;
- PID switch;
- DirectInput reacquire.

Example:

```text
current:
M1 = A
M1 physically held
→ report A

change setting:
M1 = B

next normal report:
→ A no longer added by M1
→ B added by M1
```

This immediate live re-projection is intentional for PR2.

Do **not** add release-to-rearm for a settings mutation.

PR3 is specifically responsible for presentation-boundary safety.

---

## 15. PR3 boundary: do not implement held-across-presentation switching here

A real remaining scenario after PR2 will be:

```text
Xbox360 active
M1=A
M1 physically held
        ↓
presentation switches to SteamDeck
        ↓
old X360 output neutralized
        ↓
same physical hold could become R4 on first SteamDeck report
```

and the inverse:

```text
SteamDeck M1=R4 held
→ switch to Xbox360
→ same hold could become configured A
```

That is intentionally PR3.

PR2 must preserve all existing X360 ↔ SteamDeck switching/neutralization logic and must **not** add:

- rear-button armed state;
- release gates;
- epochs;
- switch barriers;
- new presentation states.

The only PR2 behavior is mapping current raw state to current X360 output while X360 publication is already active.

---

## 16. Suspend/Resume and device-loss behavior

Do not add special suspend logic for M1/M2.

Existing lifecycle already owns:

```text
Suspend
→ stop/join publisher
→ neutral attached virtual device

Resume
→ reset LatestState to neutral
→ safely restart/reconcile publication

physical input loss
→ latest state neutral / publisher safety
→ existing recovery path
```

Because PR2 mapping is stateless and recomputed per report:

```text
neutral raw rear buttons
→ no mapped output
```

Required regression coverage should prove this, but do not add new lifecycle production code.

---

## 17. SteamDeck isolation

Do not modify:

```text
SteamDeckDeviceStateMapper
CanonicalSteamDeckInputPublisher
SteamDeckSystemButtonOverlay
ICanonicalSteamDeckSession
```

The global X360 mapping must have no path into SteamDeck output.

Keep or extend tests proving:

```text
saved M1=A / M2=RB
+ SteamDeck presentation
→ M1 still R4
→ M2 still L4
```

This can be proven at mapper/presentation composition level without changing SteamDeck code.

---

## 18. No protocol or persistence change

PR1 already completed the settings/frontend contract.

PR2 should not modify:

```text
BackButtonMapping.cs enum shape
SettingsStore schema
FrontendSettingsSnapshot schema
FrontendBootstrapSnapshot schema
FrontendRpcMethod
FrontendTransportProtocol.CurrentVersion
NamedPipeAddonFrontendClient/Server
Controller UI
```

Expected frontend protocol remains:

```text
v38
```

If implementation reveals an actual contract defect, fix it only with clear evidence; do not casually broaden PR2.

---

## 19. No firmware/profile work

Do not touch the MSI vendor-HID M1/M2 profile slots.

No:

```text
WriteProfile
SyncToROM
0x00BA
0x0163
firmware action codes
```

The physical controller remains in its existing PID1902 input behavior.

This feature is a virtual X360 software overlay only.

---

## 20. No per-game profile work

Do not modify:

```text
ProfileStore
GameProfile
GameProfileMutations
active-game profile reconciliation
Profile UI
```

The one global persisted M1/M2 mapping remains authoritative.

---

## 21. Expected production touch set

Expected production files:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Possibly only namespace imports in adjacent files.

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/Xbox360DeviceStateMapperTests.cs
tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
```

A focused new test file for back-button output mapping is acceptable if it keeps the existing mapper test file readable.

Do not modify unrelated settings/frontend tests merely because PR1 touched them.

---

## 22. Required mapper tests

### 22.1 Disabled is current behavior

```text
M1 pressed
M2 pressed
mapping = Disabled/Disabled
→ no rear-derived X360 output
```

Ordinary physical controls still map normally.

### 22.2 M1 digital target

At minimum:

```text
M1 pressed
M1 target = A
→ A bit set
```

### 22.3 M2 digital target

At minimum:

```text
M2 pressed
M2 target = RightBumper
→ RightShoulder bit set
```

### 22.4 Every digital enum target

Table-drive every digital target from PR1 and prove the exact expected X360 bit:

```text
A/B/X/Y
DPad Up/Right/Down/Left
LB/RB
L3/R3
View/Back
Menu/Start
XboxGuide/Guide
```

### 22.5 Trigger targets

Prove:

```text
M1→LT held → LT=255
M2→RT held → RT=255
```

Also prove physical analog trigger restoration/additivity:

```text
physical LT=91 + M1→LT released → 91
physical LT=91 + M1→LT held     → 255
```

### 22.6 Physical + mapped same target OR

```text
physical A held
M1→A held
→ A held

release M1 only
→ A still held
```

This is naturally demonstrated by separate stateless input frames.

### 22.7 M1 + M2 same target OR

```text
M1=A
M2=A
either held → A
both held   → A
none held   → no rear-derived A
```

### 22.8 Independent simultaneous targets

Example:

```text
M1=A
M2=RightBumper
both held
→ A | RightShoulder
```

### 22.9 Default ControllerState is safe

```text
ControllerState default
+ non-default mapping
→ no exception
→ neutral rear-derived output
```

This pins the tiny auxiliary bounds check.

### 22.10 Unknown target fails neutral

Construct an invalid enum directly in the test:

```text
M1=(Xbox360BackButtonTarget)999
M1 pressed
→ no rear-derived output
→ no exception
```

Do not log in this mapper test.

---

## 23. Required publisher tests

### 23.1 Current mapping is read per tick

Use a mutable test provider.

Example sequence:

```text
raw M1 held
provider returns M1=A
tick
→ A

provider changes to M1=B
next tick
→ B
→ A no longer added
```

Prove the publisher did not restart.

### 23.2 Current raw state and current mapping are both fresh

Prove combinations such as:

```text
tick 1: raw M1 held, mapping A
tick 2: raw M1 released, mapping B
```

Each report must reflect that tick's current inputs, not constructor-time snapshots.

### 23.3 Default provider preserves baseline

Construct publisher without a mapping provider.

M1/M2 must behave as Disabled/Disabled.

This preserves simple existing test construction.

### 23.4 Sink failure behavior unchanged

Existing reject/throw/fault-once tests must continue passing with a mapping provider.

Do not change publisher fault semantics.

---

## 24. Required production-wiring tests

Add the smallest practical test proving the production presentation path passes a live provider to the default Xbox360 publisher.

Do not attempt to unit-test the real VIIPER native device just for this feature.

Acceptable proof may be:

- a focused constructor/factory seam test; or
- an `AddonProcessHost` structure/behavior test if an existing constructable seam already exists.

The important contract is:

```text
production StartupSettingsCoordinator
→ current BackButtonMapping
→ production Xbox360 publisher
```

Avoid brittle reflection/source-text assertions when a behavior test is practical.

---

## 25. Required SteamDeck regression tests

Existing test must remain true:

```text
M2 → L4
M1 → R4
```

Add no X360 mapping parameter to `SteamDeckDeviceStateMapper`.

Do not make SteamDeck tests depend on `BackButtonMappingSettings`.

---

## 26. Hardware validation

After automated tests pass, validate on a real MSI Claw if available.

### 26.1 Prepare a mapping before UI exists

Since PR4 UI does not exist yet, use the persisted PR1 setting while the app is stopped, for example:

```json
"BackButtonMapping": {
  "M1": "A",
  "M2": "RightBumper"
}
```

Then start the Runtime normally.

Do not write a debug-only UI just for PR2 validation.

### 26.2 Xbox360 presentation

With Steam/BPM inactive:

```text
M1
→ virtual A

M2
→ virtual RB
```

Verify ordinary physical A/RB still work.

Verify simultaneous physical + mapped input does not cancel either source.

### 26.3 SteamDeck presentation

Enter Steam/BPM so normal PR7 switching selects SteamDeck.

Expected:

```text
saved M1=A is ignored
M1 → R4

saved M2=RB is ignored
M2 → L4
```

Do not yet use a held-across-switch test as PR2 acceptance; that is PR3's explicit scope.

### 26.4 Return to Xbox360

After Steam/BPM exits:

```text
Xbox360 presentation active
→ saved M1=A / M2=RB mapping is effective again
```

This validates that mapping is global and presentation-specific, not consumed or rewritten during SteamDeck mode.

### 26.5 Trigger smoke test

Temporarily configure one rear button as `LeftTrigger`.

Verify the virtual X360 target reports a full trigger press while held and releases back to the physical trigger value afterward.

---

## 27. Logging

No per-report or per-button logging.

The X360 publisher runs at ~250 Hz.

Do not add logs such as:

```text
M1 mapped to A
M2 mapped to RB
```

on every publish.

PR1 already logs settings load/save.

If one production composition log is useful, it may state that Xbox360 rear mapping is enabled by the current settings provider, but it must not be required for correctness.

Prefer no new production log unless it materially helps hardware validation.

---

## 28. Failure policy

### Mapping provider throws

This is part of the publisher's normal publish operation.

If the provider unexpectedly throws, existing publisher fault handling should fail the presentation closed exactly like another publish-path exception.

Do not swallow arbitrary provider exceptions and continue publishing stale output.

### Invalid enum somehow reaches mapper

Fail neutral for that rear target.

Do not throw per frame.

### VIIPER rejects mapped state

Use existing publisher sink-rejection fault path.

No special M1/M2 retry.

### Physical source disappears

Existing physical-input recovery owns it.

No back-button-specific recovery.

---

## 29. Anti-overengineering constraints

Do not add:

```text
BackButtonManager
BackButtonRuntime
ControllerLayoutManager
generic remap action hierarchy
mapping action interface
mapping pipeline
rear-button event queue
rear-button reference counter
mapping generation
mapping epoch
settings-change publisher restart
new lock around every report
new worker/thread/timer
new VIIPER wrapper
new physical-input owner
```

Required architecture is only:

```text
existing Settings owner
→ narrow Func<BackButtonMappingSettings>
→ existing Xbox360 publisher
→ pure Xbox360 mapper
```

The mapper itself remains stateless.

---

## 30. Non-goals

Explicitly out of scope:

- Controller-page UI;
- per-game M1/M2 profiles;
- keyboard mapping;
- mouse mapping;
- macro mapping;
- stick-direction mapping;
- firmware/EEPROM M1/M2 remapping;
- changing DirectInput indices;
- changing raw `ControllerState`;
- changing SteamDeck R4/L4 semantics;
- presentation release-to-rearm;
- any new presentation state;
- Suspend/Resume production changes;
- PnP/device-loss production changes;
- PID1901/PID1902 changes;
- HidHide changes;
- VIIPER attach/detach changes;
- rumble changes;
- WING/OEM1 changes;
- Win+G suppression changes;
- frontend protocol changes;
- persistence schema changes.

---

## 31. Acceptance criteria

PR2 is complete only when all of the following are true.

### Mapping

- [ ] M1 uses `RightRear`; M2 uses `LeftRear`.
- [ ] Disabled produces no rear-derived X360 output.
- [ ] Every PR1 digital target maps to the correct X360 bit.
- [ ] LeftTrigger / RightTrigger map to analog 255 while held.
- [ ] Physical standard controls remain unchanged.
- [ ] Physical and mapped sources combine additively.
- [ ] M1/M2 may map to the same target with OR semantics.
- [ ] Unknown enum values fail neutral without throwing.
- [ ] Default/short auxiliary state does not throw.

### Live runtime

- [ ] Production Xbox360 publisher receives the current global mapping through one narrow provider.
- [ ] Publisher reads the current mapping on each normal report.
- [ ] A successful settings mutation becomes effective without publisher restart.
- [ ] No second settings authority or mapping cache is introduced.

### Presentation isolation

- [ ] Xbox360 applies the global mapping.
- [ ] SteamDeck ignores the global X360 mapping.
- [ ] SteamDeck still maps M1→R4 and M2→L4.
- [ ] Existing X360↔SteamDeck switching code is unchanged.
- [ ] No release-to-rearm state is added in PR2.

### Lifecycle preservation

- [ ] Suspend/Resume production code is unchanged.
- [ ] Physical device-loss/PnP recovery production code is unchanged.
- [ ] PID1902/HidHide ownership is unchanged.
- [ ] VIIPER lifecycle/attachment policy is unchanged.
- [ ] Rumble behavior is unchanged.

### Contract preservation

- [ ] Frontend protocol remains v38.
- [ ] PR1 settings schema is unchanged.
- [ ] No profile schema is added.
- [ ] No firmware M1/M2 write exists.

### Verification

- [ ] Focused mapper tests pass.
- [ ] Focused publisher live-mapping tests pass.
- [ ] Presentation regression tests pass.
- [ ] SteamDeck mapper tests pass.
- [ ] Full test suite passes.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Hardware smoke test confirms X360 mapping when practical.

---

## 32. Expected end state after PR2

```text
settings.json
    |
    v
StartupSettingsCoordinator.BackButtonMapping
    |
    | current value, read live
    v
MsiClawAddonPresentation
    |
    v
CanonicalXbox360InputPublisher
    |
    +---- Latest ControllerState
    |
    +---- current BackButtonMappingSettings
    |
    v
Xbox360DeviceStateMapper
    |
    +-- normal physical X360 controls
    +-- M1 X360 target overlay
    +-- M2 X360 target overlay
    |
    v
Canonical VIIPER Xbox360 device
```

SteamDeck remains independent:

```text
ControllerState
    |
    v
SteamDeckDeviceStateMapper
    |
    +-- M1 → R4
    +-- M2 → L4
    |
    v
Canonical VIIPER SteamDeck device
```

The only intentionally unfinished behavior after PR2 is the semantic handoff of an M1/M2 button that remains physically held across an actual X360 ↔ SteamDeck presentation switch.

PR3 will solve that with the smallest release-to-rearm gate at the existing presentation-owner boundary, without creating a new controller authority.
