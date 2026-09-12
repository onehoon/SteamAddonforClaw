# Work Order — Battery PR2: Production Device UI, Persistence, and Lifecycle Reconcile

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `856a72abd4383ae2ef58c15a7fc4ce4d02706f14` (`Add automated battery validation runner (#512)`)  
> **Primary product authority:** `docs/Full 1902 Implementation/README.md` and the referenced Full1902 authority documents  
> **Hardware protocol authority:** `docs/RE_MSI_BatteryChargeLimit.md` plus the current `MsiClawBatteryChargeLimitHardware` implementation  
> **Scope:** Production MSI Claw Battery Charge Limit in the normal **Device** page, including durable desired state, startup/restart reconcile, and resume reconcile.  
> **Explicitly out of scope:** QAM, Overlay, Game Profile override, CTW integration, polling, new battery protocol research, new MSI helper process, and controller authority changes.

---

## 1. Goal

Promote the already hardware-validated MSI battery charge-limit backend from Developer-only diagnostics into a production Device feature.

The production contract is:

```text
Device > Battery Charge Limit

- one normal Device-page card
- Battery Saver icon
- On/Off ToggleSwitch
- one 60–100% slider in exact 5% steps
- moving the slider changes only a local UI draft
- hardware/persistence commit occurs only when the user finishes the interaction
- Runtime owns desired state and hardware reconcile
- startup / controlled Runtime restart converges hardware to persisted desired state
- resume converges hardware to persisted desired state
- no polling
- no QAM / Overlay in this PR
```

This PR must create **one clear production owner** for Battery Charge Limit and reuse the existing proven hardware primitive.

Do not build a second WMI implementation, a second helper client, or a second battery authority.

---

## 2. Required documents and source to read before implementation

Read these together before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/RE_MSI_BatteryChargeLimit.md
```

Also inspect the current production Device/Profile ownership patterns and battery diagnostic implementation:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawBatteryChargeLimitHardware.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/IMsiClawTdpTransport.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Profiles/ProfileStore.cs
src/SteamInputAddonforClaw/Profiles/ProfileMutationGate.cs
src/SteamInputAddonforClaw/Profiles/DeviceSettings.cs
src/SteamInputAddonforClaw/Profiles/Performance/PowerModeRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/TdpRuntime.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
```

Relevant tests include at least:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawBatteryChargeLimitHardwareTests.cs
tests/SteamInputAddonforClaw.Tests/BatteryChargeLimitValidationRunnerTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/ProfileStoreTests.cs
```

Do not implement from this work order blindly if current `main` has materially changed.

---

## 3. Current evidence already established

### 3.1 Protocol

The existing backend is intentionally narrow and already uses:

```text
MSI ACPI WMI
Block 215 / 0xD7
bit 7     = Enabled
bits 0..6 = remembered charge-limit percentage
```

Production code must continue to use the existing:

```csharp
MsiClawBatteryChargeLimitHardware
```

which already provides:

```text
Read()
SetPercent(int percent)
SetEnabled(bool enabled)
```

with:

```text
read-modify-write
+ mandatory fresh readback
+ exact enabled/percentage verification
+ no synthetic zero buffer
+ no blind write after failed read
```

Do not weaken those semantics.

### 3.2 Product range

Locked product values:

```text
60
65
70
75
80
85
90
95
100
```

No 20-point HHC-style UI step.

No arbitrary integer percentage.

No automatic clamp of an observed foreign/non-product value.

### 3.3 Physical MS-1T91 validation completed twice

Two consecutive automatic validation runs on:

```text
MSI Claw 8 EX AI+ CG3EM Launch Pack
BaseBoard: MS-1T91
```

completed all 22 validation steps and restore successfully.

Observed disabled bytes:

```text
60  -> 0x3C
65  -> 0x41
70  -> 0x46
75  -> 0x4B
80  -> 0x50
85  -> 0x55
90  -> 0x5A
95  -> 0x5F
100 -> 0x64
```

Observed enabled bytes:

```text
60  -> 0xBC
65  -> 0xC1
70  -> 0xC6
75  -> 0xCB
80  -> 0xD0
85  -> 0xD5
90  -> 0xDA
95  -> 0xDF
100 -> 0xE4
```

The physical runs also proved:

```text
Enabled=true,  Limit=100, Raw=0xE4
!=
Enabled=false, Limit=100, Raw=0x64
```

and restored the initial state:

```text
Enabled=true
Limit=80
Raw=0xD0
```

on both runs.

### 3.4 Supported models

Use the same battery protocol for the currently supported MSI Claw boards:

```text
MS-1T42
MS-1T52
MS-1T91
```

Do **not** add board-specific battery protocol classes or mappings without evidence of a real difference.

MS-1T91 has direct physical validation. MS-1T42/MS-1T52 use the same MSI battery protocol contract already evidenced by retained MSI/HHC/CTW research and are supported by the same production path.

CTW is not a product integration target. It is historical/reference evidence only.

---

## 4. Locked product decisions

### 4.1 Surface

This PR adds Battery Charge Limit only to:

```text
Main UI > Device
```

Do **not** expose it yet in:

```text
QAM
Overlay
Profile
Developer Quick Settings
```

The Developer hardware validation page remains available and unchanged except for mechanical compile fixes if required.

### 4.2 Global Device setting

Battery Charge Limit is a Device-wide global setting.

It is **not**:

```text
per-game
AC/DC split
Steam/BPM dependent
controller presentation dependent
Center M authority dependent
```

### 4.3 On/Off semantics

These are distinct durable desired states:

```text
Enabled=true,  Limit=100
Enabled=false, Limit=100
```

Turning the feature OFF must preserve the remembered percentage.

Changing the percentage while OFF is allowed and updates the remembered desired percentage.

### 4.4 No Expander

Do not use `SettingsExpander` for this feature.

There is only one child value, so an Expander adds unnecessary interaction.

Use one inline `SettingsCard` containing:

```text
Header / description
Battery Saver icon
ToggleSwitch
percentage label
Slider
```

### 4.5 Icon

Use the Windows battery-saver glyph rather than reusing the current Power Mode card icon.

Official Microsoft Segoe Fluent Icons mapping:

```text
BatterySaver8 = E86B
```

Reference:

```text
https://learn.microsoft.com/windows/apps/design/iconography/segoe-fluent-icons-font
```

Suggested XAML:

```xml
<ctcontrols:SettingsCard.HeaderIcon>
    <FontIcon Glyph="&#xE86B;" />
