# Work Order — WING GameInput System-Button Source Device Diagnostic

## Status

Focused developer diagnostic POC.

Baseline:

~~~text
repository: onehoon/SteamAddonforClaw
branch: main
commit: b9c24d0a796815d486a4e6a3a57264dc56513b92
date: 2026-10-05
architecture: standalone Full1902
~~~

This PR exists to answer one hardware/runtime question:

> When WING is pressed, does Windows GameInput report a Guide/Share system-button transition, and if so, which exact `IGameInputDevice` produced it?

This is diagnostic work only. It must not change WING suppression policy, controller routing, PID1902 ownership, HidHide, VIIPER presentation, MSI Center M authority, Overlay behavior, or the existing Main UI / Overlay / Runtime process separation.

---

## 1. Read before implementation

Read and preserve:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md`
- `docs/work-order/WING_GAMEBAR_FOREGROUND_DIAGNOSTIC_LOGGING_WORK_ORDER_2026-10-05.md`
- `docs/work-order/WING_WINDOWS_GAMEBAR_STATE_DIAGNOSTIC_WORK_ORDER_2026-10-05.md`
- `docs/work-order/FULL1902_MSI_QUICK_SETTINGS_RUNTIME_QUIESCE_WORK_ORDER_2026-10-05.md`

Inspect current main, especially:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/GameBar/GameBarStateDiagnosticObserver.cs`
- `src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs`
- `src/SteamInputAddonforClaw/Wing/WingEventGestureBridge.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml`
- `src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs`

Do not restore CTW integration. CTW is not part of the current product architecture.

---

## 2. Existing evidence and why this POC is needed

Current hardware evidence has already separated the WING behavior into multiple paths.

Observed affected-game shape:

~~~text
physical WING
  |
  +-- keyboard-emulation path
  |     LWIN + G
  |     -> visible in Desktop/BPM
  |     -> current WH_KEYBOARD_LL suppression can consume it
  |     -> missing in the affected foreground game
  |
  +-- MSI Event88 path
  |     -> arrives later
  |     -> Addon WING mapping path
  |
  +-- alternate Windows/Game Bar path
        -> GameBar.IsInputRedirected changes before Event88
        -> no corresponding Win+G hook event
        -> visible Xbox Game Bar can appear
~~~

The current `GameBarStateDiagnosticObserver` proved that Windows Game Bar state can change on the same timeline without the existing keyboard hook seeing a Win+G chord.

The next question is upstream of Game Bar:

~~~text
Does WING become a GameInput system button?
If yes, which device/interface emits it?
~~~

Do not broaden the investigation back into all of Center M.

---

## 3. Microsoft API contract used by this POC

Use the current Microsoft GameInput API as the source of truth.

Primary references:

- `IGameInput::RegisterSystemButtonCallback`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/interfaces/igameinput/methods/igameinput_registersystembuttoncallback
- `GameInputSystemButtonCallback`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/functions/gameinputsystembuttoncallback
- `GameInputSystemButtons`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/enums/gameinputsystembuttons
- `IGameInputDevice::GetDeviceInfo`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/interfaces/igameinputdevice/methods/igameinputdevice_getdeviceinfo
- `GameInputDeviceInfo`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/structs/gameinputdeviceinfo
- `GameInputFocusPolicy`
  - https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/input/gameinput/enums/gameinputfocuspolicy
- GameInput PC/NuGet/runtime notes:
  - https://learn.microsoft.com/en-us/xbox/gdk/docs/features/common/input/overviews/input-nuget

Microsoft's current callback supplies the source `IGameInputDevice*`, timestamp, current system-button state, and previous system-button state.

The device info surface can expose the identity needed by this investigation, including:

~~~text
vendorId
productId
displayName
pnpPath
containerId
deviceId
deviceRootId
supportedInput
supportedSystemButtons
~~~

Do not infer identity only from display name.

---

## 4. Critical focus-policy requirement

The Addon Runtime is normally a background process while the game is foreground.

Default GameInput focus behavior is therefore not sufficient for this diagnostic matrix.

While the probe is explicitly Running, set only:

~~~text
GameInputEnableBackgroundGuideButton
GameInputEnableBackgroundShareButton
~~~

This is required so the Runtime can observe Guide/Share while another game owns foreground focus.

