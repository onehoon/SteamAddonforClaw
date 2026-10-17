# Work Order — Full1902 Xbox360 Windows Idle: Remove the 250-ms Unchanged-State Refresh

**Date:** 2026-10-17  
**Repository:** `onehoon/SteamAddonforClaw`  
**Source reviewed:** `main` @ `7861d1370b7f97a9ba18994871d1815974e3fd57`  
**Implementation owner:** Local Codex  
**Delivery:** One focused Addon code PR, no VIIPER fork PR in this task  
**Device validation:** User-owned **after merge**; never a Codex implementation requirement, CI gate, or PR blocker.

## 1. Goal and approved decision

**Make the Full1902 Xbox360 production publisher changed-state-only while the Runtime's existing game/observer decision is in Windows desktop idle (125 Hz).** Specifically remove its forced native setter call after 250 ms of identical mapped state.

- **Idle/125 Hz**: map the latest physical state on every existing 8-ms publisher tick; call `CanonicalViiperRuntime.SetXbox360State` **only for the first state after publisher Start or when the final mapped Xbox360 state differs from its previous successfully accepted native state**. An unchanged state may remain silent indefinitely.
- **Active/250 Hz**: preserve the existing `_requires250Hz()` decision and submit a state **every 4-ms production tick**, even if the value is unchanged. Recognized Xbox game, Steam/game observer situations, or untrustworthy observer conditions must retain their existing conservative active behavior. Do not alter the Runtime's game detection or presentation selector.
- **Manual/test `IInputReportTickSource` path**: preserve one actual setter invocation per supplied tick, including identical values.
- **No global idleness detector**: the word *idle* in this work order means the **already existing cached 125-Hz production mode decision**, not measured inactivity or a new Windows idle-signal check.

This is intentionally narrower than changing the physical read loop, Xbox360 output rate in games, USB/IP poll scheduling, Steam Deck / DualSense Edge publishers, or any owner/recovery mechanism.

### 1.1 Why the policy changes now

Addon PR #806 already implemented the exact post-mapping duplicate comparison but left a **250-ms refresh** in the idle path. The earlier work order (`docs/work-order/2026-10-16_FULL1902_WINDOWS_IDLE_XBOX360_DEDUPLICATED_NATIVE_SUBMISSION_WORK_ORDER.md`) required that refresh to avoid a potential indefinitely pending report when a fresh USB/IP connection had no `lastInResp` and an earlier stream had consumed the single-slot Xbox360 input channel.

**VIIPER PR #65 has now been squash-merged** in `onehoon/VIIPER` as `f1d2f8b82367c1c17bd20fa999de1c4444536912`. In the updated USB/IP `internal/server/usb/server.go`, the server-wide `lastInResp` replay cache is deleted. On timeout without a new report, the original interrupt-IN URB remains pending. Device-produced reports, including a new setter submission whose payload is byte-for-byte unchanged, continue to complete the pending URB. In Xbox360 `device/xbox360/device.go`, `UpdateInputState` enqueues/replaces input in the existing capacity-one `inputCh`; no new call means no new report.

Therefore the **consumer's 250-ms duplicate setter is now itself a source of unnecessary idle XInput packet completions and possible Windows `GetLastInputInfo` resets**. It defeats the aim of the server-side no-replay fix for the Addon.

CTW-dev independently uses changed-state-only submission keyed to its backend/session object (`XboxGamingBarHelper/Labs/ClawButtonMonitor.cs` `SubmitToBackends`). Its patched-VIIPER **hardware measurements** reported zero idle timer resets over 30 seconds and a stable XInput packet count after removing server replay; those findings are **not yet proof of the same results on Addon**. We copy only the changed-state submission principle, not CTW process/session code. CTW and Full1902 Addon are standalone, independent applications.

**This work order supersedes only the historical instruction to send a 250-ms identical-state refresh.** Retain PR #806's exact comparison, existing 125/250-Hz scheduling/fault policies, and other Full1902 authority/lifecycle requirements. Do not silently rewrite old investigation reports as if their original experiments never happened.

## 2. Mandatory authoritative sources (read latest before implementing)

### 2.1 Addon product/ownership documents

