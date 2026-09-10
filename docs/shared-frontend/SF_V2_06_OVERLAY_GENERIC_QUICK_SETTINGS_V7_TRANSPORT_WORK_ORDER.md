# Work Order — SF-V2-06: Replace `.Overlay` v6 Device Wire with Generic Quick Settings v7

> **Date:** 2026-09-10  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase E  
> **Reviewed repository head:** `main` at `bc0200fc75df3d12d451fe2945c73c25758952c8`  
> **Previous phase:** SF-V2-05 QAM Device generic renderer migration — squash-merged as PR #506  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR roadmap:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Historical Overlay transport authority:** `docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md`  
> **Next phase:** SF-V2-07 Overlay generic Device renderer + real Device binding

---

## 1. Goal

Replace the current pre-release `.Overlay` **v6 Device-specific feature transport** with the closed shared **Quick Settings v7 transport** before the Overlay's real Device UI binds to it.

The resulting `.Overlay` transport must carry the same shared product contract that QAM now consumes:

```text
QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
```

while preserving every proven Overlay/OQ4 lifecycle property from SF-V2-02.

Target architecture after this PR:

```text
CPU Boost / TDP / Power Mode Runtime owners
                    |
                    v
       InProcessAddonFrontendControl
                    |
                    +-------------------------------+
                    |                               |
                    v                               v
     QuickSettingsPresentation          QuickSettingsMutationAdapter
        BuildDevice(...)                  MutateQuickSettingAsync(...)
                    |                               |
                    +---------------+---------------+
                                    v
                     QuickSettingsPageSnapshot(Device)
                     QuickSettingsMutationIntent/Result
                                    |
                                    v
                       AddonProcessHost Overlay binding
                                    |
                                    v
                    existing dedicated `.Overlay` pipe
                     OverlayTransportProtocol v7
                                    |
                                    v
                   SteamInputAddonforClaw.Overlay.exe
                   transport consumer only in SF-V2-06
```

The core rule is:

> **Replace the v6 feature-shaped payload/dispatch, not the proven Overlay transport/lifecycle architecture.**

SF-V2-06 is still a **transport migration**. It does not replace the current Overlay Device preview fixture with real controls. That is SF-V2-07.

---

## 2. Why this PR exists now

Shared Frontend V2 deliberately established the shared product model before binding a second real renderer.

Completed sequence:

```text
SF-V2-01
→ shared Device aggregate foundation

SF-V2-02
→ `.Overlay` v6 Device-specific transport foundation
→ proved lifecycle, admission, correlation, non-blocking slow mutation behavior

SF-V2-03
→ shared Quick Settings product contract + Device projection + central mutation adapter

SF-V2-04
→ `.Frontend/.Qam` generic Quick Settings RPC seam
→ FrontendTransportProtocol v28

SF-V2-05 / PR #506
→ QAM Device renderer migrated to QuickSettingsPageSnapshot(Device)
→ QAM Device product duplication removed
```

The current Overlay still carries the older SF-V2-02 shape:

```text
FrontendDeviceQuickSettingsSnapshot
OverlayDeviceMutationKind
OverlayDeviceMutationRequest
OverlayDeviceMutationResponse
OverlayDeviceMutationDispatch
```

That v6 shape was intentionally temporary. It was useful to prove the hard transport/lifecycle behavior before the shared product contract existed.

Do not build the real Overlay Device renderer on top of that obsolete feature-specific wire and then migrate it again.

The required sequence is:

```text
v6 Device-specific transport
→ SF-V2-06 generic Quick Settings v7 transport
→ SF-V2-07 real generic Device renderer/binder
```

---

## 3. Required reading before implementation

Read current source at implementation time. Do not implement from this work order's conceptual snippets without checking current `main` first.

### 3.1 Full PID1902 authority — mandatory

Read the documents in the precedence defined by:

```text
docs/Full 1902 Implementation/README.md
```

At minimum:

```text
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

The current folder also contains:

```text
docs/Full 1902 Implementation/EX_FIRST_FAN_CONTROL_ARCHITECTURE_2026-09-10.md
```

That fan document defines an independent future Device/Cooling architecture. It does **not** make Fan Control part of the current shared Quick Settings Device page. Do not opportunistically add Fan Control in SF-V2-06.

Frozen authority invariant:

```text
Runtime = controller / hardware / persistence authority
Overlay = disposable transient presentation client
```

This PR must not change Full1902 controller ownership.

### 3.2 Shared Frontend V2 — mandatory

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md
docs/shared-frontend/SF_V2_03_SHARED_QUICK_SETTINGS_PRODUCT_CONTRACT_WORK_ORDER.md
docs/shared-frontend/SF_V2_04_FRONTEND_QAM_GENERIC_QUICK_SETTINGS_RPC_SEAM_WORK_ORDER.md
docs/shared-frontend/SF_V2_05_QAM_DEVICE_GENERIC_RENDERER_MIGRATION_WORK_ORDER.md
```

SF-V2-02 is historical but remains the authority for the lifecycle/non-blocking properties that v7 must preserve.

### 3.3 Overlay lifecycle / controller capture — mandatory

Read:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md
```

Use current source where older Overlay architecture prose still describes already-implemented work as design-only.

### 3.4 Current source — minimum set

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
```

Inspect the current Overlay transport/lifecycle tests before changing production code:

```text
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
```

Also inspect:

```text
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

Do not duplicate tests already proving product semantics in the central Quick Settings adapter.

---

## 4. Reviewed baseline facts

This work order was prepared against:

```text
main = bc0200fc75df3d12d451fe2945c73c25758952c8
```

That commit is the SF-V2-05 / PR #506 squash merge.

Current protocol versions are:

```text
FrontendTransportProtocol.CurrentVersion = 28
OverlayTransportProtocol.CurrentVersion  = 6
```

Required result for this PR, assuming no unrelated protocol PR lands first:

```text
FrontendTransportProtocol.CurrentVersion = 28   // unchanged
OverlayTransportProtocol.CurrentVersion  = 7    // one bump
```

If `main` moves before implementation starts, re-check the actual current values. Increment `.Overlay` exactly once from then-current source when this wire replacement changes the current protocol. Do not force a historical number over a legitimate intervening protocol change.

---

## 5. Current v6 architecture to preserve versus replace

### 5.1 Preserve these proven transport/lifecycle mechanics

Current `.Overlay` v6 already has:

```text
one current-user-only named pipe
one active connection
one server read loop
one client read loop
one server _writeGate for every Runtime -> Overlay frame
one client _writeGate for every Overlay -> Runtime frame
one narrow mutation correlation slot/gate
mutation execution outside the sole server read loop
Ready + Visible transport admission
_overlayCaptureActive + shutdown Runtime admission
StateInvalidated-driven visible refresh
no polling
```

These are not obsolete merely because the payload becomes generic.

### 5.2 Replace these v6 Device-specific product transport types

Current production code includes:

```text
OverlayWireMessageKind.DeviceQuickSettingsState
OverlayWireMessageKind.DeviceMutationRequest
OverlayWireMessageKind.DeviceMutationResult

