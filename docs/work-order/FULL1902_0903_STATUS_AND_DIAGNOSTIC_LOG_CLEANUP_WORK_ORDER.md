# Work Order — Full1902 0903 Status and Diagnostic Log Cleanup

## Status

Corrective single-PR work order based on real MSI Claw Full1902 hardware logs captured on 2026-09-03 with Runtime v0.1.208.0.

This PR is intentionally small. It fixes one real operational-status mismatch and three diagnostic/logging quality problems discovered in an otherwise successful Full1902 session.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     ea6f7d9477b58768950c3f3fe348a2cfcbee328f
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/PR4_DISABLED_BOOT_ADMISSION_WORK_ORDER.md`
- `docs/work-order/PR11_FULL1902_HARDWARE_VALIDATION_ROUTING_AND_STARTUP_FIXES_WORK_ORDER.md`
- `docs/work-order/FULL1902_POLICY_A2_DECOUPLE_FRONT_BUTTON_ACTIONS_AND_DISABLE_LEGACY_ROUTING_WORK_ORDER.md`

Also inspect current `main` implementations of:

- `Status/SystemStatusProvider.cs`
- `Status/AddonStatusEvaluator.cs`
- `Status/SystemStatusContracts.cs`
- `Runtime/AddonRuntimeComposition.cs`
- `Hosting/AddonProcessHost.cs`
- `Frontend/FrontendSnapshotMapper.cs`
- `Controllers/Detection/ControllerTopologySnapshot.cs`
- `Controllers/Detection/ControllerDeviceClassifier.cs`
- `Devices/MSI/Claw/MsiClawInputSource.cs`
- `Diagnostics/DiagnosticSession.cs`
- focused tests for the above components.

---

# 1. Goal

Make Full1902 status and logs reflect the actual controller state observed on supported hardware without changing controller authority, ownership, recovery, PnP, HidHide, VIIPER, or presentation-switch behavior.

The 2026-09-03 session proved the controller path itself is healthy:

```text
Center M startup roots = Disabled
→ Disabled-boot admission = Ready
→ deterministic HidHide baseline verified
→ PID1901 -> PID1902 transition verified
→ exact PID1902 DirectInput device selected
→ DirectInput acquire + first valid read succeeded
→ physical isolation verified
→ Full1902 physical ownership committed
→ Xbox360 presentation attached
→ BPM transition switched Xbox360 -> SteamDeck
→ BPM exit switched SteamDeck -> Xbox360
→ all observed publishers reported SetStateFailures=0
```

No controller lifecycle blocker was found in this capture.

The PR therefore must not modify the working controller path merely to make logs look cleaner.

The required changes are:

1. fix the false `AddonStatus=Indeterminate` result while a healthy Full1902 Disabled-mode controller is actually owned and presented;
2. remove the high-volume generic PnP ancestry log emitted for unrelated Windows devices;
3. remove the misleading/redundant `MsiInput` state-change log that repeatedly reports only `M1=False->False` / `M2=False->False` for unrelated button/analog changes;
4. stop treating an Overlay Show request in stock/passive authority as a warning, while preserving warning severity when Full1902 authority should actually have an owned source/presentation.

Keep this as one PR.

---

# 2. Real hardware evidence

## 2.1 Healthy Full1902 Runtime is reported as `AddonStatus=Indeterminate`

During the successful Center M Disabled session, the Runtime repeatedly had all of the important positive facts:

```text
Center M startup state = Disabled
DisabledBootAdmission = Ready
Physical input source = running
Full1902 physical owner = present
Active presentation = Xbox360 or SteamDeck
```

but the ordinary system-status refresh still evaluates the old stock/legacy compatibility path:

```text
ControllerEnvironmentCompatibility
→ Unsupported / MsiCenterMNotOperational

RoutingEligibilityPolicy
→ legacy routing decision

