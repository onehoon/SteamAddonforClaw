# Work Order — Full1902 Windows-Idle Xbox360 Duplicate Native Submission Elimination

> Date: 2026-10-16  
> Repository: `onehoon/SteamAddonforClaw` / current `main`  
> Source baseline reviewed: `e90bb45450211aad0469623c998cbc7ae149cd0d`  
> VIIPER fork reviewed: `onehoon/VIIPER` at `17b9334645b4ede72de81a8d09d967189fb3fdc4` (the tracked Addon integration revision)  
> Windows USB/IP client reviewed: `vadimgrn/usbip-win2` release/tag `v.0.9.8.1`, the pinned installer version  
> Scope: Standalone Full PID1902, one interactive Windows user/session  
> Delivery: One focused code PR by local Codex; automated code/unit-test verification only. Physical-device testing is performed by the user **after merge** and is **not** a PR merge blocker.

## 1. Decision

Optimize **only** the Xbox360 native-state submission path when the Runtime's existing cached controller cadence decision is **idle (125 Hz)**.

- **No recognized Steam game, no BPM, no recognized XBOX game, observers healthy**: keep the current physical DirectInput **125 Hz / 8 ms** and Xbox360 publisher timer **125 Hz / 8 ms**. Compute the mapped Xbox360 state as today, but skip native `SetXbox360State` calls when it is byte/field-identical to the last successfully accepted state *and* a bounded refresh is not due.
- **Idle unchanged-state refresh**: submit the current state at least once every **250 ms** while the publisher is actively running, even if unchanged. This is a low-frequency native-state refresh, **not** a second timer, device health probe, or proof of attachment. A refresh can occur on the next existing 8-ms publisher tick after its deadline.
- **Any changed mapped Xbox360 state**: submit on the next existing publisher tick, without waiting for the 250-ms refresh.
- **Steam game, BPM, recognized XBOX game, or untrustworthy observer state**: keep the existing **250 Hz / 4 ms**, including **one native submission per production tick**. Do not optimize, throttle, filter, debounce, or idle-detect the game-active path. Steam/BPM continues using the unmodified SteamDeck (or existing selected Steam presentation) publisher.
- Preserve the explicit `IInputReportTickSource` test/manual-tick contract: **one sink call per supplied tick**, even for equal states. Apply suppression only to the real production Xbox360 worker, not the test-supplied tick path.

The purpose is to reduce C#→native VIIPER calls and Go-side state-channel writes on the Windows desktop. **Do not claim** it eliminates the 125-Hz physical reader, the 125-Hz publisher wakeups, or usbip-win2 interrupt transfers.

This work narrowly supersedes **only the idle Xbox360 "SetState on every tick" clause** in `docs/work-order/FULL1902_ADAPTIVE_IDLE_125HZ_CONTROLLER_CADENCE_WORK_ORDER_2026-10-10.md`. All other cadence, safety, fault, and timing requirements from that implemented order remain in force.

## 2. Source-of-truth documents and implementation reviewed

Read the latest versions at implementation time:

- `docs/Full 1902 Implementation/README.md` (document precedence).
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` (reboot-bound PID1902 ownership; exactly one attached virtual presentation).
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md` (real PnP/power/crash lifecycle).
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` (current privilege/process authority).
- `docs/VIIPER_INTEGRATION.md` and `docs/VIIPER_IMPLEMENTATION_RULES.md` (pinned native ABI, fail-close, unchanged attachment ownership).
- `docs/work-order/FULL1902_ADAPTIVE_IDLE_125HZ_CONTROLLER_CADENCE_WORK_ORDER_2026-10-10.md` (existing 4/8-ms decision and independent timers).
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs` (actual worker and manual tick source).
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs` (final buttons, analog values, live M1/M2 mapping and suppression).
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360NativeTypes.cs` (20-byte, fully blittable `Xbox360DeviceState`).
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs` (the one canonical native sink; neutral before detach; typed attach state is a different concern).
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` (sole attach/detach/feedback/pause/teardown owner).
- `tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs` (existing one-write-per-manual-tick, live mapping, sink fault, start/stop/restart contracts).
- `src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs` (official 0.9.8.1 installer selection).

### 2.1 Verified onehoon/VIIPER behavior

At the reviewed VIIPER revision:

