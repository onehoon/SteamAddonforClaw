# Work Order — Standalone MSI Claw BIOS Mode Matrix Probe Console

> **Date:** 2026-09-24  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `acc810240800d373133d4a51667b03b01886c43a`  
> **Scope:** Developer-only standalone console probe for BIOS-input reverse engineering  
> **Product impact:** None. Do not change the production Runtime/UI/Frontend protocol.

---

## 1. Goal

Build a **small standalone x64 C# console application** inside this repository that can exhaustively test the interaction between:

```text
starting native controller state
    PID1901 / XInput
    PID1902 / DirectInput
```

and:

```text
final BIOS handoff GamepadMode
    KeepCurrent
    Mode0 Offline
    Mode1 XInput
    Mode2 DirectInput
    Mode3 MSI
    Mode4 Desktop
    Mode5 BIOS
    Mode6 Testing
```

The purpose is to perform one clean experimental sweep and answer:

> Is there any Windows-selectable MSI controller firmware mode which, after a standard Windows `/fw` restart, gives the MSI Claw built-in controller the same usable BIOS input behavior as the physical cold-boot `RB + RT + Power` path?

This is a reverse-engineering probe, not a product feature.

If the matrix produces no normal BIOS gamepad behavior, the result is useful negative evidence and the Enter-BIOS controller-mode line of investigation can be closed.

---

## 2. Evidence motivating the tool

Current hardware observations:

### Physical cold boot

```text
Power fully off
→ hold RB + RT
→ press Power
→ enter BIOS
→ built-in controller works normally for BIOS navigation
```

### Pure Windows firmware restart

From an elevated terminal:

```text
shutdown.exe /r /fw /t 0
```

with no MSI controller-mode command:

```text
→ BIOS opens
→ built-in controller input is completely dead
→ no usable buttons
```

### Mode5 handoff

Current Addon experiment:

```text
Windows
→ write GamepadMode 5 / BIOS
→ verify readback
→ shutdown /r /fw /t 0
```

produced:

```text
→ BIOS input exists
→ behavior is keyboard/mouse-like
→ not normal BIOS gamepad behavior
```

This proves GamepadMode can affect preboot input behavior, but Mode5 is not equivalent to the physical RB+RT cold-boot path.

Therefore the remaining useful experiment is an exhaustive matrix.

---

## 3. Required source/design reading before implementation

Read these documents first:

```text
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/work-order/FULL1902_ENTER_BIOS_MODE5_HANDOFF_AND_MODE2_BOOT_RECONCILE_WORK_ORDER.md
docs/work-order/FULL1902_PID1902_GAMEPADMODE2_INVARIANT_NORMALIZATION_WORK_ORDER.md
```

Read current implementations as protocol/reference material:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeCommand.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeContracts.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawGamepadModeClient.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs
src/SteamInputAddonforClaw/Controllers/Detection/WindowsControllerDeviceEnumerator.cs
src/SteamInputAddonforClaw/HidHide/HidHideDriverClient.cs
```

Do not invent a different MSI protocol when the current repository already contains the hardware-verified one.

---

## 4. Project location and isolation

Create a new standalone tool:

```text
tools/ClawBiosModeProbe/
    ClawBiosModeProbe.csproj
    app.manifest
    Program.cs
    ...
