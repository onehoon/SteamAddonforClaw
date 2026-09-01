# Work Order — PR1: Persistent Dual VIIPER Devices Foundation

## Status

Implementation work order for the first code PR in the **Full PID1902 Implementation** track.

This PR is a **foundation-only POC**. It must not take physical MSI Claw controller ownership and must not implement the future default-Xbox360 runtime behavior yet.

Before implementation, read and treat the following as current design authorities:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/VIIPER_IMPLEMENTATION_RULES.md`
- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`
- the current `onehoon/VIIPER` fork documentation referenced by those files, especially its typed-device lifecycle / attach-detach / ownership rules.

Do not preserve an old policy merely because it exists in current main. However, this PR is intentionally narrow: it establishes the long-lived dual-device VIIPER substrate and does not yet replace the physical-controller orchestration.

---

## 1. Goal

Change the canonical process-lifetime VIIPER owner so that **both future virtual presentations are created during normal production initialization** on one server and one bus:

```text
Canonical VIIPER runtime
    ├─ one USB server
    ├─ one caller-owned bus
    ├─ persistent Steam Deck typed logical device
    └─ persistent Xbox360 typed logical device
```

Both logical devices must be created **detached** and must remain detached after this PR's initialization.

Target post-initialization state:

```text
VIIPER server     = alive / owned
VIIPER bus        = alive / owned
Steam Deck        = created / identity verified / DETACHED
Xbox360           = created / identity verified / DETACHED
Runtime state     = Ready
```

This PR does **not** select an active presentation.

It prepares the native lifecycle required by the next controller-ownership POC, where Xbox360 will become the default presentation after PID1902 / DirectInput / HidHide ownership is established.

---

## 2. Product architecture context

The project direction is now:

```text
Center M Enabled
    → MSI / stock controller authority

Center M Disabled
    → Addon controller authority
    → persistent physical PID1902 / DirectInput / HidHide
    → one persistent VIIPER runtime
    → Xbox360 default
    → Steam Deck only for Steam game / BPM presentation
```

Normal future Xbox360 ↔ Steam Deck presentation changes must not recreate the VIIPER runtime, switch PID1901/PID1902, reacquire DirectInput, or rebuild HidHide state.

This PR only builds the VIIPER half of that foundation.

Do **not** implement the physical-ownership half here.

---

## 3. Current code baseline to verify before editing

Re-read current main before making changes. Do not rely only on this document if names have moved.

At the time this work order was written, the important facts were:

### `CanonicalViiperRuntime`

Current production initialization creates:

```text
NewUSBServer
→ CreateUSBBus
→ CreateSteamDeckDevice(autoAttachLocalhost: false)
→ verify Steam Deck identity
→ verify Steam Deck attachment == Detached
→ Ready
```

Xbox360 fields and lifecycle primitives already exist in the same runtime, including concepts equivalent to:

- Xbox360 device handle ownership;
- logical-device identity;
- `TryGetXbox360AttachmentState`;
- `AttachXbox360`;
- `DetachXbox360`;
- `SetXbox360State`;
- Xbox360 removal;
- Xbox360 teardown phases.

However, normal production initialization does **not** create Xbox360. Xbox360 creation is currently reachable only through the `createXbox360ForTests` seam in `CanonicalViiperRuntime.TryInitialize(...)`.

### Existing Xbox360 typed path

The managed typed ABI already exists for the non-rumble Xbox360 input surface:

```text
CreateXbox360Device
SetXbox360DeviceState
RemoveXbox360Device
RemoveXbox360DeviceEx
```

The repository also already contains:

- `Xbox360DeviceState`;
- `Xbox360ButtonBits`;
- `Xbox360DeviceStateMapper`;
- `CanonicalXbox360InputPublisher`;
- associated tests.

Do not reimplement these in PR1.

### Existing legacy Game Bar presentation path — important

Current main contains an older Game Bar/Xbox360 presentation path in `AddonRoutingRuntime` and process-host delivery code. The existing production Game Bar foreground delivery can call the current `EnterXbox360PresentationAsync` / `ExitXbox360PresentationAsync` path.

Today, production VIIPER initialization does not create Xbox360, which prevents that path from owning a real X360 device.

