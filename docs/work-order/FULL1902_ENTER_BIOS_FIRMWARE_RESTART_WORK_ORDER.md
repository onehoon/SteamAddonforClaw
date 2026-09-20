# Work Order — Full1902: Enter BIOS Firmware Restart

## Status

Implementation work order for a small standalone Full1902 feature.

Current `main` baseline when this work order was prepared:

```text
168182c36b94c161bf9682e5a5530ee12eafcb3b
```

This work order is subordinate to the current Full1902 document authority order:

1. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. historical work orders only where they do not conflict with the documents above or current code.

The application is standalone. Do not reintroduce CTW integration or compatibility behavior.

---

## 1. Goal

Add one Settings-page action named exactly:

```text
Enter BIOS
```

The action must allow a Full1902 MSI Claw to restart directly into firmware/BIOS while ensuring the physical controller is temporarily restored to the BIOS-usable stock XInput/PID1901 state before Windows leaves the current session.

Primary Full1902 flow:

```text
Center M roots = Disabled
Addon owns PID1902 / DirectInput / HidHide / VIIPER
        ↓
Settings → Enter BIOS
        ↓
confirmation dialog
        ↓
prove current Runtime is safe for the transition
        ↓
retire front-button callbacks / virtual presentation
        ↓
stop DirectInput ownership
        ↓
same physical MSI Claw PID1902 → PID1901/XInput
        ↓
verify PID1901/XInput independently
        ↓
request: shutdown.exe /r /fw /t 0
        ↓
firmware/BIOS UI
```

When Windows later boots normally:

```text
Center M roots are still Disabled
        ↓
existing mandatory Addon Runtime starts
        ↓
existing Full1902 acquisition sees PID1901
        ↓
existing verified PID1901 → PID1902 reclaim
        ↓
DirectInput + HidHide + VIIPER presentation return normally
```

This is a temporary firmware-entry controller transition. It is **not** a controller-authority transfer to MSI Center M.

---

## 2. Product terminology

User-facing wording must use **BIOS**, not UEFI/Firmware terminology.

Settings card:

```text
Header:      Enter BIOS
Description: Restart the device and open BIOS settings.
Button:      Enter BIOS
```

Confirmation dialog:

```text
Title:
Enter BIOS?

Body:
The controller will temporarily switch to XInput so it can be used in BIOS.

Save your work before continuing.

Buttons:
[Cancel] [Restart and Enter BIOS]
```

The implementation may use internal names such as `FirmwareRestart` / `RequestFirmwareRestart` because those accurately describe the Windows mechanism. Do not expose those names in the normal UI.

---

## 3. Settings card — icon is mandatory

Add a normal CommunityToolkit `SettingsCard` to:

```text
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
```

Place it after the existing **Show only current power source** card and before **Required Components**.

Use a button inside the card. Do **not** make the whole card click-to-reboot; the user must intentionally press the `Enter BIOS` button and then confirm the dialog.

Required XAML shape:

```xml
<ctcontrols:SettingsCard
    x:Name="EnterBiosCard"
    Description="Restart the device and open BIOS settings."
    Header="Enter BIOS">
    <ctcontrols:SettingsCard.HeaderIcon>
        <SymbolIcon Symbol="Setting" />
    </ctcontrols:SettingsCard.HeaderIcon>
    <Button
        x:Name="EnterBiosButton"
        Click="EnterBiosButton_Click"
        Content="Enter BIOS" />
</ctcontrols:SettingsCard>
```

**The HeaderIcon is a required acceptance item. Do not omit it.**

`Symbol="Setting"` is an existing WinUI `Symbol` value and should be used unless the current Windows App SDK version in this repository proves it unavailable at compile time. Do not replace it with a blank placeholder.

---

## 4. Confirmation UX

Pressing the card button must open a confirmation dialog before any Runtime mutation or restart RPC is sent.

Requirements:

