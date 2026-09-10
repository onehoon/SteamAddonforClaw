# Work Order — SF-V2-04: Frontend / QAM Generic Quick Settings RPC Seam

> **Date:** 2026-09-10  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase D  
> **Reviewed repository head:** `main` at `793fdb5549910e6c2fef30fba6917e6e0ec5e629`  
> **Previous phase:** SF-V2-03 shared Quick Settings product contract / Device projection / dispatch is present on current `main`  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR roadmap:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Next phase:** SF-V2-05 QAM Device generic renderer migration

---

## 1. Goal

Make the SF-V2-03 shared Quick Settings page/mutation contract reachable from `SteamInputAddonforClaw.QamHost.exe` through the **existing** `.Qam` named-pipe transport and the existing `QamFrontendBridge` allowlist.

This is a transport/bridge seam PR only.

It must add two generic operations conceptually equivalent to:

```text
CaptureQuickSettingsPage
MutateQuickSetting
```

so the architecture becomes:

```text
QamHost JavaScript bridge request
        ↓
QamFrontendBridge
        ↓
NamedPipeAddonFrontendClient (.Qam)
        ↓
existing FrontendWireCodec / NamedPipeAddonFrontendServer
        ↓
IAddonFrontendControl
        ↓
CaptureQuickSettingsPageAsync
or
MutateQuickSettingAsync
        ↓
SF-V2-03 projection / explicit mutation adapter
        ↓
existing typed Device feature authority
```

The visible QAM JavaScript renderer must **not** migrate in this PR.

The fundamental rule is:

> **Expose the already-existing shared Quick Settings product seam through the existing QAM transport. Do not redesign the product model, renderer, admission model, or Runtime authority.**

---

## 2. Why SF-V2-04 is still required on current `main`

Current `main` already contains the complete SF-V2-03 in-process layer:

```text
QuickSettingsContracts.cs
QuickSettingsPresentation.BuildDevice(...)
QuickSettingsMutationAdapter.MutateAsync(...)
IAddonFrontendControl.CaptureQuickSettingsPageAsync(...)
IAddonFrontendControl.MutateQuickSettingAsync(...)
InProcessAddonFrontendControl implementations
```

But the external frontend transport still stops one layer earlier.

Current `FrontendRpcMethod` ends with the existing Device aggregate operation:

```csharp
..., CaptureCenterMStartup,
RequestCenterMAuthorityTransition,
CaptureDeviceQuickSettings
```

There is currently no:

```text
FrontendRpcMethod.CaptureQuickSettingsPage
FrontendRpcMethod.MutateQuickSetting
```

`NamedPipeAddonFrontendClient` implements:

```csharp
public Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(...)
```

but does **not** implement the two generic `IAddonFrontendControl` methods over the pipe.

`QamFrontendBridge` currently exposes only the old feature-specific Device bridge operations used by `qam.js`, including:

```text
captureDeviceQuickSettings
setDeviceCpuBoostEnabled
setDeviceCpuBoostAc
setDeviceCpuBoostDc
setDeviceTdpEnabled
setDeviceTdp
setDevicePowerModeEnabled
setDevicePowerModeAc
setDevicePowerModeDc
```

Current `qam.js` still calls those methods and still owns the old Device product definition / pending-draft implementation.

Therefore the exact missing seam is:

```text
SF-V2-03 in-process generic API
        X
        X  missing transport / QAM bridge exposure
        X
QamHost
```

SF-V2-04 fills only this gap.

---

## 3. Latest-baseline verification

This work order was prepared after re-checking the latest repository default branch.

Reviewed head:

```text
793fdb5549910e6c2fef30fba6917e6e0ec5e629
```

Current protocol versions:

```text
FrontendTransportProtocol.CurrentVersion = 27
OverlayTransportProtocol.CurrentVersion  = 6
```

Current relevant production source facts:

```text
QuickSettingsPageId = Device / Profile
QuickSettingsControlKind = Toggle / Slider
QuickSettingsValueKind = Boolean / Integer
QuickSettingsCommitMode = Immediate / TrailingDebounce
QuickSettingsCommitGroupId.DeviceTdpConfiguration exists
QuickSettingsPageSnapshot exists
QuickSettingsMutationIntent exists
QuickSettingsMutationResult exists
QuickSettingsMutationAdapter exists
Device projection exists
Profile projection/mutation is still intentionally unavailable
QAM visible Device renderer is still legacy feature-specific
Overlay still uses its separate v6 Device transport
```

### Required protocol result for this PR

Because current `main` is still frontend protocol 27, SF-V2-04 should bump it exactly once:

```text
FrontendTransportProtocol 27 → 28
```

Overlay protocol must remain:

```text
OverlayTransportProtocol = 6
```

If `main` changes before implementation actually starts, re-read `FrontendWire.cs` first and bump **once from then-current source**. Do not force 28 if another legitimate PR has already consumed that number.

---

## 4. Required reading before implementation

Do not implement this from the short roadmap section alone.

### 4.1 Full PID1902 authority documents

Read in the precedence defined by the Full1902 README:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Frozen invariant for this work:

```text
Runtime = feature/controller/hardware/persistence authority
QAM = disposable presentation client
```

The QAM pipe must never become a controller authority, HidHide owner, VIIPER owner, or PID transition participant.

### 4.2 Shared Frontend authority

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md
docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md
docs/shared-frontend/SF_V2_03_SHARED_QUICK_SETTINGS_PRODUCT_CONTRACT_WORK_ORDER.md
```

Important handoff from SF-V2-03:

```text
QuickSettingsPageSnapshot(Device)
        ↓
closed shared product semantics

QuickSettingsMutationIntent
        ↓
explicit adapter
        ↓
existing typed Device mutations
        ↓
fresh QuickSettingsPageSnapshot(Device)
```

SF-V2-04 must expose this shape without creating a second generic product implementation.

### 4.3 Current source — inspect before editing

At minimum inspect the current versions of:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Program.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
```

Tests to inspect:

```text
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
```

