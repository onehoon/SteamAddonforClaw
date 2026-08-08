$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'release-metadata.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        $Expected,

        [Parameter(Mandatory)]
        $Actual,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

$firstRelease = Get-NextReleaseMetadata -Tags @()
Assert-Equal -Expected '0.1.0' -Actual $firstRelease.Version -Message 'First release version'
Assert-Equal -Expected '' -Actual $firstRelease.PreviousTag -Message 'First release previous tag'

$afterFirstRelease = Get-NextReleaseMetadata -Tags @('v0.1.0')
Assert-Equal -Expected '0.1.1' -Actual $afterFirstRelease.Version -Message 'Patch increment'
Assert-Equal -Expected 'v0.1.0' -Actual $afterFirstRelease.PreviousTag -Message 'Previous tag'

$doubleDigitPatch = Get-NextReleaseMetadata -Tags @('v0.1.9')
Assert-Equal -Expected '0.1.10' -Actual $doubleDigitPatch.Version -Message 'Double-digit patch increment'

$largePatch = Get-NextReleaseMetadata -Tags @('v0.9.99')
Assert-Equal -Expected '0.9.100' -Actual $largePatch.Version -Message 'Large patch increment'

$invalidTagsIgnored = Get-NextReleaseMetadata -Tags @('not-a-version', 'v0.1', 'release-4', 'v0.1.7')
Assert-Equal -Expected '0.1.8' -Actual $invalidTagsIgnored.Version -Message 'Invalid tags ignored'
Assert-Equal -Expected 'v0.1.7' -Actual $invalidTagsIgnored.PreviousTag -Message 'v prefix handling'

Write-Host 'Release metadata tests passed.'
