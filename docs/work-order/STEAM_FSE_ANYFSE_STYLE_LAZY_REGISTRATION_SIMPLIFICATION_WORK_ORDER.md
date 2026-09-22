# Work Order — Simplify Steam FSE to AnyFSE-Style One-Time Registration

> **Reviewed baseline:** `main` at `d32ccf7ea4ab6f536d747760f1f74ad3157a330a`  
> **Scope:** Steam Big Picture Windows Full Screen Experience only  
> **Authority:** This supersedes the runtime-provisioning / production-signing direction in `STEAM_FSE_PACKAGE_PROVISIONING_WORK_ORDER.md`.  
> **Product scope:** standalone Steam Addon for Claw. No CTW integration.

## Goal

FSE is a small optional Windows integration that should be registered once, then remain effectively static.

Replace the current PR569 model:

```text
every Runtime startup
→ inspect/version/reconcile FSE MSIX

every PR CI
→ generate/import cert
→ makeappx
→ signtool
→ unpack/verify

every Release
→ require FSE PFX secrets
→ rebuild/sign package
→ tie package version to Addon version
```

with:

```text
fixed FSE package + public CER
→ rebuilt only when FSE itself changes

normal Runtime startup
→ no FSE provisioning

first FSE ON
→ if package missing/older:
     one bounded elevated registration
     temporary Developer Mode
     temporary exact certificate trust
     register fixed package
     cleanup
→ parent re-reads PackageFamilyName!App
→ set GamingHomeApp + StartupToGamingHome

later OFF / ON
→ registry only
```

This follows the proven AnyFSE installation ownership model without copying CTW's sparse/external package complexity.

---

## 1. Current code reviewed

Canonical Full1902 documents:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`

FSE must remain outside PID1902, DirectInput, HidHide, VIIPER, sleep/resume, and controller recovery authority. Existing Steam/BPM observation remains the only owner of X360 ↔ SteamDeck presentation policy.

Current FSE launcher:

- `src/SteamInputAddonforClaw.FseHome/Program.cs`

Keep its narrow behavior:

```text
steam://open/bigpicture
→ exit
```

Do not add Steam monitoring, retries, Runtime IPC, controller readiness waits, or generic launchers.

Current package identity:

```text
Name = SteamInputAddonforClaw.FseHome
Publisher = CN=SteamInputAddonforClaw
Application Id = App
Architecture = x64
```

Keep these stable. Continue deriving AUMID from the actually registered `package.Id.FamilyName + "!App"`; never guess PFN.

Current problematic ownership:

- `src/SteamInputAddonforClaw/WindowsGaming/SteamFsePackageProvisioning.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

Current release/CI coupling:

- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `scripts/pack.ps1`
- `scripts/package-fse-home.ps1`
- `scripts/verify-publish-assets.ps1`

---

## 2. Reference implementation decisions

AnyFSE currently uses:

```text
self-signed package certificate
→ temporarily trust public CER
→ temporarily enable Developer Mode when needed
→ register identity package
→ remove temporary certificate
→ restore Developer Mode
```

Reference source reviewed:

- `ashpynov/AnyFSE/src/AppInstaller/AppInstaller_Install.cpp`
- `ashpynov/AnyFSE/src/AppInstaller/Certificate.cpp`
- `ashpynov/AnyFSE/src/Tools/Packages.cpp`
- `ashpynov/AnyFSE/AppxManifest.xml`

CTW confirms the Gaming Home manifest/SCCD requirements but uses `ExternalLocation` because its identity package points to separately installed Center binaries.

Do **not** copy CTW `ExternalLocation` unless real hardware testing proves a fully contained FseHome package cannot work.

---

## 3. Fixed FSE distribution artifact

FSE package version is independent from the Addon version.

Use:

```text
FSE package version = 1.0.0.0
```

Do not bump it for normal Addon releases. Bump only when FseHome behavior, manifest, assets/resources, capability packaging, or registration contract changes.

Add feature-local public artifacts, e.g.:

```text
src/SteamInputAddonforClaw.FseHome/Packaging/Distribution/
  SteamInputAddonforClaw.FseHome.msix
  SteamInputAddonforClaw.FseHome.cer
```

The MSIX is signed once with a dedicated self-signed FSE package certificate.

This is **not** general Addon code signing.

Certificate subject must match the existing manifest publisher:

```text
CN=SteamInputAddonforClaw
```

Never commit or ship:

- private PFX;
- private key;
- PFX password.

Keep the private PFX outside the repository only for a future intentional FSE artifact rebuild.

No GitHub signing secret is required.

---

## 4. Package contents

Prefer a normal package containing the actual FseHome payload:

```text
SteamInputAddonforClaw.FseHome.msix
├─ AppxManifest.xml
├─ CustomCapability.SCCD
├─ SteamInputAddonforClaw.FseHome.exe
├─ SteamInputAddonforClaw.FseHome.dll
├─ required managed payload
├─ resources.pri
├─ Assets\...
└─ Public\...
```

Remove/avoid `AllowExternalContent` unless actual registration proves it is required.

The fixed artifact should include a normal resource index / scaled assets so the Windows FSE picker does not show a blank icon.

---

## 5. FSE artifact builder becomes manual-only

Keep `scripts/package-fse-home.ps1` only as a developer tool used when FSE itself changes.

It may:

1. publish the FseHome project;
2. stage manifest/SCCD/assets;
3. stamp an explicitly supplied FSE component version;
4. generate `resources.pri`;
5. create MSIX;
6. sign with a locally supplied FSE-only PFX;
7. export/copy the matching public CER;
8. place final public artifacts in the distribution folder.

It must **not** run from normal PR CI, `pack.ps1`, or Release.

Do not generate a new key automatically on each run.

---

## 6. Remove Runtime startup provisioning

Delete the PR569 startup provisioning architecture.

Remove:

- `SteamFsePackageProvisioningOutcome`
- `SteamFsePackageProvisioningResult`
- `ISteamFsePackageProvisioner`
- `ISteamFsePackageDeployment`
- `SteamFsePackageProvisioner`
- `WindowsSteamFsePackageDeployment`
- FSE MSIX ZIP/version parsing used only by startup reconciliation
- `AddonProcessHost._steamFsePackageProvisioner`
- test-only constructor injection
- startup `EnsureProvisioned()` call and related log

Delete `SteamFsePackageProvisioning.cs` if nothing useful remains.

New invariant:

```text
normal Runtime startup
→ never installs or updates FSE package
```

Delete/replace `SteamFsePackageProvisionerTests.cs` and the FSE startup-provisioner cases in `AddonProcessHostStartupTests.cs`.

---

## 7. Keep package probe read-only

Keep the useful package logic in `SteamFseConfiguration.cs`:

- exact identity enumeration by `package.Id.Name`;
- actual FamilyName-derived AUMID;
- exact owned-package uninstall removal;
- OS support probe;
- GamingConfiguration registry read/write.

Do not add persisted booleans such as `SteamFseInstalled` or `SteamFseConfigured`.

Windows package state + registry remain authoritative.

---

## 8. Settings capture semantics

Current package-missing behavior disables the card. Change it.

Target:

```text
supported OS
+ package missing
+ FSE preference off

→ Available = true
→ Enabled = false
→ toggle interactive
```

Package absence means "not configured yet", not "feature unavailable".

Unsupported Windows remains unavailable.

If package enumeration itself throws/fails, fail closed rather than pretending the package is simply missing.

---

## 9. Lazy registration on first ON

Introduce one narrow parent-side registration client, conceptually:

```text
SteamFseRegistrationClient
```

Responsibilities:

```text
EnsureRegisteredAsync()
→ exact package already usable?
   YES → return actual AUMID, no elevation
   NO  → launch one fixed elevated registration command
→ wait boundedly
→ parent re-enumerates package
→ return actual PFN-derived AUMID or failure
```

No startup use. No background retry. No generic AppX manager.

Migration rule for current pre-release/dev packages:

```text
missing exact package
OR installed exact version < fixed bundled FSE version
→ registration/update required on ON only

installed version >= bundled fixed version
→ do not downgrade/reinstall
```

---

## 10. Reuse same executable for elevation

Do not add another helper EXE/service.

Add one fixed argument, e.g.:

```text
SteamInputAddonforClaw.exe --register-fse-home
```

Handle it in `Program.Main` before single-instance/runtime startup, following the existing bounded self-elevation pattern.

Parent launch:

```csharp
UseShellExecute = true
Verb = "runas"
```

No named pipe is needed unless exit code + parent package readback proves insufficient.

UAC cancellation must leave the toggle OFF and return a normal failed/cancelled mutation.

---

## 11. Elevated registration operation

Use fixed installed paths only:

```text
fse\SteamInputAddonforClaw.FseHome.msix
fse\SteamInputAddonforClaw.FseHome.cer
```

Do not accept arbitrary frontend-supplied paths.

### Developer Mode

Use:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock
AllowDevelopmentWithoutDevLicense
```

Behavior:

```text
original ON  → leave ON
original OFF → enable + verify
               register
               restore OFF in finally
