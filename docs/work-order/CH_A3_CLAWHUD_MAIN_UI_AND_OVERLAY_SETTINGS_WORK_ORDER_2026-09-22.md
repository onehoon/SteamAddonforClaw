# Work Order — CH-A3 ClawHUD Main UI and Overlay Settings Integration

**Date:** 2026-09-22  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** main  
**Target branch after merge:** main  
**Recommended implementation branch:** feature/clawhud-a3-main-ui-overlay  
**Reviewed SteamAddon main:** 4598d3c89174ed7c5ca63c94dfc854142dd53674  
**Reviewed ClawHUD branch:** integration/steamaddon  
**Feature track:** ClawHUD ↔ SteamAddon integration — CH-A3  
**Expected PR count:** 1 focused PR

---

## 0. Purpose

CH-A1 and CH-A2 are merged.

The current Addon can already:

    persist AppSettings.ClawHudEnabled
    -> acquire the exact pinned ClawHUD Runtime
    -> launch ClawHUD.exe --managed
    -> classify Standalone vs Managed safely
    -> adopt an exact surviving Managed Runtime after Addon crash/restart
    -> verify Managed / Ready / protocol / exact Runtime version
    -> converge ClawHUD HudEnabled=true
    -> request graceful Managed shutdown
    -> force-kill only a proven Addon-launched child when required

CH-A3 completes the user-facing control path.

Required end state:

    Main Settings UI
      -> top-level ClawHUD On / Off
      -> current Managed Runtime state
      -> nested ClawHUD display settings
      -> Intel VRR Range Fix setting/result

    Addon Overlay / Setting tab
      -> same top-level On / Off authority
      -> same current Managed Runtime state
      -> same nested ClawHUD settings
      -> existing Tab Order editor remains intact

Both surfaces must consume the SAME Runtime-owned ClawHUD authority.

Do not create:

    UI -> ClawHUD Named Pipe
    Overlay -> ClawHUD Named Pipe
    UI -> settings.ini
    Overlay -> settings.ini
    UI-side Runtime download
    Overlay-side Runtime download
    second ClawHUD process owner
    second desired-state store

The ownership chain remains:

    Main UI / Overlay
      -> SteamAddon frontend contract
      -> AddonProcessHost / existing ClawHudProcessController
      -> existing ClawHudControlClient
      -> ClawHUD Control IPC
      -> ClawHUD-owned settings.ini / runtime state

---

# 1. Mandatory source review before coding

Read the latest files on the implementation branch before changing code.

## SteamAddon Full1902 authority

At minimum:

    docs/Full 1902 Implementation/README.md
    docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
    docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md

These remain authoritative for controller lifecycle.

ClawHUD UI work must not modify their ownership model.

## ClawHUD integration work orders

Read:

    docs/work-order/CH_A1_CLAWHUD_EXACT_RUNTIME_PIN_AND_ACQUISITION_WORK_ORDER_2026-09-21.md
    docs/work-order/CH_A2_CLAWHUD_MANAGED_PROCESS_IPC_AND_TOP_LEVEL_LIFECYCLE_WORK_ORDER_2026-09-21.md

Also read the original ClawHUD integration architecture on the historical integration branch:

    docs/work-order/CLAW_HUD_STEAMADDON_INTEGRATION_ARCHITECTURE_AND_PR_PLAN_2026-09-21.md
    branch: integration/clawhud

The old branch-target statement in that architecture is superseded.

Current implementation target remains:

    feature branch -> PR to main

## Current CH-A2 implementation

Read:

    src/SteamInputAddonforClaw/ClawHud/ClawHudControlProtocol.cs
    src/SteamInputAddonforClaw/ClawHud/ClawHudControlCodec.cs
    src/SteamInputAddonforClaw/ClawHud/ClawHudControlClient.cs
    src/SteamInputAddonforClaw/ClawHud/ClawHudProcessController.cs
    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

    src/SteamInputAddonforClaw/Settings/AppSettings.cs
    src/SteamInputAddonforClaw/Settings/SettingsStore.cs
    src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs

## Current frontend / Main UI

Read:

    src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

    src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

    src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
    src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
    src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

    src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
    src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
    src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs

## Current Overlay / shared surface

Read:

    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
    src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
    src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs
    src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
    src/SteamInputAddonforClaw.Overlay/App.xaml.cs

    src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

    src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

    src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
    src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsTabOrderContracts.cs
    src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

Also read:

    docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md
    docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

Important existing invariant:

    QuickSettingsPageId = Device / Profile only

Do NOT add Setting, HUD, or ClawHUD to QuickSettingsPageId.

The Setting tab has its own product contracts and must keep doing so.

---

# 2. ClawHUD producer / wire authority

Repository:

    onehoon/ClawHUD
    branch: integration/steamaddon

Before implementation, re-read at minimum:

    src/shared/ClawHudControlProtocol.h
    src/shared/ClawHudControlCodec.cpp

    src/ClawHUD.Settings/Protocol/ControlProtocol.cs
    src/ClawHUD.Settings/Protocol/ControlCodec.cs
    src/ClawHUD.Settings/Services/RuntimeControlClient.cs

The native shared protocol remains the wire authority.

The current protocol-v1 nested setting operations are:

    GetSettingsSnapshot            = 2

    SetHudEnabled                  = 11
    SetHudVisibilityMode           = 12
    SetHudSizeOffset               = 13
    SetHudFont                     = 14
    SetHudAlignment                = 15
    SetHudBackgroundMode           = 16
    PreviewHudOpacity              = 17
    CommitHudOpacity               = 18
    SetIntelVrrRangeFixEnabled     = 19

SteamAddon Managed mode must NOT expose or send:

    SetStartWithWindows = 10

RequestShutdown remains CH-A2 process-lifecycle authority, not a CH-A3 UI setting.

---

# 3. Full1902 safety boundary is unchanged

CH-A3 is a frontend/settings PR for an optional sibling feature.

No ClawHUD UI action may mutate or gate:

    Center M authority
    PID1901 / PID1902
    DirectInput ownership
    HidHide
    VIIPER
    Xbox360 / SteamDeck presentation
    controller startup admission
    controller recovery
    suspend/resume controller reconciliation
    firmware transition
    uninstall stock restoration

A ClawHUD capture or mutation failure must remain feature-local.

Examples:

    ClawHUD pipe timeout
      -> HUD setting unavailable
      -> controller unchanged

    ClawHUD Standalone conflict
      -> HUD unavailable
      -> Standalone untouched
      -> controller unchanged

    Intel VRR setting failure
      -> return authoritative ClawHUD failure
      -> controller unchanged

    ClawHUD child exits
      -> UI becomes Unavailable
      -> no PID/HidHide/VIIPER action