Do NOT set:

~~~text
GameInputExclusiveForegroundGuideButton
GameInputExclusiveForegroundShareButton
GameInputExclusiveForegroundInput
~~~

The probe must never attempt to steal or suppress another process's system-button delivery.

The intended diagnostic policy is:

~~~text
receive a copy in the background
!=
take exclusive ownership
~~~

If the required background system-button policy cannot be established, the probe result is `Unavailable` or `Failed`; do not interpret the absence of callbacks as evidence that WING is not GameInput.

On Stop/dispose, return the diagnostic GameInput policy to `GameInputDefaultFocusPolicy` before releasing the probe's GameInput lifetime when practical.

No production focus policy is introduced by this PR.

---

## 5. Product architecture boundary — do not move GameInput into the UI

This requirement is non-negotiable.

Current product separation remains:

~~~text
SteamInputAddonforClaw.exe
  Runtime / authority / native device work
        |
        | existing typed frontend transport
        v
SteamInputAddonforClaw.UI.exe
  Main UI / Developer pages

SteamInputAddonforClaw.Overlay.exe
  separate Overlay frontend
~~~

The new GameInput object/callback/session belongs only to the Runtime process.

The Main UI must NOT:

- call `GameInputCreate`;
- own `IGameInput`;
- register system-button callbacks;
- hold native GameInput pointers/tokens;
- inspect PnP devices directly;
- become controller or Game Bar authority.

The Overlay must not be modified at all for this POC.

The UI only sends typed Start/Capture/Stop requests through the existing `IAddonFrontendControl` and named-pipe transport.

Do not collapse Main UI, Overlay, and Runtime into one process.
Do not add a direct UI -> native-input shortcut.

---

## 6. Diagnostic ownership

Add one small Runtime-owned diagnostic object.

Suggested name:

~~~text
src/SteamInputAddonforClaw/Diagnostics/GameInputSystemButtonProbe.cs
~~~

It is:

- developer-only;
- explicitly started/stopped;
- read-only with respect to controller state;
- non-authoritative;
- independent of Steam/BPM presentation;
- independent of PID1902 acquisition;
- independent of HidHide;
- independent of VIIPER;
- independent of Event88 delivery;
- independent of the existing keyboard suppression guard.

`AddonProcessHost` remains the composition/lifetime owner.

Do not create:

- GameInputManager;
- SystemButtonAuthority;
- GameBarInputRouter;
- device arbitration state machine;
- background service;
- watchdog;
- polling worker.

One probe object is enough.

---

## 7. GameInput interop implementation rule

The repository currently has no GameInput implementation.

Keep the interop minimal and local to this diagnostic.

Do not add an unrelated general GameInput abstraction layer.

Do not guess Nano-COM vtable layouts or struct layouts from memory.

If direct managed interop is used:

1. derive the exact ABI from the current Microsoft GameInput header/documented version;
2. keep only the declarations required for:
   - GameInput creation;
   - focus policy;
   - RegisterSystemButtonCallback;
   - UnregisterCallback;
   - IGameInputDevice::GetDeviceInfo;
   - fields copied into the diagnostic snapshot;
3. validate x64 struct packing/layout explicitly;
4. keep callback delegates/function pointers rooted for the whole registered lifetime;
5. copy device strings/IDs inside the callback while the native objects are valid;
6. never retain `GameInputDeviceInfo*` memory after the owning GameInput lifetime ends.

Dynamic/native availability failure must be contained as a diagnostic `Unavailable` result.

Do not make normal Addon startup fail because GameInput is missing or the expected API version is unavailable.

Do not add GameInput Redistributable installation, repair, UAC, prerequisite setup, or release-installer changes in this POC.

If the developer machine requires the current Microsoft GameInput Redistributable for this diagnostic, that is a developer-environment prerequisite, not a new production prerequisite in this PR.

---

## 8. Session states

Use a small typed state.

Suggested frontend states:

~~~text
Unavailable
Ready
Running
Stopped
Failed
~~~

A probe session does not need epochs, generations, leases, or a generic diagnostic session framework.

Only one probe may run at a time.

Start while already Running is idempotent and returns the current Running snapshot.

Stop while not Running is idempotent and returns the current terminal snapshot.

---

## 9. Callback registration

