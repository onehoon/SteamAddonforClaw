# Work Order — PR-B: Controller / Steam Profile Selection UI and EN/KO User Guides

**Date:** 2026-10-11  
**Repository:** `onehoon/SteamAddonforClaw`  
**Reviewed baseline:** `main` at `a27ce72cc0e5722cb7687a92a509d263049e724d` (PR #762 squash merge)  
**Implementer:** Local Codex  
**Type:** One focused PR for main-app UI, user-facing documentation and corresponding automated tests.  
**Prerequisite:** PR-A / #762 is already merged; do not reimplement its Runtime policy.

## 1. Read the authoritative docs and the current code first

Start at `docs/Full 1902 Implementation/README.md` and follow its active precedence documents for controller ownership, sleep/resume, cleanup and standalone process architecture. Also read:

- `docs/work-order/STEAM_VIRTUAL_CONTROLLER_PR_A_TRIPLE_VIIPER_AND_GAME_PROFILE_POLICY_WORK_ORDER_2026-10-11.md` (PR-A contract, now implemented).
- `docs/DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md` for historical behavior; its older optional/lazy Edge and Experimental assumptions are superseded by #762.
- `docs/VIIPER_INTEGRATION.md` for the existing owner boundary.
- Existing UI architecture/work orders only when compatible with current source. The **current Main App tabs are Device, Controller, Steam, XBOX, ClawHUD, Shortcut, How to Use and Settings**.

This is **standalone Full1902**, not CTW integration. This PR is deliberately UI/documentation-oriented and must not add controller state machines, device managers, new presentation owners or manual hot-swap pathways.

### Verified code anchors on merged main

| Source | Current contract / required edit |
| --- | --- |
| `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml` | Contains the current `SteamVirtualControllerCard` with the too-long description, `SteamVirtualControllerComboBox` and `DualSense Edge (Experimental)`. Remove this one card. |
| `src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs` | Contains last-confirmed selector state, `RenderSteamVirtualController` and `SteamVirtualControllerComboBox_SelectionChanged`; transfer the useful parts to Controller, and delete orphan Settings code. Preserve other Settings functionality. |
| `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml` | Page title, then `MappingUnavailableText` and `MappingContent` (visibility depends on physical hardware), then Button Mapping, M1/M2, LED and vibration. Insert global card **above all those**. |
| `src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs` | `Initialize(FrontendBootstrapSnapshot, IAddonFrontendControl, Func<nint>)`, `Activate()`, `_isLoading`, already using `_frontend`. Bind the moved selector here, avoiding a second settings API. |
| `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml` | Steam game's detail `ScrollViewer` begins with TDP; selected game's master `ProfileEnabledToggle` is above it. Insert per-game selector first inside detail settings stack. This is **Steam**, not XBOX. |
| `src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs` | Existing `BeginProfileLoad`, `CaptureSelectedAsync`, `Render`, `SetEditorsEnabled`, `ProfileEnabledToggle_Toggled`, `IsCurrentProfileResponse`, `RestoreSelectedAfterMutationFailureAsync`. Reuse their render / stale-response / error pattern. |
| `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs` | Initializes both pages with the existing frontend, owns `RefreshSystemStatusAsync`, `RenderSystemStatus`, page Activate and `StateInvalidated`. Existing status propagation is the right UI refresh owner. |
| `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs` | Already has `FrontendSettingsSnapshot.SteamVirtualController`, nullable `FrontendGameProfileSnapshot.SteamVirtualController`, `FrontendStatusSnapshot.Steam.Active`, and both setter methods. |
| `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs` | PR-A already enforces the Runtime Steam/BPM editing guard and Profile ON policy; blocked global setter returns saved state, per-game setter returns `Unavailable`. |
| `src/SteamInputAddonforClaw/Status/SystemStatusProvider.cs` | `FrontendStatusSnapshot.Steam.Active` is true when `RunningAppId != 0 || BigPictureActive`; both cases use the same effective activity status. |
| `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` | `OnActualRunningAppIdChanged` and `OnBigPictureStateChanged` trigger presentation reconciliation. Current `StateInvalidated` often follows changed controller badge, not necessarily every session-fact change. |
| `README.md` | Canonical English user guide, incorrectly names Settings selector and experimental Edge; also has an English/Korean gyro subsection. |
| `docs/howtouse/README_KO.md` | **Canonical full Korean README / How to Use guide**, ~358 lines; outdated Settings references, Edge experimental labels and gyro note. MUST edit this file. |
| `docs/KOREAN_USER_GUIDE.md` | Only a redirect to `docs/howtouse/README_KO.md`; leave unchanged unless its link is broken. |

**Important:** No separate root `README.ko.md` exists. Do **not** create one as a substitute. Update `README.md` **and** `docs/howtouse/README_KO.md`. The app's How to Use page already points to the latter.

## 2. Final product policy (no design alternatives)

1. Windows with neither Steam game nor BPM active: Xbox360 presentation.
2. BPM without running game: global preference, default Steam Deck; optional DualSense Edge.
3. Steam game: **enabled game's** per-game selection wins; `Use default`, missing override or disabled profile inherits global preference.
4. A BPM game with Edge override **automatically** switches Deck → Edge at game detection, then Edge → global Deck when it ends in BPM (inverse global/override case likewise). This behavior is already implemented in PR-A: **do not suppress automatic selection or reconciliation**.
5. User **cannot manually change** either global selector or a game's override while Steam game **or** BPM is active, even if the current presentation type already matches the desired value. This is **edit lock only**. No Overlay control, forced switch button, temporary session selection, restart prompt or game compatibility policy.
6. Per-game selector is editable **only with master Profile ON**; no controller-specific enable toggle. Profile OFF displays but disables its saved choice. Re-enabling preserves and exposes it.
7. DualSense Edge option label is simply **`DualSense Edge`**. Eliminate user-facing `Experimental` / `실험 기능` qualifiers attached to Edge; do not misrepresent unvalidated device/game behavior as verified.

## 3. Move the global SettingsCard to the very top of Controller

### XAML

Remove `SteamVirtualControllerCard` from `SettingsPage.xaml`. Move/adapt it into `ControllerPage.xaml` **immediately below the page title/subtitle and above `MappingUnavailableText` and `MappingContent`**. It must not be hidden when the button-mapping/LED capability block is hidden.

Use the same CommunityToolkit `SettingsCard`, a ComboBox, and established WinUI controls. Suggested XAML (adapt only to actual Toolkit constraints):

```xml
<ctcontrols:SettingsCard x:Name="SteamVirtualControllerCard"
    Header="Steam Virtual Controller"
    Description="Choose the virtual controller used by Steam.&#10;Cannot change during Steam games or Big Picture Mode.">
    <ctcontrols:SettingsCard.HeaderIcon>
        <SymbolIcon Symbol="Contact" />
    </ctcontrols:SettingsCard.HeaderIcon>
    <ComboBox x:Name="SteamVirtualControllerComboBox"
              MinWidth="240"
              SelectionChanged="SteamVirtualControllerComboBox_SelectionChanged">
        <ComboBoxItem Content="Steam Deck (Default)" Tag="SteamDeck" />
        <ComboBoxItem Content="DualSense Edge" Tag="DualSenseEdge" />
    </ComboBox>
</ctcontrols:SettingsCard>
```

**Two concise visible lines are required**, not one long line. If the Toolkit Description property flattens `&#10;`, render two short description TextBlocks using supported content layout; do not add an elaborate custom card, another paragraph or a “may disconnect” warning.

Move the existing last-confirmed preference and selection-change logic from `SettingsPage.xaml.cs` into `ControllerPage.xaml.cs`, adapting to existing `Initialize` / `Activate`:
- Render `bootstrap.Settings.SteamVirtualController` without emitting a save (`_isLoading` / equivalent narrow suppress flag).
- On user selection, call existing `_frontend.SetSteamVirtualControllerAsync` only once and serialize concurrent edits.
- Use the **returned persisted preference** as truth. Runtime may refuse a click just as Steam/BPM becomes active; show the persisted selection, not a false optimistic value.
- On RPC failure, restore the last confirmed selection, report failure through existing UI patterns and avoid leaving a fictitious selected value.
- On page re-entry, refresh from existing `GetBootstrapAsync` if needed; do not create a polling timer or new settings authority.
- Remove now-dead selector methods, fields, XAML names from Settings; **do not alter** Updates, Gaming Home, logging, Required Components, power-source setting, Developer Menu.

## 4. Add the per-game selector at the top of Steam profile details

Add a **single `SettingsCard`** inside the Steam `ProfilePage.xaml` detail ScrollViewer's StackPanel **before TDP**, with Header **`Virtual Controller`**, concise description `Overrides the Controller default for this Steam game.`, and a ComboBox:

- `Use default` → `null`
- `Steam Deck` → `SteamVirtualControllerPreference.SteamDeck`
- `DualSense Edge` → `SteamVirtualControllerPreference.DualSenseEdge`

No extra toggle/expander and no XBOX page equivalent. The existing master `ProfileEnabledToggle` controls whether this choice can be edited/applied.

Update `ProfilePage.xaml.cs`:
- `BeginProfileLoad` and `ClearSelection`: clear/disable old game's controller value so a quick game change cannot submit to the prior AppID.
- `Render`: select the existing `snapshot.SteamVirtualController`; apply programmatic changes under `_suppressEvents` to avoid saving on initial load or after profile enable/disable.
- `SetEditorsEnabled` / final render: enable controller combo only when `snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable`, no Steam/BPM active and no save in progress. **Unlike Resolution**, do not permit the controller combo to edit with Profile OFF.
- Selection handler: capture selected game's AppID, call existing `_frontend.SetGameProfileSteamVirtualControllerAsync(appId, value)`, check `IsCurrentProfileResponse`, and render the **returned** `FrontendGameProfileMutationResult.Snapshot` on both success and `Unavailable`. Reuse `ProfileInfoBar` and `RestoreSelectedAfterMutationFailureAsync` on failures. A failed/rejected save cannot remain optimistically selected.
- Turning a profile OFF should disable the selector immediately yet retain its saved choice; ON restores the choice. Do not touch unrelated performance settings or existing draft/debounce behavior.
- Do not write profiles.json from UI, change the PR-A mutation contract or add another data source.

## 5. Keep the UI lock in sync with actual Steam/BPM activity

**Re-use existing status data; no new Steam watcher or RPC:** `FrontendStatusSnapshot.Steam.Active` already represents `RunningAppId != 0 || BigPictureActive`.

- In `MainWindow.RenderSystemStatus`, pass the observed active/inactive status to both Controller and Steam Profile pages through narrow view methods, or another equally small existing UI render path. If status capture/transport is unavailable, **disable the selectors conservatively** until a reliable refresh; do not equate an unknown status with idle.
- Refresh editability on page Activate, selected-game detail load, profile ON/OFF mutation completion and ordinary frontend status invalidation. Use the existing `RefreshSystemStatusAsync`, `StateInvalidated` and page methods rather than a new polling timer or UI Steam-status manager.
- **Concrete event gap identified:** `AddonProcessHost.OnActualRunningAppIdChanged` and `OnBigPictureStateChanged` request presentation reconcile, but present `StateInvalidated` notifications are often dependent on a *controller badge change*. A Steam/BPM fact can change without changing the attached presentation (e.g. a Steam game starts while BPM is already showing Deck, or the controller is unavailable). Explicitly request one existing frontend status invalidation at those real session-event boundaries, as necessary, so the Main App disables/re-enables the controls even when the badge does not change. Keep it event-driven and do not create a new observation authority.
- The frontend Runtime guards added by #762 remain **authoritative**. UI `IsEnabled` is advisory user experience, not a replacement for the Runtime guard. On a late rejected response, restore from returned snapshot and request a fresh existing status snapshot as appropriate.
- Keep unrelated Profile ON/OFF and performance controls working during a game; only **controller choice** is manually locked by Steam/BPM activity. Do not disable the master Profile toggle wholesale or prevent the profile's automatic startup action.

**Example status logic (UI only):**

```csharp
bool canEditGlobalController = statusKnown && !steamActive && !globalSaveInProgress;
bool canEditGameController = statusKnown && !steamActive
    && profile is { Exists: true, Enabled: true, PersistenceWritable: true }
    && !gameControllerSaveInProgress;
```

Recompute these whenever existing status or selected profile changes; do not cache an independent Runtime policy or invent per-app session state.

## 6. Update both canonical user guides in this PR

### A. `README.md` (English)

Edit the opening feature summary, `How controller presentation works`, `Controller`, `Steam`, `Settings`, `Game profiles and setting priority`, relevant troubleshooting and gyro notes:

- Replace every statement that the selector is at `Settings → Steam / Big Picture Controller` with **`Controller → Steam Virtual Controller`**.
- Document global `Steam Deck (Default)` and `DualSense Edge`, Xbox360 outside Steam/BPM, and one attached presentation.
- Explain per-game `Steam → [game] → Virtual Controller` choices `Use default / Steam Deck / DualSense Edge` and **Profile ON** requirement; `Use default` inherits global selection.
- Explain automatic BPM/game entry/exit selection, and that manual choices are disabled while a Steam game or BPM is active. Do **not** claim games universally support USB re-enumeration.
- Delete the obsolete `#### Steam / Big Picture Controller` subsection under Settings and place global selection under Controller.
- Remove `(Experimental)` and user-facing experimental-status descriptions from DualSense Edge. Preserve **concrete known** gyro/compatibility limitations and hardware verification statements accurately, without inventing validation. Avoid contradiction with Korean tested observations.

### B. `docs/howtouse/README_KO.md` (Korean)

This is the **real Korean README and the app's How to Use source**, not `docs/KOREAN_USER_GUIDE.md` (which only redirects here). Apply matching updates in natural Korean:

- Opening introduction, `컨트롤러 표현 방식` table and paragraphs.
- `### Controller` (new selector location), `### Steam` (game profile controller override).
- Remove outdated `#### Steam / Big Picture Controller` under `### Settings`, moving its relevant info under Controller.
- `게임 프로필과 설정 우선순위`, Steam-controller troubleshooting and `알려진 제한 사항`.
- Specific gyro section (currently discusses A2VM 8 Edge smoothness and Steam Deck Gyro To Mouse choppiness on 0.1.365): **keep the observed behavior, provenance link and unresolved Steam Deck issue** where accurate, but replace old Settings navigation references and remove `DualSense Edge (Experimental)`, `실험 기능`, and `실험 단계` labels. Do not convert incomplete testing into “verified on every model/game.”
- Keep English/Korean functional policies equivalent and avoid needlessly translating the entire existing README.

**README consistency pass:** Search both files for `Experimental`, `experimental`, `실험`, `Settings → Steam / Big Picture Controller`, and references to the old selector; eliminate stale product descriptions. Historical technical research docs and filenames may retain their archival text and need not be rewritten.

## 7. Regression tests / local Codex validation

Adapt and extend existing test suites, especially:

- `tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs` — deleted old selector expectations; ensure remaining Settings behavior preserved.
- `tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs` — global selector located only in Controller and placed before mapping; profile selector only on Steam detail, not XBOX/Overlay.
- Existing Controller/Profile UI tests and `tests/SteamInputAddonforClaw.Tests/SteamVirtualControllerFrontendTests.cs` / `SteamGameControllerProfileTests.cs` — no regression to PR-A policy.
- Where test harness supports it, cover page bootstrap selection without mutation, successful save, Runtime-rejected save restoring the returned persisted value, steam/BPM disabled state, Profile OFF disabled & retaining choice, Profile ON enabled when idle, `Use default` null, selected-game switching while async response is in flight, and transport failure restoring/locking UI.
- Test that session transition notifications can refresh UI **without relying on a change of controller badge**; status unavailability never opens editing.
- Build the solution and run the repository's automated test suites and CI-equivalent checks. Report accurate results.

**Local Codex is not responsible for real MSI hardware / live Steam / BPM validation.** Physical validation is performed by the user **after merge** and **must not be a PR review blocker**. Do not require physical Windows session tests in this work order.

## 8. Explicit boundaries and acceptance

**IN:** Move one existing SettingsCard into Controller top, wire its existing API, add one Steam per-game UI dropdown, status-driven UI lock via existing status/event path, focused test corrections, complete English and Korean README edits.

**OUT:** Overlay selector, Xbox game profile controller override, native controller objects, PID1902 ↔ PID1901, HidHide, VIIPER bus/server/teardown, Steam/VIIPER protocol changes, new abstractions/authority/polling, game-specific hotplug accommodations, reboot requirement for normal preference edits.

Success means the top Controller selector is usable only while Steam/BPM is idle; the Steam per-game selector requires enabled master profile *and* idle Steam/BPM; automatic presentation switching remains untouched; no unsaved UI selection masquerades as persisted state; both README versions reflect the exact same current behavior with no Edge “Experimental” qualifier; automated tests and CI remain green.