```

Recommended assembly:

```text
ClawBiosModeProbe.exe
```

Requirements:

- console application;
- x64 only;
- Windows only;
- .NET 10;
- framework-dependent is sufficient;
- `requireAdministrator` manifest;
- no packaging;
- no VeloPack integration;
- no startup registration;
- no installation into the product directory;
- no frontend/named-pipe integration;
- no production runtime dependency.

Prefer **not** to add the project to `SteamInputAddonforClaw.slnx` unless there is a concrete build reason.

It should be independently buildable:

```powershell
dotnet build tools/ClawBiosModeProbe/ClawBiosModeProbe.csproj -c Release
```

The normal application build/release pipeline must not publish or package this probe.

---

## 5. Do not reference the production project

Do not solve this by adding:

```text
ProjectReference -> SteamInputAddonforClaw
InternalsVisibleTo
public exposure of internal controller classes
new shared controller-protocol library
```

The probe is intentionally disposable research tooling.

Copy only the minimal, already-proven protocol/PnP/HID logic needed by the probe.

This duplication is preferable to changing production architecture for a temporary RE tool.

Keep copied code clearly labeled with the current source file it was derived from so later comparison is easy.

---

## 6. Exact GamepadMode protocol

Use the current verified MSI command format.

### Switch mode

64-byte output report:

```text
0F 00 00 3C 24 <mode> 00 ...
```

where:

```text
0 = Offline
1 = XInput
2 = DirectInput
3 = MSI
4 = Desktop
5 = BIOS
6 = Testing
```

### Read current mode

TX:

```text
0F 00 00 3C 26 ...
```

Expected ACK:

```text
10 00 00 3C 27 <mode> ...
```

As in production, read multiple reports from the **same open HID handle** and accept the first valid `0x27` ACK.

Do not assume the next report is necessarily the GamepadMode ACK.

---

## 7. Known command HID topology

The probe must support both known native controller PIDs.

### PID1901 / XInput

```text
VID       = 0x0DB0
PID       = 0x1901
UsagePage = 0xFFA0
Usage     = 0x0001
```

### PID1902 / DirectInput-family topology

```text
VID       = 0x0DB0
PID       = 0x1902
UsagePage = 0xFFF0
Usage     = 0x0040
```

Important:

```text
PID1902 != proof of GamepadMode 2
```

Real hardware has already shown:

```text
PID1902 + Mode2 = DirectInput/gamepad
PID1902 + Mode4 = Desktop/hardware-mouse personality
```

Therefore always record PID and GamepadMode independently.

---

## 8. Experiment matrix

Support exactly two selectable starting baselines:

```text
1. XInput     / expected PID1901 / expected Mode1
2. DirectInput / expected PID1902 / expected Mode2
```

Support eight final handoff selections:

```text
K. KeepCurrent
0. Offline
1. XInput
2. DirectInput
3. MSI
4. Desktop
5. BIOS
6. Testing
```

This gives 16 useful cases:

```text
PID1901 baseline × KeepCurrent
PID1901 baseline × Mode0
PID1901 baseline × Mode1
PID1901 baseline × Mode2
PID1901 baseline × Mode3
PID1901 baseline × Mode4
PID1901 baseline × Mode5
PID1901 baseline × Mode6

PID1902 baseline × KeepCurrent
PID1902 baseline × Mode0
PID1902 baseline × Mode1
PID1902 baseline × Mode2
PID1902 baseline × Mode3
PID1902 baseline × Mode4
PID1902 baseline × Mode5
PID1902 baseline × Mode6
```

Do not automatically run the matrix in one process.

Every case requires a firmware reboot and manual BIOS observation, so the user chooses exactly one case per execution.

---

## 9. Why KeepCurrent is a separate handoff

`KeepCurrent` means:

> Normalize/verify the requested starting baseline, then send **no additional SwitchMode command** immediately before `/fw`.

This distinguishes:

```text
PID1902 / Mode2
→ no final command
→ /fw
```

from:

```text
PID1902 / Mode2
→ explicitly write Mode2 again
→ /fw
```

and similarly for PID1901 / Mode1.

Do not collapse these cases.

---

## 10. Startup conflict guard

This standalone tool must not fight the production Addon owner.

At startup, detect whether a live process named:

```text
SteamInputAddonforClaw
```

is running.

If it is running:

```text
REFUSE TO MUTATE THE CONTROLLER
```

and print a clear developer message:

```text
SteamInputAddonforClaw Runtime is active.
Stop the Runtime before running the BIOS mode probe.
The Full1902 owner may otherwise immediately normalize the controller back to Mode2.
```

Do not auto-kill the Addon process.

Also show the current Center M startup-root state when practical:

```text
MSI_Center_M_Server
MSI_Center_M_Updater
MSI Foundation Service
```

but do not build another Center M startup manager into the probe.

If a clearly identifiable Center M controller process is already active, warn and refuse mutation rather than attempting to fight it.

Do not add broad process-name killing.

---

## 11. HidHide access

A standalone executable may not be in the current Full1902 HidHide application allow-list.

The probe must therefore handle its own temporary access without changing the product baseline permanently.

Preferred behavior:

```text
inspect current HidHide configuration

