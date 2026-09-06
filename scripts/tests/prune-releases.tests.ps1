. (Join-Path (Split-Path -Parent $PSScriptRoot) 'prune-releases.ps1')

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message. Expected '$Expected', actual '$Actual'." }
}

$singleRelease = @([pscustomobject]@{ tag_name = 'v0.1.0'; created_at = '2026-08-01T00:00:00Z'; draft = $false; prerelease = $false })
Assert-Equal -Expected 0 -Actual @(Get-ReleasesToPrune -Releases $singleRelease).Count -Message 'A sole release must be retained'

$releases = @(
    [pscustomobject]@{ tag_name = 'v0.1.6'; created_at = '2026-08-06T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.2'; created_at = '2026-08-02T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.5'; created_at = '2026-08-05T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.1-preview'; created_at = '2026-08-01T00:00:00Z'; draft = $false; prerelease = $true },
    [pscustomobject]@{ tag_name = 'v0.1.4'; created_at = '2026-08-04T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.1'; created_at = '2026-08-01T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.3'; created_at = '2026-08-03T00:00:00Z'; draft = $false; prerelease = $false }
)
$toPrune = @(Get-ReleasesToPrune -Releases $releases)
Assert-Equal -Expected 1 -Actual $toPrune.Count -Message 'Only releases older than the newest five must be selected'
Assert-Equal -Expected 'v0.1.1' -Actual $toPrune[0].tag_name -Message 'The oldest published release must be selected'
