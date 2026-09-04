# Work Order — Full1902 Production Rumble Feedback

## Status

Single focused production PR.

Code-review baseline used for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     95402bb792cbd7ad7bd6ef51b4e166d8fadd7f72
latest merged PR: #487 — App UI PR-B
```

Read these first and follow the authority order defined by the Full1902 documents:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`
- `docs/work-order/FULL1902_CLEANUP_J_RETIRE_LEGACY_VIBRATION_FEEDBACK_TRANSPORT_AND_ROUTING_NAMING_TAIL_WORK_ORDER.md`

This work order is based on the current post-Cleanup-A-through-J production architecture. Do not resurrect deleted routing-era ownership or feedback architecture.

---

# 1. Goal

Implement production controller rumble for both Full1902 virtual presentations:

```text
Steam/BPM inactive -> Xbox360 presentation -> physical MSI Claw rumble
Steam/BPM active   -> SteamDeck presentation -> physical MSI Claw rumble
```

Use the current Full1902 owners only:

```text
MsiClawAddonPhysicalOwnership
        -> owns verified PID1902 + DirectInput + HidHide state

MsiClawAddonPresentation
        -> owns one CanonicalViiperRuntime
        -> owns exactly one active virtual presentation + publisher
        -> switches Xbox360 XOR SteamDeck
```

The required production feedback flow is:

```text
                    MsiClawAddonPresentation
                     /                   \
             Xbox360                     SteamDeck
                |                           |
   VIIPER Xbox360 rumble cb       VIIPER Deck output cb
                |                           |
                |                  SteamDeckRumbleDecoder
                |                           |
                +------------+--------------+
                             |
                       TwoMotorRumble
                             |
                      MsiClawRumbleSink
                             |
                verified current PID1902 HID
                             |
                      physical motors
```

The PR must not change the Full1902 authority model, Steam/BPM presentation policy, HidHide policy, PID1901/PID1902 policy, or physical recovery policy.

---

# 2. Current production reality

## 2.1 Legacy routing feedback is already removed

Do not treat rumble as a legacy-routing migration.

Cleanup A removed the old production Steam-routing authority, including the old route-owned output stage. Cleanup J later removed the disconnected routing-era feedback authority/bridge/session transport.

Current production must not reintroduce:

```text
AddonRoutingRuntime
RoutingPipeline*
CanonicalSteamDeckOutputStage
FeedbackAuthority
FeedbackAuthorityToken
FeedbackAuthorityLease
SteamDeckRumbleFeedbackBridge
Developer vibration session/RPC ownership
```

Useful low-level rumble primitives were deliberately retained for this future Full1902 integration.

## 2.2 Xbox360 input is already production-live

`MsiClawAddonPresentation.AttachXbox360Async(...)` currently:

```text
verify persistent typed X360 is detached
-> AttachXbox360()
-> SetXbox360State(default) neutral
-> create/start CanonicalXbox360InputPublisher
-> commit active Xbox360 presentation
```

No production Xbox360 rumble callback is currently bound.

## 2.3 SteamDeck input is already production-live

`MsiClawAddonPresentation.AttachSteamDeckAsync(...)` currently creates the canonical SteamDeck session, writes neutral, and starts the canonical SteamDeck input publisher.

The canonical managed VIIPER surface already supports:

```text
SetSteamDeckOutputCallback
```

and roots the managed callback while native VIIPER owns the function pointer.

However, current Full1902 production presentation code does not register a SteamDeck output callback, so production SteamDeck rumble is currently disconnected.

`SteamDeckRumbleDecoder` remains as a stateless reusable decoder.

## 2.4 Native Xbox360 rumble ABI already exists

The pinned VIIPER header already exposes:

```c
SetXbox360RumbleCallback(Xbox360DeviceHandle handle, Xbox360RumbleCallback cb)
```

Therefore this PR must NOT modify the VIIPER native dependency solely to obtain Xbox360 rumble support.

The missing piece is the Addon's managed ABI binding and Full1902 production composition.

---

# 3. Product / architecture invariants

