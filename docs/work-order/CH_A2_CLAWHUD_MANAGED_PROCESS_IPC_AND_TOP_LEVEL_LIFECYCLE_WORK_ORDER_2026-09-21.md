# Work Order — CH-A2 ClawHUD Managed Process, Control IPC, and Top-Level Lifecycle

**Date:** 2026-09-21  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** `main`  
**Target branch after merge:** `main`  
**Recommended implementation branch:** `feature/clawhud-a2-managed-lifecycle`  
**Reviewed SteamAddon main:** `b68d213424a8a94d5e6f821ac8dd5988603c23e6`  
**Reviewed ClawHUD branch:** `integration/steamaddon`  
**Feature track:** ClawHUD ↔ SteamAddon integration — CH-A2  
**Expected PR count:** 1 focused PR

---

## 0. Purpose

CH-A1 is merged.

SteamAddon can now:

    read the exact ClawHUD Runtime lock
    -> download the immutable pinned Runtime
    -> verify SHA-256
    -> safely extract and validate it
    -> install it under the canonical Addon data root

CH-A2 adds the first real product lifecycle integration:

    AppSettings.ClawHudEnabled
    -> persisted Addon desired state
    -> exact Runtime acquisition
    -> ClawHUD.exe --managed
    -> bounded Control IPC readiness
    -> classify existing ClawHUD instance safely
    -> converge HUD renderer enabled
    -> graceful managed shutdown

CH-A2 is still **not a UI PR**.

Main UI / Overlay / Quick Settings surfaces belong to CH-A3.

---

# 1. Mandatory source review before coding

Read the latest versions on the implementation branch before changing code.

## SteamAddon

At minimum:

    docs/Full 1902 Implementation/README.md
    docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
    docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md

    docs/work-order/CH_A1_CLAWHUD_EXACT_RUNTIME_PIN_AND_ACQUISITION_WORK_ORDER_2026-09-21.md

    src/SteamInputAddonforClaw/ClawHud/ClawHudRuntimeAcquirer.cs
    src/SteamInputAddonforClaw/ClawHud/ClawHudRuntimeLock.cs

    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
    src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs

    src/SteamInputAddonforClaw/Settings/AppSettings.cs
    src/SteamInputAddonforClaw/Settings/SettingsStore.cs
    src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

    src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

    tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
    tests/SteamInputAddonforClaw.Tests/StartupUpdateContractTests.cs
    tests/SteamInputAddonforClaw.Tests/RuntimeProcessApplicationShutdownTests.cs

## ClawHUD producer / protocol authority

Repository:

    onehoon/ClawHUD
    branch: integration/steamaddon

At minimum read:

    src/shared/ClawHudControlProtocol.h
    src/shared/ClawHudControlCodec.cpp

    src/ClawHUD/LaunchMode.cpp
    src/ClawHUD/RuntimeLifecyclePolicy.h
    src/ClawHUD/App.cpp

    src/ClawHUD.Settings/Protocol/ControlProtocol.cs
    src/ClawHUD.Settings/Protocol/ControlCodec.cs
    src/ClawHUD.Settings/Services/RuntimeControlClient.cs

    .github/workflows/Build-SteamAddon-Runtime.yml

The native shared protocol is the wire authority.

The ClawHUD.Settings C# implementation is useful reference code, but SteamAddon must own its own small client and must not reference the WPF Settings project.

---

# 2. Full1902 authority must remain untouched

ClawHUD remains an optional sibling feature.

It is not controller authority.

Current controller ownership remains:

    Center M Enabled
      -> MSI / stock authority
      -> PID1901
      -> no Addon controller DirectInput
      -> no Addon controller HidHide ownership
      -> no Addon VIIPER presentation

    Center M Disabled
      -> SteamAddon mandatory Runtime authority
      -> desired PID1902
      -> Addon DirectInput
      -> deterministic persistent HidHide baseline
      -> canonical VIIPER
      -> exactly one virtual presentation

Presentation remains:

    Steam/BPM inactive -> Xbox360
    Steam/BPM active   -> SteamDeck

Every CH-A2 failure is feature-local.

The following must never change because ClawHUD failed:

    PID mode
    Center M authority
    HidHide
    physical ownership
    VIIPER
    controller startup admission
    presentation mode
    controller recovery
    routing fail-close

---

# 3. Existing SteamAddon startup boundary — preserve it

Current production startup already provides the correct boundary.

The relevant current flow is conceptually:

    RuntimeProcessApplication
      -> RunStartupAsync()
      -> InitializeRuntimeAsync()
      -> NativeMessageLoop begins pumping
      -> StartRuntimeEventWatchers()
      -> StartDeferredRuntimeStartup()

Inside the deferred startup path, current tests enforce:

    TryStartDisabledModeControllerAsync(...)
      -> controller critical attempt finishes / is classified
      -> Volatile.Write(ref _disabledControllerStartupPending, 0)
      -> unrelated background startup work begins

ClawHUD optional bootstrap must be scheduled only **after** that controller-critical boundary.

Do not:

    acquire ClawHUD Runtime in Program.Main
    acquire ClawHUD Runtime in RunStartupAsync
    acquire ClawHUD Runtime in InitializeRuntimeAsync
    start ClawHUD before _disabledControllerStartupPending clears
    make controller startup await ClawHUD
    put PresentMon work inside Full1902 startup

The required dependency direction is:

    Full1902 critical startup
      -> complete / classified
      -> optional ClawHUD reconcile

