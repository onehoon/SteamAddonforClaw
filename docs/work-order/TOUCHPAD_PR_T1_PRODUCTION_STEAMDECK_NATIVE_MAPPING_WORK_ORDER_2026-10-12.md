# Work Order — Touchpad PR-T1: Production Steam Deck Right-Trackpad Native Mapping

**Date:** 2026-10-12  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementer:** Local Codex  
**Baseline reviewed:** `main`, including the merged Phase 0 TouchpadPoc (#768), the current canonical Steam Deck mapper/publisher and their existing tests. Recheck latest `main` before implementation.  
**Target:** One focused, preferably **under 500 changed LOC** PR.  
**Physical validation:** The user performs this **after merge**. No real device, game, Steam, VRR, or touchscreen validation is a Codex/CI/PR-merge blocker.

## 0. Updated product staging decision (supersedes the old Phase 1 order)

Production touch acquisition and native touch publication will be implemented inside the existing Full1902 Runtime, but **initial feature enablement is a temporary persisted Developer Menu option**. While that option is ON, an **actually attached, live Steam Deck presentation** may enable the touch regions without requiring a running game's per-AppID profile. Default OFF. Xbox360 and DualSense Edge never receive this initial Deck touch. The *only* temporary part is the enablement UI/policy; the native mapping/capture engine must be production-quality, not a disposable diagnostic.

**The Developer Menu toggle and Runtime-owned HWND are PR-T2, not this PR.** The full game-profile master, Steam-page toggle, Overlay Profile toggle, editor, and per-game policy are deliberately LAST, after hardware characterization. Do not implement or partially persist `GameProfile.VirtualTouchpadEnabled` now.

For PR-T1, implement the **native input-mapping end of the production path**, with a testable optional publisher input seam that is *unwired by default*. No window or actual touchscreen input is to be acquired in this PR.

## 1. Required source-of-truth reading

1. [Full1902 document authority](../Full%201902%20Implementation/README.md) and its current priority order, especially [controller owner](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md), [HidHide/startup policy](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md), and [elevated Runtime](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md).
2. [Touchscreen Virtual Trackpads architecture, including actual 2026-10-12 Phase 0 findings](../STEAM_TOUCHSCREEN_VIRTUAL_TRACKPAD_ARCHITECTURE_2026-10-12.md), especially sections 4, 5.1.1, 6.1, 9–10. Its older phase ordering is superseded **only** by section 0 of this work order.
3. [Merged Phase 0 diagnostic and its contract](TOUCHPAD_POC_PHASE0_BOTTOM_RIGHT_QUARTER_WORK_ORDER_2026-10-12.md), [`tools/TouchpadPoc/`](../../tools/TouchpadPoc/).
4. Actual Addon source: [`SteamDeckDeviceStateMapper.cs`](../../src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs), [`CanonicalSteamDeckInputPublisher.cs`](../../src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs), [`SteamDeckSystemButtonOverlay.cs`](../../src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckSystemButtonOverlay.cs), [`CanonicalViiperNativeTypes.cs`](../../src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs); corresponding existing mapper/publisher tests.
5. [VIIPER native Steam Deck state/report](https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/inputstate.go), [constants](https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/const.go), [report tests](https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/steamdeck_test.go), and [fork ABI](https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md). Verify the pinned bundled native ABI matches our managed declaration.
6. Reference, **not code to port**: [HHC `SteamDeckTarget.cs`](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamDeckTarget.cs) sets right Touch in report byte 10 (`0x10`), native `RightPadX/Y` at bytes 20–23. HHC's [`OverlayTrackpad.xaml.cs`](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Views/Windows/OverlayTrackpad.xaml.cs) instead generates **DS4** two-contact touchscreen coordinates. Do **not** copy its half-width DS4 coordinate transform into Deck's independent right pad.
7. [Linux `hid-steam.c`](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-steam.c) declares Deck pad axes as signed `[-32767, +32767]` (`ABS_HAT0X/Y`, `ABS_HAT1X/Y`). That is stronger consumer-range evidence than assuming the entire `short` range. It does **not** itself certify Windows Steam Input's preferred on-screen orientation; preserve an isolated, testable Y polarity decision for the initial field test.

**Standalone Full1902 only. No CTW integration.** One Windows user/interactive session; no multi-session/RDP or theoretical race hardening.

## 2. Findings in current production code

| Site | Actual current behavior | T1 implementation |
| --- | --- | --- |
| `SteamDeckDeviceStateMapper.Map(ControllerState, bool suppressM1=false, bool suppressM2=false, MsiClawMotionState? motion=null)` | Already maps physical buttons/triggers/sticks/rear buttons and normalized gyro/accel; **all pad touch/coords/press/force = 0** | Append one optional right-pad contact value, leave existing arguments and their behavior unchanged |
| `SteamDeckDeviceState` | Existing 76-byte typed canonical ABI includes `RPadTouch:byte`, `RPadX/Y:short`, `RPadPress:byte`, `RPadForce:ushort`; left pad equivalent | **Reuse unchanged**; never change native struct, import, size, packing or VIIPER report |
| `CanonicalSteamDeckInputPublisher` | One monotonic ~250Hz Deck publisher; `PublishCurrentStateOnce` maps one physical snapshot, one motion snapshot, applies `SteamDeckSystemButtonOverlay`, calls **one** `_sink.SetState` | Add nullable optional right-pad state provider and merge its current value into the **same** mapping call, before the button overlay |
| `SteamDeckSystemButtonOverlay.Apply` | Modifies only Steam and QuickAccess; preserves other mapped fields | Keep unchanged |
| Existing tests | `SteamDeckDeviceStateMapperTests`, `CanonicalSteamDeckInputPublisherTests` with deterministic manual ticks/fake sinks | Extend, do not replace or weaken |
| `MsiClawAddonPresentation` / `AddonProcessHost` | Own attach/retire/Overlay capture/suspend logic and actual Steam Deck presenter | **No runtime wiring in T1**; T2 will supply active touch source through existing owner |

The default production constructor continues to receive **no touch provider**, so the shipped user-visible behavior remains byte-for-byte equivalent for untouched pad fields.

## 3. Required T1 deliverables

### 3.1 One small immutable right-pad contact value

Add a narrow internal value type close to the Steam Deck mapper (file name at Codex discretion, e.g. `SteamDeckRightPadContact.cs`):

~~~csharp
// Illustrative API. Use the smallest naming/visibility consistent with this codebase.
internal readonly record struct SteamDeckRightPadContact(
    bool IsTouched,
    short X,
    short Y)
{
    internal static SteamDeckRightPadContact Neutral => default;
}
~~~

- Represents **one actual Deck right pad contact**. It does not own pointer IDs, source HWND, input threads, device lifetimes, or controller authority.
- No touch: encode **RPadTouch=0, RPadX=0, RPadY=0, RPadPress=0, RPadForce=0** regardless of stale coordinates in a not-touched value.
- Keep **left** pad touch/axes/press/force zero in this PR. No left-window/dual-pad mapping yet.
- Do not broaden `ControllerState` or `IControllerStateSnapshotSource`: physical DirectInput input and touchscreen contact are independently sourced.

### 3.2 Pure coordinate conversion for future PR-T2

Expose a **small pure function**, colocated with the right-pad contact/mapping code, that converts a valid normalized touch location within one chosen input rectangle to Deck pad axes. PR-T2 will own physical-pixel geometry, pointer IDs, window bounds, and any region crossing; **T1 does not create an HWND**.

Input `normalizedX/Y` means **local left-to-right/top-to-bottom fraction in one capture rectangle**, not full-screen physical coordinates and not HHC's DS4 left/right half of a single touchpad. Convert with documented clamping and deterministic rounding:

~~~csharp
// Illustrative; centralize the polarity convention rather than spreading math.
private static short EncodePadX(double x)
{
    var normalized = Math.Clamp(x, 0.0, 1.0);
    return (short)Math.Round((normalized * 2.0 - 1.0) * 32767.0,
        MidpointRounding.AwayFromZero);
}

// Proposed initial top-positive direction. Confirm against raw Deck consumers.
// If current pinned consumer evidence proves the opposite, document and test
// that actual direction instead. Do not modify VIIPER's report wire format.
private static short EncodePadY(double y)
{
    var normalized = Math.Clamp(y, 0.0, 1.0);
    return (short)Math.Round((1.0 - normalized * 2.0) * 32767.0,
        MidpointRounding.AwayFromZero);
}
~~~

Requirements:
- Explicit `0 → negative X`, `0.5 → 0`, `1 → positive X`; initial Y assumption `0 → positive Y`, `0.5 → 0`, `1 → negative Y`. **Polarity is an initial mapping choice, not an already field-verified claim.**
- Return/construct **Neutral** if contact is inactive or coordinates are NaN, ±Infinity, or otherwise invalid. `Math.Clamp(NaN)` is not a safety strategy; reject first.
- Clamp finite values outside [0,1] to pad extrema; no integer overflow/wrap. Avoid `short.MinValue` since available Linux consumer-range evidence is ±32767.
- Keep helper focused on **one right pad**. Do not preemptively generalize to a geometry engine, arbitrary controllers, multi-contact reports, reusable axis transform library, calibration service, or game-profile layout storage.

### 3.3 Append-only native mapper argument; neutral by default

Keep exact existing leading argument order. A reasonable signature:

~~~csharp
internal static SteamDeckDeviceState Map(
    ControllerState state,
    bool suppressM1 = false,
    bool suppressM2 = false,
    MsiClawMotionState? motion = null,
    SteamDeckRightPadContact rightPad = default)
~~~

Within the existing mapping, **only** these fields differ when `rightPad.IsTouched`:

~~~csharp
RPadTouch = rightPad.IsTouched ? (byte)1 : (byte)0,
RPadX = rightPad.IsTouched ? rightPad.X : (short)0,
RPadY = rightPad.IsTouched ? rightPad.Y : (short)0,
RPadPress = 0,
RPadForce = 0,
~~~

- Do not remap a physical right-stick click (`R3`) to `RPadPress`.
- Do not fabricate touchpad press on DOWN, UP, tap, hold or drag. Steam Input interprets touch itself.
- Preserve left/right sticks, face buttons, M1/M2 R4/L4, all trigger paths, gyro/accel, quaternion neutrality, Steam/QAM synthetic button pulses and the native SetState failure path.
- Keep default `Map(state)` exactly equivalent to the current default. Update misleading mapper remarks that say Deck trackpad fields are *always* neutral.

### 3.4 Optional existing-publisher contact provider — no live wiring

Append one optional argument to the **existing** `CanonicalSteamDeckInputPublisher` constructor:

~~~csharp
Func<SteamDeckRightPadContact>? rightPadProvider = null
~~~

- Add a private field and capture one current contact **at most once per existing publish tick** on the current worker, along with the physical and motion snapshots.
- Null provider → Neutral. A throwing optional provider should result in **neutral right pad for that tick**, retaining gamepad and IMU publication, just as optional motion capture failure does. Do not throw/fault the entire canonical publisher merely because optional touch is unavailable. No per-tick exception log flood.
- Pass the current contact into `SteamDeckDeviceStateMapper.Map(..., rightPad: contact)`; preserve exactly one system-button overlay application and one `_sink.SetState(state)` per normal tick.
- No new timer, publishing task, event loop, native touch writer, locking framework, bus/device, or controller state authority.
- **Do not wire the provider from `AddonProcessHost` / `MsiClawAddonPresentation` in T1**. Doing so without a Runtime-owned and gated capture source would either be useless or introduce a second feature boundary. T2 supplies it.

### 3.5 Short taps across 4-ms publish boundaries — explicit scope boundary

A latest-only contact can miss a Down+Up that occurs entirely between publisher ticks. This is a **real input correctness concern**, not a speculative scheduler race.

**T1 must NOT claim it solves short-tap preservation.** T1 only maps the contact sample that its optional provider returns **on a publish tick** and must not invent a generic event queue or pulse manager without a real touch producer. T2, when integrating actual WM_POINTER events, must preserve at least one native Touch=1 publication followed by Touch=0 for an actual rapid tap **using the same existing Deck publisher**. Prefer a tiny bounded pending-transition handoff, designed against real TouchpadPoc timing; do not add a second SetState writer or unbounded history. Abandon/release pending events on disarm/capture loss/suspend/presentation retirement. Include deterministic short-tap tests in T2.

The T1 seam must not prevent that small T2 delivery policy. Avoid defining an interface whose contract *forbids* holding a pending Down until the next publish.

## 4. Mandatory unchanged product contracts

1. **All default states are neutral.** No saved developer option yet; no way to enable real touch in T1.
2. Steam/BPM actual presentation selection remains Xbox360 / Steam Deck / DualSense Edge under the **existing Full1902 owner**, with no added devices, PnP, HidHide, PID1901/1902 operations, Steam watcher, GameBar input, reattach/retry policy, or callback changes.
3. **Overlay Controller Capture** still stops/joins the active Deck publisher and writes neutral before letting the Overlay own controller input. T1 does not add a persistent touch source or window, so no new teardown path in this PR.
4. Suspend/Resume, physical device loss/re-enumeration, restart/crash/shutdown, routing rollback/fail-close, native ownership, LED/rumble callbacks and restore all retain the existing production policy.
5. Publisher cadence stays at its existing logical ~250 Hz; no per-touch SetState and no changes to the deadline scheduler or worker QoS.
6. No UI, `AppSettings`, `SettingsStore`, Frontend RPC protocol, developer menu, Main App Steam page, Overlay Profile, `GameProfile`, or deployment/packaging changes.
7. No CTW interfaces, global UIAccess touch rerouting, HID touchscreen disable, WPF/WinUI overlay, DirectComposition texture, graphics present/render path, or new background helper.
8. No theoretical locks/epochs/barriers/new authority layers. Support only actual in-scope input/report correctness.

## 5. Automated acceptance tests

Extend existing tests rather than creating a broad parallel test framework.

**`SteamDeckDeviceStateMapperTests.cs`**
- Legacy default calls (`Map(state)`, `Map(state, suppressM1, suppressM2, motion)`) leave all Deck pad fields neutral with no changes to buttons, triggers, right stick/R3, rear buttons or IMU values.
- Right-pad Down maps `RPadTouch=1` and exactly the specified signed `RPadX/Y`; `RPadPress/Force=0` and all left-pad fields neutral.
- Move updates X/Y while touch remains set; Up maps all right-pad fields back to zero, even when stale nonzero coordinates are supplied.
- Normalized points: four corners, center, partial/quarter positions, underflow/overflow finite values, NaN/infinite coordinates, inactive contact; deterministic sign/rounding/clamp behavior and no negative/positive wrap.
- Right-pad contact does not replace RStick/R3 or confuse physical gamepad input with pad-click input.
- IMU + right pad + normal gamepad state can coexist without field conflict.

**`CanonicalSteamDeckInputPublisherTests.cs`** (manual-tick / fake sink)
- No provider → old full mapped output, pad neutral on every tick.
- Provider changes Down → Move → Up on successive manual ticks → emitted typed states carry the expected Touch/X/Y transitions in order and retain ordinary gamepad/IMU.
- Provider called **once** per publish tick and never on a separate timer. Existing `SteamDeckSystemButtonOverlay` pulses coexist and do not zero the pad.
- Provider throws → that tick's pad neutral, existing SetState and normal input proceed, no publisher fault solely from optional touch failure.
- Existing sink SetState rejection still uses the existing publisher fault path.
- Existing stop/join/neutral/Overlay/presentation tests continue to pass unchanged.

Do not add a T1 unit test that presumes sub-4-ms Down+Up can be observed through a latest-state provider. Record that as a concrete PR-T2 test requirement, not a T1 implementation gap.

## 6. Local Codex execution and proof

- Inspect the final current code and its existing tests; choose the smallest compatible implementation matching this work order.
- Run the affected .NET solution/project build and targeted mapper/publisher tests; run the applicable repository tests without changing unrelated tests solely to obtain green CI.
- Check default unconnected publisher output and ABI invariants. If a Go/native VIIPER test is feasible without modifying the fork, it is optional reference validation only; **do not alter the VIIPER dependency**.
- State in PR description which mapper fields changed, the chosen X/Y sign policy, how absent/throwing provider fails neutral, the tests run, and specifically that **there is no touch HWND or UI yet**.
- Only modify the minimal T1 files plus tests. If it grows beyond roughly 500 changed lines, cut diagnostic/abstraction scope before widening the PR.

**Never require the user to run a touch-screen test, launch Steam, inspect VRR, or prove a real Deck binding before reviewing or merging this PR.** Physical verification belongs to the user after merge.

## 7. PR-T2 handoff (not implementation here)

T2 will add:
- The already proven **single bottom-right quadrant** pixel-invisible Win32 `WM_POINTER` region, now owned by the existing product Runtime rather than the standalone `TouchpadPoc` process.
- One persisted, **default-OFF** `Virtual Touchpad (Experimental)` switch **only in Developer Menu**, via the existing settings/Frontend RPC patterns.
- Enablement only with **actual live Steam Deck presentation** and enabled experimental switch; Xbox360/Edge, Overlay capture, Suspend/Resume admission gate, physical input failure, presentation retirement, and process shutdown disarm and neutralize. GameProfile/AppID/master are **not** active feature gates during this test stage.
- Minimal transition-preserving WM_POINTER→existing publisher handoff so quick Down/Up cannot vanish, no second native writer, no change to native attach ownership.
- Focused lifecycle, no-leak/default-off, and live-switch tests in code. The user later validates game input, physical VRR (which remained unverified in Phase 0 because D3DKMT returned `0xC0000022`), and actual Steam Input touch behavior.

After these tests, add left pad and adjustable layout in a separate phase. Add per-game profile settings **last**. The experimental Developer Menu flag will ultimately be retired/replaced as the feature's enablement authority, **not** kept as a competing second gate.

## 8. Done

T1 is code-complete when the **unwired-by-default** native right-pad mapper and same-publisher optional input seam can deterministically output Down/Move/Up-style samples with correct signed pad axes, do not synthesize Press/Force, leave all older output unchanged without a provider, pass focused offline tests, and do **not** introduce any touch interception, runtime enablement, alternative controller ownership or new active publication path.
