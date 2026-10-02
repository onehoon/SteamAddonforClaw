# Work Order — Overlay Transport File-Split Refactor

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** daa24a966ea98285b63878f4843659a326ace34c  
**Feature area:** Overlay frontend transport  
**Implementation shape:** one behavior-preserving structural refactor PR  
**Primary goal:** split the current ~1,823-line Overlay transport implementation into responsibility-based source files without changing protocol shape, protocol version, runtime authority, transport behavior, ordering, concurrency, lifecycle, or public/internal call seams.

---

# 0. Why this PR

The Overlay UI itself is now structurally in a good state.

The current OverlayWindow refactor correctly keeps one logical Window owner and separates presentation responsibilities through partial files without introducing managers/services.

The remaining unusually concentrated Overlay source file is:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
~~~

Current reviewed size:

~~~text
~1,823 lines
~111 KB
~~~

It currently contains five already-distinct responsibilities:

~~~text
protocol contracts / DTOs
wire validation
wire codec
NamedPipeOverlayServer
NamedPipeOverlayClient
~~~

These responsibilities already have clear type boundaries. This PR should therefore perform a source-file split only.

The goal is:

~~~text
same namespace
same types
same constructors
same fields
same methods
same protocol
same serializer
same locks/gates
same read loop
same write ordering
same request correlation
same lifecycle

but

those existing types live in focused files
~~~

This is not a transport redesign.

---

# 1. Mandatory authority review

Read current versions before editing:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/OVERLAY_UI_FOUNDATION_PR_A_STRUCTURAL_REFACTOR_WORK_ORDER.md
docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md
docs/shared-frontend/SF_V2_06_OVERLAY_GENERIC_QUICK_SETTINGS_V7_TRANSPORT_WORK_ORDER.md
docs/shared-frontend/SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md
docs/work-order/OVERLAY_CONTROLLER_M1_M2_MAPPING_PR1_WORK_ORDER_2026-10-02.md
~~~

Full1902 authority remains unchanged.

The Overlay process/frontend is disposable UI. It is not controller authority.

Do not move any controller/lifecycle responsibility into FrontendTransport.

---

# 2. Mandatory source review

Inspect current main before moving code:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs

src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs

tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayShortcutTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayBackButtonMappingTransportTests.cs
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
~~~

Also inspect other Overlay transport test files discovered on current main.

Do not mechanically use the reviewed line numbers if main has moved.

---

# 3. Current file boundaries verified on reviewed main

The current file already has natural responsibility boundaries.

Reviewed approximate locations:

~~~text
OverlayTransportProtocol                  line 11
OverlayWireMessageKind                    line 48
OverlayWireMessage                        line 99

OverlayBackButtonMappingWireValidation    line 125
OverlayQuickSettingsWireValidation        line 191
OverlayShortcutWireValidation             line 251

OverlayWireCodec                          line 364

NamedPipeOverlayServer                    line 430

NamedPipeOverlayClient                    line 1222
~~~

No new abstraction is required to split these types.

---

# 4. Target file layout

Use this target:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/
│
├─ OverlayWire.cs
├─ OverlayWire.Validation.cs
├─ OverlayWire.Codec.cs
├─ NamedPipeOverlayServer.cs
└─ NamedPipeOverlayClient.cs
~~~

This layout intentionally matches the existing type responsibilities.

Do not create folders or a transport framework merely for this split.

---

# 5. Keep OverlayWire.cs as the protocol-contract file

OverlayWire.cs remains the canonical protocol/DTO source file.

Keep in it:

~~~text
OverlayTransportProtocol
OverlayWireMessageKind
OverlayCommand
OverlayNavigationAction
OverlayState

OverlayQuickSettingsMutationRequest
OverlayQuickSettingsMutationResponse

OverlayClawHudMutationRequest
OverlayClawHudMutationResponse

OverlayProfileCatalogState
OverlayProfilePageRequest
OverlayProfilePageResponse

OverlayShortcutExecuteRequest
OverlayShortcutExecutionOutcome
OverlayShortcutExecuteResponse

OverlayBackButtonMappingState
OverlayBackButtonMappingMutationRequest
OverlayBackButtonMappingMutationOutcome
OverlayBackButtonMappingMutationResponse

OverlayWireMessage
~~~

Preserve declaration order unless a compiler dependency requires otherwise.

Most importantly preserve:

~~~text
OverlayTransportProtocol.CurrentVersion = 13
~~~

exactly.

No protocol bump.

No DTO change.

No member rename.

No optional/required change.

No enum reorder/value change.

---

# 6. Why protocol contracts remain in OverlayWire.cs

Current test coverage includes a source-level parity assertion that reads:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
~~~

and verifies:

