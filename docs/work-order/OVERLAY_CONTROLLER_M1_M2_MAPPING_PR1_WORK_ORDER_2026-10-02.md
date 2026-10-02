# Work Order — Overlay Controller PR1: M1 / M2 Mapping

**Date:** 2026-10-02  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** ecd8b866dcdbdbed98be6f3363f9658c1b76d647  
**Feature area:** WinUI 3 Addon Overlay / Controller tab only  
**Implementation shape:** one focused PR

---

## 0. Goal

Replace the current Overlay Controller placeholder with the first real Controller page.

PR1 exposes only the already-defined global Xbox360 M1 / M2 mapping:

~~~text
Controller
└ M1 / M2
    M1    < Disabled / A / B / ... / Menu / Xbox Guide >
    M2    < Disabled / A / B / ... / Menu / Xbox Guide >
~~~

Reuse the existing production path:

~~~text
BackButtonMappingSettings
→ StartupSettingsCoordinator.ChangeBackButtonMapping(...)
→ settings.json
→ existing BackButtonMappingChanged publication
→ Canonical Xbox360 publisher / mapper
~~~

The Overlay is only another frontend for that existing authority.

Do not create a second controller mapping owner, another settings record, or another output mapper.

---

# 1. Mandatory review before coding

Read the latest versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR1_CONTRACT_PERSISTENCE_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR2_LIVE_OUTPUT_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_MAPPING_PR3_PRESENTATION_RELEASE_TO_REARM_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR4_CONTROLLER_UI_WORK_ORDER.md
docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
~~~

Read the latest implementation of:

~~~text
src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
~~~

---

# 2. Full1902 safety boundary

This is a frontend projection only.

Do not change:

- PID1901 / PID1902 authority;
- DirectInput ownership;
- HidHide ownership or normalization;
- VIIPER lifetime;
- Xbox360 / SteamDeck presentation switching;
- physical M1/M2 acquisition;
- Xbox360DeviceStateMapper mapping semantics;
- SteamDeck R4/L4 behavior;
- presentation release-to-rearm handling;
- suspend/resume;
- restart/shutdown;
- PnP recovery;
- rumble/feedback;
- WING / Center M front-button mapping.

The Overlay must never become a controller input reader or controller mapping authority.

---

# 3. Current contract

Current targets are:

~~~text
Disabled
A
B
X
Y
DPadUp
DPadRight
DPadDown
DPadLeft
LeftBumper
RightBumper
LeftTrigger
RightTrigger
LeftStickClick
RightStickClick
View
Menu
XboxGuide
~~~

Current whole-record contract:

~~~csharp
public sealed record BackButtonMappingSettings(
    Xbox360BackButtonTarget M1,
    Xbox360BackButtonTarget M2);
~~~

Defaults:

~~~text
M1 = Disabled
M2 = Disabled
~~~

Duplicate targets are valid.

Do not add uniqueness rules between M1 and M2.

The desktop Controller page already edits and persists this exact record. Do not redesign that existing path.

---

# 4. Current Overlay gap

The current Overlay BuildPage dispatch has real builders for Device, Profile, Shortcut, and Setting.

Controller still falls through to CreatePlaceholderPage(...).

This PR replaces only that placeholder.

OverlayValueRow already provides everything needed:

~~~text
DiscreteChoice
Left / Right controller adjustment
pointer buttons
local preview
authoritative ApplyState(...)
shared OverlayRowSelection
~~~

Do not add a ComboBox, flyout, modal selector, nested page, or another row primitive.

---

# 5. Visible Controller page

The Controller page contains exactly one section:

~~~text
┌ M1 / M2 ─────────────────────────────────┐
│ Xbox 360 mode only.                      │
│ Steam Game / Big Picture keeps M1 as R4  │
│ and M2 as L4.                            │
│                                          │
│ M1              < Disabled >             │
│ M2              < Disabled >             │
└───────────────────────────────────────────┘
~~~

Use existing CreateOverlaySectionCard(...) chrome.

Do not add:

- Gamebar Button;
- Center M Button;
- joystick LED;
- vibration strength;
- diagnostics;
- routing/controller status.