Preserve all of the following.

## 3.1 Controller authority

```text
Center M Enabled
-> MSI / stock authority
-> PID1901
-> no Addon virtual controller presentation

Center M Disabled
-> Addon Runtime authority
-> desired PID1902
-> persistent DirectInput ownership
-> persistent exact-target HidHide baseline
-> one canonical VIIPER runtime
-> exactly one attached virtual presentation
```

Rumble must never decide controller authority.

## 3.2 Presentation policy

```text
Steam/BPM inactive -> Xbox360
Steam/BPM active   -> SteamDeck
```

Rumble follows the active presentation. It does not create another presentation-selection authority.

## 3.3 One virtual-presentation owner

`MsiClawAddonPresentation` remains the one production owner for:

```text
active presentation kind
publisher lifetime
SteamDeck session lifetime
VIIPER attach/detach for presentation switching
presentation fail-close cleanup
Overlay pause/resume presentation state
```

Feedback callback lifetime must be integrated into this same owner.

Do not add:

```text
RumbleManager
FeedbackManager
Full1902FeedbackAuthority
FeedbackLeaseManager
RumbleSessionManager
presentation epoch/generation manager
new state-machine framework
```

## 3.4 One physical rumble writer

Reuse the existing:

```text
MsiClawRumbleSink
MsiClawRumblePacketBuilder
IPhysicalRumbleSink
TwoMotorRumble
PhysicalRumbleWriteResult / PhysicalRumbleWriteStatus
existing MSI rumble endpoint resolver / transport
```

Do not create separate physical writers for Xbox360 and SteamDeck.

---

# 4. Explicitly out of scope

Do NOT implement or reconnect any of the following in this PR:

```text
Developer Vibration Test
Developer vibration RPC/session transport
Vibration Test UI behavior beyond leaving it intentionally unavailable
user-facing vibration-strength setting
Controller-page vibration-strength UI
per-game rumble strength
new haptic-effects UI
VIIPER native changes
Center M authority changes
PID1901/PID1902 policy changes
HidHide policy changes
Steam/BPM detection changes
Overlay capture redesign
front-button mapping changes
```

The Developer Vibration Test was intentionally disconnected by Cleanup J. Do not use it as a compatibility requirement and do not reconnect it indirectly while implementing production feedback.

---

# 5. Managed Xbox360 VIIPER ABI binding

Files expected to participate:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeApi.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs
```

Fresh-read the pinned `libVIIPER.h` before editing and mirror its exact callback typedef/calling convention.

## 5.1 Add the managed delegate

Add the minimal delegate matching the pinned Xbox360 native callback.

Conceptually:

```csharp
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void Xbox360RumbleCallback(
    nuint handle,
    byte leftMotor,
    byte rightMotor);