**Simply creating Xbox360 in production can therefore make an existing old-policy attachment path newly reachable.**

That would violate this PR's foundation-only contract.

PR1 must ensure that after dual-device initialization:

```text
Steam Deck = Detached unless separately owned by the pre-existing Steam route
Xbox360    = Detached
```

and **this PR itself must not make Xbox360 auto-attach because Game Bar foreground changes occur.**

If current main would automatically invoke X360 attachment once a production handle exists, make the smallest direct change needed so that the obsolete automatic Game Bar X360 presentation entry cannot attach the newly created X360 during this foundation PR.

Preferred principle:

- remove or disconnect the obsolete automatic invocation if it is now incompatible with the new architecture;
- do not introduce a new long-lived compatibility mode, feature flag, epoch, policy manager, or parallel authority just to preserve the old temporary Game Bar model.

Keep any such change strictly limited to preventing unintended attachment in this PR. Do not perform the broader old-routing cleanup yet.

---

## 4. Required production initialization contract

Update the canonical VIIPER production initialization to acquire resources in one deterministic ownership chain.

Recommended acquisition sequence:

```text
1. NewUSBServer
2. CreateUSBBus
3. CreateSteamDeckDevice(autoAttachLocalhost: false)
4. GetUSBDeviceIdentity(SteamDeck)
5. verify SteamDeck identity belongs to the created bus
6. GetUSBDeviceAttachmentState(SteamDeck)
7. require known-safe Detached
8. CreateXbox360Device(autoAttachLocalhost: false)
9. GetUSBDeviceIdentity(Xbox360)
10. verify Xbox360 identity belongs to the same created bus
11. GetUSBDeviceAttachmentState(Xbox360)
12. require known-safe Detached
13. set runtime State = Ready
```

The exact Deck-first / X360-second order above is preferred because it minimizes change from the current production initialization and provides a simple reverse-order staged unwind.

Do not attach either device as part of `TryInitialize`.

Do not send live controller state as part of initialization.

Do not start an Xbox360 publisher as part of initialization.

Do not register rumble callbacks.

---

## 5. Remove the test-only Xbox360 creation split

The future production contract now requires the Xbox360 logical device to exist for the runtime lifetime, so the test-only creation distinction is obsolete.

Refactor `CanonicalViiperRuntime.TryInitialize(...)` so that normal production initialization creates both devices.

Remove `createXbox360ForTests` if it no longer has a valid purpose.

Update tests to use the real production initialization contract rather than asking for a special dual-device test mode.

Do not retain:

```text
production = Deck only
tests      = Deck + X360
```

The production and test ownership model must be the same.

A test may still inject fake native outcomes, but it should not select a different resource graph.

---

## 6. Identity requirements

Each created typed device must have its exact native identity captured and verified before initialization may continue.

For each device:

```text
GetUSBDeviceIdentity(handle)
→ returned bus ID must equal runtime-owned BusId
→ capture logical device ID
```

Do not infer ownership from:

- VID/PID alone;
- device type alone;
- “some Xbox360 exists”;
- Windows enumeration alone.

The canonical runtime owns the exact handles and identities returned from its own creation calls.

Both logical device IDs should remain observable to tests and diagnostics in the same narrow way the existing Deck identity is today.

Do not add a generic device registry abstraction for two known typed devices.

---

## 7. Attachment-state requirements

Both devices must be proven detached before `Ready` is committed.

Known-safe desired state:

```text
SteamDeck attachment = Detached
Xbox360 attachment   = Detached
```

Treat these outcomes using the same canonical fail-closed principles already used by the runtime:

### Query failure

If `GetUSBDeviceAttachmentState` itself fails, attachment outcome is unknown.

Do not guess that the device is detached.

Preserve native ownership evidence and mark the runtime unsafe according to the existing contract.

Do not perform destructive cleanup after an unknown attachment outcome unless the existing canonical rules explicitly prove that operation safe.

### `OutcomeUnknown` / unrecognized enum value

Hard fail-closed boundary.

Do not reinterpret it as Detached.

### Unexpected `Attached`

The initialization path did not request auto-attach, so an Attached result is unexpected but still classified/known.

