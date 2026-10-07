# Work Order — PR4: Game-Only Minimum GPU Clock and Factory-Release Policy Correction

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@fe7a957963998e0e8bec985a22e2d5080147a0f3` (PR #705 merged)  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and its active precedence chain  
> **Previous GPU work orders:** PR1 production core, PR2 Device/global integration, PR3 Steam/XBOX per-game override  
> **Current Frontend protocol:** `FrontendTransportProtocol.CurrentVersion = 63`  
> **Current Overlay protocol:** unchanged by this PR  
> **Scope:** correct Minimum GPU Clock to be a game-only feature; remove the Device/global path; remove pre-Addon minimum ownership-marker/original-value restoration; release the minimum-frequency limit to Intel factory policy with `min = -1`; preserve the existing game-profile UI/mutations and real lifecycle reconcile paths; make uninstall always verify factory minimum release before continuing  
> **Out of scope:** Developer IGCL probe retirement, production-file split/refactor, new watchdog/retry/authority abstractions, GPU maximum-frequency ownership, PL1 control, CTW integration

---

## 1. Goal

Correct the product policy for **Minimum GPU Clock**.

This feature is not a Device/global performance baseline. It is a per-game workaround for games that benefit from preventing the integrated GPU from downclocking below a user-selected floor while that game is active.

The final policy is:

~~~text
active enabled Steam/XBOX game profile
+ that game's Minimum GPU Clock is enabled
        ↓
apply that game's current AC/DC minimum-frequency floor

otherwise
        ↓
release Minimum GPU Clock to Intel factory policy
        ↓
ctl_freq_range_t.min = -1
~~~

There is no Device fallback.

There is no "restore whatever minimum another application had configured before us" behavior.

There is no durable `OriginalMinMhz` baseline.

The Addon deliberately owns only the temporary game-specific minimum-frequency floor. When that game-specific policy is no longer effective, the Addon returns the Intel minimum-frequency limit to factory/default policy.

This correction must land before the planned Developer IGCL PoC cleanup and production GPU file-organization cleanup so those later PRs operate on the final product semantics.

---

## 2. Why the current policy must change

PR2 introduced a Device/global Minimum GPU Clock setting.

PR3 then implemented the effective priority:

~~~text
Game
→ Device
→ unmanaged/original restore
~~~

That is implemented in current `IntelGpuMinimumClockRuntime.ReconcileLoadedEffective()`.

For a handheld this is the wrong product behavior.

Example:

~~~text
Device Minimum GPU Clock = 2200 MHz
game exits
Windows desktop / idle remains active
→ Device fallback keeps min = 2200 MHz
~~~

Even though Minimum GPU Clock is a floor rather than a fixed clock, retaining a high minimum-frequency floor during ordinary Windows idle unnecessarily constrains Intel frequency management and can increase handheld idle/package power.

The product intent is instead:

> Minimum GPU Clock exists only for an active game that explicitly enables it.

Outside that condition the Addon must not retain a custom minimum-frequency floor.

---

## 3. Locked Intel IGCL semantics

The implementation must follow Intel's official `ctl_freq_range_t` contract from:

~~~text
intel/drivers.gpu.control-library
include/igcl_api.h
reviewed upstream commit: b6c462933502e13d1537dd5024949a51be30e63d
~~~

Intel documents the minimum side as:

~~~text
min = 0
→ permit hardware frequency management to go down to the hardware minimum

min = -1
→ return the minimum-frequency limit to the factory value
   (the factory value can be larger than the hardware minimum)

GetRange output min < 0
→ no external minimum-frequency limit is in effect
~~~

Therefore the Addon factory-release operation for this feature is:

~~~text
request.min = -1
~~~

Do **not** use `0` as the release value.

`0` and `-1` are not equivalent.

### 3.1 Maximum frequency remains outside Addon ownership

This feature controls only the minimum side.

Do not reset or take ownership of GPU maximum frequency.

When building an apply or factory-release request, preserve the current maximum semantics exactly as the existing minimum-only policy does:

~~~text
current max >= 0
→ preserve that explicit max

current max < 0
→ request max = -1
~~~

Do not turn this PR into a full GPU range factory reset.

---

## 4. Current code facts on reviewed main

### 4.1 Device/global persistence exists

`Profiles/DeviceSettings.cs` currently contains:

~~~csharp
DevicePerformanceSettings.GpuMinimumClock
DeviceGpuMinimumClockSettings
~~~

This is the Device/global state being retired.

### 4.2 Runtime still contains Device ownership and mutation code

`Profiles/Performance/IntelGpuMinimumClock.cs` currently contains:

~~~text
GpuMinimumClockRuntimeSnapshot
GpuMinimumClockMutationResult
CaptureDeviceSnapshot()
SetEnabled()
SetAc()
SetDc()
ReconcileDevice()
ReconcileDeviceMutation()
WithDeviceGpuMinimumClock()
BuildDeviceSnapshot()
DeviceMutationResult()
Device-specific mutation/failure logging
~~~

These exist only because PR2 made Minimum GPU Clock a Device feature.

They are not part of the final game-only product.

### 4.3 Current effective policy still falls back to Device

Current `ReconcileLoadedEffective()` conceptually does:

~~~csharp
active game GPU Min enabled
    → Game

else Device GPU Min enabled
    → Device

else
    → restore original if owned
~~~

This must become game-only.

### 4.4 Current runtime stores the pre-Addon minimum

Current production code has:

~~~text
IntelGpuMinimumClockOwnershipMarker
  VendorId
  DeviceId
  OriginalMinMhz

intel-gpu-minimum-clock-ownership.json
~~~

`ApplyMinimum()` captures the current minimum before the first Addon write.

`RestoreOriginalMinimum()` later restores that saved value.

That policy is explicitly retired.

### 4.5 Current uninstall is already in the correct fail-close location

`AddonProcessHost.PrepareForUninstallAsync()` currently calls:

~~~text
IntelGpuMinimumClockRuntime.PrepareForUninstall()
→ if GPU release fails
   → block uninstall
→ otherwise continue existing Full1902 stock-safe preparation
~~~

Keep this ordering and fail-close behavior.

Only replace the GPU release semantics.

### 4.6 Existing game lifecycle events are sufficient

The host already has the required event-driven paths:

~~~text
Steam ActualRunningAppIdChanged
→ ReconcileEffectiveGameProfile(...)
→ IntelGpuMinimumClockRuntime.ReconcileEffective(...)

XBOX ActiveGameChanged
→ ReconcileEffectiveGameProfile(...)
→ IntelGpuMinimumClockRuntime.ReconcileEffective(...)

AC/DC Changed
→ existing WindowsAcDcPowerNotificationSource
→ GPU reconcile

PowerResumeObserved
→ existing 2.5 s performance settle
→ GPU resume reconcile
~~~

Do not add another game watcher, power watcher, poller, timer, epoch, or authority layer.

---

## 5. Final product authority

The effective GPU-minimum authority becomes exactly:

~~~text
ActiveProfileResolver
        ↓
active Steam/XBOX GamePerformanceOverrides.GpuMinimumClock
        ↓
IntelGpuMinimumClockRuntime
        ↓
IGCL minimum-only SetRange
~~~

There is no Device GPU-minimum authority.

There is no saved pre-Addon minimum authority.

There is no external-application coexistence authority.

The resolution rule is:

~~~text
active profile exists
AND profile.Enabled == true
AND profile.Performance.GpuMinimumClock?.Enabled == true
    → game owns temporary minimum floor

everything else
    → Intel factory minimum policy
    → min = -1
~~~

Do not add a generalized GPU authority object.

The existing active-profile resolver remains the only game target selector.

---

## 6. Remove the Device/global Minimum GPU Clock feature

### 6.1 Persistence

Remove the typed Device/global field:

~~~csharp
DevicePerformanceSettings.GpuMinimumClock
~~~

Remove:

~~~csharp
DeviceGpuMinimumClockSettings
~~~

Do not create a schema migration solely for this removal.

The application is pre-release and `DevicePerformanceSettings` already preserves unknown JSON through `JsonExtensionData`.

If an existing development profile document still contains a legacy:

~~~json
device.performance.gpuMinimumClock
~~~

property, it must have **zero runtime authority**.

Do not read it, apply it, or use it as a fallback.

Do not add migration/state machinery merely to erase inert historical JSON.

### 6.2 Runtime Device API

Remove the Device-specific runtime surface:

~~~text
GpuMinimumClockRuntimeSnapshot
GpuMinimumClockMutationOutcome
GpuMinimumClockMutationResult

CaptureDeviceSnapshot()
SetEnabled()
SetAc()
SetDc()
ReconcileDevice()
ReconcileDeviceMutation()
WithDeviceGpuMinimumClock()
BuildDeviceSnapshot()
DeviceMutationResult()
LogDeviceMutation()
_lastDeviceFailure
~~~

If a narrow runtime capability accessor is still required by Steam/XBOX profile capture/mutation, expose the already-existing `IntelGpuMinimumClockCapability` directly through a simple runtime method such as:

~~~csharp
internal IntelGpuMinimumClockCapability CaptureCapability()
~~~

or use the existing read-only initialization/capability state directly.

Do not invent a provider/service abstraction.

### 6.3 Main Device UI

Remove the Minimum GPU Clock card and all Device-page state/handlers from:

~~~text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
~~~

This includes:

- Device Minimum GPU Clock enable toggle;
- Device AC slider;
- Device DC slider;
- Device info/error presentation specific to this feature;
- Device GPU draft state;
- Device GPU commit handlers.

The Device tab must no longer expose Minimum GPU Clock.

### 6.4 Preserve the pure slider helper required by game profile editors

Current Steam/XBOX profile pages reuse `DevicePage.GpuMinimumClockDraftPolicy`.

Once the Device implementation is removed, do not leave game-profile code depending on a Device-page nested helper.

Move only that pure helper to a small shared UI location, for example:

~~~text
GpuMinimumClockUiPolicy
~~~

and update Steam/XBOX profile editor references.

This is a necessary ownership cleanup caused by deleting the Device feature.

Do not introduce a generic GPU editor/controller abstraction.

---

## 7. Remove the Device frontend/RPC contract

The closed Runtime ↔ Main UI contract currently carries Device GPU-minimum state and mutations.

Remove the Device-only contracts:

~~~text
FrontendGpuMinimumClockSnapshot
FrontendGpuMinimumClockMutationOutcome
FrontendGpuMinimumClockMutationResult
~~~

Remove the GPU child from:

~~~text
FrontendDeviceQuickSettingsSnapshot
~~~

Update its constructor(s) accordingly.

Remove Device GPU frontend methods from `IAddonFrontendControl` and implementations, including conceptually:

~~~text
CaptureGpuMinimumClockAsync
SetDeviceGpuMinimumClockEnabledAsync
SetDeviceGpuMinimumClockAcAsync
SetDeviceGpuMinimumClockDcAsync
~~~

Remove corresponding Named Pipe transport support:

~~~text
FrontendRpcMethod.CaptureGpuMinimumClock
FrontendRpcMethod.SetDeviceGpuMinimumClockEnabled
FrontendRpcMethod.SetDeviceGpuMinimumClockAc
FrontendRpcMethod.SetDeviceGpuMinimumClockDc

SetDeviceGpuMinimumClockEnabledRequest
SetDeviceGpuMinimumClockClockRequest

client methods
server dispatch/handlers
wire tests
~~~

Do **not** remove the Steam/XBOX game-profile GPU contracts or RPCs:

~~~text
FrontendGameGpuMinimumClockConfiguration

SetGameProfileGpuMinimumClockEnabled
SetGameProfileGpuMinimumClockAc
SetGameProfileGpuMinimumClockDc

SetXboxGameProfileGpuMinimumClockEnabled
SetXboxGameProfileGpuMinimumClockAc
SetXboxGameProfileGpuMinimumClockDc
~~~

Those are the final product feature.

### 7.1 Protocol version

This is a closed frontend contract removal.

Bump:

~~~text
FrontendTransportProtocol.CurrentVersion
63 → 64
~~~

Add a concise version comment explaining that v64 retires Device/global Minimum GPU Clock and keeps game-profile-only Minimum GPU Clock.

Do not bump the Overlay protocol.

The shared Profile Quick Settings GPU rows remain valid.

---

## 8. Remove original-minimum ownership-marker semantics

Remove:

~~~text
IntelGpuMinimumClockOwnershipMarker
OriginalMinMhz
OwnershipMarkerJsonOptions
_ownershipMarkerPath
_ownershipMarker
_ownershipMarkerPresent
OwnershipMarkerPresent capability/result fields

ReadOwnershipMarker()
LoadOwnershipMarker()
PersistOwnershipMarker()
MarkerMatchesAdapter()
SetOwnershipMarker()

AddonDataPaths.IntelGpuMinimumClockOwnershipPath
AddonDataPaths.ResolveIntelGpuMinimumClockOwnershipPath(...)
associated path tests
~~~

`ApplyMinimum()` must no longer capture the pre-write minimum into persistent storage.

Do not replace this marker with:

- an epoch;
- a session file;
- an "owned" boolean file;
- a registry value;
- another recovery database.

There is no original-state debt anymore.

An inert marker file left behind by a pre-release development build has no product authority and does not justify migration machinery.

---

## 9. Replace original restore with one factory-minimum release operation

Delete:

~~~text
RestoreOriginalMinimum(...)
TryCreateRestoreRequest(originalMinMhz, ...)
RestoreIfOwned(...)
~~~

Replace them with one narrow operation, conceptually:

~~~csharp
internal IntelGpuMinimumClockOperationResult ReleaseToFactoryMinimum(string reason)
{
    // capability must already be available or initialized
    // read current range
    // request min = -1
    // preserve current max semantics
    // SetRange
    // GetRange
    // verify min is negative and max remains semantically unchanged
}
~~~

A small pure policy helper is acceptable, for example:

~~~csharp
internal static bool TryCreateFactoryMinimumReleaseRequest(
    IntelGpuFrequencyRange current,
    out IntelGpuFrequencyRange request)
{
    request = default;

    if (!double.IsFinite(current.Min) || !double.IsFinite(current.Max))
        return false;

    request = new(
        Min: -1,
        Max: current.Max >= 0 ? current.Max : -1);

    return true;
}
~~~

Use the existing readback-verification policy.

For a `min = -1` request, successful readback requires:

~~~text
readback.min < 0
~~~

Do not require the readback to numerically equal `-1`; Intel documents a negative output as "no external minimum frequency limit is in effect."

Keep the current maximum verification semantics.

---

## 10. Simplify ApplyMinimum

Current `ApplyMinimum()` includes marker admission/capture logic.

After this PR its responsibilities should be only:

~~~text
capability available
→ validate selected target against current selectable driver clocks
→ GetRange()
→ ensure target does not conflict with an explicit current max
→ SetRange(min = selected game target, preserve max)
→ GetRange()
→ verify
→ return/log result
~~~

No marker read.

No original-min capture.

No adapter-marker comparison.

No marker persistence.

---

## 11. Game-only reconcile algorithm

Replace the current Game → Device → original-restore logic with:

~~~csharp
private IntelGpuMinimumClockOperationResult ReconcileLoadedEffective(
    ProfileLoadResult loaded,
    string reason)
{
    if (!loaded.CanSafelyReplace)
    {
        var release = ReleaseToFactoryMinimum(reason + "ProfileUnavailable");
        return FailedOperation(
            release.Succeeded
                ? "Profile state is not safe to read."
                : $"Profile state is not safe to read; factory minimum release failed: {release.FailureReason}");
    }

    var active = _activeProfileResolver(loaded.Document);
    var game = active?.Performance.GpuMinimumClock;

    if (game is not { Enabled: true })
        return ReleaseToFactoryMinimum(reason + "NoEnabledActiveGame");

    // read AC/DC source
    // unknown/read failure => release to -1 and report failure

    // select only the active game's AC or DC target

    // invalid/unsupported saved target
    // => release to -1 and report SavedTargetUnsupportedByCurrentDriver

    return ApplyMinimum(canonicalGameTarget, reason);
}
~~~

Exact method names may follow the current file.

The important contract is binary:

~~~text
valid enabled active game GPU Min
→ apply game target

otherwise
→ -1
~~~

---

## 12. Per-game semantics after Device removal

Update the semantic comments/tests inherited from PR3.

Old PR3 semantics:

~~~text
per-game Off/null
→ inherit Device
~~~

New final semantics:

~~~text
GpuMinimumClock == null
→ no game GPU floor
→ factory minimum (-1) when this is the active profile

GpuMinimumClock.Enabled == false
→ preserve saved AC/DC game values for future re-enable
→ no game GPU floor
→ factory minimum (-1) when this is the active profile

GpuMinimumClock.Enabled == true
→ active enabled profile applies its current AC/DC game floor
~~~

Whole-profile `Enabled == false` remains the outer gate and therefore also resolves to factory minimum.

Do not delete saved game AC/DC values when the per-game feature is toggled Off.

---

## 13. Startup behavior

Startup must no longer restore or inspect a pre-Addon original minimum.

Startup flow:

~~~text
Runtime startup
→ initialize IGCL
→ discover integrated Intel GPU frequency capability
→ read hardware range
→ read available clocks
→ derive selectable game clocks
→ run game-only effective reconcile once profile/session startup is ready
~~~

Result:

~~~text
active enabled game + game GPU Min enabled
→ apply game rail

otherwise
→ min = -1
~~~

This deliberately means the Addon does not preserve an arbitrary minimum-frequency floor previously set by another application.

That is the intended product policy.

Do not add external-owner detection.

---

## 14. Game start / exit / change behavior

Reuse the existing event-driven host paths.

### 14.1 Game start

~~~text
active game appears
→ active profile resolved
→ profile GPU Min On
   → apply current AC/DC game target

→ profile GPU Min Off/null or whole profile Off
   → min = -1
~~~

### 14.2 Game exit

Steam:

~~~text
ActualRunningAppId → 0
→ ReconcileEffectiveGameProfile(...)
→ GPU reconcile
→ active game = none
→ min = -1
~~~

XBOX:

~~~text
ActiveGame → null
→ ReconcileEffectiveGameProfile(...)
→ GPU reconcile
→ active game = none
→ min = -1
~~~

This is a required acceptance criterion.

### 14.3 Game A → Game B

If both enabled game profiles have GPU floors:

~~~text
Game A target 1900
→ Game B target 2100
→ directly apply 2100
~~~

No forced intermediate `-1` write is required.

If Game B has GPU Min Off/null:

~~~text
Game A target 1900
→ Game B no GPU floor
→ min = -1
~~~

Do not add a transition state machine.

---

## 15. Active profile mutations

Keep the PR #705 game-profile mutation pipeline.

For an active Steam/XBOX game:

~~~text
GPU toggle On
→ persist
→ apply active rail

GPU toggle Off
→ persist saved values with Enabled=false
→ reconcile
→ min = -1

active AC/DC rail edit
→ persist
→ apply edited active rail

inactive AC/DC rail edit
→ persist only / existing narrow no-op behavior
~~~

Replace any use of Device snapshot/capability state inside `InProcessAddonFrontendControl` with direct runtime capability access.

Do not create a Device snapshot solely to support game-profile editing.

---

## 16. Unsupported saved target must release to factory policy

Keep the existing fail-close behavior, but change the release target.

Example:

~~~text
saved game target = 1900
current driver selectable clocks no longer include 1900
~~~

Required:

~~~text
do not clamp
do not select nearest
do not fall back to Device
do not keep the previous game's floor

→ min = -1
→ verify
→ report SavedTargetUnsupportedByCurrentDriver (or equivalent)
→ leave persisted game value unchanged
~~~

The user must explicitly choose a currently supported clock.

---

## 17. Unknown AC/DC source must release to factory policy

If the current power source cannot be read:

~~~text
do not guess AC
do not guess DC
do not keep a stale prior rail

→ min = -1
→ verify
→ return/log PowerSourceReadFailed or PowerSourceUnknown
~~~

Reuse `WindowsAcDcPowerNotificationSource`.

No additional watcher or debounce.

---

## 18. Sleep / Hibernate / Resume

Keep the existing real handheld lifecycle path:

~~~text
PowerResumeObserved
→ existing 2.5 second performance settle
→ IntelGpuMinimumClockRuntime.ReconcileAfterResume()
~~~

After settle:

~~~text
active enabled game GPU Min
→ reapply current game rail

no valid game GPU Min
→ min = -1
~~~

Keep the existing bounded IGCL session reinitialize only for:

~~~text
CTL_RESULT_ERROR_DEVICE_LOST
CTL_RESULT_ERROR_DEVICE_UNAVAILABLE
~~~

After successful reinitialize, retry the reconcile once.

Do not add:

- resume epoch;
- periodic IGCL health probe;
- retry manager;
- D3 watcher;
- additional lock/state machine.

---

## 19. Runtime restart / crash policy

This PR removes durable original-minimum recovery state.

Do not replace it with another persistent ownership marker.

A later Runtime startup always re-establishes the current product policy from live game state:

~~~text
active enabled game GPU Min
→ apply game target

otherwise
→ min = -1
~~~

Do not add special crash-only state or a watchdog in this PR.

Normal process shutdown/restart must not gain a separate GPU restore state machine.

The Runtime already has one startup/reconcile authority, and that remains sufficient for this scope.

---

## 20. Uninstall — always return minimum to factory policy

This is mandatory.

Current safe-uninstall ordering is already correct:

~~~text
AddonProcessHost.PrepareForUninstallAsync()
→ GPU release
→ if GPU release fails, block uninstall
→ only then continue existing Full1902 stock-safe controller preparation
~~~

Keep that ordering.

Change `IntelGpuMinimumClockRuntime.PrepareForUninstall()` so it no longer checks whether an ownership marker exists.

It must always attempt to establish/verify:

~~~text
min = -1
~~~

before uninstall can continue.

Conceptually:

~~~csharp
internal IntelGpuMinimumClockOperationResult PrepareForUninstall()
{
    if (_disposed || _shuttingDown)
        return FailedOperation("RuntimeShuttingDown");

    var capability = _capability ?? InitializeReadOnly();

    if (!capability.Available)
    {
        // retain current one bounded Reinitialize attempt
        // no retry manager
    }

    if (!capability.Available)
        return FailedOperation(...);

    return ReleaseToFactoryMinimum("Uninstall");
}
~~~

Required behavior:

~~~text
SetRange(min = -1, preserve max)
→ GetRange()
→ verify min < 0
→ success
→ continue uninstall
~~~

Failure:

~~~text
SetRange failure
readback failure
readback min still externally limited
capability unavailable after the existing bounded reinitialize attempt
→ GPU uninstall release fails
→ AddonProcessHost.PrepareForUninstallAsync() fails
→ final uninstall remains blocked
~~~

Update the host log fields to remove `OwnershipMarkerPresent`.

Do not weaken the current uninstall fail-close contract.

---

## 21. Developer IGCL probe interaction — minimal change only

The Developer IGCL probe is scheduled for a separate cleanup PR.

Do not retire it here.

Only make the minimum changes required by removal of Device/marker semantics.

Current `BlocksDeveloperFrequencyMutation()` checks:

- ownership marker;
- Device GPU setting;
- Steam game GPU settings;
- XBOX game GPU settings.

After this PR it must no longer depend on:

- ownership marker;
- Device GPU setting.

Retain the existing Steam/XBOX configured-profile conflict behavior until the dedicated Developer probe cleanup removes the probe entirely.

Do not redesign the conflict model in this PR.

---

## 22. Logging

Update logs from Device/original terminology to game/factory terminology.

Examples:

~~~text
"Effective minimum GPU clock reconcile completed."
EffectiveSource = active game label or "Factory"
RequestedMinMhz = game target or -1
PowerSource = AC / DC / Unknown
Outcome
Failure
~~~

Remove:

~~~text
OwnershipMarkerPresent
OriginalMinMhz
"Device minimum GPU clock ..."
"original minimum restore"
~~~

Keep operation-level evidence:

~~~text
PreWriteMinMhz
PreWriteMaxMhz
ReadbackMinMhz
ReadbackMaxMhz
NativeSetResult
NativeReadResult
Verified
Failure
~~~

These are useful for real operation failures and hardware validation.

---

## 23. Tests to remove/update

### 23.1 Delete Device-only UI tests

Delete or remove Device-specific cases from:

~~~text
tests/SteamInputAddonforClaw.UiTests/DeviceGpuMinimumClockUiTests.cs
~~~

Retain/move only pure helper coverage that is still required by Steam/XBOX profile editors.

### 23.2 Remove Device frontend tests

Remove Device-only GPU cases from:

~~~text
IntelGpuMinimumClockFrontendTests
DeviceQuickSettingsAggregateTests
FrontendNamedPipeTransportTests
~~~

Update the Device aggregate expected shape.

Keep and strengthen Steam/XBOX game-profile GPU frontend tests.

### 23.3 Remove Device persistence expectations

Update `ProfileStoreTests` so `DevicePerformanceSettings.GpuMinimumClock` is no longer part of the typed model.

Do not add a schema migration test.

A legacy unknown `device.performance.gpuMinimumClock` property may be preserved by extension data, but must not affect runtime policy.

### 23.4 Remove marker-path tests

Remove:

~~~text
IntelGpuMinimumClockOwnershipPath_UsesCanonicalDataRoot
~~~

and any other production marker persistence tests.

---

## 24. Required runtime/policy tests

Add or rewrite focused tests that prove the final policy.

### Capability / selection

Keep:

- actual driver clock-list normalization;
- dynamic lower threshold;
- dynamic upper bound = two supported entries below highest;
- canonical target validation;
- different Intel integrated GPU clock tables without model-specific constants.

### Minimum apply

Verify:

~~~text
game target apply
→ requested min = target
→ current max semantics preserved
→ successful readback required
~~~

### Factory release

Verify:

~~~text
current range min = 2100, max = 2300
→ release request = (-1, 2300)
→ readback min < 0 required

current max < 0
→ release request max = -1
~~~

Remove original-value restore tests.

### Startup

Test:

~~~text
no active game
→ startup reconcile requests min = -1

active enabled Steam game GPU Min On
→ startup reconcile applies game rail

active enabled XBOX game GPU Min On
→ startup reconcile applies game rail

legacy Device GPU JSON only
→ no Device floor
→ min = -1
~~~

### Game transitions

Test:

~~~text
no game → Game A On
→ A target

Game A On → no game
→ -1

Game A On → Game B On
→ B target

Game A On → Game B Off
→ -1

active game profile Enabled=false
→ -1

active game GPU feature Enabled=false
→ -1
~~~

### Error policy

Test:

~~~text
unsupported saved active-game target
→ -1 release attempted
→ failure result reports unsupported saved target

power source read failure/unknown
→ -1 release attempted
→ no stale previous floor remains authoritative
~~~

### Resume

Keep tests proving:

~~~text
2.5 s settle remains
DEVICE_LOST / DEVICE_UNAVAILABLE
→ one bounded reinitialize
→ one retry

healthy path
→ no reinitialize
~~~

### Uninstall

Required:

~~~text
PrepareForUninstall always attempts -1
even without any marker/history

verified -1 release
→ existing Full1902 uninstall preparation may continue

SetRange failure
→ uninstall blocked

readback failure / min still externally limited
→ uninstall blocked

capability unavailable after bounded reinitialize
→ uninstall blocked
~~~

---

## 25. Required frontend/UI acceptance

### Main Device page

~~~text
Minimum GPU Clock card does not exist
no Device GPU toggle
no Device GPU AC/DC sliders
~~~

### Steam Profile page

Keep:

~~~text
Minimum GPU Clock toggle
AC/DC game values
300 ms trailing slider commit behavior
failed RPC preserves/recoverable dirty-state behavior from PR #705
~~~

### XBOX Profile page

Same game-only behavior as Steam.

### Overlay / Quick Settings Profile

Keep the existing Profile GPU rows.

No Device GPU row is added.

No Overlay protocol change.

---

## 26. Files expected to change

The implementation should review at least the following current files.

### Production/runtime

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
src/SteamInputAddonforClaw/Profiles/DeviceSettings.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

### Frontend contracts/transport

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
~~~

Also update the interface containing the Device GPU methods if it lives separately from these files.

### Main UI

~~~text
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs
~~~

Add/move one small shared pure GPU slider policy helper only if required after deleting the Device-page nested helper.

### Tests

At minimum review:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/DeviceQuickSettingsAggregateTests.cs
tests/SteamInputAddonforClaw.Tests/ProfileStoreTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/AddonDataPathsTests.cs
tests/SteamInputAddonforClaw.Tests/XboxGameProfileFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/XboxGameProfileMutationsTests.cs
tests/SteamInputAddonforClaw.UiTests/DeviceGpuMinimumClockUiTests.cs
~~~

Search the complete repository for all of:

~~~text
DeviceGpuMinimumClock
FrontendGpuMinimumClock
CaptureGpuMinimumClock
SetDeviceGpuMinimumClock
OriginalMinMhz
IntelGpuMinimumClockOwnership
OwnershipMarkerPresent
RestoreOriginalMinimum
RestoreIfOwned
TryCreateRestoreRequest
~~~

No production reference to retired Device/original-marker semantics should remain after the PR.

Historical work-order documents are documentation history and do not need rewriting.

---

## 27. Explicit non-goals

Do not add:

- Device/global Minimum GPU Clock replacement;
- global default GPU floor;
- external GPU-setting coexistence or restoration;
- pre-Addon minimum capture;
- ownership marker v2;
- registry ownership state;
- new GPU manager/service/provider layer;
- new game process watcher;
- new AC/DC watcher;
- polling;
- periodic health checks;
- generalized retry machinery;
- resume epoch/barrier;
- GPU maximum-frequency control;
- model-specific B390/140V MHz tables;
- PL1 production control;
- Developer IGCL probe cleanup;
- broad production file split/refactor;
- CTW integration.

Keep this PR focused on the product-policy correction.

---

## 28. Acceptance criteria

The PR is complete only when all of the following are true:

1. Main Device page has no Minimum GPU Clock control.
2. `DevicePerformanceSettings` has no typed GPU Minimum Clock setting.
3. Steam and XBOX game profiles retain their Minimum GPU Clock feature.
4. Active enabled game + GPU feature On applies only that game's current AC/DC target.
5. Game exit results in `min = -1`.
6. Active game with GPU feature Off/null results in `min = -1`.
7. Whole game profile Off results in `min = -1`.
8. No Device fallback exists.
9. No `OriginalMinMhz` capture or restore exists.
10. No production Intel GPU minimum-clock ownership marker exists.
11. Startup performs capability discovery and game-only reconcile; with no active enabled game floor it establishes `min = -1`.
12. Unsupported saved game target releases to `-1` rather than clamping/falling back.
13. Unknown/unreadable power source releases to `-1`.
14. Existing AC/DC event path remains the only power-source watcher.
15. Existing 2.5 s resume settle remains.
16. Only DEVICE_LOST / DEVICE_UNAVAILABLE use the existing bounded reinitialize + one retry.
17. Uninstall always attempts and verifies factory minimum release, regardless of any prior marker/history.
18. Uninstall blocks if factory release cannot be verified.
19. Maximum-frequency semantics are not taken over or reset by this feature.
20. Frontend protocol is bumped 63 → 64.
21. Overlay protocol is unchanged.
22. Device GPU frontend/RPC contracts are removed.
23. Steam/XBOX game GPU frontend/RPC contracts remain.
24. No new authority/state/watchdog/retry abstraction is introduced.
25. Tests cover game start, game exit, game-to-game transition, Off/null, startup, AC/DC, resume, unsupported target, and uninstall factory release.

---

## 29. Final target architecture

After this PR:

~~~text
Runtime startup
    ↓
IGCL capability discovery
    ↓
ActiveProfileResolver
    │
    ├─ active enabled Steam/XBOX game
    │  + Minimum GPU Clock On
    │       ↓
    │    current AC/DC game target
    │       ↓
    │    minimum-only SetRange
    │       ↓
    │    readback verification
    │
    └─ otherwise
           ↓
        min = -1
           ↓
        Intel factory minimum-frequency policy
           ↓
        readback verification
~~~

Uninstall uses the same release primitive first:

~~~text
PrepareForUninstall
→ min = -1
→ verify
→ failure: block uninstall
→ success: continue existing Full1902 stock-safe controller teardown
~~~

The resulting Minimum GPU Clock implementation has one clear purpose:

> **temporary per-game Intel GPU minimum-frequency floor, automatically released to Intel factory policy whenever no active game explicitly owns it.**