AddonStatusEvaluator
→ AddonStatus=Indeterminate
```

Current code in `SystemStatusProvider` does this unconditionally:

```csharp
var decision = RoutingEligibilityPolicy.Evaluate(...);
var addon = AddonStatusEvaluator.Map(decision, compatibility);
```

That result is no longer authoritative for Addon operational status while Full1902 owns the controller.

### Important: `MsiCenterMNotOperational` itself is NOT the bug

PR4 explicitly established:

> Do not use the existing stock compatibility result as the Disabled-mode authority gate.

The stock compatibility policy intentionally expects MSI Center M to be running. Therefore while Center M is intentionally Disabled:

```text
MsiCenterMNotOperational
```

is expected.

PR11 explicitly preserved the same rule and prohibited broad changes to `ControllerEnvironmentCompatibility` for this case.

Therefore this PR must **not** change the meaning of:

```text
ControllerEnvironmentCompatibilityStatus.Unsupported
ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational
```

and must not make the stock compatibility evaluator pretend that Center M is operational.

The defect is only that a legacy compatibility/routing-derived status is still being used as the final Addon operational status after Full1902 has positively established its own live controller path.

## 2.2 PnP ancestry debug flood

The 2026-09-03 logs contain roughly 870-890 occurrences per large startup session of:

```text
[PnP] Controller ancestry resolved.
```

The current implementation emits this inside the generic `ControllerTopologySnapshot.ResolveAncestors(...)` helper:

```csharp
AppLog.Debug("PnP", "Controller ancestry resolved.", ...);
```

`ControllerDeviceClassifier` calls that helper while classifying the captured Windows device set, so the log is emitted for many unrelated Audio, ACPI, USB4, and other devices.

The ancestry resolution itself is correct and must remain unchanged. The problem is only the per-call generic logging location.

## 2.3 `MsiInput` state log is misleading and redundant

The same Full1902 session contains nearly 200 `MsiInput` state-change entries, most of which look like:

```text
ControllerState changed. M1=False->False M2=False->False
```

This happens because `MsiClawInputSource` logs a generic state-change line whenever the mapped `ControllerState` changes, but that line only prints M1/M2 fields:

```csharp
AppLog.Debug(
    "MsiInput",
    "ControllerState changed.",
    ("TestSession", session),
    ("M1", $"{IsM1Pressed(previous)}->{IsM1Pressed(current)}"),
    ("M2", $"{IsM2Pressed(previous)}->{IsM2Pressed(current)}"));
