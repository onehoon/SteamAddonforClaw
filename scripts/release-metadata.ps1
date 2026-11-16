[CmdletBinding()]
param(
    [string[]]$PublishedReleaseTags = @(),

    [ValidateSet('stable', 'pre')]
    [string]$Channel = 'stable',

    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NextReleaseMetadata {
    param(
        [string[]]$Tags,

        [ValidateSet('stable', 'pre')]
        [string]$Channel = 'stable'
    )

    $versions = foreach ($tag in $Tags) {
        if ($tag -match '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>-pre)?$') {
            [PSCustomObject]@{
                Tag = $tag
                Major = [long]$Matches.major
                Minor = [long]$Matches.minor
                Patch = [long]$Matches.patch
                Channel = if ($Matches.ContainsKey('suffix') -and $Matches['suffix'] -eq '-pre') { 'pre' } else { 'stable' }
            }
        }
    }

    $latestVersion = $versions | Sort-Object Major, Minor, Patch -Descending | Select-Object -First 1
    $previousChannelRelease = $versions |
        Where-Object { $_.Channel -eq $Channel } |
        Sort-Object Major, Minor, Patch -Descending |
        Select-Object -First 1

    if ($null -eq $latestVersion) {
        $nextMajor = 0L
        $nextMinor = 1L
        $nextPatch = 0L
    }
    else {
        if ($latestVersion.Patch -eq [long]::MaxValue) {
            throw 'The latest release patch number cannot be incremented.'
        }

        $nextMajor = $latestVersion.Major
        $nextMinor = $latestVersion.Minor
        $nextPatch = $latestVersion.Patch + 1
    }

    $nextVersion = '{0}.{1}.{2}' -f $nextMajor, $nextMinor, $nextPatch
    if ($Channel -eq 'pre') {
        $nextVersion += '-pre'
    }

    return [PSCustomObject]@{
        Version = $nextVersion
        Tag = "v$nextVersion"
        PreviousTag = if ($null -eq $previousChannelRelease) { '' } else { $previousChannelRelease.Tag }
        VelopackChannel = if ($Channel -eq 'pre') { 'win-pre' } else { 'win' }
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}

$metadata = Get-NextReleaseMetadata -Tags $PublishedReleaseTags -Channel $Channel

if ($OutputFile) {
    @(
        "VERSION=$($metadata.Version)"
        "TAG=$($metadata.Tag)"
        "PREVIOUS_TAG=$($metadata.PreviousTag)"
        "VELOPACK_CHANNEL=$($metadata.VelopackChannel)"
    ) | Add-Content -LiteralPath $OutputFile
}

$metadata
