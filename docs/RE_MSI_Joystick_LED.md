# RE: MSI Claw Joystick LED Control

Status: this document retains the historical protocol/reverse-engineering notes and
records the current production references. HHC's current exact firmware map includes EX
`0x0411` and `0x0414`; CG3EM/MS-1T91 field firmware `0x0419` is now an explicit
SteamAddon exact entry resolving to the same `0x024A` RGB base. SteamAddon's Basic
Static implementation still uses exact table matches and fails closed for every other
unknown version. This does not claim that the `0x0419` SteamAddon path has passed
physical hardware acceptance.

## Transport and packet

LED control uses the controller vendor HID channel, not WMI:

```text
PID 1901: usage page 0xFFA0 / usage 0x0001
PID 1902: usage page 0xFFF0 / usage 0x0040
Output report: 64 bytes, preamble 0F 00 00 3C
Command:       21 01 (WriteProfile)
Historical sync command: 22 (SyncToROM; not issued by the current static setter)
```

The firmware-specific RGB profile base is selected by the exact firmware table below.
The 32-byte block is:

| Block byte | Meaning |
| ---: | --- |
| `[9]` | profile/index, observed `0x00` |
| `[10]` | effect/mode field in the newer effect RE; do not confuse it with `[11]` |
| `[11]` | static write constant `0x09` in the original MSI/static path |
| `[12]` | speed field; static path uses `0x03` |
| `[13]` | brightness, `0..100` (`0x64` = 100) |
| `[14..40]` | nine RGB triplets |

The nine zones map as follows:

```text
zones 0..3: right joystick ring LEDs
zones 4..7: left joystick ring LEDs
zone  8:    controller buttons
```

Thus a solid color repeats one RGB triplet in all nine zones, while a joystick-only
color can change zones `0..3` or `4..7` independently. This is a per-zone RGB
profile, not an EC/WMI command.

## Readback

The working read command is `ReadProfile (0x04)`, not the older guessed
`ReadRGBStatus (0x0D)`:

```text
request:  0F 00 00 3C 04 01 <addrHi> <addrLo> 20
response: 10 00 00 3C 05 01 <addrHi> <addrLo> 20 00 01
          <effect> <speed> <brightness> <9 x RGB>
```

The response acknowledgement is `0x05` at byte `[4]`. This describes the historical
readback RE. The current Basic Static setter uses the exact firmware table and fills
all four frame slots instead of reading profile state on each edit.

## Current firmware address evidence

Handheld Companion's current MSI Claw source maps the following USB firmware versions
to RGB profile addresses. In particular, its MS-1T91 / EX entries are present in the
current table, superseding the older note that `0x0411` was absent.

| Firmware version | RGB address |
| ---: | ---: |
| `0x0163` | `0x01FA` |
| `0x0166` | `0x024A` |
| `0x0167` | `0x024A` |
| `0x0211` | `0x01FA` |
| `0x0217` | `0x024A` |
| `0x0219` | `0x024A` |
| `0x0308` | `0x024A` |
| EX `0x0411` | `0x024A` |
| EX `0x0414` | `0x024A` |
| EX `0x0419` | `0x024A` |