Do not add Full1902 recovery calls to any CH-A3 failure path.

---

# 4. Authority split — keep it explicit in the UI contract

There are two different state owners.

## 4.1 SteamAddon-owned top-level desired state

Existing authority:

    settings.json
      -> AppSettings.ClawHudEnabled
      -> StartupSettingsCoordinator
      -> AddonProcessHost.SetClawHudEnabledAsync(...)

Meaning:

    ClawHudEnabled=false
      -> no SteamAddon-managed ClawHUD process desired

    ClawHudEnabled=true
      -> Managed ClawHUD process desired

This is the ONLY top-level feature switch.

## 4.2 ClawHUD-owned nested settings

ClawHUD owns:

    Display Mode
    HUD Size
    Font
    Alignment
    Background Mode
    Background Opacity
    Intel VRR Range Fix
    Intel VRR last result/status

These remain stored by ClawHUD.

SteamAddon must not persist copies in AppSettings.

Do not add:

    ClawHudDisplayMode
    ClawHudFont
    ClawHudOpacity
    ClawHudVrrEnabled
    ...

to settings.json.

The mutation response from ClawHUD is the authoritative nested state.

---

# 5. Do not expose a second HUD-enabled switch

CH-A2 already converges the internal ClawHUD HudEnabled flag to true whenever the Addon-owned Managed Runtime reaches Ready.

Therefore CH-A3 must NOT show both:

    Addon ClawHudEnabled
    ClawHUD HudEnabled

as two user-facing switches.

User-facing top-level control is only:

    Enable HUD

Its semantics are process lifetime:

    Off
      -> persist ClawHudEnabled=false
      -> stop Managed ClawHUD

    On
      -> persist ClawHudEnabled=true
      -> acquire/start/adopt Managed ClawHUD
      -> Ready
      -> internal ClawHUD HudEnabled=true

The internal protocol snapshot may still decode HudEnabled because it is part of protocol v1.

Do not expose it as a separate setting.

---

# 6. Frontend contract — add one closed ClawHUD product model

Create a focused contract file, preferably:

    src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs

Do not put ClawHUD wire-protocol types into the Contracts project.

The public frontend contract should use product-facing enums.

Recommended shape:

~~~csharp
public enum FrontendClawHudRuntimeState
{
    Disabled,
    Starting,
    Ready,
    Unavailable,
    StandaloneConflict,
}

public enum FrontendClawHudDisplayMode
{
    Always,
    InGameOnly,
}

public enum FrontendClawHudFont
{
    Unispace,
    SegoeUiVariable,
}

public enum FrontendClawHudAlignment
{
    Left,
    Center,
    Right,
}

public enum FrontendClawHudBackgroundMode
{
    FullWidth,
    ContentWidth,
}

public enum FrontendClawHudIntelVrrStatus
{
    Disabled,
    Unavailable,
    UnsupportedPanel,
    AmbiguousDisplay,
    AlreadyCorrect,
    SkippedUserProfile,
    Applied,
    ApplyFailed,
    VerificationFailed,
}
~~~

Add a narrow VRR result DTO carrying the producer fields:

    Status
    PanelName
    RangeBefore
    RangeAfter
    Message
    TimestampUtc

Add a nested settings snapshot:

~~~csharp
public sealed record FrontendClawHudSettingsSnapshot(
    FrontendClawHudDisplayMode DisplayMode,
    int HudSizeOffset,
    FrontendClawHudFont Font,
    FrontendClawHudAlignment Alignment,
    FrontendClawHudBackgroundMode BackgroundMode,
    int BackgroundOpacityPercent,
    bool IntelVrrRangeFixEnabled,
    FrontendClawHudIntelVrrResult? IntelVrrLastResult);
~~~

Do not include StartWithWindows.

Do not expose internal HudEnabled as a second user-editable value.

---

# 7. Top-level ClawHUD frontend snapshot

Add one compact snapshot representing desired Addon state + actual Managed feature state + optional authoritative nested settings.

Recommended shape:

~~~csharp
public sealed record FrontendClawHudSnapshot(
    bool DesiredEnabled,
    FrontendClawHudRuntimeState RuntimeState,
    string StatusMessage,
    string? RuntimeVersion,
    string? ApplicationVersion,
    FrontendClawHudSettingsSnapshot? Settings);
~~~

Semantics:

## Off

    DesiredEnabled = false
    RuntimeState   = Disabled
    Settings       = null

## Starting

    DesiredEnabled = true
    RuntimeState   = Starting
    Settings       = null

## Ready + settings readable

    DesiredEnabled = true
    RuntimeState   = Ready
    Settings       = authoritative mapped ClawHUD SettingsSnapshot

## Ready process but one settings read fails

Do NOT silently change the CH-A2 process owner to Unavailable solely because a UI read failed.

Return:

    DesiredEnabled = true
    RuntimeState   = Ready
    Settings       = null
    StatusMessage  = user-facing settings-read failure

A later capture may succeed.

## Runtime unavailable

    DesiredEnabled = true
    RuntimeState   = Unavailable
    Settings       = null

## Standalone conflict

    DesiredEnabled = true
    RuntimeState   = StandaloneConflict
    Settings       = null

The top-level toggle always binds to DesiredEnabled.

Never bind it to:

    RuntimeState == Ready

A failed On attempt must remain visually On + Unavailable.

That preserves user intent.

---

# 8. User-facing failure/status mapping

Do not expose raw internal failure tokens as final UI copy when a stable user meaning is known.

Examples:

    Disabled
      -> "Off"

    Starting
      -> "Starting…"

    Ready
      -> "Ready"

    StandaloneConflict
      -> "ClawHUD Standalone is already running. Close it and retry."

    StartupExit:UnsupportedHardware
      -> "ClawHUD is not supported on this device."

    StartupExit:HardwareIndeterminate
      -> "ClawHUD hardware support could not be verified."

    StartupExit:PresentMonRebootRequired
      -> "Restart Windows to finish PresentMon setup."

    StartupExit:PresentMonElevationCancelled
      -> "PresentMon setup permission was cancelled."

    StartupExit:PresentMonInstallFailed
      -> "PresentMon installation failed."

    StartupExit:PresentMonValidationFailed
      -> "PresentMon could not be validated."

    Runtime download/acquisition failure
      -> "HUD Runtime could not be prepared."

    IPC timeout / transport unavailable during nested settings read
      -> "HUD settings could not be read."

Keep raw reason codes in logs.

Use one small mapping helper/private method.

Do not create a localization/status framework in this PR.

---

# 9. Nested mutation contract

Do not add one frontend RPC per ClawHUD protocol operation unless the implementation genuinely becomes simpler.

