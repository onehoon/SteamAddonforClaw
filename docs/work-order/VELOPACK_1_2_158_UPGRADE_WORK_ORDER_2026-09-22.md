# Work Order — Upgrade VeloPack 1.2.0 → 1.2.158

> **Repository:** onehoon/SteamAddonforClaw  
> **Reviewed baseline:** main@878b6385565c0eee2bd24cb9b5a40c6036f96b9d  
> **Date:** 2026-09-22  
> **Scope:** focused VeloPack dependency / packaging toolchain upgrade only  
> **Architecture baseline:** standalone Full1902 application; CTW integration is out of scope  
> **Expected PR count:** 1 small focused PR

---

## 1. Goal

Upgrade Steam Addon for Claw from VeloPack 1.2.0 to VeloPack 1.2.158 without changing the Addon's existing update ownership, startup ordering, Full1902 controller lifecycle, or packaging architecture.

Target:

```text
VeloPack managed package        1.2.0 -> 1.2.158
vpk CLI used by local pack      1.2.0 -> 1.2.158
vpk CLI used by release CI      1.2.0 -> 1.2.158
third-party notice              1.2.0 -> 1.2.158
```

This is not an updater redesign.

---

## 2. Source authority

Read before implementation:

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
5. `docs/work-order/STARTUP_UPDATE_PR_A_DECOUPLE_CHECK_DOWNLOAD_FROM_CONTROLLER_STARTUP_WORK_ORDER.md`
6. current `Program.cs`, `Updates/VelopackUpdateClient.cs`, `scripts/pack.ps1`, and release workflow

Full1902 lifecycle authority remains unchanged.

A controlled Runtime update/restart while Center M is Disabled must not become an authority-release event and must not restore PID1901 merely because VeloPack is applying an update.

---

## 3. Current verified state

### Managed package

`src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`:

```xml
<PackageReference Include="Velopack" Version="1.2.0" />
```

Preserve:

```text
TargetFramework = net10.0-windows10.0.26100.0
RuntimeIdentifier = win-x64
SelfContained = false
```

### Local pack tool

`scripts/pack.ps1` currently invokes VeloPack 1.2.0.

Current pack contract includes:

```text
--packId SteamInputAddonforClaw
--packTitle "Steam Addon for Claw"
--mainExe SteamInputAddonforClaw.exe
--framework net10.0-x64-runtime
```

### Release workflow

`.github/workflows/release.yml` currently pins VeloPack 1.2.0 for both:

```text
dnx vpk --version 1.2.0 download github ...
dnx vpk --version 1.2.0 upload github ...
```

### Runtime update API

Current Runtime intentionally uses:

```csharp
VelopackApp.Build()
    .SetAutoApplyOnStartup(false)
    .OnBeforeUninstallFastCallback(_ => UninstallBootstrap.RunFastCallbackOnly())
    .Run();
```

and applies an already-downloaded pending package only after primary-instance ownership is established through:

```text
VelopackUpdateClient.TrySchedulePendingUpdateApply(args)
```

Current code uses `UpdateManager`, `GithubSource`, `IsInstalled`, `UpdatePendingRestart`, `CheckForUpdatesAsync`, `DownloadUpdatesAsync` and `WaitExitThenApplyUpdates`.

These APIs remain available in VeloPack 1.2.158. No product-code API migration is required.

---

## 4. Why 1.2.158 is worth taking

Official release:

```text
https://github.com/velopack/velopack/releases/tag/1.2.158
Published: 2026-09-21
```

Relevant changes:

### PR #944 — native x64 Windows bootstrapper output

`vpk pack` now ships architecture-appropriate 64-bit Windows bootstrapper binaries.

Steam Addon for Claw is explicitly win-x64. New release artifacts should therefore use the x64 Setup/Update/stub path.

### PR #985 — packTitle / mainExe launcher-name fix

This repository currently uses:

```text
packTitle = Steam Addon for Claw
mainExe   = SteamInputAddonforClaw.exe
```

The basenames differ, so the upstream launcher-name fix is directly relevant.

Do not add an application-side workaround.

### PR #1010 — zstd-only delta packaging

VeloPack removed the bsdiff fallback and now uses zstd as the only delta patch format.

Required failure policy:

```text
valid zstd delta
-> package succeeds

required zstd delta generation fails
-> packaging fails
-> do not publish an unusable fallback delta
```

Do not add application-side delta recovery logic.

### PR #974 — update apply timing diagnostics

Useful for diagnosing slow EDR/AV update application. No duplicate Addon instrumentation is required.

### PR #1051 — Installed Apps EstimatedSize

Small installer UX improvement. No application code change is required.

---

## 5. Required changes

### 5.1 Managed package

Update:

`src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`

```xml
<PackageReference Include="Velopack" Version="1.2.0" />
```

to:

```xml
<PackageReference Include="Velopack" Version="1.2.158" />
```

Do not alter TargetFramework, RuntimeIdentifier, SelfContained, or unrelated packages.

### 5.2 Local pack script

Update `scripts/pack.ps1`:

```text
vpk version 1.2.0 -> 1.2.158
```

Preserve all current pack arguments and layout.

### 5.3 Release workflow

Update `.github/workflows/release.yml`:

```text
dnx vpk --version 1.2.0 download github
-> dnx vpk --version 1.2.158 download github

dnx vpk --version 1.2.0 upload github
-> dnx vpk --version 1.2.158 upload github
```

Do not change channel semantics, artifact naming, GitHub release ownership, or token handling.

### 5.4 Third-party notice

Update the VeloPack version in `THIRD_PARTY_NOTICES.md` from 1.2.0 to 1.2.158.

Do not rewrite unrelated notice text.

---

## 6. Explicit non-goals

Do not modify unless a concrete 1.2.158 compatibility failure proves it necessary:

- `Program.cs`
- `VelopackUpdateClient.cs`
- `SetAutoApplyOnStartup(false)`
- primary-instance ownership
- pending-update apply ordering
- background update timing
- restart arguments
- uninstall fast callback
- Full1902 Runtime startup
- PID1901/PID1902 handling
- HidHide / DirectInput / VIIPER ownership
- Xbox360 / SteamDeck presentation switching
- suspend/resume or crash/restart recovery

Do not add:

- a second updater owner;
- a new updater abstraction;
- retry/state machinery;
- compatibility wrappers;
- VeloPack Flow;
- MSI packaging;
- speculative race protection.

The current one-owner / one pending-apply path remains the desired design.

---

## 7. Historical work orders remain historical

Do not edit old work orders merely because they mention VeloPack 1.2.0.

`STARTUP_UPDATE_PR_A_DECOUPLE_CHECK_DOWNLOAD_FROM_CONTROLLER_STARTUP_WORK_ORDER.md` correctly records the version and contract used when that lifecycle change was designed.

---

## 8. Validation

### Restore / build / tests

Run the repository's normal restore, build, and full test suite.

At minimum:

```text
dotnet restore
dotnet build
existing automated tests PASS
```

Use the repository's actual solution/project entry points.

### Pack validation

Run existing `scripts/pack.ps1` with VeloPack 1.2.158.

Verify:

- current `net10.0-x64-runtime` framework declaration still packs;
- expected setup/full/delta artifacts are produced;
- delta generation succeeds with a valid previous release base;
- no legacy bsdiff fallback is relied on;
- packaging fails rather than publishing an unusable delta when the supported delta path cannot be generated.

Where practical, inspect the generated Windows bootstrapper PE architecture and confirm AMD64/x64.

### Mandatory first-release transition test

Before publishing the first 1.2.158-built release, test from an actual installed release produced by the old 1.2.0 toolchain:

```text
old installed release
-> background update check/download finds 1.2.158-built release
-> update becomes pending
-> current Full1902 Runtime remains alive
-> next safe primary startup
-> pending local package applies before controller Runtime startup
-> app restarts
-> updated Runtime starts
-> Full1902 ownership reconciles normally
```

While Center M is Disabled:

```text
controlled update/relaunch
-> retire process-owned resources safely
-> retain persistent Disabled-mode authority
-> do not intentionally restore PID1901
-> updated Runtime starts
-> reconcile desired PID1902 state
```

### Launcher-name regression check

Because `packTitle` differs from `mainExe`, inspect the installed/portable launcher layout after the old-to-new transition.

The upgrade must not leave two competing root launchers caused by the historical packTitle/mainExe mismatch.

### Uninstall

Confirm the existing uninstall fast callback and stock-safe teardown remain intact.

---

## 9. Acceptance criteria

- managed VeloPack package = 1.2.158;
- local vpk = 1.2.158;
- release CI vpk download/upload = 1.2.158;
- third-party notice = 1.2.158;
- runtime updater lifecycle is unchanged;
- Full1902 authority/startup behavior is unchanged;
- tests pass;
- 1.2.158 package and delta are generated successfully;
- actual 1.2.0-built installed release -> 1.2.158-built release update succeeds before publication;
- no duplicate root launcher remains after transition;
- no new updater owner/state/abstraction is introduced.

---

## 10. PR review focus

Block only concrete regressions such as:

- an active VeloPack pin remains at 1.2.0;
- managed/runtime and packaging CLI versions diverge;
- release packaging cannot create/upload a valid package;
- old installed release cannot consume the new transition release;
- pending apply moves before safe primary-instance ownership;
- Full1902 update/restart restores PID1901;
- uninstall callback regresses;
- expected runtime payload disappears.

Do not block for theoretical instruction-level races or unrelated updater architecture improvements.
