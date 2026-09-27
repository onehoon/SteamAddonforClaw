# Work Order — Full1902 Xbox360 Publisher Cadence 125 Hz

> **Date:** 2026-09-27  
> **Scope:** Full PID1902 standalone controller runtime  
> **Change type:** Small isolated timing change  
> **Target:** Xbox360 virtual publisher only  
> **SteamDeck:** No cadence change

---

## 1. Goal

Change the production Xbox360 virtual-controller publication cadence from:

```text
4 ms
≈ 250 Hz
```

to:

```text
8 ms
≈ 125 Hz
```

while keeping the SteamDeck publisher unchanged at:

```text
4 ms
≈ 250 Hz
```

This is intentionally a small, presentation-specific change.

Do not introduce a shared dynamic rate controller, runtime cadence switching API, new manager, new persisted setting, or a second timing authority.

---

## 2. Current architecture and why this can stay simple

Current Full1902 authority remains:

```text
Center M Disabled
→ Addon Runtime owns PID1902 DirectInput
→ persistent HidHide isolation
→ one canonical VIIPER runtime
→ exactly one live virtual presentation

Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

The two virtual presentations already have separate production publisher classes:

```text
CanonicalXbox360InputPublisher
CanonicalSteamDeckInputPublisher
```

and `MsiClawAddonPresentation` already creates/selects the matching publisher for the active presentation.

Therefore the requested behavior does **not** require a mutable shared period or presentation-rate provider.

The smallest correct implementation is:

```text
Xbox360 publisher ProductionPeriod = 8 ms
SteamDeck publisher ProductionPeriod = 4 ms
```

---

## 3. Current code facts

### 3.1 Physical PID1902 input already polls at 8 ms

Current file:

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`

contains:

```csharp
private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
```

So the owned physical DirectInput source has a nominal 125 Hz polling interval.

Do not change it in this PR.

### 3.2 Xbox360 production publisher currently runs at 4 ms

Current file:

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`

currently documents an approximately 250 Hz / 4 ms absolute-deadline schedule and contains:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
```

This is the value to change.

### 3.3 SteamDeck production publisher must remain at 4 ms

Current file:

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`

uses the existing approximately 250 Hz / 4 ms absolute-deadline schedule.

Do not change the SteamDeck production cadence in this work order.

The SteamDeck timing path has separate real-hardware validation history and remains outside this optimization.

### 3.4 Presentation ownership is already separated

Current `MsiClawAddonPresentation` composes separate factories for:

```text
CanonicalXbox360InputPublisher
CanonicalSteamDeckInputPublisher
```

Use that existing separation.

Do not add another cadence-selection layer.

---

## 4. Required production code change

### File

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`

### Change

