# Work Order — Shortcut Foundation PR-C2: NirCmd Fullscreen Screenshot Action

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** 8a4ec8891441229cc5667966e00bcfbfcd9efdb1  
**Depends on:** PR #593, PR #594, PR #595  
**Scope:** built-in screenshot action + NirCmd packaging + screenshot storage preference + Overlay retirement orchestration  
**No Main UI / Overlay tile transport in this PR**

---

## 0. Goal

Add the first Shortcut action that requires top-level application orchestration rather than a simple external process launch:

~~~text
system.screenshot-fullscreen
~~~

Required user behavior:

~~~text
user activates Screenshot Shortcut from Overlay
    ↓
Overlay stops accepting navigation
    ↓
Overlay performs its existing Hide path
    ↓
Runtime receives the existing Hidden acknowledgement
    ↓
Runtime waits for the controller button consumed by Overlay to be released
    ↓
same controller presentation resumes through the existing OQ4 retirement path
    ↓
NirCmd captures the full primary display
    ↓
JPEG is saved to the configured Screenshot folder
~~~

The captured image must not contain the Addon Overlay.

Do not create a second Overlay-dismiss path, screenshot lifecycle manager, capture state machine, or custom GPU capture engine.

---

## 1. Mandatory source review

Read the latest implementation-branch versions before editing:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/SHORTCUT_FOUNDATION_PR_A_DYNAMIC_ACTION_TILE_CONTRACTS_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_B_RUNTIME_PERSISTENCE_WORK_ORDER_2026-09-24.md
docs/work-order/SHORTCUT_FOUNDATION_PR_C_RUNTIME_EXTERNAL_ACTION_ENGINE_WORK_ORDER_2026-09-24.md

src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
scripts/tests/verify-publish-assets.tests.ps1
scripts/tests/report-publish-size.tests.ps1
THIRD_PARTY_NOTICES.md
~~~

Before vendoring NirCmd, verify the current official package and redistribution terms from the official NirSoft pages. Do not infer package contents from this work order.

---

## 2. Locked product decisions

### 2.1 Action identity

~~~text
TypeId        system.screenshot-fullscreen
SchemaVersion 1
Parameters    {}
~~~

The action has no per-tile settings in schema 1.

The Screenshot folder is a global application preference, not a property of each Screenshot tile.

### 2.2 Image format

~~~text
Format: JPEG
Extension: .jpg
~~~

Do not expose PNG selection, an image format selector, JPEG quality slider, or compression-level setting in PR-C2.

NirCmd savescreenshot does not expose a caller-supplied JPEG quality setting in its documented command contract. PR-C2 intentionally uses NirCmd's JPEG encoding/default quality instead of adding a second image re-encoding pipeline.

### 2.3 Filename

Normal filename:

~~~text
yyyyMMdd-HHmmss.jpg
~~~

Example:

~~~text
20260926-173758.jpg
~~~

Use local Windows time.

Do not include game name, Steam AppId, milliseconds, GUID, or device name.

If a file with the exact same-second name already exists, preserve both files with a bounded suffix:

~~~text
20260926-173758.jpg
20260926-173758-01.jpg
20260926-173758-02.jpg
~~~

Probe only -01 through -99. If all names are occupied, fail cleanly. Never overwrite an existing screenshot.

### 2.4 Display scope

PR-C2 fullscreen means:

~~~text
NirCmd savescreenshot
→ full primary display
~~~

Do not use savescreenshotfull because it combines all monitors into one image.

Do not add monitor-selection UI, monitor enumeration, or an Overlay-monitor transport field in this PR. The product is handheld-first and one interactive session is the supported scope.

---

## 3. Capture implementation

Use the already-proven NirCmd path. Do not implement Windows.Graphics.Capture, D3D11 frame pools, GPU readback, GDI BitBlt, custom JPEG encoding, Snipping Tool, Game Bar, or Win+PrintScreen.

The intended invocation is conceptually:

~~~text
nircmdc.exe savescreenshot <absolute-output-path.jpg>
~~~