~~~text
CurrentVersion = 13
~~~

Keeping the protocol/version in OverlayWire.cs means this useful contract test does not need to be weakened or redirected solely because implementation types moved.

Historical docs also commonly refer to OverlayWire.cs as the wire contract. Keeping the contracts there minimizes documentation churn.

---

# 7. Move validation to OverlayWire.Validation.cs

Move these existing types without functional edits:

~~~text
OverlayBackButtonMappingWireValidation
OverlayQuickSettingsWireValidation
OverlayShortcutWireValidation
~~~

Keep:

- every validation condition;
- fail-closed behavior;
- mixed-payload rejection;
- bounded Shortcut state/result behavior;
- current failure strings;
- current helper visibility;
- current enum/DTO checks.

Do not introduce:

~~~text
IOverlayWireValidator
generic message validator
validation registry
reflection-based validation
attribute validation
shared feature-payload abstraction
~~~

Three concrete validators are acceptable and currently clear.

---

# 8. Move codec to OverlayWire.Codec.cs

Move:

~~~text
OverlayWireCodec
~~~

without modifying behavior.

Preserve exactly:

~~~text
JsonSerializerOptions
RespectRequiredConstructorParameters = true
JsonStringEnumConverter with allowIntegerValues=false

4-byte little-endian frame prefix
MaxFrameBytes enforcement
ReadExactlyAsync behavior
serialized-length behavior
write-gate usage
FlushAsync behavior
JSON exception wrapping
~~~

Do not replace the codec.

Do not add:

- source-generated JSON context;
- MessagePack;
- protobuf;
- PipeReader/PipeWriter;
- new buffering;
- pooled arrays;
- compression;
- checksum;
- retry.

This PR has no performance requirement.

---

# 9. Move server to NamedPipeOverlayServer.cs

Move the existing:

~~~text
NamedPipeOverlayServer
~~~

type as a whole.

Do not split it further in this PR.

Preserve all current fields and gates, including their current ownership and purpose.

Examples include:

~~~text
_lifetime
_commandGate
_writeGate
_sync
_serverReady
_ready
_disconnected
_acknowledgement
_activePipe
_acceptLoop
_readyState
connection generation fields
_state
_started
_disposed
~~~

Preserve all currently bound feature delegates:

~~~text
tab order
Quick Settings
ClawHUD
Profile
Shortcut
M1/M2
~~~

Do not replace them with a feature dictionary or generic dispatcher.

The explicit typed delegates are preferable for the currently supported fixed feature set.

---

# 10. Server behavior is frozen

Preserve exact behavior for:

~~~text
StartAsync
accept loop
handshake
initial state publication
Ready tracking
Visible/Hidden state
command acknowledgement
DismissRequested
navigation send
tab-order send/mutation
Quick Settings state/mutation
ClawHUD state/mutation
Profile catalog/page
Shortcut state/execution
M1/M2 state/mutation
disconnect handling
dispose
~~~

Preserve:

~~~text
Ready/Visible admission checks
bounded command timeout
request handling outside the sole read loop where currently required
_writeGate serialization
generation/disconnect rules
feature-local failure behavior
fail-closed malformed-frame behavior
~~~

Do not simplify concurrency during this file move.

---

# 11. Move client to NamedPipeOverlayClient.cs

Move the existing:

~~~text
NamedPipeOverlayClient
~~~

type as a whole.

Do not split per feature.

Preserve:

~~~text
handshake behavior
RunAsync callback signature
read-loop dispatch
write serialization
request ID allocation
pending request correlation
per-feature pending mutation state
disconnect cancellation
DisposeAsync
Shutdown handling
protocol-failure behavior
~~~

Do not introduce:

~~~text
IOverlayRequest
generic pending-request dictionary
feature client classes
request broker
response router abstraction
channel-based dispatcher
~~~

The current explicit request fields are verbose but easy to audit and match the fixed protocol.

---

# 12. Namespace and accessibility remain unchanged

All moved types remain:

~~~text
namespace SteamInputAddonforClaw.FrontendTransport;
~~~

Keep all existing accessibility:

~~~text
internal
internal sealed
internal static
~~~

Do not make transport implementation public.

Do not add partial to server/client merely because they were moved.

They are complete types and should remain complete types.

---

# 13. Preserve constructors and call sites

The following production callers should require no functional redesign:

~~~text
OverlayProcessController
Overlay App
transport tests
~~~

Keep constructor signatures of:

~~~text
NamedPipeOverlayServer
NamedPipeOverlayClient
~~~

exactly compatible.

Keep:

~~~text
OverlayProcessController._serverFactory
new NamedPipeOverlayServer(...)
new NamedPipeOverlayClient(...)
~~~

