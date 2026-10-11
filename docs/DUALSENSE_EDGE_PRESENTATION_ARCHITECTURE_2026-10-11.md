# Full1902 DualSense Edge Virtual Presentation — Architecture and Integration Plan

**Date:** 2026-10-11  
**Status:** PR-A typed backend is merged in PR #754. PR-B Settings and runtime integration is implemented on a topic branch for review; physical DualSense Edge verification remains with the user after merge.
**Repository:** onehoon/SteamAddonforClaw  
**Product:** Standalone Steam Addon for Claw / Full PID1902  
**Scope:** Settings-selectable Steam/BPM virtual-controller presentation: existing Steam Deck or new Sony DualSense Edge. Xbox360 stays the normal Windows output.

> This is an architecture/design authority for the proposed feature, **not** evidence that virtual DualSense Edge already works in Steam Input or Xbox Game Bar. The local Codex implements code and automated tests; **physical device verification belongs exclusively to the user after merge**, and is never a PR review/CI/merge blocker.

---

## 1. Executive decision

Add exactly **one experimental DualSense Edge presentation**, not ordinary DualSense and not Xbox Elite.

The ordinary **Settings** page exposes one saved preference in a **standalone SettingsCard**, not inside Developer Menu:

- **Steam Deck (Default)** — existing behavior.
- **DualSense Edge (Experimental)** — when the existing Full1902 Steam/BPM selector asks for the Steam-facing controller, attach/publish Edge instead of Deck.

This is **not** a three-way always-attached controller system and **not** a separate routing mode. The user preference chooses the identity of the **single** Steam-facing virtual presentation; the existing actual Steam game / Big Picture Mode predicates still choose **Xbox360 versus Steam-facing**.

~~~text
Center M Disabled / healthy Full1902 PID1902 ownership
    -> one owned physical DirectInput input stream
    -> persistent HidHide baseline
    -> one canonical VIIPER server and USB bus
    -> read actual Steam RunningAppId / BigPictureActive
       |
       +-- neither is active ---------------------> Xbox360
       |
       +-- Steam game or Big Picture active ------> Settings preference
                                                     | Steam Deck (default)
                                                     | DualSense Edge (experimental)
                                                     v
                                          attach ONLY the selected device
                                          start ONLY its one input publisher
~~~

The current shipped behavior is **Xbox360 <-> Steam Deck**, with no modification unless the user explicitly selects experimental Edge on Settings.

### 1.1 What is and is not changing

| Topic | Existing baseline | Proposed Edge behavior |
| --- | --- | --- |
| Controller authority | Center M Enabled/Disabled, reboot-bound | Unchanged |
| Physical controller | MSI PID1902, one DirectInput source | Unchanged |
| Isolation | Persistent Addon-owned HidHide baseline | Unchanged |
| Virtual bus | One canonical VIIPER server/bus | Unchanged |
| Steam/BPM selection condition | RunningAppId != 0 OR BigPictureActive | Unchanged |
| No Steam/BPM | Xbox360 | Xbox360 |
| Steam/BPM active | Steam Deck | Steam Deck OR Edge, by saved Settings preference (Steam Deck default) |
| Maximum attached virtual controllers | Exactly one | Exactly one |
| M1/M2 in Steam-facing presentation | Separate R4/L4 | Separate R4/L4 |
| Gyro acquisition | Shared Full1902 Motion Core | Reuse same core, encode Edge reports |
| Existing front-button bindings | User-selectable Normal/Steam domains | Unchanged settings, change output translation only |
| Steam/QAM | Native Deck Steam/QuickAccess fields | Edge PS and PS+Cross synthetic button pulses |
| Feedback | Model-specific callback -> owned physical rumble sink | Edge ordinary two-channel rumble only |
| Native Game Bar suppression | WING Win+G guard while Addon owns controller | Unchanged |
| Frontend | Ordinary Settings page already uses SettingsCard controls | Add one standalone Settings options card |

### 1.2 Explicit non-goals

No ordinary DualSense virtual presentation; no DualShock 4; no Xbox Elite protocol; no simultaneous XInput companion controller; no DualSense advanced haptics, adaptive triggers, speaker, mic, headphone/audio stack, lightbar/player LEDs, controller battery virtualization, or fake touchpad fingers. No new motion sensor backend, Steam Input setting editor, per-game Edge override, game-profile changes, PID1901/PID1902 mode policy, CTW integration, HHC dependency, new service, new routing manager, new controller-authority state machine, or generalized plugin framework.

The existing Addon Overlay remains the preferred Addon-specific quick-settings surface. Steam native QAM is a separate front-button action.

---

## 2. Authority and current-code baseline

### 2.1 Document precedence

The following **current** Full1902 documents govern controller ownership and safety:

1. [Full1902 document authority](Full%201902%20Implementation/README.md).
2. [Deterministic HidHide/startup policy](Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md).
3. [Reboot-bound authority and lifecycle](Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md).
4. [Controller ownership architecture](Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md).
5. [Elevated Runtime architecture](Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md).

Current code wins over historical work-order implementation sketches. Useful implementation context:

- [PR7 presentation switching](work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md).
- [Current common motion/gyro plan](gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md).
- [Gyro PR2 native Steam Deck publication](work-order/GYRO_PR2_FULL1902_STEAMDECK_NATIVE_IMU_OUTPUT_WORK_ORDER_2026-10-09.md).
- [Current model-specific WING/Center M defaults](work-order/FRONT_BUTTON_A2VM_DEVICE_DEFAULT_MAPPING_WORK_ORDER_2026-10-10.md).
- [Fork native API authority](https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md).

### 2.2 Verified current source integration map (main, inspected 2026-10-11)

| Existing file / component | Verified role | Edge change |
| --- | --- | --- |
| Devices/MSI/Claw/MsiClawAddonPresentation.cs | One actual active kind, one publisher, first attach, fresh reconcile, pause/resume, classified retirement and feedback | Add Edge kind and typed attach/publish/retire paths; use one desired-kind resolver everywhere |
| Steam/SteamSessionRuntime.cs | SteamPresentationSnapshot; WantsSteamDeck = RunningAppId != 0 OR BigPictureActive | Keep detector/semantics unchanged; treat name as legacy meaning Steam-facing demand |
| Hosting/AddonProcessHost.cs | First attach, Steam/BPM event reconcile, physical recovery, motion gating, front-button wiring | Pass/read saved preference, request reconcile on preference change, widen active Steam-facing conditions |
| VirtualOutput/Viiper/CanonicalViiperRuntime.cs | Persistent one server/bus, pre-created detached Xbox360 and Deck, native attachment and teardown | Add optional Edge logical device on the SAME bus with classified ownership |
| VirtualOutput/Viiper/CanonicalViiperNativeApi.cs | Explicit managed native exports, typed handles, callback rooting, shared ownership | Bind Edge native ABI and root callback correctly |
| VirtualOutput/Viiper/CanonicalSteamDeckSession.cs | Typed Deck attach/status/neutral/detach and state sink | Implement similarly narrow Edge typed operations; do not copy generic lifecycle architecture |
| VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs | Existing ~250 Hz report publisher and fault/join contract | Reuse scheduling pattern, add Edge-specific mapper/state sink; preserve same join-before-detach invariant |
| VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs | Buttons, M1/M2 R4/L4, trigger/axis, native Deck gyro | Keep unchanged; add dedicated Edge mapping with its own axes/units |
| Devices/MSI/Claw/MsiClawMotionState.cs and motion source | Independently fresh normalized gyro deg/s and accel g | Reuse snapshots, freshness and neutralization |
| Settings/AppSettings.cs / SettingsStore.cs / StartupSettingsCoordinator.cs | Persisted settings, loaded into Runtime | Add one enum preference; missing/invalid settings -> Steam Deck default |
| Contracts/Frontend/FrontendContracts.cs | Settings snapshot, mutation APIs, controller badge | Extend typed preference and Edge active status |
| Frontend/InProcessAddonFrontendControl.cs and FrontendTransport | Runtime mutation and named-pipe UI contract | One typed setter + snapshot and protocol version update |
| UI/Views/SettingsPage.xaml(.cs) | Ordinary Settings page | Standalone SettingsCard and selection, not Developer Menu |
| Contracts/FrontButtons/FrontButtonMapping.cs | User editable normal/Steam mappings, hardware defaults | Keep mapping schema/defaults; Edge counts as an actual Steam presentation |
| Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs | Dispatches WING/Center M actions from real active presentation | Change Steam-domain check from Deck-only to Deck-or-Edge |
| CenterM/FrontButtonActionExecutor.cs | Shared SteamButton/SteamQuickAccess semantic action names | Keep action names; active presentation resolves the pulse behavior |
| Status/Full1902ControllerBadgeEvaluator.cs | Read-only virtual controller identity projection | Add Edge active badge, no false SteamDeck label |

There is currently **no production Edge presentation** in the Addon. Existing headers from bundled VIIPER can contain Edge declarations; that is not equivalent to managed runtime/publisher implementation.

### 2.3 Invariants that cannot change

- Exactly one Windows user and one interactive session; no Fast User Switching/RDP/multi-session support.
- MSI Center M Enabled = stock MSI owns controller, no Addon physical takeover or virtual gamepad.
- MSI Center M Disabled = Addon owns PID1902, one source, persistent HidHide, one VIIPER runtime/bus.
- A Steam/BPM presentation switch **never** writes MSI GamepadMode, mutates HidHide, recreates server/bus, or restarts physical ownership.
- Existing real recovery lifecycle handles sleep, hibernate, resume, device loss/re-enumeration, process restart, unexpected shutdown and explicit stock-safe release.
- A failed or outcome-unknown native attach/detach is **fail-closed**. Never attach Deck or Xbox360 as an automatic emergency substitute while Edge ownership is uncertain.
- Never detach a device whose publisher was not proven stopped/joined; release native output callbacks and request physical motor STOP before retiring their device, consistent with existing policy.

---

## 3. Exact presentation selection contract

### 3.1 Preference

Use one narrowly scoped, persistent value, e.g.:

~~~csharp
public enum SteamVirtualControllerPreference
{
    SteamDeck = 0,
    DualSenseEdge = 1
}

