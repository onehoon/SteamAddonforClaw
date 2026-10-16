# Full1902 Windows Idle CPU / VIIPER Investigation — Handoff (2026-10-16)

> **Status:** Investigation / experimental next step; no production optimization approved.
>
> **Scope:** Standalone Full1902 SteamInputAddonforClaw, Windows desktop idle with the physical MSI Claw controller owned as PID1902 and the canonical Xbox360 virtual presentation attached.
>
> **Current decision:** Keep PR #806 Xbox360 idle state deduplication, PR #808 diagnostic logging, the existing 125-Hz physical/publisher idle cadence, 250-ms native state refresh, and all Full1902 safety rules. Start with a bounded **Addon-only diagnostic A/B experiment** of the VIIPER USB/IP write-batching interval. Do not start by replacing a live DLL or refactoring USB/IP.
>
> **Who does what:** Local Codex writes and builds code when explicitly requested. The user performs physical-device verification. Real-device validation is **not** a prerequisite for opening/reviewing/merging an otherwise code-correct PR.

## 1. Why this investigation exists

The user directly observed Windows Task Manager Runtime CPU fluctuating roughly **0.2% to 1.x%** while the machine was visually idle. This is an observed product symptom, not something to dismiss by asking for yet another baseline capture.

The investigative question is **which production path causes repeated Runtime scheduling/CPU activity during an attached, healthy, idle Xbox360 session**, and whether the cost can be reduced without changing controller latency, neutral output, input freshness, or cleanup guarantees.

Importantly, the thousands of ETW **ReadyThread** events are **not thousands of physical DirectInput reads** or calls to the native Xbox360 setter. ReadyThread means a thread became eligible to execute; several readiness transitions can arise from one timer, completion, socket event, or scheduler interaction. They are not CPU-time measurements and must not be equated to user-visible report deliveries.

## 2. Evidence / source of truth

### 2.1 Captures and reports

Original CPU4 capture and investigation reports were prepared locally under:

- **CPU4:** C:\GoogleDrive\Addon\Log\AV2M\CPU4\addon-idle-verbose.etl
- Original report: FULL1902_WINDOWS_IDLE_CPU4_ETW_REPORT.md
- Log-correlated revision: FULL1902_WINDOWS_IDLE_CPU4_ETW_REPORT(1).md
- Targeted attribution: FULL1902_WINDOWS_IDLE_CPU4_TARGETED_ATTRIBUTION.md
- Timeline correction: FULL1902_WINDOWS_IDLE_CPU4_TIMELINE_VALIDATION_ADDENDUM.md
- Same-session application / VIIPER logs were inspected for the final correlated CPU4 report; consult the user's Google Drive **Addon/Log** first if investigating logs further.

CPU4 folder in Google Drive: https://drive.google.com/drive/folders/1LlkBOWf2jphQ1hP9JWszCklvBBrNLypJ

Trace identity: SHA-256 **DFA933CF555E3EA63523092F558950767BDE0473ADB84D86CEC79AD60D05BC54**; Runtime PID **7424**; App version **0.1.390.0**, product commit **49777be9fce9162ccc9acccf6fb684a6c28dbd63**; MSI Claw 8 AI+ (A2VM), Intel Core Ultra 7 258V, 8 logical processors, Windows build 26300; local trace interval **2026-10-16 18:27:03.877–18:28:48.684 KST**.

The reports are the source for the numbers below; this handoff is not a claim to have independently reprocessed the raw 327-MB ETL again.

### 2.2 Product architecture authority

Read the active documents **together**, in their stated precedence order:

- [Full1902 document authority](Full%201902%20Implementation/README.md)
- [Reboot-bound controller authority and HidHide](Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md)
- [Full1902 implementation architecture](Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md)
- [VIIPER integration contract](VIIPER_INTEGRATION.md)
- [VIIPER implementation rules](VIIPER_IMPLEMENTATION_RULES.md)
- [Idle Xbox360 native submission work order](work-order/2026-10-16_FULL1902_WINDOWS_IDLE_XBOX360_DEDUPLICATED_NATIVE_SUBMISSION_WORK_ORDER.md)

