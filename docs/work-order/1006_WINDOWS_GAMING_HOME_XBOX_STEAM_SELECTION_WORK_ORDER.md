# Work Order — Generalize Windows Gaming Home Settings for Xbox + Steam Big Picture

> **Date:** 2026-10-06
> **Repository:** onehoon/SteamAddonforClaw
> **Reviewed baseline:** main@ecaba4878901941072a305a3e71d578db9572164
> **Product scope:** standalone Full1902 SteamAddonforClaw
> **Feature surface:** Main App → Settings → Windows Gaming Full Screen Experience
> **Architecture authorities:**
> - docs/Full 1902 Implementation/README.md and its active authority chain
> - docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
> - docs/XBOX_GAME_PROFILE_ARCHITECTURE/XBOX_GAME_PROFILE_ARCHITECTURE_2026-10-05.md
> - existing Steam FSE implementation/work orders, especially the AnyFSE-style lazy-registration direction
>
> **Important protocol note:** another concurrent work item may change FrontendTransportProtocol.CurrentVersion before this work is implemented. This work order intentionally does **not** prescribe a numeric target protocol version. See section 11.

---

## 1. Goal

Replace the current Steam-only Full Screen Experience toggle with a small Windows Gaming Home selector that can represent the actual Windows configuration model:

~~~text
Windows Gaming Home app
    Off / Desktop
    Xbox
    Steam Big Picture

Windows startup behavior
    Start in Gaming Full Screen Experience on startup
    On / Off
~~~

The two settings must be independent.

The current product UI compresses both Windows concepts into one Steam-specific boolean:

~~~text
Steam Big Picture Full Screen Experience [ON/OFF]
~~~

That is no longer sufficient now that the Addon is adding first-class XBOX/Game Pass support.

Target user-facing model:

~~~text
Windows Gaming Full Screen Experience        [ Xbox ▼ ]
Choose the home app used by Windows gaming full screen experience.

    Start in Full Screen Experience on startup   [ ON ]
~~~

Recommended selector values:

~~~text
Off
Xbox
Steam Big Picture
~~~

If Windows currently points to another Gaming Home application not owned by this feature, represent that state truthfully as an Other/external readback state rather than lying and showing Off.

Do not turn this into a generic launcher framework.

---

## 2. Current source facts verified at review

Current implementation already has a useful narrow Windows Gaming Home owner:

~~~text
src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
    WindowsGamingHomeConfiguration
    WindowsGamingConfigurationStore
~~~

The Windows state store currently reads/writes:

~~~text
HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration

GamingHomeApp
StartupToGamingHome
~~~

The current Steam enable path already performs more than MSIX registration.

It effectively does:

~~~text
Steam selected / enabled
→ verify supported OS
→ inspect fixed Addon FSE Home package
→ lazily register package if missing/old
→ derive actual PackageFamilyName + "!App"
→ write GamingHomeApp = Addon FseHome AUMID
→ write StartupToGamingHome = true
→ read back both values
~~~

The current OFF path does:

~~~text
delete GamingHomeApp
StartupToGamingHome = false
~~~

The current frontend contract, however, collapses these two Windows values into:

~~~csharp
FrontendSteamFseSnapshot(
    bool Available,
    bool Enabled,
    string? UnavailableReason)
~~~

and Enabled is true only when:

~~~text
GamingHomeApp == our Steam FseHome AUMID
AND
StartupToGamingHome == true
~~~

Therefore these legitimate Windows states are currently not representable correctly in the Main App:

~~~text
Steam selected + startup OFF
Xbox selected + startup ON/OFF
~~~

This is the primary model defect to correct.

---

## 3. Microsoft platform model

Microsoft documents two separate Windows Gaming Configuration settings:

~~~text
GamingHomeApp
    Set the gaming app used for the full screen experience.

StartupToGamingHome
    Determine whether the gaming full screen experience launches on startup.
~~~

Microsoft references:

- https://learn.microsoft.com/windows-hardware/customize/desktop/unattend/microsoft-windows-gaming-configuration
- https://learn.microsoft.com/windows-hardware/customize/desktop/unattend/microsoft-windows-gaming-configuration-startuptogaminghome
- https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers

The existing Addon/AnyFSE implementation maps these concepts to the current-user GamingConfiguration state.

Preserve that narrow state owner for this PR.

Do **not** add undocumented Windows internal configuration writers, StateRepository mutation, Settings-app automation, or private Xbox shell APIs merely because the Windows Settings UI has not yet been field-verified to refresh correctly.

That field issue is explicitly deferred to section 15.

---

## 4. Product state model

Introduce one small product enum for the selected Gaming Home.

Recommended frontend/product shape:

~~~csharp
public enum FrontendGamingHomeSelection
{
    None,
    Xbox,
    SteamBigPicture,
    Other
}
~~~

Other is a readback state. It is not a launcher type the user configures through this Addon.

Recommended snapshot:

~~~csharp
public sealed record FrontendGamingHomeSnapshot(
    bool Available,
    FrontendGamingHomeSelection Selection,
    bool StartupEnabled,
    string? UnavailableReason);
~~~

Semantics:

~~~text
GamingHomeApp absent
→ Selection = None

GamingHomeApp == Xbox AUMID
→ Selection = Xbox

GamingHomeApp == current registered Addon FseHome AUMID
→ Selection = SteamBigPicture

GamingHomeApp contains another non-empty AUMID
→ Selection = Other
~~~

StartupEnabled must report the actual StartupToGamingHome value independently from Selection.

Do not persist a second Addon-owned selection boolean/string.

Windows GamingConfiguration remains authoritative.

---

## 5. UI design

Replace the existing SettingsCard:

~~~text
Steam Big Picture Full Screen Experience [Toggle]
~~~

with one SettingsExpander using the application's existing CommunityToolkit WinUI settings language.

Recommended:

~~~text
Header:
Windows Gaming Full Screen Experience

Description:
Choose the home app used by Windows gaming full screen experience.

Header/content control:
ComboBox
    Off
    Xbox
    Steam Big Picture
~~~

Inside the expander add one SettingsCard:

~~~text
Header:
Start on Windows startup

Description:
Start Windows directly in the selected gaming home.

Control:
ToggleSwitch
~~~

The expander should be collapsed by default.

Do not add:

- Apply button;
- Restart button;
- confirmation dialog;
- a link to Windows Settings as part of the normal mutation;
- a new top-level page;
- launcher icons/assets solely for this selector;
- Playnite/custom launcher selection;
- Game Bar configuration.

### 5.1 Off behavior

When Selection == None:

~~~text
ComboBox = Off
Startup toggle = false
Startup toggle disabled
~~~

### 5.2 Xbox / Steam behavior

When selection is Xbox or Steam:

~~~text
Startup toggle enabled
Startup toggle reflects actual StartupToGamingHome
~~~

Changing Xbox ↔ Steam must not silently change the startup toggle.

### 5.3 Other/external state

If another Gaming Home AUMID is currently configured:

~~~text
Selection = Other
~~~

The UI must clearly show an external/other state such as:

~~~text
Other Windows app
~~~

Do not silently show Off.

The user may then explicitly choose Off, Xbox, or Steam Big Picture.

The Addon does not need to expose arbitrary third-party AUMIDs or manage them generically.

While Other is the current selection, the startup toggle may display the actual value but should not be user-writable from the Addon. The Addon should not mutate the startup policy of an unknown external Gaming Home app.

---

## 6. Exact Xbox contract

Use the already field-used Xbox PC app identity:

~~~text
Package identity:
Microsoft.GamingApp

Package family:
Microsoft.GamingApp_8wekyb3d8bbwe

Application Id:
Microsoft.Xbox.App

AUMID:
Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App
~~~

The current repository already contains the same AUMID in:

~~~text
src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs
~~~

Do not invent a second differing Xbox identity.

Prefer one shared product constant in an appropriately narrow Windows/Xbox identity location if doing so removes the duplicate literal cleanly without creating a new abstraction hierarchy.

