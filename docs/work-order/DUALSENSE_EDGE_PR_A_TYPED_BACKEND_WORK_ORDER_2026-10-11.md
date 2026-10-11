# Work Order — DualSense Edge PR-A: Typed Backend Foundation (Full1902)

**Project:** Standalone SteamAddonforClaw, Full PID1902  
**Target:** `onehoon/SteamAddonforClaw` / current `main`  
**Implementation owner:** Local Codex  
**Hardware verification:** User, after merge (never a PR/CI blocker)  
**Scope:** One focused Addon backend PR. **Do not implement PR-B UI/persistence/presentation-selection behavior in this PR.**

## 0. Firm product decisions / scope changes

1. Implement a **virtual DualSense Edge** backend as the third *possible* presentation under the **existing** canonical VIIPER owner. It is not another physical-controller authority, bus, service, or Xbox controller.
2. **The user will update the VIIPER dependency independently.** This work order **explicitly excludes** fetching/updating `libVIIPER.dll`, copying/updating generated `libVIIPER.h`, editing `viiper.lock.json` or `PROVENANCE.md`, release hashes, binary packaging, and dependency acquisition. Do not open a separate dependency PR or make dependency refresh a Codex acceptance requirement. Existing header declarations may be read to confirm C ABI; Go fork source is another reference.
3. PR-A must **not change active Steam/Xbox output selection**. No settings schema, frontend RPC, UI, developer-menu toggle, live preference switching, WING/OEM1 dispatch change, presentation-kind resolver or badge change in PR-A. Current Xbox360 for Windows and Steam Deck for Steam/BPM continue as before.
4. **PR-B product/UI change (decided after the original architecture document):** user-facing selection belongs in a **standalone option card on the ordinary Settings page**, not Developer Menu. Default: **Steam Deck** (the current Steam-facing output). The alternative is **DualSense Edge (Experimental)**. The saved choice will affect *only* Steam/BPM; non-Steam stays Xbox360. PR-B owns all setting/UI/application of that preference, PS/QAM pulses and actual selection integration. Do not pre-create this card in PR-A.
5. VIIPER fork PR #58 now intentionally delivers **only valid classic-rumble v1/v2 updates** through the existing seven-argument DualSense output callback: LED-only reports produce no callback, LED args remain zero, and a flagged two-motor `0,0` is a real STOP. Do not reconstruct LED output or cache/replay previous rumble.
6. Native physical PID1902, existing persistent HidHide, MSI Center M reboot-bound authority, DirectInput ownership, A2VM-specific boot/session-end handling, Steam/Overlay UI and the current Xbox360/SteamDeck semantics are unchanged. **No CTW dependency/integration.**

## 1. Required architecture and inspected current-code baseline

Read these first and resolve conflicts by the current Full1902 README authority order:

- [Full1902 document authority](../Full%201902%20Implementation/README.md)
- [HidHide/startup authority policy](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md)
- [Reboot-bound ownership/lifecycle](../Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md)
- [Full1902 physical/controller owner](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md)
- [Current Edge architecture](../DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md) — use technical Edge mapping and lifecycle guidance; **this work order overrides its older Developer Menu and DLL-refresh staging assumptions**.
- [Fork Typed ABI](https://github.com/onehoon/VIIPER/blob/main/lib/viiper/dualsense.go), [fork state/flags](https://github.com/onehoon/VIIPER/blob/main/device/dualsense/state.go), [fork constants/IMU scale](https://github.com/onehoon/VIIPER/blob/main/device/dualsense/const.go), and [fork PR #58](https://github.com/onehoon/VIIPER/pull/58).

Inspected implementation paths on Addon `main`:

| Source | Current responsibility | Narrow PR-A change |
| --- | --- | --- |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeApi.cs` | Native exports, ABI delegates, per-handle native callback root, shared `_deviceOwnership` | Add optional/lazy Edge Typed native binding, Edge callback root, classified Edge remove; preserve existing required-export startup |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs` | Existing Xbox/Deck callback and state structs | Add `DSDeviceState` and typed output callback with exact C layout |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs` | One server/bus, eagerly created detached Xbox360/Deck, serial native mutations, classified cleanup phases | Add **lazy** Edge owned handle, attachment methods and complete final teardown to same owner |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckSession.cs` | Small attached-state/neutral/callback/detach session | Follow the same ownership discipline for an Edge session, without a second runtime |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs` | Deck-specific buttons, M1/M2, motion scaling | Keep intact; add separate Edge mapper |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` | Proven monotonic absolute-deadline ~250Hz producer, native timer, stop/join and test tick seam | Reuse its proven timing/stop contract for an Edge publisher; avoid new scheduling architecture |
| `src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs` | Xbox360/Deck adapters, physical STOP and bounded dead-man, callback drain | Add minimal Edge ordinary-two-motor adapter reusing those safety conventions |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs` | **Single** active-kind owner and producer/gate; currently enum only Xbox360/SteamDeck | Do not enable Edge as selectable yet. Only add a narrow Edge backend seam if necessary for deterministic integration in PR-B; avoid a half-enabled third presentation |
| `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml` | Current settings cards plus Developer Menu link | **No PR-A edit**; PR-B adds a dedicated SettingsCard here |

Existing baseline in `MsiClawAddonPresentation`: `AttachInitialAsync` and `ReconcileDesiredPresentationCoreAsync` select via `snapshot.WantsSteamDeck`; `ArmFeedbackForActivePresentationLocked` binds Xbox360/Deck; `RetireActivePresentationCoreAsync` stops/joins the producer, clears output pulse, drains/disarms rumble, requests physical STOP and classified-detaches the active device. **Do not alter those production decisions until PR-B can update all routes together**.

## 2. Native Edge ABI — precise types and binding

Add the existing VIIPER Edge Typed functions to `ICanonicalViiperNativeApi` and its implementation, using consistent `cdecl` delegate definitions:

```c
GoUint8 CreateDualSenseEdgeDevice(
    USBServerHandle serverHandle, DSDeviceHandle* outDeviceHandle,
    GoUint32 busID, GoUint8 autoAttachLocalhost,
    GoUint16 idVendor, GoUint16 idProduct, DSMetaState* meta);
GoUint8 SetDualSenseDeviceState(DSDeviceHandle handle, DSDeviceState state);
GoUint8 SetDualSenseOutputCallback(DSDeviceHandle handle, DSOutputCallback cb);
DSDeviceRemoveResult RemoveDualSenseDeviceEx(DSDeviceHandle handle);
```

- Both handles are native pointer-width `nuint`. Keep `autoAttachLocalhost = false` and VID/PID = `0,0` to request VIIPER's Edge defaults (Sony **054C:0DF2**). `DSMetaState*` is **nullable pointer**, pass `nint.Zero` for default metadata. Do not accidentally pass the metadata struct by value.
- Output callback is `void(DSDeviceHandle handle, byte rumbleSmall, byte rumbleLarge, byte ledRed, byte ledGreen, byte ledBlue, byte playerLeds)` (**handle plus six bytes**) with `CallingConvention.Cdecl`. Preserve the native ABI; application ignores the LED arguments.
- `DSDeviceRemoveResult`: `Success=0`, `RetryableFailure=1`, `UnsafeOutcomeUnknown=2`, `Invalid=3`. Use the existing generic `AttachUSBDeviceEx`, `DetachUSBDeviceEx`, `GetUSBDeviceIdentity`, `GetUSBDeviceAttachmentState`; do not invent an Edge-specific attach API.
- The native `DSDeviceState` is **passed by value**, sequential default native alignment. Keep all fields, including unused touch and IMU fields:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct DSDeviceState
{
    internal sbyte LX, LY, RX, RY;
    internal uint Buttons;
    internal byte DPad, L2, R2;
    internal ushort Touch1X, Touch1Y;
    internal byte Touch1Active;
    internal ushort Touch2X, Touch2Y;
    internal byte Touch2Active;
    internal short GyroX, GyroY, GyroZ;
    internal short AccelX, AccelY, AccelZ;
}
```

- On x64, expected `Marshal.SizeOf<DSDeviceState>() == 36`, with field offsets: `Buttons=4`, `DPad=8`, `L2=9`, `R2=10`, `Touch1X=12`, `Touch1Y=14`, `Touch1Active=16`, `Touch2X=18`, `Touch2Y=20`, `Touch2Active=22`, `GyroX=24`, `GyroY=26`, `GyroZ=28`, `AccelX=30`, `AccelY=32`, `AccelZ=34`. **Do not use the Go TCP wire format's 33-byte size as this C ABI size.**
- Keep the active `CanonicalViiperNativeApi.RequiredExports` contract for existing Deck/Xbox **startup** compatible with the existing packaged DLL. Resolve the **new experimental Edge exports only when first Edge use is requested**, or use another equally narrow optional-availability approach that cannot break default startup. Never swallow a missing export as a successful Edge attach. After the user updates the dependency separately, Edge can be activated by PR-B. Do not replace the global one-time VIIPER DLL loader with another loader or require a second module.
- Register and root Edge's callback in the **existing** `_callbackGate` / `_deviceOwnership` discipline. Keep root until a confirmed native clear/remove or proven owning bus/server teardown, even when a native clear fails. A confirmed `RemoveDualSenseDeviceEx` releases the exact device ownership/root, as do proven owning parent removals. Do not free a callback root while native can invoke it.
- Preserve `GetUSBDeviceIdentity`'s actual semantics: it proves bus/device ID, **not** Sony VID/PID. The default VID/PID comes from VIIPER. Do not claim Windows PnP recognition in unit tests.

## 3. Lazy Edge logical-device ownership and cleanup

Extend `CanonicalViiperRuntime` as the only native owner, without changing normal two-device initialization:

1. `TryInitialize` must continue creating **only** detached Steam Deck and Xbox360; default startup does **not** create/attach Edge or depend on Edge exports.
2. Provide one explicit **lazy ensure/create** operation which runs under the existing `_serial` and is called on the first *actual* Edge demand (PR-B), or directly in PR-A tests. If already proven created/detached, reuse the handle rather than creating another. Record handle, logical-device identity and existence in the **same runtime**.
3. On successful create, verify same owning `BusId`, and query `GetUSBDeviceAttachmentState == Detached` before exposing the handle as attach-ready. Treat query failure/OutcomeUnknown/invalid classification as unsafe and retain the owned handle and evidence. Do not present a detached assumption as proof. Handle a surprising known-Attached state via the existing neutral/classified-detach policy, not an unclassified blind cleanup.
4. If Edge creation is unavailable or fails **before** native ownership is acquired, report Edge unavailable **without harming an already Ready Xbox360/Deck runtime**. Once Edge native ownership may exist, never silently discard that handle after a later failed check.
5. Provide narrow counterparts to `TryGetDeckAttachmentState`, `AttachDeck`, `SetDeckState`, `SetDeckOutputCallback`, and `DetachDeck`. All mutations are classified; `RetryableFailure` follows existing explicit lifecycle handling, `UnsafeOutcomeUnknown` and `Invalid` are fail-closed, no extra retry loops.
6. Add Edge to existing `TeardownAsync` path **before** bus/server destruction: when created, query/neutral/detach if attached, classified `RemoveDualSenseDeviceEx` only when known safe. On retryable cleanup leave the current same owner with `CleanupPending` and a resumable phase. On unsafe/unknown stop native mutation; never forget the native handle. Do not add a second teardown owner. Ensure existing staged-init unwinds and no-Edge teardown keep their previous ordering/result.
7. Keep the single-attachment guarantee at the eventual PR-B owner boundary. PR-A tests must prove Edge operations do not attach Xbox360 or Deck and cannot bring up a second bus/server. The PR-A backend must not create an independent presentation authority.

Implement a thin `CanonicalDualSenseEdgeSession`/typed state sink only to the extent useful for publishing and deterministic tests, following `CanonicalSteamDeckSession`. No generic interface hierarchy or third-party framework solely for Edge.

## 4. Edge input mapper and typed publisher

Add `DualSenseEdgeDeviceStateMapper` based on existing `ControllerState`, `AuxiliaryButtonState`, and `MsiClawMotionState`.

| Physical input | Edge output |
| --- | --- |
| A / B / X / Y | Cross `0x20` / Circle `0x40` / Square `0x10` / Triangle `0x80` |
| LB / RB | L1 `0x100` / R1 `0x200` |
| L2/R2 full-pull digital | L2 `0x400` / R2 `0x800` button bits, independent of analog value |
| Trigger axes | `L2/R2` `byte` 0–255, no Deck-style 0–32767 mapping |
| D-Pad | `DPad` bitfield Up=1, Down=2, Left=4, Right=8 (preserve diagonals) |
| Left/right stick | `LX/LY/RX/RY` signed int8, bounded conversion of existing `short`, center=0, extremes -128/+127 |
| L3 / R3 | `0x4000` / `0x8000` |
| Start/Menu | Options `0x2000` |
| Back/View | Create `0x1000` |
| M1 / physical RightRear | Edge R4 `0x00800000` |
| M2 / physical LeftRear | Edge L4 `0x00400000` |
| PS / Edge LFn/RFn / Touchpad / Mic Mute | Neutral in PR-A; system pulses implemented in PR-B |

- Use the existing rear-button **suppression provider** so a held rear button does not become an accidental new R4/L4 press after a presentation switch. Never apply Xbox-only M1/M2 configurable remap to native Edge paddles.
- Keep axes orientation faithful to the current normalized physical source; test center, extrema, quadrants and negatives. For symmetric signed mapping choose one documented deterministic rounding/clamping policy, e.g. negative full-scale maps -32768 -> -128 and positive 32767 -> 127; do not cast/truncate directly.
- Encode fresh/valid gyro in signed raw counts at **16.384 units per degree/s**; accel in signed counts at **8192 units per g** (VIIPER reference `835.07` units per m/s²). Round/clamp all six values to int16. Use **Edge-specific** explicit axis/sign permutation with documented reference poses, not an unexamined copy of Deck mapping. Common `MsiClawMotionSource` normalization stays unchanged. If complete motion snapshot unavailable, non-finite or stale (gyro 250ms/accel 500ms using existing `WithFreshness`), write **all zero Edge motion fields** and keep ordinary gamepad input. Do not synthesize `-8192` resting gravity for unavailable sensors.
- PR-A publisher must support deterministic test ticks and production **absolute-deadline / high-resolution one-shot ~250 Hz** pattern already proven in `CanonicalSteamDeckInputPublisher` (worker join before detach, fault callback on rejected `SetState`, neutral on stop/retirement). Reuse `WindowsHighResolutionOneShotTimer`, `CanonicalPublisherDeadlineMath`, `PublisherThreadQoS`; **do not add `Task.Delay(4)` production polling**, duplicate hundreds of lines of Deck-only diagnostics, or refactor both working publishers just to deduplicate a small loop. Implement the smallest concrete Edge publisher using the existing timing conventions and test seam.
- PR-B will add Edge PS/PS+Cross pulse overlay and make the runtime select this publisher. PR-A must not synthesize system-button presses yet.
- Add no new motion reader or duplicate sensor polling. PR-B will extend the currently Deck-specific runtime motion-reader activation policy to include Edge when actually live.

## 5. Ordinary rumble — no LED or stale state

Provide a minimal `DualSenseEdgeRumbleFeedbackAdapter` or a small reuse of existing `Xbox360RumbleFeedbackBridge` mechanics; do not add a new feedback authority.

- Receive VIIPER `DSOutputCallback(handle, small8, large8, ...ignoredLED)`. Map large/low-frequency to `TwoMotorRumble.LargeMotor` and small/high-frequency to `TwoMotorRumble.SmallMotor`. Expand 8-bit to 16-bit using existing `value * 257` convention.
- Only callback events generated by the **new rumble-only VIIPER fork** reach this adapter. Do not use cached motor state, replay on attach, reinterpret LED bytes, start any LED controller, or connect to the physical joystick LEDs.
- An explicit callback `small=0, large=0` must request physical STOP. Reuse a **bounded dead-man STOP** for nonzero rumble (same practical 5-second window as Xbox360) to guard dropped terminal updates.
- The existing physical `IPhysicalRumbleSink` is the sole physical writer; callback errors must not escape native C ABI. Preserve callback disposal/write drain order so an already-running nonzero physical write cannot land **after** the final retirement STOP.
- PR-A may validate/arm the adapter through a unit-test seam or a not-yet-selected Edge session. Actual `MsiClawAddonPresentation.ArmFeedbackForActivePresentationLocked` wiring occurs in PR-B when the third kind is actually selected. Don't weaken existing Deck/Xbox STOP, suspend and Windows session-end policy.

## 6. Mandatory deterministic tests / implementation evidence

Modify/add focused tests under `tests/SteamInputAddonforClaw.Tests`, reusing existing `CanonicalViiperNativeAbiTests.cs`, `CanonicalViiperRuntimeTests.cs`, `CanonicalSteamDeckSessionTests.cs`, `CanonicalSteamDeckDeviceStateMapperTests.cs`, publisher and rumble tests. Extend native fakes to satisfy any interface additions.

Required test cases:

1. x64 DS state **36-byte struct size** and all critical offsets, full `DSOutputCallback` delegate arity/ABI, typed export names, classified Edge remove-result enum values. Existing Deck/Xbox ABI tests unchanged.
2. The **current packaged DLL** and its old required-export set must not be rejected by normal `CanonicalViiperNativeApi` initialization solely because Edge optional exports are absent. Missing Edge symbols produce an Edge-unavailable result only when Edge is actually requested; never call a null delegate.
3. Default runtime creation call order and two detached devices unchanged; **zero** Edge create/attach calls on ordinary initialize/Deck/Xbox operations.
4. First Edge ensure creates **once**, tracks one typed handle on the existing server/bus, proves Detached, reuses it on subsequent explicit requests; failed creation before ownership leaves default runtime usable. Bad bus identity, query failure, known-Attached surprise, OutcomeUnknown and invalid classification follow safe unwind/retention/fail-close rules.
5. Edge attach/detach/status, successful removal and retryable/unsafe classifications obey the existing contract. Final runtime teardown includes Edge **before** bus/server; a failed removal cannot silently close parent; no Edge created means previous teardown remains unchanged.
6. Buttons/A-B-X-Y masks, independent R4/L4, both bumpers/triggers, D-Pad diagonals, independent L2/R2 digital full-pull, short-to-sbyte stick boundaries, rear-button suppression, neutral unmapped features.
7. All six IMU fields for complete fresh finite snapshot, round/clamp, sign/permutation reference cases, stale/unavailable/non-finite -> zero motion with ordinary input preserved.
8. Bounded publisher start, heartbeat/failure semantics, deterministic ticks, failed native state rejection, **stop/join before any native detach**; no unintended publisher created during default startup.
9. Native callback root remains while armed; confirmation clears it; failed clear retains root until proven ownership retirement. Rumbling callbacks are mapped correct left/right, LED args ignored, explicit 0/0 STOP and bounded dead-man. Dispose drains a real callback before subsequent physical STOP.
10. Prior Xbox360, Steam Deck, existing physical-source acquisition, suspend/restart/session-end and canonical runtime tests continue passing. No test needs real PID1902 hardware or Sony PnP enumeration.

Run the repository's existing .NET build, targeted tests and regular unit/CI checks appropriate to the changed source. Keep `dotnet` tests deterministic. If a checked-in DLL lacks optional Edge symbols, **do not mark that as a PR-A failure** or temporarily replace the dependency with an untracked local DLL. The user owns the dependency update. Do not require a Windows physical controller, Steam Input, Game Bar or MSI Claw hardware for Codex/PR acceptance.

## 7. Acceptance and integration handoff

PR-A is complete when:

- Existing Windows= Xbox360 and Steam/BPM=Steam Deck routing is **bit-for-bit unchanged** in the production selector, and ordinary app cold start never requires Edge availability.
- Edge's typed ABI, lazy same-owner native lifetime, reporter/publisher, input/IMU mapper and safe ordinary rumble adapter are implemented and unit-tested, **but not enabled for users yet**.
- All real native lifecycle failure classes have safe behavior: known retryable cleanup preserves owner for the existing lifecycle boundary; unknown/invalid outcome stays fail-closed; callback root and physical feedback cleanup are not abandoned. Do not introduce speculative manager/state/epoch/lock machinery for pathological races.
- No dependency binary/lock/header/provenance changes in PR-A, no Settings/Developer UI or preference code, no new PID1902/HidHide/physical ownership actions.
- The PR description explains the implemented Edge seam and clearly lists what PR-B must connect: **Settings standalone controller choice card (Steam Deck default), persisted choice, policy resolver, actual presentation switch, FrontButton Steam domain, Deck vs Edge PS/QAM, motion-reader activation, badge, every live lifecycle path**.
- Local Codex provides code/test results. **Physical verification is exclusively the user's post-merge responsibility and never a PR blocker.**

**PR-B UI wording to preserve:** Settings page, separate `SettingsCard` e.g. **“Steam / Big Picture Controller”** with choice **“Steam Deck (Default)”** / **“DualSense Edge (Experimental)”**. Existing settings without a saved choice resolve to Steam Deck. The choice does not affect regular Windows Xbox360 mode.

**Intent:** prepare one safe, dormant Edge backend; leave exactly one existing controller owner, state authority, publisher at a time, and teardown path. Minimize speculative complexity without weakening real handheld lifecycle safety.