The historic work order examined a different earlier VIIPER revision; **the actual CPU4 shipped DLL and the current Addon dependency pin are e3275bc156fc5dd1cf8c1f2798c5770f3257dd58.** Prefer the exact pinned revision over historical code examples.

VIIPER reviewed source: https://github.com/onehoon/VIIPER/tree/e3275bc156fc5dd1cf8c1f2798c5770f3257dd58

### 2.3 Work already completed

- **PR #806:** During idle, the Xbox360 publisher skips an unchanged native state submission until the bounded **250-ms** refresh is due. Changed states are submitted promptly; active (250-Hz) behavior, failure reporting, and teardown/neutral contracts are preserved. This optimization does **not** stop the physical input timer, publisher worker timer, or the USB/IP host-side URB stream.
- **PR #808:** The existing DEBUG **CadenceApplied** event includes **IdleReportDedup=On** at **125 Hz / 8 ms** and **IdleReportDedup=Off** at **250 Hz / 4 ms**, on startup and cadence transitions only. This is a mode/eligibility indicator, **not** a counter of skipped or successful USB/IP/native submissions.
- **PR #808:** Production A2VM automatic rumble mode cycling and the special Windows session-end PID1901 command were disabled; this separate controller lifecycle change must remain untouched during CPU experiments.

## 3. CPU4 ETW results — corrected, not the original mistaken rates

### 3.1 Important event-type coverage correction

The original CPU4 report divided CSwitch CPU and ReadyThread counts by the **104.807-second ETL span**. The timeline addendum established that **all system CSwitch and ReadyThread events stop at trace-relative 82.984 seconds**, while sampled Profile events continue near trace end. No ETW lost buffers/events were reported, but that does not fill the missing CSwitch/ReadyThread interval.

| Metric | Correct interpretation |
| --- | --- |
| ETL overall duration | 104.807 s |
| CSwitch + ReadyThread event coverage | **82.984 s** (0–82.984 s only) |
| Runtime CPU time from CSwitch | **15.003217 CPU seconds during the covered 82.984 s** |
| Runtime mean CPU during covered window | **180.8 ms CPU/s; 2.26% of 8 LP** |
| Runtime-target ReadyThread count | **339,547 in the covered 82.984 s** |
| Runtime-target ReadyThread rate | **4,091.7 / second during covered window** |
| System ReadyThread rate | **6,732.5 / second during covered window** |
| Original, now superseded CPU4 full-span rate | 1.789% / 3,239.7 ReadyThread per second |
| Profile coverage | Sampled across ~104.6 s, a **different** window |

**Do not** use 1.789% or 3,239.7/s as the corrected CPU4 rates. Do not assert that Runtime threads stopped at 83–90 s: Profile samples show PID 7424 still executing near the end. Do not extrapolate the 82.984-s CSwitch result to the missing final 21.8 seconds.

### 3.2 CPU1, CPU2, CPU4 comparison

| Capture | Version | Runtime CPU (8 LP) | ReadyThread target/s | Comparison limitation |
| --- | --- | ---: | ---: | --- |
| CPU1 light | 0.1.389 | ~1.069% | ~3,966 | Source capture/settings and healthy controller state not established as comparable |
| CPU2 verbose | 0.1.389 | ~0.0466% originally reported | N/A as healthy baseline | **Physical controller acquisition failed**; exclude as a healthy idle baseline |
| CPU4 verbose, corrected | 0.1.390 | **2.26%** | **4,091.7** | Heavy trace instrumentation; CPU time and ReadyThread covered only 82.984 s |

CPU1 and CPU4 ReadyThread rates are numerically similar, but **no performance regression or PR #806 effectiveness can be inferred** because capture modes, workloads and builds differ. Likewise, the user's Task Manager observation (0.2–1.x%) is real user-reported behavior, not contradicted by a higher heavily traced CPU4 figure.

### 3.3 Profile and stack observations