```

A B button, D-pad, stick, or trigger change can therefore produce a line that visually claims no M1/M2 change.

This is not an input bug.

The existing generic `Input` diagnostics already log the fields that actually changed, and `MsiClawInputSource` already has dedicated M1/M2 change logs when those buttons really change.

## 2.4 Expected Overlay unavailability is logged as WARN

In the Center M Enabled / stock-authority session, repeated manual Overlay POC requests produced WARN entries:

```text
Overlay Show not attempted; no owned presentation / running PR5 source.
HasPresentation=False
SourceRunning=False
```

That is expected while the Addon does not own the controller.

However, the same missing-source/presentation condition can be meaningful after Center M is Disabled and Full1902 ownership is expected.

Therefore do not blindly downgrade every occurrence. Severity should reflect the actual authority mode.

---

# 3. Product invariants — do not change

This PR must preserve the Full1902 authority contract exactly:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired physical PID = PID1901
→ no Addon DirectInput ownership
→ no Addon controller HidHide ownership
→ no Addon VIIPER controller presentation

Center M Disabled
→ Addon Runtime controller authority
→ desired physical PID = PID1902
→ DirectInput owned by Addon
→ deterministic HidHide baseline owned by Addon
→ exactly one Full1902 virtual presentation attached/live

Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

This PR must not change:

- Center M Enabled/Disabled authority selection;
- reboot-bound transition semantics;
- PID1901 <-> PID1902 native writes;
- cross-mode transition validation;
- same-mode physical identity validation;
- PnP stabilization timing/polling;
- DirectInput acquire/recovery;
- HidHide normalization or ownership;
- physical isolation verification;
- VIIPER lifetime/attach/detach;
- Xbox360 <-> SteamDeck presentation switching;
- publisher scheduling;
- suspend/resume handling;
- physical device-loss recovery;
- Win+G suppression ownership;
- OEM1/WING mapping policy;
- QAM injection behavior.

The observed controller lifecycle is already working. Do not perturb it for an observability cleanup.

---

# 4. Required change A — Full1902-aware Addon operational status

## 4.1 Keep legacy compatibility and routing facts intact

`SystemStatusSnapshot.Compatibility` and `SystemStatusSnapshot.RoutingDecision` may continue to expose the existing legacy/stock facts for existing diagnostics and consumers.

Do not rewrite them merely to force the final Addon status to `Ready`.

In particular, this remains valid while Center M is Disabled:

```text
Compatibility = Unsupported / MsiCenterMNotOperational
```

The change should affect only the final derived `AddonStatusSnapshot` when the Runtime can positively prove a healthy Full1902 controller path.

## 4.2 Use existing live owners as facts — do not add a new authority source

The process already has the required facts in `AddonProcessHost`:

```text
_startupResult.CenterMStartupState
_disabledControllerStartupPending
_physicalOwnership
_physicalOwnership.LiveInputSource.IsRunning
_presentationOwnership
_presentationOwnership.ActivePresentation
```

Use those facts.

Do not add:

- `ControllerAuthorityManager`;
- a persisted `Full1902Active` boolean;
- another Center M startup reader/writer;
- another PnP inspector;
- another VIIPER status manager;
- a status cache that can drift from the owners;
- a periodic status polling loop;
- epoch/barrier machinery for status reporting.

A narrow process-local read delegate is sufficient.

## 4.3 Preferred minimal shape

Add an optional narrow Full1902 Addon-status provider to the status composition.

Conceptually:

```csharp
Func<AddonStatusSnapshot?>? captureFull1902AddonStatus
```

`null` means:

```text
No positively-proven Full1902 operational override
→ use the existing AddonStatusEvaluator result unchanged
```

A non-null snapshot is allowed only when the existing owners positively prove the healthy Disabled-mode path.

`SystemStatusProvider.CaptureCore()` should conceptually become:

```csharp
var decision = RoutingEligibilityPolicy.Evaluate(...);
var legacyAddon = AddonStatusEvaluator.Map(decision, compatibility);
var addon = TryCaptureFull1902AddonStatus() ?? legacyAddon;
```

If the optional provider throws, status capture must not crash. Log/handle it at the existing appropriate diagnostic level and fall back to the legacy result.

Do not turn a status-only exception into controller mutation or recovery.

## 4.4 Positive Full1902 Ready boundary

The override must be conservative.

Return `AddonOperationalStatus.Ready` only when all of these in-memory facts are true at capture time:

```text
startupResult.CenterMStartupState == Disabled
_disabledControllerStartupPending == 0
_physicalOwnership?.LiveInputSource?.IsRunning == true
_presentationOwnership?.ActivePresentation != null
```

Suggested reason text:

```text
Full1902 controller authority is active (Xbox360).
Full1902 controller authority is active (SteamDeck).
```

The exact wording may follow existing status conventions.

Do not require a new PnP capture, HidHide readback, or VIIPER probe here. Those owners already passed their real safety boundaries before reaching this state.

## 4.5 Do not invent failure-state taxonomy in this PR

If the positive boundary above is not met:

```text
return null
→ preserve existing status result
```

Do not use this cleanup PR to redesign all possible Disabled-mode failure statuses into `RecoveryRequired`, `SetupRequired`, etc.

Examples that must NOT be falsely reported `Ready`:

```text
Disabled startup still pending
Disabled admission blocked
physical ownership not established
LiveInputSource missing/stopped
presentation not attached
physical device currently lost/recovering
```

A later status redesign may classify those states more precisely if real product need justifies it.

This PR only removes the proven false-negative for the healthy state.

## 4.6 Composition

Prefer passing the narrow provider from `AddonProcessHost` into the existing `AddonRuntimeCompositionFactory` / `SystemStatusProvider` construction.

The provider should close over the existing `AddonProcessHost` owner fields and perform only lock-free/read-only inspection of already-owned in-memory facts.

If adding an optional parameter avoids broad test/call-site churn, use an optional parameter rather than modifying every unrelated constructor call.

Do not expose this as a new frontend RPC.

## 4.7 Logging expectation

After the healthy Full1902 controller has committed, ordinary status refresh should log something equivalent to:

```text
System status snapshot refreshed.
AddonStatus=Ready
```

while the stock compatibility fact may still separately remain:

```text
Compatibility=Unsupported
Reason=MsiCenterMNotOperational
```

That combination is intentional in Full1902 Disabled mode.

---

# 5. Required change B — remove generic per-device PnP ancestry logging

Current code:

```text
Controllers/Detection/ControllerTopologySnapshot.cs
```

`ResolveAncestors(...)` is a generic functional helper and is called broadly during classification.

Remove the unconditional per-call:

```csharp
AppLog.Debug("PnP", "Controller ancestry resolved.", ...);
```

from that generic helper.

Keep the functional ancestry resolution exactly the same.

Preferred outcome:

```text
Controller topology snapshot created.
```

may remain as one bounded summary per captured snapshot.

Existing targeted controller-candidate / physical-identity logs may also remain where they provide useful evidence for MSI Claw selection or transition verification.

Do not replace the removed line with another log inside the same broad per-device loop.

Do not add VID/PID filtering work solely for logging if simply removing the generic log is enough.

The purpose is to eliminate hundreds of unrelated lines, not to create a new logging classifier.

---

# 6. Required change C — remove misleading duplicate `MsiInput` state log

Keep the existing dedicated M1/M2 logs:

```text
M1 state changed.
M2 state changed.
```

They are useful because those auxiliary buttons have MSI-specific mapping/diagnostic value.

Remove the redundant generic `MsiInput` line that logs every `ControllerState` change while only displaying M1/M2 old/new values:

```text
ControllerState changed. M1=False->False M2=False->False
```

Do not replace it with another duplicate full-state formatter.

The existing `Diagnostics/DiagnosticSession.cs` `Input` category already reports the actual changed fields and throttles analog detail.

The desired logging split is:

```text
MsiInput
→ MSI-specific DirectInput selection/session diagnostics
→ dedicated M1/M2 change evidence

