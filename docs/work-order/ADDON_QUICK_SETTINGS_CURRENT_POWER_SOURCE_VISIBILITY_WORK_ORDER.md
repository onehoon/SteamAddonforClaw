# Work Order - Quick Settings Current Power Source Visibility

> Date: 2026-09-20
> Status: Ready for implementation
> Reviewed production baseline: main at `239e0dbd41b4cae4b019a59d2a07145310be99b1` after PR #549
> Product scope: Standalone Full PID1902
> CTW integration: Out of scope
> Full1902 authority: `docs/Full 1902 Implementation/README.md` and referenced Full1902 documents
> Shared Quick Settings authority: `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`

---

## 1. Goal

Add one persisted Main UI option that reduces AC/DC clutter on both Addon Quick Settings surfaces:

```text
Settings
[ ] Show only current power source
```

Behavior:

```text
OFF
-> QAM shows both Plugged in + On battery rows
-> Overlay shows both Plugged in + On battery rows

ON + AC
-> QAM/Overlay show only Plugged in rows

ON + DC
-> QAM/Overlay show only On battery rows

ON + power source unknown/unreadable
-> show both sides
```

The Main UI Device/Profile editors remain unchanged and continue to show both AC and DC configuration.

The purpose is presentation density only.

This PR must **not** delete, discard, normalize away, or stop carrying the hidden side's values.

---

## 2. Required reading before editing

Read:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR5_CONVERGENCE_CLEANUP_PARITY_ACCEPTANCE_WORK_ORDER.md
```

Inspect current source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
src/SteamInputAddonforClaw/Profiles/Performance/WindowsTdpPowerNotificationSource.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
```

---

## 3. Current architecture facts

Device/Profile Quick Settings already have one Runtime-side product projection:

```text
typed Runtime feature state
        ↓
QuickSettingsPresentation
        ↓
QuickSettingsPageSnapshot
       /                 \
     QAM               Overlay
 generic renderer    generic renderer
```

Keep that ownership.

Do not add:

- a QAM-only AC/DC filter;
- an Overlay-only AC/DC filter;
- a second Device/Profile projection;
- a new settings manager;
- a power-state cache/authority;
- polling.

The new setting must be one persisted Runtime setting, and both surfaces must consume the same visibility metadata.

---

## 4. Critical product rule: hide rows visually, never remove their data

This is the central requirement.

Do **not** remove the opposite power-source rows from `QuickSettingsSection.Rows`.

Add one shared presentation metadata property to `QuickSettingsRow`:

```csharp
public sealed record QuickSettingsRow(
    QuickSettingsRowId RowId,
    string Label,
    QuickSettingsControlKind ControlKind,
    bool Available,
    bool Writable,
    QuickSettingsValue? Value,
    QuickSettingsSliderSpec? SliderSpec,
    QuickSettingsCommitPolicy CommitPolicy,
    QuickSettingsCommitGroupId? CommitGroupId = null)
{
    public bool Visible { get; init; } = true;
}
```

Meaning:

```text
Visible = false
!= unavailable
!= unwritable
!= value absent
!= removed from the page
```

It means only:

```text
the generic renderer must not show this row
```

All values, slider specs, commit metadata, and grouping remain present in the authoritative page.

This is especially important for TDP.

Current TDP grouped commits carry the complete draft:

```text
Enabled
AC PL1
AC PL2
DC PL1
DC PL2
```

When AC is active and current-power-only mode is enabled, DC PL1/DC PL2 may be visually hidden, but they must remain in the section so grouped draft seeding and Runtime validation continue to receive the complete configuration.

Do not weaken or redesign the existing TDP mutation contract.

---

## 5. Persisted setting

Add one init-only setting to `AppSettings`:

```csharp
public bool QuickSettingsCurrentPowerSourceOnly { get; init; }
```

Default:

```text
false
```

Reason:

- existing users keep the current both-sides UI;
- missing JSON from older pre-release builds naturally means OFF;
- no migration layer is required.

