# Work Order — Lazy Materialization for Developer Menu and Developer Diagnostics

> **Date:** 2026-10-06  
> **Status:** Ready for implementation  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main HEAD:** `a237034a3db279748f5c5978634b3fdae7faba9b`  
> **Relevant implementation baseline:** `5e21ca301f1045279dbeed5c19e77d470fabb46c`  
> **Scope:** Main UI developer-surface lifetime + two trivial Runtime developer-probe first-use allocations  
> **Product impact:** No user-facing feature change. Reduce unused Developer/Diag memory footprint for normal users and developers who do not open a specific diagnostic.

---

## 1. Goal

Stop constructing Developer Menu UI and developer-only diagnostic UI for users who never use Developer Menu.

The current Main UI eagerly constructs these XAML controls during `MainWindow.InitializeComponent()` even though they are initially `Collapsed`:

```text
DeveloperPage
VibrationTestPage
ClawSensorProbePage
FanHardwareProbePage
BatteryChargeLimitTestPage
XboxSessionDiagnosticPage
GameInputSystemButtonProbePage
```

That means a normal user with:

```text
DeveloperMenuEnabled = false
```

still pays the XAML/control-tree allocation cost whenever the Main UI is opened.

The target behavior is:

```text
Normal user, Developer Menu never enabled/opened
  -> DeveloperPage is never constructed
  -> every developer child page is never constructed

Developer Menu enabled but never opened
  -> DeveloperPage is still never constructed
  -> every developer child page is still never constructed

Developer Menu opened
  -> construct DeveloperPage only

Developer Menu -> Gyro / Sensor opened
  -> construct ClawSensorProbePage only at first use
  -> do not construct unrelated developer child pages
```

This is a lifetime cleanup, not a navigation redesign.

---

## 2. Authority and product constraints

Follow the active Full1902 authority documents in:

```text
docs/Full 1902 Implementation/
```

In particular:

- do not change PID1902 ownership;
- do not change HidHide authority or reconciliation;
- do not change VIIPER ownership/teardown;
- do not change Sleep / Hibernate / Resume controller lifecycle;
- do not change restart/crash/shutdown fail-close behavior;
- do not introduce a new controller authority;
- do not reintroduce CTW integration.

The existing App UI information architecture remains authoritative for Developer Menu placement:

```text
Settings
  -> Developer Menu
      -> developer-only diagnostics
```

This work order only changes **when developer UI objects are materialized**.

---

## 3. Current behavior that must change

### 3.1 MainWindow eagerly constructs every developer page

`MainWindow.xaml` currently declares the Developer page and all developer child pages directly.

Because they are XAML children of `MainWindow`, `InitializeComponent()` creates them even when:

```text
Visibility = Collapsed
```

`MainWindow.xaml.cs` then initializes and wires all of them during MainWindow construction.

Examples of current eager work include:

```text
DeveloperMenuContent.Initialize(...)
VibrationTestContent.Initialize(...)
ClawSensorProbeContent.Initialize(...)
FanHardwareProbeContent.Initialize(...)
BatteryChargeLimitTestContent.Initialize(...)
XboxSessionDiagnosticContent.Initialize(...)
GameInputSystemButtonProbeContent.Initialize(...)
```

This is unnecessary for the normal product path.

### 3.2 Developer page visibility gating is not object-lifetime gating

`SettingsPage` correctly hides the Developer Menu card when:

```text
DeveloperMenuEnabled == false
```

but that does not prevent the underlying Developer/Diag XAML controls from being constructed.

The new implementation must make the hidden state cheap in both visibility **and allocation**.

---

## 4. Required UI architecture

Keep `MainWindow` as the single navigation/lifetime owner.

Do **not** add:

- `DeveloperPageManager`;
- a page factory interface;
- a navigation service;
- a dependency-injection layer;
- a second developer navigation state machine;
- a generic lazy-page framework.

Use the smallest direct implementation.

### 4.1 Replace static developer page XAML with one host

Remove the direct Developer/Diag page instances from `MainWindow.xaml`.

Use one developer content host, for example:

```xml
<ContentControl
    x:Name="DeveloperContentHost"
    Visibility="Collapsed" />
```

Exact layout/container naming may follow the current XAML structure.

Normal production pages remain unchanged.

