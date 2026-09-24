# Work Order — Deterministic Xbox360 RumbleProbe Console and Correlation Logging

## Baseline

Repository:

```text
onehoon/SteamAddonforClaw
```

Implementation baseline reviewed for this work order:

```text
branch: main
commit: e79cb0f2f1cbcf1245dd97b9c6011bfc0d76e743
```

Read before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/XBOX360_RUMBLE_CTW_VIIPER_ADDON_COMPARATIVE_ANALYSIS_2026-09-24.md`
- `docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md`
- `docs/work-order/FULL1902_XBOX360_RUMBLE_LATCH_SAFETY_STOP_HOTFIX_WORK_ORDER.md`
- `src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRumbleTransport.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperRuntime.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs`

---

# 1. Goal

Create a small standalone console probe inside this repository that generates repeatable Xbox360 vibration commands through the **real Windows XInput host path**.

The measurement path must be:

```text
RumbleProbe
-> XInputSetState(slot, vibration)
-> Windows XInput / XUSB
-> attached VIIPER Xbox360 device
-> USB/IP OUT
-> VIIPER Xbox360.HandleTransfer
-> SetXbox360RumbleCallback
-> SteamInputAddonforClaw Xbox360RumbleFeedbackBridge
-> MsiClawRumbleSink
-> physical MSI Claw HID 0x05
```

This is specifically intended to replace inconsistent game-driven reproduction with deterministic commands.

---

# 2. Do not restore the old Developer Vibration Test

The Developer Vibration Test UI currently remains intentionally disconnected after Full1902 Cleanup J.

Do not restore:

- `RunVibrationTest`;
- `OpenVibrationTestSession`;
- `CloseVibrationTestSession`;
- the old frontend vibration RPC DTOs;
- `FeedbackAuthority`;
- the old developer vibration session/lease model;
- the old routing-era SteamDeck synthetic test path.

That old test path directly exercised application/physical feedback logic and does not answer the current missing-terminal-STOP question before `OnRumbleReceived`.

This work must stay independent of the main UI/frontend transport.

---

# 3. New project

Add:

```text
tools/RumbleProbe/
    RumbleProbe.csproj
    Program.cs
