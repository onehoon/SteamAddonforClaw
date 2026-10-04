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
before any Addon mutation in that log session. The Center M values were local UI
profile values, not device readback, so this was not a direct comparison between
two hardware reads. Subsequent Addon mutations were observed, so do not infer or
automatically restore a presumed stock value. These bytes cannot currently be
presented as authoritative Center M motor settings on this model.

### Static Center M profile-flow findings

The following findings are **PROVEN within the individually inspected binaries**:

| Finding | Evidence |
| --- | --- |
| The vibration UI values are loaded from Center M's local `ControlProfile` / `profile.rec` state (`MP.LM` / `MP.RM`), not from device profile readback. | `UC_ControlMode.dll` UI/profile path |
| `MotorModule.LeftMotorValue` and `RightMotorValue` serialize at profile-relative offsets `0x22` and `0x23`. | `API_ControlMode.dll` profile serialization |
| An inspected profile write uses index `1`; when the cached profile changes only LM/RM, it can write the contiguous two-byte range at offset `0x22`. | `API_ControlMode.dll` profile save path |
| The inspected whole-profile read requests index `0`. | `API_ControlMode.dll` profile load path |
| Center M queues `SyncToROM` (`0x22`) after its profile write request. | `API_ControlMode.dll` command queue path |

The inspected versions were `UC_ControlMode.dll 1.0.2608.1201` and
`API_ControlMode.dll 1.0.2606.2401`; they do not match. These are static findings
within the inspected files and do not prove cross-assembly runtime compatibility.
If Center M's cached profile is absent, its save path may send multiple bounded
chunks rather than the two-byte LM/RM range.

The following remain **UNKNOWN**:

- firmware meaning of profile index `0` versus `1`;
- whether response byte `[5]` echoes the request index;
- whether CG3EM firmware `0x0419` maps both requests to the same profile state;
- whether a successful Center M save request proves persistent device acceptance;
- whether PID1901 and PID1902 share the same firmware profile bank.

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

The diagnostic capture on an unverified model issues exactly two bounded pair
reads: index `0`, offset `0x22`, length `2`, followed by index `1` with the same
offset and length. It logs up to the first 11 response bytes, response-index and
echo evidence, address/length fields, and structurally valid candidate LM/RM
bytes at Info level for explicit Controller-page capture only. It does not log a
complete HID report, mutate the profile, or return those candidates as production
percentages. Every probe is marked `VerifiedForProduction=False`; the production
single-byte parser remains strict about its historical index-1 response shape.

## Model-specific validation procedure

Do not enable production writes until the exact model's direct mapping is proven.

### First diagnostic — Addon authority / PID1902

On CG3EM/MS-1T91 firmware `0x0419`, use the normal reboot-bound Full1902
authority flow and verify physical PID1902. In Center M, set a clearly
asymmetric local profile pair such as Left `30%` / Right `70%`, allow its normal
save/debounce path to settle, then open the Addon Controller page once. Collect
both `ControllerVibrationProfileIndexProbe` events and record the request index,
response index/echo, response address/length, candidate bytes, and structural
parse result. This comparison is diagnostic only; it does not prove that Center
M's local profile was accepted by firmware.

Compare the two results without treating either as authoritative. For example:

```text
index 0 -> 30/70; index 1 -> 50/50
index 0 -> 30/70; index 1 -> 30/70
index 0 -> 50/50; index 1 -> 50/50
```

All outcomes keep production writes disabled. A malformed or missing response is
also evidence and must not be converted into a guessed percentage.

### Follow-up only if index behavior remains ambiguous — PID1901

PID1901 comparison is a separate later investigation, not a prerequisite for the
first PID1902 diagnostic. If needed, use the normal reboot-bound Center M
authority transition; do not perform same-session PID switching. Record whether
the same two index probes track the asymmetric Center M local values.

Whether PID1901 and PID1902 share the same bank remains unknown. Any ambiguous,
incomplete, or mode-dependent result remains unverified.

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