The Addon owns:
- Screenshot invocation policy;
- Overlay retirement ordering;
- save directory selection;
- filename generation;
- bounded process completion;
- result classification.

NirCmd owns:
- screen acquisition;
- JPEG encoding;
- writing the image file.

---

## 4. Vendor NirCmd as a pinned third-party dependency

Create:

~~~text
src/SteamInputAddonforClaw/Dependencies/NirCmd/
~~~

Use the official NirCmd x64 distribution from NirSoft.

### Redistribution requirement

Do not copy only nircmd.exe or nircmdc.exe.

At implementation time, re-check the official license. The currently documented redistribution terms require redistribution of the complete distribution package without modification when redistributed as freeware.

Therefore:
1. download the official x64 distribution;
2. inspect the actual archive contents;
3. vendor the complete upstream distribution contents unmodified;
4. add a repository-owned PROVENANCE.md beside them;
5. record upstream page, exact download URL, version, retrieval date, archive hash when available, SHA-256 of the executable actually invoked, and the exact upstream file list;
6. add NirCmd to THIRD_PARTY_NOTICES.md.

Do not create an updater or dependency-update workflow for NirCmd in PR-C2.

### Runtime executable

Prefer:

~~~text
Dependencies\NirCmd\nircmdc.exe
~~~

Use the console/error-to-console variant so a capture failure cannot show an unexpected NirCmd message dialog over a game.

If the official x64 distribution being adopted does not contain nircmdc.exe, stop and report the mismatch rather than silently changing the contract.

---

## 5. Publish/package integration

Extend the existing Runtime project/publish contract so the complete pinned NirCmd distribution is present under:

~~~text
artifacts\publish\Dependencies\NirCmd\
~~~

Update as required:

~~~text
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
scripts/report-publish-size.ps1
scripts/tests/report-publish-size.tests.ps1
THIRD_PARTY_NOTICES.md
~~~

Release/CI verification must fail if:
- the Runtime-invoked nircmdc.exe is missing;
- any upstream distribution file recorded as required is missing;
- the invoked executable SHA-256 differs from the pinned provenance value.

Do not fetch NirCmd at application startup or during release packaging. The repository-pinned payload is the release payload.

Add NirCmd as its own publish-size component and include it in the Third-party total. Do not leave Dependencies/NirCmd as Other / Unclassified.

---

## 6. Global Screenshot save-folder preference

The Shortcut collection remains in shortcuts.json.

The Screenshot save-folder preference belongs in existing settings.json.

Add an AppSettings field equivalent to:

~~~csharp
public string? ScreenshotSaveFolder { get; init; }
~~~

Meaning:

~~~text
null
→ use default Screenshot folder

absolute path
→ use that path
~~~

Default effective folder:

~~~csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
    "Screenshots")
~~~

Do not persist the resolved default path merely because the setting was absent.

### Load policy

Parse ScreenshotSaveFolder independently.

Accepted custom value:
- JSON string;
- non-empty/non-whitespace;
- fully-qualified Windows path.

Absent, null, blank, malformed, non-string, or relative value:

~~~text
→ ScreenshotSaveFolder = null
→ only Screenshot folder preference falls back to default
→ unrelated settings remain intact
~~~

Do not turn malformed ScreenshotSaveFolder into a whole-settings-file failure.

### Mutation seam

Add one narrow save-then-publish coordinator method for future Main UI use:

~~~csharp
public bool ChangeScreenshotSaveFolder(string? folder)
~~~

Rules:

~~~text
null / blank
→ normalize to null = default folder

non-null
→ must be fully-qualified
→ otherwise reject without changing Settings
~~~

Do not create the target directory when the setting is edited. Directory creation happens only when taking a Screenshot.

No frontend transport or folder picker in PR-C2.

---

## 7. Focused NirCmd capture adapter

Create one focused Runtime-side type, for example:

~~~text
src/SteamInputAddonforClaw/Shortcuts/NirCmdScreenshotCapture.cs
~~~

