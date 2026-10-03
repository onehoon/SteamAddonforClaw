# Work Order — FSE Home Steam Big Picture Boot Handoff

> **Date:** 2026-10-03  
> **Reviewed Addon baseline:** main at 20ec84652da76d28e5acbe3cb5527feaf0051bf9  
> **Reference implementation reviewed:** ashpynov/AnyFSE main at 6a336ed4917528b2619f97c0d266bd695c0a9bb0  
> **Scope:** Windows Gaming Full Screen Experience Home launcher only  
> **PR shape:** one focused PR  
> **Product scope:** standalone Steam Addon for Claw. No CTW integration.

## Goal

Fix the boot-time Windows Gaming Full Screen Experience handoff from the Addon-owned Gaming Home package to Steam Big Picture.

The current FseHome is a fire-and-forget URI launcher:

~~~text
Windows activates SteamInputAddonforClaw.FseHome
→ Process.Start("steam://open/bigpicture")
→ FseHome exits immediately
→ Steam finishes startup later
~~~

This is a valid Gaming Home registration, but it leaves no Addon-owned fullscreen surface responsible for the visual transition while Steam Big Picture is still starting.

Hardware observation motivating this work:

~~~text
Addon FSE
→ Windows FSE activates
→ Steam Big Picture audio may already be audible
→ Windows login / transition surface can remain visible
→ Steam eventually becomes foreground

AnyFSE
→ Windows FSE activates
→ AnyFSE owns a fullscreen topmost handoff surface
→ Steam starts
→ AnyFSE waits for the actual Steam Big Picture HWND
→ Steam is foregrounded
→ handoff surface closes
~~~

The requested change is intentionally narrow:

~~~text
keep existing Gaming Home registration/package model
+ keep Full 1902 Runtime/controller architecture unchanged
+ add only the missing FseHome → Steam BPM visual/foreground handoff
~~~

This is not an Overlay fix. PR #653 separately hardened Overlay HWND topmost ownership. The FseHome handoff should be correct independently.

---

## 1. Canonical architecture constraints

Read and preserve:

- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md

FSE remains outside controller authority.

Do not make FseHome own or inspect:

- PID1901 / PID1902;
- DirectInput;
- HidHide;
- VIIPER;
- SteamDeck/X360 presentation selection;
- controller readiness;
- Runtime health;
- sleep/resume controller reconciliation.

Existing Runtime Steam/BPM observation remains the authority that selects Xbox360 versus SteamDeck presentation after BPM appears.

FseHome owns only this transient job:

~~~text
cover the boot transition
→ request Steam BPM
→ detect the real BPM window
→ hand foreground to Steam
→ exit
~~~

---

## 2. Current Addon behavior reviewed

Current launcher:

- src/SteamInputAddonforClaw.FseHome/Program.cs

It currently performs only:

~~~csharp
Process.Start(new ProcessStartInfo
{
    FileName = "steam://open/bigpicture",
    UseShellExecute = true,
});
~~~

and exits immediately.

Current Gaming Home package already has the required structural contracts:

- src/SteamInputAddonforClaw.FseHome/Packaging/AppxManifest.xml
- src/SteamInputAddonforClaw.FseHome/Packaging/CustomCapability.SCCD
- windows.gamingApp app extension
- Microsoft.appCategory.gamingHome_8wekyb3d8bbwe custom capability
- runFullTrust
- Windows.FullTrustApplication / win32App
- fixed PackageFamilyName-derived GamingHomeApp AUMID
- StartupToGamingHome registry configuration

Do not redesign these parts.

---

## 3. AnyFSE behavior used as reference

Reviewed reference files:

- ashpynov/AnyFSE/AppxManifest.xml
- ashpynov/AnyFSE/src/App/Main.cpp
- ashpynov/AnyFSE/src/App/MainWindow.cpp
- ashpynov/AnyFSE/src/App/MainWindow_Animation.cpp
- ashpynov/AnyFSE/src/App/Launchers.cpp
- ashpynov/AnyFSE/src/Configuration/Config.Launchers.cpp
- ashpynov/AnyFSE/src/Tools/Process.cpp
- ashpynov/AnyFSE/src/App/ExitFSE.cpp

