# Work Order — Controller for Steam: Main App / Overlay Parity and Global Gyroscope Toggle

**Date:** 2026-10-14  
**Target:** one cohesive feature PR on onehoon/SteamAddonforClaw, standalone Full PID1902  
**Executor:** local Codex (implementation, build, automated tests); user performs physical-device validation after merge  
**Location:** docs/work-order  
**Status:** implementation requested; this document is not an implementation commit

## 1. Goal and fixed product decisions

Make the Main App Controller page and the Overlay Controller tab present the same compact **Controller for Steam** setting group:

~~~text
Controller for Steam
    Controller       [Steam Deck / DualSense Edge]
    Gyroscope        [On / Off]
~~~

- Controller is the existing **global preference for the next/current Steam-facing presentation**, not a general Windows/Xbox360 virtual-device selector.
- Gyroscope is one **global**, persisted motion-source enable setting. No per-game gyro override, Steam Input profile editing, gyro remapping or right-stick emulation.
- Remove the exact user-facing suffix **(Default)** from **Steam Deck (Default)** everywhere it is used as this global-controller option. The visible option becomes **Steam Deck**.
- Keep the *actual persisted/default choice* Steam Deck: existing enum value, AppSettings default, SettingsStore fallback, and game-profile inheritance remain unchanged.
- Keep the Steam game-profile selector's **Use default** choice: it is the meaningful inherit-global option and is not the label being removed.
- Steam Input continues to own in-game gyro activation/behavior/mapping; the Addon toggle only gates physical motion collection / virtual IMU publication.
- Both Main UI and Overlay use identical values and Runtime policy. Neither frontend owns a separate setting, alternate presentation authority, nor a duplicate motion manager.

## 2. Verified source baseline and authority

Read before implementation:

- docs/Full 1902 Implementation/README.md — authority order and standalone lifetime rules.
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md.
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md.
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md.
- docs/gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md.
- docs/gyro/FULL1902_STEAMDECK_GYRO_CHOPPINESS_INVESTIGATION_AND_DEFERRED_HANDOFF_2026-10-11.md.
- docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_A_TRIPLE_VIIPER_AND_GAME_PROFILE_POLICY_WORK_ORDER_2026-10-11.md.
- docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_B_CONTROLLER_STEAM_PROFILE_UI_EN_KO_GUIDES_WORK_ORDER_2026-10-11.md.

Verified current code paths:

| Responsibility | Actual source / finding |
| --- | --- |
| Main Controller UI | src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml: SteamVirtualControllerCard is a SettingsCard with one ComboBox. Its Steam Deck option currently says Steam Deck (Default). |
| Main selector admission | ControllerPage.xaml.cs: CanEditSteamVirtualController requires steamActive == false and no mutation in flight; unknown steam state locks the selector. |
| Runtime selector admission | Frontend/InProcessAddonFrontendControl.cs: SetSteamVirtualControllerAsync and SetGameProfileSteamVirtualControllerAsync both reject when RunningAppId != 0 or BigPictureActive. Unknown/capture failure also rejects. |
| Saved preference | Settings/AppSettings.cs initializes SteamVirtualController to SteamDeck; SettingsStore.cs independently defaults absent/invalid preference to SteamDeck; StartupSettingsCoordinator.ChangeSteamVirtualController persists it. |
| Presentation selection | Devices/MSI/Claw/MsiClawAddonPresentation.cs owns the current ActivePresentation (Xbox360, SteamDeck, DualSenseEdge); Steam AppID-enabled profile preference can override global selection automatically. |
| Motion lifetime | Hosting/AddonProcessHost.cs ShouldRunMotionReaders / ReconcileMotionReadersAsync / StartMotionSourceAsync / StopMotionSourceAsync. Existing StopMotionSourceAsync invalidates and cancels before stopping readers. |
| Overlay Controller UI | src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs currently builds M1/M2, Joystick LED and Vibration Strength section cards. It does NOT offer global Steam controller selection. |
| Overlay settings transport | FrontendTransport/OverlayWire.cs (protocol v19); NamedPipeOverlayClient.cs / NamedPipeOverlayServer.cs; Lifecycle/OverlayProcessController.cs; Hosting/AddonProcessHost.cs BindProductionControlsAuthority / CaptureOverlayFrontendSettingsAsync / MutateOverlayFrontendSettingsAsync; SteamInputAddonforClaw.Overlay/App.xaml.cs and OverlayWindow.Controller.cs. |
| Main localization/tests | UI/Strings/{en-US,ko-KR}/Resources.resw contains Controller_SteamDeckOption.Content = Steam Deck (Default); UiTests/SteamVirtualControllerUiTests.cs asserts that literal. |

