# Work Order — Full1902 Policy A: Remove the Steam Input Routing Master Switch

## Status

Focused implementation work order for removing the obsolete `SteamInputRoutingEnabled` user preference and its full settings/frontend/session-policy chain after the Full PID1902 controller-authority architecture became the product contract.

This work order is intentionally **not numbered PR13**. The existing Full1902 PR12 uninstall work order reserves PR13 for the later Windows / Velopack uninstall-entry integration. This cleanup should remain an independently reviewable policy-removal PR.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     e5752972cca435073c75ee7b6af845ebfd03cc89
```

Before implementation, read and treat these documents as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/work-order/PR4_DISABLED_BOOT_CONTROLLER_ADMISSION_WORK_ORDER.md` or the current PR4 authority-aware startup work order if its filename differs
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`

Also inspect the current implementations and focused tests named throughout this work order before editing them.

The application is pre-release. Implement the current Full1902 product policy directly. Do not preserve the obsolete switch behind a hidden constant, compatibility property, deprecated RPC, or `always true` wrapper.

---

# 1. Goal

Delete the old user-configurable Steam Input Routing master switch from the product and remove the code branch that allows this preference to suppress Steam/BPM session recognition.

Current obsolete model:

```text
SteamInputRoutingEnabled = false
→ Steam game / Big Picture can exist
→ EffectiveSteamSessionSource forces the effective routing session inactive

SteamInputRoutingEnabled = true
→ Steam game / Big Picture may produce an effective routing session
```

Full1902 product model:

```text
Center M Enabled
→ MSI / stock controller authority

Center M Disabled
→ Addon Runtime controller authority
→ PID1902 desired continuously
→ Steam/BPM inactive: Xbox360 presentation
→ Steam game OR Big Picture active: SteamDeck presentation
```

Steam/BPM therefore selects **presentation**, not whether the user opted into controller routing.

The PR must remove the preference itself rather than merely changing its default or hard-coding it to `true`.

---

# 2. Scope boundary

This PR is only the `SteamInputRoutingEnabled` policy-removal cleanup.

## In scope

- remove the Controller-page Steam Input Routing switch and its routing-only expander;
- remove `SteamInputRoutingEnabled` from persisted settings;
- remove `ISteamInputRoutingPreference` and its event/mutator path;
- remove the routing-preference gate from `EffectiveSteamSessionSource`;
- remove the routing-preference dependency from `SteamSessionRuntime`;
- remove the routing setting from prerequisite safety-gate logic;
- remove the frontend snapshot member, setter RPC, mutation result types, client/server dispatch, and wire request;
- bump the desktop/QAM frontend transport protocol because a required settings contract member and RPC are removed;
- update focused tests and active user-facing documentation that still describes Steam routing as optional.

## Explicitly out of scope

Do **not** change any of the following in this PR:

- WING button assignment or policy;
- native Win+G / Game Bar suppression lifetime;
- OEM1 / Center M button mapping policy;
- OEM1 dummy/helper suppression cleanup;
- Addon Quick Settings Overlay button assignment;
- M1/M2 Xbox360 remapping;
- rumble / haptic behavior or vibration strength;
- battery charge limit;
- PID1901/PID1902 switching;
- DirectInput acquisition/recovery;
- HidHide baseline or ownership semantics;
- VIIPER attach/detach/server/bus ownership;
- Full1902 presentation switching implementation;
- Center M Enable/Disable authority transition;
- uninstall behavior;
- `legacyRoutingAllowed` removal;
- deletion of the old `AddonRoutingRuntime` / legacy route pipeline.

Those are separate product-policy or cleanup tasks.

Do not use this small cleanup as an excuse to redesign the Steam watcher graph or build a new routing/presentation abstraction.

---

# 3. Current code-review findings

## 3.1 `AppSettings` still makes routing a persisted user preference

Current file:

```text
src/SteamInputAddonforClaw/Settings/AppSettings.cs
```

Current shape includes:

```csharp
public sealed record AppSettings(
    bool LaunchAtWindowsStartup = true,
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SteamInputRoutingEnabled = false,
    bool SuppressDeveloperMenuWarning = false)
