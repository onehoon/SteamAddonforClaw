# Work Order — Fix Blank How to Use Rendering with Lazy WebView2 Initialization

## 1. Goal

Fix the real user-visible bug where the **How to Use** page opens but the README / Korean user guide content does not appear.

This is a focused UI reliability fix.

The intended product behavior remains:

```text
English UI
→ fetch README.md from the repository
→ render Markdown inside the app-owned How to Use page

Korean UI
→ fetch docs/howtouse/README_KO.md
→ render Markdown inside the app-owned How to Use page
```

Do **not** change the documentation model established by the release user-guide work:

```text
README.md
= canonical English user guide
= English How to Use

docs/howtouse/README_KO.md
= canonical Korean user guide
= Korean How to Use
```

The bug is in the UI/WebView2 presentation path, not in the documentation content.

---

## 2. Product / Architecture Context

Read and preserve the current Full 1902 product architecture before implementation:

```text
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
```

Relevant architectural constraint:

- the Main UI is disposable frontend UI;
- the Runtime/controller owner must remain independent from this page;
- this work must not alter controller ownership, HidHide, VIIPER, PID1901/PID1902, Steam/XBOX profile state, Quick Settings Overlay, or Runtime lifecycle;
- this is entirely a frontend documentation-rendering bug.

Do not introduce a new service, manager, renderer abstraction, page-lifecycle framework, retry state machine, navigation epoch, or synchronization layer for this fix.

---

## 3. Incident Evidence

The issue was reproduced on the release-oriented build around:

```text
Version=0.1.339.0
Windows 11 build 26200
```

Logs were collected under:

```text
GoogleDrive\Addon\Log\1007\01
```

Relevant UI logs:

```text
ui-9644.log
ui-8236.log
ui-9704.log
```

Observed facts:

1. the external UI process launches normally;
2. frontend connection succeeds;
3. bootstrap succeeds;
4. MainWindow initializes;
5. frontend activates;
6. the UI remains alive long enough for the user to open How to Use;
7. no UI startup crash is present;
8. no current `[HowToUse]` failure is logged;
9. the README and Korean Markdown files are present on `main` and are reachable through the configured raw GitHub URLs.

Therefore this must **not** be treated as a missing README, missing Korean file, Runtime startup failure, or general MainWindow startup failure.

Important diagnostic limitation:

The current success path does not log enough stages to determine whether the blank page occurs:

- before Markdown download;
- during WebView2 initialization;
- after `NavigateToString()`;
- after successful WebView navigation but before visible composition.

This work must add enough stage logging to make the next incident conclusive.

---

## 4. Current Implementation

Current page:

```text
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs
```

The page is instantiated as part of `MainWindow.xaml` while initially hidden:

```xml
<views:HowToUsePage
    x:Name="HowToUseContent"
    Visibility="Collapsed" />
```

Inside the page, WebView2 is also constructed by XAML immediately:

```xml
<WebView2 x:Name="DocumentationWebView" />
```

When the user selects How to Use, `MainWindow.ShowPage(...)` currently does:

```text
HowToUseContent.Visibility = Visible
→ immediately call HowToUseContent.Activate()
```

and `Activate()` starts:

```text
download raw Markdown
→ BuildHtml()
→ EnsureCoreWebView2Async()
→ ConfigureWebView()
→ NavigateToString(html)
→ immediately set _loaded = true
→ immediately hide LoadingPanel
```

There are two practical problems in that shape.

### 4.1 WebView2 is created while its page is hidden

The `WebView2` control exists from MainWindow construction even though `HowToUsePage` is initially `Collapsed`.

This is unnecessary for a page the user may never open and creates a plausible hidden-to-visible WebView2 composition path.

A current WebView2 issue has been reported where a WebView created/kept hidden and later made visible can remain visually blank until another window interaction forces repaint. Treat this only as supporting evidence, not as proof of root cause.

The product fix should remove that hidden-WebView creation path rather than adding repaint hacks.