```

Do not blindly use this sample if the pinned header differs. The checked-in header is authoritative.

## 5.2 Extend `ICanonicalViiperNativeApi`

Add only the missing typed API:

```text
SetXbox360RumbleCallback
```

Keep the canonical Xbox360 API typed and explicit.

Do not expose legacy `clib` compatibility calls.

## 5.3 Require and bind the export

Add:

```text
SetXbox360RumbleCallback
```

to `CanonicalViiperNativeApi.RequiredExports`, bind it in the constructor, and expose the managed call.

A missing export must continue to fail dependency loading at the existing ABI-validation boundary rather than failing later during a live controller session.

## 5.4 Root the managed Xbox360 callback

Native VIIPER stores the callback pointer after `SetXbox360RumbleCallback` returns. Therefore the managed delegate must remain strongly rooted for exactly as long as native can call it.

Extend the existing callback-root ownership mechanism in `CanonicalViiperNativeApi` rather than creating another lifetime manager.

Preferred shape:

```text
existing _callbackGate
existing _deviceOwnership
existing SteamDeck callback rooting
+
Xbox360 callback roots keyed by logical device handle
```

The native Set/Clear operation and matching managed-root mutation must stay serialized under the existing callback gate for the same reason already documented for SteamDeck output callbacks.

Required cleanup points include successful:

```text
SetXbox360RumbleCallback(handle, null)
RemoveXbox360Device / RemoveXbox360DeviceEx
RemoveUSBBus
CloseUSBServer
```

Do not add another lock for Xbox360 callback ownership.

## 5.5 Runtime forwarding

Add a narrow `CanonicalViiperRuntime` forwarding method for the persistent Xbox360 logical device, analogous to the existing Deck callback forwarding.

Do not make `CanonicalViiperRuntime` decide feedback policy.

---

# 6. Full1902 physical rumble identity source

Current `MsiClawRumbleSink` correctly refuses to write unless it can prove the write belongs to the current physical DirectInput session. It consumes:

```text
IMsiClawPhysicalInputIdentityProvider.CurrentIdentity
IMsiClawPhysicalInputIdentityProvider.CurrentSessionGeneration
```

On current main there is no production implementation of that narrow provider after legacy routing cleanup.

Do not weaken or bypass this safety check.

## 6.1 Source the identity from the existing Full1902 physical owner

`MsiClawAddonPhysicalOwnership` already owns the authoritative acquisition/recovery flow:

```text
stable PID1902 proof
-> bounded DirectInput descriptor selection
-> exact primary PID1902 collection proof
-> PnP node correlation
-> strong physical identity proof
-> StartPrepared(descriptor)
-> first valid input state
-> exact HidHide target commit
```

The selected `DirectInputDeviceDescriptor` already carries the rumble-sink identity fields:

```text
InstanceGuid
DevicePath
PnpInstanceId
PhysicalIdentity
```

Use this already-verified descriptor. Do not perform a second independent controller discovery merely for rumble.

## 6.2 Preferred ownership shape

The existing Full1902 physical owner, or the one live `MsiClawInputSource` object it already owns, should satisfy the narrow existing `IMsiClawPhysicalInputIdentityProvider` contract.

Prefer the smallest implementation that preserves one source of truth.

Recommended direction:

```text
successful StartPrepared(verified descriptor)
-> publish CurrentIdentity from that descriptor
-> advance session generation for the newly committed live DirectInput session

real DirectInput session retirement/loss
-> current identity is no longer usable for normal rumble writes

successful same-source recovery StartPrepared(new verified descriptor)
-> update CurrentIdentity
-> advance session generation
```

Do not create a separate `RumblePhysicalIdentityTracker` or duplicate PnP snapshot flow.

## 6.3 Session generation semantics

`MsiClawRumbleSink` uses session generation to reject a write that resolved an endpoint for an older DirectInput session and then crosses a real loss/recovery boundary.

Preserve that existing contract.

Generation changes should correspond to real physical input-session lifecycle, not virtual X360/Deck presentation switches.

Normal:

```text
Xbox360 -> SteamDeck
SteamDeck -> Xbox360
```

must NOT manufacture a new physical-session generation because PID1902 / DirectInput ownership has not changed.

Do not introduce epoch/barrier machinery beyond the narrow generation value already required by `MsiClawRumbleSink`.

---

# 7. Production rumble sink composition

Create exactly one `MsiClawRumbleSink` for the live Full1902 owned-controller runtime.

It should be associated with the same process-owned PID1902 physical session that feeds `MsiClawAddonPresentation`.

Preferred ownership relationship:

```text
AddonProcessHost / existing Full1902 composition
    -> MsiClawAddonPhysicalOwnership
    -> one MsiClawRumbleSink bound to that owner's current physical-session identity
    -> MsiClawAddonPresentation consumes the sink for active-presentation feedback