1. `device/xbox360/device.go`: `Xbox360.UpdateInputState` replaces the most recent value in a **capacity-one** `inputCh`. It does not require every publisher tick to carry a duplicate.
2. `Xbox360.HandleTransfer`, IN endpoint 1, takes a state from `inputCh` to produce `BuildReport()`. OUT endpoint 1 separately dispatches rumble callbacks. Omitting unchanged **IN state setter** calls does not disable the **OUT feedback** path.
3. `internal/server/usb/server.go`, `HandleUrbStream`: each USB/IP connection owns a **fresh** `lastInResp` map. For an interrupt-IN request the server attempts the device transfer with an endpoint-interval timeout; if it times out and has a cached report it can answer with `lastInResp[ep]`.
4. **Important recovery boundary**: the cache belongs to the USB/IP connection, **not** the logical Xbox360 device. A recreated USB/IP connection can have no `lastInResp`, while an earlier connection already consumed `inputCh`. An indefinite identical-state skip can leave the new connection with no report until an unrelated input change. This is a realistically reachable reconnect/resume concern, not a hypothetical race.
5. The **250-ms refresh** periodically supplies a fresh `inputCh` state while the publisher runs, bounding this no-new-report condition without introducing any new attachment or retry owner. A normal tracked teardown/restart must also force the next publisher's first state write.
6. `CanonicalViiperRuntime.SetXbox360State` checks runtime readiness and dispatches the native setter. A successful setter call **does not prove** the USB/IP client remains attached, nor does it replace existing PnP or tracked-attachment verification.

Reference source:
- https://github.com/onehoon/VIIPER/blob/17b9334645b4ede72de81a8d09d967189fb3fdc4/device/xbox360/device.go
- https://github.com/onehoon/VIIPER/blob/17b9334645b4ede72de81a8d09d967189fb3fdc4/internal/server/usb/server.go
- https://github.com/onehoon/VIIPER/blob/17b9334645b4ede72de81a8d09d967189fb3fdc4/FORK_ARCHITECTURE.md

### 2.2 Verified usbip-win2 v0.9.8.1 boundary

- The Addon provisions the pinned official `USBip-0.9.8.1-x64.exe` from `vadimgrn/usbip-win2`. VIIPER supports the 0.9.8.0 and 0.9.8.1 tracked plugin ABIs, including their distinct request sizes; this work **does not modify** that compatibility selection.
- usbip-win2's UDE and `usbip2_filter` driver act as the USB/IP **client**. The pinned sources implement WSK receive and completion of USB/IP `RET_SUBMIT` responses into Windows URBs; the client does not consume the Addon's managed `SetXbox360State` calls directly.
- `onehoon/VIIPER` currently uses the zero-copy/`WskEvents=false` import setting. Keep the existing import, detach, and port-tracking behavior unmodified.
- usbip-win2's device USB interrupt activity can continue even when the Addon skips an unchanged native state write. This optimization **must not alter USB endpoint `bInterval`**, USB/IP requests/responses, WSK threading/transport mode, driver installation, or kernel behavior.
- The server-side `lastInResp` behavior above is the actual mechanism permitting duplicate native submissions to be omitted. Do not incorrectly attribute last-report caching to the usbip-win2 driver itself.

Reference source:
- https://github.com/vadimgrn/usbip-win2/tree/v.0.9.8.1
- https://github.com/vadimgrn/usbip-win2/blob/v.0.9.8.1/drivers/ude/wsk_receive_irp.cpp
- https://github.com/vadimgrn/usbip-win2/blob/v.0.9.8.1/drivers/ude/wsk_receive.cpp
- https://github.com/onehoon/VIIPER/blob/17b9334645b4ede72de81a8d09d967189fb3fdc4/docs/libviiper/fork-api.md

### 2.3 Comparable production designs (references, not copied contracts)

- **WSGM** `src/WSGM/Input/ViiperControllerBackend.cs`: encodes a target-specific report, compares it with the last **successfully** native-accepted report, and skips equal reports. Reset cache on device creation/removal/failure. WSGM has its **own VIIPER fork** and a documented USB endpoint replay/grid fix; do **not** infer that every WSGM USB/IP behavior is present in our pinned fork.
- **HHC** `HandheldCompanion/Managers/VirtualManager.cs`: skips insignificant unchanged virtual output using button/axis/gyro thresholds. The threshold policy is **not** appropriate for this scoped change because we must not introduce lost small real input or alter mapping semantics.
- **CTW** v0.3.98.0 historical source review: around 125-Hz SharpDX DirectInput virtual-mode loop; not evidence that the loop needs to stop at desktop idle.
- **WSGM perf report**: VIIPER's report and USB/IP work are a substantial part of that app's measured idle CPU. Those percentages are not an Addon benchmark; WSGM's attempted 64-ms USB endpoint idle replay caused real gyro stutter. We are **not** modifying any Deck/USB endpoint cadence.