if HidHide is inactive:
    do nothing

if HidHide is active and probe executable is already allowed:
    remember "already present"
    do nothing

if HidHide is active and probe executable is not allowed:
    add EXACT current probe executable path to Applications
    verify it is present
    mark "temporary entry added"
```

Do not change:

```text
Active
Inverse
Hidden Devices
other application entries
```

Do not normalize the full HidHide baseline.

Use the same driver contract/path-conversion semantics as current `HidHideDriverClient`, copied narrowly into the tool if necessary.

### Cleanup

Before invoking `shutdown.exe /r /fw /t 0`:

```text
if this run added the probe whitelist entry:
    remove the exact probe entry
    verify removal
```

Also remove it in normal no-reboot/exit cleanup.

If cleanup cannot be verified:

```text
print a prominent warning
do not silently claim cleanup succeeded
```

Do not remove an entry that existed before the probe started.

---

## 12. Initial observation

Before changing anything, capture and print:

```text
Timestamp
Elevated = true
AddonRuntimeRunning
CenterMStartupState if available
HidHideActive
HidHideProbeWhitelisted
Present PID1901 count
Present PID1902 count
Resolved command HID
Current GamepadMode readback if available
Physical identity/root information
```

Require exactly one supported MSI Claw logical physical controller for mutation.

If device identity is ambiguous:

```text
abort
zero mode writes
zero firmware restart
```

Do not mutate merely because one VID/PID node happens to exist.

---

## 13. Starting-baseline normalization

The selected starting state is an experiment precondition and must be verified before the final handoff is applied.

### XInput / PID1901 baseline

Target:

```text
GamepadMode = 1
PID = 1901
```

If already:

```text
PID1901 + Mode1
```

do not issue an unnecessary baseline write.

Otherwise:

```text
resolve current strong command HID
→ write Mode1
→ bounded PnP settle
→ require exactly one supported PID1901 logical target
→ resolve fresh command HID
→ ReadGamepadMode
→ require Mode1
```

### DirectInput / PID1902 baseline

Target:

```text
GamepadMode = 2
PID = 1902
```

If already:

```text
PID1902 + Mode2
```

do not issue an unnecessary baseline write.

Otherwise:

```text
resolve current strong command HID
→ write Mode2
→ bounded PnP settle
→ require exactly one supported PID1902 logical target
→ resolve fresh command HID
→ ReadGamepadMode
→ require Mode2
```

Use a bounded settle window similar to the production native-mode path.

Suggested maximum:

```text
5 seconds
poll around 50-100 ms
```

No indefinite retry.

If the requested starting baseline cannot be proven:

```text
abort this test case
do not execute handoff
do not request /fw
```

---

## 14. Final handoff behavior

After the starting baseline is proven, capture a `PreHandoff` snapshot.

### KeepCurrent

```text
send no SwitchMode command
```

Then capture the best possible current PID/mode snapshot and continue to the final confirmation.

### Mode0..Mode6

Write exactly one final handoff command:

```text
0F 00 00 3C 24 <selected mode> 00
```

Do not add automatic retries of the final handoff write.

After the write, wait a bounded observation window and capture whatever the hardware actually exposes.

---

## 15. Do not assume final PID for Mode3/4/5/6/0

For the final experimental handoff, the probe must **observe** the hardware rather than enforcing product expectations.

Known:

```text
Mode1 usually maps to PID1901
Mode2 usually maps to PID1902
Mode4 can remain PID1902
```

Unknown/experimental:

```text
Mode0 Offline
Mode3 MSI
Mode5 BIOS
Mode6 Testing
```

For those modes, record:

```text
Observed PID1901 present?
Observed PID1902 present?
Command HID still resolvable?
GamepadMode readback available?
Observed GamepadMode?
PnP topology changed?
```

Do not force the device back to a guessed PID before BIOS restart.

The whole purpose is to preserve the selected observed handoff state.

---

## 16. Mode0 and Mode6 are intentionally included

This is developer-only exhaustive RE tooling.

Include:

```text
Mode0 Offline
Mode6 Testing
```

because the stated objective is to test every defined firmware mode once and close the investigation if none works.

However these two selections require a stronger confirmation message because they may make the controller unavailable or expose unknown behavior.

Before writing Mode0 or Mode6, require typing an explicit token, for example:

```text
TYPE: MODE0
TYPE: MODE6
```

A simple Y/N is not sufficient for these two experimental modes.

Do not add any hidden undocumented mode values outside 0..6.

---

## 17. Readback policy after final handoff

The final handoff phase is an experiment, not product desired-state reconciliation.

Therefore distinguish:

```text
WriteSucceeded
ReadbackVerified
ObservedMode
ObservedPID
```

Do **not** require Mode0..6 to remain readable in order for the experiment to continue.

Examples:

### Mode3 write succeeded, readback Mode3

```text
clean verified experimental state
```

### Mode0 write succeeded, command HID disappears

```text
valid experimental observation:
WriteSucceeded=true
Readback=Unavailable
ObservedPID=None or changed
```

### write failed before any state change

```text
do not proceed as if the selected handoff happened
```

If final write succeeded but readback/topology cannot be proven, show a strong warning and require a second explicit confirmation before firmware restart.

Do not automatically restore Mode2 simply because readback is unusual.

---

## 18. Firmware restart

Use only the Windows firmware reboot request:

```text
shutdown.exe /r /fw /t 0
```

Do not pass:

```text
/f
```

The probe is already elevated, so no second `runas` helper is needed.

Immediately before calling `shutdown.exe`:

1. flush the experiment log;
2. remove any temporary probe HidHide whitelist entry;
3. verify that cleanup if possible;
4. print the exact final observed state;
5. require one final confirmation.

Suggested prompt:

```text
About to reboot into firmware UI.