- Cancel sends **zero** Enter-BIOS RPCs.
- The destructive/restart action is `Restart and Enter BIOS`.
- Prefer the safe/default focus on Cancel rather than making Enter BIOS the accidental default keyboard action.
- Disable the Settings card/button while the request is in flight so one frontend cannot double-submit the same action.
- Do not add a `Restart Later` mode.

The UI owns only confirmation/rendering. It must not call `Process.Start("shutdown.exe", ...)`, native controller switching, HidHide, or VIIPER directly.

---

## 5. Windows firmware restart mechanism

Use the existing Runtime-owned Windows restart seam rather than starting shutdown.exe from the UI.

Microsoft documents `/fw` as causing the next restart to enter the firmware user interface when combined with a shutdown/restart option:

```text
shutdown.exe /r /fw /t 0
```

Reference:

```text
https://learn.microsoft.com/windows-server/administration/windows-commands/shutdown
```

Do **not** add `/f`.

The existing `WindowsRestartRequester` already proves a normal restart request by:

- starting `shutdown.exe`;
- waiting for the command process to exit for up to 5 seconds;
- requiring exit code 0;
- reporting failure otherwise.

Extend that narrow seam, for example:

```csharp
WindowsRestartRequestResult RequestRestart();
WindowsRestartRequestResult RequestFirmwareRestart();
```

The firmware implementation must use exactly the firmware restart argument set:

```text
/r /fw /t 0
```

Do not create a generalized power-management service, privileged reboot helper, WMI reboot abstraction, or shell-command framework for this feature.

---

## 6. Full1902 authority contract — MUST NOT change

### 6.1 Disabled/Add-on authority remains authoritative

For a normal Full1902 machine with Center M roots exactly Disabled:

```text
Controller authority = Addon Runtime
Desired physical PID = PID1902
Mandatory Runtime startup = unchanged
Persistent Addon HidHide baseline = unchanged
Center M startup roots = Disabled
Win+G suppression authority = unchanged
```

`Enter BIOS` temporarily puts the physical controller in PID1901/XInput only to make the controller usable in BIOS.

It must **not** reinterpret PID1901 as a persistent authority transfer.

### 6.2 Do not mutate these things

The Enter-BIOS flow must not:

- enable or disable either Center M Scheduled Task;
- change MSI Foundation Service startup type;
- clear or rewrite the Addon persistent HidHide baseline merely because BIOS is being entered;
- remove or disable the mandatory Addon startup registration;
- call the full `RestoreStockAuthorityCoreAsync(...)` path;
- invoke `_onStockAuthorityRestored` / release Full1902 Win+G suppression as though MSI authority had been restored;
- persist an `EnteringBios`, `ReturnFromBios`, `PreviousPid`, authority override, epoch, boot flag, or similar state.

The existing next-boot Full1902 reconcile is the return path. No BIOS-return state machine is required.

---

## 7. Reuse the existing verified release path, but do not reuse Center-M semantics blindly

Current code already has the real safety-critical pieces:

- `MsiClawAddonPresentation.ReleaseForCenterMEnableAsync(...)`
  - stops/joins the publisher;
  - sends neutral;
  - detaches the typed virtual controller;
  - tears down the canonical VIIPER runtime.
- `MsiClawAddonPhysicalOwnership.ReleaseForCenterMEnableAsync(...)`
  - runs under the physical owner gate;
  - stops DirectInput;
  - clears the live physical/rumble identity;
  - captures a strong MSI Claw identity;
  - performs the verified PID1902 → PID1901/XInput transition;
  - performs a fresh final XInput verification.
- `StockCenterMStartupBaseline.EstablishAsync(...)`
  - provides an independent current-world PID1901/XInput proof.
- `CenterMRebootAuthorityTransition`
  - already owns the in-memory serialization for reboot-bound controller transitions;
  - already owns the Windows restart requester;
  - already receives the release and stock-baseline delegates.

Do **not** duplicate the PID transition algorithm in a new BIOS-specific controller writer.

However, do **not** simply call `RestoreStockAuthorityCoreAsync("CenterMEnable")`, because that intentionally:

```text
restores PID1901
→ clears Addon HidHide ownership
→ enables Center M startup roots
→ releases stock-authority-side policy
```

