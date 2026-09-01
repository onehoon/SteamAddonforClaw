# Work Order — OQ-POC-A: Addon Quick Settings Overlay Window Viability

## Status

Implementation work order for the first Quick Settings Overlay proof-of-concept PR.

This track is intentionally **not numbered as the Full PID1902 PR sequence**.

Use the label:

```text
OQ-POC-A
```

not:

```text
PR4
PR3.5
PR-any-number
```

The Full PID1902 controller-authority sequence continues independently.

Current design authority:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`

Current implementation facts relevant to this POC:

- the existing main frontend is already a separate unpackaged WinUI 3 process;
- its manifest declares `PerMonitorV2` DPI awareness;
- the main UI uses `.NET 10`, WinUI 3, Windows App SDK 2.3.1, `win-x64`, framework-dependent .NET, and Windows App SDK self-contained deployment;
- the solution build includes every project declared in `SteamInputAddonforClaw.slnx`;
- current release/publish layout explicitly publishes Runtime, main UI, and QamHost only;
- `scripts/publish-layout.ps1` does **not** currently publish an Overlay payload.

The project is pre-release. Do not add compatibility layers for an Overlay implementation that does not yet exist.

---

## 1. Goal

Prove the most basic renderer/window assumption before adding Runtime IPC, physical-button integration, controller capture, or Quick Settings feature controls.

Implement a new standalone executable:

```text
SteamInputAddonforClaw.Overlay.exe
```

that, when launched directly for this POC:

1. creates one WinUI 3 window;
2. displays an **opaque left-side panel** immediately;
3. sizes the panel to the selected monitor's current Windows **working area**;
4. does not intentionally cover the taskbar;
5. uses a temporary fixed width expressed in **DIP**, converted using the target monitor's current DPI;
6. is borderless / non-resizable / topmost;
7. is shown without intentionally taking foreground activation from the current game/window;
8. can be closed normally for POC testing.

This PR answers only:

> **Can a normal unpackaged WinUI 3 top-level window provide the required left-side Quick Settings surface on the MSI Claw, with correct DPI/work-area behavior and acceptable foreground/game compatibility?**

Do not solve the later controller-input problem in this PR.

---

## 2. Reference device and DPI contract

The primary POC hardware/design reference is:

```text
Display resolution: 1920 × 1200 physical pixels
Windows scale:      150%
Reference DPI:      144 DPI
Effective desktop:  approximately 1280 × 800 DIP before taskbar/work-area deduction
```

This is a **design reference**, not a hard-coded runtime assumption.

The implementation must also behave correctly when Windows scaling changes.

At minimum, reason and test the geometry for:

```text
100%  →  96 DPI
125%  → 120 DPI
150%  → 144 DPI   [primary hardware baseline]
175%  → 168 DPI
200%  → 192 DPI
```

Do not encode:

```text
1920
1200
150%
144 DPI
fixed taskbar pixels
fixed physical panel width
```

as product constants.

---

## 3. DPI-awareness requirement

The new Overlay executable must be **Per-Monitor V2 DPI aware**.

Use an Overlay-specific manifest with the same DPI-awareness policy already used by the current main UI:

```xml
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
```

Do not rely only on system DPI.

Do not assume the primary monitor's scaling if the target foreground window is on another monitor.

The supported product model remains one Windows user / one interactive session, but using real per-monitor DPI APIs is still the correct and simpler windowing implementation.

---

## 4. Geometry contract

### 4.1 Working area owns the vertical geometry

The window must use the selected monitor's current `rcWork` / working-area bounds.

Conceptually:

```text
X      = WorkArea.Left
Y      = WorkArea.Top
Height = WorkArea.Bottom - WorkArea.Top
```

The bottom of the Overlay must therefore stop at the working-area boundary rather than at the physical display boundary.

Do **not** implement:

```text
Height = 1200
TaskbarHeight = 72
Height = ScreenHeight - hardCodedTaskbarHeight
```

The taskbar may have a different size or edge. `rcWork` is authoritative for this POC.

### 4.2 Width is a DIP design value

For OQ-POC-A only, use a temporary constant:

```text
POC panel width = 400 DIP
```

This is intentionally **not** the final product width.

The final width will be tuned later from real Quick Settings card/icon/control layout.

Convert the width using the target monitor's current DPI:

```text
widthPx = round(400 × dpi / 96)
```

Expected diagnostic examples:

```text
100% /  96 DPI → 400 px
125% / 120 DPI → 500 px
150% / 144 DPI → 600 px
175% / 168 DPI → 700 px
200% / 192 DPI → 800 px
```

These values are expected results of DPI conversion, not separate hard-coded cases.

Clamp only to the actual work-area width if required so a pathological configuration cannot request an invalid window rectangle.

### 4.3 Do not share the main UI's window-size helper

The current main UI has `DpiAwareWindowSize` for its own normal-window dimensions (`1200 × 720 DIP` plus available-size clamping).

Do not move that helper into a shared project merely for this POC.

Its semantics differ from the Overlay contract:

```text
Main UI
→ preferred width + preferred height in DIP