- Runtime Profile total was ~13.050 s sample-weight equivalent across the profile window; ~9.937 s (76.1%) was directly sampled inside ntoskrnl.exe, ~1.714 s (13.1%) directly inside libVIIPER.dll; identified CLR modules ~0.301 s.
- In stack-bearing Runtime Profile data, **3,712 directly sampled IPs** were in recognizable ETW recording / stack walk / unwind functions. Roughly **3.7 s at a 1-ms sampling period** is sample-equivalent evidence of material tracing overhead, **not** precisely removable product CPU time. ntoskrnl's entire 9.937 s must **not** be assigned to VIIPER, USB, or physical HID I/O.
- In the original reports, the seven leading Runtime TIDs were **13532, 7292, 10740, 13108, 13096, 13072, 8276**. Together they account for ~89.2% of covered Runtime CSwitch CPU. Their sampled stacks recurrently included libVIIPER, GQCSEx, wait/event/completion routines and timers. Direct VIIPER IP samples establish native execution, but the exact Go function and the source of each wakeup are **not identified**.
- A previous claim that ~97% of a selected stack-expanded subset's ReadyThread events originated from Idle was a **denominator error**. The corrected Idle share for those seven TIDs is **132,773 / 303,289 = 43.78%**. Among all Runtime-target events the source split is approximately **49.7% Runtime / 47.9% Idle / 2.4% other**.
- Source stack *occurrence* and actual CPU execution at an instruction pointer must not be confused. The available ETL has no measured DPC/ISR time, no current per-component native call/skip counters, and no reliable Go function symbols for this exact DLL.

### 3.4 Correlated product state during CPU4

The later report cross-referenced same-PID Runtime/VIIPER logs:

| Local time | Evidence |
| --- | --- |
| 18:24:50.916 | Background Runtime PID 7424 started |
| 18:25:04.218–18:25:04.550 | Physical PID1902 DirectInput acquired; FirstValidState true; ownership Owned |
| 18:25:04.681–18:25:04.694 | VIIPER USB/IP / canonical Xbox360 presentation attached, publisher started |
| 18:25:19.292–18:25:19.296 | Physical and Xbox360 publisher timers set to **125 Hz / 8 ms**, IdleReportDedup On |
| 18:27:03.877–18:28:48.684 | CPU4 ETL interval; no per-tick runtime health heartbeat |
| 19:13:01.234–19:13:01.257 | Same Runtime final VIIPER detach and teardown |

The lifecycle bracket strongly supports **attached Xbox360 + owned physical PID1902 during CPU4**, but does not prove every individual physical read / USB/IP report succeeded. SteamRunningAppId=0 and XboxActive=False confirm this was not an active recognized game scenario. The Overlay process was hidden during this trace.

## 4. Exact currently-pinned code and hypotheses

### 4.1 P1 — 1-ms periodic VIIPER USB/IP batching flush

**Addon source:** [CanonicalViiperRuntime.cs](../src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs) configures the one canonical server with:

~~~csharp
var config = new USBServerConfig
{
    Addr = address,
    ConnectionTimeoutMs = 30_000,
    DeviceHandlerConnectTimeoutMs = 5_000,
    WriteBatchFlushIntervalMs = 1
};
~~~

**Pinned VIIPER sources:**

