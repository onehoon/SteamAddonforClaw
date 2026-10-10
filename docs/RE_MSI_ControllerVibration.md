# RE: MSI Claw Controller Vibration

Status: runtime game rumble and controller vibration-strength settings are separate
mechanisms. A contiguous profile pair write has been physically validated on the
MSI Claw 8 AI+ EX (CG3EM / MS-1T91), firmware `0x0419`, and the `0/100` motor
asymmetry was physically observed on MSI Claw 8 AI+ A2VM (MS-1T52), PID1902
firmware `0x0230`. Production applies the Addon's persisted desired pair to the
owned PID1902 controller state; it does not read firmware values as user settings
and does not issue `SyncToROM`.

## 2026-10-10 production support and recovery update

Production pair writes are enabled on CG3EM and A2VM 8 from prior hardware evidence, and are now enabled on A2VM 7 by the explicitly scoped shared-A2VM protocol policy. A2VM 7 has **not** been physically tested; this is family-level rollout, not device-level validation. On A2VM 8, the user physically confirmed that the manual PID1902 → PID1901 → PID1902 recovery restores previously silent physical rumble. A2VM 7/8 also receive one optional mode initialization per actual Windows boot when their first strong state is already PID1902. These software transitions do not themselves verify motor output or firmware register state.

Keep the three paths distinct: runtime game rumble uses the controller's output report path (including the investigated report `0x05`); saved left/right strength uses one profile command `0x3C/0x21`, index 1, offset `0x22`, length 2; Joystick LED uses separate `0x3C/0x21` profile-frame writes at the exact firmware-mapped RGB base. None is evidence for either of the other mechanisms. See [Joystick LED RE](RE_MSI_Joystick_LED.md).

## Two different meanings of “vibration”

### Runtime game rumble

MSI Center M's `UC_ControlMode.UC_Vibration` uses SharpDX XInput:

```csharp
controller.SetVibration(new Vibration {
    LeftMotorSpeed = ...,
    RightMotorSpeed = ...,
});
```

The UI stores independent `LM` and `RM` values in the `MP` / `MotorsProfile`
configuration. This is a runtime XInput request, not proof of a direct MSI
vendor-HID rumble packet. Static analysis of the inspected MSI artifacts did not
find a native report-`0x05` writer translating XInput to the physical motor. That
final translation remains a Windows-driver / firmware / hardware question.

The Addon's physical Left / Right Test and runtime game-rumble path are distinct
from the persistent desired strength pair described below.

Runtime physical rumble is separately resolved against one exact, strongly
identity-correlated PID1902 Game Pad/Joystick collection with readable report
capabilities and a usable output report. Missing identity, an unverified physical
root, an unusable collection, or a non-gamepad-only collection (including the
observed A2VM 2.30 `0001/0040` usage) is diagnosed as unavailable; no rumble packet
is issued. This does not change the game-rumble request or stop-safety behavior.

### Serialized motor profile

Static analysis of the supplied MSI artifacts found these profile-relative fields:

| Motor | Profile field | Serialized profile offset |
| --- | --- | ---: |
| Left | `MotorModule.LeftMotorValue` / `LM` | `0x22` |
| Right | `MotorModule.RightMotorValue` / `RM` | `0x23` |

Center M loads these UI values from its local `ControlProfile` / `profile.rec`
state, not from authoritative device readback. The inspected profile-save path
uses index `1`; when only LM/RM change it can write the contiguous two-byte range
starting at `0x22`. The inspected whole-profile read requests index `0`, and
Center M queues `SyncToROM` (`0x22`) after its profile write.

The inspected binaries were `UC_ControlMode.dll 1.0.2608.1201` and
`API_ControlMode.dll 1.0.2606.2401`; their versions do not match. These are
static findings within the individually inspected files and do not establish
cross-assembly runtime compatibility. A serialization offset by itself is not a
firmware address guarantee. The CG3EM and A2VM 8 entries below have direct hardware
evidence; A2VM 7 is additionally enabled only by the explicit shared-family policy
and remains untested on that model.