Those remain separate future work.

---

# 6. Controller partial

Add:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
~~~

Change BuildPage:

~~~csharp
AddonQuickSettingsTabId.Controller => BuildControllerPage(rows),
~~~

The Controller page may own only local presentation state:

~~~csharp
private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default;
private bool _backButtonMappingAvailable;
private bool _backButtonMutationInFlight;

private OverlayValueRow? _m1MappingRow;
private OverlayValueRow? _m2MappingRow;
private TextBlock? _backButtonStatusText;
~~~

No manager/service/view-model class.

---

# 7. M1/M2 rows

Create two OverlayValueRow instances with:

~~~text
buttonKind = DiscreteChoice
minimum    = Disabled
maximum    = XboxGuide
step       = 1
~~~

Conceptually:

~~~csharp
_m1MappingRow = new OverlayValueRow(
    "M1",
    FormatBackButtonTarget,
    value => RequestBackButtonMappingChange(true, value),
    OverlayValueButtonKind.DiscreteChoice);

_m2MappingRow = new OverlayValueRow(
    "M2",
    FormatBackButtonTarget,
    value => RequestBackButtonMappingChange(false, value),
    OverlayValueButtonKind.DiscreteChoice);
~~~

Register both rows through the existing shared row-selection and pointer-selection seams.

Do not create Controller-specific navigation state.

---

# 8. Display labels

Use the same visible wording as the Main App:

~~~text
Disabled
A
B
X
Y
D-Pad Up
D-Pad Right
D-Pad Down
D-Pad Left
Left Bumper (LB)
Right Bumper (RB)
Left Trigger (LT)
Right Trigger (RT)
Left Stick Click (L3)
Right Stick Click (R3)
View
Menu
Xbox Guide
~~~

For this focused PR, one private formatter in OverlayWindow.Controller.cs matching the Main UI wording is acceptable.

Do not refactor the Main UI merely to centralize these strings.

Do not display raw enum names such as LeftBumper or RightStickClick.

---

# 9. Availability and mutation state

The existing stable startup fact remains authoritative:

~~~text
FrontendBootstrapSnapshot.BackButtonMappingAvailable
~~~

The Overlay must not probe hardware.

Behavior:

~~~text
Available
→ show persisted M1/M2 values
→ rows selectable

Unavailable
→ show unavailable message
→ rows disabled
→ no mutation request

Mutation in flight
→ disable both rows
→ do not start a second mutation
~~~

One in-flight whole-record mutation is enough.

Do not add queues, epochs, retries, debounce managers, or optimistic multi-request reconciliation.

---

# 10. Whole-record candidate only

Editing M1:

~~~csharp
new BackButtonMappingSettings(
    selectedM1,
    _backButtonMapping.M2)
~~~

Editing M2:

~~~csharp
new BackButtonMappingSettings(
    _backButtonMapping.M1,
    selectedM2)
~~~

The Overlay emits the complete candidate record.

Do not create separate persisted M1/M2 RPCs.

---

# 11. Overlay protocol v13

The desktop frontend protocol already has SetBackButtonMappingAsync(...).

Do not change FrontendTransportProtocol.

Only the dedicated Overlay protocol changes.

Bump:

~~~text
OverlayTransportProtocol.CurrentVersion
12 -> 13
~~~

Version 13 adds Runtime-owned M1/M2 mapping state and one correlated whole-record mutation flow.

Pre-release policy:

~~~text
no v12 compatibility shim
version mismatch -> handshake rejection
~~~

---

# 12. Narrow Overlay wire types

Recommended transport-only records:

~~~csharp
internal sealed record OverlayBackButtonMappingState(
    bool Available,
    BackButtonMappingSettings Mapping,
    string? FailureMessage = null);

internal sealed record OverlayBackButtonMappingMutationRequest(
    long RequestId,
    BackButtonMappingSettings Mapping);

internal sealed record OverlayBackButtonMappingMutationResponse(
    long RequestId,
    bool Succeeded,
    string? FailureMessage,
    OverlayBackButtonMappingState State);