It owns only:
- resolving the pinned nircmdc.exe path;
- resolving the output folder supplied by the caller;
- Directory.CreateDirectory at capture time;
- collision-safe filename generation;
- ProcessStartInfo construction;
- bounded NirCmd execution;
- exit/output-file verification.

It must not own Overlay visibility, controller capture, Full1902 presentation, SettingsStore, Shortcut persistence, UI, or frontend transport.

Do not call it ScreenshotManager.

---

## 8. NirCmd process contract

Conceptual ProcessStartInfo:

~~~csharp
var startInfo = new ProcessStartInfo
{
    FileName = nircmdConsolePath,
    UseShellExecute = false,
    CreateNoWindow = true,
    WorkingDirectory = Path.GetDirectoryName(nircmdConsolePath) ?? string.Empty
};

startInfo.ArgumentList.Add("savescreenshot");
startInfo.ArgumentList.Add(outputPath);
~~~

Use ArgumentList so custom folders containing spaces do not require manual quoting.

Do not invoke cmd.exe, PowerShell, or shell execution.

---

## 9. Filename generation

Use:

~~~csharp
localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
~~~

Normal output:

~~~text
<folder>\<timestamp>.jpg
~~~

If already present, probe -01 through -99.

If all 100 names for the same second are occupied, fail. Do not use an unbounded loop, GUID, or persistent sequence.

A helper that accepts DateTime and a file-existence delegate is enough for deterministic tests. Do not create an IClock service.

---

## 10. Capture completion contract

Screenshot is not fire-and-forget.

Use a bounded operation with a 5-second timeout.

~~~text
start exact nircmdc.exe child
    ↓
wait for exact child process exit
    ↓
timeout?
    yes → best-effort kill exact owned child → Failed
    no  → inspect exit code
    ↓
ExitCode == 0?
    no → Failed
    yes
    ↓
output file exists AND Length > 0?
    no → Failed
    yes → Succeeded
~~~

On failure, best-effort remove a zero-length or partial file created by this attempt. Never delete a pre-existing Screenshot.

Do not track NirCmd after the bounded capture ends. Do not create a process supervisor.

A small injected process-run delegate is acceptable for unit tests. Do not add INirCmdProcessService solely for testing.

---

## 11. Shortcut action contract

Add:

~~~csharp
internal const string ScreenshotFullscreen = "system.screenshot-fullscreen";
~~~

Schema 1 requires an empty Parameters object:

~~~json
{}
~~~

If Parameters contains any property in schema 1:

~~~text
Projection → Invalid configuration
Execute    → InvalidConfiguration
~~~

This deliberately prevents accidental per-tile saveFolder/quality settings.

Unknown future Screenshot schema versions remain persisted but Unsupported, consistent with PR-C.

---

## 12. Make the one execution authority async

PR #595 currently has synchronous Execute(TileId) because EXE/PowerShell/URL only need Process.Start.

Screenshot must await:
- Overlay retirement;
- consumed-controller release;
- NirCmd completion.

Do not block async Overlay retirement with GetAwaiter().GetResult().

Migrate the single authoritative Runtime entry to an async form, conceptually:

~~~csharp
internal Task<ShortcutExecutionResult> ExecuteAsync(
    Guid tileId,
    CancellationToken cancellationToken = default)
~~~

ValueTask is acceptable only if it makes the code materially simpler.

There is no production caller of PR #595 Execute yet, so this is the correct time to migrate the seam. Do not keep competing Execute and ExecuteAsync authorities unless a concrete current caller requires both.

The existing EXE/PowerShell/URL actions retain their current behavior and may complete synchronously inside ExecuteAsync.

---

## 13. Screenshot callback into AddonProcessHost

ShortcutRuntime must not own Overlay/controller lifecycle.

Supply one narrow async callback from AddonProcessHost for the Screenshot action, conceptually:

~~~text
Func<CancellationToken, Task<ShortcutExecutionResult>>
~~~

The callback exists because Screenshot requires an application-wide visible-surface transition before capture.

