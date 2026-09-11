# Work Order — SF-V2-07: Overlay Generic Device Renderer + Real Device Binding

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase E  
> **Reviewed repository head:** `main` at `24801eb3b3a7e0d5b988389dd43e974344e10387`  
> **Previous phase:** SF-V2-06 Overlay generic Quick Settings v7 transport — squash-merged as PR #507  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR roadmap:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Historical Overlay UI/lifecycle authorities:** OQ4 + OQ5 UI work orders listed below  
> **Next phase:** SF-V2-08 Shared Profile projection/dispatch + QAM generic Profile migration

---

## 1. Goal

Replace the temporary Device-page preview fixture in `SteamInputAddonforClaw.Overlay.exe` with the **real shared Device Quick Settings product** carried by:

```text
QuickSettingsPageSnapshot(Device)
QuickSettingsMutationIntent
QuickSettingsMutationResult
```

The Overlay must become the second real renderer of the same Device Quick Settings product already consumed by Steam QAM.

After this PR, Device Quick Settings parity is complete at the product-model level:

```text
Runtime Device authorities
        ↓
FrontendDeviceQuickSettingsSnapshot
        ↓
QuickSettingsPresentation.BuildDevice(...)
        ↓
QuickSettingsPageSnapshot(Device)
        ↓
        ├─ QAM generic renderer
        └─ Overlay generic renderer
```

The required user-facing Device controls are the rows projected by current Runtime truth, in the shared page's exact section/row order:

```text
TDP
  TDP Control
  Plugged in · PL1
  Plugged in · PL2
  On battery · PL1
  On battery · PL2

CPU Boost
  CPU Boost
  Plugged in
  On battery

Windows Power Mode
  Windows Power Mode
  Plugged in
  On battery
```

The Overlay must not reconstruct this product table locally.

The core rule for this PR is:

> **Render and interact with the shared page generically; keep all controller, hardware, persistence, lifecycle, and product-policy authority where it already exists.**

---

## 2. Why this PR exists now

Shared Frontend V2 intentionally split transport from renderer binding.

Completed sequence:

```text
SF-V2-01 / PR #496
→ shared Device aggregate read

SF-V2-02 / PR #498
→ Overlay Device transport/lifecycle foundation

SF-V2-03
→ shared Quick Settings contract + Device projection + central mutation adapter

SF-V2-04 / PR #505
→ Frontend/QAM generic RPC seam

SF-V2-05 / PR #506
→ QAM Device generic renderer

SF-V2-06 / PR #507
→ Overlay v7 generic Quick Settings transport
```

Current Overlay production code now receives `QuickSettingsPageSnapshot(Device)` but intentionally does not render it. `OverlayWindow` still shows only the OQ5 validation fixture:

```text
Toggle Preview
Unavailable Toggle Preview
Slider Preview
Unavailable Slider Preview
Navigation Preview 01..12
```

That fixture has served its purpose. SF-V2-07 removes it and binds the already-proven row primitives to the real shared product.

---

## 3. Required reading before implementation

Read current `main` at implementation time. Do not code from this work order alone if source has moved.

### 3.1 Full PID1902 authority — mandatory

Read in the precedence defined by:

```text
docs/Full 1902 Implementation/README.md
```

At minimum:

```text
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Frozen authority invariant:

```text
Runtime = controller / hardware / persistence authority
Overlay = disposable transient presentation client
```

This PR must not make the Overlay a controller owner or recovery participant.

### 3.2 Shared Frontend V2 — mandatory

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_03_SHARED_QUICK_SETTINGS_PRODUCT_CONTRACT_WORK_ORDER.md
docs/shared-frontend/SF_V2_05_QAM_DEVICE_GENERIC_RENDERER_MIGRATION_WORK_ORDER.md
docs/shared-frontend/SF_V2_06_OVERLAY_GENERIC_QUICK_SETTINGS_V7_TRANSPORT_WORK_ORDER.md
```

SF-V2-05 is the semantic parity reference for Device pending drafts, grouped TDP behavior, linked constraints, and authoritative settlement.

Do not port its JavaScript architecture literally.

### 3.3 Overlay lifecycle/navigation/UI — mandatory

Read:

```text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md
docs/overlayui/OQ5_UI_05_TOGGLE_ROW_PRIMITIVE_WORK_ORDER.md
docs/overlayui/OQ5_UI_06_SLIDER_ROW_PRIMITIVE_WORK_ORDER.md
docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md
docs/overlayui/OQ5_UI_09_TAB_ORDER_RUNTIME_PERSISTENCE_WORK_ORDER.md
docs/overlayui/OQ5_UI_10_SETTING_PAGE_TAB_ORDER_EDITOR_WORK_ORDER.md
docs/overlayui/OQ5_UI_11_SHORTCUT_2X2_SLOT_SHELL_WORK_ORDER.md
```

Use current source when historical prose describes already-completed work as future work.

### 3.4 Current source — minimum set

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

Inspect existing tests before changing helpers:

```text
tests/SteamInputAddonforClaw.Tests/OverlayToggleRowTests.cs
tests/SteamInputAddonforClaw.Tests/OverlaySliderRowTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDelayedSliderCommitTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayRowSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

---

## 4. Reviewed baseline facts

This work order was prepared against:

```text
main = 24801eb3b3a7e0d5b988389dd43e974344e10387
```

Current protocol versions:

```text
FrontendTransportProtocol.CurrentVersion = 28
OverlayTransportProtocol.CurrentVersion  = 7
```

SF-V2-07 is a renderer/binding PR.

Required result:

```text
FrontendTransportProtocol = 28   // unchanged
OverlayTransportProtocol  = 7    // unchanged
```

If another protocol-changing PR lands first, re-check current source. SF-V2-07 itself should still require **no protocol bump** unless implementation discovers a genuinely missing wire shape, in which case stop and redesign explicitly rather than silently changing v7 semantics.

Current `.Overlay` v7 already provides:

```text
Runtime -> Overlay
QuickSettingsPageState(QuickSettingsPageSnapshot)

Overlay -> Runtime
QuickSettingsMutationRequest(QuickSettingsMutationIntent)

