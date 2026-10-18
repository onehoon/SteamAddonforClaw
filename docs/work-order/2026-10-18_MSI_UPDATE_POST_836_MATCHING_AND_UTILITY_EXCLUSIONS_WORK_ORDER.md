# Work Order — Post-PR #836 MSI Update Match Accuracy, Intel DSA De-duplication, Diagnostics, and Utility Exclusions

**Date:** 2026-10-18  
**Repository:** `onehoon/SteamAddonforClaw`  
**Target:** current `main` after PR #836 was squash-merged as `793f9588e1f98bbcb96d2cd11130f6ee4261983b`; re-read HEAD immediately before coding  
**Owner:** Local Codex  
**Delivery:** **ONE bounded code PR**, separate commits for (1) DSA/MSI duplicate consolidation, (2) installed-version match accuracy and concise diagnostics, and (3) utility catalog exclusions, plus corresponding tests. The commits do not require individual PRs.  
**Product:** Standalone Full PID1902 MSI Claw Addon (A2VM/CG3EM); one Windows administrator user, one interactive session.  
**Hardware verification:** Conducted by the product owner **after merge**. Hardware testing is **not** a required Local Codex task, acceptance test, or merge blocker.

## 1. Goal and authority

Fix the **two observed usability defects** in the newly merged read-only MSI Update page, **and** implement the user-requested Utility exclusions, **before** proceeding with version-comparison PR2:

1. **Intel GPU / Wi-Fi / Bluetooth / NPU duplicates:** If Intel DSA has *actually resolved the supported physical device*, show its corresponding entry as the sole source of the latest Intel version, rather than also showing the older MSI catalog record for that same Intel driver family. In the real CG3EM result Bluetooth is duplicated.
2. **Missing installed versions for MSI drivers:** Improve local matching where reliable evidence is available. An item with a missing/ambiguous match must continue to show "Installed version unavailable" rather than a fabricated "installed"/"not installed" result. Log **why** the match failed so real hardware results can be diagnosed without widening heuristics indefinitely.
3. **Utilities:** In the Utilities tab, completely exclude exactly `MSI App Player Installer CN`, `Intel Graphics Software`, and `MSI App Player Installer` **before per-entry installed-version matching**. Do not show cards, do not count them, do not match their installed versions. Keep the MSI cloud Utility request and raw category response unchanged.

**Do not implement update-available/up-to-date decisions, downloads, installers, flashing, reboot, a new tab, or a second update manager.** The previously planned **PR2 comparison** remains a separate subsequent PR.

### Mandatory source documents / product guardrails

Read current source and the following documents before coding:

- `docs/Full 1902 Implementation/README.md` — document precedence
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`
- `docs/work-order/2026-10-18_MSI_UPDATE_PAGE_COMPACT_UI_AND_FRESH_ENTRY_WORK_ORDER.md` (active UI/visit policy)
- `docs/work-order/2026-10-18_MSI_UPDATE_INSTALLED_VERSIONS_INTEL_DSA_COMPARISON_PR1_PR2_WORK_ORDER.md` (existing PR1/PR2 split and strict match/source policy)
- `docs/work-order/2026-10-18_MSI_LIVE_UPDATE_CATALOG_READ_ONLY_DEVICE_UI_WORK_ORDER.md` (historical original lookup contract; later page placement supersedes its Device expander)

Reference research:

- Google Drive `MSI_CENTER_M_DRIVER_FIRMWARE_UPDATE_REA_REPORT.md` (2026-10-18), especially sections 3 and 4: https://drive.google.com/file/d/1QYmnJBwEowwaJC017c7skHJDamD9zpEU/view
- CTW-dev reference implementation `release/v0.3.98.0`, `XboxGamingBarHelper/Services/DriverMatchUtil.cs` and `MsiClawDriverCheckService.cs`: **research reference only**, **NO CTW integration and no wholesale fuzzy matching**.
- Uploaded `idsa-en.zip` archive: fixed evidence for Intel DSA 4 supported families (Arc, Wi-Fi, Bluetooth, NPU), **not** a runtime package to bundle.

Code baseline:

- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiUpdateInstalledInventory.cs`: `MsiUpdateInstalledInventory.Capture`, `CaptureSignedDriverVersions`, `IntelDsaHardwareMatcher.Resolve/TryMatchDetectionValue`, `MsiUpdateCatalogComposer.Compose/ResolveMsiInstalledVersion/IsExactMsiDuplicate`.
- `src/SteamInputAddonforClaw/Devices/MSI/LiveUpdate/MsiLiveUpdateCatalogClient.cs`: raw OEM record parsing and `ValidateDeviceId` sanitization.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`: `RequestMsiLiveUpdateCatalogAsync` manual query orchestration and existing `AppLog.Debug("MsiUpdates", ...)` callback for DSA matches.
- `src/SteamInputAddonforClaw.Contracts/Frontend/MsiLiveUpdateContracts.cs`; `src/SteamInputAddonforClaw.UI/Views/MsiUpdatesPage.xaml.cs`; `tests/SteamInputAddonforClaw.Tests/MsiUpdateInstalledVersionsTests.cs`; `tests/SteamInputAddonforClaw.UiTests/MsiLiveUpdateCatalogUiTests.cs`.

**Absolutely unchanged:** OEM x86 helper, signed MSI request, remote request category types/body, Runtime elevation, main UI/overlay RPC protocol, Center M Enabled/Disabled startup ownership, physical PID1901/PID1902, VIIPER, HidHide, WING, routing teardown, suspend/resume, rollback, and stock restoration.

## 2. Actual log evidence: distinguish observations from inferences

Source folder: `GoogleDrive/Addon/Log/1018`.

- [Old v0.1.401 Runtime](https://drive.google.com/file/d/1wvBfFO7vyJlhv9-Bd_ZovR6IN-NRMwNx/view) (`...222028.495...P16316...`): MSI catalog `BiosCount=1; FirmwareCount=1; DriverCount=23; UtilityCount=6`, at approximately **22:22:06** and **22:22:15**.
- [New v0.1.402 Runtime](https://drive.google.com/file/d/1tV7wK4ejTwV4mF3HJ6e5rvLCJiH7l0aa/view) (`...222619.487...P12072...`): at approximately **22:26:52**, **22:28:05**, **22:28:13**, all three queries return `State=Completed; DriverCount=27; IntelDsaDriverCount=4; MsiSourceState=Completed; IntelDsaSourceState=Completed; LocalInventoryWarningCount=0`. All four Intel DSA matches succeed: Bluetooth package `927576`, Graphics `930514`, Wireless `926939`, NPU `927127`.
- **Strong inference, not a captured raw-row proof:** v0.1.402 combined 27 is consistent with MSI's prior 23 plus four DSA rows, i.e. zero visible MSI/Intel de-duplication for that sample. The old and new runs do not contain a per-row raw MSI list; don't assume identical raw server content without obtaining an actual matching breakdown.
- **Log limitation:** `LocalInventoryWarningCount=0` only means collection did not report a source failure. It does **not** mean every catalog row was matched. No per-MSI-row installed-version/match-reason log exists yet.
- `HardwareId=Unknown` on the successful Intel DSA match messages is a logging projection of absent VendorId/ProductId on some PnP enumeration paths, **not** proof of DSA match failure. The current matcher checks HardwareIds/CompatibleIds. Fix that misleading diagnostic only if it is a trivial direct projection; do not modify shared controller enumeration semantics as part of this PR.

The REA reports the following actual *A2VM* OEM MSI driver titles in a separate captured catalog: `Intel Graphics Driver`, `Intel Wireless LAN Driver`, `Intel BlueTooth Driver`, `Intel NPU Driver`, plus Intel chipset/IPF/DTT/ME and other vendors. **These are representative names, not proof of every CG3EM exact title.** Treat any additional title aliases as evidence-based, narrow additions with tests, not broad "Intel" substring matching.

## 3. Fix 1: Intel DSA precedence / MSI duplicate removal

### Current root cause

`MsiUpdateCatalogComposer.Compose` adds the already HWID-matched DSA rows after calling `IsExactMsiDuplicate` against the MSI Driver rows.

Today `IsExactMsiDuplicate` immediately returns `false` if `entry.DeviceId is null`, **even if the MSI title unambiguously identifies Intel Bluetooth/Graphics/Wi-Fi/NPU and DSA has resolved the exact present hardware**. `ValidateDeviceId` also drops a remote `deviceID` not containing a backslash; if the OEM supplies bare `VEN_...`/ `VID_...`, that may be another source of lost match metadata. **The available runtime logs do not show raw MSI device IDs**, so do not assert which missing-ID path occurred without diagnostics.

### Required precedence rules

1. **First:** Retain the existing strict HWID/base-ID/CompatibleId match when the MSI record contains a usable DeviceId. Check that it represents the *same DSA-resolved device* and the *same supported family*. Do not lose the existing 646-detection-value DSA parsing / OS compatibility safeguards.
2. **Second, limited fallback:** For an MSI Driver row **without a usable comparable device identity**, permit duplicate removal only if its **explicit known MSI component family** is Graphics, Intel Wi-Fi, Intel Bluetooth, or Intel NPU **AND** the DSA list contains **exactly one** currently resolved physical device for that family. This is product-scoped deterministic family de-duplication, **not fuzzy installed-version discovery**. If multiple distinct candidate devices resolve the family, retain the MSI row rather than guessing.
3. **Never suppress** an MSI Driver row with a **valid, conflicting device identity** in favor of an unrelated DSA device. A different DEV/PID or contradicting SUBSYS must not be treated as "no identity".
4. **No DSA match/query failure:** Keep the MSI row (with source `MSI`) and do not claim that it represents Intel's freshest driver. Don't remove valid MSI candidates merely because a title contains "Intel".
5. **Keep other Intel device families** such as Intel Chipset, Intel IPF, Intel DTT, Intel Management Engine, Serial IO, Smart Sound and HID Event Filter; non-Intel drivers remain intact.
6. The result is **one DSA-sourced row per supported present device/family**, not a second MSI record for the same component. Source label must remain `Intel DSA`. Do not change four-tab structure or invent "Update available".

For known title mapping, an explicit small family recognizer is preferred to text similarity scores; for example:

~~~csharp
// Example mapping for names observed in MSI REA; verify real CG3EM aliases.
private static IntelDsaComponentFamily? KnownMsiIntelDriverFamily(string name)
{
    return name.Trim().ToUpperInvariant() switch
    {
        "INTEL GRAPHICS DRIVER" => IntelDsaComponentFamily.Graphics,
        "INTEL WIRELESS LAN DRIVER" => IntelDsaComponentFamily.Wireless,
        "INTEL BLUETOOTH DRIVER" => IntelDsaComponentFamily.Bluetooth,
        "INTEL NPU DRIVER" => IntelDsaComponentFamily.Npu,
        _ => null
    };
}
~~~

This is illustrative; include only narrow verified aliases and preserve HWID-first authority. A record with no recognized family, or conflicting DeviceId, is retained as MSI.

**Important distinction:** "strict HWID needed to claim this is my installed device" remains the rule for **local installed-version matching**. The above unique DSA-family fallback is allowed **only to suppress a redundant MSI *server row*** after DSA proved its own physical HWID match; it must not mark a different MSI row as locally installed.

## 4. Fix 2: improve reliable local installed-version mapping

### Current code limitations

`ResolveMsiInstalledVersion(Driver)` only uses `entry.DeviceId` with exact/base-HWID matching, or `entry.InfName`. If both are null, it returns null immediately even if the Windows inventory contains the correct installed driver under an independently verifiable identity. Current WMI is `Win32_PnPSignedDriver`, joined with **present SetupAPI nodes**, with `DeviceId / Class / Provider / DriverVersion / InfName`. Generic MSI package name and PnP device name are **not a guaranteed one-to-one version type**.

Also inspect `MsiLiveUpdateCatalogResultParser.ValidateDeviceId`: it requires `'\\'`. The matcher `TryMatchDetectionValue` can already parse valid bounded bare `VEN_####&DEV_####` / `VID_####&PID_####` patterns; if real OEM data contains this shape, consider allowing it **under the same bounded strict hardware-token validation** rather than silently dropping it. **Do not relax validation to arbitrary free text and do not add test-only support for a format not found in actual vendor evidence.**