Use the existing known-safe neutral/detach logic for the relevant typed device before deciding whether cleanup can continue.

Do not silently accept Attached and mark runtime Ready.

Do not allow both devices to become attached during initialization.

---

## 8. Staged initialization failure / unwind

Keep the existing canonical principle:

> Once a native resource has been acquired, its ownership must never be silently discarded because a later initialization step failed.

The runtime object must continue to carry the real handles when cleanup is incomplete.

### Expected reverse-order unwind

With the preferred acquisition order:

```text
Server
→ Bus
→ Deck
→ Xbox360
```

a known-safe staged failure after Xbox360 creation should unwind in reverse order:

```text
Xbox360
→ Deck
→ Bus
→ Server
```

Examples:

### Xbox360 create fails

Known resources:

```text
Server + Bus + Deck
```

Unwind:

```text
Deck remove
→ Bus remove
→ Server close
```

### Xbox360 identity verification fails

Known resources:

```text
Server + Bus + Deck + Xbox360
```

If attachment ownership remains known-safe, unwind:

```text
Xbox360 remove
→ Deck remove
→ Bus remove
→ Server close
```

### Xbox360 attachment query fails / outcome unknown

Do not destroy resources past the canonical unknown-outcome fail-closed boundary.

Return the same runtime owner carrying the real handles in an Unsafe state, consistent with the current VIIPER ownership rules.

### Cleanup retryable failure

Keep the same owner in CleanupPending and preserve the exact teardown phase/resource ownership required for a later cleanup retry.

Do not return `null` unless cleanup is proven to have returned to nothing-owned.

---

## 9. Final teardown contract

Extend/verify final process-lifetime teardown so that both persistent typed devices are retired before the bus/server are released.

The final teardown must correctly handle the possibility that a future caller attached either device, even though PR1 itself does not attach Xbox360.

Required principles:

1. Never remove an attached device without following the canonical neutral/detach contract.
2. Clear/neutral typed device state where required before detach.
3. Do not continue destructive teardown beyond Unsafe/unknown native outcomes.
4. Remove both typed devices before removing the bus.
5. Remove the bus before closing the server.
6. Preserve CleanupPending/Unsafe evidence if teardown cannot be proven complete.

Because the preferred acquisition order is Deck then Xbox360, prefer reverse-order removal where no existing native contract requires otherwise:

```text
Xbox360 detached/removed
→ SteamDeck detached/removed
→ Bus removed
→ Server closed
```

However, do not rewrite already-proven Deck teardown ordering merely for aesthetic symmetry if the current canonical lifecycle has a real safety reason. The invariant is that **both exact typed devices are safely retired before bus/server teardown**, with no lost ownership.

Update `CanonicalViiperRuntimeTeardownPhase` only as necessary to reflect the actual complete dual-device lifecycle. Do not add redundant states.

---

## 10. Runtime readiness semantics

`CanonicalViiperRuntimeState.Ready` must now mean:

```text
server owned
AND bus owned
AND SteamDeck typed device created
AND SteamDeck identity verified
AND SteamDeck attachment known Detached at initialization completion
AND Xbox360 typed device created
AND Xbox360 identity verified
AND Xbox360 attachment known Detached at initialization completion
```

A runtime with only Deck created must no longer be Ready.

A runtime with X360 creation/identity/attachment verification incomplete must not be Ready.

Update comments and diagnostics that currently describe Ready as a Deck-only runtime.

Do not add another state such as `DualReady`.

The meaning of `Ready` simply changes to match the new required resource graph.

---

## 11. Logging / diagnostics

Keep logging bounded and useful.

Initialization success should make the dual-device resource graph observable, for example through the existing `SteamOutput` logging category:

```text
Persistent canonical VIIPER runtime ready
ServerHandleOwned = true
BusId = ...
DeckLogicalDeviceId = ...
Xbox360LogicalDeviceId = ...
DeckInitialAttachment = Detached
Xbox360InitialAttachment = Detached
```

Exact field names may follow current conventions.

Do not add per-frame logging.

Failures should identify the exact stage, e.g. conceptually:

```text
CreateXbox360DeviceFailed
Xbox360IdentityFailed
Xbox360AttachmentStateQueryFailed
Xbox360InitialAttachmentStateUnexpected
Xbox360RemoveRetryableFailure
```

