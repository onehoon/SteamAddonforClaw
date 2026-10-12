# Touchscreen Virtual Trackpads — Full1902 Architecture

**Date:** 2026-10-12  
**Repository:** `onehoon/SteamAddonforClaw`  
**Product:** Standalone Steam Addon for Claw / Full PID1902  
**Status:** Architecture proposal with **confirmed product policy** and **explicitly unverified Windows input/VRR feasibility**. No implementation or MSI Claw touch interception has been claimed.  
**Implementation role:** Local Codex. **Physical verification role:** User, after merge; never a mandatory local-Codex deliverable or a PR/CI/merge blocker.

> This document consolidates the user decisions made through 2026-10-12. In particular, the Steam-game profile master switch governs this feature, exactly like the existing per-game virtual-controller override. There is **no global Virtual Touchpad ON/OFF**. The previously considered independent-of-master behavior is **rejected**.

---

## 1. Goal and acceptance philosophy

Provide the MSI Claw touchscreen with configurable **left and right virtual Steam Deck trackpad zones**, with compatible behavior when the currently selected Steam-facing virtual presentation is **DualSense Edge**.

A user configures zone placement and size in the **Main App editor**, where those zones may be visibly illustrated. While playing, zones remain **completely visually absent**, receive physical touchscreen contact, convert it into virtual controller trackpad input, and **must not leak those same touch contacts into the game's native Windows touch/mouse input**.

The feature must **not compromise active VRR/Independent Flip or game presentation**. Touch capture that works only by degrading VRR is not an acceptable production implementation. Treat a rendering-free Win32 window as a **candidate requiring an initial proof-of-concept**, not an established guarantee.

The existing Full1902 controller owner, profile store, game detection, virtual presentation selection, Overlay Controller Capture, VIIPER bus/device owner, publisher, and physical rumble owner remain authoritative.

### Frozen product decisions

1. **Per-Steam-game only:** store one Virtual Touchpad enable flag in the Steam AppID game profile; **no device/global enable flag**, no independent Overlay enable flag, and no transient session-only override.
2. **Existing master coupling:** a game's existing `GameProfile.Enabled` master controls whether its Virtual Touchpad setting can be edited and applied. Master OFF retains the saved feature value but renders the feature inactive and not editable. Master ON permits feature-specific ON/OFF editing. No separate profile activation authority.
3. **Main App:** Steam -> selected game -> **Virtual Touchpad ON/OFF**, subject to that game's master ON. A dedicated zone placement/size editor exists **only in the Main App**, never as a persistent game overlay.
4. **Addon Quick Settings Overlay:** Profile -> **Virtual Touchpad ON/OFF**, operating on the *currently running Steam game* through the existing active-profile identity and mutation path. It edits the same persisted per-game value, not a second copy.
5. **No active game:** Overlay has no game-specific touchpad toggle to edit. Existing no-running-game Profile page behavior remains.
6. **Default OFF:** absent/missing profile, absent flag, Master OFF, invalid active context, unavailable input owner, Xbox360 presentation, and Overlay Controller Capture all resolve to disabled/neutral.
7. **Game-time invisibility:** do not draw, shade, outline, mark, flash, or display zone boundaries in-game. Temporary zone graphics appear only in the explicitly entered Main App editor. A system-level input-target HWND, if used, may be logically shown to receive pointer events but must create **no visible pixels**.
8. **Native touch isolation:** a contact beginning in an armed zone should become controller trackpad input and **must not simultaneously move/click/gesture in the game**. Touch outside zones should remain ordinary Windows/game touch, subject to the limitations of the proven input technology.
9. **Existing Overlay capture is decisive:** opening the Addon Overlay stops virtual trackpad input and neutralizes touch/press along with other game-facing controller input. Changing the toggle while Overlay is open is allowed to persist, but touch only resumes after the existing capture release, beginning with a **new** physical Touch Down.
10. **Optional vibration:** short touch/click tactile feedback may be added through the **existing single physical rumble owner**; no new writer or real Deck-localized haptic claim.
11. **No CTW integration:** the project is standalone Full1902. Do not revive historical CTW interfaces, QamHost, Steam React/QAM patching, a second routing/presentation manager, or a new HID/VIIPER device.

### Explicitly undecided or subject to proof

- Whether a completely unrendered, non-redirection, touch-target Win32 HWND reliably receives events above all target game display modes **while preserving VRR**. This is **not yet proven**.
- Whether the Claw touchscreen's digitizer reports usable touch pressure; do **not** claim physical force sensing without hardware evidence.
- Whether a synthesized physical pad click (`LPadPress`/`RPadPress`) should be bound to a user-selected gesture. Initial scope is contact/coordinates; **do not convert every tap into a click**.
- The precise location of the zone editor inside Main App. Preferred: a clearly linked editor in the Steam game's Virtual Touchpad section, backed by one **shared layout**. **Per-game placement** has not been requested; only per-game enablement is frozen.
- Support for true fullscreen-exclusive behavior if Windows does not route touch to a topmost non-rendering HWND. Do not promise it without evidence.

---

## 2. Source-of-truth hierarchy

Before implementation, read `docs/Full 1902 Implementation/README.md` and the active source documents in its precedence order. Especially:

- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/overlayui/README.md` and `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`, respecting their newer OQ4 Controller Capture correction
- `docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_A_TRIPLE_VIIPER_AND_GAME_PROFILE_POLICY_WORK_ORDER_2026-10-11.md`
- `docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_B_CONTROLLER_STEAM_PROFILE_UI_EN_KO_GUIDES_WORK_ORDER_2026-10-11.md`
- `docs/VIIPER_INTEGRATION.md` and `docs/VIIPER_IMPLEMENTATION_RULES.md` (but superseded one-target or historical HidHide statements must **not** override current Full1902 and merged triple-device policy)
- `https://github.com/onehoon/VIIPER/blob/main/FORK_ARCHITECTURE.md`
- `https://github.com/onehoon/VIIPER/blob/main/docs/libviiper/fork-api.md`