Replace:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(4);
```

with:

```csharp
private static readonly TimeSpan ProductionPeriod = TimeSpan.FromMilliseconds(8);
```

Update the class documentation/comments that specifically describe the Xbox360 publisher as:

```text
~250 Hz
4 ms
```

so they instead describe:

```text
~125 Hz
8 ms
```

Do not broadly rewrite unrelated historical architecture comments in this PR.

---

## 5. Scheduling behavior that must remain unchanged

Only the Xbox360 period changes.

Preserve the existing production scheduling architecture:

- dedicated Xbox360 worker thread;
- `WindowsHighResolutionOneShotTimer`;
- monotonic absolute deadline scheduling;
- `CanonicalPublisherDeadlineMath`;
- existing `ThreadPriority.AboveNormal` behavior;
- existing publisher fault reporting;
- existing sink rejection handling;
- existing stop signal;
- existing worker join / fail-close behavior;
- existing restart-after-clean-stop behavior;
- one state write per publisher tick;
- mapping from the current `LatestState` snapshot on each tick.

Do not replace the current timer design with:

- `Task.Delay(8)`;
- periodic managed timers;
- sleeps/spin loops;
- a new global scheduler;
- an event-driven physical-input-to-X360 forwarding path.

This PR is a period change, not a scheduler redesign.

---

## 6. Do not synchronize the 125 Hz physical and virtual loops

After this change both nominal values will be 8 ms:

```text
MsiClawInputSource PollInterval       = 8 ms
Xbox360 ProductionPeriod             = 8 ms
```

They are still independent loops and are not phase-locked.

That is acceptable.

Do **not** add synchronization such as:

- physical-poll completion barriers;
- sequence epochs;
- publisher wake-on-StateChanged;
- shared timer ownership;
- additional locks;
- condition variables;
- cross-thread handshakes intended only to align the two 8 ms clocks.

The publisher should continue reading the latest available snapshot when its existing absolute deadline arrives.

Occasional repeated publication of the same snapshot, or a newly-polled state not being published until the next Xbox360 deadline, is expected and is not a defect requiring new synchronization.

---

## 7. SteamDeck must remain exactly 250 Hz / 4 ms

Do not modify functional behavior in:

`src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`

In particular, do not:

- change its `ProductionPeriod`;
- derive its cadence from the Xbox360 publisher;
- create one shared `ControllerPublisherPeriod`;
- make its period conditional on the active presentation;
- change gyro/motion publication timing;
- change Steam / Quick Access synthetic pulse behavior;
- change Deck heartbeat/timing diagnostics.

Required final relationship:

```text
Xbox360  = 8 ms ≈ 125 Hz
SteamDeck = 4 ms ≈ 250 Hz
```

---

## 8. Presentation switching and lifecycle must remain unchanged

This work order must not change Full1902 presentation ownership.

Normal transition remains conceptually:

```text
current publisher
→ neutral
→ stop/join publisher
→ detach current presentation
→ attach target presentation
→ send target neutral state
→ start target publisher
```

During Xbox360 ↔ SteamDeck switching, do not change:

- PID1902 ownership;
- DirectInput lifetime;
- HidHide state;
- VIIPER server/bus lifetime;
- publisher owner/gate semantics;
- attachment authority;
- recovery ownership.

The active publisher naturally carries its own fixed cadence.

There is no need for code such as:

```csharp
publisher.SetFrequency(activeKind == SteamDeck ? 250 : 125);
```

and no need for types such as:

```text
DynamicPublisherRateManager
PresentationRateProvider
ControllerPublicationFrequencyService
PublisherCadenceState
```

---

## 9. Tests

### File

`tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs`

The current production timing test is named:

```text
Production_worker_is_background_AboveNormal_and_starts_from_a_4ms_absolute_deadline
```

Update it to describe the new Xbox360 contract, for example:

```text
Production_worker_is_background_AboveNormal_and_starts_from_an_8ms_absolute_deadline
```

Update the expected period calculation from:

```csharp
TimeSpan.FromMilliseconds(4)
```

to:

```csharp
TimeSpan.FromMilliseconds(8)
```

Preserve the existing assertions that prove:

- the production worker is a background thread;
- priority remains `AboveNormal`;
- the initial absolute deadline is based on exactly one production period;
- publisher QoS initialization still executes on the worker.

Do not add a flaky wall-clock test such as:

```text
run for one second and require exactly 125 reports
```

The existing deterministic deadline seam is the correct place to prove the configured production period.

Existing manual-tick functional tests should continue proving one write per supplied tick; those test seams are not intended to simulate the production wall-clock frequency.

---

## 10. Documentation handling

Update only living/current text that would otherwise incorrectly describe the current Xbox360 implementation as 250 Hz.

At minimum:

- Xbox360 publisher XML documentation in `CanonicalXbox360InputPublisher.cs`.

Also inspect:

- `docs/VIIPER_MIGRATION_TODO.md`

because it currently describes `CanonicalXbox360InputPublisher` as using the same approximately 250 Hz absolute-deadline schedule as the SteamDeck publisher.

If that passage is still presented as current state, update it to 125 Hz / 8 ms.

Do **not** rewrite completed historical work orders merely to erase the fact that Xbox360 previously ran at 250 Hz.

For example, old work orders that state "the existing ~250 Hz / 4 ms schedule" should remain historical records unless they are explicitly labeled as current authority.

If useful, the new work order itself is sufficient to document the policy change.

---

## 11. Explicit non-goals

Do not change:

- SteamDeck publication cadence;
- physical DirectInput polling interval;
- DirectInput acquisition/recovery;
- PID1901 ↔ PID1902 switching;
- HidHide ownership or reconciliation;
- VIIPER device creation/attach/detach;
- X360 state mapping;
- trigger/stick/button scaling;
- M1/M2 mapping behavior;
- rear-button suppression;
- rumble callback routing;
- rumble STOP/teardown safety;
- suspend/hibernate/resume lifecycle;
- process shutdown/restart lifecycle;
- Center M Disabled authority;
- Steam/BPM presentation-selection policy;
- Overlay pause/resume behavior;
- synthetic Steam/QAM pulse behavior;
- thread priority policy;
- timer implementation.

No user-facing setting is required.

This is a fixed product cadence:

```text
X360 = 125 Hz
Deck = 250 Hz
```

---

## 12. Practical latency tradeoff

The physical input source remains nominally 125 Hz / 8 ms.

Reducing only the Xbox360 publication period from 4 ms to 8 ms can add up to one additional 4 ms scheduling interval compared with the previous publisher cadence, depending on relative phase.

This is an accepted tradeoff for this change.

Do not attempt to recover those few milliseconds with extra synchronization or architectural machinery.

Hardware validation is the authority for whether the result remains acceptable.

If the 125 Hz Xbox360 cadence produces a clear, reproducible, user-visible input-latency regression on supported hardware, revert the Xbox360 production period to 4 ms rather than introducing a new synchronization subsystem.

---

## 13. Validation

Run the normal test suite and specifically verify the Xbox360 publisher tests.

Hardware smoke validation should cover the supported Full1902 lifecycle.

### Normal Xbox360 presentation

With Steam/BPM inactive:

```text
expected presentation = Xbox360
expected publisher cadence = ~125 Hz / 8 ms
```

Verify:

- buttons respond normally;
- D-pad responds normally;
- sticks respond normally;
- triggers respond normally;
- M1/M2 mapping still works;
- no stuck inputs;
- no obvious user-visible latency regression;
- rumble still works.

### SteamDeck presentation

With Steam/BPM active:

```text
expected presentation = SteamDeck
expected publisher cadence = ~250 Hz / 4 ms
```

Verify there is no cadence regression and normal Steam Input behavior remains intact.

### Presentation transitions

Exercise:

```text
Xbox360
→ SteamDeck
→ Xbox360
```

Verify:

- exactly one presentation remains live;
- the old publisher is stopped/joined before transition completes;
- X360 resumes with its 8 ms cadence;
- Deck resumes with its 4 ms cadence;
- no duplicate virtual publication path appears.

### Lifecycle smoke

At minimum perform a normal:

- Suspend → Resume smoke test.

The existing pause/neutral/resume path must behave exactly as before.

No additional lifecycle state should be introduced for this change.

---

## 14. Acceptance criteria

The work is complete only when all of the following are true:

1. `CanonicalXbox360InputPublisher` production period is exactly 8 ms.
2. Xbox360 documentation describes approximately 125 Hz / 8 ms where it describes current behavior.
3. `CanonicalSteamDeckInputPublisher` remains exactly 4 ms / approximately 250 Hz.
4. `MsiClawInputSource` remains 8 ms and is not otherwise modified for cadence coupling.
5. The Xbox360 deterministic initial-deadline test expects 8 ms.
6. Existing Xbox360 publisher lifecycle/fault/stop/restart tests still pass.
7. No dynamic cadence manager/provider/state is introduced.
8. No new synchronization is added between the DirectInput polling loop and the Xbox360 publisher loop.
9. X360 ↔ SteamDeck switching behavior is unchanged except for the active Xbox360 publisher frequency.
10. HidHide, PID1902 ownership, VIIPER lifecycle, suspend/resume, rumble safety, and teardown contracts are unchanged.
11. Hardware smoke testing does not show a clear reproducible input-latency regression.
12. Build and test CI pass.

---

## 15. Review guidance

Treat the following as blockers:

- SteamDeck accidentally changes from 4 ms;
- Xbox360 still schedules production at 4 ms;
- a shared mutable rate introduces cross-presentation timing state;
- the implementation changes presentation attachment/teardown behavior;
- publisher stop/join or fault fail-close behavior regresses;
- physical input polling is changed unnecessarily;
- Suspend/Resume or real presentation switching no longer converges safely;
- hardware validation shows a reproducible user-visible latency problem.

Do **not** block for theoretical phase races between the independent 8 ms DirectInput loop and 8 ms Xbox360 publisher loop.

Do **not** request locks, epochs, barriers, retry machinery, or an event-driven redesign solely to make both 125 Hz loops fire at the same instant.

The intended implementation is deliberately simple:

```text
one Xbox360 constant: 4 ms → 8 ms
matching deterministic test update
current Xbox360 documentation update
SteamDeck untouched
```