OverlayDeviceMutationKind
OverlayDeviceMutationRequest
OverlayDeviceMutationResponse
OverlayDeviceMutationDispatch

OverlayWireMessage.DeviceState
OverlayWireMessage.DeviceMutationRequest
OverlayWireMessage.DeviceMutationResponse
```

Current `NamedPipeOverlayClient` also exposes feature-specific methods such as:

```text
SendDeviceCpuBoostEnabledAsync
SendDeviceCpuBoostAcAsync
SendDeviceCpuBoostDcAsync
SendDeviceTdpEnabledAsync
SendDeviceTdpAsync
SendDevicePowerModeEnabledAsync
SendDevicePowerModeAcAsync
SendDevicePowerModeDcAsync
```

These are the current v6 API and must be removed from the current production v7 path.

Do not retain an old + new dual-path compatibility layer.

---

## 6. Scope boundary

### In scope

```text
OverlayTransportProtocol v6 -> v7
shared Quick Settings page state on `.Overlay`
shared Quick Settings mutation intent/result on `.Overlay`
transport-only request correlation wrapper
strict v7 JSON/payload validation
Device-only current Overlay page exposure/admission
replacement of v6 feature-specific dispatch with IAddonFrontendControl.MutateQuickSettingAsync
replacement of v6 aggregate capture with CaptureQuickSettingsPageAsync(Device)
non-blocking mutation execution preservation
StateInvalidated visible/captured refresh preservation
mutation self-invalidation/result ordering guard
Overlay App transport consumer migration
v6 production type/method removal
focused transport/lifecycle regression tests
```

### Explicitly not in scope

```text
real Overlay Device controls
Overlay generic row renderer
Overlay Toggle/Slider binding to Runtime
TDP local draft/group implementation in Overlay UI
linked-slider UI correction in Overlay
moving OverlayDelayedSliderCommit to row CommitPolicy
Profile page projection/mutation
Profile Overlay publication
Main UI migration
QAM changes
Frontend protocol changes
Fan Control shared-row exposure
Battery/LED/vibration feature exposure
new controller lifecycle or authority logic
```

`OverlayWindow.xaml.cs` should remain unchanged unless a compile-only signature update becomes unavoidable. Do not use SF-V2-06 to redesign the Overlay UI.

---

## 7. Protocol v7

Change the `.Overlay` protocol history and current version.

Conceptually:

```csharp
// Version 7 (SF-V2-06): replaces the pre-release v6 Device-specific Quick Settings
// state/mutation wire with the shared QuickSettingsPageSnapshot /
// QuickSettingsMutationIntent / QuickSettingsMutationResult contract.
// A v6 peer must fail the handshake rather than silently interpret the replaced frames.
internal const int CurrentVersion = 7;
```

Do not change:

```text
FrontendTransportProtocol.CurrentVersion = 28
```

A v6 peer must be rejected during handshake.

Do not support:

```text
v6 Device frames + v7 Quick Settings frames simultaneously
legacy translation adapter
protocol compatibility shim
```

The product is pre-release and no real Overlay Device product UI is bound to v6.

---

## 8. v7 message kinds

Replace the three v6 Device-specific kinds with narrow generic Quick Settings kinds.

Recommended current names:

```text
QuickSettingsPageState       // Runtime -> Overlay
QuickSettingsMutationRequest // Overlay -> Runtime
QuickSettingsMutationResult  // Runtime -> Overlay
```

Keep all existing unrelated lifecycle/shell kinds unchanged:

```text
Handshake
HandshakeAccepted
Command
Navigation
State
DismissRequested
ProtocolError
TabOrderState
SetTabOrder
```

Do not create a general-purpose message such as:

```text
RpcRequest
InvokeMethod
FeatureRequest
ObjectState
DynamicPayload
```

Generic means generic **inside the closed Quick Settings contract only**.

---

## 9. v7 wire payload shape

Reuse the shared contract directly.

### 9.1 Page state

Runtime -> Overlay page state carries exactly one:

```csharp
QuickSettingsPageSnapshot
```

The page already contains:

```text
PageId
AppId
Available / Message
ordered Sections
ordered Rows
row values
SliderSpec
CommitPolicy
CommitGroupId
LinkedSliderConstraints
```

Do not create an Overlay copy such as:

```text
OverlayQuickSettingsPageSnapshot
OverlayQuickSettingsRow
OverlayDevicePageV7
```

### 9.2 Mutation correlation wrapper

Correlation remains transport-specific and must not be added to the shared product contract.

A practical request wrapper is:

```csharp
internal sealed record OverlayQuickSettingsMutationRequest(
    long RequestId,
    QuickSettingsMutationIntent Intent);
```

A practical response wrapper is:

```csharp
internal sealed record OverlayQuickSettingsMutationResponse(
    long RequestId,
    QuickSettingsMutationResult? Result = null,
    string? Error = null);
```

Equivalent naming is acceptable.

Required semantics:

```text
valid Runtime Quick Settings result
→ Result != null
→ Error == null