Update `SettingsStore.Load()` and `Save()`.

Persist exact semantic state, e.g.:

```json
{
  "QuickSettingsCurrentPowerSourceOnly": true
}
```

Malformed/missing/non-boolean data for this one preference must resolve this preference to `false` without resetting unrelated settings.

Do not add a schema/version migration mechanism.

---

## 6. Settings mutation authority

Add the narrow setting mutation to `StartupSettingsCoordinator`.

Preferred shape:

```csharp
public void ChangeQuickSettingsCurrentPowerSourceOnly(bool enabled)
{
    if (Settings.QuickSettingsCurrentPowerSourceOnly == enabled)
        return;

    var next = Settings with
    {
        QuickSettingsCurrentPowerSourceOnly = enabled
    };

    _settingsStore.Save(next);
    Settings = next;
}
```

Use the existing save-then-current pattern.

No separate preference service/event is required.

`InProcessAddonFrontendControl` already owns frontend setting RPCs and can raise `StateInvalidated` after the mutation succeeds.

---

## 7. Main UI contract

Add to `FrontendSettingsSnapshot`:

```csharp
public bool QuickSettingsCurrentPowerSourceOnly { get; init; }
```

Expose one frontend mutation:

```csharp
Task<FrontendSettingsSnapshot> SetQuickSettingsCurrentPowerSourceOnlyAsync(
    bool enabled,
    CancellationToken cancellationToken = default);
```

Runtime implementation:

```csharp
public Task<FrontendSettingsSnapshot> SetQuickSettingsCurrentPowerSourceOnlyAsync(
    bool enabled,
    CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();

    _settings.ChangeQuickSettingsCurrentPowerSourceOnly(enabled);
    StateInvalidated?.Invoke(this, EventArgs.Empty);
    return Task.FromResult(MapSettings());
}
```

`MapSettings()` must publish the persisted value.

QAM and Overlay do **not** need a new settings RPC.

They only consume the normal refreshed `QuickSettingsPageSnapshot`.

---

## 8. Main UI Settings card

Add one card to `SettingsPage`.

Recommended UX:

```text
Header:
Show only current power source

Description:
Show only Plugged in or On battery controls in QAM and Overlay.
```

Use a normal `ToggleSwitch`.

OFF:

```text
always show AC + DC
```

ON:

```text
show only the currently active AC or DC rows
```

Use the existing UI initialization-suppression pattern already used by other toggles.

Conceptually:

```csharp
private bool _applyingQuickSettingsPowerSourcePreference;

internal void Initialize(...)
{
    ...
    SetQuickSettingsPowerSourceToggle(
        bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly);
}

private async void QuickSettingsPowerSourceToggle_Toggled(...)
{
    if (_applyingQuickSettingsPowerSourcePreference || _frontend is null)
        return;

    try
    {
        var settings =
            await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(
                QuickSettingsPowerSourceToggle.IsOn);

        SetQuickSettingsPowerSourceToggle(
            settings.QuickSettingsCurrentPowerSourceOnly);
    }
    catch (Exception exception)
    {
        AppLog.Warn(
            "Settings",
            "Quick Settings power-source visibility update failed.",
            exception);

        var bootstrap = await _frontend.GetBootstrapAsync();
        SetQuickSettingsPowerSourceToggle(
            bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly);
    }
}
```

Do not optimistically leave the toggle in a state the Runtime did not persist.

---

## 9. One generic Windows AC/DC read fact

Current production already reads the same Windows AC/DC fact for Intel FPS:

```text
GetSystemPowerStatus
ACLineStatus
```

The current names `FpsPowerSource`, `WindowsFpsPowerSource`, and `WindowsIntelFpsPowerNotificationSource` were acceptable while only Intel FPS consumed them, but this PR makes the same OS fact a shared Runtime input.

Promote that platform fact without creating a manager/state machine.

Preferred small cleanup:

```text
FpsPowerSource
    -> AcDcPowerSource

WindowsFpsPowerSource
    -> WindowsAcDcPowerSource

WindowsIntelFpsPowerNotificationSource
    -> WindowsAcDcPowerNotificationSource
```

Keep behavior identical.

Suggested ownership:

```text
Profiles/Performance/WindowsAcDcPowerSource.cs
    AcDcPowerSource
    WindowsAcDcPowerSource.Read()
    WindowsAcDcPowerNotificationSource
```

`IntelFrameLimiterRuntime` then consumes this same generic AC/DC type.

Do not merge this with the TDP lifecycle watcher.

`WindowsTdpPowerNotificationSource` has additional suspend/resume settle behavior and remains TDP-specific.

The goal is only to stop an Intel-named wrapper from becoming a second generic AC/DC implementation.

---

## 10. Do not cache the current power source

The notification is a wake-up signal, not a new authority.

Do not add:

```text
CurrentPowerSource field
PowerSourceManager
PowerSourceState
PowerSourceEpoch
polling timer
```

Whenever a Quick Settings page is projected in current-power-only mode, read the current source fresh with:

```text
WindowsAcDcPowerSource.Read()
```

Return type:

```csharp
AcDcPowerSource?
```

`null` means unknown/unreadable.

Unknown policy:

```text
show both AC and DC rows
```

This is a UI-density option; unknown power state must not hide potentially useful controls.

---

## 11. Testable power-source read seam

Do not force unit tests to call Windows P/Invoke.

Give `InProcessAddonFrontendControl` one narrow reader delegate.

Example:

```csharp
private readonly Func<AcDcPowerSource?> _quickSettingsPowerSource;

internal InProcessAddonFrontendControl(
    ...,
    Func<AcDcPowerSource?>? quickSettingsPowerSource = null)
{
    ...
    _quickSettingsPowerSource =
        quickSettingsPowerSource ?? WindowsAcDcPowerSource.Read;
}
```

Production may explicitly pass `WindowsAcDcPowerSource.Read` from `AddonProcessHost`.

Tests can inject:

```csharp
() => AcDcPowerSource.AC
() => AcDcPowerSource.DC
() => null
```

This delegate is only a read seam.

Do not turn it into a service/interface/manager.

---

## 12. Shared visibility projection

Keep `BuildDevice(...)` and `BuildProfile(...)` responsible for constructing the full product page.

Then apply visibility as a second shared presentation step.

Preferred location:

```text
QuickSettingsPresentation.ApplyPowerSourceVisibility(...)
```

Conceptually:

```csharp
internal static QuickSettingsPageSnapshot ApplyPowerSourceVisibility(
    QuickSettingsPageSnapshot page,
    bool currentPowerSourceOnly,
    AcDcPowerSource? source)
{
    if (!currentPowerSourceOnly || source is null)
        return page;

    return page with
    {
        Sections = page.Sections
            .Select(section => section with
            {
                Rows = section.Rows
                    .Select(row => row with
                    {
                        Visible = IsVisibleForPowerSource(row.RowId, source.Value)
                    })
                    .ToArray()
            })
            .ToArray()
    };
}
```

Use exact `QuickSettingsRowId` identities.

Do not inspect labels such as `"Plugged in"` or `"On battery"`.

---

## 13. Exact AC/DC row classification

The following rows are AC-only:

```text
DeviceTdpAcPl1
DeviceTdpAcPl2
DeviceCpuBoostAc
DevicePowerModeAc

ProfileTdpAcPl1
ProfileTdpAcPl2
ProfileCpuBoostAc
ProfilePowerModeAc
```

The following rows are DC-only:

```text
DeviceTdpDcPl1
DeviceTdpDcPl2
DeviceCpuBoostDc
DevicePowerModeDc

ProfileTdpDcPl1
ProfileTdpDcPl2
ProfileCpuBoostDc
ProfilePowerModeDc
```

Everything else remains visible:

```text
DeviceTdpEnabled
DeviceCpuBoostEnabled
DevicePowerModeEnabled

ProfileEnabled
ProfileTdpEnabled
ProfileCpuBoostEnabled
ProfilePowerModeEnabled
```

No other current row is power-source-specific.

If a future FPS-limit Quick Settings row is added, that future feature should classify its own AC/DC row IDs then. Do not add speculative row IDs in this PR.

---

## 14. Apply visibility on every authoritative page result

Do not apply the filter only to initial capture.

It must apply to:

```text
Device capture
Profile capture
mutation result page
ordinary StateInvalidated refresh
Overlay refresh
QAM refresh
```

Add one local helper in `InProcessAddonFrontendControl`, for example:

```csharp
private QuickSettingsPageSnapshot ApplyQuickSettingsVisibility(
    QuickSettingsPageSnapshot page)
{
    var currentOnly =
        _settings.Settings.QuickSettingsCurrentPowerSourceOnly;

    if (!currentOnly)
        return page;

    return QuickSettingsPresentation.ApplyPowerSourceVisibility(
        page,
        currentOnly,
        _quickSettingsPowerSource());
}
```

Then:

```csharp
CaptureDeviceQuickSettingsPageAsync
-> BuildDevice
-> ApplyQuickSettingsVisibility

CaptureProfileQuickSettingsPageAsync
-> BuildProfile
-> ApplyQuickSettingsVisibility
```

Also change `MutateQuickSettingAsync` so the returned authoritative page is filtered before leaving the Runtime:

```csharp
public async Task<QuickSettingsMutationResult> MutateQuickSettingAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();

    var result = await QuickSettingsMutationAdapter
        .MutateAsync(this, intent, cancellationToken)
        .ConfigureAwait(false);

    return result with
    {
        Page = ApplyQuickSettingsVisibility(result.Page)
    };
}
```

Do not put settings/power-source dependencies inside `QuickSettingsMutationAdapter`.

The adapter remains the mutation validator/dispatcher.

---

## 15. Runtime mutation validation must not treat hidden data as absent

This PR is presentation-only.

Do not modify central Runtime mutation validation to reject a row merely because:

```text
Visible == false
```

Why:

A visible AC TDP slider mutation still carries hidden DC companion values in the complete grouped draft.

The hidden companion values must remain valid mutation input.

Correct distinction:

```text
renderer interaction admission
-> Visible + Available + Writable

Runtime product/mutation validation
-> existing row/value/group rules
-> visibility does not delete companion data
```

---

## 16. AC/DC change notification

Use the generic `WindowsAcDcPowerNotificationSource`.

Compose one process-owned source in `AddonProcessHost`.

It should be registered independently of IGCL initialization success.

Do not keep notification creation inside the Intel FPS initialization try block.

Reason:

Current-power-only Quick Settings must still react to AC/DC changes even if Intel FPS initialization is unavailable.

The same notification can fan out to two existing consumers:

```text
AC/DC changed
    ├─ IntelFrameLimiterRuntime.Reconcile(...)
    └─ Quick Settings StateInvalidated
```

Suggested field:

```csharp
private WindowsAcDcPowerNotificationSource? _acDcPowerSource;
```

Suggested handler:

```csharp
private void OnAcDcPowerSourceChanged()
{
    _ = Task.Run(() =>
    {
        try
        {
            _intelFpsRuntime.Reconcile(
                _runtimeHost?.ActualRunningAppId ?? 0,
                "PowerSourceChanged");
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "Profiles.IntelFps",
                "FPS reconcile failed after AC/DC change.",
                exception);
        }

        if (_frontendControl is InProcessAddonFrontendControl control)
            control.NotifyQuickSettingsPowerSourceChanged();
    });
}
```

Exact logging can follow current style.

No retry loop.

---

## 17. Narrow frontend invalidation for power changes

Add one internal method to `InProcessAddonFrontendControl`:

```csharp
internal void NotifyQuickSettingsPowerSourceChanged()
{
    if (Volatile.Read(ref _shutdownStarted) != 0)
        return;

    if (!_settings.Settings.QuickSettingsCurrentPowerSourceOnly)
        return;

    StateInvalidated?.Invoke(this, EventArgs.Empty);
}
```

This keeps OFF mode from causing unnecessary QAM/Overlay Quick Settings refreshes on every cable transition.

Do not persist anything on power change.

Do not mutate Device/Profile configuration on power change.

---

## 18. Suspend / resume

Sleep/Hibernate/Resume is a supported real lifecycle.

A resume can occur with a different power source than suspend.

Do not rely solely on an AC/DC callback having arrived during suspend.

From the existing `AddonProcessHost.OnPowerResumeObserved()` path, request one Quick Settings invalidation through the same narrow frontend method.

Example:

```csharp
if (_frontendControl is InProcessAddonFrontendControl control)
    control.NotifyQuickSettingsPowerSourceChanged();
```

The next capture reads `WindowsAcDcPowerSource.Read()` fresh.

Do not add another resume timer solely for this UI option.

Existing TDP/CPU/Power Mode settle behavior remains unchanged.

---

## 19. QAM generic renderer

QAM must not classify AC/DC itself.

It only consumes:

```text
row.visible
```

At the generic renderer boundary:

```javascript
const renderQuickSettingsRow = (page, section, row) => {
  if (row.visible === false) return null;

  ...
};
```

Because Frontend protocol is bumped, validate that current-version row payloads contain a boolean visibility value where the existing page validator checks row structure.

Do not infer visibility from:

- RowId numeric values;
- labels;
- page type;
- AC/DC state;
- local Windows APIs.

QAM remains a renderer.

---

## 20. Overlay generic renderer

Overlay also must not classify AC/DC itself.

Keep the full authoritative page in `OverlayQuickSettingsPageBinding`.

Only the visual renderer skips/collapses hidden rows.

Preferred behavior:

```text
Visible=false
-> no interactive row is built / visual is Collapsed
-> no blank vertical spacing remains
```

Do not use WinUI `Visibility.Hidden` semantics that reserve empty layout space.

The product meaning is "not shown", so the rendered result must behave like `Collapsed`.

---

## 21. Overlay fast-path shape must include Visible

Current Overlay has a fast path based on `QuickSettingsRowShape`.

A pure AC -> DC change can keep the same:

```text
RowId
ControlKind
SliderKind
WellFormed
```

while changing which rows are visible.

If `Visible` is omitted from shape comparison, Overlay may keep the old visual tree and fail to switch AC/DC rows.

Add visibility to the renderer shape.

Conceptually:

```csharp
private sealed record QuickSettingsRowShape(
    QuickSettingsRowId RowId,
    QuickSettingsControlKind ControlKind,
    QuickSettingsSliderKind? SliderKind,
    bool WellFormed,
    bool Visible);
```

and:

```csharp
private static QuickSettingsRowShape QuickSettingsRowShapeOf(
    QuickSettingsRow row) => new(
        row.RowId,
        row.ControlKind,
        row.ControlKind == QuickSettingsControlKind.Slider
            ? row.SliderSpec?.Kind
            : null,
        QuickSettingsRowRendering.IsWellFormed(row),
        row.Visible);
```

A visibility change must force the existing normal rebuild path.

Do not add a special AC/DC renderer branch.

---

## 22. Pending slider drafts when the edited side becomes hidden

Normal real scenario:

```text
AC
-> user moves AC PL1
-> 2 s trailing draft exists
-> user unplugs power
-> DC becomes current
-> AC row becomes hidden
```

The not-yet-submitted AC edit should be retired when the new authoritative page arrives.

Do this using the existing generic pending-prune paths.

Overlay:

Current prune accepts:

```text
Available && Writable
```

Change the edited-row continuation condition to require:

```text
Visible && Available && Writable
```

QAM:

Apply the same rule in the existing pending-draft reconciliation/admission logic.

