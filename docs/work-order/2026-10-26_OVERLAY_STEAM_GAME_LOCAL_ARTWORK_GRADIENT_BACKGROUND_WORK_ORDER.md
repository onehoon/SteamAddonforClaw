# Work Order — Overlay Profile: Local Steam Game Artwork as a Non-Intrusive Gradient Background

> **Date:** 2026-10-26  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Target:** standalone Full1902, WinUI 3 Overlay, Profile tab **only**  
> **Owner:** local Codex for implementation and automated tests; user for optional physical-device validation after merge  
> **Implementation size:** one focused PR; no dependency on CTW  
> **Approved visual reference:** [Figma — Overlay Profile cover gradient](https://www.figma.com/design/e2NBusYYFGebJZ0bqQBm83?node-id=3-61)  
> **Product decision:** reuse on-disk Steam artwork only; missing artwork is a completely silent no-op.

## 1. Goal and fixed product behavior

When **an actual, native Steam game is currently running** and the Overlay **Profile** tab shows its authoritative active-game profile, render that game's **already cached local, horizontal Steam artwork** as a **background behind the existing Profile page content**.

The image:

- begins at the **very top edge of the right content column**, not after a new banner/card/header;
- spans the **entire right content-column width** (the referenced QAM shell has 432 DIP overall / 52 DIP left rail / 380 DIP content column; use actual layout width rather than hardcoding a viewport);
- occupies approximately **220–250 DIP vertically**, cropped with `UniformToFill` as needed;
- is progressively covered by a **top-to-bottom gradient into the existing `#0E141B` QAM surface**, fully disappearing before the ordinary settings area;
- is behind all existing title, game-name, toggle, and settings controls. Maintain readability through a dark overlay/gradient as shown in Figma;
- **does not move, resize, replace, or duplicate** the page title, game name, Profile toggle, controls, dividers, ScrollViewer, or footer.

**Do not** introduce a foreground cover card, an image section, new text or label, a new row, extra top margin, scrolling height, tab-width change, left-rail background change, or a differently positioned title. This is **background artwork only**.

**Eligibility is deliberately strict:**

| Authoritative active Profile context | Artwork |
| --- | --- |
| Running **native Steam** game with verified Steam game identity and a usable local image | Show gradient background |
| Running native Steam game but no usable local image | Original unchanged Profile UI |
| Steam client running, but **no game** active | No artwork |
| Steam **non-Steam shortcut** (even if Steam reports a numeric `RunningAppID`) | No artwork |
| Running Xbox/Game Pass game | No artwork |
| Profile snapshot is loading, unavailable, invalid, or stale | No artwork |
| Another Overlay tab is selected | No artwork |

A native Steam game is **not** the same as `QuickSettingsProfileTargetKind.Steam`: current Steam game-identity plumbing also accepts non-Steam shortcuts. See section 4. Do not classify by `AppId > 0` alone.

This requirement is about an **actively running game in the Overlay**, not the Main App's Steam installed-game list, the inactive/offline profile editor, Steam BPM merely being open, or generic Overlay page wallpaper.

## 2. Architecture and existing-code inspection

Review and preserve the authority chain defined by:

- `docs/Full 1902 Implementation/README.md`;
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`;
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`;
- `docs/work-order/PR11_XBOX_OVERLAY_ACTIVE_GAME_PROFILE_AND_PERFORMANCE_DISPLAY_PARITY_WORK_ORDER_2026-10-07.md`;
- `docs/work-order/2026-10-10_OVERLAY_PROFILE_NO_RUNNING_GAME_CONDITIONAL_RECHECK_WORK_ORDER.md`.

Verified current-main facts:

1. `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml` owns the common QAM shell. It has a **52-DIP left rail**, and the right column contains **`PageTitle` in row 0** and **`BodyScroll` in row 1**. `TabBody` is nested inside `BodyScroll` with `QamBodyContentPadding`. The 36-DIP `QuickJumpFooter` is a separate shell row. This is why inserting an image into the Profile StackPanel would **incorrectly push the game name/settings down and miss the area behind the page title**.
2. `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs` owns `BuildProfilePage()`, `PrepareActiveProfileFirstShow()`, `ApplyActiveProfilePage()`, `ApplyProfilePresentation()`, and tab-entry handling. The Profile page already alternates between an active detail and a no-game/unavailable message.
3. `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs` owns `ApplySelectedTabVisualState()` and current selected-tab state. It already resets `BodyScroll` on tab changes.
4. `src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs` already defines `QuickSettingsPageSnapshot`, `QuickSettingsProfileTargetKind.Steam/Xbox`, and `QuickSettingsProfileTarget.SteamAppId`.
5. `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs` determines the target using `_runtimeHost.ActualRunningAppId` first, then `_xboxGameSessionRuntime.ActiveGame`, then publishes the Profile Quick Settings page; it produces the existing exact no-game empty state when neither exists. Normal running-game changes already refresh the Overlay.
6. `src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs` builds `ProfileGeneral` with the active game's display name and Profile Enabled row. Keep this generic projection unchanged.
7. `src/SteamInputAddonforClaw/Profiles/ProfileGameCatalog.cs` already demonstrates Steam install-root precedence and configured Steam-library manifest discovery, and explicitly distinguishes `ProfileGameSource.Steam` from `ProfileGameSource.NonSteam` (the latter comes from `userdata/*/config/shortcuts.vdf`). Do not incorrectly infer native Steam identity from the Overlay target alone.
8. `src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj` references Contracts and FrontendTransport, **not** the Runtime assembly. Do not make the Overlay depend on the full Runtime simply to look up artwork.
9. `src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml` defines `QamSurfaceBrush = #FF0E141B`, `QamPageTitleMargin = 16,20,16,16`, and `QamBodyContentPadding = 16,0,16,12`. Preserve these coordinates and the existing text styles.

## 3. Visual integration: background in the right shell column, not in the Profile StackPanel

**Preferred small change:** in `OverlayWindow.xaml`, within the existing right-column Grid (`Grid.Column="1" Grid.Row="0"`), add one **non-interactive, top-aligned artwork layer as the FIRST visual child**. Span the existing title + body rows (`Grid.RowSpan="2"`) and place it **behind** both `PageTitle` and `BodyScroll`.

- Give the artwork host an `x:Name` owned by `OverlayWindow`; start collapsed.
- Host one `Image` with `Stretch="UniformToFill"`, clipped to a bounded height, and one `Rectangle`/overlay with a vertical `LinearGradientBrush` leading to opaque `#FF0E141B`.
- Optionally combine the fade with a subtle top tint to protect white title/game text, with the Figma screenshot as the visual authority.
- The artwork host must stretch to the **right-column width**; use `HorizontalAlignment="Stretch"`, not the inner 16-DIP padded `TabBody` width. No hardcoded 380-DIP `Width` attribute.
- Set `IsHitTestVisible="False"`, prevent focus/tab interaction, and keep the background behind buttons, pointers, gamepad selection highlights, controls, title, scroll region, and left rail.
- Prefer a fixed-height backdrop anchored at the top of the right shell column: the settings scroll *over* it. Do not insert artwork inside `BodyScroll` or change its content extent.
- On a short viewport, clip the artwork rather than expanding the Overlay. Keep the existing QAM geometry, header margins, body spacing, and QuickJumpFooter untouched.
- Use existing page resources/colors; localize no new user-facing text. No Blur effect is needed; this is **gradient fade into the surface**, not image Gaussian blur.

**Conceptual XAML only — adapt to the actual Grid structure, names and code-behind; do not paste blindly:**

```xml
<Grid Grid.Row="0"
      Grid.RowSpan="2"
      VerticalAlignment="Top"
      HorizontalAlignment="Stretch"
      Height="240"
      IsHitTestVisible="False"
      Visibility="Collapsed">
    <Grid.Clip>
        <RectangleGeometry Rect="0,0,380,240"/>
        <!-- Use actual arranged content width in production; do not fix to 380 -->
    </Grid.Clip>
    <Image Stretch="UniformToFill" />
    <Rectangle>
        <Rectangle.Fill>
            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                <GradientStop Color="#330E141B" Offset="0"/>
                <GradientStop Color="#900E141B" Offset="0.50"/>
                <GradientStop Color="#FF0E141B" Offset="1"/>
            </LinearGradientBrush>
        </Rectangle.Fill>
    </Rectangle>
</Grid>
<!-- The EXISTING PageTitle and BodyScroll remain at their existing grid rows. -->
```

The example's explicit clip rectangle is only a diagram: implement clipping using WinUI layout rather than a fixed 380-DIP width. A bounded `Grid`/container whose content is clipped is preferable.

## 4. Eligibility: native running Steam games only, no new game-identity authority

**Source of active game identity is the existing authoritative `QuickSettingsPageSnapshot`.** Do not add a process scanner, Steam polling timer, game detection service, another active-game model, or an extra IPC transport field solely for artwork.

Before resolving or showing a background, require:

1. `page.PageId == QuickSettingsPageId.Profile`;
2. `page.Available == true`;
3. `page.ProfileTarget is { IsStructurallyValid: true, Kind: QuickSettingsProfileTargetKind.Steam, SteamAppId: > 0 }`;
4. the Profile tab is selected (or prepare its artwork when a valid page arrives and only reveal on selection); and
5. a **narrow, read-only native Steam installation proof for that exact AppID**, rather than assuming all `Kind.Steam` targets are native games.

For requirement 5, inspect the current `ProfileGameCatalogScanner` convention: Steam-installed games are recorded by `steamapps/appmanifest_<AppId>.acf` in the primary Steam installation or in the libraries referenced by `steamapps/libraryfolders.vdf`, whereas `shortcuts.vdf` items are `NonSteam`. Check for the **exact AppID's** native Steam app manifest, including configured external Steam libraries. A targeted manifest lookup is sufficient; do not run the entire installed-game catalog scanner for each Overlay publication. Use a small, testable resolver and straightforward read-only file checks. Do not duplicate the entire catalog VDF parser or introduce a separate catalog authority.

If native identity cannot be established because the manifest/libraries are missing, invalid or inaccessible, quietly leave the background absent, even if a matching custom image is present. This is an aesthetic feature and should fail closed without impacting the Profile page.

**Do not** gate Steam controller presentation, TDP, Quick Settings mutation, or anything else on artwork eligibility. A missing image/manifest must never change profile editing or device ownership.

## 5. Local artwork discovery: inspect both requested Steam folders

Resolve the Steam **installation root** with the same effective precedence already used in `ProfileGameCatalogScanner.LocateSteamRoot`: `HKCU\Software\Valve\Steam\SteamPath`, then HKCU `InstallPath`, HKLM `Software\WOW6432Node\Valve\Steam\InstallPath`, then `Program Files (x86)\Steam`; require a credible `steam.exe` at the candidate root. Steam game libraries may reside on other drives; **artwork cache and userdata still belong to the primary Steam installation**, not the game installation directory.

Inspect **both** of these local-only roots:

### 5.1 User-selected Steam artwork (higher priority)

```text
<SteamRoot>\userdata\<active Steam account id>\config\grid\
```

Preferred candidate order, for the exact decimal AppID:

1. `<AppId>_hero.png`, `<AppId>_hero.jpg`, `<AppId>_hero.jpeg`;
2. `<AppId>.png`, `<AppId>.jpg`, `<AppId>.jpeg` — custom **wide/landscape grid** fallback.

Use a deterministic extension preference, but never substitute the **portrait** `<AppId>p.*`, logo or icon as horizontal wallpaper. Never treat `<AppId>_logo.*` as a hero.

A machine can contain userdata folders from several Steam accounts. Resolve the currently active Steam account if possible (e.g. the positive/current `ActiveProcess\ActiveUser` Steam account ID, or the `MostRecent=1` account in `<SteamRoot>\config\loginusers.vdf` mapped from SteamID64 to the numeric userdata account ID). If neither is trustworthy and exactly one numeric userdata account directory exists, it may be used. **Do not arbitrarily mix artwork from another logged-out account** or introduce multi-session/account machinery beyond this small local choice. When account selection is unknown, skip custom art and still try the built-in Steam cache.

Do not change any Steam files or account preferences.

### 5.2 Steam's downloaded built-in library cache (fallback)

```text
<SteamRoot>\appcache\librarycache\
```

For the exact native AppID, support the practical cached filename/layout variants:

```text
librarycache\<AppId>\library_hero.jpg
librarycache\<AppId>\library_header.jpg
librarycache\<AppId>\header.jpg                 (if present)

librarycache\<AppId>\<content-hash>\library_hero.jpg
librarycache\<AppId>\<content-hash>\library_header.jpg
librarycache\<AppId>\<content-hash>\header.jpg (if present)

librarycache\<AppId>_library_hero.jpg           (legacy flat layout, if present)
librarycache\<AppId>_library_header.jpg         (legacy flat layout, if present)
```

Prefer built-in `library_hero.jpg` to `library_header.jpg`, independent of whether a file is in the direct AppID directory or the hash subdirectory. Restrict traversal to the exact AppID subtree; do not recursively scan the whole library cache or search by partial AppID prefix. Enumerate only one hash-directory level, with ordinary I/O failures handled as cache misses. Stable deterministic choice if multiple candidates exist; no need for an asset version database.

**Global source priority (important):**

```text
active account's custom hero
→ active account's custom horizontal grid
→ built-in cached library hero
→ built-in cached library header
→ image absent (unchanged UI)
```

This means a custom wide cover beats a built-in hero, as the user's Steam personalization should win.

If a candidate exists but cannot be decoded by WinUI, hide it or try the next candidate in this list through a bounded fallback; never crash the Overlay. Prefer supported local PNG/JPEG image formats. Do not fetch URLs from `steamstatic`, SteamGridDB or any external CDN.

## 6. Resolution, caching and lifetime

Keep the artwork implementation **read-only and frontend-local**. The Overlay is a separate executable and does not reference the Full1902 Runtime project. A small Overlay-local resolver that accepts `steamRoot`, `appId`, and optionally selected account ID as testable inputs is sufficient; do not add interfaces/managers/singletons just for an optional image.

Expected behavior:

- When opening the Profile tab or receiving a **different authoritative active Profile target**, immediately remove any previous game's image unless the same exact target remains eligible.
- With a native Steam target, resolve eligible local artwork **off the UI thread**, then set `BitmapImage`/`Image.Source` on the UI dispatcher. Limit decode size near displayed physical width to avoid retaining a huge 4K hero unnecessarily; do not modify source files.
- Before applying an asynchronous result, re-check the current authoritative target and selected tab (or equivalent existing state) so a previous game's artwork cannot appear over a newer Steam/Xbox/no-game Profile. A simple target comparison is enough; **do not** invent epochs, barriers, additional authority or retry state machines.
- On same-game value-only Quick Settings refreshes (sliders, toggles, state publication), avoid repeated directory scans/bitmap reloads.
- On re-entering Profile, allow a fresh local lookup for that AppID so newly cached/customized files can appear without app restart; do not poll in the background and do not watch folders.
- Remove/hide artwork promptly on no-game, Xbox, non-Steam, unavailable state, and when another tab is selected. No prior-game image may leak into other tabs.
- Missing/unreadable files, inaccessible registry or userdata paths, Steam not found, invalid VDF, or decode failure must leave the existing UI fully functional. No user popup or repeated warning spam.
- No persistent bitmap copies, no application-managed disk/image cache, no network, no extra Steam file writes. In-memory UI bitmap retention for the currently visible image is normal, and release it when target changes/hides.
- Existing overlay show/hide, panel geometry, controller navigation, delayed mutation flushing, focus and disposal remain unchanged.

## 7. Files to inspect/change

**Expected primary implementation files:**

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml` — right-column background host/layer, before existing title/scroll children.
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Profile.cs` — artwork visibility/update wired to current active Profile target and existing profile presentation lifecycle.
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs` — selected-tab visibility boundary only if needed.
- One small appropriately named `src/SteamInputAddonforClaw.Overlay/*Steam*Artwork*.cs` file **only if useful** to keep file-system resolution testable.
- `src/SteamInputAddonforClaw.Overlay/Themes/QamOverlayResources.xaml` — only if shared QAM surface/fade values genuinely need a local resource.

Do not change unless source inspection reveals a demonstrable necessity:

- Runtime game/session detection or Xbox detection;
- `QuickSettingsPageSnapshot` or Overlay IPC wire version/schema;
- `QuickSettingsPresentation` / mutation routing;
- main Steam/Xbox Profile pages;
- PID1902/HidHide/VIIPER/device lifecycle;
- Steam account data, installation contents, or the Steam artwork cache.

If a simple Overlay-local read-only check proves native identity, **no transport protocol change is justified**.

## 8. Tests and acceptance (automated/code-based; no device blocker)

Add focused unit tests for the resolver/predicate, preferably in existing `tests/SteamInputAddonforClaw.UiTests` (which references the Overlay assembly) using temporary synthetic directory trees. Keep tests deterministic and independent of Steam installation, hardware, real registry, and current account data.

At minimum cover:

1. **Strict eligibility:** available native Steam target with matching manifest true; Xbox false; no game false; loading/unavailable false; invalid Steam AppID false; Steam non-Steam shortcut AppID with no native manifest false even if custom art exists.
2. **External Steam library:** an exact `appmanifest_<AppId>.acf` under a configured secondary Steam library qualifies; partial prefix `appmanifest_1234.acf` must not qualify `123`.
3. **Two roots/source precedence:** custom hero > custom landscape > built-in hero > built-in header. Confirm each path can independently supply artwork and that missing custom art falls through to the built-in cache.
4. **Steam cache layouts:** direct AppID directory, one-level hash directory, and recognized legacy flat filename; unrelated AppIDs not selected. Portrait-only cache is ignored.
5. **Account selection:** the active/recent account's custom art is preferred; unrelated account art is never selected by accident; unknown account falls back to built-in cache.
6. **Failures:** missing root, inaccessible/unreadable file/directory, malformed optional metadata and unsupported/undecodable image do not affect Profile UI or crash. Scope exception handling to optional artwork.
7. **Visual wiring and target changes:** image host is behind existing `PageTitle` and `BodyScroll`, right-column-only and non-interactive; new target/no-game/Xbox/tab switch removes stale image; same-target value refresh does not reload. For UI-specific assertions, use existing Overlay UI/source-contract test conventions rather than building a new UI test framework.
8. **Regression:** existing Profile Enabled/TDP/CPU Boost/Power/FPS/Resolution rows, active-game target mutation, keyboard/gamepad navigation and scroll behavior remain wired unchanged.

Run the relevant existing unit/UI tests and build where CI supports them. If a UI assertion requires a Windows environment not available to the local runner, report that fact without converting it into a PR blocker.

**Physical-device visual confirmation is explicitly the user's role AFTER merge**, not a Codex implementation prerequisite or PR review blocker.

## 9. Explicit non-goals / anti-overengineering guardrails

- No CTW integration or legacy CTW routing.
- No Xbox/Game Pass/non-Steam shortcut artwork.
- No offline installed-game thumbnails, Main App profile cover design, or artwork selection settings.
- No SteamGridDB, Steam CDN requests, external providers or download queues.
- No image file import, export, write, conversion, background scan, FileSystemWatcher, persistent image cache, account-service abstraction, artwork database or extra IPC message.
- No game-running process watcher, new owner/state authority or complex cancellation/epoch system.
- No broad redesign of the Overlay shell, resizing, scrolling, title placement, layout hierarchy, controller ownership, lifecycle or navigation.

## 10. Definition of done

A focused code PR meets this work order when:

- the **running native Steam game's** artwork is read from one of the **two Steam-owned local artwork directories**, with custom art winning and cache layout fallbacks;
- the cover fills the full **right content-column top**, gradually fades to `#0E141B`, and stays **behind** all existing Profile UI at its **unchanged coordinates**;
- all other runtime states and tabs look and behave as before;
- missing art is silent and results in exactly the original Profile layout;
- focused deterministic tests and existing supported builds pass, with no new hardware acceptance requirement;
- changes stay small and UI-focused, preserve standalone Full1902 authority and keep one clear rendering/cleanup path.
