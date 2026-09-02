# RE: MSI Claw PID 1902 M1/M2 Back-Button Mapping

Status: protocol and address are established from MSI Center M logs and on-device
read/write observations. This is a research note, not a production mapping change.

## Scope and transport

`PID 1902` is the DirectInput presentation of the controller. The vendor command
interface is HID, not MSI ACPI-WMI:

| Mode | VID/PID | Vendor usage | Purpose |
| --- | --- | --- | --- |
| XInput | `0DB0:1901` | `0xFFA0 / 0x0001` | controller commands |
| DirectInput | `0DB0:1902` | `0xFFF0 / 0x0040` | controller commands |

The 64-byte output frame starts with `0F 00 00 3C`. The relevant commands are:

- `0x21 0x01`: WriteProfile, write controller profile/EEPROM data.
- `0x22`: SyncToROM, persist the preceding profile write.
- `0x24`: SwitchMode (`01` XInput, `02` DirectInput, `04` Desktop).

There is no evidence that M1/M2 remapping uses `MSI_ACPI.Set_Data`; it uses the
controller's vendor-HID profile path.

## M1/M2 slot layout

The corrected on-device result is a four-byte structure at the slot start, followed
by zero-padded action code bytes:

| Button | Slot start | Older code-byte offset | Default code |
| --- | ---: | ---: | ---: |
| M1, right rear | `0x00BA` | `0x00BD` (`+3`) | `0x11` |
| M2, left rear | `0x0163` | `0x0166` (`+3`) | `0x12` |

The required marker is:

```text
01 04 0C <action-code-0> [<action-code-1> ...]
```

`04 0C` is not optional. A write that only changes the old `+3` code-byte
location can store bytes without producing a firmware action because the remap
marker remains unset. For a keyboard chord, codes are appended after `0C`, with
modifiers first and the ordinary key last. Trailing code bytes must be cleared to
avoid ghost keys.

Examples observed:

```text
M1 default:       01 04 0C 11
M2 default:       01 04 0C 12
M1 Win+D:         01 04 0C 75 5E
M2 Alt+Tab:       01 04 0C 76 4D
```

The complete output operation is therefore conceptually:

```text
0F 00 00 3C 21 01 00 BA <length> 01 04 0C <codes...> <zero padding>
0F 00 00 3C 22

0F 00 00 3C 21 01 01 63 <length> 01 04 0C <codes...> <zero padding>
0F 00 00 3C 22
```

The exact write length should cover the structure and the code area being cleared;
do not send a partial marker-less code-byte poke. Read the existing profile first,
preserve unknown bytes, and verify by reading the profile back where the active
firmware/transport permits it.

## Relationship to the Addon's virtual 360 output

For the Addon's virtual Steam Deck/X360 presentation, the existing logical mapping
is:

| Physical button | Virtual output |
| --- | --- |
| M1, right rear | R4 |
| M2, left rear | L4 |

That mapping is a software output mapping and is separate from changing the
controller's persistent native firmware profile. A native M1/M2 remap should not be
mistaken for proof that MSI Center M exposes independent virtual-controller
semantics; it only proves the controller accepts independent profile slots.

## Safety and unresolved points

- Entering PID 1902 changes the controller presentation; acquire the vendor HID
  interface only after the mode is confirmed.
- Never use the old guessed single-byte-only recipe as the write format.
- `0x11`/`0x12` are the default paddle action codes; keyboard codes use a separate
  code table and must be validated against the desired action.
- A2VM and EX address compatibility is reported as identical for the button profile
  in the RE material, but every new firmware revision should be read before writing.
- Do not couple this operation to routing, HidHide, VIIPER, or OEM1 state.

## Evidence

- [RE_MSI_ButtonRemap.md](https://drive.google.com/file/d/1Xgf9lE2LaaIwAoPLekPCrNvKXtIzYpMN/view)
- [HHC_msiapcfg_analysis.txt](https://drive.google.com/file/d/1vBp99KyQbbMC16ka2hoJJDNMVp0g-v8N/view)
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
- Repository controller mapping: `README.md`, `Controller` mapping section.