Runtime -> Overlay
QuickSettingsMutationResult(QuickSettingsMutationResult)
```

with:

```text
strict generic payload decoding
one write gate per side
one mutation correlation slot/gate
mutation execution outside the server read loop
Ready + Visible transport admission
Runtime capture/shutdown admission
Device-only current page scope
StateInvalidated-driven visible refresh
no polling
```

Do not redesign this transport in SF-V2-07.

---

## 5. Frozen Full1902 / OQ4 lifecycle invariants

The Overlay is already a controller-modal surface through OQ4. Real Device controls must not weaken that separation.

Normal open remains conceptually:

```text
Overlay Show acknowledged
→ OQ4 capture commits
→ SAME currently-selected virtual presentation remains attached
→ game-facing output stays neutral
→ semantic controller navigation goes to Overlay
→ Device page may then be published/rendered
```

Normal close remains:

```text
close requested
→ stop accepting Overlay navigation
→ Hide
→ keep current presentation neutral
→ consumed-control release gate
→ clear Overlay capture
→ resume SAME presentation
```

SF-V2-07 must not:

```text
switch PID1901/PID1902
open DirectInput
read virtual controller input
change HidHide
own VIIPER
switch Xbox360/SteamDeck merely because Overlay opens
add controller recovery logic
change sleep/resume ownership
change shutdown/restart authority
```

A Device mutation failure is a feature/UI failure, not an Overlay capture/session-loss condition.

---

## 6. Scope boundary

### In scope

```text
replace Device preview fixture with real shared Device page
render QuickSettingsPageSnapshot(Device) in payload order
Toggle -> OverlayToggleRow
Numeric Slider -> OverlaySliderRow
Discrete Slider -> OverlaySliderRow with index/value mapping
register rendered Device rows with existing OQ5 OverlayRowSelection
send all Device mutations through NamedPipeOverlayClient.SendQuickSettingsMutationAsync
Immediate toggle behavior from shared CommitPolicy
Trailing slider behavior from shared CommitPolicy
independent slider pending drafts
grouped TDP pending draft
linked-slider correction from shared metadata
pending draft preservation across ordinary page refresh
current settlement authoritative page handling
typed failure handling
transport/operation failure local fail-closed behavior
same-section pending cancellation before immediate parent toggle
selected Device-row identity preservation across page rebuild
hide/dispose cancellation of unsubmitted drafts
removal of temporary Device preview rows
removal of Overlay-owned production delay constant
focused renderer/binder tests
```

### Explicitly not in scope

```text
Profile shared projection
Profile Overlay binding
QAM Profile migration
Intel FPS exposure
Resolution exposure
Fan Control shared Quick Settings exposure
Battery / LED / vibration exposure
Center M authority control in Overlay
Controller/Shortcut/Setting redesign
Tab ordering redesign
Overlay shell visual redesign
new control kinds
new transport messages
protocol version bump
Runtime feature changes
QuickSettingsPresentation changes
QuickSettingsMutationAdapter changes
Full1902 controller lifecycle changes
OQ4 capture changes
new polling
```

Do not opportunistically mix UI-polish work such as new tab chrome/margins/layout redesign into this PR unless required to render the shared rows correctly.

---

## 7. Target architecture after SF-V2-07

```text
Runtime feature authorities
  TDP / CPU Boost / Power Mode
          |
          v
InProcessAddonFrontendControl
          |
          v
QuickSettingsPresentation.BuildDevice(...)
          |
          v
QuickSettingsPageSnapshot(Device)
          |
          v
Overlay v7 generic pipe
          |
          v
App.xaml.cs
  owns NamedPipeOverlayClient
  marshals page/mutation settlement to UI thread
          |
          v
small Overlay-local Quick Settings Device binder
  owns only renderer-local pending drafts/timers/error state
          |
          +----------------------+
          |                      |
          v                      v
OverlayToggleRow          OverlaySliderRow
          |
          v
