# Work Order — XBOX PR5: Production Catalog Frontend + Separate Read-Only XBOX Main App Page

> **Date:** 2026-10-06  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@229d8e3ad3a2c34a611cf63f3d9816c80b84efb4`  
> **Architecture authority:** `docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the active Full1902 documents it references  
> **Previous implementation:** PR #689 promoted PoC A into the production `SteamInputAddonforClaw.Xbox` parser/identity/catalog authority and retired the Catalog Diagnostic  
> **Scope:** expose the production installed-XBOX catalog through a narrow XBOX-specific frontend contract, rename the user-facing Profile navigation/page label to Steam, and add a separate read-only XBOX Main App page  
> **Out of scope:** XBOX profile persistence/editing, Favorites, active-game production runtime, performance apply, per-game M1/M2, Overlay XBOX work, Active Session Diagnostic retirement

---

## 1. Goal

Expose the production catalog implemented by PR #689 through the normal Main UI frontend transport.

Target user-visible navigation:

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

Target XBOX page for this PR:

~~~text
XBOX                                      [↻ Refresh]

[ Search games... ]

┌──────────────────────┐
│ Aniimo Legend        │
└──────────────────────┘

┌──────────────────────┐
│ Minecraft for Windows│
└──────────────────────┘
~~~

This PR is deliberately a **read-only catalog surface**.

The user sees the installed game name.

The user does **not** see XBOX package or identity internals.

---

## 2. User-facing information policy

The XBOX catalog page must behave like the existing Steam catalog in one important product respect:

> Platform identity exists internally, but normal users are shown the game name rather than platform IDs and package metadata.

Render only:

~~~text
DisplayName
~~~

Do not render:

~~~text
canonical key
StoreId
TitleId
PackageFamilyName / PFN
PackageFullName
PackageName
MicrosoftGame.config path
package root
ExecutableList
AUMID
PID
process path
identity publisher
identity resource ID
~~~

These values remain Runtime identity/persistence evidence only.

The existing Steam page does not show Steam AppID to the user.

The XBOX page should follow the same product principle.

### Search behavior

The XBOX search box searches:

~~~text
DisplayName
~~~

only.

Do not make hidden technical identity discoverable through search results by matching the canonical key, StoreId, TitleId, PFN, or package path.

### Internal identity is still required

The frontend catalog entry must carry the canonical string key internally so later XBOX profile operations can address the correct game even if two games have identical display names.

The key is transport/application state, not user-facing text.

---

## 3. Existing production authority that must be reused

PR #689 created:

~~~text
SteamInputAddonforClaw.Xbox
    MicrosoftGameConfigReader
    XboxGameIdentity
    XboxInstalledGameCatalog
~~~

The catalog already owns:

~~~text
PackageManager.FindPackagesForUser(string.Empty)
→ Effective / Installed locations
→ MicrosoftGame.config
→ canonical XboxGameIdentity
→ XboxInstalledGameCatalogResult
~~~

PR5 must consume this implementation.

Do not create:

~~~text
XboxUiCatalogScanner
XboxFrontendCatalogScanner
another PackageManager loop
another MicrosoftGame.config parser
another canonical-key helper
PowerShell scan
private Xbox DB scan
recursive WindowsApps scan
~~~

There remains one production installed-XBOX catalog authority:

~~~text
XboxInstalledGameCatalog
~~~

---

## 4. Microsoft platform reference

Current Microsoft documentation confirms:

~~~csharp
PackageManager.FindPackagesForUser(string userSecurityId)
~~~

returns installed packages for the specified user, and passing:

~~~csharp
string.Empty
~~~

retrieves packages for the current user.

Reference:

- https://learn.microsoft.com/windows/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser

That is already the PR #689 production implementation and must not be redesigned here.

The Main App uses the existing WinUI `NavigationView` shell:

- https://learn.microsoft.com/windows/winui/api/microsoft.ui.xaml.controls.navigationview

PR5 only adds another top-level navigation item and page to that existing shell.

---

## 5. Preserve the Steam domain exactly

Current Steam frontend contract remains:

~~~csharp
FrontendProfileGameCatalogEntry(
    uint AppId,
    string Name,
    FrontendProfileGameSource Source,
    bool Favorite)
~~~

and:

~~~text
ScanProfileGamesAsync()
CaptureGameProfileAsync(uint AppId)
SetGameProfile...
~~~

Do not:

- add XBOX entries to `ScanProfileGamesAsync()`;
- change `FrontendProfileGameCatalogEntry` into a multi-platform DTO;
- change Steam `uint AppId` to string;
- encode XBOX identity into a fake Steam AppID;
- use `AppId = 0` as an XBOX sentinel;
- change Steam profile persistence;
- change Overlay's current Steam catalog transport.

The Steam and XBOX catalog contracts remain separate.

---

## 6. Keep existing internal Profile names to avoid rename-only churn

The architecture requires a **user-facing** rename:

~~~text
Profile → Steam
~~~

It does not require an internal class/file rename.

For PR5, keep:

~~~text
ProfilePage.xaml
ProfilePage.xaml.cs
ProfileContent
MainNavigationPage.Profile
Tag="Profile"
~~~

unless a compile-time reason requires otherwise.

Change only user-facing text:

~~~xml
<NavigationViewItem Content="Steam" Tag="Profile">
~~~

and the existing Profile page catalog heading:

~~~xml
<TextBlock ... Text="Steam"/>
~~~

Do not create a large:

~~~text
ProfilePage → SteamProfilePage
MainNavigationPage.Profile → Steam
ProfileContent → SteamContent
~~~

rename diff in this PR.

The current internal names are stable Steam implementation details and can be cleaned up separately only if there is a real benefit.

---

## 7. Add a minimal XBOX frontend catalog contract

Update:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
~~~

Add a narrow XBOX-specific DTO.

Recommended:

~~~csharp
public sealed record FrontendXboxGameCatalogEntry(
    string Key,
    string DisplayName);
~~~

Do not include:

~~~text
StoreId
TitleId
PackageFamilyName
PackageFullName
PackageName
ConfigPath
Executables
~~~

in this frontend DTO.

Those values are not required by the read-only Main App catalog.

They remain inside the production Runtime identity/catalog model.

### Result/outcome

The UI must distinguish:

~~~text
scan succeeded
scan unavailable
scan failed
~~~

without receiving low-level package internals.

Recommended:

~~~csharp
public enum FrontendXboxGameCatalogOutcome
{
    Ready,
    Unavailable,
    Failed
}

public sealed record FrontendXboxGameCatalogSnapshot(
    FrontendXboxGameCatalogOutcome Outcome,
    IReadOnlyList<FrontendXboxGameCatalogEntry> Games,
    string? FailureMessage)
{
    public static FrontendXboxGameCatalogSnapshot Unavailable(string message) =>
        new(FrontendXboxGameCatalogOutcome.Unavailable, [], message);
}
~~~

Equivalent naming is acceptable.

Do not reintroduce the removed `FrontendXboxCatalogDiagnostic*` contracts.

This is a production catalog contract.

---

## 8. Add one frontend operation

Add to `IAddonFrontendControl`:

~~~csharp
Task<FrontendXboxGameCatalogSnapshot> ScanXboxGamesAsync(
    CancellationToken cancellationToken = default);
~~~

This method means:

> perform one bounded production installed-XBOX catalog scan now.

It does not mean:

- start a watcher;
- start a diagnostic;
- enable polling;
- start a background cache.

---

## 9. InProcessAddonFrontendControl mapping

Add a narrow test seam:

~~~csharp
private readonly Func<CancellationToken, Task<XboxInstalledGameCatalogResult>> _scanXboxGames;
~~~

Constructor optional parameter:

~~~csharp
Func<CancellationToken, Task<XboxInstalledGameCatalogResult>>? scanXboxGames = null
~~~

Default:

~~~csharp
_scanXboxGames = scanXboxGames
    ?? (token => new XboxInstalledGameCatalog().ScanAsync(token));
~~~

This keeps catalog execution:

- Runtime-owned;
- request-driven;
- unit-testable;
- free of a permanent catalog manager.

No new `AddonProcessHost` field is required.

No singleton catalog owner is required.

### Mapping production result to frontend result

Conceptually:

~~~csharp
public async Task<FrontendXboxGameCatalogSnapshot> ScanXboxGamesAsync(
    CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();
    cancellationToken.ThrowIfCancellationRequested();

    var result = await _scanXboxGames(cancellationToken).ConfigureAwait(false);

    return result.Outcome switch
    {
        XboxInstalledGameCatalogOutcome.Completed =>
            new(
                FrontendXboxGameCatalogOutcome.Ready,
                result.Games
                    .Select(game => new FrontendXboxGameCatalogEntry(
                        game.Identity.Key,
                        game.Identity.DisplayName))
                    .ToArray(),
                null),

        XboxInstalledGameCatalogOutcome.Unavailable =>
            FrontendXboxGameCatalogSnapshot.Unavailable(
                "XBOX game catalog is unavailable."),

        _ =>
            new(
                FrontendXboxGameCatalogOutcome.Failed,
                [],
                "XBOX game catalog could not be loaded.")
    };
}
~~~