```

The precise constructor seam may follow the current testability pattern, but do not create a new general composition service.

The physical sink remains presentation-agnostic.

---

# 8. Xbox360 production feedback path

Add the smallest presentation-scoped Xbox360 callback adapter/bridge needed to convert VIIPER's already-decoded motor values to `TwoMotorRumble` and write them to the shared physical sink.

A small `Xbox360RumbleFeedbackBridge`-style object is acceptable if it gives a clear disposable callback lifetime and keeps callback code out of the large presentation owner.

It must NOT become an authority manager.

## 8.1 Motor mapping

VIIPER Xbox360 callback semantics are:

```text
leftMotor  -> large/low-frequency motor
rightMotor -> small/high-frequency motor
```

Map to:

```text
TwoMotorRumble.LargeMotor <- leftMotor
TwoMotorRumble.SmallMotor <- rightMotor
```

The callback values are 8-bit `0..255`, while `TwoMotorRumble` is 16-bit.

Use full-range expansion:

```csharp
static ushort Expand(byte value) => (ushort)(value * 257);
```

This preserves exact 8-bit magnitude through the existing physical sink's `>> 8` conversion:

```text
0   -> 0
1   -> 257      -> >>8 == 1
127 -> 32639    -> >>8 == 127
255 -> 65535    -> >>8 == 255
```

Do not change `MsiClawRumblePacketBuilder` scaling to compensate.

## 8.2 Callback behavior

The callback should do only bounded local work:

```text
convert motor values
-> call the shared physical rumble sink
-> contain/log real write failure according to existing policy
```

Do not block native callback delivery on long retries, PnP loops, route reconciliation, or presentation switching.

Do not throw across the native callback boundary.

---

# 9. SteamDeck production feedback path

Reuse:

```text
CanonicalSteamDeckSession.SetOutputCallback / ClearOutputCallback
SteamDeckRumbleDecoder
TwoMotorRumble
MsiClawRumbleSink
```

Do NOT restore the deleted `SteamDeckRumbleFeedbackBridge` verbatim and do NOT restore `FeedbackAuthority`.

Implement the smallest Full1902 presentation-scoped callback path that:

```text
receives raw normalized Deck host-output bytes from VIIPER
-> decodes using SteamDeckRumbleDecoder
-> for supported rumble/haptic packets, writes decoded TwoMotorRumble to the shared sink
-> ignores/non-fatally handles unsupported/non-rumble packets according to decoder result
-> contains exceptions at the callback boundary
```

Any callback-owned disposable object should exist only to keep `MsiClawAddonPresentation` concise and to provide a clear callback teardown point.

Do not invent generic multi-source feedback arbitration. Only one virtual presentation is intentionally attached at a time.

---

# 10. Feedback lifetime belongs to `MsiClawAddonPresentation`

Extend the existing owner rather than creating a parallel owner.

Current presentation state already includes:

```text
_activeKind
_publisher
_deckSession
_gate
_overlayPaused
fault cleanup
```

Add only the minimal fields needed to remember the currently armed feedback callback/adapter and shared physical sink.

The existing `_gate` is the serialization boundary for presentation attach/switch/retire/release. Use it for callback arm/disarm ordering as part of the same lifecycle.

Do not add a second feedback gate unless a concrete current API invariant proves the existing owner gate cannot protect the required lifecycle.

---

# 11. Attach behavior

Feedback failure must be handled deliberately without corrupting controller presentation ownership.

## 11.1 Xbox360 attach

Target sequence, adjusted only as needed to preserve current fail-close rules:

```text
prove X360 detached
-> attach X360
-> write virtual X360 neutral
-> arm X360 rumble callback against shared physical sink
-> start Xbox360 input publisher
-> commit active X360 presentation
```

If callback registration itself fails, do not leave a partially believed callback owner.

Use a simple explicit policy:

> Controller input/presentation availability is primary. A rumble callback registration failure should be logged and leave production rumble unavailable for that presentation rather than tearing down otherwise healthy controller input solely because vibration is unavailable.

If the current implementation/test architecture makes attach rollback materially simpler and safer, a fail-attach policy is acceptable only if it is clearly justified by the existing owner invariant. Do not add retry machinery.

## 11.2 SteamDeck attach

Equivalent target behavior:

```text
start/attach canonical Deck session
-> Deck neutral
-> arm Deck output callback
-> start Deck input publisher
-> commit active Deck presentation
```

Again, rumble callback failure should not cause PID/HidHide/DirectInput mutation.

No fallback to Xbox360 merely because Deck rumble is unavailable.

---

# 12. Presentation retirement and switching

X360 <-> SteamDeck switching is a normal production lifecycle. Old presentation feedback must not remain armed after the old presentation is retired.

Integrate feedback teardown into `RetireActivePresentationCoreAsync(...)` or the smallest equivalent current lifecycle seam.

Required semantic order:

```text
1. stop/join current input publisher
2. stop accepting old presentation feedback / clear native callback
3. best-effort physical rumble STOP
4. dispose presentation-scoped feedback adapter if any
5. neutral/detach the current typed virtual device using existing rules
6. clear active presentation fields
7. later attach desired presentation and arm its callback
```

The exact order of callback-clear vs physical STOP may be adjusted if the implementation proves another ordering is safer, but after successful retirement:

```text
old native callback is not armed
physical motors are requested stopped
no old presentation feedback object remains active
```

Do not add rollback to the old presentation if attaching the desired presentation later fails. Preserve current PR7 fail-close behavior.

---

# 13. STOP policy

A best-effort physical STOP is required at real feedback-retirement boundaries to avoid a user-visible stuck motor.

At minimum request:

```text
TwoMotorRumble.Stopped
```

when retiring an armed presentation because of:

```text
Xbox360 <-> SteamDeck switch
Center M Enable-and-Restart release
process-owned presentation teardown / shutdown
presentation fail-close cleanup after publisher failure
physical-input loss handling that retires/neutralizes the active output
```

If the physical sink reports STOP unavailable because the physical session has already disappeared, log/contain that expected failure and continue the existing fail-close lifecycle. Do not invent a retry daemon.

A rumble STOP failure must not cause unsafe reattachment of an old presentation.

---

# 14. Physical input loss and recovery

Real PID1902 DirectInput loss/recovery is already owned by the current Full1902 physical recovery path.

Do not create rumble-specific physical recovery.

Required behavior:

```text
physical DirectInput session lost
-> current physical identity/session generation invalidates normal writes
-> stale or in-flight rumble write cannot be accepted as current after generation/identity changes
-> existing physical recovery reacquires the SAME input source object with a newly verified descriptor
-> physical rumble identity/session becomes current again
-> active/new presentation callback may then write through the same MsiClawRumbleSink
```

Do not cache and continue using an old rumble HID endpoint across a proven physical-session generation change.

Preserve `MsiClawRumbleSink` stale-session verification.

---

# 15. Suspend / Hibernate / Resume

Do not redesign power lifecycle.

Current Full1902 suspend/resume ownership remains authoritative.

Rumble integration must cooperate with it by ensuring the presentation quiesce/retire path does not leave a motor intentionally running when process-owned output is being neutralized.

On resume:

```text
existing Full1902 physical reconcile/recovery
-> verified PID1902 input source
-> existing presentation reconcile
-> active presentation feedback callback corresponds to the active current presentation
```

Do not add a separate rumble resume watcher, resume epoch, or power manager.

Only add focused integration hooks if the current presentation lifecycle demonstrably requires them.

---

# 16. Overlay capture behavior

Do not redesign OQ4 Overlay capture.

Current Overlay pause keeps the same typed device attached while stopping the publisher and writing virtual neutral.

Rumble policy during Overlay capture should follow the current production intent with minimal complexity.

Preferred safe behavior:

```text
PauseForOverlayAsync succeeds
-> request physical rumble STOP
-> do not detach the virtual device solely for feedback
-> callback may remain registered only if its handler is prevented from re-driving rumble while overlay pause is active
```

or, if simpler with current callback ownership:

```text
PauseForOverlayAsync succeeds
-> clear the active feedback callback
-> request STOP
ResumeAfterOverlayAsync succeeds
-> re-arm callback for the SAME active presentation
```

Choose the smaller implementation after reading the current OQ4 tests and owner structure.

Do not add another state variable if existing `_overlayPaused` plus callback ownership is sufficient.

Whatever option is selected, prove that opening the Overlay cannot leave a pre-existing physical vibration latched indefinitely.

---

# 17. Failure policy

Rumble is an optional output feature layered on top of a safety-critical controller input/presentation path.

Use the following policy unless current code provides a stricter existing contract:

## 17.1 Physical rumble write failure

```text
log/contain
-> no exception across VIIPER callback
-> do not tear down healthy PID1902 / DirectInput / HidHide
-> do not switch virtual presentation
-> do not retry in a loop
```

`MsiClawRumbleSink` already classifies unavailable/stale/disposed/write failures. Reuse those results.

## 17.2 Native feedback callback registration failure

```text
presentation input remains usable where safely possible
-> rumble unavailable for that active presentation
-> log a precise production event
```

Do not create a background callback-registration retry loop.

A later real presentation attach/reconcile boundary may naturally attempt registration again.

## 17.3 Native callback clear failure

This is more serious because native may still hold the callback pointer.

Do not free/unroot a managed callback if native callback clear was not proven successful while the logical device remains live.

Follow the existing `CanonicalViiperNativeApi` callback-rooting invariant: managed callback lifetime must cover every interval in which native may still call it.

At presentation retirement, if clear cannot be proven, use the existing native device detach/remove/VIIPER teardown ownership path to establish a safe endpoint. Do not simply discard the managed root.

Do not add speculative synchronization beyond the real callback ownership requirement.

---

# 18. Native callback concurrency policy

VIIPER callbacks may arrive on native-owned threads.

Protect only realistic lifetime hazards:

```text
managed delegate must remain rooted while native owns it
callback must not throw into native
presentation retirement must stop future callback ownership before final cleanup
physical sink must reject stale physical sessions
```

Do NOT add barriers/epochs/leases solely for instruction-level interleavings such as:

```text
one callback arriving between two individual managed assignments
Steam event changing at the exact instruction callback cleanup begins
arbitrary synthetic simultaneous X360 and Deck callback execution after the old device was already detached
```

The product invariant and existing owner gate should converge to a safe final state under real presentation switching, shutdown, suspend/resume, and physical-device loss.

---

# 19. Required tests

Use focused tests that exercise real production lifecycle. Do not recreate the deleted FeedbackAuthority race suite.

## 19.1 Managed Xbox360 ABI tests

Update `CanonicalViiperNativeAbiTests` and related native API tests to prove:

```text
RequiredExports contains SetXbox360RumbleCallback
delegate signature matches pinned header assumptions
callback is rooted after successful Set(non-null)
root is removed after successful Set(null)
root is released after successful Xbox360 device removal
root is released on bus/server teardown
failed Set/Clear does not lie about managed/native ownership
```

Use the existing test style; do not create a new native interop test framework.

## 19.2 Xbox360 motor mapping tests

Required mapping cases:

```text
left=255, right=0   -> Large=65535, Small=0
left=0, right=255   -> Large=0, Small=65535
```

Also prove exact 8-bit round-trip through existing physical conversion for:

```text
0
1
127
255
```

Conceptually:

```text
Expand(v) = v * 257
ToPhysicalByte(Expand(v)) == v
```

## 19.3 SteamDeck decoder integration tests

Keep existing pure `SteamDeckRumbleDecoder` coverage.

Add only enough presentation-feedback integration coverage to prove:

```text
recognized Deck rumble packet -> shared physical sink receives decoded TwoMotorRumble
recognized stop -> physical sink receives Stopped
unsupported/non-rumble output does not produce an invalid physical write
callback exceptions/write failures are contained
```

Do not duplicate the decoder's entire packet matrix in integration tests.

## 19.4 Presentation lifecycle tests

Extend `MsiClawAddonPresentationTests` or the closest existing suite.

Required production cases:

```text
initial X360 attach arms only X360 feedback
initial Deck attach arms only Deck feedback

