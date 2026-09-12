# SF-V2-08 — Shared Profile Projection/Dispatch + QAM Generic Profile Migration

> **Date:** 2026-09-12  
> **Status:** Implementation work order  
> **Reviewed production baseline:** `main` at `5bb4cc66e4e933a8e50fe0fb21875e6cbb5bad6e` after PR #508 / SF-V2-07  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **Roadmap authority:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Full 1902 authority:** `docs/Full 1902 Implementation/`  
> **Previous milestone:** SF-V2-07 complete — Device shared-frontend milestone closed  
> **Next milestone:** SF-V2-09 — Overlay Profile publication + generic binding

---

## 1. Goal

Finish the **QAM Profile half** of Shared Frontend V2 by moving the currently visible Steam QAM Profile product definition into the same shared Quick Settings model already used by Device.

The target ownership is:

```text
FrontendGameProfileSnapshot
        ↓
QuickSettingsPresentation.BuildProfile(...)
        ↓
QuickSettingsPageSnapshot(Profile)
        ↓
QuickSettingsMutationAdapter
        ↓
existing typed Game Profile mutation methods
        ↓
QAM generic Quick Settings renderer
```

After this PR:

```text
Device QAM product definition  = shared Quick Settings
Profile QAM product definition = shared Quick Settings
```

The remaining SF-V2-09 work should then be able to publish/render that exact Profile page in Overlay without inventing a second Profile UI definition.

This PR is **not** a Profile redesign.

It must preserve the currently intended visible Profile product while removing duplicated QAM-owned labels, option tables, debounce policy, TDP gap policy, feature-specific mutation names, and Profile-specific pending-draft machinery.

---

## 2. Non-negotiable architecture

Keep the existing authority hierarchy:

```text
Runtime feature/profile persistence = state + mutation authority
Shared Quick Settings projection     = product-definition authority
QAM                                  = Steam renderer + QAM lifecycle/admission
Overlay                              = separate renderer/lifecycle; untouched in this PR
Main UI                              = independent management UI; untouched in this PR
```

Do **not** create:

- a second Profile store;
- a Profile ViewModel framework;
- a generic feature registry;
- a reflection dispatcher;
- a page manager/service;
- a new scheduler service;
- a lifecycle epoch/barrier system;
- a cross-surface state authority;
- polling.

Use the existing shared page/value/mutation contract and the existing typed Game Profile operations.

---

## 3. Full 1902 / lifecycle constraints

This is frontend/product-projection work. It must not change controller ownership or Full 1902 lifecycle behavior.

Production diff must not alter:

```text
PID1901 ↔ PID1902 ownership
HidHide ownership/recovery
VIIPER ownership/teardown
physical input ownership
SteamDeck/Xbox360 presentation switching
OQ4 Overlay capture ownership
Sleep / Hibernate / Resume handling
Restart / Crash / Shutdown recovery
PnP re-enumeration recovery
routing rollback / fail-close policy
```

Do not add synchronization merely for theoretical interleavings.

The realistic lifecycle safety requirement in this PR is much narrower:

```text
active game A Profile page/draft
→ active game changes to B or exits
→ no old A mutation may target B
→ no late A result may replace B/Device UI
```

Protect that with the existing explicit AppId identity and current-target validation. Do not invent epochs.

---

## 4. Reviewed baseline

### 4.1 Current protocol versions

Current source at the reviewed baseline:

```text
FrontendTransportProtocol.CurrentVersion = 28
OverlayTransportProtocol.CurrentVersion  = 7
```

SF-V2-08 should require **no protocol bump** because SF-V2-03 already reserved the Profile page/section/row/group identities and v28 already carries:

```text
QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
PageId
AppId
```

Do not bump a protocol merely because previously reserved Profile enum values become active.

If implementation discovers that a genuinely new serialized field/control kind is unavoidable, stop and reassess before changing the wire. Do not silently smuggle a new schema through v28/v7.

### 4.2 Existing reserved Profile identities

`QuickSettingsContracts.cs` already reserves:

```text
QuickSettingsPageId.Profile

QuickSettingsSectionId.ProfileGeneral
QuickSettingsSectionId.ProfileTdp
QuickSettingsSectionId.ProfileCpuBoost
QuickSettingsSectionId.ProfilePowerMode

QuickSettingsRowId.ProfileEnabled
QuickSettingsRowId.ProfileTdpEnabled
QuickSettingsRowId.ProfileTdpAcPl1
QuickSettingsRowId.ProfileTdpAcPl2
QuickSettingsRowId.ProfileTdpDcPl1
QuickSettingsRowId.ProfileTdpDcPl2
QuickSettingsRowId.ProfileCpuBoostEnabled
QuickSettingsRowId.ProfileCpuBoostAc
QuickSettingsRowId.ProfileCpuBoostDc
QuickSettingsRowId.ProfilePowerModeEnabled
QuickSettingsRowId.ProfilePowerModeAc
QuickSettingsRowId.ProfilePowerModeDc

QuickSettingsCommitGroupId.ProfileTdpConfiguration
```

Use these exact identities.

Do not renumber existing enum members.

Do not add FPS/Resolution identities in this PR.

### 4.3 Existing generic QAM seam

Current QAM already exposes:

```text
captureQuickSettingsPage
mutateQuickSetting
```

through `QamFrontendBridge`, but generic mutation is currently Device-only.

Current Device renderer is already shared-model-driven and must remain behaviorally unchanged.

### 4.4 Existing Profile backend capability

Current typed frontend/runtime operations already include:

```text
CaptureActiveGameProfileAsync
CaptureGameProfileAsync
SetGameProfileEnabledAsync
SetGameProfileCpuBoostEnabledAsync
SetGameProfileCpuBoostAcAsync
SetGameProfileCpuBoostDcAsync
SetGameProfileTdpEnabledAsync
SetGameProfileTdpAsync
SetGameProfilePowerModeEnabledAsync
SetGameProfilePowerModeAcAsync
SetGameProfilePowerModeDcAsync
```