Relevant proven behavior:

~~~text
Gaming Home process remains alive
→ creates WS_EX_TOPMOST + WS_POPUP fullscreen window
→ starts Steam.exe with steam://open/bigpicture
→ checks every 500 ms for Steam BPM
→ Steam BPM identity:
     process = steamwebhelper.exe
     class   = SDL_app
→ when launcher is ready:
     close splash / handoff surface
     focus launcher
→ only then finish Gaming Home lifecycle
~~~

AnyFSE currently uses a 60-second launcher timeout.

Use this as behavioral evidence only. Do not port its generic launcher framework, video splash system, startup-app framework, ASUS support, HID listener, exit-FSE manager, or settings architecture.

---

## 4. Important splash distinction

AnyFSE has two unrelated splash concepts.

### Package activation splash

Its manifest contains:

~~~xml
<uap:SplashScreen Image="Assets\SplashScreen.png" />
~~~

This is package activation metadata.

### AnyFSE application-owned splash

AnyFSE separately creates its own native top-level window:

~~~text
CreateWindowEx
→ WS_EX_TOPMOST
→ WS_POPUP
→ ShowWindow(SW_MAXIMIZE)
~~~

and optionally renders logo/text/video according to its own settings.

The application-owned window is what remains alive while waiting for Steam and is the relevant handoff mechanism.

### Decision for this PR

Do **not** add uap:SplashScreen as a required change in this PR.

We need to isolate and validate the actual missing behavior first:

~~~text
FseHome-owned fullscreen handoff HWND
→ Steam startup
→ BPM HWND readiness
→ foreground handoff
~~~

If hardware validation later shows a visible gap specifically before the FseHome HWND itself can be created, package activation splash can be evaluated in a separate tiny change.

Do not combine the two variables now.

---

## 5. Target FseHome lifecycle

Replace the current fire-and-forget lifecycle with:

~~~text
FseHome activation
→ set DPI awareness for native pixel geometry
→ create a minimal native fullscreen topmost handoff window
→ make it visible immediately
→ request Steam Big Picture
→ boundedly wait for an actual Steam Big Picture HWND
→ when BPM HWND is confirmed:
     remove/hide FseHome handoff surface
     show/foreground the BPM HWND
→ destroy FseHome window
→ exit process
~~~

Failure:

~~~text
Steam launch request fails
OR BPM is not confirmed before the deadline
→ remove handoff surface
→ exit FseHome
→ never remain indefinitely topmost
~~~

The handoff window is transient. It must not remain resident after successful Steam takeover.

---

## 6. Keep the window implementation minimal

Do not introduce WinUI, WPF, WinForms, WebView, or another UI framework into FseHome.

The current project is intentionally tiny:

- src/SteamInputAddonforClaw.FseHome/SteamInputAddonforClaw.FseHome.csproj

Implement the handoff surface with the existing .NET executable plus narrow Win32 P/Invoke.

Conceptual native shape:

~~~text
RegisterClass / RegisterClassEx
CreateWindowEx(
    WS_EX_TOPMOST,
    ...,
    WS_POPUP,
    full primary display bounds,
    ...
)
ShowWindow(...)
UpdateWindow(...)
~~~

The reference MSI Claw product scope is one interactive user/session and one handheld display. Do not build a generic multi-session/window-manager abstraction.

Set process DPI awareness before calculating native fullscreen bounds so the handoff covers the physical display correctly at the Claw reference 1920×1200 / 150% environment.

The surface may simply paint a dark/black background.

Optional centered application logo is acceptable only if it remains trivial. Do not add configurable splash media, animation, video, themes, or a splash settings page.

---

## 7. Handoff HWND behavior

Required native contract:

~~~text
WS_POPUP
+ WS_EX_TOPMOST
+ full-screen primary monitor coverage
~~~

This window is intentionally the active Gaming Home transition surface. Unlike the Quick Settings Overlay, it does not need WS_EX_NOACTIVATE.

It is acceptable for FseHome to own foreground during the short boot handoff.

