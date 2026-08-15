[CmdletBinding()]
param(
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path))
    $RepoRoot = Split-Path -Parent $scriptDirectory
}

$viiperDir = Join-Path $RepoRoot 'src\SteamInputAddonforClaw\Dependencies\Viiper'
$lockPath = Join-Path $viiperDir 'viiper.lock.json'
$provenancePath = Join-Path $viiperDir 'PROVENANCE.md'
$integrationDocPath = Join-Path $RepoRoot 'docs\VIIPER_INTEGRATION.md'
$migrationDocPath = Join-Path $RepoRoot 'docs\VIIPER_MIGRATION_TODO.md'

function Assert-Field {
    param(
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "VIIPER lock file was not found: $lockPath"
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json

foreach ($requiredProperty in @('schema_version', 'repository', 'commit', 'build_entrypoint', 'platform', 'architecture', 'dll', 'header')) {
    if (-not (Get-Member -InputObject $lock -Name $requiredProperty -MemberType NoteProperty)) {
        throw "VIIPER lock file is missing required field: $requiredProperty"
    }
}

foreach ($artifact in @('dll', 'header')) {
    $section = $lock.$artifact
    foreach ($requiredProperty in @('file', 'sha256')) {
        if (-not (Get-Member -InputObject $section -Name $requiredProperty -MemberType NoteProperty)) {
            throw "VIIPER lock file '$artifact' section is missing required field: $requiredProperty"
        }
    }
}

Assert-Field -Actual $lock.schema_version -Expected 1 -Message 'Lock schema_version mismatch.'
Assert-Field -Actual $lock.repository -Expected 'onehoon/VIIPER' -Message 'Lock repository mismatch.'
Assert-Field -Actual $lock.build_entrypoint -Expected 'just build-libVIIPER Release' -Message 'Lock build_entrypoint mismatch.'
Assert-Field -Actual $lock.platform -Expected 'windows' -Message 'Lock platform mismatch.'
Assert-Field -Actual $lock.architecture -Expected 'amd64' -Message 'Lock architecture mismatch.'
Assert-Field -Actual $lock.dll.file -Expected 'libVIIPER.dll' -Message 'Lock dll.file mismatch.'
Assert-Field -Actual $lock.header.file -Expected 'libVIIPER.h' -Message 'Lock header.file mismatch.'

if ($lock.commit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Lock commit is not exactly 40 hex characters: '$($lock.commit)'."
}

$lockCommit = $lock.commit.ToLowerInvariant()
$lockDllHash = $lock.dll.sha256.ToUpperInvariant()
$lockHeaderHash = $lock.header.sha256.ToUpperInvariant()

if ($lockDllHash -notmatch '^[0-9A-F]{64}$') {
    throw "Lock dll.sha256 is not exactly 64 hex characters: '$($lock.dll.sha256)'."
}

if ($lockHeaderHash -notmatch '^[0-9A-F]{64}$') {
    throw "Lock header.sha256 is not exactly 64 hex characters: '$($lock.header.sha256)'."
}

$dllPath = Join-Path $viiperDir $lock.dll.file
$headerPath = Join-Path $viiperDir $lock.header.file

if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Vendored VIIPER DLL was not found: $dllPath"
}

if (-not (Test-Path -LiteralPath $headerPath -PathType Leaf)) {
    throw "Vendored VIIPER header was not found: $headerPath"
}

$actualDllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
$actualHeaderHash = (Get-FileHash -LiteralPath $headerPath -Algorithm SHA256).Hash

Assert-Field -Actual $actualDllHash -Expected $lockDllHash -Message 'Vendored libVIIPER.dll SHA-256 does not match viiper.lock.json.'
Assert-Field -Actual $actualHeaderHash -Expected $lockHeaderHash -Message 'Vendored libVIIPER.h SHA-256 does not match viiper.lock.json.'

# --- PROVENANCE.md cross-check -------------------------------------------

if (-not (Test-Path -LiteralPath $provenancePath -PathType Leaf)) {
    throw "VIIPER provenance record was not found: $provenancePath"
}

$provenanceText = Get-Content -LiteralPath $provenancePath -Raw

$provenanceCommitMatch = [regex]::Match($provenanceText, 'Commit:\s*([0-9a-fA-F]+)')
if (-not $provenanceCommitMatch.Success) {
    throw "PROVENANCE.md is missing a parseable 'Commit:' field."
}
$provenanceCommit = $provenanceCommitMatch.Groups[1].Value
if ($provenanceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "PROVENANCE.md commit is not exactly 40 hex characters: '$provenanceCommit'."
}

$provenanceHeaderMatch = [regex]::Match($provenanceText, 'Generated header SHA-256:\s*([0-9a-fA-F]+)')
if (-not $provenanceHeaderMatch.Success) {
    throw "PROVENANCE.md is missing a parseable 'Generated header SHA-256:' field."
}
$provenanceHeaderHash = $provenanceHeaderMatch.Groups[1].Value
if ($provenanceHeaderHash -notmatch '^[0-9a-fA-F]{64}$') {
    throw "PROVENANCE.md header SHA-256 is not exactly 64 hex characters: '$provenanceHeaderHash'."
}

$provenanceDllMatch = [regex]::Match($provenanceText, 'DLL SHA-256:\s*([0-9a-fA-F]+)')
if (-not $provenanceDllMatch.Success) {
    throw "PROVENANCE.md is missing a parseable 'DLL SHA-256:' field."
}
$provenanceDllHash = $provenanceDllMatch.Groups[1].Value
if ($provenanceDllHash -notmatch '^[0-9a-fA-F]{64}$') {
    throw "PROVENANCE.md DLL SHA-256 is not exactly 64 hex characters: '$provenanceDllHash'."
}

Assert-Field -Actual $provenanceCommit.ToLowerInvariant() -Expected $lockCommit -Message 'PROVENANCE.md commit does not match viiper.lock.json.'
Assert-Field -Actual $provenanceHeaderHash.ToUpperInvariant() -Expected $lockHeaderHash -Message 'PROVENANCE.md header SHA-256 does not match viiper.lock.json.'
Assert-Field -Actual $provenanceDllHash.ToUpperInvariant() -Expected $lockDllHash -Message 'PROVENANCE.md DLL SHA-256 does not match viiper.lock.json.'

# --- authoritative documentation pin cross-check --------------------------

if (-not (Test-Path -LiteralPath $integrationDocPath -PathType Leaf)) {
    throw "VIIPER integration doc was not found: $integrationDocPath"
}

$integrationText = Get-Content -LiteralPath $integrationDocPath -Raw
$integrationMatch = [regex]::Match($integrationText, 'Embedded VIIPER revision\s*\|\s*`([0-9a-fA-F]+)`')
if (-not $integrationMatch.Success) {
    throw "docs/VIIPER_INTEGRATION.md is missing a parseable 'Embedded VIIPER revision' pin."
}
$integrationCommit = $integrationMatch.Groups[1].Value
if ($integrationCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "docs/VIIPER_INTEGRATION.md pinned commit is not exactly 40 hex characters: '$integrationCommit'."
}
Assert-Field -Actual $integrationCommit.ToLowerInvariant() -Expected $lockCommit -Message 'docs/VIIPER_INTEGRATION.md pinned commit does not match viiper.lock.json.'

if (-not (Test-Path -LiteralPath $migrationDocPath -PathType Leaf)) {
    throw "VIIPER migration doc was not found: $migrationDocPath"
}

$migrationText = Get-Content -LiteralPath $migrationDocPath -Raw
$migrationMatch = [regex]::Match($migrationText, 'onehoon/VIIPER@([0-9a-fA-F]+)')
if (-not $migrationMatch.Success) {
    throw "docs/VIIPER_MIGRATION_TODO.md is missing a parseable 'onehoon/VIIPER@<commit>' pin."
}
$migrationCommit = $migrationMatch.Groups[1].Value
if ($migrationCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "docs/VIIPER_MIGRATION_TODO.md pinned commit is not exactly 40 hex characters: '$migrationCommit'."
}
Assert-Field -Actual $migrationCommit.ToLowerInvariant() -Expected $lockCommit -Message 'docs/VIIPER_MIGRATION_TODO.md pinned commit does not match viiper.lock.json.'

Write-Host 'VIIPER embedded dependency verified: lock, provenance, and documentation pins are aligned.'
