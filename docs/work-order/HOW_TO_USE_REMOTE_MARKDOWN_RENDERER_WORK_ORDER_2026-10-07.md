# Work Order — How to Use Remote Markdown Renderer

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@883218f79cd87c16cbf4c796f107435d5bbff156`  
> **Observed build:** `0.1.338`  
> **Product architecture:** standalone Full1902  
> **PR shape:** one focused PR, two logical commits  
> **Feature surface:** Main App → How to Use  
> **Goal:** render the repository's canonical Markdown guide as an app-native document surface instead of embedding the GitHub website

---

# 1. Goal

The current How to Use page points WebView2 directly at GitHub:

~~~csharp
EnglishDocumentationUrl =
    "https://github.com/onehoon/SteamAddonforClaw#readme";

KoreanDocumentationUrl =
    "https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md";

DocumentationWebView.Source = new Uri(documentationUrl);
~~~

That renders the complete GitHub site inside the app:

~~~text
GitHub global navigation
repository navigation
file tree
Preview / Code / Blame controls
README document
~~~

This is not the desired product experience.

The How to Use page should instead render only the canonical Markdown document:

~~~text
GitHub raw Markdown
    ↓
HttpClient download
    ↓
Markdig Markdown → HTML
    ↓
small app-owned HTML/CSS shell
    ↓
WebView2.NavigateToString(...)
~~~

Target:

~~~text
Steam Addon for Claw
────────────────────

rendered headings
paragraphs
bold / emphasis
lists
tables
code blocks
links
internal table-of-contents anchors
~~~

No GitHub website chrome should remain visible.

---

# 2. Mandatory source review before implementation

Read current main before coding.

## 2.1 Full1902 authority

Read:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
~~~

This work is Main UI only.

It must not alter or participate in:

~~~text
PID1901 / PID1902 ownership
HidHide
VIIPER
DirectInput
Steam / XBOX game detection
controller presentation
Sleep / Hibernate / Resume
routing rollback
Center M authority
WING suppression
SafeUninstall
~~~

Do not introduce a Runtime service, frontend protocol, controller lifecycle participant, or new process.

## 2.2 Current How to Use implementation

Inspect:

~~~text
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
~~~

Canonical user documents:

~~~text
README.md
docs/howtouse/README_KO.md
~~~

Current MainWindow behavior remains:

~~~text
navigate to How to Use
→ HowToUseContent.Activate()
~~~

Keep this page ownership. Do not move documentation fetching into MainWindow.

---

# 3. Reviewed document requirements

The current canonical documents already rely on Markdown features that must keep working:

~~~text
# / ## / ### headings
**bold**
inline code
fenced code blocks
unordered lists
GitHub-style pipe tables
internal links:
  [Shortcut](#shortcut)
  [지원 기기](#지원-기기)

relative repository links:
  [LICENSE](../../LICENSE)
~~~

Therefore raw Markdown text displayed directly in WebView2 is not acceptable.

The renderer must preserve these semantics.

---

# 4. Dependency

Add Markdig only to the Main UI project:

~~~xml
<PackageReference Include="Markdig" Version="1.4.0" />
~~~

Target:

~~~text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
~~~

`1.4.0` is the current stable Markdig release reviewed for this work order.

Use Markdig because it provides:

- CommonMark-compatible parsing;
- HTML rendering;
- pipe-table support;
- heading auto-identifiers;
- no need for a JavaScript Markdown library.

Do not add:

- HtmlAgilityPack;
- another Markdown library;
- a JavaScript Markdown package;
- a local web server;
- a documentation cache database.

One Markdown dependency is sufficient.

---

# 5. PR structure

Implement as one PR with two logical commits.

Recommended:

~~~text
Commit 1
feat(how-to-use): render remote markdown as app HTML

Commit 2
feat(how-to-use): preserve document anchors and external link routing
~~~

Each commit must build.

Do not create a separate refactor-only commit.

---

# 6. Commit 1 — Fetch raw Markdown and render app-owned HTML

## 6.1 Replace GitHub page URLs with raw Markdown sources

Required sources:

~~~text
English:
https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/README.md

Korean:
https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/README_KO.md
~~~

Keep current culture policy:

~~~text
ko-*
→ Korean guide

all other UI cultures
→ English README
~~~

Prefer returning a small source descriptor instead of only a URL because link resolution also needs the repository-relative directory.

Example:

~~~csharp
internal sealed record HowToUseDocumentSource(
    string RawMarkdownUrl,
    string RepositoryDirectory);
~~~

Mappings:

~~~text
English
  RawMarkdownUrl:
    https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/README.md
  RepositoryDirectory:
    ""

Korean
  RawMarkdownUrl:
    https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/README_KO.md
  RepositoryDirectory:
    "docs/howtouse/"
~~~

Do not put this in Runtime settings or persisted configuration.

## 6.2 Fetch with one UI-local HttpClient

Use one reusable client:

~~~csharp
private static readonly HttpClient DocumentationClient = new()
{
    Timeout = TimeSpan.FromSeconds(15)
};
~~~

Do not:

- create one HttpClient per navigation;
- add retry middleware;
- add background polling;
- add ETag persistence;
- add a disk cache;
- add Runtime IPC for documentation.

One request on first successful page activation is sufficient.

## 6.3 Minimal page-local loading state

The current `_activated` boolean assumes URL navigation succeeds.

After switching to asynchronous fetching, do not permanently lock the page after a failed request.

Use minimal local state equivalent to:

~~~text
_loaded
_loading
~~~

Behavior:

~~~text
Activate()
  already loaded → do nothing
  currently loading → do nothing
  otherwise → start LoadDocumentationAsync()

success
  _loaded = true

failure
  _loaded = false
  show failure state
  allow Retry
~~~

No generalized page state machine is needed.

## 6.4 Loading/failure UI

Keep WebView2 as the main surface and add a small loading/failure overlay.

Suggested shape:

~~~xml
<Grid>
    <WebView2 x:Name="DocumentationWebView" />

    <StackPanel x:Name="LoadingPanel"
                HorizontalAlignment="Center"
                VerticalAlignment="Center"
                Spacing="12">
        <ProgressRing IsActive="True" />
        <TextBlock Text="Loading documentation…" />
    </StackPanel>

    <StackPanel x:Name="FailurePanel"
                HorizontalAlignment="Center"
                VerticalAlignment="Center"
                Spacing="12"
                Visibility="Collapsed">
        <TextBlock Text="How to Use could not be loaded." />
        <Button Content="Retry" Click="RetryButton_Click" />
    </StackPanel>
</Grid>
~~~

Exact wording may follow existing app copy.

Do not add a new loading-state framework.

## 6.5 Markdown pipeline

Use Markdig with GitHub-compatible heading identifiers.

Recommended:

~~~csharp
private static readonly MarkdownPipeline MarkdownPipeline =
    new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();
~~~

Important ordering:

~~~text
UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
before
UseAdvancedExtensions()
~~~

`UseAdvancedExtensions()` already installs an auto-identifier extension when one is not present.

This keeps current TOC anchors such as:

~~~text
#shortcut
#지원-기기
~~~

working.

## 6.6 Disable raw Markdown HTML

Keep:

~~~csharp
.DisableHtml()
~~~

Do not permit Markdown-authored active HTML such as:

~~~html
<script>
<iframe>
<object>
~~~

The generated HTML shell and its small link-routing script are app-owned.

Normal Markdown headings, links, tables, lists, code blocks, strong/emphasis remain supported.

## 6.7 Build one self-contained HTML document

Convert Markdown:

~~~csharp
var bodyHtml = Markdig.Markdown.ToHtml(markdown, MarkdownPipeline);
~~~

Wrap it in a small app-owned HTML shell:

~~~text
<!doctype html>
<meta charset="utf-8">
viewport meta
base element
inline CSS
rendered Markdown body
small app-owned link click script
~~~

Do not load GitHub CSS, Bootstrap, fonts, or scripts from a CDN.

### Base URL

Set `<base>` to the raw repository directory for the selected document.

English:

~~~text
https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/
~~~

Korean:

~~~text
https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/
~~~

This also lets future relative Markdown images resolve to the raw repository tree without another dependency.

## 6.8 Local document CSS

Use a compact stylesheet for:

~~~text
body
h1 / h2 / h3
p
a
ul / ol
code
pre
blockquote
table / th / td
hr
img
~~~

Recommended direction:

~~~css
html {
    color-scheme: light dark;
}

body {
    margin: 0 auto;
    max-width: 1100px;
    padding: 32px 40px 64px;
    font-family: "Segoe UI Variable", "Segoe UI", sans-serif;
    font-size: 16px;
    line-height: 1.6;
}
~~~

Use `@media (prefers-color-scheme: dark)` for dark colors.

Keep code blocks horizontally scrollable.

Keep tables usable at the app width.

Use:

~~~css
img {
    max-width: 100%;
    height: auto;
}
~~~

Do not copy GitHub's complete stylesheet.

Do not add a theme abstraction.

## 6.9 Render through NavigateToString

After WebView2 initialization:

~~~csharp
DocumentationWebView.NavigateToString(html);
~~~

Do not navigate WebView2 to `github.com` or `raw.githubusercontent.com` as the document itself.

The Markdown network fetch happens through HttpClient.

WebView2 displays only generated app-owned HTML.

## 6.10 WebView2 threading

Initialize and interact with WebView2 asynchronously on the UI thread.

Do not use:

~~~text
.Result
.Wait()
GetAwaiter().GetResult()
~~~

A narrow flow is enough:

~~~text
await DocumentationWebView.EnsureCoreWebView2Async()
configure WebView settings/events once
NavigateToString(html)
~~~

Do not create a WebView owner service.

---

# 7. Commit 2 — Preserve links without turning How to Use into a browser

## 7.1 Internal anchors stay inside the rendered document

Examples:

~~~markdown
[Shortcut](#shortcut)
[지원 기기](#지원-기기)
~~~

must scroll inside the current document.

They must not:

- open the default browser;
- load raw.githubusercontent.com;
- load GitHub;
- replace the rendered page.

Because a `<base>` URI is used for relative assets, explicitly handle fragment-only anchors in the app-owned click script.

Recommended shape:

~~~javascript
document.addEventListener("click", event => {
    const anchor = event.target.closest("a");
    if (!anchor)
        return;

    const href = anchor.getAttribute("href");
    if (!href)
        return;

    if (href.startsWith("#")) {
        event.preventDefault();

        const id = decodeURIComponent(href.substring(1));
        document.getElementById(id)?.scrollIntoView({
            block: "start"
        });

        return;
    }

    event.preventDefault();

    window.chrome.webview.postMessage(JSON.stringify({
        type: "open-link",
        href
    }));
});
~~~

Exact JavaScript may vary.

Keep it static and app-owned.

Do not interpolate Markdown content into JavaScript.

## 7.2 External links open in the default browser

For:

~~~text
https://...
http://...
relative repository paths
~~~

do not navigate the embedded WebView.

Preferred Windows path:

~~~csharp
await Windows.System.Launcher.LaunchUriAsync(uri);
~~~

How to Use remains a document viewer rather than a general browser.

## 7.3 Resolve relative repository links to GitHub blob pages

Current Korean guide contains:

~~~markdown
[LICENSE](../../LICENSE)
~~~

A click should open the normal GitHub LICENSE page in the external browser, not raw text.

Add one pure resolver, for example:

~~~csharp
internal static Uri? ResolveExternalDocumentationLink(
    HowToUseDocumentSource source,
    string href)
~~~

Rules:

### Fragment

~~~text
#shortcut
→ internal / no external URI
~~~

### Absolute HTTP/HTTPS

~~~text
https://example.com/docs
→ unchanged
~~~

### Relative repository link

Korean:

~~~text
source.RepositoryDirectory = "docs/howtouse/"
href = "../../LICENSE"

→
https://github.com/onehoon/SteamAddonforClaw/blob/main/LICENSE
~~~

English:

~~~text
href = "docs/howtouse/README_KO.md"

→
https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md
~~~

Use normal URI/path resolution.

Do not hand-roll `../` removal with string replacement.

## 7.4 Restrict outbound URI schemes

Allow only:

~~~text
http
https
~~~

Reject:

~~~text
file:
javascript:
data:
ms-appx:
shell:
steam:
other custom schemes
~~~

Do not pass arbitrary Markdown URI schemes to Launcher.

## 7.5 Validate WebView messages

Use structured JSON:

~~~json
{
  "type": "open-link",
  "href": "..."
}
~~~

Validate:

~~~text
type == "open-link"
href non-empty
href bounded in length
resolved URI scheme http/https
~~~

Ignore malformed or unsupported messages.

No exceptions should escape the event handler.

## 7.6 Navigation guard

Subscribe to `NavigationStarting` and block unexpected main-frame navigation.

Allow only the local generated-document navigation form required by `NavigateToString` / internal fragment handling.

Cancel normal HTTP/HTTPS navigation inside WebView.

All document HTTP/HTTPS links must open externally instead.

This is a defense-in-depth guard, not another routing authority.

---

# 8. Failure behavior

Documentation failure is not a Runtime/controller failure.

Expected:

~~~text
Markdown fetch fails
or Markdown rendering fails
or WebView initialization fails

→ Main App stays alive
→ How to Use shows local failure state
→ Retry remains available
→ no controller/runtime state changes
~~~

Log one concise Main UI diagnostic using the existing UI logging seam if available.

Do not log the full Markdown or generated HTML.

Do not crash Main UI.

Do not retry automatically in a loop.

---

# 9. No local documentation cache

Do not add:

~~~text
AppData Markdown cache
database
ETag metadata
background refresh timer
periodic fetch
version state
~~~

Desired behavior:

~~~text
Main UI process
→ first successful How to Use activation
→ fetch current canonical guide
→ keep rendered page for that UI process
~~~

If the first load fails:

~~~text
Retry
or leave/re-enter page
→ try again
~~~

A later Main UI process naturally fetches the current guide again.

---

# 10. Tests

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
~~~

A separate small renderer test file is acceptable if clearer.

Tests must be deterministic and offline.

## 10.1 Culture/source routing

Verify:

~~~text
ko-KR
→ raw README_KO.md
→ repository directory docs/howtouse/

en-US
ja-JP
→ raw README.md
→ repository directory ""
~~~

## 10.2 Markdown rendering

Input:

~~~markdown
# Title

**Bold**

- One
- Two

| A | B |
|---|---|
| 1 | 2 |

[Section](#section)

## Section

~~~text
hello
~~~
~~~

Assert equivalent output contains:

~~~text
<h1
<strong>
<ul>
<table>
href="#section"
id="section"
<pre>
<code
~~~

Do not assert the whole HTML byte-for-byte.

## 10.3 Raw HTML disabled

Input Markdown containing:

~~~html
<script>alert('x')</script>
~~~

Assert the pure Markdown-body output does not contain an executable Markdown-authored `<script>`.

Keep the Markdown-body helper separate from the app-owned HTML shell so this test is unambiguous.

## 10.4 Link resolver

Cover:

~~~text
"#shortcut"
→ internal / no external URI

"https://example.com/docs"
→ unchanged

"http://example.com/docs"
→ unchanged

Korean + "../../LICENSE"
→ https://github.com/onehoon/SteamAddonforClaw/blob/main/LICENSE

English + "docs/howtouse/README_KO.md"
→ https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md

"javascript:alert(1)"
→ rejected

"file:///C:/Windows/win.ini"
→ rejected

"steam://open/bigpicture"
→ rejected
~~~

## 10.5 HTML shell

Verify with pure/source tests:

~~~text
Markdown body embedded
base raw URL matches source directory
local CSS present
local link handler present
no GitHub webpage Source navigation remains
~~~

Do not spin up WebView2 in unit tests unless existing infrastructure already supports it.

---

# 11. Expected files

Production:

~~~text
src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml
src/SteamInputAddonforClaw.UI/Views/HowToUsePage.xaml.cs
~~~

One small page-local helper file is acceptable if it materially improves readability, for example:

~~~text
src/SteamInputAddonforClaw.UI/Views/HowToUseMarkdownRenderer.cs
~~~

Do not create:

~~~text
DocumentationManager
DocumentationService interface
RepositoryClient abstraction
MarkdownProvider abstraction
WebViewCoordinator
~~~

Tests:

~~~text
tests/SteamInputAddonforClaw.UiTests/HowToUsePageDocumentationRoutingTests.cs
~~~

Optional separate renderer test file only if clearer.

---

# 12. Explicit non-goals

Do not change:

- canonical English README content;
- canonical Korean guide content;
- navigation menu order;
- language selection policy;
- README publishing workflow;
- Runtime/frontend protocol;
- settings schema;
- update mechanism;
- Full1902 ownership;
- Overlay;
- Shortcut;
- controller behavior.

Do not embed an offline guide copy in the application package.

The repository remains the documentation source of truth.

---

# 13. Validation

Run:

~~~text
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx -c Debug

dotnet build SteamInputAddonforClaw.slnx -c Release

dotnet test SteamInputAddonforClaw.slnx -c Release

git diff --check
~~~

Also verify existing publish asset checks remain green because the UI project gains one managed dependency.

---

# 14. Manual validation

## A. Korean UI

Open How to Use.

Expected:

- Korean `README_KO.md`;
- no GitHub header;
- no repository file tree;
- no Preview/Code/Blame controls;
- rendered headings;
- bold text;
- lists;
- tables;
- code blocks.

## B. English/non-Korean UI

Expected root `README.md` rendered with the same app-owned document shell.

## C. Internal anchors

Click TOC links such as:

~~~text
Shortcut
Settings
지원 기기
Quick Settings Overlay
~~~

Expected:

~~~text
same document
→ scroll to section
~~~

No browser launch.

## D. Relative repository link

Click Korean guide `LICENSE`.

Expected:

~~~text
default browser
→ GitHub LICENSE page
~~~

Embedded How to Use remains unchanged.

## E. Absolute HTTP/HTTPS link

Expected:

~~~text
click
→ default browser
~~~

WebView must not become a website browser.

## F. Network failure

Block raw.githubusercontent.com temporarily.

Expected:

~~~text
How to Use could not be loaded
Retry
~~~

Main App remains healthy.

Restore network and press Retry.

Expected document renders normally.

---

# 15. Acceptance criteria

## Rendering

- [ ] GitHub website chrome is gone.
- [ ] Raw English/Korean Markdown is fetched from raw.githubusercontent.com.
- [ ] Markdown is rendered with Markdig.
- [ ] Headings, strong/emphasis, lists, tables, code blocks and links render correctly.
- [ ] Existing GitHub-style TOC anchors work.
- [ ] Raw Markdown HTML is disabled.
- [ ] HTML/CSS/link script is app-owned and self-contained.
- [ ] No external CSS/JS CDN is used.

## Links

- [ ] Fragment links scroll inside the current document.
- [ ] HTTP/HTTPS links open in the default browser.
- [ ] Relative repository links resolve to GitHub blob pages and open externally.
- [ ] Unsupported URI schemes are rejected.
- [ ] WebView2 does not become a general browser.

## Lifecycle

- [ ] Successful load occurs once per Main UI process.
- [ ] Failed load can be retried.
- [ ] No automatic retry loop exists.
- [ ] No disk cache/database exists.
- [ ] Documentation failure cannot affect Runtime/controller state.

## Scope

- [ ] Main UI documentation rendering only.
- [ ] No frontend protocol change.
- [ ] No settings schema change.
- [ ] No Runtime change.
- [ ] No Full1902/controller lifecycle change.
- [ ] Publish verification remains green.

---

# 16. Overengineering guard

The required architecture is intentionally small:

~~~text
HowToUsePage
  ├─ Resolve culture → document source
  ├─ HttpClient.GetStringAsync(raw Markdown)
  ├─ Markdig.ToHtml(...)
  ├─ wrap with local CSS/link script
  └─ WebView2.NavigateToString(...)
~~~

Links:

~~~text
#fragment
→ local document scroll

http/https
→ default browser

relative repo link
→ resolve against GitHub blob/main
→ default browser
~~~

Do not turn this into a documentation subsystem.

The goal is:

> **Keep README.md / README_KO.md as the single remote source of truth, preserve Markdown formatting and links, and show only the rendered guide inside the app.**