```

Keep it a plain Windows console application.

Requirements:

- no WinUI;
- no frontend RPC;
- no dependency on the running Addon process;
- no package/release inclusion;
- no startup registration;
- no installer changes;
- no admin requirement added by the tool;
- no external NuGet dependency unless absolutely required.

Use Windows `XInputSetState` directly via a narrow P/Invoke.

Prefer `xinput1_4.dll` on the supported Windows 11 target.

The tool is a repository diagnostic utility, not a product feature.

---

# 4. Precondition

The test is meaningful only when the active Full1902 presentation is Xbox360.

Current product contract:

```text
Center M Disabled / Addon authority
Steam/BPM inactive -> Xbox360 attached/live
Steam/BPM active   -> SteamDeck attached/live
```

Before running the probe:

- Center M must be Disabled / Addon authority active;
- Steam game and BPM should be inactive;
- the Addon should be presenting the canonical VIIPER Xbox360 device;
- physical PID1902 remains hidden through the normal Addon HidHide baseline.

Do not add a second controller authority or force presentation changes from the probe.

If the operator needs the X360 presentation, they should establish the normal product condition rather than having RumbleProbe attach/detach VIIPER itself.

---

# 5. XInput slot handling

The probe must not assume slot 0 silently.

Provide:

```text
RumbleProbe list
```

that checks XInput slots 0-3 using the normal XInput APIs and reports which slots respond.

The actual test command requires:

```text
--slot 0..3
```

If no slot is explicitly supplied:

- automatically select it only when exactly one XInput slot is connected;
- otherwise fail with a clear message and require `--slot`.

Do not invent VID/PID-to-XInput-slot mapping logic.

The probe is intentionally small.

---

# 6. 8-bit motor input convention

The native VIIPER Xbox360 callback exposes motor values as 8-bit values.

Make the probe accept diagnostic amplitudes in the same human-readable domain:

```text
--left8  0..255
--right8 0..255
```

Convert to XInput's 16-bit motor range with exact full-range expansion:

```csharp
ushort Expand(byte value) => (ushort)(value * 257);
```

Log both:

- requested 8-bit value;
- actual 16-bit value passed to `XInputSetState`.

This does not assume that Windows will emit a particular USB packet. The VIIPER trace is what measures the resulting host OUT packet.

---

# 7. Required commands

Keep the CLI small. Do not build a general script language.

## 7.1 list

```text
RumbleProbe list
```

Output connected XInput slots.

## 7.2 pulse

Example:

```text
RumbleProbe pulse --slot 0 --left8 3 --right8 6 --iterations 200 --on-ms 250 --off-ms 250
```

For each iteration:

```text
send non-zero pair
wait on-ms
send 0/0
wait off-ms
```

This is the primary missing-STOP reproduction command.

Defaults may be:

```text
left8=3
right8=6
iterations=200
on-ms=250
off-ms=250
```

but all must be visible in the session header.

## 7.3 sequence

Provide one fixed built-in deterministic sequence covering weak and strong values.

For example:

```text
1,1
0,0
3,6
0,0
16,0
0,0
64,32
0,0
255,255
0,0
```

Repeat it with:

```text
--iterations <n>
```

This makes log alignment visually obvious and verifies value translation across multiple amplitudes.

Do not make the sequence user-programmable in this PR.

## 7.4 deadman

Provide one explicit Addon-safety validation command:

```text
RumbleProbe deadman --slot 0 --left8 3 --right8 6 --idle-ms 7000
```

Behavior:

```text
send one non-zero state
perform no XInputSetState call during idle-ms
after the idle window, send explicit 0/0 as final cleanup
```

This verifies the existing Addon 5-second physical safety stop without pretending the probe itself generated a host STOP during the idle period.

This command is secondary to `pulse`.

---

# 8. Safety

Every command that can set non-zero rumble must guarantee a final best-effort:

```text
XInputSetState(slot, 0, 0)
```

on:

- normal completion;
- Ctrl+C;
- handled exception;
- any non-success `XInputSetState` result encountered during the requested test sequence.

## 8.1 Ctrl+C must defer process termination until cleanup runs

Do not rely on a plain `finally` block alone for Ctrl+C.

Register `Console.CancelKeyPress` before a command can send non-zero rumble.

The handler must:

```text
Console.CancelKeyPress
-> set e.Cancel = true
-> request cancellation on the command's CancellationTokenSource
-> return promptly
```

The command loop must observe cancellation, leave the active sequence, and enter its single cleanup/finally path.

That cleanup path must attempt exactly one final best-effort:

```text
XInputSetState(selectedSlot, 0, 0)
```

before the process returns.

After cleanup completes, exit normally with a cancellation/non-success process code. Do not call `Environment.Exit`, `FailFast`, or another immediate termination path from the Ctrl+C handler.

The handler itself must not perform the XInput write. Keep the physical STOP in the normal serialized command cleanup path so Ctrl+C cannot race a still-running pulse write.

Unregister the handler when the command finishes.

## 8.2 XInputSetState return-code policy

`XInputSetState` reports ordinary failures by Win32 return code, not only by exceptions.

Treat:

```text
ERROR_SUCCESS (0)
```

as the only successful result.

A non-success return, including:

```text
ERROR_DEVICE_NOT_CONNECTED (1167)
```

means the requested test sequence did not complete successfully.

Required behavior:

```text
send requested state
-> record the exact return code
-> if result != ERROR_SUCCESS:
       stop the current iteration/sequence immediately
       enter the single cleanup path
       attempt final 0/0 once
       record the cleanup STOP return code
       exit non-zero
