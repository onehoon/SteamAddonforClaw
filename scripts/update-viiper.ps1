<#
Fetches and independently verifies the canonical Windows libVIIPER build
artifact for an exact onehoon/VIIPER commit. This script only fetches and
verifies into a staging directory; it never modifies the vendored
Dependencies/Viiper files, viiper.lock.json, PROVENANCE.md, or any managed
code. Adopting a verified artifact is a separate, later, reviewed operation.

Usage:
  powershell.exe -File scripts/update-viiper.ps1 -Commit <40-char-sha>

Eligible-run selection rule (deterministic, documented per Phase 2B1 scope):
  Among onehoon/VIIPER workflow runs with event=push, head_branch=main,
  head_sha exactly equal to -Commit, and conclusion=success, and which carry
  the exact canonical artifact 'libVIIPER-windows-amd64-Snapshot'
  (unexpired), the run with the latest created_at is selected; ties are
  broken by the highest run ID. Runs are never selected by run_number, since
  run_number is scoped per-workflow and is not a global ordering when
  multiple workflows produce runs for the same commit.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Commit,

    [string]$StagingRoot,

    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell's default console encoding is the system codepage, not
# UTF-8. Capturing `gh api` JSON output (which is UTF-8 and can contain
# multi-byte characters, e.g. in commit messages) through a non-UTF-8 console
# codepage corrupts those bytes and breaks ConvertFrom-Json.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path))
    $RepoRoot = Split-Path -Parent $scriptDirectory
}

$script:ViiperRepository = 'onehoon/VIIPER'
$script:CanonicalArtifactName = 'libVIIPER-windows-amd64-Snapshot'
$script:RequiredBuildEntrypoint = 'just build-libVIIPER Release'
$script:ManifestFileName = 'viiper-artifact.json'

# --- pure / testable functions --------------------------------------------

function Test-ViiperCommitFormat {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Commit)

    return $Commit -cmatch '^[0-9a-fA-F]{40}$'
}

function Select-EligibleWorkflowRuns {
    <#
    Filters a list of onehoon/VIIPER workflow run records down to those
    eligible for artifact discovery, sorted deterministically: created_at
    descending, then run ID descending. Never accepts pull_request runs,
    non-main branches, a different head_sha, or a non-success conclusion.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [array] $Runs,
        [Parameter(Mandatory)] [string] $Commit
    )

    $normalizedCommit = $Commit.ToLowerInvariant()

    $eligible = @($Runs | Where-Object {
        $_.event -eq 'push' -and
        $_.head_branch -eq 'main' -and
        $_.head_sha -and ($_.head_sha.ToLowerInvariant() -eq $normalizedCommit) -and
        $_.conclusion -eq 'success'
    })

    # Callers must wrap this call in @(...) themselves: PowerShell unrolls a
    # zero- or one-element pipeline output back into $null/a scalar on the
    # way out of a function, and @() at the call site is what restores it to
    # a proper array regardless of count.
    return @($eligible |
        Sort-Object -Property @{ Expression = { [DateTimeOffset]$_.created_at } }, @{ Expression = { [int64]$_.id } } -Descending)
}

function Select-CanonicalArtifactRecord {
    <#
    Returns the single unexpired artifact record whose name exactly equals
    the canonical artifact name, or $null if not present/unexpired. Throws
    if more than one unexpired artifact shares that exact name (ambiguous;
    fail closed rather than silently picking one).
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [array] $Artifacts,
        [string] $ArtifactName = $script:CanonicalArtifactName
    )

    $matches = @($Artifacts | Where-Object { $_.name -ceq $ArtifactName -and -not $_.expired })

    if ($matches.Count -gt 1) {
        throw "Ambiguous canonical artifact: found $($matches.Count) unexpired artifacts exactly named '$ArtifactName' on the same run."
    }

    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[0]
}

