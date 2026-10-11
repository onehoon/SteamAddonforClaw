# Work Order — Full1902 M1/M2 PR2: Correct Profile Readback, Contain Developer UI Layout, and Add Explicit Manual DirectInput Restore

**Date:** 2026-10-11  
**Repository:** `onehoon/SteamAddonforClaw`  
**Predecessor:** merged PR #760 (`a4b8ea0e5c0a14052a74e2760876d9495f645ba1`), the read-only profile/raw-input diagnostic. Re-read latest `main` before implementation.  
**Implementer:** local Codex. **Physical validation:** user after merge. Hardware execution is NOT a PR review, CI, or merge prerequisite.  
**Deliverable:** ONE focused implementation PR comprising (1) reader/parser correction, (2) Developer UI result-layout fix, and (3) an explicit Developer-only *manual* M1/M2 native DirectInput restore experiment. **NO production automatic restore in this PR.**

## 1. Product scope and authoritative references

Full1902 is a **standalone** app. HHC, HHD, Linux hid-msi, and historical CTW/MSI RE are references, **not** CTW integration or new product dependencies. Read `docs/Full 1902 Implementation/README.md` and its four ordered authority documents before changing lifecycle boundaries:

- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-11_FULL1902_M1_M2_PROFILE_AND_RAW_INPUT_DIAGNOSTIC_PR1_WORK_ORDER.md`
- `docs/RE_MSI_PID1902_M1_M2_BackButton.md`; `docs/RE_MSI_Joystick_LED.md` for the established PID1902 ReadProfile envelope.

Current implementation entrypoints: `MsiClawBackButtonProfileProtocol.cs`, `MsiClawBackButtonProfileReader.cs`, `MsiClawInputSource.cs`, `AddonProcessHost.cs`, `WindowsMsiClawRawHidTransport.cs`, `MsiClawLedController.cs`, `MsiClawVibrationStrengthClient.cs`, `FrontendContracts.cs`, frontend named-pipe client/server, `DeveloperPage.xaml` and `.xaml.cs`. Reuse the existing owned HID resolver/transport and typed frontend action; no new reader process/second DirectInput Acquire/manager.

External source evidence:

- HHC `HandheldCompanion/Devices/MSI/ClawA1M.cs`, `GetM12(useM1, xinput:false)` emits a `0x21 0x01` write to DirectInput slot `0x00BA` / `0x0163` with length `0x02`, data `01 00`. HHC subsequently calls `SyncToROM (0x22)` and re-applies the gamepad mode. HHC uses these writes to make M1/M2 recognizable as buttons. Verify the current upstream code before using the template.
- HHD `src/hhd/device/claw/base.py` uses the same DirectInput *slot start* addresses but a DIFFERENT `length=0x05` payload (`01 00 00 12 00`) and ROM sync. Do not silently mix its payload with HHC's short write.
- Linux `drivers/hid/hid-msi.c` uses newer *remap subfield* addresses `0x00BB` / `0x0164` and the mapping codes `0x59 = KEY_RIGHTBRACE`, `0x58 = KEY_LEFTBRACE`. **These subfield addresses are NOT the HHC DirectInput slot starts**. Linux's `READ_PROFILE` also returns a command header/echo; do not treat byte 9 as universal zero status.
- The historical standalone MSI RE document reports `01 04 0C <action>` as another profile form. This must be preserved as separate evidence; it is not proof that A2VM `0x0230` uses that marker. Do not infer a universal repair template by splicing the Linux, HHC, HHD and old RE formats.

## 2. Actual post-PR760 A2VM 8 evidence

User tested version `0.1.365.0`, A2VM 8 firmware `0x0230`, PID1902 strong physical ownership, exact control usage `0xFFF0/0x0040`. Timestamp `2026-10-11 22:25:26-22:25:36 +09:00` in the attached runtime log. Both read requests were transmitted and answered:

```text
M1 @ 0x00BA: 10 00 00 3C 05 01 00 BA 08 01 04 00 59 FF FF FF FF 00 ...
M2 @ 0x0163: 10 00 00 3C 05 01 01 63 08 01 04 00 58 FF FF FF FF 00 ...
```

PR760 currently rejects both as `UnexpectedReport`, because it requires `response[9] == 0x00` and slices the payload from byte 10. Here `response[9] == 0x01` is the FIRST PROFILE DATA BYTE, not a zero status byte. The reported 8-byte payload from byte 9 is:

```text
M1: 01 04 00 59 FF FF FF FF
M2: 01 04 00 58 FF FF FF FF
```

These are strong *candidate* Linux-keyboard mappings; they coincide exactly with Linux's right/left brace codes. Do not claim this proves which SteamOS component wrote the ROM, or that every supported model shares the same factory profile bytes. Meanwhile the existing owned DirectInput poll captured `ButtonCount=128`, `SuccessfulReadCount=1251` in `10,002 ms`, **zero button-index transitions** and no indices `15/16`, without input read failure. Existing PID1902 ownership, LED and vibration initialization remained healthy.

## 3. Fix readback parser and evidence classification

Retain the exact two bounded 64-byte `0x04` ReadProfile requests, selector `0x01`, slot starts `0x00BA/0x0163`, read length `0x08`, current owner/control-HID/firmaware verification, 4-report maximum and 750 ms receive budget. DO NOT broaden to arbitrary addresses or change the normal poll.

Correct `MsiClawBackButtonProfileProtocol.ParseReadResponse`: validate 64-byte report length, report ID `0x10`, marker `0x3C`, ACK opcode `0x05`, echoed read index `0x01`, exact requested 16-bit address and echo length `0x08`; **do not enforce response[9]==0**. Extract **exactly `response.Slice(9, 8)`** for this observed PID1902 ReadProfile format. For example:

```csharp
const int PayloadOffset = 9;
if (response.Length != ReportLength || response[0] != 0x10 ||
    response[3] != 0x3C || response[4] != 0x05)
    return ParseOutcome.UnexpectedReport;
