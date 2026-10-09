# Work Order — A2VM 8 Vibration Strength + Joystick LED Production Enablement (One PR)

**Date:** 2026-10-09  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementer:** Local Codex  
**PR count:** Exactly **one** focused implementation PR  
**Scope:** A2VM 8 / MS-1T52 Full1902 hardware feature enablement, LED read parser fix, focused tests and RE documentation  
**Hardware test ownership:** The **user** performs physical acceptance **after code merge**. It is **not** a Codex task prerequisite, required CI test, or PR-review blocker.

## 1. Decision and evidence boundary

The user physically tested the **A2VM 8 (model `msi.claw.a2vm.8`, PID1902, observed USB firmware `0x0230`)** on 2026-10-09 after PR #729:

### Vibration: physical functionality verified

Developer Apply `Left=0, Right=100` sent:

```text
0F 00 00 3C 21 01 00 22 02 00 64 ... (64-byte output report)
ControllerVibrationProfileWriteProbeCompleted
Model=msi.claw.a2vm.8 TransportSucceeded=True
```

The **user observed that the left motor did not vibrate and the right motor did** during the separate physical motor tests. This demonstrates that the existing serialized pair write actually controls A2VM 8 motor gain, not merely that a HID write completed. The explicit Restore `50/50` command also completed at transport level; no claim of verified EEPROM persistence or a hardware-original `50/50` baseline is made.

**Production decision:** Enable the **existing** Controller -> Vibration Strength production flow for **`msi.claw.a2vm.8`** only. Keep `msi.claw.a2vm.7` unsupported; CG3EM remains supported.

### LED: physical profile read proved, actual write/visual behavior still untested

Developer `ReadProfile` requested profile index 1, candidate address `0x024A`, length `0x20` on actual firmware `0x0230`:

```text
TX: 0F 00 00 3C 04 01 02 4A 20 00 ... (64 bytes)
RX: 10 00 00 3C 05 01 02 4A 20 00 04 09 03 64 ...
```

The full bounded response prefix included **27 RGB data bytes**. At least **four** repeated calls returned the same structural response; the Runtime logged `Outcome=UnexpectedReport` because **PR #729's parser incorrectly demands `response[10] == 0x01`**.

The firmware actually returns:

| Byte / range | Meaning | Observed |
| --- | --- | --- |
| `[0]` | input report ID | `0x10` |
| `[3..4]` | MSI marker / ReadProfile reply | `0x3C 0x05` |
| `[5]` | profile index | `0x01` |
| `[6..7]` | echoed address | `0x02 0x4A` |
| `[8]` | echoed length | `0x20` |
| `[9]` | observed reserved/index field | `0x00` |
| **`[10]`** | **LED effect/mode** (NOT fixed ACK) | **`0x04`** |
| **`[11]`** | **static header constant** | **`0x09`** |
| `[12]` | speed | `0x03` |
| `[13]` | brightness | `0x64` (100) |
| `[14..40]` | 9 × RGB triplets | returned |

The protocol's ReadProfile **ACK is `response[4] == 0x05`**, not `response[10] == 0x01`. This is consistent with `docs/RE_MSI_Joystick_LED.md`'s newer block layout, notwithstanding its older schematic example that used effect `0x01`.

The historical A2VM firmware `0x0229` and `0x0308` both resolve RGB base `0x024A`; the **real `0x0230` ReadProfile response confirms that this base hosts the expected LED profile data layout**.

**Production decision, authorized by user for this PR:** Add **narrow static LED production support** for **`msi.claw.a2vm.8` + firmware `0x0230`** using the existing four-packet Static writer. This is a **controlled extrapolation from valid profile readback plus historical static protocol evidence**, **not a claim that physical LED writes were already tested**. The user will verify actual LED behavior after merge. Do not enable other A2VM SKUs/firmwares or animations.

No new HID scripting, firmware discovery, extra daemon or separate diagnostic PR.

## 2. Read architectural authority before editing

Read these **current repository** documents and preserve their precedence:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
6. `docs/RE_MSI_ControllerVibration.md`
7. `docs/RE_MSI_Joystick_LED.md`
8. `docs/work-order/2026-10-09_A2VM_LED_VIBRATION_DEVELOPER_DIAGNOSTICS_WORK_ORDER.md`
9. `docs/work-order/FULL1902_CONTROLLER_VIBRATION_PRODUCTION_PERSISTENCE_LIFECYCLE_WORK_ORDER_2026-10-04.md`
10. `docs/work-order/FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md`

