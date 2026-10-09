# Work Order — Gyro PR2: Full1902 Steam Deck Native IMU Publication (A2VM + CG3EM)

**Date:** 2026-10-09  
**Repository:** onehoon/SteamAddonforClaw  
**Baseline inspected:** `main` at `a1a57ed8cdb78d05e016f862f22637ae446bd58c` (PR #731 squash merge). Re-check the latest `main` before modifying code.  
**Stage:** PR1 common motion acquisition **merged** → **this PR2** Steam Deck IMU publication → Xbox360 gyro-to-stick **deferred**.  
**Implementation owner:** Local Codex. **Physical hardware verification owner:** user, **after merge**.

## 0. Goal / scope decision

Publish the **existing PR1** real gyro and accelerometer snapshots to the **already attached canonical VIIPER Steam Deck** input report. Steam Input is responsible for deciding whether gyro is used and how it is mapped to mouse/stick, sensitivity, activation buttons and individual games.

Deliver **one focused PR2** covering exactly:
- MSI Claw A2VM 7 (`msi.claw.a2vm.7`, MS-1T42);
- MSI Claw A2VM 8 (`msi.claw.a2vm.8`, MS-1T52);
- MSI Claw EX / CG3EM (`msi.claw.cg3em`, MS-1T91).

Both families use the model-specific acquisition sources and shared normalized application-space motion state **already implemented by PR #731**. PR2 does not establish a new reader, model policy, or coordinate-normalization owner.

**No Addon gyro settings UI**, Steam Input setting mirror, app gyro sensitivity curve, activation toggle, game profile integration, virtual mouse, Xbox360 gyro-to-stick, new controller type, or extra native output publisher.

## 1. Required reading / authority

Read these documents and current code before editing:

1. [Full1902 document authority](../Full%201902%20Implementation/README.md), including the linked [Full1902 controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md), [HidHide/startup authority policy](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md), and [elevated Runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md).
2. [Approved PR1/PR2 gyro implementation plan](../gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md), especially ``3.3–3.4`, ``4–6` and lifecycle exclusions.
3. [Gyro PR1 work order](GYRO_PR1_FULL1902_COMMON_MOTION_CORE_WORK_ORDER_2026-10-09.md) and **actual merged PR #731 implementation** (code wins over the pre-implementation sketch).
4. [Prior SD6 research](../gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md), for the real-accelerometer requirement and evidence/provenance. The old research-only implementation gate is superseded by the approved October plan.
5. [Canonical VIIPER integration contract](../VIIPER_INTEGRATION.md) and current native ABI files.
6. External **encoding reference only**, not a dependency: [HHC SteamDeckTarget.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamDeckTarget.cs). HHC `BuildReport`, `ImuOffset=24`, `GyroUnitsPerDps=16`, `AccelCountsPerG=16384` establish the initial target-scale convention; verify it against this Addon's current VIIPER native struct. HHC or CTW must **not** be integrated into this standalone app.

Project scope is one interactive Windows user/session on a supported Claw. Do not add speculative multi-session or theoretical interleaving machinery.

## 2. Current main code — concrete integration sites

| File | Existing contract | PR2 change |
| --- | --- | --- |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionState.cs` | Immutable snapshot with normalized gyro deg/s, accel g, independent freshness (250ms/500ms), `IsUsableForSteamDeckImu` | **Reuse unchanged** if possible |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs` | `LatestState` is a nonblocking volatile read with freshness masking; worker threads alone call Windows sensor APIs | **No sensor-side changes** |
| `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` | Owns `_motionSource`; creates after healthy PID1902 ownership; starts/stops readers only while actual Steam Deck presentation is active; already handles loss/suspend/resume/teardown | Supply a small **read-only** snapshot delegate to the presentation owner |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` | One canonical VIIPER presentation owner; default `_deckPublisherFactory` constructs one `CanonicalSteamDeckInputPublisher` | Carry that optional delegate to the **existing Deck publisher only** |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` | One 4ms/250Hz publication path; `PublishCurrentStateOnce` maps physical buttons/sticks then applies `SteamDeckSystemButtonOverlay` and calls `_sink.SetState` | Read a *single current* motion snapshot per publish and pass to the mapper |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs` | Maps buttons/sticks/rear inputs and intentionally sets all IMU fields to zero | Encode six existing native gyro/accel signed fields when both sensors valid |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs` | Complete canonical `SteamDeckDeviceState`, including signed 16-bit `AccelX/Y/Z`, `Pitch/Yaw/Roll`, and quaternion | **No ABI/struct changes** |
| `src/SteamInputAddonforClaw/Dependencies/Viiper/libVIIPER.h` | Canonical bundled native `SteamDeckDeviceState` C layout | **No native edits** |
| `tests/SteamInputAddonforClaw.Tests/SteamDeckDeviceStateMapperTests.cs` | Native mapping and neutral-input expectations | Extend |
| `tests/SteamInputAddonforClaw.Tests/CanonicalSteamDeckInputPublisherTests.cs` | Deterministic manual tick tests; sink captures mapped native state | Extend |

**Observed anchors in reviewed main:** `AddonProcessHost.TryStartDisabledModeControllerAsync` constructs `MsiClawAddonPresentation` before acquiring physical ownership, later creates `_motionSource` after successful acquisition and calls `ReconcileMotionReadersAsync("StartupPresentation")` after `AttachInitialAsync`. `MsiClawAddonPresentation`'s default 4-argument Deck publisher factory currently calls `new CanonicalSteamDeckInputPublisher(source, sink, fault: fault, systemButtonOverlay: overlay, rearButtonSuppressionProvider: ShouldSuppressRearButton)`. The publisher's `PublishCurrentStateOnce` calls `SteamDeckDeviceStateMapper.Map(rawState, suppressM1, suppressM2)` and then the existing system-button overlay. These are the intended narrow seams.

## 3. Required data path / owner boundaries

~~~text
A2VM (WinRT gyro + legacy COM accel) / EX (legacy COM gyro + accel)
    -> existing MsiClawMotionSource worker threads (PR1; no changes)
    -> one MsiClawMotionState.LatestState (normalized, freshness-masked)
    -> host-owned read-only Func<MsiClawMotionState> (NO new source owner)
    -> MsiClawAddonPresentation, existing SteamDeck factory
    -> CanonicalSteamDeckInputPublisher, existing 4ms logical cycle
    -> SteamDeckDeviceStateMapper, one target encoding
    -> existing SteamDeckSystemButtonOverlay
    -> existing canonical VIIPER SetDeckState / Steam Input
~~~

Keep **physical sensor normalization** and **Steam Deck native target encoding** separate:
- PR1 already does raw sensor `(X,Y,Z) -> (X,Z,-Y)` **once** for both gyro and accelerometer.
- PR2 consumes **these already-normalized** gyro (deg/s) and accelerometer (g) values and performs the **Steam Deck target field mapping once**.
- Do not apply the raw-sensor coordinate transform again, rotate twice, multiply by 9.80665, or convert deg/s to radians.

A suitable small production wiring pattern (adapt to current code; do not build a wrapper framework):

~~~csharp
// AddonProcessHost: pass this to the EXISTING MsiClawAddonPresentation constructor.
// The presentation is instantiated before _motionSource exists; late dereference is required.
motionSnapshotProvider: () =>
    _motionSource?.LatestState ?? MsiClawMotionState.Unavailable
~~~

- Append `Func<MsiClawMotionState>? motionSnapshotProvider = null` to the **end** of `MsiClawAddonPresentation` constructor parameters; store it on the presentation owner.
- Let the **existing default** Deck publisher factory capture that stored provider and forward it via a final optional parameter on `CanonicalSteamDeckInputPublisher`.
- Prefer **not** to change the type or arguments of `_deckPublisherFactory`; its production lambda can close over the stored motion delegate. That avoids churn to existing test factory fakes. If code evolution makes this infeasible, take the smallest equivalent change.
- When no provider is supplied (existing unit tests/other callers) use `MsiClawMotionState.Unavailable`. Never manufacture gravity.
- Capture a **single** snapshot for each published state. Do not separately read gyro and accelerometer from the shared source, since their flags and timestamps must describe one coherent point-in-time snapshot.
- Do not change `IControllerStateSnapshotSource` or add motion fields to `ControllerState`. They are separate data streams.
- Do not add any extra source enumeration, native handle, reader task, IPC, Steam watcher, timer, mutex, sensor manager, or global authority.

## 4. Native IMU mapping (must be exact)

The current VIIPER `SteamDeckDeviceState` has six contiguous `short` IMU fields, **in this order**:

~~~text
AccelX, AccelY, AccelZ, Pitch, Yaw, Roll
~~~

The initial target encoding follows HHC's current SteamDeckTarget and the approved plan, **from the already normalized PR1 MotionState**:

| Native field | Read from MsiClawMotionState | Scale/sign |
| --- | --- | --- |
| `Pitch` | `GyroXDegPerSecond` | `+16` counts / (deg/s) |
| `Yaw` | `GyroZDegPerSecond` | `-16` counts / (deg/s) |
| `Roll` | `GyroYDegPerSecond` | `+16` counts / (deg/s) |
| `AccelX` | `AccelXG` | `+16384` counts / g |
| `AccelY` | `AccelZG` | `-16384` counts / g |
| `AccelZ` | `AccelYG` | `+16384` counts / g |

For clarity:

~~~csharp
if (motion?.IsUsableForSteamDeckImu == true)
{
    result.Pitch  = EncodeI16(motion.GyroXDegPerSecond * 16.0);
    result.Yaw    = EncodeI16(-motion.GyroZDegPerSecond * 16.0);
    result.Roll   = EncodeI16(motion.GyroYDegPerSecond * 16.0);
    result.AccelX = EncodeI16(motion.AccelXG * 16384.0);
    result.AccelY = EncodeI16(-motion.AccelZG * 16384.0);
    result.AccelZ = EncodeI16(motion.AccelYG * 16384.0);
}
~~~

This is illustrative code, not a requirement to choose a particular helper name or initializer style.

- Use a minimal deterministic `double -> short` conversion, **clamp/saturate to [-32768,32767] before cast** to prevent overflow/wrap; define and test the rounding/truncation convention. For example:

~~~csharp
private static short EncodeI16(double counts)
{
    if (!double.IsFinite(counts)) return 0;
    return (short)Math.Clamp(
        Math.Round(counts, MidpointRounding.AwayFromZero),
        short.MinValue, short.MaxValue);
}
~~~

- No free-standing quaternion estimation. Leave `GyroQuatW/X/Y/Z` at the existing neutral/default value **0**, as in the existing mapper. Quaternion is deferred; do not insert a made-up identity quaternion or gravity vector without a demonstrated native contract.
- Preserve **all** current non-IMU fields byte-for-byte logically: face buttons, D-pad, analog/digital triggers, sticks, M1/M2 => R4/L4, touchpad/stick-touch defaults, Steam/QuickAccess pulses, rumble/callbacks.
- **Do not** modify `SteamDeckDeviceState` layout/packing/field types/size (76 bytes), VIIPER API imports, Go/native dependency, USB device identity, bus/attach/detach behavior, or the canonical publisher cadence.

### 4.1 Mapper API compatibility

Current signature:
~~~csharp
SteamDeckDeviceStateMapper.Map(
    ControllerState state,
    bool suppressM1 = false,
    bool suppressM2 = false)
~~~

Prefer appending an optional argument for motion to preserve all existing bool positional calls:

~~~csharp
SteamDeckDeviceStateMapper.Map(
    ControllerState state,
    bool suppressM1 = false,
    bool suppressM2 = false,
    MsiClawMotionState? motion = null)
~~~

If `motion` is absent or invalid, the six IMU fields remain zero. Do not accidentally interpret the first bool as motion or invert M1/M2. Update the mapper's outdated remarks that currently say *all IMU fields are always neutral*.

## 5. Freshness, validity, and fail-passive semantics

**Use PR1's contract rather than inventing another gyro clock:**

- `MsiClawMotionSource.LatestState` already computes fresh state using the monotonic clock, with **independent** gyro freshness at 250 ms and accel freshness at 500 ms.
- `MsiClawMotionState.IsUsableForSteamDeckImu` is true only when **both** `HasGyro` and `HasAccelerometer` are true after freshness masking.
- When either role is absent, stale, failed, unstarted, disposed, or invalidated, **set all six native IMU values to zero for that report**. No last-known-nonzero replay.
- Real stationary acceleration is **not zero** and is necessary for the intended Steam processing path. Never substitute a hard-coded synthetic `(0,0,1)`/`(0,1,0)`/other invented gravity vector when real accelerometer data are unavailable.
- A sample that is physically zero angular velocity is valid if both freshness flags are true; do not treat numeric zero gyro as a missing reader.
- Do not call `GetCurrentReading`, COM `GetData`, enumeration, `StartAsync`, or `StopAsync` from the publisher loop. Only access the pre-existing immutable snapshot, with no reader-side locks held.

Sensor source failure is **feature-local**. Invalid/missing motion must never reject `_sink.SetState`, fault the VIIPER controller, tear down the virtual device, alter PID1902, or change HidHide. A missing snapshot provider returns neutral. If a provider unexpectedly throws, contain that optional motion failure locally (neutral IMU, no per-4ms log flood); keep existing native SetState failure policy untouched.

## 6. Activation policy already implemented in PR1 — DO NOT rework

The source acquisition gate is **actual committed SteamDeck presentation**, healthy Full1902 ownership, and not suspend-paused. This is **not** a `RunningAppId != 0` gate and **not** tied to a Steam Input user gyro-enable checkbox.

| Actual runtime state | Physical PR1 readers | PR2 native IMU |
| --- | --- | --- |
| Healthy Xbox360 presentation / normal Windows desktop | OFF | No Deck publisher/output |
| Healthy SteamDeck because BPM is active; `RunningAppId == 0` | ON | Live if both motion roles fresh |
| Healthy SteamDeck while a Steam game is running | ON | Live if both motion roles fresh |
| Healthy SteamDeck while Steam Input gyro mapping is disabled | ON | Same real IMU reports; **Steam Input** ignores/mutes them according to its own mapping |
| SteamDeck during initial sensor warmup or missing/failed sensor | Acquiring/unavailable | Neutral IMU; ordinary buttons/sticks stay live |
| Presentations switching, suspend/hibernate, physical input loss, teardown | Existing PR1 lifecycle | No stale motion; existing safe neutral/stop/teardown contract |

Do not add `RunningAppId` polling or Steam Input configuration reads. The user must be able to enter BPM with AppID 0 and configure/test gyro there.

## 7. Lifecycle and presentation integration

- **Startup:** the one default Deck publisher can start before PR1 readers finish opening. Publishing zero IMU until the first valid fresh gyro+accel pair arrives is correct. Do not block first attach on motion readiness.
- **Xbox360 <-> SteamDeck:** the existing presentation owner retires/attaches publishers and PR1 host start/stop reconciles reader eligibility; PR2's per-publisher motion delegate must not outlive the process owner or keep native devices alive. No separate switching handler is needed.
- **Overlay capture pause/resume:** reuse existing publisher pause/neutral/restart. Do not force a new motion acquisition policy for overlay capture; stopped publisher emits no reports while paused, and resumes from the latest PR1 snapshot as permitted.
- **Sleep/Hibernate/Resume:** PR1 invalidates/cancels motion and the existing presentation pause safely stops/publishes neutral before bounded sensor joins. Publisher may resume while sensor sources reacquire: output zero IMU until fresh values arrive. No pre-suspend stale gyro or fictitious gravity.
- **DirectInput loss / PnP re-enumeration:** PR1 invalidates motion; existing Full1902 owner handles physical recovery and presentation reconciliation. PR2 must never perform device detection/recovery itself. Once healthy, the same delegate observes fresh state without binding to an obsolete source object.
- **Center M Enable/authority release; shutdown/crash cleanup:** normal existing presentation-before-physical teardown stays authoritative. The host callback safely resolves to `Unavailable` if `_motionSource` has been disposed/cleared.
- Preserve the product's actual fail-close safety for PID1901<->1902, HidHide, VIIPER, rumble, and suspend. Do not add extra locks/epochs/barriers just to defend implausible single-instruction races.

## 8. Unit and integration tests (hardware-free, deterministic)

Extend **existing** tests instead of writing a parallel harness:

### 8.1 `SteamDeckDeviceStateMapperTests`

Cover at least:
1. Valid normalized input with distinguishable axes produces the **correct field order, signs and scales**. Example gyro normalized `(1,2,3)` deg/s yields `Pitch=16, Yaw=-48, Roll=32`. Example accel normalized `(0.5,0.25,-0.5)` g yields `AccelX=8192, AccelY=8192, AccelZ=4096`.
2. A stationary nonzero gravity vector remains **nonzero physical acceleration**, not a fabricated zero/freefall placeholder.
3. `MsiClawMotionState.Unavailable`, null/missing provider, gyro-only, accel-only, stale gyro, stale accel => **all six native IMU fields zero**; other gamepad input remains untouched.
4. True gyro zero with valid accelerometer **is not discarded as unavailable**.
5. Very large positive/negative rate or acceleration saturates `short.MaxValue`/`short.MinValue` without wrap. Boundary ±32768 counts and declared rounding policy are deterministic.
6. Quaternion stays neutral, and the mapper's old no-motion signature behaves exactly as before.
7. M1/M2 suppression, ordinary buttons, triggers, sticks and unchanged trackpad defaults remain correct.

Use plain immutable fake `MsiClawMotionState` values; for freshness tests use its existing `WithFreshness` method with a fake monotonic time/frequency, not time-sensitive sleeps.

### 8.2 `CanonicalSteamDeckInputPublisherTests`

Use the **existing manual-tick fake sink**:
1. Each tick reads **one** current motion snapshot; the sink's typed SteamDeck state has valid encoded IMU.
2. Changing the provided snapshot affects the **next** tick without publisher restart, and the existing buttons/sticks continue to publish.
3. Mid-session invalidation/missing accel, stop/resume warmup, or null provider produces neutral IMU on the **next** tick without failing the publisher.
4. `SteamDeckSystemButtonOverlay` still overlays Steam/QAM pulses without clearing or mutating the IMU or breaking its expiry semantics.
5. One sample may repeat on multiple 4ms ticks: no extra physical polling at 250Hz and no interpolation requirement. Ensure no sensor API calls from publish operation.
6. Where feasible, preserve existing native typed struct contract checks including `Marshal.SizeOf<SteamDeckDeviceState>() == 76`, six sequential signed fields/offsets as in canonical VIIPER header. No runtime USB device needed for unit tests.

### 8.3 Wiring / lifecycle regression

Cover (with existing tests/production seams) the narrow provider wiring from `AddonProcessHost` through the **default** `MsiClawAddonPresentation` Deck factory, while existing custom injected factories and Xbox360 paths remain unaffected. Tests should not require real Windows sensors or a live VIIPER USB device. Preserve the PR1 tests for:
- normal Xbox360 = no sensor readers;
- BPM-only SteamDeck (AppID 0) = readers eligible;
- SteamDeck switch back to Xbox360 = stop;
- loss/hibernate/resume/authority release = PR1 invalidation and lifecycle preserved.

Avoid brittle new tests that parse large source files unless an existing contract test needs a minimal update.

## 9. Logging / performance

- Optional motion availability should be diagnosable with existing PR1 `ControllerMotion` startup/failure events; do not emit an event on **every 250Hz packet** or every 100Hz sensor sample.
- Preserve existing `SteamOutput` publisher heartbeat and SetState fault reporting. If useful, add at most a tiny aggregate availability counter/transition log; only if it answers a practical diagnosis need, not a new statistics subsystem.
- Expected gyro rate ~100Hz, accelerometer ~275–300Hz, native publisher logical tick 4ms/250Hz. Publisher reads a cached snapshot; **no waits, locks around sensor GetData, I/O, independent timers, extra scheduling, or report-rate conversion** on its hot path.
- Bounded allocation from the existing freshness snapshot is acceptable; avoid needless intermediate collections or report copies.

## 10. Explicit file scope

**Expected minimal production edit set:**
1. `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` — host callback binding.
2. `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` — optional provider passed only to default Deck publisher.
3. `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` — snapshot per tick and mapper argument.
4. `src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs` — six native IMU encodings.

**Expected tests:** existing `SteamDeckDeviceStateMapperTests.cs` and `CanonicalSteamDeckInputPublisherTests.cs`; narrow additional wiring/contract test only if genuinely required.

**Expected non-edits:** `MsiClawMotionState`/`MsiClawMotionSource` except a proven production defect; VIIPER native types/imports/header/dependency; `CanonicalXbox360InputPublisher`; `MsiClawAddonPhysicalOwnership`; Steam game/BPM detection; HidHide/native-mode logic; QAM/overlay/controller settings/front-end UI; install/package; sensor diagnostics.

No user-facing gyro toggle or settings page. Steam Input remains the user interface for motion activation and mapping.

## 11. Code completion / CI acceptance criteria

The local Codex PR is ready for review when:
- [ ] Current main/Full1902 authority and PR1 implementation have been rechecked.
- [ ] Both A2VM and CG3EM use the existing common PR1 snapshot; no new hardware dependency.
- [ ] The existing Deck publisher combines current physical `ControllerState` and one fresh coherent `MsiClawMotionState` in its normal native report.
- [ ] Six native signed IMU fields match the agreed axis/sign/scale table, with saturation and explicit validity gating; neutral when absent/stale.
- [ ] Quaternion/default unused fields and **all** pre-existing native gamepad behavior are preserved.
- [ ] Steam Input owns motion selection, mapping and sensitivity; BPM-only SteamDeck works with AppID 0; no new app UI or X360 gyro output.
- [ ] No sensor I/O or blocking operations have been added to DirectInput or VIIPER hot paths.
- [ ] Real Full1902 lifecycle safety (suspend/resume, loss/recovery, rollback/fail-close, VIIPER/HidHide/rumble teardown) remains unchanged.
- [ ] Relevant deterministic unit tests pass; restore/build and full existing test suites pass, with CI green.

Run and report:
~~~powershell
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

For PR review, distinguish only *realistic and material production defects* from nonblocking/theoretical synchronization/style concerns. **Hardware/real-device validation is NOT a required local Codex task, CI prerequisite, reviewer blocker, or merge gate.** Do not introduce speculative protective architecture.

## 12. Post-merge hardware verification — USER role only (NOT blocking)

The user may verify after the code PR is reviewed and merged:
1. On A2VM 8, open Steam BPM with **no running Steam game/AppID 0** and confirm Steam Input's Steam Deck gyro settings can see and use physical motion. Steam Input gyro-on/game-level mapping should not affect the Addon's PR1 reader eligibility.
2. Test pitch/yaw/roll one at a time; confirm expected motion direction and stationary gravity; observe drift, jitter and oversensitivity. Actual sign/orientation is **not proven until this test**.
3. Run a Steam game, configure Steam Input gyro-to-mouse or gyro-to-stick, validate game behavior and normal buttons/triggers/sticks.
4. Leave SteamDeck/BPM for Xbox360: ensure no Deck publisher or physical motion readers remain active according to the PR1 gate.
5. Repeat on EX / CG3EM to determine whether the legacy gyro's reference-based deg/s assumption and signs match actual physical behavior. A2VM 7 confirmation is also user-owned when available.
6. Test ordinary suspend/hibernate/resume, Runtime restart and physical PnP reconnection; ensure no stale motion after resume and no controller failure.
7. If drift or axis conventions need correction, collect logs and create a **measured narrow follow-up**; do not preemptively add a bias-calibration framework, quaternion fusion, axis settings or generic retry manager.

**Do not claim physical Steam Input acceptance before user testing.** Successful code tests and CI are sufficient for the implementation PR to merge if production logic is correct.

## 13. PR handoff / deliverable

Implement this as **one** narrow PR, clearly titled e.g. `feat: publish Full1902 gyro and accelerometer through Steam Deck VIIPER`. In the PR description include:
- concise architecture and exact encoding table;
- supported models and the SteamDeck-only/UI-free scope;
- list of changed files and unchanged Full1902 native ownership contracts;
- test/build results;
- explicit note that real A2VM/CG3EM Steam Input acceptance is **post-merge user verification**, not completed or required for merge.

Do not create a second PR or implement deferred Xbox360 gyro-to-stick in this work order.