X360 -> Deck
-> old X360 callback cleared/retired
-> physical STOP requested
-> X360 detached through existing lifecycle
-> Deck attached
-> Deck callback armed

Deck -> X360
-> old Deck callback cleared/retired
-> physical STOP requested
-> Deck detached through existing lifecycle
-> X360 attached
-> X360 callback armed

presentation retirement/shutdown
-> callback ownership cleared safely
-> STOP requested best effort
```

Preserve current publisher stop/join and fail-close assertions.

## 19.5 Physical session tests

Extend focused `MsiClawRumbleTests` and/or physical-owner tests to prove:

```text
successful Full1902 acquisition exposes the verified current descriptor identity
real source loss/recovery advances session generation
recovery updates identity from the newly verified descriptor
virtual X360/Deck switch does NOT advance physical session generation
stale endpoint/write from previous generation remains rejected
```

Do not add artificial scheduler-race tests.

## 19.6 Registration/write failure tests

Cover realistic failures:

```text
X360 callback registration fails
-> controller input presentation remains healthy where policy permits
-> no fake callback ownership committed

Deck callback registration fails
-> same principle

physical rumble write fails
-> callback does not throw
-> presentation remains active

callback clear fails
-> managed callback root is not prematurely discarded
-> subsequent owning teardown establishes safe cleanup according to existing native lifecycle
```

## 19.7 Developer Vibration Test boundary

Keep the existing Cleanup J contract intact.

Add or preserve a negative assertion proving this production feature does not reconnect:

```text
RunVibrationTest RPC
OpenVibrationTestSession RPC
CloseVibrationTestSession RPC
FeedbackAuthority
SteamDeckRumbleFeedbackBridge
```

The Developer Vibration Test page may remain present and unavailable exactly as it is now.

---

# 20. Logging

Add concise production logs only at useful lifecycle/failure boundaries.

Suggested semantic events:

```text
ProductionRumbleCallbackArmed
ProductionRumbleCallbackDisarmed
ProductionRumbleCallbackArmFailed
ProductionRumbleCallbackClearFailed
ProductionRumbleStopFailed
ProductionRumbleWriteFailed
```

Include active presentation kind and meaningful native/write status where available.

Do not log every rumble packet at Info level.

High-frequency normal callback traffic must not create log spam.

If per-packet diagnostics are useful during development, keep them Debug-only and bounded, or omit them from this PR.

---

# 21. Files expected to change

Fresh-reference-close before editing. Expected primary production files:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeApi.cs
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
  and/or MsiClawAddonPhysicalOwnership.cs, depending on the smallest identity-provider ownership
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Possible small new source file(s):

```text
Feedback/Xbox360RumbleFeedbackBridge.cs
and/or one small Full1902 SteamDeck callback adapter
```

A separate class is optional. Prefer direct small composition if that is clearer.

Existing primitives expected to be reused without redesign:

```text
Feedback/SteamDeckRumbleDecoder.cs
Feedback/TwoMotorRumble.cs
Feedback/PhysicalRumbleWriteResult.cs
Devices/MSI/Claw/MsiClawRumbleSink.cs
Devices/MSI/Claw/MsiClawRumblePacketBuilder.cs
Devices/MSI/Claw/MsiClawRumbleEndpointResolver.cs
Devices/MSI/Claw/WindowsMsiClawRumbleEndpointCatalog.cs
```

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/CanonicalViiperNativeAbiTests.cs
tests/SteamInputAddonforClaw.Tests/CanonicalViiperRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawRumbleTests.cs
tests/SteamInputAddonforClaw.Tests/RumbleV1Tests.cs
```