The gyro choppiness investigation remains unresolved; this work order does not claim the new toggle fixes Steam Deck gyro smoothness or proves unneeded gyro acquisition causes controller latency.

## 3. Exact editing and display policy

Use the **existing authoritative Steam session snapshot** (actual RunningAppId and BigPictureActive), not XBOX profile/activity, the mere existence of Steam.exe, Overlay visibility, or an inferred virtual-device identity, to decide whether the user may *manually* change Controller.

| Actual context | Controller selection | Gyroscope toggle |
| --- | --- | --- |
| Normal Windows; no Steam game and no BPM | Editable (saved Steam preference) | Editable |
| XBOX/Game Pass game only; no Steam game and no BPM | Editable (saved Steam preference; current Xbox360 is unaffected) | Editable |
| BPM alone, even RunningAppId == 0 | Locked; show the actual Steam-facing presentation | Editable |
| Steam game with/without BPM | Locked; show the actual Steam-facing presentation | Editable |
| XBOX game while BPM/Steam game is also active | Locked, because Steam session has priority for this admission check | Editable |
| Steam session cannot be captured or Runtime state is uncertain | Locked (fail closed) | Editable if settings Runtime is available and not shutting down |

Additional contract:

1. While editable (no Steam/BPM), show the **saved global SteamDeck/DualSenseEdge preference**, never imply the currently attached Xbox360 device is a selectable Steam-facing option.
2. While locked (Steam game/BPM), show the **actual Runtime-owned ActivePresentation** if SteamDeck or DualSenseEdge. Do NOT simply echo Settings.SteamVirtualController: an enabled AppID profile may select a different controller.
3. If Steam is active but there is no proven applicable active SteamDeck/Edge presentation (startup/recovery/failed attach), show a read-only **Unavailable** or equivalent honest unknown indication. Do not fabricate the global choice as the active physical/virtual state.
4. The Overlay may pause input publication while visible. Do not equate the temporary Overlay capture pause with a detached controller; use the Runtime presentation owner's existing active typed-device identity where valid.
5. The existing game-profile automatic switch on app launch/exit remains allowed; only **user-requested mutation** is locked. Steam profile selection and its Use default inheritance must continue to work.
6. Main and Overlay must refresh from Runtime on open, on relevant status/session changes, and after mutations. Never let a stale Main/Overlay checkbox or pending edit overwrite a newer authoritative setting. Existing mutation-in-flight guards and returned readback are sufficient; avoid new global epochs/state managers.
7. Runtime admission guard remains decisive, even if the UI was stale and offered a click just before Steam/BPM became active.
8. Xbox360 has no IMU fields. Turning the global Gyroscope option on/off during Xbox-only play saves future Steam behavior and must not disrupt Xbox360 input.

Illustrative *shared Runtime-owned read-only projection* (adapt names to established conventions; do not create a new service or state authority):

~~~csharp
public sealed record FrontendControllerForSteamSnapshot(
    SteamVirtualControllerPreference SavedPreference,
    SteamVirtualControllerPreference? ActiveSteamPreference,
    bool CanChangeController,
    bool GyroscopeEnabled,
    bool Available);
~~~

The display value is ActiveSteamPreference while Steam/BPM is active and valid, otherwise SavedPreference while idle. When active selection is unavailable, render Unavailable and lock. The Runtime derives all policy from its current Steam snapshot, existing active presentation and one saved AppSettings record; the frontend never recomputes its own conflicting rule. An unknown session state must fail closed for controller mutation.

## 4. UI composition — two native surfaces, identical contract

### 4.1 Main App Controller page

Keep the existing top-of-page SteamVirtualControllerCard region, changing the title to **Controller for Steam** and presenting **two rows**:

~~~text
[ Controller for Steam                         ]
[   Controller          Steam Deck          v  ]
[   Gyroscope           On / Off               ]
~~~

- Use existing WinUI3 CommunityToolkit SettingsCard/nested settings presentation patterns; avoid a new custom reusable abstraction. **Do not introduce an expander for just two rows.**
- Keep existing controller-choice ComboBox and selection wiring, but change the row caption to Controller and add the single Gyroscope toggle.
- Existing explanatory lock text / info bar can be shown only when useful, without dominating the compact card.
- While Steam is active, show actual applicable active type; when idle, show saved preference. Handle Unavailable without silently selecting SteamDeck.
- Keep the remainder of Controller page untouched: Gamebar/Center M button mapping, M1/M2, LED, vibration and recovery.
- Existing selected option labels must be exactly **Steam Deck** and **DualSense Edge**. Remove **(Default)** from the global Steam Deck label in XAML and both RESW files. Leave enum identity/tag and true default unchanged.

