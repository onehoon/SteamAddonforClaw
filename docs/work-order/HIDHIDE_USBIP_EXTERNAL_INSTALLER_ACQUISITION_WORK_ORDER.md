# Work Order — PR-A: Externalize HidHide and usbip-win2 Installer Payloads

> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed baseline:** `main@1393f57ea35a725523904a06a8241f7959bff75f`  
> **Date:** 2026-09-22  
> **Scope:** package-size reduction only for HidHide and usbip-win2 prerequisite installer delivery  
> **Architecture baseline:** standalone Full1902 application; CTW integration is out of scope

---

## 1. Goal

Stop shipping the HidHide and usbip-win2 installer executables inside every Velopack release.

Current release payload includes:

```text
Dependencies\HidHide\HidHide_1.5.230_x64.exe
Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe
```

Current upstream asset sizes are:

```text
HidHide_1.5.230_x64.exe     8,078,016 bytes
USBip-0.9.8.0-x64.exe     26,390,744 bytes
---------------------------------------
total                      34,468,760 bytes
                           ~32.87 MiB raw publish payload
```

PR-A must change only the installer **acquisition source**:

```text
before
explicit prerequisite setup
→ elevated prerequisite helper
→ use installer bundled under AppContext.BaseDirectory
→ SHA-256 verify
→ install
→ exact package/runtime readback

after
explicit prerequisite setup
→ elevated prerequisite helper
→ download exact pinned official upstream installer
→ stage under trusted ProgramData provisioning storage
→ SHA-256 verify
→ existing installer execution path
→ exact package/runtime readback
```

Do not change controller authority, prerequisite admission policy, usbip version policy, HidHide ownership policy, VIIPER ownership, or Velopack update semantics.

---

## 2. Current product contract that must remain authoritative

Read these documents before implementation:

1. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. `docs/Full 1902 Implementation/README.md`

The current controller authority remains:

```text
Center M Enabled
→ stock MSI authority
→ desired physical PID1901

Center M Disabled
→ Addon Runtime authority
→ desired physical PID1902
→ deterministic HidHide baseline
→ Addon-owned VIIPER presentation
```

Prerequisite installation is infrastructure required before that authority can be exercised safely.

A network/download failure must never be treated as permission to continue without a prerequisite.

Required failure behavior:

```text
required component missing/update-required
+ exact installer cannot be acquired and verified
→ prerequisite setup fails
→ controller prerequisite remains not ready
→ existing Full1902 fail-close behavior remains in force
```

Do not create any new controller authority or recovery state for download failures.

---

## 3. Existing owner must remain the only production installer owner

The current production prerequisite mutation owner is:

```text
ElevatedPrerequisiteSetup
```

It already owns:

- the global prerequisite setup mutex;
- supported-hardware preflight;
- trusted ProgramData provisioning storage creation/inspection;
- Steam/session safety gates;
- HidHide and usbip package inspection;
- first install vs usbip update classification;
- provisioning receipts;
- installer SHA-256 verification;
- silent installer execution;
- exit-code handling;
- exact post-install package verification;
- pending-reboot handling;
- receipt reconciliation.

PR-A must **reuse this owner**.

Do not add:

- a dependency manager;
- a driver updater service;
- a background downloader;
- a startup download worker;
- a package cache manager;
- a second elevated helper;
- a second prerequisite state machine;
- a second receipt/journal;
- a retry daemon;
- download epochs/generations/leases.

A small deterministic installer-acquisition helper is acceptable only to keep network/file/hash code testable. It must not own installation policy.

---

## 4. Pin exact official upstream assets

Continue to support the exact versions already supported by current `main`.

### HidHide

Keep:

```text
Version:
1.5.230.0

File:
HidHide_1.5.230_x64.exe

Official release:
https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0

Pinned direct asset:
https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe

SHA-256:
F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6
```

### usbip-win2

Keep:

```text
Version:
0.9.8.0

File:
USBip-0.9.8.0-x64.exe

Official release:
https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.0

Pinned direct asset:
https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.0/USBip-0.9.8.0-x64.exe

SHA-256:
81F426741F7EE2ED991FEBE24A22DACA8400B6AE2F171054E3FB404897E15D39
```

The usbip digest above also matches the digest published by the upstream GitHub release asset.

Do not:

- call a GitHub `latest` URL;
- discover the newest release dynamically;
- accept a version returned by a web API;
- change target versions in this PR;
- fall back to mirrors;
- silently accept another filename;
- accept another hash;
- auto-downgrade a newer installed usbip package.

The application version policy remains pinned and deterministic.

---

## 5. Metadata changes

Current metadata embeds a path under the Velopack installation:

```csharp
HidHidePackageMetadata.InstallerPath
UsbIpWin2PackageMetadata.InstallerPath
UsbIpWin2PackageMetadata.VerifyInstaller()
```

Those app-bundle-path assumptions must be removed.

Keep the existing pinned version/file/hash metadata and add one fixed HTTPS asset URI per component.

Conceptual shape only:

```csharp
internal static class HidHidePackageMetadata
{
    public static readonly Version BundledVersion = new(1, 5, 230, 0);
    public const string InstallerFileName = "HidHide_1.5.230_x64.exe";
    public const string InstallerSha256 = "...";
    public static readonly Uri InstallerDownloadUri =
        new("https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe");
}
```

and equivalent usbip metadata.

For this PR, do **not** perform a repository-wide terminology rename from `BundledVersion` to `TargetVersion` merely for style. Existing code uses `BundledVersion` as the app-pinned supported package version in many prerequisite/version-policy locations. Keep the diff focused.

A short comment may clarify that the version remains app-pinned even though the installer binary is no longer bundled.

---

## 6. Installer acquisition location

Do not download into:

- the Velopack application directory;
- the current-user Temp directory;
- Downloads/Desktop;
- another user-writable path.

Use the already-owned trusted location:

```text
%ProgramData%\SteamInputAddonforClaw\provisioning
```

represented by:

```csharp
VelopackAppPaths.ProvisioningStateDirectory
```

This location is already established by `ProvisioningStorageSecurity.EnsureTrustedStorage(...)` before prerequisite mutation.

Current ACL contract is suitable:

```text
Owner: Administrators or SYSTEM

SYSTEM         FullControl
Administrators FullControl
Users          ReadAndExecute

broad user principals must not have write/delete/ACL ownership rights
reparse-point paths are rejected
```

The elevated helper must perform the download itself after this storage has been proven trusted.

Do not let the unelevated UI/runtime download an executable into a user-writable staging area and then ask the elevated process to trust it.

---

## 7. Keep downloaded installers ephemeral

PR-A should not introduce a persistent prerequisite installer cache.

Recommended staging contract:

```text
trusted provisioning directory
→ deterministic per-component staging filename
→ overwrite/recreate for every actual acquisition attempt
→ download exact pinned asset
→ SHA-256 verify
→ use for this setup attempt only
→ best-effort delete in finally
```

Example staging paths may be:

```text
%ProgramData%\SteamInputAddonforClaw\provisioning\HidHide_1.5.230_x64.exe
%ProgramData%\SteamInputAddonforClaw\provisioning\USBip-0.9.8.0-x64.exe
```

or another equally narrow owned filename.

Important invariants:

- never reuse a previous file without reacquiring and re-verifying it;
- never trust filename or HTTP headers as identity;
- use only the pinned filename/URI/hash from application metadata;
- a crash leaving a staging file must not make the next run trust that file;
- the next acquisition overwrites/replaces it and verifies the new bytes;
- best-effort deletion failure is not a controller-safety failure because the file remains in the protected ProgramData directory and is never trusted without reacquisition/hash verification.

Do not add a cleanup database or stale-file manager.

---

## 8. Network behavior

Use a normal bounded HTTPS download.

Required behavior:

```text
GET exact pinned HTTPS URI
→ require successful HTTP status
→ stream response to trusted staging file
→ close file
→ compute SHA-256 from staged bytes
→ compare exact pinned digest
```

