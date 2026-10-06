
# Work Order — PR10: Non-Steam Per-Game M1/M2 Controller Override Foundation — XBOX First Consumer

> Date: 2026-10-07  
> Repository: onehoon/SteamAddonforClaw  
> Reviewed main: main@d9c02b7f6e19464ffb97cc67179ed33abca99b8a  
> Previous phase: PR #698 / squash merge d9c02b7f6e19464ffb97cc67179ed33abca99b8a extracted the reusable non-Steam Windows observation/process-lifetime foundation  
> Architecture authorities:
> - docs/Full 1902 Implementation/README.md and its active precedence chain
> - docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
> - docs/XBOX_GAME_PROFILE_ARCHITECTURE/work-order/PR8_XBOX_SHARED_ACTIVE_PROFILE_TARGET_AND_LIVE_APPLY_WORK_ORDER_2026-10-06.md
> - docs/work-order/PR9_NON_STEAM_GAME_DETECTION_FOUNDATION_WINDOWS_OBSERVATION_PROCESS_LIFETIME_WORK_ORDER_2026-10-06.md
> Current frontend protocol: FrontendTransportProtocol.CurrentVersion = 58
> Scope: add one reusable non-Steam per-game M1/M2 override shape, persist/edit it first for XBOX profiles, and feed the existing CanonicalXbox360InputPublisher through one cached effective-mapping provider without creating another publisher, controller owner, or Steam profile path
> Out of scope: Custom EXE detection, Epic/GOG, XBOX Overlay profile projection, Steam per-game M1/M2, Steam Input changes, VIIPER ownership changes, physical PID1902 changes, firmware M1/M2 writes

---

## 1. Goal

Add per-game M1/M2 mapping for non-Steam games while preserving the existing Full1902 controller ownership and publisher architecture.

The product rule is intentionally asymmetric:

~~~text
Steam
→ Steam RunningAppID / Steam profile
→ SteamDeck presentation
→ Steam Input owns game-specific controller mapping
→ NO Addon per-game M1/M2 profile mapping

Non-Steam
→ XBOX now
→ Custom EXE / Epic / GOG later
→ Xbox360 presentation path
→ optional Addon per-game M1/M2 override
→ existing CanonicalXbox360InputPublisher
~~~

Required target shape:

~~~text
global settings.json
BackButtonMappingSettings
        │
        │ fallback
        ▼
effective non-Steam M1/M2 provider
        ▲
        │ optional override
enabled active non-Steam profile
Controller.BackButtonMapping
        │
        ▼
SAME CanonicalXbox360InputPublisher
        │
        ▼
Xbox360DeviceStateMapper
        │
        ▼
existing VIIPER Xbox360 output
~~~

There must still be exactly one Xbox360 publisher and exactly one Full1902 presentation owner.

---

## 2. Locked product semantics

### 2.1 Steam is excluded

Do not add M1/M2 controller override fields to the Steam GameProfile.

Do not add Steam profile M1/M2 frontend RPCs.

Do not inspect Steam AppID to select an Addon M1/M2 mapping.

Steam remains:

~~~text
Steam/BPM active
→ SteamDeck presentation
→ Steam Input controller policy
~~~

If ActiveProfileTarget.Kind == Steam, the non-Steam M1/M2 override is considered absent.

The existing Steam/BPM presentation reconciliation remains authoritative and unchanged.

### 2.2 Non-Steam per-game mapping is an Xbox360 software-output policy

This feature does not:

- program MSI firmware M1/M2 slots;
- alter PID1902 DirectInput;
- change HidHide;
- change VIIPER ownership;
- attach/detach a second Xbox360 device;
- change Xbox360 ↔ SteamDeck presentation selection;
- bypass rear-button suppression/release safety.

It only changes the BackButtonMappingSettings value read by the existing Xbox360 publisher.

### 2.3 Global mapping remains the fallback

Required semantics:

~~~text
no active non-Steam game
→ global BackButtonMappingSettings

active non-Steam profile disabled
→ global BackButtonMappingSettings