function Find-EligibleRunWithArtifact {
    <#
    Iterates eligible runs (already sorted deterministically) and returns the
    first (Run, Artifact) pair whose artifact list contains the unexpired
    canonical artifact. $GetArtifactsForRun is an injectable delegate
    ([scriptblock] run ID -> artifact record array) so this is testable
    without any network access.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [array] $EligibleRuns,
        [Parameter(Mandatory)] [scriptblock] $GetArtifactsForRun,
        [string] $ArtifactName = $script:CanonicalArtifactName
    )

    foreach ($run in $EligibleRuns) {
        $artifacts = @(& $GetArtifactsForRun $run.id)
        $artifact = Select-CanonicalArtifactRecord -Artifacts $artifacts -ArtifactName $ArtifactName
        if ($null -ne $artifact) {
            return [pscustomobject]@{
                Run      = $run
                Artifact = $artifact
            }
        }
    }

    return $null
}

function Resolve-ViiperManifest {
    <#
    Reads and parses the canonical artifact manifest from an extracted
    staging directory. Throws if the manifest file is missing or unparsable
    -- the manifest is not trusted merely because other files look right.
    #>
    param([Parameter(Mandatory)] [string] $StagingDirectory)

    $manifestPath = Join-Path $StagingDirectory $script:ManifestFileName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Canonical artifact manifest '$script:ManifestFileName' was not found in the extracted payload. Refusing to trust the artifact without it."
    }

    try {
        return Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Canonical artifact manifest '$script:ManifestFileName' could not be parsed as JSON: $($_.Exception.Message)"
    }
}

function Test-ViiperArtifactManifest {
    <#
    Validates the canonical artifact manifest's identity fields against the
    exact requested commit. Throws with a specific message on any violation.
    Does not touch the filesystem.
    #>
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string] $ExpectedCommit
    )

    function Assert-ManifestField {
        param($Actual, $Expected, [string] $Message)
        if ($Actual -ne $Expected) {
            throw "$Message Expected '$Expected', actual '$Actual'."
        }
    }

    foreach ($requiredProperty in @('schema_version', 'repository', 'commit', 'build_entrypoint', 'platform', 'architecture', 'dll', 'header')) {
        if (-not (Get-Member -InputObject $Manifest -Name $requiredProperty -MemberType NoteProperty)) {
            throw "Canonical artifact manifest is missing required field: $requiredProperty"
        }
    }

    foreach ($artifactKind in @('dll', 'header')) {
        $section = $Manifest.$artifactKind
        foreach ($requiredProperty in @('file', 'sha256')) {
            if (-not (Get-Member -InputObject $section -Name $requiredProperty -MemberType NoteProperty)) {
                throw "Canonical artifact manifest '$artifactKind' section is missing required field: $requiredProperty"
            }
        }
    }

    Assert-ManifestField -Actual $Manifest.schema_version -Expected 1 -Message 'Manifest schema_version mismatch.'
    Assert-ManifestField -Actual $Manifest.repository -Expected $script:ViiperRepository -Message 'Manifest repository mismatch.'
    Assert-ManifestField -Actual $Manifest.build_entrypoint -Expected $script:RequiredBuildEntrypoint -Message 'Manifest build_entrypoint mismatch.'
    Assert-ManifestField -Actual $Manifest.platform -Expected 'windows' -Message 'Manifest platform mismatch.'
    Assert-ManifestField -Actual $Manifest.architecture -Expected 'amd64' -Message 'Manifest architecture mismatch.'
    Assert-ManifestField -Actual $Manifest.dll.file -Expected 'libVIIPER.dll' -Message 'Manifest dll.file mismatch.'
    Assert-ManifestField -Actual $Manifest.header.file -Expected 'libVIIPER.h' -Message 'Manifest header.file mismatch.'

    if (-not (Test-ViiperCommitFormat -Commit $Manifest.commit)) {
        throw "Manifest commit is not exactly 40 hex characters: '$($Manifest.commit)'."
    }

    if ($Manifest.commit.ToLowerInvariant() -ne $ExpectedCommit.ToLowerInvariant()) {
        throw "Manifest commit does not match the requested exact commit. Expected '$ExpectedCommit', actual '$($Manifest.commit)'."
    }

    if ($Manifest.dll.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Manifest dll.sha256 is not exactly 64 hex characters: '$($Manifest.dll.sha256)'."
    }

    if ($Manifest.header.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Manifest header.sha256 is not exactly 64 hex characters: '$($Manifest.header.sha256)'."
    }
}