</ctcontrols:SettingsCard.HeaderIcon>
```

Do not introduce an image asset merely for this card.

---

## 5. Architecture: one production Runtime owner

Create one production Battery Charge Limit runtime owner, e.g.:

```csharp
MsiClawBatteryChargeLimitRuntime
```

Exact class naming may follow current repository conventions, but there must be one owner with this responsibility:

```text
persisted desired state
+ current hardware state
+ startup/resume reconcile
+ mutation ordering
+ frontend snapshot
```

Conceptually:

```text
DevicePage
   |
   v
IAddonFrontendControl production battery methods
   |
   v
MsiClawBatteryChargeLimitRuntime
   |---- ProfileStore + ProfileMutationGate
   |
   `---- existing MsiClawBatteryChargeLimitHardware
             |
             `---- existing shared HelperMsiClawTdpTransport
```

### 5.1 Do not create

Do not add:

```text
BatteryManager
BatteryService + BatteryCoordinator + BatteryRepository layers
new helper executable
new WMI wrapper
new ACPI transport
new power-event watcher dedicated to battery
new polling loop
new retry state machine
new authority epoch/barrier
```

The feature is small and must stay small.

### 5.2 Shared transport ownership

`AddonProcessHost` already owns one MSI helper transport used by TDP/fan/battery diagnostics.

Production battery must reuse the same process-owned transport.

Do not construct an independent helper client inside the Runtime or UI.

### 5.3 Existing developer test seam

The current frontend contract is explicitly developer-test-only:

```text
CaptureBatteryChargeLimitTestAsync
SetBatteryChargeLimitTestEnabledAsync
SetBatteryChargeLimitTestPercentAsync
```

Do not make the production Device page call these methods directly.

They can continue to call the same underlying hardware primitive, but production state requires its own persisted desired-state and lifecycle-aware contract.

---

## 6. Persistence model

Use the existing `profiles.json` Device domain, not `settings.json`.

Reason:

```text
profiles.json already owns Device-wide durable product configuration
CPU Boost / TDP / Power Mode already use this domain
ProfileStore has safe-load semantics
ProfileMutationGate is the existing lost-update serialization owner
```

Do not create a new battery JSON file.

Do not add a parallel settings store.

### 6.1 Proposed schema

Add a Device battery category alongside the existing Performance / Display categories:

```csharp
public sealed record DeviceSettings
{
    public DevicePerformanceSettings Performance { get; init; } = new();
    public DeviceDisplaySettings Display { get; init; } = new();
    public DeviceBatterySettings Battery { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DeviceBatterySettings
{
    public DeviceBatteryChargeLimitSettings? ChargeLimit { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DeviceBatteryChargeLimitSettings
{
    public required bool Enabled { get; init; }
    public required int LimitPercent { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
```

This is an additive schema-v1 field. Do not increment schema version solely for an optional additive field if current repository policy does not require it.

### 6.2 Structural validation

Update `ProfileStore.HasValidStructure()` so an explicit JSON:

```json
"battery": null
```

is not accepted as a structurally safe loaded document.

Do not silently replace malformed/unsupported/unreadable profile state.

### 6.3 Use the existing mutation gate

All battery load-modify-save operations must use the existing shared:

```csharp
ProfileMutationGate
```

Do not add a battery-only persistence lock.

The purpose is to avoid lost updates with simultaneous Device/Profile mutations.

---

## 7. First-run / bootstrap policy

There must be no hardcoded Addon default such as `80%` written to firmware merely because this build is installed.

### 7.1 No persisted battery setting yet

On startup, if:

```text
Device.Battery.ChargeLimit == null
```

read the actual hardware once.

If the observed value is product-valid:

```text
Available=true
Limit in {60,65,...100}
```

bootstrap the persisted desired state from the actual hardware:

```text
Desired.Enabled      = Current.Enabled
Desired.LimitPercent = Current.LimitPercent
```

Then save that desired state.

**Do not write hardware during this bootstrap.**

Example:

```text
firmware = Enabled / 80%
no persisted battery state

startup
-> read Enabled / 80%
-> persist Enabled / 80%
-> zero Set_Data writes
```

### 7.2 Non-product initial hardware value

If actual hardware reports e.g.:

```text
Enabled=true
Limit=83
```

then:

```text
show the actual state
DO NOT clamp to 80/85
DO NOT save a fabricated desired value
DO NOT write hardware
```

The production state remains uninitialized until the user explicitly commits a valid slider value.

This is a realistic preservation rule, not theoretical robustness.

### 7.3 Invalid manually edited persisted value

If `profiles.json` is structurally readable but contains an out-of-product battery target, do not auto-apply it.

Do not build migration machinery for unsupported manual edits.

Required behavior:

```text
- report the battery desired state as invalid / not applicable for hardware apply
- do not mutate hardware from that invalid target
- keep UI available enough for the user to commit a valid 60–100 / 5% value
- a later valid user mutation may replace the invalid target
```

Do not turn one invalid battery value into a reason to overwrite unrelated profile fields.

---

## 8. Runtime snapshot and mutation contract

Do not expose raw bytes in the production Device UI contract. Raw values remain useful on the Developer validation page.

Suggested production frontend shape:

```csharp
public sealed record FrontendBatteryChargeLimitSnapshot(
    bool Available,
    bool PersistenceWritable,
    bool Initialized,
    bool? CurrentEnabled,
    int? CurrentLimitPercent,
    bool? DesiredEnabled,
    int? DesiredLimitPercent,
    string? LastFailure)
{
    public static readonly FrontendBatteryChargeLimitSnapshot Unavailable =
        new(false, false, false, null, null, null, null, null);
}
```

Exact naming may vary, but preserve the distinction:

```text
Current = actual read hardware
Desired = persisted Addon authority
```

Do not collapse them into one bool/int.

### 8.1 Mutation result

Suggested result contract:

```csharp
public enum FrontendBatteryChargeLimitMutationOutcome
{
    Succeeded,
    InvalidTarget,
    PersistenceFailed,
    ApplyFailed,
    Unavailable
}

public sealed record FrontendBatteryChargeLimitMutationResult(
    FrontendBatteryChargeLimitMutationOutcome Outcome,
    string? FailureMessage,
    FrontendBatteryChargeLimitSnapshot Snapshot)
{
    public bool Succeeded => Outcome == FrontendBatteryChargeLimitMutationOutcome.Succeeded;
}
```

The production caller does not need to know whether the MSI backend failed specifically at WMI read/write/readback.

Keep those detailed failures in the hardware log / failure message. The product-level distinction that matters is:

```text
PersistenceFailed
vs
ApplyFailed after persistence succeeded
```

### 8.2 Focused production frontend methods

Add focused production methods such as:

```csharp
Task<FrontendBatteryChargeLimitSnapshot> CaptureBatteryChargeLimitAsync(...);
Task<FrontendBatteryChargeLimitMutationResult> SetBatteryChargeLimitEnabledAsync(bool enabled, ...);
Task<FrontendBatteryChargeLimitMutationResult> SetBatteryChargeLimitPercentAsync(int percent, ...);
```

Do not rename/remove the Developer Test RPCs in this PR unless required by a deliberate cleanup with full test coverage.

### 8.3 Do NOT add Battery to `FrontendDeviceQuickSettingsSnapshot` in this PR

The current shared Device aggregate explicitly documents that it is the CPU Boost / TDP / Power Mode convenience projection and must not gain unrelated feature members casually.

Therefore this PR must **not** extend:

```csharp
FrontendDeviceQuickSettingsSnapshot
```

with Battery Charge Limit.

Main UI `DevicePage.RefreshAsync()` may perform the existing aggregate capture plus one focused battery capture.

Future QAM/Overlay work will explicitly extend the Shared Quick Settings product contract in its own PR.

This avoids silently coupling this Device-only PR to QAM/Overlay transport and presentation work.

---

## 9. Runtime mutation policy: persist first, then converge hardware

A user mutation must make the persisted desired state authoritative before hardware apply.

Conceptually:

```text
validate target
-> load profiles.json under ProfileMutationGate
-> require CanSafelyReplace
-> save new desired battery state
-> reconcile actual hardware toward new desired
-> return authoritative snapshot
```

### 9.1 Persistence failure

If persistence fails:

```text
no hardware write
old desired state remains authoritative
Outcome = PersistenceFailed
```

### 9.2 Apply failure after persistence succeeded

If persistence succeeds but hardware apply fails:

```text
new desired state remains authoritative
Outcome = ApplyFailed
LastFailure populated
future startup/resume/user mutation may converge it again
```

Do not roll `profiles.json` back merely because one WMI operation failed.

Do not make the UI snap to the old desired value in that case.

---

## 10. One reconcile algorithm

The Runtime should own one small reconcile path used by:

```text
startup
resume
user Toggle mutation
user slider commit
```

Conceptually:

```csharp
private ReconcileResult ReconcileDesired(DeviceBatteryChargeLimitSettings desired)
{
    var current = _hardware.Read();
    if (!current.Succeeded || current.State is not { } state)
        return Fail("Battery charge-limit read failed.");

    if (desired.Enabled)
    {
        if (state.LimitPercent != desired.LimitPercent)
        {
            var percent = _hardware.SetPercent(desired.LimitPercent);
            if (!percent.Succeeded) return Fail(percent.FailureMessage);
            state = percent.State!.Value;
        }

        if (!state.Enabled)
        {
            var enabled = _hardware.SetEnabled(true);
            if (!enabled.Succeeded) return Fail(enabled.FailureMessage);
            state = enabled.State!.Value;
        }
    }
    else
    {
        if (state.Enabled)
        {
            var disabled = _hardware.SetEnabled(false);
            if (!disabled.Succeeded) return Fail(disabled.FailureMessage);
            state = disabled.State!.Value;
        }

        if (state.LimitPercent != desired.LimitPercent)
        {
            var percent = _hardware.SetPercent(desired.LimitPercent);
            if (!percent.Succeeded) return Fail(percent.FailureMessage);
            state = percent.State!.Value;
        }
    }

    return Success(state);
}
```

The important ordering is locked:

```text
Desired Enabled=true
-> establish desired percentage first
-> then enable if required

Desired Enabled=false
-> disable first if required
-> then establish remembered percentage
```

This handles drift and preserves the meaning of disabled remembered percentage.

Do not assume the hardware's enabled bit already matches the persisted desired state merely because the user changed only the slider.

### 10.1 No-op is success

If hardware already equals desired:

```text
no Set_Data
Succeeded
```

### 10.2 No retry loop yet

One reconcile attempt is enough for this PR.

Do not add generalized retries, exponential backoff, epochs, or state machines without real MSI hardware evidence that one settled attempt is insufficient.

---

## 11. Startup / Runtime-restart reconcile

Battery is a persistent Runtime-owned Device feature.

During normal Runtime startup, after the shared MSI helper transport is available:

```text
construct one battery runtime
-> load/bootstrap desired state
-> reconcile desired state once
```

Integrate it with the existing Device-feature startup phase, not with controller ownership admission.

A Battery failure must be feature-local:

```text
battery reconcile failure
!= controller startup failure
!= HidHide failure
!= VIIPER failure
!= Runtime startup failure
```

Log the failure and keep Runtime/tray/controller operational.

### 11.1 No Center M coupling

Battery Charge Limit is independent of whether Center M startup authority is Enabled or Disabled.

Do not gate Battery on:

```text
PID1901/PID1902
DirectInput
HidHide
VIIPER
Steam/BPM
Center M startup state
```

The MSI WMI transport is the only relevant hardware capability boundary.

---

## 12. Resume reconcile

Resume is a real supported product lifecycle and must reconcile Battery Charge Limit.

Do not create another Windows power notification source.

The current host already has a centralized Resume path and an existing delayed Device-feature settle for CPU Boost / Power Mode.

Extend that existing delayed Device reconcile path to include Battery Charge Limit.

Conceptually:

```text
PowerResumeObserved
-> controller presentation reconcile immediately
-> existing ~2.5 s Device/performance settle
   -> CPU Boost reconcile
   -> Power Mode reconcile
   -> Battery Charge Limit reconcile
```

Battery must not delay the controller's immediate Full1902 resume path.

### 12.1 Resume behavior

After the settle delay:

```text
load persisted desired
-> read actual battery hardware
-> if equal: no-op
-> if drifted: converge once
-> if unavailable/failure: record/log failure, no retry loop
```

Do not assume firmware state survives sleep/hibernate merely because it normally does.

### 12.2 Shutdown

Battery requires no special shutdown restoration.

The persisted desired state is intended to remain the Addon product preference.

Do not disable the charge limit during controlled Runtime shutdown.

Do not restore an OEM/default battery value on exit.

---

## 13. Device page UI

### 13.1 Card placement

Add the card on the normal `DevicePage`.

Use existing Device card styling and spacing.

Suggested placement:

```text
Device summary
MSI Center M
Battery Charge Limit
TDP Control
CPU Boost
Windows Power Mode
...
```

The exact location may be adjusted for current UI consistency, but Battery belongs in Device, not Controller or Settings.

### 13.2 Card structure

Recommended XAML shape:

```xml
<InfoBar
    x:Name="BatteryChargeLimitInfoBar"
    IsClosable="False"
    IsOpen="False"
    Severity="Warning" />

<ctcontrols:SettingsCard
    x:Name="BatteryChargeLimitCard"
    Header="Battery Charge Limit"
    Description="Limit battery charging to help reduce long-term battery wear.">

    <ctcontrols:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE86B;" />
    </ctcontrols:SettingsCard.HeaderIcon>

    <Grid ColumnSpacing="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>

        <TextBlock
            x:Name="BatteryChargeLimitValueText"
            VerticalAlignment="Center"
            Text="—%" />

        <Slider
            x:Name="BatteryChargeLimitSlider"
            Grid.Column="1"
            Minimum="60"
            Maximum="100"
            StepFrequency="5"
            TickFrequency="5"
            ValueChanged="BatteryChargeLimitSlider_ValueChanged"
            PointerCaptureLost="BatteryChargeLimitSlider_PointerCaptureLost"
            KeyUp="BatteryChargeLimitSlider_KeyUp" />

        <ToggleSwitch
            x:Name="BatteryChargeLimitToggle"
            Grid.Column="2"
            OffContent=""
            OnContent=""
            Toggled="BatteryChargeLimitToggle_Toggled" />
    </Grid>
</ctcontrols:SettingsCard>
```

This is illustrative, not a requirement to use exactly these event names.

### 13.3 Toggle and slider visible together

Do not hide the slider when Toggle is OFF.

The hardware protocol explicitly preserves the lower seven percentage bits while disabled, so users must be able to select the remembered limit before re-enabling.

Expected behavior:

```text
OFF + 80%
user changes slider to 85%
-> desired becomes OFF + 85%
-> firmware remembered value becomes 85%
-> still OFF

then user toggles ON
-> desired becomes ON + 85%
-> firmware converges to ON + 85%
```

### 13.4 Percentage text

Show the current draft percentage next to the slider while the user is dragging.

Examples:

```text
60%
80%
100%
```

Do not show raw WMI bytes in production UI.

---

## 14. Slider interaction: release-commit, not debounce-write

This feature is explicitly **not** a live control.

Do not copy the current TDP 300 ms trailing hardware mutation behavior.

### 14.1 While moving

`ValueChanged` must only:

```text
- snap/normalize local draft to the valid 5% product step
- update percentage text
- mark draft dirty
```

It must not:

```text
- save profiles.json
- call frontend mutation RPC
- call WMI
```

### 14.2 Commit boundary

Commit once when the interaction is completed.

For pointer/touch interaction, use an existing WinUI event pattern that reliably represents release/end of capture, such as:

```text
PointerCaptureLost
```

or an equivalent combination proven against mouse and touch.

Do not depend only on `PointerReleased` if slider internal pointer capture means the page does not reliably receive it.

For keyboard/gamepad slider edits, commit on a clear end-of-edit event such as `KeyUp` / equivalent rather than writing on every repeated value change.

### 14.3 One local commit helper

Use one page-local commit seam, e.g.:

```csharp
private async Task CommitBatteryChargeLimitDraftAsync()
{
    if (!_batteryDraftDirty || _frontend is null)
        return;

    var percent = NormalizeBatteryPercent(_batteryDraftPercent);
    if (_batterySnapshot.DesiredLimitPercent == percent)
    {
        _batteryDraftDirty = false;
        return;
    }

    _batteryDraftDirty = false;
    await RunBatteryMutationAsync(
        () => _frontend.SetBatteryChargeLimitPercentAsync(percent));
}
```

Exact implementation may differ.

### 14.4 Avoid duplicate commit

Pointer release/capture-loss/key events can sometimes arrive in combinations.

Do not create a state machine for that.

A simple:

```text
draftDirty
+ last authoritative desired value
```

is enough to make same-value duplicate commits no-op.

### 14.5 Render must not write back

Like the existing CPU/TDP/Power Mode controls, use a suppression flag while applying an authoritative snapshot to the Toggle/Slider.

Programmatic `IsOn` / `Value` changes must never be mistaken for a user mutation.

---

## 15. UI render rules

The Device page must render from the production Runtime snapshot.

### 15.1 Normal initialized state

If:

```text
Available=true
PersistenceWritable=true
Initialized=true
```

then:

```text
Toggle.IsOn = DesiredEnabled
Slider.Value = DesiredLimitPercent
controls enabled
```

The production setting is the persisted desired state; actual current hardware is diagnostic/current-state context.

If desired and current differ because the last apply failed, keep displaying the desired setting and show a warning.

Do not silently make current hardware the new desired value after an apply failure.

### 15.2 Uninitialized but readable hardware

If no desired state exists yet because current hardware is non-product:

```text
show actual current percentage text
show actual enabled state where meaningful
allow a valid slider commit to initialize the product setting
```

Do not visually pretend 83% is 80% or 85%.

A programmatically assigned slider value may reflect the actual 83% location before the first user edit if WinUI permits it; the first committed user edit must normalize to an allowed 5% product value.

### 15.3 Persistence not safe to replace

If `ProfileStore.Load().CanSafelyReplace == false`:

```text
show actual battery read if available
show InfoBar error
Disable Toggle and Slider mutation
```

Do not overwrite malformed/newer/unreadable `profiles.json` just to save Battery Charge Limit.

### 15.4 Hardware unavailable

If hardware transport/read is unavailable:

```text
show card if this is still a supported MSI Claw product capability
Disable controls
show concise unavailable/failure InfoBar
```

Do not crash the Device page or hide unrelated cards.

---

## 16. Toggle mutation

Toggle is an immediate deliberate action, unlike slider drag.

On a real user toggle:

```text
persist desired Enabled state
-> reconcile the complete desired { Enabled, LimitPercent }
-> mandatory hardware readback remains inside the existing hardware backend
-> render returned authoritative production snapshot
```

Do not call only `SetEnabled()` and assume percentage is already correct.

The reconcile path must own the complete desired pair.

If the feature is uninitialized because there is no valid desired percentage yet, enabling cannot fabricate one.

Return `InvalidTarget` / visible guidance asking the user to choose a valid percentage first.

---

## 17. Frontend transport

Add the focused production battery methods to the existing named-pipe frontend transport.

Expected touched surfaces likely include:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Follow the current transport versioning policy.

If adding production RPC methods changes the closed named-pipe protocol, bump the frontend protocol version exactly once and update transport tests.

Do not add a second pipe or a battery-specific IPC channel.

---

## 18. State invalidation

Successful production battery mutation should raise the existing frontend state invalidation event if that is the current Device-feature convention.

However:

```text
capture/read alone must not invalidate state
startup reconcile should not cause a UI refresh storm
```

Keep this event-driven.

No polling timer.

Since Battery is intentionally not in Shared Quick Settings in this PR, QAM/Overlay do not need to consume the invalidation yet.

---

## 19. Logging

Normal AppLog should remain concise.

Recommended production logs:

```text
Battery desired state bootstrapped from hardware
Battery desired mutation persisted
Battery reconcile succeeded
Battery reconcile failed
Battery resume reconcile failed
```

At Debug level it is acceptable to include:

```text
trigger
DesiredEnabled
DesiredLimit
CurrentEnabled
CurrentLimit
underlying hardware failure kind/message
```

Do not log every slider `ValueChanged` event.

Do not reuse the dedicated Developer validation report writer for normal product operation.

---

## 20. Failure policy

### Persistence failure

```text
no hardware mutation
surface Error
keep previous authoritative desired state
```

### Hardware apply/readback failure after persistence

```text
new persisted desired state remains authoritative
surface Warning
retain LastFailure
future startup/resume/user mutation can retry convergence
```

### Resume failure

```text
feature-local warning/log
no loop
no controller impact
```

### Shutdown during in-flight operation

Use existing Runtime shutdown admission/cancellation conventions.

Do not invent a separate battery shutdown state machine.

### Helper/WMI exception

Map through the existing hardware/backend failure behavior.

Do not let a battery exception escape and terminate Runtime.

---

## 21. Explicitly forbidden coupling

Battery Charge Limit must not become a dependency of:

```text
PID1902 ownership
PID1901 restoration
DirectInput acquisition
HidHide normalization
VIIPER initialization/teardown
Steam/BPM presentation switching
Win+G suppression
OEM1/WING mapping
Center M Enable/Disable and Restart
```

Likewise those systems must not be required merely to read/set block 215.

This is an independent Device capability.

---

## 22. Suggested implementation files

Likely modified/new files:

```text
src/SteamInputAddonforClaw/Profiles/DeviceSettings.cs
src/SteamInputAddonforClaw/Profiles/ProfileStore.cs

src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawBatteryChargeLimitRuntime.cs   (new; naming may vary)
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawBatteryChargeLimitHardware.cs
    only if a tiny reusable surface adjustment is actually required

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
```

Tests should be added beside the existing Device/Profile/battery tests rather than creating a new test project.

---

## 23. Required unit/integration tests

At minimum add tests for the following realistic product paths.

### 23.1 Bootstrap

```text
no persisted setting + hardware valid 80% enabled
-> persisted desired becomes enabled/80
-> zero hardware writes
```

```text
no persisted setting + hardware 83%
-> no persisted fabricated battery setting
-> zero hardware writes
-> snapshot remains uninitialized but reports actual state
```

### 23.2 Persistence safety

```text
ProfileStore unsafe to replace
-> mutation returns PersistenceFailed
-> zero hardware write
```

### 23.3 Reconcile no-op

```text
desired enabled/80 == hardware enabled/80
-> zero Set_Data
-> success
```

### 23.4 Enable ordering

```text
desired enabled/80
hardware disabled/100
-> SetPercent(80)
-> SetEnabled(true)
-> verified success
```

### 23.5 Disable ordering

```text
desired disabled/80
hardware enabled/100
-> SetEnabled(false)
-> SetPercent(80)
-> verified success
```

### 23.6 Slider desired mutation

```text
persist enabled/80
user commits 85
-> save enabled/85
-> reconcile
-> final desired/current enabled/85
```

### 23.7 Apply failure after persistence

```text
save desired succeeds
hardware Set_Data/readback fails
-> Outcome ApplyFailed
-> persisted desired remains new value
-> snapshot reports LastFailure
```

### 23.8 Toggle with no valid desired percent

```text
uninitialized non-product actual value
user tries to enable without first choosing valid product percentage
-> InvalidTarget
-> no fabricated percentage
```

### 23.9 Resume reconcile

Verify the existing host resume path invokes battery reconcile only in the delayed Device-feature portion, after the immediate controller presentation reconcile request.

Do not require instruction-level callback ordering beyond the real lifecycle contract.

### 23.10 Frontend transport

Add round-trip tests for:

```text
CaptureBatteryChargeLimit
SetBatteryChargeLimitEnabled
SetBatteryChargeLimitPercent
```

Verify the returned production snapshot and mutation outcome survive the named pipe unchanged.

### 23.11 UI event policy

Add source/UI architecture tests or focused logic tests proving:

```text
ValueChanged updates draft only
no SetBatteryChargeLimitPercentAsync call from ValueChanged
commit helper exists on end-of-interaction
render suppression prevents programmatic feedback mutation
```

Prefer testing a small pure helper if that makes the release-commit behavior deterministic.

Do not create a generalized slider framework just for this.

---

## 24. Physical validation after implementation

Run on the available MS-1T91 first.

Required smoke validation:

```text
1. Open Device page.
2. Confirm current 80% / Enabled state renders correctly.
3. Drag slider 80 -> 60 -> 65 -> 70 -> 75 -> 80 -> 85 while holding input.
4. Confirm no intermediate hardware mutation is issued during the drag.
5. Release at 85.
6. Confirm exactly one logical user commit and verified hardware state Enabled / 85.
7. Toggle OFF.
8. Confirm Disabled / 85.
9. Move and release slider at 90 while OFF.
10. Confirm Disabled / 90.
11. Toggle ON.
12. Confirm Enabled / 90.
13. Restart Runtime / Windows as appropriate and confirm desired state remains Enabled / 90.
14. Sleep / resume and confirm desired state remains/reconverges Enabled / 90.
15. Restore preferred user value after test.
```

A2VM (`MS-1T42` / `MS-1T52`) physical smoke validation is useful when available but is **not** a reason to build a separate protocol path in advance.

---

## 25. QAM / Overlay future handoff boundary

Do not implement these now, but leave the production Runtime contract reusable.

Future QAM/Overlay work should consume the same:

```text
FrontendBatteryChargeLimitSnapshot
SetBatteryChargeLimitEnabled
SetBatteryChargeLimitPercent
```

through the deliberate Shared Quick Settings extension for that later PR.

Do not let this PR pre-implement hidden QAM rows, Overlay schema fields, mutation kinds, JS controls, or generic metadata just because they may be useful later.

The correct sequence is:

```text
PR2: production Runtime + persistence + Main Device UI + lifecycle
later PR: Shared Quick Settings / QAM / Overlay projection
```

---

## 26. Non-goals

This PR must not:

```text
- remove the Developer battery validation page
- add Game Profile battery overrides
- add charging-state telemetry
- estimate time-to-full/time-to-empty
- poll battery percentage
- change Windows battery policy
- change MSI Center M ownership
- change controller routing
- touch HidHide/VIIPER ownership
- redesign TDP UI
- implement Fan Control
- add CTW/HHC integration
- support arbitrary 1% battery steps
- add automatic normalization of 83-like external values
```

---

## 27. Acceptance checklist

Implementation is complete only when all are true:

```text
[ ] Production battery runtime exists with one clear desired-state/hardware owner.
[ ] Existing MsiClawBatteryChargeLimitHardware is reused.
[ ] Existing shared MSI helper transport is reused.
[ ] No second WMI/helper implementation exists.
[ ] Device-wide desired state persists in profiles.json.
[ ] Existing ProfileMutationGate protects battery save operations.
[ ] First valid hardware state bootstraps desired state without a hardware write.
[ ] Non-product first-run value is preserved and not clamped/written.
[ ] Startup/Runtime restart reconcile is implemented.
[ ] Resume reconcile is implemented through the existing delayed Device lifecycle path.
[ ] Resume battery work does not delay immediate controller presentation resume reconcile.
[ ] Battery failure remains feature-local.
[ ] Device page contains one inline SettingsCard, not an Expander.
[ ] Header icon uses BatterySaver8 / E86B or a documented equivalent battery-saver glyph.
[ ] Toggle and slider are visible together.
[ ] Slider is exactly 60–100 in 5% product steps.
[ ] Slider ValueChanged performs zero persistence/WMI mutation.
[ ] Pointer/touch/keyboard end-of-interaction commits once.
[ ] Toggle commits immediately.
[ ] OFF preserves remembered percentage and slider remains editable.
[ ] 100% Enabled and 100% Disabled remain distinct.
[ ] Production UI never displays raw block-215 bytes.
[ ] Production frontend does not call Developer Test RPCs directly.
[ ] Battery is NOT added to FrontendDeviceQuickSettingsSnapshot in this PR.
[ ] No QAM/Overlay implementation is added.
[ ] Named-pipe production battery RPC tests pass.
[ ] Battery runtime tests cover bootstrap, ordering, no-op, persistence failure, apply failure, and resume.
[ ] Existing battery hardware/validation tests remain green.
[ ] Full Debug and Release builds pass.
[ ] Full test suite passes.
[ ] git diff --check passes.
```

---

## 28. Implementation priority

Keep the implementation straightforward.

The required safety comes from:

```text
one persisted desired state
one Runtime owner
one shared ProfileMutationGate
one existing hardware backend
mandatory backend readback
one startup reconcile
one resume reconcile
```

Do not add synchronization machinery for theoretical slider/power/callback interleavings that are not realistically reachable or harmful in the supported single-user, single-interactive-session product lifecycle.

If a race-defense proposal requires a new authority, manager, epoch, barrier, generalized state machine, or wrapper layer, first prove the real supported lifecycle failure it prevents.

The target architecture is not maximum abstraction. It is:

> **one clear Battery Charge Limit owner, one desired state, one hardware path, one reconcile policy.**