Later, **verified 2026-10-09 hardware evidence and this work order supersede the older "A2VM 8 unverified" statements** for exactly the functionality and firmware stated here.

Product assumptions: **standalone Full1902 app**, **one Windows user**, **one interactive session**. No CTW integration, Center M coexistence, RDP / fast user switching / multi-session compatibility architecture. CTW/HHC are only historical RE sources.

Preserve real lifecycle safety: Sleep/Hibernate/Resume, Runtime restart/crash/shutdown, physical loss and PnP re-enumeration, fail-close controller authority, HidHide ownership, VIIPER native owner/teardown, and stock PID1901 restoration. Do **not** add epochs, redundant locks, wrappers, inspectors, duplicated authority or generic policy abstraction just for theoretical interleavings.

## 3. Part A — Turn on A2VM 8 production vibration strength

### 3.1 Minimal model-policy change

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationProfilePolicy.cs`

Replace the current CG3EM-only policy with the two **physically tested** models:

```csharp
internal static bool IsProductionPairWriteVerified(HandheldDeviceModelId modelId) =>
    modelId.Value is "msi.claw.cg3em" or "msi.claw.a2vm.8";
```

`msi.claw.a2vm.7`, unknowns and other models remain false. Update `GetProductionUnavailableReason` so it no longer claims A2VM 8 is unverified; A2VM 7 remains accurately described as unavailable. Keep the existing model-only policy semantics for CG3EM; do not add a new firmware/role authority merely because the tested A2VM happened to report `0x0230`.

### 3.2 Reuse existing production implementation without rewriting it

`MsiClawVibrationStrengthClient.ApplyAsync` already:

- Validates saved `LeftPercent/RightPercent` are 0..100.
- Requires a strong, same-physical owned PID1902 identity and exactly one vendor control HID `0xFFF0/0x0040`.
- Serializes with the existing `_transactionGate`.
- Sends **one** `MsiClawVibrationProfileCommand.BuildMotorPairWrite(left, right)`.
- Returns feature-local failure; never sends `SyncToROM`.

Do not introduce a second writer or new write contract.

The exact packet remains:

```text
0F 00 00 3C 21 01 00 22 02 <LeftPercent> <RightPercent> [zero-fill to 64 bytes]
```

**Caution:** Byte `0x22` at offset 7 is the **profile data offset**, not the separate `SyncToROM` opcode. Never issue a separate firmware `0x22` sync.

Existing `settings.json` persisted whole pair, default `50/50`, debounced controller UI sliders, and left/right physical motor Test buttons must be reused unchanged. Do not create a new setting or change normal rumble forwarding.

### 3.3 Existing production lifecycle applies automatically

The current host `ApplyOwnedControllerVibrationSettingsAsync` and frontend `CaptureControllerVibrationSnapshot` derive support from the same production policy. Therefore after Part A:

- Controller -> Vibration Strength becomes available on A2VM 8.
- A user slider edit saves and best-effort applies the pair.
- Startup applies the persisted pair after a verified live physical PID1902 ownership session.
- Successful real physical recovery/PnP return reapplies the pair.
- Sleep/Hibernate Resume uses the existing delayed physical-setting reapply.
- **No writes** for Xbox360 ↔ SteamDeck presentation switches, BPM changes, status refresh, UI open/close.
- Failures do not roll back settings, change PID, reconfigure HidHide or detach VIIPER.

Review the existing tests and lifecycle helpers, but avoid changing these host paths unless a **real implementation defect** is discovered.

### 3.4 Developer diagnostics

Keep the Developer 0/100 and explicit `Restore Addon default 50/50` tools as separate user-invoked diagnostics. Update any outdated wording asserting A2VM 8 is production-unverified while keeping the distinction between transport success and actual physical result in the diagnostic UI. The user-verified motor asymmetry can now be documented; do not assert device-side EEPROM durability.

## 4. Part B — Fix A2VM LED read response parsing

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedProtocol.cs`

PR #729 currently incorrectly assumes `response[10] == 0x01` and parses `Effect=response[11]`. Fix this narrow parser. The following is **illustrative code**; retain current request/response types and other validation:

```csharp
// Validate report length, report ID 0x10, marker 0x3C,
// reply opcode 0x05, index 1, echoed 0x024A address,
// echoed 0x20 length, and the observed block structure.
if (response.Length != 64
    || response[0] != 0x10
    || response[3] != 0x3C
    || response[4] != 0x05
    || response[9] != 0x00
    || response[11] != 0x09)
    return CandidateReadParseOutcome.UnexpectedReport;

var address = (ushort)((response[6] << 8) | response[7]);
if (response[5] != 0x01 || address != 0x024A || response[8] != 0x20)
    return CandidateReadParseOutcome.WrongAddressOrIndex;

readback = new(
    response[10],                    // LED effect (0x04 is valid)
    response[12],                    // speed
    response[13],                    // brightness
    response[14..41].ToArray());     // exactly nine RGB triplets
return CandidateReadParseOutcome.CandidateReadbackParsed;
```

Preserve the existing one outgoing request / up to four incoming reports / 750 ms total timeout behavior introduced in PR #729. Do not reinstate a one-report false-negative.

Required fixture derived from **actual firmware response**, not a synthetic fixed-static frame:

```text
10 00 00 3C 05 01 02 4A 20 00 04 09 03 64 ...
```

It must parse as `CandidateReadbackParsed` and return `Effect=0x04`, `Speed=0x03`, `Brightness=100`. Retain the existing static-effect `0x01` test, and reject malformed header, wrong report ID, command, index, address, length, bad header constant, and short reports. **Do not require effect to equal 0x01.** Do not loosen identification or accept unrelated reports as a valid reply.

Update `MsiClawLedController` log interpretation; preserve bounded byte prefixes, concise structured outcome, and read-only Developer probe. It must show a successful parsed **read**, not falsely claim that the LED was already seen to change.

## 5. Part C — Enable exact A2VM 8 firmware 0x0230 Static LED production writes

### 5.1 Narrow RGB profile address admission

Add one exact firmware mapping:

```csharp
0x0230 => 0x024A
```

to the existing `MsiClawLedProtocol.TryResolveRgbAddress` table (or equivalent exact mapping).

**Do not** infer a generic A2VM firmware range, use a nearest-version fallback, probe arbitrary addresses, or infer that a VID/PID alone establishes the tested model. The only newly permitted target is **A2VM 8 / MS-1T52 with verified device attributes `VersionNumber=0x0230`**.

**Important model guard:** `MsiClawLedController.ApplyAsync` currently knows firmware and owned physical identity but not the **MSI model ID**; only extending the global firmware address table would make **every** MSI model with `0x0230` firmware eligible. Add the smallest model-specific gate at the existing production apply boundary:

```csharp
if (attributes.VersionNumber == 0x0230
    && modelId != "msi.claw.a2vm.8")
    return Fail("UnsupportedFirmwareForModel");
```

Choose the simplest signature/constructor change that can receive the already-detected model ID. The host already has an `MsiClawVibrationStrengthClient.ModelId` representing the startup hardware model, so it may reuse `_controllerVibrationStrengthClient?.ModelId ?? "unknown"` instead of introducing a parallel persistent model authority. Pass the actual current model at the call site. A2VM 8 must additionally match the existing exact/strong PID1902 physical owner and current vendor HID attributes.

Avoid breaking previously supported firmware mappings on CG3EM and other devices. Avoid new policy classes, managers, special service lifetimes or extra hardware locks. Keep the method signature and focused tests coherent.

### 5.2 Keep the production Static writer identical

Reuse `MsiClawLedProtocol.TryBuildStaticWrites` and `MsiClawLedController.ApplyAsync` four-write Static behavior:

1. Profile header + first 27-byte frame at `0x024A`.
2. Raw 27-byte identical RGB frames at `0x024A + 32`, `+59`, and `+86`.
3. The current static header mode `0x01`, constant `0x09`, speed `0x03`, requested brightness or `0` for Off, and nine uniform RGB triplets.
4. **Never** send `SyncToROM` / standalone opcode `0x22`.
5. Stop on the **first** failed HID write; log `ProfileWriteFailed:<packetIndex>`; do not automatically retry, reset the controller or roll back the persisted user LED setting.

**Keep all existing LED feature semantics unchanged**: On/Off, single Static RGB color, brightness 0..100, saved default Off, SettingsStore save-first behavior, existing Controller page and Overlay presentation. No animation editor, per-zone UI, profile EEPROM persistence or new Windows layer.

The four-write sequence is already the shipped Static path for exact other firmware mappings and is supported by historical firmware/protocol evidence. Do not duplicate it as an A2VM-specific writer.

### 5.3 User-approved provisional hardware acceptance boundary