On Start:

1. verify Debug logging is enabled;
2. create/acquire the GameInput instance;
3. establish the non-exclusive background Guide/Share focus policy;
4. register one system-button callback with:
   - `device = nullptr` / all devices;
   - button filter = `Guide | Share`;
5. retain the callback token and native lifetime;
6. publish `Running`.

Conceptual native call:

~~~cpp
RegisterSystemButtonCallback(
    nullptr,
    GameInputSystemButtonGuide | GameInputSystemButtonShare,
    context,
    callback,
    &token);
~~~

Do not pre-filter to MSI VID/PID.

The purpose is to discover the source device.

---

## 10. What to capture for each callback

For every callback, copy at minimum:

~~~text
sequence
timestampMicroseconds

currentSystemButtons
previousSystemButtons

Guide:
  pressed transition?
  released transition?

Share:
  pressed transition?
  released transition?

source device:
  vendorId
  productId
  displayName
  pnpPath
  containerId
  deviceId
  deviceRootId
  supportedInput
  supportedSystemButtons
~~~

Also capture whether `GetDeviceInfo` succeeded.

Prefer both human-readable and raw representations where cheap:

~~~text
VID=0x0DB0
PID=0x....
SupportedSystemButtons=Guide|Share
SupportedSystemButtonsRaw=0x...
SupportedInputRaw=0x...
ContainerId={...}
DeviceId=<stable hex rendering>
DeviceRootId=<stable hex rendering>
~~~

Do not invent a second PnP resolver in this PR.

Use GameInput's own `pnpPath` and IDs first.

Do not walk SetupAPI topology unless the result later proves that additional correlation is actually required.

---

## 11. Snapshot contract

Add a compact frontend snapshot for Developer UI.

Suggested shape:

~~~text
FrontendGameInputSystemButtonProbeState

FrontendGameInputSystemButtonProbeSnapshot
  Available
  State
  Status
  EventCount
  LastEvent
~~~

Where `LastEvent` contains the copied identity/event fields needed by the page.

Keep protocol-native fields simple:

- strings;
- integers;
- booleans;
- GUID rendered as string if that avoids platform-specific contract types;
- device IDs rendered as stable hex strings.

Do not expose native pointers, COM identities, callback tokens, or native handles through the frontend contract.

The full event timeline belongs in the application log. The UI needs only current status + last event.

---

## 12. Runtime logging

Use existing `AppLog`.

Require Debug logging for Start.

Suggested category:

~~~text
GameInput.SystemButton
~~~

Lifecycle events:

~~~text
ProbeStartRequested
ProbeStarted
ProbeUnavailable
ProbeFailed
ProbeStopped
~~~

Per callback event:

~~~text
SystemButtonChanged
~~~

Include:

~~~text
Sequence
TimestampUs
CurrentButtons
PreviousButtons
GuidePressed
GuideReleased
SharePressed
ShareReleased
VendorId
ProductId
DisplayName
PnpPath
ContainerId
DeviceId
DeviceRootId
SupportedInput
SupportedSystemButtons
DeviceInfoSucceeded
~~~

This is a low-frequency button event. No debounce/coalescing is needed.

Do not add high-frequency gamepad reading callbacks.

Do not log ordinary gamepad state.

---

## 13. Existing diagnostic timeline correlation

Do not remove or redesign the existing:

- `WinGSuppressionGuard` DEBUG input evidence;
- `WingGameBarDiagnosticProbe`;
- `GameBarStateDiagnosticObserver`;
- Event88 / `Wing.Event` logs;
- `Wing.Action` logs.

The hardware run needs all of them on one timeline.

The desired comparison is:

~~~text
Wing.Input
GameInput.SystemButton
GameBar.State
Wing.Event / Event88
Wing.Action
SteamDeck.SystemButton
~~~

This PR supplements the current diagnostics; it does not replace them.

---

## 14. Stop and teardown

Use `IGameInput::UnregisterCallback` for the registered token so teardown waits for an in-flight callback to leave before native lifetime is released.

Stop sequence:

~~~text
mark stopping / prevent new publication
-> unregister system-button callback
-> restore diagnostic focus policy to Default when practical
-> release native GameInput/device callback lifetime
-> publish Stopped
~~~

Process shutdown must also stop/dispose the probe best-effort.

