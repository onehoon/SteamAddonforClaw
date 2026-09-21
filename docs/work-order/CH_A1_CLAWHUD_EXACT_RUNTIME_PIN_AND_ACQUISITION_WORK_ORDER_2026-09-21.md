# Work Order — CH-A1 ClawHUD Exact Runtime Pin and Acquisition

**Date:** 2026-09-21  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**PR base:** `main`  
**Target branch after merge:** `main`  
**Recommended implementation branch:** `feature/clawhud-a1-runtime-acquisition`  
**Current reviewed main:** `e39a9e078f5393207e3427c7fc06bb256d80fbae`  
**Expected PR count:** 1 focused PR  
**Feature track:** ClawHUD ↔ SteamAddon integration — CH-A1

---

## 0. Mandatory branch / scope rule

This implementation is no longer staged behind `integration/clawhud`.

Use the current SteamAddon `main` as the implementation base and PR target.

Conceptually:

    main
      -> feature/clawhud-a1-runtime-acquisition
      -> PR
      -> main

Before coding, update the implementation branch from the latest `main` and re-check the files named in this work order. The reviewed commit above records the design baseline; it is not permission to ignore newer `main` changes.

The older architecture handoff at:

    docs/work-order/CLAW_HUD_STEAMADDON_INTEGRATION_ARCHITECTURE_AND_PR_PLAN_2026-09-21.md
    branch: integration/clawhud

remains the overall design source for the ClawHUD integration, except for its former requirement to keep Addon implementation PRs on `integration/clawhud`.

For Addon implementation PRs, this work order supersedes that branch rule:

    PR base = main

ClawHUD itself remains on its own integration track:

    onehoon/ClawHUD
    integration/steamaddon

Do not move ClawHUD product/runtime implementation into this repository.

---

## 1. Read these sources before implementation

### SteamAddon — mandatory

Read the current versions of:

    docs/Full 1902 Implementation/README.md
    docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
    docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md

Also inspect current production code, at minimum:

    src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
    src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
    scripts/verify-publish-assets.ps1
    tests/SteamInputAddonforClaw.Tests/AddonDataPathsTests.cs
    tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj

Use current repository style for logging, JSON, cancellation, tests, and internal visibility.

### ClawHUD producer contract — mandatory

Read:

    onehoon/ClawHUD
    branch: integration/steamaddon

    docs/work-orders/managed/
      CH_I3_STEAMADDON_COMPANION_RUNTIME_PAYLOAD_WORK_ORDER_2026-09-21.md

    steamaddon-runtime/package-runtime.ps1
    steamaddon-runtime/payload.manifest.json

CH-A1 is the Addon consumer of that producer contract.

Do not infer the Runtime ZIP shape from the old chat/design document when the actual ClawHUD producer code is more specific.

---

## 2. Product architecture that must not change

SteamAddon remains the standalone Full1902 controller product.

ClawHUD is not part of controller authority.

Controller ownership remains:

    Center M Enabled
      -> MSI / stock authority
      -> physical PID1901
      -> no Addon DirectInput ownership
      -> no Addon controller HidHide ownership
      -> no Addon VIIPER presentation

    Center M Disabled
      -> Addon Runtime authority
      -> desired physical PID1902
      -> Addon DirectInput
      -> Addon deterministic persistent HidHide baseline
      -> canonical VIIPER
      -> exactly one current virtual presentation

Virtual presentation remains:

    Steam/BPM inactive -> Xbox360
    Steam/BPM active   -> SteamDeck

ClawHUD is an optional companion feature beside that graph.

Therefore:

    ClawHUD download failure
    ClawHUD package corruption
    ClawHUD Runtime unavailable
    network unavailable

must not:

    switch PID
    mutate HidHide
    teardown VIIPER
    change Center M authority
    delay controller recovery
    alter the current X360/SteamDeck presentation

CH-A1 does not wire ClawHUD into startup at all, so controller runtime behavior should be unchanged by construction.

---

## 3. CH-A1 goal

Implement only the immutable ClawHUD Runtime dependency consumer.

End state:

    Addon-shipped clawhud.lock.json
      -> exact Runtime version/tag/source commit/ZIP SHA
      -> deterministic release URL
      -> download exact ClawHUDRuntime.zip
      -> verify ZIP SHA-256
      -> reject unsafe archive paths
      -> extract to staging
      -> validate embedded Runtime identity
      -> validate required payload files
      -> adopt into versioned Addon data directory
      -> return verified ClawHUD.exe path