### 4.2 MainWindow owns nullable first-use page references

Use direct nullable fields, for example:

```csharp
private DeveloperPage? _developerMenuContent;
private VibrationTestPage? _vibrationTestContent;
private ClawSensorProbePage? _clawSensorProbeContent;
private FanHardwareProbePage? _fanHardwareProbeContent;
private BatteryChargeLimitTestPage? _batteryChargeLimitTestContent;
private XboxSessionDiagnosticPage? _xboxSessionDiagnosticContent;
private GameInputSystemButtonProbePage? _gameInputSystemButtonProbeContent;
```

Do not create them in the `MainWindow` constructor.

### 4.3 Create DeveloperPage only when Developer Menu is actually opened

`OnDeveloperMenuRequested` keeps the existing:

- hidden-menu gating;
- warning dialog;
- “Don't show this warning again” behavior;
- existing `MainNavigationState` transition.

Only after the request is accepted and the app is actually entering Developer Menu should MainWindow ensure the page exists.

Suggested shape:

```csharp
private DeveloperPage GetOrCreateDeveloperMenu()
{
    if (_developerMenuContent is not null)
        return _developerMenuContent;

    var page = new DeveloperPage();
    page.Initialize(_frontend, _bootstrap, () => _prerequisiteSetupInProgress);

    page.BackRequested += ...;
    page.VibrationTestRequested += ...;
    page.SensorProbeRequested += ...;
    page.FanHardwareProbeRequested += ...;
    page.BatteryChargeLimitTestRequested += ...;
    page.XboxSessionDiagnosticRequested += ...;
    page.GameInputSystemButtonProbeRequested += ...;

    return _developerMenuContent = page;
}
```

The exact method name is not important.

The important contract is:

```text
MainWindow creation != DeveloperPage creation
```

### 4.4 Create each child page only when that child is first opened

Do not construct all children when DeveloperPage is created.

Each child should have a small direct `GetOrCreate...` helper that performs its current:

- `new`;
- `Initialize`;
- BackRequested wiring.

Example:

```csharp
private ClawSensorProbePage GetOrCreateClawSensorProbePage()
{
    if (_clawSensorProbeContent is not null)
        return _clawSensorProbeContent;

    var page = new ClawSensorProbePage();
    page.Initialize(_frontend);
    page.BackRequested += (_, _) => ShowPage(_navigationState.ReturnToDeveloperMenu());

    return _clawSensorProbeContent = page;
}
```

Cache the page after first creation.

Do not repeatedly destroy/recreate pages on every navigation transition.

The goal is **zero allocation before first use**, not aggressive page eviction after use.

---

## 5. Navigation behavior must remain unchanged

Do not redesign `MainNavigationState`.

These existing logical pages remain valid:

```text
DeveloperMenu
VibrationTest
ClawSensorProbe
FanHardwareProbe
BatteryChargeLimitTest
XboxSessionDiagnostic
GameInputSystemButtonProbe
```

`ShowPage(...)` should:

1. determine whether a developer page is currently active;
2. deactivate the previous developer child when required;
3. materialize the requested developer page only if needed;
4. place that page in `DeveloperContentHost.Content`;
5. set the host visible only for Developer/Diag pages;
6. preserve normal-page navigation behavior.

Do not materialize a page merely to ask whether it is visible.

For example, replace patterns such as:

```csharp
var wasClawSensorProbe =
    ClawSensorProbeContent.Visibility == Visibility.Visible;
```

with state derived from the existing navigation owner, current hosted content, or nullable reference checks that do not create the page.

The implementation must not introduce a second competing source of navigation truth.

---

## 6. Developer child lifecycle requirements

Lazy creation must **not** weaken any existing teardown behavior.

### 6.1 Gyro / Sensor Probe

Current behavior is correct and must remain so:

```text
enter Gyro / Sensor page
  -> OpenClawSensorProbeAsync
  -> 200 ms UI polling starts

leave page by any navigation route
  -> Deactivate
  -> polling timer stops
  -> pending UI request is cancelled/retired
  -> Runtime probe session is closed
```

This is a hard acceptance requirement.

A developer entering this page has explicitly chosen to run the diagnostic, so the 200 ms polling while the page is active is intentional.

Do not reduce the poll rate or redesign the probe in this PR.

