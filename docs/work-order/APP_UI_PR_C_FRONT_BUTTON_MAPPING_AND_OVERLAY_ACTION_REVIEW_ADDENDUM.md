# Review Addendum — App UI PR-C: Front-Button Mapping + Quick Settings Overlay Action

> **Date:** 2026-09-04  
> **Status:** Authoritative review addendum for implementation  
> **Applies to:** `docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md`  
> **Current main after the original work-order commit:** `16e449064cec9d08e3c50491152266ce854dd961`  
> **Original reviewed code baseline remains:** `e255b44b4ed4d0bfcb68a3cfde71dee07f424f0b`

This addendum resolves the implementation-review findings raised after the original PR-C work order was written.

Where this addendum conflicts with the original PR-C work order, **this addendum wins**.

---

## 1. Correction: Full1902 Policy B is already implemented on current `main`

Do **not** treat `FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md` as an unimplemented prerequisite.

Policy B was already merged by:

```text
6bcc57e5e29020e8d242e36371c2611531f0bb45
Full1902 Policy B: bind WING / Game Bar suppression to Addon controller authority (#473)
```

Current product behavior is already:

```text
Center M Disabled / Addon controller authority
→ WinGSuppressionGuard is installed from the pumping message-loop path
→ Disabled-mode startup arms and proves Win+G suppression before the first live presentation
→ MsiClawFrontButtonRuntime WING delivery authority reads the existing guard
→ stock-authority restoration disarms only at the already-defined verified release boundary
```

Production wiring is already conceptually:

```csharp
nativeWinGSuppressionReady: () => _winGSuppressionGuard.IsArmed
```

Therefore PR-C does **not** need to invent or open a new WING authority gate.

The intended PR-C result is that the new default:

```text
Normal.Gamebar Button = Quick Settings Overlay
```

is immediately executable whenever the existing Full1902 Policy-B Addon-authority preconditions are satisfied.

### 1.1 Stale source comment must be corrected

`MsiClawFrontButtonRuntime.Create(...)` still contains historical wording that says production currently passes an always-false WING suppression callback.

That statement predates merged Policy B and is stale.

PR-C must update/remove that stale comment while touching the same composition surface so source documentation matches current production reality.

Do not change the actual Policy-B authority lifecycle merely to fix the comment.

### 1.2 Preserve Policy-B fail-closed behavior

PR-C must preserve all existing Policy-B boundaries:

```text
Win+G suppression not proven
→ no live Addon presentation commit
→ no Gamebar/WING custom action delivery

Win+G suppression drops while Addon authority remains active
→ existing presentation reconcile remains fail-closed

verified stock authority restored
→ existing onStockAuthorityRestored boundary may disarm suppression
```

Do not move arm/disarm ownership into the new button mapping model.

Do not add a second `WINGEnabled`, `GamebarAuthority`, `ButtonSuppressionReady`, or equivalent state.

---

## 2. Explicit Runtime seam wiring required by PR-C

The original work order correctly requires reuse of existing semantic execution seams, but the construction/wiring work must be explicit.

`MsiClawFrontButtonRuntime.Create(...)` is the composition boundary for the Event41/Event88 action paths and must be extended so both physical-button paths can execute the complete action vocabulary allowed by the new shared mapping contract.

### 2.1 Add coordinated Overlay toggle seam

Extend `MsiClawFrontButtonRuntime.Create(...)` with one narrow callback from `AddonProcessHost`, conceptually:

```csharp
Action requestOverlayToggle
```

or an equivalently narrow delegate.

Production `AddonProcessHost` must pass the existing Runtime-owned coordinated Overlay toggle path, not the lower-level Overlay process controller.

The host method may be renamed from the POC-era name:

```text
ToggleOverlayForPoc()
```

to a product-semantic name such as:

```text
RequestOverlayToggle()
```

but it must continue to enter:

```text
CoordinateOverlayToggleAsync()
```

and therefore preserve:

```text
Main UI ↔ Overlay visible-surface ordering
Overlay Show/Hide acknowledgement
controller capture
neutral virtual publication
release-to-resume gate
physical-input-loss handling
```

A button dispatcher must **not** call:

```text
OverlayProcessController.ShowAsync()
OverlayProcessController.ToggleForPocAsync()
```

directly.