No ClawHUD process is launched in CH-A1.

No ClawHUD IPC is implemented in CH-A1.

No HUD setting is added in CH-A1.

No Main UI / Overlay surface is added in CH-A1.

---

## 4. Exact Runtime pin for this PR

Pin the already-published ClawHUD Managed Runtime:

    runtime_version = 1.0.1
    tag             = steamaddon-runtime-v1.0.1
    asset           = ClawHUDRuntime.zip
    source_commit   = 717c57ea1812a874cf474f360faa01ed50cf39ea
    sha256          = 9e9fc07c43db90837d01de6b24388a001eae1b3da222d1ee8dbf1dabcf0383f5

GitHub release properties verified during work-order preparation:

    release name = SteamAddon Runtime v1.0.1
    prerelease   = true

Assets:

    ClawHUDRuntime.zip
    ClawHUDRuntime.zip.sha256
    runtime-manifest.json

Do not pin:

    latest
    latest prerelease
    integration/steamaddon HEAD
    v0.1.x Standalone release
    releases.stable.json

The Addon local lock is the product dependency authority.

---

## 5. Required Addon lock file

Add:

    src/SteamInputAddonforClaw/
      Dependencies/
        ClawHUD/
          clawhud.lock.json

Exact initial content:

~~~json
{
  "schema_version": 1,
  "runtime_version": "1.0.1",
  "tag": "steamaddon-runtime-v1.0.1",
  "asset": "ClawHUDRuntime.zip",
  "source_commit": "717c57ea1812a874cf474f360faa01ed50cf39ea",
  "sha256": "9e9fc07c43db90837d01de6b24388a001eae1b3da222d1ee8dbf1dabcf0383f5"
}
~~~

This is metadata only.

Do not vendor:

    ClawHUDRuntime.zip
    ClawHUD.exe
    ClawHUD.EcHelper.exe
    PresentMonAPI2Loader.dll
    ClawHUD.PresentMonRuntime.msi

into the SteamAddon repository or normal Addon installer.

---

## 6. Important producer-contract correction

The actual ClawHUD CH-I3 packager currently does this:

1. create the embedded `clawhud/runtime-manifest.json`;
2. at that moment set:

       sha256 = null

3. create `ClawHUDRuntime.zip`;
4. compute the final ZIP SHA-256;
5. write that final SHA only into the external release `runtime-manifest.json` and the `.sha256` sidecar.

This is required by the package construction: the ZIP cannot contain its own final ZIP hash without changing that hash.

Therefore the older integration architecture sentence that suggested comparing the embedded manifest's `sha256` to the lock must not be implemented literally.

For schema v1:

### Local Addon lock

Authoritative for:

    runtime_version
    tag
    asset
    source_commit
    final ZIP sha256

### Downloaded ZIP bytes

Must match:

    clawhud.lock.json.sha256

before extraction/adoption.

### Embedded ZIP runtime-manifest.json

Must match the lock for:

    schema_version
    runtime_version
    tag
    asset
    source_commit

and, for the current producer schema, its:

    sha256

is expected to be JSON `null`.

Do not reject the valid v1.0.1 Runtime because the embedded manifest has no final ZIP hash.

Do not weaken the ZIP verification: the Addon lock SHA remains the trust anchor for the downloaded archive.

CH-A1 does not need to download the external release `runtime-manifest.json` or `.sha256` sidecar during normal runtime acquisition. They are release/provenance artifacts; the Addon already ships the exact verified identity in its lock.

---

## 7. Lock model and strict validation

Add a narrow lock model, for example:

    ClawHudRuntimeLock

Exact naming may follow repository convention.

It should deserialize the shipped JSON and validate all fields before any network access.

Required schema rules:

    schema_version == 1

    runtime_version:
      strict MAJOR.MINOR.PATCH
      digits only
      no "v"
      no prerelease suffix
      no build metadata

    tag:
      exactly "steamaddon-runtime-v" + runtime_version

    asset:
      exactly "ClawHUDRuntime.zip"

    source_commit:
      exactly 40 hexadecimal characters

    sha256:
      exactly 64 hexadecimal characters

Normalize hash/commit case only for comparison.