Do not mechanically touch historical work orders just because they contain deleted routing names.

---

# 22. Implementation sequence

Use this order to keep the PR reviewable:

```text
1. Re-read pinned VIIPER header and current managed callback-root code.
2. Bind/root SetXbox360RumbleCallback in managed canonical VIIPER API.
3. Add CanonicalViiperRuntime Xbox360 callback forwarding.
4. Expose the current verified Full1902 physical-session identity/generation to MsiClawRumbleSink.
5. Compose one MsiClawRumbleSink for the Full1902 owned-controller runtime.
6. Add the minimal Xbox360 motor callback adapter.
7. Add the minimal SteamDeck decoder callback adapter.
8. Integrate arm/disarm/STOP into MsiClawAddonPresentation attach/retire/switch/release.
9. Integrate with existing Overlay pause/resume only as required to prevent latched rumble.
10. Add realistic lifecycle and failure tests.
11. Run full build/test validation and inspect final diff for accidental legacy feedback resurrection.
```

Do not split this into a native-ABI-only PR unless implementation size unexpectedly becomes materially larger than this reference closure. The intended delivery is one production PR.

---

# 23. Acceptance criteria

The PR is complete only when all of the following are true.

## Production behavior

```text
Center M Disabled + X360 presentation
-> game/app XInput rumble reaches the physical MSI Claw

Center M Disabled + SteamDeck presentation
-> Steam Deck host-output rumble reaches the physical MSI Claw

X360 <-> SteamDeck switch
-> old callback cannot remain the active feedback source
-> best-effort physical STOP occurs
-> new presentation feedback is armed

physical DirectInput loss/recovery
-> stale physical-session writes are rejected
-> recovered verified session becomes the current rumble source

Center M Enable-and-Restart / process teardown
-> callback ownership is safely retired
-> physical STOP is attempted
```