existing OverlayRowSelection / scroll / OQ4 semantic navigation
```

There must still be exactly one real product mutation authority:

```text
QuickSettingsMutationAdapter
```

The Overlay binder creates **intent**, not feature policy.

---

## 8. Current Overlay code facts that define the implementation seam

### 8.1 `App` owns the transport client

Current `App.xaml.cs` owns:

```text
NamedPipeOverlayClient _client
ConnectAndRunAsync()
HandleQuickSettingsPageAsync(...)
HandleNavigationAsync(...)
HandleCommandAsync(...)
```

Keep that ownership.

Do **not** pass `NamedPipeOverlayClient` into `OverlayWindow` or a row object.

The UI should receive a narrow mutation delegate, for example conceptually:

```csharp
Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>>
```

owned/wired by `App`.

### 8.2 `OverlayWindow` already owns shell/navigation composition

Current `OverlayWindow` owns:

```text
_tabPages
_pageRows
_rowSelection
BodyScroll
row selected visuals
BringSelectedRowIntoView()
semantic Up/Down/Left/Right/Accept behavior
```

Reuse it.

Do not create:

```text
QuickSettingsNavigationManager
DeviceSelectionManager
FocusCoordinator
RendererHost service
```

### 8.3 Toggle and slider primitives are already feature-agnostic

`OverlayToggleRow` already supports:

```text
ApplyState(authoritative available/on)
controller Accept toggle
pointer/touch toggle
feedback suppression on authoritative apply
```

`OverlaySliderRow` already supports:

```text
numeric min/max/step
immediate local preview
controller Left/Right one step
pointer/touch slider edits
value formatter
feedback suppression on authoritative apply
```

Use them rather than creating Device-specific controls.

### 8.4 The current delayed helper is intentionally narrow but owns an obsolete production constant

Current `OverlayDelayedSliderCommit` has proven useful mechanics:

```text
latest draft wins
trailing delay cancellation/restart
generation-based stale completion rejection
pending draft query
CancelUnsubmitted()
Dispose()
```

Preserve those mechanics.

But this field must stop being product authority:

```csharp
OverlayDelayedSliderCommit.ProductionDelay = 2000 ms
```

The shared row's `CommitPolicy.DelayMilliseconds` is now the source.

---

## 9. Recommended production shape: one small page-local binder

Create the smallest internal helper that keeps `OverlayWindow` from becoming a feature-specific mutation/state blob.

A conceptual name is:

```text
OverlayQuickSettingsPageBinding
```

or:

```text
OverlayDeviceQuickSettingsBinding
```

Exact naming is flexible.

It is acceptable for this helper to be reusable by SF-V2-09 Profile later, but **do not design a plugin framework now**.

The helper may own only surface-local facts such as:

```text
latest authoritative QuickSettingsPageSnapshot
rendered row bindings keyed by QuickSettingsRowId
pending delayed commit entries keyed by row/group identity
last local failure message
optional one narrow mutation-busy fact
```

It must not own:

```text
hardware state
settings persistence
Runtime feature state
controller capture
transport connection lifetime
PID/HidHide/VIIPER facts
feature-specific TDP/CPU/Power DTOs
```

A small page-local binding helper is preferred over adding feature logic directly to every row callback.

---

## 10. App ↔ Window/binder transport boundary

### 10.1 Page publication

`HandleQuickSettingsPageAsync(QuickSettingsPageSnapshot page)` currently only logs.

SF-V2-07 must marshal the Device page to the UI thread and complete only after the page has been accepted/applied.

Conceptually:

```csharp
private Task HandleQuickSettingsPageAsync(QuickSettingsPageSnapshot page)
{
    var completion = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);

    if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(() =>
    {
        try
        {
            _window?.ApplyQuickSettingsPage(page);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }))
    {
        completion.TrySetException(...);
    }

    return completion.Task;
}
```

Equivalent implementation is acceptable.

Do not run WinUI row creation/application on the pipe read thread.

### 10.2 Mutation request

App should expose a narrow async callback to the Window/binder that calls only:

```csharp
_client.SendQuickSettingsMutationAsync(intent)
```

The binder must not know about wire message kinds/request IDs/correlation.

Do not create another mutation queue around the client's existing correlation gate.

---

## 11. Device-only page admission in this PR

SF-V2-07 renders only:

```text
QuickSettingsPageId.Device
AppId == null
```

If another page reaches the current Device binder unexpectedly:

```text
ignore/fail closed locally
log narrow diagnostic
perform zero mutation
```

Do not render Profile early merely because the wire can carry its PageId.

The existing Profile Overlay tab remains its current placeholder until SF-V2-09.

---

## 12. Render sections and rows exactly in shared payload order

The shared page is product authority for:

```text
section order
section label
row order
row label
control kind
value
availability
writability
slider spec
commit policy
commit group
linked constraints
```

Do not sort by enum numeric value.

Do not create local arrays such as:

```text
TdpRows[]
CpuBoostRows[]
PowerModeRows[]
```

Do not infer sections from RowId prefixes.

Render:

```text
foreach page.Sections in order
    render section label if present
    foreach section.Rows in order
        render row from ControlKind
```

The section visual may be a simple heading + stacked rows. Shared section identity does not require card chrome.

Keep the existing Overlay visual language; this is not a redesign PR.

---

## 13. Row binding identity

Each rendered Quick Settings row should retain its shared identity alongside the existing visual/capability pair.

Current `OverlayWindow` has conceptually:

```csharp
OverlayRow(Border Container, OverlayRowCapabilities Capabilities)
```

For Device generic binding, use the smallest refinement needed to preserve stable row identity, e.g. conceptually:

```csharp
OverlayRow(
    Border Container,
    OverlayRowCapabilities Capabilities,
    QuickSettingsRowId? QuickSettingsRowId = null)
```

or keep a parallel page-local identity list/dictionary.

Do not teach the global selection model about Quick Settings enums unless truly necessary.

The goal is only to preserve the currently selected Device row across ordinary authoritative page rebuilds.

---

## 14. Availability and writability

A row is mutable only when all required facts are valid:

```text
row.Available == true
row.Writable == true
row.Value structurally matches control/value kind
required SliderSpec is valid for a slider
current Overlay page/binding is alive
no narrow surface-local mutation block that intentionally disables editing
```

Do not reinterpret Runtime product availability.

A row can be rendered unavailable/disabled but must not emit a mutation.

Do not invent feature-specific unavailability rules in Overlay.

---

## 15. Toggle rendering and immediate mutation

For:

```text
QuickSettingsControlKind.Toggle
```

require a Boolean `QuickSettingsValue`.

Render through:

```text
OverlayToggleRow
```

Apply authoritative/effective state through its existing:

```csharp
ApplyState(isAvailable, isOn)
```

### 15.1 Mutation intent

An independent immediate Toggle submits exactly one value:

```csharp
new QuickSettingsMutationIntent(
    PageId: page.PageId,
    AppId: page.AppId,
    EditedRowId: row.RowId,
    Values:
    [
        new QuickSettingsRowValue(
            row.RowId,
            QuickSettingsValue.Boolean(desired))
    ])
```

Do not call feature-specific Device methods from Overlay.

### 15.2 Commit policy

Current shared Device toggles are:

```text
CommitMode = Immediate
DelayMilliseconds = 0
```

The Overlay should validate/use that shared policy rather than maintaining a Device toggle table.

If a future malformed page declares a Toggle with unsupported delayed behavior, fail that row closed rather than inventing a second toggle scheduler in this PR.

---

## 16. Immediate parent toggle must retire same-section unsubmitted slider work

This is normal product behavior already established in QAM.

Example:

```text
CPU Boost AC draft pending
→ CPU Boost toggle OFF
→ old AC/DC pending draft must not fire two seconds later
```

Likewise:

```text
TDP grouped draft pending
→ TDP Control OFF
→ grouped delayed draft must not fire later
```

Before submitting an immediate Toggle mutation:

```text
cancel unsubmitted pending commit entries belonging to the same shared SectionId
```

Drive this from the section identity associated with the row.

Do not hard-code:

```text
if CPU row ...
if TDP row ...
```

An already-submitted Runtime mutation is not held/canceled merely to make this simpler; its later completion remains governed by current-generation settlement rules.

---

## 17. Numeric slider rendering

For:

```text
ControlKind = Slider
SliderSpec.Kind = Numeric
```

require:

```text
QuickSettingsValueKind.Integer
valid Minimum <= Maximum
Step > 0
```

Render through `OverlaySliderRow` using shared:

```text
Minimum
Maximum
Step
Suffix
```

Visible value formatting must use the shared suffix.

Examples from current Device page include:

```text
20W
25W
```

Do not locally decide TDP ranges or suffixes.

---

## 18. Discrete slider rendering — value/index separation is mandatory

CPU Boost and Windows Power Mode are shared `Discrete` sliders.

The product value is the option's `Value`.

The WinUI slider position is the option's **ordered index**.

Required mapping:

```text
authoritative/pending product value
→ find index where option.Value == product value
→ OverlaySliderRow receives numeric range 0..(N-1), step 1, value=index
→ value formatter displays option.Label for current index

