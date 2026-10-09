# Work Order — A2VM 8 Developer-Only Rumble Re-arm via Verified PID1902 → PID1901 → PID1902 Mode Cycle (PoC)

**Date:** 2026-10-10  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementer:** Local Codex, one focused PR  
**Scope:** Existing Full1902 **standalone** Runtime and Developer > Vibration Test page  
**Priority:** Investigate a **reproducible, user-visible A2VM 8 firmware-rumble initialization failure**, not a general-purpose controller reset feature.  
**Hardware acceptance:** The user tests on their own A2VM 8 **after the code-only PR has passed CI and merged**. Do not make real hardware, a successful physical vibration, or a user verification screenshot a Codex/CI/PR-review blocker.

---

## 1. Goal and observed evidence

Add one **explicit, manual, Developer-only** command labeled **`Re-arm Rumble (A2VM 8)`** to the **existing Developer > Vibration Test** page. This command should perform one **real and positively verified** controller native-mode round-trip:

```text
Addon owns healthy PID1902 / DirectInput
-> safely quiesce and detach the active VIIPER virtual presentation
-> stop and release the process-owned DirectInput and physical rumble endpoints
-> switch the same positively identified MSI Claw to PID1901 / XInput
-> prove actual PID1901 transition and disappearance of the old PID1902
-> switch the positively identified PID1901 back to PID1902 / DirectInput
-> prove actual PID1902 transition and disappearance of PID1901
-> re-establish existing owned DirectInput + exact HidHide isolation
-> reapply saved vibration-strength pair and LED settings on healthy ownership
-> attach/reconcile exactly the currently desired Xbox360 or SteamDeck virtual presentation
-> return a **software lifecycle/transport result**, then let the user test both motors manually
```

**Do not promise that the firmware motor is physically armed just because mode writes, readback, or HID writes succeeded.** This PR is the PoC to establish that fact.

### 1.1 Real-user incident

On an **MSI Claw 8 AI+ A2VM / MS-1T52**, firmware **`0x0230`**, the user reports:

- Physical vibration sometimes fails after **Windows-only** reboots, even without SteamOS; therefore SteamOS is **not** a required causal condition.
- The failure also exists after SteamOS use, but SteamOS 3.9.2 Beta no longer appears to strand the whole controller topology.
- During the failure **Xbox360, SteamDeck/Steam, and SteamOS** vibration all fail. **EC Reset consistently restores physical vibration** according to the user.
- 2026-10-10 00:42 Runtime log in `GoogleDrive/Addon/Log/1009/04`: strong, functioning PID1902 DirectInput and a composite USB HID device; `ModeWriteRequired=False`, `ModeWriteIssued=False`, `ObservedMode=DirectInput`.
- That same Runtime receives production Xbox360 rumble callbacks, emits full-size physical rumble reports (`Large8=255`, `Small8=255`, `Result=OK`), and confirms both Left and Right Developer motor Test **HID writes and STOPs**. Nonetheless the user **felt no vibration**. USB write success is not proof of motor actuation.
- After a plain Runtime restart (`00:43:07`) the native mode is again already DirectInput; `ModeWriteIssued=False`. No physical mode transition was performed.

These facts favor a **firmware/MCU rumble-engine initialization state** hypothesis over a VIIPER callback or virtual-controller decoding problem. The precise hardware cause is not yet proven.

### 1.2 Independent CTW reference — evidence, **not integration**

The private historical fork `onehoon/ClawTweaks-Dev`, branch `release/v0.3.98.0`, documents the same A2VM symptom:

- `XboxGamingBarHelper/Startup/Program.MSIClaw.cs`, around **3957–3975**, **4035–4076**: a proactive XInput switch followed by a real DInput transition is intentionally used to arm the firmware rumble engine.
- `XboxGamingBarHelper/Labs/ClawButtonMonitor.cs`, around **381–398**, **2431–2455**: stale DirectInput discovery on an A2VM skipped the necessary physical transition on **13 of 17 observed startups**, leaving motor output dead; a forced real `XInput -> DInput` transition fixed the particular observed path.

**Important:** This is **historical empirical guidance only**. Do **not** reinstate CTW components, a CTW dependency, CTW settings, VIIPER migration logic, old HidHide pre-cloak algorithms, or CTW startup mode cycling. The current application is standalone Full1902.

---

## 2. Mandatory document precedence

Before implementing, read the current code and these documents:

1. `docs/Full 1902 Implementation/README.md` — precedence authority.
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`.
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`.
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`.
5. `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
6. `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`, `PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`, `PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`, `PR9_OWNED_PID1901_DRIFT_RECLAIM_WORK_ORDER.md`, `PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`, `PR11_FULL1902_HARDWARE_VALIDATION_ROUTING_AND_STARTUP_FIXES_WORK_ORDER.md`.
7. `docs/RE_MSI_ControllerVibration.md`, `docs/Rumble.txt`, and the current production rumble code.

The normal Full1902 contract remains: one Windows admin user and interactive session, Center M startup state exactly Disabled, Addon-owned PID1902 + HidHide, VIIPER virtual presentation, fail-close when ownership/cleanup cannot be proven, and no mode switch merely for reboot/restart. **This work order introduces only an explicit Developer exception, not a change in the default startup policy.**

---

## 3. Code review: actual reuse seams and important traps

### 3.1 Native mode transition already exists

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs`

- `IMsiClawModeController.SwitchModeAsync(target, expectedIdentity, token)` is already the bounded native mode transition operation.
- It resolves the correct control HID for the **current** native mode, submits the command, and requires the **old PID to disappear and the target PID to appear** with verified topology.
- `MsiClawModeTransitionResult` contains `WriteSucceeded`, `OldPidDisappeared`, `TargetPidAppeared`, `SourceIdentityVerified`, `TargetTopologyVerified`, `Status`, and `Reason`.
- The physical-root string legitimately differs between PID1901 and PID1902; never demand literal cross-mode root-string equality instead of relying on the **already verified controlled transition**.
- If an HID writer reports success but no actual USB/PnP transition happens, this is **failure**, not success.

**Reuse this existing transition implementation.** Do not hand-write vendor bytes in the UI, call Windows PnP reset/cycle-port as a substitute, add a second HID mode switcher, or use the command `RESET_DEVICE (0x28)`. That firmware reset command has not been shown to arm the motor.

### 3.2 Physical ownership and DirectInput already exist

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs`

- `AcquireCoreAsync` only writes PID1902 when the starting hardware is PID1901. Already-PID1902 startup intentionally skips it.
- `RecoverLostInputAsync` returns **`RecoveryNotNeeded` when the input source is still running**, so it cannot perform this experiment directly.
- Its recovery core already handles **stopped** process-owned DirectInput, exact previously-owned PnP targets, strong identity, mode normalization, HidHide readback, reacquisition, first valid input, and publishing a fresh physical session.
- `ReleaseForCenterMEnableAsync` / `ReleaseToXInputAsync` sets `_releasedForEnable=true` and is exclusively for **stock Center M authority handover**. **Never call it for this PoC.**
- `_gate` is the current physical-owner serialization authority. Use it, with a **small explicit developer recovery operation** that stops the existing owned source, switches XInput and back once, then enters the **existing recovery core**. Do not duplicate all of PR8/PR9/PR10 or create a new owner/manager.

After a proven round-trip, the fresh PID1902 strong identity may be adopted using the same proven-cross-mode continuity policy the recovery code uses. Nevertheless **do not silently accept a changed exact PID1902 primary HidHide PnP target**: if the current recovery contract rejects `RecoveredTargetChanged`, retain that fail-close result. No speculative identity/PnP migration in this PoC.

### 3.3 Presentation / VIIPER retirement already exists

`src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`

- The current `_gate` serializes presentation operations.
- `RetireActivePresentationCoreAsync(reason)` already **stops and joins** the publisher, cancels the Xbox360 diagnostic loop, clears the feedback callback, drains callbacks, sends a physical STOP, writes neutral, and **detaches the active Xbox360/SteamDeck typed virtual device**.
- `ReconcileDesiredPresentationAsync(source, captureSnapshot, token)` already attaches exactly the current Steam/BPM-selected presentation when the live physical source is ready.
- `ReleaseForCenterMEnableAsync` calls **`RetireAsync`**, which tears down the canonical VIIPER runtime to Closed. This is **not** the right seam for temporarily retiring the selected typed device and reattaching it in the same Runtime.

Expose/reuse the **smallest internal presentation method** wrapping `RetireActivePresentationCoreAsync("DeveloperRumbleRearm")` under its existing gate, **without permanent VIIPER runtime teardown**. The existing detach proof must be a **hard barrier before any native PID switch**. If a publisher cannot stop/join, a callback cannot be safely drained, or VIIPER device detach cannot be confirmed, abort **before switching modes** and remain fail-closed. Preserve the current ownership evidence on failure.

