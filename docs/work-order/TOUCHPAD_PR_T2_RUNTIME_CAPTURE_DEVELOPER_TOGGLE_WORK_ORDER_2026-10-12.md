# Work Order — Touchpad PR-T2: Runtime-Owned Right-Pad Capture + Developer-Only Enable Toggle

**Date:** 2026-10-12  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation:** local Codex  
**Source baseline reviewed:** current `main` after **PR #771** squash merge (`c6dcad84292ee85f1b7a3515cb5655750d87c5eb`); recheck the latest `main` before editing.  
**Hardware verification owner:** user after merge. No mandatory MSI Claw, touchscreen, Steam Input, game, Independent Flip, or physical VRR test for Codex/CI/PR review.  
**PR objective:** finish the first **production-owned but experimental-gated** end-to-end path: bottom-right Windows touch -> Steam Deck **right pad** -> the existing VIIPER Publisher. The only experimental aspect is the Developer Menu enable policy and fixed test layout.

## 0. Decision and precise scope

The user changed the rollout order after Phase 0:

1. Implement the real touch HWND, contact handoff, and native report publication in the **production Full1902 Runtime**, not in a separate tool or child process.
2. Add exactly **one persisted Developer Menu switch**, labeled **Virtual Touchpad (Experimental)**, initially **OFF** on fresh and upgraded installations.
3. When the switch is ON, arm the touch region **only during an actually running Steam game in gameplay**, with the current attached/live virtual presentation **Steam Deck** and the existing controller/input/Overlay/power gates satisfied. **Steam BPM browsing/home/library alone must NEVER arm the window, even if Steam Deck is attached.** Require the existing raw Steam `RunningAppId != 0` as a necessary (not by itself sufficient for foreground) game-running fact, but do NOT require an enabled Game Profile or its master bit at this stage. **If BPM/home/desktop is foreground while a game remains running in the background, disarm; this is in-game-only, not merely game-process-running.**
4. Xbox360 and DualSense Edge always remain touchpad-inactive in this test phase.
5. Reuse the proven **single bottom-right quarter of the primary display**, mapping to **only RPadTouch/RPadX/RPadY**. No left zone, editor, layout file, click, pressure, haptics, gesture synthesizer, Edge mapping or virtual mouse.
6. **Production quality** ownership/failure/cleanup code from day one. Later, replace the *enablement source* with per-game profile policy; do not move/rewrite the capture engine. The temporary developer flag must not become a competing second authority.

**PR-T1 is complete and merged.** Reuse its `SteamDeckRightPadContact.FromNormalizedPosition`, the append-only `SteamDeckDeviceStateMapper.Map(... rightPad)` argument, and the existing `CanonicalSteamDeckInputPublisher(... rightPadProvider: ...)` optional seam. Do not duplicate conversion or add another native writer.

**One cohesive T2 PR is acceptable** even if Windows HWND + typed frontend transport + focused tests exceed T1's preferred ~500 changed LOC. Keep actual scope tight; do not invent a framework to hit a numeric LOC target. If a split becomes necessary, ensure the first part remains default-OFF and cannot create a live touch-blocking window unintentionally.

## 1. Required reading and precedence

