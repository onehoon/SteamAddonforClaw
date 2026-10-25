# Work Order — Reserve the Quick Overlay Top Edge by ClawHUD Font and Size

**Date:** 2026-10-25  
**Status:** Ready for local Codex implementation — revised with user-measured HWND heights  
**Repository:** `onehoon/SteamAddonforClaw`  
**Implementation base:** Recheck current `main` before editing (initial review: `72eb9b5ee58134135f044ce53530ed05eae5228d`).  
**Deliverable:** One focused SteamAddon implementation PR  
**Implementation owner:** Local Codex  
**Real-device verification:** User's responsibility **after merge**; not a Codex, CI, or PR blocker.

---

## 0. Locked product decision

**Only the Addon's WinUI 3 Quick Overlay native window geometry changes.** The Overlay reserves the vertical pixel height of ClawHUD at the top of the monitor, derived **exclusively from the ClawHUD Enabled setting, Font, and HUD Size offset**.

1. **HUD DesiredEnabled = false:** reserve **0 physical pixels**, whatever Font and Size are stored.
2. **HUD DesiredEnabled = true:** reserve the **user-measured ClawHUD HWND height** for the selected Font (**Unispace** or **Segoe UI Variable**) and Size (**-2..+2**).
3. Do **not** inspect ClawHUD HWND/process existence, actual visibility, running games, the InGameOnly display mode, or RuntimeState for geometry admission. Even when the HUD itself is hidden or its runtime is unavailable, **DesiredEnabled + Font + Size** determine the inset.
4. For the existing right-edge Overlay, preserve **X and Width**. Move **Y** down by the inset and reduce **Height** by the same amount. Preserve the original bottom and right monitor edges.
5. Keep the Overlay responsive to accepted local Enabled/Font/Size mutations and to successful, failed, and authoritative settings state updates **while visible**, without reopening/rebuilding the Overlay.
6. Do **not** change or relocate ClawHUD, remove FullWidth/ContentWidth, change its presentation or rendering path, touch Intel VRR, introduce new IPC, or implement a general overlay-avoidance system.

Previous proposals for font-independent estimated **40–48px** insets, ClawHUD movement, ContentWidth-only mode, or runtime HWND interrogation are **superseded** by the exact measured table in §2.

---

## 1. Architecture and code review

This is the **standalone Full1902** product. Ignore obsolete CTW integration references.

Local Codex must review active architecture documents, as needed to preserve the established authority/lifecycle boundaries:

- `docs/Full 1902 Implementation/README.md` (document precedence);
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`;
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`;
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`;
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`;
- `docs/work-order/OVERLAY_EDGE_TO_EDGE_L_RAIL_CLOCK_Y_FOOTER_WORK_ORDER_2026-10-25.md` (current right/top/bottom edge semantics, not earlier floating-gap layouts);
- `docs/work-order/CH_A3_CLAWHUD_MAIN_UI_AND_OVERLAY_SETTINGS_WORK_ORDER_2026-09-22.md` (existing settings contract).

Current source facts verified against `main`:

| File | Current implementation relevant to this work |
| --- | --- |
| `src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs` | `OverlayWindowGeometry.Calculate(..., uint dpi)` preserves the legacy right-edge horizontal anchor and returns the full monitor top/height. |
| `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs` | `Configure()` chooses the target monitor, provisionally sizes HWND to the monitor for `GetDpiForWindow`, then uses `SetWindowPos` for final placement. |
| `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs` | `PrepareHidden()` and `ShowForPocAsync()` use `ConfigureWindow()`; show placement precedes visibility and animation. |
| `src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs` | `ApplyClawHudState()`, `CompleteClawHudMutation()`, `ApplyClawHudFailure()`, `RequestClawHudEnabled()`, and `RequestClawHudSetting()` already handle state and local preview. |
| `src/SteamInputAddonforClaw.Overlay/OverlayClawHudMutationState.cs` | `DisplayEnabled` previews `PendingEnabled` over `Snapshot.DesiredEnabled`. `DisplaySettings` already previews **both** `FrontendClawHudMutationKind.Font` and `HudSizeOffset`, and clears pending on completion/failure. Reuse this authority. |
| `src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs` | `FrontendClawHudSnapshot.DesiredEnabled`; `FrontendClawHudSettingsSnapshot.Font`, `.HudSizeOffset`; enum `FrontendClawHudFont.Unispace` / `.SegoeUiVariable`. No new DTO or wire field needed. |
| `src/SteamInputAddonforClaw.Overlay/App.xaml.cs` | The existing ClawHUD snapshot and mutation callbacks are already dispatched onto the Overlay UI thread. No new messaging. |
| `tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs` | Pure geometry regression coverage including 1920x1200 at multiple DPI, offset/negative monitors, small and zero-sized monitors. |
| `tests/SteamInputAddonforClaw.UiTests/OverlayClawHudMutationStateTests.cs` | Existing local preview, accepted mutation and rollback tests. |
| `tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs` | Source-level guards for the provisional monitor placement → DPI read → final placement order and Overlay show lifecycle. |

ClawHUD reference only: `onehoon/ClawHUD` at `integration/steamaddon`, `src/ClawHUD/HudSize.cpp`, `src/ClawHUD/HudPresentation.cpp`. The user's measurements below came from the **actual HUD HWND** on their device using `GetWindowRect()` at all five size settings for each font. Those measurements supersede size guesses from render metrics. Do not modify the ClawHUD repo.

---

## 2. Exact measured Font × Size heights (physical px)

The user measured:

| HUD Size | Unispace when enabled | Segoe UI Variable when enabled | Disabled, either font |
| ---: | ---: | ---: | ---: |
| -2 | **21px** | **23px** | **0px** |
| -1 | **22px** | **24px** | **0px** |
| 0 | **23px** | **25px** | **0px** |
| +1 | **24px** | **26px** | **0px** |
| +2 | **25px** | **27px** | **0px** |

This is exactly represented by a **base height + size offset** formula. Put it in **one pure function** in `OverlayWindowGeometry.cs` (or the existing narrow geometry owner). Do not create a second configuration authority.

~~~csharp
internal static int ClawHudTopInsetPx(
    bool desiredEnabled, FrontendClawHudFont font, int hudSizeOffset)
{
    if (!desiredEnabled)
        return 0;

    int baseHeightPx = font == FrontendClawHudFont.SegoeUiVariable ? 25 : 23;
    return baseHeightPx + Math.Clamp(hudSizeOffset, -2, 2);
}
~~~

**Required semantics:**

- Size 0: **23px for Unispace** and **25px for Segoe UI Variable**. Size ±1/±2 changes by exactly ±1/±2 physical pixels; Segoe is always 2px taller.
- **No speculative extra gap/padding**; use exactly the measured heights.
- The values are **physical pixels**: do not multiply by DPI or XAML `RasterizationScale`. The native geometry already operates on monitor physical coordinates.
- When there is **no snapshot yet**, `DesiredEnabled` is unknown and is treated as false → **0px**. If Enabled is true but nested Settings is temporarily null, use the simple fallback **Unispace, Size 0**; a later normal snapshot corrects the height.
- Do not query the real HUD HWND during Overlay operation. These are user-measured reference values intentionally treated as static policy constants; no promise of measurements for future fonts or untested configurations.

---

## 3. Change only the Overlay rectangle

Keep current X and Width logic, monitor selection, DPI handling, and right-edge alignment unchanged.

Extend `OverlayWindowGeometry.Calculate()` with one optional or required `topInsetPx` input. An optional `= 0` default allows existing full-monitor geometry tests/calls to retain their original meaning.

~~~csharp
internal static OverlayRect Calculate(
    int monitorLeft, int monitorTop, int monitorRight,
    int monitorBottom, uint dpi, int topInsetPx = 0)
{
    // Preserve the existing monitor width/height, DPI, and X calculations.
    // ...
    var inset = Math.Clamp(topInsetPx, 0, monitorHeight);
    return new OverlayRect(
        x,
        monitorTop + inset,
        Math.Max(0, monitorRight - x),
        monitorHeight - inset);
}
~~~