Preferred frontend shape:

    CaptureClawHudAsync
    SetClawHudEnabledAsync
    MutateClawHudSettingAsync

Use one CLOSED typed nested-mutation intent.

Required nested mutation kinds:

    DisplayMode
    HudSizeOffset
    Font
    Alignment
    BackgroundMode
    PreviewOpacity
    CommitOpacity
    IntelVrrRangeFixEnabled

The payload must be strongly bounded and validated.

Acceptable design:

~~~text
FrontendClawHudMutationKind
+ one typed intent record
+ only the value member required by that Kind
~~~

For example, the intent may contain nullable typed members if validation requires exactly one matching member.

Do not use:

    string setting name
    object value
    JsonElement product dispatch
    Dictionary<string, object>
    reflection
    arbitrary operation IDs from UI

The Runtime must explicitly switch over every known mutation kind.

Unknown or malformed combinations fail closed and perform ZERO ClawHUD IPC mutations.

---

# 10. Mutation result

Use one authoritative result:

~~~csharp
public sealed record FrontendClawHudMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendClawHudSnapshot Snapshot);
~~~

Rules:

    successful nested mutation
      -> use SettingsSnapshot returned by THAT ClawHUD IPC response
      -> map it directly
      -> Succeeded=true

Do not:

    assume requested value was accepted
    locally patch old snapshot
    return request echo as authoritative state

If ClawHUD returns:

    Ok + snapshot with a different value

render the returned value.

For protocol/transport failure:

    Succeeded=false
    FailureMessage=user-facing
    no fabricated nested Settings snapshot

The CH-A2 desired state remains unchanged.

Do not restart ClawHUD merely because one nested setting mutation failed.

---

# 11. Extend the existing ClawHudControlClient — do not create another client

Current SteamAddon IClawHudControlClient exposes only the CH-A2 subset.

Extend that same interface/class with:

~~~text
SetHudVisibilityModeAsync
SetHudSizeOffsetAsync
SetHudFontAsync
SetHudAlignmentAsync
SetHudBackgroundModeAsync
PreviewHudOpacityAsync
CommitHudOpacityAsync
SetIntelVrrRangeFixEnabledAsync
~~~

Use the codec already implemented in CH-A2.

Do NOT create:

    ClawHudUiControlClient
    OverlayClawHudClient
    MainUiClawHudClient
    second pipe endpoint
    persistent pipe connection

The existing one-request / one-response / one-connection transport remains correct.

Do not add SetStartWithWindows to the SteamAddon UI seam.

---

# 12. Reuse the existing ClawHudProcessController gate

Nested capture/mutation must only occur against a currently classified Managed Ready Runtime.

Use the existing ClawHudProcessController ownership/gate.

Do not add:

    ClawHudSettingsManager
    ClawHudUiSession
    second SemaphoreSlim
    settings worker
    retry service
    heartbeat

A narrow process-controller method or internal dispatch seam is enough.

Required behavior before nested IPC:

    State.ActualState == Ready
    managed session classified / ready

Otherwise:

    return feature-local unavailable result
    send zero nested setting mutation

A settings capture/mutation does NOT download or launch the Runtime.

Only top-level SetClawHudEnabledAsync(true) owns acquisition/start/reconcile.

This distinction is important:

    capture nested settings
      != ensure process exists

    mutate nested settings
      != ensure process exists

---

# 13. Mapping from wire types to frontend types

Keep wire types internal to SteamInputAddonforClaw.

Explicitly map:

    ClawHudWireVisibilityMode.Always
      -> FrontendClawHudDisplayMode.Always

    ClawHudWireVisibilityMode.InGameOnly
      -> FrontendClawHudDisplayMode.InGameOnly

    ClawHudWireFont.Unispace
      -> FrontendClawHudFont.Unispace

    ClawHudWireFont.SegoeUiVariable
      -> FrontendClawHudFont.SegoeUiVariable

    ClawHudWireAlignment.Left / Center / Right
      -> matching frontend values

    ClawHudWireBackgroundMode.FullWidth / ContentWidth
      -> matching frontend values

Intel VRR status must map all nine current protocol-v1 values.

Unknown wire enum values are already rejected by the codec.

Do not silently cast internal wire enums into public frontend enums by numeric coincidence.

---

# 14. Product bounds

Preserve the producer contract exactly:

    HudSizeOffset
      minimum = -2
      maximum = +2
      step    = 1

    BackgroundOpacityPercent
      minimum = 50
      maximum = 100
      step    = 5

Reject invalid frontend mutation values before sending IPC.

Do not clamp an invalid request into a different value silently.

---

# 15. InProcessAddonFrontendControl remains the frontend boundary

Add the three ClawHUD frontend operations to IAddonFrontendControl:

~~~text
CaptureClawHudAsync(...)
SetClawHudEnabledAsync(bool enabled, ...)
MutateClawHudSettingAsync(FrontendClawHudMutationIntent intent, ...)
~~~

Safe default interface implementations may return Unavailable if that avoids unrelated test-double churn.

Production InProcessAddonFrontendControl must use the Runtime-owned ClawHUD seam.

Do not give InProcessAddonFrontendControl direct ownership of:

    HttpClient
    ClawHudRuntimeAcquirer
    Process
    ClawHudControlClient
    settings.ini

Preferred low-churn construction:

    AddonProcessHost
      -> passes narrow capture / top-level mutation / nested mutation delegates
      -> InProcessAddonFrontendControl

Do not pass the whole AddonProcessHost as a generic service object.

Do not create IClawHudIntegrationManager.

---

# 16. AddonProcessHost remains the integration composition owner

The current host already owns:

    ClawHudRuntimeAcquirer
    ClawHudProcessController
    StartupSettingsCoordinator

Add the smallest host-facing frontend helpers required to:

    capture current ClawHUD frontend snapshot
    map CH-A2 process state
    read nested settings only when Ready
    perform one validated nested mutation
    return mapped authoritative frontend result

Top-level SetClawHudEnabledAsync already exists.

Do not duplicate it.

If its return type remains internal ClawHudState, wrap/map it at the frontend seam rather than changing CH-A2 lifecycle ownership.

---

# 17. StateInvalidated — one narrow real lifecycle addition

Current CH-A2 observes unexpected child exit and records:

    ManagedChildExited:<exitCode>
    ActualState = Unavailable

That is a realistic lifecycle event.

An already-open Main UI / Overlay should not keep showing Ready indefinitely.

Add the smallest notification path.

Recommended:

    ClawHudProcessController
      -> one narrow StateChanged event when its externally meaningful State changes

    AddonProcessHost
      -> forwards it into the existing frontend StateInvalidated authority