active non-Steam profile enabled
+ no explicit controller mapping override
→ global BackButtonMappingSettings

active non-Steam profile enabled
+ valid explicit controller mapping override
→ per-game BackButtonMappingSettings
~~~

A disabled profile does not erase its persisted override. Re-enabling that profile may make the existing override effective again.

---

## 3. Current code facts

Reviewed main@d9c02b7f6e19464ffb97cc67179ed33abca99b8a.

### 3.1 One global mapping already exists

Current contract:

~~~csharp
public sealed record BackButtonMappingSettings(
    Xbox360BackButtonTarget M1,
    Xbox360BackButtonTarget M2);
~~~

Current global persistence:

~~~text
AppSettings.BackButtonMapping
→ settings.json
→ StartupSettingsCoordinator.BackButtonMapping
~~~

Validation already exists:

~~~text
BackButtonMappingValidation.Validate(...)
~~~

Reuse it.

Do not create another M1/M2 enum or another target list.

### 3.2 The existing publisher already has the correct dynamic seam

CanonicalXbox360InputPublisher currently owns:

~~~csharp
private readonly Func<BackButtonMappingSettings> _backButtonMappingProvider;
~~~

and reads it in PublishCurrentStateOnce() for every report before calling:

~~~text
Xbox360DeviceStateMapper.Map(...)
~~~

Therefore do not restart the publisher when the active game or per-game mapping changes.

The provider can expose the new effective value on the next normal report.

### 3.3 The publisher runs at approximately 250 Hz

The production Xbox360 publisher reads the provider roughly every 4 ms.

Therefore the provider must be an in-memory read.

Forbidden:

~~~text
publisher tick
→ ProfileStore.Load()
→ JSON parse / disk I/O
→ resolve active game
~~~

Profile storage may be read only at real reconciliation boundaries, then projected into a small cached derived override.

### 3.4 Existing XBOX profile has no Controller field yet

Current:

~~~csharp
public sealed record XboxGameProfile
{
    public bool Enabled { get; init; }
    public bool Favorite { get; init; }
    public string? DisplayName { get; init; }
    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();
}
~~~

This PR adds the controller override.

### 3.5 Existing XBOX profile live mutation authority already exists

XboxGameProfileMutations persists under the existing ProfileMutationGate.

InProcessAddonFrontendControl already:

- captures XBOX profiles;
- persists XBOX mutations;
- checks the current effective target;
- live-reconciles active profile changes.

Extend this authority.

Do not add a second XBOX profile mutation service.

---

## 4. Add one reusable non-Steam controller override shape

Add a small profile model, recommended:

~~~text
src/SteamInputAddonforClaw/Profiles/NonSteamGameControllerOverrides.cs
~~~

Contract:

~~~csharp
public sealed record NonSteamGameControllerOverrides
{
    public BackButtonMappingSettings? BackButtonMapping { get; init; }
}
~~~

Then extend XBOX:

~~~csharp
public sealed record XboxGameProfile
{
    ...
    public NonSteamGameControllerOverrides Controller { get; init; } = new();
}
~~~

The type is intentionally not named XboxGameControllerOverrides because the semantics are not XBOX-specific.

Future Custom EXE / Epic / GOG profile types should reuse this nested controller settings shape.

Do not create a generalized persisted non-Steam game identity model in this PR.

Null meaning:

~~~text
Controller.BackButtonMapping == null
→ Use global mapping
~~~

Do not persist a copied global value merely because a profile exists or is enabled.

---

## 5. Profile schema policy

This is an additive optional field under an existing XBOX profile.

Expected:

~~~text
ProfileDocument.SchemaVersion
→ unchanged
~~~

Do not bump the profile schema merely for this additive field.

Older profiles deserialize as:

~~~text
Controller = new NonSteamGameControllerOverrides()
BackButtonMapping = null
→ global mapping
~~~

Preserve unknown extension data exactly as current ProfileStore behavior requires.

---

## 6. Extend XboxGameProfileMutations

Add one whole-record M1/M2 mutation.

Recommended:

~~~csharp
internal MutationOutcome SetBackButtonMapping(
    string key,
    BackButtonMappingSettings? mapping)
