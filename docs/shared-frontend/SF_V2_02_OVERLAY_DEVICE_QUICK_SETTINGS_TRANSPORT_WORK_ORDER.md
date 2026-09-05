# Work Order — SF-V2-02: Overlay Device Quick Settings Transport

> **Date:** 2026-09-05  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase B foundation  
> **Reviewed baseline:** `main` at `4c03d2b8aab85e1b47ec6e82eb57d697e4eb9d27`  
> **Previous phase:** `SF-V2-01`, squash-merged as PR #496 (`ade3f9b5e303ff1c501fc0bde8203ddf906a1f3f`)  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR plan:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`

---

## 1. Goal

Implement the Shared Frontend V2 **Phase B transport foundation**.

SF-V2-01 already created the one typed Runtime-owned Device Quick Settings projection used by the desktop Main UI and Steam QAM:

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
└─ PowerMode
```

SF-V2-02 extends the **existing dedicated `.Overlay` transport** so the Addon Overlay process can receive the same typed Device state and submit the already-approved Device mutations without being given the full desktop/QAM frontend API.

Target architecture:

```text
CpuBoostRuntime / TdpRuntime / PowerModeRuntime
                    ↓
       InProcessAddonFrontendControl
                    ↓
 FrontendDeviceQuickSettingsSnapshot
 + existing typed mutation results
                    ↓
       narrow Runtime/Overlay binding
                    ↓
        existing .Overlay endpoint
     OverlayTransportProtocol v6
                    ↓
   SteamInputAddonforClaw.Overlay.exe
```

This PR is still **foundation only**.

It must **not** replace the current Device preview rows with real CPU Boost / TDP / Power Mode controls. Real Overlay Device UI binding starts in the later SF-V2-03/SF-V2-04 PRs.

The design target remains:

> **One Runtime authority per real feature, shared typed semantics, and an explicit narrow Overlay allowlist.**

---

## 2. Latest-baseline verification

This work order was re-checked against current `main` immediately before writing.

Current repository head:

```text
4c03d2b8aab85e1b47ec6e82eb57d697e4eb9d27
```

SF-V2-01 was squash-merged as:

```text
ade3f9b5e303ff1c501fc0bde8203ddf906a1f3f
```

The two commits after that merge only changed `docs/gyro/` documentation. No production source changed between the SF-V2-01 merge and this reviewed baseline.

Therefore the current production facts below are the actual post-PR496 source state.

Current protocol versions are now:

```text
FrontendTransportProtocol.CurrentVersion = 26
OverlayTransportProtocol.CurrentVersion  = 5
```

Where older Shared Frontend V2 architecture/roadmap prose still describes the pre-SF-V2-01 frontend protocol as `25`, **current source wins**. SF-V2-02 must keep the desktop/QAM frontend protocol at `26` and change only `.Overlay` from `5` to `6`.

---

## 3. Required reading before implementation

Read current source and current authority documents. Do not implement from old Overlay POC snippets alone.

### 3.1 Full PID1902 authority — current precedence

Read:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Relevant invariant:

```text
Runtime = controller/device feature authority
Overlay = disposable transient presentation client
```

This PR must not alter:

```text
Center M Enabled/Disabled authority
PID1901 / PID1902 ownership
DirectInput physical ownership
HidHide deterministic baseline
VIIPER ownership / teardown
Xbox360 / SteamDeck presentation selection
physical device loss / PnP recovery
sleep / hibernate / resume behavior
restart / shutdown teardown
Win+G suppression
stock restoration
OQ4 Overlay controller capture / neutral publication
```

### 3.2 Shared Frontend V2

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md
```

### 3.3 Current Overlay authority / implementation context

Read at minimum:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/OQ5_UI_09_OVERLAY_PREFERENCE_TRANSPORT_WORK_ORDER.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
```

Use current source where an older document still says Overlay is design-only.

### 3.4 Current source — minimum review set

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

And relevant tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderTransportTests.cs
existing OQ4 capture/navigation/session-loss tests
existing SF-V2-01 Device aggregate/frontend transport tests
```

---

## 4. Current source facts that define this PR boundary

### 4.1 SF-V2-01 already owns the shared Device projection

Current contract:

```csharp
public sealed record FrontendDeviceQuickSettingsSnapshot(
    FrontendCpuBoostSnapshot CpuBoost,
    FrontendTdpSnapshot Tdp,
    FrontendPowerModeSnapshot PowerMode)
{
    public static readonly FrontendDeviceQuickSettingsSnapshot Unavailable = new(
        FrontendCpuBoostSnapshot.Unavailable,
        FrontendTdpSnapshot.Unavailable,
        FrontendPowerModeSnapshot.Unavailable);
}
```

Existing typed mutation results already describe the feature semantics Overlay needs later:

```text
FrontendCpuBoostMutationResult
FrontendTdpMutationResult
FrontendPowerModeMutationResult
```