user chooses index
→ map index back to option.Value
→ create QuickSettingsValue.Integer(option.Value)
```

Do **not** assume:

```text
option.Value == option index
option values are contiguous
option values start at 0
```

Tests must include a synthetic non-contiguous option set such as:

```text
10 = Low
20 = Balanced
40 = High
```

to prove the renderer uses ordered option metadata correctly.

---

## 19. Slider preview remains immediate

Keep OQ5-UI-06 behavior:

```text
controller Left/Right or pointer/touch
→ OverlaySliderRow updates local preview immediately
→ desired product value enters pending draft
→ Runtime mutation waits according to shared CommitPolicy
```

Do not move debounce timing into `OverlaySliderModel` or WinUI `ValueChanged`.

The existing row primitive remains responsible only for immediate local interaction mechanics.

---

## 20. Generic pending identity

Use typed shared identity, not feature strings.

Conceptually:

```text
row.CommitGroupId == null
→ PendingKey.Row(row.RowId)

row.CommitGroupId != null
→ PendingKey.Group(row.CommitGroupId.Value)
```

A small private/internal discriminated record/struct is acceptable.

Do not use product strings such as:

```text
"tdp"
"cpu-ac"
"device-power-dc"
```

Do not create a global mutation registry.

---

## 21. Refine `OverlayDelayedSliderCommit` around the shared mutation payload

The current helper's generation/timer mechanics are correct, but its production surface is still shaped around the old fake `double` fixture.

SF-V2-07 should make it useful for the real generic Device binding with the smallest change.

### 21.1 Required retained mechanics

Preserve:

```text
trailing debounce
latest draft replaces unsubmitted draft
restart window on each edit
one current generation/token
stale in-flight completion ignored
pending draft query
CancelUnsubmitted()
Dispose()
```

### 21.2 Remove Overlay-owned policy constant

Remove production ownership of:

```csharp
OverlayDelayedSliderCommit.ProductionDelay
```

The delay must be supplied from:

```csharp
row.CommitPolicy.DelayMilliseconds
```

A helper may receive the delay per schedule or per pending entry.

Do not replace one global `2000` constant with another Overlay-specific `2000` constant.

### 21.3 Preferred payload shape

Because the preview fixture is removed, the helper no longer needs to pretend the production payload is one `double`.

A narrow typed refinement is preferred, conceptually:

```text
pending payload = QuickSettingsMutationIntent
settlement = QuickSettingsMutationResult OR operation/transport failure
```

The binder owns visible pending values/group draft; the helper owns timer/generation/submission mechanics.

Equivalent separation is acceptable if it stays small and typed.

Do not generalize to `object`, arbitrary JSON, reflection, or a cross-feature scheduler service.

---

## 22. Commit policy validation

Current Device slider policy is:

```text
TrailingDebounce(2000ms)
```

The renderer must use the payload, not assume the literal.

If a test page changes the delay to:

```text
750ms
```

then the helper must be scheduled with:

```text
750ms
```

without editing Overlay source.

Malformed/unsupported policy should fail the row closed rather than silently falling back to 2000ms.

This is important: a fallback product constant would recreate the duplication Shared Frontend V2 exists to remove.

---

## 23. Independent slider draft

For a slider with:

```text
CommitGroupId == null
```

the delayed mutation carries exactly one shared row value:

```text
EditedRowId = row.RowId
Values = [ edited row value ]
```

Examples in the current Device page:

```text
DeviceCpuBoostAc
DeviceCpuBoostDc
DevicePowerModeAc
DevicePowerModeDc
```

Do not hard-code these IDs into scheduling logic; they are examples only.

---

## 24. Grouped slider draft — seed from the containing shared section

Current Device TDP is the only grouped Device section.

Do not teach Overlay that by RowId table.

On the **first** edit of a grouped row:

```text
find the containing shared section
→ read its current value-bearing rows in section order
→ every required row must have a non-null structurally valid QuickSettingsValue
→ seed one whole pending draft
→ replace the edited row value
→ apply linked constraints
→ schedule one mutation keyed by CommitGroupId
```

Current TDP projection intentionally gives that section the exact values required by the central adapter:

```text
TDP Enabled boolean
AC PL1 integer
AC PL2 integer
DC PL1 integer
DC PL2 integer
```

But the Overlay must discover them from section rows, not hard-code those five RowIds.

If the section cannot be safely seeded, do not schedule a partial grouped mutation.

---

## 25. Later grouped edits reuse the current whole draft

If another row in the same commit group is edited before settlement:

```text
reuse current pending whole draft
→ overlay newest edited value
→ re-apply relevant linked constraint
→ restart trailing delay from newest edit
→ keep one pending group
```

Required normal sequence:

```text
AC PL1 edit
→ whole TDP draft A

DC PL2 edit before delay
→ reuse draft A
→ update DC PL2
→ whole TDP draft B becomes current
→ only B is eventually submitted
```

Do not create four independent TDP timers.

---

## 26. Linked-slider constraints are metadata-driven

Use only:

```text
page.LinkedSliderConstraints
row identities
current pending integer values
shared slider bounds
constraint.MinimumGap
```

Required initial rule is the existing lower/upper gap relationship.

Generic behavior equivalent to QAM is:

```text
if edited lower and upper < lower + gap:
    prefer raising upper if within upper.Maximum
    otherwise clamp lower to upper.Maximum - gap

if edited upper and lower > upper - gap:
    prefer lowering lower if within lower.Minimum
    otherwise clamp upper to lower.Minimum + gap