Do not reuse the Overlay WindowInterop implementation. These are different processes with different contracts.

Do not add a recurring topmost watchdog. The FseHome owns the surface only for one bounded startup transaction.

---

## 8. Steam launch path

Prefer direct Steam executable launch when a valid installed Steam executable can be resolved.

Keep resolution narrow and local to FseHome.

Suggested order:

~~~text
HKCU\Software\Valve\Steam\SteamExe
→ if valid file, use it

otherwise HKCU\Software\Valve\Steam\SteamPath + steam.exe
→ if valid file, use it

otherwise
→ fallback to the existing steam://open/bigpicture ShellExecute path
~~~

Direct launch:

~~~text
Steam.exe steam://open/bigpicture
~~~

Use normal user context. No elevation.

If the direct Process.Start attempt throws or the resolved path is stale, fall back once to:

~~~text
UseShellExecute = true
FileName = steam://open/bigpicture
~~~

No retry loop beyond this direct-launch → URI fallback.

Do not add a Steam installation manager or shared Runtime dependency merely for path resolution.

---

## 9. Steam Big Picture readiness contract

The Runtime already has a proven BPM identity in:

- src/SteamInputAddonforClaw/Steam/SteamBigPictureWindowProbe.cs

Current Runtime discovery evidence:

~~~text
process name = steamwebhelper
window class = SDL_app
title prefix = Steam Big Picture
~~~

FseHome must not reference the full Runtime project just to reuse this class.

Implement only the tiny local readiness check required by the boot launcher and keep a source comment stating that the identity intentionally matches SteamBigPictureWindowProbe.

A candidate is ready only when:

1. HWND is a real top-level window returned by EnumWindows;
2. owning process is steamwebhelper;
3. class name is SDL_app;
4. title starts with Steam Big Picture, case-insensitive;
5. the window still exists at the point it is selected.

Do not weaken this to "any Steam process exists". Steam.exe or steamwebhelper process existence alone does not prove that the BPM visual surface is ready.

Do not introduce the Runtime watcher's full EntryProtected/Reconciling state machine into FseHome. That complexity is appropriate for a long-lived session watcher, not a one-shot boot handoff.

---

## 10. Bounded readiness wait

A simple bounded startup probe is preferred over a WinEvent framework here.

Reference AnyFSE behavior:

~~~text
check interval = 500 ms
launcher timeout = 60 seconds
~~~

Use the same practical shape unless implementation evidence requires a small adjustment:

~~~text
deadline = 60 seconds
probe interval = 500 ms
~~~

This is a one-shot boot transaction, not continuous polling.

Do not add:

- background watcher after handoff;
- permanent timer;
- retry manager;
- epoch/generation state;
- generalized lifecycle state machine.

The deadline must be measured from the Steam launch request.

If the deadline expires, close the FseHome surface and exit cleanly. Do not trap the user behind a permanent black/topmost surface if Steam fails.

---

## 11. Foreground handoff

When a valid BPM HWND is found:

1. stop treating the FseHome window as the visual owner;
2. hide/destroy the FseHome fullscreen window;
3. ensure the BPM HWND is shown if necessary;
4. call SetForegroundWindow once for the BPM HWND;
5. verify/log the resulting foreground HWND;
6. exit FseHome.

Do not initially port AnyFSE's aggressive focus fallback machinery.

Specifically do not add in this PR:

- AttachThreadInput;
- synthetic Alt key input;
- repeated SetForegroundWindow loops;
- focus stealing retries;
- periodic focus watchdog.

Reason:

FseHome is the currently activated Gaming Home and should have a legitimate foreground handoff opportunity. First test the normal Win32 foreground transfer on real hardware.

If a confirmed BPM HWND consistently cannot become foreground, capture that as separate evidence before adding stronger focus machinery.

---

## 12. Window close ordering

Do not leave the topmost handoff window covering Steam after BPM readiness is known.

Preferred order:

~~~text
BPM candidate confirmed
→ hide handoff HWND
→ ShowWindow BPM if needed
→ SetForegroundWindow BPM once
→ destroy handoff HWND
→ exit
~~~