Do not duplicate these as Overlay-specific feature DTOs.

### 4.2 One Runtime/frontend projection authority already exists

Current `AddonProcessHost.InitializeRuntimeAsync()` creates exactly one production:

```text
InProcessAddonFrontendControl
```

stored as:

```text
_frontendControl
```

The same instance is already used by:

```text
.Frontend → Main UI
.Qam      → QamHost
```

SF-V2-02 must bind Overlay Device operations to this **same `_frontendControl`**.

Do not create a second CPU/TDP/Power owner and do not create another `InProcessAddonFrontendControl` for Overlay.

### 4.3 `.Overlay` is already a real narrow transport

Current `.Overlay` protocol v5 already carries:

```text
Handshake
HandshakeAccepted
Command (Show / Hide / Shutdown)
Navigation
State (Ready / Visible / Hidden)
DismissRequested
ProtocolError
TabOrderState
SetTabOrder
```

Current `NamedPipeOverlayServer` owns:

```text
one active current-user-only pipe
one _writeGate shared by ALL Runtime → Overlay writes
one command acknowledgement path
one connection read loop
```

Current `NamedPipeOverlayClient` owns one read loop and uses the same connection for command/navigation/tab-order traffic.

Extend this transport. Do not add another pipe.

### 4.4 Current handshake order is safety-relevant and must stay stable

Current client startup requires:

```text
Handshake
→ HandshakeAccepted
→ mandatory initial TabOrderState
→ client applies tab order
→ client sends Ready
```

This exists so a warm Overlay never reports Ready with a stale/local default tab order.

**Do not insert Device state as another mandatory pre-Ready frame.**

Device capture is feature-local. CPU/TDP/Power unavailability must not prevent the Overlay process from becoming Ready or weaken Show/Hide/OQ4 lifecycle behavior.

### 4.5 Current Device page is still intentionally a preview fixture

`OverlayWindow.xaml.cs` still labels the Device content as temporary preview/navigation fixtures.

It currently has local sample:

```text
Toggle Preview
Unavailable Toggle Preview
Slider Preview
navigation preview rows
```

Those are not Runtime-backed product controls.

SF-V2-02 must not replace or bind them.

### 4.6 Current OQ4 Show/capture order is authoritative

`AddonProcessHost.CoordinateOverlayToggleAsync()` currently does:

```text
hold _visibleSurfaceTransition
→ retire Main UI if needed
→ require owned presentation + running physical source
→ OverlayProcessController.ShowAsync()
→ require Visible acknowledgement
→ presentation.PauseForOverlayAsync()
→ require neutral/pause success
→ start OverlayControllerInputRouter
→ set _overlayCaptureActive = true
→ capture committed
```

This ordering protects the current game/Steam surface from controller input once the Overlay becomes modal.

**Do not delay the pause/capture commit by inserting Device snapshot work between Visible acknowledgement and `PauseForOverlayAsync()`.**

A feature snapshot is less important than OQ4 capture safety.

### 4.7 Current TDP mutation can wait for real hardware completion

Current:

```csharp
InProcessAddonFrontendControl.SetDeviceTdpAsync(...)
```

persists/commits TDP, raises `StateInvalidated`, and may then await the Runtime's hardware apply completion before returning `FrontendTdpMutationResult`.

Therefore a Device mutation is **not guaranteed to be an immediate operation**.

This is important for `.Overlay` because its current server read loop also needs to receive:

```text
Hidden acknowledgement
DismissRequested
SetTabOrder
other client state
```

while the Overlay is visible.

The new Device mutation implementation must not block that sole read loop while waiting for a Runtime mutation to settle.

---

## 5. Frozen surface exposure for SF-V2-02

Approved Device feature exposure after this transport foundation:

```text
                         Main UI    Steam QAM    Addon Overlay
CPU Boost                  Yes         Yes            Yes
TDP                        Yes         Yes            Yes
Windows Power Mode         Yes         Yes            Yes
MSI Center M authority     Yes         No             No
```

This PR does not imply any other feature becomes available to Overlay.

Do not expose:

```text
Center M Enable/Disable
FrontendStatusSnapshot
prerequisite setup
Developer probes
environment report
front-button settings
active Profile
fan probe
future battery/fan/LED/vibration placeholders
```

The existence of `IAddonFrontendControl` does not grant Overlay access to its full surface.

---

## 6. Protocol version

Change only:

```text
OverlayTransportProtocol.CurrentVersion
5 → 6
```

Add a protocol-history comment, for example:

```text
Version 6 (SF-V2-02): adds typed Device Quick Settings state delivery and the
explicit CPU Boost/TDP/Power Mode mutation request/result messages.
```

Keep:

```text
FrontendTransportProtocol.CurrentVersion = 26
```

Do not bump desktop/QAM frontend protocol merely because `.Overlay` changed.