### Implementation policy

Use this ordered evidence path for **MSI-sourced Driver** rows, with no new PnP scanner:

1. **Exact present-device evidence:** full instance ID, HWID/CompatibleId and strict base-HWID matching against present PnP, validating optional segments. If a unique device and `DriverVersion` are established, display the installed version with `Exact` confidence.
2. **INF evidence:** exact INF filename **plus** applicable provider/class/component disambiguation where multiple devices share an INF. A unique evidenced version may be shown; multiple device matches sharing one INF/driver version should be labeled **Heuristic** as today, not `Exact` without proof. Multiple disagreeing versions => `Unknown` (null).
3. **Narrow component-family fallback when no usable ID/INF exists:** only for **specific positively identified** MSI driver families with corroborating local **provider + device class/function + distinctive product tokens/device name**, and a unique plausible match. If any of those dimensions is absent, inconsistent or ambiguous, **leave `InstalledVersion=null`**. Label successful family fallback `Heuristic`/estimated. The CTW `DriverMatchUtil` name-token score alone is *not* sufficient.
4. **Packages with multiple drivers** (Chipset, Smart Sound, IPF/DTT or similar): never report one arbitrarily selected PnP `DriverVersion` as the installed *package* version. A multi-component or inconsistent match is Unknown until an actual equivalent version source is proven.
5. **NPU** DSA `OtherDrivers` still has a *package* latest version and a *device driver* installed version: preserve differing version kinds and leave update comparison to PR2. No forced equality.
6. Do not infer `NotInstalled` from absent identifiers or an empty match. Preserve the current Runtime read-only inventory and ordinary WMI/registry failure semantics.
7. Do not expand application/UI contract or frontend wire protocol solely to carry internal diagnostics; only previously supported `InstalledVersion`, `InstalledVersionConfidence`, `VersionKind` and `LatestVersionSource` are needed for cards.