If hiding before SetForegroundWindow proves visually worse on hardware, a tightly ordered variation is acceptable, but there must be exactly one clear owner at completion.

On all exception/timeout paths:

~~~text
finally
→ destroy/hide handoff HWND if it still exists
~~~

No leaked topmost surface.

---

## 13. Minimal diagnostics

This boot path needs enough evidence for hardware validation.

Add one best-effort last-run diagnostic file, not a logging framework.

Suggested path:

~~~text
%LOCALAPPDATA%\SteamInputAddonforClaw\logs\fse-home-last.log
~~~

Overwrite it at each FseHome activation so it cannot grow without bound.

Logging failure must never block launch.

Record only high-value events:

~~~text
FseHome start
handoff HWND created/shown + HWND + display bounds
Steam launch mode = DirectExe / UriFallback
resolved Steam executable path when direct mode is used
BPM candidate found + HWND + PID + elapsed ms
foreground before handoff
SetForegroundWindow return value
foreground after handoff
timeout
exception type/message
FseHome exit
~~~

Do not log a line every 500 ms. The readiness poll itself should remain silent until success/timeout.

Do not add Runtime IPC just for logging.

---

## 14. Fixed FSE package version must advance

This PR changes behavior inside the payload shipped in:

- src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/SteamInputAddonforClaw.FseHome.msix

Therefore the fixed FSE component version must advance.

Use:

~~~text
1.0.1.0
~~~

Update all exact version contracts together, including:

- src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
  - SteamFsePackageContract.FixedPackageVersionText
- src/SteamInputAddonforClaw.FseHome/Packaging/AppxManifest.xml
- scripts/package-fse-home.ps1 default/template handling
- scripts/verify-publish-assets.ps1 expected manifest version
- affected source/contract tests

Do not tie the FSE version to the normal Addon release version.

This remains a separately versioned frozen component.

---

## 15. Establish the permanent FSE signing identity now

The existing fixed 1.0.0.0 artifact is pre-release, and the original private signing key/PFX is not available.

Do **not** attempt to preserve update compatibility with that disposable pre-release signer.

Instead, this PR is the point where the project establishes the permanent FSE signing identity that all future fixed FSE MSIX updates will reuse.

Create one dedicated self-signed code-signing certificate with:

~~~text
Subject = CN=SteamInputAddonforClaw
EKU     = Code Signing (1.3.6.1.5.5.7.3.3)
KeyUse  = Digital Signature
Private key = exportable
~~~

The subject must exactly match the existing manifest Publisher:

~~~xml
Publisher="CN=SteamInputAddonforClaw"
~~~

Keep the package identity name and Application Id unchanged.

Export and preserve:

~~~text
permanent encrypted PFX  ← private signing identity, REQUIRED for future FSE updates
matching public CER      ← shipped registration trust artifact
~~~

The PFX is a project secret.

Do not:

- commit the PFX;
- commit its password;
- write the password into documentation;
- generate a fresh signer for each FSE rebuild;
- generate the signer in normal CI;
- replace it with an unrelated product certificate.

Recommended ownership:

~~~text
working copy:
  CurrentUser\My or another user-controlled local certificate store

backup:
  encrypted PFX in a private user-controlled secret/backup location outside the repository
~~~

The exact private storage path is an operator concern and must not be hard-coded into product code.

Once this signer is created, treat losing the PFX/private key as a signing-identity loss requiring explicit recovery/migration work. Future routine FSE version updates must reuse this same signing identity.

---

## 16. Rebuild the fixed distribution artifact with the new permanent signer

Because FseHome is embedded in the fixed MSIX, source changes are not sufficient.

After implementation and after the new permanent FSE signing identity exists:

1. publish SteamInputAddonforClaw.FseHome framework-dependent win-x64;
2. run the existing manual package-fse-home.ps1 path;
3. use the new permanent FSE-only PFX;
4. build/sign version 1.0.1.0;
5. replace:
   - Packaging/Distribution/SteamInputAddonforClaw.FseHome.msix
   - Packaging/Distribution/SteamInputAddonforClaw.FseHome.cer;
6. update both pinned SHA-256 values in verify-publish-assets.ps1.