```

Use shared row `SliderSpec.Minimum/Maximum` for bounds.

Do not inspect:

```text
"PL1" / "PL2" label text
known (8,30,8,37) tuple
known (8,35,8,45) tuple
TDP-specific enums/configuration DTOs
```

The Runtime projection already decided whether a linked constraint exists and what its gap is.

---

## 27. Linked companion preview must update immediately

When a constraint adjusts a second member of the pending group, the visible companion row must reflect that correction immediately.

Example:

```text
PL1 visible 25
PL2 visible 26
gap = 2

user raises PL1 to 26
→ pending draft corrects PL2 to 28
→ BOTH visible rows immediately show 26 / 28
→ one delayed whole-group commit follows
```

Do not wait for Runtime settlement to display the local linked correction.

The pending draft is the current visible draft for every row value it contains.

---

## 28. Effective visible value rule

For every value-bearing Device row:

```text
if a current pending row/group draft contains RowId:
    visible value = pending value
else:
    visible value = latest authoritative page row.Value
```

This is the same observable rule QAM now uses.

The authoritative page must still replace all **unrelated** rows immediately.

Do not freeze an entire old page merely because one row is pending.

---

## 29. Ordinary `QuickSettingsPageState` while a draft is pending

A Runtime `StateInvalidated` may republish Device while the user is still inside the debounce window.

Required behavior:

```text
latest authoritative page arrives
→ store it as newest authority
→ rebuild/update rows from it
→ overlay current pending values for affected row/group
→ unrelated rows adopt latest Runtime state
→ pending timer/draft stays current
```

Do not clear pending drafts merely because a page state frame arrived.

Do not snap the edited slider back to old authoritative value during the debounce window.

No page revision/epoch is required.

---

## 30. Current mutation settlement is authoritative

`QuickSettingsMutationResult.Page` is a fresh Runtime projection.

For the **current generation** settlement:

```text
clear that row/group pending entry
→ apply result.Page as newest authority
→ render result.Page
→ if Succeeded == false, show FailureMessage locally
```

This applies both to:

```text
Succeeded = true
Succeeded = false
```

A typed failure may still reflect persisted or partially-applied authoritative truth. Never roll back by patching the old page with the submitted draft.

---

## 31. Stale completion must never replace a newer draft

This is a normal async I/O path already proven in `OverlayDelayedSliderCommit`.

Example:

```text
draft A waits
→ A submits
→ user edits again
→ draft B becomes current
→ A settles late
```

Required:

```text
A settlement does not clear B
A settlement does not apply A.Page over B preview
B remains current
```

Keep one small generation/token per pending entry/helper.

Do not add global epochs/revisions/barriers.

---

## 32. Operation/transport failure is not a product settlement

SF-V2-06 deliberately distinguishes:

```text
valid QuickSettingsMutationResult
vs
thrown Runtime/transport operation failure
```

`NamedPipeOverlayClient.SendQuickSettingsMutationAsync` throws for the latter.

For a **current** delayed/immediate mutation transport failure:

```text
clear/retire that current local pending draft
→ render latest cached authoritative page for affected rows
→ expose/log a narrow local failure message
→ keep Overlay process/session alive if the pipe itself remains healthy
```

Do not synthesize a fake authoritative product page in Overlay.

Do not convert feature failure into OQ4 capture failure.

If the pipe itself actually dies, existing Overlay process/session-loss handling owns retirement.

---

## 33. Immediate mutation busy behavior — keep it narrow

A single page-local mutation-busy fact is acceptable if needed to prevent an immediate Toggle double-submit or conflicting edit while its response is outstanding.

Do not create a mutation state machine.

Busy is surface-local only and must never:

```text
hold OQ4 capture open
block Hide/Dismiss
be persisted
become Runtime authority
```

If current delayed entries are already in flight, normal generation/result handling remains authoritative.

---

## 34. Preserve selected Device row identity across authoritative rebuilds

A shared page refresh may change row availability or row presence, especially when a parent toggle changes.

Do not reset the user's Device selection to the first row on every `StateInvalidated` page.

Before applying/rebuilding Device rows:

```text
capture selected QuickSettingsRowId when current selected Device row has one
```

After rebuilding:

```text
if the same RowId still exists and is selectable
→ use its new index as preferred selection
else
→ let existing OverlayRowSelection choose first selectable row
```

Reuse:

```csharp
OverlayRowSelection.SetRows(rows, preferredIndex)
```

Do not add a second selection model.

This stable identity rule is important when rows appear/disappear after TDP/CPU/Power toggles.

---

## 35. Ordinary page refresh must not reset Device scroll to top

Current `ApplySelectedTabVisualState()` intentionally resets scroll on an actual tab change.

Do **not** call that full path merely to apply a refreshed Device page.

For a Device page refresh while Device is already selected:

```text
replace/update Device page content
→ preserve selected row identity when possible
→ refresh row highlight
→ bring selected row into view only if needed
→ do not intentionally reset BodyScroll to 0
```

Actual tab changes keep existing behavior.

---

## 36. Page/section unavailable state and messages

Use the shared product facts.

### Whole page unavailable

If:

```text
page.Available == false
```

render a simple non-editable message using `page.Message` where present.

No Device mutation may be emitted.

### Section/row unavailable

Preserve shared rows/order where useful, but unavailable/non-writable rows must be disabled/non-selectable mutation targets.

A `section.Message` may be shown with simple text if present.

Do not invent feature-specific error copy where the shared page already provides a message.

Do not add a global notification framework for this PR.

---

## 37. Section headings are product text, not feature detection

Render `QuickSettingsSection.Label` as simple section heading text when non-empty.

Do not use it for logic.

Wrong:

```text
if label == "TDP" then grouped behavior
if label contains "PL1" then lower-row behavior
```

Correct:

```text
grouping      → CommitGroupId
constraint    → LinkedSliderConstraints
control shape → ControlKind / SliderSpec
identity      → RowId / SectionId
```

---

## 38. Remove the temporary Device preview fixture completely

Delete/replace Device-only preview construction for:

```text
Toggle Preview
Unavailable Toggle Preview
Slider Preview
Unavailable Slider Preview
Navigation Preview 01..12
```

Remove obsolete fields/constants used only by that fixture, including:

```text
NavigationPreviewRowCount
_sliderPreviewCommit
```

where no longer used.

Do not remove or redesign:

```text
Setting tab-order editor
Shortcut 2x2 shell
Profile placeholder
Controller placeholder
shared tab/navigation shell
```

---

## 39. Hide / dismiss must never wait for debounce

At the start of the existing Overlay hide path:

```text
cancel all current UN-SUBMITTED Device delayed drafts
```

Then continue normal OQ4 hide immediately.

Required:

```text
pending timer exists
→ B / outside click / Runtime Hide
→ timer canceled
→ Hidden acknowledgement is not delayed
→ OQ4 capture retirement continues
```

If a mutation already passed its delay and was submitted:

```text
Hide still proceeds immediately
→ operation may settle later
→ hidden/disposed UI does not let obsolete settlement regain visual authority
```

Do not wait for mutation completion in `HideForPocAsync()`.

---

## 40. Window/process teardown

On actual Overlay window/process teardown:

```text
prevent new local scheduling
cancel unsubmitted timers
invalidate/suppress obsolete UI settlement callbacks
dispose page-local helpers
```

Do not attempt to cancel/rollback Runtime hardware state from the Overlay process.

Runtime/feature teardown owns already-submitted operations.

---

## 41. Touch/pointer and controller must use the same intent path

The existing primitives already converge pointer and controller edits through their desired-value callbacks.

Preserve that.

Required:

```text
controller Accept on Toggle
pointer ToggleSwitch
→ same immediate QuickSettingsMutationIntent path

