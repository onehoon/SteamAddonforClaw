# Work Order — Full1902 M1/M2 Xbox360 Mapping PR4: Controller Page UI

## Status

Implementation work order for PR4 of the global M1/M2 back-button mapping feature.

Baseline reviewed against current `main`:

```text
2baed95f028956280dd2ec3d41d0e3d01572e558
feat: rearm M1 and M2 after presentation switches (#556)
```

Feature sequence:

```text
PR1  Contract + persistence + frontend transport foundation      [merged #554]
 ↓
PR2  Xbox360 M1/M2 software mapping                              [merged #555]
 ↓
PR3  Xbox360 ↔ SteamDeck held-button release-to-rearm safety     [merged #556]
 ↓
PR4  Controller-page UI                                          [this PR]
```

PR4 is **UI-only over the already-complete Runtime/frontend contract**.

Do not reopen controller output, presentation switching, HidHide, PID1902, persistence schema, or frontend transport design.

---

## 1. Read Before Implementation

Read these current authorities before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md

docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR1_CONTRACT_PERSISTENCE_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR2_LIVE_OUTPUT_WORK_ORDER.md
docs/work-order/FULL1902_M1_M2_MAPPING_PR3_PRESENTATION_RELEASE_TO_REARM_WORK_ORDER.md
```

Current product rule:

```text
Center M Disabled
→ Addon owns PID1902 / DirectInput / HidHide / VIIPER

Steam/BPM inactive
→ Xbox360 virtual presentation
→ saved M1/M2 Xbox360 mapping applies

Steam game or BPM active
→ SteamDeck virtual presentation
→ M1 remains R4
→ M2 remains L4
```

The Main UI must edit that existing global Xbox360 mapping and nothing else.

---

## 2. Current Code State Verified

### 2.1 Back-button contract already exists

Current contract:

```text
src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs
```

contains:

```text
BackButtonMappingSettings
├ M1
└ M2

Xbox360BackButtonTarget
├ Disabled
├ A / B / X / Y
├ DPad Up / Right / Down / Left
├ Left / Right Bumper
├ Left / Right Trigger
├ Left / Right Stick Click
├ View
├ Menu
└ XboxGuide
```

Defaults:

```text
M1 = Disabled
M2 = Disabled
```

M1 and M2 may intentionally use the same target.

Do not add a same-target exclusion rule.

### 2.2 Frontend contract is already complete

PR1 already provides:

```text
FrontendSettingsSnapshot.BackButtonMapping
FrontendBootstrapSnapshot.BackButtonMappingAvailable
IAddonFrontendControl.SetBackButtonMappingAsync(...)
FrontendRpcMethod.SetBackButtonMapping
NamedPipe client/server transport
FrontendTransportProtocol v38
```

PR4 must consume these existing members.

No protocol change is required.

### 2.3 Runtime output is already live

PR2 already makes a successful setting mutation live on subsequent Xbox360 reports without:

- publisher restart;
- detach/attach;
- PID change;
- DirectInput reacquire.

PR3 already handles held M1/M2 across Xbox360 ↔ SteamDeck kind changes.

The UI must not reproduce either behavior.

### 2.4 Current Controller page already owns button mapping

Current:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
```

already render:

```text
Controller

Button Mapping

[Gamebar Button]
  Normal
  Steam Game / Big Picture

[Center M Button]
  Normal
  Steam Game / Big Picture
```

This is the correct page and section for M1/M2.

Do not create a new navigation page.

### 2.5 MainWindow already owns ordered front-button persistence

Current `MainWindow` keeps:

```text
_frontButtonUiMapping
_frontButtonPersistedMapping
_frontButtonSaveChain
_frontButtonEditVersion
QueueFrontButtonMutation(...)
SaveFrontButtonAfterAsync(...)
```

This prevents rapid UI edits from racing separate async saves and lets the newest failed save roll the visible UI back to the last persisted value.

PR4 should use the same simple ownership pattern for the independent `BackButtonMappingSettings` record.

