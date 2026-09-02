# Work Order — Full1902 Policy A2: Decouple OEM1/WING Front-Button Actions and Disable Legacy Physical Routing

## Status

Corrective prerequisite work order after merged PR #468 (`SteamInputRoutingEnabled` removal).

PR #468 removed the routing preference, but the merged tree still contains the old production `AddonRoutingRuntime` selection path. During review two real blockers were identified:

1. removing the preference gate while leaving `AddonRoutingRuntime` selectable allows Center M Enabled / stock authority to enter the legacy PID1902 physical routing path when Steam/BPM becomes active;
2. simply forcing `routingRuntime = null` also removes the only production wiring for OEM1/WING action paths because `AddonRoutingRuntime.Create(...)` currently owns the calls to `ConfigureOem1ActionPath(...)` and `ConfigureWingActionPath(...)`.

This work order resolves both blockers together.

It must land **before**:

- `FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md`;
- any final OEM1/WING button-assignment redesign;
- PR13 Windows / Velopack uninstall-entry integration.

This work order is intentionally **not PR13**.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     e87ac911908c11a730cea512b1696f432b51bd9e
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_A_REMOVE_STEAM_INPUT_ROUTING_MASTER_SWITCH_WORK_ORDER.md`
- `docs/work-order/PR6_FIRST_VIRTUAL_PRESENTATION_ATTACH_WORK_ORDER.md`
- `docs/work-order/PR7_RUNTIME_XBOX360_STEAMDECK_PRESENTATION_SWITCHING_WORK_ORDER.md`
- `docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md` only as the **next-step dependency**, not as scope for this PR.

Also read the full PR #468 review discussion. The two `[BLOCKER]` findings there are the direct reason this work order exists.

---

# 1. Product invariant to restore

Full1902 has exactly two controller-authority modes:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired physical PID = PID1901
→ no Addon DirectInput ownership
→ no Addon controller HidHide ownership
→ no Addon VIIPER controller presentation
→ Steam/BPM MUST NOT trigger legacy PID1902 routing

Center M Disabled
→ Addon controller authority
→ desired physical PID = PID1902
→ Full1902 physical owner active
→ one Full1902 VIIPER presentation active
→ Steam/BPM only selects Xbox360 vs SteamDeck presentation
```

Therefore production must no longer have a third path where:

```text
Center M Enabled
+ Steam/BPM active
→ old AddonRoutingRuntime
→ legacy PID1902 takeover
```

The final target for the old physical routing runtime is:

```csharp
AddonRoutingRuntime? routingRuntime = null;
```

in normal production composition.

But this is only safe **after** OEM1/WING front-button action wiring is no longer owned by `AddonRoutingRuntime.Create(...)`.

---

# 2. Current code problem

## 2.1 `AddonRuntimeCompositionFactory` still selects the legacy runtime

Current file:

```text
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
```

Current logic still does both of these:

```csharp
if (legacyRoutingAllowed)
    steamRuntime.StartRoutingObservation();
```

and:

```csharp
var routingRuntime = legacyRoutingAllowed
    ? AddonRoutingRuntime.Create(...)
    : null;
```

`legacyRoutingAllowed` is effectively the Center M Enabled / stock-authority branch.

After PR #468, `EffectiveSteamSessionSource` no longer has `SteamInputRoutingEnabled` as a user gate. A normal Steam game, BPM activation, or Developer Test can therefore produce an effective active session and drive the legacy runtime.

This is a reachable production authority violation, not a theoretical race.

## 2.2 `AddonRoutingRuntime.Create(...)` owns unrelated front-button wiring

Current file:

```text
src/SteamInputAddonforClaw/Routing/AddonRoutingRuntime.cs
```

Besides constructing the old physical routing/output graph, it also uniquely calls:

```csharp
runtime.Oem1ActivationTask = handheldRoutingComposition.ConfigureOem1ActionPath(...);
_ = handheldRoutingComposition.ConfigureWingActionPath(...);
```

Those calls currently receive route-owned primitives:

```text
OEM1:
  captureRoutingStatus      = AddonRoutingRuntime.CaptureStatus
  requestQuickAccessPulse   = CanonicalSteamDeckOutputStage.RequestQuickAccessPulse

WING:
  captureAuthority          = WinGProtectionRoutingStage.CaptureAuthority
  tryRequestSteamPulse      = CanonicalSteamDeckOutputStage.TryRequestSteamPulse
```