~~~

Add message kinds:

~~~text
BackButtonMappingState
BackButtonMappingMutationRequest
BackButtonMappingMutationResult
~~~

Add matching optional payload members to OverlayWireMessage.

Do not send the entire FrontendSettingsSnapshot over the Overlay pipe.

Do not add a generic Controller settings schema/dictionary.

---

# 13. Wire validation

Use BackButtonMappingValidation as the existing target-validation authority.

Transport validation should prove only:

~~~text
payload exists
mapping exists
mapping is structurally valid
request id > 0 for correlated messages
no unrelated payload shares the frame
~~~

A small OverlayBackButtonMappingWireValidation helper is acceptable.

Do not create a generic Overlay message-validation framework.

Update existing message-kind guards so Command, Navigation, QuickSettings, ClawHUD, Profile, Shortcut, TabOrder, etc. reject unexpected BackButtonMapping payloads.

---

# 14. OverlayProcessController authority binding

Add two narrow delegates:

~~~text
capture BackButtonMapping state
mutate whole BackButtonMappingSettings
~~~

Add one method:

~~~csharp
BindBackButtonMappingAuthority(capture, mutate)
~~~

Follow the same ownership shape as BindTabOrderAuthority, BindQuickSettingsAuthority, BindClawHudAuthority, and BindShortcutAuthority.

Pass the delegates to each new NamedPipeOverlayServer.

OverlayProcessController does not own persistence or hardware.

No BackButtonMapping manager class.

---

# 15. AddonProcessHost binding

Bind before warm Overlay startup:

~~~csharp
_overlayController.BindBackButtonMappingAuthority(
    capture: CaptureOverlayBackButtonMappingAsync,
    mutate: MutateOverlayBackButtonMappingAsync);
~~~

## Capture

Use the existing frontend authority:

~~~text
_frontendControl.GetBootstrapAsync(...)
→ BackButtonMappingAvailable
→ Settings.BackButtonMapping
~~~

Map only those facts to OverlayBackButtonMappingState.

Do not read StartupSettingsCoordinator or settings.json directly.

## Mutation admission

Require:

~~~text
process not shutting down
AND
_overlayCaptureActive
~~~

NamedPipeOverlayServer separately checks Ready + Visible.

Then call only:

~~~csharp
_frontendControl.SetBackButtonMappingAsync(candidate, token)
~~~

Never call settings coordinator, settings store, mapper, or publisher directly from this Overlay path.

Determine success from:

~~~text
returned FrontendSettingsSnapshot.BackButtonMapping == requested candidate
~~~

If not equal, return failed result with the current authoritative mapping.

Do not duplicate product validation in AddonProcessHost.

---

# 16. State publication

Add:

~~~csharp
OverlayProcessController.RefreshBackButtonMappingAsync()
~~~

Rules:

~~~text
no server/delegate -> no-op
not Ready/Visible -> no-op
capture succeeds -> publish state
capture fails -> publish unavailable state
~~~

Unavailable fallback may be:

~~~text
Available = false
Mapping = BackButtonMappingSettings.Default
FailureMessage = "M1 / M2 mapping is unavailable."
~~~

A feature capture failure must not retire the Overlay.

---

# 17. Show and invalidation refresh

After successful OQ4 capture commit, publish alongside existing state refreshes:

~~~text
RefreshQuickSettingsAsync()
RefreshTabOrderAsync()
RefreshClawHudAsync()
RefreshShortcutAsync()
RefreshBackButtonMappingAsync()
~~~

Keep it fire-and-forget.

Also add RefreshBackButtonMappingAsync() to the current visible/captured StateInvalidated path.

No polling.

No direct BackButtonMappingChanged subscription in OverlayProcessController.

---

# 18. Server mutation handling

NamedPipeOverlayServer must:

1. validate the request;
2. require Ready + Visible;
3. require a bound mutation delegate;
4. execute mutation outside the sole read loop;
5. return one correlated authoritative result;
6. keep Hide/Dismiss/Navigation readable while the save runs.

Follow existing correlated mutation patterns.

Do not block ServeConnectionAsync on persistence.

