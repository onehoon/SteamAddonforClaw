<#
Mechanically adopts an already-fetched, already-verified canonical VIIPER
artifact staging payload (produced by scripts/update-viiper.ps1) into the
vendored dependency identity: the DLL/header themselves, viiper.lock.json,
PROVENANCE.md, the documented pins, THIRD_PARTY_NOTICES.md, and the two
other files that independently pin the DLL hash
(ViiperRuntimeInspector.ExpectedPayloadSha256 and
scripts/verify-publish-assets.ps1).

This script never edits managed P/Invoke code, RequiredExports, struct/enum
definitions, callback lifetime logic, or any runtime/session/mapper
behavior. It never invents ABI claims -- PROVENANCE.md's "ABI review"
section is reset to an evergreen human-review placeholder, never
synthesized from the new header's content.

Usage:
  powershell.exe -File scripts/adopt-viiper.ps1 -StagingDirectory <dir> -ExpectedCommit <40-char-sha>

-StagingDirectory must be a directory already containing a verified
libVIIPER.dll, libVIIPER.h, and viiper-artifact.json (e.g. the staging
directory scripts/update-viiper.ps1 leaves behind). -ExpectedCommit must be
the exact commit the caller actually requested/verified; this script
independently re-validates that the staged manifest's commit matches it
(not just that the manifest is internally well-formed), and independently
recomputes the staged DLL/header hashes against the manifest -- it does not
simply trust that update-viiper.ps1 was run first or that the staging
directory's contents match what the caller intended to adopt.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$StagingDirectory,

    [Parameter(Mandatory)]
    [string]$ExpectedCommit,

    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path))
    $RepoRoot = Split-Path -Parent $scriptDirectory
}

$script:ViiperRepository = 'onehoon/VIIPER'
$script:RequiredBuildEntrypoint = 'just build-libVIIPER Release'
$script:ManifestFileName = 'viiper-artifact.json'

# --- pure / testable functions --------------------------------------------

function Test-ViiperUpgradeEligibility {
    <#
    Classifies an adoption request as NoOp / Eligible / Downgrade / FailClosed
    without any I/O. $CompareStatus is the caller-supplied classification of
    the CurrentCommit..TargetCommit relationship on VIIPER main (e.g. the
    GitHub compare API's "status" field: identical/ahead/behind/diverged) --
    pure so it's testable without a real repository ancestry lookup.
    #>
    param(
        [Parameter(Mandatory)] [string] $CurrentCommit,
        [Parameter(Mandatory)] [string] $TargetCommit,
        [string] $CompareStatus
    )

    if ($TargetCommit.ToLowerInvariant() -eq $CurrentCommit.ToLowerInvariant()) {
        return 'NoOp'
    }

    switch ($CompareStatus) {
        'ahead' { return 'Eligible' }
        'identical' { return 'NoOp' }
        'behind' { return 'Downgrade' }
        default { return 'FailClosed' }
    }
}

function Resolve-ViiperAdoptionManifest {
    param([Parameter(Mandatory)] [string] $StagingDirectory)

    $manifestPath = Join-Path $StagingDirectory $script:ManifestFileName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Canonical artifact manifest '$script:ManifestFileName' was not found in staging directory: $StagingDirectory"
    }

    try {
        return Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Canonical artifact manifest '$script:ManifestFileName' could not be parsed as JSON: $($_.Exception.Message)"
    }
}

