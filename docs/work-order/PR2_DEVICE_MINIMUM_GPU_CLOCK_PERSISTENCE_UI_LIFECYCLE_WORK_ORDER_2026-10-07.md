# Work Order — PR2: Device Minimum GPU Clock Persistence, UI, and Lifecycle Reconcile

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@e7dddbb4f2a11428a50003db8411d2e9719ef9b2`  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and its active precedence chain  
> **PR1 architecture/work order:** `docs/work-order/PR1_INTEL_MINIMUM_GPU_CLOCK_PRODUCTION_CORE_WORK_ORDER_2026-10-07.md`  
> **PR1 implementation:** PR #702, squash merge `b4c5b8932e0fc4f3a57bda9c37050859c8d408fa`  
> **Official Intel API baseline:** Intel Graphics Control Library `include/igcl_api.h`, upstream `master@b6c462933502e13d1537dd5024949a51be30e63d`  
> **Intel upstream checked:** 2026-10-07; `master` still resolves to `b6c462933502e13d1537dd5024949a51be30e63d`  
> **Current Frontend protocol:** `FrontendTransportProtocol.CurrentVersion = 61`  
> **Current Overlay protocol:** `OverlayTransportProtocol.CurrentVersion = 17`  
> **Scope:** production Device-level Minimum GPU Clock persistence, Main App Device UI, AC/DC application, startup/restart/crash/resume recovery, and uninstall release  
> **Explicitly deferred:** Steam/XBOX per-game override and all Quick Settings/Overlay GPU-clock rows

---

## 1. Goal

Turn the PR1 production Intel frequency core into a complete **Device-level Minimum GPU Clock** feature.

The feature exists for one practical use case:

~~~text
some AAA games
→ Intel integrated GPU clock sometimes falls farther than desired
→ observed performance / frametime may become worse
→ user chooses a minimum GPU frequency floor
→ Intel remains free to boost above that floor
→ Intel package-power / current / thermal limits remain authoritative
~~~

The product must **not** force a fixed GPU clock.

The product must **not** own GPU maximum frequency.

The production behavior is:

~~~text
Minimum GPU Clock = user-selected AC/DC floor

Addon controls:
  minimum frequency only

Intel / hardware still controls:
  maximum frequency
  boost behavior above the floor
  voltage
  PL1 / PL2 / PL4
  current limits
  thermal limits
  actual resolved clock
~~~

PR2 completes only the Device/global path.

Per-game Steam/XBOX overrides remain PR3.

---

## 2. Current code facts

Reviewed `main@e7dddbb4f2a11428a50003db8411d2e9719ef9b2`.

### 2.1 PR1 production core is merged

`Profiles/Performance/IntelGpuMinimumClock.cs` already provides:

~~~text
IntelGpuMinimumClockPolicy
IntelGpuMinimumClockControl
IntelGpuMinimumClockRuntime
~~~

and production capability discovery for:

- integrated Intel graphics adapter;
- `CTL_FREQ_DOMAIN_GPU`;
- `canControl`;
- hardware min/max;
- `ctlFrequencyGetAvailableClocks`;
- dynamic selectable lower/upper/default policy;
- minimum-only SetRange request construction;
- immediate GetRange verification;
- original-minimum durable ownership marker;
- exact original-minimum restore;
- bounded native-session reinitialize primitive;
- x64 ABI tests.

Do not replace this with another owner.

### 2.2 PR1 startup is deliberately read-only

Current host path:

~~~text
ReconcileDeviceProfileStartup()
→ InitializeIntelGpuMinimumClockReadOnlyForStartup()
→ IntelGpuMinimumClockRuntime.InitializeReadOnly()
~~~

PR1 never applies a production frequency range.

PR2 changes this from discovery-only to Device-policy reconcile.

### 2.3 Device persistence has no GPU minimum field yet

Current `DevicePerformanceSettings` contains:

~~~text
CpuBoost
Tdp
PowerMode
~~~

PR2 adds one additive optional field.

Do not change profile schema architecture.

### 2.4 Main Device page uses the shared Device aggregate

Current Main UI:

~~~text
DevicePage.RefreshAsync()
→ CaptureDeviceQuickSettingsAsync()
→ FrontendDeviceQuickSettingsSnapshot
~~~

Current aggregate children are:

~~~text
CPU Boost
TDP
Power Mode
Battery Charge Limit
~~~

PR2 should extend this existing aggregate rather than make DevicePage perform a second independent refresh round trip.

### 2.5 Current AC/DC notification path

Current:

~~~text
WindowsAcDcPowerNotificationSource
→ AddonProcessHost.OnAcDcPowerSourceChanged()
→ IntelFrameLimiterRuntime.Reconcile("PowerSourceChanged")
→ Quick Settings power-source notification
~~~

PR2 adds Device Minimum GPU Clock reconciliation to this existing event.

Do not add another power watcher.

### 2.6 Current Resume path

Current:

~~~text
PowerResumeObserved
→ controller presentation reconcile immediately
→ controller LED/vibration bounded reapply
→ ReconcilePerformanceAfterResumeAsync()
   → 2.5 second settle
   → CPU Boost
   → Power Mode
   → Battery Charge Limit
~~~

PR2 adds Minimum GPU Clock to this existing delayed performance reconcile.

Do not add a separate resume watcher or timer.

### 2.7 Current transport versions

~~~text
FrontendTransportProtocol.CurrentVersion = 61
OverlayTransportProtocol.CurrentVersion = 17
~~~

PR2 changes the Runtime ↔ Main UI frontend contract.

PR2 does **not** change the shared Quick Settings row/section schema or Overlay wire contract.

Therefore:

~~~text
Frontend protocol → bump
Overlay protocol  → unchanged
~~~

---

## 3. Official Intel IGCL facts reconfirmed

Intel upstream reviewed:

~~~text
intel/drivers.gpu.control-library
master = b6c462933502e13d1537dd5024949a51be30e63d
~~~

This is still the same API baseline used by PR1.

### 3.1 Available clocks

Intel documents:

~~~c
ctlFrequencyGetAvailableClocks(...)
~~~

as returning:

- non-overclocked hardware clock frequencies;
- MHz values;
- slowest-to-fastest order.

PR1 already normalizes the list conservatively.

PR2 must consume PR1's derived capability.

Do not rediscover or reinterpret available clocks in UI code.

### 3.2 Minimum range semantics

Intel defines `ctl_freq_range_t.min` as:

> the minimum frequency below which hardware frequency management will not request frequencies.

Official special values:

~~~text
min = 0
→ allow hardware minimum

min = -1
→ restore factory minimum-frequency limit

output min < 0
→ no external minimum-frequency limit is in effect
~~~

### 3.3 Maximum remains outside Addon ownership

Intel defines `range.max` separately.

PR1 production logic already preserves current max semantics.

PR2 must not add any path that sets:

~~~text
max = target
max = hardwareMax
max = selectableUpper
~~~

### 3.4 Device loss/unavailable is a normal recoverable lifecycle signal

Intel documents:

~~~text
CTL_RESULT_ERROR_DEVICE_LOST       = 0x40000003
CTL_RESULT_ERROR_DEVICE_UNAVAILABLE = 0x40000027
~~~

including device removal, driver update, D3 low-power state, TDR/FLR/recovery.

PR1 already exposes one bounded session-reinitialize primitive for these results.

PR2 should use it only where a real lifecycle recovery warrants it.

No retry manager.

---

## 4. Locked product policy

### 4.1 Product name

Use:

> **Minimum GPU Clock**

Do not call the feature:

- GPU Clock Lock;
- GPU Max Clock;
- GPU Overclock;
- GPU Clock Target.

### 4.2 Default state

Fresh install / absent persisted value:

~~~text
Minimum GPU Clock = Off
~~~

Do not create a persisted record merely by opening the Device page.

### 4.3 Dynamic selectable lower bound

Keep PR1 policy:

~~~text
first actual driver-reported clock >= 1500 MHz
~~~

1500 MHz is a product threshold only.

Do not require an exact 1500 MHz entry.

Example:

~~~text
1475, 1525, 1575...
→ first selectable = 1525
~~~

### 4.4 Dynamic selectable upper bound

Keep PR1 policy:

~~~text
highest actual available clock
→ move down exactly two supported entries
→ selectable upper
~~~

Do not hardcode 2200 MHz.

Example only:

~~~text
2200, 2250, 2300
→ upper = 2200
~~~

Another GPU may produce a different upper value.

### 4.5 Initial AC/DC value

For an uninitialized setting, the initial value for both rails is:

~~~text
PR1 RecommendedDefaultMhz
= selectable upper
= driver max minus two supported entries
~~~

This is the value used when the user first enables an uninitialized feature.

Feature state remains Off until the user enables it. While Off, the AC/DC selectors are disabled, matching the existing Device Performance option policy.

### 4.6 B390 and Lunar Lake 140V use the same path

Do not branch by:

- GPU display name;
- B390 device ID;
- Arc 140V device ID;
- MSI board solely to choose MHz values.

Both use:

~~~text
official IGCL capability
→ actual available clock list
→ PR1 policy
~~~

No model-specific clock table.

### 4.7 Saved value removed by a driver update

Background startup/reconcile must never silently select another frequency.

If a saved value is no longer present in the current selectable list while the feature is Enabled:

~~~text
saved value remains persisted
→ invalid rail is not applied
→ UI reports that the saved value is no longer supported
→ AC/DC selectors remain editable because the feature is On
→ user chooses a current supported value explicitly
~~~

Do not:

- nearest-neighbor clamp;
- move upward automatically;
- move downward automatically;
- rewrite persistence during background startup/resume/reconcile.

If the feature is already Off, its AC/DC selectors remain disabled like the other Device Performance options. In that specific state, an explicit user **Enable** action may reinitialize only unsupported saved rail value(s) to the current `RecommendedDefaultMhz` before enabling. This is an explicit user-triggered reinitialization, not background auto-clamping.

The default is therefore used only for:

- a previously uninitialized setting; or
- an unsupported saved rail when the user explicitly turns the feature back On.

---

## 5. Device persistence

Add one optional field:

~~~csharp
public sealed record DevicePerformanceSettings
{
    public DeviceCpuBoostSettings? CpuBoost { get; init; }
    public DeviceTdpSettings? Tdp { get; init; }
    public DevicePowerModeSettings? PowerMode { get; init; }

    public DeviceGpuMinimumClockSettings? GpuMinimumClock { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
~~~

Add:

~~~csharp
public sealed record DeviceGpuMinimumClockSettings
{
    public bool Enabled { get; init; }

    public required double AcMhz { get; init; }

    public required double DcMhz { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
~~~

### 5.1 Why `double`

IGCL returns available clocks as `double`.

Persist the exact canonical driver value returned by PR1.

Do not round the storage model to an arbitrary integer policy.

The UI may format ordinary integer-valued clocks as:

~~~text
1900 MHz
~~~

but storage/runtime validation remains based on the canonical `double` value and PR1 tolerance.

### 5.2 Null semantics

~~~text
GpuMinimumClock == null
→ feature never initialized
→ effective Device setting Off
~~~

A non-null record always preserves AC and DC together.

### 5.3 No schema migration

This is additive optional persistence.

Do not bump the profile-document schema solely for this field.

Existing extension-data preservation remains authoritative.

---

## 6. Extend the existing runtime — do not create another GPU owner

PR2 should extend:

~~~text
IntelGpuMinimumClockRuntime
~~~

into the one Device production owner.

Do not add:

~~~text
GpuClockManager
GpuTuningManager
IntelPerformanceManager
DeviceGpuClockService
provider registry
vendor abstraction
~~~

The runtime may now receive the same existing dependencies used by other Device performance owners:

~~~csharp
ProfileStore
ProfileMutationGate
Func<AcDcPowerSource?>
IIntelGpuMinimumClockControl
ownership marker path
~~~

Recommended constructor shape conceptually:

~~~csharp
IntelGpuMinimumClockRuntime(
    ProfileStore store,
    ProfileMutationGate mutationGate,
    IIntelGpuMinimumClockControl control,
    Func<AcDcPowerSource?>? powerSource = null,
    string? marker = null)
~~~

Exact signature may follow repository test seams.

Use:

~~~text
WindowsAcDcPowerSource.Read
~~~

as the production power-source reader.

---

## 7. Runtime snapshot

Add one small Device runtime snapshot.

Suggested shape:

~~~csharp
internal sealed record GpuMinimumClockRuntimeSnapshot(
    bool Available,
    bool PersistenceWritable,
    bool Initialized,
    bool Enabled,
    IReadOnlyList<double> SelectableClocksMhz,
    double? AcMhz,
    double? DcMhz,
    double? RecommendedDefaultMhz,
    string? LastFailure);
~~~

Semantics:

~~~text
Available
→ PR1 production capability is available/control-capable

PersistenceWritable
→ latest ProfileStore load is safe to replace

Initialized
→ Device.Performance.GpuMinimumClock != null

Enabled
→ persisted Device setting Enabled

SelectableClocksMhz
→ exact current PR1 selectable clock list

AcMhz / DcMhz
→ persisted desired values only

RecommendedDefaultMhz
→ PR1 dynamic upper/default candidate

LastFailure
→ latest production apply/release/reconcile failure
~~~

Do not expose live `ActualMhz` as a user-setting authority.

ClawHUD remains the observation surface for live GPU clock/power.

---

## 8. Mutation outcomes

Add a narrow mutation result type similar to existing Device performance features.

Recommended:

~~~csharp
internal enum GpuMinimumClockMutationOutcome
{
    Succeeded,
    InvalidTarget,
    PersistenceFailed,
    ApplyFailed,
    Unavailable
}

internal readonly record struct GpuMinimumClockMutationResult(
    GpuMinimumClockMutationOutcome Outcome,
    string? FailureMessage,
    GpuMinimumClockRuntimeSnapshot Snapshot)
{
    public bool Succeeded => Outcome == GpuMinimumClockMutationOutcome.Succeeded;
}
~~~

Do not invent a generalized performance-mutation result abstraction.

---

## 9. Persist-then-apply ordering

Every user mutation follows:

~~~text
load latest profile
→ validate
→ persist desired value
→ only after successful persistence:
   reconcile/apply hardware
~~~

If persistence fails:

~~~text
zero new GPU frequency writes
previous desired state remains authoritative
~~~

If persistence succeeds but hardware apply fails:

~~~text
new desired state remains persisted
do not roll back profile
retain ownership marker when required
future lifecycle reconcile may retry
~~~

This matches the existing product direction used by other Device performance owners.

---

## 10. Set Enabled behavior

Add conceptually:

~~~text
SetDeviceGpuMinimumClockEnabled(bool enabled)
~~~

### 10.1 First enable from uninitialized state

When:

~~~text
GpuMinimumClock == null
enabled == true
~~~

require a valid PR1 capability and `RecommendedDefaultMhz`.

Persist:

~~~text
Enabled = true
AcMhz = RecommendedDefaultMhz
DcMhz = RecommendedDefaultMhz
~~~

Then reconcile the current power rail.

Do not hardcode 2200.

### 10.2 Enable an existing configuration

When enabling an existing record:

- keep every saved AC/DC value that is still a current supported selectable clock;
- for any saved rail that is no longer supported by the current driver, replace **that rail only** with the current `RecommendedDefaultMhz`;
- persist the complete AC/DC record with `Enabled = true`;
- then reconcile the current power rail.

Example:

~~~text
saved while Off:
  AC = 1900   // still supported
  DC = 1850   // no longer supported

current RecommendedDefault = 2000

explicit user Enable
→ AC remains 1900
→ DC becomes 2000
→ Enabled = true
→ persist once
→ apply current power rail
~~~

Do not nearest-neighbor clamp an unsupported value. The explicit Enable action is the recovery boundary because AC/DC selectors are intentionally disabled while the feature is Off.

If capability/`RecommendedDefaultMhz` is unavailable, do not enable and do not alter persistence.

### 10.3 Disable

On user disable:

~~~text
persist Enabled = false first
→ RestoreOriginalMinimum("DeviceDisabled")
→ immediate PR1 readback verification
~~~

If restore succeeds:

~~~text
marker removed
feature Off
success
~~~

If restore fails:

~~~text
persisted setting remains Off
marker remains
return ApplyFailed
future startup/resume/reconcile retries release
~~~

Do not leave the Device setting On merely because restore failed.

Desired policy and actual cleanup state are separate.

---

## 11. AC/DC value mutation

Add conceptually:

~~~text
SetDeviceGpuMinimumClockAc(double mhz)
SetDeviceGpuMinimumClockDc(double mhz)
~~~

### 11.1 Validation

The submitted value must canonicalize to one entry in:

~~~text
IntelGpuMinimumClockRuntime.Capability.SelectableClocksMhz
~~~

using the existing PR1 tolerance.

No arbitrary MHz values.

### 11.2 Off-state edits are rejected

Match the existing Device Performance option policy:

~~~text
Enabled = false
→ AC selector disabled
→ DC selector disabled
~~~

The frontend must not submit AC/DC mutations while Off.

The Runtime must also reject a direct/stale RPC attempt while Off:

~~~text
Enabled = false
SetDeviceGpuMinimumClockAc/Dc(...)
→ no persistence change
→ no SetRange
→ return operation unavailable with a clear "Enable Minimum GPU Clock first" message
~~~

There is no Off-state preconfiguration path.

An uninitialized setting is created only by the explicit Enable action in section 10.1.

### 11.3 Enabled + active rail edit

Example:

~~~text
power source = AC
feature Enabled
user edits AC
→ persist AC
→ ApplyMinimum(new AC target)
~~~

### 11.4 Enabled + inactive rail edit

Example:

~~~text
power source = AC
feature Enabled
user edits DC
→ persist DC
→ no frequency write
→ current AC floor remains unchanged
~~~

Do not unnecessarily reapply the active rail merely because the inactive target changed.

---

## 12. Device reconcile policy

Add one explicit Device reconcile path to the existing runtime.

Conceptually:

~~~text
ReconcileDevice(reason, allowSessionReinitialize = false)
~~~

### 12.1 Load safety

Acquire the existing:

~~~text
ProfileMutationGate.Sync
~~~

then load the current profile.

If:

~~~text
CanSafelyReplace == false
~~~

do not persist anything.

If an ownership marker exists, attempt a best-effort verified restore to the original minimum because the desired policy cannot be trusted.

If restore cannot run or fails:

- keep marker;
- report failure;
- do not invent a target.

### 12.2 Setting missing or disabled

~~~text
GpuMinimumClock == null
OR
Enabled == false
~~~

means:

~~~text
no Device floor should be owned
~~~

If a marker exists:

~~~text
RestoreOriginalMinimum(reason)
~~~

Otherwise no frequency write.

### 12.3 Setting enabled + power source known

~~~text
AC
→ use persisted AcMhz

DC
→ use persisted DcMhz
~~~

The current-side value must still be a current selectable clock.

If valid:

~~~text
ApplyMinimum(target, reason)
~~~

PR1 captures/preserves the original min before first ownership.

### 12.4 Setting enabled + active target no longer supported

Do not clamp.

If a marker exists:

~~~text
restore original minimum
→ verify
→ clear marker only on verified restore
~~~

Keep the persisted unsupported value unchanged.

Report that the selected value is no longer supported by the current Intel driver.

### 12.5 Unknown power source

If power source is unknown:

- do not guess AC;
- do not guess DC.

If a marker exists, release the owned minimum back to the original state.

If no marker exists, do nothing.

A later known power-source event may reapply the enabled desired setting.

---

## 13. Startup behavior

Replace the PR1 discovery-only host call with production startup reconcile.

Current:

~~~text
InitializeIntelGpuMinimumClockReadOnlyForStartup()
~~~

PR2 target:

~~~text
IntelGpuMinimumClockRuntime.StartupReconcile()
~~~

Required sequence:

~~~text
InitializeReadOnly()
→ read current driver capability / available clocks
→ load Device setting
→ reconcile Device policy
~~~

### 13.1 Fresh install / feature Off

~~~text
no marker
setting absent/Off
→ no SetRange
~~~

### 13.2 Enabled setting after controlled Runtime restart

~~~text
marker exists
setting Enabled
→ keep original baseline from marker
→ do NOT overwrite original baseline
→ apply current AC/DC desired floor
~~~

### 13.3 Enabled setting after crash

Same policy.

The marker is the durable evidence from the original ownership transaction.

Do not create a second crash journal.

### 13.4 Stale marker + setting Off/missing

~~~text
restore original minimum
→ verify
→ delete marker
~~~

This is a real crash/restart recovery path.

---

## 14. AC/DC lifecycle

Extend the existing:

~~~text
OnAcDcPowerSourceChanged()
~~~

Do not create another watcher.

Required:

~~~text
AC/DC changed
→ Intel FPS reconcile
→ Minimum GPU Clock Device reconcile
→ existing frontend power-source notification
~~~

The order between Intel FPS and GPU minimum floor is not a new authority concern; both are independent low-frequency operations.

No retry loop.

No debounce state machine is needed for the OS power-source event.

---

## 15. Sleep / Hibernate / Resume

Use the existing `PowerResumeObserved` flow.

After the existing bounded performance settle:

~~~text
~2.5 s
→ CPU Boost
→ Power Mode
→ Minimum GPU Clock
→ Battery Charge Limit
~~~

Equivalent nearby order is acceptable.

### 15.1 One bounded IGCL session recovery

For Resume only, if the Minimum GPU Clock operation reports either:

~~~text
0x40000003 DEVICE_LOST
0x40000027 DEVICE_UNAVAILABLE
~~~

through PR1's operation result:

~~~text
TryReinitializeSession(nativeResult)
→ rediscover adapter/domain/properties/available clocks
→ recompute current selectable policy
→ retry Device reconcile once
~~~

No second retry.

No timer.

No background health loop.

### 15.2 Available clock list changed after Resume/driver recovery

After reinitialize, current capability is authoritative.

If the saved active target disappeared:

~~~text
do not choose a nearby clock
→ fail closed
→ restore existing owned minimum if possible
→ keep saved value unchanged
→ report unsupported saved value
~~~

---

## 16. Controlled shutdown / restart

Keep PR1 policy:

~~~text
normal Runtime shutdown
→ close IGCL session
→ DO NOT restore merely because process exits
→ keep durable ownership marker
~~~

The replacement Runtime will reconcile the persisted setting.

Do not copy IntelFrameLimiter's shutdown-release behavior to Minimum GPU Clock.

These two features have intentionally different ownership contracts.

---

## 17. Safe uninstall must release the production minimum floor

PR2 introduces real production writes, so uninstall must not leave an Addon-owned external minimum frequency behind.

Before file removal, use the existing stock-safe Runtime uninstall path.

Required behavior:

~~~text
PrepareForUninstallAsync
→ if no GPU minimum ownership marker:
     no GPU work
→ if marker exists:
     initialize/reinitialize IGCL capability if necessary
     RestoreOriginalMinimum("Uninstall")
     verify readback
     remove marker only on verified restore
→ only after GPU release succeeds:
     continue existing Full1902 stock-authority uninstall preparation
~~~

If GPU restore fails:

~~~text
fail uninstall preparation
keep marker
do not delete product files
~~~

This is a real teardown obligation, not optional cleanup.

### 17.1 Headless uninstall path

Current safe uninstall may construct a headless Runtime and does not run normal Device/Profile startup.

Therefore uninstall release must not assume:

~~~text
ReconcileDeviceProfileStartup()
~~~

already initialized the GPU runtime.

The uninstall path must be able to:

~~~text
marker exists
→ InitializeReadOnly()
→ RestoreOriginalMinimum()
~~~

directly.

Do not run Device desired-state apply during uninstall.

### 17.2 No rollback if later controller stock preparation fails

If GPU minimum release succeeds but a later existing Full1902 stock-authority step fails:

- leave GPU minimum safely restored;
- do not reapply the Device setting solely to roll back uninstall preparation.

The next normal Runtime startup will reconcile the still-persisted Device setting if the application remains installed.

Do not build transactional rollback across unrelated controller/GPU authorities.

---

## 18. Main App frontend contract

Add a typed frontend snapshot.

Recommended:

~~~csharp
public sealed record FrontendGpuMinimumClockSnapshot(
    bool Available,
    bool PersistenceWritable,
    bool Initialized,
    bool Enabled,
    IReadOnlyList<double> SelectableClocksMhz,
    double? AcMhz,
    double? DcMhz,
    double? RecommendedDefaultMhz,
    string? LastFailure)
{
    public static readonly FrontendGpuMinimumClockSnapshot Unavailable = ...;
}
~~~

Add mutation outcome/result:

~~~csharp
public enum FrontendGpuMinimumClockMutationOutcome
{
    Succeeded,
    InvalidTarget,
    PersistenceFailed,
    ApplyFailed,
    Unavailable
}

public sealed record FrontendGpuMinimumClockMutationResult(
    FrontendGpuMinimumClockMutationOutcome Outcome,
    string? FailureMessage,
    FrontendGpuMinimumClockSnapshot Snapshot)
{
    public bool Succeeded => Outcome == FrontendGpuMinimumClockMutationOutcome.Succeeded;
}
~~~

---

## 19. Extend the existing Device aggregate

Extend:

~~~csharp
FrontendDeviceQuickSettingsSnapshot
~~~

with:

~~~text
GpuMinimumClock
~~~

Recommended order:

~~~csharp
public sealed record FrontendDeviceQuickSettingsSnapshot(
    FrontendCpuBoostSnapshot CpuBoost,
    FrontendTdpSnapshot Tdp,
    FrontendPowerModeSnapshot PowerMode,
    FrontendGpuMinimumClockSnapshot GpuMinimumClock,
    FrontendBatteryChargeLimitSnapshot BatteryChargeLimit)
~~~

Update `Unavailable`.

### Important

This does **not** mean PR2 exposes the feature in Quick Settings/Overlay.

The aggregate is also the Main Device page refresh transport.

PR2 extends transport truth now; shared Quick Settings presentation remains unchanged.

---

## 20. Focused frontend methods

Add:

~~~csharp
Task<FrontendGpuMinimumClockSnapshot> CaptureGpuMinimumClockAsync(...)

Task<FrontendGpuMinimumClockMutationResult>
    SetDeviceGpuMinimumClockEnabledAsync(bool enabled, ...)

Task<FrontendGpuMinimumClockMutationResult>
    SetDeviceGpuMinimumClockAcAsync(double mhz, ...)

Task<FrontendGpuMinimumClockMutationResult>
    SetDeviceGpuMinimumClockDcAsync(double mhz, ...)
~~~

`CaptureDeviceQuickSettingsAsync` should capture the GPU child alongside the current four children.

One child failure remains isolated.

Do not let an IGCL failure make CPU Boost/TDP/Power/Battery unavailable.

All successful/failed user mutations should return a fresh authoritative GPU snapshot and raise the existing `StateInvalidated` notification.

---

## 21. Frontend protocol bump

Bump:

~~~text
FrontendTransportProtocol.CurrentVersion
61 → 62
~~~

Reason:

- `FrontendDeviceQuickSettingsSnapshot` shape changes;
- new GPU snapshot/result contracts are serialized;
- new capture/mutation RPCs are added.

Add `FrontendRpcMethod` values for:

~~~text
CaptureGpuMinimumClock
SetDeviceGpuMinimumClockEnabled
SetDeviceGpuMinimumClockAc
SetDeviceGpuMinimumClockDc
~~~

Add typed request payloads for the AC/DC `double Mhz` values.

Update:

- NamedPipeAddonFrontendClient;
- NamedPipeAddonFrontendServer;
- transport tests;
- protocol-version comments.

No compatibility shim is required; product is pre-release.

---

## 22. Do not bump Overlay protocol

Keep:

~~~text
OverlayTransportProtocol.CurrentVersion = 17
~~~

PR2 must not add:

- `QuickSettingsSectionId.DeviceGpuMinimumClock`;
- GPU rows;
- GPU mutation intents;
- Overlay renderer code.

Current shared Device Quick Settings presentation remains:

~~~text
TDP
CPU Boost
Windows Power Mode
Battery Charge Limit
~~~

even though the underlying aggregate now contains a GPU child for Main UI use.

Add/adjust tests to prove PR2 does not accidentally expose a new Overlay/Quick Settings row.

Quick Settings/Overlay GPU exposure is deferred.

---

## 23. Main Device UI

Add a new card/expander to:

~~~text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
~~~

Recommended position:

~~~text
TDP Control
Minimum GPU Clock
CPU Boost
Windows Power Mode
Battery charge limit
~~~

This keeps GPU floor adjacent to package-power control.

### 23.1 Header

~~~text
Minimum GPU Clock
~~~

### 23.2 Description

Use a concise description equivalent to:

> Prevents unnecessary GPU downclocking below the selected frequency. Power and thermal limits may still reduce the actual GPU clock.

Do not claim that the frequency is guaranteed.

### 23.3 Controls

Use:

~~~text
Toggle: Enabled

Plugged in
  [value text] [discrete index slider]

On battery
  [value text] [discrete index slider]
~~~

### 23.4 Slider is index-based

Do not configure the WinUI slider as a continuous MHz range.

Given:

~~~text
SelectableClocksMhz = [1525, 1575, 1625, ..., 2175]
~~~

configure:

~~~text
Slider.Minimum = 0
Slider.Maximum = Count - 1
Slider.StepFrequency = 1
~~~

Then:

~~~text
index 0 → 1525 MHz
index 1 → 1575 MHz
...
~~~

The displayed MHz comes from the list entry.

The UI must never synthesize an unsupported clock between entries.

### 23.5 Uninitialized presentation

If:

~~~text
Initialized == false
~~~

use:

~~~text
RecommendedDefaultMhz
~~~

as the **uncommitted draft** for both rails.

Do not persist merely because the page rendered.

Toggle remains Off.

### 23.6 Off-state sliders are disabled

Match CPU Boost, TDP, and Windows Power Mode behavior:

~~~text
Enabled = false
→ Plugged in slider disabled
→ On battery slider disabled
~~~

The saved AC/DC values remain visible when valid, but are not editable until the user enables the feature.

For an uninitialized setting, show the current `RecommendedDefaultMhz` as the disabled draft value for both rails. Rendering this draft must not persist anything.

### 23.7 Removed saved value

If a persisted AC/DC value does not map to the current selectable list:

- do not display it as a valid selected clock;
- show a warning;
- use `RecommendedDefaultMhz` only as an uncommitted draft for that invalid rail;
- never silently write the draft during render or background reconcile.

If the feature is **Enabled**, the sliders remain editable and the user may explicitly commit a supported replacement.

If the feature is **Off**, the sliders remain disabled. The user recovers by explicitly turning the feature On; section 10.2 then reinitializes only unsupported rail value(s) to the current `RecommendedDefaultMhz` as part of that explicit Enable transaction.

### 23.8 Commit policy

Do not issue native SetRange calls for every pointer movement.

Use the existing simple Device UI pattern:

~~~text
ValueChanged
→ update draft/value text only

PointerCaptureLost
or keyboard commit boundary
→ persist selected rail
→ active rail applies if Enabled
~~~

A small trailing debounce is also acceptable if it reuses existing Device-page machinery, but do not introduce another generic debounce manager.

---

## 24. UI mutation busy/error behavior

Use one narrow GPU mutation busy flag.

While a GPU mutation is in flight:

- disable the toggle and both GPU sliders;
- do not disable unrelated Device controls.

On result:

~~~text
Succeeded
→ render returned snapshot

PersistenceFailed
→ error InfoBar

ApplyFailed
→ warning InfoBar

InvalidTarget
→ warning InfoBar explaining the driver no longer advertises that clock

Unavailable
→ disabled control + warning
~~~

On frontend/pipe exception:

- show Runtime connection error;
- refresh authoritative Device aggregate if possible;
- do not keep speculative slider/toggle state.

---

## 25. Developer IGCL probe coexistence

The Developer probe and production Device feature must not write the same frequency range simultaneously.

Do **not** build a generic owner-arbitration framework.

Use two narrow checks in the existing frontend/runtime boundary.

### 25.1 Production authority blocks Developer frequency mutation

If:

~~~text
Device Minimum GPU Clock is enabled
OR
production ownership marker exists
~~~

then Developer frequency mutations:

~~~text
Set Max / Max
Restore Original Frequency
~~~

must be refused.

Developer read/capture remains available.

Developer PL1 test remains independent.

### 25.2 Existing Developer mutation blocks production enable/apply

If the current in-process Developer probe reports:

~~~text
ModifiedByProbe == true
~~~

a user attempt to enable/apply Device Minimum GPU Clock must fail without persisting a new enabled state.

Return a clear message:

~~~text
Restore the Developer GPU frequency probe before enabling Minimum GPU Clock.
~~~

No new manager/interface is needed.

This is a direct conflict check between the only two in-process writers.

### 25.3 Do not solve pathological developer crash history

Do not add a second durable journal for the Developer probe solely to defend against an artificial sequence such as:

~~~text
Developer Max/Max write
→ hard kill process
→ next boot production enable
~~~

The Developer probe remains diagnostic-only.

Production lifecycle safety remains fully durable through its own PR1 marker.

---

## 26. Runtime host wiring

### Construction

Construct `IntelGpuMinimumClockRuntime` with:

- existing `ProfileStore`;
- existing `ProfileMutationGate`;
- existing PR1 control;
- existing ownership-marker path;
- production AC/DC reader.

Pass the same runtime instance to `InProcessAddonFrontendControl`.

Do not create another instance for frontend operations.

### Startup

Replace the PR1 read-only host helper with Device startup reconcile.

### AC/DC

Add GPU reconcile to `OnAcDcPowerSourceChanged`.

### Resume

Add one GPU resume reconcile callback to the existing delayed performance path.

### Shutdown

Keep:

~~~text
BeginShutdown
Dispose
~~~

and do not restore solely for ordinary process exit.

### Uninstall

Add the narrow GPU ownership-release pre-step described above.

---

## 27. Runtime reconcile details

Recommended internal methods, equivalent naming accepted:

~~~text
StartupReconcile()
ReconcileDevice(reason)
ReconcileAfterResume()
CaptureSnapshot()
SetEnabled(bool)
SetAc(double)
SetDc(double)
PrepareForUninstall()
~~~

Do not add a generalized reconcile scheduler.

### 27.1 Resume retry helper

A tiny helper is acceptable:

~~~text
TryReconcileOnceAfterSessionRecovery(...)
~~~

only if it implements:

~~~text
first operation fails with DeviceLost/DeviceUnavailable
→ TryReinitializeSession once
→ reconcile once more
→ stop
~~~

No loop.

---

## 28. Persistence and marker invariants

The following must always remain true.

### Before first production write

~~~text
original minimum marker is durable first
→ only then SetRange
~~~

### After verified apply

~~~text
marker remains
~~~

because the Addon still owes a future restore.

### After write success + readback failure

~~~text
marker remains
~~~

### After setting Off + verified restore

~~~text
marker removed
~~~

### After setting Off + failed restore

~~~text
setting Off persisted
marker remains
~~~

### Controlled restart

~~~text
marker remains
original baseline is not recaptured
~~~

### Crash

~~~text
marker remains
next Runtime adopts it
~~~

Do not overwrite original-minimum evidence on re-entry.

---

## 29. No automatic interaction with TDP

TDP and Minimum GPU Clock remain separate manual controls.

Do not implement:

~~~text
GPU clock floor → automatically raise TDP
GPU power → dynamically change TDP
PL1 throttle → reduce selected minimum
ActualMhz mismatch → retry/reassert repeatedly
~~~

The user may choose both manually.

Intel remains responsible for enforcing the actual package-power envelope.

A selected minimum is a requested floor, not a guarantee.

---

## 30. Tests — persistence

Add tests for:

~~~text
DeviceGpuMinimumClockSettings round-trip
absent legacy field remains null / Off
unknown extension data survives
Enabled false preserves AC/DC values
double MHz values round-trip exactly enough for PR1 canonical tolerance
~~~

No profile schema bump.

---

## 31. Tests — first enable/default

Use synthetic driver tables.

Verify:

~~~text
B390-like:
  ... 2200, 2250, 2300
  → RecommendedDefault = 2200

different 140V-like table:
  ... X, Y, Z
  → RecommendedDefault = third from highest
~~~

Then:

~~~text
uninitialized + Enable
→ AC = RecommendedDefault
→ DC = RecommendedDefault
→ Enabled = true
→ current rail ApplyMinimum called
~~~

No test may encode 2200 as a universal expected constant.

---

## 32. Tests — Off-state gating

Verify UI policy:

~~~text
feature Off
→ AC slider disabled
→ DC slider disabled
~~~

Verify Runtime fail-closed behavior for stale/direct RPCs:

~~~text
feature Off
Set AC
→ persistence unchanged
→ zero SetRange
→ operation rejected

feature Off
Set DC
→ persistence unchanged
→ zero SetRange
→ operation rejected
~~~

Verify uninitialized state cannot be created by an AC/DC mutation while Off.

Verify explicit Enable is the initialization/recovery boundary:

~~~text
uninitialized + Enable
→ AC = RecommendedDefault
→ DC = RecommendedDefault
→ Enabled = true

existing Off configuration
+ one unsupported saved rail
+ Enable
→ supported rail preserved
→ unsupported rail = RecommendedDefault
→ Enabled = true
→ one persisted complete record
~~~

---

## 33. Tests — active/inactive rail behavior

With AC power:

~~~text
Enabled
Set AC
→ persist + SetRange

Enabled
Set DC
→ persist only
→ no SetRange
~~~

Repeat inverse for DC power.

---

## 34. Tests — disable/release

Verify:

~~~text
Enabled + marker
→ SetEnabled(false)
→ persistence says Off before restore
→ RestoreOriginalMinimum
→ verified restore removes marker
~~~

Failure:

~~~text
restore fails
→ profile still Off
→ marker remains
→ result ApplyFailed
~~~

---

## 35. Tests — unsupported saved values

Verify:

~~~text
saved target removed from current available list
→ no nearest substitution
→ no automatic persistence rewrite
→ current-side apply not attempted
→ owned marker restored if possible
→ snapshot reports failure
~~~

Verify an invalid rail is editable while the feature is Enabled.

Verify that when the feature is Off, AC/DC controls are disabled and an explicit Enable reinitializes only unsupported rail value(s) to the current RecommendedDefault before applying.

---

## 36. Tests — startup/restart/crash

### Fresh Off

~~~text
no marker
setting null/Off
→ zero SetRange
~~~

### Startup Enabled

~~~text
valid AC/DC setting
known power source
→ ApplyMinimum current side
~~~

### Controlled replacement

~~~text
marker with original min
next runtime starts
→ does not overwrite marker original min
→ reapplies current desired target
~~~

### Crash recovery

Same observable contract.

### Stale marker + Off

~~~text
→ RestoreOriginalMinimum
→ clear marker only when verified
~~~

---

## 37. Tests — AC/DC lifecycle

Drive the existing host callback seam.

Verify:

~~~text
AC → DC notification
→ one GPU reconcile
→ DC target applied

DC → AC
→ one GPU reconcile
→ AC target applied
~~~

No new watcher.

---

## 38. Tests — Resume

Simulate:

~~~text
first GPU operation
→ DEVICE_UNAVAILABLE
→ one Reinitialize
→ capability rediscovered
→ one retry
~~~

Assert:

~~~text
ReinitializeCalls == 1
retry count == 1
~~~

If retry fails:

~~~text
stop
~~~

If rediscovered available clocks no longer contain saved target:

~~~text
no clamp
restore owned state if possible
report unsupported target
~~~

---

## 39. Tests — uninstall

Verify:

~~~text
no marker
→ uninstall GPU step is no-op
→ existing stock prepare continues
~~~

and:

~~~text
marker exists
→ headless path initializes GPU runtime
→ restore verified
→ marker removed
→ existing stock prepare continues
~~~

Failure:

~~~text
restore fails
→ uninstall preparation fails
→ marker remains
→ no file-removal success path
~~~

Do not create a cross-authority rollback test/state machine.

---

## 40. Tests — frontend/transport

Bump expected protocol:

~~~text
61 → 62
~~~

Add round-trip coverage for:

- `FrontendGpuMinimumClockSnapshot`;
- Device aggregate with GPU child;
- capture RPC;
- Enabled mutation RPC;
- AC mutation RPC;
- DC mutation RPC;
- mutation result.

Update Main UI/test doubles implementing `IAddonFrontendControl`.

---

## 41. Tests — Device UI

Add focused UI policy tests where practical.

Verify:

~~~text
slider range is 0..Count-1
slider step = 1
index maps to exact SelectableClocksMhz entry
no continuous unsupported MHz can be submitted
uninitialized draft uses RecommendedDefault
opening page does not persist
Off-state AC/DC sliders are disabled
no AC/DC mutation is submitted while Off
invalid persisted clock is not presented as a valid saved selection
Enabled invalid rail can be explicitly corrected
Off invalid rail is recovered only through explicit Enable reinitialization
~~~

Do not test guessed real 140V clock values.

---

## 42. Tests — Quick Settings/Overlay deferral

Update any aggregate constructor tests, but assert product rows remain unchanged.

PR2 must still render exactly the current Device Quick Settings sections:

~~~text
TDP
CPU Boost
Windows Power Mode
Battery Charge Limit
~~~

No GPU row.

Keep:

~~~text
OverlayTransportProtocol.CurrentVersion = 17
~~~

---

## 43. Tests — Developer probe conflict

Verify:

~~~text
production Enabled/marker owned
→ Developer SetMaxMax refused
→ Developer frequency restore/write refused as appropriate
→ read-only capture still works
→ PL1 operations unaffected
~~~

and:

~~~text
Developer probe ModifiedByProbe
→ production Enable/apply refused
→ profile is not persisted Enabled
~~~

Do not add a generalized owner table.

---

## 44. Expected files to change

Likely:

~~~text
src/SteamInputAddonforClaw/Profiles/DeviceSettings.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs

tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/ProfileStoreTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/... relevant host/frontend tests
tests/SteamInputAddonforClaw.UiTests/... relevant Device page policy tests
~~~

Only touch Quick Settings presentation/tests as necessary to preserve the explicit “no GPU row in PR2” contract.

Do not change GameProfile/XboxGameProfile GPU fields in this PR.

---

## 45. Hardware validation — B390 / CG3EM

On MSI Claw 8 EX AI+ / B390, validate this sequence.

### A. Off baseline

~~~text
feature Off
→ no production frequency marker
→ no Addon minimum floor
~~~

### B. First enable

Open Device page.

Verify:

- clock list came from IGCL;
- lower is first supported >= 1500;
- upper/default is max minus two supported entries;
- no hardcoded 2200 path.

Enable.

Verify:

~~~text
AC/DC defaults persisted to RecommendedDefault
current rail requested minimum applied
GetRange readback verifies min
max semantics unchanged
marker created
~~~

### C. Slider

Move current rail one or more supported entries lower.

Verify:

~~~text
only a driver-advertised value is submitted
min readback matches
max remains unchanged
~~~

### D. AC/DC

Configure different AC and DC targets.

Physically switch power source.

Verify current minimum follows the correct saved rail.

### E. Disable

Verify:

~~~text
original minimum restored exactly
max unchanged
marker removed
~~~

### F. Controlled Runtime restart while Enabled

Verify:

~~~text
original marker baseline survives
new Runtime does not capture the current Addon min as a new baseline
desired floor is reconciled
~~~

### G. Forced Runtime kill while Enabled

Restart Addon.

Verify the same durable recovery behavior.

### H. Sleep/Hibernate/Resume

Verify:

- no crash/hang;
- GPU minimum target is reconciled once after settle;
- if IGCL returns D3/device unavailable, one session reinitialize occurs;
- no retry loop.

---

## 46. Hardware validation — Lunar Lake / Arc 140V / A2VM

On MSI Claw 7/8 AI+ A2VM:

Verify the **same code path**.

Required evidence:

~~~text
actual 140V available-clock list
dynamic lower
dynamic max-minus-two upper/default
Device UI discrete slider values
minimum-only write/readback
max preservation
AC/DC switch
disable restore
resume
~~~

Do not add a 140V-specific MHz table after testing.

If 140V returns a different number/spacing of frequencies, that is expected.

The product policy must remain capability-driven.

---

## 47. Logging

Keep PR1 category:

~~~text
Profiles.IntelGpuMinimumClock
~~~

Add low-frequency Device context where useful:

~~~text
Reason
Enabled
PowerSource
DesiredAcMhz
DesiredDcMhz
EffectiveTargetMhz
PersistenceWritable
CapabilityAvailable
OwnershipMarkerPresent
Outcome
Failure
~~~

Do not add polling/per-frame logs.

Startup should still log the PR1 capability once.

Mutation/reconcile logs should be one record per actual operation.

---

## 48. Explicit non-goals

PR2 must not add:

~~~text
GamePerformanceOverrides.GpuMinimumClock
Steam per-game GPU minimum
XBOX per-game GPU minimum
ActiveProfileResolver GPU-clock logic
Quick Settings GPU row
Overlay GPU row
Overlay protocol bump
GPU maximum frequency control
GPU voltage control
GPU PL1 / PL2 / PL4 production write
dynamic TDP adjustment
GPU-power feedback loop
ClawHUD control dependency
PresentMon control dependency
hardcoded 2200 MHz
hardcoded 140V clock table
continuous raw-MHz slider
nearest supported-clock auto clamp
background polling
periodic reassert
retry manager
resume epoch/state machine
generic GPU provider abstraction
AMD/NVIDIA architecture
new process/service/helper
controller/HidHide/VIIPER changes
~~~

---

## 49. PR3 boundary

PR3 will add the game-specific override.

Expected later work:

~~~text
GamePerformanceOverrides.GpuMinimumClock
Steam profile persistence/UI
XBOX profile persistence/UI
ActiveProfileResolver priority:
  enabled active game override
  else Device
Steam/XBOX active-target transition reconcile
Profile Quick Settings / Overlay projection if selected for that PR
~~~

PR2 must leave a clean single Device owner that PR3 can extend without redesign.

---

## 50. Acceptance criteria

PR2 is complete when all are true:

1. `DevicePerformanceSettings` has one optional Minimum GPU Clock setting.
2. Fresh/absent state is Off.
3. AC and DC persist exact canonical IGCL clock values.
4. No hardcoded 2200 MHz production default exists.
5. Initial uninitialized AC/DC draft/default is PR1 `RecommendedDefaultMhz`.
6. B390 and Arc 140V use the same capability-driven code path.
7. Main Device UI uses a discrete index slider backed only by current driver-reported selectable clocks.
8. Off-state AC/DC sliders are disabled, matching the other Device Performance options.
9. User Enable persists first, then applies the current AC/DC rail.
10. User Disable persists Off first, then restores the exact original minimum.
11. Restore failure keeps the marker and returns ApplyFailed.
12. Active-rail edit applies; inactive-rail edit only persists.
13. Background startup/resume/reconcile never auto-clamps or rewrites unsupported saved values; only an explicit user Enable may reinitialize unsupported Off-state rail value(s) to the current `RecommendedDefaultMhz`.
14. Startup Enabled state reuses the existing marker baseline instead of recapturing it.
15. Startup Off/missing state restores any stale marker-owned minimum.
16. AC/DC changes reuse the existing power-source watcher.
17. Resume reuses the existing power lifecycle and performs at most one DeviceLost/DeviceUnavailable session reinitialize + one retry.
18. Normal Runtime shutdown does not restore solely because the process exits.
19. Safe uninstall restores any owned minimum before file removal and fails closed if verified release cannot be completed.
20. Developer frequency writes cannot conflict with active production ownership, without adding a generic authority framework.
21. Frontend protocol is bumped from 61 to 62.
22. Main Device aggregate carries the GPU child.
23. Quick Settings/Overlay still expose no GPU Minimum Clock rows.
24. Overlay protocol remains 17.
25. Existing CPU Boost/TDP/Power Mode/Battery behavior is unchanged.
26. Existing Intel FPS limiter remains independent.
27. Existing Full1902 controller startup/ownership lifecycle is unchanged.
28. CI contains no physical GPU write.
29. B390 hardware validation passes.
30. Arc 140V uses the same implementation and is physically validated before claiming completed 140V support.

---

## 51. Implementation principle

The final PR2 ownership model should remain simple:

~~~text
Driver
→ reports exact supported frequency table

PR1 core
→ derives safe selectable subset
→ owns minimum-only native mutation and durable restore evidence

PR2 Device setting
→ persists Off/On + AC/DC desired values
→ applies only the current rail
→ restores when Device ownership is released

Intel hardware
→ still decides actual resolved frequency under power/thermal/current constraints
~~~

One owner.

One marker.

One AC/DC source.

One startup/reconcile path.

No tuning framework.