Do not accept alternate asset names, arbitrary URLs, or a URL supplied from the lock.

The lock is identity metadata, not a generic download descriptor.

Malformed lock must fail before HTTP is attempted.

Use the smallest reasonable implementation with `System.Text.Json`.

Do not add a generic lock-file framework.

---

## 8. Deterministic download URL

Construct the URL from reviewed constants plus the validated exact tag and asset.

Required shape:

    https://github.com/onehoon/ClawHUD/releases/download/
      steamaddon-runtime-v1.0.1/
      ClawHUDRuntime.zip

Conceptually:

    RepositoryBase = https://github.com/onehoon/ClawHUD
    URL = RepositoryBase/releases/download/{validated-tag}/{validated-asset}

Do not call GitHub release-list APIs at runtime.

Do not resolve "latest".

Do not use the ClawHUD Standalone update feed.

Do not require a GitHub token.

---

## 9. AddonDataPaths changes

Current `AddonDataPaths` already owns the canonical persistent root:

    <VeloPack root sibling>/SteamInputAddonforClaw-Data

Extend this owner rather than constructing ClawHUD paths in multiple classes.

Required conceptual helpers:

    ClawHudRuntimeRoot
      = <RootDirectory>/Runtime/ClawHUD

    ResolveClawHudRuntimeVersionDirectory(rootAppDirectory, runtimeVersion)
      = <data root>/Runtime/ClawHUD/<runtimeVersion>

Production convenience may also expose:

    ClawHudRuntimeVersionDirectory(runtimeVersion)

using the current VeloPack root.

Keep path construction centralized.

Expected v1.0.1 layout after successful acquisition:

    SteamInputAddonforClaw-Data/
      Runtime/
        ClawHUD/
          1.0.1/
            runtime-manifest.json
            ClawHUD.exe
            ClawHUD.EcHelper.exe
            PresentMonAPI2Loader.dll
            velopack_libc.dll
            LICENSE
            THIRD-PARTY-NOTICES.md
            fonts/
              Unispace.otf
              Unispace-LICENSE.txt
            runtime/
              ClawHUD.PresentMonRuntime.msi

The final version directory contains the contents of the ZIP's top-level `clawhud/` directory directly.

Do not keep an extra final:

    1.0.1/clawhud/ClawHUD.exe

layer.

Do not install under `AppContext.BaseDirectory`.

---

## 10. Runtime acquirer ownership

Introduce one narrow owner, for example:

    ClawHudRuntimeAcquirer

Its responsibilities are only:

1. read and validate the shipped lock;
2. resolve the exact final version directory;
3. validate an already-installed exact Runtime;
4. if needed, download the exact pinned ZIP;
5. verify the ZIP SHA-256;
6. validate archive entry safety;
7. extract into staging;
8. validate the embedded identity manifest;
9. validate required payload files;
10. adopt the verified payload into the final version directory;
11. return a verified executable path/result.

It must not own:

    Process.Start
    --managed
    named pipe IPC
    AppSettings
    HUD desired On/Off
    PresentMon install logic
    Full1902 controller state
    PID1901/PID1902
    HidHide
    VIIPER
    frontend state

Those belong to later PRs or existing owners.

Do not create:

    DependencyManager
    ExternalRuntimeManager
    CompanionProcessManager
    PackageCoordinator

for this one dependency.

---

## 11. Acquisition API shape

Keep the public/internal seam small enough for CH-A2 to consume later.

A reasonable result contains only information CH-A2 will need:

    success/ready
    runtime version
    runtime directory
    ClawHUD.exe path
    failure classification/message when unavailable

Exact names are implementation choice.

Do not expose staging paths or HTTP internals to callers.

Expected acquisition failures should be returned/classified locally rather than crashing the Addon process.

Programming/configuration errors may still throw where current repository conventions require it, but network, hash, archive, manifest, disk, and payload failures are feature-local outcomes.

---

## 12. HTTP implementation

Use a normal `HttpClient` seam.

Tests must be able to supply a fake `HttpMessageHandler` or equivalent narrow download delegate.

Do not add a new HTTP abstraction hierarchy.

Recommended behavior:

    GET exact pinned URL
    require success status
    stream response to a staging file
    honor CancellationToken
    dispose response/stream correctly
    compute SHA-256 from the downloaded file

Do not buffer the complete ZIP in a giant byte array solely for convenience.