The old pre-release 1.0.0.0 CER/MSIX may be replaced completely.

No compatibility guarantee is required from the old 1.0.0.0 signer to the new permanent signer because the product has not been released.

For hardware/dev machines that already have the old package registered, use a clean migration for this one pre-release transition:

~~~text
disable/clear FSE preference if needed
→ remove the exact old SteamInputAddonforClaw.FseHome package
→ register/trust the new 1.0.1.0 artifact through the existing normal first-enable path
→ verify GamingHomeApp from the actually registered FamilyName
→ reboot/test
~~~

Do not add product code whose only purpose is to migrate the lost old development signer.

Do not:

- reintroduce Runtime startup package provisioning;
- add a package watcher;
- add a background repair daemon;
- restore release-time FSE signing;
- make normal CI rebuild the FSE artifact.

Future fixed FSE updates after 1.0.1.0 may use the existing lazy registration/version policy because they will be signed by the same preserved permanent PFX.

---

## 17. Manifest scope

Keep the existing Gaming Home manifest contract.

Do not add uap:SplashScreen in this PR.

Do not change the following merely because AnyFSE differs:

- AllowExternalContent;
- ExternalLocation packaging;
- package identity;
- publisher;
- Application Id;
- gamingApp extension shape;
- CustomCapability.SCCD model;
- MaxVersionTested.

The current package is fully contained by design.

MaxVersionTested can be evaluated separately if actual testing demonstrates a compatibility issue. Do not mix it into the handoff A/B.

---

## 18. Tests

Update focused tests only.

### Packaging contract tests

Update SteamFsePackagingContractTests to assert:

- fixed version = 1.0.1.0;
- FseHome no longer exits immediately after one URI Process.Start;
- FseHome contains the native fullscreen handoff path;
- WS_EX_TOPMOST and WS_POPUP are present;
- bounded timeout exists;
- direct Steam executable launch is attempted;
- steam://open/bigpicture remains as fallback;
- BPM identity intentionally matches:
  - steamwebhelper
  - SDL_app
  - Steam Big Picture;
- foreground handoff uses SetForegroundWindow;
- no AttachThreadInput;
- no synthetic keyboard focus hack;
- no controller/VIIPER/HidHide dependency.

### Artifact verification tests

Update expected:

- MSIX version 1.0.1.0;
- new MSIX SHA-256;
- new CER SHA-256 from the newly established permanent FSE signer.

### Narrow logic tests

Where practical, test pure/local helpers without creating a generic framework:

- valid SteamExe registry path selected;
- invalid direct path falls back to URI launch;
- candidate identity requires process + class + title;
- timeout completes and closes the handoff path;
- successful candidate ends the wait.

Do not mock all of Win32 or build a generic window abstraction only to increase unit-test coverage.

---

## 19. Manual hardware validation

This PR is incomplete until real MSI Claw boot behavior is checked.

### A. Cold boot — Addon FSE

With FSE enabled and Steam not already initialized:

1. reboot;
2. observe Windows login → Gaming Home transition;
3. FseHome handoff surface should cover the screen promptly;
4. Steam starts;
5. Steam BPM becomes visible without waiting behind the Windows login surface;
6. when BPM appears, FseHome surface disappears;
7. fse-home process exits;
8. no black/topmost window remains.

Record fse-home-last.log.

### B. Steam already running

Enter/restart the FSE path with Steam already available.

Expected:

~~~text
handoff surface
→ Big Picture request
→ BPM detected quickly
→ foreground transfer
→ FseHome exits
~~~

### C. Steam slow cold start

Cold boot with Steam taking materially longer than normal.

Expected:

- surface remains boundedly alive;
- no early FseHome exit exposing the login screen merely because Steam takes several seconds;
- BPM handoff succeeds before the 60-second deadline when Steam is healthy.

### D. Steam launch failure

Temporarily make direct resolution fail or otherwise test the fallback.

Verify:

- URI fallback is attempted once;
- if BPM never appears, the 60-second deadline eventually closes the FseHome surface;
- no permanent topmost window remains.

### E. Overlay regression check

