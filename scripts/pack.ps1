[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$PreserveReleases,

    [string]$ReleaseNotesPath,

    [string]$FseCertificatePath,

    [SecureString]$FseCertificatePassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $repositoryRoot 'src\SteamInputAddonforClaw\Assets\AppIcon.ico'
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'publish'
$releasesDirectory = Join-Path $artifactsDirectory 'Releases'

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (-not $PreserveReleases -and (Test-Path -LiteralPath $releasesDirectory)) {
    Remove-Item -LiteralPath $releasesDirectory -Recurse -Force
}

if (-not (Test-Path -LiteralPath $releasesDirectory)) {
    New-Item -ItemType Directory -Path $releasesDirectory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Application icon was not found: $iconPath"
}

& (Join-Path $PSScriptRoot 'publish-layout.ps1') `
    -Version $Version `
    -Configuration $Configuration `
    -PublishDirectory $publishDirectory

if ([string]::IsNullOrWhiteSpace($FseCertificatePath) -or $null -eq $FseCertificatePassword) {
    throw 'Release packaging requires the stable FSE signing certificate and password. No private signing material is stored in the repository.'
}

$fsePackageVersion = (($Version -split '-')[0] + '.0')
$fsePackagePath = Join-Path $publishDirectory 'fse\SteamInputAddonforClaw.FseHome.msix'
& (Join-Path $PSScriptRoot 'package-fse-home.ps1') `
    -PublishDirectory $publishDirectory `
    -OutputPath $fsePackagePath `
    -CertificatePath $FseCertificatePath `
    -CertificatePassword $FseCertificatePassword `
    -ExpectedVersion $fsePackageVersion
if ($LASTEXITCODE -ne 0) { throw "FSE Home package creation failed with exit code $LASTEXITCODE." }

& (Join-Path $PSScriptRoot 'verify-publish-assets.ps1') `
    -PublishDirectory $publishDirectory `
    -RequireFsePackage `
    -ExpectedFsePackageVersion $fsePackageVersion

& (Join-Path $PSScriptRoot 'report-publish-size.ps1') -PublishDirectory $publishDirectory

$vpkArguments = @(
    'vpk', '--version', '1.2.0', 'pack',
    '--packId', 'SteamInputAddonforClaw',
    '--packTitle', 'Steam Addon for Claw',
    '--packVersion', $Version,
    '--packDir', $publishDirectory,
    '--mainExe', 'SteamInputAddonforClaw.exe',
    '--icon', $iconPath,
    '--outputDir', $releasesDirectory,
    '--framework', 'net10.0-x64-runtime'
)

if ($ReleaseNotesPath) {
    $vpkArguments += @('--releaseNotes', $ReleaseNotesPath)
}

dnx @vpkArguments

if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed with exit code $LASTEXITCODE."
}