Do not move unrelated front-button behavior into the Gaming Home feature.

### Xbox selection mutation

Selecting Xbox must do only the bounded work required for Windows Gaming Home selection:

~~~text
verify supported Windows FSE
→ verify the Xbox PC app package/AUMID is installed/resolvable
→ preserve current StartupToGamingHome
→ write GamingHomeApp = Xbox AUMID
→ read back GamingHomeApp
→ verify exact match
→ return authoritative snapshot
~~~

No Addon MSIX is required for Xbox.

Do not:

- register the Addon FseHome package just because Xbox was selected;
- install the Xbox app;
- open Microsoft Store;
- change Xbox Game Bar;
- change Win+G suppression;
- change controller presentation;
- launch Xbox as part of the settings mutation.

If Xbox is not installed/resolvable:

~~~text
selection mutation fails
→ keep actual Windows state
→ return concise feature-local failure
~~~

Do not add an Xbox package watcher or repair service.

---

## 7. Exact Steam Big Picture contract

Preserve the existing fixed Addon Gaming Home package architecture.

Steam selection remains:

~~~text
Windows Gaming Home
→ SteamInputAddonforClaw.FseHome
→ existing FseHome boot handoff
→ Steam Big Picture
~~~

Required selection flow:

~~~text
Steam Big Picture selected
→ inspect exact Addon FseHome package
→ if missing/older, use existing lazy registration path
→ re-read actual registered package
→ derive actual FamilyName + "!App"
→ preserve current StartupToGamingHome
→ write GamingHomeApp = actual Addon FseHome AUMID
→ read back / verify
→ return authoritative snapshot
~~~

Do not change the Steam FSE package payload in this PR.

Therefore, unless implementation unexpectedly requires a real FseHome payload/manifest change:

~~~text
no FseHome source behavior change
no MSIX rebuild
no CER change
no fixed FSE package version bump
no signing-identity work
~~~

Keep current Steam-specific helper names where they truly remain Steam-specific:

~~~text
SteamFsePackageContract
WindowsSteamFsePackageProbe
SteamFseRegistrationClient
SteamFseRegistrationWorker
~~~

Do not rename these merely for symmetry.

Only the Windows Gaming Home selection owner and frontend surface become Steam/Xbox-aware.

---

## 8. Selection mutation semantics

Replace the current single SetEnabledAsync(bool) concept with one narrow selection mutation.

Suggested backend shape:

~~~csharp
Task<FrontendGamingHomeMutationResult> SetSelectionAsync(
    FrontendGamingHomeSelection selection,
    CancellationToken cancellationToken = default);
~~~

Allowed user mutation targets:

~~~text
None
Xbox
SteamBigPicture
~~~

Reject Other as a mutation target.

### None

~~~text
delete GamingHomeApp
write StartupToGamingHome = false
read back
success only when:
    GamingHomeApp absent
    AND StartupToGamingHome == false
~~~

This preserves the existing explicit OFF contract.

Do not restore a previous launcher.

### Xbox

~~~text
capture current StartupToGamingHome
validate Xbox app identity
write only GamingHomeApp = Xbox AUMID
read back
verify exact Xbox AUMID
verify StartupToGamingHome still equals the captured value
~~~

### Steam Big Picture

~~~text
capture current StartupToGamingHome
ensure existing Addon Steam FSE package registration
write only GamingHomeApp = actual current Addon FseHome AUMID
read back
verify exact Addon AUMID
verify StartupToGamingHome still equals the captured value
~~~

Do not write StartupToGamingHome during normal Xbox ↔ Steam selection changes.

If selection write/readback fails:

~~~text
return failure
→ recapture actual Windows state
→ UI renders actual state
~~~

A tiny local best-effort restoration of the prior GamingHomeApp is acceptable only if implementation remains obvious and bounded.

Do not add journals, epochs, transaction managers, retry loops, or background reconciliation.

---

## 9. Startup mutation semantics

Add one independent startup mutation.

Suggested backend shape:

~~~csharp
Task<FrontendGamingHomeMutationResult> SetStartupEnabledAsync(
    bool enabled,
    CancellationToken cancellationToken = default);