This is the coupling that prevented the first #468 blocker fix from simply setting `routingRuntime = null`.

## 2.3 Do not reproduce the coupling in a new wrapper

Do **not** solve this by creating a new `FrontButtonActionRuntime` that internally recreates:

- `HandheldRoutingCompositionFactory`;
- legacy physical routing stages;
- `CanonicalSteamDeckOutputStage`;
- a second `CanonicalViiperRuntime`;
- a second SteamDeck virtual device just to obtain Steam/QAM pulse primitives.

That would technically move the call site while preserving the same architectural violation.

Center M Enabled must not create an Addon VIIPER controller presentation just so a front button can emit a Steam/QAM pulse.

---

# 3. Required implementation shape

Use the already-existing Full1902 owners as the only controller/presentation source of truth.

The preferred structure is:

```text
AddonProcessHost
├─ AddonRuntimeHost                     // Steam/BPM facts, profiles, etc.
├─ Full1902 physical owner              // Disabled only
├─ MsiClawAddonPresentation             // Disabled only, one VIIPER runtime
└─ narrow front-button action owner     // feature-local WMI/gesture/dispatch only

Legacy AddonRoutingRuntime
└─ not production-composed
```

The front-button owner must **not** become a new controller authority.

It may own only feature-local items such as:

- Event41 / Event88 WMI observation;
- OEM1/WING gesture recognizers;
- mapping preference subscriptions;
- OEM1/WING action dispatchers;
- the already-required short gesture epoch/lifetime fact if stale double-click delivery must still be discarded.

It must not own:

- PID1901/PID1902 switching;
- DirectInput;
- HidHide;
- VIIPER server/bus/device creation;
- Steam/BPM observation;
- controller recovery;
- Center M startup authority.

Keep one clear owner and one teardown path. Do not add a generic `ButtonManager`, `AuthorityService`, `ActionBroker`, or cross-device abstraction just for this extraction.

---

# 4. Reuse the Full1902 SteamDeck publisher for Steam/QAM pulses

The current Full1902 presentation owner is:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
```

It already creates the production `CanonicalSteamDeckInputPublisher`, but unlike the legacy `CanonicalSteamDeckOutputStage`, it does not currently expose the existing `SteamDeckSystemButtonOverlay` pulse seam.

The existing pulse primitive is already suitable:

```text
SteamDeckSystemButtonOverlay
```

It is explicitly output-only and merges Steam/QuickAccess pulses into the **existing continuous SteamDeck publish path**. It does not create another publication path or call VIIPER directly.

## 4.1 Add one overlay instance to the Full1902 presentation owner

Preferred concept:

```csharp
private readonly SteamDeckSystemButtonOverlay _systemButtonOverlay = new();
```

The production Deck publisher created by `MsiClawAddonPresentation` should receive this same overlay instance:

```csharp
new CanonicalSteamDeckInputPublisher(
    source,
    sink,
    fault: fault,
    systemButtonOverlay: _systemButtonOverlay)
```

Adjust the test publisher factory seam only as much as necessary. Do not introduce another publisher abstraction if the current `IAddonPresentationPublisher` can remain sufficient.

## 4.2 Expose narrow availability-aware pulse methods

Add narrow internal methods on `IMsiClawAddonPresentation` / `MsiClawAddonPresentation`, conceptually:

```csharp
bool TryRequestSteamPulse();
bool TryRequestQuickAccessPulse();
```

Exact names are not mandated.

They must return `false` unless the current presentation is a healthy live SteamDeck publication.

Minimum eligibility:

```text
owner not disposed
active presentation == SteamDeck
publisher exists and is running
SteamDeck session == Active
Overlay capture is not holding publication paused
```

If eligible:

```text
TryRequestSteamPulse
→ _systemButtonOverlay.RequestSteamPulse()

