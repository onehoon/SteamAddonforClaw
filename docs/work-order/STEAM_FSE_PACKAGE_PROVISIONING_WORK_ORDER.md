# Work Order — Provision Steam FSE Gaming Home Package in the Installed Product

## Problem

PR553 added the Steam Big Picture Windows Full Screen Experience feature, but the installed product currently ships only the FSE package **source payload**.

The normal release path is:

```text
release.yml
→ pack.ps1
→ publish-layout.ps1
→ fse\Package\AppxManifest.xml / SCCD / executable
→ Velopack package
```

The repository also contains:

```text
scripts/package-fse-home.ps1
scripts/register-fse-home.ps1
```

but neither script participates in the release/install/update lifecycle.

Therefore a normal Velopack installation does not register package identity:

```text
SteamInputAddonforClaw.FseHome
```

and `WindowsSteamFsePackageProbe.TryGetOwnedAumid()` correctly returns no AUMID. The Settings card consequently shows:

```text
The Steam Big Picture Gaming Home package is not registered.
```

and disables the toggle.

This is a production-path defect, not a UI-state defect.

---

## Goal

A normal SteamAddonforClaw install/update must leave the current Windows user with the exact Addon-owned FSE Gaming Home package registered and resolvable.

After normal installation, on a supported Windows build:

```text
Get-AppxPackage -Name SteamInputAddonforClaw.FseHome
→ exactly one current-user package

PackageFamilyName + "!App"
→ resolvable AUMID

Settings → Steam Big Picture Full Screen Experience
→ available immediately
```

The user must not have to:

- run PowerShell manually;
- run `register-fse-home.ps1`;
- enable Developer Mode manually;
- open Windows Gaming settings;
- know the package path/AUMID.

Keep the existing product semantics:

- FSE package registration is infrastructure.
- The Settings toggle is preference.
- Registration alone must **not** enable FSE startup.
- ON/OFF continues to use `GamingHomeApp` and `StartupToGamingHome`.
- No reboot or popup is added to the toggle.

---

# 1. Do not fix this in SettingsPage

Do not make the Settings toggle install/register the package.

The current UI behavior is correct as a fail-closed readback:

```text
package unavailable
→ Available=false
→ toggle disabled
```

Fix the missing provisioning lifecycle instead.

Do not add:

- a "Register package" button;
- a retry button;
- a PowerShell instructions popup;
- registration every time Settings opens;
- package registration on every toggle.

---

# 2. Build a real distributable FSE package during Release

The current `package-fse-home.ps1` already has the correct separation:

```text
published fse package source
→ makeappx
→ signtool
→ signed package
```

Wire this into `.github/workflows/release.yml` / `scripts/pack.ps1`.

The release artifact installed by Velopack must contain a ready-to-register signed package, for example:

```text
fse\SteamInputAddonforClaw.FseHome.msix
```

Do not require `makeappx.exe` or `signtool.exe` on the user's machine.

Those are release-build responsibilities.

The end-user machine should receive the already built/signed package.

## Version

Generate the FSE package manifest version from the Addon release version using a valid four-part MSIX version.

Keep these stable across releases:

```text
Identity Name = SteamInputAddonforClaw.FseHome
Publisher
Application Id = App
architecture
```

Only package version changes.

Stable identity is required so:

- AUMID remains stable;
- update replaces the previous FSE package;
- `GamingHomeApp` remains valid across Addon updates.

---

# 3. Signing is a release prerequisite

Current `package-fse-home.ps1` correctly refuses to produce a registerable package without a certificate.

Do not weaken that behavior.

Do not:

- commit a private PFX;
- commit a certificate password;
- generate a random certificate on every release;
- install a new random root certificate on every user machine;
- permanently enable Developer Mode to bypass signing requirements.

Add explicit GitHub Actions secrets for the stable FSE package signing identity.

Recommended release flow:

```text
GitHub secret: FSE signing certificate/PFX (base64 or equivalent)
GitHub secret: FSE signing password

release job
→ materialize PFX into runner temp directory
→ ConvertTo-SecureString password
→ package-fse-home.ps1
→ delete temporary private-key file in finally/cleanup
```

Do not print the password or PFX contents.

The manifest Publisher must exactly match the signing certificate subject.

Add a release-time verification that fails before publishing when they do not match.

If the project intentionally uses a locally trusted/self-signed certificate rather than a publicly trusted production code-signing chain, then the corresponding **public certificate only** may be shipped for one-time trust provisioning. Never ship the private key.

Do not guess which trust model is acceptable: implement the chosen repository/release signing model explicitly and document it in the PR. The normal installer must work on a clean supported machine using that chosen model.