~~~

Rules:

### Current selection = Xbox or SteamBigPicture

~~~text
write StartupToGamingHome = requested value
read back
verify exact requested value
verify GamingHomeApp did not change
return authoritative snapshot
~~~

### Current selection = None

Do not allow startup=true without a selected Gaming Home.

Return the authoritative state with a concise rejected/failure result.

startup=false may be treated as idempotent.

### Current selection = Other

Do not mutate startup policy for a Gaming Home application the Addon does not own/select.

Return unavailable/rejected for mutation while still reporting the real snapshot.

No retry loop.

---

## 10. Rename the active frontend contract away from Steam-only naming

The Settings feature is no longer Steam-only, so the active frontend contract should stop calling the whole Windows Gaming Home state SteamFse.

Replace the active contract surface with Gaming Home naming, e.g.:

~~~text
FrontendSteamFseSnapshot
    → FrontendGamingHomeSnapshot

FrontendSteamFseMutationResult
    → FrontendGamingHomeMutationResult

CaptureSteamFseAsync
    → CaptureGamingHomeAsync

SetSteamFseEnabledAsync
    → SetGamingHomeSelectionAsync
      SetGamingHomeStartupAsync
~~~

Likewise replace the active RPC/request names:

~~~text
CaptureSteamFse
SetSteamFseEnabled
SetSteamFseEnabledRequest
~~~

with narrow Gaming Home equivalents.

Because Main App and Runtime ship together and the frontend handshake is versioned, do not keep duplicate old/new RPCs solely for compatibility.

Do not add a compatibility shim unless implementation-time product requirements explicitly change.

Internal Steam package-registration helpers remain Steam-named as described in section 7.

---

## 11. Frontend transport protocol version — implementation-time rule

This PR changes frontend DTO/RPC semantics, so a protocol bump is required.

**Do not hard-code a target number from this work order.**

There is concurrent work that may advance the protocol before implementation.

Required implementation procedure:

~~~text
1. Rebase / update against the actual target HEAD.
2. Read the actual current:
       FrontendTransportProtocol.CurrentVersion
3. Increment it exactly once for this contract change.
4. Update the version-history comment using the newly assigned implementation-time number.
5. Update any exact-version tests to that actual value.
~~~

Concept only:

~~~text
actual HEAD version = N
this PR version     = N + 1
~~~

Do **not** assume any fixed pair such as 53→54 or 52→53.

If another PR lands first and changes the version, use the new actual HEAD as the base.

If this work is rebased after implementation and the protocol number conflicts, reconcile it to one increment from the rebased parent rather than preserving a stale number.

This is intentionally a semantic instruction rather than a numeric contract.

The Overlay transport protocol does not need to change for this Settings-only feature.

---

## 12. Main UI interaction rules

Replace the current Steam toggle-specific guard state with Gaming Home equivalents.

Preserve the useful current pattern:

~~~text
programmatic render guard
+ one mutation in flight
+ disable controls during mutation
+ render authoritative returned snapshot
+ on exception refresh actual state
~~~

Do not add debounce timers.

### Selection change

~~~text
real user selection
→ disable selector/startup control
→ SetGamingHomeSelectionAsync(...)
→ render returned authoritative snapshot
→ re-enable according to snapshot
~~~

### Startup toggle

~~~text
real user toggle
→ disable selector/startup control
→ SetGamingHomeStartupAsync(...)
→ render returned authoritative snapshot
→ re-enable according to snapshot
~~~

Programmatic ComboBox/Toggle rendering must not trigger a mutation.

On failure:

- render actual returned/refreshed Windows state;
- show one concise failure in the SettingsExpander/Card description;
- no ContentDialog;
- no success popup.

---

## 13. Windows state capture details

Keep WindowsGamingConfigurationStore as the one narrow owner of:

~~~text
GamingHomeApp
StartupToGamingHome
~~~

The capture path should no longer require the Steam Addon FseHome package to exist merely to report Windows Gaming Home state.

Required capture ordering:

~~~text
verify supported Windows FSE
→ read GamingHomeApp
→ read StartupToGamingHome
→ classify selected AUMID
~~~

Steam package inspection is needed to recognize the current Addon Steam AUMID and to prepare a Steam mutation.

Do not let a missing Steam package make the entire Xbox-capable Settings control unavailable.

For Steam recognition:

- inspect the exact owned package when present;
- derive the actual AUMID from FamilyName + ApplicationId;
- do not guess PFN.

For Xbox recognition:

- use the fixed supported Xbox AUMID above;
- package presence verification may use current-user package enumeration;
- do not scan WindowsApps directories.

If the Windows registry can be read but Steam package probing fails, do not incorrectly block Xbox/Off control unless that failure truly prevents safe classification of the current value.

Keep failure policy simple and evidence-based.

---

## 14. Uninstall and update behavior

### Uninstall

Keep current Addon-owned cleanup intent:

~~~text
delete GamingHomeApp
StartupToGamingHome = false
remove only exact Addon FseHome package
~~~

Do not accidentally remove or alter the Xbox app package.

No wildcard package cleanup.

No Microsoft.GamingApp removal.

### Normal Addon update

Do not change the selected Gaming Home or startup preference merely because the Addon updated.

If current selection is Xbox, leave Xbox selected.

If current selection is Steam, leave Addon FseHome AUMID selected.

If startup is off, do not turn it on.

No startup-time FSE package reconcile should be reintroduced.

---

## 15. Deferred field issue — Windows Settings UI did not visibly update

The current source already writes and verifies the two known GamingConfiguration values.

However, field observation from the supported device indicates that after changing the current Addon Steam FSE setting, the corresponding Windows Settings UI did not visibly appear to change.

This is a real observation, but the root cause is not yet proven.

Possible categories include, but are not limited to:

- test build predating the current registry mutation code;
- Windows Settings UI caching / delayed refresh;
- a separate Windows configuration projection/state not yet identified;
- the current registry mapping being sufficient for boot behavior but not immediately reflected by Settings UI;
- another Windows build-specific behavior.

**Do not solve this by speculation in this PR.**

Specifically do not add:

- undocumented StateRepository writes;
- Windows Settings process automation;
- private SystemSettings handlers;
- registry duplication into guessed keys;
- periodic synchronization;
- a second settings authority;
- internal Windows API reverse-engineering changes without field evidence.

For this implementation, retain the existing registry owner/readback behavior and improve diagnostics enough to validate it later.

Recommended concise mutation logging:

~~~text
GamingHome selection requested
previous GamingHomeApp
previous StartupToGamingHome
requested selection
written GamingHomeApp
readback GamingHomeApp
readback StartupToGamingHome

GamingHome startup requested
previous StartupToGamingHome
requested StartupToGamingHome
readback StartupToGamingHome
current GamingHomeApp
~~~

Do not dump unrelated registry state.

### Post-implementation field validation

Later hardware validation must compare all three layers:

~~~text
A. Addon authoritative snapshot
B. HKCU GamingConfiguration values
C. Windows Settings → Gaming → Full Screen Experience UI
~~~

Test separately:

~~~text
Off → Xbox
Xbox → Steam Big Picture
Steam Big Picture → Xbox

Startup OFF → ON
Startup ON → OFF
~~~

For each transition:

1. perform mutation in Addon;
2. record Addon readback;
3. inspect the registry values;
4. close/reopen Windows Settings and inspect the FSE page;
5. reboot/logon where relevant and verify actual startup behavior.

If registry readback is correct but Windows Settings UI remains stale/wrong, stop and create a separate focused Windows Gaming Configuration diagnostic/fix work item.

Do not expand this PR into an undocumented Windows-internals investigation.

This deferred UI-sync investigation is not a reason to preserve the current incorrect single-boolean frontend model.

---

## 16. Full1902 / controller boundary

This feature changes only Windows Gaming Home preference/presentation.

It must not modify the Full1902 controller authority contract:

~~~text
Center M Disabled
→ Addon Runtime owns PID1902
→ DirectInput Addon-owned
→ HidHide persistent baseline
→ VIIPER Addon-owned
~~~

Existing presentation policy also remains:

~~~text
Steam/BPM inactive
→ Xbox360

Steam game or BPM active
→ SteamDeck
~~~

Selecting Xbox as Windows Gaming Home does **not** mean:

~~~text
force Xbox360
change PID1902
change HidHide
detach SteamDeck
change Steam watcher state
change XBOX active-game detection
~~~

Likewise selecting Steam Big Picture as Windows Gaming Home does not become a second Steam-session authority.

The existing Steam/BPM watcher remains the presentation authority.

The XBOX game/profile architecture remains separate:

~~~text
Windows Gaming Home selection
≠ XBOX game identity
≠ XBOX profile selection
≠ front-button Xbox action
~~~

No new cross-domain manager is required.

---

## 17. Tests

Update focused tests only.

### 17.1 Windows Gaming Home capture

Required cases:

1. no GamingHomeApp, startup false → Selection=None, StartupEnabled=false.
2. Xbox AUMID, startup false → Selection=Xbox, StartupEnabled=false.
3. Xbox AUMID, startup true → Selection=Xbox, StartupEnabled=true.
4. current Addon FseHome AUMID, startup false → Selection=SteamBigPicture, StartupEnabled=false.
5. current Addon FseHome AUMID, startup true → Selection=SteamBigPicture, StartupEnabled=true.
6. another AUMID → Selection=Other and preserve actual StartupEnabled.
7. unsupported Windows → Available=false.

Do not collapse selection and startup back into one boolean in tests.

### 17.2 Selection mutation

Required:

- None clears GamingHomeApp and forces startup=false.
- Xbox writes exact Xbox AUMID.
- Xbox selection preserves startup=false.
- Xbox selection preserves startup=true.
- Xbox missing/unresolvable returns failure and does not claim success.
- Steam selection with current package writes actual PFN-derived AUMID.
- Steam selection with missing/old package invokes existing registration once.
- Steam selection preserves existing startup value.
- Other cannot be requested as a mutation target.
- wrong readback returns failure and authoritative snapshot.

### 17.3 Startup mutation

Required:

- Xbox + OFF→ON changes only startup.
- Xbox + ON→OFF changes only startup.
- Steam + OFF→ON changes only startup.
- Steam + ON→OFF changes only startup.
- None + request ON is rejected/fails closed.
- Other + startup mutation is rejected/not writable.
- wrong readback does not claim success.

### 17.4 Frontend transport

Update named-pipe round-trip tests for:

~~~text
CaptureGamingHome
SetGamingHomeSelection
SetGamingHomeStartup
FrontendGamingHomeSnapshot
FrontendGamingHomeMutationResult
~~~

Remove obsolete active Steam-only RPC assertions.

Protocol test rule:

~~~text
assert the actual implementation-time CurrentVersion
~~~

Do not copy a numeric version from this work order.

### 17.5 Main UI

Replace the old test that asserts one Steam FSE toggle.

Required UI contract:

- one Windows Gaming FSE SettingsExpander;
- one Gaming Home ComboBox;
- values for Off, Xbox, Steam Big Picture;
- one nested startup ToggleSwitch;
- selection and startup use separate mutation calls;
- programmatic render guard exists;
- overlapping mutation guard exists;
- startup toggle disabled for None;
- external/Other state is not rendered as Off;
- no Apply/Restart/confirmation button;
- no Windows Settings launch in normal mutation code.

### 17.6 Regression

Full suite must continue to prove:

- Steam FseHome package registration behavior remains intact;
- FseHome fixed package artifact contract remains unchanged unless source really changed;
- Full1902 controller tests unchanged;
- XBOX game catalog/session tests unchanged;
- front-button Xbox launch behavior unchanged;
- Overlay protocol unchanged.

---

## 18. Expected file scope

Exact files may change slightly with concurrent work, but expected primary touch points are:

~~~text
src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/SteamFseConfigurationTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.UiTests/SettingsPageUpdateTests.cs
~~~