- [Full1902 authority order](../Full%201902%20Implementation/README.md), [controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md), [HidHide/startup authority](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md), [reboot-bound ownership](../Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md), [elevated Runtime](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md).
- [Touchscreen architecture and Phase 0 evidence](../STEAM_TOUCHSCREEN_VIRTUAL_TRACKPAD_ARCHITECTURE_2026-10-12.md), especially §§4, 5.1.1, 6.1, 9–10. The **current developer-gated experiment supersedes that document's old “game-profile first” sequencing**, but **retains its in-game-only requirement** (BPM without a foreground game is OFF); only the per-game setting/master enablement is deferred.
- [T1 work order](TOUCHPAD_PR_T1_PRODUCTION_STEAMDECK_NATIVE_MAPPING_WORK_ORDER_2026-10-12.md), [merged PR #771](https://github.com/onehoon/SteamAddonforClaw/pull/771), and actual current code/tests.
- [Phase 0 tool](../../tools/TouchpadPoc/) and [Phase 0 work order](TOUCHPAD_POC_PHASE0_BOTTOM_RIGHT_QUARTER_WORK_ORDER_2026-10-12.md); reuse **working Win32 input and no-activation mechanics**, not console timers/JSONL/per-event diagnostic output.
- [Current canonical VIIPER adapter](../../src/SteamInputAddonforClaw/VirtualOutput/Viiper/) and [presentation owner](../../src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs). VIIPER/HHC are reference implementations only; **no CTW coupling**.
- Current Main App `DeveloperPage`, `AppSettings`, `SettingsStore`, `StartupSettingsCoordinator`, `FrontendSettingsSnapshot`, `IAddonFrontendControl`, `InProcessAddonFrontendControl`, `FrontendWire`, `NamedPipeAddonFrontendClient/Server`, and `AddonProcessHost`. Their existing contracts win over outdated sketches.

The shipped elevated Runtime is already the platform/controller owner; Main UI/Overlay are frontends that inherit elevation. No separate helper, duplicate controller owner, privileged-input broker, or RDP/multi-user machinery.

## 2. Concrete baseline integration map (verified on main)

| Actual code | Current behavior | T2 insertion |
| --- | --- | --- |
| `tools/TouchpadPoc/TouchCaptureWindow.cs`, `NativeMethods.cs`, `Diagnostics.cs` | Standalone 90s console test creates a topmost no-redirection/no-activation Win32 HWND with a bottom-right-quarter physical rect, handles `WM_POINTER*`, tracks IDs and preserves outside-region drag | Adapt the **small necessary native mechanics** to one Runtime-owned component; no console/timer/JSONL startup path |
| `SteamDeckRightPadContact.cs` | `Neutral`, `FromNormalizedPosition(bool,double,double)`, signed ±32767 conversion, initial top-positive Y | Use unchanged; local fraction is computed from the physical **capture rectangle**, not full-screen coordinates |
| `CanonicalSteamDeckInputPublisher.cs` | One 4ms/~250Hz logical publication; calls optional `rightPadProvider` once per tick, then maps and invokes one native `SetState` | Plug in a host-owned, lightweight provider; **keep native timing and fault contract unchanged** |
| `MsiClawAddonPresentation.cs` | Default Deck factory at approximately lines 410–418 passes motion/rear suppression to the canonical publisher; optional factories injected by tests | Append optional provider delegate to constructor and capture it in the **existing** Deck publisher factory, like the already-established `motionSnapshotProvider`. Keep factory delegate signature/test fakes unchanged |
| `AddonProcessHost.cs` | Constructs the presentation owner during Disabled-mode startup; handles first attach, game/BPM reconcile, Overlay visible-surface transitions, suspend, physical recovery, and process teardown | Own exactly one capture component/handoff; admit/reconcile against **actual live Deck + actual foreground gameplay**, not BPM browsing, at the existing lifecycle entry/exit points |
| `AppSettings` / `SettingsStore` / `StartupSettingsCoordinator` | One persisted `settings.json`, explicit load and anonymous-object save; existing save-before-publish preference methods | One optional `ExperimentalVirtualTouchpadEnabled` bool; missing/malformed -> **false only for this field**; persistence before activation |
| `FrontendSettingsSnapshot` / `IAddonFrontendControl` / in-process control | Typed settings projection and setters | Add one typed bool + `SetExperimentalVirtualTouchpadEnabledAsync(bool)`; no profile mutation |
| `FrontendWire.cs`, `NamedPipeAddonFrontendClient/Server.cs` | Protocol **76**, exact typed RPC handshake, `SetSteamVirtualController` preference pattern | Add one request/method/typed response and bump to **77**; do not reuse the virtual-device selection RPC or its Steam/BPM mutation lock |
| `DeveloperPage.xaml(.cs)` | Already initialized with `FrontendBootstrapSnapshot`; has toolkit `SettingsCard` diagnostics, no touchpad toggle | One `SettingsCard`/`ToggleSwitch`, initialize from persisted snapshot, save through frontend, revert to returned/last-confirmed preference on failure |
| `tests/SteamInputAddonforClaw.Tests` / `tools/TouchpadPoc.Tests` | Existing deterministic publisher/mapper, settings/RPC/lifecycle tests and separate PoC input tests | Extend focused tests for admission, HWND lifecycle, pointer handoff and transport; do not transplant the whole PoC test harness |

## 3. Runtime-native touch target

### 3.1 Host one narrow HWND and message pump

Create a small Runtime-local Win32 component, e.g. `TouchpadRightCaptureWindow`, with exactly **one owner** in `AddonProcessHost`.

- Reuse the proven HWND flags: `WS_POPUP` plus `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST`, no rendered surface, no opacity texture, no WinUI/WPF window.
- `WM_NCHITTEST -> HTCLIENT`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`, `WM_POINTERACTIVATE -> PA_NOACTIVATE`, `WM_ERASEBKGND -> handled`. Preserve actual touch interception/suppression from PoC; `WS_EX_TRANSPARENT` is **not** appropriate.
- Accept **touch** (`PT_TOUCH`) only as mapped input. Other pointer types do not become Steam Deck pad activity.
- Prefer a small dedicated window thread with a normal `GetMessage/DispatchMessage` pump if the existing Runtime message loop cannot straightforwardly own/create/close the HWND. **This is a Win32 message-pump necessity, not a second publisher or process.** Commands to create/show/hide/close execute on that window's owning thread; use bounded startup/shutdown acknowledgement. Do not call `DestroyWindow` from a foreign thread.
- The PoC calls `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` because it is a standalone EXE. **Do not call that globally from an already-running elevated Runtime.** Inspect its existing process DPI manifest/context; if needed, use a **narrow thread-window DPI context** before HWND creation and restore it correctly. Use verified pixel-coherent `GetMonitorInfo` physical bounds, `GetWindowRect`, and `POINTER_INFO.ptPixelLocationRaw`; do not mix DIPs/virtualized coordinates with physical pixels.
- Fixed physical rectangle: primary monitor bottom-right quarter. For the Phase 0 sample at 1920×1200, `X=960,Y=600,W=960,H=600`. Respect the actual monitor origin and odd dimensions; no hardcoded 1920×1200. If geometry becomes invalid during actual DPI/display/rotation change, disarm/neutralize and rebuild or remain disabled pending the next existing reconcile.
- Do not make a full-screen invisible input blanket. Keep the one right-hand zone visually **entirely absent**.
- A normal OFF/disarm **must stop intercepting touch**. Hiding/destroying the window on its owning thread is acceptable; a passive HWND left on top and consuming input is not. On failure, force the pad sample neutral, log the failure, and report feature unavailable rather than pretending deactivation succeeded.

### 3.2 Single contact and raw physical coordinates

- Only a `WM_POINTERDOWN` **beginning inside** the currently armed region can establish the owned contact. Remember its pointer ID. If another finger arrives inside the region, do not silently switch owners or manufacture a second Deck contact.
- `WM_POINTERUPDATE` for the tracked ID updates the current local point (including when the finger moves outside the region); clamp position to region. `WM_POINTERUP`, `POINTER_FLAG_CANCELED`, `WM_POINTERCAPTURECHANGED`, window hide/destroy, and other real loss paths terminate/clear it.
- A finger beginning outside the region must not become owned merely by crossing inward.
- Normalize raw physical point from the verified capture rectangle and use **only** PR-T1's `SteamDeckRightPadContact.FromNormalizedPosition`:
  
~~~csharp
// Example only: region uses the same raw physical pixel coordinate system as pointer data.
double nx = (pointerPixelX - region.X) / (double)region.Width;
double ny = (pointerPixelY - region.Y) / (double)region.Height;
var nativeContact = SteamDeckRightPadContact.FromNormalizedPosition(true, nx, ny);
~~~

- Do not invent Press/Force on touch. Native mapping stays `RPadPress=0`, `RPadForce=0`, all LPad fields neutral.
- Reuse only the simple PoC pointer tracking/geometry semantics needed here. No JSONL stream, per-Move logging, per-user window registry, telemetry database, generic multi-pad window manager, or driver interception.

### 3.3 Correct handoff to the existing publisher, including rapid taps

The HWND WndProc and the Deck publisher run on different threads; a **minimal bounded handoff** is needed. Ordinary Down/Move/Hold/Up map to one contact snapshot. However, a Down+Up faster than an existing ~4ms publisher tick can be entirely missed if only a latest-state variable is sampled. T1 explicitly deferred fixing that realistic input-loss path to T2.

Implement one small handoff scoped to **this single right pad**, maintaining the minimum state required to:

1. Preserve **at least one `RPadTouch=1` native publication and then one `RPadTouch=0`** when a valid quick Down+Up occurs between ordinary publisher ticks.
2. Coalesce high-rate MOVE to the latest position while touched; avoid unbounded per-Move queues.
3. Keep continuous hold touched until real Up, cancellation, loss or disarm.
4. Drop all queued/pending contacts and return **Neutral** immediately on OFF/disarm/Overlay/Suspend/presentation retirement, so an old Down cannot replay after rearm.
5. Never produce native `SetState` from the WndProc; only the one canonical publisher tick performs publication.

**Illustrative two-state transition rule**, not a requirement for another manager: `WM_POINTERDOWN` marks a not-yet-published contact; if Up happens before the next tick, hold a pending touched sample for one tick and return neutral on the following tick. For a contact already published, Up makes the next read neutral. Define the deterministic, bounded behavior for quickly repeated real taps without manufacturing unrelated presses. An ordinary private lock around this one contact handoff (or equivalently simple safe single-producer/single-consumer synchronization) is appropriate. **Do not** build generalized event brokers, epochs, barriers or arbitrary queues for impossible instruction-level races.

The `Func<SteamDeckRightPadContact>` passed to the existing publisher should read this handoff **only when touch admission is active**. When the publisher is missing/stopped/paused/retired, it is never a reason to start an additional publisher.

## 4. Developer Menu preference and transport (only feature enable authority for now)

### 4.1 Persistence

Add to `AppSettings`:

~~~csharp
public bool ExperimentalVirtualTouchpadEnabled { get; init; } // default false
~~~

Read **only literal JSON true** as enabled; absent, null, wrong type -> false. Write alongside existing `SettingsStore.Save` payload. Don't reset unrelated settings for an invalid value. Preserve the current explicit serialization schema, startup defaults, normal save-then-publish pattern and original `DeveloperMenuEnabled` behavior.

Add `StartupSettingsCoordinator.ExperimentalVirtualTouchpadEnabled` read and a narrow `ChangeExperimentalVirtualTouchpadEnabled(bool)` save-before-publish mutation. Do not make merely hiding Developer Menu clear the saved experimental bit; this preference remains active during ordinary UI close/relaunch and Runtime restart if the user deliberately set it.

### 4.2 Typed frontend and DeveloperPage

- Add `FrontendSettingsSnapshot.ExperimentalVirtualTouchpadEnabled` as an additive bool; update `MapSettings()`, bootstrap and existing read projections.
- Add `IAddonFrontendControl.SetExperimentalVirtualTouchpadEnabledAsync(bool,...)` and in-process implementation. After persisted change, call a **small Runtime-owned reconcile callback for touch capture**; do **not** request a Steam Deck/Edge native presentation switch just to change this boolean.
- Add `FrontendRpcMethod.SetExperimentalVirtualTouchpadEnabled`, `SetExperimentalVirtualTouchpadEnabledRequest(bool Enabled)`, matching server/client methods, protocol version **76 -> 77** and compatibility tests. Preserve existing old-method enum IDs/serialization assumptions.
- In `DeveloperPage.xaml`, add one `SettingsCard` with a WinUI `ToggleSwitch` labeled `Virtual Touchpad (Experimental)` and a short description: `Right quarter of the primary touchscreen. Active only during a foreground Steam game with a live Steam Deck virtual controller; not in BPM browsing, Xbox360, or DualSense Edge.`
- In page Initialize, set the switch from `bootstrap.Settings.ExperimentalVirtualTouchpadEnabled` without emitting an outbound mutation. On explicit user toggle, call the typed RPC, honor the **returned persisted value**, restore the previously confirmed value on any failed/throwing RPC, and avoid recursive `Toggled` events. Avoid fake success/active status if the window could not arm; saved ON is only a **preference**, not proof of current physical capture. A small read-only message may explain `Waiting for active Steam Deck`; don't add a dedicated status service merely for cosmetic presentation.
- No global toggle on normal Settings, Steam Profile, Overlay, Game Profile, device profile, or Quick Settings. No Steam/BPM controller-preference edit lock on this touch setting: it **must** work when Steam Deck is already live.

## 5. Runtime owner admission and lifecycle — required real-world safety

Define one derived decision, using current facts rather than a second persisted or authoritative state:

~~~csharp
// Product decision sketch; adapt to the existing actual owner semantics.
bool shouldArm =
    startupSettings.ExperimentalVirtualTouchpadEnabled
    && _centerMStartupControl?.Capture().State == FrontendCenterMStartupState.Disabled
    && _physicalOwnership?.LiveInputSource is { IsRunning: true }
    && _runtimeHost?.ActualRunningAppId > 0  // Steam game is actually running
    && IsCurrentForegroundSteamGameplay()     // NOT BPM UI/desktop; see note below
    && _presentationOwnership?.ActivePresentation == AddonPresentationKind.SteamDeck
    && _presentationOwnership.IsActivePresentationLive
    && !_presentationOwnership.IsOverlayPaused
    && !_presentationOwnership.IsSuspendPaused
    && !_overlayCaptureActive
    && !_overlayController.IsVisible
    && Volatile.Read(ref _processShutdownStarted) == 0;
~~~

Apply this predicate (plus valid window/monitor geometry) at the **existing safe lifecycle boundaries**; don't poll it every few milliseconds or install a second game detector. The actual kind must be **SteamDeck**, not merely the saved Steam-facing preference or `WantsSteamDeck` (which can be true for BPM alone and can select Edge).

**Important distinction verified in current code:** `SteamPresentationSnapshot.WantsSteamDeck` is `RunningAppId != 0 || BigPictureActive`; `SteamSessionRuntime.ActualRunningAppId` comes from the raw Steam `RunningAppID` registry observation and is **zero for BPM-only navigation**. Therefore require **nonzero actual game AppID** as the mandatory first game gate, separately from virtual-controller type. But nonzero AppID means *a game is running*, **not necessarily that it is foreground**: a running background game while BPM UI, desktop, or another app has focus must not leave a topmost touch blocker active. Resolve `IsCurrentForegroundSteamGameplay()` with the **smallest reliable current Windows foreground-window/game association** available in this codebase; inspect `GameDetection/Windows/WindowsGameWindowEventSource.cs` for existing `GetForegroundProcessId` semantics and `SteamBigPictureWindowProbe` for BPM window identity, but **do not copy a general-purpose game detector or add new win-event manager solely for touch**. If there is no trustworthy evidence that foreground is the active Steam game, fail closed (window hidden, touch Neutral) rather than guessing that BPM browsing is gameplay. Re-evaluate on the existing actual-AppID/presentation events and practical foreground activation changes; ensure loss of gameplay foreground immediately hides/neutralizes the window before it can steal UI touch. Do not require `!BigPictureActive`: BPM may remain *running behind a launched game*.

Examples: (a) BPM library, RunningAppID=0, Steam Deck live -> OFF; (b) actual game foreground, RunningAppID>0, Steam Deck live -> ON if other gates pass; (c) game RunningAppID>0 but BPM/desktop foreground -> OFF; (d) game ends and BPM stays -> OFF. No game-profile enable bit in T2.

### Required integration points from reviewed `AddonProcessHost`

| Existing boundary | Touch target behavior |
| --- | --- |
| Settings changed through new developer RPC | Persist first; re-evaluate arming immediately. OFF neutralizes/disarms before reporting success. ON may remain saved but inactive if no live Deck |
| First `AttachInitialAsync` during `TryCompleteInitialControllerPresentationAsync` (~line 1119) | Reconcile after real attach and active publisher **only if an actual Steam game is foreground**; BPM-only attach must not arm |
| `RequestControllerPresentationReconcile` / `ReconcileControllerPresentationAsync` (~lines 3827–3940) | Disarm when an actual output transition/loss is being attempted, then reconcile after the existing owner completes. Re-arm only if the resulting output is live Deck **and the game is actually foreground**. A normal no-change event should not destroy/recreate a healthy HWND unnecessarily |
| **Overlay Show** (~lines 2455–2505) | **Crucial ordering:** current code calls `_overlayController.ShowAsync` **before** `PauseForOverlayAsync`. Therefore disarm/hide touch HWND **before calling `ShowAsync`**; otherwise the always-topmost invisible region can steal touch from the Addon Overlay. On show failure or pause rollback, re-evaluate after rollback; never rearm while the Overlay is still visible or capture remains held |
| Overlay Hide / `RetireOverlayCaptureUnderTransitionAsync` (~lines 2670–2730) | Keep disarmed until surface is really hidden, existing release gate completed, the actual Deck publisher resumed **and the game is foreground**. No old touch carryover; require new Down |
| Suspend quiesce `QuiesceFull1902PresentationForSuspendAsync` (~lines 3950+) | Immediately neutral/disarm before/with existing pause and **before Windows sleeps**; leave no topmost touch target active during suspend |
| Resume (normal/suspend deferral) | Do not restore pre-suspend contact. Re-arm only after fresh owned-input/readiness/actual Deck publisher; no automatic source or VIIPER reconnection from touch code |
| Actual Steam game starts/ends, BPM home/library displayed, or gameplay loses/recovers foreground | **Foreground gameplay only:** first game admission can arm if all gates pass; game exit to BPM, game backgrounding, or BPM/desktop foreground disarms immediately. A subsequent verified game foreground can re-arm neutral and accept only a fresh Down |
| Physical DirectInput loss, PnP re-enumeration, publisher/native `SetState` fault | Drop contact, disarm capture as soon as existing host detects the loss. Continue using the existing physical/VIIPER fail-closed recovery; touch never retries controller attach |
| Xbox360↔Deck↔Edge selection, including Developer Rumble Restore paths | Disarm across retirement/switch; no touch on Xbox360/Edge; after Deck recovery/reattach, start neutral and admit fresh Down |
| `TryBeginProcessShutdownCore` / `DisposeAsync` (~lines 3386+) | Close touch admission at shutdown start, clear pending contacts, tear down HWND on its owner thread and bounded-join before/with native presentation disposal; no interaction with permanent HidHide/PID1902 authority |
| Display bounds/DPI/rotation change | Treat invalid/outdated geometry as a feature-local disarm + neutral, recompute/validate and rearm when safe; no global window hooks/framework for unsupported multi-monitor topologies |

Use the existing `AddonProcessHost`/presentation owner as the *sole authority*. If an event is already represented there, call the narrow reconcile at that boundary; don't create a new lifecycle coordinator. Ordinary creation/show failures must **not** fail the Xbox360 or Steam Deck attachment, tear down the VIIPER bus, or accidentally trigger physical controller recovery. If creation fails, preference may remain ON, but the capture feature is unavailable and pad reports stay neutral.

**Important focus caveat:** the proof-of-concept verified desktop touch suppression and independent-flip family in Minecraft; it did **not** establish universal game-focus safety or mouse input transparency in the captured quadrant. Avoid claiming those are already proven. Do not introduce a global input hook or full-screen rerouter merely to anticipate possible mouse behavior. Document those user verification targets explicitly.

## 6. Production logging and observability (bounded)

Use the existing `AppLog` categories or one small `Touchpad` category:

- `TouchpadToggleSaved` — desired enabled, previous enabled.
- `TouchpadArmed` — reason/trigger, actual SteamDeck, physical rectangle, DPI/window flags (once per arm).
- `TouchpadDisarmed` — reason, hadContact, pending transitions discarded.
- `TouchpadCaptureUnavailable` — creation/geometry/window-thread error with code, **not** controller/presentation failure.
- Optionally **Debug** Down/Up/Cancel aggregated or rate-limited with pointer ID; **no per-Move logs**, no persistent pointer-coordinate history and no new diagnostics app.

Keep this low-rate feature testable from `Addon/Log`; future hardware logging may be performed by the user. Do not log private touchscreen trajectories continuously.

## 7. Offline/CI test plan — no hardware requirement

Use existing test project and seams. Test deterministic code behavior, not hypothetical instruction-level interleavings.

1. **Settings:** missing/malformed/wrong-type experimental flag defaults false without resetting other valid settings; `true` persists across save/reload/restart, false persists, no unintended `profiles.json` changes.
2. **Frontend:** typed bootstrap, read projection and setter; protocol 77 handshake, request dispatch, false/true roundtrip, failure UI rollback and unchanged setting on persistence rejection.
3. **Gate truth table:** live Deck + developer ON **+ nonzero actual Steam RunningAppID + verified foreground gameplay** -> eligible; OFF, Xbox360, Edge, no physical source, stock Center M, Overlay visible/capturing, suspend paused, shutdown, absent publisher, **BPM-only even with live Deck**, or **running game in background while BPM/desktop foreground** -> inactive. An enabled game profile is not required.
4. **Raw pointer handoff:** Down -> successive Moves -> Up; hold, drag outside with clamped coordinates, ignore outside-origin finger, release on cancel/capture loss, ignore extra finger without remapping the owner.
5. **Rapid tap:** Down+Up before a manual publisher tick still generates a touched native state followed by neutral on successive ticks, without a second `SetState`. A canceled/disarmed rapid tap must NOT be replayed on rearm.
6. **Publisher field isolation:** physical buttons/sticks/triggers/M1-M2/IMU and Steam/QuickAccess pulses coexist with touch; no synthetic RPadPress/Force or left-pad fields; no provider -> unchanged neutral.
7. **Real lifecycle invariants:** Toggle OFF while held, Overlay Show-before-pause (including Show failure), Overlay close and resume, SteamDeck->Edge/Xbox switch, physical loss, suspend/resume, restart/dispose, invalid bounds/window creation fault — no stale virtual touch or lingering *armed* HWND and no native attach/authority regression.
8. **Window plumbing:** test fake/native seam for owning-thread create/hide/destroy, eligibility and state reset; platform-specific integration checks can be conditionally run in supported Windows CI, not require a real touchscreen.
9. Build/run existing affected tests and frontend transport/UI tests; extend only where new typed contracts require it. Existing Full1902 owner teardown/publisher tests must remain intact.

Do **not** block PR review or merge over: missing device screenshots, absence of a game/VRR proof, uncontrollable physical touch data, a hypothetical microsecond race, or failure to demonstrate every possible Windows fullscreen mode. **Do** block genuinely reachable stuck-touch, input theft while OFF/Overlay, unsafe native writes, incorrect teardown, broken enabled default/persistence, or a real regression to routing.

## 8. Separate user-run verification after merge (not Codex acceptance)

These are manual observations for the user, not code/CI blockers:

- Boot with default OFF: no touch interception, existing Xbox360/Deck controls unchanged.
- Enable in Developer Menu while Steam Deck is actually active **and a Steam game is foreground**: single bottom-right quarter creates **right pad Touch and movement** under Steam Input; try center, corners, reversed Y if needed, hold, drag beyond edge, quick taps, cancellation.
- BPM navigation/home/library with no running game must **not** arm, even if Deck is live. A background-running game while BPM/desktop is foreground also must not arm. Only a foreground Steam game with live Deck qualifies; Xbox360/Edge never arm.
- Developer toggle OFF or Overlay opened: touched game/window no longer blocked by the invisible HWND; no stuck pad. Overlay touch and Main UI mouse/touch interaction remain usable.
- Exit game, change controller preference, suspend/resume, physically disconnect/re-enumerate, and restart Runtime: old contacts do not reappear; no unmanaged HWND persists on crash.
- Repeat the Phase 0 Minecraft presentation-path test (Independent Flip, drop count), and if possible a **separate physical VRR observation**. Existing D3DKMT `STATUS_ACCESS_DENIED` made prior physical VRR verdict `INCONCLUSIVE`; this is not evidence VRR was OFF. **Touch-induced ~60fps occurred even with PoC OFF**, so do not label that common touchscreen behavior a PoC regression without a controlled A/B comparison.

## 9. Explicit exclusions / next phases

Not in PR-T2: left Steam Deck pad, simultaneous two-zone gesture policy, arbitrary zone sizing/position, zone editor, touchscreen haptics, synthetic pad click, force, DualSense Edge output, **per-game/AppID profile enablement and Profile-master gating** (distinct from the required actual Steam RunningAppID/foreground gameplay admission), new ordinary Settings toggle, Overlay Profile setting, globally disabled touchscreen, driver/UIAccess rerouting, separate SteamDeck publisher, second VIIPER bus, extra native writer, CTW/HHC linkage, new multi-session machinery.

After T2 user hardware evaluation, refine coordinate direction/Windows window behavior if evidence justifies it, then tackle **two-pad layout/editor**, and **only last** replace the experimental Developer Menu preference with game-profile enablement and Overlay/Main App profile UX.

## 10. PR deliverables / completion

- [ ] Actual production Runtime component consumes single-zone WM_POINTER touch without drawing and without doing native SetState.
- [ ] T1 contact provider hooked into the current selected Deck publisher, single writer preserved.
- [ ] Minimum rapid-tap handoff + deterministic tests.
- [ ] Runtime gate and lifecycle disarm/arm wired to existing Full1902/Overlay/Power/Recovery ownership, default OFF, no unauthorized HWND.
- [ ] One persisted Developer Menu bool and correct frontend typed protocol 77/UI behavior.
- [ ] Normal gamepad/IMU/publisher cadence/VIIPER ABI unchanged.
- [ ] Focused offline tests and release build pass.
- [ ] PR description states actual tested code paths and **clearly lists pending user hardware verification after merge**.

**Done means code-reviewed, lifecycle-safe experimental production integration that can be enabled for the user's next hardware test; it does NOT mean physical Steam Input behavior or panel VRR has already been measured.**