A v5 Overlay peer must fail the v6 handshake cleanly.

Historical older-version rejection tests must keep their historical values.

---

## 7. Extend `.Overlay` with explicit Device message kinds

Add only the concrete wire intents required by this phase.

Recommended message kinds:

```text
DeviceQuickSettingsState   // Runtime → Overlay
DeviceMutationRequest      // Overlay → Runtime
DeviceMutationResult       // Runtime → Overlay
```

Do not add a generic RPC message such as:

```text
Request(method, json)
InvokeFeature
GetSetting
SetSetting
FeatureMessage
```

The `.Overlay` protocol must remain understandable from its enum/records alone.

---

## 8. Device state wire payload — reuse the shared typed contract directly

Runtime → Overlay Device state must carry:

```text
FrontendDeviceQuickSettingsSnapshot
```

Do **not** create:

```text
OverlayCpuBoostSnapshot
OverlayTdpSnapshot
OverlayPowerModeSnapshot
OverlayDeviceSnapshotV2
```

A valid state frame may contain partial availability, for example:

```text
CpuBoost = available
Tdp      = FrontendTdpSnapshot.Unavailable
PowerMode = available
```

That shape must survive wire serialization exactly.

Do not add an aggregate-wide `Available` flag.

---

## 9. Approved Device mutation allowlist

Overlay transport v6 may submit exactly these Device/global operations:

```text
CPU Boost
  SetDeviceCpuBoostEnabledAsync
  SetDeviceCpuBoostAcAsync
  SetDeviceCpuBoostDcAsync

TDP
  SetDeviceTdpEnabledAsync
  SetDeviceTdpAsync

Windows Power Mode
  SetDevicePowerModeEnabledAsync
  SetDevicePowerModeAcAsync
  SetDevicePowerModeDcAsync
```

No other frontend operation crosses `.Overlay` in this PR.

Use one explicit mutation enum, for example:

```csharp
internal enum OverlayDeviceMutationKind
{
    SetCpuBoostEnabled,
    SetCpuBoostAc,
    SetCpuBoostDc,
    SetTdpEnabled,
    SetTdp,
    SetPowerModeEnabled,
    SetPowerModeAc,
    SetPowerModeDc,
}
```

Keep this enum transport-specific: it expresses wire intent. It is not a feature registry.

---

## 10. Mutation request/result wire shape

Use small explicit request/result wrappers while reusing the existing typed feature values/results.

A practical request shape is:

```csharp
internal sealed record OverlayDeviceMutationRequest(
    long RequestId,
    OverlayDeviceMutationKind Kind,
    bool? Enabled = null,
    CpuBoostMode? CpuBoostMode = null,
    FrontendTdpConfiguration? TdpConfiguration = null,
    WindowsPowerMode? PowerMode = null);
```

A practical result shape is:

```csharp
internal sealed record OverlayDeviceMutationResponse(
    long RequestId,
    OverlayDeviceMutationKind Kind,
    FrontendCpuBoostMutationResult? CpuBoost = null,
    FrontendTdpMutationResult? Tdp = null,
    FrontendPowerModeMutationResult? PowerMode = null,
    string? Error = null);
```

Equivalent naming is fine, but preserve the semantics.

### 10.1 Strict request validation

Validate each request kind explicitly.

Examples:

```text
SetCpuBoostEnabled
→ RequestId > 0
→ Enabled present
→ CpuBoostMode/TdpConfiguration/PowerMode absent

SetCpuBoostAc / SetCpuBoostDc
→ RequestId > 0
→ CpuBoostMode present
→ all unrelated payload fields absent

SetTdpEnabled
→ Enabled present only

SetTdp
→ TdpConfiguration present only

SetPowerModeEnabled
→ Enabled present only

SetPowerModeAc / SetPowerModeDc
→ PowerMode present only
```

Malformed request:

```text
→ invoke ZERO Runtime mutations
→ reject as protocol-invalid or return the narrow invalid-request response consistent with current Overlay wire style
```

Do not silently normalize contradictory fields.

### 10.2 Result shape

A successful Runtime invocation returns exactly one matching typed feature result:

```text
CPU mutation   → FrontendCpuBoostMutationResult
TDP mutation   → FrontendTdpMutationResult
Power mutation → FrontendPowerModeMutationResult
```

Typed feature failures such as:

```text
PersistenceFailed
ApplyFailed
InvalidTarget
Unavailable
```

are **valid mutation results**, not transport failure.

Keep the `.Overlay` connection alive and deliver the typed result.

A thrown operation/transport-side failure may use the response's narrow `Error` field. Do not invent a second copy of the frontend mutation outcome enums.

---

## 11. Correlation is Device-mutation-specific only

A mutation response must be correlated with the request that produced it.

Use a monotonically increasing positive `RequestId` on Device mutation requests/results only.

Do **not** retrofit request IDs onto:

```text
Show/Hide/Shutdown
Navigation
State
DismissRequested
TabOrderState/SetTabOrder
```

Do not create a generic Overlay RPC correlation framework.

### Client-side requirement

The existing `NamedPipeOverlayClient.RunAsync()` remains the **one pipe reader**.

Do not let `SendSetDevice...Async()` call `OverlayWireCodec.ReadAsync()` directly while `RunAsync()` is also reading the same stream.

The read loop must receive `DeviceMutationResult` and complete the matching pending Device request.

A simple narrow design is sufficient:

```text
one Device mutation send gate
+ one current pending mutation TCS/id
```

Serializing current Device mutations is acceptable for this foundation and avoids a generic pending-request dictionary.

The request ID still matters because a cancelled/retired old request must never complete a later request if its result arrives late.

Do not add epochs/revision vectors/transaction IDs.

### Cancellation

No Device-mutation cancel wire message is required in SF-V2-02.

If a caller stops waiting after submission:

```text
already-admitted Runtime operation may settle normally
late result for retired RequestId is ignored
next request must not consume that old result
```

This matches the product rule that frontend lifetime is not feature authority.

---

## 12. Critical requirement — do not block the Overlay read loop on a Device mutation

This is a real lifecycle requirement, not a theoretical race.

A TDP request can wait for real hardware apply completion. If `NamedPipeOverlayServer.ServeAsync()` does this:

```csharp
var result = await mutate(...);
await SendResult(...);
```

inline on the only connection read loop, then a visible Overlay could be unable to process a later:

```text
Hidden acknowledgement
DismissRequested
other client state
```

until the hardware operation completes.

That can delay or break OQ4 Overlay retirement while the panel is modal.

### Required implementation shape

For a validated Device mutation request:

```text
ServeAsync reads + validates request
→ start one exception-contained async mutation operation outside the read loop
→ ServeAsync immediately resumes reading the connection
→ mutation operation calls the bound Runtime frontend method
→ when it settles, send DeviceMutationResult through the existing _writeGate
```

The helper task must catch its own exceptions so no unobserved exception can tear down the process.

Do not add:

```text
worker service
mutation manager
job queue hierarchy
background scheduler
state machine
```

A small private async method is enough.

The existing Runtime feature owners already serialize/protect their own mutations.

---

## 13. Keep one Runtime → Overlay write gate

Current `NamedPipeOverlayServer._writeGate` protects:

```text
HandshakeAccepted
TabOrderState
Command
Navigation
```

v6 must use this **same gate** for:

```text
DeviceQuickSettingsState
DeviceMutationResult
```

Do not create a second per-feature/per-connection write semaphore.

All frames share one byte stream. Prefix/payload interleaving is a real corruption risk; the existing single write gate is the correct authority.

Likewise, `NamedPipeOverlayClient` must use its existing single `_writeGate` for Device mutation request frames.

---

## 14. Bind Overlay transport to the existing `_frontendControl`

Do not pass a second Runtime object tree into Overlay transport.

Add the smallest explicit binding seam to `OverlayProcessController` / `NamedPipeOverlayServer`.

Recommended shape:

```text
capture delegate
+ mutation delegate
```

Conceptually:

```csharp
_overlayController.BindDeviceQuickSettingsAuthority(
    capture: token => _frontendControl.CaptureDeviceQuickSettingsAsync(token),
    mutate: (request, token) => HandleOverlayDeviceMutationAsync(request, token));
```

`HandleOverlayDeviceMutationAsync` should explicitly switch the eight approved mutation kinds onto the same `_frontendControl` methods.

No direct `ProfileStore`, registry, power API, TDP hardware helper, or Runtime feature instance should be called from `.Overlay` transport.

### 14.1 Do not expose full `IAddonFrontendControl`

Do not hand Overlay transport a general-purpose `IAddonFrontendControl` passthrough that could later invoke every desktop/diagnostic method by accident.

The binding must expose only:

```text
CaptureDeviceQuickSettings
8 approved Device mutations
```

### 14.2 Bind before warm startup

Current tab-order binding is installed in `AddonProcessHost.InitializeRuntimeAsync()` before:

```text
_overlayStartup = StartOverlayWarmupAsync();
```

Install the Device binding at the same stage, after `_frontendControl` has been created and before the first warm Overlay connection.

Do not create a second Overlay controller.

---

## 15. Mutation admission — visible committed Overlay session only

A warm hidden Overlay process must not be able to mutate Device settings merely because its pipe remains connected.

Required admission:

```text
current Overlay connection Ready
AND server state Visible
AND AddonProcessHost _overlayCaptureActive == true
AND process shutdown has not started
→ Device mutation may invoke Runtime frontend authority
```

If the client submits a well-formed Device mutation while hidden/unready/not-captured/shutting down:

```text
→ invoke ZERO Runtime mutations
→ return a narrow feature/operation-unavailable response when the connection still exists
```

