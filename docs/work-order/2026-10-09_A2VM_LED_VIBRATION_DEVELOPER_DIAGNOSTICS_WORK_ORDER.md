# Work Order — A2VM Vibration Strength and Joystick LED Developer Diagnostics (Single PR)

**Date:** 2026-10-09  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation:** Local Codex, one focused code PR  
**Hardware evidence / acceptance:** User performs physical tests **after merge**; physical testing is **not** a Codex/CI/PR-review blocker  
**Scope:** Developer diagnostics only. No A2VM production feature enablement.

## 1. Decision

Implement **both missing A2VM controller-feature investigations in one diagnostic PR**:

1. Allow the existing bounded **left 0 / right 100** vibration-strength profile write and explicit **50 / 50** restore experiment on the supported target **Claw 8 AI+ A2VM (MS-1T52)**.
2. Add one bounded **read-only joystick LED profile-address probe** for the observed A2VM controller firmware `0x0230`, using the historically supported **candidate** RGB profile base `0x024A`.

These are independent developer actions on the same existing PID1902 vendor HID. Reuse the Runtime's current controller owner, control-HID resolver, HID transport, frontend RPC conventions, and Developer diagnostics page. Do **not** make a new HID service, controller authority, cross-process lock, state machine, or standalone PowerShell dependency.

**Evidence standard:** A successful transport write is **not** proof of a physical motor effect. A structurally valid LED ReadProfile reply is **not** proof that the candidate address is safe to mutate. This PR gathers evidence; normal user feature support is a later decision.

## 2. Required product/architecture context

Read these current documents and follow their precedence:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/RE_MSI_ControllerVibration.md`
- `docs/RE_MSI_Joystick_LED.md`
- `docs/work-order/FULL1902_CG3EM_VIBRATION_0_100_RUNTIME_WRITE_PROBE_WORK_ORDER_2026-10-04.md`
- `docs/work-order/FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md`

This is a **standalone Full1902 application**. Do not implement CTW integration or Center M coexistence. Existing CTW/HHC observations are reference evidence only, not runtime dependencies.

Supported product environment: one Windows user, one interactive session; no RDP/multi-session/Fast User Switching architecture. Preserve the actual sleep/hibernate/resume, restart/crash/shutdown, physical loss/PnP return, ownership and teardown contracts. No new guards for purely theoretical instruction-level race interleavings.

## 3. Evidence from 2026-10-09 A2VM Windows logs

After an EC reset restored the native **composite** USB structure, the Runtime successfully obtained exactly one PID1902 physical root:

```text
VID_0DB0&PID_1902
HID ... MI_00&COL01 = physical DirectInput gamepad
HID ... MI_00&COL02 = vendor control HID (UsagePage=FFF0, Usage=0040)
Firmware REV_0230
GamepadMode desktop -> DirectInput normalization: ACK verified
PhysicalOwnershipVerified; HidHide verified
Xbox360 -> SteamDeck -> Xbox360 presentation switching: successful
```

**Unresolved feature-local logs:**

```text
ControllerLedApplyFailed Reason=UnsupportedFirmware FirmwareVersion=0x0230
ControllerVibrationSettingsApplySkipped Reason=ProductionPairWriteNotVerifiedForModel
```

No PID mode recovery, Steam routing, HidHide, VIIPER, rumble forwarding, or controller input redesign is authorized by this work order.

### 3.1 Existing source hooks

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationStrengthClient.cs`
  - `RunDiagnosticMotorPairWriteAsync` already exists.
  - It uses `MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred` and `RestoreFiftyFifty`, `_transactionGate`, exact PID1902 vendor HID resolution, and a single profile write.
  - **Only model admission currently restricts diagnostic execution to `msi.claw.cg3em`.**
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationProfileCommand.cs`
  - Existing 64-byte motor pair frame: `0F 00 00 3C 21 01 00 22 02 <Left> <Right>`.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationProfilePolicy.cs`
  - **Production policy intentionally enables only `msi.claw.cg3em`. Do not modify.**
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs`
  - Existing exact physical identity, vendor HID, firmware read, and production write path; reuse its resolution mechanism.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedProtocol.cs`
  - Known firmware RGB addresses are exact-table-only. `0x0230` has **no verified mapping**. Keep production `TryResolveRgbAddress` and `TryBuildStaticWrites` unchanged.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs`
  - Existing `IMsiClawRawHidTransport.WriteAndReadAsync` performs a bounded 64-byte HID request/read on one handle; reuse it for LED read-only diagnostics.
- `src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml[.cs]`
  - Existing Developer page already contains the vibration profile test/restore buttons and result status.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`, `AddonProcessHost.cs`, `FrontendContracts.cs`, and named-pipe client/server/wire define the current frontend pattern.

