# Work Order — Reserve the Quick Overlay Top Edge for ClawHUD Size

**Date:** 2026-10-25  
**Status:** Ready for local Codex implementation  
**Repository:** onehoon/SteamAddonforClaw  
**Implementation base:** main (reviewed at 72eb9b5ee58134135f044ce53530ed05eae5228d); recheck HEAD before editing  
**Deliverable:** One focused SteamAddon implementation PR  
**Implementation owner:** Local Codex  
**Real-device verification owner:** User, after merge; **never a PR blocker**

---

## 0. Locked product decision

Change **only the Addon's WinUI 3 Quick Overlay native window rectangle** so its top edge leaves room for the configured ClawHUD. The previous proposals to relocate ClawHUD, clip its FullWidth surface, or force ContentWidth are **withdrawn**.

Rules:

1. If the **ClawHUD DesiredEnabled setting is false**, the reserved top inset is **exactly 0 physical pixels**, regardless of the stored HUD size.
2. If **DesiredEnabled is true**, reserve a small physical-pixel height determined **only** by the existing **HUD Size offset (-2, -1, 0, +1, +2)**.
3. Do **not** ask whether a ClawHUD HWND exists, whether ClawHUD is currently visible, whether a game is running, or whether its runtime is Ready. InGameOnly visibility does not change this rule. The setting is the only policy authority.
4. For the current right-edge Overlay, retain the **existing X and Width**. Move the window's top down by the inset and reduce Height by the same amount; its bottom stays at the selected monitor's bottom:
   
   ~~~text
   Y      = monitorTop + min(insetPx, monitorHeight)
   Height = monitorHeight - min(insetPx, monitorHeight)
   X, Width = existing right-edge geometry (unchanged)
   ~~~
5. At **HUD Off**, the Overlay is once again full monitor height, touching top and bottom as before. When the HUD is On, only the top is inset; the bottom still touches the monitor edge.
6. The Overlay must respond to setting changes while open, including successful changes and rollback after a failed mutation; do not wait for ClawHUD, start a timer, or introduce a new IPC operation.
7. Preserve the ClawHUD renderer / Windows Presentation API / GPU resource lifecycle completely untouched. The Addon and ClawHUD remain independent; this is a passive Overlay-only space reservation.
8. Do not remove the ClawHUD FullWidth setting or change its alignment, font, visibility, rendering, or user preferences. This is **not** a ClawHUD refactor.

The goal is a predictable, deliberately small layout change, **not** automatic collision detection or pixel-perfect introspection of another process.

## 1. Architecture and source review

Use the active standalone Full1902 contract; ignore retired CTW integration material. Read these before implementation:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/work-order/OVERLAY_EDGE_TO_EDGE_L_RAIL_CLOCK_Y_FOOTER_WORK_ORDER_2026-10-25.md (current top/bottom/right-edge layout takes precedence over older floating-gap proposals)
- docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md (historical setting contract only)

Source files verified on main:

| File | Existing owner / finding |
| --- | --- |
| src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs | OverlayWindowGeometry.Calculate(monitorLeft, monitorTop, monitorRight, monitorBottom, dpi) currently preserves the right-edge X and returns full monitor Y/Height. |
| src/SteamInputAddonforClaw.Overlay/WindowInterop.cs | Configure() chooses the foreground monitor, performs provisional full-monitor placement for GetDpiForWindow, then applies the final HWND rectangle with SetWindowPos. |
| src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs | PrepareHidden() and ShowForPocAsync() call ConfigureWindow(); ShowForPocAsync() performs geometry configuration before making the Overlay visible. |
| src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs | A single existing OverlayClawHudMutationState owns ClawHUD frontend display state. ApplyClawHudState(), CompleteClawHudMutation(), and ApplyClawHudFailure() render after updating it. |
| src/SteamInputAddonforClaw.Overlay/OverlayClawHudMutationState.cs | DisplayEnabled reflects PendingEnabled or Snapshot.DesiredEnabled; DisplaySettings reflects pending size mutations or Snapshot.Settings. This already supports preview and rollback. |
| src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs | The existing FrontendClawHudSnapshot.DesiredEnabled and FrontendClawHudSettingsSnapshot.HudSizeOffset supply everything needed. |
| src/SteamInputAddonforClaw.Overlay/App.xaml.cs | Existing ClawHUD state/mutation results arrive on the Overlay UI dispatcher. No transport alteration is necessary. |
| tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs | Existing pure geometry tests cover reference display, multiple DPI, translated/negative monitor origins, and tiny monitors. |
| tests/SteamInputAddonforClaw.UiTests/OverlayClawHudMutationStateTests.cs | Existing mutation preview/rollback behavior. |
| tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs | Existing source-level contracts for Configure's provisional-DPI-before-final-placement ordering and the show path. |

