# RE: MSI Claw Joystick LED Control

Status: the static/per-zone RGB write and readback protocol is established for the
A2VM path. EX firmware `0x0411` is not yet directly validated; the current address
selection is a nearest-table inference and must remain diagnostic until tested.

## Transport and packet

LED control uses the controller vendor HID channel, not WMI:

```text
PID 1901: usage page 0xFFA0 / usage 0x0001
PID 1902: usage page 0xFFF0 / usage 0x0040
Output report: 64 bytes, preamble 0F 00 00 3C
Command:       21 01 (WriteProfile)
Sync:          22 (SyncToROM)
```

The firmware-specific RGB profile base observed for A2VM is `0x024A` (`02 4A`).
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

The response acknowledgement is `0x05` at byte `[4]`. Readback is important because
the firmware address depends on controller firmware and because MSI's animated
effect experiments showed that stale follow-on frames can remain in the profile.

## Firmware address selection

| Controller firmware | Evidence | RGB address |
| --- | --- | --- |
| A2VM `0x229` | on-device read/write RE; nearest firmware table entry | `02 4A` |
| A2VM `0x308` | device control-surface RE | `02 4A` |
| EX `0x0411` | address not present in current table | nearest-match inference to `02 4A` |

For EX, do not silently claim support from the nearest-match rule. First read the
candidate block, validate the response shape and expected RGB state, then perform
only a reversible static-color write and readback. If the address is wrong, stop;
do not probe arbitrary EEPROM addresses because an invalid profile write can wedge
the controller until reboot.

## Static write recipe

For a static color, construct the known 32-byte block from a valid current read,
change only the intended mode/brightness/zone fields, then write it using:

```text
0F 00 00 3C 21 01 <rgbAddrHi> <rgbAddrLo> 20
<index> <mode> 09 <speed> <brightness> <9 x RGB> ...
0F 00 00 3C 22
```

Read back with `0x04` and compare the owned fields. Preserve unknown bytes and do
not use the historical guessed mode values blindly. Static mode `0x01` is the
current effect-RE's write-side static code; the original static packet also has
the `0x09` constant at the following field. The field meanings differ between
read-side labels and write-side implementation notes, so a production writer must
follow one verified packet builder and its round-trip test rather than mix labels
from different RE revisions.

## Effects and stale-frame hazard

The effect RE reports these observations:

- Static: mode `0x01`.
- Breathing: mode `0x06` was observed in one write-side mapping, but reliable
  breathing was later reproduced as a multi-frame `0x04` sequence with brightness
  ramps.
- Wave and color-cycle experiments use contiguous 27-byte frame slots after the
  base header; stale frames must be overwritten or cleared.

For this repository's next implementation, static/per-zone RGB is the safest
documented surface. Do not implement an effect writer from these notes without a
fresh round-trip test for that exact firmware.

## Evidence

- [todo-read-led-firmware-state.md](https://drive.google.com/file/d/1SHAdriD_MF58d1Uu71Z-IVu1jppCPvSx/view)
- [todo-led-effects-msi-claw.md](https://drive.google.com/file/d/1477Q7JaAYj3lDF2iMlxyJYDpmqZzk9Lx/view)
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
- [device-a2vm-control-surfaces.md](https://drive.google.com/file/d/1ZHrmSEZbDq5s41RyhbmxyxCspfOCKhvy/view)
- [device-claw8ex-panther-lake.md](https://drive.google.com/file/d/1qWgcszUg4BFtllHrs3dP826sxAr3NZaF/view)
