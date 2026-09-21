# Work Order — Full1902 M1/M2 Xbox360 Mapping PR1: Contract, Persistence, and Frontend Foundation

## Status

Implementation work order for PR1 of the global M1/M2 back-button mapping feature.

Baseline reviewed against current `main`:

```text
a6682acb3651cdd488fd78c0a2a3ed859b6c97df
Add Steam BPM Windows FSE toggle (#553)
```

This PR is **foundation only**. It must not change any controller output behavior.

Planned sequence:

```text
PR1  Contract + persistence + frontend transport foundation      [this PR]
 ↓
PR2  Xbox360 M1/M2 software mapping
 ↓
PR3  Xbox360 ↔ SteamDeck held-button release-to-rearm safety
 ↓
PR4  Controller-page UI
```

The application is pre-release. Do not add compatibility shims for an unreleased back-button schema.

---

## 1. Mandatory architecture context

Read these documents before implementation:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/RE_MSI_PID1902_M1_M2_BackButton.md
```

Relevant current Full1902 policy:

```text
Center M Disabled
→ physical PID1902 owned by Addon Runtime
→ one DirectInput source
→ one canonical VIIPER runtime

Steam/BPM inactive
→ Xbox360 presentation

Steam game or BPM active
→ SteamDeck presentation
```

M1/M2 are already captured from the physical PID1902 DirectInput source:

```text
Buttons[15] = M1
Buttons[16] = M2
        ↓
MsiClawControllerStateMapper
        ↓
ControllerState.Auxiliary
        ↓
M1 = RightRear
M2 = LeftRear
```

Current virtual behavior is intentionally split:

```text
Xbox360DeviceStateMapper
→ M1/M2 currently ignored

SteamDeckDeviceStateMapper
→ M1 → R4
→ M2 → L4
```

PR1 must preserve all of that behavior exactly.

---

## 2. Goal

Introduce one global persisted contract describing what M1 and M2 should become **when the active Full1902 presentation is Xbox360**, and expose that setting through the existing Runtime-owned frontend transport.

After PR1, the application must be capable of storing and transporting:

```text
M1 = Disabled | Xbox360 target
M2 = Disabled | Xbox360 target
```

but pressing M1/M2 must still produce exactly the same controller output as current `main`.

In particular:

```text
PR1 does NOT make M1=A work yet.
PR1 does NOT alter SteamDeck R4/L4 behavior.
PR1 does NOT add held-button transition handling.
PR1 does NOT add UI.
```

Those belong to later PRs.

---

## 3. Product contract

The mapping is:

- global;
- current-user persisted;
- independent of game profiles;
- relevant only to Xbox360 presentation;
- software-only;
- not an MSI firmware/EEPROM remap;
- not a replacement for the raw M1/M2 physical state;
- not a second controller authority.

Example future behavior, implemented only in PR2+:

```text
Saved:
M1 = A
M2 = RightBumper

Xbox360 presentation:
M1 → A
M2 → RB

SteamDeck presentation:
M1 → R4
M2 → L4
```

The saved Xbox360 mapping must never redefine the physical meaning of `ControllerState.Auxiliary`.

---

## 4. Do not merge this into FrontButtonMappingSettings

Current `FrontButtonMappingSettings` owns a different product concept:

```text
Gamebar Button
Center M Button
×
Normal / Steam Game-Big Picture action domains
```

M1/M2 are not another pair of front-button action-domain bindings.

They are rear physical controls whose future mapping is an Xbox360 gamepad projection.

Therefore create a separate contract, for example under:

```text
src/SteamInputAddonforClaw.Contracts/BackButtons/
```

Do not expand `FrontButtonMappingSettings` with M1/M2 members.

Do not add Normal/Steam domains to the M1/M2 setting.

---

## 5. New contract

Preferred shape:

```csharp
namespace SteamInputAddonforClaw.Contracts.BackButtons;

public enum Xbox360BackButtonTarget
{
    Disabled = 0,

    A,
    B,
    X,
    Y,

