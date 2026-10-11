# Work Order — DualSense Edge Y-Axis Fix, Overlay Motion Lifecycle and Documentation Alignment

**Repository:** `onehoon/SteamAddonforClaw`  
**Code baseline reviewed:** `main` at `8046250c461c0444a60fe33f54044f8aa1d8d6c3` (PR #755 already squash-merged, 2026-10-11). Recheck current `main` before editing if the branch advances.  
**Implementer:** Local Codex; **hardware validation owner:** user **after merge**, never a PR/CI blocker.  
**Delivery:** **ONE pull request, exactly THREE logically separated implementation commits** as described in §1. Do not create three PRs or squash commits locally; preserve the distinct commits in the PR for review. The repository's eventual Squash Merge policy is unchanged.

## 0. Decision / accepted review scope

The user **physically confirmed that both DualSense Edge analog sticks move vertically in the opposite direction**. Fix this demonstrated input defect. Also address the valid nonblocking observation that the common A2VM/CG3EM motion readers keep running while Quick Settings Overlay capture has stopped the current Steam Deck or Edge publisher. Finally, update the following **four existing documents** to the already-merged PR-A/PR-B/current pinned VIIPER reality.

These are the complete items for this PR:

1. **Fix the left and right stick Y axis in the Edge mapper only.**
2. **Stop/cancel the shared motion readers promptly on successful Overlay capture; resume through existing release/reconcile**, without blocking Overlay show/hide on a 3-second sensor shutdown.
3. **Correct stale current-state documentation** in `docs/VIIPER_INTEGRATION.md`, `docs/VIIPER_MIGRATION_TODO.md`, `src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md`, and `docs/DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md`.

**Not part of this PR:** Native VIIPER source or dependency bump, vendored `libVIIPER.dll`/`libVIIPER.h` replacement, `viiper.lock.json` or artifact hashes, PS/QAM mapping changes, rumble/LED/haptics changes, gyro axis/sign changes, Xbox360/Steam Deck stick mapping changes, controller-selector changes, startup/Center M/PID1901/PID1902/HidHide changes, additional managers/locks/epochs, or generalized cleanup refactors.

An earlier speculative issue involving Edge *creation followed by impossible/inconsistent native bus identity* and retryable device removal **is explicitly excluded**. On the supported real single-owner path, the fork's `GetUSBDeviceIdentity` reads registered in-memory handle/bus metadata immediately after successful creation; no plausible user lifecycle path to that combined invalid-identity scenario was established. Existing classified cleanup/fail-close must remain untouched. A test's ability to fabricate that case alone is not a new product requirement.

## 1. Commit boundaries — one PR, three commits

Implement in this order:

| Commit | Suggested subject | Strict files/scope |
| --- | --- | --- |
| **1** | `fix(edge): correct left and right stick Y direction` | `DualSenseEdgeDeviceStateMapper.cs` and `DualSenseEdgeDeviceStateMapperTests.cs` only |
| **2** | `fix(motion): stop readers during overlay capture` | `AddonProcessHost.cs` and focused existing motion/Overlay tests only |
| **3** | `docs(viiper): align Edge production state and pinned ABI` | **Exactly the four named existing documentation files**; no code or dependency artifacts |

Both implementation commits must be individually coherent and testable. Keep mechanical documentation-only changes out of the code commits; do not create new subsystems just to achieve commit separation.

**Submit one PR** containing all three commits, not three dependent PRs. PR description should show the hardware-confirmed stick bug, Overlay motion behavior, docs alignment, and automated test evidence. No physical verification checklist as a merge gate.

## 2. Source authority and verified insertion points

Read the [Full1902 document-precedence README](../Full%201902%20Implementation/README.md), [HidHide/startup authority](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md), [reboot-bound controller ownership](../Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md), [Full1902 controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md), [elevated runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md), [Edge feature architecture](../DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md), [PR-A](DUALSENSE_EDGE_PR_A_TYPED_BACKEND_WORK_ORDER_2026-10-11.md) and [PR-B](DUALSENSE_EDGE_PR_B_SETTINGS_AND_RUNTIME_INTEGRATION_WORK_ORDER_2026-10-11.md). All refer to the **standalone** application, **not CTW integration**; older routing/CTW references are historical only.

Inspected current code:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawControllerStateMapper.cs`: normalizes physical stick Y with `invert: true`, yielding **positive for up**. Leave this shared normalization untouched.
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/DualSenseEdgeDeviceStateMapper.cs`: currently sets `LY = ToSByte(state.LeftStick.Y)` and `RY = ToSByte(state.RightStick.Y)`; these are **confirmed reversed** on real Edge mode. The existing `ToSByte(short)` divides by 256, rounds away from zero and clamps to `sbyte`.
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs` and `Xbox360DeviceStateMapper.cs`: keep existing Y orientation; they are not the faulty Edge HID conversion.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: `CoordinateOverlayToggleAsync()` calls `PauseForOverlayAsync`, commits `_overlayCaptureActive = true`, then starts fire-and-forget Overlay data refreshes. There is **no** `ReconcileMotionReadersAsync` after successful capture commit. `RetireOverlayCaptureUnderTransitionAsync` already invokes `ResumeAfterOverlayAsync` and requests `RequestControllerPresentationReconcile("OverlayCaptureReleased")` when the physical source is usable.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`: `PauseForOverlayAsync` stops/joins the publisher, disarms feedback, physical STOP, and sends neutral; `ResumeAfterOverlayAsync` restarts the **same** proven attached typed device where safe. The runtime's `IsActivePresentationLive` checks publisher running and becomes false when paused.
- `AddonProcessHost.ShouldRunMotionReaders`: starts only for **healthy, live Steam Deck OR DualSense Edge** and never for Xbox360. `ReconcileMotionReadersAsync` already selects between `StartMotionSourceAsync` and `StopMotionSourceAsync`.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs`: `StopAsync` invokes `InvalidateAndCancel()` **before** waiting for its lifecycle gate/reader completion; reader join has a **3-second bounded wait** and logs deferred stop. `StartAsync`/ `StopAsync` already serialize on `_lifecycleGate`; do not introduce additional synchronization.
- Relevant existing tests: `tests/SteamInputAddonforClaw.Tests/DualSenseEdgeDeviceStateMapperTests.cs`, `MsiClawMotionSourceTests.cs`, `AddonProcessHostOverlayQuickSettingsContractTests.cs`, and presentation/Overlay lifecycle tests. Reuse existing projects and test helpers.

## 3. Commit 1 — Edge Y-axis correction

Change **only** the Edge mapper's vertical stick conversion. Preserve horizontal X, triggers, D-Pad, button mapping, M1/M2 `R4/L4`, gyro/accel mapping and system pulse overlay.

Sony HID stick position uses low byte **0 = up** and high byte **255 = down**, whereas the current normalized Claw Y is positive for up. The VIIPER Edge input-report packing converts `sbyte` to centered HID byte (`0x80` neutral). **Negate both Y values before signed 8-bit encoding.**

Minimal example (adapt to actual local code; keep single `ToSByte` helper):

```csharp
// DualSenseEdgeDeviceStateMapper.Map(...)
LX = ToSByte(state.LeftStick.X),
LY = ToSByte(state.LeftStick.Y, invert: true),
RX = ToSByte(state.RightStick.X),
RY = ToSByte(state.RightStick.Y, invert: true),

private static sbyte ToSByte(short value, bool invert = false)
{
    // Promote BEFORE negation. Negating short.MinValue in a short can overflow.
    var signed = invert ? -(int)value : (int)value;
    var rounded = Math.Round(signed / 256d, MidpointRounding.AwayFromZero);
    return (sbyte)Math.Clamp(rounded, sbyte.MinValue, sbyte.MaxValue);
}
```

Keep the mapper's existing rounding and clipping policy; do not cast `short.MinValue` back to short, don't convert Y to `byte` in the managed ABI, and do not alter `DSDeviceState` layout or VIIPER.

**Tests:** Update the existing `ConvertsStickBoundariesWithNearestAwayFromZeroAndPreservesQuadrants` expectations (**current `LY = +1, RY = +127` need to become `-1, -128` for that sample**; positive full-scale inverted Y rounds to -128, not -127). Add direction-semantic coverage for **BOTH** sticks with independent neutral, up-full, down-full, intermediate up/down and X-only cases:

- source Y=+32767 (physical UP) -> Edge signed -128 / HID 0;
- source Y=-32768 (physical DOWN) -> Edge signed +127 / HID 255;
- Y=0 -> 0 / HID 128;
- source Y=+128 -> -1; source Y=-128 -> +1, both with preserved independent X.
- Keep X coordinate invariants and existing button/gyro tests green.

Use comments that describe physical UP/DOWN and Sony output semantics, not merely signed arithmetic. **Real-device stick inversion is already confirmed by the user; do not require Codex to reproduce on hardware.**

## 4. Commit 2 — Motion-reader stop on successful Overlay capture

### The bug

During normal Overlay capture, `PauseForOverlayAsync` stops the game-facing Deck/Edge publisher but the existing motion readers keep polling. This consumes avoidable sensor I/O/CPU/power until a later lifecycle reconcile. It affects **both Deck and Edge**, not only Edge.

### Smallest correct design

Use the existing `ReconcileMotionReadersAsync` in `AddonProcessHost` **immediately after Overlay capture is successfully committed** and the publisher has been proven stopped, never before `PauseForOverlayAsync` returns success.

Suggested call site:

```csharp
_overlayInputRouter = router;
_overlayCaptureActive = true;
AppLog.Info("OverlayCapture", "Capture committed.", ("Event", "OverlayCaptureCommitted"));

// The publisher is now stopped and ShouldRunMotionReaders() is false.
// Invalidate/cancel immediately; do not await a potentially 3 s sensor join
// while the visible-surface transition gate is held.
_ = ReconcileMotionReadersAsync("OverlayCaptureCommitted");
```

This reuses the current `ShouldRunMotionReaders` decision, `StopMotionSourceAsync` (which invalidates samples synchronously before its first async wait), bounded reader cleanup and existing error logging. **Do not await the full motion-reader stop** inside `CoordinateOverlayToggleAsync` or under `_visibleSurfaceTransition`; a blocked sensor read must not delay a user's Overlay dismissal or reopening Main UI.

Placement must preserve these facts:
- Do not stop sensors if Overlay `ShowAsync` fails or `PauseForOverlayAsync` rejects without stopping the publisher.
- If publisher was stopped and Overlay pause then **neutral write fails**, the existing fail-close retirement and `OverlayPauseFailed` presentation reconcile own the recovery; don't add an independent sensor restart/fallback inside a failure branch.
- Existing Overlay controller input router still consumes the physical DirectInput stream; **only the separate gyro/accelerometer readers** should be canceled. Do not stop `MsiClawInputSource` or detach typed VIIPER devices when Overlay opens.
- Keep pulse-clear, rumble STOP, Overlay `_visibleSurfaceTransition` serialization and native device owner unchanged. Avoid `Task.Run` just to stop sensor readers, new locks/gates/counters/epochs, stop retries or custom polling.

### Resume and normal lifecycle

The **existing** `RetireOverlayCaptureUnderTransitionAsync` calls `ResumeAfterOverlayAsync` (with source/attached-device proof) and then `RequestControllerPresentationReconcile("OverlayCaptureReleased")`. Its completion path already calls `ReconcileMotionReadersAsync("PresentationReconcile:OverlayCaptureReleased")` in `ReconcileControllerPresentationAsync`'s `finally`. Use that existing path to re-enable readers **only when presentation actually is healthy/live**; do not start readers simply because the Overlay surface disappeared.

If Overlay release determines the DirectInput source is unavailable, readers must stay canceled until existing physical/PnP recovery re-admits a live presentation. If suspend remains active, readers stay off until the existing Suspend/Resume path clears it. If the source/model is unsupported or an I/O stop is deferred by a blocked reader, use the current `MsiClawMotionSource` bounded policy; a sensor lifecycle warning must **not** become a controller routing/Overlay failure.

Because stop/restart is already serialized by `MsiClawMotionSource._lifecycleGate`, normal rapid Overlay toggle should not require a new operation epoch. Avoid attempts to solve purely theoretical scheduling interleavings; review only realistically reachable Overlay open/close and sensor cancellation.

### Tests

Add focused deterministic coverage in the **existing** test classes (not a new test project):

1. Host Overlay success path: actual/structural proof that `ReconcileMotionReadersAsync("OverlayCaptureCommitted")` runs **after** successful `PauseForOverlayAsync` and capture commit, and that the code does **not await a 3-second `StopAsync`** while `_visibleSurfaceTransition` is held. The established source-contract test method `Overlay_initial_quick_settings_refresh_uses_the_profile_priority_after_capture_commit` provides an appropriate insertion-order precedent.
2. Logic for `ShouldRunMotionReaders`: live Deck/Edge true; stopped publisher or Suspend true -> false; Xbox360 -> false. Include paused Overlay cases via publisher stopped, not by a new Overlay-specific mutable state.
3. Sensor-source behavior using existing injectable/fake readers: calling stop invalidates `LatestState` immediately, cancels workers without disturbing physical controller reader, and existing start may resume after a completed stop. If the existing tests already cover the generic stop/restart semantics, reuse them rather than duplicating.
4. On clean Overlay release, assert the existing path requests presentation reconcile, whose `finally` owns motion restart. With source absent or Suspend still active, no reader restart. Include one deterministic quick open/close sequence to check no permanent lost-start path; keep test schedules realistic and fast.
5. Do not enforce real gyro hardware or sensor-power measurements in CI. Existing Full1902/Overlay, SteamDeck/Edge and lifecycle tests must remain green.

## 5. Commit 3 — Four-document, source-grounded alignment

The document commit must edit **exactly** these four files. Preserve historic phase/PR descriptions explicitly labeled as historical; correct misleading **present-tense/current** claims instead of rewriting every old architecture paragraph.

### 5.1 `docs/VIIPER_INTEGRATION.md`

- Fix top-of-file production authority and current status: `MsiClawAddonPresentation` -> **one** `CanonicalViiperRuntime`; Windows X360; Steam/BPM selects **Steam Deck (default) or DualSense Edge (experimental)** by Settings preference. **One attached device and publisher at a time**, with one shared server/bus. The normal initial runtime creates detached Deck+X360; **Edge logical device is lazy**, created only upon actual request.
- Remove current assertions such as “PR-A adds no production caller” and “Edge remains dormant and unselected until a later integration step”. PR-A (#754) **and PR-B (#755)** are merged; PlayStation output is now a selectable production path, not yet proof of hardware/Steam Input compatibility.
- Refresh the **Current** process/lifetime model from Deck-only route attach/detach wording to presentation-neutral one-owner operation; retain the valid details of the Deck-specific typed interface in dedicated historical/Deck sections where helpful. Do not resurrect the removed `AddonRoutingRuntime` or Game Bar foreground watcher.
- Record that Edge callback forwards valid **ordinary two-motor rumble only** to the physical rumble sink; LED-only, adaptive trigger, audio and specialized haptics are ignored. Existing Deck haptic behavior is a different mode and must not be accidentally generalized to Edge.
- Preserve pinned commit `0689d18416cf6cde9ba4fed04eb373a1a3383ddc` and existing dependency automation policy.

### 5.2 `docs/VIIPER_MIGRATION_TODO.md`

- In the introductory **current-status** block, correct “one X360 or Steam Deck publisher” to “one X360, Deck, or Edge publisher”; Windows X360, Steam Deck default for Steam/BPM, Edge optional via Settings.
- Fix similarly outdated “current Steam Deck only” wording and reflect that Edge typed backend + selection was completed in PR #754/#755. Clarify existing Steam Deck `28DE:1205` identity is **Deck-only**, not Edge's.
- This roadmap contains extensive historical phases/superseded routing references: preserve their chronology and mark clearly as historical, do **not** attempt a whole-document cleanup, revive superseded actors or silently upgrade the hardware validation state.

### 5.3 `src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md`

**Keep exact artifact identity unchanged**:
- repository `onehoon/VIIPER`, pinned SHA `0689d18416cf6cde9ba4fed04eb373a1a3383ddc`;
- generated header SHA-256 `2e70012583747384c18f7229e270f5dfc092a79d0625e6f0bc999efe6e5dbde0`;
- DLL SHA-256 `e7eb94adb4df0347928507f28bcd7491029663ff47d06cd3d85c00899c718015`;
- existing build attestation, automation marker comments, license and lock consistency.

Within the **existing** `<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->` / `<!-- AUTOMATION: END MANAGED ABI REVIEW SECTION -->` delimiters, **replace only the “review this later / Replace this paragraph” placeholder** with a real, **source-grounded review record** for the currently pinned generated header versus managed C# contracts. Cite the actual checked-in `Dependencies/Viiper/libVIIPER.h`, `CanonicalViiperNativeApi.cs`, `CanonicalViiperNativeTypes.cs`, and `CanonicalViiperNativeAbiTests.cs`.

Document, *to the extent verified by those files*:
- generic classified `AttachUSBDeviceEx`/`DetachUSBDeviceEx` and `GetUSBDeviceAttachmentState`, typed Deck and Xbox360 exports/callbacks remain supported;
- Edge typed exports `CreateDualSenseEdgeDevice`, `SetDualSenseDeviceState`, `SetDualSenseOutputCallback`, `RemoveDualSenseDeviceEx` are in the pinned artifact, lazily bound and **optional in Addon `RequiredExports`** so an old DLL doesn't break normal startup;
- `DSDeviceHandle` is pointer-width, `DSDeviceState` by value is x64 **36 bytes** in native-aligned sequential field layout (critical fields `Buttons@4`, `DPad@8`, `GyroX@24`, `AccelX@30`), `DSDeviceRemoveResult` is classified 0/1/2/3;
- `DSOutputCallback` is `cdecl`, **handle + six byte arguments** (small, large, RGB, player), with only ordinary two-motor vibration used by Addon; LED values unused;
- distinguish checked-in source/ABI contract evidence from any claims of hardware behavior, Sony recognition or historic "all changes since previous ABI" assertions not supported by compared headers. **Do not invent a header delta or claim physical tests passed.**

Update adjacent **current integration summary** from “Steam Deck only” to Deck + X360 + optionally selected Edge; update hardware-validation caveat accurately. Keep verification scripts passing.

### 5.4 `docs/DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md`

- Update top **Status** and §11 PR-B heading from “implemented on a topic branch for review” to **merged PR #755**, alongside already-merged PR-A #754 (VIIPER dependency separately updated in PR #753).
- Preserve that the Settings standalone card exists, default Steam Deck, Edge experimental, Windows mode Xbox360, single canonical owner, device is lazily created, and **physical Edge recognition/gyro/PS/QAM/rumble results remain post-merge user validation** unless there is specific user-provided evidence. The user **has confirmed both stick Y axes were reversed**; it is safe to mention this bug being fixed by the present follow-up PR without claiming all Edge hardware tests completed.
- Change only stale staging/status references and direct contradictions. Do not rewrite the entire originally proposed architecture or retcon PR-A's original scope.

## 6. Checklists and completion criteria

### Automated verification

Run the normal repo solution build and existing managed test suites, targeted at minimum:

- `DualSenseEdgeDeviceStateMapperTests`;
- `MsiClawMotionSourceTests`;
- `AddonProcessHostOverlayQuickSettingsContractTests`;
- existing `MsiClawAddonPresentationTests`, system-pulse, Edge rumble and canonical VIIPER runtime tests.

Verify dependency consistency with the repository's normal `scripts/verify-viiper.ps1` / CI path, but **do not regenerate, update, or re-adopt any VIIPER artifact** just to make verification pass. Inspect actual CI workflow commands before choosing PowerShell/.NET invocations. Tests must not need PID1902 hardware or real Steam Input.

### Merge readiness

- One PR with **three reviewable commits** (stick fix, Overlay reader lifecycle, docs), no code changes in docs commit.
- Up/down movement is correct on **both** Edge sticks; Xbox360, Deck, horizontal stick axes and all other Edge mappings untouched.
- Successful Deck/Edge Overlay capture promptly cancels motion-reader work while preserving the sole physical controller reader and neutralized presentation, without Overlay show/hide waiting for sensor-worker join.
- Overlay close recovers readers **through existing** successful live-presentation reconcile; failed source, Suspend and unsafe owner remain neutral/stopped. No new manager/gate/retry/state machinery.
- Four documentation files accurately reflect merged PR #754/#755, optional/lazy Edge and pinned ABI, without changing native dependency hashes or erasing historical records.
- Tests/build pass, and no real lifecycle failure or recovery contract is weakened.

**Physical verification remains the user's post-merge role.** Local Codex must not require a Claw, Sony controller, Steam game, gyro pose, or live vibration test before opening the PR or as a merge condition.

**Code-review policy:** block only realistic production defects or regressions; do not invent pathological timing-only races, duplicate authority, extra wrappers/locks, or unsupported multi-user/RDP lifecycle requirements.