References:
- https://github.com/KillerPixelCrew/WSGM/blob/master/src/WSGM/Input/ViiperControllerBackend.cs
- https://github.com/KillerPixelCrew/WSGM/blob/master/docs/perf/README.md
- https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Managers/VirtualManager.cs

## 3. Code change — minimal existing-worker-local cache

Primary production file: `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`.

Implement a small publisher-owned cache only:

- `Xbox360DeviceState` of the last successful native submission;
- one `bool` indicating the cache is valid;
- last successful submission's monotonic `Stopwatch` timestamp.

A `250 ms` `TimeSpan` constant is enough. Do not create a `NativeReportCache` class, input-idle state machine, separate detector, monitor, extra lock, second timer, task, report pipeline, or native abstraction.

The publisher has a single owned worker/tick execution path and already a well-defined stop/join boundary. Keep the cache local to the instance and touched only from the publishing path; invalidate on each new `Start()` **before** either production worker or explicit-tick task begins.

### 3.1 Match the final mapped output, not raw controller input

Always map through the **existing**:
`Xbox360DeviceStateMapper.Map(rawState, _backButtonMappingProvider(), _rearButtonSuppressionProvider(...), ...)`.

The mapping and both rear-button suppression providers must still be consulted at the existing tick cadence even when no native submission occurs. A mapping change, release, rear-button suppression change, neutralized raw snapshot, trigger, Guide, or stick change that affects the **final** output must trigger the next submission.

Compare **exactly** the 20-byte native-relevant output state, without adding axis deadzones, float thresholds, smoothing, or a physical input idle detector. Prefer an allocation-free explicit comparison of `Buttons`, `LT`, `RT`, `LX`, `LY`, `RX`, `RY`, and `Reserved0`–`Reserved5`. Do not use a `ValueType.Equals`/boxing or per-tick heap array/serialization operation. An inline comparator is sufficient; do not change the ABI struct to add a new interface.

### 3.2 Production worker decision

Use the **existing** `_requires250Hz` cached Runtime decision; do not create a game detector:

1. For each real production timer tick, determine whether the cached decision currently requires fast 250-Hz handling. **When fast: call the sink unconditionally** as before. Keep the rest of the existing timer scheduling logic and live 4/8-ms period re-arm.
2. When idle: calculate the mapped output, then:
   - no cached successful submission → invoke sink;
   - mapped output different from cached output → invoke sink;
   - at least 250 ms elapsed since last successful native submission → invoke sink;
   - otherwise → omit the native call and return successful continuation to the existing worker loop.
3. A skipped native call is **not** a successful native submission. It must not alter `PublishedStateCount`, the last accepted state, or the last successful submission timestamp.
4. Only after `_setState(mapped)` returns `true` update the cache, successful timestamp, and existing `PublishedStateCount`.
5. Native `false` or exception → **existing `ReportFault` and stop/fail-close behavior**. Do not cache failure or turn it into a benign skip.
6. No sleep, spinning, extra timer, forced neutral report, periodic PnP polling, or automatic native reattachment.

Implementation sketch for the worker-only seam (illustrative; fit exact current code and tests):

```csharp
// Production worker:
bool requireFast = _requires250Hz();
if (!PublishCurrentStateOnce(allowIdleDuplicateSkip: !requireFast))
    return;

// Keep existing deadline calculation/re-arm, including its current
// latest cadence read and timer-failure fail-close path.
```

```csharp
// In PublishCurrentStateOnce, AFTER existing final mapping.
// The explicit tick-source caller uses the default 'false' so its
// one-native-write-per-supplied-tick contract stays unchanged.
long now = _timestampProvider();
bool isEqual = _hasLastAccepted && StatesEqual(mapped, _lastAccepted);
bool refreshDue = !_hasLastAccepted ||
    Stopwatch.GetElapsedTime(_lastAcceptedAt, now) >= TimeSpan.FromMilliseconds(250);

if (allowIdleDuplicateSkip && isEqual && !refreshDue)
    return true;

if (!_setState(mapped))
{
    ReportFault(new InvalidOperationException(
        "Canonical VIIPER rejected a typed Xbox360 state."));
    return false;
}

_lastAccepted = mapped;
_hasLastAccepted = true;
_lastAcceptedAt = _timestampProvider();
_publishedStateCount++;
return true;
```