The 2026-10-11 triple-device work supersedes older descriptions of DualSense Edge as a lazy/optional experimental device: current initialization owns three **pre-created, detached** logical VIIPER devices on one bus and attaches **at most one** of Xbox360, Steam Deck, and DualSense Edge.

Main App navigation is now Device / Controller / Steam / XBOX / ClawHUD / Shortcut / How to Use / Settings. The user-facing Steam game editor belongs under **Steam**, not the old combined Profile tab. The Overlay still has its **Profile** tab for the active game.

---

## 3. Current verified Addon integration points

| Component | Existing behavior | Required narrow extension |
| --- | --- | --- |
| `Profiles/GameProfile.cs` | AppID-keyed `GameProfile`, `Enabled` master, optional `SteamVirtualController` | Add one `VirtualTouchpadEnabled` boolean, default false |
| `Profiles/ProfileDocument.cs`, `ProfileStore.cs` | Runtime-owned `profiles.json`, additive JSON schema v1, safe load/save | Additive property; no breaking schema bump or second store |
| `Profiles/GameProfileMutations.cs` | Existing `ProfileMutationGate`, profile capture, per-game setters | One setter gated by `profile.Enabled`; preserve saved flag on Master OFF |
| `Contracts/Frontend/FrontendContracts.cs` | `FrontendGameProfileSnapshot` and typed game mutations | Project flag and typed setter/result into Main App |
| `UI/Views/ProfilePage.xaml(.cs)` | Steam game detail, master ON/OFF, Steam-facing controller selector | One `Virtual Touchpad` toggle + entry to placement editor, editability follows master |
| `Contracts/Frontend/QuickSettingsContracts.cs` | Shared Profile `QuickSettingsRowId`, typed Boolean Toggle, target identity | Add one Steam-only Profile toggle row ID and section as appropriate |
| `Frontend/QuickSettingsPresentation.cs` | Pure projection; active Steam/XBOX Profile rows | Project touchpad row **only for Steam**, Master ON controls `Writable` |
| `Frontend/QuickSettingsMutationAdapter.cs` | Validates active target, row, and writable before dispatch | Reuse dispatch with `QuickSettingsProfileTargetKind.Steam`, no new transport protocol |
| `Hosting/AddonProcessHost.cs` | Captures actual active Profile target for Overlay; handles Controller Capture | Feed effective per-game touchpad policy and existing capture/lifecycle state |
| `Overlay/OverlayWindow.Profile.cs` and generic row renderer | Active-game-only Profile UI with unavailable/no-game state | Render one normal projected toggle; avoid custom Overlay-only UI |
| `VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs` | Native Deck trackpad fields currently explicitly zero | Fill valid per-side touch and coordinates after ordinary physical-state mapping |
| `VirtualOutput/Viiper/DualSenseEdgeDeviceStateMapper.cs` | DualSense contact fields currently neutral | Map supported touchscreen contacts to native Edge touch report geometry |
| `VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs` | Typed `SteamDeckDeviceState`, `DSDeviceState` ABI already exposes touch fields | **Do not change ABI** for initial touch-contact support |
| `VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` / Edge publisher | Existing single selected game-facing publication path, approximately 250 Hz Deck baseline | Merge one authoritative touch snapshot into mapped state; no second publisher |
| `Devices/MSI/Claw/MsiClawAddonPresentation.cs` | One presentation owner, Overlay neutral/capture, native attach/detach/recovery, feedback ownership | Narrow arming/neutralization gate; never duplicate presentation authority |
| `Feedback/IPhysicalRumbleSink`, `MsiClawRumbleSink.cs` | Sole physical vibration writer with lifecycle STOP requirements | Optional tiny tactile feedback routed through existing owner only |

**Important difference from the DualSense Edge virtual-controller preference:** `SetGameProfileSteamVirtualControllerAsync` is intentionally **not editable while Steam/BPM is active**, because it changes virtual device identity. **Virtual Touchpad ON/OFF is different**: it changes only touch publication and an input target window, **not controller identity or native attachment**, and must be editable while the game is running from Overlay. Do not reuse the Steam/BPM controller-identity edit lock.

The current `QuickSettingsMutationAdapter` already checks that the submitted `QuickSettingsProfileTarget` still matches the Runtime-selected active game, and returns a refreshed authoritative page. Retain that guard so an old response cannot mutate another game's profile.

---

## 4. Steam Deck capabilities versus physical Claw touchscreen

### 4.1 Native fields already present

The 76-byte canonical Steam Deck state includes:

- `LPadTouch` / `RPadTouch` — separate digital contact flags;
- `LPadX/Y` / `RPadX/Y` — separate signed 16-bit positions;
- `LPadPress` / `RPadPress` — distinct physical click/press signals;
- `LPadForce` / `RPadForce` — force values;
- separately, unrelated stick touch/force fields.

The current mapping explicitly sets these pad fields to zero, because Claw has no physical Deck trackpads. The `onehoon/VIIPER` fork exposes these fields through `SetSteamDeckDeviceState`; the canonical ABI already supports the feature.

For DualSense Edge the existing `DSDeviceState` has two `TouchX/TouchY/TouchActive` contacts on **one** emulated Sony touchpad; a separate touchpad-press button bit is supported by VIIPER. Deck's **two independent pads** and Edge's **one two-contact pad** must not be treated as physically identical.