Exact code may differ.

### Do not expose raw failure internals

`XboxInstalledGameCatalog` already logs the production failure reason.

Normal UI should receive a user-safe message.

Do not surface raw:

~~~text
HRESULT
package path
WindowsApps path
XML parser detail
publisher
PFN
StoreId
~~~

in the page.

---

## 10. Frontend transport

Add:

~~~text
FrontendRpcMethod.ScanXboxGames
~~~

to the normal `.Frontend` transport.

Add client:

~~~csharp
Task<FrontendXboxGameCatalogSnapshot> ScanXboxGamesAsync(...)
~~~

Add server dispatch:

~~~text
ScanXboxGames
→ _inner.ScanXboxGamesAsync(...)
~~~

This RPC takes no request payload.

Add it to the existing no-payload validation set.

### Protocol version

Current production protocol after PR #689:

~~~text
FrontendTransportProtocol.CurrentVersion = 53
~~~

PR5 adds a production RPC and DTO contract.

Bump:

~~~text
53 → 54
~~~

Document:

~~~text
Version 54:
add the production XBOX installed-game catalog snapshot and ScanXboxGames RPC
for the separate Main App XBOX page.
~~~

No compatibility shim is required.

Overlay protocol remains unchanged.

---

## 11. Add the separate top-level XBOX navigation item

Update:

~~~text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
~~~

Current:

~~~xml
<NavigationViewItem Content="Profile" Tag="Profile">
~~~

Target:

~~~xml
<NavigationViewItem Content="Steam" Tag="Profile">
    ...
</NavigationViewItem>

<NavigationViewItem Content="XBOX" Tag="Xbox">
    <NavigationViewItem.Icon>
        <SymbolIcon Symbol="Play" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
~~~

The known-supported `Play` symbol is sufficient.

Do not spend this PR on icon abstraction or custom icon assets.

Required order:

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

---

## 12. MainNavigationState

Add:

~~~csharp
MainNavigationPage.Xbox
~~~

and:

~~~csharp
"Xbox" => MainNavigationPage.Xbox
~~~

Keep:

~~~csharp
"Profile" => MainNavigationPage.Profile
~~~

for the user-facing Steam page.

XBOX is a top-level page.

It has no special mouse-back parent.

Do not implement:

~~~text
Profile
  Steam
  XBOX
~~~

and do not make XBOX a child of Steam/Profile.

---

## 13. Add XboxPage

Recommended files:

~~~text
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml
src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs
~~~

Do not call it a Diagnostic page.

This is the production Main App page.

### XAML layout

Mirror the mature Steam catalog surface rather than inventing a new visual system:

~~~text
20,16 page padding
InfoBar
header row
  "XBOX"
  Refresh button
Search box
3-column ItemsRepeater
same approximate card height / spacing / corner radius
~~~

Recommended structure:

~~~xml
<Grid Padding="20,16" RowSpacing="16">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <InfoBar x:Name="XboxInfoBar"
             Grid.Row="0"
             IsClosable="False"
             IsOpen="False"/>

    <Grid Grid.Row="1">
        ...
        <TextBlock Text="XBOX"
                   FontSize="24"
                   FontWeight="SemiBold"/>

        <Button x:Name="RefreshGamesButton"
                Content="↻ Refresh"
                Style="{StaticResource AccentButtonStyle}"
                Click="RefreshGamesButton_Click"/>

        <TextBox x:Name="GameSearchBox"
                 PlaceholderText="Search games..."
                 TextChanged="GameSearchBox_TextChanged"/>

        <ItemsRepeater x:Name="GameGrid">
            ...
        </ItemsRepeater>
    </Grid>
</Grid>
~~~

### Game card content

For PR5 each game card renders only:

~~~text
DisplayName
~~~

The card does not show:

- key;
- IDs;
- paths;
- package metadata;
- favorite star;
- profile toggle;
- launch button.

Use the full card width for the game name.

A simple non-interactive card is preferred for PR5 because there is no detail/profile action yet.

PR6 can make the card selectable when profile editing exists.

Do not add a click handler that performs no action.

---

## 14. XBOX page lifecycle

The architecture requires:

~~~text
XBOX page activation
→ one bounded rescan
~~~

and:

~~~text
explicit Refresh
→ one bounded rescan
~~~

Implement:

~~~csharp
internal void Initialize(IAddonFrontendControl frontend)
internal void Activate()
internal void Deactivate()
~~~

### Activate

~~~text
set active
start one ScanXboxGamesAsync
~~~

### Deactivate

~~~text
set inactive
cancel current page scan
do not render late result after page deactivation
~~~

### Explicit Refresh

If a scan is already running:

~~~text
cancel old page request
start one new bounded scan
~~~

Do not queue an unbounded series of scans.

### Simple stale-response guard

Use the current page request token/source as the identity of the latest request.

For example:

~~~csharp
var scan = new CancellationTokenSource();
_scanCancellation?.Cancel();
_scanCancellation?.Dispose();
_scanCancellation = scan;

var snapshot = await _frontend.ScanXboxGamesAsync(scan.Token);

if (!_active || !ReferenceEquals(_scanCancellation, scan))
    return;

Render(snapshot);
~~~

This is a page-local stale-render guard, not a new application state machine/epoch architecture.

---

## 15. No StateInvalidated subscription for package catalog refresh

The XBOX page should not subscribe to the broad Runtime `StateInvalidated` event just to rescan installed packages.

Package install/uninstall state is not changed by normal Device/Profile/Shortcut mutations.

Initial policy remains:

~~~text
page activation
explicit Refresh
~~~

only.

Do not add:

- `PackageCatalog` watcher;
- periodic timer;
- Runtime state-invalidated catalog rescan;
- polling;
- auto-refresh loop.

If real UX testing later demonstrates stale package state is a meaningful problem, design that separately.

---

## 16. XBOX page rendering rules

### Ready with games

~~~text
Outcome = Ready
Games.Count > 0
→ close InfoBar
→ display alphabetically
~~~

Order:

~~~text
DisplayName case-insensitive
then Key as internal deterministic tiebreaker
~~~

Do not display Key.

### Ready and empty

Show:

~~~text
No installed XBOX games were found.
~~~

This is informational, not an error.

### Unavailable

Show an InfoBar:

~~~text
XBOX game catalog is unavailable.
~~~

### Failed

Show:

~~~text
XBOX game catalog could not be loaded.
~~~

Do not render raw exception/package detail.

### Search

Filter only:

~~~csharp
game.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
~~~

No hidden metadata search.

---

## 17. Wire XboxPage into MainWindow

Update:

~~~text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
~~~

Add:

~~~xml
<views:XboxPage x:Name="XboxContent" Visibility="Collapsed" />
~~~

Initialize:

~~~csharp
XboxContent.Initialize(_frontend);
~~~

Navigation visibility:

~~~csharp
var wasXbox = XboxContent.Visibility == Visibility.Visible;

XboxContent.Visibility =
    page == MainNavigationPage.Xbox
        ? Visibility.Visible
        : Visibility.Collapsed;
~~~

Lifecycle:

~~~csharp
if (page == MainNavigationPage.Xbox)
    XboxContent.Activate();
else if (wasXbox)
    XboxContent.Deactivate();
~~~

Do not touch Overlay lifecycle.

---

## 18. Change the existing Steam page heading

Current Profile page header:

~~~xml
<TextBlock ... Text="Profile"/>
~~~

Change to:

~~~xml
<TextBlock ... Text="Steam"/>
~~~

Keep existing Steam behavior:

- catalog;
- search;
- Favorite;
- detail editor;
- Steam AppID internally;
- profile mutations.

Do not alter its search, profile mutations, debounce logic, favorites, or detail controls in this PR.

This is a label-only change inside `ProfilePage`.

---

## 19. No Favorites yet on XBOX

Although the final architecture includes XBOX Favorites, PR5 has no `XboxGames` profile persistence yet.

Therefore do not add:

~~~text
Favorite star
Favorite toggle
SetXboxGameFavorite RPC
temporary settings file
favorite in-memory state
copy of Steam favorites
~~~

Favorites arrive with the XBOX persistence/profile phase.

Do not add UI that cannot persist correctly.

---

## 20. No XBOX profile detail yet

PR5 must not add:

~~~text
Enabled
CPU Boost
TDP
Windows Power Mode
Intel FPS Limit
Resolution
M1
M2
~~~

to XBOX page.

Those require XBOX profile persistence/mutations.

Do not reuse Steam `CaptureGameProfileAsync(uint)` with fake AppIDs.

Do not create disabled placeholder controls/cards.

