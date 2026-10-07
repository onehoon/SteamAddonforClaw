# Work Order — PR3: Steam/XBOX Per-Game Minimum GPU Clock Override + Effective Priority

> **Date:** 2026-10-07  
> **Repository:** onehoon/SteamAddonforClaw  
> **Reviewed main:** main@42092775c7509ecc578816013a321657669507d0  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** docs/Full 1902 Implementation/README.md and its active precedence chain  
> **XBOX profile authority:** docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md  
> **PR1 work order:** docs/work-order/PR1_INTEL_MINIMUM_GPU_CLOCK_PRODUCTION_CORE_WORK_ORDER_2026-10-07.md  
> **PR2 work order:** docs/work-order/PR2_DEVICE_MINIMUM_GPU_CLOCK_PERSISTENCE_UI_LIFECYCLE_WORK_ORDER_2026-10-07.md  
> **PR2 implementation:** PR #704, squash merge 42092775c7509ecc578816013a321657669507d0  
> **Official Intel API baseline:** intel/drivers.gpu.control-library include/igcl_api.h, master@b6c462933502e13d1537dd5024949a51be30e63d  
> **Intel upstream checked:** 2026-10-07; master still resolves to b6c462933502e13d1537dd5024949a51be30e63d  
> **Current frontend protocol:** FrontendTransportProtocol.CurrentVersion = 62  
> **Current Overlay protocol:** OverlayTransportProtocol.CurrentVersion = 17  

---

## 1. Goal

Extend the already-merged Device Minimum GPU Clock feature into one optional per-game override shared by Steam and XBOX profiles.

The effective policy is:

~~~text
enabled active Steam/XBOX profile
+ per-game Minimum GPU Clock enabled
        ↓
Game AC/DC minimum GPU clock

otherwise

Device Minimum GPU Clock enabled
        ↓
Device AC/DC minimum GPU clock

otherwise
        ↓
Addon does not own a GPU minimum
        ↓
restore the exact pre-Addon minimum if an ownership marker exists
~~~

This PR must **not** create a second GPU-frequency owner.

The one production owner remains:

~~~text
IntelGpuMinimumClockRuntime
→ one IGCL frequency session
→ one production ownership marker
→ one minimum-only SetRange path
→ one verified restore path
~~~

PR3 adds profile persistence, effective-source selection, Steam/XBOX frontend editing, and Profile Quick Settings/Overlay projection on top of that owner.

---

## 2. Product intent

Minimum GPU Clock is a floor, not a lock.

The user selects a lower bound to reduce unnecessary downclocking in games that benefit from it.

Example:

~~~text
hardware-supported range: 100 ... 2300 MHz
selected minimum:         1900 MHz

normal requested range:
1900 ... existing/unmanaged maximum
~~~

Intel power, thermal, current, firmware, and hardware limits remain authoritative.

Therefore:

~~~text
requested minimum = 1900
actual frequency  = 1800
power/thermal limiter active
~~~

is not by itself an Addon SetRange failure if the frequency-range readback is correct.

Do not add reassert loops or fight the Intel governor.

---

## 3. Reviewed current main facts

The following are concrete facts from main@42092775c7509ecc578816013a321657669507d0.

### 3.1 PR1/PR2 already own the native frequency path

IntelGpuMinimumClockRuntime already provides:

~~~text
InitializeReadOnly
StartupReconcile
CaptureDeviceSnapshot
SetEnabled
SetAc
SetDc
ReconcileDevice
ReconcileAfterResume
PrepareForUninstall
BlocksDeveloperFrequencyMutation
TryReinitializeSession
ApplyMinimum
RestoreOriginalMinimum
BeginShutdown
~~~

The PR1 policy already derives the selectable table from the driver:

~~~text
normalize ctlFrequencyGetAvailableClocks()
lower = first supported entry >= approximately 1500 MHz
upper = third-highest supported entry
default = upper
~~~

No hardcoded B390 or 140V frequency table exists.

### 3.2 Current minimum-only native write is correct

The production write preserves maximum semantics:

~~~csharp
request = new(
    canonicalTargetMhz,
    current.Max >= 0 ? current.Max : -1);
~~~

The production restore likewise restores only the original minimum while preserving the then-current maximum semantics.

PR3 must not redesign this native primitive.

### 3.3 One durable marker already protects restart/crash recovery

The current production marker owns the original minimum baseline.

It is intentionally retained across ordinary Runtime shutdown/restart and removed only after a verified release/restore.

PR3 must reuse the same marker across Device ↔ Game source changes.

Do not add:

- per-game marker files;
- source-specific markers;
- a second recovery journal;
- a “Device owner marker” plus “Game owner marker”.

### 3.4 Steam and XBOX already converge at ActiveProfileResolver

Current:

~~~csharp
internal readonly record struct ResolvedActiveProfile(
    string TargetLabel,
    GamePerformanceOverrides Performance,
    GameDisplayOverrides Display);
~~~

ActiveProfileResolver already returns the enabled active Steam or XBOX profile using the correct platform identity.

Both GameProfile and XboxGameProfile use the same GamePerformanceOverrides.

This is exactly the seam PR3 should consume.

Do not create:

~~~text
SteamGpuClockResolver
XboxGpuClockResolver
GpuProfileManager
GpuProfileAuthority
~~~

### 3.5 Existing active-target lifecycle is already event-driven

AddonProcessHost already routes:

~~~text
Steam RunningAppId change
→ ReconcileEffectiveGameProfile(...)

XBOX ActiveGame change
→ ReconcileEffectiveGameProfile(...)
~~~

That shared host reconcile already drives CPU Boost, TDP, Windows Power Mode, Intel FPS Limit, and Display Resolution.

PR3 only adds Minimum GPU Clock to that existing path.

No new game watcher is required.

### 3.6 Main Profile editors already have the desired product pattern

Steam and XBOX pages already use:

~~~text
whole profile Enabled
→ feature toggle editable

feature toggle Off
→ child AC/DC controls not editable

feature toggle On
→ child AC/DC controls editable
~~~

Use the same pattern for Minimum GPU Clock.

### 3.7 Shared Profile Quick Settings already supports Steam and XBOX

The shared Profile Quick Settings contract already carries QuickSettingsProfileTarget:

~~~text
Steam → Steam AppId
XBOX  → canonical Xbox game key
~~~

The Overlay renders the same generic Profile Quick Settings page.

PR3 should extend that existing closed contract, not add a GPU-specific Overlay page.

---

## 4. Locked architecture

The production ownership chain after PR3 is:

~~~text
ProfileDocument
    │
    ├─ active Steam/XBOX GamePerformanceOverrides.GpuMinimumClock
    │
    └─ Device.Performance.GpuMinimumClock
    │
    v