    DPadUp,
    DPadRight,
    DPadDown,
    DPadLeft,

    LeftBumper,
    RightBumper,

    LeftTrigger,
    RightTrigger,

    LeftStickClick,
    RightStickClick,

    View,
    Menu,

    XboxGuide,
}

public sealed record BackButtonMappingSettings(
    Xbox360BackButtonTarget M1,
    Xbox360BackButtonTarget M2)
{
    public static BackButtonMappingSettings Default { get; } =
        new(Xbox360BackButtonTarget.Disabled, Xbox360BackButtonTarget.Disabled);
}
```

Equivalent naming is acceptable if it remains narrow and explicit that these are Xbox360 targets.

### 5.1 Deliberately excluded from PR1

Do not add:

- left-stick directional targets;
- right-stick directional targets;
- keyboard;
- mouse;
- macro;
- application launch;
- Quick Settings actions;
- Steam/QAM actions;
- per-game override;
- firmware paddle target codes.

The initial contract should cover ordinary X360 controls only.

A later feature can extend the enum if a concrete product requirement exists.

### 5.2 M1/M2 duplicate targets are valid

Unlike the front-button mapping, M1 and M2 are allowed to target the same X360 control.

This must be valid:

```text
M1 = A
M2 = A
```

PR2 will use additive/OR semantics so either physical rear button can assert the same virtual control.

Do not copy the `FrontButtonMappingSettings` same-domain uniqueness rule.

---

## 6. Validation

Add one small shared validation policy in the BackButtons contract area.

Conceptually:

```csharp
public static class BackButtonMappingValidation
{
    public static bool IsValid(BackButtonMappingSettings? mapping)
        => Validate(mapping) is null;

    public static string? Validate(BackButtonMappingSettings? mapping)
    {
        if (mapping is null)
            return "MappingMissing";

        if (!Enum.IsDefined(mapping.M1))
            return "InvalidM1Target";

        if (!Enum.IsDefined(mapping.M2))
            return "InvalidM2Target";

        return null;
    }
}
```

Exact reason names may differ.

Required rules:

- null mapping is invalid;
- undefined/numeric-out-of-range M1 is invalid;
- undefined/numeric-out-of-range M2 is invalid;
- `Disabled` is valid;
- both buttons using the same valid target is valid;
- no presentation/runtime state is consulted during validation.

Do not create a generic mapping framework.

---

## 7. AppSettings persistence

Extend the existing Runtime-owned `AppSettings`.

Preferred shape:

```csharp
public BackButtonMappingSettings BackButtonMapping { get; init; }
    = BackButtonMappingSettings.Default;
```

Use the existing `settings.json`.

Do not create:

- a second settings file;
- a registry setting;
- a profile-store record;
- a controller-specific cache file.

The setting belongs beside the existing global controller preferences.

---

## 8. SettingsStore load policy

Add a feature-isolated reader equivalent in spirit to `ReadFrontButtonMapping(...)`, for example:

```csharp
private static BackButtonMappingSettings ReadBackButtonMapping(JsonElement root)
```

Important: because `Disabled == 0`, ordinary deserialization of a missing enum member could accidentally make an incomplete object look valid.

Therefore the reader must explicitly prove that both required members exist.

Required persisted shape:

```json
{
  "BackButtonMapping": {
    "M1": "Disabled",
    "M2": "Disabled"
  }
}
```

Loading must require:

- `BackButtonMapping` exists and is an object;
- `M1` exists;
- `M2` exists;
- each value is a string enum name;
- numeric enum values are rejected;
- unknown names are rejected;
- validation succeeds.

If any of those checks fail:

```text
only BackButtonMapping
→ BackButtonMappingSettings.Default
```

Unrelated settings must remain loaded normally.

Examples:

```text
missing BackButtonMapping
→ M1=Disabled, M2=Disabled

missing M1
→ whole BackButtonMapping defaults

unknown M2 target
→ whole BackButtonMapping defaults

numeric M1 target
→ whole BackButtonMapping defaults