---

# 4. Prefer normal packaged content; remove unnecessary ExternalLocation design

The current manifest contains:

```xml
<uap10:AllowExternalContent>true</uap10:AllowExternalContent>
```

PR553 copied part of the CTW/ExternalLocation shape, but SteamAddonforClaw now has its own tiny dedicated FSE executable and does not need to wrap the main Velopack executable as external content.

For this follow-up, package the actual FSE executable and required runtime files **inside the MSIX**.

Prefer:

```text
MSIX
├─ AppxManifest.xml
├─ CustomCapability.SCCD
├─ SteamInputAddonforClaw.FseHome.exe
├─ SteamInputAddonforClaw.FseHome.dll
├─ required framework-dependent managed files
├─ Assets\...
└─ Public\...
```

Then register the MSIX normally.

If `AllowExternalContent` is no longer required, remove it.

Do not adopt CTW's `-ExternalLocation` complexity unless hardware testing proves the dedicated packaged FseHome cannot work without it.

This project does not need to preserve CTW's packaging constraints.

---

# 5. Provision from the installed product, not from repository scripts

The normal installed Runtime must own a small one-time provisioning operation.

Do not launch `scripts/register-fse-home.ps1` from the installed app: repository scripts are build/developer tooling and PowerShell policy/environment is unnecessary product coupling.

Implement a narrow product component, conceptually:

```text
SteamFsePackageProvisioner
```

Responsibilities only:

- determine whether the supported OS can use FSE;
- find the exact bundled FSE MSIX;
- enumerate current-user package with exact identity name;
- compare installed package version with bundled version;
- install/update the exact bundled package when missing or older;
- verify exact identity/AUMID afterward;
- return bounded success/failure;
- log result.

Do not turn it into a generic AppX package manager.

Use `Windows.Management.Deployment.PackageManager` directly, consistent with the existing package enumeration/removal code.

Conceptual operation:

```csharp
EnsureProvisioned()
{
    if (!osProbe.Capture().Supported)
        return Unsupported;

    bundled = inspect exact bundled FSE package;

    installed = find current-user package
        where package.Id.Name == PackageIdentityName;

    if (installed exists && installed.Version >= bundled.Version)
        return AlreadyProvisioned;

    AddPackageAsync(
        bundledUri,
        dependencyUris: null,
        deploymentOptions: ForceApplicationShutdown /* only if actually required */);

    verify exact identity exists;
    derive actual package.Id.FamilyName + "!App";
    return Provisioned;
}
```

Use the real `PackageManager` API signatures supported by the current target SDK; the pseudocode above is not a mandate to copy an overload blindly.

---

# 6. When provisioning runs

Run it once from normal Runtime startup after Velopack bootstrap/update scheduling has settled, but before the frontend begins serving the Settings snapshot.

Required lifecycle:

```text
Program startup
→ Velopack bootstrap
→ pending-update scheduling decision
→ if process continues normally:
    FSE infrastructure reconcile
→ Runtime/frontend starts
→ Settings capture
```

This gives a newly installed or updated build a chance to repair missing/outdated FSE infrastructure before the UI asks for state.

Important:

- unsupported OS → skip;
- already-current package → cheap no-op;
- missing package → install once;
- older exact package → update once;
- registration failure → log and continue the Addon;
- FSE failure must not block Full1902 Runtime/controller startup.

Do not put this operation inside controller reconciliation.

Do not delay controller safety on FSE package registration.

If the PackageManager call is observably slow, it may be performed as a bounded startup infrastructure task, but do not create a background watcher or retry service.

---

# 7. Developer Mode / custom capability

The current SCCD contains:

```text
Microsoft.appCategory.gamingHome_8wekyb3d8bbwe
```

and must remain packaged with the manifest.

AnyFSE/CTW references show that development/custom-capability registration can require Developer Mode depending on the package/signing path.

Do not permanently modify Developer Mode.

First test the final signed package on a clean supported Windows installation.

If the final chosen package actually requires temporary Developer Mode for registration, implement the same bounded ownership pattern proven by CTW:

```text
read existing Developer Mode state

already enabled
→ register
→ leave enabled

disabled
→ record original state locally for crash recovery
→ enable only for registration
→ register package
→ restore disabled in finally
→ clear recovery marker
```

Also repair an interrupted temporary change on the next startup before another registration attempt.

Only add this path if real package registration demonstrates it is required.

Do **not** add Developer Mode state/marker preemptively just because CTW needed it for its ExternalLocation design.

This follows the project overengineering policy: preserve real lifecycle safety without copying unnecessary machinery.

---