## Hardware evidence — CG3EM / MS-1T91 / firmware `0x0419`

The 2026-10-04 Developer probe and matching Addon hardware test established:

1. One PID1902 vendor-HID profile write using index `1`, offset `0x22`, length
   `2`, payload `00 64` (`Left=0`, `Right=100`) was accepted with no
   `SyncToROM` command.
2. Subsequent pair-read diagnostics for request indexes `0` and `1` both
   returned candidate values `0/100`.
3. The existing physical Left Test and Right Test each produced the expected
   changed motor behavior.
4. A single explicit restore pair write of `32 32` (`50/50`), again without
   `SyncToROM`, was accepted; subsequent pair-read diagnostics returned `50/50`.

This proves a live physical effect and the tested pair-write/readback behavior on
that CG3EM firmware. It does **not** prove durable controller-side persistence
across power loss or that Center M's local UI values are firmware readback. In the
field test, Center M returned to its local `50/50` UI values after relaunch; that
observation is not a universal hardware default. The pair `50/50` is the Addon's
chosen first-install default, not a guessed value read from the device.

## Hardware evidence — A2VM 8 / MS-1T52 / firmware `0x0230`

On 2026-10-09, the user applied the existing PID1902 pair write with Left `0` and
Right `100` through the Developer action. The 64-byte vendor-HID report was
accepted, then the separate physical Left and Right motor tests showed the left
motor silent and the right motor vibrating. This confirms the live asymmetric
motor effect for the tested A2VM 8 firmware; the production setting reuses this
same contiguous pair-write frame and the persisted Controller settings.

The explicit restore command `50/50` was accepted by the HID transport. The user
did not establish a firmware-original value or controller-side persistence across
power loss. `50/50` remains the Addon's chosen saved default.

### Product interpretation

```text
settings.json ControllerVibration = durable Addon desired state
PID1902 profile pair              = current applied controller state
production apply                  = one contiguous pair write
SyncToROM                         = never issued by production
```

The Addon owns durable persistence. It reapplies the saved pair after initial
healthy PID1902 ownership, a successful real physical-input recovery, and Sleep /
Hibernate Resume after the existing bounded 500 ms HID settle. A user edit is
saved before its best-effort immediate apply. Apply failure does not roll back
settings or alter controller ownership.

Normal Controller-page capture returns the saved pair and performs zero profile
HID reads. The physical pair is not reapplied for Xbox360 ↔ SteamDeck changes,
Steam / Big Picture transitions, Overlay / QAM visibility, or ordinary status
refreshes.

## Current production policy

| Addon model | Board | Production pair write |
| --- | --- | --- |
| `msi.claw.cg3em` | MS-1T91 | Enabled; validated on firmware `0x0419` |
| `msi.claw.a2vm.7` | MS-1T42 | Enabled by shared-A2VM protocol policy; not individually hardware-tested |
| `msi.claw.a2vm.8` | MS-1T52 | Enabled; `0/100` motor asymmetry observed on firmware `0x0230` |

The CG3EM and A2VM 8 production policies are model-only, matching the existing
family policy; the observed `0x0230` firmware is documented evidence, not a new
vibration firmware gate. A2VM 7 now uses the same contiguous profile pair write
based on shared A2VM evidence, without a claim of direct A2VM 7 validation. Runtime
game rumble remains a separate path and is not evidence for profile-strength support.

Production writes require Center M startup authority exactly `Disabled`, a
healthy Addon-owned live PID1902 session, a Strong matching physical identity,
and exactly one PID1902 control HID with VID `0x0DB0`, usage page `0xFFF0`, and
usage `0x0040`.

The only production profile frame is a 64-byte report with this populated prefix:

```text
0F 00 00 3C 21 01 00 22 02 <Left> <Right>
```

Examples:

```text
50/50  →  0F-00-00-3C-21-01-00-22-02-32-32
0/100  →  0F-00-00-3C-21-01-00-22-02-00-64
100/0  →  0F-00-00-3C-21-01-00-22-02-64-00
```