The probe must never keep the Runtime alive during shutdown.

Do not tie normal controller teardown to this diagnostic.

A probe Stop failure must not trigger controller rollback, PID changes, HidHide changes, or VIIPER teardown.

---

## 15. Developer UI

Add one new Developer Menu card:

~~~text
GameInput System Button Probe
Identify which GameInput device emits Guide/Share when WING is pressed.

[Open]
~~~

Prefer a dedicated developer page because the device identity payload is larger than the current one-line diagnostic cards.

Suggested page:

~~~text
GameInput System Button Probe

State: Ready / Running / Stopped / Failed

[Start] [Stop]

Events: 4

Last event
  Button change: Guide Released
  Timestamp: ...
  VID: 0DB0
  PID: ....
  Name: ...
  PnP Path: ...
  Container ID: ...
  Device ID: ...
  Device Root ID: ...
  Supported Input: ...
  Supported System Buttons: ...
~~~

When not Running, do not poll.

While Running and the page is visible, a simple Developer-only Capture approximately once per second is sufficient.

Do not use global frontend `StateInvalidated` for every Guide press.

On normal page deactivation/navigation away, request Stop.

Because this probe is observation-only and does not mutate hardware/output, do not add a new generic frontend-session disconnect manager solely to defend against an unexpected UI crash. Runtime shutdown still disposes it; a later cleanup can be considered only if real use proves headless probe lifetime is materially harmful.

---

## 16. Frontend RPC contract

Add exactly three methods:

~~~text
CaptureGameInputSystemButtonProbe
StartGameInputSystemButtonProbe
StopGameInputSystemButtonProbe
~~~

to the existing `FrontendRpcMethod`.

Add corresponding methods to `IAddonFrontendControl`.

Wire through the existing path only:

~~~text
Developer page
  -> IAddonFrontendControl
  -> NamedPipeAddonFrontendClient
  -> existing frontend pipe
  -> NamedPipeAddonFrontendServer
  -> InProcessAddonFrontendControl
  -> AddonProcessHost-owned probe
~~~

Do not add another pipe or local socket.

Current reviewed transport version is:

~~~text
FrontendTransportProtocol.CurrentVersion = 49
~~~

Because this PR adds RPC methods and response contracts, bump once:

~~~text
49 -> 50
~~~

Update the version comment and existing protocol assertions accordingly.

Do not change Overlay RPC contracts or shared Quick Settings contracts.

---

## 17. In-process frontend wiring

`InProcessAddonFrontendControl` should receive only narrow delegates/functions from the existing host composition.

Conceptually:

~~~text
Capture -> current probe snapshot
Start   -> host starts/returns probe snapshot
Stop    -> host stops/returns probe snapshot
~~~

Do not give the frontend control direct ownership of `IGameInput`.

Do not move GameInput lifetime into `AddonRuntimeHost`, Main UI, or Overlay.

`AddonProcessHost` remains the process-lifetime orchestrator.

---

## 18. Important non-goal — no suppression implementation

This PR must not attempt to block the discovered Guide path.

Do NOT call or set:

~~~text
GameInputExclusiveForegroundGuideButton
GameInputExclusiveForegroundShareButton
~~~

Do not add:

- Guide swallowing;
- Guide remapping;
- Game Bar closing;
- Game Bar process killing;
- Xbox Game Bar registry/GPO mutation;
- `UseNexusForGameBarEnabled` writes;
- HID output/feature writes;
- MSI firmware changes;
- Event88 changes;
- keyboard-hook changes;
- XInput fallback;
- WING mapping changes.

The only active GameInput focus flags allowed are the non-exclusive background receive flags required to make the diagnostic observable while a game owns foreground focus.

---

## 19. Interpretation rules

### Outcome A — Guide observed from the physical MSI controller/interface

Example:

~~~text
GameInput.SystemButton
GuidePressed
VID=0x0DB0
PID=...
PnpPath=...
ContainerId=<same physical Claw container>
~~~

Interpretation:

> WING has a real Windows GameInput Guide/SystemButton path originating from an MSI physical controller/interface.

Next work should identify the smallest safe suppression/interception policy for that exact source.

Do not implement it in this PR.

### Outcome B — Guide observed from a distinct MSI logical HID/interface

Example:

~~~text
GuidePressed
VID=0x0DB0
PID=<different interface/product>
PnpPath=<distinct MSI interface>
~~~

Interpretation:

> WING is surfaced through a separate logical MSI input interface.

Next work may inspect only that interface's HID/descriptor/driver path.

Do not reopen all Center M RE.

### Outcome C — Game Bar changes but no GameInput callback

This conclusion is valid only if:

~~~text
probe state = Running
background Guide/Share policy established successfully
callback registration succeeded
test action definitely occurred
~~~

Then:

~~~text
GameBar.IsInputRedirected changes
but no GameInput system-button callback
~~~

Interpretation:

> the public GameInput system-button path did not observe the activation.

Next investigation may move to targeted ETW / HID / driver / shell activation evidence.

Do not automatically conclude this from a probe that was Unavailable or failed to establish background policy.

### Outcome D — another controller/device emits Guide

Record the exact device identity.

Do not assume it is WING until the WING press is correlated in the test matrix.

---

## 20. Automated tests

Keep tests focused and deterministic.

Do not build a fake whole GameInput stack merely to achieve broad unit coverage.

### 20.1 State/lifetime tests

Using the smallest natural seam, prove:

~~~text
Start from Ready
-> Running

Start while Running
-> idempotent Running

Stop while Running
-> unregister once
-> Stopped

Stop while Stopped
-> idempotent Stopped
~~~

### 20.2 Callback mapping

Feed representative callback data into the managed snapshot/log mapping seam and prove:

- Guide down transition;
- Guide up transition;
- Share down transition;
- Share up transition;
- current/previous raw masks retained;
- source identity copied into the snapshot.

### 20.3 Identity formatting

Prove stable formatting for:

- VID/PID;
- ContainerId;
- deviceId;
- deviceRootId;
- null/empty displayName;
- null/empty pnpPath.

### 20.4 Failure containment

Prove representative failures produce `Unavailable`/`Failed` and do not invoke any controller/presentation mutation.

At minimum:

- GameInput create unavailable;
- background focus policy failure;
- callback registration failure;
- GetDeviceInfo failure on one callback.

### 20.5 Focus policy contract

Source/contract test should prove the diagnostic contains:

~~~text
GameInputEnableBackgroundGuideButton
GameInputEnableBackgroundShareButton
~~~

and does not contain:

~~~text
GameInputExclusiveForegroundGuideButton
GameInputExclusiveForegroundShareButton
~~~

No production code outside this diagnostic should acquire a GameInput focus policy in this PR.

### 20.6 Frontend transport

Prove:

- protocol version == 50;
- Capture/Start/Stop round-trip;
- default/passive implementation reports Unavailable;
- malformed payload handling remains normal;
- no second transport is introduced.

### 20.7 UI architecture

Prove:

- Developer Menu exposes the GameInput System Button Probe entry;
- page uses only `IAddonFrontendControl`;
- UI project contains no GameInput native interop;
- Overlay project is unchanged by this feature.

Do not add timing torture tests or theoretical callback races.

---

## 21. Expected files

Likely production files:

~~~text
src/SteamInputAddonforClaw/Diagnostics/GameInputSystemButtonProbe.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs

src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/GameInputSystemButtonProbePage.xaml.cs
~~~

If the existing MainWindow/navigation owner needs a small mechanical route for the new Developer page, touch only that existing navigation seam.

Likely tests:

~~~text
tests/SteamInputAddonforClaw.Tests/GameInputSystemButtonProbeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/*TransportContractTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

Do not create a new application project merely for this POC unless implementation proves the documented GameInput ABI cannot be safely represented inside the existing Runtime project. If that happens, stop and document the blocker rather than silently adding a permanent native subsystem.

---

## 22. Explicitly preserve the existing UI/Overlay/Runtime architecture

Review must verify all of the following:

~~~text
Main UI remains external frontend process
Overlay remains external frontend process
Runtime remains native/device authority

GameInput callback exists only in Runtime
Developer UI controls it only through existing frontend pipe
Overlay receives no new GameInput dependency
Overlay shared-surface contracts are unchanged
closing Main UI does not stop the controller Runtime
closing Overlay does not stop the controller Runtime
~~~

This diagnostic must not become an excuse to merge process roles.

---

## 23. Manual hardware matrix

Set application logging to Debug before Runtime startup so the existing `GameBarStateDiagnosticObserver` is also active.

Then open:

~~~text
Settings
-> Developer Menu
-> GameInput System Button Probe
-> Start
~~~

Run four cases.

### A. Desktop

- close Xbox Game Bar first;
- press WING once;
- capture the last GameInput event and logs.

### B. Steam BPM

- enter BPM;
- press WING once;
- capture:
  - Wing.Input;
  - GameInput.SystemButton;
  - GameBar.State;
  - Event88/Wing.Event;
  - Wing.Action.

### C. affected game foreground

Use the current reproducible 007 foreground case.

- ensure the game is foreground;
- press WING once;
- do not alt-tab before the callback window has passed;
- capture the same log categories.

Primary question:

~~~text
Does Guide/Share fire?
If yes, which exact device/interface emitted it?
~~~

### D. another normal game foreground

Repeat with another ordinary game.

This distinguishes a 007-specific context from a general foreground-game GameInput/Guide behavior.

---

## 24. Required evidence export

For each case record:

~~~text
Probe Start result
GameInput system-button event(s)
source device identity
GameBar.State transitions
Win+G hook evidence
Event88 time
WING action time
foreground process evidence
~~~

The important ordering is based on existing application timestamps plus GameInput's callback timestamp.

Do not add a separate clock synchronization subsystem for this POC.

---

## 25. No overengineering

Do not add:

- GameInput service;
- GameInput manager hierarchy;
- generic native-input plugin system;
- device cache;
- SetupAPI topology crawler;
- ETW session;
- HID descriptor parser;
- Guide suppression state machine;
- callback epoch/barrier;
- retry scheduler;
- automatic probe restart;
- persistent diagnostic settings;
- per-game diagnostic policy;
- background probe at every startup.

The user explicitly starts this probe from Developer Menu when evidence is needed.

The real product lifecycle remains protected by the existing Full1902 owners.

---

## 26. Validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet test SteamInputAddonforClaw.slnx -c Debug --no-build

dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release --no-build

git diff --check
~~~

Also verify publish/packaging still succeeds and no accidental GameInput redistributable/installer payload was introduced by this diagnostic-only PR.

---

## 27. Acceptance criteria

- [ ] Developer Menu contains a GameInput System Button Probe.
- [ ] GameInput native lifetime exists only in the Runtime process.
- [ ] Main UI accesses it only through existing `IAddonFrontendControl` / named pipe.
- [ ] Overlay code and Overlay transport contracts are unchanged.
- [ ] Start listens to all devices for Guide + Share.
- [ ] Probe explicitly enables non-exclusive background Guide/Share receipt while Running.
- [ ] Probe never requests exclusive Guide/Share/input focus.
- [ ] Every callback records current/previous system-button state and source device identity.
- [ ] VID/PID/displayName/pnpPath/containerId/deviceId/deviceRootId/supportedInput/supportedSystemButtons are captured when available.
- [ ] GetDeviceInfo failure is visible but does not crash the callback.
- [ ] Stop unregisters the callback before releasing native lifetime.
- [ ] Stop restores the diagnostic focus policy to Default when practical.
- [ ] Runtime shutdown disposes the probe best-effort.
- [ ] No routing, HidHide, PID1901/PID1902, VIIPER, WING mapping, or Game Bar suppression policy changes are made.
- [ ] Existing Win+G, Event88, foreground, and GameBar.State diagnostics remain intact.
- [ ] Frontend transport bumps exactly once from v49 to v50.
- [ ] No second pipe/process authority is introduced.
- [ ] Hardware matrix can unambiguously distinguish "no public GameInput system-button event" from "probe was not able to receive background system buttons."

---

## 28. Final invariant

After this PR:

~~~text
Developer explicitly starts probe
        |
        v
Runtime GameInput observer
  -> non-exclusive background Guide/Share receipt
  -> source IGameInputDevice identity
  -> DEBUG log + compact typed snapshot
        |
        v
existing frontend pipe
        |
        v
Developer page display
~~~

And nothing else changes.

The only question this PR answers is:

> When WING causes the alternate Game Bar behavior, does Windows expose a Guide/Share system-button event, and exactly which GameInput device/interface is responsible?
