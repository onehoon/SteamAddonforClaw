# Work Order — XBOX PR8: Platform-Neutral Active Profile Resolution and Live XBOX Apply

> **Date:** 2026-10-06  
> **Repository:** onehoon/SteamAddonforClaw  
> **Reviewed main:** main@d16ebde10bb1cdd4fa8cb38cd6ceb82b2ccacb8a  
> **Architecture authority:** docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md  
> **Full1902 authority:** docs/Full 1902 Implementation/README.md and its active precedence chain  
> **Previous XBOX phase:** PR #695 / merge 21e07e7693779751e07a4f17361d4251461d9e93  
> **Current frontend protocol:** FrontendTransportProtocol.CurrentVersion = 58  
> **Scope:** keep Steam detection and persistence semantics unchanged, resolve the currently effective game profile outside the hardware runtimes, and feed the already-existing CPU Boost / TDP / Windows Power Mode / Intel FPS / Display Resolution runtime owners through one platform-neutral apply path for both Steam and XBOX  
> **Out of scope:** generic game-detection framework, Epic/GOG/custom EXE detection, Steam detector rewrite, XBOX-specific performance runtimes, per-game M1/M2, Overlay XBOX projection, Xbox app front-button action, controller-presentation changes, new frontend RPCs

---

## 1. Goal

Complete the XBOX game-profile path without creating a second performance-control implementation.

The locked architecture rule for PR8 is:

> **Platform-specific logic ends at active-profile resolution. CPU/TDP/Power/FPS/Resolution runtimes must not know whether the effective profile came from Steam or XBOX.**

Required structure:

~~~text
Steam detector / Steam profile identity
    SteamSessionRuntime.ActualRunningAppId
    ProfileDocument.Games
                │
                ├──────────────┐
                │              │
XBOX detector / XBOX profile identity
    XboxGameSessionRuntime.ActiveGame
    ProfileDocument.XboxGames
                │              │
                └──────┬───────┘
                       ↓
          Active profile resolution
        platform-specific work ends here
                       ↓
            ResolvedActiveProfile
                       ↓
       SAME existing runtime instances
          ├─ CpuBoostRuntime
          ├─ TdpRuntime
          ├─ PowerModeRuntime
          ├─ IntelFrameLimiterRuntime
          └─ GameDisplayResolutionRuntime
~~~

Forbidden structure:

~~~text
Steam → Steam TDP path
XBOX  → XBOX TDP path

Steam → existing runtime
XBOX  → new XBOX wrapper/runtime
~~~

There is exactly one runtime owner per machine-setting feature.

---

## 2. Non-negotiable authority rules

### 2.1 Steam logic remains authoritative and is not replaced

Do not change how Steam active-game identity is obtained.

Steam remains:

~~~text
SteamSessionRuntime.ActualRunningAppId
→ numeric Steam AppID
→ ProfileDocument.Games
~~~

Do not:

- move Steam into WinEvent detection;
- infer Steam games from PID/EXE;
- add a generic Steam detector abstraction;
- replace numeric Steam AppID persistence;
- change Steam/BPM controller-presentation behavior;
- change Steam catalog/frontend identity.

PR8 only consumes the existing Steam fact when selecting the effective profile.

### 2.2 XBOX identity remains owned by XboxGameSessionRuntime

XBOX remains:

~~~text
XboxGameSessionRuntime
→ exact package/config/executable proof
→ ActiveXboxGame(Key, DisplayName)
→ ProfileDocument.XboxGames
~~~

Do not redesign the PR7 detector.

### 2.3 Existing feature runtimes are mandatory shared owners

The following existing runtime instances are the only owners:

~~~text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
GameDisplayResolutionRuntime
~~~

They must not contain platform branching.

Forbidden inside these runtimes:

~~~text
if Steam ...
if Xbox ...
switch ActiveProfileTargetKind ...
document.Games versus document.XboxGames ...
read XboxGameSessionRuntime ...
read SteamSessionRuntime ...
~~~

They must receive a platform-neutral resolved profile.

### 2.4 No platform-specific runtime variants

Do not create:

~~~text
XboxCpuBoostRuntime
XboxTdpRuntime
XboxPowerModeRuntime
XboxIntelFpsRuntime
XboxDisplayResolutionRuntime
XboxPerformanceCoordinator
~~~