**Physical rumble endpoint:** After final STOP and callback drain but **before** PID1902 re-enumeration, invalidate/release the cached retained HID handle in the existing `MsiClawRumbleSink` / transport. On successful reacquisition, it must resolve against the **new** physical session/generation. No concurrent old-session rumble writes.

### 3.4 The host is the existing operation coordinator

`src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

- Owns both `_physicalOwnership` and `_presentationOwnership`, Runtime shutdown admission, Center M authority state, actual model identity, device arrival/recovery scheduling, and persisted settings.
- Handles `RecoverOwnedControllerPhysicalInputAsync`, which reapplies saved LED/vibration settings and reconciles Steam/BPM virtual presentation on real recovery.
- Its device-arrival watcher and asynchronous presentation reconcile remain live. The Developer operation must be **coordinated with these existing owners**, not bypass them through a new frontend-owned HID command.
- The current explicit diagnostic-identity capture helper `CaptureLiveOwnedPhysicalIdentityForDeveloperProbe` is useful as a **prerequisite validation pattern**, but a mode cycle mutates device topology and requires much stricter retirement/recovery orchestration than a profile write.

### 3.5 Developer page + IPC already support diagnostics

- `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml[.cs]` already links to the `VibrationTestPage`.
- `src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml[.cs]` contains Xbox360 STOP loop, 0/100 pair-write probe, and A2VM LED readback probe, plus local `_busy` and `_profileProbeBusy` UI gates.
- `IAddonFrontendControl` is in `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`.
- `InProcessAddonFrontendControl` delegates Developer requests to the Runtime, and the named-pipe request path traverses `FrontendWire.cs`, `NamedPipeAddonFrontendClient.cs`, and `NamedPipeAddonFrontendServer.cs`.
- Current `FrontendTransportProtocol.CurrentVersion` is **69** on the reviewed main branch. Increment it **once** for the new typed RPC, with a short version history comment; no legacy compatibility shim for a pre-release frontend.

Place the button **inside the existing Vibration Test page**. Do not add a new Developer page, new tab, or a controller user setting.

---

## 4. Exact PoC operation and failure policy

### 4.1 Admission (no mutation if not proven)

The operation is initiated by one explicit Developer UI click (no automatic startup/resume/SteamOS heuristic). It must be **Unavailable** without switching anything if:

- Physical hardware model is not exact **`msi.claw.a2vm.8`**. Do not include the A2VM 7 or CG3EM in this PoC, even if other models also have vibration strength support.
- Runtime is shutting down, or Center M authority is not exactly **Disabled**, or the expected elevated single-user Runtime is not in normal Full1902 ownership.
- Current physical PID1902 ownership is not `Owned` with a live DirectInput source, strong unique physical identity, exact primary PID1902 target, and healthy current control HID.
- A prior input-loss recovery is active, cleanup is unproven, the power suspend/resume lifecycle is currently paused, the Overlay capture owns input, an authority release is in progress, or this same Developer re-arm is already running.
- The Xbox360 terminal STOP loop diagnostic or another currently exclusive motor test is running; user must stop it first. Do not silently interrupt concurrent diagnostics with physical motor writes.
- Safe presentation retirement cannot be guaranteed.

**No firmware/EC reset**, no `SyncToROM`, no power-management control, no persistent "rumble armed" flag, and no generic vendor command.

### 4.2 One controlled attempt (state change only after retirement)

Inside the **existing Runtime/owner authority**:

1. Log a distinct `DeveloperRumbleRearmStarted` with model, firmware if known, starting mode, physical-session generation, presentation kind and expected primary target.
2. Set a **single process-local in-progress gate** around this diagnostic so duplicate clicks and ordinary automatic recovery/presentation reconcile cannot take ownership of the temporary intentionally-stopped source. Reuse owner gates rather than adding a separate coordinator. Block beginning an authority transition/teardown in the normal owner order; if shutdown happens later, honor the existing shutdown fail-close policy.
3. Stop the motion reader and other active physical-input consumers through the host's existing seams as needed, and safely retire the active presentation with `RetireActivePresentationCoreAsync`: stop+join publisher, disarm+drain callbacks, physical STOP, neutral, detach. **No PID switch before verified detach.** Do not permanently dispose canonical VIIPER.
4. Invalidate the existing rumble endpoint/handle. **Stop and prove cleanup of** the exact process-owned DirectInput session, clear its published live physical session so stale rumble calls reject. Failure to stop/clean up is a hard barrier; do **not** switch PID.
5. Use the already-injected `_switchMode` under the physical owner's gate to request **`MsiClawNativeMode.XInput`** with the **pre-cycle PID1902 strong identity**. Require:
   - command write reported successful,
   - old PID1902 disappeared,
   - PID1901 appeared,
   - source identity and target topology verified,
   - a fresh stable native-state capture reports **XInput / PID1901 with Strong identity**.
6. With that **fresh PID1901 identity**, request the existing `_switchMode` to **`MsiClawNativeMode.DirectInput`**. Require the same actual-disappearance/arrival/source/target proof and fresh final native capture of **PID1902 / DirectInput**. **Do not** trust a stale PID1902 joystick discovery, a command's `WriteFile` success alone, or sending DInput while already in DInput. Allow the existing bounded per-transition settle; no arbitrary 2.5-second sleep on top of the current bounded PnP/DirectInput resolution.
7. Enter the existing stopped-session **physical recovery core** under the *same* owner gate. Re-prove the current PID1902 mode and first DirectInput input state, reconcile exact HidHide target set, require an unmodified exact primary target, and publish the freshly recovered generation/identity. Do **not** call `AcquireAsync` as a second startup or `RecoverLostInputAsync` while the source is still running.
8. After physical ownership succeeds, reapply persisted `ControllerVibration` strength (trigger `DeveloperRumbleRearm`) and existing LED settings through their current production helpers. These feature-local applies must **not** turn a safe recovered controller into a failed owned lifecycle; log any apply failure truthfully. Do **not** run an automatic motor pulse.
9. Reconcile the currently desired Steam/BPM presentation from a **fresh** snapshot. It may differ from the presentation active when the button was clicked. Start only one publisher/typed virtual device and re-arm its current feedback callback using the existing `ReconcileDesiredPresentationAsync`.
10. Log `DeveloperRumbleRearmCompleted`, capturing each transition's proof, recovered ownership, virtual presentation status, and the fact `PhysicalMotorEffectVerified=False`. Return a typed result. **Succeeded = verified software mode round-trip and safe controller restoration, not physical vibration measured.**

**Implementer flexibility:** The exact method boundaries may differ slightly if the existing `_gate` ownership or recovery interfaces demand it. Keep the **single host-coordinated operation with existing presentation/physical owners** and the above safety invariants. Do not introduce a second architecture or transient Windows service.

### 4.3 Failure / interruption

The only acceptable policy is **bounded, explicit, fail-close**:

- **Before first PID switch:** if retirement, STOP/drain, DI stop/cleanup or admission fails, do not issue any native mode write. If the presentation was already safely detached, a subsequent normal reconcile is allowed **only** when physical ownership is positively healthy; otherwise leave virtual devices detached.
- **PID1902 → PID1901 failure:** do not assume the old PID1902 survived or PID1901 was reached merely because the command returned; capture what actually happened. A single safe recovery to **verified PID1902** may use existing owner primitives if the current state uniquely supports it; no blind retry loop.
- **PID1901 → PID1902 failure:** attempt no unverified VIIPER reattach. Report the physical mode actually observed, preserve persistent HidHide and Center M Disabled authority, and require existing normal recovery/controlled Runtime restart or subsequent user action if safe recovery cannot be proven. **Do not silently switch to stock authority or expose a second virtual pad.**
- **Recovery target changed / HidHide not compliant / DI input absent / VIIPER detach unproven:** fail-close and retain relevant teardown/ownership evidence. No target migration or make-it-work resets.
- **Shutdown/suspend/device loss while running:** obey the existing lifecycle paths and cancellation/cleanup gates; do not allow reattachment after a shutdown/power-pause admission change. Once the first mode mutation has begun, do not abandon remaining cleanup merely because the Main UI page closes or the pipe disconnects; complete the bounded safe attempt or reach fail-close under Runtime ownership. No indefinite busy state.
- Suppress automatic recovery from the **intentional** temporary source stop, but do not block genuine later recovery after the Developer operation has ended. A normal Device Arrival from the command round-trip is not a reason to start a competing independent recovery while the diagnostic owns the operation.
- Only one attempt per button press, no automatic mode-cycle retries, and no periodic rumble health watcher.

**Critical distinction:** The Developer mode cycle is not the supported **Center M Enable-and-Restart** authority transition. In particular, it must never clear the permanent Disabled-mode HidHide baseline, set `_releasedForEnable`, or restart MSI Center M.

---

## 5. Frontend/UI contract

### 5.1 Typed contract

Add the smallest typed result to `FrontendContracts.cs`, for example:

```csharp
public enum FrontendRumbleRearmOutcome
{
    Completed,   // Lifecycle restored; physical vibration still unverified
    Unavailable, // Admission refused; no mode write
    Failed       // Operation started but was not fully restored
}