Use streaming; do not require loading the entire 26 MiB usbip installer into memory.

A small bounded timeout is appropriate so a broken network cannot hang the setup helper indefinitely.

Do not add automatic multi-attempt retry/backoff machinery in this PR. The existing explicit prerequisite setup can be run again by the user after an ordinary network failure.

GitHub release URLs may redirect to GitHub's release-asset storage. That is acceptable because the executable identity authority is the pinned SHA-256 digest.

Do not add certificate pinning or final-host allow-list machinery solely for redirects.

---

## 9. Acquisition result and logging

Keep the acquisition result narrow.

It only needs to communicate enough for `ElevatedPrerequisiteSetup` to decide:

```text
success → exact local staging path
failure → bounded reason
```

Useful stable failure reasons include:

```text
InstallerDownloadFailed
InstallerDownloadHttpFailure
InstallerDownloadTimedOut
InstallerHashMismatch
InstallerStagingFailed
```

Do not log signed URLs, response bodies, or unnecessary network details.

Log at least:

```text
Component
PinnedVersion
PinnedAssetUri
DownloadStarted
DownloadCompleted
BytesWritten
HashVerified
```

Do not log per-buffer progress.

---

## 10. Important receipt ordering change

Current `ElevatedPrerequisiteSetup` writes `InstallStarted` before calling the bundled installer.

With external acquisition, a pure network failure is not an installer attempt.

Preferred ordering for each component:

```text
package/runtime assessment says installation is required
→ acquire pinned installer into trusted ProgramData
→ verify pinned SHA-256
→ persist InstallStarted receipt
→ run the existing immediate pre-install safety gate
→ RunChild re-verifies SHA-256 immediately before execution
→ execute installer
→ existing post-install verification
→ existing receipt transition
→ cleanup staging file in finally
```

This preserves the distinction:

```text
download failed
→ no InstallStarted receipt

installer was actually ready to execute
→ InstallStarted receipt exists
```

The existing second safety gate must remain **after** download and immediately before installer execution, because Steam/session state may change while the download is in progress.

Do not remove the initial safety gate.

Do not weaken receipt reconciliation.

---

## 11. Preserve double integrity verification

The acquisition step must hash the completed download.

The existing `RunChild(... expectedHash)` validation must also remain and re-hash the exact path immediately before `Process.Start`.

Conceptually:

```text
download completes
→ hash check #1
→ receipt + safety gate
→ RunChild
→ hash check #2
→ Process.Start
```

This is not theoretical race hardening; it preserves the current install boundary after moving the executable outside the immutable Velopack payload.

Update `RunChild` wording from "Bundled prerequisite installer..." to neutral wording such as:

```text
Prerequisite installer was not found.
Prerequisite installer hash validation failed.
```

Do not remove the hash parameter from `RunChild`.

---

## 12. HidHide install path

Current HidHide install behavior must otherwise remain unchanged:

```text
HidHide installation status == Missing
→ snapshot desktop shortcuts
→ validate legacy receipt policy
→ acquire exact pinned HidHide installer
→ save InstallStarted receipt
→ BeforeHidHideInstall safety gate
→ run:
   /exenoui /qn /norestart
→ exact package evidence / prerequisite probe
→ current receipt state decision
→ remove installer-created desktop shortcuts only when exact package is established
```

Do not add HidHide upgrade behavior in PR-A.

Do not change:

- HidHide package-version equivalence policy;
- official HidHide CLI/Client path authority;
- deterministic Disabled-mode HidHide normalization;
- reboot handling;
- legacy receipt policy.

### Legacy `HidHideProvisioner`

`HidHideProvisioner` currently has no production construction call site but its default installer-path provider points to `HidHidePackageMetadata.InstallerPath`.

Do not create a second downloader inside that class.

Preferred minimal correction:

- remove its implicit bundled-path default;
- require its installer path provider explicitly where the class is instantiated by tests;
- keep its existing unit-testable provisioning behavior otherwise unchanged.