malformed BackButtonMapping object
→ whole BackButtonMapping defaults
```

Do not reset LogLevel, FrontButtonMapping, OverlayTabOrder, battery settings, or other unrelated preferences because this feature's value is malformed.

### 8.1 Pre-release migration policy

There is no previous shipped BackButtonMapping schema.

Do not add migration code from:

- HHC;
- ClawTweaks;
- MSI Center M;
- firmware EEPROM values;
- hypothetical old Addon keys.

Absent data simply means `BackButtonMappingSettings.Default`.

---

## 9. SettingsStore save policy

Extend the one existing payload written by `SettingsStore.Save(...)`.

The new member must round-trip as enum names through the current `JsonStringEnumConverter` policy.

Conceptually:

```csharp
var payload = new
{
    ...,
    settings.FrontButtonMapping,
    settings.BackButtonMapping,
    ...
};
```

Do not add a special BackButton JSON writer unless the current serializer cannot satisfy the required string-enum output.

Keep the existing temporary-file + overwrite save path.

---

## 10. StartupSettingsCoordinator

Expose the current value from the existing settings owner:

```csharp
public BackButtonMappingSettings BackButtonMapping
    => Settings.BackButtonMapping;
```

Add one whole-record mutation, for example:

```csharp
public bool ChangeBackButtonMapping(BackButtonMappingSettings mapping)
```

Required mutation policy:

```text
validate candidate
→ invalid: reject, no write, Settings unchanged

candidate == current
→ accepted no-op

valid changed candidate
→ build next AppSettings
→ save next
→ only after save succeeds assign Settings = next
```

This is the same save-then-current principle already used by current settings mutations.

Do not silently repair an invalid target into `Disabled` on a direct mutation request. Load-time malformed persistence falls back to default; mutation-time invalid input is rejected.

### 10.1 No unused runtime abstraction in PR1

Do not add a new:

```text
BackButtonManager
BackButtonRuntime
BackButtonMappingService
BackButtonOutputCoordinator
```

PR1 has no output consumer.

A narrow runtime read interface/event should be introduced in PR2 only if the real publisher/mapper integration demonstrates it is needed.

---

## 11. Frontend settings contract

Expose the same `BackButtonMappingSettings` type through `FrontendSettingsSnapshot`.

Do not create a second frontend-shaped copy of the mapping enum/record.

Preferred low-churn shape:

```csharp
public sealed record FrontendSettingsSnapshot(...)
{
    ...
    public BackButtonMappingSettings BackButtonMapping { get; init; }
        = BackButtonMappingSettings.Default;
}
```

Then `InProcessAddonFrontendControl.MapSettings()` must populate the actual persisted value.

A fresh bootstrap must therefore carry the current M1/M2 mapping before PR4 UI exists.

---

## 12. Frontend mutation RPC

Add one whole-record RPC:

```csharp
Task<FrontendSettingsSnapshot> SetBackButtonMappingAsync(
    BackButtonMappingSettings mapping,
    CancellationToken cancellationToken = default);
```

Use one request record:

```csharp
internal sealed record SetBackButtonMappingRequest(
    BackButtonMappingSettings Mapping);