if (response[5] != ReadIndex ||
    ((response[6] << 8) | response[7]) != requestedAddress ||
    response[8] != PayloadLength)
    return ParseOutcome.WrongAddressOrIndex;
var payload = response.Slice(PayloadOffset, PayloadLength).ToArray();
```

Keep `01 04 0C` historical-marker recognition but ADD a descriptive A2VM observed form for `01 04 00 59 FF FF FF FF` (M1) and `01 04 00 58 FF FF FF FF` (M2), e.g. `ObservedLinuxRightBraceCandidate` / `ObservedLinuxLeftBraceCandidate`. It is not a generic firmware-corrupt classification. Preserve original raw bytes in the log, show compact classification in UI; classify all other well-formed payloads without presuming failure. If firmware/model differs, return the matching parsed raw bytes and an evidence-only classification (do not assert factory expectations). Consider an explicit per-slot observation of code byte `payload[3]` as *candidate* keyboard mapping, without treating `payload[3]` as universal action encoding for every profile form.

Update focused tests using the exact two user-captured 64-byte reports (zero-fill unshown trailing bytes). Replace artificial test responses that set `response[9]=0` / payload offset 10. Cover wrong opcode/address/index/length/short report, `01 04 0C` historical form and unknown profile form. A valid matching reply must be `ReadbackParsed` and log the **8 raw bytes**, not `UnexpectedReport`.

## 4. Fix actual Developer UI layout regression

Current `DeveloperPage.xaml` renders a long multi-line result into `MsiBackButtonDiagnosticStatusText` within nested `StackPanel` elements in a Toolkit `SettingsCard`. The formatter in `DeveloperPage.xaml.cs` joins multiple long variable-width strings (indices, flags, explanatory sentence) into a single TextBlock. User observed that **the entire app UI layout appears corrupted after the test finishes**. The log does not prove an exception or exact XAML measurement path; nevertheless unbounded horizontal measurement/large desired width caused by the new result text is a realistic and directly related candidate.

Fix the layout at its source. Keep the SettingsCard within the available page width. Place result display inside a width-constrained `Grid`/star cell (or equivalent) and ensure wrapping does not grow the card or parent ScrollViewer horizontally. Avoid using a fixed width that breaks 1920x1200 at 150% or other DPI/scales. Prefer a compact visible summary (model, firmware, M1 status, M2 status, raw index summary) and send full hex/report details to existing logs rather than printing a gigantic single TextBlock. If useful, split lines into individually wrapping TextBlocks. Preserve useful status after cancellation, failure and completion. Do not change global styles, unrelated Developer cards, window sizing or app navigation.

Add a narrowly scoped UI regression test of the result formatter/control layout with an unusually long list of transitions, success/no transitions, cancellation and unavailable outcomes. Unit/XAML source assertions can supplement but do not claim pixel-level layout proof from source strings alone. Do not require on-device screen verification for merge.

## 5. Developer-only explicit manual M1/M2 DirectInput restore experiment

Add **one separate** Developer-only button, e.g. `[Restore M1/M2 DirectInput (Experimental)]`, next to or below the diagnostic; keep the read-only diagnostic unchanged in semantics. Show an explicit WinUI confirmation: this action **writes M1/M2 controller profile, may persist across reboot and SteamOS/Windows, and is NOT a read-only test**. Default to cancel. One invocation, one in-flight guard and cancellation before the first mutation. Never run at startup, on profile switch, after sleep/resume, or automatically when the read-only diagnostic completes.

Admission must reuse the current Runtime-owned strong PID1902 physical session, exact single PID1902 control HID (UsagePage `FFF0` / Usage `0040`), matched device/firmware attributes and Center M exactly Disabled. Reject missing/ambiguous/wrong-mode device, stale/lost physical owner, shutdown/suspend/recovery or active mode change. Reuse existing producer/transport; no independent controller authority or manager. Support the same known physical model family (A2VM 7/8 and CG3EM/EX), with **explicit firmware gate for write eligibility** based on actually established device versions. A2VM 8 `0x0230` is the initial primary validation target. EX firmware `0x0414` and `0x0419` have documented slot/address evidence but no assumption of identical original factory action codes; keep EX eligibility explicit/evidence-backed. A2VM 7 remains read-only unless its matching actual firmware and write contract are established.

**Before any mutation:** query and parse BOTH slot snapshots, log original 8-byte payloads, and require the exact recognized remap pattern for that slot, initially matching the user-observed A2VM keyboard candidates (`01 04 00 59 FF FF FF FF` for M1; `01 04 00 58 FF FF FF FF` for M2). A well-formed but unknown/custom profile must return `NotEligible/UnknownProfile` without overwriting the user's intentional button mapping. If only one slot matches the known remap, report per-slot eligibility and let the implementer decide the smallest safe all-or-nothing policy (recommended: reject partial set until actual raw/firmware evidence); never silently restore arbitrary bytes. An already-known DirectInput profile should report `AlreadyPrepared/NoWrite`.

Use the **exact upstream HHC DirectInput (xinput=false) `GetM12` template**, not a mashup with Linux's `0x00BB/0x0164` remap subfields, HHD's five-byte template, or historical `01 04 0C` action profile:

```text
M1 HHC: 0F 00 00 3C 21 01 00 BA 02 01 00 [zero pad to 64]
M2 HHC: 0F 00 00 3C 21 01 01 63 02 01 00 [zero pad to 64]
ROM:    0F 00 00 3C 22                [zero pad to 64]
```

These are the established HHC references; the 2-byte write should be treated as **an explicit firmware experiment**, not assumed sufficient on current `0x0230` before the user's validation. Do not issue Linux keyboard remap `0x07` write, HHD `0x05` write or old RE `0x0C` pattern in the same operation. Do not add an unrelated reset/device mode command.

Recommended controlled sequence: capture owner and both pre-read snapshots; confirm user intent; send one bounded HHC M1 write; if successful and the same current owner remains valid send one bounded HHC M2 write; if both succeed send one `0x22` SyncToROM; then re-read both original profile starts with `0x04` and record before/after; then offer/launch the existing owned raw-input transition diagnostic for 10 seconds as a separate non-mutating result. Report `WriteTransportSucceeded`, `RomSyncIssued`, `ReadbackObserved`, and `RawInput15_16Observed` INDEPENDENTLY. No claim of repair solely from write transport success, ACK, or ROM sync. If the hardware requires a mode reapply for the new profile to be active, **DO NOT** run `SwitchMode 0x24` on the live Addon-owned PID1902 session in this PR; report `MayRequireReenumeration` and ask the user to do an ordinary controlled Windows restart before a second read-only raw-input test.

On any write/owner failure, stop further writes and report exactly which operations were issued; do not claim atomic rollback or successful repair. If one of two profile writes succeeded but the other failed, never issue ROM sync and explicitly report `PartialVolatileWritePossible` (the user can choose the next safe step). Do not introduce speculative transaction managers, epoch/barrier machinery or EEPROM undo writes. Safeguard the ownership lifecycle, including PnP loss and Suspend/Shutdown cancellation. No automatic retry of firmware writes.

Developer logs must distinguish `ControllerBackButtonProfileRead*` from new explicit `ControllerBackButtonManualRestore*`, with exact model/version, candidate original raw bytes, per-command result, sync issuance, readback bytes, eligibility and final verified *input* evidence. Never log that firmware was unchanged when a manual write was attempted. Use an explicit result DTO/RPC only as needed; increment frontend protocol version if new method is added, not Overlay protocol. Keep the UI usable if the restore fails or is cancelled.

## 6. Required deterministic tests

1. Exact A2VM 0x0230 M1/M2 read responses parse with offset 9 and correct `59/58` classification, including original bytes in logs. Existing read-only diagnostic remains mutation-free.
2. Long result / many indices cannot expand the new Developer result card beyond intended layout width; normal, unknown, cancelled and failed results remain legible.
3. Explicit restore unavailable under unsupported/unknown model/firmware, no current strong owner, PID1901, ambiguous control HID, Center M not Disabled, cancellation or lost owner; **zero write**.
4. Unknown/custom/F22/old `01 04 0C` patterns are not automatically overwritten solely because they differ from A2VM's observed SteamOS mapping. An eligible exact M1/M2 pair builds only the two HHC 64-byte `0x21` packets above, then at most one `0x22` sync; never `0x24` and never other profile addresses.
5. Write failure or ownership loss between M1/M2 halts the remaining operation; no sync on incomplete pair; final result acknowledges possible volatile partial write. Successful transport but mismatched post-read yields `Unverified`, not `Restored`.
6. Readback/owned raw transition confirmation is independent of transport success; 10s capture reuses existing poll worker, no second Acquire/ReadState and no normal cadence change.
7. Current Runtime/frontend/Overlay ownership, VIIPER/HidHide teardown, PID1901 restoration, A2VM boot rumble, LED and vibration settings, steam/Xbox360/Edge mapping and every other Developer diagnostic are unchanged.

Run normal repository build, managed tests and UI tests. Hardware verification is explicitly the user's role **after merge**, not a blocker or CI requirement.

## 7. After-merge user validation and production follow-up (NOT in PR2)

User runs read-only diagnostic before manual restore and saves log, confirms the explicit restore, examines before/after profile bytes, then presses/releases M1 and M2 during 10-second raw capture; if raw buttons remain absent after a successful write/readback, user can reboot Windows and rerun read-only capture. Repeat appropriately on CG3EM/EX to establish any firmware-specific differences. Never force a physical mode re-enumeration simply to make the test appear successful.

**Separate PR3, only after accepted hardware evidence:** factor the successfully validated M1/M2 prepare protocol into a small existing-runtime operation during normal **post-PID1902 owned/readiness** hardware-settings phase, adjacent to (and orderly with) LED/vibration application as the user requested. Read both slots once per genuine newly owned PID1902 session; if recognized correct state, do nothing; if a verified supported wrong state, perform the narrowly proven restoration only, optional ROM sync exactly as user validated, and re-read. Do not add repeated writes on mere frontend restart, Steam/BPM presentation change, profile changes, per-poll, or theoretical timing crossings. Route operations through the existing physical owner and preserve fail-close/typed teardown; support both A2VM and EX only for firmware/profile forms actually accepted in the manual experiment. Handle ordinary restart, PnP loss and Sleep/Resume by existing ownership lifecycle. Do not turn an unknown profile into an unconditional factory reset.

## 8. Explicit non-goals

No CTW app integration; no automatic firmware mutation in PR2; no routine `0x24` mode switch; no broad firmware-default guesses; no custom profile editor; no hidden retry/background service; no alternate DirectInput reader; no abstract global HID inspector; no new per-device state database; no speculative race-hardening beyond real user/device lifecycle.