controller Left/Right on Slider
pointer Slider drag
→ same pending draft / commit-group path
```

Do not add separate touch-specific feature mutation code.

---

## 42. Do not duplicate current Device product knowledge in Overlay

After SF-V2-07, production Overlay code must not contain its own copy of:

```text
"TDP Control"
"CPU Boost"
"Windows Power Mode"
"Plugged in · PL1"
"On battery · PL2"
CPU Boost seven-option labels
Power Mode three-option labels
known Claw TDP gap tuples
2000ms product delay
feature-specific Device mutation method names
```

Tests may use synthetic/example labels as test data.

Production rendering comes from the shared page.

---

## 43. No contract / Runtime / transport changes expected

SF-V2-07 should normally have **zero production diff** in:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.QamHost/**
src/SteamInputAddonforClaw/Devices/MSI/Claw/**
src/SteamInputAddonforClaw/VirtualOutput/**
HidHide / PID mode code
```

If implementation appears to require a new wire field/control kind/protocol bump, stop and verify whether the renderer is incorrectly rebuilding product semantics rather than consuming the existing closed contract.

---

## 44. Expected production files

Likely production scope:

```text
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
```

Recommended small new helper:

```text
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
```

or equivalent narrow page-local binder.

Possible tiny supporting changes only if actually needed:

```text
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
```

Do not change those primitives merely for stylistic refactoring.

No XAML redesign is expected; `OverlayWindow.xaml` should remain unchanged unless a minimal host element is genuinely required by the chosen page-binding implementation.

---

## 45. Recommended implementation sequence

Keep the implementation reviewable in this order.

### Step A — Refine delayed commit helper

- remove `ProductionDelay` as product source;
- accept delay from caller/shared policy;
- adapt pending payload/settlement to the real Quick Settings binding;
- preserve generation/cancel/dispose semantics;
- update deterministic helper tests first.

### Step B — Add pure/shared-page interaction helper logic

Implement/test without XAML where practical:

```text
pending key derivation
value conversion
independent intent construction
group draft seeding
linked-constraint application
discrete index/value mapping
pending-over-authoritative effective value
```

Keep it typed and page-local.

### Step C — Bind generic rows to existing primitives

- section/row order from page;
- Toggle -> `OverlayToggleRow`;
- Numeric/Discrete Slider -> `OverlaySliderRow`;
- register capabilities into existing `_pageRows[Device]`.

### Step D — Wire App transport delegate/page callback

- App owns client;
- UI gets narrow mutation delegate;
- page callbacks marshal to DispatcherQueue;
- no client ownership inside Window/binder.

### Step E — Replace preview fixture and preserve selection/scroll

- remove temporary rows;
- preserve selected RowId across page rebuild;
- do not reset scroll on ordinary refresh;
- cancel unsubmitted drafts on hide.

### Step F — full regression pass

Run all Overlay transport/lifecycle/navigation tests plus QAM/shared Quick Settings tests.

---

## 46. Deterministic test plan — delayed commit mechanics

Do not make CI sleep for the real debounce duration.

Reuse/adapt the existing manual-delay seam.

Required tests:

### A. Delay comes from shared policy input

```text
schedule with 750ms policy
→ helper receives/uses 750ms
→ no Overlay production 2000ms constant required
```

Also prove current real Device projected slider policy supplies 2000ms.

### B. Latest pending intent wins

```text
intent A
intent B
intent C
before delay
→ only C submitted
```

### C. Delay restarts after newest edit

Existing trailing-debounce behavior remains.

### D. Current pending payload remains queryable

Needed for refresh/invalidation overlay.

### E. Stale in-flight result cannot clear newer draft

Preserve current generation test.

### F. `CancelUnsubmitted` drops timer without waiting

No mutation submitted after cancel.

### G. Already-submitted mutation is not used as a hide blocker

Cancel-unsubmitted remains a no-op once submitted; operation may settle later.

### H. Dispose suppresses obsolete UI settlement

Preserve current teardown guarantee.

Remove the obsolete test whose only contract is:

```text
OverlayDelayedSliderCommit.ProductionDelay == 2000ms
```

Replace it with data-driven shared-policy tests.

---

## 47. Deterministic test plan — generic Device draft semantics

Add pure tests around the new page-local interaction/binder logic.

### A. Independent key identity

```text
CommitGroupId null
→ key by RowId
```

### B. Group key identity

```text
CommitGroupId present
→ all group members share one key
```

### C. Independent slider intent

One row value only; correct PageId/AppId/EditedRowId.

### D. Group first edit seeds section order

Use a synthetic section with:

```text
one Toggle value
four Slider values
```

Assert submitted `Values` preserve section order and include all values.

### E. Group later edit reuses pending whole draft

No reversion to stale authoritative values.

### F. Missing/null section value fails grouped scheduling closed

No partial mutation.

### G. Linked lower edit adjusts upper using metadata