Do not add thin XBOX wrappers that merely delegate to the existing runtimes.

---

## 3. Current code facts

Reviewed main@d16ebde10bb1cdd4fa8cb38cd6ceb82b2ccacb8a.

### 3.1 Existing runtimes currently perform Steam lookup themselves

Current examples:

~~~text
CpuBoostRuntime
    SetActualAppIdSource(Func<uint>)
    Reconcile(uint actualAppId)

TdpRuntime
    Func<uint> _actualAppIdSource
    ResolveEffectiveTdp(ProfileDocument, uint actualAppId)

PowerModeRuntime
    Func<uint> _actualAppIdSource
    document.Games lookup

IntelFrameLimiterRuntime
    Func<uint> _app
    document.Games lookup

GameDisplayResolutionRuntime
    Reconcile(uint appId)
    document.Games lookup
~~~

This Steam-specific lookup is the seam that must move outward.

Do not duplicate the rest of these runtimes for XBOX.

### 3.2 Steam and XBOX already share the same override shapes

Both GameProfile and XboxGameProfile contain:

~~~text
Enabled
Performance : GamePerformanceOverrides
Display     : GameDisplayOverrides
~~~

That shared shape is the correct runtime boundary.

---

## 4. Add one narrow active identity selector

Add one small internal value in the Profiles domain.

Example:

~~~text
Profiles/ActiveProfileTarget.cs
~~~

Conceptual model:

~~~csharp
internal enum ActiveProfileTargetKind
{
    None,
    Steam,
    Xbox
}

internal readonly record struct ActiveProfileTarget
{
    internal ActiveProfileTargetKind Kind { get; }
    internal uint SteamAppId { get; }
    internal string? XboxGameKey { get; }

    internal static ActiveProfileTarget None { get; }
    internal static ActiveProfileTarget ForSteam(uint appId);
    internal static ActiveProfileTarget ForXbox(string key);

    internal string LogLabel { get; }
}
~~~

This type is allowed only in the selection/resolution layer.

Do not pass ActiveProfileTarget into the five feature runtimes.

---

## 5. Effective target selection

AddonProcessHost derives the current target.

Required priority:

~~~text
Steam ActualRunningAppId != 0
→ Steam target

else XBOX ActiveGame != null
→ XBOX target

else
→ None
~~~

Conceptually:

~~~csharp
private ActiveProfileTarget CaptureActiveProfileTarget()
{
    var appId = _runtimeHost?.ActualRunningAppId ?? 0;
    if (appId != 0)
        return ActiveProfileTarget.ForSteam(appId);

    var xbox = _xboxGameSessionRuntime?.ActiveGame;
    return xbox is null
        ? ActiveProfileTarget.None
        : ActiveProfileTarget.ForXbox(xbox.Key);
}
~~~

This is selection only. It does not apply settings.

---

## 6. Resolve platform identity into one common profile view

Add one small resolver outside the feature runtimes.

Recommended:

~~~text
Profiles/ActiveProfileResolver.cs
~~~

Recommended common value:

~~~csharp
internal readonly record struct ResolvedActiveProfile(
    string TargetLabel,
    GamePerformanceOverrides Performance,
    GameDisplayOverrides Display);
~~~

Equivalent naming is acceptable.

Required resolver behavior:

~~~text
None
→ null

Steam
→ exact AppID lookup in ProfileDocument.Games
→ require profile.Enabled
→ project to ResolvedActiveProfile

XBOX
→ exact canonical-key lookup in ProfileDocument.XboxGames
→ require profile.Enabled
→ project to ResolvedActiveProfile
~~~

After resolution, platform identity is finished.

The five feature runtimes may consume:

~~~text
Performance.CpuBoost
Performance.Tdp
Performance.PowerMode
Performance.FpsLimit
Display.Resolution
TargetLabel for diagnostics only
~~~

They must not consume:

~~~text
SteamAppId
XboxGameKey
Steam/Xbox enum
Games/XboxGames dictionaries
~~~

ResolvedActiveProfile is derived in-memory state only. Do not persist it.

---

## 7. One common profile-source seam

Use one platform-neutral source for all existing feature runtimes.

Preferred shape:

~~~csharp
Func<ProfileDocument, ResolvedActiveProfile?> activeProfileResolver
~~~

Equivalent minimal design is acceptable.

This shape preserves the current feature ownership:

~~~text
feature runtime
→ loads ProfileStore under its existing gate
→ calls shared resolver with that document
→ receives common resolved profile or null
→ applies existing game override / Device fallback logic
~~~

Example:

~~~csharp
var loaded = _profileStore.Load();
var active = _activeProfileResolver(loaded.Document);

if (active?.Performance.CpuBoost is { Enabled: true } gameCpu)
{
    // existing game CPU apply path
}
else
{
    // existing Device CPU fallback
}
~~~

Do not inject separate Steam and XBOX sources into each runtime.

Forbidden:

~~~text
Func<uint> steamAppIdSource
Func<string?> xboxGameKeySource
~~~

inside a feature runtime.

---

## 8. CpuBoostRuntime

CpuBoostRuntime remains the only CPU Boost owner.

Change only the game-profile selection seam:

~~~text
OLD
Steam AppID
→ document.Games
→ game CPU override
→ existing apply

NEW
ResolvedActiveProfile
→ Performance.CpuBoost
→ SAME existing apply
~~~

Preserve:

- baseline/bootstrap;
- complete AC/DC baseline requirement;
- persist-before-apply;
- unsafe-load behavior;
- mutation gate;
- snapshot semantics;
- no persistence rollback after apply failure.

Do not add XBOX logic to WindowsCpuBoostPowerPolicy.

---

## 9. TdpRuntime

TdpRuntime remains the only TDP owner.

Target:

~~~text
ResolvedActiveProfile
→ Performance.Tdp
→ SAME TDP queue
→ SAME helper/hardware path
~~~

Preserve:

- MsiClawTdpPolicy validation;
- helper transport;
- queue/tail;
- authority/reconcile versions;
- cache invalidation;
- power-source lifecycle watcher;
- resume settle/retry;
- Center M manual Device seed;
- failure behavior.

If an active resolved game TDP exists but is invalid, preserve the existing fail-closed behavior.

No XBOX-specific TDP code path is allowed.

---

## 10. PowerModeRuntime

PowerModeRuntime remains the only Power Mode owner.

Replace only:

~~~text
Steam AppID profile lookup
~~~

with:

~~~text
ResolvedActiveProfile.Performance.PowerMode
~~~

Preserve all current bootstrap, readback, mutation-gate, persistence, and failure semantics.

No platform branch inside PowerModeRuntime.

---

## 11. IntelFrameLimiterRuntime

IntelFrameLimiterRuntime remains the only Intel FPS owner.

Target:

~~~text
ResolvedActiveProfile
→ Performance.FpsLimit
→ SAME IGCL FRAME_LIMIT path
~~~

The current native call is global:

~~~text
ApplicationName = 0
ApplicationNameLength = 0
~~~

Therefore do not invent a numeric XBOX AppID.

If diagnostic context must change, use TargetLabel only for logging.

Preserve:

- IGCL ABI;
- ownership marker;
- fail-close disable;
- startup recovery;
- shutdown cleanup;
- valid FPS range;
- AC/DC notification.

No platform-specific FPS implementation.

---

## 12. GameDisplayResolutionRuntime

GameDisplayResolutionRuntime remains the only display-resolution owner.

Replace only:

~~~text
Steam AppID profile lookup
~~~

with:

~~~text
ResolvedActiveProfile.Display.Resolution
~~~

Preserve:

- stale startup recovery;
- original-mode capture;
- recovery file;
- fail-closed restore;
- shutdown restore.

Required transition:

~~~text
Steam resolution A
→ effective profile becomes XBOX resolution B
→ SAME runtime applies B
→ original pre-game mode remains the recovery baseline

XBOX exits and no active profile remains
→ SAME runtime restores original
~~~

---

## 13. Host-level convergence

AddonProcessHost coordinates; it does not apply hardware itself.

Add one helper conceptually:

~~~text
ReconcileEffectiveGameProfile(trigger)
    target = CaptureActiveProfileTarget()
    resolver supplies ResolvedActiveProfile

    SAME CpuBoostRuntime
    SAME PowerModeRuntime
    SAME GameDisplayResolutionRuntime
    SAME TdpRuntime
    SAME IntelFrameLimiterRuntime
