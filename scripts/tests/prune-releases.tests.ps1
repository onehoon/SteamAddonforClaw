$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'prune-releases.ps1')

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function New-Release {
    param([string]$Tag, [string]$Date, [bool]$PreRelease = $false, [bool]$Draft = $false, [string]$PublishedAt = $Date)
    [pscustomobject]@{
        tag_name = $Tag
        created_at = $Date
        published_at = $PublishedAt
        draft = $Draft
        prerelease = $PreRelease
    }
}

$singleRelease = @(New-Release 'v0.1.0' '2026-08-01T00:00:00Z')
Assert-Equal 0 @(Get-ReleasesToPrune -Releases $singleRelease).Count 'A sole stable release is retained'

$atRetentionLimit = @(
    (New-Release 'v0.1.1' '2026-08-01T00:00:00Z'),
    (New-Release 'v0.1.2' '2026-08-02T00:00:00Z'),
    (New-Release 'v0.1.3' '2026-08-03T00:00:00Z'),
    (New-Release 'v0.1.4' '2026-08-04T00:00:00Z'),
    (New-Release 'v0.1.5' '2026-08-05T00:00:00Z'),
    (New-Release 'v0.1.6-pre' '2026-08-06T00:00:00Z' -PreRelease $true)
)
Assert-Equal 0 @(Get-ReleasesToPrune -Releases $atRetentionLimit).Count 'No release is deleted at the retention limits'

$releases = @(
    (New-Release 'v0.1.8-pre' '2026-08-12T00:00:00Z' -PreRelease $true),
    (New-Release 'v0.1.6' '2026-08-10T00:00:00Z'),
    (New-Release 'v0.1.4' '2026-08-08T00:00:00Z'),
    (New-Release 'v0.1.9' '2026-08-13T00:00:00Z' -Draft $true),
    (New-Release 'v0.1.2-pre' '2026-08-06T00:00:00Z' -PreRelease $true),
    (New-Release 'v0.1.7' '2026-08-11T00:00:00Z'),
    (New-Release 'v0.1.1' '2026-08-05T00:00:00Z'),
    (New-Release 'v0.1.5' '2026-08-09T00:00:00Z'),
    (New-Release 'v0.1.3' '2026-08-07T00:00:00Z'),
    (New-Release 'v0.1.1-pre' '2026-08-04T00:00:00Z' -PreRelease $true)
)

$toPrune = @(Get-ReleasesToPrune -Releases $releases)
Assert-Equal 3 $toPrune.Count 'Only stable releases beyond five and pre-releases beyond one are pruned'
Assert-Equal 'v0.1.1' $toPrune[0].tag_name 'Oldest stable release is pruned'
Assert-Equal 'v0.1.2-pre' $toPrune[1].tag_name 'Older pre-release is pruned'
Assert-Equal 'v0.1.1-pre' $toPrune[2].tag_name 'Oldest pre-release is pruned'
Assert-Equal $false (@($toPrune | Where-Object { $_.draft }).Count -gt 0) 'Draft releases are never pruned'

$outOfOrder = @(
    (New-Release 'v0.2.2' '2026-09-02T00:00:00Z'),
    (New-Release 'v0.2.1' '2026-09-01T00:00:00Z' -PublishedAt '2026-09-20T00:00:00Z'),
    (New-Release 'v0.2.3' '2026-09-03T00:00:00Z'),
    (New-Release 'v0.2.4' '2026-09-04T00:00:00Z'),
    (New-Release 'v0.2.5' '2026-09-05T00:00:00Z'),
    (New-Release 'v0.2.6' '2026-09-06T00:00:00Z')
)
$stableToPrune = @(Get-ReleasesToPrune -Releases $outOfOrder)
Assert-Equal 1 $stableToPrune.Count 'Out-of-order API results retain exactly five stable releases'
Assert-Equal 'v0.2.2' $stableToPrune[0].tag_name 'Stable pruning uses publication date rather than API order or creation date'

Write-Host 'Release pruning tests passed.'