The currently published ZIP is only a few MiB, but streaming is equally simple and avoids unnecessary allocation.

No live GitHub network is allowed in unit tests.

---

## 13. Staging layout and serialization

Use one feature-local `SemaphoreSlim` if needed so two realistic same-process acquisition requests cannot extract/adopt the same version simultaneously.

This is enough for the supported product model:

    one Windows user
    one interactive session
    one Addon Runtime

Do not add:

    named mutex
    cross-process installer coordinator
    epoch
    transaction service
    distributed lock
    multi-session arbitration

A reasonable staging layout is:

    Runtime/ClawHUD/
      1.0.1.staging/
        ClawHUDRuntime.zip
        extracted/
          clawhud/
            ...

The exact temporary subdirectory names may differ.

Before a new acquisition attempt, stale staging for the same version may be removed best-effort when it is clearly owned by this acquirer.

Never treat another version directory as staging.

---

## 14. ZIP SHA verification

SHA-256 verification is mandatory and happens before extraction/adoption.

For v1.0.1:

    expected:
      9e9fc07c43db90837d01de6b24388a001eae1b3da222d1ee8dbf1dabcf0383f5

Process:

    download complete
    -> compute SHA256
    -> compare exact value, case-insensitive
    -> mismatch:
         do not extract/adopt
         classify verification failure
         delete staging best-effort
         leave all existing installed Runtime versions untouched

Do not substitute the GitHub API asset digest for runtime verification.

The Addon-shipped lock is authoritative.

---

## 15. Safe ZIP extraction

Do not blindly trust archive entry paths.

Before writing each entry:

- reject rooted/absolute paths;
- reject drive-qualified paths;
- resolve the destination with `Path.GetFullPath`;
- require the resolved destination to remain inside the extraction root;
- reject traversal outside staging;
- create directories only inside staging.

The ZIP produced by CH-I3 has exactly one top-level payload directory:

    clawhud/

Reject an archive that does not provide the expected `clawhud/` payload root.

Do not extract directly into the final version directory.

All validation happens in staging first.

This is a real package-boundary safety check, not a theoretical concurrency defense.

---

## 16. Embedded Runtime manifest validation

After extraction, read:

    staging/.../clawhud/runtime-manifest.json

For schema v1 require:

    schema_version == lock.schema_version
    runtime_version == lock.runtime_version
    tag == lock.tag
    asset == lock.asset
    source_commit == lock.source_commit
    sha256 == null

Reject:

    missing manifest
    malformed JSON
    unsupported schema
    wrong runtime version
    wrong tag
    wrong source commit
    wrong asset
    non-null/unexpected schema-v1 embedded SHA value if it conflicts with the producer contract

Do not "repair" a mismatched manifest.

Do not use the embedded manifest to override the Addon lock.

---

## 17. Required Runtime payload validation

The current ClawHUD producer contract requires:

    ClawHUD.exe
    ClawHUD.EcHelper.exe
    PresentMonAPI2Loader.dll
    velopack_libc.dll
    LICENSE
    THIRD-PARTY-NOTICES.md
    fonts/Unispace.otf
    fonts/Unispace-LICENSE.txt
    runtime/ClawHUD.PresentMonRuntime.msi

CH-I3 additionally creates:

    runtime-manifest.json

All of the above must exist as files before staging can be adopted.

For schema v1 also reject accidental Standalone/package payloads if present at the known producer paths:

    ClawHUD.Settings.exe
    ClawHUD.Settings.dll
    ClawHUD.Settings.deps.json
    ClawHUD.Settings.runtimeconfig.json
    ClawHUD.Diag.exe
    Setup.exe
    releases.stable.json

Do not add recursive "allow-list every file" validation. The immutable ZIP hash already fixes the exact bytes, and a full generic payload policy would add complexity without a product need.

Required/known-forbidden checks are enough.

---

## 18. Existing-installed fast path

If:

    <RuntimeRoot>/<exact version>/

already exists, do not download immediately.

Validate:

    embedded runtime-manifest identity
    required payload files

If valid:

    return exact ClawHUD.exe path
    no HTTP
    no extraction

Do not hash every extracted file on every startup.

The install-time ZIP hash is the package trust boundary.

Same-user arbitrary post-install tampering is not a new adversarial boundary for this product.