Never the reverse.

---

# 4. Desired state authority

Add one top-level Addon setting:

~~~csharp
public bool ClawHudEnabled { get; init; }
~~~

Default:

    false

Use an init-only property rather than adding a positional record argument.

Reason:

- avoids churn at existing positional construction sites;
- missing pre-CH-A2 settings naturally resolve to false;
- matches existing AppSettings compatibility style.

The one authority remains:

    settings.json
      -> AppSettings
      -> StartupSettingsCoordinator

Do not add:

    clawhud.json
    companion-settings.json
    registry state
    secondary desired-state store

Nested ClawHUD settings remain owned by ClawHUD itself.

---

# 5. SettingsStore requirements

Update the existing single settings store.

## Load

Read:

    ClawHudEnabled

Rules:

    absent -> false
    true   -> true
    false  -> false
    malformed/non-bool -> false for this feature

Do not reset unrelated settings because this one key is malformed.

## Save

Include:

    ClawHudEnabled

in the existing anonymous save payload.

No schema migration file is required.

---

# 6. StartupSettingsCoordinator requirements

Expose:

~~~csharp
public bool ClawHudEnabled => Settings.ClawHudEnabled;
~~~

Add one narrow persistence mutation, e.g.:

~~~csharp
public void ChangeClawHudEnabled(bool enabled)
~~~

Required semantics:

    if same value:
      accepted no-op

    otherwise:
      next = Settings with { ClawHudEnabled = enabled }
      SettingsStore.Save(next)
      Settings = next

Follow the current **save-then-publish** policy.

This coordinator owns persistence only.

It must not:

    start ClawHUD
    stop ClawHUD
    own IPC
    own Process
    download Runtime

---

# 7. Control protocol implementation

Add a SteamAddon-owned protocol mirror.

Suggested files:

    ClawHud/ClawHudControlProtocol.cs
    ClawHud/ClawHudControlCodec.cs
    ClawHud/ClawHudControlClient.cs

Do not reference:

    ClawHUD.Settings.exe
    ClawHUD.Settings.dll
    ClawHUD Settings project

Port only the stable wire contract.

---

# 8. Protocol v1 constants

Use the exact current native contract.

Frame:

    magic             = CHUD
    protocolVersion   = 1
    headerSize        = 24
    max payload       = 16 KiB
    max string bytes  = 4096
    byte order        = little-endian

Header:

    0   u8[4]  magic
    4   u16    protocolVersion
    6   u16    headerSize
    8   u16    messageKind
    10  u16    operation
    12  u32    requestId
    16  u32    status
    20  u32    payloadSize

MessageKind:

    Request  = 1
    Response = 2

---

# 9. Operations required in CH-A2

CH-A2 only needs these operations directly:

    GetRuntimeInfo      = 1
    GetSettingsSnapshot = 2
    SetHudEnabled       = 11
    RequestShutdown     = 20

The codec may define the full protocol enum now because those values are public wire constants, but do not implement CH-A3 UI behavior around every nested setting yet.

Do **not** send:

    SetStartWithWindows

from SteamAddon Managed mode.

ClawHUD explicitly rejects that operation in Managed mode.

---

# 10. ControlStatus values

Mirror exactly:

    Ok                 = 0
    InvalidFrame       = 1
    UnsupportedVersion = 2
    UnknownOperation   = 3
    InvalidPayload     = 4
    InvalidValue       = 5
    RuntimeUnavailable = 6
    OperationFailed    = 7
    ShuttingDown       = 8

Do not collapse all protocol failures into one fabricated success/failure bool internally.

A compact result is fine, but protocol error vs transport unavailable vs malformed vs timeout must remain distinguishable enough for safe lifecycle classification.

---

# 11. RuntimeInfo contract

Decode:

~~~text
applicationVersion
minimumProtocolVersion
maximumProtocolVersion
launchMode
runtimeState
~~~

LaunchMode:

    Standalone = 1
    Managed    = 2

RuntimeState:

    Starting     = 1
    Ready        = 2
    ShuttingDown = 3

Readiness requires all of:

    response status == Ok
    launchMode == Managed
    runtimeState == Ready
    minimumProtocolVersion <= 1
    maximumProtocolVersion >= 1

And for the currently pinned SteamAddon Runtime:

    applicationVersion == pinned runtime version

---

# 12. Important version-space clarification

Conceptually these remain different product version spaces:

    SteamAddon app version
    ClawHUD standalone version
    SteamAddon ClawHUD Runtime version

Do not collapse those concepts in architecture.

However, the current ClawHUD SteamAddon Runtime build workflow explicitly configures:

~~~text
-DCLAWHUD_VERSION=<SteamAddon Runtime version>
~~~

and ClawHUD reports:

~~~cpp
metadata.applicationVersion = CLAWHUD_VERSION_UTF8;
~~~

Therefore the actual Managed Runtime artifact pinned by CH-A1:

    steamaddon-runtime-v1.0.1

is expected to report:

    GetRuntimeInfo.applicationVersion == "1.0.1"

That exact check is valid for the managed artifact because the producer workflow intentionally binds them.

Do not infer that an independently installed Standalone ClawHUD version must equal the Addon Runtime version.

---

# 13. SettingsSnapshot subset needed in CH-A2

The full protocol snapshot contains:

    StartWithWindows
    HudEnabled
    HudSizeOffset
    HudFont
    VisibilityMode
    Alignment
    BackgroundMode
    BackgroundOpacityPercent
    IntelVrrRangeFixEnabled
    optional IntelVrrLastResult