### 4.2 Overlay Controller tab

Add **one separate Controller for Steam section/card at the top**, ahead of existing M1/M2, Joystick LED, Vibration Strength. Within that section show:

~~~text
Controller for Steam
    Controller          Steam Deck / DualSense Edge
    Gyroscope           On / Off

M1 / M2
...
Joystick LED
...
Vibration Strength
...
~~~

- Match the existing Overlay visual language (CreateOverlaySectionCard, section heading, row spacing/separators, controller navigation), not a copy/paste of Main XAML.
- Use an existing discrete-choice OverlayValueRow for the two supported options, if it can show the required read-only/unavailable state correctly, and an existing OverlayToggleRow for Gyroscope. Do not add a new ComboBox if that would undermine controller-first Left/Right and pointer interaction; do not write a new generic row engine.
- When selection is locked, value remains readable but all Left/Right, pointer and click edits are disabled; focus must skip genuinely disabled rows through existing OverlayRowCapabilities. Gyroscope remains selectable.
- Include both rows in the existing Overlay row-selection/navigation sequence and keep their current position stable across state refresh.
- This is **not** an Overlay Steam profile selection or an active XBOX device switch. No per-game gyro row is added.

### 4.3 Terminology and guides

- The **section/card title** is Controller for Steam, on both Main and Overlay.
- The **row labels** are Controller and Gyroscope.
- Update directly affected Main en-US/ko-KR RESW and UI tests; do not leave one culture saying Steam Deck (Default).
- Update docs/howtouse/README_EN.md and docs/howtouse/README_KO.md (or the current actual canonical guide filenames after checking) for Controller for Steam, the Gyroscope global toggle, Overlay access and Steam Input's per-game gyro policy. Preserve the documented unresolved Steam Deck Gyro To Mouse smoothness caveat, with no invented hardware validation.
- The separate Steam game-profile Use default value is intentionally retained; its meaning is inherited global preference. Do not remove or relabel it merely because the global Steam Deck (Default) suffix was removed.

## 5. Global Gyroscope persistence and exact runtime behavior

Add one **init-only bool** to AppSettings, default **true**, e.g.:

~~~csharp
public bool GyroscopeEnabled { get; init; } = true;
~~~

- Missing property in old settings.json => true; malformed GyroscopeEnabled property => true **for this property only**, preserving all other valid settings.
- SettingsStore Save/Load and StartupSettingsCoordinator change path follow existing additive settings behavior; do not modify AppSettings positional constructor, settings schema globally or build a second file.
- Provide the value in FrontendSettingsSnapshot and the shared Controller for Steam read projection.
- Add one Runtime mutation API through existing IAddonFrontendControl, the main NamedPipe frontend transport, and the Overlay production-settings transport. Return authoritative state/readback. Persistence error must show failure, not a visually committed success.
- Unlike controller choice, GyroscopeEnabled may change **during** Steam game/BPM; it also remains editable in idle/Xbox-only state.
- OFF closes reader admission, **immediately neutralizes** the motion snapshot and stops/cancels current readers through the existing MotionSource path. The canonical SteamDeck and DualSenseEdge publishers keep publishing buttons/sticks/triggers at their unchanged cadence and use unavailable/neutral IMU.
- Protect the output boundary as well: gate the existing motionSnapshotProvider (or equivalent already-owned snapshot read) by the **persisted desired GyroscopeEnabled** so a delayed/failed physical reader stop cannot continue leaking non-neutral motion to the virtual device. No new motion-owner/service/lock/epoch is warranted.
- ON restarts readers via the **existing** ReconcileMotionReadersAsync path *when eligible* (healthy physical owner; active SteamDeck/Edge; not suspend-paused; publisher live). If user turns ON while Overlay capture is pausing the presentation, start naturally at the existing resume/reconcile path. In normal Windows/Xbox360, ON only saves the desired preference; readers remain off.
- OFF does **not** unregister motion capability, disconnect/recreate VIIPER devices, detach the virtual controller, transition PID1901/PID1902, alter HidHide, modify Steam Input's per-game gyro mapping, or interrupt controller input.
- Reuse existing sensor failure behavior and neutral motion semantics; do not insert invented gravity/quaternion values, gyro-to-stick mode or a new sensor timing strategy.
- Honor the persisted OFF setting on first boot, restart/crash recovery, Steam game/BPM entry, suspend/hibernate/resume, physical device loss/re-enumeration, Overlay pause/resume and normal shutdown. If acquisition fails while ON, retain saved ON preference and continue fail-neutral rather than escalating into controller failure.