Do not create a generic settings mutation framework just to share a few lines.

---

## 3. Goal

Add one clear M1/M2 editor to the existing Controller page.

Target user-facing structure:

```text
Controller

Button Mapping

[Gamebar Button]
...

[Center M Button]
...

[M1 / M2]
Xbox 360 mode only.
Steam Game / Big Picture keeps M1 as R4 and M2 as L4.

M1   [ Disabled ▼ ]
M2   [ Disabled ▼ ]
```

The exact control styling should follow the current CommunityToolkit WinUI `SettingsExpander` / `SettingsCard` visual language already used on this page.

Do not introduce a custom layout system.

---

## 4. Placement and Page Ownership

Place M1/M2 after the existing two front-button expanders inside the current:

```text
Button Mapping
```

section.

Required order:

```text
Gamebar Button
Center M Button
M1 / M2
```

Reason:

- Gamebar / Center M are the two front buttons;
- M1/M2 are the two rear buttons;
- all four are controller-button behavior;
- Controller is already the product-design authority for these controls.

Do not move:

- Center M controller-authority control from Device;
- Device settings into Controller;
- M1/M2 into Profile;
- M1/M2 into Settings.

No per-game M1/M2 UI in this PR.

---

## 5. Recommended XAML Shape

Use one additional `SettingsExpander`.

Preferred structure:

```xml
<ctcontrols:SettingsExpander
    x:Name="BackButtonMappingExpander"
    Header="M1 / M2"
    Description="Xbox 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4."
    IsExpanded="True">

    <ctcontrols:SettingsExpander.Items>
        <ctcontrols:SettingsCard Header="M1">
            <ComboBox
                x:Name="M1TargetComboBox"
                MinWidth="220"
                SelectionChanged="BackButtonTargetComboBox_SelectionChanged" />
        </ctcontrols:SettingsCard>

        <ctcontrols:SettingsCard Header="M2">
            <ComboBox
                x:Name="M2TargetComboBox"
                MinWidth="220"
                SelectionChanged="BackButtonTargetComboBox_SelectionChanged" />
        </ctcontrols:SettingsCard>
    </ctcontrols:SettingsExpander.Items>
</ctcontrols:SettingsExpander>
```

Equivalent naming is fine.

Keep both ComboBoxes visually aligned with the existing action ComboBoxes.

Do not add:

- toggle switches;
- per-button enable switches;
- nested Normal/Steam domain cards;
- Apply/Save buttons;
- a second page;
- a firmware mapping button.

`Disabled` is already the explicit X360 mapping-off value.

---

## 6. User-Facing Wording

The UI must make the presentation-specific behavior clear without exposing internal lifecycle terminology.

Recommended expander description:

```text
Xbox 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4.
```

This prevents a user from assuming that:

```text
M1 = A
```

also replaces SteamDeck R4.

Do not use misleading wording such as:

```text
Global M1 action
Always map M1 to A
Remap physical M1
Firmware mapping
```

The setting is global in persistence scope but **Xbox360-only in output scope**.

---

## 7. Target List and Labels

Every currently defined `Xbox360BackButtonTarget` value must be selectable.

Use this user-facing order:

```text
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
```

The enum remains the authority for stored values.

Do not persist UI strings.

A simple page-local description helper is sufficient, for example:

```csharp
private static string DescribeBackButtonTarget(Xbox360BackButtonTarget target) => target switch
{
    Xbox360BackButtonTarget.Disabled => "Disabled",
    Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
    Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
    ...
};
```

Populate the ComboBoxes from the defined enum values and put the enum value in `ComboBoxItem.Tag`.

Do not create:

- another target enum;
- a generic controller action catalog;
- localization infrastructure solely for this PR;
- a target manager/service.

---

## 8. Do Not Copy the Front-Button Uniqueness Rule

Current Gamebar/Center M mapping prevents the two front buttons from selecting the same action within one domain.

That rule does **not** apply here.

Valid:

```text
M1 = A
M2 = A
```