```

This is now an obsolete state variable.

Important implementation hazard: the parameter after `SteamInputRoutingEnabled` is also a `bool`.

Blindly deleting the third positional parameter can silently reinterpret code such as:

```csharp
new AppSettings(startup, logLevel, routeValue, suppressWarning)
```

or a shortened positional constructor call.

Some incorrect edits can still compile while binding the wrong boolean to `SuppressDeveloperMenuWarning`.

Therefore all affected `AppSettings(...)` construction sites must be reviewed explicitly. Prefer named arguments for multi-value construction touched by this PR.

Example target shape:

```csharp
new AppSettings(
    LaunchAtWindowsStartup: startup,
    LogLevel: logLevel,
    SuppressDeveloperMenuWarning: suppressDeveloperMenuWarning)
```

Do not add a dummy third boolean merely to preserve positional compatibility. The app is pre-release and this is deliberate contract deletion.

## 3.2 `SettingsStore` still reads, validates, logs, and saves the preference

Current file:

```text
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
```

Current behavior includes:

- `Load()` reads `SteamInputRoutingEnabled`;
- missing/non-true value currently becomes `false`;
- `LoadForSafetyGate()` strictly validates the routing setting;
- `Save()` logs and serializes `SteamInputRoutingEnabled`;
- `SettingsLoadResult` exists for the safety-gate settings read.

After this PR:

```text
legacy JSON key present
→ ignored

next settings save
→ key omitted
```

No migration, alias, compatibility read, or tombstone setting is required.

If `LoadForSafetyGate()` / `SettingsLoadResult` have no remaining production consumer after the safety-gate cleanup, delete them rather than retaining dead infrastructure.

## 3.3 `StartupSettingsCoordinator` is still the preference owner

Current file:

```text
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs
```

It currently:

```text
implements ISteamInputRoutingPreference
exposes SteamInputRoutingEnabled
raises SteamInputRoutingEnabledChanged
persists ChangeSteamInputRoutingEnabled(...)
```

All of that is obsolete and should be removed.

Do not replace it with `AlwaysEnabledSteamInputRoutingPreference`, a constant implementation, or a no-op change event.

Clean comments in the OEM1/WING preference area that cite the routing preference as the model to mirror, but do not redesign OEM1/WING in this PR.

## 3.4 `EffectiveSteamSessionSource` still contains the real master gate

Current file:

```text
src/SteamInputAddonforClaw/Steam/EffectiveSteamSessionSource.cs
```

Current `ComputeState()` starts with approximately:

```csharp
if (!_settings.SteamInputRoutingEnabled)
    return SteamSessionState.FromRunningAppId(0);
```

and the source subscribes to `SteamInputRoutingEnabledChanged`.

This is the behavior to remove.

Target effective-session precedence remains the existing non-preference behavior:

```csharp
private SteamSessionState ComputeState()
{
    var actual = _watcher.State;
    if (actual.IsActive) return actual;
    if (_bigPictureWatcher.IsActive) return SteamSessionState.CreateBigPicture();
    return _testMode.IsEnabled ? SteamSessionState.CreateDeveloperTest() : actual;
}
```

Exact formatting/naming is not mandated.

Remove:

- `_settings` field;
- constructor `ISteamInputRoutingPreference` parameter;
- preference event subscribe/unsubscribe;
- `StaticSteamInputRoutingPreference`.

Do not change actual-game > Big-Picture > Developer-Test ordering in this PR.

## 3.5 Full1902 presentation already bypasses the old preference

Current file:

```text
src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs
```

The current `SteamPresentationSnapshot` explicitly documents that Full1902 first-presentation selection is **not** derived from the old routing preference or Developer Test Mode.

Its raw policy is already:

```text
RunningAppId != 0 OR BigPictureActive
→ WantsSteamDeck
```

This PR must preserve that behavior exactly.

Remove only the now-unnecessary routing-preference constructor dependency used to construct `EffectiveSteamSessionSource`.

Do not change:

- `ActualRunningAppId`;
- `ActualRunningAppIdChanged`;
- `BigPictureStateChanged`;
- `CapturePresentationSnapshot()`;
- `WantsSteamDeck` semantics;
- event-driven presentation reconciliation.

## 3.6 `ElevatedSteamSafetyGate` currently changes safety behavior based on the preference

Current file:

```text
src/SteamInputAddonforClaw/Prerequisites/ElevatedSteamSafetyGate.cs
```

Current sequence is approximately:

```text
RunningAppID active
→ block

settings unreliable
→ block

SteamInputRoutingEnabled == false
→ allow without checking BPM