### 6.2 Vibration Test

Preserve:

- page-entry capture/current-state behavior;
- Start-only rumble-loop diagnostic;
- stop-on-leave behavior;
- shutdown cleanup.

If the page was never created, shutdown must not create it solely in order to call cleanup.

Use a nullable guard:

```csharp
if (_vibrationTestContent is not null)
    await _vibrationTestContent.DeactivateAsync();
```

### 6.3 GameInput System Button Probe

Preserve:

- page-entry snapshot;
- Start-only native callback registration;
- stop-on-leave behavior;
- shutdown stop behavior.

Again, shutdown must not materialize the page just to stop a diagnostic that was never started.

### 6.4 Battery Charge Limit Test

Preserve:

- page-entry read;
- validation navigation block while validation is running;
- current mutation behavior.

Update `IsBatteryValidationBlockingNavigation()` so a never-created Battery test page is treated as not blocking.

Example:

```csharp
private bool IsBatteryValidationBlockingNavigation() =>
    _batteryChargeLimitTestContent is { IsValidationRunning: true }
    && ReferenceEquals(DeveloperContentHost.Content, _batteryChargeLimitTestContent);
```

Equivalent simple logic is fine.

### 6.5 Fan Hardware Probe

Do not add a new close RPC or Runtime lifetime manager.

Current Fan Probe behavior is intentionally allowed to keep its small Runtime session object after first use.

There is no continuous idle:

- timer;
- polling loop;
- EC read loop;
- WMI read loop;
- background Task.

The Suspend/Resume test also intentionally needs Runtime state to survive until resume/cleanup.

This PR only makes the **UI page** first-use lazy.

### 6.6 XBOX Active Game Diagnostic

Do not redesign or optimize the Runtime diagnostic in this PR.

The XBOX diagnostic is expected to be promoted/replaced by production XBOX session work and retired.

If that retirement lands before this work order is implemented:

```text
do not reintroduce XboxSessionDiagnosticPage
do not reintroduce XboxGameSessionDiagnostic
simply omit the retired page from this lazy-materialization change
```

If it still exists, make only the UI page first-use lazy and preserve its current force-stop-on-leave behavior.

---

## 7. Frontend invalidation must not materialize hidden developer UI

Current frontend invalidation includes developer-page refresh behavior such as:

```csharp
XboxSessionDiagnosticContent.RequestRefresh();
```

After lazy materialization, an ordinary Runtime invalidation must **never create** a developer page.

Use null checks:

```csharp
_xboxSessionDiagnosticContent?.RequestRefresh();
```

Apply the same principle anywhere a developer page is referenced from:

- Runtime state invalidation;
- MainWindow shutdown;
- navigation guards;
- page refresh helpers.

No background event may implicitly materialize Developer UI.

---

## 8. Runtime first-use cleanup — GameInput probe

The Runtime currently eagerly allocates:

```csharp
private readonly GameInputSystemButtonProbe _gameInputSystemButtonProbe = new();
```

The object is small and does not register GameInput callbacks until Start, so this is not a performance bug.

However, it is developer-only and can be made first-use lazy with no architectural cost.

Change to:

```csharp
private GameInputSystemButtonProbe? _gameInputSystemButtonProbe;
```

Recommended behavior:

```text
CaptureGameInputSystemButtonProbe
  -> create on first developer page use if necessary

StartGameInputSystemButtonProbe
  -> create if necessary
  -> start

StopGameInputSystemButtonProbe
  -> if null, return Unavailable/Stopped-equivalent without creating
  -> otherwise stop existing probe
```

Shutdown:

```text
if probe exists
  -> dispose/stop exactly as today
if probe never existed
  -> do nothing
```

Do not add a lock/state machine solely to defend against theoretical simultaneous first-use RPCs.

The supported product model is one interactive user/session, and existing UI request serialization remains the practical gate.

---

## 9. Runtime first-use cleanup — Intel IGCL probe

The Runtime currently eagerly allocates:

```csharp
private readonly IntelGpuIgclProbe _intelGpuFrequencyProbe = new();
```

Its native IGCL initialization is already lazy:

```text
new IntelGpuIgclProbe
  -> no ControlLib.dll load yet

first Capture/Run
  -> EnsureInitialized
  -> ControlLib.dll
  -> ctlInit
  -> adapter/domain enumeration
```