### 4.2 `NavigateToString()` is treated as successful completion

Current code effectively treats:

```csharp
DocumentationWebView.NavigateToString(html);
```

as if the content is already loaded and visible.

Immediately after the call:

```csharp
_loaded = true;
LoadingPanel.Visibility = Visibility.Collapsed;
```

This is not a valid completion boundary.

Navigation has only been initiated.

The page must consider the document loaded only after WebView2 reports a successful `NavigationCompleted`.

---

## 5. Scope

### Required

1. Stop constructing the How to Use WebView2 while the page is hidden.
2. Create/attach WebView2 lazily on first real How to Use activation.
3. Initialize WebView2 only after the lazily created control is attached to the visible page.
4. Preserve the current remote Markdown fetch.
5. Preserve the current Markdig rendering.
6. Preserve the current HTML sanitization behavior.
7. Preserve internal fragment navigation.
8. Preserve external HTTP/HTTPS link routing through the system browser.
9. Treat `NavigationCompleted` as the success/failure boundary.
10. Add useful `HowToUse` stage logging.
11. Keep Retry functional.
12. Add/adjust tests.
13. Manually verify first-open rendering without Alt-Tab/resize workarounds.

### Not in scope

Do not change:

- README content;
- Korean guide content;
- README/How-to-Use source-of-truth policy;
- language-selection policy;
- ClawHUD;
- Quick Settings Overlay;
- controller routing;
- controller Runtime;
- Full1902 ownership;
- Steam/XBOX profile behavior;
- update logic;
- application navigation design;
- WebView2 runtime installation architecture;
- global logging architecture.

Do not add a local cached documentation database in this PR.

Do not fall back to embedding a second copy of README into the app package.

Do not revert to showing the GitHub webpage directly inside WebView2.

The current app-owned Markdown rendering model is correct and must remain.

---

## 6. Required Design

### 6.1 Do not construct WebView2 in XAML at MainWindow startup

Replace the eagerly created WebView2 with a simple host container.

Conceptually:

```xml
<Grid>
    <Grid x:Name="DocumentationWebViewHost" />

    <StackPanel x:Name="LoadingPanel" ...>
        ...
    </StackPanel>

    <StackPanel x:Name="FailurePanel" ...>
        ...
    </StackPanel>
</Grid>
```

Do **not** place a `WebView2` element in XAML.

The exact host may be `Grid`, `Border`, or another simple existing WinUI container.

Prefer the smallest change.

No custom host control is needed.

---

## 6.2 Lazily create one WebView2 instance

In `HowToUsePage.xaml.cs`, own one nullable WebView2 instance.

Conceptually:

```csharp
private WebView2? _documentationWebView;
```

The first time How to Use becomes active:

```text
HowToUse page is now Visible
→ create WebView2
→ attach it to DocumentationWebViewHost
→ wait until the control is loaded/attached
→ initialize CoreWebView2
→ fetch/render/navigate
```

Subsequent navigation away/back should reuse the same page-owned WebView2 instance.

Do not repeatedly destroy/recreate WebView2 for ordinary tab switching.

Do not add a WebView pool.

Do not make WebView2 a Runtime-owned object.

---

## 6.3 Preserve MainWindow page ownership

`MainWindow.ShowPage()` already sets:

```csharp
HowToUseContent.Visibility =
    page == MainNavigationPage.HowToUse
        ? Visibility.Visible
        : Visibility.Collapsed;
```

and then calls:

```csharp
if (page == MainNavigationPage.HowToUse)
    HowToUseContent.Activate();
```

A broad MainWindow navigation refactor is not required.

The fix can stay inside `HowToUsePage` as long as WebView2 itself is no longer created while the page is hidden.

If a very small MainWindow ordering adjustment is needed after real testing, keep it local and explicit.

Do not replace the overall navigation system.

---

## 6.4 Start only when the lazily created WebView is attached

Avoid arbitrary delays such as:

```csharp
await Task.Delay(100);
```

or:

```csharp
await Task.Delay(500);
```

Timing sleeps are not the product contract.

Prefer actual WinUI lifecycle evidence.

A simple acceptable pattern is:

```text
create WebView2
→ add to visible host
→ observe WebView2 Loaded
→ call EnsureCoreWebView2Async()
```

or an equivalently simple attached/loaded check.

The implementation must remain idempotent:

```text
Activate() called multiple times while already loading
→ no duplicate load

Activate() called after successful load
→ no duplicate download/navigation
```

The existing `_loading` / `_loaded` concept may be retained.

Do not invent additional lifecycle state unless actual implementation requires it.

---

## 7. Navigation Completion Is the Success Boundary

Do not set:

```csharp
_loaded = true;
```

immediately after `NavigateToString()`.

Successful flow must conceptually be:

```text
Markdown downloaded
→ HTML built
→ CoreWebView2 ready
→ NavigateToString(html)
→ NavigationCompleted
   ├─ IsSuccess = true
   │    → _loaded = true
   │    → _loading = false
   │    → hide LoadingPanel
   │    → keep FailurePanel collapsed
   │
   └─ IsSuccess = false
        → _loaded = false
        → _loading = false
        → hide LoadingPanel
        → show FailurePanel
        → log WebErrorStatus
```

Implement this with the smallest clear ownership.

A one-shot event completion helper is acceptable.

Example shape:

```csharp
private static Task<CoreWebView2NavigationCompletedEventArgs> NavigateToStringAsync(
    WebView2 webView,
    string html)
{
    var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    void OnCompleted(
        WebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        sender.NavigationCompleted -= OnCompleted;
        completion.TrySetResult(args);
    }

    webView.NavigationCompleted += OnCompleted;
    webView.NavigateToString(html);
    return completion.Task;
}
```

Adjust delegate types to the actual WinUI WebView2 API.

This is an example, not a mandate to create a generalized navigation abstraction.

If the page can implement the same behavior more simply with a page-owned event handler, prefer the simpler code.

---

## 8. Retry Semantics

Current user-visible Retry must continue to work.

On failure:

```text
FailurePanel visible
Retry clicked
→ clear failed load state
→ reuse the existing lazily created WebView2 when healthy
→ retry Markdown fetch/render/navigation
```

If WebView2 initialization itself failed in a way that leaves the instance unusable, the implementation may dispose/remove that failed page-owned WebView2 and create a fresh one on Retry.

Do not always recreate WebView2 for every ordinary HTTP failure.

Do not implement infinite automatic retry.

Do not add background retry loops.

The user-facing Retry button is sufficient for this issue.

---

## 9. Logging Requirements

This incident exposed a concrete observability gap.

Add concise stage logs under:

```text
[HowToUse]
```

### Required normal-path logs

At minimum provide evidence for:

```text
How to Use activation entered
Document source selected
Remote Markdown download completed
WebView2 control created
CoreWebView2 initialization completed
HTML navigation started
HTML navigation completed
```

Useful fields:

```text
Language
SourceUrl
MarkdownLength
HtmlLength
WebViewCreated
NavigationSuccess
WebErrorStatus
```

Example style:

```text
[INFO] [HowToUse] Documentation activation started. Language=ko
[INFO] [HowToUse] Remote Markdown downloaded. SourceUrl=... MarkdownLength=18169
[DEBUG] [HowToUse] WebView2 control created lazily.
[INFO] [HowToUse] CoreWebView2 initialization completed.
[DEBUG] [HowToUse] HTML navigation started. HtmlLength=...
[INFO] [HowToUse] HTML navigation completed. Success=True WebErrorStatus=Unknown
```

Do not log the entire Markdown document.

Do not log the rendered HTML body.

Do not add per-link noisy logs unless an error occurs.

### Failure logs

Existing:

```text
Remote documentation load failed.
```

is too broad by itself.

Log the failing stage.

Examples:

```text
Stage=MarkdownDownload
Stage=WebViewCreation
Stage=CoreWebViewInitialization
Stage=HtmlNavigation
```

This can be a field on the existing catch path rather than separate exception classes.

Do not create a logging state machine.

---

## 10. Preserve Current Security / Link Behavior

Do not regress `HowToUseMarkdownRenderer`.

Preserve:

```csharp
.DisableHtml()
```

The remote README must not gain arbitrary executable HTML capability through this fix.

Preserve current navigation policy:

- generated local document is app-owned;
- fragment links stay inside the document;
- external HTTP/HTTPS links open through `Windows.System.Launcher`;
- `javascript:`, `file:`, `steam:`, protocol-relative, and invalid/out-of-repository relative links remain rejected according to the current contract.

Do not loosen `ShouldCancelWebViewNavigation()` just to make the blank page disappear.

The fix is not to allow arbitrary WebView browsing.

---

## 11. Do Not Revert to Direct GitHub Navigation

Do not solve this by restoring:

```csharp
DocumentationWebView.Source =
    new Uri("https://github.com/...");
```

That reintroduces the GitHub chrome/layout problem this feature was specifically designed to remove.

The desired UI remains:

```text
raw Markdown
→ Markdig
→ app-owned HTML/CSS
→ WebView2
```

---

## 12. Do Not Add Repaint Hacks in the First Fix

Do not initially add:

- window resize nudges;
- fake 1-pixel resize;
- Alt-Tab emulation;
- HWND invalidation loops;
- compositor polling;
- periodic WebView visibility toggling;
- repeated `Reload()`;
- arbitrary delays.

The current practical bug is plausibly caused by constructing WebView2 while hidden.

First remove that lifecycle shape.

Only add a targeted presentation workaround if hardware testing demonstrates that a lazily created, visibly attached WebView2 still completes navigation successfully but remains blank.

If that happens, collect logs proving:

```text
WebView created while visible
CoreWebView2 initialized
NavigationCompleted Success=True
content still visually blank
```

before adding any repaint workaround.

---

## 13. Tests

Update/add tests in:

```text
tests/SteamInputAddonforClaw.UiTests/
```

### 13.1 Preserve renderer tests

Existing tests for:

- Markdown conversion;
- raw HTML suppression;
- GitHub-style anchors;
- relative-link resolution;
- external-link filtering;

must continue to pass.

Do not weaken them.

### 13.2 No eager WebView2 in XAML

Add a structural regression test proving the page XAML no longer eagerly creates WebView2.

Conceptually assert:

```text
HowToUsePage.xaml
does not contain
<WebView2 x:Name="DocumentationWebView"
```

and contains the new host name.

Use a robust text assertion consistent with existing UI tests.

Do not create a UI automation framework for this.

### 13.3 Lazy creation contract

Add a focused code/logic test where practical proving that:

```text
first activation
→ creates/attaches one WebView

second activation
→ does not create a second WebView
```

If direct WebView2 construction cannot be unit-tested without a live WinUI dispatcher, keep unit tests at the separable helper/state boundary and cover the actual behavior with the required manual test below.

Do not introduce interfaces solely to mock WebView2.

### 13.4 Completion semantics

Add testable helper/state coverage proving:

```text
NavigateToString call alone
!= loaded success
```

and that the success path is tied to navigation completion.

Again, do not create a generic browser abstraction only to satisfy the unit test.

### 13.5 Source routing

Existing source routing must stay:

```text
ko-KR
→ raw README_KO.md

en-US / other non-Korean
→ raw root README.md
```

---

## 14. Manual Verification

This fix requires real UI verification because the defect is visual/composition-related.

### Test environment

At minimum test on the affected MSI Claw / Windows environment if available:

```text
Windows 11 build 26200 family
Steam Addon for Claw release-style build
```

### Test A — first open

1. start the app;
2. do not resize the window;
3. do not Alt-Tab as a workaround;
4. click **How to Use** for the first time;
5. confirm Loading is shown briefly if needed;
6. confirm the rendered guide appears automatically.