~~~

Each feature keeps independent exception/failure handling.

### Steam event

Keep the existing Steam path:

~~~text
OnActualRunningAppIdChanged
→ existing RequestControllerPresentationReconcile(...)
→ effective-profile convergence
~~~

Do not alter Steam detector behavior.

### XBOX event

Subscribe:

~~~csharp
_xboxGameSessionRuntime.ActiveGameChanged += OnActiveXboxGameChanged;
~~~

Handler:

~~~text
ActiveXboxGame changed
→ effective-profile convergence
~~~

Do not request controller-presentation reconcile for XBOX identity changes.

Unsubscribe during shutdown.

---

## 14. Startup

Required:

~~~text
Runtime initializes
→ XBOX session starts/reconciles
→ Device/Profile startup phase
→ capture effective target
→ resolve common profile
→ SAME feature runtimes reconcile
~~~

No active game:

~~~text
ResolvedActiveProfile = null
→ existing Device/global behavior
~~~

XBOX detection failure must not block Runtime startup.

---

## 15. XBOX process exit

Required:

~~~text
ActiveXboxGame XboxA → null
→ recompute effective target
→ no Steam target
→ ResolvedActiveProfile = null
→ SAME runtime owners converge to Device/global state
~~~

Expected behavior:

- CPU Boost → Device policy;
- TDP → Device policy;
- Power Mode → Device policy;
- Intel FPS → release Addon-owned FRAME_LIMIT if no game profile owns it;
- Display → restore original mode if no active resolution override remains.

No platform-specific teardown.

---

## 16. Steam/XBOX overlap

Steam priority remains fixed.

~~~text
XBOX alive + Steam AppID 0
→ resolve XBOX

Steam AppID becomes non-zero
→ resolve Steam

Steam AppID returns to zero while XBOX remains alive
→ resolve XBOX
~~~

All three transitions use the same downstream runtime calls.

---

## 17. Resume and AC/DC lifecycle

Keep existing lifecycle owners.

Resume:

~~~text
XboxGameSessionRuntime.ReconcileAfterResumeAsync()
existing Full1902 presentation resume
existing performance settle
existing TdpPowerLifecycleWatcher
~~~

Only the game-profile source becomes platform-neutral.

Do not add another power coordinator.

AC/DC:

~~~text
effective XBOX profile
→ existing TDP/FPS power notification
→ SAME runtime
→ common resolved profile
→ XBOX profile's AC/DC value

effective Steam profile
→ same path
→ Steam profile's AC/DC value
~~~

---

## 18. Frontend surface contract and live XBOX profile mutations

Main App and Overlay are different editing surfaces, not different apply authorities.

Required product semantics:

~~~text
Main App
→ may select any installed profile
→ persist selected profile changes
→ if selected profile == current effective profile: live reconcile
→ otherwise: persistence only

Overlay
→ active-game/current-profile oriented
→ uses the same typed frontend mutation authority
→ uses the same shared runtime apply path
~~~

PR8 does not add XBOX profile UI to Overlay; that remains a later phase. However, PR8 must preserve the shared mutation/apply architecture so future XBOX Overlay projection can bind to the same authority without another hardware path.

Forbidden:

~~~text
Main App apply implementation
Overlay apply implementation
Steam apply implementation
XBOX apply implementation
~~~

There is one persistence/mutation authority and one shared feature-runtime apply path.

Current XBOX mutations are persistence-only.

After successful persistence, apply immediately only when the mutated XBOX key is the current effective target.

Important:

> The live apply must go through the same platform-neutral existing runtime owner used by Steam. No XBOX hardware method is allowed.

Recommended mapping:

~~~text
Favorite
→ persistence only

Profile Enabled
→ reconcile all five existing feature runtimes

CPU Boost
→ CpuBoostRuntime

TDP
→ TdpRuntime

Power Mode
→ PowerModeRuntime

FPS
→ IntelFrameLimiterRuntime

Resolution
→ GameDisplayResolutionRuntime
~~~

Inactive XBOX profile mutation remains persistence-only.

If Steam is currently effective, XBOX mutation remains persistence-only.

Failure policy:

~~~text
persist succeeds
→ apply fails
→ persisted user choice remains
→ report ApplyFailed where supported
→ no rollback
~~~