If the exact installed directory is incomplete or identity-mismatched:

    classify it invalid
    perform a fresh acquisition on this admitted acquisition request

but do not touch other valid Runtime versions.

---

## 19. Adoption rules

Only adopt after all staging checks pass.

For a new version directory:

    verified staging/clawhud
    -> Directory.Move to exact final version directory

Do not copy individual files one-by-one into a live final directory.

If an invalid final directory for the exact same version already exists:

1. finish downloading/extracting/verifying the replacement in staging first;
2. only after replacement is fully valid, remove/replace the invalid exact-version directory;
3. move the verified staging payload into the canonical final directory.

Do not delete the final directory before the replacement has passed all checks.

Do not mutate older different-version directories during the critical acquisition path.

Aggressive old-version cleanup is not required in CH-A1.

---

## 20. Failure cleanup

On any failed acquisition attempt:

    remove owned staging residue best-effort
    keep other version directories
    return feature-local failure

Examples:

    HTTP 404/5xx
    timeout/cancellation
    disk failure
    SHA mismatch
    malformed ZIP
    traversal entry
    missing clawhud/ root
    embedded manifest mismatch
    required file missing
    final move failure

Cleanup failure should be logged but must not replace the original acquisition failure classification.

Do not touch Full1902 state as part of cleanup.

---

## 21. Logging

Use existing `AppLog`.

Recommended category:

    ClawHUD.Runtime

or, if useful without duplication:

    ClawHUD.Download

Useful events:

    RuntimeLockLoaded
    RuntimeFastPathValidated
    RuntimeDownloadStarted
    RuntimeDownloadCompleted
    RuntimeHashVerified
    RuntimeStagingValidated
    RuntimeAdopted
    RuntimeAcquisitionFailed

Useful fields:

    RuntimeVersion
    Tag
    SourceCommit
    Path
    ElapsedMs
    Reason

Do not log ZIP contents or full binary buffers.

Do not add high-frequency logging; acquisition is infrequent.

---

## 22. Project packaging changes

Update:

    src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj

so:

    Dependencies/ClawHUD/clawhud.lock.json

is copied to both:

    output
    publish

using the same content pattern already used for dependency metadata.

Expected published path:

    Dependencies/
      ClawHUD/
        clawhud.lock.json

Only the lock is packaged.

No ClawHUD Runtime binaries are bundled.

---

## 23. Publish verification

Update:

    scripts/verify-publish-assets.ps1

Required additions:

1. `Dependencies\ClawHUD\clawhud.lock.json` is a required publish asset.
2. Parse the published lock.
3. Verify:
   - schema_version == 1;
   - runtime_version is strict MAJOR.MINOR.PATCH;
   - tag exactly matches `steamaddon-runtime-v<runtime_version>`;
   - asset exactly `ClawHUDRuntime.zip`;
   - source_commit is 40 hex;
   - sha256 is 64 hex.
4. Reject accidental bundled ClawHUD Runtime payload under the dependency directory, at minimum:
   - `Dependencies\ClawHUD\ClawHUDRuntime.zip`;
   - `Dependencies\ClawHUD\ClawHUD.exe`.

Do not make the publish verifier contact GitHub.

Do not download the pinned Runtime during normal Addon publish verification.

---

## 24. Tests — lock contract

Add focused tests, for example:

    ClawHudRuntimeLockTests.cs

Cover at least:

- valid v1.0.1 lock accepted;
- unsupported schema rejected;
- missing field rejected;
- malformed semantic version rejected;
- prefixed/suffixed version rejected;
- tag/version mismatch rejected;
- wrong asset rejected;
- source commit not exactly 40 hex rejected;
- SHA not exactly 64 hex rejected;
- deterministic URL uses exact validated tag/asset;
- malformed lock performs no download when exercised through the acquirer.

Do not test generic JSON serializer behavior.

Test the product contract.

---

## 25. Tests — AddonDataPaths

Extend:

    AddonDataPathsTests.cs

Prove:

    ClawHudRuntimeRoot
      == <canonical data root>\Runtime\ClawHUD

and:

    ResolveClawHudRuntimeVersionDirectory(..., "1.0.1")
      == <canonical data root>\Runtime\ClawHUD\1.0.1

Also prove it remains outside the VeloPack install root.

Do not create a second LocalAppData authority.

---

## 26. Tests — acquisition

Add:

    ClawHudRuntimeAcquirerTests.cs