Do not pass save folder, output filename, monitor coordinates, NirCmd arguments, or raw Screenshot parameters from Overlay/Main UI at execution time.

The Runtime/Host re-resolves current authority.

---

## 14. AddonProcessHost orchestration

Add one narrow method conceptually equivalent to:

~~~text
ExecuteFullscreenScreenshotShortcutAsync(token)
~~~

Required ordering:

~~~text
acquire existing _visibleSurfaceTransition
    ↓
if Overlay visible OR Overlay capture active
    ↓
RetireOverlayCaptureUnderTransitionAsync(
    "ShortcutScreenshot",
    surfaceAlreadyGone: false)
    ↓
retirement succeeded?
    no → DO NOT CAPTURE
    yes
    ↓
resolve current effective ScreenshotSaveFolder
    ↓
NirCmdScreenshotCapture.CaptureAsync(...)
    ↓
release _visibleSurfaceTransition
~~~

### Gate rule

Keep existing _visibleSurfaceTransition held through the actual NirCmd capture.

Reason:

~~~text
Hidden ACK
→ if gate were released
→ another Show/Main UI transition could make a surface visible
→ screenshot could include that surface
~~~

The capture is bounded to 5 seconds, so holding the existing visible-surface ordering gate for this explicit user action is acceptable.

Do not create another Screenshot visibility gate.

---

## 15. Reuse the ONE Overlay retirement path

Do not call OverlayProcessController.EnsureHiddenAsync directly as the complete Screenshot policy.

Use existing:

~~~text
RetireOverlayCaptureUnderTransitionAsync
~~~

because it already owns:

~~~text
StopAcceptingNavigation
→ EnsureHiddenAsync
→ Hidden acknowledgement
→ consumed controller release
→ dispose Overlay input router
→ clear Overlay capture fact
→ resume presentation / existing reconcile behavior
~~~

This matters because Screenshot is activated with controller A.

Required behavior:

~~~text
A press selects Screenshot
→ Overlay hides
→ Runtime waits until consumed A is released
→ game-facing presentation resumes
→ Screenshot occurs
~~~

The A press used to invoke Screenshot must not leak into the game.

---

## 16. No speculative screenshot timing machinery

Current Overlay Hide already awaits:

~~~text
Hide animation
→ WindowInterop.Hide
→ command handler completion
→ Hidden state acknowledgement
~~~

Trust that semantic acknowledgement in PR-C2.

Do not add arbitrary Task.Delay, screenshot epochs, frame counters, compositor-barrier managers, retries, or a DwmFlush requirement solely for a theoretical timing window.

If real hardware proves NirCmd can still capture the Overlay after the existing Hidden acknowledgement, fix that proven issue in a focused follow-up.

This follows the project's race/overengineering policy.

---

## 17. Invocation while Overlay is already hidden

The built-in action should remain reusable outside an Overlay-visible state.

If:

~~~text
Overlay hidden
AND no Overlay capture active
~~~

the Host still uses the same visible-surface transition gate, but no retirement work is needed before capture.

Do not show the Overlay merely to retire it.

---

## 18. Projection and outcomes

For a valid Screenshot tile with the production callback wired:

~~~text
Title      persisted Title
StatusText null
State      Neutral
Enabled    true
~~~

Screenshot is one-shot. Do not invent Active/Toggled state.

If the Screenshot callback/capability is absent in an incomplete/test composition:

~~~text
Enabled    false
State      Unavailable
StatusText "Unavailable"
~~~

Reuse ShortcutExecutionOutcome.

### Succeeded

All required steps succeeded:
- Overlay retirement when required;
- NirCmd started;
- exited within timeout;
- exit code 0;
- target .jpg exists;
- target length > 0.

### Unavailable

Examples:
- Overlay could not be proven retired;
- pinned nircmdc.exe is missing;
- Screenshot callback is not composed.

### InvalidConfiguration

- schema 1 Parameters is not empty.

### Failed