## 4. Part A — A2VM vibration profile probe

### 4.1 Model-scoped diagnostic eligibility

Keep **CG3EM** diagnostic behavior as is. Add the exact target model **`msi.claw.a2vm.8`** to the *developer-only* probe eligibility. Do not automatically include `msi.claw.a2vm.7` or unknown models. This field observation belongs to A2VM 8 AI+ / MS-1T52, controller revision `0x0230`.

Example, in the existing method, not as a new policy manager:

```csharp
if (_modelId.Value is not ("msi.claw.cg3em" or "msi.claw.a2vm.8"))
    return DiagnosticProbeUnavailable(mode, pair.Value, "UnsupportedModel");
```

Preserve the current Center M exactly-Disabled guard, strong physical identity, one exact PID1902 `0xFFF0/0x0040` control HID, transaction gate, cancellation, and one-write behavior. If there is no healthy currently owned physical session, return `Unavailable` with zero writes via the existing Runtime/frontend ownership fact rather than borrowing another authority source. Make the smallest necessary change to hook the existing fact in; avoid new persistent state.

### 4.2 Exact write packets

```text
Apply:   0F 00 00 3C 21 01 00 22 02 00 64 [zero-fill to 64 bytes]
Restore: 0F 00 00 3C 21 01 00 22 02 32 32 [zero-fill to 64 bytes]
```

- `0x21` is WriteProfile; `0x22` at **byte 7** is the motor profile *offset*, **not** the `SyncToROM` command.
- Never send the standalone `0x22` SyncToROM command.
- Do not implement retries, background probes, automatic restores, test loops, or a new persistence model.
- Do not change `settings.json` saved vibration percentages.
- A2VM results must say **"HID write accepted / physical effect not yet verified on A2VM"** rather than the current CG3EM-specific success statement.
- Label 50/50 as **"Restore Addon default 50/50"**; it is **not** guaranteed to be the firmware's original pre-test hardware value.
- A failed restore must be explicitly reported; do not silently claim restoration.

### 4.3 UI workflow

Keep existing buttons in `Developer -> Vibration Test`. A2VM user can:

1. Press **Apply Left 0 / Right 100** once.
2. Use the existing **Controller -> Vibration Strength -> physical Left Test / Right Test** to observe actual motor behavior, if physical rumble testing is available.
3. Press **Restore 50 / 50** once and recheck physically.

Retain the existing prohibition against running the Xbox360 terminal STOP-loop diagnostic and profile-write probe at the same time. Do not treat profile-write success as a rumble callback success.

## 5. Part B — A2VM joystick LED candidate-address read-only probe

### 5.1 Why read only

The historical A2VM `0x0229` and `0x0308` observations both use RGB base `0x024A`. **They do not establish that A2VM firmware `0x0230` uses that same writable address**. A profile read can gather firmware evidence without a WriteProfile mutation.

**This PR must not send an LED `0x21` WriteProfile packet**, overwrite any RGB slots, write another guessed address, or send `SyncToROM`. A limited LED color-mutation experiment is allowed **only in a follow-up after the read evidence is reviewed**. This avoids making unverified profile-memory writes simply to reduce the number of PRs.

### 5.2 New developer-only LED read probe

Add a narrowly named method near the existing `MsiClawLedController`/protocol, not a generic profile editor. Trigger **only** from a new explicit developer UI button, for example:

```text
A2VM LED Address Probe (Read Only)
[Read 0x024A Profile]
Status: ...
```

Mandatory preflight, including another check immediately before I/O where existing code already provides it:

- Center M startup authority **exactly Disabled**.
- Currently healthy **Addon-owned** PID1902 physical DirectInput session; use `_physicalOwnership.LiveInputSource` and `OwnedPhysicalIdentity` supplied by the existing Runtime.
- Model exactly `msi.claw.a2vm.8`; controller attributes `VID=0x0DB0`, `PID=0x1902`, `VersionNumber=0x0230`.
- One strongly matching current PID1902 control HID at `UsagePage=0xFFF0`, `Usage=0x0040`; reject missing/ambiguous/mismatched endpoints.
- Use the current `IMsiClawRawHidTransport.WriteAndReadAsync`, max one or a few bounded replies and a short timeout; **no periodic polling or read daemon**.
- No operations while the Runtime is shutting down or in a real physical-input recovery/suspend state; reuse current session-liveness/ownership checks, do not add an epoch/manager.