CH-A2 must decode the full snapshot correctly so framing stays valid.

But CH-A2 lifecycle logic consumes only:

    HudEnabled

for startup convergence.

CH-A3 will expose the other nested settings.

Do not persist those nested values in SteamAddon settings.

---

# 14. Request ID

Use monotonically increasing non-zero `uint` request IDs.

Conceptually:

~~~csharp
uint NextRequestId()
{
    var id = unchecked(++_nextRequestId);
    if (id == 0)
        id = unchecked(++_nextRequestId);
    return id;
}
~~~

Every response must match:

    requestId
    operation
    messageKind Response
    protocol version
    exact payload bounds

Reject malformed or uncorrelated replies.

Do not fabricate settings from malformed IPC.

---

# 15. Named Pipe transport

Current endpoint:

~~~text
\\.\pipe\ClawHUD.Control.<WindowsSessionId>
~~~

For `NamedPipeClientStream`:

~~~text
server = "."
pipeName = "ClawHUD.Control.<Process.GetCurrentProcess().SessionId>"
~~~

The server contract is:

    local only
    current-user protected DACL
    same-session checked
    one pipe instance
    one request / one response / one connection

The client should mirror the existing ClawHUD.Settings transport style:

    open fresh pipe
    connect
    send one frame
    read one frame
    validate
    close

Do not add:

    persistent pipe connection
    heartbeat
    reconnect worker
    polling loop
    subscription channel

---

# 16. IPC timeout

Use a small bounded operation timeout.

The existing ClawHUD.Settings client uses:

    3 seconds

Using the same default is reasonable.

Caller cancellation and internal timeout are different:

    caller cancellation -> propagate/return cancellation to process owner
    IPC budget expiry   -> feature-local timeout/unavailable result

Do not allow an IPC call to block Addon shutdown indefinitely.

---

# 17. RequestShutdown codec difference from ClawHUD.Settings

The current ClawHUD.Settings C# codec intentionally does not encode `RequestShutdown` because the standalone WPF frontend does not send it.

SteamAddon **does require it**.

For SteamAddon:

    RequestShutdown
      -> empty request payload

Successful response:

    status = Ok
    empty payload

The ClawHUD server guarantees the response is delivered before its normal shutdown path is posted.

Add explicit SteamAddon tests for this operation.

Do not copy the ClawHUD.Settings codec limitation blindly.

---

# 18. Process owner

Add one narrow process owner:

    ClawHudProcessController

Its responsibilities:

    ensure desired Managed Runtime is running
    launch verified ClawHUD.exe --managed
    classify startup exit
    perform bounded IPC readiness
    classify an already-running ClawHUD instance
    converge HudEnabled=true after Managed readiness
    request graceful shutdown
    use fallback kill only for a proven child it launched
    expose compact feature-local state

It must not own:

    HTTP download implementation
    ZIP extraction
    AppSettings persistence
    Full1902
    Center M
    HidHide
    VIIPER
    Main UI
    Overlay UI

Reuse:

    ClawHudRuntimeAcquirer

for acquisition.

Do not add a `ClawHudIntegrationManager` above this.

---

# 19. Process launch

Launch exactly the executable returned by the verified CH-A1 acquisition result.

Required:

~~~csharp
var info = new ProcessStartInfo(executablePath)
{
    UseShellExecute = false,
    WorkingDirectory = runtimeDirectory,
};
info.ArgumentList.Add("--managed");
~~~

Do not launch:

    ClawHUD.Settings.exe
    Setup.exe
    installer
    latest discovered Runtime
    Standalone ClawHUD

Do not modify PATH globally.

---

# 20. Managed startup readiness

`Process.Start()` is not readiness.

For a newly launched child:

    Process.Start
      -> bounded IPC retry
      -> GetRuntimeInfo
      -> Managed + Ready + protocol compatible + exact version
      -> GetSettingsSnapshot
      -> if HudEnabled == false:
           SetHudEnabled(true)
           require returned authoritative snapshot.HudEnabled == true
      -> Ready

Use a small bounded readiness loop because the pipe is created late in ClawHUD initialization, after:

    hardware gate
    PresentMon prerequisite
    runtime initialization
    game/telemetry setup
    control bridge
    control pipe startup

A bounded retry around initial pipe availability is appropriate.

This is startup readiness, not a heartbeat.

Do not create a permanent monitoring loop.

---

# 21. Readiness timeout budget

Choose one clear bounded readiness budget.

A reasonable default is:

    total readiness: 15 seconds
    retry interval: 100–250 ms
    individual IPC operation: <= 3 seconds

Do not create exponential backoff or a general retry service.

The process may legitimately spend time in the PresentMon prerequisite path.

If the child exits before readiness, classify its exit immediately.

---

# 22. Existing single-instance behavior

ClawHUD owns:

~~~text
Local\ClawHUD.SingleInstance
~~~

SteamAddon must not create another ClawHUD mutex.

A new Managed launch may exit because another ClawHUD already exists.

The important rule:

    exit 20 / AlreadyRunning
      != automatically safe to kill
      != automatically Managed

After an AlreadyRunning result, classify the existing instance through Control IPC.

---

# 23. Existing-instance classification

After the new child loses the single-instance race, call:

    GetRuntimeInfo

Then classify.

## A. Existing Standalone