otherwise
→ inspect BPM
```

Once the preference no longer exists, the `routing off → ignore BPM` exception is invalid.

Target safety evaluation:

```text
RunningAppID read fails
→ block

RunningAppID != 0
→ block

BPM probe fails / unreliable
→ block

BPM active
→ block

otherwise
→ allow
```

Prefer the narrow signature:

```csharp
Evaluate(
    Func<uint> runningAppIdReader,
    Func<SteamBigPictureProbeResult> bigPictureProbe)
```

Then update:

```text
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
```

so its safety evaluation no longer constructs `SettingsStore` merely to read a removed preference.

This is required cleanup, not a new safety policy framework.

## 3.7 Frontend contracts still expose the switch as a required API

Current contract file:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
```

Remove:

```text
FrontendSettingsSnapshot.SteamInputRoutingEnabled
FrontendSteamInputRoutingMutationOutcome
FrontendSteamInputRoutingMutationResult
IAddonFrontendControl.SetSteamInputRoutingEnabledAsync(...)
```

Important implementation hazard: `FrontendSettingsSnapshot` currently contains adjacent boolean fields.

A positional-constructor edit can silently move the old routing boolean into `SuppressDeveloperMenuWarning`.

Review every `FrontendSettingsSnapshot(...)` construction site. Prefer named arguments where this PR touches a multi-boolean constructor.

Add/retain tests proving `SuppressDeveloperMenuWarning`, `LaunchAtWindowsStartupRequired`, OEM1 mapping, WING mapping, and Developer Menu state still round-trip correctly after the contract shape changes.

## 3.8 In-process frontend still owns the setter projection

Current file:

```text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Remove:

- `SetSteamInputRoutingEnabledAsync(...)`;
- the routing value from `MapSettings()`.

Do not change unrelated Device/Profile, OEM1, WING, Center M authority, diagnostics, or overlay frontend operations.

## 3.9 Named-pipe frontend transport still carries the obsolete RPC

Current files include:

```text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
```

Current wire contract includes:

```text
FrontendRpcMethod.SetSteamInputRoutingEnabled
SetSteamInputRoutingEnabledRequest
```

Remove both plus client/server method/dispatch support.

The current frontend protocol is version 19 at the reviewed baseline.

Because this PR removes:

- one required `FrontendSettingsSnapshot` constructor member; and
- one RPC method,

bump:

```text
FrontendTransportProtocol.CurrentVersion
19 → 20
```

Add a concise Version 20 history comment stating that Full1902 removed the user-configurable Steam Input Routing preference/RPC.

A v19 peer must fail the handshake rather than connecting with a structurally different settings contract.

Do not add a compatibility alias or accept both RPC names. Main UI / Runtime / QAM are pre-release components shipped from the same product repository.

Preserve the earlier version-history comments as historical facts; only add the new v20 entry.

## 3.10 Controller UI still presents the old product model

Current files:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
```

Remove the complete `SteamInputRoutingExpander`, including:

- `SteamInputRoutingToggleSwitch`;
- the enable confirmation dialog;
- `_lastKnownSteamInputRoutingEnabled`;
- setter/restore helper logic;
- the nested `Routing Button Behavior` card.

The nested WING/OEM1 fixed-routing description must not survive elsewhere in this PR because final WING/OEM1 policy is intentionally not decided by this cleanup.

Keep the existing MSI Center M authority card unchanged.

Keep the existing OEM1 inline settings surface unchanged except for compile-required removal of references to the deleted routing setting.

Change the Controller-page subtitle away from the obsolete phrase:

```text
Configure Steam Input controller routing.
```

to a neutral current description such as:

```text
Configure controller settings.
```

Do not introduce the future WING/OEM1/Overlay wording in this PR.

If `_isLoading` becomes unused after the toggle deletion, remove it rather than leaving dead UI state.

---

# 4. Legacy-routing transitional boundary

This is important for keeping the PR small and honest.

The repository still contains a legacy route composition selected through `legacyRoutingAllowed` when the startup authority path permits it. PR4/PR7 intentionally left that old path in place while the Full1902 Disabled-mode owner was introduced.

This PR does **not** delete that legacy physical-routing architecture.

Removing the master preference means that whenever the remaining legacy effective-session path is actually composed, its Steam game / Big Picture observation behaves like the old switch was ON because there is no longer a user preference gate.

That is the expected narrow result of deleting the preference.

Do not solve this by adding:

```text
SteamInputRoutingEnabled = true constant
AlwaysEnabledRoutingPreference
LegacyRoutingDisabledPreference
hidden feature flag
new authority boolean
```

and do not expand this PR into deleting the full legacy routing stack.

The later legacy-route cleanup should separately decide/removes:

- `legacyRoutingAllowed`;
- old Steam-session physical-ownership routing;
- old route-bound WING/Game Bar/OEM1 mechanics where superseded.

Full1902 Disabled-mode presentation is already based on raw `RunningAppID + BPM` and must remain unaffected by this transitional legacy note.

---

# 5. Persistence contract

No migration is required.

If an existing pre-release settings file contains:

```json
{
  "SteamInputRoutingEnabled": false
}
```

or:

```json
{
  "SteamInputRoutingEnabled": true
}
```

then after this PR:

```text
Load
→ ignore the key
→ no routing preference exists in memory

next Save for any remaining setting
→ SteamInputRoutingEnabled is not written
```

Do not:

- rename the key;
- write it as `true` forever;
- add a schema-migration marker solely for this deletion;
- preserve the value in an extension-data dictionary;
- expose it through frontend bootstrap.

The application is pre-release and the removed preference has no current Full1902 product meaning.

---

# 6. Required test changes

## 6.1 Settings tests

Update `SettingsStoreTests` and related settings tests to prove:

- default `AppSettings` has no routing property;
- a legacy JSON `SteamInputRoutingEnabled` key is ignored;
- saving settings does not serialize `SteamInputRoutingEnabled`;
- `SuppressDeveloperMenuWarning` remains correct after the positional-record change;
- OEM1 and WING mapping persistence remains unchanged;
- malformed unrelated settings retain their existing behavior.

If `LoadForSafetyGate()` and `SettingsLoadResult` become unused, delete their routing-specific tests with the production code.

## 6.2 Effective Steam session tests

Update `EffectiveSteamSessionSourceTests`:

Delete the ON/OFF preference matrix and live preference-toggle tests.

Keep/add direct policy coverage for:

```text
actual Steam game active
→ Actual

no actual game + BPM active
→ BigPicture

no actual/BPM + Developer Test enabled
→ DeveloperTest

none active
→ inactive
```

Keep existing event publication / duplicate suppression coverage that remains meaningful.

No fake `ISteamInputRoutingPreference` should remain.

## 6.3 Steam runtime tests

Update `SteamSessionRuntimeTests`, `SteamPresentationSnapshotTests`, `AddonRuntimeHostTests`, `AddonProcessHostStartupTests`, and any other tests/fakes that only implement/pass `ISteamInputRoutingPreference` for construction.

Do not weaken the existing raw Full1902 presentation tests.

Specifically preserve assertions equivalent to:

```text
RunningAppID != 0 → WantsSteamDeck
BPM active         → WantsSteamDeck
both inactive      → WantsXbox360
Developer Test alone does NOT alter the raw Full1902 presentation snapshot
```

## 6.4 Safety-gate tests

Update `ElevatedSteamSafetyGateTests` / prerequisite tests:

- RunningAppID active → blocked;
- RunningAppID read failure → blocked;
- BPM active → blocked;
- BPM probe unreliable/failure → blocked;
- no game + BPM inactive → allowed;
- remove all tests asserting that `routing disabled` bypasses BPM checks;
- remove settings-reader failure cases that exist solely for the deleted routing preference.

## 6.5 Frontend setting tests

The current:

```text
tests/SteamInputAddonforClaw.Tests/FrontendSteamInputRoutingSettingTests.cs
```

is dedicated to a feature that no longer exists.

Delete it rather than retaining setter/bootstrap tests for a removed preference.

Add a small negative contract guard in an appropriate existing frontend-contract test, or a narrowly named replacement test if needed, proving at minimum:

```text
IAddonFrontendControl has no SetSteamInputRoutingEnabledAsync
FrontendSettingsSnapshot has no SteamInputRoutingEnabled
```

Do not create a large new reflection framework solely for this deletion.

## 6.6 Transport tests

Update frontend transport tests for protocol v20 and remove obsolete setter round-trips.

Required:

- v19 peer is rejected by v20 handshake;
- remaining settings/bootstrap RPCs still round-trip;
- removed `SetSteamInputRoutingEnabled` cannot be emitted as a current valid RPC method;
- no compatibility forwarding exists.

---

# 7. Active documentation cleanup

