[CmdletBinding()]
param(
    [string[]]$PublishedReleaseTags = @(),

    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NextReleaseMetadata {
    param(
        [string[]]$Tags
    )

    $versions = foreach ($tag in $Tags) {
        if ($tag -match '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$') {
            [PSCustomObject]@{
                Tag = $tag
                Major = [int]$Matches.major
                Minor = [int]$Matches.minor
                Patch = [int]$Matches.patch
            }
        }
    }

    $previous = $versions | Sort-Object Major, Minor, Patch -Descending | Select-Object -First 1
    if ($null -eq $previous) {
        return [PSCustomObject]@{
            Version = '0.1.0'
            Tag = 'v0.1.0'
            PreviousTag = ''
        }
    }

    $nextVersion = '{0}.{1}.{2}' -f $previous.Major, $previous.Minor, ($previous.Patch + 1)
    return [PSCustomObject]@{
        Version = $nextVersion
        Tag = "v$nextVersion"
        PreviousTag = $previous.Tag
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}

$metadata = Get-NextReleaseMetadata -Tags $PublishedReleaseTags

if ($OutputFile) {
    @(
        "VERSION=$($metadata.Version)"
        "TAG=$($metadata.Tag)"
        "PREVIOUS_TAG=$($metadata.PreviousTag)"
    ) | Add-Content -LiteralPath $OutputFile
}

$metadata
