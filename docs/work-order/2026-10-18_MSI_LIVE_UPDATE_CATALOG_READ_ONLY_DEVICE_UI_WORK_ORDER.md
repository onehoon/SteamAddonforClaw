# Work Order — MSI Live Update Catalog (Read-Only) in Device UI

**Date:** 2026-10-18
**Repository:** onehoon/SteamAddonforClaw
**Target:** Current main; re-read HEAD before editing
**Product:** Standalone Full PID1902 MSI Claw Addon; one Windows administrator user / one interactive session
**Owner:** Local Codex
**Delivery:** One focused PR; commits may be separated into OEM catalog invocation, Frontend transport, and UI/tests
**Hardware validation:** Product owner after merge. Physical-device testing is **not** a Codex requirement or PR merge blocker.

## 1. Goal and locked decisions

Add a **manual, read-only MSI Live Update catalog query** to the existing Main App **Device** page, with a native WinUI 3 expandable panel and four category tabs.

- Insert a collapsed **MSI Updates** SettingsExpander immediately **after DeviceSummaryCard** and **before the existing MSI Center M startup card**.
- Its expanded area contains a **fixed TabView**: **BIOS**, **Firmware**, **Drivers**, **Utilities**. Fixed means no add/close/reorder; switching tabs changes only the visible list and must not trigger another query.
- Provide one explicit **Check for updates** action. The first implementation fetches **catalog records only**. Clearly label the returned values as MSI catalog/latest published versions, **not verified updates available for this machine**.
- Use the original MSI **API_LiveUpdate.dll** request method, as established by the supplied REA report. **Do not extract, copy, embed or reimplement MSI API credentials/AWS signing**.
- MSI Center M may be **startup-disabled**. Do not start/enable Center M, its service, scheduled tasks, UI, or updater to perform a lookup. The DLL request path should be independent of the Center M application lifecycle.
- **No local installed-version collection/comparison**, no compatibility/applicability filtering beyond the product identity needed to query the catalog, no package downloads, no install, no firmware/BIOS write, no Store launch, no reboot.
- No new top-level navigation tab, no automatic scan, no background scheduler, no persistent cache/database, no generic update engine, and no CTW integration.
- Existing Full1902 Runtime/HidHide/VIIPER/physical PID1902 authority, routing, startup tasks, Center M enable/disable and all existing Device controls must remain unchanged.

The user's next PR will add **locally installed versions + candidate applicability/version comparison**. This PR must retain enough clean display metadata to support that future read-only addition without implementing it now.

## 2. Mandatory sources and source-of-truth boundaries

Read the current code and the following repository documents before making changes:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/work-order/APP_UI_PR_A_NAVIGATION_AND_PAGE_OWNERSHIP_REORGANIZATION_WORK_ORDER.md (historical page ownership)
- docs/work-order/2026-10-14_MAIN_APP_DEVICE_TAB_RESW_PR2_WORK_ORDER.md (current Main App localization rules)
- docs/work-order/2026-10-15_DEVICE_MAXIMUM_PROCESSOR_STATE_WORK_ORDER.md (new Device expander pattern)

**REA evidence:** User-supplied **MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md**, dated 2026-10-18, particularly sections 2, 3, 4, 9 and 10. This report is research input, not an Addon source dependency or proof of vendor redistribution permission.

REA-confirmed facts:

