# Work Order — Full1902 M1/M2 PR3: Production Profile Preparation and Developer UI Cleanup

**Date:** 2026-10-12  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementer:** local Codex (implementation and automated tests). **Physical validation is performed by the user after merge**, and must not be required for CI or PR merge.  
**Baseline:** merged PR #764 (squash `429aec82a795b8b5bc1c9d6c8f580db8726411c8`), version `0.1.366.0`. Re-read current `main` before implementation.

## 1. Goal / decision

Move the proven PID1902 physical rear-button preparation from experimental Developer manual restore into the **existing Runtime's production owned-controller hardware-initialization phase**, specifically **after strong physical PID1902 ownership and initial virtual presentation are established, before the existing LED and vibration setting applications**. Nothing in this work order introduces CTW integration; Full1902 is standalone.

**Product result:** in Center M Disabled mode, when the supported MSI Claw controller boots Windows after SteamOS (or another OS) has persistently mapped M1/M2 to keyboard codes, the Addon checks the current firmware slots once for that newly prepared owned physical session and restores independently recognizable DirectInput M1/M2 only when the mismatch is **positively recognized and safe to change**. Already-prepared slots are read-only. Unknown/custom profiles must be preserved. No UI toggle, background monitor, new manager, or database.

**Developer cleanup (explicit user decision, supersedes PR2):** retain the **Developer menu itself and all unrelated Developer tools**, but remove **ALL M1/M2-specific Developer UI and developer-only diagnostic/manual-restore code**, including the read-only 10-second diagnostic, the experimental restore button, status/confirmation/result formatting, UI handlers, developer-only Runtime host entrypoints, frontend RPC/contracts/wire/client/server methods, and exclusively diagnostic capture helpers/tests. The physical M1/M2 input mapping and poll logic, firmware profile reader/parser, production restoration transport/protocol, and their product tests **must remain**. Do not touch unrelated Developer cards or tools.

## 2. Required architecture and source review

Read these current docs first, in their authoritative order:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-11_FULL1902_M1_M2_PARSER_UI_AND_DEVELOPER_RESTORE_WORK_ORDER.md`
- `docs/RE_MSI_PID1902_M1_M2_BackButton.md` (research only; historical markers are NOT current A2VM proof)

Inspect these implementation seams: `AddonProcessHost.CompleteInitialControllerOwnershipAsync`, `RecoverOwnedControllerPhysicalInputAsync`, `ReapplyOwnedControllerHardwareSettingsAfterResumeAsync`, `RunMsiBackButtonProfileAndRawInputDiagnosticAsync`, `RunMsiBackButtonManualRestoreAsync`, `CaptureLiveOwnedPhysicalIdentityForDeveloperProbe`, `MsiClawBackButtonProfileReader`, `MsiClawBackButtonProfileProtocol`, `MsiClawInputSource`, current LED/vibration writers, `DeveloperPage.xaml(.cs)`, `FrontendContracts`, and FrontendTransport method enums/codecs/tests. Identify callers before removing any diagnostic shared helpers.

**Important lifecycle trap:** `CaptureLiveOwnedPhysicalIdentityForDeveloperProbe()` currently rejects while `_ownedControllerRecovery` is incomplete. The initial deferred acquisition is committed *inside* that tracked recovery task, and physical input recovery also executes *inside* it. **Do not simply call the current Developer manual-restore host entrypoint** from initial/recovery or it may be silently unavailable. Extract/use a narrowly scoped production operation with a live strong owner, matching current input source and exact control HID validation **appropriate to the current owning continuation**. Retain existing owner and safety invariants; no second authority, no extra DirectInput Acquire/ReadState, no new orchestration object.

## 3. A2VM 0.1.366.0 real-device evidence (Google Drive `Addon/Log/AV2M/1012`)

Only the `0.1.366.0` logs count for this acceptance; version `0.1.365.0` is excluded. At `2026-10-12 00:44:50–00:46:26 KST`, the A2VM 8 (`msi.claw.a2vm.8`), PID1902, firmware `0x0230`, Center M Disabled, strong owner, exact vendor control HID `UsagePage=0xFFF0, Usage=0x0040`, produced:

```text
Before M1 @ 0x00BA: 01 04 00 59 FF FF FF FF
Before M2 @ 0x0163: 01 04 00 58 FF FF FF FF

Command M1: 0F 00 00 3C 21 01 00 BA 02 01 00 ... (64 bytes), transport succeeded
Command M2: 0F 00 00 3C 21 01 01 63 02 01 00 ... (64 bytes), transport succeeded
Sync ROM:   0F 00 00 3C 22 ... (64 bytes), transport succeeded