Do not use this PR to redesign or delete the whole legacy/test-facing HidHide provisioning component.

---

## 13. usbip-win2 install/update path

Preserve the existing self-upgrade policy added before this PR.

Current allowed mutation cases remain:

```text
Missing
→ install 0.9.8.0

UpdateRequired
→ installed version is older than 0.9.8.0
→ install pinned 0.9.8.0

Installed
→ do nothing

Incompatible
→ block

ExistingUnverified
→ block

Indeterminate
→ block
```

Required flow:

```text
ShouldInstallUsbIp(status) == true
→ acquire exact pinned 0.9.8.0 x64 installer
→ save existing receipt with:
   PreInstallationStatus
   PreviousInstalledVersion
→ BeforeUsbIpInstall safety gate
→ run existing silent arguments:
   /VERYSILENT
   /SUPPRESSMSGBOXES
   /NORESTART
   /RESTARTEXITCODE=3010
   /TYPE=compact
   /NOICONS
→ existing exact-version post-install verification
```

Do not change:

- `UpdateRequired` classification;
- exact runtime version compatibility;
- newer-than-target fail-close behavior;
- receipt schema unless actually required by this acquisition change;
- exit 3010 semantics;
- reboot-bound reconciliation;
- unresolved `InstallStarted` fail-close behavior;
- VIIPER ABI/runtime requirements.

---

## 14. Offline behavior

Required behavior is intentionally asymmetric.

### Existing correct prerequisites

```text
HidHide exact supported package installed
usbip-win2 exact supported package installed
→ no installer acquisition
→ no network requirement
→ normal Runtime operation remains offline-capable
```

### Missing/old prerequisite

```text
HidHide missing
or
usbip missing/update-required
+ no network
→ explicit prerequisite setup fails
→ component remains not ready
→ user may retry later
```

Do not add an offline fallback copy to the Velopack installer; that would defeat PR-A.

---

## 15. Remove installer payloads from the application package

Delete the two binary payloads from the source tree:

```text
src/SteamInputAddonforClaw/Dependencies/HidHide/HidHide_1.5.230_x64.exe
src/SteamInputAddonforClaw/Dependencies/UsbIpWin2/USBip-0.9.8.0-x64.exe
```

Remove the corresponding `Content` entries from:

```text
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
```

Do **not** remove or externalize VIIPER in this PR.

VIIPER remains:

```text
Dependencies\Viiper\libVIIPER.dll
Dependencies\Viiper\PROVENANCE.md
Dependencies\Viiper\libVIIPER.h
Dependencies\Viiper\LICENSE.txt
```

and must retain its current publish/hash/provenance contract.

---

## 16. Publish verification must invert the old contract

Current `scripts/verify-publish-assets.ps1` requires both installers and verifies their bundled hashes.

After PR-A:

- remove HidHide installer from `$requiredAssets`;
- remove usbip installer from `$requiredAssets`;
- remove the positive published-installer hash checks;
- add negative assertions that the release payload must **not** contain these prerequisite installer executables.

At minimum reject:

```text
Dependencies\HidHide\HidHide_1.5.230_x64.exe
Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe
```

Prefer an assertion wording that clearly states:

```text
Published output must not bundle the HidHide prerequisite installer.
Published output must not bundle the usbip-win2 prerequisite installer.
```

Do not weaken any existing checks for:

- VIIPER;
- ClawHUD lock/payload exclusion;
- framework-dependent .NET publish;
- UI/Overlay/QAM/FSE assets.

`scripts/report-publish-size.ps1` may keep the HidHide/USBip component categories. They will naturally report zero bytes when the payloads are absent and this is useful evidence for the size reduction.

Do not mix unrelated size-script refactoring into this PR.

---

## 17. Publish verification tests

Update:

```text
scripts/tests/verify-publish-assets.tests.ps1
```

The normal success fixture must no longer copy the two installer binaries.

Add focused negative cases:

```text
otherwise valid publish
+ HidHide installer added
→ verify-publish-assets.ps1 fails

otherwise valid publish
+ usbip installer added
→ verify-publish-assets.ps1 fails
```

The tests must no longer depend on the deleted source installer binaries.

Existing VIIPER and other publish verification fixtures remain unchanged.

---

## 18. Runtime/unit tests for pinned download acquisition

Replace tests whose only purpose is proving the source tree contains the bundled binary:

```text
HidHideProvisionerTests.BundledInstaller_HashMatchesRuntimeMetadata
FirstTimeSetupPolicyTests.BundledUsbIpInstaller_HashMatchesRuntimeMetadata
```

with metadata/acquisition tests.

Do not make unit tests access GitHub.

Use a fake/injected stream or HTTP handler.

Required focused cases:

1. exact HidHide metadata uses the pinned HTTPS release URI, filename, version and expected hash;
2. exact usbip metadata uses the pinned HTTPS release URI, filename, version and expected hash;
3. successful download writes the expected staging file and returns success only after SHA-256 matches;
4. hash mismatch fails and never returns an executable path for launch;
5. HTTP failure fails;
6. truncated/partial bytes fail by hash;
7. staging write failure fails;
8. existing stale staging file is overwritten/replaced and never trusted as-is;
9. exact installed HidHide never invokes acquisition;
10. exact installed usbip never invokes acquisition;
11. usbip `UpdateRequired` invokes acquisition;
12. usbip newer/incompatible never invokes acquisition;
13. download failure occurs before `InstallStarted` is persisted;
14. successful acquisition followed by blocked pre-install safety gate transitions the newly-created receipt to the existing cancelled/failure-safe state and never launches the installer;
15. the installer launch path is still hash-validated immediately before process creation.

Do not create network integration tests that depend on GitHub availability for normal CI.

---

## 19. Existing prerequisite tests that must remain valid

Preserve current behavior covered by existing tests:

- hardware unsupported/indeterminate never offers mutation;
- Steam active blocks prerequisite mutation;
- recovery unsafe blocks mutation;
- failed usbip upgrade with an older package remains retryable;
- unresolved usbip `InstallStarted` remains blocked;
- newer usbip package remains incompatible and is never downgraded;
- pending reboot semantics remain unchanged;
- exact package post-install evidence remains the success boundary;
- provisioning storage ACL checks remain fail-closed;
- global setup mutex remains the only cross-process setup gate.

If test refactoring is necessary to inject the acquisition step, keep the policy assertions intact rather than weakening them.

---

## 20. Third-party notices

Update `THIRD_PARTY_NOTICES.md`.

Current HidHide wording says the installer is bundled.

Change the distribution wording to make the new model explicit, for example:

```text
Distribution: The application pins the official upstream release installer version and SHA-256.
When explicit prerequisite provisioning is required, the installer is downloaded from the pinned
official GitHub release asset, verified before execution, and is not included in the application package.
```

Do the equivalent for usbip-win2.

Keep the upstream projects and licenses unchanged.

This PR does not change VIIPER licensing/provenance.

---

## 21. Do not change historical work orders

Historical documents that describe a bundled installer are records of the implementation contract at that time.

Do not mass-edit:

```text
docs/usbip2/...
older docs/work-order/...
```

Add this new work order as the current implementation instruction.

Current code and current Full1902 authority take precedence over old packaging wording.

---

## 22. Suggested production files

Expected production changes:

```text
src/SteamInputAddonforClaw/HidHide/HidHideProvisioning.cs
src/SteamInputAddonforClaw/Prerequisites/UsbIpWin2Provisioning.cs
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
THIRD_PARTY_NOTICES.md
scripts/verify-publish-assets.ps1
```

A new narrow helper file is acceptable, for example:

```text
src/SteamInputAddonforClaw/Prerequisites/PrerequisiteInstallerAcquisition.cs
```

only if it contains the bounded acquisition/staging/hash mechanics.

Do not turn it into a generic package manager.

Expected test changes:

```text
tests/SteamInputAddonforClaw.Tests/HidHideProvisionerTests.cs
tests/SteamInputAddonforClaw.Tests/FirstTimeSetupPolicyTests.cs
scripts/tests/verify-publish-assets.tests.ps1
```

A focused new acquisition test file is preferred if that keeps policy tests readable.

Binary deletions:

```text
src/SteamInputAddonforClaw/Dependencies/HidHide/HidHide_1.5.230_x64.exe
src/SteamInputAddonforClaw/Dependencies/UsbIpWin2/USBip-0.9.8.0-x64.exe
```

---

## 23. Explicit non-goals

Do not include any of the following in PR-A:

- Windows App SDK framework-dependent conversion;
- Windows App Runtime bootstrap/download;
- `Microsoft.Windows.SDK.NET.dll` deduplication;
- FSE Home TFM changes;
- .NET runtime packaging changes;
- Velopack redesign;
- Setup EXE redesign;
- Windows App SDK version upgrade;
- HidHide version upgrade;
- usbip version upgrade;
- VIIPER version/ABI changes;
- VIIPER external download;
- controller lifecycle changes;
- PID1901/PID1902 transition changes;
- HidHide deterministic authority changes;
- PnP recovery changes;
- Sleep/Hibernate/Resume changes;
- Center M startup-root changes;
- new driver rollback machinery;
- automatic network retry service;
- generic dependency abstraction.

PR-B and later package-size work will be handled separately.

---

## 24. Real lifecycle/failure cases to handle

Follow the repository race/overengineering policy.

Handle realistic cases:

### Network unavailable

```text
download required
→ acquisition fails
→ no installer launch
→ no false Provisioned state
→ setup exits failed
→ later explicit retry is possible
```

### Download interrupted/corrupt

```text
partial/corrupt file
→ SHA-256 mismatch
→ no installer launch
→ setup fails
```

### Application/process crash during download

```text
staging file may remain in protected ProgramData
→ next acquisition overwrites/replaces it
→ never trust stale bytes
```

### Steam becomes active while download is occurring

```text
download may complete
→ existing Before*Install safety gate re-checks
→ installer does not run when gate is blocked
```

### Installer returns 3010

Preserve current pending-reboot behavior exactly.

### Installer fails

Preserve current `AttemptFailed` behavior exactly.

### Physical controller/lifecycle state changes

Existing hardware/safety checks remain authoritative. Do not add special download-specific controller states.

Do not add synchronization solely for pathological instruction-level interleavings.

---

## 25. Manual validation

Use a clean supported MSI Claw test environment.

### A. Existing fully provisioned installation, offline

Precondition:

```text
HidHide 1.5.230.0 installed
usbip-win2 0.9.8.0 installed
```

Disconnect network.

Launch the Addon.

Verify:

- no prerequisite installer download is attempted;
- no prerequisite setup is required solely because installers are no longer bundled;
- normal Runtime/controller behavior is unchanged.

### B. Missing HidHide

Remove HidHide using the supported test procedure.

With network available:

- launch normal prerequisite setup;
- verify exact official HidHide asset is downloaded;
- verify logged hash validation succeeds;
- verify installer runs with existing silent arguments;
- verify exact package/readback behavior;
- verify staging installer is removed best-effort afterward.

Repeat with network unavailable:

- setup must fail without installer launch;
- no controller authority bypass occurs.

### C. Missing usbip

Repeat equivalent validation for usbip 0.9.8.0.

### D. Older usbip package

Use a supported test fixture/known older package if available.

Verify:

```text
older package
→ UpdateRequired
→ pinned 0.9.8.0 asset downloaded
→ exact hash verified
→ existing upgrade receipt records prior version
→ existing update path executes
```

### E. Tampered download simulation

Use the unit/injected-download test path rather than modifying upstream traffic.

Verify wrong bytes never reach `Process.Start`.

### F. Publish payload

Publish the real release layout and verify:

```text
Dependencies\HidHide\HidHide_1.5.230_x64.exe      absent
Dependencies\UsbIpWin2\USBip-0.9.8.0-x64.exe      absent
Dependencies\Viiper\libVIIPER.dll                  present
```