- [lib/viiper/server.go](https://github.com/onehoon/VIIPER/blob/e3275bc156fc5dd1cf8c1f2798c5770f3257dd58/lib/viiper/server.go), maps the value to a Go time.Duration in milliseconds.
- [internal/server/usb/server.go](https://github.com/onehoon/VIIPER/blob/e3275bc156fc5dd1cf8c1f2798c5770f3257dd58/internal/server/usb/server.go), handleUrbStream / newBatchingWriter / flushLoop.

The server uses a batchingWriter **if and only if** WriteBatchFlushInterval > 0; the writer starts a goroutine with time.NewTicker(flushEvery), then calls Flush() on each tick. Flush acquires the batching writer mutex and calls bufio.Writer.Flush(). This can entail **a periodic wakeup even when the application has not delivered a new Xbox360 state**; an empty bufio buffer normally means no socket payload is flushed. Actual tick count is subject to timer coalescing/scheduling and must not be presumed exactly 1,000/s.

Separately, writeRet performs an **immediate bw.Flush() for async interrupt-IN replies**, so its per-response immediate flush can coexist with the 1-ms periodic flush. Therefore, the periodic timer may be redundant during steady idle, but **its CPU contribution is currently unmeasured**.

**Status:** Code-level candidate with a direct one-line Addon control point, **not** a demonstrated percentage of CPU4 ReadyThread. The 1-ms timer alone should not be claimed to account for ~4,092 ReadyThread events/s.

### 4.2 P2 — asynchronous USB/IP interrupt-IN request work and cached response

In pinned VIIPER internal/server/usb/server.go, handleUrbStream handles a non-control interrupt-IN USB/IP submission by registering a pending request and starting a goroutine. The goroutine calls processSubmit under a context timeout based on interruptINWaitDuration(dev, ep), then either returns fresh data, a per-endpoint cached prior response after timeout, or waits/cancels appropriately.

In pinned [device/xbox360/device.go](https://github.com/onehoon/VIIPER/blob/e3275bc156fc5dd1cf8c1f2798c5770f3257dd58/device/xbox360/device.go), main IN endpoint 0x81 has bInterval=4 ms. HandleTransfer waits on a single-slot inputCh or context cancellation. SetXbox360DeviceState replaces the slot via UpdateInputState. The Addon 250-ms unchanged-state refresh means this channel is replenished periodically even when no buttons move.

This transport path can continue servicing host USB/IP requests despite fewer C#-to-Go setter calls. It may involve repeated context timers, completion wakeups and retained cached report replies.

**Status:** Plausible second candidate. Its actual rate is **driven by host URBs, endpoint requests and response behavior**, not established by the 4-ms descriptor alone. Do not equate an endpoint bInterval with observed report count or CPU consumption; do not alter endpoint polling, timeout, cached replay or context cancellation without direct transport evidence and safety tests.

### 4.3 P3 — 10-ms pre-read select in USB/IP stream loop

Pinned internal/server/usb/server.go contains this sequence at the top of its URB processing loop:

~~~go
select {
case <-ctx.Done():
    // transport/device teardown
case <-time.After(10 * time.Millisecond):
}
if err := usbip.ReadExactly(conn, hdr[:]); err != nil {
    return fmt.Errorf("read URB header: %w", err)
}
~~~

This introduces **up to one 10-ms timed wait before each loop's blocking read**. The blocking ReadExactly itself is *not* a 10-ms polling loop. This is a legitimate candidate for protocol latency and timer overhead, but no observed request throughput or response latency has yet proved it harmful in actual Full1902 use. Review its purpose with cancellation and transport lifecycle before changing it; do not remove it as a speculative "obvious fix."

**Status:** Investigation only; **lower priority than P1/P2**.

## 5. Why a live DLL replacement is the wrong experiment

The Addon does **check** the exact VIIPER DLL hash at startup:

- [ViiperRuntimeInspector.cs](../src/SteamInputAddonforClaw/Prerequisites/ViiperRuntimeInspector.cs) compares SHA-256 against ExpectedPayloadSha256, pinned at **7CD41BCDBF331D440E20CDFFD188F13592496AD7491C2C368393BBA1D11A1915**; a mismatch returns Unusable / ViiperPayloadHashMismatch.
- [DisabledBootControllerAdmission.cs](../src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs) blocks new physical controller acquisition when prerequisites are not all Ready, so swapping an unpinned DLL can leave Center M Disabled with the Addon unable to present a controller.
- [ViiperNativeModuleCache.cs](../src/SteamInputAddonforClaw/VirtualOutput/Viiper/ViiperNativeModuleCache.cs) loads by absolute path **once per process** and never unloads the Go c-shared module during that process lifetime.
- The repository also pins the binary in Dependencies/Viiper/viiper.lock.json, PROVENANCE.md, tests and publish verification tooling. A proper future VIIPER update uses its verified artifact/adoption workflow and matching ABI review, not ad hoc overwriting.

**Do not** try to race the normal Addon restart and swap the DLL while running. Do not temporarily turn off or bypass the hash/prerequisite gate, and do not hand-modify the current live Velopack installation. Existing process handles, app update mechanics and fail-close make this both unreliable and unnecessarily risky.

## 6. Recommended next work — scoped experiment, then decision

### Phase A (FIRST): Addon-only temporary diagnostic A/B, same pinned DLL

**Aim:** Check whether VIIPER's current buffered 1-ms flush **configuration** materially contributes to the user's observed idle CPU oscillation.

Make an **isolated local diagnostic build** from current Addon code with only:

~~~diff
- WriteBatchFlushIntervalMs = 1
+ WriteBatchFlushIntervalMs = 0
~~~

Location: src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs.

At the pinned VIIPER version, **0 bypasses newBatchingWriter entirely** and uses the underlying net.Conn directly for the USB/IP response writer. Therefore it avoids the 1-ms flush goroutine **but also changes buffered versus direct socket-write behavior**. This is an **A/B of the whole write strategy**, not a controlled measurement of just the timer. It may change the number of network writes, packet grouping, completion latency and CPU use in either direction. It does **not** change the physical/PID1902 polling intervals, game-observer policy or virtual controller native ABI.

Implementation preparation by local Codex:

1. Confirm the exact current branch, pin, config, and minimal diff. Do not touch VIIPER Go source, its DLL/header, sha256 pins, startup admission, HidHide, USB/IP driver, attach/detach or session end.
2. Produce a **reversible, explicitly labeled diagnostic build** and separately retain the original known-good installer/build. Prefer a normal controlled installation/replace flow rather than editing a loaded DLL or executable in place.
3. Keep existing Release build and relevant .NET tests passing and state clearly what changed. Real-device verification is the **user's role after code review/merge** if the change proceeds through a PR; it is **not** a Codex implementation gate or merge blocker.
4. Do not create a persistent user setting, new state owner, diagnostic manager, synchronization layer or speculative race protection for this one-bit trial.

User-operated A/B comparison (not a PR blocker):

- Same Claw, power profile, Windows desktop and game/BPM state, Center M Disabled, healthy PID1902 + attached Xbox360 presentation, no new input during idle observation.
- Compare known-good **1 ms (buffered)** against temporary **0 ms (direct writes)**. The user's existing Task Manager observation of roughly 0.2% to 1.x% is the motivating baseline; the task is to look for a **repeatable difference**, not re-prove that an idle CPU issue exists.
- Watch for gross CPU level/spikes as well as controller availability, first input after idle, press/release neutrality, rumble and presentation reliability. Testing and reporting physical effects are owned by the user.
- Optional low-overhead process CPU deltas may help quantify an apparent win; **do not** use another heavy 1-ms-profile + ReadyThread/CSwitch ETW trace as the default A/B comparator.
- Any meaningful regression, lost input, stuck virtual report, abnormal transport state or uncertain teardown → **restore the known-good build**. Do not promote the experiment on CPU alone.

**Decision:** If CPU improves repeatably with no functional problems reported, P1 becomes more credible but the win could be due to timer removal, buffering behavior or their interaction; follow with targeted diagnosis before choosing production policy. If unchanged or worse, deprioritize P1 and investigate P2. In either case this A/B is not yet authorization to merge 0 ms into production.

### Phase B (ONLY IF NEEDED): narrowly targeted native telemetry

If Phase A is promising but causation remains ambiguous, or if native work remains a major candidate, add **temporary aggregate counters** to the pinned VIIPER fork or a clearly isolated diagnostic branch/build:

- 1-ms flush tick count, number of ticks with empty versus nonempty buffer, and per-response immediate-flush count;
- interrupt-IN submissions, context timeouts, cached-response completions, actual response writes, and total outstanding requests (only if necessary for P2);
- bounded fixed-window (for example 30-second) summaries, **never** per-tick, per-URB or per-report file logging.

Do **not** add a 30-second high-frequency timer just to log counters; emit summaries using existing activity/lifecycle opportunities or a lightweight bounded mechanism justified by the experiment. Use simple counters; no new transport authority, locking hierarchy, manager or global profiler.

For any actual DLL change: build from the exact source revision, verify the generated matching header/ABI, and **adopt the resulting DLL plus all cryptographic pins together** using the existing VIIPER workflow (see docs/VIIPER_INTEGRATION.md). Test with a controlled compatible Addon build. No hot-swap or bypassed hash check.

### Phase C (EVIDENCE-DEPENDENT): evaluate targeted implementation, not speculative tuning

- If P1 is supported, compare a *minimal* behavior-preserving buffered-writer improvement (e.g. avoiding empty periodic flush) against the 0-ms direct-write approach **after** confirming what is safe. Preserve error delivery and the existing immediate flush behavior where required.
- If P2 dominates, inspect actual host URB submission/timeout/cached-replay frequency and only then consider a change. Maintain correct endpoint reports, neutral/first-input behavior, callback and cancellation/unlink semantics, and full transport drain.
- Only consider P3's 10-ms pre-read wait separately if actual latency or avoidable timer events are evidenced. Preserve correct cancellation on device removal and real Windows detach/shutdown.
- Stop optimization if the attributable cost is already minor; **an ETW test can produce event counts even when eliminating them has no meaningful product benefit**.

## 7. Non-negotiable Full1902 safety and scope

Supported product model: **one Windows user, one interactive session**; standalone Full1902. **CTW is not a runtime integration/dependency.**

Do not weaken:

- PID1902 controller ownership; strict PID1901→PID1902 transition when required; Center M authority and stock restoration;
- HidHide persistent isolation, identity checks, and recovery admission;
- exactly one live virtual presentation, typed VIIPER attachment/ownership, neutral state before detach and publisher stop/join;
- USB/IP request completion, unlink/cancellation, drain and native teardown/fail-close handling;
- sleep/hibernate/resume, restart/crash/shutdown, PnP device loss and re-enumeration, and real operation failure behavior;
- active 250-Hz publishing and immediate changed-input delivery, idle 125-Hz physical/publisher timing, bounded 250-ms unchanged-state native refresh, failure propagation and rumble safety;
- the explicit product decision to **keep automatic A2VM boot rumble cycling disabled**.

Avoid defending unobserved theoretical scheduling races with more epochs, managers, wrappers, inspectors, retries or speculative locks. Code review should focus on realistically reachable behavior affecting the supported handheld lifecycle. User physical tests remain post-code user work, not a merge blocker.

## 8. Open facts and handoff checklist

**Confirmed:**

- Real user sees Runtime idle CPU fluctuate at ~0.2% to 1.x%.
- CPU4 physically owned PID1902 and had an Xbox360 presentation attached in the surrounding lifecycle logs.
- The **corrected** CPU4 CSwitch window shows **2.26% 8-LP CPU** and **~4,092 Runtime-target ReadyThread/s** under heavy ETW; these are different from no-ETW Task Manager measurements.
- VIIPER code genuinely contains a **1-ms unconditional flush ticker**, async interrupt-IN goroutine/timeout/cached replay, and a pre-URB 10-ms wait.
- Addon genuinely passes **WriteBatchFlushIntervalMs=1**, and VIIPER source selects **direct net.Conn writes** at **0**; the installed DLL is cryptographically pinned.

**Not confirmed:**

- What portion of ReadyThread or observed ordinary idle CPU is attributable to the 1-ms flush ticker versus USB/IP/IOCP, Go scheduler or physical/publisher timers.
- Exact count of empty flushes, per-endpoint URB completions or native state setter/skip calls in CPU4.
- Whether 0-ms direct writes are beneficial in real use, or whether they affect input/USB/IP reliability.
- Whether the 10-ms pre-read delay is removable without violating actual transport lifetime/cancellation semantics.
- Any causal CPU savings from PR #806 alone, from the differing CPU1/CPU4 traces.

**Next owner action:** Local Codex prepares the **single-line, isolated Addon-only 1→0 diagnostic build** (only when requested) and reports its build/test evidence and exactly how to revert. The **user** decides when to run it on the physical handheld and reports the results. Review the result **before** planning any VIIPER source modification or production PR.

**Do not open an automatic production optimization PR from the current ETW evidence alone.**