So this is not currently causing native IGCL activity for normal users.

Still, because the object is developer-only, remove even the unused managed allocation:

```csharp
private IntelGpuIgclProbe? _intelGpuFrequencyProbe;
```

Use first-use creation only from:

```text
CaptureIntelGpuFrequencyProbeAsync
RunIntelGpuFrequencyProbeAsync
```

Shutdown must preserve the existing safety contract:

```text
if probe exists
  -> Dispose()
  -> best-effort restore any probe-owned frequency/power mutation
if probe never existed
  -> do nothing
```

Do not change any IGCL mutation, restore, verification, or native ABI behavior.

Do not initialize IGCL merely because Developer Menu itself was opened.

The Intel card remains passive until the developer presses Refresh/Set/Restore.

---

## 10. Things that must remain always available

Do not confuse developer diagnostics with production features that happen to share hardware helpers.

In particular, do **not** lazy-disable production dependencies required by normal pages/runtime.

Examples include:

- production Battery Charge Limit Runtime/hardware;
- production controller vibration strength client;
- production WING suppression;
- production Full1902 physical input;
- production VIIPER presentation;
- production Device/Profile runtimes.

Only the clearly developer-only objects named in this work order are in scope.

---

## 11. Logging policy

No logging-default change in this PR.

The intended product default is:

```text
LogLevel = Info
```

The older Developer UI text saying “Off by default” is stale if still present.

If this work touches that exact XAML card, correct only the descriptive text so it no longer contradicts the actual product default.

Do not otherwise redesign logging.

### Debug-only diagnostics

Preserve existing Debug behavior:

```text
GameBarStateDiagnosticObserver
  -> created only when Debug is enabled at Runtime startup

WingGameBarDiagnosticProbe
  -> starts only on actual WING/Event88 activity
  -> only in Debug
  -> ~1 second bounded observation

extra rumble/input evidence
  -> Debug-gated/event-driven
```

Do not move those diagnostics behind Developer Menu visibility.

Debug logging itself is an explicit developer diagnostic mode.

---

## 12. Tests

Update/add focused tests around object lifetime and existing teardown contracts.

### 12.1 MainWindow XAML architecture

Add static/UI architecture coverage proving that `MainWindow.xaml` no longer directly instantiates:

```text
DeveloperPage
VibrationTestPage
ClawSensorProbePage
FanHardwareProbePage
BatteryChargeLimitTestPage
XboxSessionDiagnosticPage
GameInputSystemButtonProbePage
```

If XBOX diagnostic has already been retired, omit it from the expected list.

Assert that the developer host exists instead.

### 12.2 First-use creation wiring

Add source/architecture tests consistent with current repository style proving:

```text
DeveloperPage is constructed from MainWindow code on first Developer Menu entry
each child page is constructed only from its corresponding first-use path
normal MainWindow constructor no longer initializes developer pages
```

Do not write brittle tests requiring a particular helper method name if a structural assertion is enough.

### 12.3 No implicit materialization

Cover at least:

```text
frontend invalidation
UI shutdown cleanup
battery navigation guard
```

so those paths use nullable existing page references and do not call a `GetOrCreate...` helper.

### 12.4 Sensor leave stops polling

Preserve or add deterministic coverage that leaving `ClawSensorProbePage`:

```text
stops the DispatcherQueueTimer
retires/cancels pending refresh work
closes the Runtime sensor probe session
```

This is a required lifecycle invariant.

### 12.5 Start-only diagnostics still stop on leave

Preserve existing tests for:

- Xbox360 rumble loop stop on leave;
- GameInput probe stop on leave;
- XBOX session diagnostic force stop on leave, if that diagnostic still exists.

### 12.6 Runtime developer probes

Add simple tests/source assertions that:

```text
GameInputSystemButtonProbe is nullable/lazy
IntelGpuIgclProbe is nullable/lazy
Stop does not construct a missing GameInput probe
shutdown null-checks missing probes
```

Do not add concurrency machinery/tests for pathological first-use races.

---

## 13. Expected files to change