Source: [Handheld Companion `ClawA1M.cs`](https://github.com/Valkirie/HandheldCompanion/blob/master/HandheldCompanion/Devices/MSI/ClawA1M.cs).
HHC's exact MS-1T91 table currently ends at `0x0414`. The tested CG3EM/MS-1T91
reports controller firmware `0x0419`; CTW's deployed production resolver selects the
nearest EX entry (`0x0414`) and therefore `0x024A` for that firmware. SteamAddon does
not adopt CTW's general nearest-match policy: `0x0419` is promoted to one explicit
known entry, while every other unknown version still fails closed with zero profile
writes. Physical SteamAddon LED acceptance for `0x0419` remains pending.

The A2VM 2.30 observation (`VersionNumber=0x0230` / `REV_0230`) does not have an
exact verified RGB address in the current table or the inspected RE. The historical
A2VM `0x0229` and `0x0308` observations both point to `0x024A`, but they do not prove
that `0x0230` uses that address. Keep `0x0230` unsupported for production writes.
The explicit Developer-only A2VM 8 read probe may issue one bounded `ReadProfile`
query to candidate `0x024A`, index 1, block length `0x20`, only on the current
healthy Addon-owned PID1902 `0xFFF0/0x0040` control HID with exact firmware
`0x0230` and Center M exactly Disabled. It structurally validates the 64-byte
response and logs only the bounded profile block. This is diagnostic evidence,
not proof that the address is safe to write. No LED `WriteProfile` (`0x21`) or
`SyncToROM` command is issued by this probe; the production resolver continues to
reject `0x0230`. Physical A2VM response evidence remains pending user testing.

## Historical device-specific RE notes

| Controller firmware | Historical evidence | RGB address |
| --- | --- | --- |
| A2VM `0x229` | on-device read/write RE; nearest firmware table entry | `02 4A` |
| A2VM `0x308` | device control-surface RE | `02 4A` |
| EX `0x0411` | current HHC table | `02 4A` |
| EX `0x0414` | current HHC table | `02 4A` |
| EX `0x0419` | CG3EM/MS-1T91 field firmware; CTW nearest-match resolves to EX `0x0414` | `02 4A` |

The table is evidence for address selection, not a SteamAddon hardware acceptance
result. `0x0419` is an explicit exact SteamAddon entry but still awaits physical LED
smoke-test confirmation. All other unknown versions remain unsupported; do not probe
arbitrary EEPROM addresses or generalize CTW's nearest-match behavior.

## CTW production Static reference

CTW `release/v0.3.98.0`'s shipped static setter uses Static mode `0x01`, speed `0x03`,
and one RGB triplet repeated across all nine zones. It writes the base header block,
then fills the remaining three frame slots with identical raw 27-byte RGB payloads at
`base + 32`, `base + 59`, and `base + 86`. It does not issue SyncToROM in its normal
static setter. Source: [CTW `MsiClawLedController.cs`](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/Devices/MSIClaw/MsiClawLedController.cs).

SteamAddon implements only this Basic Static subset: On/Off, global brightness, and one
uniform color. Battery/SoC, animation effects, speed/direction, per-zone editing, and
other effect research below remain historical/reference material and are out of scope.

The SteamAddon base packet contains the Static header and first frame. The three
follow-on packets contain raw frames only (no effect header).

The firmware version must exactly match the table above. Effective hardware brightness
is the saved brightness while enabled and zero while disabled; the saved brightness
and color are retained. SteamAddon sends no `0x22` SyncToROM command.

## Effects and stale-frame hazard

The effect RE reports these observations:

- Static: mode `0x01`.
- Breathing: mode `0x06` was observed in one write-side mapping, but reliable
  breathing was later reproduced as a multi-frame `0x04` sequence with brightness
  ramps.
- Wave and color-cycle experiments use contiguous 27-byte frame slots after the
  base header; stale frames must be overwritten or cleared.

Do not implement an effect writer from these notes without a separate scoped work order
and validation for that exact firmware.

## Evidence

- [todo-read-led-firmware-state.md](https://drive.google.com/file/d/1SHAdriD_MF58d1Uu71Z-IVu1jppCPvSx/view)
- [todo-led-effects-msi-claw.md](https://drive.google.com/file/d/1477Q7JaAYj3lDF2iMlxyJYDpmqZzk9Lx/view)
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
- [device-a2vm-control-surfaces.md](https://drive.google.com/file/d/1ZHrmSEZbDq5s41RyhbmxyxCspfOCKhvy/view)
- [device-claw8ex-panther-lake.md](https://drive.google.com/file/d/1qWgcszUg4BFtllHrs3dP826sxAr3NZaF/view)