The comparator `StatesEqual`, `_hasLastAccepted`, and `_lastAcceptedAt` in this sketch represent simple fields, **not** an additional synchronization or ownership model. Handle `Stopwatch` timestamps consistently with the existing `_timestampProvider` seam. The 250-ms refresh is a *native state refresh*, not a claim of USB transport or PnP health.

Avoid changing `PublishAsync(IInputReportTickSource ticks,...)`: have it continue invoking the full per-tick sink path. Also preserve all existing fast/idle cadence switch behavior and preexisting production fault simulation seams.

### 3.3 Production lifecycle and output contracts

- **Initial Xbox360 attach**: presentation owner already writes neutral and starts the publisher. Its publisher's **first actual production tick** still must submit once; the last accepted state must never be inherited from a prior run/USBIP connection.
- **Game activation 125→250 Hz**: immediately continue full per-tick writes, without waiting for a 250-ms deadline, including recognized XBOX game while the virtual device remains Xbox360. Existing Steam/BPM switching still swaps presentations through its owner.
- **Return to Windows idle 250→125 Hz**: duplicate skipping may begin based on the last successful state. Continue 250-ms refreshes in the idle period, and keep the existing 4/8-ms physical/virtual scheduling.
- **Overlay capture**: existing publisher stop/join, direct neutralization of the same attached typed device, feedback drain, and capture release retain authority. `Start()` after capture clears the cache and forces a fresh live native report.
- **Suspend/Hibernate/Resume**: existing suspend pause, physical neutral reset, attached-device proof, and resumed publisher `Start()` remain authoritative. Clear the cache on restart; **never replay pre-suspend pressed state**.
- **PnP/device loss, detach/re-attach, native failure**: do not add per-tick USB/PnP checks. The existing physical input owner, presentation reconcile, and fail-close retain cleanup/recovery ownership. A native refresh alone is not device attachment proof.
- **Rumble**: native USB OUT callback is independent of the IN-state native setter. Preserve rumble feedback registration, physical STOP, teardown and callback drain exactly.
- **Runtime crash/shutdown, PID1901 restoration**: no changes to lifecycle owners, commit/recovery authority, HidHide, VIIPER remove, usbip-win2 install/uninstall, or MSI Center M.
- **Manual tick tests**: preserve one write per explicit tick and correct `PublishedStateCount`.

## 4. Telemetry and performance

This change is being pursued because the Runtime had observable Windows desktop idle CPU while no game, overlay, or Main UI was being used. **There is no source-supported attribution of that CPU percentage to the Xbox360 setter alone.**

- Do not emit a log line on every suppressed report or each 8-ms wakeup.
- If useful, keep two simple counters (e.g. native accepted vs idle duplicate suppressed) and write **one bounded Debug summary at a normal stop/lifecycle boundary**, without moving teardown responsibility. This summary is optional; avoid adding noisy logger overhead.
- Existing Debug cadence logs and Warn/Error real-failure logs stay intact. No fake successful `SetState` log/counter for a skip.
- A stationary desktop should reduce native setter calls from approximately **125/s** to approximately **4/s** after the initial submission (about **96.8% fewer setter calls**). This is a *call-count expectation*, **not** a CPU-saving guarantee.
- The physical input 125-Hz timer, publisher 125-Hz wakeups, native VIIPER USB/IP cached-response servicing, and usbip-win2 UDE URBs **continue running**. Do not claim that this PR cuts those rates.

## 5. Required automated tests (local Codex)

Update `tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs`. Use existing deterministic tick/time providers and production timer seams where available; prefer focused, deterministic unit tests over long wall-clock sleeps.