Because the motivating A/B also exposed an Overlay difference:

1. run the build containing PR #653;
2. boot through Addon FSE;
3. once BPM is stable, open/close Addon Quick Settings Overlay repeatedly;
4. confirm Overlay appears above BPM and does not steal controller focus.

This is validation only. Do not add Overlay-specific code to FseHome.

### F. Controller architecture check

Confirm after BPM handoff:

- Runtime still detects BPM independently;
- desired presentation converges to SteamDeck;
- PID1902 ownership is unchanged;
- HidHide/VIIPER ownership is unchanged.

No FseHome-to-Runtime handshake should exist.

---

## 20. Success criteria

Complete only when all are true:

1. Addon remains a real Windows Gaming Home / FSE application using the existing registration model.
2. FseHome no longer exits immediately after requesting Steam.
3. A native fullscreen topmost handoff surface exists only during Steam startup.
4. Steam direct executable launch is preferred when safely resolvable.
5. current steam://open/bigpicture URI activation remains a fallback.
6. FseHome waits for the actual Steam BPM HWND, not merely a Steam process.
7. BPM readiness uses the existing product identity: steamwebhelper + SDL_app + Steam Big Picture title.
8. readiness wait is bounded and cannot strand a topmost surface indefinitely.
9. successful readiness performs one normal foreground handoff and exits.
10. no AttachThreadInput / synthetic input / focus retry machinery is added without hardware evidence.
11. no Runtime/controller/VIIPER/HidHide dependency is introduced.
12. manifest package splash remains out of scope.
13. FSE fixed package advances to 1.0.1.0.
14. a new permanent FSE-only signing PFX is established and preserved outside the repository.
15. fixed MSIX/CER are rebuilt from that signer and both hash contracts are updated.
16. future FSE package rebuilds reuse the same preserved PFX rather than generating a new signer.
17. ordinary Addon CI/release still does not rebuild or sign FSE.
18. real cold-boot validation shows the Windows login surface is no longer exposed for the whole Steam BPM startup interval.
19. no FseHome process/window remains after handoff.
20. full build/test suite passes.

---

## 21. Expected file scope

Primary:

- src/SteamInputAddonforClaw.FseHome/Program.cs
- one or two small FseHome-local source files if needed for native window / BPM probing
- src/SteamInputAddonforClaw.FseHome/Packaging/AppxManifest.xml
- src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs
- tests/SteamInputAddonforClaw.Tests/SteamFsePackagingContractTests.cs
- scripts/package-fse-home.ps1
- scripts/verify-publish-assets.ps1
- source tests for the narrow handoff logic if useful
- src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/SteamInputAddonforClaw.FseHome.msix
- src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/SteamInputAddonforClaw.FseHome.cer only if the rebuilt public artifact actually changes

Do not modify without demonstrated compilation necessity:

- Full1902 controller ownership code;
- Overlay code;
- SteamBigPictureWatcher policy;
- HidHide;
- VIIPER;
- Center M authority;
- GamingConfiguration ON/OFF semantics;
- lazy elevated registration architecture.

---

## 22. Overengineering guardrail

This is a boot handoff, not a new subsystem.

Do not add:

- FseHome ↔ Runtime IPC;
- controller-ready barrier;
- SteamDeck-ready barrier;
- VIIPER/HidHide readiness probe;
- generic launcher framework;
- launcher interface hierarchy;
- persistent launcher state;
- recovery epochs;
- watchdog service;
- background process supervisor;
- event-bus abstraction;
- timer manager;
- multi-monitor policy engine;
- RDP / Fast User Switching / multi-session support;
- configurable splash/video framework;
- Windows package splash as a substitute for the actual handoff window.

The desired implementation is approximately:

~~~text
one tiny Gaming Home process
one transient native HWND
one Steam launch request
one bounded BPM readiness loop
one foreground handoff
one exit path
~~~

That is the product contract.

---

## Final principle

The previous FSE implementation correctly registered a Gaming Home but treated launching Steam as fire-and-forget.

The new contract is:

~~~text
Gaming Home owns the screen until Steam Big Picture is actually ready to own it.
~~~

Nothing more.
