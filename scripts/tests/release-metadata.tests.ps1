$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'release-metadata.ps1')

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ciWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/ci.yml') -Raw
$releaseWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release.yml') -Raw

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

$ciLines = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/ci.yml')
$pullRequestIndex = [Array]::IndexOf($ciLines, '  pull_request:')
Assert-Equal $true ($pullRequestIndex -ge 0) 'PR test workflow trigger exists'

$pullRequestTriggerLines = @()
for ($index = $pullRequestIndex + 1; $index -lt $ciLines.Count; $index++) {
    if ($ciLines[$index] -match '^  [A-Za-z0-9_-]+:') {
        break
    }

    $pullRequestTriggerLines += $ciLines[$index]
}

Assert-Equal $false ((($pullRequestTriggerLines -join "`n") -match '(?m)^    branches(?:-ignore)?:')) 'PR test workflow accepts every target branch'
Assert-Equal $true ($releaseWorkflow -match '(?m)^  workflow_dispatch:') 'Release workflow remains manually dispatchable'
Assert-Equal $false ($releaseWorkflow -match 'Verify main branch|refs/heads/main|Release can only run from main') 'Release workflow accepts a selected non-main branch'

$firstStable = Get-NextReleaseMetadata -Tags @() -Channel stable
Assert-Equal '0.1.0' $firstStable.Version 'First stable release version'
Assert-Equal 'v0.1.0' $firstStable.Tag 'First stable release tag'
Assert-Equal '' $firstStable.PreviousTag 'First stable release previous tag'
Assert-Equal 'win' $firstStable.VelopackChannel 'Stable Velopack channel'

$firstPre = Get-NextReleaseMetadata -Tags @() -Channel pre
Assert-Equal '0.1.0-pre' $firstPre.Version 'First pre-release version'
Assert-Equal 'v0.1.0-pre' $firstPre.Tag 'First pre-release tag'
Assert-Equal '' $firstPre.PreviousTag 'First pre-release previous tag'
Assert-Equal 'win-pre' $firstPre.VelopackChannel 'Pre-release Velopack channel'

$stableThenPre = Get-NextReleaseMetadata -Tags @('v0.1.465') -Channel pre
Assert-Equal '0.1.466-pre' $stableThenPre.Version 'Pre after stable'
Assert-Equal '' $stableThenPre.PreviousTag 'First pre-release has no pre-release base'

$repeatedPre = Get-NextReleaseMetadata -Tags @('v0.1.465', 'v0.1.466-pre') -Channel pre
Assert-Equal '0.1.467-pre' $repeatedPre.Version 'Second pre-release increments the shared sequence'
Assert-Equal 'v0.1.466-pre' $repeatedPre.PreviousTag 'Pre-release selects previous pre-release'

$stableAfterPre = Get-NextReleaseMetadata -Tags @('v0.1.465', 'v0.1.466-pre', 'v0.1.467-pre') -Channel stable
Assert-Equal '0.1.468' $stableAfterPre.Version 'Stable after pre-release'
Assert-Equal 'v0.1.465' $stableAfterPre.PreviousTag 'Stable selects previous stable release'

$prunedHistory = Get-NextReleaseMetadata -Tags @('v0.1.465', 'v0.1.467-pre') -Channel pre
Assert-Equal '0.1.468-pre' $prunedHistory.Version 'Retained newest pre-release preserves the shared sequence after pruning'
Assert-Equal 'v0.1.467-pre' $prunedHistory.PreviousTag 'Newest retained pre-release remains the delta base'

$numericOrdering = Get-NextReleaseMetadata -Tags @('v0.1.9', 'v0.1.99', 'v0.1.10000') -Channel stable
Assert-Equal '0.1.10001' $numericOrdering.Version 'Versions are ordered numerically'
Assert-Equal 'v0.1.10000' $numericOrdering.PreviousTag 'Numeric latest stable tag'

$invalidTagsIgnored = Get-NextReleaseMetadata -Tags @('not-a-version', 'v0.1', 'release-4', 'v0.1.7-preview', 'v0.1.8-pre.1', 'v0.1.7') -Channel stable
Assert-Equal '0.1.8' $invalidTagsIgnored.Version 'Malformed tags are ignored'
Assert-Equal 'v0.1.7' $invalidTagsIgnored.PreviousTag 'Stable tag parsing'

$largePatch = Get-NextReleaseMetadata -Tags @('v0.9.9999') -Channel stable
Assert-Equal '0.9.10000' $largePatch.Version 'Large patch increment'

$output = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    & (Join-Path (Split-Path -Parent $PSScriptRoot) 'release-metadata.ps1') -PublishedReleaseTags @('v0.1.465') -Channel pre -OutputFile $output | Out-Null
    $outputText = Get-Content -LiteralPath $output -Raw
    Assert-Equal $true ($outputText.Contains("VERSION=0.1.466-pre")) 'Workflow version output'
    Assert-Equal $true ($outputText.Contains("PREVIOUS_TAG=")) 'Workflow previous tag output'
    Assert-Equal $true ($outputText.Contains("VELOPACK_CHANNEL=win-pre")) 'Workflow feed output'
}
finally {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release metadata tests passed.'