thrown operation / transport-side failure
→ Result == null
→ Error = narrow transport/operation message
```

Do not add another feature-outcome enum. `QuickSettingsMutationResult.Succeeded/FailureMessage/Page` is the shared surface result.

### 9.3 Suggested `OverlayWireMessage` direction

Conceptually replace the v6 fields with:

```csharp
QuickSettingsPageSnapshot? QuickSettingsPage = null,
OverlayQuickSettingsMutationRequest? QuickSettingsMutationRequest = null,
OverlayQuickSettingsMutationResponse? QuickSettingsMutationResponse = null
```

Keep lifecycle/tab-order fields unchanged.

---

## 10. Strict JSON decoding — mandatory fail-closed rule

SF-V2-04 exposed a real defect class that must not be repeated in `.Overlay` v7.

The shared Quick Settings enums begin at zero. If required constructor members are omitted and deserialization silently default-initializes them, malformed JSON can accidentally become valid identities such as:

```text
QuickSettingsPageId.Device == 0
QuickSettingsRowId.DeviceTdpEnabled == 0
QuickSettingsValueKind.Boolean == 0
```

Therefore v7 generic payload decoding must fail closed when required constructor parameters are omitted.

### 10.1 Update Overlay JSON options

The current `.Overlay` codec already uses strict string enum names:

```csharp
new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
```

Preserve that behavior and add required-constructor enforcement for v7, matching the proven `.Frontend/.Qam` approach:

```csharp
private static readonly JsonSerializerOptions Json = new()
{
    RespectRequiredConstructorParameters = true,
    Converters =
    {
        new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
    }
};
```

Because the protocol is intentionally moving v6 -> v7, this stricter decoding is part of the v7 contract.

### 10.2 Required omission tests

At minimum prove malformed generic mutation JSON is rejected before any Runtime mutation when each of these required identities is omitted:

```text
QuickSettingsMutationIntent.PageId
QuickSettingsMutationIntent.EditedRowId
QuickSettingsRowValue.RowId
QuickSettingsValue.Kind
```

Also prove numeric enum JSON is still rejected where string enum names are required.

### 10.3 Nullable-reference structural guard

`RespectRequiredConstructorParameters` proves presence, not non-nullability.

A hostile/malformed current-user peer can still send structurally unusable values such as:

```json
{
  "values": null
}
```

or null nested entries/values where the C# type is non-nullable.

Before invoking the bound Runtime mutation delegate, validate only the narrow **wire structural safety** needed to avoid null-reference/protocol ambiguity, for example:

```text
RequestId > 0
Intent != null
Intent.PageId enum defined
Intent.EditedRowId enum defined
Intent.Values != null
no null row-value entries
all RowId enums defined
all Value objects present
all Value.Kind enums defined
all Value shapes structurally valid
```

Do **not** duplicate product validation here.

Specifically, the Overlay transport must **not** decide:

```text
which Device row is editable
whether a CPU Boost integer is a valid CpuBoostMode
whether a TDP grouped intent has exactly five rows
whether TDP Enabled must be true
whether a row belongs to a section
whether a value is inside a Runtime feature's final valid range
```

Those remain `QuickSettingsMutationAdapter` / existing typed Runtime authority.

The transport's job is only:

> **Is this a structurally safe closed Quick Settings message that can be handed to the shared adapter?**

---

## 11. Page-state structural validation

Outbound page state is created by the trusted Runtime projection, but the Overlay client must still fail closed on a malformed wire frame rather than pass null collections into the future renderer.

Keep this validation narrow.

At minimum reject a `QuickSettingsPageState` frame when:

```text
page payload is absent
Sections is null
LinkedSliderConstraints is null
any section entry is null
any section Rows collection is null
any row entry is null
```

The JSON enum converter already rejects unknown enum names.

Do not re-run the entire shared product projection's semantic validation in the transport.

---

## 12. Device is the only active Overlay Quick Settings page in SF-V2-06

The v7 wire is page-generic, but current product exposure is intentionally narrow.

SF-V2-06 publishes only:

```text
QuickSettingsPageId.Device
AppId = null
```

Do not publish or admit Profile yet.

Current `QuickSettingsPageId.Profile` and Profile row identities are reserved vocabulary. Their projection/mutation is a later milestone.

Required Overlay surface exposure rule:

```text
Device page
→ allowed under Overlay admission

Profile page
→ not exposed/admitted in SF-V2-06
```

This is an Overlay surface-scope rule, not product-row validation.

The wire still carries `PageId` and optional `AppId`, so adding Profile later must not require another generic transport redesign.

Do not create a page registry or permission matrix. One explicit current check is enough.

---

## 13. Remove `OverlayDeviceMutationDispatch`

The central shared adapter now owns Device row/group -> typed Runtime mapping.

Therefore v7 must not preserve this second dispatch authority:

```text
OverlayDeviceMutationDispatch
```

The v7 Runtime path is:

```text
OverlayQuickSettingsMutationRequest
        |
        v
Overlay admission / structural validation
        |
        v
IAddonFrontendControl.MutateQuickSettingAsync(intent)
        |
        v
QuickSettingsMutationAdapter
        |
        v
existing typed Device mutation method
```

Do not switch on Device row IDs again inside `OverlayWire.cs` or `AddonProcessHost` to map them to CPU/TDP/Power methods.

The only explicit page-level check allowed in the Overlay binding is the current surface exposure rule (`PageId == Device`).

Existing `QuickSettingsMutationAdapterTests` already own the detailed product mapping assertions. Preserve those tests rather than cloning their eight-operation matrix into the Overlay transport suite.

---

## 14. Runtime authority binding

Bind `.Overlay` to the **same one** `_frontendControl` already used by Main UI/QAM.

Current v6 binding:

```text
CaptureDeviceQuickSettingsAsync
+ feature-shaped mutation delegate
```

v7 should become conceptually:

```csharp
_overlayController.BindQuickSettingsAuthority(
    captureDevicePage: token =>
        _frontendControl!.CaptureQuickSettingsPageAsync(
            QuickSettingsPageId.Device,
            appId: null,
            token),
    mutate: (intent, token) =>
        HandleOverlayQuickSettingsMutationAsync(intent, token));
```

Exact method names may differ.

A Device-only capture delegate is acceptable in SF-V2-06 and is simpler than building a general page registry. The wire itself is already future-safe because `QuickSettingsPageSnapshot` carries `PageId`.

Do not pass full `IAddonFrontendControl` into Overlay transport as a general-purpose service.

Do not call:

```text
ProfileStore
CPU Boost registry APIs
TDP hardware transport
Power APIs
fan helper/WMI
controller/presentation owners
```

directly from `.Overlay` transport.

---

## 15. Bind before warm Overlay startup

Preserve current composition order.

The Quick Settings authority must be bound after `_frontendControl` exists and before:

```text
_overlayStartup = StartOverlayWarmupAsync()
```

Keep it alongside the existing tab-order authority bind.

Do not create a second Overlay controller or second pipe.

---

## 16. Overlay mutation admission

Surface admission remains separate from shared product semantics.

Required mutation admission is unchanged from SF-V2-02/OQ4:

```text
current Overlay connection Ready
AND server state Visible
AND AddonProcessHost _overlayCaptureActive == true
AND process shutdown has not started
AND requested shared page is currently exposed by Overlay (Device only)
→ invoke IAddonFrontendControl.MutateQuickSettingAsync(intent)
```

If any admission condition is false:

```text
invoke ZERO Runtime mutations
keep connection alive when the wire itself is valid
return a generic failed QuickSettingsMutationResult when possible
```

A simple generic not-admitted result is sufficient:

```csharp
new QuickSettingsMutationResult(
    false,
    "The Overlay is not the active captured surface.",
    QuickSettingsPageSnapshot.Unavailable(
        intent.PageId,
        intent.AppId,
        "Quick Settings are unavailable for this Overlay session."));