**Bounded scope:** This PR is a meaningful accuracy improvement, **not** a mandate to identify installed versions of all ~23/27 MSI records. A defensible Unknown is preferable to inaccurate installed data. Restrict new family rules to concrete, testable OEM records on supported Claw models. If vendor evidence is missing, document the unresolved name and reason rather than growing an open-ended fuzzy matcher.

## 5. Fix 3: actionable per-entry match diagnostics

On each **explicit manual Check only**, provide enough bounded DEBUG diagnostics to explain *why* an MSI Driver or retained MSI Utility row has no installed version, and why an MSI Intel row was suppressed or retained.

The current Runtime writes only DSA positive-match messages and a summary with `LocalInventoryWarningCount`. Add a lightweight **per-row outcome** at the existing `MsiUpdateCatalogComposer` / `RequestMsiLiveUpdateCatalogAsync` logging boundary:

Suggested stable outcome codes (choose smallest useful subset):

~~~text
MsiDriver: ExactHardwareId | ExactInf | HeuristicFamily | NoUsableIdentity
           | NoMatchingPresentDevice | MissingDriverVersion | AmbiguousMatches
MsiIntelDuplicate: SuppressedByExactIdentity | SuppressedByUniqueDsaFamily
                   | RetainedNoDsa | RetainedConflictingIdentity | RetainedAmbiguousFamily
