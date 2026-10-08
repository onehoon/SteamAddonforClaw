# Work Order — v0.1.342 Shortcut Drag, How to Use, and Medium Launch Regression Fixes

**Date:** 2026-10-08  
**Repository:** onehoon/SteamAddonforClaw  
**Delivery:** **ONE implementation PR, EXACTLY THREE topical implementation commits**  
**Baseline reviewed:** main @ 915efd2233230542424683597d4973bbfc9192f9 (merged user-action process privilege policy, PR #723)  
**Observed build:** v0.1.342.0 only  
**Evidence:** Google Drive / Addon / Log / 1008  
**Implementation owner:** local Codex  
**Physical Windows/handheld validation owner:** user, **after merge**  

> Re-read the latest main before coding. This SHA is the reviewed baseline, not permission to override newer main changes. This document is a work order, not a request to implement or open a PR now.

## 0. Scope and non-goals

Fix all three user-visible defects in one PR, separating the modifications and their diagnostic logging by commit:

1. Main App Shortcut cards cannot be reordered through drag-and-drop.
2. Main App How to Use does not render its downloaded document.
3. Overlay Shortcut built-ins for Steam, Xbox, and Steam Big Picture do not launch after the elevated Runtime / Medium user-process implementation. Screenshot succeeds.

Each commit must contain both a real corrective change and focused diagnostics for its own defect. Logging alone does not satisfy the goal. Maintain existing product behavior outside these failures.

**Explicitly excluded:** legacy Shortcut document Schema 1 compatibility, migration, recovery, backup, storage rewriting, or any change to Shortcut schema version. Do not include that unrelated issue in this PR.

Do not expand this PR into a UI redesign, Shortcut feature work, updater change, transport rewrite, external-app discovery framework, or controller lifecycle refactor.

## 1. Mandatory authority and source review

Read these current project authorities before coding:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/work-order/1008_USER_ACTION_PROCESS_LAUNCH_PRIVILEGE_WORK_ORDER.md
- docs/work-order/1008_HOW_TO_USE_WEBVIEW2_LAZY_INITIALIZATION_AND_RENDER_COMPLETION_FIX_WORK_ORDER.md
- docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md
- docs/work-order/1007_SHORTCUT_CLOSE_OVERLAY_AFTER_LAUNCH_WORK_ORDER.md

Inspect actual implementations and tests, not just historical work-order assumptions:

- src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml
- src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
- tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
- src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
- src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
- src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs
- tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
- src/SteamInputAddonforClaw/Processes/UserProcessLauncher.cs
- src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
- src/SteamInputAddonforClaw/CenterM/Oem1BigPictureLauncher.cs
- src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs
- src/SteamInputAddonforClaw/CenterM/Oem1ApplicationLauncher.cs
- tests/SteamInputAddonforClaw.Tests/UserProcessLauncherTests.cs
- tests/SteamInputAddonforClaw.Tests/ShortcutEditorRuntimeTests.cs
- tests/SteamInputAddonforClaw.Tests/ShortcutEditorFrontendTests.cs

### Product boundaries

Full1902 is a standalone app: one Windows user, one interactive session. No CTW integration, RDP, Fast User Switching, or multi-session machinery.

The persistent Runtime remains High-integrity for Full1902 controller ownership/WING suppression; Main UI and Overlay remain frontend-only and inherit that High token. Normal user-launched external actions must use a verified same-user Medium context unless explicitly opted into High for the supported EXE/PowerShell actions. Do not de-elevate the Runtime or frontends to work around Shortcut problems.

No changes to PID1901/PID1902 ownership, physical DirectInput, HidHide, VIIPER, virtual Steam Deck/Xbox360 presentation, routing rollback/fail-close, WING suppression, sleep/hibernate/resume, or controller recovery.

Do not add authorities, persistent brokers, workers, managers, new pipes, locks, epochs, retries or state machines for theoretical timing issues.

## 2. v0.1.342 incident evidence — do not overclaim causation

All timestamps below are 2026-10-08 KST. Exclude logs from lower build versions even when placed in the same 1008 folder.

### A. Shortcut drag

- Runtime log starting 19:03:20, PID 5652, identifies Version=0.1.342.0.
- Four new Shortcut tiles were saved at 19:04:20, 19:04:29, 19:04:39, and 19:04:45, respectively; all persisted with DocumentSchemaVersion=2.
- Runtime log starting 19:05:00, PID 3100, loaded TileCount=4 / DocumentSchemaVersion=2.
- No successful Shortcut Mutation=Move is visible in the captured build logs.
- There is no pointer press/capture/move/cancel/release diagnostic in the Main UI log, so the log does NOT yet establish where the drag is lost.

Source facts: ShortcutPage uses a fixed three-column ItemsWrapGrid. The drag gesture is wired only to a small FontIcon; the custom flow captures its pointer, resolves item bounds, and issues a single Runtime Move on PointerReleased. PointerCaptureLost/Canceled clears the gesture. Current tests mostly cover handler wiring and index math, not live routed-pointer behavior. Do NOT claim a specific failed capture event as proven.

### B. How to Use

UI logs ui-13368.log (19:01:46 onward) and ui-5460.log (19:03:41 onward) show:

- Korean GitHub Markdown successfully downloaded (MarkdownLength=18169).
- HTML generated (HtmlLength=27457).
- CoreWebView2 initialization succeeds.
- HTML navigation reports NavigationSuccess=False / WebErrorStatus=OperationCanceled on every attempt and retry.
- Documentation load fails at Stage=HtmlNavigation.

This is not evidence of a missing Korean guide or a failed GitHub HTTP request. Source uses a lazy WebView2, calls NavigateToString, awaits the first matching .NET event subscription's NavigationCompleted **without checking NavigationId**, and cancels NavigationStarting unless URI equals about:blank. A canceled internal navigation or an unrelated/initialization navigation completion may be incorrectly treated as the document's completion. Determine the real case and fix it; neither hypothesis is yet proven.

### C. Built-in launches

Runtime log 19:05:00, PID 3100, v0.1.342.0:

- 19:06:08: system.screenshot-fullscreen completes successfully.
- 19:06:14 onward: system.steam-client repeatedly fails with ExceptionType=Win32Exception.
- 19:06:23 onward: system.xbox-app repeatedly fails with ExceptionType=Win32Exception.
- 19:06:25 onward: system.steam-big-picture repeatedly fails with ExceptionType=Win32Exception.
- Runtime D-pad / A Accept input and Overlay tile selection are observed; the action reaches Runtime, so this is not a lost controller input or missing tile.
- Current ShortcutRuntime.LogLaunchFailure logs only the exception class, not the native Win32 error code nor failing OS API.

Source facts: all three built-ins call UserProcessLauncher.LaunchUri/LaunchXboxApp -> LaunchShellTarget -> StartWithMediumUserToken -> CreateEnvironmentBlock / CreateProcessAsUserW for an Explorer executable. The Medium path validates the current High token's UAC-linked Medium token and creates a separate process. StartWithMediumUserToken also serves normal user EXE/PowerShell and front-button external launches.

**Possible, not established:** CreateProcessAsUserW may fail due to token/privilege requirements (for example ERROR_PRIVILEGE_NOT_HELD / 1314). Other APIs earlier in the path may also throw Win32Exception. Preserve the NativeErrorCode and failing stage before treating any guessed error code as the cause.

## 3. Delivery plan — EXACTLY THREE implementation commits

Recommended order maps to the user's three reported problems:

1. **fix(shortcuts-ui): restore three-column drag reorder and add gesture diagnostics**
2. **fix(how-to-use): correlate internal WebView navigation and add failure diagnostics**
3. **fix(user-launch): restore Medium shell activation and expose native launch failures**

One PR should contain these three commits (test changes live in their relevant commit). Do not make a separate logging-only commit, a fourth refactor commit, or a schema migration commit. If implementation reveals the appropriate ownership order differs, retain three isolated topical commits and explain any dependency in the PR description.

### Commit 1 — Shortcut drag/reorder

**Goal:** A user can grip a Shortcut card in the Main App, move it into another occupied position in the three-column row-major layout, release, and persist the new order to Runtime. Overlay reflects that same authoritative tile order.

**Reviewed current path:**

- ShortcutPage.xaml: ListView with ItemsWrapGrid Orientation=Horizontal / MaximumRowsOrColumns=3.
- Shortcut drag handlers are attached to a FontIcon with Tag bound to TileId.
- ShortcutPage.xaml.cs: ShortcutDragHandle_PointerPressed/Moved/Released/Canceled/PointerCaptureLost; CaptureRealizedShortcutItemBounds; ResolveShortcutDropTargetIndex; ApplyMutationAsync.
- Runtime mutation authority: FrontendShortcutMutationKind.Move -> ShortcutRuntime, not locally maintained row/column positions.

**Required correction:**

1. Make the drag gesture reliably hit-testable. The current FontIcon is a small hit target; give its grip a deliberate transparent-background pointer surface (e.g. compact Grid/Border around the existing glyph) while preserving clearly separate Edit and Delete buttons. Capture the pointer on that stable grip surface, not on decorative glyph content. Ensure touch and mouse are both supported by the same event route. Avoid accidentally causing an Edit/Delete click during reordering.
2. Inspect why the existing pointer gesture may not reach a release/Move on a normally operated ListView (parent interaction, capture loss, hit testing, event routing, or target geometry). Fix the confirmed code cause; if manual pointer capture is unsuitable for the supported WinUI3 ListView/ItemsWrapGrid, prefer the supported native ListView reorder path as **a replacement**, not a second competing drag owner. Microsoft documents CanReorderItems + AllowDrop; CanDragItems is needed for DragItemsStarting/Completed. Do not assume a native reorder automatically persists correctly—verify interaction with the observable source and keep Runtime the sole order authority.
3. Preserve three equal column slots, row-major ordering, current cards, editing/deleting, existing payload limits, and Runtime-only Move persistence. No new persisted Row/Column fields.
4. A drop that changes index triggers exactly one Move intent and one authoritative refresh. Cancel/capture-loss without successful drop triggers no Move. Keep the original card order if Runtime rejects the mutation. Do not introduce speculative pointer-timing state management.
5. If the fix uses manual pointer handling, make it evident that the entire intended grip is hittable. Do not silently turn the full card into an action trigger that competes with Edit/Delete.

**Focused diagnostics (DEBUG for normal gestures, WARN only on real failure):**

- drag start attempt with TileId / source index / pointer device type / capture attempted and returned boolean;
- capture failure or canceled/capture-lost with stage and whether the active gesture was aborted;
- target index **only when it changes**, not on every PointerMoved frame;
- release/drop with source index, resolved target index, whether a Move request was sent;
- Move response with Succeeded/FailureCategory and resulting ordered-tile count (avoid serializing entire tile lists).

The Main UI currently catches errors in ApplyMutationAsync without actionable diagnostics: add a targeted log for failed Move RPC or result rather than converting failure into a successful-looking reorder.

Illustrative hit target (adapt exact WinUI names/handlers to current XAML):

~~~xml
<Border Width="36" Height="36" Background="Transparent"
        Tag="{x:Bind TileId}"
        PointerPressed="ShortcutDragHandle_PointerPressed"
        PointerMoved="ShortcutDragHandle_PointerMoved"
        PointerReleased="ShortcutDragHandle_PointerReleased"
        PointerCanceled="ShortcutDragHandle_PointerCanceled"
        PointerCaptureLost="ShortcutDragHandle_PointerCaptureLost"
        AutomationProperties.Name="Drag to reorder">
    <FontIcon Glyph="&#xE7C3;" IsHitTestVisible="False"/>
</Border>
~~~

This is an example of increasing hit-test area, NOT a claim that hit-testing alone resolves the incident. The implementation must trace and correct the actual code-level broken path.

**Tests:**

- Existing three-column sizing and row-major tests remain valid.
- Validate target-index math for intra-row, cross-row, first/last position, and incomplete final row.
- Validate single Move request on a completed changed-order drop, no Move on cancel/capture loss and no optimistic independent persisted order.
- Assert the revised grip event bindings and accessible hit target; update tests that are coupled to the old FontIcon surface.
- Use appropriate available code/WinUI unit tests; no local physical input validation required for merge.

### Commit 2 — How to Use rendering

**Goal:** After a successful remote Markdown download, How to Use reliably displays the generated local HTML in its page-owned lazy WebView2. Retry handles actual failures. Internal anchors and external-link routing remain correct.

**Reviewed current path:**

- HowToUsePage.xaml hosts a Grid; WebView2 is lazily created.
- HowToUsePage.xaml.cs: StartDocumentationLoad -> EnsureDocumentationWebView -> LoadDocumentationAsync -> EnsureCoreWebView2Async -> ConfigureWebView -> NavigateToStringAsync.
- The current NavigateToStringAsync subscribes to WebView2.NavigationCompleted, invokes NavigateToString(html), and completes on the first event. It does not correlate NavigationId or distinguish initial WebView2 navigation.
- ConfigureWebView attaches CoreWebView2.NavigationStarting and cancels all URI navigations except about:blank.
- HowToUseMarkdownRenderer generates HTML and routes external links through WebMessageReceived / Windows.System.Launcher.

**Required correction:**

1. Correlate the requested HTML document's NavigationStarting and NavigationCompleted by NavigationId (or a proven equivalent API-supported method). Do not report unrelated initial navigation cancellation as failure/success of the new HTML. Never set _loaded=true until the intended document has completed successfully.
2. Examine and correct the actual NavigationStarting cancellation policy for the generated document. Allow the page's own NavigateToString navigation, but continue blocking arbitrary external/page-navigation URLs and file/script navigation inside WebView2. Keep external links delegated to the system browser through the existing WebMessageReceived and URI resolution path.
3. Ensure a navigation genuinely canceled by the app (or failing with WebErrorStatus) is distinguishable from an unrelated canceled navigation. Real failure still displays FailurePanel and permits Retry; retry must not leave dead event subscriptions or accumulate duplicate navigation handlers.
4. Preserve one lazy page-owned WebView2 instance for ordinary tab switches, current language source selection, Markdown rendering, CSS, anchors, external links, and existing download timeout. No recreate-on-every-attempt loop, browser/IPC abstraction, periodic repaint hacks, or broad security loosening.
5. Do not replace the embedded HTML renderer with direct GitHub browsing.

**Focused diagnostics:**

- internal document navigation requested with generated HTML length and language;
- NavigationStarting with NavigationId, URI classification (about:blank/internal vs external; never log arbitrary full URLs unnecessarily), IsUserInitiated if useful, and Allow/Cancel decision;
- NavigationCompleted with NavigationId / IsSuccess / WebErrorStatus / matched-to-pending-document boolean;
- failure stage (Download, Initialize, Navigation, Render) and whether Retry is available;
- no high-frequency UI events or full HTML/Markdown content in logs.

Illustrative correlation condition:

~~~csharp
// Illustration only: establish the intended NavigationId from the
// matching internal NavigationStarting event, not an unrelated initial load.
if (args.NavigationId != expectedHtmlNavigationId)
{
    // This completion belongs to another navigation; it cannot settle
    // the requested document's load Task.
    return;
}
completion.TrySetResult(args);
~~~

Use the actual WinUI3 WebView2 event signatures/API behavior when implementing; do not introduce a second navigation ownership system. Ensure the pending attempt has an ordinary failure/cleanup path if the intended navigation does not complete.

**Tests:**

- An unrelated initial about:blank NavigationCompleted/OperationCanceled cannot finish the HTML load attempt.
- A matching successful HTML navigation commits loaded state; matching failure shows failure/retry.
- The generated document's navigation is allowed; external HTTP(S), file, javascript/custom-scheme top-level navigation stays blocked.
- Korean and English sources, renderer output, internal anchor handling, external-link delegation, lazy creation, and reuse continue to work.
- Prefer a separable tiny decision/correlation helper plus existing structural tests if live WebView2 cannot run in unit tests. Do not create mock-WebView frameworks solely for this test.

### Commit 3 — Medium user-action process launching

**Goal:** Steam client, Steam Big Picture, and Xbox built-in Shortcut tiles can dispatch successfully through the correct same-user Medium shell activation path from the High Runtime. Preserve the shared launch contract for normal-user EXE, PowerShell, URL, and mapped front-button actions.

**Reviewed current path:**

- ShortcutRuntime.Execute... -> UserProcessLauncher.LaunchUri or LaunchXboxApp.
- LaunchShellTarget builds a literal Windows explorer.exe invocation with one shell/URI argument.
- Launch delegates to StartWithMediumUserToken because the built-ins never opt into High.
- StartWithMediumUserToken validates current/linked tokens, DuplicateAsPrimary, CreateEnvironmentBlock, then CreateProcessAsUserW.
- LaunchWebUrl uses rundll32.exe url.dll,FileProtocolHandler through the same Medium path.
- LogLaunchFailure currently discards exception details and NativeErrorCode.
- Current tests only override launch delegates and check policy/command construction; they do not prove the underlying native API can create a Medium process in ordinary Windows.

**Required correction:**

1. Identify which native operation throws the observed Win32Exception. Preserve the Win32 error number at the point of failure. If the root cause is CreateProcessAsUserW token/privilege rights, fix that exact Medium launch mechanism. Do NOT paper over a privilege error with launching the target at High.
2. A supported implementation may adjust/use privileges already present on the current token, or select another documented same-user Medium process-creation path as appropriate; choose **one small shared implementation** that fits the product rather than stacked speculative fallbacks. Check behavior against actual Windows API privilege requirements. CreateProcessWithTokenW has different privilege requirements AND a documented smaller command-line limit; do not blindly replace CreateProcessAsUserW and regress long EXE/encoded PowerShell command lines.
3. Preserve identity/session/Medium integrity verification, literal EXE validation, existing argument quoting/working directory/environment semantics, proper native handle cleanup, failure classification and **no Medium->High retry**.
4. Preserve the exact Steam URI actions:
   - steam://open/main
   - steam://open/bigpicture
   - Xbox through the existing XboxGamingHomeAppIdentity.Aumid / shell:AppsFolder activation.
5. Preserve the existing HTTP(S)-only Website validation and safe launch association; no broad arbitrary URI acceptance or cmd.exe /c start workaround.
6. Do not alter the PID1902/HidHide/VIIPER/WING privilege architecture or split Main UI/Overlay into new privilege tiers.

**Focused diagnostics:**

- one bounded launch attempt/result per requested action, with action TypeId/category and requested privilege mode (Medium/High);
- current vs linked token validation stage and safe token attributes (SID match boolean, session match boolean, elevation category, integrity category); NEVER log token handles, SID bytes, environment variables, scripts, full arguments or sensitive target paths;
- failed native API/stage (OpenProcessToken, GetTokenInformation, DuplicateTokenEx, CreateEnvironmentBlock, CreateProcessAsUserW, or selected replacement);
- Win32Exception.NativeErrorCode as decimal and optional hex, plus exception type/message safe of private payloads;
- final dispatch outcome. Distinguish dispatch succeeded from the target actually becoming visible; no false positive guarantee that the Steam client UI or Xbox window opened.
- Logging should be useful in the next 1008-style capture without changing global log levels or adding a separate telemetry system.

Example of the existing missing failure evidence:

~~~csharp
// Inside the existing ShortcutRuntime failure log path; adapt argument types.
("ExceptionType", exception?.GetType().Name ?? "None"),
("NativeErrorCode", exception is Win32Exception native
    ? native.NativeErrorCode : (int?)null)
// Also log the stage *where the Win32 call actually failed*;
// do not guess it here based solely on ExceptionType.
~~~

At the Win32 failure location capture Marshal.GetLastWin32Error immediately, before calling another native API that could overwrite it.

**Tests:**

- Steam, Big Picture, and Xbox built-ins still route through the single Medium launcher with their exact original URI/AUMID; Screenshot remains its independent capture path.
- A controlled simulated native API failure preserves stage + numeric Win32 error in diagnostic results/logging; failures remain failures with no High fallback.
- Valid Medium path and explicit High opt-in are distinguishable and do not accidentally share the High launch API.
- EXE and encoded PowerShell launch construction/long command-line compatibility stays intact; URL encoding and HTTP(S) restrictions intact.
- Front-button launch sites continue to use the same authority and failure behavior as before.
- Existing native handle/resource lifetime and exception tests are extended only where relevant. No permanent token cache, helper service, background resident broker or retry manager.

Useful official API references:

- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems
- https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2

## 4. Cross-commit regression constraints

- Three independent user-visible fixes, one PR; avoid unrelated tidy-ups.
- Shortcut tile persistence remains v2 and continues to round-trip unchanged; do not touch historical Schema 1 at all.
- Successful Screenshot action and Overlay close/resume behavior are unchanged.
- Both mouse clicks and controller A execute the same Runtime-owned Shortcut action by TileId.
- Actions with Close Overlay after launch close only after actual accepted/successful dispatch, not after a failed launch; Screenshot retains its established policy.
- Do not double-fire one Shortcut action due to both pointer and controller input handlers.
- How to Use never permits arbitrary in-WebView external navigation; only its internal document plus approved external-link delegation.
- One Full1902 Runtime authority and original frontend transport; no controller lifecycle modifications.

## 5. Testing and PR evidence

**Required from local Codex before submitting its implementation PR:**

- code-level tests and current relevant test suites run locally or in CI;
- compile/publish/validation checks as available in the repository;
- identify and fix any test assumptions that encode broken behavior;
- PR description with three-commit mapping, root-cause diagnosis for each problem, and concise tests/CI results;
- if the exact native failure stage cannot be reproduced outside the user's machine, explicitly mark that uncertainty, include actionable telemetry, and still implement a justified concrete correction rather than claiming it is hardware-proven.

**NOT required from local Codex or as a merge blocker:** launching on the user's Claw, collecting new physical device logs, real mouse/touch validation on that device, FSE manual test, actual Steam/Xbox activation on that device, or real Sleep/Resume trials. Those are the user's **post-merge** validation role. Do not require physical tests for PR approval when code/test/lifecycle review is otherwise clean.

Suggested user-owned post-merge sanity matrix (informational only, not an implementation acceptance gate):

- Main App Shortcut: reorder 4+ cards across the first/second rows via mouse/touch grip; restart and verify persisted order and Overlay parity.
- How to Use: Korean first visit, Retry if necessary, switch away/back, anchors and external links; verify NavigationId and no OperationCanceled for intended HTML.
- Overlay: Screenshot, Steam, BPM, Xbox via controller A and pointer; check dispatch/outcome; test desktop and FSE where relevant.
- Normal user EXE, PowerShell, URL, and front-button mappings; verify requested Medium/High policy without fallback.

## 6. Review / merge guidance

Review for realistic defects affecting supported lifecycle, user-visible functionality, safety, data integrity, privilege correctness, and resource cleanup. Do not block for theoretical interleavings that cannot plausibly occur under the single-user/single-session Windows handheld lifecycle. Fail-closed behavior and correct privilege isolation are real requirements; speculative new locks, epochs, managers, services, or state wrappers are not.

**Definition of completion:** one implementation PR, three topical commits, actual corrective changes, actionable focused diagnostics, appropriate tests/CI, no Schema 1 changes, no unrelated Full1902 ownership modifications, and no physical hardware verification obligation imposed on local Codex.