Do not add:

    polling
    timer
    heartbeat
    process watchdog
    event bus
    state subscription framework

This event is also useful after real CH-A2 state changes such as:

    unexpected child exit
    readiness failure
    Standalone conflict classification

Avoid duplicate invalidations where a direct frontend mutation already returns the final authoritative snapshot.

One extra redundant low-rate StateInvalidated is not a correctness problem; do not add epochs/barriers merely to deduplicate theoretical ordering.

---

# 18. StateInvalidated behavior for nested settings

Read-only capture:

    does not raise StateInvalidated

Successful committed nested mutation:

    may raise the existing StateInvalidated once

Top-level On / Off:

    raise once after the operation settles

Opacity Preview:

    do NOT broadcast a global StateInvalidated for every preview step

Reason:

    Preview is an interaction draft
    the caller already receives the authoritative preview response
    global invalidation for every slider step would create unnecessary UI/Overlay refresh traffic

Opacity Commit:

    raise once after settlement

Unexpected process exit:

    raise once

No polling.

---

# 19. Frontend Named Pipe protocol

Current frontend protocol on reviewed main:

    FrontendTransportProtocol.CurrentVersion = 38

CH-A3 changes the wire-visible contract.

Bump:

    38 -> 39

Document the reason in FrontendWire.cs.

Add RPC methods conceptually:

    CaptureClawHud
    SetClawHudEnabled
    MutateClawHudSetting

Add only the narrow request DTOs required.

CaptureClawHud has no payload.

NamedPipeAddonFrontendClient must implement all three methods.

NamedPipeAddonFrontendServer must:

    validate payload presence/absence
    decode typed requests
    dispatch only onto IAddonFrontendControl
    return typed result/snapshot

It must NOT reach:

    ClawHudControlClient
    ClawHudProcessController
    SettingsStore

directly.

The server remains transport only.

---

# 20. Frontend transport validation

Update the existing no-payload validation so:

    CaptureClawHud + payload
      -> InvalidMessage

Malformed nested mutation intent:

    -> Runtime/product validation failure
    -> zero ClawHUD IPC calls

Unknown RPC method:

    -> existing UnsupportedMethod behavior

Old v38 peer:

    -> fail handshake against v39

Do not add compatibility shims.

The product is pre-release.

---

# 21. Main UI placement

Use the existing Settings page.

Do not create a new top-level navigation page solely for ClawHUD.

Add one grouped ClawHUD section, preferably a SettingsExpander, in:

    src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml

Recommended placement:

    Application updates
    Steam Big Picture Full Screen Experience
    Show only current power source
    ClawHUD
    Enter BIOS
    Required Components
    Developer Menu

Exact icon may use an existing WinUI Symbol already proven by the project.

Do not add a custom icon asset merely for this PR.

---

# 22. Main UI ClawHUD section

Recommended layout:

    ClawHUD
      Description: current runtime/status text

      Enable HUD                         [Toggle]

      Display Mode                      [ComboBox]
      HUD Size                          [ComboBox]
      Font                              [ComboBox]
      Alignment                         [ComboBox]
      Background                        [ComboBox]
      Background Opacity                [Slider]
      Intel VRR Range Fix               [Toggle]

      Intel VRR status/result            [read-only card/text]

Optional compact Retry button:

    Retry

is appropriate only when:

    DesiredEnabled == true
    RuntimeState in { Unavailable, StandaloneConflict }

Retry must call the SAME top-level operation:

    SetClawHudEnabledAsync(true)

It is not a second retry engine.

Do not add automatic retry loops.

---

# 23. Main UI top-level toggle semantics

Bind:

    Toggle.IsOn = snapshot.DesiredEnabled

Never:

    Toggle.IsOn = snapshot.RuntimeState == Ready

Examples:

## On + Ready

    Toggle On
    Description Ready
    nested editors enabled

## On + Unavailable

    Toggle On
    Description failure
    nested editors disabled
    Retry visible/enabled if implemented

## On + StandaloneConflict

    Toggle On
    Description Standalone conflict
    nested editors disabled
    Retry available after the user closes Standalone

## Off

    Toggle Off
    Description Off
    nested editors disabled

During an in-flight top-level mutation:

    disable top-level toggle / Retry
    do not create a second concurrent On/Off request

After the response:

    render the returned authoritative snapshot

Do not optimistically declare Ready.

---

# 24. Main UI nested editor availability

Nested editors are writable only when:

    RuntimeState == Ready
    Settings != null
    no nested mutation is currently being applied for that control

When Settings is null:

    show status
    disable nested editors
    do not fabricate defaults

Do not display:

    default opacity 100
    default font Unispace
    default alignment Left

unless those values came from an authoritative ClawHUD snapshot.

---

# 25. Main UI labels / values

Use stable product labels.

## Display Mode

    Always
    In game only

## HUD Size

Map exact offsets:

    -2
    -1
    Default   (0)
    +1
    +2

Do not invent physical pixel sizes.

## Font

    Unispace
    Segoe UI Variable

## Alignment

    Left
    Center
    Right

## Background

    Full width
    Content width

## Background Opacity

    50% ... 100%
    step 5%

## Intel VRR Range Fix

    Off / On

Keep values tied exactly to protocol-v1 enums/bounds.

---

# 26. Intel VRR last-result display

When IntelVrrLastResult is null:

    do not fabricate a result

A compact read-only summary is enough.

When present, show meaningful fields such as:

    status
    panel name
    range before -> range after
    message

Timestamp may be shown if it fits the current Settings UI without clutter.

Do not add an Intel/IGCL implementation to SteamAddon.

Do not independently probe VRR state from the Addon.

ClawHUD remains the sole owner.

---

# 27. Main UI refresh

SettingsPage already receives the one startup Bootstrap snapshot and has explicit refresh methods for dynamic Runtime state.

Add:

    RequestClawHudRefresh()

and a private:

    RefreshClawHudAsync()

Initialization should request one ClawHUD capture.

MainWindow.OnFrontendStateInvalidated currently refreshes:

    status
    app update
    Steam FSE

Also request:

    SettingsContent.RequestClawHudRefresh()

Do not poll.

Do not add a DispatcherTimer.

StateInvalidated is the wake-up signal.

---

# 28. Main UI authoritative-apply suppression

Follow the existing SettingsPage pattern.

When an authoritative snapshot sets:

    ToggleSwitch.IsOn
    ComboBox.SelectedItem
    Slider.Value

suppress the corresponding UI event so rendering readback does not emit another mutation.

Use page-local booleans/operation guards.

Do not build a generic binding framework.

---

# 29. Main UI nested mutations

For discrete controls:

    user change
      -> disable that control while request is in flight
      -> MutateClawHudSettingAsync(...)
      -> render returned authoritative snapshot
      -> re-enable according to returned state