### 4.2 Capability matrix

| Behavior | Claw touch implementation | Initial policy |
| --- | --- | --- |
| Touch down/up | Yes, via Windows pointer messages -> native Touch flag | Required |
| Continuous touch / touch-and-hold | Yes, keep Touch=1 while finger remains | Required |
| Drag / slide / swipe | Yes, update X/Y under the same contact | Required |
| Double tap | Yes, two separate down/up sequences; Steam Input interprets bindings | Preserve raw sequence, do not synthesize click |
| Long touch | Yes, sustained Touch=1 | Preserve raw sequence; **not automatically physical press** |
| Left and right simultaneously | Yes, one tracked primary contact per region on Deck | Required where digitizer supports multi-touch |
| Physical pad click/force | Not directly replicated by ordinary touchscreen coordinates | Optional gesture-to-Press later; no fabricated force |
| Two independent fingers **within one Deck pad** | Not represented by the current one-X/Y-per-side ABI | Not in scope |
| Localized Deck trackpad haptic actuator | Claw does not contain a Deck-equivalent actuator | Not in scope; optional normal motor rumble |
| Steam Input trackball / mouse / mouse region / scrolling / radial menu / touch menu / gyro activation | Steam Input interprets normal pad contact/position according to user/game bindings | Do not reimplement Steam's binding engine |

**Key distinction:** long **touch** and force/physical **click** are different. A held contact is supported, including Steam Input bindings configured for a hold; pressing the physical Deck pad is not created unless the Addon deliberately asserts `LPadPress`/`RPadPress`.

**Initial recommended click behavior:** `Press=0`, `Force=0` until a separate, proven gesture/feedback policy is selected. Never automatically interpret every tap as a click or consume double taps / long touches that Steam Input may bind itself.

Coordinates must follow the **native VIIPER report's actual units, sign, origin, center and orientation**, checked against `device/steamdeck` implementation and the existing test fixtures. Do not assume the full Int16 range or hardcode a guessed Deck pad rectangle.

### 4.3 Contact selection

- Each configured zone captures a touch **starting inside it**; retain its Windows pointer ID to keep the correct contact when two zones are active concurrently.
- One primary contact per Deck zone is mapped into that pad's coordinates. Extra contacts inside the same zone are not quietly remapped into a second Deck finger; avoid creating ambiguous clicks or stuck touch.
- A finger that begins **outside** a zone is not acquired merely because it subsequently crosses into one.
- A captured finger may move outside its original rectangle: clamp coordinates to that zone while it remains captured, then release on real UP, cancellation, capture loss, or authority retirement.
- For Edge, define one consistent placement/cropping transform from the left/right zones to its **single** pad's supported first/second contacts, and verify it against the native Edge report. Do not claim Deck-specific independent pad bindings on Edge.
- A rapid Down/Up in one controller publication tick can disappear if only a latest-state snapshot is sampled. The implementation must preserve observable transitions without adding an unrelated high-rate virtual publisher or overbuilt event machinery. Verify rapid tapping deterministically.

---

## 5. Windows touch interception — researched alternatives

### 5.1 Preferred PoC: narrow, non-rendering Win32 touch-target HWNDs

Create **only the required input regions** as native Win32 popup windows under Runtime ownership. Candidate flags:

```cpp
WS_EX_NOACTIVATE |
WS_EX_TOOLWINDOW |
WS_EX_NOREDIRECTIONBITMAP |
WS_EX_TOPMOST
```

- Windows documents `WS_EX_NOREDIRECTIONBITMAP` for windows with **no visible content** or externally provided visuals. That does **not** prove this exact style reliably receives touch above games or preserves VRR.
- Do **not** use ClawHUD's `WS_EX_TRANSPARENT` or `WM_NCHITTEST -> HTTRANSPARENT`: its HUD must pass pointer input **through**, whereas the virtual pad must capture touches.
- The intended rectangle should hit-test as a normal client input area (`HTCLIENT`) for the supported pointer types; preserve no-activate behavior with `WM_POINTERACTIVATE -> PA_NOACTIVATE` and `WM_MOUSEACTIVATE -> MA_NOACTIVATE` as applicable.
- Handle `WM_POINTERDOWN`, `WM_POINTERUPDATE`, `WM_POINTERUP`, `WM_POINTERCAPTURECHANGED` / cancellation; obtain touch type, ID, and coordinates using `GetPointerType` / `GetPointerTouchInfo` or `GetPointerInfo`.
- The Windows pointer-message documentation says pointer down implicitly captures the pointer to that window until release. Do not count on paired UP messages following cancellation/capture loss.
- Consume **every handled touch message in the captured sequence** rather than selectively calling `DefWindowProc` for portions of that same sequence: Microsoft documents undefined legacy gesture/mouse-promotion behavior for selective handling.
- No XAML/WinUI overlay during gameplay; **no D3D11 texture, Direct2D, DirectComposition visual, swapchain, presentation surface, redraw timer, GPU Present, or hidden full-screen bitmap**.
- Input-target windows may have to be **visible as HWNDs to receive hit-tested touch**, but are **pixel-invisible**. A hidden HWND is not an equivalent hit-test target. Confirm actual paintlessness and touch routing in PoC.
- This is **not** a transparent full-screen overlay. Only left/right configured rectangles are input targets; outside touch must retain normal handling.
- Default physical mouse/pointer clicks on these windows require explicit study. Avoid unintentionally swallowing ordinary mouse input if the user uses the touchscreen alongside a mouse.
- Use existing physical-pixel/DPI/monitor geometries; the intended baseline is the Claw's 1920x1200 at 150% scaling. Handle monitor, resolution, rotation, DPI, and placement changes on the same supported interactive desktop.
- A foreground game in fullscreen-exclusive or special FSE mode may bypass the intended window ordering. This must be a PoC compatibility finding, not silently described as supported.