~~~

Semantics:

~~~text
mapping == null
→ clear explicit override
→ Use global mapping

mapping != null
→ BackButtonMappingValidation.Validate(mapping)
→ invalid: InvalidTarget, persist nothing
→ valid: persist exact whole record
~~~

Do not add separate SetM1 / SetM2 persistence mutations.

Persistence should stay atomic because M1/M2 form one mapping record.

Profile creation semantics:

~~~text
no existing profile + null mapping
→ accepted no-op

no existing profile + explicit valid mapping
→ create profile entry
→ Enabled remains false
→ persist Controller.BackButtonMapping
~~~

Do not implicitly enable the profile merely because M1/M2 was edited.

Disabling a profile must preserve its stored controller override.

---

## 7. Add a host-owned derived effective override cache

Do not add a new controller manager.

Use one small host-owned derived field, conceptually:

~~~csharp
private BackButtonMappingSettings? _activeNonSteamBackButtonMappingOverride;
~~~

This is not persisted authority.

It is only the currently-resolved per-game override for the live Xbox360 publisher.

The provider given to MsiClawAddonPresentation should become conceptually:

~~~csharp
backButtonMappingProvider: () =>
    Volatile.Read(ref _activeNonSteamBackButtonMappingOverride)
    ?? startupSettings.BackButtonMapping
~~~

The global value remains read fresh from StartupSettingsCoordinator.

Therefore:

- changing global M1/M2 while no per-game override is active takes effect immediately;
- changing global M1/M2 while an override is active persists the new fallback but does not replace the active override;
- once the override is no longer effective, the latest global value is used automatically.

Use a simple atomic/volatile reference read/write.

Do not add a lock on the 250 Hz publisher path.

---

## 8. Resolve the active non-Steam override only at reconciliation boundaries

Add one host method, for example:

~~~csharp
private bool ReconcileEffectiveBackButtonMapping(string trigger)
~~~

Required algorithm:

~~~text
capture current ActiveProfileTarget

if target == Steam
    next override = null

else if target == Xbox
    load ProfileStore under existing ProfileMutationGate
    exact XboxGames[key]
    require profile.Enabled
    read profile.Controller.BackButtonMapping
    validate it
    valid explicit mapping → next override
    otherwise → null

else
    next override = null

Volatile.Write(cachedOverride, next override)
~~~

For this PR, XBOX is the only implemented non-Steam profile collection.

Future Custom/Epic/GOG work extends this resolution boundary, not the publisher.

Fail-close behavior:

~~~text
profile document cannot be read safely
or persisted mapping is invalid
→ cached override = null
→ global BackButtonMappingSettings
~~~

Do not leave a stale previous-game override active after a failed target change.

---

## 9. Required host reconciliation triggers

### 9.1 XBOX active game change

Current:

~~~text
OnActiveXboxGameChanged
→ ReconcileEffectiveGameProfile(...)
~~~

Extend:

~~~text
OnActiveXboxGameChanged
→ ReconcileEffectiveGameProfile(...)
→ ReconcileEffectiveBackButtonMapping(...)
~~~

Do not request controller-presentation switching merely because XBOX identity changed.

### 9.2 Steam RunningAppID change

Current:

~~~text
OnActualRunningAppIdChanged
→ RequestControllerPresentationReconcile(...)
→ ReconcileEffectiveGameProfile(...)
~~~

Also refresh the non-Steam mapping cache.

Required:

~~~text
Steam starts while XBOX remains alive
→ cached non-Steam override cleared

Steam ends while XBOX remains alive
→ XBOX override resolved again
~~~

Do not add a Steam M1/M2 profile path.

### 9.3 Runtime startup

XboxGameSessionRuntime already starts/reconciles before deferred Full1902 presentation attach.

Populate the effective mapping cache after runtime startup settings are available and before the first Xbox360 publisher can begin normal game-facing publication.

A Runtime restart while an XBOX game is already running must not begin with a stale mapping from the previous process lifetime.

### 9.4 Resume

Do not create a new power participant.