Start baseline : DirectInput / PID1902 / Mode2
Final handoff  : Mode3 MSI
Observed PID   : 1902
Observed Mode  : 3 / MSI
Readback       : Verified

Press ENTER to run:
shutdown.exe /r /fw /t 0

Press Q to cancel.
```

If the user cancels, leave the selected controller handoff state intact unless they explicitly choose a restore action.

---

## 19. Optional restore action before reboot

Provide one explicit command in the final screen:

```text
R = Restore to DirectInput / Mode2
```

This is for a cancelled experiment.

Restore behavior:

```text
current state
→ Mode2
→ bounded PID1902 settle
→ ReadGamepadMode == 2
```

Do not restore automatically on every cancellation because a developer may want to inspect the selected Windows state manually.

Do not restore to PID1901 by default.

---

## 20. Console UX

Keep the UI simple.

Example:

```text
MSI Claw BIOS Mode Matrix Probe
Developer / hardware RE tool

Current state
-------------
PID1901 : absent
PID1902 : present
Mode    : 2 / DirectInput
HidHide : Active
Probe access : Temporary whitelist active

Choose starting baseline:
  1  XInput      / PID1901 / Mode1
  2  DirectInput / PID1902 / Mode2

> 2

Starting baseline verified:
  PID  = 1902
  Mode = 2 / DirectInput

Choose final BIOS handoff:
  K  Keep current (no final mode write)
  0  Offline
  1  XInput
  2  DirectInput
  3  MSI
  4  Desktop
  5  BIOS
  6  Testing

> 3

Final handoff:
  Write          = succeeded
  Observed PID   = 1902
  Observed Mode  = 3 / MSI
  Readback       = verified