No label/feature table.

### H. Linked upper edit adjusts lower using metadata

Test lower/upper bound fallback branches too.

### I. Linked companion pending preview updates immediately

Both effective row values reflect corrected group draft before settlement.

### J. No matching linked constraint

Only edited row changes.

---

## 48. Deterministic test plan — numeric/discrete renderer semantics

### Numeric

Prove page metadata drives:

```text
minimum
maximum
step
suffix
```

and the row's current/pending integer value is used.

### Discrete

Use non-contiguous values:

```text
10 -> Low
20 -> Balanced
40 -> High
```

Prove:

```text
product 20 -> slider index 1 -> label Balanced
user index 2 -> submitted product value 40
```

Unknown authoritative product value should fail that row closed rather than select an arbitrary option.

### Unsupported/malformed row shape

Prove zero mutation for:

```text
Toggle + non-Boolean value
Slider without SliderSpec
Numeric slider with invalid range/step
Discrete slider with empty/options mismatch
Unavailable row
Non-writable row
unsupported ControlKind/SliderKind enum if constructible in test
```

---

## 49. Deterministic test plan — authoritative refresh/settlement

### A. Pending survives ordinary page refresh

```text
authority says 50
pending draft says 65
new authority page still says 50
→ visible stays 65
```

### B. Unrelated rows adopt newest authority

While one slider is pending, update another row in incoming page and prove the unrelated row changes immediately.

### C. Current success settlement clears pending and applies result page

No submitted-draft patching.

### D. Current typed failure clears pending and applies result page

`FailureMessage` preserved/displayable; false preview does not remain.

### E. Transport/operation exception retires current pending and falls back to cached authoritative state

No fake product page.

### F. Older settlement cannot overwrite a newer current draft

Normal async regression.

---

## 50. Deterministic test plan — immediate Toggle interaction

### A. Immediate mutation uses generic client seam only

One `QuickSettingsMutationIntent`, one row value.

### B. Same-section pending drafts are canceled before toggle mutation

Use section identity, not feature names.

### C. Other-section pending draft remains intact

A CPU toggle must not cancel unrelated Power Mode pending work merely because both are Device rows.

### D. Typed failure applies authoritative result page

Same rule as sliders.

### E. Unavailable/non-writable toggle emits nothing

---

## 51. Deterministic test plan — selection and page rebuild

Prefer pure/composition tests; do not add a full UI automation framework solely for this PR.

Required behavior:

### A. Same selected RowId remains selected after ordinary rebuild

Even if its numeric index moved because another row appeared/disappeared before it.

### B. Selected row disappeared

Fallback uses existing `OverlayRowSelection` first-selectable behavior.

### C. Selected row became unavailable

Fallback normalizes; the same controller action must not mutate the fallback row under stale highlight.

### D. Device ordinary refresh does not execute the tab-change scroll-reset path

A focused source/composition assertion is acceptable if WinUI hosting would otherwise add disproportionate test infrastructure.

Do not add a second selection abstraction merely to make this test easier.

---

## 52. App/window wiring regression tests

Add focused source/composition tests where direct XAML hosting is impractical.

Prove at minimum:

```text
HandleQuickSettingsPageAsync no longer log-only
Device page is marshalled/applied on UI DispatcherQueue
mutation requests flow through SendQuickSettingsMutationAsync
OverlayWindow/binder does not own NamedPipeOverlayClient
preview fixture labels are absent from production Device construction
NavigationPreviewRowCount is gone from Device path
OverlayDelayedSliderCommit.ProductionDelay is gone
```

Do not make brittle tests assert arbitrary formatting/line layout.

---

## 53. Existing regression suites that must remain green

Run the full existing suite, including at minimum:

```text
OverlayDeviceQuickSettingsTransportTests
OverlayTransportTests
OverlayRowSelectionTests
OverlayToggleRowTests
OverlaySliderRowTests
OverlayDelayedSliderCommitTests
Overlay tab-order/editor/shortcut tests
OQ4 controller capture tests
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QamFrontendContractTests
QamFrontendBridgeTests
```

SF-V2-07 must not weaken SF-V2-06 transport tests merely because a real renderer now exists.

---

## 54. Manual MSI Claw validation — Device milestone sign-off

When real supported hardware/runtime access is available, validate the actual product path.

### Content parity

```text
Overlay Device section order == QAM Device section order
row labels match
CPU Boost option labels/order match
Power Mode option labels/order match
TDP numeric ranges reflect Runtime limits
```

### CPU Boost

- Toggle with controller Accept.
- Toggle with touch/pointer.
- AC/DC discrete sliders with controller Left/Right.
- AC/DC discrete sliders with touch/pointer.
- Authoritative result appears after commit.

### Power Mode

Same controller/touch coverage.

### TDP

- enable/disable;
- AC PL1/PL2;
- DC PL1/PL2;
- rapid edits produce latest whole-group commit only;
- linked PL correction is visible immediately before Runtime settlement;
- no Overlay-specific gap mismatch versus QAM.

### Shared delay

Current product page should produce approximately the current shared 2000ms trailing behavior.

The validation is of shared policy behavior, not an Overlay constant.

### Refresh / reopen

```text
edit in Overlay
→ QAM later shows same Runtime truth

edit in QAM
→ reopen/refresh Overlay
→ Overlay shows same Runtime truth
```

### Close safety

With a pending unsubmitted slider draft:

```text
Back / outside click / Runtime Hide
→ closes immediately
→ no 2-second capture delay
→ no later hidden unsubmitted mutation
```

With an already-submitted slow mutation:

```text
Hide still completes
→ game input resumes through normal OQ4 release path
```

### Controller modal safety

While Overlay capture is active:

```text
controller navigation changes Overlay only
game/Steam behind it receives neutral game-facing controller output
```

Do not expand SF-V2-07 scope if a hardware issue is actually in OQ4 controller capture/Full1902 authority; classify it against the owning subsystem.

---

## 55. Logging guidance

Keep logs diagnostic and bounded.

Useful events may include:

```text
Device shared page applied
Quick Settings immediate mutation failed
Quick Settings delayed mutation failed
malformed/unsupported row skipped
```

Do not log every slider preview step at Info level in production.