1. `docs/Full 1902 Implementation/README.md` (current document precedence; later active revisions supersede historical work orders).
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`, `REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`, and `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` for PID1902 physical ownership, presentation, fail-close, recovery, Sleep/Hibernate/Resume.
3. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md` for current Runtime authority (no privilege changes here).
4. `docs/VIIPER_INTEGRATION.md`, `docs/VIIPER_IMPLEMENTATION_RULES.md`, `docs/VIIPER_MIGRATION_TODO.md` and `onehoon/VIIPER/FORK_ARCHITECTURE.md`, `onehoon/VIIPER/docs/libviiper/fork-api.md`.
5. `docs/work-order/FULL1902_ADAPTIVE_IDLE_125HZ_CONTROLLER_CADENCE_WORK_ORDER_2026-10-10.md`; `docs/work-order/2026-10-16_FULL1902_WINDOWS_IDLE_XBOX360_DEDUPLICATED_NATIVE_SUBMISSION_WORK_ORDER.md` (PR #806 rationale and the now-superseded 250-ms clause).
6. `docs/FULL1902_WINDOWS_IDLE_CPU_AND_VIIPER_HANDOFF_2026-10-16.md` (historical CPU/ETW observations, not a demonstrated CPU percentage attribution). Where this historical handoff describes pre-PR65 cached-replay behavior, use the latest VIIPER source instead.

### 2.2 Actual implementation files and tests

- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs` — **only required production edit**.
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/Xbox360DeviceStateMapper.cs`; `Xbox360NativeTypes.cs` — preserve the exact final mapped report representation and M1/M2, Guide and suppression rules.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` — presentation/Publisher owner, initial neutral write, start/stop, suspend pause, Resume restart and attachment proof. **Reference only; do not edit it for this optimization.**
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs` — typed setter and attachment/neutral/detach boundaries. **Reference only.**
- `tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs` — **required test edits**; check related `MsiClawAddonPresentationTests.cs` and `CanonicalViiperRuntimeTests.cs` for regression coverage, without adding unrelated tests.
- VIIPER `main` at/after `f1d2f8b82367c1c17bd20fa999de1c4444536912`; specifically `device/xbox360/device.go`, `internal/server/usb/server.go`, and PR #65's wire behavior regression tests.
- CTW comparative reference: `onehoon/ClawTweaks-Dev` `release/v0.3.98.0`, `Doku/VIIPER_IdleInput_Repeat_Report_2026-10-15.md` and `SubmitToBackends`. **No CTW integration or modifications.**

**Document freshness note:** `docs/VIIPER_INTEGRATION.md` defines the currently implemented three-presentation selection (Xbox360 for regular Windows, Steam Deck / DualSense Edge for Steam/BPM). If older `VIIPER_IMPLEMENTATION_RULES.md` passages describe a Steam Deck-only historical target, treat those as superseded by the current implementation/selection contract; do not regress Edge or reintroduce a Game Bar foreground presentation switch.

## 3. Verified code paths and constraints

### 3.1 Existing publisher and exact duplicate comparison

In `CanonicalXbox360InputPublisher.cs`:

- `ActiveProductionPeriod` = 4 ms and `IdleProductionPeriod` = 8 ms. `WorkerLoop` uses `WindowsHighResolutionOneShotTimer` and `CanonicalPublisherDeadlineMath` with `_timestampProvider` to schedule/re-arm deadlines. **Keep `_timestampProvider`. It is still required by the worker even after the refresh timestamp is removed.**
- `PublishProductionStateOnce()` calls `_requires250Hz()` and delegates to `PublishCurrentStateOnce(allowIdleDuplicateSkip: !requires250Hz)`.
- The explicit `PublishAsync(IInputReportTickSource, ...)` path calls `PublishCurrentStateOnce(allowIdleDuplicateSkip: false)` and intentionally does **not** suppress equal manual ticks.
- `Xbox360DeviceStateMapper.Map(...)` runs on every production tick and uses current back-button mapping and per-rear-button suppression. No change to mapper or when these providers are consulted.
- The existing `StatesEqual` compares the final mapped `Buttons`, `LT`, `RT`, `LX`, `LY`, `RX`, `RY` and `Reserved0` through `Reserved5`; keep this exact, allocation-free, no-hash comparison.
- `_lastAcceptedState` and `_hasLastAcceptedState` cache **only successful setter submissions**; `Start()` resets both. Skips return `true` without increasing `PublishedStateCount` or updating the cache. A rejected setter calls the existing `ReportFault`, returns `false`, and terminates the worker.
- The **only remaining time-bounded duplicate decision** is `IdleDuplicateRefreshInterval = 250ms` + `_lastSuccessfulSubmissionTimestamp` + the `Stopwatch.GetElapsedTime` clause.

### 3.2 Lifecycle already in place

- `MsiClawAddonPresentation.AttachXbox360Async` proves the detached-ready handle, attaches the device, writes initial neutral directly via `runtime.SetXbox360State(default)`, then constructs/starts the Xbox360 publisher.
- On first production tick after `Start()`, `_hasLastAcceptedState == false` guarantees another native report even when neutral.
- Stop/join precedes neutral/detach; overlay/suspend pause writes neutral using the existing presentation owner, not the publisher. The relevant resume method proves the same tracked attachment is still `Attached` before `publisher.Start()`; Start invalidates the last accepted state so the next worker tick writes again.
- Structural reconcile / physical PnP-recovery and unsafe/unknown results stay with the existing owner. This PR must not reattach during an output tick, ignore a sink error, or bypass fail-close.

**Known bounded-coverage limitation to document, not to over-engineer away:** A USB/IP stream might reconnect or be reimported *while the same typed logical device and production publisher remain live*, without going through a normal presentation `StopAsync` / `Start`. VIIPER's capacity-one `inputCh` is not per stream, and `GetUSBDeviceAttachmentState` confirms tracked attachment ownership, not necessarily a new host first report. If no new input state is submitted after such a reimport, the new host may wait until a real input change. The former 250-ms refresh masked this case.

The user has explicitly chosen **changed-state-only while Windows idle** to protect automatic display-off/sleep. **Do not add native import hooks, per-stream state caches, new ownership authorities, timers, speculative polling or a duplicated recovery manager solely to guarantee a report after an otherwise unobserved reimport.** Preserve currently implemented lifecycle safety, ensure normal known Start/restart/repair paths still submit their first report, and clearly disclose the above limitation in the PR. If an actual supported normal-lifecycle path is *demonstrably* broken by this change, raise the concrete path rather than silently weakening recovery. Do not treat theoretical scheduling crossings as blockers.

### 3.3 Supported modes

| Mode | Existing publisher schedule | Native submission requirement after this PR |
| --- | --- | --- |
| Windows desktop, healthy idle 125 Hz, mapped state stable | 8 ms source mapping/timer still run | One at Start and only on actual mapped changes; **no 250-ms refresh** |
| Windows desktop idle, press/release, axis, trigger, mapped M1/M2/Guide change | 8 ms | Publish next existing tick; do not delay or alter input values |
| Recognized Xbox game or any current `_requires250Hz == true` condition | 4 ms | **One native submission every tick even if unchanged** |
| Fast-to-idle transition | Schedule changes 4 ms → 8 ms | Preserve last successfully submitted state and skip further equal idle values; changed values still publish |
| Idle-to-fast transition | Schedule changes 8 ms → 4 ms | Equal values are again submitted every active tick |
| Steam/BPM presentation | Existing Steam Deck / Edge publisher | Not affected; no Xbox360 publisher work in this path |
| Stop/overlay pause/suspend, then valid restart | Controlled by existing presentation owner | Publisher's first actual tick submits once even for an unchanged neutral state |
| Manual explicit tick-source tests | Existing test tick source | Still **one setter call per supplied tick**, equal or different |

**Important:** Game presence by itself is not a new VIIPER input policy. The current `_requires250Hz` Runtime decision owns fast/idle eligibility. Do not introduce a new foreground/game lookup or replace this decision with Windows last-input time.

## 4. Minimal production change (one file)

File: `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalXbox360InputPublisher.cs`.

1. Delete the no-longer-required constant:
   ```csharp
   private static readonly TimeSpan IdleDuplicateRefreshInterval = TimeSpan.FromMilliseconds(250);
   ```
2. Delete the unused per-submission timestamp field and only its associated reset/write:
   ```csharp
   private long _lastSuccessfulSubmissionTimestamp;
   // Remove _lastSuccessfulSubmissionTimestamp = 0; from Start().
   // Remove _lastSuccessfulSubmissionTimestamp = _timestampProvider(); after a successful setter call.
   ```
3. Replace the existing bounded duplicate predicate with the existing exact comparison **without the time clause**:
   ```csharp
   if (allowIdleDuplicateSkip &&
       _hasLastAcceptedState &&
       StatesEqual(mapped, _lastAcceptedState))
   {
       return true;
   }

   if (!_setState(mapped))
   {
       ReportFault(new InvalidOperationException(
           "Canonical VIIPER rejected a typed Xbox360 state."));
       return false;
   }

   _lastAcceptedState = mapped;
   _hasLastAcceptedState = true;
   _publishedStateCount++;
   return true;
   ```
4. Retain `Start()` cache invalidation, the injected `_timestampProvider`, all `Stopwatch` and `WindowsHighResolutionOneShotTimer` scheduling, both 4/8-ms periods, `_requires250Hz`, `StatesEqual`, native error/fault semantics, and the explicit test-tick path.
5. Update stale **local XML comments** that still describe the 250-ms refresh. They should accurately say that the idle production worker submits only changed mapped states while the manual tick and fast production paths keep one native write per tick.
6. Keep existing bounded cadence diagnostic semantics (`IdleReportDedup` On/Off), without per-tick logging or a new health counter. That event denotes suppression **eligibility**, not number of successful native reports.

**No other production file is necessary.** Do not mutate VIIPER Go code, update its DLL/headers/hashes or dependency lock, change HID/PID1902 logic, or silently upgrade USB/IP 0.9.8.1.

## 5. Focused automated regression tests

File: `tests/SteamInputAddonforClaw.Tests/CanonicalXbox360InputPublisherTests.cs`.

Reuse existing deterministic `PublishProductionStateOnce()`, `Snapshot`, `FakeSink`, `_requires250Hz`/`timestampProvider` seams and the existing Start/Stop seams. Do not introduce a new abstraction or thread/timer solely to test identical-state skipping.

### 5.1 Replace the now-obsolete tests

- Replace `Production_idle_state_refreshes_after_250ms_with_controlled_monotonic_time` with `Production_idle_equal_state_is_never_refreshed_by_elapsed_time` (or similar):
  - first production call sends neutral;
  - repeated equal calls do not invoke the sink even when fake monotonic time advances from just below 250ms to 250ms, 1s, and a long idle interval;
  - `PublishedStateCount` remains 1 throughout, and `PublishProductionStateOnce()` returns success for skips.
  - Keep the injected monotonic time seam where helpful, but do not reintroduce an idle refresh deadline.
- Replace `Production_rejected_idle_refresh_does_not_advance_success_timestamp` with a relevant **rejected changed-state does not replace accepted-state cache** test, or delete it if `Production_rejected_changed_state_is_not_counted_as_accepted` already covers the same invariant fully. Avoid redundant tests that obscure the narrowly changed contract.
- Remove or update comments/assertions referencing the now-nonexistent `IdleDuplicateRefreshInterval`/`_lastSuccessfulSubmissionTimestamp`; do not weaken useful mapping/fault coverage just to get green tests.

### 5.2 Preserve/add coverage of actual product behavior

1. **First state**: an idle publisher production tick with default/neutral input must produce one successful setter call; no stale cache inherited from a previous run.
2. **Unchanged state indefinitely**: controlled time advancing past the old 250-ms threshold must not cause any more successful native calls or alter `PublishedStateCount`.
3. **All meaningful changed-state cases**: button down/up, both trigger values, all four stick axes, M1/M2 target remapping, rear suppression, Guide and neutral restoration must continue being compared *after* mapping and sent on the next existing tick. Reuse current strong tests rather than building an exhaustive product suite.
4. **Fast mode and transition**: with `_requires250Hz` true, three identical production ticks must still generate three sink calls; idle→fast→idle transitions preserve existing correct cache behavior and 4/8-ms timer selection.
5. **Manual tick semantics**: `One_write_per_tick` remains unchanged, even for three identical states. Do not accidentally enable dedup for test-supplied tick sources.
6. **Stop/Start / overlay release / resume contract**: successful `StopAsync()` then `Start()` of the same publisher instance must force its first production write even when the mapped neutral state equals the pre-stop state. Exercise via the smallest existing seam, and retain existing presentation-owner attachment, neutral, resume and fail-close tests.
7. **Sink failure path**: a changed-state setter rejection/exception reports fault exactly once, never counts a success or overwrites the last accepted state, and the worker obeys its current stop/join cleanup. Do not turn failure into a successful skip.
8. **No reporting/ABI changes**: existing `CanonicalViiperRuntimeTests`, `MsiClawAddonPresentationTests` and native ABI/provenance checks remain valid. No changes to Xbox360 rumble OUT callback, Steam Deck/Edge report flow or live game mapping.

Do not assert an exact Windows `GetLastInputInfo` outcome or XInput packet number in .NET unit tests; those depend on the actual Windows USB/IP/XInput stack. Post-merge user hardware observations own those outcomes.

## 6. Verification by Codex / CI

Perform source review, formatting/checks and the repository's normal .NET build/test workflow for the affected project. Suggested starting point (adapt to the repo's actual project/CI commands):

```powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --configuration Release
dotnet build --configuration Release
git diff --check
```

If the workspace cannot build a Windows workload or the full solution in the local environment, run all applicable supported automated checks and accurately report limitations; do not fabricate a PASS. No physical Claw, manual OS sleep/resume, external controller, usbip-win2 device or CPU profiling is required of local Codex for PR readiness.

**PR review/merge policy:** A demonstrable code defect, broken test/CI, materially harmful normal-lifecycle regression or violation of current product contracts can block. Real-device testing belongs to the **user after merge**; do not gate code merge on absent hardware evidence. Do not add speculative races/locks/epochs/states for pathological interleavings or unsupported multiple Windows sessions.

## 7. Explicit exclusions

- Do **not** change the 125-Hz physical input reader or Xbox360 idle worker's existing 8-ms timer. **Suppress only native duplicate setter calls**, not controller sampling/scheduling.
- Do **not** change active 250-Hz publisher/submission cadence, fast/idle eligibility, Xbox game observer logic, Steam/BPM selection, Steam Deck/Edge publishers, game profiles or button mapping semantics.
- Do **not** change `VIIPER` PR #65 no-replay semantics, native ABI, DLL dependency pin, generated headers, usbip-win2 import/detach or transport batching.
- Do **not** modify CTW or implement CTW integration. It is solely external behavior evidence.
- Do **not** add producer-side deadzones, sensor filtering, axis jitter thresholds, debounce, idle detection, synthetic periodic keepalives, second timer, mandatory first-report polling, per-stream map, retry machinery, new owner or new UI settings.
- Do **not** alter WING suppression, Center M, HidHide, PnP recovery, physical device ownership, PID1901↔PID1902 restoration, suspend barriers, neutral/rumble STOP, callback drain or fail-close policy.
- Do **not** create a new response-generation path for identical input or suppress an actual **changed** value because of a time window.
- Do **not** add high-rate DEBUG logs for skipped frames or claim measured percentage CPU savings. After this change the 125-Hz timer and VIIPER's timeout checks still exist.