Update only current user/product documentation that describes the removed preference as user-configurable.

At minimum inspect:

```text
README.md
docs/KOREAN_USER_GUIDE.md
```

Current stale wording includes concepts such as:

```text
Optional automatic Steam Input routing
Steam Input Routing is optional
routing switch
```

Remove or rewrite those statements to the current Full1902 authority/presentation model where necessary.

Do **not** use this PR to finalize WING/OEM1 button mappings in documentation. If an old user-facing sentence claims a fixed routing-time WING/OEM1 mapping that is now under separate policy review, remove the stale claim rather than inventing the future answer.

Historical `docs/work-order/*` files must remain historical records. Do not bulk-edit old work orders merely to achieve a repository-wide textual zero.

The current Full1902 authority documents already define automatic X360 vs SteamDeck presentation and should not need semantic redesign for this cleanup.

---

# 8. No overengineering

This PR deletes state and branches.

Do not add:

- a replacement routing-option abstraction;
- a `RoutingPolicyProvider`;
- another feature flag;
- a compatibility settings shim;
- an epoch/barrier/state machine;
- extra polling;
- a new authority manager;
- a migration service;
- a second Steam observer.

Reuse the existing raw Steam/BPM facts and current session observers.

The desired architecture after this PR is simpler than before.

---

# 9. Expected production shape after the PR

Settings path:

```text
AppSettings
├─ LaunchAtWindowsStartup
├─ LogLevel
├─ SuppressDeveloperMenuWarning
├─ DeveloperMenuEnabled
├─ Oem1Mapping
└─ WingMapping

NO SteamInputRoutingEnabled
```

Steam observation:

```text
SteamSessionRuntime
├─ actual RunningAppID
├─ Big Picture watcher
├─ Developer Test fact for the legacy/effective session surface
└─ raw Full1902 presentation snapshot

NO routing preference dependency
```

Full1902 controller presentation:

```text
Center M Disabled / Addon authority
    ↓
RunningAppID != 0 OR BPM active
    ├─ yes → SteamDeck
    └─ no  → Xbox360
```

Frontend:

```text
Controller page
├─ MSI Center M authority controls
└─ existing button/settings surfaces

NO Steam Input Routing toggle
NO SetSteamInputRoutingEnabled RPC
```

---

# 10. Acceptance criteria

The PR is complete only when all of the following are true.

## Production code

- [ ] `AppSettings` no longer contains `SteamInputRoutingEnabled`.
- [ ] `SettingsStore.Load()` does not read the routing key.
- [ ] `SettingsStore.Save()` does not write/log the routing key.
- [ ] legacy persisted routing key is ignored without migration.
- [ ] `ISteamInputRoutingPreference` is deleted.
- [ ] `StartupSettingsCoordinator` no longer exposes a routing preference property/event/mutator.
- [ ] `EffectiveSteamSessionSource` has no routing preference field, parameter, subscription, or master-gate branch.
- [ ] `StaticSteamInputRoutingPreference` is deleted.
- [ ] `SteamSessionRuntime` no longer requires a routing preference constructor argument.
- [ ] raw `SteamPresentationSnapshot.WantsSteamDeck` semantics are unchanged.
- [ ] `ElevatedSteamSafetyGate` no longer reads settings or bypasses BPM when routing is disabled.
- [ ] `ElevatedPrerequisiteSetup` no longer constructs `SettingsStore` for this safety decision.
- [ ] unused `LoadForSafetyGate()` / `SettingsLoadResult` are deleted if no production consumer remains.
- [ ] `FrontendSettingsSnapshot` has no routing member.
- [ ] `FrontendSteamInputRoutingMutationOutcome` and `FrontendSteamInputRoutingMutationResult` are deleted.
- [ ] `IAddonFrontendControl.SetSteamInputRoutingEnabledAsync` is deleted.
- [ ] in-process frontend setter/projection is deleted.
- [ ] `FrontendRpcMethod.SetSteamInputRoutingEnabled` is deleted.
- [ ] `SetSteamInputRoutingEnabledRequest` is deleted.
- [ ] named-pipe client/server routing setter handling is deleted.
- [ ] frontend transport protocol is bumped from v19 to v20 with no compatibility alias.
- [ ] Controller-page routing expander/toggle/dialog/fixed routing-button card are removed.
- [ ] MSI Center M authority UI remains intact.
- [ ] no WING/OEM1/Overlay/M1/M2/rumble/battery policy is introduced.
- [ ] no PID/HidHide/DirectInput/VIIPER lifecycle code is changed except mechanical compile fixes if absolutely required.