Do not log large full page JSON payloads on every invalidation.

Avoid duplicating Runtime feature logs that already record persistence/apply failure.

---

## 56. Overengineering guardrails

Do not add merely for theoretical concurrency:

```text
page epoch
page revision counter
global mutation manager
cross-surface lock
renderer registry
feature registry
ViewModel framework
MVVM conversion of the Overlay
state machine for every row
background polling worker
global scheduler service
barrier between StateInvalidated and row preview
```

The supported real lifecycle already has sufficient authorities:

```text
Runtime feature authority
Overlay v7 transport admission/correlation
OQ4 capture authority
page-local pending generation
existing row-selection model
```

Add synchronization only for a realistic normal path demonstrated by current I/O/lifecycle behavior.

Normal async stale completion protection in the delayed helper is required. Arbitrary instruction-boundary races are not.

---

## 57. Acceptance criteria

The implementation PR is acceptable when all of the following are true:

- [ ] Current implementation was based on then-current `main`, not stale planning source.
- [ ] Full1902 authority documents were read in current precedence order.
- [ ] Device preview fixture is removed from production Overlay Device page.
- [ ] Device renders from `QuickSettingsPageSnapshot(Device)` only.
- [ ] Section order comes from `page.Sections` order.
- [ ] Row order comes from each `section.Rows` order.
- [ ] Section/row labels come from shared payload.
- [ ] Toggle maps generically to `OverlayToggleRow`.
- [ ] Numeric slider maps generically to `OverlaySliderRow` using shared min/max/step/suffix.
- [ ] Discrete slider uses ordered option index while submitting the option's product value.
- [ ] Discrete renderer does not assume option value equals index.
- [ ] Unavailable/non-writable/malformed rows fail closed and do not mutate.
- [ ] Immediate Toggle creates one generic `QuickSettingsMutationIntent`.
- [ ] Immediate Toggle cancels unsubmitted pending work only in the same shared section.
- [ ] Slider debounce delay comes from `QuickSettingsCommitPolicy`.
- [ ] `OverlayDelayedSliderCommit.ProductionDelay` is no longer production policy.
- [ ] Independent slider pending identity is row-based.
- [ ] Grouped slider pending identity is `CommitGroupId`-based.
- [ ] First grouped edit seeds the containing section generically in row order.
- [ ] Current TDP grouped mutation carries Enabled + four numeric values without a hard-coded TDP row table in Overlay.
- [ ] Later grouped edits reuse the current whole pending draft.
- [ ] Linked constraints are applied from `page.LinkedSliderConstraints` only.
- [ ] No label parsing or known TDP limit tuple policy exists in production Overlay code.
- [ ] Linked companion preview updates immediately.
- [ ] Pending draft stays visible across ordinary authoritative page publication.
- [ ] Unrelated rows adopt newest authoritative page while another row/group is pending.
- [ ] Current success settlement clears pending and applies `result.Page`.
- [ ] Current typed failure clears pending, applies `result.Page`, and retains/display failure message.
- [ ] Transport/operation failure does not synthesize fake authoritative product state.
- [ ] Stale old completion cannot overwrite a newer pending draft.
- [ ] Device selection is registered through existing `OverlayRowSelection`.
- [ ] Selected Device RowId is preserved across ordinary rebuild when still selectable.
- [ ] Row disappearance/unavailability falls back through existing selection normalization.
- [ ] Ordinary Device refresh does not intentionally reset body scroll to top.
- [ ] Pointer/touch and controller edits share the same mutation path.
- [ ] Hide cancels unsubmitted Device drafts without waiting for debounce.
- [ ] Already-submitted mutation cannot hold Hide/OQ4 capture retirement open.
- [ ] Window/process teardown suppresses obsolete local settlement callbacks.
- [ ] `App` remains owner of `NamedPipeOverlayClient`.
- [ ] Window/binder receives only a narrow generic mutation delegate, not the transport client.
- [ ] Profile/Controller/Shortcut/Setting behavior is not migrated/redesigned in this PR.
- [ ] No polling is introduced.
- [ ] No new Runtime/hardware/controller authority is introduced.
- [ ] No Full1902 PID/HidHide/VIIPER/controller lifecycle code is changed.
- [ ] `FrontendTransportProtocol` remains current value (`28` at reviewed baseline).
- [ ] `OverlayTransportProtocol` remains current value (`7` at reviewed baseline).
- [ ] Existing SF-V2-06 transport/non-blocking tests remain green.
- [ ] Existing OQ4/OQ5 navigation/capture tests remain green.
- [ ] QAM shared Device renderer tests remain green.
- [ ] Debug build succeeds with zero new warnings.
- [ ] Release build succeeds with zero new warnings.
- [ ] Full Release test suite passes.
- [ ] `git diff --check` is clean.
- [ ] Final diff contains no unrelated UI polish/refactor/controller changes.

Hardware validation should be completed before declaring the Device shared-frontend milestone hardware-proven, but CI should remain deterministic and must not depend on physical MSI hardware.

---

## 58. PR review focus

Review SF-V2-07 primarily for realistic product failures:

```text
wrong row/value submitted
TDP group missing/stale member
linked correction differs from shared metadata
discrete value/index mismatch
pending draft snaps back on normal invalidation
old settlement overwrites newer draft
parent toggle leaves stale child timer alive
failure leaves false committed preview
ordinary refresh resets selection/scroll destructively
Hide waits on debounce/mutation
Overlay gains feature/controller authority
transport/client ownership leaks into Window/binder
```

Do not block the PR for hypothetical instruction-level races without a plausible supported lifecycle path.

Prefer the smallest implementation that satisfies the actual Device product contract and existing OQ4 lifecycle.

---

## 59. Completion state

After SF-V2-07:

```text
Device shared product contract          COMPLETE
QAM Device generic renderer             COMPLETE
Overlay generic v7 transport            COMPLETE
Overlay Device generic renderer/binding COMPLETE
Device QAM ↔ Overlay parity milestone   COMPLETE
```

Then proceed to:

```text
SF-V2-08
→ shared Profile projection/dispatch
→ QAM Profile generic migration

SF-V2-09
→ Overlay Profile publication/binding
→ Profile parity milestone
```

Do not start Profile work inside SF-V2-07.