Utility: MatchedRegistry | MatchedAppx | Unmatched | ExcludedByPolicy
~~~

- Log **category, bounded catalog name, version source, outcome/reason, confidence and number of matching devices/versions** (and optional normalized `VEN/DEV` or `VID/PID` pair **without instance path or serial**). Don't log raw PnP instance IDs, device serials, full registry dumps, OEM response JSON, URLs, credentials or vendor request signing inputs.
- Keep logging **DEBUG** except the pre-existing top-level INFO query summary. Cap work/log lines to the already bounded catalog/category sizes.
- **No extra Windows inventory collection, registry scan or DSA HTTP call** to produce logs. Log the decisions already computed by the matching methods.
- An excluded Utility should never enter `MapMsiEntry` or `ResolveMsiInstalledVersion` (see §6). A simple filtered-count summary is acceptable; there is **no requirement** to log each excluded utility via a matching path.
- Reuse the existing Runtime `AppLog.Debug("MsiUpdates", ...)` style and direct optional callback/hook if needed. **Do not create a diagnostics service, separate public DTO/IPC, JSON report file, new event bus, observer framework or persistent inventory.**

Optional improvement: clarify the existing misleading DSA log `HardwareId=Unknown` when HWID matching has already succeeded, using a safe `MatchReason` or minimal vendor/device token. Do not change the global controller PnP identity parser for logging cosmetics.

## 6. Fix 4: Utilities — exact exclusions before local matching

The only excluded Utility catalog entry names are:

- `MSI App Player Installer CN`
- `Intel Graphics Software`
- `MSI App Player Installer`

This must apply **only** when processing `MsiLiveUpdateCatalogCategory.Utility` in `MsiUpdateCatalogComposer.Compose`.

Filter `source.Entries` **BEFORE** `MapMsiEntry(sourceCategory, entry, inventory)`. Comparison: `entry.Name.Trim()` and `StringComparison.OrdinalIgnoreCase`. Do **not** use broad Contains/StartsWith: `Intel Graphics Command Center` and any other unrelated names must remain.

~~~csharp
private static bool IsExcludedUtility(MsiLiveUpdateCatalogEntry entry)
{
    var name = entry.Name.Trim();
    return name.Equals("MSI App Player Installer CN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Intel Graphics Software", StringComparison.OrdinalIgnoreCase)
        || name.Equals("MSI App Player Installer", StringComparison.OrdinalIgnoreCase);
}

// Inside Compose, before per-entry mapping:
var sourceEntries = sourceCategory == MsiLiveUpdateCatalogCategory.Utility
    ? source.Entries.Where(entry => !IsExcludedUtility(entry)).ToList()
    : source.Entries.ToList();
~~~

The snippet is illustrative. The **shared read-only Registry/AppX inventory snapshot is still collected once**, because other retained Utility rows require it; it is **not a per-excluded-product installed-version lookup**. Do not introduce an expensive selective Windows scanner to exclude those names from the shared OS enumeration. Per-entry matching for all three excluded catalog rows must be skipped entirely.

- Still send the normal OEM `utility` request; preserve its parser, original raw response and MSI source status.
- Excluded rows absent from frontend Utility snapshot and cards; Utility tab count is **filtered visible entries**.
- If all returned Utility rows are excluded, show ordinary empty content / `Utility (0)`, not a query failure. Existing Success-with-zero-entries rendering is already supported; changing state to Empty is optional only if it preserves success/source semantics.
- BIOS/Firmware/Drivers and DSA-only result sets remain unaffected.
- No UI toggle, new strings, new archive, download, package installation or persistent policy setting.

## 7. Tests — ONE PR, deterministic code verification

Use existing fixtures and `MsiUpdateInstalledVersionsTests.cs` / UI tests where relevant. Include the following *reachable* regressions, not synthetic timing-only races.

### DSA/MSI de-dup