If:

    launchMode == Standalone

Result:

    StandaloneConflict

Rules:

    do not adopt
    do not kill
    do not RequestShutdown
    report feature unavailable
    desired Addon setting remains On

The user may close Standalone ClawHUD and retry later.

## B. Existing exact Managed Runtime

If:

    launchMode == Managed
    runtimeState == Ready
    protocol v1 compatible
    applicationVersion == pinned runtime version

Then:

    adopt logically
    GetSettingsSnapshot
    converge HudEnabled=true
    treat Ready

This is the crash/restart recovery path.

No process restart is required.

## C. Existing Managed but different Runtime version

If:

    launchMode == Managed
    protocol compatible
    applicationVersion != pinned runtime version

This is SteamAddon-owned Managed state, so:

    RequestShutdown
    wait boundedly for endpoint/process disappearance
    then launch exact pinned Runtime
    perform normal readiness

Do not require a process PID solely for this path.

Pipe disappearance / inability to reconnect after acknowledged shutdown is sufficient to proceed with exact relaunch.

## D. Cannot safely classify

Examples:

    pipe unavailable
    malformed RuntimeInfo
    protocol incompatible
    unknown launch/runtime enum
    timeout

Result:

    feature-local unavailable

Rules:

    do not kill
    do not guess
    do not change desired setting
    do not affect controller runtime

---

# 24. Do not overbuild adopted-process ownership

For a child SteamAddon itself launched, the controller has a proven `Process` / PID.

For an exact Managed runtime adopted after Addon crash/restart, CH-A2 may not have a `Process` object.

Do not add:

    WMI process scanning
    process-name enumeration
    GetNamedPipeServerProcessId P/Invoke
    process supervisor
    PID journal

solely so the Addon can force-kill an adopted runtime.

For adopted exact Managed state:

    graceful IPC shutdown is enough for CH-A2

If graceful shutdown fails:

    mark feature unavailable
    leave process untouched

For a proven current child only, bounded graceful failure may fall back to:

~~~csharp
process.Kill(entireProcessTree: true)
~~~

Do not force-kill Standalone or unclassified processes.

---

# 25. Managed startup exit codes

Use the Managed-mode exit-code contract already implemented by ClawHUD.

Expected meanings from the integration contract:

    20 AlreadyRunning
    21 UnsupportedHardware
    22 HardwareIndeterminate

    30 PresentMonRebootRequired
    31 PresentMonElevationCancelled
    32 PresentMonMsiMissing
    33 PresentMonInstallTimedOut
    34 PresentMonInstallFailed
    35 PresentMonValidationFailed

    40 RuntimeInitializationFailed
    41 ControlIpcUnavailable

Before implementation, verify the exact current enum/mapping in the ClawHUD integration branch and mirror those numeric values only once.

Do not treat these as Addon process exit codes.

Do not turn any of these into Full1902 recovery.

All are HUD feature failures.

---

# 26. PresentMon ownership

PresentMon is entirely owned by ClawHUD.

SteamAddon must not:

    install PresentMon itself
    validate PresentMon itself
    elevate for PresentMon itself
    share ClawHUD PresentMon state into Full1902 prerequisites

The Managed child may trigger the existing ClawHUD PresentMon bootstrap.

If it fails with a Managed startup exit code:

    desired ClawHudEnabled remains true
    actual state becomes unavailable
    controller remains unaffected

---

# 27. Top-level feature state

Keep the internal state compact.

A small model such as:

~~~text
Disabled
Starting
Ready
Unavailable
StandaloneConflict
~~~

plus a failure reason/details field is enough.

Do not create a large lifecycle state machine.

Important distinction:

    desiredEnabled
    actual runtime state

Examples:

~~~text
desired=false / actual=Disabled
desired=true  / actual=Ready
desired=true  / actual=Unavailable
desired=true  / actual=StandaloneConflict
~~~

A startup failure must never silently persist:

    ClawHudEnabled=false

---

# 28. AddonProcessHost is the integration owner

Wire CH-A2 into the existing:

    AddonProcessHost

Recommended shape:

~~~text
AddonProcessHost
  + ClawHudRuntimeAcquirer
  + ClawHudProcessController
      + ClawHudControlClient
  + existing StartupSettingsCoordinator
~~~

Do not create:

    ClawHudIntegrationManager
    CompanionRuntimeManager
    ExternalProcessSupervisor
    OptionalFeatureCoordinator

The host already owns process-lifetime sibling capabilities.

---

# 29. Composition / construction

Production should create:

    one HttpClient for ClawHUD Runtime acquisition
    one ClawHudRuntimeAcquirer
    one ClawHudProcessController

at the host composition boundary.

Tests should be able to inject narrow seams.

Prefer:

    constructor delegate for process launch
    IClawHudControlClient only if genuinely useful for process-controller tests
    test acquirer delegate / small interface only if required

Do not create an interface for every concrete class automatically.

The goal is testability without abstraction inflation.

---

# 30. User-desired On mutation

CH-A2 should expose one host-level operation that CH-A3 can call later, conceptually:

~~~csharp
Task<ClawHudState> SetClawHudEnabledAsync(
    bool enabled,
    CancellationToken cancellationToken)
~~~

Exact DTO/name may differ.

For **On**:

    reject if Addon process shutdown has started
    -> persist desired true through StartupSettingsCoordinator
    -> acquire exact Runtime
    -> ensure Managed process / adopt
    -> converge HUD renderer enabled
    -> return actual feature state