If the active XBOX game/profile remains valid, the cached override may remain valid across Suspend/Resume.

If the game exited or identity changed during sleep, existing XBOX resume reconciliation must publish the active-game change and the normal callback must refresh/clear the override.

No polling.

---

## 10. Live XBOX controller mutation

Extend InProcessAddonFrontendControl with one narrow callback to the host, for example:

~~~csharp
Func<string, bool>? reconcileXboxBackButtonMapping = null
~~~

Equivalent small shape is acceptable.

Do not pass the presentation owner or publisher into frontend control.

Add:

~~~csharp
Task<FrontendXboxGameProfileMutationResult>
    SetXboxGameProfileBackButtonMappingAsync(
        string key,
        BackButtonMappingSettings? mapping,
        CancellationToken cancellationToken = default);
~~~

Flow:

~~~text
validate/persist through XboxGameProfileMutations
→ capture fresh XBOX profile snapshot
→ if mutated XBOX key is the current effective target:
       refresh host effective M1/M2 cache
→ otherwise:
       persistence only
~~~

SetXboxGameProfileEnabledAsync must also refresh controller mapping when that XBOX key is currently effective.

Required:

~~~text
active XBOX Enabled true → false
→ persisted override retained
→ effective mapping immediately falls back global

active XBOX Enabled false → true
+ persisted override exists
→ override becomes effective immediately
~~~

Persistence remains first.

If persistence succeeds but active mapping reconciliation unexpectedly fails:

~~~text
keep persisted value
→ return ApplyFailed where the existing mutation result supports it
→ effective path fails closed to global mapping
~~~

Do not roll back persistence.

---

## 11. Frontend contract

Add a reusable frontend mapping configuration rather than embedding two unrelated enum fields.

Recommended:

~~~csharp
public sealed record FrontendGameBackButtonMappingConfiguration(
    bool UseGlobalMapping,
    BackButtonMappingSettings Mapping);
~~~

Extend FrontendXboxGameProfileSnapshot with:

~~~text
FrontendGameBackButtonMappingConfiguration? BackButtonMapping = null
~~~

Projection:

~~~text
persisted override == null
→ UseGlobalMapping = true
→ Mapping = current global BackButtonMappingSettings

persisted override != null
→ UseGlobalMapping = false
→ Mapping = persisted override
~~~

This lets Main App show the actual global fallback while preserving the semantic distinction between fallback and explicit override.

Do not make FrontendSettingsSnapshot.BackButtonMapping context-sensitive. It remains the global setting.

---

## 12. Frontend transport version

This PR adds a new XBOX profile snapshot field and a new typed XBOX profile mutation RPC/request.

Bump:

~~~text
FrontendTransportProtocol.CurrentVersion
58 → 59
~~~

Update:

- FrontendRpcMethod;
- request DTO;
- IAddonFrontendControl;
- in-process implementation;
- named-pipe client;
- named-pipe server;
- protocol/contract tests.

Do not bump OverlayTransportProtocol.

PR10 does not add XBOX profile projection to Overlay.

---

## 13. Main App XBOX UI

Add a Controller section to the selected XBOX profile editor.

Required controls:

~~~text
Controller

Use global M1 / M2 mapping    [On/Off]

M1                            [dropdown]
M2                            [dropdown]
~~~

Default for old/new profiles:

~~~text
Use global mapping = On
~~~

When On:

- M1/M2 selectors show current global fallback values;
- M1/M2 selectors are disabled;
- persisted override is null.

When switched Off:

- initialize explicit override from the currently displayed mapping pair;
- persist the complete BackButtonMappingSettings;
- enable both selectors.

When switched On again:

- persist null;
- an active game immediately returns to the latest global mapping.

Reuse Xbox360BackButtonTarget and BackButtonMappingValidation.

Do not create XBOX-only target enums.

Avoid maintaining a second hard-coded target set.

If ControllerPage's display-name formatter/list construction cannot be reused cleanly, extract one tiny UI-only formatter/options helper and use it from both ControllerPage and XboxPage.

Do not create a generic controller-settings ViewModel framework for two dropdowns.