Backend capability also exists for:

```text
Intel FPS Limit
Resolution
Favorites
```

but backend capability is **not** permission to expose it in shared Quick Settings.

---

## 5. Freeze the actual visible Profile product

The current `qam.js` was re-reviewed at the SF-V2-08 baseline.

### 5.1 Visible current order

The current visible Profile page is:

```text
[Game title]
  Profile

TDP area
  TDP Control
  Plugged in · PL1        (when TDP feature enabled)
  Plugged in · PL2        (when TDP feature enabled)
  On battery · PL1        (when TDP feature enabled)
  On battery · PL2        (when TDP feature enabled)

CPU Boost area
  CPU Boost
  Plugged in              (when CPU Boost feature enabled)
  On battery              (when CPU Boost feature enabled)

Windows Power Mode area   (only when PowerMode capability exists)
  Windows Power Mode
  Plugged in              (when Power Mode feature enabled)
  On battery              (when Power Mode feature enabled)
```

The Profile master toggle is independent from the per-feature enable flags:

```text
ProfileEnabled = false
```

must not erase the saved TDP/CPU/Power feature settings. It only makes those subfeature rows non-writable while the Profile is disabled.

### 5.2 Hidden Intel FPS Limit is NOT in scope

Current source still has:

```javascript
const SHOW_INTEL_FPS_LIMIT = false;
```

Therefore Intel FPS Limit is **not** part of the visible SF-V2-08 Profile product.

Do not add shared Quick Settings FPS rows merely because these backend methods exist:

```text
SetGameProfileFpsLimitEnabledAsync
SetGameProfileFpsLimitAcAsync
SetGameProfileFpsLimitDcAsync
```

### 5.3 Resolution is NOT in scope

`FrontendGameProfileSnapshot` contains `Resolution`, but current QAM Profile does not render Resolution.

Do not add a Resolution row/control kind in this PR.

### 5.4 Hidden-QAM FPS cleanup

Once the visible Profile path has migrated, remove the unreachable QAM-only hidden FPS rendering branch if it has no remaining product use:

```text
SHOW_INTEL_FPS_LIMIT
fpsControls
Profile FPS local draft/render helpers
setActiveGameFpsLimit* QAM bridge string methods/calls
```

This is safe because the path is currently hard-disabled and is specifically excluded from the SF-V2-08 visible product.

Do **not** delete the typed frontend/runtime FPS capability from:

```text
IAddonFrontendControl
NamedPipeAddonFrontendClient
Frontend RPC enum/wire
GameProfileMutations
IntelFrameLimiterRuntime
Main UI/Profile backend
```

A future product decision can expose FPS through a focused shared-contract change.

The same rule applies to Resolution: leave backend capability intact.

---

## 6. Target shared Profile page

Add:

```csharp
QuickSettingsPresentation.BuildProfile(FrontendGameProfileSnapshot snapshot)
```

as a pure/stateless projection.

It must:

- read no hardware directly;
- persist nothing;
- mutate nothing;
- scan no games itself;
- subscribe to nothing;
- own no cache;
- depend only on the supplied snapshot.

### 6.1 Page identity

For a valid active target:

```text
PageId    = Profile
AppId     = snapshot.AppId
Available = true
```

`AppId` is mandatory product context for a writable Profile page.

### 6.2 General section

Use:

```text
SectionId = ProfileGeneral
Label     = snapshot.DisplayName when non-empty
            otherwise "Game {AppId}"
```

Rows:

```text
ProfileEnabled
  Label         = "Profile"
  Control       = Toggle
  Value         = snapshot.Enabled
  Available     = true for a valid active Profile target
  Writable      = snapshot.PersistenceWritable
  CommitPolicy  = Immediate
  CommitGroupId = null
```

`Exists == false` is not the same as unavailable.

A running game with no saved Profile must still render the Profile toggle OFF so the user can create/enable it when persistence is writable.

### 6.3 TDP section

Only project the visible TDP product when `snapshot.Limits` exists, matching the current QAM policy.

Use:

```text
SectionId = ProfileTdp
Label     = null
```

The section contains:

```text
ProfileTdpEnabled
  Label        = "TDP Control"
  Toggle
  Value        = snapshot.Tdp.Enabled
  Writable     = snapshot.PersistenceWritable && snapshot.Enabled
  Immediate
```

When the saved TDP feature is enabled, also include exactly:

```text
ProfileTdpAcPl1   "Plugged in · PL1"
ProfileTdpAcPl2   "Plugged in · PL2"
ProfileTdpDcPl1   "On battery · PL1"
ProfileTdpDcPl2   "On battery · PL2"
```

All four sliders:

```text
ControlKind   = Slider
SliderKind    = Numeric
Step          = 1
CommitPolicy  = TrailingDebounce(2000 ms)
CommitGroupId = ProfileTdpConfiguration
Writable      = snapshot.PersistenceWritable && snapshot.Enabled
```

Use the real TDP limits:

```text
PL1 min = Limits.Pl1MinimumWatts
PL1 max = Limits.Pl1MaximumWatts
PL2 min = Limits.Pl2MinimumWatts
PL2 max = Limits.Pl2MaximumWatts
```

Do not preserve the legacy QAM implementation detail where the PL1 Steam slider was constructed with `pl2MaximumWatts` and then locally clamped in `onChange`.

The actual existing product policy is the true PL1 limit. The shared projection must express that directly, exactly as Device now does.

For parity with the current Profile QAM presentation, do not introduce a new visible unit suffix solely in this migration. If the current Profile slider displays plain numeric watts through Steam's own slider value presentation, keep the shared Profile suffix null/empty. A later shared UI polish can change both surfaces once.

### 6.4 Profile TDP linked constraints

Reuse the one known Claw gap policy already owned by `QuickSettingsPresentation`:

```text
limits 8..30 / 8..37 → minimum gap 1
limits 8..35 / 8..45 → minimum gap 2
other limit shapes    → no shared linked constraint
```

Emit Profile IDs:

```text
ProfileTdpAcPl1 ↔ ProfileTdpAcPl2
ProfileTdpDcPl1 ↔ ProfileTdpDcPl2
```

Do not copy the tuple switch into a second Profile-only function if a small shared helper can serve both Device and Profile.

Preferred shape:

```csharp
private static int GetKnownTdpGap(FrontendTdpLimits limits) => ...;
```

with Device/Profile each creating constraints from their own row IDs.

Do not add a general TDP policy service.

### 6.5 CPU Boost section

Use:

```text
SectionId = ProfileCpuBoost
Label     = null
```

Rows:

```text
ProfileCpuBoostEnabled
  Label     = "CPU Boost"
  Toggle
  Value     = snapshot.CpuBoost.Enabled
  Writable  = snapshot.PersistenceWritable && snapshot.Enabled
  Immediate
```

When the saved CPU Boost feature is enabled, include:

```text
ProfileCpuBoostAc   "Plugged in"
ProfileCpuBoostDc   "On battery"
```

Both are discrete sliders using the **existing shared** `CpuBoostDiscreteOptions`:

```text
0 Disabled
1 Enabled
2 Aggressive
3 Efficient Enabled
4 Efficient Aggressive
5 Aggressive At Guaranteed
6 Efficient Aggressive At Guaranteed
```

Policy:

```text
TrailingDebounce(2000 ms)
no commit group
Writable = snapshot.PersistenceWritable && snapshot.Enabled
```

Do not leave a second `modes`/Profile option label table in `qam.js` after migration.

### 6.6 Windows Power Mode section

Only include this section when:

```text
snapshot.PowerMode != null
```

matching current QAM behavior.

Use:

```text
SectionId = ProfilePowerMode
Label     = null
```

Rows:

```text
ProfilePowerModeEnabled
  Label     = "Windows Power Mode"
  Toggle
  Value     = snapshot.PowerMode.Enabled
  Writable  = snapshot.PersistenceWritable && snapshot.Enabled
  Immediate
```

When the saved Power Mode feature is enabled, include:

```text
ProfilePowerModeAc   "Plugged in"
ProfilePowerModeDc   "On battery"
```

Reuse the existing shared `PowerModeDiscreteOptions`:

```text
0 Best power efficiency
1 Balanced
2 Best performance
```

Policy:

```text
TrailingDebounce(2000 ms)
no commit group
Writable = snapshot.PersistenceWritable && snapshot.Enabled
```

Remove Profile-only copies of Power Mode labels/names from `qam.js` when no longer used.

---

## 7. Profile capture semantics

Activate the already-existing generic seam:

```csharp
CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, appId)
```

### 7.1 Do not trust an arbitrary requested AppId

A Profile Quick Settings page is the **current active-game** product.

For a Profile capture request:

```text
requested AppId missing / 0
→ unavailable Profile page

requested AppId != current active game AppId
→ unavailable Profile page for the requested context
→ zero mutation / zero wrong-game projection

requested AppId == current active game AppId
→ capture current active FrontendGameProfileSnapshot
→ BuildProfile(snapshot)
```

Use the existing active-game authority. Do not scan/process-match in QAM JavaScript.

### 7.2 Preserve the current display-name behavior

Current `CaptureActiveGameProfileAsync` enriches a missing persisted display name from the existing game catalog scan.

The generic page migration must not regress a known active game title to a raw `Game 12345` label merely because qam.js stopped calling `captureActiveGameProfile` directly.

The simplest approved path is to reuse `CaptureActiveGameProfileAsync` inside the Runtime-side Profile page capture, then validate that its `AppId` still matches the requested context before projecting.

This preserves current behavior without adding a second scanner or scan cache.

Do not move game scanning into QAM or Overlay.

### 7.3 No active game

No active game is explicit, not a fake Device/Profile hybrid:

```text
Profile capture
→ QuickSettingsPageSnapshot.Unavailable(Profile, ...)
→ message such as "No active game."
```

QAM page selection still chooses Device when there is no active game, so this state is primarily needed for fail-closed capture semantics and later SF-V2-09 Overlay publication.

---

## 8. Central Profile mutation dispatch

Extend `QuickSettingsMutationAdapter` from Device-only to:

```text
Device
Profile
```

Keep one explicit switch. No reflection, no method-name strings, no registry.

### 8.1 Mandatory stale-target validation

Before **any** Profile mutation:

1. require `intent.AppId` to exist and be non-zero;
2. capture the current active Profile target using the existing Runtime/frontend authority;
3. require:

```text
active.AppId == intent.AppId
```

If not:

```text
Succeeded = false
zero typed Profile mutations
Page = unavailable/stale Profile page for the submitted AppId
FailureMessage = clear active-game-changed/unavailable message
```

This is the central protection SF-V2-09 Overlay will later reuse.

Do not rely only on QAM JavaScript or QAM bridge checks for wrong-AppId safety.

### 8.2 Validate against current projected writability

After current-target validation, build the current Profile page and locate `EditedRowId`.

Before dispatching a Profile subfeature intent, require the projected row to exist and be:

```text
Available == true
Writable  == true
```

This is important for real product transitions such as:

```text
Profile was enabled
→ delayed CPU/TDP/Power draft exists
→ user disables Profile
→ old delayed draft reaches Runtime afterward
```

Once the Profile is disabled, subfeature rows project non-writable. The central adapter must fail that stale draft closed instead of changing disabled Profile configuration behind the user's just-completed action.

This removes the need for a new cross-section cancellation protocol field/state machine.

### 8.3 Dispatch matrix

Use the existing typed methods exactly:

| Shared row | Typed mutation |
|---|---|
| `ProfileEnabled` | `SetGameProfileEnabledAsync(appId, enabled, displayName, ...)` |
| `ProfileTdpEnabled` | `SetGameProfileTdpEnabledAsync(appId, enabled, ...)` |
| Profile TDP four sliders | `SetGameProfileTdpAsync(appId, configuration, ...)` |
| `ProfileCpuBoostEnabled` | `SetGameProfileCpuBoostEnabledAsync(appId, enabled, ...)` |
| `ProfileCpuBoostAc` | `SetGameProfileCpuBoostAcAsync(appId, mode, ...)` |
| `ProfileCpuBoostDc` | `SetGameProfileCpuBoostDcAsync(appId, mode, ...)` |
| `ProfilePowerModeEnabled` | `SetGameProfilePowerModeEnabledAsync(appId, enabled, ...)` |
| `ProfilePowerModeAc` | `SetGameProfilePowerModeAcAsync(appId, mode, ...)` |
| `ProfilePowerModeDc` | `SetGameProfilePowerModeDcAsync(appId, mode, ...)` |

Do not call persistence/runtime implementations directly from the generic adapter.

### 8.4 Profile master toggle display name

`QuickSettingsMutationIntent` intentionally does not carry a duplicated display-name field.

For `ProfileEnabled`, use the display name from the already-validated active Profile snapshot:

```csharp
await control.SetGameProfileEnabledAsync(
    appId,
    enabled,
    activeProfile.DisplayName,
    cancellationToken);
```

Do not add `DisplayName` to the generic mutation wire solely for this row.

### 8.5 Independent Toggle/Slider validation

Independent intent rules remain strict:

```text
EditedRowId matches the one Values entry
Values.Count == 1
Boolean row → structurally valid Boolean
Discrete row → structurally valid Integer and valid enum member
no unrelated row values
```

Use the existing shared validation helpers where practical.

### 8.6 Profile TDP group validation

A Profile TDP slider commit must contain exactly one each of:

```text
ProfileTdpEnabled = Boolean true
ProfileTdpAcPl1   = Integer
ProfileTdpAcPl2   = Integer
ProfileTdpDcPl1   = Integer
ProfileTdpDcPl2   = Integer
```

Requirements:

```text
Values.Count == 5
no duplicates
no missing members
no unrelated rows
EditedRowId is one of the four Profile TDP slider rows
ProfileTdpEnabled must be true
```

Construct:

```csharp
new FrontendGameTdpConfiguration(
    true,
    new FrontendTdpPowerPair(acPl1, acPl2),
    new FrontendTdpPowerPair(dcPl1, dcPl2))
```

Then call `SetGameProfileTdpAsync` exactly once.

A small parameterized TDP-group validation helper shared with Device is acceptable if it makes the existing validation clearer.

Do not introduce a generic mutation registry.

### 8.7 Mutation result page

For a typed Profile mutation result, use its returned authoritative `FrontendGameProfileSnapshot` to build the generic result page:

```text
QuickSettingsMutationResult
  Succeeded      = typed result.Succeeded
  FailureMessage = typed result.FailureMessage
  Page           = BuildProfile(typed result.Snapshot)
```

A typed `ApplyFailed`/`PersistenceFailed` result is still a **typed result**, not a transport exception.

The returned snapshot/page remains authoritative even on failure.

If the active game changed while the operation was already in flight, the typed operation still targets its explicit original AppId; it must never retarget the new game. The surface must reject/ignore the old page by context identity once its visible context changed.

---

## 9. QAM bridge migration

### 9.1 Generic seam becomes Device + Profile

Keep only:

```text
captureQuickSettingsPage
mutateQuickSetting
```

for QAM Quick Settings product operations.

`CaptureQuickSettingsPageAsync` already accepts `PageId` + optional `AppId`; no new bridge method is needed.

### 9.2 Device admission remains exactly unchanged

Keep the proven Device rule:

```text
Steam.Active
AND Steam.AppId == 0
AND Steam.Source == BigPicture
```

Do not weaken or relocate it.

### 9.3 Profile generic mutation

Remove the current bridge rule that rejects every non-Device generic intent.

Allow `QuickSettingsPageId.Profile` through the generic seam.

Do **not** duplicate the Runtime adapter's AppId/current-target validation in a second complex QAM validator.

The bridge should remain surface-scope only:

```text
Device  → existing Device QAM admission
Profile → generic transport allowed; central adapter proves current AppId/row validity
other/unknown page → reject
```

### 9.4 Remove legacy Profile QAM allowlist methods

Once `qam.js` has zero callsites, remove the feature-specific QAM string operations:

```text
captureActiveGameProfile
setActiveGameProfileEnabled
setActiveGameCpuBoostEnabled
setActiveGameCpuBoostAc
setActiveGameCpuBoostDc
setActiveGameTdp
setActiveGameTdpEnabled
setActiveGamePowerModeEnabled
setActiveGamePowerModeAc
setActiveGamePowerModeDc
```

If hidden FPS QAM UI is removed as required above, also remove from **QamFrontendBridge only**:

```text
setActiveGameFpsLimitEnabled
setActiveGameFpsLimitAc
setActiveGameFpsLimitDc
```

Then remove now-dead bridge helpers such as:

```text
ActiveMutationAsync
DecodePowerMode
DeviceProfiles using
```

only if they truly have no remaining use.

Do not remove the typed `NamedPipeAddonFrontendClient` Profile methods. Main UI and other product code still use the typed frontend contract.

---

## 10. QAM refresh/page selection

Replace the current dual legacy refresh:

```text
captureStatus
captureActiveGameProfile
[maybe capture Device shared page]
```

with:

```text
captureStatus
        ↓
status.steam.appId == 0
    → captureQuickSettingsPage(Device, null)

status.steam.appId > 0
    → captureQuickSettingsPage(Profile, status.steam.appId)
```

The QAM still owns the surface choice:

```text
no active game → Device shared page
active game    → Profile shared page
```

Do not move this page-selection policy into `QuickSettingsPresentation`.

### 10.1 One current shared page state is preferred

The clean target in `qam.js` is one current generic page state/ref, conceptually:

```javascript
const [quickSettingsPage, setQuickSettingsPage] = React.useState(null);
const quickSettingsPageRef = React.useRef(null);
```

instead of maintaining separate product-definition state machines for:

```text
devicePage
legacy profile object + Profile-only drafts/previews
```

Exact naming may differ.