# 8. UAC/elevation

Do not silently bypass UAC.

If the chosen certificate trust or temporary Developer Mode path requires machine-level elevation, use the project's existing bounded elevated-helper pattern rather than inventing a service.

The normal steady-state must remain:

```text
package already registered
→ no elevation
→ Settings ON/OFF uses HKCU only
```

A one-time Windows UAC prompt during first infrastructure provisioning is acceptable if Windows requires it.

Do not add an extra Addon confirmation dialog before the UAC prompt.

Do not request elevation on every startup.

---

# 9. Package source of truth

Keep exact identity constants shared narrowly with the current probe:

```text
PackageIdentityName = SteamInputAddonforClaw.FseHome
ApplicationId = App
```

Do not hard-code a guessed PackageFamilyName.

After registration:

```text
package.Id.FamilyName + "!App"
```

remains the authoritative AUMID.

The existing fix from PR553 that enumerates packages and matches:

```csharp
package.Id.Name == PackageIdentityName
```

must remain.

Do not regress to passing the identity name to a PackageManager API that expects PFN.

---

# 10. Settings behavior after this fix

Keep `WindowsGamingHomeConfiguration.Capture()` fail-closed.

Expected supported-machine state after normal provisioning:

```text
package registered
GamingHomeApp absent
StartupToGamingHome false

→ Available = true
→ Enabled = false
→ toggle enabled and OFF
```

User turns ON:

```text
StartupToGamingHome = true
GamingHomeApp = exact registered AUMID
→ readback
→ toggle ON
```

User turns OFF:

```text
delete GamingHomeApp
StartupToGamingHome = false
→ readback
→ toggle OFF
```

Do not make registration itself set either registry preference.

---

# 11. Update behavior

On a Velopack application update:

```text
new Addon files installed
→ next normal Runtime start
→ compare bundled FSE package version
→ update exact FSE package if older
```

The stable identity means existing:

```text
GamingHomeApp = <PFN>!App
```

continues to resolve.

Do not clear `GamingHomeApp` merely because the FSE package version changed.

Do not uninstall the old package first unless Windows package update semantics require it.

Prefer an in-place package update with the stable identity.

If package update fails:

- leave the existing registered package usable when possible;
- log failure;
- continue Addon Runtime;
- do not disturb controller ownership.

---

# 12. Uninstall behavior

Keep the PR553 uninstall contract:

```text
delete GamingHomeApp
StartupToGamingHome = false
remove only exact SteamInputAddonforClaw.FseHome package
```

The current exact identity enumeration is appropriate.

If this PR adds a public signing certificate to a machine trust store, uninstall must remove it **only if**:

- the Addon installed that exact certificate;
- it is uniquely identifiable;
- removal cannot affect another product.

Preserve explicit ownership evidence if certificate cleanup is necessary.

Do not remove broad publisher certificates by subject-name wildcard.

FSE uninstall cleanup must remain independent from Full1902 controller stock restoration.

---

# 13. Release pipeline changes

Update `.github/workflows/release.yml` and/or `scripts/pack.ps1` so the actual release cannot succeed without the final registerable FSE package.

Required shape:

```text
restore/build/test
→ publish-layout
→ create signed FSE MSIX
→ copy signed MSIX into final Velopack publish tree
→ verify FSE MSIX exists and identity/version/signature are valid
→ vpk pack
→ publish release
```

Currently `verify-publish-assets.ps1` only verifies FSE source assets.

Change it to require the final distributable package as well.

At minimum verify:

- exact MSIX file exists;
- manifest identity name;
- Application Id;
- `windows.gamingApp`;
- Gaming Home custom capability;
- SCCD included;
- expected package version;
- signature verification succeeds;
- publisher matches manifest identity.

Release must fail closed if FSE packaging/signing failed.

Do not publish a release in which the Settings card is guaranteed to be unavailable.

---

# 14. Local/dev build behavior

Do not make ordinary `dotnet build` require the private signing key.

Keep:

```text
dotnet build
dotnet test
```

usable for development/CI validation.

Separate:

```text
compile/test
```

from:

```text
production release package signing
```

The Release workflow is where signing secrets are required.

For local end-to-end FSE package testing, retain `package-fse-home.ps1` with explicit certificate arguments.

Update `register-fse-home.ps1` as a developer/manual diagnostic helper if useful, but it must no longer be required for normal users.

---

# 15. Tests

Add focused tests; do not mock an entire AppX framework.

Required:

1. Missing exact package → provisioner requests install.
2. Exact current package/version → no install.
3. Older exact package → update requested.
4. Different package identity does not satisfy probe.
5. Package registration success → exact PFN-derived AUMID resolves.
6. Registration failure does not fail Runtime startup.
7. Unsupported OS skips provisioning.
8. Provisioning never changes `GamingHomeApp` or `StartupToGamingHome`.
9. Settings capture after successful provisioning is Available=true / Enabled=false when preference is off.
10. Existing ON state remains ON across package-version update with stable identity.
11. Uninstall removes only the exact owned package.
12. Release/publish contract requires the final FSE MSIX.
13. Release/publish contract fails when final package is absent.
14. Manifest/package identity remains stable.
15. Production package version matches release version mapping.
16. Package contains SCCD and `windows.gamingApp` extension.

If Developer Mode handling is actually required after hardware validation, additionally test:

17. pre-enabled Developer Mode is not disabled afterward;
18. temporarily enabled state is restored;
19. interrupted temporary ownership is repaired on next startup.

Do not add those three if the final signed package does not need Developer Mode.

---

# 16. Manual validation — mandatory before considering the feature complete

Use a supported clean Windows 11 environment at build 26100.8039 or newer.

## Fresh install

Start with:

```powershell
Get-AppxPackage -Name SteamInputAddonforClaw.FseHome
```

returning nothing.

Install SteamAddonforClaw normally through its real Velopack installer.

Launch normally.

Verify:

```powershell
Get-AppxPackage -Name SteamInputAddonforClaw.FseHome
```

returns exactly one package.

Verify the Settings FSE toggle is enabled and initially OFF.

No manual PowerShell registration is allowed in this test.

## Enable

Turn the toggle ON.

Verify:

```text
GamingHomeApp = actual PFN!App
StartupToGamingHome = 1
```

Restart Windows manually.

Verify Windows FSE launches the packaged FseHome and FseHome launches Steam BPM.

Verify existing Full1902 presentation reconciliation reaches SteamDeck without any FSE-specific controller handshake.

## Disable

Turn toggle OFF.

Verify:

```text
GamingHomeApp absent
StartupToGamingHome = 0
```

Restart Windows manually and verify normal desktop startup.

## Update

Enable FSE, then install a newer Addon build with a newer FSE package version.

Verify:

- exact package identity/PFN remains stable;
- package version updates;
- `GamingHomeApp` remains valid;
- toggle remains ON;
- Steam BPM still launches after reboot.

## Uninstall

With FSE enabled, uninstall through the supported path.

Verify:

- `GamingHomeApp` absent;
- `StartupToGamingHome=0`;
- exact Addon FSE package absent;
- normal Windows startup remains available;
- existing Full1902 stock-safe uninstall behavior is unchanged.

---

# 17. Non-goals

Do not add:

- current-session `SetGamingFullScreenExperience`;
- reboot automation;
- FSE retry daemon;
- package-registration watcher;
- Settings-time provisioning;
- controller/FSE startup barrier;
- new controller state;
- CTW integration;
- generic launcher support;
- external-location packaging unless demonstrated necessary;
- permanent Developer Mode;
- per-user duplicate FSE preference state.

---

# 18. Acceptance criteria

This follow-up is complete when:

1. A normal release contains a ready-to-register signed FSE package.
2. A normal fresh installation automatically registers that package for the current user.
3. No manual script is required.
4. Settings no longer reports “Gaming Home package is not registered” after successful normal installation.
5. The toggle is available and OFF before the user chooses FSE.
6. Registration does not enable FSE by itself.
7. ON/OFF registry semantics from PR553 are unchanged.
8. FSE infrastructure failure never blocks Full1902 controller Runtime.
9. Update reconciles an older FSE package without changing stable identity.
10. Existing FSE preference survives package update.
11. Uninstall clears Gaming Home preference and removes the exact Addon package.
12. Private signing material is never committed or shipped.
13. Developer Mode is not permanently changed.
14. No generic package manager/watcher/state machine is introduced.
15. Release verification fails if the final FSE package is missing or invalid.
16. Fresh-install, update, ON/OFF reboot, and uninstall are manually validated on a supported Windows build.

---

# 19. Architecture boundary

The final ownership model remains:

```text
Release pipeline
→ builds/signs FSE package

Runtime startup infrastructure
→ ensures exact package is registered/current

Windows GamingConfiguration
→ source of truth for user's FSE startup preference

FseHome
→ launches steam://open/bigpicture once and exits

existing Steam/BPM watcher
→ owns BPM fact

existing Full1902 presentation owner
→ owns Xbox360 ↔ SteamDeck selection
```

Do not merge these responsibilities.

The bug is missing package provisioning. Fix that lifecycle directly rather than adding UI workarounds.