The XBOX page is intentionally just the installed catalog for PR5.

---

## 21. No Overlay changes

Do not change:

~~~text
OverlayWire
NamedPipeOverlayServer
OverlayProcessController
OverlayWindow.Profile
FrontendProfileGameCatalogEntry
Overlay profile catalog
QuickSettings Profile projection
~~~

XBOX Main App offline catalog support does not require XBOX catalog support in Overlay.

Overlay XBOX behavior remains a later phase.

---

## 22. No Active Session Diagnostic changes

Keep:

~~~text
XboxGameSessionDiagnostic
XboxSessionDiagnosticPage
FrontendXboxSessionDiagnostic*
WinEvent hooks
process generation cache
resume/restart reconcile
~~~

unchanged.

PR5 is installed-catalog frontend/UI only.

The production active-session runtime comes later.

---

## 23. Tests — frontend contract

Add focused production catalog frontend tests.

Recommended file:

~~~text
tests/SteamInputAddonforClaw.Tests/XboxGameCatalogFrontendTests.cs
~~~

Required:

1. Completed production result maps to `Ready`.
2. Each frontend entry carries:
   - canonical `Key`;
   - `DisplayName`.
3. Frontend entry does **not** contain StoreId/TitleId/PFN/package/config/executable fields.
4. `Unavailable` maps to a user-safe unavailable message.
5. `Failed` maps to a user-safe failure message.
6. Raw Runtime failure reason is not copied to the frontend user message.
7. cancellation is forwarded to the production catalog scan seam.
8. one call to `ScanXboxGamesAsync` causes one bounded catalog scan invocation.

Do not duplicate PR #689 parser/package tests here.

---

## 24. Tests — frontend transport

Update:

~~~text
FrontendNamedPipeTransportTests
~~~

Required:

1. protocol version is 54;
2. `ScanXboxGames` round-trips `FrontendXboxGameCatalogSnapshot`;
3. canonical string key survives transport exactly;
4. Unicode display names survive transport;
5. `ScanXboxGames` rejects an unexpected request payload;
6. v53 peer is rejected by v54 handshake;
7. all existing Steam RPCs continue round-tripping unchanged;
8. XBOX Active Session Diagnostic transport remains unchanged.

Update other tests that pin:

~~~text
FrontendTransportProtocol.CurrentVersion
~~~

from 53 to 54.

Do not change Overlay protocol version.

---

## 25. Tests — navigation/UI

Update/add UI tests proving:

### Navigation

~~~text
Device
Controller
Steam
XBOX
Overlay
Shortcut
How to Use
Settings
~~~

The existing item must no longer display:

~~~text
Profile
~~~

as its user-facing Content.

Internal `Tag="Profile"` is allowed and expected.

### Steam page

Assert:

~~~text
ProfilePage catalog heading = Steam
existing Steam detail editor still exists
existing Favorite control still exists
~~~

No broad Steam-page behavior change.

### XBOX page

Assert:

~~~text
XboxPage exists
header = XBOX
Refresh exists
Search games... exists
ItemsRepeater/catalog exists
Activate triggers ScanXboxGamesAsync
Deactivate cancels the current request
Refresh starts a new bounded request
~~~

### No identity leakage

The XBOX page XAML/code must not render technical fields.

At minimum assert absence of bindings/text such as:

~~~text
StoreId
TitleId
PackageFamilyName
PackageFullName
ConfigPath
Executable
AUMID
~~~

The code may access `game.Key` only for internal deterministic identity/tiebreak purposes.

It must not bind/render it.

---

## 26. Runtime/package scan failure policy

PR #689 already owns package-level failure behavior.

PR5 simply maps it to the page.

### One invalid/unreadable package

~~~text
XboxInstalledGameCatalog skips it
→ remaining games render normally
~~~

### Top-level catalog unavailable

~~~text
XBOX page shows unavailable InfoBar
Steam page unaffected
~~~

### Top-level catalog failed

~~~text
XBOX page shows failure InfoBar
Steam page unaffected
~~~

No:

- fallback package scan;
- PowerShell;
- retry loop;
- ACL modification;
- package watcher.

---

## 27. Logging

Do not duplicate the production catalog's package-level logs in Main UI.

Runtime already logs:

~~~text
XboxCatalog
scan requested/completed
accepted installed identities
global enumeration failure
~~~

Main UI may log one UI failure if the frontend transport itself throws.

Do not log every displayed game from the UI.

Do not log canonical keys at Info solely because the list rendered.

---

## 28. Required boundaries / non-goals