```

Equivalent concise messages are fine.

Do not reuse QAM admission:

```text
Big Picture active + AppId == 0
```

Overlay Device Quick Settings remains Device/global scope even while a game is running.

---

## 17. Critical: mutation execution must remain outside the sole server read loop

This is a real lifecycle requirement already proven by SF-V2-02.

A generic Device TDP intent still ultimately reaches `SetDeviceTdpAsync`, which can wait for real hardware completion.

Wrong:

```csharp
// inside ServeAsync read loop
var result = await mutate(intent);
await SendResult(result);
```

That could block processing of:

```text
Hidden acknowledgement
DismissRequested
SetTabOrder
other client state
```

while the Overlay is modal.

Required shape:

```text
ServeAsync reads QuickSettingsMutationRequest
→ validate wrapper + structural intent
→ start one exception-contained async mutation operation
→ immediately resume sole read loop
→ operation checks Ready/Visible + bound Runtime admission
→ await shared MutateQuickSettingAsync outside read loop
→ write QuickSettingsMutationResult through existing _writeGate
```

A small private async method is sufficient.

Do not add:

```text
worker service
mutation queue service
background scheduler
operation manager
state machine
```

---

## 18. Keep exactly one byte-stream write gate per side

All Runtime -> Overlay frames share the same byte stream.

The current server `_writeGate` must continue to protect:

```text
HandshakeAccepted
TabOrderState
Command
Navigation
QuickSettingsPageState
QuickSettingsMutationResult
ProtocolError where applicable
```

The current client `_writeGate` must continue to protect:

```text
Handshake
State
DismissRequested
SetTabOrder
QuickSettingsMutationRequest
```

Do not add a second Quick Settings write semaphore.

The existing separate mutation **request-serialization gate** on the client is not a byte-stream write gate and may remain as the narrow one-current-mutation correlation policy.

---

## 19. Client: preserve one reader, replace the Device-specific API

`NamedPipeOverlayClient.RunAsync()` must remain the sole reader from the pipe after handshake.

Do not let a mutation send method call `OverlayWireCodec.ReadAsync()` directly while `RunAsync()` is reading.

### 19.1 Page handler

Replace the v6 Device aggregate handler with a generic page handler, conceptually:

```csharp
Func<QuickSettingsPageSnapshot, Task>? quickSettingsPageHandler
```

On `QuickSettingsPageState`:

```text
validate frame shape
validate narrow structural page safety
→ invoke page handler
```

Do not bind product controls in the transport client.

### 19.2 Generic mutation API

Remove the eight v6 feature-specific send methods from current production code.

Expose one narrow shared-product method, conceptually:

```csharp
internal Task<QuickSettingsMutationResult> SendQuickSettingsMutationAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken token = default)
```

This is not a general RPC API. Its input is the closed typed shared contract.

### 19.3 Correlation

Rename/reuse the current v6 correlation mechanics rather than designing a new request system.

Conceptually retain:

```text
one mutation send gate
one monotonically increasing positive RequestId
one current pending request id
one current TaskCompletionSource
```

For example:

```text
_deviceMutationGate         -> _quickSettingsMutationGate
_deviceRequestSequence      -> _quickSettingsRequestSequence
_pendingDeviceRequestId     -> _pendingQuickSettingsRequestId
_pendingDeviceMutation      -> _pendingQuickSettingsMutation
```

Exact names are implementation choice.

### 19.4 Cancellation / late result

Preserve current behavior:

```text
request A submitted
→ caller stops waiting / cancellation retires local waiter
→ already-admitted Runtime mutation A may settle normally
→ request B becomes current
→ late result A arrives
→ RequestId mismatch, ignore A
→ A must never complete B
```

No mutation-cancel wire message is required.

Do not add a dictionary of arbitrary concurrent RPCs merely because the payload is now generic.

---

## 20. Runtime -> Overlay page publication

Replace v6 aggregate publication with shared Device page publication.

Current semantic trigger remains:

```text
successful OQ4 capture commit
→ best-effort publish current Device shared page

StateInvalidated while visible + captured
→ best-effort republish current Device shared page

hidden/not captured
→ no Quick Settings page polling/publication loop
```

### 20.1 Capture source

Capture through:

```csharp
_frontendControl.CaptureQuickSettingsPageAsync(
    QuickSettingsPageId.Device,
    null,
    token)
```

Do not capture `FrontendDeviceQuickSettingsSnapshot` in the Overlay transport and then re-project it locally.

The Runtime shared projection is the one source of product labels/order/options/policy.

### 20.2 Whole capture failure

If the shared Device page capture unexpectedly throws:

```text
log concise feature-local failure
→ best effort publish QuickSettingsPageSnapshot.Unavailable(Device)
   if the surface is still valid for delivery
