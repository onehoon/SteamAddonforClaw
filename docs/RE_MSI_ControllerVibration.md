# RE: MSI Claw Controller Vibration Strength

Status: the application-level vibration path and the firmware motor ceiling are
separate mechanisms. The former is usable for runtime rumble; the latter is a
persistent controller profile value with a verified HID write path.

## Two different meanings of “vibration strength”

### Runtime game rumble

MSI Center M's `UC_ControlMode.UC_Vibration` uses SharpDX XInput:

```csharp
controller.SetVibration(new Vibration {
    LeftMotorSpeed = ...,
    RightMotorSpeed = ...
});
```

The UI stores independent `LM` and `RM` values in the `MP`/`MotorsProfile`
configuration. This is a runtime XInput request and is not proof of a direct MSI
vendor-HID rumble packet. Static analysis of the supplied MSI artifact set did not
find a native report-`0x05` writer that translates XInput to the physical motor.
That final translation remains a Windows-driver/firmware/hardware question.

### Firmware motor ceiling

The controller profile contains two persistent motor ceiling values:

| Motor | Profile field | Confirmed profile offset/address |
| --- | --- | ---: |
| Left | `MP.LeftMotorValue` / `LM` | `0x0022` |
| Right | `MP.RightMotorValue` / `RM` | `0x0023` |

On-device RE reports round-tripped these values as one-byte firmware profile values
and correlated them with MSI Center M. The supplied newer MSI artifact analysis
confirms offsets 34/35 decimal (`0x22/0x23`) in the serialized motor profile, but
that artifact alone does not prove every serialized offset is an EEPROM address.
The direct address claim is based on the earlier controller read/write RE.

## Persistent HID command

Use the controller vendor interface, not `MSI_ACPI`:

```text
PID 1901: usage page 0xFFA0 / usage 0x0001
PID 1902: usage page 0xFFF0 / usage 0x0040
```

For each motor, the verified profile write shape is:

```text
0F 00 00 3C 21 01 00 22 01 <leftValue>
0F 00 00 3C 22

0F 00 00 3C 21 01 00 23 01 <rightValue>
0F 00 00 3C 22
```

The address bytes in a real 64-byte frame are the big-endian profile address;
the examples above show the conceptual address placement. Values are the stored
0–100 percentage used by the observed MSI UI. A one-unit change is therefore a
one-byte value change, but it must be bounded to `0..100` unless a newer device
read proves another accepted range.

The safe sequence is read, preserve, write one byte, sync, and read back. Do not
write a guessed complete profile, and do not assume `0x22/0x23` are valid direct
EEPROM addresses on an untested firmware.

## What is and is not proven

- Proven: MSI's UI keeps left/right motor values separately.
- Proven: the controller profile places them at offsets `0x22` and `0x23` in the
  RE material; round-trip writes were reported on-device.
- Proven: runtime XInput `SetVibration` is used by MSI's vibration test.
- Not proven from the supplied MSI binaries: a native MSI runtime writer for
  report `0x05`, or the exact physical motor translation performed after XInput.
- Not a production policy: whether a future Addon UI should expose 0–100, a
  default ceiling, or per-game values.

## Safety

Do not test by sustained motor stress or by repeatedly dragging a slider. Debounce
any persistent write, keep values conservative, and verify both channels. A
diagnostic should restore the prior values or explicitly report that restoration
failed. Do not conflate a firmware ceiling change with a live game rumble test.

## Evidence

- [RE_MSI_ButtonRemap.md](https://drive.google.com/file/d/1Xgf9lE2LaaIwAoPLekPCrNvKXtIzYpMN/view)
  contains the controller profile offsets and HID profile write format.
- [MSI_COMPLETE_RESEARCH_RESULT.md](https://drive.google.com/file/d/1O6C1X2fnVAB36YK9YW2JTWGhG1xcgIG2/view)
  records the MSI binary audit and the unresolved XInput-to-physical path.
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
  records the vendor HID channel and sync command.