function Test-ViiperAdoptionManifest {
    <#
    Re-validates the staging payload's manifest identity fields. Duplicated
    (deliberately, not shared) from scripts/update-viiper.ps1's validation so
    this script fails closed even if invoked without update-viiper.ps1 having
    run first, and so the two scripts can evolve independently.
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
            throw "Staged artifact manifest is missing required field: $requiredProperty"
        }
    }

    Assert-ManifestField -Actual $Manifest.schema_version -Expected 1 -Message 'Manifest schema_version mismatch.'
    Assert-ManifestField -Actual $Manifest.repository -Expected $script:ViiperRepository -Message 'Manifest repository mismatch.'
    Assert-ManifestField -Actual $Manifest.build_entrypoint -Expected $script:RequiredBuildEntrypoint -Message 'Manifest build_entrypoint mismatch.'
    Assert-ManifestField -Actual $Manifest.platform -Expected 'windows' -Message 'Manifest platform mismatch.'
    Assert-ManifestField -Actual $Manifest.architecture -Expected 'amd64' -Message 'Manifest architecture mismatch.'
    Assert-ManifestField -Actual $Manifest.dll.file -Expected 'libVIIPER.dll' -Message 'Manifest dll.file mismatch.'
    Assert-ManifestField -Actual $Manifest.header.file -Expected 'libVIIPER.h' -Message 'Manifest header.file mismatch.'

    if ($Manifest.commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Manifest commit is not exactly 40 hex characters: '$($Manifest.commit)'."
    }

    if ($Manifest.commit.ToLowerInvariant() -ne $ExpectedCommit.ToLowerInvariant()) {
        throw "Manifest commit does not match the caller's expected target commit. Expected '$ExpectedCommit', actual '$($Manifest.commit)'. Refusing to adopt a staging payload for a different commit than what was actually requested/verified."
    }

    if ($Manifest.dll.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Manifest dll.sha256 is not exactly 64 hex characters: '$($Manifest.dll.sha256)'."
    }

    if ($Manifest.header.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Manifest header.sha256 is not exactly 64 hex characters: '$($Manifest.header.sha256)'."
    }
}

function Test-ViiperAdoptionPayloadFiles {
    param(
        [Parameter(Mandatory)] [string] $StagingDirectory,
        [Parameter(Mandatory)] $Manifest
    )

    $dllPath = Join-Path $StagingDirectory $Manifest.dll.file
    $headerPath = Join-Path $StagingDirectory $Manifest.header.file

    if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
        throw "Staged canonical DLL was not found: $dllPath"
    }

    if (-not (Test-Path -LiteralPath $headerPath -PathType Leaf)) {
        throw "Staged canonical header was not found: $headerPath"
    }

    $actualDllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $actualHeaderHash = (Get-FileHash -LiteralPath $headerPath -Algorithm SHA256).Hash
    $expectedDllHash = $Manifest.dll.sha256.ToUpperInvariant()
    $expectedHeaderHash = $Manifest.header.sha256.ToUpperInvariant()

    if ($actualDllHash -ne $expectedDllHash) {
        throw "Staged libVIIPER.dll SHA-256 does not match the manifest. Expected '$expectedDllHash', actual '$actualDllHash'."
    }

    if ($actualHeaderHash -ne $expectedHeaderHash) {
        throw "Staged libVIIPER.h SHA-256 does not match the manifest. Expected '$expectedHeaderHash', actual '$actualHeaderHash'."
    }

    return [pscustomobject]@{
        DllPath      = $dllPath
        HeaderPath   = $headerPath
        DllSha256    = $actualDllHash
        HeaderSha256 = $actualHeaderHash
    }
}

function Set-ExactTextReplacement {
    <#
    Fails closed unless $OldValue occurs in the file exactly $ExpectedCount
    times (default 1); only then replaces all of those occurrences with
    $NewValue. Never a blind global-replace: a mismatched count means the
    file's shape changed in a way this script does not understand, and it
    must not guess.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $OldValue,
        [Parameter(Mandatory)] [string] $NewValue,
        [int] $ExpectedCount = 1
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot apply mechanical replacement: file not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $escaped = [regex]::Escape($OldValue)
    $actualCount = [regex]::Matches($content, $escaped).Count

    if ($actualCount -ne $ExpectedCount) {
        throw "Refusing to edit $Path -- expected exactly $ExpectedCount occurrence(s) of '$OldValue', found $actualCount. The file's shape may have changed; this requires manual review."
    }

    # A MatchEvaluator (not -replace's replacement-string form) keeps the substitution fully
    # literal regardless of what characters NewValue contains ($ and \ are special in -replace).
    $updated = [regex]::Replace($content, $escaped, { param($m) $NewValue })

    Set-Content -LiteralPath $Path -Value $updated -NoNewline
}