Input
→ actual ControllerState field changes
```

This is a logging-only change. Do not modify mapping, event emission, polling frequency, or `LatestState` publication.

---

# 7. Required change D — authority-aware Overlay unavailable severity

Current branch in `AddonProcessHost`:

```csharp
if (presentation is null || source is not { IsRunning: true })
{
    AppLog.Warn(
        "OverlayCapture",
        "Overlay Show not attempted; no owned presentation / running PR5 source.",
        ...);
    return;
}
```

Keep the same no-op behavior.

Only adjust severity using the already-known authority fact.

Conceptually:

```text
Center M Enabled / stock authority
+ no Full1902 presentation/source
→ expected
→ INFO or DEBUG

Center M Disabled / Addon authority expected
+ no Full1902 presentation/source
→ potentially meaningful controller/ownership degradation
→ WARN remains appropriate
```

Prefer `Info` for an explicit user-triggered POC request that cannot run because stock authority is active. `Debug` is also acceptable if existing Overlay logging policy treats expected no-op requests as debug-only.

Do not:

- start Full1902 ownership just because Overlay was requested;
- create a presentation for Overlay;
- change PID;
- change HidHide;
- add retry machinery;
- add a new Overlay availability state machine;
- redesign Overlay UI enable/disable behavior in this PR.

The existing early return remains the safety behavior.

---

# 8. Explicitly out of scope

Do not mix any of the following into this PR:

- changing `ControllerEnvironmentCompatibility` Disabled-mode semantics;
- making `MsiCenterMNotOperational` report Supported;
- deleting the legacy compatibility/routing model wholesale;
- redesigning the Status page's legacy `ControllerStatusText` / routing wording;
- adding a new Full1902 frontend wire contract solely for this cleanup;
- physical mode switching changes;
- PnP transition timing changes;
- PnP identity changes;
- device-loss recovery changes;
- suspend/resume changes;
- HidHide changes;
- VIIPER changes;
- Xbox360/SteamDeck switching changes;
- publisher timing optimizations;
- QAM changes;
- Overlay transport/capture lifecycle changes;
- OEM1/WING policy changes;
- unrelated log cleanup.

If implementation reveals that a requested log cleanup requires any of those architectural changes, stop and keep the cleanup smaller rather than expanding the PR.

---

# 9. Tests

Add focused regression tests. Do not build a large new test framework.

## 9.1 Full1902 status tests

Required cases:

```text
legacy / no Full1902 override
→ existing AddonStatusEvaluator result unchanged

Center M Enabled
→ Full1902 provider returns null
→ existing stock/legacy status unchanged

Center M Disabled but startup pending
→ NOT Ready

Center M Disabled, physical source missing/stopped
→ NOT Ready

Center M Disabled, source running, no active presentation
→ NOT Ready

Center M Disabled, source running, Xbox360 active
→ AddonStatus=Ready

Center M Disabled, source running, SteamDeck active
→ AddonStatus=Ready