Also search the full test tree for hard-coded frontend protocol `27` expectations before final verification.

---

## 5. Scope

Production changes should be limited to the existing frontend transport and QAM bridge.

Expected production files:

```text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
```

A very small testability-only constructor/seam in `QamFrontendBridge` is acceptable if required for deterministic named-pipe admission tests.

Expected test files:

```text
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
possibly focused current-version assertion tests
```

### Files that should normally have zero production diff

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw.UI/**
```

If these need material changes to implement SF-V2-04, stop and re-check the design.

---

## 6. Explicit non-goals

Do **not** include any of the following in SF-V2-04:

```text
qam.js generic Device renderer migration
QAM tab order changes
QAM default-tab selection changes
Steam native UI discovery changes
Steam ToggleField / SliderField / PanelSection changes
Device labels/order cleanup in JavaScript
QAM slider debounce cleanup
TDP pending-draft rewrite
Profile generic renderer
Profile generic mutation admission
Overlay v7 generic wire
Overlay real Device UI binding
Main UI migration to QuickSettingsPageSnapshot
new named pipe
new process/service
new Runtime authority
new Quick Settings state cache
new invalidation event
new mutation scheduler
controller lifecycle changes
```

Those belong to later focused PRs.

In particular, **do not combine the QAM Steam-native visual cleanup / Addon-first-tab work discussed separately with SF-V2-04**. That work must wait until the generic Device renderer is complete and current QAM structure is stable.

---

## 7. Target architecture after SF-V2-04

Read path:

```text
future qam.js
  request("captureQuickSettingsPage", ...)
        ↓
QamFrontendBridge
        ↓
NamedPipeAddonFrontendClient.CaptureQuickSettingsPageAsync
        ↓
FrontendRpcMethod.CaptureQuickSettingsPage
        ↓
NamedPipeAddonFrontendServer
        ↓
IAddonFrontendControl.CaptureQuickSettingsPageAsync
        ↓
InProcessAddonFrontendControl
        ↓
QuickSettingsPresentation.BuildDevice
```

Mutation path:

```text
future qam.js
  request("mutateQuickSetting", QuickSettingsMutationIntent)
        ↓
QamFrontendBridge
        ↓
QAM Device admission
(Big Picture + no running Steam AppId)
        ↓
NamedPipeAddonFrontendClient.MutateQuickSettingAsync
        ↓
FrontendRpcMethod.MutateQuickSetting
        ↓
NamedPipeAddonFrontendServer
        ↓
IAddonFrontendControl.MutateQuickSettingAsync
        ↓
QuickSettingsMutationAdapter
        ↓
existing typed Device mutation
        ↓
fresh authoritative QuickSettingsPageSnapshot
```

No new authority appears anywhere in this chain.

---

## 8. Frontend protocol bump

Update `FrontendWire.cs` from current:

```csharp
public static class FrontendTransportProtocol
{
    public const int CurrentVersion = 27;
}
```

to the then-current + 1 version.

For the reviewed baseline:

```csharp
public static class FrontendTransportProtocol
{
    public const int CurrentVersion = 28;
}
```

Add a concise version-history comment adjacent to the current v27 history.

Recommended wording:

```csharp
// Version 28: Shared Frontend V2 SF-V2-04 exposes the closed shared Quick Settings
// product seam through .Frontend/.Qam via CaptureQuickSettingsPage and MutateQuickSetting.
// The new RPC methods carry QuickSettingsPageSnapshot / QuickSettingsMutationIntent /
// QuickSettingsMutationResult. A v27 peer does not implement this wire contract, so fail
// the handshake up front. Pre-release: no compatibility shim.
```

Do not add dual-version compatibility.

Do not change `.Overlay` protocol version.

---

## 9. Add exactly two frontend RPC methods

Extend `FrontendRpcMethod` with:

```csharp
CaptureQuickSettingsPage,
MutateQuickSetting
```

Prefer appending them to the current enum rather than reordering existing methods.

Illustrative target:

```csharp
internal enum FrontendRpcMethod
{
    Unknown = 0,
    // existing methods unchanged...
    CaptureCenterMStartup,
    RequestCenterMAuthorityTransition,
    CaptureDeviceQuickSettings,
    CaptureQuickSettingsPage,
    MutateQuickSetting,
}
```

The wire converter serializes RPC method names exactly, so the spelling is part of the protocol contract.

Required exact wire names:

```text
CaptureQuickSettingsPage
MutateQuickSetting
```

No generic method-name string supplied by the client is allowed.

Do not create:

```text
InvokeQuickSetting(string methodName, object payload)
GenericRpc
DynamicMutation
reflection dispatch
```

---

## 10. Capture request wire payload

`CaptureQuickSettingsPageAsync` requires both:

```text
QuickSettingsPageId pageId
optional uint AppId
```

Use one small transport request record in the existing frontend wire assembly.

Recommended shape:

```csharp
internal sealed record CaptureQuickSettingsPageRequest(
    QuickSettingsPageId PageId,
    uint? AppId);
```

Do not make this transport wrapper public merely so QamHost can reuse it.

QamHost may use its own tiny bridge-local DTO or parse its bridge payload directly.

Do not add a generic context property bag.

### Capture request examples

Device:

```json
{
  "pageId": "Device",
  "appId": null
}
```

Future Profile-capable frontend wire shape:

```json
{
  "pageId": "Profile",
  "appId": 480
}
```

Important: the examples above describe the **named-pipe wire codec**, whose current `FrontendWireCodec.Json` uses `JsonStringEnumConverter(... allowIntegerValues: false)`.

Do not change that codec policy.

---

## 11. Mutation request wire payload

Do not invent another duplicate product DTO.

Use the SF-V2-03 contract directly:

```csharp
QuickSettingsMutationIntent
```

The client can send it directly as the RPC payload.

Recommended client-side shape:

```csharp
FrontendWireCodec.Payload(intent)
```

not:

```csharp
new Dictionary<string, object> { ... }
new { Method = "SetDeviceTdp", ... }
object JsonPayload
```

A wrapper record such as `MutateQuickSettingRequest(Intent)` is unnecessary unless the current implementation discovers a concrete serialization reason for it.

Keep the simplest typed wire.

---

## 12. `NamedPipeAddonFrontendClient` implementation

`NamedPipeAddonFrontendClient` implements `IAddonFrontendControl` but currently relies on the interface's fail-closed default for the generic methods because no concrete pipe methods exist.

Add concrete implementations.

Recommended code:

```csharp
public Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(
    QuickSettingsPageId pageId,
    uint? appId = null,
    CancellationToken cancellationToken = default) =>
    SendAsync<QuickSettingsPageSnapshot>(
        FrontendRpcMethod.CaptureQuickSettingsPage,
        FrontendWireCodec.Payload(new CaptureQuickSettingsPageRequest(pageId, appId)),
        cancellationToken);

public Task<QuickSettingsMutationResult> MutateQuickSettingAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken cancellationToken = default) =>
    SendAsync<QuickSettingsMutationResult>(
        FrontendRpcMethod.MutateQuickSetting,
        FrontendWireCodec.Payload(intent),
        cancellationToken);
```

Use the existing request correlation, cancellation, read-loop, and write gate.

Do not add another pending-request dictionary or Quick Settings-specific correlation mechanism.

---

## 13. `NamedPipeAddonFrontendServer` dispatch

The server must dispatch the new methods to the two existing `IAddonFrontendControl` generic methods.

### 13.1 Capture

Conceptual server mapping:

```csharp
var request = FrontendWireCodec.Decode<CaptureQuickSettingsPageRequest>(payload);
var page = await _inner.CaptureQuickSettingsPageAsync(
    request.PageId,
    request.AppId,
    cancellationToken).ConfigureAwait(false);
return FrontendWireCodec.Payload(page);
```

### 13.2 Mutation

Conceptual mapping:

```csharp
var intent = FrontendWireCodec.Decode<QuickSettingsMutationIntent>(payload);
var result = await _inner.MutateQuickSettingAsync(
    intent,
    cancellationToken).ConfigureAwait(false);
return FrontendWireCodec.Payload(result);
```

A tiny private helper is acceptable if it avoids repeatedly decoding the same payload in the existing large `InvokeAsync` expression.

Example:

```csharp
private async Task<JsonElement> InvokeQuickSettingsCaptureAsync(
    JsonElement? payload,
    CancellationToken cancellationToken)
{
    var request = FrontendWireCodec.Decode<CaptureQuickSettingsPageRequest>(payload);
    var result = await _inner.CaptureQuickSettingsPageAsync(
        request.PageId,
        request.AppId,
        cancellationToken).ConfigureAwait(false);
    return FrontendWireCodec.Payload(result);
}
```

Do **not** refactor the entire existing `InvokeAsync` dispatcher merely because it is visually large. SF-V2-04 does not justify a transport-wide rewrite.

---

## 14. Keep product validation in SF-V2-03

This is critical.

`QuickSettingsMutationAdapter` already owns:

```text
Device PageId / AppId validation
Toggle Boolean shape validation
CPU Boost defined-enum validation
Power Mode defined-enum validation
TDP complete 5-value group validation
duplicate/missing/unrelated row rejection
zero typed mutations for malformed intents
fresh authoritative page after valid mutation attempts
```

The frontend transport must **not** duplicate those product rules.

Transport validation should remain transport validation:

```text
missing payload
invalid JSON shape
unknown enum token rejected by the current strict wire codec
missing required constructor members
unsupported RPC method
protocol mismatch
```

Once a valid typed `QuickSettingsMutationIntent` has been decoded, pass it to:

```csharp
_inner.MutateQuickSettingAsync(intent, token)
```

and let the existing SF-V2-03 adapter remain the product validation/dispatch authority.

Do not add another row switch in `NamedPipeAddonFrontendServer`.

Do not add another Device feature switch in `QamFrontendBridge`.

---

## 15. Preserve current `FrontendWireCodec.Json`

Current frontend transport JSON options intentionally include:

```csharp
RespectRequiredConstructorParameters = true,
Converters =
{
    new FrontendRpcMethodJsonConverter(),
    new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
}
```

Keep this behavior.

This gives the named-pipe contract strict, readable enum names and prevents arbitrary numeric enum values from being accepted as valid wire representation.

Do not loosen it to support the new Quick Settings payload.

Do not introduce a second Quick Settings-specific wire codec.

---

## 16. QAM bridge methods

Add two **QamHost JS allowlist names**:

```text
captureQuickSettingsPage
mutateQuickSetting
```

Illustrative switch extension:

```csharp
object result = method switch
{
    "captureStatus" => await _client.CaptureStatusAsync(token),
    "captureDeviceQuickSettings" => await _client.CaptureDeviceQuickSettingsAsync(token),

    // SF-V2-04 generic seam; qam.js starts consuming these in SF-V2-05.
    "captureQuickSettingsPage" => await CaptureQuickSettingsPageAsync(root, token),
    "mutateQuickSetting" => await MutateQuickSettingAsync(root, token),

    // existing feature-specific operations remain for SF-V2-04...
};
```

Use exact lower-camel bridge method names shown above.

Do not expose arbitrary `IAddonFrontendControl` methods.

---

## 17. QAM bridge capture payload

A bridge-local request record is acceptable and preferable to making the frontend-transport DTO public.

Example:

```csharp
private sealed record QuickSettingsPageBridgeRequest(
    QuickSettingsPageId PageId,
    uint? AppId);
```

Example helper:

```csharp
private async Task<object> CaptureQuickSettingsPageAsync(
    JsonElement root,
    CancellationToken cancellationToken)
{
    var request = root.GetProperty("payload")
        .Deserialize<QuickSettingsPageBridgeRequest>(BridgeJson)
        ?? throw new JsonException("Invalid Quick Settings page request.");

    if (!Enum.IsDefined(request.PageId))
        throw new JsonException("Invalid Quick Settings page id.");

    return await _client.CaptureQuickSettingsPageAsync(
        request.PageId,
        request.AppId,
        cancellationToken).ConfigureAwait(false);
}
```

### Current page behavior

For this PR:

```text
Device + AppId null
→ real shared Device page

Profile
→ existing InProcessAddonFrontendControl returns explicit unavailable Profile page

unknown/invalid page
→ bounded bridge/wire error; never silently fall back to Device
```

Do not implement Profile projection here.

---

## 18. QAM Device mutation admission must remain surface-owned

Current QAM Device mutations are admitted only when:

```text
status.Steam.Active == true
AND status.Steam.AppId == 0
AND status.Steam.Source == FrontendSteamSource.BigPicture
```

This remains mandatory.

The generic Device mutation path must pass through the **same** policy.

Do not move this rule into:

```text
QuickSettingsMutationAdapter
QuickSettingsPresentation
InProcessAddonFrontendControl
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
```

### Recommended small deduplication

Current `QamFrontendBridge.MutateAsync(...)` embeds the admission check directly.

It is reasonable to extract exactly that check into one private helper and reuse it from both old feature-specific Device mutations and the new generic Device mutation.

Example:

```csharp
private async Task EnsureDeviceMutationAdmittedAsync(CancellationToken token)
{
    var status = await _client.CaptureStatusAsync(token).ConfigureAwait(false);
    if (!status.Steam.Active ||
        status.Steam.AppId != 0 ||
        status.Steam.Source != FrontendSteamSource.BigPicture)
    {
        throw new InvalidOperationException(
            "Device QAM mutation is available only in Big Picture with no running game.");
    }
}
```

Then preserve the existing legacy path:

```csharp
private async Task<object> MutateAsync(...)
{
    await EnsureDeviceMutationAdmittedAsync(token).ConfigureAwait(false);
    return await mutation(
        _client,
        root.GetProperty("payload"),
        token).ConfigureAwait(false);
}
```

and add the generic path:

```csharp
private async Task<object> MutateQuickSettingAsync(
    JsonElement root,
    CancellationToken token)
{
    var intent = root.GetProperty("payload")
        .Deserialize<QuickSettingsMutationIntent>(BridgeJson)
        ?? throw new JsonException("Invalid Quick Settings mutation intent.");

    // SF-V2-04 / 05 only expose Device mutation through this generic QAM path.
    // Profile generic admission is a later focused milestone.
    if (intent.PageId != QuickSettingsPageId.Device)
        throw new InvalidOperationException(
            "Only Device Quick Settings mutation is available through the QAM generic seam.");

    await EnsureDeviceMutationAdmittedAsync(token).ConfigureAwait(false);

    return await _client.MutateQuickSettingAsync(
        intent,
        token).ConfigureAwait(false);
}
```

Important distinction:

- checking `PageId == Device` here is **surface scope/admission** for SF-V2-04;
- validating `AppId`, row/value shapes, TDP group completeness, and enum values remains the SF-V2-03 mutation adapter's job.

Do not reproduce the adapter switch in the bridge.

---

## 19. Preserve `QamFrontendBridge.BridgeJson` compatibility

Current bridge options are:

```csharp
internal static readonly JsonSerializerOptions BridgeJson = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

Do **not** globally add `JsonStringEnumConverter` to `BridgeJson` in this PR.

The existing QAM bridge / JavaScript paths already exchange enum-backed values using the bridge's current JSON behavior. Changing the global enum representation while `qam.js` still uses the old feature-specific path risks a visible regression in CPU Boost / Power Mode / other existing QAM behavior.

Therefore preserve:

```text
named-pipe FrontendWireCodec
→ current strict string-enum wire

QamFrontendBridge ↔ qam.js
→ current camelCase bridge serialization semantics
```

SF-V2-05 can consume the generic page payload exactly as this bridge serializes it.

Do not change all existing QAM enum semantics just to make the new generic payload look prettier in diagnostics.

---

## 20. Existing QAM Device methods remain temporarily

SF-V2-04 is intentionally a transition state.

Keep these existing bridge methods because current `qam.js` still calls them:

```text
captureDeviceQuickSettings
setDeviceCpuBoostEnabled
setDeviceCpuBoostAc
setDeviceCpuBoostDc
setDeviceTdpEnabled
setDeviceTdp
setDevicePowerModeEnabled
setDevicePowerModeAc
setDevicePowerModeDc
```

Add a concise code comment if helpful:

```csharp
// SF-V2-04 transition: qam.js still uses the feature-specific Device bridge operations.
// Remove them only after SF-V2-05 migrates Device rendering/mutation to the generic seam.
```

Do not remove the corresponding focused methods from `NamedPipeAddonFrontendClient` either. Main UI and other code may still use focused typed feature methods after QAM stops using them.

---

## 21. `qam.js` must remain behaviorally unchanged

There should be no SF-V2-04 production change to:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

Current source should still contain, after this PR:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
request("captureDeviceQuickSettings")
request("setDeviceCpuBoostEnabled", ...)
request("setDeviceCpuBoostAc", ...)
request("setDeviceCpuBoostDc", ...)
request("setDeviceTdpEnabled", ...)
request("setDeviceTdp", ...)
request("setDevicePowerModeEnabled", ...)
request("setDevicePowerModeAc", ...)
request("setDevicePowerModeDc", ...)
```

The new bridge operations exist but are not yet called by JavaScript.

This is intentional.

SF-V2-05 owns the migration and duplicate removal.

---

## 22. State invalidation behavior

Do not add a Quick Settings-specific invalidation event.

Current flow remains:

```text
existing typed mutation
→ existing Runtime/frontend StateInvalidated
→ NamedPipeAddonFrontendServer notification
→ NamedPipeAddonFrontendClient.StateInvalidated
→ QamFrontendBridge.StateInvalidated
→ current qam.js recapture path
```

The generic mutation adapter intentionally does not raise another duplicate invalidation.

SF-V2-04 must preserve that.

Do not add:

```text
QuickSettingsChanged
PageInvalidated
RowChanged
revision number
page generation on the Runtime transport
```

The existing request correlation and current QAM document-generation handling are sufficient for this seam.

---

## 23. Cancellation / disconnect / shutdown behavior

Use the existing frontend transport semantics without a new Quick Settings exception policy.

Required behavior:

```text
caller cancellation
→ existing request cancellation path
→ FrontendRemoteErrorCode.Cancelled as today

malformed wire payload
→ existing FrontendProtocolException / InvalidMessage path

Runtime operation exception
→ existing OperationFailed path

QAM host stopping
→ QamFrontendBridge stops accepting as today

frontend pipe disconnect
→ Runtime feature/controller authority survives
```

Do not catch `OperationCanceledException` in the Quick Settings product layer and turn it into `Succeeded=false`.

Do not turn a Quick Settings feature failure into QAM/Runtime lifecycle failure.

---

## 24. Named-pipe transport tests

Extend `FrontendNamedPipeTransportTests` rather than creating a second transport-test framework.

Use the existing recording/fake frontend control pattern.

### 24.1 Generic Device page round trip

Configure the fake control to return a representative `QuickSettingsPageSnapshot` containing at least:

```text
Device page
TDP section
CPU Boost section
Power Mode section
Toggle row
Numeric slider row
Discrete slider row
TrailingDebounce policy
DeviceTdpConfiguration commit group
linked slider constraint
```

Then assert:

```csharp
var actual = await client.CaptureQuickSettingsPageAsync(
    QuickSettingsPageId.Device);

Assert.Equal(expected, actual);
Assert.Equal(QuickSettingsPageId.Device, recordedPageId);
Assert.Null(recordedAppId);
Assert.Equal(1, captureQuickSettingsPageCalls);
```

The purpose is to prove the complete closed page contract survives the real codec, not only the top-level PageId.

### 24.2 Context round trip

Also prove the request can carry an AppId without losing it at the transport boundary:

```csharp
await client.CaptureQuickSettingsPageAsync(
    QuickSettingsPageId.Profile,
    appId: 480);

Assert.Equal(QuickSettingsPageId.Profile, recordedPageId);
Assert.Equal((uint)480, recordedAppId);
```

The fake may return `QuickSettingsPageSnapshot.Unavailable(Profile, 480)`.

This test does **not** implement Profile product behavior. It only proves the generic wire carries context.

### 24.3 Mutation intent/result round trip

Use at least one independent row and one grouped TDP-shaped intent.

Example independent intent:

```csharp
var intent = new QuickSettingsMutationIntent(
    QuickSettingsPageId.Device,
    AppId: null,
    QuickSettingsRowId.DeviceCpuBoostEnabled,
    [
        new QuickSettingsRowValue(
            QuickSettingsRowId.DeviceCpuBoostEnabled,
            QuickSettingsValue.Boolean(true))
    ]);
```

Example grouped TDP intent:

```csharp
var intent = new QuickSettingsMutationIntent(
    QuickSettingsPageId.Device,
    AppId: null,
    QuickSettingsRowId.DeviceTdpAcPl1,
    [
        new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
        new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(20)),
        new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(25)),
        new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(15)),
        new(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(20)),
    ]);
```

Assert:

```text
server inner receives the same intent
result Succeeded/FailureMessage preserved
fresh result Page preserved
exactly one generic MutateQuickSettingAsync call
```

Do not require the transport fake to reimplement the SF-V2-03 Device mutation adapter.

### 24.4 Structural invalid wire requests

Use existing raw-frame test patterns to prove malformed generic payloads fail boundedly.

Examples:

```text
CaptureQuickSettingsPage missing payload
CaptureQuickSettingsPage missing PageId
unknown PageId token
MutateQuickSetting missing required Values
unknown row enum token
wrong primitive JSON type
```

Expected result should follow current transport conventions:

```text
InvalidMessage / FrontendProtocolException path
zero generic inner invocation
connection remains in the normal state expected by the existing request-error contract
```

Do not duplicate semantic adapter validation such as the entire TDP group matrix here; those tests already belong to `QuickSettingsMutationAdapterTests`.

---

## 25. Protocol-version tests

Because this PR changes the frontend wire contract, add/update tests proving:

```text
FrontendTransportProtocol.CurrentVersion == 28
```

for the reviewed baseline.

Also prove a v27 frontend peer is rejected by the v28 server handshake using the existing stale-client/version test pattern.

Conceptual test:

```csharp
await using var staleClient = new NamedPipeAddonFrontendClient(
    pipeName,
    version: 27);

await Assert.ThrowsAsync<FrontendProtocolException>(
    () => staleClient.ConnectAsync());
```

Adapt to the current test helper pattern rather than forcing this exact code if the test harness differs.

### Search all current-version assertions

Current `main` contains hard-coded frontend `27` expectations in multiple historical contract tests, including at least:

```text
CenterMStartupContractTests
VibrationContractRemovalTests
FrontButtonTransportContractTests
OverlayDeviceQuickSettingsTransportTests
```

Update **current-version expectations/comments** to 28 where they are asserting the repository's live frontend protocol.

Do not rewrite historical version comments that correctly document old protocol milestones.

Keep:

```text
OverlayTransportProtocol.CurrentVersion == 6
```

---

## 26. QAM bridge tests — deterministic admission

The roadmap specifically requires proving the generic Device QAM mutation preserves the current admission rule.

Current `QamFrontendBridgeTests` mostly validates local decode/error behavior, while `QamFrontendContractTests` source-checks the admission predicate.

Prefer adding deterministic behavior tests for the new generic method instead of relying only on a string-source assertion.

### Minimal test seam if required

Do not add a mock-client interface hierarchy.

A tiny internal pipe-name constructor is enough.

Example:

```csharp
private readonly NamedPipeAddonFrontendClient _client;

internal QamFrontendBridge()
    : this(FrontendPipeEndpoint.CreateQamForCurrentUser())
{
}

internal QamFrontendBridge(string pipeName)
{
    _client = new NamedPipeAddonFrontendClient(pipeName);
    _client.StateInvalidated += OnStateInvalidated;
}
```

This keeps production behavior identical while allowing tests to create an isolated random pipe.

If the current test harness already has a simpler safe way to inject a client/pipe, reuse it instead.

Do not create `IQamFrontendClient`, `QuickSettingsBridgeService`, or another DI container merely for these tests.

### Required behavior cases

#### A. Device mutation allowed

Status:

```text
Steam.Active = true
Steam.AppId = 0
Steam.Source = BigPicture
```

Request:

```text
method = mutateQuickSetting
PageId = Device
valid typed intent
```

Expected:

```text
Response.Ok = true when fake Runtime returns success
one generic MutateQuickSettingAsync call
```

#### B. Running Steam game rejects Device mutation

Status:

```text
Steam.Active = true
Steam.AppId = 480
Steam.Source = Actual or BigPicture according to test fixture
```

Expected:

```text
Response.Ok = false
zero generic MutateQuickSettingAsync calls
```

#### C. Non-Big-Picture rejects Device mutation

Status example:

```text
Steam.Active = true
Steam.AppId = 0
Steam.Source = Actual
```

Expected:

```text
Response.Ok = false
zero generic MutateQuickSettingAsync calls
```

#### D. Non-Device generic mutation is not accidentally admitted

Until the Profile generic milestone exists:

```text
PageId = Profile
→ bounded rejection from QamFrontendBridge
→ zero generic Runtime mutation call
```

This prevents SF-V2-04 from accidentally creating a future Profile mutation policy by transport availability alone.

---

## 27. QAM bridge JSON tests

Because the JS bridge and named-pipe wire intentionally use different serializer options, add at least one focused bridge test proving a representative generic payload decodes with **current `BridgeJson` semantics**.

Recommended pattern:

```csharp
var intent = new QuickSettingsMutationIntent(
    QuickSettingsPageId.Device,
    null,
    QuickSettingsRowId.DeviceCpuBoostEnabled,
    [new(
        QuickSettingsRowId.DeviceCpuBoostEnabled,
        QuickSettingsValue.Boolean(true))]);

var json = JsonSerializer.Serialize(
    new
    {
        id = 1,
        method = "mutateQuickSetting",
        payload = intent,
    },
    QamFrontendBridge.BridgeJson);
```

Then send it through the real bridge test path.

Do not change `BridgeJson` globally to string enums just to make this test resemble the named-pipe payload.

---

## 28. Existing QAM regression tests must remain green

Preserve all current QAM contracts, especially:

```text
existing fiber patch/restore contract
QAM install/uninstall teardown gate
old-document response retirement
document-generation handling
current Device aggregate read path
current CPU/TDP/Power feature-specific mutation paths
slider pending-draft retirement
native Steam control discovery
current Big Picture/no-game mutation gate
```

Because `qam.js` is intentionally unchanged, any visible QAM behavior regression in SF-V2-04 is a defect.

Do not weaken existing source-contract tests simply because generic bridge operations were added.

If a test currently asserts the exact bridge allowlist and needs extension, add the new methods without deleting assertions for the old transition methods.

---

## 29. Existing SF-V2-03 tests remain authoritative

Do not move or duplicate all product validation tests into transport tests.

Keep these as the authority for generic product behavior:

```text
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QuickSettingsInProcessSeamTests
```

They must continue proving:

```text
exact Device section/row order
shared labels/options
commit policy
TDP group / linked constraints
malformed intent = zero typed mutation
valid intent = exact one typed mutation
fresh authoritative re-projection
Profile unavailable
no duplicate StateInvalidated authority
```

SF-V2-04 only proves that the exact typed contract reaches that seam through `.Qam`.

---

## 30. No `.Overlay` change

Do not edit the Overlay transport for SF-V2-04.

Current transition state remains:

```text
.Frontend/.Qam
→ generic Quick Settings seam added in SF-V2-04

.Overlay
→ existing Device-specific v6 transport remains temporarily
```

Required after this PR:

```text
OverlayTransportProtocol.CurrentVersion == 6
OverlayDeviceMutationKind still exists
OverlayDeviceMutationDispatch still exists
current Overlay Device transport tests remain green
```

SF-V2-06 is the dedicated generic Overlay transport replacement.

Do not prematurely implement it here.

---

## 31. No Main UI change

The desktop Main UI remains independent and continues using its current typed/aggregate paths.

Do not migrate `DevicePage.xaml.cs` to the generic page renderer.

Do not remove:

```text
CaptureDeviceQuickSettingsAsync
focused CPU/TDP/Power methods
```

from `NamedPipeAddonFrontendClient` merely because the generic seam now exists.

The shared Quick Settings renderer contract is specifically for QAM + Overlay parity, not a forced Main UI schema migration.

---

## 32. No Full1902 / controller lifecycle changes

There must be no behavior change in:

```text
Center M Enabled / Disabled authority
PID1901 / PID1902 transitions
DirectInput physical ownership
HidHide deterministic baseline / normalization
VIIPER ownership / teardown
Xbox360 / SteamDeck presentation selection
Steam/BPM presentation authority
physical device loss / PnP re-enumeration
sleep / hibernate / resume
restart / shutdown teardown
OQ4 capture / neutral publication
front-button mapping / WING / OEM1 handling
```

The generic QAM pipe is a frontend RPC seam only.

If implementation appears to require a new controller state, lock, epoch, recovery participant, or ownership path, the design has drifted outside SF-V2-04.

---

## 33. Failure-policy matrix

Preserve these layers distinctly:

```text
Invalid QAM bridge JSON
→ QamFrontendBridge bounded error response
→ no Runtime mutation

QAM Device surface not admitted
→ QamFrontendBridge bounded error response
→ no Runtime mutation

Invalid frontend wire JSON / enum / required member
→ existing InvalidMessage semantics
→ no Runtime mutation

Structurally decoded but product-invalid QuickSettingsMutationIntent
→ SF-V2-03 QuickSettingsMutationAdapter returns Succeeded=false
→ zero typed Device mutation
→ fresh Device page when capture is available

Valid typed mutation returns feature failure
→ QuickSettingsMutationResult.Succeeded=false
→ FailureMessage preserved
→ fresh authoritative page returned

Transport disconnect
→ QAM surface loses editable trust
→ Runtime survives
```

Do not collapse these into one generic `false` or one exception type.

---

## 34. Overengineering guardrails

Do not add any of the following for this PR:

```text
new Quick Settings service/manager
new QAM state cache
new transport process
new named pipe
new request broker
new dispatcher interface hierarchy
reflection-based RPC routing
string feature names
JSON schema engine
capability registry
page registry
renderer registry
surface visibility matrix
revision vector
epoch/barrier protocol
global mutation queue
retry framework
new QAM lifecycle state machine
```

Existing architecture already supplies:

```text
one Runtime/frontend authority
one frontend named-pipe transport
one request correlation implementation
one cancellation implementation
one StateInvalidated path
one SF-V2-03 product projection
one SF-V2-03 mutation adapter
one QAM surface admission rule
```

Reuse those.

---

## 35. Expected diff shape

A healthy SF-V2-04 diff should look approximately like:

```text
FrontendWire.cs
  + protocol 27 -> 28
  + v28 history comment
  + 2 RPC enum members
  + 1 small capture request DTO

NamedPipeAddonFrontendClient.cs
  + CaptureQuickSettingsPageAsync
  + MutateQuickSettingAsync

NamedPipeAddonFrontendServer.cs
  + dispatch for 2 RPCs
  + optional tiny helper(s)

QamFrontendBridge.cs
  + 2 bridge allowlist methods
  + generic capture decode helper
  + generic Device mutation helper
  + small shared Device-admission helper extraction
  + optional small pipe-name constructor for tests

Tests
  + generic page wire round trip
  + generic mutation wire round trip
  + malformed wire coverage
  + v27 -> v28 handshake rejection/current-version updates
  + QAM generic Device admission behavior
```

Unexpected large diff areas are a reason to stop and reassess.

---

## 36. Concrete implementation sketch

The following is an illustrative combined sketch against the reviewed source. Adapt mechanically to current formatting; do not treat it as permission to rewrite surrounding code.

### 36.1 `FrontendWire.cs`

```csharp
// Version 28: Shared Frontend V2 SF-V2-04 exposes the generic Quick Settings page/mutation
// seam through the .Frontend/.Qam transport. Pre-release: no compatibility shim.
public static class FrontendTransportProtocol
{
    public const int CurrentVersion = 28;
}

internal enum FrontendRpcMethod
{
    Unknown = 0,
    // existing entries unchanged ...
    CaptureDeviceQuickSettings,
    CaptureQuickSettingsPage,
    MutateQuickSetting,
}

internal sealed record CaptureQuickSettingsPageRequest(
    QuickSettingsPageId PageId,
    uint? AppId);
```

### 36.2 `NamedPipeAddonFrontendClient.cs`

```csharp
public Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(
    QuickSettingsPageId pageId,
    uint? appId = null,
    CancellationToken cancellationToken = default) =>
    SendAsync<QuickSettingsPageSnapshot>(
        FrontendRpcMethod.CaptureQuickSettingsPage,
        FrontendWireCodec.Payload(
            new CaptureQuickSettingsPageRequest(pageId, appId)),
        cancellationToken);

public Task<QuickSettingsMutationResult> MutateQuickSettingAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken cancellationToken = default) =>
    SendAsync<QuickSettingsMutationResult>(
        FrontendRpcMethod.MutateQuickSetting,
        FrontendWireCodec.Payload(intent),
        cancellationToken);
```

### 36.3 `NamedPipeAddonFrontendServer.cs`

Conceptual additions only:

```csharp
: m == FrontendRpcMethod.CaptureQuickSettingsPage
? await InvokeQuickSettingsCaptureAsync(p, t).ConfigureAwait(false)
: m == FrontendRpcMethod.MutateQuickSetting
? FrontendWireCodec.Payload(
    await _inner.MutateQuickSettingAsync(
        FrontendWireCodec.Decode<QuickSettingsMutationIntent>(p),
        t).ConfigureAwait(false))
```

with:

```csharp
private async Task<JsonElement> InvokeQuickSettingsCaptureAsync(
    JsonElement? payload,
    CancellationToken cancellationToken)
{
    var request = FrontendWireCodec.Decode<CaptureQuickSettingsPageRequest>(payload);
    var result = await _inner.CaptureQuickSettingsPageAsync(
        request.PageId,
        request.AppId,
        cancellationToken).ConfigureAwait(false);
    return FrontendWireCodec.Payload(result);
}
```

Do not refactor unrelated RPC dispatch entries.

### 36.4 `QamFrontendBridge.cs`

```csharp
private sealed record QuickSettingsPageBridgeRequest(
    QuickSettingsPageId PageId,
    uint? AppId);
```

Switch:

```csharp
"captureQuickSettingsPage" => await CaptureQuickSettingsPageAsync(root, token),
"mutateQuickSetting" => await MutateQuickSettingAsync(root, token),
```

Helpers:

```csharp
private async Task<object> CaptureQuickSettingsPageAsync(
    JsonElement root,
    CancellationToken token)
{
    var request = root.GetProperty("payload")
        .Deserialize<QuickSettingsPageBridgeRequest>(BridgeJson)
        ?? throw new JsonException("Invalid Quick Settings page request.");

    if (!Enum.IsDefined(request.PageId))
        throw new JsonException("Invalid Quick Settings page id.");

    return await _client.CaptureQuickSettingsPageAsync(
        request.PageId,
        request.AppId,
        token).ConfigureAwait(false);
}

private async Task EnsureDeviceMutationAdmittedAsync(CancellationToken token)
{
    var status = await _client.CaptureStatusAsync(token).ConfigureAwait(false);
    if (!status.Steam.Active ||
        status.Steam.AppId != 0 ||
        status.Steam.Source != FrontendSteamSource.BigPicture)
    {
        throw new InvalidOperationException(
            "Device QAM mutation is available only in Big Picture with no running game.");
    }
}

private async Task<object> MutateQuickSettingAsync(
    JsonElement root,
    CancellationToken token)
{
    var intent = root.GetProperty("payload")
        .Deserialize<QuickSettingsMutationIntent>(BridgeJson)
        ?? throw new JsonException("Invalid Quick Settings mutation intent.");

    if (intent.PageId != QuickSettingsPageId.Device)
        throw new InvalidOperationException(
            "Only Device Quick Settings mutation is available through the QAM generic seam.");

    await EnsureDeviceMutationAdmittedAsync(token).ConfigureAwait(false);
    return await _client.MutateQuickSettingAsync(intent, token).ConfigureAwait(false);
}
```

Then change the existing legacy Device `MutateAsync(...)` only enough to reuse `EnsureDeviceMutationAdmittedAsync`:

```csharp
private async Task<object> MutateAsync(
    JsonElement root,
    CancellationToken token,
    Func<NamedPipeAddonFrontendClient, JsonElement, CancellationToken, Task<object>> mutation)
{
    await EnsureDeviceMutationAdmittedAsync(token).ConfigureAwait(false);
    return await mutation(
        _client,
        root.GetProperty("payload"),
        token).ConfigureAwait(false);
}
```

This preserves the exact existing admission condition for old and new Device paths without inventing another policy owner.

---

## 37. Validation commands

During implementation, run focused tests as useful, then complete at minimum:

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also run a focused test filter covering at least:

```text
FrontendNamedPipeTransportTests
QamFrontendBridgeTests
QamFrontendContractTests
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QuickSettingsInProcessSeamTests
OverlayDeviceQuickSettingsTransportTests
```

No new warnings.

Do not declare completion from focused tests alone; full Release suite must pass.

---

## 38. Manual/source verification checklist

Before opening the PR, verify:

```text
[ ] FrontendTransportProtocol bumped exactly once from current main
[ ] OverlayTransportProtocol unchanged
[ ] CaptureQuickSettingsPage RPC exists
[ ] MutateQuickSetting RPC exists
[ ] NamedPipeAddonFrontendClient concretely implements both generic interface methods
[ ] server calls IAddonFrontendControl generic methods, not feature runtimes directly
[ ] QamFrontendBridge exposes only the two approved generic JS method names
[ ] generic Device mutation uses the exact current BigPicture/no-running-game admission rule
[ ] Profile generic mutation is not accidentally admitted
[ ] old feature-specific QAM Device bridge methods remain for current qam.js
[ ] qam.js has no production diff
[ ] QAM_SLIDER_COMMIT_DELAY_MS remains for SF-V2-05 to remove
[ ] QuickSettingsMutationAdapter remains the only generic product mutation validator/dispatcher
[ ] no new StateInvalidated event/path exists
[ ] no .Overlay production diff
[ ] no Main UI production diff
[ ] no Full1902/controller lifecycle diff
[ ] all current protocol-version assertions are consistent
[ ] full tests pass
[ ] git diff --check passes
```

---

## 39. Acceptance criteria

SF-V2-04 is complete only when all of the following are true:

```text
1. The existing shared QuickSettingsPageSnapshot contract can cross the .Frontend/.Qam
   named-pipe transport through one explicit CaptureQuickSettingsPage RPC.

2. The existing QuickSettingsMutationIntent / QuickSettingsMutationResult contract can
   cross the same transport through one explicit MutateQuickSetting RPC.

3. NamedPipeAddonFrontendClient concretely implements the two generic
   IAddonFrontendControl methods instead of falling back to interface defaults.

4. NamedPipeAddonFrontendServer dispatches both operations only to the existing generic
   IAddonFrontendControl seam; it does not add feature-specific Quick Settings dispatch.

5. QamFrontendBridge exposes exactly the approved generic bridge names:
   captureQuickSettingsPage and mutateQuickSetting.

6. Generic Device QAM mutation preserves the exact current surface admission:
   Steam active + Big Picture source + AppId == 0.

7. Non-Device generic mutation is not accidentally admitted before the later Profile
   milestone.

8. Product validation remains owned by SF-V2-03 QuickSettingsMutationAdapter; transport
   and bridge do not duplicate TDP/CPU/Power row logic.

9. Existing feature-specific QAM Device bridge operations remain temporarily because
   current qam.js still uses them.

10. qam.js visible behavior is unchanged. No generic renderer work lands in this PR.

11. Frontend protocol is bumped exactly once from the implementation baseline
    (27 -> 28 on the reviewed current main), with stale v27 handshake rejection covered.

12. Overlay protocol remains v6 and Overlay production behavior is untouched.

13. Main UI behavior is untouched.

14. Existing cancellation, request correlation, invalidation, disconnect, and shutdown
    semantics are reused without another Quick Settings transport/lifecycle mechanism.

15. No controller/HidHide/VIIPER/PID/Full1902 lifecycle behavior changes.

16. Focused tests, full Release test suite, Debug/Release builds, and git diff --check pass.
```

---

## 40. Handoff to SF-V2-05

After this PR, the code should intentionally be in this transition state:

```text
Runtime
  QuickSettingsPageSnapshot(Device)
  QuickSettingsMutationIntent
  QuickSettingsMutationResult
        ↓
Frontend/.Qam protocol v28
        ↓
QamFrontendBridge generic allowlist
        ↓
READY FOR QAM JS CONSUMPTION

but current qam.js still uses:
  captureDeviceQuickSettings
  setDeviceCpuBoost*
  setDeviceTdp*
  setDevicePowerMode*
  QAM_SLIDER_COMMIT_DELAY_MS
```

That is the correct SF-V2-04 endpoint.

The next PR, SF-V2-05, then owns:

```text
qam.js Device generic renderer
RowId / CommitGroupId / CommitPolicy-driven interaction
shared linked TDP constraint consumption
removal of Device labels/options/policy duplication from JavaScript
removal of old feature-specific Device QAM bridge calls after no JS callsite remains
```

Do **not** pull any of that renderer work forward into SF-V2-04.