function Set-AnchoredCommitReplacement {
    <#
    Replaces a commit SHA that appears inside a larger, specific anchor
    pattern (e.g. a doc's "Embedded VIIPER revision" table row), rather than
    blindly replacing every occurrence of the raw 40-hex string in the file --
    other prose (changelog-style "Phase 2B2 adopted X -> Y" sentences, for
    example) may legitimately keep mentioning an old commit as history and
    must not be touched.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $NewCommit,
        [int] $ExpectedCount = 1
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot apply mechanical replacement: file not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $matches = [regex]::Matches($content, $Pattern)

    if ($matches.Count -ne $ExpectedCount) {
        throw "Refusing to edit $Path -- expected exactly $ExpectedCount match(es) of pattern '$Pattern', found $($matches.Count). The file's shape may have changed; this requires manual review."
    }

    $updated = [regex]::Replace($content, $Pattern, { param($m) $m.Value.Replace($m.Groups[1].Value, $NewCommit) })
    Set-Content -LiteralPath $Path -Value $updated -NoNewline
}

function Set-ViiperAbiReviewPlaceholder {
    <#
    Resets PROVENANCE.md's marker-delimited "ABI review" section to the
    evergreen human-review placeholder. Never synthesizes ABI claims from the
    new header -- the whole point is that a human fills this in during
    review, and automation must not pretend to have already done that.
    #>
    param([Parameter(Mandatory)] [string] $Path)

    $beginMarker = '<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->'
    $endMarker = '<!-- AUTOMATION: END MANAGED ABI REVIEW SECTION -->'
    # A single-quoted here-string (no interpolation) is deliberate: a double-quoted @"..."@
    # here-string treats backtick as an escape character, and markdown code spans like
    # `libVIIPER.h` inside one silently get mangled (e.g. `` `t `` is the TAB escape).
    $placeholder = @'
<!-- AUTOMATION: BEGIN MANAGED ABI REVIEW SECTION -->
## ABI review

ABI compatibility is not inferred by the dependency automation. Review the
generated `libVIIPER.h` diff and the managed interop
(`CanonicalViiperNativeApi.cs`, `CanonicalViiperNativeTypes.cs`,
`CanonicalViiperNativeAbiTests.cs`) before merging this dependency update.
Replace this paragraph with the reviewed ABI delta -- including any changed
struct layout, offsets, or exports -- once confirmed.
<!-- AUTOMATION: END MANAGED ABI REVIEW SECTION -->
'@

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot reset ABI review section: file not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $pattern = [regex]::Escape($beginMarker) + '[\s\S]*?' + [regex]::Escape($endMarker)
    $matches = [regex]::Matches($content, $pattern)

    if ($matches.Count -ne 1) {
        throw "Refusing to edit $Path -- expected exactly one ABI review section delimited by '$beginMarker' / '$endMarker', found $($matches.Count). The file's shape may have changed; this requires manual review."
    }

    $updated = [regex]::Replace($content, $pattern, { param($m) $placeholder.TrimEnd("`r", "`n") })
    Set-Content -LiteralPath $Path -Value $updated -NoNewline
}

# --- main -------------------------------------------------------------

if ($MyInvocation.InvocationName -eq '.') {
    return
}

if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "-ExpectedCommit must be exactly 40 hexadecimal characters. Got: '$ExpectedCommit'."
}
$ExpectedCommit = $ExpectedCommit.ToLowerInvariant()

$viiperDir = Join-Path $RepoRoot 'src\SteamInputAddonforClaw\Dependencies\Viiper'
$lockPath = Join-Path $viiperDir 'viiper.lock.json'
$provenancePath = Join-Path $viiperDir 'PROVENANCE.md'
$integrationDocPath = Join-Path $RepoRoot 'docs\VIIPER_INTEGRATION.md'
$migrationDocPath = Join-Path $RepoRoot 'docs\VIIPER_MIGRATION_TODO.md'
$noticesPath = Join-Path $RepoRoot 'THIRD_PARTY_NOTICES.md'
$runtimeInspectorPath = Join-Path $RepoRoot 'src\SteamInputAddonforClaw\Prerequisites\ViiperRuntimeInspector.cs'
$verifyPublishAssetsPath = Join-Path $RepoRoot 'scripts\verify-publish-assets.ps1'

if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "Current viiper.lock.json was not found: $lockPath"
}
$currentLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$currentCommit = $currentLock.commit
$currentDllHashUpper = $currentLock.dll.sha256.ToUpperInvariant()
$currentHeaderHashUpper = $currentLock.header.sha256.ToUpperInvariant()
$currentDllHashLower = $currentDllHashUpper.ToLowerInvariant()
$currentHeaderHashLower = $currentHeaderHashUpper.ToLowerInvariant()

$manifest = Resolve-ViiperAdoptionManifest -StagingDirectory $StagingDirectory
Test-ViiperAdoptionManifest -Manifest $manifest -ExpectedCommit $ExpectedCommit
$payload = Test-ViiperAdoptionPayloadFiles -StagingDirectory $StagingDirectory -Manifest $manifest

$targetCommit = $manifest.commit.ToLowerInvariant()