Examples:
- save directory creation failed;
- Process.Start returned null;
- ordinary launch failure;
- timeout;
- non-zero exit;
- output missing after zero exit;
- zero-length output.

One Screenshot failure must not poison ShortcutRuntime. EXE/PowerShell/URL remain usable.

---

## 19. Logging/privacy

Screenshot paths may expose usernames or personal folder names.

Do not log:
- full ScreenshotSaveFolder;
- output path;
- command line;
- raw Process exception.Message.

Allowed metadata:
- ActionType;
- Outcome;
- ExceptionType;
- UsedDefaultFolder;
- CollisionSuffixUsed;
- ElapsedMs;
- ExitCode when available.

Use fixed generic FailureMessage strings, for example:

~~~text
"Screenshot is unavailable."
"Screenshot could not be saved."
~~~

---

## 20. Settings tests

Extend current SettingsStore / StartupSettingsCoordinator tests.

Required:
1. absent ScreenshotSaveFolder → null/default mode;
2. JSON null → null/default mode;
3. valid absolute path round-trips;
4. blank string → null/default mode;
5. relative path → null/default mode on load;
6. non-string → null/default mode;
7. malformed ScreenshotSaveFolder does not reset unrelated settings;
8. ChangeScreenshotSaveFolder(valid absolute) saves before publishing;
9. ChangeScreenshotSaveFolder(null/blank) resets to default mode;
10. ChangeScreenshotSaveFolder(relative) rejects and leaves current Settings unchanged;
11. failed SettingsStore.Save does not publish a new folder value where the current test seam supports that pattern.

Do not create destination folders in Settings tests.

---

## 21. NirCmdScreenshotCapture tests

Do not execute real NirCmd in ordinary unit tests.

Required tests:

### Folder resolution
- null setting → Pictures\Screenshots;
- custom absolute path → exact custom path.

### Filename
For fixed local time:

~~~text
2026-09-26 17:37:58
→ 20260926-173758.jpg
~~~

### Collision
Existing base name → -01, then -02, with no overwrite and bounded -99 behavior.

### ProcessStartInfo
Assert:
- FileName = <AppContext base>\Dependencies\NirCmd\nircmdc.exe
- UseShellExecute = false
- CreateNoWindow = true
- WorkingDirectory = NirCmd dependency directory
- ArgumentList[0] = savescreenshot
- ArgumentList[1] = exact absolute .jpg output path

### Result classification
- exit 0 + non-empty file → Succeeded;
- exit non-zero → Failed;
- timeout → Failed;
- process start failure → Failed;
- exit 0 + missing file → Failed;
- exit 0 + zero bytes → Failed.

### Folder failure
Directory creation IOException/UnauthorizedAccessException:
- Failed;
- no NirCmd launch.

Do not assert or log raw path-bearing exception messages.

---

## 22. ShortcutRuntime tests

Update PR #595 tests for unified async TileId execution.

Add:
1. Screenshot schema 1 + empty Parameters projects Neutral/Enabled when callback exists.
2. Screenshot executes callback exactly once.
3. Callback result is returned.
4. Any schema-1 parameter property → InvalidConfiguration; callback not invoked.
5. Screenshot schema 2 → Unsupported.
6. missing callback → Unavailable.
7. already-cancelled token → callback not invoked.
8. EXE/PowerShell/URL regressions remain unchanged after async migration.
9. Screenshot failure does not affect later EXE/URL execution.

Do not add locks/epochs merely because ExecuteAsync now exists. PR-C2 still has no Shortcut document mutation.

---

## 23. Host orchestration tests

Add the smallest practical coverage proving:

~~~text
existing _visibleSurfaceTransition
→ existing RetireOverlayCaptureUnderTransitionAsync
→ only after retirement succeeds call Screenshot capture
~~~

Required:
1. capture is not called if Overlay retirement returns false;
2. capture happens after retirement;
3. visible-surface gate spans both retirement and capture;
4. no direct standalone EnsureHiddenAsync Screenshot policy;
5. no Screenshot-specific Task.Delay;
6. no second Overlay/controller authority.