Overlay
→ preferred width in DIP + exact WorkArea height
```

A tiny Overlay-local geometry calculation is simpler and clearer than adding a shared abstraction with mixed semantics.

Do not add `IWindowGeometryProvider`, `DpiManager`, or another generic window-layout framework.

---

## 5. Target-monitor selection

Before the Overlay is shown, capture the current foreground HWND.

Select the target monitor using the foreground window where possible:

```text
GetForegroundWindow()
        ↓
MonitorFromWindow(..., MONITOR_DEFAULTTONEAREST)
        ↓
GetMonitorInfo()
        ↓
rcWork
```

If there is no usable foreground HWND, fall back to the primary/default monitor using the smallest normal Win32 fallback.

Do not build a persistent monitor-selection state manager.

For this POC, perform one fresh resolution at launch/show time.

---

## 6. Target-monitor DPI resolution

The Overlay is PerMonitorV2 aware.

Avoid computing the target width from a cached or system-wide scale.

A simple acceptable sequence is:

```text
create Overlay HWND while hidden
capture target monitor + rcWork
move/place hidden Overlay onto the target monitor if required
query the Overlay HWND's effective DPI after it belongs to that monitor
calculate 400-DIP physical width
apply final WorkArea rectangle
show without activation
```

Use the smallest Win32/WinUI interop that reliably gives the actual target-monitor DPI.

Do not introduce a DPI service abstraction.

If the chosen WinUI/AppWindow API provides an equally reliable current-monitor DPI path under the repository's Windows App SDK version, that is also acceptable.

The required result is the geometry contract, not a mandated wrapper hierarchy.

---

## 7. Window behavior

The POC window should be:

```text
opaque
rectangular
left aligned
borderless
not user-resizable
not shown as a normal taskbar app window
topmost
shown without intentional activation
```

Use minimal HWND/AppWindow interop only.

Likely tools include the existing WinUI pattern for obtaining the HWND plus ordinary Win32 extended styles / placement APIs.

The exact implementation may use the supported API shape available in Windows App SDK 2.3.1, but the resulting behavior must satisfy the acceptance criteria below.

### 7.1 No intentional foreground steal

The key POC requirement is:

```text
foreground game/window before Overlay launch
        ↓
Overlay appears
        ↓