function Test-ViiperArtifactPayload {
    <#
    Verifies the extracted DLL/header exist and that their independently
    recomputed SHA-256 hashes equal the (already format-validated) manifest
    hashes. Never trusts the manifest hash without recomputing from the
    actual extracted bytes.
    #>
    param(
        [Parameter(Mandatory)] [string] $StagingDirectory,
        [Parameter(Mandatory)] $Manifest
    )

    $dllPath = Join-Path $StagingDirectory $Manifest.dll.file
    $headerPath = Join-Path $StagingDirectory $Manifest.header.file

    if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
        throw "Extracted canonical DLL was not found: $dllPath"
    }

    if (-not (Test-Path -LiteralPath $headerPath -PathType Leaf)) {
        throw "Extracted canonical header was not found: $headerPath"
    }

    $actualDllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $actualHeaderHash = (Get-FileHash -LiteralPath $headerPath -Algorithm SHA256).Hash
    $expectedDllHash = $Manifest.dll.sha256.ToUpperInvariant()
    $expectedHeaderHash = $Manifest.header.sha256.ToUpperInvariant()

    if ($actualDllHash -ne $expectedDllHash) {
        throw "Extracted libVIIPER.dll SHA-256 does not match the manifest. Expected '$expectedDllHash', actual '$actualDllHash'."
    }

    if ($actualHeaderHash -ne $expectedHeaderHash) {
        throw "Extracted libVIIPER.h SHA-256 does not match the manifest. Expected '$expectedHeaderHash', actual '$actualHeaderHash'."
    }

    return [pscustomobject]@{
        DllSha256    = $actualDllHash
        HeaderSha256 = $actualHeaderHash
    }
}

# --- network-facing wrappers (not exercised by fixture tests) -------------

function Get-ViiperWorkflowRuns {
    param([Parameter(Mandatory)] [string] $Commit)

    $uri = "repos/$script:ViiperRepository/actions/runs?head_sha=$Commit&event=push&branch=main&per_page=100"
    # Native stderr must not be merged into stdout here: doing so corrupts
    # the JSON payload before it reaches ConvertFrom-Json.
    $json = gh api $uri
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query $script:ViiperRepository workflow runs via GitHub CLI (exit $LASTEXITCODE). Ensure 'gh' is installed and authenticated ('gh auth login')."
    }

    $response = $json | ConvertFrom-Json
    if ($response.total_count -gt 100) {
        throw "Unexpectedly large result set ($($response.total_count)) for a single exact commit ($Commit). Refusing to page through results silently -- investigate manually."
    }

    return @($response.workflow_runs)
}

function Get-ViiperRunArtifacts {
    param([Parameter(Mandatory)] [long] $RunId)

    $uri = "repos/$script:ViiperRepository/actions/runs/$RunId/artifacts?per_page=100"
    $json = gh api $uri
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list artifacts for $script:ViiperRepository run $RunId via GitHub CLI (exit $LASTEXITCODE)."
    }

    $response = $json | ConvertFrom-Json
    if ($response.total_count -gt 100) {
        throw "Unexpectedly large artifact list ($($response.total_count)) for run $RunId. Refusing to page through results silently -- investigate manually."
    }

    return @($response.artifacts)
}

function Invoke-ViiperArtifactDownload {
    param(
        [Parameter(Mandatory)] [long] $RunId,
        [Parameter(Mandatory)] [string] $ArtifactName,
        [Parameter(Mandatory)] [string] $DownloadDirectory,
        [Parameter(Mandatory)] [string] $StagingDirectory
    )

    New-Item -ItemType Directory -Force -Path $DownloadDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $StagingDirectory | Out-Null

    $downloadOutput = gh run download $RunId --repo $script:ViiperRepository --name $ArtifactName --dir $DownloadDirectory 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to download artifact '$ArtifactName' from $script:ViiperRepository run $RunId. It may be missing or expired. Details: $downloadOutput"
    }

    # The VIIPER build pipeline uploads the canonical payload as a single
    # zip *inside* the GitHub Actions artifact; `gh run download` extracts
    # the outer artifact wrapper but leaves that inner zip for us to expand.
    $innerZips = @(Get-ChildItem -LiteralPath $DownloadDirectory -Filter '*.zip' -File)
    if ($innerZips.Count -eq 0) {
        # Some artifact layouts may already be flat (no inner zip).
        Copy-Item -Path (Join-Path $DownloadDirectory '*') -Destination $StagingDirectory -Recurse -Force
        return
    }

    if ($innerZips.Count -gt 1) {
        throw "Downloaded artifact '$ArtifactName' from run $RunId contains more than one top-level zip; cannot unambiguously identify the canonical payload."
    }

    Expand-Archive -LiteralPath $innerZips[0].FullName -DestinationPath $StagingDirectory -Force
}