if ($targetCommit -eq $currentCommit.ToLowerInvariant()) {
    Write-Host "No-op: staged commit $targetCommit is already the current embedded revision. Nothing to adopt."
    return
}

Write-Host "Adopting VIIPER $currentCommit -> $targetCommit"

$targetDllHashUpper = $manifest.dll.sha256.ToUpperInvariant()
$targetHeaderHashUpper = $manifest.header.sha256.ToUpperInvariant()
$targetDllHashLower = $targetDllHashUpper.ToLowerInvariant()
$targetHeaderHashLower = $targetHeaderHashUpper.ToLowerInvariant()

# 1. Vendored DLL/header: verified binary payload copied as-is.
Copy-Item -LiteralPath $payload.DllPath -Destination (Join-Path $viiperDir 'libVIIPER.dll') -Force
Copy-Item -LiteralPath $payload.HeaderPath -Destination (Join-Path $viiperDir 'libVIIPER.h') -Force

# 2. viiper.lock.json: fully-owned format, rewritten directly rather than
#    round-tripped through ConvertTo-Json (which would not guarantee the
#    same key order/formatting as the hand-authored file).
$lockContent = @"
{
  "schema_version": 1,
  "repository": "$script:ViiperRepository",
  "commit": "$targetCommit",
  "build_entrypoint": "$script:RequiredBuildEntrypoint",
  "platform": "windows",
  "architecture": "amd64",
  "dll": {
    "file": "libVIIPER.dll",
    "sha256": "$targetDllHashUpper"
  },
  "header": {
    "file": "libVIIPER.h",
    "sha256": "$targetHeaderHashUpper"
  }
}
"@
Set-Content -LiteralPath $lockPath -Value ($lockContent + "`n") -NoNewline

# 3. PROVENANCE.md: mechanical identity fields, then reset the ABI review
#    placeholder (never synthesized).
Set-ExactTextReplacement -Path $provenancePath -OldValue "Commit:     $currentCommit" -NewValue "Commit:     $targetCommit"
Set-ExactTextReplacement -Path $provenancePath -OldValue "Generated header SHA-256: $currentHeaderHashLower" -NewValue "Generated header SHA-256: $targetHeaderHashLower"
Set-ExactTextReplacement -Path $provenancePath -OldValue "DLL SHA-256:              $currentDllHashLower" -NewValue "DLL SHA-256:              $targetDllHashLower"
Set-ViiperAbiReviewPlaceholder -Path $provenancePath

# 4. Documented pins.
Set-AnchoredCommitReplacement -Path $integrationDocPath -Pattern '(?m)^\| Embedded VIIPER revision \| `([0-9a-fA-F]{40})` \|\r?$' -NewCommit $targetCommit
Set-AnchoredCommitReplacement -Path $migrationDocPath -Pattern '(?m)^onehoon/VIIPER@([0-9a-fA-F]{40})\r?$' -NewCommit $targetCommit

# 5. THIRD_PARTY_NOTICES.md VIIPER source identity.
Set-AnchoredCommitReplacement -Path $noticesPath -Pattern '(?m)^- Canonical source: https://github\.com/onehoon/VIIPER/tree/([0-9a-fA-F]{40})\r?$' -NewCommit $targetCommit
Set-AnchoredCommitReplacement -Path $noticesPath -Pattern '(?m)^- Source baseline: pinned commit `([0-9a-fA-F]{40})`\r?$' -NewCommit $targetCommit

# 6. Runtime prerequisite hash.
Set-ExactTextReplacement -Path $runtimeInspectorPath -OldValue "ExpectedPayloadSha256 = `"$currentDllHashUpper`";" -NewValue "ExpectedPayloadSha256 = `"$targetDllHashUpper`";"

# 7. Publish verification gate hash.
Set-ExactTextReplacement -Path $verifyPublishAssetsPath -OldValue "expectedViiperSha256 = '$currentDllHashUpper'" -NewValue "expectedViiperSha256 = '$targetDllHashUpper'"

Write-Host ''
Write-Host 'VIIPER dependency mechanically adopted.'
Write-Host "Previous commit: $currentCommit"
Write-Host "New commit: $targetCommit"
Write-Host "DLL SHA-256: $targetDllHashUpper"
Write-Host "Header SHA-256: $targetHeaderHashUpper"
Write-Host ''
Write-Host 'Managed ABI compatibility has NOT been inferred or automatically adapted. Review the generated header diff and managed interop before merging.'