foreground game/window remains foreground
```

Do not call `Window.Activate()` as the normal show mechanism.

Prefer an `AppWindow.Show(false)` / no-activate-capable path where supported, or the equivalent `SetWindowPos(... SWP_NOACTIVATE ...)` behavior.

Use `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW` or the closest supported minimal combination if required.

Do not add focus-proxy windows or hidden owner-window frameworks.

### 7.2 Topmost

The panel should remain visually above an ordinary borderless/windowed game while visible.

Use a normal topmost desktop window.

Do not add injection, graphics hooks, DirectComposition, or swap-chain integration in this POC.

True legacy exclusive fullscreen remains outside the guaranteed POC target.

---

## 8. POC visual content

This is not a visual-design PR.

Keep the XAML intentionally minimal.

Recommended content:

```text
Addon Quick Settings
Overlay POC-A

Resolution / WorkArea
DPI / Scale
Panel DIP / physical width

[ Close POC ]
```

The diagnostic text is useful because the hardware tester can immediately confirm that the window believes it is using the expected DPI/work-area values.

Requirements:

- solid opaque background;
- normal readable text;
- no transparency;
- no acrylic;
- no blur;
- no animation requirement;
- no production card layout;
- no final icons.

A simple Close button is acceptable for terminating the POC process after the no-activation-on-show behavior has been observed.

Clicking the POC window itself may naturally change foreground/focus; the required test is that **showing it programmatically does not intentionally steal foreground**.

---

## 9. Process behavior for OQ-POC-A

Keep process lifetime deliberately simple.

For this PR:

```text
launch SteamInputAddonforClaw.Overlay.exe directly
→ construct window
→ show POC immediately
→ user closes POC
→ process exits
```

Do **not** implement the final warm-hidden lifecycle yet.

Do not start Overlay from `AddonProcessHost` in this PR.

Do not keep it alive in the background after the POC window closes.

The warm hidden process belongs to the next Overlay lifecycle/IPC POC after the window technology is proven.

This separation is intentional: if the panel is not viable over real games, no Runtime/process orchestration work should have been added unnecessarily.

---

## 10. Project structure

Add a new project:

```text
src/SteamInputAddonforClaw.Overlay/
```

Expected minimal files may include:

```text
SteamInputAddonforClaw.Overlay.csproj
app.manifest
Program.cs
App.xaml
App.xaml.cs
OverlayWindow.xaml
OverlayWindow.xaml.cs
OverlayWindowGeometry.cs
WindowInterop.cs            [only if needed]
```

Exact filenames may follow repository conventions.

Do not copy the main UI wholesale.

Only reuse the minimal WinUI bootstrap patterns required for a correct unpackaged application.

### 10.1 Project settings

Match the existing UI's platform baseline unless a concrete build reason requires otherwise:

```text
OutputType                    = WinExe
TargetFramework               = net10.0-windows10.0.26100.0
TargetPlatformMinVersion      = 10.0.26100.0
WindowsPackageType            = None
UseWinUI                      = true
RuntimeIdentifier             = win-x64
SelfContained                 = false
WindowsAppSDKSelfContained    = true
PlatformTarget                = x64
Microsoft.WindowsAppSDK       = 2.3.1
```

Use the custom WinUI bootstrap pattern already established by the main UI if required by the project configuration.

The Overlay does **not** need `CommunityToolkit.WinUI.Controls.SettingsControls` for this empty POC.

Do not add dependencies it does not use.

### 10.2 No Runtime/Frontend project references yet

OQ-POC-A is standalone window validation.

Do not reference:

```text
SteamInputAddonforClaw
SteamInputAddonforClaw.FrontendTransport
SteamInputAddonforClaw.Contracts
SteamInputAddonforClaw.QamHost
```

unless a specific compile-time need is demonstrated.

A pure window POC should not require controller/runtime contracts.

---

## 11. Solution integration

Add the new Overlay project to:

```text
SteamInputAddonforClaw.slnx
```

so ordinary PR CI performs:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release
```

against the new project.

Do not create a second solution.

---

## 12. Release/publish packaging is explicitly out of scope

Current `scripts/publish-layout.ps1` publishes:

```text
Runtime root
ui/
qam/
```

OQ-POC-A must **not** add the Overlay to production release packaging yet.