Persistence happens first.

If runtime start fails:

    persisted desired remains true
    return actual unavailable

This allows later retry without losing user intent.

---

# 31. User-desired Off mutation

For **Off**:

    persist desired false first
    -> stop Managed runtime feature-locally
    -> return actual Disabled / failure status

Do not implement Off as:

    SetHudEnabled(false) and leave ClawHUD.exe running

Top-level Off means:

    no SteamAddon-managed ClawHUD process should remain

Use:

    RequestShutdown
    -> bounded wait
    -> if this is a proven child and still alive:
         Kill(entireProcessTree: true)
    -> clear tracked process/session state

For adopted Managed runtime without proven child ownership:

    RequestShutdown
    -> bounded confirmation
    -> no force kill if confirmation fails

---

# 32. Managed Ready HUD convergence

After readiness:

    GetSettingsSnapshot

If:

    HudEnabled == true

then ready.

If:

    HudEnabled == false

send:

    SetHudEnabled(true)

and require:

    response status == Ok
    returned authoritative SettingsSnapshot.HudEnabled == true

Do not assume the request succeeded just because the pipe write succeeded.

Do not fabricate a local HUD-enabled flag.

The returned ClawHUD snapshot is authoritative for the nested setting.

---

# 33. Why SetHudEnabled(true) persists in ClawHUD

Current ClawHUD semantics persist its own `HudEnabled` nested preference.

That means SteamAddon Managed readiness may leave ClawHUD's own shared nested setting true.

This is accepted for the initial integration.

Do not create a duplicate Addon-side nested HUD setting to avoid this.

Top-level Addon meaning remains:

    ClawHudEnabled=false -> no Managed process
    ClawHudEnabled=true  -> Managed process desired

Nested ClawHUD meaning remains:

    HudEnabled -> renderer preference inside ClawHUD

---

# 34. Cold/logon startup

When the Addon starts:

## ClawHudEnabled == false

Required:

    no ClawHudRuntimeAcquirer call
    no HTTP
    no ClawHUD process
    no pipe connection
    no PresentMon
    no EC helper
    no VRR work

## ClawHudEnabled == true

Required order:

    Full1902 startup admission
    -> controller critical deferred startup attempt completes / classified
    -> _disabledControllerStartupPending cleared
    -> optional ClawHUD reconcile scheduled
    -> acquisition / process / IPC

ClawHUD startup is not allowed to delay or fail controller startup.

---

# 35. Where to schedule startup reconcile

Current `StartDeferredRuntimeStartup()` is the correct lifecycle location.

Add one optional tail action after the existing controller-critical completion boundary.

Conceptually:

~~~text
TryStartDisabledModeControllerAsync
finally:
    _disabledControllerStartupPending = 0

then:
    StartBackgroundUpdate()
    StartOptionalClawHudReconcile()
~~~

Exact relative ordering between:

    background Addon update
    optional ClawHUD reconcile

does not need a new serialization rule.

They are unrelated feature-local tasks.

Do not make either one block the controller critical path.

---

# 36. Startup task tracking

Track at most one optional startup/reconcile task so shutdown can drain/cancel it.

For example:

~~~text
_clawHudStartup
~~~

Use the existing process-lifetime cancellation token.

Do not add:

    queue
    worker service
    retry timer
    watchdog

If startup fails, record unavailable and stop.

A later explicit user On action or later intentional reconcile may retry.

---

# 37. Unexpected ClawHUD child exit

If the proven child exits unexpectedly while desired state is On:

    record actual state unavailable
    clear tracked child
    log exit code

Do not immediately enter an infinite restart loop.

No polling is required.

Use the child Process exit event if convenient.

A future explicit retry / feature reconcile can start it again.

---

# 38. Addon crash / restart

An Addon crash can leave Managed ClawHUD alive.

On next Addon startup with:

    ClawHudEnabled == true

after Full1902 recovery:

    attempt exact normal bootstrap
    -> new --managed launch may return AlreadyRunning
    -> GetRuntimeInfo
    -> exact Managed Ready
    -> adopt logically
    -> converge HudEnabled
    -> Ready

Do not kill a healthy exact Managed survivor merely because the Addon restarted.

---

# 39. Controlled Addon restart

Normal controlled Addon restart is different from crash recovery.

Current Runtime restart path enters normal Addon process shutdown.

CH-A2 should therefore:

    RequestShutdown Managed ClawHUD during controlled Addon shutdown

The replacement Addon process later starts it again if desired state remains On.

Do not intentionally preserve ClawHUD across a normal controlled Addon restart.

---

# 40. BeginProcessShutdown behavior

Extend the existing host shutdown admission barrier.

When:

    BeginProcessShutdown()

runs:

    stop accepting new ClawHUD On/reconcile requests
    cancel in-flight optional acquisition/readiness where appropriate

Do not dispose controller ownership here earlier than current policy.

Do not let a late HUD request reopen work after shutdown has begun.

---

# 41. Controller-safe shutdown ordering

ClawHUD cleanup must not delay controller safety.

Do not write:

~~~text
await ClawHUD shutdown for 15 seconds
then begin Full1902 teardown
~~~

Instead preserve the current controller shutdown path.

Recommended shape:

~~~text
BeginProcessShutdown
  -> close HUD admission