and that is wrong for Enter BIOS.

### Recommended small refactor

Extract/reuse only the narrow physical/presentation release-to-XInput core needed by both operations.

A reasonable shape is:

```text
Center M Enable:
    retire presentation
    → release physical session to verified XInput/PID1901
    → independent stock baseline proof
    → clear HidHide
    → enable Center M roots
    → release stock-side policy
    → normal restart

Enter BIOS:
    retire presentation
    → release physical session to verified XInput/PID1901
    → independent XInput/PID1901 proof
    → KEEP HidHide
    → KEEP Center M roots exactly as they were
    → KEEP mandatory Runtime startup
    → firmware restart
```

It is acceptable to add a firmware-specific method to the existing presentation/physical owner interfaces if that keeps intent explicit, for example:

```csharp
Task<bool> ReleaseForFirmwareRestartAsync(CancellationToken cancellationToken);
Task<PhysicalOwnershipReleaseResult> ReleaseForFirmwareRestartAsync(CancellationToken cancellationToken);
```

Internally those methods should delegate to one shared private retirement/release core rather than copying the teardown and PID-transition code.

Do not build a new public/general-purpose "controller mode manager".

---

## 8. Reuse the existing reboot-bound serialization

This feature must not race a real Center M Enable/Disable authority transition.

Prefer reusing the existing `CenterMRebootAuthorityTransition` in-memory `_inProgress` serialization rather than adding a second independent high-level manager/gate.

A narrow method such as:

```csharp
Task<FrontendEnterBiosResult> RequestEnterBiosAsync(CancellationToken cancellationToken);
```

may live on the existing reboot-bound transition owner even though the operation itself does not change Center M authority.

Do not rename or reorganize the entire lifecycle architecture solely to make the class name more generic. A large naming refactor is not the goal of this PR.

The method must:

1. reject an overlapping reboot-bound authority/BIOS operation;
2. capture the real Center M startup state;
3. reject `Partial` / `Unavailable` authority truth rather than guessing;
4. use the existing lower-level Runtime safety decision so real routing/native/recovery/teardown work can finish;
5. honor frontend cancellation before mutation;
6. after the user-confirmed mutation begins, use Runtime-owned completion semantics rather than aborting teardown because the frontend pipe disappeared.

No extra epoch/barrier/transaction framework.

---

## 9. Enter-BIOS runtime flow

### 9.1 Center M Disabled — primary Full1902 case

Required order:

```text
capture Center M startup roots = exactly Disabled
→ prove lower-level controller Runtime is safe to terminate/transition
→ stop/dispose the front-button Runtime that targets the presentation
→ retire/neutralize/detach VIIPER presentation
→ stop DirectInput physical ownership
→ restore same strongly verified MSI Claw to PID1901/XInput
→ independently prove current PID1901/XInput
→ request shutdown.exe /r /fw /t 0
```

After the physical release commits, the current Runtime should treat that physical controller session as terminal for the current Windows session. Do not let normal owned-session recovery immediately fight the deliberate BIOS transition and switch it back to PID1902.

This terminal fact remains process-memory-only. Do not persist it.

### 9.2 Center M Enabled — stock case

The Settings action should also work when Center M roots are exactly Enabled.

There is normally no Addon Full1902 physical/presentation ownership to retire.

Required behavior:

```text
capture Center M startup roots = exactly Enabled
→ prove lower-level Runtime is safe
→ establish/verify the existing stock PID1901/XInput baseline
→ request shutdown.exe /r /fw /t 0
```

Do not disable Center M or create an Addon ownership session merely to enter BIOS.

### 9.3 Partial / Unavailable

For:

```text
Center M roots = Partial
or
Center M roots = Unavailable
```

fail closed before controller mutation or reboot request.

Do not guess which authority currently owns the controller.

---

## 10. Do not introduce an MSI "BIOS controller mode"

Do not add a new MSI native-mode write such as a guessed/RE-only `BIOS` mode for this PR.

The required and already validated product behavior is:

```text
firmware controller compatibility
→ physical MSI Claw in stock XInput / PID1901
```

Use the existing XInput/PID1901 transition and verification path.

A separate MSI BIOS-mode command would require independent hardware proof and is out of scope.

---

## 11. Restart request failure after PID1901 commit

A firmware restart command can fail because of OS policy, rights, firmware capability, or command execution failure. This is a realistic operation failure and must be handled explicitly.

If teardown + PID1901/XInput verification succeeded but `shutdown.exe /r /fw /t 0` fails:

```text
DO NOT claim success
DO NOT mutate Center M startup roots
DO NOT clear HidHide
DO NOT remove mandatory Addon startup
DO NOT invent a rollback transaction
DO NOT immediately fight the deliberate release by auto-reclaiming PID1902
```

Return a concise failure to the UI. Suggested message:

```text
BIOS restart could not be started. The controller is temporarily in XInput. Try Enter BIOS again or restart Windows.
```

The machine remains usable because the physical controller is now stock XInput/PID1901.

The user may retry Enter BIOS. Keep the firmware-release path idempotent enough that an already-retired presentation / already-XInput physical controller can reach the restart request again without reconstructing controller ownership.

If the user performs a normal Windows restart instead, the existing next-boot authority policy resolves the state:

- Center M Disabled → Runtime starts and reclaims PID1901 → PID1902;
- Center M Enabled → stock PID1901 remains correct.

Do not add a dedicated recovery daemon/state machine for this command-failure case.

---

## 12. Frontend contract and transport

Add one narrow Main-UI frontend operation. Suggested contract:

```csharp
public enum FrontendEnterBiosOutcome
{
    RestartRequested,
    Blocked,
    Failed,
    Unavailable
}

public sealed record FrontendEnterBiosResult(
    FrontendEnterBiosOutcome Outcome,
    string? FailureMessage)
{
    public bool Succeeded => Outcome == FrontendEnterBiosOutcome.RestartRequested;
}

Task<FrontendEnterBiosResult> RequestEnterBiosAsync(
    CancellationToken cancellationToken = default);
```

Equivalent naming is acceptable if it remains BIOS-specific and narrow.

Do not reuse `FrontendCenterMStartupMutationResult`; Enter BIOS is not a Center M startup mutation and must not return a fabricated Center M mutation snapshot.

Current frontend transport protocol on this baseline is:

```text
Version 35
```

Because this adds a new RPC method, bump the protocol exactly once to:

```text
Version 36
```

Update:

- `FrontendRpcMethod`;
- request/response transport handling;
- `NamedPipeAddonFrontendClient`;
- `NamedPipeAddonFrontendServer`;
- protocol-version comment;
- focused transport tests.

No backward-compatibility shim is required for this pre-release project.

This action is Main UI Settings only. Do not add it to QAM or Overlay.

---

## 13. UI implementation guidance

In `SettingsPage.xaml.cs`:

- add one in-flight guard for the Settings action, matching the page's existing update-action pattern;
- show the confirmation dialog;
- invoke `_frontend.RequestEnterBiosAsync()` only after confirmation;
- disable the Enter BIOS button/card while the call is active;
- on failure, show the concise returned error through the page's existing appropriate dialog/InfoBar pattern;
- do not add a timer/polling loop.

The operation should not require a live status refresh loop after success because Windows is expected to leave the session immediately.

If restart request fails, restore the button so the user can retry.

---

## 14. Logging

Keep logs event-oriented and concise.

Recommended events:

```text
EnterBiosRequested
EnterBiosBlocked
EnterBiosPresentationReleaseStarted/Failed
EnterBiosPhysicalReleaseStarted/Failed
EnterBiosPid1901Verified
EnterBiosFirmwareRestartRequested
EnterBiosFirmwareRestartFailed
```

Include stable reason/outcome fields where useful.

Do not continuously poll BIOS/firmware state.

Do not dump full HidHide state or controller reports for this feature.

---

## 15. Required automated tests

### 15.1 Settings card / icon