Reuse existing native diagnostics and failure classification rather than creating a second logger.

---

## 12. Explicitly preserve current ABI boundary

Do not modify the canonical native ABI just to complete this PR if the required typed calls already exist.

This PR should consume the current typed Xbox360 APIs already present in the pinned VIIPER fork.

Do not add:

- legacy flat `viiper_*` C ABI use;
- a second P/Invoke surface;
- standalone `viiper.exe`;
- ViGEm;
- direct `usbip.exe` process management;
- generic raw-report Xbox360 transport.

Use the canonical typed API and exact native handles.

---

## 13. Existing Xbox360 mapper/publisher are not PR1 work

Do not redesign or duplicate:

- `Xbox360DeviceStateMapper`;
- `CanonicalXbox360InputPublisher`;
- button mapping;
- trigger/stick scaling;
- 250 Hz publisher timing;
- future feedback routing.

Those components already exist and belong to the next presentation/ownership steps.

PR1 may update stale comments that incorrectly call them permanently Game-Bar-only or future-only **only when necessary to avoid misleading architecture documentation**, but do not turn this into a broad rename/refactor PR.

---

## 14. Prevent accidental X360 activation in this PR

This requirement is important because current main has old Game Bar delivery wiring.

After PR1, the existence of a real production X360 handle must **not** itself cause a user-visible Xbox360 controller to appear.

Acceptance invariant:

```text
PR1 initialization complete
→ Xbox360 attachment state remains Detached
```

If the current `GameBarForegroundWatcher` / delivery path can invoke X360 attachment during normal runtime solely because the handle now exists, disconnect that automatic path in the smallest architecture-consistent way.

Do not solve this with a new compatibility feature flag such as:

```text
EnableLegacyGameBarX360
DualDeviceFoundationOnly
AllowXbox360Attach
```

Do not create another authority boolean simply for this migration.

The old temporary Game Bar physical/presentation policy is not a preservation requirement under the new project direction.

However, **do not perform the full replacement of Game Bar/Steam routing orchestration in this PR**. Only prevent the newly-created X360 device from becoming attached before a future PR explicitly owns presentation selection.

Tests must prove no production startup/delivery path introduced by this PR attaches X360.

---

## 15. In scope

Implement only the following:

- production `CanonicalViiperRuntime` creates Steam Deck and Xbox360 typed devices;
- both use the same runtime-owned server and bus;
- both are created with `autoAttachLocalhost: false`;
- both exact identities are captured and verified against the owned bus;
- both initial attachment states are queried and must be known-safe Detached before Ready;
- Xbox360 creation is removed from the test-only special mode and becomes normal production ownership;
- staged-init unwind covers the second typed device without losing ownership;
- final teardown covers both typed devices;
- runtime Ready semantics/comments/logging reflect the dual-device graph;
- tests cover dual-device initialization, failure, unwind, and teardown;
- prevent current old Game Bar delivery from accidentally attaching the newly-created X360 during this foundation PR, using the smallest direct change and no new compatibility state.

---

## 16. Explicitly out of scope

Do **not** implement any of the following in this PR:

### Physical MSI controller ownership

- Center M Enabled/Disabled controller admission;
- Center M process quiesce/kill;
- PID1901 → PID1902 switching;
- PID1902 persistence;
- native-mode reclaim;
- DirectInput acquisition;
- DirectInput publisher lifecycle;
- HidHide configuration/isolation;
- physical PnP recovery;
- strong physical-device reconciliation changes.

### Xbox360 user-visible presentation

- attach Xbox360 as default;
- start `CanonicalXbox360InputPublisher` at application startup;
- map live physical input into X360 as a new baseline;
- make X360 user-visible outside the existing explicitly-controlled path;
- implement X360 slot policy.

### Steam presentation policy

- automatic X360 ↔ Steam Deck switching;
- Steam game selector changes;
- BPM selector changes;
- presentation transition timing/blackout POC;
- Steam-session ownership redesign.

### Feedback / motion

- Xbox360 rumble callback;
- Steam Deck rumble changes;
- physical rumble routing;
- vibration-strength setting;
- gyro;
- accelerometer;
- motion state;
- IMU report work.