```

and one enum method:

```text
FrontendRpcMethod.SetBackButtonMapping
```

Required in-process behavior:

```text
SetBackButtonMappingAsync(candidate)
→ ThrowIfShuttingDown()
→ ChangeBackButtonMapping(candidate)
→ StateInvalidated
→ return MapSettings()
```

If validation rejects the candidate, the returned settings snapshot must reflect the unchanged persisted mapping.

Do not create separate:

```text
SetM1Mapping
SetM2Mapping
```

RPCs.

The pair is one atomic user setting.

---

## 13. Named-pipe transport

Wire the new RPC through the current transport:

```text
IAddonFrontendControl
NamedPipeAddonFrontendClient
FrontendRpcMethod
SetBackButtonMappingRequest
NamedPipeAddonFrontendServer
InProcessAddonFrontendControl
```

Do not invent a second pipe or transport endpoint.

No QAM-specific or Overlay-specific pipe contract is needed for this PR.

Any test fake implementing `IAddonFrontendControl` that requires a method stub should be updated mechanically.

---

## 14. Frontend protocol version

Current reviewed baseline:

```text
FrontendTransportProtocol.CurrentVersion = 37
```

PR1 adds:

- a new settings-snapshot member;
- a new mutation RPC;
- a new request payload.

Bump exactly once:

```text
37 → 38
```

Add the version history comment explaining the new BackButtonMapping contract.

Pre-release policy:

```text
v37 peer
→ fail handshake against v38
```

Do not add a v37 compatibility shim.

---

## 15. Back-button availability fact

The future Controller UI should not infer capability from Steam/BPM state or current presentation.

Add one bootstrap capability fact:

```text
FrontendBootstrapSnapshot.BackButtonMappingAvailable
```

Its meaning:

> the detected machine is a supported MSI Claw for which the Full1902 physical input contract includes M1/M2.

For the current supported product scope this should use the same startup hardware-support fact already used to establish `FrontButtonMappingAvailable`.

Do not add another hardware probe.

Do not gate it on:

- current Xbox360 vs SteamDeck presentation;
- Steam running;
- BPM;
- current PID momentarily being missing during PnP recovery;
- HidHide health;
- VIIPER attachment state.

It is a stable hardware/product capability fact for the current process bootstrap.

To minimize constructor churn, an init-only property on `FrontendBootstrapSnapshot` is acceptable if consistent with current contract style.

---

## 16. Explicitly preserve physical input semantics

Do not modify:

```text
src/SteamInputAddonforClaw/Input/ControllerState.cs
src/SteamInputAddonforClaw/Input/AuxiliaryButtonState.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawControllerStateMapper.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
```

Current physical mapping remains:

```text
M1 physical press
→ ControllerState.Auxiliary[RightRear]

M2 physical press
→ ControllerState.Auxiliary[LeftRear]
```

The saved target must not be applied at this layer.

PR2 will consume the raw state at the X360 output boundary.

---

## 17. Explicitly preserve virtual output behavior

Do not modify:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs
```

After PR1:

```text
Xbox360:
M1/M2 still have no effect

SteamDeck:
M1 → R4 unchanged
M2 → L4 unchanged
```

Keep the existing Xbox360 test that proves auxiliary M1/M2 do not affect the X360 result.

That test should change only in PR2.

---

## 18. Explicitly preserve presentation/lifecycle ownership

Do not modify M1/M2 behavior in:

```text
MsiClawAddonPresentation
AddonProcessHost controller presentation reconcile
Suspend/Resume presentation pause
physical-input recovery
PnP recovery
HidHide
PID1901/PID1902 switching
VIIPER attach/detach
rumble feedback
```

PR3 owns held-button release-to-rearm behavior across X360 ↔ SteamDeck transitions.

PR1 must not pre-build that lifecycle state.

---

## 19. No firmware remapping

The repository research establishes a separate MSI vendor-HID EEPROM/profile path for M1/M2.

That path is **not** this feature.

Do not issue:

```text
WriteProfile
SyncToROM
M1 slot write
M2 slot write
```

from settings load or mutation.

Do not change the firmware profile to A/B/etc.

Reason:

```text
firmware remap
→ persists below the Addon's presentation boundary
→ can affect behavior outside the intended X360-only software projection
```

The desired product contract is software mapping at the virtual X360 output layer, implemented in PR2.

---

## 20. No per-game profile work

Do not modify:

```text
ProfileStore
GameProfile
GameProfileMutations
active-game reconcile
Profile UI
```

There is intentionally one global M1 mapping and one global M2 mapping.

Per-game overrides are a future decision only if a concrete need is established.

---

## 21. Expected production touch set

Expected new file:

```text
src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs
```

Expected existing production files:

```text
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
```

Additional compile-only updates to tests/fakes implementing `IAddonFrontendControl` are expected.

