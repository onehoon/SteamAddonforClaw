# Work Order — A2VM 7/8 Boot Rumble Re-arm, Shared LED/Vibration Support, and Controller Recovery UI

**Date:** 2026-10-10  
**Repository:** onehoon/SteamAddonforClaw  
**Implementation owner:** Local Codex  
**Delivery:** One focused implementation PR, with code, automated tests, and the documentation updates specified below.  
**Hardware validation owner:** User, **after merge**. On-device tests must NOT be required of local Codex or treated as PR/merge blockers.

## 1. Decision — binding product requirements

Implement the following policy on the existing **standalone Full1902** architecture. CTW integration is discontinued; CTW/HHC can only be used as historical reverse-engineering references.

| Function | MSI Claw 7 AI+ A2VM (MS-1T42) | MSI Claw 8 AI+ A2VM (MS-1T52) | MSI Claw 8 EX AI+ CG3EM (MS-1T91) |
| --- | --- | --- | --- |
| Automatically prime physical rumble once per actual Windows boot | **Yes** | **Yes** | **No** |
| Manual **Re-arm / Restore Vibration** command on Controller page | **Yes** | **Yes** | **Yes** |
| Controller-page left/right vibration-strength write | **Enable** using existing shared pair-write implementation | Already enabled | Already enabled |
| Joystick LED Basic Static / color / brightness | Use existing shared LED implementation and exact firmware-address policy; extend A2VM family 0x0230 admission | Existing A2VM 8 0x0230 path | Preserve existing EX firmware support |
| Return PID1901 at ordinary Windows shutdown / Runtime restart | **Never** | **Never** | **Never** |

Model identities are exact:
- msi.claw.a2vm.7 / MS-1T42
- msi.claw.a2vm.8 / MS-1T52
- msi.claw.cg3em / MS-1T91

**One confirmed field result:** The user physically verified that PR #735's manual PID1902 -> PID1901 -> PID1902 re-arm restores silent physical rumble on A2VM 8 without EC Reset. This is evidence for the mode-cycle remedy, not a firmware register-level proof. The user also reports that A2VM 7 and 8 share MSI's 2.30 firmware release. Distinguish **manufacturer version/reasoned shared-family support** from a **live HID 0x0230 and physical motor/LED test on A2VM 7**; the latter remains untested.

Further user field findings: returning from SteamOS to Windows now restores PID1902 and virtual Xbox360/SteamDeck presentation normally, so do not add SteamOS-mode recovery work. SteamOS rumble is not working and is out of scope for this Windows Addon. Windows boot rumble initialization remains intermittent, including Windows-only boots.

**No new public startup toggle; no new generic reset feature; no periodic watcher; no automatic cycling on Sleep/Hibernate/Resume or ordinary Runtime/Velopack/tray restart.** Manual re-arm remains explicitly user requested and may be repeated when admitted.

## 2. Mandatory source/architecture reading and precedence

Read the current main branch BEFORE editing (not merely these work-order excerpts):