## Architectural behavior

```text
MsiClawAddonPresentation remains the one presentation/feedback lifetime owner
MsiClawAddonPhysicalOwnership remains the physical ownership authority
MsiClawRumbleSink remains the one physical MSI rumble writer
no new feedback authority/manager/state machine exists
no legacy routing runtime/output stage is restored
no Developer vibration transport is restored
```

## ABI behavior

```text
SetXbox360RumbleCallback is a required, bound canonical export
managed Xbox360 delegate remains rooted exactly while native may call it
successful native clear/remove/bus/server teardown releases roots correctly
```

## Regression behavior

No regression to:

```text
PID1901 <-> PID1902 authority rules
Center M reboot-bound transitions
HidHide exact-target baseline
DirectInput acquisition/recovery
PnP loss/return recovery
Sleep/Hibernate/Resume
Steam/BPM presentation switching
Overlay controller capture
WING/OEM1 mapping
uninstall stock restoration
```

Developer Vibration Test remains intentionally disconnected/unavailable.

---

# 24. Validation

Run the repository-standard full validation expected by the current project baseline.

At minimum:

```text
Debug build
Release build
full Release test suite
```

Then verify the final diff and reference closure:

```text
no FeedbackAuthority resurrection
no SteamDeckRumbleFeedbackBridge resurrection
no AddonRoutingRuntime / RoutingPipeline production resurrection
no Developer vibration RPC/session resurrection
no second physical rumble writer
no second presentation owner
no unrooted native callback lifetime
```