Pin that the Settings page contains:

```text
Header="Enter BIOS"
Description="Restart the device and open BIOS settings."
Button Content="Enter BIOS"
SettingsCard.HeaderIcon
SymbolIcon Symbol="Setting"
```

Also pin its intended placement:

```text
Show only current power source
→ Enter BIOS
→ Required Components
```

The icon assertion is required. Do not let a future UI cleanup silently remove it.

### 15.2 Confirmation

Verify:

```text
Cancel
→ zero RequestEnterBiosAsync calls
```

and:

```text
Restart and Enter BIOS
→ exactly one RequestEnterBiosAsync call
```

Pin duplicate-click suppression while the request is in flight.

### 15.3 Disabled Full1902 happy path

Pin the real order:

```text
safe preflight
→ front-button owner stopped
→ virtual presentation retired
→ DirectInput stopped
→ PID1902 → PID1901 verified
→ independent XInput baseline proof
→ firmware restart requested exactly once
```

Assert that the Enter-BIOS path does **not** call:

- Center M startup-root mutation;
- HidHide Enabled-mode clear;
- Addon startup-registration removal;
- stock-authority callback / Win+G suppression release.

### 15.4 Presentation release failure

```text
presentation cannot retire safely
→ physical PID switch not attempted
→ firmware restart not requested
```

### 15.5 Physical release / PID1901 verification failure

```text
DirectInput stop or PID1901 transition/proof fails
→ firmware restart not requested
→ failure returned
```

### 15.6 Already XInput

If the physical controller is already verified XInput/PID1901:

```text
do not issue an unnecessary native-mode write
→ final XInput proof succeeds
→ firmware restart may proceed
```

This is especially important for a retry after a firmware-restart command failure.

### 15.7 Center M Enabled

Pin:

```text
Center M roots exactly Enabled
→ no Addon authority/root/HidHide mutation
→ stock XInput baseline verified
→ firmware restart requested
```

### 15.8 Partial / Unavailable

```text
Partial or Unavailable startup truth
→ no presentation/physical mutation
→ no firmware restart
```

### 15.9 Busy real lifecycle operation

A real lower-level routing/native/recovery/teardown operation blocks Enter BIOS.

Do not add tests for artificial instruction-level timing interleavings.

### 15.10 Firmware restart requester

Pin exact production arguments:

```text
shutdown.exe /r /fw /t 0
```

Assert:

- no `/f`;
- started process must exit inside the existing bounded wait;
- exit code must be zero;
- start/timeout/non-zero/exception all return failure.

### 15.11 Firmware restart request failure

Pin:

```text
PID1901/XInput already verified
→ firmware restart request fails
→ result = Failed
→ no reverse PID/root/HidHide/startup mutation
→ retry remains possible
```

### 15.12 Next-boot Full1902 regression

Do not duplicate the whole existing PR5/PR9 suite.

Keep or add only the focused regression proof that existing Disabled startup behavior still accepts a real PID1901 baseline and reclaims it to PID1902 through the existing verified transition path.

The BIOS feature must not introduce a `ReturnFromBios` condition into that startup logic.

### 15.13 Transport

Pin protocol v36 and one complete named-pipe round trip for `RequestEnterBios`.

---

## 16. Manual hardware validation

Perform on a supported MSI Claw.

### A. Primary Full1902 / Center M Disabled case

Start from:

```text
Center M startup roots = exactly Disabled
physical = PID1902
DirectInput = live
HidHide = compliant Addon baseline
virtual presentation = attached
```

Then:

1. Open Settings.
2. Confirm the **Enter BIOS** card is visible and has its icon.
3. Press `Enter BIOS`.
4. Confirm `Restart and Enter BIOS`.
5. Verify the machine opens BIOS/firmware UI.
6. Verify the built-in controller can navigate BIOS as expected.
7. Exit BIOS and boot Windows.
8. Verify Center M startup roots are still Disabled.
9. Verify mandatory Addon Runtime starts.
10. Verify the physical controller returns to PID1902 through the existing startup reclaim.
11. Verify DirectInput/HidHide/VIIPER presentation returns normally.
12. Verify no duplicate physical + virtual controller remains visible in normal Windows gameplay state.

