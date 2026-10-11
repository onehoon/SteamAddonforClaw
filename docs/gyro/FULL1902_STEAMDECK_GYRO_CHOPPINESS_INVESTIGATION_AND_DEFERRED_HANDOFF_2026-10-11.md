# Full1902 Steam Deck Gyro Choppiness — Investigation and Deferred Handoff (2026-10-11)

**Status:** DEFERRED / unresolved. Historical evidence and next-step diagnostic plan, not an implementation work order.  
**Owner:** Steam Addon for Claw (standalone Full PID1902); VIIPER native virtual USB controller dependency.  
**Tested physical model:** MSI Claw 8 AI+ A2VM, model key 'msi.claw.a2vm.8'. Other Claw models were not proven by these October A2VM logs.  
**Last evidence:** Addon v0.1.365.0, 2026-10-11 evening test; VIIPER PR #59 already bundled.  
**Immediate decision:** Do not begin another timing/smoothing PR on the basis of these logs alone. Preserve the evidence and resume when prioritized.

## 1. Executive conclusion

A Steam Deck virtual presentation produces visibly **choppy gyro-to-mouse** according to user testing, while the DualSense Edge virtual presentation was previously reported smooth. The subjective reports are observations, not instrumented side-by-side measurements under identical Steam Input profiles, loads and machines.

The original **A2VM production sensor acquisition bottleneck** observed on Addon 0.1.359 was corrected in the subsequent polling-policy work. In the inspected **0.1.365.0** session, physical WinRT gyro accepted approximately **100 fresh samples/s**, and the canonical Steam Deck publisher usually sent approximately **250 SetState calls/s** with **zero SetState failures**. It is therefore incorrect to equate the ~250 Hz publisher with ~250 Hz *new physical gyro samples*.

There are real, **sporadic 50–150 ms reader/publisher delays** in the 365 test. These can cause transient stalls, but they have not been correlated to the reported persistent choppiness and do **not** establish the sole/root cause.

VIIPER **PR #59** aligned Steam Deck USB interrupt metadata and software wait policy more closely to reference Deck behavior, with cross-device regression coverage. Its code was included in **Addon v0.1.365**, but external testing still reported choppy gyro. This lowers the priority of the original 'bInterval=6ms alone' hypothesis; it does not measure the host-received packet stream or rule out *all* USB/IP timing behavior.

**Unmeasured boundary:** VIIPER USB/IP controller interrupt IN completion -> Windows HID -> Steam Input consumption, including report/frame repetition, host packet timing and device-specific gyro-to-mouse processing. No current evidence proves a particular Steam Input filter or timestamp mismatch is responsible.

## 2. Product/architecture scope

The relevant path is:

~~~text
MSI Claw PID1902 owned physical motion sources
  A2VM WinRT Gyrometer (~100 new samples/s)
  A2VM legacy Physical Accelerometer (variable ~220–273 samples/s)
         |
         v
Addon Full1902 shared motion snapshot (acquisition / freshness)
         |
         v
Canonical Steam Deck publisher (~250 SetState calls/s)
         |
         v
VIIPER Steam Deck typed device (64-byte, 28DE:1205, report type 0x09)
         |
         v
VIIPER USB/IP -> usbip-win2 -> Windows HID -> Steam Input
         |
         v
Steam Input 'Gyro To Mouse' -> game cursor/camera
~~~

The app is **standalone**; CTW IPC or integration is not part of the design. Existing one-user/one-interactive-session Full1902 controller authority, HidHide, VIIPER ownership, PID1901/PID1902 restoration, fail-close and teardown are preserved. This report does not propose a new motion owner, virtual publisher, filter manager, locking framework or alternate authority.

Primary architecture:
- [Full1902 document authority](../Full%201902%20Implementation/README.md)
- [Approved gyro implementation plan](FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md)
- [Debug-only gyro jitter diagnostics work order](../work-order/GYRO_DEBUG_SENSOR_CADENCE_JITTER_DIAGNOSTICS_WORK_ORDER_2026-10-11.md)
- [Probe-matched production polling work order (Gyro PR3)](../work-order/GYRO_PR3_FULL1902_MATCH_PROBE_POLLING_WORK_ORDER_2026-10-11.md)