# --- main -------------------------------------------------------------

if ($MyInvocation.InvocationName -eq '.') {
    return
}

if (-not (Test-ViiperCommitFormat -Commit $Commit)) {
    throw "-Commit must be exactly 40 hexadecimal characters. Branch names, tags, and short SHAs are not accepted. Got: '$Commit'."
}

$normalizedCommit = $Commit.ToLowerInvariant()

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI ('gh') was not found on PATH. Install it and run 'gh auth login' before using this script."
}

Write-Host "Querying $script:ViiperRepository for an exact push/main/success run at commit $normalizedCommit..."
$allRuns = @(Get-ViiperWorkflowRuns -Commit $normalizedCommit)
$eligibleRuns = @(Select-EligibleWorkflowRuns -Runs $allRuns -Commit $normalizedCommit)

if ($eligibleRuns.Count -eq 0) {
    throw "No eligible $script:ViiperRepository run found for exact commit $normalizedCommit (requires event=push, branch=main, conclusion=success). This commit may not exist, may not be on main, or its build may not have succeeded. Not falling back to another commit."
}

Write-Host "Found $($eligibleRuns.Count) eligible run(s); searching for canonical artifact '$script:CanonicalArtifactName'..."
$selection = Find-EligibleRunWithArtifact -EligibleRuns $eligibleRuns -GetArtifactsForRun { param($runId) Get-ViiperRunArtifacts -RunId $runId }

if ($null -eq $selection) {
    $runIds = ($eligibleRuns | ForEach-Object { $_.id }) -join ', '
    throw "Eligible $script:ViiperRepository run(s) exist for commit $normalizedCommit (run IDs: $runIds) but none carries an unexpired canonical artifact named '$script:CanonicalArtifactName'. Not fetching a newer artifact and not using any mutable dev-snapshot release. Choose another explicitly reviewed commit."
}

$run = $selection.Run
$artifact = $selection.Artifact
Write-Host "Selected run $($run.id) (created $($run.created_at)) with artifact ID $($artifact.id)."

if ([string]::IsNullOrEmpty($StagingRoot)) {
    $StagingRoot = Join-Path $RepoRoot 'artifacts\viiper-update'
}
$stagingDirectory = Join-Path $StagingRoot $normalizedCommit
$downloadDirectory = Join-Path $stagingDirectory '_download'

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

Write-Host "Downloading and extracting into staging directory: $stagingDirectory"
Invoke-ViiperArtifactDownload -RunId $run.id -ArtifactName $script:CanonicalArtifactName -DownloadDirectory $downloadDirectory -StagingDirectory $stagingDirectory
Remove-Item -LiteralPath $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue

$manifest = Resolve-ViiperManifest -StagingDirectory $stagingDirectory
Test-ViiperArtifactManifest -Manifest $manifest -ExpectedCommit $normalizedCommit
$hashes = Test-ViiperArtifactPayload -StagingDirectory $stagingDirectory -Manifest $manifest

Write-Host ''
Write-Host 'VIIPER artifact verified'
Write-Host "Repository: $script:ViiperRepository"
Write-Host "Commit: $normalizedCommit"
Write-Host "Run ID: $($run.id)"
Write-Host "Artifact: $script:CanonicalArtifactName"
Write-Host "DLL SHA-256: $($hashes.DllSha256)"
Write-Host "Header SHA-256: $($hashes.HeaderSha256)"
Write-Host "Staging directory: $stagingDirectory"
Write-Host ''
Write-Host 'No vendored dependency files were modified.'