### 2.2 Both button dispatch paths need all action seams they can legally resolve

The final capability table allows the following semantic actions across the two domains:

```text
QuickSettingsOverlay
SteamBigPicture
SteamButton
SteamQuickAccess
KeyboardHotkey
LaunchApplication
```

Therefore the runtime action execution layer must have access to the existing concrete seams for all six actions.

Current production seams already include:

```text
isSteamDeckPresentationActive
tryRequestSteamPulse
tryRequestQuickAccessPulse
Big Picture launcher
keyboard/hotkey executor
application launcher
```

PR-C adds:

```text
coordinated Overlay toggle callback
```

### 2.3 Current dispatcher asymmetry must be removed

Today:

```text
WingActionDispatcher
→ primarily receives trySteam + existing custom action helpers

Oem1ActionDispatcher
→ receives Quick Access + Big Picture + existing custom action helpers
```

That is insufficient for the PR-C capability model.

Required post-PR-C behavior:

```text
Gamebar physical path
→ can execute every action supported in the currently resolved domain

Center M physical path
→ can execute every action supported in the currently resolved domain
```

In particular:

```text
Gamebar / Normal
→ SteamBigPicture must be executable

Gamebar / Steam
→ SteamQuickAccess must be executable

Center M / Steam
→ SteamButton must be executable

both buttons / both domains
→ QuickSettingsOverlay must be executable
```

Implementation choice:

- extend the two existing dispatchers with the missing narrow callbacks; **or**
- replace their duplicated action-execution switch with one small shared front-button action executor if that clearly reduces duplication.

Do **not** introduce a manager/state machine/authority object merely to share the switch.

Regardless of implementation shape, `MsiClawFrontButtonRuntime.Create(...)` must thread the Overlay toggle and both Steam system-button pulse seams to the action path(s) that require them.

---

## 3. Product decisions are confirmed — do not reopen them during implementation

The following two items are intentional product decisions already confirmed for PR-C.

### 3.1 Uniqueness compares semantic action only, not payload

Within the same domain:

```text
Gamebar.Action != CenterM.Action
```

This remains true even when action-specific payload differs.

These are intentionally invalid:

```text
Gamebar = Keyboard / Hotkey (Ctrl+F1)
Center M = Keyboard / Hotkey (Alt+F2)
```

and:

```text
Gamebar = Launch Application (A.exe)
Center M = Launch Application (B.exe)
```

Do not relax uniqueness to `(Action + payload)` equality.

Do not silently swap or mutate the partner button to make a conflicting request valid.

### 3.2 Normal Gamebar default intentionally changes from the old WING behavior

Old pre-PR-C WING persistence has no Normal/Steam domain and defaults:

```text
Single = SteamButton
```

PR-C intentionally changes the final product default to:

```text
Normal.Gamebar = QuickSettingsOverlay
Steam.Gamebar  = SteamButton
```

Therefore Normal/Xbox360/desktop behavior changes by design.

This is acceptable because the product is pre-release and the PR-C persistence migration explicitly resets obsolete button-mapping schema to the new frozen defaults rather than preserving ambiguous legacy semantics.

Update relevant current documentation so no active doc continues to imply that WING always defaults to Steam Button regardless of presentation.

---

## 4. Clarify the shared binding record

The conceptual contract in the original §6 must be read as:

```csharp
public sealed record FrontButtonBinding(
    FrontButtonAction Action,
    FrontButtonHotkeyBinding Hotkey,
    FrontButtonLaunchApplicationBinding Launch);
```

Exact syntax is implementation choice, but all three semantic fields are required:

```text
Action
Hotkey
Launch
```

`Hotkey` and `Launch` must be non-null value objects even when their action is not currently selected, preserving the existing behavior where switching actions does not discard previously entered configuration.

Do not use nullable payloads as an alternate implicit `None` representation.

---

## 5. Gesture/slot cleanup — explicit source targets

The original work order removes persisted/user Single/Double slot semantics but intentionally does not require a broad recognizer rewrite.

The implementation must explicitly inspect and update:

```text
src/SteamInputAddonforClaw/CenterM/Oem1MappingSlots.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
src/SteamInputAddonforClaw/CenterM/Oem1GestureRecognizer.cs
src/SteamInputAddonforClaw/Wing/WingGestureRecognizer.cs
```