→ keep Overlay alive/captured
→ do not classify as VisibleSessionLost
```

### 20.3 Ready/Visible re-check

Keep the current cheap pre-check and final send-time check.

If capture starts while visible but the Overlay is hidden before send:

```text
do not intentionally publish the page into the hidden session
```

A simple re-check is sufficient.

Do not add a visibility epoch/revision vector.

---

## 21. OQ4 Show/capture ordering is unchanged

The feature page is less important than controller capture safety.

Required successful open order remains:

```text
hold existing _visibleSurfaceTransition
→ retire Main UI if needed
→ require current physical/presentation prerequisites
→ Overlay Show
→ Visible acknowledgement
→ PauseForOverlayAsync
→ prove current virtual presentation publisher stopped/neutral
→ start OverlayControllerInputRouter
→ _overlayCaptureActive = true
→ OQ4 capture committed
→ THEN fire-and-forget shared Device page refresh
```

Do not do:

```text
Visible acknowledgement
→ await Quick Settings page capture/send
→ then neutralize controller presentation
```

A slow/failed Quick Settings capture must never extend game-facing live input while the modal Overlay is already visible.

Preserve the current fire-and-forget publication after capture commit.

---

## 22. `StateInvalidated` refresh remains event-driven

Keep the existing `_frontendControl.StateInvalidated` subscription.

Normal external invalidation publication remains gated by:

```text
process shutdown not started
AND _overlayCaptureActive
AND OverlayProcessController.IsVisible
```

Then:

```text
→ best-effort shared Device page refresh
```

No polling timer.

No periodic capture loop.

No event bus.

---

## 23. Important normal-path ordering: mutation result must win over its own invalidation

SF-V2-05 QAM review already exposed this as a real normal execution path, not a theoretical race.

Current Device mutation methods can publish `StateInvalidated` **before** their mutation task returns. TDP may then continue waiting for hardware completion before the final result is produced.

Without a guard, the Overlay path could do:

```text
Quick Settings mutation starts
→ underlying typed Runtime mutates/persists
→ StateInvalidated is raised
→ Overlay ordinary refresh starts
→ shared mutation later returns QuickSettingsMutationResult(Page = fresh authority)
→ mutation result is sent
→ racing refresh can send an older/less-complete page afterward
```

For a future real renderer this can violate:

```text
current mutation settlement/result page is authoritative
```

and can especially erase the visible context of a typed `Succeeded=false + FailureMessage` settlement if a later ordinary state refresh is treated as a clean page update.

### 23.1 Required minimal fix

Do not add an epoch/revision protocol.

Use the smallest host-local fact around **only the actual admitted Overlay Quick Settings Runtime mutation call**.

A narrow in-flight counter/boolean is sufficient, conceptually:

```csharp
private int _overlayQuickSettingsMutationInFlight;
```

Then:

```csharp
private async Task<QuickSettingsMutationResult> HandleOverlayQuickSettingsMutationAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken token)
{
    // admission checks first
    Interlocked.Increment(ref _overlayQuickSettingsMutationInFlight);
    try
    {
        return await _frontendControl!.MutateQuickSettingAsync(intent, token)
            .ConfigureAwait(false);
    }
    finally
    {
        Interlocked.Decrement(ref _overlayQuickSettingsMutationInFlight);
    }
}
```

And the Overlay-specific `StateInvalidated` publication handler should skip the redundant refresh while this counter is non-zero:

```csharp
if (Volatile.Read(ref _overlayQuickSettingsMutationInFlight) != 0)
    return;
```

Why dropping that refresh is correct for normal typed completion:

```text
QuickSettingsMutationResult already contains a fresh authoritative page
```

This suppresses only the Overlay page republish caused while an Overlay mutation is actively settling. It does not suppress `StateInvalidated` for Main UI, QAM, Runtime, or other subscribers.

### 23.2 Do not over-engineer this

Do not add:

```text
page revision numbers
epochs
barriers
mutation transaction ids beyond existing transport RequestId
state history
refresh generation manager
cross-surface lock
```

Also do not hold this fact during a future 2-second UI debounce. SF-V2-06 transport never owns the debounce window; SF-V2-07's renderer-local pending helper will.

The in-flight fact exists only around the actual Runtime mutation call because that is the real path that raises the self-invalidation.

### 23.3 Thrown operation

Normal feature failures should return typed `QuickSettingsMutationResult` and therefore receive a fresh `Page`.

Do not add a large deferred-refresh state machine merely for hypothetical exceptions after partial mutation. If current source/tests prove a specific thrown-after-invalidation path that leaves changed authority without a result page, add only the smallest one-shot convergence needed for that proven case.

---

## 24. Server mutation response behavior

For a structurally valid, admitted request:

```text
await bound MutateQuickSettingAsync outside read loop
→ receive QuickSettingsMutationResult
→ send correlated QuickSettingsMutationResult frame
→ connection remains alive whether Succeeded is true or false
```

Typed failure example:

```csharp
new QuickSettingsMutationResult(
    Succeeded: false,
    FailureMessage: "TDP apply failed.",
    Page: freshPage)
