# Work Order — MSI Update Page: Compact Catalog UI, Single Scroll and Fresh-Entry Query

**Date:** 2026-10-18
**Repository:** onehoon/SteamAddonforClaw
**Target:** Current `main`; re-read HEAD immediately before implementation
**Product:** Standalone Full PID1902 MSI Claw Addon; one Windows administrator user / one interactive session
**Owner:** Local Codex
**Delivery:** One focused PR (commits may separate XAML/card layout, navigation/query state, and UI tests/resources)
**Verification:** Normal build/automated UI tests are Codex's responsibility. Hardware/on-device visual verification belongs to the product owner **after merge**; it is neither a work-order requirement nor a PR review blocker.

## 1. Goal — locked user decisions

Polish the already-implemented, **separate top-level MSI Update page**, without changing catalog retrieval. The design is based on the four screenshots captured in Google Drive / Addon / MSI Update on 2026-10-18.

1. Keep the existing separate **MSI Update** top-level navigation page, native WinUI 3 **TabView** and the four categories: BIOS / Firmware / Drivers / Utilities.
2. Place **Check for updates** on the **right of the main MSI Update page title**, within the same header row. Keep a modest progress indicator/status message, but **remove the Last checked display entirely**.
3. Center the tab titles, enforce **equal-width four tabs**, and dynamically append actual category catalog counts **only after a successful lookup**: e.g. BIOS (2), Firmware (1), Drivers (21), Utilities (7). Counts are catalog records, **not installed or applicable updates**. A successful empty category may show (0), but a failed, missing or unqueried category **must not show (0)**.
4. Add breathing room: **about 8 DIPs vertical padding within tab headers**, **16 DIPs between tab header and first card**, **8 DIPs between cards**, **12 DIPs vertical padding within cards**, and **16 DIPs after the last card**. These are WinUI DIPs and must adapt naturally to DPI/text scale.
5. Make all category content occupy **the same available horizontal width**, regardless of name lengths, item count or active tab. Tab selection must not shift the left/right edges of cards or the tab strip.
6. **One vertical scroll surface per MSI Update page**, owned by the outer page ScrollViewer. Remove the four TabViewItem-local ScrollViewers and restrictive `TabView.MaxHeight=620` / `ScrollViewer.MaxHeight=560` responsible for nested scrolling. The 21 drivers should scroll through the **page**, not an inner scroll area. No clipping.
7. Remove the large enclosing **SettingsCard** that nests the catalog inside another card. Keep theme-aware **individual result cards** with the app's normal background/border brush.
8. Each entry is **compact, normally two lines**:
   - Line 1, left: component name (semi-bold, wrap safely for long names).
   - Line 2, left: **Latest published version: X · Released: YYYY-MM-DD** (localized labels; single line when space allows, wrapping naturally at narrower widths).
   - No entry description, multi-line instructions, HTML markup or raw installer text. Remove the now-dead `FormatMsiCatalogDescription` helper and its unused references/tests as appropriate.
   - Use a simple `Grid` with **left `*` / right `Auto`** so a **future per-entry Update button** can be placed on the right later. **Do not create the button, inactive placeholder or extra installation logic in this PR.**
9. **No automatic querying at any lifecycle point.** On **every new navigation into MSI Update from another top-level page**, reset the page to **Not checked yet**, clear displayed cards/counts/status from the previous visit, and require a fresh manual Check click. Within an uninterrupted visit, switching BIOS/Firmware/Drivers/Utilities tabs must **reuse the current visit's query result** without another request.
10. When the user leaves MSI Update **during an in-flight lookup**, cancel the request if feasible through the existing frontend cancellation token and **do not present its stale result on a subsequent visit**. This is ordinary navigation behavior and needs only a **page-local request CancellationTokenSource + identity check** or an equivalently simple bounded mechanism. Do not add a general epoch/state manager.
11. **No cache or last-checked persistence**, in memory across visits or on disk. A second Check click within the same visit is allowed and must perform a fresh request, rather than reusing the previous response; clear the previous result while that request is pending.
12. Keep the current catalog-only disclaimer in a **short, subdued line**, not a large card. Do not label results “Update available”/“Up to date”: installed versions and applicability remain out of scope.

