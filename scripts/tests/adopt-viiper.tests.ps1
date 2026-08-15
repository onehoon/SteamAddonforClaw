$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\adopt-viiper.ps1'
$realViiperDir = Join-Path $repoRoot 'src\SteamInputAddonforClaw\Dependencies\Viiper'

# Dot-source to load the pure/testable functions. The script requires
# -StagingDirectory/-ExpectedCommit even when dot-sourced (mandatory
# parameter binding happens before the main-guard runs), but the main-guard
# returns before doing anything with them.
. $scriptPath -StagingDirectory 'unused-during-dot-source' -ExpectedCommit ('0' * 40)

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function Assert-Throws {
    param([Parameter(Mandatory)] [scriptblock] $ScriptBlock, [Parameter(Mandatory)] [string] $Case)
    $threw = $false
    try {
        & $ScriptBlock
    }
    catch {
        $threw = $true
    }
    if (-not $threw) {
        throw "Expected '$Case' to throw, but it did not."
    }
}

$currentCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$targetCommit = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$utf8Sentinel = "This is an em dash $([char]0x2014) and Korean text $([char]0xD55C)$([char]0xAE00)."

function New-ValidManifest {
    param([string] $Commit = $targetCommit, [string] $DllSha256, [string] $HeaderSha256)
    return [pscustomobject]@{
        schema_version   = 1
        repository       = 'onehoon/VIIPER'
        commit           = $Commit
        build_entrypoint = 'just build-libVIIPER Release'
        platform         = 'windows'
        architecture     = 'amd64'
        dll              = [pscustomobject]@{ file = 'libVIIPER.dll'; sha256 = $DllSha256 }
        header           = [pscustomobject]@{ file = 'libVIIPER.h'; sha256 = $HeaderSha256 }
    }
}

# --- fixture repo builder ---------------------------------------------

function New-FixtureRepo {
    <#
    Builds a minimal fixture repo tree mirroring the real one closely enough
    for adopt-viiper.ps1 to operate on: the vendored dependency directory
    (lock/provenance/DLL/header) plus the docs/notices/runtime-inspector/
    publish-gate files it mechanically edits, all seeded with $currentCommit
    identity and the REAL vendored DLL/header bytes (so hash math is
    meaningful) copied under that identity.
    #>
    param([string] $Commit = $currentCommit)

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("adopt-viiper-test-" + [System.Guid]::NewGuid())
    $viiperDir = Join-Path $root 'src\SteamInputAddonforClaw\Dependencies\Viiper'
    $docsDir = Join-Path $root 'docs'
    $prereqDir = Join-Path $root 'src\SteamInputAddonforClaw\Prerequisites'
    $scriptsDir = Join-Path $root 'scripts'
    New-Item -ItemType Directory -Force -Path $viiperDir, $docsDir, $prereqDir, $scriptsDir | Out-Null

    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.dll') -Destination $viiperDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.h') -Destination $viiperDir

    $dllHash = (Get-FileHash -LiteralPath (Join-Path $viiperDir 'libVIIPER.dll') -Algorithm SHA256).Hash
    $headerHash = (Get-FileHash -LiteralPath (Join-Path $viiperDir 'libVIIPER.h') -Algorithm SHA256).Hash

    # NOTE: PowerShell double-quoted here-strings (@"..."@) treat backtick as an escape
    # character (e.g. `t is literally a TAB), so markdown code fences/spans containing literal
    # backticks are built as plain variables first (via single-quoted string concatenation, which
    # does no escape processing) and only then interpolated -- never written as raw backticks
    # inside an @"..."@ block.
    $backtick = [char]0x60
    $integrationRevisionLine = "| Embedded VIIPER revision | $backtick$Commit$backtick |"
    $integrationHistoricalLine = "not to be touched: Phase X adopted ${backtick}0000000000000000000000000000000000000000$backtick."
    $noticesBaselineLine = "- Source baseline: pinned commit $backtick$Commit$backtick"

    @"
{
  "schema_version": 1,
  "repository": "onehoon/VIIPER",
  "commit": "$Commit",
  "build_entrypoint": "just build-libVIIPER Release",
  "platform": "windows",
  "architecture": "amd64",
  "dll": {
    "file": "libVIIPER.dll",
    "sha256": "$dllHash"
  },
  "header": {
    "file": "libVIIPER.h",
    "sha256": "$headerHash"
  }
}
"@ | Set-Content -LiteralPath (Join-Path $viiperDir 'viiper.lock.json') -NoNewline

    @"
# libVIIPER.dll provenance

## Embedded artifact

Repository: onehoon/VIIPER
Commit:     $Commit
Branch:     main
Entrypoint: just build-libVIIPER Release

## Build attestation