After  M1 @ 0x00BA: 01 00 00 59 FF FF FF FF
After  M2 @ 0x0163: 01 00 00 58 FF FF FF FF
```

Both subsequent profile reads returned `ReadbackParsed`, and `ProfileReadbackVerified=True`. The first 10-second manual input capture at `00:44:56` observed zero transitions (cannot prove the operator pressed the paddles during this window). The second capture at `00:45:37–00:45:47` observed independent pressed/released input on **Button 15 and Button 16**, `Transitions=15:3+/3-,16:3+/3-`, `ReadFailures=0`, with **no second firmware write, ROM sync, Windows restart, or PID mode switch**. An additional read-only capture at `00:46:16–00:46:26` repeated both raw index observations.

**Conclusion:** the HHC-format two-byte per-slot command followed by one ROM sync is validated on A2VM 8 `0x0230`. A Windows restart / `0x24` mode switch / extra forced DirectInput re-enumeration is **not** a required step. No automatic 10-second human press test is possible or appropriate at production startup. Distinguish readback verification from physical button verification in log names/outcomes.

**EX/CG3EM:** existing code's explicit write-eligibility firmware versions are `0x0414` and `0x0419`, with the same PID1902 control HID addresses/protocol. The current supplied hardware log does **not** demonstrate an EX manual restore. Support EX through the exact same **positive profile recognition and explicit firmware gates**, but preserve any unrecognized EX/custom pattern without writing. Do not fabricate a factory profile, broaden eligible versions, or make EX physical testing a merge blocker. `msi.claw.a2vm.7` stays read-only until matching firmware/protocol is established.

## 4. Production call sites, ordering, and eligibility

After `CompleteInitialControllerOwnershipAsync` establishes initial presentation/publisher and front-button feature composition, **before** `ApplyOwnedControllerLedSettingsAsync` and `ApplyOwnedControllerVibrationSettingsAsync`, await the narrowly scoped feature-local M1/M2 profile preparation. The virtual publisher is already available; this optional read/write must **not delay first publisher attach**, change the acquired ownership result, or cause a rollback to PID1901. Then apply LED and vibration in their existing order regardless of M1/M2 feature-local outcome.

In `RecoverOwnedControllerPhysicalInputAsync`, after a truly recovered and newly owned PID1902 session (`result.IsOwned && result.Reason != "RecoveryNotNeeded"`) and **before** LED/vibration reapplication, run the same operation once in that owning recovery sequence. The source may be different from startup; read current strong identity, input source and control endpoint. Ordinary power Resume with the **same** still-owned source only reapplies LED/vibration and does not require a fresh EEPROM scan; if Resume actually forces loss/re-acquisition, use the normal owned recovery path. Do not run on Steam/BPM switch, profile/game change, controller 250/125 Hz cadence changes, frontend UI restarts, or every DirectInput poll.

Avoid adding a redundant global `alreadyChecked` flag/epoch; invocation under the **existing owning acquisition/recovery continuation** gives the correct once-per-new-owner-session boundary. On controlled Runtime restart, the new Runtime may check the existing PID1902 profile once, observe prepared, and perform **zero ROM writes**.

Admission: Center M exactly Disabled, strong uniquely owned PID1902 physical session, live current DirectInput source, unique present `VID_0DB0/PID_1902` control HID `0xFFF0/0x0040` from the same strongly matched physical identity, stable relevant model/firmware/path across bounded reads and before mutation; existing shutdown/owner-release/suspend/power lifecycle and failed physical cleanup gates apply. Do not use merely `startupResult.HardwareSupported`, a cached stale device path, or the developer-only `_ownedControllerRecovery.IsCompleted` predicate to veto work *inside* the owning recovery task. No second HID reader process and no alternate gamepad input session.

## 5. Match/restore behavior and firmware contract

Reuse `MsiClawBackButtonProfileReader`, existing resolver, transport, profile parser and the successful 64-byte `BuildDirectInputRestoreWriteRequest` and `BuildSyncToRomRequest`. Prefer factoring the existing experimentally named `RestoreKnownDirectInputProfileAsync` into a product-focused method without duplicating protocol logic. Keep existing bounded I/O and live-owner checks, but do not retain frontend confirmation, 10-second raw-input capture, or developer-diagnostic RPC/DTO dependencies in the production method.

Read M1/M2 **once each** at the beginning. For supported firmware, define slot interpretations based on documented bytes:

- **Prepared**: both mode-selector bytes `01 00` in an otherwise parseable slot; on the A2VM `0x0230` captured after-image, the full bytes are `01000059FFFFFFFF` / `01000058FFFFFFFF`. Since `01 00` changes mode semantics but remaining per-button codes may be preexisting, do not write a prepared slot even when its following bytes differ from this observation.
- **Recognized keyboard remap candidate**: exact eight bytes `01040059FFFFFFFF` for M1 and `01040058FFFFFFFF` for M2. These are the observed A2VM SteamOS/Linux brace patterns, not universal manufacturer defaults.
- **Unknown/custom/old-marker or malformed**: anything else. **Never auto-overwrite or `SyncToROM` an unknown profile.** Do not normalize unrelated firmware mappings.
- For a first production pass, preserve the **current tested all-or-nothing pair policy** for mutation: when both are candidates, perform the two validated 2-byte writes, then exactly one `0x22` ROM sync, then re-read both. When both are already prepared, no write. For a mixed prepared/candidate or unknown/candidate pair, skip writes and log `MixedOrUnknownProfilePreserved` with both raw payloads. The one-slot mixed case can be revisited after explicit evidence; the current experimental writer deliberately rejects it.

Firmware write eligibility must remain explicitly limited to A2VM 8 `0x0230` and EX/CG3EM `0x0414` / `0x0419`, using the existing `IsAllowedManualRestoreFirmware` policy (renamed to reflect production if suitable). Other versions/models: log `UnsupportedModelOrFirmware`, **zero writes**. Do not treat A2VM 7 read-only path as a failure that blocks normal routing.

For a recognized candidate pair, command sequence:

```text
WriteProfile M1: 0F 00 00 3C 21 01 00 BA 02 01 00 ... 64 bytes
WriteProfile M2: 0F 00 00 3C 21 01 01 63 02 01 00 ... 64 bytes
SyncToROM:      0F 00 00 3C 22 ... 64 bytes
ReadProfile M1/M2: verify both parse and begin 01 00 (record full 8-byte before/after)
```

If M1 fails, stop before M2; if M2 fails or the owner changes, no SyncToROM; report partial volatile write possibility, no forced rollback and no blind retry. If SyncToROM or post-read fails, log `Unverified` but leave the rest of the routing stack usable. Never call `0x24` mode switch, PID1901/PID1902 transition, or re-acquire physical input to apply this mapping. Cancel further firmware operations on ordinary shutdown/suspend/device loss; after first write, preserve the existing production-safe bounded operation and current ownership gates, without arbitrary frontend request cancellation.

There is no physical-button test during startup. **Readback `01 00` is profile-prepared evidence, not a claim that raw 15/16 button presses occurred.** On-device pressed/released validation was already established for A2VM 8 manually; for EX the product logs should not imply EX physical verification.

## 6. Production logging and lifecycle safety

Use concise structured logs, e.g. `ControllerBackButtonProductionPrepareStarted`, `...Skipped`, `...Write`, `...Completed` with `Trigger=Startup|DeferredInitialAcquisition|PhysicalRecovery`, exact model/firmware, full pre/post payloads where available, status (`AlreadyPrepared`, `RestoredProfileReadback`, `UnknownProfilePreserved`, `UnsupportedFirmware`, `PartialVolatileWritePossible`, `Unverified`, `OwnerUnavailable`), and write / ROM-sync booleans. Do not report `PhysicalButtonsVerified` without actual owned input observation.

The operation is **feature-local, best effort**. Failure must not demote a valid PID1902 physical owner, detach a valid VIIPER presentation, disturb HidHide, discard LED/vibration, force PID1901 mode, change WING/Center M suppression, or block shutdown. Maintain actual lifecycle safety on Sleep/Hibernate/Resume, crash/restart/shutdown, physical PnP loss, and owned cleanup-unproven states by using the existing gates; do not invent a new authority/lock/epoch/barrier/state machine for theoretical tiny interleavings.

No new Steam/Overlay publication operations, CTW bridge, cache, user setting, scheduled service, or new retry loop. Existing pipeline remains single Windows user / single interactive session.

## 7. Remove the **entire M1/M2 Developer feature**, preserve all other Developer tools

**Remove the whole** `M1 / M2 Physical Input & Firmware Profile` **SettingsCard** from `DeveloperPage.xaml`, including `Run 10s Diagnostic`, `Restore M1/M2 DirectInput (Experimental)`, `Cancel`, instructions, progress text, read-only/manual results, the restore confirmation dialog and UI layout/formatter support. Remove all corresponding M1/M2-specific fields, command handlers, cancellation sources and formatter methods from `DeveloperPage.xaml.cs`. Preserve the Developer menu, navigation, and **every unrelated** Developer card (including vibration test and existing non-M1/M2 diagnostics).

Remove both feature-only frontend RPCs: `RunMsiBackButtonProfileAndRawInputDiagnostic` **and** `RunMsiBackButtonManualRestore`, including frontend interface methods, dispatch/client bindings, named-pipe command IDs/codecs, response DTOs and enum definitions that have no remaining production consumers, and their feature-only tests. Keep any shared transport DTOs where production still requires them; if sensible, move necessary records to the production/Devices boundary rather than retain a frontend API solely as a type container. Follow the existing frontend protocol version/compatibility convention on changing wire commands; do not leave callable dead RPC methods, unused fields or broken enum mappings.

Remove the **developer-only** owned raw 10-second transition probe: `MsiClawInputSource.RunRawDirectInputButtonTransitionDiagnosticAsync`, its transient collection/collector, per-test status/event machinery and related diagnostic-only tests **only where not used by any other live feature**. Do not remove or change `MsiClawInputSource` production DirectInput polling, `Buttons[15]`/`Buttons[16]` detection, rear-button mapping, input events, or the M1/M2 logical mapping UX elsewhere in the product. Preserve current production polling cadence and ownership.

Remove the Developer-only `AddonProcessHost.RunMsiBackButtonProfileAndRawInputDiagnosticAsync` and `RunMsiBackButtonManualRestoreAsync` wrappers and feature-only `FrontendMsiBackButtonDiagnosticResult` / `FrontendMsiBackButtonManualRestoreResult` wire types if no production consumer remains. **Do not delete** `MsiClawBackButtonProfileReader`/`MsiClawBackButtonProfileProtocol`, their firmware I/O, deterministic safety tests, owner/identity gates, or the confirmed M1/M2 2-write/1-ROM-sync operation: refactor and retain these as the small internal production helper for section 4/5. Keep readback evidence and concise structured production logs, but **no developer screen controls**.

All other Developer UI elements, other developer RPCs, and general UI geometry/layout remain unchanged. Do not remove an unrelated card merely because it shares a `SettingsCard` container or helper type. No replacement diagnostic screen or extra hidden setting is requested.

## 8. Automated test expectations / merge policy

Tests should cover:

1. A2VM 8 `0x0230` exact captured before pair auto-restores via **two writes + one sync + two post-reads**, and logs the expected after payloads and profile outcome.
2. A subsequent Runtime start/profile read showing both `01 00` performs **zero writes** and **zero ROM sync**; ignore non-mode trailing bytes for prepared status.
3. A2VM 7, unknown firmware, unsupported models, unowned PID1902, PID1901, ambiguous/missing control HID, wrong firmware, Center M Enabled, shutdown/suspend/device loss, read timeout and unrecognized/mixed/custom profiles perform **no firmware mutation**.
4. EX/CG3EM `0x0414` / `0x0419` with the **exact supported candidate pair** follows the same protocol; an unrecognized EX profile is preserved. This is a code-contract test, not a claim of EX hardware validation.
5. On M1 failure, M2 failure, SyncToROM failure, post-read mismatch, or identity/device-path change, stop unsafe commands, never sync an incomplete pair, classify outcome correctly and still apply LED/vibration as existing optional hardware settings.
6. Initial owned session, **deferred initial acquisition** and **actual physical session recovery** each reach this operation once and in the proper phase, while a Steam/BPM presentation switch, profile change, ordinary no-reacquisition Resume and frontend reopen do not.
7. The operation neither schedules the raw 10-second input diagnostic nor blocks the first VIIPER presentation/publication; it reuses existing control HID transport and ownership. Validate no new DirectInput Acquire, `0x24` mode write, HidHide or VIIPER lifecycle change.
8. **No M1/M2 Developer card, read-only diagnostic button, manual restore button, status/dialog, developer-only M1/M2 RPC, raw-capture probe, or dead feature-only frontend contracts remain**. All unrelated Developer functions, navigation, and other diagnostic RPCs still compile and operate; production M1/M2 DirectInput input mapping remains intact.

Run normal build/test workflows. Hardware testing and confirming the startup restore on A2VM/EX are **the user's post-merge responsibility**, never a PR blocker. Review by realistically reachable product/lifecycle defects, not speculative race permutations.

## 9. User post-merge checks (outside Codex PR/CI)

The user can return to SteamOS, allow it to remap both M keys, then boot Windows 11. In the new Runtime log confirm one recognized read + narrow two writes + one ROM sync + two post-reads, and then check physical M1/M2 **through the normal production rear-button mapping and resulting virtual controller/game input**. There is intentionally **no M1/M2 Developer diagnostic screen or 10-second probe** after PR3. Restart Windows/Runtime and confirm the profile is already prepared and no ROM writes occur. EX/CG3EM can be validated analogously; if its profile is unfamiliar, automatic preservation is intentional and its raw evidence informs a separate narrow follow-up, not an excuse to overwrite unknown firmware bytes.