**ClawHUD reference only; do not modify:** onehoon/ClawHUD integration/steamaddon, src/ClawHUD/HudSize.cpp and HudPresentation.cpp. Its 20 + sizeOffset main-font size affects a DirectWrite-measured text height plus 10 physical pixels. Consequently, **exact actual HUD HWND height is not a five-value constant independent of font/metrics**. This task intentionally uses a conservative fixed reservation based only on the user's chosen size; never describe it as an exact DirectWrite measurement.

## 2. Five-step inset policy

Put the mapping in **one place**, preferably OverlayWindowGeometry.cs alongside the existing pure geometry logic. Do not add a separate options DTO, service, configuration file, HWND detector, DPI manager, or generic geometry framework.

Use these conservative **initial product-layout reservation values (physical px)**:

| HUD Size | Enabled=true inset | Enabled=false inset |
| ---: | ---: | ---: |
| -2 | 40 | 0 |
| -1 | 42 | 0 |
| 0 | 44 | 0 |
| +1 | 46 | 0 |
| +2 | 48 | 0 |

These are intentionally a little larger than the nominal bar height inferred from the current ClawHUD 18–22 physical-pixel text and measured-height-plus-10 policy, to accommodate either supported font without runtime measurement. They are **fixed layout allowances, not hardware-measured exact values**. If an exact height discrepancy is discovered later, the **user's post-merge device check** can inform adjustment of these five constants. Do not block code merge waiting for that measurement.

Suggested minimal pure function (adapt naming to the current style):

~~~csharp
internal static int ClawHudTopInsetPx(bool desiredEnabled, int hudSizeOffset)
{
    if (!desiredEnabled) return 0;
    return Math.Clamp(hudSizeOffset, -2, 2) switch
    {
        -2 => 40,
        -1 => 42,
         0 => 44,
         1 => 46,
         2 => 48,
         _ => 44
    };
}
~~~

Physical pixels are deliberate: OverlayWindowGeometry uses monitor physical coordinates, and ClawHUD converts its configured font pixels through DPI rather than scaling the physical font size by 150%. **Do not multiply these heights by monitor DPI or XAML RasterizationScale.**

If settings have not arrived yet, use **offset 0**; if DesiredEnabled is not known yet, use **disabled/0px**. On the next ordinary authoritative snapshot, update the geometry. This must not become an IPC readiness gate.

## 3. Pure geometry change

Extend the existing Calculate() with a **small optional or explicit top-inset parameter** so existing zero-inset call sites and tests continue to describe the default full-height Overlay correctly. Clamp only to the monitor height; preserve its existing monitor selection and horizontal calculations.

Illustrative endpoint:

~~~csharp
internal static OverlayRect Calculate(
    int monitorLeft, int monitorTop, int monitorRight,
    int monitorBottom, uint dpi, int hudTopInsetPx = 0)
{
    // Keep the existing monitor width/height and X calculations.
    // ...
    var inset = Math.Clamp(hudTopInsetPx, 0, monitorHeight);
    return new OverlayRect(
        x, monitorTop + inset,
        Math.Max(0, monitorRight - x),
        monitorHeight - inset);
}
~~~

No work-area/taskbar adjustment and no other floating inset. Existing 1920x1200, 144 DPI **enabled=false** rectangle must stay (1194,0,726,1200). Enabled=true, HUD size 0 must become **(1194,44,726,1156)**, and size +2 **(1194,48,726,1152)**. For a monitor whose top is 40 and bottom 1240, add inset relative to 40, **not** relative to desktop (0,0). Tiny-height monitors clamp safely without a negative size.

## 4. Hook into existing Overlay settings state

Use the current OverlayClawHudMutationState, **not another saved state authority**:

~~~csharp
private int DesiredHudTopInsetPx() =>
    OverlayWindowGeometry.ClawHudTopInsetPx(
        _clawHudMutation.DisplayEnabled,
        _clawHudMutation.DisplaySettings?.HudSizeOffset ?? 0);
~~~