Illustrative gate only (adapt to actual ownership/call sites; existing lifecycle policy remains in force):

~~~csharp
private bool ShouldRunMotionReaders() =>
    _settings.Settings.GyroscopeEnabled
    && ExistingHealthyOwnedSteamFacingMotionGate();

private MsiClawMotionState CapturePublishedMotion() =>
    _settings.Settings.GyroscopeEnabled
        ? _motionSource?.LatestState ?? MsiClawMotionState.Unavailable
        : MsiClawMotionState.Unavailable;
~~~

No new always-on poller. Do not block the 250 Hz controller-input path on settings I/O or sensor StopAsync.

## 6. Smallest end-to-end settings/transport connection

Existing Overlay supports settings state and two mutation categories (LED, QuickSettingsCurrentPowerSourceOnly), not Steam controller or gyro. Extend that **existing** production-settings seam rather than building a second Overlay channel.

Expected touchpoints:

1. Runtime: Hosting/AddonProcessHost.cs; Settings/AppSettings.cs, SettingsStore.cs, StartupSettingsCoordinator.cs; Frontend/InProcessAddonFrontendControl.cs.
2. Shared frontend contracts: Contracts/Frontend/FrontendContracts.cs (additive property and/or read-only Controller for Steam projection and gyro mutation contract).
3. Main app: UI/Views/ControllerPage.xaml(.cs), UI/MainWindow.xaml.cs, existing NamedPipeAddonFrontendClient/Server and FrontendWire path, localized resources.
4. Overlay: FrontendTransport/OverlayWire.cs, NamedPipeOverlayClient.cs, NamedPipeOverlayServer.cs, Lifecycle/OverlayProcessController.cs, SteamInputAddonforClaw.Overlay/App.xaml.cs, OverlayWindow.Controller.cs.
5. Tests and canonical user guides.

Implementation guidance:

- The same Runtime API/validator currently handling global Steam controller selection must service **both** Main and Overlay. Do not reproduce only a UI-side guard.
- The Overlay production settings capture must additionally receive a *Runtime-derived, presentation-aware* Controller for Steam snapshot (or a comparably small projection of existing state). Do not let the Overlay infer current controller from stored global preference.
- Route a requested controller change to the **existing** SetSteamVirtualControllerAsync method. Because that method returns the unchanged snapshot on rejected active-session changes, the Overlay must treat a rejected change as **failure/not applied**, not a blind success.
- Route gyro writes to one new Runtime-owned global setting setter and existing motion reconcile; both frontends refresh/read back from Runtime.
- Extend the existing correlated Overlay settings request/response for two new setting kinds (or the smallest equally explicit typed extension). An explicit false GyroscopeEnabled request must not be mistaken for an absent field. Validate exactly one intended change per request.
- Overlay protocol currently uses version 19: **bump the version** if wire messages/payloads/kinds change, update encoder/decoder/tests, and preserve its existing incompatible-peer fail-handshake behavior. Do not invent an old-protocol compatibility shim for this pre-release product.
- Extend OnFrontendStateInvalidatedForOverlay / ordinary refresh capture so Steam/BPM transition, app override and saved Gyroscope changes are reflected even if UI stays open. If current transport refresh events do not cover a needed normal transition, connect the existing specific transition event, not a new polling worker.
- A failed/unavailable Overlay mutation must restore authoritative selection/toggle display and keep the unrelated rows working. Preserve the existing serialized/correlated transport mutation guards; no speculative race-oriented state machine.
- Follow the current Main UI refresh owner and lifecycle; do not replace the main UI / Overlay transport or create shared UI controls across independent processes.

## 7. Regression checks (local Codex, fully testable without a physical Claw)

Add focused tests and update existing expectations using the project's test patterns:

- SteamDeck remains enum/settings/invalid-or-missing JSON fallback. Steam Deck (Default) disappears from the **global** displayed option and both RESW values; Steam game profile Use default remains intact.
- Old settings files missing GyroscopeEnabled -> true. Both true/false persist and reload; malformed property falls back only gyro, not the rest of the file. Setter failure retains prior effective value.
- Initial and changed gyro OFF -> neutral SteamDeck/Edge motion publication while ordinary button/stick/trigger state and publisher cadence are unaffected. Even if a reader's stop does not complete immediately, output remains neutral.
- ON -> motion reader admission only when existing Full1902 conditions are satisfied; idle/Xbox360/suspend/offline remain stopped. Restart, resume, presentation switch, overlay pause/resume, physical recovery preserve desired preference and fail-neutral lifecycle.
- Controller selector admission matrix: idle allowed, Xbox-only allowed, Steam game disallowed, BPM-only disallowed, Xbox+BPM disallowed, indeterminate session locked. Validate rejection at Runtime even after stale UI request.
- Idle display uses saved global preference (not currently attached Xbox360); active Steam game/profile override displays actual Deck/Edge; missing active presentation shows unavailable, not a fabricated saved global value; Overlay pause does not incorrectly discard valid active identity.
- Main UI and Overlay share one setting and reflect cross-surface changes. Overlay read-only controller row is non-selectable while locked but Gyroscope stays accessible. Overlay input focus/pointer navigation and other controller sections still work.
- Overlay version handshake, correlated settings mutations (including false bool), result/readback on rejection or persistence error, reconnect and refresh. Existing LED/current-power-source/vibration transport remains unchanged.
- Run applicable Release build, unit/UI/transport tests, and normal CI/static localization checks; update precise tests rather than weakening unrelated assertions.

Suggested existing suites:
- tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
- tests/SteamInputAddonforClaw.Tests/SteamVirtualControllerFrontendTests.cs
- tests/SteamInputAddonforClaw.Tests/MsiClawMotionSourceTests.cs
- tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs and OverlayBackButtonMappingTransportTests.cs
- tests/SteamInputAddonforClaw.UiTests/SteamVirtualControllerUiTests.cs
- tests/SteamInputAddonforClaw.UiTests/OverlayControllerRendererTests.cs
- existing Main UI localization/RESW tests.

## 8. Non-goals, safety, implementation boundaries

- No CTW/ClawTweaks interoperability.
- No Xbox360 gyro-to-stick/mouse, per-game gyro toggle, Steam Input configuration mutations or automatic detection of a game's gyro mapping.
- No VIIPER library/API changes, USB/IP timing changes, re-enumeration or new presentation switching architecture.
- No HidHide ownership, physical PID1902 admission, PID1901 restoration, MSI Center M, sensor backend, LED, rumble, overlay capture routing, front/rear button mapping or touchpad behavior changes.
- Preserve one Windows user/one interactive session support assumption; no Fast User Switching/RDP/multi-session layers.
- Avoid redundant authorities, locks, managers, retry loops, epoch systems, future-compatible frameworks, speculative timing-only race prevention.
- Concrete lifecycle failures (sleep/hibernate/resume, crash/restart/shutdown, device loss/recovery, routing fail-close, VIIPER ownership/teardown and operation failure) remain in-scope safety requirements.
- **No mandatory on-device test or physical hardware evidence is required of local Codex and no PR blocker may be created from missing physical validation.** User owns actual-device verification after merge.
- One cohesive PR is acceptable for this as-yet-unreleased connected feature; keep unrelated refactors out. If changed files are numerous, use clear implementation commits within that PR rather than introducing an unnecessary cross-PR compatibility layer.

## 9. Final acceptance criteria

The PR is complete when code/build/tests prove:

1. Both UI surfaces show **Controller for Steam / Controller / Gyroscope**, with saved Steam Deck default and the visible choice **Steam Deck** (no suffix).
2. Both surfaces apply the exact same Steam/BPM controller lock policy; Xbox-only idle-Steam case is editable; Gyroscope is editable independently.
3. An active Steam game displays its actual attached Steam Deck/Edge presentation, including game-profile override, and never misleadingly substitutes the global saved preference.
4. Gyroscope OFF suppresses motion input from the virtual SteamDeck/Edge while preserving normal controller publication, current virtual attachment, PID1902/HidHide/VIIPER lifecycles and Xbox360 behavior.
5. Settings, Main UI IPC, Overlay IPC, failure handling, readback and localization are integrated and tested.
6. Other Controller UI features and default/profile selection policies remain operational.

On completion, Codex should include changed-file summary, tests/build commands and results, and any known software-only limitations in its PR description. Physical testing belongs exclusively to the user after merge.