Likely:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
tests/SteamInputAddonforClaw.UiTests/XboxSessionDiagnosticUiTests.cs   if still present/relevant
tests/SteamInputAddonforClaw.Tests/*                                   only where needed for Runtime lazy lifetime
```

Potentially no changes are required in individual Developer child page implementations.

Do not spread this into unrelated files solely to create a generalized lazy navigation mechanism.

---

## 14. Explicit non-goals

Do not include:

```text
XBOX production session promotion
XBOX diagnostic redesign
Fan Runtime session teardown redesign
new CloseFanProbe RPC
Sensor polling-rate changes
Sensor diagnostic redesign
logging default changes
AppLog writer changes
Debug observer redesign
controller ownership changes
HidHide changes
VIIPER changes
PID1902 lifecycle changes
new DI container
new page manager
new navigation service
new generic lazy abstraction
page eviction after first use
multi-session support
RDP / Fast User Switching support
extra locks/epochs/barriers for theoretical first-use races
```

---

## 15. Practical lifecycle policy

This work should follow the project's real-world race/overengineering policy.

Blocking issues are realistic paths such as:

- leaving a Sensor page but polling continues;
- leaving a Start-only diagnostic but native callbacks remain registered;
- shutdown fails to stop a diagnostic that was actually started;
- lazy UI accidentally changes controller ownership or hardware state;
- frontend invalidation unexpectedly creates hidden Developer UI.

Do **not** add complexity for scenarios such as:

- two hypothetical developer-page creation calls interleaving at one instruction;
- simultaneous first-use RPCs that the supported single UI lifecycle does not realistically generate;
- unsupported multi-session/RDP behavior.

The existing owner/gate/teardown structure should remain simple.

---

## 16. Acceptance criteria

The PR is complete when all of the following are true:

1. A normal user who never opens Developer Menu does not construct `DeveloperPage`.
2. A normal user who never opens Developer Menu constructs none of the developer child pages.
3. Merely having `DeveloperMenuEnabled=true` does not eagerly construct Developer UI.
4. Opening Developer Menu constructs only `DeveloperPage`.
5. Opening one diagnostic constructs only that diagnostic child page; unrelated child pages remain uncreated.
6. Once created, pages may remain cached for the lifetime of the Main UI; repeated recreation is not required.
7. Runtime/frontend invalidation does not materialize hidden developer pages.
8. Main UI shutdown does not materialize pages solely to call cleanup.
9. Gyro/Sensor polling still begins only on Sensor page activation.
10. Leaving Gyro/Sensor page still stops its 200 ms polling and closes the Runtime probe session.
11. Vibration/GameInput/XBOX Start-only diagnostics preserve current stop-on-leave behavior.
12. Fan Probe behavior remains unchanged except that its UI page is created only on first use.
13. `GameInputSystemButtonProbe` is not allocated until its developer frontend path is first used.
14. `IntelGpuIgclProbe` is not allocated until its developer frontend path is first used.
15. Intel IGCL native initialization remains first Capture/Run only, never Developer Menu open.
16. Existing Intel probe shutdown restore semantics remain intact when the lazy probe was actually created.
17. XBOX diagnostic retirement work, if already merged, is not reversed or reintroduced.
18. Production `Info` logging remains the default.
19. Full1902 controller lifecycle/ownership behavior is unchanged.
20. Full solution build and relevant tests pass.

---

## 17. Review guidance

A reviewer should request changes if:

- any Developer/Diag page is still directly instantiated by `MainWindow.xaml` without a concrete product reason;
- opening Developer Menu eagerly constructs all child diagnostics;
- a background Runtime invalidation creates a developer page;
- UI shutdown creates a never-used page just to invoke cleanup;
- Sensor polling can survive page exit;
- GameInput callbacks can survive page exit after an actual Start;
- Intel probe lazy conversion loses shutdown restore of a probe-owned mutation;
- production Device/Controller/Profile functionality is incorrectly moved behind developer lazy initialization;
- the implementation introduces a new manager/service/authority solely to implement this small lifetime cleanup.

A reviewer should **not** block the PR because a theoretical simultaneous first-use call could allocate twice unless a realistic supported application path is demonstrated.

The intended implementation is deliberately small:

```text
one MainWindow developer ContentControl
+ nullable cached page fields
+ direct GetOrCreate helpers
+ nullable developer-only Runtime probe fields
```

No broader framework is required.