## 2. Mandatory source documents and implementation boundaries

Re-read:

- `docs/Full 1902 Implementation/README.md` (document precedence)
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/work-order/2026-10-18_MSI_LIVE_UPDATE_CATALOG_READ_ONLY_DEVICE_UI_WORK_ORDER.md` (historical original catalog contract; **this work order supersedes its earlier Device-expander placement**)
- `docs/work-order/2026-10-14_MAIN_APP_DEVICE_TAB_RESW_PR2_WORK_ORDER.md` (RESW conventions; respect the existing localization implementation)
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md` for background only; later Main App navigation/code and this new MSI Update placement take precedence

**Screenshot evidence** (user-supplied Google Drive / Addon / MSI Update; access where permitted):

- BIOS: https://drive.google.com/file/d/1aFcrcvDzIimjCsFu-gJe03pbUV4qw_AT/view
- Firmware: https://drive.google.com/file/d/1uOmlua55J0RB1nPbE81gujjKuJJ8F12L/view
- Drivers / double-scroll: https://drive.google.com/file/d/1CeCbl-R_AcQkoxgVZFNXRB7sM9fmY78h/view
- Utilities / double-scroll: https://drive.google.com/file/d/1z3PWxYUA-XrRWeLnaGzAMJDXQCLXm7Qr/view

The screenshot model/data are visual evidence only. Do not hard-code counts, model versions, timestamps or driver names from them.

**Absolutely unchanged:** backend MSI catalog request, original OEM DLL and x86 helper invocation, elevated Runtime, frontend RPC/protocol/contracts, product selection, raw response parsing, Center M startup suppression, controller/HidHide/VIIPER, PID1901/PID1902, routing and any package/install behavior. This is **UI-only** apart from using the existing frontend cancellation already supported.

## 3. Verified code baseline (current `main` at drafting)

- `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml`
  - currently contains the **outer ScrollViewer Padding=32**, an inner **SettingsCard**, a left-aligned Check button in a Grid, `MsiUpdatesLastCheckedText`, `TabView MaxHeight=620`, four `TabViewItem` children each wrapping a StackPanel in an **inner ScrollViewer MaxHeight=560**.
  - fixed tabs already use `IsAddTabButtonVisible=False`, `CanReorderTabs=False`, `CanDragTabs=False`; preserve these.
  - `MsiCatalogEntryCardStyle` already uses `ThemeResource CardBackgroundFillColorDefaultBrush` and `CardStrokeColorDefaultBrush`; reuse/adapt rather than inventing a new visual system.
- `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml.cs`
  - owns `_msiUpdatesBusy` and `_msiUpdatesSnapshot`; the click handler currently calls `_frontend.RequestMsiLiveUpdateCatalogAsync()` with no token, and `RenderMsiLiveUpdateCatalog()` renders `MsiUpdatesLastCheckedText`.
  - `RenderMsiLiveUpdateCategory` constructs a text StackPanel with name, version, release date and description, using `FormatMsiCatalogDescription`.
- `src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs`
  - `GetOrCreateMsiUpdatesPage()` caches a **single instance** in `_msiUpdatesContent`.
  - `ShowPage(MainNavigationPage page)` materializes pages, changes `MsiUpdatesPageHost.Visibility`, and updates `_displayedPage`. It currently has no MSI-page Activate/Deactivate lifecycle calls.
  - use this **existing navigation ownership** to invoke simple `MsiUpdatesPage.Activate()` / `Deactivate()` at genuine page transitions. Do **not** recreate/rebuild the page on every navigation or change `MainNavigationState` semantics.