Full1902 status provider throws
→ status capture does not crash
→ legacy status is used
```

Preserve existing tests proving `ControllerEnvironmentCompatibility` returns `MsiCenterMNotOperational` when Center M is not running. Do not update those tests to hide the intended legacy fact.

If practical, add one architecture/composition test proving the Full1902 Ready provider reads the existing `_physicalOwnership` / `_presentationOwnership` facts and does not introduce another controller owner.

## 9.2 PnP tests

Keep existing `ControllerTopologySnapshotTests` functional ancestry assertions unchanged/passing:

```text
resolved ancestors remain case-insensitive and correct
missing ancestor remains ignored exactly as before
```

If the logging test infrastructure makes this cheap, assert that `ResolveAncestors()` no longer emits one `Controller ancestry resolved` line per call.

Do not add expensive Windows PnP integration tests for a removed debug line.

## 9.3 MsiInput tests

Preserve all input mapping and M1/M2 behavior tests.

Where log capture is already available, verify:

```text
M1 transition → dedicated M1 log remains
M2 transition → dedicated M2 log remains
unrelated ControllerState change → no redundant MsiInput M1=False->False / M2=False->False generic line
```

Do not alter StateChanged semantics to satisfy the log test.

## 9.4 Overlay severity tests

Add the smallest focused test possible for the branch:

```text
stock authority + no source/presentation
→ request remains no-op
→ not WARN

Disabled/Add-on authority expected + no source/presentation
→ request remains no-op
→ WARN remains
```

If current logging utilities make direct severity assertions disproportionately invasive, a small extracted pure severity helper is acceptable only if it reduces code/test complexity. Do not create an Overlay availability manager.

---

# 10. Hardware validation

After automated tests pass, validate one normal supported-device sequence with debug logging enabled:

```text
1. boot with Center M Enabled
2. request Overlay POC once
   → no controller mutation
   → expected unavailable request is not WARN

3. Disable Center M and Restart
4. verify Disabled admission Ready
5. verify PID1902 ownership and Xbox360 presentation
6. refresh/open Status
   → AddonStatus=Ready
   → no false "MSI Center M is not operational" Addon warning
   → legacy compatibility may still internally report MsiCenterMNotOperational

7. enter BPM
   → Xbox360 -> SteamDeck still succeeds
8. exit BPM
   → SteamDeck -> Xbox360 still succeeds

9. inspect debug log volume
   → no hundreds of generic "Controller ancestry resolved" lines
   → no repeated "ControllerState changed. M1=False->False M2=False->False" MsiInput spam
```

Also confirm no new ERROR/FATAL entries and no regression in:

```text
PhysicalIsolationVerified
Physical ownership acquired
PresentationSwitchCompleted
SetStateFailures=0
```

Do not introduce timing assertions for Xbox360 <-> SteamDeck switching in this cleanup PR.

---

# 11. Acceptance criteria

- [ ] Healthy Center M Disabled Full1902 operation reports `AddonStatus=Ready` after physical ownership + active presentation are positively established.
- [ ] `ControllerEnvironmentCompatibility` remains unchanged; `MsiCenterMNotOperational` is still valid/expected for the legacy stock compatibility fact while Center M is Disabled.
- [ ] No new persisted Full1902/authority status flag exists.
- [ ] No new authority manager/state machine/poller exists.
- [ ] Full1902 status is derived only from existing process-owned facts.
- [ ] Non-healthy/incomplete Full1902 states are not falsely reported Ready.
- [ ] `ControllerTopologySnapshot.ResolveAncestors()` behavior is unchanged.
- [ ] Generic per-device `Controller ancestry resolved` flood is removed.
- [ ] Dedicated MSI M1/M2 transition logs remain.
- [ ] Redundant generic `MsiInput` false->false state-change spam is removed.
- [ ] Overlay request under stock authority remains a safe no-op but is no longer WARN.
- [ ] Missing Full1902 source/presentation while Disabled can still surface as WARN.
- [ ] No PID/PnP/HidHide/DirectInput/VIIPER/presentation/recovery behavior changes.
- [ ] Existing Full1902 lifecycle tests remain green.
- [ ] Full test suite passes in Debug and Release as required by repository CI.

---

# 12. Review guidance

Review this PR as a **status/observability correction**, not as a new controller architecture round.

Block the PR if it:

- changes the working Full1902 lifecycle to solve a logging issue;
- weakens a real safety boundary;
- makes stock compatibility lie about Center M state;
- reports Full1902 Ready without a running physical input source and active presentation;
- adds a second controller-authority source;
- adds generalized retry/locking/state machinery without a real product failure path.

Do not block it for theoretical instruction-level races in status capture.

A status snapshot is observational. If ownership changes immediately after the read, the existing controller owner/reconcile path remains authoritative. Do not add epochs, barriers, or locks solely to make multiple observational fields mathematically atomic.

The production question is only:

> Does the status path stop reporting a known false failure during normal healthy Full1902 operation, while preserving fail-close behavior and the existing controller lifecycle?

If yes, keep the implementation simple.
