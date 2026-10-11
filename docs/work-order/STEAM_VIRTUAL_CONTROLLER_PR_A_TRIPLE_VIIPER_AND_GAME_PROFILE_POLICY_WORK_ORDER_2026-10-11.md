# Work Order — PR-A: Persistent Triple VIIPER Devices and Per-Steam-Game Presentation Policy

**Date:** 2026-10-11  
**Target:** `onehoon/SteamAddonforClaw` (current `main`)  
**Implementation:** Local Codex  
**Scope:** Runtime, persistence, frontend contracts/transport as necessary, automated tests. **No UI/README implementation in PR-A.**  
**Follow-up:** PR-B moves the Settings selector to the top of Controller, adds Profile controls, removes “Experimental”, shortens the description and updates English/Korean README.

## 1. Required source-of-truth review

Read `docs/Full 1902 Implementation/README.md` and its active precedence documents before coding. Read `docs/DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md`, `docs/VIIPER_INTEGRATION.md`, and the existing DualSense Edge PR-A/PR-B work orders as implementation history. Where the old optional/lazy Edge policy or “Experimental” label conflicts with this work order, **this work order is the new requested policy**. This is a **standalone Full1902** app; ignore CTW integration guidance.

Code inspected at baseline `9fe7fa92cefd2b1aeb7dfedfdafe4527de50e99c`:
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs`: `TryInitialize`, `TryEnsureDualSenseEdgeDevice`, staged unwind, teardown, typed USB state.
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeApi.cs`: native ABI availability, classification, required/optional exports.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`: `ResolveDesiredPresentation`, initial attach, serialized reconcile, suspend/resume and overlay capture.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: VIIPER acquisition, `OnActualRunningAppIdChanged`, `OnBigPictureChanged`, `RequestControllerPresentationReconcile`, initial attach and recovery.
- `src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs`: `SteamPresentationSnapshot(RunningAppId, BigPictureActive)`.
- `src/SteamInputAddonforClaw/Profiles/GameProfile.cs`, `GameProfileMutations.cs`, `ProfileStore.cs`, `ProfileDocument.cs`: per-Steam-AppID profile ownership/persistence.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`, frontend contracts and Named Pipe transport: current Settings mutation and game-profile APIs.
- Existing tests for VIIPER staged init/teardown, presentation policy, profile mutations, frontend transport.

## 2. Product decisions (binding)

1. Three logical VIIPER devices **must be created during normal Runtime VIIPER initialization**: Steam Deck, Xbox360 and DualSense Edge, all initially **detached** (`autoAttachLocalhost: false`), sharing **one** server and bus.
2. `CanonicalViiperRuntimeState.Ready` means **all three** logical devices have proved valid identity and detached state. There is no “optional Edge unavailable” partial-Ready mode.
3. Failure of creation/identity/detached-state proof for **any one** device is an initialization failure, with the **same classification, safe staged unwind, owner retention, and recovery/retry policy** for all three; no Edge-only bypass, or silently falling back to Deck/Xbox.
4. At most **one** virtual device is Windows-attached/published at any time. “Pre-create all three” does **not** mean “attach all three.”
5. Windows/no Steam game/no BPM: Xbox360. BPM with no Steam game: global `SteamVirtualControllerPreference` (Steam Deck by default). Active Steam game: its **enabled** game profile's optional controller override, if specified; otherwise the global preference.
6. Game-profile `Enabled = false` disables its controller override (same master profile toggle); no independent controller-enable flag. A nullable per-game value represents “Use default” and explicit Steam Deck / DualSense Edge values.
7. Steam game launch/exit in BPM **automatically switches** to the game-specific controller and back to the global BPM controller. This is an intentional existing Presentation-owner-serialized automatic native detach/attach transition. **Do not conflate it with disallowed manual hot swapping.**
8. **User-initiated** mutation of the global controller preference and per-game controller overrides is denied while a Steam game is running **or** BPM is active. Enforce at Runtime API/mutation authority, not just disabled frontend widgets. Mutations made before entering BPM/game are allowed. Do not prohibit automatic selection/reconcile during game or BPM.
9. No Overlay selector or manual in-game switching. No changes to physical PID1902, HidHide ownership, Center M, WING policy, USB/IP infrastructure, or Xbox game profile controller identity.

## 3. Triple-device staged VIIPER initialization