1. [Full1902 document authority](../Full%201902%20Implementation/README.md)
2. [HidHide and startup authority revision](../Full%201902%20Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md)
3. [Reboot-bound controller authority](../Full%201902%20Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md)
4. [Full1902 controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md)
5. [Elevated Runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md)
6. [PR #735 developer re-arm PoC](../../pull/735) and [its original work order](2026-10-10_A2VM8_DEVELOPER_RUMBLE_REARM_MODE_CYCLE_POC_WORK_ORDER.md)
7. [Vibration RE and field evidence](../RE_MSI_ControllerVibration.md)
8. [Joystick LED RE, firmware mappings and field evidence](../RE_MSI_Joystick_LED.md)
9. [A2VM 8 vibration + LED enablement order](2026-10-09_A2VM8_VIBRATION_AND_LED_PRODUCTION_ENABLEMENT_WORK_ORDER.md)
10. [Original vibration firmware/address safety hotfix](FULL1902_VIBRATION_FIRMWARE_ADDRESS_VALIDATION_HOTFIX_WORK_ORDER_2026-10-04.md)
11. [Controller vibration persistence lifecycle](FULL1902_CONTROLLER_VIBRATION_PRODUCTION_PERSISTENCE_LIFECYCLE_WORK_ORDER_2026-10-04.md)
12. [Initial joystick LED Basic Static work](FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md)
13. [A2VM LED/vibration diagnostic work](2026-10-09_A2VM_LED_VIBRATION_DEVELOPER_DIAGNOSTICS_WORK_ORDER.md)
14. [English end-user README](../../README.md) and [canonical Korean guide](../howtouse/README_KO.md). The older [Korean-guide redirect](../KOREAN_USER_GUIDE.md) contains no guide body.
15. Relevant actual source files and associated test fixtures listed below.

Architecture rule: Center M exactly Disabled = Addon owns desired physical **PID1902** and persistent HidHide; Center M Enable-and-Restart is the only ordinary intentional release to stock PID1901. The *temporary and verified* PID1901 visit for this narrow A2VM boot feature or explicit manual re-arm is **not** an authority release, and does not change Center M startup state, task state, or persistent HidHide ownership.

Single Windows administrator user + one interactive session only. Preserve practical lifecycle safety, do not build race defenses for unsupported multi-session/RDP or pathological instruction-level interleavings.

## 3. Code inspection findings (current main, 2026-10-10)

### 3.1 Existing startup path is already ordered

**src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs**:
- TryStartDisabledModeControllerAsync creates the existing physical owner and presentation owner.
- After exact Disabled-boot admission and canonical VIIPER Ready, it calls **owner.AcquireAsync**.
- Only after successful physical acquisition/strong identity/real DirectInput source does it apply saved LED and motor strength, prove Win+G suppression, and call **presentation.AttachInitialAsync** with a fresh Steam/BPM snapshot.
- This is the right time to complete the boot-only native mode transition, **before** a virtual controller becomes visible. VIIPER initialization being Ready is not a virtual attachment.

**src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs**:
- AcquireCoreAsync currently switches PID1901 -> PID1902 if initial mode is XInput; an already-PID1902 boot does **not** cycle.
- The existing injected _switchMode calls MsiClawModeController; IsCrossModeTransitionProven validates command success, old PID disappearance, target PID arrival, source and target topology; fresh strong-state capture and exact PID1902 primary collection/DirectInput proof follow.
- The manual re-arm method assumes **already-owned live DirectInput and active presentation**; it cannot simply be called as the pre-acquisition startup step.
- Reuse the *existing mode controller and proof rules* inside the existing physical owner's startup operation; do not invoke manual teardown/VIIPER presentation APIs before first attach.

### 3.2 Existing manual operation is reusable across models

**src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs**:
- RunDeveloperRumbleRearmAsync / RunDeveloperRumbleRearmCoreAsync already own shutdown/power/Overlay admission, motion stop, existing presentation retirement, physical recovery, saved LED/strength reapply, fresh Steam/BPM presentation restore, and typed frontend result.
- **GetDeveloperRumbleRearmAdmissionFailure** is currently hard-coded to **msi.claw.a2vm.8**; widen only to the three exact supported models.
- **MsiClawAddonPresentation.RunDeveloperRumbleRearmAsync** retires publisher, callback feedback, physical STOP, cached rumble endpoint, DirectInput; delegates verified native PID cycle to owner; reattaches only after proven ownership and a fresh desired presentation. Keep this safety ordering.
- The existing frontend RPC method/serialization already exists. Prefer **reusing the same RPC** and result instead of adding a second public command or bumping the frontend protocol just for naming.

### 3.3 Vibration strength is already one physical command for EX/A2VM 8

**src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawVibrationProfileCommand.cs**:
- One 64-byte pair write prefix: **0F 00 00 3C 21 01 00 22 02 <Left> <Right>**; each percentage 0..100; no SyncToROM.
- **MsiClawVibrationStrengthClient.ApplyAsync** resolves one exact strongly identified PID1902 vendor-control HID (0xFFF0/0x0040), shares _transactionGate, and issues this one common packet.
- **MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified** currently allows **cg3em, a2vm.8**, but excludes **a2vm.7** solely for the previous hardware-evidence admission policy. The user explicitly authorizes enabling A2VM 7 based on shared A2VM family/firmware evidence. **No new command builder and no new HID writer.**
- Do not misrepresent the A2VM 7 extension as physically validated.

### 3.4 LED is shared code with a firmware-address lookup

**src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedProtocol.cs**:
- TryResolveRgbAddress returns known firmware-specific RGB profile base: e.g. **0x0230 -> 0x024A**, EX **0x0419 -> 0x024A**, **0x0163/0x0211 -> 0x01FA**.
- TryBuildStaticWrites produces **four** 64-byte profile writes for the existing static/brightness/color presentation; no SyncToROM.

**src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawLedController.cs**:
- ApplyAsync has an additional explicit guard **if firmware == 0x0230 and model != a2vm.8, refuse**.
- Extend that guard to **allow exactly a2vm.7 or a2vm.8 for 0x0230**; retain the existing exact firmware address lookup and unknown-firmware fail-close.
- Other known mappings and EX behavior must remain unchanged. **Never make all arbitrary firmware versions 0x024A**.
- The separate **ReadA2vm230CandidateProfileAsync** is a developer-only, currently a2vm.8-scoped read diagnostic. This order does **not** require broadening its purpose/renaming it to support production static writes; production write admission is independent from diagnostic availability. Avoid unrelated developer diagnostic churn.

### 3.5 Current UI is ONE expander containing TWO cards

**src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml** has **VibrationStrengthExpander** containing **SettingsCard Left Motor** and **SettingsCard Right Motor**, each with sliders, percentage, Test button. **There are not two nested SettingsExpanders.**

User intent: append a third sibling **SettingsCard Vibration Recovery** *inside the existing VibrationStrengthExpander.Items*, immediately beneath Right Motor. **Do not make a separate top-level settings card or three stacked top-level expanders.** Reuse the current CTW-style SettingsExpander visual hierarchy and existing VibrationStrengthInfoBar for status if appropriate.

**src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml[.cs]** contains the temporary Developer-only A2VM 8 re-arm button. Move that user-accessible command to Controller and remove the obsolete Developer re-arm section/duplicate action, while retaining other diagnostics.

## 4. Implementation A — automatic A2VM physical rumble initialization at Windows boot

### A.1 Exact trigger and cardinality

- Eligible only for **msi.claw.a2vm.7** or **msi.claw.a2vm.8** with Center M exactly Disabled and the normal Full1902 Disabled-boot admission satisfied.
- Attempt **once per actual Windows boot**, not once per **process**, not once per game, presentation switch, Steam/BPM change, UI open, Resume, or PnP event. No periodic retry.
- A normal tray **Restart Addon**, Velopack app-restart/update, process crash/restart, or repeated controller-initialization invocation **within the same Windows boot** must **not re-run the optional A2VM startup mode cycle**. Ordinary Full1902 recovery/reconciliation still runs.
- If first physical startup identity is already **PID1902**, perform an actual **PID1902 -> PID1901 -> PID1902**, requiring both verified transitions.
- If first stable physical identity is **PID1901**, the existing **PID1901 -> PID1902** startup takeover already supplies the real XInput-to-DirectInput transition; do not first create an unnecessary triple/higher-order mode oscillation. The user selected an *always-prime-on-boot* policy, not a speculative rumble-health sensor.
- On **EX/CG3EM**, retain exact old startup behavior: PID1902 remains PID1902, PID1901 normalizes once to PID1902, **no forced cycle**.

### A.2 Minimal same-boot gate, NOT another controller authority

The current process-local startup path would cycle again on a Runtime restart; that violates the requirement above. Use the smallest reliable **internal per-Windows-boot attempt marker** if no existing durable boot-session fact can serve it. Repo already has **src/SteamInputAddonforClaw/Prerequisites/BootSession.cs** with a simple TickCount64-based boot-change test; inspect/reuse its bounded boot-session semantics where appropriate.

Required semantics:
1. No new user-facing setting, startup preference, service, watchdog, custom authority, controller mode owner, state machine, generalized persistent transaction journal, or arbitrary retry policy.
2. Only the **A2VM optional auto-re-arm attempt** consumes this marker; neither Center M Disabled ownership nor HidHide/controller recovery may depend on its existence.
3. Recognize a later real Windows boot as eligible again; recognize the same boot across process/update/tray restarts as already attempted. Avoid treating Sleep/Hibernate Resume as a fresh boot.
4. Establish/record **attempted** (not physically verified or firmware armed) **before the first optional mode write**, so a crash after the first transition cannot lead to a second automatic cycle on same-boot restart. A later ordinary startup must still restore PID1902 from the actual mode if needed.
5. A marker that cannot be reliably read/written must **not cause uncontrolled repeated cycling** or block ordinary controller startup; log and skip only the *optional cycle*, then use the existing normal PID1902 ownership path. Keep marker I/O bounded; do not put it into settings.json or mix it into provisioning receipts. Avoid complicated cross-user coordination (unsupported).
6. A2VM 7/8 have no automatic motor pulse requirement. Successful native re-enumeration and owned-source proof are software evidence only; do not write PhysicalMotorEffectVerified=true.

The marker is a narrow **anti-repeat observation**, never an authority determining whether Addon controls the device. If you find a smaller existing dependable boot-only discriminator, use that and explain it in the PR.

### A.3 Mode-cycle location / proof

Modify the existing owner startup workflow under its existing gate rather than introducing a second input manager.

Pseudocode illustrating required shape (adapt to actual constructors/interfaces):

~~~csharp
var initial = await CaptureStableNativeStateAsync(token);
if (!TryReadStrongIdentity(initial, out var mode, out var identity))
    return FailWithoutPhysicalMutation();

if (shouldPrimeA2vmAtThisWindowsBoot && mode == MsiClawNativeMode.DirectInput)
{
    // Record one-shot boot attempt before the first optional write.
    if (TryRecordBootAttempt())
    {
        RequireExactlyDisabledAuthority();
        var to1901 = await _switchMode(MsiClawNativeMode.XInput, identity, token);
        RequireVerifiedCrossModeTransition(to1901);

        var xinput = await CaptureStableNativeStateAsync(token);
        RequireStrongCurrentPid1901(xinput, out var fresh1901Identity);

        // Fall through to the existing normal 1901 -> 1902 acquisition,
        // using fresh1901Identity and its existing proof/reconcile.
        mode = MsiClawNativeMode.XInput;
        identity = fresh1901Identity;
    }
}
// Existing normal PID1901 -> PID1902 path and all normal final ownership
// checks, first DirectInput state, HidHide, GamepadMode, etc. stay authoritative.
~~~

Pseudocode is **not** authorization for a duplicate read, a second owner, or skipping error checks. Preserve:
- exact physical identity continuity via verified PnP transitions, not literal root-string equality across PID changes;
- fresh strong PID1901 and final PID1902 captures, old-PID disappearance/target arrival, final exact PID1902 primary target;
- current Center M authority before first write, and other already-required admission gates;
- normal GamepadMode normalization, first valid DirectInput report, exact HidHide baseline, ownership publication;
- **fail-close if either optional/native transition was attempted and cannot be proved**: do not attach Xbox360/SteamDeck to unproven source, do not roll back to stock PID1901 merely for normal Disabled-mode failure. A later independent real PnP return or next ordinary Runtime startup may use existing recovery logic.
- No mode cycle just because user manually changes Steam/BPM output or resumes from suspend.

**Important:** The developer command's retirement path starts from a live attached presentation and is therefore not the boot path. Reuse its *existing transition evidence/predicates*, not a pre-attach call to RunDeveloperRumbleRearmAsync.

### A.4 No regressions in startup timing and reapply ordering

Complete optional cycle before:
1. final PID1902 DirectInput ownership and exact HidHide target are committed;
2. persisted LED / vibration strength is applied;
3. first live publisher / VIIPER virtual attachment.

Keep the existing Win+G suppression guarantee before presentation. Preserve the real failure policy without adding artificial waits, extra polling loops, repeated mode switches, or new locks.

## 5. Implementation B — manual re-arm on all three models

Reuse PR #735's physical/presentation/Runtime command and existing frontend transport. Widen the one host model admission from **a2vm.8 only** to the **three exact known supported Claw models**.

Keep all existing real admission checks:
- Center M exactly Disabled, Runtime initialized/elevated owner, current strong PID1902 physical identity, exact HidHide target and healthy owned DirectInput;
- no in-progress recovery, lifecycle suspension, shutdown, Overlay capture, or conflicting motor test;
- VIIPER Ready, active publisher can retire and STOP confirmed; callback drained; physical HID endpoint invalidated;
- verify both PID1902 -> PID1901 -> PID1902 mode changes and fresh real DirectInput ownership;
- reapply existing saved LED/strength through their normal feature policies, restoring current Xbox360 or SteamDeck based on a **fresh** Steam/BPM snapshot;
- no virtual attach if actual mode/input/cleanup cannot be proven.

This is an **explicit recovery action**, not a guaranteed MCU reset. Never silently launch EC Reset or change Center M startup state.

Do not add a second frontend RPC or fork independent "EX rearm" logic. Keeping the existing RunDeveloperRumbleRearmAsync method identifier internally is acceptable if changing it would churn named-pipe protocol/tests; rename only UI-facing text / narrowly needed internal diagnostics.

## 6. Implementation C — Controller page: third card in one existing expander

In **ControllerPage.xaml**:

~~~text
VibrationStrengthExpander [existing]
  SettingsCard: Left Motor
    Slider | value | Test
  SettingsCard: Right Motor
    Slider | value | Test
  SettingsCard: Vibration Recovery                <-- NEW THIRD SIBLING
    Restore Vibration [button]
    Brief warning: virtual controller disconnects temporarily;
                   save/close the current game first
~~~

- Use the existing SettingsCard visual style and a short ordinary action caption (**Restore Vibration** or **Re-arm Vibration**).
- No fourth/top-level expander, no separate feature card, no new Device tab feature, no Overlay parity requirement.
- Wire the same typed existing _frontend.RunDeveloperRumbleRearmAsync() method; show unavailable/failed/succeeded statuses in the existing VibrationStrengthInfoBar or a small inline status.
- Button busy state and minimal admission/disable state; prevent duplicate clicks. Inform user before executing about game input temporarily disconnecting (an inline warning and explicit button click is sufficient; an extra confirmation dialog is optional only if existing UI convention warrants it).
- On software success say controller mode cycle/restoration completed, **not** that physical motor effect was verified. Suggest Left Test / Right Test.
- Preserve current motor sliders, slider debounce, Test actions, persisted values, pointer gesture state, page Unloaded behavior and previous InfoBar use. Do not allow a pending slider mutation to make success status misleading.
- The manual control is available for all three supported models **even when a motor-strength write is unavailable due to an unverified firmware policy**; eligibility comes from manual Runtime ownership/admission, not LED/strength write capability. Do not couple the recovery button to the vibration percentage editor's Writable flag.
- Keep the current VibrationStrengthExpander's existing visibility/Controller availability semantics and ensure frontend initialization does not hide the new recovery action for a supported model merely due to an unrelated strength-write policy.
- Remove only the **obsolete Re-arm A2VM 8** section/button and event handler from Developer -> Vibration Test page. Preserve all other Developer diagnostic probes and the standalone left/right motor tests.

## 7. Implementation D — enable A2VM 7 common physical features

### D.1 Vibration

Change **MsiClawVibrationProfilePolicy.IsProductionPairWriteVerified** to include **msi.claw.a2vm.7** along with a2vm.8 and cg3em.

~~~csharp
internal static bool IsProductionPairWriteVerified(HandheldDeviceModelId modelId) =>
    modelId.Value is "msi.claw.cg3em"
        or "msi.claw.a2vm.7"
        or "msi.claw.a2vm.8";
~~~

- Preserve one 64-byte pair packet / strong exact PID1902 control HID / 0..100 range / default 50-50 / settings.json source of truth / feature-local error.
- No SyncToROM, no firmware EEPROM-read-as-UI, no changed game rumble report, no additional hardware transport or software motor gain.
- Update stale unsupported-model diagnostic status and related tests.
- The matching RE documentation must say **A2VM 7 enabled based on A2VM shared-family assumption**, not directly physically tested.

### D.2 LED

In **MsiClawLedController.ApplyAsync** use the existing firmware attribute and exact supported firmware-to-address lookup. Narrowly change the **0x0230** guard to the A2VM family:

~~~csharp
if (attributes.VersionNumber == 0x0230
    && modelId is not ("msi.claw.a2vm.7" or "msi.claw.a2vm.8"))
    return Fail("UnsupportedFirmwareForModel");
~~~

- Retain **MsiClawLedProtocol.TryResolveRgbAddress** (0x0230 -> 0x024A), 4 static packets, known other versions and current EX mappings.
- Retain unknown-firmware refusal; DO NOT enable 0x0229/unknown offsets or nearest-match fallback solely from model identity.
- Retain exact control HID identity/PID checks and no SyncToROM. Do not implement animations/independent zone editor.
- A2VM 7 and 8 share an advertised 2.30 firmware generation according to the user's MSI-site observation, but runtime must still use **actual detected HID firmware**; do not hard-code "all A2VM are 0x0230".
- Preserve the Developer A2VM 8 read-only probe semantics unless changing it is necessary for code correctness (no need to broaden diagnostics for production parity).
- Do not implicitly require physical LED effect testing as a code review gate; the user will validate it after merge.

## 8. Documentation — REQUIRED in the implementation PR, not optional

### 8.1 Main English README (especially important)

Update **README.md** at the repo root, preserving all existing unrelated sections, links and gyro bilingual section.

Specifically revise:
- **Supported devices:** separate support vs physical validation accurately (EX validated, A2VM 8 manual rumble recovery verified, A2VM 7 feature support extrapolated, not individually hardware tested).
- **Controller:** list Joystick LED and Left/Right Vibration Strength availability on A2VM 7/8/EX; describe the **third Vibration Recovery card inside Vibration Strength**, its manual action, brief controller disconnect and Left/Right Test follow-up.
- **Quick start / How controller presentation works / Background operation / Sleep, resume, and restart:** a single brief explanation that A2VM 7/8 perform a boot-only mode initialization to avoid silent motors; no PID1901 release on shutdown and no automatic repeat on Runtime restart/resume.
- **Recovery and troubleshooting:** add "**Motors do not vibrate**" guidance: first Test left/right, save/close game, Controller -> Vibration Strength -> Restore Vibration, retry the tests. Clarify success message cannot physically verify motors and failures leave controller offline if ownership is unproven.
- **Known limitations:** do not promise all hardware/firmware or SteamOS motor effect; SteamOS rumble and mode behaviors are not controlled by this Windows-only addon.

Do not duplicate entire guide, and do not describe the command as developer-only.

### 8.2 Canonical Korean README/How-to

Update **docs/howtouse/README_KO.md** with Korean-language equivalents in the corresponding sections:
- 지원 기기, Controller, 빠른 시작/절전·복귀·재시작, 복구 및 문제 해결, 알려진 제한 사항.
- User-facing labels must exactly match the actual English buttons shown in the Windows UI; Korean text explains their meanings.
- State **A2VM 7/8 부팅 시 1회 진동 초기화**, **EX 자동 실행 제외**, **3개 모델 모두 수동 Restore Vibration**, **게임 저장/종료 후 실행**, and **왼쪽·오른쪽 Test 버튼 확인**.
- Preserve the guide's links and How to Use render format. **docs/KOREAN_USER_GUIDE.md is only a redirect; do not replace it with a second duplicate Korean guide.**

### 8.3 Architecture README and owning architecture text

Update **docs/Full 1902 Implementation/README.md** with a concise **model-scoped A2VM boot rumble exception** and link to this work order. Its document precedence list must remain intact.

Update relevant startup/normalization sections in:
- **docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md**
- **docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md**

Both currently state that already-PID1902 startups do not force PID cycling; this remains true for EX and ordinary same-boot/runtime recovery, with the **new explicitly justified once-per-boot A2VM 7/8 exception**. Also document that shutdown/restart still do **not** restore PID1901, HidHide remains persistent Disabled-mode state, and user-invoked manual recovery is not stock authority release.

Update the startup policy revision only if its words require a concrete clarification, not for cosmetic churn. The 2026-10-05 elevated-runtime authority does not require rewriting.

### 8.4 RE evidence/reference documentation

Update BOTH:
- **docs/RE_MSI_ControllerVibration.md**
- **docs/RE_MSI_Joystick_LED.md**

Add a short dated 2026-10-10 support/evidence section, refresh production support tables and previously accurate-but-now-stale "A2VM 7 disabled" / "re-arm developer-only" wording:
- Distinguish field-proven A2VM 8 manual motor re-arm; A2VM 8 0/100 strength asymmetry; EX/CG3EM previously verified strength; A2VM 7 family-protocol rollout (not physically verified).
- Separate **runtime rumble 0x05 report**, **motor strength 0x3C/0x21 write at 0x22/0x23**, and **LED 0x3C/0x21 four-profile-frame writes at firmware-mapped RGB base**; do not conflate.
- State A2VM 7/8 0x0230 LED admission and address 0x024A, known EX firmware mappings, actual runtime firmware read, and unknown-firmware fail-close.
- Keep historical RE intact and mark superseded "not supported" policy only where needed. Preserve CTW/HHC attribution as historical evidence, not integration.
- Do not claim A2VM 7, or A2VM 8 LED physical effect, was directly observed if not tested.

If any other user-facing README or reference document explicitly contradicts the new implemented policy, update the specific stale paragraph rather than duplicating another guide.

### 8.5 Documentation quality/links

- Confirm root English and canonical Korean sections agree in **device matrix, label names, boot vs restart distinction, and troubleshooting**.
- Avoid ungrounded claims about firmware state readback or EC registers.
- Verify relative Markdown links from each directory and ensure no broken relative link to work order/PR.
- Commit implementation and documentation **together**, not in a promised later documentation-only PR.

## 9. Automated test contract (code-level; no hardware prerequisite)

Add/update focused tests using the existing fakes:

1. Exact model policy: A2VM 7 and A2VM 8 eligible for automatic boot re-arm; CG3EM and unknown are not. All three eligible for manual re-arm only when normal owner admission is satisfied.
2. New Windows boot, initially PID1902 A2VM: **two real verified transitions** in order 1902 -> 1901 -> 1902, then final exact owned DirectInput and no first virtual attach before successful completion.
3. New Windows boot, initially PID1901 A2VM: existing **one** verified 1901 -> 1902 startup normalization, no unnecessary reverse/extra transitions.
4. Same Windows boot **Restart Addon**, update restart, simulated crash/restart: no second *optional* A2VM cycle; ordinary acquisition and physical/PnP recovery remain available. A genuinely new Windows boot re-enables exactly one attempt.
5. CG3EM already-PID1902 boot: no cycle and no change to current startup behavior.
6. Center M not exactly Disabled, blocked admission, weak/ambiguous physical identity, wrong control HID, VIIPER not Ready: no optional mode writes/unsafe attach.
7. First or second native transition unproven, source lost, final PID1902/DirectInput/Hide target not verified: correct fail-close; no live virtual controller attached to an unproven physical session; ordinary later recovery and stock release path still possible.
8. Power suspend/resume and physical PnP return: no automatic cycle merely because those events happened.
9. Manual re-arm on CG3EM, A2VM 7, A2VM 8: shared PR #735 retirement/STOP/callback drain/mode verification/restore behavior, and rejected unsafe admission; no duplicated RPC.
10. A2VM 7 strength writes use the exact existing contiguous profile pair command, no SyncToROM, proper percentages and persistence. Unknown model remains rejected.
11. 0x0230 Static LED on both A2VM 7 and 8 emits exactly four expected packets at **0x024A, 0x026A, 0x0285, 0x02A0**; EX's known version table remains unchanged; other models with 0x0230 and unknown firmware remain fail-closed.
12. Controller XAML/UI tests: **one VibrationStrengthExpander with three SettingsCards** in order, Left/Right sliders and tests unchanged, third Restore Vibration command wired through current frontend, sensible success/unavailable/failure/busy behavior. Remove obsolete developer-specific re-arm UI while preserving other diagnostics.
13. Documentation consistency/link assertions or lightweight checks where existing test suites already enforce them; do not create large new tooling purely for Markdown.

Run the relevant existing .NET tests and solution/build checks supported by the local environment; if Windows-only XAML tooling is unavailable, let Windows CI validate it and report that limitation. **No manual device test, physical motor observation, Windows restart experiment, or SteamOS test is required from Codex or as PR review blocker**; these are user-owned post-merge acceptance activities.

## 10. Real lifecycle safety and exclusions

**Must retain**:
- Windows Sleep/Hibernate/Resume and shutdown/restart safety;
- Center M reboot-bound authority, stock PID1901 restoration only via explicit Enable-and-Restart / supported uninstall;
- owned PID1902 / exact HidHide identity, physical loss and PnP re-enumeration;
- VIIPER native ownership/retirement, callback STOP/drain, publisher teardown;
- one trustworthy Runtime controller owner and one explicit fail-close policy;
- fresh Steam/BPM desired presentation after user manual recovery.

**Do not add**:
- CTW integration, new virtual-controller implementation, duplicated HID writer, generic controller-reset service;
- startup/exit mode oscillation on CG3EM, per-game mode cycling, auto Resume mode cycling;
- firmware-nearest-match fallback, exploratory EEPROM write, SyncToROM, unproven LED effects;
- new locks/barriers/epochs/managers for theoretical narrow timing races, unsupported RDP or multi-user handling;
- mandatory hardware test as local-Codex task or merge precondition.

## 11. Post-merge hardware acceptance checklist — USER ROLE ONLY

The user (not Codex/CI) may confirm:
- A2VM 8 Windows full reboot with previously silent motor: auto initialization restores physical Left/Right motor Test.
- A2VM 8 remains working on both X360 and SteamDeck rumble routes; clean Runtime restart does not cause another unnecessary PID cycle.
- If an A2VM 7 machine is available: 2.30 HID version, strength 0/100 behavior, Static LED color/brightness, boot re-arm.
- EX/CG3EM boot performs no optional auto cycle; manual Restore Vibration is available and controller resumes its prior active presentation.
- Full1902 sleep/resume, fail-close, device re-enumeration and explicit Center M stock restore still work.

Hardware acceptance is **after** code merge and does **not** block PR approval. Record newly observed behavior in RE docs in a later user-evidence update if necessary, without retroactively claiming these tests occurred in this PR.

## 12. Definition of Done

One focused PR implementing the exact user-approved behavior:
- A2VM 7/8-only once-per-Windows-boot optional rumble initialization before first virtual attach, never ordinary process restart or resume.
- Shared manual Restore Vibration for A2VM 7/8 and EX, with existing safety/IPC and a third SettingsCard in the existing Controller > Vibration Strength expander.
- A2VM 7 added to common vibration-strength support; A2VM 7 admitted for exact 0x0230 LED Basic Static using existing firmware lookup.
- Focused code tests and current CI; no speculative lifecycle infrastructure.
- **Root English README.md + canonical Korean docs/howtouse/README_KO.md + Full1902 architecture/README + both LED/vibration RE documents updated coherently in the same PR.**
- No CTW runtime dependency; no mandatory on-device verification by Codex.