Do not rewrite unrelated QAM installation/Steam component discovery.

### 10.2 Context identity

Treat page context as:

```text
(PageId, AppId)
```

Device context:

```text
(Device, null)
```

Profile context:

```text
(Profile, activeAppId)
```

A context change must retire unsubmitted pending work from the previous context.

Examples:

```text
Device → Profile(480)
Profile(480) → Profile(570)
Profile(480) → Device
```

must not retain the previous context's delayed drafts.

---

## 11. Generalize the existing QAM Quick Settings renderer — do not build a second renderer

SF-V2-05 already created a metadata-driven Device renderer.

Refactor the smallest amount needed so the same logic renders Profile.

Conceptual renames/generalization are expected around existing Device helpers:

```text
deviceQuickSettingsPendingKey
→ quickSettingsPendingKey(page, row)

deviceQuickSettingsPendingValue
→ quickSettingsPendingValue(page, rowId)

applyDeviceQuickSettingsLinkedConstraints
→ applyQuickSettingsLinkedConstraints(page, values, editedRowId)

commitDeviceImmediate
→ commitQuickSettingsImmediate

scheduleDeviceQuickSettingsCommit
→ scheduleQuickSettingsCommit

renderDeviceQuickSettingsRow
→ renderQuickSettingsRow
```

Do not mechanically rename code that does not need to become shared.

The goal is one actual Toggle/Slider renderer for both pages, not an abstract UI framework.

### 11.1 Shared native control mapping

Continue using only the existing Steam native controls:

```text
Toggle → native.ToggleField
Slider → native.SliderField
Section → native.PanelSection / PanelSectionRow
```

No custom Profile DOM/UI implementation.

### 11.2 Discrete values remain product values, never option indexes

The generic renderer must continue the SF-V2-05 rule:

```text
option.Value = product value
array index   = only Steam SliderField presentation position
```

Profile CPU Boost/Power Mode must work even if a future option set is non-contiguous.

Do not reconstruct enum labels in JS.

---

## 12. Generic pending-draft identity

Current Device pending keys contain Device-specific prefixes/state fields.

Generalize them so Device and Profile drafts are isolated by page context.

Recommended identity:

```text
PageId
AppId
RowId OR CommitGroupId
```

Conceptually:

```text
qs-page:{pageId}:app:{appId-or-none}:row:{rowId}
qs-page:{pageId}:app:{appId-or-none}:group:{groupId}
```

The exact string is not product state. It is a QAM-local scheduler key.

Each pending entry should carry enough typed context to answer:

```text
which PageId?
which AppId?
which SectionId?
which current row/group values?
```

Do not use product labels as keys.

### 12.1 Profile TDP group

All four Profile TDP sliders:

```text
ProfileTdpAcPl1
ProfileTdpAcPl2
ProfileTdpDcPl1
ProfileTdpDcPl2
```

must resolve to one:

```text
ProfileTdpConfiguration
```

pending draft.

First edit seeds the whole containing Profile TDP section from authoritative/effective values.

Subsequent edits reuse that draft and restart the shared trailing delay.

Linked companion correction must be visible immediately before Runtime settlement.

---

## 13. Pending draft vs authoritative refresh

Preserve the proven SF-V2-05 rule:

```text
same page context + pending writable row/group
→ pending local draft wins visually

unrelated row
→ newest authoritative value wins
```

A normal `StateInvalidated` must not erase a legitimate pending draft.

### 13.1 Generic fail-closed pruning

A fresh authoritative page **may** retire a pending entry when that entry is no longer valid in the same product context, for example:

```text
edited row disappeared
edited row became unavailable
edited row became non-writable
page/AppId context changed
```

This is the generic way to handle real parent-state transitions such as:

```text
ProfileEnabled OFF
→ CPU/TDP/Power rows become non-writable
→ old unsubmitted child drafts are retired
```

Do not clear all pending work on every invalidation.

Do not add cross-section dependency metadata or a new cancellation-policy protocol field solely for this PR.

The Runtime adapter's projected-writability check remains the final fail-closed backstop if a timer reaches Runtime after the page changed.

---

## 14. Immediate Toggle behavior

Immediate Toggle rows still follow shared metadata:

```text
CommitPolicy.Mode = Immediate
```

Before an immediate mutation, preserve the existing generic same-section rule:

```text
retire still-unsubmitted pending slider/group work in that same section
```

Examples:

```text
ProfileTdpEnabled OFF
→ pending ProfileTdpConfiguration timer retired

ProfileCpuBoostEnabled OFF
→ pending ProfileCpuBoostAc/Dc timers retired

ProfilePowerModeEnabled OFF
→ pending ProfilePowerModeAc/Dc timers retired
```

For the overall `ProfileEnabled` toggle, do not invent a new framework solely to encode cross-section cancellation.

Safety is provided by:

1. current-page refresh/result making subfeature rows non-writable;
2. generic pending pruning against the new page;
3. central adapter validating current projected row writability before dispatch.

This is sufficient to prevent a stale child commit from changing a disabled Profile after the master toggle settles.

---

## 15. Mutation busy / invalidation ordering

Preserve the existing QAM mutation gate introduced/fixed during SF-V2-05.

The generic path must still use:

```text
beginMutation()
endMutation()
deferredInvalidationRef
```

for both Device and Profile generic mutations.

Reason:

```text
typed Runtime mutation
→ Runtime may raise StateInvalidated before its response returns
→ premature refresh must not erase the current typed failure/result settlement
```

The current mutation result page wins first.

After settlement, any deferred invalidation can refresh again if necessary.

Do not create a second Profile-specific busy gate.

---

## 16. Late result / active-game change rule

A Profile mutation is always tied to its submitted AppId.

Before applying a mutation result to QAM state, compare result page context with the currently visible shared page context.

Required behavior:

```text
Profile(480) mutation submitted
→ active game becomes 570
→ QAM refresh installs Profile(570)
→ late Profile(480) result arrives
→ ignore it for visible state
→ never replace Profile(570)
```

A simple `(PageId, AppId)` equality check is sufficient.