```

Do not continue issuing later test pulses after a failed XInput call.

A failed cleanup STOP must also be logged explicitly. It must not cause a retry loop.

The `deadman` command intentionally waits without sending a STOP during its observation window, but its cleanup path must still send a final STOP before process exit.

Do not add an option that intentionally exits while leaving a non-zero motor state.

---

# 9. Probe log

Create a dedicated log per run.

Preferred path:

```text
<canonical Addon log directory>/RumbleProbe/rumble-probe-YYYYMMDD-HHmmss.fff-P<pid>.log
```

Reuse the existing canonical log-root resolution only if it can be referenced without pulling production runtime/UI ownership into the tool.

If that creates an undesirable project dependency, use a small local equivalent resolving:

```text
%LOCALAPPDATA%\SteamInputAddonforClaw-Data\logs\RumbleProbe
```

Do not reference or instantiate `AppLog` from the console tool.

The probe log is part of the measurement contract, not optional decoration.

Before the first non-zero `XInputSetState` call, the tool must:

```text
resolve/create the RumbleProbe log directory
-> create the per-run log file
-> write the session header
-> flush the header successfully
-> only then allow a non-zero vibration command
```

If directory creation, file creation, header write, or flush fails:

- print a clear error to stderr;
- do not issue any non-zero vibration command;
- exit non-zero.

Do not run an unlogged reproduction session.

Each command must log:

```text
wall-clock timestamp with milliseconds
Stopwatch elapsed ticks/milliseconds
ProbeSeq
command
slot
left8/right8
left16/right16
XInputSetState return code
phase = NonZero / Stop / CleanupStop / IdleBegin / IdleEnd
```

Example:

```text
2026-09-24T20:20:31.123+09:00 ElapsedMs=1532 ProbeSeq=41 Phase=NonZero Slot=0 Left8=3 Right8=6 Left16=771 Right16=1542 Result=0
2026-09-24T20:20:31.373+09:00 ElapsedMs=1782 ProbeSeq=42 Phase=Stop Slot=0 Left8=0 Right8=0 Left16=0 Right16=0 Result=0
```

Flush each line. The test rate is intentionally low enough that a simple StreamWriter is sufficient.

For every `XInputSetState` call, including the final cleanup STOP, log the exact Win32 return code before applying the success/failure policy from section 8.2.

---

# 10. Addon correlation logging

The current Addon production Xbox360 rumble path uses the same canonical typed callback that CTW now consumes.

Add **Debug-only diagnostic correlation** to the existing production path; do not add a new feedback owner.

## 10.1 Xbox360 callback

In `Xbox360RumbleFeedbackBridge.OnRumble`, when Debug logging is enabled, log every received callback during the probe.

Use a bridge-local monotonic diagnostic sequence counter.

Suggested event:

```text
Event=ProductionXbox360RumbleRx
RumbleSeq=<n>
Left8=<value>
Right8=<value>
IsStop=<true|false>
```

Use `AppLog.IsEnabled(AppLogLevel.Debug)` before any extra diagnostic-only work.

`AppLog` is already backed by a bounded asynchronous writer, so do not create another logging thread/queue.

Do not change callback ordering.

## 10.2 Physical write result

For the same callback sequence, after `_sink.SetRumble(rumble)`, log:

```text
Event=ProductionXbox360RumblePhysicalWrite
RumbleSeq=<n>
Large8=<effective physical byte>
Small8=<effective physical byte>
Status=<Succeeded|DuplicateSuppressed|Unavailable|Failed|...>
Reason=<existing reason>
```

Use the actual sink semantics/current status names.

Do not fabricate success.

Do not change `MsiClawRumbleSink` state logic merely for logging.

## 10.3 Safety stop

When the existing 5-second Xbox360 safety stop fires, emit one Debug event:

```text
Event=ProductionXbox360RumbleSafetyStop
SourceRumbleSeq=<sequence that armed it>
```

Log the physical write result if available without changing the safety-stop behavior.

This lets a `deadman` probe run distinguish:

```text
host STOP callback
vs.
Addon safety-generated physical STOP
```

Do not shorten or lengthen the current production timeout in this PR.

---

# 11. Correlation with VIIPER diagnostic build

This work order is designed to be used together with the separate VIIPER diagnostic PR.

Expected three logs:

```text
RumbleProbe log
libVIIPER.log
SteamInputAddonforClaw Runtime log
```

They are correlated by:

1. wall-clock time;
2. strict ordering;
3. distinctive motor pairs;
4. per-layer monotonic sequence numbers.

Do not attempt to propagate `ProbeSeq` through XInput: XInput exposes only vibration values, not an application correlation ID.

The fixed `sequence` command exists specifically to make cross-log alignment unambiguous without inventing a transport extension.

---

# 12. Recommended primary reproduction

Start with:

```text
pulse
left8=3
right8=6
on-ms=250
off-ms=250
iterations=500
```

Then repeat with:

```text
left8=1 right8=1
left8=16 right8=0
left8=99 right8=127
left8=255 right8=255
```

Do not immediately increase to extremely high-frequency timing. The reported product issue occurs in normal gameplay and does not require pathological scheduler stress.

If all normal-rate patterns are lossless, then one later targeted timing experiment may reduce on/off intervals. Keep that separate from the first evidence run.

---

# 13. Evidence interpretation

## Probe logs successful 0/0, VIIPER raw trace has no 0/0

The command was accepted by XInput but the expected host OUT did not reach VIIPER.

This shifts investigation upstream of `Xbox360.HandleTransfer`.

## VIIPER raw trace has 0/0 and parsed/dispatch logs it, Addon Rx lacks 0/0

Investigate typed C/managed callback bridge.

## Addon Rx receives 0/0 and physical write succeeds

VIIPER/managed callback are not the missing-STOP boundary for that iteration.

If hardware still vibrates, move to physical/firmware investigation.

## Probe omits STOP in deadman mode

Expected:

```text
no host 0/0 callback
~5 seconds later
Addon ProductionXbox360RumbleSafetyStop
physical 0/0
```

This is a validation of the Addon's fail-safe, not evidence about VIIPER root cause.

---

# 14. Tests

Add narrow tests for probe logic that do not require physical hardware.

Abstract only the one XInput call boundary enough to test:

- 8-bit -> 16-bit expansion;
- pulse command ordering;
- sequence command ordering;
- exactly one STOP after each non-zero pulse;
- final cleanup STOP on completion;
- final cleanup STOP after a simulated exception;
- a non-success `XInputSetState` return code stops the active sequence immediately, attempts one final cleanup STOP, and produces a non-zero command result;
- `ERROR_DEVICE_NOT_CONNECTED` follows the same non-success path;
- a failed cleanup STOP is logged/classified and does not create a retry loop;
- Ctrl+C cancellation leaves the command loop through the normal cleanup path and produces one final cleanup STOP;
- deadman emits no STOP during its observation window but always emits final cleanup STOP.

Add slot-selection tests with a fake XInput boundary:

```text
0 connected slots + no --slot
-> fail clearly