An input-only window is still a **top-level window**. The claim that it cannot affect DWM/VRR solely because it submits no graphics is **not proven**.

### 5.2 Windows global pointer rerouting: not initial scope

`RegisterPointerInputTarget(hwnd, PT_TOUCH)` is a documented global pointer redirection API, not a per-rectangle hook. Microsoft documents:

- requires **UIAccess privilege**; an ordinary elevated process is not sufficient;
- only one global target per pointer type/desktop;
- *all* touches of that type are redirected;
- ordinary touch outside the two pad zones would need a separate safe reinjection/forwarding strategy.

Therefore do **not** add UIAccess manifests, signatures, an assistive-tech privilege installation path, or global input reinjection merely to support the initial feature. Revisit only if proven non-rendering HWND interception cannot meet the product's fundamental VRR/touch-isolation goals and the user explicitly approves a new architecture.

### 5.3 Raw Input monitoring is not equivalent to suppression

`RegisterRawInputDevices` is an input delivery/subscription API; it does not provide a generic Windows equivalent of Linux `EVIOCGRAB` that consumes ordinary touchscreen input for other programs. HID collection availability/device mapping on the Claw would also have to be verified. Raw Input alone is **not** accepted as a solution to the no-leak-to-game contract.

### 5.4 Do not disable the physical touchscreen to acquire it

Disabling the touchscreen's PnP/HID function globally can suppress game touch but also prevents the Addon from receiving its input. It would defeat the feature unless a **separate, proven** acquisition mechanism exists. No automatic PnP disable/enable, HidHide expansion to the touchscreen, or new kernel filter driver in the initial proposal. If later considered, it must be opt-in, reversible, explained to the user, and must not strand touch on crash/restart.

### 5.5 Windows pointer and gesture hygiene

Keep the preferred implementation on the documented Windows pointer path. Suppress Windows touch contact visual feedback through documented APIs **only where applicable and verified**; do not assume that suppressing a visual effect suppresses input or compositor effects. Do not inject synthetic mouse clicks or re-dispatch touch to the game for captured contacts.

If a pointer/capture behavior cannot be proven, the feature should refuse to arm rather than claim touch suppression while leaking unwanted input.

---

## 6. ClawHUD VRR engineering evidence and implications

The actual ClawHUD project is `onehoon/ClawHUD`:

- `src/ClawHUD/HudPresentationContract.h`
- `src/ClawHUD/HudPresentation.cpp`
- `docs/HUD_PRESENTATION_VRR_DECISION_HISTORY.md`
- `docs/VRR_HARDWARE_VALIDATION_RESULTS.md`
- `docs/PHASE0-VRR-POC.md`

**Current ClawHUD production contract** (verified code, 2026-10-12):

```cpp
WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW |
WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOPMOST
```

ClawHUD renders D3D11 displayable BGRA textures via Direct2D, DirectComposition, and Windows Presentation API (`IPresentationManager::Present`). It requires Independent Flip capability and uses `WM_NCHITTEST -> HTTRANSPARENT` to **pass game input through**.

**Historical finding:** ClawHUD previously used `WS_EX_NOREDIRECTIONBITMAP` but switched to a layered-window host because reliable cross-process **click-through** did not work on the tested MSI Claw. A working layered HUD then retained Independent Flip in documented game captures. This is evidence that a suitable top-level HUD **can coexist** with VRR; it is **not evidence** that a new touch-capturing HWND automatically preserves VRR.

**Critical diagnostic finding:** an old synthetic HUD test rendering every 100 ms produced a fixed-120-Hz observation even when normal production HUD and a more representative 500-ms diagnostic did not. Merely retaining a particular PresentMode is **not proof of active physical VRR**. Avoid any per-touch redraw/present path.

### Nonnegotiable VRR evidence

Compare, in the **same** game, resolution, driver, Windows refresh/VRR settings, overlay environment, and presentation path:

1. **OFF:** Virtual Touchpad not armed (baseline).
2. **ON idle:** Invisible HWND(s) present but no finger touching.
3. **ON active:** simultaneous zones, touch down/move/up, rapid taps, long holds.
4. **Capture transition:** Addon Overlay open/close while game continues.

Observe **game PresentMon PresentMode**, independent-flip/composed transitions, swapchain continuity, displayed frame pacing, and **variable versus fixed physical refresh indications** by the existing ClawHUD-informed Special K and D3DKMT cadence diagnostics where available.

ClawHUD's logged hardware evidence distinguishes VRR ON (variable refresh) from VRR OFF (fixed ~120Hz), **even while PresentMon remained 100% Hardware Composed: Independent Flip in both**. This means the required gate is **no regressions to actual variable-refresh behavior**, not just a green Independent Flip label.

All real-device observations are performed by the **user after merge**, not by local Codex; provide a repeatable diagnostic/runbook but never make that user-role measurement a code PR blocker.

---

## 7. Linux/HHC/Steam references: what transfers and what does not