On exception:

    log
    -> perform one fresh CaptureClawHudAsync if possible
    -> otherwise keep last known top-level desired/runtime state
       and disable nested editor that cannot be verified

Do not roll back by assuming the old value is still authoritative.

---

# 30. Main UI opacity interaction

The Main UI may use a normal WinUI Slider because it has a real pointer/drag interaction.

Required protocol semantics:

    interactive value movement
      -> PreviewHudOpacity

    interaction settlement/release
      -> CommitHudOpacity

Programmatic authoritative Slider.Value assignment must emit neither request.

Keep implementation local to SettingsPage.

Do not create:

    global slider scheduler
    opacity manager
    multi-setting debounce framework

The existing ClawHudProcessController gate is sufficient Runtime serialization.

A small page-local in-flight/coalescing pattern is acceptable if needed to keep rapid pointer movement from opening many concurrent requests.

Do not add epochs/barriers for pathological timing.

The important real behavior is:

    final committed value is the user's final slider value
    authoritative Commit response wins
    failed preview/commit never changes Addon desired state

---

# 31. Overlay scope

CH-A3 includes the Addon Overlay.

It does NOT include Steam QAM.

Do not modify QamFrontendBridge/qam.js for ClawHUD in this PR.

The current architecture explicitly scoped CH-A3 user control to:

    Main UI
    Addon Overlay

QAM can be evaluated separately later if desired.

---

# 32. Do not extend QuickSettingsPageId

This is non-negotiable.

Do NOT add:

    QuickSettingsPageId.Setting
    QuickSettingsPageId.ClawHud
    QuickSettingsPageId.Hud

Device/Profile generic Quick Settings remain unchanged.

ClawHUD belongs to the existing Setting tab through a dedicated typed contract.

Do not route ClawHUD through:

    QuickSettingsMutationIntent
    QuickSettingsRowId
    QuickSettingsMutationAdapter

Those are Device/Profile contracts.

---

# 33. Overlay Setting page composition

Current Setting page is only the Tab Order editor.

Refactor narrowly:

    BuildTabOrderEditorPage(...)
      -> BuildSettingPage(...)
           -> HUD section
           -> existing Tab Order section

Recommended visible order:

    HUD
      Enabled
      Runtime status
      Display Mode
      HUD Size
      Font
      Alignment
      Background
      Opacity
      Intel VRR Range Fix
      VRR result/status

    Tab Order
      Device
      Controller
      Profile
      Shortcut
      Setting

Keep the existing five tab-order row instances/authority.

Do not rewrite tab ordering.

Do not move tab-order persistence into the Overlay.

---

# 34. Reuse Overlay row primitives

Use existing:

    OverlayToggleRow
    OverlayValueRow
    OverlayRowCapabilities
    OverlayRowSelection

for ClawHUD controls where they fit.

Recommended:

    Enabled
      -> OverlayToggleRow

    Display Mode
      -> OverlayValueRow, discrete two values

    HUD Size
      -> OverlayValueRow, -2..+2

    Font
      -> OverlayValueRow, discrete two values

    Alignment
      -> OverlayValueRow, discrete three values

    Background
      -> OverlayValueRow, discrete two values

    Opacity
      -> OverlayValueRow, 50..100 step 5

    Intel VRR Range Fix
      -> OverlayToggleRow

Runtime status / VRR result are read-only TextBlocks and are not selectable rows.

Do not create a new generic Overlay form framework.

---

# 35. Overlay row availability

Top-level Enabled row:

    selectable whenever the Runtime frontend can accept a top-level request
    reflects DesiredEnabled

Nested rows:

    selectable only when
      RuntimeState == Ready
      Settings != null
      no matching operation is in flight

When Off / Starting / Unavailable / StandaloneConflict:

    nested rows remain visible but disabled
    status text explains why

This keeps the Setting page layout stable and avoids rebuild churn.

The existing Tab Order rows remain selectable regardless of ClawHUD state.

A ClawHUD failure must not disable tab-order editing.

---

# 36. Overlay selection semantics

The Setting page now contains more rows than the five Tab Order rows.

Update its _pageRows composition so controller Up/Down sees:

    selectable ClawHUD rows
    then selectable Tab Order rows

Read-only status text is never in _pageRows.

When a ClawHUD snapshot changes nested availability:

    call the existing selection normalization
    preserve current selected row when it is still selectable
    otherwise fall back to the first selectable row

Do not create a second selection model for ClawHUD.

Shortcut remains the only page with its dedicated shortcut selection owner.

---

# 37. Overlay opacity — use current discrete UI honestly

Current OverlayValueRow is a discrete Left/Right stepper, not a drag Slider.

Therefore do NOT add preview/debounce/state-machine machinery solely to imitate a gesture the current Overlay does not have.

For the current Overlay:

    one accepted 5% Left/Right step
      -> CommitHudOpacity

The authoritative Commit response updates the row.

Main UI, which has a real Slider drag, uses Preview + Commit as described earlier.

If a future Overlay replaces the value row with a real draggable slider, it can adopt PreviewHudOpacity then.

This is deliberately simpler and matches the current product UI.

---

# 38. Dedicated Overlay ClawHUD transport

The Overlay process must not use the desktop frontend pipe directly and must not open ClawHUD IPC.

Extend the existing Overlay transport.

Current reviewed Overlay protocol:

    OverlayTransportProtocol.CurrentVersion = 9

CH-A3 adds wire-visible ClawHUD messages.

Bump:

    9 -> 10

Add a narrow ClawHUD snapshot publish and correlated mutation request/response using the SAME public frontend ClawHUD contracts.

Do not duplicate product enums inside OverlayWire.

Conceptually:

    Runtime -> Overlay
      FrontendClawHudSnapshot

    Overlay -> Runtime
      top-level Enabled request
      OR nested FrontendClawHudMutationIntent

    Runtime -> Overlay
      authoritative FrontendClawHudMutationResult / snapshot

Use one strict discriminated wrapper if needed.

Reject a malformed request that supplies both top-level Enabled and nested mutation.

---

# 39. OverlayProcessController authority binding

Add one narrow binding beside the existing Tab Order / Quick Settings bindings.

Conceptually:

~~~text
BindClawHudAuthority(
    capture,
    setEnabled,
    mutateSetting)
~~~

These delegates must point to the same Runtime frontend authority used by Main UI.

Do not bind:

    SettingsStore
    ClawHudControlClient
    ClawHudProcessController

directly into the Overlay server.

The path remains:

    Overlay
      -> Overlay pipe
      -> OverlayProcessController/server
      -> _frontendControl
      -> Runtime ClawHUD authority