Therefore:

- do not disable M2=A when M1=A;
- do not add partner availability logic;
- do not reuse `RefreshPartnerAvailability()` for M1/M2.

The Runtime intentionally combines duplicate rear targets with additive/OR semantics.

---

## 9. ControllerPage State

Add one page-local current back-button mapping:

```csharp
private BackButtonMappingSettings _backButtonMapping =
    BackButtonMappingSettings.Default;
```

Add one edit event:

```csharp
internal event EventHandler<BackButtonMappingSettings>?
    BackButtonMappingEditRequested;
```

Keep persistence out of the page.

The page should only:

1. render authoritative values;
2. capture a valid user selection;
3. emit the complete desired `BackButtonMappingSettings`.

MainWindow owns persistence ordering.

---

## 10. Bootstrap Initialization

Current `ControllerPage.Initialize(...)` already receives the complete `FrontendBootstrapSnapshot`.

Use:

```text
bootstrap.BackButtonMappingAvailable
bootstrap.Settings.BackButtonMapping
```

No new capture RPC is needed.

At initialization:

```text
BackButtonMappingAvailable = true
→ populate target ComboBoxes
→ show M1/M2 expander
→ ApplyBackButtonMapping(bootstrap.Settings.BackButtonMapping)

BackButtonMappingAvailable = false
→ do not offer editable M1/M2 controls
→ preserve the saved mapping in Runtime/persistence
```

Do not erase a saved mapping merely because the current machine reports the feature unavailable.

---

## 11. Keep Front and Back Availability Independent in the UI

The current Runtime supplies `FrontButtonMappingAvailable` and `BackButtonMappingAvailable` from the same supported-hardware startup fact, but they are separate frontend contract members.

Do not wire the M1/M2 visibility to `FrontButtonMappingAvailable`.

Use:

```text
BackButtonMappingAvailable
```

for M1/M2.

The Controller page should remain structurally correct if a test/passive frontend supplies different values.

A simple policy is enough:

```text
front available
→ show front-button mapping controls

back available
→ show M1/M2 controls

neither available
→ show the existing overall Unavailable state
```

If only one category is available, show the available category rather than hiding the entire Button Mapping section.

Do not add another hardware probe.

---

## 12. ApplyBackButtonMapping

Add a dedicated render method:

```csharp
internal void ApplyBackButtonMapping(BackButtonMappingSettings mapping)
```

It should:

- set the page-local mapping;
- select the matching M1 ComboBox item;
- select the matching M2 ComboBox item;
- run under the existing UI-loading suppression so programmatic selection does not emit a new user edit.

The existing `_isLoading` flag may be reused.

Do not add another synchronization primitive.

The page runs on the UI thread.

---

## 13. Capture User Edits as One Whole Record

When M1 changes:

```text
new BackButtonMappingSettings(
    selected M1,
    current M2)
```

When M2 changes:

```text
new BackButtonMappingSettings(
    current M1,
    selected M2)
```

Then emit:

```text
BackButtonMappingEditRequested
```

Do not call `SetBackButtonMappingAsync` directly from `ControllerPage`.

Do not add:

```text
SetM1Async
SetM2Async
```

The pair remains one atomic settings record exactly as PR1 designed.

---

## 14. MainWindow Owns One Ordered Back-Button Save Chain

Add the smallest parallel state to the existing front-button chain:

```csharp
private BackButtonMappingSettings _backButtonUiMapping =
    BackButtonMappingSettings.Default;

private BackButtonMappingSettings _backButtonPersistedMapping =
    BackButtonMappingSettings.Default;

private Task _backButtonSaveChain = Task.CompletedTask;
private long _backButtonEditVersion;
```

Initialize both values from:

```text
bootstrap.Settings.BackButtonMapping
```

Subscribe:

```csharp
ControllerContent.BackButtonMappingEditRequested +=
    (_, mapping) => QueueBackButtonMutation(mapping);
```

Do not merge the front/back save chains into one generic mutation scheduler.