Generated header SHA-256: $($headerHash.ToLowerInvariant())
DLL SHA-256:              $($dllHash.ToLowerInvariant())

<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

Some previous revision's stale ABI notes that must not survive adoption.
<!-- AUTOMATION: END MANAGED ABI REVIEW SECTION -->

## Addon integration alignment
"@ | Set-Content -LiteralPath (Join-Path $viiperDir 'PROVENANCE.md') -NoNewline

    $integrationText = @"
# VIIPER Integration Contract

| Item | Current contract |
| --- | --- |
$integrationRevisionLine

Some other paragraph that historically mentions a different commit and must
$integrationHistoricalLine
$utf8Sentinel
"@
    $noBomUtf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $docsDir 'VIIPER_INTEGRATION.md'), $integrationText, $noBomUtf8)

    @"
# Steam Deck Runtime Roadmap

onehoon/VIIPER@$Commit
"@ | Set-Content -LiteralPath (Join-Path $docsDir 'VIIPER_MIGRATION_TODO.md') -NoNewline

    @"
### VIIPER

- Project: VIIPER
- Canonical source: https://github.com/onehoon/VIIPER/tree/$Commit
$noticesBaselineLine
- License: GPL-3.0
"@ | Set-Content -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -NoNewline

    @"
internal sealed class ViiperRuntimeInspector
{
    internal const string ExpectedPayloadSha256 = "$dllHash";
}
"@ | Set-Content -LiteralPath (Join-Path $prereqDir 'ViiperRuntimeInspector.cs') -NoNewline

    @"
`$expectedViiperSha256 = '$dllHash'
"@ | Set-Content -LiteralPath (Join-Path $scriptsDir 'verify-publish-assets.ps1') -NoNewline

    return [pscustomobject]@{
        Root          = $root
        ViiperDir     = $viiperDir
        DllHash       = $dllHash
        HeaderHash    = $headerHash
    }
}

function New-StagingDirectory {
    param(
        [Parameter(Mandatory)] [string] $Commit,
        [string] $DllSourcePath = (Join-Path $realViiperDir 'libVIIPER.dll'),
        [string] $HeaderSourcePath = (Join-Path $realViiperDir 'libVIIPER.h'),
        [switch] $OmitManifest,
        [switch] $OmitDll,
        [switch] $OmitHeader,
        [string] $ManifestCommitOverride,
        [string] $ManifestDllHashOverride,
        [string] $ManifestHeaderHashOverride
    )

    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("adopt-viiper-staging-" + [System.Guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    if (-not $OmitDll) { Copy-Item -LiteralPath $DllSourcePath -Destination (Join-Path $dir 'libVIIPER.dll') }
    if (-not $OmitHeader) { Copy-Item -LiteralPath $HeaderSourcePath -Destination (Join-Path $dir 'libVIIPER.h') }

    $dllHash = if ($OmitDll) { '1' * 64 } else { (Get-FileHash -LiteralPath (Join-Path $dir 'libVIIPER.dll') -Algorithm SHA256).Hash }
    $headerHash = if ($OmitHeader) { '2' * 64 } else { (Get-FileHash -LiteralPath (Join-Path $dir 'libVIIPER.h') -Algorithm SHA256).Hash }

    if (-not $OmitManifest) {
        $manifest = New-ValidManifest -Commit ($(if ($ManifestCommitOverride) { $ManifestCommitOverride } else { $Commit })) `
            -DllSha256 ($(if ($ManifestDllHashOverride) { $ManifestDllHashOverride } else { $dllHash })) `
            -HeaderSha256 ($(if ($ManifestHeaderHashOverride) { $ManifestHeaderHashOverride } else { $headerHash }))
        ($manifest | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $dir 'viiper-artifact.json')
    }

    return $dir
}

function Invoke-Adopt {
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $StagingDirectory,
        [string] $ExpectedCommit = $targetCommit
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -StagingDirectory $StagingDirectory -ExpectedCommit $ExpectedCommit -RepoRoot $RepoRoot 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = ($output | Out-String) }
}

function Assert-Success {
    param($Result, [string] $Case)
    if ($Result.ExitCode -ne 0) { throw "Expected '$Case' to succeed, but it failed with output:`n$($Result.Output)" }
}

function Assert-Failure {
    param($Result, [string] $Case)
    if ($Result.ExitCode -eq 0) { throw "Expected '$Case' to fail, but it succeeded with output:`n$($Result.Output)" }
}

$fixturesToClean = @()