### 5.1 `Oem1MappingSlots.cs`

The old NormalSingle/NormalDouble/RoutingSingle/RoutingDouble resolver belongs to the superseded persistence model.

Delete it if no current caller remains after the shared front-button mapping conversion.

Do not preserve a slot abstraction whose only purpose is translating the deleted four-slot OEM1 schema.

### 5.2 Production recognizer policy

Both front buttons now have one action per physical press per domain.

Production wiring must therefore disable Double recognition unconditionally:

```csharp
doubleClickEnabled: false
```

or the corresponding constructor/delegate form for each recognizer.

Specifically, remove current production mapping-dependent logic that checks deleted Double slots, including the OEM1 lambda that resolves `NormalDouble` / `RoutingDouble` and the WING lambda that reads `WingMapping.Double`.

A press must be delivered immediately as the single physical-button action; it must not incur the old 200 ms delay waiting for a Double mapping that no longer exists.

The low-level recognizer implementation may retain dormant Double-support code if deleting it would unnecessarily broaden this PR.

---

## 6. Existing hotkey/application executors — avoid cosmetic churn

Moving the persisted binding DTOs to the shared `FrontButtons` contracts namespace does **not** by itself require broad renaming of runtime executor classes.

Existing implementation types such as:

```text
Oem1KeyboardHotkeyExecutor
Oem1ApplicationLauncher
Oem1BigPictureLauncher
```

may keep their current names if they are still the smallest safe reusable runtime implementation and renaming them would be cosmetic churn.

Rename/move an executor only when required to remove a real compile-time dependency on deleted contract types or when the old name would materially misrepresent ownership after the shared action path is complete.

Do not turn PR-C into a broad namespace/class naming cleanup.

---

## 7. Frontend availability consolidation must be verified

Current bootstrap exposes two feature flags:

```text
Oem1MappingAvailable
WingMappingAvailable
```

PR-C should replace these with one user-facing capability fact:

```text
FrontButtonMappingAvailable
```

Before deleting the two booleans, verify in current production composition that both are derived from the same supported-MSI-Claw hardware fact.

The intended post-PR-C rule is:

```text
supported MSI Claw startup hardware fact
→ FrontButtonMappingAvailable = true

unsupported / indeterminate non-supported hardware
→ FrontButtonMappingAvailable = false
```

Do not derive availability from:

```text
Steam/BPM state
current presentation
Overlay readiness
Win+G suppression armed state
individual Event41/Event88 transient observation success
```

Those are runtime execution/lifecycle facts, not whether the Controller mapping feature belongs to this hardware.

Add/update tests proving the old two bootstrap booleans do not survive and the new single availability fact is sourced from the same supported-hardware authority.

---

## 8. Settings validation and migration clarification

The one atomic `FrontButtonMappingSettings` object remains the persistence authority.

Validation must prove all of the following before a mapping can become current:

```text
all four bindings non-null
all action values known
action allowed in that binding's domain
Normal.Gamebar.Action != Normal.CenterM.Action
Steam.Gamebar.Action  != Steam.CenterM.Action
hotkey/launch payload objects non-null
```

A malformed/obsolete pre-release `Oem1Mapping` / `WingMapping` pair is not partially merged into the new shape.

Migration remains intentionally simple:

```text
new FrontButtonMapping absent
→ use frozen PR-C defaults

new FrontButtonMapping present and valid
→ use it

new FrontButtonMapping present but invalid
→ use frozen PR-C defaults for this feature
```

On the next settings save, obsolete `Oem1Mapping` and `WingMapping` properties disappear.

Do not build a schema-version migration framework for this pre-release settings change.

---

## 9. Required current-source comment/doc cleanup

PR-C must update stale active documentation/comments that contradict the final behavior.