- `src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw` and `ko-KR/Resources.resw`, `MainUiStrings` handle localized text. Existing `MsiUpdates_*Tab.Header` XAML resource keys must not be silently bypassed when constructing dynamic labels.
- `tests/SteamInputAddonforClaw.UiTests/MsiLiveUpdateCatalogUiTests.cs` already tests manual Check, four fixed tabs, RESW parity, description rendering, theme-aware cards. Update tests to reflect the new user-approved UI; remove/replace the obsolete description-formatting test.
- `tests/SteamInputAddonforClaw.UiTests/MainNavigationStateTests.cs` and `UiArchitectureTests.cs` already cover top-level navigation; augment narrowly for genuine re-entry.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs` already receives a CancellationToken and supports helper cancellation. **Do not alter it for this UI task.**

## 4. Target layout

**After a successful Check (example counts only):**

~~~text
┌──────────────────────────────────────────────────────────────────────┐
│ MSI Update                                       [Check for updates]  │
│ MSI catalog only · installed versions not checked                    │
│ [short status/progress only when useful]                              │
│                                                                      │
│  [    BIOS (2)    ][ Firmware (1) ][ Drivers (21) ][ Utilities (7) ] │
│                                                                      │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ Intel Graphics Driver                                            │ │
│ │ Latest 32.0.101.xxxx  ·  Released 2026-10-18                     │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ Goodix FingerPrinter Driver                                      │ │
│ │ Latest 1.0.0.1  ·  Released 2026-03-18                           │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│                         (page scroll continues)                      │
└──────────────────────────────────────────────────────────────────────┘
~~~

The names/versions above illustrate placement; actual UI must use the server result. There must be **one consistent horizontal content edge** across all four tabs, no inner scrollbar and no hard-coded per-tab panel width.

**New visit / before Check:**

~~~text
MSI Update                                         [Check for updates]
MSI catalog only · installed versions not checked

[ BIOS ][ Firmware ][ Drivers ][ Utilities ]

                  Select Check for updates to load MSI catalog entries.
~~~

No Last checked label or prior category counts/cards appear after re-entry.

### Layout implementation constraints

1. Use a simple title/header `Grid` with `ColumnDefinitions="*,Auto"`, heading in first column, Check button in second column. Put the progress indicator alongside the button or in a compact status row, without shifting title alignment. Avoid fixed pixel widths.
2. Remove the outer `SettingsCard` that wraps the whole list; retain the **one outer page ScrollViewer** and consistent page padding. Remove the four local ScrollViewers and all `MaxHeight=620/560` constraints from the tab/list. `TabView`, its content presenter, category panels and cards must stretch horizontally to the parent available width.
3. Use `TabWidthMode="Equal"` for `TabView`. Center the text in each tab header (use the native `TabViewItem.HeaderTemplate` or a small header TextBlock with `HorizontalAlignment=Center` and `TextAlignment=Center` as appropriate). Do not change to custom tabs or a second navigation control. Native selected-tab appearance must remain.
4. Keep the tab **click target comfortable**: ~8 DIP top/bottom around header text; keep 16 DIP between the tab strip and first card and 16 DIP bottom padding inside content; ~8 DIP card spacing. Prefer a single shared style/template for the four tab headers rather than four subtly different templates.
5. Card layout: one `Grid` `ColumnDefinitions="*,Auto"`; currently the right Auto column has **no button/placeholder**. A left `StackPanel` (Spacing ~4) shows Name and one `TextBlock` composed from optional latest version and optional released date, with a dot separator only when both exist. `FontWeight=SemiBold` on name; secondary style/opacity on metadata. Vertical card padding ~12 DIP. Long text wraps without horizontal page overflow. Trim time-of-day from a recognizable ISO release date, without inventing a date when parse fails.
6. If a category has no entries, show one modest empty-state text aligned to the same content padding, not a full-width second SettingsCard.
7. The **description field can stay in the existing transport contract** untouched, but the page must no longer render it or turn its HTML into visible text. Do not alter backend parsing/normalization. Remove the page-only `FormatMsiCatalogDescription` implementation if now dead.
8. Preserve UI error/busy semantics and localized English/Korean resources. Remove unused `MsiUpdates_LastChecked` UI resource key/test if no other caller needs it; **do not delete/modify `CheckedAtUtc`** from backend/front-end data contracts merely because the UI no longer displays it.
9. Tab counts are derived from `FrontendMsiLiveUpdateCatalogCategorySnapshot.Entries.Count` only when category state is **Success** or **Empty**. For failure/unknown/unqueried, present the localized bare tab name. Do not use screenshot's 2/1/21/7 as default labels. Ensure runtime-composed labels use correctly localized base strings (RESW), not raw English `ToString()` or a hard-coded Korean mapping.
10. The *title* and *Check* button are always in the header and visible regardless of selected category. Switching inner tabs does not invoke the backend.

## 5. Navigation/session semantics — minimum necessary lifecycle

The current single cached `MsiUpdatesPage` instance can be retained; **only its result/presentation state resets each time the user re-enters from a different main page**.

Do not reset when:
- BIOS/Firmware/Drivers/Utilities tab selection changes;
- the MainWindow gets focus again;
- frontend `StateInvalidated` fires;
- ordinary system status refresh runs;
- `ShowPage(MsiUpdates)` is called while MSI Update is already displayed (same-page reselection).

Do reset when:
- opening MSI Update after Device/Controller/Steam/XBOX/other top-level page;
- leaving MSI Update and later re-entering in the same app lifetime;
- the UI app restarts (normal initial state).

**Implementation sketch (adapt to current code; not a demand for a new architecture):**

~~~csharp
// In MainWindow.ShowPage, where page visibility transitions are already known:
bool wasMsiUpdates = MsiUpdatesPageHost.Visibility == Visibility.Visible;
// ... existing materialization/visibility updates ...
if (page == MainNavigationPage.MsiUpdates && !wasMsiUpdates)
    _msiUpdatesContent?.Activate(); // reset: no fetch
else if (wasMsiUpdates && page != MainNavigationPage.MsiUpdates)
    _msiUpdatesContent?.Deactivate(); // cancel pending UI-request
~~~

`MsiUpdatesPage.Activate()`: reset `_msiUpdatesSnapshot` to NotChecked and an empty categories array, `_msiUpdatesBusy = false`, selected tab to BIOS (index 0), status/guidance, and base headers with **no counts**; render exactly once. No backend call.

`MsiUpdatesPage.Deactivate()`: cancel/invalidate the current page-local request; do not mutate Runtime/session/controller state.

`MsiUpdatesCheckButton_Click`:
- acquire a page-local `CancellationTokenSource`; while active, disable the Check button, show progress, and clear older displayed results/counts;
- pass its token to existing `_frontend.RequestMsiLiveUpdateCatalogAsync(token)`;
- when it completes, apply the result **only if this is still the same active request and the page is still on the same visit**; otherwise discard it;
- cancellation caused by navigation should not be shown as an error, should not be logged as an unhandled exception and should not leave the Check button permanently disabled;
- use the existing busy flag plus one CTS/reference identity check (or equivalent) as needed. **No new owner/manager, epoch framework, gate or background worker.**
- let the existing frontend/runtime code deal with subprocess/helper cancellation; do not add Helper code or new IPC.

This is important for a normal user sequence: **Click Check → immediately switch to Device → return to MSI Update**. The new visit must show **Not checked yet**, with no stale cards or counts and a working Check button. It must not silently reuse the prior run's result.

## 6. Deterministic test expectations (no physical Claw required)

Update/add targeted UI tests to verify all of the following:

1. **Layout shape:** exactly one outer ScrollViewer in the MSI Update page; no ScrollViewer nested inside TabViewItem; no old `MaxHeight` constraints; no wrapping SettingsCard; equal-width fixed `TabView`; center-aligned headers and reasonable padding/spacing.
2. **Header:** MSI Update title and Check button occupy the same two-column header; no `MsiUpdatesLastCheckedText`, no last-checked rendering logic, no last-checked visible resource.
3. **Card:** name and compact one-line metadata label (wrapping allowed); omitted description and HTML helper; theme-aware card retained; future right Auto column available without fake button; long names/versions do not impose fixed horizontal overflow.
4. **Counts:** after valid Success and Empty categories show localized `(N)` based on actual entries; before query, on failed category, and after navigation reset, show localized names without a number; never show failed category as `(0)`. Keep tab order/selection.
5. **Fresh-entry behavior:** MSI page is cached but `Activate` runs only on **genuine navigation in**, not tab switch or reselect; no query on activation or tab selection; leaving/returning clears previously displayed cards and counts.
6. **In-flight navigation:** cancel/discard an in-progress result when leaving; quickly returning starts a clean visit with an enabled button, and any old response cannot overwrite the new result; cancellation is not displayed as an error.
7. **Manual refresh:** Check always performs a new RPC on each click; state clears while checking and becomes Completed, PartialFailure, Unavailable, or Failed as appropriate.
8. **Locale parity:** all new/renamed dynamic labels in `en-US` and `ko-KR` RESW; existing base labels and fallbacks remain coherent; no app-level localization redesign.
9. **Regression:** Device/Controller/Steam/XBOX and the existing navigation/status refresh behavior remain unchanged; MSI catalog query IPC tests remain valid.
10. **Build/publish:** WinUI XAML, resources and UI tests pass. No required hardware testing or MSI Center M installation in CI.

Prefer extending `MsiLiveUpdateCatalogUiTests.cs` and existing main navigation tests. Existing source-contract tests may need updates after markup changes; keep meaningful behavioral assertions, not stale literal structure requirements.

## 7. Out of scope / anti-overengineering

- Do not modify **MSI Center M SDK discovery**, helper elevation, OEM `API_LiveUpdate.dll` reflection, network POST/signing, response parser, requests/identity, frontend RPC/wire, error taxonomy or query concurrency in Runtime.
- Do not add local installed version checks, applicability matching, installer/download buttons, driver/BIOS flash logic, file downloads, automatic or periodic checks, persistent storage, caches, timers, update scheduling, Windows notifications or badges.
- Do not relocate MSI Update again or change the unrelated Device page.
- Do not alter controller authority, Sleep/Hibernate/Resume routing, recovery, HidHide, VIIPER, PID1901/1902, TDP, fan, Overlay or CTW (standalone application).
- Do not invent reusable styling engines, additional view models/controllers, another navigation authority or synchronization primitives. The existing page-local code-behind plus two lifecycle hooks is the intended scope.
- A hypothetical instruction-level navigation/callback interleaving is **not** a merge blocker. A reproducible ordinary **navigate-away-during-check** stale-result problem **is** within scope.

## 8. Acceptance / PR delivery

The PR is ready on code and automated tests when:

- the MSI Update page displays a clean title row with a right-aligned Check button, four centered equal-width category tabs, and the correct dynamic category counts only after a scan;
- switching tabs never changes list width and **only the entire page scrolls** (including a full list of ~21 driver cards);
- individual result cards are compact (name + version/date metadata, no descriptions), theme aware and have a simple future Auto action column;
- no Last checked text is visible or reconstructed;
- navigating away and back always returns **Not checked yet**, with no stale data or auto-query; switching categories within the current visit retains the current result;
- in-flight navigation is handled without stale results/stranded busy UI;
- the original OEM helper/server request path, Full1902 Runtime and all unrelated pages remain unchanged;
- no physical-device validation is required of Local Codex or as a PR merge gate; the product owner will validate screenshots/on-device appearance **after merge**.

**Reminder:** This work order supersedes the historical original catalog work order only with respect to **the UI and navigation lifecycle**. It does not reopen that work order's OEM request or security contracts.