The 64-byte HID outbound *ReadProfile request* (this is a query, not profile mutation):

```text
0F 00 00 3C 04 01 02 4A 20 00 ... [64 bytes]
             ^^ ^^ ^^^^^ ^^^
          read index addr  len
```

The historical response shape, as described in `docs/RE_MSI_Joystick_LED.md`:

```text
10 00 00 3C 05 01 02 4A 20 00 01 <effect> <speed> <brightness> <27 RGB bytes> ...
```

Implement a **pure exact-response parser** with conservative bounds:

- Actual input report length and ReportID match expected HID endpoint/reply shape.
- `response[0]==0x10`, `response[3]==0x3C`, `response[4]==0x05`.
- Validate echoed index `1`, address `0x024A`, requested block length `0x20` and header/profile layout where supported by the documented protocol.
- Distinguish `TransportWriteFailed`, `NoReply/Timeout`, `UnexpectedReport`, `WrongAddress/Index`, and `CandidateReadbackParsed` instead of flattening them into success.
- Log/report bounded bytes (e.g., up to the documented first profile block) and parsed effect/speed/brightness/RGB triplets. Do not call any returned color profile "verified hardware mapping" merely because the bytes are syntactically plausible.
- Do not silently accept malformed/truncated messages or switch to alternate memory addresses.
- The probe must make **zero** LED `0x21` profile-write calls, even on failure.

If current transport cannot report whether the HID request write itself succeeded, keep the outcome honest (e.g. `NoValidResponse`) rather than fabricating an ACK. Fix only the narrowly necessary transport result exposure, if unavoidable, without introducing a second transport.

### 5.3 Frontend/API/UI implementation

Prefer extending the existing Developer `VibrationTestPage` with a clearly separated **A2VM LED Profile Read-Only Probe** section rather than introducing a new Developer navigation page.

Connect via the same Runtime → `InProcessAddonFrontendControl` → named-pipe client/server → main UI RPC pattern as the existing vibration profile probe. Add only one developer-only frontend operation and compact outcome/status record. Keep production `ControllerLedSettings`, the normal Controller page, and Overlay/Quick Settings untouched.

**Important:** `FrontendRpcMethod` is a positional enum; **append** the new RPC enum member, do not insert it in the middle and renumber existing operations. Respect the existing transport compatibility/version convention; do not bump the protocol merely for this synchronized one-PR client/server addition unless actual protocol tests demand it.

Provide visible result text with firmware, candidate address, and whether it was a valid *read response*; preserve detailed bytes only in Runtime diagnostic logging if that is cleaner.

## 6. Separation from production and normal lifecycle

The following must remain exactly as before:

- `MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified`: CG3EM only.
- `MsiClawLedProtocol.TryResolveRgbAddress(0x0230)`: **false**, no production mapping.
- Normal Controller-page vibration/LED setting mutations on A2VM: **unavailable / fail-closed**.
- Physical game rumble and left/right motor test semantics.
- Initial PID1902 acquisition, GamepadMode normalization, HidHide isolation and whitelist, VIIPER server/device ownership, Xbox360 ↔ SteamDeck switching, WING/OEM1 actions, Overlay, startup task, shutdown and PID restoration.
- Existing CG3EM LED and vibration behavior and its own diagnostic eligibility.

No Developer probe should run at startup, status refresh, page activation, Steam/BPM transition, sleep/resume, PnP arrival or controlled Runtime restart.

A diagnostic failure **must remain feature-local**: no controller mode changes, routing rollback, HidHide mutation, virtual-controller detach, ownership release, or automatic retry.

No CTW IPC/integration, Center M runtime launch, or arbitrary firmware memory/address scanner.

## 7. Logging and result interpretation

Use consistent structured events instead of verbose per-byte spam:

```text
ControllerVibrationProfileWriteProbeStarted
ControllerVibrationProfileWriteProbeCompleted
    Model=msi.claw.a2vm.8
    Left=0 Right=100 / Left=50 Right=50
    TransportSucceeded=True|False
    PhysicalEffectVerified=False
    ProductionEnabled=False

ControllerLedProfileReadProbeStarted
    Model=msi.claw.a2vm.8
    Firmware=0x0230 CandidateAddress=0x024A ReadIndex=1 Length=0x20
ControllerLedProfileReadProbeCompleted
    Outcome=CandidateReadbackParsed|NoValidResponse|Unavailable|Failed
    ReplyMatchesRequest=True|False
    Effect=... Speed=... Brightness=...
    ProductionEnabled=False
```