Do not cancel already-submitted Runtime operations with a new epoch/cancellation architecture.

Do not add a race state machine.

The existing authoritative result + next refresh is sufficient.

---

## 23. QAM/Overlay interaction admission

For a row to be interactively editable on a renderer:

```text
Visible
Available
Writable
valid value/control metadata
surface not busy
```

This is a renderer admission rule only.

Again, do not make `QuickSettingsMutationAdapter` reject hidden companion values.

---

## 24. Frontend transport version

Current baseline:

```text
FrontendTransportProtocol = 34
```

This PR adds:

- a new `FrontendSettingsSnapshot` member;
- a new settings mutation RPC;
- `QuickSettingsRow.Visible`.

Bump:

```text
34 -> 35
```

Add the normal version-history comment.

Add:

```text
FrontendRpcMethod.SetQuickSettingsCurrentPowerSourceOnly
```

and the typed request payload.

Update:

- client;
- server dispatch;
- tests;
- fake implementations.

Pre-release policy: no compatibility shim.

---

## 25. Overlay transport version

Current baseline:

```text
OverlayTransportProtocol = 8
```

The shared `QuickSettingsRow` wire shape gains visibility metadata.

Bump:

```text
8 -> 9
```

Do not add a second Overlay-specific visibility DTO.

Overlay continues to transport the same `QuickSettingsPageSnapshot`.

---

## 26. Main UI Device/Profile pages remain unchanged

Do not hide AC/DC controls in:

```text
DevicePage
ProfilePage
```

Those are full configuration editors.

This option applies only to compact Quick Settings surfaces:

```text
Steam QAM
Addon Overlay
```

That distinction is intentional.

---

## 27. Required tests - persistence

Add/extend Settings tests to prove:

1. missing preference -> false;
2. true round-trips;
3. false round-trips;
4. malformed preference only falls back to false;
5. unrelated settings remain intact;
6. normal save includes the new key.

No migration test framework.

---

## 28. Required tests - presentation visibility

Extend `QuickSettingsPresentationTests`.

For Device:

```text
filter OFF
-> every AC/DC row Visible=true

filter ON + AC
-> all AC rows Visible=true
-> all DC rows Visible=false
-> common toggle rows Visible=true

filter ON + DC
-> inverse

filter ON + unknown
-> all rows Visible=true
```

Repeat representative assertions for Profile.

Also prove:

- hidden rows still exist in the section;
- hidden rows retain their values;
- hidden TDP rows retain `CommitGroupId`;
- section row count/order is unchanged.

---

## 29. Required tests - mutation result visibility

Add in-process/mutation tests proving:

```text
current-only + AC
-> initial Device capture hides DC
-> mutation result page also hides DC

current-only + DC
-> initial Profile capture hides AC
-> mutation result page also hides AC
```

Do not accept a design where rows reappear immediately after a slider/toggle settlement.

---

## 30. Required tests - TDP grouped draft integrity

Add regression coverage proving the hidden side is still data.

Example:

```text
current-only + AC
Device TDP page:
  AC rows visible
  DC rows hidden

edit AC PL1
-> grouped draft still contains:
   Enabled
   AC PL1
   AC PL2
   DC PL1
   DC PL2
```

Same principle for Profile TDP.

This is the highest-value correctness regression for this feature.

---

## 31. Required tests - power transition

Inject the power-source reader.

Verify:

```text
AC capture
-> AC visible / DC hidden

reader changes to DC
-> StateInvalidated-triggered recapture
-> AC hidden / DC visible
```

When the setting is OFF:

```text
power-source changed notification
-> no Quick Settings-specific invalidation required
-> next capture still shows both
```

When read returns null:

```text
-> both sides visible
```

---

## 32. Required tests - pending draft retirement

Overlay:

- pending visible AC slider draft;
- apply authoritative page where that edited row becomes `Visible=false`;
- unsubmitted draft is retired;
- no mutation fires later.

QAM contract/source test:

- pending row validity includes `visible`;
- hidden edited row cannot retain/schedule a pending draft.

Do not build synthetic instruction-level race tests.

---

## 33. Required tests - renderer parity

Extend the existing shared-surface parity coverage.

For one page containing visible and hidden rows:

QAM:

- consumes `row.visible`;
- does not contain an AC/DC RowId table;
- does not call Windows power APIs.

Overlay:

- consumes `row.Visible`;
- renderer shape includes visibility;
- does not contain its own AC/DC RowId classification table;
- no blank row placeholder is rendered for hidden rows.

Product-side RowId classification must exist once in Runtime/shared presentation code.

---

## 34. Protocol tests

Update transport tests for:

```text
Frontend 35
Overlay 9
```

Round-trip at least one `QuickSettingsPageSnapshot` containing:

```text
Visible=true row
Visible=false row
```

and prove the flag survives transport unchanged.

Also cover the new Main UI settings RPC.

---

## 35. Shutdown / cleanup

Dispose/unregister the generic AC/DC notification source during normal process teardown.

Do not let the callback publish new invalidations after process shutdown admission closes.

Reuse current shutdown facts.

No additional cancellation owner is required.

---

## 36. Full1902 invariants - untouched

This PR must not change:

- Center M authority;
- PID1901/PID1902 policy;
- DirectInput ownership;
- HidHide ownership/baseline;
- VIIPER ownership/teardown;
- X360/SteamDeck presentation switching;
- physical-device recovery;
- routing rollback/fail-close;
- WING/OEM1 behavior;
- Overlay OQ4 neutral/capture/release;
- QAM BrowserView width patch from PR #549.

Power-source observation in this PR is UI presentation invalidation plus the already-existing Intel FPS reconcile only.

It must not become controller authority.

---

## 37. Overengineering guardrails

Do not add:

- `PowerSourceManager`;
- `QuickSettingsVisibilityManager`;
- cached AC/DC state authority;
- power-source epochs;
- renderer-specific AC/DC policy;
- new background worker;
- polling;
- periodic refresh;
- visibility registry;
- generic rule engine;
- new page/provider abstraction;
- cancellation/state machinery for already-submitted mutations.

Required architecture:

```text
one persisted bool
+
one fresh Windows AC/DC read
+
one shared row Visible property
+
existing StateInvalidated refresh
+
two generic renderers
```

---

## 38. Suggested implementation sequence

### A. Shared contract + persistence

- add `QuickSettingsRow.Visible`;
- add `QuickSettingsCurrentPowerSourceOnly`;
- update SettingsStore;
- update frontend settings contract/RPC;
- bump Frontend protocol to 35;
- bump Overlay protocol to 9.

### B. Generic AC/DC Windows fact

- promote existing FPS-named AC/DC reader/notification to generic naming;
- wire Intel FPS to the generic source;
- register source independently of IGCL availability.

### C. Shared visibility projection

- add exact RowId classification in `QuickSettingsPresentation`;
- apply visibility to Device/Profile capture;
- apply it to mutation result pages.

### D. Runtime invalidation

- AC/DC change -> Intel FPS reconcile + Quick Settings invalidation;
- resume -> Quick Settings invalidation;
- OFF mode skips UI-specific invalidation.

### E. Renderers

- QAM skips `visible=false`;
- Overlay collapses/skips `Visible=false`;
- Overlay row shape includes visibility;
- pending-draft pruning treats hidden edited rows as no longer interactable.

### F. Main UI

- Settings card + toggle;
- authoritative readback on mutation/failure.

### G. Tests

- persistence;
- visibility projection;
- mutation result;
- grouped TDP integrity;
- AC/DC transition;
- pending draft retirement;
- transport;
- QAM/Overlay parity.

---

## 39. Validation

Run:

```powershell
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet test SteamInputAddonforClaw.slnx --no-restore --logger "console;verbosity=minimal"
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
git diff --check
```

Useful focused iteration:

```powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-restore --filter "FullyQualifiedName~QuickSettingsPresentation|FullyQualifiedName~QuickSettingsMutation|FullyQualifiedName~OverlayQuickSettings|FullyQualifiedName~QamFrontend|FullyQualifiedName~Settings"
```

Focused tests do not replace the full suite.

---

## 40. Real-device acceptance

Test with the Settings option OFF:

```text
AC -> QAM/Overlay show both sides
DC -> QAM/Overlay show both sides
```

Turn the option ON while QAM/Overlay is open:

```text
current surface refreshes without restart
only current side remains visible
no empty row spacing
```

While ON:

```text
AC
-> only Plugged in rows

unplug
-> On battery rows replace Plugged in rows

plug back in
-> Plugged in rows replace On battery rows
```

Verify both:

```text
Device
Profile for an active game
```

Verify hidden-side settings are preserved:

1. set different AC and DC TDP/CPU Boost/Power Mode values;
2. enable current-power-only;
3. switch AC/DC several times;
4. confirm each side returns with its own previously configured values;
5. disable current-power-only;
6. confirm both complete sides are still present.

Also verify:

- TDP grouped slider edits still settle normally;
- no opposite-side value is zeroed/reset;
- QAM close/reopen is correct;
- Overlay hide/show is correct;
- Sleep/Resume with AC/DC change converges to the current side;
- PR #549 QAM width behavior remains unchanged.

---

## 41. Definition of done

- [ ] Main UI Settings has one current-power-only toggle.
- [ ] Default is OFF.
- [ ] Main UI Device/Profile full editors remain unchanged.
- [ ] AppSettings persists the preference.
- [ ] FrontendSettingsSnapshot exposes the preference.
- [ ] New settings RPC returns authoritative settings readback.
- [ ] Frontend protocol is 35.
- [ ] Overlay protocol is 9.
- [ ] One generic Windows AC/DC reader/notification seam exists.
- [ ] No cached power-source authority exists.
- [ ] Unknown power source shows both sides.
- [ ] `QuickSettingsRow.Visible` defaults true.
- [ ] Opposite-side rows remain in every authoritative page.
- [ ] Opposite-side row values/specs/group IDs are preserved.
- [ ] Device visibility is applied on capture and mutation result.
- [ ] Profile visibility is applied on capture and mutation result.
- [ ] QAM hides rows only from shared `row.visible`.
- [ ] Overlay hides rows only from shared `row.Visible`.
- [ ] Overlay fast-path shape includes visibility.
- [ ] Pending unsubmitted edit is retired when its edited row becomes hidden.
- [ ] Runtime mutation validation still accepts hidden companion data in grouped TDP intents.
- [ ] AC/DC notification refresh is event-driven.
- [ ] Resume refresh converges without polling.
- [ ] Full1902/controller/OQ4/QAM-width ownership remains untouched.
- [ ] Full build/test suite passes.
- [ ] Real-device AC/DC acceptance passes.

---

## 42. Review standard

Blocking examples:

- hidden AC/DC rows are removed from `QuickSettingsPageSnapshot`;
- TDP grouped commits lose the hidden side;
- hidden-side persisted values are reset or rewritten;
- QAM and Overlay implement separate AC/DC RowId policies;
- only initial capture is filtered and mutation results restore both sides;
- Overlay fast path fails to rebuild on visibility change;
- a pending hidden-side draft can still fire later;
- power-source updates depend on Intel IGCL initialization succeeding;
- AC/DC state is polled;
- Resume can permanently leave the wrong side visible;
- Main UI Device/Profile full editors are also filtered;
- PR touches controller/HidHide/VIIPER authority without necessity.

Do not block for:

- exact Settings card icon choice;
- QAM vs Overlay pixel-level visual differences;
- immediate vs next-event convergence for an already-submitted mutation;
- theoretical instruction-level races;
- unsupported multi-session behavior;
- desire for a generalized visibility/rule framework.

Final target:

> keep the complete AC/DC product state, change only what the compact surfaces render.