Run the size report and record before/after total raw publish size.

Expected raw reduction from these two binaries alone is approximately:

```text
34,468,760 bytes
~32.87 MiB
```

Compression means the final Velopack Setup reduction will not equal the raw figure exactly.

---

## 26. Validation commands

Run at minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
pwsh -File scripts/tests/verify-publish-assets.tests.ps1
git diff --check
```

Also run the repository's current release/publish validation path so the final Velopack input is checked rather than only project build output.

Run:

```text
scripts/report-publish-size.ps1
```

against the real publish directory and include the before/after result in the PR description.

---

## 27. Acceptance criteria

PR-A is complete only when all of the following are true.

1. HidHide supported version remains exactly 1.5.230.0.
2. usbip-win2 supported version remains exactly 0.9.8.0.
3. The existing SHA-256 pins remain unchanged.
4. Each component has one exact pinned official HTTPS release-asset URI.
5. HidHide installer binary is deleted from the repository/publish payload.
6. usbip installer binary is deleted from the repository/publish payload.
7. Runtime `.csproj` no longer copies either installer into output/publish.
8. Normal startup with already-correct prerequisites performs no download.
9. Missing HidHide acquires only the pinned official HidHide asset.
10. Missing usbip acquires only the pinned official usbip asset.
11. usbip `UpdateRequired` acquires the pinned usbip target.
12. newer/incompatible usbip never triggers a download/downgrade.
13. Download occurs in the elevated prerequisite owner, not the unelevated UI.
14. Staging is under the existing trusted ProgramData provisioning path.
15. A downloaded installer is SHA-256 verified before an `InstallStarted` receipt is created.
16. The existing immediate pre-install safety gate remains after download.
17. `RunChild` re-verifies the exact hash immediately before process launch.
18. HTTP/network/hash/staging failure never launches an installer.
19. Download failure does not produce a false `InstallStarted` receipt.
20. Existing installer exit-code and post-install exact-version verification remain unchanged.
21. Exit 3010/reboot reconciliation remains unchanged.
22. Existing usbip upgrade receipt semantics remain unchanged.
23. Existing Full1902 controller fail-close behavior remains unchanged.
24. No second prerequisite owner/downloader service/state machine is introduced.
25. Downloaded installers are not retained as a new persistent cache.
26. Publish verification now rejects accidental rebundling of either installer.
27. Publish verification still requires and verifies VIIPER.
28. Unit tests do not depend on live GitHub network access.
29. `THIRD_PARTY_NOTICES.md` describes pinned on-demand official download instead of bundled distribution.
30. Real publish size drops by the two removed installer payloads, with before/after measurements recorded.
31. Windows App SDK/runtime packaging is untouched.
32. FSE Home/`Microsoft.Windows.SDK.NET.dll` work is untouched.
33. Full Release build/tests and publish verification pass.
34. `git diff --check` is clean.

---

## 28. Final architecture after PR-A

```text
Velopack application payload
├─ Addon Runtime
├─ UI
├─ Overlay
├─ QAM
├─ FSE
├─ VIIPER pinned native payload
└─ normal app assets
   (NO HidHide installer)
   (NO usbip installer)

Explicit prerequisite setup
        │
        ▼
existing elevated prerequisite helper
        │
        ├─ hardware/safety/storage gates
        │
        ├─ HidHide missing?
        │     └─ download pinned official asset
        │        → SHA-256 verify
        │        → receipt
        │        → safety re-check
        │        → existing installer/readback path
        │
        └─ usbip missing/update-required?
              └─ download pinned official asset
                 → SHA-256 verify
                 → receipt
                 → safety re-check
                 → existing installer/readback path
```

The architectural rule is:

> **Change delivery, not authority.**

HidHide and usbip remain exact app-pinned prerequisites with the same installation and lifecycle policy. PR-A only removes their large installer executables from every application release and acquires those exact bytes from their official upstream release when an actual prerequisite mutation is required.