### B. Center M Enabled case

Start from normal stock authority:

```text
Center M startup roots = exactly Enabled
physical = PID1901/XInput
```

Run Enter BIOS and verify:

- BIOS opens;
- controller works;
- no Center M startup-root mutation occurred;
- next Windows boot remains stock authority.

### C. Restart-command failure if safely reproducible

If a non-destructive way to make the firmware restart request fail is available:

- verify failure is surfaced;
- verify physical controller remains usable as XInput;
- retrying Enter BIOS does not require reconstructing Full1902 ownership first;
- a normal Windows reboot under Disabled authority returns to PID1902 automatically.

Do not create risky system-policy changes solely to force this test.

---

## 17. Build / verification

Before considering the PR complete:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore --no-build
git diff --check
```

Requirements:

- Debug build clean;
- Release build clean;
- full automated suite green except existing intentional skips;
- no weakening/removal of current Full1902 lifecycle safety tests.

---

## 18. Out of scope / overengineering guard

Do not add:

- CTW integration;
- a service or supervisor;
- a BIOS-return daemon;
- persisted BIOS-entry state;
- a new controller-authority enum/state;
- a new HidHide authority layer;
- a generalized reboot/power manager;
- a generalized transaction/rollback engine;
- an epoch/barrier system;
- polling for firmware/BIOS state;
- speculative protection for pathological callback/interleaving races;
- a guessed MSI native `BIOS` mode;
- QAM/Overlay exposure.

Protect only real production lifecycle boundaries already relevant to the product:

- current controller operation in progress;
- safe publisher/VIIPER teardown;
- DirectInput ownership release;
- verified PID1902 → PID1901 transition;
- restart-command failure;
- next Windows boot reclaim.

---

## 19. Acceptance criteria

This PR is complete only when:

1. Settings contains an **Enter BIOS** SettingsCard.
2. The card has the required **Setting icon** and the icon is covered by a focused UI test.
3. The card description is `Restart the device and open BIOS settings.`.
4. Only the explicit button + confirmation can start the operation.
5. Cancel performs zero Runtime mutation.
6. Full1902 Disabled mode retires virtual presentation before physical release.
7. DirectInput is stopped before PID1902 → PID1901.
8. PID1901/XInput is independently verified before firmware restart.
9. The Runtime issues `shutdown.exe /r /fw /t 0` and never adds `/f`.
10. Enter BIOS does not clear persistent HidHide.
11. Enter BIOS does not enable Center M startup roots.
12. Enter BIOS does not remove/disable mandatory Addon startup.
13. Enter BIOS does not release Full1902 authority policy as though Center M had been enabled.
14. Center M Enabled stock mode can also enter BIOS without creating Addon controller authority.
15. Partial/Unavailable authority truth fails closed.
16. A failed firmware restart request is surfaced and leaves a usable temporary XInput controller without speculative rollback machinery.
17. Retrying after restart-request failure works with the already-XInput state.
18. Returning to Windows under Center M Disabled automatically reuses the existing PID1901 → PID1902 startup reclaim; no ReturnFromBios state exists.
19. Frontend transport is bumped once from v35 to v36 for the new Main-UI RPC.
20. QAM and Overlay remain unchanged.
21. Debug/Release builds and the full test suite pass.
22. Manual validation proves that the built-in controller is usable in BIOS and Full1902 is restored on the subsequent Windows boot.

---

## 20. Final implementation rule

> **Enter BIOS temporarily restores the physical MSI Claw to verified stock XInput/PID1901, then requests the Windows firmware restart path. It does not transfer persistent controller authority away from Full1902. Center M roots, Addon startup authority, and the persistent HidHide baseline remain untouched, and the existing next-boot Full1902 reconcile restores PID1902 automatically.**

Keep the implementation narrow, reuse the existing verified owners/gates, and do not add lifecycle machinery for theoretical races.
