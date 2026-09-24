# Xbox 360 Rumble Path Comparative Analysis — CTW, Canonical VIIPER, and SteamInputAddonforClaw

**Date:** 2026-09-24  
**Scope:** Static source comparison only. Hardware reproduction/measurement remains owned by the CTW measuring build.  
**Repositories compared:**

- `onehoon/ClawTweaks-Dev`
- `onehoon/VIIPER`
- `onehoon/SteamAddonforClaw`
- historical pre-migration VIIPER source used by CTW for rumble-path comparison

---

## 1. Executive conclusion

The current evidence does **not** support treating the new canonical VIIPER backend as the cause of CTW's newly observed stuck weak-vibration symptom.

The most important distinctions are:

1. CTW and SteamInputAddonforClaw currently use the **same canonical VIIPER artifact**.
2. The Xbox 360 rumble packet interpretation relevant to CTW is effectively unchanged from the old VIIPER path to the canonical path:
   - the same 8-byte Xbox 360 output report is recognized;
   - the same motor bytes are extracted;
   - explicit `0 / 0` motor values are valid and are not intentionally filtered.
3. The migration changed the **application-facing callback API** from the old generic feedback callback to the typed `SetXbox360RumbleCallback(leftMotor, rightMotor)` API. It did **not** introduce a different Xbox 360 rumble protocol or different stop semantics.
4. The meaningful differences between CTW and SteamInputAddonforClaw are primarily **application-side physical-rumble safety and write-policy differences**, not VIIPER backend differences.
5. SteamInputAddonforClaw has already reproduced a closely related real hardware failure class: a last non-zero Xbox 360 rumble state remained physically latched because no terminal physical `0 / 0` followed. The Addon therefore added a bounded 5-second inactivity safety stop.
6. That Addon safety stop does **not** prove that VIIPER was the component that lost the stop. It is an application-side fail-safe against a persistent physical actuator state when terminal feedback stops arriving for any reason.

The best current hypothesis is therefore:

```text
small non-zero Xbox 360 rumble
-> canonical VIIPER forwards it
-> CTW writes a very small physical motor value
-> no later terminal 0 / 0 reaches the physical output path
-> CTW has no inactivity safety stop
-> the MSI Claw keeps the last physical motor state latched
-> a later stronger rumble overwrites that state and the subtle vibration disappears
```

Exactly where the terminal `0 / 0` is lost is **not yet proven**. Static source analysis makes an internal VIIPER drop/reorder less likely, but only native-side packet instrumentation can fully exclude it.

---

## 2. Exact canonical VIIPER artifact identity

The CTW canonical migration and the current SteamInputAddonforClaw production path use the same reviewed VIIPER revision and binary.

```text
repository:
onehoon/VIIPER

commit:
61b6fc236bf71ff4f723223373eabd39c8676ca2

libVIIPER.dll SHA-256:
5F2CE963B8ADA1FDE78BF4A1C25BF063503D761E3D42DA6BB418FE735CE1F948

libVIIPER.h SHA-256:
202444479F20CD599D0AD48890FC644DD3085F9C6ADE1E00FA404E689D88F718
```

This is important because the current CTW-vs-Addon behavior difference cannot be explained as:

```text
CTW VIIPER implementation
vs.
Addon VIIPER implementation
```

at the native backend level. Both applications consume the same canonical native artifact.

---

## 3. Pre-migration vs canonical VIIPER rumble semantics

### 3.1 Historical CTW path

Before the canonical migration, CTW consumed rumble through the generic flat callback:

```text
Xbox 360 USB OUT report
-> VIIPER Xbox360.HandleTransfer
-> XRumbleState
-> generic viiper_device_set_feedback_callback
-> byte[2] { large, small }
-> CTW OnViiperFeedbackReceived
-> OnRumbleReceived
-> WriteRumble
```

### 3.2 Canonical CTW path

After the migration:

```text
Xbox 360 USB OUT report
-> VIIPER Xbox360.HandleTransfer
-> XRumbleState
-> SetXbox360RumbleCallback
-> (leftMotor, rightMotor)
-> CanonicalViiperXbox360Session.OnNativeRumble
-> CTW OnRumbleReceived
-> WriteRumble
```

### 3.3 Relevant native parser behavior is unchanged

The old and canonical Xbox 360 implementations both recognize the wired Xbox 360 rumble output format:

```text
[0] = 0x00
[1] = 0x08
[2] = reserved/status
[3] = left / large / low-frequency motor
[4] = right / small / high-frequency motor
[5..7] = reserved
```

Both extract the same fields.

The canonical implementation also has an explicit unit-test case for:

```text
00 08 00 00 00 00 00 00
-> LeftMotor  = 0
-> RightMotor = 0
```

