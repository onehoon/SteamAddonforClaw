# Work Order — v0.1.343 / PR 724 post-merge regression corrections

Date: 2026-10-08
Repository: onehoon/SteamAddonforClaw
Code baseline reviewed: main, squash commit 62a027955a52f15027c76d5205d6b02729fc8f85 (PR #724)
Log baseline: Google Drive / Addon / Log / 1008, 2026-10-08 20:28–20:30 KST, build 0.1.343.0
Execution: local Codex
Requested delivery: ONE implementation PR with THREE independently reviewable topical implementation commits
Hardware/visual validation: user after merge, NEVER mandatory for local Codex or a PR blocker

Status: This is a work order only. Implement corrections in the repo in a new PR. Re-read main immediately before coding.

---

## 1. Governing architecture, sources and scope

Before coding read together:
- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/work-order/1008_V01342_SHORTCUT_DRAG_HOWTOUSE_MEDIUM_LAUNCH_REGRESSION_ONE_PR_THREE_COMMITS_WORK_ORDER.md
- docs/work-order/1008_USER_ACTION_PROCESS_LAUNCH_PRIVILEGE_WORK_ORDER.md
- docs/work-order/1008_HOW_TO_USE_WEBVIEW2_LAZY_INITIALIZATION_AND_RENDER_COMPLETION_FIX_WORK_ORDER.md
- docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md

Review current source and existing tests:
- src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs and HowToUsePage.xaml; HowToUseMarkdownRenderer.cs
- tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
- src/SteamInputAddonforClaw/Processes/UserProcessLauncher.cs
- src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
- src/SteamInputAddonforClaw/CenterM/Oem1BigPictureLauncher.cs; FrontButtonXboxAppLauncher.cs; Oem1ApplicationLauncher.cs
- tests/SteamInputAddonforClaw.Tests/UserProcessLauncherTests.cs and ShortcutLaunchDiagnosticsTests.cs
- src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml and ShortcutPage.xaml.cs
- tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
- tests/SteamInputAddonforClaw.Tests/ShortcutEditorRuntimeTests.cs and ShortcutEditorFrontendTests.cs

Product: standalone Full1902; one Windows administrator user and one interactive session; no CTW integration, RDP, multi-session or Fast User Switching. The High Runtime remains the sole controller authority and WING/Win+G suppression owner. Main UI and Overlay remain High-token-inheriting frontends. Same-user, same-session Medium actions remain Medium, except expressly opted-in EXE/PowerShell High. Do not modify PID1901/PID1902, DirectInput, HidHide, VIIPER, presentation, routing rollback, sleep/hibernate/resume, startup task, teardown, WING suppression or controller ownership.

Explicit exclusion: legacy Shortcut Schema 1 support, conversion, storage migration or schema version changes. No new broker, helper, token-cache authority, process layer, renderer framework, gesture state machine, timer, epoch or speculative race handling.

---

## 2. Confirmed v0.1.343 evidence versus remaining uncertainty

Source folder: https://drive.google.com/drive/folders/1BHxU_-FJmh5bI4qzGQYnvSZ8f_6XN3Eg

Primary Runtime: https://drive.google.com/file/d/1eEDFN9e9D-4kccxwwvTeBtoR34LBOfB5/view
Main UI: https://drive.google.com/file/d/1ctrbg2MdNp8_G0uU-Gh7rJxK44Jvzmia/view
Second UI: https://drive.google.com/file/d/12fCRRH2KjlINok6sLNiOuoJ3y9SROEFk/view

These are NEW v0.1.343 logs, not the previous v0.1.342 evidence.

A. How to Use, 20:28:45 UI lines 5–13, repeat 20:29:03 and in the next UI process:
- Markdown downloaded: length 18169.
- Generated HTML: length 27457.
- CoreWebView2 initialized.
- The app called NavigateToString.
- WebView navigation starting: NavigationId=2 UriKind=data IsUserInitiated=False Decision=Cancel MatchedPendingDocument=False.
- NavigationCompleted: same ID, IsSuccess=False, WebErrorStatus=OperationCanceled, not matched.
- No completion for the pending generated document, then 15-second timeout; retry repeats.

PROVEN: the own generated document's NavigationStarting is rejected. Current ShouldCancelWebViewNavigation accepts only about:blank, and HowToUseNavigationCorrelation.TryCaptureNavigationStart uses the same rejection policy. BOTH gates reject the observed internal data: URI.
IMPORTANT: Microsoft documentation describes the location/origin of the resulting NavigateToString document as about:blank; that DOES NOT mean this installation will report about:blank in NavigationStarting.Uri. Real logs show data:. Never conflate document origin with navigation-event URI.

B. Medium process launch, Runtime lines 436–438, 449–451, 476–478, 542–602:
- Current High/Full validated.
- Linked Limited/Medium, same SID and session validated.
- DuplicateTokenEx fails: NativeErrorCode=1346 (0x00000542).
- Steam client, Big Picture and Xbox built-ins all fail at this SAME stage; Screenshot succeeds.

PROVEN: neither CreateProcessAsUserW nor CreateProcessWithTokenW was attempted in those failed calls; replacing those APIs did not address the actual failure. 1346 means ERROR_BAD_IMPERSONATION_LEVEL. Its numeric value alone DOES NOT prove whether the linked handle is Primary, impersonation, lacks needed rights, or is affected by a calling-thread security condition.
CURRENT CODE: GetLinkedToken -> ValidateMediumLinkedToken -> unconditional DuplicateAsPrimary(linkedToken). That unconditional step is the actual failing operation.
UNKNOWN until queried: GetTokenInformation(TokenType) on the linked handle; permitted token handle access rights.

C. Main App native Shortcut reorder, UI line 14 onward and next UI lines 14–16:
- Native drag starts once (SourceIndex 3, or SourceIndex 0 after UI relaunch).
- Later DragItemsStarting events keep being rejected with ItemCount=1, OperationInProgress=False, RefreshInProgress=False.
- No DragItemsCompleted log and no Runtime Move evidence.
- Current code stores _pendingDragOriginalTiles/_pendingDragTileId at start and clears ONLY in DragItemsCompleted, so the next attempt rejects on _pendingDragOriginalTiles != null.

PROVEN: a missing completion strands drag state and blocks subsequent attempts for that page lifetime.
POSSIBLE REAL CONTRIBUTOR (not proven): ShortcutPage.xaml replaces the entire default ListViewItem ControlTemplate with a bare Grid/ContentPresenter without ListViewItemPresenter/default visual states; Windows App SDK documents ListViewItemPresenter and native reordering. Target this unnecessary customization first, then ensure stale state never permanently blocks new drags.

No log basis for modifying controller ownership, Overlay gamepad input, Screenshot, or Shortcut persistence. Schema 2 and four tiles load successfully.

---

## 3. Microsoft official API contract references

WebView2:
- Local content / NavigateToString, about:blank document origin, 2 MB HTML limit:
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content
- CoreWebView2.NavigateToString:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.navigatetostring
- WinUI3 WebView2 navigation events, thread affinity, navigation/message security:
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2

Windows tokens:
- System error 1346:
  https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-
- TOKEN_LINKED_TOKEN: linked handle belongs to caller and must be closed; this documentation does not promise the handle's token type:
  https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_linked_token
- GetTokenInformation and token type:
  https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation
- DuplicateTokenEx: source must permit TOKEN_DUPLICATE and requested token type is explicit:
  https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-duplicatetokenex
- Token rights:
  https://learn.microsoft.com/en-us/windows/win32/secauthz/access-rights-for-access-token-objects
- Impersonation level meanings:
  https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-security_impersonation_level
- CreateProcessWithTokenW: Primary token, required token access, SeImpersonatePrivilege and bounded command line:
  https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw
- CreateProcessAsUserW: Primary token / privilege contracts and existing long-command route:
  https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw

WinUI native reorder:
- ListViewBase.CanReorderItems: CanReorderItems+AllowDrop, CanDragItems for starting/completed events, IsSwipeEnabled for touch:
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems?view=windows-app-sdk-1.8
- DragItemsCompleted:
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.dragitemscompleted?view=windows-app-sdk-1.8
- Default item template/presenter:
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/item-containers-templates
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewitem?view=windows-app-sdk-1.8
  https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.listviewitempresenter?view=windows-app-sdk-1.8

Use the repository's actual Windows App SDK version and installed generic.xaml for precise properties. No SDK upgrade in this PR.

---

## 4. Implementation PR layout: three topical commits

Order (each includes its own tests/diagnostics):
1. fix(how-to-use): allow only the pending generated HTML navigation
2. fix(user-launch): use verified UAC-linked Primary token without unnecessary duplication
3. fix(shortcuts-ui): restore supported ListView item template and recover stale drag state

ONE PR, exactly THREE implementation commits. No logging-only or generic refactoring commit.

### Commit 1 — How to Use

Goal: load the downloaded/generated local HTML successfully without opening arbitrary external pages in the embedded WebView2.

Required:
1. Update DocumentationWebView_NavigationStarting AND HowToUseNavigationCorrelation.TryCaptureNavigationStart together. They currently share about:blank-only logic. A change in only one location will still time out.
2. When and only when one app-issued NavigateToString request is pending, match its observed non-user-initiated HTML data: navigation and capture its NavigationId exactly once. Inspect scheme and expected generated-HTML MIME/prefix rather than the entire data payload; do not log the embedded HTML.
3. Do not globally allow all data: URLs. Outside the pending app-issued document load, reject data:, arbitrary HTTP(S), file:, javascript: and custom URI top-level navigation. Preserve normal about:blank initialization if applicable. Never allow user-initiated data: navigation merely because a load is pending.
4. Correlate subsequent NavigationCompleted by the captured ID. Matching success commits loaded=true; matching failure shows FailurePanel/Retry. Unrelated initial and previous-completed IDs cannot finish this load.
5. Clear the pending attempt on success, failure and timeout. Existing 15-second bound stays. A Retry starts a fresh pending attempt and cannot reuse its previous ID.
6. Preserve lazy single WebView2, Korean/English document selection, Markdown renderer and HTML CSS, internal anchors, validated WebMessageReceived links, system external browser routing and current link validation. No navigation to GitHub-hosted pages inside WebView2; no temp HTML file or renderer architecture change.

Illustrative event handler (adapt predicate to the exact observed safe data: HTML format):
~~~csharp
private void DocumentationWebView_NavigationStarting(
    object? sender, CoreWebView2NavigationStartingEventArgs e)
{
    var pending = _pendingHtmlNavigation;
    var matchesGeneratedDocument = pending is not null
        && !e.IsUserInitiated
        && IsExpectedGeneratedHtmlNavigationUri(e.Uri)
        && pending.Correlation.TryCaptureNavigationStart(e.NavigationId, e.Uri);

    e.Cancel = !(matchesGeneratedDocument || IsAllowedInitializationBlank(e.Uri));
    AppLog.Debug("HowToUse", "WebView navigation starting.",
        ("NavigationId", e.NavigationId),
        ("UriKind", ClassifyNavigationUri(e.Uri)),
        ("Decision", e.Cancel ? "Cancel" : "Allow"),
        ("MatchedPendingDocument", matchesGeneratedDocument));
}
~~~

Keep a SINGLE page-owned document matcher. IsExpectedGeneratedHtmlNavigationUri checks the WebView-generated local HTML URI class, not all possible data: types, and the current pending app-call context gates allowance. Do not use URL query/full data string in log.

Tests:
- Pending, non-user-initiated generated HTML data: => allow, correlate NavigationId, successful completion.
- No pending or user-initiated data: => deny; unrelated data MIME and HTTP(S)/file/javascript/custom => deny even during pending.
- A completion for another NavigationId cannot settle pending.
- Matching OperationCanceled => failure/Retry, not success; timeout cleanup and repeated retry behave correctly.
- Korean/English source and external browser routing unchanged.
- Replace existing tests asserting only about:blank is allowed, not with an unconditional data: allowlist.

Expected later device evidence: UriKind=data / Decision=Allow / MatchedPendingDocument=True; same ID completes IsSuccess=True, no HtmlNavigation timeout.

### Commit 2 — UserProcessLauncher Medium token

Goal: eliminate the observed DuplicateTokenEx 1346 while retaining a verified same-user Medium action boundary.

Required:
1. Add GetTokenInformation(TokenType) to the existing native token-inspection code (Windows TOKEN_INFORMATION_CLASS TokenType=8; TOKEN_TYPE Primary/Impersonation). Read actual returned linked handle type, log categorical LinkedTokenType; keep existing High/Full and linked Limited/Medium, SID/session validation.
2. For a verified linked TOKEN_TYPE Primary token, use the already-owned linked token directly for CreateEnvironmentBlock and the selected CreateProcess API. No unconditional DuplicateTokenEx just to create another Primary token. Use proper SafeTokenHandle lifetime through process creation; dispose once. Honor API-required handle rights.
3. If the linked token actually has TOKEN_TYPE Impersonation, do not pass it to CreateProcess* as Primary. Only convert through a documented sufficient-impersonation-level/rights path if genuinely supported; otherwise fail closed with specific TokenType/ImpersonationLevel diagnostic. Do not silently weaken integrity, SID or session checks.
4. Preserve API selection: short, fixed steam://open/main and steam://open/bigpicture plus Xbox AppsFolder use CreateProcessWithTokenW; direct EXE, encoded PowerShell and HTTP(S) dispatch use CreateProcessAsUserW to retain long command lines. Do not change this again merely on the possibility of a later failure.
5. If already Primary but current linked-token handle rights do not satisfy selected API, solve via one documented access/right pattern after verifying the failure; do not use arbitrary token theft from explorer.exe, SeDebugPrivilege, global AdjustTokenPrivileges, additional brokers, saved tokens, or Medium-to-High retry.
6. Native GetLastWin32Error must be taken immediately after failed API and carried to existing UserProcessLaunchException. Preserve Stage, NativeErrorCode and safe token categorical logs; no token handles/SIDs, executable arguments or scripts in logs. Report dispatch accepted separately from target visibility.
7. Apply shared token preparation fix for BOTH process API branches. The old unconditional DuplicateAsPrimary is shared with user EXE/PowerShell/URL, not just Steam/Xbox. Keep front-button launchers sharing one UserProcessLauncher and no privilege escalation.

Illustrative shape, not a mandatory extra abstraction:
~~~csharp
using var linkedToken = GetLinkedToken(currentToken);
ValidateMediumLinkedToken(currentToken, linkedToken, currentIntegrity, currentElevationType);
var linkedType = GetTokenType(linkedToken); // GetTokenInformation(TokenType)

if (linkedType == TokenType.Primary)
{
    // A verified owned Primary token handle: use directly rather than
    // first calling the observed failing DuplicateTokenEx.
    return StartVerifiedMediumChild(startInfo, linkedToken, processCreationApi);
}

// Not Primary: documented conversion only if source actually permits it,
// otherwise fail closed. Never try the High token as fallback.
~~~

Prefer adapting the current StartWithMediumUserToken body so it takes/uses validated processToken rather than adding a launcher/broker framework. Do not assert linked tokens are always Primary unless measured. The error 1346 is a fact, token type is currently unknown.

Tests:
- Simulated validated linked Primary takes process API without invoking DuplicateTokenEx, and token ownership survives until environment/process creation cleanup.
- Impersonation/invalid token never passed directly as Primary; invalid/inadequate token fails closed.
- Exact native stage/error preserved for CreateEnvironmentBlock, CreateProcessWithTokenW and CreateProcessAsUserW.
- User EXE and PowerShell long command unchanged, no 1024-character limit imposed on them; quoting/working dir/environment intact.
- Steam/Big Picture exact URI; Xbox AUMID; HTML URL scheme whitelist; Screenshot separate.
- No Medium-to-High retry and explicit High user opt-in intact.
- Use narrow code-level/native seam only where existing tests permit; no comprehensive token fake API architecture.

Expected later device evidence: LinkedTokenType=Primary, token preparation uses linked primary, process creation attempted; if new native API failure occurs, stage/error recorded. Dispatch accepted is not target UI visibility proof.

### Commit 3 — Main App Shortcut reorder

Goal: native three-column item reorder works and an interrupted drag cannot leave the page permanently unable to reorder.

Required:
1. In ShortcutPage.xaml remove the complete Setter Property="Template" under ListView.ItemContainerStyle. It currently replaces native ListViewItemPresenter/default item visual states with a Grid+ContentPresenter. Retain ordinary container Style setters for transparent borders, stretch, zero padding and current card visuals; no bespoke ControlTemplate.
2. Preserve CanDragItems=True, CanReorderItems=True, AllowDrop=True, horizontal ItemsWrapGrid with MaximumRowsOrColumns=3, Edit/Delete buttons, existing spacing, and Runtime-only Move intent. No return to the per-FontIcon custom capture path in parallel with native reorder.
3. At a new DragItemsStarting, if stale _pendingDragOriginalTiles remains from a prior attempt while not busy/refreshing, CLEAR and log the abandoned gesture, then accept the current valid single-tile start instead of rejecting forever. Ordinary cancelled/no-op drags must not block future starts. Also reset pending state when Deactivate or a safe authoritative rebind invalidates the gesture.
4. DragItemsCompleted must clear its pending state for canceled, mismatch, invalid order or accepted Move; protect cleanup on exceptions. A missing completion may still happen, so a subsequent valid drag start recovers without Main UI restart. No arbitrary timer/epoch/lock.
5. Ensure the order snapshot is obtained after native collection reorder actually occurs. If event and source-update order differ, reconcile using the existing UI dispatcher turn or ObservableCollection notification; no independent order authority. Restore temporary UI order prior to sending exactly one Runtime FrontendShortcutMutationKind.Move; authoritative Runtime snapshot remains the final state.
6. Keep three equal row-major slots and correct drop indices across rows, including incomplete final row. Do not add persisted Row/Column fields, schema changes, or optimistic persistent local ordering.
7. If the stock ListViewItem template adds undesired visuals, customize only supported style properties, not a new whole-template replacement. Confirm drag affordance matches the actual item-level native drag behavior and the Edit/Delete buttons remain separate.

Illustrative revised container style:
~~~xml
<ListView.ItemContainerStyle>
    <Style TargetType="ListViewItem">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="0"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="VerticalContentAlignment" Value="Stretch"/>
        <!-- Intentionally NO Template setter: keep default ListViewItemPresenter. -->
    </Style>
</ListView.ItemContainerStyle>
~~~

Illustrative abandoned-drag recovery:
~~~csharp
if (!_operationInProgress && !_refreshInProgress
    && _pendingDragOriginalTiles is not null)
{
    AppLog.Debug("Shortcut", "Recovering an abandoned drag before new start.",
        ("PreviousTileId", _pendingDragTileId));
    ClearPendingShortcutDrag();
}
// Continue with current valid single-tile drag.
~~~

Diagnostics:
- DragItemsStarting acceptance or explicit RejectReason; distinguish Busy/Refresh/InvalidItem/MissingTile/StaleRecovered.
- DragItemsCompleted present/missing inferred from next-start recovery, DropResult, before/after index and order change.
- Runtime Move request/result (existing bounded logging), no per-frame pointer spam.

Tests:
- UPDATE existing UiArchitectureTests that currently ASSERT the custom ControlTemplate and absence of ListViewItem visual states. Assert the Template Setter is absent and default native reorder properties retained.
- Existing card geometry/3-column tests preserved.
- Single changed-item order -> one Runtime Move; canceled/no-op/invalid -> zero Move; rejected mutation restores Runtime snapshot.
- First start with lost completion -> next valid start RECOVERS rather than rejects; Deactivate and rebind clean up state.
- Cross-row and incomplete-final-row index cases.
- Ensure UI-native event/mutation timing assumed by the implementation has a meaningful code-level test; structural string tests alone do not establish actual user drag behavior.

Expected device evidence: completed valid drag reaches DragItemsCompleted and persisted Runtime Mutation=Move, not a permanent stream of "Native Shortcut drag start was rejected." The specific reason the former native drag did not complete is not conclusively established; do not state that restoring the template is independently proven sufficient until user hardware testing.

---

## 5. Cross-feature nonregression requirements

- Shortcut document stays Schema 2. NO Schema 1 handling.
- WING, HidHide, VIIPER, PID1902, controller routing and lifecycle untouched.
- How to Use origin versus navigation URI carefully distinguished; external links still validated/system-browser delegated.
- No untrusted data navigation allowed outside app-issued pending HTML.
- Steam/Xbox process start never silently runs High. Front button actions and long EXE/PowerShell use shared verified token preparation.
- Screenshot continues to work; Overlay closes only on existing accepted/succeeded dispatch policy.
- One tile owner / one Move request / authoritative Runtime ordering.

## 6. Delivery and verification

Local Codex must implement all three actual corrections, keep three topical commits in one implementation PR, run applicable build and relevant UI/Runtime tests and CI, and include a concise root-cause mapping in the PR body. Existing tests that merely freeze wrong XAML/URI behavior must be changed. Do not declare physical device behavior proven by mocks. Do not invent fallback infrastructure for a downstream error not yet observed.

Do NOT require local Codex to run the Claw, test touchscreen/mouse physically, open Xbox or Steam on hardware, reproduce FSE, or conduct suspend/resume testing. These are USER-OWNED POST-MERGE checks and NEVER an implementation-review or CI blocker.

User-owned post-merge matrix (informational):
- Korean How to Use initial load/retry, tab return, links/anchors; matching data: navigation should be allowed/completed without timeout.
- Repeated Shortcut drags across rows, canceled drag then immediate new drag, UI restart, persisted order/Overlay parity.
- Steam, Big Picture, Xbox and Screenshot; capture the next native error Stage/NativeErrorCode if process creation now advances but still fails.
- User EXE, PowerShell, HTTP(S) and mapped front-button actions, preserving requested Medium/High integrity.
- Desktop and Steam FSE as useful real-world contexts.

Review only realistically reachable, materially harmful defects. One user, one session. Do not add theoretical timing race machinery. Correct normal cleanup/failure policy and privilege isolation must remain intact.