---

# 19. Client mutation correlation

NamedPipeOverlayClient should expose:

~~~csharp
Task<OverlayBackButtonMappingMutationResponse>
    SendBackButtonMappingMutationAsync(
        BackButtonMappingSettings mapping,
        CancellationToken token = default);
~~~

Use:

- increasing request id;
- one pending request;
- one small SemaphoreSlim gate or equivalent current pattern;
- exact request-id correlation.

Do not allow two simultaneous M1/M2 mapping requests.

Clear/cancel the pending operation on disconnect/dispose.

No generic request broker.

---

# 20. Client state handler

Extend the final RunAsync handler set with:

~~~text
Func<OverlayBackButtonMappingState, Task>? backButtonMappingHandler
~~~

Existing overloads may forward null.

Reject malformed/mixed BackButtonMapping messages.

Do not reinterpret this state as generic Quick Settings.

---

# 21. Overlay App wiring

OverlayWindow must never hold NamedPipeOverlayClient.

App remains transport owner.

Wire:

~~~text
OverlayWindow.BackButtonMappingEditRequested
→ App
→ NamedPipeOverlayClient
→ Runtime
→ authoritative result
→ DispatcherQueue
→ OverlayWindow
~~~

Add a state handler:

~~~text
HandleBackButtonMappingStateAsync(state)
→ DispatcherQueue
→ ApplyBackButtonMappingState(state)
~~~

Transport failure:

~~~text
clear in-flight flag
show feature-local failure
keep Overlay visible
~~~

Do not dismiss the Overlay.

---

# 22. Authoritative settlement

State apply:

~~~text
_backButtonMapping = state.Mapping
_backButtonMappingAvailable = state.Available
_backButtonMutationInFlight = false
status = state.FailureMessage or normal informational caption
render both rows
refresh row selection if Controller is selected
~~~

Mutation result:

~~~text
success
→ apply returned authoritative state
→ clear failure
→ re-enable rows

failure
→ apply returned authoritative state
→ show failure message
→ re-enable rows
~~~

Never leave an optimistic preview displayed after a failed mutation.

---

# 23. Row rendering

Use the full enum range:

~~~csharp
var min = (double)Xbox360BackButtonTarget.Disabled;
var max = (double)Xbox360BackButtonTarget.XboxGuide;

_m1MappingRow.ApplyState(
    _backButtonMappingAvailable && !_backButtonMutationInFlight,
    min,
    max,
    1,
    (double)_backButtonMapping.M1);
~~~

Same for M2.

No separate per-button availability flags.

---

# 24. Xbox360-only semantics remain unchanged

Current product behavior:

~~~text
Xbox360 presentation
→ physical M1/M2 use BackButtonMappingSettings

SteamDeck presentation
→ M1 = R4
→ M2 = L4
→ BackButtonMappingSettings does not redefine SteamDeck rear buttons
~~~

The persisted Xbox360 mapping may still be edited while SteamDeck is active.

Do not:

- add Normal/Steam M1/M2 profiles;
- rewrite targets on presentation transition;
- disable the page just because SteamDeck is active;
- expose R4/L4 as Xbox360 target choices.

The caption is sufficient.

---

# 25. Main UI remains unchanged

Do not redesign:

~~~text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
~~~

except a strictly mechanical compile fix if genuinely required.

The Main UI keeps its existing ordered save chain.

Do not share UI save-chain classes across processes.

---

# 26. Do not extend generic Quick Settings

Do not add:

~~~text
QuickSettingsPageId.Controller
QuickSettingsRowId.M1
QuickSettingsRowId.M2
generic enum-row schema
Controller QuickSettings mutation adapter
~~~

Device/Profile generic Quick Settings remains separate.

M1/M2 already have a typed product contract; wrapping them in a second schema would be unnecessary translation.

---

# 27. Tests

## Existing domain tests

Keep all existing M1/M2 tests passing, including persistence/frontend/mapper/publisher/rearm/UI tests.

## New Overlay transport tests

Prefer:

~~~text
tests/SteamInputAddonforClaw.Tests/OverlayBackButtonMappingTransportTests.cs
~~~