## Positional-construction safety

- [ ] every affected `new AppSettings(...)` call is reviewed so no boolean shifts into `SuppressDeveloperMenuWarning`.
- [ ] every affected `new FrontendSettingsSnapshot(...)` call is reviewed so no old routing boolean shifts into `SuppressDeveloperMenuWarning`.
- [ ] touched multi-boolean construction sites use named arguments where practical.
- [ ] tests prove the developer-warning preference still retains its intended value.

## Behavior

- [ ] actual Steam game activity is no longer suppressible by a user routing preference.
- [ ] Big Picture activity is no longer suppressible by a user routing preference.
- [ ] Full1902 Disabled-mode X360 ↔ SteamDeck selection remains raw `RunningAppID || BPM`.
- [ ] Developer Test Mode does not become a Full1902 SteamDeck-presentation input accidentally.
- [ ] prerequisite safety setup still blocks during an actual Steam game or BPM session.
- [ ] remaining settings/UI mutations operate normally after frontend contract v20.

## Cleanup

- [ ] active `src/` production code contains zero `SteamInputRoutingEnabled` references.
- [ ] active `tests/` contains zero positive references to the removed preference/API; a narrowly scoped negative source/contract assertion is acceptable if useful.
- [ ] current user-facing docs no longer describe Steam Input Routing as an optional switch.
- [ ] historical work orders are not rewritten merely for string cleanup.
- [ ] no dead always-true routing preference wrapper remains.

---

# 11. Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also run repository searches over active code/tests, for example:

```text
SteamInputRoutingEnabled
ISteamInputRoutingPreference
SetSteamInputRoutingEnabled
FrontendSteamInputRoutingMutation
```

Expected result:

```text
active production implementation: none
active tests: no positive feature implementation references
historical docs: may remain
this work order: naturally contains the removed names
```

Focused automated coverage should include:

```text
SettingsStore
EffectiveSteamSessionSource
SteamSessionRuntime / SteamPresentationSnapshot
ElevatedSteamSafetyGate
frontend contracts / frontend transport v20
Controller page compile path
```

---

# 12. Manual smoke validation

This PR adds no new hardware protocol and does not require new hardware evidence to prove a controller packet or PID lifecycle.

If a supported MSI Claw is available, perform a short regression smoke only:

```text
Center M Disabled / Addon authority
→ normal desktop state presents Xbox360
→ launch Steam game: presentation becomes SteamDeck
→ exit Steam game: presentation returns Xbox360
→ enter/leave BPM: presentation follows BPM
→ Controller page has no Steam Input Routing toggle
→ MSI Center M authority buttons still work as before
```

Do not expand this validation into WING/OEM1/Overlay policy testing; those are separate tracks.

---

# 13. Review guidance

Blocking findings for this PR include:

- a hidden/defaulted `SteamInputRoutingEnabled` authority remains;
- a legacy settings value can still suppress Steam/BPM observation;
- frontend v19 is retained despite changing a required settings contract shape;
- positional bool deletion silently changes `SuppressDeveloperMenuWarning` or another setting;
- prerequisite setup can mutate during BPM because the removed preference bypass was accidentally retained;
- Full1902 raw presentation starts depending on Developer Test Mode or the old effective-session gate;
- removing the UI toggle also accidentally removes/changes MSI Center M authority controls;
- the PR expands into WING/OEM1/Overlay/controller-mapping policy without explicit scope.

Non-blocking / separate work:

- deleting the remaining legacy `AddonRoutingRuntime` path;
- changing WING or OEM1 defaults;
- moving Win+G suppression from route lifetime to Full1902 authority lifetime;
- M1/M2 Xbox360 mapping;
- vibration-strength settings;
- battery charge limit.

Do not block this focused removal PR merely because those later Full1902 product-cleanup items still exist.

---

# 14. Final invariant

After this PR there is no product concept of:

```text
Steam Input Routing = On / Off
```

There is only controller authority plus automatic presentation policy:

```text
Center M Enabled
→ MSI / stock authority

Center M Disabled
→ Addon authority
→ PID1902
→ normal = Xbox360
→ Steam game / BPM = SteamDeck
```

The removed routing preference must not survive as persisted state, frontend API, session eligibility gate, or hidden constant.