Preserve the XBOX page's existing selected-key response validation, event suppression during Render, fresh snapshot rendering, and mutation failure behavior.

---

## 14. Overlay contract

Current Overlay Controller M1/M2 controls call:

~~~text
SetBackButtonMappingAsync
→ global settings.json BackButtonMapping
~~~

Keep that exact meaning in PR10.

The current Overlay Controller tab remains a global fallback editor.

Do not silently reinterpret it as current-game M1/M2.

When XBOX/non-Steam active-game profile projection is implemented later, it must use the same per-game profile persistence/mutation authority and the same effective mapping cache/provider.

It must not add an Overlay-specific M1/M2 apply path.

No XBOX Overlay UI is required in PR10.

---

## 15. CanonicalXbox360InputPublisher remains the one publisher

The publisher already reads a mapping provider on every report.

PR10 must not modify its lifecycle.

Do not add:

- mapping-change restart;
- mapping-change Stop/Start;
- mapping-change neutral/detach/attach;
- second publisher;
- second mapper;
- per-platform publisher.

Expected steady state:

~~~text
same publisher thread
→ next report
→ provider returns new effective mapping
→ mapper outputs new mapping
~~~

The existing rear-button suppression provider remains part of the same mapper call.

Do not bypass or duplicate it.

---

## 16. Mapping transition behavior

A mapping change while a physical rear button is already held does not require a new epoch/state machine.

The mapper publishes a complete Xbox360 state every report, so the old mapped target is naturally released when the mapping changes.

Do not add a new hold-transition barrier solely for an artificial exact-timing interleaving.

Existing presentation-switch release gates remain unchanged.

---

## 17. Architecture document update

Update:

~~~text
docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
~~~

The current Phase X4 wording is XBOX-specific.

Revise only the relevant controller/M1/M2 sections so the ownership becomes:

~~~text
Non-Steam per-game M1/M2 foundation
→ first production consumer: XBOX
→ future consumers: Custom EXE / Epic / GOG
→ Steam excluded because Steam Input owns per-game mapping
→ one existing CanonicalXbox360InputPublisher
→ global BackButtonMappingSettings remains fallback
~~~

Do not rewrite unrelated XBOX architecture sections.

---

## 18. Explicit non-goals

Do not implement:

- Custom EXE registration/detection;
- Epic;
- GOG;
- generic persisted non-Steam game identity;
- game-platform plugin interfaces;
- Steam per-game M1/M2;
- Steam Input configuration;
- SteamDeck rear-button mapping;
- XBOX Overlay profile page;
- global Overlay controller semantic changes;
- firmware M1/M2 programming;
- VIIPER attach/detach changes;
- controller-presentation selection changes;
- polling;
- profile schema bump;
- Overlay protocol bump.

---

## 19. Suggested production file changes

Expected:

~~~text
src/SteamInputAddonforClaw/Profiles/NonSteamGameControllerOverrides.cs
src/SteamInputAddonforClaw/Profiles/XboxGameProfile.cs
src/SteamInputAddonforClaw/Profiles/XboxGameProfileMutations.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs

docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
~~~

A small shared UI formatter/options helper is allowed only if needed to avoid duplicating Xbox360BackButtonTarget display logic.

CanonicalXbox360InputPublisher should ideally require no behavioral modification.

MsiClawAddonPresentation should continue to receive the existing mapping provider delegate; no lifecycle redesign.

---

## 20. Persistence tests

Extend XboxGameProfileMutationsTests.

Required:

1. old/missing Controller field loads as Use Global;
2. explicit valid M1/M2 override persists exactly;
3. invalid M1 enum rejected and persistence unchanged;
4. invalid M2 enum rejected and persistence unchanged;
5. clearing override persists null;
6. null on missing profile is a no-op;
7. explicit override on missing profile creates a disabled profile;
8. disabling profile preserves persisted controller override;
9. re-enabling profile preserves override;
10. unrelated CPU/TDP/Power/FPS/Resolution mutation preserves Controller;
11. extension data preservation remains intact;
12. no profile schema-version bump.