A small shared Xbox AUMID constant may be introduced only if it removes the existing duplicate literal cleanly.

Do not modify without demonstrated necessity:

~~~text
src/SteamInputAddonforClaw.FseHome/*
FseHome MSIX/CER distribution artifacts
Steam/BPM watcher
Full1902 PID1902 ownership
HidHide
VIIPER
XBOX game session detector
XBOX catalog/profile architecture
WinG suppression
front-button execution policy
Overlay transport
~~~

---

## 19. Explicit non-goals / overengineering guardrail

Do not add:

- generic launcher provider interfaces;
- GamingHomeManager + provider hierarchy + strategy hierarchy;
- arbitrary launcher/AUMID editor;
- Playnite support;
- custom executable support;
- Xbox Game Bar integration;
- Xbox app installer;
- Microsoft Store automation;
- package watcher;
- registry watcher;
- background reconciliation timer;
- retry daemon;
- Windows Settings UI automation;
- undocumented StateRepository mutation;
- multi-user/RDP/Fast User Switching support;
- separate persisted Addon Gaming Home preference;
- startup epochs/locks/barriers for theoretical interleavings;
- controller/FSE synchronization;
- FseHome ↔ Runtime readiness handshake.

The product scope remains:

~~~text
one Windows user
one interactive session
one authoritative HKCU GamingConfiguration state
one bounded mutation
one readback
~~~

Real operation failures must be handled.

Pathological instruction-level races do not justify new architecture.

---

## 20. Acceptance criteria

Implementation is complete when all of the following are true:

1. Settings no longer exposes a Steam-only FSE boolean as the complete Windows state.
2. Settings has a Gaming Home selector with Off, Xbox, and Steam Big Picture.
3. Startup-to-FSE is a separate toggle.
4. Xbox and Steam selection preserve the current startup flag.
5. Off clears the selected home and forces startup=false.
6. Xbox selection uses Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App.
7. Xbox requires no Addon FSE MSIX registration.
8. Steam keeps the existing lazy fixed-FseHome registration path.
9. Steam selection derives the actual AUMID from the registered package FamilyName.
10. Missing/old Steam package is repaired only when Steam is selected.
11. Missing Xbox app fails locally without installing anything.
12. Another external Gaming Home is represented truthfully as Other, not Off.
13. The Addon does not mutate the startup policy of an unknown Other app.
14. Windows GamingConfiguration remains the source of truth; no duplicate persisted selection is added.
15. Active frontend DTO/RPC names are generalized from Steam-only naming.
16. The frontend protocol is incremented exactly once from the **actual implementation-time parent HEAD**, with no protocol number assumed by this work order.
17. No Overlay protocol change is made.
18. Existing fixed FseHome payload/artifact remains unchanged unless compilation evidence proves otherwise.
19. No FSE package version bump occurs solely for this Settings/backend refactor.
20. Full1902 PID1902/HidHide/VIIPER ownership is unchanged.
21. Steam/BPM X360↔SteamDeck presentation policy is unchanged.
22. XBOX game detection/profile architecture is unchanged.
23. Main UI uses guarded, authoritative refresh for both selector and startup toggle.
24. Focused unit/transport/UI tests pass.
25. Full test suite passes.
26. The known Windows Settings UI mismatch is logged as a deferred field-validation issue, not "fixed" with speculative Windows internals.
27. Later hardware validation compares Addon snapshot, registry readback, Windows Settings UI, and actual reboot behavior before any further Windows-side workaround is designed.

---

## 21. Final implementation principle

The current code models:

~~~text
Steam selected
AND
start FSE on boot
= one bool
~~~

The corrected product model is:

~~~text
Windows Gaming Home app
    Off | Xbox | Steam Big Picture

        +

Start Gaming Full Screen Experience on boot
    false | true
~~~

Steam-specific package registration remains a child implementation detail of selecting Steam.

Xbox uses the already-installed Microsoft Gaming App identity.

Windows GamingConfiguration remains the single state authority.

Do not add another authority merely because the Windows Settings UI still needs field validation.
