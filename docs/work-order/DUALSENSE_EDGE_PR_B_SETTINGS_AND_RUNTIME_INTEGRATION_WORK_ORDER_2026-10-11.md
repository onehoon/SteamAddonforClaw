# Work Order — DualSense Edge PR-B: Settings Selection and Full1902 Runtime Integration

**Repository:** `onehoon/SteamAddonforClaw`  
**Baseline:** `main` at `498168247d4434dcb4f0b4222d564a2be794cde3` (PR #754 squash merge, 2026-10-11)  
**Implementer:** Local Codex | **Physical verification:** user *after merge*, never a code-review/CI blocker  
**PR scope:** One integrated Addon implementation PR covering saved Settings selection and all runtime owner/lifecycle paths.  
**Status:** Work order; no application code is changed by this document.

## 0. Product contract and explicit exclusions

1. The ordinary **Settings** page gets a **standalone SettingsCard** titled **“Steam / Big Picture Controller”** with exactly **Steam Deck (Default)** and **DualSense Edge (Experimental)**. This is a regular setting, **not** a Developer Menu control, Overlay control, per-game profile, or new page.
2. The **default and fallback is Steam Deck** for Steam-facing output. When the existing actual Steam/BPM predicate is false, the desired output is **always Xbox360**, regardless of the saved selection. Existing non-Steam/Xbox game behavior must not change.
3. One physical PID1902 owner/DirectInput reader/HidHide baseline, **one canonical VIIPER server/bus**, at most **one attached virtual controller and one live publisher**, owned by the existing `MsiClawAddonPresentation` gate. PR-A's Edge native session/publisher/feedback is dormant until explicitly selected under Steam/BPM.
4. Preserve existing front-button user mappings: EX/CG3EM vs A2VM initial defaults, Normal/Steam domains, WING Win+G suppression, OEM1/Center M bindings, Quick Settings Overlay, keyboard/application actions. Steam-facing Edge should use the **same Steam action domain** as Deck, not new mappings.
5. Native Edge front-button actions: Steam Button -> **PS** pulse; Steam Quick Access -> **PS + Cross** finite chord, merged on the **existing Edge publisher**. Deck keeps native Steam/QuickAccess pulses. The PS+Cross behavior is an informed experimental implementation target based on observed real DualSense; **do not claim it has been verified with virtual Edge on Windows**.
6. Edge emits **ordinary dual-motor vibration only**. Ignore Sony LED, player LED, adaptive trigger, audio/haptics. No motor cache or LED-triggered callback replay. Keep the existing physical STOP, callback drain, and bounded dead-man from PR-A.
7. **Do not update/rebuild/download/replace VIIPER DLL, header, `viiper.lock.json`, provenance, package manifests, dependency version, or unrelated libraries in this PR.** Dependency was already updated by separate Addon **PR #753** immediately before #754 (main commit `4204657d9aecd0d95e94862cc3baec0dc4dbc116`); treat the installed/pinned dependency as user-owned.
8. No CTW integration, no new service, manager, authority, gate, epoch, polling loop, process or persistent cache. Supported scope is one Windows user/one interactive session; no Fast User Switching/RDP/multi-session support. No change to Center M reboot-bound authority, PID1901↔PID1902 physical transition, HidHide policy, or A2VM one-boot/session-end exception.

## 1. Design authorities and current-code inspection

Read [Full1902 document precedence](../Full%201902%20Implementation/README.md), [HidHide/startup revision](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md), [reboot-bound lifecycle](../Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md), [Full1902 architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md), and [elevated Runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md). Feature design: [DualSense Edge architecture](../DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md), specifically §§3, 6, 7, 9–13; previous [PR-A work order](DUALSENSE_EDGE_PR_A_TYPED_BACKEND_WORK_ORDER_2026-10-11.md).

**Verified current main source, not a guessed refactor map:**

| Actual code | Required integration |
| --- | --- |
| `Settings/AppSettings.cs`, `SettingsStore.cs`, `StartupSettingsCoordinator.cs` | One persisted Steam-facing preference, isolated normalization, save-before-publish |
| `Contracts/Frontend/FrontendContracts.cs`, `Frontend/InProcessAddonFrontendControl.cs` | Settings snapshot, typed mutation, Runtime-owned persistence, reconciliation request |
| `FrontendTransport/FrontendWire.cs`, `NamedPipeAddonFrontendClient.cs`, `NamedPipeAddonFrontendServer.cs` | One new typed RPC and an appropriate protocol version increment (current inspected **71**) |
| `UI/Views/SettingsPage.xaml(.cs)` | Ordinary standalone card, load/sync, failure rollback |
| `Hosting/AddonProcessHost.cs` and `Runtime/AddonRuntimeComposition.cs` | Wire the **same** coordinator/preference, request existing reconcile, extend motion, front-button output and badge observation |
| `Devices/MSI/Claw/MsiClawAddonPresentation.cs` | Exactly one presentation owner: add Edge to first attach, reconcile, developer re-arm, Overlay/Suspend/Resume, residual-state proof, retirement and strict Windows session-end |
| `VirtualOutput/Viiper/CanonicalDualSenseEdgeSession.cs` | Existing PR-A attached typed Edge session / neutral / callback / classified detach |
| `VirtualOutput/Viiper/CanonicalDualSenseEdgeInputPublisher.cs` | Existing PR-A 250Hz worker and mapper; add same-publisher PS/PS+Cross pulse application |
| `VirtualOutput/Viiper/SteamDeckSystemButtonOverlay.cs` | Existing proven 100ms Deck action pulse; **preserve it** |
| `Feedback/PresentationRumbleFeedback.cs` | PR-A `DualSenseEdgeRumbleFeedbackAdapter.TryArm` / callback drain / safety STOP |
| `Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs`, `CenterM/Oem1ActionDispatcher.cs`, `CenterM/FrontButtonActionExecutor.cs`, WING dispatcher | Steam domain when actual live Deck **or Edge**; preserve existing action executor |
| `Status/Full1902ControllerBadgeEvaluator.cs`, `UI/DeviceSummaryPresentation.cs` | Actual attached and publisher-live Edge badge, not merely saved preference |
| `Hosting/AddonProcessHost.ShouldRunMotionReaders` | Healthy **live** Deck OR Edge (not Xbox); existing freshness/loss/suspend handling |

In current main the owner has only `AddonPresentationKind { Xbox360, SteamDeck }`. `AttachInitialAsync`, `ReconcileDesiredPresentationCoreAsync`, `ResumeAfterSuspendAsync` independently choose Deck vs Xbox from `snapshot.WantsSteamDeck`; `RunDeveloperRumbleRearmAsync` delegates to reconcile. **This PR must eliminate those inconsistent direct choices** using the one resolver below.

The source already contains explicit Deck-only branches in Overlay pause/resume, Suspend pause/resume, `TryProveNoResidualPresentationLocked`, `ArmFeedbackForActivePresentationLocked`, `RetireActivePresentationCoreAsync`, and `TryRequestSystemButtonPulse`. An Edge case omitted from **any** of these is not finished.

## 2. Saved preference: exact persistence policy

Introduce a small shared enum (use an existing contracts assembly or existing shared type location, no new project):

```csharp
public enum SteamVirtualControllerPreference
{
    SteamDeck = 0,
    DualSenseEdge = 1
}
```

Use an **init-only** `AppSettings.SteamVirtualController` property with default `SteamDeck`; do not add a required positional record parameter or change existing settings model shape.

Update `SettingsStore.Load` to parse **only this optional field** in isolation, matching the current string-enum policy (`JsonStringEnumConverter(... allowIntegerValues:false)`): missing, `null`, numeric, wrong JSON type, unknown string or an undefined enum -> **SteamDeck**, **without discarding the rest of the valid settings file**. Existing case policy should be respected; no general JSON format migration. `Save` continues to serialize through the same SettingsStore (one settings.json, no separate file).

In `StartupSettingsCoordinator` expose the current value and implement a **single validated mutation**. Accept exactly two defined enum members, reject out-of-range/cast numeric values; same-value request is an accepted no-op. **Write to disk before changing `Settings` and before signaling live reconciliation.** A persistence failure must leave the previous in-memory and persisted preference effective, return an error via the established frontend failure path, and never initiate an Edge switch. Do not turn a change in this preference into a Center M authority or PID write. An already-saved Edge choice must remain stored even if the optional runtime Edge attach subsequently fails.

Suggested shape, adapted to real interface:

```csharp
public bool ChangeSteamVirtualController(SteamVirtualControllerPreference requested)
{
    if (!Enum.IsDefined(requested)) return false;
    if (Settings.SteamVirtualController == requested) return true;
    var next = Settings with { SteamVirtualController = requested };
    _settingsStore.Save(next);            // may throw: no in-memory change yet
    Settings = next;
    return true;
}
```

For a successful new persisted preference, signal **only the existing AddonProcessHost.RequestControllerPresentationReconcile("SteamVirtualControllerPreferenceChanged")** through a narrow same-process delegate/event. Keep status invalidation and settings snapshot return coherent. If runtime owner is absent or authority is stock/Center M Enabled, store preference normally and do **not** force a controller session. Later next legitimate initial attach reads the saved choice.

## 3. Single desired-kind resolver and presentation-owner integration

Add `AddonPresentationKind.DualSenseEdge` **without renumbering Xbox360/SteamDeck**. Resolve exclusively from the current actual Steam/BPM condition and latest saved preference:

```csharp
private static AddonPresentationKind ResolveDesiredPresentation(
    SteamPresentationSnapshot snapshot, SteamVirtualControllerPreference preference)
    => !snapshot.WantsSteamDeck ? AddonPresentationKind.Xbox360 :
       preference == SteamVirtualControllerPreference.DualSenseEdge
           ? AddonPresentationKind.DualSenseEdge
           : AddonPresentationKind.SteamDeck;
```

Prefer one short `Func<SteamVirtualControllerPreference>` injected into `MsiClawAddonPresentation` from the **same** `StartupSettingsCoordinator` used by UI/RPC. Read the **current value within the existing `_gate`** next to the fresh actual Steam snapshot. No preference snapshots captured at event-queue time, no separate mutable preference owner. Existing `SteamPresentationSnapshot.WantsSteamDeck` retains its meaning as the **Steam/BPM context predicate**; avoid renaming its public ABI in this PR just to express the new kind.

Apply the one resolver to:
- first attach (`AttachInitialAsync`);
- every ordinary running-AppID/BPM/Settings-triggered `ReconcileDesiredPresentationCoreAsync`;
- developer rumble re-arm restore (reuses that same internal reconciliation);
- the desired-kind check in `ResumeAfterSuspendAsync` before same-publisher resume;
- physical loss/PnP re-admission and restart through existing initial/recovery owner callbacks.

**No-op** if actual live kind matches desired; a saved Edge preference while no Steam/BPM is active leaves Xbox360 attached with no attach/detach or native operation. During Steam/BPM, switching Deck→Edge or Edge→Deck must first stop/join the single publisher; clear synthetic pulses, disarm and drain host feedback, request physical STOP, write neutral, and classified-detach; only then attach the new known-detached typed device, write neutral and start one publisher. Target failure => leave detached or explicit retained unsafe ownership, **no automatic fallback** to Deck/Xbox360.

**Required Edge attach routine** patterned on `AttachSteamDeckAsync`:
1. Construct/reuse the PR-A `CanonicalDualSenseEdgeSession` on the existing `CanonicalViiperRuntime`. `Start()` performs lazy Edge create/proven detached/classified attach. **Do not create an Edge device at normal default startup** or when only Xbox360/Deck is requested.
2. Send neutral `SetNeutral()` before starting the publisher. On rejection or publisher-start exception, attempt normal classified session detach; if detach fails or is cleanup-pending/unsafe, **retain the Edge session field** as real ownership evidence and refuse the next attach. This is exactly the existing Deck failed-initial-neutral recovery concern, not a reason for new authority layers.
3. Start a thin `IAddonPresentationPublisher` adapter wrapping PR-A `CanonicalDualSenseEdgeInputPublisher`, using the physical source, **existing** `ShouldSuppressRearButton` release gate, `_motionSnapshotProvider`, `OnPublisherFault` and a single Edge pulse overlay.
4. Commit `_activeKind=DualSenseEdge` and `_publisher` only when native and publisher setup are proven; then call existing `ArmFeedbackForActivePresentationLocked()`. Callback registration failure is **feedback-only unavailable**, not input failure.
5. Add an Edge case to the same `RetireActivePresentationCoreAsync` (including strict session-end/developer-rearm semantics) and dispose/clear Edge session only after proven classified detach. Persist existing `_gate` and physical-rumble STOP ordering.

Guard **residual ownership**: `TryProveNoResidualPresentationLocked` currently checks retained `_deckSession` and Xbox attachment but **does not** check an Edge session or lazily created Edge known-attached status. Extend it so a failed Edge initial neutral or detach cannot be mistaken for a clean empty state on Suspend/Resume/reconcile. Do not detach an unknown-outcome device or allow another attach with a residual Edge session. Keep runtime final teardown exactly one owner; PR-A already includes Edge detach/removal before bus/server.

## 4. PS / Quick Access pulse implementation (existing Edge publisher only)

PR-A's `CanonicalDualSenseEdgeInputPublisher.PublishCurrentStateOnce` does:

```text
physical ControllerState -> DualSenseEdgeDeviceStateMapper.Map
    -> _sink.SetState(mapped)
```

Add a narrowly scoped `DualSenseEdgeSystemButtonOverlay` with the same finite **100ms** pulse convention as `SteamDeckSystemButtonOverlay`, or adapt its minimal existing pattern without changing the current Deck behavior:

- Steam Button requests `PS` (`0x00010000`) on next Edge publication.
- Quick Access requests `PS | Cross` (`0x00010020`) on next Edge publication. Synthetic Cross assertion is added for the short chord; after expiry remove **only** the synthetic Cross, preserving a still-pressed **physical A/Cross** from the mapper.
- `Apply(DSDeviceState)` merges those bits after mapping and before **the same publisher's** `SetDualSenseDeviceState`. A front-button callback must **never** issue concurrent native `SetState` or create a second writer.
- Finite, time-based, nonblocking pulses; a request during owner gate lock, detach, Overlay pause, Suspend, fault or no live Edge session returns false and is **dropped**, not queued for later.
- Clear pulse intents on retirement, Overlay capture pause, Suspend quiesce, publisher failure and process shutdown. No cross-session pulse replay. Preserve all physical/ordinary gamepad buttons and M1/M2 paddles.
- Dispatch `IMsiClawAddonPresentation.TryRequestSteamPulse/ TryRequestQuickAccessPulse` to the **actually active live** Deck or Edge session under the existing nonblocking gate, not from the preference alone. Xbox/unattached/unsafe = false. Keep Deck overlay behavior unchanged.
- Do not implement controller keyboard hacks or a second phantom Xbox device to emulate QAM. Sony behavior remains user-hardware verification after merge.

## 5. Complete existing lifecycle branch audit — not optional

All of the following use the same `MsiClawAddonPresentation` owner and one attached kind. Codex must review **each explicit Deck-only and Xbox-vs-Deck branch**, not just the initial switch.

1. **Overlay pause:** stop/join actual publisher, disarm/drain output callback and STOP physical rumble, clear Edge pulses and write neutral to the **same** Edge session; on rejected neutral, retire fail-closed. No detach for healthy pause. **Overlay resume:** verify source running, VIIPER Ready, exact Edge session Active and `TryGetTrackedAttachmentState == Attached`, then restart **same publisher** and arm Edge feedback; otherwise leave neutral.
2. **Suspend/Hibernate:** retain pause barrier, stop/join, clear both system pulse overlays, drain callback, physical STOP, Edge neutral. Do not detach healthy Edge merely for Suspend. **Resume:** compare fresh desired kind including saved preference; if changed while paused, do not restart the previous kind—let existing reconcile switch. If unchanged, require source and typed attachment proof before restarting same Edge publisher. If physical source missing, keep suspend paused and defer existing PnP recovery.
3. **Physical input loss / PnP re-enumeration:** retire Edge and stop readers using the same physical owner/fail-close rules as Deck. On a healthy new owned PID1902 source, consult latest preference plus actual Steam status. Never use Edge switching to cause a physical PID1901↔PID1902 write.
4. **Publisher fault / initial attach failure / failed neutral / callback failure:** no concurrent publisher after a stop/join failure, no stale native handle usage, no accidental second attach. When cleanup is unproven, preserve `_edgeSession` and unsafe classification; never substitute X360/Deck.
5. **Explicit Center M Enable / stock restoration / uninstall:** prove publisher stop, feedback drain & STOP, classified Edge detach and canonical runtime `Closed` before physical ownership is released to MSI. Do not bypass existing HidHide cleanup or restart boundary.
6. **Windows WM_ENDSESSION(TRUE) A2VM exception:** preserve the already-narrow 2-second/one-command PID1901 preparation admission. Actual active Edge follows **the same** retirement, VIIPER Closed, physical STOP, DirectInput cleanup proofs. Any uncertainty skips the command; do not change reboot-bound authority or add timers.
7. **Controlled restart / crash-recovery startup / shutdown:** selection is loaded from one persisted SettingsStore; no automatic previous-host rumble state or PS/QAM pulses are replayed. Final VIIPER teardown still retires Edge in the PR-A owner and stops on classified unknown outcomes.
8. **Developer Restore Vibration round-trip:** preserve gate, callback/STOP/recovery ordering and fresh resolver after physical re-arm. The in-flight-settings-change event uses the existing defer/schedule semantics rather than forcing nested owner operations.
9. **Xbox360 diagnostic rumble loop:** retain the existing Xbox-only eligibility; never run Xbox-specific XInput diagnostic against Edge.

Do not add synchronization to protect arbitrary instruction-level interleavings. Treat actual process restart/suspend/PnP/device loss/operation failure as product requirements, while unsupported multi-session and timing-pathological races are not blockers.

## 6. Motion readers and front-button mapping

### Motion

In `Hosting/AddonProcessHost.ShouldRunMotionReaders`, change only the presentation check:

```csharp
hasHealthyOwnedMotionSession
    && !isSuspendPaused
    && activePresentation is (AddonPresentationKind.SteamDeck or AddonPresentationKind.DualSenseEdge)
```

Retain all prior ownership/identity/readback guards; Xbox360 never starts the gyro reader solely due to the preference. The existing `ReconcileMotionReadersAsync` after first attach, reconcile, pause/Resume and physical recovery remains the owner; no new sensor reader or 250Hz sensor polling. PR-A Edge mapper already handles freshness (250ms gyro / 500ms accel), normalized axes and zero for stale/incomplete motion. **No new accelerometer rest-vector synthesis**. Live gyro axis/sign verification belongs to post-merge user testing; do not block code merge on uncertain physical orientation.

### Front buttons

The host currently passes `isSteamDeckPresentationActive: () => ActivePresentation == SteamDeck` to `MsiClawFrontButtonRuntime.Create`. Replace that **predicate's semantics** with actual healthy **Steam-facing presentation = Steam Deck OR Edge**; optionally rename narrow callback/local descriptions as needed. Avoid reimplementing `FrontButtonMappingSettings`, `Oem1ActionDispatcher`, `WingActionDispatcher` or `FrontButtonActionExecutor`; these already select the Steam vs Normal domain and invoke the two pulse delegates. Keep WING Win+G native suppression armed exactly as before. Do not modify normal-mode/Steam-mode persisted user mappings, action schema, shortcut behavior or OEM1 button orientation/model defaults.

## 7. Settings UI, frontend transport and status

1. **XAML:** `UI/Views/SettingsPage.xaml`, place one standalone `CommunityToolkit.WinUI.Controls.SettingsCard` **outside DeveloperMenuCard**, labeled **Steam / Big Picture Controller**. Include a two-item `ComboBox` (or native RadioButtons), clear context text e.g. `Controls which virtual controller Steam sees while a Steam game or Big Picture is active. Normal Windows mode stays Xbox360; switching may briefly disconnect the controller.` Order: Steam Deck (Default), DualSense Edge (Experimental).
2. **UI code-behind:** `SettingsPage.Initialize` reads `bootstrap.Settings.SteamVirtualController`. Use a small `_applying...` guard and an in-flight flag (same pattern as Gaming Home selection) to avoid sending RPC on initial `SelectedItem` change and duplicate clicks. Call typed mutation, use returned **persisted** settings snapshot as truth. On rejection, timeout, Runtime unavailability or transport failure, restore the actual saved choice and show a short error; don't falsely indicate it was switched. UI must not require DeveloperMenuEnabled. No second settings file.
3. **Contracts:** add the preference enum in a shared contract and `FrontendSettingsSnapshot.SteamVirtualController` as an init-only value, preserving every existing positional construction. Add exactly one `IAddonFrontendControl.SetSteamVirtualControllerAsync(preference,...)` method.
4. **RPC:** add `FrontendRpcMethod.SetSteamVirtualController`, a typed `SetSteamVirtualControllerRequest`, `NamedPipeAddonFrontendClient` sender, `NamedPipeAddonFrontendServer` dispatcher to the existing in-process method. Follow `FrontendWireCodec`'s existing serializer and exact enum names. Update protocol comment and increment **71 → 72**, **after verifying current `main` still reads 71**. No new transport, protocol negotiation framework or second settings snapshot endpoint.
5. **Runtime mutation:** `InProcessAddonFrontendControl` calls the same `StartupSettingsCoordinator` mutation, emits `StateInvalidated`, and requests host reconciliation **only after confirmed persistence and only when value changed**. A failed mutation propagates a normal typed/remote failure (or returns authoritative unchanged snapshot), not an optimistic success. The host receives a narrow callback from the existing `AddonProcessHost` composition; don't have this generic frontend class directly own physical/presentation objects.
6. **Badge:** add `FrontendControllerBadgeState.DualSenseEdgeActive` to `FrontendContracts`, map it in `Full1902ControllerBadgeEvaluator` and `UI/DeviceSummaryPresentation` (e.g. `DualSense Edge · Active`). Preserve existing precedence for initializing, reconnecting, suspended/paused, recovery unsafe, and NeedsAttention. Actual Edge badge **only** when physical source and actual Edge publisher are healthy/live; saved preference by itself never changes the badge.
7. **Status refresh:** ensure the existing `NotifyControllerBadgeIfChangedAfterAsync` path and Settings UI update reflect the resolved actual presentation *after* a successful switch. A saved Edge preference with failed attach remains saved but actual state should show unavailable/NeedsAttention, not an Edge badge. Status/UI must not silently interpret a UI ComboBox choice as actual native attachment.

**Note:** The existing `SettingsPage` already has `GamingHomeComboBox` refresh/mutation and power-source/logging guards; use its straightforward precedent. Do not create a view model or a dedicated settings subsystem only for this card.

## 8. CI/deterministic regression coverage (no physical verification requirement)

Add/update focused tests under `tests/SteamInputAddonforClaw.Tests`, `tests/SteamInputAddonforClaw.FrontendTransport.Tests` (if present; inspect tree) and `tests/SteamInputAddonforClaw.UiTests`. Use actual repository test-project names; do not create a separate test project just for Edge.

Required tests:

- **Settings:** no key / valid Deck / valid Edge / malformed string / number / JSON null / unknown enum name load correctly; unrelated custom settings survive a bad preference. Persist-and-reload Edge; invalid mutation and failed save do not publish new choice or schedule reconcile.
- **Frontend:** bootstrap and returned settings snapshot carry same saved preference; RPC request/response and reconnect round-trip, version mismatch existing behavior, UI initial binding doesn't mutate, failed RPC restores selected item. Ensure no accidental `DeveloperMenuEnabled` gating.
- **Selection matrix:** no Steam no BPM + either choice = Xbox360; any positive Steam AppID/BPM + Deck=Deck, + Edge=Edge; successful same-kind reconcile is no-op. Preference change while only X360 active must NOT call Edge constructor/attach.
- **Attach/switch:** first Steam Edge attach, X360↔Edge, Deck↔Edge, back to Deck; exactly one attached kind and one publisher; neutral precedes publisher; stop/join, callback drain, physical STOP, and classified detach precede target attach. A rejection/unknown outcome never attaches a second device or falls back. Lazy Edge created once on first relevant demand, not default startup.
- **Residue:** forced Edge neutral failure followed by failed classified detach retains session, blocks future attachment, is not falsely classified empty by Suspend/Resume or initial retry. No managed handle loss on cleanup-pending.
- **Pulse:** PS and PS+Cross finite assertion/release over same publish loop, one or more 250Hz ticks before expiry, no extra native SetState path, held physical Cross remains held beyond synthetic chord, queued pulse cleared on switch, Overlay pause, Suspend, fault, shutdown. Deck native Steam/QuickAccess path unchanged.
- **Motion:** healthy live Edge activates existing motion-reader policy; Xbox360 not; suspend/unhealthy session stops reader; stale/nonfinite motion remains neutral while ordinary buttons continue.
- **Feedback:** Edge ordinary `small8`/`large8` correctly maps to physical motors; 0/0 STOP; dead-man STOP; callback arming failure doesn't tear down controller input; disarm/drain protects final STOP during switch/Suspend/Center M return/session-end.
- **Full lifecycle:** Overlay pause/resume/neutral failure and proven typed status; suspend→resume same kind; changed preference during Suspend routes to existing reconcile; physical loss/PnP re-admission; developer rumble re-arm restore; Center M Enable & restart, A2VM short session-end, runtime teardown. No new PID1901/1902 or HidHide mutation from controller selection.
- **Frontend badge:** actual live Edge -> Edge Active; saved Edge without actual attached output -> never Edge Active; all existing 7+ status precedence cases preserved. WING/OEM1 domain and customizable default mapping unchanged.
- Preserve PR-A ABI/Edge mapper/native callback/publisher/runtimes tests plus Xbox360, Deck, Full1902 tests.

Run solution build, targeted and normal managed tests, and existing CI. **Hardware enumeration, Steam Input recognition, actual PS/QAM response, joystick-LED behavior, real gyro sign validation, and physical vibration are strictly user-owned post-merge verification.** They are *not* local Codex deliverables and *not* PR review/merge blockers.

## 9. PR handoff and acceptance

One PR is sufficient because separating preference enablement from lifecycle integration would temporarily expose a half-wired controller mode. Do **not** split the single authoritative presentation owner across incomplete changes. Keep code changes focused; do not opportunistically refactor Xbox360/Deck publishers, WinUI navigation, USB/IP, prereqs, startup helper, HidHide, physical mode, or native VIIPER.

The PR is acceptable when:
- Saved default remains Steam Deck; non-Steam stays Xbox360; no Edge native creation until genuinely requested in Steam/BPM.
- A Settings **standalone card** selects Edge and persists it; one settings/RPC/owner path applies it without restart.
- Every real lifecycle path knows the third output kind and retains one safe owner, one active publisher, fail-closed cleanup, and confirmed feedback STOP semantics.
- Edge PS and PS+Cross are short, finite pulses on the sole existing Edge writer; no cross-session replay or keyboard fallback.
- Actual badge and Settings preference do not conflate saved selection with attached state; legacy mappings and UI/Overlay rules stay intact.
- Native dependency is **untouched** in this PR.
- Automated checks pass (or review addresses legitimate code failures), **without mandatory MSI Claw/Sony/Steam physical tests**.

**Final principle:** implement a selectable experimental third presentation inside the existing Full1902 owner, not a third authority. Favor a few explicit, reviewable cases over a new generalized controller abstraction. Real suspend/restart/PnP/device-loss/rollback safety is mandatory; theoretical instruction-level race defenses are not.