---

## 21. Effective mapping tests

Required:

~~~text
No active target
→ global

Steam target
→ global / no non-Steam override

XBOX active + missing profile
→ global

XBOX active + profile disabled + override
→ global

XBOX active + profile enabled + null override
→ global

XBOX active + profile enabled + valid override
→ override

XBOX active + invalid persisted override
→ global fail-close
~~~

Transitions:

~~~text
None → XBOX A
global → A override

XBOX A → None
A override → global

XBOX A → Steam
A override → global/non-Steam override cleared

Steam → live XBOX A
global → A override
~~~

Do not add pathological instruction-level race tests.

---

## 22. Publisher/provider tests

Required:

1. provider is read on each publish, not captured once at publisher construction;
2. changing cached override changes the next mapped report without publisher restart;
3. clearing cached override causes next report to use current global mapping;
4. changing global mapping while no override is active changes next report;
5. changing global mapping while override is active does not replace override;
6. no Stop/Start occurs for mapping-only changes;
7. rear-button suppression callback remains applied with selected mapping.

Source audit: publisher/provider hot path must contain no ProfileStore, file I/O, JSON, XboxGameSessionRuntime, package lookup, or blocking lock.

---

## 23. Frontend/XBOX live mutation tests

Extend XboxGameProfileFrontendTests.

Required:

- capture projects Use Global + current global mapping;
- capture projects explicit override;
- inactive XBOX M1/M2 edit persists only;
- active XBOX explicit override mutation refreshes effective cache;
- active XBOX clear-to-global refreshes effective cache;
- active XBOX Profile Enabled false refreshes to global;
- active XBOX Profile Enabled true restores persisted override;
- XBOX edit while Steam target is effective persists but does not activate non-Steam override;
- reconciliation failure does not roll persistence back;
- Favorite does not refresh controller mapping;
- CPU/TDP/Power/FPS/Resolution edits do not perform redundant controller mapping reconciliation.

---

## 24. Frontend transport tests

Because protocol changes 58 → 59, update tests for:

- handshake version;
- XBOX profile snapshot serialization;
- FrontendGameBackButtonMappingConfiguration;
- Use Global semantics;
- explicit override semantics;
- new mutation request serialization;
- named-pipe client/server dispatch;
- invalid payload behavior according to existing wire rules.

Do not change Overlay protocol tests except compile fixtures that must implement the new frontend interface method.

---

## 25. Main App UI tests

Required:

- XBOX detail editor contains Controller section;
- Use Global control exists;
- M1 selector exists;
- M2 selector exists;
- selectors disabled while Use Global is true;
- turning override on seeds from displayed current mapping;
- snapshot Render does not trigger mutation;
- stale response for a previously selected XBOX key is ignored;
- mutation failure restores/renders returned authoritative snapshot;
- target options come from existing Xbox360BackButtonTarget contract.

---

## 26. Steam regression locks

Explicitly prove PR10 does not change Steam behavior.

Required source/test checks:

~~~text
GameProfile
→ no Controller / BackButtonMapping field added

Steam frontend game-profile RPCs
→ no M1/M2 methods added

SteamSessionRuntime
→ unchanged

SteamBigPictureWindowProbe
→ unchanged

SteamDeck publisher
→ unchanged

Steam/BPM active → SteamDeck
→ unchanged
~~~

Do not require an Xbox360 per-game mapping result for Steam.

---

## 27. Real lifecycle validation

Use a distinctive global mapping:

~~~text
M1=A
M2=B
~~~

and a distinctive XBOX profile override:

~~~text
M1=X
M2=Y
~~~

Validate:

### XBOX launch

~~~text
ActiveXboxGame
→ effective override X/Y
→ SAME Xbox360 publisher
~~~

### Alt+Tab

Matched game process remains alive:

~~~text
X/Y retained
~~~

### Game exit

~~~text
ActiveXboxGame clears
→ cached override null
→ latest global A/B
~~~

### Live edit

While XBOX game runs:

~~~text
X/Y → LB/RB
→ save
→ cache refresh
→ next publisher reports use LB/RB
→ no publisher restart
→ no detach/attach
~~~