---

## 19. Steam mutation behavior is regression-locked

The existing Steam behavior defines the surface semantics that XBOX should match:

~~~text
Main App edits inactive Steam profile
→ persistence only

Main App edits currently active Steam profile
→ persistence + live reconcile

Overlay edits current Steam profile
→ same typed mutation APIs
→ same live reconcile
~~~

Do not redesign Steam frontend mutation APIs.

Keep:

~~~text
uint Steam AppID contracts
ProfileDocument.Games
existing Steam editing semantics
~~~

Current active Steam mutations must still live-apply through the same existing runtime owners.

Inactive Steam mutations remain persistence-only.

---

## 20. README and architecture documentation

Update README.md in the implementation PR so it no longer states that XBOX profile changes never apply live.

The README must distinguish:

~~~text
offline/pre-launch editing capability
≠ offline-only apply policy
~~~

Required wording/meaning:

- XBOX profiles remain editable before launch and while inactive;
- inactive-profile edits only persist;
- editing the currently effective XBOX profile performs live reconciliation;
- Steam and XBOX use platform-specific detection/identity;
- after profile resolution, both platforms use the same CPU/TDP/Power/FPS/Resolution runtime owners;
- Main App and Overlay are frontend surfaces over the same mutation/apply authority.

Keep the architecture authority synchronized with this rule.

---

## 21. Frontend protocol

No new RPC or schema.

Keep:

~~~text
FrontendTransportProtocol.CurrentVersion = 58
~~~

---

## 22. Logging

Platform identity belongs in resolver/host diagnostic context.

Preferred:

~~~text
ProfileTarget=Steam:553850
ProfileTarget=Xbox:store:...
ProfileTarget=None
~~~

Feature runtime behavior must not depend on this label.

Do not emit fake XBOX RunningAppID values.

Steam detector-specific RunningAppID logs remain unchanged.

---

## 23. Expected files

Likely:

~~~text
src/SteamInputAddonforClaw/Profiles/ActiveProfileTarget.cs
src/SteamInputAddonforClaw/Profiles/ActiveProfileResolver.cs

src/SteamInputAddonforClaw/Profiles/Performance/CpuBoostRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/TdpRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/PowerModeRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
src/SteamInputAddonforClaw/Profiles/Display/GameDisplayResolutionRuntime.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

The five runtime files may change only enough to replace embedded Steam-only game-profile selection with the common resolved-profile source.

Do not duplicate runtime ownership.

---

## 24. Required tests

### 24.1 Resolver

Required:

1. None → no resolved profile.
2. Steam AppID → exact enabled GameProfile.
3. disabled/missing Steam → no resolved profile.
4. XBOX key → exact enabled XboxGameProfile.
5. disabled/missing XBOX → no resolved profile.
6. both active → Steam wins.
7. Steam ends while XBOX remains live → XBOX resolves.
8. both source types project to the same GamePerformanceOverrides / GameDisplayOverrides view.

### 24.2 Platform-neutral runtime equivalence

For each feature:

~~~text
Steam-resolved profile X
and
XBOX-resolved profile X
→ identical feature target/effect
~~~

There must be no XBOX-specific runtime path in the test.

### 24.3 Steam regressions

Required:

- existing Steam active profile apply unchanged;
- Steam exit fallback unchanged;
- active Steam mutation apply unchanged;
- inactive Steam mutation persistence-only behavior unchanged;
- Steam RunningAppID authority unchanged;
- Steam/BPM presentation unchanged.

### 24.4 XBOX live apply

Required:

- CPU Boost;
- TDP;
- Power Mode;
- Intel FPS;
- Resolution;
- process exit Device/global convergence.

### 24.5 Lifecycle

Required:

- startup with already-running XBOX game;
- controlled Runtime restart with XBOX game alive;
- Sleep/Hibernate/Resume;
- AC/DC switch;
- XBOX process exit;
- XBOX → Steam → XBOX transition.

### 24.6 XBOX mutations

Extend XboxGameProfileFrontendTests.

Required:

- Favorite never applies hardware;
- active XBOX CPU edit uses existing CpuBoostRuntime;
- active XBOX TDP edit uses existing TdpRuntime;
- active XBOX Power edit uses existing PowerModeRuntime;
- active XBOX FPS edit uses existing IntelFrameLimiterRuntime;
- active XBOX Resolution edit uses existing GameDisplayResolutionRuntime;
- inactive XBOX edit persists only;
- XBOX edit while Steam is effective persists only;
- apply failure does not roll back persistence.

Do not add pathological timing-race tests outside supported lifecycle.

---

## 25. Manual MSI Claw validation

Validate one field-proven XBOX title.

### A. Live apply

~~~text
XBOX detected
→ XBOX profile resolved
→ SAME existing five runtime owners
→ settings applied
~~~

### B. Alt+Tab

Matched XBOX process remains alive, so profile remains effective.

### C. Exit

Exit game and verify Device/global convergence through the same runtimes.

### D. Steam regression

Launch an existing Steam-profile title and verify behavior is indistinguishable from pre-PR8 behavior.

### E. Cross-platform transition

~~~text
XBOX effective
→ Steam starts
→ resolver selects Steam
→ SAME runtimes update

Steam ends, XBOX still alive
→ resolver selects XBOX
→ SAME runtimes update
~~~

---

## 26. Explicit non-goals

Do not implement:

- Epic;
- GOG;
- Custom EXE;
- shared Windows process detector;
- HHC-style ProcessManager;
- ClawHUD IPC;
- XBOX-specific feature runtimes;
- platform-specific hardware apply methods;
- generic provider/plugin framework;
- XBOX per-game M1/M2;
- Overlay XBOX projection;
- Xbox app action;
- Steam detector changes;
- Steam persistence migration;
- controller-presentation changes;
- polling;
- protocol bump.

---

## 27. Overengineering constraints

Preferred:

~~~text
Steam identity ─┐
                ├─ active-profile resolver
XBOX identity ──┘
                     ↓
              common profile view
                     ↓
          SAME existing runtime owners
~~~

Reject:

~~~text
platform
→ platform manager
→ platform-specific runtime
→ hardware
~~~

Protect realistic:

- game start;
- game exit;
- Runtime restart;
- Sleep/Hibernate/Resume;
- AC/DC;
- persistence failure;
- hardware apply failure.

Do not add locks/epochs/state machines for theoretical interleavings.

---

## 28. Source-audit requirements

Before PR submission verify:

~~~text
SteamSessionRuntime.ActualRunningAppId remains Steam authority

XboxGameSessionRuntime remains XBOX authority

exactly one production owner of each:
    CpuBoostRuntime
    TdpRuntime
    PowerModeRuntime
    IntelFrameLimiterRuntime
    GameDisplayResolutionRuntime

no feature runtime contains:
    ActiveProfileTargetKind
    Steam/Xbox switch
    ProfileDocument.Games versus XboxGames selection
    XboxGameSessionRuntime access
    SteamSessionRuntime access

no frontend surface contains direct hardware apply logic:
    Main App
    Overlay

Main App and Overlay dispatch through the same typed frontend mutation authority

platform collection selection exists only in active-profile resolution

no XboxCpuBoostRuntime
no XboxTdpRuntime
no XboxPowerModeRuntime
no XboxIntelFpsRuntime
no XboxDisplayResolutionRuntime

FrontendTransportProtocol.CurrentVersion remains 58
~~~

Required commands:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

---

## 29. Completion condition

PR8 is complete only when:

~~~text
Steam identity
or
XBOX identity
        ↓
one resolver selects enabled profile
        ↓
one common ResolvedActiveProfile
        ↓
existing five runtime owners apply it
        ↓
no runtime knows which platform supplied it
~~~

XBOX path:

~~~text
XboxGameSessionRuntime.ActiveGame
→ XboxGames lookup in resolver
→ ResolvedActiveProfile
→ existing CpuBoostRuntime
→ existing TdpRuntime
→ existing PowerModeRuntime
→ existing IntelFrameLimiterRuntime
→ existing GameDisplayResolutionRuntime
~~~

Steam path:

~~~text
SteamSessionRuntime.ActualRunningAppId
→ Games lookup in resolver
→ same ResolvedActiveProfile
→ same existing runtime owners
~~~

No duplicate performance implementation is acceptable.

After PR8, a later Custom EXE / Epic / GOG identity source must terminate at the same resolver boundary and reuse the same runtime owners.