## 3. Test/change chronology (do not conflate runs)

| Checkpoint | Source or change | Observation / disposition |
| --- | --- | --- |
| A2VM probe before gyro PR3 | Claw Sensor Probe | Gyro ~100 Hz; accel roughly 300 Hz in the independent diagnostic environment; *not* an output-stream measurement. |
| Addon **0.1.359.0**, 2026-10-11 | [Gyro PR3 work order and linked 359 recording](../work-order/GYRO_PR3_FULL1902_MATCH_PROBE_POLLING_WORK_ORDER_2026-10-11.md) | Production gyro **37.10 Hz** (mean accepted gap 26.96 ms), accel **64.61 Hz** (mean 15.48 ms), Steam Deck publisher ~250 Hz, gyro receive-timestamp reuse ~85.1%. Extra application wait after accepted reads diagnosed as a production-vs-Probe policy mismatch. |
| Addon **0.1.360.0** follow-up | [A2VM 360 test log](https://drive.google.com/file/d/1bSbXM__bELwr5fVSX6bCqYw7VuWmxoFp/view) | Earlier review found gyro approximately **100 Hz**, accel approximately **245–252 Hz** during reviewed intervals and ~60% gyro reuse at 250 Hz output. Different capture from 365; do not substitute its metrics for the 365 dataset. |
| VIIPER **PR #59** | [PR #59](https://github.com/onehoon/VIIPER/pull/59), [squash merge 17b9334](https://github.com/onehoon/VIIPER/commit/17b9334645b4ede72de81a8d09d967189fb3fdc4) | Steam Deck controller endpoint USB 'bInterval' **6 -> 1**, separately preserve ~**4 ms** async IN wait budget; coalesced latest-state notification/fallback, tests including Xbox360 and DualSense Edge regression coverage. This is **not** a generic 1 kHz IMU acquisition or output-rate change. |
| Addon **0.1.365.0** | [Release v0.1.365](https://github.com/onehoon/SteamAddonforClaw/releases/tag/v0.1.365), [VIIPER dependency lock at tag](https://github.com/onehoon/SteamAddonforClaw/blob/v0.1.365/src/SteamInputAddonforClaw/Dependencies/Viiper/viiper.lock.json) | Dependency pinned to VIIPER merge commit **17b9334**, release CI/publish validation passed; user still reported choppy Steam Deck gyro. |
| Addon **0.1.365.0** local diagnostic | Two 2026-10-11 log files specified below | Reader/publisher generally at ~100/250 Hz, with several significant sporadic pauses. Actual USB/IP report/Steam-consumption timing **not logged**. |

**Subjective observations from discussion:** DualSense Edge virtual gyro was described as smooth; Steam Deck gyro-to-mouse remains choppy on 365. Earlier discussion also noted gyro-to-right-stick felt more acceptable than gyro-to-mouse. These are useful distinctions, not proof of a single packet or Steam Input root cause.

## 4. Exact 0.1.365 log evidence (2026-10-11, +09:00)

Files inspected, supplied with the investigation, **not checked into GitHub** (device log identifiers/private paths should remain outside source control):
- 'SteamInputAddonforClaw-2026-10-11-222616.050-P6160-L15eb818805(1).log' — 793 lines; launch header explicitly 'Version=0.1.365.0'; Debug enabled.
- 'libVIIPER(2).log' — 1,004 lines; **append/history across multiple preceding sessions** (begins at 16:45, while this Addon process starts at 22:26). Correlate by actual timestamp and session; do not treat older errors as belonging to the 23:01 gyro test.

Timeline observed in the Addon log:
- **22:26:16** App 0.1.365.0 starts.
- **22:42:14** suspend event; **23:01:00** resume, with 'Suspend quiesce ... Outcome=TimedOutOrFailed ... FinalPowerState=Unsafe'. This lifecycle issue is **separate/unattributed** to gyro choppiness; investigate against the Full1902 lifecycle contract separately if reproducible.
- **23:01:20** virtual presentation switches Xbox360 -> SteamDeck.
- **23:01:21** motion readers select WinRT Gyrometer + legacy Sensor API Physical Accelerometer.
- **23:01:52** Steam 'RunningAppID=4126040' present (active game context).
- **23:04:15** switches SteamDeck -> Xbox360. The logged SteamDeck period therefore includes a game run, not only BPM menus.

Reader configuration on this run:
- Gyroscope: WinRT Gyrometer, min/requested/effective interval **10 ms**.
- Accelerometer: legacy Sensor API, source candidate minimum **2 ms** (not an authoritative measured ODR).
- Both: 'NonFreshBackoffMs=1', **'FreshAcceptedExtraWaitMs=0'**. That is the intended Probe-matched rule; no return to 359's extra wait.

### 4.1 Ten-second summary aggregate

Computed from **17 x approximately 10-second 'MotionReaderDiagnosticsSummary' windows** for each sensor role, and 17 'MotionImuPublishDiagnosticsSummary' windows. The table gives per-window *median* or observed minimum/maximum, **not** a new independent per-packet capture.

| Metric | Observed on 365 |
| --- | --- |
| Gyro 'ActualFreshHz' | **97.49–100.03 Hz**, median approximately **99.99 Hz** |
| Gyro fresh receive interval mean | per-window approximately **10.0 ms** |
| Gyro worst fresh receive gap | **151.724 ms** |
| Gyro counted intervals >20 / >50 / >100 ms | **22 / 3 / 2**, summed across 17 windows |
| Accelerometer 'ActualFreshHz' | **219.13–272.58 Hz**, median approximately **244.18 Hz** |
| Accelerometer worst fresh receive gap | **154.897 ms** |
| Accelerometer counted intervals >20 / >50 / >100 ms | **42 / 3 / 1** across 17 windows |
| Accepted fresh sensor samples | gyro **16,945**, accel **42,229** over summarized windows |
| Duplicate sensor timestamps / no-data | **0 / 0** reported for both roles in the 17 windows |
| Publisher 'PublishTickCount' per 10-second window | **2,451–2,501**, generally approximately 2,500 |
| Publisher IMU usable ticks | all ticks in every window after the first startup window; initial window **2,262 usable / 234 unavailable** |
| Publisher gyro receive timestamp reuse | typically ~**1,500 repeated / 1,000 unique** per 2,500-tick 10-second window (~**60% reuse**). Expected when 100-Hz gyro is read into 250-Hz publication; **not automatically a bug**. |

The separate one-second 'Canonical Steam Deck publisher heartbeat' recorded **174 windows** with **zero 'SetStateFailures'**. 'EffectiveSetStateHz' was generally near 250; its lowest observed window was **215.66 Hz**, and the maximum observed 'MaxWakeToWakeMs' was **143.835 ms**.

### 4.2 Noteworthy long gaps

The timestamp below denotes **the summary log emission / window end**, not the exact instant within the preceding window when the long stall occurred.

| Summary emitted (+09:00) | Observed window maximum | Notes |
| --- | --- | --- |
| **23:01:51** | gyro fresh receive gap **59.50 ms** | Isolated >50-ms gap. |
| **23:03:11** | gyro **116.36 ms**; accel in nearby summary **54.06 ms** | Reader stalls possible; no exact shared causal event proven. |
| **23:03:41** | Steam Deck publisher wake gap **143.835 ms** | Heartbeat had **227 calls**, **215.66 effective Hz**, 'MaxWakeLatenessMs=140.0936', zero SetState failures. |
| **23:03:51** | gyro **151.724 ms**, accel **154.897 ms** | 10-second reader windows both included >100-ms gaps; do not assume the two maxima occurred on the same instruction or were caused by USB/IP. |

These are **realistic transient input-quality anomalies** worth retaining in a future investigation. They can produce brief noticeable stalls; the diagnostic counters alone cannot establish that they cause the user's continuously described 'choppy' feel.

### 4.3 VIIPER server/transport evidence and limits

The VIIPER file shows successful localhost USB/IP attach/import/detach cycles. The linked 23:01 SteamDeck session does not show a continuous device disconnect/re-enumeration loop. Some 'URB completion after disconnect / connection aborted' entries occur at detach and are *not by themselves* evidence of sustained data loss during gameplay.

**Important separate finding:** An earlier **18:51:48** VIIPER session (before the 22:26 Addon launch) logs native detach 'unsafe-outcome-unknown' and server 'close-failed'. This must **not** be used to explain the 23:01 gyro run without session correlation. It may warrant independent lifecycle analysis if requested.

VIIPER currently does **not** log per-controller-interrupt summary metrics adequate to answer:
- actual successful Steam Deck **EP 0x83 USB/IP IN completions/s** and inter-completion gap distribution;
- host-visible **Frame** advancement, repeats and skips (separate from Addon's SetState calls);
- wait-for-state notification vs 4-ms deadline fallback counts;
- stale last-response reuse, cancellations or aborted completions during an active steady session.

**Do not claim PR #59 demonstrably fixed host-visible report jitter just because SetState ≈250 Hz or release CI passed.** The host/Steam boundary was not measured.

## 5. Protocol/reference comparison completed

### 5.1 Classic Steam Controller vs Steam Deck in VIIPER

There are **two distinct VIIPER typed devices**; do not confuse 'Classic Steam Controller' with the product string used by a Steam Deck.

| Property | VIIPER Classic Steam Controller | VIIPER Steam Deck |
| --- | --- | --- |
| Source | [device/steamcontroller](https://github.com/onehoon/VIIPER/tree/main/device/steamcontroller) | [device/steamdeck](https://github.com/onehoon/VIIPER/tree/main/device/steamdeck) |
| VID:PID | **28DE:1102** | **28DE:1205** |
| Valve 64-byte input type | **0x01**, classic | **0x09**, Deck |
| Accelerometer bytes | **28–33** | **24–29** |
| Gyro bytes | **34–39** | **30–35** |
| Linux/default advertised sensor interval | **9000 us** (~111 Hz) | **4000 us** (250 Hz) |
| VIIPER USB controller endpoint interval at time of inspection | **6 ms** | **1 ms** after PR #59 |
| VIIPER interrupt IN behavior | Immediately snapshot latest state | Coalesced new-state wakeup / bounded 4-ms wait; latest-state deadline fallback |
| Classic IMU activation | Default off; host can enable raw gyro/accel via setting '0x30' | Raw accel + raw gyro enabled by default |
| Embedded dedicated sensor timestamp | None in either 64-byte state report | None |

The **9ms versus 4ms** figures are driver/device-advertised sensor update assumptions, not measured transport results. Classic behavior is *not* a demonstrated solution for Deck choppiness; simply copying its 6ms endpoint or immediate snapshot response is unjustified.

Cross-reference:
- [VIIPER Classic state encoding](https://github.com/onehoon/VIIPER/blob/main/device/steamcontroller/inputstate.go)
- [VIIPER Deck state encoding](https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/inputstate.go)
- [SDL Classic Steam Controller](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_steam.c)
- [SDL Steam Deck](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_steamdeck.c)
- [Linux 'hid-steam.c'](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-steam.c)

### 5.2 SDL/Linux: sensors, axes, time assumptions

- SDL Classic explicitly writes 'SETTING_IMU_MODE' with raw accel + raw gyro when sensors are enabled; it obtains 'ATTRIB_CONNECTION_INTERVAL_IN_US' (default 9000 us) and advances its **software sensor timestamp** per accepted state packet.
- SDL Deck treats sensors as **already enabled**, states 'Always 1kHz according to USB descriptor, but actually about 4 ms', uses **4000 us** for its software sensor timeline, and validates the full 64-byte Deck input frame.
- Linux 'hid-steam.c' defaults to **9000 us Classic / 4000 us Deck**, can read a device-advertised interval, and increments the software sensor timestamp for received sensor reports. This does **not** prove Steam for Windows follows precisely SDL/Linux's internal timestamp calculations.
- Both have **16 gyro counts/(degree/s)** and **16,384 accel counts/g**. The device-to-SDL axis transform differs in sign between Classic and Deck for the corresponding gyro Y/Z-derived axis; copying classic sign/order into Deck is incorrect.

### 5.3 HHC reference implementation (comparison, not dependency)

Reviewed [HHC SteamDeckTarget.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamDeckTarget.cs), [HHC SteamControllerTarget.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamControllerTarget.cs), [MotionManager](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Managers/MotionManager.cs), [VIIPERTarget](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/VIIPERTarget.cs).

HHC Deck target:
- 'MasterIntervalOverrideHz => 250', 'GamepadMotion.GetRawGyro()' / 'GetRawAcceleration()', then gyro x16 and accel x16384 with axis conversion into offsets 30–35 / 24–29.
- Obtains a live quaternion from 'GetQuaternion()' and writes it at offsets 36–43. **Our output does not currently publish an equivalent live quaternion.** This is a real difference, but not demonstrated as the cause of choppiness; do not add a static/incorrect quaternion as a speculative fix.
- **Declared report-header length differs:** HHC builds a 64-byte array but sets 'data[3] = 56'; current VIIPER declares **64**. SDL Deck checks '**ucLength == 64**', and [HIDMaestro real-capture-derived Deck profile](https://github.com/hifihedgehog/HIDMaestro/blob/master/profiles/valve/steam-deck-composite.json) describes header '01 00 09 40'. Do **not** blindly copy HHC's 56 field. HHC's older VIIPER integration uses a different report path and final-wire parity was **not captured** in this review.
- No standalone 'Deck-only smoothing' mechanism was identified in this target's raw IMU serialization. HHC MotionManager does contain calibrated/filtered channels for other purposes, but the Deck target selects **Raw**.

No documented HHC real-device A/B test establishes that its emulated Steam Deck gyro is smoother than ours. HHC is a **reference code comparison**, not a proven performance baseline.

## 6. Hypothesis disposition (evidence vs inference)

| Candidate | Current status |
| --- | --- |
| 359 extra application sensor wait | **Confirmed then fixed**: 359 37/65Hz -> later ~100/245Hz. Not seen again in 365 configuration. |
| 365 sustained source starvation | **Not supported**: approximately 100Hz gyro across stable windows. Isolated stalls **do** exist. |
| 250Hz virtual SetState = 250Hz unique gyro | **False**; ~60% repeated gyro receive timestamps expected at 100Hz physical. |
| Steam Deck old 6ms USB bInterval alone causes choppy gyro | **Deprioritized** after PR #59, which was included in 365 yet the complaint remained. |
| Bad gyro unit/field placement | **No discrepancy found** versus SDL/Linux/HHC reference byte positions/scales. |
| Classic Steam Controller implementation is a better remedy | **Unproven**; different type, rate, protocol, axes, and activation. |
| HHC live quaternion solves it | **Unproven**; observable implementation difference only. |
| USB/IP interrupt completions are irregular, stale, duplicated or improperly paced | **Open**, because existing VIIPER log lacks required measurement. |
| Steam for Windows uses a Deck-specific gyro-to-mouse algorithm/timebase that reacts differently than Edge | **Open, plausible**, not directly observable from current logs or proven by SDL/Linux. |
| 100–150ms sporadic motion/publisher stalls produce transient visible hitches | **Plausible**, but events are not correlated to user-visible timestamps or complete sustained choppiness. |

## 7. Deferred restart plan (smallest evidence-first next action)

When this work is resumed:

1. **Freeze the known baseline**: Addon 0.1.365 / VIIPER 17b9334, existing exact SteamDeck 64-byte packet, 4ms requested software cadence, same Full1902 authority. Do not reintroduce the 359 sensor wait.
2. **Define one reproducible user-owned A/B scenario** if needed: same physical A2VM, same Steam game/camera or Gyro To Mouse test, Steam profile/sensitivity/filters recorded, Deck vs Edge, stable rotating movement. This is a post-merge/user validation activity, **never a mandatory Codex/CI/PR blocker**.
3. **If new code is justified, create a separate tiny, DEBUG-ONLY VIIPER diagnostic PR** that aggregates controller EP0x83 interrupt IN completions, report Frame unique/repeated/skipped, new-state wake/deadline/last-cache fallback counts and max/mean inter-completion gap per 10s. Count on current host-transfer paths, no hot-path per-packet log output, no new worker/timer/manager and no behavioral changes; only minimal cross-device (Xbox360/Edge) regression tests. Distinguish USB/IP **server completion** from confirmed Windows/Steam HID **consumption**.
4. **Correlate** the VIIPER 10-second windows to existing Addon 'MotionReaderDiagnosticsSummary', 'MotionImuPublishDiagnosticsSummary' and publisher heartbeat under the same clock/session. If server completions are regular and Frames advance, inspect Steam Input settings and Deck-specific processing next. If server data stalls, narrow the existing VIIPER/usbip-win2 path first.
5. **Treat rare reader and publisher stalls separately** unless evidence links them to frequent choppiness. No interpolation/smoothing, guessed Steam filters, new synchronization architecture or arbitrary high-rate resampling solely to mask them.
6. Preserve user-owned post-merge device testing and existing real lifecycle safety (sleep/hibernate/resume, crash/restart/shutdown, PnP, routing rollback/fail-close, HidHide, VIIPER teardown).

**Not authorized by this research alone:** a production patch, unverified claim of Steam Input root cause, protocol-layout changes, a switch to Classic Steam Controller presentation, or a new UI/feature.

## 8. Source / evidence index

**Addon repository and dependency**
- [Addon gyro document index](README.md)
- [Gyro PR3 work order, with 359 quantified baseline and probe evidence](../work-order/GYRO_PR3_FULL1902_MATCH_PROBE_POLLING_WORK_ORDER_2026-10-11.md)
- [VIIPER Steam Deck cadence work order](https://github.com/onehoon/VIIPER/blob/main/docs/work-order/STEAM_DECK_USBIP_IMU_REPORT_CADENCE_PARITY_WORK_ORDER_2026-10-11.md)
- [VIIPER PR #59](https://github.com/onehoon/VIIPER/pull/59) and [merged commit](https://github.com/onehoon/VIIPER/commit/17b9334645b4ede72de81a8d09d967189fb3fdc4)
- [Addon 0.1.365 dependency lock](https://github.com/onehoon/SteamAddonforClaw/blob/v0.1.365/src/SteamInputAddonforClaw/Dependencies/Viiper/viiper.lock.json)
- [A2VM 359 baseline recording](https://drive.google.com/file/d/1jO4ObgyFFQSuzK825gfYfz68GrvUXHAx/view)
- [A2VM 360 follow-up recording](https://drive.google.com/file/d/1bSbXM__bELwr5fVSX6bCqYw7VuWmxoFp/view)
- Addon 365 diagnostic: user-supplied 2026-10-11 log files named in section 4. They were **not** published or embedded in the repo.

**External code references**
- [SDL Classic Steam Controller](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_steam.c)
- [SDL Steam Deck](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_steamdeck.c)
- [Linux hid-steam](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-steam.c)
- [HHC SteamDeckTarget](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamDeckTarget.cs)
- [HHC SteamControllerTarget](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamControllerTarget.cs)
- [HIDMaestro captured Deck composite profile](https://github.com/hifihedgehog/HIDMaestro/blob/master/profiles/valve/steam-deck-composite.json)

**End state (2026-10-11):** diagnostic/research handoff complete; choppiness unresolved; development intentionally deferred.