---

# 40. Overlay Show publication

Current successful Overlay capture commit does fire-and-forget refresh for:

    Device/Profile Quick Settings
    Tab Order

Also schedule:

    RefreshClawHudAsync()

after capture has committed.

Do not await ClawHUD settings capture before the Overlay can become visible.

Reason:

    ClawHUD IPC can take up to its bounded timeout
    optional HUD settings must never lengthen controller capture/presentation pause admission

The Setting page may temporarily show:

    Loading…

until the first ClawHUD snapshot arrives.

No polling is required.

---

# 41. StateInvalidated -> visible Overlay refresh

Current visible/captured Overlay refreshes Device/Profile + Tab Order from StateInvalidated.

Also request one ClawHUD refresh while:

    Overlay capture active
    Overlay visible
    process shutdown not started

Do not add a dedicated timer.

Do not add an independent ClawHUD observer inside Overlay.exe.

---

# 42. Overlay mutation result handling

Every successful Runtime mutation reply carries authoritative state.

Overlay must:

    apply returned snapshot
    update controls from readback
    clear local failure text

Typed product failure:

    keep Overlay alive
    show local ClawHUD failure/status
    apply any authoritative snapshot included in result

Transport/operation exception:

    do not synthesize success
    show a bounded local failure
    request one fresh ClawHUD capture when appropriate

Do not close the whole Overlay because one ClawHUD mutation failed.

Do not release controller capture because one ClawHUD mutation failed.

---

# 43. Overlay hide / shutdown

No special ClawHUD process action occurs when the Overlay hides.

Overlay lifetime is only a frontend lifetime.

Required:

    Overlay hide
      -> no ClawHUD shutdown
      -> no ClawHudEnabled change
      -> no nested setting reset

ClawHUD process lifetime remains controlled only by:

    top-level Enable HUD setting
    Addon controlled process shutdown
    CH-A2 lifecycle

Do not connect Overlay visibility to ClawHUD process lifetime.

---

# 44. Unexpected ClawHUD child exit while UI is open

Real scenario:

    DesiredEnabled=true
    Managed child Ready
    ClawHUD crashes/exits
    CH-A2 ObserveChildExitAsync records Unavailable

CH-A3 must propagate this through the one StateInvalidated notification.

Expected:

    Main UI
      -> refresh
      -> toggle stays On
      -> status becomes Unavailable
      -> nested controls disabled

    visible Overlay
      -> refresh
      -> Enabled remains On
      -> status becomes Unavailable
      -> nested rows disabled
      -> Tab Order remains usable

Do NOT automatically restart ClawHUD from the UI notification.

Retry remains explicit / next lifecycle reconcile.

---

# 45. Standalone conflict UX

When:

    DesiredEnabled=true
    RuntimeState=StandaloneConflict

Main UI and Overlay must not:

    call RequestShutdown
    kill Standalone
    adopt Standalone
    mutate Standalone settings

Display a clear message.

Main UI may provide Retry.

Retry simply calls:

    SetClawHudEnabledAsync(true)

after the user closes Standalone.

No process enumeration is needed.

---

# 46. Top-level Off failure semantics

CH-A2 can encounter a real case where an adopted Managed Runtime acknowledges poorly or shutdown cannot be confirmed.

The persisted user intent is still:

    DesiredEnabled=false

Do not flip the UI switch back On merely because shutdown cleanup failed.

Display:

    Off desired
    actual cleanup unavailable/failure

If the returned CH-A2 state is Unavailable, preserve that distinction in the frontend snapshot.

Do not force-kill an adopted/unproven process from CH-A3.

---

# 47. Sleep / Hibernate / Resume

Do not add CH-A3 suspend/resume machinery.

ClawHUD already owns its internal suspend/resume.

SteamAddon Full1902 already owns controller suspend/resume.

UI state after a real runtime change converges through:

    existing state invalidation
    page refresh
    next open/capture

Do not add:

    resume timer
    resume retry loop
    resume epoch
    ClawHUD restart-on-resume

---

# 48. Controlled Addon restart / shutdown

CH-A3 must not alter CH-A2 shutdown ordering.

The UI may disappear while ClawHUD shutdown is still completing.

That is normal.

Do not make:

    Main UI Close
    Overlay Hide

mean:

    stop ClawHUD

Only Addon Runtime process shutdown invokes CH-A2 Managed shutdown.

Controller-critical teardown remains independent.

---

# 49. Logging

Use existing AppLog / OverlayLog.

Suggested Runtime categories:

    ClawHUD.UI
    ClawHUD.IPC
    ClawHUD.Process

Useful fields:

    Operation
    DesiredEnabled
    RuntimeState
    RuntimeVersion
    ApplicationVersion
    Failure
    Setting
    RequestedValue
    ReturnedValue

Overlay may log:

    ClawHudStateApplied
    ClawHudMutationRequested
    ClawHudMutationSettled
    ClawHudMutationFailed

Do not log every opacity preview at Info.

High-frequency preview detail, if any, belongs at Debug.

Never dump binary protocol frames.

---

# 50. Expected production files

Likely new:

    src/SteamInputAddonforClaw.Contracts/Frontend/ClawHudFrontendContracts.cs

Likely modified:

    src/SteamInputAddonforClaw/ClawHud/ClawHudControlClient.cs
    src/SteamInputAddonforClaw/ClawHud/ClawHudProcessController.cs
    src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
    src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

    src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

    src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
    src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
    src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
    src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

    src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

    src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
    src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
    src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs

    src/SteamInputAddonforClaw.Overlay/App.xaml.cs
    src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs

A small dedicated Overlay partial such as:

    OverlayWindow.ClawHud.cs

is acceptable if it keeps Shell.cs focused.

Do not create a manager/service hierarchy to reduce file size.

---

# 51. ClawHudControlClient tests

Extend existing tests to prove exact request mapping for:

    SetHudVisibilityMode
    SetHudSizeOffset
    SetHudFont
    SetHudAlignment
    SetHudBackgroundMode
    PreviewHudOpacity
    CommitHudOpacity
    SetIntelVrrRangeFixEnabled

At minimum prove:

    exact operation ID
    exact payload type/size
    valid range/enum
    authoritative returned SettingsSnapshot used

Also prove invalid:

    size < -2 / > +2
    opacity not 50..100
    opacity not 5% step
    unknown enum

causes zero transport mutation / encode failure as appropriate.

No live ClawHUD required.

---

# 52. Process-controller nested setting tests

Add focused tests proving:

## Not Ready

    RuntimeState != Ready
    -> CaptureSettings / mutation rejected feature-locally
    -> control client mutation call count = 0
    -> no process start/acquisition
    -> no Full1902 call