This PR must not change:

~~~text
ProfileDocument
GameProfile
GameProfileMutations
SteamSessionRuntime
XboxGameSessionDiagnostic
CPU Boost runtime
TDP runtime
Power Mode runtime
Intel FPS runtime
Display Resolution runtime
BackButtonMappingSettings
CanonicalXbox360InputPublisher
Full1902 routing
HidHide
VIIPER
WING / Center M
Xbox app front-button action
Overlay
~~~

It also must not add:

~~~text
XboxGameProfile
XboxGames dictionary
ActiveProfileTarget
XboxGameSessionRuntime
per-game M1/M2
XBOX Favorite persistence
XBOX profile mutation RPCs
~~~

---

## 29. Acceptance criteria

PR5 is complete only when all are true:

1. `FrontendXboxGameCatalogEntry` is XBOX-specific and uses string `Key`.
2. Its user-facing data is only `DisplayName`.
3. StoreId/TitleId/PFN/package/config/executable metadata is not added to the frontend catalog DTO.
4. `ScanXboxGamesAsync` exists on `IAddonFrontendControl`.
5. `InProcessAddonFrontendControl` calls the production `XboxInstalledGameCatalog`.
6. No second package scanner/parser/key authority is created.
7. completed/unavailable/failed catalog outcomes map to a narrow production frontend snapshot.
8. raw package failure internals are not surfaced in the normal UI.
9. `FrontendRpcMethod.ScanXboxGames` exists.
10. frontend protocol is bumped 53 → 54.
11. no-payload validation includes `ScanXboxGames`.
12. named-pipe client/server round-trip the XBOX catalog snapshot.
13. existing Steam frontend contracts are unchanged.
14. existing user-facing Navigation item `Profile` becomes `Steam`.
15. internal `ProfilePage` / `MainNavigationPage.Profile` rename is not required.
16. a separate top-level `XBOX` navigation item exists immediately after Steam.
17. `XboxPage` exists as a normal production page.
18. XboxPage activation performs one bounded scan.
19. explicit Refresh performs one bounded scan.
20. leaving the page cancels/ignores the current page request.
21. no timer/package watcher/background rescan exists.
22. XBOX page shows game names only.
23. search matches game names only.
24. canonical key is never rendered.
25. StoreId/TitleId/PFN/package/config/executable details are never rendered.
26. no Favorite UI is added to XBOX yet.
27. no profile detail/editor is added to XBOX yet.
28. existing Steam page still contains its full catalog/profile UI.
29. Steam page heading is user-facing `Steam`.
30. XBOX page failure does not affect Steam or controller authority.
31. Overlay code/protocol is unchanged.
32. XBOX Active Game Session Diagnostic is unchanged.
33. Full1902 controller ownership/presentation code is unchanged.
34. focused backend/frontend transport tests pass.
35. UI tests pass.
36. full solution build/tests pass.

---

## 30. Review policy

Review for realistic regressions:

- XBOX page accidentally reimplements package discovery;
- technical identity leakage into user UI;
- Steam catalog/profile contract regression;
- wrong navigation ownership;
- XBOX page scanning continuously;
- stale page request rendering after navigation;
- transport protocol mismatch;
- global catalog failure affecting unrelated Main App pages;
- accidental Overlay changes.

Do not block for theoretical races requiring new application-wide epochs/managers.

The only request-order protection needed here is a small page-local latest-request/cancellation guard.

---

## 31. Follow-up after PR5

After PR5:

~~~text
Steam
→ current fully editable Steam profile page

XBOX
→ installed XBOX game names
→ read-only production catalog
~~~

The next phase can add:

~~~text
ProfileDocument.XboxGames
XboxGameProfile
XboxGameProfileMutations
XBOX game selection/detail editor
Favorite
Enabled
CPU Boost
TDP
Windows Power Mode
Intel FPS Limit
Resolution
~~~

M1/M2 remains a later focused phase unless explicitly combined by a new approved work order.

---

## 32. Final ownership

After PR5:

~~~text
Runtime
    XboxInstalledGameCatalog
        ↓
    InProcessAddonFrontendControl.ScanXboxGamesAsync
        ↓
    .Frontend / ScanXboxGames
        ↓
Main App
    XboxPage
        ↓
    DisplayName only
~~~

Internal identity:

~~~text
XboxGameIdentity.Key
→ transported for stable identity
→ NOT rendered
~~~

User-facing:

~~~text
Aniimo Legend
Minecraft for Windows
~~~

That separation is intentional.