Examples for a 1920×1200 physical monitor at 144 DPI:

| Setting | X | Y | Width | Height | Bottom |
| --- | ---: | ---: | ---: | ---: | ---: |
| HUD Off | 1194 | 0 | 726 | 1200 | 1200 |
| Unispace, Size -2 | 1194 | 21 | 726 | 1179 | 1200 |
| Unispace, Size 0 | 1194 | 23 | 726 | 1177 | 1200 |
| Unispace, Size +2 | 1194 | 25 | 726 | 1175 | 1200 |
| Segoe UI Variable, Size -2 | 1194 | 23 | 726 | 1177 | 1200 |
| Segoe UI Variable, Size 0 | 1194 | 25 | 726 | 1175 | 1200 |
| Segoe UI Variable, Size +2 | 1194 | 27 | 726 | 1173 | 1200 |

For a monitor with nonzero or negative origin, the inset is relative to **monitorTop**, not desktop 0. Clamp to the actual monitor height; never produce negative sizes. Keep taskbar/`rcWork` out of the rectangle authority.

---

## 4. Integrate with existing ClawHUD settings state (no new IPC)

Read all inputs from the **existing** Overlay mutation state. Illustrative helper:

~~~csharp
private int DesiredHudTopInsetPx() =>
    OverlayWindowGeometry.ClawHudTopInsetPx(
        _clawHudMutation.DisplayEnabled,
        _clawHudMutation.DisplaySettings?.Font ?? FrontendClawHudFont.Unispace,
        _clawHudMutation.DisplaySettings?.HudSizeOffset ?? 0);
~~~

1. During the normal **initial hidden preparation and each show**, pass the effective inset through existing `ConfigureWindow()` → `WindowInterop.Configure()` → `OverlayWindowGeometry.Calculate()` before showing the Overlay. Preserve the existing two-pass provisional monitor placement for DPI **on initial configuration** and the current 180ms/140ms animations.
2. For a visible Overlay, react on the existing state and mutation paths to **Enabled, Font and Size changes**. `ApplyClawHudState`, `CompleteClawHudMutation`, `ApplyClawHudFailure` and accepted local Enabled/Setting mutations already exist. The shared `RenderClawHudControls()` can invoke a narrowly-scoped reconciliation method after the state update (or place equivalent calls at those paths).
3. Compare the **effective inset** with the last successfully applied inset; if unchanged, do not reposition. Opacity, alignment, background mode, VRR and unrelated snapshot publications must not trigger a redundant `SetWindowPos`.
4. When visible and changed, update the **current Overlay window** once with its new final rectangle, keeping its current monitor and horizontal placement. **Do not rerun provisional full-monitor positioning during a live change**, because that would temporarily cover the whole monitor and can visibly flash. A small `WindowInterop` final-placement-only path using **one** `SetWindowPos` suffices.
5. Do **not** hide/reopen/recreate the Overlay or ClawHUD; do not reset current tab/card, rebuild XAML, or restart animation to change the inset. When hidden, simply apply the latest state on the next normal configuration.
6. Retain only the one last-applied inset necessary for de-duplicating live geometry changes. Update that value after successful placement. Do not introduce additional locks, epochs, timers, observers, retries, state machines, cross-process probes, or long-lived managers.
7. **Preview and rollback:** `OverlayClawHudMutationState.DisplaySettings` already previews pending Font and Size, and `DisplayEnabled` already previews Enabled. On accepted local input, geometry follows the preview; on an authoritative result it follows the result; on mutation failure it reverts to the pre-mutation effective inset.
8. The reserve policy **does not depend on** `FrontendClawHudRuntimeState.Ready` or on actual HUD visibility. InGameOnly, Starting, Unavailable, StandaloneConflict, or a hidden window are not reasons to remove the inset while `DesiredEnabled` remains true.
9. If a live placement operation fails, log using the existing Overlay diagnostic pattern, retain the last successful inset, and leave settings/mutation completion functional. Do not start periodic retries or make failure to move the UI a controller/Runtime fault.