## Ready capture

    Ready
    -> GetSettingsSnapshot
    -> authoritative internal snapshot returned

## Ready mutation

    Ready
    -> exact control client mutation
    -> authoritative returned snapshot propagated

## Nested mutation failure

    timeout / transport / protocol error
    -> mutation result failure
    -> DesiredEnabled unchanged
    -> process not killed
    -> process not restarted
    -> controller authority untouched

## Internal HudEnabled

Prove CH-A3 does not expose a second user mutation that sends:

    SetHudEnabled(false)

Top-level Off must still use CH-A2 StopAsync / RequestShutdown.

---

# 53. Frontend contract tests

Add tests for:

    FrontendClawHudSnapshot JSON round-trip
    nested enum round-trip
    optional Intel VRR result round-trip
    null Settings when unavailable
    mutation intent strict validation
    authoritative mutation result round-trip

Do not rely on enum numeric coincidence across wire/frontend models.

---

# 54. Frontend Named Pipe tests

Update:

    FrontendTransportProtocol 38 -> 39

Cover:

    CaptureClawHud no-payload round trip
    SetClawHudEnabled true/false round trip
    MutateClawHudSetting each mutation-kind family
    authoritative result returned
    CaptureClawHud with unexpected payload rejected
    malformed mutation rejected
    cancellation
    old protocol rejected

Keep all existing frontend transport tests green.

---

# 55. In-process frontend tests

Prove:

    CaptureClawHud is read-only
    CaptureClawHud does not raise StateInvalidated
    SetClawHudEnabled invokes only the supplied top-level delegate
    nested mutation invokes only the supplied nested delegate
    committed mutation raises StateInvalidated once
    opacity Preview does not create global StateInvalidated spam
    opacity Commit raises once
    absent ClawHUD delegate fails closed

Do not require a real process.

---

# 56. Main UI tests

Follow existing UI/source-contract test style where full WinUI hosting is unnecessary.

At minimum prove source/contract behavior:

    Settings page contains one ClawHUD group
    top-level switch exists
    nested editors exist
    StartWithWindows is absent
    second HudEnabled switch is absent
    RequestClawHudRefresh is wired from MainWindow StateInvalidated
    authoritative-apply suppression exists
    desired On + unavailable keeps switch On
    nested editors disabled without authoritative Settings
    Retry, if implemented, calls top-level On rather than a new retry service

Keep UI visual polish modest.

This is functionality, not a redesign of the Settings page.

---

# 57. Overlay transport tests

Bump:

    OverlayTransportProtocol 9 -> 10

Cover:

    Runtime publishes FrontendClawHudSnapshot
    Overlay client receives it
    top-level enabled mutation request correlates to response
    nested setting mutation request correlates to response
    malformed mixed request rejected
    transport failure is not synthesized as success
    protocol mismatch rejected

Keep existing:

    command
    navigation
    tab-order
    Device/Profile Quick Settings

transport tests green.

---

# 58. Overlay Setting page tests

Required:

    Setting page contains HUD section + existing Tab Order section
    existing five tab-order rows still exist
    tab-order move behavior unchanged
    Enabled row selectable when appropriate
    nested ClawHUD rows disabled when not Ready
    nested rows selectable when Ready + Settings available
    read-only status text is not a selectable row
    selection skips disabled nested rows
    applying authoritative snapshot emits zero mutation
    controller Left/Right uses the expected nested mutation
    opacity Left/Right uses CommitOpacity directly
    VRR result text reflects authoritative snapshot

Do not extend QuickSettingsPageId in tests or production.

Add a regression assertion:

    Enum.GetNames<QuickSettingsPageId>()
    does not contain "Setting"
    does not contain "ClawHud"
    does not contain "Hud"

---

# 59. Overlay lifecycle tests

Prove:

    successful Overlay capture schedules ClawHUD refresh after capture commit
    slow ClawHUD capture does not delay Show/capture commit
    StateInvalidated refreshes ClawHUD only while Overlay is visible/captured
    Overlay hide does not stop ClawHUD
    ClawHUD mutation failure does not release Overlay capture
    ClawHUD mutation failure does not trigger controller reconcile
    ClawHUD child-exit invalidation updates visible Overlay state

Do not add polling tests because production must not poll.

---

# 60. Full1902 regression contract

Explicitly keep regression coverage proving CH-A3 cannot call controller authority code.

No ClawHUD frontend operation may call:

    CenterMRebootAuthorityTransition
    MsiClawAddonPhysicalOwnership
    AddonControllerHidHideBaseline
    CanonicalViiperRuntime
    controller presentation reconcile
    firmware mode switch

A source-contract test is acceptable if that is the project's current style.

The important product behavior is:

    HUD frontend failure
      != controller recovery trigger

---

# 61. Manual validation — MSI Claw

After automated tests pass, validate on real supported hardware.

## Case A — first state Off

    ClawHudEnabled=false
    -> Addon starts
    -> no ClawHUD process
    -> Main UI shows HUD Off
    -> Overlay Setting shows Enabled Off
    -> nested controls disabled
    -> controller works normally

## Case B — turn On from Main UI

    Toggle On
    -> exact Runtime acquisition if missing
    -> ClawHUD.exe --managed
    -> Ready
    -> HUD visible
    -> nested settings appear
    -> controller remains uninterrupted

## Case C — change nested settings from Main UI

Validate:

    Display Mode
    Size
    Font
    Alignment
    Background
    Opacity
    Intel VRR Range Fix

Close/reopen Main UI and confirm ClawHUD authoritative values persist.

## Case D — Overlay parity

Open Overlay -> Setting.

Confirm:

    Enabled matches Main UI desired state
    nested settings match
    every change reaches ClawHUD
    close/reopen Overlay -> values remain
    Tab Order editor still works

## Case E — opacity

Main UI:

    drag Slider
    -> live preview
    release/settle
    -> commit
    -> close/reopen
    -> committed value remains

Overlay:

    Left/Right one 5% step
    -> direct commit
    -> reopen
    -> value remains

## Case F — Standalone conflict

Start Standalone ClawHUD first.

Then enable Addon HUD.

Required:

    desired switch remains On
    StandaloneConflict displayed
    Standalone process untouched
    nested editors disabled

Close Standalone.

Use Main UI Retry or Off -> On.

Required:

    Managed Runtime starts
    -> Ready

## Case G — unexpected Managed child exit

While desired On and Ready:

    terminate only the Addon-managed ClawHUD process

Required:

    controller remains usable
    Addon remains running
    Main UI / visible Overlay updates to Unavailable
    desired toggle remains On
    no automatic restart loop

Then explicit retry should recover.