`CanonicalViiperRuntime.TryInitialize(...)` presently creates/validates Steam Deck and Xbox360 and then marks `Ready`. `TryEnsureDualSenseEdgeDevice()` currently handles lazy Edge creation after `Ready`; this optional branch is obsolete.

- Extend existing staged init **after the current Deck and Xbox stages** to call the already-bound `CreateDualSenseEdgeDevice(..., autoAttachLocalhost: false, idVendor: 0, idProduct: 0, metaState: nint.Zero)`; verify nonzero owned handle, expected BusId/identity and detached attachment state with the same safety rules as the other two devices.
- Keep existing known-ownership semantics, including creation returning failure with an owned nonzero handle; never leak the handle or lose ownership on failure.
- For known-safe failed stages, unwind **Edge → Xbox360 → Deck → Bus → Server**. For unknown native state/query failure/invalid classification, retain the **same real owner** in unsafe/cleanup-pending state; do not destructively teardown or retry over uncertain ownership.
- Reuse the existing edge removal/detach helpers, teardown phases and native callback lifetime; do not introduce another owner or manager.
- Once Edge is a required initialized device, remove/flatten the obsolete lazy-creation path from normal presentation attach; do not duplicate creation there. The Edge attach path should only verify/reuse the existing handle.
- Review `CanonicalViiperNativeApi` initialization and the pinned, vendored VIIPER dependency. The production Edge API is now **required**, not an optional capability. Ensure missing required exports reject readiness cleanly, but **do not change vendored DLL/header/lock/provenance or fetch a new native dependency** absent actual evidence of a dependency mismatch.
- Update precise logs/comments/test names that incorrectly describe two devices, optional Edge or lazy Edge.

**Retry is required on recoverable init failure for every device equally.** First trace the *existing* host/runtime initial acquisition, deferral, retry and device arrival paths. Reuse their real lifecycle recovery; if initial VIIPER init currently has no applicable retry path, implement the **smallest bounded/serialized safe retry at the existing startup owner boundary**, only after all acquired resources are proved cleaned/closed. Do not claim an existing retry mechanism covers VIIPER unless code proves it. No busy loop, second bus over an uncleared first bus, separate Edge retry manager or new authority. Unknown native ownership remains fail-closed and must never be forcibly retried. Maintain cancellation/shutdown behavior.

## 4. Game profile persistence and effective preference

Add a single optional controller preference to `GameProfile`, preferably:

```csharp
public SteamVirtualControllerPreference? SteamVirtualController { get; init; }
```

Use the existing enum and AppID-keyed `profiles.json` (keep backward-compatible additive JSON with omitted/null = global default). Add one narrow `GameProfileMutations` setter and read/capture method, reuse existing `ProfileMutationGate` and load/save semantics. Do not create another profile store, static cache authority, or duplicate settings in `settings.json`.

Resolve effective desired presentation from the current captured `SteamPresentationSnapshot`:

```text
if (!snapshot.WantsSteamDeck)                    => Xbox360
else if (snapshot.RunningAppId != 0
         && profile for that Steam AppID is Enabled
         && profile.SteamVirtualController != null) => explicit game preference
else                                               => global Steam preference
```

Note: BPM may remain active when `RunningAppId` changes; the AppID override still wins. A profile OFF or absent never wins. Prefer a small existing-owner resolver/provider rather than adding policy state, a new service, a new selector manager or another presentation pipeline. Snapshot/effective preference must be captured **after acquiring the existing presentation gate**, as current reconcile already does, so automatic launch/exit/recovery always sees current state. Cover initial attach, game start/end, direct game launch, BPM entry/exit, suspend/resume, physical recovery and developer rearm using **the same** desired selection policy. Do not couple to unrelated 2.5-second performance-profile settlement.

## 5. Runtime mutation guard (no manual change during Steam/BPM)

Current `InProcessAddonFrontendControl.SetSteamVirtualControllerAsync` calls `_settings.ChangeSteamVirtualController(preference)` and then requests presentation reconcile; there is no Steam/BPM admission guard. Add a clear, testable Runtime-side guard against mutation when `RunningAppId != 0 || BigPictureActive`, using existing session snapshot providers. It must leave existing saved preference unchanged and must not schedule a presentation switch when blocked.

Add a minimal per-game mutation API/contract and named-pipe transport integration sufficient for PR-B to bind a Profile selector. Apply the **same** Steam/BPM mutation guard to controller-override writes. Do not block unrelated profile/performance changes by these controller-specific rules. Guard the effective setter even if an old Settings UI still renders an enabled ComboBox before PR-B; PR-B will disable the controls and show concise explanatory text.