PASS condition:

```text
No resize
No Alt-Tab
No tab-switch-away-and-back
README/guide becomes visible
```

### Test B — repeated navigation

```text
How to Use
→ Device
→ How to Use
→ Controller
→ How to Use
```

Confirm:

- content remains visible;
- no duplicate WebView2 is created;
- no second remote fetch is performed after successful first load unless current product intentionally refreshes it;
- link handling still works.

### Test C — English source

With a non-Korean UI culture:

```text
README.md
```

must render.

### Test D — Korean source

With Korean UI culture:

```text
docs/howtouse/README_KO.md
```

must render.

### Test E — internal anchor

Click a Table of Contents entry.

Confirm it scrolls inside the same rendered document.

### Test F — external link

Click a permitted external/repository link.

Confirm it opens through the system browser rather than navigating the How to Use WebView away from the local document.

### Test G — offline / download failure

Temporarily make the remote Markdown request fail using a safe test setup.

Confirm:

```text
Loading
→ FailurePanel
→ Retry
```

works and the UI does not remain stuck forever on the spinner.

---

## 15. Diagnostic Acceptance

After this PR, a future `ui-*.log` must be sufficient to distinguish at least:

```text
A. How to Use was never activated

B. activation happened but Markdown download failed

C. Markdown downloaded but WebView2 initialization failed

D. WebView2 initialized but navigation failed

E. NavigationCompleted reported success
```

This is a practical requirement based on the current incident.

Do not log every UI event.

Only log the meaningful How-to-Use pipeline boundaries.

---

## 16. Error Handling

Keep failure behavior simple.

### Markdown download failure

```text
show FailurePanel
allow Retry
log Stage=MarkdownDownload
```

### WebView2 creation / initialization failure

```text
show FailurePanel
allow Retry
log Stage=CoreWebViewInitialization
```

### NavigationCompleted failure

```text
show FailurePanel
allow Retry
log IsSuccess=False + WebErrorStatus
```

Do not crash the Main UI because documentation failed.

Do not affect Runtime/controller ownership because documentation failed.

Do not close the entire MainWindow.

---

## 17. Resource Lifetime

One HowToUsePage owns one lazily created WebView2 for the UI process lifetime after first use.

Normal navigation away from How to Use:

```text
page hidden
→ keep already loaded WebView2
→ no teardown/recreate requirement
```

UI process exit:

```text
normal WinUI ownership/disposal
```

If explicit disposal is already necessary for WebView2 under the current framework, perform only the normal page-owned cleanup.

Do not add a separate lifetime manager.

---

## 18. Race / Overengineering Policy

Follow the repository's production review policy.

The actual user bug is:

```text
normal app launch
→ normal navigation to How to Use
→ visible blank documentation area
```

This is a real reachable product failure and must be fixed.

Do not expand the PR to defend against theoretical combinations such as:

- user switching tabs at a precise instruction boundary during WebView initialization;
- multiple simultaneous Retry clicks separated by a scheduler interleaving;
- MainWindow close occurring between arbitrary individual statements;
- an invented multi-session UI environment.

The app supports one Windows user / one interactive session.

The existing `_loading` gate plus UI-thread ownership should remain sufficient unless a concrete normal-lifecycle bug proves otherwise.

Do not add locks, epochs, cancellation trees, or a browser state machine for theoretical timing.

---

## 19. Suggested Implementation Shape

A simple target shape is:

```csharp
public sealed partial class HowToUsePage : UserControl
{
    private WebView2? _documentationWebView;
    private bool _loading;
    private bool _loaded;
    private bool _webViewConfigured;

    internal void Activate()
    {
        if (_loaded || _loading)
            return;

        EnsureDocumentationWebView();
        TryStartDocumentationLoadWhenWebViewIsLoaded();
    }

    private void EnsureDocumentationWebView()
    {
        if (_documentationWebView is not null)
            return;

        var webView = new WebView2();
        webView.Loaded += DocumentationWebView_Loaded;

        DocumentationWebViewHost.Children.Add(webView);
        _documentationWebView = webView;

        AppLog.Debug("HowToUse", "WebView2 control created lazily.");
    }
}
```