TryRequestQuickAccessPulse
→ _systemButtonOverlay.RequestQuickAccessPulse()
```

No attach/detach, no VIIPER recreation, no PID mutation, no retry loop.

When the active presentation is Xbox360 or absent, pulse requests simply report unavailable.

## 4.3 Clear pulse state on presentation retirement

Any path that retires the active SteamDeck presentation must clear the shared overlay before/while the publisher is retired so an old synthetic button cannot survive into a later new Deck publisher.

Reuse the existing `SteamDeckSystemButtonOverlay.Clear()`.

Do not add another pulse queue, timestamp authority, or epoch.

---

# 5. Front-button mapping domain after legacy routing removal

The current OEM1 mapping contract already says Normal/Routing is a **mapping domain**, not controller authority.

Current capability semantics are:

```text
Normal domain
→ Steam Big Picture available
→ Steam Quick Access unavailable

Routing domain
→ Steam Quick Access available
→ Steam Big Picture unavailable
```

After legacy physical routing is gone, the source for that domain must no longer be `RoutingRuntimeStatusSnapshot.SteamOutputActive` from `AddonRoutingRuntime`.

For Full1902 Addon authority use the actual presentation:

```text
active Full1902 presentation == SteamDeck
→ Routing mapping domain

active Full1902 presentation == Xbox360 / none
→ Normal mapping domain
```

This preserves the existing user-facing mapping model without reviving the legacy routing owner.

Do not rename the persisted Normal/Routing slots in this corrective PR.

A later front-button UX cleanup may rename/redesign the mapping model if desired.

---

# 6. Center M Enabled policy for this corrective PR

Center M Enabled means MSI / stock controller authority.

Therefore:

```text
Center M Enabled
→ do not create Full1902 physical owner
→ do not create Full1902 presentation
→ do not create a VIIPER pulse-only presentation
→ do not start legacy AddonRoutingRuntime
```

A Steam/QAM synthetic controller pulse is unavailable in this state because no Addon SteamDeck presentation exists.

Do not work around that by creating a hidden/detached virtual controller solely for the buttons.

This work order does **not** finalize the long-term user-facing OEM1/WING behavior while Center M is Enabled. It only requires that stock controller authority remain stock-safe.

Do not expand this corrective PR into a new stock-mode button policy.

---

# 7. Center M Disabled / Addon authority front-button lifetime

The front-button action owner belongs to the already-selected Addon-authority runtime path.

Preferred composition point is `AddonProcessHost`, after Full1902 startup has established the necessary live objects.

Conceptual ordering:

```text
Center M exactly Disabled
→ Full1902 admission
→ physical PID1902 ownership + HidHide verified
→ Full1902 presentation owner created
→ first Xbox360/SteamDeck presentation attached
→ create/start feature-local OEM1/WING action path against that existing presentation owner
```

If implementation can safely create the WMI/gesture owner slightly earlier, that is acceptable, but it must not dispatch Steam/QAM pulses before a valid presentation exists.

Do not make front-button startup a prerequisite for PID1902 acquisition or VIIPER attach.

A button-feature failure is feature-local unless it directly compromises a safety invariant.

---

# 8. OEM1 extraction boundary

Current OEM1 Event41 / gesture / dispatcher logic is embedded in `MsiClawRoutingComposition` because it historically shared the old Center M helper/suppression route lifetime.

For this work order extract the **front-button action path** needed by Full1902 from that routing composition.

At minimum the new feature-local owner should reuse, not duplicate:

- `WmiMsiEventSource`;
- `Oem1GestureRecognizer`;
- `Oem1EventGestureBridge`;
- `Oem1ActionDispatcher`;
- `IOem1MappingPreference`;
- `Oem1ActionCapabilities` / existing persisted mapping records.

Do not fork a second OEM1 mapping implementation.

## 8.1 Do not carry legacy physical-routing dependencies

The extracted path must not require:

```text
MsiClawNativeModeStage
MsiClawPhysicalInputStage
MsiClawPhysicalIsolationStage
CenterMMainUiRoutingGuardStage
RoutingPipelineRuntimeCoordinator
AddonRoutingRuntime.CaptureStatus
CanonicalSteamDeckOutputStage
```

Use the Full1902 presentation fact/pulse methods instead.

## 8.2 Legacy Center M helper suppression cleanup is not the main goal here

Do not turn this corrective PR into a broad deletion of every old OEM1 helper class.

However, the new Full1902 front-button path must **not** instantiate the legacy dummy `MSI Center M.exe` helper solely because the old `MsiClawRoutingComposition` did so.

Center M Disabled already owns the controller and its startup roots are disabled; the new Addon-authority action path does not need a fake Center M process to justify custom button delivery.

The now-dead legacy helper/suppression classes may remain for a separate deletion PR if removing them here would excessively widen the diff.

No new compatibility wrapper around them.

---

# 9. WING extraction boundary

Reuse the existing:

- `WmiMsiEventSource` for Event88;
- `WingGestureRecognizer`;
- `WingEventGestureBridge`;
- `WingActionDispatcher`;
- `IWingMappingPreference`.

Remove the production dependency on:

```text
WinGProtectionRoutingStage.CaptureAuthority
CanonicalSteamDeckOutputStage.TryRequestSteamPulse
```

The Steam-button action must use the Full1902 presentation owner's `TryRequestSteamPulse()` seam.

## 9.1 Do not invent a second WING controller authority

If the existing `WingRouteAuthoritySnapshot` epoch is still required to reject a delayed double-click after the front-button lifetime has been revoked, move/reuse that small lifecycle fact inside the new feature owner.

Do not create a separate global `WingAuthorityManager`.

The active decision must derive from the existing process/controller lifetime, not a persisted setting.

## 9.2 Policy B remains the next WING safety step

This work order is the prerequisite extraction.

`FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md` remains responsible for moving native Win+G suppression to the Full1902 Addon-authority lifetime.

Do not reintroduce route-scoped suppression as part of this extraction.

If WING custom action delivery cannot safely be made live before Policy B because native Win+G would also surface, keep that WING delivery gated by the existing suppression readiness and let Policy B activate the final authority-bound path. Do not create an unsafe interim double-action behavior just to satisfy a test.

The important result of this PR is that WING wiring no longer requires `AddonRoutingRuntime`.

---

# 10. Remove production legacy physical routing selection

After the front-button dependency is removed, change `AddonRuntimeCompositionFactory` so production no longer creates `AddonRoutingRuntime`.

Target concept:

```csharp
steamRuntime.StartActualObservation();