ShutdownRuntimeBeforeMessageLoopExitAsync
  -> start bounded ClawHUD stop task
  -> execute existing Runtime / Full1902 shutdown path unchanged
  -> after controller-critical teardown is complete,
       observe/await remaining bounded ClawHUD stop
  -> HUD failure is logged only
~~~

The exact factoring may differ, but invariant is:

    controller teardown/fail-close never depends on HUD shutdown success

No ClawHUD exception may skip existing Full1902 teardown.

---

# 42. Uninstall interaction

Current uninstall already performs stock-safe preparation before ordinary Runtime shutdown.

Preserve:

    PrepareForUninstallAsync
      -> stock authority restoration
      -> startup-task removal / existing uninstall policy
      -> only on success begin process shutdown

Do not involve ClawHUD before stock restoration succeeds.

After uninstall preparation succeeds and ordinary shutdown begins:

    stop Managed ClawHUD feature-locally

Do not delete standalone ClawHUD user settings.

Addon-owned downloaded Runtime cache remains under:

    SteamInputAddonforClaw-Data/Runtime/ClawHUD

and is already under the Addon data root lifecycle.

---

# 43. Sleep / Hibernate / Resume

Do not add Addon-side ClawHUD suspend/resume logic in CH-A2.

ClawHUD already owns:

    suspend handling
    HUD hiding
    telemetry pause
    resume recovery

SteamAddon owns its existing Full1902 suspend/resume behavior.

Do not:

    restart ClawHUD every resume
    poll its pipe after resume
    recreate its HUD from Addon
    add a resume epoch

If the ClawHUD process actually exits, that is normal process-loss behavior.

---

# 44. No ClawHUD StartWithWindows ownership from Addon

Managed mode explicitly skips Standalone startup-task reconciliation.

SteamAddon must never send:

    SetStartWithWindows

in Managed operation.

SteamAddon itself already owns its own mandatory startup task.

These are separate products/lifecycles.

Do not attempt to synchronize the ClawHUD Standalone startup task with:

    AppSettings.ClawHudEnabled

---

# 45. Logging

Recommended categories:

    ClawHUD.Process
    ClawHUD.IPC

Keep existing CH-A1:

    ClawHUD.Runtime

Useful events:

    ManagedStartRequested
    ManagedProcessStarted
    ManagedStartupExit
    ExistingInstanceClassified
    ManagedRuntimeAdopted
    ControlReady
    HudEnableConverged
    ManagedShutdownRequested
    ManagedShutdownCompleted
    ManagedShutdownFallbackKill
    ManagedRuntimeUnavailable

Fields:

    RuntimeVersion
    ApplicationVersion
    PID when known
    ExitCode
    LaunchMode
    RuntimeState
    ProtocolMin
    ProtocolMax
    Failure
    ElapsedMs

Do not dump raw frames at Info.

Debug-level framing diagnostics may log header metadata, never arbitrary binary payload dumps.

---

# 46. CH-A2 must not add Main UI / Overlay behavior

Do not add:

    Settings page HUD card
    Controller page HUD card
    Overlay HUD controls
    QAM HUD tab controls
    opacity slider
    nested setting UI
    runtime status visuals

Those are CH-A3.

CH-A2 may expose an internal host/state API designed for CH-A3 consumption later, but do not widen frontend transport now unless the implementation cannot be tested otherwise.

---

# 47. Suggested files

Expected production shape:

    src/SteamInputAddonforClaw/
      ClawHud/
        ClawHudControlProtocol.cs
        ClawHudControlCodec.cs
        ClawHudControlClient.cs
        ClawHudProcessController.cs

      Settings/
        AppSettings.cs
        SettingsStore.cs
        StartupSettingsCoordinator.cs

      Hosting/
        AddonProcessHost.cs

Normally no change should be needed in:

    Program.cs

`RuntimeProcessApplication.cs` should need no architectural change unless a tiny shutdown/composition seam is genuinely necessary.

Avoid broad frontend changes.

---

# 48. Protocol tests

Add focused tests such as:

    ClawHudControlCodecTests.cs
    ClawHudControlClientTests.cs

Required codec coverage:

- exact CHUD header;
- little-endian fields;
- non-zero request id;
- GetRuntimeInfo empty request;
- GetSettingsSnapshot empty request;
- SetHudEnabled one-byte bool;
- RequestShutdown empty request;
- response correlation;
- wrong magic rejected;
- wrong protocol rejected;
- wrong operation rejected;
- wrong request id rejected;
- oversized payload rejected;
- unknown enum rejected;
- malformed UTF-8 rejected;
- trailing payload rejected;
- successful RequestShutdown accepts Ok + empty payload.

Use known byte vectors where practical.

---

# 49. Named Pipe client tests

Use a temporary test pipe name.

No live ClawHUD process required.

Prove:

    connect
    -> request
    -> one response
    -> close

Cover:

- GetRuntimeInfo success;
- SettingsSnapshot success;
- SetHudEnabled success;
- RequestShutdown success;
- protocol error;
- malformed response;
- transport unavailable;
- internal timeout;
- caller cancellation.

Do not create a new server framework solely for tests.

A small test NamedPipeServerStream helper is enough.

---

# 50. Process controller test seams

Do not require unit tests to launch the real ClawHUD binary.

Use narrow seams for:

    process launcher / child handle
    runtime acquirer result
    control client

The production class should still use ordinary:

    System.Diagnostics.Process

Avoid a full generic `IProcessService` platform layer.

A small delegate or minimal ClawHUD-specific seam is sufficient.

---