1. **Manual tick unchanged**: three equal supplied manual ticks produce three native sink calls and `PublishedStateCount == 3`. Existing test stays valid.
2. **First production tick**: even neutral state is sent once after `Start()`.
3. **Idle equal-state skip**: fake idle decision plus controlled time, the second identical production tick before 250 ms makes no additional native call. Counter remains the actual successful native-call count.
4. **Idle changed-state immediate**: button press and release, trigger and stick changes produce the next native call rather than waiting for refresh.
5. **Final mapping semantics**: change active M1/M2 mappings or suppression while raw input remains the same; send only when the final output changes, without altering mapping rules. Include a Guide/back-button mapping case if it is easy using existing test fixtures.
6. **250-ms idle refresh**: with constant state and a monotonic controlled clock, native setter fires again no later than the next existing worker tick after expiration; refresh timestamp advances only on success.
7. **Active 250-Hz path**: equal production samples still invoke the native setter every tick when `_requires250Hz` is true, including idle→fast and fast→idle transitions. Existing cadence/timer behavior and no extra timer remain true.
8. **Start→Stop→Start**: first tick of the new run submits even if equal to the last pre-stop state. This covers overlay release/resume publisher lifecycle without changing presentation code.
9. **Sink rejection/throw**: fail-close fault is reported exactly once and the worker exits; no failed state is cached or counted as accepted.
10. **Native ABI / safety tests remain unchanged**: existing VIIPER typed setter, neutral-on-detach, stop/join, and presentation cleanup tests pass without modifying VIIPER, usbip-win2, or the source's HID lifecycle.

If production scheduling makes a pure deterministic unit test disproportionately invasive, use the **smallest existing test seam** to invoke the production-only idle decision and simulate monotonic timestamps. Do not add a general scheduler abstraction or extra thread/lock solely to make the test.

Run the repository's applicable .NET test/build/verification commands with available local dependencies. Correct code/build/test defects before PR. **Do not require physical MSI Claw, Sleep/Resume hardware runs, USB/IP reinstall, or manual measured CPU results from local Codex; post-merge hardware evaluation is the user's responsibility and is not a PR-review blocker.**

## 6. Explicit exclusions / overengineering guardrails

Do **not** implement:

- lowering physical polling below 125 Hz, variable user-inactivity timer, 62.5-Hz mode, new game detector, Steam observer changes, or foreground polling;
- changes to SteamDeck/DualSense Edge publishers or 250-Hz game paths;
- global controller-report dedup across all virtual types, a new manager, interface/ABI/struct changes, or shared cache between presentation objects;
- USB endpoint `bInterval`, USB/IP `lastInResp` implementation, VIIPER Go source, usbip-win2 kernel driver, WSK mode, or prerequisite package changes;
- new attach/detach, network probing, recovery loop, redundant native lifetime authority, epoch, barrier, or lock for theoretical timing-only interleavings;
- new axis deadzones, jitter filters, threshold-based dropped input, special M1/M2 timing rules, or rumble command dedup;
- logs/metrics on every controller sample;
- implementation of future optimization ideas (physical-reader allocations, VIIPER Go URB allocation optimization) in the same PR.

Keep one existing owner each for physical input, presentation, native VIIPER lifecycle, and teardown. Protect realistic lifecycle failure paths; do not defend pathological artificial races by adding state machines.

## 7. Definition of done

1. **Only Windows idle Xbox360** suppresses redundant native state submissions with a **250-ms bounded refresh**.
2. Every changed mapped state reaches native on the next existing publisher tick.
3. All recognized game/BPM/observer-unready paths preserve full **250-Hz unthrottled behavior**.
4. The real production physical and Xbox360 timers remain **125 Hz** while idle; manual tick tests still submit once per supplied tick.
5. Each publisher `Start()` causes a first native write; 125/250 transition, neutral, overlay capture, suspend/resume, attachment, feedback, and fail-close semantics remain correct.
6. A native rejection/throw retains exactly the existing publisher fault/teardown policy; omitted duplicate sends never count as successful submissions.
7. No changes to the VIIPER binary, USB/IP driver, native ABI, or supported provisioning; no new polling/manager/lock layers.
8. Local Codex automated build/test verification passes; physical MSI Claw and CPU/latency validation is **post-merge user-owned**, never a required PR gate.

## 8. Suggested post-merge user hardware comparison (non-blocking)

User may compare **Info** and **Off** logging during 3–5 minutes at Windows desktop with Steam/BPM/XBOX game inactive and no controller input, checking Runtime CPU, general stability, first press/release after idle, one overlay capture/release, Steam game launch, XBOX game launch, and suspend/resume. If CPU improves little, profile VIIPER USB/IP and physical DirectInput cost separately before changing rates or native internals. These are optional user-side observations **after** a code-correct merge.

---

**Requested delivery:** local Codex implements this narrowly scoped work order in one focused PR. Do not widen it to all idle CPU optimization.
