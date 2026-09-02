# RE: MSI Claw Battery Charge Limit

Status: WMI command path established from HHC source analysis and MSI Center M
artifacts. The protocol can represent one-unit changes; firmware acceptance and
the actual battery behavior still require device validation for each model.

## Control surface

Battery charge limit is not a controller HID command. It uses `msiapcfg.dll` to
expose the MSI ACPI WMI provider:

```text
Namespace:  root\\WMI
Class:      MSI_ACPI
Instance:   InstanceName='ACPI\\PNP0C14\\0_0'
Methods:    Get_Data / Set_Data
Block:      215 (0xD7)
```

The WMI wrapper sends a 32-byte package whose first byte identifies the data block.
The returned data must be treated as a real read result; do not synthesize it when
the read fails.

## Byte format

HHC's `SetBatteryMaster` and `SetBatteryChargeLimit` behavior identifies one byte
in block `215`:

```text
bit 7       charge-limit enable
bits 0..6   charge-limit value
```

The safe write is read-modify-write:

```text
old = Get_Data(215)
new = (old & 0x80) | (requestedValue & 0x7F)
Set_Data(215, new)
read back Get_Data(215)
```

To enable/disable without changing the selected value:

```text
enable:  new = old | 0x80
disable: new = old & 0x7F
```

The exact byte position inside the returned data wrapper must follow the existing
WMI helper's payload convention. The important invariant is that the original
block payload is read first and all unrelated bytes are preserved.

## One-unit adjustment

For a one-unit adjustment, preserve bit 7 and replace only the lower seven bits:

```text
requestedValue = currentValue + 1   // or -1
new = (old & 0x80) | requestedValue
```

This is a one-unit protocol write, not a UI policy statement. The lower seven bits
allow values through `127` at the encoding level, but the supported/meaningful
range, firmware floor/ceiling, and whether the device quantizes the value must be
confirmed by readback and battery observation. Do not infer that the protocol's
7-bit storage means a production UI should expose `0..127`.

## Required failure policy

The operation must fail closed:

1. `Get_Data(215)` fails: do not write.
2. The returned payload is too short or invalid: do not write.
3. `Set_Data` returns failure: report failure.
4. Readback does not preserve the enable bit and requested lower-seven-bit value:
   report verification failure.

Do not write from an all-zero buffer. Do not treat `0` as a valid read when WMI
reported an error. Keep the operation independent of TDP, fan, controller mode,
HidHide, and routing.

## Center M coexistence

Center M and HHC use the same WMI/ACPI family for this setting. A future Addon
implementation must determine whether Center M also mirrors the setting in an
off-EC store and may reapply it later. The existing RE material establishes this
coexistence rule for controller profile settings, but it does not prove that the
battery component has the same persistence file semantics. Therefore:

- read the live WMI value before any change;
- document whether the change is transient or persisted;
- do not silently fight a running Center M instance;
- verify after a Center M Apply if coexistence is required.

## Evidence and limits

- [HHC_msiapcfg_analysis.txt](https://drive.google.com/file/d/1vBp99KyQbbMC16ka2hoJJDNMVp0g-v8N/view)
  identifies block `215/0xD7`, bit 7, and lower-seven-bit value handling.
- Local MSI files include `msiapcfg.dll`, `MSIWMIACPI2.dll`, `UC_Battery.dll`, and
  `API_Battery.dll`. Decompilation of `UC_Battery.dll` shows the UI dispatches
  `SetBatteryMode:<value>`; the WMI backend is the authoritative command path.
- The current repository README does not expose a battery-limit production feature;
  this note intentionally does not add one.