try {
    # --- pure eligibility classification ------------------------------

    Assert-Equal -Expected 'NoOp' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $currentCommit -CompareStatus 'ahead') -Message 'Same commit not treated as no-op.'
    Assert-Equal -Expected 'Eligible' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $targetCommit -CompareStatus 'ahead') -Message 'Forward revision not treated as eligible.'
    Assert-Equal -Expected 'NoOp' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $targetCommit -CompareStatus 'identical') -Message 'Identical status not treated as no-op.'
    Assert-Equal -Expected 'Downgrade' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $targetCommit -CompareStatus 'behind') -Message 'Behind status not treated as downgrade.'
    Assert-Equal -Expected 'FailClosed' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $targetCommit -CompareStatus 'diverged') -Message 'Diverged status not failed closed.'
    Assert-Equal -Expected 'FailClosed' -Actual (Test-ViiperUpgradeEligibility -CurrentCommit $currentCommit -TargetCommit $targetCommit -CompareStatus $null) -Message 'Unknown status not failed closed.'

    # --- exact valid adoption end-to-end -------------------------------

    $fixture = New-FixtureRepo
    $fixturesToClean += $fixture.Root
    $staging = New-StagingDirectory -Commit $targetCommit
    $fixturesToClean += $staging
    $result = Invoke-Adopt -RepoRoot $fixture.Root -StagingDirectory $staging
    Assert-Success -Result $result -Case 'exact valid adoption'

    $newLock = Get-Content -LiteralPath (Join-Path $fixture.ViiperDir 'viiper.lock.json') -Raw | ConvertFrom-Json
    Assert-Equal -Expected $targetCommit -Actual $newLock.commit -Message 'Lock commit not updated.'
    $stagedDllHash = (Get-FileHash -LiteralPath (Join-Path $staging 'libVIIPER.dll') -Algorithm SHA256).Hash
    $vendoredDllHash = (Get-FileHash -LiteralPath (Join-Path $fixture.ViiperDir 'libVIIPER.dll') -Algorithm SHA256).Hash
    Assert-Equal -Expected $stagedDllHash -Actual $vendoredDllHash -Message 'Vendored DLL bytes do not match staged payload after adoption.'

    $provenance = Get-Content -LiteralPath (Join-Path $fixture.ViiperDir 'PROVENANCE.md') -Raw
    if ($provenance -notmatch [regex]::Escape("Commit:     $targetCommit")) { throw 'PROVENANCE.md commit not updated.' }
    if ($provenance -match 'Some previous revision''s stale ABI notes') { throw 'Stale ABI review content survived adoption.' }
    if ($provenance -notmatch 'ABI compatibility is not inferred') { throw 'ABI review placeholder not reset.' }

    $integrationDoc = [System.IO.File]::ReadAllText((Join-Path $fixture.Root 'docs\VIIPER_INTEGRATION.md'), [System.Text.Encoding]::UTF8)
    if ($integrationDoc -notmatch [regex]::Escape("| Embedded VIIPER revision | ``$targetCommit`` |")) { throw 'VIIPER_INTEGRATION.md pin not updated.' }
    if ($integrationDoc -notmatch [regex]::Escape('0000000000000000000000000000000000000000')) { throw 'Unrelated historical commit mention was touched.' }
    if ($integrationDoc -notmatch [regex]::Escape($utf8Sentinel)) { throw 'BOM-less UTF-8 non-ASCII text was not preserved by PowerShell 5.1 adoption.' }

    $migrationDoc = Get-Content -LiteralPath (Join-Path $fixture.Root 'docs\VIIPER_MIGRATION_TODO.md') -Raw
    if ($migrationDoc -notmatch [regex]::Escape("onehoon/VIIPER@$targetCommit")) { throw 'VIIPER_MIGRATION_TODO.md pin not updated.' }

    $notices = Get-Content -LiteralPath (Join-Path $fixture.Root 'THIRD_PARTY_NOTICES.md') -Raw
    if ($notices -notmatch [regex]::Escape($targetCommit)) { throw 'THIRD_PARTY_NOTICES.md source pin not updated.' }
    if ($notices -match [regex]::Escape($currentCommit)) { throw 'THIRD_PARTY_NOTICES.md still references the old commit.' }

    $inspector = Get-Content -LiteralPath (Join-Path $fixture.Root 'src\SteamInputAddonforClaw\Prerequisites\ViiperRuntimeInspector.cs') -Raw
    if ($inspector -notmatch [regex]::Escape($stagedDllHash)) { throw 'ViiperRuntimeInspector.ExpectedPayloadSha256 not updated.' }

    $publishGate = Get-Content -LiteralPath (Join-Path $fixture.Root 'scripts\verify-publish-assets.ps1') -Raw
    if ($publishGate -notmatch [regex]::Escape($stagedDllHash)) { throw 'verify-publish-assets.ps1 expected hash not updated.' }

    # --- current == target: no-op --------------------------------------

    $noopFixture = New-FixtureRepo -Commit $targetCommit
    $fixturesToClean += $noopFixture.Root
    $noopStaging = New-StagingDirectory -Commit $targetCommit
    $fixturesToClean += $noopStaging
    $beforeLock = Get-Content -LiteralPath (Join-Path $noopFixture.ViiperDir 'viiper.lock.json') -Raw
    $result = Invoke-Adopt -RepoRoot $noopFixture.Root -StagingDirectory $noopStaging
    Assert-Success -Result $result -Case 'current == target no-op'
    $afterLock = Get-Content -LiteralPath (Join-Path $noopFixture.ViiperDir 'viiper.lock.json') -Raw
    Assert-Equal -Expected $beforeLock -Actual $afterLock -Message 'No-op adoption modified the lock file.'

    # --- malformed target SHA rejected (via manifest) -------------------

    $fixture2 = New-FixtureRepo
    $fixturesToClean += $fixture2.Root
    $badShaStaging = New-StagingDirectory -Commit $targetCommit -ManifestCommitOverride 'not-a-commit'
    $fixturesToClean += $badShaStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture2.Root -StagingDirectory $badShaStaging) -Case 'malformed target SHA'

    # --- missing manifest rejected ---------------------------------------

    $fixture3 = New-FixtureRepo
    $fixturesToClean += $fixture3.Root
    $noManifestStaging = New-StagingDirectory -Commit $targetCommit -OmitManifest
    $fixturesToClean += $noManifestStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture3.Root -StagingDirectory $noManifestStaging) -Case 'missing manifest'

    # --- missing DLL/header rejected --------------------------------------

    $fixture4 = New-FixtureRepo
    $fixturesToClean += $fixture4.Root
    $noDllStaging = New-StagingDirectory -Commit $targetCommit -OmitDll
    $fixturesToClean += $noDllStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture4.Root -StagingDirectory $noDllStaging) -Case 'missing staged DLL'

    $fixture5 = New-FixtureRepo
    $fixturesToClean += $fixture5.Root
    $noHeaderStaging = New-StagingDirectory -Commit $targetCommit -OmitHeader
    $fixturesToClean += $noHeaderStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture5.Root -StagingDirectory $noHeaderStaging) -Case 'missing staged header'

    # --- manifest commit mismatch / DLL hash mismatch / header hash mismatch rejected ---

    $fixture6 = New-FixtureRepo
    $fixturesToClean += $fixture6.Root
    $wrongDllHashStaging = New-StagingDirectory -Commit $targetCommit -ManifestDllHashOverride ('0' * 64)
    $fixturesToClean += $wrongDllHashStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture6.Root -StagingDirectory $wrongDllHashStaging) -Case 'DLL hash mismatch'

    $fixture7 = New-FixtureRepo
    $fixturesToClean += $fixture7.Root
    $wrongHeaderHashStaging = New-StagingDirectory -Commit $targetCommit -ManifestHeaderHashOverride ('0' * 64)
    $fixturesToClean += $wrongHeaderHashStaging
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture7.Root -StagingDirectory $wrongHeaderHashStaging) -Case 'header hash mismatch'

    # --- manifest commit mismatch rejected (staged payload is for a different commit than requested) ---

    $fixture7b = New-FixtureRepo
    $fixturesToClean += $fixture7b.Root
    $unexpectedCommitStaging = New-StagingDirectory -Commit $targetCommit
    $fixturesToClean += $unexpectedCommitStaging
    $wrongExpectedCommit = 'cccccccccccccccccccccccccccccccccccccccc'
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture7b.Root -StagingDirectory $unexpectedCommitStaging -ExpectedCommit $wrongExpectedCommit) -Case 'manifest commit does not match caller-expected commit'

    # --- expected-current-value mismatch / replacement cardinality mismatch fail closed ---
    # Simulated by corrupting the fixture's PROVENANCE.md commit line so it no longer matches
    # what the lock file (and thus the script's expected "current value") says.

    $fixture8 = New-FixtureRepo
    $fixturesToClean += $fixture8.Root
    (Get-Content -LiteralPath (Join-Path $fixture8.ViiperDir 'PROVENANCE.md') -Raw) `
        -replace [regex]::Escape("Commit:     $currentCommit"), 'Commit:     cccccccccccccccccccccccccccccccccccccccc' |
        Set-Content -LiteralPath (Join-Path $fixture8.ViiperDir 'PROVENANCE.md') -NoNewline
    $staging8 = New-StagingDirectory -Commit $targetCommit
    $fixturesToClean += $staging8
    Assert-Failure -Result (Invoke-Adopt -RepoRoot $fixture8.Root -StagingDirectory $staging8) -Case 'expected-current-value mismatch (PROVENANCE.md drifted from lock)'

    Write-Host 'adopt-viiper.ps1 tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