### MSI button / device features

- WING/OEM1 behavior changes;
- controller remapping;
- TDP/Fan/Power feature changes;
- profile work;
- overlay/QAM work.

### Broad cleanup

- deleting all old Steam routing code;
- renaming the entire routing architecture;
- new generalized controller-authority frameworks;
- new generic virtual-device registries;
- new state-machine abstractions for future requirements.

---

## 17. Tests — production dual-device initialization

Update existing `CanonicalViiperRuntimeTests` and related fakes so the default production initialization expects both devices.

At minimum cover:

### Success

```text
NewUSBServer succeeds
CreateUSBBus succeeds
CreateSteamDeckDevice succeeds
Deck identity bus matches
Deck attachment = Detached
CreateXbox360Device succeeds
Xbox360 identity bus matches
Xbox360 attachment = Detached
→ runtime Ready
```

Assert:

- exact call ordering where ordering is part of the lifecycle contract;
- both handles are retained;
- both logical IDs are retained;
- same bus ID is used;
- `autoAttachLocalhost` is false for both;
- no Attach call occurs;
- no live state write is required for initialization;
- no rumble callback is registered.

### No test-only dual graph

Remove/update tests that require `createXbox360ForTests: true`.

Default `TryInitialize` should now produce the dual graph.

---

## 18. Tests — Xbox360 staged failures

Add deterministic fake-native coverage for at least:

1. `CreateXbox360Device` failure.
2. Xbox360 identity query failure.
3. Xbox360 identity returns wrong bus.
4. Xbox360 attachment-state query failure.
5. Xbox360 attachment returns `OutcomeUnknown`.
6. Xbox360 attachment returns an unrecognized enum value.
7. Xbox360 unexpectedly reports Attached and known-safe detach succeeds.
8. Xbox360 unexpectedly reports Attached and detach returns retryable failure.
9. Xbox360 unexpectedly reports Attached and detach outcome becomes unsafe/unknown.
10. Xbox360 removal retryable failure during staged unwind.
11. Xbox360 removal unsafe/unknown outcome during staged unwind.

For each case assert the canonical contract:

- whether runtime returns null / CleanupPending / Unsafe;
- which exact handles remain owned;
- whether later destructive calls are forbidden after unknown outcome;
- whether Deck/bus/server cleanup proceeds only when proven safe.

Do not weaken existing Deck failure tests to make the new graph pass.

---

## 19. Tests — final dual-device teardown

Cover at least:

- both devices detached → both removed → bus removed → server closed;
- future-shaped case where Xbox360 is Attached → neutral + detach + remove before bus/server;
- future-shaped case where Deck is Attached → existing Deck safe detach/remove preserved;
- retryable Xbox360 detach/remove leaves CleanupPending with exact teardown phase;
- retry resumes from the correct owned phase rather than recreating resources;
- unsafe/unknown Xbox360 outcome stops destructive teardown;
- bus/server are never removed while an exact typed device remains owned;
- successful full teardown leaves Runtime state Closed and zeroed/cleared native ownership fields according to existing convention.

Do not add concurrency torture tests for instruction-level interleavings that cannot occur in normal runtime lifecycle.

---

## 20. Tests — no accidental presentation activation

Because production Xbox360 creation makes the existing handle available, add coverage proving the foundation does not accidentally expose it.

At minimum establish:

```text
normal runtime initialization
→ CreateXbox360Device called
→ AttachUSBDeviceEx(Xbox360) NOT called
```

If old Game Bar production delivery wiring is disconnected as required by this PR, add a narrow test proving a Game Bar foreground event does not attach X360 through that obsolete automatic path.

Do not create a new policy manager solely to test this.

---

## 21. Regression expectations

Although current Steam-session orchestration is not the future architecture contract, this PR must not accidentally break unrelated proven native safety.

Verify:

- canonical Steam Deck creation still uses the same typed API;
- Steam Deck identity/attachment failure classification remains fail-closed;
- no second VIIPER server is created;
- no second bus is created for Xbox360;
- VIIPER load failure still leaves output unavailable rather than using a legacy fallback;
- CleanupPending/Unsafe ownership is still preserved rather than discarded;
- existing ABI tests remain valid except for comments/assertions explicitly tied to “Xbox360 not production-created”.