The **real ReadProfile answer identifies a profile block at 0x024A**, but a read alone does not physically validate that the four writes produce desired LED effects. The user **explicitly requested production inclusion in this one PR** despite that remaining hardware observation.

Accordingly:

- Implement production support with **strict exact model/firmware admission**, fail-closed unknowns and existing non-persistent Static writes.
- Do **not** label LED Static output as **physically verified** in docs/tests/PR description.
- Do **not** add a mandatory pre-merge hands-on LED test.
- Do **not** invent a generic run-time read-before-every-write, fallback address scan, multi-step restore state machine or speculative compatibility layer. Existing exact firmware/model admission plus operational failure handling is the bounded implementation.
- The user will do the initial color/brightness/Off acceptance after merge. If the firmware does not behave as expected, keep the issue strictly local to A2VM LED functionality; do not touch routing ownership or escalate writes.

### 5.4 Lifecycle boundaries

Existing `AddonProcessHost` hardware-settings apply seams already run once on:

- healthy physical ownership startup,
- **successful** physical recovery/PnP return,
- bounded Sleep/Hibernate Resume settle,
- an explicit Controller LED mutation.

Do not add an LED apply on VIIPER virtual presentation changes, an additional PnP watcher, a startup loop or an automatic new LED profile probe. Error in any individual LED write remains feature-local and must **never** prevent Xbox360/SteamDeck attach, fail-close routing, change the physical PID, or mutate HidHide.

This first production build may overwrite a pre-existing animated LED profile with the Addon's saved **Static/Off** preference at the next successful owned startup. That is the documented existing LED feature behavior; it is **not** evidence of a hardware failure. If A2VM LED unexpectedly misbehaves, cease further developer write experiments and inspect logs before considering any change.

## 6. Tests (software-only Codex/CI requirements)

Update focused tests in existing test files. Do **not** require access to an MSI Claw.

### 6.1 Vibration policy and production

- `IsProductionPairWriteVerified("msi.claw.cg3em") == true`.
- `IsProductionPairWriteVerified("msi.claw.a2vm.8") == true`.
- `IsProductionPairWriteVerified("msi.claw.a2vm.7") == false`.
- Unknown models still false.
- Controller vibration snapshot Available/Writable now projects A2VM 8 when Addon authority is active; stock/no-authority stays unavailable for mutation.
- A2VM 8 production write uses **the persisted arbitrary valid pair**, not only diagnostic `0/100`; verify 50/50 and one nondefault pair, exact 64 bytes, one write, zero SyncToROM.
- Reuse `_transactionGate`; unsupported/weak/ambiguous physical HID means **zero** writes; transport failure leaves saved desired pair intact.
- Existing CG3EM support unaffected.
- Startup, real physical recovery and Resume reapply the same persisted pair; no write on SteamDeck/Xbox360 virtual presentation transitions.

### 6.2 LED parser

- Parse actual observed A2VM report prefix with `0x04 0x09 0x03 0x64`: `CandidateReadbackParsed`; Effect=4, Speed=3, Brightness=100; exactly 27 RGB bytes.
- Static profile `0x01 0x09...` also still parses.
- Reject wrong length, header, reply opcode, profile index, echoed address, read length, reserved `[9]`, and unexpected `[11]`.
- Preserve up-to-4-reply selection of an exact matching response after unrelated Report ID `0x02` with exactly one request.
- Failure/no reply/cancellation produce no LED profile writes.

### 6.3 LED production

- `TryResolveRgbAddress(0x0230) == 0x024A`.
- No other existing exact firmware mapping changes; unknown `0xFFFF` remains unsupported.
- **Model guard:** A2VM 8 + `0x0230` + healthy strong PID1902 control HID may generate exactly four Static packets; **CG3EM or A2VM 7 reporting `0x0230` must not mutate**.
- Correct base + offsets `0x024A`, `0x026A`, `0x0285`, `0x02A0`; correct header and uniform 9-zone RGB data, OFF brightness 0, full-scale brightness, 64-byte reports.
- Failure at packet 1/2/3/4 stops later sends; no SyncToROM; no change to existing saved setting on failure.
- Existing CG3EM/known-firmware LEDs behave exactly as before; stock/no owner does zero LED writes.
- Runtime PnP/Resume/startup apply remains feature-local; no VIIPER/HidHide/physical-mode transitions on failure.

### 6.4 Build/review discipline

Run repository build, relevant unit/transport tests, and existing frontend/UI/lifecycle regression tests. Prefer modifying existing fixtures/tests over introducing a new testing layer. Fix compiler/test failures before opening the PR.