AddonRoutingRuntime? routingRuntime = null;
```

There must be no production branch that calls:

```csharp
AddonRoutingRuntime.Create(...)
```

based on Center M Enabled, Steam/BPM, Developer Test, or a hidden replacement preference.

Do not replace it with:

```text
AlwaysEnabledRoutingPreference
AlwaysDisabledRoutingPreference
LegacyRoutingEnabled = false
new hidden setting
new feature flag
```

Delete the selection, not just the old user switch.

The old classes may remain temporarily unreferenced if deleting the entire legacy pipeline would make this PR too large.

---

# 11. Preserve stock PID1901 resume baseline independently

One part of the old `legacyRoutingAllowed` branch is still valid: Center M Enabled must retain stock PID1901 baseline verification/recovery on resume.

Do not remove that safety behavior when disabling legacy routing.

Separate the concepts.

Preferred parameter rename:

```text
legacyRoutingAllowed
→ stockCenterMAuthority
```

or another name that clearly means **Center M stock authority**, not permission to run the old Steam route.

Target concept:

```csharp
Func<CancellationToken, Task<bool>> establishBaseline =
    stockCenterMAuthority && stockCenterMBaseline is not null
        ? async token =>
            (await stockCenterMBaseline.EstablishAsync(token).ConfigureAwait(false)).Succeeded
        : _ => Task.FromResult(!stockCenterMAuthority);
```

Exact syntax is not mandated.

Required behavior:

```text
Center M Enabled
→ stock PID1901 resume baseline still runs