# 51. Process controller tests — normal start

Required:

    desired On
    acquirer returns exact Runtime
    process launches exact ClawHUD.exe --managed
    WorkingDirectory is exact Runtime dir
    UseShellExecute=false
    readiness IPC reaches Managed/Ready/exact version
    GetSettingsSnapshot.HudEnabled=true
    result Ready

Also:

    HudEnabled=false
    -> SetHudEnabled(true)
    -> returned snapshot true
    -> Ready

and:

    SetHudEnabled returns false snapshot / protocol failure
    -> unavailable
    -> desired setting remains true

---

# 52. Process controller tests — existing instance

Required scenarios:

## Standalone conflict

    child exits AlreadyRunning
    GetRuntimeInfo => Standalone
    -> StandaloneConflict
    -> no RequestShutdown
    -> no kill

## Exact Managed adoption

    child exits AlreadyRunning
    GetRuntimeInfo => Managed + Ready + version 1.0.1 + protocol compatible
    -> adopt
    -> no relaunch
    -> no kill

## Managed mismatch replacement

    child exits AlreadyRunning
    GetRuntimeInfo => Managed + Ready + other version + protocol compatible
    -> RequestShutdown
    -> bounded endpoint disappearance
    -> launch exact pinned Runtime
    -> readiness
    -> Ready

## Unclassifiable existing instance

    AlreadyRunning
    pipe timeout / malformed / incompatible
    -> unavailable
    -> no kill

---

# 53. Process controller tests — startup exit classifications

Cover the Managed exit codes that can realistically occur:

    UnsupportedHardware
    HardwareIndeterminate
    PresentMon prerequisite failures
    RuntimeInitializationFailed
    ControlIpcUnavailable

Each must:

    return feature-local unavailable state
    retain desired On
    not invoke controller recovery

AlreadyRunning must go through IPC classification rather than generic failure.

---

# 54. Process controller tests — shutdown

## Proven child

    RequestShutdown succeeds
    child exits within bound
    -> no kill

If graceful shutdown fails / times out:

    proven child still alive
    -> Kill(entireProcessTree:true) allowed

## Adopted Managed runtime

    RequestShutdown succeeds
    -> stop

If graceful shutdown cannot be confirmed:

    no force kill
    -> state unavailable / cleanup failure logged

## Standalone / unclassified

    no RequestShutdown
    no kill

---

# 55. Settings tests

Extend current settings coverage.

Required:

    old settings without ClawHudEnabled -> false
    ClawHudEnabled=true round trips
    ClawHudEnabled=false round trips
    malformed ClawHudEnabled does not reset unrelated settings
    StartupSettingsCoordinator mutation uses save-then-publish
    failed SettingsStore.Save does not publish new desired value

Follow the current SettingsStore / coordinator test style.

---

# 56. Startup ordering regression tests

Extend the existing source/behavior contract tests around deferred startup.

Prove:

    TryStartDisabledModeControllerAsync
      occurs before
    _disabledControllerStartupPending = 0
      occurs before
    optional ClawHUD startup scheduling

Also prove:

    ClawHudEnabled=false
      -> no Runtime acquisition / process start

and:

    ClawHUD startup failure
      -> existing Full1902 startup result stays RuntimeReady
      -> controller-owned startup task is not re-run / torn down

Do not weaken the existing background-update ordering test.

---

# 57. Shutdown regression tests

Add a test proving:

    ClawHUD stop throws / times out

does not prevent the existing:

    RuntimeHost disposal
    Full1902 presentation cleanup
    physical ownership cleanup
    controller fail-close / existing shutdown completion

Do not add artificial race orchestration.

Test the real product guarantee:

    optional HUD cleanup cannot block controller safety

---

# 58. No startup network when disabled

This deserves an explicit test because it is a product requirement.

When:

    AppSettings.ClawHudEnabled == false

prove:

    ClawHudRuntimeAcquirer is not called

This guarantees:

    no GitHub request
    no download
    no PresentMon
    no UAC
    no ClawHUD background process

for users who never enable the feature.

---

# 59. No permanent watchdog

Do not add:

    Timer
    PeriodicTimer
    polling Task
    hourly/minutely reconcile
    pipe heartbeat
    process health heartbeat

for CH-A2.

Event-driven child Exit plus explicit startup/user reconcile is sufficient.

If real field behavior later proves a need for a recovery trigger, add it from evidence.

---

# 60. Race / overengineering policy

Protect real cases:

    user toggles On twice
    user toggles Off while a startup is in progress
    Addon shutdown during optional startup
    ClawHUD startup exits
    PresentMon install failure
    existing Standalone conflict
    surviving Managed runtime after Addon crash
    controlled shutdown
    disk/network/IPC failure

A narrow per-feature gate is acceptable to serialize top-level On/Off/reconcile.

Do not add:

    epochs
    generations
    multiple authority flags
    distributed mutexes
    command queue
    actor model
    state-machine framework

solely for instruction-level interleavings.

One clear process owner and one narrow lifecycle gate are enough.

---

# 61. Suggested top-level reconcile shape

Conceptually:

~~~csharp
await _clawHudGate.WaitAsync(token);
try
{
    if (shutdownStarted)
        return unavailable;

    if (!settings.ClawHudEnabled)
        return await StopManagedAsync(...);

    var acquired = await acquirer.AcquireAsync(token);
    if (!acquired.IsReady)
        return unavailable;

    return await processController.EnsureRunningAsync(acquired, token);
}
finally
{
    _clawHudGate.Release();
}
~~~