working without adapters.

Preferred production diff outside FrontendTransport:

~~~text
none
~~~

A using cleanup is acceptable if compiler-required.

---

# 14. Preserve Runtime / lifecycle ownership

Do not touch the following as part of this refactor:

~~~text
AddonProcessHost._visibleSurfaceTransition
_overlayCaptureActive
OverlayControllerInputRouter
PauseForOverlayAsync
RetireOverlayCaptureUnderTransitionAsync
release-to-resume
VisibleSessionLost
OverlayDismissRequested
OverlayProcessController._transition
OverlayProcessController visibility state
OverlayProcessController process teardown
~~~

No change to:

- Sleep / Hibernate / Resume;
- crash/restart;
- PID1901/PID1902;
- HidHide;
- VIIPER;
- presentation ownership;
- routing fail-close.

This is a frontend transport source split only.

---

# 15. Preserve protocol version and wire compatibility

Frozen:

~~~text
Overlay protocol CurrentVersion = 13
~~~

Do not bump to 14.

Do not change serialized property names.

Do not reorder enum numeric values.

Do not change serializer options.

Do not add fields.

Do not remove fields.

Do not change nullability semantics.

Do not change frame size.

Do not change handshake order.

A build produced before and after this source-only refactor should be wire-shape equivalent at protocol v13.

---

# 16. Preserve concurrency exactly

This PR must not opportunistically change locks/gates.

Do not add or remove:

~~~text
SemaphoreSlim
lock
TaskCompletionSource
generation counters
CancellationTokenSource
pending-request fields
~~~

Do not replace separate typed request gates with one generalized gate.

Do not serialize operations that currently intentionally run independently.

Do not parallelize operations that currently serialize.

Do not add race defenses for theoretical interleavings.

The goal is zero behavioral change.

---

# 17. Preserve lifecycle failure semantics

Real supported lifecycle conditions remain:

~~~text
Overlay process disconnect
Overlay process crash
Hide/Shutdown
server/client Dispose
pending mutation during disconnect
command timeout
malformed frame
operation failure
visible-session loss
~~~

Existing tests already cover important cases such as:

~~~text
pending mutation cancellation on disconnect
dismissal while mutation persistence is pending
wrong request ID does not complete another pending mutation
hidden/not-ready server does not invoke mutation authority
capture failure is feature-local
~~~

Do not weaken these behaviors.

---

# 18. Source move rules

Prefer literal move of existing blocks before any cleanup.

Recommended implementation order:

~~~text
1. Keep protocol/DTO block in OverlayWire.cs.
2. Move validators to OverlayWire.Validation.cs.
3. Move codec to OverlayWire.Codec.cs.
4. Move server as one block to NamedPipeOverlayServer.cs.
5. Move client as one block to NamedPipeOverlayClient.cs.
6. Fix only required using directives.
7. Build.
8. Run transport tests.
9. Only then perform tiny comment/file-name cleanup if useful.
~~~

This makes accidental behavior edits easy to detect.

---

# 19. Allowed cleanup

Allowed only when directly caused by the file split:

- remove now-unused using directives;
- add required using directives to moved files;
- update comments saying "this file" when that statement becomes false;
- update a current architecture comment that points to a now-moved type;
- preserve logical type ordering within the new files.

Do not combine:

- naming cleanup;
- async cleanup;
- nullable cleanup;
- timeout changes;
- logging redesign;
- exception-policy changes;
- performance optimization;
- request API cleanup.

---

# 20. Do not split server/client further

NamedPipeOverlayServer is large because it owns the complete server side of one fixed Overlay protocol.

NamedPipeOverlayClient similarly owns the client side.

Do not create:

~~~text
OverlayQuickSettingsServer
OverlayShortcutServer
OverlayProfileServer
OverlayControllerServer

OverlayQuickSettingsClient
OverlayShortcutClient
...
~~~

That would fragment one connection/read-loop/write-gate authority across multiple runtime objects.

The target is one server owner and one client owner.

---

# 21. Do not create a generic feature transport framework

Avoid:

~~~text
IOverlayFeatureTransport
IOverlayMessageHandler<T>
OverlayFeatureRegistry
OverlayRequestRouter
OverlayResponseRouter
OverlayPendingRequestManager
OverlayWireFeature
feature handler DI
reflection/attribute routing
~~~

The product has a small fixed feature set.

Explicit switch/callback code is acceptable and safer to audit.

This PR reduces file concentration, not explicitness.

---

# 22. Tests that must remain green

At minimum run:

~~~text
OverlayTransportTests
OverlayDeviceQuickSettingsTransportTests
OverlayShortcutTransportTests
OverlayBackButtonMappingTransportTests
AddonQuickSettingsSurfaceParityTests
~~~