At minimum inspect:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
docs/VIIPER_MIGRATION_TODO.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
```

Required corrections include:

```text
- Policy B is already production-active; WING delivery is not intentionally always-false.
- user-facing name is Gamebar Button, not WING.
- Normal Gamebar default is Quick Settings Overlay.
- Steam Game / Big Picture Gamebar default is Steam Button.
- Center M defaults are Steam Big Picture / Steam Quick Access by domain.
- no user/persisted None or Double mapping remains.
```

Historical work orders may remain historical unless their current-status section is actively misleading implementation.

---

## 10. Additions to the original “Likely Files” list

Ensure implementation review covers at least these source surfaces in addition to the original list:

```text
src/SteamInputAddonforClaw/CenterM/Oem1MappingSlots.cs
src/SteamInputAddonforClaw/CenterM/Oem1GestureRecognizer.cs
src/SteamInputAddonforClaw/Wing/WingGestureRecognizer.cs
src/SteamInputAddonforClaw/Wing/WingActionDispatcher.cs
src/SteamInputAddonforClaw/CenterM/Oem1ActionDispatcher.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

This does not require all listed files to survive after the PR. Some are deletion/replacement targets.

---

## 11. Additional required tests

Add to the original PR-C test requirements:

### 11.1 Policy-B production wiring regression

Prove current production composition still binds WING authority to:

```text
_winGSuppressionGuard.IsArmed
```

and PR-C does not replace it with a constant or mapping setting.

Also remove/update any stale source assertion/comment that still claims production always passes false.

### 11.2 Complete action-seam reachability

For each physical path, cover the actions newly reachable through the shared mapping model.

At minimum:

```text
Gamebar Normal → QuickSettingsOverlay
Gamebar Normal → SteamBigPicture
Gamebar Steam  → SteamButton
Gamebar Steam  → SteamQuickAccess

CenterM Normal → QuickSettingsOverlay
CenterM Normal → SteamBigPicture
CenterM Steam  → QuickSettingsOverlay
CenterM Steam  → SteamButton
CenterM Steam  → SteamQuickAccess
```

Hotkey/application execution should remain covered for both physical paths through the shared action executor or equivalent dispatcher tests.

### 11.3 Overlay seam ownership

Prove front-button `QuickSettingsOverlay` reaches the host's coordinated overlay-toggle seam and does not call the lower-level `OverlayProcessController` directly.

Do not duplicate the entire OQ4 Overlay lifecycle suite in front-button tests; one narrow wiring test plus the existing Overlay tests is sufficient.

### 11.4 No deleted Double-slot dependency in production composition

Guard that production `MsiClawFrontButtonRuntime.Create(...)` no longer reads:

```text
NormalDouble
RoutingDouble
WingMapping.Double
```

to decide whether a physical press is delayed.

### 11.5 Availability consolidation

Prove:

```text
one FrontButtonMappingAvailable bootstrap fact
```

and no remaining frontend contract properties named:

```text
Oem1MappingAvailable
WingMappingAvailable
```

when the new protocol is complete.

---

## 12. Acceptance-criteria amendments

The original acceptance criteria remain, with these explicit additions/corrections:

1. Full1902 Policy B is treated as **already merged production behavior**, not a future prerequisite.
2. `nativeWinGSuppressionReady` remains bound to the existing `WinGSuppressionGuard.IsArmed` lifecycle fact.
3. The stale “production always false” WING comment is removed/corrected.
4. `MsiClawFrontButtonRuntime.Create(...)` receives/threads the coordinated Overlay toggle seam required by both button paths.
5. Both physical button action paths can execute every action their current domain capability table permits, including Center M → Steam Button and Gamebar → Steam Quick Access / Steam Big Picture.
6. `Oem1MappingSlots.cs` and mapping-dependent Double-enable wiring are deleted/replaced as appropriate.
7. Physical-button presses are immediate; deleted Double mappings do not retain an unnecessary 200 ms delay.
8. Payload-independent same-domain action uniqueness remains enforced exactly as specified.
9. The Normal Gamebar default change from old domainless `SteamButton` to `QuickSettingsOverlay` is documented as intentional pre-release behavior.
10. Shared `FrontButtonBinding` carries non-null Action + Hotkey + Launch values.
11. Executor class renames are not required unless technically necessary; avoid cosmetic churn.
12. Frontend mapping availability is one supported-hardware-derived fact after the contract migration.

---

## 13. Final implementation rule after review

The PR-C implementation should now be read as:

> **Keep the already-merged Full1902 Policy-B Win+G authority exactly where it is, replace the obsolete split WING/OEM1 persistence with one four-binding Normal/Steam mapping, and thread the existing Big Picture / Steam / Quick Access / hotkey / application / coordinated-Overlay execution seams through the one current front-button runtime without creating a second authority or manager.**