// AppSettings: init-only, old settings remain valid.
public SteamVirtualControllerPreference SteamVirtualController { get; init; }
    = SteamVirtualControllerPreference.SteamDeck;
~~~

The name identifies a **Steam-facing virtual presentation**, not Valve's original 2015 Steam Controller hardware. The standalone Settings card should label it "Steam / Big Picture Controller" with options "Steam Deck (Default)" and "DualSense Edge (Experimental)".

Only two accepted enum names; a missing, malformed, numeric, or unknown value uses the existing Steam Deck behavior. Do not globally reset unrelated persisted settings on an invalid field. Existing SettingsStore string-enum contract and normalization pattern should be followed.

The selected preference persists across Runtime/UI restart. Developer Menu visibility is unrelated to this ordinary Settings preference and must never reset it.

### 3.2 Single resolver

The same resolver is used for **initial attach, ordinary runtime reconcile, developer rumble re-arm restore and Resume after Suspend**:

~~~csharp
private static AddonPresentationKind ResolveDesiredPresentation(
    SteamPresentationSnapshot steam,
    SteamVirtualControllerPreference preference)
{
    if (!steam.WantsSteamDeck)
        return AddonPresentationKind.Xbox360;

    return preference == SteamVirtualControllerPreference.DualSenseEdge
        ? AddonPresentationKind.DualSenseEdge
        : AddonPresentationKind.SteamDeck;
}
~~~

This is design pseudocode; actual location should minimize existing call-site churn. Capture the **fresh Steam snapshot and current saved preference within the existing presentation-owner gate**, not when an event is first queued. Do not introduce a new epoch, source of truth, debounce graph or polling loop.

The existing SteamSessionRuntime predicate stays exactly the same. No Xbox game-session facts are added to it: Xbox Game Pass/non-Steam contexts continue to use Xbox360 unless the existing Steam/BPM predicate independently becomes true.

### 3.3 Behavior matrix

| Real Steam game | BPM | Preference | Desired attached output |
| --- | --- | --- | --- |
| No | No | Either | Xbox360 |
| Yes | No | Steam Deck | Steam Deck |
| No | Yes | Steam Deck | Steam Deck |
| Yes | Yes | Steam Deck | Steam Deck |
| Yes | No | Edge | DualSense Edge |
| No | Yes | Edge | DualSense Edge |
| Yes | Yes | Edge | DualSense Edge |
| No | No | Edge | Xbox360 |

Normal startup with a saved Edge preference but **no Steam/BPM** must still initialize and attach Xbox360 normally. The Edge is not an always-on Windows controller.

### 3.4 User switching while app is running

User selects Edge in Settings -> validate/store selection -> trigger the **existing** RequestControllerPresentationReconcile pathway -> presentation gate reads fresh desired state. If the current actual state is Xbox360 and Steam is inactive, there is no virtual-device churn. If Steam-facing presentation is currently live, existing orderly stop/neutral/detach/attach behavior applies.

On switch in a running game, a brief device disconnect/reconnect and Steam per-device layout refresh are expected. The UI must not report "Edge active" merely because the preference was saved: actual status comes from the existing presentation owner. No application restart or MSI PID mode cycle should be required by design.

---

## 4. Canonical VIIPER native ownership

### 4.1 Upstream/fork capabilities actually present