### Use global

~~~text
clear override
→ latest global mapping immediately effective
~~~

### Profile disable

~~~text
disable active XBOX profile
→ persisted override retained
→ global mapping effective
~~~

### Runtime restart

With XBOX game already running:

~~~text
bounded startup reconcile
→ active key recovered
→ effective M1/M2 cache populated
→ initial Xbox360 publication uses correct mapping
~~~

### Sleep/Hibernate/Resume

Verify:

- active live process retains/recovers correct override;
- game that exited during sleep converges to global;
- no publisher/presentation ownership regression.

### Steam regression

~~~text
Steam/BPM title
→ SteamDeck presentation
→ Steam Input remains controller mapping authority
→ non-Steam per-game M1/M2 has no effect
~~~

---

## 28. Logging

Add low-frequency logs only at effective-mapping reconciliation boundaries.

Recommended fields:

~~~text
Category=Controller.BackButtons
Trigger=XboxActiveGameChanged / RunningAppIdChanged / Startup / XboxProfileMutation
ProfileTarget=Xbox:...
MappingSource=Global / NonSteamProfile
M1=...
M2=...
~~~

Do not log every publisher lookup.

Do not log physical button state continuously.

---

## 29. Source audits

### One output owner

Confirm exactly one production CanonicalXbox360InputPublisher and one MsiClawAddonPresentation owner.

No:

~~~text
XboxGameBackButtonPublisher
NonSteamXbox360Publisher
per-game VIIPER owner
~~~

### No Steam profile contamination

Expected:

~~~text
0 per-game BackButtonMapping references in Steam GameProfile
0 Steam M1/M2 profile RPCs
~~~

### Hot-path safety

The provider called by CanonicalXbox360InputPublisher performs only in-memory reads.

No ProfileStore/disk/JSON/blocking lock.

### Profile ownership

XBOX persistence remains:

~~~text
ProfileStore
+ ProfileMutationGate
+ XboxGameProfileMutations
~~~

No second persistence file.

### Global ownership

Global fallback remains:

~~~text
settings.json
→ StartupSettingsCoordinator.BackButtonMapping
~~~

Do not copy global mapping into every XBOX profile.

---

## 30. Required validation commands

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

Run the source audits in section 29.

---

## 31. Overengineering constraints

Do not create:

- controller-profile manager hierarchy;
- game-platform plugin interfaces;
- M1/M2 runtime per platform;
- M1/M2 publisher per platform;
- controller mapping epoch;
- mapping transition state machine;
- new background worker;
- polling;
- second persistence authority.

The required derived state is only:

~~~text
BackButtonMappingSettings? currentNonSteamOverride
~~~

with:

~~~text
effective = currentNonSteamOverride ?? global
~~~

That is enough.

---

## 32. Completion condition

PR10 is complete only when:

~~~text
XBOX game profile
Controller.BackButtonMapping
        │
        │ optional explicit override
        ▼
host-owned cached non-Steam override
        │
        ├─ null ───────────────→ global StartupSettingsCoordinator.BackButtonMapping
        │
        └─ value
                ↓
      one effective mapping provider
                ↓
SAME CanonicalXbox360InputPublisher
                ↓
SAME Xbox360DeviceStateMapper
                ↓
SAME VIIPER Xbox360 device
~~~

And all of the following remain true:

~~~text
Steam per-game M1/M2 = not implemented
Steam Input = unchanged
SteamDeck publisher = unchanged
Full1902 presentation authority = unchanged
PID1902 / HidHide / VIIPER ownership = unchanged
global M1/M2 remains fallback
Overlay Controller M1/M2 remains global fallback editor
no disk I/O in the 250 Hz provider
Frontend protocol = 59
Overlay protocol = unchanged
~~~

After this PR, the next Custom EXE profile implementation should reuse:

- NonSteamGameControllerOverrides;
- the same effective override cache/provider boundary;
- the same CanonicalXbox360InputPublisher;

and add only its own identity/persistence/frontend selection above that shared controller-output boundary.