exactly 1 connected slot + no --slot
-> auto-select that slot

2+ connected slots + no --slot
-> fail clearly and require --slot

explicit --slot outside 0..3
-> reject before any XInput mutation

explicit valid slot
-> use exactly that slot
```

Also test the log preflight boundary:

```text
log open/header/flush failure
-> no non-zero XInputSetState call is made
-> command fails non-zero
```

Do not add a general controller abstraction framework.

For Addon correlation logging, extend existing `Xbox360RumbleFeedbackBridgeTests` only as needed to prove logging instrumentation does not change:

- callback value translation;
- write count;
- explicit STOP behavior;
- safety-stop behavior.

Do not write tests that depend on wall-clock file timestamps.

---

# 15. Packaging / product boundaries

RumbleProbe must not be included in:

- Velopack package;
- installed app payload;
- startup tasks;
- user-facing Developer menu;
- normal UI navigation;
- release ZIP/MSIX artifacts unless a later explicit decision says otherwise.

It is a source-tree diagnostic utility.

Do not modify Center M authority, HidHide, PID1901/PID1902 ownership, Steam/BPM presentation policy, Overlay, QAM, M1/M2, gyro, or power lifecycle.

---

# 16. Validation

## 16.1 Runtime logging prerequisite for hardware measurement

The Addon Runtime correlation events in section 10 are Debug-only.

Before a hardware reproduction session:

```text
set the Addon log level to Debug
restart/reload the Runtime if required for that setting to take effect
verify a current SteamInputAddonforClaw Runtime log is being written
verify libVIIPER.log is being written by the diagnostic VIIPER build
verify RumbleProbe can create and flush its own session log
only then start the non-zero probe sequence
```

The normal `AppLog` default is `Off`; therefore a run performed without enabling Debug is not sufficient evidence for cross-layer correlation.

Record the effective Addon log level in the validation notes.

## 16.2 Build/test validation

Before completing the PR:

```text
dotnet build
dotnet test
dotnet build tools/RumbleProbe/RumbleProbe.csproj -c Release
```

Run the probe help/list path on Windows if possible.

Hardware validation, when available, should record:

- Addon build commit;
- VIIPER diagnostic commit;
- effective Addon Runtime log level (`Debug`);
- selected XInput slot;
- exact probe command;
- RumbleProbe session log path;
- `libVIIPER.log` path;
- exact SteamInputAddonforClaw Runtime log path;
- whether any `XInputSetState` call returned non-success;
- whether physical stuck vibration was observed.

Do not claim the missing-STOP root cause until those logs identify the exact disappearing boundary.

---

# 17. Delivery

Open one focused SteamInputAddonforClaw PR containing:

1. `tools/RumbleProbe`;
2. minimal Debug-only Xbox360 callback / physical-write / safety-stop correlation logs;
3. tests;
4. no UI or frontend vibration-test restoration.

The PR description must explicitly state that the console probe exists to reproduce and locate the missing-terminal-`0/0` boundary deterministically, not to change production rumble policy.
