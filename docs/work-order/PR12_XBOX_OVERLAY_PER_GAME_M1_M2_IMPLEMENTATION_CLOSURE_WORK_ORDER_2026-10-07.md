# Work Order — PR12: XBOX Overlay Per-Game M1/M2 + X5 Implementation Closure

> **Date:** 2026-10-07  
> **Repository:** \`onehoon/SteamAddonforClaw\`  
> **Reviewed main:** \`main@48130af09f37723b5d23e01eedef72bc920aa15c\`  
> **Production-code baseline:** PR #700 squash merge \`11b226620a4558d496128748a93a227323de50a7\`  
> **Architecture authority:** \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md\`  
> **Full1902 authority:** \`docs/Full 1902 Implementation/README.md\` and its active precedence chain  
> **Current frontend protocol:** \`FrontendTransportProtocol.CurrentVersion = 60\`  
> **Current Overlay protocol:** \`OverlayTransportProtocol.CurrentVersion = 16\`  
> **Scope:** expose the already-implemented XBOX per-game M1/M2 profile override through the active-game Overlay Profile Quick Settings surface, above the performance/display sections, using the existing XBOX profile snapshot, existing typed \`SetXboxGameProfileBackButtonMappingAsync\` mutation authority, existing host effective-mapping cache, and existing \`CanonicalXbox360InputPublisher\`  
> **Acceptance:** code + automated tests only  
> **Explicitly out of scope for this PR:** physical-device lifecycle validation, launch/exit validation, Alt+Tab validation, Runtime-restart validation, Sleep/Hibernate/Resume validation. The user will perform those later in a separate validation session.

---

## 1. Goal

Complete the XBOX Overlay profile feature without adding any new controller-output architecture.

Current production path is already complete below the frontend:

~~~text
XboxGameSessionRuntime.ActiveGame
        ↓
ActiveProfileTarget.Xbox
        ↓
ProfileDocument.XboxGames
        ↓
FrontendXboxGameProfileSnapshot.BackButtonMapping
        ↓
SetXboxGameProfileBackButtonMappingAsync(...)
        ↓
host cached effective non-Steam override
        ↓
existing CanonicalXbox360InputPublisher
~~~

PR12 only exposes that existing per-game mapping in the active XBOX Overlay Profile.

Required XBOX Overlay order:

~~~text
<game title>
Profile

Controller
  Use global M1 / M2 mapping
  M1
  M2

TDP Control
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution
~~~

Steam active Profile remains unchanged and does not show the Controller section.

---

## 2. Locked ownership rules

### 2.1 No new controller owner

Do not add:

- another Xbox360 publisher;
- another M1/M2 runtime;
- another effective-mapping cache;
- another profile store;
- Overlay-specific controller persistence;
- Overlay-specific hardware apply.

The one mutation authority remains:

~~~text
IAddonFrontendControl.SetXboxGameProfileBackButtonMappingAsync
→ XboxGameProfileMutations
→ existing effective mapping reconciliation
~~~

The one output path remains:

~~~text
CanonicalXbox360InputPublisher
→ Xbox360DeviceStateMapper
→ existing VIIPER Xbox360 device
~~~

### 2.2 Steam remains excluded

Steam Profile Quick Settings must not gain:

- Use global M1/M2;
- M1;
- M2;
- XBOX controller mapping mutation dispatch.

Steam Input remains the Steam per-game controller-mapping authority.

### 2.3 Global Overlay Controller tab remains global

The existing Overlay Controller tab M1/M2 controls keep their current meaning:

~~~text
Overlay Controller tab
→ global settings.json BackButtonMapping
→ fallback mapping
~~~

PR12 must not route those existing controls through XBOX profile persistence.

The new rows exist only in:

~~~text
Overlay Profile
+ active target kind == Xbox
~~~

---

## 3. Current code facts

Reviewed the latest production code after PR #700.

### 3.1 XBOX snapshot already contains everything PR12 needs

Current contract:

~~~csharp
public sealed record FrontendXboxGameProfileSnapshot(...)
{
    public FrontendGameBackButtonMappingConfiguration? BackButtonMapping { get; init; }
}

public sealed record FrontendGameBackButtonMappingConfiguration(
    bool UseGlobalMapping,
    BackButtonMappingSettings Mapping);
~~~

Current capture behavior already normalizes:

~~~text
stored explicit mapping
→ UseGlobalMapping = false
→ Mapping = stored mapping

no explicit mapping
→ UseGlobalMapping = true
→ Mapping = current global fallback
~~~

Do not add another XBOX controller DTO.

### 3.2 Existing typed mutation already has correct persistence/apply semantics

Current:

~~~csharp
SetXboxGameProfileBackButtonMappingAsync(
    string key,
    BackButtonMappingSettings? mapping,
    ...)
~~~

Meaning:

~~~text
mapping == null
→ use global fallback

mapping != null
→ explicit per-game mapping
~~~

It already performs the required active-XBOX mapping reconciliation.

Reuse it exactly.

### 3.3 Generic Overlay renderer already supports the needed controls

The shared Quick Settings renderer already supports:

- Toggle;
- discrete Slider/value row;
- section ordering;
- row writability;
- immediate commits;
- trailing debounce;
- grouped whole-section drafts.

Do not create a new XBOX-specific Overlay control tree.

### 3.4 Existing grouped Quick Settings draft is the correct whole-record save mechanism

\`OverlayQuickSettingsPageBinding\` already supports a \`CommitGroupId\`.

For grouped sliders it seeds the complete containing section, keeps one pending draft, and submits the whole group after the trailing delay.

Use that existing mechanism for M1/M2.

Do not add an XBOX-specific save chain inside Overlay.

---

## 4. Extend the Quick Settings closed contract

Add one XBOX Profile-only section:

~~~csharp
QuickSettingsSectionId.ProfileController
~~~

Add three rows:

~~~csharp
QuickSettingsRowId.ProfileBackButtonUseGlobal
QuickSettingsRowId.ProfileBackButtonM1
QuickSettingsRowId.ProfileBackButtonM2
~~~

Add one commit group:

~~~csharp
QuickSettingsCommitGroupId.ProfileBackButtonMapping
~~~

Do not add a new page or new control kind.

---

## 5. Protocol versions

The new Quick Settings section/row/commit-group enum values cross both existing frontend transports as JSON string enums.

Bump:

~~~text
FrontendTransportProtocol
60 → 61

OverlayTransportProtocol
16 → 17
~~~

Reason:

- a v60/v16 peer does not know the new closed enum members;
- fail handshake up front rather than allowing later enum-deserialization failure.

No compatibility shim is required.

Do not add any new RPC or Overlay wire message kind.

---

## 6. Centralize M1/M2 display labels once

There are currently two separate label maps:

- Main UI \`BackButtonMappingUiOptions.Describe\`;
- Overlay Controller \`OverlayWindow.FormatBackButtonTarget\`.

PR12 needs the same labels again inside \`QuickSettingsPresentation\`.

Do not create a third duplicate switch.

Add one tiny shared label helper in the existing BackButtons contract domain, for example:

~~~csharp
public static class BackButtonMappingLabels
{
    public static string GetDisplayName(Xbox360BackButtonTarget target) => target switch
    {
        Xbox360BackButtonTarget.Disabled => "Disabled",
        Xbox360BackButtonTarget.A => "A",
        Xbox360BackButtonTarget.B => "B",
        Xbox360BackButtonTarget.X => "X",
        Xbox360BackButtonTarget.Y => "Y",
        Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
        Xbox360BackButtonTarget.DPadRight => "D-Pad Right",
        Xbox360BackButtonTarget.DPadDown => "D-Pad Down",
        Xbox360BackButtonTarget.DPadLeft => "D-Pad Left",
        Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
        Xbox360BackButtonTarget.RightBumper => "Right Bumper (RB)",
        Xbox360BackButtonTarget.LeftTrigger => "Left Trigger (LT)",
        Xbox360BackButtonTarget.RightTrigger => "Right Trigger (RT)",
        Xbox360BackButtonTarget.LeftStickClick => "Left Stick Click (L3)",
        Xbox360BackButtonTarget.RightStickClick => "Right Stick Click (R3)",
        Xbox360BackButtonTarget.View => "View",
        Xbox360BackButtonTarget.Menu => "Menu",
        Xbox360BackButtonTarget.XboxGuide => "Xbox Guide",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
}
~~~

Then:

- \`BackButtonMappingUiOptions.Describe\` delegates to it;
- existing Overlay Controller formatting validates the numeric input and delegates to it;
- Quick Settings discrete options use it.

Do not move ComboBox-specific UI logic into Contracts.

---

## 7. Quick Settings M1/M2 discrete options

In \`QuickSettingsPresentation\`, build one option list from the actual enum values:

~~~csharp
internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> BackButtonMappingOptions =
    Enum.GetValues<Xbox360BackButtonTarget>()
        .Select(target => new QuickSettingsDiscreteOption(
            (int)target,
            BackButtonMappingLabels.GetDisplayName(target)))
        .ToArray();
~~~

Do not hard-code a separate numeric range.

Do not expose raw enum names such as \`LeftBumper\`.

The displayed labels must match the existing Main App and global Overlay Controller labels.

---

## 8. Extend the internal Profile projection narrowly

Current internal projection contains only the fields shared by Steam and XBOX performance/display.

Extend it with one optional controller member:

~~~csharp
private sealed record ProfileQuickSettingsProjection(
    ...,
    QuickSettingsProfileTarget ProfileTarget,
    FrontendGameBackButtonMappingConfiguration? BackButtonMapping);
~~~

Projection rules:

~~~text
Steam FrontendGameProfileSnapshot
→ BackButtonMapping = null

XBOX FrontendXboxGameProfileSnapshot
→ BackButtonMapping = snapshot.BackButtonMapping
~~~

Do not add XBOX-specific platform branching to hardware/application code.

This optional member exists only inside the stateless presentation mapper.

---

## 9. XBOX Profile section order

Current order after PR700:

~~~text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

New order for XBOX:

~~~text
ProfileGeneral
ProfileController
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

Steam order remains exactly:

~~~text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

Implementation should remain simple:

~~~csharp
var sections = new List<QuickSettingsSection>
{
    BuildProfileGeneralSection(snapshot)
};

if (snapshot.BackButtonMapping is not null)
    sections.Add(BuildProfileControllerSection(snapshot));

...
~~~

Do not make a platform enum switch solely to control section order.

The presence of the XBOX-only controller projection is sufficient.

---

## 10. Build the Profile Controller section

Required section:

~~~text
Section label: Controller

Use global M1 / M2 mapping
M1
M2
~~~

### Use-global row

~~~text
RowId       = ProfileBackButtonUseGlobal
ControlKind = Toggle
Value       = configuration.UseGlobalMapping
Commit      = Immediate
~~~

Writability:

~~~text
snapshot.PersistenceWritable
~~~

Do **not** require \`Profile.Enabled == true\`.

This matches the Main App XBOX editor: users may preconfigure the per-game mapping while the profile is disabled.

The mapping only becomes effective according to the already-existing profile-enabled/effective-mapping policy.

### M1 / M2 rows

~~~text
ControlKind = Slider
SliderKind  = Discrete
Options     = BackButtonMappingOptions
Values      = configuration.Mapping.M1 / M2
Commit      = TrailingDebounce300
CommitGroup = ProfileBackButtonMapping
~~~

Writability:

~~~text
snapshot.PersistenceWritable
&& !configuration.UseGlobalMapping
~~~

When Use Global is ON:

- M1/M2 remain visible;
- they display the **current global fallback** values already supplied in the snapshot;
- they are not writable.

When Use Global is OFF:

- M1/M2 show the explicit per-game mapping;
- they are writable.

---

## 11. Use the existing generic Quick Settings renderer

Do not modify the Overlay renderer to special-case XBOX M1/M2 layout.

The existing generic renderer already renders:

- the Controller section heading;
- the Use Global toggle;
- the discrete M1/M2 rows;
- section order from the authoritative snapshot.

The only expected Overlay-specific changes should be those needed for the shared label centralization or tests.

Do not add:

~~~text
BuildXboxControllerSection(...)
XboxM1Row
XboxM2Row
XboxProfileControllerView
~~~

inside the Overlay project.

---

## 12. Use Global mutation semantics

Add one explicit adapter case:

~~~text
ProfileBackButtonUseGlobal
~~~

This row is XBOX-only.

### Steam target

If a Steam intent somehow carries this row:

~~~text
reject
→ zero typed mutation
→ return current authoritative Steam page
~~~

Do not silently map it to any Steam behavior.

### XBOX target / desired ON

~~~text
UseGlobal = true
→ SetXboxGameProfileBackButtonMappingAsync(key, null)
~~~

### XBOX target / desired OFF

The current XBOX page already contains M1/M2 values.

When the user switches Use Global from ON → OFF:

~~~text
read current ProfileBackButtonM1
read current ProfileBackButtonM2
validate both enum values
construct BackButtonMappingSettings
→ SetXboxGameProfileBackButtonMappingAsync(key, mapping)
~~~

Because a Use-Global snapshot carries the current global fallback in \`configuration.Mapping\`, this seeds the explicit per-game override from exactly what the user was currently seeing.

Do not substitute \`BackButtonMappingSettings.Default\`.

---

## 13. M1/M2 grouped mutation semantics

Both rows share:

~~~text
QuickSettingsCommitGroupId.ProfileBackButtonMapping
~~~

The grouped intent must contain exactly:

~~~text
ProfileBackButtonUseGlobal
ProfileBackButtonM1
ProfileBackButtonM2
~~~

Required validation:

- edited row must be M1 or M2;
- exactly one of each row ID;
- no duplicates;
- no unrelated row IDs;
- UseGlobal must be structurally valid Boolean;
- UseGlobal must be \`false\`;
- M1/M2 must be structurally valid Integer values;
- both must map to defined \`Xbox360BackButtonTarget\` values.

Then:

~~~csharp
new BackButtonMappingSettings(m1, m2)
→ BackButtonMappingValidation.Validate(...)
→ SetXboxGameProfileBackButtonMappingAsync(key, mapping)
~~~

Malformed grouped intents invoke zero typed mutations.

---

## 14. Why the existing grouped binding is sufficient

Normal rapid edit:

~~~text
M1 changes
→ group draft seeded with UseGlobal + M1 + M2

M2 changes before debounce expires
→ same group draft updated
→ one newest generation retained

debounce settles
→ one whole BackButtonMappingSettings mutation
~~~

Therefore do not add:

- another save queue;
- another mutation version counter;
- another debounce type;
- another M1/M2 state object inside Overlay.

The existing Quick Settings pending-generation/context retirement logic remains authoritative.

---

## 15. Toggle vs pending M1/M2 draft

The existing immediate-toggle behavior already cancels unsubmitted pending work in the same section.

This gives the required behavior automatically:

~~~text
pending M1/M2 edit
→ user turns Use Global ON
→ unsubmitted ProfileController group draft canceled
→ null mapping mutation submitted
~~~

and:

~~~text
Use Global ON
→ user turns it OFF
→ explicit mapping seeded from current displayed M1/M2 values
→ fresh returned page makes M1/M2 writable
~~~

Do not add another synchronization mechanism.

---

## 16. Typed mutation result remains authoritative

After every mutation:

~~~text
SetXboxGameProfileBackButtonMappingAsync
→ FrontendXboxGameProfileMutationResult
→ QuickSettingsPresentation.BuildProfile(result.Snapshot)
→ returned QuickSettingsMutationResult.Page
~~~

The returned typed snapshot is authoritative.

Do not patch submitted values directly into the previous page.

This preserves:

- persistence-failure behavior;
- active-XBOX apply failure behavior;
- global-fallback projection;
- current effective XBOX profile metadata.

---

## 17. No changes below the typed frontend boundary

PR12 must not modify behavior in:

~~~text
XboxGameProfileMutations
ReconcileEffectiveBackButtonMapping
_activeNonSteamBackButtonMappingOverride
CanonicalXbox360InputPublisher
Xbox360DeviceStateMapper
MsiClawAddonPresentation
Viiper
HidHide
PID1902
XboxGameSessionRuntime
ActiveProfileTarget
~~~

If a change below \`SetXboxGameProfileBackButtonMappingAsync\` appears necessary, stop and re-evaluate first.

PR699 already implemented that layer.

---

## 18. No Host/session changes

PR700 already implemented active Steam/XBOX Profile selection and visible Overlay refresh.

PR12 must not add:

- another active-target callback;
- another Overlay refresh path;
- another Xbox session callback;
- polling;
- game-start/game-exit watchers.

The new rows arrive through the same active XBOX Profile page snapshot already published by PR700.

---

## 19. Main App behavior remains unchanged

The existing XBOX Main App controller editor remains:

~~~text
Use global M1 / M2 mapping
M1
M2
~~~

Its persistence behavior is already correct.

PR12 may only change its target-label formatter to delegate to the new shared label helper.

Do not redesign the Main App XBOX page.

---

## 20. Global Overlay Controller behavior remains unchanged

Current global mapping surface:

~~~text
Overlay Controller
→ M1
→ M2
→ OverlayBackButtonMappingState
→ SetBackButtonMappingAsync
~~~

This path remains separate.

Allowed change:

~~~text
FormatBackButtonTarget(...)
→ shared BackButtonMappingLabels.GetDisplayName(...)
~~~

Not allowed:

- routing global Controller tab through Profile Quick Settings;
- removing the current global mapping transport;
- making Controller tab context-sensitive to active XBOX game.

---

## 21. Quick Settings presentation tests

Add tests proving exact section order.

### Steam

Expected:

~~~text
ProfileGeneral
ProfileTdp                 when limits available
ProfileCpuBoost
ProfilePowerMode           when available
ProfileFpsLimit            when available
ProfileResolution
~~~

And:

~~~text
ProfileController
→ absent
~~~

### XBOX

Expected:

~~~text
ProfileGeneral
ProfileController
ProfileTdp                 when limits available
ProfileCpuBoost
ProfilePowerMode           when available
ProfileFpsLimit            when available
ProfileResolution
~~~

Controller section must be immediately after ProfileGeneral and before all performance/display sections.

---

## 22. Controller projection tests

Required:

### Use Global ON

Given:

~~~text
BackButtonMapping =
  UseGlobalMapping = true
  Mapping = LB / RB
~~~

Project:

~~~text
Use global M1 / M2 mapping = true
M1 = LeftBumper
M2 = RightBumper
M1/M2 writable = false
~~~

### Explicit override

Given:

~~~text
UseGlobalMapping = false
Mapping = X / Y
~~~

Project:

~~~text
Use global M1 / M2 mapping = false
M1 = X
M2 = Y
M1/M2 writable = true when persistence writable
~~~

### Persistence unavailable

All three rows must be non-writable.

### Profile disabled

Controller persistence rows remain editable according to persistence writability, matching the Main App.

Do not tie controller-row writability to Profile Enabled.

---

## 23. Mutation adapter tests

Required cases:

1. XBOX Use Global false → seeds exact current displayed M1/M2 mapping and calls \`SetXboxGameProfileBackButtonMappingAsync(key, mapping)\`;
2. XBOX Use Global true → calls typed mutation with \`null\`;
3. XBOX grouped M1 edit → submits exact whole M1/M2 mapping;
4. XBOX grouped M2 edit → submits exact whole M1/M2 mapping;
5. invalid M1 enum → zero mutation;
6. invalid M2 enum → zero mutation;
7. duplicate group row → zero mutation;
8. missing UseGlobal row → zero mutation;
9. missing M1 or M2 → zero mutation;
10. grouped mutation with UseGlobal true → zero mutation;
11. Steam target + any ProfileBackButton row → zero XBOX/Steam mapping mutation;
12. stale/non-current XBOX target continues to fail at the existing active-target admission boundary before mutation.

Do not add sleep/resume/start/exit tests as PR12 acceptance.

---

## 24. Quick Settings binding tests

Reuse the generic binding; add only focused coverage necessary to prove the new product section uses it correctly.

Required:

- M1 and M2 share \`ProfileBackButtonMapping\` commit group;
- first slider edit seeds the full Controller section draft;
- second slider edit updates the same pending group;
- resulting intent contains UseGlobal + M1 + M2;
- immediate UseGlobal toggle cancels an unsubmitted M1/M2 group draft in the same section;
- target/page-context replacement retires pending Controller drafts through the existing generic logic.

Do not create XBOX-specific binding state.

---

## 25. Label consistency tests

One shared label source must be tested for all defined \`Xbox360BackButtonTarget\` values.

Verify at minimum:

~~~text
DPadUp          → D-Pad Up
LeftBumper      → Left Bumper (LB)
RightBumper     → Right Bumper (RB)
LeftTrigger     → Left Trigger (LT)
RightTrigger    → Right Trigger (RT)
LeftStickClick  → Left Stick Click (L3)
RightStickClick → Right Stick Click (R3)
XboxGuide       → Xbox Guide
~~~

Verify:

- Main UI helper delegates to shared labels;
- Overlay global Controller formatter delegates to shared labels;
- Quick Settings discrete options use shared labels.

No raw enum names appear in normal M1/M2 UI.

---

## 26. Transport tests

### Frontend

Bump 60 → 61.

Verify:

- v60 handshake rejected;
- new section/row/commit-group enum values serialize and deserialize;
- existing typed XBOX back-button RPC remains unchanged.

### Overlay

Bump 16 → 17.

Verify:

- v16 handshake rejected;
- XBOX Profile page with \`ProfileController\` round-trips;
- UseGlobal/M1/M2 rows round-trip;
- grouped M1/M2 mutation intent round-trips;
- malformed/undefined enum values remain rejected by existing validation/deserialization policy.

No new message kind.

---

## 27. Overlay renderer tests

Because the renderer is generic, tests should prove the authoritative page drives the layout rather than introduce XBOX-specific renderer code.

Required:

- XBOX page renders Controller section before TDP/CPU/etc.;
- Use Global renders as Toggle;
- M1/M2 render as discrete choices;
- M1/M2 disabled while Use Global is ON;
- M1/M2 enabled while explicit override is active and writable;
- Steam page contains no Controller section;
- global Controller tab remains present and unchanged.

---

## 28. Source audits

Before PR submission:

### No duplicate owner

Search production code for:

~~~text
SetXboxGameProfileBackButtonMappingAsync
~~~

Expected new PR12 caller:

~~~text
QuickSettingsMutationAdapter
~~~

Do not add a second persistence/apply implementation.

### No XBOX-specific Overlay controls

Expected no new production types such as:

~~~text
XboxProfileM1Row
XboxProfileM2Row
XboxControllerProfilePanel
XboxProfileControllerManager
~~~

### No Steam contamination

Search Steam profile projection/mutation for:

~~~text
ProfileBackButton
BackButtonMappingSettings
~~~

Steam typed profile persistence/mutation must remain unchanged.

### No lifecycle implementation

Do not add:

- polling;
- process watchers;
- WinEvent hooks;
- power participants;
- resume handlers;
- startup reconciliation;
- game-exit handling.

Those are outside PR12.

---

## 29. Expected production files

Likely changes:

~~~text
src/SteamInputAddonforClaw.Contracts/BackButtons/BackButtonMapping.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.UI/Views/BackButtonMappingUiOptions.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
~~~

Plus focused tests.

No Host/session/profile-runtime files should require behavioral changes.

---

## 30. Required automated validation

Run:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

Also run the source audits in section 28.

---

## 31. Physical lifecycle validation is intentionally NOT a PR requirement

Do not add a PR checklist requiring:

- XBOX launch/exit testing;
- Alt+Tab testing;
- Runtime restart testing;
- Sleep testing;
- Hibernate testing;
- Resume testing;
- physical M1/M2 button verification.

Those will be performed later by the user in a separate physical-device validation session.

Do not block PR12 on unavailable hardware validation.

---

## 32. Overengineering constraints

Do not add:

- another profile projection framework;
- another controller DTO hierarchy;
- another Overlay mutation manager;
- another M1/M2 debounce implementation;
- controller mapping epochs;
- new locks;
- background polling;
- provider registries;
- platform strategies.

The existing Quick Settings model already has exactly the primitives needed:

~~~text
optional XBOX-only projection member
+ one Controller section
+ one immediate toggle
+ one grouped two-slider draft
+ one existing typed mutation
~~~

Keep it that small.

---

## 33. Completion condition

PR12 is complete when an active XBOX profile projects:

~~~text
ProfileGeneral
ProfileController
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileFpsLimit
ProfileResolution
~~~

with:

~~~text
ProfileController
  Use global M1 / M2 mapping
  M1
  M2
~~~

and:

~~~text
Use Global ON
→ M1/M2 display current global fallback
→ M1/M2 non-writable
→ typed mutation stores null override

Use Global OFF
→ current displayed mapping becomes explicit override
→ M1/M2 writable
→ grouped edits persist one whole BackButtonMappingSettings
~~~

while all of the following remain true:

~~~text
Steam Profile has no M1/M2 rows
Overlay Controller tab still edits global fallback
SetXboxGameProfileBackButtonMappingAsync remains the only per-game mutation authority
existing effective mapping cache remains the only active override cache
CanonicalXbox360InputPublisher remains the only Xbox360 output publisher
no lifecycle/hardware validation is required for PR acceptance
~~~