1. MSI Center M's Live Update uses **API_LiveUpdate.dll v1.0.2605.2901**, managed **x86 / .NET Framework 4.8**. The normal Center M orchestration involves UC_LiveUpdate.dll, a component-loader/named pipe, and MSI_Center_M_Server_LiveUpdate.exe. **The cloud API is in API_LiveUpdate.dll**, not the host EXE.
2. MSI production endpoint: **https://webservice.msi.com/production/download**. Requests are HTTPS POST, JSON, one request per category; the DLL computes AWS SigV4 authorization and x-api-key itself.
3. Request JSON has **product_title**, **country_code**, **type**, **os**. Accepted category values: **bios**, **firmware**, **driver**, **utility**. Windows 11 uses **win11**. MSI derives marketing product title from **Win32_ComputerSystemProduct.Name**, stripping any suffix after slash/backslash, and uses a current-culture-derived country code (ko-KR -> kr). **Do not use the board product (MS-1Txx) or Addon's display model string as product_title.**
4. The specific managed method **ApiRequest.GetPostRequest**, token **0x06000236**, RVA **0x17654**, was successfully invoked with default AWS request parameters and an explicit JSON request body in the REA follow-up. The report identifies **CreateRequestPayload** (0x06000237) and **Sign** (0x06000238). The report **does not provide a C#-compilable invocation signature**, full dependent assembly list, OEM installation path or proof that every OEM installation exposes the same binary. Check the exact local assembly/IL signature and dependencies before coding the invocation; do **not** invent signatures.
5. On **2026-10-18**, a controlled request with **product_title = Claw 8 AI+ A2VM, country_code = kr, os = win11** returned arrays of **2 BIOS**, **1 firmware**, **21 drivers**, **7 utilities** (31 catalog records). This is a validated example **for that supplied title**, not a guaranteed response for other models or proof of locally applicable updates.
6. The test with literal **MT-** yielded empty/false responses. It is **not** a model wildcard. Never send a prefix such as MT- or MS-1Txx in place of the exact product title.
7. A **HTTP 200 response of literal false** was observed for some category queries; it is **not an empty successful catalog**. The server also may return arrays or an enclosing SoftwareRecord object. A genuinely empty JSON array is an empty result, not a failure.
8. Response fields include name/title, version/displayVersion, releaseDate, description, size, package metadata, deviceId and various install/link fields. UI only needs **name, display version/version, release date and optional description**, plus category/count. Preserve honest missing-value handling.
9. The catalog has no per-machine applicability guarantee; installed-driver matching, BIOS/firmware heuristics and version comparison are downstream MSI client stages. In particular, the two raw BIOS-category rows include **Intel ME FW Update Tool** and **BIOS**. Do not relabel raw catalog entries as two BIOS flashing updates.
10. The separate MSI Center M **application self-updater** (updater.exe, XML/ZIP manifests) is **out of scope**; its behavior must not be confused with the four Live Update categories.

## 3. Verified Addon code / integration locations (current main as reviewed)

- **src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml**: a ScrollViewer and StackPanel, top DeviceSummaryCard, CenterMStartupCard, then Device controls. The page already uses CommunityToolkit SettingsCard/SettingsExpander and a WinUI InfoBar. Add new UI **only between DeviceSummaryCard and CenterMStartupInfoBar**.
- **src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs**: Activate/Deactivate and device-settings refresh, plus independent Center M startup state. Avoid touching their lifecycle/events. Own only transient catalog presentation and manual-scan state.
- **src/SteamInputAddonforClaw.UI/MainWindow.xaml**: existing Device navigation and page instance. **No navigation changes**.
- **src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw** and **ko-KR/Resources.resw**, **MainUiStrings.cs**: use the existing RESW/x:Uid path. Mirror currently established Device-page localization convention; do not add an independent localization system.
- **src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs**: existing IAddonFrontendControl and FrontendStatusSnapshot.Device. Add one focused **read-only** request/result contract rather than mutating the shared DeviceQuickSettings aggregate (which also feeds Overlay).
- **src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs**, **NamedPipeAddonFrontendClient.cs** and existing server dispatch implementation: add exactly one RPC and increment the protocol version in the established pre-release fail-fast manner. Current version observed while drafting: **86**; re-read at HEAD.
- **src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs**: existing Runtime-owned frontend operations and status capture; add the catalog query facade here or the nearest existing appropriate Runtime service, without controller operation gates.
- **src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawDeviceModels.cs**: exact supported model identity mapping: Claw 7 AI+ A2VM = **MS-1T42**; Claw 8 AI+ A2VM = **MS-1T52**; Claw 8 EX AI+ CG3EM = **MS-1T91**. Use the **existing resolved supported-device identity** as the local eligibility guard, while sourcing the remote marketing title from WMI.
- **src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj** and **src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj**: Runtime/Main UI are **net10 Windows x64**; **do not try to load an x86 .NET Framework 4.8 managed DLL in these x64 processes**.
- **scripts/publish-layout.ps1**, **scripts/verify-publish-assets.ps1** and existing helper/publish integration: ensure any new helper (if needed) is actually deployed with the Runtime and checked by existing release verification, without bundling MSI's proprietary DLL or changing WinUI publish behavior.

The Full1902 Runtime is already elevated; a catalog read must not introduce a second UAC workflow or elevate Center M.

## 4. Implementation approach: real OEM DLL call, not a copied signing implementation

**Required data flow:**