Do not apply Steam QAM's Big Picture/no-running-game gate here.

That gate belongs only to QAM surface policy.

### Why `_overlayCaptureActive` matters

There is a real interval in the OQ4 Show path:

```text
Visible acknowledged
→ presentation pause
→ input router start
→ capture commit
```

Pointer/touch could theoretically interact with a visible surface before controller capture commits.

The Runtime-side admission check prevents a Device write from becoming active before the Overlay has fully entered its current modal/capture session.

No new authority boolean is needed: reuse the existing `_overlayCaptureActive` fact.

---

## 16. Authoritative Device state publication

Add one best-effort Runtime → Overlay state publish seam, e.g.:

```text
NamedPipeOverlayServer.RefreshDeviceQuickSettingsAsync()
OverlayProcessController.RefreshDeviceQuickSettingsAsync()
```

Exact naming may vary.

### 16.1 Publish only for the active visible session

Required normal behavior:

```text
not Ready / hidden
→ no Device state frame

visible + capture committed
→ capture current FrontendDeviceQuickSettingsSnapshot
→ send DeviceQuickSettingsState
```

No periodic polling.

### 16.2 Show path ordering — OQ4 safety first

On a successful Overlay open, use this order:

```text
Overlay Show → Visible acknowledged
→ PauseForOverlayAsync succeeds
→ virtual presentation is neutral
→ OverlayControllerInputRouter starts
→ _overlayCaptureActive = true
→ OQ4 capture committed
→ THEN best-effort fresh Device state publish
```

Do not do:

```text
Visible acknowledged
→ await Device state capture/send
→ only then pause presentation
```

A slow/failed Device snapshot must never extend the period where the Overlay is visible while game-facing controller publication is still live.

### 16.3 Snapshot failure is feature-local

If the whole aggregate capture unexpectedly throws while the visible session remains valid:

```text
→ log concise Device-state failure
→ best effort send FrontendDeviceQuickSettingsSnapshot.Unavailable
   OR skip the frame if shutdown/disconnect makes delivery impossible
→ keep Overlay visible/captured
→ do not resume controller publication
→ do not tear down Overlay transport solely for the feature failure
```

The normal SF-V2-01 aggregate already isolates ordinary child failures into child-level `Unavailable` snapshots.

---

## 17. `StateInvalidated` → visible Overlay refresh, no polling

SF-V2-01's `_frontendControl` is already the event source used by other frontend transports.

`AddonProcessHost` should subscribe one narrow handler to:

```text
_frontendControl.StateInvalidated
```

The handler must only schedule a Device refresh when the Overlay is currently relevant:

```text
_processShutdownStarted == 0
AND _overlayCaptureActive
AND _overlayController.IsVisible
```

Then:

```text
→ best-effort OverlayProcessController.RefreshDeviceQuickSettingsAsync()
```

Do not poll CPU/TDP/Power from Overlay.

Do not add a timer.

Do not add a general event bus.

### 17.1 Small refresh serialization is acceptable

Show publication and a simultaneous `StateInvalidated` can request two refreshes close together.

A small server-local Device refresh gate is acceptable to keep captures/publications sequential and to re-check visibility before the final send.

This is not a cross-feature transaction or authority.

Do not add:

```text
snapshot epoch
revision vector
refresh generation manager
barrier
atomic multi-feature read
```

If two real refreshes occur near one another, eventual latest Runtime authority is sufficient for this UI.

### 17.2 Re-check visibility before send

If a refresh starts while visible but the Overlay becomes hidden before the capture completes, do not intentionally publish a new Device state to the hidden session.

A simple visibility re-check before the write is enough.

Do not defend against every instruction-level boundary with an epoch/state machine.

---

## 18. Extend `NamedPipeOverlayClient` without creating a second reader

Add a Device-state handler to the existing client run loop.

Conceptually:

```csharp
RunAsync(
    commandHandler,
    navigationHandler,
    tabOrderHandler,
    deviceQuickSettingsHandler,
    token)
```

Preserve existing overloads if they are useful for current tests/callers.

### 18.1 State handler

On `DeviceQuickSettingsState`:

```text
validate no conflicting wire fields
validate Device snapshot exists
→ invoke deviceQuickSettingsHandler(snapshot)
```

### 18.2 Mutation-result handler

On `DeviceMutationResult`:

```text
validate request id / kind / typed result shape
→ if it matches the current pending Device request, complete it
→ if it belongs to an already-retired request id, ignore/log it narrowly
```

Do not let a late result close the connection merely because the initiating UI wait was cancelled.

Malformed wire shape may still fail the protocol.

### 18.3 Explicit send methods

Expose narrow typed client methods for the eight approved Device mutations, for example:

```text
SetDeviceCpuBoostEnabledAsync
SetDeviceCpuBoostAcAsync
SetDeviceCpuBoostDcAsync
SetDeviceTdpEnabledAsync
SetDeviceTdpAsync
SetDevicePowerModeEnabledAsync
SetDevicePowerModeAcAsync
SetDevicePowerModeDcAsync
```

These are Overlay transport methods returning the existing typed frontend results.

Do not expose a public generic:

```text
InvokeDeviceMutation(kind, object)
```

to Overlay UI code.

A private helper inside `NamedPipeOverlayClient` may share the request/send/wait mechanics.

---

## 19. Overlay process-side scope in this PR

`SteamInputAddonforClaw.Overlay/App.xaml.cs` should accept the new Device state transport.

Add a dispatcher-safe handler such as:

```text
HandleDeviceQuickSettingsAsync(snapshot)
```

For SF-V2-02, the minimum acceptable production behavior is:

```text
receive typed snapshot
→ log/retain transient latest snapshot only as needed for transport verification/future binding
→ no direct persistence
→ no direct hardware access
```

Do not bind it to the current preview rows yet.

### Explicitly preserve the current Device fixture

Do not change the existing temporary:

```text
Toggle Preview
Unavailable Toggle Preview
Slider Preview
navigation preview rows
```

into real product controls in SF-V2-02.

Do not add a ViewModel framework or shared UI component framework in this transport PR.

---

## 20. Feature failure must not become Overlay lifecycle failure

Keep these failure domains separate.

### Device state capture fails

```text
Device state unavailable
→ Overlay stays alive
→ OQ4 capture stays active
→ Show/Hide remains usable
```

### Runtime mutation returns typed failure

```text
PersistenceFailed / ApplyFailed / InvalidTarget / Unavailable
→ return typed mutation result
→ Overlay connection stays alive
→ controller capture unchanged
```

### Runtime mutation throws

```text
→ return narrow DeviceMutationResult error when possible
→ connection remains usable if wire is still healthy
```

### Overlay pipe/process actually disconnects while visible

Keep the existing real OQ4 behavior:

```text
VisibleSessionLost
→ AddonProcessHost unified capture retirement/recovery path
```

Do not reinterpret a CPU/TDP/Power failure as a `VisibleSessionLost` condition.

---

## 21. Shutdown / restart / suspend / device-loss rules

This PR must not add new lifecycle authority.

### Process shutdown

Current `AddonProcessHost.BeginProcessShutdown()` already:

```text
sets _processShutdownStarted
retires Overlay input router/capture fact
calls _overlayController.BeginShutdown()
cancels Runtime startup token
prepares Runtime shutdown
```

SF-V2-02 must stop new Device refresh/mutation admission once process shutdown begins.

Unsubscribe the new `_frontendControl.StateInvalidated` Overlay refresh handler during shutdown/disposal so no new feature publish work is intentionally scheduled after shutdown admission closes.

Do not add a new shutdown token hierarchy solely for Shared Frontend.

### Sleep / Hibernate / Resume

Do not change OQ4 or Full1902 suspend/resume sequencing.

A later `StateInvalidated` after ordinary Runtime feature reconcile may refresh a still-valid visible Overlay, but Shared Frontend must not become a power lifecycle participant.

### Physical controller loss / PnP re-enumeration

Do not touch existing physical ownership recovery.

If the visible Overlay session is retired by the existing controller/session-loss logic, Device transport follows the surface lifecycle. It must not attempt controller recovery itself.

### Restart / crash

Overlay process death must not undo CPU/TDP/Power Runtime authority.

Runtime process shutdown follows existing Full1902 teardown rules; no shared-frontend recovery journal/state is added.

---

## 22. Expected production footprint