Then:

```text
WebView Loaded
→ LoadDocumentationAsync()
→ download
→ BuildHtml
→ EnsureCoreWebView2Async
→ ConfigureWebView
→ await actual HTML navigation completion
→ success/failure UI
```

The exact ordering of Markdown download vs WebView initialization may remain whichever is simplest.

For example, downloading Markdown while WebView initializes is not necessary optimization.

Prefer readable sequential code.

---

## 20. Important Correction to Current Success State

Do not retain this semantic:

```csharp
DocumentationWebView.NavigateToString(html);

_loaded = true;
LoadingPanel.Visibility = Visibility.Collapsed;
```

Replace it with actual completion evidence.

This is mandatory even if lazy construction alone appears to fix the visual symptom, because otherwise:

- navigation failure cannot be surfaced correctly;
- the spinner can disappear before the document is usable;
- logs cannot distinguish navigation initiation from successful rendering.

---

## 21. Files Expected to Change

Expected:

```text
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
```

Possibly:

```text
tests/SteamInputAddonforClaw.UiTests/HowToUseMarkdownRendererTests.cs
```

only if needed for directly related regression coverage.

Avoid changes to:

```text
README.md
docs/howtouse/README_KO.md
HowToUseMarkdownRenderer.cs
MainWindow navigation
Runtime projects
controller projects
```

unless a small directly required correction is proven by implementation.

If `HowToUseMarkdownRenderer.cs` does not need a change, leave it untouched.

---

## 22. CI / Validation

Run the normal repository CI, including:

- restore;
- UI build;
- UI tests;
- full test suite;
- publish validation;
- startup smoke test where part of the normal workflow.

Do not accept unit tests alone as proof of this visual fix.

Record manual verification of first-open rendering in the PR description or implementation notes.

---

## 23. Acceptance Criteria

The PR is complete only when all are true.

### User-visible behavior

- Clicking **How to Use** on a fresh UI process displays the guide.
- No Alt-Tab is required.
- No window resize is required.
- No switch-away/switch-back workaround is required.
- English renders the root README.
- Korean renders `docs/howtouse/README_KO.md`.

### WebView lifecycle

- WebView2 is not eagerly created while HowToUsePage is initially collapsed.
- Exactly one page-owned WebView2 is lazily created after first real activation.
- Normal navigation away/back reuses it.

### Load semantics

- `NavigateToString()` alone does not mark the document loaded.
- success is committed only after successful `NavigationCompleted`.
- navigation failure shows the existing failure UI.
- Retry remains functional.

### Logging

Logs clearly show:

- activation;
- selected source;
- Markdown download completion;
- WebView creation;
- CoreWebView initialization;
- navigation start;
- navigation completion/failure.

### Security / behavior preservation

- Markdig rendering remains.
- remote raw Markdown remains the source.
- raw HTML remains disabled.
- internal anchors remain internal.
- external links still open externally.
- unsupported schemes remain rejected.

### Scope

- no README content rewrite;
- no controller/runtime changes;
- no new manager/state machine;
- no arbitrary repaint or delay workaround unless new hardware evidence proves lazy visible creation still fails.

---

## 24. PR Review Focus

Review this PR primarily for:

1. Does WebView2 still get instantiated while the How to Use page is hidden?
2. Is successful load tied to `NavigationCompleted`, not merely `NavigateToString()`?
3. Can the user retry after download/init/navigation failure?
4. Are logs sufficient to localize a future blank-page incident?
5. Are current Markdown security/link rules preserved?
6. Was the fix kept frontend-local without unnecessary abstractions?
7. Was the actual affected hardware/Windows path manually verified?

Do not block the PR for speculative races that are not realistically reachable in the normal single-user/single-session product lifecycle.