1. MSI Bluetooth Driver name with **no DeviceId**, one actual installed Intel Bluetooth device resolved by Intel DSA HWID => **one Intel DSA card**, no redundant MSI Bluetooth card.
2. Equivalent cases for Intel Graphics, Intel Wireless LAN and Intel NPU; the DSA records must have an actual matching present HWID. When the DSA query fails / no physical match, the MSI row must remain.
3. A valid MSI DeviceId matching a *different* device or conflicting `SUBSYS` remains MSI; no name-only hiding in this case.
4. Multiple distinct resolved devices in the same family => do not suppress an ambiguous MSI record by family title alone.
5. `Intel Chipset Driver`, `Intel IPF Driver`, `Intel Management Engine Driver`, `Intel Smart Sound Technology` and non-Intel OEM rows remain visible.

### Installed version accuracy

6. Exact/full/base PnP HWID, same-device `DriverVersion` displays with Exact; validated INF and unique supported known product family display correctly and carry the appropriate Exact/Heuristic confidence.
7. Missing DeviceId/INF and **no corroborating** local device => installed version unknown. A family-name collision, multiple candidates with differing versions, and wrong provider/class => unknown, not a fabricated version.
8. Actual vendor raw HWID shape check: preserve only **validated** bare hardware-token format if such data is observed; do not alter parser permissiveness solely for test fixtures.
9. Existing NPU version-kind distinction, BIOS/controller firmware handling and UWP sentinel behavior remain unchanged.

### Diagnostics

10. Deterministic logs or test-hook records distinguish missing identity from no present match vs multiple matches, and indicate which MSI duplicate was suppressed and by which rule. Never log a serial/instance path/OEM raw response.
11. `LocalInventoryWarningCount=0` does **not** imply row matches; per-row outcomes are separate. No extra OS scan or remote query due to diagnostics.

### Utilities exclusion

12. Given all three excluded Utility names plus `MSI Center M` and `Intel Graphics Command Center`, only the latter two appear. Case and surrounding whitespace are ignored **for exact excluded names only**. Similarly named unrelated utilities remain.
13. Empty after filtering => normal successful empty view; Utility tab count `(0)`, not error or unfiltered count.
14. Demonstrate the filtered entries are absent **before** per-entry local-version resolver invocation via a narrow unit seam/observable result; **do not add a production abstraction/test-only counter** merely for this proof.
15. All four fixed tabs, fresh-entry reset/cancellation, source partial failures, existing transport protocol and no-install policy remain intact.

Run repo-standard .NET build, targeted Runtime + UI test suites, and existing publish checks affected by modified code. No real network/hardware dependency in automated tests. Product-owner on-device tests follow **after merge**.

## 8. PR execution / explicitly out of scope

Suggested commits within **one PR**:

1. `fix(msi-update): consolidate HWID-resolved Intel DSA rows with MSI catalog families`
2. `fix(msi-update): improve evidence-based installed version matching and debug outcomes`
3. `fix(msi-update): omit three utility catalog rows before installed-version resolution`

The Local Codex PR description should report:

- The number of bundled commits / touched source files, and known OEM title aliases actually supported.
- Exact vs heuristic local matches and remaining **Unknown** cases (do not promise all installed versions are discoverable).
- How distinct MSI and Intel source state, duplicate cleanup and Utility count were preserved.
- Evidence from unit/UI test results. Explain that hardware verification is post-merge user role only.

**No overengineering:** one Runtime owner, one existing manual query, existing local inventory and matcher/composer. No additional managers/DB/cache/watchers, retry machinery, mutable controller access, extra PnP authority, additional locks/epochs, speculative races, RDP/multi-session support, future compatibility plugins, new frontend RPCs or generic driver taxonomy engine.

**Next step:** After this one small follow-up PR passes normal code/CI review and merges, proceed with the existing **PR2** strict same-device, same-version-kind comparison. Do not sneak PR2 comparison verdicts into this correction PR.
