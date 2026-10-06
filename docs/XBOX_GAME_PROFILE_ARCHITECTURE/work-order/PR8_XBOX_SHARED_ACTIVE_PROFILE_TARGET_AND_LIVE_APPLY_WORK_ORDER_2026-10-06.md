# Work Order — XBOX PR8: Shared Active Profile Target and Live XBOX Profile Apply

> **Date:** 2026-10-06  
> **Repository:** \`onehoon/SteamAddonforClaw\`  
> **Reviewed main:** \`main@d16ebde10bb1cdd4fa8cb38cd6ceb82b2ccacb8a\`  
> **Architecture authority:** \`docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md\`  
> **Full1902 authority:** \`docs/Full 1902 Implementation/README.md\` and its active precedence chain  
> **Previous XBOX phase:** PR #695 / merge \`21e07e7693779751e07a4f17361d4251461d9e93\` promoted the field-proven detector into production \`XboxGameSessionRuntime\`  
> **Current frontend protocol:** \`FrontendTransportProtocol.CurrentVersion = 58\`  
> **Scope:** introduce one narrow derived \`ActiveProfileTarget\`, select Steam first / XBOX second / Device baseline otherwise, refactor the existing CPU Boost / TDP / Windows Power Mode / Intel FPS / Display Resolution runtimes to resolve Steam or XBOX profiles through one small shared lookup policy, wire \`XboxGameSessionRuntime.ActiveGameChanged\` into live profile convergence, and make active XBOX profile mutations apply immediately  
> **Out of scope:** generic game-detection framework, Epic/GOG/custom EXE detection, Steam detector rewrite, XBOX per-game M1/M2, Overlay XBOX projection, Xbox app front-button action, controller presentation changes, new frontend RPCs

---

## 1. Goal

Complete the XBOX core vertical slice from proven active-game identity to real per-game machine-setting application.

Current production state after PR7:

~~~text
Steam
  SteamSessionRuntime.ActualRunningAppId
      ↓
  existing Steam profile apply

XBOX
  XboxGameSessionRuntime
      ↓
  ActiveXboxGame(Key, DisplayName)
      ↓
      STOP
~~~

PR8 target:

~~~text
SteamSessionRuntime.ActualRunningAppId
            │
            ├── Steam target
            │
XboxGameSessionRuntime.ActiveGame
            │
            └── XBOX target
                    ↓
            ActiveProfileTarget
                    ↓
         shared enabled-profile lookup
                    ↓
     existing apply implementations only
       ├─ CPU Boost
       ├─ TDP
       ├─ Windows Power Mode
       ├─ Intel FPS Limit
       └─ Display Resolution
~~~

The XBOX profile must become effective while its proven XBOX game process is alive and must converge back to Device/global policy when that process exits.

This PR is the final core XBOX performance-profile integration step before broader non-Steam detection work.

---

## 2. Locked authority boundaries

### 2.1 Steam identity remains separate and authoritative

Do not replace or wrap the existing Steam detector.

Steam remains:

~~~text
SteamSessionRuntime.ActualRunningAppId
→ numeric Steam AppID
→ ProfileDocument.Games
~~~

PR8 may consume this fact when deriving the effective profile target, but must not change how Steam RunningAppID is detected.

Do not add Steam to:

- WinEvent XBOX detection;
- process/executable matching;
- a generic Windows game detector;
- a universal string game-ID abstraction.

### 2.2 XBOX identity remains owned by XboxGameSessionRuntime

PR8 consumes:

~~~csharp
XboxGameSessionRuntime.ActiveGame
XboxGameSessionRuntime.ActiveGameChanged
~~~

Do not move XBOX package/config/executable validation into the profile layer.

Do not change the proven XBOX identity path:

~~~text
WinEvent
→ HWND/PID
→ process generation
→ package identity
→ MicrosoftGame.config
→ exact executable match
→ ActiveXboxGame
~~~

### 2.3 ActiveProfileTarget is derived state only

\`ActiveProfileTarget\` is not:

- persisted game identity;
- a new game database;
- a detector;
- a platform registry;
- an entitlement model;
- a replacement for Steam AppID or XBOX canonical keys.

It is only the narrow selector used by machine-wide profile apply code.

### 2.4 Full1902 controller authority is unchanged

PR8 must not change:

~~~text
Center M Enabled/Disabled authority
PID1901 / PID1902 ownership
DirectInput
HidHide
VIIPER
Xbox360 / SteamDeck presentation selection
WING / Win+G suppression
PnP recovery
Sleep controller recovery
rumble
LED
vibration
M1/M2 mapping
~~~

In particular:

~~~text
ActiveProfileTarget = Xbox
≠ force Xbox360
≠ detach SteamDeck
≠ change physical PID
≠ change HidHide
~~~

Steam/BPM remains the only current virtual-presentation selector.

---

## 3. Current code facts that must drive the implementation

Reviewed \`main@d16ebde10bb1cdd4fa8cb38cd6ceb82b2ccacb8a\`.

### 3.1 Production XBOX session owner already exists

Current production files:

~~~text
src/SteamInputAddonforClaw/Xbox/Session/
    ActiveXboxGame.cs
    XboxGameSessionRuntime.cs
    XboxGameWindowEventSource.cs
    XboxGameProcessIdentityProbe.cs
    XboxGameProcessIdentityEvaluator.cs
~~~

\`XboxGameSessionRuntime\` already:

- starts for normal Runtime lifetime;
- publishes \`ActiveGame\`;
- raises \`ActiveGameChanged\`;
- retains matched process lifetime;
- clears on matched process exit;
- performs bounded startup reconciliation;
- re-arms/reconciles on resume;
- does not change profile settings or controller presentation.

Do not redesign it in PR8.

### 3.2 XBOX persistence already uses the same override shapes as Steam

Current persistence:

~~~text
ProfileDocument.Games
    key = Steam AppID string
    value = GameProfile

ProfileDocument.XboxGames
    key = canonical XBOX string key
    value = XboxGameProfile
~~~

Both profile types expose:

~~~text
Enabled
Performance : GamePerformanceOverrides
Display     : GameDisplayOverrides
~~~

Therefore the apply layer does not need platform-specific hardware runtimes.

### 3.3 Existing apply runtimes are Steam-AppID-shaped

Current code still assumes numeric Steam AppID at its profile-selection boundary.

Examples:

~~~text
CpuBoostRuntime
    SetActualAppIdSource(Func<uint>)
    Reconcile(uint actualAppId)
    StartupReconcile(uint actualAppId)

TdpRuntime
    SetActualAppIdSource(Func<uint>)
    ResolveEffectiveTdp(ProfileDocument, uint actualAppId)

PowerModeRuntime
    SetActualAppIdSource(Func<uint>)
    Reconcile(uint appId)
    ApplyEffective(... reads document.Games)

IntelFrameLimiterRuntime
    SetActualAppIdSource(Func<uint>)
    Reconcile(uint appId)
    ApplyPolicy(... reads document.Games)

GameDisplayResolutionRuntime
    Reconcile(uint appId)
    reads document.Games
~~~

PR8 removes this Steam-only assumption from the **apply boundary**, not from Steam detection.

### 3.4 Host currently reacts only to Steam identity changes

\`AddonProcessHost.OnActualRunningAppIdChanged(uint appId)\` currently reconciles:

- CPU Boost;
- Power Mode;
- Display Resolution;
- TDP;
- Intel FPS.

\`XboxGameSessionRuntime.ActiveGameChanged\` currently has no production consumer outside the runtime itself.

PR8 adds that missing XBOX convergence path.

### 3.5 XBOX frontend mutations are persistence-only today

Current \`InProcessAddonFrontendControl.MutateXboxGame(...)\`:

~~~text
persist mutation
→ map result
→ return refreshed XBOX profile snapshot
~~~

It does not apply the changed setting even when that XBOX game is currently active.

PR8 must make active XBOX mutations follow the same persist-first, then live-apply behavior already used by Steam profile mutations.

---

## 4. Add one narrow ActiveProfileTarget value

Add one small internal model under the Profiles domain, for example:

~~~text
src/SteamInputAddonforClaw/Profiles/ActiveProfileTarget.cs
~~~

Recommended conceptual contract:

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

Required semantic states:

~~~text
None
→ Kind=None
→ SteamAppId=0
→ XboxGameKey=null

Steam
→ Kind=Steam
→ SteamAppId!=0
→ XboxGameKey=null

Xbox
→ Kind=Xbox
→ SteamAppId=0
→ XboxGameKey=non-empty canonical key
~~~

Do not expose arbitrary public construction that makes invalid mixed states easy.

Expected log labels:

~~~text
None
Steam:553850
Xbox:store:9XXXXXXXXXXX
Xbox:pfn:...
Xbox:identity:...
~~~

Do not create:

- \`IGameIdentity\`;
- \`IGamePlatform\`;
- \`GameSessionManager\`;
- provider registries;
- plugin abstractions;
- a universal persisted game key.

---

## 5. One selection policy in AddonProcessHost

Add one narrow method/fact in \`AddonProcessHost\` that derives the current machine-setting target from the two existing authorities.

Required priority:

~~~text
if SteamSessionRuntime.ActualRunningAppId != 0
    → ActiveProfileTarget.Steam(appId)

else if XboxGameSessionRuntime.ActiveGame != null
    → ActiveProfileTarget.Xbox(activeXbox.Key)

else
    → ActiveProfileTarget.None
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

This is a product-priority rule, not simultaneous-game arbitration.

Do not add:

- epochs;
- priorities stored in another state machine;
- foreground arbitration;
- process scoring;
- multi-game ownership.

Unsupported simultaneous Steam + XBOX gameplay does not justify more machinery.

---

## 6. Add one small shared enabled-profile lookup policy

The five hardware runtimes must not each duplicate:

~~~text
if Steam → document.Games
if Xbox  → document.XboxGames
~~~

Add one small lookup helper, for example:

~~~text
Profiles/ActiveProfileLookup.cs
~~~

It may return a narrow common view such as:

~~~csharp
internal readonly record struct ActiveGameProfileView(
    GamePerformanceOverrides Performance,
    GameDisplayOverrides Display);
~~~

Required lookup semantics:

~~~text
Target=None
→ no game profile

Target=Steam
→ exact numeric AppID key in ProfileDocument.Games
→ profile must exist
→ profile.Enabled must be true

Target=Xbox
→ exact canonical key in ProfileDocument.XboxGames
→ profile must exist
→ profile.Enabled must be true
~~~

No fuzzy/case-rewritten identity conversion should be invented beyond the collection's current key semantics.

The helper must not:

- mutate persistence;
- apply hardware;
- read Steam/XBOX detectors;
- own lifecycle;
- fall back to Device settings itself.

Each feature runtime continues to own its existing Device fallback / release policy.

---

## 7. Refactor CPU Boost to ActiveProfileTarget

Current Steam behavior must remain unchanged.

Replace the Steam-only profile lookup boundary with \`ActiveProfileTarget\`.

Recommended API direction:

~~~text
SetActualAppIdSource(Func<uint>)
→ SetActiveProfileTargetSource(Func<ActiveProfileTarget>)

Reconcile(uint)
→ Reconcile(ActiveProfileTarget)

ReconcileWithResult(uint)
→ ReconcileWithResult(ActiveProfileTarget)

StartupReconcile(uint)
→ StartupReconcile(ActiveProfileTarget)
~~~

Equivalent naming is acceptable if the ownership remains obvious.

Required effective policy:

~~~text
enabled active game profile has enabled CPU Boost override
→ apply game AC/DC override

otherwise
→ existing Device CPU Boost policy

Device disabled/uninitialized
→ preserve existing behavior exactly
~~~

Preserve all current CPU Boost invariants:

- first-run baseline bootstrap;
- complete AC/DC baseline requirement;
- persistence-before-Windows-write ordering;
- unsafe profile-load behavior;
- no rollback of persisted user choice when Windows apply fails;
- mutation gate serialization;
- snapshot semantics.

Do not rewrite \`WindowsCpuBoostPowerPolicy\`.

---

## 8. Refactor TDP to ActiveProfileTarget

Replace \`Func<uint> _actualAppIdSource\` with the active profile target source.

\`ResolveEffectiveTdp\` must resolve the shared enabled game-profile view.

Required policy:

~~~text
enabled active game profile
+ enabled valid game TDP
→ use game TDP

no enabled game TDP
→ existing enabled Device TDP

active game TDP exists but is outside current model ranges
→ preserve current fail-closed behavior
→ do not silently fall through to another TDP target
~~~

Preserve:

- \`MsiClawTdpPolicy\` validation;
- TDP helper/transport ownership;
- queue/tail semantics;
- authority/reconcile versions;
- hardware-cache invalidation;
- AC/DC power lifecycle watcher;
- resume settle/retry policy;
- Center M manual seed behavior;
- existing helper failure handling.

Do not create \`XboxTdpRuntime\`.

\`TdpPowerLifecycleWatcher\` must continue to call the same one \`TdpRuntime\`; because that runtime now reads \`ActiveProfileTarget\`, AC/DC/resume lifecycle applies the current Steam or XBOX profile automatically.

---

## 9. Refactor Windows Power Mode to ActiveProfileTarget

Replace the Steam-only source/lookup with \`ActiveProfileTarget\`.

Required policy:

~~~text
enabled active game profile
+ enabled Power Mode override
→ apply game AC/DC modes

otherwise
→ existing enabled Device Power Mode

Device disabled
→ preserve current no-op behavior
~~~

Preserve:

- first-run Device Power Mode bootstrap;
- persist-then-apply mutation policy;
- current readback/snapshot behavior;
- existing failure result classification;
- mutation gate.

Do not rewrite \`WindowsPowerModePolicy\`.

---

## 10. Refactor Intel FPS to ActiveProfileTarget without fake AppIDs

The Intel frame limiter is currently global at the IGCL call site:

~~~text
ApplicationName = 0
ApplicationNameLength = 0
~~~

The current \`uint appId\` passed through \`IIntelFrameLimiter\` / \`NativeIgcl.Set\` is diagnostic context, not the native per-app selector.

PR8 must not invent a numeric AppID for XBOX.

Required profile policy:

~~~text
enabled active game profile
+ enabled FPS limit
→ apply AC/DC FPS target

otherwise
→ release Addon-owned FRAME_LIMIT state using existing fail-close rules
~~~

Preserve:

- startup stale-ownership recovery;
- ownership marker semantics;
- immediate disable on marker-persist failure;
- fail-close cleanup behavior;
- supported 40–120 validation;
- AC/DC notification behavior;
- shutdown cleanup.

Logging must become platform-neutral at the profile layer:

~~~text
ProfileTarget=Steam:553850
ProfileTarget=Xbox:store:...
ProfileTarget=None
~~~

If the lower-level \`IIntelFrameLimiter\` / \`NativeIgcl\` diagnostic argument is changed, keep it diagnostic-only. A small managed rename from numeric \`appId\` to a target label is acceptable and preferable to logging \`RunningAppID=0\` for XBOX.

Do not change the IGCL ABI or start using per-app ApplicationName in PR8.

---

## 11. Refactor Display Resolution to ActiveProfileTarget

Change:

~~~text
GameDisplayResolutionRuntime.Reconcile(uint appId)
~~~

to consume \`ActiveProfileTarget\`.

Required policy:

~~~text
enabled active game profile
+ Resolution override
→ apply target resolution

target has no enabled resolution
→ restore saved original display mode if Addon currently owns an override
~~~

Preserve existing recovery ownership:

- startup stale-recovery file handling;
- capture original mode before first override;
- one original baseline across live target changes;
- fail-closed restore on apply/capture failure;
- shutdown restore;
- recovery-file deletion only after confirmed restore.

Important live transition:

~~~text
Steam profile resolution A
→ Steam ends while an XBOX game session is still live
→ effective target becomes XBOX
→ apply XBOX resolution B using the same original baseline

XBOX exits
→ target None
→ restore original pre-game display mode
~~~

Do not restore original between two live profile targets unless the existing resolution owner requires it for a real correctness reason.

---

## 12. Add one host-level profile convergence entrypoint

Refactor the profile-apply portion of \`OnActualRunningAppIdChanged\` into one host helper that accepts/captures the current \`ActiveProfileTarget\`.

Conceptually:

~~~text
ReconcileActiveProfileTarget(trigger)
    target = CaptureActiveProfileTarget()

    CPU Boost      reconcile(target)
    Power Mode     reconcile(target)
    Display        reconcile(target)
    TDP            reconcile current target
    Intel FPS      reconcile(target)
~~~

Each feature remains independently guarded with its existing error logging so one failure does not prevent the other features from converging.

The host helper is coordination only.

It must not become:

- another profile persistence owner;
- another hardware implementation;
- another detector;
- a generic scheduler.

### Steam event

Keep:

~~~text
OnActualRunningAppIdChanged
→ RequestControllerPresentationReconcile("RunningAppIdChanged")
~~~

exactly as the controller-presentation path.

Then call the profile-target convergence helper.

Steam identity changes continue to affect presentation.

### XBOX event

Subscribe to:

~~~csharp
_xboxGameSessionRuntime.ActiveGameChanged += OnActiveXboxGameChanged;
~~~

The subscription should be established before or as part of production session startup so no normal active-game transition is missed.

The XBOX handler:

~~~text
ActiveXboxGame changes
→ recompute ActiveProfileTarget
→ reconcile machine-setting profiles
~~~

It must **not** call:

~~~text
RequestControllerPresentationReconcile
~~~

XBOX game identity is not a VIIPER presentation authority.

Unsubscribe during process shutdown before disposing the XBOX session owner.

---

## 13. Startup convergence

\`ReconcileDeviceProfileStartup()\` must use the fresh derived target rather than only \`ActualRunningAppId\`.

Required order remains compatible with current startup design:

~~~text
controller Runtime initialized
→ XBOX production session owner already started / bounded startup reconcile attempted
→ deferred Device/Profile startup phase
→ capture current ActiveProfileTarget
→ reconcile Display / CPU / Power / Intel FPS
→ existing TDP lifecycle startup schedule resolves the same target source
~~~

If XBOX detection is unavailable or failed to start:

~~~text
Steam AppID == 0
+ ActiveXboxGame == null
→ ActiveProfileTarget.None
→ Device/global policy
~~~

XBOX detection failure must not fail Runtime startup.

---

## 14. Process exit and target transitions

The following transitions are required.

### XBOX start

~~~text
Steam AppID = 0
ActiveXboxGame: null → XboxA
→ target None → XboxA
→ apply enabled XboxA profile
~~~

### XBOX exit

~~~text
Steam AppID = 0
ActiveXboxGame: XboxA → null
→ target XboxA → None
→ CPU/TDP/Power converge to Device policy
→ Intel FPS releases Addon-owned limit
→ Display restores original if owned
~~~

### Steam takes priority

~~~text
XboxA remains alive
Steam AppID: 0 → 553850
→ target XboxA → Steam:553850
→ Steam profile becomes effective
→ controller presentation follows existing Steam/BPM policy
~~~

### Steam ends while XBOX remains alive

~~~text
XboxA remains alive
Steam AppID: 553850 → 0
→ target Steam:553850 → XboxA
→ XBOX profile becomes effective
~~~

Do not add simultaneous-game arbitration beyond this fixed Steam-first rule.

---

## 15. Resume behavior

Keep the existing controller resume path independent.

Current:

~~~text
OnPowerResumeObserved
→ XboxGameSessionRuntime.ReconcileAfterResumeAsync()
→ immediate controller-presentation reconcile
→ existing device/profile delayed settle
→ TDP lifecycle watcher handles its own resume settle/retry
~~~

PR8 requirements:

1. XBOX session resume reconciliation remains the identity owner.
2. CPU Boost / Power Mode / Display Resolution / Intel FPS resume work must capture the current \`ActiveProfileTarget\`, not only Steam AppID.
3. TDP resume remains owned by \`TdpPowerLifecycleWatcher\`; only its target source changes to \`ActiveProfileTarget\`.
4. Do not add another generic power lifecycle framework.
5. Do not add polling.
6. A resume-time identity transition emitted by \`ActiveGameChanged\` must also converge profiles normally.

Do not wait on controller presentation to apply profile settings, and do not make profile failure affect Full1902 resume safety.

---

## 16. AC/DC power-source behavior

Current Intel FPS AC/DC callback uses:

~~~text
_runtimeHost?.ActualRunningAppId ?? 0
~~~

Change it to the active profile target.

TDP already receives its own power notification and should resolve the same active target through its target source.

Required result:

~~~text
XBOX game active
→ AC ↔ DC
→ XBOX FPS AC/DC value selected
→ XBOX TDP AC/DC value selected
~~~

No periodic power polling is required.

---

## 17. Live XBOX profile mutations

Current XBOX mutations are persistence-only.

PR8 must apply a successful mutation immediately **only when that XBOX key is the effective current target**.

Effective means:

~~~text
ActiveProfileTarget.Kind == Xbox
&& target.XboxGameKey == mutated key
~~~

Because Steam has priority, this naturally means:

~~~text
Steam AppID != 0
→ XBOX mutation persists
→ no XBOX live hardware apply
~~~

### Feature-specific apply

Do not write unrelated machine settings after every mutation.

Recommended mapping:

~~~text
Favorite
→ persistence only

Profile Enabled
→ reconcile all five profile-owned features

CPU Boost enabled/AC/DC
→ CPU Boost only

TDP enabled/values
→ TDP only

Power Mode enabled/AC/DC
→ Power Mode only

FPS enabled/AC/DC
→ Intel FPS only

Resolution
→ Display Resolution only
~~~

A small internal enum/flags local to the frontend mutation helper is acceptable if it reduces duplicated branches.

Do not create a general command bus.

### Failure semantics

Keep existing product policy:

~~~text
persist succeeded
→ live apply attempted

live apply failed
→ persisted user choice remains
→ return ApplyFailed where the existing frontend result contract supports it
→ do not roll persistence back
~~~

Do not weaken per-feature fail-close behavior.

The existing frontend DTO/RPC surface is already sufficient. No new frontend method is required.

---

## 18. Keep Steam profile mutation behavior working

Steam profile mutation APIs remain numeric-AppID public/frontend contracts.

They must continue to:

- persist Steam profile edits;
- immediately apply edits when the effective target is that Steam AppID;
- not apply an inactive Steam game's edits;
- preserve current per-feature result behavior.

Internal implementation may reuse the new \`ActiveProfileTarget\` source/lookup, but do not migrate Steam frontend DTOs to generic string IDs.

---

## 19. Frontend transport

PR8 requires no new RPC and no wire-schema change.

Therefore:

~~~text
FrontendTransportProtocol.CurrentVersion remains 58
~~~

Do not bump the protocol merely because internal apply behavior changes.

Do not add an active-XBOX frontend DTO in this PR.

Overlay XBOX active-profile projection remains Phase X5.

---

## 20. Logging

Replace profile-selection logs that would be ambiguous outside Steam with stable target context.

Preferred:

~~~text
ProfileTarget=Steam:553850
ProfileTarget=Xbox:store:...
ProfileTarget=None
Reason=ActualRunningAppIdChanged
Reason=ActiveXboxGameChanged
Reason=Startup
Reason=PowerResume
Reason=PowerSourceChanged
Reason=Mutation
~~~

Keep useful feature-specific native details.

Do not remove current Steam RunningAppID diagnostics where they remain specifically about the Steam detector itself.

Do not log an XBOX target as a fake numeric RunningAppID.

---

## 21. Suggested file changes

Expected production files include, but are not limited to:

~~~text
src/SteamInputAddonforClaw/Profiles/ActiveProfileTarget.cs
src/SteamInputAddonforClaw/Profiles/ActiveProfileLookup.cs

src/SteamInputAddonforClaw/Profiles/Performance/CpuBoostRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/TdpRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/PowerModeRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
src/SteamInputAddonforClaw/Profiles/Display/GameDisplayResolutionRuntime.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

Do not move or rename the XBOX detector files merely for this PR.

Do not create separate XBOX hardware runtime classes.

---

## 22. Tests

Add focused tests before relying on full-suite coverage.

### 22.1 ActiveProfileTarget / lookup tests

Required:

1. None resolves no game profile.
2. Steam exact AppID resolves enabled \`GameProfile\`.
3. disabled Steam profile resolves no game override.
4. XBOX exact canonical key resolves enabled \`XboxGameProfile\`.
5. disabled XBOX profile resolves no game override.
6. missing key resolves no game override.
7. target selection chooses Steam when both Steam AppID and ActiveXboxGame exist.
8. target selection chooses XBOX when Steam AppID is zero.
9. target selection chooses None when neither is active.

### 22.2 CPU Boost tests

Required:

- active XBOX CPU profile overrides Device;
- XBOX profile disabled/missing falls back to Device;
- XBOX exit/None converges to Device;
- Steam behavior remains unchanged;
- Device mutation while XBOX profile is active does not incorrectly override the active game policy.

### 22.3 TDP tests

Required:

- active XBOX TDP profile queues the correct AC/DC values;
- invalid active XBOX TDP preserves current fail-closed/deferred behavior;
- XBOX exit falls back to Device TDP;
- power-source change while XBOX target is active selects the correct XBOX side;
- existing queue/cache/retry tests remain passing.

### 22.4 Power Mode tests

Required:

- active XBOX Power Mode overrides Device;
- XBOX exit restores Device policy;
- disabled/missing XBOX override falls back to Device;
- Steam tests remain unchanged.

### 22.5 Intel FPS tests

Required:

- active XBOX FPS profile applies;
- active XBOX exit releases owned FRAME_LIMIT;
- XBOX AC/DC change selects the correct target value;
- no fake XBOX numeric AppID is required by the managed policy;
- ownership-marker/fail-close tests remain passing;
- Steam behavior remains unchanged.

### 22.6 Display tests

Required:

- active XBOX resolution applies;
- XBOX exit restores original mode;
- Steam → XBOX effective target transition changes to the XBOX resolution without losing the original recovery baseline;
- XBOX → Steam transition works equivalently;
- apply/capture failure still restores fail-closed.

### 22.7 Host wiring tests

Required:

- production XBOX session remains Runtime-owned;
- \`ActiveGameChanged\` is subscribed for normal runtime lifetime and unsubscribed on shutdown;
- XBOX active-game change triggers profile convergence;
- XBOX active-game change does not request controller-presentation switching;
- Steam RunningAppID change still requests controller-presentation reconcile first/independently;
- Steam-first target priority is enforced;
- startup profile reconcile can consume an already-reconciled ActiveXboxGame;
- resume profile reconcile reads ActiveProfileTarget;
- headless uninstall preparation does not start the XBOX session owner.

Do not add timing-race tests for pathological callback interleavings that are outside normal product lifecycle.

### 22.8 Frontend mutation tests

Extend \`XboxGameProfileFrontendTests\`.

Required:

- Favorite mutation never applies hardware;
- active XBOX profile enable/disable applies all profile-owned features;
- active XBOX CPU mutation applies CPU only;
- active XBOX TDP mutation applies TDP only;
- active XBOX Power mutation applies Power only;
- active XBOX FPS mutation applies FPS only;
- active XBOX Resolution mutation applies Display only;
- inactive XBOX mutation persists without apply;
- XBOX mutation while Steam target is active persists without XBOX apply;
- apply failure returns the existing ApplyFailed outcome where applicable and does not roll persistence back.

---

## 23. Manual validation on MSI Claw

After automated tests pass, validate on the supported MSI Claw.

Use at least one already field-proven XBOX title from the PR2/PR7 validation set.

### A. Basic live apply

1. Configure a distinctive XBOX profile.
2. Launch the game.
3. Confirm positive XBOX identity.
4. Confirm log:
   ~~~text
   ProfileTarget=Xbox:<canonical key>
   ~~~
5. Confirm configured TDP / CPU Boost / Power / FPS / Resolution values apply as applicable.

### B. Alt+Tab retention

~~~text
XBOX game running
→ Alt+Tab to desktop/app
→ XBOX process remains alive
→ XBOX profile remains effective
~~~

Foreground loss must not restore Device settings.

### C. Process exit restore

Exit the XBOX game normally.

Verify:

- CPU Boost returns to Device policy;
- TDP returns to Device policy;
- Power Mode returns to Device policy;
- Intel FPS ownership is released when no active profile owns it;
- Display Resolution restores the original mode;
- no controller presentation/authority mutation occurs because of the XBOX exit.

### D. Live profile edit

While the XBOX game is active:

- change one TDP field;
- change CPU Boost;
- change Power Mode;
- change FPS;
- change Resolution.

Verify each setting applies immediately and unrelated settings are not gratuitously rewritten.

### E. Runtime restart

With the XBOX game already running:

~~~text
controlled Runtime restart
→ bounded XBOX startup reconcile
→ ActiveProfileTarget=Xbox
→ XBOX profile reapplied
~~~

### F. Sleep / Resume

With the XBOX game active:

~~~text
Sleep/Hibernate
→ Resume
→ XBOX session reconciles
→ existing profile/lifecycle settle
→ XBOX profile remains/reapplies correctly
~~~

Verify Full1902 controller recovery remains unchanged.

### G. Steam regression

Launch a normal Steam game with a Steam profile.

Verify:

- numeric RunningAppID remains authoritative;
- Steam profile applies exactly as before;
- SteamDeck/Xbox360 presentation behavior is unchanged;
- XBOX session events do not override the Steam profile while Steam RunningAppID is nonzero.

---

## 24. Explicit non-goals

Do not implement in PR8:

- Epic detection;
- GOG detection;
- custom EXE registration;
- shared/general Windows game detector extraction;
- generic launcher framework;
- HHC-style universal ProcessManager;
- ClawHUD IPC;
- XBOX per-game M1/M2;
- Overlay XBOX active profile;
- Xbox app launch action;
- Game Bar integration;
- PresentMon detection;
- foreground/fullscreen heuristics;
- periodic polling;
- Steam identity migration;
- Steam frontend genericization;
- controller presentation changes;
- multi-game arbitration;
- new profile schema version;
- frontend protocol bump.

The shared Windows observation extraction for future Custom/Epic/GOG support should be considered only after this end-to-end XBOX profile path is complete.

---

## 25. Architecture / overengineering constraints

Follow the project review policy.

PR8 should protect realistic lifecycle behavior:

- XBOX game start;
- XBOX process exit;
- Runtime startup/restart;
- Sleep/Hibernate/Resume;
- AC/DC transition;
- real persistence/apply failure;
- active profile mutation;
- Steam ↔ XBOX effective-target transition;
- profile disable / Device fallback.

Do not add state/locks/epochs/barriers solely for theoretical instruction-level races.

Prefer:

~~~text
existing Steam authority
+ existing XboxGameSessionRuntime authority
→ one derived target
→ one small lookup policy
→ existing feature owners
~~~

The goal is not minimum LOC.

The goal is one clear target-selection rule and no duplicated hardware authority.

---

## 26. Required validation

Before opening the PR:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

Also perform source audits:

~~~text
ActiveProfileTarget exists exactly once as the shared apply selector
XboxGameSessionRuntime still owns XBOX identity only
SteamSessionRuntime.ActualRunningAppId remains the Steam detector authority
no XboxCpuBoostRuntime / XboxTdpRuntime / XboxPowerModeRuntime / XboxFpsRuntime / XboxDisplayRuntime
no generic game identity/provider registry
no new polling loop
FrontendTransportProtocol.CurrentVersion remains 58
~~~

---

## 27. PR description requirements

The implementation PR description must explicitly state:

- reviewed architecture/main baseline;
- one derived \`ActiveProfileTarget\` was added;
- Steam RunningAppID remains untouched and has priority;
- XBOX active identity comes only from \`XboxGameSessionRuntime\`;
- the existing five apply implementations were reused;
- XBOX process exit returns to Device/global policy;
- active XBOX mutations now apply live;
- no controller-presentation behavior changed;
- no generic game-detection framework was added;
- frontend protocol remains 58;
- automated validation results;
- real MSI Claw manual validation performed or explicitly pending.

---

## 28. Completion condition

PR8 is complete only when this full path works:

~~~text
installed XBOX profile
→ exact production XBOX game detection
→ ActiveXboxGame
→ ActiveProfileTarget.Xbox
→ persisted XboxGameProfile
→ existing CPU/TDP/Power/FPS/Display owners
→ live machine settings

matched XBOX process exits
→ ActiveXboxGame=None
→ ActiveProfileTarget.None
→ Device/global convergence
~~~

At that point the core XBOX game-profile implementation is end-to-end complete.

Per-game M1/M2, Overlay projection, and front-button Xbox action remain separate follow-up phases.

The next game-detection expansion should then introduce Custom EXE as the second Windows-process consumer and extract only the Windows observation/process-lifetime mechanics that are proven to be genuinely shared.