One apply attempt sends exactly one pair write. Production does not send separate
one-byte writes to `0x22` and `0x23`, read values back, or issue command `0x22` /
`SyncToROM`. A failed write is feature-local; it does not release PID1902,
change HidHide, detach VIIPER, or reset the persisted pair.

The persisted settings are global Left / Right values in `settings.json`, each
validated to `0..100`, with default `50/50`. They are not per-game values,
software rumble multipliers, or EEPROM-persistence controls.

## Developer diagnostics

Developer → Vibration Test retains a bounded profile probe for CG3EM and an
explicit A2VM 8 strength write/restore diagnostic:

| Action | Pair written | Required follow-up |
| --- | --- | --- |
| Apply Left `0` / Right `100` | index `1`, offset `0x22`, length `2`, bytes `00 64` | Press physical Left Test once, then Right Test once |
| Restore `50/50` | index `1`, offset `0x22`, length `2`, bytes `32 32` | Explicit user action; no automatic retry |

These are explicit Developer profile-write actions, use the same profile transaction gate as
production writes, and do not change production settings. They require Center M
authority exactly `Disabled`, a healthy live Addon-owned physical PID1902 session,
and one strongly identified PID1902 control HID. A2VM eligibility for this
diagnostic remains restricted to `msi.claw.a2vm.8`; it does not include A2VM 7. The user observed the expected
left-silent/right-vibrating response for the A2VM 8 `0/100` motor tests. An
individual probe result still reports transport acceptance, not a fresh physical
observation. `50/50` is the Addon default, not readback of the firmware's original
value, and its accepted write does not prove device-side durability. A failed
restore must be surfaced and retried only by another explicit button press.
Neither model uses `SyncToROM` in this probe.

The existing physical Left / Right Test remains independent of whether
production strength setting is available. The separate **Restore Vibration**
manual recovery is now on Controller for all three supported models; it temporarily
retires the virtual presentation, verifies both native transitions, restores owned
input/settings, and selects a fresh Steam/BPM presentation. It is not a Developer
probe or stock authority release. The Developer profile-write probe remains a
separate explicit diagnostic and is not used by normal production apply.

The former index-0 / index-1 pair-read capture was a one-off Developer
investigation. It is no longer run implicitly by normal Controller-page capture,
and its obsolete runtime read path has been removed. Its recorded 0/100 and
50/50 responses above are retained as historical evidence, not live settings.

## Still unproven

- Direct physical pair-write and motor-effect validation on A2VM 7 / MS-1T42 remains unperformed; enabled support is based on shared-family protocol evidence.
- A2VM 7 boot re-arm behavior and A2VM 7 physical motor effect have not been tested.
- Physical effect of the A2VM 8 `50/50` restore was not separately established;
  its transport acceptance does not prove device-side durability.
- Controller-side durable profile persistence across power loss; production
  relies on Addon `settings.json` and lifecycle reapplication instead.
- Production writes through PID1901 or equivalence between PID1901 / PID1902
  profile banks.
- Behavior on future or otherwise untested controller firmware versions.
- The final driver / firmware translation from runtime XInput rumble requests to
  physical motor output.

## Historical command and Center M findings

The old RE captured this illustrative one-byte-plus-sync sequence for Center M:

```text
0F 00 00 3C 21 01 00 22 01 <leftValue>
0F 00 00 3C 22

0F 00 00 3C 21 01 00 23 01 <rightValue>
0F 00 00 3C 22
```

That historical Center M approach is not the Addon's production contract. The
production implementation uses the tested contiguous pair frame and never
issues `SyncToROM`. Earlier one-byte response parsing, index-0 / index-1 read
diagnostics, and the previous all-model fail-closed policy should be understood
as superseded implementation history, not current product behavior.

## Evidence sources

- `RE_MSI_ButtonRemap.md` contains controller profile offsets and HID profile command format.
- `MSI_COMPLETE_RESEARCH_RESULT.md` records the MSI binary audit and unresolved XInput-to-physical path.
- `clawtweaks-hid-protocol.md` records the vendor HID channel and historical sync command.