ENTER = restart to BIOS
R     = restore Mode2
Q     = quit without reboot
```

Do not add GUI, WinUI, frontend RPC, menus, tray integration, or persistent app settings.

---

## 21. Result logging

Create a tool-local log directory:

```text
tools/ClawBiosModeProbe/logs/
```

or, for built binaries, a sibling:

```text
<probe directory>\logs
```

Do not write into the production Addon log folder automatically.

Each execution gets a timestamped text log:

```text
20260924_091530_StartPID1902_HandoffMode3.log
```

Also maintain a simple CSV matrix file:

```text
bios-mode-matrix.csv
```

Recommended columns:

```text
Timestamp
ToolVersion
RequestedStartState
InitialPid
InitialGamepadMode
StartWriteIssued
StartPid
StartGamepadMode
StartVerified
RequestedHandoff
HandoffWriteIssued
HandoffWriteSucceeded
PostHandoffPid1901Present
PostHandoffPid1902Present
PostHandoffGamepadMode
PostHandoffReadbackVerified
FirmwareRestartRequested
BiosInputResult
Notes
```

Before reboot:

```text
BiosInputResult = Pending
```

---

## 22. Manual BIOS result completion

On the next launch, if the CSV contains the most recent row with:

```text
FirmwareRestartRequested=true
BiosInputResult=Pending
```

prompt once:

```text
Previous BIOS test result?

0 = No input
1 = Keyboard / mouse style
2 = Normal gamepad navigation
3 = Mixed / partial
4 = Not tested / unknown
```

Update that row.

Allow an optional short note.

This makes the final 16-case matrix self-contained without attempting to automate BIOS observation.

Do not automatically infer the BIOS result from the Windows state after returning.

---

## 23. Recommended test order

The tool supports all cases, but the suggested manual sequence is:

```text
PID1902 + KeepCurrent   -- already known: no input; record as baseline
PID1902 + Mode3 MSI
PID1902 + Mode1 XInput
PID1902 + Mode4 Desktop
PID1902 + Mode5 BIOS   -- already known: keyboard/mouse; record formally
PID1902 + Mode0 Offline
PID1902 + Mode6 Testing
PID1902 + Mode2 explicit

PID1901 + KeepCurrent
PID1901 + Mode3 MSI
PID1901 + Mode2 DirectInput
PID1901 + Mode4 Desktop
PID1901 + Mode5 BIOS
PID1901 + Mode0 Offline
PID1901 + Mode6 Testing
PID1901 + Mode1 explicit
```

Order is not enforced in code.

The user may stop as soon as one mode produces normal BIOS gamepad navigation.

---

## 24. Minimal internal structure

Keep the project small.

Suggested shape only:

```text
Program.cs
ProbeModels.cs
MsiClawPnP.cs
MsiClawHid.cs
TemporaryHidHideAccess.cs
ProbeLog.cs
app.manifest
ClawBiosModeProbe.csproj
```

Do not create:

```text
ControllerAuthorityManager
ProbeStateMachine
ModeStrategy hierarchy
Repository pattern
service container
dependency injection framework
generic hardware abstraction framework
background worker
watchdog
event bus
```

A few small static/sealed classes are sufficient.

---

## 25. PnP/identity rules

Reuse the current product's strong-identity concepts narrowly.

At minimum:

- VID must be MSI `0x0DB0`;
- PID must be one of known controller PIDs `0x1901` / `0x1902` when present;
- resolve one logical physical controller root;
- command HID must correspond to that same physical controller;
- multiple independent MSI Claw roots are ambiguous and must block writes.

The probe is not required to support multiple Claw devices.

Do not select the first matching HID path blindly.

---

## 26. Bounded observation after experimental write

For a final handoff write, use a bounded observation window, for example:

```text
up to 3-5 seconds
```

During the window:

- observe PID1901/PID1902 presence;
- re-resolve command HID if possible;
- attempt a GamepadMode query if possible;
- stop early when a stable readback is obtained.

Do not issue additional final-mode writes during this observation window.

One selected handoff = one final experimental write.

---

## 27. Failure handling

### Before baseline verified

Any failure:

```text
abort
no /fw reboot
```

### Final handoff write failed

```text
do not claim selected mode
do not silently reboot
offer:
  retry entire case from menu
  restore Mode2
  quit