- During normal PrepareHidden()/ShowForPocAsync() placement, pass this inset into the existing Configure → Calculate path. Preserve the existing first-placement monitor selection, provisional full-monitor positioning for DPI acquisition, final topmost position, 180ms show / 140ms hide animation, rail, content, footer, and outside-click behavior.
- After changing the ClawHUD snapshot, applying a successful or failed mutation result, or beginning an accepted local Enabled/Size mutation, compare the **effective inset** with the last applied inset. If unchanged, do nothing.
- When the Overlay is **visible** and the effective inset changes, update the native window rectangle once, preserving the existing X/right/bottom policy. The update should apply on the current Overlay monitor without rerunning the **full-monitor provisional SetWindowPos**: temporarily expanding an already visible Overlay to fullscreen solely to reread DPI is unnecessary and risks a visual flash. A small WindowInterop method that gets the current monitor/DPI and applies the final rectangle with **one SetWindowPos** is sufficient. Keep the existing Configure() two-pass DPI behavior for initial show.
- Do not reset tab selection, rebuild XAML, re-run show/hide animations, hide the Overlay, or touch ClawHUD when updating this inset.
- Record only the one **last applied inset** needed to suppress redundant live SetWindowPos calls. Update that value **only after a successful placement**; do not introduce additional epochs, locks, retries, observers, cached rectangles, or lifecycle states.
- If the Overlay is **hidden**, do not reposition it just because a settings snapshot arrived; the next normal ConfigureWindow() will apply the latest effective inset. This also covers late initial snapshots without delaying Overlay startup.
- State publications or mutations unrelated to Enabled/Size must **not** cause an extra window placement.
- If live SetWindowPos fails, use the existing Overlay diagnostics/logging pattern, leave the last-applied value unchanged, keep the Overlay alive and otherwise functional. Do not block settings UI or ClawHUD mutation completion; no retry timer.
- Even when the ClawHUD runtime state is Starting, InGameOnly/hidden, Unavailable or StandaloneConflict, **DesiredEnabled** controls the inset. Do not gate on RuntimeState == Ready.
- If the mutation fails, the current OverlayClawHudMutationState rollback removes pending values; the effective inset must follow the resulting state without retaining a stale reserved band.

For absolute simplicity, compute the inset once per relevant state update (or from the state inside a small shared placement helper). Do not periodically poll an external window, query processes, or infer the physical HUD presentation height.

### Suggested implementation touch points

- OverlayWindowGeometry.cs: fixed inset mapping + optional inset passed to Calculate.
- WindowInterop.cs: forward initial inset through Configure; implement narrow live final-placement-only helper.
- OverlayWindow.Presentation.cs: read current DisplayEnabled/DisplaySettings, pass inset to Configure and coordinate the last applied inset during show/live update.
- OverlayWindow.ClawHud.cs: call the small reconciliation helper on the existing state and accepted-mutation paths (or from RenderClawHudControls), with a no-change guard.
- Tests as below.

Do not edit ClawHUD repository, Main App ClawHUD Settings tab, contracts, transport, Runtime, or the native rendering paths to implement this.

## 5. Automated test coverage (required for Codex / CI)

Use deterministic tests; physical handheld testing is not part of this PR.

1. **All five sizes:** desired enabled yields 40/42/44/46/48px; disabled yields 0px regardless of stored size.
2. **Default compatibility:** original Calculate() and 96/120/144/168/192 DPI horizontal anchors and bottom edges retain existing expected results when inset is zero.
3. **Reference enabled geometry:** size -2/0/+2 on the 1920x1200 monitor at 144 DPI: Y/Height change, X=1194, Width=726 and Bottom=1200 remain fixed.
4. **Monitor origin / clamp:** nonzero and negative monitor origins; positive/tiny height; zero height; no negative dimensions; no additional DPI multiplication.
5. **Settings timing:** initial snapshot absent is 0px; authoritative snapshot On with size value applies its inset, snapshot Off restores 0px. DisplayEnabled/DisplaySettings local preview is used, and failed mutation reverts it.
6. **Visible placement wiring:** only effective inset change invokes the live placement; it uses one final SetWindowPos (not the full-monitor provisional stage), does not reset the tab, and does not execute a new show animation. This may be covered by an existing focused source-wiring test if native HWND cannot be instantiated in unit tests.
7. **Unchanged architecture:** no new ClawHUD protocol commands, HWND introspection, new timer, ClawHUD presentation churn, or Controller/HidHide/VIIPER changes.

Test/build on a supported Windows developer or CI environment, adapting only paths/flags as required by the repo:

~~~powershell
dotnet build src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj -c Release -p:Platform=x64
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release -p:Platform=x64
git diff --check
~~~

Do not add a flaky timing or VRR simulation test. The user's real-device review **after merge** may check HUD overlap and game's VRR behavior but is not a requirement for local Codex implementation, CI PASS, PR approval, or merge.

## 6. Non-goals / acceptance checklist

- [ ] HUD Off: **0px** top reserved, irrespective of size; current full-height Overlay returns.
- [ ] HUD On: one of **five fixed physical heights** based only on size -2..+2; independent of actual HUD visibility, game presence, runtime status, background mode, font, and alignment.
- [ ] X and Width unchanged; right and bottom touch monitor boundary; Y moves down and Height shrinks by equal amount.
- [ ] Works on the next normal show and reacts to enabled/size changes while open, without an Overlay recreation or transient full-monitor live resize.
- [ ] Correct preview/authoritative result/rollback using existing OverlayClawHudMutationState; no new ClawHUD IPC.
- [ ] Existing Overlay rail/clock/tabs/scroll/footer/animations/input/hide behavior preserved.
- [ ] No changes to ClawHUD Presentation API, VRR ownership, or Full1902 controller authority.
- [ ] Focused geometry/state/wiring tests and Overlay build pass.
- [ ] Hardware validation is assigned to the user **after merge**; do not hold this PR for it.

One focused PR. Favor the smallest direct edit; **do not generalize this into an overlay collision framework**.