Do not add an epoch counter solely for this.

Existing scheduler generation/token handling still protects newer edits inside the **same** page context.

---

## 17. Remove legacy Profile product authority from qam.js

After migration, the visible Profile path must no longer own copies of:

```text
PROFILE_SLIDER_COMMIT_DELAY_MS
legacyProfileAdjustTdpPair
Profile TDP label/range policy
CPU Boost mode label table
Power Mode label/name table
profileTdpControls
profileCpuControls
profilePowerControls
Profile feature-specific mutation method names
profile-specific slider pending keys
profileTdpDraft/profileTdpDraftRef product draft authority
previewAc / previewDc Profile CPU draft authority
profile-* Power Mode preview authority
```

Delete only state/helpers proven dead after the generic page is wired.

Keep shared renderer utilities such as `labelRow` if the generic renderer still uses them.

Do not touch Steam webpack/component discovery, QAM patch ownership, teardown, or native component resolution.

---

## 18. Product error behavior

Preserve current user-visible fail-closed behavior:

### Typed feature failure

```text
QuickSettingsMutationResult.Succeeded = false
+ FailureMessage
+ authoritative result Page
```

→ show failure locally  
→ apply the returned page as authority when its context still matches.

### Operation/transport failure

Do not fabricate a successful/authoritative page.

Use the existing QAM `failClosed`/refresh path.

### Stale AppId

```text
zero wrong-game mutation
clear/retire obsolete pending context
refresh current page
```

Do not silently retarget the user's action to the new game.

---

## 19. No protocol changes

Expected:

```text
FrontendTransportProtocol = 28 unchanged
OverlayTransportProtocol  = 7 unchanged
```

Reason:

- `Profile` page identity already exists;
- all visible Profile row IDs already exist;
- `ProfileTdpConfiguration` group already exists;
- current Quick Settings page/value/mutation shape already carries AppId;
- Toggle/Numeric Slider/Discrete Slider already cover the visible product.

Update historical comments from "reserved"/"later" to implemented where appropriate, but do not change enum numeric order.

---

## 20. Production file scope

Expected production changes are focused in:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
  - comment/status cleanup only; no required wire-shape change

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
  - BuildProfile
  - shared TDP gap helper reuse

src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
  - Profile current-target validation
  - Profile explicit dispatch
  - Profile TDP group validation

src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
  - activate generic Profile page capture

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
  - allow generic Profile mutation
  - remove obsolete legacy Profile QAM string operations/helpers

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
  - one Device/Profile generic page renderer
  - context-aware pending identity
  - remove visible legacy Profile product definition
  - remove unreachable hidden FPS QAM branch