Expected implementation touch points:

- `OverlayWindowGeometry.cs`: formula + top-inset parameter / geometry adjustment.
- `WindowInterop.cs`: forward inset for initial Configure and one narrowly-scoped final-only live placement.
- `OverlayWindow.Presentation.cs`: extract current effective inset; apply on normal show; reconcile visible updates.
- `OverlayWindow.ClawHud.cs`: invoke reconciliation through existing state/preview/results, including **Font** changes.
- Targeted existing UiTests.

No ClawHUD repo, Runtime, Main App settings tab, frontend contract, transport, controller ownership, HidHide, VIIPER, or VRR code changes.

---

## 5. Deterministic tests (required for local Codex / CI)

Use existing tests and test conventions; add only focused cases:

1. **All ten measured enabled combinations:** Unispace (-2..+2) → 21/22/23/24/25px; SegoeUiVariable (-2..+2) → 23/24/25/26/27px.
2. **All ten disabled combinations:** return exactly 0px for both fonts/all size choices.
3. **Geometry regression:** with inset 0, preserve all existing 96/120/144/168/192 DPI horizontal anchors, Y, full monitor Height, right edge and bottom.
4. **Enabled reference examples:** both fonts with sizes -2/0/+2 on 1920×1200/144 DPI yield the table in §3, preserving X=1194 and Width=726.
5. **Monitor boundaries:** nonzero and negative monitor origins; small/zero monitor height clamp; no negative sizes; no accidental DPI multiplication.
6. **State and preview:** absent first snapshot is 0; authoritative Enable and Size/Font snapshots set the correct inset; authoritative Off restores 0; local Enable/Size/**Font** previews work before mutation result; failures revert it. In particular, Unispace → Segoe at unchanged Size 0 changes 23px → 25px without reopening the Overlay.
7. **Live placement wiring:** only a changed effective inset causes a final-only `SetWindowPos`; no intermediate fullscreen reposition, tab reset, show animation, new IPC, native HUD inspection, or renderer reinitialization. If HWND integration cannot run in unit tests, focused source-wiring tests are acceptable.
8. **Existing Overlay contracts:** the show/hide, outside-click, rail/clock/tab/footer and initial DPI/provisional-placement tests remain valid.

Run on supported local Windows CI/developer environment:

~~~powershell
dotnet build src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj -c Release -p:Platform=x64
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release -p:Platform=x64
git diff --check
~~~

Do not add theoretical timing races, speculative synchronization, physical-hardware gates, or flaky VRR simulation. **Real handheld testing is performed by the user after merge, and its absence must not block PR review/merge if the code and automated tests are sound.**

---

## 6. Acceptance checklist

- [ ] ClawHUD Enabled **Off** → 0px reserved, both fonts and all sizes.
- [ ] Enabled **On** → exact user-measured Font × Size inset from §2; **no estimated 40–48px allowance** and no extra margin.
- [ ] Real HUD visibility/game/runtime state does not influence reserved height.
- [ ] X/Width preserved; Y increases and Height decreases by equal pixels, with bottom/right edges unchanged.
- [ ] Live **Enabled, Font and Size** changes, preview, completion and rollback work without recreating a window or re-running full-monitor provisional placement.
- [ ] The existing Overlay shell/controls/animations and source-level lifetime contracts remain intact.
- [ ] No ClawHUD Presentation API, GPU resources, VRR policy, Full1902 physical controller authority, or new IPC is touched.
- [ ] Focused automated geometry, state and wiring tests plus Overlay Release build pass.
- [ ] User retains post-merge real-device verification responsibility; no hardware test PR blocker.

**One focused PR.** Solve this with the smallest direct edits; do not construct a generic collision-avoidance architecture.
