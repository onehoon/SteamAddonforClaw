# Work Order — Overlay Profile: Conditional Active-Game Recheck on "No Running Game"

> Date: 2026-10-10  
> Repository: `onehoon/SteamAddonforClaw`  
> Product: Standalone Full1902 (no CTW integration)  
> Reference: `docs/Full 1902 Implementation/README.md` and its current authority chain  
> Overlay contract: `docs/work-order/PR11_XBOX_OVERLAY_ACTIVE_GAME_PROFILE_AND_PERFORMANCE_DISPLAY_PARITY_WORK_ORDER_2026-10-07.md`  
> Existing profile-first polish: `docs/work-order/1007_XBOX_OVERLAY_M1_M2_AND_PROFILE_LOADING_POLISH_WORK_ORDER.md`  
> Current Overlay protocol on inspected main: `OverlayTransportProtocol.CurrentVersion = 18`  
> Implementation owner: local Codex. Physical-device validation owner: user, after merge.

## 1. Goal

Recover from a false or stale **"No game is currently running"** state in the standalone Overlay's **Profile** tab, particularly when a freshly added Steam non-Steam shortcut is launched before the Main App's game list has been refreshed.

Do **not** require a game-catalog refresh, restart, manual Refresh button, or unconditional profile recapture on every Profile tab entry.

The target behavior is:

```text
Overlay Profile first show / entry
    |
    +-- authoritative active-game profile is available
    |       -> show immediately; NO extra recheck
    |
    +-- authoritative result = exact "No running game" empty state
            -> ONE conditional Runtime recheck for this Profile entry
                 -> Steam actual RunningAppID / XBOX active game read again
                 -> recognized target: publish existing active-profile page
                 -> still none: retain existing no-game empty state
```

This is a **bounded on-demand retry**, not a new detector, background watcher, periodic poll, or second profile authority.

## 2. Verified code facts and diagnosis boundary

The following behavior exists in the inspected code:

1. `AddonProcessHost.CoordinateOverlayToggleAsync()` already starts a **post-capture** `_overlayController.RefreshQuickSettingsAsync(preferActiveProfile)` when the Overlay first opens. Hence the initial Show is **not** missing all profile refreshes.
2. `AddonProcessHost.CaptureOverlayProfileQuickSettingsPageAsync()` evaluates `CaptureActiveQuickSettingsProfileTarget()`. Only a null target produces the exact message:
   `No game is currently running. Start a game to configure its profile.`
3. `CaptureActiveProfileTarget()` uses `_runtimeHost.ActualRunningAppId` (Steam) followed by `_xboxGameSessionRuntime?.ActiveGame` (XBOX). It does **not** require the game to be present in the Main App's installed-game catalog.
4. `SteamSessionRuntime.ActualRunningAppId` invokes `SteamRunningAppIdRegistrySource.GetRunningAppId()`, which performs a fresh read of `HKCU\Software\Valve\Steam\RunningAppID` on each call. It is **not** a cached game list.
5. `OverlayWindow.Profile.cs:OnProfileTabSelectionChanged()` currently re-renders the last bound page without asking the Runtime to recapture.
6. `OverlayWindow.Profile.cs:ApplyActiveProfilePage()` receives authoritative Runtime snapshots, and `HasRenderableActiveProfile()` recognizes a valid active target.
7. `OverlayProcessController.RefreshQuickSettingsAsync()` already uses `_quickSettingsRefreshGate` to serialize state capture/publish. `QuickSettingsPageState` already carries the desired reply.

**Root-cause qualification:** this work order addresses the observable stale/transient no-game state. The precise cause of the reported occurrence is not established by a runtime log; do not claim that an unrefreshed `shortcuts.vdf` catalog is proven to be the cause. Re-reading the same Steam registry value cannot manufacture an AppID if Steam still reports zero.

Do not mix in the separate 007 / non-Steam shortcut AppID-mismatch issue.

## 3. Locked scope and trigger policy

### 3.1 Only the exact no-active-game empty state qualifies

A recheck is allowed only when the **authoritative Profile page** is:

```csharp
page is {
    PageId: QuickSettingsPageId.Profile,
    Available: false,
    ProfileTarget: null,
    Message: "No game is currently running. Start a game to configure its profile."
}
```

Use the actual contract/property types in current HEAD; extract one narrowly scoped predicate if useful. Do not infer no-game merely from `!Available`: a capture error, persistence failure, target mismatch, malformed state, or shutdown is **not** a signal to retry.

Do not treat the local `"Loading the active game profile."` placeholder as no-game.