~~~text
Device page / user clicks Check for updates
  -> one focused IAddonFrontendControl RPC
  -> existing elevated Runtime, read-only catalog-query operation
  -> short-lived compatible OEM DLL invocation if necessary
  -> MSI API_LiveUpdate.dll's actual GetPostRequest method
  -> four category POST requests
  -> validate/normalize category results
  -> return typed read-only snapshot to Device UI
~~~

### 4.1 Compatible execution boundary

The verified binary is **x86 / .NET Framework 4.8** while Addon Runtime/Main UI are **x64 / .NET 10**. The preferred narrow solution is **one short-lived x86 .NET Framework 4.8 OEM-catalog helper EXE** owned/launched by Runtime. It must use the **user-installed original MSI DLL**, with the expected method signature obtained from the real DLL/REA evidence; launch asInvoker as a Runtime child. A small worker is justified here by a **real architecture/runtime incompatibility**, not speculative race defense. Do not repurpose CenterMStartupHelper/TdpHelper or introduce a generic helper framework.

Keep the helper limited to one read-only request batch. IPC can be simple bounded stdin/stdout JSON or another already-present narrow process-invocation convention. Exactly one helper invocation for one user check is sufficient; no long-lived service, job queue, watchdog, state manager, cache or background polling. Avoid deadlocks by draining process output while awaiting exit and bounding the request/child lifetime.

**Mandatory implementation reconnaissance** before deciding the final invocation shape:

1. Inspect the **actual MSI-installed API_LiveUpdate.dll** via local REA/IL tools for GetPostRequest signature, default AWS parameter construction, initializer side effects, assembly references and any dependent files; match against the report's version/identity. Document the actual invocation and installed location in the PR description. Do not infer them from method name alone.
2. Resolve a **trusted, installed MSI Center M path**, not a working-directory DLL search or arbitrary user-supplied path. Never take the DLL from the Addon's runtime/UI output unless it is a properly licensed/approved redistribution (not part of this task). Do not persist/copy OEM DLL bytes to app storage.
3. Call only the **catalog request** path. Do **not** call C_Update.Process_Install, MSI component-loader Install/GetProgress, shell installers, self-updater, or the full scan routine if it would download, write inventory files or mutate state. Prefer the previously demonstrated direct request method.
4. Keep MSI-owned signature/key material inside the OEM DLL. Do not print/log raw Authorization, x-api-key, binary credentials, serials, identifiers unrelated to the request, or raw URLs containing sensitive parameters. **No AWS signing implementation or credential extraction in Addon.**
5. Verify product-specific query works with Center M **disabled/not running**, without starting its services or tasks. If DLL is not installed, its invocation is incompatible, or the expected method is unavailable, return **MSI Catalog unavailable** with a clear reason. Do not silently fall back to scraping web pages, inventing response data, or calling a copied-key REST client.

A successful call with an MSI binary in the REA report proves the approach is worth implementing; it **does not itself prove a particular user's installation will contain that DLL**. Treat missing/unsupported OEM DLL as a real supported failure state. Any distribution/licensing constraints remain separate; do not ship MSI proprietary artifacts in this PR.

### 4.2 Identity and request

- First verify Addon's already-resolved exact supported MSI Claw board/model. If unsupported/unknown, present **Unavailable** without querying MSI.
- Obtain actual product title from **Win32_ComputerSystemProduct.Name**, applying the MSI DLL's observed removal of text after slash/backslash. Do not substitute **MSI Claw 8 AI+ A2VM** from the Addon descriptor for the reported **Claw 8 AI+ A2VM** title.
- Determine country from existing Windows culture context using the MSI-observed mapping where available (ko-KR -> kr), and os from Windows 11 (win11). Avoid hard-coding **kr** for all users or imposing a product-title/model-name fuzzy mapping without evidence. Missing/empty title => Unavailable.
- Issue **bios**, **firmware**, **driver**, **utility** queries. Display server-returned catalog data even when the local installed versions are unknown.
- Do not use **MT-** as a wildcard. Do not use the MSI Center M updater patch's BaseBoardProduct mapping to form the remote product_title.

### 4.3 Result contract and failure policy

Use one small typed result contract with:

- overall state: NotChecked (UI initial state only), Checking (UI transient), Completed, PartialFailure, Unavailable, Failed;
- request model title and optionally normalized country/OS for user-facing context (no serial);
- per-category status: Success, Empty, Failed; per-category entry list;
- safe display fields per entry: category, name, version or displayVersion, optional release date, optional description/size if useful;
- checked-at time only for a **completed response attempt**, and a short non-sensitive error kind/message (no embedded MSI credentials or request headers).