## Case H — controlled Addon restart

    desired On
    -> Restart Addon
    -> CH-A2 controlled shutdown stops Managed ClawHUD
    -> replacement Addon starts
    -> Full1902 controller recovery happens first
    -> optional ClawHUD starts afterward
    -> UI returns Ready

## Case I — sleep / resume

    desired On + Ready
    -> sleep
    -> resume

Required:

    existing Full1902 resume path works
    ClawHUD owns its own suspend/resume behavior
    no duplicate Addon ClawHUD restart/reconcile loop
    UI remains usable after normal refresh

---

# 62. Build / automated validation

Run the repository-supported full baseline.

At minimum:

~~~powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build -p:IsTestProject=true

dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build -p:IsTestProject=true
~~~

Also run the normal publish/layout verification used by current main:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-publish-assets.tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-layout.ps1 -Version 0.1.0 -Configuration Release -PublishDirectory artifacts/publish -NoRestore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-publish-assets.ps1 -PublishDirectory artifacts/publish
~~~

Run focused ClawHUD/frontend/Overlay tests as a fast local loop, but do not replace the full suite with focused tests.

---

# 63. Explicit non-goals

Do NOT include:

    Steam QAM ClawHUD UI
    QAM React changes
    new QuickSettingsPageId
    generic Setting-page row schema
    generic plugin/provider registry
    ClawHUD process watchdog
    automatic crash restart loop
    ClawHUD heartbeat
    persistent ClawHUD pipe
    Windows service
    process scanning/WMI ownership
    Job Object ownership
    PID journal
    ClawHUD standalone management
    Standalone kill/adoption
    ClawHUD StartWithWindows control
    PresentMon installer UI owned by SteamAddon
    Intel IGCL implementation inside SteamAddon
    ClawHUD settings.ini parsing/writing
    duplicate nested setting persistence in AppSettings
    Full1902 controller changes
    Settings-page redesign unrelated to ClawHUD
    Overlay tab redesign unrelated to ClawHUD
    CH-A4 dependency automation changes

CH-A4 remains a separate operational/dependency-update scope.

---

# 64. Overengineering / race policy

Follow the project production-review policy.

Must handle realistic cases:

    ClawHUD process exits while UI is open
    Addon controlled restart
    Addon crash with Managed survivor
    Standalone conflict
    IPC timeout/failure
    nested mutation failure
    Overlay show/hide
    sleep/resume
    process shutdown

Do NOT add extra state/lock/epoch/barrier machinery for:

    exact instruction-level overlap between StateInvalidated and one UI callback
    an artificial interleaving between preview response and tab-order update
    pathological scheduler timing between two already serialized ClawHUD operations

Reuse:

    existing ClawHudProcessController gate
    existing frontend StateInvalidated
    existing Overlay capture authority
    existing Overlay row selection
    existing CH-A2 process ownership

If current owners converge to authoritative state after a realistic operation, keep the implementation simple.

---

# 65. Acceptance checklist

## Authority

- [ ] Full1902 controller authority is unchanged.
- [ ] AppSettings.ClawHudEnabled remains the sole top-level desired-state authority.
- [ ] Nested ClawHUD settings are not persisted by SteamAddon.
- [ ] UI/Overlay never open ClawHUD Control IPC directly.
- [ ] No second ClawHUD process owner exists.

## Runtime seam

- [ ] Existing ClawHudControlClient gains the required nested setting operations.
- [ ] Existing ClawHudProcessController gate is reused.
- [ ] Nested operations require Managed Ready.
- [ ] Nested operations never launch/download ClawHUD.
- [ ] StartWithWindows is not exposed or sent.
- [ ] Mutation readback is authoritative.

## Frontend

- [ ] One typed FrontendClawHudSnapshot exists.
- [ ] One closed typed nested mutation contract exists.
- [ ] CaptureClawHud is read-only.
- [ ] Top-level On/Off uses CH-A2 SetClawHudEnabledAsync.
- [ ] Frontend protocol is bumped 38 -> 39.
- [ ] Named Pipe server/client cover capture + top-level mutation + nested mutation.
- [ ] Unexpected child exit reaches existing StateInvalidated without polling.

## Main UI

- [ ] Settings page contains one ClawHUD group.
- [ ] Top-level toggle binds to DesiredEnabled.
- [ ] On + Unavailable keeps the toggle On.
- [ ] Nested editors require Ready + authoritative Settings.
- [ ] No second HudEnabled switch exists.
- [ ] Display Mode / Size / Font / Alignment / Background / Opacity / Intel VRR are controllable.
- [ ] VRR result is read-only.
- [ ] Main UI opacity uses Preview during real Slider interaction and Commit at settlement.
- [ ] Retry is explicit and feature-local if implemented.
- [ ] No polling.

## Overlay

- [ ] Setting tab contains HUD section plus existing Tab Order section.
- [ ] Existing five Tab Order rows remain fully functional.
- [ ] QuickSettingsPageId remains Device/Profile only.
- [ ] Existing Overlay row primitives are reused.
- [ ] Overlay protocol is bumped 9 -> 10.
- [ ] Overlay receives Runtime-owned ClawHUD snapshot.
- [ ] Overlay mutations return authoritative snapshot/result.
- [ ] Overlay opacity discrete steps use Commit directly.
- [ ] Overlay hide does not stop ClawHUD.
- [ ] Slow ClawHUD capture does not delay Overlay capture admission/show.
- [ ] ClawHUD failure does not release controller capture.

## Failure / lifecycle

- [ ] Standalone conflict never kills/adopts Standalone.
- [ ] IPC failure never triggers Full1902 recovery.
- [ ] Unexpected child exit invalidates UI state but does not auto-restart.
- [ ] Controlled Addon restart still uses CH-A2 shutdown.
- [ ] Sleep/resume adds no duplicate ClawHUD lifecycle.
- [ ] Desired state is never silently changed because actual Runtime is unavailable.

## Regression

- [ ] Debug build passes.
- [ ] Debug full tests pass.
- [ ] Release build passes.
- [ ] Release full tests pass.
- [ ] publish asset tests pass.
- [ ] publish layout verification passes.
- [ ] QAM remains functionally unchanged.
- [ ] Full1902 controller lifecycle tests remain green.

---

# 66. Final implementation principle

CH-A3 is not a second ClawHUD integration layer.

It is only the frontend projection of the authority already created by CH-A1/A2.

The final ownership must remain easy to explain:

    SteamAddon setting
      -> whether Managed ClawHUD should exist

    AddonProcessHost + ClawHudProcessController
      -> whether the exact Managed Runtime actually exists / is Ready

    ClawHUD Control IPC
      -> authoritative nested HUD settings

    Main UI + Overlay
      -> render those facts and submit typed user intent

No other owner is needed.