They are independent persisted records and do not need cross-record atomicity.

---

## 15. QueueBackButtonMutation

Follow the proven front-button pattern.

Conceptually:

```csharp
private void QueueBackButtonMutation(BackButtonMappingSettings next)
{
    _backButtonUiMapping = next;
    var version = ++_backButtonEditVersion;

    ControllerContent.ApplyBackButtonMapping(next);

    _backButtonSaveChain =
        SaveBackButtonAfterAsync(_backButtonSaveChain, next, version);
}
```

Why serialize saves:

A user can quickly change:

```text
M1: Disabled → A
M2: Disabled → RB
M1: A → B
```

The final persisted record must be the latest complete visible record, not whichever asynchronous RPC happens to complete last.

Do not disable the whole editor until each save returns merely to avoid ordering.

The current UI already has a proven ordered whole-record pattern.

---

## 16. Save Success and Rollback

Use the existing frontend method:

```text
_frontend.SetBackButtonMappingAsync(next)
```

On success:

```text
returned FrontendSettingsSnapshot.BackButtonMapping
→ authoritative persisted value
→ update _backButtonPersistedMapping
```

Only the newest edit version may re-render controls.

On exception:

```text
if this is still the newest edit
→ restore _backButtonPersistedMapping
→ ApplyBackButtonMapping(...)
```

This mirrors current front-button behavior.

Do not invent a toast/error dialog unless the existing Controller page already has a standard mutation failure surface.

Log the transport/save failure once through existing `AppLog` conventions.

No per-selection success log is needed.

---

## 17. Runtime Validation Readback

The UI only exposes defined enum values, so normal user edits are valid.

Still, the Runtime remains validation authority.

If `SetBackButtonMappingAsync(candidate)` returns an unchanged mapping because Runtime rejected the candidate:

```text
returned BackButtonMapping
→ render that returned value
```

Do not assume the requested value persisted merely because the RPC returned successfully.

The returned settings snapshot is authoritative.

---

## 18. Live Behavior After UI Save

No extra action is required after a successful UI save.

Current PR2 path already provides:

```text
SetBackButtonMappingAsync
→ StartupSettingsCoordinator.BackButtonMapping changes
→ current Xbox360 publisher reads current mapping on a later report
```

Therefore PR4 must not:

- restart the publisher;
- detach/attach Xbox360;
- force neutral;
- call presentation reconcile;
- touch PID1902;
- reacquire DirectInput.

The saved mapping becomes live through the existing provider.

---

## 19. SteamDeck Behavior Is Informational Only in This UI

When SteamDeck presentation is active:

```text
M1 → R4
M2 → L4
```

regardless of the saved X360 mapping.

The Main UI must not disable the M1/M2 editor merely because SteamDeck is currently active.

The user is editing a global saved Xbox360 preference.

No current-presentation observer is needed on the page.

Do not add:

- ActivePresentation frontend property;
- Steam/BPM listener;
- live mode label;
- mapping-domain switch.

The descriptive text is sufficient.

---

## 20. PR3 Release-to-Rearm Remains Completely Outside UI

Do not surface or persist:

```text
suppressM1UntilRelease
suppressM2UntilRelease
rear-button armed state
```

These are transient lifecycle facts inside `MsiClawAddonPresentation`.

Changing M1 mapping while Xbox360 is active remains immediate PR2 behavior.

PR4 must not require M1/M2 release after a settings change.

Only actual Xbox360 ↔ SteamDeck kind switches use PR3 release-to-rearm.

---

## 21. No Frontend Contract or Protocol Change

Do not modify:

```text
FrontendSettingsSnapshot schema
FrontendBootstrapSnapshot schema
IAddonFrontendControl method set
FrontendRpcMethod
SetBackButtonMappingRequest
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
FrontendTransportProtocol.CurrentVersion
```

Expected protocol after PR4:

```text
v38
```

If implementation seems to require v39, stop and re-check the design: PR1 intentionally completed the transport foundation before the UI PR.