Physical effect is assessed by the **user** after merge, not by Codex. Document the distinction between transmitted command, parsed reply, and physically observed behavior. Report outcome/exception details without dumping unrelated raw HID reports.

## 8. Tests required from local Codex (software only)

Provide focused automated tests using existing fake HID/endpoint/frontend transport patterns.

**Vibration**

- CG3EM still allowed; A2VM 8 allowed diagnostically; A2VM 7 and unsupported models rejected.
- Exact 64-byte Apply/Restore packets, exactly one write per explicit action; no SyncToROM.
- No mutation with Center M not Disabled, missing/ambiguous/weak physical control HID, unavailable physical owner, or cancellation.
- A2VM frontend result text does not claim CG3EM hardware validation.
- Production A2VM vibration apply remains blocked and normal user settings are unchanged.

**LED**

- Build the exact `0x024A` candidate ReadProfile 64-byte request.
- Parse a valid 64-byte response; reject wrong report ID, command, index, base address, length, truncated reply.
- Bound read timeout/no-reply, denied permissions, missing HID, mismatched firmware/model, and cancellation => no LED profile write.
- Verify `TryResolveRgbAddress(0x0230)` still fails in production and that the existing known EX/CG3EM address table is unchanged.
- Developer frontend RPC roundtrip (request/result/unsupported fallback) and UI action wiring.
- All test cases remain offline/deterministic; no hardware access, kernel drivers or local device required.

**Regression**

- Run existing build and relevant unit/frontend/UI tests.
- No production controller presentation, HidHide, VIIPER, rumble lifecycle, or normal LED/vibration regression.
- Practical failures only; do not introduce new locks/epochs/authority objects to defend speculative interleavings.

## 9. User-operated physical test plan (after merge, informational only)

**Local Codex/CI/PR review must NOT require completing this section.** A code-correct PR may merge before hands-on acceptance. The user will perform this separately.

Preconditions: current MSI Claw 8 AI+ A2VM, composite PID1902 restored, `MI_00&COL01` and `MI_00&COL02` present, firmware `0x0230`, Center M disabled, Full1902 physically owned, virtual presentation healthy.

1. **Vibration:** Developer -> Vibration Test -> Apply 0/100. Observe HID write result; press independent physical Left Test and Right Test. Record audible/tactile outcome. Explicitly Restore 50/50; retest. If restore fails, record state and use the explicit Restore action only after confirming conditions, not an automatic repeat loop.
2. **LED:** On the same session, click Read `0x024A` Profile. Save full probe log/result; verify expected reply header, index/address match and sensible parsed block. **No LED mutation occurs in this PR**, so LED color change is not an expected outcome at this stage.
3. **Safety smoke:** Confirm normal Xbox360/BPM SteamDeck input and current ownership are still healthy. Physical experiments remain user responsibility and not PR blockers.

Report actual evidence back into `docs/RE_MSI_ControllerVibration.md` and `docs/RE_MSI_Joystick_LED.md` afterward. Do not label A2VM production-verified until physically observed and separately approved.

## 10. Expected file scope / PR size

Prefer edits to existing files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawVibrationStrengthClient.cs
    MsiClawLedController.cs
    MsiClawLedProtocol.cs (developer read-only builder/parser; preserve production table)

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/
    FrontendWire.cs
    NamedPipeAddonFrontendClient.cs
    NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/
    VibrationTestPage.xaml
    VibrationTestPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/
    MsiClawVibrationStrengthClientTests.cs
    ControllerLedTests.cs
    FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.UiTests/
    UiArchitectureTests.cs (only if needed)

docs/RE_MSI_ControllerVibration.md
docs/RE_MSI_Joystick_LED.md
```

No new UI framework, diagnostic service, general firmware inspector, PowerShell tool or separate PR for each feature. Production A2VM enablement remains **out of scope** and should be done only once real hardware evidence justifies it.

## 11. Completion criteria for this diagnostic PR

- Developer UI exposes both A2VM probes in one existing page.
- A2VM 8 can explicitly issue and log one bounded motor-pair test/restore; CG3EM still works.
- A2VM firmware `0x0230` can explicitly query the single documented `0x024A` LED candidate and receive a typed validation result.
- Both operations are limited to the correct PID1902 control HID under current owner/authority.
- A2VM production LED/vibration writes remain blocked.
- Tests/build are successful, no realistic lifecycle regression.
- **No mandatory physical-hardware pass before merge.**