Exact factoring is implementation choice.

Do not duplicate this decision in UI, IPC transport, and process controller.

---

# 62. Failure policy summary

Every ClawHUD failure:

~~~text
download failure
hash failure
disk failure
process start failure
AlreadyRunning Standalone
unsupported hardware
PresentMon failure
Control IPC unavailable
protocol mismatch
malformed response
HUD enable failure
shutdown timeout
~~~

results only in:

    ClawHUD actual state unavailable/conflict
    log/status
    future retry remains possible

It must never result in:

    PID1901/PID1902 mutation
    Center M mutation
    HidHide mutation
    VIIPER mutation
    controller teardown
    process-wide fatal exception

---

# 63. Validation commands

Run the current repository-supported full validation.

At minimum:

~~~powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Debug --no-build

dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test SteamInputAddonforClaw.slnx -c Release --no-build
~~~

Also run the existing publish verifier path because CH-A1 packaging must remain intact.

No test may require live GitHub or a real installed ClawHUD Runtime.

---

# 64. Manual hardware validation after implementation

After automated tests pass, validate on MSI Claw hardware.

Use the real CH-A1 pinned Runtime.

## Case 1 — desired Off

    ClawHudEnabled=false
    restart Addon

Expect:

    no ClawHUD process
    no ClawHUD Runtime download if not already installed
    controller behavior unchanged

## Case 2 — desired On / no existing ClawHUD

    enable feature
    Runtime downloads if needed
    ClawHUD.exe --managed starts
    no ClawHUD tray
    no ClawHUD Settings window
    HUD appears according to ClawHUD nested settings
    IPC reports Managed / Ready

## Case 3 — Addon restart

Controlled restart:

    Managed ClawHUD shuts down
    replacement Addon later starts it again

## Case 4 — simulated Addon crash

Terminate Addon without controlled shutdown while Managed ClawHUD is alive.

Restart Addon.

Expect:

    Full1902 recovers first
    then exact Managed survivor is classified/adopted
    no unnecessary ClawHUD restart

## Case 5 — Standalone conflict

Start standalone ClawHUD first.

Enable Addon HUD.

Expect:

    Standalone remains untouched
    Addon reports feature conflict/unavailable
    controller behavior unchanged

---

# 65. Explicit non-goals

Do not include:

    Main UI work
    Overlay UI work
    QAM HUD controls
    nested ClawHUD setting persistence in Addon
    SetStartWithWindows from Addon
    PresentMon ownership in Addon
    Intel VRR implementation in Addon
    ClawHUD EC helper sharing
    generic child process manager
    generic dependency manager
    service
    supervisor
    heartbeat
    polling
    multi-session support
    RDP support
    Fast User Switching support
    ClawHUD runtime auto-update automation
    dependency PR automation
    runtime-cache cleanup policy overhaul

CH-A4 handles dependency update automation later.

---

# 66. Acceptance criteria

CH-A2 is complete when all are true:

1. `ClawHudEnabled` is persisted in the existing Addon settings and defaults false.
2. Default Off performs zero ClawHUD acquisition/process/IPC work.
3. Optional startup occurs only after the Full1902 controller-critical deferred startup boundary.
4. The Addon has its own strict protocol-v1 codec/client.
5. `RequestShutdown` is supported by the SteamAddon codec despite the standalone WPF client not sending it.
6. A verified CH-A1 Runtime launches only as `ClawHUD.exe --managed`.
7. Process start alone is not treated as readiness.
8. Ready requires Managed + Ready + protocol-v1 compatibility + exact pinned Runtime applicationVersion.
9. Ready also requires authoritative `HudEnabled=true`.
10. An existing Standalone instance is never killed or adopted.
11. An exact surviving Managed Runtime can be adopted after Addon crash/restart.
12. A compatible mismatched Managed Runtime is gracefully shut down before exact pinned relaunch.
13. An unclassifiable existing instance is never killed.
14. Force kill is allowed only for a child proven to have been launched by the current Addon process.
15. Managed startup exit codes remain feature-local.
16. PresentMon remains entirely owned by ClawHUD.
17. Controlled Addon shutdown requests Managed ClawHUD shutdown.
18. ClawHUD cleanup failure cannot prevent Full1902/controller teardown.
19. Sleep/resume remains independently owned by ClawHUD and Full1902 respectively.
20. No heartbeat/polling/supervisor is introduced.
21. No UI is added in this PR.
22. Full test suite and publish verification remain green.

---

# 67. Suggested PR title

    CH-A2: add managed ClawHUD process and IPC lifecycle

Suggested PR summary:

    - persist the top-level ClawHUD desired state
    - launch/adopt the exact pinned Runtime in --managed mode
    - add strict protocol-v1 Control IPC readiness and graceful shutdown
    - keep all ClawHUD failures isolated from Full1902 controller authority

---

# 68. Handoff to CH-A3

After CH-A2 is merged and hardware-proven, CH-A3 may expose the existing authority through Main UI and Quick Settings.

CH-A3 should consume:

    desiredEnabled
    actual runtime state
    runtime/application version
    failure/conflict reason
    authoritative nested SettingsSnapshot

and add IPC mutations for:

    visibility
    size
    font
    alignment
    background mode
    opacity preview/commit
    Intel VRR Range Fix

CH-A3 must not create another process owner or another direct pipe client in the UI processes.