There is no intentional native `0 / 0` filter in the inspected Xbox 360 path.

**Therefore, the migration changed the callback surface and ownership/lifecycle model, but not the Xbox 360 rumble value or STOP semantics relevant to this incident.**

This distinction is important: the current vibration issue must not be described merely as "the new VIIPER backend changed rumble behavior." The source does not support that statement.

---

## 4. Canonical VIIPER OUT ordering analysis

The canonical USB/IP server treats input and output transfers differently.

Interrupt-IN work may be handled asynchronously, but EP0 and OUT transfers are explicitly handled in order.

Conceptually:

```text
read USB/IP OUT header
-> read complete OUT payload
-> processSubmit(...)
-> Xbox360.HandleTransfer(...)
-> invoke rumble callback synchronously
-> return RET_SUBMIT
-> read next OUT transfer
```

The rumble callback is invoked directly from `Xbox360.HandleTransfer`. It is not queued through a separate rumble worker.

This substantially weakens a VIIPER-internal reordering theory such as:

```text
host sends:
non-zero
0 / 0

but VIIPER delivers:
0 / 0
old non-zero
```

for one normal Xbox 360 attachment/stream.

The pre-migration VIIPER server also processed OUT reports synchronously in the same general order, so this is not a new ordering property introduced by the canonical migration.

This does **not** prove that every host stop packet reaches VIIPER. It only shows that once sequential OUT reports are read by this server path, the inspected implementation does not contain an obvious asynchronous rumble queue that would reorder them.

---

## 5. CTW-vs-Addon differences are application-side differences, not VIIPER backend differences

This section is the most important clarification.

The following differences must **not** be attributed to "our VIIPER" versus "CTW VIIPER". There is no such native backend distinction in the current comparison: both applications use the same canonical VIIPER DLL.

The differences are in the code **after the VIIPER callback**, especially the physical MSI Claw output policy.

| Area | CTW | SteamInputAddonforClaw |
|---|---|---|
| Canonical VIIPER binary | Same artifact | Same artifact |
| Xbox 360 callback | `SetXbox360RumbleCallback` | `SetXbox360RumbleCallback` |
| X360 motor fields | left/right 8-bit | left/right 8-bit |
| Physical MSI report family | `0x05` rumble report | `0x05` rumble report |
| Dedupe authority | CTW `WriteRumble` | Addon physical rumble sink |
| Dedupe state commit | currently before physical write | only after confirmed successful physical write |
| Scaling | user intensity can change the physical 8-bit pair | X360 8-bit magnitude is preserved exactly through 16-bit semantic form |
| Dedupe value domain | currently raw pre-scale pair | effective semantic/wire-equivalent pair |
| Native write completeness | current CTW path does not use returned byte count as success criterion | Addon rejects partial writes |
| Physical HID handle | CTW opens/writes per call | Addon transport caches/reuses the validated handle |
| Bounded physical write protection | no equivalent in current path | 250 ms write cancellation seam |
| Missing terminal STOP protection | none | 5-second inactivity dead-man STOP |

These are **application implementation choices**.

They are not changes to canonical VIIPER's Xbox 360 rumble backend.

### 5.1 Why the Addon safety mechanisms do not prove a VIIPER defect

SteamInputAddonforClaw added extra physical-rumble safety because a physical motor is persistent state. If the last physically written value is non-zero and terminal feedback disappears, the physical device can remain in that state indefinitely.

A dead-man stop therefore protects against multiple possible upstream failure origins:

```text
game / engine
Steam / XInput stack
Windows controller stack
USB/IP transport
VIIPER output path
managed callback path
application output path
```

The safety mechanism intentionally does not need to know which upstream component stopped producing feedback.

Therefore:

> The existence of the Addon's 5-second dead-man safety stop is evidence that the product must fail safe when terminal feedback disappears. It is **not** evidence that canonical VIIPER itself is defective.

---

## 6. Relevant SteamInputAddonforClaw production precedent

SteamInputAddonforClaw previously reproduced a real MSI Claw Xbox 360 rumble latch on hardware.

The observed sequence ended with progressively smaller non-zero physical rumble values and no terminal physical stop, approximately:

```text
Large8=76 Small8=0
Large8=51 Small8=0
Large8=16 Small8=0
... no later physical 0 / 0
```

The physical controller continued vibrating until application teardown later issued an explicit STOP.

The same incident did not show a contemporaneous attach failure, USB/IP attachment loss, or physical HID write failure.

The production response was a bounded Xbox 360 inactivity fail-safe:

```text
non-zero callback
-> write physical rumble
-> arm/refresh 5-second safety window

new non-zero callback
-> refresh safety window

explicit 0 / 0 callback
-> write STOP immediately
-> cancel safety window

no callback after non-zero
-> safety window expires
-> write physical 0 / 0 once
```

This is deliberately implemented in the Addon's presentation/physical-feedback layer, not in VIIPER.

### 6.1 What that historical incident proves

It proves:

- a missing terminal physical STOP can produce an indefinitely latched MSI Claw motor;
- the failure is real, not theoretical;
- a bounded app-side fail-safe prevents indefinite latch;
- an explicit physical `0 / 0` can stop the latched motor.

### 6.2 What that historical incident does **not** prove

Without raw native Xbox 360 OUT packet capture at the incident boundary, it does **not** prove whether:

1. the game/host never emitted a terminal `0 / 0`;
2. Windows/USB-IP never delivered that `0 / 0`;
3. VIIPER received it but failed to callback it;
4. the managed/application path lost it.

The current source analysis makes item 3 less likely, but the historical incident alone cannot fully exclude it.

---

## 7. Confirmed CTW defects found during this investigation

Two application-side CTW defects are real independently of the current reproduction.

### 7.1 Dedupe state is committed before physical write success

Current CTW logic conceptually does:

```csharp
if (large == _prevRumbleLarge && small == _prevRumbleSmall)
    return;

_prevRumbleLarge = large;
_prevRumbleSmall = small;

bool ok = SharedHidWrite(...);
```

If endpoint resolution or the physical write fails after the pre-commit, CTW's dedupe cache can claim that a state was physically written when it was not.

The current real-session logs do not support that failure path as the cause of the reported 2026-09-23 weak-rumble incident: the relevant failure exits log unconditionally and no such failure was observed.

It is nevertheless a real correctness defect and should be fixed separately.

The safer policy is:

```text
calculate desired physical state
-> compare against last successfully written physical state
-> attempt physical write
-> only on confirmed success, commit last-written state
```

### 7.2 CTW dedupes raw values but writes scaled values

CTW currently compares pre-intensity raw motor values while the physical write contains scaled motor values.

Example:

```text
raw large = 1
intensity = 30%
physical large = 0
```

The observed play-session logs include exactly this class of values.

This does not independently explain why a real terminal `0 / 0` would be lost: raw `0 / 0` still scales to physical `0 / 0`.

However, it means CTW's dedupe authority is not tracking the actual physical state and can also prevent an intensity change from being reflected while the same raw rumble remains active.

The dedupe key should therefore be the **scaled physical pair**, and it should be committed only after the physical write succeeds.

Again, this is CTW application behavior. It is unrelated to a change in VIIPER's Xbox 360 rumble parser.

---

## 8. Additional physical-write observation: partial-write visibility

The inspected CTW path treats `WriteFile == true` as success without using the returned written-byte count as a full-write validation signal.

SteamInputAddonforClaw explicitly checks:

```text
written == expected output-report length
```

and classifies a shorter successful API return as `PartialWrite`.

There is currently no evidence that partial writes caused the reproduced CTW symptom, so this should not be promoted to a root-cause claim.

For the measuring build, however, logging:

```text
WriteFile return
bytesWritten
expectedBytes
```

for a diagnostic `0 / 0` stop would remove this ambiguity at almost no conceptual cost.

---

## 9. Current candidate ranking

### Candidate 1 — terminal `0 / 0` is never emitted/delivered to the CTW callback

**Current likelihood: strongest.**

Possible origin remains upstream of the CTW physical writer:

```text
game / host does not issue terminal stop
or
some layer before the managed callback stops delivering feedback
```

This is strongly compatible with the prior Addon hardware latch incident.

The CTW symptom also fits particularly well because intensity scaling can leave a last physical value as small as `1` or `2`, producing a vibration subtle enough not to be noticed immediately.

### Candidate 2 — CTW receives and physically writes `0 / 0`, but MSI firmware does not stop cleanly from a very low motor state

**Current likelihood: possible, unproven.**

This needs hardware measurement.

A successful full-size physical STOP followed by continued vibration would move the investigation strongly toward firmware/device behavior.

### Candidate 3 — a non-zero host output arrives after the terminal `0 / 0`

**Current likelihood: possible.**

If this happens in the host stream, VIIPER should forward that ordering.

This is distinct from VIIPER internally reordering the packets.

### Candidate 4 — silent partial physical HID write

**Current likelihood: low, but cheaply measurable.**

CTW currently has less complete write-result classification than the Addon.

### Candidate 5 — canonical VIIPER drops `0 / 0` or reorders rumble

**Current likelihood: low, but not yet absolutely excluded.**

Reasons it is currently weak:

- the inspected parser accepts `0 / 0`;
- a unit test covers rumble-off decoding;
- old and new VIIPER read the same motor bytes;
- canonical OUT transfers are processed synchronously/in order;
- the typed callback is a direct pass-through;
- CTW and Addon use the exact same canonical binary.

It becomes a serious VIIPER candidate only if measurement proves a boundary such as:

```text
raw VIIPER Xbox360.HandleTransfer sees 0 / 0
but
SetXbox360RumbleCallback does not deliver 0 / 0
```

or if raw native ordering and callback ordering differ.

Until such evidence exists, changing VIIPER would be speculative.

---

## 10. Most plausible sequence from current evidence

A representative sequence is:

```text
game/host generates weak rumble
-> canonical VIIPER callback: e.g. 3,6
-> CTW intensity: 30-35%
-> physical output: approximately 1,2
-> MSI Claw motor is now vibrating very slightly

terminal 0 / 0 is not subsequently observed by the physical output path
-> CTW has no inactivity safety stop
-> last physical 1,2 state remains latched

later game effect generates stronger rumble
-> new non-zero callback
-> CTW writes a new physical state
-> the previously stuck subtle state disappears/changes
```

This matches the reported user experience closely:

- strong vibration behaves normally;
- the CTW test slider behaves normally;
- only some very subtle in-game vibration remains stuck;
- a later stronger vibration clears the symptom.

---

## 11. Measurement required to separate the remaining candidates

The CTW developer's proposed measuring build is the correct next hardware step.

For every `OnRumbleReceived(0,0)`, record:

```text
raw callback pair
scaled physical pair
dedupe result: written / deduped
last successfully physically written scaled pair
physical WriteFile result
bytes written / expected bytes
```

One stuck event then gives a useful decision tree.

### Result A

```text
last callback = non-zero
no later OnRumbleReceived(0,0)
```

This supports missing terminal feedback.

Next diagnostic step, if root-source attribution is still required:

instrument canonical VIIPER immediately where the Xbox 360 OUT packet is decoded.

### Result B

```text
VIIPER raw OUT sees 0 / 0
CTW managed callback does not
```

Now investigate the canonical native callback bridge / ABI / callback lifetime.

This is the point at which a VIIPER change becomes justified.

### Result C

```text
CTW receives 0 / 0
physical full write succeeds
bytesWritten == expected
but motor remains active
```

This strongly shifts the investigation toward MSI firmware / physical motor behavior.

### Result D

```text
CTW receives 0 / 0
but the physical write is deduped or not fully written
```

This is an application-side CTW physical-output bug.

---

## 12. Recommended product policy independent of root-cause attribution

The measurement build should remain measurement-only, as planned.

Separately, the following CTW corrections are justified on their own merits:

1. Commit the dedupe state only after a confirmed successful physical write.
2. Dedupe the scaled physical motor pair, not the raw pre-intensity pair.
3. Validate complete physical output writes where practical.
4. Consider the same bounded Xbox 360 inactivity safety stop already proven in SteamInputAddonforClaw.

The dead-man should remain a **physical-output safety policy in CTW**, not a synthetic-stop feature added to VIIPER.

Conceptually:

```text
non-zero physical rumble written
-> start/refresh bounded inactivity stop

explicit 0 / 0
-> stop immediately
-> cancel inactivity stop

feedback disappears after non-zero
-> bounded timeout
-> force one physical 0 / 0
```

This protects the user regardless of whether the terminal event disappeared in the game, XInput/Steam/Windows, USB/IP, native callback delivery, or another upstream layer.

It also avoids assigning physical MSI Claw policy to the generic VIIPER virtual-device backend.

---

## 13. Final attribution statement

The most defensible statement from the current evidence is:

> The canonical VIIPER migration changed CTW's ownership/attachment model and replaced the generic feedback API with a typed Xbox 360 rumble callback, but the relevant Xbox 360 rumble parsing and STOP semantics did not materially change. CTW and SteamInputAddonforClaw now use the same canonical VIIPER binary. The meaningful rumble-behavior differences between the two applications are app-side physical-output policies, especially successful-write state tracking and the Addon's 5-second inactivity safety stop.

And regarding the unresolved root cause:

> A canonical VIIPER defect is currently **not supported by the static source evidence and is considered a low-probability candidate**, but it is not yet mathematically excluded because the affected sessions did not capture the raw native Xbox 360 OUT stream. Do not modify VIIPER unless measurement demonstrates that a valid `0 / 0` reaches the VIIPER native boundary but is lost or reordered before the managed callback.

This preserves the correct distinction between:

```text
root-cause attribution
and
application-side fail-safe responsibility
```

The Addon's existing dead-man safety stop addresses the latter and should not be interpreted as an admission that VIIPER caused the former.