```

Do not auto-repeat the write internally.

### Final write succeeded but readback unavailable

Record the result faithfully.

Allow firmware restart only after explicit confirmation.

### HidHide temporary-access setup fails

Do not continue with blind HID writes.

Abort before controller mutation.

### Temporary HidHide cleanup fails

Log prominently.

Allow the user to choose whether to continue to reboot, but do not claim a clean test environment.

---

## 28. No production authority mutation

The probe must not:

- enable/disable Center M scheduled tasks;
- change MSI Foundation Service startup type;
- alter Addon startup registration;
- create VIIPER devices;
- attach X360/SteamDeck;
- start DirectInput polling;
- change Steam/BPM state;
- arm/disarm Win+G suppression;
- touch OEM1/WING mappings;
- alter production settings files;
- invoke production frontend IPC;
- edit the production HidHide hidden-device set.

It only needs:

```text
temporary self whitelist if required
PnP/HID observe
GamepadMode write/read
Windows /fw reboot
experiment logs
```

---

## 29. Tests

Because this is hardware RE tooling, do not build a large test suite.

Add focused unit tests only for pure logic that can regress easily.

Suggested test project:

```text
tools/ClawBiosModeProbe.Tests/
```

or keep tiny pure tests in an existing test project only if that does not create a production dependency.

Minimum useful tests:

### Command bytes

```text
Mode0 -> 0F 00 00 3C 24 00 00
Mode1 -> 0F 00 00 3C 24 01 00
...
Mode6 -> 0F 00 00 3C 24 06 00
Read  -> 0F 00 00 3C 26
```

### ACK parser

Accept:

```text
10 00 00 3C 27 00..06
```

Reject:

- too short;
- wrong report id;
- wrong marker;
- wrong command;
- mode > 6.

### Menu mapping

Verify:

```text
K -> KeepCurrent
0..6 -> exact enum value
```

### Matrix rows

Verify the 2 × 8 combinations are representable without duplicate/missing values.

Do not mock the entire Windows device stack just to increase test count.

Hardware behavior is validated on the real device.

---

## 30. Manual validation before BIOS matrix run

Before trusting the tool, test without firmware reboot.

### Read-only inspection

Run the probe and confirm current:

```text
PID
command HID
GamepadMode
```

match known current Addon logs/hardware state.

### PID1902 baseline

From a normal Full1902 Mode2 state with the Addon Runtime stopped:

```text
choose DirectInput/PID1902
→ no unnecessary write
→ Mode2 verified
```

### PID1902 -> PID1901 -> PID1902 round trip

Without `/fw`:

```text
start PID1902
→ normalize PID1901/Mode1
→ verify
→ restore PID1902/Mode2
→ verify
```

This proves the probe's baseline transition and PnP observation before testing BIOS handoffs.

### Same-PID Mode4 -> Mode2

If practical:

```text
PID1902 / Mode2
→ handoff Mode4 without reboot
→ observe PID1902 + Mode4
→ restore Mode2
→ verify PID1902 + Mode2
```

This should match already-known CTW/Addon hardware behavior.

Only after these smoke tests should the firmware reboot matrix be used.

---

## 31. Build requirements

Recommended project:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PlatformTarget>x64</PlatformTarget>
<SelfContained>false</SelfContained>
<OutputType>Exe</OutputType>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<ApplicationManifest>app.manifest</ApplicationManifest>
```

Keep dependencies minimal.

If WinRT `Windows.Devices.Enumeration` / `Windows.Devices.HumanInterfaceDevice` is the smallest path, use the same Windows SDK targeting already used by the main application.

Do not add unrelated NuGet packages.

---

## 32. Definition of a successful research sweep

The tool itself is successful when we can fill a table like:

| Start baseline | Final handoff | Pre-reboot observed PID | Pre-reboot observed Mode | BIOS result |
|---|---|---:|---:|---|
| PID1902 / Mode2 | KeepCurrent | 1902 | 2 | No input |
| PID1902 / Mode2 | Mode3 MSI | ? | 3? | ? |
| PID1902 / Mode2 | Mode5 BIOS | ? | 5? | Keyboard/mouse |
| PID1901 / Mode1 | KeepCurrent | 1901 | 1 | ? |
| ... | ... | ... | ... | ... |