Only **realistic, material** defects block merge. Do not treat theoretical scheduler races, unsupported multi-session environments, or an unavailable physical MSI device in CI as blockers. User physical acceptance is separate.

## 7. Documentation updates (same implementation PR)

Update:

- `docs/RE_MSI_ControllerVibration.md`
  - Mark **A2VM 8 / MS-1T52** as user-verified for 0/100 motor asymmetry using PID1902 firmware `0x0230`.
  - Production model table: CG3EM + A2VM 8 enabled, A2VM 7 unsupported.
  - Clarify Restore 50/50 is the Addon default and no firmware durability was proven.
- `docs/RE_MSI_Joystick_LED.md`
  - Add the actual field response `10 00 00 3C 05 01 02 4A 20 00 04 09 03 64...`.
  - Clarify field `[10]` effect may be `0x04` and `[11]` is the `0x09` header constant; `[4]` is the reply opcode/ACK.
  - Add A2VM 8 `0x0230 -> 0x024A` as a **scoped enabled Static production path**, justified by verified readback but explicitly **pending physical LED write acceptance**.
  - Update older "0x0230 must remain unsupported" statements **in the current contract**, preserving historical notes as such. No misleading claim of already observed LED behavior.
- Update any affected UI-facing messages/test names that still say the A2VM 8 vibration mapping is unverified or that 0x0230 LED is universally unsupported. Keep UI scope minimal.

Do not create additional architecture docs, feature flags, firmware database, or migration machinery.

## 8. Suggested changed files

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawVibrationProfilePolicy.cs
    MsiClawLedProtocol.cs
    MsiClawLedController.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
    (only to pass the already-known MSI model to the existing LED production writer)

src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
    (only outdated diagnostic/status text if necessary)

tests/SteamInputAddonforClaw.Tests/
    MsiClawVibrationProfilePolicyTests.cs
    ControllerVibrationStrengthFrontendTests.cs
    ControllerLedTests.cs
    ControllerLedFrontendTests.cs (only if necessary)
    ControllerVibrationLifecycleContractTests.cs (only if necessary)

docs/RE_MSI_ControllerVibration.md
docs/RE_MSI_Joystick_LED.md
```

No new user-facing page, UI architecture, RPC method, device service, controller owner, HID transport, configuration store, driver change, or CI hardware test.

## 9. User physical acceptance checklist — after merge only

This section is **informational for the user**, not a Codex/CI/PR-review gate.

1. Confirm the same A2VM 8 / firmware `0x0230` is in owned PID1902 DirectInput mode and the virtual controller remains responsive.
2. **Vibration:** Using normal Controller page (not Developer diagnostic), set Left `0`, Right `100`, separately press physical Left and Right Test: left silent, right vibrates. Set 50/50 and retest. Change arbitrary strengths to check normal UI/persistence.
3. **LED:** Set Static On, a distinctive color, and medium brightness; confirm **both joystick rings** display the requested color. Then Off and On again; check saved color and brightness. If unexpected LED behavior occurs, stop issuing additional writes and provide logs.
4. Confirm persisted LED and vibration are reapplied after ordinary Runtime restart. Later perform optional Suspend/Resume and actual physical PnP recovery tests.
5. Confirm Steam BPM can switch Xbox360 -> SteamDeck -> Xbox360 without losing physical input.
6. Report any **LED physical behavior** explicitly back into `docs/RE_MSI_Joystick_LED.md` after this user acceptance; the PR must not claim the observation ahead of time.

**Do not merge-block on items 1–6.** The local Codex responsibility ends at correct code, offline automated tests and normal CI.

## 10. Completion contract

The one code PR is complete when:

- Production vibration strength on **A2VM 8** uses the existing persisted settings and lifecycle without modifying routing/rumble.
- LED ReadProfile accepts the observed `0x04` effect / `0x09` header and returns the correctly parsed value.
- Static LED output is enabled only for **A2VM 8 / `0x0230`** via the existing exact four-write path; previous models/firmwares are unchanged.
- Unknown firmware and unsupported models still fail closed with zero hardware writes.
- No SyncToROM, no arbitrary firmware write/scanning.
- Tests/build pass, and documentation states exactly what was physically observed versus merely inferred/read back.
- Physical LED write validation remains with the **user after merge**.

Do not split this into two feature PRs or defer normal production activation behind a mandatory second diagnostic PR.