```

is a **normal transport result**.

Do not turn `Succeeded=false` into:

```text
ProtocolError
connection teardown
VisibleSessionLost
controller capture retirement
```

If the bound mutation delegate throws unexpectedly:

```text
catch inside the off-read-loop operation
→ send correlated response with Error when connection remains usable
→ keep pipe alive
→ do not reinterpret as Overlay process/session failure
```

---

## 25. Overlay App scope in SF-V2-06

Update `SteamInputAddonforClaw.Overlay/App.xaml.cs` to consume the generic page callback.

Conceptually replace:

```text
HandleDeviceQuickSettingsAsync(FrontendDeviceQuickSettingsSnapshot)
```

with:

```text
HandleQuickSettingsPageAsync(QuickSettingsPageSnapshot)
```

Minimum acceptable behavior in this PR:

```text
receive shared page
→ verify/log page identity and basic counts if useful
→ optionally retain the latest page transiently for the next binding PR
→ no direct persistence
→ no direct hardware access
→ no preview-row replacement
```

Do not make the transport layer create WinUI controls.

Do not add a ViewModel framework merely to retain one page.

The current `OverlayWindow` Device preview fixture stays visually unchanged in SF-V2-06.

---

## 26. Leave `OverlayDelayedSliderCommit` alone in SF-V2-06

Current `OverlayDelayedSliderCommit` still contains the old infrastructure-era:

```text
ProductionDelay = 2000 ms
```

The Shared Frontend architecture ultimately requires the **real shared Device binding** to take its delay from:

```text
row.CommitPolicy.DelayMilliseconds
```

But that binding does not exist until SF-V2-07.

Therefore SF-V2-06 must **not** opportunistically redesign `OverlayDelayedSliderCommit` or the preview slider infrastructure.

SF-V2-07 will decide the smallest adjustment required so the generic renderer uses shared row policy while preserving the helper's proven latest-draft/stale-settlement behavior.

Transport migration and UI debounce migration should not be mixed in this PR.

---

## 27. Hide / dismissal / late mutation result behavior

Preserve current OQ4 behavior.

Hide/dismiss must not wait for an already-submitted Quick Settings mutation.

Required normal sequence remains:

```text
Quick Settings mutation submitted
→ Runtime operation still running
→ Overlay close requested
→ server read loop still processes Hide/Dismiss
→ Hidden acknowledgement completes
→ OQ4 capture retirement continues
→ Runtime mutation may settle later
→ late response may be written/ignored according to connection/request lifetime
```

If the Overlay client disposed or retired its wait:

```text
already-admitted Runtime feature mutation may still finish
frontend lifetime does not roll back Runtime authority
stale/late response must not complete a newer request
```

No cancel-on-hide Runtime rollback is required.

No controller restoration belongs here.

---

## 28. Shutdown / sleep / restart / physical device loss

SF-V2-06 must not become a lifecycle authority.

### 28.1 Process shutdown

Once process shutdown begins:

```text
no new Overlay Quick Settings mutations admitted
no new Overlay Quick Settings refresh intentionally scheduled
existing _frontendControl.StateInvalidated Overlay handler unsubscribed as today
Overlay controller follows existing shutdown path
```

Do not add a second shutdown token hierarchy solely for v7.

### 28.2 Sleep / Hibernate / Resume

Do not modify Full1902/OQ4 suspend/resume sequencing.

Shared Quick Settings may receive ordinary Runtime invalidation after feature reconciliation and republish if the same Overlay session is still legitimately visible/captured.

It must not become a power-lifecycle participant.

### 28.3 Physical device loss / PnP re-enumeration

Do not touch physical controller recovery.

If existing OQ4/Full1902 logic retires Overlay capture because the physical source disappears:

```text
Quick Settings transport follows the retired surface
```

It does not reacquire DirectInput, repair HidHide, switch PID, or recover VIIPER.

### 28.4 Restart / crash

Overlay process death does not undo Device feature Runtime authority.

Runtime shutdown/restart continues using existing Full1902 ownership rules.

No Quick Settings recovery journal is added.

---

## 29. Failure-domain separation

Keep these failures independent.

### Page capture fails

```text
shared Device page becomes unavailable / skipped if connection is gone
Overlay remains alive
controller capture remains governed by OQ4
```

### Shared mutation returns `Succeeded=false`

```text
return correlated QuickSettingsMutationResult
include fresh authoritative Page
Overlay connection remains alive
```

### Mutation delegate throws

```text
return narrow correlated Error if possible
connection remains usable when the pipe itself is healthy
```

### Malformed v7 frame

```text
protocol-invalid
invoke zero Runtime mutation
connection may be rejected/closed according to current strict wire policy
```

### Pipe/process dies while Overlay was visible

```text
existing VisibleSessionLost / OQ4 unified capture retirement path
```

A Quick Settings feature failure must never be classified as `VisibleSessionLost`.

---

## 30. Do not expand the shared Device product in this PR

Current shared Device page is exactly the product projection already established by SF-V2-03 and consumed by QAM after SF-V2-05:

```text
TDP
CPU Boost
Windows Power Mode
```

Do not add:

```text
Fan Control
Battery Charge Limit
LED
Vibration strength
Center M authority
Developer probes
Intel FPS Limit
Resolution
```

merely because Runtime capability, new architecture documents, or future UI plans exist.

Shared-page inclusion remains a separate product decision requiring QAM + Overlay parity.

---

## 31. Forbidden architecture / over-engineering guardrails

Do not create merely for SF-V2-06:

```text
new process
new named pipe
new project/csproj
QuickSettingsTransportManager
QuickSettingsSessionManager
OverlayFeatureRegistry
page registry/plugin framework
reflection dispatcher
arbitrary method-name RPC
Dictionary<string, object> payloads
JSON form engine
generic event bus
cross-surface transaction manager
page revision vector
epoch/barrier protocol
multi-session authority
RDP/Fast User Switching handling
second Runtime/frontend control
second Overlay read loop
second byte-stream write gate
worker service/job queue
```

Supported product assumptions remain:

```text
one Windows user
one interactive session
```

Do not add unsupported multi-session machinery.

The realistic safety requirements that **must** remain protected are:

```text
Hide while slow TDP mutation is running
Overlay process/pipe loss while captured
shutdown
sleep/hibernate/resume
physical controller loss / PnP lifecycle
Full1902 fail-close/teardown
actual operation failures
```

---

## 32. Expected production file footprint

Likely modified files:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
```

Likely test files:

```text
tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
    rename/rewrite to generic Quick Settings terminology if useful

tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
    protocol/lifecycle expectations only where needed

tests/SteamInputAddonforClaw.Tests/OverlayTabOrderTransportTests.cs
    only if protocol-number assertions need update
```