Center M Disabled
→ do not run stock PID1901 resume baseline
```

Do not reuse the same boolean to decide whether legacy physical routing exists.

---

# 12. Steam/BPM observation after legacy routing removal

`SteamSessionRuntime` still has important non-legacy consumers:

- actual RunningAppID for Device/Profile features;
- raw Steam/BPM facts for Full1902 X360 ↔ SteamDeck presentation;
- QAM / frontend state.

Therefore keep observation alive.

Production should use the non-routing observation path required by those consumers.

Do not remove:

```text
ActualRunningAppId
ActualRunningAppIdChanged
BigPictureStateChanged
CapturePresentationSnapshot
SteamPresentationSnapshot.WantsSteamDeck
```

Do not let Developer Test become a Full1902 presentation input.

The Full1902 presentation rule remains exactly:

```text
RunningAppID != 0 OR BigPictureActive
→ SteamDeck
else
→ Xbox360
```

---

# 13. `AddonRuntimeComposition` cleanup

Once `AddonRoutingRuntime` is no longer responsible for front-button activation, review this transitional member:

```csharp
Task Oem1ActivationTask
```

If it has no remaining production consumer, remove it from `AddonRuntimeComposition` rather than returning `Task.CompletedTask` forever.

Do the same for comments/tests whose only purpose was ordering OEM1 helper activation ahead of the legacy routing helper-acquisition boundary.

Do not retain dead lifecycle members for hypothetical compatibility.

If some OEM1 activation task remains necessary inside the new feature-local owner, that owner should own and join it directly.

---

# 14. Teardown / lifecycle requirements

The extracted front-button owner must have one clear teardown path.

On controlled Runtime shutdown / authority release:

```text
stop accepting new button events
→ dispose gesture recognizers/bridges/event sources
→ join any owned feature-local async work if required
→ no callbacks into a disposed presentation owner
```

Order it before `_presentationOwnership` is finally disposed if the button owner still holds pulse callbacks to that presentation.

Do not add a new shutdown coordinator.

Use the existing `AddonProcessHost` teardown ordering.

Real supported lifecycle cases to preserve:

- sleep / hibernate / resume;
- process restart / update restart;
- shutdown;
- physical device loss / PnP recovery;
- explicit Enable Center M + Restart;
- uninstall stock-safe preparation.

Do not build extra locks/epochs for arbitrary instruction-level interleavings.

---

# 15. Tests — mandatory regression coverage

## 15.1 Blocker #1: Center M Enabled can never enter legacy physical routing

Add production-composition regression tests for at least:

```text
Center M Enabled
→ Steam RunningAppID becomes non-zero
→ no AddonRoutingRuntime
→ no routing reconcile
→ no PID1902 takeover mutation

Center M Enabled
→ BPM becomes active
→ no AddonRoutingRuntime
→ no routing reconcile
→ no PID1902 takeover mutation
```

Developer Test should also not resurrect the old physical route.

A useful structural assertion is allowed:

```text
AddonRuntimeCompositionFactory production source contains no AddonRoutingRuntime.Create(...)
```

but do not rely only on source-string tests; keep at least one behavior-level regression test.

## 15.2 Stock resume safety remains

Test:

```text
Center M Enabled + resume
→ StockCenterMStartupBaseline invoked

Center M Disabled + resume
→ StockCenterMStartupBaseline not invoked
```

## 15.3 Front-button wiring no longer depends on `AddonRoutingRuntime`

Add a production-lifetime test proving supported MSI Claw Full1902 startup can construct/configure the front-button action owner while:

```text
routingRuntime == null
```

The test must prove the feature path exists independently, not merely that settings records still serialize.

## 15.4 Full1902 presentation pulse tests

Extend `MsiClawAddonPresentationTests` or focused publisher tests to prove:

```text
SteamDeck live
→ TryRequestSteamPulse == true
→ next publisher state contains Steam pulse

SteamDeck live
→ TryRequestQuickAccessPulse == true
→ next publisher state contains QuickAccess pulse

Xbox360 active
→ both pulse methods return false

presentation absent / retired / publisher stopped
→ false

Overlay capture paused
→ false

SteamDeck retirement
→ pending pulse state cleared
```

Use existing deterministic `SteamDeckSystemButtonOverlay` test seams; no sleeps.

## 15.5 OEM1 domain selection without routing status

Prove:

```text
Full1902 Xbox360
→ Normal mapping slot

Full1902 SteamDeck
→ Routing mapping slot
```

No `RoutingRuntimeStatusSnapshot` or `AddonRoutingRuntime.CaptureStatus` should be required for that decision.

## 15.6 WING action dependency removed

Prove the WING action wiring no longer requires:

```text
WinGProtectionRoutingStage
AddonRoutingRuntime
CanonicalSteamDeckOutputStage
```

and that its SteamButton action delegates to the existing Full1902 presentation pulse seam when eligible.

Do not require native Game Bar suppression behavior in this PR; that belongs to Policy B.

---

# 16. Code-search acceptance checks

After implementation, verify production code under `src/` satisfies all of these:

```text
AddonRuntimeCompositionFactory
→ no production AddonRoutingRuntime.Create(...)

