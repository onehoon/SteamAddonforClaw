# RE: MSI Claw Controller Vibration

Status: runtime physical rumble and persistent firmware motor-profile values are
separate mechanisms. The profile serialization offsets are known, but their use
as direct persistent firmware addresses is not validated for the currently
supported MSI Claw models. Production firmware reads are diagnostic-only and
production writes fail closed until the model-specific mapping is proven.

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

### Firmware motor profile

The MSI motor profile serializes independent left/right values at these offsets:

| Motor | Profile field | Serialized profile offset | Direct firmware address |
| --- | --- | ---: | --- |
| Left | `MotorModule.LeftMotorValue` / `LM` | `0x22` | Not proven per current model |
| Right | `MotorModule.RightMotorValue` / `RM` | `0x23` | Not proven per current model |

#### Evidence level A — serialization offsets are proven

The supplied MSI artifact analysis identifies the left and right motor fields at
34 and 35 decimal in the serialized profile (`0x22` and `0x23`). This establishes
the profile layout only.

> A serialization offset is not, by itself, proof that the same numeric value is
> the direct persistent firmware address on MS-1T42, MS-1T52, or MS-1T91.

#### Evidence level B — historical on-device round-trip evidence exists

Earlier on-device RE reported one-byte reads/writes and round-trip behavior for
these values, correlated with MSI Center M. Preserve that historical observation;
it is not discarded. However, the repository evidence does not bind that result to
each currently supported model and firmware contract, so it must not be generalized
into a production write permission for every MSI Claw.

#### Evidence level C — current model mapping requires validation

No currently supported model is enabled for production access to the direct
`0x22`/`0x23` motor addresses:

| Addon model ID | Board ID | Production direct-address capability |
| --- | --- | --- |
| `msi.claw.a2vm.7` | MS-1T42 | Unverified; read-only diagnostic probe only |
| `msi.claw.a2vm.8` | MS-1T52 | Unverified; read-only diagnostic probe only |
| `msi.claw.cg3em` | MS-1T91 | Unverified; read-only diagnostic probe only |

On 2026-10-04, the tested CG3EM/MS-1T91 reported Left/Right `50% / 50%` in the
Center M UI, while the Addon's `0x22`/`0x23` read path presented `100% / 100%`
before any Addon mutation in that log session. Subsequent Addon mutations were
observed, so do not infer or automatically restore a presumed stock value.
This mismatch means those bytes cannot currently be presented as authoritative
Center M motor settings on this model.

## Historical vendor-HID command evidence

Earlier RE recorded the controller vendor interface, rather than `MSI_ACPI`, for
profile operations:

```text
PID 1901: usage page 0xFFA0 / usage 0x0001
PID 1902: usage page 0xFFF0 / usage 0x0040
```

The historically observed profile write and sync shape was:

```text
0F 00 00 3C 21 01 00 22 01 <leftValue>
0F 00 00 3C 22

0F 00 00 3C 21 01 00 23 01 <rightValue>
0F 00 00 3C 22
```

The address bytes in a real 64-byte frame are the big-endian profile address;
these examples show the conceptual address placement. The observed read parser
requires the report ID, command marker, read-profile acknowledgement, requested
address, one-byte length, and a value in `0..100`. Keep those checks strict.
Historical command shape and parser success do not establish that the value is
the Center M motor setting for a particular current model.

The hotfix's diagnostic capture may issue bounded reads for `0x22` and `0x23` on
an unverified model. It logs a short response prefix and parse result at Info level
for explicit Controller-page capture only. It does not log a complete HID report,
does not mutate the profile, and never returns those raw values as production
percentages. Every probe is marked `VerifiedForProduction=False`.

## Model-specific validation procedure

Do not enable production writes until the exact model's direct mapping is proven.

### Phase A — stock authority / PID1901

Boot with Center M Enabled and MSI authority. Record model, board ID, PID,
usage page/usage, Center M Left/Right values, and the diagnostic events for both
addresses. If safe, change Center M to a clearly asymmetric pair such as Left
`40%` / Right `70%`, close or settle the UI, then capture again. A single matching
default pair is not sufficient evidence.

Required candidate evidence:

```text
Center M 40/70 -> PID1901 diagnostic values 40/70
```

### Phase B — Addon authority / PID1902

Use the normal reboot-bound Disable Center M and Restart path. Do not perform a
same-session authority handoff. After Full1902 PID1902 ownership is established,
open Controller > Vibration Strength to collect the same bounded diagnostic
probe and compare with the last stock values.

```text
PID1901 tracks the asymmetric Center M values
PID1902 tracks the same values
-> strong model/firmware-specific direct-mapping evidence candidate

PID1902 differs, returns 100/100, or has a different response shape
-> keep production writes disabled and investigate the mode-specific behavior
```

If PID1901 does not track Center M, the offsets are not the direct Center M
setting on that model or another translation layer exists. Any ambiguous or
incomplete result remains unverified.

### Phase C — separate enablement change

Only after evidence for a specific model/firmware contract is reviewed should a
separate focused change enable that model's direct-address capability. Then run
the existing bounded write/readback acceptance test. Do not combine a plausible
byte value or one successful parse with production enablement.

## Safety and current production policy

- All currently supported models fail closed for production `0x22`/`0x23` writes.
- A valid known MSI model or TDP capability does not prove vibration address support.
- No hard-coded `50%` or `100%` fallback is allowed.
- Unverified capture returns no Left/Right percentages; the UI displays `—` and
  disables/hides the strength sliders. The live physical rumble Test remains a
  separate capability.
- A valid Set request on an unverified model returns unavailable before HID
  enumeration/read/write. `WriteProfile` and `SyncToROM` are never issued.
- Do not automatically restore `50/50` or any other presumed prior values.
- Do not test with sustained motor stress or repeated slider movement. A future
  explicitly authorized write test must preserve/read back both channels and
  report restoration failure rather than guessing.
- Do not conflate a persistent firmware ceiling with a live game-rumble test.

## Evidence sources

- [RE_MSI_ButtonRemap.md](https://drive.google.com/file/d/1Xgf9lE2LaaIwAoPLekPCrNvKXtIzYpMN/view)
  contains controller profile offsets and HID profile command format.
- [MSI_COMPLETE_RESEARCH_RESULT.md](https://drive.google.com/file/d/1O6C1X2fnVAB36YK9YW2JTWGhG1xcgIG2/view)
  records the MSI binary audit and unresolved XInput-to-physical path.
- [clawtweaks-hid-protocol.md](https://drive.google.com/file/d/1TuI-21v5nT0u6hdSksUfKLuPCSHyB2d-/view)
  records the vendor HID channel and sync command.