If fully instantiating AddonProcessHost would require disproportionate test-only architecture, use the repository's existing source-contract-test style instead of adding public test hooks/managers.

---

## 24. Packaging tests

Update publish-verifier and size-report fixtures.

Required:
1. complete pinned NirCmd distribution passes;
2. missing nircmdc.exe fails;
3. missing another required upstream distribution file fails;
4. wrong nircmdc.exe hash fails;
5. NirCmd publishes under Dependencies\NirCmd;
6. NirCmd is its own publish-size component;
7. Third-party total includes NirCmd bytes.

Do not add network access to CI merely to verify NirCmd.

---

## 25. Manual Windows 11 validation

After automated tests, perform a small real-device validation on MSI Claw / Windows 11.

Validate:
- game running;
- Overlay visible;
- trigger Screenshot through a temporary Runtime/test seam until Overlay Shortcut transport exists;
- Overlay is absent from saved JPEG;
- game frame is visible;
- filename matches yyyyMMdd-HHmmss.jpg;
- file is non-empty and opens normally;
- controller A used for invocation does not leak into the game;
- default folder works;
- custom absolute folder works.

Failure validation:
- temporarily remove/rename NirCmd in a test publish layout;
- Screenshot fails safely;
- Runtime stays alive;
- Full1902 controller state is unaffected.

Do not add Defender/antivirus exclusions. If normal supported Windows security quarantines the pinned payload, treat that as a packaging/product blocker and reassess the dependency.

---

## 26. No Main UI / Overlay transport in PR-C2

Do not add:
- folder picker;
- Open folder button;
- Shortcut editor;
- dynamic Shortcut grid;
- ExecuteShortcut pipe/RPC;
- Screenshot toast;
- preview;
- capture sound;
- format selector;
- quality slider.

PR-C2 establishes the Runtime capability only.

---

## 27. Full1902 lifecycle isolation

Screenshot remains a sibling utility capability.

It must not change:
- PID1901/PID1902 authority;
- HidHide;
- DirectInput ownership;
- VIIPER ownership;
- SteamDeck/Xbox360 presentation policy;
- PnP recovery;
- suspend/resume;
- restart/shutdown policy.

The only controller interaction is reuse of the existing Overlay retirement path so captured Overlay input is released safely before Screenshot.

Screenshot failure never triggers controller recovery.

---

## 28. Expected production changes

Likely files:

~~~text
src/SteamInputAddonforClaw/Dependencies/NirCmd/<complete upstream distribution>
src/SteamInputAddonforClaw/Dependencies/NirCmd/PROVENANCE.md

src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj

src/SteamInputAddonforClaw/Shortcuts/ShortcutActionTypeIds.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Shortcuts/NirCmdScreenshotCapture.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

THIRD_PARTY_NOTICES.md

scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
~~~

Tests likely touch ShortcutRuntimeTests, SettingsStoreTests, focused Screenshot capture tests, verify-publish-assets.tests.ps1, and report-publish-size.tests.ps1.

Do not create a new project/assembly for Screenshot.

---

## 29. Explicit non-goals

Do not implement:
- Windows.Graphics.Capture;
- D3D11/D3D12 capture;
- custom GDI capture;
- custom JPEG encoder;
- PNG;
- format/quality selector;
- screenshot history/gallery;
- Steam screenshot integration;
- Game Bar;
- Snipping Tool;
- Win+PrintScreen;
- multi-monitor stitching;
- monitor selection transport;
- region/window capture;
- game-name/AppId folders;
- cloud upload;
- clipboard capture;
- Screenshot worker/service/manager/state machine.

---

## 30. Overengineering guard

Required architecture:

~~~text
ShortcutRuntime
    system.screenshot-fullscreen
        ↓ narrow async callback

AddonProcessHost
    existing visible-surface gate
        ↓
    existing Overlay retirement
        ↓
    NirCmdScreenshotCapture