Do not modify in this PR unless CI literally cannot build the new project without doing so:

```text
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
release.yml
installer/update layout
Runtime process launcher
```

The POC can be launched directly from its normal build output for hardware validation.

Why:

- this is renderer viability, not production delivery;
- Windows App SDK payload duplication/shared-layout decisions belong to the later packaging POC;
- adding release layout now would broaden the PR before the window approach is proven.

The existing publish-layout CI should continue to validate the existing production payload unchanged.

---

## 13. Runtime/controller scope boundary

This PR must not modify controller behavior.

Strictly out of scope:

- `.Overlay` named pipe;
- `CreateOverlayForCurrentUser()`;
- `AddonProcessHost` Overlay process lifecycle;
- warm hidden startup;
- WING integration;
- OEM1 integration;
- Game Bar suppression changes;
- Steam QAM visibility logic;
- QamHost changes;
- Main UI ↔ Overlay mutual exclusion;
- `ToggleAddonQuickSettings()`;
- `ControllerState` navigation;
- DirectInput changes;
- XInput/GameInput reads;
- X360 publisher changes;
- SteamDeck publisher changes;
- pause/neutral/resume;
- release-to-resume latch;
- HidHide changes;
- PID1901/PID1902 changes;
- VIIPER changes;
- TDP/CPU/FPS/Power mutations;
- game profile UI;
- Steam/BPM routing changes.

There should be **zero controller lifecycle behavior change** from merging OQ-POC-A.

---

## 14. Geometry implementation shape

Keep the geometry computation separately testable from Win32 calls.

A reasonable narrow shape is conceptually:

```csharp
internal readonly record struct OverlayRect(int X, int Y, int Width, int Height);

internal static class OverlayWindowGeometry
{
    internal const double PocPanelWidthDip = 400.0;

    internal static OverlayRect Calculate(
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        uint dpi)
    {
        // convert width DIP -> px
        // clamp width to WorkArea width
        // height is exactly WorkArea height
    }
}
```

Exact type names are not mandated.

Do not put HWND/PInvoke calls inside the pure arithmetic function.

This gives deterministic tests without inventing an abstraction layer around Windows.

---

## 15. Automated tests

Add focused deterministic tests for the pure geometry calculation.

The existing test project already targets the same Windows TFM and may reference the Overlay project if needed.

Required cases:

### 15.1 Primary baseline — 150%

For:

```text
DPI = 144
WorkArea = (0, 0) .. (1920, 1120)   [example test rectangle only]
Panel = 400 DIP
```

expect:

```text
Width  = 600 px
Height = 1120 px
X      = 0
Y      = 0
```

The `1120` height here is just deterministic fixture data. It is **not** a product assumption about the real taskbar.

### 15.2 Scale conversion

Verify at least:

```text
 96 DPI → 400 px
120 DPI → 500 px
144 DPI → 600 px
168 DPI → 700 px
192 DPI → 800 px
```

### 15.3 Non-zero work-area origin

Example:

```text
WorkArea.Left = 100
WorkArea.Top  = 40
```

must be preserved exactly.

This protects taskbars on the left/top and non-primary monitor coordinate spaces.

### 15.4 Width clamp

If calculated panel width exceeds available work-area width, clamp safely to the work-area width.

### 15.5 Invalid/zero DPI fallback

Use a simple safe 96-DPI fallback if the pure calculator receives `0`.

Do not build recovery machinery around an invalid test value.

---

## 16. Manual MSI Claw hardware validation

Automated tests cannot validate the reason this POC exists.

### 16.1 Primary test environment

Required primary manual test:

```text
MSI Claw
1920 × 1200
Windows scaling 150%
```

Record:

```text
reported DPI
reported scale
reported WorkArea
calculated physical panel width
foreground HWND/process before Show
foreground HWND/process immediately after Show
```

Expected at 150%:

```text
400 DIP → approximately/exactly 600 physical px under 144 DPI
```

The Overlay vertical bounds must match the current WorkArea rather than the full 1200-pixel screen height.

### 16.2 Foreground behavior

With a representative borderless/windowed game focused:

```text
launch Overlay.exe
```

PASS requires:

- Overlay becomes visible;
- game remains the foreground window immediately after show;
- Overlay does not minimize the game;
- Overlay does not resize the game;
- ordinary game rendering continues;
- Overlay appears topmost above the game.

A mouse/touch click on the Overlay after it is already visible is not part of this no-activation assertion.

### 16.3 Taskbar/work-area behavior

PASS requires:

- top edge aligns with `WorkArea.Top`;
- bottom edge aligns with `WorkArea.Bottom`;
- normal taskbar is not covered;
- no unexplained top/bottom gap.

### 16.4 DPI variation

At minimum manually test:

```text
100%
150%
200%
```

Prefer also:

```text
125%
175%
```

for completeness if convenient.

For every tested scale:

- panel remains approximately the same logical/physical UI size because width is DIP based;
- diagnostic DPI/scale is correct;
- no text clipping;
- WorkArea height remains correct;
- taskbar remains uncovered.

Changing scale may require restarting the standalone POC for this first PR. Dynamic live DPI-change handling while a warm Overlay remains running is not required until the later lifecycle POC.

### 16.5 Game-mode sampling

Test at least:

```text
normal desktop/windowed app
borderless fullscreen game
fullscreen-optimized / modern flip-model game if available
```

Record whether the window is visible and foreground remains with the game.

True legacy exclusive fullscreen is not a blocker for OQ-POC-A unless it is part of an explicitly supported target discovered during testing.

---

## 17. Performance observations

This POC is not yet warm/hidden, so do not optimize background memory lifecycle here.

Still record basic evidence while the POC is visible:

```text
Overlay process working set/private bytes
idle CPU while visible and untouched
any obvious game frametime/VRR disturbance
```

No high-rate timers or animation loops should exist, so idle CPU should be effectively quiet.

Do not add performance instrumentation frameworks. Task Manager / existing diagnostics are sufficient for this POC.

---

## 18. Logging

Logging is optional and should remain tiny.

If added, useful one-shot diagnostic information is:

```text
Overlay startup
foreground HWND
selected monitor bounds
selected WorkArea
DPI
scale
400-DIP converted width
final rectangle
window shown
window closed
```

Do not add:

- rolling controller logs;
- telemetry loop;
- separate logging subsystem;
- raw high-frequency events.

If no existing lightweight Overlay logging dependency exists, visible diagnostic text is sufficient for this POC.

---

## 19. Failure behavior

Keep failure policy simple.

If monitor/DPI/window placement cannot be obtained safely:

```text
log/show a local POC failure if practical
exit Overlay.exe
```

Do not:

- touch Runtime;
- touch controller output;
- fall back to Game Bar;
- modify global Windows display settings;
- add retry supervisors.

Because OQ-POC-A has no controller ownership, an Overlay failure must have zero controller-side effect.

---

## 20. Anti-overengineering constraints

Do not add in OQ-POC-A:

- `OverlayManager` in Runtime;
- `OverlayAuthority`;
- `OverlayStateMachine`;
- `.Overlay` IPC;
- event bus;
- process supervisor/watchdog;
- hidden-process lifecycle;
- DPI manager abstraction;
- monitor manager abstraction;
- rendering interface hierarchy;
- WPF fallback;
- DirectComposition fallback;
- Direct2D fallback;
- injection fallback;
- generalized shared window-geometry package;
- extraction/refactor of the main UI just to share a few lines of DPI math.

The PR should answer a single concrete question with a small amount of code.

---

## 21. Expected changed-file area

Expected additions/changes are approximately:

```text
+ src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
+ src/SteamInputAddonforClaw.Overlay/app.manifest
+ src/SteamInputAddonforClaw.Overlay/Program.cs
+ src/SteamInputAddonforClaw.Overlay/App.xaml
+ src/SteamInputAddonforClaw.Overlay/App.xaml.cs
+ src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
+ src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
+ src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
+ src/SteamInputAddonforClaw.Overlay/WindowInterop.cs        [only if necessary]
~ SteamInputAddonforClaw.slnx
~ tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj   [if project reference required]
+ tests/SteamInputAddonforClaw.Tests/OverlayWindowGeometryTests.cs
```

Avoid unrelated cleanup.

Do not modify Full PID1902 runtime code just because this PR is in the same repository.

---

## 22. Validation before completion

Run the normal repository validation required by the global development policy, including at least:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Also launch the built Overlay executable on Windows and prove that it constructs and shows without a XAML/bootstrap failure.

Do not claim MSI Claw hardware validation from CI.

Hardware evidence must be reported separately.

---

## 23. PR completion criteria

OQ-POC-A is complete when all of the following are true:

- [ ] a dedicated `SteamInputAddonforClaw.Overlay.exe` WinUI 3 project exists;
- [ ] it is part of the main solution build;
- [ ] direct launch shows one opaque left-side POC panel;
- [ ] process is `PerMonitorV2` DPI aware;
- [ ] target monitor is selected from the pre-show foreground window with a simple fallback;
- [ ] vertical geometry comes from the selected monitor WorkArea;
- [ ] taskbar dimensions are not hard-coded;
- [ ] temporary panel width is exactly `400 DIP`, converted using target-monitor DPI;
- [ ] geometry math has deterministic scale/origin/clamp tests;
- [ ] window is borderless and not user-resizable;
- [ ] window is topmost for ordinary desktop/borderless-game use;
- [ ] show path does not intentionally activate the Overlay;
- [ ] close exits the standalone POC normally;
- [ ] no `.Overlay` IPC exists yet;
- [ ] no Runtime process lifecycle change exists;
- [ ] no physical-button mapping change exists;
- [ ] no controller capture/neutralization change exists;
- [ ] no Steam QAM/QamHost change exists;
- [ ] no release/publish-layout change exists;
- [ ] Release build/tests pass.

Hardware POC result should then be recorded separately for:

```text
1920 × 1200 @ 150%   [mandatory primary result]
100% / 200%          [minimum additional scale checks]
representative borderless game foreground behavior
taskbar/work-area geometry
visible process CPU/memory observation
```

---

## 24. Explicit next step after OQ-POC-A

Do not pull the next step into this PR.

If OQ-POC-A hardware validation passes, the next Overlay track should establish the process/transport lifecycle, conceptually:

```text
OQ-POC-B
→ dedicated .Overlay endpoint
→ Runtime-owned Overlay process launcher
→ warm hidden WinUI process
→ Show / Hide / shutdown command path
→ no controller capture yet
```

Only after that should a later POC bind a temporary physical WING/OEM1 action and then add neutral/controller-navigation capture.

This sequencing keeps failures attributable:

```text
POC-A = can the window work?
POC-B = can Runtime own the warm window lifecycle/IPC?
POC-C = can the real physical button toggle it reliably?
POC-D = can controller capture/neutral/release work safely?
```

Do not skip directly from this standalone window POC into a combined controller-lifecycle PR.

---

## 25. Final implementation principle

OQ-POC-A should remain boring.

The desired diff is essentially:

```text
one new WinUI executable
+ one small DPI/WorkArea geometry path
+ one small no-activate/topmost window interop path
+ deterministic geometry tests
+ solution registration
```

No controller authority, no IPC authority, no Game Bar replacement logic, and no Full PID1902 lifecycle change belongs in this PR.

The POC succeeds if it gives reliable hardware evidence for the ordinary WinUI overlay window choice; everything else can then be layered on top in later independently reviewable work.