If hardware validation is available, exercise at least:

```text
1. boot/launch in Center M Disabled mode -> X360 presentation -> game rumble
2. enter Steam game/BPM -> Deck presentation -> Steam rumble
3. leave Steam -> X360 -> verify rumble still works and no stuck vibration
4. repeated normal X360 <-> Deck transitions
5. suspend/resume with no stuck motor
6. one physical controller loss/re-enumeration/recovery cycle if practical
7. Enable Center M and Restart path -> no stuck motor during release
```

Do not treat inability to hardware-test every pathological timing combination as a blocker. Focus on real supported handheld lifecycle.

---

# 25. Final implementation principle

The desired end state is intentionally simple:

```text
one Full1902 physical owner
+ one Full1902 presentation owner
+ one physical MSI rumble sink
+ one active VIIPER presentation callback at a time
```

Xbox360 and SteamDeck differ only at the virtual feedback decode/source edge:

```text
Xbox360 -> already-decoded 8-bit left/right motors
SteamDeck -> raw output packet -> existing SteamDeckRumbleDecoder
```

After that, both use the same semantic `TwoMotorRumble` and the same verified physical MSI write path.

Do not solve theoretical races by recreating the old feedback authority architecture. Protect real callback lifetime, real presentation switches, real physical-session loss/recovery, suspend/resume, shutdown, and actual native/HID failures using the owners and gates that already exist.