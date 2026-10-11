# Work Order — Full1902 M1/M2 PR1: A2VM + EX Read-Only Firmware Profile and Raw DirectInput Diagnostics

**Date:** 2026-10-11  
**Repository:** `onehoon/SteamAddonforClaw`  
**Reviewed baseline:** `main@64655a8ce0552dba76be3a2d2f13ca6c98eea8c7` (includes merged Edge fix PR #756). Recheck latest `main` before implementing.  
**Implementer:** Local Codex. **Physical validation:** user **after merge**; do not require device testing for the PR, review approval, or CI.  
**Deliverable:** **ONE focused PR** for Developer-only diagnostics. This is the *first PR of the new firmware investigation*, **not** the already completed `FULL1902_M1_M2_XBOX360_MAPPING_PR1`.

## 1. Product decision and incident

MSI Claw 8 AI+ **A2VM** running controller firmware **`0x0230`** has user-confirmed M1/M2 nonfunction in *both* normal Windows/Xbox360 and Steam/DualSense Edge presentation. In build `0.1.362.0`, the already-owned DirectInput poll completed a 381,835 ms observation with `M1Observed=False`, `M2Observed=False`, `ReadFailures=0`. These two values mean the *currently assumed* DirectInput indices never became true in that session; by themselves they do **not** prove that no other index or keyboard/firmware output was generated.

The user's Claw **EX (CG3EM)** works with the same Addon mapping. Compare both models and allow the diagnostics to remain useful when future firmware changes their physical profile.

**Goal:** collect **two independent pieces of non-mutating evidence on the same owned PID1902 physical controller**:

1. Firmware: read and report the existing **M1 and M2 profile slots** with model/firmware, original bytes and cautious classification.
2. Input: for a short user-invoked window, observe which **raw DirectInput button indices transition** when the user presses/release M1 and M2; use the **existing owner poll**, never a second reader.

**PR1 must NOT:** send `WriteProfile 0x21`, `SyncToROM 0x22`, `SwitchMode 0x24` or any other controller mutation; repair/reset EEPROM; change the physical button index 15/16; change Xbox360 user mappings; change Steam Deck R4/L4 or Edge rear LB/RB bits; change VIIPER, HidHide, Center M authority, controller mode, LED/vibration, gyro, startup/boot-rumble, Suspend/Resume, teardown, uninstall or PID1901 restoration. **Automatic firmware normalisation is explicitly PR2**, after the user provides PR1's hardware evidence.

The normal Full1902 app is **standalone**. HHC/CTW are **research references**, never product dependencies.

## 2. Required document and source authority

Read the full current design, respecting `docs/Full 1902 Implementation/README.md` precedence:

- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/RE_MSI_PID1902_M1_M2_BackButton.md`
- `docs/work-order/FULL1902_M1_M2_XBOX360_MAPPING_PR2_LIVE_OUTPUT_WORK_ORDER.md` (historical *software* mappings)
- `docs/work-order/2026-10-09_A2VM_LED_VIBRATION_DEVELOPER_DIAGNOSTICS_WORK_ORDER.md` (existing bounded HID read-only diagnostic pattern)
- `docs/work-order/2026-10-11_A2VM_INITIAL_CONTROLLER_FAST_PATH_HHC_CTW_SIMPLIFICATION_WORK_ORDER.md`

External source evidence inspected:

- **HHC**, `Valkirie/HandheldCompanion`: `HandheldCompanion/Devices/MSI/ClawA1M.cs`, `ClawA2VM.cs`, `ClawCG3EM.cs` and `HandheldCompanion/Controllers/MSI/DClawController.cs`. A2VM and CG3EM **both inherit** the common ClawA1M implementation. `DClawController` maps M1/M2 to DirectInput `Buttons[15]` / `Buttons[16]`. `ClawA1M.Open()` actually *writes* `GetM12` for each button, runs `SyncToROM` and re-applies mode. `GetM12(useM1, xinput=false)` is a short `0x21 0x01` write (payload `02 01 00`), using firmware table addresses such as `0x00BA` / `0x0163`. **Those writes are reference for a future PR only; they are forbidden in PR1.**
- **CTW/MSI Center M research**: [RE_MSI_ButtonRemap.md](https://drive.google.com/file/d/1Xgf9lE2LaaIwAoPLekPCrNvKXtIzYpMN/view) and [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view). On-device A2VM `0x0229` and EX `0x0414` research found matching M1/M2 slot locations. **Corrected multi-byte layout** is `01 04 0C <action...>` and setting only `+3` action byte can leave a missing `04 0C` marker and a nonfunctional paddle. EX's researched factory M1 may be `KB_F22` (`0x9D`), **not** necessarily A2VM's `0x11`.
- Historical research observed the MSI `0x04` ReadProfile command. It describes the `0x04 00` request/index from Center M logs, whereas the Addon's established PID1902 LED read probe uses `0x04 01`. **Do not silently assume these selector/index variants are interchangeable.** A2VM `0x0230` and arbitrary future EX firmware response formats are *not yet proven* for these slots.

Current Addon code verified:

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHardware.cs`: `M1DirectInputButtonIndex = 15`, `M2DirectInputButtonIndex = 16`, expected VID/PID `0DB0:1902` and control UsagePage/Usage `FFF0/0040`.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs`: **the one owned** 125/250 Hz DirectInput `PollWorker` and existing `M1/M2 input verified` logs; its `RunPid1902InputCadenceDiagnosticAsync` shows how a temporary collector can attach to an existing session. Do not alter default cadence or duplicate the DirectInput acquisition.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`: only after `LiveInputSource`, `OwnedPhysicalIdentity` and exact HidHide proof is controller input admitted; actual startup, fast A2VM 1901→1902 mode writes and recovery are owned here, not by diagnostics.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs` / `MsiClawLedProtocol.cs`: **existing exact control-HID lookup, attributes, bounded `WriteAndReadAsync`, response validation and classified outcome** pattern. Use it as precedent without coupling the button reader to the LED feature.
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs`: `IMsiClawRawHidTransport.WriteAndReadAsync` opens one exact path and executes bounded request/replies on that one handle.
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`: `CaptureLiveOwnedPhysicalIdentityForDeveloperProbe` and `RunA2vmLedProfileReadProbeAsync` already provide Runtime-authoritative admission; `RunPid1902InputCadenceDiagnosticAsync` forwards to the already-owned input source.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`, `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`, `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs` / `NamedPipeAddonFrontendServer.cs`, `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml[.cs]`: follow the existing explicit Developer diagnostic button/front-end wire pattern.
- Existing tests: `tests/SteamInputAddonforClaw.Tests/ControllerLedTests.cs`, `MsiClawInputSourceTests.cs`, `MsiClawInputCadenceDiagnosticTests.cs`, `FrontendNamedPipeTransportTests.cs` and corresponding UI tests.

## 3. One explicit Developer diagnostic action — no background auto-run

Expose **one** simple Developer menu card; example:

```text
M1 / M2 Physical Input & Firmware Profile (Read Only)
[Run 10s Diagnostic]
Press and release ONLY M1, then ONLY M2, one at a time.
Result: firmware read result; raw index transitions; observed indices 15/16.
Detailed bounded results are recorded in Addon logs.
```

- Developer menu only; not in Xbox360 mapping settings, Steam Settings, Overlay, normal startup, profile activation or boot.
- One explicit invocation, single in-flight guard, cancellation and clear `Unavailable` / `Failed` / `Completed` status. Use existing Runtime/front-end contracts and the current named pipe. Avoid an unbounded UI/report feature or separate diagnostic process.
- The requested **10 s** window applies to raw input capture. Firmware reads are at most two bounded queries; a busy/error result must not leave the UI waiting indefinitely.
- The button action first captures the current physical owner and exact model. Read results and raw input results are **independent**; a profile-query failure should not suppress a safe still-owned raw-input diagnostic (and vice versa).
- Exposing this through Developer UI requires only the minimal analogous contract/RPC/controller glue. Do not introduce a new global inspection service, daemon, persistent cache or extra manager.

## 4. Firmware profile slot query (no EEPROM mutations)

### 4.1 Strict admission

Every hardware query must require the **currently admitted** Addon-owned session:

1. Center M startup state **exactly Disabled**, Runtime not stopping, no developer mode-cycle, firmware restore, Suspend or real physical recovery underway.
2. A healthy `_physicalOwnership.LiveInputSource` with `IsRunning == true` and a **strong** current `OwnedPhysicalIdentity` from the same owner. Do not run on arbitrary externally enumerated HID.
3. Exact recognized device model: `msi.claw.a2vm.8`, `msi.claw.a2vm.7` or `msi.claw.cg3em`. A2VM 7 receives **read-only diagnostic eligibility only** (no claim its factory bytes have been validated). Unsupported or indeterminate model returns `Unavailable`.
4. Resolve one strongly matching **current** PID1902 control-HID interface `VID=0DB0/PID=1902`, `UsagePage=FFF0, Usage=0040`, using the existing `MsiClawControlHidResolver` and `WindowsMsiClawModeWriter.SelectDeviceInformation` pattern; reject missing, ambiguity, weak identity or PID1901.
5. Retrieve `VersionNumber` from the exact resolved HID and record it. **Do not require version==0x0229/0x0230/0x0414** merely to read known slots; version is diagnostic evidence, not proof of compatibility with a future firmware.
6. Recheck live owner/Center M state immediately before issuing each bounded query. Stop if ownership/PnP/control device changed.

Read-only in this work order means **an output-format `ReadProfile 0x04` request that does not mutate profile**. It does not mean no `WriteFile` I/O call: HID transport uses `WriteFile` to transmit a read command. Enforce `opcode==0x04` at the diagnostic command builder; **no** production `WriteProfile` or `SyncToROM` path reachable.

### 4.2 Target addresses and request

Query exactly these **two** slot starts:

| Slot | PID1902 control-HID profile address | Research shape | Historical action code |
| --- | --- | --- | --- |
| M1 (right rear) | `0x00BA` | `01 04 0C <code(s)>` | A2VM independent `0x11`; EX could be `0x9D` (F22) |
| M2 (left rear) | `0x0163` | `01 04 0C <code(s)>` | A2VM independent `0x12` |

Use a **fixed bounded slot read**, e.g. **8 bytes each**, sufficient for the 4-byte marker and remaining mapped action bytes, rather than reading/dumping the entire EEPROM. Build **64-byte zero-padded** `ReadProfile` query packets, with report prefix and bytes derived from `MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest`:

```text
0F 00 00 3C 04 <read-index> 00 BA 08 ... zeros     // M1
0F 00 00 3C 04 <read-index> 01 63 08 ... zeros     // M2
```

**Read-index decision:** do not infer this from a code-byte offset or hardcode two speculative fallback commands. First inspect checked-in Addon PID1902 `0x04 01` read implementation and the CTW Center M `0x04 00` RE evidence. In PR1 choose **one explicitly documented selector** supported by the current PID1902 transport evidence (prefer the *existing* Addon `0x04 01` query encoding), and record the selector in the result. If either model's reply does not validate, record `QueryUnverified` / `NoReply` and let the user supply post-merge logs. No silent protocol brute-force, more than two requests, or aggressive retry loops.

For each slot, reuse `WriteAndReadAsync` with bounded report length, max responses and timeout (existing LED probe uses 64-byte replies, max 4 and 750 ms). Read M1 and M2 through the same exact owned control interface; do not open another path for a second source.

### 4.3 Response parsing and classification

Implement a narrow parser near a small `MsiClawBackButtonProfileDiagnostic` (exact class name up to Codex). It must check response envelope / report ID / operation ACK, echoed selector/address/length where available, status and sufficient payload length **before** calling data valid. **Do not blindly copy the LED-specific `response[11] == 0x09` validation** into a back-button parser; that byte was observed for the LED block and has not been proven a universal slot prefix. Confirm the request/response format against current source; do not manufacture a readback layout.

Allowed result categories should remain **descriptive**, e.g.:

- `ReadbackParsed` / `HistoricalMarkerPresent` (valid matching response; marker `01 04 0C` observed; report the raw action bytes without assuming they must be `11/12` on EX);
- `ReadbackParsed` / `HistoricalMarkerAbsent` (valid matching response and different payload; **not yet proof firmware is broken**, could be another valid format);
- `ReadbackParsed` / `OtherFormat`;
- `NoReplyOrTimeout`, `UnexpectedReport`, `WrongAddressOrIndex`, `TransportUnavailable`, `OwnerUnavailable`, `UnsupportedModel`, `Cancelled`.

The only confirmed fact from this query is a **bounded matching profile read**, *not* confirmation that either paddle actually emits independent DirectInput input. EX's factory `KB_F22` is **not automatically a defect**. Similarly, a matching `01 04 0C 11` marker is not sufficient to conclude the firmware is working. Do **not** label any observed value `repair required` or automatically modify firmware.

Record, at INFO level **once per explicit diagnostic**, preferably one result per slot plus summary:

- Model ID (exact), firmware `0xXXXX`, PID and control usage, slot name, address, read selector, requested size, result/classification, bounded original payload hex (8 bytes), failure reason, elapsed ms, `FirmwareMutationIssued=False`.
- For unmatched packets, include a **short bounded response prefix** and reason; no entire EEPROM dump, no per-poll report spam, no machine serial/path/PnP IDs in user-facing status.
- Keep logs distinct from `ControllerLedProfileReadProbe*`; add `ControllerBackButtonProfileReadStarted/Completed` or similarly unambiguous event names.

## 5. Raw DirectInput button-transition diagnostic (10 s)

### 5.1 Must observe **RAW**, not only normalized M1/M2

The existing code assumes:

```csharp
MsiClawHardware.M1DirectInputButtonIndex = 15;
MsiClawHardware.M2DirectInputButtonIndex = 16;
```

These indices are backed by HHC and the current models, but the A2VM `0x0230` report has shown neither becoming true. Therefore capture the **raw `DirectInputState.Buttons`** array from the **same `MsiClawInputSource.PollWorker` immediately after the existing valid-layout/initial-sample checks**, not from `ControllerState.Auxiliary` alone.

**Hard requirement:** keep *exactly one* DirectInput input owner, `ReadState()` caller and publisher. **Do not** add an independent DirectInput diagnostic thread, second device enumeration+Acquire, polling timer or input driver hook. Reuse the transient collector approach from `RunPid1902InputCadenceDiagnosticAsync`, but **do not switch normal cadence to its 1 ms oversampling mode** merely to collect button changes.

### 5.2 Collector and bounded evidence

- Run for **10 seconds** only after explicit Developer action. Sample each already-obtained raw `Buttons` state; store the previous button snapshot (or equivalent compact bitmask), count/record **rising and falling edges by original zero-based DirectInput button index**. Compare stable count bounds: reject changing/invalid layouts, and cap interesting indices and reported events (e.g. first **64** indices, max **32** event log lines). Keep total per-index counts for the summary even if event-line cap is reached.
- Record observed physical `ButtonCount`, indices with transitions, rising/falling counts, `15`/`16` pressed/released observed, and whether each was seen **independently**. Keep first/last state as diagnostic evidence if useful. Avoid logging every sample or unrelated axis values.
- If no indices change, report precisely `NoRawButtonTransitionsObserved`, **not** `FirmwareBad`. If an unexpected index changes, record it as a *candidate* input; because physical buttons are manually exercised, the software cannot prove which real paddle caused a given edge without user testimony.
- Present a brief instruction **"press/release M1 only, then M2 only; do not touch any other controls"**. The 10 s bounded collector should be sufficient to check two separate press/release pairs; do not require test-mode publisher changes.
- No user-configurable index selection or persistent calibration mapping in PR1. Future PR2 can consume confirmed evidence.
- When physical session stops, is disposed, loses current ownership or the explicit action is canceled, complete/cancel collector promptly and release it. Do not retain diagnostic references across recovery or source restart; **do not delay physical shutdown, Overlay, native teardown or resume** for the 10 s test.
- Existing `StateChanged`, `LatestState`, normal `M1/M2 input verified`, publisher, cadence, fault classification and recovery behavior must remain unchanged. The diagnostics are observation only.

### 5.3 Test injection and concurrency

The collector can be a tiny internal type or nested session-specific data class. Prefer the existing `InputSession` diagnostic field + bounded completion model; it can coexist with normal polling. If the cadence diagnostic already occupies the test lane, return `AlreadyRunning` or explicitly reject overlapping runs instead of layering two collection modes. **No new global mutex, epoch, manager or background work queue**.

A user pressing Developer buttons or closing the UI while source shuts down is a normal lifecycle: cancellation/cleanup must not leave the UI permanently busy. Do not add defenses for hypothetical instruction-by-instruction race crossings unsupported by actual lifecycle.

## 6. Frontend contract, result visibility and action isolation

Use the existing `FrontendPid1902InputCadenceResult` / `FrontendControllerLedProfileReadProbeResult` conventions. A single `FrontendMsiBackButtonDiagnosticResult` or similar small DTO can include both independent outcomes and the raw index summary; **avoid a generic arbitrary-HID dump contract**.

The one explicit Developer action may:
- capture model and live owner;
- issue two bounded profile read requests;
- attach the 10 s raw collector to the existing poller;
- return a concise status of both parts, with detailed structured INFO diagnostics to logs.

**If the profile read fails** while the valid physical input session survives, still allow the raw 10 s collector. **If the input source is no longer owned** when the raw step starts, return its own `Unavailable` and do not open an unowned device as a workaround.

Keep developer operations disjoint from active mode cycling, rumble re-arm, device restore and profile writes using the existing front-end diagnostic admission pattern; do not modify any other feature's lifetime or behavior. Avoid blocking the runtime startup or ordinary gamepad use on the diagnostic response.

## 7. Unit and contract tests (no physical test gating)

Implement focused deterministic tests in the existing managed/UI test projects. Mandatory cases:

1. Profile read request is **exactly two** 64-byte `0x04` commands at `0x00BA` and `0x0163`, bounded `len=8`, with the selected/read-index reflected correctly; **zero `0x21` WriteProfile, zero `0x22` SyncToROM, zero `0x24` SwitchMode**.
2. A valid matching response extracts 8 original bytes and recognizes historical marker without assuming `0x11/0x12` are universal; an EX `01 04 0C 9D` sample is diagnostic evidence, not a repair instruction.
3. Wrong ACK, address, selector, length, response size, timeout, unrelated report, cancellation, weak identity, ambiguous control HID, wrong PID, Center M Enabled, unsupported model, no owned live session: no unsafe read/mutation and an explicit classified outcome.
4. Model/FW: A2VM 8 `0x0230` and EX `0x0414` permit strictly diagnostic reads, no firmware-version-specific auto-write; A2VM 7's status is explicitly **research-only**.
5. Raw button collector: feed synthesized **already-read** array snapshots; verify independent `15`/`16` press/release, candidate other indices, no-change, short/invalid arrays, bounded logs, cancellation, immediate stop on session end and cleanup.
6. Ensure diagnostic attaches to the existing `PollWorker` and does **not** call another `ReadState` or second `Acquire`. Unchanged normal state mapping and publisher input.
7. UI/RPC: single invocation, one-in-flight protection, both independent outcomes visible, no accidental UI/frontend mutation and bounded completion. Existing LED read, input cadence and presentation tests stay green.

Run repository-standard managed build and tests plus VIIPER dependency consistency checks as relevant. No CI, reviewer or Codex hardware requirement. The user will validate A2VM `0x0230` and normal EX after this read-only PR merges.

## 8. Evidence to request from the user **after merge** (not Codex)

For each machine, the user will run Developer → M1/M2 diagnostic and **press/release M1 only, then M2 only** during the 10 s window, then provide the Addon log. Capture:

| Comparison | A2VM 2.30 | EX current and future firmware |
| --- | --- | --- |
| Exact model / controller firmware | To collect | To collect |
| PID1902 profile read validity | To collect | To collect |
| M1 slot 0x00BA, 8 raw bytes | To collect | To collect |
| M2 slot 0x0163, 8 raw bytes | To collect | To collect |
| Raw DirectInput indices with edges | To collect | To collect |
| Explicit 15/16 press/release | To collect | To collect |
| Any unexpected candidate index | To collect | To collect |

The next **PR2** is a **separate decision after comparing the evidence**: if a verified firmware profile is the cause, implement the narrow HHC/CTW-derived conditional prepare/repair for A2VM **and EX** with exact owner and readback. If profile bytes match but raw 15/16 do not change, investigate the actual DirectInput/keyboard presentation rather than writing EEPROM speculatively.

## 9. Merge-ready criteria and non-goals

**Merge-ready when:** normal Full1902 boot, Windows/Xbox360, Steam Deck/Edge, actual physical source ownership, HidHide/VIIPER lifecycle, Sleep/Hibernate/Resume and shutdown/recovery are **unchanged**; the Developer-only action emits bounded, clearly classified profile/raw-input evidence and unit/contract tests pass. **No hardware execution required to merge.**

No hardware EEPROM mutation, speculative F22-to-0x11 reset, silent profile.rec edits, extra PnP enumeration authority, own reader thread, global HID interface manager, second state authority, future-firmware guessing table or theoretical race-state machine. Favor the smallest code path that can answer **"what is stored in each slot?"** and **"which raw input indices actually change?"** for A2VM and EX.