Use:

    temporary directories
    generated ZIP fixtures
    fake HttpMessageHandler / narrow download seam

Do not use live GitHub.

Required scenarios:

### Fast path

- valid already-installed exact Runtime returns Ready;
- no HTTP request is made;
- executable path is exact version directory / ClawHUD.exe.

### Lock / download

- exact URL is requested;
- HTTP failure is feature-local;
- cancellation is honored;
- download does not touch final directory before validation.

### SHA

- matching SHA proceeds;
- mismatched SHA is rejected before extraction/adoption;
- pre-existing other version remains untouched.

### Archive safety

- absolute entry rejected;
- traversal entry such as `../escape` rejected;
- missing top-level `clawhud/` rejected;
- extraction never writes outside staging.

### Embedded manifest

- matching schema-v1 identity accepted with `sha256: null`;
- wrong runtime version rejected;
- wrong tag rejected;
- wrong source commit rejected;
- wrong asset rejected;
- malformed/missing manifest rejected.

### Payload

- each required file absence causes invalid payload;
- known forbidden Standalone payload is rejected.

### Adoption

- successful verified staging becomes:
  `Runtime/ClawHUD/1.0.1/ClawHUD.exe`;
- staging residue is removed after success;
- failed staging validation leaves a previous valid Runtime version untouched;
- invalid exact-version directory is not removed until replacement staging has fully validated.

Do not require process launch.

---

## 27. Publish contract test

Use the repository's existing script-test style if practical, or cover through the existing release/publish validation path.

At minimum the PR must demonstrate:

    dotnet publish
      -> Dependencies/ClawHUD/clawhud.lock.json exists

and:

    ClawHUDRuntime.zip absent
    ClawHUD.exe absent from Dependencies/ClawHUD

Do not add binary fixtures to the repository to test this.

---

## 28. Explicitly out of scope

Do not implement any of these in CH-A1:

- `ClawHudEnabled` AppSettings field;
- ClawHUD process launch;
- `ClawHUD.exe --managed`;
- process ownership/adoption;
- Managed startup exit-code handling;
- single-instance classification;
- Control IPC v1;
- GetRuntimeInfo;
- GetSettingsSnapshot;
- SetHudEnabled;
- RequestShutdown;
- PresentMon setup handling;
- Main UI HUD card;
- Overlay HUD controls;
- ClawHUD dependency update automation;
- runtime crash restart;
- sleep/resume HUD coordination;
- ClawHUD Standalone conflict handling;
- old Runtime aggressive cleanup;
- generic companion/dependency framework;
- changes to PID1901/PID1902;
- changes to HidHide;
- changes to VIIPER;
- changes to controller startup sequencing.

Those belong to CH-A2+ or existing Full1902 owners.

---

## 29. No startup integration in this PR

This is deliberate.

Do not modify:

    AddonProcessHost

to automatically call the new acquirer.

Do not modify:

    StartupCoordinator
    Full1902 startup
    deferred startup tail

to download ClawHUD.

CH-A1 ships the dependency pin and implements/tests the acquisition unit, but production process-lifetime composition begins in CH-A2.

This keeps CH-A1 independently reviewable and proves that the package boundary works before any optional process is started.

---

## 30. Full1902 regression requirement

Because CH-A1 is dormant until explicitly called in tests/future code, existing Full1902 behavior should be byte-for-byte/behaviorally unaffected except for the additional shipped JSON lock.

Review as blocking if CH-A1 changes any of:

    controller authority admission
    Center M Enabled/Disabled classification
    mandatory startup task
    PID switching
    DirectInput ownership
    HidHide normalization
    PnP recovery
    VIIPER lifecycle
    X360/SteamDeck switching
    routing rollback/fail-close

There is no valid CH-A1 reason to modify those paths.

---

## 31. Suggested production files

Expected shape; exact names may follow current conventions:

    src/SteamInputAddonforClaw/
      Dependencies/
        ClawHUD/
          clawhud.lock.json

      ClawHud/
        ClawHudRuntimeLock.cs
        ClawHudRuntimeAcquirer.cs

      Install/
        AddonDataPaths.cs

      SteamInputAddonforClaw.csproj

    scripts/
      verify-publish-assets.ps1