Likely production files:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
```

Preferred outcome:

```text
OverlayWindow.xaml.cs unchanged
FrontendWire.cs unchanged (stays v26)
NamedPipeAddonFrontendClient/Server unchanged
Main UI unchanged
QAM unchanged
Full1902 physical/presentation/HidHide owners unchanged
```

Tests likely touch:

```text
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderTransportTests.cs
```

A focused new test file such as:

```text
OverlayDeviceQuickSettingsTransportTests.cs
```

is acceptable if it keeps `OverlayTransportTests` readable.

Do not create a new production project.

---

## 23. Required tests

### 23.1 Protocol / handshake

Prove:

```text
OverlayTransportProtocol.CurrentVersion == 6
v5 peer is rejected during handshake
FrontendTransportProtocol remains 26
```

Keep the current mandatory initial handshake ordering:

```text
HandshakeAccepted
→ TabOrderState
→ client applies TabOrderState
→ Ready
```

Prove Device state is **not** a mandatory pre-Ready frame.

### 23.2 Device snapshot state delivery

Prove:

- visible/ready Overlay receives `FrontendDeviceQuickSettingsSnapshot` intact;
- partial child availability survives round trip;
- all three `Unavailable` children survive round trip;
- hidden/unready Overlay receives no normal Device refresh frame;
- a refresh started for a visible session does not intentionally publish after the session is already hidden;
- no polling timer/loop is added.

### 23.3 Mutation allowlist / mapping

For each approved operation prove the Runtime binding invokes exactly the matching existing frontend method:

```text
SetCpuBoostEnabled → SetDeviceCpuBoostEnabledAsync
SetCpuBoostAc      → SetDeviceCpuBoostAcAsync
SetCpuBoostDc      → SetDeviceCpuBoostDcAsync
SetTdpEnabled      → SetDeviceTdpEnabledAsync
SetTdp             → SetDeviceTdpAsync
SetPowerModeEnabled→ SetDevicePowerModeEnabledAsync
SetPowerModeAc     → SetDevicePowerModeAcAsync
SetPowerModeDc     → SetDevicePowerModeDcAsync
```

Verify returned typed result is preserved.

### 23.4 Strict malformed-request rejection

For representative malformed variants prove zero Runtime mutation calls, including:

```text
missing/zero request id
wrong value field for mutation kind
multiple conflicting value fields
missing required value
unknown mutation kind / invalid enum representation
```

### 23.5 Hidden / not-captured admission

Prove a well-formed Device mutation submitted when any required admission is false invokes zero Runtime mutations:

```text
unready
hidden
visible but OQ4 capture not committed
process shutdown started
```

Do not fabricate new product state just to make the test possible; use the existing host/controller seams or the smallest local test delegate.

### 23.6 Typed failures are not transport failures

Prove:

```text
CPU PersistenceFailed/ApplyFailed
TDP InvalidTarget/PersistenceFailed/Unavailable
Power Mode PersistenceFailed/ApplyFailed
```

can return over the connection without closing it.

After a typed failure, perform another ordinary Overlay operation to prove the connection remains usable.

### 23.7 Thrown mutation failure remains feature-local

Inject one mutation delegate that throws.

Prove:

```text
request gets a narrow error response when connection survives
Overlay command/state transport remains usable
```

Do not terminate the process/server for this feature-local error.

### 23.8 Critical regression — long TDP mutation must not block Hide

Add a deterministic test with a TDP mutation delegate blocked on a `TaskCompletionSource`.

Sequence:

```text
connect / Ready
→ Show / Visible
→ submit Device SetTdp request
→ prove mutation delegate entered and is still blocked
→ issue Runtime Hide command
→ client processes Hide and sends Hidden
→ server Hide completes BEFORE blocked TDP mutation is released
→ release TDP mutation
→ DeviceMutationResult arrives afterward
→ connection remains usable
```

This test protects a realistic OQ4 lifecycle failure mode.

Do not replace it with a theoretical scheduler stress test.

### 23.9 Request correlation

Prove:

```text
request A submitted
→ caller retires/cancels wait
→ request B becomes current
→ late result for A arrives
→ B is NOT completed by A
→ result for B completes B
```

No generalized epoch system is required.

### 23.10 `StateInvalidated` refresh

Prove:

```text
frontend StateInvalidated + visible/captured Overlay
→ schedules fresh Device aggregate delivery
```

and:

```text
frontend StateInvalidated + hidden/non-captured Overlay
→ no Device frame / no feature polling
```

### 23.11 Shared write-gate integrity

Extend current write-gate coverage so concurrent/bursty Runtime writes across:

```text
Command
Navigation
TabOrderState
DeviceQuickSettingsState
DeviceMutationResult
```

remain valid frames on the single pipe.

Do not create separate per-message write gates.

### 23.12 OQ4 / lifecycle regression

All existing tests for these must remain green:

```text
Show/Hide/Shutdown acknowledgement
semantic controller navigation
outside-click dismissal
Back dismissal
VisibleSessionLost
capture neutralization / release-to-resume
Main UI ↔ Overlay visible-surface ordering
TabOrderState / SetTabOrder
physical-session-loss recovery
process shutdown teardown
```

A Device feature failure must not alter those outcomes.

---

## 24. Explicit non-goals

Do not implement any of the following in SF-V2-02.

### Real Overlay Device UI

```text
no real CPU Boost rows
no real TDP rows
no real Power Mode rows
no replacement of preview Toggle/Slider fixtures
no UI layout redesign
```

### Profile

```text
no active Profile transport
no FrontendGameProfileSnapshot on .Overlay
no Profile mutation messages
```

### Controller/future Device features

```text
no Center M authority action
no front-button setting transport
no M1/M2
no LED
no vibration strength
no fan control
no battery charge limit
```

### New transport architecture

```text
no new named pipe
no .Overlay.Feature endpoint
no third NamedPipeAddonFrontendServer
no Overlay connection to .Frontend or .Qam
no generic RPC bus
no reflection dispatcher
```

### New authority/state framework

```text
no OverlayFeatureManager
no QuickSettingsRuntime
no OverlayDeviceRuntime
no SharedDeviceStateCache
no feature registry
no capability/surface registry
no cross-feature lock/epoch/barrier/transaction
no recovery journal for frontend state
```

### Full1902 lifecycle

No behavior changes to:

```text
PID1901/PID1902
HidHide
DirectInput
VIIPER
X360/SteamDeck presentation policy
physical PnP recovery
sleep/hibernate/resume
restart/shutdown
Win+G suppression
stock restoration
OQ4 capture/release
```

---

## 25. Overengineering / race review policy for this PR

Review realistic supported lifecycle behavior, not arbitrary instruction-level interleavings.

Blocking examples:

```text
long TDP mutation blocks Hide/retirement
malformed request invokes a Runtime mutation
hidden Overlay can mutate Device state
feature failure tears down Overlay/controller Runtime
writes can interleave and corrupt the pipe
Overlay crash changes Runtime feature/controller authority
shutdown still admits new mutations
```

Do **not** block for hypothetical cases such as:

```text
StateInvalidated happens on one exact instruction between a visibility re-check and frame write
Show refresh and one mutation response cross in a harmless order
an old state frame reaches a surface at the exact moment it becomes hidden
```

if the existing owner/gate/reconcile model still converges safely and there is no realistic user-visible incorrect authority state.

Do not add epochs/barriers/managers solely to eliminate those theoretical orderings.

---

## 26. Verification

Before marking the PR complete, run:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Requirements:

```text
Debug build:   PASS, 0 new warnings/errors
Release build: PASS, 0 new warnings/errors
Full tests:    PASS
diff check:    clean
```

This PR adds transport behavior but no new controller/hardware ownership behavior, so a new physical MSI Claw hardware-validation pass is not required for merge.

A basic real Overlay Show/Hide smoke test is useful when available, especially to confirm v6 startup and no visible regression, but the automated OQ4/read-loop test is the critical lifecycle guard.

---

## 27. Acceptance checklist

### Architecture

- [ ] one existing `_frontendControl` remains the Device feature projection authority;
- [ ] Overlay uses the existing `.Overlay` endpoint;
- [ ] no third full frontend server exists;
- [ ] no new CPU/TDP/Power Runtime/hardware owner exists;
- [ ] no new state cache/manager/framework exists;
- [ ] Full1902/OQ4 ownership and teardown paths are unchanged.

### Protocol

- [ ] `OverlayTransportProtocol` is exactly v6;
- [ ] `FrontendTransportProtocol` remains v26;
- [ ] v5 Overlay peer fails handshake;
- [ ] initial TabOrderState still precedes Ready;
- [ ] Device state is not required for Ready;
- [ ] all Runtime → Overlay frames use the existing one server `_writeGate`;
- [ ] all Overlay → Runtime frames use the existing one client `_writeGate`.

### Device state

- [ ] wire carries `FrontendDeviceQuickSettingsSnapshot` directly;
- [ ] partial child unavailability is preserved;
- [ ] no duplicate Overlay CPU/TDP/Power snapshot DTO exists;
- [ ] fresh state publishes after successful OQ4 capture commit;
- [ ] `StateInvalidated` refresh occurs only for a relevant visible/captured session;
- [ ] no polling loop/timer exists.

### Mutations

- [ ] exactly eight approved Device mutation intents exist;
- [ ] each maps to the existing `_frontendControl` method;
- [ ] hidden/unready/not-captured/shutdown mutation invokes zero Runtime operations;
- [ ] typed mutation failure remains typed and feature-local;
- [ ] thrown operation error does not automatically close the Overlay session;
- [ ] long TDP mutation does not block Hide/Dismiss/state processing;
- [ ] request correlation cannot misapply a late old result to a newer request.

### Overlay UI scope

- [ ] `OverlayWindow` preview Device rows remain preview fixtures;
- [ ] no real Device UI binding is added;
- [ ] no layout/navigation redesign is mixed into this PR.

### Regression

- [ ] Show/Hide/Shutdown green;
- [ ] navigation green;
- [ ] outside-click/Back dismissal green;
- [ ] OQ4 capture/release green;
- [ ] VisibleSessionLost green;
- [ ] TabOrder transport green;
- [ ] Main UI/QAM frontend tests green;
- [ ] Full test suite green.

---

## 28. Completion statement

SF-V2-02 is complete when:

```text
Runtime Device truth
→ existing shared typed projection
→ narrow .Overlay v6 state + mutation transport
→ Overlay process can receive authoritative Device state and submit approved Device mutations
```

while:

```text
real Overlay Device rows are still not bound
OQ4 controller capture remains the lifecycle authority
FrontendTransport stays v26
Overlay remains a disposable presentation client
Runtime remains the only Device/controller authority
```

The next implementation phase after this PR is the focused Overlay Device UI binding work, not another transport/framework layer.