---

## 22. No Shared QAM / Overlay Scope in This PR

This PR is the **Main WinUI Controller-page editor** from the M1/M2 feature sequence.

Do not also add M1/M2 editing to:

- Steam native QAM;
- Addon Overlay;
- shared Quick Settings Controller projection;
- Shortcut page;
- Profile page.

Those shared-surface product decisions are separate tracks.

Do not expand PR4 solely for UI parity.

The Runtime/frontend contract introduced in PR1 remains reusable later if a Controller Quick Settings product is explicitly designed.

---

## 23. No Firmware/Profile Scope

Do not touch:

```text
MSI firmware M1/M2 profile slots
WriteProfile
SyncToROM
ProfileStore
GameProfile
GameProfileMutations
```

This UI edits only:

```text
settings.json
→ BackButtonMappingSettings
```

through the existing Runtime-owned frontend RPC.

---

## 24. No Controller Lifecycle Changes

Do not modify production logic in:

```text
MsiClawControllerStateMapper
MsiClawInputSource
CanonicalXbox360InputPublisher
Xbox360DeviceStateMapper
CanonicalSteamDeckInputPublisher
SteamDeckDeviceStateMapper
MsiClawAddonPresentation
AddonProcessHost controller ownership/reconcile
HidHide
VIIPER attach/detach
Suspend/Resume
PnP recovery
rumble
```

PR1–PR3 already completed those layers.

PR4 must be reviewable as a UI consumption PR.

---

## 25. Expected Production Touch Set

Expected production files:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
```

Potential test files:

```text
tests/SteamInputAddonforClaw.Tests/BackButtonUiLayoutTests.cs
tests/SteamInputAddonforClaw.Tests/FrontButtonUiLayoutTests.cs
tests/SteamInputAddonforClaw.Tests/UiArchitectureTests.cs
```

Prefer a focused new `BackButtonUiLayoutTests.cs` rather than making unrelated existing test files harder to read.

No Runtime production file should need modification.

---

## 26. Required UI Tests

### 26.1 Placement

Prove Controller XAML contains:

```text
Button Mapping
Gamebar Button
Center M Button
M1 / M2
M1
M2
```

and that order is:

```text
Gamebar < Center M < M1/M2
```

### 26.2 Exact two rear editors

Prove there is one:

```text
M1TargetComboBox
M2TargetComboBox
```

and no per-domain M1/M2 duplicate controls.

### 26.3 Xbox360-only explanation

Prove the UI contains wording that communicates:

- mapping is for Xbox360 mode;
- Steam Game / Big Picture keeps M1→R4 and M2→L4.

Avoid brittle punctuation checks; pin the important product terms.

### 26.4 Availability authority

Prove Controller code uses:

```text
bootstrap.BackButtonMappingAvailable
```

for the M1/M2 surface.

Do not accept `FrontButtonMappingAvailable` as the back-button gate.

### 26.5 Existing front mapping remains

All current Gamebar/Center M UI tests must continue passing.

No front-button vocabulary or behavior regression is acceptable.

---

## 27. Required Target-List Tests

Prove every defined `Xbox360BackButtonTarget` is offered once.

At minimum verify the UI mapping code covers:

```text
Disabled
A/B/X/Y
DPadUp/Right/Down/Left
Left/RightBumper
Left/RightTrigger
Left/RightStickClick
View
Menu
XboxGuide
```

Also prove user-facing labels are readable:

```text
D-Pad Up
Left Bumper (LB)
Left Trigger (LT)
Left Stick Click (L3)
Xbox Guide
```

Do not expose raw enum labels such as:

```text
DPadUp
LeftStickClick
```

as the visible text.

---

## 28. Required Whole-Record Edit Tests

Prove the page's edit model preserves the partner value.

Examples:

```text
current M1=A, M2=RB
user changes M1→B
→ emitted mapping = M1=B, M2=RB