### 3.2 Recheck only when the user is viewing Profile

- **Initial Show:** keep the existing initial Runtime snapshot publication. If the first authoritative Profile snapshot is the exact no-game state **and Profile is the selected tab**, request a one-shot recheck.
- **Profile tab entry/re-entry:** if the bound authoritative snapshot is already the exact no-game state, request one recheck. If a renderable profile exists, **do nothing beyond normal presentation**.
- **Snapshot arrives while Profile is selected:** if this is the first exact no-game snapshot of this entry, permit the one-shot recheck. This covers the case where the Profile tab was selected before the initial state arrived.
- **Profile not selected:** do not request a recheck. On later entry, inspect the then-current authoritative page.
- Once a conditional request was initiated for that Profile entry, **do not initiate another on its no-game reply**. Reset that narrow UI-side entry flag when leaving and entering Profile, or on a new Show. Do not install continuous timers, retry loops, or repeated requests on each render.
- A successfully captured valid profile needs no conditional recheck.
- Existing Runtime-driven `ActualRunningAppIdChanged` / `ActiveGameChanged` refreshes remain authoritative and must keep working.

Initial Show is already in flight independently. Preserve the existing `PrepareActiveProfileFirstShow()` loading behavior and avoid sending a redundant stale-page request during the initial loading placeholder. If Profile is the initially selected tab due to configured order, ensure its stale page from a prior Show is not mistaken for a newly delivered no-game snapshot.

### 3.3 A small, single, conditional retry

A once-only short delay (suggested **250–300 ms**) **after the first no-game result and before the requested re-read** is acceptable to allow transient Steam identity publication to settle. It must be cancelable on Overlay hide/disconnect/shutdown, and must not block the named-pipe receive loop or Overlay capture/teardown.

No delay on success, no multiple attempts, no polling timer, and no general Steam-process scan. If a shorter or zero-delay one-shot already solves the verified scenario, prefer it.

## 4. Narrow implementation plan

### 4.1 Overlay UI: trigger at Profile entry / authoritative no-game delivery

**File:** `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs`

Extend the existing Profile selection/presentation path with one narrowly scoped callback such as:

```csharp
internal Action? NoRunningGameProfileRecheckRequested { get; set; }
```

Illustrative logic (adapt to current class/layout):

```csharp
private void TryRequestNoRunningGameRecheck(QuickSettingsPageSnapshot? page)
{
    if (_tabState.SelectedTab != AddonQuickSettingsTabId.Profile ||
        _activeProfileInitialLoadPending ||
        _noGameRecheckRequestedForThisEntry ||
        !IsExactNoRunningGamePage(page))
        return;

    _noGameRecheckRequestedForThisEntry = true;
    NoRunningGameProfileRecheckRequested?.Invoke();
}
```

Invoke on real Profile selection and on authoritative Profile state application. Keep **one flag** tied to the selected-tab entry; do not introduce generation/epoch/state-machine infrastructure solely for theoretical races.

No Refresh button, no catalog tab, no UX redesign, no profile mutation changes.

### 4.2 Overlay transport: one no-payload request, existing page-state reply

**Files:**

- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`

Introduce **one** new Overlay -> Runtime message kind (e.g. `NoRunningGameProfileRecheckRequest`). It needs **no profile ID**, catalog payload, response record, correlation ID, or extra contract: the Runtime owns the current active target.

Increment `OverlayTransportProtocol.CurrentVersion` from the inspected value of **18 to 19** (or from the latest HEAD version by one if it changed before implementation). Retain strict handshake version compatibility; no pre-release compatibility shim.

- UI callback -> `App.xaml.cs` -> `NamedPipeOverlayClient` -> narrow request frame under the existing write gate.
- Server validates a payload-free request and checks Ready/Visible/current connection; reject/ignore non-visible, malformed, or disconnected requests appropriately.
- Server routes it to one Runtime-owned recheck callback; **do not block the sole named-pipe read loop** while delaying/capturing.
- Response is the **existing** `QuickSettingsPageState`, with `QuickSettingsPageId.Profile`. Do not create a second recheck result model.
- Follow existing protocol validation conventions and update relevant tests.

If current HEAD already exposes an equivalent safe, typed Overlay -> Runtime refresh request, reuse it instead of adding a duplicate protocol message.

### 4.3 Runtime: Profile-only recapture, not broad refresh

**Files:**

- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

Connect the server's request to the **existing** `CaptureOverlayProfileQuickSettingsPageAsync()` authority, then publish that page using `SendQuickSettingsPageStateAsync()`. Reuse `_quickSettingsRefreshGate` for orderly capture/publication alongside the initial Show and Runtime invalidation refreshes.

- Read **current** `CaptureActiveQuickSettingsProfileTarget()` again.
- If Steam/XBOX target is now present, capture the profile through the existing `_frontendControl.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, target, token)`.
- If target is still null, publish the ordinary no-game snapshot.
- Run only when the Overlay is Visible and the coordinated Overlay capture has actually committed; no publication before `_overlayCaptureActive`.
- Capture/profile publication exceptions remain feature-local, consistent with existing `PublishQuickSettingsPageAsync()`. Never stall the Overlay hide/retirement path.
- Keep refresh **Profile-only**; do not recapture Device/ClawHUD/settings as a side effect.
- Do not call `SteamSessionRuntime.Refresh()` to satisfy a UI recheck. It calls `OnActualRunningAppIdChanged`, which can initiate controller presentation/profile reconcile and is not merely a fresh cache read.
- Do not trigger `RefreshGamesAsync()`, rescan the entire `shortcuts.vdf` catalog to **identify** a running game, or mutate Steam/HidHide/VIIPER state.

**Minimal read path:**

```text
No-running Profile page visible
 -> one Overlay -> Runtime request
 -> short one-shot delay (only if justified)
 -> CaptureActiveQuickSettingsProfileTarget()
      Steam: fresh RunningAppID registry read
      XBOX: existing ActiveGame runtime fact
 -> CaptureOverlayProfileQuickSettingsPageAsync()
 -> existing Profile QuickSettingsPageState
 -> ApplyActiveProfilePage()
```

A fresh capture may incidentally enrich an **already identified** Steam game's display name through existing frontend logic. Do not redesign that path; catalog membership is not a precondition for active-game identity.

## 5. Implementation constraints

- **No** manual Refresh button.
- **No** unconditional recheck for a valid active-game profile.
- **No** full Steam catalog refresh, shortcut reimport, cache invalidation subsystem, process enumeration, or new background game detector.
- **No** recurring polling, exponential retry, retry manager, second owner, or generalized IPC framework.
- **No** changes to game-profile settings/persistence, Steam AppID mapping/identity semantics, Steam shortcut creation, or XBOX key semantics.
- **No** new controller state/reconcile behavior.
- **No** alteration to Full1902 PID1901/PID1902, VIIPER, HidHide, Center M authority, SteamDeck/Xbox360 presentation, suspend/resume, restart/shutdown, or fail-close policies.
- Preserve existing profile-first Show UX and its no-false-no-game-flash behavior.
- One Windows user / one interactive session only; no multi-session compatibility work.

## 6. Tests and acceptance criteria (local Codex)

Add/adjust **automated tests** at the existing test seams. Do not require physical-hardware testing as a PR completion or merge condition.

1. **Valid Steam profile on initial capture:** appears normally; no conditional request; no second Registry/profile read attributable to this feature.
2. **Valid XBOX profile:** same fast path, no conditional request.
3. **Exact no-game page, Profile selected:** exactly one recheck request; a newly available Steam/XBOX target yields a renderable Profile state.
4. **Exact no-game page remains no-game:** one request maximum for this tab entry, no loop when the reply arrives, original empty state remains correct.
5. **Enter Profile after cached no-game:** requests once; leave and re-enter permits one new attempt.
6. **No-game while another tab selected:** no request until actual Profile entry.
7. **Unavailable for a different reason / loading placeholder:** must **not** cause a conditional retry.
8. **Existing initial Show and Runtime-driven active-target update:** still publish normally, preserving Profile-first behavior.
9. **Hide/disconnect/shutdown while the conditional request is pending:** no late publication to a hidden/retired Overlay, no blocked input/teardown, no fatal exception.
10. **Protocol:** new no-payload request validated, version mismatch fails closed, and existing `QuickSettingsPageState` remains the only response.
11. **Scope:** no new catalog refresh and no call to `SteamSessionRuntime.Refresh()` from this path.

Real-device smoke tests (fresh Steam non-Steam shortcut launch, active game, and true no-game) are **user-owned after merge**. Do not list them as local Codex blockers.

## 7. Completion / handoff

Local Codex should implement the smallest coherent production change, include focused automated tests, and report:

- files changed;
- what exact no-game condition triggers the one-shot recheck;
- which existing Runtime authority performs the fresh active-game read;
- automated build/test results;
- any remaining uncertainty, especially that a second read cannot find a game Steam never reports.

Do not expand this PR into a speculative Steam non-Steam shortcut AppID fix or a general controller lifecycle refactor.