Expected unchanged production areas:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
src/SteamInputAddonforClaw.QamHost/**
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
QuickSettingsPresentation.cs
QuickSettingsMutationAdapter.cs
Full1902 physical/HidHide/VIIPER owners
```

If a shared contract change appears necessary, stop and prove why. SF-V2-03 already provides the intended v7 payload contract.

---

# Required tests

## 33. Protocol / handshake tests

Prove:

```text
OverlayTransportProtocol.CurrentVersion == 7
FrontendTransportProtocol.CurrentVersion == 28  // unless current main legitimately changed first
v6 peer is rejected by v7 server
```

Preserve mandatory startup order:

```text
Handshake
→ HandshakeAccepted
→ TabOrderState
→ client applies TabOrderState
→ Ready
```

Prove `QuickSettingsPageState` is **not** a mandatory pre-Ready frame.

Do not make Device page availability a prerequisite for Overlay readiness.

---

## 34. Shared Device page round-trip

Build a representative `QuickSettingsPageSnapshot(Device)` that exercises the real generic shape, not just a page header.

The test page should include enough content to prove preservation of:

```text
section order
section labels
row order
row labels
Toggle control kind
Numeric SliderSpec min/max/step/suffix
Discrete SliderSpec ordered options
Available / Writable
Boolean / Integer value shapes
Immediate commit policy
TrailingDebounce delay value
TDP CommitGroupId
linked slider constraints
```

Send it Runtime -> Overlay and assert structural equality after round trip.

Include at least:

```text
one TDP numeric row
one CPU Boost discrete row
one Windows Power Mode discrete row
```

Prefer using `QuickSettingsPresentation.BuildDevice(...)` or an equivalent shared-fixture helper if that keeps the test tied to the real contract without making the transport test depend on private implementation details.

---

## 35. Unavailable/partial page delivery

Prove a valid generic Device page can represent partial product availability without becoming a transport failure.

Examples:

```text
TDP rows unavailable
CPU Boost healthy
Power Mode healthy
```

and:

```text
QuickSettingsPageSnapshot.Unavailable(Device)
```

Both must cross the wire as normal page states.

Hidden/unready server state must still refuse normal page publication.

---

## 36. Generic mutation round-trip

Test representative shared intents, not a duplicate eight-feature dispatch matrix.

At minimum:

### Toggle

```text
EditedRowId = DeviceCpuBoostEnabled
Values = [Boolean]
```

### Independent slider

```text
EditedRowId = DevicePowerModeAc
Values = [Integer]
```

### Grouped TDP slider

```text
EditedRowId = one TDP slider
Values = complete five-row TDP draft
```

For each, prove:

```text
Overlay client sends exact QuickSettingsMutationIntent
bound Runtime generic delegate receives it exactly once
response RequestId matches
QuickSettingsMutationResult returns intact
```

Do not re-test the full row -> typed-method product mapping here. `QuickSettingsMutationAdapterTests` owns that.

---

## 37. Central adapter ownership regression

Add a focused regression proving the production Overlay binding reaches:

```text
IAddonFrontendControl.MutateQuickSettingAsync
```

and does not retain:

```text
OverlayDeviceMutationDispatch
```

The v7 transport should not contain Device CPU/TDP/Power method-name dispatch.

Existing central adapter tests must remain green.

---

## 38. Strict malformed generic request tests

Prove **zero Runtime mutation calls** for malformed wire input including:

```text
RequestId = 0 / negative
missing request Intent
missing Intent.PageId
missing Intent.EditedRowId
missing nested RowId
missing nested Value.Kind
Values = null
null nested row-value entry
null nested Value
unknown enum string
numeric enum token where string enum is required
```

Distinguish:

```text
wire-structural invalidity
→ reject before Runtime delegate

structurally valid but product-invalid intent
→ may reach QuickSettingsMutationAdapter
→ adapter returns Succeeded=false + fresh page
→ zero typed feature mutation there
```

This boundary is important. Do not move product validation back into the transport.

---

## 39. Hidden / unready / not-captured / shutdown admission

For a well-formed Device intent, prove each condition individually invokes zero Runtime mutation:

```text
connection not Ready
Ready but Hidden
Visible acknowledgement but OQ4 capture not committed
process shutdown started
```

Also prove a non-exposed `Profile` intent invokes zero Runtime mutation in SF-V2-06.

Keep the connection usable when the request itself is structurally valid and the only failure is admission.

---

## 40. Typed generic failure is not transport failure

Return a shared result such as:

```csharp
new QuickSettingsMutationResult(
    false,
    "Device update failed.",
    authoritativeDevicePage)
```

Prove:

```text
client receives Succeeded=false
FailureMessage survives
Page survives intact
connection remains usable
```

Then perform another ordinary Overlay operation, for example navigation/command/tab-order or another mutation, to prove the pipe is still healthy.

---

## 41. Thrown mutation remains feature-local

Inject a bound generic mutation delegate that throws.

Prove:

```text
client receives a narrow correlated operation/transport error
server read loop survives
Overlay command/state transport remains usable
```

Do not turn the throw into visible-session teardown if the pipe itself remains healthy.

---

## 42. Critical regression: slow generic TDP mutation must not block Hide

Port the existing v6 deterministic test to the shared generic intent.

Use a mutation delegate blocked on `TaskCompletionSource`.

Required sequence:

```text
connect / Ready
→ Show / Visible
→ submit a valid grouped TDP QuickSettingsMutationIntent
→ prove mutation delegate entered and remains blocked
→ issue Runtime Hide command
→ client processes Hide and sends Hidden
→ server Hide completes BEFORE blocked mutation is released
→ release mutation
→ correlated QuickSettingsMutationResult arrives afterward if connection remains active
→ connection remains usable
```

This is a merge-blocking lifecycle regression if broken.

Do not replace it with stress-loop race tests.

---

## 43. Mutation self-invalidation ordering regression

Add a deterministic regression for the normal Runtime ordering described in section 23.

Required behavior to prove:

```text
admitted Overlay generic mutation is marked in-flight
→ mutation path raises StateInvalidated before returning
→ Overlay-specific ordinary page refresh is NOT started for that self-invalidation
→ mutation returns QuickSettingsMutationResult with authoritative Page
→ result remains the settlement source
→ after mutation is no longer in-flight, a later ordinary external StateInvalidated can refresh again
```

Prefer a focused existing host/test seam.

Do not introduce a new manager solely to make this test easy.

The test is about the **normal mutation-generated invalidation**, not arbitrary scheduler interleavings.

---

## 44. Request correlation / retired result

Preserve the current v6 proof with generic response wrappers.

Sequence:

```text
request A sent
→ A's local wait is cancelled/retired
→ request B becomes current
→ late result A arrives
→ A is ignored
→ B remains pending
→ result B arrives
→ B completes correctly
```

Do not add revision vectors or a general RPC pending dictionary.

---

## 45. Page publication visibility gating

Prove:

```text
unready → no QuickSettingsPageState
hidden → no QuickSettingsPageState
visible + captured → page can publish
```

Also preserve the realistic hide-during-capture case:

```text
refresh begins while visible
→ page capture is intentionally blocked in test
→ Overlay becomes hidden
→ release capture
→ no page is intentionally sent to hidden session
```

Use the current final Ready/Visible send check. No epoch required.

---

## 46. StateInvalidated is event-driven only

Preserve/adjust tests proving:

```text
frontend StateInvalidated while captured + visible
→ shared Device page refresh requested

hidden / capture inactive / shutdown
→ no refresh
```

Add no polling.

A source assertion against new timers is acceptable only as supplementary evidence; behavior tests are preferred where current test seams already exist.

---

## 47. One reader / one write gate regressions

Preserve tests or source invariants proving:

```text
NamedPipeOverlayClient.RunAsync = sole pipe reader after handshake
mutation send helper does not read the pipe
all server writes use the same _writeGate
all client writes use the same _writeGate
```

No second pipe and no second byte-stream write semaphore.

---

## 48. v6 production cleanup assertions

After migration, current production code must not contain the old v6 Device transport API.

At minimum verify removal of:

```text
OverlayWireMessageKind.DeviceQuickSettingsState
OverlayWireMessageKind.DeviceMutationRequest
OverlayWireMessageKind.DeviceMutationResult
OverlayDeviceMutationKind
OverlayDeviceMutationRequest
OverlayDeviceMutationResponse
OverlayDeviceMutationDispatch
SendDeviceCpuBoostEnabledAsync
SendDeviceCpuBoostAcAsync
SendDeviceCpuBoostDcAsync
SendDeviceTdpEnabledAsync
SendDeviceTdpAsync
SendDevicePowerModeEnabledAsync
SendDevicePowerModeAcAsync
SendDevicePowerModeDcAsync
```

Historical docs may continue to contain these names.

Do not delete SF-V2-02 documentation; it records the lifecycle proof and historical v6 contract.

---

## 49. Preserve unrelated Overlay regressions

Run all existing OQ4/OQ5 transport/UI tests.

At minimum ensure no regressions in:

```text
handshake/Ready
Show/Visible
Hide/Hidden
Shutdown
DismissRequested
Navigation
PreviousTab/NextTab
TabOrderState / SetTabOrder
current-user pipe behavior
VisibleSessionLost handling
OQ4 capture/neutral publication
release-to-resume gate
```

SF-V2-06 does not redesign any of those systems.

---

## 50. Build / verification

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Builds should be warning-clean under the repository's current baseline.

Do not claim real Overlay Device UI parity from this PR. There is intentionally no real Device renderer yet.

---

## 51. Manual smoke verification

If the current hardware/runtime environment is available, perform a transport/lifecycle smoke test only.

Recommended:

```text
1. Start Runtime normally.
2. Confirm warm Overlay reaches Ready.
3. Open Overlay and confirm current preview UI still appears unchanged.
4. Confirm OQ4 controller capture still neutralizes game/Steam-facing navigation.
5. Close Overlay and confirm same presentation resumes normally.
6. Inspect logs for a received/published QuickSettingsPageSnapshot(Device) path if current logging makes this visible.
7. Confirm no protocol/transport loop failure.
```

Do not block this transport PR on a manual ability to edit real Device settings from Overlay; that feature does not exist until SF-V2-07.

---

## 52. Acceptance criteria

SF-V2-06 is complete only when all of the following are true:

```text
[ ] `.Overlay` protocol bumped exactly once to v7 from reviewed v6 baseline.
[ ] `.Frontend/.Qam` protocol remains unchanged at current source value.
[ ] v6 peer fails v7 handshake cleanly.
[ ] v6 Device-specific state/mutation production messages/types are removed.
[ ] `.Overlay` page state carries QuickSettingsPageSnapshot directly.
[ ] `.Overlay` mutation carries QuickSettingsMutationIntent directly inside a narrow correlation wrapper.
[ ] `.Overlay` mutation response carries QuickSettingsMutationResult directly inside a narrow correlation wrapper.
[ ] Overlay JSON decoding uses required-constructor enforcement and strict string enums.
[ ] malformed required generic identities fail before Runtime invocation.
[ ] null structural payload hazards fail closed before Runtime invocation.
[ ] product validation remains in QuickSettingsMutationAdapter, not Overlay transport.
[ ] current Overlay page exposure is Device only.
[ ] Runtime binding uses the existing one _frontendControl.
[ ] mutation dispatch reaches MutateQuickSettingAsync, not eight duplicated typed methods.
[ ] page capture uses CaptureQuickSettingsPageAsync(Device), not v6 aggregate projection at transport boundary.
[ ] slow mutation execution remains outside the sole server read loop.
[ ] Hide/Dismiss remains processable while TDP mutation is blocked.
[ ] one server write gate and one client write gate remain authoritative for frame bytes.
[ ] one client reader remains authoritative for incoming frames.
[ ] request correlation ignores late retired results.
[ ] Ready + Visible + _overlayCaptureActive + not-shutdown admission remains intact.
[ ] mutation-generated self-invalidation cannot race a redundant Overlay refresh over the authoritative result page.
[ ] OQ4 Show/capture ordering is unchanged; page publication happens only after capture commit.
[ ] StateInvalidated refresh remains event-driven with no polling.
[ ] feature failures do not become Overlay lifecycle failures.
[ ] Overlay App consumes generic page state but does not bind real Device controls yet.
[ ] OverlayWindow preview UI remains unchanged.
[ ] OverlayDelayedSliderCommit remains outside this transport PR.
[ ] Full1902 controller/HidHide/VIIPER authority is unchanged.
[ ] Fan Control is not opportunistically added to shared Device Quick Settings.
[ ] full test suite passes.
[ ] Debug and Release builds pass.
[ ] git diff --check is clean.
```

---

## 53. Review focus for this PR

Reviewers should prioritize realistic production defects:

```text
protocol mismatch not rejected
malformed generic payload can default to a valid row/page identity
null payload crashes the read loop after admission
old feature-specific dispatch remains a second product authority
slow TDP mutation blocks Hide/Dismiss
mutation result can be overwritten by its own StateInvalidated refresh
hidden/not-captured Overlay can mutate Runtime
late result can complete a newer request
writes can interleave on the same pipe
page capture delays OQ4 capture commit
feature failure tears down Overlay/controller capture
```

Do not block the PR for theoretical instruction-level races that require pathological scheduling and have no plausible supported lifecycle impact.

Do not request new state machines/epochs/barriers unless a concrete current product path proves they are necessary.

---

## 54. Next phase — do not pull it into this PR

After SF-V2-06 lands, SF-V2-07 will:

```text
receive QuickSettingsPageSnapshot(Device)
→ build Overlay-local generic Section/Row rendering
→ Toggle -> OverlayToggleRow
→ Slider -> OverlaySliderRow
→ register logical OQ5 selection/navigation
→ implement generic pending drafts by RowId / CommitGroupId
→ use row.CommitPolicy delay
→ apply shared LinkedSliderConstraints
→ call SendQuickSettingsMutationAsync(intent)
→ use QuickSettingsMutationResult.Page as authoritative settlement
→ replace the temporary Device preview fixture
```

That is the point where the Overlay becomes the second real renderer of the shared Device product.

Do not start SF-V2-07 inside SF-V2-06 merely because the new v7 transport makes it possible.

---

# Final implementation rule

The desired diff should read as a **transport contract replacement**, not as a new subsystem:

```text
keep the proven Overlay pipe/lifecycle machinery
remove the v6 Device-specific product transport
reuse the shared Quick Settings contract and central adapter
keep Device-only current exposure explicit
preserve OQ4 fail-safe behavior
leave the real WinUI generic renderer for SF-V2-07
```

If implementation starts requiring a new manager, registry, generic RPC framework, revision protocol, or controller-lifecycle abstraction, stop and re-check whether the design has drifted beyond SF-V2-06.