current M1=B, M2=RB
user changes M2→A
→ emitted mapping = M1=B, M2=A
```

If direct WinUI interaction tests are impractical in the current test harness, pin the implementation shape with the same focused source-level tests already used by `FrontButtonUiLayoutTests`.

Do not introduce a new UI test framework solely for this PR.

---

## 29. Duplicate Target Test

Explicitly prove the UI does not disable partner values.

This must remain selectable:

```text
M1 = A
M2 = A
```

The back-button editor must not call the front-button `DisablePartnerAction` logic.

This is a product contract, not merely a missing validation rule.

---

## 30. MainWindow Save-Chain Tests

Pin that MainWindow owns:

```text
QueueBackButtonMutation
_backButtonSaveChain
SetBackButtonMappingAsync
ControllerContent.ApplyBackButtonMapping
```

and that `ControllerPage` itself does not persist directly.

Where practical, add a behavior-focused test for:

```text
edit A
edit B before A save finishes
→ B remains visible/latest
→ final persisted/readback render is B
```

If the current WinUI/MainWindow harness makes that disproportionately complex, use focused structural assertions consistent with existing front-button UI tests rather than adding test-only production abstractions.

---

## 31. Rollback Test

Pin the required failure behavior:

```text
last persisted mapping = M1=A, M2=RB
user edits M1=B
latest save throws
→ UI restores M1=A, M2=RB
```

Older failed saves must not roll back a newer edit.

This is why the edit-version check is required.

Do not add retries for settings persistence in the UI.

---

## 32. Protocol Regression Test

Existing protocol tests must continue proving:

```text
FrontendTransportProtocol.CurrentVersion == 38
SetBackButtonMapping exists
BackButtonMapping round-trips
BackButtonMappingAvailable round-trips
```

PR4 should not alter those tests except where a UI test references the existing contract.

No v39 bump.

---

## 33. Hardware / Manual Validation

On a supported MSI Claw:

### 33.1 Default state

Open:

```text
Controller → Button Mapping → M1 / M2
```

Expected:

```text
M1 = Disabled
M2 = Disabled
```

for a fresh settings file.

### 33.2 Xbox360 mapping

Set:

```text
M1 = A
M2 = Right Bumper (RB)
```

With Steam/BPM inactive / Xbox360 presentation:

```text
M1 → virtual A
M2 → virtual RB
```

No app restart.

### 33.3 Live edit

While Xbox360 is active and M1 is held or repeatedly pressed:

```text
M1 A → B
```

must become effective through the existing PR2 live provider without virtual-controller recreation.

Do not add UI-side release-to-rearm.

### 33.4 SteamDeck isolation

Enter Steam Game / Big Picture:

```text
M1 → R4
M2 → L4
```

even though UI still shows the saved X360 targets.

Return to Xbox360:

```text
M1 → saved A/B/etc.
M2 → saved target
```

### 33.5 Duplicate mapping

Set:

```text
M1 = A
M2 = A
```

Both must remain selectable and either rear button must assert A in Xbox360 mode.

### 33.6 Persistence

Close/reopen the Main UI and, when practical, restart the Runtime.

The selected values must reload exactly.

---

## 34. Logging

Do not log every ComboBox selection as an Info event.

Existing settings/frontend persistence logs and failure logs are enough.

One warning on an actual frontend mutation exception is appropriate, matching the existing front-button save path.

No new controller-output logs.

---

## 35. Anti-Overengineering Constraints

Do not add:

```text
BackButtonViewModel framework
ControllerMappingViewModel hierarchy
generic MappingEditor
generic SettingsMutationQueue
BackButtonManager
BackButtonRuntime
target registry service
action plug-in model
new frontend DTO
new RPC
new protocol version
new hardware capability probe
new current-presentation observer
new settings file
new profile model
```

The intended implementation is only:

```text
existing bootstrap.BackButtonMapping
        ↓
ControllerPage M1/M2 ComboBoxes
        ↓
whole-record BackButtonMappingEditRequested
        ↓
MainWindow ordered save chain
        ↓
existing SetBackButtonMappingAsync
        ↓
