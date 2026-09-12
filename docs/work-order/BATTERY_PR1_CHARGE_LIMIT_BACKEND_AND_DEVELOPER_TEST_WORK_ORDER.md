# Work Order — Battery PR1: MSI Charge-Limit Backend + Developer Validation UI

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `3b127d7da26fdc89c45ebc5204b1a2d36b61a7c2` (`SF-V2-08: Shared Profile projection/dispatch + QAM generic Profile migration`, PR #509)  
> **Primary protocol authority:** `docs/RE_MSI_BatteryChargeLimit.md`  
> **Product/lifecycle authority:** `docs/Full 1902 Implementation/README.md` and the Full1902 documents referenced there  
> **Scope:** Production-quality MSI battery charge-limit hardware backend plus a Developer Menu validation page. No normal Device-page product UI, desired-state persistence/reconciliation, Profile integration, QAM, or Overlay exposure in this PR.

---

## 1. Goal

Implement the smallest safe MSI Claw battery charge-limit backend and expose it only through a developer-only Main UI test page so the protocol can be validated on real supported Claw hardware before the normal-user Device UI is built.

PR1 must establish and test this exact hardware path:

```text
Developer Main UI
    -> existing IAddonFrontendControl / desktop frontend transport
    -> Runtime-owned battery test operation
    -> existing shared MSI helper transport
    -> Get_Data(215) / Set_Data(215)
    -> readback verification
    -> MSI ACPI / firmware
```

The important design decision is that battery charge limit is an **independent Device feature**. It is not controller routing state and must not acquire dependencies on Full1902 presentation, PID1901/PID1902 selection, HidHide, VIIPER, Steam/BPM state, front-button handling, or fan/TDP policy.

PR1 exists to answer one practical question with safe tooling:

> On each supported MSI Claw model, do enable/disable and the Addon product values `60, 65, 70, 75, 80, 85, 90, 95, 100` round-trip correctly through the established MSI `Get_Data/Set_Data` block-215 protocol?

---

## 2. Required documents and references to read before implementation

Read these repository documents together before modifying code:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/RE_MSI_BatteryChargeLimit.md
```

Also inspect the current source, not an older snapshot:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/IMsiClawTdpTransport.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawWmiTdpTransport.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs
src/SteamInputAddonforClaw.TdpHelper/TdpHelperProtocol.cs
src/SteamInputAddonforClaw.TdpHelper/Program.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawDeviceModels.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
```

Use the existing `Fan Hardware Probe` only as a **Developer navigation/frontend wiring precedent**. Do not copy fan ownership/lifecycle behavior into the battery feature.

External implementation reference:

```text
Valkirie/HandheldCompanion
HandheldCompanion/Devices/MSI/ClawA1M.cs
```

HHC confirms the same block-215 bit layout, but its current error-handling behavior is not the Addon failure contract. The Addon must follow `docs/RE_MSI_BatteryChargeLimit.md` and fail closed.

---

## 3. Product decisions fixed for this work order

These are requirements, not items for implementation-time reinterpretation.

### 3.1 Supported user values

The Addon product range is exactly:

```text
60%
65%
70%
75%
80%
85%
90%
95%
100%
```

Equivalent validation rule:

```text
60 <= value <= 100
and
(value - 60) % 5 == 0
```

Do **not** expose the protocol's theoretical lower-seven-bit `0..127` range.

Do **not** copy HHC's current UI/bypass step size as Addon product policy. The Addon step is fixed at **5%**.

### 3.2 Enabled 100% and Disabled are distinct states

Keep these separate even if their observed charging behavior can be similar:

```text
Enabled=true,  Limit=100%
Enabled=false, remembered Limit=100%
```

The raw protocol has a separate enable bit, so the Addon must preserve that distinction.

### 3.3 Scope in PR1

PR1 contains:

```text
safe hardware backend
helper whitelist extension for block 215
frontend developer-only RPC seam
developer-only Main UI test page
unit/transport/navigation tests
```

PR1 does **not** contain:

```text
normal Device-page Battery card
settings.json desired-state persistence
startup reconciliation
sleep/hibernate/resume reconciliation
periodic polling
per-game Profile battery settings
QAM / Addon Overlay battery controls
shared Quick Settings battery row
battery notifications/toasts
battery-health recommendation logic
```

Normal Main App Device UI is a later PR after PR1 hardware validation. QAM/Overlay work is later again and must not be pulled into this PR.

### 3.4 CTW is out of scope

There is no current CTW integration/coexistence product requirement for this feature.

Do not add:

```text
CTW detection
CTW IPC
CTW ownership arbitration
CTW state mirroring
CTW compatibility gates
CTW-specific persistence
```

Historical CTW research must not create a runtime dependency or another authority.

---

## 4. Protocol contract

`docs/RE_MSI_BatteryChargeLimit.md` establishes the command path:

```text
Namespace: root\WMI
Class:     MSI_ACPI
Instance:  MSI_ACPI.InstanceName='ACPI\\PNP0C14\\0_0'
Method:    Get_Data / Set_Data
Block:     215 / 0xD7
```

The relevant logical byte is:

```text
bit 7      charge-limit enable
bits 0..6  charge-limit value
```

After the current helper's WMI response framing is removed, `TryGetData(215, out payload)` returns the logical payload and the relevant byte is `payload[0]`.

### 4.1 Read

Required behavior:

```csharp
if (!_transport.TryGetData(215, out var payload) || payload.Length < 1)
    return failure;

byte raw = payload[0];
bool enabled = (raw & 0x80) != 0;
int percent = raw & 0x7F;
```

A read failure is not `0`, `100`, `Disabled`, or any other synthetic state.

### 4.2 Change limit while preserving enable state

For a valid requested product value:

```csharp
byte target = (byte)((raw & 0x80) | requestedPercent);
```

Then:

```text
Set_Data(215, target)
Get_Data(215)
verify:
  enable bit == original enable bit
  lower seven bits == requestedPercent
```

### 4.3 Disable while preserving remembered value

```csharp
byte target = (byte)(raw & 0x7F);
```

Read back and verify:

```text
enable bit == 0
lower seven bits == original lower seven bits
```

### 4.4 Enable while preserving remembered value

```csharp
byte target = (byte)(raw | 0x80);
```

However, PR1 must **not enable an out-of-product-range remembered value**.

If the current lower seven bits are not one of:

```text
60, 65, 70, 75, 80, 85, 90, 95, 100
```

return `InvalidTarget` / equivalent without writing. The Developer page can instruct the developer to apply a valid limit first, then enable it.

This prevents an old/foreign/raw value such as `40` from being silently activated by the Addon.

### 4.5 Readback is mandatory

A normal return from `Set_Data` is not enough to declare success.

Every mutation must perform a fresh `Get_Data(215)` and validate the requested owned bits.

If readback fails or differs, return a verification failure and show the actual failure to the Developer UI.

Do not retry repeatedly and do not build a state machine around a diagnostic write. One read -> one write -> one verification read is sufficient for PR1.

---

## 5. Reuse the existing MSI helper path

Current `main` already has the required infrastructure.

`IMsiClawTdpTransport` already exposes:

```csharp
bool TryGetData(int block, out byte[] payload);
bool TrySetData(int block, byte value);
```

`HelperMsiClawTdpTransport` already forwards both through the existing elevated `TdpHelperClient`.

`TdpHelperClient` already has:

```text
GetData -> Get_Data
SetData -> Set_Data
```

and the helper already serializes operations over its existing pipe.

Therefore:

> **Do not create a second WMI stack, a BatteryHelper process, another elevated helper, or a generic EC framework.**

The existing helper name contains `Tdp`, but renaming/generalizing that infrastructure is outside this PR. Reuse it as-is.

### 5.1 Required helper whitelist change

Current `TdpHelperProtocol.IsSupported()` allows:

```text
SetData: 80, 81, 152, 210, 212
GetData: 152, 210, 212
```

Add **only** block `215` to the existing `GetData` and `SetData` allowed sets.

Target concept:

```csharp
"SetData" => index is 80 or 81 or 152 or 210 or 212 or 215,
"GetData" => index is 152 or 210 or 212 or 215,
```

Do not change these operations to accept arbitrary `0..255` blocks.

### 5.2 Direct WMI transport parity

`MsiClawWmiTdpTransport` currently implements the same MSI WMI mechanics directly but does not currently override `TryGetData`.

Add the narrow parity method by reusing its existing `TryInvoke` implementation, for example:

```csharp
public bool TryGetData(int block, out byte[] payload) =>
    TryInvoke("Get_Data", BuildPackage(block, 0), "Block", block, true, out payload);
```

Do not duplicate `TryInvoke`, response framing, fallback handling, or WMI exception logic.

---

## 6. New battery hardware backend

Add one narrow backend under the existing MSI Claw device feature location, for example:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawBatteryChargeLimitHardware.cs
```

Suggested responsibility:

```text
MsiClawBatteryChargeLimitHardware
    -> validate Addon product target
    -> read block 215
    -> decode raw state
    -> perform safe read-modify-write
    -> perform readback verification
    -> return typed result
```

It should depend on the existing `IMsiClawTdpTransport` only.

### 6.1 Suggested narrow internal model

Exact names may differ, but keep the model battery-specific rather than creating a generic device-setting abstraction.

A reasonable shape is:

```csharp
internal readonly record struct MsiBatteryChargeLimitState(
    bool Enabled,
    int LimitPercent,
    byte RawValue,
    bool IsProductValue);

internal enum MsiBatteryChargeLimitMutationOutcome
{
    Succeeded,
    InvalidTarget,
    ReadFailed,
    WriteFailed,
    VerificationFailed
}
```

The result may carry the latest successfully observed state where available.

Do not create:

```text
IDeviceSetting<T>
IDevicePolicyManager
BatteryAuthorityManager
ECTransactionManager
BatteryStateMachine
BatteryEpoch / lease / ownership token
```

There is no demonstrated product need for those abstractions in PR1.

### 6.2 Observed out-of-range state

A raw live value outside the Addon product set must be represented honestly.

Example:

```text
raw lower bits = 83
```

Return/display:

```text
Observed limit: 83%
Product value:  No / unsupported by Addon UI
```

Do not clamp it to 80 or 85 merely for presentation.

The Developer page may still allow `Disable`, because disabling preserves the unowned remembered lower bits and only clears the enable bit.

Do not allow `Enable` until a valid Addon value has been explicitly applied.

---

## 7. Supported hardware gate

Use the existing MSI Claw model identity as the product gate.

Current exact recognized boards are:

```text
MS-1T42 -> MSI Claw 7 AI+ A2VM
MS-1T52 -> MSI Claw 8 AI+ A2VM
MS-1T91 -> MSI Claw 8 EX AI+ CG3EM
```

Do not add fuzzy product-name matching, CPU/GPU inference, or a second board table for the battery feature.

The Developer test is available only when the current authoritative hardware assessment resolves to a supported known MSI Claw model.

A future Claw model must be explicitly identified and validated before this write path is enabled on it.

---

## 8. Frontend contract for the Developer test

The WinUI process must not call WMI directly.

Keep Runtime/hardware authority behind `IAddonFrontendControl`, following the current Fan/Sensor Developer pages.

Add a narrow battery-test contract, for example:

```csharp
public sealed record FrontendBatteryChargeLimitTestSnapshot(
    bool Available,
    string Manufacturer,
    string Model,
    string BaseBoard,
    bool? Enabled,
    int? LimitPercent,
    byte? RawValue,
    bool ProductValueValid,
    string? FailureMessage);

public enum FrontendBatteryChargeLimitTestMutationOutcome
{
    Succeeded,
    InvalidTarget,
    ReadFailed,
    WriteFailed,
    VerificationFailed,
    Unavailable
}

public sealed record FrontendBatteryChargeLimitTestMutationResult(
    FrontendBatteryChargeLimitTestMutationOutcome Outcome,
    string? FailureMessage,
    FrontendBatteryChargeLimitTestSnapshot Snapshot);
```

Exact record organization may change if a smaller equally clear contract fits current conventions.

Add these methods to `IAddonFrontendControl` with fail-closed default implementations:

```csharp
Task<FrontendBatteryChargeLimitTestSnapshot> CaptureBatteryChargeLimitTestAsync(...);
Task<FrontendBatteryChargeLimitTestMutationResult> SetBatteryChargeLimitTestEnabledAsync(bool enabled, ...);
Task<FrontendBatteryChargeLimitTestMutationResult> SetBatteryChargeLimitTestPercentAsync(int percent, ...);
```

Use explicit `Test`/`Probe` naming in PR1 so this manual validation seam is not accidentally treated as the future persisted production Device-setting authority.

---

## 9. `InProcessAddonFrontendControl` composition

Reuse the existing Runtime-owned helper transport already supplied for the fan probe:

```text
AddonProcessHost._tdpTransport
    -> InProcessAddonFrontendControl fanProbeTransport
```

PR1 must not create another helper owner in `AddonProcessHost`.

The frontend control may create/use one battery-specific hardware backend over the same supplied transport.

Recommended shape:

```text
shared existing IMsiClawTdpTransport instance
    +-- existing fan probe
    +-- new battery charge-limit backend
```

This is shared hardware transport reuse, not shared policy authority.

### 9.1 Capture

For `CaptureBatteryChargeLimitTestAsync`:

1. reject shutdown as existing frontend operations do;
2. capture authoritative device/hardware status;
3. gate to a recognized supported MSI Claw;
4. call the battery backend read;
5. return actual observed state or a clear unavailable/read-failure result.

### 9.2 Mutation

For enable/disable/percent mutation:

- validate product target before write where applicable;
- use the battery backend's RMW/readback path;
- return the fresh authoritative readback snapshot;
- do not update controller-routing status or trigger controller reconciliation;
- do not persist a desired value in PR1.

A simple manual operation path is enough. Do not copy the fan probe's armed-session/suspend cleanup state, because battery charge limit is intentionally persistent firmware policy rather than a temporary dangerous diagnostic fan table.

---

## 10. Desktop frontend transport

This is a typed Main UI <-> Runtime contract change, so update the desktop frontend wire explicitly.

### 10.1 Protocol version

Current baseline:

```text
FrontendTransportProtocol.CurrentVersion = 28
```

Bump it to:

```text
29
```

Add the matching Version 29 comment explaining that the Developer battery charge-limit test adds typed capture/mutation RPCs.

### 10.2 RPC methods

Add narrow methods such as:

```text
CaptureBatteryChargeLimitTest
SetBatteryChargeLimitTestEnabled
SetBatteryChargeLimitTestPercent
```

with request records for the mutation payloads.

Update:

```text
FrontendWire.cs
NamedPipeAddonFrontendClient.cs
NamedPipeAddonFrontendServer.cs
```

and the frontend transport tests.

### 10.3 Do not expose to QAM/Overlay in PR1

Do not add battery fields/rows to:

```text
FrontendDeviceQuickSettingsSnapshot
QuickSettingsPresentation
QuickSettingsMutationAdapter
QamFrontendBridge
qam.js
Overlay protocol
shared Quick Settings Device page
```

The user-facing QAM/Overlay feature is explicitly deferred until after the normal Main App UI implementation.

---

## 11. Developer Menu UI

Add one card to the existing Developer Menu:

```text
Battery Charge Limit Test
Developer-only MSI battery charge-limit read/write validation.
Changes apply immediately and remain until changed.
[Open]
```

Mirror the existing Sensor/Fan child-page navigation style. Do not place all test controls inline on `DeveloperPage`.

Add a child page, for example:

```text
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs
```

### 11.1 Target page layout

Keep it simple and consistent with existing WinUI 3 pages:

```text
Battery Charge Limit Test
Developer-only hardware validation.
Changes are written immediately to MSI firmware state and are NOT restored when leaving this page.

[Device]
Manufacturer      ...
Model             ...
Board             ...

[Live State]
Enabled           Yes / No / Unknown
Limit             80% / 83% / Unknown
Raw               0xD0 / Unknown
Product value     Valid / Outside Addon range / Unknown

[Refresh]

[Limit]
[ 60% v ] [Apply Limit]
  allowed items only: 60,65,70,75,80,85,90,95,100

[State]
[Enable] [Disable]

[Result / error text]
```

### 11.2 Use buttons, not a mutation-bound toggle

Use explicit `Enable` and `Disable` buttons in the Developer test.

Do not bind a `ToggleSwitch.Toggled` event directly to firmware writes while the page initializes/renders. This page is diagnostic tooling, so explicit operations are preferable and easier to validate.

### 11.3 Selector behavior

If the observed limit is one of the allowed Addon values, select it in the ComboBox.

If the observed limit is outside the allowed set:

- display the actual observed value;
- leave the Addon limit selector unselected or clearly separate from the live value;
- do not silently choose the nearest valid value.

### 11.4 Busy/error behavior

While a hardware operation is in progress:

- disable mutation controls;
- prevent duplicate button execution;
- re-enable controls after completion;
- render the returned authoritative snapshot after every successful or failed mutation when one is available.

A page-local busy flag is sufficient. Do not introduce a global operation scheduler for this test.

### 11.5 Back/close behavior

Leaving the page must **not** restore the original battery setting.

Battery charge limit is persistent device policy, unlike temporary fan calibration mutations.

Do not add shutdown cleanup that disables or resets the charge limit.

---

## 12. Navigation wiring

Extend the current child-page pattern minimally.

### `DeveloperPage`

Add:

```text
BatteryChargeLimitTestRequested event
OpenBatteryChargeLimitTestButton_Click handler
```

### `MainNavigationState`

Add one child page:

```text
BatteryChargeLimitTest
```

and:

```text
OpenBatteryChargeLimitTest()
Mouse Back from BatteryChargeLimitTest -> DeveloperMenu
```

### `MainWindow`

Instantiate the page in the existing content grid, initialize it with the existing `_frontend`, wire the Developer request, and wire Back to `ReturnToDeveloperMenu()`.

Do not create a new navigation service/router for one child destination.

---

## 13. Lifecycle policy in PR1

PR1 intentionally has almost no automatic lifecycle policy.

### Startup / restart

Do not automatically write a battery value at Runtime startup.

### Sleep / hibernate / resume

Do not automatically write/reconcile in PR1.

The Developer test may be used manually before/after resume to observe whether firmware preserved the state, but production reconciliation belongs to the later normal Device-feature PR after hardware behavior is known.

### App shutdown / crash

Do not restore or disable the charge limit.

### Physical device/PnP changes

A later manual capture should either read the currently available MSI ACPI provider or return unavailable/read failure. Do not build a new PnP watcher solely for this diagnostic.

### Controller routing

Battery operations must not call or depend on:

```text
PID1901 / PID1902 transitions
HidHide
VIIPER
Steam/BPM presentation
controller recovery journal
routing rollback
```

A battery operation failure is local to the battery test and must not invalidate a healthy controller route.

---

## 14. Center M behavior in PR1

Do not add automatic Center M arbitration or a periodic fight/reapply loop.

PR1 is a manual validation tool over the live firmware value:

```text
Refresh -> observe current firmware state
Explicit button -> write once
Readback -> verify
```

If MSI Center M later changes the same firmware value, the next Developer `Refresh` must display that actual value. The Addon does not immediately reassert a desired value because PR1 deliberately has no persisted desired-state owner.

This keeps PR1 useful for coexistence observation without prematurely designing PR2 reconciliation policy.

---

## 15. Logging

Keep logs low-volume and operation-oriented.

Recommended events:

```text
BatteryLimit read failed
BatteryLimit write rejected: invalid product target
BatteryLimit Set_Data failed
BatteryLimit readback failed
BatteryLimit verification mismatch
BatteryLimit mutation succeeded (Debug, raw before/after)
```

Do not log in a polling loop because PR1 has no polling loop.

Do not log sensitive machine identifiers beyond the same ordinary device/board information already used by Developer diagnostics.

---

## 16. Automated tests

Add focused tests for the new backend.

Suggested file:

```text
tests/SteamInputAddonforClaw.Tests/MsiClawBatteryChargeLimitHardwareTests.cs
```

Required cases:

1. decode enabled `80%` from raw `0xD0`;
2. decode disabled remembered `80%` from raw `0x50`;
3. limit write preserves enable bit, e.g. `0xD0 -> 0xD5` for `85%`;
4. limit write preserves disabled state, e.g. `0x50 -> 0x55` for `85%`;
5. Enable preserves a valid lower-seven-bit value;
6. Disable preserves the lower-seven-bit value;
7. all exact product values `60..100` in 5% steps are accepted;
8. invalid targets such as `59`, `61`, `99`, `101` are rejected before any write;
9. enabled `100%` and disabled remembered `100%` remain distinct (`0xE4` vs `0x64`);
10. `TryGetData` failure performs no write;
11. empty/invalid read payload performs no write;
12. `TrySetData` failure reports write failure;
13. verification read failure reports verification failure;
14. readback mismatch reports verification failure;
15. observed out-of-product-range value is reported without clamping;
16. Enable of an out-of-product-range remembered value is rejected without write;
17. Disable may clear bit 7 while preserving an observed out-of-range lower value.

### 16.1 Helper protocol tests

Add/update tests proving:

```text
GetData(215) == supported
SetData(215) == supported
GetData(214/216) == unsupported
SetData(214/216) == unsupported
```

Do not weaken the existing whitelist assertion.

### 16.2 Frontend transport tests

Update `FrontendNamedPipeTransportTests` to cover the new protocol/RPCs:

- capture round trip;
- enable mutation round trip;
- percent mutation round trip;
- protocol version `29` handshake expectation;
- existing protocol mismatch behavior remains fail-fast.

### 16.3 Navigation tests

Update `MainNavigationStateTests`:

```text
OpenBatteryChargeLimitTest -> BatteryChargeLimitTest
Mouse Back -> DeveloperMenu
```

Do not add elaborate UI automation for the simple XAML page unless an existing test pattern makes it trivial.

---

## 17. Manual hardware validation after PR1

PR1 provides the tool required for physical validation. This does not need to become an artificial CI gate, but **PR2 normal-user Device UI must not assume success on a model that has not been physically validated**.

For each available target board, initially:

```text
A2VM
  MS-1T42 and/or MS-1T52

CG3EM
  MS-1T91
```

validate:

```text
Refresh produces a real non-synthetic block-215 value.
Apply 60,65,70,75,80,85,90,95,100 individually.
Each write read-backs exactly.
Applying a percentage preserves the existing enable bit.
Enable preserves the selected valid percentage.
Disable preserves the selected percentage.
100% Enabled remains distinct from Disabled.
Leaving/reopening the page reports the actual firmware state.
No controller routing/presentation behavior changes during battery operations.
```

If a model rejects or quantizes a value, record the evidence and stop before PR2 product exposure for that model. Do not hide model differences with a retry/clamping layer.

Sleep/hibernate/reboot persistence can be observed and recorded during validation, but PR1 must not add automatic reconciliation based on assumptions from another model.

---

## 18. Expected source changes

Expected/new files include:

```text
NEW
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawBatteryChargeLimitHardware.cs
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs
tests/SteamInputAddonforClaw.Tests/MsiClawBatteryChargeLimitHardwareTests.cs

MODIFY
src/SteamInputAddonforClaw.TdpHelper/TdpHelperProtocol.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawWmiTdpTransport.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/MainNavigationStateTests.cs
```

A helper-protocol test file may be added or an existing suitable test file may be extended.

`AddonProcessHost.cs` should normally **not** need another helper field/process. If constructor wiring requires the battery feature to receive a transport separately, pass the same existing `_tdpTransport`; do not instantiate a second `HelperMsiClawTdpTransport`.

---

## 19. Explicit non-goals / forbidden expansion

Do not expand PR1 into any of the following:

```text
normal-user Device Battery UI
QAM or Overlay implementation
shared Quick Settings battery schema
per-game charge-limit profiles
battery telemetry/health analytics
charge/discharge automation
automatic AC/DC switching
startup/resume desired-state enforcement
new background polling service
new PnP watcher
new helper executable
TdpHelper rename/refactor
new generic MSI WMI abstraction
CTW integration/coexistence framework
controller routing changes
HidHide/VIIPER changes
fan-control changes
TDP changes beyond reusing the existing transport
```

The PR is successful if it leaves one small, trustworthy battery hardware write path and one explicit Developer UI to exercise it.

---

## 20. Validation commands

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.sln -c Debug
dotnet build SteamInputAddonforClaw.sln -c Release
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug
```

Expected:

```text
Debug build: 0 errors
Release build: 0 errors
Test suite: PASS
```

Do not accept a test change that merely weakens an existing helper whitelist, routing invariant, or frontend protocol mismatch test to make PR1 pass.

---

## 21. Acceptance criteria

PR1 is implementation-complete when all of the following are true:

- block `215` is admitted only through the existing bounded MSI helper operations;
- no second MSI WMI/helper owner is introduced;
- battery read failure never becomes a guessed value;
- every mutation uses read-modify-write and mandatory readback verification;
- only `60..100` in 5% increments can be selected/applied through the Addon test UI;
- Enable cannot activate an invalid remembered lower-seven-bit value;
- `100% Enabled` and `Disabled` remain distinct;
- a Developer Menu child page can Refresh, Apply Limit, Enable, and Disable;
- the page shows actual raw/live state and does not clamp unexpected firmware values;
- leaving the page/app does not restore or reset battery policy;
- there is no startup/resume/polling reconciliation in PR1;
- there is no Device-page, Profile, QAM, Overlay, or CTW integration in PR1;
- battery failure cannot disturb controller routing/HidHide/VIIPER state;
- desktop frontend protocol version is bumped consistently and transport tests pass;
- Debug/Release builds and the test suite pass.

---

## 22. Follow-up boundary

After PR1 is merged and real-hardware behavior is validated, prepare a separate PR for the normal App `Device` page.

That later PR may add:

```text
normal-user Battery Charge Limit card
persistent desired Enabled + Limit value
startup reconciliation
sleep/hibernate/resume reconciliation based on PR1 observations
user-facing failure/status presentation
```

Even that Main App production PR should still exclude QAM/Overlay initially.

QAM/Overlay battery controls are a later, separate work item after the Main App implementation is stable.