Also run all other current test classes whose name or source references:

~~~text
Overlay
NamedPipeOverlay
TabOrder
QuickSettings transport
ClawHud transport
Profile transport
Shortcut transport
BackButtonMapping transport
~~~

Then run the normal repository Build and Test workflow/full suite.

---

# 23. Required regression properties

Existing tests must continue proving:

## Protocol / codec

~~~text
CurrentVersion = 13
round-trip DTO serialization
required-constructor enforcement
string-enum enforcement
frame size validation
malformed frame rejection
~~~

## Server

~~~text
handshake
Ready/Visible admission
command acknowledgement
DismissRequested
navigation delivery
feature state publication
feature mutation request/response
capture failure isolation
hidden/not-ready rejection
~~~

## Client

~~~text
request correlation
wrong request ID ignored
pending request cancellation on disconnect/dispose
state callback dispatch
Shutdown exits run loop
~~~

## Concurrency/lifecycle

~~~text
Dismiss remains readable while a feature mutation is pending
a feature mutation does not block the only read loop
disconnect clears pending state
visible publication only while admitted
~~~

No new artificial race tests are required.

---

# 24. Optional structural source test

A new structure-only test is optional, not mandatory.

If added, keep it narrow:

~~~text
OverlayWire.cs contains OverlayTransportProtocol and OverlayWireMessage
OverlayWire.Validation.cs contains validation classes
OverlayWire.Codec.cs contains OverlayWireCodec
NamedPipeOverlayServer.cs contains NamedPipeOverlayServer
NamedPipeOverlayClient.cs contains NamedPipeOverlayClient
~~~

Do not assert exact line counts.

Do not test file ordering.

Do not introduce brittle regex checks for every method.

Compiler + existing behavioral tests provide most of the required confidence.

---

# 25. Expected production diff

Expected:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs                [shrink]
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.Validation.cs     [new]
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.Codec.cs          [new]
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs     [new]
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs     [new]
~~~

Potential test-only adjustment:

~~~text
tests/... only if a source-shape test genuinely needs to know the new files
~~~

Expected functional production changes outside FrontendTransport:

~~~text
none
~~~

---

# 26. Review checklist

Before submitting the PR verify:

~~~text
[ ] OverlayTransportProtocol.CurrentVersion is still 13
[ ] OverlayWireMessage shape is unchanged
[ ] all enum members/order are unchanged
[ ] serializer options are unchanged
[ ] frame prefix/size behavior is unchanged
[ ] server constructors are unchanged
[ ] client constructor is unchanged
[ ] server fields/gates are unchanged
[ ] client pending-request fields/gates are unchanged
[ ] read-loop dispatch order is unchanged
[ ] command timeout is unchanged
[ ] write-gate use is unchanged
[ ] Ready/Visible checks are unchanged
[ ] DismissRequested behavior is unchanged
[ ] VisibleSessionLost path is untouched
[ ] OverlayProcessController functional diff is zero
[ ] AddonProcessHost functional diff is zero
[ ] Overlay App functional diff is zero
[ ] existing Overlay transport tests pass
[ ] full Build and Test passes
~~~

---

# 27. Explicit non-goals

Not in this PR:

- protocol v14;
- protocol redesign;
- request/response shape changes;
- serializer migration;
- performance optimization;
- PipeReader/PipeWriter migration;
- generic request dictionary;
- generic feature dispatcher;
- feature transport interfaces;
- server/client per-feature classes;
- new retry policy;
- new timeout policy;
- new synchronization;
- connection state-machine redesign;
- Runtime lifecycle changes;
- Overlay UI changes;
- QAM visual changes;
- Device/Profile/Controller/Shortcut/ClawHUD feature changes.

---

# 28. Overengineering guardrail

Desired final architecture:

~~~text
OverlayWire.cs
    protocol + DTOs

OverlayWire.Validation.cs
    existing validators

OverlayWire.Codec.cs
    existing codec

NamedPipeOverlayServer.cs
    one existing server owner

NamedPipeOverlayClient.cs
    one existing client owner
~~~

Nothing else.

Do not turn a file split into a transport framework.

---

# 29. Definition of done

The PR is complete when:

~~~text
OverlayWire.cs is reduced to the protocol/DTO contract
validation is in one focused file
codec is in one focused file
server is in its own file
client is in its own file

AND

protocol version remains 13
wire format remains identical
server/client APIs remain identical
all existing transport/lifecycle behavior remains identical
all relevant tests pass
normal Build and Test passes
~~~

After this PR, no further Overlay structural refactor should be pursued solely to reduce file size. Future changes should be driven by concrete feature work or a demonstrated maintenance/reliability problem.
