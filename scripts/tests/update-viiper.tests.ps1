$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repoRoot 'scripts\update-viiper.ps1'
$realViiperDir = Join-Path $repoRoot 'src\SteamInputAddonforClaw\Dependencies\Viiper'

# Dot-source to load the pure/testable functions. The script requires a
# well-formed -Commit even when dot-sourced (mandatory parameter binding
# happens before the main-guard runs), but the main-guard returns before
# doing anything with it.
. $scriptPath -Commit ('0' * 40)

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

function New-RunRecord {
    param(
        [string] $Id = '1',
        [string] $Event = 'push',
        [string] $Branch = 'main',
        [string] $HeadSha,
        [string] $Conclusion = 'success',
        [string] $CreatedAt = '2026-01-01T00:00:00Z'
    )
    return [pscustomobject]@{
        id          = [long]$Id
        event       = $Event
        head_branch = $Branch
        head_sha    = $HeadSha
        conclusion  = $Conclusion
        created_at  = $CreatedAt
    }
}

function New-ArtifactRecord {
    param(
        [string] $Name = 'libVIIPER-windows-amd64-Snapshot',
        [bool] $Expired = $false,
        [string] $Id = '1'
    )
    return [pscustomobject]@{
        id      = [long]$Id
        name    = $Name
        expired = $Expired
    }
}

$validCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'

function New-ValidManifest {
    param([string] $Commit = $validCommit)
    return [pscustomobject]@{
        schema_version   = 1
        repository       = 'onehoon/VIIPER'
        commit           = $Commit
        build_entrypoint = 'just build-libVIIPER Release'
        platform         = 'windows'
        architecture     = 'amd64'
        dll              = [pscustomobject]@{ file = 'libVIIPER.dll'; sha256 = '1' * 64 }
        header           = [pscustomobject]@{ file = 'libVIIPER.h'; sha256 = '2' * 64 }
    }
}

$fixturesToClean = @()