Do not broaden the PR because nearby controller code looks convenient to change.

---

## 22. Required tests

### 22.1 Contract defaults

Prove:

```text
BackButtonMappingSettings.Default.M1 == Disabled
BackButtonMappingSettings.Default.M2 == Disabled
```

### 22.2 Validation

Cover:

```text
null                         → invalid
M1 undefined enum           → invalid
M2 undefined enum           → invalid
M1 Disabled / M2 Disabled   → valid
M1 A / M2 RB                → valid
M1 A / M2 A                 → valid
```

The last case is important: duplicates are intentionally permitted.

### 22.3 Settings missing value

Given an existing settings file with no `BackButtonMapping`:

```text
Load
→ BackButtonMappingSettings.Default
→ unrelated settings preserved
```

### 22.4 Settings round-trip

Save a non-default mapping, for example:

```text
M1 = A
M2 = RightBumper
```

Reload and prove exact equality.

Also assert persisted JSON uses enum names, not integer values.

### 22.5 Malformed feature isolation

Cover at least:

```text
BackButtonMapping = string
missing M1
missing M2
unknown M1
unknown M2
numeric M1
numeric M2
```

Each must:

```text
default only BackButtonMapping
preserve unrelated setting(s)
```

### 22.6 Coordinator mutation

Prove:

```text
valid changed mapping
→ saved
→ current Settings updated

same mapping
→ accepted no-op

invalid mapping
→ false/rejected
→ no file mutation
→ Settings unchanged
```

Do not require an output-runtime notification in PR1.

### 22.7 Frontend settings snapshot

Prove `FrontendSettingsSnapshot` JSON round-trips a non-default `BackButtonMapping`.

### 22.8 Bootstrap capability

Prove `BackButtonMappingAvailable` round-trips and reflects the supplied startup hardware-support fact.

### 22.9 In-process RPC

Given a valid mapping:

```text
SetBackButtonMappingAsync
→ coordinator contains new mapping
→ returned FrontendSettingsSnapshot contains new mapping
```

Given an invalid enum candidate:

```text
RPC returns snapshot
→ persisted/current mapping remains unchanged
```

### 22.10 Named-pipe transport

Add a focused round-trip test:

```text
client SetBackButtonMappingAsync
→ wire request
→ server dispatch
→ fake/in-process control receives exact mapping
→ response carries exact mapping
```

### 22.11 Protocol

Update protocol assertions:

```text
CurrentVersion == 38
SetBackButtonMapping exists
SetBackButtonMappingRequest carries BackButtonMappingSettings
```

### 22.12 No-output regression

Keep current tests proving:

```text
Xbox360 mapper ignores Auxiliary M1/M2
SteamDeck mapper maps M1→R4 / M2→L4
```

PR1 must not modify those expectations.

---

## 23. Logging

Keep logs settings-level and transition-only.

Useful examples:

```text
Settings
  Back-button mapping loaded
  Back-button mapping missing; using defaults
  Back-button mapping invalid; using defaults
  Rejected invalid back-button mapping candidate
  Back-button mapping saved
```

Do not log every M1/M2 press.

There is no output behavior in PR1, so there should be no new controller-frame logging.

---

## 24. Anti-overengineering constraints

This is a simple settings/transport foundation.

Do not add:

```text
BackButtonManager
MappingEngine
ControllerLayoutManager
MappingPipeline
BackButtonStateMachine
BackButtonEpoch
publisher wrapper
new settings repository
new JSON schema framework
generic input-action abstraction
new synchronization primitive
new background worker
new polling loop
```

The actual product architecture already has:

```text
raw ControllerState
Xbox360 mapper
SteamDeck mapper
one presentation owner
one SettingsStore
one StartupSettingsCoordinator
one frontend pipe contract
```

Use those owners.

---

## 25. Non-goals

Explicitly out of scope for PR1:

- applying M1/M2 mappings to Xbox360 output;
- modifying `Xbox360DeviceStateMapper`;
- modifying the Xbox360 publisher;
- changing SteamDeck R4/L4 mapping;
- held-button release-to-rearm;
- X360 ↔ SteamDeck transition changes;
- Suspend/Resume changes;
- PnP/device-loss recovery changes;
- PID1901/PID1902 behavior;
- HidHide behavior;
- VIIPER lifecycle changes;
- firmware/EEPROM M1/M2 writes;
- keyboard/mouse/macro targets;
- stick-direction targets;
- game profiles;
- Controller-page UI;
- Overlay/QAM UI;
- changing Gamebar/Center M button mapping behavior.

---

## 26. Acceptance criteria

PR1 is complete only when all of the following are true.

### Contract

- [ ] One dedicated BackButtons contract exists.
- [ ] M1 and M2 each store one `Xbox360BackButtonTarget`.
- [ ] Default for both is `Disabled`.
- [ ] Initial target catalog is limited to ordinary X360 controls listed in this work order.
- [ ] M1 and M2 may use the same target.
- [ ] Undefined enum values are rejected.

### Persistence

- [ ] `AppSettings` carries one global `BackButtonMapping`.
- [ ] Existing Runtime-owned `settings.json` is the only persistence location.
- [ ] Save writes enum names.
- [ ] Load requires both M1 and M2 to be explicitly present.
- [ ] Missing/malformed/unknown/numeric values default only this feature.
- [ ] Unrelated settings remain intact.
- [ ] No legacy migration code is added.

### Settings mutation

- [ ] `StartupSettingsCoordinator` exposes the current mapping.
- [ ] One whole-record mutation persists M1+M2 atomically.
- [ ] Save succeeds before current in-memory settings are published.
- [ ] Invalid direct mutation is rejected rather than silently repaired.
- [ ] No unused mapping manager/runtime is introduced.

### Frontend

- [ ] `FrontendSettingsSnapshot` carries `BackButtonMappingSettings`.
- [ ] Bootstrap carries `BackButtonMappingAvailable`.
- [ ] One `SetBackButtonMappingAsync` RPC exists.
- [ ] Named-pipe client/server transport the same shared contract type.
- [ ] Protocol version is bumped once from 37 to 38.
- [ ] No compatibility shim is added.

### Behavior preservation

- [ ] Physical M1/M2 raw mapping is unchanged.
- [ ] Xbox360 mapper behavior is unchanged.
- [ ] SteamDeck M1→R4 / M2→L4 behavior is unchanged.
- [ ] Presentation switching is unchanged.
- [ ] Suspend/Resume is unchanged.
- [ ] PnP recovery is unchanged.
- [ ] HidHide/PID/VIIPER authority is unchanged.
- [ ] No firmware M1/M2 profile write exists.

### Verification

- [ ] Focused contract tests pass.
- [ ] Persistence tests pass.
- [ ] Frontend contract tests pass.
- [ ] Named-pipe transport tests pass.
- [ ] Existing Xbox360/SteamDeck mapper tests pass unchanged.
- [ ] Full test suite passes.
- [ ] Debug build passes.
- [ ] Release build passes.

---

## 27. Expected end state after PR1

```text
settings.json
    |
    +-- BackButtonMapping
          M1 = Disabled / X360 target
          M2 = Disabled / X360 target
                |
                v
StartupSettingsCoordinator
                |
                v
FrontendSettingsSnapshot
                |
                +-- Main UI transport can read/write the setting
                |
                X
                |
                +-- NO controller-output connection yet
```

Physical/output path remains:

```text
PID1902 DirectInput
       |
       v
ControllerState
       |
       +-------------------------+
       |                         |
       v                         v
Xbox360 mapper              SteamDeck mapper
M1/M2 ignored               M1→R4, M2→L4
       |                         |
       v                         v
unchanged                    unchanged
```

PR2 will connect the persisted mapping to the Xbox360 output boundary.

PR3 will add only the realistic held-button presentation-transition safety needed so a single physical rear-button hold cannot carry one meaning across X360 and a different meaning across SteamDeck.

Keep PR1 strictly to the foundation above.