The [onehoon/VIIPER fork native interface](https://github.com/onehoon/VIIPER/blob/main/lib/viiper/dualsense.go) already contains:

- CreateDualSenseEdgeDevice (separate Edge constructor, shared DualSense state family).
- SetDualSenseDeviceState.
- SetDualSenseOutputCallback.
- RemoveDualSenseDeviceEx (classified removal; Edge supported).
- Existing generic GetUSBDeviceIdentity, GetUSBDeviceAttachmentState, AttachUSBDeviceEx and DetachUSBDeviceEx.

The [fork API document](https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md) lists Edge in the typed-device support matrix. This feature should **reuse those native exports**, rather than add a new DualSense emulation protocol or rewrite VIIPER.

The VIIPER Edge identity is Sony VID 054C / PID 0DF2. It is a **USB HID PlayStation gamepad**, not XInput/XUSB. It has a real Edge descriptor, but host recognition remains to be tested with the **packaged fork DLL**, not inferred from a header.

Upstream Edge implementation references:

- [Device and VID/PID selection](https://github.com/Alia5/VIIPER/blob/main/device/dualsense/device.go).
- [HID descriptor](https://github.com/Alia5/VIIPER/blob/main/device/dualsense/descriptor.go).
- [State and feedback wire layout](https://github.com/Alia5/VIIPER/blob/main/device/dualsense/state.go).
- [Constants and motion scales](https://github.com/Alia5/VIIPER/blob/main/device/dualsense/const.go).

### 4.2 Preferred minimal lifetime design

The current shipped Runtime eagerly creates two detached logical devices (Xbox360 and Steam Deck) on one persistent server/bus. **Do not turn an experimental Edge failure into a failure of the default controller path.**

Preferred implementation: create the Edge logical device **lazily, once on first Edge selection that needs attachment**, then retain its known-owned detached handle for the remaining canonical VIIPER runtime lifetime. It uses the existing server/bus; it is never a second bus or process. On subsequent Edge switches, reuse the same typed handle if state and ownership remain known-safe.

~~~text
canonical VIIPER server + USB bus (existing owner)
    |
    +-- typed Xbox360 (existing; created detached at start)
    +-- typed Steam Deck (existing; created detached at start)
    +-- typed DualSense Edge (new; created detached on first demand)
            |
            +-- exactly one device attached at any instant
~~~

If the existing native owner makes eager creation clearly simpler **without** expanding failure impact on default boot, Codex may choose that narrower implementation, but must prove normal Xbox360/Deck startup is not broken by Edge-only failures. No fallback VIIPER runtime.

On creation: require successful typed creation with autoAttachLocalhost=false, matching bus identity, and a **proven Detached** tracked attachment state before entering Ready-to-attach. Treat inability to establish ownership/status as the existing unsafe/fail-close boundary. Do not leave an untracked native handle and claim initialization failure was clean.

On activation: classified attach, neutral input state, start one bounded publisher, register optional feedback. On retirement: stop/join publisher, clear synthetic pulse, disarm callback and physical STOP, write neutral, classified detach. On final teardown: remove owned Edge typed handle using RemoveDualSenseDeviceEx **before** removal of its parent USB bus/server. Extend existing cleanup/residual-attachment checks and classified teardown phases, rather than adding another independent owner.

### 4.3 C ABI/managed binding discipline

Use the actual **generated header for the pinned bundled fork build**, not hand-inferred wire packing. Existing generated libVIIPER.h declarations are a reference; confirm they match the shipped DLL exports/version.

The C state family contains:

~~~c
typedef struct {
    int8_t LX, LY, RX, RY;
    uint32_t Buttons;
    uint8_t DPad, L2, R2;
    uint16_t Touch1X, Touch1Y;
    uint8_t Touch1Active;
    uint16_t Touch2X, Touch2Y;
    uint8_t Touch2Active;
    int16_t GyroX, GyroY, GyroZ;
    int16_t AccelX, AccelY, AccelZ;
} DSDeviceState;
~~~

This is a C struct passed **by value** to native SetDualSenseDeviceState; the **33-byte on-wire state** is a different contract and must not be mistaken for managed struct size. Verify StructLayout/field offsets/sizeof and calling convention for the target x64 C ABI using deterministic automated tests, including Edge remove-result enum values.

Managed binding must root DSOutputCallback while registered; clear it on retirement; release the root only after proven removal/detach/parent teardown according to existing ownership rules. Extend the existing shared _deviceOwnership/callback cleanup, not a new callback manager. Avoid unconditional required-export initialization that would make a missing experimental export disable both normal controller modes; the packaged DLL/build should remain the canonical supported dependency.

---

## 5. DualSense Edge input mapping

### 5.1 Ordinary controls

Build one dedicated Edge mapper from the existing physical ControllerState + AuxiliaryButtonState + normalized MotionState. No ordinary DualSense app setting or mapper.

| MSI physical input | Virtual DualSense Edge |
| --- | --- |
| A | Cross |
| B | Circle |
| X | Square |
| Y | Triangle |
| Left/Right Bumper | L1/R1 |
| Left/Right analog trigger | L2/R2 analog 0..255 |
| Left/Right full-pull digital | L2/R2 digital bits as available, not faked from analog max |
| D-Pad | 4-direction bitfield with diagonal preservation |
| Left/Right stick | LX/LY/RX/RY signed int8, preserving centers/extrema and correct Y orientation |
| L3/R3 | L3/R3 |
| Start/Menu | Options |
| Back/View | Create |
| Physical M1 = RightRear | Edge R4 |
| Physical M2 = LeftRear | Edge L4 |
| Virtual system button | PS, only through SteamButton/QuickAccess pulse |
| Edge LFn/RFn | Neutral; no mapped physical buttons |
| Touchpad click/touch | Neutral initially |
| Microphone mute | Neutral |

Edge button masks from fork const.go: Cross 0x20, Circle 0x40, Square 0x10, Triangle 0x80; PS 0x00010000; L4 0x00400000; R4 0x00800000. Edge R4/L4 are **independent buttons**, not duplicates of A/B/X/Y.

Use existing M1/M2 rear-button release suppression when switching presentations, so a held rear button does not become a fresh unintended press on the new device. Xbox360-only global/per-game M1/M2 mapping is **not** applied to Edge; independent paddle exposure is the purpose of this presentation.

Map int16 physical sticks to int8 Edge axes with bounded conversion and confirmed sign. Do not simply cast/truncate int16 to int8. Keep left/right stick click and buttons independent.

### 5.2 Report and cadence

VIIPER accepts the typed 33-byte input-state content, then constructs its 64-byte DualSense USB input report (Report ID 0x01). The report is sent through the existing USB/IP attachment, not XInput. The input publisher should use the existing proven high-resolution deadline/worker scheduling and stop/join semantics (nominal 250 Hz) rather than Task.Delay polling or a new scheduler abstraction.

A one-time or long-lived physical input observation cadence change is **not** required. Do not create extra IMU polling on the 250 Hz publish thread.

### 5.3 Gyroscope/accelerometer

Reuse existing MsiClawMotionSource and MsiClawMotionState. The current healthy-motion-reader condition is SteamDeck-specific in AddonProcessHost.ShouldRunMotionReaders; change it to **healthy attached live Steam-facing motion-capable presentation: Deck OR Edge**. Xbox360 still does not start native gyro readers.

Native Edge fields are raw signed 16-bit values. The upstream fork specifies:

- Gyro counts per degree/second = **16.384**.
- Acceleration counts per m/s² = **835.07**, equivalent to approximately **8192 raw counts per g**.
- Each component must be rounded and clamped to signed int16.
- Existing normalized gyro is degrees/second; existing normalized accel is g.
- Treat the Edge axis/sign transform as its **own controller coordinate mapping**. Do not assume that the Steam Deck Pitch/Yaw/Roll/Accel permutation is automatically correct for Sony axes.

From the current MotionState, propose a target-specific mapping with explicit yaw/roll/pitch and stationary-gravity reference cases; keep the transform confined to EdgeDeviceStateMapper. The initial sign/permutation needs user hardware confirmation. Do not change common Motion Core normalization to fix an Edge-only output orientation.

PR-A's initial software transform maps normalized source `+X/+Y/+Z` directly to Edge `+X/+Y/+Z` for gyro and accelerometer. That is a documented candidate transform, not a claimed physical match; the user must confirm stationary-gravity and rotation reference poses on hardware after merge.

Keep the PR1/PR2 existing freshness checks (gyro 250 ms; accel 500 ms) and require a complete usable snapshot before encoding motion. An unavailable/stale/invalid source publishes **neutral motion** and continues ordinary gamepad input. No stale nonzero gyroscope and no invented gravity vector when measurements are unavailable. Motion failures must never release PID1902 ownership.

---

## 6. Steam Button / QAM and WING / Center M

### 6.1 Existing product mapping stays authoritative

The App currently has ONE FrontButtonMappingSettings with Normal and Steam domains, each holding one Gamebar/WING and one Center M/OEM1 binding. The user can edit them. **Do not** add an Edge-only four-slot front-button mapping, duplicate actions, or override saved user settings.

The current model-aware **first-install defaults** must remain:

| Physical family | Right button | Left button | Normal right / left | Steam right / left |
| --- | --- | --- | --- | --- |
| Claw 8 EX CG3EM | Center M | WING | Addon Overlay / Steam Big Picture | Addon Overlay / Steam Button |
| Claw 7/8 AI+ A2VM | WING | Center M | Addon Overlay / Steam Big Picture | Addon Overlay / Steam Button |

These defaults describe initial settings only; existing user choices always win.

### 6.2 Domain resolution

Current MsiClawFrontButtonRuntime passes a callback called isSteamDeckPresentationActive to both action dispatchers. Replace its semantics with **isSteamFacingPresentationActuallyActive** (or a similarly narrowly named callback) and return true for active Deck or active Edge. Xbox360, no presentation, an unsafe transition and failed attachment remain the Normal domain. Preserve the existing native Win+G WING suppression rule while Addon owns the controller.

Do not change keyboard hotkeys, user-specified application launch, Xbox app action, Big Picture launch or Addon Quick Settings Overlay routing.

### 6.3 Output-specific action translation

**Confirmed user observation on a physical normal DualSense:** PS invokes the Steam button; PS + Cross invokes Steam Quick Access (QAM). The real DualSense was reported as a wired/Pico-bridged USB VID_054C PID_0CE6 device with Sony HID gamepad interfaces. This establishes the observed physical behavior, **not** guaranteed emulated Edge behavior.

| Persisted semantic action | Actual Deck output | Actual Edge output |
| --- | --- | --- |
| SteamButton | Existing native Steam pulse | Synthetic PS button pulse |
| SteamQuickAccess | Existing native QuickAccess pulse | Synthetic PS + Cross chord |
| QuickSettingsOverlay | Existing Addon Overlay | Same Addon Overlay |
| SteamBigPicture / XboxApp / KeyboardHotkey / LaunchApplication | Existing implementation | Unchanged |

Implement Edge PS/PS+Cross as **brief, finite, nonblocking button pulses over the SAME Edge input publisher**; the front-button event must not call native SetState from a second concurrent publisher or create a transient device. The pulse overlay must merge with physical buttons for the required frames, then release; clear it on switch/retire, Overlay capture pause, Suspend and failure. Preserve normal held Cross behavior outside the synthetic chord.

This is an implementation target based on observed physical controller behavior, **not** a promise that Steam Input will recognize the virtual Edge identically. If virtual QAM support fails on real hardware, investigate descriptor/Steam Input behavior before adding a second controller or unrelated keyboard hacks.

### 6.4 Existing steam front-button actions may return unavailable

Current action executor deliberately tolerates inability to issue an output pulse; it does not treat that as a reason to release controller authority. Keep that policy for Edge. A press during attach/detach/recovery may be ignored, not queued into a later device.

---

## 7. Feedback and vibration

Implement **ordinary** two-channel rumble only. The fork SetDualSenseOutputCallback returns rumbleSmall / rumbleLarge 8-bit host values, plus LED fields. The current physical IPhysicalRumbleSink accepts two-motor feedback. Follow the established Xbox360/Deck feedback adapter lifetime and scaling conventions, mapping callback motor intensities to physical sink with measured motor ordering; stop both motors on callback disarm, presentation transition and shutdown.

A callback registration failure should leave ordinary Edge input functioning with feedback unavailable, consistent with existing best-effort feedback policy; do not make feedback a controller-ownership failure. A feedback STOP or drain failure on paths that already require strict proof (e.g. explicit developer re-arm or short Windows session-end preparation) remains subject to the existing strict safety contract.

Explicitly out of scope: DualSense native high-frequency haptic audio/waveform semantics, adaptive L2/R2 force feedback, speaker/audio endpoints, lightbar behavior, player LED semantics, vibration-strength UI changes. Do not claim that ordinary rumble is equivalent to Sony advanced haptics.

---

## 8. Xbox Game Bar compatibility and evidence boundary

The requested Game Bar behavior is **navigation of an already open Xbox Game Bar with D-Pad/sticks/buttons**, not launching it with a Guide/PS button.

Known evidence:

- Existing virtual Xbox360: Game Bar navigation works on user hardware.
- Existing virtual Steam Deck: Game Bar navigation does not work on user hardware.
- Physical ordinary Sony DualSense: user personally confirmed D-Pad navigation of an already-open Game Bar.
- The user's Windows enumerated physical/Pico-bridged DualSense as USB VID_054C PID_0CE6 and HID game-controller interface; a separate Xbox wireless adapter was also present.
- VIIPER virtual **DualSense Edge** exposes Sony VID_054C PID_0DF2, USB HID gamepad. **No successful virtual Edge Game Bar test has yet been reported.**

Conclusions: XInput is **not a universal requirement** for Game Bar navigation; however a Sony VID/PID and generic HID gamepad descriptor are **not proof** that this particular virtual Edge is accepted by Windows/Game Bar. A physical controller with composite/vendor-specific companion interfaces and a VIIPER emulated single HID interface may enumerate differently. Any connected input remapper/Steam desktop configuration must be controlled for in the post-merge test.

**Do not add a parallel Xbox360 virtual controller, transient XInput device or Game Bar-specific routing workaround** to preempt a failure that has not been demonstrated.

---

## 9. Presentation switch, recovery and shutdown safety

### 9.1 Normal switch

~~~text
Steam/BPM observed or saved developer selection changed
    -> request existing asynchronous presentation reconcile
    -> acquire existing presentation gate
    -> read fresh Steam/BPM + saved preference
    -> if live desired == current: no-op
    -> otherwise:
         stop and JOIN old publisher
         clear any Deck or Edge synthetic button pulse
         disarm native feedback + request physical rumble STOP
         write neutral and classified-detach old typed device
         verify desired typed device's known-safe Detached state
         classified-attach exactly desired typed device
         publish neutral before ordinary live input
         start exactly one new publisher
         optionally arm ordinary rumble feedback
~~~

No PID write, no HidHide mutation and no secondary USB bus creation in a presentation switch. If the old publisher cannot be joined, or detach is uncertain, **never** attach a new device. If target attach or initial neutral fails after old retirement, remain detached/fail-closed; no silent fallback to old Deck or Xbox360.

### 9.2 Suspend/Hibernate/Resume

Use existing PauseForSuspendAsync / ResumeAfterSuspendAsync and PR7 fresh reconciler. Neutralize/stop Edge on suspend as for Deck, invalidate motion snapshots, and avoid replaying pre-suspend system pulses. On Resume compare current *actual* attached kind with a fresh resolved desired kind (including developer preference); only resume the same publisher after structural attachment proof. Otherwise use existing reconcile/recovery. No new sleep-only state machine.

### 9.3 Overlay capture

The Addon Overlay temporarily pauses game-facing publication. Extend same-presentation neutral, stop/join, and resume rules for Edge, including proof of attachment; never leave a live Edge publisher feeding a game during overlay capture. The Addon Overlay input router/controller capture is unchanged unless code inspection exposes an actual Edge-specific assumption.

### 9.4 Physical loss, PnP re-enumeration and Runtime restart

Owned DirectInput source loss/re-admission remains handled by existing physical recovery. The selected Steam-facing preference survives physical owner recovery and Runtime restart via SettingsStore; choose the desired presentation from actual Steam facts on recovery. Do not use device switching to drive PID1901/PID1902 transitions.

### 9.5 Explicit stock release / uninstall / shutdown

Continue the existing retirement ordering, classified native Edge detach/removal, physical rumble STOP, motion reader shutdown and canonical bus/server release. The A2VM narrow Windows end-session physical-mode preparation depends on proven publisher/VIIPER teardown within its existing budget; Edge must not bypass these safeguards. Unknown native ownership remains fail-closed; no forced cleanup of an outcome-unknown handle.

---

## 10. Frontend, settings and observability

### 10.1 Ordinary Settings page — standalone option card

Add one standalone SettingsCard on the existing SettingsPage (not DeveloperPage) with:

- Header: **Steam / Big Picture Controller**
- ComboBox or RadioButtons: **Steam Deck (Default)** / **DualSense Edge (Experimental)**
- Description: "Selects the virtual gamepad while a Steam game or Big Picture is active. Normal Windows mode remains Xbox360. Changing during a game may temporarily disconnect the controller."
- On mutation: disable duplicate submission, await existing frontend RPC, update selection from returned persisted snapshot; show a short error and restore prior value if mutation rejected.
- If practical, show read-only **Actual active output** separately from saved preference; do not conflate the two.

Do not create a new settings page, overlay tab, profile preference, local cache or secondary settings file. Developer Menu visibility is unrelated to this ordinary Settings choice and never gates the controller.

### 10.2 IPC

Extend FrontendSettingsSnapshot with one typed preference and IAddonFrontendControl with one mutation operation; use the existing InProcessAddonFrontendControl, named-pipe client/server and FrontendWire RPC switch. Follow current frontend version convention (current inspected FrontendTransportProtocol.CurrentVersion **71** on 2026-10-11; re-read latest main before implementation, then increment appropriately). UI is frontend-only; only Runtime persists preference and requests reconcile.

Extend FrontendControllerBadgeState to include **DualSenseEdgeActive**, map only the actual proven active presentation. Preserve existing Ready/NeedsAttention semantics and any status clients; no false "Steam Deck active" badge on Edge.

### 10.3 Diagnostics

Log structured events using existing ControllerPresentation / SteamOutput / Rumble / Motion categories, including:

- Requested preference and actual active kind.
- Actual Steam RunningAppId and BigPictureActive at selection.
- Edge typed create/identity/status, classified attach/detach/remove results.
- Publisher start/fault/join, heartbeat and report-state failures (bounded, not per-frame spam).
- Steam/QuickAccess pulse accepted/refused, with current presentation.
- Motion available/stale/neutral statistics and feedback callback availability.
- Presentation switch and physical recovery result, with explicit reason on fail-close.

Never log every input packet or add a new polling diagnostic loop. Avoid marking an unverified Game Bar success in production status.

---

## 11. Implementation staging

**Suggested two focused PRs, subject to latest code review:**

### PR-A — completed in PR #754

Implementation work order: [DualSense Edge PR-A typed backend](work-order/DUALSENSE_EDGE_PR_A_TYPED_BACKEND_WORK_ORDER_2026-10-11.md). The user separately updates the VIIPER dependency.

1. Add managed typed Edge ABI/state/callback and classified removal. **The user independently updates the VIIPER DLL/header/dependency pin; PR-A must not change dependency artifacts.**
2. Extend same canonical VIIPER owner with lazy/optional Edge logical device and complete teardown.
3. Add Edge state mapper/publisher, native neutral, motion encoding and basic ordinary rumble callback.
4. Add code-level tests for struct ABI, controls/M1/M2, gyro scales and neutrality, publisher stop/join, attach/retire, ownership/callback cleanup.
5. Do **not** change the shipped Xbox360/Deck selection policy. Edge is not production-selected in this PR.

### PR-B — Settings preference and runtime integration (implemented for review)

Implementation work order (written after PR #754 merged): [DualSense Edge PR-B Settings and Full1902 integration](work-order/DUALSENSE_EDGE_PR_B_SETTINGS_AND_RUNTIME_INTEGRATION_WORK_ORDER_2026-10-11.md). VIIPER dependency was independently updated in PR #753; PR-B must not re-update its binaries/header/lock.

1. Typed settings persistence and frontend RPC are implemented at protocol 72, with a standalone **SettingsPage SettingsCard** for **Steam Deck (Default)** / **DualSense Edge (Experimental)**; there is no DeveloperPage option.
2. One fresh selection resolver drives first attach, ordinary reconcile, Resume and developer re-arm recovery inside the existing presentation owner.
3. WING/Center M use the Steam domain for a live Deck or Edge; Steam and QAM actions use Deck behavior or finite Edge PS / PS+Cross pulses.
4. Edge uses the existing motion readers and the actual-live-controller badge; lifecycle and overlay-safe automated coverage is included.
5. Physical Edge recognition, controls, motion, vibration and lifecycle verification remains a post-merge user task and is not represented as completed here.

This division avoids exposing a partially wired Edge option while allowing one mergeable backend change and one integration change. If the latest implementation makes one smaller combined PR safer, minimizing lifecycle risk is more important than PR count; do not split a single owner across incomplete states.

---

## 12. Required code/CI tests — hardware independent

At minimum, deterministic tests should prove:

1. No new preference or invalid preference loads as SteamDeck; valid Edge choice persists/round-trips.
2. All four Steam AppID/BPM combinations select Xbox360 or exactly the persisted Deck/Edge preference.
3. First attach and runtime reconcile use the same desired-kind policy; setting changed while Steam inactive leaves X360 attached.
4. During active Steam/BPM, changing preference detaches/join-stops old kind and attaches only Edge, then can return to Deck; no secondary simultaneous attached device.
5. No-op when desired kind is already live; failure leaves safe detached/known state and does not silently attach an alternate kind.
6. Classified attach/detach/remove/unknown outcomes retain existing fail-close, physical owner and native resource-ownership rules.
7. Native x64 DSDeviceState ABI/field offsets and callback delegate lifetime match generated fork exports.
8. A/B/X/Y -> Cross/Circle/Square/Triangle; L2/R2 analog/digital; stick scaling/extrema; D-Pad diagonals; M1 R4/M2 L4 with release suppression.
9. PS pulse and PS+Cross chord are finite, in one publisher, do not leak across retarget/pause/Suspend.
10. Normal user-defined WING/OEM1 mappings, EX/A2VM defaults and WING Win+G suppression are unchanged.
11. SteamDeck native Steam/QAM and both existing output modes retain their behavior.
12. Edge motion starts only when allowed by healthy source and active Edge; invalid/stale motion becomes neutral while gamepad input continues.
13. Basic two-channel rumble callback, stop and retirement drain; no callback after proven handle removal.
14. Sleep/Resume preference changes and physical loss/recovery apply the fresh desired kind without new PID or HidHide mutations.
15. Overlay pause/restart, explicit Center M stock release and process teardown include Edge.
16. Live controller badge shows Edge only when actually active, with existing initializing/needs-attention status otherwise.

No test should assert a physical Sony device or Game Bar is available on CI. Hardware verification is **not a blocker** if code and deterministic tests are correct.

---

## 13. Post-merge hardware verification ledger — USER role

The following are **manual investigation targets after merge**, explicitly not Codex implementation or PR merge requirements:

| Manual step | Evidence to collect |
| --- | --- |
| Settings card default | Old installs show Steam Deck selected; Windows no Steam still Xbox360 |
| Select Edge, enter BPM/start game | Windows PnP reports Sony VID_054C PID_0DF2 and Steam Input identifies DualSense Edge |
| Standard controls | Stick axes, D-Pad, Cross/Circle, shoulders/triggers, Options/Create and L3/R3 operate correctly |
| Paddles | M1 -> independent R4; M2 -> independent L4; editable separately in Steam Input |
| Motion | Gyro yaw/pitch/roll directions and stationary gravity, including A2VM/CG3EM |
| Native Steam button/QAM | PS opens Steam; PS+Cross opens QAM with user’s Steam configuration |
| Xbox Game Bar already visible | Navigate with D-Pad/buttons, with no other input conversion interfering |
| Rumble | Ordinary vibration channel order and STOP/no stuck motor |
| Hot switch | Deck <-> Edge while BPM/game active, then leave Steam -> Xbox360 |
| Overlay, sleep/resume, device loss, restart | No stale publisher, stuck button/motor, dual virtual device, unsafe ownership, or erroneous physical PID switch |
| Explicit stock restore | Center M re-enable/uninstall restores preexisting stock-safe policy |

Physical ordinary DualSense success, including user's USB PnP evidence, is a **promising reference** but not proof that the VIIPER emulation works. In particular, real DualSense 0CE6 and virtual Edge 0DF2, and Pico bridge vs standalone virtual HID, may be processed differently.

---

## 14. Remaining uncertainties and non-blocking choices

**Hardware-specific, not implementation blockers:**

- Will the packed virtual Edge report be identified by Steam Input as an Edge, including its independently mappable R4/L4?
- Will the virtual Edge’s PS and PS+Cross trigger the same native Steam/QAM behavior observed on real DualSense?
- Will an already-open Xbox Game Bar accept D-Pad and action input from this virtual Sony HID?
- Which Edge-native IMU axis signs correspond to normalized Claw axes in practice?
- Will ordinary rumble callback values map to the physical left/right motors with expected force?
- Will Steam Input reconnect/reassociate the controller layout correctly after a hot output switch?

**Implementation-time choices that should not drive a new architecture:**

- Lazy one-time Edge typed creation is preferred to preserve default-mode startup safety.
- Implement a small Edge-specific pulse composition next to its publisher; do not generalize Deck pulse machinery into a subsystem solely to share two boolean buttons.
- Keep the existing publisher deadline timing and native ownership facilities. Add targeted code, not a new device-interface hierarchy unless the actual code exposes a concrete duplication problem.

These are the realistic boundaries of the requested experimental feature. Do not block PRs for speculative instruction-level races, unsupported multi-session paths or unproven pathological scheduling interleavings.

---

## 15. Design completion criteria

A complete implementation:

1. Leaves existing Full1902 Xbox360/SteamDeck behavior untouched by default.
2. Makes one persisted Settings card preference choose the **Steam-facing** output while preserving the original Steam/BPM condition.
3. Adds a real canonical VIIPER Edge typed state/publisher on the existing USB bus, with only one attached controller at any time.
4. Maps base controls, independent M1/M2 Edge paddles, normalized motion and ordinary two-channel rumble; excludes Sony advanced effects.
5. Preserves user-editable WING/Center M mappings while translating semantic Steam/QAM actions to Edge PS and PS+Cross pulses.
6. Preserves real lifecycle safety and classified fail-close ownership across switch/restart/Suspend/PnP/teardown.
7. Reports the **actual** attached presentation and is code/CI verifiable without requiring an MSI Claw or real DualSense device.
8. Leaves post-merge real device, Steam Input and Xbox Game Bar validation to the user.

**Architecture principle:** add a third *presentation kind* under one existing Full1902 physical/VIIPER owner, not a third controller authority, routing strategy or runtime.