try {
    # --- commit format ------------------------------------------------
    Assert-Equal -Expected $true -Actual (Test-ViiperCommitFormat -Commit $validCommit) -Message 'Valid 40-hex commit rejected.'
    Assert-Equal -Expected $false -Actual (Test-ViiperCommitFormat -Commit 'ec64282') -Message 'Short commit accepted.'
    Assert-Equal -Expected $false -Actual (Test-ViiperCommitFormat -Commit 'main') -Message 'Branch name accepted as commit.'
    Assert-Equal -Expected $false -Actual (Test-ViiperCommitFormat -Commit ('g' * 40)) -Message 'Non-hex commit accepted.'

    # --- run eligibility ------------------------------------------------

    # PR/non-main/non-push run is not eligible.
    $prRun = New-RunRecord -Id '1' -Event 'pull_request' -HeadSha $validCommit
    Assert-Equal -Expected 0 -Actual @(Select-EligibleWorkflowRuns -Runs @($prRun) -Commit $validCommit).Count -Message 'pull_request run treated as eligible.'

    $nonMainRun = New-RunRecord -Id '2' -Branch 'feature/x' -HeadSha $validCommit
    Assert-Equal -Expected 0 -Actual @(Select-EligibleWorkflowRuns -Runs @($nonMainRun) -Commit $validCommit).Count -Message 'non-main branch run treated as eligible.'

    # run SHA mismatch is rejected.
    $wrongShaRun = New-RunRecord -Id '3' -HeadSha ('b' * 40)
    Assert-Equal -Expected 0 -Actual @(Select-EligibleWorkflowRuns -Runs @($wrongShaRun) -Commit $validCommit).Count -Message 'wrong head_sha run treated as eligible.'

    # unsuccessful run is rejected.
    $failedRun = New-RunRecord -Id '4' -HeadSha $validCommit -Conclusion 'failure'
    Assert-Equal -Expected 0 -Actual @(Select-EligibleWorkflowRuns -Runs @($failedRun) -Commit $validCommit).Count -Message 'failed run treated as eligible.'

    # valid eligible run accepted, and deterministic ordering (created_at desc, then id desc).
    $olderRun = New-RunRecord -Id '10' -HeadSha $validCommit -CreatedAt '2026-01-01T00:00:00Z'
    $newerRun = New-RunRecord -Id '11' -HeadSha $validCommit -CreatedAt '2026-01-02T00:00:00Z'
    $tieRunLowerId = New-RunRecord -Id '12' -HeadSha $validCommit -CreatedAt '2026-01-02T00:00:00Z'
    $tieRunHigherId = New-RunRecord -Id '13' -HeadSha $validCommit -CreatedAt '2026-01-02T00:00:00Z'
    $ordered = @(Select-EligibleWorkflowRuns -Runs @($olderRun, $newerRun, $tieRunLowerId, $tieRunHigherId) -Commit $validCommit)
    Assert-Equal -Expected 4 -Actual $ordered.Count -Message 'Eligible run count mismatch.'
    Assert-Equal -Expected 13 -Actual $ordered[0].id -Message 'Deterministic ordering: newest created_at, tie broken by highest run ID, expected first.'
    Assert-Equal -Expected 12 -Actual $ordered[1].id -Message 'Deterministic ordering: tie-break second entry mismatch.'
    Assert-Equal -Expected 10 -Actual $ordered[3].id -Message 'Deterministic ordering: oldest run expected last.'

    # --- canonical artifact selection -----------------------------------

    Assert-Equal -Expected $null -Actual (Select-CanonicalArtifactRecord -Artifacts @()) -Message 'Empty artifact list yielded a match.'

    $wrongNameArtifact = New-ArtifactRecord -Name 'VIIPER-windows-amd64-Snapshot'
    Assert-Equal -Expected $null -Actual (Select-CanonicalArtifactRecord -Artifacts @($wrongNameArtifact)) -Message 'Similarly-named (non-exact) artifact matched by prefix/substring.'

    $expiredArtifact = New-ArtifactRecord -Expired $true
    Assert-Equal -Expected $null -Actual (Select-CanonicalArtifactRecord -Artifacts @($expiredArtifact)) -Message 'Expired artifact matched.'

    $validArtifact = New-ArtifactRecord
    $selectedArtifact = Select-CanonicalArtifactRecord -Artifacts @($validArtifact, $wrongNameArtifact)
    Assert-Equal -Expected $validArtifact.id -Actual $selectedArtifact.id -Message 'Valid exact-name unexpired artifact not selected.'

    # missing/expired artifact fails closed: no eligible run carries it.
    $runWithoutArtifact = New-RunRecord -Id '20' -HeadSha $validCommit
    $noArtifactResult = Find-EligibleRunWithArtifact -EligibleRuns @($runWithoutArtifact) -GetArtifactsForRun { param($runId) @($expiredArtifact) }
    Assert-Equal -Expected $null -Actual $noArtifactResult -Message 'Run with only an expired artifact treated as having the canonical artifact.'

    # valid exact artifact succeeds via the discovery pipeline.
    $runWithArtifact = New-RunRecord -Id '21' -HeadSha $validCommit
    $found = Find-EligibleRunWithArtifact -EligibleRuns @($runWithArtifact) -GetArtifactsForRun { param($runId) @($validArtifact) }
    Assert-Equal -Expected $runWithArtifact.id -Actual $found.Run.id -Message 'Eligible run with canonical artifact not found.'
    Assert-Equal -Expected $validArtifact.id -Actual $found.Artifact.id -Message 'Canonical artifact not returned alongside its run.'

    # --- manifest validation ---------------------------------------------

    # valid exact metadata accepted.
    Test-ViiperArtifactManifest -Manifest (New-ValidManifest) -ExpectedCommit $validCommit

    # wrong repository rejected.
    $m = New-ValidManifest
    $m.repository = 'someone-else/VIIPER'
    Assert-Throws -Case 'wrong repository' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # wrong manifest commit rejected.
    Assert-Throws -Case 'wrong manifest commit' -ScriptBlock { Test-ViiperArtifactManifest -Manifest (New-ValidManifest -Commit ('c' * 40)) -ExpectedCommit $validCommit }

    # wrong build entrypoint rejected.
    $m = New-ValidManifest
    $m.build_entrypoint = 'make release'
    Assert-Throws -Case 'wrong build entrypoint' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # wrong platform rejected.
    $m = New-ValidManifest
    $m.platform = 'linux'
    Assert-Throws -Case 'wrong platform' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # wrong architecture rejected.
    $m = New-ValidManifest
    $m.architecture = 'arm64'
    Assert-Throws -Case 'wrong architecture' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # wrong filenames rejected.
    $m = New-ValidManifest
    $m.dll.file = 'libVIIPER2.dll'
    Assert-Throws -Case 'wrong dll filename' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    $m = New-ValidManifest
    $m.header.file = 'libVIIPER2.h'
    Assert-Throws -Case 'wrong header filename' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # malformed manifest commit rejected.
    $m = New-ValidManifest
    $m.commit = 'not-a-commit'
    Assert-Throws -Case 'malformed manifest commit' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # malformed manifest hash rejected.
    $m = New-ValidManifest
    $m.dll.sha256 = 'not-a-hash'
    Assert-Throws -Case 'malformed manifest dll hash' -ScriptBlock { Test-ViiperArtifactManifest -Manifest $m -ExpectedCommit $validCommit }

    # --- payload validation (real file fixtures) --------------------------

    function New-PayloadFixture {
        $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("update-viiper-test-" + [System.Guid]::NewGuid())
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.dll') -Destination $dir
        Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.h') -Destination $dir
        return $dir
    }

    $realDllHash = (Get-FileHash -LiteralPath (Join-Path $realViiperDir 'libVIIPER.dll') -Algorithm SHA256).Hash
    $realHeaderHash = (Get-FileHash -LiteralPath (Join-Path $realViiperDir 'libVIIPER.h') -Algorithm SHA256).Hash

    # valid exact artifact succeeds (payload stage).
    $goodDir = New-PayloadFixture
    $fixturesToClean += $goodDir
    $goodManifest = New-ValidManifest
    $goodManifest.dll.sha256 = $realDllHash
    $goodManifest.header.sha256 = $realHeaderHash
    $hashes = Test-ViiperArtifactPayload -StagingDirectory $goodDir -Manifest $goodManifest
    Assert-Equal -Expected $realDllHash.ToUpperInvariant() -Actual $hashes.DllSha256 -Message 'Returned DLL hash mismatch on success path.'

    # DLL hash mismatch rejected.
    $badDllDir = New-PayloadFixture
    $fixturesToClean += $badDllDir
    $badDllManifest = New-ValidManifest
    $badDllManifest.dll.sha256 = '0' * 64
    $badDllManifest.header.sha256 = $realHeaderHash
    Assert-Throws -Case 'DLL hash mismatch' -ScriptBlock { Test-ViiperArtifactPayload -StagingDirectory $badDllDir -Manifest $badDllManifest }

    # header hash mismatch rejected.
    $badHeaderDir = New-PayloadFixture
    $fixturesToClean += $badHeaderDir
    $badHeaderManifest = New-ValidManifest
    $badHeaderManifest.dll.sha256 = $realDllHash
    $badHeaderManifest.header.sha256 = '0' * 64
    Assert-Throws -Case 'header hash mismatch' -ScriptBlock { Test-ViiperArtifactPayload -StagingDirectory $badHeaderDir -Manifest $badHeaderManifest }

    # missing DLL/header rejected.
    $missingDllDir = Join-Path ([System.IO.Path]::GetTempPath()) ("update-viiper-test-" + [System.Guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $missingDllDir | Out-Null
    $fixturesToClean += $missingDllDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.h') -Destination $missingDllDir
    Assert-Throws -Case 'missing DLL' -ScriptBlock { Test-ViiperArtifactPayload -StagingDirectory $missingDllDir -Manifest $goodManifest }

    $missingHeaderDir = Join-Path ([System.IO.Path]::GetTempPath()) ("update-viiper-test-" + [System.Guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $missingHeaderDir | Out-Null
    $fixturesToClean += $missingHeaderDir
    Copy-Item -LiteralPath (Join-Path $realViiperDir 'libVIIPER.dll') -Destination $missingHeaderDir
    Assert-Throws -Case 'missing header' -ScriptBlock { Test-ViiperArtifactPayload -StagingDirectory $missingHeaderDir -Manifest $goodManifest }

    # --- manifest presence (Resolve-ViiperManifest) ------------------------

    # missing manifest rejected.
    $noManifestDir = Join-Path ([System.IO.Path]::GetTempPath()) ("update-viiper-test-" + [System.Guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $noManifestDir | Out-Null
    $fixturesToClean += $noManifestDir
    Assert-Throws -Case 'missing manifest' -ScriptBlock { Resolve-ViiperManifest -StagingDirectory $noManifestDir }

    # valid manifest file parses.
    $withManifestDir = Join-Path ([System.IO.Path]::GetTempPath()) ("update-viiper-test-" + [System.Guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $withManifestDir | Out-Null
    $fixturesToClean += $withManifestDir
    (New-ValidManifest | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $withManifestDir 'viiper-artifact.json')
    $parsedManifest = Resolve-ViiperManifest -StagingDirectory $withManifestDir
    Assert-Equal -Expected $validCommit -Actual $parsedManifest.commit -Message 'Parsed manifest commit mismatch.'

    Write-Host 'update-viiper.ps1 tests passed.'
}
finally {
    foreach ($fixture in $fixturesToClean) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