## 8. Acceptance criteria

- [ ] In production idle 125-Hz mode, repeated identical final mapped Xbox360 states cause **no further native setter calls** for any elapsed time after the first successful submission.
- [ ] Actual mapped input changes still send once at the next existing tick, including press/release, sticks/triggers, M1/M2 remap/suppression and Guide.
- [ ] In production 250-Hz active mode, equal states still send on **every tick**; the same is true for the explicit manual/test tick source.
- [ ] First production tick after each new publisher `Start()` sends the state, including an unchanged neutral value; Start/Stop fail-close remains intact.
- [ ] Removed: `IdleDuplicateRefreshInterval`, `_lastSuccessfulSubmissionTimestamp`, their writes/resets and stale comments; retained: `_timestampProvider` and high-resolution deadline scheduler.
- [ ] `PublishedStateCount` reflects actual successfully accepted native sends only; skipped duplicates do not mask a changed-state sink rejection.
- [ ] Normal Full1902 startup, presentation transitions, Overlay pause/release, Suspend/Hibernate/Resume, physical loss/recovery and safe teardown remain owned by their existing code; no new authority/ABI/pin introduced.
- [ ] All relevant .NET automated tests, Release build and CI pass; real-device idle-timer/XInput/CPU validation is **after-merge user work**, never a code-review blocker.

## 9. Post-merge user observations (non-blocking)

Once an Addon build actually includes **both** VIIPER PR #65 (`f1d2f8b`) and this publisher change, a user may compare on hardware:

- Idle Windows with virtual Xbox360 attached: controller untouched; Windows screen-off/sleep idle timer proceeds normally and XInput packet count no longer rises from native **duplicate** submissions.
- A/B press, release, both sticks, triggers and mapped rear buttons after a long idle; neutral release must arrive immediately on the next publishing tick.
- Recognized Xbox game: existing 250-Hz reports and button/rumble responsiveness.
- Suspend/Hibernate/Resume, a normal tracked reconnect/reconcile, and presentation change: first real input and neutral state are correct.

These are observations to be performed **by the user**, not test deliverables for Codex. A stable source state is necessary for an exact zero-idle-submissions expectation; actual analog jitter or mapping changes still produce real mapped changes. Likewise other OS input sources can affect `GetLastInputInfo` even when the Xbox360 publisher is silent. Do not promise a particular Windows idle reset count or CPU percent before this hardware comparison.