| Reference | Source-backed behavior | Relevant lesson |
| --- | --- | --- |
| Handheld Companion (`OverlayTrackpad.xaml`, `.xaml.cs`, `OverlayWindow.cs`, `DS4Touch.cs`) | Two on-screen WPF touch regions, `TouchDown/Move/Up`, coords mapped to synthetic DS4 contacts; WPF transparent topmost window | Reference coordinate handling, left/right UI editing, simulated touch; **not a VRR-safe display design** |
| hhd-dev/hhd (`src/hhd/plugins/overlay/controllers.py`) | Linux evdev touchscreen input and `dev.grab()` for touchscreen-disable option | Input ownership inspiration only; Windows has no direct drop-in `EVIOCGRAB` |
| hhd-dev/hhd (`src/hhd/plugins/touchpad.yml`) | Emulates a controller touchpad from a **physical** touchpad, short/long contact options and desktop-disable behavior | Steam-facing behavior reference, **not evidence of touchscreen-zones-on-Windows** |
| Bazzite / InputPlumber ecosystem | Linux evdev/input routing and virtual devices | Useful distinction between consuming a physical event and merely observing it; Linux implementation is not Windows API proof |
| Steamworks Input Source Modes / Activators | Steam Input interprets Touch, Click, mouse-region, scrolling, menus, double/long activation according to bindings | Preserve raw contact events; do not independently create Steam Input gestures unless explicitly requested |

Steam Deck is physically **two pads**, while a DualSense Edge is **one central pad with two touch contacts**. Per-game Steam Input mappings determine actual game semantics and can display Steam-owned UI; the Addon only promises **no Addon zone art in-game**.

---

## 8. Persistence, master ON/OFF, Main App, and Overlay

### 8.1 One authoritative persisted enable bit per Steam AppID

Preferred additive model:

```csharp
public sealed record GameProfile
{
    public bool Enabled { get; init; } // existing master
    public bool VirtualTouchpadEnabled { get; init; } // new; defaults false
    // Existing Display, Performance, SteamVirtualController, etc.
}
```

Effective policy sketch (not a substitute for existing owner/reconcile checks):

```csharp
bool enabled =
    activeTarget.Kind == ActiveProfileTargetKind.Steam &&
    activeTarget.SteamAppId != 0 &&
    gameProfile is { Enabled: true, VirtualTouchpadEnabled: true } &&
    activePresentation is SteamDeck or DualSenseEdge &&
    !overlayCaptureActive &&
    physicalOwnershipHealthy &&
    inputTargetReady &&
    !suspended;
```

**Do not create a second master state.** A game's Master OFF preserves the saved `VirtualTouchpadEnabled` bit but makes it ineffective and uneditable, matching the existing per-game Steam virtual-controller override policy.

The enum/presentation selection has an unrelated **existing global default**. That global Steam Deck/Edge controller choice remains untouched and does **not** imply a global Virtual Touchpad feature toggle.

Use the existing `GameProfileMutations` and `ProfileMutationGate` to write `profiles.json`. Default false; no second JSON/registry cache, database, compatibility migration, or schema-v2 bump for a compatible additive property. Existing invalid/malformed/unreadable file protection remains intact. The only permitted mutation requires the profile to exist, Master ON, and persistence writable.

### 8.2 Main App Steam game detail

Suggested layout:

```text
Steam -> Game Detail
  [Profile master ON/OFF]          (existing)
  [Virtual Controller selector]   (existing)
  [Virtual Touchpad    ON/OFF]     (new)
  [Edit Touchpad Regions...]       (Main App only)
  [TDP / other profile settings]   (existing)
```

- Master OFF -> show stored feature value but disable control and effective input.
- Master ON -> permit feature toggle (including while a game is running; no device switch).
- Profile save and mutation result/snapshot are authoritative; failed saves restore last confirmed UI state.
- Region editor can draw two adjustable rectangles **only while explicitly editing in Main App**. This display may use WinUI3 visuals and need not itself be VRR-safe; it is not used for gameplay.
- Preferred layout persistence is **one shared physical-device/display layout** (normalized left/right rectangles) with a separate setting from the per-game enable bit. This is **a design recommendation, not a user-confirmed per-game-vs-shared placement choice**. It is not a global ON/OFF. Use existing settings storage if adopted rather than a second configuration authority.
- Validate nonzero size, bounds, overlap policy, orientation/DPI, and an obvious Reset to Default. Do not add an elaborate multi-monitor/RDP system unsupported by the product.

### 8.3 Addon Overlay Profile toggle

Reuse these current structures:

```text
Runtime active Steam target -> QuickSettingsProfileTarget.ForSteam(AppID)
  -> existing CaptureQuickSettingsPageAsync(Profile)
  -> QuickSettingsPresentation.BuildProfile
  -> new ProfileVirtualTouchpadEnabled Boolean Toggle row
  -> Overlay generic row renderer
  -> existing QuickSettingsMutationAdapter
  -> GameProfileMutations.SetVirtualTouchpadEnabled
  -> refreshed authoritative Profile page
```

- Only Steam; no Xbox Profile toggle or device-global equivalent.
- Use the current **active** AppID; stale target, no game, Master OFF, unwritable profile or shutdown -> reject/disable, return authoritative page.
- Overlay toggle is **allowed while Steam/BPM/game is running**, unlike controller-identity selection.
- A saved change during Controller Capture **does not unpause the game**. Touch target activation occurs **only after** existing Overlay capture release and existing held-input release gate.
- Closing Overlay after ON must **not resurrect touches started while Overlay was visible**. Require fresh Touch Down; clear current pointer IDs and native pad state.
- Overlay itself should never render the invisible touch regions, size/position editor, live touch cursor, or any other gameplay overlay.
- Existing Overlay Quick Settings section ordering and generic controls remain authoritative; keep this addition small.

### 8.4 Game/Steam lifecycle