IntelGpuMinimumClockRuntime
    │
    ├─ choose one effective source
    ├─ choose current AC/DC rail
    ├─ validate against current driver selectable clocks
    ├─ persist/reuse one original-minimum marker
    ├─ minimum-only ctlFrequencySetRange
    └─ verified readback / restore
~~~

There is one effective source at a time.

Do not combine values.

Specifically, do not implement:

~~~text
max(DeviceMin, GameMin)
min(DeviceMin, GameMin)
Device AC + Game DC mixing
Device floor plus “game delta”
priority weights
merged policy objects
~~~

The rule is binary:

~~~text
valid enabled active-game override wins
otherwise Device wins
otherwise unmanaged
~~~

---

## 5. Persistence — one shared per-game setting shape

Add one optional field to GamePerformanceOverrides:

~~~csharp
public sealed record GamePerformanceOverrides
{
    public GameCpuBoostSettings? CpuBoost { get; init; }
    public GameTdpSettings? Tdp { get; init; }
    public GamePowerModeSettings? PowerMode { get; init; }
    public GameFpsLimitSettings? FpsLimit { get; init; }

    public GameGpuMinimumClockSettings? GpuMinimumClock { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
~~~

Add the narrow setting record:

~~~csharp
public sealed record GameGpuMinimumClockSettings
{
    public bool Enabled { get; init; }
    public required double AcMhz { get; init; }
    public required double DcMhz { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
~~~

Because XboxGameProfile already uses GamePerformanceOverrides, this single additive field covers both domains.

Do not add a second Xbox-specific GPU setting record.

No profile schema bump is required.

---

## 6. Per-game Off/null means inherit Device

The semantic contract is:

~~~text
GpuMinimumClock == null
→ per-game override uninitialized/off
→ inherit Device

GpuMinimumClock.Enabled == false
→ per-game override explicitly off
→ preserve saved game AC/DC values
→ inherit Device

GpuMinimumClock.Enabled == true
→ active game profile owns the effective game floor
~~~

Do not interpret per-game Off as:

~~~text
force GPU frequency unmanaged
~~~

That would incorrectly suppress an enabled Device baseline.

Per-game Off means only:

> this game does not override Device Minimum GPU Clock.

---

## 7. Whole Profile Enabled remains the outer gate

A game GPU override can become effective only when the owning game profile itself is enabled.

Required effective condition:

~~~text
ResolvedActiveProfile exists
AND
ResolvedActiveProfile.Performance.GpuMinimumClock.Enabled == true
~~~

ActiveProfileResolver already returns only enabled whole profiles.

Do not add another whole-profile check elsewhere unless needed for mutation validation.

### Important persistence rule

Enabling a whole Steam/XBOX profile must **not** automatically enable Minimum GPU Clock.

GameProfileMutations.Complete(...) and XboxGameProfileMutations.Complete(...) must leave:

~~~text
Performance.GpuMinimumClock
~~~

as null/off unless the user explicitly configured it.

This feature is opt-in.

---

## 8. First per-game Enable default

The locked production default remains capability-derived:

~~~text
RecommendedDefaultMhz
= third-highest currently advertised driver clock
after the PR1 selectable policy is applied
~~~

On the first explicit per-game Minimum GPU Clock Enable:

~~~text
GpuMinimumClock == null
→ AcMhz = RecommendedDefaultMhz
→ DcMhz = RecommendedDefaultMhz
→ Enabled = true
→ persist
~~~

Do **not** copy the Device AC/DC values into the new game override.

Reason:

~~~text
per-game Off/null
already means “inherit Device”

explicit per-game Enable
means “create an independent game override”
and therefore uses the feature's own dynamic default
~~~

No universal 2200 MHz constant is allowed.

B390 may resolve to 2200 MHz from its table; Arc 140V may resolve to a different value.

---

## 9. Re-enabling an existing Off game override

If an existing Off game override is re-enabled:

~~~text
supported saved AC
→ preserve

supported saved DC
→ preserve

unsupported saved rail
→ replace that rail only with current RecommendedDefaultMhz
~~~

This replacement is allowed only at the explicit user Enable boundary.

Do not background-clamp saved profile values during:

- Runtime startup;
- game launch;
- Steam/XBOX target change;
- AC/DC change;
- resume;
- Profile capture;
- Quick Settings render.

The same rule already exists for Device PR2 and should remain symmetrical.

---

## 10. Runtime: promote Device-only reconcile to effective reconcile

PR704 currently resolves only Device desired state inside IntelGpuMinimumClockRuntime.

PR3 must make the same Runtime resolve the effective game/device source.

Add the existing shared resolver to the runtime:

~~~csharp
private Func<ProfileDocument, ResolvedActiveProfile?>? _activeProfileResolver;

internal void SetActiveProfileResolver(
    Func<ProfileDocument, ResolvedActiveProfile?> resolver)
{
    _activeProfileResolver = resolver;
}
~~~

Equivalent naming is acceptable.

Do not give the runtime Steam/XBOX IDs directly.

It only needs the already-shared resolved profile.

---

## 11. Effective target resolution

Inside IntelGpuMinimumClockRuntime, replace the Device-only desired selection with a single effective resolution step.

Conceptually:

~~~csharp
var activeProfile = _activeProfileResolver?.Invoke(document);

if (activeProfile?.Performance.GpuMinimumClock is { Enabled: true } game)
{
    // Game wins.
    return game AC/DC;
}

if (document.Device.Performance.GpuMinimumClock is { Enabled: true } device)
{
    // Device fallback.
    return device AC/DC;
}

// Nothing owns frequency.
restore if owned;
~~~

Keep this logic inside the existing production runtime.

Do not modify ActiveProfileResolver so that it starts returning Device state.

ActiveProfileResolver remains:

~~~text
Which game profile, if any, is active?
~~~

IntelGpuMinimumClockRuntime remains:

~~~text
Given active game + Device state, which GPU floor is effective?
~~~

This preserves one clear authority per layer.

---

## 12. Exact effective-source priority

Lock the priority as:

~~~text
1. Active enabled game profile
   + game Minimum GPU Clock enabled
   → game AC/DC target

2. Otherwise Device Minimum GPU Clock enabled
   → Device AC/DC target

3. Otherwise
   → restore original minimum if owned
~~~

Examples:

### Device On, no game

~~~text
Device 1800/1600
→ effective = Device
~~~

### Device On, active Steam game override On

~~~text
Device 1800/1600
Steam game 2000/1800
→ effective = Steam game
~~~

### Device On, active XBOX game override Off

~~~text
Device 1800/1600
XBOX game override Off
→ effective = Device
~~~

### Device Off, active game override On

~~~text
Device Off
Game 1900/1700
→ effective = Game
~~~

### Device Off, active game override Off

~~~text
→ unmanaged
→ restore original minimum if marker exists
~~~

---

## 13. Critical PR3 invariant: Device mutations must not steal an active game override

This is the most important PR704 integration correction.

Current PR704 Device mutations can call Device apply/restore directly.

That is no longer correct once game overrides exist.

Example that must **not** happen:

~~~text
active game override = 2000 MHz
Device setting       = 1800 MHz

user edits Device AC to 1700 while game is running
→ Device must NOT write 1700
→ effective game 2000 remains authoritative
~~~

Likewise:

~~~text
active game override On
user turns Device Minimum GPU Clock Off
→ do NOT RestoreOriginalMinimum
→ game override stays effective
→ marker stays owned
~~~

Therefore after PR3:

- Device Enable must persist Device state, then reconcile **effective** source.
- Device Disable must persist Off, then reconcile **effective** source.
- Device active-rail edit must persist, then reconcile **effective** source.
- Device inactive-rail edit remains persistence-only where the current power rail is known.

Do not call RestoreIfOwned merely because Device became Off.

Restore only when the **effective** policy resolves to unmanaged.

---

## 14. Reuse one ownership marker across Device ↔ Game switches

Example:

~~~text
pre-Addon minimum = -1 / unmanaged
Device floor       = 1800
Game floor         = 2000

Device applies first
→ marker captures original minimum -1

game starts
→ apply 2000
→ keep same marker

game exits
→ apply Device 1800
→ keep same marker

Device turns Off
→ no effective source
→ restore -1
→ verify
→ remove marker
~~~

Never recapture 1800 or 2000 as a new baseline during source switching.

The original baseline remains the only restore debt.

---

## 15. Unsupported active game target must fail closed

If an enabled active game override contains a saved AC/DC value that no longer appears in the current driver selectable list:

~~~text
active game override says 1900
driver no longer advertises 1900
~~~

Do not:

- nearest-neighbor clamp;
- silently use RecommendedDefault;
- silently fall back to Device;
- keep the previous game's floor;
- repeatedly retry.

Required behavior:

~~~text
active explicit game override is invalid
→ release/restore owned minimum if possible
→ report SavedTargetUnsupportedByCurrentDriver or equivalent
→ keep persisted game value unchanged
→ require explicit user correction
~~~

Why no Device fallback here:

> A currently active, explicitly enabled game override is the higher-priority authority. If its requested target is invalid, silently applying a lower-priority Device setting would hide the configuration error and produce a state the user did not request for that game.

Per-game **Off/null** still falls back to Device normally.

---

## 16. AC/DC selection

The selected source and selected power rail are independent steps.

~~~text
effective source = Game or Device
current power    = AC or DC
→ choose that source's current rail
~~~

Unknown/unreadable power source remains fail-close:

~~~text
unknown AC/DC
→ release owned minimum if possible
→ do not guess AC or DC
~~~

Reuse the existing WindowsAcDcPowerNotificationSource.

Do not add another power watcher.

---

## 17. Game-profile mutation API in persistence owners

Add narrow Steam methods to GameProfileMutations:

~~~text
SetGpuMinimumClockEnabled(...)
SetGpuMinimumClockAc(...)
SetGpuMinimumClockDc(...)
~~~

Add the equivalent XBOX methods to XboxGameProfileMutations.

The persistence owners must continue to use the existing shared ProfileMutationGate.

No new lock or store.

### 17.1 Whole-profile gate

A direct GPU feature mutation requires:

~~~text
profile exists
profile.Enabled == true
profile persistence writable
~~~

If the whole profile is Off:

~~~text
GPU feature toggle/edit mutation
→ Unavailable
→ zero persistence change
→ zero native write
~~~

This matches the Main Profile editor policy.

### 17.2 Per-feature Off gate

If GameGpuMinimumClockSettings is null or Enabled=false:

~~~text
AC/DC mutation
→ Unavailable
→ no persistence change
~~~

Only explicit GPU Enable initializes/re-enables it.

---

## 18. Do not let profile persistence know IGCL internals

GameProfileMutations and XboxGameProfileMutations must not:

- load ControlLib.dll;
- create an IGCL session;
- enumerate adapters;
- call frequency APIs;
- own the marker.

The existing IntelGpuMinimumClockRuntime remains the capability authority.

The frontend/runtime orchestration may provide:

- current SelectableClocksMhz;
- current RecommendedDefaultMhz;
- a resolved canonical clock.

A tiny pure shared helper for Steam/XBOX persistence normalization is acceptable if it genuinely removes duplicated logic.

Do not create a generic “GPU settings provider” abstraction.

---

## 19. Discrete index contract for Profile editors

The driver reports double MHz values.

PR1/PR2 deliberately preserve them without assuming integral MHz.

Current Quick Settings value/options are integer-based.

Do **not** widen the entire shared Quick Settings value contract to arbitrary doubles just for this feature.

Use a discrete **index** for Profile UI/Quick Settings mutation.

Example:

~~~text
SelectableClocksMhz:
index 0 → 1525.000
index 1 → 1625.000
index 2 → 1725.125
index 3 → 1825.375
~~~

UI/Quick Settings transports:

~~~text
selected value = index 2
~~~

Runtime/frontend mutation boundary:

~~~text
index 2
→ validate current selectable table
→ canonical MHz = 1725.125
→ persist exact double 1725.125
~~~

Out-of-range index:

~~~text
→ InvalidTarget
→ zero persistence
→ zero SetRange
~~~

This keeps the shared Overlay slider integer/discrete while preserving the exact driver value.

Do not encode MHz by parsing display labels.

Do not invent a fixed kHz/scaling integer format.

---

## 20. No theoretical index/table generation machinery

A driver clock table changing at the exact instant between UI capture and user mutation is not a reason to add:

- table generation IDs;
- epochs;
- capability tokens;
- compare-and-swap versions;
- another session manager.

On mutation:

~~~text
validate submitted index against the Runtime's current table
→ use that current canonical entry
~~~

On resume/device-unavailable reinitialization, existing lifecycle reconcile refreshes capability and subsequent UI refresh returns the current table.

A graphics-driver replacement normally involves a much broader device lifecycle/restart boundary.

Keep this simple.

---

## 21. Profile Enable mutation semantics

### First GPU Enable

Required ordering:

~~~text
validate capability/default
→ build complete AC/DC setting
→ persist Enabled=true
→ if this profile is currently active:
     reconcile effective GPU source
→ otherwise:
     zero native write
~~~

### Existing Off setting

On explicit Enable:

~~~text
supported saved rail
→ preserve

unsupported saved rail
→ RecommendedDefault

persist
→ active target reconcile if applicable
~~~

### GPU Disable

~~~text
persist Enabled=false
→ if profile currently active:
     reconcile effective source
       → Device fallback if Device enabled
       → otherwise restore original
→ if profile not active:
     zero native write
~~~

The saved game AC/DC values remain persisted while Off.

---

## 22. Profile AC/DC edit semantics

Only canonical current driver entries are accepted.

### Active profile, current rail edit

~~~text
persist exact canonical MHz
→ reconcile effective GPU source
→ one minimum write/readback when effective source is still this game
~~~

### Active profile, inactive rail edit

~~~text
persist exact canonical MHz
→ zero native write
~~~

### Offline/non-active profile edit

~~~text
persist exact canonical MHz
→ zero native write
~~~

Do not disturb the currently effective Device/other-game floor while editing an offline profile.

---

## 23. Whole Profile Enabled mutation must reconcile GPU

Current ProfileApplyKind does not include Minimum GPU Clock.

Add one bit:

~~~csharp
GpuMinimumClock = 1 << 5
~~~

Include it in the existing whole-profile apply set:

~~~text
ExistingProfileEnable
= CpuBoost
| Tdp
| PowerMode
| FpsLimit
| GpuMinimumClock
~~~

Resolution remains governed by the existing policy.

Consequences:

~~~text
active profile Off → On
→ game GPU override may become effective

active profile On → Off
→ game GPU override is released
→ Device fallback or unmanaged restore becomes effective
~~~

Do not create a separate profile-enable callback just for GPU.

---

## 24. Frontend active-profile reconcile

Extend ReconcileActiveProfileMutationAsync(...) with one sibling:

~~~text
ProfileApplyKind.GpuMinimumClock
→ IntelGpuMinimumClockRuntime.ReconcileEffective(...)
~~~

Use the same Profiles.IntelGpuMinimumClock logging category.

This is the same pattern already used for CPU Boost / Power Mode / Intel FPS.

Do not call ApplyMinimum directly from frontend profile code.

---

## 25. Host active-target lifecycle

In AddonProcessHost:

### 25.1 Wire the resolver once

Alongside:

~~~text
_cpuBoostRuntime.SetActiveProfileResolver(...)
_powerModeRuntime.SetActiveProfileResolver(...)
_intelFpsRuntime.SetActiveProfileResolver(...)
_displayResolutionRuntime.SetActiveProfileResolver(...)
~~~

add:

~~~text
_intelGpuMinimumClockRuntime.SetActiveProfileResolver(_activeProfileResolver)
~~~

### 25.2 Existing game transitions

Add one call to the existing ReconcileEffectiveGameProfile(...):

~~~text
_intelGpuMinimumClockRuntime.ReconcileEffective(trigger)
~~~

This automatically covers:

- Steam RunningAppId start/change/clear;
- XBOX ActiveGame start/change/clear.

Do not add another Steam event or XBOX watcher.

---

## 26. Startup

ReconcileDeviceProfileStartup currently invokes the PR2 GPU startup path.

After PR3 it must mean:

~~~text
startup
→ load profile document
→ resolve active Steam/XBOX profile if any
→ choose Game → Device → unmanaged
→ reconcile once
~~~

The XBOX session runtime is already started before deferred profile startup in the current host.

Do not create delayed GPU-only startup timers.

---

## 27. AC/DC changes

Current host already receives one AC/DC notification source.

Replace the Device-only GPU reconcile call with effective reconcile.

Required:

~~~text
AC → DC
active game GPU override On
→ apply game DC

AC → DC
active game GPU override Off/null
Device On
→ apply Device DC

AC → DC
no effective source
→ no ownership
~~~

No polling.

---

## 28. Resume

Keep the existing delayed performance resume path and single IGCL session recovery rule.

Required:

~~~text
resume settle
→ effective GPU reconcile
→ first native operation returns DeviceLost/DeviceUnavailable
→ one Reinitialize
→ rediscover capability
→ one reconcile retry
→ stop
~~~

After capability rediscovery:

- active game target is still resolved fresh;
- current driver selectable table is authoritative;
- unsupported active game target fails closed;
- Device fallback is used only when game override is Off/null.

No retry loop.

No resume epoch/state machine.

---

## 29. Normal shutdown / restart / crash

PR2 behavior remains correct:

~~~text
normal controlled Runtime shutdown
→ do not restore solely because process exits
→ keep marker if production owns a floor

controlled restart
→ next Runtime adopts same original-minimum marker
→ resolve current effective Game/Device source
→ reapply

unexpected crash
→ marker remains
→ next Runtime adopts same recovery debt
~~~

Do not add source identity to the durable marker.

The marker owes restoration of the machine baseline, not restoration of a specific profile source.

---

## 30. Uninstall

Uninstall remains source-independent.

If the production marker exists:

~~~text
PrepareForUninstall
→ restore original minimum
→ verify
→ remove marker
→ continue existing stock-safe uninstall
~~~

It does not matter whether the current effective floor came from Device or Game.

Do not inspect every game profile during uninstall.

---

## 31. Developer IGCL probe coexistence

Keep the PR2 “no generic arbitration manager” policy.

There are still only two frequency writers in-process:

~~~text
production IntelGpuMinimumClockRuntime
developer IntelGpuIgclProbe
~~~

### 31.1 Block Developer frequency writes when production can become active automatically

Broaden BlocksDeveloperFrequencyMutation() enough to cover the new persisted production path.

Developer Set Max / Max and Developer Restore Original Frequency are refused if any of these is true:

~~~text
production ownership marker exists
OR
Device Minimum GPU Clock is enabled
OR
an enabled Steam profile has enabled Game GPU override
OR
an enabled XBOX profile has enabled Game GPU override
~~~

Why include inactive enabled profiles:

A configured enabled profile can become active from the existing Steam/XBOX game lifecycle without another user mutation. Blocking the developer diagnostic in that state avoids introducing another diagnostic-vs-production lifecycle coordinator.

This is intentionally conservative because the Developer probe is diagnostic-only.

Do not create active-profile leases or a writer registry.

### 31.2 Developer-modified probe blocks production persistence transitions that could establish ownership

While:

~~~text
IntelGpuIgclProbe.ModifiedByProbe == true
~~~

refuse:

- Device GPU Enable/edit as already implemented;
- Steam game GPU Enable/edit;
- XBOX game GPU Enable/edit;
- whole Steam/XBOX Profile Enable when that profile already contains an enabled GPU override.

Do so before persistence.

This maintains the simple invariant:

> A developer frequency write and a persisted enabled production policy cannot be newly established on top of each other in one Runtime.

Do not add a durable Developer crash journal.

---

## 32. Frontend profile GPU DTO

Add one shared Steam/XBOX profile GPU projection.

Recommended shape:

~~~csharp
public sealed record FrontendGameGpuMinimumClockConfiguration(
    bool Available,
    bool Initialized,
    bool Enabled,
    IReadOnlyList<double> SelectableClocksMhz,
    double? AcMhz,
    double? DcMhz,
    double? RecommendedDefaultMhz,
    string? UnavailableReason = null);
~~~

Expose it from both:

~~~text
FrontendGameProfileSnapshot
FrontendXboxGameProfileSnapshot
~~~

The real frontend captures must populate it from:

- persisted GameGpuMinimumClockSettings;
- current production IntelGpuMinimumClockRuntime capability.

Do not create separate Steam and XBOX GPU DTOs.

### Unavailable capability

A persisted game setting may still exist while IGCL is unavailable.

Snapshot should preserve/display the saved Enabled/AC/DC facts where practical, while:

~~~text
Available = false
→ mutation disabled
→ no native apply
~~~

Do not erase profile persistence because a driver/API capability is temporarily unavailable.

---

## 33. New typed frontend methods

Add Steam:

~~~text
SetGameProfileGpuMinimumClockEnabledAsync(uint appId, bool enabled)
SetGameProfileGpuMinimumClockAcAsync(uint appId, int selectableClockIndex)
SetGameProfileGpuMinimumClockDcAsync(uint appId, int selectableClockIndex)
~~~

Add XBOX:

~~~text
SetXboxGameProfileGpuMinimumClockEnabledAsync(string key, bool enabled)
SetXboxGameProfileGpuMinimumClockAcAsync(string key, int selectableClockIndex)
SetXboxGameProfileGpuMinimumClockDcAsync(string key, int selectableClockIndex)
~~~

AC/DC mutation takes a discrete index, not raw MHz.

The Runtime/frontend control resolves that index to the exact current driver value before persistence.

Use existing FrontendGameProfileMutationResult / FrontendXboxGameProfileMutationResult.

Do not invent a second profile mutation result type solely for GPU.

Map:

~~~text
invalid index / unsupported saved target
→ FrontendGameProfileMutationOutcome.InvalidTarget

unsafe profile persistence
→ PersistenceFailed

active-target native apply/readback failure after persistence
→ ApplyFailed

capability unavailable / whole profile off
→ Unavailable
~~~

---

## 34. Frontend transport protocol

Bump:

~~~text
FrontendTransportProtocol
62 → 63
~~~

Reason:

- Steam profile snapshot gains the GPU projection;
- XBOX profile snapshot gains the GPU projection;
- six new typed profile GPU mutation RPC methods are added.

Add typed request payloads for:

~~~text
Steam:
  appId + enabled
  appId + selectableClockIndex

XBOX:
  key + enabled
  key + selectableClockIndex
~~~

Use the existing NamedPipeAddonFrontendClient/Server dispatch pattern.

Do not add a generic “SetProfileSetting” RPC.

---

## 35. Main Steam page UI

Add a Minimum GPU Clock SettingsExpander to the Steam detail page.

Recommended order:

~~~text
TDP Control
CPU Boost
Windows Power Mode
Minimum GPU Clock
Intel FPS Limit
Resolution
~~~

Use the same description as Device, adjusted for profile semantics:

> Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.

### 35.1 Toggle

GPU feature toggle is editable only when:

~~~text
profile exists
&& whole profile Enabled
&& persistence writable
&& GPU capability Available
~~~

### 35.2 Child sliders

Use two discrete index sliders:

~~~text
Plugged in
On battery
~~~

Each index maps to SelectableClocksMhz exactly.

No continuous MHz slider.

No hand-built 50 MHz / 100 MHz step.

### 35.3 Feature Off

Match current Profile feature policy.

When per-game GPU override is Off:

- AC/DC controls are not editable;
- the feature does not override Device.

The Main Profile page may collapse the child cards like the existing CPU/TDP/Power/FPS expanders.

Do not allow Off-state preconfiguration.

### 35.4 First enable

If uninitialized, render RecommendedDefaultMhz as the uncommitted draft.

Do not persist merely because the page was opened.

---

## 36. Main XBOX page UI

Add the same Minimum GPU Clock editor to XBOX.

Use:

- the same labels;
- same discrete clock ordering;
- same enable/off policy;
- same warning text;
- same default semantics.

Do not create XBOX-specific clock policy.

The only difference is the profile identity and typed XBOX mutation method.

The existing XBOX Controller M1/M2 section remains independent.

---

## 37. Reuse the current Device UI clock-index helpers where sensible

DevicePage.GpuMinimumClockDraftPolicy already provides the relevant UI-only mechanics:

- find canonical index;
- resolve saved/default draft;
- map index to exact clock;
- format MHz.

The Steam/XBOX UI project already has precedent for reusing DevicePage.TdpDraftPolicy.

It is acceptable to reuse/refine GpuMinimumClockDraftPolicy rather than duplicate the same index math three times.

Do not move IGCL ownership logic into the UI.

Do not create a generic UI framework merely for these three pages.

---

## 38. Main UI commit behavior

Profile GPU sliders should follow the current Profile editing style.

Recommended:

~~~text
ValueChanged
→ update draft/value text

300 ms trailing settle
→ submit one discrete index mutation
~~~

or an equivalent existing Profile commit boundary.

Requirements:

- do not call Runtime for every pointer tick;
- only the newest pending edit for that rail commits;
- leaving/changing selected profile cancels stale pending edits;
- response must still match the currently selected Steam AppId/XBOX key before it is rendered.

Reuse the existing page-local debounce/generation pattern.

Do not introduce a shared debounce manager.

---

## 39. Profile Quick Settings contract

Add:

~~~csharp
QuickSettingsSectionId.ProfileGpuMinimumClock
~~~

Add rows:

~~~csharp
QuickSettingsRowId.ProfileGpuMinimumClockEnabled
QuickSettingsRowId.ProfileGpuMinimumClockAc
QuickSettingsRowId.ProfileGpuMinimumClockDc
~~~

No new page.

No new control kind.

No new commit group is required because AC and DC are independent rails.

### Section order

For both Steam and XBOX:

~~~text
Profile
[Controller only for XBOX]
TDP Control
CPU Boost
Windows Power Mode
Minimum GPU Clock
Intel FPS Limit
Resolution
~~~

---

## 40. Profile Quick Settings discrete options

Build options from the current profile GPU snapshot:

~~~text
option Value = zero-based selectable-clock index
option Label = exact MHz display text
~~~

Example:

~~~text
0 → "1525 MHz"
1 → "1625 MHz"
2 → "1725.13 MHz"
~~~

Use one formatting helper consistent with Main UI.

Do not make option Value equal rounded MHz.

Do not parse the Label later.

---

## 41. Profile Quick Settings row semantics

### Toggle row

~~~text
Available = gpu.Available
Writable  = snapshot.PersistenceWritable
            && snapshot.Enabled
            && gpu.Available

Value = gpu.Enabled
Commit = Immediate
~~~

### AC/DC rows

When gpu.Enabled is false, follow existing compact Profile Quick Settings behavior:

~~~text
show only the toggle row
~~~

When gpu.Enabled is true, add both AC/DC rows.

Each row:

~~~text
ControlKind = Slider
SliderKind  = Discrete
Options     = current selectable-clock index/options
Commit      = TrailingDebounce300
~~~

Writability:

~~~text
snapshot.PersistenceWritable
&& snapshot.Enabled
&& gpu.Available
~~~

The existing current-power-source visibility setting must hide the opposite rail using the new AC/DC row IDs.

---

## 42. Unsupported saved game value in Quick Settings

If an enabled saved rail is not in the current selectable list:

- do not present it as a valid supported selection;
- use RecommendedDefault index only as an uncommitted display draft for that invalid rail;
- include a section/page warning indicating the saved target is no longer supported;
- require an explicit user change to persist a replacement;
- do not background-write the draft.

The Runtime remains fail-closed while the explicit active-game target is invalid.

If the game GPU feature is Off, the AC/DC rows are hidden and explicit Enable performs the section 9 recovery.

---

## 43. Quick Settings mutation adapter

Add explicit cases for:

~~~text
ProfileGpuMinimumClockEnabled
ProfileGpuMinimumClockAc
ProfileGpuMinimumClockDc
~~~

Dispatch by QuickSettingsProfileTarget.Kind:

~~~text
Steam
→ SetGameProfileGpuMinimumClock...

Xbox
→ SetXboxGameProfileGpuMinimumClock...
~~~

### Toggle validation

Require exactly one structurally valid Boolean value for the toggle row.

### Slider validation

Require exactly one structurally valid Integer value for the edited AC/DC row.

That integer is the discrete selectable-clock index.

Typed Runtime method performs final current-capability index validation.

Malformed intent:

~~~text
→ zero typed mutation
→ return failure + freshly captured authoritative page
~~~

Do not accept raw arbitrary MHz through Quick Settings.

---

## 44. Overlay protocol

The new Profile Quick Settings section/row enum members cross the Overlay wire.

Bump:

~~~text
OverlayTransportProtocol
17 → 18
~~~

Reason:

- v17 does not know the new closed section/row enum members.

Do not add:

- a new Overlay message kind;
- a GPU-specific Overlay request;
- a GPU-specific Overlay state packet;
- a GPU-specific renderer.

The existing generic QuickSettingsPageSnapshot / QuickSettingsMutationIntent transport is sufficient.

---

## 45. Overlay renderer

No GPU-specific Overlay visual tree.

The existing generic renderer must consume:

~~~text
ProfileGpuMinimumClock section
→ Toggle
→ discrete AC/DC sliders
~~~

from the shared snapshot.

Only generic fixes required to correctly render the new section/options are in scope.

Do not add:

~~~text
XboxGpuClockOverlayView
SteamGpuClockOverlayView
GpuClockOverlayManager
~~~

---

## 46. Device page remains a Device editor, but effective hardware stays Game-first

The Main Device page continues to edit Device persistence even while a game override is active.

Do not disable the Device editor merely because an active game currently overrides it.

Example:

~~~text
active game = 2000
Device currently = 1800

user changes Device to 1700
→ Device persistence becomes 1700
→ hardware remains effective game 2000

game exits
→ Device 1700 becomes effective
~~~

This makes Device a true fallback baseline and avoids hidden “cannot edit while game running” state.

---

## 47. Capture semantics while game override is active

FrontendDeviceQuickSettingsSnapshot.GpuMinimumClock remains the Device configuration, not the currently effective game configuration.

Do not silently replace Device AC/DC display with active game values.

The Device page answers:

> What is my Device baseline?

The Profile page answers:

> What is this game's override?

Hardware/effective diagnostics belong in logs, not by conflating these persisted editors.

---

## 48. Logging

Continue category:

~~~text
Profiles.IntelGpuMinimumClock
~~~

Add source context to effective reconcile logs:

~~~text
Reason
ProfileTarget
EffectiveSource = Game | Device | None
PowerSource
DesiredAcMhz
DesiredDcMhz
EffectiveTargetMhz
CapabilityAvailable
OwnershipMarkerPresent
Outcome
Failure
~~~

Do not log every UI slider movement.

One log per actual persistence/reconcile/native operation is enough.

No polling/per-frame logging.

---

## 49. Tests — persistence

Add tests for both Steam and XBOX through the shared GamePerformanceOverrides field.

Verify:

~~~text
Steam GameGpuMinimumClockSettings round-trip
XBOX GameGpuMinimumClockSettings round-trip
Enabled=false preserves AC/DC
unknown extension data survives
legacy profile without field remains null/off
no schema bump
double values round-trip within existing canonical tolerance
~~~

---

## 50. Tests — first game enable/default

Use synthetic clock tables.

Verify:

~~~text
B390-like table
→ default derived from third-highest entry

different 140V-like table
→ default derived from that table's third-highest entry
~~~

Then for both Steam and XBOX:

~~~text
whole profile Enabled
GPU override null
explicit GPU Enable
→ AC = RecommendedDefault
→ DC = RecommendedDefault
→ Enabled = true
~~~

For an offline/non-active profile:

~~~text
→ zero SetRange
~~~

No test may assume 2200 MHz universally.

---

## 51. Tests — explicit re-enable recovery

For Steam and XBOX:

~~~text
GPU override Off
AC supported
DC no longer supported
explicit Enable
→ AC preserved
→ DC = current RecommendedDefault
→ Enabled=true
~~~

Background capture/startup must leave the unsupported persisted value untouched until that explicit Enable.

---

## 52. Tests — effective priority

Cover the complete priority matrix.

### Game beats Device

~~~text
Device On = 1800
active game GPU On = 2000
→ SetRange min 2000
~~~

### Game Off inherits Device

~~~text
Device On = 1800
active game GPU Off
→ SetRange min 1800
~~~

### Device Off, Game On

~~~text
Device Off
active game GPU On = 2000
→ SetRange min 2000
~~~

### Nothing active

~~~text
Device Off
no enabled active game override
marker exists
→ original min restored
→ marker removed after verification
~~~

Repeat active game cases for Steam and XBOX through the same resolver seam.

Do not duplicate native runtime implementations in tests.

---

## 53. Tests — Device mutation cannot steal Game authority

Add direct regression tests for the critical PR3 invariant.

### Device rail edit during game override

~~~text
active game = 2000
Device AC edit 1800 → 1700
→ Device persistence = 1700
→ effective SetRange remains 2000
~~~

### Device Disable during game override

~~~text
game override active
Device On → Off
→ Device persisted Off
→ zero RestoreOriginalMinimum
→ game floor remains effective
→ marker remains
~~~

### Device Enable during game override

~~~text
game override active
Device Off → On
→ Device persistence enabled
→ game remains effective
~~~

These are realistic normal-user paths and are blocking correctness tests.

---

## 54. Tests — game transitions

Use existing host/active-profile seams.

Verify:

~~~text
Device = 1800

no game
→ 1800

Steam game A override = 2000 starts
→ 2000

Steam game A exits
→ 1800

XBOX game B override = 1900 starts
→ 1900

XBOX game B exits
→ 1800
~~~

Also:

~~~text
Device Off
game exits
→ original restore
~~~

No new event source.

---

## 55. Tests — whole Profile toggle

For an active Steam/XBOX target with saved GPU override Enabled:

~~~text
Profile Enabled true → false
→ GPU effective reconcile
→ Device fallback or restore
~~~

and:

~~~text
Profile Enabled false → true
→ GPU override becomes effective
~~~

If Developer probe is modified and enabling the whole profile would establish an enabled GPU override:

~~~text
→ reject before persistence
~~~

---

## 56. Tests — active/inactive AC/DC rail

Active game override enabled.

With AC power:

~~~text
edit game AC
→ persist
→ one effective GPU write

edit game DC
→ persist only
→ zero GPU write
~~~

Repeat inverse for DC.

Offline profile AC/DC edit always:

~~~text
→ persist only
→ zero GPU write
~~~

---

## 57. Tests — unsupported active game target

Setup:

~~~text
Device valid and enabled = 1800
active game override enabled = saved 1777
driver current selectable list does not contain 1777
marker exists
~~~

Expected:

~~~text
do NOT apply Device 1800 as silent fallback
do NOT clamp 1777
restore original minimum if possible
keep persisted 1777 unchanged
report unsupported active target
~~~

This test locks the fail-close priority contract.

---

## 58. Tests — marker continuity across source switches

Verify:

~~~text
original min = 900

Device applies 1800
→ marker original = 900

Game applies 2000
→ marker still original = 900

Game exits → Device 1800
→ marker still original = 900

effective source becomes None
→ restore 900
→ verify
→ marker removed
~~~

No source switch may overwrite original-minimum evidence.

---

## 59. Tests — startup / restart / crash

### Startup with active game override

If the current Runtime starts while an already-running Steam/XBOX game is resolved:

~~~text
game override On
→ game target is effective
~~~

### Controlled restart

Existing marker:

~~~text
→ original baseline preserved
→ effective Game/Device target reapplied
~~~

### Crash recovery

Same observable marker behavior.

Do not add a second crash journal.

---

## 60. Tests — AC/DC and Resume

AC/DC:

~~~text
active game override On
AC → DC
→ game DC applied

game override Off
Device On
AC → DC
→ Device DC applied
~~~

Resume:

~~~text
first operation DeviceUnavailable
→ one session Reinitialize
→ rediscover capability
→ one effective reconcile retry
→ stop
~~~

If active game saved target disappears after rediscovery:

~~~text
→ no clamp
→ no Device fallback
→ restore owned state if possible
→ report invalid target
~~~

---

## 61. Tests — Developer probe conflict

Verify Developer SetMaxMax is refused when:

~~~text
Device GPU enabled
enabled Steam profile + GPU override enabled
enabled XBOX profile + GPU override enabled
production marker exists
~~~

Verify it remains available when:

~~~text
Device Off
no marker
only disabled whole profiles contain saved GPU overrides
~~~

Verify Developer PL1 operations remain unaffected.

Verify probe ModifiedByProbe blocks:

- game GPU Enable;
- game GPU AC/DC edit;
- whole Profile Enable when that profile would activate an enabled GPU override.

No generic authority table.

---

## 62. Tests — frontend snapshots

For Steam and XBOX snapshot capture, verify:

~~~text
GpuMinimumClock.Available
Initialized
Enabled
SelectableClocksMhz exact order
AcMhz
DcMhz
RecommendedDefaultMhz
UnavailableReason
~~~

Uninitialized persisted state:

~~~text
Initialized=false
Enabled=false
saved AC/DC null
selectable/default still available when hardware capability exists
~~~

---

## 63. Tests — named-pipe transport

Bump expected frontend protocol:

~~~text
62 → 63
~~~

Add round-trip coverage for all six new methods.

Verify:

- Steam enabled mutation;
- Steam AC index;
- Steam DC index;
- XBOX enabled mutation;
- XBOX AC index;
- XBOX DC index;
- profile snapshots carry the GPU projection.

Malformed/out-of-range payloads must invoke zero persistence mutations.

---

## 64. Tests — Main Steam/XBOX UI

Verify both pages use discrete driver indices only.

Minimum assertions:

~~~text
slider min = 0
slider max = selectable count - 1
step = 1
index maps to exact driver double
no arbitrary MHz can be submitted

whole profile Off
→ GPU toggle disabled

GPU feature Off
→ AC/DC not editable

first Enable
→ default comes from RecommendedDefault

unsupported enabled saved value
→ warning
→ user can select current supported entry

page open/render
→ zero persistence
→ zero native write
~~~

Reuse existing UI test patterns.

---

## 65. Tests — Profile Quick Settings

Add section/rows to shared presentation tests.

Verify Steam order:

~~~text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileGpuMinimumClock
ProfileFpsLimit
ProfileResolution
~~~

Verify XBOX order:

~~~text
ProfileGeneral
ProfileController
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
ProfileGpuMinimumClock
ProfileFpsLimit
ProfileResolution
~~~

Verify:

- toggle is Immediate;
- AC/DC are Discrete;
- option integer is index;
- label is exact formatted MHz;
- trailing debounce is 300 ms;
- current-power-source visibility hides opposite rail;
- Off feature exposes only toggle row;
- malformed mutation intents do not reach typed methods.

---

## 66. Tests — Overlay transport

Bump:

~~~text
OverlayTransportProtocol
17 → 18
~~~

Verify the existing generic Overlay Profile renderer accepts and renders the new section.

No new message kind should exist.

No GPU-specific Overlay transport DTO should exist.

---

## 67. Existing behaviors that must not regress

PR3 must preserve:

- Full1902 PID1902 physical ownership;
- HidHide deterministic baseline;
- VIIPER ownership/teardown;
- Xbox360 ↔ SteamDeck presentation authority;
- elevated Runtime model;
- Win+G suppression;
- Steam game detection;
- XBOX event-driven game detection;
- per-game M1/M2 XBOX mapping;
- CPU Boost;
- TDP;
- Windows Power Mode;
- Intel FPS Limit;
- Display Resolution;
- Battery Charge Limit;
- Device Minimum GPU Clock semantics;
- Developer PL1 probe;
- safe uninstall.

GPU profile work is a sibling performance feature only.

---

## 68. Expected files to change

Likely:

~~~text
src/SteamInputAddonforClaw/Profiles/GameProfile.cs
src/SteamInputAddonforClaw/Profiles/GameProfileMutations.cs
src/SteamInputAddonforClaw/Profiles/XboxGameProfileMutations.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs

tests/... profile persistence/mutation/runtime/frontend/transport/Quick Settings/UI tests
~~~

Touch Overlay rendering implementation only if the generic renderer needs a genuinely generic correction for the new section.

Do not create a new project or helper executable.

---

## 69. Explicit non-goals

PR3 must not add:

~~~text
GPU maximum clock control
Max/Max production mode
GPU voltage
GPU PL1/PL2/PL4 production writes
automatic TDP changes
GPU power feedback
ClawHUD dependency
PresentMon control dependency
PawnIO dependency
private IGCLIPFProvider/B390Native path
hardcoded B390 MHz table
hardcoded Arc 140V MHz table
hardcoded 2200 default
continuous arbitrary-MHz slider
nearest-clock auto clamp
periodic polling
periodic frequency reassert
retry manager
writer registry
authority manager
source-specific ownership markers
profile-specific recovery journals
Steam-specific GPU runtime
XBOX-specific GPU runtime
new game watcher
new power watcher
new resume state machine
generic GPU vendor abstraction
AMD/NVIDIA support
controller/HidHide/VIIPER changes
~~~

---

## 70. Hardware validation — B390 / CG3EM

After code/CI passes, validate on the current B390 hardware.

### A. Device baseline only

~~~text
Device On
no game override
→ Device floor applies
~~~

### B. Steam override

~~~text
Device = lower value
Steam game override = higher value
launch game
→ game floor

exit game
→ Device floor
~~~

### C. XBOX override

Repeat through the XBOX active-game path.

### D. Per-game Off

~~~text
active game profile Enabled
game GPU override Off
→ Device floor remains effective
~~~

### E. Device edit while game active

~~~text
game floor active
edit Device floor
→ hardware remains game floor
exit game
→ edited Device floor becomes effective
~~~

### F. Power rail

Configure different game AC/DC values and switch power source.

### G. Sleep/Hibernate/Resume

Verify one effective reapply after settle and no retry loop.

### H. Runtime restart / forced kill

Verify the original marker baseline is retained across Device↔Game source selection.

---

## 71. Hardware validation — Lunar Lake / Arc 140V / A2VM

Use exactly the same implementation.

Collect:

~~~text
actual ctlFrequencyGetAvailableClocks() list
derived lower bound
derived third-highest upper/default
Steam profile discrete options
XBOX profile discrete options
Device → Game → Device source switching
AC/DC switching
resume
restore
~~~

Do not add a 140V-specific table after testing.

A different number or spacing of frequency entries is expected.

The feature remains capability-driven.

---

## 72. CI / automated validation

CI must not issue physical GPU writes.

Use fake IIntelGpuMinimumClockControl implementations and synthetic clock tables.

At minimum run:

~~~text
dotnet build SteamInputAddonforClaw.slnx --no-restore -c Release -m:1 -p:UseSharedCompilation=false

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-build -c Release -m:1

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release -m:1 -p:UseSharedCompilation=false
~~~

Follow the repository's current CI command if it has changed.

---

## 73. Acceptance criteria

PR3 is complete when all are true:

1. GamePerformanceOverrides has one optional GameGpuMinimumClockSettings used by both Steam and XBOX.
2. Existing/legacy profiles remain valid without schema bump.
3. Per-game GPU override defaults Off/null.
4. Whole Profile Enable does not automatically enable the GPU override.
5. First explicit game GPU Enable uses current PR1 RecommendedDefaultMhz for both AC/DC.
6. No universal 2200 MHz default exists.
7. Saved game values remain exact current driver-advertised doubles.
8. Game Off/null inherits Device.
9. Enabled active-game override has higher priority than Device.
10. No effective source restores the exact original minimum through the existing marker.
11. Device edits while an active game override is effective do not steal hardware authority.
12. Device Disable while an active game override is effective does not restore the original minimum.
13. One marker survives Device↔Game↔Device source switching without recapturing the baseline.
14. Unsupported active game targets fail closed and do not silently fall back to Device.
15. Background lifecycle never clamps/replaces unsupported saved game values.
16. Explicit game Enable may replace only unsupported Off-state rails with the current RecommendedDefault.
17. Steam and XBOX use the same IntelGpuMinimumClockRuntime and ActiveProfileResolver seam.
18. Existing Steam/XBOX active-target events trigger GPU effective reconcile; no new watcher exists.
19. AC/DC uses the existing power notification source.
20. Resume performs at most one DeviceLost/DeviceUnavailable session reinitialize + one retry.
21. Normal Runtime shutdown does not restore solely because the process exits.
22. Uninstall still performs one verified source-independent restore.
23. Developer frequency writes cannot conflict with a persisted enabled production GPU policy.
24. Developer PL1 operations remain independent.
25. Steam Main page exposes per-game Minimum GPU Clock.
26. XBOX Main page exposes the same feature.
27. Profile Off / GPU Off follows existing Profile editor gating.
28. UI submits discrete selectable-clock indices, never arbitrary raw MHz.
29. Frontend resolves indices to exact current driver double values.
30. Frontend protocol is 63.
31. Shared Profile Quick Settings contains the new GPU section for both Steam and XBOX.
32. Overlay protocol is 18.
33. Overlay uses the existing generic Quick Settings renderer and no GPU-specific wire message.
34. Current-power-source-only filtering handles the new AC/DC rows.
35. Existing CPU Boost/TDP/Power Mode/FPS/Resolution behavior is unchanged.
36. Existing XBOX per-game M1/M2 behavior is unchanged.
37. Existing Device Minimum GPU Clock behavior is unchanged except where effective game priority correctly supersedes hardware application.
38. B390 uses the current capability-driven table.
39. Arc 140V uses the same capability-driven implementation with no device-specific MHz constants.
40. CI performs no physical GPU write.

---

## 74. Implementation principle

Keep the final architecture small:

~~~text
driver clock table
      ↓
one IntelGpuMinimumClockRuntime
      ↓
effective source:
  active game override
  else Device
  else unmanaged
      ↓
current AC/DC rail
      ↓
one marker
      ↓
one minimum-only SetRange path
~~~

Steam and XBOX differ only in profile identity/persistence/front-end routing.

They do not get different GPU control architectures.

One owner.

One marker.

One driver table.

One effective-source rule.

No tuning framework.
