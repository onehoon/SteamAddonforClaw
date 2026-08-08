. (Join-Path (Split-Path -Parent $PSScriptRoot) 'prune-releases.ps1')

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message. Expected '$Expected', actual '$Actual'." }
}

$singleRelease = @([pscustomobject]@{ tag_name = 'v0.1.0'; created_at = '2026-08-01T00:00:00Z'; draft = $false; prerelease = $false })
Assert-Equal -Expected $null -Actual (Get-ReleaseToPrune -Releases $singleRelease) -Message 'A sole release must be retained'

$releases = @(
    [pscustomobject]@{ tag_name = 'v0.1.2'; created_at = '2026-08-03T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.0'; created_at = '2026-08-01T00:00:00Z'; draft = $false; prerelease = $false },
    [pscustomobject]@{ tag_name = 'v0.1.1-preview'; created_at = '2026-08-02T00:00:00Z'; draft = $false; prerelease = $true }
)
Assert-Equal -Expected 'v0.1.0' -Actual (Get-ReleaseToPrune -Releases $releases).tag_name -Message 'The oldest published release must be selected'