| Scenario | Feature status |
| --- | --- |
| Steam AppID A, Master ON, saved touchpad ON, Steam Deck active | Armed if input/VRR prerequisites proven |
| Steam AppID A, Master ON, saved ON, DualSense Edge active | Armed with Edge mapping if supported |
| Steam AppID A, Master OFF, saved ON | OFF; saved bit retained |
| Steam AppID B, touchpad OFF | OFF without affecting AppID A |
| Steam game ends; BPM remains with global Deck/Edge | OFF (no active game-specific permission) |
| BPM with no running game | OFF, regardless of global controller selection |
| Windows Xbox360 | OFF |
| Addon Overlay open | OFF + native Neutral, regardless of saved ON |
| Overlay changed to ON then closed | Arm only on valid release and new touch |
| Main App editing zone layout | Editor may show zones; **not a game-time touch target** |
| No valid Steam AppID / stale active target / persistence error | OFF or no mutation; no guessed game |
| Center M Enabled / no owned PID1902 / presentation lost | OFF; no standalone input path |

---

## 9. Runtime ownership and data path

```text
Windows touchscreen digitizer
        |
        v
[Runtime-owned, pixel-invisible left/right Win32 input regions]
        | WM_POINTER touch / contact ID / coordinates
        v
[small authoritative touch snapshot]
        | neutralized by existing Overlay Capture / lifecycle gate
        v
[one active virtual controller publisher]
        | merge with canonical MSI physical ControllerState
        +--> SteamDeckDeviceState: L/R Touch + X/Y + optional Press
        +--> DSDeviceState: active contacts + X/Y + optional touchpad press
        |
        v
[existing one VIIPER owner, one bus, exactly one attached presentation]
        |
        v
Steam / game's selected Steam Input mapping

Optional feedback request
        |
        v
[existing single physical rumble owner -> MSI Claw motors]
```

The Main App and Quick Settings Overlay remain frontends; they never become native device owners. The transient input-target HWNDs may be hosted by the Runtime's existing interactive message-loop facilities or a **narrow Runtime-owned window component**, whichever is simplest in current code. **No independent process, controller authority, game detector, bus, writer, or duplicated publisher**.

The input-target window lifetime is subordinate to the Runtime's current effective per-game + presentation + Overlay capture decision. It must not own PID1901/1902 restoration, HidHide, VIIPER attach/detach, or physical controller cleanup.

Avoid high-rate pointer-event IPC through the existing Overlay Quick Settings transport. The Overlay's typed toggle is a low-rate configuration command. The touchscreen's moving contact data remains entirely in the Runtime/local publisher path.

A per-zone pointer snapshot is enough for the target ABI. Do not invent generalized event brokers, locks/epochs/barriers, new submanagers, or unrequested future multi-touch abstractions to defend against microscopic theoretical scheduling interleavings.

---

## 10. Lifecycle, fail-close, and no-stuck-touch

Existing Full1902 owner decisions and the Overlay capture gate must be reused for:

- Steam game start/end and AppID change, including game change while BPM stays open;
- automatic Deck <-> Edge presentation transition, after the existing selected publisher has been safely stopped and before any new device receives contact;
- Overlay open, close, failed show, crash, capture rollback and held-control release;
- touch pointer UP, cancel, capture changed/lost, region edited or moved, display/DPI/rotation changed;
- suspension, hibernate, resume, controller physical loss/PnP re-enumeration, VIIPER attach/retire or publisher failure;
- controlled restart, crash, shutdown and ordinary session-end teardown.

### Required behavior

1. Before disarming/moving/destroying a window: clear owned pointer IDs, `Touch=0`, `Press=0`, and any optional per-contact haptic request. If the active publisher is available, emit/publish Neutral through its **existing** policy; never introduce a separate writer that can outlive a retired native device.
2. On reacquisition/return from Overlay or Resume: **do not restore** an old contact from a previous epoch/session; accept only a **new Down** after the gate reopens. This is a simple input admission rule, **not** an invitation to create new epoch/state-machine infrastructure.
3. If the window cannot be created, placed, hit-tested, or guaranteed as an input sink in an environment the implementation detects: disable the feature and report accurate availability. Do not emit controller touches based on unreliable observation while the original game's Windows input still leaks.
4. On malformed/unsafe profile store, do not rewrite an untrusted `profiles.json`; existing profile store behavior wins.
5. Native attachment outcome unknown, PID1902 ownership failure, and callback/teardown uncertainty retain their existing Full1902 fail-close rules. Virtual Touchpad must never try an alternative native attach or revive a retired presentation.
6. Don't intercept all physical touch outside configured zones during normal operation. Avoid a full-screen invisible input blanket.
7. A crash of the Runtime must not leave a persistent OS-level global touch redirect or globally disabled touchscreen; this is another reason to avoid UIAccess rerouting/PnP disable in the initial design.

Prioritize real handheld lifecycle failure paths. **Do not** use hypothetical instruction-level interleavings as a reason to add another authority, synchronization framework, or generalized recovery engine.

---

## 11. Optional haptic feedback

The Steam Deck has local trackpad haptic behavior; the Claw touchscreen has **no equivalent localized Deck trackpad actuator**. A short **ordinary two-motor controller rumble** is a possible approximate feedback and must be described honestly.

| Event | Recommended motor action |
| --- | --- |
| Contact Down | Optional very short/weak pulse (initial default OFF) |
| Move/drag | None; never rumble on each pointer update |
| Optional synthetic press | Optional short click pulse |
| Contact Up | None |
| Overlay capture / suspend / game/presentation change | Cancel and ensure stop through existing rumble owner |