public sealed record FrontendRumbleRearmResult(
    FrontendRumbleRearmOutcome Outcome,
    string Status,
    bool XInputTransitionVerified,
    bool DirectInputTransitionVerified,
    bool PhysicalOwnershipRestored,
    bool PresentationRestored)
{
    public bool SoftwareCycleCompleted => Outcome == FrontendRumbleRearmOutcome.Completed;
}
```

The shape can be simplified if needed, but **do not return an unqualified `bool` called `RumbleArmed`**. Actual motor effect is only established by the user's subsequent test. A specific stage/reason can be included if useful for diagnosing a failure without reading giant logs.

Add one **parameterless** Developer request such as:

```csharp
Task<FrontendRumbleRearmResult> RunDeveloperRumbleRearmAsync(
    CancellationToken cancellationToken = default);
```

Wire end-to-end through `IAddonFrontendControl`, `InProcessAddonFrontendControl`, typed `FrontendRpcMethod`, named-pipe client/server dispatch, and increment the pipe protocol version. Keep the operation **in the Runtime**; the UI merely requests and displays it.

### 5.2 Vibration Test page

`VibrationTestPage.xaml`:

- Below the existing vibration profile probe or above it, add a section **`A2VM 8 Rumble Re-arm (Developer PoC)`**.
- Clearly warn: **`Temporarily disconnects the virtual controller and cycles physical PID1902 -> PID1901 -> PID1902. Close games and save progress before running. It may leave controller input unavailable if recovery fails.`**
- One **`Re-arm Rumble (A2VM 8)`** button, a lightweight inline status area, and optionally a confirmation dialog using existing WinUI conventions if one is already established. No new multi-step wizard.
- Disable this button while the command is pending or the Xbox360 loop/profile probe/LED read probe is running. Likewise disable competing local diagnostic controls while this command is in flight. The Runtime remains authoritative for admission/rejection.
- On `Completed`, display: **`Mode cycle and controller restoration completed. Physical motor vibration is NOT verified: go to Controller > Vibration Strength and run Left Test / Right Test.`**
- On `Unavailable` or `Failed`, display the specific stage/reason and advise the user to consult Runtime logs. **Do not** automatically call EC Reset, perform a second mode cycle, or declare motor arming success.
- Page navigation/dismissal must not cancel an already-started physical transition; the Runtime owns safety and the bounded recovery. No automatic trigger during UI load/Activate.

Use the existing `DeveloperPage` → `VibrationTestPage` navigation. **Do not add** a production Controller-page option, persistent Settings flag, startup preference, or overlay control.

---

## 6. Structured logs needed for hardware diagnosis

Use a small set of stable diagnostic events under `[Rumble]` or `[ControllerOwnership]`, with a common attempt identifier or correlation token **only if needed to associate one operation's lines**; do not build a tracing subsystem.

Suggested events:

```text
DeveloperRumbleRearmStarted
DeveloperRumbleRearmPresentationRetired
DeveloperRumbleRearmDirectInputStopped
DeveloperRumbleRearmXInputTransitionResult
DeveloperRumbleRearmDirectInputTransitionResult
DeveloperRumbleRearmPhysicalRecovered
DeveloperRumbleRearmPresentationRestored
DeveloperRumbleRearmCompleted
DeveloperRumbleRearmFailed
```

Log at least:

- model and firmware if read without creating another risky HID operation;
- starting PID, strong physical-identity proof (avoid sensitive/unstable full user-specific paths unless already logged conventionally);
- callback STOP result, detach proof, DI cleanup;
- each `MsiClawModeTransitionResult` status/reason, old-PID disappeared, new-PID arrived, source/target verified;
- final PID1902 native mode, GamepadMode ACK, recovered **exact** HidHide target and first valid DirectInput state;
- final virtual presentation kind / attach result and source `Steam/BPM` snapshot;
- final `PhysicalMotorEffectVerified=False` until the user separately reports physical Test behavior.

A HID write success does **not** justify `RumbleEngineArmed=True`. If the actual motor remains silent after a verified cycle, that negative hardware finding is the intended PoC outcome, not a failed code-review contract.

---

## 7. Automated tests and merge expectations (no device required)

Use the existing test projects and fakes for `MsiClawModeController`, `MsiClawAddonPhysicalOwnership`, `MsiClawAddonPresentation`, frontend/pipe, and UI structure. Focus on real, observable lifecycle safety—not narrow scheduler races.

### Required software scenarios

1. **Eligible A2VM 8, healthy owned PID1902:** one click performs exactly one true `1902 -> 1901` and one `1901 -> 1902`; both are positively verified, physical source is recovered, and exactly one currently desired presentation reattached.
2. **Already-PID1902:** it **must still switch to XInput first**. The Developer operation is not short-circuited to existing startup's `ModeWriteIssued=False`.
3. **Wrong model, Center M Enabled, missing/weak/ambiguous identity, source unavailable, suspend active, ongoing recovery, diagnostic already running:** `Unavailable`, **zero mode writes**.
4. **Teardown ordering:** complete and verified publisher stop/join + feedback callback drain, STOP, neutral and VIIPER detach **before** first physical mode mutation. If detach fails, **zero mode writes** and no second presentation.
5. **DirectInput cleanup fails:** zero PID transition commands, no false success and no duplicate input source.
6. **First transition command reports success without actual PID re-enumeration:** operation **fails**; never attempts an unverified follow-up against the wrong endpoint and never claims motor armed.
7. **Second transition fails / PID1901 remains:** no virtual controller reattached; controlled and bounded cleanup only; preserve stock-disabled authority and HidHide.
8. **Recovery refuses changed primary HidHide target, first-input failure, or isolation failure:** fail-close with no virtual reattachment.
9. **Successful restoration after deliberate PID switch:** saved vibration strength/LED are reapplied only after verified physical ownership; fresh Steam/BPM state selects Xbox360 or SteamDeck, not a stale pre-cycle selection.
10. **Single attempt / duplicate request:** no overlapping mode-cycle operations; the busy request is rejected. External Device Arrival does not create an overlapping recovery during this deliberate cycle, while normal genuine later recovery remains permitted.
11. **Frontend pipe:** exact typed payload, no unexpected request payload, version bump, UI status distinction between `Completed`, `Unavailable`, and `Failed`.
12. **No regression:** normal PID1902 boot/restart remains no-mode-write, normal owned recovery and Enable Center M-and-Restart remain unmodified, existing STOP test and profile probes still function.

Run a normal solution build and relevant automated tests. **Do not write artificial high-order interleaving tests** purely to justify more locks, epochs, managers, or another persisted state.

### Hardware work belongs exclusively to the user **after merge**

After merge, with the A2VM 8 in its currently silent-rumble state:

1. **Do not EC Reset first**; preserve the reproduction.
2. Close games, open **Settings > Developer Menu > Vibration Test**.
3. Record that Left/Right Test is **physically silent** before running (software `Succeeded` is not enough).
4. Click **Re-arm Rumble (A2VM 8)** exactly once and wait for status.
5. If software cycle completes, go to **Controller > Vibration Strength**, press **Left Test** and **Right Test** and note **felt vibration Yes/No** for each.
6. Optionally check Xbox360 and SteamDeck rumble afterward; they are separate source paths and should not be conflated with direct motor Test.
7. Upload `Addon/Log` Runtime/VIIPER logs; record success or failure. If physical recovery fails and controls disappear, avoid repeated automatic cycling. EC Reset remains the user-controlled last-resort recovery outside this PR.

**Physical behavior is research evidence, not a PR merge blocker.** Code review should block only realistic contract/lifecycle defects; theoretical races or requests to implement automatic full-firmware reinitialization are not grounds to block.

---

## 8. Change boundaries and acceptance criteria

Expected touched areas (exact file count may vary):

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs
tests/... focused existing projects
```

**Do not touch**:

- Normal Windows startup, shutdown, restart, sleep/resume, automatic PID normalization, Center M Disabled/Enabled reboot-bound authority, or stock-safe uninstall policy.
- MSI HID profile offsets/packet formats, `SyncToROM`, vibration strength persistence semantics, LED mapping, firmware `RESET_DEVICE`, or EC power registers.
- VIIPER native library, USB/IP kernel behavior, Steam Input/QAM or CTW integration.
- Other models, general controller automation, new UI setting, new background watcher, new abstract owner/manager.

**Done when:** the Developer button can request **one verified round-trip through current Runtime ownership**, correctly detach/recover/reconcile virtual presentation, expose honest typed/logged outcomes and no longer risk claiming success on a mere HID write. Automated tests pass. **Actual motor arming remains a post-merge user observation.**