Model **HTTP 200 + JSON false** as **Failed**, not an empty catalog. An empty array is **Empty** (success with zero results). One failing category must not erase successful category results, and failed category counts must not be shown as zero. Record exceptions/timeouts as retryable user-visible failures while allowing a new click. An exception in this independent feature must not crash Runtime/UI or alter device ownership. An absent helper/DLL is **Unavailable**, not “No updates”.

Process results must be validated: expected category/type, bounded response sizes, safe strings/lengths, correctly parsed JSON, no executable actions/URLs activated. Log only model/category, elapsed time, counts and sanitized failure kinds as appropriate. Do not store raw catalog responses or persist last-checked state on disk.

The OEM request path has an observed **30-second HTTP timeout**. Ensure helper execution is bounded across four requests with a practical overall timeout and handles cancellation/shutdown. No infinite wait/retry loops. Disable the Check button while a scan is active to avoid duplicate manual requests; a single simple in-flight guard suffices.

## 5. Exact Device UI

One existing-page insert:

~~~text
Device
[Device summary — unchanged]

[ MSI Updates                                    v ]  (collapsed initially)
  Expanded:
    [ Check for updates ]       Catalog only · installed versions not checked
    [ BIOS | Firmware | Drivers | Utilities ]    (native fixed TabView)
    [ Component | Latest version | Release date ]
    [ catalog rows for active category ]
    [ Last checked: yyyy-mm-dd hh:mm / or Not checked yet ]
    [ status/error area as appropriate ]

[MSI Center M — existing]
[TDP — existing]
...
~~~

- Use existing **ctcontrols:SettingsExpander**, **ctcontrols:SettingsCard** and native **TabView/TabViewItem**; no custom tab control, no nested top-level navigation.
- Tab headers are fixed **BIOS / Firmware / Drivers / Utilities**, with counts after a successful scan if desired. Counts are **raw catalog item counts**; do not call them “available updates.”
- Disable add/close/reorder, preserve expansion while switching tabs, and keep the selected category during the current view lifetime.
- Do not start scanning just because the Device page opens, its expander expands, a tab changes, or StateInvalidated fires. Initial state: **Not checked yet**, empty panel guidance.
- While checking, show a small progress indicator and disable Check; after success, show per-tab lists; empty category: **No catalog entries**; failed category: **Catalog request failed**, not “Up to date.”
- Display **latest published version**; never show fabricated installed version, “Update available,” “Up to date,” or an Install button.
- The report's **two BIOS-category entries** (BIOS and Intel ME FW tool) must remain properly named raw catalog records, not grouped as two firmware flashes.
- Use a simple layout that wraps/truncates safely on the actual Device page width and respects DPI/theme. Prefer existing WinUI typography/layout; no horizontal fixed-width table extending beyond the content area.
- Existing header icon style should be reused; do not change spacing, style, or handlers of neighboring Device cards.
- New user-facing strings must use the existing **en-US/ko-KR RESW** convention and fallback behavior. Follow current resource localization policy; do not replace resource infrastructure.

### Recommended UI states

| Condition | Presentation |
| --- | --- |
| Before first click | Not checked yet; empty tabs/guidance |
| Request running | Checking MSI catalog…; button disabled |
| All categories successful | Last checked; raw catalog count and tab rows |
| Some successful | Partial result; clear failure indication only on failing tab(s) |
| Category returned [] | No catalog entries |
| Category returned false/malformed response | Catalog request failed |
| MSI DLL missing/unsupported / device unsupported | MSI catalog unavailable; no dummy data |
| Runtime/transport interrupted | Scan failed; existing Device controls unaffected |

## 6. Test plan — code/CI level

Use existing test projects and conventions. All normal PR CI tests must work **without** an MSI machine, without installed MSI Center M files, and without network access.

Required deterministic test coverage:

1. Product name selection uses **Win32_ComputerSystemProduct.Name**, not board code or Addon marketing descriptor; slash/backslash trimming and missing-title unavailable path.
2. Recognized board scope: MS-1T42, MS-1T52, MS-1T91; unknown/unsupported boards fail closed without server requests. Use the existing board/model resolver, not a second authority.
3. Exact request category values and response mapping; raw entries from a **2/1/21/7** example fixture (counts are example, not hardcoded production expectations). Ensure “Intel ME FW Update Tool” remains a distinct BIOS-category row.
4. Real JSON response shapes: arrays, supported SoftwareRecord wrapper, empty [], literal false, malformed JSON, missing optional version/date, duplicate display names and unknown/extra fields. No synthetic catalog entries.
5. A category failure preserves successful sibling categories; never present HTTP 200 + false as “no updates.”
6. Helper absent/DLL absent, failed process launch, timeout, cancellation, malformed child output, and nonzero exit produce bounded, non-crashing failure results. Verify no credential/header/serial data leaks into returned diagnostics/logging.
7. Frontend method, single RPC dispatcher/client route and wire-version update; test that the UI triggers a query **only on button click**, not tab-switch, page activation, expansion, unrelated Device StateInvalidated, or ordinary QuickSettings refresh. Prevent duplicate requests while busy.
8. RESW strings/XAML compilation and normal UI publish assets; check new helper build/publish presence **only if one is added**, without bundling MSI DLL.

Review existing **scripts/publish-layout.ps1**, **scripts/verify-publish-assets.ps1** and relevant packaging checks; a new helper must appear in the release output for this to work on user machines. Keep OEM binary absent from the published payload.

**Not a PR blocker:** physical MSI DLL availability on the user's handheld, actual MSI server reachability, hardware verification, live 31-record reproduction on the user's Claw, physical BIOS/firmware testing. Those are **product-owner post-merge validation**, not Codex test gates. Build/serialization/UI correctness, mockable failure handling and verified code paths **are** normal PR responsibilities.

## 7. Scope exclusions / review priorities

**Hard exclusions:**

- Installed BIOS/controller firmware/driver/application version reads, MSI ServerHasNewerVersion or applicability logic, any “available updates” badge.
- Any package download, ZIP extraction, CRC or signature verification of update archives, executable execution, installing drivers, updating BIOS/EC/controller firmware, reboot or recovery paths.
- MSI Center M app self-update, startup enablement, MSI Foundation Service manipulation, background Center M process startup.
- Changes to device authority, HidHide, VIIPER, PID1901/PID1902, game routing, TDP, CPU, power mode, battery charge limit, Overlay/QAM.
- A server proxy, embedded static OEM API credentials, custom AWS SigV4 code, cross-model mass queries, persistent cache, compatibility database, generic provider interfaces, duplicate refresh-state manager.
- Invented support for Fast User Switching, RDP or multi-session.

**Review as blockers only when materially reachable:** user-triggered scan hangs/crashes Runtime or UI, silently changes MSI/controller ownership, starts a privileged OEM install path, sends the wrong product identity, leaks credentials, falsely reports failed responses as empty/up-to-date, or ships a nonfunctional helper/reference/publish setup. Do not expand scope to defend against highly improbable scheduler interleavings.

## 8. Suggested commit sequence (still one PR)

1. **OEM read-only catalog transport:** inspect installed DLL signature/dependencies, add compatible narrowly-scoped invocation/helper and parser, deterministic tests.
2. **Runtime/frontend contract:** add focused read-only RPC, exception handling, wire version, transport tests, publish hookup.
3. **Device UI:** collapsed SettingsExpander + four fixed TabView tabs + button + localized text + status/list presentation and UI tests.

Keep one clear catalog-query owner. No on-disk cache and no parallel second implementation of the OEM API.

## 9. Acceptance

- In ordinary Addon/Center-M-disabled operation, the newly visible Device expander supports user-triggered **read-only MSI catalog queries** through the original installed MSI API DLL when available.
- BIOS/Firmware/Drivers/Utilities each display MSI server **raw catalog entries** (name, published/display version and release date where returned), with truthful category-specific failures and last-check status.
- No installed-version comparison, **no update-available claims**, no downloads/installation, no Center M/physical-controller state transitions.
- All touched .NET projects/tests/WinUI resources and packaged outputs build consistently; read-only feature failure leaves all existing Device and Full1902 behavior unchanged.
- Product-owner hardware verification can be performed **after merge**; it is not required to complete the local Codex PR.

**Implementation note:** If inspecting the real installed MSI DLL reveals that GetPostRequest is not safely invocable outside Center M, do **not** replace it with copied credentials or an unverified API client. Keep the UI honest (Unavailable), and identify the specific signature/dependency limitation in the PR for a follow-up decision; never fake a successful result.