If a test encodes the obsolete assumption `Production startup creates Deck only`, update the test to the new design contract rather than preserving that assumption.

Do not weaken tests that protect native ownership, classified outcomes, or teardown safety.

---

## 22. Build / validation requirements

Before opening the PR:

1. Build the relevant solution/projects in Debug.
2. Build in Release.
3. Run the full automated test suite.
4. Confirm there are no new compiler warnings.
5. Confirm canonical VIIPER ABI tests pass against the bundled/pinned header/binary contract.
6. Review the final diff for scope creep.

This PR should not require MSI Claw hardware validation because it must not attach the new X360 presentation or mutate the physical controller.

If a local environment permits native VIIPER smoke validation, it is useful but not a substitute for deterministic lifecycle tests.

Do not block this foundation PR solely because the project owner cannot currently perform handheld testing, provided no user-visible X360 attachment is introduced.

---

## 23. Acceptance criteria

PR1 is complete only when all of the following are true:

- [ ] Production canonical VIIPER initialization owns one server.
- [ ] It owns one bus.
- [ ] It creates one Steam Deck typed device.
- [ ] It creates one Xbox360 typed device.
- [ ] Both are created with local auto-attach disabled.
- [ ] Both exact identities are captured and verified against the runtime-owned bus.
- [ ] Both initial attachment states are explicitly queried.
- [ ] Runtime `Ready` requires both devices to be known Detached at initialization completion.
- [ ] The `createXbox360ForTests` dual-graph special case is removed or no longer changes the production resource graph.
- [ ] Staged failure after Xbox360 creation cannot lose native ownership evidence.
- [ ] Known-safe staged unwind retires Xbox360 before older resources where appropriate.
- [ ] Unknown/unsafe attachment outcomes stop destructive cleanup exactly as required by canonical VIIPER rules.
- [ ] Final teardown safely retires both typed devices before bus/server teardown.
- [ ] No second VIIPER runtime/server/bus is introduced.
- [ ] No X360 attach happens during initialization.
- [ ] Existing automatic Game Bar delivery cannot newly attach X360 merely because production now created the handle.
- [ ] No Xbox360 publisher is started by this PR.
- [ ] No PID/HidHide/DirectInput/Center M controller ownership behavior is added.
- [ ] No rumble or gyro work is added.
- [ ] Full automated tests pass.
- [ ] Debug and Release builds are clean.

---

## 24. Review guidance

Review this PR as a foundation POC, not as the full controller replacement.

Blocking findings should be limited to realistic defects such as:

- production initialization can return Ready without a verified Xbox360 device;
- an X360 device is accidentally attached/user-visible;
- the existing Game Bar delivery unexpectedly activates X360 after this change;
- failure after X360 creation leaks or loses native ownership;
- unsafe/unknown native outcomes are followed by destructive cleanup;
- teardown removes bus/server while a typed device is still owned;
- retryable cleanup cannot resume correctly;
- a second server/bus/runtime is created;
- existing Deck native safety regresses.

Do not block for theoretical instruction-level races with no realistic product lifecycle path.

Do not request generalized managers, epochs, registries, or abstraction layers solely because future PRs will add controller ownership and presentation switching.

The purpose of this PR is deliberately simple:

> **Make the one canonical VIIPER owner permanently own both future typed logical presentations, safely and detached, without yet choosing or exposing either as the new default controller.**

---

## 25. What comes after this PR — context only, not implementation scope

The next planned POC will use this dual-device foundation to establish the first Full-PID1902 controller baseline:

```text
Center M Disabled
→ Addon physical-controller ownership admitted
→ PID1902 / DirectInput
→ HidHide isolation verified
→ Xbox360 neutral
→ Xbox360 attach
→ Xbox360 publisher live
→ Steam Deck remains detached
```

Later work will add:

- owned-state PID1901 reclaim / Center M resurrection handling;
- device-loss / PnP recovery;
- manual X360 ↔ Steam Deck presentation switching;
- automatic Steam/BPM presentation selection;
- rumble;
- gyro.

Do not pull those features into PR1.