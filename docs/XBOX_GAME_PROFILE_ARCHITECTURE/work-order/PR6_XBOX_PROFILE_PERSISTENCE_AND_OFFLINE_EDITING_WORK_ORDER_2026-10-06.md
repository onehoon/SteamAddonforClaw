# Work Order — XBOX PR6: Profile Persistence + Offline Editing

> **Date:** 2026-10-06  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Current main reviewed:** `main@1370f437d955d457e23dc9795f085a3a9385a601`  
> **Feature code baseline:** PR #691 squash `fc28ca88efad0cc9ab711f9cdc5e3499219b087a`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the active Full1902 documents it references  
> **Previous XBOX phase:** PR #691 added the separate read-only XBOX Main App catalog backed by production `XboxInstalledGameCatalog`  
> **Scope:** persist XBOX profiles by canonical string key and make the existing XBOX page support offline/pre-launch Favorite + profile editing  
> **Out of scope:** production active XBOX session runtime, automatic XBOX profile apply, shared ActiveProfileTarget, per-game M1/M2, Overlay XBOX profile projection, Active Session Diagnostic retirement

---

## 1. Goal

Turn the PR #691 XBOX page from a read-only installed-game list into an offline/pre-launch profile editor.

Target user flow:

~~~text
XBOX
→ installed game list
→ choose a game
→ edit and persist:

Favorite
Profile Enabled
CPU Boost
TDP Control
Windows Power Mode
Intel FPS Limit
Resolution
~~~

This must work while the XBOX game is not running.

This PR is persistence + editing only.

It must **not** apply any XBOX profile to Windows/hardware yet.

The future production active-session PR will connect an active XBOX identity to the existing apply runtimes.

---

## 2. Locked phase boundary

PR6 ends here:

~~~text
installed XBOX game
→ canonical Xbox key
→ ProfileDocument.XboxGames
→ XBOX profile mutations
→ frontend snapshot/mutations
→ XBOX Main App profile editor
~~~

PR6 does **not** implement:

~~~text
running Xbox game
→ XboxGameSessionRuntime
→ ActiveProfileTarget
→ CPU/TDP/Power/FPS/Resolution apply
~~~

and does not implement:

~~~text
per-game M1/M2
Overlay active XBOX profile
~~~

Those remain later phases.

This separation is intentional.

Do not let “the game may already be running” cause this PR to call the Steam-oriented live apply runtimes.

---

## 3. Current persistence authority

The product already has one durable Device/Profile document:

~~~text
SteamInputAddonforClaw-Data/profiles.json
~~~

owned by:

~~~text
ProfileStore
ProfileMutationGate
~~~

Current document:

~~~csharp
ProfileDocument
{
    SchemaVersion
    Device
    Games       // Steam AppID -> GameProfile
}
~~~

PR6 adds one sibling collection to the same document.

Do not create:

- xbox-profiles.json;
- another profile store;
- another persistence root;
- another mutation gate;
- a frontend-owned cache/database.

Target:

~~~csharp
ProfileDocument
{
    SchemaVersion
    Device
    Games
    XboxGames
}
~~~

One file.

One `ProfileStore`.

One shared `ProfileMutationGate`.

---

## 4. Add ProfileDocument.XboxGames

Update:

~~~text
src/SteamInputAddonforClaw/Profiles/ProfileDocument.cs
~~~

Add:

~~~csharp
public Dictionary<string, XboxGameProfile> XboxGames { get; init; } = [];
~~~

Key = the existing canonical XBOX identity from:

~~~text
XboxGameIdentity.Key
~~~

Examples:

~~~text
store:9PK8PHLCQDF6
store:9NBLGGH2JHXJ
pfn:Microsoft.Example_8wekyb3d8bbwe
identity:SAMPLE.GAME|CN%3DSAMPLE|PC
~~~

The dictionary key is authoritative.

Never use:

- DisplayName;
- PID;
- HWND;
- package full name/version;
- install path;
- config path;
- TitleId alone

as profile identity.

---

## 5. Keep schema version 1

Current `ProfileDocument` policy explicitly says:

> purely additive fields do not require a schema bump.

`XboxGames` is an additive sibling field.

Keep:

~~~csharp
ProfileDocument.CurrentSchemaVersion = 1;
~~~

Do not invent a version-2 migration solely because `xboxGames` was added.

Old v1 files that contain no `xboxGames` must load as:

~~~text
XboxGames = empty
~~~

Current extension-data behavior already preserves unknown top-level fields for compatible old/new round trips.

Retain that model.

---

## 6. Add a separate XboxGameProfile type

Do not reuse `GameProfile` directly.

`GameProfile` is explicitly documented as Steam-AppID keyed.

Add a separate type, recommended:

~~~text
src/SteamInputAddonforClaw/Profiles/XboxGameProfile.cs
~~~

Shape for PR6:

~~~csharp
public sealed record XboxGameProfile
{
    public bool Enabled { get; init; }
    public bool Favorite { get; init; }
    public string? DisplayName { get; init; }

    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
~~~

Reuse the existing nested types:

~~~text
GamePerformanceOverrides
GameCpuBoostSettings
GameTdpSettings
GamePowerModeSettings
GameFpsLimitSettings
GameDisplayOverrides
GameDisplayResolution
~~~

Do not duplicate those models as XboxCpuBoostSettings / XboxTdpSettings / etc.

Their setting semantics are platform-independent.

---

## 7. Do not add Controller/M1/M2 persistence yet

The full architecture later allows:

~~~text
XboxGameControllerOverrides
BackButtonMapping override
~~~

That is Phase X4, not PR6.

Do not add an empty `Controller` section merely as a placeholder.

Do not add:

~~~text
XboxGameControllerOverrides
BackButtonMapping
UseGlobalMapping
M1/M2 fields
~~~

in PR6.

The profile document is additive; the controller section can be added when there is a real consumer.

---

## 8. Do not broaden the frontend catalog with technical identity metadata

PR #691 deliberately narrowed the frontend catalog to:

~~~csharp
FrontendXboxGameCatalogEntry(
    string Key,
    string DisplayName)
~~~

Keep that technical-information boundary.

PR6 may add:

~~~csharp
bool Favorite
~~~

to the catalog entry.

Recommended:

~~~csharp
public sealed record FrontendXboxGameCatalogEntry(
    string Key,
    string DisplayName,
    bool Favorite = false);
~~~

Do not add:

- StoreId;
- TitleId;
- PFN;
- PackageFullName;
- ConfigPath;
- ExecutableList;
- AUMID;
- process path.

The normal UI still renders the game name and user-facing profile state, not platform diagnostics.

### Persisted identity metadata

Do not add a package-identity sidecar in PR6 if doing so requires:

- broadening the frontend DTO;
- rescanning packages on every mutation;
- adding a Runtime catalog cache/manager.

The canonical dictionary key is sufficient identity authority for this phase.

`DisplayName` is persisted as descriptive metadata.

If a later concrete diagnostic/recovery requirement needs more persisted package evidence, add only the fields that requirement proves necessary.

---

## 9. ProfileStore structural validation

Update:

~~~text
src/SteamInputAddonforClaw/Profiles/ProfileStore.cs
~~~

`HasValidStructure` must require:

~~~text
document.XboxGames != null
~~~

and every XBOX entry must have:

~~~text
profile != null
profile.Performance != null
profile.Display != null
~~~

For an enabled XBOX profile, require the same minimum complete performance structure as an enabled Steam profile:

~~~text
CpuBoost != null
Tdp != null
Tdp.Ac != null
Tdp.Dc != null
~~~

Do not allow:

~~~json
"xboxGames": null
~~~

or:

~~~json
"xboxGames": {
  "store:...": null
}
~~~

to load as a safe writable document.

Preserve the current fail-safe contract:

~~~text
Malformed / UnsupportedSchemaVersion / ReadFailure
→ CanSafelyReplace = false
→ never overwrite original profiles.json
~~~

---

## 10. ProfileStore logging

Existing logs report Steam `GameCount`.

Extend the useful persistence log metadata with a separate XBOX count.

For example:

~~~text
GameCount
XboxGameCount
~~~

Do not merge them into one ambiguous count.

No per-profile Info logging is needed.

---

## 11. Add XboxGameProfileMutations

Add one narrow XBOX persistence owner:

~~~text
src/SteamInputAddonforClaw/Profiles/XboxGameProfileMutations.cs
~~~

Recommended constructor:

~~~csharp
internal XboxGameProfileMutations(
    ProfileStore store,
    ProfileMutationGate gate,
    HandheldDeviceModelId? modelId = null)
~~~

It must use the **same** `ProfileStore` and **same** `ProfileMutationGate` as:

~~~text
GameProfileMutations
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
GameDisplayResolutionRuntime
IntelFrameLimiterRuntime
~~~

Do not create another lock/gate.

The shared gate is the one serialization authority for the one `profiles.json` document.

---

## 12. Keep Steam and XBOX mutation owners separate

Do not rewrite `GameProfileMutations` into:

~~~text
GenericGameProfileMutations<TId>
IGameProfileBackend
PlatformProfileProvider
GameIdentity abstraction
~~~

PR6 has two known persistence domains:

~~~text
Steam  → uint AppID → ProfileDocument.Games
XBOX   → string Key → ProfileDocument.XboxGames
~~~

A separate `XboxGameProfileMutations` is simpler and safer.

Small repeated persistence code is acceptable here.

Do not introduce a platform framework just to remove a few repeated lines.

Likewise, avoid a risky Steam mutation refactor in this PR.

---

## 13. Validate XBOX mutation keys narrowly

Every XBOX profile mutation must reject:

~~~text
null
empty
whitespace
unknown key format
prefix with no payload
~~~

Accepted canonical families:

~~~text
store:<non-empty>
pfn:<non-empty>
identity:<non-empty>
~~~

A small static validation helper adjacent to `XboxGameProfileMutations` or `XboxGameIdentity` is sufficient.

Do not re-derive identity from DisplayName.

Do not attempt package discovery during mutation.

Do not create another identity parser hierarchy.

Invalid key:

~~~text
→ MutationOutcome.InvalidTarget
→ no persistence write
~~~

---

## 14. XBOX profile capture

Recommended mutation-owner capture:

~~~csharp
internal sealed record Capture(
    string Key,
    XboxGameProfile Profile,
    bool Exists,
    bool PersistenceWritable);
~~~

~~~csharp
internal Capture CaptureProfile(string key)
~~~

Semantics mirror Steam:

### Existing entry

~~~text
XboxGames[key] exists
→ return saved profile
→ complete missing editable defaults for presentation in memory
→ Exists = true
~~~

### No entry

~~~text
no XboxGames[key]
→ return a non-persisted completed default profile
→ Exists = false
→ PersistenceWritable reflects ProfileStore load safety
~~~

Capture must not save.

Capture must not apply anything.

---

## 15. Profile enable/disable semantics

Mirror the mature Steam behavior.

### Enable

~~~text
SetEnabled(key, true, displayName)
→ load latest document under shared gate
→ obtain existing XBOX entry or new XboxGameProfile
→ complete CPU/TDP/Power defaults from Device values
→ preserve existing saved feature values
→ Enabled = true
→ persist DisplayName if supplied
→ save once
~~~

### Disable

~~~text
SetEnabled(key, false, ...)
→ preserve all existing profile values
→ Enabled = false
→ save once
~~~

Disabling must not delete the entry.

Re-enabling must restore the user's previous values.

---

## 16. Initial profile defaults

Use the same practical default semantics as current Steam profile creation.

### CPU Boost

When device CPU values exist:

~~~text
copy persisted Device CPU AC/DC
Enabled = true
~~~

otherwise current Steam fallback:

~~~text
AC = Enabled
DC = Enabled
Enabled = true
~~~

### TDP

When complete Device TDP AC/DC values exist:

~~~text
copy them
Enabled = true
~~~

otherwise current Steam fallback:

~~~text
AC 20 / 22
DC 20 / 22
Enabled = true
~~~

### Windows Power Mode

When the Device has a complete persisted Power Mode pair:

~~~text
copy it
Enabled = true
~~~

otherwise:

~~~text
PowerMode = null
~~~

### Intel FPS

Keep current Steam semantics:

~~~text
no persisted FpsLimit required at initial enable
frontend presents 60 / 60 disabled defaults
first FPS enable/value mutation creates the section
~~~

### Resolution

Independent optional display override:

~~~text
null = Do not change
~~~

Do not read current hardware/Windows state in order to invent XBOX defaults.

Use persisted Device values / existing fallback rules only.

---

## 17. Favorite semantics

Add:

~~~csharp
CaptureFavoriteKeys()
SetFavorite(string key, bool favorite, string? displayName)
~~~

Behavior mirrors Steam.

Favorite ON for a game without a profile:

~~~text
create disabled XboxGameProfile
Favorite = true
DisplayName = current catalog name
do not populate/enable performance ownership
~~~

Favorite OFF for an absent entry:

~~~text
no-op success
~~~

Favorite mutations must preserve:

- Enabled;
- performance values;
- display resolution;
- future extension data.

---

## 18. Resolution semantics

Mirror current Steam persistence behavior.

~~~text
SetResolution(key, resolution, displayName)
~~~

may create a disabled XBOX profile when a non-null resolution is selected.

~~~text
resolution = null
+ no existing profile
→ no ghost profile
~~~

Resolution is offline persistence only in this PR.

Do not call:

~~~text
GameDisplayResolutionRuntime.Reconcile(...)
~~~

from an XBOX mutation.

---

## 19. CPU Boost / TDP / Power / FPS mutations

Provide XBOX equivalents of the current Steam persistence mutations.

Required mutation-owner operations:

~~~text
SetCpuBoostEnabled
SetCpuBoostAc
SetCpuBoostDc

SetTdpEnabled
SetTdp

SetPowerModeEnabled
SetPowerModeAc
SetPowerModeDc

SetFpsLimitEnabled
SetFpsLimitAc
SetFpsLimitDc

SetResolution
SetFavorite
SetEnabled
~~~

Use existing nested types and validation.

### TDP validation

Use the same:

~~~text
HandheldDeviceModelId
MsiClawTdpPolicy
~~~

validation as Steam.

Invalid PL1/PL2:

~~~text
→ InvalidTarget
→ no save
~~~

### FPS validation

Keep:

~~~text
40..120
~~~

as current supported range.

---

## 20. Add the XBOX mutation owner to AddonProcessHost

Current constructor creates:

~~~text
_profileStore
_profileMutationGate
_gameProfileMutations
_cpuBoostRuntime
_tdpRuntime
_powerModeRuntime
_displayResolutionRuntime
_intelFpsRuntime
~~~

Add:

~~~text
_xboxGameProfileMutations
~~~

constructed with:

~~~text
the same _profileStore
the same _profileMutationGate
~~~

When startup resolves a supported TDP device model and currently calls:

~~~csharp
_gameProfileMutations.SetModelId(tdpModel);
~~~

also provide that same model to the XBOX mutation owner.

Do not add another hardware probe.

Do not add another model authority.

---

## 21. Extend the XBOX catalog with Favorite only

Current PR #691 catalog mapping:

~~~text
XboxInstalledGameCatalog
→ Key + DisplayName
~~~

At frontend projection time:

~~~text
CaptureFavoriteKeys()
+
production installed catalog
→ FrontendXboxGameCatalogEntry(Key, DisplayName, Favorite)
~~~

Do not modify `XboxInstalledGameCatalog` to know about profiles.

Catalog discovery and user profile persistence remain separate owners.

Do not make the package scanner read `profiles.json`.

Merge at the frontend/application projection boundary, like the current Steam catalog does.

---

## 22. Add FrontendXboxGameProfileSnapshot

Add an XBOX-specific snapshot.

Recommended:

~~~csharp
public sealed record FrontendXboxGameProfileSnapshot(
    string Key,
    string? DisplayName,
    bool Exists,
    bool Enabled,
    FrontendGameCpuBoostConfiguration CpuBoost,
    FrontendGameTdpConfiguration Tdp,
    bool PersistenceWritable,
    FrontendTdpLimits? Limits,
    FrontendGameResolution? Resolution = null,
    FrontendGamePowerModeConfiguration? PowerMode = null,
    FrontendGameFpsLimitConfiguration? FpsLimit = null);
~~~

Reuse the existing nested frontend configuration records.

Do not create:

~~~text
FrontendXboxCpuBoostConfiguration
FrontendXboxTdpConfiguration
FrontendXboxPowerModeConfiguration
FrontendXboxResolution
~~~

when semantics are identical.

The XBOX-specific identity remains:

~~~text
string Key
~~~

instead of Steam:

~~~text
uint AppId
~~~

---

## 23. XBOX mutation result

Reuse the existing mutation outcome enum:

~~~text
FrontendGameProfileMutationOutcome
~~~

Recommended:

~~~csharp
public sealed record FrontendXboxGameProfileMutationResult(
    FrontendGameProfileMutationOutcome Outcome,
    string? FailureMessage,
    FrontendXboxGameProfileSnapshot Snapshot)
{
    public bool Succeeded =>
        Outcome == FrontendGameProfileMutationOutcome.Succeeded;
}
~~~

In PR6:

~~~text
ApplyFailed
~~~

must never be produced by XBOX mutations because no live apply occurs.

Later active-profile integration may use the existing outcome if a persist-then-apply mutation gains a real XBOX apply boundary.

---

## 24. Frontend XBOX profile methods

Add XBOX-specific methods to `IAddonFrontendControl`.

Required:

~~~text
CaptureXboxGameProfileAsync(string key)

SetXboxGameProfileFavoriteAsync(string key, bool favorite, string? displayName)
SetXboxGameProfileEnabledAsync(string key, bool enabled, string? displayName)

SetXboxGameProfileCpuBoostEnabledAsync(string key, bool enabled)
SetXboxGameProfileCpuBoostAcAsync(string key, CpuBoostMode mode)
SetXboxGameProfileCpuBoostDcAsync(string key, CpuBoostMode mode)

SetXboxGameProfileTdpEnabledAsync(string key, bool enabled)
SetXboxGameProfileTdpAsync(string key, FrontendGameTdpConfiguration configuration)

SetXboxGameProfilePowerModeEnabledAsync(string key, bool enabled)
SetXboxGameProfilePowerModeAcAsync(string key, WindowsPowerMode mode)
SetXboxGameProfilePowerModeDcAsync(string key, WindowsPowerMode mode)

SetXboxGameProfileFpsLimitEnabledAsync(string key, bool enabled)
SetXboxGameProfileFpsLimitAcAsync(string key, int fps)
SetXboxGameProfileFpsLimitDcAsync(string key, int fps)

SetXboxGameProfileResolutionAsync(
    string key,
    FrontendGameResolution? resolution,
    string? displayName)
~~~

Do not add:

~~~text
CaptureActiveXboxGameProfileAsync
~~~

in PR6.

There is not yet a production active XBOX session authority.

---

## 25. InProcessAddonFrontendControl — persistence only

Add:

~~~text
XboxGameProfileMutations? _xboxGameProfileMutations
~~~

and map capture/mutation results to XBOX frontend snapshots.

Critically, XBOX mutation paths in PR6 must **not** call:

~~~text
ReconcileGame(...)
_cpuBoostRuntime.Reconcile(...)
_cpuBoostRuntime.ReconcileWithResult(...)
_tdpRuntime.ReconcileCurrent(...)
_powerModeRuntime.Reconcile(...)
_powerModeRuntime.ReconcileWithResult(...)
_intelFpsRuntime.Reconcile(...)
_intelFpsRuntime.ReconcileWithResult(...)
_displayResolutionRuntime.Reconcile(...)
~~~

even when an XBOX title happens to be running during manual testing.

PoC B / `XboxGameSessionDiagnostic` is diagnostic evidence only.

It is not production profile authority.

The mutation result is:

~~~text
persist
→ capture saved snapshot
→ return
~~~

No apply.

---

## 26. Snapshot capability metadata may reuse existing runtime capability reads

It is acceptable for XBOX snapshot construction to use existing **read-only capability metadata**:

### TDP

~~~text
_tdpRuntime?.CaptureSnapshot().Policy
→ FrontendTdpLimits
~~~

### Intel FPS

~~~text
_intelFpsRuntime?.Available
_intelFpsRuntime?.UnavailableReason
~~~

This is for editor availability/range presentation only.

It is not an apply.

Do not initialize a second TDP/IGCL runtime for XBOX.

---

## 27. StateInvalidated policy

Do not use broad `StateInvalidated` as an XBOX profile apply trigger.

PR6 has no XBOX runtime consumer.

The mutation RPC already returns the authoritative saved snapshot.

The XBOX page should update from that result.

Favorite can update the page-local catalog entry from the returned success.

No broad Runtime invalidation is required merely to save an offline XBOX profile.

If implementation reuses `StateInvalidated` only for benign Main UI refresh parity, it must not cause:

- package rescans;
- Steam profile apply;
- hardware apply;
- Overlay XBOX behavior.

Simpler is preferred: keep PR6 local to the returned XBOX mutation result.

---

## 28. Frontend transport

Current protocol after PR #691:

~~~text
FrontendTransportProtocol.CurrentVersion = 54
~~~

PR6 adds persisted XBOX profile snapshot/mutation RPCs.

Bump:

~~~text
54 → 55
~~~

Document:

~~~text
Version 55:
add XBOX profile persistence/capture/mutation RPCs and Favorite on the
production XBOX catalog entry. No active-XBOX apply contract is added.
~~~

Add separate `FrontendRpcMethod` values for the methods in section 24.

Do not overload Steam RPCs.

Do not encode the XBOX key in a uint.

---

## 29. Wire request records

Add narrow request DTOs in the existing frontend transport layer.

Examples:

~~~csharp
internal sealed record CaptureXboxGameProfileRequest(string Key);

internal sealed record SetXboxGameProfileEnabledRequest(
    string Key,
    bool Enabled,
    string? DisplayName);

internal sealed record SetXboxGameProfileFavoriteRequest(
    string Key,
    bool Favorite,
    string? DisplayName);

internal sealed record SetXboxGameProfileCpuBoostAcRequest(
    string Key,
    CpuBoostMode Mode);

internal sealed record SetXboxGameProfileTdpRequest(
    string Key,
    FrontendGameTdpConfiguration Configuration);
~~~

Continue equivalently for DC/Power/FPS/Resolution.

Do not create a general:

~~~text
MutateAnyGameProfileRequest
Platform + TargetId + arbitrary payload
~~~

for two known domains.

---

## 30. Named-pipe validation

The Runtime mutation owner remains the final validator.

The transport should still reject malformed payload shapes through existing decode/error behavior.

At minimum:

~~~text
missing Key
null Key where non-null is required
invalid enum JSON
malformed configuration payload
~~~

must fail boundedly.

A syntactically valid but noncanonical key reaches the mutation owner and returns:

~~~text
InvalidTarget
~~~

without persistence.

Do not add a second independent canonical-key parser in the pipe server.

---

## 31. XBOX page — catalog cards

Update the current:

~~~text
XboxPage.xaml
XboxPage.xaml.cs
~~~

PR #691 cards are intentionally non-interactive.

PR6 makes them profile entries.

Match the mature Steam page visual language:

~~~text
game name
Favorite star
click/select game
~~~

The card still must not display:

- canonical key;
- StoreId;
- TitleId;
- PFN;
- package/path information.

`Tag` / page-local state may carry `FrontendXboxGameCatalogEntry` including Key.

Key remains invisible.

---

## 32. XBOX page — detail view

Add a detail view modeled on the current Steam `ProfilePage`.

Required user-facing detail:

~~~text
Back

<Game Name>                              [Profile toggle]

TDP Control
  Plugged in
  On battery

CPU Boost
  Plugged in
  On battery

Windows Power Mode
  Plugged in
  On battery

Intel FPS Limit
  Plugged in
  On battery

Resolution
~~~

Use the same labels, controls, ranges, and semantic layout where the settings are identical.

Do not invent a separate XBOX settings UX.

Do not show technical identity.

---

## 33. XBOX page — Favorite

Add the same user-facing favorite behavior as Steam:

~~~text
☆ / ★
~~~

Catalog sorting:

~~~text
Favorite descending
then DisplayName case-insensitive
then Key internal deterministic tiebreaker
~~~

Search remains:

~~~text
DisplayName only
~~~

Do not make Key searchable.

---

## 34. XBOX page — detail selection lifecycle

On game selection:

~~~text
selectedKey = entry.Key
selected display = entry.DisplayName
catalog panel hidden
detail panel visible
Refresh button hidden
CaptureXboxGameProfileAsync(selectedKey)
~~~

On Back:

~~~text
cancel detail debounce requests
clear selected detail state
detail hidden
catalog visible
Refresh visible
~~~

When a response returns:

~~~text
response.Key must still equal selectedKey
~~~

before rendering.

This is the XBOX equivalent of the existing Steam AppID stale-response guard.

No global epoch/state manager is required.

---

## 35. XBOX page — feature editing behavior

Mirror current Steam page behavior.

### Profile toggle OFF

Keep saved values.

Disable nested performance editors.

### CPU Boost / TDP / Power / FPS feature toggles

Only editable when:

~~~text
profile Exists
profile Enabled
PersistenceWritable
~~~

plus feature-specific availability.

### TDP sliders

Reuse:

~~~text
DevicePage.TdpDraftPolicy
FrontendTdpLimits
current debounce behavior
~~~

Do not duplicate TDP linked-slider policy.

### FPS sliders

Keep:

~~~text
40..120
current 275 ms debounce behavior
~~~

### Resolution

Use the same items and order as Steam:

~~~text
Do not change
1920 × 1200
1920 × 1080
1680 × 1050
1440 × 900
~~~

Resolution remains editable according to the current Steam persistence semantics and must not apply live in PR6.

---

## 36. Do not refactor Steam ProfilePage into a shared generic page

The XBOX UI is semantically similar to Steam, but PR6 is not a UI-framework refactor.

Do not create:

~~~text
GenericProfilePage<T>
PlatformProfileViewModel
IProfilePageAdapter
GameProfilePageBase
universal profile editor framework
~~~

Copy/reuse small presentation patterns where necessary.

Reuse existing shared data types/policies.

Do not destabilize the mature Steam page for code-deduplication aesthetics.

A later measured maintenance problem can justify a focused UI refactor.

---

## 37. Installed vs saved-but-uninstalled profiles

Initial XBOX page continues to show:

~~~text
installed titles only
~~~

Do not delete an XBOX profile when its game disappears from the installed catalog.

Therefore:

~~~text
game uninstall
→ no catalog card
→ XboxGames[key] retained
~~~

Reinstall with the same canonical key:

~~~text
→ saved Favorite/profile values return
~~~

Do not add a “Saved but not installed” section in PR6.

---

## 38. ProfileStore backward/forward compatibility tests

Extend `ProfileStoreTests`.

Required:

1. existing schema-v1 file with no `xboxGames` loads successfully with empty collection;
2. Steam `Games` and `XboxGames` round-trip independently;
3. canonical XBOX string keys round-trip unchanged;
4. `xboxGames: null` is malformed and original file is preserved;
5. null XBOX profile entry is malformed;
6. null XBOX Performance/Display is malformed;
7. enabled XBOX profile missing required CPU/TDP structure is malformed;
8. disabled incomplete XBOX profile remains loadable;
9. unknown property under `XboxGameProfile` round-trips;
10. unknown properties under reused Performance/Display still round-trip;
11. adding/saving XBOX profiles does not alter existing Steam profiles;
12. adding/saving Steam profiles does not alter XBOX profiles;
13. malformed/unsupported/read-failure document is never replaced by an XBOX mutation;
14. schema remains version 1.

---

## 39. XboxGameProfileMutations tests

Add focused tests.

Required:

### Identity

1. valid store key accepted;
2. valid pfn key accepted;
3. valid identity key accepted;
4. empty/whitespace/unknown-prefix key rejected with zero write.

### Enable/disable

5. enabling creates a profile from Device defaults;
6. fallback CPU/TDP values match current Steam semantics when Device values are absent;
7. disabling preserves saved values;
8. re-enable preserves previous custom values;
9. DisplayName is metadata and changing it never changes the dictionary key.

### Favorite

10. Favorite ON creates a disabled entry;
11. Favorite OFF on absent key is no-op success;
12. Favorite preserves existing performance/display.

### CPU

13. AC/DC changes preserve opposite side;
14. CPU feature toggle preserves values.

### TDP

15. valid pair saves;
16. feature toggle preserves pair;
17. invalid model/range returns InvalidTarget with no write.

### Power Mode

18. AC/DC changes preserve opposite side;
19. feature toggle preserves values;
20. absent Device Power Mode keeps profile PowerMode unavailable until a valid persisted section exists according to current completion semantics.

### FPS

21. first enable can create 60/60 FPS section for an enabled profile;
22. AC/DC edits preserve opposite side;
23. out-of-range FPS rejected.

### Resolution

24. non-null resolution can create disabled profile;
25. clearing absent resolution creates no ghost profile;
26. clear preserves unrelated extension data.

### Shared storage/gate

27. Steam + XBOX mutations against one `profiles.json` preserve both collections;
28. no lost-update behavior under the existing shared mutation gate for normal concurrent mutation tests already supported by the product.

Do not create pathological scheduler tests merely to discover theoretical races.

---

## 40. Frontend tests

Add XBOX profile frontend tests proving:

1. catalog Favorite is merged from `XboxGames` without changing `XboxInstalledGameCatalog`;
2. capture maps Key, DisplayName, Exists, Enabled, persistence writable state;
3. capture provides TDP limits from existing device runtime policy;
4. capture reports FPS capability from the existing Intel runtime;
5. Favorite persists only;
6. Enable persists only;
7. CPU mutation persists only;
8. TDP mutation persists only;
9. Power mutation persists only;
10. FPS mutation persists only;
11. Resolution mutation persists only;
12. failed persistence returns a non-success outcome and safe snapshot;
13. InvalidTarget returns without a hardware apply;
14. **no XBOX mutation calls Steam/current-game reconcile methods**.

For item 14, use the least-complex evidence available:

- counting fake hardware/runtime seam if practical; or
- a focused source/contract test isolating the XBOX mutation region and asserting absence of current apply calls.

Do not add a new runtime abstraction solely to make this test injectable.

---

## 41. Transport tests

Update `FrontendNamedPipeTransportTests`.

Required:

1. protocol = 55;
2. XBOX capture round-trips a string canonical key;
3. Favorite mutation round-trips;
4. Enabled mutation round-trips;
5. CPU enabled/AC/DC round-trip;
6. TDP enabled/configuration round-trip;
7. Power enabled/AC/DC round-trip;
8. FPS enabled/AC/DC round-trip;
9. Resolution set/clear round-trip;
10. Unicode DisplayName survives where supplied;
11. malformed/missing payload is rejected;
12. v54 peer is rejected by v55;
13. existing Steam profile RPCs remain unchanged;
14. XBOX catalog scan still round-trips;
15. Active Session Diagnostic RPCs remain unchanged.

Update all tests that pin frontend protocol 54 → 55.

Overlay protocol stays unchanged.

---

## 42. XBOX page UI tests

Update `XboxCatalogPageUiTests` or split focused XBOX profile UI tests.

Required:

### User-visible privacy

1. catalog still renders DisplayName only plus Favorite control;
2. detail header renders game name only;
3. canonical Key is never bound to text;
4. StoreId/TitleId/PFN/package/config/executable fields remain absent.

### Catalog

5. Favorite sort works;
6. search is DisplayName-only;
7. Refresh behavior remains bounded;
8. saved-but-uninstalled profiles are not synthesized as installed cards.

### Detail

9. clicking a card opens detail;
10. detail captures by string Key;
11. Back returns to catalog;
12. stale response for another Key is ignored;
13. Profile toggle calls XBOX, not Steam, RPC;
14. all CPU/TDP/Power/FPS/Resolution handlers call XBOX-specific methods;
15. TDP/FPS debounce cancellation occurs on selection change/back/deactivate.

### No apply ownership

16. XboxPage never references Steam RunningAppID;
17. XboxPage never references XboxSessionDiagnostic;
18. XboxPage never talks directly to ProfileStore/hardware.

---

## 43. Main UI lifecycle

Keep PR #691 behavior:

~~~text
XBOX page activation
→ one bounded installed catalog scan
~~~

Selecting/editing a game does not start any background package watcher.

Leaving XBOX page:

~~~text
cancel current catalog request
cancel pending TDP/FPS detail debounce
ignore retired async results
~~~

Do not add:

- polling;
- PackageCatalog listener;
- active process watcher;
- timer-based profile recapture.

---

## 44. No Active Session Diagnostic coupling

Keep:

~~~text
XboxGameSessionDiagnostic
XboxSessionDiagnosticPage
FrontendXboxSessionDiagnostic*
~~~

unchanged.

PR6 must not ask the diagnostic:

~~~text
is this XBOX game running?
~~~

to decide whether to apply a mutation.

Diagnostic state is not production authority.

The diagnostic is retired only when the later production `XboxGameSessionRuntime` exists.

---

## 45. No live apply in AddonProcessHost

Do not wire XBOX profile mutation callbacks into:

~~~text
SteamSessionRuntime
CpuBoostRuntime actual AppID source
TdpRuntime current session
PowerModeRuntime current AppID
IntelFrameLimiterRuntime current AppID
GameDisplayResolutionRuntime current AppID
~~~

PR6 adds persistence owner wiring only.

No new process/session subscription is needed.

---

## 46. Failure policy

### Unsafe profiles.json

~~~text
Capture:
→ PersistenceWritable = false

Mutation:
→ PersistenceFailed
→ original file untouched
~~~

### Invalid canonical key

~~~text
→ InvalidTarget
→ no write
~~~

### Invalid TDP/FPS value

~~~text
→ InvalidTarget
→ no write
~~~

### Save exception

~~~text
→ PersistenceFailed
→ no hardware apply
~~~

### Catalog scan fails

Existing PR #691 behavior remains:

~~~text
XBOX catalog unavailable/error
→ saved XboxGames untouched
→ Steam unaffected
~~~

No destructive recovery.

No automatic profile deletion.

---

## 47. Logging

Add useful persistence-domain logs only where current mutation style warrants them.

Normal user edits do not need noisy Info logs per slider tick.

Failures may log:

~~~text
Profiles.Xbox
Key
Operation
FailureCategory
~~~

Do not log:

- package install path;
- config path;
- StoreId/TitleId duplicates solely for UI edits.

The key is sufficient stable diagnostic identity.

---

## 48. Explicit non-goals

Do not implement in PR6:

- `XboxGameSessionRuntime`;
- `ActiveProfileTarget`;
- Steam/XBOX target selection;
- CPU/TDP/Power/FPS/Resolution XBOX live apply;
- active XBOX startup/resume reconcile;
- per-game M1/M2;
- `XboxGameControllerOverrides`;
- Overlay XBOX profile;
- Overlay XBOX catalog;
- Game Bar;
- Win+G;
- ClawHUD dependency;
- PresentMon;
- package polling;
- saved-but-uninstalled UI;
- generic game-platform framework.

Do not change Full1902:

- PID1902 ownership;
- DirectInput;
- HidHide;
- VIIPER;
- Xbox360/SteamDeck presentation;
- WING/Center M authority.

---

## 49. Acceptance criteria

PR6 is complete only when all are true:

1. `ProfileDocument.XboxGames` exists as `Dictionary<string, XboxGameProfile>`.
2. schema version remains 1.
3. old v1 profile documents without `xboxGames` load safely.
4. `XboxGameProfile` is separate from Steam `GameProfile`.
5. existing performance/display nested models are reused.
6. no M1/M2/controller profile section is added.
7. no technical package identity sidecar is required for this phase.
8. `ProfileStore.HasValidStructure` validates XBOX structural null/incomplete cases.
9. malformed/unsafe profiles remain fail-safe and are never overwritten.
10. one existing `ProfileStore` owns Steam + XBOX data.
11. one existing `ProfileMutationGate` serializes Steam + XBOX persistence.
12. `XboxGameProfileMutations` exists with string-key operations.
13. mutation key validation accepts only canonical store/pfn/identity families.
14. Favorite can create a disabled XBOX entry.
15. enabling creates/completes persisted CPU/TDP/Power settings using current Device/fallback semantics.
16. disabling preserves profile values.
17. CPU/TDP/Power/FPS/Resolution edits persist correctly.
18. invalid TDP/FPS targets do not write.
19. uninstall/catalog absence does not delete saved XBOX profiles.
20. `FrontendXboxGameCatalogEntry` adds Favorite but no technical identity fields beyond internal Key.
21. `FrontendXboxGameProfileSnapshot` exists with string Key and shared setting DTOs.
22. XBOX-specific capture/mutation frontend methods exist.
23. frontend protocol is bumped 54 → 55.
24. Steam profile frontend RPCs remain unchanged.
25. XBOX catalog RPC remains production/request-driven.
26. XBOX page cards become selectable and expose Favorite.
27. XBOX page detail mirrors Steam settings UX for CPU/TDP/Power/FPS/Resolution.
28. normal UI never renders canonical Key.
29. normal UI never renders StoreId/TitleId/PFN/package/config details.
30. search remains DisplayName-only.
31. stale async profile responses are ignored by selected Key.
32. TDP/FPS debounces are canceled on navigation/selection retirement.
33. **XBOX mutations perform persistence only and never call live apply runtimes.**
34. Active Session Diagnostic remains unchanged.
35. Overlay remains unchanged.
36. Full1902 ownership/presentation remains unchanged.
37. persistence tests pass.
38. frontend tests pass.
39. transport tests pass.
40. UI tests pass.
41. full solution build/tests pass.

---

## 50. Review policy

Review for realistic production defects:

- corrupting/overwriting existing Steam `Games`;
- unsafe malformed-profile overwrite;
- Xbox key/display-name identity confusion;
- Xbox/Steam mutations losing each other's writes;
- incorrect initial profile defaults;
- Favorite/profile entry loss;
- invalid TDP/FPS persistence;
- technical identity leaking into normal UI;
- accidentally invoking live Steam/hardware apply from XBOX mutation;
- transport method wired to wrong Steam RPC;
- stale selected-game response rendered onto another game;
- regression to PR #691 bounded catalog scan.

Do not block for theoretical races that require arbitrary instruction-level interleavings.

The existing shared `ProfileMutationGate`, page-local cancellation, and selected-Key stale-response check are sufficient for the supported one-user/one-session lifecycle.

Do not add epochs, barriers, a generic profile manager, or a second persistence authority.

---

## 51. Follow-up after PR6

After PR6 the product state is:

~~~text
Steam
→ installed catalog
→ persisted editable Steam profiles
→ existing live Steam apply

XBOX
→ installed catalog
→ persisted editable XBOX profiles
→ NO live apply yet
~~~

Next phase:

~~~text
PR7
→ promote PoC B into production XboxGameSessionRuntime
→ add derived ActiveProfileTarget
→ teach existing apply runtimes to resolve Steam or XBOX profile
→ startup/resume/exit reconciliation
→ retire Active Session Diagnostic
~~~

Then:

~~~text
PR8
→ per-game XBOX M1/M2
~~~

Then:

~~~text
Overlay XBOX active-profile projection
~~~

---

## 52. Final design principle

PR6 creates a second **profile identity domain**, not a second hardware-control system.

~~~text
Steam identity
→ ProfileDocument.Games

XBOX identity
→ ProfileDocument.XboxGames

both
→ same ProfileStore
→ same ProfileMutationGate

PR6 stops here.
~~~

No XBOX mutation in this PR writes hardware or Windows state.

That boundary is the primary acceptance rule.