NirCmdScreenshotCapture
    filename
    folder
    one bounded child process
    result verification
~~~

Do not add ScreenshotManager, ScreenshotCoordinator, ScreenshotLifecycle, CaptureSession, CaptureAuthority, CaptureQueue, ScreenshotWorker, or ImagePipeline.

One focused adapter is sufficient.

---

## 31. Acceptance checklist

### Product
- [ ] system.screenshot-fullscreen schema 1 exists.
- [ ] Parameters must be empty object.
- [ ] output is JPG only.
- [ ] normal filename is yyyyMMdd-HHmmss.jpg.
- [ ] local time is used.
- [ ] same-second collision never overwrites.
- [ ] primary display only in PR-C2.

### Folder
- [ ] ScreenshotSaveFolder is global in settings.json.
- [ ] null means Pictures\Screenshots.
- [ ] absolute custom folder is supported.
- [ ] malformed/relative value falls back feature-locally.
- [ ] no folder per Shortcut tile.
- [ ] no folder UI in this PR.

### Overlay/controller
- [ ] existing _visibleSurfaceTransition reused.
- [ ] existing RetireOverlayCaptureUnderTransitionAsync reused.
- [ ] capture blocked if retirement cannot be proven.
- [ ] consumed controller release happens before Screenshot.
- [ ] no parallel Hide path.
- [ ] no arbitrary Screenshot delay.
- [ ] no new controller lifecycle state.

### NirCmd
- [ ] official x64 distribution pinned.
- [ ] complete upstream distribution vendored unmodified.
- [ ] nircmdc.exe invoked.
- [ ] provenance/version/hash recorded.
- [ ] THIRD_PARTY_NOTICES updated.
- [ ] publish verifier checks pinned files/hash.
- [ ] no Runtime download/update subsystem.

### Execution
- [ ] one async TileId execution authority.
- [ ] ProcessStartInfo uses ArgumentList.
- [ ] UseShellExecute=false.
- [ ] CreateNoWindow=true.
- [ ] 5-second bounded wait.
- [ ] non-zero exit fails.
- [ ] zero exit still requires non-empty output.
- [ ] timeout contained.
- [ ] failure does not poison Shortcut Runtime.

### Scope
- [ ] no Main UI.
- [ ] no Overlay dynamic tile wiring.
- [ ] no frontend RPC/wire version change.
- [ ] no custom capture stack.
- [ ] no Full1902 authority change.

---

## 32. Automated validation

Run at minimum:

~~~text
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore

dotnet test SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-restore

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/report-publish-size.tests.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 -Version 0.1.0 -Configuration Release -PublishDirectory artifacts/publish

powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 -PublishDirectory artifacts/publish

git diff --check
~~~

Run normal GitHub CI on the final head SHA before merge.

---

## 33. Follow-up sequence

### PR-D — Runtime edit contract + Main App Shortcut editor/settings

Expose:
- Shortcut collection editing;
- drag/drop order;
- EXE/PowerShell/URL/Screenshot selection;
- Screenshot Save Folder picker;
- Open Screenshot Folder action.

Main UI remains a client of Runtime-owned persistence.

### PR-E — Dynamic Overlay Shortcut grid

Replace temporary 2x2 shell with:
- actual persisted tiles;
- compact near-square layout;
- title/status;
- controller navigation;
- ExecuteShortcut(TileId).

Screenshot path:

~~~text
A on Screenshot tile
→ Runtime TileId execution
→ existing Overlay retirement
→ consumed A release
→ NirCmd capture
~~~

No Screenshot-specific Overlay lifecycle code belongs in Overlay.exe.

---

## 34. Final principle

PR-C2 adds Screenshot as a real built-in action without turning Screenshot into a second subsystem.

~~~text
one Shortcut TileId authority
+ one existing Overlay retirement authority
+ one global save-folder preference
+ one pinned NirCmd capture adapter
~~~

JPEG, the simple local date-time filename, and reuse of the existing Overlay retirement path are deliberate product decisions.

Keep it that simple.