- Reuse `IPhysicalRumbleSink` / `MsiClawRumbleSink` ownership and existing vibration strength policy.
- Do **not** instantiate a new physical sink/endpoint resolver or writer from the touchscreen event callback.
- Do not race a feedback pulse against game-owned rumble or let a pulse override a mandatory STOP. Only add a small existing-owner mediation path if the current code proves it is needed; otherwise defer haptics.
- No high-rate haptic stream, Deck LRA simulation, adaptive trigger, or fabricated force signal.
- Touchpad usability and VRR PoC **must not depend on haptic implementation**.

---

## 12. Phased implementation strategy (not yet individual PR work orders)

### Phase 0 — Windows interception and VRR viability PoC

**Only goal:** determine whether an invisible, non-rendering region can (a) get a touchscreen Down/Move/Up, (b) prevent the original game from receiving that touch, (c) preserve game focus, (d) preserve active VRR/Independent Flip in real use.

Minimal code: Runtime-compatible Win32 class/window, configurable fixed test rectangles, pointer ID/coordinate debug, correct handled-message behavior, no native input publication required. Do not build the full Main App layout editor, profile mutation, Edge mapping, or haptics yet.

Deterministic local validation: compile, window flags/hit-test/coordinate transformations/neutral lifecycle tests where practical, no desktop-wide touch registration; keep instrumentation lean and bounded.

A user can separately compare baseline/armed-idle/armed-touch on physical Claw using the existing ClawHUD-informed VRR diagnostic method **after the PoC PR is merged**. Report the measured conditions/results. Hardware observations are **not** a Codex or PR-review blocker.

**Decision:** if true game touch isolation and acceptable VRR cannot coexist, stop production feature work and document the unsupported condition; do not silently ship leaking touches or claim universal compatibility. A wider-scope fallback requires separate approval.

### Phase 1 — Per-game policy and native Deck touch

Introduce one additive flag in `GameProfile` (Master-bound), Main App Steam toggle, Runtime/Overlay shared profile mutation and read projection; single publisher merges Deck contact/coords. Existing Overlay capture and native teardown semantics remain in force. Focused automatic tests for master OFF, game switch, Overlay mutation and neutralization.

### Phase 2 — Full edit UX, DualSense Edge and refinement

Add Main App-only draggable/resizable zone layout editor, normalized rectangles, monitor/DPI placement, optional click mapping (only if explicitly approved), Edge single-pad/two-contact mapping, and focused automated tests. Reuse the same per-game bool, no separate Edge enable flag.

### Phase 3 — Optional tactile feedback

After the input and native mapping path has worked, consider ordinary Claw vibration through the existing single physical rumble owner, with bounded pulse and mandatory-stop precedence.

These are engineering phases, **not commitments to a fixed PR count**. Local Codex should receive separate scoped work orders only after architecture/PoC review.

---

## 13. Automatic test matrix versus user hardware verification

### Local Codex / automated tests (merge-eligible code evidence)

- Existing profile absent/disabled/enabled, saved bool preservation, default OFF, additive JSON roundtrip and malformed/unwritable store.
- Master OFF rejects touchpad mutation in both Main App and Overlay, preserving saved bit; Master ON permits only the selected game.
- Overlay active-game target mismatch is rejected, late mutation response cannot apply to a new AppID, no game/XBOX does not expose the Steam toggle.
- Overlay capture always neutralizes contact even if the persisted toggle switches ON while capture is active.
- No stale Down replay after Overlay closes, suspend/resume, presentation replacement or input-target release.
- Pointer Down/Update/Up/cancel/capture-loss; one finger per zone; simultaneous left/right; extra contact policy; clamp/normalization/DPI math; short tap does not disappear between publisher samples.
- Correct Deck/Edge fields and force/press neutrality; current USB device ownership and native ABI unchanged.
- Existing VIIPER single attached presentation, owner recovery, callback/rumble STOP and teardown automated regressions.
- No extra high-rate graphics or Overlay transport path, and no accidental permanent touchscreen/PnP modification.
- Build/test/diff check as supported by the repo. Describe any limitations truthfully.

### User's post-merge physical validation (not PR blockers)

- The MSI Claw actually receives touchscreen input in both configured invisible windows over a running game.
- Touching a region does **not** also click/drag/gesture in that game, while touching outside behaves as Windows touch.
- Window does not steal game focus, create visible pixels, show desktop/window switcher artifacts or unintended cursor visual feedback.
- Borderless/Steam FSE/fullscreen modes tested separately with results recorded rather than claimed universal.
- Baseline/ON-idle/ON-active VRR and latency/pacing comparison using ClawHUD diagnostic lessons, variable versus fixed refresh and no unexpected XeFG effects.
- Native Deck left/right touch and Edge mapping in actual Steam Input, game-specific profile toggles, Overlay capture and optional rumble behavior.
- Suspend/resume, Restart/Crash/Shutdown, physical loss/PnP recovery, presentation transitions and game exit.

The user conducts all physical tests **after merge**. A PR with correct code, appropriate automated tests, and no demonstrated production blocker must not be held waiting for real hardware testing.

---

## 14. Explicit non-goals

- Global Virtual Touchpad ON/OFF; independent-of-master profile enable; session-only Overlay settings.
- Permanent on-screen zone art, full-screen transparent GPU overlay, WPF HHC overlay port, or GPU Present on finger movement.
- Steam QAM tab injection or replacement; Steam Input binding engine rewrite.
- New HID/PnP kernel driver, touchscreen disable, UIAccess global rerouting or mouse-event reinjection in the initial architecture.
- New VIIPER ABIs/devices, second bus/server/publisher, new controller manager or persistent input router.
- New Xbox game profile option or Xbox360 touchpad emulation.
- Claims of real Steam Deck pressure sensing/force haptics or per-Deck-pad multi-contact coordinates.
- Windows multi-user/RDP/Fast User Switching support, speculative state/lock/epoch architectures, hardware validation as a PR blocker.