```

Potentially no production changes should be needed in:

```text
FrontendWire.cs
NamedPipeAddonFrontendClient.cs
NamedPipeAddonFrontendServer.cs
OverlayWire.cs
SteamInputAddonforClaw.Overlay/*
OverlayProcessController.cs
AddonProcessHost.cs
Main UI ProfilePage
GameProfileMutations
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
```

If implementation begins changing those areas, stop and prove why the SF-V2-08 scope requires it.

---

## 21. Required tests — shared Profile projection

Extend `QuickSettingsPresentationTests` with focused Profile cases.

At minimum prove:

### 21.1 Exact visible page

For a representative enabled profile with all visible capabilities:

```text
ProfileGeneral
  ProfileEnabled
ProfileTdp
  ProfileTdpEnabled
  ProfileTdpAcPl1
  ProfileTdpAcPl2
  ProfileTdpDcPl1
  ProfileTdpDcPl2
ProfileCpuBoost
  ProfileCpuBoostEnabled
  ProfileCpuBoostAc
  ProfileCpuBoostDc
ProfilePowerMode
  ProfilePowerModeEnabled
  ProfilePowerModeAc
  ProfilePowerModeDc
```

No FPS rows.

No Resolution row.

### 21.2 Exact labels/order

Assert exact current visible labels/order.

### 21.3 Header context

Assert:

```text
DisplayName present → General section label == DisplayName
DisplayName blank   → General section label == "Game {AppId}"
Page.AppId          == snapshot.AppId
```

### 21.4 Profile disabled

When overall Profile is disabled:

- Profile master row remains writable if persistence is writable;
- saved subfeature toggles/children may remain visible according to their saved feature enable state;
- all subfeature rows are non-writable.

### 21.5 TDP capability

- no limits → no visible TDP controls/section according to final chosen representation;
- PL1 uses true PL1 min/max;
- PL2 uses true PL2 min/max;
- step 1;
- all four sliders share `ProfileTdpConfiguration`;
- gap-one limits emit Profile AC/DC gap 1;
- gap-two limits emit Profile AC/DC gap 2;
- unknown limits emit no shared gap.

### 21.6 CPU Boost options

Assert exact seven values/labels and ordering.

### 21.7 Power Mode options

Assert exact three values/labels and ordering.

### 21.8 Optional Power Mode capability

`PowerMode == null` must not fabricate Power Mode rows.

---

## 22. Required tests — central mutation adapter

Extend `QuickSettingsMutationAdapterTests`.

At minimum:

### Target identity

- Profile AppId null → zero mutation;
- Profile AppId 0 → zero mutation;
- stale AppId → zero mutation;
- current AppId → eligible for dispatch.

### Master toggle

- `ProfileEnabled` Boolean dispatches exactly once to `SetGameProfileEnabledAsync`;
- the active snapshot display name is forwarded;
- malformed value invokes zero typed mutation.

### Feature toggles

Each of:

```text
ProfileTdpEnabled
ProfileCpuBoostEnabled
ProfilePowerModeEnabled
```

maps to exactly one matching typed method.

### Independent sliders

Each CPU/Power side maps to exactly one matching typed method and rejects undefined enum integers.

### TDP group

- exact five-entry group accepted;
- missing enable rejected;
- enable=false rejected;
- duplicate row rejected;
- unrelated row rejected;
- missing one PL row rejected;
- edited row outside the four TDP sliders rejected;
- valid group calls `SetGameProfileTdpAsync` exactly once.

### Current projected writability

Prove:

```text
Profile currently disabled
→ stale subfeature intent
→ zero typed mutation
```

Also prove a feature-specific slider cannot mutate when its current projected row is absent/non-writable.

### Result authority

For success and typed failure:

```text
result.Page.PageId == Profile
result.Page.AppId  == target AppId
page is built from typed result.Snapshot
```

Device adapter tests must remain green unchanged.

---

## 23. Required tests — generic Profile capture

Add/extend focused `InProcessAddonFrontendControl` Quick Settings tests.

Prove:

- current active AppId + matching requested AppId → Profile page;
- requested stale AppId → unavailable page;
- no active game → unavailable Profile page;
- display name enrichment behavior is preserved;
- page capture itself performs no persistence/mutation/reconcile;
- no new scanner/cache is introduced.

Do not require support for multi-session/Fast User Switching/RDP. They are outside product scope.

---

## 24. Required tests — QAM bridge

Update `QamFrontendBridgeTests` / `QamFrontendContractTests`.

Prove:

### Generic seam

- Device generic mutation still goes through existing Device admission;
- Profile generic mutation reaches the generic frontend RPC instead of being bridge-rejected;
- unknown page still rejects;
- strict `QuickSettingsBridgeJson` constructor handling remains.

### Legacy QAM Profile operations removed

Assert qam.js/bridge no longer use:

```text
captureActiveGameProfile
setActiveGameProfileEnabled
setActiveGameCpuBoostEnabled
setActiveGameCpuBoostAc
setActiveGameCpuBoostDc
setActiveGameTdp
setActiveGameTdpEnabled
setActiveGamePowerModeEnabled
setActiveGamePowerModeAc
setActiveGamePowerModeDc
```

If hidden FPS branch is removed, also assert absence of the three QAM FPS method strings.

Do not assert removal from `NamedPipeAddonFrontendClient`.

---

## 25. Required tests — qam.js product contract

Source/composition tests are appropriate for the JS/native Steam renderer boundary.

At minimum prove:

### One generic renderer

- Profile uses the same `renderQuickSettingsRow`/generic page path as Device;
- no second feature-shaped Profile control arrays remain.

### No duplicated visible Profile policy

Assert absence of:

```text
PROFILE_SLIDER_COMMIT_DELAY_MS
legacyProfileAdjustTdpPair
profileTdpControls
profileCpuControls
profilePowerControls
hard-coded visible Profile CPU option label table
hard-coded visible Profile Power Mode label table
```

### Hidden capability stays hidden

Assert:

```text
no shared/QAM Profile FPS section
no SHOW_INTEL_FPS_LIMIT escape hatch
no QAM Resolution row
```

after the unreachable hidden branch is removed.

### Page selection

Prove source flow is conceptually:

```text
status.steam.appId == 0 → Device page request
status.steam.appId > 0  → Profile page request with that AppId
```

### Context-aware pending identity

Prove pending identity contains PageId + AppId and does not use old `profile-*` feature keys.

### Context transition

Prove a change:

```text
Profile(A) → Profile(B)
```

retires old unsubmitted A drafts.

### Late result

Prove a result for old AppId cannot replace the currently visible different-AppId page.

### Pending refresh rule

Prove same-context pending drafts survive ordinary invalidation while unrelated rows update.

### Non-writable prune

Prove a pending subfeature draft is retired once the authoritative page makes its edited row non-writable/absent.

### Device regression

Prove Device still:

- uses metadata labels/order/options/ranges;
- uses metadata debounce;
- groups Device TDP correctly;
- preserves linked constraint preview;
- keeps Device admission behavior.

---

## 26. Existing tests that must remain green

At minimum run the entire suite, with particular attention to:

```text
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QamFrontendBridgeTests
QamFrontendContractTests
FrontendNamedPipeTransportTests
Device shared Quick Settings tests
Overlay generic Device transport/renderer tests
Main UI Profile tests
GameProfileMutations tests
CPU Boost/TDP/Power Mode runtime tests
Full1902 lifecycle/controller ownership tests
```

Do not weaken existing Device tests merely to make the new generalization easier.

---

## 27. Manual validation on MSI Claw

### 27.1 No active game

Open QAM with no game running:

- Device page remains exactly as SF-V2-05 behavior;
- no Profile rows appear;
- Device mutations still require current Device admission;
- rapid Device TDP/CPU/Power edits still behave identically.

### 27.2 Active Steam game / no saved Profile

Launch a Steam game with no saved profile:

- QAM switches to Profile shared page;
- game title is preserved;
- Profile toggle is visible OFF;
- enabling creates/enables the correct AppId only;
- no FPS Limit;
- no Resolution row.

### 27.3 Active enabled Profile

Validate:

```text
Profile toggle
TDP toggle + AC/DC PL1/PL2
CPU Boost toggle + AC/DC
Windows Power Mode toggle + AC/DC
```

and exact current order/labels.

### 27.4 Debounce / group behavior

For Profile TDP:

- rapid PL edits preview immediately;
- linked PL1/PL2 correction previews immediately;
- only latest whole-group draft commits after the shared delay;
- reopen/refresh shows Runtime truth.

For CPU/Power:

- each side remains an independent delayed commit;
- metadata delay is used;
- no Profile literal `2000` policy remains in renderer code.

### 27.5 Profile master OFF with pending child draft

Reproduce:

```text
edit Profile TDP/CPU/Power slider
→ before delay, toggle Profile OFF
```

Expected:

- Profile turns OFF;
- subfeature controls become non-writable;
- stale delayed child work cannot mutate the now-disabled Profile after settlement;
- no hidden re-enable or wrong saved state.

### 27.6 Game switch during pending work

Reproduce:

```text
Game A active
→ edit a Profile slider
→ switch/exit to Game B or no game before delayed commit
```

Expected:

- Game A draft does not mutate Game B;
- old result never replaces Game B/Device UI;
- QAM converges to the current context without polling.

### 27.7 Typed failure

Force/observe a realistic persistence/apply failure if available:

- failure is surfaced;
- authoritative returned Profile page is rendered;
- no false committed preview remains.

---

## 28. Implementation order

Recommended implementation sequence inside the PR:

1. **Projection first**
   - implement `BuildProfile`;
   - add projection tests;
   - reuse shared option tables/gap policy.

2. **Runtime generic capture**
   - activate `CaptureQuickSettingsPageAsync(Profile, appId)`;
   - add active/stale/no-game tests.

3. **Mutation adapter**
   - add current-target validation;
   - add Profile row dispatch;
   - add group validation;
   - add tests.

4. **QAM bridge**
   - allow generic Profile;
   - keep Device admission unchanged;
   - temporarily keep legacy Profile strings only until JS migration compiles/tests.

5. **QAM generic renderer migration**
   - convert Device-specific generic helper names/state into page-generic versions;
   - switch refresh to Device/Profile shared page selection;
   - add context-aware pending identity/result guard.

6. **Delete legacy QAM Profile product code**
   - remove old control arrays/drafts/options/mutation names;
   - remove unreachable hidden FPS QAM branch.

7. **Delete legacy QAM bridge allowlist methods**
   - only after source search proves zero JS callsites.

8. **Run full validation**
   - Debug build;
   - Release build;
   - full test suite;
   - `git diff --check`;
   - source search for legacy Profile strings/policy constants.

Do not start by deleting legacy Profile code before the shared projection/adapter tests exist.

---

## 29. Source-search checklist before PR submission

The implementation PR should include evidence that production QAM code no longer contains migrated product authority.

Suggested checks:

```text
PROFILE_SLIDER_COMMIT_DELAY_MS
legacyProfileAdjustTdpPair
profileTdpControls
profileCpuControls
profilePowerControls
captureActiveGameProfile
setActiveGameProfileEnabled
setActiveGameCpuBoostEnabled
setActiveGameCpuBoostAc
setActiveGameCpuBoostDc
setActiveGameTdpEnabled
setActiveGameTdp
setActiveGamePowerModeEnabled
setActiveGamePowerModeAc
setActiveGamePowerModeDc
SHOW_INTEL_FPS_LIMIT
profile-fps-section
```

Expected after SF-V2-08:

```text
no visible-QAM production callsites
```

Typed Runtime/frontend APIs may still contain similarly named backend methods and are intentionally retained.

---

## 30. Acceptance checklist

### Shared product

- [ ] `QuickSettingsPageSnapshot(Profile)` is real, not reserved-only.
- [ ] Page carries active AppId.
- [ ] Exact visible Profile row set is frozen from current QAM policy.
- [ ] Profile title/fallback is preserved.
- [ ] FPS Limit is not exposed.
- [ ] Resolution is not exposed.
- [ ] CPU Boost options come from shared option table.
- [ ] Power Mode options come from shared option table.
- [ ] Profile TDP uses true limits.
- [ ] Profile TDP uses shared known-gap policy.
- [ ] Sliders use shared 2000ms policy.
- [ ] Profile TDP uses `ProfileTdpConfiguration` group.

### Mutation safety

- [ ] Missing/zero Profile AppId invokes zero typed mutations.
- [ ] Stale AppId invokes zero typed mutations.
- [ ] Subfeature mutation while current Profile is disabled/non-writable invokes zero typed mutations.
- [ ] Every valid visible row maps to exactly one existing typed method.
- [ ] Profile TDP group is strict and whole-draft.
- [ ] Typed failure returns authoritative shared Profile page.
- [ ] No wrong-game retargeting.

### QAM

- [ ] Device/no-game behavior unchanged.
- [ ] Active game selects Profile shared page.
- [ ] One generic Toggle/Slider renderer serves Device + Profile.
- [ ] Pending identity includes PageId/AppId.
- [ ] Same-context pending draft survives ordinary invalidation.
- [ ] Context change retires old draft.
- [ ] Late old-AppId result cannot overwrite current page.
- [ ] Non-writable/removed row retires obsolete pending draft.
- [ ] Legacy Profile visible product code removed.
- [ ] Hidden FPS QAM branch removed/not exposed.
- [ ] No polling.

### Bridge/protocol

- [ ] Generic bridge accepts Device + Profile only.
- [ ] Device QAM admission unchanged.
- [ ] Legacy Profile QAM string allowlist removed after zero callsites.
- [ ] Frontend protocol remains 28.
- [ ] Overlay protocol remains 7.
- [ ] Typed frontend Profile APIs remain available to Main UI/backend.

### Architecture

- [ ] No new state authority.
- [ ] No new Profile store.
- [ ] No manager/registry/reflection dispatcher.
- [ ] No controller/Full1902 lifecycle changes.
- [ ] No speculative race machinery.

---

## 31. Explicit non-goals

Do not include in SF-V2-08:

- Overlay Profile publication/rendering — SF-V2-09;
- Intel FPS Limit exposure;
- Resolution exposure;
- fan control;
- battery limit;
- controller LED/vibration;
- QAM visual redesign;
- Steam component discovery rewrite;
- Main UI Profile migration to generic Quick Settings;
- removal of typed Profile frontend APIs;
- new Profile persistence format;
- multi-user/session support;
- polling;
- new lifecycle epochs/locks/barriers for theoretical timing combinations.

---

## 32. Expected end state

After merge:

```text
Runtime Profile state / persistence
        ↓
FrontendGameProfileSnapshot
        ↓
QuickSettingsPageSnapshot(Profile)
        ↓
          ┌─────────────────┐
          │ shared product  │
          └─────────────────┘
                   ↓
        QAM generic renderer
```

Device and Profile are then both shared products in QAM.

SF-V2-09 only needs to add Profile **publication + binding** to the already-generic Overlay path:

```text
QuickSettingsPageSnapshot(Profile)
        ↓
Overlay v7 generic transport
        ↓
existing Overlay generic renderer/binder
```

No new Profile-specific Overlay control construction should be necessary.