Prove:

1. protocol version 13;
2. valid state round-trip;
3. malformed mapping fails closed;
4. request id must be positive;
5. hidden/not-ready server invokes zero mutation;
6. visible admitted request invokes exactly one mutation delegate;
7. result contains authoritative state;
8. wrong request id does not complete another pending request;
9. disconnect/dispose clears pending mutation;
10. unrelated message types reject BackButtonMapping payloads.

## OverlayProcessController tests

Prove:

~~~text
BindBackButtonMappingAuthority exists
visible refresh publishes state
hidden refresh does not publish
capture failure publishes unavailable state
~~~

## AddonProcessHost composition tests

Prove:

~~~text
binding uses _frontendControl
capture comes from bootstrap projection
mutation uses _frontendControl.SetBackButtonMappingAsync(...)
no direct StartupSettingsCoordinator.ChangeBackButtonMapping in Overlay path
post-Show refresh includes M1/M2
StateInvalidated refresh includes M1/M2
~~~

## Overlay UI tests

Prove:

~~~text
Controller dispatches to BuildControllerPage(rows)
Controller no longer uses placeholder content
exactly two mapping rows: M1 and M2
both use DiscreteChoice
all enum values have user-facing labels
M1 candidate preserves current M2
M2 candidate preserves current M1
duplicate targets remain valid
shared OverlayRowSelection remains navigation authority
~~~

Do not invent a new WinUI test host merely for this PR.

---

# 28. Expected files

Primary:

~~~text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs

tests/SteamInputAddonforClaw.Tests/OverlayBackButtonMappingTransportTests.cs
relevant existing Overlay process/composition tests
relevant Overlay UI source/composition tests
~~~

Do not change Full1902 controller/output code unless a real compile issue proves it necessary.

---

# 29. Explicit non-goals

Not in PR1:

- Gamebar/WING mapping;
- Center M Button mapping;
- hotkey/application editor;
- joystick LED;
- vibration strength;
- rumble;
- gyro;
- per-game M1/M2 profiles;
- SteamDeck rear-button remapping;
- macros;
- firmware/native MSI remap;
- QAM palette/font work;
- Motiva Sans;
- Full1902 lifecycle changes.

---

# 30. Overengineering guardrails

Reuse:

~~~text
BackButtonMappingSettings
BackButtonMappingValidation
IAddonFrontendControl.SetBackButtonMappingAsync
StateInvalidated
OverlayProcessController
NamedPipeOverlayServer/Client
OverlayValueRow
OverlayRowSelection
~~~

Add only:

~~~text
one Overlay state
one correlated whole-record mutation
one capture/mutate binding
one Controller partial page
two value rows
one in-flight UI flag
~~~

Do not add managers, generic schemas, caches, retry state machines, epochs, barriers, or another controller reader.

---

# 31. Validation without handheld hardware

This PR is intentionally suitable for implementation away from the MSI Claw.

Code/CI can validate:

- wire shape;
- authority composition;
- whole-record mutation;
- persistence path reuse;
- correlation;
- unavailable behavior;
- Controller row construction;
- existing navigation wiring;
- no duplicate controller authority.

Hardware follow-up later only needs to confirm:

~~~text
open Overlay
→ Controller tab
→ persisted M1/M2 values shown
→ Left/Right changes target
→ close/reopen preserves value
→ Xbox360 uses selected mapping
→ SteamDeck still exposes M1=R4 and M2=L4
~~~

Do not block this frontend projection on unrelated PID/HidHide/VIIPER retesting when those paths are unchanged.

---

# 32. Definition of done

Complete when:

~~~text
Overlay Controller placeholder
→ real Controller page
→ M1 + M2 discrete rows
→ existing BackButtonMappingSettings
→ existing Runtime persistence/output authority
~~~

while preserving:

~~~text
LB/RB tab navigation
Up/Down row navigation
Left/Right adjustment
A behavior
B dismiss
pointer input
one mutation in flight
authoritative settlement
Overlay protocol v13
Full1902 lifecycle unchanged
~~~