Tests:

    tests/SteamInputAddonforClaw.Tests/
      ClawHudRuntimeLockTests.cs
      ClawHudRuntimeAcquirerTests.cs
      AddonDataPathsTests.cs

Do not create additional interfaces/classes unless concrete testing or ownership requires them.

Prefer constructor injection of `HttpClient` over introducing `IClawHudDownloader` solely for mocking.

---

## 32. Implementation guidance — keep it simple

This project supports:

    one Windows user
    one interactive session

Do not defend theoretical unsupported multi-process races.

A single in-process acquisition gate is enough.

Do not add synchronization solely because an artificial test can interleave individual filesystem operations.

Real cases that do matter:

    network disconnect
    process cancellation
    disk write failure
    corrupt/incomplete ZIP
    wrong release bytes
    interrupted previous staging
    invalid exact-version local Runtime

Design for those.

---

## 33. Validation commands

Run the repository's normal build/test commands for current `main`.

At minimum:

~~~powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release
~~~

Also perform a Release publish using the same path used by repository CI/release and run:

~~~powershell
.\scripts\verify-publish-assets.ps1 -PublishDirectory <publish-directory>
~~~

Verify manually from publish output:

    Dependencies\ClawHUD\clawhud.lock.json
      exists

and no downloaded Runtime payload is packaged.

Do not require live ClawHUD download as part of normal unit/CI test execution.

A separate local/manual smoke may call the real acquirer against v1.0.1, but it is not a required CI dependency.

---

## 34. Review checklist

A reviewer should verify:

- PR base is current `main`;
- exact Runtime pin is v1.0.1 with the values in §4;
- lock is shipped with Addon;
- no ClawHUD binary is vendored;
- no mutable/latest URL resolution exists;
- malformed lock fails before network;
- exact ZIP hash is verified before extraction;
- ZIP traversal is blocked;
- embedded schema-v1 manifest correctly accepts `sha256:null`;
- embedded version/tag/source/asset must match the lock;
- required payload is checked;
- staging is validated before final adoption;
- existing valid exact Runtime uses no network;
- failures are feature-local;
- no process starts;
- no AppSettings/UI/IPC is added;
- no Full1902 owner is modified;
- tests do not depend on GitHub availability.

Do not block this PR for speculative multi-session or instruction-level race hardening.

---

## 35. Acceptance criteria

CH-A1 is complete when all are true:

1. SteamAddon `main` ships a strict `clawhud.lock.json`.
2. The initial pin is exactly ClawHUD Runtime v1.0.1.
3. Only metadata is bundled; no ClawHUD Runtime binary is part of the Addon package.
4. AddonDataPaths owns the versioned ClawHUD Runtime data path.
5. The acquirer constructs only the exact immutable release URL.
6. Downloaded ZIP bytes must match the Addon-shipped SHA-256.
7. Unsafe ZIP paths cannot escape staging.
8. Embedded manifest identity must match the lock, with schema-v1 embedded `sha256:null` handled correctly.
9. Every current required Runtime file must exist before adoption.
10. Verified Runtime is adopted under:
       `SteamInputAddonforClaw-Data/Runtime/ClawHUD/1.0.1`
11. A valid existing exact Runtime takes the fast path without network.
12. Acquisition failure leaves controller authority unchanged.
13. No ClawHUD process can be launched by CH-A1.
14. Full existing test suite passes.
15. Release publish validation passes and verifies the lock is present.

---

## 36. Suggested PR title

    CH-A1: add pinned ClawHUD Runtime acquisition

Suggested PR summary:

    - pin immutable ClawHUD SteamAddon Runtime v1.0.1
    - add SHA-verified safe staged Runtime acquisition
    - install Runtime under the canonical Addon data root
    - ship metadata only; do not launch ClawHUD yet

---

## 37. Handoff to CH-A2

Do not implement CH-A2 here.

After CH-A1 is merged and the real v1.0.1 acquisition is hardware/local-machine validated, CH-A2 will consume the verified executable path and add:

    Addon ClawHudEnabled
    -> Ensure Runtime
    -> ClawHUD.exe --managed
    -> bounded Control IPC readiness
    -> exact Managed/version/protocol classification
    -> HUD enable convergence
    -> RequestShutdown on Addon HUD Off / controlled shutdown

The CH-A1 acquisition contract should therefore return the exact verified Runtime directory/executable path cleanly, but should not predict or absorb CH-A2 process ownership.