---

## 15. References (verified source links)

### Our repositories

- [Full1902 document authority](Full%201902%20Implementation/README.md)
- [Full1902 owner architecture](Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md)
- [Current Overlay policy and historical corrections](overlayui/README.md)
- [GameProfile.cs](../src/SteamInputAddonforClaw/Profiles/GameProfile.cs)
- [GameProfileMutations.cs](../src/SteamInputAddonforClaw/Profiles/GameProfileMutations.cs)
- [QuickSettingsPresentation.cs](../src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs)
- [QuickSettingsMutationAdapter.cs](../src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs)
- [SteamDeckDeviceStateMapper.cs](../src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs)
- [CanonicalViiperNativeTypes.cs](../src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs)
- [Current triple-VIIPER/per-game controller design](work-order/STEAM_VIRTUAL_CONTROLLER_PR_A_TRIPLE_VIIPER_AND_GAME_PROFILE_POLICY_WORK_ORDER_2026-10-11.md)
- [Current Steam profile Main App UI design](work-order/STEAM_VIRTUAL_CONTROLLER_PR_B_CONTROLLER_STEAM_PROFILE_UI_EN_KO_GUIDES_WORK_ORDER_2026-10-11.md)
- [VIIPER fork Steam Deck typed state](https://github.com/onehoon/VIIPER/blob/main/lib/viiper/steamdeck.go)
- [ClawHUD presentation contract](https://github.com/onehoon/ClawHUD/blob/main/src/ClawHUD/HudPresentationContract.h)
- [ClawHUD display/VRR decision history](https://github.com/onehoon/ClawHUD/blob/main/docs/HUD_PRESENTATION_VRR_DECISION_HISTORY.md)
- [ClawHUD hardware validation record](https://github.com/onehoon/ClawHUD/blob/main/docs/VRR_HARDWARE_VALIDATION_RESULTS.md)
- [ClawHUD original VRR PoC runbook](https://github.com/onehoon/ClawHUD/blob/main/docs/PHASE0-VRR-POC.md)

### Official Windows / Steam references

- [Microsoft — Extended Window Styles (`WS_EX_NOREDIRECTIONBITMAP`)](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [Microsoft — Pointer Messages: capture/cancel/DPI](https://learn.microsoft.com/en-us/windows/win32/inputmsg/messages)
- [Microsoft — WM_POINTERDOWN](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerdown)
- [Microsoft — WM_POINTERUPDATE](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerupdate)
- [Microsoft — WM_POINTERACTIVATE](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointeractivate)
- [Microsoft — RegisterPointerInputTarget / UIAccess](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerpointerinputtarget)
- [Microsoft — Raw Input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)
- [Microsoft — DXGI Flip / Independent Flip / MPO](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model)
- [Steamworks — Input Source Modes](https://partner.steamgames.com/doc/features/steam_controller/input_source_modes)
- [Steamworks — Activators (double and long press)](https://partner.steamgames.com/doc/features/steam_controller/activators)
- [Steamworks — Steam Deck FAQ (touchscreen versus touchpad input)](https://partner.steamgames.com/doc/steamhardware/steamdeck/faq)

### Community implementation comparisons

- [Handheld Companion — OverlayTrackpad.xaml](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Views/Windows/OverlayTrackpad.xaml)
- [Handheld Companion — OverlayTrackpad.xaml.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Views/Windows/OverlayTrackpad.xaml.cs)
- [Handheld Companion — OverlayWindow.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Views/Classes/OverlayWindow.cs)
- [Handheld Companion — DS4Touch.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Misc/DS4Touch.cs)
- [HHD — Linux touchscreen grab](https://github.com/hhd-dev/hhd/blob/master/src/hhd/plugins/overlay/controllers.py)
- [HHD — touchpad-emulation settings](https://github.com/hhd-dev/hhd/blob/master/src/hhd/plugins/touchpad.yml)
- [InputPlumber](https://github.com/ShadowBlip/InputPlumber)

---

## 16. Decision log

| Topic | Decision | Status |
| --- | --- | --- |
| Per-Steam-AppID enable | Required; persisted in `profiles.json` | **Frozen** |
| Game Profile Master | Existing `GameProfile.Enabled` gates edit + apply; saved value retained while OFF | **Frozen** |
| Global enable toggle | **Do not add** | **Frozen** |
| Overlay toggle | Same per-game persisted setting, current Steam game only | **Frozen** |
| Overlay capture | No touch/game controller publication during capture; new Down after release | **Frozen** |
| Game-time zone visuals | None; editor-only visualization | **Frozen** |
| Game touch isolation | Required for in-zone touches; no game mouse/touch leak | **Frozen acceptance** |
| Windows input mechanism | Narrow non-rendering Win32 target HWND first | **PoC hypothesis** |
| Active VRR and Independent Flip | Must not regress; use ClawHUD validation evidence | **Frozen acceptance, not yet verified for this feature** |
| Native mappings | Deck L/R, Edge one pad; reuse VIIPER ABI and owner | **Design direction** |
| Short tap, drag, double tap, long touch | Preserve actual contact sequences; Steam Input decides bindings | **Design direction** |
| Physical click, pressure | No automatic click, force neutral initially | **Initial design** |
| Placement | Main App visual editor; suggested shared rectangle geometry | **Editor requirement frozen; shared-vs-per-game layout not explicitly decided** |
| Rumble | Optional via existing physical writer; initially OFF | **Future optional** |
| Global touch rerouting / disable | Not initial scope | **Deferred** |
| CTW / new authority / hardware blocking PR | Prohibited | **Frozen** |
