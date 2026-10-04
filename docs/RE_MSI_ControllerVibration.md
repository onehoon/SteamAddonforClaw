# RE: MSI Claw Controller Vibration

Status: runtime game rumble and controller vibration-strength settings are separate
mechanisms. A contiguous profile pair write has been physically validated on the
MSI Claw 8 AI+ EX (CG3EM / MS-1T91), firmware `0x0419`. Production applies the
Addon's persisted desired pair to the owned PID1902 controller state; it does not
read firmware values as user settings and does not issue `SyncToROM`.

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
firmware address guarantee. The model-specific hardware evidence below is what
authorizes the current CG3EM production pair write.

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
| `msi.claw.a2vm.7` | MS-1T42 | Disabled pending physical validation |
| `msi.claw.a2vm.8` | MS-1T52 | Disabled pending physical validation |

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

Developer → Vibration Test retains the bounded CG3EM-only profile probe for
future hardware investigation:

| Action | Pair written | Required follow-up |
| --- | --- | --- |
| Apply Left `0` / Right `100` | index `1`, offset `0x22`, length `2`, bytes `00 64` | Press physical Left Test once, then Right Test once |
| Restore `50/50` | index `1`, offset `0x22`, length `2`, bytes `32 32` | Explicit user action; no automatic retry |

These are explicit Developer actions, use the same profile transaction gate as
production writes, and do not change production settings. They require Center M
authority exactly `Disabled` and one strongly identified PID1902 control HID.
Transport success means only that the HID transport accepted the frame. A failed
restore must be surfaced and retried only by another explicit button press.

The existing physical Left / Right Test remains independent of whether
production strength setting is available. The CG3EM write probe remains
CG3EM-only in this PR; A2VM probe enablement and physical acceptance are a later,
evidence-backed change.

The former index-0 / index-1 pair-read capture was a one-off Developer
investigation. It is no longer run implicitly by normal Controller-page capture,
and its obsolete runtime read path has been removed. Its recorded 0/100 and
50/50 responses above are retained as historical evidence, not live settings.

## Still unproven

- Production pair-write behavior on A2VM / MS-1T42 and MS-1T52.
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