existing Runtime settings owner
```

---

## 36. Non-Goals

Explicitly out of scope:

- QAM M1/M2 editor;
- Addon Overlay M1/M2 editor;
- per-game M1/M2 mappings;
- keyboard mapping;
- mouse mapping;
- macros;
- stick-direction targets;
- MSI firmware/EEPROM remap;
- M1/M2 event diagnostics;
- changing DirectInput indices;
- changing Xbox360 output logic;
- changing SteamDeck R4/L4 logic;
- changing release-to-rearm behavior;
- changing presentation policy;
- changing HidHide;
- changing PID1901/PID1902;
- changing VIIPER ownership;
- changing rumble;
- Controller LED;
- vibration-strength UI.

LED/vibration remain separate future Controller Settings work.

---

## 37. Acceptance Criteria

PR4 is complete only when all of the following are true.

### Layout

- [ ] M1/M2 editor is on the existing Controller page.
- [ ] It is inside the existing Button Mapping section.
- [ ] Order is Gamebar Button → Center M Button → M1/M2.
- [ ] Exactly one M1 and one M2 ComboBox exist.
- [ ] No new navigation page exists.

### Product wording

- [ ] UI clearly states the mapping is Xbox360-only.
- [ ] UI states Steam Game / Big Picture keeps M1→R4 and M2→L4.
- [ ] User-facing target labels are readable and not raw enum identifiers.

### Target behavior

- [ ] Every current `Xbox360BackButtonTarget` value is selectable.
- [ ] Disabled is selectable.
- [ ] M1 and M2 may select the same target.
- [ ] No Normal/Steam domain selector is added for M1/M2.
- [ ] No per-button enable toggle is added.

### Availability

- [ ] M1/M2 UI uses `BackButtonMappingAvailable`.
- [ ] Saved mapping is not erased when unavailable.
- [ ] Front-button availability remains independently respected.

### State/persistence

- [ ] Bootstrap renders `Settings.BackButtonMapping`.
- [ ] ControllerPage emits whole `BackButtonMappingSettings`.
- [ ] MainWindow owns one ordered back-button save chain.
- [ ] The existing `SetBackButtonMappingAsync` RPC is used.
- [ ] Runtime returned mapping is treated as authoritative.
- [ ] Latest save failure rolls UI back to last persisted mapping.
- [ ] Older save completion/failure cannot overwrite a newer edit.

### Contract preservation

- [ ] Frontend protocol stays v38.
- [ ] No new frontend DTO/RPC is added.
- [ ] PR1 persistence schema is unchanged.
- [ ] PR2 live output path is unchanged.
- [ ] PR3 release-to-rearm path is unchanged.
- [ ] No Runtime controller lifecycle production file is modified.

### Regression

- [ ] Existing front-button Controller UI remains functional.
- [ ] Existing Gamebar/Center M uniqueness logic remains front-button-only.
- [ ] Existing UI architecture tests pass.
- [ ] Existing frontend transport tests pass.
- [ ] Full test suite passes.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Hardware smoke test passes when practical.

---

## 38. Expected End State

```text
Main UI
Controller
  ↓
Button Mapping
  ├ Gamebar Button
  ├ Center M Button
  └ M1 / M2
       ├ M1 [Xbox360 target]
       └ M2 [Xbox360 target]
              ↓
       BackButtonMappingSettings
              ↓
       MainWindow ordered save chain
              ↓
       IAddonFrontendControl.SetBackButtonMappingAsync
              ↓
       StartupSettingsCoordinator.BackButtonMapping
              ↓
       existing PR2 live Xbox360 publisher provider
```

SteamDeck stays independent:

```text
saved X360 M1/M2 mapping
        X
        |
SteamDeck M1→R4 / M2→L4
```

No new lifecycle authority is introduced.

After PR4, the original M1/M2 feature sequence is complete: contract, persistence, live Xbox360 projection, presentation-switch safety, and Main UI editing all share the same single settings/runtime authority.