```

No permanent Developer Mode.

Do not add a persistent recovery journal initially. `try/finally` is sufficient unless real hardware/lifecycle testing demonstrates a reachable product failure requiring more state.

### Temporary certificate trust

Use `LocalMachine\TrustedPeople`.

Install only the exact bundled public CER and identify it by exact thumbprint.

Never write it to Root.

Sequence:

```text
exact CER thumbprint
→ if not already trusted, add exact CER
→ package registration
→ finally remove exact CER only if this operation added it
```

Do not remove a pre-existing matching certificate that the Addon did not add.

### Package registration

Use `Windows.Management.Deployment.PackageManager` directly.

For the fully contained package, do not use `ExternalLocation` initially.

After deployment, exact package readback must succeed before reporting success.

---

## 12. ON mutation ordering

The frontend contract may remain:

```csharp
Task<FrontendSteamFseMutationResult> SetSteamFseEnabledAsync(...)
```

Make backend ON handling asynchronous as needed.

Required order:

```text
ON requested
→ supported OS?
→ package/AUMID usable?
→ if not: EnsureRegisteredAsync()
→ parent readback actual AUMID
→ ONLY NOW:
     StartupToGamingHome = true
     GamingHomeApp = actual AUMID
→ registry readback
```

Never write the ON preference before successful registration/readback.

Registration failure/UAC cancel:

```text
no ON registry mutation
→ render actual state
→ feature-local failure only
→ Full1902 Runtime unaffected
```

---

## 13. OFF behavior

Keep the agreed simple behavior:

```text
OFF
→ delete GamingHomeApp
→ StartupToGamingHome = false
→ readback
```

Do not uninstall the FSE package on OFF.

Therefore after first successful registration:

```text
ON → OFF → ON
```

must require no UAC, Developer Mode change, certificate operation, or package deployment.

No automatic reboot and no success popup.

---

## 14. Uninstall

Keep exact package cleanup:

```text
clear FSE boot preference
StartupToGamingHome = false
remove only exact SteamInputAddonforClaw.FseHome package
```

There should normally be no FSE certificate to remove because trust is temporary.

Do not wildcard certificate cleanup by publisher name.

Do not couple FSE cleanup to controller ownership beyond existing uninstall ordering.

---

## 15. Publish / Release simplification

### `scripts/publish-layout.ps1`

Stop building a per-Addon-version FSE package staging tree for normal Release.

Copy only the fixed public artifacts into:

```text
artifacts\publish\fse\SteamInputAddonforClaw.FseHome.msix
artifacts\publish\fse\SteamInputAddonforClaw.FseHome.cer
```

It is fine for the FseHome project to remain in the solution so ordinary compilation catches source breakage.

### `scripts/pack.ps1`

Remove:

- `FseCertificatePath`
- `FseCertificatePassword`
- Addon-version → FSE-version mapping
- Release-time `package-fse-home.ps1`
- Release-time signing requirement

### `.github/workflows/release.yml`

Remove:

- `FSE_SIGNING_CERTIFICATE_BASE64`
- `FSE_SIGNING_CERTIFICATE_PASSWORD`
- PFX materialization
- secret-presence failure
- FSE password handling/cleanup

Normal Release must not rebuild/resign the fixed FSE package.

---

## 16. Cheap artifact verification

Update `verify-publish-assets.ps1` to verify the fixed artifact cheaply.

Require:

- MSIX exists;
- CER exists;
- pinned SHA-256 for both;
- MSIX can be opened as ZIP using managed APIs;
- internal `AppxManifest.xml` has exact identity/publisher/`Id="App"`/fixed version/`windows.gamingApp`/Gaming Home capability;
- `CustomCapability.SCCD` exists inside the MSIX.

Do not:

- modify certificate stores;
- call `signtool verify`;
- rebuild package;
- call `makeappx unpack` in ordinary Release.

When FSE is intentionally rebuilt and hardware-validated, update pinned hashes in that same FSE-focused change.

---

## 17. Normal PR CI

Delete the entire current:

```text
Build and verify CI FSE package
```

step from `.github/workflows/ci.yml`.

Normal PR CI must not perform:

- `New-SelfSignedCertificate`
- PFX/CER export
- certificate import
- FSE makeappx
- FSE signtool
- FSE certificate-store mutation

Keep normal build/tests.

Lightweight unit/source contract tests inside the ordinary test assembly are fine; no separate expensive FSE E2E for every PR.

Review/remove obsolete expectations in:

- `SteamFsePackagingContractTests.cs`
- `SteamFsePackageProvisionerTests.cs`
- `AddonProcessHostStartupTests.cs`
- `scripts/tests/verify-publish-assets.tests.ps1`

---

## 18. Required tests

Keep focused tests only:

1. supported OS + no package → Available=true / Enabled=false;
2. unsupported OS → unavailable;
3. registered package derives AUMID from actual FamilyName;
4. ON with usable package does not invoke registration;
5. ON with missing/older package invokes registration once;
6. registration failure/UAC cancel writes no ON registry state;
7. registration success writes actual AUMID + startup=true;
8. OFF clears registry preference without package removal;
9. OFF→ON after registration does not re-register;
10. uninstall removes only exact Addon package;
11. publish copies fixed MSIX + CER;
12. Release contains no FSE PFX secrets;
13. normal CI contains no FSE cert/makeappx/signtool E2E.

Do not mock an entire AppX framework or add state for theoretical races.

---

## 19. Manual hardware validation

Run when this refactor is implemented and whenever the fixed FSE artifact itself changes. Do not run for every unrelated PR/release.

### Clean first use

Start with no registered FSE package.

Verify:

1. normal Runtime startup performs no FSE provisioning;
2. Settings FSE toggle is available and OFF;
3. first ON produces one UAC prompt;
4. registration succeeds;
5. Developer Mode is restored;
6. exact temporary CER is not left trusted if this operation added it;
7. package is registered;
8. Windows FSE picker shows the Addon entry with icon;
9. toggle readback is ON.

### Reboot

With FSE ON:

1. reboot manually;
2. Windows activates FseHome;
3. FseHome launches Steam BPM and exits;
4. existing Steam/BPM observation makes Full1902 converge to SteamDeck without any FSE/controller handshake.

### OFF / re-enable

OFF must restore normal Windows boot preference.

Next ON must require no UAC or package work.

### Ordinary Addon update

Update Addon while FSE remains registered.

Verify no startup package reconcile occurs and FSE still works.

### Uninstall

Verify FSE preference clears and exact package is removed while existing Full1902 stock-safe uninstall behavior remains unchanged.

---

## 20. Failure policy / overengineering guardrail

Handle real failures:

- UAC cancelled;
- cert trust fails;
- Developer Mode write/readback fails;
- package deployment fails;
- package readback fails;
- fixed artifact missing;
- user removes the package externally and later requests ON.

Failure:

```text
restore temporary cert/dev-mode state
→ do not write ON preference
→ return feature-local failure
→ Runtime continues
```

Do not add:

- startup FSE provisioning;
- package watcher/retry daemon;
- generic package manager abstraction;
- production FSE signing secrets;
- Addon-version-coupled FSE package version;
- permanent certificate trust;
- permanent Developer Mode;
- FSE/controller readiness barrier;
- CTW integration;
- multi-user/RDP/Fast User Switching support;
- locks/epochs/state solely for pathological instruction-level interleavings.

---

## 21. File-level checklist

Expected implementation touch points:

### Remove
- `src/SteamInputAddonforClaw/WindowsGaming/SteamFsePackageProvisioning.cs`
- FSE provisioner ownership/call in `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