AddonRoutingRuntime.Create(...)
→ no longer the only production owner of OEM1/WING action wiring

OEM1 Full1902 action path
→ no AddonRoutingRuntime.CaptureStatus dependency
→ no CanonicalSteamDeckOutputStage dependency

WING Full1902 action path
→ no WinGProtectionRoutingStage.CaptureAuthority dependency
→ no CanonicalSteamDeckOutputStage.TryRequestSteamPulse dependency

Center M Enabled
→ no path from Steam/BPM observation to legacy PID1902 physical mutation
```

If old legacy code remains in `src/`, it may still contain historical methods/classes, but it must be unreachable from normal production composition.

Do not delete useful historical tests solely to make grep clean unless the production contract they tested is intentionally gone.

---

# 17. Explicit out of scope

Do not expand this PR into:

- final WING Single/Double button assignment;
- deciding whether WING ultimately opens Steam vs Overlay;
- full native Win+G suppression lifetime migration — next Policy B;
- broad deletion of all OEM1 dummy/helper suppression code;
- UI redesign of Normal/Routing slot names;
- M1/M2 Xbox360 remapping;
- rumble / vibration-strength changes;
- battery charge limit;
- PID1902 ownership redesign;
- HidHide policy changes;
- DirectInput recovery redesign;
- VIIPER ownership redesign;
- crash supervisor/service/watchdog;
- PR13 uninstall interception;
- Fast User Switching / RDP / multi-session support.

Do not introduce abstractions for unsupported environments or theoretical races.

---

# 18. Build / validation

Required before PR completion:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Run the repository's normal full solution/project commands used by CI.

Manual MSI Claw smoke if hardware is available:

```text
1. Center M Enabled boot
   - controller remains PID1901
   - start Steam game
   - enter/exit BPM
   - verify no PID1902 legacy takeover

2. Center M Disabled boot
   - normal Full1902 PID1902 ownership
   - Xbox360 when Steam/BPM inactive
   - SteamDeck when Steam/BPM active
   - OEM1/WING front-button runtime can be composed without AddonRoutingRuntime

3. SteamDeck presentation
   - synthetic Steam/QAM pulse uses the existing publisher
   - no second VIIPER device/runtime created

4. X360 ↔ SteamDeck switch
   - front-button mapping domain follows actual presentation
   - physical PID/HidHide stay unchanged
```

If hardware is unavailable, report that clearly; automated regression coverage is still mandatory.

---

# 19. Completion criteria

This work order is complete only when all are true:

- PR #468 blocker #1 is closed: Center M Enabled cannot enter legacy physical routing from Steam/BPM/Developer Test;
- PR #468 blocker #2 is closed: OEM1/WING front-button wiring no longer depends on `AddonRoutingRuntime.Create(...)`;
- production `routingRuntime` is always `null` / old physical runtime is not created;
- stock PID1901 resume baseline remains intact and independently gated;
- Full1902 Steam/QAM synthetic pulses reuse the existing `MsiClawAddonPresentation` SteamDeck publisher;
- no second VIIPER server/bus/device exists for front buttons;
- OEM1 Normal/Routing domain is derived from actual Full1902 Xbox360/SteamDeck presentation rather than legacy routing status;
- WING SteamButton uses the Full1902 presentation pulse seam, not `CanonicalSteamDeckOutputStage`;
- no new controller authority/state manager/service/watchdog was introduced;
- full build/tests pass;
- Policy B can subsequently move Win+G suppression to Addon authority without depending on the legacy route.

---

# 20. Review focus

Review this PR primarily for realistic product failures:

- Center M Enabled accidentally switching to PID1902;
- front-button settings becoming dead because the only wiring was removed;
- accidental creation of a second VIIPER presentation;
- stale synthetic Steam/QAM pulse leaking across presentation retirement;
- stock resume baseline regression;
- teardown calling into disposed presentation objects;
- real suspend/resume/restart/PnP regressions.

Do **not** block on hypothetical instruction-level races with no realistic handheld lifecycle path. Preserve simple ownership and teardown semantics.