Decide whether an API returns an existing `Unavailable`/rejected outcome or the existing snapshot unchanged based on current contract conventions; ensure the frontend receives a truthful result and cannot mistakenly display a changed selection.

## 6. Required automatic reconcile behavior

Reuse `MsiClawAddonPresentation` (one gate, one publisher, one attached device, stop/join → neutral → detach → attach → publish) and existing host triggers `OnActualRunningAppIdChanged`, `OnBigPictureChanged`. Do **not** add another async switching subsystem.

Must work as follows:

| Event | Expected attached presentation |
| --- | --- |
| Standalone Windows (no Steam/BPM) | Xbox360 |
| BPM enters, global Deck | Steam Deck |
| BPM enters, global Edge | DualSense Edge |
| BPM (global Deck) launches AppID A with Enabled override Edge | DualSense Edge |
| AppID A ends, BPM remains | Steam Deck |
| BPM (global Edge) launches AppID B with Enabled override Deck | Steam Deck |
| Game starts directly outside BPM, enabled override exists | Per-game selection |
| Game profile OFF / override null | Global selection when Steam-facing |
| Steam and BPM both leave | Xbox360 |

Game arrival and exit may inherently require Windows device re-enumeration: that is **supported automatic behavior**. Do not promise universal per-game hot-plug compatibility or build game-specific workarounds. If native switch cannot complete, preserve existing fail-close, never attach/publish two simultaneously, and do not silently substitute another controller.

Do not change Overlay capture policy: while capture owns/pauses output, existing reconcile remains deferred/blocked, then the existing release trigger reconciles the latest desired game state. Respect suspend/resume, shutdown, explicit Center M authority return, physical loss/re-enumeration, and feedback callback teardown; do not introduce theoretical instruction-interleaving race defenses.

## 7. Tests / evidence required from local Codex

Add/extend automated tests for:
- Triple init successful: exactly three native logical devices, same server/bus, all Detached before first attach, then exactly one attached.
- Edge create failure with/without owned handle; identity mismatch; attachment query failed/unknown/initially-attached; correct reverse teardown and no unsafe cleanup; equal Ready/retry policy as Deck/Xbox.
- Clean recoverable startup init failure followed by safe bounded retry to success; retained unsafe owner **never** retried atop live native ownership.
- Per-game controller override stored/reloaded (null/Deck/Edge), profile OFF ignored, profile ON wins; unrelated profile settings and legacy persisted JSON retained.
- BPM global Deck → game Edge → BPM Deck and inverse; Windows Xbox360; direct game; game exit; initial attach/resume/recovery agree on effective policy.
- Runtime controller mutations while Steam game or BPM active are rejected without persistence/reconcile; same mutations work while idle; unrelated settings remain writable.
- No double Windows-attached device, no simultaneous publishers; failed attach/detach respects existing fail-close and safe callback teardown.
- Build/test and any repository CI-relevant static checks; report failures accurately. No physical-device/real-game validation required of local Codex. Hardware validation is the **user's** responsibility after merge and **must not be a PR blocker**.

## 8. Explicit PR boundaries

**Do in PR-A:** Runtime VIIPER triple initialization, common safe failure/retry, per-game persistence, effective resolver, frontend/API/transport contracts, controller-only Runtime mutation gate, focused tests.

**Do not do in PR-A:** Move cards from Settings to Controller; Profile XAML controls; UI disabled-state/description text; English/Korean README update; remove the Edge “Experimental” label in UI/docs (PR-B); Overlay controls; CTW support; new architecture, PID1902/HidHide behaviors, new native ABI, speculative race machinery.

## 9. Completion conditions

- Existing tests plus new focused tests pass; no production lifecycle safety regression.
- All three logical devices are required for VIIPER Ready and are not auto-attached at creation.
- Initial creation/recovery honors proven-safe teardown and all-device retry parity.
- Steam AppID enabled profile override causes correct **automatic** presentation on BPM/game entry/exit; user mutation cannot alter controller identity while Steam/BPM is active.
- Existing authority, single-bus/publisher, physical PID1902/HidHide, shutdown, overlay pause and fail-close invariants remain intact.
- Report code changes and automated validation. User performs post-merge physical validation separately.