### Modify
- `src/SteamInputAddonforClaw/WindowsGaming/SteamFseConfiguration.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw/Program.cs`
- `scripts/publish-layout.ps1`
- `scripts/pack.ps1`
- `scripts/verify-publish-assets.ps1`
- `scripts/package-fse-home.ps1`
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- affected tests

### Add narrowly
Feature-local one-time registration code, e.g.:

```text
SteamFseRegistrationClient.cs
SteamFseElevatedRegistration.cs
```

Names may vary; do not create a generic framework.

---

## 22. Acceptance criteria

Complete only when:

1. normal Runtime startup performs zero FSE package installation/update work;
2. package missing on supported OS is a normal interactive OFF state;
3. first ON lazily registers the fixed package;
4. registration uses temporary Developer Mode + exact temporary TrustedPeople certificate;
5. certificate and Developer Mode return to original steady state;
6. ON preference is written only after exact package/AUMID readback;
7. later ON/OFF is registry-only;
8. FSE package version is independent from Addon version;
9. no FSE PFX/password GitHub secrets exist in Release;
10. normal Release does not rebuild/resign FSE;
11. normal PR CI has no FSE certificate/makeappx/signtool E2E;
12. fixed MSIX + CER are shipped and cheaply hash/identity verified;
13. actual reboot validation proves Windows FSE → FseHome → Steam BPM;
14. Full1902 controller lifecycle is unchanged;
15. full build/test suite passes.

## Final principle

```text
Build and validate FSE when FSE changes.
Register it when the user first enables it.
Then leave it alone.
```

Do not make every Runtime startup, every unrelated PR, or every Addon release pay the lifecycle cost of a component intended to remain frozen after validation.