The research question is answered by the BIOS-result column.

If any case gives:

```text
Normal gamepad navigation
```

capture that exact pre-reboot PID + GamepadMode combination for production analysis.

If no case gives normal gamepad navigation:

> Stop extending GamepadMode-based Enter-BIOS behavior. Treat the RB+RT cold-boot BIOS input path as a separate preboot/firmware path not reproduced by the known Windows GamepadMode protocol.

That negative conclusion is acceptable and is one of the explicit goals of the probe.

---

## 33. Explicit non-goals

Do not implement in this work:

- production Enter BIOS changes;
- Settings-page BIOS mode dropdown;
- frontend protocol changes;
- automatic exhaustive reboot loop;
- firmware/EC reverse engineering;
- arbitrary HID fuzzing;
- unknown mode values outside 0..6;
- automatic BIOS input detection;
- keyboard/mouse event injection;
- BIOS automation;
- controller remapping;
- VIIPER;
- Steam;
- HidHide hidden-device mutation;
- Center M ownership changes;
- recovery watchdogs;
- new product abstractions.

---

## 34. Safety / overengineering policy

This is a direct hardware experiment tool.

Protect only practical failure modes:

- wrong/ambiguous physical controller;
- Addon Runtime fighting the probe;
- active HidHide blocking the probe;
- mode write failure;
- PID re-enumeration delay;
- command HID disappearing;
- readback unavailable;
- experimental Offline/Testing modes;
- temporary whitelist cleanup failure;
- `shutdown /fw` failure.

Do not add machinery for theoretical instruction-level races.

One interactive process, one MSI Claw, one experiment at a time.

---

## 35. Acceptance criteria

The work is complete when:

1. A standalone `ClawBiosModeProbe.exe` builds independently.
2. No production Runtime/UI/frontend source needs modification.
3. The probe refuses controller mutation while `SteamInputAddonforClaw` is running.
4. It uniquely resolves one supported MSI Claw before any write.
5. It can read current GamepadMode using the existing `0x26/0x27` protocol.
6. It can normalize and verify the starting baseline to PID1901/Mode1.
7. It can normalize and verify the starting baseline to PID1902/Mode2.
8. It exposes `KeepCurrent` and every defined Mode0..6 as final handoff choices.
9. `KeepCurrent` sends no final mode command.
10. Every explicit final mode selection sends exactly one final experimental SwitchMode command.
11. It records PID and GamepadMode independently after the final handoff.
12. It does not assume Mode3/4/5/6/0 have a particular PID.
13. Readback loss after an experimental write is recorded rather than silently normalized away.
14. Mode0 and Mode6 require an explicit stronger confirmation.
15. It temporarily whitelists only its own executable in HidHide when required.
16. It preserves all existing HidHide hidden devices/global flags/other applications.
17. It removes only a whitelist entry that this run added and verifies cleanup before reboot when possible.
18. It logs each case and maintains a simple matrix CSV.
19. The next run can record the previous manual BIOS result.
20. Firmware restart uses exactly `shutdown.exe /r /fw /t 0` with no `/f`.
21. A cancelled experiment can optionally restore Mode2/PID1902.
22. The probe never changes Center M startup authority, VIIPER, Addon startup, or product settings.
23. Basic command/parser/menu tests pass.
24. A real-device PID1902↔PID1901 smoke round trip succeeds before BIOS testing.
25. The resulting 16-case sweep is sufficient to either identify a usable BIOS-gamepad handoff or close this line of investigation.

---

## 36. Final implementation principle

Do not turn this into a product subsystem.

The entire purpose is:

```text
choose known starting native state
→ prove it
→ choose one firmware GamepadMode handoff
→ observe actual PID + firmware mode
→ log it
→ standard Windows /fw reboot
→ manually classify BIOS input
```

Then repeat until the matrix is complete.

This probe should make it possible to answer the BIOS-mode question once, with clean evidence, and then either reuse the one working combination or delete the tool.
